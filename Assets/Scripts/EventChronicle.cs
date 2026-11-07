using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// 事件里某个 NPC 的第一视角：他经历到了什么、他怎么看。
[Serializable]
public class EventPerception
{
    public string npcId, npcName;
    public string role;          // "subject" 当事人（事情发生在他身上）/ "witness" 目击者
    public bool friendOfSubject; // 当事人是不是他的朋友（死亡事件用：朋友死了 vs 陌生人死了）
    public int voiceIndex = -1;  // 低语：他听见的是演员提示词里的第几种声音
    public string llmPerceived, llmOpinion, llmLang; // LLM 补写的经历和看法（空 = 用代码模板）
}

// 一条事件记录。
[Serializable]
public class EventRecord
{
    public int id;
    public int day;
    public string type;          // whisper_vision / space_warp / time_disorder / storm / causal_reversal / creature / death / breakdown / other
    public string text;          // 客观描述（导演写的那句，或代码写的）
    public string lang;          // text 是用哪种语言写的
    public string subjectId, subjectName;
    public bool subjectBroken;   // 死亡事件：死者是不是在崩解之后死的
    public List<EventPerception> perceptions = new List<EventPerception>();
}

// 事件编年史（纯代码记录 + LLM 补写看法）：
//   · 每发生一件事（导演事件、怪物逼近、有人崩解、有人死亡），记一条：第几天、什么事、客观描述，
//     以及每个在场 NPC 的第一视角——"你的经历"和"你的看法与推理"。
//   · 经历和看法先由代码按演员提示词立刻写好（例如低语：从他自己的 3 种声音里挑一种），
//     再排队让 LLM 以这个人的身份补写得更具体（写好、检查合格后替换）。
//   · 和 NPC 对话时，按玩家这句话里提到的"第几天 / 昨天 / 低语 / 死"等挑出相关的记录，加上最近几条，
//     放进提示词的【你经历过的事】——玩家问"你还记得第3天发生了什么吗"，NPC 就能答上来；记录里没有的事不会编。
//   · 文件：SessionData/events.txt（给人看的）+ events.json（结构化）。
//   【现在默认只用代码记录】NPC 对事件的反应太啰嗦（事件一多就刷屏），而且每件事都要排队让主模型补写，
//   和领导分工抢算力。所以默认 LlmOpinions = false：只在本地记录"发生过这件事 + 代码模板写的经历和看法"，
//   和 NPC 聊天时照样能问起来。想恢复 LLM 补写，把 LlmOpinions 改成 true。
public static class EventChronicle
{
    public static bool LlmOpinions = false;  // true = 每件事让 LLM 以当事人 / 目击者的身份补写经历和看法
    public static int MaxRefinePerEvent = 3; // 每件事最多让 LLM 补写几个人（当事人优先，其余随机挑目击者）
    const int AlwaysRecent = 3;              // 提示词里总是附上最近几件事
    const int MaxInPrompt = 7;

    static readonly List<EventRecord> _records = new List<EventRecord>();
    static int _session;

    public static IReadOnlyList<EventRecord> All => _records;

    public static void BeginSession()
    {
        _session++;
        _records.Clear();
        WriteFiles();
    }

    // ============ 记录 ============

    public static EventRecord Record(string type, string text, string subjectId)
    {
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w == null) return null;
        type = Normalize(type);
        NpcData subject = string.IsNullOrEmpty(subjectId) ? null : WorldManager.Instance.GetNpcById(subjectId);

        EventRecord r = new EventRecord
        {
            id = _records.Count + 1,
            day = w.day,
            type = type,
            text = string.IsNullOrEmpty(text) ? null : text.Trim(),
            lang = LanguageGuard.IsJp ? "ja" : "zh",
            subjectId = subject != null ? subject.id : null,
            subjectName = subject != null ? subject.npcName : null,
            subjectBroken = subject != null && subject.isBroken,
        };

