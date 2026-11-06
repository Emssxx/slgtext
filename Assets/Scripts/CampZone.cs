using System.Collections.Generic;
using System.Text;
using UnityEngine;

// ============================================================================
// 营地范围（玩家看不见，只给 NPC 和代码用）。
//   · 以营地中心（终端机 / 出生点）为中心的一块长方形，默认 8×6 大格，对齐网格。
//   · 长方形的一圈边被切成一段段"墙位"，每段正好放一面墙（墙的占地多大，一段就多大）。
//     顺着一圈编号（下边 → 右边 → 上边 → 左边），所以知道哪两段是挨着的。
//   · 留 gateCount 个出入口（朝地图中心那一边的中间），不修墙——现在还没有门，全围死 NPC 就出不去采集了。
//   · 每段墙位的状态（每 rescanInterval 秒重新看一次）：
//       Closed  已经挡住：建好的挡路建筑，或地形（山壁、水、大块岩石 / 树）占了一半以上
//       Planned 有蓝图了，还没建好
//       Open    空着，可以修墙
//       Blocked 空着但放不下墙（被箱子、小石头之类占着），也挡不住人
//       Gate    出入口
//   · 所有非出入口的墙位都 Closed = 营地被围起来了（封闭空间）。
//   · 第一次有墙位被建筑 / 蓝图占上后，范围就固定下来（不再跟着营地中心移动，免得墙修一半范围跑了）。
// 用到它的地方：NpcSecurity（围合度、封闭、人在不在营地里）、NpcIntentBrain（墙修在哪一段、其他建筑放在营地里面）、
//               AI 的提示词（营地围墙还差几段、哪边空着）。
// 调试：Scene 视图里一直画着（Gizmos）；Inspector 勾 debugDraw 后游戏画面里也画出范围（玩家正常玩看不到）。
// 由 NpcBehaviorDriver.Start 自动挂上。
// ============================================================================
public class CampZone : MonoBehaviour
{
    public static CampZone Instance { get; private set; }

    [Header("范围（世界单位 = 大格）")]
    public Vector2 size = new Vector2(8f, 6f);
    [Tooltip("留几个出入口（1 或 2）。出入口不用修墙，算围合时当作已经围上。")]
    [Range(1, 2)] public int gateCount = 1;
    public float rescanInterval = 2f;

    [Header("调试（勾上后游戏画面里画出范围；玩家正常玩看不到）")]
    public bool debugDraw = false;

    public enum SlotState { Open, Planned, Closed, Gate, Blocked }

    public class Slot
    {
        public Vector2Int origin;   // 占地左下角那一格
        public Vector2Int center;   // 中心那一格（放蓝图时传它）
        public string side;         // north / south / east / west
        public int index;           // 顺着一圈的编号
        public bool gate;
        public SlotState state;
    }

    public bool Ready { get; private set; }
    public bool Locked { get; private set; }
    public Vector2Int Origin { get; private set; }    // 范围左下角（格）
    public Vector2Int Size { get; private set; }      // 范围大小（格）
    public Vector2Int SlotSize { get; private set; }  // 一段墙位占几格
    public readonly List<Slot> Slots = new List<Slot>();
    public int OpenCount { get; private set; }
    public int PlannedCount { get; private set; }
    public int ClosedCount { get; private set; }
    public int BlockedCount { get; private set; }
    public int GateCount { get; private set; }

    // 围合度：非出入口的墙位里，已经挡住的比例。
    public float Closure => Slots.Count - GateCount > 0 ? (float)ClosedCount / (Slots.Count - GateCount) : 0f;
    // 封闭：所有非出入口的墙位都挡住了。
    public bool Enclosed => Ready && Slots.Count > 0 && ClosedCount + GateCount >= Slots.Count;

