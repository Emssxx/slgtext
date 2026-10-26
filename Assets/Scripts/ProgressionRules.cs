using System.Collections.Generic;
using UnityEngine;

// 营地进度规则（纯规则，不走 LLM）：终端机等级（几产）、科技、解锁判定、材料检查/扣除。
// 建造菜单、终端机面板、任务队列都来这里问"能不能"，判断逻辑只有一份。
public static class ProgressionRules
{
    public const int MaxTier = 3; // 终端机最高三产

    static WorldState W => WorldManager.Instance != null ? WorldManager.Instance.world : null;

    // 当前终端机等级：0=还没建成终端机，1~3=一产~三产。
    // 兜底：场景里预先摆好、已建成的终端机，至少算一产（防止世界数据初始化顺序导致等级为 0）。
    public static int Tier
    {
        get
        {
            int lv = W != null ? W.terminalLevel : 0;
            if (lv < 1 && TerminalController.Instance != null && TerminalController.Instance.isBuilt) lv = 1;
            return Mathf.Clamp(lv, 0, MaxTier);
        }
    }

    // 终端机是否在线（建成 + 激活）。离线时不能合成/研究/升级，任务也会暂停。
    public static bool TerminalOnline => TerminalController.Instance != null && TerminalController.Instance.IsOnline();

    // 是否已研究某项科技（空 id = 不需要科技，视为已满足）。
    public static bool HasTech(string techId)
    {
        if (string.IsNullOrEmpty(techId)) return true;
        return W != null && W.unlockedTechs.Contains(techId);
    }

    // ================= 建造解锁 =================

    // 这个建筑现在能不能建？不能时 reason 是给玩家看的原因（已本地化）。
    public static bool IsBuildableUnlocked(BuildableData bd, out string reason)
    {
        reason = "";
        if (bd == null) return false;
        if (bd.uniqueBuild && W != null && W.placedUniqueBuilds.Contains(bd.id))
        { reason = L("lock_already_built"); return false; }               // 唯一建筑（终端机）已放置
        if (Tier < bd.requiredTier)
        { reason = LF("lock_need_tier", TierLabel(bd.requiredTier)); return false; } // 终端机等级不够
        if (!HasTech(bd.requiredTechId))
        { reason = LF("lock_need_tech", TechName(bd.requiredTechId)); return false; } // 缺科技
        return true;
    }

    // 建造菜单按钮上显示的"短原因"（按钮窄，放不下长句）。
    public static string ShortLockReason(BuildableData bd)
    {
        if (bd.uniqueBuild && W != null && W.placedUniqueBuilds.Contains(bd.id)) return L("lock_short_built");
        if (Tier < bd.requiredTier) return "Lv" + bd.requiredTier;
        if (!HasTech(bd.requiredTechId)) return L("lock_short_tech");
        return "";
    }

    // 放下一个建筑时调用：唯一建筑记一笔，之后就不能再建第二座。
    public static void NotifyPlaced(BuildableData bd)
    {
        if (bd != null && bd.uniqueBuild && W != null && !W.placedUniqueBuilds.Contains(bd.id))
            W.placedUniqueBuilds.Add(bd.id);
    }

    // ================= 合成 / 研究 / 升级 =================

    // 配方是否已解锁（只看等级和科技，不看材料）——列表里灰不灰用这个。
    public static bool IsRecipeUnlocked(RecipeData r, out string reason)
    {
        reason = "";
        if (Tier < r.requiredTier) { reason = LF("lock_need_tier", TierLabel(r.requiredTier)); return false; }
        if (!HasTech(r.requiredTechId)) { reason = LF("lock_need_tech", TechName(r.requiredTechId)); return false; }
        return true;
    }

    // 现在能不能合成（在线 + 已解锁 + 材料够）。
    public static bool CanCraft(RecipeData r, out string reason)
    {
        if (!TerminalOnline) { reason = L("lock_terminal_offline"); return false; }
        if (!IsRecipeUnlocked(r, out reason)) return false;
        if (!HasMaterials(r.inputs)) { reason = L("lock_no_materials"); return false; }
        return true;
    }

