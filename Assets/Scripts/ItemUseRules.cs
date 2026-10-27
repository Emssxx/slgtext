using UnityEngine;

// 物品使用规则（纯规则，不走 LLM）：从仓库拿出一件物品，用在某个 NPC 身上，改他的数值。
// 玩家（AI）在仓库面板里点"使用"、NPC 饿了自己吃东西，都走这里——逻辑只有一份。
public static class ItemUseRules
{
    // 能不能把这件物品用在这个 NPC 身上？不能时 failKey 给出原因（翻译表的 key）。
    public static bool CanUse(NpcData npc, string itemId, WorldState world, out string failKey)
    {
        failKey = null;
        ItemData item = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(itemId) : null;

        if (npc == null)        { failKey = "item_fail_no_target"; return false; } // 没选使用对象
        if (npc.IsDead)         { failKey = "item_fail_dead";      return false; } // 死人不能用
        if (item == null)       { failKey = "item_fail_unknown";   return false; } // 图鉴里没有这个物品
        if (!item.IsUsable)     { failKey = "item_fail_not_usable"; return false; } // 材料/特殊物品不能直接用
        if (GetCount(itemId, world) <= 0) { failKey = "item_fail_empty"; return false; } // 仓库里没了

        // 已崩解的人，镇静剂（只回理智的药）救不回来——呼应"崩解不可逆"的设定。
        if (npc.isBroken && item.sanityDelta > 0f && item.healthDelta <= 0f && item.hungerDelta >= 0f)
        { failKey = "item_fail_broken"; return false; }

        return true;
    }

    // 使用物品：扣仓库 1 个 → 改 NPC 数值 → 写记忆和日志。成功返回 true。
    // record=false 时不写记忆和日志（调用方想自己写别的文案时用，比如 NPC 自己吃饭）。
    public static bool Use(NpcData npc, string itemId, WorldState world, bool record = true)
    {
        if (!CanUse(npc, itemId, world, out _)) return false;
        if (!WorldManager.Instance.ConsumeFromStorage(itemId, 1)) return false;

        ItemData item = ItemDatabase.Instance.Get(itemId);

        // 改数值，全部夹在 0~100。
        npc.hunger = Mathf.Clamp(npc.hunger + item.hungerDelta, 0f, 100f);
        npc.energy = Mathf.Clamp(npc.energy + item.energyDelta, 0f, 100f);
        npc.health = Mathf.Clamp(npc.health + item.healthDelta, 0f, 100f);
        // 已崩解的人理智不再回升（只可能被兴奋剂之类再扣）。
        if (!npc.isBroken || item.sanityDelta < 0f)
            npc.sanity = Mathf.Clamp(npc.sanity + item.sanityDelta, 0f, 100f);

        // 理智回到幻觉线以上，就不再处于幻觉状态（阈值与 MentalState 的"幻觉 <30"一致）。
        if (npc.sanity >= 30f) npc.isHallucinating = false;

        // 净化水：补水分；急救包：治好受伤 / 生病（NpcHealth）
        if (itemId == WaterWorks.WaterItem && NpcHealth.Instance != null)
            npc.water = Mathf.Clamp(npc.water + NpcHealth.Instance.waterPerDrink, 0f, 100f);
        NpcHealth.OnItemUsed(npc, itemId);

        // 写进记忆（喂 AI 时它会"记得"被用过什么）和日志栏。
        if (record)
        {
            string itemName = ItemDatabase.Instance.GetName(itemId);
            string log = Loc("log_use_item", npc.npcName, itemName);
            npc.memories.Add(new MemoryEntry { day = world.day, content = log });
            if (LogManager.Instance != null)
                LogManager.Instance.AddLog(npc.npcName, log, npc.id);
        }

        return true;
    }

    // 仓库里某物品还剩几个。
    public static int GetCount(string itemId, WorldState world)
    {
        foreach (ItemStack stack in world.sharedStorage)
            if (stack.itemName == itemId) return stack.count;
        return 0;
    }

    // 取本地化文本（LocalizationManager 没就绪时兜底返回 key）。
    static string Loc(string key, params object[] args)
    {
        if (LocalizationManager.Instance == null) return key;
        return LocalizationManager.Instance.GetFormat(key, args);
    }
}
