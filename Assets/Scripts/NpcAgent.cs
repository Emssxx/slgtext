using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

// NPC 代理（新版）：玩家和"某一个" NPC 的一对一对话 + 指令。
//
// 【分工】扬长避短——7B 小模型只负责"演好眼前这一句"，其余全交给代码：
//   代码：点名（NpcRoster）→ 认指令（NpcCommands + GameLexicon 词库）→ 信任判定 → 排任务执行；
//         从这个 NPC 的档案（NpcMemory，最近 200 条）里挑"最近几句"和"跟这次话题相关的旧记忆"；
//         从词库里挑出玩家提到的东西的说明。
//   LLM ：只扮演这一个 NPC——最先读他的演员提示词（NpcActors），再读他经历过的事（EventChronicle）、
//         记忆和此刻的处境，说一两句话（也负责给"接受/做不到"的指令配音）。
//
// 每个 NPC 有自己的对话窗口（NpcChatWindow）和自己的聊天记录，不再共用一个聊天栏、也不会串台。
public class NpcAgent : MonoBehaviour
{
    [Header("引用与设置")]
    public ChatManager chatManager;  // 旧版共用聊天栏（保留引用，新版不再使用）
    public string npcId = "npc_01";  // 最近一次对话的 NPC

    const int RecentLines = 8;  // 提示词里放最近几句对话
    const int RecallLines = 4;  // 提示词里放几条相关的旧记忆

    void Start()
    {
        NpcChatWindow.Ensure().OnPlayerMessage += HandlePlayerMessage;
    }

    void OnDestroy()
    {
        if (NpcChatWindow.Instance != null) NpcChatWindow.Instance.OnPlayerMessage -= HandlePlayerMessage;
    }

    // 打开某个 NPC 的对话窗口（点场景里的 NPC、点状态卡时调用）。
    public void SwitchTarget(string newNpcId)
    {
        if (string.IsNullOrEmpty(newNpcId)) return;
        npcId = newNpcId;
        NpcChatWindow.Ensure().Open(newNpcId);
    }

    // ============ 玩家在某个 NPC 的窗口里说了一句话 ============
    void HandlePlayerMessage(string id, string text)
    {
        if (WorldManager.Instance == null) return;
        WorldState world = WorldManager.Instance.world;
        NpcData npc = WorldManager.Instance.GetNpcById(id);
        if (npc == null) return;

        // 1. 点名：一开口就在叫某人（"王芳，去仓库" / "让王芳去仓库"）→ 由那个人来执行，窗口也切过去。
        string body = text;
        string called = NpcRoster.FindAddressee(text, out string rest);
        if (called != null)
        {
            if (!string.IsNullOrEmpty(rest)) body = rest;
            if (called != id)
            {
                NpcData other = WorldManager.Instance.GetNpcById(called);
                if (other != null)
                {
                    NpcChat.Post(id, ChatRole.System, LF("chatwin_rerouted", other.npcName));
                    id = called;
                    npc = other;
                    NpcChatWindow.Ensure().Open(id);
                }
            }
        }
        npcId = id;
        NpcChat.Post(id, ChatRole.Player, text);

        // 2. 死亡 / 崩解拦截
        if (npc.IsDead)
        {
            NpcChat.Post(id, ChatRole.System, LF("npc_dead_cannot_talk", npc.npcName));
            return;
        }
        NpcIdleBrain.InterruptFor(id, 8f); // 停下闲逛/闲聊，站着听你说（执行指令时不受影响）
        if (npc.isBroken)
        {
            NpcChat.Post(id, ChatRole.Npc, Prefix(npc) + BrokenSpeech.Generate(npc), true);
            return;
        }

        // 3. 是指令吗？（代码用词库 + 名册 + 动词表认，认不出就是普通聊天）
        NpcCommand cmd = NpcCommands.Parse(body, npc, world);
        if (cmd != null) { HandleCommand(npc, cmd, body); return; }

        // 4. 普通聊天
        RequestReply(npc, body, null);
    }

