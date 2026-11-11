using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;   // 2D 光（Light2D）

// ============================================================================
// 剪影投影（URP 2D 光）：人 / 敌人 / ABA / 建筑 / 树 / 石头被会投影子的灯（手电筒、终端机、枪火）照到时，
// 把它们"现在这一帧的样子"拍成一张剪影，再按真实的投影几何铺到地上——影子就是人物的边缘剪影，不是地上一团。
//
// 【几何（3/4 俯视角）】画面上的"往上"= 高度。灯在画面上的位置也带着高度：挂在人身上的灯（头灯 / 枪灯）
//   的落地点 = 灯正下方的脚底高度，灯高 = 它比脚底高多少；没挂在人身上的灯（终端机、枪火）用 defaultLightHeight。
//   剪影上高 z 的一点，落到地上离脚底 = 灯到脚的距离 × z / (灯高 − z)（相似三角形）：
//     · 离灯越远影子越长；灯比人矮（手电筒）时上半身的影子会一直拉到光照不到的地方（软上限）。
//     · 点光源的光是散开的，所以影子越往外越宽（透视扇形），离灯越近越夸张。
//     · 灯从下面照 → 影子往上（正立）；从上面照 → 影子往下（倒立）；从侧面照 → 斜着。左右不会镜像。
// 【拟真的渐变】
//   · 半影：影子贴脚的地方边缘锐利，越往外越模糊（模糊半径 = 灯的大小 × 影子离脚的距离 ÷ 灯到人的距离）。
//   · 深浅：影子只挡住"这盏灯"的光——深浅 = 这盏灯的亮度 ÷（环境光 + 所有灯）；几盏灯同时照，每条影子都变淡。
//   · 衰减：影子越往外，那里本来就越暗（离灯远 / 出了手电光束），影子跟着淡掉，到光照不到的地方正好消失。
//   · 脚下接触阴影（人 / 敌人）：脚底一小团很柔的暗，被照亮时才看得见。
// 【遮挡】灯和人之间隔着墙 / 石头 / 树（Ballistics 的格子）就不投这盏灯的影子。
// 【性能】只处理镜头附近、正被灯照着的东西；人每帧拍一次（动作跟得上），建筑 / 树石每秒最多拍一次；
//   一个东西最多 maxPerCaster 条影子（取最亮的几盏灯）；剪影图 3 秒没用就释放。
// 【着色器】Resources/Shaders/SilhouetteShadow.shader（铺影子）+ SilhouetteCapture.shader（拍剪影）。找不到就整套不工作（只打一次警告）。
// 【开关】WorldLighting → Shadow Mode = Silhouette（默认）/ Classic（旧的：Unity 投影体 + 脚下拉长的一条）。
// ============================================================================
[DefaultExecutionOrder(10000)]   // 在所有动画（NpcRig / NpcRigB 的 Update / LateUpdate）之后拍剪影
public class SilhouetteShadows : MonoBehaviour
{
    public static SilhouetteShadows Instance { get; private set; }

    [Header("几何")]
    [Tooltip("没挂在人身上的灯（终端机、枪火）离地多高（单位）")]
    public float defaultLightHeight = 0.6f;
    [Tooltip("影子长度倍数（1 = 按相似三角形算）")]
    public float lengthScale = 1f;
    [Tooltip("影子最长多长（单位）；实际还会被灯的范围限制")]
    public float maxLength = 6f;
    [Tooltip("影子分几段（越多透视越顺）")]
    [Range(4, 24)] public int rows = 12;

    [Header("深浅 / 渐变")]
    [Range(0f, 1f)] public float strength = 0.85f;        // 被一盏灯单独照着时影子最深多深
    [Range(0f, 1f)] public float fadeAlong = 0.35f;       // 影子尖比脚边再淡多少（地面反光、散射）
    public float lightSize = 0.12f;                       // 灯有多大（越大半影越宽、越模糊）
    public float baseBlur = 0.012f;                       // 贴脚处的模糊（单位）
    [Range(0f, 1f)] public float contactStrength = 0.45f; // 脚下接触阴影多深
    public Color shadowColor = new Color(0.02f, 0.02f, 0.05f, 1f);   // 影子的颜色（略带冷色）

