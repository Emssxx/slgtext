using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// NPC 之间的闲聊：数据结构 + 台词来源接口 + 规则。
// 遵循"LLM配音、代码做决定"：
//   话题选什么、聊完关系/理智怎么变、记忆和日志怎么写 → IdleChatRules（代码）。
//   具体说哪几句话 → IdleChat.Voice（默认本地台词 LocalIdleChatVoice；以后可以换成 LLM 配音）。
// ============================================================

// 闲聊话题（由规则 IdleChatRules.PickTopic 决定）。
public enum IdleChatTopic { SmallTalk, Past, Food, Tired, Fear, Camp, Terminal, Friendly, Tense, Hallucination }

// 一句台词：谁说的（发起方 / 对方）+ 内容。
public class IdleChatLine
{
    public bool byInitiator;
    public string text;

    public IdleChatLine(bool byInitiator, string text)
    {
        this.byInitiator = byInitiator;
        this.text = text;
    }
}

// 一场闲聊：两个参与者 + 话题 + 状态。任何一方退出（被玩家指令叫走、被打断、死亡）整场都会散掉。
public class IdleChatSession
{
    public readonly NpcIdleBrain initiator;   // 发起方（走过去搭话的人）
    public readonly NpcIdleBrain partner;     // 被搭话的一方
    public readonly WorldState world;
    public IdleChatTopic topic = IdleChatTopic.SmallTalk;
    public List<IdleChatLine> lines;          // 实际说出的台词
    public bool llmLines;                     // 台词是 AI 现场写的（不是本地剧本）：聊完让小模型判断实际语气

    public bool Ended { get; private set; }   // 已结束（正常聊完或中途散场）
    public bool Aborted { get; private set; } // 是否中途散场

    public IdleChatSession(NpcIdleBrain initiator, NpcIdleBrain partner, WorldState world)
    {
        this.initiator = initiator;
        this.partner = partner;
        this.world = world;
    }

    public NpcData A => initiator != null ? initiator.Data : null; // 发起方数据
    public NpcData B => partner != null ? partner.Data : null;     // 对方数据

    // 正常聊完。
    public void Complete()
    {
        Ended = true;
    }

    // 中途散场：收起两人的气泡，双方的活动协程看到 Ended 会自己退出。
    public void Abort()
    {
        if (Ended) return;
        Ended = true;
        Aborted = true;
        if (initiator != null) initiator.HideBubble();
        if (partner != null) partner.HideBubble();
    }
}

// 台词来源接口。现有两种实现：
//   LocalIdleChatVoice —— 本地剧本台词（同步，永远可用，作兜底）
//   LlmIdleChatVoice   —— 本地 LLM 现场写台词（异步；忙/失败/超时时 done(null)，自动退回本地台词）
// NpcIdleManager 启动时设置 IdleChat.Voice（默认 LLM 配音）。
public interface IIdleChatVoice
{
    // 生成一场闲聊的台词。完成时调用 done(lines)；放弃时 done(null)。
    void Compose(IdleChatSession session, Action<List<IdleChatLine>> done);

    // 闲聊散场 / 等太久时调用：取消还没完成的生成（本地台词什么都不用做）。
    void Cancel(IdleChatSession session);
}

public static class IdleChat
{
    // 当前使用的台词来源（可在运行时替换）。
    public static IIdleChatVoice Voice = new LocalIdleChatVoice();

    // 从"开始要台词"算起最多等多久（秒）。要台词和走过去是同时进行的，到了还没好就再等一会儿，
    // 超时用本地台词兜底（两人已经面对面站着了，不能让他们干等）。
    public static float VoiceTimeout = 7f;

    // 一句话在气泡里停留多久：按字数算，2~5 秒。
    public static float LineDuration(string text)
    {
        int len = string.IsNullOrEmpty(text) ? 0 : text.Length;
        return Mathf.Clamp(1.4f + len * 0.09f, 2f, 5f);
    }
}

// 闲聊规则（纯代码）：话题怎么选、聊完关系/理智怎么变、记忆和日志怎么写。数值都集中在这里，方便调。
public static class IdleChatRules
{
    const int MaxMemories = 60; // 每个 NPC 最多留多少条记忆（防止无限增长；AI 上下文只用最近几条）

