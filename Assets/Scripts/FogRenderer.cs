using UnityEngine;

// 战争迷雾 · 视觉层（第2批）。
// 读取 FogManager 的每格状态，用一层黑色方块把迷雾"画"出来：
//   Unexplored（未探索）→ 全黑（不透明）
//   Explored（探索过）  → 半黑（半透明）
//   Visible（视野内）   → 透明（看得清）
//
// 做法：为每个格子生成一个黑色 Sprite 方块，盖在地图上，按状态调它的透明度。
// 这批只做"把状态显示出来"；状态怎么变（NPC揭雾）是第3批。
[RequireComponent(typeof(Transform))]
public class FogRenderer : MonoBehaviour
{
    [Header("迷雾外观")]
    public Color fogColor = Color.black;        // 迷雾颜色（默认黑）
    [Range(0f, 1f)] public float unexploredAlpha = 1f;   // 未探索：全黑
    [Range(0f, 1f)] public float exploredAlpha = 0.5f;   // 探索过：半黑
    [Range(0f, 1f)] public float visibleAlpha = 0f;      // 视野内：透明
    public int sortingOrder = 100;              // 排序层级（要盖在地图/NPC之上）

    [Header("过渡")]
    public float fadeSpeed = 8f;                // 透明度渐变速度（让明暗变化柔和，不生硬）

    private SpriteRenderer[,] _tiles;           // 每格一个黑块
    private int _w, _h;
    private Sprite _squareSprite;               // 共用的白色方块Sprite（用颜色染成黑）

    void Start()
    {
        BuildTiles();
    }

    // 为每个格子生成一个黑色遮罩块。
    void BuildTiles()
    {
        GridManager grid = GridManager.Instance;
        if (grid == null) { Debug.LogError("[FogRenderer] 找不到 GridManager"); return; }

        _w = grid.gridWidth;
        _h = grid.gridHeight;
        _tiles = new SpriteRenderer[_w, _h];

        // 生成一个 1x1 的白色方块 Sprite（运行时创建，不依赖美术资源）。
        _squareSprite = CreateSquareSprite();

        for (int x = 0; x < _w; x++)
        {
            for (int y = 0; y < _h; y++)
            {
                GameObject tile = new GameObject($"Fog_{x}_{y}");
                tile.transform.parent = transform;
                tile.transform.position = grid.CellToWorld(x, y);       // 摆到格子中心
                tile.transform.localScale = Vector3.one * grid.cellSize; // 撑满一格

                SpriteRenderer sr = tile.AddComponent<SpriteRenderer>();
                sr.sprite = _squareSprite;
                sr.color = new Color(fogColor.r, fogColor.g, fogColor.b, unexploredAlpha); // 初始全黑
                sr.sortingOrder = sortingOrder;

                _tiles[x, y] = sr;
            }
        }
    }

    // 每帧根据 FogManager 的状态，更新每格黑块的透明度（带柔和渐变）。
    void Update()
    {
        if (_tiles == null || FogManager.Instance == null) return;

        for (int x = 0; x < _w; x++)
        {
            for (int y = 0; y < _h; y++)
            {
                SpriteRenderer sr = _tiles[x, y];
                if (sr == null) continue;

                // 按当前迷雾状态，决定目标透明度。
                float targetAlpha = unexploredAlpha;
                switch (FogManager.Instance.GetState(x, y))
                {
                    case FogState.Unexplored: targetAlpha = unexploredAlpha; break;
                    case FogState.Explored:   targetAlpha = exploredAlpha;   break;
                    case FogState.Visible:    targetAlpha = visibleAlpha;    break;
                }

                // 平滑过渡到目标透明度（避免明暗突变、生硬闪烁）。
                Color c = sr.color;
                c.a = Mathf.MoveTowards(c.a, targetAlpha, fadeSpeed * Time.deltaTime);
                sr.color = c;
            }
        }
    }

    // 运行时创建一个纯白 1x1 方块 Sprite（避免依赖外部美术素材）。
    Sprite CreateSquareSprite()
    {
        Texture2D tex = new Texture2D(4, 4);
        Color[] pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        // pixelsPerUnit = 4，配合 texture 4x4，使这个 sprite 本身是 1x1 世界单位。
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
    }
}
