using UnityEngine;

// 建造物定义（ScriptableObject）：每一种建造物（墙、箱子、工作台…）是一个独立的资产文件。
// 在 Project 里右键 Create > 亚空间SLG > 建造物 就能新建一个，然后在 Inspector 里填属性。
// 好处：加新建造物 = 建个资产填数据，不用改代码（数据驱动）。
[CreateAssetMenu(fileName = "新建造物", menuName = "亚空间SLG/建造物")]
public class BuildableData : ScriptableObject
{
    [Header("基础")]
    public string id = "wall";          // 唯一编号（程序用，如 "wall" / "box" / "workbench"）
    public string displayName = "墙";    // 显示名（配合"同名加序号"逻辑，如 墙、墙01）
    public Sprite sprite;               // 建造物的图片素材（实体和半透明预览都用它）
    public GameObject prefab;           // 实例化用的预制体（拖入 Wall.prefab / Box.prefab）

    [Header("网格占用（单位：小格，见 GridManager.subdivision）")]
    [Tooltip("勾上 = 按图片大小自动算占几格（1 单位大的图 = 3×3 小格）。想做细长 / 特殊形状的建筑就关掉，用下面的宽高。")]
    public bool autoFootprint = true;
    [Tooltip("占几格宽（小格）。只在 autoFootprint 关掉时用。")]
    public int width = 1;
    [Tooltip("占几格高（小格）。只在 autoFootprint 关掉时用。")]
    public int height = 1;

    // 实际占地（小格数）。cellSize = GridManager.cellSize。
    public Vector2Int FootprintSize(float cellSize)
    {
        if (!autoFootprint || cellSize <= 0f) return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
        Vector2 sz = Vector2.zero;
        Vector3 scale = Vector3.one;
        SpriteRenderer sr = prefab != null ? prefab.GetComponent<SpriteRenderer>() : null;
        if (prefab != null) scale = prefab.transform.localScale;
        if (sr != null && sr.drawMode != SpriteDrawMode.Simple) sz = sr.size;
        else if (sr != null && sr.sprite != null) sz = sr.sprite.bounds.size;
        else if (sprite != null) sz = sprite.bounds.size;
        if (sz == Vector2.zero) return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
        int w = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sz.x * scale.x) / cellSize - 0.25f)); // 差一点点满格也算一格（0.9 单位的图 = 3 格）
        int h = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sz.y * scale.y) / cellSize - 0.25f));
        return new Vector2Int(w, h);
    }

    [Header("通行")]
    [Tooltip("是否阻挡移动。true=墙（NPC无法穿过，建造后需重新烘焙NavMesh）；false=箱子（可穿过）。")]
    public bool blocksMovement = true;  // 是否阻挡移动 —— 决定建造后挂不挂 NavigationModifier(Not Walkable)

    [Header("建造")]
    [Tooltip("NPC 到达后，实际建造需要的秒数。")]
    public float buildTime = 1.5f;      // 建造耗时（NPC到达后建几秒才完成）

    [Tooltip("建造需要的材料。以后接 craft 系统用，现在先留空。")]
    public MaterialCost[] requiredMaterials;  // 需要的材料（以后用，现在可空）

    [Header("多语言")]
    [Tooltip("日文显示名（可空：空则显示中文 displayName）。")]
    public string displayNameJa = "";

    // 按当前语言取显示名（只用于 UI 显示；格子占用记录等逻辑仍用 displayName）。
    public string LocalizedName =>
        (LocalizationManager.Instance != null && LocalizationManager.Instance.current == Language.Japanese
         && !string.IsNullOrEmpty(displayNameJa)) ? displayNameJa : displayName;

    [Header("分类与解锁（AI 终端机）")]
    [Tooltip("建造菜单里归在哪一类：基础/防御/生产/电力/生活。")]
    public BuildCategory buildCategory = BuildCategory.Basic;
    [Tooltip("需要终端机几产才能建：0=不需要终端机（只有终端机本身用0），1=一产，2=二产，3=三产。")]
    [Range(0, 3)] public int requiredTier = 0;
    [Tooltip("需要研究的科技 id（空=不需要）。")]
    public string requiredTechId = "";
    [Tooltip("唯一建筑（整局只能建一座，如 AI 终端机）。")]
    public bool uniqueBuild = false;

    [Header("功能分类（以后扩展）")]
    [Tooltip("建造物类型，方便以后按类型做逻辑（装饰/储物/工作台/防御…）。")]
    public BuildableCategory category = BuildableCategory.Decoration;
}

// 材料消耗：建造需要的一种材料 + 数量（以后接 craft/物品系统）。
[System.Serializable]
public class MaterialCost
{
    public string materialName;  // 材料名（如"木材"）
    public int amount;           // 需要数量
}

// 建造物类型（以后按类型做不同逻辑，现在先占位）。
public enum BuildableCategory
{
    Decoration,  // 装饰
    Storage,     // 储物
    Workbench,   // 工作台
    Defense,     // 防御（如墙）
    Other        // 其它
}
