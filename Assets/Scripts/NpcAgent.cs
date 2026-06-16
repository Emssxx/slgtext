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
        npcId = newNpcId;
        NpcData npc = WorldManager.Instance.GetNpcById(newNpcId);
        string name = npc != null ? npc.npcName : newNpcId;
        chatManager.AddMessage(
            LocalizationManager.Instance != null ? LocalizationManager.Instance.Get("system_speaker") : "系统",
            "你现在正在和【" + name + "】对话。", Color.gray);
    }

    // ============ 玩家说话入口 ============
    void HandlePlayerMessage(string playerText)
    {
        NpcData npc = WorldManager.Instance.GetNpcById(npcId);
        if (npc == null) { Debug.LogError("找不到 NPC，id = " + npcId); return; }

        // 崩解拦截：崩解的 NPC 不调 LLM，直接说代码生成的疯话。
        if (npc.isBroken)
        {
            string crazy = BrokenSpeech.Generate(npc);
            chatManager.AddMessage(npc.npcName, Prefix(npc) + crazy, chatManager.npcColor);
            return;
        }

        // 先用 AI 把玩家的话解析成任务（看是不是行动指令）。
        string parsePrompt = TaskPlanner.BuildParsePrompt(WorldManager.Instance.world);
        OllamaAIClient.Instance.SendChatRequest(
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
                string say = string.IsNullOrEmpty(plan.reply) ? "（开始行动）" : plan.reply;
                chatManager.AddMessage(npc.npcName, Prefix(npc) + say, chatManager.npcColor);
                return;
            }
        }

        // 没有任务（纯聊天）→ 正常生成对话。
        string systemPrompt = BuildSystemPrompt(npc);
        OllamaAIClient.Instance.SendChatRequest(systemPrompt, playerText,
            (reply) => OnNpcReply(reply, npc.id), true);
    }

    // ============ 让某个 NPC 主动开口（危急时、导演点名时调用）============
    public void MakeNpcSpeak(NpcData npc, string reason)
    {
        if (npc == null) return;

        // 崩解拦截
        if (npc.isBroken)
        {
            string crazy = BrokenSpeech.Generate(npc);
            chatManager.AddMessage(npc.npcName, Prefix(npc) + crazy, chatManager.npcColor);
            return;
        }

        string systemPrompt = BuildSystemPrompt(npc);
        string userPrompt =
            "（这一刻没有玩家对你说话。你出于以下原因，主动开口说点什么：" + reason + "。" +
            "请用你的口吻说一句自言自语或对同伴的话。）";

        OllamaAIClient.Instance.SendChatRequest(systemPrompt, userPrompt,
            (reply) => OnNpcReply(reply, npc.id), true);
    }

    // ============ 构建对话提示词 ============
    // 注意：新架构里 LLM 只生成对话，不再决定 action/reason。
    string BuildSystemPrompt(NpcData npc)
    {
        WorldState world = WorldManager.Instance.world;

        // 把结构化性格翻译成文字（喂 AI）。
        Personality p = npc.personality;
        string personalityDesc =
            $"勇敢{p.bravery}/利他{p.altruism}/谨慎{p.caution}/外向{p.sociability}/乐观{p.optimism}（0~100，越高越强）";
        string traitDesc = npc.traits != null ? string.Join("、", npc.traits) : "";

        // 精神状态注入（清醒时为空串）。
        string mental = MentalState.GetInjection(npc, world);

        // 世界快照。
        string worldJson = WorldManager.Instance.ExportWorldAsJson();

        // 拼接。世界观 + 人物卡 + 状态 + 精神注入 + 输出格式 + 语言指令（语言指令放最后）。
        return
            WorldLore.Background + "\n\n" +
            WorldLore.WorldLaws + "\n\n" +
            WorldLore.PlayerIdentity + "\n\n" +
            WorldLore.SpeakingRules + "\n\n" +
            WorldLore.NpcMood + "\n\n" +
            "你就是下面这个人，你真实地身处亚空间中。请完全以这个人的身份思考和说话。\n" +
            "【人物卡】\n" +
            "- 名字：" + npc.npcName + "（" + npc.gender + "）\n" +
            "- 职业：" + npc.profession + "\n" +
            "- 性格词条：" + traitDesc + "\n" +
            "- 性格维度：" + personalityDesc + "\n" +
            "- 价值观：" + npc.values + "\n" +
            "- 说话风格：" + npc.speakingStyle + "（你的对话必须符合这个风格）\n" +
            "- 背景：" + npc.background + "\n" +
            "- 当前目标：" + npc.currentGoal + "\n\n" +
            "【你的当前状态】\n" +
            $"- 生命{npc.health:0} 饥饿{npc.hunger:0} 体力{npc.energy:0} 理智{npc.sanity:0}，位置：{npc.location}，正在：{npc.currentAction}\n\n" +
            "【世界状态 JSON】\n" + worldJson + "\n\n" +
            mental + "\n\n" +
            "【输出要求】只输出一个 JSON 对象，不要任何多余文字：\n" +
            "{\n" +
            "  \"dialogue\": \"符合你说话风格的话\"\n" +
            "}" +
            "\n\n" + (LocalizationManager.Instance != null ? LocalizationManager.Instance.GetAILanguageInstruction() : "");
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
        if (t < 30f)
            pool = new[] { "凭什么听你的。", "我不信任你。", "……不。", "你算什么，能命令我？" };
        else if (t < 60f)
            pool = new[] { "我现在不想这么做。", "……让我想想，先不去。", "这个我做不到。", "为什么要听你的？" };
        else
            pool = new[] { "现在不太方便……", "等等，我有点犹豫。", "这次我先不去了，抱歉。" };
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
