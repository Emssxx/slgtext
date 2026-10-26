using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

// NPC 专属对话窗口（运行时自建 UI，不用在场景里摆）。
// 点场景里的 NPC / 点状态卡 → 弹出和这个 NPC 的对话窗口，显示的是"他自己的"聊天记录（来自 NpcMemory），
// 换一个 NPC 就换成那个人的记录——所有 NPC 不再共用一个聊天栏。
// 操作：Enter 发送、Esc 先退出输入再关窗口、Y 重新打开上次的窗口。
// 窗口是扁平的小窗，默认在屏幕左下角；拖标题栏或窗口边缘可以挪到任何地方（拖不出屏幕）。
// 窗口只负责显示和输入；玩家发出的话交给 NpcAgent 处理（OnPlayerMessage）。
public class NpcChatWindow : MonoBehaviour
{
    public static NpcChatWindow Instance { get; private set; }

    // 输入框是否正在打字（其他系统据此屏蔽快捷键，见 ChatManager.InputFocused）。
    public static bool InputFocused => Instance != null && Instance.IsOpen && Instance._input != null && Instance._input.isFocused;

    // 玩家在窗口里发了一句话：(npcId, 内容)。
    public event Action<string, string> OnPlayerMessage;

    [Header("外观")]
    public TMP_FontAsset font;                         // 不拖会自动借用 NPC 列表等面板的中日文字体
    public Vector2 windowSize = new Vector2(460f, 290f);  // 扁平小窗
    public int maxShown = 60;                          // 窗口里最多显示多少条（完整记录在档案文件里）

    public bool IsOpen => _window != null && _window.gameObject.activeSelf;
    public string CurrentNpcId { get; private set; }
    public string LastNpcId { get; private set; }

    Canvas _canvas;
    RectTransform _window;
    TMP_Text _title, _status, _thinking, _sendLabel, _placeholder;
    ScrollRect _scroll;
    RectTransform _content;
    TMP_InputField _input;
    readonly HashSet<string> _thinkingIds = new HashSet<string>();
    int _scrollFrames;
    float _statusTimer;

