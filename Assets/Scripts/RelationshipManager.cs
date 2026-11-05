using UnityEngine;
using System.Collections.Generic;

// 关系工具：统一管理 NPC 之间关系的读取和修改。
// 关系是单向的——A 对 B 的关系，存在 A 的 relationships 列表里。
// 三个数值：好感（-100~100，仇恨 ↔ 亲近）、信任（0~100，怀疑 ↔ 信任）、尊重（0~100，看不起 ↔ 敬重）。
// 数值只由代码按发生的事结算（Apply），每次变化记一句原因；AI 只读（PromptBlock），不改。
// 由数值推出"感觉"标签：仇恨 / 厌恶 / 怀疑 / 亲近 / 友善 / 普通（+ 敬重 / 看不起），标签变了会写一条日志。
public static class RelationshipManager
{
    // 取得 A 对 B 的关系记录（没有就返回 null）。
    public static Relationship Get(NpcData from, string toId)
    {
        if (from == null || from.relationships == null) return null;
        foreach (Relationship r in from.relationships)
            if (r.targetId == toId) return r;
        return null;
    }

    // 取得 A 对 B 的关系；如果还没有，就新建一条默认关系并返回。
    public static Relationship GetOrCreate(NpcData from, string toId)
    {
        Relationship r = Get(from, toId);
        if (r == null)
        {
            r = new Relationship { targetId = toId, trust = 50f, affinity = 0f, respect = 50f, note = "" };
            if (from.relationships == null) from.relationships = new List<Relationship>();
            from.relationships.Add(r);
        }
        return r;
    }

    // 修改 A 对 B 的信任度（增量，可正可负），自动夹在 0~100。
    public static void ChangeTrust(NpcData from, string toId, float delta)
    {
        Relationship r = GetOrCreate(from, toId);
        r.trust = Mathf.Clamp(r.trust + delta, 0f, 100f);
    }

    // 修改 A 对 B 的友善度（增量），自动夹在 -100~100。
    public static void ChangeAffinity(NpcData from, string toId, float delta)
    {
        Relationship r = GetOrCreate(from, toId);
        r.affinity = Mathf.Clamp(r.affinity + delta, -100f, 100f);
    }

    // —— 考虑健康状态后的"此刻"好感 / 信任（关系本身不改，状态消失就恢复）——
    // 饥饿 / 饥渴难耐：好感 -20（NpcHealth.affinityPenalty）。
    public static float AffinityOf(NpcData from, string toId)
    {
        Relationship r = Get(from, toId);
        float a = r != null ? r.affinity : 0f;
        return Mathf.Clamp(a - NpcHealth.AffinityPenalty(from), -100f, 100f);
    }

    // 精神病：对营地里的人信任降为 0。
    public static float TrustOf(NpcData from, string toId)
    {
        if (NpcHealth.DistrustsEveryone(from)) return 0f;
        Relationship r = Get(from, toId);
        return r != null ? r.trust : 50f;
    }

    // 修改 A 对 B 的尊重（增量），自动夹在 0~100。
    public static void ChangeRespect(NpcData from, string toId, float delta)
    {
        Relationship r = GetOrCreate(from, toId);
        r.respect = Mathf.Clamp(r.respect + delta, 0f, 100f);
    }

    public static float RespectOf(NpcData from, string toId)
    {
        Relationship r = Get(from, toId);
        return r != null ? r.respect : 50f;
    }

    // ============ 按发生的事结算（所有改关系的地方尽量走这里）============

    const int MaxReasons = 4;

    // A 对 B：好感 / 信任 / 尊重各变多少，reason = 这件事（已本地化，可空；空 = 小事不记）。
    // "感觉"标签变了（比如从普通变成厌恶）会写一条行为日志 + A 的记忆。
    public static void Apply(NpcData from, NpcData to, float dAffinity, float dTrust, float dRespect, string reason)
    {
        if (from == null || to == null || from == to || from.IsDead) return;
        Relationship r = GetOrCreate(from, to.id);
        string before = PrimaryKey(r.affinity, r.trust);
        r.affinity = Mathf.Clamp(r.affinity + dAffinity, -100f, 100f);
        r.trust = Mathf.Clamp(r.trust + dTrust, 0f, 100f);
        r.respect = Mathf.Clamp(r.respect + dRespect, 0f, 100f);
        if (!string.IsNullOrEmpty(reason))
        {
            if (r.reasons == null) r.reasons = new List<string>();
            r.reasons.Add(reason);
            while (r.reasons.Count > MaxReasons) r.reasons.RemoveAt(0);
        }
        string after = PrimaryKey(r.affinity, r.trust);
        if (after != before)
        {
            string log = LF("rel_changed", from.npcName, to.npcName, L(after));
            int day = WorldManager.Instance != null && WorldManager.Instance.world != null ? WorldManager.Instance.world.day : 0;
            IdleChatRules.AddMemory(from, day, log);
            if (LogManager.Instance != null) LogManager.Instance.AddLog(from.npcName, log, from.id);
        }
    }

