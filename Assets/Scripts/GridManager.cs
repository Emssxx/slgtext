using UnityEngine;
using System.Collections.Generic;

// 网格系统：把地图划分成方格，提供坐标互转、编号、可建造判定、占用记录。
// 是建造模式的地基。
// 【细分】场景里设置的是"大格"（配合 Tilemap，1 大格 = 1 世界单位）；运行时每个大格再切成 subdivision × subdivision 个小格
//   （默认 3 → 一格变 9 格，每小格 1/3 单位）。之后所有格子坐标、建造占地、坐标浮窗都按小格算。
//   建筑占几个小格由它的图片大小自动决定（1 单位的图 = 3×3 小格），也可以在 BuildableData 里手动指定，做出不同体积、形状的建筑。
//   想恢复原来的大格：把 subdivision 改成 1。
// 【地图大小】mapSize（大格）不为 0 时，运行时用它代替上面的 gridWidth / gridHeight，并以 mapCenter 为中心铺开
//   （默认 72×33，是场景里那块 24×11 测试地图的 9 倍）。想回到测试地图：mapSize 填 0, 0。
//   地图变大后，场景里原来那圈测试墙由 TerrainSystem 自动换成新地图四周的围墙。
public class GridManager : MonoBehaviour
{
    public static GridManager Instance;

    [Header("网格设置")]
    public int gridWidth = 30;          // 横向多少格
    public int gridHeight = 20;         // 纵向多少格
    public float cellSize = 1f;         // 每格的世界尺寸（配合 Tilemap，1 格 = 1 单位）
    public Vector2 origin = Vector2.zero; // 网格左下角的世界坐标
    [Tooltip("运行时把每格再细分成 N×N 小格（3 = 一格变 9 格；1 = 不细分）。上面的宽高和格子大小按细分前填。")]
    [Range(1, 6)] public int subdivision = 3;

    [Header("地图大小（运行时）")]
    [Tooltip("游戏里实际的地图大小（大格，1 大格 = 1 世界单位）。0, 0 = 用上面的 Grid Width / Height（场景里那块测试地图）。")]
    public Vector2Int mapSize = new Vector2Int(72, 33);
    [Tooltip("地图以哪个世界坐标为中心铺开（营地 / NPC 生成点在 0, 0）。左下角会对齐到整数格，和场景里的 Tilemap 对齐。")]
    public Vector2 mapCenter = Vector2.zero;

    // 实际生效的细分倍数（别的系统把"以前按大格填的数值"换算成小格时用，比如视野半径 ×Subdivision）。
    public static int Subdivision { get; private set; } = 1;

