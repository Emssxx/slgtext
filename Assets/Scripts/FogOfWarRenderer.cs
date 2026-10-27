using UnityEngine;

// ============================================================================
// 战争迷雾 · 贴图版（替代原来的粒子雾）。
//   整张地图（外加一圈边）只用一张贴图，1 个小格 = 1 个像素，双线性过滤 → 边缘柔和，镜头移到哪里都有雾。
//   读 FogManager 的三种状态：
//     没探索过 → 深紫黑，几乎看不见底下；
//     探索过但不在视野里 → 半透明的暗雾（看得见地形，看不清动静）；
//     视野内 → 透明。
//   状态变化时慢慢淡入淡出；雾里有缓慢流动的雾气纹理（亚空间的气氛），视野边缘被模糊成柔和的过渡。
// FogManager 在 Awake 里自动挂上本组件（FogManager.useTextureFog），原来的 FogParticleRenderer 自动停用。
// 揭雾范围还是由 FogVisionUpdater.sightRadius（NPC 视野）和 FogTerminalGate（终端机）决定。
// ============================================================================
public class FogOfWarRenderer : MonoBehaviour
{
    public static FogOfWarRenderer Instance { get; private set; }

    [Header("颜色（a = 浓度）")]
    public Color unexploredColor = new Color(0.035f, 0.03f, 0.07f, 1f);
    public Color exploredColor = new Color(0.06f, 0.05f, 0.12f, 0.55f);
    [Tooltip("雾气纹理的亮色")]
    public Color mistColor = new Color(0.32f, 0.27f, 0.48f, 1f);

    [Header("雾气流动")]
    [Range(0f, 1f)] public float mistStrength = 0.35f;
    [Tooltip("雾气纹理大小（越小团越大）")]
    public float mistScale = 0.08f;
    [Tooltip("雾气飘动速度（小格 / 秒）")]
    public Vector2 mistSpeed = new Vector2(1.2f, 0.4f);

    [Header("过渡")]
    [Tooltip("明暗变化速度（每秒变多少浓度）")]
    public float fadeSpeed = 2.5f;
    [Tooltip("视野边缘模糊几次（每次 3×3）")]
    [Range(0, 4)] public int blurPasses = 2;

    [Header("渲染")]
    public int sortingOrder = 100;          // 盖在地图 / NPC 之上
    [Tooltip("多久刷新一次贴图（秒）")]
    public float updateInterval = 0.08f;
    [Tooltip("地图外多画几格雾（盖住边上的围墙外面）")]
    public int margin = 4;

