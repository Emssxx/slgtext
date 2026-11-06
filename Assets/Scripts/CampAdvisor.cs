using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Newtonsoft.Json.Linq;

// ============================================================================
// 营地参谋（隐形，4B 小模型）："代码算数 → 4B 按规则判断 → 14B 演绎 → 代码执行"。
//   1. 代码每 checkInterval 秒（默认 5 秒）把营地整理成一张【局势表】：净化水还够几天、收集器 / 净化器、食物、电、
//      材料、围墙、伤病、天气、最近的事、现在的分工……要算的数字全由代码算好（4B 不开思考时算数不可靠）。
//   2. 局势有变化（askOnlyWhenChanged）就把【规则手册】+ 局势表交给 4B。规则手册只讲"游戏是怎么运作的"，
//      不给现成答案（Resources/AI/advisor_rules.txt，策划可以直接改，不用动代码）。
//      4B 自己推出营地眼下最要紧的 1~3 件事，对策只能从固定清单里选：
//        build 建什么 / gather 采什么 / haul_water 打水 / scavenge 找吃的和药 / care 照顾伤病 / rest 休息 / ask_ai 只有 AI 能做的事
//   3. 代码审核：没解锁、不存在、眼下做不到的建议直接丢掉。4B 没装 / 出错 / 关掉 useLiteModel → 代码按简单规则出一份（Fallback）。
//   4. 结果不给玩家看（隐形参谋），直接写进 14B 的提示词，说成"营地里的人心里大多有数的事"：
//        · NPC 自己想事情、和 AI 说话（PromptContext.WorldSummary）
//        · 领导分工（NpcSociety.PlanPrompt）：参谋的看法变了 / 出现新的紧急情况，领导会重新安排分工、可以定一个"建造项目"
//        · 还没有领导时：出现新的紧急情况，没分工的人会自己想办法（NpcIntentBrain）
//   5. 调试：F9 调度器面板最下面是参谋最近一次的判断；勾上 logToConsole 会把局势表和 4B 的原文打到 Console。
// 由 NpcBehaviorDriver.Start 自动挂上。
// ============================================================================
public class CampAdvisor : MonoBehaviour
{
    public static CampAdvisor Instance { get; private set; }

    [Header("开关 / 节奏")]
    public bool advisorEnabled = true;
    [Tooltip("多久检测一次局势（秒，真实时间）")]
    public float checkInterval = 5f;
    [Tooltip("局势没变就不重复问 4B（省得和 14B 抢显卡）。关掉 = 每次检测都问一次")]
    public bool askOnlyWhenChanged = true;
    [Tooltip("关掉 = 不用 4B，只用代码规则出建议（想对比 4B 的判断时用）")]
    public bool useLiteModel = true;
    [Tooltip("规则手册：Resources 里的文本文件（不写扩展名）。以 # 开头的行不会发给模型")]
    public string rulesResource = "AI/advisor_rules";
    [Tooltip("把每次的局势表和 4B 的原文打到 Console（调规则手册时用）")]
    public bool logToConsole = false;

    [Header("紧急程度的线（真实秒数）")]
    [Tooltip("净化水只够喝这么多秒 = 紧急。按秒不按天：NPC 去打水、建收集器要花的时间是按秒算的，跟一天几秒无关")]
    public float waterUrgentSeconds = 60f;
    public float waterLowSeconds = 180f;
    public float foodUrgentSeconds = 60f;
    public float foodLowSeconds = 180f;

    // 换算成"天"（局势表、搬运都用）。现在一天 = NpcHealth.secondsPerDay 秒（= AIDirector.secondsPerTurn）。
    public static float WaterUrgentDays => (Instance != null ? Instance.waterUrgentSeconds : 60f) / SecondsPerDay;
    public static float WaterLowDays => (Instance != null ? Instance.waterLowSeconds : 180f) / SecondsPerDay;
    static float FoodUrgentDays => (Instance != null ? Instance.foodUrgentSeconds : 60f) / SecondsPerDay;
    static float FoodLowDays => (Instance != null ? Instance.foodLowSeconds : 180f) / SecondsPerDay;
    static float SecondsPerDay => NpcHealth.Instance != null && NpcHealth.Instance.secondsPerDay > 0.01f ? NpcHealth.Instance.secondsPerDay : 20f;

    // 一条建议
    public class Advice
    {
        public string need;    // water / food / shelter / health / power / material / rest
        public int level;      // 3 = 马上 / 2 = 今天之内 / 1 = 有空再做
        public string act;     // build / gather / haul_water / scavenge / care / rest / ask_ai
        public string target;  // 建筑 id / 材料 id（可空）
        public string note;    // 一句理由（只给 F9 看）
        public string Key => need + "|" + act + "|" + target;
    }

    static readonly string[] Needs = { "water", "food", "shelter", "health", "power", "material", "rest" };
    static readonly string[] Acts = { "build", "gather", "haul_water", "scavenge", "care", "rest", "ask_ai" };
    static readonly string[] Materials = { ResourceInfo.WoodItem, ResourceInfo.StoneItem, ResourceInfo.OreItem, ResourceInfo.FiberItem };

    static readonly List<Advice> _current = new List<Advice>();
    public static IReadOnlyList<Advice> Current => _current;     // 按紧急程度排好
    public static string Situation { get; private set; } = "";   // 4B 对局势的一句话判断（调试用）
    public static string Source { get; private set; } = "";      // "4B" / "code"
    public static int Version { get; private set; }              // 建议变了（哪几条、各多急）就 +1
    public static int UrgentVersion { get; private set; }        // 出现新的紧急建议（level 3）就 +1
    public static float UpdatedAt { get; private set; } = -999f;
    public static bool HasRun { get; private set; }

