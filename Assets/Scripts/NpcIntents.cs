using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Newtonsoft.Json.Linq;

// ============================================================================
// NPC 自主意图（社会系统第 1 阶段）："AI 做判断，代码做拆解和执行"。
//   1. 什么时候想：由【动机】触发，不是闲着就想。目前的动机：
//      · 安全感不足（NpcSecurity：营地空旷、异常度高、刚有危险……安全感掉到他自己的底线以下；越胆小底线越高）
//      · 营地参谋（CampAdvisor，隐形的 4B）发现了新的紧急情况（断水、断粮、有人伤重……）：每次最多 urgentResponders 个
//        没分工的人去想办法，每人对同一次紧急情况只想一次。
//      生理需求（饿 / 累 / 逃命）优先，
//      正在执行玩家指令 / 施工、玩家正在和他说话时也先不想；冷却到了、今天还有次数才会想。
//   2. 想什么：通过 AI 调度器（AiKind.NpcIntent）问主模型。AI 看得到他为什么不安（哪边敞开、异常度……），
//      只能从代码给的清单里选一件事：
//        build（建造：哪种建筑 + 营地哪一边）/ gather（采集）/ water（去湖边打水，有收集器时）/ scavenge（出去找吃的）
//        / regroup（回营地待在大家身边）/ rest（休息）/ none（先不做）
//      输出 JSON：goal / target / area / reason（为什么这么想）/ dialogue（说出口的话）。
//   3. 代码审核，再拆成任务链：
//        build：代码挑具体格子放下蓝图（玩家能看到他的计划）；围墙这类挡路的建筑优先补营地四周还敞开的缺口
//               → 材料够：去仓库取材料 → 施工（交给 ConstructionManager）
//               → 材料不够：先去采集缺的那种（最多 maxGatherTrips 趟），回来再取材料施工；凑不齐就放弃（撤掉蓝图）
//        gather / scavenge / rest：交给 NpcTaskRunner 现有的任务。
//   4. 请人帮忙（社会系统第 3 阶段）：决定建造时，AI 可以顺便点名请最多 2 个人帮忙（去采集某种材料 / 一起施工）。
//      对方答不答应由代码算：对请求者的尊重、好感、信任 + 自己的利他心 + 自己是不是也不安（修墙）- 疲劳；
//      累了 / 手上有事 / 动不了 = 有理由的拒绝；其余按意愿掷骰。小模型只负责写他回答的那一句（没有就用本地台词）。
//      答应：去采集的马上出发；一起施工的等请求者取到材料开工时再过来。被无理由拒绝，请求者对他的好感 / 信任会下降。
//   5. 让玩家看见：头顶气泡说出那句话，日志里写他的想法、打算、请了谁、对方怎么回答。
//   玩家的指令优先：NPC 正在做自己的计划时被玩家叫走，计划就放下（蓝图留着，玩家可以指派别人）。
//   饿了 / 累了 / 要逃命时也会放下手上的计划，交给需求系统。
// 材料：NPC 自己发起的建造要消耗材料（BuildableData.requiredMaterials；没填就用 DefaultCost 的默认值）。
// 【蓝图】（Blueprint.cs）：NPC 想建东西时不再直接放规划区，而是设计一份蓝图交给玩家批准（TryProposeWalls / TryProposeProject /
//   StartBuild）。批准后 / 玩家发来的蓝图：指挥的人优先忙这份蓝图（DoBlueprintWork），搬运的人把材料送到工地（HaulSite），
//   没分工的人有蓝图的活也会去帮忙；材料不够时采集的人先采缺的那种。
// 【等 AI 的时候】排队问主模型那段时间先用代码找点有用的事做（StartFiller：采营地缺的材料），AI 的决定回来再换。
// 【只有领导用 AI】（npcThinking 默认关）：AI 不再扮演每一个 NPC。14B 只扮演领导（NpcSociety：分工、定建造项目、说话），
//   其他人和普通 SLG 的 NPC 一样——按领导的分工（DoRoleWork）和玩家的指令干活，代码执行，不调 AI。
//   玩家和某个 NPC 对话时照样走 14B（NpcAgent，优先级最高）。
//   领导在想分工（主模型在写）的时候站着「正在思考……」（NpcSociety.IsThinking；以后可以接思考动画），不干活。
// 由 NpcBehaviorDriver.Start 自动挂上。
// ============================================================================
public class NpcIntentBrain : MonoBehaviour
{
    public static NpcIntentBrain Instance { get; private set; }

    [Header("开关（关掉 = NPC 不再自己想做什么）")]
    public bool autonomy = true;
    [Tooltip("普通 NPC 自己调 AI 想事情（安全感不足 / 营地有急事时）。默认关：只有领导用 AI（分工），其他人按分工 / 指令干活")]
    public bool npcThinking = false;

    [Header("节奏（秒）")]
    public float firstThinkMin = 8f;      // 开局后第一次想：在这个范围里随机，错开，不一起挤进队列
    public float firstThinkMax = 25f;
    public float cooldownAfterDone = 30f; // 做完一件事后多久再想
    public float retryAfterFail = 20f;    // 没拿到结果 / 结果不能用时多久再试
    public float phaseTimeout = 240f;     // 某一步卡住超过这么久就放弃（防止状态卡死）

    [Header("分工（领导早会分配，NpcSociety）")]
    public float roleCooldown = 2f;       // 按分工干完一件，歇多久再干下一件
    public int maxStagedWalls = 2;        // 搬运：最多先运好几段墙的材料等人来建（多了就先去干别的）

    [Header("营地参谋（CampAdvisor）")]
    public int urgentResponders = 2;      // 出现新的紧急情况时，最多几个没分工的人自己去想办法

    // 每种分工的"活的优先顺序"：前面的没活可干，才轮到后面的。
    //   活：build 建筑 / haul 搬运 / gather 采集 / care 医疗 / scavenge 探寻 / rest 休息
    [Serializable]
    public class RolePriority
    {
        public string role;
        public string[] jobs;
    }
    public List<RolePriority> rolePriorities = new List<RolePriority>
    {
        new RolePriority { role = "build",    jobs = new[] { "build", "haul", "gather", "care", "scavenge" } },
        new RolePriority { role = "haul",     jobs = new[] { "haul", "build", "gather", "care", "scavenge" } },
        new RolePriority { role = "gather",   jobs = new[] { "gather", "scavenge", "haul", "build", "care" } },
        new RolePriority { role = "scavenge", jobs = new[] { "scavenge", "gather", "haul", "build", "care" } },
        new RolePriority { role = "care",     jobs = new[] { "care", "haul", "gather", "scavenge", "build" } },
        new RolePriority { role = "rest",     jobs = new[] { "rest", "haul", "gather", "scavenge", "build" } },
    };

    [Header("限制")]
    public int maxThinking = 2;           // 同时最多几个人在"想"
    public int maxGatherTrips = 3;        // 为了凑建筑材料最多跑几趟采集

    enum Phase { Idle, Thinking, Fetching, Gathering, Building, Doing, Waiting } // Waiting = 答应了一起施工，等对方开工

    class State
    {
        public string id;
        public Phase phase = Phase.Idle;
        public float nextThink;
        public float phaseStart;
        public string goal;
        public BuildableData build;
        public ConstructionSite site;
        public GameObject siteGo;   // 蓝图所在的物体：建成后组件被删掉但物体还在；被取消则物体也没了
        public int gatherTrips;
        public readonly List<string> buildHelpers = new List<string>(); // 答应了一起施工、等开工的人
        public float nextRole;      // 下次按分工干活的时间
        public string lastJob;      // 上一次干的是哪种活（换了才写日志）
        public int roleLogDay = -1; // 今天写过"按分工去干什么"的日志了（一天只写一次，免得刷屏）
        public int urgentSeen = -1; // 对哪一次紧急情况（CampAdvisor.UrgentVersion）已经想过了
        public string projectHaul;  // 正在给哪个建造项目运材料（同一个项目同时只让一个人运）
        public string foremanBp;    // 指挥建造的蓝图（玩家发来的 / 自己交上去被批准的）
        public bool filler;         // 等 AI 回话期间，先用代码干着的活（AI 的决定回来就停掉）
        public int token;      // 每开始一个新的异步步骤 +1：旧的回调回来时对不上就忽略
    }

    class Helper { public string name, job, target; }

    class Choice
    {
        public string goal, target, area, reason, dialogue;
        public readonly List<Helper> helpers = new List<Helper>();
    }

    static readonly string[] Materials = { ResourceInfo.WoodItem, ResourceInfo.StoneItem, ResourceInfo.OreItem, ResourceInfo.FiberItem };
    static readonly string[] Areas = { "north", "south", "east", "west" };

    readonly Dictionary<string, State> _states = new Dictionary<string, State>();
    float _tick;
    int _urgentVersion = -1, _urgentCount; // 这次紧急情况已经有几个人去想办法了

    // ============ 生命周期 ============

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 新的一局：所有人的计划清空。
    public static void ClearAll()
    {
        if (Instance != null) Instance._states.Clear();
    }

    // 这个 NPC 现在在忙自己的计划吗（给别的系统看，比如悬停信息）。
    public static bool HasPlan(string npcId)
    {
        return Instance != null && Instance._states.TryGetValue(npcId ?? "", out State s) && s.phase != Phase.Idle;
    }

