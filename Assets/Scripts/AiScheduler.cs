using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

// ============================================================================
// AI 请求调度器（NPC 社会系统的地基）。
// 所有 AI 请求先进这里排队，由代码决定"谁、什么时候能用 AI"：
//   · 两条队列：Main = 主模型（14B），Lite = 小模型（4B）。两条互不排队，各自按优先级先后。
//   · 优先级：每种请求有基础优先级（见 rules，Inspector 可改）；排队越久优先级慢慢升高，低优先级不会被饿死。
//   · 过期作废：排队超过 expire 秒、或 stillValid 返回 false，直接丢掉（回调 null，调用方用代码兜底）。
//   · 限额：每种请求每个游戏日最多几次，按 key（通常是 npcId）分开算。超额的直接拒绝。
//   · 统计：每种请求的次数、平均等待 / 耗时、占用模型时间的比例。按 F9 打开调试面板看"算力分配"。
// 真正发请求仍然走 AIClientHub（语言守卫、后台让路、小模型这些逻辑都不变）。
//
// 用法：
//   AiScheduler.Submit(new AiRequest {
//       kind = AiKind.NpcIntent, key = npc.id,
//       system = () => 拼系统提示词, user = () => 拼本轮输入,
//       onResult = raw => { /* raw == null：没拿到（作废 / 失败），用代码兜底 */ } });
//   Submit 返回 false = 当场拒绝（超限额 / 小模型没装），onResult 不会被调用；
//   返回 true = 已排队，onResult 之后一定会被调用一次。
//
// 还没接进来的：LlmJobQueue（演员提示词、事件看法）和 NPC 闲聊配音仍然直接走 AIClientHub 的后台通道，
//   调试面板里单独显示它们忙不忙。以后一步步迁进来。
// ============================================================================

public enum AiLane { Main, Lite }

// 请求的种类。新加种类：在这里加一个，再在 DefaultRules 里给它一条规则。
public enum AiKind
{
    PlayerChat,     // 玩家和 NPC 对话（玩家在等）
    EventReaction,  // NPC 对突发事件的反应
    NpcIntent,      // NPC 自己决定现在做什么（社会系统第 1 阶段）
    NpcSocial,      // NPC 之间的请求 / 指挥 / 回应
    Director,       // AI 导演生成事件
    Background,     // 不重要、可以被打断的（只在模型空闲时发）
    InputFilter,    // 小模型：玩家输入分类（指令 / 闲聊 / 离题）
    EnemyLine,      // 小模型：敌人、异常存在的台词
    MemorySummary,  // 小模型：把一天的经历压缩成几句
    ChatTone,       // 小模型：读一遍 NPC 闲聊实际说的话，判断两人的语气（友好 / 普通 / 冷淡 / 敌意）
    SocialLine,     // 小模型：NPC 被别人请求 / 指挥时的一句回答（答不答应由代码决定）
    LeaderPlan,     // 领导每天早会给所有人分工（一次调用安排所有人）
    LeaderObserve,  // 小模型当观察者：读大家的数值，判断谁最适合当领导
}

[Serializable]
public class AiKindRule
{
    public AiKind kind;
    public AiLane lane;
    [Tooltip("基础优先级，越大越先发")]
    public int priority;
    [Tooltip("排队超过几秒还没发出去就作废（0 = 不过期）")]
    public float expire;
    [Tooltip("每个游戏日最多几次，按 key 分开算（比如每个 NPC 各算各的）。0 = 不限")]
    public int dailyQuota;
    [Tooltip("走后台通道：只在主模型空闲时发，玩家一说话就让路（可能拿不到结果）。只对 Main 有效")]
    public bool background;

    public AiKindRule(AiKind k, AiLane l, int p, float e, int q, bool bg)
    {
        kind = k; lane = l; priority = p; expire = e; dailyQuota = q; background = bg;
    }
}

