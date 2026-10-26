using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

// ============================================================================
// NPC 演员提示词（Actor Profile）
// ----------------------------------------------------------------------------
// 每个 NPC 一份"演员提示词"：LLM 扮演这个 NPC 时【最先】读到的一整段设定。
// 分两层（扬长避短）：
//   ① 固定事实 —— 代码生成，绝不交给 LLM：年龄、以前的职业、收入、家庭、宠物、爱好、性格、
//      对 AI 的初始信任和态度类型（由性格数值按规则算出，见 ActorFacts.ComputeTrust）。
//   ② 演绎部分 —— 交给 LLM 在开局后台写（NpcActors 排队生成）：被卷入的那天、和 AI 的说话方式、
//      对亚空间的认知、低语时会听见的声音、面对各种事件 / 死亡 / 敌人的态度。
//      LLM 还没写好、写坏了、或语言不对时，用代码模板兜底——游戏随时都有一份完整的演员提示词。
// 渲染格式固定（【标题】+ 第二人称"你"），见 Render()。
// ============================================================================
[Serializable]
public class NpcActorProfile
{
    public string id;
    public string lang;      // 演绎部分是用哪种语言写的："zh" / "ja"（和当前界面语言不同时，演绎部分改用代码模板）
    public string source;    // "template" = 全部是代码模板；"llm" = 演绎部分由 LLM 写成

    // —— ① 固定事实（代码生成）——
    public string name;
    public bool male;
    public int age;
    public int professionIndex = -1;   // TraitPool.Professions 的下标（-1 = 不在表里）
    public string professionRaw;       // NpcData.profession 原文（不在表里时直接显示它）
    public int income;                 // 月收入（中文：元；日文：万円）；学生 = 生活费；农民 = 年收入（中文：万元；日文：万円）
    public string genLang;             // 生成事实时的界面语言（收入的货币单位跟着它；家乡、宠物名按当前语言的词库显示）
    public int hometown;               // 家乡（下标）
    public int marital;                // 0 单身 / 1 已婚 / 2 离异 / 3 丧偶
    public int children;               // 孩子数（0~2）
    public int childAge;               // 大的那个孩子几岁
    public int parents;                // 0 父母健在 / 1 父亲已故 / 2 母亲已故 / 3 都已过世
    public int lostRelative = -1;      // 已故的亲人（低语用）：-1 无 / 0 祖母 / 1 父亲 / 2 母亲 / 3 伴侣 / 4 兄弟姐妹
    public int petKind = -1;           // 宠物种类（-1 = 不养）
    public int petName;                // 宠物名（下标）
    public List<int> hobbies = new List<int>();
    public bool religious;             // 词条"信教"
    public int initialTrust;           // 对 AI 的初始信任（0~100）
    public int attitude;               // 对 AI 的态度类型：0 依赖 / 1 合作 / 2 观望 / 3 抗拒

    // —— ② 演绎部分（LLM 写；空 = 用代码模板）——
    public string sweptIn, speechToAi, subspaceKnowledge;
    public List<string> whispers = new List<string>();
    public string whisperReaction, spaceWarp, timeDisorder, storm, causalReversal;
    public string deathStranger, deathFriend, deathSelf, enemyHuman, enemyCreature;

    // 演绎部分的字段名（LLM 输出的 JSON 键名 → 字段）。
    public static readonly string[] TextKeys =
    {
        "swept_in", "speech_to_ai", "subspace_knowledge", "whisper_reaction",
        "space_warp", "time_disorder", "storm", "causal_reversal",
        "death_stranger", "death_friend", "death_self", "enemy_human", "enemy_creature"
    };

    public string GetText(string key)
    {
        switch (key)
        {
            case "swept_in": return sweptIn;
            case "speech_to_ai": return speechToAi;
            case "subspace_knowledge": return subspaceKnowledge;
            case "whisper_reaction": return whisperReaction;
            case "space_warp": return spaceWarp;
            case "time_disorder": return timeDisorder;
            case "storm": return storm;
            case "causal_reversal": return causalReversal;
            case "death_stranger": return deathStranger;
            case "death_friend": return deathFriend;
            case "death_self": return deathSelf;
            case "enemy_human": return enemyHuman;
            case "enemy_creature": return enemyCreature;
        }
        return null;
    }

    public void SetText(string key, string v)
    {
        switch (key)
        {
            case "swept_in": sweptIn = v; break;
            case "speech_to_ai": speechToAi = v; break;
            case "subspace_knowledge": subspaceKnowledge = v; break;
            case "whisper_reaction": whisperReaction = v; break;
            case "space_warp": spaceWarp = v; break;
            case "time_disorder": timeDisorder = v; break;
            case "storm": storm = v; break;
            case "causal_reversal": causalReversal = v; break;
            case "death_stranger": deathStranger = v; break;
            case "death_friend": deathFriend = v; break;
            case "death_self": deathSelf = v; break;
            case "enemy_human": enemyHuman = v; break;
            case "enemy_creature": enemyCreature = v; break;
        }
    }

    // 取某项演绎文字：LLM 写过、且语言和当前界面一致就用它，否则用代码模板。
    public string Text(string key, NpcData npc, bool jp)
    {
        string v = GetText(key);
        if (!string.IsNullOrEmpty(v) && lang == (jp ? "ja" : "zh")) return v;
        return ActorFacts.Fallback(this, npc, key, jp);
    }

    // 低语时可能听见的声音（3 种左右）。
    public List<string> Whispers(NpcData npc, bool jp)
    {
        if (whispers != null && whispers.Count >= 2 && lang == (jp ? "ja" : "zh")) return whispers;
        return ActorFacts.FallbackWhispers(this, npc, jp);
    }

