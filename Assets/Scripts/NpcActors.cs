using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

// 演员提示词的管理器：
//   开局（NpcMemory.BeginSession 里调用 BeginSession）→ 每个 NPC：
//     1. 代码立刻生成固定事实 + 用模板补齐演绎部分（马上就能用），并按性格设定初始信任 npc.playerTrust；
//     2. 往后台队列放一个任务，让本地 LLM 写演绎部分（写好、检查合格后替换模板）；
//     3. 写文件：SessionData/actor_npc_xx.txt（演员提示词全文，就是喂给 LLM 的那段）+ actor_npc_xx.json（结构化数据）。
//   以后中途加入新 NPC 时，调用 NpcActors.Create(npc) 即可。
//   和 NPC 对话时，NpcAgent 用 NpcActors.Render(npc) 取全文，放在提示词最前面。
public static class NpcActors
{
    static readonly Dictionary<string, NpcActorProfile> _profiles = new Dictionary<string, NpcActorProfile>();
    static int _session;   // 每开一局 +1：上一局排队的任务回来时直接作废

    public static void BeginSession(WorldState w)
    {
        _session++;
        _profiles.Clear();
        if (LlmJobQueue.Instance != null) LlmJobQueue.Instance.Clear();
        AiScheduler.ClearQueued(); // 上一局还在排队的 AI 请求作废
        NpcIntentBrain.ClearAll(); // 上一局 NPC 自己的计划清空
        NpcSociety.ClearAll();     // 开会 / 分工状态清空（领导存在世界数据里，新世界自然没有）
        if (w == null) return;
        foreach (NpcData n in w.npcs) Create(n);
        Debug.Log("[演员提示词] 已用代码模板生成 " + _profiles.Count + " 份，正在后台让 LLM 补写演绎部分。");
    }

    // 为一个 NPC 生成演员提示词（固定事实 + 模板），设定初始信任，并排队让 LLM 补写。
    public static NpcActorProfile Create(NpcData npc, bool askLlm = true)
    {
        if (npc == null) return null;
        NpcActorProfile p = ActorFacts.Generate(npc);
        npc.playerTrust = p.initialTrust; // 初始信任由性格决定（代码算），不再纯随机
        _profiles[npc.id] = p;
        Save(p);
        if (askLlm) EnqueueGeneration(p);
        return p;
    }

