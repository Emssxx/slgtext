using UnityEngine;
using System;
using Newtonsoft.Json;

// NPC 代理（新架构版）：只负责"表达层"——把玩家的话变成 NPC 的对话/行动反应。
// 【职责边界】
//   ✅ 负责：调 LLM 生成对话、解析玩家指令成任务、信任度听从判定。
//   ❌ 不负责：NPC 行为决策（那是 NpcDecisionRules 状态机的事，纯规则，不走 LLM）。
// 玩家说话时的处理流程：
//   崩解拦截 → AI 解析指令是否含任务 → 有任务则按信任度判定听不听 → 听则交任务队列；
//   无任务（纯聊天）则正常生成对话。
public class NpcAgent : MonoBehaviour
{
    [Header("引用与设置")]
    public ChatManager chatManager;  // 聊天框（Inspector 拖入）
    public string npcId = "npc_01";  // 当前对话目标 NPC 的 id

    void Start()
    {
        if (chatManager != null)
            chatManager.OnPlayerMessage += HandlePlayerMessage; // 订阅玩家发消息
        else
            Debug.LogError("NpcAgent 没有设置 chatManager 引用！");
    }

    void OnDestroy()
    {
        if (chatManager != null)
            chatManager.OnPlayerMessage -= HandlePlayerMessage;
    }

    // 切换当前对话的 NPC（UI 点卡片/点场景NPC时调用）。
    public void SwitchTarget(string newNpcId)
    {
        NpcData target = WorldManager.Instance.GetNpcById(newNpcId);

        // 死亡拦截：不能切换到已死亡的 NPC 对话。
        if (target != null && target.IsDead)
        {
            // 说话人和提示语都走翻译表（GetFormat 把 NPC 名字填进 {0}）
            LocalizationManager loc = LocalizationManager.Instance;
            chatManager.AddMessage(
                loc != null ? loc.Get("system_speaker") : "系统",
                loc != null ? loc.GetFormat("npc_dead_cannot_talk", target.npcName) : $"{target.npcName} 已经死亡，无法与其对话。",
                Color.gray);
            return;
        }

        npcId = newNpcId;
        string name = target != null ? target.npcName : newNpcId;
        chatManager.AddMessage(
            LocalizationManager.Instance != null ? LocalizationManager.Instance.Get("system_speaker") : "系统",
            // 提示语走翻译表，按界面语言显示（GetFormat 把 NPC 名字填进 {0}）
            LocalizationManager.Instance != null
                ? LocalizationManager.Instance.GetFormat("now_talking_with", name)
                : "你现在正在和【" + name + "】对话。",
            Color.gray);
    }

    // ============ 玩家说话入口 ============
    void HandlePlayerMessage(string playerText)
    {
        NpcData npc = WorldManager.Instance.GetNpcById(npcId);
        if (npc == null) { Debug.LogError("找不到 NPC，id = " + npcId); return; }

        // 死亡拦截：死了的 NPC 不回应（优先级比崩解拦截高）。
        if (npc.IsDead)
        {
            var loc0 = LocalizationManager.Instance;
            chatManager.AddMessage(loc0 != null ? loc0.Get("system_speaker") : "系统",
                loc0 != null ? loc0.GetFormat("npc_dead_cannot_talk", npc.npcName) : $"{npc.npcName} 已经死亡，无法回应。", Color.gray);
            return;
        }

        // 崩解拦截：崩解的 NPC 不调 LLM，直接说代码生成的疯话。
        if (npc.isBroken)
        {
            string crazy = BrokenSpeech.Generate(npc);
            chatManager.AddMessage(npc.npcName, Prefix(npc) + crazy, chatManager.npcColor);
            return;
        }

        // 先用 AI 把玩家的话解析成任务（看是不是行动指令）。
        string parsePrompt = TaskPlanner.BuildParsePrompt(WorldManager.Instance.world);
        AIClientHub.Instance.Send(
            parsePrompt, playerText,
            (rawJson) => OnTaskPlanReply(rawJson, npc, playerText),
            true);
    }

