using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

// AI 词库的一个词条：游戏里的一样东西（物品 / 地点 / 可推的物体），以及玩家可能用的各种叫法。
[Serializable]
public class LexEntry
{
    public const string KindItem = "item";   // 仓库物品（id = 物品 id，如 "medkit"）
    public const string KindPlace = "place"; // 地点（id = 地点记号，如 "@camp"，或场景里的地点名）
    public const string KindBox = "box";     // 地图上可推的物体（id = 物体名，如 "木箱"）
    public const string KindLore = "lore";   // 世界观名词（ABA、亚空间、生存支援AI…）：只用于提示 AI，不参与指令解析

    public string id;
    public string kind;
    public string nameZh, nameJa;
    public List<string> aliases = new List<string>(); // 中日两种语言的叫法都放这里
    public string descZh, descJa;                      // 这是什么（需要时提示给 AI）

    [JsonIgnore] public string Name => LanguageGuard.IsJp ? nameJa : nameZh;
    [JsonIgnore] public string Desc => LanguageGuard.IsJp ? descJa : descZh;
}

// 在一段文字里找到的一个词条。
public struct LexHit
{
    public LexEntry entry;
    public int index;
    public int length;
}

// AI 词库（纯代码）：告诉"代码"和"AI"游戏里的各种东西是什么、玩家会怎么叫它。
//   · 指令解析：玩家说"去找急救包"，代码在词库里认出"急救包"= medkit，直接安排 NPC 去做（不靠 LLM 猜）。
//   · 提示 AI：玩家聊天时提到了哪些东西，就把这些词条的说明附进提示词，NPC 才知道急救包是干什么的。
//   · 回忆：判断两段话是不是在说同一样东西。
// 物品的名字和说明自动从 ItemDatabase 取；下面只需补"玩家常用的叫法"。开局导出为 lexicon.json。
public static class GameLexicon
{
    static readonly List<LexEntry> _entries = new List<LexEntry>();
    static readonly List<KeyValuePair<string, LexEntry>> _aliases = new List<KeyValuePair<string, LexEntry>>(); // 长的在前
    static bool _built;

    // 物品的额外叫法（物品本身的中日文名会自动加入）。加新物品时在这里补一行即可。
    static readonly Dictionary<string, string[]> ItemAliases = new Dictionary<string, string[]>
    {
        { "ration",    new[] { "营养块", "蛋白块", "口粮", "干粮", "栄養ブロック", "レーション" } },
        { "water",     new[] { "净化水", "饮用水", "水", "浄化水", "飲み水" } },
        { "sedative",  new[] { "镇静剂", "镇定剂", "安定剂", "鎮静剤", "鎮静薬" } },
        { "medkit",    new[] { "急救包", "医疗包", "急救箱", "医药箱", "药箱", "绷带", "救急キット", "救急箱", "医療キット", "包帯" } },
        { "stimulant", new[] { "兴奋剂", "强心针", "興奮剤" } },
        { "nano",      new[] { "纳米物质", "纳米", "ナノ物質", "ナノ" } },
        { "crystal",   new[] { "异变结晶", "结晶", "変異結晶", "結晶" } },
        // 地形资源
        { "wood",      new[] { "木材", "木头", "木料", "原木", "木柴", "柴火", "丸太", "薪" } },
        { "stone",     new[] { "石材", "石头", "石块", "石料", "石子", "岩石" } },
        { "ore",       new[] { "矿石", "亚空间矿石", "矿物", "原矿", "鉱石", "亜空間鉱石", "鉱物" } },
        { "fiber",     new[] { "植物纤维", "纤维", "草料", "植物繊維", "繊維" } },
        { "subspace_protein", new[] { "亚空间蛋白质", "蛋白质", "鱼肉", "亜空間タンパク質", "タンパク質", "魚肉" } },
    };

    // ============ 建库 ============

