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
// 【别的地方加灯】用 CreatePoint（NPC 头灯 / 身上的光、终端机、枪口火光都用它）。
// 【旧管线下】什么都不做。
// ============================================================================
public class WorldLighting : MonoBehaviour
{
    public static WorldLighting Instance { get; private set; }

    [Header("环境光（亚空间没有太阳：很暗）")]
    public Color ambientColor = new Color(0.55f, 0.6f, 0.85f);    // 环境光颜色（偏冷的蓝紫）
    [Range(0f, 1f)] public float ambientIntensity = 0.12f;        // 环境光亮度（0 = 全黑，1 = 和以前一样亮）

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
        if (scene == "AnimTest") w.ambientIntensity = 0.5f;             // 动画测试场景亮一点（要看清动作）
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
