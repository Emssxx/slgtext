using System.Collections.Generic;
using System.Text;
using UnityEngine;

// ============================================================================
// 安全感（NPC 自发行动的第一个动机）。纯代码计算，不走 LLM。
//   每个 NPC 有一个安全感数值 NpcData.security（0~100），会慢慢靠近"眼下环境给的安全感"：
//     · 营地围合度：从营地中心往四周看，有多少方向被围墙 / 建筑 / 地形（水、山壁、大块岩石树木）挡住；
//     · 营地被整个围起来（封闭空间：从营地里走不出去）→ 额外加一大截；
//     · 身边有同伴 → 加；异常度高 / 刚有危险的东西靠近过 / 人在营地外面 / 身体不好 → 减。
//   每个人的"底线"不一样：越勇敢底线越低（不怕空旷），越胆小底线越高。词条「勇敢」「怯懦」再各挪一截。
//   安全感低于底线 = 安全感不足 → NpcIntentBrain 让他自己决定怎么办（多半会去修围墙）。
// 由 NpcBehaviorDriver.Start 自动挂上。数值都在 Inspector 里可调。
// ============================================================================
public class NpcSecurity : MonoBehaviour
{
    public static NpcSecurity Instance { get; private set; }

    [Header("营地围合度扫描")]
    public float scanInterval = 2f;       // 多久重新看一次营地围起来了多少
    public float extraRadius = 2.5f;      // 从营地半径往外再看多远（世界单位）
    const int Rays = 32;                  // 从营地中心往外看多少个方向

    [Header("环境给的安全感")]
    public float baseValue = 40f;         // 什么都没有时
    public float coverageBonus = 40f;     // 四周全部挡住时加多少（按比例）
    public float enclosedBonus = 15f;     // 营地被整个围起来（封闭）时再加
    public float companionBonus = 5f;     // 身边每有一个同伴加多少
    public float companionMax = 15f;
    public float companionRange = 3f;     // 多近算"身边"
    public float anomalyWeight = 0.3f;    // 异常度每 1 点减多少
    public float outsidePenalty = 15f;    // 人在营地外面
    public float lowHealthPenalty = 10f;  // 生命低于一半
    public float nightPenalty = 10f;      // 夜里（以后有昼夜时生效）
    public float dangerDecay = 0.15f;     // "刚有危险"的余悸每秒消退多少
    [Tooltip("安全感每秒向环境值靠近的比例（越大变得越快）")]
    public float followSpeed = 0.05f;

    [Header("底线（低于它 = 安全感不足）")]
    public float baseNeed = 55f;          // 勇敢 50 的人
    public float braveryWeight = 0.5f;    // 勇敢每高 1 点，底线低多少
    public float traitShift = 10f;        // 词条「勇敢」-10，「怯懦」+10

    public float Coverage { get; private set; }   // 0~1：营地四周被挡住的比例
    public bool Enclosed { get; private set; }    // 营地是不是被整个围起来了