    GridManager _grid;
    Texture2D _tex;
    Color32[] _px;
    float[] _a, _b, _c;
    float[] _n1, _n2;          // 预先算好的可平铺噪声（每次刷新只查表，不重新算噪声）
    const int Tile = 64, Lattice = 16; // 噪声表 64×64，一个噪声格 = 16 个样本
    int _tw, _th;
    float _timer, _time;
    bool _first = true;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        FogParticleRenderer.Suppressed = true; // 不再用粒子雾
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        // 场景里已经跑起来的粒子雾（万一它先 Start 了）一起关掉
        foreach (FogParticleRenderer p in FindObjectsByType<FogParticleRenderer>(FindObjectsSortMode.None))
        {
            p.enabled = false;
            Transform t = p.transform.Find("FogParticles");
            if (t != null) t.gameObject.SetActive(false);
        }
        Build();
    }

    void Build()
    {
        _grid = GridManager.Instance;
        if (_grid == null) { Debug.LogError("[FogOfWarRenderer] 找不到 GridManager"); return; }
        _tw = _grid.gridWidth + margin * 2;
        _th = _grid.gridHeight + margin * 2;
        int n = _tw * _th;
        _a = new float[n];
        _b = new float[n];
        _c = new float[n];
        _px = new Color32[n];
        _n1 = MakeNoise(7331);
        _n2 = MakeNoise(9157);
        _tex = new Texture2D(_tw, _th, TextureFormat.RGBA32, false);
        _tex.name = "FogOfWar";
        _tex.filterMode = FilterMode.Bilinear; // 柔和边缘
        _tex.wrapMode = TextureWrapMode.Clamp;

        GameObject go = new GameObject("FogOfWar");
        go.transform.SetParent(transform, false);
        float cs = _grid.cellSize;
        go.transform.position = new Vector3(_grid.origin.x - margin * cs, _grid.origin.y - margin * cs, 0f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(_tex, new Rect(0, 0, _tw, _th), Vector2.zero, 1f / cs, 0, SpriteMeshType.FullRect);
        sr.sortingOrder = sortingOrder;
        Refresh(0f);
    }

    void LateUpdate()
    {
        if (_tex == null || FogManager.Instance == null) return;
        _timer += Time.unscaledDeltaTime;
        if (_timer < updateInterval) return;
        float dt = _timer;
        _timer = 0f;
        Refresh(dt);
    }

    float TargetAlpha(FogState s)
    {
        switch (s)
        {
            case FogState.Visible: return 0f;
            case FogState.Explored: return exploredColor.a;
            default: return unexploredColor.a;
        }
    }

    void Refresh(float dt)
    {
        FogManager fm = FogManager.Instance;
        if (fm == null) return;
        int W = _grid.gridWidth, H = _grid.gridHeight;
        _time += dt;
        float step = fadeSpeed * dt;

        // 1. 每格的目标浓度 → 慢慢靠过去（地图外的边：取最近一格的状态）
        for (int ty = 0; ty < _th; ty++)
        {
            int cy = Mathf.Clamp(ty - margin, 0, H - 1);
            for (int tx = 0; tx < _tw; tx++)
            {
                int cx = Mathf.Clamp(tx - margin, 0, W - 1);
                int i = ty * _tw + tx;
                float target = TargetAlpha(fm.GetState(cx, cy));
                if (_first) _a[i] = target;
                else if (_a[i] < target) _a[i] = Mathf.Min(target, _a[i] + step);
                else if (_a[i] > target) _a[i] = Mathf.Max(target, _a[i] - step);
            }
        }
        _first = false;

        // 2. 模糊：视野边缘变成柔和的过渡
        System.Array.Copy(_a, _b, _a.Length);
        for (int p = 0; p < blurPasses; p++) Blur3(_b, _c);

        // 3. 上色 + 流动的雾气
        float ox = _time * mistSpeed.x, oy = _time * mistSpeed.y;
        float ea = Mathf.Max(0.001f, exploredColor.a), ua = Mathf.Max(ea + 0.001f, unexploredColor.a);
        for (int ty = 0; ty < _th; ty++)
            for (int tx = 0; tx < _tw; tx++)
            {
                int i = ty * _tw + tx;
                float a = _b[i];
                if (a < 0.004f) { _px[i] = new Color32(0, 0, 0, 0); continue; }
                float k = Mathf.Clamp01((a - ea) / (ua - ea));       // 0 = 探索过的暗雾，1 = 没探索过
                float r = Mathf.Lerp(exploredColor.r, unexploredColor.r, k);
                float g = Mathf.Lerp(exploredColor.g, unexploredColor.g, k);
                float b = Mathf.Lerp(exploredColor.b, unexploredColor.b, k);
                float nz = Sample(_n1, (tx + ox) * mistScale * Lattice, (ty + oy) * mistScale * Lattice);
                float nz2 = Sample(_n2, (tx - ox * 0.6f) * mistScale * 1.7f * Lattice, (ty + oy * 1.3f) * mistScale * 1.7f * Lattice);
                float mist = Mathf.Clamp01((nz * 0.65f + nz2 * 0.35f - 0.35f) * 1.6f) * mistStrength; // 一缕一缕的亮雾
                r = Mathf.Lerp(r, mistColor.r, mist);
                g = Mathf.Lerp(g, mistColor.g, mist);
                b = Mathf.Lerp(b, mistColor.b, mist);
                float alpha = Mathf.Clamp01(a * (1f - mistStrength * 0.12f + mist * 0.35f)); // 没探索过的地方基本不透明
                _px[i] = new Color32((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), (byte)(alpha * 255f));
            }
        _tex.SetPixels32(_px);
        _tex.Apply(false);
    }

    // 可平铺的两层值噪声（周期 = Tile 个样本）。
    static float[] MakeNoise(int seed)
    {
        float[] t = new float[Tile * Tile];
        for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++)
                t[y * Tile + x] = Periodic(seed, x, y, Lattice) * 0.67f + Periodic(seed + 101, x, y, Lattice / 2) * 0.33f;
        return t;
    }

    static float Periodic(int seed, int x, int y, int spacing)
    {
        int period = Tile / spacing;
        float fx = x / (float)spacing, fy = y / (float)spacing;
        int x0 = (int)fx, y0 = (int)fy;
        float u = fx - x0, v = fy - y0;
        u = u * u * (3f - 2f * u); v = v * v * (3f - 2f * v);
        int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
        float a = TerrainNoise.Hash01(seed, x0, y0), b = TerrainNoise.Hash01(seed, x1, y0);
        float c = TerrainNoise.Hash01(seed, x0, y1), d = TerrainNoise.Hash01(seed, x1, y1);
        float ab = a + (b - a) * u, cd = c + (d - c) * u;
        return ab + (cd - ab) * v;
    }

    // 查噪声表（双线性，循环平铺）。
    static float Sample(float[] t, float u, float v)
    {
        int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
        float fx = u - x0, fy = v - y0;
        x0 = ((x0 % Tile) + Tile) % Tile; y0 = ((y0 % Tile) + Tile) % Tile;
        int x1 = (x0 + 1) % Tile, y1 = (y0 + 1) % Tile;
        float a = t[y0 * Tile + x0], b = t[y0 * Tile + x1], c = t[y1 * Tile + x0], d = t[y1 * Tile + x1];
        float ab = a + (b - a) * fx, cd = c + (d - c) * fx;
        return ab + (cd - ab) * fy;
    }

    // 3×3 平均（横一遍竖一遍）。
    void Blur3(float[] v, float[] tmp)
    {
        int W = _tw, H = _th;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                float l = v[x > 0 ? i - 1 : i], rr = v[x < W - 1 ? i + 1 : i];
                tmp[i] = (l + v[i] + rr) / 3f;
            }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                float d = tmp[y > 0 ? i - W : i], u = tmp[y < H - 1 ? i + W : i];
                v[i] = (d + tmp[i] + u) / 3f;
            }
    }
}
