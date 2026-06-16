using UnityEngine;
using System.Collections.Generic;

// 网格系统：把地图划分成方格，提供坐标互转、编号、可建造判定、占用记录。
// 是建造模式的地基。配合 Tilemap（1 格 = 1 世界单位）。
public class GridManager : MonoBehaviour
{
    public static GridManager Instance;

    [Header("网格设置")]
    public int gridWidth = 30;          // 横向多少格
    public int gridHeight = 20;         // 纵向多少格
    public float cellSize = 1f;         // 每格的世界尺寸（配合 Tilemap，1 格 = 1 单位）
    public Vector2 origin = Vector2.zero; // 网格左下角的世界坐标

    // 每格的占用记录：null = 空，否则存占用它的东西的名字（如建筑名）。
    private string[,] _occupied;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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

    // —— 格子坐标 → 编号（如 (6,7) → "G7"）。列用字母，行用数字。——
    public string CellToLabel(int x, int y)
    {
        // 列：0→A,1→B...（超过26用 AA 之类，简单起见先支持到 Z）
        char col = (char)('A' + Mathf.Clamp(x, 0, 25));
        return $"{col}{y}";
    }

    // —— 编号 → 格子坐标（如 "G7" → (6,7)）。解析失败返回 false。——
    public bool LabelToCell(string label, out Vector2Int cell)
    {
        cell = Vector2Int.zero;
        if (string.IsNullOrEmpty(label) || label.Length < 2) return false;

        char col = char.ToUpper(label[0]);
        if (col < 'A' || col > 'Z') return false;
        int x = col - 'A';

        if (!int.TryParse(label.Substring(1), out int y)) return false;

        cell = new Vector2Int(x, y);
        return true;
    }

    // —— 这个格子在地图范围内吗 ——
    public bool InBounds(int x, int y)
    {
        return x >= 0 && x < gridWidth && y >= 0 && y < gridHeight;
    }

    // —— 这个格子能不能建造（在范围内 且 没被占用）——
    public bool IsBuildable(int x, int y)
    {
        if (!InBounds(x, y)) return false;
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
}