    // ================= 渲染：演员提示词全文（固定格式）=================
    public string Render(NpcData npc, bool jp)
    {
        StringBuilder sb = new StringBuilder();
        string n = npc != null ? npc.npcName : name;
        if (jp)
        {
            sb.AppendLine("【役者への指示書】");
            sb.AppendLine("あなたは今「" + n + "」を演じている。以下があなたの設定のすべてである。口にする言葉はすべてこれに沿わせ、設定を勝手に変えてはならない。");
            sb.AppendLine("【身分】異次元の渦に巻き込まれ、亜空間に迷い込んだ一般市民。" + ActorFacts.JobLine(this, true));
            sb.AppendLine("【年齢】" + age + "歳");
            sb.AppendLine("【性別】" + (male ? "男性" : "女性"));
            sb.AppendLine("【家族】" + ActorFacts.FamilyLine(this, true));
            sb.AppendLine("【趣味】" + ActorFacts.HobbyLine(this, true));
            sb.AppendLine("【性格】" + ActorFacts.PersonalityLine(npc, true));
            sb.AppendLine("【性格の表れ】" + ActorFacts.TraitDescLine(npc, true));
            sb.AppendLine("【巻き込まれた日】" + Text("swept_in", npc, true));
            sb.AppendLine("【AIとの話し方】" + Text("speech_to_ai", npc, true) + "（最初のAIへの信頼：" + initialTrust + "/100、" + ActorFacts.AttitudeName(attitude, true) + "）");
            sb.AppendLine("【亜空間についての認識】" + Text("subspace_knowledge", npc, true));
            sb.AppendLine("―― 出来事への態度 ――");
            sb.AppendLine("【不可解な囁き】聞こえるかもしれない声：" + Numbered(Whispers(npc, true), true) +
                          "一度に聞こえるのはそのうち一つだけで、声はぼんやりしていて聞き取れない。" + Text("whisper_reaction", npc, true));
            sb.AppendLine("【空間の歪み】" + Text("space_warp", npc, true));
            sb.AppendLine("【時間の乱れ】" + Text("time_disorder", npc, true));
            sb.AppendLine("【負のエネルギーの嵐】" + Text("storm", npc, true));
            sb.AppendLine("【因果の逆転】" + Text("causal_reversal", npc, true));
            sb.AppendLine("―― 死に向き合うとき ――");
            sb.AppendLine("【他人の死】" + Text("death_stranger", npc, true));
            sb.AppendLine("【仲間の死】" + Text("death_friend", npc, true));
            sb.AppendLine("【自分の死】" + Text("death_self", npc, true));
            sb.AppendLine("―― 敵に向き合うとき ――");
            sb.AppendLine("【人間の敵】" + Text("enemy_human", npc, true));
            sb.AppendLine("【亜空間の生物】" + Text("enemy_creature", npc, true));
        }
        else
        {
            sb.AppendLine("【演员提示词】");
            sb.AppendLine("你现在扮演「" + n + "」。下面是你的全部设定，你说出口的每一句话都必须符合它，不得擅自改动。");
            sb.AppendLine("【身份】你是被异次元漩涡卷入亚空间的普通市民。" + ActorFacts.JobLine(this, false));
            sb.AppendLine("【年龄】" + age + "岁");
            sb.AppendLine("【性别】" + (male ? "男" : "女"));
            sb.AppendLine("【家庭】" + ActorFacts.FamilyLine(this, false));
            sb.AppendLine("【爱好】" + ActorFacts.HobbyLine(this, false));
            sb.AppendLine("【性格】" + ActorFacts.PersonalityLine(npc, false));
            sb.AppendLine("【性格表现】" + ActorFacts.TraitDescLine(npc, false));
            sb.AppendLine("【被卷入的那天】" + Text("swept_in", npc, false));
            sb.AppendLine("【与AI的对话方式】" + Text("speech_to_ai", npc, false) + "（一开始对AI的信任：" + initialTrust + "/100，" + ActorFacts.AttitudeName(attitude, false) + "）");
            sb.AppendLine("【对亚空间的认知】" + Text("subspace_knowledge", npc, false));
            sb.AppendLine("—— 事件态度 ——");
            sb.AppendLine("【神秘的低语】你可能听见的声音：" + Numbered(Whispers(npc, false), false) +
                          "每次只会听见其中一种，声音很模糊，听不清。" + Text("whisper_reaction", npc, false));
            sb.AppendLine("【空间扭曲】" + Text("space_warp", npc, false));
            sb.AppendLine("【时间失序】" + Text("time_disorder", npc, false));
            sb.AppendLine("【负能量风暴】" + Text("storm", npc, false));
            sb.AppendLine("【因果倒置】" + Text("causal_reversal", npc, false));
            sb.AppendLine("—— 面对死亡 ——");
            sb.AppendLine("【他人死亡】" + Text("death_stranger", npc, false));
            sb.AppendLine("【朋友死亡】" + Text("death_friend", npc, false));
            sb.AppendLine("【自己濒死】" + Text("death_self", npc, false));
            sb.AppendLine("—— 面对敌人 ——");
            sb.AppendLine("【人类敌人】" + Text("enemy_human", npc, false));
            sb.AppendLine("【亚空间生物】" + Text("enemy_creature", npc, false));
        }
        return sb.ToString();
    }

    // 只有固定事实的部分（给 LLM 写演绎部分时用，也用于 NPC 闲聊的人物卡）。
    public string FactSheet(NpcData npc, bool jp)
    {
        StringBuilder sb = new StringBuilder();
        string n = npc != null ? npc.npcName : name;
        if (jp)
        {
            sb.AppendLine("- 名前：" + n + "（" + (male ? "男性" : "女性") + "、" + age + "歳）");
            sb.AppendLine("- 職業と収入：" + ActorFacts.JobLine(this, true));
            sb.AppendLine("- 家族：" + ActorFacts.FamilyLine(this, true));
            sb.AppendLine("- 趣味：" + ActorFacts.HobbyLine(this, true));
            sb.AppendLine("- 性格：" + ActorFacts.PersonalityLine(npc, true) + "。" + ActorFacts.TraitDescLine(npc, true));
            sb.AppendLine("- AIへの最初の態度：" + ActorFacts.AttitudeName(attitude, true) + "――" + ActorFacts.AttitudeDesc(attitude, true) + "（信頼 " + initialTrust + "/100）");
        }
        else
        {
            sb.AppendLine("- 名字：" + n + "（" + (male ? "男" : "女") + "，" + age + "岁）");
            sb.AppendLine("- 职业与收入：" + ActorFacts.JobLine(this, false));
            sb.AppendLine("- 家庭：" + ActorFacts.FamilyLine(this, false));
            sb.AppendLine("- 爱好：" + ActorFacts.HobbyLine(this, false));
            sb.AppendLine("- 性格：" + ActorFacts.PersonalityLine(npc, false) + "。" + ActorFacts.TraitDescLine(npc, false));
            sb.AppendLine("- 对AI的初始态度：" + ActorFacts.AttitudeName(attitude, false) + "——" + ActorFacts.AttitudeDesc(attitude, false) + "（信任 " + initialTrust + "/100）");
        }
        return sb.ToString();
    }

    static string Numbered(List<string> items, bool jp)
    {
        StringBuilder sb = new StringBuilder();
        string[] marks = { "①", "②", "③", "④" };
        for (int i = 0; i < items.Count && i < marks.Length; i++)
        {
            string t = items[i].Trim().TrimEnd('。', '.', '；', ';');
            sb.Append(marks[i]).Append(t).Append(jp ? "。" : "；");
        }
        return sb.ToString();
    }
}

// ============================================================================
// 固定事实的生成规则 + 词库 + 演绎部分的代码模板（中 / 日）
// ============================================================================
public static class ActorFacts
{
    // ---------------- 生成固定事实 ----------------

    // 职业下标顺序与 TraitPool.Professions 一致：教师 医生 军人 消防员 学生 工程师 厨师 警察 记者 农民
    static readonly int[] AgeMin = { 25, 28, 20, 22, 19, 24, 20, 23, 23, 30 };
    static readonly int[] AgeMax = { 55, 58, 38, 45, 23, 50, 55, 50, 50, 65 };
    // 中文：元/月（学生=生活费，农民=万元/年）
    static readonly int[] IncomeZhMin = { 4000, 9000, 4500, 5000, 1500, 9000, 4000, 6000, 5000, 3 };
    static readonly int[] IncomeZhMax = { 7000, 18000, 7000, 8000, 2500, 18000, 8000, 9000, 10000, 6 };
    // 日文：万円/月（学生=仕送り・バイト、農家=万円/年）
    static readonly int[] IncomeJaMin = { 25, 60, 22, 25, 5, 32, 20, 25, 30, 250 };
    static readonly int[] IncomeJaMax = { 35, 110, 30, 33, 9, 50, 28, 35, 45, 450 };