public class AiRequest
{
    public AiKind kind;
    public string key;                 // 谁的请求（通常是 npcId）：限额、去重、调试显示都按它
    public Func<string> system;        // 发出去那一刻才拼提示词（拿到最新状态）
    public Func<string> user;
    public bool json = true;           // 要求模型输出 JSON
    public int maxTokens;              // 最多生成多少 token（0 = 默认；前台请求不限）
    public bool languageAnchor;        // 只对小模型有效：要给玩家看的台词才需要加语言提示
    public bool replacePending;        // true：同 kind + key 还在排队的旧请求作废，换成这个（只要最新的局势）
    public Func<bool> stillValid;      // 发出去之前再问一次"还需要吗"，返回 false 就作废
    public Action<string> onResult;    // 模型原文；作废 / 失败 = null

    internal float submitTime, sendTime;
    internal long order;
    internal bool done;
}

public class AiScheduler : MonoBehaviour
{
    public static AiScheduler Instance { get; private set; }

    [Header("主模型同时跑几个请求（和 Ollama 的 OLLAMA_NUM_PARALLEL 一致；默认 1）")]
    [Min(1)] public int mainConcurrency = 1;

    [Header("排队越久优先级越高：每等 1 秒加多少")]
    public float agingPerSecond = 2f;

    [Header("每种请求的规则（优先级 / 过期 / 每日限额 / 后台）")]
    public List<AiKindRule> rules = DefaultRules();

    [Header("调试面板（F9 开关）")]
    public bool showPanel = false;

    // 默认规则。玩家对话最高（只占几秒，插一下对 NPC 思考影响很小）；想让 NPC 思考排在玩家前面，把 NpcIntent 调到 100 以上即可。
    public static List<AiKindRule> DefaultRules()
    {
        return new List<AiKindRule>
        {
            new AiKindRule(AiKind.PlayerChat,    AiLane.Main, 100, 0f,  0, false),
            new AiKindRule(AiKind.EventReaction, AiLane.Main,  80, 20f, 0, false),
            new AiKindRule(AiKind.NpcIntent,     AiLane.Main,  60, 15f, 3, false),   // 每个 NPC 每天最多想 3 次
            new AiKindRule(AiKind.NpcSocial,     AiLane.Main,  50, 20f, 0, false),
            new AiKindRule(AiKind.Director,      AiLane.Main,  40, 0f,  0, false),
            new AiKindRule(AiKind.Background,    AiLane.Main,  10, 30f, 0, true),
            new AiKindRule(AiKind.InputFilter,   AiLane.Lite, 100, 5f,  0, false),
            new AiKindRule(AiKind.EnemyLine,     AiLane.Lite,  60, 8f,  0, false),
            new AiKindRule(AiKind.MemorySummary, AiLane.Lite,  20, 0f,  0, false),
            new AiKindRule(AiKind.ChatTone,      AiLane.Lite,  40, 20f, 0, false),
            new AiKindRule(AiKind.SocialLine,    AiLane.Lite,  70, 10f, 0, false),
            new AiKindRule(AiKind.LeaderPlan,    AiLane.Main,  55, 30f, 2, false),   // 每天最多 2 次（换了领导时会再分一次）
            new AiKindRule(AiKind.LeaderObserve, AiLane.Lite,  30, 30f, 0, false),
        };
    }

    // —— 统计 ——
    class KindStats
    {
        public int submitted, sent, ok, failed, expired, rejected;
        public double busySec, waitSec;
    }

    readonly List<AiRequest> _main = new List<AiRequest>();
    readonly List<AiRequest> _lite = new List<AiRequest>();
    readonly List<AiRequest> _active = new List<AiRequest>();   // 已经发出去、还没回来的
    readonly Dictionary<AiKind, KindStats> _stats = new Dictionary<AiKind, KindStats>();
    readonly Dictionary<string, int> _used = new Dictionary<string, int>();   // "kind|key" → 今天用了几次
    int _mainFgInFlight, _mainBgInFlight, _liteInFlight;
    int _day = int.MinValue;
    long _counter;

    // ============ 对外接口 ============