    void Update()
    {
        _tick -= Time.deltaTime;
        if (_tick > 0f) return;
        _tick = 0.5f;

        WorldState w = World;
        if (w == null) return;
        float now = Time.time;
        int thinking = 0;
        foreach (State s in _states.Values) if (s.phase == Phase.Thinking) thinking++;

        foreach (NpcData n in w.npcs)
        {
            if (n == null) continue;
            State s = Get(n.id, now);
            if (n.IsDead || n.isBroken)
            {
                if (s.phase != Phase.Idle) EndIntent(n, s, false, null);
                continue;
            }

            switch (s.phase)
            {
                case Phase.Idle:
                    if (!autonomy) break;
                    // 指挥建造的人：先忙自己负责的蓝图（施工 / 送材料 / 采缺的材料），忙不了再按分工干
                    // （玩家交代的事优先：对话窗口还开着、领导正在想分工，也马上动身）
                    if (!string.IsNullOrEmpty(s.foremanBp))
                    {
                        if (!BlueprintSystem.HasOpenSites(s.foremanBp)) s.foremanBp = null;
                        else if (now >= s.nextRole && CanWorkRole(n, w, true))
                        {
                            s.nextRole = now + roleCooldown;
                            if (DoBlueprintWork(n, s, s.foremanBp)) break;
                        }
                    }
                    // 领导在想分工（主模型在写）：手上的事做完就站着思考，先不接新活
                    if (NpcSociety.IsThinking(n))
                    {
                        if (CanWorkRole(n, w))
                        {
                            n.currentAction = L("leader_thinking");
                            NpcIdleBrain.InterruptFor(n.id, 1.2f);   // 别闲逛 / 闲聊
                        }
                        break;
                    }
                    // 有分工的人：只干自己的活（代码执行，不调 AI），不再自己琢磨
                    if (NpcSociety.HasRole(n))
                    {
                        if (now >= s.nextRole && CanWorkRole(n, w))
                        {
                            s.nextRole = now + roleCooldown;
                            DoRoleWork(n, s);
                        }
                        break;
                    }
                    // 没分工的人：蓝图有活（施工 / 送材料 / 采缺的材料）就去帮忙，不用等 AI
                    if (now >= s.nextRole && BlueprintSystem.HasOpenSites(null) && CanWorkRole(n, w))
                    {
                        s.nextRole = now + roleCooldown * 2f;
                        if (DoBlueprintWork(n, s, null)) break;
                    }
                    // 没分工的人：有动机才想——安全感不足，或者营地参谋发现了新的紧急情况（每次最多 urgentResponders 人，每人只想一次）
                    // （npcThinking 关着时不想：普通 NPC 不调 AI，等领导分工）
                    if (!npcThinking) break;
                    if (now < s.nextThink || thinking >= maxThinking) break;
                    bool insecure = NpcSecurity.Insecure(n);
                    if (_urgentVersion != CampAdvisor.UrgentVersion) { _urgentVersion = CampAdvisor.UrgentVersion; _urgentCount = 0; }
                    bool urgent = !insecure && CampAdvisor.HasActionableUrgent && s.urgentSeen != CampAdvisor.UrgentVersion && _urgentCount < urgentResponders;
                    if ((insecure || urgent) && CanThink(n, w, true) && Think(n, s, urgent))
                    {
                        thinking++;
                        if (urgent) { s.urgentSeen = CampAdvisor.UrgentVersion; _urgentCount++; }
                    }
                    break;
                case Phase.Building:
                    CheckBuilding(n, s);
                    break;
                case Phase.Fetching:
                case Phase.Gathering:
                case Phase.Doing:
                    // 饿了 / 累了 / 要逃命：放下自己的计划，交给需求系统（玩家的指令不受这个影响）
                    NpcDecisionRules.Behavior need = NpcDecisionRules.Decide(n, w);
                    if (need == NpcDecisionRules.Behavior.Flee || need == NpcDecisionRules.Behavior.Eat || need == NpcDecisionRules.Behavior.Rest)
                        EndIntent(n, s, false, LF("intent_interrupted", n.npcName));
                    break;
            }

            if (s.phase != Phase.Idle && now - s.phaseStart > phaseTimeout)
                EndIntent(n, s, false, LF("intent_interrupted", n.npcName));
        }
    }

    State Get(string id, float now)
    {
        if (!_states.TryGetValue(id, out State s))
        {
            s = new State { id = id, nextThink = now + UnityEngine.Random.Range(firstThinkMin, firstThinkMax) };
            _states[id] = s;
        }
        return s;
    }

    // ============ 什么时候想 ============

    // full = true：还要看今天的次数、有没有正在排队的请求（发起之前用）；false：只看人能不能动（结果回来时用）。
    bool CanThink(NpcData n, WorldState w, bool full)
    {
        if (n == null || n.IsDead || n.isBroken || !NpcHealth.CanWork(n)) return false;
        if (NpcDecisionRules.Decide(n, w) != NpcDecisionRules.Behavior.Idle) return false; // 有需求要处理：需求系统优先
        bool filler = _states.TryGetValue(n.id, out State fs) && fs.filler && fs.phase == Phase.Thinking; // 等 AI 时先干着的活不算忙
        if (!filler && NpcBusy.IsBusy(n.id)) return false;                                // 正在执行指令 / 施工 / 需求
        NpcTaskRunner r = Runner(n.id);
        if (r == null || (r.IsBusy && !filler)) return false;
        if (ConstructionManager.Instance != null && ConstructionManager.Instance.SiteOf(n.id) != null) return false; // 被指派了施工
        NpcChatWindow cw = NpcChatWindow.Instance;
        if (cw != null && cw.IsOpen && cw.CurrentNpcId == n.id) return false;              // 玩家正在和他说话
        if (!full) return true;
        if (AiScheduler.QuotaLeft(AiKind.NpcIntent, n.id) == 0) return false;               // 今天想够了
        if (AiScheduler.HasPending(AiKind.NpcIntent, n.id)) return false;
        return true;
    }

    // 能不能按分工干活：和 CanThink 一样，但"仓库食物见底"不算有需求（那是搜寻组的事），自己饿了才算。
    // chatOk = true：玩家正开着和他的对话窗口也照样干（玩家刚交代的蓝图）。
    bool CanWorkRole(NpcData n, WorldState w, bool chatOk = false)
    {
        if (n == null || n.IsDead || n.isBroken || !NpcHealth.CanWork(n)) return false;
        NpcDecisionRules.Behavior need = NpcDecisionRules.Decide(n, w);
        bool storageLowOnly = need == NpcDecisionRules.Behavior.Gather && n.hunger < 90f - n.personality.caution * 0.2f;
        if (need != NpcDecisionRules.Behavior.Idle && !storageLowOnly) return false;
        if (NpcBusy.IsBusy(n.id)) return false;
        NpcTaskRunner r = Runner(n.id);
        if (r == null || r.IsBusy) return false;
        if (ConstructionManager.Instance != null && ConstructionManager.Instance.SiteOf(n.id) != null) return false;
        NpcChatWindow cw = NpcChatWindow.Instance;
        if (!chatOk && cw != null && cw.IsOpen && cw.CurrentNpcId == n.id) return false;
        return true;
    }

