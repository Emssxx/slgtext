using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Newtonsoft.Json.Linq;

// ============================================================================
// 领导与分组（社会系统第 4 阶段）。
//   【第一天就选出领导】（开场 OpeningDirector）：开场第二场戏大家投票，实际由观察者（4B）读数值挑一个人（PickLeader）
//     → 代码标记他为领导（CrownChosen）→ 他用主模型（14B）说一句话 → 马上分工。从此 14B 主要扮演领导：
//     给大家分工、定建造项目、说话；4B 当隐形参谋（CampAdvisor）只给领导出主意。其他 NPC 不再自己调 AI（见 NpcIntentBrain）。
//     领导死了 / 崩解了：马上重新推举（Emerge）。没有 OpenDirector 的场景照旧按下面的老规则（第 3~6 天）出现领导。
//   领导在想分工时（主模型在写）站着「正在思考……」：IsThinking(npc)，以后可以接思考动画。
//   领导怎么出现（4B 小模型当"观察者"）：
//     · 前几天大家还互相不熟，不会有领导（最早第 leaderMinDay 天）。
//     · 到了之后，要等一个"契机"才会有人站出来：一半以上的人安全感不足 / 刚有危险的东西靠近 /
//       已经有人开始请别人帮忙（第一次集体行动）；实在没有契机，到第 leaderLatestDay 天也会出现。
//     · 观察者读每个人生成时的数值（性格 5 维、词条、职业、身心状态），判断谁最适合当领导，给一句理由。
//       现在 NPC 的数值是固定的，所以领导也是固定的（只有领导死了 / 崩解了才重新看）；以后有成长系统再定期重看。
//       小模型没装 / 失败时，用代码按同样的数值打分。
//     · 出现时：大家聚到营地中心，日志写"这几天大家渐渐开始听 X 的——理由"，其他人对他的尊重 +3，他说一句话。
//   分工（AI，一次调用安排所有人）：
//     · 领导用主模型看营地现状、每个人的状态，以及营地参谋（CampAdvisor，隐形的 4B）整理的"营地眼下最要紧的事"，
//       给每个人分一个组：build（修建）/ haul（搬运：运材料放蓝图、打水）/ gather（采集，带材料）/ scavenge（找吃的和药）
//       / care（照顾伤病员）/ rest（休整）；还可以定一个"建造项目"（project：今天优先建什么，搬运组运料放蓝图、建造组施工）。
//     · 什么时候重新安排（不按"天"——5 秒一天时每天一次太频繁）：刚当上领导马上排；参谋发现新的紧急情况隔 planOnUrgentGap 秒；
//       参谋的看法变了隔 planOnChangeGap 秒；什么都没变每 planRefresh 秒重新看一次。
//     · 没换活的人不用再问听不听；有人换了活 / 不服 / 换了项目才写日志、领导才开口（免得刷屏）。
//       不服的人 refuseCooldown 秒内不再派他（免得一次次掷骰、关系一次次变差）。
//     · 听不听安排由代码算（对领导的尊重为主）：不服的人当天不按分工干，领导对他的好感 / 信任下降。
//     · 领导自己也必须干活（以身作则）：AI 没给他分活、或者让身体好好的他去休息，代码改成建造组。
//     · AI 漏了的人由代码补上分工；AI 没回 / 回的不能用时，整份分工由代码按规则排（AutoAssign），保证每天都有分工。
//     · 有分工的人就只干自己的活：不再自己琢磨（NpcIntentBrain 的"安全感不足 → 自己想"只给没分工的人），
//       仓库食物见底时也不全员出去找吃的（交给搜寻组，见 NpcBehaviorDriver）。
//     · 分工只管"闲下来的时候干什么"：需求（饿 / 累 / 逃命）、玩家指令、自己的计划（安全感不足）都比分工优先。
//       具体怎么干由 NpcIntentBrain.DoRoleWork 按分工用代码执行，不再调用 AI。
// 由 NpcBehaviorDriver.Start 自动挂上。
// ============================================================================
public class NpcSociety : MonoBehaviour
{
    public static NpcSociety Instance { get; private set; }

    [Header("领导（4B 观察者）")]
    public bool leadersEnabled = true;
    public int minMembers = 3;            // 至少几个人才会有领导
    public int leaderMinDay = 3;          // 最早第几天（前几天大家还互相不熟）
    public int leaderLatestDay = 6;       // 到这天还没有契机，也会有人站出来
    public float observeInterval = 20f;   // 多久看一次有没有契机（秒）
    public float meetingWait = 20f;       // 聚到营地中心最多等多少秒

    [Header("分工")]
    public bool dailyPlan = true;
    [Range(0f, 100f)] public float obeyThreshold = 45f; // 听从安排的门槛（看对领导的尊重为主）
    public float planOnUrgentGap = 8f;    // 参谋发现新的紧急情况：距上次安排至少隔几秒就重新安排
    public float planOnChangeGap = 25f;   // 参谋的看法变了：距上次至少隔几秒才重新安排
    public float planRefresh = 90f;       // 什么都没变：每隔几秒重新看一次
    public float refuseCooldown = 90f;    // 不服安排的人，几秒内不再派他

    public static readonly string[] Roles = { "build", "haul", "gather", "scavenge", "care", "rest" };

    bool _meeting;
    float _nextTry;
    int _plannedDay = -1;
    bool _planPending;
    float _lastPlanTime = -999f;
    int _plannedAdviceVersion = -1, _plannedUrgentVersion = -1;
    string _plannedLeaderId;
    readonly Dictionary<string, float> _refusedAt = new Dictionary<string, float>();
    static string _projectId;   // 建造项目（BuildableData.id），放下蓝图后清空
    int _helpRequests;   // 营地里一共发生过几次"请人帮忙"（第一次集体行动 = 出现领导的契机）

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 新的一局：重置（领导存在 WorldState 里，新世界自然没有领导）。
    public static void ClearAll()
    {
        if (Instance == null) return;
        Instance.StopAllCoroutines();
        Instance._meeting = false;
        Instance._plannedDay = -1;
        Instance._planPending = false;
        Instance._helpRequests = 0;
        Instance._plannedLeaderId = null;
        Instance._lastPlanTime = -999f;
        Instance._refusedAt.Clear();
        _projectId = null;
    }