    float _timer = 2f;
    string _lastSig;
    string _rules;
    int _seq;            // 每问一次 +1：旧的结果回来时对不上就不用
    int _calls, _fails;
    WorldState _world;   // 换了一局就重置

    public static CampAdvisor Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("CampAdvisor").AddComponent<CampAdvisor>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void ResetAll()
    {
        _current.Clear();
        Situation = "";
        Source = "";
        HasRun = false;
        _lastSig = null;
        _seq++;
        Version++;
        UpdatedAt = -999f;
    }

    // ============ 每 5 秒检测 ============

    void Update()
    {
        if (!advisorEnabled) return;
        WorldState w = World;
        if (w == null) return;
        if (w != _world) { _world = w; ResetAll(); }
        if (Time.timeScale <= 0f) return;            // 暂停（建造模式等）时不检测
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = Mathf.Max(1f, checkInterval);
        Check(w);
    }

    void Check(WorldState w)
    {
        Snap s = Take(w);
        if (s.alive == 0) return;
        string sig = s.Signature();
        if (askOnlyWhenChanged && sig == _lastSig) return;
        _lastSig = sig;
        if (!useLiteModel || !Ask(s)) Apply(Fallback(s), "code", "");
    }

    // ============ 问 4B ============

    bool Ask(Snap s)
    {
        string facts = s.Facts();
        int seq = ++_seq;
        bool queued = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.Advisor, key = "camp", json = true, maxTokens = 260, replacePending = true,
            system = SystemPrompt,
            user = () => facts + "\n现在给出你的判断。",
            onResult = raw =>
            {
                if (seq != _seq) return;                       // 已经有更新的局势在问了（被替换的旧请求也会回 null）
                if (logToConsole) Debug.Log("[营地参谋] 局势表：\n" + facts + "\n4B 原文：" + (raw ?? "（没有回应）"));
                List<Advice> list = Parse(raw, out string situation);
                if (list == null) { _fails++; Apply(Fallback(Take(World)), "code", ""); return; }
                Apply(list, "4B", situation);
            },
        });
        if (queued) _calls++;
        return queued;
    }

    string SystemPrompt()
    {
        if (_rules == null) _rules = LoadRules();
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("你是营地的生存参谋。你不出场，只在背后替营地判断局势。");
        sb.AppendLine("下面是这个世界的【规则手册】。用户会给你营地此刻的【局势表】，数字都已经算好了，直接用，不要自己重新算。");
        sb.AppendLine("按规则手册推理：营地眼下最要紧的是哪 1~3 件事，各自该怎么办。");
        sb.AppendLine();
        sb.AppendLine(_rules);
        sb.AppendLine();
        sb.AppendLine("【你能给的对策】act 只能填下面之一：");
        sb.AppendLine("- build：建造。target 填局势表【能建的建筑】里的英文 id");
        sb.AppendLine("- gather：采集材料。target 填 wood / stone / ore / fiber");
        sb.AppendLine("- haul_water：去湖边打水，倒进雨水收集器（要先有收集器）");
        sb.AppendLine("- scavenge：出营地找吃的、药和物资");
        sb.AppendLine("- care：照顾受伤 / 生病的人（要有药）");
        sb.AppendLine("- rest：让太累的人休息");
        sb.AppendLine("- ask_ai：只有 AI 能做的事（接电线、调设备档位、升级终端机、研究科技）。target 填相关建筑的英文 id，没有就留空");
        sb.AppendLine("【need】water（水）/ food（食物）/ shelter（围墙和安全）/ health（伤病）/ power（电）/ material（材料）/ rest（体力）");
        sb.AppendLine("【level】3 = 再不处理就会有人渴死、饿死、伤重而死，或马上断水断粮；2 = 今天之内要处理；1 = 有空再做");
        sb.AppendLine();
        sb.AppendLine("先用一句话写你对局势的判断（situation，30 字以内），再给建议。最多 3 条，最要紧的放前面；营地没什么问题就给空数组。");
        sb.AppendLine("只输出一个 JSON 对象，不要任何多余文字：");
        sb.AppendLine("{\"situation\":\"……\",\"advice\":[{\"need\":\"water\",\"level\":3,\"act\":\"haul_water\",\"target\":\"\",\"note\":\"为什么（15 字以内）\"}]}");
        return sb.ToString();
    }

    string LoadRules()
    {
        TextAsset ta = Resources.Load<TextAsset>(rulesResource);
        if (ta == null || string.IsNullOrEmpty(ta.text))
        {
            Debug.LogWarning("[营地参谋] 找不到规则手册 Resources/" + rulesResource + ".txt，先用内置的简略版。");
            return FallbackRules;
        }
        StringBuilder sb = new StringBuilder();
        foreach (string line in ta.text.Replace("\r", "").Split('\n'))
            if (!line.TrimStart().StartsWith("#")) sb.AppendLine(line);
        return sb.ToString().Trim();
    }

    // 规则手册文件丢了时的简略版（正式内容在 Resources/AI/advisor_rules.txt）
    const string FallbackRules =
        "【规则（简略）】\n" +
        "· 能喝的只有仓库里的净化水。来源：下雨 / 湖水 → 雨水收集器 → 水源净化器（要接电）→ 净化水。没水喝会口渴、掉生命。\n" +
        "· 食物和药靠出营地搜寻。没食物会挨饿、掉生命。受伤 / 生病的人不能工作，要用药照顾。\n" +
        "· 木材、石头、矿石、纤维靠采集；建造会消耗材料。营地被围墙围起来大家才安心。\n" +
        "· 接电线、调档位、研究科技只有 AI 能做。";

    // 解析 + 审核。不合格返回 null（整份不能用）；合格但一条都没有 = 营地没问题。
    List<Advice> Parse(string raw, out string situation)
    {
        situation = "";
        if (string.IsNullOrEmpty(raw)) return null;
        JObject o;
        try
        {
            int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
            if (a < 0 || b <= a) return null;
            o = JObject.Parse(raw.Substring(a, b - a + 1));
        }
        catch { return null; }
        JArray arr = o["advice"] as JArray;
        if (arr == null) return null;
        situation = Clip((o.Value<string>("situation") ?? "").Trim(), 40);

        List<Advice> list = new List<Advice>();
        foreach (JToken t in arr)
        {
            JObject j = t as JObject;
            if (j == null) continue;
            int level = 1;
            JToken lv = j["level"];
            if (lv != null) int.TryParse(lv.ToString(), out level);
            Advice ad = new Advice
            {
                need = Norm(j.Value<string>("need")),
                level = Mathf.Clamp(level, 1, 3),
                act = Norm(j.Value<string>("act")),
                target = Norm(j.Value<string>("target")),
                note = Clip((j.Value<string>("note") ?? "").Trim(), 24),
            };
            if (Valid(ad)) list.Add(ad);
        }
        return list;
    }

    // 这条建议眼下做得到吗。
    static bool Valid(Advice a)
    {
        if (Array.IndexOf(Needs, a.need) < 0 || Array.IndexOf(Acts, a.act) < 0) return false;
        switch (a.act)
        {
            case "build":      return Candidate(a.target) != null;
            case "gather":     return Array.IndexOf(Materials, a.target) >= 0;
            case "haul_water": a.target = ""; return WaterWorks.CollectorCount > 0;
            case "ask_ai":     if (Candidate(a.target) == null) a.target = ""; return true;
            default:           a.target = ""; return true;
        }
    }

    // 收下一份建议：排好、去重、最多 3 条；内容变了才 +Version，出现新的紧急建议 +UrgentVersion。
    void Apply(List<Advice> list, string source, string situation)
    {
        List<Advice> clean = new List<Advice>();
        HashSet<string> seen = new HashSet<string>();
        list.Sort((x, y) => y.level.CompareTo(x.level));
        foreach (Advice a in list)
        {
            if (a == null || clean.Count >= 3 || !seen.Add(a.Key)) continue;
            clean.Add(a);
        }

        string before = Fingerprint(_current, false), after = Fingerprint(clean, false);
        HashSet<string> oldUrgent = new HashSet<string>();
        foreach (Advice a in _current) if (a.level >= 3) oldUrgent.Add(a.Key);
        bool newUrgent = false;
        foreach (Advice a in clean) if (a.level >= 3 && !oldUrgent.Contains(a.Key)) newUrgent = true;

        _current.Clear();
        _current.AddRange(clean);
        Situation = situation ?? "";
        Source = source;
        UpdatedAt = Time.time;
        HasRun = true;
        if (before != after) Version++;
        if (newUrgent) UrgentVersion++;
    }

    static string Fingerprint(List<Advice> list, bool withNote)
    {
        StringBuilder sb = new StringBuilder();
        foreach (Advice a in list) sb.Append(a.Key).Append('#').Append(a.level).Append(withNote ? a.note : "").Append(';');
        return sb.ToString();
    }

    // ============ 代码规则（4B 用不了时）============

    List<Advice> Fallback(Snap s)
    {
        List<Advice> list = new List<Advice>();
        if (s == null || s.alive == 0) return list;
        string collector = PowerBuildings.CollectorId, purifier = PowerBuildings.PurifierId, turbine = PowerBuildings.TurbineId;

        // 水：短期缺水（还够几天）+ 长期缺水（没有收集器 / 净化器 / 净化器没电）
        int wl = s.waterDays < s.waterUrgent ? 3 : s.waterDays < s.waterLow ? 2 : 0;
        if (s.collectors == 0 && Candidate(collector) != null) Add(list, "water", Mathf.Max(wl, 2), "build", collector, "没有收集器");
        else if (s.purifiers == 0 && Candidate(purifier) != null) Add(list, "water", Mathf.Max(wl, 2), "build", purifier, "没有净化器");
        else if (s.purifiers > 0 && !s.purifierLinked) Add(list, "water", Mathf.Max(wl, 2), "ask_ai", purifier, "净化器没接电");
        else if (wl > 0 && s.collectors > 0 && s.raw < s.rawCap && !s.raining) Add(list, "water", wl, "haul_water", "", "不下雨，去打水");

        // 伤病
        if (s.critical + s.dying > 0 && s.medicine > 0) Add(list, "health", 3, "care", "", "有人伤得很重");
        else if (s.injured + s.sick + s.critical + s.dying > 0) Add(list, "health", 2, s.medicine > 0 ? "care" : "scavenge", "", s.medicine > 0 ? "有人受伤" : "有伤员但没有药");

        // 食物
        int fl = s.foodDays < s.foodUrgent ? 3 : s.foodDays < s.foodLow ? 2 : 0;
        if (fl > 0) Add(list, "food", fl, "scavenge", "", "食物不多了");

        // 安全
        if (s.wallsNeeded) Add(list, "shelter", NpcSecurity.Danger >= 8f || s.insecure * 2 >= s.alive ? 2 : 1, "build", "wall", "营地还没围好");

        // 电
        if (s.shed > 0)
        {
            if (Candidate(turbine) != null) Add(list, "power", 2, "build", turbine, "电不够用");
            else Add(list, "power", 2, "ask_ai", "", "电不够用");
        }

        // 体力
        if (s.fatigued * 2 >= s.alive) Add(list, "rest", 2, "rest", "", "很多人累了");
        return list;
    }

    static void Add(List<Advice> list, string need, int level, string act, string target, string note)
    {
        Advice a = new Advice { need = need, level = level, act = act, target = target ?? "", note = note };
        if (Valid(a)) list.Add(a);
    }

    // ============ 给 14B 的提示词（说成"营地里的人心里大多有数的事"，不提参谋）============

    public static string PromptBlock(bool jp)
    {
        if (!HasRun || Instance == null || !Instance.advisorEnabled) return "";
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(jp ? "【拠点で今いちばん急ぐこと】（拠点のみんなはだいたい分かっている）" : "【营地眼下最要紧的事】（营地里的人心里大多有数）");
        if (_current.Count == 0)
        {
            sb.AppendLine(jp ? "・今のところ、特に急ぐことはない。" : "・眼下没有特别急的事。");
            return sb.ToString();
        }
        foreach (Advice a in _current)
            sb.Append("・").Append(LevelText(a.level, jp)).Append("｜").Append(NeedText(a.need, jp)).Append(jp ? "：" : "：")
              .Append(NeedFact(a.need, jp)).Append(" → ").AppendLine(ActText(a, jp));
        return sb.ToString();
    }

    // 有没有"大家能动手解决"的紧急情况（ask_ai 那种只有 AI 能做的不算）。
    public static bool HasActionableUrgent
    {
        get
        {
            foreach (Advice a in _current) if (a.level >= 3 && a.act != "ask_ai") return true;
            return false;
        }
    }

    // 最要紧的那条"建什么"（领导没定项目时，代码分工用）。
    public static Advice TopBuild(int minLevel)
    {
        foreach (Advice a in _current) if (a.act == "build" && a.level >= minLevel && a.target != "wall") return a;
        return null;
    }

    public static Advice Top(string act, int minLevel)
    {
        foreach (Advice a in _current) if (a.act == act && a.level >= minLevel) return a;
        return null;
    }

    static string LevelText(int level, bool jp)
    {
        if (level >= 3) return jp ? "急ぎ" : "紧急";
        if (level == 2) return jp ? "今日中" : "今天";
        return jp ? "余裕があれば" : "有空";
    }

    static string NeedText(string need, bool jp)
    {
        switch (need)
        {
            case "water":    return jp ? "水" : "水";
            case "food":     return jp ? "食料" : "食物";
            case "shelter":  return jp ? "安全" : "安全";
            case "health":   return jp ? "けが・病気" : "伤病";
            case "power":    return jp ? "電気" : "电";
            case "material": return jp ? "資材" : "材料";
            case "rest":     return jp ? "体力" : "体力";
            default:         return need;
        }
    }

    // 这方面的实际数字（写提示词时现算，保证是最新的）。
    static string NeedFact(string need, bool jp)
    {
        WorldState w = World;
        switch (need)
        {
            case "water":
            {
                int stored = WaterWorks.StoredWater, c = WaterWorks.CollectorCount, p = WaterWorks.PurifierCount;
                string days = DaysPhrase(WaterWorks.DaysLeft, jp);
                string linked = p > 0 && !PurifierLinked() ? (jp ? "・電源につながっていない" : "，没接上电") : "";
                return jp ? $"浄化水は残り{stored}、{days}（雨水タンク{c}基・浄水器{p}台{linked}）"
                          : $"净化水还剩 {stored} 份，{days}（雨水收集器 {c} 台、净化器 {p} 台{linked}）";
            }
            case "food":
            {
                int food = w != null && ItemDatabase.Instance != null ? ItemDatabase.Instance.CountInStorage(ItemCategory.Food, w) : 0;
                string days = DaysPhrase(FoodDays(w), jp);
                return jp ? $"食料は{food}食分、{days}" : $"食物 {food} 份，{days}";
            }
            case "shelter":
            {
                CampZone z = CampZone.Instance;
                if (z == null || !z.Ready) return jp ? "拠点のまわりがまだ無防備" : "营地四周还敞着";
                if (z.Enclosed) return jp ? "拠点はもう囲まれている" : "营地已经围起来了";
                return jp ? $"拠点の壁は{Mathf.RoundToInt(z.Closure * 100f)}%完成" : $"营地围墙完成 {Mathf.RoundToInt(z.Closure * 100f)}%";
            }
            case "health":
            {
                int hurt = 0;
                if (w != null)
                    foreach (NpcData n in w.npcs)
                        if (n != null && !n.IsDead && (NpcHealth.Has(n, HealthTag.Injured) || NpcHealth.Has(n, HealthTag.Sick) || NpcHealth.Has(n, HealthTag.CriticalWound) || NpcHealth.Has(n, HealthTag.Dying))) hurt++;
                int med = w != null && ItemDatabase.Instance != null ? ItemDatabase.Instance.CountInStorage(ItemCategory.Medical, w) : 0;
                return jp ? $"けが人・病人{hurt}人、薬{med}個" : $"{hurt} 人有伤病，药 {med} 份";
            }
            case "power":
            {
                PowerGrid.Summary ps = PowerGrid.Total();
                string extra = ps.shed > 0 ? (jp ? "・電気が足りず止まっている設備がある" : "，有设备因为电不够停了") : "";
                if (ps.unlinkedDevices > 0) extra += jp ? "・電源につながっていない設備がある" : "，有设备没接上电";
                return jp ? $"蓄電 {ps.stored:0.#}/{ps.capacity:0.#}{extra}" : $"储电 {ps.stored:0.#}/{ps.capacity:0.#}{extra}";
            }
            case "material":
                return (jp ? "倉庫：" : "仓库：") + ItemName(ResourceInfo.WoodItem) + Have(ResourceInfo.WoodItem) + "、" + ItemName(ResourceInfo.StoneItem) + Have(ResourceInfo.StoneItem)
                       + "、" + ItemName(ResourceInfo.OreItem) + Have(ResourceInfo.OreItem) + "、" + ItemName(ResourceInfo.FiberItem) + Have(ResourceInfo.FiberItem);
            case "rest":
            {
                int tired = 0;
                if (w != null) foreach (NpcData n in w.npcs) if (n != null && !n.IsDead && NpcHealth.Has(n, HealthTag.Fatigued)) tired++;
                return jp ? $"{tired}人がへとへと" : $"{tired} 人很疲惫";
            }
            default: return "";
        }
    }

    static string ActText(Advice a, bool jp)
    {
        switch (a.act)
        {
            case "build":
            {
                BuildableData d = Candidate(a.target);
                if (d == null) return "";
                MaterialCost[] cost = NpcIntentBrain.CostOf(d);
                string missing = MissingText(cost, jp);
                return (jp ? d.LocalizedName + "を建てる（" : "建" + d.LocalizedName + "（") + CostText(cost, jp)
                       + (missing.Length == 0 ? (jp ? "／倉庫にある）" : "；仓库够）") : (jp ? "／" : "；") + missing + (jp ? "）" : "）"));
            }
            case "gather":     return jp ? ItemName(a.target) + "を採る" : "去采集" + ItemName(a.target);
            case "haul_water": return jp ? "湖で水を汲み、雨水タンクへ運ぶ" : "去湖边打水，倒进雨水收集器";
            case "scavenge":   return jp ? "拠点の外で食べ物や薬を探す" : "出营地找吃的和药";
            case "care":       return jp ? "けが人・病人の世話をする" : "照顾伤病的人";
            case "rest":       return jp ? "疲れた人を休ませる" : "让累的人歇一歇";
            case "ask_ai":
            {
                BuildableData d = Candidate(a.target);
                string what = d != null ? (jp ? "（" + d.LocalizedName + "）" : "（" + d.LocalizedName + "）") : "";
                return jp ? "自分たちではどうにもならない。AIに頼むしかない" + what : "自己解决不了，得请 AI 帮忙" + what;
            }
            default: return a.act;
        }
    }

    // ============ F9 调试面板 ============

    public static string DebugText()
    {
        CampAdvisor me = Instance;
        if (me == null) return "";
        StringBuilder sb = new StringBuilder();
        string age = HasRun ? Mathf.RoundToInt(Time.time - UpdatedAt) + "s ago" : "-";
        sb.Append("<color=#B8FFC7>ADVISOR</color>  ").Append(me.advisorEnabled ? (me.useLiteModel ? "4b" : "code only") : "off")
          .Append("  v").Append(Version).Append("  ").Append(Source.Length > 0 ? Source : "-").Append("  ").Append(age)
          .Append("  ask ").Append(me._calls).Append(me._fails > 0 ? "  <color=#FF6A60>bad " + me._fails + "</color>" : "").Append('\n');
        if (Situation.Length > 0) sb.Append("<color=#8E9490>").Append(Clip(Situation, 26)).Append("</color>\n");
        if (HasRun && _current.Count == 0) sb.Append("<color=#8E9490>(no issue)</color>\n");
        foreach (Advice a in _current)
        {
            string head = "L" + a.level + " " + a.need + " " + a.act + (a.target.Length > 0 ? " " + a.target : "");
            sb.Append(a.level >= 3 ? "<color=#FFE36A>" : "").Append(Clip(head, 34)).Append(a.level >= 3 ? "</color>" : "");
            if (!string.IsNullOrEmpty(a.note)) sb.Append("  <color=#8E9490>").Append(Clip(a.note, 12)).Append("</color>");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // ============ 局势表（代码算好的数字）============

    class Snap
    {
        public int day, alive, dead, injured, critical, dying, sick, thirsty, hungry, fatigued, insecure, broken;
        public int stored, collectors, raw, rawCap, purifiers, purified;
        public bool purifierLinked, raining;
        public float demand, waterDays;
        public int food, medicine;
        public float foodDays;
        public int turbines, batteries, shed, unlinked;
        public float pStored, pCap, pProduced, pDemand;
        public int wood, stone, ore, fiber;
        public bool zoneReady, enclosed, wallUnlocked, wallsNeeded;
        public int slots, closed, planned, gates, open;
        public float closure, danger, anomaly;
        public string timeOfDay = "", weather = "", lastEvent = "", leader = "";
        public readonly SortedDictionary<string, int> roles = new SortedDictionary<string, int>();
        public readonly SortedDictionary<string, int> sites = new SortedDictionary<string, int>();
        public readonly List<BuildableData> cands = new List<BuildableData>();
        public float waterUrgent, waterLow, foodUrgent, foodLow;

        // 拿来判断"局势变没变"：只看会改变判断的东西（分档），不看每秒都在变的细数。
        public string Signature()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Band(waterDays, waterUrgent, waterLow)).Append(collectors).Append(',').Append(purifiers).Append(purifierLinked ? 1 : 0)
              .Append(raw > 0 ? 1 : 0).Append(rawCap > 0 && raw >= rawCap ? 1 : 0).Append(raining ? 1 : 0).Append('|')
              .Append(Band(foodDays, foodUrgent, foodLow)).Append(medicine > 0 ? 1 : 0).Append('|')
              .Append(alive).Append(',').Append(injured + sick).Append(',').Append(critical + dying).Append(',').Append(thirsty > 0 ? 1 : 0)
              .Append(hungry > 0 ? 1 : 0).Append(fatigued * 2 >= alive ? 1 : 0).Append(insecure * 2 >= alive ? 1 : 0).Append('|')
              .Append(zoneReady ? 1 : 0).Append(enclosed ? 1 : 0).Append(wallUnlocked ? 1 : 0).Append(Mathf.FloorToInt(closure * 4f)).Append('|')
              .Append(MatBand(wood)).Append(MatBand(stone)).Append(MatBand(ore)).Append(MatBand(fiber)).Append('|')
              .Append(turbines).Append(',').Append(batteries).Append(shed > 0 ? 1 : 0).Append(unlinked > 0 ? 1 : 0).Append('|')
              .Append(danger >= 8f ? 1 : 0).Append(Mathf.FloorToInt(anomaly / 25f)).Append('|').Append(leader.Length > 0 ? 1 : 0).Append('|');
            foreach (KeyValuePair<string, int> kv in sites) sb.Append(kv.Key).Append(kv.Value);
            sb.Append('|');
            foreach (BuildableData d in cands) sb.Append(d.id).Append(Affordable(d) ? '+' : '-');
            return sb.ToString();
        }

        static int Band(float days, float urgent, float low) => days < urgent ? 0 : days < low ? 1 : 2;
        static int MatBand(int n) => n <= 0 ? 0 : n < 5 ? 1 : n < 10 ? 2 : 3;

        public string Facts()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("【局势表】第 ").Append(day).Append(" 天").Append(timeOfDay.Length > 0 ? "，" + timeOfDay : "").AppendLine();
            if (weather.Length > 0) sb.AppendLine(weather);
            sb.Append("・人：活着 ").Append(alive).Append(" 人").Append(dead > 0 ? "（已死 " + dead + " 人）" : "").Append("。受伤 ").Append(injured)
              .Append("、致命伤 ").Append(critical).Append("、濒死 ").Append(dying).Append("、生病 ").Append(sick)
              .Append("；口渴 ").Append(thirsty).Append("、饥饿 ").Append(hungry).Append("、疲惫 ").Append(fatigued)
              .Append("；安全感不足 ").Append(insecure).Append(broken > 0 ? "；精神崩溃 " + broken : "").AppendLine();
            if (leader.Length > 0)
            {
                sb.Append("・分工：领导 ").Append(leader);
                foreach (KeyValuePair<string, int> kv in roles) sb.Append("；").Append(RoleZh(kv.Key)).Append(' ').Append(kv.Value).Append(" 人");
                sb.AppendLine();
            }
            else sb.AppendLine("・分工：还没有领导，大家各干各的");
            sb.Append("・水：仓库净化水 ").Append(stored).Append(" 份，全营地每天约喝 ").Append(demand.ToString("0.0")).Append(" 份 → ")
              .Append(DaysPhrase(waterDays, false)).Append("【").Append(Label(waterDays, waterUrgent, waterLow)).AppendLine("】");
            sb.Append("　　雨水收集器 ").Append(collectors).Append(" 台（存雨水 ").Append(raw).Append('/').Append(rawCap).Append("）；水源净化器 ").Append(purifiers).Append(" 台")
              .Append(purifiers > 0 ? (purifierLinked ? "（已接上电源）" : "（没接上电源，不能工作）") : "")
              .Append("；上回合净化 ").Append(purified).Append(" 份；").AppendLine(raining ? "正在下雨 / 雪" : "现在没有下雨");
            sb.Append("・食物：").Append(food).Append(" 份 → ").Append(DaysPhrase(foodDays, false)).Append("【").Append(Label(foodDays, foodUrgent, foodLow))
              .Append("】；药品 ").Append(medicine).AppendLine(" 份");
            sb.Append("・电：风力发电机 ").Append(turbines).Append(" 台、蓄电池 ").Append(batteries).Append(" 座；储电 ").Append(pStored.ToString("0.#")).Append('/').Append(pCap.ToString("0.#"))
              .Append("；本回合发电 ").Append(pProduced.ToString("0.#")).Append("、用电 ").Append(pDemand.ToString("0.#"))
              .Append(shed > 0 ? "；有 " + shed + " 台设备因为电不够停了" : "").Append(unlinked > 0 ? "；有 " + unlinked + " 台设备没接上电源" : "").AppendLine();
            sb.Append("・材料（仓库）：木材 ").Append(wood).Append("、石头 ").Append(stone).Append("、矿石 ").Append(ore).Append("、纤维 ").Append(fiber).AppendLine();
            sb.Append("・围墙：");
            if (!zoneReady) sb.AppendLine("营地范围还没确定");
            else if (enclosed) sb.AppendLine("营地已经整个围起来了");
            else
                sb.Append("营地一圈 ").Append(slots).Append(" 段墙位（含出入口 ").Append(gates).Append(" 个），已建成 ").Append(closed).Append(" 段、蓝图 ").Append(planned)
                  .Append(" 段、还空着 ").Append(open).Append(" 段 → 完成 ").Append(Mathf.RoundToInt(closure * 100f)).Append('%').AppendLine(wallUnlocked ? "" : "（围墙还没解锁）");
            sb.Append("・等材料 / 等施工的蓝图：");
            if (sites.Count == 0) sb.AppendLine("没有");
            else
            {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in sites) parts.Add(BuildName(kv.Key) + "×" + kv.Value);
                sb.AppendLine(string.Join("、", parts));
            }
            sb.Append("・异常度 ").Append(Mathf.RoundToInt(anomaly)).Append("；").AppendLine(danger >= 8f ? "刚有危险的东西靠近" : "眼下没有危险靠近");
            if (lastEvent.Length > 0) sb.Append("・最近的事：").AppendLine(lastEvent);
            sb.AppendLine("【能建的建筑】（只能从这里选；后面是造价和仓库够不够）");
            if (cands.Count == 0) sb.AppendLine("- （现在什么都建不了）");
            foreach (BuildableData d in cands)
            {
                MaterialCost[] cost = NpcIntentBrain.CostOf(d);
                string missing = MissingText(cost, false);
                sb.Append("- ").Append(d.id).Append(' ').Append(d.displayName).Append("：").Append(CostText(cost, false))
                  .Append("（").Append(missing.Length == 0 ? "够" : missing).AppendLine("）");
            }
            return sb.ToString();
        }

        static string Label(float days, float urgent, float low) => days < urgent ? "紧急" : days < low ? "偏低" : "充足";
    }

    Snap Take(WorldState w)
    {
        Snap s = new Snap
        {
            waterUrgent = WaterUrgentDays, waterLow = WaterLowDays, foodUrgent = FoodUrgentDays, foodLow = FoodLowDays,
        };
        if (w == null) return s;
        s.day = w.day;
        s.timeOfDay = w.timeOfDay ?? "";
        s.anomaly = w.anomalyLevel;
        s.danger = NpcSecurity.Danger;
        if (ClimateSystem.Instance != null)
        {
            s.weather = ClimateSystem.Instance.PromptLine(false);
            s.raining = ClimateSystem.Instance.Rain != PrecipLevel.None || ClimateSystem.Instance.Snow != PrecipLevel.None;
        }
        if (w.eventLog != null && w.eventLog.Count > 0) s.lastEvent = Clip(w.eventLog[w.eventLog.Count - 1].content ?? "", 60);

        foreach (NpcData n in w.npcs)
        {
            if (n == null) continue;
            if (n.IsDead) { s.dead++; continue; }
            s.alive++;
            if (n.isBroken) s.broken++;
            if (NpcHealth.Has(n, HealthTag.Injured)) s.injured++;
            if (NpcHealth.Has(n, HealthTag.CriticalWound)) s.critical++;
            if (NpcHealth.Has(n, HealthTag.Dying)) s.dying++;
            if (NpcHealth.Has(n, HealthTag.Sick)) s.sick++;
            if (NpcHealth.Has(n, HealthTag.Thirsty) || NpcHealth.Has(n, HealthTag.VeryThirsty) || NpcHealth.Has(n, HealthTag.Parched)) s.thirsty++;
            if (NpcHealth.Has(n, HealthTag.Hungry)) s.hungry++;
            if (NpcHealth.Has(n, HealthTag.Fatigued)) s.fatigued++;
            if (NpcSecurity.Insecure(n)) s.insecure++;
            if (NpcSociety.HasRole(n))
            {
                s.roles.TryGetValue(n.role, out int k);
                s.roles[n.role] = k + 1;
            }
        }
        NpcData leader = NpcSociety.Leader;
        s.leader = leader != null ? leader.npcName : "";

        s.stored = WaterWorks.StoredWater;
        s.demand = WaterWorks.DailyDemand;
        s.waterDays = WaterWorks.DaysLeft;
        s.collectors = WaterWorks.CollectorCount;
        s.raw = WaterWorks.RawTotal;
        s.rawCap = WaterWorks.RawCapacity;
        s.purifiers = WaterWorks.PurifierCount;
        s.purified = WaterWorks.PurifiedLastTurn;
        s.purifierLinked = PurifierLinked();

        if (ItemDatabase.Instance != null)
        {
            s.food = ItemDatabase.Instance.CountInStorage(ItemCategory.Food, w);
            s.medicine = ItemDatabase.Instance.CountInStorage(ItemCategory.Medical, w);
        }
        s.foodDays = FoodDays(w);

        foreach (PowerNode p in PowerNode.All)
        {
            if (p == null || !p.IsBuilt) continue;
            if (p.kind == PowerNodeKind.Turbine) s.turbines++;
            else if (p.kind == PowerNodeKind.Battery) s.batteries++;
        }
        PowerGrid.Summary ps = PowerGrid.Total();
        s.pStored = ps.stored; s.pCap = ps.capacity; s.pProduced = ps.produced; s.pDemand = ps.demand;
        s.shed = ps.shed; s.unlinked = ps.unlinkedDevices;

        s.wood = Have(ResourceInfo.WoodItem);
        s.stone = Have(ResourceInfo.StoneItem);
        s.ore = Have(ResourceInfo.OreItem);
        s.fiber = Have(ResourceInfo.FiberItem);

        CampZone z = CampZone.Instance;
        BuildableData wall = CampZone.WallData();
        s.wallUnlocked = wall != null && ProgressionRules.IsBuildableUnlocked(wall, out _);
        if (z != null && z.Ready)
        {
            s.zoneReady = true;
            s.enclosed = z.Enclosed;
            s.slots = z.Slots.Count;
            s.closed = z.ClosedCount;
            s.planned = z.PlannedCount;
            s.gates = z.GateCount;
            s.open = z.OpenCount;
            s.closure = z.Closure;
        }
        s.wallsNeeded = NpcIntentBrain.WallsNeeded();

        foreach (ConstructionSite site in ConstructionSite.All)
        {
            if (site == null || site.data == null) continue;
            s.sites.TryGetValue(site.data.id, out int k);
            s.sites[site.data.id] = k + 1;
        }
        s.cands.AddRange(NpcIntentBrain.BuildCandidates());
        s.cands.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return s;
    }

    // ============ 小工具 ============

    // 有没有一台净化器接在有电源（发电机 / 电池 / 终端机）的电网上。
    static bool PurifierLinked()
    {
        foreach (PowerGrid.Network n in PowerGrid.Networks)
        {
            if (n.Generators + n.Batteries <= 0) continue;
            foreach (PowerNode p in n.nodes)
                if (p != null && p.kind == PowerNodeKind.Purifier && p.IsBuilt) return true;
        }
        return false;
    }

    // 仓库里的食物还够全营地吃几天（按每种食物能顶多少饥饿 / 每人每天消耗多少饱腹算）。
    static float FoodDays(WorldState w)
    {
        NpcHealth h = NpcHealth.Instance;
        if (w == null || h == null || ItemDatabase.Instance == null) return float.PositiveInfinity;
        float supply = 0f;
        foreach (ItemStack st in w.sharedStorage)
        {
            if (st == null || st.count <= 0) continue;
            ItemData d = ItemDatabase.Instance.Get(st.itemName);
            if (d == null || d.category != ItemCategory.Food) continue;
            supply += st.count * Mathf.Max(5f, -d.hungerDelta); // 没填数值的食物按 5 算
        }
        int alive = 0;
        foreach (NpcData n in w.npcs) if (n != null && !n.IsDead) alive++;
        float perDay = alive * h.satietyPerSecond * h.secondsPerDay;
        return perDay > 0.001f ? supply / perDay : float.PositiveInfinity;
    }

    static bool Affordable(BuildableData d) => MissingText(NpcIntentBrain.CostOf(d), false).Length == 0;

    static string MissingText(MaterialCost[] cost, bool jp)
    {
        if (cost == null) return "";
        List<string> parts = new List<string>();
        foreach (MaterialCost c in cost)
        {
            if (c == null || c.amount <= 0) continue;
            int lack = c.amount - Have(c.materialName);
            if (lack > 0) parts.Add((jp ? ItemName(c.materialName) + "が" + lack + "足りない" : "缺" + MatName(c.materialName, jp) + " " + lack));
        }
        return string.Join(jp ? "・" : "、", parts);
    }

    static string CostText(MaterialCost[] cost, bool jp)
    {
        if (cost == null || cost.Length == 0) return jp ? "材料不要" : "不要材料";
        List<string> parts = new List<string>();
        foreach (MaterialCost c in cost) if (c != null && c.amount > 0) parts.Add(MatName(c.materialName, jp) + "×" + c.amount);
        return string.Join("、", parts);
    }

    // 给 4B 的局势表固定用中文名；给 14B 的按当前语言。
    static string MatName(string id, bool jp)
    {
        if (jp) return ItemName(id);
        switch (id)
        {
            case ResourceInfo.WoodItem:  return "木材";
            case ResourceInfo.StoneItem: return "石头";
            case ResourceInfo.OreItem:   return "矿石";
            case ResourceInfo.FiberItem: return "纤维";
            default: return ItemName(id);
        }
    }

    static string BuildName(string id)
    {
        BuildManager bm = BuildManager.Instance;
        if (bm != null && bm.buildables != null)
            foreach (BuildableData d in bm.buildables) if (d != null && d.id == id) return d.displayName;
        return id;
    }

    static string RoleZh(string role)
    {
        switch (role)
        {
            case "build":    return "建造组";
            case "haul":     return "搬运组";
            case "gather":   return "采集组";
            case "scavenge": return "搜寻组";
            case "care":     return "医护组";
            case "rest":     return "休整";
            default:         return role;
        }
    }

    // "约够 2.5 天" / "あと約2.5日分"
    public static string DaysPhrase(float days, bool jp)
    {
        if (float.IsInfinity(days) || float.IsNaN(days)) return jp ? "当分もつ" : "够用很久";
        if (days >= 99f) return jp ? "99日分以上" : "够 99 天以上";
        return jp ? "あと約" + days.ToString("0.0") + "日分" : "约够 " + days.ToString("0.0") + " 天";
    }

    static BuildableData Candidate(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (BuildableData d in NpcIntentBrain.BuildCandidates())
            if (string.Equals(d.id, id, StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }

    static int Have(string id) => World != null ? ItemUseRules.GetCount(id, World) : 0;
    static string ItemName(string id) => string.IsNullOrEmpty(id) ? "" : ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(id) : GameLexicon.NameOf(id);
    static string Norm(string s) => string.IsNullOrEmpty(s) ? "" : s.Trim().ToLowerInvariant();
    static string Clip(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length > max ? s.Substring(0, max) + "…" : s;
    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;
}