    // 对 AI 的初始信任：职业修正（顺序同上）
    static readonly int[] TrustByProfession = { 0, 5, 5, 5, 5, 10, 0, 5, -10, -10 };

    public static NpcActorProfile Generate(NpcData npc)
    {
        NpcActorProfile p = new NpcActorProfile();
        p.id = npc.id;
        p.name = npc.npcName;
        p.male = npc.gender == null || npc.gender.Contains("男");
        p.genLang = LanguageGuard.IsJp ? "ja" : "zh";
        p.lang = p.genLang;
        p.source = "template";
        p.professionRaw = npc.profession;
        p.professionIndex = ProfessionIndex(npc.profession);

        int pi = p.professionIndex;
        p.age = pi >= 0 ? UnityEngine.Random.Range(AgeMin[pi], AgeMax[pi] + 1) : UnityEngine.Random.Range(22, 56);

        // 收入
        if (pi >= 0)
        {
            if (p.genLang == "ja")
                p.income = UnityEngine.Random.Range(IncomeJaMin[pi], IncomeJaMax[pi] + 1);
            else
            {
                p.income = UnityEngine.Random.Range(IncomeZhMin[pi], IncomeZhMax[pi] + 1);
                if (pi != 9) p.income = Mathf.RoundToInt(p.income / 500f) * 500; // 取整到 500
            }
        }

        // 家庭：学生一律单身、父母健在；28 岁以上才可能有孩子
        bool student = pi == 4;
        p.hometown = UnityEngine.Random.Range(0, HometownsZh.Length);
        if (student) p.marital = 0;
        else
        {
            float r = UnityEngine.Random.value;
            float marriedChance = Mathf.InverseLerp(22f, 45f, p.age) * 0.75f;
            if (r < marriedChance) p.marital = 1;
            else if (r < marriedChance + 0.08f && p.age >= 30) p.marital = 2;
            else p.marital = 0;
        }
        if ((p.marital == 1 || p.marital == 2) && p.age >= 28 && UnityEngine.Random.value < 0.7f)
        {
            p.children = UnityEngine.Random.value < 0.7f ? 1 : 2;
            p.childAge = Mathf.Clamp(p.age - UnityEngine.Random.Range(24, 34), 1, 30);
        }
        float parentDeath = student ? 0f : Mathf.InverseLerp(30f, 65f, p.age);
        float pr = UnityEngine.Random.value;
        if (pr < parentDeath * 0.35f) p.parents = 3;
        else if (pr < parentDeath * 0.75f) p.parents = UnityEngine.Random.value < 0.5f ? 1 : 2;
        else p.parents = 0;

        // 词条：失去过家人 / 信教
        bool lostFamily = HasTrait(npc, "失去过家人");
        p.religious = HasTrait(npc, "信教");
        if (lostFamily)
        {
            if (p.marital == 1 && UnityEngine.Random.value < 0.4f) { p.marital = 3; p.lostRelative = 3; }
            else if (p.parents == 0 && !student) { p.parents = UnityEngine.Random.value < 0.5f ? 1 : 2; p.lostRelative = p.parents == 1 ? 1 : 2; }
            else if (p.parents == 1 || p.parents == 3) p.lostRelative = 1;
            else if (p.parents == 2) p.lostRelative = 2;
            else p.lostRelative = 4;
        }
        else if (UnityEngine.Random.value < 0.4f) p.lostRelative = 0; // 和已故的祖母很亲（低语素材）

        // 宠物（约 45%）
        if (UnityEngine.Random.value < 0.45f)
        {
            p.petKind = UnityEngine.Random.Range(0, PetKinds.Length);
            p.petName = UnityEngine.Random.Range(0, PetNamesZh.Length);
        }

        // 爱好 2 个
        int h1 = UnityEngine.Random.Range(0, Hobbies.Length);
        int h2 = (h1 + UnityEngine.Random.Range(1, Hobbies.Length)) % Hobbies.Length;
        p.hobbies.Add(h1);
        p.hobbies.Add(h2);

        // 对 AI 的初始信任与态度类型（代码按性格算，不交给 LLM）
        p.initialTrust = ComputeTrust(npc, pi);
        p.attitude = AttitudeOf(p.initialTrust);
        return p;
    }

    // 初始信任 = 50 + 职业 + 性格词条 + 性格数值 + 一点随机，夹在 15~88。
    //   多疑（偏执）、暴躁、毒舌、念旧、不信机器的职业（记者、农民）→ 低；
    //   胆小需要依靠的（怯懦、怕黑）、温和、开朗、技术相关职业（工程师）→ 高；
    //   越谨慎越不信，越乐观越信。
    public static int ComputeTrust(NpcData npc, int professionIndex)
    {
        float t = 50f;
        if (professionIndex >= 0) t += TrustByProfession[professionIndex];
        if (HasTrait(npc, "偏执")) t -= 20f;
        if (HasTrait(npc, "暴躁")) t -= 10f;
        if (HasTrait(npc, "阴沉")) t -= 5f;
        if (HasTrait(npc, "麻木")) t -= 5f;
        if (HasTrait(npc, "勇敢")) t -= 5f;
        if (HasTrait(npc, "冷静")) t += 5f;
        if (HasTrait(npc, "开朗")) t += 5f;
        if (HasTrait(npc, "怯懦")) t += 10f;
        if (HasTrait(npc, "话痨")) t += 5f;
        if (HasTrait(npc, "温和")) t += 5f;
        if (HasTrait(npc, "寡言")) t -= 5f;
        if (HasTrait(npc, "毒舌")) t -= 5f;
        if (HasTrait(npc, "念旧")) t -= 5f;
        if (HasTrait(npc, "怕黑")) t += 5f;
        Personality p = npc.personality;
        if (p != null)
        {
            t -= (p.caution - 50) * 0.2f;
            t += (p.optimism - 50) * 0.2f;
        }
        t += UnityEngine.Random.Range(-5f, 5f);
        return Mathf.Clamp(Mathf.RoundToInt(t), 15, 88);
    }

    public static int AttitudeOf(float trust)
    {
        if (trust >= 64f) return 0;
        if (trust >= 48f) return 1;
        if (trust >= 32f) return 2;
        return 3;
    }

    public static string AttitudeName(int a, bool jp)
    {
        switch (a)
        {
            case 0: return jp ? "依存" : "依赖";
            case 1: return jp ? "協力" : "合作";
            case 2: return jp ? "様子見" : "观望";
            default: return jp ? "拒絶" : "抗拒";
        }
    }

    public static string AttitudeDesc(int a, bool jp)
    {
        switch (a)
        {
            case 0: return jp ? "AIを命綱だと思い、言われたことには従いたい。何かあればまずAIに頼る。"
                              : "你把AI当成救命稻草，愿意听它的，遇事先问它。";
            case 1: return jp ? "AIは役に立つと思っていて、提案は真剣に考えるが、自分の判断も持っている。"
                              : "你觉得AI有用，会认真考虑它的建议，但也有自己的判断。";
            case 2: return jp ? "AIを半信半疑で見ていて、その意図をよく問い返す。まず結果を見せてほしい。"
                              : "你对AI将信将疑，常反问它的用意，需要它拿出结果证明自己。";
            default: return jp ? "この機械を信用していない。冷たくとげのある口調で、言うことにはたいてい従わない。"
                               : "你不信任这台机器，说话冷淡带刺，多半不愿照它说的做。";
        }
    }