    // ============ 建造项目 ============

    // 现在的建造项目（没有 / 已经不能建了返回 null）。
    public static BuildableData Project => string.IsNullOrEmpty(_projectId) ? null : NpcIntentBrain.FindCandidate(_projectId);

    // 项目的蓝图放下了（NpcIntentBrain 搬运时调）：项目完成，之后由建造组施工。
    public static void ProjectPlaced(BuildableData d)
    {
        if (d != null && _projectId == d.id) _projectId = null;
    }

    // 定一个新项目。空 / 围墙 / 不能建 → 保持原来的。换了返回 true。
    bool SetProject(string id, NpcData leader)
    {
        BuildableData d = string.IsNullOrEmpty(id) || id == "wall" ? null : NpcIntentBrain.FindCandidate(id);
        if (d == null || _projectId == d.id) return false;
        _projectId = d.id;
        if (leader != null) Announce(LF("society_project", leader.npcName, d.LocalizedName));
        return true;
    }

    // 有人请别人帮忙了（NpcIntentBrain 调）：集体行动开始了，是出现领导的契机之一。
    public static void NoteHelpRequest()
    {
        if (Instance != null) Instance._helpRequests++;
    }

    // ============ 对外查询 ============

    public static NpcData Leader
    {
        get
        {
            WorldState w = World;
            if (w == null || string.IsNullOrEmpty(w.leaderId)) return null;
            NpcData n = WorldManager.Instance.GetNpcById(w.leaderId);
            return n != null && !n.IsDead && !n.isBroken ? n : null;
        }
    }

    public static bool IsLeader(NpcData n) => n != null && Leader == n;

    // 有没有（听从了的）分工。分工一直有效，直到下次早会换掉（早会没开成也照旧干昨天的活）。
    public static bool HasRole(NpcData n) => n != null && !string.IsNullOrEmpty(n.role);

    public static string RoleName(string role) => string.IsNullOrEmpty(role) ? "" : L("role_" + role);

    // 领导正在想分工（主模型在写）。这段时间他手上的事做完就站着思考（NpcIntentBrain 不派他新活）。
    public static bool Thinking => Instance != null && Instance._planPending && Leader != null;
    // 这个人是不是正在思考的领导（以后的思考动画读这个）。
    public static bool IsThinking(NpcData n) => n != null && Thinking && IsLeader(n);

    // ============ 开场：第一天投票选领导（OpeningDirector）============

    // 观察者（4B）读大家的数值挑领导；4B 用不了 / 没给结果时，代码按同样的数值挑。done(领导, 理由)；没人活着 done(null, null)。
    public static void PickLeader(Action<NpcData, string> done)
    {
        List<NpcData> alive = Alive();
        if (alive.Count == 0) { done(null, null); return; }
        if (alive.Count == 1 || Instance == null || !Instance.leadersEnabled) { NpcData b = BestByStats(); done(b, StatReason(b)); return; }
        Instance.RequestObserver((id, why) =>
        {
            NpcData n = id != null && WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(id) : null;
            if (n == null || n.IsDead || n.isBroken) { n = BestByStats(); why = null; }
            done(n, n != null && string.IsNullOrEmpty(why) ? StatReason(n) : why);
        });
    }

    // 除了 leader 以外数值最高的人（开场投票时，领导自己投给他）。
    public static NpcData RunnerUp(NpcData leader)
    {
        NpcData best = null;
        float bestScore = float.MinValue;
        foreach (NpcData n in Alive())
        {
            if (n == leader) continue;
            float sc = StatScore(n);
            if (sc > bestScore) { bestScore = sc; best = n; }
        }
        return best;
    }

    // 开场投票的结果：标记领导、大家对他的尊重 +3、他用主模型说一句话、马上分工。votes < 0 = 没有投票（直接推举）。
    public static void CrownChosen(NpcData leader, string reason, int votes, int total)
    {
        if (Instance == null || leader == null) return;
        Instance.Crown(leader, reason, votes, total, true);
    }

    // ============ 每帧 ============

    void Update()
    {
        WorldState w = World;
        if (w == null || _meeting) return;
        if (OpeningDirector.Playing) return;   // 开场演出中：领导由开场选

        // 第一天的领导由开场选（OpeningDirector）；之后领导不在了（死亡 / 崩解）就马上重新推举。
        // 没有开场的场景：照旧等第 leaderMinDay 天以后的契机。
        bool reelect = w.lastElectionDay > 0;
        if (leadersEnabled && Leader == null && !OpeningDirector.HandlesElection && Time.time >= _nextTry && Alive().Count >= minMembers
            && (reelect || w.day >= leaderMinDay))
        {
            _nextTry = Time.time + observeInterval;
            if (reelect || HasMoment(w)) StartCoroutine(Emerge());
            return;
        }

        NpcData leader = Leader;
        if (dailyPlan && leader != null && !_planPending && PlanDue(leader)) RequestPlan();
    }

    // 该不该重新安排分工（见文件开头）。
    bool PlanDue(NpcData leader)
    {
        float since = Time.time - _lastPlanTime;
        if (_plannedLeaderId != leader.id) return true;                                                  // 刚当上领导 / 还没安排过
        if (CampAdvisor.UrgentVersion != _plannedUrgentVersion && since >= planOnUrgentGap) return true; // 新的紧急情况
        if (CampAdvisor.Version != _plannedAdviceVersion && since >= planOnChangeGap) return true;       // 参谋的看法变了
        return since >= planRefresh;
    }