    public static AiScheduler Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("AiScheduler").AddComponent<AiScheduler>();
    }

    // 提交一个请求。false = 当场拒绝（onResult 不会被调用）；true = 已排队（onResult 之后一定会被调用一次）。
    public static bool Submit(AiRequest r) { return Ensure().SubmitInternal(r); }

    // 这个 kind + key 有没有还没结束的请求（排队中 / 进行中都算）。防止同一个 NPC 重复提交。
    public static bool HasPending(AiKind kind, string key)
    {
        return Instance != null && Instance.Pending(kind, key);
    }

    // 今天这个 kind + key 还剩几次（-1 = 不限）。
    public static int QuotaLeft(AiKind kind, string key)
    {
        AiScheduler s = Ensure();
        AiKindRule rule = s.Rule(kind);
        if (rule == null || rule.dailyQuota <= 0) return -1;
        s.RollDay();
        s._used.TryGetValue(QuotaKey(kind, key), out int used);
        return Mathf.Max(0, rule.dailyQuota - used);
    }

    // 新的一局：清空排队中的请求（进行中的回来后照常回调）。
    public static void ClearQueued()
    {
        if (Instance == null) return;
        Instance.DropAll(Instance._main);
        Instance.DropAll(Instance._lite);
        Instance._used.Clear();
    }

    // ============ 生命周期 ============

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        foreach (AiKind k in Enum.GetValues(typeof(AiKind))) _stats[k] = new KindStats();
        if (rules == null || rules.Count == 0) rules = DefaultRules();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        RollDay();
        PumpMain();
        PumpLite();

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.f9Key.wasPressedThisFrame) showPanel = !showPanel;
        UpdatePanel();
    }

    // ============ 排队 ============

    bool SubmitInternal(AiRequest r)
    {
        if (r == null || r.system == null || r.user == null) return false;
        AiKindRule rule = Rule(r.kind);
        if (rule == null) { Debug.LogWarning("[AI调度] 没有这种请求的规则：" + r.kind); return false; }
        KindStats st = _stats[r.kind];
        st.submitted++;

        // 小模型没装 / 连不上：当场拒绝，调用方立刻用代码兜底
        if (rule.lane == AiLane.Lite && LiteDead()) { st.rejected++; return false; }

        // 每日限额
        RollDay();
        if (rule.dailyQuota > 0)
        {
            _used.TryGetValue(QuotaKey(r.kind, r.key), out int used);
            if (used >= rule.dailyQuota) { st.rejected++; return false; }
        }

        List<AiRequest> q = rule.lane == AiLane.Lite ? _lite : _main;
        if (r.replacePending)
        {
            for (int i = q.Count - 1; i >= 0; i--)
                if (q[i].kind == r.kind && q[i].key == r.key) { AiRequest old = q[i]; q.RemoveAt(i); st.expired++; Finish(old, null); }
        }

        r.submitTime = Time.realtimeSinceStartup;
        r.order = _counter++;
        r.done = false;
        q.Add(r);
        return true;
    }

    // 主模型：严格按优先级。最高的那个发不出去（模型忙），就先不发更低的——免得后台请求刚发出去就被打断。
    void PumpMain()
    {
        AIClientHub hub = AIClientHub.Instance;
        if (hub == null) return;
        for (int guard = 0; guard < 8; guard++)
        {
            AiRequest best = PickBest(_main, out AiKindRule rule);
            if (best == null) return;

            if (!rule.background)
            {
                if (_mainFgInFlight >= mainConcurrency) return;
                if (!BuildPrompts(_main, best, out string sys, out string usr)) continue;
                _mainFgInFlight++;
                MarkSent(best);
                AiRequest sent = best;
                hub.Send(sys, usr, raw => { _mainFgInFlight = Mathf.Max(0, _mainFgInFlight - 1); Complete(sent, raw); }, best.json);
            }
            else
            {
                if (_mainFgInFlight > 0 || hub.ForegroundBusy || hub.BackgroundBusy) return;
                string sys = null, usr = null;
                try { sys = best.system(); usr = best.user(); }
                catch (Exception e) { Debug.LogWarning("[AI调度] 拼提示词出错，作废 " + best.kind + "：" + e.Message); _main.Remove(best); Fail(best); continue; }
                // SendBackground 返回 false = 这次没发（间隔没到 / 忙），请求留在队列里下一帧再试。
                // 万一回调在返回之前就来了（请求当场失败），先记下来，等登记完再处理。
                AiRequest sent = best;
                bool returned = false, early = false;
                string earlyRaw = null;
                bool ok = hub.SendBackground(sys, usr, raw =>
                {
                    if (!returned) { early = true; earlyRaw = raw; return; }
                    _mainBgInFlight = Mathf.Max(0, _mainBgInFlight - 1);
                    Complete(sent, raw);
                }, best.json, best.maxTokens);
                returned = true;
                if (!ok) return;
                _main.Remove(best);
                _mainBgInFlight++;
                MarkSent(best);
                if (early) { _mainBgInFlight = Mathf.Max(0, _mainBgInFlight - 1); Complete(sent, earlyRaw); }
            }
        }
    }

    void PumpLite()
    {
        if (_lite.Count == 0) return;
        AIClientHub hub = AIClientHub.Instance;
        if (hub == null) return;
        if (LiteDead()) { DropAll(_lite); return; }   // 小模型没装：排队中的全部作废
        for (int guard = 0; guard < 8; guard++)
        {
            if (!hub.LiteAvailable || hub.LiteBusy) return; // 还在加载 / 正忙
            AiRequest best = PickBest(_lite, out AiKindRule rule);
            if (best == null) return;
            if (!BuildPrompts(_lite, best, out string sys, out string usr)) continue;
            AiRequest sent = best;
            _liteInFlight++;
            MarkSent(best);
            bool ok = hub.SendLite(sys, usr, raw => { _liteInFlight = Mathf.Max(0, _liteInFlight - 1); Complete(sent, raw); },
                                   best.json, best.maxTokens > 0 ? best.maxTokens : 128, best.languageAnchor);
            if (!ok) { _liteInFlight = Mathf.Max(0, _liteInFlight - 1); Complete(sent, null); return; }
        }
    }

    // 挑最该发的那个；顺手清掉过期的、不再需要的、超限额的。
    AiRequest PickBest(List<AiRequest> q, out AiKindRule bestRule)
    {
        bestRule = null;
        float now = Time.realtimeSinceStartup;
        AiRequest best = null;
        float bestScore = float.MinValue;
        for (int i = q.Count - 1; i >= 0; i--)
        {
            AiRequest r = q[i];
            AiKindRule rule = Rule(r.kind);
            bool dead = rule == null || (rule.expire > 0f && now - r.submitTime > rule.expire);
            if (!dead && r.stillValid != null)
            {
                try { dead = !r.stillValid(); } catch { dead = true; }
            }
            if (!dead && rule.dailyQuota > 0)
            {
                _used.TryGetValue(QuotaKey(r.kind, r.key), out int used);
                if (used >= rule.dailyQuota) dead = true;
            }
            if (dead) { q.RemoveAt(i); _stats[r.kind].expired++; Finish(r, null); continue; }

            float score = rule.priority + (now - r.submitTime) * agingPerSecond;
            if (best == null || score > bestScore || (score == bestScore && r.order < best.order))
            {
                best = r; bestScore = score; bestRule = rule;
            }
        }
        return best;
    }

    // 拼提示词并移出队列；出错就作废（返回 false）。
    bool BuildPrompts(List<AiRequest> q, AiRequest r, out string sys, out string usr)
    {
        q.Remove(r);
        sys = usr = null;
        try { sys = r.system(); usr = r.user(); return true; }
        catch (Exception e)
        {
            Debug.LogWarning("[AI调度] 拼提示词出错，作废 " + r.kind + "：" + e.Message);
            Fail(r);
            return false;
        }
    }

    void MarkSent(AiRequest r)
    {
        r.sendTime = Time.realtimeSinceStartup;
        _active.Add(r);
        KindStats st = _stats[r.kind];
        st.sent++;
        st.waitSec += r.sendTime - r.submitTime;
        AiKindRule rule = Rule(r.kind);
        if (rule != null && rule.dailyQuota > 0)
        {
            string k = QuotaKey(r.kind, r.key);
            _used.TryGetValue(k, out int used);
            _used[k] = used + 1;
        }
    }

    void Complete(AiRequest r, string raw)
    {
        KindStats st = _stats[r.kind];
        st.busySec += Time.realtimeSinceStartup - r.sendTime;
        if (string.IsNullOrEmpty(raw)) st.failed++; else st.ok++;
        Finish(r, raw);
    }

    void Fail(AiRequest r)
    {
        _stats[r.kind].failed++;
        Finish(r, null);
    }

    // 回调调用方（只调一次；调用方的代码出错也不影响调度器）。
    void Finish(AiRequest r, string raw)
    {
        if (r.done) return;
        r.done = true;
        _active.Remove(r);
        try { r.onResult?.Invoke(raw); }
        catch (Exception e) { Debug.LogException(e); }
    }

    void DropAll(List<AiRequest> q)
    {
        for (int i = q.Count - 1; i >= 0; i--)
        {
            AiRequest r = q[i];
            q.RemoveAt(i);
            _stats[r.kind].expired++;
            Finish(r, null);
        }
    }

    bool Pending(AiKind kind, string key)
    {
        AiKindRule rule = Rule(kind);
        List<AiRequest> q = rule != null && rule.lane == AiLane.Lite ? _lite : _main;
        foreach (AiRequest r in q) if (r.kind == kind && r.key == key) return true;
        foreach (AiRequest r in _active) if (r.kind == kind && r.key == key) return true;
        return false;
    }

    void RollDay()
    {
        int day = WorldManager.Instance != null && WorldManager.Instance.world != null ? WorldManager.Instance.world.day : 0;
        if (day == _day) return;
        _day = day;
        _used.Clear();
    }

    AiKindRule Rule(AiKind kind)
    {
        if (rules != null)
            foreach (AiKindRule r in rules) if (r != null && r.kind == kind) return r;
        return null;
    }

    static string QuotaKey(AiKind kind, string key) => (int)kind + "|" + (key ?? "");

    // 小模型确定用不了（关掉了 / 没装 / 连不上）。还在检查中（"..."）不算。
    static bool LiteDead()
    {
        OllamaAIClient ol = OllamaAIClient.Instance;
        if (ol == null || !ol.liteEnabled) return true;
        return !ol.LiteReady && ol.LiteStatus == "NG";
    }

    // ============ 调试面板（F9）============
    // 识别框样式（UiStyle）：每条队列一段，列出每种请求的 排队数 / 已发 / 失败 / 平均耗时 / 占用模型时间的比例。

    GameObject _panelRoot;
    RectTransform _panelBox;
    TextMeshProUGUI _panelText;
    float _panelRefresh;

    void UpdatePanel()
    {
        if (!showPanel)
        {
            if (_panelRoot != null && _panelRoot.activeSelf) _panelRoot.SetActive(false);
            return;
        }
        if (_panelRoot == null) BuildPanel();
        if (!_panelRoot.activeSelf) { _panelRoot.SetActive(true); _panelRefresh = 0f; }
        _panelRefresh -= Time.unscaledDeltaTime;
        if (_panelRefresh > 0f) return;
        _panelRefresh = 0.25f;

        _panelText.text = PanelText();
        Vector2 pref = _panelText.GetPreferredValues(_panelText.text, 440f, 0f);
        _panelBox.sizeDelta = new Vector2(464f, pref.y + UiStyle.TagH + 20f);
    }

    void BuildPanel()
    {
        _panelRoot = new GameObject("AiSchedulerPanel");
        _panelRoot.transform.SetParent(transform, false);
        Canvas canvas = _panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1500;
        CanvasScaler scaler = _panelRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject box = new GameObject("Box", typeof(RectTransform));
        box.transform.SetParent(_panelRoot.transform, false);
        _panelBox = (RectTransform)box.transform;
        _panelBox.anchorMin = _panelBox.anchorMax = _panelBox.pivot = new Vector2(1f, 0.5f);
        _panelBox.anchoredPosition = new Vector2(-16f, 40f);
        _panelBox.sizeDelta = new Vector2(464f, 200f);
        Image bg = box.AddComponent<Image>();
        bg.color = UiStyle.Panel;
        bg.raycastTarget = false;
        UiStyle.Frame(_panelBox, UiStyle.Green);
        UiStyle.Tag(_panelBox, "ai_scheduler : debug", UiStyle.Green);

        GameObject tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(_panelBox, false);
        _panelText = tgo.AddComponent<TextMeshProUGUI>();
        _panelText.font = UiStyle.Mono;
        _panelText.fontSize = 14f;
        _panelText.color = UiStyle.TextMain;
        _panelText.richText = true;
        _panelText.alignment = TextAlignmentOptions.TopLeft;
        _panelText.textWrappingMode = TextWrappingModes.NoWrap;
        _panelText.raycastTarget = false;
        RectTransform tr = _panelText.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(12f, 8f);
        tr.offsetMax = new Vector2(-12f, -(UiStyle.TagH + 8f));
    }

    string PanelText()
    {
        AIClientHub hub = AIClientHub.Instance;
        OllamaAIClient ol = OllamaAIClient.Instance;
        StringBuilder sb = new StringBuilder();

        string mainName = hub == null ? "?" : hub.provider == AIProvider.LocalOllama ? (ol != null ? ol.modelName : "?") : hub.provider.ToString().ToLowerInvariant();
        sb.Append("<color=#B8FFC7>MAIN</color>  ").Append(mainName)
          .Append("  busy ").Append(_mainFgInFlight).Append('/').Append(mainConcurrency)
          .Append("  queue ").Append(_main.Count).Append('\n');
        AppendLane(sb, AiLane.Main, _main);
        bool jobBusy = hub != null && hub.BackgroundBusy && _mainBgInFlight == 0;
        int jobs = LlmJobQueue.Instance != null ? LlmJobQueue.Instance.Pending : 0;
        sb.Append("<color=#8E9490>bg (jobs/idle chat)  pending ").Append(jobs).Append(jobBusy ? "  running" : "").Append("</color>\n\n");

        string liteName = ol != null ? ol.liteModelName + " [" + ol.LiteStatus + "]" + (ol.liteCpuOnly ? " cpu" : "") : "?";
        int liteMax = hub != null ? hub.liteMaxConcurrent : 0;
        sb.Append("<color=#B8FFC7>LITE</color>  ").Append(liteName)
          .Append("  busy ").Append(_liteInFlight).Append('/').Append(liteMax)
          .Append("  queue ").Append(_lite.Count).Append('\n');
        AppendLane(sb, AiLane.Lite, _lite);
        sb.Append("<color=#8E9490>day ").Append(_day).Append("   F9 close</color>");
        return sb.ToString();
    }

    void AppendLane(StringBuilder sb, AiLane lane, List<AiRequest> q)
    {
        double total = 0;
        foreach (AiKindRule r in rules) if (r != null && r.lane == lane) total += _stats[r.kind].busySec;

        bool any = false;
        foreach (AiKindRule r in rules)
        {
            if (r == null || r.lane != lane) continue;
            KindStats st = _stats[r.kind];
            int queued = 0;
            foreach (AiRequest x in q) if (x.kind == r.kind) queued++;
            if (st.submitted == 0 && queued == 0) continue;
            if (!any)
            {
                sb.Append("<color=#8E9490>").Append(Pad("kind", 15)).Append(PadL("q", 3)).Append(PadL("sent", 6))
                  .Append(PadL("fail", 6)).Append(PadL("wait", 7)).Append(PadL("avg", 7)).Append(PadL("share", 7)).Append("</color>\n");
                any = true;
            }
            int fails = st.failed + st.expired + st.rejected;
            string wait = st.sent > 0 ? (st.waitSec / st.sent).ToString("0.0") + "s" : "-";
            string avg = st.sent > 0 && st.ok + st.failed > 0 ? (st.busySec / (st.ok + st.failed)).ToString("0.0") + "s" : "-";
            string share = total > 0.01 ? Mathf.RoundToInt((float)(st.busySec / total * 100.0)) + "%" : "-";
            sb.Append(Pad(SnakeName(r.kind), 15)).Append(PadL(queued.ToString(), 3)).Append(PadL(st.sent.ToString(), 6))
              .Append(fails > 0 ? "<color=#FF6A60>" + PadL(fails.ToString(), 6) + "</color>" : PadL("0", 6))
              .Append(PadL(wait, 7)).Append(PadL(avg, 7)).Append(PadL(share, 7)).Append('\n');
        }
        if (!any) sb.Append("<color=#8E9490>(idle)</color>\n");
    }

    static string Pad(string s, int w) => s.Length >= w ? s.Substring(0, w - 1) + " " : s.PadRight(w);
    static string PadL(string s, int w) => s.Length >= w ? " " + s : s.PadLeft(w);

    // PlayerChat → player_chat
    static string SnakeName(AiKind k)
    {
        string s = k.ToString();
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsUpper(c) && i > 0) b.Append('_');
            b.Append(char.ToLowerInvariant(c));
        }
        return b.ToString();
    }
}
