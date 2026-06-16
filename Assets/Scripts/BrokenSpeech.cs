using UnityEngine;

// 崩解疯话生成器：人格崩解的 NPC 不再调用 AI，改由代码直接生成破碎的疯话。
// 按性格（外向尖叫 / 内向失语）+ 职业残留（旧身份的口头禅）随机拼。
public static class BrokenSpeech
{
    // 外向（sociability 高）：尖叫、嘶吼、断喝。
    static readonly string[] Extrovert =
    {
        "啊——！别过来！别过来！",
        "哈、哈哈……哈哈哈哈——！",
        "谁！是谁在那儿！出来！",
        "不、不不不不不——！",
        "听见了吗？！它就在那儿！！",
        "救命……救命啊……有没有人……！"
    };

    // 内向（sociability 低）：失语、气音、含糊哼吟。
    static readonly string[] Introvert =
    {
        "唔……唔唔……",
        "……（断断续续的哼吟）",
        "嗬……嗬……",
        "（喉咙里发出含糊的气音）",
        "别……别……唔……",
        "……（蜷缩着，说不出完整的话）"
    };

    // 职业残留：崩解时无意识重复旧身份的话。按职业关键字匹配。
    static string GetProfessionEcho(string profession)
    {
        if (string.IsNullOrEmpty(profession)) return null;

        if (profession.Contains("教师") || profession.Contains("老师"))
            return new[] { "……翻到第……第几页了……", "……同学们……安静……", "……这道题……解……解不出来……", "……答案是……是多少来着……" }[Random.Range(0, 4)];

        if (profession.Contains("医生") || profession.Contains("护士"))
            return new[] { "……按住伤口……别松手……", "……脉搏……还有脉搏吗……", "……手术刀……手术刀呢……", "……病人……病人在哪……" }[Random.Range(0, 4)];

        if (profession.Contains("军") || profession.Contains("兵") || profession.Contains("消防"))
            return new[] { "……撤离……快撤离……", "……报数！一、二……", "……火……火还没灭……", "……保持队形……别散开……" }[Random.Range(0, 4)];

        return null; // 其它职业暂无残留
    }

    // 生成一句崩解疯话。传入 NPC，返回要显示的话。
    public static string Generate(NpcData npc)
    {
        // 1. 按性格选基础疯话。
        bool extro = npc.personality.sociability >= 50;
        string baseLine = extro
            ? Extrovert[Random.Range(0, Extrovert.Length)]
            : Introvert[Random.Range(0, Introvert.Length)];

        // 2. 有概率夹带职业残留（约 40%）。
        if (Random.value < 0.4f)
        {
            string echo = GetProfessionEcho(npc.profession);
            if (echo != null)
                baseLine = baseLine + " " + echo;
        }

        return baseLine;
    }
}