    readonly bool[] _rayBlocked = new bool[Rays];
    readonly Dictionary<string, bool> _nameBlocks = new Dictionary<string, bool>();
    readonly HashSet<Vector2Int> _blueprintCells = new HashSet<Vector2Int>();
    float _danger;
    float _scanT, _tickT;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _danger = Mathf.Max(0f, _danger - dangerDecay * dt);
        _scanT -= dt;
        if (_scanT <= 0f) { _scanT = scanInterval; Scan(); }
        _tickT -= dt;
        if (_tickT <= 0f) { UpdateNpcs(0.5f - _tickT); _tickT = 0.5f; }
    }

    // ============ 对外接口 ============

    // 发生了让人害怕的事（危险生物靠近、诡异事件……）：所有人的安全感都往下掉一截，之后慢慢恢复。
    public static void NotifyDanger(float amount)
    {
        if (Instance != null) Instance._danger = Mathf.Min(30f, Instance._danger + amount);
    }

    // 这个人的底线。
    public static float Need(NpcData n)
    {
        NpcSecurity s = Instance;
        if (n == null || s == null) return 50f;
        int bravery = n.personality != null ? n.personality.bravery : 50;
        float need = s.baseNeed - (bravery - 50) * s.braveryWeight;
        if (n.traits != null)
        {
            if (n.traits.Contains("勇敢")) need -= s.traitShift;
            if (n.traits.Contains("怯懦")) need += s.traitShift;
        }
        return Mathf.Clamp(need, 10f, 95f);
    }

    // 还差多少才安心（> 0 = 安全感不足）。
    public static float Lack(NpcData n) => n == null ? 0f : Need(n) - n.security;
    public static bool Insecure(NpcData n) => Instance != null && n != null && !n.IsDead && !n.isBroken && Lack(n) > 0f;

    // 某个方向（0° = 东，90° = 北）有没有被挡住。挑围墙位置时用：优先补空着的方向。
    public static bool BlockedAt(float deg)
    {
        if (Instance == null) return false;
        int i = Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 360f * Rays) % Rays;
        return Instance._rayBlocked[i];
    }

    // 四个方向各被挡住了多少（0~1）。side：north / east / south / west。
    public static float SideCover(string side)
    {
        if (Instance == null) return 0f;
        float center = side == "east" ? 0f : side == "north" ? 90f : side == "west" ? 180f : 270f;
        int hit = 0, total = 0;
        for (int i = 0; i < Rays; i++)
        {
            float deg = i * 360f / Rays;
            if (Mathf.Abs(Mathf.DeltaAngle(deg, center)) > 45f) continue;
            total++;
            if (Instance._rayBlocked[i]) hit++;
        }
        return total > 0 ? (float)hit / total : 0f;
    }

    // 给 AI 看的"此刻的感受"：安全感多少、底线多少、为什么不安。
    public static string Describe(NpcData n, bool jp)
    {
        NpcSecurity s = Instance;
        if (n == null || s == null) return "";
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        int sec = Mathf.RoundToInt(n.security), need = Mathf.RoundToInt(Need(n));
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(jp ? "【今の気持ち】" : "【你此刻的感受】");
        if (Lack(n) > 0f)
            sb.AppendLine(jp ? $"落ち着かない：安心感 {sec}（あなたは {need} 以上ないと安心できない）。" : $"你觉得不安全：安全感 {sec}，你要到 {need} 才安心。");
        else
            sb.AppendLine(jp ? $"今はそれなりに安心している（安心感 {sec}／{need}）。" : $"你现在还算安心（安全感 {sec}／{need}）。");

        List<string> why = new List<string>();
        int cover = Mathf.RoundToInt(s.Coverage * 100f);
        if (!s.Enclosed)
        {
            List<string> open = new List<string>();
            foreach (string side in new[] { "north", "east", "south", "west" })
                if (SideCover(side) < 0.3f) open.Add(SideName(side, jp));
            string openText = open.Count > 0 ? (jp ? "、" + string.Join("・", open) + "はがら空き" : "，" + string.Join("、", open) + "完全敞开") : "";
            why.Add(jp ? $"拠点のまわりを囲うものが少ない（囲まれている割合 {cover}%）{openText}" : $"营地四周没什么遮挡（围合 {cover}%）{openText}");
        }
        if (w != null && w.anomalyLevel >= 30f) why.Add(jp ? "異常度が高まっている" : "异常度在升高");
        if (s._danger >= 5f) why.Add(jp ? "さっき危険なものが近づいた" : "刚才有危险的东西靠近过");
        if (w != null && w.timeOfDay == "夜晚") why.Add(jp ? "夜だ" : "现在是夜里");
        if (s.CompanionsNear(n) == 0) why.Add(jp ? "そばに誰もいない" : "身边没有人");
        else if (s.CompanionValue(n) < 0f) why.Add(jp ? "そばにいるのは信用できない／嫌いな人だ" : "身边的是你讨厌或信不过的人");
        if (!s.InCamp(n)) why.Add(jp ? "拠点の外にいる" : "你在营地外面");
        if (n.health < 50f) why.Add(jp ? "体の調子が悪い" : "身体状况不好");
        if (why.Count > 0) sb.AppendLine((jp ? "理由：" : "原因：") + string.Join(jp ? "。" : "；", why) + (jp ? "。" : "。"));
        if (s.Enclosed) sb.AppendLine(jp ? "（拠点はもう壁で囲まれている。）" : "（营地已经被围起来了。）");
        else sb.AppendLine(jp ? "（壁や建物で拠点のまわりを塞げば安心できる。拠点をぐるりと囲めば、みんなかなり落ち着く。）"
                              : "（用围墙、建筑把营地四周挡起来会安心；把营地整个围起来，大家会安心很多。）");
        return sb.ToString();
    }

    static string SideName(string side, bool jp)
    {
        switch (side)
        {
            case "north": return jp ? "北側" : "北边";
            case "east": return jp ? "東側" : "东边";
            case "south": return jp ? "南側" : "南边";
            default: return jp ? "西側" : "西边";
        }
    }

    // ============ 每个人的安全感 ============

    void UpdateNpcs(float dt)
    {
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w == null) return;
        float k = Mathf.Clamp01(followSpeed * dt);
        foreach (NpcData n in w.npcs)
        {
            if (n == null || n.IsDead) continue;
            float target = Target(n, w);
            n.security = Mathf.Clamp(n.security + (target - n.security) * k, 0f, 100f);
        }
    }

    // 眼下环境给这个人的安全感。
    float Target(NpcData n, WorldState w)
    {
        bool inCamp = InCamp(n);
        float t = baseValue;
        if (inCamp)
        {
            t += coverageBonus * Coverage;
            if (Enclosed) t += enclosedBonus;
        }
        else t -= outsidePenalty;
        t += CompanionValue(n);
        t -= w.anomalyLevel * anomalyWeight;
        t -= _danger;
        if (w.timeOfDay == "夜晚") t -= nightPenalty;
        if (n.health < 50f) t -= lowHealthPenalty;
        return Mathf.Clamp(t, 0f, 100f);
    }

    bool InCamp(NpcData n)
    {
        NpcMover m = NpcView.GetMover(n.id);
        if (m == null) return true;
        Vector3 camp = NpcNav.CampCenter(m.transform.position);
        return Vector2.Distance(m.transform.position, camp) <= NpcNav.CampRadius + 0.5f;
    }

    // 身边的人给的安全感：喜欢的人在旁边更安心；讨厌 / 怀疑的人在旁边反而不安（看关系）。
    float CompanionValue(NpcData n)
    {
        NpcMover m = NpcView.GetMover(n.id);
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (m == null || w == null) return 0f;
        float sum = 0f;
        foreach (NpcData o in w.npcs)
        {
            if (o == null || o == n || o.IsDead) continue;
            NpcMover om = NpcView.GetMover(o.id);
            if (om == null || Vector2.Distance(m.transform.position, om.transform.position) > companionRange) continue;
            float aff = RelationshipManager.AffinityOf(n, o.id), trust = RelationshipManager.TrustOf(n, o.id);
            if (aff <= -35f || trust <= 25f) sum -= 3f;
            else if (aff >= 30f) sum += companionBonus * 1.4f;
            else sum += companionBonus;
        }
        return Mathf.Clamp(sum, -10f, companionMax);
    }

    int CompanionsNear(NpcData n)
    {
        NpcMover m = NpcView.GetMover(n.id);
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (m == null || w == null) return 0;
        int count = 0;
        foreach (NpcData o in w.npcs)
        {
            if (o == null || o == n || o.IsDead) continue;
            NpcMover om = NpcView.GetMover(o.id);
            if (om != null && Vector2.Distance(m.transform.position, om.transform.position) <= companionRange) count++;
        }
        return count;
    }

    // ============ 营地围合度 ============

    // 从营地中心往 32 个方向看，统计被挡住的比例；再从营地中心往外"灌水"，出不去 = 封闭。
    void Scan()
    {
        GridManager g = GridManager.Instance;
        if (g == null) { Coverage = 0f; Enclosed = false; return; }
        PrepareBlockers();

        Vector3 camp = NpcNav.CampCenter(Vector3.zero);
        float r0 = NpcNav.CampRadius;
        float rMax = r0 + extraRadius;
        float step = Mathf.Max(0.05f, g.cellSize * 0.5f);

        int hit = 0;
        for (int i = 0; i < Rays; i++)
        {
            float rad = i * Mathf.PI * 2f / Rays;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            bool blocked = false;
            for (float r = r0 * 0.6f; r <= rMax && !blocked; r += step)
            {
                Vector2Int c = g.WorldToCell(camp + (Vector3)(dir * r));
                if (Blocked(g, c.x, c.y)) blocked = true;
            }
            _rayBlocked[i] = blocked;
            if (blocked) hit++;
        }
        Coverage = (float)hit / Rays;
        Enclosed = hit == Rays && FloodClosed(g, camp, rMax + 1f);
    }

    // 从营地中心（找最近的空格）往外灌水（上下左右四个方向）：走到 limit 之外或地图外 = 漏了，不封闭。
    bool FloodClosed(GridManager g, Vector3 camp, float limit)
    {
        Vector2Int center = g.WorldToCell(camp);
        Vector2Int start = center;
        bool found = false;
        for (int r = 0; r <= 4 && !found; r++)
            for (int dx = -r; dx <= r && !found; dx++)
                for (int dy = -r; dy <= r && !found; dy++)
                {
                    Vector2Int c = new Vector2Int(center.x + dx, center.y + dy);
                    if (g.InBounds(c.x, c.y) && !Blocked(g, c.x, c.y)) { start = c; found = true; }
                }
        if (!found) return false;

        int limitCells = Mathf.CeilToInt(limit / Mathf.Max(0.01f, g.cellSize));
        HashSet<Vector2Int> seen = new HashSet<Vector2Int> { start };
        Queue<Vector2Int> q = new Queue<Vector2Int>();
        q.Enqueue(start);
        Vector2Int[] nb = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue();
            if (Mathf.Abs(c.x - center.x) > limitCells || Mathf.Abs(c.y - center.y) > limitCells) return false;
            if (seen.Count > 30000) return false;
            foreach (Vector2Int d in nb)
            {
                Vector2Int n = c + d;
                if (seen.Contains(n)) continue;
                if (!g.InBounds(n.x, n.y)) return false; // 走到地图外面去了
                if (Blocked(g, n.x, n.y)) continue;
                seen.Add(n);
                q.Enqueue(n);
            }
        }
        return true;
    }

    // 这一格挡不挡路：地形（山壁、水面、大块岩石 / 树）+ 建好的挡路建筑（围墙、设施……；蓝图不算）。
    bool Blocked(GridManager g, int x, int y)
    {
        if (!g.InBounds(x, y)) return true;
        Vector2Int c = new Vector2Int(x, y);
        if (TerrainSystem.IsWall(c) || TerrainSystem.TypeAt(c) == TerrainType.Water) return true;
        ResourceNode r = TerrainSystem.ResourceAt(c);
        if (r != null && ResourceInfo.Blocks(r.kind, r.size)) return true;
        string occ = g.GetOccupied(x, y);
        if (string.IsNullOrEmpty(occ) || _blueprintCells.Contains(c)) return false;
        return _nameBlocks.TryGetValue(occ, out bool b) && b;
    }

    // 每次扫描前：哪些建筑挡路（按显示名）、哪些格子还只是蓝图。
    void PrepareBlockers()
    {
        _nameBlocks.Clear();
        BuildManager bm = BuildManager.Instance;
        if (bm != null && bm.buildables != null)
            foreach (BuildableData d in bm.buildables)
                if (d != null && !string.IsNullOrEmpty(d.displayName)) _nameBlocks[d.displayName] = d.blocksMovement;
        _blueprintCells.Clear();
        foreach (ConstructionSite site in ConstructionSite.All)
        {
            if (site == null) continue;
            for (int x = site.origin.x; x < site.origin.x + site.size.x; x++)
                for (int y = site.origin.y; y < site.origin.y + site.size.y; y++)
                    _blueprintCells.Add(new Vector2Int(x, y));
        }
    }
}