    // 有没有让领导站出来的契机。
    bool HasMoment(WorldState w)
    {
        if (w.day >= leaderLatestDay) return true;
        if (_helpRequests > 0) return true;                 // 已经有人开始组织大家干活
        if (NpcSecurity.Danger >= 8f) return true;          // 刚有危险的东西靠近 / 诡异的事
        List<NpcData> alive = Alive();
        int uneasy = 0;
        foreach (NpcData n in alive) if (NpcSecurity.Insecure(n)) uneasy++;
        return uneasy * 2 >= alive.Count;                    // 一半以上的人心里不安
    }

    // ============ 领导出现 ============

    IEnumerator Emerge()
    {
        _meeting = true;
        // 1. 观察者（小模型）判断谁最适合；结果回来之前大家先聚过来
        string pickId = null, reason = null;
        bool done = false;
        RequestObserver((id, why) => { pickId = id; reason = why; done = true; });

        Announce(L("society_meeting_start"));
        List<string> called = new List<string>();
        NpcIntentBrain brain = NpcIntentBrain.Instance;
        foreach (NpcData n in Alive())
            if (brain != null && brain.Summon(n.id)) called.Add(n.id);

        float t = 0f;
        while (t < meetingWait)
        {
            bool walking = false;
            foreach (string id in called) if (NpcIntentBrain.IsSummoned(id)) { walking = true; break; }
            if (!walking && done) break;
            t += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        while (!done && t < meetingWait + 15f) { t += 0.5f; yield return new WaitForSeconds(0.5f); }

        // 2. 观察者没给出结果：用代码按同样的数值打分
        NpcData leader = pickId != null ? WorldManager.Instance.GetNpcById(pickId) : null;
        if (leader == null || leader.IsDead || leader.isBroken) { leader = BestByStats(); reason = null; }
        if (leader != null) Crown(leader, string.IsNullOrEmpty(reason) ? StatReason(leader) : reason);
        _meeting = false;
    }

    // 3. 宣布：日志 + 尊重 + 他说一句话（主模型，14B 扮演领导）+ 马上分工。
    //    votes >= 0：开场投票选出的（total = 投票人数）；votes < 0：大家慢慢推举出来的 / 领导不在了重新推举。
    void Crown(NpcData leader, string reason, int votes = -1, int total = 0, bool chosen = false)
    {
        WorldState w = World;
        if (w == null) return;
        NpcData old = !string.IsNullOrEmpty(w.leaderId) && w.leaderId != leader.id && WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(w.leaderId) : null;
        bool reelected = old != null && w.lastElectionDay > 0;
        w.leaderId = leader.id;
        w.lastElectionDay = Mathf.Max(1, w.day);
        string situation;
        if (votes >= 0)
        {
            Announce(LF("society_voted", leader.npcName, votes, total, reason));
            situation = LanguageGuard.Pick(
                $"【现在】大家刚刚在营地里投票，选你当这群人的领导（你得了 {votes}/{total} 票）。所有人都看着你，等你开口。说一两句你此刻想对大家说的话。",
                $"【今】みんなはたった今、拠点で投票し、あなたをこの集団のリーダーに選んだ（{total}票中{votes}票）。全員があなたの言葉を待っている。今みんなに言いたいことを一、二言。");
        }
        else if (reelected)
        {
            Announce(LF("society_reelected", old.npcName, leader.npcName, reason));
            situation = LanguageGuard.Pick(
                $"【现在】{old.npcName} 不在了，大家推举你接着带头。说一两句你此刻想对大家说的话。",
                $"【今】{old.npcName}がいなくなり、みんなはあなたに後を任せた。今みんなに言いたいことを一、二言。");
        }
        else if (chosen)
        {
            Announce(LF("society_chosen", leader.npcName, reason));
            situation = LanguageGuard.Pick("【现在】大家刚刚推举你带头，所有人都看着你，等你开口。说一两句你此刻想对大家说的话。",
                                           "【今】みんなはたった今、あなたに先頭を任せた。全員があなたの言葉を待っている。今みんなに言いたいことを一、二言。");
        }
        else
        {
            Announce(LF("society_emerged", leader.npcName, reason));
            situation = LanguageGuard.Pick("【现在】这几天营地里的人渐渐开始听你的，今天大家聚过来等你拿主意。说一两句你此刻想说的话。",
                                           "【今】ここ数日、拠点のみんなは少しずつあなたの言うことを聞くようになり、今日はみんなが集まってあなたの判断を待っている。今言いたいことを一、二言。");
        }
        foreach (NpcData o in Alive())
            if (o != leader) RelationshipManager.Apply(o, leader, 0f, 1f, 3f, LF("rel_reason_elected", w.day, leader.npcName));
        SpeakMain(leader, situation, L(votes >= 0 ? "society_vote_line" : "society_leader_line"));
        _plannedDay = -1; // 马上分工
        _plannedLeaderId = null;
    }

    // ============ 观察者（4B）============

    // 把每个人生成时的数值交给小模型，判断谁最适合当领导。done(npcId, 理由)；失败 done(null, null)。
    void RequestObserver(Action<string, string> done)
    {
        List<NpcData> people = Alive();
        bool jp = LanguageGuard.IsJp;
        StringBuilder sb = new StringBuilder();
        foreach (NpcData n in people)
        {
            Personality p = n.personality ?? new Personality();
            sb.Append("・").Append(n.npcName);
            if (!string.IsNullOrEmpty(n.profession)) sb.Append("（").Append(n.profession).Append("）");
            sb.Append("：勇敢 ").Append(p.bravery).Append("，利他 ").Append(p.altruism).Append("，谨慎 ").Append(p.caution)
              .Append("，外向 ").Append(p.sociability).Append("，乐观 ").Append(p.optimism);
            if (n.traits != null && n.traits.Count > 0) sb.Append("；词条：").Append(string.Join("、", n.traits));
            sb.Append("；生命 ").Append(Mathf.RoundToInt(n.health)).Append("，理智 ").Append(Mathf.RoundToInt(n.sanity)).AppendLine();
        }
        string list = sb.ToString();
        string sys =
            "你是营地的观察者，不属于任何一方。下面是营地里每个幸存者的数据（数值 0~100）。\n" +
            "判断谁最适合当这群人的领导：遇事冷静、敢担当、肯为大家出力、做事有分寸、身心状态撑得住；只看数据，不要凭名字猜。\n" +
            "只输出一个 JSON 对象，不要任何多余文字：{\"leader\":\"名字\",\"reason\":\"为什么是他（一句话，20 字以内，" + (jp ? "用日语写" : "用中文写") + "）\"}";
        bool queued = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.LeaderObserve, key = "observer", json = true, maxTokens = 80, languageAnchor = true,
            system = () => sys,
            user = () => list,
            onResult = raw =>
            {
                string id = null, why = null;
                try
                {
                    int a = raw != null ? raw.IndexOf('{') : -1, b = raw != null ? raw.LastIndexOf('}') : -1;
                    if (a >= 0 && b > a)
                    {
                        JObject o = JObject.Parse(raw.Substring(a, b - a + 1));
                        NpcData n = FindByName((o.Value<string>("leader") ?? "").Trim());
                        if (n != null) { id = n.id; why = (o.Value<string>("reason") ?? "").Trim(); }
                    }
                }
                catch { }
                if (id != null) Debug.Log("[观察者] 最适合当领导：" + WorldManager.Instance.GetNpcById(id).npcName + "——" + why);
                done(id, why);
            },
        });
        if (!queued) done(null, null);
    }

    // 小模型用不了时：按同样的数值打分（勇敢 30% + 利他 25% + 乐观 15% + 谨慎 15% + 外向 15%，词条加减，身心状态打折）。
    static NpcData BestByStats()
    {
        NpcData best = null;
        float bestScore = float.MinValue;
        foreach (NpcData n in Alive())
        {
            float s = StatScore(n);
            if (s > bestScore) { bestScore = s; best = n; }
        }
        return best;
    }

    static float StatScore(NpcData n)
    {
        Personality p = n.personality ?? new Personality();
        float s = 0.3f * p.bravery + 0.25f * p.altruism + 0.15f * p.optimism + 0.15f * p.caution + 0.15f * p.sociability;
        if (n.traits != null)
            foreach (string t in n.traits)
            {
                if (t == "勇敢" || t == "冷静" || t == "可靠" || t == "领导力") s += 8f;
                if (t == "怯懦" || t == "暴躁" || t == "自私" || t == "多疑") s -= 8f;
            }
        s *= Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.health / 100f)) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.sanity / 100f));
        return s;
    }

    static string StatReason(NpcData n)
    {
        Personality p = n.personality ?? new Personality();
        List<string> parts = new List<string>();
        bool jp = LanguageGuard.IsJp;
        if (p.bravery >= 60) parts.Add(jp ? "肝が据わっている" : "胆子大");
        if (p.altruism >= 60) parts.Add(jp ? "人のために動ける" : "肯为大家出力");
        if (p.caution >= 60) parts.Add(jp ? "慎重だ" : "做事稳");
        if (p.optimism >= 60) parts.Add(jp ? "前向きだ" : "不轻易泄气");
        if (parts.Count == 0) parts.Add(jp ? "いちばん落ち着いている" : "最沉得住气");
        return string.Join(jp ? "、" : "、", parts);
    }

    // ============ 分工 ============

    void RequestPlan()
    {
        NpcData leader = Leader;
        WorldState w = World;
        if (leader == null || w == null) return;
        int day = w.day;
        string lid = leader.id;
        bool replan = _plannedLeaderId == lid;   // 已经安排过一次了：这次是调整
        _plannedLeaderId = lid;
        _lastPlanTime = Time.time;
        _plannedAdviceVersion = CampAdvisor.Version;
        _plannedUrgentVersion = CampAdvisor.UrgentVersion;
        _planPending = true;
        bool queued = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.LeaderPlan, key = lid, json = true,
            system = () => PlanPrompt(WorldManager.Instance.GetNpcById(lid), replan),
            user = () => replan ? LanguageGuard.Pick("看看要不要调整分工。", "割り振りを見直して。") : LanguageGuard.Pick("给大家分工。", "みんなに仕事を割り振って。"),
            onResult = raw =>
            {
                _planPending = false;
                _plannedDay = day;
                NpcData l = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(lid) : null;
                if (l != null && l.currentAction == L("leader_thinking")) l.currentAction = "";   // 想完了
                if (!ApplyPlan(l, raw, day, replan)) AutoPlan(l, day, replan); // AI 没回 / 回的不能用：按规则分
            },
        });
        if (!queued) { _planPending = false; _plannedDay = day; AutoPlan(leader, day, replan); } // 调度器拒绝：按规则分
    }

    // 这个人刚不服过安排、还在冷静期：这次不派他。
    bool RecentlyRefused(NpcData n)
    {
        return n != null && _refusedAt.TryGetValue(n.id, out float t) && Time.time - t < refuseCooldown;
    }

    void Refuse(NpcData n, NpcData leader, int day, List<string> refused)
    {
        n.role = null;
        refused.Add(n.npcName);
        _refusedAt[n.id] = Time.time;
        RelationshipManager.Apply(leader, n, -3f, -2f, 0f, LF("rel_reason_disobeyed", day, n.npcName));
    }

    // ============ 代码分工（AI 用不了时，或补 AI 漏掉的人）============

    // 按规则给所有人分工：伤病 / 太累 → 休整；有伤员又有药 → 1 人医护；食物少 → 1~2 人搜寻；
    // 剩下的：营地还没围起来 → 一半建造（领导带头）、一半采石头；围好了 → 采最缺的材料。
    void AutoPlan(NpcData leader, int day, bool replan = false)
    {
        if (leader == null || leader.IsDead) return;
        Dictionary<NpcData, string> plan = AutoAssign(Alive(), leader);
        Dictionary<string, List<string>> byRole = new Dictionary<string, List<string>>();
        List<string> refused = new List<string>();
        int changed = 0;
        foreach (KeyValuePair<NpcData, string> kv in plan)
        {
            NpcData n = kv.Key;
            if (n.role == kv.Value) { ListRole(n, byRole); continue; }        // 没换活：不用再问听不听
            if (n != leader && RecentlyRefused(n)) continue;
            if (n != leader && !Obeys(n, leader)) { Refuse(n, leader, day, refused); continue; }
            SetRole(n, kv.Value, day, byRole);
            changed++;
        }
        // 建造项目：参谋认为今天之内该建的东西
        CampAdvisor.Advice b = CampAdvisor.TopBuild(2);
        bool projectChanged = b != null && SetProject(b.target, leader);
        if (!replan || changed > 0 || refused.Count > 0 || projectChanged)
        {
            Speak(leader, null, L("society_plan_line"));
            AnnouncePlan(leader, byRole, refused, replan);
        }
    }

    Dictionary<NpcData, string> AutoAssign(List<NpcData> people, NpcData leader)
    {
        Dictionary<NpcData, string> result = new Dictionary<NpcData, string>();
        List<NpcData> workers = new List<NpcData>();
        foreach (NpcData p in people)
        {
            if (!NpcHealth.CanWork(p) || p.energy < 25f) result[p] = "rest";
            else workers.Add(p);
        }
        WorldState w = World;
        // 医护：有伤员又有药，挑最肯帮人的（尽量不是领导）
        bool hurt = false;
        foreach (NpcData p in people)
            if (NpcHealth.Has(p, HealthTag.Injured) || NpcHealth.Has(p, HealthTag.Sick) || NpcHealth.Has(p, HealthTag.CriticalWound)) { hurt = true; break; }
        bool med = ItemDatabase.Instance != null && w != null && ItemDatabase.Instance.FindFirstInStorage(ItemCategory.Medical, w) != null;
        if (!med) foreach (NpcData p in people) if (NpcInventory.Has(p, "medkit")) { med = true; break; }   // 有人身上带着医疗包也算
        if (hurt && med && workers.Count > 1) TakeBest(workers, result, leader, "care", p => p.personality.altruism);
        // 搜寻：食物少于 4 份 → 1 人（5 人以上 → 2 人），挑最胆大的
        int food = ItemDatabase.Instance != null && w != null ? ItemDatabase.Instance.CountInStorage(ItemCategory.Food, w) : 0;
        int scav = food < 4 ? (workers.Count >= 5 ? 2 : 1) : 0;
        if (scav == 0 && CampAdvisor.Top("scavenge", 2) != null) scav = 1;   // 参谋说要出去找（食物 / 药）
        for (int i = 0; i < scav && workers.Count > 1; i++) TakeBest(workers, result, leader, "scavenge", p => p.personality.bravery);
        // 搬运：参谋说要打水（有收集器和净化器），或者有建造项目要运料 → 1 人，挑体力最好的
        bool water = CampAdvisor.Top("haul_water", 2) != null && WaterWorks.CollectorCount > 0 && WaterWorks.PurifierCount > 0;
        bool project = Project != null || CampAdvisor.TopBuild(2) != null;
        if ((water || project) && workers.Count > 1) TakeBest(workers, result, leader, "haul", p => p.energy);
        // 剩下的：修墙 / 采材料
        CampZone z = CampZone.Instance;
        BuildableData wall = CampZone.WallData();
        bool needWalls = z != null && z.Ready && !z.Enclosed && wall != null && ProgressionRules.IsBuildableUnlocked(wall, out _);
        if (workers.Contains(leader)) { result[leader] = needWalls ? "build" : "gather"; workers.Remove(leader); }
        int builders = needWalls ? Mathf.CeilToInt(workers.Count * 0.5f) : 0;
        foreach (NpcData p in workers)
        {
            if (builders > 0) { result[p] = "build"; builders--; }
            else result[p] = "gather";
        }
        return result;
    }

    // 从 workers 里挑某项最高的人（尽量不挑领导）分去 role。
    static void TakeBest(List<NpcData> workers, Dictionary<NpcData, string> result, NpcData leader, string role, Func<NpcData, float> score)
    {
        NpcData best = null;
        foreach (NpcData p in workers)
            if (p != leader && (best == null || score(p) > score(best))) best = p;
        if (best == null) return;
        result[best] = role;
        workers.Remove(best);
    }

    // 修墙要石头：采集组默认采石头（营地围好了就采最缺的）。
    static string DefaultGatherTarget()
    {
        CampZone z = CampZone.Instance;
        if (z != null && z.Ready && !z.Enclosed) return ResourceInfo.StoneItem;
        WorldState w = World;
        string best = ResourceInfo.StoneItem;
        int min = int.MaxValue;
        foreach (string m in new[] { ResourceInfo.StoneItem, ResourceInfo.WoodItem, ResourceInfo.OreItem })
        {
            int have = w != null ? ItemUseRules.GetCount(m, w) : 0;
            if (have < min) { min = have; best = m; }
        }
        return best;
    }

    void SetRole(NpcData n, string role, int day, Dictionary<string, List<string>> byRole, string target = null)
    {
        n.role = role;
        n.roleTarget = role == "gather" ? (string.IsNullOrEmpty(target) ? DefaultGatherTarget() : target) : "";
        n.roleDay = day;
        ListRole(n, byRole);
    }

    // 记进"这次分工"的名单（宣布用），不改他的分工。
    static void ListRole(NpcData n, Dictionary<string, List<string>> byRole)
    {
        if (n == null || string.IsNullOrEmpty(n.role)) return;
        if (!byRole.TryGetValue(n.role, out List<string> list)) { list = new List<string>(); byRole[n.role] = list; }
        list.Add(n.npcName + (n.role == "gather" ? "（" + ItemName(n.roleTarget) + "）" : ""));
    }

    void AnnouncePlan(NpcData leader, Dictionary<string, List<string>> byRole, List<string> refused, bool replan = false)
    {
        List<string> parts = new List<string>();
        foreach (string r in Roles)
            if (byRole.TryGetValue(r, out List<string> names)) parts.Add(RoleName(r) + "：" + string.Join("、", names));
        if (parts.Count > 0) Announce(LF(replan ? "society_replan" : "society_plan", leader.npcName, string.Join(LanguageGuard.Pick("；", "／"), parts)));
        foreach (string name in refused) Announce(LF("society_refused", name, leader.npcName));
    }

    string PlanPrompt(NpcData leader, bool replan = false)
    {
        if (leader == null) throw new Exception("领导不存在");
        WorldState w = World;
        bool jp = LanguageGuard.IsJp;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(NpcActors.Render(leader));
        sb.AppendLine();
        if (replan)
            sb.AppendLine(jp ? $"あなたは拠点のみんなに選ばれたリーダーだ。今は{w.day}日目。拠点の状況が変わってきたので、みんなの割り振りを見直す（変える必要のない人はそのままでいい）。"
                             : $"你是营地里大家推举出来的领导。现在是第 {w.day} 天，营地的情况有了变化，你要看看要不要调整大家的分工（没必要换的人就别换）。");
        else
            sb.AppendLine(jp ? $"あなたは拠点のみんなに選ばれたリーダーだ。今は{w.day}日目の朝。みんなに今日の仕事を割り振る。"
                             : $"你是营地里大家推举出来的领导。现在是第 {w.day} 天早上，你要给大家分配今天的工作。");
        sb.AppendLine();
        sb.AppendLine(jp ? "【拠点の今の状況】" : "【营地现状】");
        sb.Append(NpcIntentBrain.CampStatusText(leader, jp));
        sb.AppendLine(jp ? $"拠点の囲い：{Mathf.RoundToInt((NpcSecurity.Instance != null ? NpcSecurity.Instance.Coverage : 0f) * 100f)}%" + (NpcSecurity.Instance != null && NpcSecurity.Instance.Enclosed ? "（囲まれている）" : "")
                         : $"营地围合：{Mathf.RoundToInt((NpcSecurity.Instance != null ? NpcSecurity.Instance.Coverage : 0f) * 100f)}%" + (NpcSecurity.Instance != null && NpcSecurity.Instance.Enclosed ? "（已经围起来了）" : ""));
        BuildableData project = Project;
        if (project != null) sb.AppendLine(jp ? $"今の建設プロジェクト：{project.LocalizedName}（まだ設計図を置いていない）" : $"现在的建造项目：{project.LocalizedName}（还没放下蓝图）");
        sb.AppendLine();
        string concerns = CampAdvisor.PromptBlock(jp); // 营地眼下最要紧的事（隐形的营地参谋整理的）
        if (concerns.Length > 0) { sb.Append(concerns); sb.AppendLine(); }
        sb.AppendLine(jp ? "【みんなの様子】" : "【大家的情况】");
        foreach (NpcData n in Alive())
        {
            sb.Append("・").Append(n.npcName);
            if (!string.IsNullOrEmpty(n.profession)) sb.Append("（").Append(n.profession).Append("）");
            if (n == leader) sb.Append(jp ? "＝あなた" : "＝你自己");
            sb.Append(jp ? "：生命 " : "：生命 ").Append(Mathf.RoundToInt(n.health))
              .Append(jp ? "／体力 " : "，体力 ").Append(Mathf.RoundToInt(n.energy))
              .Append(jp ? "／安心感 " : "，安全感 ").Append(Mathf.RoundToInt(n.security));
            List<HealthTag> tags = NpcHealth.Tags(n);
            if (tags.Count > 0)
            {
                List<string> names = new List<string>();
                foreach (HealthTag t in tags) names.Add(NpcHealth.TagName(n, t, jp));
                sb.Append(jp ? "／" : "，").Append(string.Join("・", names));
            }
            if (n != leader) sb.Append(jp ? "／あなたの気持ち：" : "，你对他的感觉：").Append(RelationshipManager.Label(leader, n.id));
            sb.Append(jp ? "／今：" : "，现在：").Append(HasRole(n) ? RoleName(n.role) + (n.role == "gather" ? "（" + ItemName(n.roleTarget) + "）" : "") : (jp ? "割り振りなし" : "没有分工"));
            sb.AppendLine();
        }
        sb.AppendLine();
        string buildList = NpcIntentBrain.BuildListText(jp, true);
        if (jp)
        {
            sb.AppendLine("【割り振れる仕事】build（建設：置いてある設計図を建てに行く。ふだんは拠点のまわりの壁を建てる）／haul（運搬：資材を運んで設計図を置く、湖で水を汲んで雨水タンクへ運ぶ）／gather（資源を採る。target に wood / stone / ore / fiber。壁には石が要る）／scavenge（拠点の外で食べ物や薬を探す）／care（けが人・病人の世話）／rest（休ませる）");
            if (buildList.Length > 0)
            {
                sb.AppendLine("【建設プロジェクト】project に、拠点で優先して建てる物を一つ書ける（下の英字IDから選ぶ。要らなければ空）。決めると運搬班が資材を運んで設計図を置き、建設班が建てる。資材が足りなければ採集する人が先に採る。壁は書かなくていい（建設班がふだんから建てる）。");
                sb.Append(buildList);
            }
            sb.AppendLine("あなた自身も含めて、全員に一つずつ割り振る。【拠点で今いちばん急ぐこと】の「急ぎ」をまず片づけ、それから体の状態・職業を見て決める。けがや病気の人は rest か care にする。");
            sb.AppendLine("リーダーのあなたも必ず手を動かすこと（指示するだけで休むのはだめ。率先して一番きつい仕事を取ってもいい）。");
            sb.AppendLine("【出力形式】JSONオブジェクトを1つだけ出力し、それ以外は何も書かないこと：");
            sb.AppendLine("{\"groups\":[{\"name\":\"名前\",\"role\":\"上のどれか（英字）\",\"target\":\"gatherのときの資源\"}],\"project\":\"建てる物の英字ID（なければ空）\",\"speech\":\"みんなに言う一言（短く）\"}");
        }
        else
        {
            sb.AppendLine("【可以分配的工作】build（修建：去施工已经放下的蓝图，平时修营地那一圈围墙）／haul（搬运：把材料运过去放下蓝图、去湖边打水倒进雨水收集器）／gather（采集，target 填 wood / stone / ore / fiber；修墙要石头）／scavenge（出营地找吃的和药）／care（照顾伤病的人）／rest（休整）");
            if (buildList.Length > 0)
            {
                sb.AppendLine("【建造项目】project 可以填一样营地优先要建的东西（从下面的英文 id 里选；不需要就留空）。定了以后搬运组会运材料、放蓝图，建造组去施工；材料不够时采集的人会先去采。围墙不用填，建造组平时就会修。");
                sb.Append(buildList);
            }
            sb.AppendLine("每个人分一项，包括你自己。先顾【营地眼下最要紧的事】里「紧急」的事，再按每个人的身体状况、职业来分。受伤、生病的人分 rest 或让别人 care。");
            sb.AppendLine("你是领导，自己也必须干活，以身作则（不能只指挥、自己休息；可以带头去干最累的活）。");
            sb.AppendLine("【输出格式】只输出一个 JSON 对象，不要多余文字：");
            sb.AppendLine("{\"groups\":[{\"name\":\"名字\",\"role\":\"上面之一（英文）\",\"target\":\"gather 时要采的东西\"}],\"project\":\"要建的英文 id（不需要就留空）\",\"speech\":\"你对大家说的一两句话（简短）\"}");
        }
        if (LocalizationManager.Instance != null) sb.AppendLine(LocalizationManager.Instance.GetAILanguageInstruction());
        return sb.ToString();
    }

    // 解析分工 → 每个人决定听不听 → 写日志。AI 的结果不能用返回 false（调用方改用代码分工）。
    bool ApplyPlan(NpcData leader, string raw, int day, bool replan = false)
    {
        WorldState w = World;
        if (leader == null || w == null || leader.IsDead || string.IsNullOrEmpty(raw)) return false;
        JObject o;
        try
        {
            int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
            if (a < 0 || b <= a) return false;
            o = JObject.Parse(raw.Substring(a, b - a + 1));
        }
        catch { return false; }
        JArray groups = o["groups"] as JArray;
        if (groups == null || groups.Count == 0) return false;

        string speech = (o.Value<string>("speech") ?? "").Trim();
        string projectId = (o.Value<string>("project") ?? "").Trim().ToLowerInvariant();

        Dictionary<string, List<string>> byRole = new Dictionary<string, List<string>>();
        List<string> refused = new List<string>();
        List<NpcData> assigned = new List<NpcData>();
        int changed = 0;
        foreach (JToken t in groups)
        {
            JObject g = t as JObject;
            if (g == null) continue;
            NpcData n = FindByName((g.Value<string>("name") ?? "").Trim());
            string role = (g.Value<string>("role") ?? "").Trim().ToLowerInvariant();
            if (n == null || Array.IndexOf(Roles, role) < 0 || assigned.Contains(n)) continue;
            string target = (g.Value<string>("target") ?? "").Trim().ToLowerInvariant();
            if (target != ResourceInfo.WoodItem && target != ResourceInfo.StoneItem && target != ResourceInfo.OreItem && target != ResourceInfo.FiberItem) target = "";

            assigned.Add(n);
            // 领导自己也要干活（以身作则）：身体没问题却被分去休息 → 改成建造组
            if (n == leader && role == "rest" && n.energy >= 40f && NpcHealth.CanWork(n)) role = "build";
            if (n.role == role)
            {
                // 没换活：不用再问听不听（采集换一种材料也算没换活）
                if (role == "gather" && target.Length > 0 && n.roleTarget != target) { n.roleTarget = target; changed++; }
                ListRole(n, byRole);
                continue;
            }
            if (n != leader && RecentlyRefused(n)) continue;        // 刚不服过：这次先不派他
            if (n != leader && !Obeys(n, leader))
            {
                Refuse(n, leader, day, refused);
                RelationshipManager.Apply(n, leader, 0f, 0f, -2f, null);
                continue;
            }
            SetRole(n, role, day, byRole, target);
            changed++;
            if (n != leader) RelationshipManager.Apply(leader, n, 0f, 1f, 0f, null);
        }

        // AI 漏掉的人（包括领导自己）：按规则补上分工
        List<NpcData> missing = new List<NpcData>();
        foreach (NpcData p in Alive()) if (!assigned.Contains(p) && (p == leader || !RecentlyRefused(p))) missing.Add(p);
        if (missing.Count > 0)
        {
            Dictionary<NpcData, string> auto = AutoAssign(Alive(), leader);
            foreach (NpcData p in missing)
            {
                if (!auto.TryGetValue(p, out string r)) continue;
                if (p.role == r) { ListRole(p, byRole); continue; }
                SetRole(p, r, day, byRole);
                changed++;
            }
        }

        bool projectChanged = SetProject(projectId, leader);
        // 第一次分工一定宣布；之后只有真的有变化才宣布、领导才开口（免得刷屏）
        if (!replan || changed > 0 || refused.Count > 0 || projectChanged)
        {
            if (speech.Length > 0) Speak(leader, null, speech);
            AnnouncePlan(leader, byRole, refused, replan);
        }
        return true;
    }

    // 听不听领导的安排：尊重 50% + 好感 20% + 信任 10% + 利他 20% + 随机 ±10，过门槛就听。
    bool Obeys(NpcData n, NpcData leader)
    {
        float respect = RelationshipManager.RespectOf(n, leader.id);
        float aff = RelationshipManager.AffinityOf(n, leader.id);
        float trust = RelationshipManager.TrustOf(n, leader.id);
        float altruism = n.personality != null ? n.personality.altruism : 50f;
        float w = 0.5f * respect + 0.2f * ((aff + 100f) * 0.5f) + 0.1f * trust + 0.2f * altruism + UnityEngine.Random.Range(-10f, 10f);
        return w >= obeyThreshold;
    }

    // ============ 小工具 ============

    // 头顶气泡说出来 + 写进日志。
    static void ShowLine(string id, string line)
    {
        NpcData me = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(id) : null;
        if (me == null || string.IsNullOrEmpty(line)) return;
        line = DialogueCleaner.Clean(line, me.npcName);
        if (string.IsNullOrEmpty(line)) return;
        NpcMover m = NpcView.GetMover(id);
        if (m != null) NpcSpeechBubble.Show(m.transform, line, 4f);
        if (LogManager.Instance != null) LogManager.Instance.AddLog(me.npcName, LF("intent_say", me.npcName, line), me.id);
    }

    // 领导说话：主模型（14B 扮演领导），系统提示词是他完整的演员提示词。没回 / 不能用时说 fallback。
    void SpeakMain(NpcData n, string situation, string fallback)
    {
        if (n == null) return;
        string id = n.id;
        bool sent = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.Story, key = "speech_" + id, json = true,
            system = () => NpcActors.Render(WorldManager.Instance.GetNpcById(id)) + "\n\n" + situation + LanguageGuard.Pick(
                "\n只说一两句话（30 字以内，口语，不要动作和神态描写）。只输出一个 JSON：{\"dialogue\":\"...\"}",
                "\n一、二文だけ話す（30文字以内、話し言葉、動作や表情の描写はしない）。JSONを1つだけ出力：{\"dialogue\":\"...\"}"),
            user = () => LanguageGuard.Pick("说吧。", "どうぞ。"),
            onResult = raw =>
            {
                string line = null;
                try
                {
                    int a = raw != null ? raw.IndexOf('{') : -1, b = raw != null ? raw.LastIndexOf('}') : -1;
                    if (a >= 0 && b > a) line = JObject.Parse(raw.Substring(a, b - a + 1)).Value<string>("dialogue");
                }
                catch { }
                if (!string.IsNullOrEmpty(line) && line == L("ai_no_response")) line = null;   // 请求失败时的占位回复
                ShowLine(id, string.IsNullOrEmpty(line) ? fallback : line);
            },
        });
        if (!sent) ShowLine(id, fallback);
    }

    // 说一句话：prompt 不为空时让小模型按人设写（失败用 fallback）；prompt 为空时直接说 fallback。
    void Speak(NpcData n, string prompt, string fallback)
    {
        if (n == null) return;
        string id = n.id;
        Action<string> show = line => ShowLine(id, line);
        if (string.IsNullOrEmpty(prompt)) { show(fallback); return; }
        bool sent = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.SocialLine, key = id, json = true, maxTokens = 60, languageAnchor = true,
            system = () =>
            {
                NpcData me = WorldManager.Instance.GetNpcById(id);
                string traits = me != null && me.traits != null ? string.Join("、", me.traits) : "";
                return LanguageGuard.Pick(
                    "你是" + me.npcName + "（" + me.profession + "）。性格：" + traits + "。说话风格：" + me.speakingStyle + "。\n" + prompt +
                    "\n只说一句很短的话（20 字以内，口语）。只输出一个 JSON：{\"dialogue\":\"...\"}",
                    "あなたは" + me.npcName + "（" + me.profession + "）。性格：" + traits + "。話し方：" + me.speakingStyle + "。\n" + prompt +
                    "\n短い一言だけ（20文字以内、話し言葉）。JSONを1つだけ出力：{\"dialogue\":\"...\"}");
            },
            user = () => LanguageGuard.Pick("说吧。", "どうぞ。"),
            onResult = raw =>
            {
                string line = null;
                try
                {
                    int a = raw != null ? raw.IndexOf('{') : -1, b = raw != null ? raw.LastIndexOf('}') : -1;
                    if (a >= 0 && b > a) line = JObject.Parse(raw.Substring(a, b - a + 1)).Value<string>("dialogue");
                }
                catch { }
                show(string.IsNullOrEmpty(line) ? fallback : line);
            },
        });
        if (!sent) show(fallback);
    }

    // 大家对某人的平均尊重。
    static float AverageRespect(NpcData target)
    {
        float sum = 0f;
        int k = 0;
        foreach (NpcData o in Alive())
        {
            if (o == target) continue;
            sum += RelationshipManager.RespectOf(o, target.id);
            k++;
        }
        return k > 0 ? sum / k : 50f;
    }

    static List<NpcData> Alive()
    {
        List<NpcData> list = new List<NpcData>();
        WorldState w = World;
        if (w == null) return list;
        foreach (NpcData n in w.npcs) if (n != null && !n.IsDead && !n.isBroken) list.Add(n);
        return list;
    }

    static NpcData FindByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (NpcData n in Alive()) if (n.npcName == name) return n;
        foreach (NpcData n in Alive()) if (!string.IsNullOrEmpty(n.npcName) && (name.Contains(n.npcName) || n.npcName.Contains(name))) return n;
        return null;
    }

    // 营地级别的消息：写进日志栏（用"营地"作为说话人）。
    static void Announce(string text)
    {
        if (string.IsNullOrEmpty(text) || LogManager.Instance == null) return;
        LogManager.Instance.AddLog("", text, "");
    }

    static string ItemName(string id) => string.IsNullOrEmpty(id) ? "" : ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(id) : GameLexicon.NameOf(id);
    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;
    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;
}
