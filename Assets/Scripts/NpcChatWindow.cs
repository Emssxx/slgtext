using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

// NPC 专属对话窗口（运行时自建 UI，不用在场景里摆）。
// 点场景里的 NPC / 幸存者列表 / 日志里的名字 → 弹出这个 NPC 的对话窗口：
//   上方标题栏：「会話 — 名字（职业）」，右边是 正気（精神状态）和 信頼（信任度）；
//   中间：他自己的聊天记录（来自 NpcMemory）——NPC 的话是左边的灰色气泡，玩家的话是右边的绿框气泡，
//         系统消息（开始行动、转交给别人……）是居中的绿色「>> 」行；
//   下方：「> 」开头的输入框，Enter 发送。
// 外观和 HUD / 锁定框同一套：深色底、淡绿色细边框。
// 打开动画：从这个 NPC 锁定框的左上角伸出一条线 → 线到达窗口的右上角后转成水平、往左变成窗口的上沿
//           → 窗口从这条线往下展开。
// 连线：展开后，锁定框 → 窗口右上角的那条线一直保留，跟着 NPC 和窗口移动（NPC 在屏幕外时连到屏幕边上）。
//       点这条线：镜头锁定这个 NPC，把他放在画面中心并一直跟随；再点一次、手动移动镜头或关窗口就解除。
// 操作：Enter 发送、Esc 先退出输入再关窗口、Y 重新打开上次的窗口；拖窗口可以挪动（拖不出屏幕）。
// 窗口只负责显示和输入；玩家发出的话交给 NpcAgent 处理（OnPlayerMessage）。
public class NpcChatWindow : MonoBehaviour
{
    public static NpcChatWindow Instance { get; private set; }

    // 输入框是否正在打字（其他系统据此屏蔽快捷键，见 ChatManager.InputFocused）。
    public static bool InputFocused => Instance != null && Instance.IsOpen && Instance._input != null && Instance._input.isFocused;

    // 玩家在窗口里发了一句话：(npcId, 内容)。
    public event Action<string, string> OnPlayerMessage;

    // 某个 NPC 的窗口被关掉 / 换成了别人（日志里点过变灰的名字据此恢复颜色）。
    public static event Action<string> OnNpcClosed;

    [Header("外观")]
    public TMP_FontAsset font;                              // 不拖会自动借用 NPC 列表等面板的中日文字体
    public Vector2 windowSize = new Vector2(440f, 560f);
    public Vector2 defaultPosition = new Vector2(350f, 16f); // 窗口左下角离屏幕左下角的位置（左上角抽屉的右边）
    public int maxShown = 60;                               // 窗口里最多显示多少条（完整记录在档案文件里）

    [Header("打开动画（秒）")]
    [Tooltip("线从锁定框左上角伸到窗口右上角")]
    public float lineTime = 0.16f;
    [Tooltip("线转成水平、往左变成窗口上沿")]
    public float edgeTime = 0.16f;
    [Tooltip("窗口从上沿往下展开")]
    public float unfoldTime = 0.2f;

    public bool IsOpen => _window != null && _window.gameObject.activeSelf;
    public string CurrentNpcId { get; private set; }
    public string LastNpcId { get; private set; }

    Canvas _canvas;
    RectTransform _canvasRt, _window, _body;
    CanvasGroup _windowGroup;
    TMP_Text _thinking, _placeholder, _title, _status;
    ScrollRect _scroll;
    RectTransform _scrollRt, _content;
    TMP_InputField _input;
    readonly HashSet<string> _thinkingIds = new HashSet<string>();
    int _scrollFrames;
    float _statusTimer;

    // —— 打开动画 ——
    RectTransform _segLine, _segEdge;   // 连线（锁定框 → 窗口右上角，展开后一直保留）和水平线（窗口上沿）
    Image _lineGlow;
    TMP_Text _trackLabel;               // 镜头跟随时，连线的 NPC 那头显示「[ TRACKING ]」
    bool _lineHover;
    bool _animating;
    float _animT;
    bool _hasOrigin;
    Vector2 _origin, _tl, _tr;          // 都是"画布左下角为原点"的坐标

    const float HeaderH = 44f;
    const float InputH = 36f;
    const float Pad = 10f;
    const float LabelH = 18f;
    const float MsgFont = 15f;

