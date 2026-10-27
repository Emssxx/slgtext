using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 需求行为的"实体执行器"（纯规则，不走 LLM）。
// NpcDecisionRules 决定"该吃饭/休息/搜寻/逃跑"后，由它让 NPC 真的走过去做，做完再结算数值
// （结算仍然是 NpcDecisionRules.Execute，数值和日志都没变，只是从"瞬间完成"变成"走过去做完才算"）。
//   吃饭：走到营地（仓库）→ 吃 → 结算。仓库空了 20 秒内不再白跑。
//   休息：原地坐下，每秒回 0.3 体力（消耗饥饿值，NpcHealth 结算），歇到「疲惫」消失或饿得歇不下去为止。
//   搜寻：走到营地外（优先迷雾里看不清的地方，顺便揭开迷雾）→ 翻找 → 带回营地 → 结算。
//   逃跑：慌张地跑回营地中心 → 缩一会儿 → 结算。30 秒内不重复逃。
// 执行期间在 NpcBusy 登记占用（Need，优先级 50）：闲置行为会让路；
// 玩家指令（100）和施工（80）更优先，它们一接管，这里立刻放手（不结算，下次再来）。
// 由 NpcBehaviorDriver 自动挂到每个 NPC 身上，不用改预制体。
[RequireComponent(typeof(NpcView))]
public class NpcNeedRunner : MonoBehaviour
{
    public enum StartResult { Started, Cooling, Unavailable }

    [Header("各动作持续时间（秒）")]
    public float eatSeconds = 2.5f;
    public float restSeconds = 8f;      // 休息最少坐多久
    public float restMaxSeconds = 150f; // 休息最多坐多久（防止一直坐着）
    public float searchSeconds = 4f;
    public float cowerSeconds = 4f;

    public bool IsRunning { get; private set; }
    public NpcDecisionRules.Behavior Current { get; private set; }

    NpcView _view;
    NpcMover _mover;
    NpcTaskRunner _runner;
    NavMeshAgent _agent;
    string _claimedId;     // 登记占用时用的 id（物体销毁后也能正确解除）
    Vector3 _spawnPos;     // 找不到营地中心时的兜底
    readonly Dictionary<NpcDecisionRules.Behavior, float> _cooldownUntil = new Dictionary<NpcDecisionRules.Behavior, float>();

    public string NpcId => _view != null ? _view.npcId : null;

    NpcData Data
    {
        get
        {
            if (WorldManager.Instance == null || string.IsNullOrEmpty(NpcId)) return null;
            return WorldManager.Instance.GetNpcById(NpcId);
        }
    }

    bool AgentReady => _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh;

    // 有没有更优先的系统（玩家指令 / 施工）接管了这个 NPC。
    bool Overridden => (_runner != null && _runner.IsBusy) || NpcBusy.HasHigherThan(_claimedId, NpcBusy.Need);

    void Awake()
    {
        _view = GetComponent<NpcView>();
        _mover = GetComponent<NpcMover>();
        _runner = GetComponent<NpcTaskRunner>();
        _agent = GetComponent<NavMeshAgent>();
        _spawnPos = transform.position;
    }

    void OnDisable()
    {
        if (IsRunning) Abort(false);
    }

    void Update()
    {
        if (!IsRunning) return;
        NpcData npc = Data;
        // 人没了/崩解了，或被玩家指令、施工接管 → 立刻放手（不动移动，交给接管者）
        if (npc == null || npc.IsDead || npc.isBroken || Overridden) Abort(false);
    }

    // ============ 对外接口 ============

    // 尝试开始一个需求行为。
    //   Started     = 开始做了（结算会在做完时进行）
    //   Cooling     = 这个需求刚失败过/刚做过，冷却中（这次什么都不做）
    //   Unavailable = 现在没法实体执行（在执行指令/施工、不在导航网格上…），调用方应直接结算数值
    public StartResult TryStart(NpcData npc, NpcDecisionRules.Behavior need, WorldState world)
    {
        if (IsRunning) return StartResult.Started;
        if (_cooldownUntil.TryGetValue(need, out float until) && Time.time < until) return StartResult.Cooling;
        if (need != NpcDecisionRules.Behavior.Eat && need != NpcDecisionRules.Behavior.Rest
            && need != NpcDecisionRules.Behavior.Gather && need != NpcDecisionRules.Behavior.Flee)
            return StartResult.Unavailable;
        if (npc == null || string.IsNullOrEmpty(NpcId) || _mover == null || !AgentReady) return StartResult.Unavailable;
        if (_runner != null && _runner.IsBusy) return StartResult.Unavailable;
        if (!NpcBusy.CanClaim(NpcId, NpcBusy.Need)) return StartResult.Unavailable;

        _claimedId = NpcId;
        NpcBusy.Claim(_claimedId, NpcBusy.Need);
        IsRunning = true;
        Current = need;
        StartCoroutine(Run(npc, need, world));
        return StartResult.Started;
    }