    // —— 选话题：按双方状态和关系给各话题加权，再加权随机 ——
    public static IdleChatTopic PickTopic(NpcData a, NpcData b, WorldState w)
    {
        List<KeyValuePair<IdleChatTopic, float>> c = new List<KeyValuePair<IdleChatTopic, float>>();
        float affAB = Affinity(a, b);
        float affBA = Affinity(b, a);
        float minAff = Mathf.Min(affAB, affBA);
        float anomaly = w != null ? w.anomalyLevel : 0f;

        if (a.isHallucinating) Add(c, IdleChatTopic.Hallucination, 4f);                       // 发起方在幻觉中：说看见的怪东西
        if (minAff <= -20f) Add(c, IdleChatTopic.Tense, 3f + (-20f - minAff) / 20f);          // 关系差：容易吵起来
        if (a.hunger >= 60f || b.hunger >= 60f || FoodLow(w)) Add(c, IdleChatTopic.Food, 2.5f); // 饿 / 仓库见底
        if (a.energy <= 35f || b.energy <= 35f) Add(c, IdleChatTopic.Tired, 2f);              // 累
        if (anomaly >= 40f || a.sanity < 60f || b.sanity < 60f)
            Add(c, IdleChatTopic.Fear, 1.5f + anomaly / 50f);                                  // 异常度高 / 心神不宁
        if ((affAB + affBA) * 0.5f >= 25f) Add(c, IdleChatTopic.Friendly, 2f);                 // 关系好：说心里话
        bool terminalUp = w != null && w.terminalLevel >= 1;
        Add(c, terminalUp ? IdleChatTopic.Terminal : IdleChatTopic.Camp, 1.2f);               // 终端机建好前后话题不同
        if (!string.IsNullOrEmpty(a.profession) && !string.IsNullOrEmpty(b.profession))
            Add(c, IdleChatTopic.Past, 1.3f);                                                  // 聊以前的职业
        Add(c, IdleChatTopic.SmallTalk, 1.6f);                                                 // 家常（保底）

        float total = 0f;
        foreach (KeyValuePair<IdleChatTopic, float> kv in c) total += kv.Value;
        float r = UnityEngine.Random.value * total;
        foreach (KeyValuePair<IdleChatTopic, float> kv in c)
        {
            r -= kv.Value;
            if (r <= 0f) return kv.Key;
        }
        return IdleChatTopic.SmallTalk;
    }

    static string RelLF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;