    [Header("范围 / 性能")]
    [Range(1, 4)] public int maxPerCaster = 3;            // 一个东西最多几条影子
    public int characterRes = 128;                        // 人的剪影图多大（像素）
    public int objectRes = 192;                           // 建筑 / 树石剪影图最大多大
    public bool occlusion = true;                         // 灯和人之间有墙就不投影子
    public bool objects = true;                           // 建筑 / 树 / 石头也投影子
    public bool hideFixedTreeShadows = true;              // 剪影模式下藏起树自带的固定方向斜影（亚空间没有太阳）
    public int sortingOrder = -1;                         // 影子的排序（地面层：人 / 建筑下面，草 / 地砖上面）
    public bool invertCapture = false;                    // 影子上下颠倒时勾上（个别显卡 / 平台）

    // —— 灯 ——
    struct LightInfo
    {
        public Light2D l;
        public Vector2 ground;     // 落地点
        public float h, r, intensity, halfOuter, halfInner;
        public bool spot;
        public Vector2 beam;
        public Transform owner;    // 挂在谁身上（这个人不给自己投影子）
    }

    // —— 投影子的东西 ——
    class Caster
    {
        public GameObject root;
        public bool character;
        public float scale = 1f;
        public bool tree;
        public Vector2 basePoint;                 // 脚底 / 树根（世界）
        public float xMin, xMax, yBottom, yTop;   // 剪影框（相对脚底）
        public RenderTexture rt;
        public int res;
        public float litAt = -99f, capturedAt = -99f, rendsAt = -99f;
        public readonly List<Renderer> rends = new List<Renderer>();
        public Slot[] slots;
        public MeshRenderer contact;
        public Vector2 lastW = Vector2.right;
        public bool hasW;
        public bool empty;
    }

    class Slot
    {
        public GameObject go;
        public MeshRenderer mr;
        public Mesh mesh;
        public Vector3[] v;
        public Color[] c;
        public Vector2[] uv, uv1;
    }

    readonly Dictionary<GameObject, Caster> _casters = new Dictionary<GameObject, Caster>();
    readonly List<LightInfo> _lights = new List<LightInfo>();
    readonly List<Caster> _toCapture = new List<Caster>();
    readonly List<GameObject> _dead = new List<GameObject>();
    readonly Dictionary<Sprite, Mesh> _spriteMeshes = new Dictionary<Sprite, Mesh>();
    readonly int[] _pick = new int[4];
    readonly float[] _pickK = new float[4];
    CommandBuffer _cmd;
    MaterialPropertyBlock _mpb;
    Material _shadowMat, _captureMat, _contactMat;
    float _scanTimer;
    bool _broken;
    static Mesh _quad;
    static Texture2D _blob;

    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int RectId = Shader.PropertyToID("_SilCaptureRect");
    static readonly int FlipId = Shader.PropertyToID("_SilCaptureFlip");