    // 科技是否已解锁可研究（等级 + 前置科技）。
    public static bool IsTechUnlocked(TechData t, out string reason)
    {
        reason = "";
        if (Tier < t.requiredTier) { reason = LF("lock_need_tier", TierLabel(t.requiredTier)); return false; }
        if (t.prerequisites != null)
            foreach (string pre in t.prerequisites)
                if (!HasTech(pre)) { reason = LF("lock_need_tech", TechName(pre)); return false; }
        return true;
    }

    // 现在能不能研究（在线 + 没研究过 + 已解锁 + 材料够）。
    public static bool CanResearch(TechData t, out string reason)
    {
        if (!TerminalOnline) { reason = L("lock_terminal_offline"); return false; }
        if (HasTech(t.id)) { reason = L("tech_done"); return false; }
        if (!IsTechUnlocked(t, out reason)) return false;
        if (!HasMaterials(t.cost)) { reason = L("lock_no_materials"); return false; }
        return true;
    }

    // 现在能不能执行这一级升级（在线 + 等级正好是上一级 + 科技 + 材料）。
    public static bool CanUpgrade(TerminalUpgradeStep s, out string reason)
    {
        reason = "";
        if (!TerminalOnline) { reason = L("lock_terminal_offline"); return false; }
        if (Tier >= s.toLevel) { reason = L("upgrade_done"); return false; }
        if (Tier < s.toLevel - 1) { reason = LF("lock_need_tier", TierLabel(s.toLevel - 1)); return false; }
        if (!HasTech(s.requiredTechId)) { reason = LF("lock_need_tech", TechName(s.requiredTechId)); return false; }
        if (!HasMaterials(s.cost)) { reason = L("lock_no_materials"); return false; }
        return true;
    }

    // ================= 材料 =================

    // 仓库里某物品有几个。
    public static int Have(string itemId) => W != null ? ItemUseRules.GetCount(itemId, W) : 0;

    // 材料够不够。
    public static bool HasMaterials(MaterialCost[] cost)
    {
        if (cost == null) return true;
        foreach (MaterialCost c in cost)
            if (c != null && Have(c.materialName) < c.amount) return false;
        return true;
    }

    // 扣材料（先检查够不够，够才扣，避免扣一半）。成功返回 true。
    public static bool ConsumeMaterials(MaterialCost[] cost)
    {
        if (!HasMaterials(cost)) return false;
        if (cost == null) return true;
        foreach (MaterialCost c in cost)
            if (c != null && c.amount > 0) WorldManager.Instance.ConsumeFromStorage(c.materialName, c.amount);
        return true;
    }

    // 材料表写成一行，如"纳米物质 3/5　异变结晶 1/1"（够=绿，不够=红）。
    public static string DescribeCost(MaterialCost[] cost)
    {
        if (cost == null || cost.Length == 0) return L("cost_none");
        string s = "";
        foreach (MaterialCost c in cost)
        {
            if (c == null) continue;
            int have = Have(c.materialName);
            string color = have >= c.amount ? "#5FD69A" : "#FF6B6B";
            s += $"{ItemName(c.materialName)} <color={color}>{have}/{c.amount}</color>　";
        }
        return s;
    }

    // ================= 显示用小工具 =================

    public static string TierName(int tier) => L("tier_name_" + tier);   // 一产 / 二产 / 三产
    public static string TierLabel(int tier) => tier <= 0 ? L("tier_none") : LF("tier_label", tier, TierName(tier)); // Lv1·一产
    public static string TechName(string id) => TerminalDatabase.Instance != null ? TerminalDatabase.Instance.TechName(id) : id;
    public static string ItemName(string id) => ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(id) : id;

    public static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    public static string LF(string key, params object[] a) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, a) : key;
}
