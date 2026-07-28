using UnityEngine;

// 行为决策状态机（纯规则，不走 LLM）。
// 决定 NPC 在"没有玩家指令"时该做什么。按优先级从上到下判断，命中即决定。
// 这是新架构"决策层"的核心：稳定、可预测、零延迟，像 RimWorld 的小人。
// 性格词条通过影响"阈值"来体现个性（如勇敢者更晚逃跑）。
public static class NpcDecisionRules
{
    // NPC 可能的行为。
    public enum Behavior
    {
        Broken,   // 已崩解（不决策，走 BrokenSpeech）
        Flee,     // 逃跑/求救（生命危急）
        Eat,      // 吃东西（饿）
        Rest,     // 休息（累）
        Gather,   // 搜寻物资
        Idle      // 待命/闲逛（安全时默认）
    }

    // 决定一个 NPC 当前该做什么行为。
    public static Behavior Decide(NpcData npc, WorldState world)
    {
        // 1. 崩解：最高优先级，不做正常决策。
        if (npc.isBroken || npc.sanity <= 1f)
            return Behavior.Broken;

        // 2. 生命危急 → 逃跑/求救。
        //    阈值受勇敢影响：越勇敢，越晚逃（勇敢100→阈值约15；勇敢0→阈值约35）。
        float fleeThreshold = 35f - npc.personality.bravery * 0.2f;
        if (npc.health <= fleeThreshold)
            return Behavior.Flee;

        // 3. 太饿 → 吃东西（仓库有吃的才有意义，但决策先做出，执行时再看仓库）。
        //    阈值受谨慎影响：越谨慎越早去吃（谨慎100→阈值约70；谨慎0→阈值约90）。
        float hungerThreshold = 90f - npc.personality.caution * 0.2f;
        if (npc.hunger >= hungerThreshold)
            return Behavior.Eat;

        // 4. 太累 → 休息。
        float energyThreshold = 20f;
        if (npc.energy <= energyThreshold)
            return Behavior.Rest;

        // 5. 物资不足 → 搜寻（利他高的人更愿意为集体去搜寻）。
        if (IsStorageLow(world) && npc.energy > 40f)
            return Behavior.Gather;

        // 6. 安全 → 默认待命。
        return Behavior.Idle;
    }

    // 仓库是否物资不足（简单判断：罐头少于 2）。
    static bool IsStorageLow(WorldState world)
    {
        foreach (ItemStack stack in world.sharedStorage)
            if (stack.itemName == "罐头" && stack.count >= 2)
                return false;
        return true;
    }

    // 把决策真正落到世界上：修改数值/物资，写进记忆。
    // （这是规则执行，不是 LLM。）
    public static void Execute(NpcData npc, Behavior behavior, WorldState world)
    {
        string log;
        switch (behavior)
        {
            case Behavior.Eat:
                if (WorldManager.Instance.ConsumeFromStorage("罐头", 1))
                {
                    npc.hunger = Mathf.Clamp(npc.hunger - 30f, 0f, 100f);
                    npc.currentAction = "刚吃过东西";
                    log = Loc("log_eat_success", npc.npcName);
                }
                else { npc.currentAction = "在找吃的"; log = Loc("log_eat_fail", npc.npcName); }
                break;

            case Behavior.Rest:
                npc.energy = Mathf.Clamp(npc.energy + 20f, 0f, 100f);
                npc.hunger = Mathf.Clamp(npc.hunger + 8f, 0f, 100f);
                npc.currentAction = "休息中";
                log = Loc("log_rest", npc.npcName);
                break;

            case Behavior.Gather:
                npc.energy = Mathf.Clamp(npc.energy - 15f, 0f, 100f);
                WorldManager.Instance.AddToStorage("罐头", 1);
                npc.currentAction = "外出搜寻物资";
                log = Loc("log_gather", npc.npcName);
                break;

            case Behavior.Flee:
                npc.currentAction = "惊慌逃避";
                log = Loc("log_flee", npc.npcName);
                break;

            case Behavior.Idle:
            default:
                npc.currentAction = "待命中";
                log = Loc("log_idle", npc.npcName);
                break;
        }

        npc.memories.Add(new MemoryEntry { day = world.day, content = log });
        // 把 NPC 行为送进日志栏（LogManager 不存在时安全跳过）
        if (LogManager.Instance != null)
            LogManager.Instance.AddLog(npc.npcName, log, npc.id);
    }

    // 取本地化日志文案（LocalizationManager 还没就绪时兜底返回 key本身，避免规则逻辑因此崩溃）。
    static string Loc(string key, params object[] args)
    {
        if (LocalizationManager.Instance == null) return key;
        return LocalizationManager.Instance.GetFormat(key, args);
    }
}
