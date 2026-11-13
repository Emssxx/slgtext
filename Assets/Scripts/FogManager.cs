using System.Collections.Generic;
using UnityEngine;

// 战争迷雾 · 数据层（第1批）。
// 只负责"每个格子处于什么迷雾状态"，不负责显示（显示是第2批）、不负责NPC揭雾（第3批）。
//
// 三态（经典战争迷雾）：
//   Unexplored（未探索）：全黑，从没看到过。
//   Explored（探索过）  ：变暗但可见，看到过但现在视野外（记得地形，但不知道当前情况）。
//   Visible（视野内）   ：完全亮，当前有NPC能看到。
public enum FogState { Unexplored, Explored, Visible }

public class FogManager : MonoBehaviour
{
    public static FogManager Instance { get; private set; }

    // 每个格子的迷雾状态，和 GridManager 的网格同尺寸。
    private FogState[,] _fog;
    private int _w, _h;

    [Tooltip("用整张地图一张贴图画迷雾（FogOfWarRenderer：边缘柔和、带流动的雾气，镜头外也有）。关掉 = 用原来的粒子雾")]
    public bool useTextureFog = true;

    // ============ 永久雾（新地图规则，第 1 步）============
    // 营地是固定基地，营地外是永久的雾：NPC 视野、光照、终端机都揭不开。
    // 只有两种地方没有雾：① 营地矩形 ② 用 ClearRegion 登记的区域（以后 AI 导演生成的区域；消失时 CoverRegion 盖回去）。
    // 永久雾模式下只有"雾 / 没雾"两种状态（GetState 只会返回 Unexplored 或 Visible，没有"探索过"）。
    // 关掉 permanentFog = 回到原来的三态战争迷雾（NPC 走到哪揭到哪）。
    [Header("永久雾（新地图规则）")]
    [Tooltip("开 = 营地外永远是雾，NPC 视野 / 光照 / 终端机都揭不开；关 = 原来的战争迷雾")]
    public bool permanentFog = true;
    [Tooltip("营地地砖范围往外再露出几格（大格，1 大格 = 1 块地砖）。营地围墙修在地砖边上，留 1 格能看见墙")]
    public int campClearMargin = 1;
    [Tooltip("还没有地形数据时用的营地大小（大格，左右 / 上下各多少）。正常情况用 TerrainSystem 的营地地砖范围")]
    public Vector2 fallbackCampHalfSize = new Vector2(4f, 3f);

    bool[,] _clear;                                          // 永久雾模式：这一格没有雾
    RectInt _campRect;                                       // 营地（小格，已经含 campClearMargin）
    bool _campSet, _campFromMap;
    readonly Dictionary<int, RectInt> _regions = new Dictionary<int, RectInt>(); // 区域编号 → 范围（小格）
    int _nextRegionId = 1;

