using System.Collections.Generic;

// NPC 占用登记：某个系统正在"直接指挥"某个 NPC 时，先在这里登记，其他系统就知道要让路。
// 用法：开始指挥前 NpcBusy.Claim(id, 原因)，结束后 NpcBusy.Release(id, 原因)，两者成对。
//
// 【优先级】数字越大越优先，低优先级的系统发现有更高优先级的占用时要主动放手：
//   Task  玩家指令（NpcTaskRunner）                       100
//   Build 施工（BuildManager）                              80
//   Need  需求行为：吃饭/休息/搜寻/逃跑（NpcNeedRunner）    50
//   闲置行为（NpcIdleBrain）不登记，优先级最低：只要有任何占用就让路。
// 以后加新的指挥系统（采集、巡逻、搬运…）：在这里加一个常量 + 优先级即可。
// 纯数据，不走 LLM。
public static class NpcBusy
{
    public const string Task = "task";
    public const string Build = "build";
    public const string Need = "need";

    // npcId → 占用原因集合（同一个 NPC 可能同时被多个系统占用）
    static readonly Dictionary<string, HashSet<string>> _claims = new Dictionary<string, HashSet<string>>();

    // 各占用原因的优先级（没列出的按 60 算：比需求行为高、比施工低）。
    public static int PriorityOf(string reason)
    {
        switch (reason)
        {
            case Task:  return 100;
            case Build: return 80;
            case Need:  return 50;
            default:    return 60;
        }
    }

    // 登记占用。
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

    // 这个 NPC 现在是否被任何系统占用。
    public static bool IsBusy(string npcId)
    {
        return !string.IsNullOrEmpty(npcId) && _claims.ContainsKey(npcId);
    }

    // 是否被某个原因占用。
    public static bool IsHeldBy(string npcId, string reason)
    {
        return !string.IsNullOrEmpty(npcId)
               && _claims.TryGetValue(npcId, out HashSet<string> set) && set.Contains(reason ?? "");
    }

    // 有没有比 reason 更优先的占用（有的话，持有 reason 的系统应该放手）。
    public static bool HasHigherThan(string npcId, string reason)
    {
        if (string.IsNullOrEmpty(npcId) || !_claims.TryGetValue(npcId, out HashSet<string> set)) return false;
        int mine = PriorityOf(reason);
        foreach (string r in set)
            if (r != reason && PriorityOf(r) > mine) return true;
        return false;
    }

    // 能不能以 reason 的身份接管这个 NPC（没有同级或更高级的占用）。
    public static bool CanClaim(string npcId, string reason)
    {
        if (string.IsNullOrEmpty(npcId)) return false;
        if (!_claims.TryGetValue(npcId, out HashSet<string> set)) return true;
        int mine = PriorityOf(reason);
        foreach (string r in set)
            if (PriorityOf(r) >= mine) return false;
        return true;
    }

    // 清空所有登记（新开一局时由 NpcIdleManager 调用，防止上一局残留）。
    public static void ClearAll()
    {
        _claims.Clear();
    }
}
