using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

// 一条聊天记录的说话人类型。
public enum ChatRole { Player, Npc, Other, System }

// 一条聊天记录（存进 NPC 档案文件）。
[Serializable]
public class ChatEntry
{
    public int day;          // 第几天
    public string role;      // "player" 玩家（终端里的声音） / "npc" 这个 NPC 自己 / "other" 别的 NPC / "system" 系统提示
    public string speaker;   // 说话人显示名
    public string with;      // 这句话是对谁说的："player" 或某个 NPC 的名字
    public string text;      // 内容

    [JsonIgnore]
    public ChatRole Role
    {
        get
        {
            switch (role)
            {
                case "player": return ChatRole.Player;
                case "npc":    return ChatRole.Npc;
                case "other":  return ChatRole.Other;
                default:       return ChatRole.System;
            }
        }
    }

    public static string RoleKey(ChatRole r)
    {
        switch (r)
        {
            case ChatRole.Player: return "player";
            case ChatRole.Npc:    return "npc";
            case ChatRole.Other:  return "other";
            default:              return "system";
        }
    }
}

// NPC 档案文件的结构：人物卡快照 + 最近 200 条聊天。
[Serializable]
public class NpcRecordFile
{
    public string id;
    public string savedAt;
    public NpcProfileSnapshot profile;
    public List<ChatEntry> chat = new List<ChatEntry>();
}

[Serializable]
public class NpcProfileSnapshot
{
    public string name, gender, profession, values, speakingStyle, background, currentGoal;
    public List<string> traits;
    public Personality personality;
    public float health, hunger, energy, sanity, playerTrust;
    public bool isBroken;
    public string currentAction;
    public List<string> mutations;
    public List<RelationSnapshot> relationships = new List<RelationSnapshot>();
    public List<MemoryEntry> recentEvents = new List<MemoryEntry>();
}

[Serializable]
public class RelationSnapshot
{
    public string id, name;
    public float trust, affinity;
}

// NPC 记忆档案（纯代码，不走 LLM）。
// 每开一局：在 persistentDataPath/SessionData/ 建一个临时文件夹（上一局的会被清掉），写入：
//   roster.json   —— NPC 名册（id ↔ 名字 / 称呼），玩家点名时用
//   lexicon.json  —— AI 词库（物品、地点的名字和说明）
//   npc_xx.json   —— 每个 NPC 一份：人物卡快照 + 最近 200 条聊天（和玩家的对话 + 和同伴的闲聊）
// 和 NPC 对话时，代码从这 200 条里挑出"最近几句"和"跟这次话题相关的旧记忆"喂给 LLM，
// 7B 小模型不用记住一切，只要演好眼前这一句——这就是扬长避短。
public static class NpcMemory
{
    public const int MaxChat = 200;

    static readonly Dictionary<string, List<ChatEntry>> _chats = new Dictionary<string, List<ChatEntry>>();
    static string _folder;

    // 本局档案文件夹。
    public static string Folder
    {
        get
        {
            if (_folder == null) _folder = Path.Combine(Application.persistentDataPath, "SessionData");
            return _folder;
        }
    }

    // ============ 开局 ============

    // 新的一局：清空并重建临时文件夹，写名册、词库和每个 NPC 的档案。由 WorldManager 在世界生成后调用。
    public static void BeginSession(WorldState w)
    {
        _chats.Clear();
        try
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            Directory.CreateDirectory(Folder);
        }
        catch (Exception e) { Debug.LogWarning("[NpcMemory] 建立档案文件夹失败：" + e.Message); }

        NpcRoster.Build(w);
        GameLexicon.Rebuild(w);
        WriteJson("roster.json", NpcRoster.Export());
        WriteJson("lexicon.json", GameLexicon.Export());