    // 此刻对 AI 的态度（按当前信任度，每次对话都重新算）。
    public static string TrustNow(NpcData npc, bool jp)
    {
        int t = Mathf.RoundToInt(npc.playerTrust);
        int a = AttitudeOf(t);
        return (jp ? "信頼 " : "信任 ") + t + "/100：" + AttitudeName(a, jp) + (jp ? "――" : "——") + AttitudeDesc(a, jp);
    }

    // ---------------- 事实 → 文字 ----------------

    public static string ProfessionName(NpcActorProfile p, bool jp)
    {
        if (p.professionIndex >= 0 && p.professionIndex < TraitPool.Professions.Length)
            return jp ? TraitPool.ProfessionsJa[p.professionIndex] : TraitPool.Professions[p.professionIndex];
        return p.professionRaw ?? "";
    }

    public static string JobLine(NpcActorProfile p, bool jp)
    {
        string job = ProfessionName(p, jp);
        int pi = p.professionIndex;
        bool ja = p.genLang == "ja";
        if (pi < 0) return jp ? "以前は" + job + "だった。" : "你以前是" + job + "。";
        if (pi == 4) // 学生
        {
            if (jp) return "以前は学生で、" + (ja ? "仕送りとバイトで月に" + p.income + "万円ほどでやりくりしていた。" : "毎月の生活費は家からの仕送り頼みだった。");
            return "你以前是一名学生，" + (ja ? "靠家里寄钱和打工过日子。" : "每个月靠家里给的" + p.income + "元生活费过日子。");
        }
        if (pi == 9) // 农民：年收入
        {
            if (jp) return "以前は農家で、年収は" + (ja ? "約" + p.income + "万円" : "あまり多くない") + "。暮らしは質素だった。";
            return "你以前是一名农民，" + (ja ? "收入不高" : "一年收入大约" + p.income + "万元") + "，日子过得简单。";
        }
        if (jp)
        {
            string money = ja ? "月収は約" + p.income + "万円で、" + ClassJa(p.income, true) : ClassJa(ToManYen(p), true);
            return "以前は" + job + "で、" + money + "。";
        }
        string m = ja ? ClassZh(FromManYen(p)) : "月薪约" + p.income + "元，" + ClassZh(p.income);
        return "你以前是一名" + job + "，" + m + "。";
    }

    static int ToManYen(NpcActorProfile p) => Mathf.RoundToInt(p.income / 200f); // 元 → 万円（大致换算，只用于判断阶层）
    static int FromManYen(NpcActorProfile p) => p.income * 200;                   // 万円 → 元（同上）

    static string ClassZh(int yuan)
    {
        if (yuan < 5000) return "收入不高，日子过得紧巴巴";
        if (yuan < 8000) return "属于普通工薪阶层";
        if (yuan < 15000) return "属于中产阶级";
        return "收入不错，生活比较宽裕";
    }

    static string ClassJa(int man, bool sentence)
    {
        if (man < 22) return "暮らしはぎりぎりだった";
        if (man < 32) return "ごく普通の暮らしだった";
        if (man < 55) return "中流の暮らしだった";
        return "かなり裕福な暮らしだった";
    }

    public static string FamilyLine(NpcActorProfile p, bool jp)
    {
        string home = jp ? HometownsJa[p.hometown % HometownsJa.Length] : HometownsZh[p.hometown % HometownsZh.Length]; // 按当前语言取（日语提示词里不混中文地名）
        StringBuilder sb = new StringBuilder();
        if (jp)
        {
            switch (p.marital)
            {
                case 1: sb.Append("結婚している"); break;
                case 2: sb.Append("離婚している"); break;
                case 3: sb.Append("連れ合いを数年前に亡くした"); break;
                default: sb.Append("独身"); break;
            }
            if (p.children > 0) sb.Append(p.children == 1 ? "。子どもが一人いる（" + p.childAge + "歳）" : "。子どもが二人いる（上の子は" + p.childAge + "歳）");
            sb.Append("。");
            switch (p.parents)
            {
                case 1: sb.Append("父はすでに亡くなり、母は故郷（" + home + "）で一人暮らしをしている。"); break;
                case 2: sb.Append("母はすでに亡くなり、父は故郷（" + home + "）で一人暮らしをしている。"); break;
                case 3: sb.Append("両親はどちらもすでに亡くなっている。故郷は" + home + "。"); break;
                default: sb.Append("両親は故郷（" + home + "）で暮らしている。"); break;
            }
            if (p.lostRelative == 0) sb.Append("小さい頃は祖母に一番懐いていたが、祖母は数年前に亡くなった。");
            if (p.lostRelative == 4) sb.Append("兄弟を亡くしたことがあり、今も心の傷になっている。");
            if (p.petKind >= 0) sb.Append(PetKindsJa[p.petKind] + "を飼っていて、名前は" + PetName(p, true) + "。とても可愛がっている。");
            if (p.religious) sb.Append("信仰を持っていて、よく心の中で祈る。");
        }
        else
        {
            switch (p.marital)
            {
                case 1: sb.Append("你已婚"); break;
                case 2: sb.Append("你离过婚"); break;
                case 3: sb.Append("你的伴侣几年前去世了"); break;
                default: sb.Append("你单身"); break;
            }
            if (p.children > 0) sb.Append(p.children == 1 ? "，有一个" + p.childAge + "岁的孩子" : "，有两个孩子，大的" + p.childAge + "岁");
            sb.Append("。");
            switch (p.parents)
            {
                case 1: sb.Append("父亲已经去世，母亲独自生活在老家（" + home + "）。"); break;
                case 2: sb.Append("母亲已经去世，父亲独自生活在老家（" + home + "）。"); break;
                case 3: sb.Append("父母都已经去世了，老家在" + home + "。"); break;
                default: sb.Append("父母生活在老家（" + home + "）。"); break;
            }
            if (p.lostRelative == 0) sb.Append("你从小跟奶奶最亲，她几年前去世了。");
            if (p.lostRelative == 4) sb.Append("你失去过一个兄弟，这道伤一直没好。");
            if (p.petKind >= 0) sb.Append("你养了一只" + PetKinds[p.petKind] + "，叫" + PetName(p, false) + "，你很爱它。");
            if (p.religious) sb.Append("你有自己的信仰，常常在心里默默祈祷。");
        }
        return sb.ToString();
    }

    public static string HobbyLine(NpcActorProfile p, bool jp)
    {
        List<string> list = new List<string>();
        foreach (int h in p.hobbies) if (h >= 0 && h < Hobbies.Length) list.Add(jp ? HobbiesJa[h] : Hobbies[h]);
        return string.Join("、", list);
    }

