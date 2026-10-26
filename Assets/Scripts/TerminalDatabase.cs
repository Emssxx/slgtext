using System.Collections.Generic;
using UnityEngine;

// 终端机数据库（单例）：合成表、科技树、终端机升级表都在这里。
// 场景里挂在一个常驻空物体上（不要挂在终端机预制体上——终端机建成前，建造菜单就要查它）。
// 列表为空时会自动填入【示例数据】，保证开箱能跑；正式内容后续在 Inspector 里补充/替换。
public class TerminalDatabase : MonoBehaviour
{
    public static TerminalDatabase Instance { get; private set; }

    [Header("合成表（AI 终端机 · 建造台）")]
    public List<RecipeData> recipes = new List<RecipeData>();

    [Header("科技树")]
    public List<TechData> techs = new List<TechData>();

    [Header("终端机升级表（Lv1→Lv2→Lv3）")]
    public List<TerminalUpgradeStep> upgrades = new List<TerminalUpgradeStep>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (recipes.Count == 0 && techs.Count == 0 && upgrades.Count == 0) FillExamples();
    }

    // 示例数据：只为了让框架能跑起来、能测试。具体合成表/科技树/数值后续补充。
    [ContextMenu("填入示例数据")]
    public void FillExamples()
    {
        // —— 合成表（示例）——
        recipes = new List<RecipeData>
        {
            // 终端机把亚空间的扭曲物质重构成基础材料（无需输入，只花时间 = 算力）。
            new RecipeData { id = "r_nano",      outputItemId = "nano",      outputCount = 2, inputs = new MaterialCost[0],              craftTime = 25f, requiredTier = 1 },
            new RecipeData { id = "r_ration",    outputItemId = "ration",    outputCount = 2, inputs = M("nano", 3),                     craftTime = 12f, requiredTier = 1 },
            new RecipeData { id = "r_water",     outputItemId = "water",     outputCount = 2, inputs = M("nano", 2),                     craftTime = 8f,  requiredTier = 1 },
            new RecipeData { id = "r_medkit",    outputItemId = "medkit",    outputCount = 1, inputs = M("nano", 4),                     craftTime = 15f, requiredTier = 1 },
            new RecipeData { id = "r_sedative",  outputItemId = "sedative",  outputCount = 1, inputs = M("nano", 3, "crystal", 1),       craftTime = 20f, requiredTier = 1, requiredTechId = "t_neuro" },
            new RecipeData { id = "r_stimulant", outputItemId = "stimulant", outputCount = 1, inputs = M("nano", 5, "crystal", 1),       craftTime = 20f, requiredTier = 2 },
        };

        // —— 科技树（示例）——
        techs = new List<TechData>
        {
            new TechData { id = "t_neuro", nameZh = "神经稳定学", nameJa = "神経安定学", requiredTier = 1,
                cost = M("nano", 5), researchTime = 20f,
                descZh = "研究亚空间对神经的侵蚀。解锁【镇静剂】的合成。",
                descJa = "亜空間による神経の侵食を研究する。【鎮静剤】の合成を解放。" },
            new TechData { id = "t_restructure", nameZh = "物质重构 II", nameJa = "物質再構成 II", requiredTier = 1,
                cost = M("nano", 10, "crystal", 1), researchTime = 30f,
                descZh = "提升终端机重构物质的精度。终端机升级到【Lv2】的前提。",
                descJa = "端末の物質再構成の精度を高める。端末を【Lv2】へ上げる前提。" },
            new TechData { id = "t_power", nameZh = "风暴能量捕获", nameJa = "嵐エネルギー捕獲", requiredTier = 1,
                cost = M("nano", 8), researchTime = 25f,
                descZh = "捕获亚空间风暴的能量。解锁电力类建筑（发电机等，后续补充）。",
                descJa = "亜空間の嵐のエネルギーを捕らえる。電力系の建築（発電機など、今後追加）を解放。" },
            new TechData { id = "t_quantum", nameZh = "量子算力", nameJa = "量子演算", requiredTier = 2,
                prerequisites = new[] { "t_restructure" }, cost = M("nano", 20, "crystal", 3), researchTime = 45f,
                descZh = "扩展终端机的核心算力。终端机升级到【Lv3】的前提。",
                descJa = "端末の中核演算能力を拡張する。端末を【Lv3】へ上げる前提。" },
        };

        // —— 终端机升级表（示例）——
        upgrades = new List<TerminalUpgradeStep>
        {
            new TerminalUpgradeStep { toLevel = 2, cost = M("nano", 20, "crystal", 2), upgradeTime = 40f, requiredTechId = "t_restructure",
                noteZh = "算力槽 +1（可同时处理 2 项任务）。解锁 Lv2 的建筑与配方（后续补充）。",
                noteJa = "演算スロット +1（同時に2件処理可能）。Lv2の建築とレシピを解放（今後追加）。" },
            new TerminalUpgradeStep { toLevel = 3, cost = M("nano", 40, "crystal", 4), upgradeTime = 60f, requiredTechId = "t_quantum",
                noteZh = "算力槽 +1（可同时处理 3 项任务）。解锁 Lv3 的建筑与配方（后续补充）。",
                noteJa = "演算スロット +1（同時に3件処理可能）。Lv3の建築とレシピを解放（今後追加）。" },
        };
    }

    // —— 查询 ——
    public RecipeData GetRecipe(string id) { foreach (var r in recipes) if (r.id == id) return r; return null; }
    public TechData GetTech(string id)     { foreach (var t in techs) if (t.id == id) return t; return null; }
    public TerminalUpgradeStep GetUpgrade(int toLevel) { foreach (var u in upgrades) if (u.toLevel == toLevel) return u; return null; }

    static bool IsJp => LocalizationManager.Instance != null && LocalizationManager.Instance.current == Language.Japanese;

    // 科技名 / 说明（按当前语言）。找不到时显示 id，方便发现漏配。
    public string TechName(string id) { var t = GetTech(id); return t == null ? id : (IsJp ? t.nameJa : t.nameZh); }
    public string TechDesc(string id) { var t = GetTech(id); return t == null ? "" : (IsJp ? t.descJa : t.descZh); }
    public string UpgradeNote(TerminalUpgradeStep s) { return s == null ? "" : (IsJp ? s.noteJa : s.noteZh); }

    // 小工具：快速写材料表。M("nano", 3, "crystal", 1) = 纳米物质×3 + 异变结晶×1。
    static MaterialCost[] M(params object[] pairs)
    {
        var list = new List<MaterialCost>();
        for (int i = 0; i + 1 < pairs.Length; i += 2)
            list.Add(new MaterialCost { materialName = (string)pairs[i], amount = (int)pairs[i + 1] });
        return list.ToArray();
    }
}