    static readonly Color Green = new Color(0.72f, 1f, 0.78f, 1f);          // 和天数数字、锁定框同色
    static readonly Color PanelColor = new Color(0.055f, 0.063f, 0.067f, 0.94f);
    static readonly Color HeaderColor = new Color(0.085f, 0.095f, 0.1f, 1f);
    static readonly Color NpcBubble = new Color(0.17f, 0.18f, 0.19f, 1f);
    static readonly Color NpcBorder = new Color(0.26f, 0.28f, 0.29f, 1f);
    static readonly Color PlayerBubble = new Color(0.05f, 0.075f, 0.06f, 1f);
    static readonly Color SystemColor = new Color(0.6f, 0.92f, 0.68f, 1f);
    static readonly Color LabelColor = new Color(0.62f, 0.66f, 0.66f, 1f);
    static readonly Color FieldBorder = new Color(0.25f, 0.27f, 0.28f, 1f);

    // 确保场景里有一个窗口（没有就创建）。
    public static NpcChatWindow Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("NpcChatWindow").AddComponent<NpcChatWindow>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (font == null) font = GameFonts.Find();
        Build();
        _window.gameObject.SetActive(false);
        NpcChat.OnPosted += HandlePosted;
        LocalizationManager.OnLanguageChanged += RefreshTexts;
    }

    void OnDestroy()
    {
        NpcChat.OnPosted -= HandlePosted;
        LocalizationManager.OnLanguageChanged -= RefreshTexts;
        if (Instance == this) Instance = null;
    }

    // ============ 打开 / 关闭 ============

    // 打开和某个 NPC 的对话窗口（已经开着别人的，就换成这个人，并从他的锁定框重新播一次打开动画）。
    public void Open(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return;
        string previous = IsOpen ? CurrentNpcId : null;
        if (previous != null && previous != npcId && OnNpcClosed != null) OnNpcClosed(previous);
        if (previous != null && previous != npcId) StopTracking(); // 换人了：镜头不再跟着上一个人

        CurrentNpcId = npcId;
        LastNpcId = npcId;
        _window.gameObject.SetActive(true);
        RefreshHeader();
        RebuildMessages();
        RefreshThinking();
        StartOpenAnimation(npcId);
        FocusInput();
    }

    // 重新打开上次的窗口（没有就找第一个活着的 NPC）。
    public void OpenLast()
    {
        string id = LastNpcId;
        if (string.IsNullOrEmpty(id) && WorldManager.Instance != null)
            foreach (NpcData n in WorldManager.Instance.world.npcs)
                if (!n.IsDead) { id = n.id; break; }
        if (!string.IsNullOrEmpty(id)) Open(id);
    }

    public void Close()
    {
        StopAnimation();
        StopTracking();
        _segLine.gameObject.SetActive(false);
        _trackLabel.gameObject.SetActive(false);
        if (_input != null) _input.DeactivateInputField();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        _window.gameObject.SetActive(false);
        string closed = CurrentNpcId;
        CurrentNpcId = null;
        if (!string.IsNullOrEmpty(closed) && OnNpcClosed != null) OnNpcClosed(closed);
    }

    // 在输入栏末尾插入一段文字（点地图格子时插入格子编号用）。窗口没开返回 false。
    public bool AppendToInput(string s)
    {
        if (!IsOpen || _input == null || string.IsNullOrEmpty(s)) return false;
        _input.text = string.IsNullOrEmpty(_input.text) ? s : _input.text + " " + s;
        FocusInput();
        _input.caretPosition = _input.text.Length;
        return true;
    }

    // 显示/隐藏"某某在想……"（等 LLM 回复时）。
    public void SetThinking(string npcId, bool on)
    {
        if (string.IsNullOrEmpty(npcId)) return;
        if (on) _thinkingIds.Add(npcId); else _thinkingIds.Remove(npcId);
        RefreshThinking();
    }

    // ============ 每帧 ============

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (IsOpen && kb.escapeKey.wasPressedThisFrame)
            {
                if (_input.isFocused) { _input.DeactivateInputField(); if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); }
                else Close();
            }
            else if (kb.yKey.wasPressedThisFrame && !ChatManager.InputFocused && !IsAnyInputSelected())
            {
                if (!IsOpen) OpenLast();
                else FocusInput();
            }
        }

        if (IsOpen)
        {
            if (_animating) TickAnimation(Time.unscaledDeltaTime);
            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f) { _statusTimer = 0.25f; RefreshHeader(); }
        }
    }

    void LateUpdate()
    {
        // 新消息加进来后，布局要一两帧才算准，这几帧都把滚动条拉到底。
        if (_scrollFrames > 0 && _scroll != null)
        {
            _scrollFrames--;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }
        if (IsOpen && !_animating) UpdateTether();
    }

    // 别的输入框（如果有）正被选中时，不抢 Y 键。
    static bool IsAnyInputSelected()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null) return false;
        return EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
    }

    // ============ 打开动画 ============

    void StartOpenAnimation(string npcId)
    {
        // 先按完整大小摆好、限制在屏幕内，再记下左上角 / 右上角
        _window.sizeDelta = windowSize;
        UiWindowDragger.ClampInside(_window);
        _tl = _window.anchoredPosition;                 // 窗口轴心在左上角，锚点在画布左下角
        _tr = _tl + new Vector2(windowSize.x, 0f);
        _hasOrigin = TryGetLockOnCorner(npcId, out _origin) && (_origin - _tr).sqrMagnitude > 16f;

        _animT = 0f;
        _animating = true;
        _window.sizeDelta = new Vector2(windowSize.x, 0f);
        _windowGroup.alpha = 0f;
        _windowGroup.blocksRaycasts = false;
        _segLine.gameObject.SetActive(false);
        _segEdge.gameObject.SetActive(false);
        TickAnimation(0f);
    }

    void TickAnimation(float dt)
    {
        _animT += dt;
        float a = _hasOrigin ? Mathf.Max(0.0001f, lineTime) : 0f;   // 没有起点：跳过斜线这一段
        float b = Mathf.Max(0.0001f, edgeTime);
        float c = Mathf.Max(0.0001f, unfoldTime);
        float t = _animT;

        if (t < a)
        {
            // ① 线从锁定框左上角伸向窗口右上角
            float p = EaseOut(t / a);
            SetSegment(_segLine, _origin, Vector2.Lerp(_origin, _tr, p));
            _segEdge.gameObject.SetActive(false);
            return;
        }
        t -= a;
        if (_hasOrigin) SetSegment(_segLine, _origin, _tr);   // 连线留着，不消失
        else _segLine.gameObject.SetActive(false);
        if (t < b)
        {
            // ② 到了右上角拐成水平，往左伸到左上角，变成窗口上沿
            float q = EaseInOut(t / b);
            SetSegment(_segEdge, _tr, Vector2.Lerp(_tr, _tl, q));
            return;
        }
        t -= b;
        SetSegment(_segEdge, _tr, _tl);
        _windowGroup.alpha = 1f;
        if (t < c)
        {
            // ③ 窗口从上沿往下展开（里面的内容不动，被窗口边缘逐渐露出来）
            float h = windowSize.y * EaseOut(t / c);
            _window.sizeDelta = new Vector2(windowSize.x, h);
            return;
        }
        FinishAnimation();
    }

    void FinishAnimation()
    {
        _animating = false;
        _window.sizeDelta = windowSize;
        _windowGroup.alpha = 1f;
        _windowGroup.blocksRaycasts = true;
        _segEdge.gameObject.SetActive(false);   // 上沿交给窗口自己的边框；连线由 UpdateTether 每帧更新
        _scrollFrames = 3;
    }

    // ============ 连线（锁定框 → 窗口右上角）============

    // 每帧：一头跟着 NPC 的锁定框左上角，一头跟着窗口右上角（窗口被拖动也跟着）。
    void UpdateTether()
    {
        bool tracking = IsTracking();
        if (!TryGetLockOnCorner(CurrentNpcId, out Vector2 from))
        {
            // NPC 不在了（死亡被移除等）：收起连线
            _segLine.gameObject.SetActive(false);
            _trackLabel.gameObject.SetActive(false);
            if (tracking) StopTracking();
            return;
        }
        Vector2 tr = _window.anchoredPosition + new Vector2(_window.sizeDelta.x, 0f);
        SetSegment(_segLine, from, tr);

        // 鼠标指着 / 正在跟随：线变粗、光变亮，表示可以点
        bool strong = _lineHover || tracking;
        _segLine.sizeDelta = new Vector2(_segLine.sizeDelta.x, strong ? 3f : 2f);
        _lineGlow.color = new Color(Green.r, Green.g, Green.b, strong ? 0.35f : 0.18f);

        _trackLabel.gameObject.SetActive(tracking);
        if (tracking) _trackLabel.rectTransform.anchoredPosition = from + new Vector2(0f, 6f);
    }

    // 点连线：镜头锁定 / 解锁这个 NPC。
    public void OnTetherClicked()
    {
        CameraController cam = CameraController.Ensure();
        NpcMover mover = NpcView.GetMover(CurrentNpcId);
        if (cam == null || mover == null) return;
        if (cam.IsFollowing(mover.transform)) cam.StopFollow();
        else cam.Follow(mover.transform);
    }

    public void OnTetherHover(bool on) { _lineHover = on; }

    bool IsTracking()
    {
        CameraController cam = CameraController.Instance;
        NpcMover mover = NpcView.GetMover(CurrentNpcId);
        return cam != null && mover != null && cam.IsFollowing(mover.transform);
    }

    // 关窗口 / 换人时：如果镜头正跟着这个人，就解除。
    void StopTracking()
    {
        if (IsTracking()) CameraController.Instance.StopFollow();
    }

    void StopAnimation()
    {
        if (_animating) FinishAnimation();
    }

    static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    // 把一条线段摆到 from → to（线段的轴心在左端，按角度旋转）。
    static void SetSegment(RectTransform seg, Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        float len = d.magnitude;
        bool on = len > 0.5f;
        if (seg.gameObject.activeSelf != on) seg.gameObject.SetActive(on);
        if (!on) return;
        seg.anchoredPosition = from;
        seg.sizeDelta = new Vector2(len, seg.sizeDelta.y);
        seg.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    // 这个 NPC 锁定框的左上角（画布左下角为原点）。NPC 不在场景里 / 在镜头后面时返回 false。
    bool TryGetLockOnCorner(string npcId, out Vector2 pos)
    {
        pos = Vector2.zero;
        Camera cam = Camera.main;
        NpcMover mover = NpcView.GetMover(npcId);
        if (cam == null || mover == null) return false;
        SpriteRenderer sr = mover.GetComponentInChildren<SpriteRenderer>();
        Bounds bounds = sr != null ? sr.bounds : new Bounds(mover.transform.position, new Vector3(0.6f, 0.9f, 0f));
        Vector3 sp = cam.WorldToScreenPoint(new Vector3(bounds.min.x, bounds.max.y, 0f));
        if (sp.z < 0f) return false;

        // 锁定框比 NPC 图片大一圈（HoverInfo.framePadding，按 1920 宽的画布算）→ 换算成屏幕像素
        float padUnits = HoverInfo.Instance != null ? HoverInfo.Instance.framePadding : 6f;
        float padPx = padUnits * Screen.width / 1920f;
        sp += new Vector3(-padPx, padPx, 0f);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, sp, null, out Vector2 local)) return false;
        Rect r = _canvasRt.rect;
        pos = local - r.min;                                           // 换成"左下角为原点"
        pos.x = Mathf.Clamp(pos.x, 0f, r.width);                      // 在屏幕外：从屏幕边上出发
        pos.y = Mathf.Clamp(pos.y, 0f, r.height);
        return true;
    }

    // ============ 标题栏 ============

    void RefreshHeader()
    {
        NpcData npc = Npc(CurrentNpcId);
        if (npc == null) return;
        string who = npc.npcName + (string.IsNullOrEmpty(npc.profession) ? "" : "（" + npc.profession + "）");
        _title.text = "<noparse>" + LF("chatwin_header", who) + "</noparse>";
        _status.text = "<color=#8E9490>" + L("stat_sanity") + "</color> " + MentalTag(npc) +
                       "　<color=#8E9490>" + L("stat_trust") + "</color> <color=#B8FFC7><b>" + Mathf.RoundToInt(npc.playerTrust) + "</b></color>";
    }

    static string MentalTag(NpcData n)
    {
        if (n.IsDead) return "<color=#8C8C8C>" + L("chatwin_dead") + "</color>";
        switch (MentalState.GetLevel(n))
        {
            case SanityLevel.Dazed:         return "<color=#FFE36A>" + L("mental_dazed") + "</color>";
            case SanityLevel.Hallucinating: return "<color=#C080FF>" + L("mental_halluc") + "</color>";
            case SanityLevel.Broken:        return "<color=#FF6A60>" + L("mental_broken") + "</color>";
            default:                        return "<color=#B8FFC7>" + L("mental_lucid") + "</color>";
        }
    }

    // ============ 消息 ============

    void HandlePosted(string npcId, ChatEntry e)
    {
        if (!IsOpen || npcId != CurrentNpcId || e == null) return;
        if (e.Role == ChatRole.Other) return; // 和别的 NPC 的闲聊只记在档案里，不显示在这个窗口
        AddLine(e);
        RefreshHeader();
    }

    void RebuildMessages()
    {
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            Transform c = _content.GetChild(i);
            if (_thinking != null && c == _thinking.transform) continue;
            c.SetParent(null);
            Destroy(c.gameObject);
        }
        List<ChatEntry> all = NpcMemory.All(CurrentNpcId);
        List<ChatEntry> shown = new List<ChatEntry>();
        for (int i = all.Count - 1; i >= 0 && shown.Count < maxShown; i--)
            if (all[i].Role != ChatRole.Other) shown.Insert(0, all[i]);
        foreach (ChatEntry e in shown) AddLine(e, false);
        ScrollToBottom();
    }

    // 内容区的宽度（窗口宽 - 滚动区两边 - 排版留白）。
    float ContentWidth => windowSize.x - 16f - 16f;

    void AddLine(ChatEntry e, bool scroll = true)
    {
        switch (e.Role)
        {
            case ChatRole.Player: AddBubble(L("chatwin_you"), Quote(e.text), true); break;
            case ChatRole.Npc:    AddBubble(e.speaker, Quote(e.text), false); break;
            default:              AddSystemLine(e.text); break;
        }
        if (_thinking != null) _thinking.transform.SetAsLastSibling();

        // 超出显示上限：删最旧的一条（完整记录仍在档案文件里）
        int lines = _content.childCount - (_thinking != null ? 1 : 0);
        if (lines > maxShown)
        {
            Transform oldest = _content.GetChild(0);
            if (_thinking != null && oldest == _thinking.transform) oldest = _content.GetChild(1);
            oldest.SetParent(null);
            Destroy(oldest.gameObject);
        }
        if (scroll) ScrollToBottom();
    }

    // 台词加上引号（日语「」，中文“”）；本来就带引号的不重复加。
    static string Quote(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return s;
        char f = s[0];
        if (f == '「' || f == '“' || f == '"' || f == '『') return s;
        return LanguageGuard.IsJp ? "「" + s + "」" : "“" + s + "”";
    }

    // 一条对话：上面一行小字写是谁，下面是气泡。NPC 靠左（灰底），玩家靠右（绿框）。
    void AddBubble(string speaker, string text, bool player)
    {
        float width = ContentWidth;
        float maxBubble = Mathf.Round(width * 0.84f);
        const float padX = 12f, padY = 8f;

        RectTransform item = NewRect("Msg", _content);
        LayoutElement le = item.gameObject.AddComponent<LayoutElement>();

        TextMeshProUGUI label = NewText(item, "Speaker", 12f, player ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft);
        label.color = player ? new Color(Green.r, Green.g, Green.b, 0.75f) : LabelColor;
        label.richText = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.text = speaker ?? "";
        label.rectTransform.anchorMin = new Vector2(0f, 1f);
        label.rectTransform.anchorMax = new Vector2(1f, 1f);
        label.rectTransform.pivot = new Vector2(0.5f, 1f);
        label.rectTransform.offsetMin = new Vector2(2f, -LabelH);
        label.rectTransform.offsetMax = new Vector2(-2f, 0f);

        RectTransform bubble = NewRect("Bubble", item);
        Image bg = bubble.gameObject.AddComponent<Image>();
        bg.color = player ? PlayerBubble : NpcBubble;
        bg.raycastTarget = false;
        Outline border = bubble.gameObject.AddComponent<Outline>();
        border.effectColor = player ? Green : NpcBorder;
        border.effectDistance = new Vector2(1f, -1f);

        TextMeshProUGUI t = NewText(bubble, "Text", MsgFont, TextAlignmentOptions.TopLeft);
        t.richText = true;
        t.color = new Color(0.9f, 0.92f, 0.9f, 1f);
        t.text = "<noparse>" + text + "</noparse>";

        // 气泡大小：短句按实际宽度，长句到最大宽度后换行
        float maxText = maxBubble - padX * 2f;
        float textW = Mathf.Min(Mathf.Ceil(t.GetPreferredValues(t.text).x) + 2f, maxText);
        float textH = Mathf.Ceil(t.GetPreferredValues(t.text, textW, 0f).y);
        float bw = textW + padX * 2f, bh = textH + padY * 2f;

        bubble.anchorMin = bubble.anchorMax = bubble.pivot = new Vector2(player ? 1f : 0f, 1f);
        bubble.anchoredPosition = new Vector2(player ? -1f : 1f, -(LabelH + 3f));
        bubble.sizeDelta = new Vector2(bw, bh);
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(padX, padY), new Vector2(-padX, -padY));

        le.preferredHeight = LabelH + 3f + bh + 2f;
    }

    // 系统消息：居中的绿色小字，前面加「>> 」（原文两边的括号去掉）。
    void AddSystemLine(string text)
    {
        string s = (text ?? "").Trim();
        if (s.Length >= 2 && (s[0] == '（' || s[0] == '(') && (s[s.Length - 1] == '）' || s[s.Length - 1] == ')'))
            s = s.Substring(1, s.Length - 2);
        TextMeshProUGUI t = NewText(_content, "System", 12.5f, TextAlignmentOptions.Top);
        t.richText = true;
        t.color = SystemColor;
        t.text = "<noparse>>> " + s + "</noparse>";
    }

    void ScrollToBottom()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        Canvas.ForceUpdateCanvases();
        if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        _scrollFrames = 3;
    }

    void RefreshThinking()
    {
        if (_thinking == null) return;
        bool show = IsOpen && CurrentNpcId != null && _thinkingIds.Contains(CurrentNpcId);
        if (show)
        {
            NpcData npc = Npc(CurrentNpcId);
            _thinking.text = LF("chatwin_thinking", npc != null ? npc.npcName : "");
            _thinking.transform.SetAsLastSibling();
        }
        _thinking.gameObject.SetActive(show);
        if (show) ScrollToBottom();
    }

    void RefreshTexts()
    {
        if (_placeholder != null) _placeholder.text = L("chatwin_placeholder");
        if (IsOpen) { RefreshHeader(); RebuildMessages(); RefreshThinking(); }
    }

    void Submit()
    {
        if (_input == null || string.IsNullOrEmpty(CurrentNpcId)) return;
        string text = _input.text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _input.text = "";
        string id = CurrentNpcId;
        if (OnPlayerMessage != null) OnPlayerMessage(id, text);
        FocusInput();
    }

    void FocusInput()
    {
        if (_input == null || !IsOpen) return;
        _input.Select();
        _input.ActivateInputField();
    }

    // ============ 搭 UI ============

    void Build()
    {
        GameObject canvasGo = new GameObject("NpcChatCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 300;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        _canvasRt = (RectTransform)canvasGo.transform;

        // 窗口本体：轴心在左上角（展开动画改的是高度，上沿不动）；RectMask2D 让里面的内容随高度逐渐露出
        _window = NewRect("Window", canvasGo.transform);
        _window.anchorMin = _window.anchorMax = Vector2.zero;
        _window.pivot = new Vector2(0f, 1f);
        _window.sizeDelta = windowSize;
        _window.anchoredPosition = defaultPosition + new Vector2(0f, windowSize.y);
        Image panel = _window.gameObject.AddComponent<Image>();
        panel.color = PanelColor;
        Outline edge = _window.gameObject.AddComponent<Outline>();          // 淡绿色细边框
        edge.effectColor = new Color(Green.r, Green.g, Green.b, 0.85f);
        edge.effectDistance = new Vector2(1.5f, -1.5f);
        Outline glow = _window.gameObject.AddComponent<Outline>();          // 外面再淡淡一圈，像微光
        glow.effectColor = new Color(Green.r, Green.g, Green.b, 0.12f);
        glow.effectDistance = new Vector2(4f, -4f);
        _window.gameObject.AddComponent<RectMask2D>();
        _windowGroup = _window.gameObject.AddComponent<CanvasGroup>();
        AddDragger(_window.gameObject);

        // 内容层：固定为完整大小、贴着窗口上沿（窗口变矮时内容不跟着挤，只是被遮住）
        _body = NewRect("Body", _window);
        _body.anchorMin = _body.anchorMax = _body.pivot = new Vector2(0f, 1f);
        _body.anchoredPosition = Vector2.zero;
        _body.sizeDelta = windowSize;

        BuildHeader();

        // 消息区（可滚动）
        _scrollRt = NewRect("Scroll", _body);
        Stretch(_scrollRt, Vector2.zero, Vector2.one, new Vector2(8f, InputH + Pad * 2f), new Vector2(-8f, -(HeaderH + 6f)));
        _scroll = _scrollRt.gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 30f;

        RectTransform viewport = NewRect("Viewport", _scrollRt);
        Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image vpImg = viewport.gameObject.AddComponent<Image>();   // 透明底：让滚轮在空白处也能滚
        vpImg.color = new Color(0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        _scroll.viewport = viewport;

        _content = NewRect("Content", viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup vlg = _content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 10f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter fit = _content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scroll.content = _content;

        _thinking = NewText(_content, "Thinking", 12.5f, TextAlignmentOptions.Top);
        _thinking.color = new Color(0.62f, 0.66f, 0.66f);
        _thinking.fontStyle = FontStyles.Italic;
        _thinking.gameObject.SetActive(false);

        BuildInput();
        BuildSegments(canvasGo.transform);
    }

    // 标题栏：「会話 — 名字（职业）」+ 右边 正気 / 信頼 + 关闭按钮；下面一条淡绿细线。
    void BuildHeader()
    {
        RectTransform header = NewRect("Header", _body);
        Stretch(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -HeaderH), Vector2.zero);
        Image hb = header.gameObject.AddComponent<Image>();
        hb.color = HeaderColor;
        hb.raycastTarget = false;

        RectTransform line = NewRect("Line", header);
        Stretch(line, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f));
        Image li = line.gameObject.AddComponent<Image>();
        li.color = new Color(Green.r, Green.g, Green.b, 0.3f);
        li.raycastTarget = false;

        _title = NewText(header, "Title", 16f, TextAlignmentOptions.Left);
        _title.fontStyle = FontStyles.Bold;
        _title.textWrappingMode = TextWrappingModes.NoWrap;
        _title.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(_title.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-170f, 0f));

        _status = NewText(header, "Status", 13f, TextAlignmentOptions.Right);
        _status.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(_status.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-168f, 0f), new Vector2(-34f, 0f));

        Button close = NewButton(header, "Close", "×", 20f, new Color(0f, 0f, 0f, 0f), out TMP_Text closeLabel);
        closeLabel.color = new Color(0.62f, 0.66f, 0.66f);
        RectTransform cr = (RectTransform)close.transform;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1f, 0.5f);
        cr.sizeDelta = new Vector2(30f, 30f);
        cr.anchoredPosition = new Vector2(-4f, 0f);
        close.onClick.AddListener(Close);
    }

    // 输入框：细边框，左边一个绿色「>」，Enter 发送。
    void BuildInput()
    {
        RectTransform box = NewRect("Input", _body);
        Stretch(box, Vector2.zero, new Vector2(1f, 0f), new Vector2(Pad, Pad), new Vector2(-Pad, Pad + InputH));
        Image boxImg = box.gameObject.AddComponent<Image>();
        boxImg.color = new Color(0.035f, 0.04f, 0.045f, 1f);
        Outline bo = box.gameObject.AddComponent<Outline>();
        bo.effectColor = FieldBorder;
        bo.effectDistance = new Vector2(1f, -1f);

        TextMeshProUGUI prompt = NewText(box, "Prompt", 15f, TextAlignmentOptions.Left);
        prompt.text = ">";
        prompt.color = Green;
        prompt.fontStyle = FontStyles.Bold;
        Stretch(prompt.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(10f, 0f), new Vector2(26f, 0f));

        RectTransform area = NewRect("Text Area", box);
        Stretch(area, Vector2.zero, Vector2.one, new Vector2(28f, 4f), new Vector2(-10f, -4f));
        area.gameObject.AddComponent<RectMask2D>();
        TextMeshProUGUI ph = NewText(area, "Placeholder", 14f, TextAlignmentOptions.Left);
        ph.color = new Color(0.5f, 0.54f, 0.54f, 0.9f);
        ph.textWrappingMode = TextWrappingModes.NoWrap;
        ph.overflowMode = TextOverflowModes.Ellipsis;
        ph.text = L("chatwin_placeholder");
        Stretch(ph.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _placeholder = ph;
        TextMeshProUGUI tx = NewText(area, "Text", 15f, TextAlignmentOptions.Left);
        tx.textWrappingMode = TextWrappingModes.NoWrap;
        tx.richText = false;
        Stretch(tx.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // TMP_InputField 先在"关闭"状态下挂上并接好引用，再打开——避免它初始化时找不到文字组件。
        box.gameObject.SetActive(false);
        _input = box.gameObject.AddComponent<TMP_InputField>();
        _input.textViewport = area;
        _input.textComponent = tx;
        _input.placeholder = ph;
        if (font != null) _input.fontAsset = font;
        _input.pointSize = 15f;
        _input.lineType = TMP_InputField.LineType.SingleLine;
        _input.characterLimit = 200;
        _input.richText = false;
        _input.caretColor = Green;
        _input.customCaretColor = true;
        _input.selectionColor = new Color(Green.r, Green.g, Green.b, 0.3f);
        _input.targetGraphic = boxImg;
        _input.onSubmit.AddListener(_ => Submit());
        box.gameObject.SetActive(true);
    }

    // 打开动画用的两条线（画在窗口外面，所以挂在画布上，不受窗口遮罩影响）。
    void BuildSegments(Transform parent)
    {
        _segLine = NewSegment("Tether", parent);
        _lineGlow = _segLine.Find("Glow").GetComponent<Image>();
        // 点击范围：比线粗得多的透明条（线只有 2 像素，直接点很难点中）
        RectTransform hit = NewRect("Hit", _segLine);
        Stretch(hit, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -9f), new Vector2(0f, 9f));
        Image hitImg = hit.gameObject.AddComponent<Image>();
        hitImg.color = new Color(0f, 0f, 0f, 0f);
        hit.gameObject.AddComponent<ChatTetherHandle>();

        _segEdge = NewSegment("OpenEdge", parent);

        _trackLabel = NewText(parent, "Tracking", 12f, TextAlignmentOptions.BottomLeft);
        _trackLabel.text = "[ TRACKING ]";
        _trackLabel.color = Green;
        _trackLabel.textWrappingMode = TextWrappingModes.NoWrap;
        RectTransform lr = _trackLabel.rectTransform;
        lr.anchorMin = lr.anchorMax = Vector2.zero;
        lr.pivot = new Vector2(0f, 0f);
        lr.sizeDelta = new Vector2(140f, 18f);
        _trackLabel.gameObject.SetActive(false);
    }

    static RectTransform NewSegment(string name, Transform parent)
    {
        RectTransform seg = NewRect(name, parent);
        seg.anchorMin = seg.anchorMax = Vector2.zero;
        seg.pivot = new Vector2(0f, 0.5f);
        seg.sizeDelta = new Vector2(0f, 2f);
        Image core = seg.gameObject.AddComponent<Image>();
        core.color = Green;
        core.raycastTarget = false;
        RectTransform halo = NewRect("Glow", seg);                 // 线两侧淡淡的光
        Stretch(halo, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-2f, -4f), new Vector2(2f, 4f));
        Image hi = halo.gameObject.AddComponent<Image>();
        hi.color = new Color(Green.r, Green.g, Green.b, 0.18f);
        hi.raycastTarget = false;
        seg.gameObject.SetActive(false);
        return seg;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    void AddDragger(GameObject go)
    {
        UiWindowDragger d = go.AddComponent<UiWindowDragger>();
        d.target = _window;
        d.canvas = _canvas;
    }

    TextMeshProUGUI NewText(Transform parent, string name, float size, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    Button NewButton(Transform parent, string name, string label, float size, Color color, out TMP_Text labelText)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        Button b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        TextMeshProUGUI t = NewText(rt, "Label", size, TextAlignmentOptions.Center);
        t.text = label;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        labelText = t;
        return b;
    }

    static NpcData Npc(string id)
    {
        return WorldManager.Instance != null && !string.IsNullOrEmpty(id) ? WorldManager.Instance.GetNpcById(id) : null;
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;
}

// 对话窗口连线的点击范围：点一下 = 镜头锁定 / 解锁这个 NPC；鼠标指着时线变亮。
public class ChatTetherHandle : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public void OnPointerClick(PointerEventData e) { if (NpcChatWindow.Instance != null) NpcChatWindow.Instance.OnTetherClicked(); }
    public void OnPointerEnter(PointerEventData e) { if (NpcChatWindow.Instance != null) NpcChatWindow.Instance.OnTetherHover(true); }
    public void OnPointerExit(PointerEventData e) { if (NpcChatWindow.Instance != null) NpcChatWindow.Instance.OnTetherHover(false); }
}