    // 中止某个 NPC 正在做的需求行为（例如被空间扭曲卷走时）。
    public static void CancelFor(string npcId)
    {
        NpcMover m = NpcView.GetMover(npcId);
        NpcNeedRunner r = m != null ? m.GetComponent<NpcNeedRunner>() : null;
        if (r != null && r.IsRunning) r.Abort(true);
    }

    // 立刻放手。stopMover：是否顺便停下脚步（被指令/施工接管时不能停，否则会取消它们的移动）。
    public void Abort(bool stopMover)
    {
        StopAllCoroutines();
        IsRunning = false;
        NpcHealth.SetResting(NpcId, false);
        ReleaseClaim();
        if (_mover != null)
        {
            _mover.SetSpeedMultiplier(1f);
            if (stopMover && AgentReady) _mover.Stop();
        }
        NpcSpeechBubble.Hide(transform);
    }

    // ============ 各需求的执行流程 ============

    IEnumerator Run(NpcData npc, NpcDecisionRules.Behavior need, WorldState world)
    {
        switch (need)
        {
            case NpcDecisionRules.Behavior.Eat:    yield return DoEat(npc, world); break;
            case NpcDecisionRules.Behavior.Rest:   yield return DoRest(npc, world); break;
            case NpcDecisionRules.Behavior.Gather: yield return DoGather(npc, world); break;
            case NpcDecisionRules.Behavior.Flee:   yield return DoFlee(npc, world); break;
        }
        Finish();
    }

    // 吃饭：走到营地（仓库在那）→ 吃 → 结算。
    IEnumerator DoEat(NpcData npc, WorldState world)
    {
        SetAction("need_act_eat");
        yield return WalkTo(NearCamp(1.2f), 1f, 12f);
        StopMoving();
        Say(LanguageGuard.Pick("（找点吃的）", "（何か食べる物を探す）"), eatSeconds);
        yield return new WaitForSeconds(eatSeconds);

        bool hadFood = HasFood(world);
        NpcDecisionRules.Execute(npc, NpcDecisionRules.Behavior.Eat, world); // 吃到/没吃到的日志、记忆都在这里
        if (!hadFood)
        {
            Say(LanguageGuard.Pick("……什么都没剩了。", "……何も残ってない。"), 2.5f);
            _cooldownUntil[NpcDecisionRules.Behavior.Eat] = Time.time + 20f; // 仓库空了：20 秒内别再白跑
        }
    }

    // 休息：原地坐下 → 每秒回体力（NpcHealth）→ 歇到疲惫消失（或饿得没法再歇）→ 记日志。
    IEnumerator DoRest(NpcData npc, WorldState world)
    {
        SetAction("need_act_rest");
        StopMoving();
        Say(LanguageGuard.Pick("（坐下歇口气）", "（腰を下ろして一息つく）"), 2.5f);
        NpcDecisionRules.Execute(npc, NpcDecisionRules.Behavior.Rest, world); // 日志
        float target = NpcHealth.Instance != null ? NpcHealth.Instance.fatigueClearAt : 60f;
        NpcHealth.SetResting(NpcId, true);
        float t = 0f;
        while (t < restMaxSeconds && (t < restSeconds || npc.energy < target) && npc.satiety > 0f && !npc.IsDead)
        {
            t += Time.deltaTime;
            yield return null;
        }
        NpcHealth.SetResting(NpcId, false);
    }

