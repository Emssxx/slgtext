using UnityEngine;

// ============================================================
// AI 终端机相关的"纯数据定义"：建造分类、合成配方、科技、终端机升级、任务。
// 只放数据结构，不放逻辑（逻辑在 ProgressionRules / TerminalWorkshop）。
// 具体的合成表、科技树、升级内容，填在 TerminalDatabase 的列表里（后续补充）。
// 材料统一复用 BuildableData.cs 里的 MaterialCost（materialName 填物品 id，如 "nano"）。
// ============================================================

// 建造物五大分类（和多数 SLG 一样）。决定建造菜单里归在哪一栏。
public enum BuildCategory
{
    Basic,      // 基础：终端机、储物箱等营地基础设施
    Defense,    // 防御：墙、防御工事
    Production, // 生产：加工、转换类设施
    Power,      // 电力：发电机、电网（吸收亚空间风暴能量）
    Living      // 生活：住所、休息设施（影响 SAN / 体力回复）
}

// 一条合成配方：消耗若干材料 → 产出一种物品。由 AI 终端机的"建造台"功能执行。
[System.Serializable]
public class RecipeData
{
    public string id = "r_ration";          // 配方编号（程序用）
    public string outputItemId = "ration";  // 产出物品 id（对应 ItemDatabase）
    public int outputCount = 1;             // 产出数量
    public MaterialCost[] inputs;           // 消耗的材料（可为空 = 终端机凭空重构，如纳米物质）
    public float craftTime = 10f;           // 合成耗时（秒）
    [Range(1, 3)] public int requiredTier = 1; // 需要终端机几产（1=一产）
    public string requiredTechId = "";      // 需要的科技 id（空 = 不需要）
}

// 一项科技：研究完成后解锁某些建筑 / 配方 / 终端机升级。
// "解锁什么"不写在科技里，而是由建筑、配方、升级各自声明"我需要哪项科技"——
// 这样加新内容时只改那一条数据，科技树本身不用动。
[System.Serializable]
public class TechData
{
    public string id = "t_example";         // 科技编号（程序用）
    public string nameZh = "示例科技";       // 中文名
    public string nameJa = "サンプル技術";   // 日文名
    [TextArea] public string descZh;        // 中文说明（写它解锁了什么）
    [TextArea] public string descJa;        // 日文说明
    [Range(1, 3)] public int requiredTier = 1; // 终端机需要几产才能研究
    public string[] prerequisites;          // 前置科技 id（全部研究完才能研究这项）
    public MaterialCost[] cost;             // 研究消耗的材料
    public float researchTime = 20f;        // 研究耗时（秒）
}

// 终端机的一次升级（一产→二产、二产→三产）。最高三产。
[System.Serializable]
public class TerminalUpgradeStep
{
    [Range(2, 3)] public int toLevel = 2;   // 升级到几产
    public MaterialCost[] cost;             // 升级消耗的材料
    public float upgradeTime = 30f;         // 升级耗时（秒）
    public string requiredTechId = "";      // 需要的科技 id（空 = 不需要）
    [TextArea] public string noteZh;        // 升级带来什么（文字说明，具体效果后续补充）
    [TextArea] public string noteJa;
}

// 终端机正在处理/排队的一项任务。
public enum TerminalJobType { Craft, Research, Upgrade }

[System.Serializable]
public class TerminalJob
{
    public TerminalJobType type;  // 任务类型：合成 / 研究 / 升级
    public string targetId;       // 目标：配方 id / 科技 id / 升级到的等级（"2"、"3"）
    public float duration;        // 总耗时（秒）
    public float remaining;       // 剩余时间（秒）

    // 进度 0~1（UI 显示进度用）。
    public float Progress01 => duration <= 0f ? 1f : Mathf.Clamp01(1f - remaining / duration);
}
