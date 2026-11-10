using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;   // SortingGroup：终端机的几层图当成一个整体排序（始终在 NPC 下面）

// ============================================================================
// TerminalVisual —— AI 终端机的外观动画（纯表现，不影响任何逻辑）。
//
// 【它做什么】
//   · 缸里的大脑缓慢上下浮动；连着大脑的软管跟着伸缩（上面的软管固定在盖子上，下面的固定在缸底）。
//   · 随机大小的气泡从缸底往上冒，只在液体范围里显示，冒到盖子下面消失。
//   · 施工中（还没建成）保持一张静态图；建成后才开始动。
//
// 【它怎么拼】素材在 Resources/BuildingArt/Terminal/（从你的 PSD 分层导出）。
//   终端机本体的图片换成"后层"（缸体 + 底座），其他几层作为子物体叠在上面：
//   后层 → 大脑 → 前层A（机器零件）→ 上软管 → 下软管 → 前层B（配线）→ 气泡 → 顶层（盖子、屏幕光、天线）。
//
// 【它不改什么】不改预制体：由 TerminalController 在 Awake 里自动挂上。占地、碰撞、建造流程都不变。
// ============================================================================
[DisallowMultipleComponent]
public class TerminalVisual : MonoBehaviour
{
    [Header("大脑浮动")]
    public float brainBob = 70f;                          // 上下浮动多远（原图像素；终端机 1 单位 = 2394 像素）
    public float brainPeriod = 4.5f;                      // 浮动一个来回要几秒

    [Header("气泡")]
    public Vector2 bubbleInterval = new Vector2(0.2f, 0.6f);   // 隔多久冒一个（秒，随机）
    public Vector2 bubbleSize = new Vector2(0.35f, 1.15f);     // 大小（倍数，1 = 你画的那个大气泡）
    public Vector2 bubbleSpeed = new Vector2(120f, 300f);      // 上浮速度（原图像素 / 秒，随机）
    public int maxBubbles = 14;                                // 同时最多几个

    [Header("灯光（URP 2D 光；建成后缸里发光照亮周围）")]
    public Color lightColor = new Color(0.45f, 0.95f, 0.9f);   // 光的颜色（青色）
    public float lightIntensity = 1.2f;                        // 亮度（0 = 关）
    public float lightRadius = 5f;                             // 照多远（单位）

    // ---------------- 固定换算（和导出素材时一致，不要改） ----------------
    const float PPU_C = 2394f;                            // 原图上多少像素 = 1 单位（整台机器底部宽度 = 1 单位 = 3 小格）
    static readonly Vector2 ROOT = new Vector2(1384, 1595);   // 终端机本体的轴心在原图上的位置（占地正中）
    const float BUBBLE_X0 = 560f, BUBBLE_X1 = 2100f;      // 气泡从哪段宽度里冒出来（原图 x）
    const float BUBBLE_Y0 = 1560f, BUBBLE_Y1 = 1660f;     // 从多高冒出来（缸底附近，原图 y，向下为正）
    const float BUBBLE_END = 560f, BUBBLE_FADE = 120f;    // 冒到哪里消失（盖子下沿）/ 消失前多远开始变淡
    const string ART = "BuildingArt/Terminal/";           // 素材文件夹（Resources 里）

    // ---------------- 运行时引用 ----------------
    SpriteRenderer _root;                  // 终端机本体的图片
    Sprite _fullSprite;                    // 本体原来的整张图（施工中显示它）
    Sprite _backSprite;                    // 后层（动画时本体显示它）
    TerminalController _ctrl;              // 读"建成了没有"
    GameObject _parts;                     // 所有动画子物体的父物体（施工中整个关掉）
    Transform _brain;                      // 大脑
    Vector3 _brainBase;                    // 大脑的原始位置
    WarpMesh _hosesTop, _hoseBottom;       // 连着大脑的软管（会伸缩）
    readonly List<SpriteRenderer> _plain = new List<SpriteRenderer>();   // 不动的几层（跟着本体变色 / 变透明）
    Transform _bubbleRoot;                 // 气泡的父物体
    Sprite _bubbleSprite;                  // 气泡图
    readonly List<Bubble> _bubbles = new List<Bubble>();                // 气泡池（用完的重复利用）
    bool _ok;                              // 素材齐不齐（不齐就保持静态图）
    bool _animating;                       // 现在在不在播动画
    UnityEngine.Rendering.Universal.Light2D _light;   // 缸里的光（URP 2D 光；旧管线下没有）
    float _t, _nextBubble;                 // 动画时间 / 距离下一个气泡还有多久

