using System.Collections.Generic;
using System.Text;

// 提示词上下文：把世界状态整理成一段【当前语言】的简短文字，代替原来整份世界 JSON。
//
// 为什么要换掉 JSON：
//   1. 原来的世界 JSON 里全是中文值（地点、行为、记忆…），选日语时会把模型往中文带。
//   2. NPC 记忆会不断累积，JSON 越来越长，超出本地模型的上下文长度后，
//      提示词开头（世界观、人设、语言指令）会被截掉——这是模型"越玩越发疯"的重要原因。
// 这里只保留 AI 真正需要的信息，并且只放【符合当前语言】的文字。
public static class PromptContext
{
    const int MaxEvents = 4;    // 最近几条大事
    const int MaxMemories = 4;  // 这个 NPC 最近几条记忆

    // self：给某个 NPC 用时传入（会附上他自己的记忆）；导演用时传 null。
    // includeEvents：是否附上全局的"最近发生的事"。NPC 对话改用 EventChronicle（带他自己的经历和看法），传 false。
    public static string WorldSummary(WorldState w, NpcData self = null, bool includeEvents = true)
    {
        if (w == null) return "";
        bool jp = LanguageGuard.IsJp;
        var sb = new StringBuilder();

        // —— 基本状况 ——
        sb.AppendLine(jp ? "【現在の状況】" : "【当前状况】");
        sb.AppendLine(jp
            ? $"・{w.day}日目。亜空間の異常度：{w.anomalyLevel:0}/100（高いほど危険）"
            : $"・第{w.day}天。亚空间异常度：{w.anomalyLevel:0}/100（越高越危险）");
        if (ClimateSystem.Instance != null) sb.AppendLine(ClimateSystem.Instance.PromptLine(jp)); // 天气和气温
        int tier = ProgressionRules.Tier;
        sb.AppendLine(jp
            ? (tier > 0 ? $"・AI端末：Lv{tier}（稼働中）" : "・AI端末：まだ建っていない")
            : (tier > 0 ? $"・AI终端机：Lv{tier}（运行中）" : "・AI终端机：尚未建成"));

        // —— 幸存者 ——
        sb.AppendLine(jp ? "【生存者】" : "【幸存者】");
        foreach (NpcData n in w.npcs)
        {
            string me = (self != null && n.id == self.id) ? (jp ? "（あなた）" : "（你自己）") : "";
            if (n.IsDead) { sb.AppendLine($"・{n.npcName}{me}：" + (jp ? "死亡" : "已死亡")); continue; }
            // 饥饿值 / 水分：100 = 饱 / 不渴（写成"満腹""饱腹"，免得模型以为数字越大越饿）
            sb.AppendLine(jp
                ? $"・{n.npcName}{me}：生命{n.health:0} 満腹{n.satiety:0} 水分{n.water:0} 体力{n.energy:0} 正気{n.sanity:0}（{MentalLabel(n, true)}／{NpcHealth.BriefLine(n, true)}）" + Carry(n, true)
                : $"・{n.npcName}{me}：生命{n.health:0} 饱腹{n.satiety:0} 水分{n.water:0} 体力{n.energy:0} 理智{n.sanity:0}（{MentalLabel(n, false)}／{NpcHealth.BriefLine(n, false)}）" + Carry(n, false));
        }

        // —— 仓库 ——
        var items = new List<string>();
        foreach (ItemStack s in w.sharedStorage)
            if (s.count > 0) items.Add(ProgressionRules.ItemName(s.itemName) + "×" + s.count);
        sb.AppendLine((jp ? "【拠点の倉庫】" : "【营地仓库】") + (items.Count > 0 ? string.Join("、", items) : (jp ? "空っぽ" : "空的")));

        // —— 营地眼下最要紧的事（隐形的营地参谋 CampAdvisor 整理；只给 NPC 自己的提示词：想事情、和 AI 说话）——
        if (self != null) sb.Append(CampAdvisor.PromptBlock(jp));

        // —— 最近大事（只取符合当前语言的条目）——
        if (includeEvents) AppendRecent(sb, w.eventLog, MaxEvents, jp ? "【最近の出来事】" : "【最近发生的事】");

        // —— 这个 NPC 自己的记忆 ——
        if (self != null) AppendRecent(sb, self.memories, MaxMemories, jp ? "【あなたの最近の記憶】" : "【你最近的记忆】");

        return sb.ToString();
    }

    // 从末尾往前取最多 max 条、语言正确的记录，按时间顺序写出。
    // 身上带着什么（没带东西就不写，省提示词）
    static string Carry(NpcData n, bool jp)
    {
        if (NpcInventory.UsedSlots(n) == 0) return "";
        return (jp ? " 持ち物：" : " 身上带着：") + NpcInventory.Describe(n, jp);
    }

    static void AppendRecent(StringBuilder sb, List<MemoryEntry> log, int max, string title)
    {
        if (log == null || log.Count == 0) return;
        Language lang = LanguageGuard.IsJp ? Language.Japanese : Language.Chinese;
        var picked = new List<MemoryEntry>();
        for (int i = log.Count - 1; i >= 0 && picked.Count < max; i--)
            if (!string.IsNullOrEmpty(log[i].content) && LanguageGuard.TextMatches(log[i].content, lang, true))
                picked.Insert(0, log[i]);
        if (picked.Count == 0) return;
        sb.AppendLine(title);
        foreach (MemoryEntry m in picked)
            sb.AppendLine(LanguageGuard.IsJp ? $"・{m.day}日目：{m.content}" : $"・第{m.day}天：{m.content}");
    }

    // 精神状态的文字（当前语言）。
    static string MentalLabel(NpcData n, bool jp)
    {
        switch (MentalState.GetLevel(n))
        {
            case SanityLevel.Dazed:         return jp ? "放心状態" : "失神";
            case SanityLevel.Hallucinating: return jp ? "幻覚" : "幻觉";
            case SanityLevel.Broken:        return jp ? "人格崩壊" : "人格崩解";
            default:                        return jp ? "正常" : "清醒";
        }
    }
}
