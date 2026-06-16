using UnityEngine;
using System.Collections.Generic;

// 世界规则核心：集中处理"亚空间对人的影响"（纯规则，不走 LLM）。
// 全部基于 world.anomalyLevel（异常度）联动，是本作生存压力的来源。
// 每回合由 AIDirector 调用，顺序：ApplyTurnEffects → ApplyBreakdown → ApplyHealth。
public static class WorldRules
{
    public const float HallucinationThreshold = 30f; // 低于它进入幻觉

    // —— 每回合自然演变：SAN 随异常度衰减；安全区回复 ——
    public static void ApplyTurnEffects(WorldState world)
    {
        world.anomalyMood = MoodFromAnomaly(world.anomalyLevel);

        foreach (NpcData npc in world.npcs)
        {
            if (npc.isBroken) { npc.sanity = 0f; continue; } // 已崩解者 SAN 锁死，跳过

            float sanDecay = world.anomalyLevel * 0.06f; // 异常度越高掉得越快

            if (world.isSafeZone)
            {
                sanDecay = 0f;
                npc.sanity = Mathf.Clamp(npc.sanity + 3f, 0f, 100f); // 安全区回 SAN
                npc.energy = Mathf.Clamp(npc.energy + 4f, 0f, 100f); // 回体力
            }

            // 乐观的人 SAN 衰减略慢。
            sanDecay = Mathf.Max(0f, sanDecay - npc.personality.optimism * 0.01f);

            npc.sanity = Mathf.Clamp(npc.sanity - sanDecay, 0f, 100f);

            // 同伴陪伴缓冲（人多时不那么容易崩）。
            if (world.npcs.Count >= 2 && !world.isSafeZone)
                npc.sanity = Mathf.Clamp(npc.sanity + 0.5f, 0f, 100f);

            npc.isHallucinating = npc.sanity < HallucinationThreshold;
        }
    }

    // —— 危险生物冲击：全员 SAN 暴跌 ——
    public static void ApplyCreatureShock(WorldState world, float severity)
    {
        float sanHit = Mathf.Lerp(10f, 35f, Mathf.Clamp01(severity));
        foreach (NpcData npc in world.npcs)
        {
            if (npc.isBroken) continue;
            float braveryReduction = npc.personality.bravery * 0.15f; // 勇敢者受冲击小
            float actual = Mathf.Max(2f, sanHit - braveryReduction);
            npc.sanity = Mathf.Clamp(npc.sanity - actual, 0f, 100f);
            npc.isHallucinating = npc.sanity < HallucinationThreshold;
        }
        world.anomalyLevel = Mathf.Clamp(world.anomalyLevel + 8f, 0f, 100f);
    }

    // —— 崩解与异变：SAN 触底则锁定崩解（不可逆）+ 异变 ——
    // 关键：阈值用 <=1（SAN 会卡在 0.x 永远到不了 0）。
    public static void ApplyBreakdown(WorldState world)
    {
        foreach (NpcData npc in world.npcs)
        {
            if (npc.health <= 0f) continue;

            // SAN 触底 → 锁定崩解（一旦 true 永不恢复）。
            if (npc.sanity <= 1f && !npc.isBroken)
            {
                npc.isBroken = true;
                npc.currentAction = "人格崩解";
                AddMutation(npc); // 崩解瞬间长第一个异变
                world.eventLog.Add(new MemoryEntry
                {
                    day = world.day,
                    content = npc.npcName + " 的精神彻底崩溃了，身体开始发生可怕的异变……"
                });
            }

            // 已崩解：有概率继续长新异变（掉血移交 ApplyHealth 统一处理）。
            if (npc.isBroken && Random.value < 0.4f)
                AddMutation(npc);
        }
    }

    // —— 统一生命结算（每回合，所有掉血/回血在这里算）——
    public static void ApplyHealth(WorldState world)
    {
        foreach (NpcData npc in world.npcs)
        {
            if (npc.health <= 0f) continue;

            float delta = 0f;

            // 饥饿掉血
            if (npc.hunger >= 90f) delta -= 6f;
            else if (npc.hunger >= 70f) delta -= 2f;

            // 体力耗尽掉血
            if (npc.energy <= 0f) delta -= 3f;

            // 崩解掉血
            if (npc.isBroken) delta -= 8f;

            // 安全区 + 不饿 + 没崩解 → 回血
            if (world.isSafeZone && npc.hunger < 50f && !npc.isBroken)
                delta += 4f;

            npc.health = Mathf.Clamp(npc.health + delta, 0f, 100f);

            // 死亡判定
            if (npc.health <= 0f)
            {
                if (npc.isBroken)
                {
                    npc.currentAction = "已死亡";
                    world.eventLog.Add(new MemoryEntry { day = world.day,
                        content = npc.npcName + " 在彻底异变后死去，化作了亚空间的一部分。" });
                }
                else
                {
                    npc.currentAction = "已死亡";
                    world.eventLog.Add(new MemoryEntry { day = world.day,
                        content = npc.npcName + " 没能撑下去，倒下了。" });
                }
            }
        }
    }

    // 异常度 → 亚空间"情绪"（导演定基调用）。
    public static string MoodFromAnomaly(float anomaly)
    {
        if (anomaly < 30f) return "平静";
        if (anomaly < 60f) return "躁动";
        if (anomaly < 85f) return "充满恶意";
        return "疯狂";
    }

    // —— 异变特征池 ——
    static readonly string[] MutationPool =
    {
        "额头长出了一对弯曲的羊角",
        "手掌中央睁开了一只湿润的眼睛",
        "皮肤下游走着不属于人类的黑色纹路",
        "脊背裂开，渗出黏稠的触须",
        "嘴部撕裂到耳根，露出过多的牙齿",
        "影子开始与身体的动作不同步",
        "双眼变成了没有瞳孔的纯黑"
    };

    static void AddMutation(NpcData npc)
    {
        if (npc.mutations == null) return;
        var candidates = new List<string>();
        foreach (string m in MutationPool)
            if (!npc.mutations.Contains(m)) candidates.Add(m);
        if (candidates.Count == 0) return;
        npc.mutations.Add(candidates[Random.Range(0, candidates.Count)]);
    }
}