    class Bubble
    {
        public SpriteRenderer sr;          // 气泡的图片
        public bool alive;                 // 正在冒（false = 空闲，等着重复利用）
        public float x, y, v, s, ph, age;  // 位置（原图像素）/ 上浮速度 / 大小 / 左右晃的相位 / 冒了多久
    }

    // ============================================================================
    void Awake()
    {
        _root = GetComponent<SpriteRenderer>();                       // 本体图片
        _ctrl = GetComponent<TerminalController>();                   // 终端机控制器
        if (_root == null) return;                                    // 没有图片就什么都不做
        _fullSprite = _root.sprite;                                   // 记下原来的整张图
        _backSprite = Resources.Load<Sprite>(ART + "Back");           // 后层
        _bubbleSprite = Resources.Load<Sprite>(ART + "Bubble");       // 气泡
        Sprite brain = Resources.Load<Sprite>(ART + "Brain");         // 大脑
        if (_backSprite == null || _bubbleSprite == null || brain == null)
        {
            Debug.LogWarning("[TerminalVisual] 找不到 Resources/BuildingArt/Terminal/ 里的素材，终端机保持静态图。");
            return;                                                   // 素材不齐：不做动画
        }

        // 整体排序：几层图一起排，整体在 NPC（顺序 1）下面
        SortingGroup group = GetComponent<SortingGroup>();
        if (group == null) group = gameObject.AddComponent<SortingGroup>();
        group.sortingLayerID = _root.sortingLayerID;                  // 和本体同一个排序层
        group.sortingOrder = _root.sortingOrder;                      // 同一个顺序（建筑 = 0）

        _parts = new GameObject("TerminalParts");                     // 动画子物体的父物体
        _parts.transform.SetParent(transform, false);
        _brain = Plain("Brain", brain, 1331f, 1120f, 1).transform;    // 大脑（顺序 1）
        _brainBase = _brain.localPosition;                            // 记下原始位置
        Plain("FrontA", Resources.Load<Sprite>(ART + "FrontA"), 1362.5f, 1492f, 2);   // 前层A：机器零件 + 外面的软管
        _hosesTop = new WarpMesh("HosesTop", _parts.transform, Resources.Load<Sprite>(ART + "HosesTop"), 1284f, 830f, 3,
                                 cy => Smooth((cy - 560f) / 260f));                   // 上软管：盖子那头不动，越往下越跟着大脑
        _hoseBottom = new WarpMesh("HoseBottom", _parts.transform, Resources.Load<Sprite>(ART + "HoseBottom"), 1024f, 1615.5f, 4,
                                   cy => 1f - Smooth((cy - 1450f) / 350f));           // 下软管：大脑那头跟着动，缸底那头不动
        Plain("FrontB", Resources.Load<Sprite>(ART + "FrontB"), 1396.5f, 2108.5f, 5); // 前层B：配线

        _bubbleRoot = new GameObject("Bubbles").transform;            // 气泡的父物体
        _bubbleRoot.SetParent(_parts.transform, false);
        GameObject maskGo = new GameObject("LiquidMask");             // 液体遮罩：气泡只在缸里的液体范围显示
        maskGo.transform.SetParent(_parts.transform, false);
        maskGo.transform.localPosition = CanvasToLocal(1341.5f, 1053.5f);
        SpriteMask mask = maskGo.AddComponent<SpriteMask>();          // Unity 自带的精灵遮罩（只管同一个 SortingGroup 里的图）
        mask.sprite = Resources.Load<Sprite>(ART + "LiquidMask");

        Plain("Top", Resources.Load<Sprite>(ART + "Top"), 1252.5f, 1104f, 7);         // 顶层：盖子、屏幕光、天线

        _ok = true;                                                   // 素材齐了
        SetAnimating(false);                                          // 先当作没建成（下一帧 Update 再按实际情况切换）
    }

    // 建一个不动的图层（顺序 order，位置按原图上的轴心算）
    SpriteRenderer Plain(string name, Sprite sprite, float cx, float cy, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(_parts.transform, false);              // 放在动画父物体下
        go.transform.localPosition = CanvasToLocal(cx, cy);           // 原图位置 → 本地坐标
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;                                      // 在 SortingGroup 里的前后顺序
        _plain.Add(sr);                                               // 记下，之后跟着本体变色
        return sr;
    }

