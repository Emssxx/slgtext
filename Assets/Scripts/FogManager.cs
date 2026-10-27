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
    }

    // 查询某格的迷雾状态（越界返回未探索）。
    public FogState GetState(int x, int y)
    {
        if (_fog == null || x < 0 || x >= _w || y < 0 || y >= _h) return FogState.Unexplored;
        return _fog[x, y];
    }

    // —— 揭雾接口（第3批 NPC 揭雾、第4批 营地初始揭开 都会调这些）——

    // 每帧/每次更新视野前，先把所有"视野内"降级为"探索过"。
    // （因为NPC会移动，上一刻能看到的，这一刻可能看不到了 → 变成"记得但看不清"。）
    public void DemoteVisibleToExplored()
    {
        if (_fog == null) return;
        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
                if (_fog[x, y] == FogState.Visible)
                    _fog[x, y] = FogState.Explored;
    }

    // 把某个格子设为"视野内"（完全亮）。NPC视野覆盖到的格子调这个。
    public void RevealCell(int x, int y)
    {
        if (_fog == null || x < 0 || x >= _w || y < 0 || y >= _h) return;
        _fog[x, y] = FogState.Visible;
    }

    // 把以 (cx,cy) 为中心、半径 radius 的圆形范围设为"视野内"。
    // NPC揭雾、营地初始揭开都用这个。
    public void RevealArea(int cx, int cy, int radius)
    {
        if (_fog == null) return;
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
                switch (_fog[x, y])
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
