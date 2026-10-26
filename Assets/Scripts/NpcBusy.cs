using System.Collections.Generic;

// NPC 占用登记：某个系统正在"直接指挥"某个 NPC 时（施工、以后的采集/巡逻/搬运等），
// 先在这里登记，闲置行为（NpcIdleBrain）看到登记就不去碰这个 NPC 的移动。
// 玩家指令（NpcTaskRunner）不用登记——闲置系统会直接读 NpcTaskRunner.IsBusy。
// 用法：开始指挥前 NpcBusy.Claim(id, "build")，指挥结束后 NpcBusy.Release(id, "build")，两者成对。
// 纯数据，不走 LLM。
public static class NpcBusy
{
    // npcId → 占用原因集合（同一个 NPC 可能同时被多个系统占用）
    static readonly Dictionary<string, HashSet<string>> _claims = new Dictionary<string, HashSet<string>>();

    // 登记占用。reason 用英文短词，如 "build"。
    public static void Claim(string npcId, string reason)
    {
        if (string.IsNullOrEmpty(npcId)) return;
        if (!_claims.TryGetValue(npcId, out HashSet<string> set))
        {
            set = new HashSet<string>();
            _claims[npcId] = set;
        }
        set.Add(reason ?? "");
    }

    // 解除占用（和 Claim 成对调用）。
    public static void Release(string npcId, string reason)
    {
        if (string.IsNullOrEmpty(npcId)) return;
        if (_claims.TryGetValue(npcId, out HashSet<string> set))
        {
            set.Remove(reason ?? "");
            if (set.Count == 0) _claims.Remove(npcId);
        }
    }

    // 这个 NPC 现在是否被某个系统占用。
    public static bool IsBusy(string npcId)
    {
        return !string.IsNullOrEmpty(npcId) && _claims.ContainsKey(npcId);
    }

    // 清空所有登记（新开一局时由 NpcIdleManager 调用，防止上一局残留）。
    public static void ClearAll()
    {
        _claims.Clear();
    }
}