    static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.95f);
    static readonly Color BarColor = new Color(0.14f, 0.16f, 0.22f, 1f);
    static readonly Color FieldColor = new Color(0.16f, 0.18f, 0.23f, 1f);
    static readonly Color ButtonColor = new Color(0.2f, 0.42f, 0.7f, 1f);

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

    // 打开和某个 NPC 的对话窗口（已经开着别人的，就换成这个人的记录）。
    public void Open(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return;
        CurrentNpcId = npcId;
        LastNpcId = npcId;
        _window.gameObject.SetActive(true);
        RebuildMessages();
        RefreshHeader();
        RefreshThinking();
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
        if (_input != null) _input.DeactivateInputField();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        _window.gameObject.SetActive(false);
        CurrentNpcId = null;
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
            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f) { _statusTimer = 1f; RefreshHeader(); }
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
    }

    // 别的输入框（如果有）正被选中时，不抢 Y 键。
    static bool IsAnyInputSelected()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null) return false;
        return EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
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

    void AddLine(ChatEntry e, bool scroll = true)
    {
        TextMeshProUGUI t = NewText(_content, "Msg", 15f, TextAlignmentOptions.TopLeft);
        t.richText = true;
        string body = "<noparse>" + e.text + "</noparse>";
        switch (e.Role)
        {
            case ChatRole.Player:
                t.text = "<color=#7FD3FF><b><noparse>" + e.speaker + "</noparse></b>：</color><color=#CFEFFF>" + body + "</color>";
                break;
            case ChatRole.Npc:
                t.text = "<color=#FFD27F><b><noparse>" + e.speaker + "</noparse></b>：</color>" + body;
                break;
            default:
                t.text = "<color=#8C8C8C><i>" + body + "</i></color>";
                t.fontSize = 13f;
                break;
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

    void RefreshHeader()
    {
        NpcData npc = Npc(CurrentNpcId);
        if (npc == null) return;
        _title.text = LF("chatwin_title", npc.npcName);
        string state = npc.IsDead ? L("chatwin_dead") : MentalName(npc);
        _status.text = LF("chatwin_status", Mathf.RoundToInt(npc.playerTrust), state);
    }

    void RefreshTexts()
    {
        if (_sendLabel != null) _sendLabel.text = L("chat_send");
        if (_placeholder != null) _placeholder.text = L("chatwin_placeholder");
        if (IsOpen) { RefreshHeader(); RefreshThinking(); }
    }

    static string MentalName(NpcData n)
    {
        switch (MentalState.GetLevel(n))
        {
            case SanityLevel.Dazed:         return L("mental_dazed");
            case SanityLevel.Hallucinating: return L("mental_halluc");
            case SanityLevel.Broken:        return L("mental_broken");
            default:                        return L("mental_lucid");
        }
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

        // 窗口本体（左下角的扁平小窗，可拖动）
        _window = NewRect("Window", canvasGo.transform);
        _window.anchorMin = _window.anchorMax = _window.pivot = Vector2.zero;
        _window.sizeDelta = windowSize;
        _window.anchoredPosition = new Vector2(16f, 24f);
        _window.gameObject.AddComponent<Image>().color = PanelColor;
        AddDragger(_window.gameObject);   // 拖窗口边缘（消息区、输入框以外的地方）也能挪

        // 标题栏：名字 + 状态（同一行）+ 关闭按钮（拖它可以挪窗口）
        RectTransform bar = NewRect("TitleBar", _window);
        Stretch(bar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -40f), Vector2.zero);
        bar.gameObject.AddComponent<Image>().color = BarColor;
        AddDragger(bar.gameObject);

        _title = NewText(bar, "Title", 17f, TextAlignmentOptions.Left);
        _title.fontStyle = FontStyles.Bold;
        _title.textWrappingMode = TextWrappingModes.NoWrap;
        _title.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(_title.rectTransform, new Vector2(0f, 0f), new Vector2(0.55f, 1f), new Vector2(12f, 0f), Vector2.zero);
        _status = NewText(bar, "Status", 13f, TextAlignmentOptions.Right);
        _status.color = new Color(0.7f, 0.74f, 0.82f);
        _status.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(_status.rectTransform, new Vector2(0.55f, 0f), new Vector2(1f, 1f), new Vector2(4f, 0f), new Vector2(-40f, 0f));

        Button close = NewButton(bar, "Close", "×", 22f, new Color(0f, 0f, 0f, 0f), out _);
        RectTransform cr = (RectTransform)close.transform;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1f, 0.5f);
        cr.sizeDelta = new Vector2(36f, 36f);
        cr.anchoredPosition = new Vector2(-2f, 0f);
        close.onClick.AddListener(Close);

        // 消息区（可滚动）
        RectTransform scrollRt = NewRect("Scroll", _window);
        Stretch(scrollRt, Vector2.zero, Vector2.one, new Vector2(6f, 46f), new Vector2(-6f, -44f));
        scrollRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
        _scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 30f;

        RectTransform viewport = NewRect("Viewport", scrollRt);
        Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();
        _scroll.viewport = viewport;

        _content = NewRect("Content", viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup vlg = _content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 6, 6);
        vlg.spacing = 4f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter fit = _content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scroll.content = _content;

        _thinking = NewText(_content, "Thinking", 13f, TextAlignmentOptions.TopLeft);
        _thinking.color = new Color(0.65f, 0.65f, 0.7f);
        _thinking.fontStyle = FontStyles.Italic;
        _thinking.gameObject.SetActive(false);

        // 输入行：输入框 + 发送按钮
        RectTransform row = NewRect("InputRow", _window);
        Stretch(row, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(6f, 6f), new Vector2(-6f, 40f));

        RectTransform inputRt = NewRect("Input", row);
        Stretch(inputRt, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-68f, 0f));
        inputRt.gameObject.AddComponent<Image>().color = FieldColor;
        RectTransform area = NewRect("Text Area", inputRt);
        Stretch(area, Vector2.zero, Vector2.one, new Vector2(10f, 4f), new Vector2(-10f, -4f));
        area.gameObject.AddComponent<RectMask2D>();
        TextMeshProUGUI ph = NewText(area, "Placeholder", 14f, TextAlignmentOptions.Left);
        ph.fontStyle = FontStyles.Italic;
        ph.color = new Color(0.6f, 0.62f, 0.68f, 0.8f);
        ph.textWrappingMode = TextWrappingModes.NoWrap;
        ph.text = L("chatwin_placeholder");
        Stretch(ph.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _placeholder = ph;
        TextMeshProUGUI tx = NewText(area, "Text", 15f, TextAlignmentOptions.Left);
        tx.textWrappingMode = TextWrappingModes.NoWrap;
        tx.richText = false;
        Stretch(tx.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // TMP_InputField 先在"关闭"状态下挂上并接好引用，再打开——避免它初始化时找不到文字组件。
        inputRt.gameObject.SetActive(false);
        _input = inputRt.gameObject.AddComponent<TMP_InputField>();
        _input.textViewport = area;
        _input.textComponent = tx;
        _input.placeholder = ph;
        if (font != null) _input.fontAsset = font;
        _input.pointSize = 15f;
        _input.lineType = TMP_InputField.LineType.SingleLine;
        _input.characterLimit = 200;
        _input.richText = false;
        _input.targetGraphic = inputRt.GetComponent<Image>();
        _input.onSubmit.AddListener(_ => Submit());
        inputRt.gameObject.SetActive(true);

        Button send = NewButton(row, "Send", L("chat_send"), 15f, ButtonColor, out _sendLabel);
        RectTransform sr = (RectTransform)send.transform;
        sr.anchorMin = new Vector2(1f, 0f);
        sr.anchorMax = new Vector2(1f, 1f);
        sr.pivot = new Vector2(1f, 0.5f);
        sr.sizeDelta = new Vector2(62f, 0f);
        sr.anchoredPosition = Vector2.zero;
        send.onClick.AddListener(Submit);
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