    // 原图坐标（像素，y 向下）→ 本地坐标（单位，y 向上）
    static Vector3 CanvasToLocal(float x, float y) { return new Vector3((x - ROOT.x) / PPU_C, -(y - ROOT.y) / PPU_C, 0f); }

    // 0~1 之间平滑过渡（两头慢、中间快）
    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // 切换：动画（本体显示后层 + 子物体打开）/ 静态（本体显示整张图 + 子物体关掉）
    void SetAnimating(bool on)
    {
        _animating = on;
        if (on && _light == null && _brain != null)                   // 第一次建成：在缸（大脑）的位置加一盏灯；施工中跟着动画子物体一起关掉
        {
            _light = WorldLighting.CreatePoint("TerminalLight", _brain.parent, lightColor, lightIntensity, lightRadius, 360f);
            if (_light != null) _light.transform.localPosition = _brainBase;
            WorldLighting.EnableShadows(_light);                       // 终端机的光照到建筑 / 树石会投影子
        }
        _root.sprite = on ? _backSprite : _fullSprite;                // 换本体的图
        _parts.SetActive(on);                                         // 子物体开 / 关
    }

    // ============================================================================
    void Update()
    {
        if (!_ok) return;                                             // 素材不齐：一直静态
        bool built = _ctrl == null || _ctrl.isBuilt;                  // 建成了吗
        if (built != _animating) SetAnimating(built);                 // 状态变了就切换
        if (!_animating) return;
        if (_light != null)                                           // Inspector 里改的灯光参数马上生效
        {
            _light.enabled = lightIntensity > 0f;
            WorldLighting.SetPoint(_light, lightColor, lightIntensity, lightRadius, 360f);
        }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;                                         // 暂停（建造模式）时不动
        _t += dt;

        Color tint = _root.color;                                     // 本体现在的颜色（变透明 / 变色时其他层跟着）
        foreach (SpriteRenderer sr in _plain) if (sr != null) sr.color = tint;

        // 大脑：正弦上下浮动（dy 是原图像素，向下为正）
        float dy = brainBob * Mathf.Sin(2f * Mathf.PI * _t / Mathf.Max(0.5f, brainPeriod));
        _brain.localPosition = _brainBase + new Vector3(0f, -dy / PPU_C, 0f);
        _hosesTop.Apply(dy, tint);                                    // 上软管跟着伸缩
        _hoseBottom.Apply(dy, tint);                                  // 下软管跟着伸缩

        // 气泡：到时间就冒一个新的
        _nextBubble -= dt;
        if (_nextBubble <= 0f)
        {
            _nextBubble = Random.Range(bubbleInterval.x, bubbleInterval.y);   // 下一个随机隔多久
            Spawn();
        }
        foreach (Bubble b in _bubbles)
        {
            if (!b.alive) continue;                                   // 空闲的不管
            b.age += dt;                                              // 冒了多久
            b.y -= b.v * dt;                                          // 往上浮（原图 y 变小）
            if (b.y < BUBBLE_END) { b.alive = false; b.sr.enabled = false; continue; }   // 到盖子下面：消失
            float x = b.x + 15f * Mathf.Sin(b.ph + b.age * 2f * Mathf.PI * 1.3f);         // 左右轻轻晃
            b.sr.transform.localPosition = CanvasToLocal(x, b.y);
            float a = Mathf.Min(1f, b.age / 0.3f) * Mathf.Min(1f, (b.y - BUBBLE_END) / BUBBLE_FADE);  // 刚冒出来 / 快消失时变淡
            b.sr.color = new Color(tint.r, tint.g, tint.b, tint.a * a);
        }
    }

