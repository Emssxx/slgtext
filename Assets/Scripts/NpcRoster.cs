using System;
using System.Collections.Generic;

// NPC 名册（纯代码，不走 LLM）：开局时收集所有 NPC 的名字和常见称呼，存成 roster.json。
// 用途：
//   1. 点名：玩家在任何一个对话窗口里说"王芳，去仓库"/"让王芳去仓库"/"田中さん、倉庫に行って"，
//      代码查名册认出是在叫王芳，就把这句话转给王芳执行（对话窗口也切到王芳）。
//   2. 指令里提到别人："给王芳用急救包"→ 代码查名册得到王芳的 id，作为使用对象。
//   3. 回忆：判断两段话是不是在说同一个人。
public static class NpcRoster
{
    [Serializable]
    public class Entry
    {
        public string id;
        public string name;
        public string gender;
        public string profession;
        public List<string> aliases = new List<string>(); // 名字 + 常见称呼（老张、小张、阿伟…），长的在前
    }

    static readonly List<Entry> _entries = new List<Entry>();

    // 日语的称呼后缀（"田中さん" 也算叫田中）。
    static readonly string[] Honorifics = { "さん", "くん", "ちゃん", "君", "様", "さま", "殿" };
    // 叫人的分隔符：名字后面跟着这些，就是在叫这个人。
    const string Separators = "，,、：:！!。 　~～…";
    // 中文"让/叫/请 + 名字 + 去做某事"。
    static readonly string[] Causatives = { "让", "叫", "请", "喊" };
    // 名字后面直接跟这些动词开头（"王芳去仓库"），也算在叫这个人。
    static readonly string[] VerbStarts =
    {
        "去", "到", "拿", "取", "给", "回", "停", "别", "搜", "找", "休息", "歇", "吃", "推", "过来", "来", "跟", "帮", "用",
        "行って", "来て", "戻って", "取って", "持って", "探して", "休んで", "使って", "押して"
    };

    public static IReadOnlyList<Entry> Entries => _entries;

    // 开局：根据当前所有 NPC 建名册。
    public static void Build(WorldState w)
    {
        _entries.Clear();
        if (w == null) return;
        bool zh = !LanguageGuard.IsJp;
        Dictionary<string, int> used = new Dictionary<string, int>();
        foreach (NpcData n in w.npcs)
        {
            Entry e = new Entry { id = n.id, name = n.npcName, gender = n.gender, profession = n.profession };
            foreach (string a in CandidateAliases(n.npcName, zh))
                if (!e.aliases.Contains(a)) e.aliases.Add(a);
            foreach (string a in e.aliases) used[a] = used.TryGetValue(a, out int c) ? c + 1 : 1;
            _entries.Add(e);
        }
        // 两个人共用的称呼（比如两个人都姓王时的"老王"）不能用来点名，删掉；本名永远保留。
        foreach (Entry e in _entries)
        {
            e.aliases.RemoveAll(a => a != e.name && used[a] > 1);
            e.aliases.Sort((x, y) => y.Length.CompareTo(x.Length));
        }
    }

    // 一个名字的常见称呼。中文名：老张/小张（姓）、小伟/阿伟（单字名）；日文名只用本名（后缀在匹配时处理）。
    static List<string> CandidateAliases(string name, bool zh)
    {
        List<string> list = new List<string>();
        if (string.IsNullOrEmpty(name)) return list;
        list.Add(name);
        if (!zh || name.Length < 2 || name.Length > 3) return list;
        char first = name[0];
        if (first == '老' || first == '小' || first == '阿') return list; // 本身就是外号（如"老五"）
        string sur = name.Substring(0, 1), given = name.Substring(1);
        list.Add("老" + sur);
        list.Add("小" + sur);
        if (given.Length == 1) { list.Add("小" + given); list.Add("阿" + given); }
        else list.Add(given);
        return list;
    }

    public static Entry Get(string id)
    {
        foreach (Entry e in _entries) if (e.id == id) return e;
        return null;
    }

    // 文字里提到了哪些 NPC（按第一次出现的位置排序）。excludeId：不算这个人（比如说话对象自己）。
    public static List<string> FindMentioned(string text, string excludeId)
    {
        List<KeyValuePair<int, string>> hits = new List<KeyValuePair<int, string>>();
        if (!string.IsNullOrEmpty(text))
            foreach (Entry e in _entries)
            {
                if (e.id == excludeId) continue;
                int best = -1;
                foreach (string a in e.aliases)
                {
                    int i = text.IndexOf(a, StringComparison.Ordinal);
                    if (i >= 0 && (best < 0 || i < best)) best = i;
                }
                if (best >= 0) hits.Add(new KeyValuePair<int, string>(best, e.id));
            }
        hits.Sort((x, y) => x.Key.CompareTo(y.Key));
        List<string> ids = new List<string>();
        foreach (var h in hits) ids.Add(h.Value);
        return ids;
    }

    // 点名：这句话是不是一开口就在叫某个 NPC？是的话返回他的 id，并把"叫人"的部分去掉，剩下的话放进 rest。
    // 认得的形式："王芳，去仓库" "王芳去仓库" "让王芳去仓库" "田中さん、倉庫に行って" "田中さん倉庫に行って"
    public static string FindAddressee(string text, out string rest)
    {
        rest = text;
        if (string.IsNullOrEmpty(text)) return null;
        string s = text.TrimStart();

        // 中文"让/叫/请 + 名字"
        foreach (string c in Causatives)
            if (s.StartsWith(c, StringComparison.Ordinal))
            {
                string after = s.Substring(c.Length);
                foreach (Entry e in _entries)
                    foreach (string a in e.aliases)
                        if (after.StartsWith(a, StringComparison.Ordinal))
                        {
                            rest = after.Substring(a.Length).TrimStart(Separators.ToCharArray());
                            return e.id;
                        }
            }

        // 名字开头（长称呼优先，避免"小王"被"王"截断）
        Entry bestEntry = null;
        string bestAlias = null;
        foreach (Entry e in _entries)
            foreach (string a in e.aliases)
                if (s.StartsWith(a, StringComparison.Ordinal) && (bestAlias == null || a.Length > bestAlias.Length))
                {
                    bestEntry = e;
                    bestAlias = a;
                }
        if (bestEntry == null) return null;

        string tail = s.Substring(bestAlias.Length);
        bool honorific = false;
        foreach (string h in Honorifics)
            if (tail.StartsWith(h, StringComparison.Ordinal)) { tail = tail.Substring(h.Length); honorific = true; break; }

        bool called = tail.Length == 0
                      || Separators.IndexOf(tail[0]) >= 0
                      || honorific
                      || StartsWithAny(tail, VerbStarts);
        if (!called) return null; // "王芳的伤怎么样了"——是在说王芳，不是在叫王芳
        rest = tail.TrimStart(Separators.ToCharArray());
        return bestEntry.id;
    }

    static bool StartsWithAny(string s, string[] words)
    {
        foreach (string w in words) if (s.StartsWith(w, StringComparison.Ordinal)) return true;
        return false;
    }

    // 导出给 roster.json。
    public static object Export() => _entries;
}
