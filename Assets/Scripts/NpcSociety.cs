using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Newtonsoft.Json.Linq;

// ============================================================================
// 领导与分组（社会系统第 4 阶段）。
//   选领导（代码）：
//     · 什么时候：营地里活着的人 ≥ minMembers、到了第 electionStartDay 天、还没有领导；
//                 或者领导死了 / 崩解了；或者每天早上看一次，大家对领导的平均尊重低于 noConfidence → 重新选。
//     · 怎么选：先喊大家到营地中心开会（手上没事的人会走过来），然后每人投一票（不能投自己）：
//                 尊重 60% + 好感 30% + 信任 10% + 一点随机，投给分最高的人；票最多的当选（平票看平均尊重）。
//     · 当选后：其他人对他的尊重 +3；他说一句话（小模型写，没有就用本地台词）。
//   每天早会分工（AI，一次调用安排所有人）：
//     · 新的一天，领导用主模型看营地现状和每个人的状态，给每个人分一个组：
//         build（修建）/ gather（采集，带材料）/ scavenge（找吃的）/ care（照顾伤病员）/ rest（休整）
//     · 听不听安排由代码算（对领导的尊重为主）：不服的人当天不按分工干，领导对他的好感 / 信任下降。
//     · 分工只管"闲下来的时候干什么"：需求（饿 / 累 / 逃命）、玩家指令、自己的计划（安全感不足）都比分工优先。
//       具体怎么干由 NpcIntentBrain.DoRoleWork 按分工用代码执行，不再调用 AI。
// 由 NpcBehaviorDriver.Start 自动挂上。
// ============================================================================
public class NpcSociety : MonoBehaviour
{
    public static NpcSociety Instance { get; private set; }

    [Header("选领导")]
    public bool electionsEnabled = true;
    public int minMembers = 3;            // 至少几个人才选领导
    public int electionStartDay = 2;      // 第几天开始
    public float noConfidence = 35f;      // 大家对领导的平均尊重低于这个 → 重新选
    public float meetingWait = 20f;       // 开会最多等大家多少秒

    [Header("早会分工")]
    public bool dailyPlan = true;
    [Range(0f, 100f)] public float obeyThreshold = 45f; // 听从安排的门槛（看对领导的尊重为主）

    public static readonly string[] Roles = { "build", "gather", "scavenge", "care", "rest" };

    bool _meeting;
    float _nextTry;
    int _plannedDay = -1, _checkedDay = -1;
    bool _planPending;

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
        Instance._checkedDay = -1;
        Instance._planPending = false;
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

    // 今天有没有（听从了的）分工。
    public static bool HasRole(NpcData n) => n != null && World != null && !string.IsNullOrEmpty(n.role) && n.roleDay == World.day;

    public static string RoleName(string role) => string.IsNullOrEmpty(role) ? "" : L("role_" + role);

    // ============ 每帧 ============

    void Update()
    {
        WorldState w = World;
        if (w == null || _meeting) return;

        // 每天早上：看一次领导还靠不靠得住
        if (w.day != _checkedDay)
        {
            _checkedDay = w.day;
            NpcData leader = Leader;
            if (leader != null && AverageRespect(leader) < noConfidence)
            {
                Announce(LF("society_no_confidence", leader.npcName));
                w.leaderId = null;
            }
        }

        if (electionsEnabled && Leader == null && w.day >= electionStartDay && Time.time >= _nextTry && Alive().Count >= minMembers)
        {
            _nextTry = Time.time + 60f;
            StartCoroutine(Meeting());
            return;
        }

        if (dailyPlan && Leader != null && _plannedDay != w.day && !_planPending) RequestPlan();
    }

    // ============ 开会选领导 ============

    IEnumerator Meeting()
    {
        _meeting = true;
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
            if (!walking) break;
            t += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        Vote();
        _meeting = false;
    }