    // ============ 指令 ============
    void HandleCommand(NpcData npc, NpcCommand cmd, string playerText)
    {
        WorldState world = WorldManager.Instance.world;

        // 做不到（仓库没有、地点不对、对象已死…）：不执行，让 NPC 用自己的口吻说明原因。
        if (!NpcCommands.Plan(npc, cmd, world, out List<NpcTask> tasks, out string order, out string fail))
        {
            RequestReply(npc, playerText, LF("sit_order_fail", order, fail));
            return;
        }

        // 信任判定：这次听不听你的（规则掷骰，不调 LLM）。
        if (!TrustRules.WillObey(npc))
        {
            NpcChat.Post(npc.id, ChatRole.Npc, Prefix(npc) + GetRefuseLine(npc), true);
            return;
        }

        NpcMover mover = NpcView.GetMover(npc.id);
        NpcTaskRunner runner = mover != null ? mover.GetComponent<NpcTaskRunner>() : null;
        if (runner == null)
        {
            RequestReply(npc, playerText, LF("sit_order_fail", order, L("fail_place_unknown")));
            return;
        }

        // 新指令覆盖旧指令：先停下手上的，再开始新的（动作立刻开始，台词随后由 LLM 配上）。
        runner.CancelAll();
        runner.RunTasks(tasks);
        NpcChat.Post(npc.id, ChatRole.System, LF("chatwin_order_started", npc.npcName, order));
        RequestReply(npc, playerText, LF("sit_order_ok", order));
    }

    // ============ 让 LLM 以这个 NPC 的身份说一句 ============
    // situation：此刻的特殊情况（接受了指令 / 做不到），没有就传 null。
    void RequestReply(NpcData npc, string playerText, string situation)
    {
        string id = npc.id;
        NpcChatWindow.Ensure().SetThinking(id, true);
        string systemPrompt = BuildSystemPrompt(npc, playerText, situation);

        AIClientHub.Instance.Send(systemPrompt, playerText, raw =>
        {
            if (NpcChatWindow.Instance != null) NpcChatWindow.Instance.SetThinking(id, false);
            NpcData n = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(id) : null;
            string line = DialogueCleaner.Clean(ParseDialogue(raw), n != null ? n.npcName : "");
            if (string.IsNullOrEmpty(line)) line = "……";
            NpcChat.Post(id, ChatRole.Npc, (n != null ? Prefix(n) : "") + line, true);
        }, true);
    }

    // ============ 提示词：一个 NPC 的完整"剧本" ============
    // 整段按当前界面语言写，顺序固定：
    //   ① 演员提示词（最先读，NpcActors）→ ② 此刻对 AI 的态度（按当前信任度）→ ③ 世界观（NPC 视角）+ AI 是谁
    //   → ④ 当前状况 → ⑤ 你经历过的事（EventChronicle，按这句话挑）→ ⑥ 话里提到的东西（词库）
    //   → ⑦ 你想起来的事 → ⑧ 最近的对话 → ⑨ 此刻 / 精神状态 → ⑩ 演出规则 + 输出格式（放最后，小模型最听得进去）
    string BuildSystemPrompt(NpcData npc, string playerText, string situation)
    {
        WorldState world = WorldManager.Instance.world;
        bool jp = LanguageGuard.IsJp;

        string actor = NpcActors.Render(npc);
        string trustNow = ActorFacts.TrustNow(npc, jp);
        string context = PromptContext.WorldSummary(world, npc, false);
        string events = EventChronicle.ForPrompt(npc, playerText, jp);
        string hints = GameLexicon.Hints(playerText + " " + situation);
        string recent = FormatRecent(npc, jp);
        string recalled = FormatRecalled(npc, playerText, jp);
        string mental = MentalState.GetInjection(npc, world);
        string langRule = LocalizationManager.Instance != null ? LocalizationManager.Instance.GetAILanguageInstruction() : "";

        StringBuilder sb = new StringBuilder();
        sb.AppendLine(actor);
        if (jp)
        {
            sb.AppendLine("【今のAIへの態度】" + trustNow);
            sb.AppendLine();
            sb.AppendLine(WorldLore.NpcWorld);
            sb.AppendLine();
            sb.AppendLine(WorldLore.PlayerIdentity);
            sb.AppendLine();
            sb.AppendLine(WorldLore.NpcMood);
            sb.AppendLine();
            sb.Append(context);
            if (events.Length > 0) sb.Append(events);
            if (hints.Length > 0) { sb.AppendLine("【話に出てきたものの説明】"); sb.Append(hints); }
            if (recalled.Length > 0) { sb.AppendLine("【あなたが思い出したこと】"); sb.Append(recalled); }
            if (recent.Length > 0) { sb.AppendLine("【最近の会話】"); sb.Append(recent); }
            if (!string.IsNullOrEmpty(situation)) { sb.AppendLine("【今この瞬間】"); sb.AppendLine(situation); }
            sb.AppendLine(mental);
            sb.AppendLine();
            sb.AppendLine(WorldLore.SpeakingRules);
            sb.AppendLine();
            sb.AppendLine("【出力形式】JSONオブジェクトを1つだけ出力し、それ以外は何も書かないこと：");
            sb.AppendLine("{\"dialogue\": \"この人物として、実際に口に出す台詞\"}");
            sb.AppendLine("台詞は1〜2文、短く口語的に。AIが今言ったことにそのまま答えること。前に言ったことを繰り返さない。");
        }
        else
        {
            sb.AppendLine("【你此刻对AI的态度】" + trustNow);
            sb.AppendLine();
            sb.AppendLine(WorldLore.NpcWorld);
            sb.AppendLine();
            sb.AppendLine(WorldLore.PlayerIdentity);
            sb.AppendLine();
            sb.AppendLine(WorldLore.NpcMood);
            sb.AppendLine();
            sb.Append(context);
            if (events.Length > 0) sb.Append(events);
            if (hints.Length > 0) { sb.AppendLine("【话里提到的东西】"); sb.Append(hints); }
            if (recalled.Length > 0) { sb.AppendLine("【你想起来的事】"); sb.Append(recalled); }
            if (recent.Length > 0) { sb.AppendLine("【最近的对话】"); sb.Append(recent); }
            if (!string.IsNullOrEmpty(situation)) { sb.AppendLine("【此刻】"); sb.AppendLine(situation); }
            sb.AppendLine(mental);
            sb.AppendLine();
            sb.AppendLine(WorldLore.SpeakingRules);
            sb.AppendLine();
            sb.AppendLine("【输出要求】只输出一个 JSON 对象，不要任何多余文字：");
            sb.AppendLine("{\"dialogue\": \"以这个人的身份，真正说出口的话\"}");
            sb.AppendLine("只说一到两句，口语、简短。直接回应AI刚才的话，不要重复你之前说过的话。");
        }
        sb.AppendLine();
        sb.Append(langRule);
        return sb.ToString();
    }