    // 性格：抽到的词条 + 由性格数值推出的标签（≥70 / ≤30）。
    public static string PersonalityLine(NpcData npc, bool jp)
    {
        List<string> words = new List<string>();
        if (npc != null && npc.traits != null)
            foreach (string t in npc.traits)
            {
                string w = TraitName(t, jp);
                if (!string.IsNullOrEmpty(w) && !words.Contains(w)) words.Add(w);
            }
        List<string> tags = new List<string>();
        Personality p = npc != null ? npc.personality : null;
        if (p != null)
        {
            AddTag(tags, words, p.bravery, "pers_brave_hi", "pers_brave_lo");
            AddTag(tags, words, p.altruism, "pers_altruism_hi", "pers_altruism_lo");
            AddTag(tags, words, p.caution, "pers_caution_hi", "pers_caution_lo");
            AddTag(tags, words, p.sociability, "pers_social_hi", "pers_social_lo");
            AddTag(tags, words, p.optimism, "pers_optimism_hi", "pers_optimism_lo");
        }
        string line = string.Join("、", words);
        if (tags.Count > 0) line += (jp ? "（" : "（") + string.Join("、", tags) + (jp ? "）" : "）");
        return line;
    }

    // 性格词条的具体表现（词条池里的描述，按当前语言）："你遇事沉着，很少慌乱。你话很少，习惯用短句。"
    public static string TraitDescLine(NpcData npc, bool jp)
    {
        StringBuilder sb = new StringBuilder();
        if (npc != null && npc.traits != null)
            foreach (string t in npc.traits)
            {
                TraitPool.Trait tr = FindTrait(t);
                if (tr != null) sb.Append(jp ? tr.descJa : tr.desc);
            }
        if (sb.Length == 0 && npc != null && !string.IsNullOrEmpty(npc.speakingStyle)) sb.Append(npc.speakingStyle);
        return sb.ToString();
    }

    static void AddTag(List<string> tags, List<string> words, int v, string hi, string lo)
    {
        string key = v >= 70 ? hi : (v <= 30 ? lo : null);
        if (key == null) return;
        string w = LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
        if (!words.Contains(w) && !tags.Contains(w)) tags.Add(w);
    }

    // 词条名（NpcData 里存的是生成时的语言）→ 当前语言。
    public static string TraitName(string stored, bool jp)
    {
        TraitPool.Trait t = FindTrait(stored);
        if (t == null) return stored;
        return jp ? t.nameJa : t.name;
    }

    static TraitPool.Trait FindTrait(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        foreach (TraitPool.Trait[] pool in new[] { TraitPool.MainTraits, TraitPool.SocialTraits, TraitPool.QuirkTraits })
            foreach (TraitPool.Trait t in pool)
                if (t.name == stored || t.nameJa == stored) return t;
        return null;
    }

    // 这个 NPC 有没有某个词条（用中文名查，日文生成的也能认）。
    public static bool HasTrait(NpcData npc, string zhName)
    {
        if (npc == null || npc.traits == null) return false;
        foreach (string s in npc.traits)
        {
            TraitPool.Trait t = FindTrait(s);
            if ((t != null && t.name == zhName) || s == zhName) return true;
        }
        return false;
    }

    public static int ProfessionIndex(string prof)
    {
        if (string.IsNullOrEmpty(prof)) return -1;
        for (int i = 0; i < TraitPool.Professions.Length; i++)
            if (TraitPool.Professions[i] == prof || (i < TraitPool.ProfessionsJa.Length && TraitPool.ProfessionsJa[i] == prof)) return i;
        return -1;
    }

    static string PetName(NpcActorProfile p, bool jp) => jp ? PetNamesJa[p.petName % PetNamesJa.Length] : PetNamesZh[p.petName % PetNamesZh.Length];

    // 一段简短的背景（NPC 闲聊的人物卡用）。
    public static string Brief(NpcActorProfile p, bool jp)
    {
        string pet = p.petKind >= 0 ? (jp ? "、" + PetKindsJa[p.petKind] + "の" + PetName(p, true) + "を飼っている" : "，养了一只叫" + PetName(p, false) + "的" + PetKinds[p.petKind]) : "";
        string fam = jp ? (p.marital == 1 ? "既婚" : (p.marital == 3 ? "連れ合いを亡くした" : "独身")) : (p.marital == 1 ? "已婚" : (p.marital == 3 ? "丧偶" : "单身"));
        string kids = p.children > 0 ? (jp ? "、子ども" + p.children + "人" : "，有" + p.children + "个孩子") : "";
        return jp
            ? p.age + "歳、以前は" + ProfessionName(p, true) + "。" + fam + kids + pet + "。趣味は" + HobbyLine(p, true) + "。"
            : p.age + "岁，以前是" + ProfessionName(p, false) + "；" + fam + kids + pet + "；爱好" + HobbyLine(p, false) + "。";
    }

    // ---------------- 词库 ----------------

    static readonly string[] HometownsZh =
    {
        "四川成都", "湖南长沙", "山东青岛", "河南洛阳", "广东佛山", "浙江温州", "辽宁大连",
        "陕西西安", "湖北宜昌", "江苏扬州", "云南昆明", "黑龙江哈尔滨", "福建泉州", "安徽合肥"
    };
    static readonly string[] HometownsJa =
    {
        "北海道旭川市", "青森県弘前市", "宮城県仙台市", "長野県松本市", "新潟県長岡市", "静岡県浜松市", "石川県金沢市",
        "広島県尾道市", "愛媛県松山市", "熊本県熊本市", "鹿児島県霧島市", "岡山県倉敷市", "群馬県高崎市", "三重県伊勢市"
    };
    static readonly string[] Hobbies =
    {
        "打篮球", "钓鱼", "跑步", "下象棋", "看书", "打游戏", "做饭", "摄影", "爬山", "唱歌",
        "画画", "养花", "看电影", "弹吉他", "健身", "骑行", "追剧", "打麻将", "写书法", "野外露营"
    };
    static readonly string[] HobbiesJa =
    {
        "バスケットボール", "釣り", "ランニング", "将棋", "読書", "テレビゲーム", "料理", "写真", "登山", "カラオケ",
        "絵を描くこと", "ガーデニング", "映画鑑賞", "ギター", "筋トレ", "サイクリング", "ドラマ鑑賞", "麻雀", "書道", "アウトドア"
    };
    static readonly string[] PetKinds = { "猫", "狗", "仓鼠", "鹦鹉", "兔子", "金鱼" };
    static readonly string[] PetKindsJa = { "猫", "犬", "ハムスター", "インコ", "ウサギ", "金魚" };
    static readonly string[] PetNamesZh = { "辣条", "豆豆", "咪咪", "旺财", "团子", "可乐", "年糕", "布丁", "花卷", "煤球" };
    static readonly string[] PetNamesJa = { "タマ", "ポチ", "モモ", "コタロウ", "ミルク", "きなこ", "ハナ", "ソラ", "チビ", "おもち" };

    // ---------------- 演绎部分的代码模板（LLM 没写好时用）----------------
    // 按性格挑一句。所有模板都是第二人称、一句话。