    // —— 聊完结算：关系、理智、记忆、一条行为日志 ——
    public static void ApplyOutcome(IdleChatSession s)
    {
        NpcData a = s.A, b = s.B;
        if (a == null || b == null) return;

        // 各话题的效果：A→B 友善、B→A 友善、双向信任、A 理智、B 理智
        float affA = 3f, affB = 3f, trust = 1f, sanA = 1.5f, sanB = 1.5f; // 默认（家常）：有人说说话，心里踏实些
        switch (s.topic)
        {
            case IdleChatTopic.Past:
                affA = 4f; affB = 4f;
                break;
            case IdleChatTopic.Food:
            case IdleChatTopic.Tired:
            case IdleChatTopic.Camp:
            case IdleChatTopic.Terminal:
                affA = 2f; affB = 2f; sanA = 1f; sanB = 1f;
                break;
            case IdleChatTopic.Fear:
                affA = 2f; affB = 2f; sanA = 0f; sanB = 0f;        // 说出来好受些，但也更不安：理智不变
                break;
            case IdleChatTopic.Friendly:
                affA = 5f; affB = 5f; trust = 2f; sanA = 2f; sanB = 2f;
                break;
            case IdleChatTopic.Tense:
                affA = -4f; affB = -4f; trust = -2f; sanA = -1f; sanB = -1f;
                break;
            case IdleChatTopic.Hallucination:
                affA = 3f; affB = 1f; sanA = 3f; sanB = -1f;       // A 被安抚，B 被吓到一点
                break;
        }
        if (a.personality.sociability >= 65) affA += 1f;          // 外向的人聊完更有好感
        if (b.personality.sociability >= 65) affB += 1f;

        // 关系结算：
        //   本地剧本台词 → 台词本来就是按话题写的，直接按话题结算；
        //   AI 现场写的台词 → 内容可能和话题不一样（家常聊着聊着吵起来了），让小模型读一遍实际说的话判断语气，按语气结算。
        //   小模型没装 / 太忙 / 判断不出来 → 还是按话题结算。
        // 只有印象深的（投机 / 吵架 / 聊得僵 / 聊过去）才记一句原因。
        int rday = s.world != null ? s.world.day : 0;
        string reasonKey = s.topic == IdleChatTopic.Tense ? "rel_reason_quarrel"
                         : s.topic == IdleChatTopic.Friendly ? "rel_reason_chat_good"
                         : s.topic == IdleChatTopic.Past ? "rel_reason_chat_past" : null;
        float fAffA = affA, fAffB = affB, fTrust = trust;
        Action byTopic = () =>
        {
            RelationshipManager.Apply(a, b, fAffA, fTrust, 0f, reasonKey != null ? RelLF(reasonKey, rday, b.npcName) : null);
            RelationshipManager.Apply(b, a, fAffB, fTrust, 0f, reasonKey != null ? RelLF(reasonKey, rday, a.npcName) : null);
        };
        if (!(s.llmLines && JudgeTone(s, a, b, rday, byTopic))) byTopic();
        ChangeSanity(a, sanA);
        ChangeSanity(b, sanB);

        // 记忆按界面语言写（以后喂给 LLM 当上下文），行为日志只写一条概要。
        int day = s.world != null ? s.world.day : 0;
        string log;
        if (s.topic == IdleChatTopic.Tense)
        {
            AddMemory(a, day, Loc("idle_mem_chat_tense", b.npcName));
            AddMemory(b, day, Loc("idle_mem_chat_tense", a.npcName));
            log = Loc("idle_log_chat_tense", a.npcName, b.npcName);
        }
        else
        {
            string topicName = Loc("idle_topic_" + s.topic.ToString().ToLowerInvariant());
            AddMemory(a, day, Loc("idle_mem_chat", b.npcName, topicName));
            AddMemory(b, day, Loc("idle_mem_chat", a.npcName, topicName));
            log = Loc("idle_log_chat", a.npcName, b.npcName, topicName);
        }
        if (LogManager.Instance != null) LogManager.Instance.AddLog(a.npcName, log, a.id);

        // 实际说过的台词记进双方的档案（以后和玩家聊天时能想起来）。
        NpcMemory.AddIdleChat(s);
    }

    // ============ 闲聊语气判断（小模型）============

    const string ToneSystemPrompt =
        "你是对话语气判断器。读下面 A 和 B 的一段对话（可能是中文或日文），分别判断 A 对 B、B 对 A 的态度，只能从这四个里选：\n" +
        "friendly = 友好、亲切、关心、开玩笑\n" +
        "neutral = 普通、平淡、就事论事\n" +
        "cold = 冷淡、敷衍、不耐烦、阴阳怪气\n" +
        "hostile = 敌意、指责、争吵、嘲讽、威胁\n" +
        "只输出一个 JSON 对象，不要任何多余文字：{\"a\":\"A 对 B 的态度\",\"b\":\"B 对 A 的态度\"}";