    void Vote()
    {
        WorldState w = World;
        List<NpcData> people = Alive();
        if (w == null || people.Count < 2) return;
        Dictionary<string, int> votes = new Dictionary<string, int>();
        foreach (NpcData voter in people)
        {
            NpcData best = null;
            float bestScore = float.MinValue;
            foreach (NpcData c in people)
            {
                if (c == voter) continue;
                float score = 0.6f * RelationshipManager.RespectOf(voter, c.id)
                            + 0.3f * ((RelationshipManager.AffinityOf(voter, c.id) + 100f) * 0.5f)
                            + 0.1f * RelationshipManager.TrustOf(voter, c.id)
                            + UnityEngine.Random.Range(0f, 5f);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            if (best == null) continue;
            votes.TryGetValue(best.id, out int k);
            votes[best.id] = k + 1;
        }

        NpcData winner = null;
        int top = -1;
        float topRespect = -1f;
        foreach (NpcData c in people)
        {
            votes.TryGetValue(c.id, out int v);
            float rs = AverageRespect(c);
            if (v > top || (v == top && rs > topRespect)) { winner = c; top = v; topRespect = rs; }
        }
        if (winner == null) return;

        w.leaderId = winner.id;
        w.lastElectionDay = w.day;
        Announce(LF("society_elected", winner.npcName, top, people.Count));
        foreach (NpcData o in people)
            if (o != winner) RelationshipManager.Apply(o, winner, 0f, 1f, 3f, LF("rel_reason_elected", w.day, winner.npcName));
        Speak(winner, LanguageGuard.Pick("你刚被营地里的人推举为领导。说一句你此刻想说的话。", "あなたは今、拠点のみんなにリーダーに選ばれた。今言いたいことを一言。"),
              L("society_leader_line"));
        _plannedDay = -1; // 选完马上分工
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
                ApplyPlan(WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(lid) : null, raw, day);
            },
        });
        if (!queued) { _planPending = false; _plannedDay = day; } // 今天的次数用完了 / 调度器拒绝：今天不分工
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
            sb.AppendLine("【割り振れる仕事】build（拠点を建てる・囲う）／gather（資源を採る。target に wood / stone / ore / fiber）／scavenge（拠点の外で食べ物を探す）／care（けが人・病人の世話）／rest（休ませる）");
            sb.AppendLine("あなた自身も含めて、全員に一つずつ割り振る。体の状態・職業・拠点に今いちばん足りないものを見て決める。けがや病気の人は rest か care にする。");
            sb.AppendLine("【出力形式】JSONオブジェクトを1つだけ出力し、それ以外は何も書かないこと：");
            sb.AppendLine("{\"groups\":[{\"name\":\"名前\",\"role\":\"上のどれか（英字）\",\"target\":\"gatherのときの資源\"}],\"speech\":\"朝の集まりでみんなに言う一言（短く）\"}");
        }
        else
        {
            sb.AppendLine("【可以分配的工作】build（修建、把营地围起来）／gather（采集，target 填 wood / stone / ore / fiber）／scavenge（出营地找吃的）／care（照顾伤病的人）／rest（休整）");
            sb.AppendLine("每个人分一项，包括你自己。按每个人的身体状况、职业和营地现在最缺什么来分。受伤、生病的人分 rest 或让别人 care。");
            sb.AppendLine("【输出格式】只输出一个 JSON 对象，不要多余文字：");
            sb.AppendLine("{\"groups\":[{\"name\":\"名字\",\"role\":\"上面之一（英文）\",\"target\":\"gather 时要采的东西\"}],\"speech\":\"你在早会上对大家说的一两句话（简短）\"}");
        }
        if (LocalizationManager.Instance != null) sb.AppendLine(LocalizationManager.Instance.GetAILanguageInstruction());
        return sb.ToString();
    }

    // 解析分工 → 每个人决定听不听 → 写日志。
    void ApplyPlan(NpcData leader, string raw, int day)
    {
        WorldState w = World;
        if (leader == null || w == null || leader.IsDead || string.IsNullOrEmpty(raw)) return;
        JObject o;
        try
        {
            int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
            if (a < 0 || b <= a) return;
            o = JObject.Parse(raw.Substring(a, b - a + 1));
        }
        catch { return; }

        string speech = (o.Value<string>("speech") ?? "").Trim();
        if (speech.Length > 0) Speak(leader, null, speech);

        Dictionary<string, List<string>> byRole = new Dictionary<string, List<string>>();
        List<string> refused = new List<string>();
        JArray groups = o["groups"] as JArray;
        if (groups == null) return;
        foreach (JToken t in groups)
        {
            JObject g = t as JObject;
            if (g == null) continue;
            NpcData n = FindByName((g.Value<string>("name") ?? "").Trim());
            string role = (g.Value<string>("role") ?? "").Trim().ToLowerInvariant();
            if (n == null || Array.IndexOf(Roles, role) < 0) continue;
            string target = (g.Value<string>("target") ?? "").Trim().ToLowerInvariant();

            if (n != leader && !Obeys(n, leader))
            {
                n.role = null;
                refused.Add(n.npcName);
                RelationshipManager.Apply(leader, n, -3f, -2f, 0f, LF("rel_reason_disobeyed", day, n.npcName));
                RelationshipManager.Apply(n, leader, 0f, 0f, -2f, null);
                continue;
            }
            n.role = role;
            n.roleTarget = role == "gather" ? target : "";
            n.roleDay = day;
            if (n != leader) RelationshipManager.Apply(leader, n, 0f, 1f, 0f, null);
            if (!byRole.TryGetValue(role, out List<string> list)) { list = new List<string>(); byRole[role] = list; }
            list.Add(n.npcName + (role == "gather" && target.Length > 0 ? "（" + ItemName(target) + "）" : ""));
        }

        List<string> parts = new List<string>();
        foreach (string r in Roles)
            if (byRole.TryGetValue(r, out List<string> names)) parts.Add(RoleName(r) + "：" + string.Join("、", names));
        if (parts.Count > 0) Announce(LF("society_plan", leader.npcName, string.Join(LanguageGuard.Pick("；", "／"), parts)));
        foreach (string name in refused) Announce(LF("society_refused", name, leader.npcName));
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