    static bool Fearful(NpcData n) => n != null && (HasTrait(n, "怯懦") || (n.personality != null && n.personality.bravery <= 40));
    static bool Brave(NpcData n) => n != null && (HasTrait(n, "勇敢") || HasTrait(n, "暴躁") || (n.personality != null && n.personality.bravery >= 65));
    static bool Calm(NpcData n) => n != null && (HasTrait(n, "冷静") || (n.personality != null && n.personality.caution >= 62));
    static bool Warm(NpcData n) => n != null && (HasTrait(n, "温和") || (n.personality != null && n.personality.altruism >= 62));
    static bool Numb(NpcData n) => n != null && HasTrait(n, "麻木");

    // 0=默认 1=胆小 2=勇敢 3=冷静 4=热心 5=麻木（取第一个符合的）
    public static int Mood(NpcData n)
    {
        if (Numb(n)) return 5;
        if (Fearful(n)) return 1;
        if (Calm(n)) return 3;
        if (Brave(n)) return 2;
        if (Warm(n)) return 4;
        return 0;
    }

    public static string ByMood(NpcData n, bool jp, string[] zh, string[] ja)
    {
        string[] pool = jp ? ja : zh;
        int m = Mood(n);
        return m < pool.Length && !string.IsNullOrEmpty(pool[m]) ? pool[m] : pool[0];
    }

    public static string Fallback(NpcActorProfile p, NpcData n, string key, bool jp)
    {
        int pi = p.professionIndex;
        switch (key)
        {
            case "swept_in":
                return pi >= 0 ? (jp ? SweptInJa[pi] : SweptInZh[pi])
                               : (jp ? "いつもと同じ一日のはずだったのに、気づけば渦に呑まれていた。" : "那本该是平常的一天，回过神时你已经被漩涡吞了进来。");
            case "speech_to_ai":
            {
                string s = jp ? SpeechJa[p.attitude] : SpeechZh[p.attitude];
                int soc = n != null && n.personality != null ? n.personality.sociability : 50;
                if (soc >= 65 || HasTrait(n, "话痨")) s += jp ? "口数が多く、つい話し続けてしまう。" : "你话很多，常常说个不停。";
                else if (soc <= 35 || HasTrait(n, "寡言")) s += jp ? "口数は少なく、返事は短い。" : "你话很少，回答简短。";
                return s;
            }
            case "subspace_knowledge":
                return pi >= 0 ? (jp ? KnowledgeJa[pi] : KnowledgeZh[pi])
                               : (jp ? "渦やABA、生存支援AIのことは、ニュースでざっと見たことがあるだけだった。" : "关于漩涡、ABA和生存支援AI，你只在新闻上粗略看过一些。");
            case "whisper_reaction":
                return ByMood(n, jp,
                    new[] { "越回想越头痛，你不想再提这件事。", "你会一遍遍问别人有没有听见，越来越不安，夜里不敢合眼。", "你会骂一句，逼自己别去理会，可那声音总在脑子里打转。", "越回想越头痛，你不想再提这件事。", "你会想起那个人，心里发酸，却不敢跟别人说。", "你只是愣了一会儿，没跟任何人说。" },
                    new[] { "思い出そうとするほど頭が痛くなり、この話はもうしたくない。", "誰かにも聞こえなかったか何度も確かめ、不安がつのって夜も眠れない。", "悪態をついて無理に気にしないようにするが、あの声が頭から離れない。", "思い出そうとするほど頭が痛くなり、この話はもうしたくない。", "あの人のことを思い出して胸が苦しくなるが、誰にも言えない。", "少しぼうっとしただけで、誰にも話さなかった。" });
            case "space_warp":
                return ByMood(n, jp,
                    new[] { "你说不清那是怎么回事，只觉得脚下的地面都不可靠了。", "你最怕哪天自己也被卷走，一想到就手心冒汗。", "你觉得与其害怕，不如找出它的规律。", "你会先确认所有人都在，再试着记下方向的变化。", "你最担心的是有人被卷到看不见的地方，没人去找。", "你觉得被卷到哪里都一样。" },
                    new[] { "何が起きたのか説明できず、足元の地面さえ信用できない気がする。", "いつか自分も飛ばされるのではと思うと、手のひらに汗がにじむ。", "怖がるより、その法則を突き止めた方がいいと考える。", "まず全員そろっているか確かめ、方角の変化を覚えておこうとする。", "誰かが見えない場所へ飛ばされて、探してもらえなくなるのが一番心配だ。", "どこへ飛ばされても同じだと思っている。" });
            case "time_disorder":
                return ByMood(n, jp,
                    new[] { "你觉得时间像被人偷走了一段，心里发毛。", "你开始怀疑自己的记忆，不确定今天到底是第几天。", "你不想被这种事牵着走，照样做该做的事。", "你会在本子上记下日子，用来对照时间到底乱成了什么样。", "你担心大家的身体撑不住这样的消耗。", "你已经不在乎过了多久了。" },
                    new[] { "時間を一部盗まれたような気がして、ぞっとする。", "自分の記憶が信じられなくなり、今日が何日目なのか分からない。", "こんなことに振り回されたくないので、やるべきことを淡々と続ける。", "手帳に日付を書きとめ、時間がどれだけ狂ったのか照らし合わせる。", "みんなの体がこの消耗に耐えられるか心配だ。", "どれだけ時間が経ったかなど、もうどうでもいい。" });
            case "storm":
                return ByMood(n, jp,
                    new[] { "那种风暴让你头皮发麻，你只想等它过去。", "你会第一时间躲进营地，抱紧身边能抓住的东西。", "你会先把同伴拉进营地，再顾自己。", "你会盯着风暴的方向，判断它会不会再来。", "你会挨个确认大家有没有事。", "你站在原地，任由它刮过去。" },
                    new[] { "あの嵐には頭皮がぞわりとする。過ぎ去るのをただ待ちたい。", "真っ先に拠点へ逃げ込み、手近なものにしがみつく。", "まず仲間を拠点へ引き込み、自分のことは後回しにする。", "嵐の向きを見つめ、また来るかどうかを見極めようとする。", "一人ずつ無事かどうか確かめて回る。", "その場に立ったまま、嵐が過ぎるに任せる。" });
            case "causal_reversal":
                return ByMood(n, jp,
                    new[] { "你不敢再相信自己看到的任何东西。", "你觉得这地方在嘲弄你们，一切都不可信。", "你觉得只要盯紧了，总能看穿它的把戏。", "你会拼命想弄清先后顺序，越想越乱。", "你担心大家因此慌了神，会尽量安抚他们。", "顺序乱了就乱了吧，你懒得去想。" },
                    new[] { "自分の目に映るものが、もう何一つ信じられない。", "この場所に嘲笑われている気がして、何もかも信用できない。", "目を離さなければ、いつかからくりを見破れると思っている。", "必死に前後関係を突き止めようとするが、考えるほど混乱する。", "みんなが取り乱さないよう、できるだけなだめようとする。", "順番が狂ったなら狂ったでいい。考えるのも面倒だ。" });
            case "death_stranger":
                return ByMood(n, jp,
                    new[] { "陌生人的死让你很不舒服，你会想起自己也随时可能这样。", "你对陌生人的死亡十分恐惧，害怕有一天会轮到自己。", "你会替他合上眼睛，然后继续往前走。", "你会压下不适，先弄清他是怎么死的，免得别人重蹈覆辙。", "即使素不相识，你也会难过，想至少为他做点什么。", "你只是看了一眼，什么也没说。" },
                    new[] { "見知らぬ人の死は気分が悪い。自分もいつそうなるか分からないと思う。", "見知らぬ人の死がとても怖い。いつか自分の番が来るのではと怯えている。", "目を閉じさせてやり、そのまま前へ進む。", "不快感を抑え、同じ目に遭わないよう、まず死因を確かめる。", "見ず知らずの人でも悲しくなり、せめて何かしてやりたいと思う。", "ちらりと見ただけで、何も言わない。" });
            case "death_friend":
                return ByMood(n, jp,
                    new[] { "你会很久说不出话，心里空了一块。", "你会很悲伤，但比起悲伤，你更恐惧死亡。", "你会把悲伤压在心里，先照顾好还活着的人。", "你会很悲伤，但你是理智的，会先弄清楚他为什么会死。", "你会崩溃大哭，很久都走不出来。", "你沉默了很久，像是什么都感觉不到了。" },
                    new[] { "しばらく言葉が出ず、心にぽっかり穴が開く。", "とても悲しいが、悲しみよりも死への恐怖の方が大きい。", "悲しみは胸にしまい込み、まず生きている者の面倒を見る。", "とても悲しいが、理性的なあなたは、なぜ死んだのかをまず突き止めようとする。", "泣き崩れ、長いあいだ立ち直れない。", "長いこと黙り込み、何も感じなくなったかのようだ。" });
            case "death_self":
                return ByMood(n, jp,
                    new[] { "你不想就这样死在这里，会拼命抓住任何一点希望。", "你很怕死，濒死时会疯狂地向同伴和AI求救，求他们救救你。", "你会咬牙撑着，不想拖累任何人。", "你会尽量冷静地交代还没做完的事。", "你最放不下的是同伴和家人，会托人把话带出去。", "你会出奇地平静，好像早就在等这一天。" },
                    new[] { "こんな所で死にたくない。どんなわずかな望みにもしがみつく。", "死ぬのがとても怖い。死にかけたら、仲間やAIに必死で助けを求める。", "歯を食いしばって耐え、誰の足手まといにもなりたくない。", "やり残したことを、できるだけ冷静に言い残そうとする。", "仲間と家族のことが心残りで、言葉を託そうとする。", "妙に落ち着いていて、まるでこの日を待っていたかのようだ。" });
            case "enemy_human":
                return ByMood(n, jp,
                    new[] { "你会先警告他，他再靠近你就不客气了。", "你会先躲开，找人帮忙。", "你很清楚这个人已经是你的敌人，没什么好说的，在他伤害你之前，你会先动手。", "你会先拉开距离，看准机会再制住他。", "你还是想先劝他放下，但不会犹豫太久。", "你不在乎他想干什么，挡路就推开。" },
                    new[] { "まず警告する。それでも近づくなら容赦しない。", "まず身を隠し、誰かに助けを求める。", "この人間がもう敵だとはっきり分かっている。話すことは何もない。傷つけられる前に、こちらから手を出す。", "まず距離をとり、隙を見て取り押さえる。", "まずは説得して武器を下ろさせたいが、長くは迷わない。", "相手が何をしたいかなど気にしない。邪魔なら押しのける。" });
            case "enemy_creature":
                return ByMood(n, jp,
                    new[] { "未知的生物让你感到恶心和恐惧，但你清楚，不把它杀掉，它就会杀掉你。", "未知的生物让你恶心又恐惧，你只想逃得越远越好。", "你很清楚，不杀掉它，它就会杀掉你，所以你会冲在前面。", "你会先观察它的弱点，再决定是打还是逃。", "你会先护着身边的人撤退。", "你看着它，竟然不觉得害怕。" },
                    new[] { "得体の知れない生物には吐き気と恐怖を覚えるが、殺さなければ殺されることはよく分かっている。", "得体の知れない生物が気持ち悪くて怖い。できるだけ遠くへ逃げたい。", "殺さなければ殺される。だから自分が先頭に立つ。", "まず弱点を探り、戦うか逃げるかを決める。", "まずそばにいる人をかばいながら退く。", "それを見ても、なぜか怖いと感じない。" });
        }
        return "";
    }

