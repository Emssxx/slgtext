using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Newtonsoft.Json.Linq;

// ============================================================================
// 开场（第一天）：所有 NPC 聚在一起演的两场戏 + 玩家（防护服里的 AI）开机。
//
//  第一场（黑屏）：画面全黑，只看得见每个人的对话框（像黑暗里从不同方向传来的说话声）。
//    设定：他们被卷进亚空间已经有一段时间了，刚在废弃营地里找到一批被遗弃的资源设备，穿上了防护服，
//    聚在一起讨论这些资源怎么用、有了这些能不能活下去甚至逃出去——无意间按到了防护服上的启动开关。
//    · 台词由主模型（14B）按每个人的设定现场写（AiKind.Story）。等它的时候先放开场字幕和几句环境声；
//      写不出来 / 超过 dialogueTimeout 秒 → 用本地台词。最后一句一定是"按到了开关"。
//    · 这段时间里：NPC 的数值由代码生成（开局已经生成好），每个人的「演员提示词」（人格、说话方式……）
//      由 14B 在后台写（NpcActors → LlmJobQueue）。AI 启动时的「人格建模」一步会等它写完（最多 personaWaitMax 秒）。
//
//  AI 启动（开关按下 = 玩家这个 AI 开机）：老电脑式的启动日志，能真的检查的都真的检查：
//    推理服务连接（Ollama 版本）→ 主模型（参数量 / 量化 / 是否已在显存，没载入就当场预热）→ 辅助模型（4B）
//    → 视觉 / 听觉 / 知识库（真实条目数）→ 人格建模（等演员提示词）→ 地形测绘（真实格数、资源点、水域）
//    → 导航网格 → 营地判定 → 战术界面。然后黑幕像头盔面罩一样上下拉开，HUD 一块一块闪着亮起来。
//
//  第二场：幸存者们讨论（不写台词，只显示「讨论中」）→ 要团队合作才能活下去 → 投票选领导。
//    实际：AI 启动一开始就让 4B（NpcSociety 的观察者）读大家的数值挑一个人（PickLeader），
//    投票只是把这个结果演出来；最后由代码标记他为领导（NpcSociety.CrownChosen），他用 14B 说一句话，马上分工。
//
//  开场期间 Time.timeScale = 0：世界静止（不过天、不出事件、NPC 不动、不开始分工），结束时恢复。
//  HUD 先藏起来（临时 CanvasGroup 透明 / 关掉 Canvas），面罩拉开后再亮起来。
//  操作：Esc = 跳过当前这一段；空格 / 回车 / 点击 = 下一句。
//  WorldManager.playOpening 关掉 = 不演开场，直接在第一天由 4B 选出领导。
// 由 WorldManager.Start 最后调用 OpeningDirector.Begin()。
// ============================================================================
public class OpeningDirector : MonoBehaviour
{
    public static OpeningDirector Instance { get; private set; }
    // 开场正在演（其他系统：不选领导、不响应快捷键）
    public static bool Playing { get; private set; }
    // 第一天的领导还没由开场选出来（NpcSociety 这段时间不自己选）
    public static bool HandlesElection => Instance != null && !Instance._elected;

    [Header("第一场戏（黑屏对话）")]
    [Tooltip("等 14B 写台词最多几秒，超时用本地台词")]
    public float dialogueTimeout = 30f;
    [Tooltip("对话框打字速度（字 / 秒）")]
    public float typeSpeed = 26f;
    public int minLines = 8, maxLines = 12;

    [Header("AI 启动")]
    [Tooltip("「人格建模」最多等几秒（14B 在后台写每个人的演员提示词），没写完的在后台继续")]
    public float personaWaitMax = 45f;
    [Tooltip("主模型还没进显存时，最多等它预热几秒")]
    public float warmupWaitMax = 90f;
    [Tooltip("辅助模型（4B）还在加载时最多等几秒")]
    public float liteWaitMax = 15f;

    [Header("第二场戏（投票）")]
    public float discussSeconds = 3.5f;
    public float voteInterval = 0.8f;

    static bool _nextPlay = true;
    bool _play = true, _elected, _aborted;
    bool _advance, _skip;
    float _time;

    // —— 界面 ——
    Canvas _canvas;
    RectTransform _root, _stage, _bootRoot;
    Image _black, _blocker, _visorTop, _visorBottom, _visorLine;
    TextMeshProUGUI _center, _sub, _hint;
    TMP_FontAsset _font;
    readonly List<Box> _boxes = new List<Box>();
    readonly Dictionary<string, float> _slotX = new Dictionary<string, float>();

    // —— HUD ——
    class HudCanvas { public Canvas canvas; public CanvasGroup group; public bool addedGroup, disabledCanvas; }
    readonly List<HudCanvas> _hud = new List<HudCanvas>();
    bool _hudHidden;
    float _hudScan;

    // —— 启动日志 ——
    class BLine { public string text = ""; public Color color; public bool rule; }
    TerminalConsole _con;
    readonly List<BLine> _bl = new List<BLine>();
    readonly List<TerminalConsole.Spec> _bspecs = new List<TerminalConsole.Spec>();
    int _bPrinted;
    float _bootT0 = -1f;

    // —— 第二场 ——
    RectTransform _capRt;
    TextMeshProUGUI _capText, _capSub, _capTally;
    string _capBase;
    bool _capDots;
    readonly List<Bubble> _bubbles = new List<Bubble>();
    NpcData _leader;
    string _reason;
    bool _picked, _scene1Over, _leaderLocked;
    int _votes = -1, _total;

    // ================= 开始 =================

    public static void Begin(bool play)
    {
        if (Instance != null) return;
        _nextPlay = play;
        new GameObject("OpeningDirector").AddComponent<OpeningDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _play = _nextPlay;
        if (!_play) return;
        Playing = true;
        Time.timeScale = 0f;   // 世界静止，直到投票结束
        _font = GameFonts.Find();
        BuildUI();
        _hudHidden = true;
        HideHudScan();
    }

    void Start()
    {
        StartCoroutine(_play ? Safe(Run()) : QuickElection());
    }

