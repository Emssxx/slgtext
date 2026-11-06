using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Newtonsoft.Json.Linq;

// ============================================================================
// 领导与分组（社会系统第 4 阶段）。
//   领导怎么出现（4B 小模型当"观察者"）：
//     · 前几天大家还互相不熟，不会有领导（最早第 leaderMinDay 天）。
//     · 到了之后，要等一个"契机"才会有人站出来：一半以上的人安全感不足 / 刚有危险的东西靠近 /
//       已经有人开始请别人帮忙（第一次集体行动）；实在没有契机，到第 leaderLatestDay 天也会出现。
//     · 观察者读每个人生成时的数值（性格 5 维、词条、职业、身心状态），判断谁最适合当领导，给一句理由。
//       现在 NPC 的数值是固定的，所以领导也是固定的（只有领导死了 / 崩解了才重新看）；以后有成长系统再定期重看。
//       小模型没装 / 失败时，用代码按同样的数值打分。
//     · 出现时：大家聚到营地中心，日志写"这几天大家渐渐开始听 X 的——理由"，其他人对他的尊重 +3，他说一句话。
//   每天早会分工（AI，一次调用安排所有人）：
//     · 新的一天，领导用主模型看营地现状和每个人的状态，给每个人分一个组：
//         build（修建）/ gather（采集，带材料）/ scavenge（找吃的）/ care（照顾伤病员）/ rest（休整）
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

    [Header("早会分工")]
    public bool dailyPlan = true;
    [Range(0f, 100f)] public float obeyThreshold = 45f; // 听从安排的门槛（看对领导的尊重为主）

    public static readonly string[] Roles = { "build", "gather", "scavenge", "care", "rest" };

    bool _meeting;
    float _nextTry;
    int _plannedDay = -1;
    bool _planPending;
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

    // ============ 每帧 ============

    void Update()
    {
        WorldState w = World;
        if (w == null || _meeting) return;

        if (leadersEnabled && Leader == null && w.day >= leaderMinDay && Time.time >= _nextTry && Alive().Count >= minMembers)
        {
            _nextTry = Time.time + observeInterval;
            if (HasMoment(w)) StartCoroutine(Emerge());
            return;
        }

        if (dailyPlan && Leader != null && _plannedDay != w.day && !_planPending) RequestPlan();
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

    // 3. 宣布：日志 + 尊重 + 他说一句话 + 马上分工。
    void Crown(NpcData leader, string reason)
    {
        WorldState w = World;
        if (w == null) return;
        w.leaderId = leader.id;
        w.lastElectionDay = w.day;
        Announce(LF("society_emerged", leader.npcName, reason));
        foreach (NpcData o in Alive())
            if (o != leader) RelationshipManager.Apply(o, leader, 0f, 1f, 3f, LF("rel_reason_elected", w.day, leader.npcName));
        Speak(leader, LanguageGuard.Pick("这几天营地里的人渐渐开始听你的，今天大家聚过来等你拿主意。说一句你此刻想说的话。",
                                         "ここ数日、拠点のみんなは少しずつあなたの言うことを聞くようになり、今日はみんなが集まってあなたの判断を待っている。今言いたいことを一言。"),
              L("society_leader_line"));
        _plannedDay = -1; // 马上分工
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

    // ============ 早会分工 ============

    void RequestPlan()
    {
        NpcData leader = Leader;
        WorldState w = World;
        if (leader == null || w == null) return;
        int day = w.day;
        string lid = leader.id;
        _planPending = true;
        bool queued = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.LeaderPlan, key = lid, json = true,
            system = () => PlanPrompt(WorldManager.Instance.GetNpcById(lid)),
            user = () => LanguageGuard.Pick("给大家分工。", "みんなに仕事を割り振って。"),
            onResult = raw =>
            {
                _planPending = false;
                _plannedDay = day;
                NpcData l = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(lid) : null;
                if (!ApplyPlan(l, raw, day)) AutoPlan(l, day); // AI 没回 / 回的不能用：按规则分
            },
        });
        if (!queued) { _planPending = false; _plannedDay = day; AutoPlan(leader, day); } // 调度器拒绝：按规则分
    }

    // ============ 代码分工（AI 用不了时，或补 AI 漏掉的人）============

    // 按规则给所有人分工：伤病 / 太累 → 休整；有伤员又有药 → 1 人医护；食物少 → 1~2 人搜寻；
    // 剩下的：营地还没围起来 → 一半建造（领导带头）、一半采石头；围好了 → 采最缺的材料。
    void AutoPlan(NpcData leader, int day)
    {
        if (leader == null || leader.IsDead) return;
        Dictionary<NpcData, string> plan = AutoAssign(Alive(), leader);
        Dictionary<string, List<string>> byRole = new Dictionary<string, List<string>>();
        List<string> refused = new List<string>();
        foreach (KeyValuePair<NpcData, string> kv in plan)
        {
            NpcData n = kv.Key;
            if (n != leader && !Obeys(n, leader))
            {
                n.role = null;
                refused.Add(n.npcName);
                RelationshipManager.Apply(leader, n, -3f, -2f, 0f, LF("rel_reason_disobeyed", day, n.npcName));
                continue;
            }
            SetRole(n, kv.Value, day, byRole);
        }
        Speak(leader, null, L("society_plan_line"));
        AnnouncePlan(leader, byRole, refused);
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
        if (hurt && med && workers.Count > 1) TakeBest(workers, result, leader, "care", p => p.personality.altruism);
        // 搜寻：食物少于 4 份 → 1 人（5 人以上 → 2 人），挑最胆大的
        int food = ItemDatabase.Instance != null && w != null ? ItemDatabase.Instance.CountInStorage(ItemCategory.Food, w) : 0;
        int scav = food < 4 ? (workers.Count >= 5 ? 2 : 1) : 0;
        for (int i = 0; i < scav && workers.Count > 1; i++) TakeBest(workers, result, leader, "scavenge", p => p.personality.bravery);
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
        if (!byRole.TryGetValue(role, out List<string> list)) { list = new List<string>(); byRole[role] = list; }
        list.Add(n.npcName + (role == "gather" ? "（" + ItemName(n.roleTarget) + "）" : ""));
    }

    void AnnouncePlan(NpcData leader, Dictionary<string, List<string>> byRole, List<string> refused)
    {
        List<string> parts = new List<string>();
        foreach (string r in Roles)
            if (byRole.TryGetValue(r, out List<string> names)) parts.Add(RoleName(r) + "：" + string.Join("、", names));
        if (parts.Count > 0) Announce(LF("society_plan", leader.npcName, string.Join(LanguageGuard.Pick("；", "／"), parts)));
        foreach (string name in refused) Announce(LF("society_refused", name, leader.npcName));
    }

    string PlanPrompt(NpcData leader)
    {
        if (leader == null) throw new Exception("领导不存在");
        WorldState w = World;
        bool jp = LanguageGuard.IsJp;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(NpcActors.Render(leader));
        sb.AppendLine();
        sb.AppendLine(jp ? $"あなたは拠点のみんなに選ばれたリーダーだ。今は{w.day}日目の朝。みんなに今日の仕事を割り振る。"
                         : $"你是营地里大家推举出来的领导。现在是第 {w.day} 天早上，你要给大家分配今天的工作。");
        sb.AppendLine();
        sb.AppendLine(jp ? "【拠点の今の状況】" : "【营地现状】");
        sb.Append(NpcIntentBrain.CampStatusText(leader, jp));
        sb.AppendLine(jp ? $"拠点の囲い：{Mathf.RoundToInt((NpcSecurity.Instance != null ? NpcSecurity.Instance.Coverage : 0f) * 100f)}%" + (NpcSecurity.Instance != null && NpcSecurity.Instance.Enclosed ? "（囲まれている）" : "")
                         : $"营地围合：{Mathf.RoundToInt((NpcSecurity.Instance != null ? NpcSecurity.Instance.Coverage : 0f) * 100f)}%" + (NpcSecurity.Instance != null && NpcSecurity.Instance.Enclosed ? "（已经围起来了）" : ""));
        sb.AppendLine();
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
            sb.AppendLine();
        }
        sb.AppendLine();
        if (jp)
        {
            sb.AppendLine("【割り振れる仕事】build（拠点のまわりに壁を建てる：自分で倉庫から石を取って建てる。石がなければ先に採る）／gather（資源を採る。target に wood / stone / ore / fiber。壁には石が要る）／scavenge（拠点の外で食べ物を探す）／care（けが人・病人の世話）／rest（休ませる）");
            sb.AppendLine("あなた自身も含めて、全員に一つずつ割り振る。体の状態・職業・拠点に今いちばん足りないものを見て決める。けがや病気の人は rest か care にする。");
            sb.AppendLine("リーダーのあなたも必ず手を動かすこと（指示するだけで休むのはだめ。率先して一番きつい仕事を取ってもいい）。");
            sb.AppendLine("【出力形式】JSONオブジェクトを1つだけ出力し、それ以外は何も書かないこと：");
            sb.AppendLine("{\"groups\":[{\"name\":\"名前\",\"role\":\"上のどれか（英字）\",\"target\":\"gatherのときの資源\"}],\"speech\":\"朝の集まりでみんなに言う一言（短く）\"}");
        }
        else
        {
            sb.AppendLine("【可以分配的工作】build（修营地那一圈围墙：会自己去仓库取石头、开工；没石头就先去采）／gather（采集，target 填 wood / stone / ore / fiber；修墙要石头）／scavenge（出营地找吃的）／care（照顾伤病的人）／rest（休整）");
            sb.AppendLine("每个人分一项，包括你自己。按每个人的身体状况、职业和营地现在最缺什么来分。受伤、生病的人分 rest 或让别人 care。");
            sb.AppendLine("你是领导，自己也必须干活，以身作则（不能只指挥、自己休息；可以带头去干最累的活）。");
            sb.AppendLine("【输出格式】只输出一个 JSON 对象，不要多余文字：");
            sb.AppendLine("{\"groups\":[{\"name\":\"名字\",\"role\":\"上面之一（英文）\",\"target\":\"gather 时要采的东西\"}],\"speech\":\"你在早会上对大家说的一两句话（简短）\"}");
        }
        if (LocalizationManager.Instance != null) sb.AppendLine(LocalizationManager.Instance.GetAILanguageInstruction());
        return sb.ToString();
    }

    // 解析分工 → 每个人决定听不听 → 写日志。AI 的结果不能用返回 false（调用方改用代码分工）。
    bool ApplyPlan(NpcData leader, string raw, int day)
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
        if (speech.Length > 0) Speak(leader, null, speech);

        Dictionary<string, List<string>> byRole = new Dictionary<string, List<string>>();
        List<string> refused = new List<string>();
        List<NpcData> assigned = new List<NpcData>();
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
            if (n != leader && !Obeys(n, leader))
            {
                n.role = null;
                refused.Add(n.npcName);
                RelationshipManager.Apply(leader, n, -3f, -2f, 0f, LF("rel_reason_disobeyed", day, n.npcName));
                RelationshipManager.Apply(n, leader, 0f, 0f, -2f, null);
                continue;
            }
            // 领导自己也要干活（以身作则）：身体没问题却被分去休息 → 改成建造组
            if (n == leader && role == "rest" && n.energy >= 40f && NpcHealth.CanWork(n)) role = "build";
            SetRole(n, role, day, byRole, target);
            if (n != leader) RelationshipManager.Apply(leader, n, 0f, 1f, 0f, null);
        }

        // AI 漏掉的人（包括领导自己）：按规则补上分工
        List<NpcData> missing = new List<NpcData>();
        foreach (NpcData p in Alive()) if (!assigned.Contains(p)) missing.Add(p);
        if (missing.Count > 0)
        {
            Dictionary<NpcData, string> auto = AutoAssign(Alive(), leader);
            foreach (NpcData p in missing)
                if (auto.TryGetValue(p, out string r)) SetRole(p, r, day, byRole);
        }

        AnnouncePlan(leader, byRole, refused);
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

    // 说一句话：prompt 不为空时让小模型按人设写（失败用 fallback）；prompt 为空时直接说 fallback。
    void Speak(NpcData n, string prompt, string fallback)
    {
        if (n == null) return;
        string id = n.id;
        Action<string> show = line =>
        {
            NpcData me = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(id) : null;
            if (me == null || string.IsNullOrEmpty(line)) return;
            line = DialogueCleaner.Clean(line, me.npcName);
            if (string.IsNullOrEmpty(line)) return;
            NpcMover m = NpcView.GetMover(id);
            if (m != null) NpcSpeechBubble.Show(m.transform, line, 4f);
            if (LogManager.Instance != null) LogManager.Instance.AddLog(me.npcName, LF("intent_say", me.npcName, line), me.id);
        };
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