    public static void Rebuild(WorldState w)
    {
        _entries.Clear();

        // 1. 固定地点（记号由 CommandParser.TryGetLocationPos 换算成坐标）
        Add(new LexEntry
        {
            id = "@camp", kind = LexEntry.KindPlace, nameZh = "营地", nameJa = "拠点",
            aliases = { "营地", "基地", "据点", "营区", "大本营", "拠点", "キャンプ", "ベース" },
            descZh = "幸存者们落脚的地方，吃饭、休息都在这里。", descJa = "生存者たちの拠り所。食事も休息もここでする。"
        });
        Add(new LexEntry
        {
            id = "@storage", kind = LexEntry.KindPlace, nameZh = "仓库", nameJa = "倉庫",
            aliases = { "仓库", "储藏处", "物资堆", "倉庫", "物資置き場" },
            descZh = "营地的公共仓库，所有物资都放在这里。", descJa = "拠点の共用倉庫。物資はすべてここにある。"
        });
        Add(new LexEntry
        {
            id = "@terminal", kind = LexEntry.KindPlace, nameZh = "AI终端机", nameJa = "AI端末",
            aliases = { "AI终端机", "AI终端", "终端机", "终端", "AI端末", "端末", "ターミナル" },
            descZh = "调查局投放的 AI 终端，「那个声音」就是从这里传出来的。", descJa = "調査局が送り込んだAI端末。「あの声」はここから聞こえてくる。"
        });

        // 1.5 世界观名词（玩家提到时，把"普通市民所知道的"解释附进提示词）
        AddLore("@aba", "特异现象调查局", "特異現象調査局",
                new[] { "特异现象调查局", "调查局", "ABA", "特異現象調査局", "調査局" },
                "各国联合成立的机构，负责研究和应对空间漩涡；生存支援AI就是它投放进来的。普通市民只知道新闻里公开的部分。",
                "各国が共同で設立した機関で、空間の渦の研究と対応を担う。生存支援AIを送り込んだのもここ。一般市民はニュースで公表された部分しか知らない。");
        AddLore("@subspace", "亚空间", "亜空間",
                new[] { "亚空间", "亜空間" },
                "漩涡内部的异常领域：时间、空间、因果都不可靠，待得越久精神越会被侵蚀。",
                "渦の内側の異常な領域。時間・空間・因果が信用できず、長くいるほど精神がむしばまれる。");
        AddLore("@vortex", "空间漩涡", "空間の渦",
                new[] { "异次元漩涡", "空间漩涡", "漩涡", "異次元の渦", "空間の渦", "渦" },
                "突然出现在现代地球上的空间漩涡，会把附近的人卷进亚空间。",
                "現代の地球に突然現れる空間の渦。近くにいる人を亜空間へ呑み込む。");
        AddLore("@support_ai", "生存支援AI", "生存支援AI",
                new[] { "生存支援AI", "生存指导AI", "支援AI", "生存指導AI", "AI" },
                "ABA投放进亚空间的AI。没有身体，只能通过信号和幸存者说话，引导大家活下去、离开亚空间。就是正在和你说话的那个AI。",
                "ABAが亜空間へ送り込んだAI。体はなく、信号を通じてしか生存者と話せない。皆を生き延びさせ、亜空間から脱出させようとしている。今あなたと話しているAIのこと。");
        AddLore("@expedition", "先遣队", "先遣隊",
                new[] { "先遣队", "先遣隊" },
                "最早进入漩涡的ABA调查队。新闻只说他们失联了，真相没人知道。",
                "最初に渦へ入ったABAの調査隊。ニュースでは消息を絶ったとしか言われておらず、真相は誰も知らない。");
        AddLore("@creature", "造物", "造物",
                new[] { "造物", "怪物", "化け物" },
                "亚空间里无法名状的东西，光是看见就会让人精神受创；没人确定它们是真是幻。",
                "亜空間にいる名状しがたいもの。見るだけで精神に傷を負う。本物か幻か、確かめた者はいない。");
        AddLore("@storm", "负能量风暴", "負のエネルギーの嵐",
                new[] { "负能量风暴", "风暴", "負のエネルギーの嵐", "嵐" },
                "亚空间里弥漫的能量风暴，所过之处让人理智流失。",
                "亜空間に漂うエネルギーの嵐。通り過ぎた場所では正気が失われる。");
        AddLore("@whisper", "低语", "囁き",
                new[] { "低语", "幻听", "囁き", "幻聴" },
                "亚空间里会听见不存在的声音，往往和听的人自己的过去有关，模糊、听不真切。",
                "亜空間では存在しない声が聞こえることがある。たいてい聞く人自身の過去にまつわるもので、ぼんやりして聞き取れない。");

        // 2. 场景里摆的命名地点（LocationMarker）
        if (w != null && w.locations != null)
            foreach (LocationData loc in w.locations)
                if (!string.IsNullOrEmpty(loc.locName))
                    Add(new LexEntry { id = loc.locName, kind = LexEntry.KindPlace, nameZh = loc.locName, nameJa = loc.locName, aliases = { loc.locName } });

        // 3. 仓库物品（名字、说明来自 ItemDatabase）
        if (ItemDatabase.Instance != null)
            foreach (ItemData d in ItemDatabase.Instance.items)
            {
                if (d == null || string.IsNullOrEmpty(d.id)) continue;
                LexEntry e = new LexEntry
                {
                    id = d.id, kind = LexEntry.KindItem, nameZh = d.nameZh, nameJa = d.nameJa,
                    descZh = d.descZh, descJa = d.descJa
                };
                e.aliases.Add(d.nameZh);
                e.aliases.Add(d.nameJa);
                if (ItemAliases.TryGetValue(d.id, out string[] extra)) e.aliases.AddRange(extra);
                Add(e);
            }

        // 4. 地图上可推的物体（WorldItemMarker）
        if (w != null && w.worldItems != null)
            foreach (WorldItem it in w.worldItems)
            {
                if (it == null || string.IsNullOrEmpty(it.itemName) || Get(it.itemName) != null) continue;
                LexEntry e = new LexEntry
                {
                    id = it.itemName, kind = LexEntry.KindBox, nameZh = it.itemName, nameJa = it.itemName,
                    descZh = "地图上可以推动的物体。", descJa = "マップ上にある、押して動かせる物。"
                };
                e.aliases.Add(it.itemName);
                if (it.itemType == "box") { e.aliases.Add("箱子"); e.aliases.Add("箱"); }
                Add(e);
            }

        // 叫法索引：长的在前（"急救箱"优先于"箱"），重复的叫法只留第一个词条。
        _aliases.Clear();
        HashSet<string> seen = new HashSet<string>();
        foreach (LexEntry e in _entries)
            foreach (string a in e.aliases)
                if (!string.IsNullOrEmpty(a) && seen.Add(a)) _aliases.Add(new KeyValuePair<string, LexEntry>(a, e));
        _aliases.Sort((x, y) => y.Key.Length.CompareTo(x.Key.Length));
        _built = true;
    }

