using UnityEngine;
using System.Collections.Generic;

// 关系工具：统一管理 NPC 之间关系的读取和修改。
// 关系是单向的——A 对 B 的关系，存在 A 的 relationships 列表里。
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
            r = new Relationship { targetId = toId, trust = 50f, affinity = 0f, note = "" };
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
            s += $"  对 {name}：信任 {r.trust:0} / 友善 {r.affinity:0}（{r.note}）\n";
        }
        return s;
    }
}