    public static List<string> FallbackWhispers(NpcActorProfile p, NpcData n, bool jp)
    {
        List<string> list = new List<string>();
        int pi = p.professionIndex;
        list.Add(pi >= 0 ? (jp ? WhisperJobJa[pi] : WhisperJobZh[pi])
                         : (jp ? "昔の職場で誰かが自分を呼ぶ声が聞こえた気がする" : "你仿佛听见以前单位里有人在叫你"));

        string pet = p.petKind >= 0 ? PetName(p, jp) : null;
        if (pet != null) list.Add(jp ? pet + "が玄関で自分を呼んで鳴いている声が聞こえた気がする" : "你仿佛听见" + pet + "在家门口叫你");
        else if (p.children > 0) list.Add(jp ? "子どもが「早く帰ってきて」と呼ぶ声が聞こえた気がする" : "你仿佛听见孩子在喊你快点回家");
        else if (p.marital == 1) list.Add(jp ? "連れ合いが台所からご飯だよと呼ぶ声が聞こえた気がする" : "你仿佛听见爱人在厨房里叫你吃饭");
        else list.Add(jp ? "母が電話の向こうで、いつ帰ってくるのかと聞く声が聞こえた気がする" : "你仿佛听见母亲在电话那头问你什么时候回家");

        switch (p.lostRelative)
        {
            case 0: list.Add(jp ? "亡くなった祖母が、子どもの頃の子守唄を歌ってくれる声が聞こえた気がする" : "你仿佛听见去世的奶奶在给你唱小时候的童谣"); break;
            case 1: list.Add(jp ? "亡くなった父が、低い声で自分の名前を呼んでいるのが聞こえた気がする" : "你仿佛听见去世的父亲在低声叫你的名字"); break;
            case 2: list.Add(jp ? "亡くなった母が、夕飯ができたと呼ぶ声が聞こえた気がする" : "你仿佛听见去世的母亲在叫你回家吃饭"); break;
            case 3: list.Add(jp ? "亡くなった連れ合いが、すぐそばで溜め息をつくのが聞こえた気がする" : "你仿佛听见去世的爱人就在身边叹气"); break;
            case 4: list.Add(jp ? "亡くした兄弟が、子どもの頃の遊びに誘う声が聞こえた気がする" : "你仿佛听见去世的兄弟在叫你一起去玩，就像小时候那样"); break;
            default: list.Add(jp ? "同僚たちが自分の陰口を叩いているのが聞こえた気がする" : "你仿佛听见同事们在背后对你议论纷纷"); break;
        }
        return list;
    }

