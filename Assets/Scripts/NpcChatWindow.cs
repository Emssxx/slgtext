using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

// NPC 专属对话窗口（运行时自建 UI，不用在场景里摆）。旧的 NPC 状态卡已经并进来了。
// 点场景里的 NPC / 幸存者列表 / 日志里的名字 → 弹出这个 NPC 的竖长窗口：
//   上方：左边是 NPC 的实时动画头像（同步他在地图上的动画帧和朝向），右边是名字、性别·精神状态、生命/饥饿/体力/理智/信任状态条；
//   中间：词条（职业、性格词条、由性格数值推出的"勇敢""谨慎"之类，崩解后的异变）；
//   下方：他自己的聊天记录（来自 NpcMemory）+ 输入框——所有 NPC 不再共用一个聊天栏。
// 操作：Enter 发送、Esc 先退出输入再关窗口、Y 重新打开上次的窗口；拖窗口上半部分或边缘可以挪动（拖不出屏幕）。
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
    public Vector2 windowSize = new Vector2(400f, 680f);    // 竖长窗口
    public Vector2 defaultPosition = new Vector2(350f, 16f); // 离屏幕左下角的位置（左上角抽屉的右边）
    public int maxShown = 60;                               // 窗口里最多显示多少条（完整记录在档案文件里）

    public bool IsOpen => _window != null && _window.gameObject.activeSelf;
    public string CurrentNpcId { get; private set; }
    public string LastNpcId { get; private set; }

    Canvas _canvas;
    RectTransform _window;
    TMP_Text _thinking, _sendLabel, _placeholder;
    ScrollRect _scroll;
    RectTransform _scrollRt, _content;
    TMP_InputField _input;
    readonly HashSet<string> _thinkingIds = new HashSet<string>();
    int _scrollFrames;
    float _statusTimer;

    // —— 上方：头像 + 名字 + 状态条 ——
    Image _portrait;
    RectTransform _portraitRt;
    SpriteRenderer _portraitSource;   // 地图上这个 NPC 的 SpriteRenderer（头像每帧跟着它换帧）
    string _portraitSourceId;
    TMP_Text _name, _sub;

    class Bar
    {
        public string key;
        public TMP_Text label, value;
        public RectTransform fill;
        public Image fillImage;
    }
    Bar _hp, _hunger, _water, _energy, _sanity, _trust;

    // —— 中间：词条 ——
    RectTransform _chips;
    string _chipSignature;

    const float Pad = 12f;
    const float PortraitSize = 116f;
    const float HeaderH = 172f;
    const float ChipH = 24f;

    static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.95f);
    static readonly Color HeaderColor = new Color(0.14f, 0.16f, 0.22f, 1f);
    static readonly Color FieldColor = new Color(0.16f, 0.18f, 0.23f, 1f);
    static readonly Color ButtonColor = new Color(0.2f, 0.42f, 0.7f, 1f);
    static readonly Color GoodColor = new Color(0.35f, 0.8f, 0.4f, 1f);
    static readonly Color BadColor = new Color(0.9f, 0.3f, 0.25f, 1f);
    static readonly Color TrustColor = new Color(0.35f, 0.6f, 1f, 1f);
    static readonly Color ChipProfession = new Color(0.18f, 0.32f, 0.5f, 1f);
    static readonly Color ChipTrait = new Color(0.32f, 0.24f, 0.5f, 1f);
    static readonly Color ChipPersonality = new Color(0.2f, 0.38f, 0.26f, 1f);
    static readonly Color ChipMutation = new Color(0.55f, 0.16f, 0.16f, 1f);

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

    // 打开和某个 NPC 的对话窗口（已经开着别人的，就换成这个人的头像、状态、词条和记录）。
    public void Open(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return;
        string previous = IsOpen ? CurrentNpcId : null;
        if (previous != null && previous != npcId && OnNpcClosed != null) OnNpcClosed(previous);

        CurrentNpcId = npcId;
        LastNpcId = npcId;
        _window.gameObject.SetActive(true);
        UiWindowDragger.ClampInside(_window);
        _portraitSource = null;
        _portraitSourceId = null;
        _chipSignature = null;
        RefreshHeader();
        RebuildMessages();
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
        if (IsOpen) UpdatePortrait();
    }

    // 别的输入框（如果有）正被选中时，不抢 Y 键。
    static bool IsAnyInputSelected()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null) return false;
        return EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
    }

    // ============ 上方：头像 / 状态 / 词条 ============

    // 头像：每帧把地图上这个 NPC 当前的动画帧、朝向、颜色抄过来（所以是"活的"）。
    void UpdatePortrait()
    {
        if (_portrait == null) return;
        if (_portraitSource == null && _portraitSourceId != CurrentNpcId)
        {
            _portraitSourceId = CurrentNpcId; // 每次打开只找一次，找不到（比如已死亡被移除）就保留最后一帧
            NpcMover mover = NpcView.GetMover(CurrentNpcId);
            if (mover != null)
            {
                _portraitSource = mover.GetComponent<SpriteRenderer>();
                if (_portraitSource == null) _portraitSource = mover.GetComponentInChildren<SpriteRenderer>();
            }
            if (_portraitSource == null) _portrait.enabled = false;
        }
        NpcData npc = Npc(CurrentNpcId);
        bool dead = npc != null && npc.IsDead;
        if (_portraitSource == null)
        {
            // 地图上的 NPC 已经不在了（比如死亡后被移除）：保留最后一帧，变暗
            if (dead) _portrait.color = new Color(0.4f, 0.4f, 0.4f, 1f);
            return;
        }

        Sprite s = _portraitSource.sprite;
        _portrait.sprite = s;
        _portrait.enabled = s != null;
        _portraitRt.localScale = new Vector3(_portraitSource.flipX ? -1f : 1f, 1f, 1f);
        Color c = _portraitSource.color;
        if (dead) c = new Color(c.r * 0.4f, c.g * 0.4f, c.b * 0.4f, c.a);
        _portrait.color = c;
    }

    void RefreshHeader()
    {
        NpcData npc = Npc(CurrentNpcId);
        if (npc == null) return;
        _name.text = npc.npcName;
        _sub.text = GenderName(npc.gender) + "  ·  " + MentalTag(npc);
        SetBar(_hp, npc.health, false);
        SetBar(_hunger, npc.satiety, false); // 饥饿值：100 = 不饿
        SetBar(_water, npc.water, false);    // 水分：100 = 不渴
        SetBar(_energy, npc.energy, false);
        SetBar(_sanity, npc.sanity, false);
        SetBar(_trust, npc.playerTrust, TrustColor);

        string sig = ChipSignature(npc);
        if (sig != _chipSignature) { _chipSignature = sig; RebuildChips(npc); }
    }

    void SetBar(Bar b, float v, bool higherIsWorse)
    {
        float r = Mathf.Clamp01(v / 100f);
        SetBar(b, v, Color.Lerp(BadColor, GoodColor, higherIsWorse ? 1f - r : r));
    }

    void SetBar(Bar b, float v, Color color)
    {
        if (b == null) return;
        b.fill.anchorMax = new Vector2(Mathf.Clamp01(v / 100f), 1f);
        b.fillImage.color = color;
        b.value.text = Mathf.RoundToInt(v).ToString();
    }

    static string GenderName(string g)
    {
        if (g == "男") return L("gender_male");
        if (g == "女") return L("gender_female");
        return g ?? "";
    }

    static string MentalTag(NpcData n)
    {
        if (n.IsDead) return "<color=#8C8C8C>" + L("chatwin_dead") + "</color>";
        switch (MentalState.GetLevel(n))
        {
            case SanityLevel.Dazed:         return "<color=#E0C060>" + L("mental_dazed") + "</color>";
            case SanityLevel.Hallucinating: return "<color=#C080FF>" + L("mental_halluc") + "</color>";
            case SanityLevel.Broken:        return "<color=#FF6060>" + L("mental_broken") + "</color>";
            default:                        return "<color=#8FD18F>" + L("mental_lucid") + "</color>";
        }
    }

    // 词条：职业 → 生成时抽到的性格词条 → 由 5 维性格数值推出的标签（≥70 / ≤30）→ 崩解后的异变。重复的只显示一次。
    // （以后有了正式的词条库，改这里就行。）
    List<KeyValuePair<string, Color>> CollectChips(NpcData npc)
    {
        List<KeyValuePair<string, Color>> list = new List<KeyValuePair<string, Color>>();
        HashSet<string> seen = new HashSet<string>();
        // 状态词条（健康系统）放最前面：濒死 / 受伤 / 生病 / 饥饿 / 口渴 / 疲惫……没有生命类词条时显示「健康」
        if (!npc.IsDead)
        {
            if (NpcHealth.IsHealthy(npc)) AddChip(list, seen, L("stat_healthy"), NpcHealth.HealthyColor);
            bool jp = LanguageGuard.IsJp;
            foreach (HealthTag t in NpcHealth.Tags(npc)) AddChip(list, seen, NpcHealth.TagName(npc, t, jp), NpcHealth.TagColor(t));
        }
        AddChip(list, seen, npc.profession, ChipProfession);
        if (npc.traits != null) foreach (string t in npc.traits) AddChip(list, seen, t, ChipTrait);
        Personality p = npc.personality;
        if (p != null)
        {
            AddChip(list, seen, PersonalityTag(p.bravery, "pers_brave_hi", "pers_brave_lo"), ChipPersonality);
            AddChip(list, seen, PersonalityTag(p.altruism, "pers_altruism_hi", "pers_altruism_lo"), ChipPersonality);
            AddChip(list, seen, PersonalityTag(p.caution, "pers_caution_hi", "pers_caution_lo"), ChipPersonality);
            AddChip(list, seen, PersonalityTag(p.sociability, "pers_social_hi", "pers_social_lo"), ChipPersonality);
            AddChip(list, seen, PersonalityTag(p.optimism, "pers_optimism_hi", "pers_optimism_lo"), ChipPersonality);
        }
        if (npc.isBroken && npc.mutations != null) foreach (string m in npc.mutations) AddChip(list, seen, m, ChipMutation);
        return list;
    }

    static void AddChip(List<KeyValuePair<string, Color>> list, HashSet<string> seen, string text, Color color)
    {
        if (string.IsNullOrEmpty(text) || !seen.Add(text)) return;
        list.Add(new KeyValuePair<string, Color>(text, color));
    }

    static string PersonalityTag(int value, string highKey, string lowKey)
    {
        if (value >= 70) return L(highKey);
        if (value <= 30) return L(lowKey);
        return null;
    }

    static string ChipSignature(NpcData npc)
    {
        Personality p = npc.personality;
        return npc.profession + "|" + (npc.traits != null ? string.Join(",", npc.traits) : "") + "|" +
               (p != null ? p.bravery + "," + p.altruism + "," + p.caution + "," + p.sociability + "," + p.optimism : "") + "|" +
               npc.isBroken + "|" + (npc.mutations != null ? string.Join(",", npc.mutations) : "") + "|" +
               NpcHealth.BriefLine(npc, false) + "|" +
               (LanguageGuard.IsJp ? "ja" : "zh");
    }

    // 词条按宽度自动换行排成一块；排完后把下面的聊天区顶到词条下方。
    void RebuildChips(NpcData npc)
    {
        for (int i = _chips.childCount - 1; i >= 0; i--)
        {
            Transform c = _chips.GetChild(i);
            c.SetParent(null);
            Destroy(c.gameObject);
        }

        List<KeyValuePair<string, Color>> list = CollectChips(npc);
        float maxW = windowSize.x - Pad * 2f;
        const float gapX = 6f, gapY = 6f;
        float x = 0f, y = 0f;
        foreach (KeyValuePair<string, Color> kv in list)
        {
            RectTransform chip = NewRect("Chip", _chips);
            Image img = chip.gameObject.AddComponent<Image>();
            img.color = kv.Value;
            img.raycastTarget = false;
            TextMeshProUGUI t = NewText(chip, "Text", 13f, TextAlignmentOptions.Center);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.richText = false;
            t.text = kv.Key;
            Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            float w = Mathf.Min(maxW, Mathf.Ceil(t.GetPreferredValues(kv.Key).x) + 18f);
            if (x > 0f && x + w > maxW) { x = 0f; y += ChipH + gapY; }
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0f, 1f);
            chip.anchoredPosition = new Vector2(x, -y);
            chip.sizeDelta = new Vector2(w, ChipH);
            x += w + gapX;
        }

        float height = list.Count > 0 ? y + ChipH : 0f;
        _chips.offsetMin = new Vector2(Pad, -(HeaderH + 10f + height));
        _chips.offsetMax = new Vector2(-Pad, -(HeaderH + 10f));
        float top = HeaderH + 10f + height + (height > 0f ? 10f : 0f);
        _scrollRt.offsetMax = new Vector2(-6f, -top);
        _scrollFrames = 3;
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

    void RefreshTexts()
    {
        if (_sendLabel != null) _sendLabel.text = L("chat_send");
        if (_placeholder != null) _placeholder.text = L("chatwin_placeholder");
        foreach (Bar b in new[] { _hp, _hunger, _water, _energy, _sanity, _trust })
            if (b != null) b.label.text = L(b.key);
        if (IsOpen) { _chipSignature = null; RefreshHeader(); RefreshThinking(); }
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

        // 窗口本体（竖长，默认在左下、抽屉右边；拖上半部分或边缘可以挪）
        _window = NewRect("Window", canvasGo.transform);
        _window.anchorMin = _window.anchorMax = _window.pivot = Vector2.zero;
        _window.sizeDelta = windowSize;
        _window.anchoredPosition = defaultPosition;
        _window.gameObject.AddComponent<Image>().color = PanelColor;
        AddDragger(_window.gameObject);

        BuildHeader();

        // 词条区（内容和高度在 RebuildChips 里定）
        _chips = NewRect("Traits", _window);
        _chips.anchorMin = new Vector2(0f, 1f);
        _chips.anchorMax = new Vector2(1f, 1f);
        _chips.pivot = new Vector2(0.5f, 1f);
        _chips.offsetMin = new Vector2(Pad, -(HeaderH + 10f));
        _chips.offsetMax = new Vector2(-Pad, -(HeaderH + 10f));

        // 消息区（可滚动；上沿跟着词条区走）
        _scrollRt = NewRect("Scroll", _window);
        Stretch(_scrollRt, Vector2.zero, Vector2.one, new Vector2(6f, 46f), new Vector2(-6f, -(HeaderH + 10f)));
        _scrollRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
        _scroll = _scrollRt.gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 30f;

        RectTransform viewport = NewRect("Viewport", _scrollRt);
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

    // 上方一整块：头像（左）+ 名字 / 性别·精神状态 / 五条状态条（右）+ 关闭按钮。
    // 这块不挡拖动：文字、图片都不接收点击，拖它就是拖窗口。
    void BuildHeader()
    {
        RectTransform header = NewRect("Header", _window);
        Stretch(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -HeaderH), Vector2.zero);
        Image hb = header.gameObject.AddComponent<Image>();
        hb.color = HeaderColor;
        hb.raycastTarget = false;

        // 头像：橙色细框 + 深色底 + 实时帧
        RectTransform frame = NewRect("PortraitFrame", header);
        frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0f, 1f);
        frame.anchoredPosition = new Vector2(Pad, -Pad);
        frame.sizeDelta = new Vector2(PortraitSize, PortraitSize);
        Image fi = frame.gameObject.AddComponent<Image>();
        fi.color = new Color(1f, 0.6f, 0.3f, 0.45f);
        fi.raycastTarget = false;
        RectTransform pbg = NewRect("Bg", frame);
        Stretch(pbg, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        Image pb = pbg.gameObject.AddComponent<Image>();
        pb.color = new Color(0.03f, 0.035f, 0.05f, 1f);
        pb.raycastTarget = false;
        _portraitRt = NewRect("Portrait", frame);
        Stretch(_portraitRt, Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -8f));
        _portrait = _portraitRt.gameObject.AddComponent<Image>();
        _portrait.preserveAspect = true;
        _portrait.raycastTarget = false;
        _portrait.enabled = false;

        // 右侧：名字、性别·精神状态、状态条
        float colX = Pad + PortraitSize + 12f;
        _name = NewText(header, "Name", 21f, TextAlignmentOptions.Left);
        _name.fontStyle = FontStyles.Bold;
        _name.richText = false;
        _name.textWrappingMode = TextWrappingModes.NoWrap;
        _name.overflowMode = TextOverflowModes.Ellipsis;
        TopRow(_name.rectTransform, colX, 10f, 26f, -44f);
        _sub = NewText(header, "Sub", 13f, TextAlignmentOptions.Left);
        _sub.color = new Color(0.72f, 0.75f, 0.82f);
        _sub.textWrappingMode = TextWrappingModes.NoWrap;
        TopRow(_sub.rectTransform, colX, 38f, 18f, -Pad);

        float y = 60f;
        _hp = NewBar(header, "stat_health", colX, y); y += 18f;
        _hunger = NewBar(header, "stat_hunger", colX, y); y += 18f;
        _water = NewBar(header, "stat_water", colX, y); y += 18f;
        _energy = NewBar(header, "stat_energy", colX, y); y += 18f;
        _sanity = NewBar(header, "stat_sanity", colX, y); y += 18f;
        _trust = NewBar(header, "stat_trust", colX, y);

        Button close = NewButton(header, "Close", "×", 22f, new Color(0f, 0f, 0f, 0f), out _);
        RectTransform cr = (RectTransform)close.transform;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1f, 1f);
        cr.sizeDelta = new Vector2(36f, 36f);
        cr.anchoredPosition = new Vector2(-4f, -4f);
        close.onClick.AddListener(Close);
    }

    // 一条状态条：左标签 + 中间条 + 右数字。
    Bar NewBar(Transform parent, string key, float left, float top)
    {
        RectTransform row = NewRect("Bar_" + key, parent);
        TopRow(row, left, top, 15f, -Pad);
        Bar b = new Bar { key = key };

        b.label = NewText(row, "Label", 12f, TextAlignmentOptions.Left);
        b.label.color = new Color(0.75f, 0.78f, 0.85f);
        b.label.textWrappingMode = TextWrappingModes.NoWrap;
        b.label.text = L(key);
        Stretch(b.label.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(40f, 0f));

        RectTransform bg = NewRect("Bg", row);
        Stretch(bg, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(42f, -5f), new Vector2(-34f, 5f));
        Image bgi = bg.gameObject.AddComponent<Image>();
        bgi.color = new Color(1f, 1f, 1f, 0.12f);
        bgi.raycastTarget = false;

        b.fill = NewRect("Fill", bg);
        Stretch(b.fill, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        b.fillImage = b.fill.gameObject.AddComponent<Image>();
        b.fillImage.raycastTarget = false;

        b.value = NewText(row, "Value", 12f, TextAlignmentOptions.Right);
        b.value.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(b.value.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-32f, 0f), Vector2.zero);
        return b;
    }

    // 顶部横向拉伸的一行：距左 left、距顶 top、高 height、右边偏移 right（负数 = 离右边的距离）。
    static void TopRow(RectTransform rt, float left, float top, float height, float right)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(left, -(top + height));
        rt.offsetMax = new Vector2(right, -top);
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
