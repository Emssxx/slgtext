using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;   // 2D 光（Light2D）
using UnityEngine.SceneManagement;

// ============================================================================
// 世界光照（URP 2D 光）：亚空间没有太阳，整体很暗，只有灯、终端机、开枪的火光照得亮。
//
// 【自动创建】每个场景开始时自动加一个"WorldLighting"物体，上面是全局光（环境光）。
//   主菜单不加；场景里已经自己放了全局光（Global Light 2D）就用自己的，不再加。
//   运行时选中 WorldLighting 物体，在 Inspector 里调环境光颜色 / 亮度，马上生效。
// 【别的地方加灯】用 CreatePoint（NPC 头灯 / 身上的光、终端机、枪口火光都用它）；要投影子的灯再调 EnableShadows。
// 【阴影】Shadow Mode 选：
//   Silhouette（默认）：剪影投影（SilhouetteShadows）——人 / 敌人 / 建筑 / 树石的影子 = 它们自己的边缘剪影，按灯的位置、高度真实投影，带半影和渐变。
//   Classic（旧）：建筑 / 树 / 石头加 Unity 投影体（形状 = 底部占地）；人 / 敌人加脚下拉长的一条（GroundShadow）。
// 【旧管线下】什么都不做。
// ============================================================================
public class WorldLighting : MonoBehaviour
{
    public static WorldLighting Instance { get; private set; }

    [Header("环境光（亚空间没有太阳：很暗）")]
    public Color ambientColor = new Color(0.55f, 0.6f, 0.85f);    // 环境光颜色（偏冷的蓝紫）
    [Range(0f, 1f)] public float ambientIntensity = 0.02f;        // 环境光亮度（0 = 全黑，1 = 和以前一样亮；恐怖游戏标准：没灯的地方基本看不见；原来 0.04，再调暗一半）

    Light2D _global;                   // 全局光
    static bool _hooked;               // 已经挂上"换场景时自动创建"了