    static readonly string[] SweptInZh =
    {
        "那天你刚改完作业，走出校门时，眼前的街道突然扭成了一个漩涡。",
        "那天你刚下夜班，在医院停车场里，四周的空气突然像水面一样旋转起来。",
        "那天你在执勤，接到附近出现异常的通报，赶过去时就被卷了进去。",
        "那天你在出警的路上，消防车开进了一团怪雾，再出来时已经在这里了。",
        "那天你在图书馆复习，一抬头，窗外的天空裂开了一道漩涡。",
        "那天你在加班，所有仪表同时失灵，下一秒你就到了这里。",
        "那天你在后厨备菜，灯一闪，再睁眼时手里还握着菜刀。",
        "那天你在巡逻，追着一个报警的人拐进巷子，巷子尽头是一道漩涡。",
        "那天你正在漩涡封锁线外采访，封锁线突然朝你们这边吞了过来。",
        "那天你在地里干活，田埂那头卷起一阵不对劲的风，你就被卷了进来。"
    };
    static readonly string[] SweptInJa =
    {
        "その日、採点を終えて校門を出たところで、目の前の通りが渦にねじれた。",
        "夜勤明け、病院の駐車場で、周りの空気が水面のように渦を巻きはじめた。",
        "任務中、近くで異常が起きたと通報を受け、駆けつけたところで巻き込まれた。",
        "出動の途中、消防車ごと奇妙な霧に入り、抜けたときにはもうここにいた。",
        "図書館で勉強していて、顔を上げると窓の外の空に渦が裂けていた。",
        "残業中、すべての計器が同時に狂い、次の瞬間にはここにいた。",
        "厨房で仕込みをしていたら照明がまたたき、目を開けると包丁を握ったままだった。",
        "巡回中、通報者を追って路地に入ると、その突き当たりに渦があった。",
        "渦の封鎖線の外で取材していたとき、封鎖線ごとこちらへ呑み込まれた。",
        "畑仕事の最中、あぜ道の向こうから妙な風が巻き起こり、そのまま巻き込まれた。"
    };
    static readonly string[] SpeechZh =
    {
        "你把AI当成救命稻草，遇事先问它，说话里带着恳求，常追问「接下来怎么办」。",
        "你把AI当成有用的搭档，说话直接、讲效率，也会提出自己的看法。",
        "你对AI将信将疑，说话有所保留，常反问它的用意，要它先拿出结果。",
        "你不信任这台机器，说话冷淡带刺，怀疑它只是把你们当实验品。"
    };
    static readonly string[] SpeechJa =
    {
        "AIを命綱だと思っていて、何でもまず相談し、すがるような口調で「次はどうすればいい」と聞きたがる。",
        "AIを役に立つ相棒だと考えていて、率直で効率的に話し、自分の意見も言う。",
        "AIを半信半疑で見ていて、言葉を選び、よくその意図を問い返し、まず結果を見せろと思っている。",
        "この機械を信用しておらず、冷たくとげのある口調で話し、自分たちを実験台にしているのではと疑っている。"
    };
    static readonly string[] KnowledgeZh =
    {
        "关于漩涡、ABA和生存支援AI，你只在新闻上粗略看过一些，还在课上提醒过学生别靠近封锁区，没想到自己会被卷进来。",
        "你在医院的内部通知里看过ABA的说明，知道被卷入者会出现精神异常，但具体情况一无所知。",
        "你接到过ABA的协查命令，知道漩涡周边要封锁，也听说调查局在研发支援AI，但细节都是保密的。",
        "你参加过漩涡周边的疏散演练，知道ABA的存在，但从没想过有一天要靠一个AI活命。",
        "你在网上刷到过漩涡的视频，当时以为是特效；ABA说会投放生存支援AI，你只当是新闻。",
        "你在技术论坛上看过关于ABA支援AI的讨论，对它的原理有点好奇，但从没见过真的。",
        "你只在店里的电视新闻上瞄到过几眼漩涡的报道，觉得那离自己很远。",
        "你接到过上级关于漩涡和ABA的通报，知道要配合封锁，但里面到底怎么回事，你也不清楚。",
        "你跟踪报道过漩涡事件，知道的比普通人多一点：先遣队失联、ABA对外口径含糊，你一直怀疑他们隐瞒了什么。",
        "你只在电视里看到过几眼漩涡的新闻，觉得那是城里的事，什么ABA、AI，你都不太懂。"
    };
    static readonly string[] KnowledgeJa =
    {
        "渦やABA、生存支援AIのことはニュースでざっと見ただけで、授業で生徒に封鎖区域へ近づくなと注意したこともあったが、まさか自分が巻き込まれるとは思わなかった。",
        "病院の内部通達でABAの説明を読み、巻き込まれた人には精神の異常が出ると知っていたが、詳しいことは何も知らない。",
        "ABAの協力要請を受けたことがあり、渦の周辺が封鎖されることや、調査局が支援AIを開発していることは聞いていたが、詳細は機密だった。",
        "渦の周辺での避難訓練に参加したことがあり、ABAの存在は知っていたが、AIに命を預ける日が来るとは思わなかった。",
        "ネットで渦の動画を見たことはあったが、そのときは特撮だと思っていた。ABAが生存支援AIを送り込むと発表したのも、ただのニュースだと思っていた。",
        "技術系の掲示板でABAの支援AIについての議論を読み、仕組みに少し興味はあったが、本物を見たことはない。",
        "店のテレビのニュースで渦の報道を何度かちらりと見ただけで、自分には遠い話だと思っていた。",
        "上からの通達で渦とABAのことは聞いていて、封鎖に協力しろと言われていたが、中で何が起きているのかは知らなかった。",
        "渦の事件を追って取材していたので、普通の人より少しは詳しい。先遣隊が消息を絶ったこと、ABAの公式発表が曖昧なこと――何かを隠していると、ずっと疑っていた。",
        "テレビで渦のニュースを何度か見ただけで、都会の話だと思っていた。ABAだのAIだの、よく分からない。"
    };
    static readonly string[] WhisperJobZh =
    {
        "你仿佛听见曾经的学生在问你课上的问题",
        "你仿佛听见监护仪的报警声里，有个病人在叫你的名字",
        "你仿佛听见战友在无线电里呼叫你的代号",
        "你仿佛听见火场深处有人在喊救命",
        "你仿佛听见老师在点你的名字，全班都在笑",
        "你仿佛听见机器有规律的轰鸣里，混着有人在说话",
        "你仿佛听见后厨有人在喊你出菜",
        "你仿佛听见对讲机里报出了你家的地址",
        "你仿佛听见采访录音里冒出一个陌生人的声音，在念你的名字",
        "你仿佛听见田里的风声里夹着牛的叫声"
    };
    static readonly string[] WhisperJobJa =
    {
        "かつての教え子が授業のことを質問してくる声が聞こえた気がする",
        "心電図モニターの警報音にまじって、患者が自分の名前を呼ぶ声が聞こえた気がする",
        "戦友が無線で自分のコールサインを呼んでいる声が聞こえた気がする",
        "火事場の奥で誰かが助けを呼ぶ声が聞こえた気がする",
        "先生が自分の名前を呼び、クラス中が笑っている声が聞こえた気がする",
        "機械の規則正しい唸りにまじって、誰かが話している声が聞こえた気がする",
        "厨房で誰かが早く料理を出せと怒鳴る声が聞こえた気がする",
        "無線が自分の家の住所を読み上げる声が聞こえた気がする",
        "取材の録音に、知らない人の声が自分の名前を読み上げているのが聞こえた気がする",
        "畑を渡る風の音にまじって、牛の鳴き声が聞こえた気がする"
    };
}
