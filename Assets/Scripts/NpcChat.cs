using System;

// 对话的统一出口：一句话 → 记进这个 NPC 的档案（NpcMemory）→ 通知对话窗口显示 →（可选）NPC 头顶冒气泡。
// 玩家说的、NPC 回的、系统提示，全部走这里，所以每个 NPC 的聊天记录都是完整、独立的。
public static class NpcChat
{
    // 有新的一句话（npcId, 记录）。NpcChatWindow 监听它来刷新。
    public static event Action<string, ChatEntry> OnPosted;

    // bubble：NPC 说的话是否同时在头顶冒气泡（窗口没打开也能看到他在说什么）。
    public static ChatEntry Post(string npcId, ChatRole role, string text, bool bubble = false)
    {
        if (string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(text)) return null;
        NpcData npc = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(npcId) : null;
        string npcName = npc != null ? npc.npcName : npcId;

        string speaker, with;
        switch (role)
        {
            case ChatRole.Player: speaker = L("player_speaker"); with = npcName; break;
            case ChatRole.Npc:    speaker = npcName;             with = "player"; break;
            case ChatRole.System: speaker = L("system_speaker"); with = "";       break;
            default:              speaker = "?";                 with = npcName;  break;
        }

        ChatEntry e = NpcMemory.Add(npcId, role, speaker, with, text);
        if (e != null && OnPosted != null) OnPosted(npcId, e);

        if (bubble && role == ChatRole.Npc)
        {
            NpcMover m = NpcView.GetMover(npcId);
            if (m != null) NpcSpeechBubble.Show(m.transform, text, Math.Min(6f, IdleChat.LineDuration(text) + 1f));
        }
        return e;
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
}