    // 营地没有雾的范围（小格）。还没算出来时宽高为 0。
    public RectInt CampRect { get { EnsureCamp(); return _campRect; } }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (useTextureFog && GetComponent<FogOfWarRenderer>() == null) gameObject.AddComponent<FogOfWarRenderer>();
    }

    void Start()
    {
        InitFog();
    }

    // 初始化迷雾：全部设为"未探索"（全黑）。
    public void InitFog()
    {
        GridManager grid = GridManager.Instance;
        if (grid == null) { Debug.LogError("[FogManager] 找不到 GridManager"); return; }

        _w = grid.gridWidth;
        _h = grid.gridHeight;
        _fog = new FogState[_w, _h];

        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
                _fog[x, y] = FogState.Unexplored;  // 开局全黑

        _clear = new bool[_w, _h];
        _campSet = _campFromMap = false;
        EnsureCamp();
    }

    // 查询某格的迷雾状态（越界返回未探索）。
    public FogState GetState(int x, int y)
    {
        if (_fog == null || x < 0 || x >= _w || y < 0 || y >= _h) return FogState.Unexplored;
        if (permanentFog)
        {
            EnsureCamp();
            return _clear[x, y] ? FogState.Visible : FogState.Unexplored;
        }
        return _fog[x, y];
    }

    // ============ 永久雾：查询 ============

    // 这一格在不在雾里（地图外也算雾）。没开永久雾时：不在视野内就算。
    public bool IsFog(int x, int y)
    {
        return GetState(x, y) != FogState.Visible;
    }

    // 这个世界坐标在不在雾里。没有迷雾系统时返回 false。
    public static bool InFog(Vector3 worldPos)
    {
        if (Instance == null || GridManager.Instance == null) return false;
        Vector2Int c = GridManager.Instance.WorldToCell(worldPos);
        return Instance.IsFog(c.x, c.y);
    }

    // ============ 永久雾：区域（第 2 步 AI 导演用）============

    // 让一块长方形区域的雾散开（小格坐标）。返回区域编号，区域消失时用它调 CoverRegion。
    public int ClearRegion(RectInt cells)
    {
        int id = _nextRegionId++;
        _regions[id] = cells;
        RebuildClear();
        return id;
    }

    // 同上，范围用世界坐标填（中心 + 宽高，单位 = 大格 = 地砖）。
    public int ClearRegionWorld(Vector2 center, Vector2 size)
    {
        GridManager g = GridManager.Instance;
        if (g == null) return -1;
        Vector2Int a = g.WorldToCell(new Vector3(center.x - size.x * 0.5f, center.y - size.y * 0.5f, 0f));
        Vector2Int b = g.WorldToCell(new Vector3(center.x + size.x * 0.5f, center.y + size.y * 0.5f, 0f));
        return ClearRegion(new RectInt(a.x, a.y, Mathf.Max(1, b.x - a.x), Mathf.Max(1, b.y - a.y)));
    }

    // 区域消失：雾重新盖回去（和营地 / 别的区域重叠的部分不会被盖）。
    public void CoverRegion(int id)
    {
        if (_regions.Remove(id)) RebuildClear();
    }

    // 手动指定营地范围（小格）。以后换成预制基地、或营地扩建时调这个。
    public void SetCampRect(RectInt cells)
    {
        _campRect = cells;
        _campSet = true;
        _campFromMap = true;   // 手动指定后不再跟着地形数据改
        RebuildClear();
    }

    // 营地范围：优先用 TerrainSystem 生成的营地地砖矩形；地形还没生成好时先用出生点周围，等地形好了再换成准确的。
    void EnsureCamp()
    {
        if (_campFromMap || _clear == null) return;
        int m = Mathf.Max(0, campClearMargin) * GridManager.Subdivision;

        TerrainSystem t = TerrainSystem.Instance;
        if (t != null && t.Map != null && t.Map.campW > 0 && t.Map.campH > 0)
        {
            _campRect = new RectInt(t.Map.campX - m, t.Map.campY - m, t.Map.campW + m * 2, t.Map.campH + m * 2);
            _campSet = _campFromMap = true;
            RebuildClear();
            return;
        }

        if (_campSet || GridManager.Instance == null) return;
        GridManager g = GridManager.Instance;
        NpcSpawner sp = FindFirstObjectByType<NpcSpawner>();
        Vector3 c = sp != null ? (sp.spawnCenter != null ? sp.spawnCenter.position : sp.transform.position) : Vector3.zero;
        Vector2Int a = g.WorldToCell(new Vector3(c.x - fallbackCampHalfSize.x, c.y - fallbackCampHalfSize.y, 0f));
        Vector2Int b = g.WorldToCell(new Vector3(c.x + fallbackCampHalfSize.x, c.y + fallbackCampHalfSize.y, 0f));
        _campRect = new RectInt(a.x - m, a.y - m, b.x - a.x + m * 2, b.y - a.y + m * 2);
        _campSet = true;
        RebuildClear();
    }

    // 重新算一遍"哪些格没有雾" = 营地 ∪ 所有区域。只在范围变化时调，不是每帧。
    void RebuildClear()
    {
        if (_clear == null) return;
        System.Array.Clear(_clear, 0, _clear.Length);
        if (_campSet) Mark(_campRect);
        foreach (RectInt r in _regions.Values) Mark(r);
    }

    void Mark(RectInt r)
    {
        int x0 = Mathf.Max(0, r.xMin), y0 = Mathf.Max(0, r.yMin);
        int x1 = Mathf.Min(_w, r.xMax), y1 = Mathf.Min(_h, r.yMax);
        for (int x = x0; x < x1; x++)
            for (int y = y0; y < y1; y++)
                _clear[x, y] = true;
    }

    // —— 揭雾接口（第3批 NPC 揭雾、第4批 营地初始揭开 都会调这些）——

    // 每帧/每次更新视野前，先把所有"视野内"降级为"探索过"。
    // （因为NPC会移动，上一刻能看到的，这一刻可能看不到了 → 变成"记得但看不清"。）
    public void DemoteVisibleToExplored()
    {
        if (_fog == null || permanentFog) return;   // 永久雾：NPC 视野揭不开雾，下面三个揭雾接口都不起作用
        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
                if (_fog[x, y] == FogState.Visible)
                    _fog[x, y] = FogState.Explored;
    }

    // 把某个格子设为"视野内"（完全亮）。NPC视野覆盖到的格子调这个。
    public void RevealCell(int x, int y)
    {
        if (permanentFog) return;
        if (_fog == null || x < 0 || x >= _w || y < 0 || y >= _h) return;
        _fog[x, y] = FogState.Visible;
    }

    // 把以 (cx,cy) 为中心、半径 radius 的圆形范围设为"视野内"。
    // NPC揭雾、营地初始揭开都用这个。
    public void RevealArea(int cx, int cy, int radius)
    {
        if (_fog == null || permanentFog) return;
        for (int x = cx - radius; x <= cx + radius; x++)
        {
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                if (x < 0 || x >= _w || y < 0 || y >= _h) continue;
                // 圆形范围：用距离判断（比方形更自然）。
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= radius * radius)
                    _fog[x, y] = FogState.Visible;
            }
        }
    }

    // —— 调试用：把整个迷雾状态打印出来，验证数据对不对（第1批测试用）——
    [ContextMenu("Debug Print Fog")]
    public void DebugPrintFog()
    {
        if (_fog == null) { Debug.Log("[FogManager] 迷雾未初始化"); return; }
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        // 从上往下打印（y 从高到低），符合视觉习惯。
        for (int y = _h - 1; y >= 0; y--)
        {
            for (int x = 0; x < _w; x++)
            {
                switch (GetState(x, y))
                {
                    case FogState.Unexplored: sb.Append('■'); break; // 黑
                    case FogState.Explored:   sb.Append('▒'); break; // 半黑
                    case FogState.Visible:    sb.Append('□'); break; // 亮
                }
            }
            sb.Append('\n');
        }
        Debug.Log("[FogManager] 迷雾状态：\n" + sb.ToString());
    }
}
