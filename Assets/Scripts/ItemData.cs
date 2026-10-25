using UnityEngine;

// 物品的种类。决定物品能怎么用、NPC 自主行为时会不会去找它。
public enum ItemCategory
{
    Food,      // 食物：NPC 饿了会自己吃（蛋白营养块、净化水）
    Medical,   // 医疗：回复 SAN / 生命 / 体力（镇静剂、急救包、兴奋剂）
    Material,  // 材料：建造用，不能直接"使用"（纳米物质）
    Special    // 特殊：亚空间特产，以后接电力等系统（异变结晶）
}

// 一种物品的定义（数据驱动）：名字、说明、图标、效果数值。
// 仓库里只存"物品 id + 数量"（ItemStack），具体是什么东西、有什么效果，都在这里查。
// 好处：加新物品 = 在 ItemDatabase 的列表里加一条，不用改规则代码。
[System.Serializable]
public class ItemData
{
    [Header("基础")]
    public string id = "ration";        // 唯一编号（程序用，存进仓库的就是它）。用英文，喂给 AI 时不会把中文混进日语提示词
    public string nameZh = "蛋白营养块"; // 中文名
    public string nameJa = "タンパク栄養ブロック"; // 日文名
    [TextArea] public string descZh;    // 中文说明（仓库面板里显示）
    [TextArea] public string descJa;    // 日文说明
    public Sprite icon;                 // 图标（可空；以后美术阶段拖进来）
    public ItemCategory category = ItemCategory.Food;

    [Header("使用效果（正数=增加，负数=减少）")]
    public float hungerDelta = 0f;      // 饥饿变化（饥饿越高越饿，所以食物填负数）
    public float energyDelta = 0f;      // 体力变化
    public float sanityDelta = 0f;      // 理智变化
    public float healthDelta = 0f;      // 生命变化

    // 能不能直接对 NPC 使用：材料和特殊物品不能，食物和医疗可以。
    public bool IsUsable => category == ItemCategory.Food || category == ItemCategory.Medical;

    // 快速构造一条物品定义（ItemDatabase 填默认物品时用）。
    public static ItemData Make(string id, string zh, string ja, ItemCategory cat,
                                float hunger, float energy, float sanity, float health,
                                string descZh, string descJa)
    {
        return new ItemData
        {
            id = id, nameZh = zh, nameJa = ja, category = cat,
            hungerDelta = hunger, energyDelta = energy, sanityDelta = sanity, healthDelta = health,
            descZh = descZh, descJa = descJa
        };
    }
}