    public static SilhouetteShadows Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("SilhouetteShadows").AddComponent<SilhouetteShadows>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        transform.position = Vector3.zero;
        Shader s = Shader.Find("SLG/SilhouetteShadow");
        Shader c = Shader.Find("Hidden/SLG/SilhouetteCapture");
        if (s == null || c == null)
        {
            Debug.LogWarning("[SilhouetteShadows] 找不到着色器 SLG/SilhouetteShadow 或 Hidden/SLG/SilhouetteCapture（要放在 Assets/Resources/Shaders/ 里），剪影投影不工作。");
            _broken = true;
            return;
        }
        _shadowMat = new Material(s) { name = "SilhouetteShadow" };
        _contactMat = new Material(s) { name = "SilhouetteContact" };
        _captureMat = new Material(c) { name = "SilhouetteCapture" };
        _cmd = new CommandBuffer { name = "SilhouetteCapture" };
        _mpb = new MaterialPropertyBlock();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (Caster c in _casters.Values) Release(c, true);
        _casters.Clear();
        foreach (Mesh m in _spriteMeshes.Values) if (m != null) Destroy(m);
        if (_cmd != null) _cmd.Release();
    }

    bool Active
    {
        get
        {
            WorldLighting w = WorldLighting.Instance;
            return !_broken && RenderMats.IsURP && w != null && w.shadows && w.shadowMode == WorldLighting.ShadowMode.Silhouette;
        }
    }

    // ============ 每帧 ============

    void LateUpdate()
    {
        if (!Active) { HideAll(); return; }
        _scanTimer -= Time.unscaledDeltaTime;
        if (_scanTimer <= 0f) { _scanTimer = 1f; Scan(); }
        CollectLights();

        Camera cam = Camera.main;
        Rect view = cam != null ? ViewRect(cam, 3f) : new Rect(-1e5f, -1e5f, 2e5f, 2e5f);
        _toCapture.Clear();
        _dead.Clear();
        foreach (KeyValuePair<GameObject, Caster> kv in _casters)
        {
            Caster c = kv.Value;
            if (kv.Key == null) { _dead.Add(kv.Key); Release(c, true); continue; }
            int n = 0;
            if (_lights.Count > 0 && kv.Key.activeInHierarchy)
            {
                c.basePoint = BasePoint(c);
                if (view.Contains(c.basePoint)) n = PickLights(c);
            }
            if (n == 0)
            {
                Hide(c);
                if (c.rt != null && Time.time - c.litAt > 3f) Release(c, false);   // 3 秒没用：释放剪影图
                continue;
            }
            c.litAt = Time.time;
            bool need = c.rt == null || c.character || Time.time - c.capturedAt > 1f;   // 人每帧拍；建筑 / 树石每秒一次
            if (need) _toCapture.Add(c);
            Draw(c, n);
        }
        foreach (GameObject g in _dead) _casters.Remove(g);
        if (_toCapture.Count > 0) Capture();
    }

    // ============ 找东西 ============

    void Scan()
    {
        foreach (NpcView v in NpcView.GetAllViews())
            if (v != null) Add(v.gameObject, true, 1f, false);
        foreach (Enemy e in Enemy.All)
            if (e != null) Add(e.gameObject, true, e.Def != null ? e.Def.scale : 1f, false);
        foreach (AbaOperative o in AbaOperative.All)
            if (o != null) Add(o.gameObject, true, 1f, false);
        if (objects)
        {
            foreach (BuildingFootprint f in BuildingFootprint.All)
                if (f != null) Add(f.gameObject, false, 1f, false);
            foreach (ResourceNode r in ResourceNode.All)
            {
                if (r == null || r.kind == ResourceKind.Grass) continue;
                Add(r.gameObject, false, 1f, r.kind == ResourceKind.Tree);
                if (hideFixedTreeShadows) HideFixedShadow(r.transform);
            }
        }
        // 旧的脚下影子（GroundShadow）关掉，免得两套影子叠在一起
        foreach (GroundShadow g in FindObjectsByType<GroundShadow>(FindObjectsSortMode.None))
            if (g != null && g.enabled) { g.enabled = false; foreach (SpriteRenderer sr in g.GetComponentsInChildren<SpriteRenderer>(true)) if (sr.name == "GroundShadow") sr.enabled = false; }
    }

    void Add(GameObject go, bool character, float scale, bool tree)
    {
        if (_casters.ContainsKey(go)) return;
        Caster c = new Caster { root = go, character = character, scale = Mathf.Max(0.3f, scale), tree = tree };
        c.slots = new Slot[4];
        if (character)
        {
            float s = c.scale;
            c.xMin = -0.75f * s; c.xMax = 0.75f * s;
            c.yBottom = -0.05f; c.yTop = 1.35f * s;
            c.res = characterRes;
        }
        _casters.Add(go, c);
    }

    // 树自带的固定方向斜影（ResourceNode.AddShadow 生成的 "Shadow" 子物体）：剪影模式下藏起来（只关显示，不删）
    static void HideFixedShadow(Transform t)
    {
        Transform s = t.Find("Shadow");
        if (s != null && s.gameObject.activeSelf) s.gameObject.SetActive(false);
    }

    Vector2 BasePoint(Caster c)
    {
        Vector3 p = c.root.transform.position;
        if (c.character) return new Vector2(p.x, p.y - 0.453125f);        // 人 / 敌人：物体中心在脚底上方 0.453
        if (c.tree) return p;                                              // 树：原点 = 树根
        if (c.capturedAt > 0f) return c.basePoint;                         // 建筑 / 石头：拍剪影时按图的底边定好了
        Bounds b;
        if (!ObjectBounds(c, out b)) return p;
        return new Vector2(b.center.x, b.min.y + b.size.y * 0.06f);
    }

    // ============ 灯 ============

    void CollectLights()
    {
        _lights.Clear();
        List<Light2D> list = WorldLighting.ShadowLights;
        for (int i = 0; i < list.Count; i++)
        {
            Light2D l = list[i];
            if (l == null || !l.isActiveAndEnabled || l.intensity <= 0.01f) continue;
            LightInfo li = new LightInfo { l = l, r = Mathf.Max(0.05f, l.pointLightOuterRadius), intensity = l.intensity };
            Vector2 pos = l.transform.position;
            li.owner = OwnerOf(l.transform, out float feetY);
            if (li.owner != null) { li.h = Mathf.Clamp(pos.y - feetY, 0.1f, 2f); li.ground = new Vector2(pos.x, feetY); }   // 挂在人身上：落地点 = 正下方的脚底
            else
            {
                float hh = WorldLighting.LightHeight(l, defaultLightHeight);
                li.h = Mathf.Max(0.1f, hh);
                li.ground = new Vector2(pos.x, pos.y - li.h);
            }
            li.spot = l.pointLightOuterAngle < 359f;
            if (li.spot)
            {
                li.beam = l.transform.up;
                li.halfOuter = l.pointLightOuterAngle * 0.5f;
                li.halfInner = Mathf.Min(li.halfOuter - 0.5f, l.pointLightInnerAngle * 0.5f);
            }
            _lights.Add(li);
        }
    }

    // 灯挂在哪个人 / 敌人身上（返回那个人的根物体，feetY = 他的脚底高度）
    static Transform OwnerOf(Transform t, out float feetY)
    {
        feetY = 0f;
        for (Transform p = t.parent; p != null; p = p.parent)
        {
            if (p.GetComponent<NpcView>() != null || p.GetComponent<Enemy>() != null || p.GetComponent<AbaOperative>() != null)
            {
                feetY = p.position.y - 0.453125f;
                return p;
            }
        }
        return null;
    }

    // 这盏灯在 p 点有多亮（0 = 照不到）
    static float LightAt(LightInfo li, Vector2 p)
    {
        Vector2 d = p - li.ground;
        float dist = d.magnitude;
        if (dist >= li.r) return 0f;
        float k = li.intensity * Smooth01(1f - dist / li.r);
        if (li.spot && dist > 0.02f)
        {
            float ang = Vector2.Angle(li.beam, d);
            if (ang >= li.halfOuter) return 0f;
            if (ang > li.halfInner) k *= Smooth01(1f - (ang - li.halfInner) / Mathf.Max(0.5f, li.halfOuter - li.halfInner));
        }
        return k;
    }

    // 挑照得最亮的几盏灯，返回几盏
    int PickLights(Caster c)
    {
        int max = Mathf.Clamp(maxPerCaster, 1, 4);
        for (int i = 0; i < max; i++) { _pick[i] = -1; _pickK[i] = 0f; }
        float size = Mathf.Max(c.xMax - c.xMin, c.yTop - c.yBottom);
        for (int i = 0; i < _lights.Count; i++)
        {
            LightInfo li = _lights[i];
            if (li.owner != null && li.owner.gameObject == c.root) continue;                // 自己身上的灯不给自己投影子
            if (li.l.transform.IsChildOf(c.root.transform)) continue;                       // 终端机自己的灯同理
            float dist = Vector2.Distance(li.ground, c.basePoint);
            if (dist > li.r + size) continue;
            float k = LightAt(li, c.basePoint);
            if (!c.character && k <= 0f)                                                    // 大东西：底边照不到，看看中间 / 两边
                k = Mathf.Max(LightAt(li, c.basePoint + new Vector2(c.xMin * 0.8f, 0f)), LightAt(li, c.basePoint + new Vector2(c.xMax * 0.8f, 0f)));
            k *= Smooth01(Mathf.InverseLerp(0.06f, 0.3f, dist));                            // 灯贴在身上：没有像样的影子
            if (k <= 0.005f) continue;
            if (occlusion && c.character && Ballistics.Instance != null && !Ballistics.LineClear(li.ground, c.basePoint)) continue;   // 中间隔着墙
            for (int j = 0; j < max; j++)
            {
                if (k <= _pickK[j]) continue;
                for (int m = max - 1; m > j; m--) { _pick[m] = _pick[m - 1]; _pickK[m] = _pickK[m - 1]; }
                _pick[j] = i; _pickK[j] = k;
                break;
            }
        }
        int n = 0;
        while (n < max && _pick[n] >= 0) n++;
        return n;
    }

    // ============ 铺影子 ============

    void Draw(Caster c, int n)
    {
        if (c.empty || c.rt == null && c.capturedAt > 0f) { Hide(c); return; }
        float ambient = WorldLighting.Instance != null ? WorldLighting.Instance.ambientIntensity : 0.05f;
        float sum = 0f;
        for (int i = 0; i < n; i++) sum += _pickK[i];
        float denom = Mathf.Max(0.05f, ambient + 0.04f) + sum;
        for (int i = 0; i < 4; i++)
        {
            if (i >= n) { if (c.slots[i] != null) c.slots[i].mr.enabled = false; continue; }
            Slot s = GetSlot(c, i);
            float a = strength * _pickK[i] / denom;                           // 只挡住这盏灯的那份光
            Build(c, s, _lights[_pick[i]], a);
            s.mr.enabled = c.rt != null;
        }
        if (c.character) DrawContact(c, Mathf.Clamp01(sum * 1.5f));
    }

    // 按投影几何生成一条影子的网格（rows 段，每段两个点）
    void Build(Caster c, Slot s, LightInfo li, float alpha)
    {
        int rowsN = Mathf.Clamp(rows, 4, 24);
        EnsureArrays(s, rowsN);
        Vector2 F = c.basePoint;
        Vector2 d = F - li.ground;
        float dist = Mathf.Max(0.001f, d.magnitude);
        Vector2 dir = d / dist;
        float cap = Mathf.Clamp(li.r - dist, 0.15f, Mathf.Max(0.15f, maxLength));   // 最多拉到光照不到的地方

        // 影子的"横"方向：和投影方向垂直，并且尽量和画面的 x 同向（左右不镜像）；正侧面照时保持上一帧的朝向，不突然翻
        Vector2 w = new Vector2(1f, 0f) - dir * dir.x;
        if (w.magnitude > 0.25f) { w.Normalize(); c.lastW = w; c.hasW = true; }
        else
        {
            Vector2 perp = new Vector2(-dir.y, dir.x);
            if (c.hasW && Vector2.Dot(perp, c.lastW) < 0f) perp = -perp;
            w = perp;
            c.lastW = w;
            c.hasW = true;
        }

        float hL = li.h;
        float baseAtt = Mathf.Max(0.05f, LightAt(li, F));
        float spanX = c.xMax - c.xMin;
        for (int i = 0; i <= rowsN; i++)
        {
            float t = i / (float)rowsN;
            float z = (c.yBottom + t * (c.yTop - c.yBottom)) * lengthScale;
            float rho = z <= 0f ? 0f : (z >= hL * 0.97f ? 1e4f : z / (hL - z));     // 相似三角形
            float g = cap * (1f - Mathf.Exp(-dist * rho / cap));                  // 软上限：越接近上限越慢
            float fan = Mathf.Min(4f, 1f + g / dist);                             // 点光源：影子越往外越宽
            Vector2 center = F + dir * g;
            Vector2 left = center + w * (c.xMin * fan);
            Vector2 right = center + w * (c.xMax * fan);

            // 深浅：这一段那里本来有多亮（离灯远 / 出了光束 → 影子跟着淡）× 往外再淡一点
            float att = Mathf.Clamp01(LightAt(li, center) / baseAtt);
            float a = alpha * att * (1f - fadeAlong * t);
            if (i == 0) a *= 0.85f;
            // 半影：贴脚锐利，越往外越模糊
            float pen = baseBlur + lightSize * g / Mathf.Max(0.25f, dist);
            float blurUv = Mathf.Min(0.15f, pen / Mathf.Max(0.05f, spanX * fan));

            int k = i * 2;
            s.v[k] = new Vector3(left.x, left.y, 0f);
            s.v[k + 1] = new Vector3(right.x, right.y, 0f);
            s.uv[k] = new Vector2(0f, t);
            s.uv[k + 1] = new Vector2(1f, t);
            s.uv1[k] = s.uv1[k + 1] = new Vector2(blurUv, 0f);
            s.c[k] = s.c[k + 1] = new Color(1f, 1f, 1f, a);
        }
        s.mesh.vertices = s.v;
        s.mesh.uv = s.uv;
        s.mesh.uv2 = s.uv1;
        s.mesh.colors = s.c;
        s.mesh.RecalculateBounds();
        _mpb.Clear();
        _mpb.SetTexture(MainTexId, c.rt != null ? (Texture)c.rt : Texture2D.blackTexture);
        _mpb.SetColor(ColorId, shadowColor);
        s.mr.SetPropertyBlock(_mpb);
        s.mr.sortingOrder = sortingOrder;
        SortLow(s.mr);
    }

    void EnsureArrays(Slot s, int rowsN)
    {
        int nv = (rowsN + 1) * 2;
        if (s.v != null && s.v.Length == nv) return;
        s.v = new Vector3[nv]; s.c = new Color[nv]; s.uv = new Vector2[nv]; s.uv1 = new Vector2[nv];
        int[] tri = new int[rowsN * 6];
        for (int i = 0; i < rowsN; i++)
        {
            int a = i * 2, t = i * 6;
            tri[t] = a; tri[t + 1] = a + 2; tri[t + 2] = a + 1;
            tri[t + 3] = a + 1; tri[t + 4] = a + 2; tri[t + 5] = a + 3;
        }
        s.mesh.Clear();
        s.mesh.vertices = s.v;
        s.mesh.triangles = tri;
    }

    Slot GetSlot(Caster c, int i)
    {
        if (c.slots[i] != null && c.slots[i].go != null) return c.slots[i];
        GameObject go = new GameObject("SilShadow");
        go.transform.SetParent(transform, false);
        Slot s = new Slot { go = go, mesh = new Mesh { name = "SilShadow" } };
        s.mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = s.mesh;
        s.mr = go.AddComponent<MeshRenderer>();
        s.mr.sharedMaterial = _shadowMat;
        s.mr.sortingOrder = sortingOrder;
        c.slots[i] = s;
        return s;
    }

    // 脚下接触阴影（很柔的一小团；被照亮时才看得见）
    void DrawContact(Caster c, float lit)
    {
        if (c.contact == null)
        {
            GameObject go = new GameObject("SilContact");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            c.contact = go.AddComponent<MeshRenderer>();
            _contactMat.SetTexture(MainTexId, Blob());
            c.contact.sharedMaterial = _contactMat;
        }
        float s = c.scale;
        c.contact.transform.position = new Vector3(c.basePoint.x, c.basePoint.y, 0f);
        c.contact.transform.localScale = new Vector3(0.5f * s, 0.17f * s, 1f);
        _mpb.Clear();
        _mpb.SetTexture(MainTexId, Blob());
        _mpb.SetColor(ColorId, new Color(shadowColor.r, shadowColor.g, shadowColor.b, contactStrength * lit));
        c.contact.SetPropertyBlock(_mpb);
        c.contact.sortingOrder = sortingOrder;
        c.contact.enabled = lit > 0.01f;
        SortLow(c.contact);
    }

    // 排序：和草 / 地砖同一层时，画在它们上面（按 y 排序时 y 越小越靠前 → 把排序用的包围盒中心放到很下面）
    static void SortLow(Renderer r)
    {
        Bounds b = r.bounds;
        r.bounds = new Bounds(new Vector3(b.center.x, -50000f, 0f), new Vector3(Mathf.Max(b.size.x, 1f), 200000f, 1f));
    }

    // ============ 拍剪影 ============

    void Capture()
    {
        _cmd.Clear();
        float flip = (SystemInfo.graphicsUVStartsAtTop ? -1f : 1f) * (invertCapture ? -1f : 1f);
        foreach (Caster c in _toCapture)
        {
            RefreshRenderers(c);
            if (!c.character && !FitObjectBox(c)) { c.empty = true; continue; }
            int res = c.character ? characterRes : ObjectRes(c);
            if (c.rt == null || c.res != res)
            {
                if (c.rt != null) { c.rt.Release(); Destroy(c.rt); }
                c.res = res;
                float aspect = (c.xMax - c.xMin) / Mathf.Max(0.01f, c.yTop - c.yBottom);
                int wpx = Mathf.Clamp(Mathf.RoundToInt(res * Mathf.Min(1f, aspect)), 16, res);
                int hpx = Mathf.Clamp(Mathf.RoundToInt(res * Mathf.Min(1f, 1f / Mathf.Max(0.01f, aspect))), 16, res);
                c.rt = new RenderTexture(wpx, hpx, 0, RenderTextureFormat.ARGB32) { name = "Silhouette", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                c.rt.Create();
            }
            c.capturedAt = Time.time;
            Vector2 F = c.basePoint;
            float cx = F.x + (c.xMin + c.xMax) * 0.5f, cy = F.y + (c.yBottom + c.yTop) * 0.5f;
            float hw = (c.xMax - c.xMin) * 0.5f, hh = (c.yTop - c.yBottom) * 0.5f;
            _cmd.SetRenderTarget(c.rt);
            _cmd.ClearRenderTarget(false, true, Color.clear);
            _cmd.SetGlobalVector(RectId, new Vector4(cx, cy, 1f / Mathf.Max(0.01f, hw), 1f / Mathf.Max(0.01f, hh)));
            _cmd.SetGlobalFloat(FlipId, flip);
            int drawn = 0;
            foreach (Renderer r in c.rends)
            {
                if (!Include(r)) continue;
                if (r is SpriteRenderer sr)
                {
                    Mesh m = SpriteMesh(sr.sprite);
                    if (m == null) continue;
                    Matrix4x4 mtx = sr.transform.localToWorldMatrix * Matrix4x4.Scale(new Vector3(sr.flipX ? -1f : 1f, sr.flipY ? -1f : 1f, 1f));
                    _mpb.Clear();
                    _mpb.SetTexture(MainTexId, sr.sprite.texture != null ? (Texture)sr.sprite.texture : Texture2D.whiteTexture);
                    _mpb.SetColor(ColorId, sr.color);
                    _cmd.DrawMesh(m, mtx, _captureMat, 0, 0, _mpb);
                    drawn++;
                }
                else if (r is MeshRenderer mr)
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    Texture tex = mr.sharedMaterial != null && mr.sharedMaterial.HasProperty(MainTexId) ? mr.sharedMaterial.mainTexture : null;
                    _mpb.Clear();
                    _mpb.SetTexture(MainTexId, tex != null ? tex : Texture2D.whiteTexture);
                    _mpb.SetColor(ColorId, Color.white);
                    _cmd.DrawMesh(mf.sharedMesh, mr.transform.localToWorldMatrix, _captureMat, 0, 0, _mpb);
                    drawn++;
                }
            }
            c.empty = drawn == 0;
        }
        Graphics.ExecuteCommandBuffer(_cmd);
    }

    // 拍进剪影的部件：开着的 SpriteRenderer / MeshRenderer；不要血条、气泡、发光、影子自己、地上的东西（排序 < 0）
    bool Include(Renderer r)
    {
        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
        if (r.sortingOrder < 0) return false;
        Material m = r.sharedMaterial;
        if (m != null)
        {
            if (m == RenderMats.SpriteUnlit || m == _shadowMat || m == _contactMat) return false;
            string sn = m.shader != null ? m.shader.name : "";
            if (sn.Contains("Unlit") || sn.Contains("Additive") || sn.Contains("Particle") || sn.Contains("Text") || sn.Contains("UI/")) return false;
        }
        if (r is SpriteRenderer sr) return sr.sprite != null && sr.color.a > 0.02f;
        return r is MeshRenderer;
    }

    void RefreshRenderers(Caster c)
    {
        if (Time.time - c.rendsAt < (c.character ? 0.5f : 2f) && c.rends.Count > 0) return;
        c.rendsAt = Time.time;
        c.rends.Clear();
        foreach (Renderer r in c.root.GetComponentsInChildren<Renderer>(true))
            if (r is SpriteRenderer || r is MeshRenderer) c.rends.Add(r);
    }

    // 建筑 / 树石：剪影框 = 拍进去的部件的包围盒（多留一点边给模糊）
    bool FitObjectBox(Caster c)
    {
        if (!ObjectBounds(c, out Bounds b)) return false;
        if (!c.tree) c.basePoint = new Vector2(b.center.x, b.min.y + b.size.y * 0.06f);
        Vector2 F = c.basePoint;
        float mx = b.size.x * 0.06f + 0.05f, my = b.size.y * 0.04f + 0.05f;
        c.xMin = b.min.x - F.x - mx; c.xMax = b.max.x - F.x + mx;
        c.yBottom = Mathf.Min(-0.05f, b.min.y - F.y - my); c.yTop = b.max.y - F.y + my;
        return c.xMax - c.xMin > 0.05f && c.yTop - c.yBottom > 0.05f;
    }

    bool ObjectBounds(Caster c, out Bounds b)
    {
        b = new Bounds();
        bool any = false;
        if (c.rends.Count == 0) RefreshRenderers(c);
        foreach (Renderer r in c.rends)
        {
            if (!Include(r)) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }

    int ObjectRes(Caster c)
    {
        float size = Mathf.Max(c.xMax - c.xMin, c.yTop - c.yBottom);
        return Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.RoundToInt(size * 96f)), 64, Mathf.Max(64, objectRes));
    }

    // 精灵的网格（按图的真实轮廓，不是整张方块）；每张图只建一次
    Mesh SpriteMesh(Sprite sp)
    {
        if (sp == null) return null;
        if (_spriteMeshes.TryGetValue(sp, out Mesh m) && m != null) return m;
        Vector2[] sv = sp.vertices;
        ushort[] st = sp.triangles;
        Vector3[] v = new Vector3[sv.Length];
        for (int i = 0; i < sv.Length; i++) v[i] = new Vector3(sv[i].x, sv[i].y, 0f);
        int[] t = new int[st.Length];
        for (int i = 0; i < st.Length; i++) t[i] = st[i];
        m = new Mesh { name = "SilSprite_" + sp.name };
        m.vertices = v;
        m.uv = sp.uv;
        m.triangles = t;
        _spriteMeshes[sp] = m;
        return m;
    }

    // ============ 收拾 ============

    void Hide(Caster c)
    {
        if (c.slots != null) foreach (Slot s in c.slots) if (s != null && s.mr != null) s.mr.enabled = false;
        if (c.contact != null) c.contact.enabled = false;
    }

    void HideAll()
    {
        foreach (Caster c in _casters.Values) Hide(c);
    }

    void Release(Caster c, bool all)
    {
        if (c.rt != null) { c.rt.Release(); Destroy(c.rt); c.rt = null; }
        c.capturedAt = -99f;
        if (!all) return;
        if (c.slots != null)
            foreach (Slot s in c.slots)
                if (s != null) { if (s.mesh != null) Destroy(s.mesh); if (s.go != null) Destroy(s.go); }
        if (c.contact != null) Destroy(c.contact.gameObject);
    }

    // ============ 小工具 ============

    static Rect ViewRect(Camera cam, float margin)
    {
        float h = cam.orthographicSize, w = h * cam.aspect;
        Vector3 p = cam.transform.position;
        return new Rect(p.x - w - margin, p.y - h - margin, (w + margin) * 2f, (h + margin) * 2f);
    }

    static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    static Mesh Quad()
    {
        if (_quad != null) return _quad;
        _quad = new Mesh { name = "SilQuad" };
        _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        _quad.uv2 = new[] { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero };
        _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        _quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        return _quad;
    }

    // 接触阴影用的柔边椭圆（r 通道 = 浓度）
    static Texture2D Blob()
    {
        if (_blob != null) return _blob;
        const int n = 64;
        _blob = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SilContactBlob", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                _blob.SetPixel(x, y, new Color(a, a, a, a));
            }
        _blob.Apply();
        return _blob;
    }
}