    // 一起建成了一座建筑：施工的人互相更亲近、更信任、更尊重；营地里其他人对施工的人多一点尊重。
    public static void OnBuilt(List<string> crew, string siteName)
    {
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w == null || crew == null || crew.Count == 0) return;
        int day = w.day;
        foreach (string aId in crew)
            foreach (string bId in crew)
            {
                if (aId == bId) continue;
                NpcData a = WorldManager.Instance.GetNpcById(aId), b = WorldManager.Instance.GetNpcById(bId);
                if (a != null && b != null) Apply(a, b, 6f, 4f, 3f, LF("rel_reason_built_together", day, b.npcName, siteName));
            }
        foreach (NpcData o in w.npcs)
        {
            if (o == null || o.IsDead || crew.Contains(o.id)) continue;
            foreach (string bId in crew)
            {
                NpcData b = WorldManager.Instance.GetNpcById(bId);
                if (b != null) Apply(o, b, 1f, 1f, 2f, null);
            }
        }
    }

    // ============ "感觉"标签 ============

    // 只看好感和信任的主标签（标签变化、日志用）。
    static string PrimaryKey(float aff, float trust)
    {
        if (aff <= -70f) return "rel_label_hate";
        if (aff <= -35f) return "rel_label_dislike";
        if (trust <= 25f) return "rel_label_suspicious";
        if (aff >= 60f) return "rel_label_close";
        if (aff >= 30f) return "rel_label_friendly";
        return "rel_label_neutral";
    }

    // A 此刻对 B 的感觉（含饥饿等状态的临时影响），例如"友善・敬重"。
    public static string Label(NpcData from, string toId)
    {
        string key = PrimaryKey(AffinityOf(from, toId), TrustOf(from, toId));
        string s = L(key);
        float rs = RespectOf(from, toId);
        if (rs >= 75f && key != "rel_label_hate" && key != "rel_label_dislike") s += "・" + L("rel_label_respect");
        else if (rs <= 25f) s += "・" + L("rel_label_contempt");
        return s;
    }

    // 给 AI 读的"你和其他人的关系"：每人一行，感觉 + 数值 + 最近一件事。
    public static string PromptBlock(NpcData self, bool jp)
    {
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (self == null || w == null) return "";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine(jp ? "【ほかの人との関係】" : "【你和其他人的关系】");
        NpcData leader = NpcSociety.Leader;   // 领导和今天的分工（NpcSociety）
        if (leader != null) sb.AppendLine(leader == self ? (jp ? "拠点のリーダー：あなた自身" : "营地领导：你自己") : (jp ? "拠点のリーダー：" : "营地领导：") + leader.npcName);
        if (NpcSociety.HasRole(self)) sb.AppendLine((jp ? "今日のあなたの仕事：" : "你今天的分工：") + NpcSociety.RoleName(self.role));
        int listed = 0;
        foreach (NpcData o in w.npcs)
        {
            if (o == null || o == self || o.IsDead || listed >= 9) continue;
            Relationship r = Get(self, o.id);
            sb.Append("・").Append(o.npcName).Append("：").Append(Label(self, o.id));
            sb.Append(jp ? "（好感 " : "（好感 ").Append(Mathf.RoundToInt(AffinityOf(self, o.id)))
              .Append(jp ? "／信頼 " : "，信任 ").Append(Mathf.RoundToInt(TrustOf(self, o.id)))
              .Append(jp ? "／尊敬 " : "，尊重 ").Append(Mathf.RoundToInt(RespectOf(self, o.id))).Append("）");
            if (r != null && r.reasons != null && r.reasons.Count > 0)
                sb.Append(jp ? "　最近：" : "　最近：").Append(r.reasons[r.reasons.Count - 1]);
            sb.AppendLine();
            listed++;
        }
        return listed > 0 ? sb.ToString() : "";
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;

    // 为世界里所有 NPC 互相建立初始关系（两两之间，单向各一条）。
    // 在 WorldManager 创建完世界、NPC 都就位后调用一次。
    public static void InitAllRelationships(WorldState world)
    {
        if (world == null || world.npcs == null) return;

        foreach (NpcData a in world.npcs)
        {
            foreach (NpcData b in world.npcs)
            {
                if (a.id == b.id) continue; // 不和自己建立关系
                GetOrCreate(a, b.id);       // A 对 B 建一条默认关系（50信任/0友善）
            }
        }
    }

    // 调试用：把某个 NPC 对所有人的关系打印出来。
    public static string Describe(NpcData from, WorldState world)
    {
        if (from == null || from.relationships == null) return from?.npcName + "：无关系记录";
        string s = from.npcName + " 的关系：\n";
        foreach (Relationship r in from.relationships)
        {
            NpcData target = null;
            foreach (NpcData n in world.npcs) if (n.id == r.targetId) { target = n; break; }
            string name = target != null ? target.npcName : r.targetId;
            s += $"  对 {name}：信任 {r.trust:0} / 友善 {r.affinity:0} / 尊重 {r.respect:0}（{r.note}）\n";
        }
        return s;
    }
}