    // AI 解析指令的结果回来。
    void OnTaskPlanReply(string rawJson, NpcData npc, string playerText)
    {
        TaskPlan plan = TaskPlanner.ParsePlan(rawJson);

        // 解析出了任务 → 这是行动指令，要走"信任度判定 + 任务执行"。
        if (plan != null && plan.tasks != null && plan.tasks.Count > 0)
        {
            // 信任度判定：这个 NPC 这次听不听玩家的指令。
            if (!TrustRules.WillObey(npc))
            {
                chatManager.AddMessage(npc.npcName, Prefix(npc) + GetRefuseLine(npc), chatManager.npcColor);
                return;
            }

            // 听 → 交给任务队列执行。
            NpcTaskRunner runner = NpcView.GetMover(npc.id)?.GetComponent<NpcTaskRunner>();
            if (runner != null)
            {
                runner.RunTasks(plan.tasks);
                string startAction = LocalizationManager.Instance != null ? LocalizationManager.Instance.Get("start_action") : "开始行动";
                string say = string.IsNullOrEmpty(plan.reply) ? "（" + startAction + "）" : plan.reply;
                chatManager.AddMessage(npc.npcName, Prefix(npc) + say, chatManager.npcColor);
                return;
            }
        }

        // 没有任务（纯聊天）→ 正常生成对话。
        string systemPrompt = BuildSystemPrompt(npc);
        AIClientHub.Instance.Send(systemPrompt, playerText,
            (reply) => OnNpcReply(reply, npc.id), true);
    }

    // ============ 让某个 NPC 主动开口（危急时、导演点名时调用）============
    public void MakeNpcSpeak(NpcData npc, string reason)
    {
        if (npc == null) return;

        // 死亡拦截：死了的 NPC 不主动说话（优先级比崩解拦截高）。
        if (npc.IsDead) return;

        // 崩解拦截
        if (npc.isBroken)
        {
            string crazy = BrokenSpeech.Generate(npc);
            chatManager.AddMessage(npc.npcName, Prefix(npc) + crazy, chatManager.npcColor);
            return;
        }

        string systemPrompt = BuildSystemPrompt(npc);
        string userPrompt = LanguageGuard.Pick(
            "（这一刻没有玩家对你说话。你出于以下原因，主动开口说点什么：" + reason + "。" +
            "请用你的口吻说一句自言自语或对同伴的话。）",
            "（今、誰もあなたに話しかけていない。次の理由で、あなたは自分から何かを口にする：" + reason + "。" +
            "あなたの口調で、独り言か仲間への一言を言うこと。）");

        AIClientHub.Instance.Send(systemPrompt, userPrompt,
            (reply) => OnNpcReply(reply, npc.id), true);
    }

    // ============ 构建对话提示词 ============
    // 注意：新架构里 LLM 只生成对话，不再决定 action/reason。
    string BuildSystemPrompt(NpcData npc)
    {
        WorldState world = WorldManager.Instance.world;
        bool jp = LanguageGuard.IsJp;

        // 把结构化性格翻译成文字（喂 AI）。
        Personality p = npc.personality;
        string personalityDesc = jp
            ? $"勇敢{p.bravery}／利他{p.altruism}／慎重{p.caution}／社交{p.sociability}／楽観{p.optimism}（0〜100、高いほど強い）"
            : $"勇敢{p.bravery}/利他{p.altruism}/谨慎{p.caution}/外向{p.sociability}/乐观{p.optimism}（0~100，越高越强）";
        string traitDesc = npc.traits != null ? string.Join("、", npc.traits) : "";
        string gender = jp ? (npc.gender == "男" ? "男性" : "女性") : npc.gender;

        // 精神状态注入（清醒时为空串，已按当前语言）。
        string mental = MentalState.GetInjection(npc, world);

        // 世界概况：当前语言的简短文字（代替原来整份世界 JSON，防止中文混入和上下文溢出）。
        string context = PromptContext.WorldSummary(world, npc);

        string langRule = LocalizationManager.Instance != null ? LocalizationManager.Instance.GetAILanguageInstruction() : "";

        // 拼接。世界观 + 人物卡 + 概况 + 精神注入 + 输出格式 + 语言指令（语言指令放最后）。
        // 整段提示词都用当前界面语言写——这是让小模型不说错语言的根本办法。
        if (jp)
        {
            return
                WorldLore.Background + "\n\n" +
                WorldLore.WorldLaws + "\n\n" +
                WorldLore.PlayerIdentity + "\n\n" +
                WorldLore.SpeakingRules + "\n\n" +
                WorldLore.NpcMood + "\n\n" +
                "あなたは以下の人物そのものであり、本当に亜空間の中にいる。この人物として考え、話すこと。\n" +
                "【人物カード】\n" +
                "- 名前：" + npc.npcName + "（" + gender + "）\n" +
                "- 職業：" + npc.profession + "\n" +
                "- 性格：" + traitDesc + "\n" +
                "- 性格の数値：" + personalityDesc + "\n" +
                "- 価値観：" + npc.values + "\n" +
                "- 話し方：" + npc.speakingStyle + "（台詞は必ずこの話し方に合わせること）\n" +
                "- 経歴：" + npc.background + "\n" +
                "- 今の目標：" + npc.currentGoal + "\n\n" +
                context + "\n" +
                mental + "\n\n" +
                "【出力形式】JSONオブジェクトを1つだけ出力し、それ以外は何も書かないこと：\n" +
                "{\n" +
                "  \"dialogue\": \"あなたの話し方で、実際に口に出す台詞\"\n" +
                "}" +
                "\n\n" + langRule;
        }

        return
            WorldLore.Background + "\n\n" +
            WorldLore.WorldLaws + "\n\n" +
            WorldLore.PlayerIdentity + "\n\n" +
            WorldLore.SpeakingRules + "\n\n" +
            WorldLore.NpcMood + "\n\n" +
            "你就是下面这个人，你真实地身处亚空间中。请完全以这个人的身份思考和说话。\n" +
            "【人物卡】\n" +
            "- 名字：" + npc.npcName + "（" + gender + "）\n" +
            "- 职业：" + npc.profession + "\n" +
            "- 性格词条：" + traitDesc + "\n" +
            "- 性格维度：" + personalityDesc + "\n" +
            "- 价值观：" + npc.values + "\n" +
            "- 说话风格：" + npc.speakingStyle + "（你的对话必须符合这个风格）\n" +
            "- 背景：" + npc.background + "\n" +
            "- 当前目标：" + npc.currentGoal + "\n\n" +
            context + "\n" +
            mental + "\n\n" +
            "【输出要求】只输出一个 JSON 对象，不要任何多余文字：\n" +
            "{\n" +
            "  \"dialogue\": \"符合你说话风格的话\"\n" +
            "}" +
            "\n\n" + langRule;
    }