    // 把实际说的话交给小模型判断语气。发出去返回 true（结果回来再结算）；没发出去返回 false（调用方按话题结算）。
    static bool JudgeTone(IdleChatSession s, NpcData a, NpcData b, int day, Action fallback)
    {
        if (s.lines == null || s.lines.Count == 0) return false;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (IdleChatLine line in s.lines)
            if (line != null && !string.IsNullOrEmpty(line.text)) sb.Append(line.byInitiator ? "A：" : "B：").AppendLine(line.text);
        string dialogue = sb.ToString();
        IdleChatTopic topic = s.topic;
        return AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.ChatTone, key = a.id + "|" + b.id, json = true, maxTokens = 40,
            system = () => ToneSystemPrompt,
            user = () => dialogue,
            onResult = raw =>
            {
                if (!TryParseTone(raw, out int ta, out int tb)) { fallback(); return; }
                SettleByTone(a, b, ta, tb, topic, day);
            },
        });
    }

    // 语气 → 分数：friendly 2 / neutral 1 / cold -1 / hostile -2。两个都认出来才算成功。
    static bool TryParseTone(string raw, out int ta, out int tb)
    {
        ta = tb = 0;
        if (string.IsNullOrEmpty(raw)) return false;
        try
        {
            int i = raw.IndexOf('{'), j = raw.LastIndexOf('}');
            if (i < 0 || j <= i) return false;
            Newtonsoft.Json.Linq.JObject o = Newtonsoft.Json.Linq.JObject.Parse(raw.Substring(i, j - i + 1));
            ta = ToneScore(o.Value<string>("a"));
            tb = ToneScore(o.Value<string>("b"));
            return ta != 0 && tb != 0;
        }
        catch { return false; }
    }

    static int ToneScore(string t)
    {
        switch ((t ?? "").Trim().ToLowerInvariant())
        {
            case "friendly": return 2;
            case "neutral": return 1;
            case "cold": return -1;
            case "hostile": return -2;
            default: return 0;
        }
    }

    // 好感变化看"对方怎么对我"：对方友好 +5、普通 +2、冷淡 -2、敌意 -6（外向的人 +1）。
    // 信任：两边都友好 +2；有人敌意 -2；有人冷淡 0；其余 +1。
    static void SettleByTone(NpcData a, NpcData b, int ta, int tb, IdleChatTopic topic, int day)
    {
        if (a == null || b == null) return;
        float affAB = ToneAffinity(tb) + (a.personality.sociability >= 65 ? 1f : 0f);
        float affBA = ToneAffinity(ta) + (b.personality.sociability >= 65 ? 1f : 0f);
        float trust = (ta == 2 && tb == 2) ? 2f : (ta == -2 || tb == -2) ? -2f : (ta < 0 || tb < 0) ? 0f : 1f;
        string key = (ta == -2 || tb == -2) ? "rel_reason_quarrel"
                   : (ta == 2 && tb == 2) ? "rel_reason_chat_good"
                   : (ta < 0 || tb < 0) ? "rel_reason_chat_cold"
                   : topic == IdleChatTopic.Past ? "rel_reason_chat_past" : null;
        RelationshipManager.Apply(a, b, affAB, trust, 0f, key != null ? RelLF(key, day, b.npcName) : null);
        RelationshipManager.Apply(b, a, affBA, trust, 0f, key != null ? RelLF(key, day, a.npcName) : null);
        Debug.Log($"[闲聊语气] {a.npcName} → {b.npcName}：{ta}，{b.npcName} → {a.npcName}：{tb}（话题 {topic}）");
    }

    static float ToneAffinity(int tone)
    {
        switch (tone)
        {
            case 2: return 5f;
            case 1: return 2f;
            case -1: return -2f;
            default: return -6f;
        }
    }

    // 聊完后多久才会再聊（外向的人短，内向的人长）。
    public static float RollCooldown(NpcData npc)
    {
        NpcIdleManager m = NpcIdleManager.Instance;
        Vector2 range = m != null ? m.chatCooldown : new Vector2(25f, 45f);
        float social = npc != null ? npc.personality.sociability / 100f : 0.5f;
        return UnityEngine.Random.Range(range.x, range.y) * (1.4f - social * 0.8f);
    }

    // 加一条记忆（超过上限时删掉最旧的）。
    public static void AddMemory(NpcData npc, int day, string content)
    {
        if (npc == null || string.IsNullOrEmpty(content)) return;
        if (npc.memories == null) npc.memories = new List<MemoryEntry>();
        npc.memories.Add(new MemoryEntry { day = day, content = content });
        if (npc.memories.Count > MaxMemories) npc.memories.RemoveRange(0, npc.memories.Count - MaxMemories);
    }

    static void ChangeSanity(NpcData npc, float delta)
    {
        if (npc == null || npc.isBroken || delta == 0f) return;
        npc.sanity = Mathf.Clamp(npc.sanity + delta, 0f, 100f);
        npc.isHallucinating = npc.sanity < WorldRules.HallucinationThreshold;
    }

    static float Affinity(NpcData from, NpcData to)
    {
        return RelationshipManager.AffinityOf(from, to.id); // 饥饿等状态会让好感暂时变低
    }

    static bool FoodLow(WorldState w)
    {
        return w != null && ItemDatabase.Instance != null
               && ItemDatabase.Instance.CountInStorage(ItemCategory.Food, w) < 3;
    }

    static void Add(List<KeyValuePair<IdleChatTopic, float>> list, IdleChatTopic topic, float weight)
    {
        list.Add(new KeyValuePair<IdleChatTopic, float>(topic, weight));
    }

    static string Loc(string key, params object[] args)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return loc != null ? loc.GetFormat(key, args) : key;
    }
}
