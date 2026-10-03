using UnityEngine;

// 崩解疯话生成器：人格崩解的 NPC 不再调用 AI，改由代码直接生成破碎的疯话。
// 按性格（外向尖叫 / 内向失语）+ 职业残留（旧身份的口头禅）随机拼。
// 中文 / 日语两套文本，按当前界面语言选。
public static class BrokenSpeech
{
    // 当前是否日语。
    static bool IsJp => LocalizationManager.Instance != null
                        && LocalizationManager.Instance.current == Language.Japanese;

    // —— 外向（sociability 高）：尖叫、嘶吼、断喝 ——
    static readonly string[] ExtrovertCn =
    {
        "啊——！别过来！别过来！",
        "哈、哈哈……哈哈哈哈——！",
        "谁！是谁在那儿！出来！",
        "不、不不不不不——！",
        "听见了吗？！它就在那儿！！",
        "救命……救命啊……有没有人……！"
    };
    static readonly string[] ExtrovertJp =
    {
        "ああ——！来るな！来るな！",
        "は、はは……ははははは——！",
        "誰だ！そこにいるのは誰だ！出てこい！",
        "いや、いやいやいやいや——！",
        "聞こえるか？！そこにいるんだ！！",
        "助けて……助けてくれ……誰か……！"
    };

    // —— 内向（sociability 低）：失语、气音、含糊哼吟 ——
    static readonly string[] IntrovertCn =
    {
        "唔……唔唔……",
        "……（断断续续的哼吟）",
        "嗬……嗬……",
        "（喉咙里发出含糊的气音）",
        "别……别……唔……",
        "……（蜷缩着，说不出完整的话）"
    };
    static readonly string[] IntrovertJp =
    {
        "う……ううっ……",
        "……（途切れ途切れのうめき声）",
        "ひゅ……ひゅう……",
        "（喉の奥から、かすれた息が漏れる）",
        "やめ……やめて……う……",
        "……（うずくまり、言葉にならない）"
    };

    // 职业残留：崩解时无意识重复旧身份的话。按职业关键字匹配。
    static string GetProfessionEcho(string profession)
    {
        if (string.IsNullOrEmpty(profession)) return null;

        // 职业关键字可能是中文或日文，两种都匹配。
        bool isTeacher = profession.Contains("教师") || profession.Contains("老师") || profession.Contains("教師") || profession.Contains("先生");
        bool isMedic   = profession.Contains("医生") || profession.Contains("护士") || profession.Contains("医者") || profession.Contains("看護");
        bool isSoldier = profession.Contains("军") || profession.Contains("兵") || profession.Contains("消防") || profession.Contains("軍") || profession.Contains("兵士");

        if (isTeacher)
            return IsJp
                ? new[] { "……何ページ……何ページだったか……", "……みんな……静かに……", "……この問題……解け……解けない……", "……答えは……いくつだったか……" }[Random.Range(0, 4)]
                : new[] { "……翻到第……第几页了……", "……同学们……安静……", "……这道题……解……解不出来……", "……答案是……是多少来着……" }[Random.Range(0, 4)];

        if (isMedic)
            return IsJp
                ? new[] { "……傷口を押さえて……離すな……", "……脈は……まだ脈はあるか……", "……メスは……メスはどこだ……", "……患者は……患者はどこだ……" }[Random.Range(0, 4)]
                : new[] { "……按住伤口……别松手……", "……脉搏……还有脉搏吗……", "……手术刀……手术刀呢……", "……病人……病人在哪……" }[Random.Range(0, 4)];

        if (isSoldier)
            return IsJp
                ? new[] { "……退避……早く退避……", "……番号！いち、に……", "……火が……まだ消えていない……", "……隊列を保て……散らばるな……" }[Random.Range(0, 4)]
                : new[] { "……撤离……快撤离……", "……报数！一、二……", "……火……火还没灭……", "……保持队形……别散开……" }[Random.Range(0, 4)];

        return null;
    }

    // 生成一句崩解疯话。
    public static string Generate(NpcData npc)
    {
        bool extro = npc.personality.sociability >= 50;

        string[] pool = extro
            ? (IsJp ? ExtrovertJp : ExtrovertCn)
            : (IsJp ? IntrovertJp : IntrovertCn);
        string baseLine = pool[Random.Range(0, pool.Length)];

        // 有概率夹带职业残留（约 40%）。
        if (Random.value < 0.4f)
        {
            string echo = GetProfessionEcho(npc.profession);
            if (echo != null)
                baseLine = baseLine + " " + echo;
        }

        return baseLine;
    }
}