    // 每格的占用记录：null = 空，否则存占用它的东西的名字（如建筑名）。
    private string[,] _occupied;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (mapSize.x > 0 && mapSize.y > 0)
        {
            gridWidth = mapSize.x;
            gridHeight = mapSize.y;
            origin = new Vector2(Mathf.Round(mapCenter.x - mapSize.x * cellSize * 0.5f), Mathf.Round(mapCenter.y - mapSize.y * cellSize * 0.5f));
        }
        int n = Mathf.Max(1, subdivision);
        gridWidth *= n;          // 细分：格子数 ×N、每格边长 ÷N，地图在世界里的大小不变
        gridHeight *= n;
        cellSize /= n;
        Subdivision = n;
        _occupied = new string[gridWidth, gridHeight]; // 默认全 null（空）
    }

    // —— 世界坐标 → 格子坐标（鼠标点击用）——
    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt((worldPos.x - origin.x) / cellSize);
        int y = Mathf.FloorToInt((worldPos.y - origin.y) / cellSize);
        return new Vector2Int(x, y);
    }

    // —— 格子坐标 → 世界坐标（取格子中心，给 NPC 寻路目标用）——
    public Vector3 CellToWorld(int x, int y)
    {
        float wx = origin.x + x * cellSize + cellSize * 0.5f;
        float wy = origin.y + y * cellSize + cellSize * 0.5f;
        return new Vector3(wx, wy, 0f);
    }
    public Vector3 CellToWorld(Vector2Int cell) => CellToWorld(cell.x, cell.y);

    // —— 格子坐标 → 坐标文字（如 (6,7) → "6,7"）。x = 列（从左往右），y = 行（从下往上），都从 0 开始。——
    // 建筑名后面的位置（"风力发电机（6,7）"）、NPC 指令的目的地都用这个写法。
    public string CellToLabel(int x, int y)
    {
        return x + "," + y;
    }

    // 坐标的各种写法："6,7" "(6,7)" "（6，7）" "6 7" "x6 y7" "x6y7" "X:6 Y:7"；也认旧的字母编号 "G7"（= 6,7）。
    static readonly System.Text.RegularExpressions.Regex CoordPattern = new System.Text.RegularExpressions.Regex(
        @"^[\(（\[]?\s*[xX]?\s*[:：=]?\s*(\d{1,3})(?:\s*[,，、]\s*[yY]?|\s+[yY]?|\s*[yY])\s*[:：=]?\s*(\d{1,3})\s*[\)）\]]?$");

    // —— 坐标文字 → 格子坐标。解析失败返回 false。——
    public bool LabelToCell(string label, out Vector2Int cell)
    {
        cell = Vector2Int.zero;
        if (string.IsNullOrEmpty(label)) return false;
        string t = label.Trim();

        System.Text.RegularExpressions.Match m = CoordPattern.Match(t);
        if (m.Success)
        {
            cell = new Vector2Int(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            return true;
        }

        // 旧写法：字母 + 数字（G7）
        if (t.Length < 2) return false;
        char col = char.ToUpper(t[0]);
        if (col < 'A' || col > 'Z') return false;
        if (!int.TryParse(t.Substring(1), out int y)) return false;
        cell = new Vector2Int(col - 'A', y);
        return true;
    }

    // —— 这个格子在地图范围内吗 ——
    public bool InBounds(int x, int y)
    {
        return x >= 0 && x < gridWidth && y >= 0 && y < gridHeight;
    }

    // —— 这个格子能不能建造（在范围内 且 地形允许 且 没被占用）——
    public bool IsBuildable(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        if (TerrainSystem.BlocksBuilding(x, y)) return false; // 墙、水面、沼泽、树 / 石头 / 矿石占着的格（TerrainSystem）
        return _occupied[x, y] == null;
    }

    // —— 标记某格被占用（建造后调用）——
    public void SetOccupied(int x, int y, string what)
    {
        if (InBounds(x, y)) _occupied[x, y] = what;
    }

    // —— 取某格占用情况（null = 空）——
    public string GetOccupied(int x, int y)
    {
        return InBounds(x, y) ? _occupied[x, y] : "越界";
    }

    // —— 清空某格（拆除后调用）——
    public void ClearCell(int x, int y)
    {
        if (InBounds(x, y)) _occupied[x, y] = null;
    }

    // ============ 多格建筑（占地 size 个小格）============

    // 以鼠标所在的格子为中心摆一块 size 大小的地：返回左下角那一格。偶数边长时鼠标格偏左下。
    public Vector2Int FootprintOrigin(Vector2Int cell, Vector2Int size)
    {
        return new Vector2Int(cell.x - (size.x - 1) / 2, cell.y - (size.y - 1) / 2);
    }

    // 这块地是不是每一格都能建。
    public bool IsAreaBuildable(Vector2Int origin, Vector2Int size)
    {
        for (int x = origin.x; x < origin.x + size.x; x++)
            for (int y = origin.y; y < origin.y + size.y; y++)
                if (!IsBuildable(x, y)) return false;
        return true;
    }

    public void SetAreaOccupied(Vector2Int origin, Vector2Int size, string what)
    {
        for (int x = origin.x; x < origin.x + size.x; x++)
            for (int y = origin.y; y < origin.y + size.y; y++)
                SetOccupied(x, y, what);
    }

    public void ClearArea(Vector2Int origin, Vector2Int size)
    {
        for (int x = origin.x; x < origin.x + size.x; x++)
            for (int y = origin.y; y < origin.y + size.y; y++)
                ClearCell(x, y);
    }

    // 这块地的中心（世界坐标）：建筑放在这里。
    public Vector3 AreaCenter(Vector2Int origin, Vector2Int size)
    {
        Vector3 c = CellToWorld(origin);
        return c + new Vector3((size.x - 1) * cellSize * 0.5f, (size.y - 1) * cellSize * 0.5f, 0f);
    }
}

// 挂在放下的建筑上：记下它占了哪几个小格（多格建筑的点击、电线、悬停信息都靠它找到建筑本体）。
public class BuildingFootprint : MonoBehaviour
{
    public static readonly List<BuildingFootprint> All = new List<BuildingFootprint>();

    public Vector2Int origin;            // 左下角那一格
    public Vector2Int size = Vector2Int.one;

    public void Set(Vector2Int o, Vector2Int s) { origin = o; size = new Vector2Int(Mathf.Max(1, s.x), Mathf.Max(1, s.y)); }

    public bool Contains(Vector2Int c)
    {
        return c.x >= origin.x && c.x < origin.x + size.x && c.y >= origin.y && c.y < origin.y + size.y;
    }

    public static BuildingFootprint At(Vector2Int c)
    {
        foreach (BuildingFootprint f in All) if (f != null && f.Contains(c)) return f;
        return null;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }
}