    public static NpcActorProfile Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        _profiles.TryGetValue(id, out NpcActorProfile p);
        return p;
    }

    // 演员提示词全文（当前语言）。没有档案时现场补一份（不排队）。
    public static string Render(NpcData npc)
    {
        if (npc == null) return "";
        NpcActorProfile p = Get(npc.id);
        if (p == null)
        {
            p = ActorFacts.Generate(npc);
            p.initialTrust = Mathf.RoundToInt(npc.playerTrust);
            p.attitude = ActorFacts.AttitudeOf(p.initialTrust);
            _profiles[npc.id] = p;
        }
        return p.Render(npc, LanguageGuard.IsJp);
    }

    // 简短背景（NPC 闲聊的人物卡用）。
    public static string Brief(NpcData npc, bool jp)
    {
        NpcActorProfile p = npc != null ? Get(npc.id) : null;
        return p != null ? ActorFacts.Brief(p, jp) : "";
    }

    // ============ 让 LLM 写演绎部分 ============

    static void EnqueueGeneration(NpcActorProfile p)
    {
        int session = _session;
        string id = p.id;
        LlmJobQueue.Ensure().Enqueue(new LlmJob
        {
            name = "演员提示词 " + p.name,
            priority = 10,
            maxTokens = 1100,
            system = () => BuildGenerationPrompt(Get(id), Npc(id), LanguageGuard.IsJp),
            user = () => LanguageGuard.Pick("请为「" + p.name + "」写出这份设定。", "「" + p.name + "」の設定を書いてください。"),
            accept = raw => session != _session || Apply(Get(id), Npc(id), raw), // 上一局的结果直接作废（算完成）
            stillValid = () => session == _session && Get(id) != null && Npc(id) != null,
        });
    }

    static NpcData Npc(string id) => WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(id) : null;

    // 给 LLM 的写作提示词（整段按当前界面语言写；附一个示例，示例只演示写法和长度）。
    static string BuildGenerationPrompt(NpcActorProfile p, NpcData npc, bool jp)
    {
        StringBuilder sb = new StringBuilder();
        if (jp)
        {
            sb.AppendLine("あなたは人物設定の作家である。亜空間サバイバルドラマに登場する一人の人物について、「役者への指示書」の補足部分を書く。");
            sb.AppendLine("【世界】現代の地球に、近くにいる一般市民を呑み込む空間の渦が現れるようになった。渦の内側は「亜空間」と呼ばれる異常な領域で、時間・空間・因果のすべてが信用できず、長くいるほど精神がむしばまれる。" +
                          "各国は共同で「特異現象調査局」(ABA)を設立した。ABAは巻き込まれた人々のもとへ「生存支援AI」を送り込んでいる。それは体を持たず、信号を通じて話しかけ、生存者が生き延びるよう導くAIである。一般市民はこうしたことをニュースで少し見た程度しか知らない。");
            sb.AppendLine("【この人物の確定した事実（必ず守り、変えたり矛盾させたりしないこと）】");
            sb.Append(p.FactSheet(npc, true));
            sb.AppendLine("【書く内容】二人称「あなた」で書く。各項目1〜2文、簡潔かつ具体的に。上の事実・性格と必ず一致させること：");
            sb.AppendLine("- swept_in：亜空間に巻き込まれた瞬間、あなたは何をしていたか");
            sb.AppendLine("- speech_to_ai：AIとの話し方（口調、長さ、AIを何と呼ぶか）。最初の態度を反映させる");
            sb.AppendLine("- subspace_knowledge：巻き込まれる前に、亜空間・ABA・生存支援AIについて知っていたこと（一般市民程度。職業と関係があってよい）");
            sb.AppendLine("- whisper：配列で3つ。「不可解な囁き」が起きたとき聞こえるかもしれない3種類の声。必ずあなた自身の人生（職業・家族・過去）に由来するもので、それぞれ「〜が聞こえた気がする」で終える");
            sb.AppendLine("- whisper_reaction：囁きを聞いた後のあなたの反応");
            sb.AppendLine("- space_warp / time_disorder / storm / causal_reversal：空間の歪み／時間の乱れ／負のエネルギーの嵐／因果の逆転に向き合うときの態度");
            sb.AppendLine("- death_stranger / death_friend / death_self：見知らぬ人の死／仲間の死／自分が死にかけたときの態度と行動");
            sb.AppendLine("- enemy_human / enemy_creature：人間の敵／亜空間の生物に向き合うときの態度と行動");
            sb.AppendLine("【禁止】ゲーム・プレイヤー・ロールプレイ・プロンプトに触れない。確定した事実を変えない。家族とペット以外の人名を出さない。自然な日本語だけで書き、中国語を混ぜない。");
            sb.AppendLine("【例】（別の人物：元バス運転手、52歳。書き方と長さを示すだけで、内容は絶対に写さないこと）");
            sb.AppendLine("{\"swept_in\":\"あなたは最終便のハンドルを握っていて、トンネルを抜けた瞬間、乗客ごと渦に呑まれた。\",\"speech_to_ai\":\"AIのことは「機械さん」と呼び、ぶっきらぼうだが聞かれたことには律儀に答える。\",\"subspace_knowledge\":\"運転席のラジオで渦のニュースを聞き流していた程度で、ABAが何をしているのかは知らなかった。\",\"whisper\":[\"降車ボタンのチャイムが何度も鳴る音が聞こえた気がする\",\"亡くなった妻が「お帰り」と言う声が聞こえた気がする\",\"見覚えのない乗客が自分の名前を呼ぶ声が聞こえた気がする\"],\"whisper_reaction\":\"あなたは首を振って、聞こえなかったふりをする。\",\"space_warp\":\"道がいきなり変わるのは慣れっこだと強がるが、内心は冷や汗をかいている。\",\"time_disorder\":\"あなたは腕時計を何度も見直し、時刻表どおりにいかない一日に苛立つ。\",\"storm\":\"あなたは若い者を先に避難させ、自分は最後に入る。\",\"causal_reversal\":\"あなたは考えるのをやめ、目の前のことだけを片づける。\",\"death_stranger\":\"あなたは帽子を取って黙祷し、それ以上は何も言わない。\",\"death_friend\":\"あなたは一晩中黙って座り込み、翌朝には何事もなかったように働く。\",\"death_self\":\"あなたは自分の家族への伝言を誰かに託そうとする。\",\"enemy_human\":\"あなたは相手を落ち着かせようと話しかけるが、仲間に手を出せば容赦しない。\",\"enemy_creature\":\"あなたは震える手で棒を握り、若い者の前に立つ。\"}");
            sb.AppendLine("【出力形式】JSONオブジェクトを1つだけ出力し、それ以外は何も書かないこと（キーは英語、値は日本語）：");
            sb.Append("{\"swept_in\":\"…\",\"speech_to_ai\":\"…\",\"subspace_knowledge\":\"…\",\"whisper\":[\"…\",\"…\",\"…\"],\"whisper_reaction\":\"…\",\"space_warp\":\"…\",\"time_disorder\":\"…\",\"storm\":\"…\",\"causal_reversal\":\"…\",\"death_stranger\":\"…\",\"death_friend\":\"…\",\"death_self\":\"…\",\"enemy_human\":\"…\",\"enemy_creature\":\"…\"}");
        }
        else
        {
            sb.AppendLine("你是一名人物设定作家，要为一部亚空间生存剧里的一个人物，写「演员提示词」的补充部分。");
            sb.AppendLine("【世界】现代地球上开始出现会吞噬附近普通市民的空间漩涡。漩涡内部是名为「亚空间」的异常领域：时间、空间、因果都不可靠，待得越久，精神越被侵蚀。" +
                          "各国联合成立了「特异现象调查局」(ABA)。ABA 向被卷入的人投放了「生存支援AI」——它没有身体，只能通过信号说话，引导幸存者活下去。普通市民对这些只在新闻上看过一点。");
            sb.AppendLine("【这个人的固定事实（必须遵守，不得修改或矛盾）】");
            sb.Append(p.FactSheet(npc, false));
            sb.AppendLine("【你要写的内容】用第二人称「你」来写，每项1~2句，简洁、具体，必须和上面的事实、性格一致：");
            sb.AppendLine("- swept_in：被卷进亚空间的那一刻，你正在做什么");
            sb.AppendLine("- speech_to_ai：你和AI说话的方式（语气、长短、怎么称呼它），要体现你的初始态度");
            sb.AppendLine("- subspace_knowledge：卷入前你对亚空间、ABA、生存支援AI的了解（只是普通市民的程度，可以和职业有关）");
            sb.AppendLine("- whisper：数组，3句。「神秘的低语」发生时你可能听见的3种声音，必须来自你自己的人生（职业、家人、过去），每句以「你仿佛听见」开头");
            sb.AppendLine("- whisper_reaction：听见低语之后你的反应");
            sb.AppendLine("- space_warp / time_disorder / storm / causal_reversal：你面对空间扭曲 / 时间失序 / 负能量风暴 / 因果倒置时的态度");
            sb.AppendLine("- death_stranger / death_friend / death_self：你面对陌生人死亡 / 朋友死亡 / 自己濒死时的态度和行为");
            sb.AppendLine("- enemy_human / enemy_creature：你面对人类敌人 / 亚空间生物时的态度和行为");
            sb.AppendLine("【禁止】不得提到游戏、玩家、角色扮演、提示词；不得改动固定事实；除了家人和宠物，不得出现其他人名；只用自然的简体中文，不要夹杂日语。");
            sb.AppendLine("【示例】（另一个人：一名52岁的退休公交司机。只演示写法和长度，内容绝对不要照抄）");
            sb.AppendLine("{\"swept_in\":\"你正开着末班车，刚驶出隧道，就连车带乘客一起被漩涡吞了进去。\",\"speech_to_ai\":\"你管AI叫「机器」，说话生硬，但问什么答什么，一板一眼。\",\"subspace_knowledge\":\"你只在驾驶座的收音机里听过几耳朵漩涡的新闻，不知道ABA到底在干什么。\",\"whisper\":[\"你仿佛听见下车铃一遍遍地响\",\"你仿佛听见去世的老伴在说「回来啦」\",\"你仿佛听见一个没见过的乘客在叫你的名字\"],\"whisper_reaction\":\"你摇摇头，假装什么也没听见。\",\"space_warp\":\"你嘴上说路突然变了你见多了，心里其实直冒冷汗。\",\"time_disorder\":\"你一遍遍看手表，受不了这种不按时刻表走的日子。\",\"storm\":\"你会让年轻人先躲进去，自己最后一个进。\",\"causal_reversal\":\"你干脆不去想，只管把眼前的事做好。\",\"death_stranger\":\"你会摘下帽子默哀，不再多说一句。\",\"death_friend\":\"你会默默坐一整夜，第二天照常干活，像什么都没发生。\",\"death_self\":\"你会想托人把话带给家里人。\",\"enemy_human\":\"你会先试着让对方冷静，但他敢动同伴，你绝不手软。\",\"enemy_creature\":\"你会攥紧棍子，哆嗦着挡在年轻人前面。\"}");
            sb.AppendLine("【输出格式】只输出一个 JSON 对象，不要任何多余文字（键名用英文，值用中文）：");
            sb.Append("{\"swept_in\":\"…\",\"speech_to_ai\":\"…\",\"subspace_knowledge\":\"…\",\"whisper\":[\"…\",\"…\",\"…\"],\"whisper_reaction\":\"…\",\"space_warp\":\"…\",\"time_disorder\":\"…\",\"storm\":\"…\",\"causal_reversal\":\"…\",\"death_stranger\":\"…\",\"death_friend\":\"…\",\"death_self\":\"…\",\"enemy_human\":\"…\",\"enemy_creature\":\"…\"}");
        }
        return sb.ToString();
    }

    // 示例里的特征词：输出里出现就说明模型在照抄示例，该项作废。
    static readonly string[] ExampleMarks = { "公交", "末班车", "下车铃", "收音机", "老伴", "バス", "最終便", "降車ボタン", "ラジオ", "機械さん", "摘下帽子", "帽子を取" };

    // 解析 LLM 输出，逐项检查后写回档案。合格的项数够多才算成功（不合格的项继续用模板）。
    public static bool Apply(NpcActorProfile p, NpcData npc, string raw)
    {
        if (p == null || npc == null) return true; // 人没了：任务算结束
        JObject obj = ParseObject(raw);
        if (obj == null) return false;

        bool jp = LanguageGuard.IsJp;
        int good = 0;
        Dictionary<string, string> values = new Dictionary<string, string>();
        foreach (string key in NpcActorProfile.TextKeys)
        {
            string v = CleanField(StringOf(obj, key), npc, jp, 140, 8);
            if (v != null) { values[key] = v; good++; }
        }
        List<string> whispers = new List<string>();
        JToken wt = obj["whisper"] ?? obj["whispers"];
        if (wt is JArray arr)
        {
            foreach (JToken t in arr)
            {
                string s = t.Type == JTokenType.String ? (string)t : (t is JObject o ? StringOf(o, "text") : null);
                s = CleanField(s, npc, jp, 70);
                if (s != null && whispers.Count < 3) whispers.Add(s);
            }
        }
        else if (wt != null && wt.Type == JTokenType.String)
        {
            foreach (string part in ((string)wt).Split(new[] { '\n', '；', ';', '／' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = CleanField(part, npc, jp, 70);
                if (s != null && whispers.Count < 3) whispers.Add(s);
            }
        }

        // 一半以上的项合格才采用（否则多半整段跑偏了，重试）
        if (good < NpcActorProfile.TextKeys.Length / 2) return false;

        p.lang = jp ? "ja" : "zh";
        foreach (string key in NpcActorProfile.TextKeys)
            p.SetText(key, values.TryGetValue(key, out string v) ? v : null); // 不合格的项留空 → 用模板
        p.whispers = whispers.Count >= 2 ? whispers : new List<string>();
        p.source = "llm";
        Save(p);
        Debug.Log("[演员提示词] " + npc.npcName + " 的演绎部分已由 LLM 写好（合格 " + good + "/" + NpcActorProfile.TextKeys.Length + " 项，低语 " + whispers.Count + " 条）。");
        return true;
    }

    static string StringOf(JObject obj, string key)
    {
        JToken t = obj[key];
        return t != null && t.Type == JTokenType.String ? (string)t : null;
    }

    static JObject ParseObject(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            int s = raw.IndexOf('{'), e = raw.LastIndexOf('}');
            if (s < 0 || e <= s) return null;
            return JObject.Parse(raw.Substring(s, e - s + 1));
        }
        catch { return null; }
    }

    // 一项文字的清洗 + 检查：去引号、改成第二人称（中文）、长度、语言、穿帮词、照抄示例。不合格返回 null。
    public static string CleanField(string s, NpcData npc, bool jp, int maxLen, int minLen = 6)
    {
        if (string.IsNullOrEmpty(s)) return null;
        s = s.Trim().Trim('"', '“', '”', '「', '」', '『', '』').Trim();
        if (s.Length < minLen || s.Length > maxLen) return null;
        if (s.Contains("…") && s.Replace("…", "").Trim().Length < 4) return null; // 只有省略号
        if (!LanguageGuard.TextMatches(s, jp ? Language.Japanese : Language.Chinese, true)) return null;
        foreach (string bad in ForbiddenWords) if (s.Contains(bad)) return null;
        foreach (string m in ExampleMarks) if (s.Contains(m)) return null;
        if (!jp && s.Contains("我") && !s.Contains("你"))
            s = s.Replace("我们", "你们").Replace("我", "你"); // 演员提示词统一用第二人称
        if (npc != null && !string.IsNullOrEmpty(npc.npcName))
            s = Regex.Replace(s, "^" + Regex.Escape(npc.npcName) + "[，,：:]?", jp ? "あなたは" : "你");
        return s;
    }

    static readonly string[] ForbiddenWords =
    {
        "游戏", "玩家", "角色扮演", "提示词", "剧本", "AI助手", "ChatGPT", "人工智能助手",
        "ゲーム", "プレイヤー", "ロールプレイ", "プロンプト", "脚本", "AIアシスタント"
    };

    // ============ 文件 ============

    static void Save(NpcActorProfile p)
    {
        NpcData npc = Npc(p.id);
        string header = LanguageGuard.Pick(
            "# " + p.name + " 的演员提示词（来源：" + (p.source == "llm" ? "LLM 生成 + 代码固定事实" : "代码模板，LLM 补写中") + "）\n# 这段文字会放在和 " + p.name + " 对话的提示词最前面。\n\n",
            "# " + p.name + " の役者への指示書（出典：" + (p.source == "llm" ? "LLM生成＋コードの確定事実" : "コードのテンプレート、LLMで補筆中") + "）\n# この文章は、" + p.name + " との会話のプロンプトの先頭に置かれる。\n\n");
        NpcMemory.WriteText("actor_" + p.id + ".txt", header + p.Render(npc, LanguageGuard.IsJp));
        NpcMemory.WriteJson("actor_" + p.id + ".json", p);
    }
}
