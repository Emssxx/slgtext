using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;

// 闲置行为管理器（纯规则，不走 LLM）。
// NPC 没有玩家指令、没在施工、需求也不紧急时，不再傻站着，而是自己找事做：
//   发呆（原地待机、左右张望）/ 在营地里闲逛 / 找另一个 NPC 闲聊（头顶冒对话气泡）。
//
// 【结构】
//   NpcIdleManager（本脚本，场景里一个）：全局参数 + 活动列表 + 公共工具（营地中心、随机落点、找聊天对象）。
//   NpcIdleBrain（每个 NPC 一个，本脚本自动挂上）：判断能不能闲置、挑活动、被打断/恢复。
//   IdleActivity（每种活动一个类，见 IdleActivities.cs）：自己给自己打分（Score）+ 自己的执行协程（Run）。
//   IdleChat / IdleChatRules（见 IdleChat.cs）：闲聊的话题选择、结算规则、台词来源接口。
//
// 【优先级】玩家指令 > 施工等系统占用（NpcBusy）> 打断暂停 > 闲置活动。闲置永远让路，不抢移动控制权。
//
// 【扩展】加新闲置活动 = 写一个继承 IdleActivity 的类，然后在 Awake 的 activities 列表里加一行
//   （或运行时调用 NpcIdleManager.Instance.Register(new 你的活动())）。不用改 Brain。
//
// 【挂载】场景里可以手动放一个（方便在 Inspector 调参数）；不放的话 NpcBehaviorDriver 会自动挂一个。
public class NpcIdleManager : MonoBehaviour
{
    public static NpcIdleManager Instance { get; private set; }

    [Header("总开关")]
    public bool idleEnabled = true;

    [Header("营地范围（不填中心：终端机 → NPC 生成点 → NPC 出生位置）")]
    public Transform campCenter;
    public float campRadius = 4.5f;

    [Header("各活动的权重倍率（调手感用，0 = 关掉这个活动）")]
    [Range(0f, 3f)] public float standWeight = 1f;
    [Range(0f, 3f)] public float wanderWeight = 1.1f;
    [Range(0f, 3f)] public float chatWeight = 1f;

    [Header("发呆")]
    public Vector2 standSeconds = new Vector2(3f, 7f);    // 一次发呆持续多久（秒，随机）

    [Header("闲逛")]
    public float wanderSpeed = 0.55f;                      // 闲逛速度倍率（相对正常走路）
    public Vector2 wanderStep = new Vector2(1.2f, 3.5f);   // 一次走多远（世界单位，随机）

    [Header("闲聊")]
    public float chatSearchRadius = 7f;                    // 多远以内的同伴可以去找他聊天
    public float talkDistance = 0.9f;                      // 聊天时两人站多远
    public Vector2 chatCooldown = new Vector2(25f, 45f);   // 一个人聊完后多久才会再聊（秒，随机；外向的人更短）
    public float pairCooldown = 50f;                       // 同一对人聊完后多久才会再聊
    public bool logChatLines = false;                      // 是否把每句台词也写进行为日志（默认只写一条概要）

    [Header("节奏")]
    public Vector2 gapSeconds = new Vector2(0.5f, 1.5f);   // 两个活动之间的停顿
    public float holdAfterTaskSeconds = 60f;               // 玩家指令做完后，在指令终点附近待多久再回营地
    public float holdRadius = 1.5f;                        // 待在指令终点时的活动半径

    [Header("对话气泡")]
    public TMP_FontAsset bubbleFont;                       // 不拖会自动借用 NPC 列表/终端/仓库面板的中日文字体
    public float bubbleFontSize = 2.4f;
    public float bubbleMaxWidth = 3.2f;                    // 气泡最大宽度（世界单位），超出自动换行
    public float bubbleOffsetY = 0.55f;                    // 气泡底部在 NPC 中心上方多高
    public int bubbleSortingOrder = 500;                   // 画在 NPC(1)、迷雾(100) 之上

    // —— 活动列表（按分数加权随机挑）——
    public readonly List<IdleActivity> activities = new List<IdleActivity>();

    // 被别人拉去聊天时执行的"陪聊"活动（不在上面的列表里，不会被随机选中，只由 Brain 直接启动）。
    public readonly IdleActivity partnerActivity = new ChatPartnerActivity();

    readonly Dictionary<string, float> _pairCooldownUntil = new Dictionary<string, float>();
    readonly List<KeyValuePair<IdleActivity, float>> _scored = new List<KeyValuePair<IdleActivity, float>>();
    readonly List<KeyValuePair<NpcIdleBrain, float>> _partners = new List<KeyValuePair<NpcIdleBrain, float>>();
    NavMeshPath _path;
    NpcSpawner _spawner;
    bool _spawnerSearched;
    bool _fontSearched;
    float _scanTimer;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        NpcBusy.ClearAll(); // 新的一局：清掉上一局残留的占用登记