        if (w != null)
            foreach (NpcData n in w.npcs)
            {
                _chats[n.id] = new List<ChatEntry>();
                Save(n.id);
            }
        Debug.Log("[NpcMemory] 本局 NPC 档案文件夹：" + Folder);
    }

    // ============ 写入 ============

    // 记一条聊天。with：这句话是对谁说的（"player" 或 NPC 名字）。
    public static ChatEntry Add(string npcId, ChatRole role, string speaker, string with, string text, bool save = true)
    {
        if (string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(text)) return null;
        List<ChatEntry> list = Get(npcId);
        ChatEntry e = new ChatEntry
        {
            day = WorldManager.Instance != null ? WorldManager.Instance.world.day : 0,
            role = ChatEntry.RoleKey(role),
            speaker = speaker,
            with = with,
            text = text
        };
        list.Add(e);
        if (list.Count > MaxChat) list.RemoveRange(0, list.Count - MaxChat);
        if (save) Save(npcId);
        return e;
    }

    // 把一场 NPC 之间的闲聊记进双方的档案（以后和玩家聊天时，他们会"记得"跟谁聊过什么）。
    public static void AddIdleChat(IdleChatSession s)
    {
        if (s == null || s.lines == null) return;
        NpcData a = s.A, b = s.B;
        if (a == null || b == null) return;
        foreach (IdleChatLine line in s.lines)
        {
            NpcData speaker = line.byInitiator ? a : b;
            NpcData listener = line.byInitiator ? b : a;
            Add(speaker.id, ChatRole.Npc, speaker.npcName, listener.npcName, line.text, false);
            Add(listener.id, ChatRole.Other, speaker.npcName, listener.npcName, line.text, false);
        }
        Save(a.id);
        Save(b.id);
    }

    // ============ 读取 ============

    public static List<ChatEntry> All(string npcId) => Get(npcId);

    // 最近 count 条（跳过最后 skipLast 条；默认不含系统提示）。
    public static List<ChatEntry> Recent(string npcId, int count, int skipLast = 0, bool includeSystem = false)
    {
        List<ChatEntry> list = Get(npcId);
        List<ChatEntry> result = new List<ChatEntry>();
        for (int i = list.Count - 1 - skipLast; i >= 0 && result.Count < count; i--)
        {
            if (!includeSystem && list[i].Role == ChatRole.System) continue;
            result.Insert(0, list[i]);
        }
        return result;
    }

    // 回忆：从较早的记录里（不含最后 excludeLast 条），挑出和 query 最相关的最多 k 条，按时间顺序返回。
    // 相关度 = 共同的词条（物品/地点）+ 提到的同一个人 + 字面重合（两字片段，越少见的片段权重越高）。
    public static List<ChatEntry> Recall(string npcId, string query, int k, int excludeLast)
    {
        List<ChatEntry> list = Get(npcId);
        List<ChatEntry> result = new List<ChatEntry>();
        int n = list.Count - excludeLast;
        if (n <= 0 || string.IsNullOrEmpty(query) || k <= 0) return result;

        HashSet<string> qGrams = new HashSet<string>(Grams(query));
        HashSet<string> qLex = GameLexicon.FindIds(query);
        HashSet<string> qNpc = new HashSet<string>(NpcRoster.FindMentioned(query, null));
        if (qGrams.Count == 0 && qLex.Count == 0 && qNpc.Count == 0) return result;

        // 候选 + 各片段出现在多少条记录里（用来算稀有度）
        List<int> idx = new List<int>();
        List<HashSet<string>> grams = new List<HashSet<string>>();
        Dictionary<string, int> df = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            if (list[i].Role == ChatRole.System) continue;
            HashSet<string> g = new HashSet<string>(Grams(list[i].text));
            idx.Add(i);
            grams.Add(g);
            foreach (string x in g)
                if (qGrams.Contains(x)) df[x] = df.TryGetValue(x, out int c) ? c + 1 : 1;
        }

        List<KeyValuePair<int, float>> scored = new List<KeyValuePair<int, float>>();
        int total = idx.Count;
        for (int j = 0; j < total; j++)
        {
            float score = 0f;
            foreach (string x in grams[j])
                if (qGrams.Contains(x)) score += Mathf.Log((total + 1f) / (df[x] + 0.5f));
            string text = list[idx[j]].text;
            foreach (string id in GameLexicon.FindIds(text)) if (qLex.Contains(id)) score += 1.5f;
            foreach (string id in NpcRoster.FindMentioned(text, null)) if (qNpc.Contains(id)) score += 1.5f;
            if (score >= 2f) scored.Add(new KeyValuePair<int, float>(idx[j], score));
        }
        scored.Sort((x, y) => y.Value.CompareTo(x.Value));
        List<int> picked = new List<int>();
        for (int i = 0; i < scored.Count && picked.Count < k; i++) picked.Add(scored[i].Key);
        picked.Sort();
        foreach (int i in picked) result.Add(list[i]);
        return result;
    }

    // 文字切成"两字片段"（中日文按字，英文数字按字母），用于粗略的相关度比较。
    static List<string> Grams(string s)
    {
        List<string> list = new List<string>();
        if (string.IsNullOrEmpty(s)) return list;
        char prev = '\0';
        foreach (char c0 in s)
        {
            char c = char.ToLowerInvariant(c0);
            bool ok = char.IsLetterOrDigit(c);
            if (ok && prev != '\0') list.Add(new string(new[] { prev, c }));
            prev = ok ? c : '\0';
        }
        return list;
    }

    // ============ 存档 ============

    public static void Save(string npcId)
    {
        NpcData npc = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(npcId) : null;
        NpcRecordFile file = new NpcRecordFile
        {
            id = npcId,
            savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            profile = Snapshot(npc),
            chat = Get(npcId)
        };
        WriteJson(npcId + ".json", file);
    }

    static NpcProfileSnapshot Snapshot(NpcData n)
    {
        if (n == null) return null;
        NpcProfileSnapshot p = new NpcProfileSnapshot
        {
            name = n.npcName, gender = n.gender, profession = n.profession, values = n.values,
            speakingStyle = n.speakingStyle, background = n.background, currentGoal = n.currentGoal,
            traits = n.traits, personality = n.personality,
            health = n.health, hunger = n.hunger, energy = n.energy, sanity = n.sanity, playerTrust = n.playerTrust,
            isBroken = n.isBroken, currentAction = n.currentAction, mutations = n.mutations
        };
        if (n.relationships != null && WorldManager.Instance != null)
            foreach (Relationship r in n.relationships)
            {
                NpcData t = WorldManager.Instance.GetNpcById(r.targetId);
                p.relationships.Add(new RelationSnapshot { id = r.targetId, name = t != null ? t.npcName : r.targetId, trust = r.trust, affinity = r.affinity });
            }
        if (n.memories != null)
            for (int i = Mathf.Max(0, n.memories.Count - 20); i < n.memories.Count; i++) p.recentEvents.Add(n.memories[i]);
        return p;
    }

    static List<ChatEntry> Get(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return new List<ChatEntry>();
        if (!_chats.TryGetValue(npcId, out List<ChatEntry> list))
        {
            list = new List<ChatEntry>();
            _chats[npcId] = list;
        }
        return list;
    }

    static void WriteJson(string fileName, object data)
    {
        try
        {
            if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, fileName), JsonConvert.SerializeObject(data, Formatting.Indented), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[NpcMemory] 写文件失败 " + fileName + "：" + e.Message);
        }
    }
}