    // 进游戏时：当前场景加一个，以后每换一个场景再加一个
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!_hooked) { _hooked = true; SceneManager.sceneLoaded += (s, m) => Ensure(); }
        Ensure();
    }

    // 确保当前场景有环境光
    public static void Ensure()
    {
        if (!RenderMats.IsURP || Instance != null) return;               // 旧管线 / 已经有了
        string scene = SceneManager.GetActiveScene().name;
        if (scene.Contains("Menu")) return;                              // 主菜单不加
        foreach (Light2D l in FindObjectsByType<Light2D>(FindObjectsSortMode.None))
            if (l.lightType == Light2D.LightType.Global) return;         // 场景里自己放了全局光：用自己的
        WorldLighting w = new GameObject("WorldLighting").AddComponent<WorldLighting>();
        if (scene.StartsWith("AnimTest")) w.ambientIntensity = 0.5f;    // 动画测试场景（AnimTest、AnimTest2）亮一点（要看清动作）
    }

    void Awake()
    {
        Instance = this;
        _global = gameObject.AddComponent<Light2D>();
        _global.lightType = Light2D.LightType.Global;
        AllLayers(_global);
        Apply();
    }

    void Update() { Apply(); }         // 运行时在 Inspector 里改了马上生效

    void Apply()
    {
        if (_global == null) return;
        _global.color = ambientColor;
        _global.intensity = ambientIntensity;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ============ 阴影 ============
    // 建筑（有占地的）、树、石头会自动加"投影体"：形状 = 它们底部占地的一块，被手电筒 / 终端机 / 枪火照到就往后投影子。
    // 投影体和它的图在同一个物体上，并且"用图的轮廓"，所以物体自己的图不会被自己的影子盖黑。

    public enum ShadowMode
    {
        [InspectorName("剪影投影（拟真）")] Silhouette,
        [InspectorName("旧：投影体 + 脚下一条")] Classic,
    }

    [Header("阴影")]
    public bool shadows = true;                                   // 建筑 / 树 / 石头投影子
    public ShadowMode shadowMode = ShadowMode.Silhouette;         // 影子用哪一套（剪影投影的细节参数在运行时的 SilhouetteShadows 物体上调）
    [Range(0f, 1f)] public float shadowStrength = 0.9f;           // 影子多黑（灯的阴影强度）
    [Range(0f, 1f)] public float shadowSoftness = 0.3f;           // 影子边缘多软（新版 Unity 才有）

    float _scanTimer;                                             // 多久找一次新出现的建筑 / 树石

    void LateUpdate()
    {
        if (!shadows) return;
        _scanTimer -= Time.deltaTime;
        if (_scanTimer > 0f) return;
        _scanTimer = 1f;                                          // 每秒找一次（新盖的建筑、新长的树也会加上）
        ShadowLights.RemoveAll(l => l == null);                   // 删掉已经没了的灯
        bool silhouette = shadowMode == ShadowMode.Silhouette;
        foreach (Light2D l in ShadowLights) SetMember(l, "shadowsEnabled", !silhouette);   // 剪影模式：不用 Unity 自带的阴影（免得两套叠在一起）
        if (silhouette) { SilhouetteShadows.Ensure(); return; }   // 剪影投影自己找要投影子的东西
        float cell = GridManager.Instance != null ? GridManager.Instance.cellSize : 1f / 3f;   // 1 小格多长
        foreach (BuildingFootprint f in BuildingFootprint.All)
            if (f != null) AddCaster(f.gameObject, new Vector2(f.size.x * cell * 0.95f, f.size.y * cell * 0.95f));   // 建筑：整块占地
        foreach (NpcView v in NpcView.GetAllViews())              // 人：脚下的影子（GroundShadow）
            if (v != null && v.GetComponent<GroundShadow>() == null) v.gameObject.AddComponent<GroundShadow>();
        foreach (Enemy e in Enemy.All)                            // 敌人：同上，宽度跟着体型
        {
            if (e == null || e.GetComponent<GroundShadow>() != null) continue;
            GroundShadow gs = e.gameObject.AddComponent<GroundShadow>();
            gs.width = 0.32f * Mathf.Max(0.5f, e.Def != null ? e.Def.scale : 1f);
            gs.sortingOrder = 0;                                  // 敌人身体是 1，影子画在它下面
        }
        ShadowLights.RemoveAll(l => l == null);                   // 删掉已经没了的灯
        foreach (ResourceNode n in ResourceNode.All)
        {
            if (n == null || n.kind == ResourceKind.Grass) continue;                 // 草不投影子
            float s = n.size * cell;
            Vector2 box = n.kind == ResourceKind.Tree ? new Vector2(s * 0.35f, s * 0.3f) : new Vector2(s * 0.8f, s * 0.6f);   // 树：只有树干那一小块；石头：大半块
            AddCaster(n.gameObject, box);
        }
    }

    static System.Type _casterType;                               // ShadowCaster2D（用名字找，免得版本不同编译不过）

    // 给一个物体加投影体：以物体位置为中心的矩形（世界单位）。已经有了就跳过
    static void AddCaster(GameObject go, Vector2 box)
    {
        if (_casterType == null) _casterType = typeof(Light2D).Assembly.GetType("UnityEngine.Rendering.Universal.ShadowCaster2D");
        if (_casterType == null || go.GetComponent(_casterType) != null) return;
        Component c = go.AddComponent(_casterType);
        Vector3 ls = go.transform.lossyScale;                     // 形状是物体本地坐标：除掉物体的缩放
        float hx = box.x * 0.5f / Mathf.Max(0.0001f, Mathf.Abs(ls.x)), hy = box.y * 0.5f / Mathf.Max(0.0001f, Mathf.Abs(ls.y));
        Vector3[] path = { new Vector3(-hx, -hy, 0f), new Vector3(hx, -hy, 0f), new Vector3(hx, hy, 0f), new Vector3(-hx, hy, 0f) };
        SetEnum(c, "m_ShadowCastingSource", "ShapeEditor");       // 用下面给的形状（不用整张图的轮廓）
        SetMember(c, "m_ShapePath", path);                        // 形状
        SetMember(c, "m_ShapePathHash", Random.Range(int.MinValue, int.MaxValue));   // 告诉它形状变了，重新生成
        SetMember(c, "useRendererSilhouette", true);              // 自己的图不被自己的影子盖住
        SetMember(c, "selfShadows", false);
        SetMember(c, "castsShadows", true);
    }

    // 会投影子的灯（人 / 敌人脚下的影子按它们算）
    public static readonly System.Collections.Generic.List<Light2D> ShadowLights = new System.Collections.Generic.List<Light2D>();
    static readonly System.Collections.Generic.Dictionary<Light2D, float> _heights = new System.Collections.Generic.Dictionary<Light2D, float>();

    // 这盏灯离地多高（EnableShadows 时给过就用给的，没给用 fallback）。挂在人身上的灯剪影投影会自己算，不看这个
    public static float LightHeight(Light2D l, float fallback)
    {
        return l != null && _heights.TryGetValue(l, out float h) ? h : fallback;
    }

    // 给一盏灯打开阴影（手电筒、终端机、枪火用）。height = 灯离地多高（单位；不给 = 剪影投影按默认值 / 挂在谁身上自动算）
    public static void EnableShadows(Light2D l, float height = -1f)
    {
        if (l == null) return;
        if (!ShadowLights.Contains(l)) ShadowLights.Add(l);       // 登记：人 / 敌人的影子会参考它
        if (height > 0f) _heights[l] = height;
        if (Instance == null || !Instance.shadows) return;
        if (Instance.shadowMode == ShadowMode.Silhouette) { SilhouetteShadows.Ensure(); return; }   // 剪影模式：不开 Unity 自带的阴影
        SetMember(l, "shadowsEnabled", true);
        SetMember(l, "shadowIntensity", Instance.shadowStrength);
        SetMember(l, "shadowSoftness", Instance.shadowSoftness);
    }

    // 按名字把一个枚举字段设成某个值（找不到就算了）
    static void SetEnum(object o, string field, string valueName)
    {
        FieldInfo f = o.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null || !f.FieldType.IsEnum) return;
        try { f.SetValue(o, System.Enum.Parse(f.FieldType, valueName)); } catch (System.ArgumentException) { }
    }

    // 按名字设一个属性 / 字段（找不到就算了；不同版本的 URP 名字可能不一样）
    static void SetMember(object o, string name, object value)
    {
        if (o == null) return;
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        System.Type t = o.GetType();
        PropertyInfo p = t.GetProperty(name, F);
        if (p != null && p.CanWrite && p.PropertyType.IsAssignableFrom(value.GetType())) { p.SetValue(o, value); return; }
        FieldInfo f = t.GetField(name, F);
        if (f != null && f.FieldType.IsAssignableFrom(value.GetType())) f.SetValue(o, value);
    }

    // 让一盏灯照所有排序层（代码加的灯默认可能一层都不照）
    static FieldInfo _layersField;
    public static void AllLayers(Light2D l)
    {
        if (l == null) return;
        if (_layersField == null) _layersField = typeof(Light2D).GetField("m_ApplyToSortingLayers", BindingFlags.NonPublic | BindingFlags.Instance);
        if (_layersField == null) return;                                // 找不到这个字段：就按它自己的默认
        SortingLayer[] layers = SortingLayer.layers;
        int[] ids = new int[layers.Length];
        for (int i = 0; i < layers.Length; i++) ids[i] = layers[i].id;
        _layersField.SetValue(l, ids);
    }

    // 加一盏点光 / 聚光：angle = 360 是一圈都亮，小于 360 是朝自己"上方"照的扇形（聚光）。旧管线返回 null
    public static Light2D CreatePoint(string name, Transform parent, Color color, float intensity, float radius, float angle)
    {
        if (!RenderMats.IsURP) return null;
        GameObject go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        Light2D l = go.AddComponent<Light2D>();
        l.lightType = Light2D.LightType.Point;
        AllLayers(l);
        SetPoint(l, color, intensity, radius, angle);
        l.falloffIntensity = 0.6f;                                       // 边缘渐暗的快慢
        return l;
    }

    // 改一盏点光 / 聚光的颜色、亮度、范围、角度
    public static void SetPoint(Light2D l, Color color, float intensity, float radius, float angle)
    {
        if (l == null) return;
        l.color = color;
        l.intensity = intensity;
        l.pointLightOuterRadius = Mathf.Max(0.01f, radius);
        l.pointLightInnerRadius = Mathf.Max(0f, radius * 0.15f);         // 中间一小圈最亮
        l.pointLightOuterAngle = Mathf.Clamp(angle, 1f, 360f);
        l.pointLightInnerAngle = Mathf.Clamp(angle * 0.6f, 0f, 360f);    // 扇形中间亮、两边渐暗
    }
}