    // 开场出错时千万不能卡在黑屏 + 世界静止：每一步都包起来，出错就收拾干净直接进游戏（领导照样选）。
    IEnumerator Safe(IEnumerator root)
    {
        Stack<IEnumerator> stack = new Stack<IEnumerator>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            bool moved;
            object cur = null;
            try
            {
                moved = top.MoveNext();
                if (moved) cur = top.Current;
            }
            catch (Exception e)
            {
                Debug.LogError("[开场] 出错了，跳过开场直接进游戏：" + e);
                Abort();
                yield break;
            }
            if (!moved) { stack.Pop(); continue; }
            if (cur is IEnumerator sub) { stack.Push(sub); continue; }
            yield return cur;
        }
    }

    void Abort()
    {
        if (_aborted) return;
        _aborted = true;
        foreach (Bubble b in _bubbles)
        {
            if (b.rt != null) Destroy(b.rt.gameObject);
            if (b.badge != null) Destroy(b.badge.gameObject);
        }
        _bubbles.Clear();
        _hudHidden = false;
        foreach (HudCanvas h in _hud) RestoreHud(h);
        _hud.Clear();
        Time.timeScale = 1f;
        Playing = false;
        if (_canvas != null) Destroy(_canvas.gameObject);
        if (!_elected) StartCoroutine(QuickElection());
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        if (Playing) { Playing = false; Time.timeScale = 1f; }
        foreach (HudCanvas h in _hud) RestoreHud(h);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _time += dt;
        if (!Playing) return;
        Keyboard kb = Keyboard.current;
        Mouse ms = Mouse.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) _skip = true;
        if ((kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) || (ms != null && ms.leftButton.wasPressedThisFrame)) _advance = true;

        if (_hudHidden)
        {
            _hudScan -= dt;
            if (_hudScan <= 0f) { _hudScan = 0.2f; HideHudScan(); }   // 晚一点才建出来的界面也藏起来
        }
        if (_con != null)
        {
            _con.Tick(dt);
            if (_bootT0 >= 0f) _con.SetStatus("SSU-AI  //  BOOT", "t+" + (Time.realtimeSinceStartup - _bootT0).ToString("0.0") + "s");
        }
        UpdateBoxes(dt);
        UpdateBubbles();
        UpdateCaption();
    }

    IEnumerator Run()
    {
        yield return null;   // 等一帧：NPC 物体、各系统都挂好
        yield return Scene1();
        yield return Boot();
        yield return Reveal();
        yield return Scene2();
        Finish();
    }

    // 不演开场：第一天直接由 4B 选领导。
    IEnumerator QuickElection()
    {
        float t = 0f;
        while (NpcSociety.Instance == null && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
        bool done = false;
        NpcData leader = null;
        string why = null;
        NpcSociety.PickLeader((n, r) => { leader = n; why = r; done = true; });
        t = 0f;
        while (!done && t < 40f) { t += Time.unscaledDeltaTime; yield return null; }
        _elected = true;
        if (leader != null) NpcSociety.CrownChosen(leader, why, -1, 0);
    }

    void Finish()
    {
        foreach (Bubble b in _bubbles)
        {
            if (b.rt != null) Destroy(b.rt.gameObject);
            if (b.badge != null) Destroy(b.badge.gameObject);
        }
        _bubbles.Clear();
        Time.timeScale = 1f;
        Playing = false;
        _elected = true;
        if (_leader != null && !_leader.IsDead) NpcSociety.CrownChosen(_leader, _reason, _votes, _total);
        StartCoroutine(FadeOutAndClose());
    }

    IEnumerator FadeOutAndClose()
    {
        CanvasGroup g = _root.gameObject.AddComponent<CanvasGroup>();
        float t = 0f;
        while (t < 0.4f) { t += Time.unscaledDeltaTime; g.alpha = 1f - t / 0.4f; yield return null; }
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    // ================= 第一场：黑屏对话 =================

    class Line { public NpcData npc; public string text; }

    // 开场里观众还不认识他们：名字一律显示成 ？？？（对话框、台词里的称呼、投票、结果）。开场结束后才用真名。
    const string Unknown = "？？？";
    static readonly string[] NameSuffix = { "さん", "くん", "君", "ちゃん", "先生", "哥", "姐", "，", "、", "！", "？", "。", "…", ",", "!", "?", " " };

    // 把台词 / 理由里出现的人名换成 ？？？（两个字以上的名字直接换；一个字的名字只在后面跟称呼 / 标点时换，免得误伤普通字）。
    static string HideNames(string text, List<NpcData> cast)
    {
        if (string.IsNullOrEmpty(text) || cast == null) return text;
        List<NpcData> sorted = new List<NpcData>(cast);
        sorted.Sort((a, b) => (b.npcName ?? "").Length.CompareTo((a.npcName ?? "").Length));   // 长名字先换（"王小明" 先于 "小明"）
        foreach (NpcData n in sorted)
        {
            string nm = n != null ? n.npcName : null;
            if (string.IsNullOrEmpty(nm)) continue;
            if (nm.Length >= 2) { text = text.Replace(nm, Unknown); continue; }
            foreach (string suf in NameSuffix) text = text.Replace(nm + suf, Unknown + suf);
            if (text.EndsWith(nm)) text = text.Substring(0, text.Length - nm.Length) + Unknown;
        }
        return text;
    }

    IEnumerator Scene1()
    {
        _skip = false;
        _advance = false;
        List<NpcData> cast = Alive();
        AssignSlots(cast);
        SetHint(true);

        // 1. 马上让 14B 写台词；等它的时候放开场字幕和环境声
        List<Line> lines = null;
        bool got = false;
        RequestDialogue(cast, l => { lines = l; got = true; });
        float waitStart = Time.realtimeSinceStartup;
        yield return Caption(L("open_cap_place"), L("open_cap_time"));
        int amb = 0;
        while (!got && !_skip && Time.realtimeSinceStartup - waitStart < dialogueTimeout && cast.Count > 0)
        {
            yield return Ambient(L("open_amb_0" + (amb % 5 + 1)), 2.6f, () => got);
            amb++;
        }
        if (lines == null || lines.Count < 3) lines = FallbackLines(cast);
        EnsureSwitchLine(lines, cast);
        foreach (Line ln in lines) ln.text = HideNames(ln.text, cast);   // 台词里互相叫名字的也换成 ？？？

        // 2. 台词
        foreach (Line ln in lines)
        {
            if (_skip) break;
            yield return SayLine(ln);
        }
        _scene1Over = true;

        // 3. 开关（咔哒）
        _skip = false;
        foreach (Box b in _boxes) b.targetAlpha = 0f;
        yield return Ambient(L("open_click"), 1.3f, null);
        SetHint(false);
        _stage.gameObject.SetActive(false);
    }

    // 黑暗里每个人站在不同的方向：对话框的横向位置按人分开。
    void AssignSlots(List<NpcData> cast)
    {
        _slotX.Clear();
        int n = cast.Count;
        float spacing = n > 1 ? Mathf.Min(0.18f, 0.66f / (n - 1)) : 0f;
        for (int i = 0; i < n; i++) _slotX[cast[i].id] = (i - (n - 1) * 0.5f) * spacing;
    }

    void RequestDialogue(List<NpcData> cast, Action<List<Line>> done)
    {
        if (cast.Count == 0) { done(null); return; }
        bool jp = LanguageGuard.IsJp;
        string sys = DialoguePrompt(cast, jp);
        bool queued = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.Story, key = "opening", json = true,
            system = () => sys,
            user = () => LanguageGuard.Pick("写这场戏的台词。", "この場面の台詞を書いてください。"),
            stillValid = () => !_scene1Over && !_skip,
            onResult = raw => done(ParseLines(raw, cast)),
        });
        if (!queued) done(null);
    }

    string DialoguePrompt(List<NpcData> cast, bool jp)
    {
        StringBuilder sb = new StringBuilder();
        if (jp)
        {
            sb.AppendLine("あなたは亜空間サバイバルドラマの脚本家だ。オープニング第一場の台詞を書く。");
            sb.AppendLine("【世界】現代の地球に、近くにいる一般市民を呑み込む空間の渦が現れるようになった。渦の内側は「亜空間」と呼ばれる異常な領域で、時間・空間・因果のすべてが信用できず、長くいるほど精神がむしばまれる。各国は共同で「特異現象調査局」(ABA)を設立した。");
            sb.AppendLine("【状況】下の人たちは亜空間に巻き込まれてから、もうしばらく経つ。廃棄された拠点の近くでなんとか持ちこたえてきた。ついさっき、拠点で亜空間に置き去りにされた資源設備を見つけた：防護服が数着、浄水、栄養ブロック、救急キット、建設に使うナノ物質。彼らはもう防護服を着ている。画面は真っ暗で、観客には彼らの声だけが聞こえる。");
            sb.AppendLine("【人物】");
            foreach (NpcData n in cast) sb.AppendLine(CastLine(n, true));
            sb.AppendLine("【条件】");
            sb.AppendLine($"- 台詞を {minLines}〜{maxLines} 行書く。全員が必ず話し、それぞれの性格・話し方に合わせる。");
            sb.AppendLine("- 内容：この資源をどう使うか。これがあれば生き延びられるのか、出口さえ見つかるのか。楽観する人、疑う人、怖がる人がいる。");
            sb.AppendLine("- 最後の一行：誰かが防護服をいじっていて、うっかり胸のスイッチを押してしまう（本人はそれが何か知らない）。例：「あれ、このボタンは何だ——」");
            sb.AppendLine("- 一行は30文字以内。口に出す言葉だけを書き、ナレーション・動作・表情の描写は書かない。");
            sb.AppendLine("- 台詞の中で互いを名前で呼ばない（観客はまだ彼らの名前を知らない）。「あんた」「みんな」などで呼ぶこと。");
            sb.AppendLine("- 彼らは防護服にAIが入っていることをまだ知らない。ゲーム・プレイヤー・ロールプレイには触れない。");
            sb.AppendLine("【出力形式】JSONオブジェクトを1つだけ出力すること：{\"lines\":[{\"name\":\"話す人の名前\",\"text\":\"台詞\"}]}");
        }
        else
        {
            sb.AppendLine("你是一部亚空间生存剧的编剧，要写开场第一场戏的台词。");
            sb.AppendLine("【世界】现代地球上开始出现会吞噬附近普通市民的空间漩涡，漩涡内部是名为「亚空间」的异常领域：时间、空间、因果都不可靠，待得越久，精神越被侵蚀。各国联合成立了「特异现象调查局」(ABA)。");
            sb.AppendLine("【处境】下面这几个人被卷进亚空间已经有一段日子了，一直在一处废弃营地附近勉强撑着。刚才，他们在营地里找到了一批被遗弃在亚空间的资源设备：几套防护服、净化水、营养块、急救包、一些建造用的纳米物质。他们已经把防护服穿上了。画面是全黑的，观众只听得到他们说话。");
            sb.AppendLine("【人物】");
            foreach (NpcData n in cast) sb.AppendLine(CastLine(n, false));
            sb.AppendLine("【要求】");
            sb.AppendLine($"- 写 {minLines}~{maxLines} 句台词，每个人都要开口，说话要符合各自的性格和说话风格。");
            sb.AppendLine("- 内容：这些资源该怎么用；有了这些是不是就能活下去，甚至找到出去的办法；有人乐观，有人怀疑，有人害怕。");
            sb.AppendLine("- 最后一句：有人摆弄防护服时，无意间按到了胸前的一个开关（他不知道那是什么），比如「咦，这个按钮是干什么的——」。");
            sb.AppendLine("- 每句不超过 30 字，只写说出口的话，不要旁白、动作、神态描写。");
            sb.AppendLine("- 台词里不要互相叫名字（观众还不知道他们是谁），用「你」「大家」「兄弟」之类的称呼。");
            sb.AppendLine("- 他们还不知道防护服里有 AI；不得提到游戏、玩家、角色扮演。");
            sb.AppendLine("【输出格式】只输出一个 JSON 对象：{\"lines\":[{\"name\":\"说话人的名字\",\"text\":\"台词\"}]}");
        }
        if (LocalizationManager.Instance != null) sb.AppendLine(LocalizationManager.Instance.GetAILanguageInstruction());
        return sb.ToString();
    }

    static string CastLine(NpcData n, bool jp)
    {
        StringBuilder sb = new StringBuilder("・").Append(n.npcName).Append("（").Append(n.gender);
        if (!string.IsNullOrEmpty(n.profession)) sb.Append(jp ? "、" : "，").Append(n.profession);
        sb.Append("）");
        if (n.traits != null && n.traits.Count > 0) sb.Append(jp ? "：性格 " : "：性格 ").Append(string.Join(jp ? "・" : "、", n.traits));
        if (!string.IsNullOrEmpty(n.speakingStyle)) sb.Append(jp ? "／話し方 " : "；说话风格 ").Append(n.speakingStyle);
        string brief = NpcActors.Brief(n, jp);
        if (!string.IsNullOrEmpty(brief)) sb.Append(jp ? "／経歴 " : "；背景 ").Append(brief);
        return sb.ToString();
    }

    List<Line> ParseLines(string raw, List<NpcData> cast)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
            if (a < 0 || b <= a) return null;
            JObject o = JObject.Parse(raw.Substring(a, b - a + 1));
            JArray arr = o["lines"] as JArray;
            if (arr == null) return null;
            List<Line> list = new List<Line>();
            foreach (JToken t in arr)
            {
                NpcData n = FindByName(cast, (string)t["name"]);
                string text = (string)t["text"];
                if (n == null || string.IsNullOrEmpty(text)) continue;
                text = DialogueCleaner.Clean(text, n.npcName);
                if (string.IsNullOrEmpty(text)) continue;
                text = text.Trim().Trim('「', '」', '"', '“', '”');
                if (text.Length == 0) continue;
                list.Add(new Line { npc = n, text = text });
            }
            if (list.Count > maxLines)
            {
                Line last = list[list.Count - 1];
                list.RemoveRange(maxLines - 1, list.Count - (maxLines - 1));
                list.Add(last);
            }
            return list.Count >= 3 ? list : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[开场] 台词解析失败：" + e.Message);
            return null;
        }
    }

    static NpcData FindByName(List<NpcData> cast, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        name = name.Trim();
        foreach (NpcData n in cast) if (n.npcName == name) return n;
        foreach (NpcData n in cast) if (!string.IsNullOrEmpty(n.npcName) && (name.Contains(n.npcName) || n.npcName.Contains(name))) return n;
        return null;
    }

    // 本地台词（14B 写不出来时）：8 句轮流分给每个人。
    List<Line> FallbackLines(List<NpcData> cast)
    {
        List<Line> list = new List<Line>();
        if (cast.Count == 0) return list;
        List<NpcData> order = new List<NpcData>(cast);
        for (int i = order.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); NpcData tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
        for (int i = 1; i <= 8; i++) list.Add(new Line { npc = order[(i - 1) % order.Count], text = L("open_fb_0" + i) });
        return list;
    }

    // 最后一句一定要是"按到了开关"（AI 写漏了就补一句）。
    void EnsureSwitchLine(List<Line> lines, List<NpcData> cast)
    {
        if (cast.Count == 0) return;
        string[] keys = { "按钮", "开关", "按到", "按了", "按下", "ボタン", "スイッチ", "押し", "押す" };
        if (lines.Count > 0)
        {
            string last = lines[lines.Count - 1].text;
            foreach (string k in keys) if (last.Contains(k)) return;
        }
        lines.Add(new Line { npc = cast[UnityEngine.Random.Range(0, cast.Count)], text = L("open_fb_switch") });
    }

    // 开场字幕：地点 + 时间。
    IEnumerator Caption(string a, string b)
    {
        _center.fontSize = 28f;
        _center.color = UiStyle.Green;
        _center.fontStyle = FontStyles.Normal;
        yield return TypeInto(_center, "[ " + a + " ]", 18f);
        _sub.text = b;
        _sub.alpha = 0f;
        float t = 0f;
        while (t < 0.5f && !_skip) { t += Time.unscaledDeltaTime; _sub.alpha = t / 0.5f; yield return null; }
        _sub.alpha = 1f;
        yield return Hold(1.8f);
        t = 0f;
        while (t < 0.5f) { t += Time.unscaledDeltaTime; _center.alpha = _sub.alpha = 1f - t / 0.5f; yield return null; }
        _center.text = _sub.text = "";
        _center.alpha = _sub.alpha = 1f;
    }

    // 环境声（画面中间淡淡的一行斜体）。stop 返回 true 时提前结束（AI 的台词到了）。
    IEnumerator Ambient(string text, float seconds, Func<bool> stop)
    {
        _center.fontSize = 19f;
        _center.color = new Color(0.72f, 1f, 0.78f, 0.55f);
        _center.fontStyle = FontStyles.Italic;
        _center.text = text;
        float t = 0f;
        while (t < seconds && !_skip)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Min(1f, t / 0.35f) * Mathf.Min(1f, (seconds - t) / 0.35f);
            _center.alpha = Mathf.Clamp01(a);
            if (stop != null && stop() && t > 0.6f) break;
            yield return null;
        }
        _center.text = "";
        _center.alpha = 1f;
        _center.fontStyle = FontStyles.Normal;
    }

    IEnumerator TypeInto(TextMeshProUGUI t, string text, float cps)
    {
        t.text = text;
        t.alpha = 1f;
        t.maxVisibleCharacters = 0;
        float shown = 0f;
        while (shown < text.Length && !_skip)
        {
            shown += Time.unscaledDeltaTime * cps;
            t.maxVisibleCharacters = Mathf.FloorToInt(shown);
            yield return null;
        }
        t.maxVisibleCharacters = 99999;
    }

    // 等几秒；点击 / 空格提前结束，Esc 直接结束。
    IEnumerator Hold(float seconds)
    {
        _advance = false;
        float t = 0f;
        while (t < seconds && !_advance && !_skip) { t += Time.unscaledDeltaTime; yield return null; }
        _advance = false;
    }

    // 一句台词：在说话人的位置弹出对话框，打字，停一会儿（点击跳过打字 / 下一句）。
    IEnumerator SayLine(Line ln)
    {
        Box box = AddBox(Unknown, ln.text, _slotX.TryGetValue(ln.npc.id, out float x) ? x : 0f);
        _advance = false;
        float shown = 0f;
        while (shown < ln.text.Length && !_skip)
        {
            if (_advance) { _advance = false; break; }
            shown += Time.unscaledDeltaTime * typeSpeed;
            box.text.maxVisibleCharacters = Mathf.FloorToInt(shown);
            yield return null;
        }
        box.text.maxVisibleCharacters = 99999;
        yield return Hold(Mathf.Clamp(1.1f + ln.text.Length * 0.06f, 1.8f, 4.5f));
    }

    // ================= 对话框（第一场）=================

    class Box
    {
        public RectTransform rt;
        public CanvasGroup cg;
        public TextMeshProUGUI text;
        public float y, targetY, alpha, targetAlpha = 1f, height;
    }

    const float BoxW = 500f, BoxGap = 16f, BoxBaseY = -40f;

    Box AddBox(string name, string text, float slot)
    {
        float W = _root.rect.width;
        RectTransform rt = NewRect("Line", _stage);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.06f, 0.045f, 0.94f);
        bg.raycastTarget = false;
        UiStyle.Frame(rt, new Color(UiStyle.Green.r, UiStyle.Green.g, UiStyle.Green.b, 0.55f), 1.2f);
        UiStyle.Tag(rt, name, UiStyle.Green, 13f);
        TextMeshProUGUI t = NewText(rt, 21f, UiStyle.TextMain, TextAlignmentOptions.TopLeft, _font);
        t.textWrappingMode = TextWrappingModes.Normal;
        RectTransform tr = t.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(14f, 12f);
        tr.offsetMax = new Vector2(-14f, -28f);
        t.text = text;
        t.maxVisibleCharacters = 0;
        float h = Mathf.Ceil(t.GetPreferredValues(text, BoxW - 28f, 0f).y) + 40f;
        float x = Mathf.Clamp(slot * W, -W * 0.5f + BoxW * 0.5f + 24f, W * 0.5f - BoxW * 0.5f - 24f);
        rt.sizeDelta = new Vector2(BoxW, h);
        CanvasGroup cg = rt.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        Box box = new Box { rt = rt, cg = cg, text = t, height = h, targetY = BoxBaseY, y = BoxBaseY - 24f };
        rt.anchoredPosition = new Vector2(x, box.y);
        // 旧的往上推、变淡；超过 3 条的淡出
        for (int i = _boxes.Count - 1; i >= 0; i--) _boxes[i].targetY += h + BoxGap;
        _boxes.Add(box);
        for (int i = 0; i < _boxes.Count; i++)
        {
            int age = _boxes.Count - 1 - i;
            _boxes[i].targetAlpha = age == 0 ? 1f : age == 1 ? 0.6f : age == 2 ? 0.34f : 0f;
        }
        return box;
    }

    void UpdateBoxes(float dt)
    {
        for (int i = _boxes.Count - 1; i >= 0; i--)
        {
            Box b = _boxes[i];
            if (b.rt == null) { _boxes.RemoveAt(i); continue; }
            b.y = Mathf.Lerp(b.y, b.targetY, 1f - Mathf.Exp(-dt * 10f));
            b.alpha = Mathf.MoveTowards(b.alpha, b.targetAlpha, dt * 4f);
            b.rt.anchoredPosition = new Vector2(b.rt.anchoredPosition.x, b.y);
            b.cg.alpha = b.alpha;
            if (b.targetAlpha <= 0f && b.alpha <= 0.001f) { Destroy(b.rt.gameObject); _boxes.RemoveAt(i); }
        }
    }

    // ================= AI 启动 =================

    IEnumerator Boot()
    {
        _skip = false;
        // 4B 观察者：启动一开始就去挑领导（小模型和主模型互不排队），第二场戏用结果
        NpcSociety.PickLeader((n, why) => { if (_leaderLocked) return; _leader = n; _reason = why; _picked = true; });

        float W = _root.rect.width, H = _root.rect.height;
        _con = new TerminalConsole(_bootRoot, 70f, 46f, W - 140f, H - 92f);
        _con.SetPrompt("ssu://boot");
        _con.SetStatus("SSU-AI  //  BOOT", "");
        _con.Echo("");
        _con.CrtOn();
        _bootT0 = Time.realtimeSinceStartup;
        while (_con.CrtBusy) yield return null;

        Out("SSU-AI  //  " + L("boot_title"), TerminalConsole.Bright);
        Out(L("boot_sub"), TerminalConsole.Dim);
        Rule();
        yield return Pause(0.4f);

        // 电源 / 内存
        Out(Stamp() + Col(LF("boot_power", UnityEngine.Random.Range(91, 99)), Ok("OK")));
        yield return Pause(0.25f);
        yield return Dots(L("boot_mem"), Ok("2048 MB  OK"), 0.35f);

        // 推理服务（本地 Ollama：真的去连；云端：显示服务商）
        AIClientHub hub = AIClientHub.Instance;
        bool local = hub == null || hub.provider == AIProvider.LocalOllama;
        OllamaAIClient oc = OllamaAIClient.Instance;
        OllamaAIClient.ProbeResult probe = null;
        if (oc != null)
        {
            bool pd = false;
            oc.Probe(r => { probe = r; pd = true; });
            string st = Stamp(), label = LF("boot_server", oc.ServerHost);
            BLine l = Out(st + Col(label, ""));
            float t = 0f;
            while (!pd && t < 8f) { t += Time.unscaledDeltaTime; l.text = st + Col(label, Spinner() + " ..."); Flush(false); yield return null; }
            l.text = st + Col(label, probe != null && probe.ok ? Ok(LF("boot_server_ok", probe.version)) : Bad(L("boot_server_ng")));
            Flush(false);
        }
        if (!local) Out(Stamp() + Col(LF("boot_server_cloud", hub.provider.ToString()), Ok("OK")));

        // 主模型：已在显存 / 当场预热 / 没装
        if (local && oc != null && probe != null && probe.ok)
        {
            OllamaAIClient.ModelState m = probe.main;
            string st = Stamp(), label = LF("boot_main", m.name, Detail(m));
            BLine l = Out(st + Col(label, ""));
            yield return WaitTyping();
            if (!m.installed) l.text = st + Col(label, Bad(LF("boot_main_missing", m.name)));
            else if (m.loaded) l.text = st + Col(label, Ok(LF("boot_main_loaded", Gb(m.sizeVram))));
            else
            {
                bool wd = false, wok = false;
                float secs = 0f, t = 0f;
                oc.WarmMain((ok, s) => { wok = ok; secs = s; wd = true; });
                while (!wd && !_skip && t < warmupWaitMax)
                {
                    t += Time.unscaledDeltaTime;
                    float p = 0.95f * (1f - Mathf.Exp(-t / 6f));
                    l.text = st + Col(label, BarText(p) + "  " + L("boot_main_loading"));
                    Flush(false);
                    yield return null;
                }
                if (wd && wok)
                {
                    bool pd2 = false;
                    oc.Probe(r => { if (r != null && r.ok) probe = r; pd2 = true; });
                    float t2 = 0f;
                    while (!pd2 && t2 < 3f) { t2 += Time.unscaledDeltaTime; yield return null; }
                    l.text = st + Col(label, Ok(LF("boot_main_done", secs.ToString("0.0"), Gb(probe.main.sizeVram))));
                }
                else l.text = st + Col(label, wd ? Bad("NG") : Warn(L("boot_main_bg")));
            }
            Flush(false);
        }

        // 辅助模型（4B：输入分类、隐形参谋、选领导的观察者）
        if (oc != null)
        {
            string st = Stamp(), label = LF("boot_lite", oc.liteModelName);
            BLine l = Out(st + Col(label, ""));
            if (!oc.liteEnabled) l.text = st + Col(label, Warn(L("boot_lite_off")));
            else
            {
                float t = 0f;
                while (oc.LiteStatus == "..." && !_skip && t < liteWaitMax) { t += Time.unscaledDeltaTime; l.text = st + Col(label, Spinner() + " ..."); Flush(false); yield return null; }
                if (oc.LiteReady) l.text = st + Col(label, Ok(LF("boot_lite_ok", probe != null && probe.lite.loaded ? Gb(probe.lite.sizeVram) : "")));
                else if (oc.LiteStatus == "NG") l.text = st + Col(label, Warn(L("boot_lite_ng")));
                else l.text = st + Col(label, Warn(L("boot_main_bg")));
            }
            Flush(false);
        }

        // 视觉 / 听觉
        List<NpcData> cast = Alive();
        FogVisionUpdater fog = FindFirstObjectByType<FogVisionUpdater>();
        yield return Dots(LF("boot_vision", cast.Count, fog != null ? fog.sightRadius : 4), Ok("OK"), 0.45f);
        yield return Dots(LF("boot_hearing", cast.Count, L("boot_lang_name")), Ok("OK"), 0.35f);

        // 知识库：真实条目数
        int items = ItemDatabase.Instance != null ? ItemDatabase.Instance.items.Count : 0;
        int builds = BuildManager.Instance != null && BuildManager.Instance.buildables != null ? BuildManager.Instance.buildables.Length : 0;
        int techs = TerminalDatabase.Instance != null ? TerminalDatabase.Instance.techs.Count : 0;
        int recipes = TerminalDatabase.Instance != null ? TerminalDatabase.Instance.recipes.Count : 0;
        int lore = (WorldLore.Background ?? "").Length + (WorldLore.WorldLaws ?? "").Length + (WorldLore.NpcWorld ?? "").Length + (WorldLore.EventThemes ?? "").Length;
        yield return BarStep(LF("boot_kb", items, builds, techs, recipes, lore), Ok(L("boot_kb_ok")), 0.6f);

        // 人格建模：等 14B 写完每个人的演员提示词
        yield return Persona(cast);

        // 地形 / 导航 / 营地
        GridManager g = GridManager.Instance;
        int gw = g != null ? g.gridWidth : 0, gh = g != null ? g.gridHeight : 0, res = 0, water = 0;
        foreach (ResourceNode r in ResourceNode.All) if (r != null && !r.depleted && r.kind != ResourceKind.Grass) res++;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
                if (TerrainSystem.TypeAt(new Vector2Int(x, y)) == TerrainType.Water) water++;
        yield return BarStep(LF("boot_terrain", gw, gh, res, water), Ok(L("boot_render_ok")), 0.7f);
        yield return Dots(L("boot_nav"), Ok("OK"), 0.25f);
        CampZone z = CampZone.Instance;
        if (z != null && z.Ready && g != null)
        {
            Vector2Int c = g.WorldToCell(NpcNav.CampCenter(Vector3.zero));
            yield return Dots(LF("boot_camp", c.x, c.y, z.Size.x, z.Size.y), Ok("OK"), 0.3f);
        }
        else yield return Dots(L("boot_camp_none"), Warn("—"), 0.2f);
        yield return Dots(L("boot_hud"), Ok(L("boot_hud_ok")), 0.3f);
        Rule();
        Out(L("boot_ready"), TerminalConsole.Bright);
        yield return WaitTyping();
        _skip = false;
        yield return Pause(1f);
    }

    IEnumerator Persona(List<NpcData> cast)
    {
        int total = cast.Count, traits = 0;
        foreach (NpcData n in cast) if (n.traits != null) traits += n.traits.Count;
        string st = Stamp(), label = LF("boot_persona", total, traits);
        BLine l = Out(st + Col(label, BarText(0f)));
        AIClientHub hub = AIClientHub.Instance;
        bool bgOk = hub != null && hub.backgroundEnabled && (hub.provider == AIProvider.LocalOllama || hub.backgroundOnCloud);
        if (!bgOk || total == 0)
        {
            l.text = st + Col(label, Warn(L("boot_persona_skip")));
            Flush(false);
            yield break;
        }
        float t = 0f;
        while (!_skip && t < personaWaitMax)
        {
            int done = PersonaDone(cast);
            int pending = LlmJobQueue.Instance != null ? LlmJobQueue.Instance.Pending : 0;
            l.text = st + Col(label, BarText((float)done / total) + "  " + done + "/" + total + "  " + Spinner());
            Flush(false);
            if (done >= total || pending == 0) break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        int d = PersonaDone(cast);
        l.text = st + Col(label, d >= total ? Ok(L("boot_persona_done")) : Warn(LF("boot_persona_partial", d, total)));
        Flush(false);
    }

    static int PersonaDone(List<NpcData> cast)
    {
        int k = 0;
        foreach (NpcData n in cast)
        {
            NpcActorProfile p = NpcActors.Get(n.id);
            if (p != null && p.source == "llm") k++;
        }
        return k;
    }

    // —— 启动日志的小工具 ——

    BLine Out(string text, Color color = default(Color))
    {
        BLine l = new BLine { text = text ?? "", color = color };
        _bl.Add(l);
        Flush(true);
        return l;
    }

    void Rule()
    {
        _bl.Add(new BLine { rule = true });
        Flush(true);
    }

    // type = true：新加的行打字；false：只刷新内容（进度条、点点点）
    void Flush(bool type)
    {
        if (_con == null) return;
        int cap = _con.Capacity;
        int start = Mathf.Max(0, _bl.Count - cap);
        _bspecs.Clear();
        for (int i = start; i < _bl.Count; i++)
            _bspecs.Add(new TerminalConsole.Spec { text = _bl[i].text, color = _bl[i].color, rule = _bl[i].rule });
        if (type && _bPrinted < _bl.Count && !_skip) _con.SetPageFrom(_bspecs, Mathf.Clamp(_bPrinted - start, 0, _bspecs.Count), 1500f);
        else if (type) _con.SetPageFrom(_bspecs, _bspecs.Count);
        else _con.SetPage(_bspecs, false);
        _bPrinted = _bl.Count;
    }

    IEnumerator WaitTyping()
    {
        while (_con != null && _con.Revealing && !_skip) yield return null;
    }

    IEnumerator Pause(float seconds)
    {
        float t = 0f;
        while (t < seconds && !_skip) { t += Time.unscaledDeltaTime; yield return null; }
    }

    IEnumerator Dots(string label, string status, float seconds)
    {
        string st = Stamp();
        BLine l = Out(st + Col(label, ""));
        yield return WaitTyping();
        for (int i = 1; i <= 6 && !_skip; i++)
        {
            l.text = st + Col(label, new string('.', i * 2));
            Flush(false);
            yield return Pause(seconds / 6f);
        }
        l.text = st + Col(label, status);
        Flush(false);
    }

    IEnumerator BarStep(string label, string status, float seconds)
    {
        string st = Stamp();
        BLine l = Out(st + Col(label, BarText(0f)));
        yield return WaitTyping();
        float t = 0f;
        while (t < seconds && !_skip)
        {
            t += Time.unscaledDeltaTime;
            l.text = st + Col(label, BarText(t / seconds));
            Flush(false);
            yield return null;
        }
        l.text = st + Col(label, status);
        Flush(false);
    }

    string Stamp() => "[" + (Time.realtimeSinceStartup - _bootT0).ToString("0.000").PadLeft(8) + "] ";

    // 状态放在一栏里对齐（标签太长就直接跟在后面）
    string Col(string label, string status)
    {
        if (string.IsNullOrEmpty(status)) return label;
        bool fits = _con != null && _con.Measure(label) < _con.LinesWidth * 0.6f - 16f;
        return fits ? label + "<pos=60%>" + status : label + "   " + status;
    }

    string Spinner() => "|/-\\".Substring(Mathf.FloorToInt(_time * 10f) % 4, 1);

    static string BarText(float p)
    {
        p = Mathf.Clamp01(p);
        int k = Mathf.RoundToInt(20f * p);
        return "[" + new string('#', k) + new string('.', 20 - k) + "] " + Mathf.RoundToInt(p * 100f).ToString().PadLeft(3) + "%";
    }

    static string Ok(string s) => "<color=" + TerminalConsole.OkHex + ">" + s + "</color>";
    static string Warn(string s) => "<color=" + TerminalConsole.WarnHex + ">" + s + "</color>";
    static string Bad(string s) => "<color=" + TerminalConsole.BadHex + ">" + s + "</color>";

    static string Detail(OllamaAIClient.ModelState m)
    {
        List<string> parts = new List<string>();
        if (!string.IsNullOrEmpty(m.parameterSize)) parts.Add(m.parameterSize);
        if (!string.IsNullOrEmpty(m.quantization)) parts.Add(m.quantization);
        return string.Join(" · ", parts);
    }

    static string Gb(long bytes) => bytes > 0 ? (bytes / 1073741824.0).ToString("0.0") + " GB" : "";

    // ================= 面罩拉开 + HUD 亮起 =================

    IEnumerator Reveal()
    {
        _skip = false;
        if (CameraController.Instance != null) CameraController.Instance.LookAt(NpcNav.CampCenter(Vector3.zero));
        TickFog();
        bool off = false;
        _con.CrtOff(() => off = true);
        float t = 0f;
        while (!off && t < 1.5f) { t += Time.unscaledDeltaTime; yield return null; }
        _bootRoot.gameObject.SetActive(false);
        _bootT0 = -1f;

        // 黑幕换成上下两片面罩，中间一道亮线闪一下，然后往上下拉开
        _visorTop.gameObject.SetActive(true);
        _visorBottom.gameObject.SetActive(true);
        _black.enabled = false;
        _visorLine.gameObject.SetActive(true);
        t = 0f;
        while (t < 0.35f)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI);
            _visorLine.color = new Color(0.85f, 1f, 0.9f, a);
            _visorLine.rectTransform.sizeDelta = new Vector2(0f, 2f + 4f * a);
            yield return null;
        }
        _visorLine.gameObject.SetActive(false);
        float H = _root.rect.height;
        t = 0f;
        while (t < 0.8f)
        {
            t += Time.unscaledDeltaTime;
            float k = Ease(t / 0.8f);
            _visorTop.rectTransform.anchoredPosition = new Vector2(0f, H * 0.5f * k);
            _visorBottom.rectTransform.anchoredPosition = new Vector2(0f, -H * 0.5f * k);
            yield return null;
        }
        _visorTop.gameObject.SetActive(false);
        _visorBottom.gameObject.SetActive(false);
        yield return RevealHud();
    }

    // HUD 一块一块闪着亮起来（像系统逐个上线）。
    IEnumerator RevealHud()
    {
        HideHudScan();
        _hudHidden = false;
        _hud.RemoveAll(h => h.canvas == null);
        _hud.Sort((a, b) => a.canvas.sortingOrder.CompareTo(b.canvas.sortingOrder));
        const float step = 0.08f;
        float total = step * _hud.Count + 0.15f, t = 0f;
        while (t < total)
        {
            t += Time.unscaledDeltaTime;
            for (int i = 0; i < _hud.Count; i++)
            {
                float lt = t - i * step;
                SetHud(_hud[i], lt >= 0f && !(lt > 0.05f && lt < 0.1f));
            }
            yield return null;
        }
        foreach (HudCanvas h in _hud) RestoreHud(h);
        _hud.Clear();
    }

    void HideHudScan()
    {
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || c == _canvas || !c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            bool known = false;
            foreach (HudCanvas h in _hud) if (h.canvas == c) { known = true; break; }
            if (known) continue;
            HudCanvas hc = new HudCanvas { canvas = c };
            CanvasGroup g = c.GetComponent<CanvasGroup>();
            if (g == null)
            {
                hc.group = c.gameObject.AddComponent<CanvasGroup>();
                hc.addedGroup = true;
                hc.group.alpha = 0f;
                hc.group.blocksRaycasts = false;
                hc.group.interactable = false;
            }
            else if (c.enabled)
            {
                c.enabled = false;
                hc.disabledCanvas = true;
            }
            else continue;
            _hud.Add(hc);
        }
    }

    static void SetHud(HudCanvas h, bool on)
    {
        if (h.canvas == null) return;
        if (h.addedGroup && h.group != null) h.group.alpha = on ? 1f : 0f;
        else if (h.disabledCanvas) h.canvas.enabled = on;
    }

    static void RestoreHud(HudCanvas h)
    {
        if (h.canvas == null) return;
        if (h.addedGroup && h.group != null) Destroy(h.group);   // 去掉临时加的 CanvasGroup
        else if (h.disabledCanvas) h.canvas.enabled = true;
    }

    // 世界静止时揭雾不会自己更新：手动让 NPC 周围揭开一次。
    static void TickFog()
    {
        foreach (FogVisionUpdater f in FindObjectsByType<FogVisionUpdater>(FindObjectsSortMode.None))
            if (f != null) f.SendMessage("UpdateVision", SendMessageOptions.DontRequireReceiver);
    }

    // ================= 第二场：讨论 → 投票 =================

    IEnumerator Scene2()
    {
        _skip = false;
        _advance = false;
        List<NpcData> cast = Alive();
        if (cast.Count == 0) yield break;
        SetHint(true);
        BuildCaption();

        // 1. 讨论中（每个人头上 ……）；顺便等观察者（4B）的结果
        SetCaption(L("open_s2_discuss"), "", true);
        foreach (NpcData n in cast) AddBubble(n, "", true);
        float t = 0f;
        while ((t < discussSeconds || !_picked) && t < discussSeconds + 20f && !_skip)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        _leaderLocked = true;   // 从这里开始结果不再变（观察者晚到的结果不要了）
        if (_leader == null || _leader.IsDead || _leader.isBroken) { _leader = NpcSociety.RunnerUp(null); _reason = null; }
        if (_leader == null) yield break;

        if (cast.Count == 1)
        {
            SetCaption(LF("open_s2_alone", Unknown), "", false);
            _votes = 1;
            _total = 1;
            _skip = false;
            yield return Hold(2.5f);
            yield break;
        }

        // 2. 要团队合作才能活下去
        foreach (Bubble b in _bubbles) SetBubble(b, "", false);
        SetCaption(L("open_s2_team"), "", false);
        _skip = false;
        yield return Hold(2.4f);

        // 3. 投票（结果其实是观察者挑的人）
        SetCaption(L("open_s2_vote"), "", false);
        Dictionary<NpcData, int> tally = new Dictionary<NpcData, int>();
        List<KeyValuePair<NpcData, NpcData>> votes = BuildVotes(cast, _leader);
        foreach (KeyValuePair<NpcData, NpcData> v in votes)
        {
            Bubble b = BubbleOf(v.Key);
            if (b != null) SetBubble(b, v.Value != null ? LF("open_vote_for", Unknown) : L("open_vote_self"), false);
            if (v.Value != null)
            {
                tally.TryGetValue(v.Value, out int c);
                tally[v.Value] = c + 1;
                SetBadge(v.Value, c + 1);   // 名字不显示：票数直接标在被投的人头上（跳一下 = 这一票投给了他）
            }
            _capTally.text = TallyText(tally);
            if (!_skip) yield return Pause(voteInterval);
        }

        // 4. 结果
        tally.TryGetValue(_leader, out int lv);
        _votes = lv;
        _total = cast.Count;
        SetCaption(LF("open_s2_result", Unknown, lv, _total), string.IsNullOrEmpty(_reason) ? "" : LF("open_s2_reason", HideNames(_reason, cast)), false);
        foreach (Bubble b in _bubbles)
        {
            bool me = b.npc == _leader;
            b.frame.color = me ? UiStyle.Yellow : new Color(UiStyle.Green.r, UiStyle.Green.g, UiStyle.Green.b, 0.3f);
            b.cg.alpha = me ? 1f : 0.45f;
            if (b.badge != null) b.badgeCg.alpha = me ? 1f : 0.45f;
        }
        _skip = false;
        yield return Hold(2.8f);
    }

    // 谁投谁：大家投领导；领导投第二人选（只有两个人时说"我听大家的"）；5 个人以上有一个人投第二人选（像真的投票）。
    static List<KeyValuePair<NpcData, NpcData>> BuildVotes(List<NpcData> cast, NpcData leader)
    {
        NpcData ru = NpcSociety.RunnerUp(leader);
        List<NpcData> voters = new List<NpcData>(cast);
        for (int i = voters.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); NpcData tmp = voters[i]; voters[i] = voters[j]; voters[j] = tmp; }
        NpcData dissent = null;
        if (cast.Count >= 5 && ru != null)
        {
            List<NpcData> pool = new List<NpcData>();
            foreach (NpcData n in cast) if (n != leader && n != ru) pool.Add(n);
            if (pool.Count > 0) dissent = pool[UnityEngine.Random.Range(0, pool.Count)];
        }
        List<KeyValuePair<NpcData, NpcData>> list = new List<KeyValuePair<NpcData, NpcData>>();
        foreach (NpcData v in voters)
        {
            NpcData target = v == leader ? (cast.Count > 2 ? ru : null) : v == dissent ? ru : leader;
            list.Add(new KeyValuePair<NpcData, NpcData>(v, target));
        }
        return list;
    }

    static string TallyText(Dictionary<NpcData, int> tally)
    {
        List<KeyValuePair<NpcData, int>> list = new List<KeyValuePair<NpcData, int>>(tally);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        List<string> parts = new List<string>();
        foreach (KeyValuePair<NpcData, int> kv in list) parts.Add(Unknown + " " + new string('■', kv.Value) + " " + kv.Value);
        return string.Join("　　", parts);
    }

    // —— 字幕框（第二场，屏幕上方）——

    void BuildCaption()
    {
        if (_capRt != null) return;
        _capRt = NewRect("Caption", _root);
        _capRt.anchorMin = _capRt.anchorMax = _capRt.pivot = new Vector2(0.5f, 1f);
        _capRt.anchoredPosition = new Vector2(0f, -132f);
        _capRt.sizeDelta = new Vector2(760f, 112f);
        Image bg = _capRt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.06f, 0.045f, 0.9f);
        bg.raycastTarget = false;
        UiStyle.Frame(_capRt, new Color(UiStyle.Green.r, UiStyle.Green.g, UiStyle.Green.b, 0.6f), 1.2f);
        UiStyle.Tag(_capRt, "day 1 : meeting", UiStyle.Green, 13f);
        _capText = NewText(_capRt, 24f, UiStyle.TextMain, TextAlignmentOptions.Center, _font);
        Place(_capText.rectTransform, 0f, 24f, 760f, 34f);
        _capSub = NewText(_capRt, 16f, UiStyle.TextSub, TextAlignmentOptions.Center, _font);
        Place(_capSub.rectTransform, 0f, 58f, 760f, 22f);
        _capTally = NewText(_capRt, 16f, UiStyle.Green, TextAlignmentOptions.Center, _font);
        Place(_capTally.rectTransform, 0f, 82f, 760f, 22f);
        foreach (TextMeshProUGUI t in new[] { _capText, _capSub, _capTally }) t.textWrappingMode = TextWrappingModes.NoWrap;
    }

    void SetCaption(string text, string sub, bool dots)
    {
        _capBase = text;
        _capDots = dots;
        _capText.text = text;
        _capSub.text = sub ?? "";
    }

    void UpdateCaption()
    {
        if (_capText == null || !_capDots) return;
        _capText.text = _capBase + new string('.', 1 + Mathf.FloorToInt(_time * 2.5f) % 3);
    }

    // —— NPC 头上的小对话框（第二场）——

    class Bubble
    {
        public NpcData npc;
        public RectTransform rt;
        public CanvasGroup cg;
        public Image frame;
        public TextMeshProUGUI text;
        public bool dots;
        public float headY = float.NaN;   // 头顶的世界坐标 y（开场时世界静止，算一次就够）
        public RectTransform badge;       // 得票标记（■■ 2），在气泡上面
        public CanvasGroup badgeCg;
        public TextMeshProUGUI badgeText;
        public float pulse;               // 刚得一票时跳一下（1 → 0）
    }

    void AddBubble(NpcData n, string text, bool dots)
    {
        RectTransform rt = NewRect("Bubble_" + n.npcName, _root);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.06f, 0.045f, 0.92f);
        bg.raycastTarget = false;
        Image frame = UiStyle.Frame(rt, new Color(UiStyle.Green.r, UiStyle.Green.g, UiStyle.Green.b, 0.6f), 1.2f);
        TextMeshProUGUI t = NewText(rt, 16f, UiStyle.TextMain, TextAlignmentOptions.Center, _font);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        RectTransform tr = t.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(8f, 3f);
        tr.offsetMax = new Vector2(-8f, -3f);
        CanvasGroup cg = rt.gameObject.AddComponent<CanvasGroup>();
        Bubble b = new Bubble { npc = n, rt = rt, cg = cg, frame = frame, text = t };
        _bubbles.Add(b);
        SetBubble(b, text, dots);
    }

    void SetBubble(Bubble b, string text, bool dots)
    {
        b.dots = dots;
        string shown = dots ? "..." : text;
        b.text.text = shown;
        float w = string.IsNullOrEmpty(shown) ? 0f : Mathf.Ceil(b.text.GetPreferredValues(shown).x) + 18f;
        b.rt.sizeDelta = new Vector2(Mathf.Clamp(w, 46f, 300f), 28f);
        b.rt.gameObject.SetActive(!string.IsNullOrEmpty(shown));
    }

    // 某人头上的得票标记：第一次得票时建出来，之后只改数字并跳一下。
    void SetBadge(NpcData n, int count)
    {
        Bubble b = BubbleOf(n);
        if (b == null) return;
        if (b.badge == null)
        {
            RectTransform rt = NewRect("VoteBadge", _root);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.07f, 0.02f, 0.92f);
            bg.raycastTarget = false;
            UiStyle.Frame(rt, UiStyle.Yellow, 1.2f);
            TextMeshProUGUI t = NewText(rt, 15f, UiStyle.Yellow, TextAlignmentOptions.Center, _font);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform tr = t.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(6f, 2f);
            tr.offsetMax = new Vector2(-6f, -2f);
            b.badge = rt;
            b.badgeText = t;
            b.badgeCg = rt.gameObject.AddComponent<CanvasGroup>();
        }
        string s = new string('■', count) + " " + count;
        b.badgeText.text = s;
        b.badge.sizeDelta = new Vector2(Mathf.Max(40f, Mathf.Ceil(b.badgeText.GetPreferredValues(s).x) + 14f), 24f);
        b.pulse = 1f;
    }

    Bubble BubbleOf(NpcData n)
    {
        foreach (Bubble b in _bubbles) if (b.npc == n) return b;
        return null;
    }

    void UpdateBubbles()
    {
        if (_bubbles.Count == 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        foreach (Bubble b in _bubbles)
        {
            if (b.rt == null) continue;
            if (b.dots) b.text.text = new string('.', 1 + (Mathf.FloorToInt(_time * 3f) + (b.npc.id.GetHashCode() & 0x7fffffff) % 3) % 3);
            NpcMover m = NpcView.GetMover(b.npc.id);
            if (m == null) { b.cg.alpha = 0f; continue; }
            if (float.IsNaN(b.headY))
            {
                b.headY = m.transform.position.y + 1.1f;
                bool any = false;
                foreach (SpriteRenderer sr in m.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (sr == null || !sr.enabled) continue;
                    b.headY = any ? Mathf.Max(b.headY, sr.bounds.max.y + 0.15f) : sr.bounds.max.y + 0.15f;
                    any = true;
                }
            }
            Vector3 head = new Vector3(m.transform.position.x, b.headY, m.transform.position.z);
            Vector3 sp = cam.WorldToScreenPoint(head);
            if (sp.z < 0f) continue;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, sp, null, out Vector2 local))
            {
                b.rt.anchoredPosition = local;
                if (b.badge != null)
                {
                    // 气泡显示着就放在气泡上面，没显示就贴着头顶
                    b.badge.anchoredPosition = local + new Vector2(0f, b.rt.gameObject.activeSelf ? b.rt.sizeDelta.y + 4f : 0f);
                    b.pulse = Mathf.Max(0f, b.pulse - Time.unscaledDeltaTime * 3.5f);
                    b.badge.localScale = Vector3.one * (1f + 0.35f * b.pulse);
                }
            }
        }
    }

    // ================= 搭界面 =================

    void BuildUI()
    {
        GameObject go = new GameObject("OpeningCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1500;   // 盖住所有界面
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        _root = (RectTransform)go.transform;

        // 透明的挡板：开场期间点不到下面的界面和地图
        _blocker = FullImage("Blocker", new Color(0f, 0f, 0f, 0f));
        _blocker.raycastTarget = true;
        _black = FullImage("Black", Color.black);

        _stage = NewRect("Stage", _root);
        Fill(_stage);

        _center = NewText(_root, 26f, UiStyle.Green, TextAlignmentOptions.Center, _font);
        _center.rectTransform.anchorMin = _center.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _center.rectTransform.sizeDelta = new Vector2(1400f, 50f);
        _center.rectTransform.anchoredPosition = new Vector2(0f, 20f);
        _sub = NewText(_root, 18f, UiStyle.TextSub, TextAlignmentOptions.Center, _font);
        _sub.rectTransform.anchorMin = _sub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _sub.rectTransform.sizeDelta = new Vector2(1400f, 30f);
        _sub.rectTransform.anchoredPosition = new Vector2(0f, -24f);

        _bootRoot = NewRect("Boot", _root);
        Fill(_bootRoot);

        // 面罩：上下两片黑，拉开时露出地图
        _visorTop = FullImage("VisorTop", Color.black);
        _visorTop.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        _visorTop.gameObject.SetActive(false);
        _visorBottom = FullImage("VisorBottom", Color.black);
        _visorBottom.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        _visorBottom.gameObject.SetActive(false);
        _visorLine = FullImage("VisorLine", Color.white);
        _visorLine.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        _visorLine.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        _visorLine.rectTransform.sizeDelta = new Vector2(0f, 2f);
        _visorLine.gameObject.SetActive(false);

        _hint = NewText(_root, 14f, new Color(0.72f, 1f, 0.78f, 0.45f), TextAlignmentOptions.BottomRight, UiStyle.Mono);
        RectTransform hr = _hint.rectTransform;
        hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(1f, 0f);
        hr.anchoredPosition = new Vector2(-24f, 18f);
        hr.sizeDelta = new Vector2(700f, 22f);
        _hint.gameObject.SetActive(false);
    }

    void SetHint(bool on)
    {
        if (_hint == null) return;
        _hint.text = L("open_skip_hint");
        _hint.gameObject.SetActive(on);
        _hint.transform.SetAsLastSibling();
    }

    Image FullImage(string name, Color c)
    {
        RectTransform r = NewRect(name, _root);
        Fill(r);
        Image img = r.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI NewText(Transform parent, float size, Color color, TextAlignmentOptions align, TMP_FontAsset font)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    static List<NpcData> Alive()
    {
        List<NpcData> list = new List<NpcData>();
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w != null) foreach (NpcData n in w.npcs) if (n != null && !n.IsDead && !n.isBroken) list.Add(n);
        return list;
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] a) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, a) : key;
}