        activities.Add(new StandIdleActivity());  // 发呆
        activities.Add(new WanderActivity());     // 闲逛
        activities.Add(new ChatActivity());       // 找人闲聊
        // 以后的新活动加在这里，例如：activities.Add(new WarmByFireActivity());
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        // 每秒扫一次：给新生成的 NPC 自动挂上 NpcIdleBrain（不用改预制体）。
        _scanTimer -= Time.unscaledDeltaTime;
        if (_scanTimer > 0f) return;
        _scanTimer = 1f;
        foreach (NpcView view in NpcView.GetAllViews())
        {
            if (view == null || view.GetComponent<NpcMover>() == null) continue;
            if (view.GetComponent<NpcIdleBrain>() == null)
                view.gameObject.AddComponent<NpcIdleBrain>();
        }
    }

    // 注册一个新的闲置活动（同 Id 的不会重复加）。
    public void Register(IdleActivity activity)
    {
        if (activity == null) return;
        foreach (IdleActivity a in activities)
            if (a.Id == activity.Id) return;
        activities.Add(activity);
    }

    // —— 营地中心：手动指定 → 终端机（营地核心）→ NPC 生成点 → 传入的兜底位置 ——
    public Vector3 GetCampCenter(Vector3 fallback)
    {
        if (campCenter != null) return campCenter.position;
        if (TerminalController.Instance != null) return TerminalController.Instance.transform.position;
        if (!_spawnerSearched)
        {
            _spawnerSearched = true;
            _spawner = FindFirstObjectByType<NpcSpawner>();
        }
        if (_spawner != null)
            return _spawner.spawnCenter != null ? _spawner.spawnCenter.position : _spawner.transform.position;
        return fallback;
    }

    // —— 加权随机挑一个活动（分数越高越容易被选中，但不是每次都选最高的，这样才有变化）——
    public IdleActivity PickActivity(NpcIdleBrain brain, IdleContext ctx)
    {
        _scored.Clear();
        float total = 0f;
        foreach (IdleActivity a in activities)
        {
            if (a == null || brain.IsOnCooldown(a.Id)) continue;
            float s = a.Score(brain, ctx);
            if (s <= 0f) continue;
            if (a.Id == ctx.lastActivityId) s *= 0.6f; // 不爱连着做同一件事
            _scored.Add(new KeyValuePair<IdleActivity, float>(a, s));
            total += s;
        }
        if (total <= 0f) return null;

        float r = Random.value * total;
        foreach (KeyValuePair<IdleActivity, float> kv in _scored)
        {
            r -= kv.Value;
            if (r <= 0f) return kv.Key;
        }
        return _scored[_scored.Count - 1].Key;
    }

    // —— 闲逛落点：在活动范围内、在导航网格上、走得到、不和别人挤在一起 ——
    public bool TryGetWanderPoint(NpcIdleBrain brain, IdleContext ctx, out Vector3 point)
    {
        Vector3 from = brain.transform.position;
        point = from;
        if (!NavMesh.SamplePosition(from, out NavMeshHit startHit, 1f, NavMesh.AllAreas)) return false;

        float radius = Mathf.Max(0.5f, ctx.anchorRadius);
        bool outside = (from - ctx.anchor).sqrMagnitude > radius * radius; // 在范围外：不限步长，先往回走

        for (int i = 0; i < 14; i++)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 c = new Vector3(ctx.anchor.x + r.x, ctx.anchor.y + r.y, from.z);
            float d = Vector2.Distance(from, c);
            if (!outside && (d < wanderStep.x || d > wanderStep.y)) continue;
            if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 0.8f, NavMesh.AllAreas)) continue;
            Vector3 p = new Vector3(hit.position.x, hit.position.y, from.z);
            if (!InGrid(p) || IsCrowded(p, brain, 0.7f)) continue;
            if (!IsReachable(startHit.position, hit.position)) continue;
            point = p;
            return true;
        }
        return false;
    }

    bool InGrid(Vector3 p)
    {
        GridManager g = GridManager.Instance;
        if (g == null) return true;
        Vector2Int c = g.WorldToCell(p);
        return g.InBounds(c.x, c.y);
    }

    bool IsCrowded(Vector3 p, NpcIdleBrain self, float minDist)
    {
        foreach (NpcIdleBrain b in NpcIdleBrain.All)
        {
            if (b == null || b == self) continue;
            if ((b.transform.position - p).sqrMagnitude < minDist * minDist) return true;
        }
        return false;
    }

    bool IsReachable(Vector3 from, Vector3 to)
    {
        if (_path == null) _path = new NavMeshPath();
        return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _path)
               && _path.status == NavMeshPathStatus.PathComplete;
    }

    // —— 找聊天对象（加权随机：关系好、外向、离得近的更容易被选中；关系差的也可能碰上，会吵起来）——
    public NpcIdleBrain FindChatPartner(NpcIdleBrain self, IdleContext ctx)
    {
        NpcData me = self.Data;
        if (me == null) return null;
        float maxDist = ctx.holding ? holdRadius + 2f : chatSearchRadius;

        _partners.Clear();
        float total = 0f;
        foreach (NpcIdleBrain b in NpcIdleBrain.All)
        {
            if (b == null || b == self || !b.IsAvailableForChat()) continue;
            float d = Vector2.Distance(self.transform.position, b.transform.position);
            if (d > maxDist) continue;
            if (IsPairOnCooldown(self.NpcId, b.NpcId)) continue;
            NpcData other = b.Data;
            if (other == null) continue;

            Relationship rel = RelationshipManager.Get(me, other.id);
            float aff = rel != null ? rel.affinity : 0f;
            float w = Mathf.Clamp(1f + aff / 40f, 0.25f, 3f);              // 关系越好越想聊
            w *= 0.6f + other.personality.sociability / 100f * 0.8f;       // 对方外向更好搭话
            w *= Mathf.Lerp(1.2f, 0.7f, d / Mathf.Max(0.01f, maxDist));   // 近的优先
            _partners.Add(new KeyValuePair<NpcIdleBrain, float>(b, w));
            total += w;
        }
        if (total <= 0f) return null;

        float r = Random.value * total;
        foreach (KeyValuePair<NpcIdleBrain, float> kv in _partners)
        {
            r -= kv.Value;
            if (r <= 0f) return kv.Key;
        }
        return _partners[_partners.Count - 1].Key;
    }

    // 聊天站位：2D 侧视角色只会左右翻转，所以站到对方左边或右边（离自己近的那边），看起来才像面对面。
    public Vector3 GetMeetingPoint(Vector3 partnerPos, Vector3 selfPos)
    {
        float side = selfPos.x >= partnerPos.x ? 1f : -1f;
        for (int k = 0; k < 2; k++)
        {
            Vector3 c = partnerPos + new Vector3(side * talkDistance, 0f, 0f);
            if (NavMesh.SamplePosition(c, out NavMeshHit hit, 0.35f, NavMesh.AllAreas))
                return new Vector3(hit.position.x, hit.position.y, selfPos.z);
            side = -side; // 这边站不了（墙/地图边），换另一边
        }
        Vector3 dir = selfPos - partnerPos;
        dir.z = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;
        return partnerPos + dir.normalized * talkDistance;
    }

    // —— 成对的聊天冷却 ——
    static string PairKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;

    public bool IsPairOnCooldown(string a, string b)
    {
        return _pairCooldownUntil.TryGetValue(PairKey(a, b), out float until) && Time.time < until;
    }

    public void SetPairCooldown(string a, string b, float seconds)
    {
        _pairCooldownUntil[PairKey(a, b)] = Time.time + seconds;
    }

    // —— 气泡字体：没拖就借用场景里其他面板的中日文字体 ——
    public TMP_FontAsset BubbleFont
    {
        get
        {
            if (bubbleFont == null && !_fontSearched)
            {
                _fontSearched = true;
                NpcListPanel list = FindFirstObjectByType<NpcListPanel>(FindObjectsInactive.Include);
                if (list != null) bubbleFont = list.font;
                if (bubbleFont == null)
                {
                    TerminalPanelUI term = FindFirstObjectByType<TerminalPanelUI>(FindObjectsInactive.Include);
                    if (term != null) bubbleFont = term.font;
                }
                if (bubbleFont == null)
                {
                    StoragePanelUI storage = FindFirstObjectByType<StoragePanelUI>(FindObjectsInactive.Include);
                    if (storage != null) bubbleFont = storage.font;
                }
                if (bubbleFont == null)
                {
                    StatusPanelManager status = FindFirstObjectByType<StatusPanelManager>(FindObjectsInactive.Include);
                    if (status != null) bubbleFont = status.font;
                }
                if (bubbleFont == null)
                    Debug.LogWarning("[NpcIdleManager] 没找到中日文字体，对话气泡里的字可能显示成方块。请把 NPC 列表用的 TMP 字体拖到 NpcIdleManager.bubbleFont。");
            }
            return bubbleFont;
        }
    }
}
