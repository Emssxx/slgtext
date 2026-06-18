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

    [Header("网格占用")]
    [Tooltip("占几格宽。当前建造逻辑只支持 1×1，此字段先留作以后扩展多格用。")]
    public int width = 1;               // 占几格宽（以后多格用，现在固定 1）
    [Tooltip("占几格高。当前建造逻辑只支持 1×1。")]
    public int height = 1;              // 占几格高（以后多格用，现在固定 1）

    [Header("通行")]
    [Tooltip("是否阻挡移动。true=墙（NPC无法穿过，建造后需重新烘焙NavMesh）；false=箱子（可穿过）。")]
    public bool blocksMovement = true;  // 是否阻挡移动 —— 决定建造后挂不挂 NavigationModifier(Not Walkable)

    [Header("建造")]
    [Tooltip("NPC 到达后，实际建造需要的秒数。")]
    public float buildTime = 1.5f;      // 建造耗时（NPC到达后建几秒才完成）

    [Tooltip("建造需要的材料。以后接 craft 系统用，现在先留空。")]
    public MaterialCost[] requiredMaterials;  // 需要的材料（以后用，现在可空）

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