// 拖动移动窗口（挂在标题栏 / 窗口底板上）。窗口不会被拖出屏幕。
public class UiWindowDragger : MonoBehaviour, IDragHandler
{
    public RectTransform target;
    public Canvas canvas;

    public void OnDrag(PointerEventData eventData)
    {
        if (target == null) return;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        target.anchoredPosition += eventData.delta / Mathf.Max(0.01f, scale);
        ClampInside(target);
    }

    // 把窗口整个限制在父物体（这里是全屏的 Canvas）范围内。
    public static void ClampInside(RectTransform rt)
    {
        RectTransform parent = rt.parent as RectTransform;
        if (parent == null) return;
        Rect pr = parent.rect;
        Rect r = rt.rect;
        Vector2 lp = rt.localPosition;
        float dx = 0f, dy = 0f;
        if (lp.x + r.xMin < pr.xMin) dx = pr.xMin - (lp.x + r.xMin);
        else if (lp.x + r.xMax > pr.xMax) dx = pr.xMax - (lp.x + r.xMax);
        if (lp.y + r.yMin < pr.yMin) dy = pr.yMin - (lp.y + r.yMin);
        else if (lp.y + r.yMax > pr.yMax) dy = pr.yMax - (lp.y + r.yMax);
        if (dx != 0f || dy != 0f) rt.anchoredPosition += new Vector2(dx, dy);
    }
}