    bool Think(NpcData n, State s, bool urgent = false)
    {
        string id = n.id;
        int tok = ++s.token;
        bool queued = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.NpcIntent, key = id, json = true, replacePending = true,
            system = () => BuildSystemPrompt(Npc(id)),
            user = () => urgent
                ? LanguageGuard.Pick("营地眼下有急事（见【营地眼下最要紧的事】）。现在你打算怎么做？", "拠点に急ぎの問題がある（【拠点で今いちばん急ぐこと】を見て）。今、どうするつもり？")
                : LanguageGuard.Pick("你觉得不安。现在你打算怎么做？", "落ち着かない。今、どうするつもり？"),
            // 排队期间安心下来了 / 急事已经过去了，就不用想了
            stillValid = () => autonomy && CanThink(Npc(id), World, false) && (urgent ? CampAdvisor.HasActionableUrgent : NpcSecurity.Insecure(Npc(id))),
            onResult = raw => OnThought(id, tok, raw),
        });
        if (!queued)
        {
            s.nextThink = Time.time + cooldownAfterDone;
            return false;
        }
        SetPhase(s, Phase.Thinking);
        n.currentAction = L("intent_thinking");
        StartFiller(n, s);   // 排队等 AI 的这段时间别干站着
        return true;
    }

    // 等 AI 回话时先干着的活：采营地眼下最缺的材料（蓝图 / 项目缺的优先）。AI 的决定回来就停掉。
    void StartFiller(NpcData n, State s)
    {
        NpcTaskRunner r = Runner(n.id);
        NpcMover m = NpcView.GetMover(n.id);
        if (r == null || r.IsBusy || m == null) return;
        string mat = BlueprintSystem.MissingMaterial(null) ?? ProjectMissingMaterial() ?? LowestMaterial();
        if (!ResourceInfo.TryParseKind(mat, out ResourceKind kind)) return;
        if (TerrainSystem.FindNearest(kind, m.transform.position, n.id, null) == null) return;
        s.filler = true;
        int tok = s.token;
        r.RunTasks(new List<NpcTask> { new NpcTask { action = "harvest", target = mat } }, ok => { if (s.token == tok) s.filler = false; });
        n.currentAction = LF("intent_filler", ItemName(mat));
    }

    // AI 的决定回来了：先停掉等待期间干着的活。
    void StopFiller(State s)
    {
        if (!s.filler) return;
        s.filler = false;
        NpcTaskRunner r = Runner(s.id);
        if (r != null && r.IsBusy) r.CancelAll();
    }

    // ============ AI 的结果回来了 ============

    void OnThought(string id, int tok, string raw)
    {
        if (!_states.TryGetValue(id, out State s) || s.token != tok || s.phase != Phase.Thinking) return;
        NpcData n = Npc(id);
        if (n != null && n.currentAction == L("intent_thinking")) n.currentAction = "";
        if (n == null || n.IsDead) { StopFiller(s); EndIntent(n, s, false, null); return; }

        Choice c = Parse(raw);
        bool keepFiller = c == null || c.goal == "none";   // AI 没给出能做的事：等待时的活接着干完
        if (!keepFiller) StopFiller(s);
        else s.filler = false;
        if (c == null || !CanThink(n, World, false)) { EndIntent(n, s, false, null); return; } // 没拿到 / 不能用 / 这期间有别的事了

        Say(n, c.dialogue);
        if (!string.IsNullOrEmpty(c.reason))
        {
            n.currentGoal = c.reason; // 和玩家聊天时，提示词里会带上"当前目标"
            Log(n, LF("intent_reason", n.npcName, c.reason), false);
        }
        s.goal = c.goal;

        switch (c.goal)
        {
            case "build":
                StartBuild(n, s, c);
                break;
            case "gather":
                Log(n, LF("intent_log_gather", n.npcName, ItemName(c.target)), true);
                RunChain(n, s, Phase.Doing, null, new NpcTask { action = "harvest", target = c.target });
                break;
            case "scavenge":
                Log(n, LF("intent_log_scavenge", n.npcName), true);
                RunChain(n, s, Phase.Doing, null, new NpcTask { action = "search_item", target = "" });
                break;
            case "rest":
                Log(n, LF("intent_log_rest", n.npcName), true);
                RunChain(n, s, Phase.Doing, null, new NpcTask { action = "rest" });
                break;
            case "water":
                Log(n, LF("intent_log_water", n.npcName), true);
                RunChain(n, s, Phase.Doing, null, new NpcTask { action = "harvest", target = "water" });
                break;
            case "regroup":
                Log(n, LF("intent_log_regroup", n.npcName), true);
                RunChain(n, s, Phase.Doing, null, new NpcTask { action = "go", destination = "@camp" }, new NpcTask { action = "act" });
                break;
            default: // none：先不做什么，过一会儿再想
                EndIntent(n, s, true, null);
                break;
        }
    }

    // 解析 + 审核：goal 必须在清单里，target 必须是清单里给的。不合格返回 null。
    Choice Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        JObject o;
        try
        {
            int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
            if (a < 0 || b <= a) return null;
            o = JObject.Parse(raw.Substring(a, b - a + 1));
        }
        catch { return null; }

        Choice c = new Choice
        {
            goal = Norm(o.Value<string>("goal")),
            target = Norm(o.Value<string>("target")),
            area = Norm(o.Value<string>("area")),
            reason = (o.Value<string>("reason") ?? "").Trim(),
            dialogue = (o.Value<string>("dialogue") ?? "").Trim(),
        };
        switch (c.goal)
        {
            case "build":
                if (FindBuildable(c.target) == null) return null;
                if (Array.IndexOf(Areas, c.area) < 0) c.area = Areas[UnityEngine.Random.Range(0, Areas.Length)];
                JArray hs = o["helpers"] as JArray;   // 请谁帮忙（可以没有）
                if (hs != null)
                    foreach (JToken t in hs)
                    {
                        JObject h = t as JObject;
                        if (h == null || c.helpers.Count >= 2) continue;
                        string job = Norm(h.Value<string>("job"));
                        if (job != "gather" && job != "build") continue;
                        c.helpers.Add(new Helper { name = (h.Value<string>("name") ?? "").Trim(), job = job, target = Norm(h.Value<string>("target")) });
                    }
                return c;
            case "gather":
                return Array.IndexOf(Materials, c.target) >= 0 ? c : null;
            case "water":
                return WaterWorks.CollectorCount > 0 ? c : null;   // 没有收集器，打来的水没地方倒
            case "scavenge":
            case "rest":
            case "regroup":
            case "none":
                return c;
            default:
                return null;
        }
    }

    static string Norm(string s) => string.IsNullOrEmpty(s) ? "" : s.Trim().ToLowerInvariant();

    // ============ 建造：挑地方 → 放蓝图 → 取材料 / 先采集 → 施工 ============

    // 想建东西：不再自己直接放规划区，而是设计一份蓝图交给玩家批准（围墙 = 把营地还空着的墙位一次画全）。
    void StartBuild(NpcData n, State s, Choice c)
    {
        BuildableData d = FindBuildable(c.target);
        GridManager g = GridManager.Instance;
        if (d == null || g == null || BuildManager.Instance == null) { EndIntent(n, s, false, null); return; }

        bool ok;
        if (d == CampZone.WallData() || d.id == "wall")
            ok = TryProposeWalls(n, c.reason) || BlueprintSystem.HasActive("walls");
        else
        {
            string kind = "build:" + d.id;
            ok = BlueprintSystem.HasActive(kind);
            if (!ok && BlueprintSystem.CanPropose(kind) && TryPickSpot(d, c.area, out Vector2Int cell))
            {
                Vector2Int size = d.FootprintSize(g.cellSize);
                List<BlueprintItem> items = new List<BlueprintItem> { new BlueprintItem { buildId = d.id, origin = g.FootprintOrigin(cell, size), size = size } };
                ok = BlueprintSystem.Propose(n, kind, LF("bp_name_build", d.LocalizedName),
                                             string.IsNullOrEmpty(c.reason) ? LF("bp_note_build", d.LocalizedName) : c.reason,
                                             items, LF("bp_say_build", d.LocalizedName), false) != null;
            }
        }
        if (!ok)
        {
            Log(n, LF("intent_no_spot", n.npcName, d.LocalizedName), false);
            EndIntent(n, s, false, null);
            return;
        }
        Log(n, LF("bp_log_wait", n.npcName, d.LocalizedName), true);
        EndIntent(n, s, true, null);
    }

    // ============ 蓝图（交给玩家批准 / 按批准的蓝图干活）============

    // 营地还空着的墙位（不含门）→ 一份围墙蓝图的每一项（玩家在对话里叫人修墙时用）。
    public static List<BlueprintItem> OpenWallItems()
    {
        List<BlueprintItem> items = new List<BlueprintItem>();
        CampZone z = CampZone.Instance;
        BuildableData wall = CampZone.WallData();
        GridManager g = GridManager.Instance;
        if (!WallsNeeded(z, wall) || g == null) return items;
        foreach (CampZone.Slot slot in z.Slots)
            if (!slot.gate && slot.state == CampZone.SlotState.Open && g.IsAreaBuildable(slot.origin, z.SlotSize))
                items.Add(new BlueprintItem { buildId = wall.id, origin = slot.origin, size = z.SlotSize });
        return items;
    }

    // 在营地里给这种建筑挑一个位置（和 NPC 自己提计划时一样的规则）。cell = 中心格。
    public static bool PickSpot(BuildableData d, out Vector2Int cell)
    {
        cell = Vector2Int.zero;
        return Instance != null && d != null && Instance.TryPickSpot(d, Areas[UnityEngine.Random.Range(0, Areas.Length)], out cell);
    }

    // 玩家把蓝图交给这个人指挥建造（BlueprintSystem 调）。
    public static void SetForeman(string npcId, string bpId)
    {
        if (Instance == null || string.IsNullOrEmpty(npcId)) return;
        State s = Instance.Get(npcId, Time.time);
        s.foremanBp = bpId;
        s.nextRole = 0f;   // 马上开始
    }

    // 营地还没围好：把还空着的墙位一次画成「营地围墙」蓝图交给玩家批准（有领导就由领导提交）。
    bool TryProposeWalls(NpcData n, string reason = null)
    {
        CampZone z = CampZone.Instance;
        BuildableData wall = CampZone.WallData();
        GridManager g = GridManager.Instance;
        if (n == null || !WallsNeeded(z, wall) || g == null) return false;
        if (!BlueprintSystem.CanPropose("walls")) return false;
        List<BlueprintItem> items = new List<BlueprintItem>();
        List<string> sides = new List<string>();
        foreach (CampZone.Slot slot in z.Slots)
        {
            if (slot.gate || slot.state != CampZone.SlotState.Open || !g.IsAreaBuildable(slot.origin, z.SlotSize)) continue;
            items.Add(new BlueprintItem { buildId = wall.id, origin = slot.origin, size = z.SlotSize });
            string side = CampZone.SideName(slot.side, LanguageGuard.IsJp);
            if (!sides.Contains(side)) sides.Add(side);
        }
        if (items.Count == 0) return false;
        NpcData leader = NpcSociety.Leader;
        NpcData author = leader != null && !leader.IsDead && !leader.isBroken ? leader : n;
        string note = string.IsNullOrEmpty(reason) ? LF("bp_note_walls", string.Join("、", sides), items.Count) : reason;
        return BlueprintSystem.Propose(author, "walls", L("bp_name_walls"), note, items, LF("bp_say_walls", string.Join("、", sides))) != null;
    }

    // 领导定的建造项目：挑好位置画成蓝图交给玩家批准（交出去就算这个项目定下来了，批不批由玩家决定）。
    bool TryProposeProject(NpcData n)
    {
        BuildableData d = NpcSociety.Project;
        GridManager g = GridManager.Instance;
        if (n == null || d == null || g == null) return false;
        string kind = "project:" + d.id;
        if (!BlueprintSystem.CanPropose(kind)) return false;
        if (!TryPickSpot(d, Areas[UnityEngine.Random.Range(0, Areas.Length)], out Vector2Int cell)) return false;
        Vector2Int size = d.FootprintSize(g.cellSize);
        List<BlueprintItem> items = new List<BlueprintItem> { new BlueprintItem { buildId = d.id, origin = g.FootprintOrigin(cell, size), size = size } };
        NpcData leader = NpcSociety.Leader;
        NpcData author = leader != null && !leader.IsDead && !leader.isBroken ? leader : n;
        Blueprint bp = BlueprintSystem.Propose(author, kind, LF("bp_name_build", d.LocalizedName), LF("bp_note_project", d.LocalizedName), items, LF("bp_say_project", d.LocalizedName));
        if (bp != null) NpcSociety.ProjectPlaced(d);
        return bp != null;
    }

    // 蓝图的活（bpId = null：任何一份）：材料到位的工地去施工 → 缺材料但仓库够的工地去送材料 → 都缺就去采缺的那种。
    bool DoBlueprintWork(NpcData n, State s, string bpId)
    {
        string desc;
        string job;
        if (JobBuildBlueprint(n, s, bpId, out desc)) job = "build";
        else if (HaulSite(n, s, bpId, out desc)) job = "haul";
        else
        {
            string mat = BlueprintSystem.MissingMaterial(bpId);
            if (mat == null || !GatherMaterial(n, s, mat, out desc)) return false;
            job = "gather";
        }
        if (s.phase == Phase.Idle) return false;
        s.goal = "role";
        if (job != s.lastJob && !string.IsNullOrEmpty(desc)) Log(n, LF("bp_log_job", n.npcName, desc), false);
        s.lastJob = job;
        return true;
    }

    bool JobBuildBlueprint(NpcData n, State s, string bpId, out string desc)
    {
        desc = null;
        ConstructionSite site = BlueprintSite(n, bpId, false);
        if (site == null) return false;
        ConstructionManager.Ensure().Assign(n.id, site);
        s.token++;
        SetPhase(s, Phase.Building);
        desc = LF("job_desc_build", site.DisplayName);
        return true;
    }

    // 送材料：去仓库取（这时才扣）→ 走到工地 → 工地材料到位。工地半路被撤了就把材料放回仓库。
    bool HaulSite(NpcData n, State s, string bpId, out string desc)
    {
        desc = null;
        ConstructionSite site = BlueprintSite(n, bpId, true);
        GridManager g = GridManager.Instance;
        if (site == null || g == null) return false;
        MaterialCost[] cost = site.cost;
        string label = g.CellToLabel(site.cell.x, site.cell.y);
        desc = LF("job_desc_haul_site", CostText(cost), site.DisplayName);
        n.currentAction = desc;
        string id = n.id;
        RunChain(n, s, Phase.Fetching, ok =>
        {
            NpcData me = Npc(id);
            if (!ok || me == null) { EndIntent(me, s, false, null); return; }
            ConstructionSite target = s.site;
            if (target == null || target.materialsIn) { EndIntent(me, s, true, null); return; }   // 被撤了 / 别人送到了
            if (!ProgressionRules.ConsumeMaterials(cost)) { EndIntent(me, s, true, null); return; } // 被别人先用掉了
            RunChain(me, s, Phase.Fetching, ok2 =>
            {
                NpcData me2 = Npc(id);
                if (target == null) { Refund(cost); EndIntent(me2, s, false, null); return; }
                target.materialsIn = true;   // 半路被打断也算送到了（材料不会凭空消失）
                if (me2 != null) Log(me2, LF("bp_log_delivered", me2.npcName, target.DisplayName), false);
                EndIntent(me2, s, true, null);
            },
            new NpcTask { action = "go", destination = label },
            new NpcTask { action = "act" });
            if (s.phase == Phase.Fetching) { s.site = target; s.siteGo = target.gameObject; }
        },
        new NpcTask { action = "go", destination = "@storage" },
        new NpcTask { action = "act" });
        if (s.phase != Phase.Fetching) return false;
        s.site = site;
        s.siteGo = site.gameObject;
        return true;
    }

    // 采某种材料（附近有能采的才去）。
    bool GatherMaterial(NpcData n, State s, string mat, out string desc)
    {
        desc = null;
        if (!ResourceInfo.TryParseKind(mat, out ResourceKind kind)) return false;
        NpcMover mover = NpcView.GetMover(n.id);
        if (mover == null || TerrainSystem.FindNearest(kind, mover.transform.position, n.id, null) == null) return false;
        RunChain(n, s, Phase.Doing, null, new NpcTask { action = "harvest", target = mat });
        desc = LF("job_desc_gather", ItemName(mat));
        return s.phase == Phase.Doing;
    }

    // 蓝图的工地（bpId = null：任何一份）：
    //   forHaul = false：去施工的——材料到位（或仓库够，开工时直接取）、人没满、没人正在送材料；
    //   forHaul = true： 要送材料的——材料没到、仓库够、没人正在送。挑离他最近的。
    ConstructionSite BlueprintSite(NpcData n, string bpId, bool forHaul)
    {
        NpcMover m = NpcView.GetMover(n.id);
        Vector3 pos = m != null ? m.transform.position : Vector3.zero;
        ConstructionSite best = null;
        float bestD = float.MaxValue;
        foreach (ConstructionSite site in ConstructionSite.All)
        {
            if (site == null || string.IsNullOrEmpty(site.blueprintId) || (bpId != null && site.blueprintId != bpId)) continue;
            bool reserved = false;
            foreach (State o in _states.Values)
                if (o.site == site && (o.phase == Phase.Fetching || o.phase == Phase.Gathering)) { reserved = true; break; }
            if (reserved) continue;
            if (forHaul)
            {
                if (site.materialsIn || site.builders.Count > 0 || !ProgressionRules.HasMaterials(site.cost)) continue;
            }
            else
            {
                if (site.builders.Count >= 4) continue;
                if (!site.materialsIn && !ProgressionRules.HasMaterials(site.cost)) continue;
            }
            float d = (site.transform.position - pos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = site; }
        }
        return best;
    }

    static void Refund(MaterialCost[] cost)
    {
        if (cost == null || WorldManager.Instance == null) return;
        foreach (MaterialCost c in cost) if (c != null && c.amount > 0) WorldManager.Instance.AddToStorage(c.materialName, c.amount);
    }

    // 看材料：够就去仓库取，然后施工；不够就先去采集缺的那种。
    void ProceedBuild(NpcData n, State s)
    {
        if (s.site == null || s.build == null) { EndIntent(n, s, false, null); return; } // 蓝图被玩家取消了
        MaterialCost[] cost = CostOf(s.build);
        MaterialCost missing = FirstMissing(cost, out int lack);

        if (missing == null)
        {
            n.currentAction = LF("intent_act_fetch", CostText(cost));
            RunChain(n, s, Phase.Fetching, ok =>
            {
                if (!ok) { EndIntent(n, s, false, LF("intent_interrupted", n.npcName)); return; }
                if (s.site == null) { EndIntent(n, s, false, null); return; }
                if (!ProgressionRules.ConsumeMaterials(cost)) { ProceedBuild(n, s); return; } // 被别人先用掉了：重新看
                Log(n, LF("intent_took_mat", n.npcName, CostText(cost)), true);
                ConstructionManager.Ensure().Assign(n.id, s.site);
                s.token++;
                SetPhase(s, Phase.Building);
                CallBuildHelpers(n, s); // 答应一起施工的人现在过来
            },
            new NpcTask { action = "go", destination = "@storage" },
            new NpcTask { action = "act" });
            return;
        }

        if (s.gatherTrips >= maxGatherTrips || Array.IndexOf(Materials, missing.materialName) < 0)
        {
            Log(n, LF("intent_give_up", n.npcName, s.build.LocalizedName), true);
            if (s.site != null && s.site.builders.Count == 0 && ConstructionManager.Instance != null)
                ConstructionManager.Instance.Cancel(s.site);
            s.site = null;
            EndIntent(n, s, false, null);
            return;
        }

        s.gatherTrips++;
        Log(n, LF("intent_need_mat", n.npcName, ItemName(missing.materialName), lack), true);
        RunChain(n, s, Phase.Gathering, ok =>
        {
            if (!ok) { EndIntent(n, s, false, LF("intent_interrupted", n.npcName)); return; }
            ProceedBuild(n, s);
        },
        new NpcTask { action = "harvest", target = missing.materialName });
    }

    // 施工中：ConstructionManager 管走过去、干活、暂停和恢复；建成 / 被玩家撤掉指派后，这件事就算结束。
    void CheckBuilding(NpcData n, State s)
    {
        ConstructionManager cm = ConstructionManager.Instance;
        if (cm != null && cm.SiteOf(n.id) != null) return;
        // 自己提出、自己修好了：营地里其他人对他更尊重（建成 = 蓝图组件没了、物体还在）
        if (s.site == null && s.siteGo != null && s.build != null && World != null)
            foreach (NpcData o in World.npcs)
                if (o != null && o != n && !o.IsDead)
                    RelationshipManager.Apply(o, n, 2f, 1f, 4f, LF("rel_reason_initiative", World.day, n.npcName, s.build.LocalizedName));
        s.site = null;
        EndIntent(n, s, true, null); // 建成的消息由 ConstructionManager 自己写
    }

    // 挑格子（一圈都看）：
    //   挡路的建筑（围墙等）→ 放在营地边缘那一圈，优先补还敞开的方向，尽量挨着已有的同类（连成一排，最终把营地围起来）；
    //   不挡路的 → 放在营地里面。AI 说的那一边（area）优先。
    bool TryPickSpot(BuildableData d, string area, out Vector2Int best)
    {
        best = Vector2Int.zero;
        GridManager g = GridManager.Instance;
        if (g == null) return false;

        // 营地范围（CampZone）就绪：墙只修在范围那一圈的墙位上；其他建筑放在营地里面（不压墙位）
        CampZone z = CampZone.Instance;
        if (z != null && z.Ready)
        {
            if (d.id == "wall")
            {
                CampZone.Slot slot = z.PickWallSlot(area);
                if (slot == null) return false;
                best = slot.center;
                return true;
            }
            return PickInside(g, z, d, area, out best);
        }

        Vector3 camp = NpcNav.CampCenter(Vector3.zero);
        float r0 = NpcNav.CampRadius;
        float areaDeg = AreaDeg(area);
        Vector2Int size = d.FootprintSize(g.cellSize);
        float step = Mathf.Max(0.2f, g.cellSize * 2f);
        float rMin = d.blocksMovement ? r0 - 0.5f : r0 * 0.35f;
        float rMax = d.blocksMovement ? r0 + 1.0f : r0 * 0.8f;
        float rBest = d.blocksMovement ? r0 : r0 * 0.6f;
        float bestScore = float.MinValue;
        bool found = false;

        for (float r = rMin; r <= rMax; r += step)
        {
            for (int deg = 0; deg < 360; deg += 10)
            {
                float rad = deg * Mathf.Deg2Rad;
                Vector2 v = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * r;
                Vector2Int c = g.WorldToCell(camp + (Vector3)v);
                Vector2Int o = g.FootprintOrigin(c, size);
                if (!g.IsAreaBuildable(o, size)) continue;
                float score = -Mathf.Abs(r - rBest) * 0.5f;
                float off = Mathf.Abs(Mathf.DeltaAngle(deg, areaDeg));
                if (off <= 30f) score += 3f; else if (off <= 60f) score += 2f;
                if (d.blocksMovement)
                {
                    if (!NpcSecurity.BlockedAt(deg)) score += 3f;       // 这个方向还敞开着
                    if (NextToSame(g, d, o, size)) score += 2f;          // 挨着已有的墙
                }
                if (score > bestScore) { bestScore = score; best = c; found = true; }
            }
        }
        return found;
    }

    // 营地里面挑一块地（不压墙位那一圈）：AI 说的那一边优先，离中心不远不近。
    static bool PickInside(GridManager g, CampZone z, BuildableData d, string area, out Vector2Int best)
    {
        best = Vector2Int.zero;
        Vector2Int size = d.FootprintSize(g.cellSize);
        Vector2 center = new Vector2(z.Origin.x + z.Size.x * 0.5f, z.Origin.y + z.Size.y * 0.5f);
        Vector2 dir = AreaDeg(area) == 90f ? Vector2.up : AreaDeg(area) == 270f ? Vector2.down : AreaDeg(area) == 0f ? Vector2.right : Vector2.left;
        float bestScore = float.MinValue;
        bool found = false;
        int step = Mathf.Max(1, GridManager.Subdivision);
        for (int x = z.Origin.x; x <= z.Origin.x + z.Size.x - size.x; x += step)
            for (int y = z.Origin.y; y <= z.Origin.y + z.Size.y - size.y; y += step)
            {
                Vector2Int o = new Vector2Int(x, y);
                if (!z.Inner(o, size) || !g.IsAreaBuildable(o, size)) continue;
                Vector2 c = new Vector2(x + size.x * 0.5f, y + size.y * 0.5f);
                Vector2 off = c - center;
                float score = Vector2.Dot(off.normalized, dir) * 2f - Mathf.Abs(off.magnitude - z.Size.x * 0.25f) * 0.05f + UnityEngine.Random.Range(0f, 0.2f);
                if (score > bestScore) { bestScore = score; best = new Vector2Int(x + (size.x - 1) / 2, y + (size.y - 1) / 2); found = true; }
            }
        return found;
    }

    static bool NextToSame(GridManager g, BuildableData d, Vector2Int o, Vector2Int size)
    {
        for (int x = o.x - 1; x <= o.x + size.x; x++)
            for (int y = o.y - 1; y <= o.y + size.y; y++)
            {
                bool inside = x >= o.x && x < o.x + size.x && y >= o.y && y < o.y + size.y;
                if (!inside && g.InBounds(x, y) && g.GetOccupied(x, y) == d.displayName) return true;
            }
        return false;
    }

    // 方向的角度：0° = 东，90° = 北。
    static float AreaDeg(string area)
    {
        switch (area)
        {
            case "north": return 90f;
            case "south": return 270f;
            case "east": return 0f;
            default: return 180f;
        }
    }

    // ============ 任务链 ============

    // 交给 NpcTaskRunner 跑一串任务；跑完（ok = true）或被打断（ok = false）后调用 after（null = 直接结束这件事）。
    void RunChain(NpcData n, State s, Phase phase, Action<bool> after, params NpcTask[] tasks)
    {
        NpcTaskRunner r = Runner(n.id);
        if (r == null || r.IsBusy) { EndIntent(n, s, false, null); return; }
        int tok = ++s.token;
        SetPhase(s, phase);
        string id = n.id;
        r.RunTasks(new List<NpcTask>(tasks), ok =>
        {
            if (s.token != tok) return;
            NpcData me = Npc(id);
            if (after != null) after(ok);
            else EndIntent(me, s, ok, ok ? null : LF("intent_interrupted", me != null ? me.npcName : ""));
        });
    }

    // 结束这件事（做完 / 放弃 / 被打断），过一会儿再想下一件。
    void EndIntent(NpcData n, State s, bool ok, string log)
    {
        Phase was = s.phase;
        string wasGoal = s.goal;
        s.token++; // 先作废还在路上的回调
        ReleaseHelpers(s);
        s.phase = Phase.Idle;
        s.goal = null;
        s.build = null;
        s.site = null;
        s.siteGo = null;
        s.gatherTrips = 0;
        s.projectHaul = null;
        s.nextThink = Time.time + (ok ? cooldownAfterDone : retryAfterFail) * UnityEngine.Random.Range(0.8f, 1.3f);
        if (wasGoal == "role") s.nextRole = Time.time + roleCooldown;
        if (was == Phase.Fetching || was == Phase.Gathering || was == Phase.Doing)
        {
            NpcTaskRunner r = Runner(s.id);
            if (r != null && r.IsBusy) r.CancelAll(); // 自己的任务还在跑：停下（玩家的指令已经替换掉的话这里不会动它）
        }
        if (n != null && !string.IsNullOrEmpty(log)) Log(n, log, true);
    }

    static void SetPhase(State s, Phase p)
    {
        s.phase = p;
        s.phaseStart = Time.time;
    }

    // ============ 提示词 ============

    string BuildSystemPrompt(NpcData n)
    {
        if (n == null) throw new Exception("NPC 不存在");
        WorldState w = World;
        bool jp = LanguageGuard.IsJp;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(NpcActors.Render(n));
        sb.AppendLine();
        sb.Append(PromptContext.WorldSummary(w, n, true));
        sb.AppendLine();
        sb.AppendLine(jp ? "【拠点の今の状況】" : "【营地现状】");
        sb.Append(CampStatus(w, n, jp));
        sb.AppendLine();
        sb.Append(RelationshipManager.PromptBlock(n, jp)); // 对其他人的感觉
        string body = NpcHealth.ActorBlock(n, jp);
        if (body.Length > 0) sb.AppendLine(body);
        sb.AppendLine(MentalState.GetInjection(n, w));
        sb.AppendLine();
        sb.Append(NpcSecurity.Describe(n, jp)); // 动机：为什么不安
        sb.AppendLine();
        sb.Append(Options(jp));
        sb.AppendLine();
        if (jp)
        {
            sb.AppendLine("【出力形式】JSONオブジェクトを1つだけ出力し、それ以外は何も書かないこと：");
            sb.AppendLine("{\"goal\":\"上のどれか（英字のまま）\",\"target\":\"建てる物の英字ID、または採る物（不要なら空）\",\"area\":\"north / south / east / west（buildのときだけ）\",\"helpers\":[{\"name\":\"頼む人の名前\",\"job\":\"gather か build\",\"target\":\"採る物（gatherのとき）\"}],\"reason\":\"なぜそうするのか（一人称で一文）\",\"dialogue\":\"実際に口に出す一言（短く口語で）\"}");
            sb.AppendLine("helpers は build のときだけ。頼まないなら [] にする。");
            sb.AppendLine("材料が足りなくても build を選んでよい（先に材料を採りに行く）。自分の性格と今の状況で決めること。いつも同じことを選ばない。");
        }
        else
        {
            sb.AppendLine("【输出格式】只输出一个 JSON 对象，不要多余文字：");
            sb.AppendLine("{\"goal\":\"上面之一（保持英文）\",\"target\":\"要建的英文 id，或要采的东西（不需要就留空）\",\"area\":\"north / south / east / west（只有 build 要填）\",\"helpers\":[{\"name\":\"要请的人的名字\",\"job\":\"gather 或 build\",\"target\":\"要采的东西（gather 时）\"}],\"reason\":\"你为什么这么做（第一人称，一句）\",\"dialogue\":\"你说出口的一句话（短，口语）\"}");
            sb.AppendLine("helpers 只有 build 时才填；不请人就写 []。");
            sb.AppendLine("材料不够也可以选 build——你会先去采集材料。按你的性格和眼下的局势来决定，不要每次都选同一件事。");
        }
        if (LocalizationManager.Instance != null) sb.AppendLine(LocalizationManager.Instance.GetAILanguageInstruction());
        return sb.ToString();
    }

    // 营地现状：仓库材料、已有建筑、危险程度、其他人在做什么。
    string CampStatus(WorldState w, NpcData self, bool jp)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(jp ? "倉庫：" : "仓库：");
        foreach (string m in Materials) sb.Append(ItemName(m)).Append(' ').Append(Have(m)).Append(jp ? "、" : "、");
        int food = ItemDatabase.Instance != null ? ItemDatabase.Instance.CountInStorage(ItemCategory.Food, w) : 0;
        sb.Append(jp ? "食料 " : "食物 ").Append(food).AppendLine(jp ? "食分" : " 份");
        sb.AppendLine(WaterLine(jp));
        string power = PowerLine(jp);
        if (power.Length > 0) sb.AppendLine(power);

        Dictionary<string, int> built = new Dictionary<string, int>(), planned = new Dictionary<string, int>();
        GridManager g = GridManager.Instance;
        if (g != null)
            foreach (BuildingFootprint fp in BuildingFootprint.All)
            {
                if (fp == null) continue;
                string name = g.GetOccupied(fp.origin.x, fp.origin.y);
                if (string.IsNullOrEmpty(name)) continue;
                Dictionary<string, int> dict = fp.GetComponent<ConstructionSite>() != null ? planned : built;
                dict.TryGetValue(name, out int k);
                dict[name] = k + 1;
            }
        sb.Append(jp ? "建物：" : "已有建筑：").AppendLine(Count(built, jp));
        if (planned.Count > 0) sb.Append(jp ? "計画中：" : "规划中：").AppendLine(Count(planned, jp));
        if (CampZone.Instance != null && CampZone.Instance.Ready) sb.AppendLine(CampZone.Instance.Describe(jp)); // 营地范围那一圈的围墙进度

        sb.Append(jp ? "異常度 " : "异常度 ").Append(Mathf.RoundToInt(w.anomalyLevel)).Append("（").Append(w.anomalyMood).Append("）　")
          .Append(jp ? "時間帯 " : "时段 ").AppendLine(w.timeOfDay);
        if (w.eventLog != null && w.eventLog.Count > 0)
            sb.Append(jp ? "最近の出来事：" : "最近发生：").AppendLine(w.eventLog[w.eventLog.Count - 1].content);

        sb.AppendLine(jp ? "他の人が今していること：" : "其他人在做什么：");
        int listed = 0;
        foreach (NpcData o in w.npcs)
        {
            if (o == null || o == self || o.IsDead || listed >= 9) continue;
            string act = string.IsNullOrEmpty(o.currentAction) ? (jp ? "特に何も" : "没在做什么") : o.currentAction;
            if (act.Length > 24) act = act.Substring(0, 24);
            sb.Append("・").Append(o.npcName).Append("：").AppendLine(act);
            listed++;
        }
        if (listed == 0) sb.AppendLine(jp ? "・（ほかに誰もいない）" : "・（没有别人）");
        return sb.ToString();
    }

    static string Count(Dictionary<string, int> d, bool jp)
    {
        if (d.Count == 0) return jp ? "なし" : "没有";
        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, int> kv in d) parts.Add(kv.Key + "×" + kv.Value);
        return string.Join("、", parts);
    }

    // 能做的事（清单）。建筑只列已经解锁的；每种写上要的材料和仓库够不够。
    string Options(bool jp)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(jp ? "【今できること】（必ずこの中から選ぶ）" : "【你现在能做的事】（只能从这里选）");
        List<BuildableData> list = Candidates();
        if (list.Count > 0)
        {
            sb.AppendLine(jp ? "- build：建てる。target は次のどれか：" : "- build：建造。target 填下面之一：");
            foreach (BuildableData d in list)
            {
                MaterialCost[] cost = CostOf(d);
                bool enough = FirstMissing(cost, out _) == null;
                sb.Append("    ").Append(d.id).Append("（").Append(d.LocalizedName).Append(jp ? "／材料 " : "，材料 ").Append(CostText(cost))
                  .Append(enough ? (jp ? "／倉庫にある" : "，仓库够") : (jp ? "／足りない" : "，不够")).AppendLine("）");
            }
            sb.AppendLine(jp ? "  area：拠点のどちら側に建てるか（north / south / east / west）" : "  area：建在营地哪一边（north / south / east / west）");
            sb.AppendLine(jp ? "  ※建てる前に設計図（建設計画）を描いて AI に渡し、許可をもらう。dialogue は計画を AI に見せる時のひとこと。"
                             : "  ※建造前要先画一份蓝图（建造计划）交给 AI，等 AI 批准。dialogue 写你把计划交给 AI 时说的话。");
            sb.AppendLine(jp ? "  helpers：一人では大変だと思ったら、ほかの人に手伝いを頼める（最大2人、頼まなくてもよい）。" +
                               "job は gather（target に採る物）か build（一緒に建てる）。誰に頼むかは関係と、その人が今していることで決める。相手が引き受けるとは限らない。"
                             : "  helpers：觉得一个人太费劲，可以请别人帮忙（最多 2 人，也可以不请）。" +
                               "job 填 gather（target 填要采的东西）或 build（一起施工）。请谁要看你和他们的关系、他们现在在做什么。对方不一定答应。");
        }
        sb.Append(jp ? "- gather：資源を採る。target は " : "- gather：采集。target 填 ");
        for (int i = 0; i < Materials.Length; i++) sb.Append(i > 0 ? " / " : "").Append(Materials[i]).Append("（").Append(ItemName(Materials[i])).Append("）");
        sb.AppendLine();
        if (WaterWorks.CollectorCount > 0)
            sb.AppendLine(jp ? "- water：湖で水を汲み、雨水タンクへ運ぶ（タンクの雨水は浄水器で浄化しないと飲めない）"
                             : "- water：去湖边打水，倒进雨水收集器（收集器里的雨水要经过净化器才能喝）");
        sb.AppendLine(jp ? "- scavenge：拠点の外へ食べ物や物資を探しに行く" : "- scavenge：出营地搜寻食物和物资");
        sb.AppendLine(jp ? "- regroup：拠点の真ん中に戻って、みんなのそばにいる" : "- regroup：回到营地中心，待在大家身边");
        sb.AppendLine(jp ? "- rest：休む" : "- rest：休息");
        sb.AppendLine(jp ? "- none：今は特に何もしない" : "- none：暂时不做什么");
        return sb.ToString();
    }

    // ============ 分工 / 开会（第 4 阶段，NpcSociety 调）============

    // 营地现状文字（领导分工的提示词也用）。
    public static string CampStatusText(NpcData self, bool jp) => Instance != null && World != null ? Instance.CampStatus(World, self, jp) : "";

    // 开会：手上没事的人走到营地中心。叫得动返回 true。
    public bool Summon(string id)
    {
        NpcData n = Npc(id);
        if (n == null || Excuse(n, false) != null) return false;
        State s = Get(id, Time.time);
        RunChain(n, s, Phase.Doing, null, new NpcTask { action = "go", destination = "@camp" }, new NpcTask { action = "act" });
        if (s.phase != Phase.Doing) return false;
        s.goal = "meeting";
        return true;
    }

    // 还在去开会的路上吗。
    public static bool IsSummoned(string id)
    {
        return Instance != null && Instance._states.TryGetValue(id ?? "", out State s) && s.phase == Phase.Doing && s.goal == "meeting";
    }

    // 按分工干活（代码执行，不调 AI）：按这个分工的"活的优先顺序"一项项看，第一项有活可干就干它。
    // 都没活可干返回 false。
    bool DoRoleWork(NpcData n, State s)
    {
        WorldState w = World;
        if (w == null) return false;
        foreach (string job in JobsFor(n.role))
        {
            string desc = null;
            bool started;
            switch (job)
            {
                case "build":    started = JobBuild(n, s, out desc); break;
                case "haul":     started = JobHaul(n, s, out desc); break;
                case "gather":   started = JobGather(n, s, out desc); break;
                case "care":     started = JobCare(n, s, w, out desc); break;
                case "scavenge": started = JobScavenge(n, s, out desc); break;
                case "rest":     started = JobRest(n, s, out desc); break;
                default:         started = false; break;
            }
            if (!started || s.phase == Phase.Idle) continue;
            s.goal = "role";
            // 换了一种活才写日志（同一种活一直干不刷屏）
            if (job != s.lastJob && !string.IsNullOrEmpty(desc)) Log(n, LF("role_log_job", n.npcName, NpcSociety.RoleName(n.role), desc), false);
            s.lastJob = job;
            return true;
        }
        return false;
    }

    string[] JobsFor(string role)
    {
        if (rolePriorities != null)
            foreach (RolePriority rp in rolePriorities)
                if (rp != null && rp.role == role && rp.jobs != null && rp.jobs.Length > 0) return rp.jobs;
        // Inspector 里的列表是旧的（比如场景里存过、没有新加的分工）：用默认顺序
        switch (role)
        {
            case "build":    return new[] { "build", "haul", "gather", "care", "scavenge" };
            case "haul":     return new[] { "haul", "build", "gather", "care", "scavenge" };
            case "scavenge": return new[] { "scavenge", "gather", "haul", "build", "care" };
            case "care":     return new[] { "care", "haul", "gather", "scavenge", "build" };
            case "rest":     return new[] { "rest", "haul", "gather", "scavenge", "build" };
            default:         return new[] { "gather", "scavenge", "haul", "build", "care" };
        }
    }

    // —— 建筑：去建还没建完的蓝图（不是别人正在凑材料的那块）——
    bool JobBuild(NpcData n, State s, out string desc)
    {
        desc = null;
        ConstructionSite site = OpenSite();
        if (site == null) return false;
        ConstructionManager.Ensure().Assign(n.id, site);
        s.token++;
        SetPhase(s, Phase.Building);
        desc = LF("job_desc_build", site.DisplayName);
        return true;
    }

    // —— 搬运（按这个顺序看）：
    //   ① 净化水快喝完了（少于参谋的"紧急"线，默认只够 60 秒）：先去湖边打水倒进雨水收集器
    //   ② 已经批准 / 玩家发来的蓝图：把材料从仓库送到工地（HaulSite）
    //   ③ 领导定的建造项目、营地还没围好：画成蓝图交给玩家批准（不是活，交完接着往下看）
    //   ④ 净化水不多（少于参谋的"偏低"线，默认只够 180 秒）：打水
    //   打水要有收集器、收集器没满、有净化器（没有净化器，雨水喝不了）——
    bool JobHaul(NpcData n, State s, out string desc)
    {
        desc = null;
        if (WaterWorks.DaysLeft < CampAdvisor.WaterUrgentDays && HaulWater(n, s, out desc)) return true;
        if (HaulSite(n, s, null, out desc)) return true;   // 批准了的蓝图：把材料送到工地
        TryProposeProject(n);                               // 领导定的项目：画成蓝图交给玩家批准（不再自己直接放）
        TryProposeWalls(n);                                 // 营地没围好：画「营地围墙」蓝图交给玩家批准
        if (WaterWorks.DaysLeft < CampAdvisor.WaterLowDays && HaulWater(n, s, out desc)) return true;
        return false;
    }

    bool HaulWater(NpcData n, State s, out string desc)
    {
        desc = null;
        if (WaterWorks.CollectorCount == 0 || WaterWorks.PurifierCount == 0 || WaterWorks.RawTotal >= WaterWorks.RawCapacity) return false;
        RunChain(n, s, Phase.Doing, null, new NpcTask { action = "harvest", target = "water" });
        desc = L("job_desc_haul_water");
        return true;
    }

    // 建造项目：材料够、还没放下蓝图、没有别人正在运 → 去仓库取材料，运到营地里挑好的位置，放下蓝图。
    // 【已停用】改成画蓝图交给玩家批准（TryProposeProject）后不再调用，先留着备查。
    bool HaulProject(NpcData n, State s, out string desc)
    {
        desc = null;
        BuildableData d = NpcSociety.Project;
        GridManager g = GridManager.Instance;
        if (d == null || g == null || BuildManager.Instance == null) return false;
        foreach (ConstructionSite site in ConstructionSite.All)
            if (site != null && site.data != null && site.data.id == d.id) return false;           // 蓝图已经放下了：等建造组施工
        foreach (State o in _states.Values)
            if (o != s && o.projectHaul == d.id && o.phase != Phase.Idle) return false;           // 别人正在运
        MaterialCost[] cost = CostOf(d);
        if (FirstMissing(cost, out _) != null) return false;                                     // 材料不够：采集的人会先去采
        string area = Areas[UnityEngine.Random.Range(0, Areas.Length)];
        if (!TryPickSpot(d, area, out Vector2Int cell)) return false;
        string label = g.CellToLabel(cell.x, cell.y);
        desc = LF("job_desc_haul_project", CostText(cost), d.LocalizedName);
        n.currentAction = desc;
        string id = n.id;
        RunChain(n, s, Phase.Fetching, ok =>
        {
            NpcData me = Npc(id);
            if (!ok || me == null) { EndIntent(me, s, false, null); return; }
            if (NpcSociety.Project != d) { EndIntent(me, s, true, null); return; }                 // 半路换了项目
            if (!ProgressionRules.ConsumeMaterials(cost)) { EndIntent(me, s, true, null); return; } // 材料被别人先用掉了
            ConstructionSite site = BuildManager.Instance.PlaceBlueprint(d, cell);              // 放不下（位置被占了）会返回 null
            if (site == null && TryPickSpot(d, area, out Vector2Int other)) site = BuildManager.Instance.PlaceBlueprint(d, other);
            if (site == null)
            {
                foreach (MaterialCost c in cost) if (c != null && c.amount > 0 && WorldManager.Instance != null) WorldManager.Instance.AddToStorage(c.materialName, c.amount);
                EndIntent(me, s, true, null);
                return;
            }
            NpcSociety.ProjectPlaced(d);
            Log(me, LF("role_log_wall_ready", me.npcName, site.DisplayName), false);
            EndIntent(me, s, true, null);
        },
        new NpcTask { action = "go", destination = "@storage" },
        new NpcTask { action = "act" },
        new NpcTask { action = "go", destination = label },
        new NpcTask { action = "act" });
        if (s.phase == Phase.Fetching) s.projectHaul = d.id;
        return true;
    }

    // 【已停用】改成画「营地围墙」蓝图交给玩家批准（TryProposeWalls）后不再调用，先留着备查。
    bool HaulWall(NpcData n, State s, out string desc)
    {
        desc = null;
        CampZone z = CampZone.Instance;
        BuildableData wall = CampZone.WallData();
        GridManager g = GridManager.Instance;
        if (!WallsNeeded(z, wall) || g == null) return false;
        if (StagedWalls() >= maxStagedWalls) return false;           // 已经有够多的墙等着人建了
        MaterialCost[] cost = CostOf(wall);
        if (FirstMissing(cost, out _) != null) return false;          // 石头不够：没东西可搬（采集的人会去采）
        CampZone.Slot slot = z.PickWallSlot(z.MostOpenSide());
        if (slot == null) return false;
        string label = g.CellToLabel(slot.center.x, slot.center.y);
        string where = CampZone.SideName(slot.side, LanguageGuard.IsJp) + "（" + label + "）";
        desc = LF("job_desc_haul_wall", CostText(cost), where);
        n.currentAction = desc;
        string id = n.id;
        RunChain(n, s, Phase.Fetching, ok =>
        {
            NpcData me = Npc(id);
            if (!ok || me == null) { EndIntent(me, s, false, null); return; }
            if (!ProgressionRules.ConsumeMaterials(cost)) { EndIntent(me, s, true, null); return; } // 半路被别人用掉了
            ConstructionSite site = null;
            GridManager gg = GridManager.Instance;
            if (gg != null && gg.IsAreaBuildable(slot.origin, z.SlotSize) && BuildManager.Instance != null)
                site = BuildManager.Instance.PlaceBlueprint(wall, slot.center);
            if (site == null) site = PlaceNextWall(wall, out _);       // 那个位置被占了：就近换一个
            if (site == null)
            {
                foreach (MaterialCost c in cost) if (c != null && c.amount > 0 && WorldManager.Instance != null) WorldManager.Instance.AddToStorage(c.materialName, c.amount);
                EndIntent(me, s, true, null);
                return;
            }
            Log(me, LF("role_log_wall_ready", me.npcName, site.DisplayName), false);
            EndIntent(me, s, true, null);
        },
        new NpcTask { action = "go", destination = "@storage" },
        new NpcTask { action = "act" },
        new NpcTask { action = "go", destination = label },
        new NpcTask { action = "act" });
        return true;
    }

    // 已经放下、还没人施工的墙（材料都已运到）。
    static int StagedWalls()
    {
        int k = 0;
        foreach (ConstructionSite site in ConstructionSite.All)
            if (site != null && site.data != null && site.data.id == "wall" && site.builders.Count == 0) k++;
        return k;
    }

    // 营地还没围好、还有能修墙的缺口、围墙已解锁（顶部资源栏的石头提示也用它）
    public static bool WallsNeeded() => WallsNeeded(CampZone.Instance, CampZone.WallData());

    static bool WallsNeeded(CampZone z, BuildableData wall)
    {
        return z != null && z.Ready && !z.Enclosed && z.OpenCount > 0 && wall != null && ProgressionRules.IsBuildableUnlocked(wall, out _);
    }

    // —— 采集：采集组按分到的材料；其他人：营地还缺墙就采石头，否则采最缺的。附近采不到就换一种 ——
    bool JobGather(NpcData n, State s, out string desc)
    {
        desc = null;
        List<string> order = new List<string>();
        if (n.role == "gather" && Array.IndexOf(Materials, n.roleTarget) >= 0) order.Add(n.roleTarget);
        string bpNeed = BlueprintSystem.MissingMaterial(null);              // 蓝图（建造中 / 等批准）还缺的材料
        if (bpNeed != null && !order.Contains(bpNeed)) order.Add(bpNeed);
        string projectNeed = ProjectMissingMaterial();                       // 领导定的建造项目还缺的材料
        if (projectNeed != null && !order.Contains(projectNeed)) order.Add(projectNeed);
        string usual = WallsNeeded(CampZone.Instance, CampZone.WallData()) ? ResourceInfo.StoneItem : LowestMaterial();
        if (!order.Contains(usual)) order.Add(usual);
        foreach (string m in new[] { ResourceInfo.StoneItem, ResourceInfo.WoodItem, ResourceInfo.OreItem, ResourceInfo.FiberItem })
            if (!order.Contains(m)) order.Add(m);
        NpcMover mover = NpcView.GetMover(n.id);
        Vector3 pos = mover != null ? mover.transform.position : Vector3.zero;
        foreach (string m in order)
        {
            if (!ResourceInfo.TryParseKind(m, out ResourceKind kind)) continue;
            if (TerrainSystem.FindNearest(kind, pos, n.id, null) == null) continue; // 附近没有能采的
            RunChain(n, s, Phase.Doing, null, new NpcTask { action = "harvest", target = m });
            desc = LF("job_desc_gather", ItemName(m));
            return true;
        }
        return false;
    }

    // —— 医疗：有伤病的人、仓库里有药 ——
    bool JobCare(NpcData n, State s, WorldState w, out string desc)
    {
        desc = null;
        NpcData patient = MostHurt(n);
        // 身上带着医疗包就先用自己的（NpcInventory），没有再去仓库拿药
        string item = NpcInventory.Has(n, "medkit") ? "medkit"
                    : ItemDatabase.Instance != null ? ItemDatabase.Instance.FindFirstInStorage(ItemCategory.Medical, w) : null;
        if (patient == null || item == null) return false;
        RunChain(n, s, Phase.Doing, null, new NpcTask { action = "use_item", target = item, destination = "@npc:" + patient.id });
        desc = LF("job_desc_care", patient.npcName);
        return true;
    }

    // —— 探寻：出营地找吃的和物资 ——
    bool JobScavenge(NpcData n, State s, out string desc)
    {
        RunChain(n, s, Phase.Doing, null, new NpcTask { action = "search_item", target = "" });
        desc = L("job_desc_scavenge");
        return true;
    }

    // —— 休息：还累才歇 ——
    bool JobRest(NpcData n, State s, out string desc)
    {
        desc = null;
        if (n.energy >= 80f) return false;
        RunChain(n, s, Phase.Doing, null, new NpcTask { action = "rest" });
        desc = L("job_desc_rest");
        return true;
    }

    // 在营地范围那一圈开下一段墙：空得最多的那一边优先、挨着已有墙优先；位置被占了就换下一个。
    static ConstructionSite PlaceNextWall(BuildableData wall, out CampZone.Slot used)
    {
        used = null;
        CampZone z = CampZone.Instance;
        GridManager g = GridManager.Instance;
        if (z == null || g == null || BuildManager.Instance == null) return null;
        string side = z.MostOpenSide();
        HashSet<CampZone.Slot> skip = new HashSet<CampZone.Slot>();
        for (int i = 0; i < 8; i++)
        {
            CampZone.Slot slot = z.PickWallSlot(side, skip);
            if (slot == null) return null;
            skip.Add(slot);
            if (!g.IsAreaBuildable(slot.origin, z.SlotSize)) continue;
            ConstructionSite site = BuildManager.Instance.PlaceBlueprint(wall, slot.center);
            if (site != null) { used = slot; return site; }
        }
        return null;
    }

    // 仓库里最少的材料（木 / 石 / 矿）。
    static string LowestMaterial()
    {
        string best = ResourceInfo.StoneItem;
        int min = int.MaxValue;
        foreach (string m in new[] { ResourceInfo.StoneItem, ResourceInfo.WoodItem, ResourceInfo.OreItem })
        {
            int have = Have(m);
            if (have < min) { min = have; best = m; }
        }
        return best;
    }

    // 可以去帮忙施工的蓝图：人不满、也不是别人正在凑材料的（那块还没扣材料，不能先开工）。
    ConstructionSite OpenSite()
    {
        foreach (ConstructionSite site in ConstructionSite.All)
        {
            if (site == null || site.builders.Count >= 4) continue;
            if (!site.materialsIn && !ProgressionRules.HasMaterials(site.cost)) continue;   // 蓝图的工地：材料没到、仓库也不够
            bool reserved = false;
            foreach (State o in _states.Values)
                if (o.site == site && (o.phase == Phase.Fetching || o.phase == Phase.Gathering)) { reserved = true; break; }
            if (!reserved) return site;
        }
        return null;
    }

    // 伤得最重的人（受伤 / 致命伤 / 生病 / 濒死），不含自己。
    static NpcData MostHurt(NpcData self)
    {
        NpcData best = null;
        if (World == null) return null;
        foreach (NpcData o in World.npcs)
        {
            if (o == null || o == self || o.IsDead) continue;
            bool hurt = NpcHealth.Has(o, HealthTag.Injured) || NpcHealth.Has(o, HealthTag.CriticalWound)
                        || NpcHealth.Has(o, HealthTag.Sick) || NpcHealth.Has(o, HealthTag.Dying);
            if (hurt && (best == null || o.health < best.health)) best = o;
        }
        return best;
    }

    // ============ 请人帮忙（第 3 阶段）============

    // 【已停用】建造改成交蓝图以后不再调用（批准后指挥的人 + 没分工的人会一起干），先留着备查。
    void RequestHelp(NpcData asker, State s, Choice c)
    {
        s.buildHelpers.Clear();
        if (c.helpers.Count == 0 || s.build == null) return;
        MaterialCost[] cost = CostOf(s.build);
        foreach (Helper h in c.helpers)
        {
            NpcData t = FindByName(h.name, asker);
            if (t == null) continue;
            string mat = null;
            if (h.job == "gather")
            {
                mat = Array.IndexOf(Materials, h.target) >= 0 ? h.target : null;
                if (mat == null) { MaterialCost m = FirstMissing(cost, out _); mat = m != null ? m.materialName : (cost.Length > 0 ? cost[0].materialName : ResourceInfo.StoneItem); }
                if (Array.IndexOf(Materials, mat) < 0) continue;
            }
            Ask(asker, t, h.job, mat, s);
        }
    }

    // 请求 → 代码决定答不答应 → 关系结算 → 回答（小模型写一句）→ 答应就开始干。
    void Ask(NpcData asker, NpcData t, string job, string mat, State askerState)
    {
        int day = World != null ? World.day : 0;
        string what = job == "gather" ? LF("social_job_gather", ItemName(mat)) : LF("social_job_build", askerState.build.LocalizedName);
        Bubble(asker, LF("social_ask_bubble", t.npcName, what));
        Log(asker, LF("social_log_ask", asker.npcName, t.npcName, what), true);
        NpcSociety.NoteHelpRequest(); // 第一次集体行动：出现领导的契机之一

        string excuse = Excuse(t, false);
        bool yes = excuse == null && Willingness(t, asker, job, askerState.build) >= 50f;
        if (yes) RelationshipManager.Apply(asker, t, 3f, 2f, 0f, LF("rel_reason_agreed", day, t.npcName, what));
        else if (excuse != null) RelationshipManager.Apply(asker, t, -1f, 0f, 0f, null);           // 有理由的拒绝：有点失望
        else
        {
            RelationshipManager.Apply(asker, t, -5f, -3f, 0f, LF("rel_reason_refused", day, t.npcName)); // 不肯帮：好感 / 信任下降
            RelationshipManager.Apply(t, asker, 0f, 0f, -1f, null);
        }
        Reply(t, asker, what, yes, excuse);
        if (!yes) return;

        State ts = Get(t.id, Time.time);
        if (job == "gather")
        {
            string tid = t.id, aid = asker.id;
            RunChain(t, ts, Phase.Doing, ok =>
            {
                NpcData me = Npc(tid), a2 = Npc(aid);
                if (ok && me != null && a2 != null && World != null)
                    RelationshipManager.Apply(a2, me, 4f, 1f, 2f, LF("rel_reason_helped_gather", World.day, me.npcName, ItemName(mat)));
                EndIntent(me, ts, ok, ok ? null : LF("intent_interrupted", me != null ? me.npcName : ""));
            }, new NpcTask { action = "harvest", target = mat });
            ts.goal = "help";
        }
        else
        {
            ts.token++;
            SetPhase(ts, Phase.Waiting);   // 先占住，等请求者取到材料开工再过来（期间自己不会去想别的事）
            ts.goal = "help";
            askerState.buildHelpers.Add(t.id);
        }
    }

    // 有理由的拒绝：动不了 / 太累 / 手上有事。null = 没理由，看意愿。allowWaiting：已经答应在等的不算"手上有事"。
    string Excuse(NpcData t, bool allowWaiting)
    {
        if (t == null || t.IsDead || t.isBroken || !NpcHealth.CanWork(t)) return "cannot";
        if (t.energy < 20f) return "tired";
        if (_states.TryGetValue(t.id, out State ts) && ts.phase != Phase.Idle && !(allowWaiting && ts.phase == Phase.Waiting)) return "busy";
        if (NpcBusy.IsBusy(t.id)) return "busy";
        NpcTaskRunner r = Runner(t.id);
        if (r == null) return "cannot";
        if (r.IsBusy) return "busy";
        if (ConstructionManager.Instance != null && ConstructionManager.Instance.SiteOf(t.id) != null) return "busy";
        NpcDecisionRules.Behavior need = World != null ? NpcDecisionRules.Decide(t, World) : NpcDecisionRules.Behavior.Idle;
        if (need == NpcDecisionRules.Behavior.Rest) return "tired";
        if (need == NpcDecisionRules.Behavior.Eat || need == NpcDecisionRules.Behavior.Flee) return "busy";
        return null;
    }

    // 愿不愿意（中立的人大约 50，答应的门槛是 50）：
    //   尊重 35% + 好感 25% + 信任 15% + 利他 25%；自己也觉得不安、又是修墙 → +15；体力不到 50 → 每少 1 点 -0.6；再加一点随机。
    static float Willingness(NpcData t, NpcData asker, string job, BuildableData build)
    {
        float respect = RelationshipManager.RespectOf(t, asker.id);
        float aff = RelationshipManager.AffinityOf(t, asker.id);
        float trust = RelationshipManager.TrustOf(t, asker.id);
        float altruism = t.personality != null ? t.personality.altruism : 50f;
        float w = 0.35f * respect + 0.25f * ((aff + 100f) * 0.5f) + 0.15f * trust + 0.25f * altruism;
        if (build != null && build.blocksMovement && NpcSecurity.Insecure(t)) w += 15f;
        w -= Mathf.Max(0f, 50f - t.energy) * 0.6f;
        return w + UnityEngine.Random.Range(-12f, 12f);
    }

    // 请求者取到材料开工了：答应一起施工的人过来。
    void CallBuildHelpers(NpcData asker, State s)
    {
        foreach (string id in s.buildHelpers)
        {
            NpcData h = Npc(id);
            if (h == null || !_states.TryGetValue(id, out State hs) || hs.phase != Phase.Waiting) continue;
            if (s.site == null || Excuse(h, true) != null)
            {
                Log(h, LF("social_log_no_show", h.npcName), false);
                EndIntent(h, hs, false, null);
                continue;
            }
            ConstructionManager.Ensure().Assign(h.id, s.site);
            hs.token++;
            SetPhase(hs, Phase.Building);
            Log(h, LF("social_log_join", h.npcName, asker.npcName), true);
        }
        s.buildHelpers.Clear();
    }

    // 请求者的计划结束了：还在等开工的人散了。
    void ReleaseHelpers(State s)
    {
        if (s.buildHelpers.Count == 0) return;
        List<string> ids = new List<string>(s.buildHelpers);
        s.buildHelpers.Clear();
        foreach (string id in ids)
            if (_states.TryGetValue(id, out State hs) && hs.phase == Phase.Waiting) EndIntent(Npc(id), hs, true, null);
    }

    // 被请求的人怎么回答：小模型按他的人设写一句（答不答应已经定了）；没有小模型 / 失败就用本地台词。
    void Reply(NpcData t, NpcData asker, string what, bool yes, string excuse)
    {
        string local = L(yes ? "social_reply_yes" : excuse == "tired" ? "social_reply_tired" : excuse == "busy" ? "social_reply_busy"
                       : excuse == "cannot" ? "social_reply_cannot" : "social_reply_no");
        string outcome = L(yes ? "social_outcome_yes" : "social_outcome_no");
        string tid = t.id, aid = asker.id;
        bool sent = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.SocialLine, key = tid, json = true, maxTokens = 60, languageAnchor = true, replacePending = true,
            system = () => ReplyPrompt(Npc(tid), Npc(aid), what, yes, excuse),
            user = () => LanguageGuard.Pick("说出你的回答。", "返事をして。"),
            onResult = raw =>
            {
                string line = DialogueOf(raw);
                ShowReply(Npc(tid), string.IsNullOrEmpty(line) ? local : line, outcome);
            },
        });
        if (!sent) ShowReply(t, local, outcome);
    }

    static string ReplyPrompt(NpcData t, NpcData asker, string what, bool yes, string excuse)
    {
        if (t == null || asker == null) throw new Exception("NPC 不存在");
        bool jp = LanguageGuard.IsJp;
        string traits = t.traits != null && t.traits.Count > 0 ? string.Join("、", t.traits) : "";
        string why;
        if (yes) why = NpcSecurity.Insecure(t) ? (jp ? "自分も拠点が不安だと思っている" : "你自己也觉得营地不安全") : (jp ? "手伝ってもいいと思った" : "你愿意帮这个忙");
        else if (excuse == "tired") why = jp ? "くたくたに疲れている" : "你累坏了";
        else if (excuse == "busy") why = jp ? "ほかにやることがある" : "你手上有别的事";
        else if (excuse == "cannot") why = jp ? "体が動かない" : "你身体动不了";
        else
        {
            float aff = RelationshipManager.AffinityOf(t, asker.id), rs = RelationshipManager.RespectOf(t, asker.id);
            why = aff <= -20f ? (jp ? "この人が好きじゃない" : "你不喜欢这个人")
                : rs <= 35f ? (jp ? "この人に指図されたくない" : "你不服这个人，不想听他使唤")
                : (jp ? "気が進まない" : "你就是不想去");
        }
        StringBuilder sb = new StringBuilder();
        if (jp)
        {
            sb.AppendLine("あなたは" + t.npcName + (string.IsNullOrEmpty(t.profession) ? "" : "（" + t.profession + "）") + "。");
            if (traits.Length > 0) sb.AppendLine("性格：" + traits);
            if (!string.IsNullOrEmpty(t.speakingStyle)) sb.AppendLine("話し方：" + t.speakingStyle);
            sb.AppendLine(asker.npcName + "への気持ち：" + RelationshipManager.Label(t, asker.id));
            sb.AppendLine(asker.npcName + "に今、" + what + "のを頼まれた。");
            sb.AppendLine("あなたは【" + (yes ? "引き受けた" : "断った") + "】。理由：" + why + "。");
            sb.AppendLine("この人物として、短い返事を一言だけ（20文字以内、話し言葉）。");
            sb.AppendLine("JSONを1つだけ出力：{\"dialogue\":\"返事\"}");
        }
        else
        {
            sb.AppendLine("你是" + t.npcName + (string.IsNullOrEmpty(t.profession) ? "" : "（" + t.profession + "）") + "。");
            if (traits.Length > 0) sb.AppendLine("性格：" + traits);
            if (!string.IsNullOrEmpty(t.speakingStyle)) sb.AppendLine("说话风格：" + t.speakingStyle);
            sb.AppendLine("你对" + asker.npcName + "的感觉：" + RelationshipManager.Label(t, asker.id));
            sb.AppendLine(asker.npcName + "刚才请你帮忙" + what + "。");
            sb.AppendLine("你【" + (yes ? "答应了" : "拒绝了") + "】。原因：" + why + "。");
            sb.AppendLine("用这个人的口吻，只说一句很短的回答（20 字以内，口语）。");
            sb.AppendLine("只输出一个 JSON：{\"dialogue\":\"回答\"}");
        }
        return sb.ToString();
    }

    static void ShowReply(NpcData t, string line, string outcome)
    {
        if (t == null) return;
        line = DialogueCleaner.Clean(line, t.npcName);
        if (string.IsNullOrEmpty(line)) return;
        Bubble(t, line);
        Log(t, LF("social_log_reply", t.npcName, outcome, line), true);
    }

    static string DialogueOf(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
            if (a < 0 || b <= a) return null;
            return (JObject.Parse(raw.Substring(a, b - a + 1)).Value<string>("dialogue") ?? "").Trim();
        }
        catch { return null; }
    }

    // 按名字找人（不含自己、活着的）：先全名，再互相包含。
    static NpcData FindByName(string name, NpcData self)
    {
        if (string.IsNullOrEmpty(name) || World == null) return null;
        foreach (NpcData o in World.npcs)
            if (o != null && o != self && !o.IsDead && o.npcName == name) return o;
        foreach (NpcData o in World.npcs)
            if (o != null && o != self && !o.IsDead && !string.IsNullOrEmpty(o.npcName) && (name.Contains(o.npcName) || o.npcName.Contains(name))) return o;
        return null;
    }

    static void Bubble(NpcData n, string line)
    {
        if (n == null || string.IsNullOrEmpty(line)) return;
        NpcMover m = NpcView.GetMover(n.id);
        if (m != null) NpcSpeechBubble.Show(m.transform, line, 3.5f);
    }

    // ============ 建筑 / 材料 ============

    // 现在能建的建筑（已解锁、不是唯一建筑）。营地参谋、领导分工也用。
    public static List<BuildableData> BuildCandidates() => Candidates();

    // 按 id / 名字找一个能建的建筑；找不到返回 null。
    public static BuildableData FindCandidate(string idOrName) => FindBuildable(idOrName);

    // 能建的建筑清单（领导定建造项目用）：- id（名字，材料 …，仓库够 / 不够）。
    public static string BuildListText(bool jp, bool skipWall)
    {
        StringBuilder sb = new StringBuilder();
        foreach (BuildableData d in Candidates())
        {
            if (skipWall && d.id == "wall") continue;
            MaterialCost[] cost = CostOf(d);
            bool enough = FirstMissing(cost, out _) == null;
            sb.Append("- ").Append(d.id).Append("（").Append(d.LocalizedName).Append(jp ? "／材料 " : "，材料 ").Append(CostText(cost))
              .Append(enough ? (jp ? "／倉庫にある" : "，仓库够") : (jp ? "／足りない" : "，不够")).AppendLine("）");
        }
        return sb.ToString();
    }

    // 建造项目还缺哪种材料（都够 / 没有项目返回 null）。
    static string ProjectMissingMaterial()
    {
        BuildableData d = NpcSociety.Project;
        if (d == null) return null;
        MaterialCost m = FirstMissing(CostOf(d), out _);
        return m != null && Array.IndexOf(Materials, m.materialName) >= 0 ? m.materialName : null;
    }

    // 水和电的现状（营地现状里用：NPC 自己想事情、领导分工都看得到）。
    static string WaterLine(bool jp)
    {
        string d = CampAdvisor.DaysPhrase(WaterWorks.DaysLeft, jp);
        return jp ? $"水：浄化水 {WaterWorks.StoredWater}（{d}）／雨水タンク {WaterWorks.CollectorCount} 基（雨水 {WaterWorks.RawTotal}/{WaterWorks.RawCapacity}）／浄水器 {WaterWorks.PurifierCount} 台"
                  : $"水：净化水 {WaterWorks.StoredWater} 份（{d}）；雨水收集器 {WaterWorks.CollectorCount} 台（雨水 {WaterWorks.RawTotal}/{WaterWorks.RawCapacity}）；水源净化器 {WaterWorks.PurifierCount} 台";
    }

    static string PowerLine(bool jp)
    {
        int nodes = 0;
        foreach (PowerNode p in PowerNode.All) if (p != null && p.IsBuilt && p.kind != PowerNodeKind.Terminal) nodes++;
        if (nodes == 0) return "";
        PowerGrid.Summary ps = PowerGrid.Total();
        string s = jp ? $"電気：蓄電 {ps.stored:0.#}/{ps.capacity:0.#}" : $"电：储电 {ps.stored:0.#}/{ps.capacity:0.#}";
        if (ps.shed > 0) s += jp ? "（電気が足りず止まっている設備がある）" : "（有设备因为电不够停了）";
        if (ps.unlinkedDevices > 0) s += jp ? "（電源につながっていない設備がある）" : "（有设备没接上电）";
        return s;
    }

    static List<BuildableData> Candidates()
    {
        List<BuildableData> list = new List<BuildableData>();
        BuildManager bm = BuildManager.Instance;
        if (bm == null || bm.buildables == null) return list;
        foreach (BuildableData d in bm.buildables)
            if (d != null && !d.uniqueBuild && d.prefab != null && ProgressionRules.IsBuildableUnlocked(d, out _)) list.Add(d);
        return list;
    }

    static BuildableData FindBuildable(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName)) return null;
        foreach (BuildableData d in Candidates())
            if (string.Equals(d.id, idOrName, StringComparison.OrdinalIgnoreCase) || d.displayName == idOrName || d.displayNameJa == idOrName) return d;
        return null;
    }

    // 建造要的材料：资产里填了就用资产的；没填用默认值（玩家放的蓝图目前不要材料，只有 NPC 自己发起的建造才扣）。
    public static MaterialCost[] CostOf(BuildableData d)
    {
        if (d == null) return new MaterialCost[0];
        if (d.requiredMaterials != null && d.requiredMaterials.Length > 0) return d.requiredMaterials;
        return DefaultCost(d.id);
    }

    static MaterialCost[] DefaultCost(string id)
    {
        switch (id)
        {
            case "wall": return Cost(ResourceInfo.StoneItem, 2);
            case "box": return Cost(ResourceInfo.WoodItem, 1);
            case PowerBuildings.CollectorId: return Cost(ResourceInfo.WoodItem, 2);
            case PowerBuildings.PurifierId: return Cost(ResourceInfo.StoneItem, 1, ResourceInfo.OreItem, 1);
            case PowerBuildings.TurbineId: return Cost(ResourceInfo.WoodItem, 2, ResourceInfo.OreItem, 1);
            case PowerBuildings.BatteryId: return Cost(ResourceInfo.OreItem, 2);
            case PowerBuildings.HeaterId:
            case PowerBuildings.CoolerId: return Cost(ResourceInfo.OreItem, 1, ResourceInfo.StoneItem, 1);
            default: return Cost(ResourceInfo.WoodItem, 1);
        }
    }

    static MaterialCost[] Cost(params object[] pairs)
    {
        List<MaterialCost> list = new List<MaterialCost>();
        for (int i = 0; i + 1 < pairs.Length; i += 2)
            list.Add(new MaterialCost { materialName = (string)pairs[i], amount = (int)pairs[i + 1] });
        return list.ToArray();
    }

    static MaterialCost FirstMissing(MaterialCost[] cost, out int lack)
    {
        lack = 0;
        if (cost == null) return null;
        foreach (MaterialCost c in cost)
        {
            if (c == null || c.amount <= 0) continue;
            int have = Have(c.materialName);
            if (have < c.amount) { lack = c.amount - have; return c; }
        }
        return null;
    }

    public static string CostText(MaterialCost[] cost)
    {
        if (cost == null || cost.Length == 0) return LanguageGuard.Pick("不要材料", "材料不要");
        List<string> parts = new List<string>();
        foreach (MaterialCost c in cost) if (c != null && c.amount > 0) parts.Add(ItemName(c.materialName) + "×" + c.amount);
        return string.Join("、", parts);
    }

    static int Have(string itemId) => World != null ? ItemUseRules.GetCount(itemId, World) : 0;

    static string ItemName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        return ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(id) : GameLexicon.NameOf(id);
    }

    // ============ 让玩家看见 ============

    // 头顶气泡 + 日志。
    static void Say(NpcData n, string line)
    {
        if (n == null || string.IsNullOrEmpty(line)) return;
        line = DialogueCleaner.Clean(line, n.npcName);
        if (string.IsNullOrEmpty(line)) return;
        NpcMover m = NpcView.GetMover(n.id);
        if (m != null) NpcSpeechBubble.Show(m.transform, line, 4f);
        Log(n, LF("intent_say", n.npcName, line), false);
    }

    // 写进行为日志；memory = true 时也写进他自己的记忆（和玩家聊天时会想起来）。
    static void Log(NpcData n, string text, bool memory)
    {
        if (n == null || string.IsNullOrEmpty(text)) return;
        if (memory && World != null) IdleChatRules.AddMemory(n, World.day, text);
        if (LogManager.Instance != null) LogManager.Instance.AddLog(n.npcName, text, n.id);
    }

    // ============ 小工具 ============

    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;
    static NpcData Npc(string id) => WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(id) : null;

    static NpcTaskRunner Runner(string id)
    {
        NpcMover m = NpcView.GetMover(id);
        return m != null ? m.GetComponent<NpcTaskRunner>() : null;
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;
}