        foreach (NpcData n in w.npcs)
        {
            if (n.IsDead || n.isBroken) continue; // 死者、已崩解的人不再有"自己的看法"
            EventPerception per = new EventPerception
            {
                npcId = n.id,
                npcName = n.npcName,
                role = subject != null && n.id == subject.id ? "subject" : "witness",
            };
            if (subject != null && per.role == "witness")
            {
                Relationship rel = RelationshipManager.Get(n, subject.id);
                per.friendOfSubject = rel != null && rel.affinity >= 15f;
            }
            if (type == "whisper_vision" && per.role == "subject")
            {
                NpcActorProfile prof = NpcActors.Get(n.id);
                int count = prof != null ? prof.Whispers(n, LanguageGuard.IsJp).Count : 3;
                per.voiceIndex = UnityEngine.Random.Range(0, Mathf.Max(1, count));
            }
            r.perceptions.Add(per);
        }

        _records.Add(r);
        WriteFiles();
        if (LlmOpinions) EnqueueRefinement(r);
        Debug.Log("[事件记录] 第" + r.day + "天 " + TypeName(type, LanguageGuard.IsJp) + "，在场 " + r.perceptions.Count + " 人");
        return r;
    }

    static string Normalize(string type)
    {
        switch (type)
        {
            case "whisper_vision": case "space_warp": case "time_disorder": case "storm":
            case "causal_reversal": case "creature": case "death": case "breakdown":
                return type;
            default:
                return "other";
        }
    }

    public static string TypeName(string type, bool jp)
    {
        switch (type)
        {
            case "whisper_vision": return jp ? "囁きと幻影" : "低语与幻象";
            case "space_warp": return jp ? "空間の歪み" : "空间扭曲";
            case "time_disorder": return jp ? "時間の乱れ" : "时间失序";
            case "storm": return jp ? "負のエネルギーの嵐" : "负能量风暴";
            case "causal_reversal": return jp ? "因果の逆転" : "因果倒置";
            case "creature": return jp ? "何かの接近" : "怪物逼近";
            case "death": return jp ? "死" : "死亡";
            case "breakdown": return jp ? "人格崩壊" : "人格崩解";
            default: return jp ? "異常な出来事" : "异常事件";
        }
    }

    // ============ 文字（当前语言；LLM 写过且语言一致就用 LLM 的，否则用代码模板）============

    // 客观描述。
    public static string Objective(EventRecord r, bool jp)
    {
        if (!string.IsNullOrEmpty(r.text) && r.lang == (jp ? "ja" : "zh")) return r.text;
        string s = r.subjectName ?? "";
        switch (r.type)
        {
            case "whisper_vision": return jp ? "拠点で、" + s + "が存在しない囁きを聞きはじめた。" : "营地里，" + s + "开始听见不存在的低语。";
            case "space_warp": return jp ? s + "が空間の歪みに巻き込まれ、別の場所に現れた。" : s + "被空间扭曲卷走，出现在了别处。";
            case "time_disorder": return jp ? "時間が突然前へ飛び、何日も過ぎてしまった。" : "时间突然向前跳跃，一下子过去了好几天。";
            case "storm": return jp ? "負のエネルギーの嵐が拠点を吹き抜けた。" : "一阵负能量风暴扫过营地。";
            case "causal_reversal": return jp ? "拠点で物事の順番が狂い、結果が原因より先に現れた。" : "营地里的先后顺序乱了：结果先于原因出现。";
            case "creature": return jp ? "何かが拠点に近づいてきた。" : "有什么东西靠近了营地。";
            case "death": return r.subjectBroken ? (jp ? s + "は完全に変異したあと、息絶えた。" : s + "在彻底异变后死去了。") : (jp ? s + "は力尽きて倒れ、死んだ。" : s + "没能撑下去，死了。");
            case "breakdown": return jp ? s + "の精神が完全に崩壊し、体が変異しはじめた。" : s + "的精神彻底崩溃了，身体开始发生可怕的异变。";
            default: return jp ? "拠点で説明のつかない異常が起きた。" : "营地里发生了说不清的异常。";
        }
    }

    // 这个人经历到了什么（第二人称）。
    public static string Perceived(EventRecord r, EventPerception p, NpcData npc, bool jp)
    {
        if (!string.IsNullOrEmpty(p.llmPerceived) && p.llmLang == (jp ? "ja" : "zh")) return p.llmPerceived;
        bool me = p.role == "subject";
        string s = r.subjectName ?? "";
        switch (r.type)
        {
            case "whisper_vision":
                if (me)
                {
                    NpcActorProfile prof = NpcActors.Get(p.npcId);
                    List<string> voices = prof != null ? prof.Whispers(npc, jp) : null;
                    string v = voices != null && voices.Count > 0 ? voices[Mathf.Clamp(p.voiceIndex, 0, voices.Count - 1)].TrimEnd('。', '.') : (jp ? "誰かの声が聞こえた気がする" : "你仿佛听见有人在叫你");
                    return jp ? v + "。声はぼんやりしていて、何と言っているのか聞き取れなかった。" : v + "。那声音很模糊，听不清在说什么。";
                }
                return jp ? s + "が急にぼんやりして、何かの声が聞こえると言い出した。あなた自身には何も聞こえなかった。"
                          : s + "突然神情恍惚，说自己听见了什么声音。你自己什么都没听见。";
            case "space_warp":
                if (me) return jp ? "目の前がぶれて、気づいたときには拠点の外の別の場所に立っていた。" : "你眼前一花，回过神时，已经站在营地外的另一个地方了。";
                return jp ? s + "が目の前で消え、別の場所に現れるのを見た。" : "你亲眼看见" + s + "凭空消失，又出现在了别的地方。";
            case "time_disorder":
                return jp ? "空が明るくなったり暗くなったりして、我に返ると何日も経っていたようだった。" : "天色忽明忽暗，回过神来，好像一下子过去了好几天。";
            case "storm":
                return jp ? "頭皮がぞわりとする嵐が拠点を吹き抜け、頭の中がかき乱された。" : "一阵让人头皮发麻的风暴扫过营地，脑子里乱成一团。";
            case "causal_reversal":
                return jp ? "先に音が聞こえ、しばらくしてからその音を出すものが現れた。順番がめちゃくちゃだった。" : "你先听到了声音，过了一会儿才看见发出声音的东西——先后顺序全乱了。";
            case "creature":
                return jp ? "何かが拠点に近づいてきた。輪郭のはっきりしない影を一瞬見ただけだった。" : "有什么东西靠近了营地，你只瞥见一个说不清的轮廓。";
            case "death":
                return r.subjectBroken ? (jp ? s + "は完全に変異したあと、死んでしまった。" : s + "在彻底异变之后死了。")
                                       : (jp ? s + "が死んだ。" : s + "死了。");
            case "breakdown":
                return jp ? s + "の精神が完全に崩れ、体がおぞましく変わっていくのを見た。" : "你看着" + s + "的精神彻底崩溃，身体一点点变得可怕。";
            default:
                return jp ? "あなたはこれを目にした：" + Objective(r, true) : "你目睹了：" + Objective(r, false);
        }
    }

    // 这个人的看法与推理。
    public static string Opinion(EventRecord r, EventPerception p, NpcData npc, bool jp)
    {
        if (!string.IsNullOrEmpty(p.llmOpinion) && p.llmLang == (jp ? "ja" : "zh")) return p.llmOpinion;
        NpcActorProfile prof = NpcActors.Get(p.npcId);
        bool me = p.role == "subject";
        switch (r.type)
        {
            case "whisper_vision":
                if (me) return ActorText(prof, npc, "whisper_reaction", jp);
                return ActorFacts.ByMood(npc, jp,
                    new[] { "你说不准那是不是真的，只觉得心里发毛。", "你害怕下一个听见声音的会是自己。", "你觉得别被这种东西牵着走，该干嘛干嘛。", "你怀疑那是精神被侵蚀的征兆，暗暗提醒自己保持清醒。", "你担心他，想多陪陪他。", "你觉得这里的人迟早都会这样。" },
                    new[] { "本当なのかどうか分からないが、胸がざわつく。", "次に声を聞くのは自分かもしれないと怯えている。", "こんなものに振り回されず、やるべきことをやればいいと思う。", "精神がむしばまれている兆しではないかと疑い、正気を保てと自分に言い聞かせる。", "彼のことが心配で、そばにいてやりたいと思う。", "ここにいる者はいずれみんなこうなると思っている。" });
            case "space_warp": return ActorText(prof, npc, "space_warp", jp);
            case "time_disorder": return ActorText(prof, npc, "time_disorder", jp);
            case "storm": return ActorText(prof, npc, "storm", jp);
            case "causal_reversal": return ActorText(prof, npc, "causal_reversal", jp);
            case "creature": return ActorText(prof, npc, "enemy_creature", jp);
            case "death": return ActorText(prof, npc, p.friendOfSubject ? "death_friend" : "death_stranger", jp);
            case "breakdown":
                return ActorFacts.ByMood(npc, jp,
                    new[] { "你不敢相信，人竟然会变成那样。", "你害怕自己有一天也会变成那样。", "你告诉自己，绝不能让同样的事落到自己头上。", "你意识到侵蚀是真的，开始盘算怎么保住自己的理智。", "你想救他，却不知道还能做什么。", "你看着他，说不出一句话。" },
                    new[] { "人があんなふうになるなんて、信じられない。", "いつか自分もああなるのではと怯えている。", "同じことが自分に起きてたまるかと、自分に言い聞かせる。", "侵食は本物だと悟り、どうすれば正気を保てるか考えはじめる。", "助けたいのに、もう何ができるのか分からない。", "彼を見つめたまま、一言も出てこない。" });
            default:
                return jp ? "何が起きたのか説明できず、この場所がますますおかしくなっている気がする。" : "你说不清那是怎么回事，只觉得这地方越来越不对劲。";
        }
    }

    static string ActorText(NpcActorProfile prof, NpcData npc, string key, bool jp)
    {
        if (prof != null) return prof.Text(key, npc, jp);
        return npc != null ? ActorFacts.Fallback(ActorFacts.Generate(npc), npc, key, jp) : "";
    }

    // ============ 给对话提示词用 ============

    // 挑出和这句话相关的记录 + 最近几条，写成【你经历过的事】。没有任何记录时返回空串。
    public static string ForPrompt(NpcData npc, string query, bool jp)
    {
        if (npc == null || WorldManager.Instance == null) return "";
        int today = WorldManager.Instance.world.day;

        List<EventRecord> mine = new List<EventRecord>();
        foreach (EventRecord r in _records)
            if (FindPerception(r, npc.id) != null) mine.Add(r);

        HashSet<int> days = MentionedDays(query, today);
        HashSet<string> types = MentionedTypes(query);

        List<EventRecord> picked = new List<EventRecord>();
        foreach (EventRecord r in mine) if (days.Contains(r.day) && picked.Count < MaxInPrompt) picked.Add(r);
        foreach (string t in types)
        {
            int added = 0;
            for (int i = mine.Count - 1; i >= 0 && added < 2; i--)
                if (mine[i].type == t && !picked.Contains(mine[i]) && picked.Count < MaxInPrompt) { picked.Add(mine[i]); added++; }
        }
        for (int i = mine.Count - 1, n = 0; i >= 0 && n < AlwaysRecent && picked.Count < MaxInPrompt; i--, n++)
            if (!picked.Contains(mine[i])) picked.Add(mine[i]);
        picked.Sort((a, b) => a.id.CompareTo(b.id));

        // 玩家问到的日子里什么都没发生：明确告诉模型"那天没什么特别的事"，免得它现编
        List<int> emptyDays = new List<int>();
        foreach (int d in days)
        {
            if (d < 1 || d > today) continue;
            bool any = false;
            foreach (EventRecord r in mine) if (r.day == d) { any = true; break; }
            if (!any) emptyDays.Add(d);
        }
        emptyDays.Sort();

        if (picked.Count == 0 && emptyDays.Count == 0) return "";
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(jp
            ? "【あなたが経験した出来事】（あなた自身が経験した出来事の記録。聞かれたらこれに基づいて答えること。感情や不確かさをにじませてよいが、ここにないことは知らないので、作り話をしない）"
            : "【你经历过的事】（你亲身经历的事件记录。被问起时依据这里回答，可以带着你的情绪和不确定；这里没有的事，你不知道，不要编造）");
        foreach (EventRecord r in picked)
        {
            EventPerception p = FindPerception(r, npc.id);
            sb.AppendLine((jp ? "・" + r.day + "日目【" : "・第" + r.day + "天【") + TypeName(r.type, jp) + "】" + Objective(r, jp));
            string role = p.role == "subject" ? (jp ? "（あなたの身に起きたこと）" : "（这件事发生在你身上）") : (jp ? "（そばで見ていた）" : "（你在一旁目睹）");
            sb.AppendLine("　" + role + (jp ? "あなたの体験：" : "你的经历：") + Perceived(r, p, npc, jp));
            sb.AppendLine("　" + (jp ? "あなたの考え：" : "你的看法：") + Opinion(r, p, npc, jp));
        }
        foreach (int d in emptyDays)
            sb.AppendLine(jp ? "・（" + d + "日目は、特に何も起きなかったと記憶している）" : "・（你记得第" + d + "天没有发生什么特别的事）");
        return sb.ToString();
    }

    static EventPerception FindPerception(EventRecord r, string npcId)
    {
        foreach (EventPerception p in r.perceptions) if (p.npcId == npcId) return p;
        return null;
    }

    // 这句话里提到的日子（第3天 / 第三天 / 3日目 / 昨天 / 前天 / 3天前 / 今天 / 刚才 …）。
    public static HashSet<int> MentionedDays(string q, int today)
    {
        HashSet<int> days = new HashSet<int>();
        if (string.IsNullOrEmpty(q)) return days;
        foreach (Match m in Regex.Matches(q, @"第\s*([0-9０-９]+|[零一二两三四五六七八九十]+)\s*[天日]"))
        { int n = ParseNumber(m.Groups[1].Value); if (n > 0) days.Add(n); }
        foreach (Match m in Regex.Matches(q, @"([0-9０-９]+|[一二三四五六七八九十]+)\s*日目"))
        { int n = ParseNumber(m.Groups[1].Value); if (n > 0) days.Add(n); }
        foreach (Match m in Regex.Matches(q, @"([0-9０-９]+|[一二两三四五六七八九十]+)\s*(?:天前|日前)"))
        { int n = ParseNumber(m.Groups[1].Value); if (n > 0) days.Add(today - n); }
        if (q.Contains("大前天") || q.Contains("さきおととい")) days.Add(today - 3);
        else if (q.Contains("前天") || q.Contains("一昨日") || q.Contains("おととい")) days.Add(today - 2);
        if (q.Contains("昨天") || q.Contains("昨晚") || Regex.IsMatch(q, "(?<!一)昨日") || q.Contains("きのう") || q.Contains("昨夜")) days.Add(today - 1);
        if (q.Contains("今天") || q.Contains("刚才") || q.Contains("刚刚") || q.Contains("今日") || q.Contains("きょう") || q.Contains("さっき")) days.Add(today);
        days.RemoveWhere(d => d < 1);
        return days;
    }

    // 中文数字 / 全角数字 / 阿拉伯数字 → int（支持到 99）。
    public static int ParseNumber(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        StringBuilder digits = new StringBuilder();
        foreach (char c in s)
        {
            if (c >= '0' && c <= '9') digits.Append(c);
            else if (c >= '０' && c <= '９') digits.Append((char)('0' + (c - '０')));
        }
        if (digits.Length > 0) { int.TryParse(digits.ToString(), out int v); return v; }

        const string units = "零一二三四五六七八九";
        int result = 0, cur = 0;
        foreach (char c in s)
        {
            if (c == '两') { cur = 2; continue; }
            int idx = units.IndexOf(c);
            if (idx >= 0) { cur = idx; continue; }
            if (c == '十') { result += (cur == 0 ? 1 : cur) * 10; cur = 0; }
        }
        return result + cur;
    }

    // 这句话里提到的事件类型（关键词）。
    static HashSet<string> MentionedTypes(string q)
    {
        HashSet<string> set = new HashSet<string>();
        if (string.IsNullOrEmpty(q)) return set;
        foreach (KeyValuePair<string, string[]> kv in TypeWords)
            foreach (string w in kv.Value)
                if (q.Contains(w)) { set.Add(kv.Key); break; }
        return set;
    }

    static readonly Dictionary<string, string[]> TypeWords = new Dictionary<string, string[]>
    {
        { "whisper_vision", new[] { "低语", "声音", "幻听", "幻象", "幻觉", "听见", "听到", "囁き", "声", "幻聴", "幻影", "幻覚", "聞こえ" } },
        { "space_warp", new[] { "扭曲", "消失", "瞬移", "不见了", "传送", "卷走", "歪み", "消え", "飛ばされ", "ワープ" } },
        { "time_disorder", new[] { "时间", "几天", "日子", "時間", "何日" } },
        { "storm", new[] { "风暴", "暴风", "嵐" } },
        { "causal_reversal", new[] { "因果", "顺序", "先后", "逆転", "順番" } },
        { "creature", new[] { "怪物", "生物", "造物", "化け物", "怪物", "何か来" } },
        { "death", new[] { "死", "去世", "尸体", "亡くな" } },
        { "breakdown", new[] { "崩溃", "崩解", "疯了", "异变", "崩壊", "狂っ", "変異" } },
    };

    // ============ 让 LLM 补写经历和看法 ============

    static void EnqueueRefinement(EventRecord r)
    {
        if (LlmJobQueue.Ensure() == null) return;
        List<EventPerception> list = new List<EventPerception>();
        foreach (EventPerception p in r.perceptions) if (p.role == "subject") list.Add(p);
        List<EventPerception> witnesses = new List<EventPerception>();
        foreach (EventPerception p in r.perceptions) if (p.role != "subject") witnesses.Add(p);
        while (list.Count < MaxRefinePerEvent && witnesses.Count > 0)
        {
            int i = UnityEngine.Random.Range(0, witnesses.Count);
            list.Add(witnesses[i]);
            witnesses.RemoveAt(i);
        }

        int session = _session;
        foreach (EventPerception p in list)
        {
            EventPerception per = p;
            LlmJobQueue.Instance.Enqueue(new LlmJob
            {
                name = "事件看法 第" + r.day + "天 " + TypeName(r.type, LanguageGuard.IsJp) + " / " + p.npcName,
                priority = p.role == "subject" ? 6 : 4,
                maxTokens = 320,
                system = () => BuildRefinePrompt(r, per, Npc(per.npcId), LanguageGuard.IsJp),
                user = () => LanguageGuard.Pick("写出你的经历和看法。", "あなたの体験と考えを書いてください。"),
                accept = raw => session != _session || ApplyRefinement(r, per, Npc(per.npcId), raw),
                stillValid = () =>
                {
                    if (session != _session || WorldManager.Instance == null) return false;
                    NpcData n = Npc(per.npcId);
                    return n != null && !n.IsDead && !n.isBroken && WorldManager.Instance.world.day - r.day <= 2;
                },
            });
        }
    }

    static NpcData Npc(string id) => WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(id) : null;

    static string BuildRefinePrompt(EventRecord r, EventPerception p, NpcData npc, bool jp)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(NpcActors.Render(npc));
        bool me = p.role == "subject";
        if (jp)
        {
            sb.AppendLine("【たった今起きたこと】" + r.day + "日目、" + TypeName(r.type, true) + "：" + Objective(r, true));
            sb.AppendLine("【あなたの立場】" + (me ? "これはあなたの身に起きた。" : "あなたはそばで見ていた。"));
            sb.AppendLine("【あなたが実際に体験したこと】" + Perceived(r, p, npc, true));
            sb.AppendLine("【課題】この人物として、二人称「あなた」で次の2つを書く。それぞれ1〜2文：");
            sb.AppendLine("- perceived：あなたが自分で感じ取った細部（見たもの、聞いたもの、体の感覚）。" +
                          (r.type == "whisper_vision" && me ? "囁きなら、その声を具体的に描くこと――誰の声に似ているか、何を言っているようか、どんなふうに聞こえるか。ただし最後まではっきりとは聞き取れない。" : "") +
                          "上の体験と矛盾させないこと。");
            sb.AppendLine("- opinion：この出来事についてのあなたの考えと推理――何が起きたと思うか、何を意味するか、これからどうするつもりか。性格・職業・AIへの態度に合わせる。");
            sb.AppendLine("【禁止】ゲーム・プレイヤー・ロールプレイに触れない。設定を変えない。自然な日本語だけで書く。");
            sb.AppendLine("【出力形式】JSONオブジェクトを1つだけ出力すること：");
            sb.Append("{\"perceived\":\"…\",\"opinion\":\"…\"}");
        }
        else
        {
            sb.AppendLine("【刚刚发生的事】第" + r.day + "天，" + TypeName(r.type, false) + "：" + Objective(r, false));
            sb.AppendLine("【你在其中】" + (me ? "这件事发生在你身上。" : "你在一旁目睹了这件事。"));
            sb.AppendLine("【你实际经历到的】" + Perceived(r, p, npc, false));
            sb.AppendLine("【任务】以这个人的身份，用第二人称「你」补写两项，各1~2句：");
            sb.AppendLine("- perceived：你亲身感知到的细节（看到、听到、身体的感觉）。" +
                          (r.type == "whisper_vision" && me ? "如果是低语：具体描述那个声音——像谁、大概在说什么、听起来怎样，但始终听不真切。" : "") +
                          "不得与上面的经历矛盾。");
            sb.AppendLine("- opinion：你对这件事的看法和推理——你觉得这是怎么回事、意味着什么、接下来打算怎么办。要符合你的性格、职业和对AI的态度。");
            sb.AppendLine("【禁止】不得提到游戏、玩家、角色扮演；不得改动设定；只用自然的简体中文。");
            sb.AppendLine("【输出格式】只输出一个 JSON 对象：");
            sb.Append("{\"perceived\":\"…\",\"opinion\":\"…\"}");
        }
        return sb.ToString();
    }

    static bool ApplyRefinement(EventRecord r, EventPerception p, NpcData npc, string raw)
    {
        if (npc == null) return true;
        JObject obj = null;
        try
        {
            int s = raw.IndexOf('{'), e = raw.LastIndexOf('}');
            if (s >= 0 && e > s) obj = JObject.Parse(raw.Substring(s, e - s + 1));
        }
        catch { obj = null; }
        if (obj == null) return false;

        bool jp = LanguageGuard.IsJp;
        JToken pt = obj["perceived"], ot = obj["opinion"];
        string perceived = NpcActors.CleanField(pt != null && pt.Type == JTokenType.String ? (string)pt : null, npc, jp, 160);
        string opinion = NpcActors.CleanField(ot != null && ot.Type == JTokenType.String ? (string)ot : null, npc, jp, 160);
        if (opinion == null) return false; // 看法是这项任务的重点，必须合格

        p.llmLang = jp ? "ja" : "zh";
        if (perceived != null) p.llmPerceived = perceived;
        p.llmOpinion = opinion;
        WriteFiles();
        return true;
    }

    // ============ 文件 ============

    public static void WriteFiles()
    {
        bool jp = LanguageGuard.IsJp;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(jp ? "# この回の出来事の記録（各NPCの一人称視点の体験と考え）" : "# 本局事件记录（每个 NPC 的第一视角：经历和看法）");
        sb.AppendLine(jp ? "# NPCと話すとき、関係する記録がプロンプトの【あなたが経験した出来事】に入る。" : "# 和 NPC 对话时，相关的记录会放进提示词的【你经历过的事】。");
        sb.AppendLine();
        foreach (EventRecord r in _records)
        {
            sb.AppendLine((jp ? "==== " + r.day + "日目 · " : "==== 第" + r.day + "天 · ") + TypeName(r.type, jp) + "（#" + r.id + "）====");
            sb.AppendLine((jp ? "何が起きたか：" : "发生了什么：") + Objective(r, jp));
            if (!string.IsNullOrEmpty(r.subjectName)) sb.AppendLine((jp ? "当事者：" : "当事人：") + r.subjectName);
            foreach (EventPerception p in r.perceptions)
            {
                NpcData npc = Npc(p.npcId);
                string role = p.role == "subject" ? (jp ? "当事者" : "当事人") : (jp ? "目撃" : "目击");
                bool llm = !string.IsNullOrEmpty(p.llmOpinion) && p.llmLang == (jp ? "ja" : "zh");
                sb.AppendLine("· " + p.npcName + "（" + role + "）" + (llm ? "［LLM］" : (jp ? "［テンプレート］" : "［代码模板］")));
                sb.AppendLine("    " + (jp ? "体験：" : "经历：") + Perceived(r, p, npc, jp));
                sb.AppendLine("    " + (jp ? "考え：" : "看法：") + Opinion(r, p, npc, jp));
            }
            sb.AppendLine();
        }
        NpcMemory.WriteText("events.txt", sb.ToString());
        NpcMemory.WriteJson("events.json", _records);
    }
}