// 找一个能显示中日文的 TMP 字体：借用场景里其他面板已经设置好的字体。
public static class GameFonts
{
    static TMP_FontAsset _cached;

    public static TMP_FontAsset Find()
    {
        if (_cached != null) return _cached;
        if (NpcIdleManager.Instance != null && NpcIdleManager.Instance.BubbleFont != null)
            return _cached = NpcIdleManager.Instance.BubbleFont;
        NpcListPanel list = UnityEngine.Object.FindFirstObjectByType<NpcListPanel>(FindObjectsInactive.Include);
        if (list != null && list.font != null) return _cached = list.font;
        TerminalPanelUI term = UnityEngine.Object.FindFirstObjectByType<TerminalPanelUI>(FindObjectsInactive.Include);
        if (term != null && term.font != null) return _cached = term.font;
        StoragePanelUI storage = UnityEngine.Object.FindFirstObjectByType<StoragePanelUI>(FindObjectsInactive.Include);
        if (storage != null && storage.font != null) return _cached = storage.font;
        StatusPanelManager status = UnityEngine.Object.FindFirstObjectByType<StatusPanelManager>(FindObjectsInactive.Include);
        if (status != null && status.font != null) return _cached = status.font;
        Debug.LogWarning("[NpcChatWindow] 没找到中日文字体，对话窗口的字可能显示成方块。请把 NPC 列表用的 TMP 字体拖到 NpcChatWindow.font。");
        return null;
    }
}