    // 冒一个气泡（优先用空闲的，没有就新建，最多 maxBubbles 个）
    void Spawn()
    {
        Bubble b = null;
        foreach (Bubble x in _bubbles) if (!x.alive) { b = x; break; }   // 找一个空闲的
        if (b == null)
        {
            if (_bubbles.Count >= maxBubbles) return;                  // 已经够多了
            GameObject go = new GameObject("Bubble");
            go.transform.SetParent(_bubbleRoot, false);
            b = new Bubble { sr = go.AddComponent<SpriteRenderer>() };
            b.sr.sprite = _bubbleSprite;
            b.sr.sortingOrder = 6;                                     // 在配线前面、盖子后面
            b.sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;   // 只在液体遮罩里显示
            _bubbles.Add(b);
        }
        b.alive = true;
        b.x = Random.Range(BUBBLE_X0, BUBBLE_X1);                      // 随机横向位置
        b.y = Random.Range(BUBBLE_Y0, BUBBLE_Y1);                      // 缸底附近
        b.v = Random.Range(bubbleSpeed.x, bubbleSpeed.y);              // 随机上浮速度
        b.s = Random.Range(bubbleSize.x, bubbleSize.y);                // 随机大小
        b.ph = Random.Range(0f, 6.28f);                                // 随机晃动相位
        b.age = 0f;
        b.sr.transform.localScale = new Vector3(b.s, b.s, 1f);         // 大小
        b.sr.transform.localPosition = CanvasToLocal(b.x, b.y);
        b.sr.color = new Color(1f, 1f, 1f, 0f);                        // 先透明，Update 里淡入
        b.sr.enabled = true;
    }

    // ============================================================================
    // 软管：用一条网格画（横着切成很多条），每一条按它在原图上的高度决定跟着大脑动多少
    // ============================================================================
    class WarpMesh
    {
        readonly Mesh mesh;
        readonly Vector3[] basePos, pos;      // 原始顶点 / 当前顶点
        readonly float[] weight;              // 每个顶点跟着大脑动多少（0 = 不动，1 = 和大脑一起动）
        readonly Color[] colors;
        static Material _mat;                 // 所有软管共用一个材质

        public WarpMesh(string name, Transform parent, Sprite sprite, float cx, float cy, int order, System.Func<float, float> weightOfCanvasY)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = CanvasToLocal(cx, cy);          // 放在它的轴心位置
            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (_mat == null) _mat = RenderMats.MeshSpriteMaterial();    // 2D 光照：软管是建筑的一部分，受光（旧管线 = Sprites/Default）
            mr.sharedMaterial = _mat;
            mr.sortingOrder = order;                                     // 在 SortingGroup 里的前后顺序
            if (sprite == null) return;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetTexture("_MainTex", sprite.texture);                  // 用软管的贴图
            mr.SetPropertyBlock(mpb);

            const int ROWS = 20;                                         // 竖着分 20 条，每条单独上下偏移
            Rect r = sprite.rect; Vector2 piv = sprite.pivot; float ppu = sprite.pixelsPerUnit;
            Texture2D tex = sprite.texture;
            basePos = new Vector3[(ROWS + 1) * 2]; pos = new Vector3[basePos.Length];
            weight = new float[basePos.Length]; colors = new Color[basePos.Length];
            Vector2[] uv = new Vector2[basePos.Length];
            int[] tris = new int[ROWS * 6];
            for (int i = 0; i <= ROWS; i++)
            {
                float v = (float)i / ROWS;                               // 0 = 底边，1 = 顶边
                float y = (v * r.height - piv.y) / ppu;                  // 本地 y（单位）
                float w = weightOfCanvasY(cy - y * PPU_C);               // 换回原图高度，算它跟着动多少
                for (int j = 0; j < 2; j++)                              // j = 0 左边，1 右边
                {
                    int idx = i * 2 + j;
                    float x = (j * r.width - piv.x) / ppu;
                    basePos[idx] = new Vector3(x, y, 0f);
                    weight[idx] = w;
                    uv[idx] = new Vector2((r.x + j * r.width) / tex.width, (r.y + v * r.height) / tex.height);
                }
                if (i < ROWS)
                {
                    int t = i * 6, a = i * 2;                            // 两个三角形拼一个小矩形
                    tris[t] = a; tris[t + 1] = a + 2; tris[t + 2] = a + 3;
                    tris[t + 3] = a; tris[t + 4] = a + 3; tris[t + 5] = a + 1;
                }
            }
            mesh = new Mesh { name = name };
            mesh.vertices = basePos; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
        }

        // dy：大脑往下移了多少（原图像素）；tint：颜色（跟着本体）
        public void Apply(float dy, Color tint)
        {
            if (mesh == null) return;
            for (int i = 0; i < basePos.Length; i++)
            {
                pos[i] = basePos[i] + new Vector3(0f, -dy / PPU_C * weight[i], 0f);  // 按权重跟着大脑上下
                colors[i] = tint;
            }
            mesh.vertices = pos;
            mesh.colors = colors;
            mesh.RecalculateBounds();                                    // 伸缩后重新算范围（不然可能被错误裁掉）
        }
    }
}