    // ============ AI 对话回复处理 ============
    void OnNpcReply(string rawJson, string speakerId)
    {
        string dialogue = ParseDialogue(rawJson);

        NpcData npc = WorldManager.Instance.GetNpcById(speakerId);
        string speaker = npc != null ? npc.npcName : "NPC";

        // 精神状态前缀（失神/幻觉的提示）。
        string prefix = npc != null ? Prefix(npc) : "";

        // 清洗对话（去自报名字、出戏称呼等）。
        string cleaned = DialogueCleaner.Clean(dialogue, npc != null ? npc.npcName : "");

        chatManager.AddMessage(speaker, prefix + cleaned, chatManager.npcColor);
    }

    // 按精神状态返回对话前缀（用翻译表，跟随语言）。
    string Prefix(NpcData npc)
    {
        var loc = LocalizationManager.Instance;
        switch (MentalState.GetLevel(npc))
        {
            case SanityLevel.Dazed:
                return loc != null ? loc.Get("state_dazed") : "（眼神涣散）";
            case SanityLevel.Hallucinating:
                return loc != null ? loc.Get("state_hallucinate") : "（声音发颤）";
            case SanityLevel.Broken:
                return loc != null ? loc.Get("state_broken") : "（已不成人形）";
            default:
                return "";
        }
    }

    // 拒绝指令时说的话（不调 AI，按信任度高低给不同语气）。
    string GetRefuseLine(NpcData npc)
    {
        float t = npc.playerTrust;
        string[] pool;
        bool jp = LanguageGuard.IsJp; // 拒绝台词是写死的，也要跟随界面语言
        if (t < 30f)
            pool = jp ? new[] { "なんでお前の言うことを聞かなきゃならない。", "お前は信用できない。", "……嫌だ。", "何様のつもりだ、俺に命令するのか？" }
                      : new[] { "凭什么听你的。", "我不信任你。", "……不。", "你算什么，能命令我？" };
        else if (t < 60f)
            pool = jp ? new[] { "今はそうしたくない。", "……少し考えさせて。今はやめておく。", "それは無理だ。", "どうしてお前に従わなきゃいけない？" }
                      : new[] { "我现在不想这么做。", "……让我想想，先不去。", "这个我做不到。", "为什么要听你的？" };
        else
            pool = jp ? new[] { "今はちょっと……", "待って、少し迷ってる。", "今回はやめておくよ、ごめん。" }
                      : new[] { "现在不太方便……", "等等，我有点犹豫。", "这次我先不去了，抱歉。" };
        return pool[UnityEngine.Random.Range(0, pool.Length)];
    }

    // 解析 AI 返回的 JSON，取出 dialogue（失败则把原文当对话）。
    string ParseDialogue(string raw)
    {
        try
        {
            int start = raw.IndexOf('{');
            int end = raw.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                string jsonPart = raw.Substring(start, end - start + 1);
                NpcReplyData d = JsonConvert.DeserializeObject<NpcReplyData>(jsonPart);
                if (d != null && !string.IsNullOrEmpty(d.dialogue))
                    return d.dialogue;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("解析 NPC 对话 JSON 失败，按纯文本处理。错误：" + e.Message);
        }
        return raw; // 降级：直接用原文
    }
}

// AI 对话输出契约（新架构：只有 dialogue，不再有 action/reason）。
[Serializable]
public class NpcReplyData
{
    public string dialogue;
}