    // 最近的对话（和玩家的，也包括刚和同伴聊过的），不含刚说的这一句（它会作为用户输入单独给）。
    string FormatRecent(NpcData npc, bool jp)
    {
        List<ChatEntry> list = NpcMemory.Recent(npc.id, RecentLines + 1);
        if (list.Count > 0 && list[list.Count - 1].Role == ChatRole.Player) list.RemoveAt(list.Count - 1);
        StringBuilder sb = new StringBuilder();
        foreach (ChatEntry e in list) sb.AppendLine(Line(e, jp));
        return sb.ToString();
    }

    // 从更早的记录里，挑出和这句话相关的回忆。
    string FormatRecalled(NpcData npc, string playerText, bool jp)
    {
        List<ChatEntry> list = NpcMemory.Recall(npc.id, playerText, RecallLines, RecentLines + 4);
        StringBuilder sb = new StringBuilder();
        foreach (ChatEntry e in list)
            sb.AppendLine((jp ? $"・{e.day}日目　" : $"・第{e.day}天　") + Line(e, jp));
        return sb.ToString();
    }

    // 一条记录 → 提示词里的一行（"AI：…" / "你：…" / "王芳（对你）：…"）。
    static string Line(ChatEntry e, bool jp)
    {
        switch (e.Role)
        {
            case ChatRole.Player:
                return "AI：" + e.text;
            case ChatRole.Npc:
                if (string.IsNullOrEmpty(e.with) || e.with == "player") return (jp ? "あなた：" : "你：") + e.text;
                return (jp ? "あなた（" + e.with + "に）：" : "你（对" + e.with + "）：") + e.text;
            case ChatRole.Other:
                return (jp ? e.speaker + "（あなたに）：" : e.speaker + "（对你）：") + e.text;
            default:
                return e.text;
        }
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
        bool jp = LanguageGuard.IsJp;
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
        if (string.IsNullOrEmpty(raw)) return "";
        try
        {
            int start = raw.IndexOf('{');
            int end = raw.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                NpcReplyData d = JsonConvert.DeserializeObject<NpcReplyData>(raw.Substring(start, end - start + 1));
                if (d != null && !string.IsNullOrEmpty(d.dialogue))
                    return d.dialogue;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("解析 NPC 对话 JSON 失败，按纯文本处理。错误：" + e.Message);
        }
        return raw;
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;
}

// AI 对话输出契约（只有 dialogue）。
[Serializable]
public class NpcReplyData
{
    public string dialogue;
}