    static void AddLore(string id, string zh, string ja, string[] aliases, string descZh, string descJa)
    {
        LexEntry e = new LexEntry { id = id, kind = LexEntry.KindLore, nameZh = zh, nameJa = ja, descZh = descZh, descJa = descJa };
        e.aliases.AddRange(aliases);
        Add(e);
    }

    static void Add(LexEntry e)
    {
        e.aliases.RemoveAll(string.IsNullOrEmpty);
        _entries.Add(e);
    }

    static void EnsureBuilt()
    {
        if (!_built) Rebuild(WorldManager.Instance != null ? WorldManager.Instance.world : null);
    }

    // ============ 查询 ============

    public static LexEntry Get(string id)
    {
        foreach (LexEntry e in _entries) if (e.id == id) return e;
        return null;
    }

    // 显示名（当前语言）。找不到词条就原样返回 id。
    public static string NameOf(string id)
    {
        EnsureBuilt();
        LexEntry e = Get(id);
        return e != null && !string.IsNullOrEmpty(e.Name) ? e.Name : id;
    }

    // 找出文字里提到的所有词条（按出现位置排序；长叫法优先，互不重叠）。
    public static List<LexHit> FindAll(string text)
    {
        EnsureBuilt();
        List<LexHit> hits = new List<LexHit>();
        if (string.IsNullOrEmpty(text)) return hits;
        bool[] taken = new bool[text.Length];
        foreach (KeyValuePair<string, LexEntry> kv in _aliases)
        {
            int start = 0;
            while (start < text.Length)
            {
                int i = text.IndexOf(kv.Key, start, StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                bool free = true;
                for (int j = i; j < i + kv.Key.Length; j++) if (taken[j]) { free = false; break; }
                if (free)
                {
                    for (int j = i; j < i + kv.Key.Length; j++) taken[j] = true;
                    hits.Add(new LexHit { entry = kv.Value, index = i, length = kv.Key.Length });
                }
                start = i + kv.Key.Length;
            }
        }
        hits.Sort((x, y) => x.index.CompareTo(y.index));
        return hits;
    }

    // 文字里提到的第一个某类词条（没有返回 null）。
    public static LexEntry FindFirst(string text, string kind)
    {
        foreach (LexHit h in FindAll(text)) if (h.entry.kind == kind) return h.entry;
        return null;
    }

    // 文字里提到的词条 id 集合（回忆时比较用）。
    public static HashSet<string> FindIds(string text)
    {
        HashSet<string> ids = new HashSet<string>();
        foreach (LexHit h in FindAll(text)) ids.Add(h.entry.id);
        return ids;
    }

    // 给 AI 的词条提示：文字里提到的东西是什么（当前语言，最多 max 条）。没提到任何东西返回空串。
    public static string Hints(string text, int max = 4)
    {
        StringBuilder sb = new StringBuilder();
        HashSet<string> done = new HashSet<string>();
        foreach (LexHit h in FindAll(text))
        {
            if (done.Count >= max) break;
            if (!done.Add(h.entry.id) || string.IsNullOrEmpty(h.entry.Desc)) continue;
            sb.AppendLine("・" + h.entry.Name + "：" + h.entry.Desc);
        }
        return sb.ToString();
    }

    // 导出给 lexicon.json。
    public static object Export()
    {
        EnsureBuilt();
        return _entries;
    }
}