    // 搜寻：走到营地外（优先迷雾里看不清的地方）→ 翻找 → 带回营地 → 结算（仓库 +1、体力 -15）。
    IEnumerator DoGather(NpcData npc, WorldState world)
    {
        SetAction("need_act_gather");
        Vector3 camp = Camp();
        float r = NpcNav.CampRadius;
        if (NpcNav.TryRandomPoint(transform.position, camp, r + 1f, r + 6f, true, out Vector3 field))
        {
            yield return WalkTo(field, 1f, 20f);
            StopMoving();
            Say(LanguageGuard.Pick("（四处翻找）", "（あたりを探っている）"), searchSeconds);
            yield return LookAround(searchSeconds);
            yield return WalkTo(NearCamp(1.2f), 1f, 20f);
            StopMoving();
        }
        // 找不到能去的地方：就地结算（和以前一样），不让需求卡住。
        NpcDecisionRules.Execute(npc, NpcDecisionRules.Behavior.Gather, world);
        Say(LanguageGuard.Pick("找到了点能用的。", "使えそうな物を見つけた。"), 2.5f);
    }

    // 逃跑：慌张地跑回营地中心 → 缩一会儿 → 结算（日志）。
    IEnumerator DoFlee(NpcData npc, WorldState world)
    {
        SetAction("need_act_flee");
        Say(LanguageGuard.Pick("……救、救命！", "……た、助けて！"), 2f);
        yield return WalkTo(NearCamp(0.8f), 1.3f, 12f);
        StopMoving();
        yield return new WaitForSeconds(cowerSeconds);
        NpcDecisionRules.Execute(npc, NpcDecisionRules.Behavior.Flee, world);
        _cooldownUntil[NpcDecisionRules.Behavior.Flee] = Time.time + 30f; // 别每个决策周期都逃一次
    }

    // ============ 小工具 ============

    Vector3 Camp() => NpcNav.CampCenter(_spawnPos);

    // 营地中心附近的一个空位（不和别人挤在同一点）。
    Vector3 NearCamp(float radius)
    {
        Vector3 c = Camp();
        if (NpcNav.TryRandomPoint(transform.position, c, 0f, radius, false, out Vector3 p)) return p;
        return c;
    }

    // 走到某处，到了或超时就继续。走之前先确认没被更优先的系统接管（避免和它们抢移动）。
    IEnumerator WalkTo(Vector3 target, float speedMul, float timeout)
    {
        if (!AgentReady || Overridden) yield break;
        _mover.SetSpeedMultiplier(speedMul);
        _mover.MoveTo(target);
        yield return null; // 等一帧让寻路生效
        float t = 0f;
        while (_mover.IsMoving && t < timeout)
        {
            _mover.SetSpeedMultiplier(speedMul); // 防止被别的系统改回去
            t += Time.deltaTime;
            yield return null;
        }
        _mover.SetSpeedMultiplier(1f);
    }

    void StopMoving()
    {
        if (_mover == null) return;
        _mover.SetSpeedMultiplier(1f);
        if (AgentReady) _mover.Stop();
    }

    // 原地左右张望 seconds 秒（翻找时用）。
    IEnumerator LookAround(float seconds)
    {
        float t = 0f, next = 0.8f;
        while (t < seconds)
        {
            if (t >= next)
            {
                if (_mover != null) _mover.FaceTowards(transform.position + (Random.value < 0.5f ? Vector3.left : Vector3.right));
                next = t + Random.Range(0.8f, 1.6f);
            }
            t += Time.deltaTime;
            yield return null;
        }
    }

    void Say(string text, float seconds)
    {
        NpcSpeechBubble.Show(transform, text, seconds);
    }

    void SetAction(string key)
    {
        NpcData npc = Data;
        if (npc == null) return;
        LocalizationManager loc = LocalizationManager.Instance;
        npc.currentAction = loc != null ? loc.Get(key) : key;
    }

    static bool HasFood(WorldState world)
    {
        if (ItemDatabase.Instance == null) return true;
        return ItemDatabase.Instance.CountInStorage(ItemCategory.Food, world) > 0;
    }

    void Finish()
    {
        IsRunning = false;
        NpcHealth.SetResting(NpcId, false);
        ReleaseClaim();
        if (_mover != null) _mover.SetSpeedMultiplier(1f);
    }

    void ReleaseClaim()
    {
        if (string.IsNullOrEmpty(_claimedId)) return;
        NpcBusy.Release(_claimedId, NpcBusy.Need);
        _claimedId = null;
    }
}
