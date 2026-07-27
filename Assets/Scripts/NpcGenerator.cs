using System.Collections.Generic;
using UnityEngine;

// NPC 生成器：用词条池随机组合，生成一个独一无二的 NPC。
// 各维度随机抽词条 → 组合出性格数值 + 词条列表 + 喂AI的描述文字。
public static class NpcGenerator
{
    // 随机名字池（简单起见用常见姓名；以后可扩充或按性别分）。
    private static readonly string[] NamesMale =
        { "老五", "张伟", "陈强", "王磊", "刘洋", "赵刚", "孙阳" };
    private static readonly string[] NamesFemale =
        { "李静", "王芳", "张敏", "陈洁", "刘霞", "周琳", "孙怡" };

    // 记录本局已经用过的名字，避免重复。
    private static readonly HashSet<string> _usedNames = new HashSet<string>();

    // 每局开始生成 NPC 前，先调用这个重置已用名字（否则跨局会累积）。
    public static void ResetUsedNames()
    {
        _usedNames.Clear();
    }

    // 从名字池里挑一个还没用过的名字。如果都用完了，允许重复（兜底）。
    private static string PickUnusedName(string[] pool)
    {
        // 先收集还没用过的名字。
        List<string> available = new List<string>();
        foreach (string n in pool)
            if (!_usedNames.Contains(n)) available.Add(n);

        string chosen;
        if (available.Count > 0)
            chosen = available[Random.Range(0, available.Count)]; // 从没用过的里挑
        else
            chosen = pool[Random.Range(0, pool.Length)];          // 都用完了，只能重复

        _usedNames.Add(chosen);
        return chosen;
    }

    // 生成一个 NPC。id 由外部传入（保证唯一，如 "npc_01"）。
    public static NpcData Generate(string id)
    {
        NpcData npc = new NpcData();
        npc.id = id;

        // —— 性别 ——
        npc.gender = TraitPool.PickOne(TraitPool.Genders);

        // —— 名字（按性别挑，且排除本局已用过的名字，避免重复）——
        npc.npcName = npc.gender == "男"
            ? PickUnusedName(NamesMale)
            : PickUnusedName(NamesFemale);

        // —— 职业 ——
        npc.profession = TraitPool.PickOne(TraitPool.Professions);

        // —— 抽三类词条：主性格 + 社交 + 癖好 ——
        TraitPool.Trait main = TraitPool.PickOne(TraitPool.MainTraits);
        TraitPool.Trait social = TraitPool.PickOne(TraitPool.SocialTraits);
        TraitPool.Trait quirk = TraitPool.PickOne(TraitPool.QuirkTraits);

        // 记录词条名（喂 AI 用）
        npc.traits = new List<string> { main.name, social.name, quirk.name };

        // —— 用词条的倾向，叠加出 5 维性格数值（基准 50，加各词条增量，最后夹 0~100）——
        npc.personality = new Personality
        {
            bravery = Clamp01_100(50 + main.bravery + social.bravery + quirk.bravery),
            altruism = Clamp01_100(50 + main.altruism + social.altruism + quirk.altruism),
            caution = Clamp01_100(50 + main.caution + social.caution + quirk.caution),
            sociability = Clamp01_100(50 + main.sociability + social.sociability + quirk.sociability),
            optimism = Clamp01_100(50 + main.optimism + social.optimism + quirk.optimism),
        };

        // —— 说话风格 / 价值观 / 背景（拼词条描述，喂 AI）——
        npc.speakingStyle = main.desc + social.desc;
        npc.values = quirk.desc;
        npc.background = $"你曾是一名{npc.profession}。{quirk.desc}";
        npc.currentGoal = "在亚空间里活下去。";

        // —— 初始数值（可加一点随机，让每个人起点略不同）——
        npc.health = 100f;
        npc.hunger = Random.Range(0f, 30f);
        npc.energy = Random.Range(70f, 100f);
        npc.sanity = 100f;

        // —— 对玩家的初始信任度（随机，让有的人天生更信任你、有的更戒备）——
        npc.playerTrust = Random.Range(35f, 65f);

        npc.location = "营地";
        npc.currentAction = "刚到营地";

        return npc;
    }

    // 把数值夹在 0~100。
    private static int Clamp01_100(int v)
    {
        return Mathf.Clamp(v, 0, 100);
    }
}