    readonly Dictionary<string, bool> _nameBlocks = new Dictionary<string, bool>();
    readonly HashSet<Vector2Int> _blueprintCells = new HashSet<Vector2Int>();
    Vector2Int _builtCenter = new Vector2Int(int.MinValue, int.MinValue);
    Vector2 _builtSize;
    float _scanT;
    LineRenderer _line;

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
        _scanT -= Time.deltaTime;
        if (_scanT <= 0f)
        {
            _scanT = rescanInterval;
            GridManager g = GridManager.Instance;
            if (g != null)
            {
                if (!Locked) Rebuild(g);
                if (Ready) Evaluate(g);
            }
        }
        UpdateDebugLine();
    }

    // ============ 对外接口 ============

    // 这个点在营地范围里吗（margin = 往外放宽多少世界单位）。
    public static bool Contains(Vector3 world, float margin = 0f)
    {
        CampZone z = Instance;
        GridManager g = GridManager.Instance;
        if (z == null || !z.Ready || g == null) return false;
        GetWorldRect(z, g, out Vector2 min, out Vector2 max);
        return world.x >= min.x - margin && world.x <= max.x + margin && world.y >= min.y - margin && world.y <= max.y + margin;
    }

    // 某一边的围合度（0~1）。
    public float SideClosure(string side)
    {
        int total = 0, closed = 0;
        foreach (Slot s in Slots)
        {
            if (s.side != side || s.gate) continue;
            total++;
            if (s.state == SlotState.Closed) closed++;
        }
        return total > 0 ? (float)closed / total : 1f;
    }

    // 空得最多的那一边（建造组先补这边）。
    public string MostOpenSide()
    {
        string best = "south";
        int max = -1;
        foreach (string side in new[] { "north", "east", "south", "west" })
        {
            int k = SideGaps(side);
            if (k > max) { max = k; best = side; }
        }
        return best;
    }

    // 某一边还空着几段（Open + Blocked）。
    public int SideGaps(string side)
    {
        int k = 0;
        foreach (Slot s in Slots)
            if (s.side == side && !s.gate && (s.state == SlotState.Open || s.state == SlotState.Blocked)) k++;
        return k;
    }

    // 下一面墙修在哪一段：只挑空着能修的（Open）；AI 说的那一边优先，挨着已有墙 / 蓝图 / 出入口的优先（连成一排）。
    public Slot PickWallSlot(string preferSide, HashSet<Slot> skip = null)
    {
        if (!Ready) return null;
        int n = Slots.Count;
        Slot best = null;
        float bestScore = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            Slot s = Slots[i];
            if (s.gate || s.state != SlotState.Open || (skip != null && skip.Contains(s))) continue;
            float score = 0f;
            if (s.side == preferSide) score += 3f;
            Slot a = Slots[(i + n - 1) % n], b = Slots[(i + 1) % n];
            if (Solid(a)) score += 2f;
            if (Solid(b)) score += 2f;
            score -= SideClosure(s.side) * 1f;                // 稍微偏向还空得多的那一边
            score += Random.Range(0f, 0.3f);
            if (score > bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    static bool Solid(Slot s) => s.gate || s.state == SlotState.Closed || s.state == SlotState.Planned;

    // 这块占地完全在营地里面、又不压在墙位那一圈上吗（放墙以外的建筑用）。
    public bool Inner(Vector2Int origin, Vector2Int size)
    {
        if (!Ready) return false;
        return origin.x >= Origin.x + SlotSize.x && origin.y >= Origin.y + SlotSize.y
            && origin.x + size.x <= Origin.x + Size.x - SlotSize.x && origin.y + size.y <= Origin.y + Size.y - SlotSize.y;
    }

    // 给 AI 看的一段话：营地围墙的现状。
    public string Describe(bool jp)
    {
        if (!Ready) return "";
        GridManager g = GridManager.Instance;
        float cs = g != null ? g.cellSize : 1f;
        int w = Mathf.RoundToInt(Size.x * cs), h = Mathf.RoundToInt(Size.y * cs);
        int need = Slots.Count - GateCount;
        StringBuilder sb = new StringBuilder();
        if (Enclosed)
            sb.Append(jp ? $"拠点（約{w}×{h}）はもう壁で囲まれている（出入口 {GateCount} か所）。"
                         : $"营地（约 {w}×{h}）已经被围起来了（留了 {GateCount} 个出入口）。");
        else
        {
            sb.Append(jp ? $"拠点（約{w}×{h}）を囲む壁の位置は {need} か所（ほかに出入口 {GateCount}）：囲めている {ClosedCount}、建設予定 {PlannedCount}、まだ空いている {OpenCount + BlockedCount}"
                         : $"营地（约 {w}×{h}）一圈要修墙的位置共 {need} 段（另留 {GateCount} 个出入口）：已围上 {ClosedCount}，规划中 {PlannedCount}，还空着 {OpenCount + BlockedCount}");
            List<string> gaps = new List<string>();
            foreach (string side in new[] { "north", "east", "south", "west" })
            {
                int k = SideGaps(side);
                if (k > 0) gaps.Add(SideName(side, jp) + k);
            }
            if (gaps.Count > 0) sb.Append(jp ? "（" + string.Join("・", gaps) + "）" : "（" + string.Join("、", gaps) + "）");
            sb.Append(jp ? "。" : "。");
        }
        return sb.ToString();
    }

    public static string SideName(string side, bool jp)
    {
        switch (side)
        {
            case "north": return jp ? "北側" : "北边";
            case "east": return jp ? "東側" : "东边";
            case "south": return jp ? "南側" : "南边";
            default: return jp ? "西側" : "西边";
        }
    }

    // ============ 划范围 ============

    void Rebuild(GridManager g)
    {
        BuildableData wall = WallData();
        Vector2Int slot = wall != null ? wall.FootprintSize(g.cellSize) : new Vector2Int(Mathf.Max(1, GridManager.Subdivision), Mathf.Max(1, GridManager.Subdivision));
        Vector3 c = NpcNav.CampCenter(new Vector3(g.origin.x + g.gridWidth * g.cellSize * 0.5f, g.origin.y + g.gridHeight * g.cellSize * 0.5f, 0f));
        Vector2Int cc = g.WorldToCell(c);
        if (Ready && cc == _builtCenter && slot == SlotSize && size == _builtSize) return; // 没变

        int nx = Mathf.Max(3, Mathf.RoundToInt(size.x / (slot.x * g.cellSize)));
        int ny = Mathf.Max(3, Mathf.RoundToInt(size.y / (slot.y * g.cellSize)));
        SlotSize = slot;
        Origin = new Vector2Int(cc.x - nx * slot.x / 2, cc.y - ny * slot.y / 2);
        Size = new Vector2Int(nx * slot.x, ny * slot.y);
        _builtCenter = cc;
        _builtSize = size;

        // 顺着一圈：下边（左→右）→ 右边（下→上）→ 上边（右→左）→ 左边（上→下）
        Slots.Clear();
        for (int i = 0; i < nx; i++) AddSlot(i, 0, "south");
        for (int j = 1; j < ny; j++) AddSlot(nx - 1, j, "east");
        for (int i = nx - 2; i >= 0; i--) AddSlot(i, ny - 1, "north");
        for (int j = ny - 2; j >= 1; j--) AddSlot(0, j, "west");

        // 出入口：朝地图中心那一边的中间（营地在地图正中就放南边）；要两个就再加对面那一边
        Vector3 mapCenter = new Vector3(g.origin.x + g.gridWidth * g.cellSize * 0.5f, g.origin.y + g.gridHeight * g.cellSize * 0.5f, 0f);
        Vector2 d = mapCenter - c;
        string gateSide = d.sqrMagnitude < 1f ? "south" : Mathf.Abs(d.x) > Mathf.Abs(d.y) ? (d.x > 0f ? "east" : "west") : (d.y > 0f ? "north" : "south");
        MarkGate(g, gateSide);
        if (gateCount >= 2) MarkGate(g, Opposite(gateSide));
        Ready = Slots.Count > 0;
    }

    void AddSlot(int ix, int iy, string side)
    {
        Vector2Int o = new Vector2Int(Origin.x + ix * SlotSize.x, Origin.y + iy * SlotSize.y);
        Slots.Add(new Slot
        {
            origin = o,
            center = new Vector2Int(o.x + (SlotSize.x - 1) / 2, o.y + (SlotSize.y - 1) / 2),
            side = side,
            index = Slots.Count,
        });
    }

    // 这一边中间那段当出入口（中间被地形挡死就往两边找能走的）。
    void MarkGate(GridManager g, string side)
    {
        List<Slot> list = new List<Slot>();
        foreach (Slot s in Slots) if (s.side == side) list.Add(s);
        if (list.Count == 0) return;
        int mid = list.Count / 2;
        for (int k = 0; k < list.Count; k++)
        {
            int i = mid + ((k % 2 == 0) ? k / 2 : -(k / 2 + 1));
            if (i < 0 || i >= list.Count) continue;
            if (BlockedFraction(g, list[i]) < 0.5f) { list[i].gate = true; return; }
        }
        list[mid].gate = true;
    }

    static string Opposite(string side) => side == "north" ? "south" : side == "south" ? "north" : side == "east" ? "west" : "east";

    // ============ 每段墙位的状态 ============

    void Evaluate(GridManager g)
    {
        PrepareBlockers(g);
        int open = 0, planned = 0, closed = 0, blocked = 0, gates = 0;
        bool anyBuilt = false;
        int total = SlotSize.x * SlotSize.y;
        foreach (Slot s in Slots)
        {
            if (s.gate) { s.state = SlotState.Gate; gates++; continue; }
            int terrain = 0, building = 0, plan = 0;
            for (int x = s.origin.x; x < s.origin.x + SlotSize.x; x++)
                for (int y = s.origin.y; y < s.origin.y + SlotSize.y; y++)
                {
                    Vector2Int c = new Vector2Int(x, y);
                    if (!g.InBounds(x, y)) { terrain++; continue; }
                    if (_blueprintCells.Contains(c)) { plan++; continue; }
                    if (BuildingBlocks(g, x, y)) { building++; continue; }
                    if (TerrainBlocks(c)) terrain++;
                }
            if (plan > 0) { s.state = SlotState.Planned; planned++; anyBuilt = true; }
            else if (building + terrain >= total * 0.5f) { s.state = SlotState.Closed; closed++; if (building > 0) anyBuilt = true; }
            else if (g.IsAreaBuildable(s.origin, SlotSize)) { s.state = SlotState.Open; open++; }
            else { s.state = SlotState.Blocked; blocked++; }
        }
        OpenCount = open; PlannedCount = planned; ClosedCount = closed; BlockedCount = blocked; GateCount = gates;
        if (anyBuilt) Locked = true; // 开始修墙了：范围固定
    }

    float BlockedFraction(GridManager g, Slot s)
    {
        int k = 0, total = SlotSize.x * SlotSize.y;
        for (int x = s.origin.x; x < s.origin.x + SlotSize.x; x++)
            for (int y = s.origin.y; y < s.origin.y + SlotSize.y; y++)
                if (!g.InBounds(x, y) || TerrainBlocks(new Vector2Int(x, y))) k++;
        return total > 0 ? (float)k / total : 0f;
    }

    // 地形挡路：山壁、水面、大块岩石 / 树。
    static bool TerrainBlocks(Vector2Int c)
    {
        if (TerrainSystem.IsWall(c) || TerrainSystem.TypeAt(c) == TerrainType.Water) return true;
        ResourceNode r = TerrainSystem.ResourceAt(c);
        return r != null && ResourceInfo.Blocks(r.kind, r.size);
    }

    // 建好的挡路建筑（蓝图不算）。
    bool BuildingBlocks(GridManager g, int x, int y)
    {
        string occ = g.GetOccupied(x, y);
        return !string.IsNullOrEmpty(occ) && _nameBlocks.TryGetValue(occ, out bool b) && b;
    }

    void PrepareBlockers(GridManager g)
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

    public static BuildableData WallData()
    {
        BuildManager bm = BuildManager.Instance;
        if (bm == null || bm.buildables == null) return null;
        foreach (BuildableData d in bm.buildables) if (d != null && d.id == "wall") return d;
        return null;
    }

    static void GetWorldRect(CampZone z, GridManager g, out Vector2 min, out Vector2 max)
    {
        Vector3 a = g.CellToWorld(z.Origin);
        Vector3 b = g.CellToWorld(z.Origin.x + z.Size.x - 1, z.Origin.y + z.Size.y - 1);
        float half = g.cellSize * 0.5f;
        min = new Vector2(a.x - half, a.y - half);
        max = new Vector2(b.x + half, b.y + half);
    }

    // ============ 调试显示 ============

    void UpdateDebugLine()
    {
        GridManager g = GridManager.Instance;
        bool show = debugDraw && Ready && g != null;
        if (!show)
        {
            if (_line != null && _line.enabled) _line.enabled = false;
            return;
        }
        if (_line == null)
        {
            GameObject go = new GameObject("CampZoneDebug");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.widthMultiplier = 0.06f;
            _line.loop = true;
            _line.useWorldSpace = true;
            _line.sortingOrder = 50;
            _line.positionCount = 4;
            _line.startColor = _line.endColor = new Color(0.72f, 1f, 0.78f, 0.7f);
        }
        _line.enabled = true;
        GetWorldRect(this, g, out Vector2 min, out Vector2 max);
        _line.SetPosition(0, new Vector3(min.x, min.y, 0f));
        _line.SetPosition(1, new Vector3(max.x, min.y, 0f));
        _line.SetPosition(2, new Vector3(max.x, max.y, 0f));
        _line.SetPosition(3, new Vector3(min.x, max.y, 0f));
    }

    // Scene 视图：每段墙位按状态上色（白 = 空着，青 = 规划中，绿 = 已围上，黄 = 出入口，红 = 放不下墙）。
    void OnDrawGizmos()
    {
        GridManager g = GridManager.Instance;
        if (!Ready || g == null) return;
        foreach (Slot s in Slots)
        {
            Gizmos.color = s.state == SlotState.Closed ? new Color(0.4f, 1f, 0.5f, 0.8f)
                         : s.state == SlotState.Planned ? new Color(0.3f, 0.9f, 1f, 0.8f)
                         : s.state == SlotState.Gate ? new Color(1f, 0.9f, 0.3f, 0.9f)
                         : s.state == SlotState.Blocked ? new Color(1f, 0.35f, 0.3f, 0.8f)
                         : new Color(1f, 1f, 1f, 0.5f);
            Vector3 c = g.AreaCenter(s.origin, SlotSize);
            Gizmos.DrawWireCube(c, new Vector3(SlotSize.x * g.cellSize * 0.9f, SlotSize.y * g.cellSize * 0.9f, 0f));
        }
    }
}
