using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// HUD 布局（运行时整理主界面，不用改场景）：
//   左上角：事件栏 + 日志栏收进一个抽屉，点右侧的小把手滑动展开 / 收起。幸存者列表按钮跟着抽屉走。
//   右下角：建造按钮；进入建造模式时，建造菜单从屏幕右边滑出来（菜单本身仍由 BuildManager 生成）。
//   右上角：天数（电子表风格段码字，第 1 天显示 1……）；
//   顶部中间：电力 | 净化水 | 「终端机」按钮（ResourceHud），下面一行是天气和气温（ClimateSystem）。
//   NPC 对话窗口在左下角（见 NpcChatWindow）。
// 事件栏、日志栏是两个独立的识别框（UiStyle）：淡绿细边 + 左上角淡绿标签（就是原来的标题「事件」「日志」）。
// 抽屉本身透明，只负责一起滑动。
// 由 WorldManager.Start 调 HudLayout.Ensure() 创建。场景里原有的面板只是被"挪了位置"，各自的逻辑不变。
public class HudLayout : MonoBehaviour
{
    public static HudLayout Instance { get; private set; }

    [Header("左上角：事件 / 日志抽屉")]
    public float drawerWidth = 300f;
    public float margin = 10f;
    public bool drawerOpenOnStart = true;

    [Header("右下角：建造")]
    [Tooltip("建造按钮离屏幕右下角的距离（下面留出 AI 状态文字的位置）")]
    public Vector2 buildButtonOffset = new Vector2(-16f, 60f);

    [Header("天数（右上角）")]
    [Tooltip("1 = 屏幕右上角；0.5 = 屏幕右边正中")]
    [Range(0f, 1f)] public float dayAnchorY = 1f;
    [Tooltip("天数框大小（原来是 96×64）")]
    public Vector2 daySize = new Vector2(150f, 100f);
    [Tooltip("离屏幕右上角的距离")]
    public Vector2 dayOffset = new Vector2(-14f, -6f);

    // 建造菜单停靠的位置（右下角锚点坐标，BuildManager 生成菜单时读取）。
    public static Vector2 BuildMenuPos = new Vector2(-16f, 106f);

    public bool DrawerOpen => _drawerOpen;

    RectTransform _drawer;
    UiSlider _drawerSlider;
    TMP_Text _tabLabel;
    // 栏的标题 → 左上角标签：标题文字（会随语言切换）每帧同步到标签上
    readonly System.Collections.Generic.List<TMP_Text> _titleSrc = new System.Collections.Generic.List<TMP_Text>();
    readonly System.Collections.Generic.List<TMP_Text> _titleTag = new System.Collections.Generic.List<TMP_Text>();
    RectTransform _tabArrow;
    bool _drawerOpen;
    TMP_FontAsset _font;

    const float TabWidth = 30f;
    const float TabHeight = 92f;

    public static HudLayout Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("HudLayout").AddComponent<HudLayout>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        LocalizationManager.OnLanguageChanged += RefreshTexts;
    }

    void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= RefreshTexts;
        if (Instance == this) Instance = null;
    }

    IEnumerator Start()
    {
        // 等一帧：让场景里各面板（NpcListPanel 运行时生成的按钮、折叠栏测量高度等）都先初始化完。
        yield return null;

        _font = GameFonts.Find();
        Canvas root = FindHudCanvas();
        if (root == null)
        {
            Debug.LogWarning("[HudLayout] 找不到主界面 Canvas，界面布局保持原样。");
            yield break;
        }

        BuildLogDrawer(root.transform);
        PlaceBuildButton();
        BuildDayCounter(root.transform);
        ResourceHud.Create(root.transform, _font); // 顶部：电力 / 净化水 + 终端机按钮；左边日志栏下面：木材 / 石头 / 矿石
        HoverInfo.Ensure();                        // 鼠标悬停在 NPC / 建筑上的信息框 + 地图坐标浮窗
        CameraController.Ensure();                 // 滚轮缩放 + 中键拖动画面
        AiScheduler.Ensure();                      // AI 请求调度器（F9 调试面板）
        NpcTaskPanel.Ensure();                     // 右下角：任务按钮 → 弹出营地目标浮窗（目标、完成打勾、负责人）
    }

    // ============ 左上角：事件 / 日志抽屉 ============

    void BuildLogDrawer(Transform root)
    {
        RectTransform events = null, logs = null;
        if (EventLogManager.Instance != null)
            events = SectionOf(EventLogManager.Instance.scrollRect != null ? (Component)EventLogManager.Instance.scrollRect : EventLogManager.Instance.contentParent);
        if (LogManager.Instance != null)
            logs = SectionOf(LogManager.Instance.scrollRect != null ? (Component)LogManager.Instance.scrollRect : LogManager.Instance.contentParent);
        if (events == null && logs == null)
        {
            Debug.LogWarning("[HudLayout] 没找到事件栏 / 日志栏，左上角抽屉不创建。");
            return;
        }

        // 抽屉本体：宽度固定，高度跟着里面的栏走（栏自己还能单独折叠）
        _drawer = NewRect("HudLogDrawer", root);
        _drawer.anchorMin = _drawer.anchorMax = _drawer.pivot = new Vector2(0f, 1f);
        _drawer.sizeDelta = new Vector2(drawerWidth, 0f);
        Image drawerImg = _drawer.gameObject.AddComponent<Image>();
        drawerImg.color = Color.clear;          // 透明：事件、日志各自是一个框
        drawerImg.raycastTarget = false;        // 两个框中间的空隙不挡地图
        VerticalLayoutGroup v = _drawer.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(0, 0, 0, 0);
        v.spacing = 10f;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        ContentSizeFitter fit = _drawer.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (events != null) events.SetParent(_drawer, false);
        if (logs != null) logs.SetParent(_drawer, false);

        StyleSection(events, "event_log");
        StyleSection(logs, "sys_log");
        if (EventLogManager.Instance != null) EventLogManager.Instance.eventColor = new Color(1f, 0.6f, 0.56f); // 事件 = 异常：淡红
        if (LogManager.Instance != null) LogManager.Instance.nameColor = UiStyle.Green;                       // NPC 名字：淡绿

        BuildDrawerTab();
        AttachNpcList();

        _drawerSlider = _drawer.gameObject.AddComponent<UiSlider>();
        _drawerOpen = drawerOpenOnStart;
        _drawer.anchoredPosition = DrawerPos(_drawerOpen);
        RefreshTexts();

        HideLegacyDialogPanel();
    }

    // 抽屉右侧的小把手：箭头 + 竖排"记录"。
    void BuildDrawerTab()
    {
        RectTransform tab = NewRect("Tab", _drawer);
        tab.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        tab.anchorMin = tab.anchorMax = new Vector2(1f, 1f);
        tab.pivot = new Vector2(0f, 1f);
        tab.anchoredPosition = Vector2.zero;
        tab.sizeDelta = new Vector2(TabWidth, TabHeight);
        Image img = tab.gameObject.AddComponent<Image>();
        img.color = new Color(UiStyle.Panel.r, UiStyle.Panel.g, UiStyle.Panel.b, 0.95f);
        UiStyle.Frame(tab, new Color(UiStyle.Green.r, UiStyle.Green.g, UiStyle.Green.b, 0.7f));
        Button b = tab.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(ToggleDrawer);

        TextMeshProUGUI arrow = NewText(tab, "Arrow", 15f);
        arrow.text = "▶";
        arrow.color = UiStyle.Green;
        _tabArrow = arrow.rectTransform;
        _tabArrow.anchorMin = new Vector2(0f, 1f);
        _tabArrow.anchorMax = new Vector2(1f, 1f);
        _tabArrow.pivot = new Vector2(0.5f, 0.5f);
        _tabArrow.sizeDelta = new Vector2(0f, 26f);
        _tabArrow.anchoredPosition = new Vector2(0f, -17f);

        TextMeshProUGUI label = NewText(tab, "Label", 15f);
        label.color = UiStyle.Green;
        Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 4f), new Vector2(0f, -30f));
        _tabLabel = label;
    }

    // 幸存者列表（NpcListPanel 运行时生成的按钮和面板）挂到抽屉把手右边，跟着抽屉一起滑。
    void AttachNpcList()
    {
        GameObject btn = GameObject.Find("NpcListBtn");
        if (btn == null) return;
        RectTransform btnRt = btn.transform as RectTransform;
        Transform panel = btn.transform.parent != null ? btn.transform.parent.Find("NpcListPanel") : null;
        PlaceBesideTab(btnRt, new Vector2(TabWidth + 8f, 0f));
        if (panel != null) PlaceBesideTab(panel as RectTransform, new Vector2(TabWidth + 8f, -(btnRt.sizeDelta.y + 6f)));
    }

    void PlaceBesideTab(RectTransform rt, Vector2 pos)
    {
        if (rt == null) return;
        rt.SetParent(_drawer, false);
        LayoutElement le = rt.GetComponent<LayoutElement>();
        if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
    }

    public void ToggleDrawer() { SetDrawer(!_drawerOpen); }

    // 左边那一列（资源栏，ResourceHud）从哪里开始往下排（离画布顶边的距离）：
    // 事件 / 日志抽屉展开时贴在它下沿下面；收起时排在把手、幸存者列表下面。
    public float LeftColumnTop()
    {
        if (_drawer == null) return margin;
        if (_drawerOpen) return margin + _drawer.rect.height + 10f;
        float bottom = TabHeight;
        foreach (Transform c in _drawer)
        {
            RectTransform rt = c as RectTransform;
            LayoutElement le = rt != null ? rt.GetComponent<LayoutElement>() : null;
            if (rt == null || le == null || !le.ignoreLayout || !rt.gameObject.activeSelf) continue;
            bottom = Mathf.Max(bottom, -rt.anchoredPosition.y + rt.rect.height);
        }
        return margin + bottom + 10f;
    }

    public float LeftMargin => margin;

    public void SetDrawer(bool open)
    {
        if (_drawer == null) return;
        _drawerOpen = open;
        _drawerSlider.SlideTo(DrawerPos(open));
        RefreshTexts();
    }

    // 展开：贴左上角；收起：整个抽屉移到屏幕左边外面，只露出把手。
    Vector2 DrawerPos(bool open)
    {
        return open ? new Vector2(margin, -margin) : new Vector2(-drawerWidth, -margin);
    }

    void RefreshTexts()
    {
        if (_tabLabel != null) _tabLabel.text = L("hud_drawer_tab");
        // 展开时箭头朝左（点一下收起），收起时朝右（点一下展开）
        if (_tabArrow != null) _tabArrow.localEulerAngles = new Vector3(0f, 0f, _drawerOpen ? 180f : 0f);
    }

    // 旧的整块对话面板（事件、日志已经挪走，聊天栏已隐藏）只剩空壳：设成透明、不挡点击。
    // 面板上挂着 ChatManager / EventLogManager / LogManager，所以不 SetActive(false)，只是看不见。
    void HideLegacyDialogPanel()
    {
        if (ChatManager.Instance != null && !ChatManager.Instance.hideLegacyChat) return; // 还在用旧聊天栏：保留
        GameObject panel = EventLogManager.Instance != null ? EventLogManager.Instance.gameObject : null;
        if (panel == null || panel.GetComponent<Canvas>() != null) return;
        if (_drawer != null && _drawer.IsChildOf(panel.transform)) return;
        CanvasGroup g = panel.GetComponent<CanvasGroup>();
        if (g == null) g = panel.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        g.blocksRaycasts = false;
        g.interactable = false;
    }

    // 事件栏 / 日志栏换成识别框样式：深色底（去掉圆角）+ 淡绿细边；原来的标题字藏起来，
    // 换成框左上角的淡绿标签（文字照抄标题，切换语言也跟着变）；标题栏本身透明，只留右边的箭头和点击折叠。
    // 场景里的物体只改颜色，不改结构；新加的边框 / 标签不参与排版、不挡点击。
    void StyleSection(RectTransform sec, string fallbackTitle)
    {
        if (sec == null) return;
        Image bg = sec.GetComponent<Image>();
        if (bg != null) { bg.sprite = null; bg.color = new Color(UiStyle.Panel.r, UiStyle.Panel.g, UiStyle.Panel.b, 0.82f); }

        TMP_Text title = null;
        CollapsibleSection cs = sec.GetComponent<CollapsibleSection>();
        if (cs != null && cs.headerButton != null)
        {
            Image hb = cs.headerButton.GetComponent<Image>();
            if (hb != null) { hb.sprite = null; hb.color = Color.clear; } // 透明，但还能点
            foreach (TMP_Text t in cs.headerButton.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == cs.toggleIcon) { t.color = UiStyle.Green; continue; }
                if (title == null) title = t;
                t.color = Color.clear;                                     // 原标题藏起来（文字还在，用来同步）
            }
        }

        Image frame = UiStyle.Frame(sec, new Color(UiStyle.Green.r, UiStyle.Green.g, UiStyle.Green.b, 0.75f), 1.2f);
        frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        frame.transform.SetAsLastSibling();

        TMP_Text tag = UiStyle.Tag(sec, title != null ? title.text : fallbackTitle, UiStyle.Green);
        RectTransform tagRt = (RectTransform)tag.transform.parent;
        tagRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        tagRt.SetAsLastSibling();
        if (title != null) { _titleSrc.Add(title); _titleTag.Add(tag); }
    }

    // 标题文字变了（切换语言）就更新标签。
    void Update()
    {
        for (int i = 0; i < _titleSrc.Count; i++)
            if (_titleSrc[i] != null && _titleTag[i] != null && !string.IsNullOrEmpty(_titleSrc[i].text))
                UiStyle.SetTag(_titleTag[i], _titleSrc[i].text, UiStyle.Green);
    }

    // 从栏里的某个物体往上找，第一个挂了折叠控制器（CollapsibleSection）的就是这一栏。
    static RectTransform SectionOf(Component inside)
    {
        if (inside == null) return null;
        Transform t = inside.transform;
        while (t != null)
        {
            if (t.GetComponent<CollapsibleSection>() != null) return t as RectTransform;
            t = t.parent;
        }
        return null;
    }

    // ============ 右下角：建造按钮 ============

    void PlaceBuildButton()
    {
        BuildModeToggle toggle = FindFirstObjectByType<BuildModeToggle>(FindObjectsInactive.Include);
        RectTransform rt = toggle != null ? toggle.transform as RectTransform : null;
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = buildButtonOffset;
        BuildMenuPos = buildButtonOffset + new Vector2(0f, rt.sizeDelta.y + 8f);
    }

    // ============ 天数 ============

    void BuildDayCounter(Transform root)
    {
        bool top = dayAnchorY > 0.75f;

        // 透明底 + 电子表风格的段码数字（淡绿色、淡淡发光），下面一行小字 DAY（见 SegmentDisplay.cs）
        RectTransform frame = NewRect("HudDay", root);
        frame.anchorMin = frame.anchorMax = new Vector2(1f, dayAnchorY);
        frame.pivot = new Vector2(1f, top ? 1f : 0.5f);
        frame.anchoredPosition = new Vector2(dayOffset.x, top ? dayOffset.y : 0f);
        frame.sizeDelta = daySize;

        RectTransform digits = NewRect("Day", frame);
        Stretch(digits, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        RawImage digitImage = digits.gameObject.AddComponent<RawImage>();
        digitImage.raycastTarget = false;
        digitImage.color = Color.clear; // 第一次画好之前别闪白块
        SegmentText seg = digits.gameObject.AddComponent<SegmentText>();
        seg.subText = "DAY";

        HudDayCounter counter = frame.gameObject.AddComponent<HudDayCounter>();
        counter.segments = seg;

        // 天气 + 气温（顶部中间，电力 / 水那一排下面）
        const float resRowH = 64f; // ResourceHud 那一排的高度
        RectTransform wx = NewRect("HudWeather", root);
        wx.anchorMin = wx.anchorMax = new Vector2(0.5f, 1f);
        wx.pivot = new Vector2(0.5f, 1f);
        wx.anchoredPosition = new Vector2(0f, -(8f + resRowH + 4f));
        wx.sizeDelta = new Vector2(440f, 28f);
        Image wbg = wx.gameObject.AddComponent<Image>();
        wbg.color = new Color(0.06f, 0.07f, 0.1f, 0.7f);
        wbg.raycastTarget = false;
        TextMeshProUGUI wt = NewText(wx, "Text", 17f);
        Stretch(wt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        HudWeather hw = wx.gameObject.AddComponent<HudWeather>();
        hw.label = wt;
        hw.background = wbg;

        // 顶部中间的状态卡区域往下让一让，别被天数挡住
        if (StatusPanelManager.Instance != null)
        {
            RectTransform sp = StatusPanelManager.Instance.transform as RectTransform;
            if (sp != null && sp.anchorMin.y > 0.99f)
            {
                float y = -(8f + resRowH + 4f + 28f + 6f);
                if (sp.anchoredPosition.y > y) sp.anchoredPosition = new Vector2(sp.anchoredPosition.x, y);
            }
        }
    }

    // ============ 工具 ============

    // 主界面 Canvas = 事件栏 / 日志栏 / 状态卡所在的那个。
    static Canvas FindHudCanvas()
    {
        Component anchor = null;
        if (EventLogManager.Instance != null) anchor = EventLogManager.Instance;
        else if (LogManager.Instance != null) anchor = LogManager.Instance;
        else if (StatusPanelManager.Instance != null) anchor = StatusPanelManager.Instance;
        Canvas c = anchor != null ? anchor.GetComponentInParent<Canvas>() : null;
        return c != null ? c.rootCanvas : null;
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

    TextMeshProUGUI NewText(Transform parent, string name, float size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
}

// 顶部的天数：world.day 变了就换数字，并"跳"一下提醒玩家新的一天到了。
public class HudDayCounter : MonoBehaviour
{
    public TMP_Text label;
    public SegmentText segments; // 有它就用电子表数字，没有就用 label
    int _shown = -1;
    float _pulse;

    void Update()
    {
        if ((label == null && segments == null) || WorldManager.Instance == null || WorldManager.Instance.world == null) return;
        int day = WorldManager.Instance.world.day;
        if (day != _shown)
        {
            if (_shown >= 0) _pulse = 1f; // 开局第一次显示不跳
            _shown = day;
            if (segments != null) segments.SetText(day.ToString());
            else label.text = day.ToString();
        }
        if (_pulse > 0f)
        {
            _pulse = Mathf.Max(0f, _pulse - Time.unscaledDeltaTime / 0.45f);
            float s = 1f + 0.35f * _pulse * _pulse;
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}

// 顶部的天气 + 气温：每半秒刷新；炎热 / 寒冷 / 异常气温时变色。
public class HudWeather : MonoBehaviour
{
    public TMP_Text label;
    public Image background;
    float _timer;

    void Update()
    {
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f || label == null) return;
        _timer = 0.5f;
        ClimateSystem cs = ClimateSystem.Instance;
        bool show = cs != null;
        label.enabled = show;
        if (background != null) background.enabled = show;
        if (!show) return;
        label.text = cs.HudText(LanguageGuard.IsJp);
        switch (cs.StateOf(cs.CampTemperature))
        {
            case TempState.Hot: label.color = new Color(1f, 0.62f, 0.32f); break;
            case TempState.Cold: label.color = new Color(0.55f, 0.8f, 1f); break;
            case TempState.ExtremeHot:
            case TempState.ExtremeCold: label.color = new Color(0.9f, 0.45f, 1f); break;
            default: label.color = Color.white; break;
        }
    }
}

// 让一个 UI 面板平滑地滑到指定位置（抽屉、建造菜单用）。
// 用不受暂停影响的时间：建造模式会把 timeScale 设成 0。
public class UiSlider : MonoBehaviour
{
    public float duration = 0.22f;
    Coroutine _co;

    public void SlideTo(Vector2 target, Action done = null)
    {
        RectTransform rt = (RectTransform)transform;
        if (_co != null) { StopCoroutine(_co); _co = null; }
        if (!isActiveAndEnabled || duration <= 0f)
        {
            rt.anchoredPosition = target;
            if (done != null) done();
            return;
        }
        _co = StartCoroutine(Run(rt, rt.anchoredPosition, target, done));
    }

    IEnumerator Run(RectTransform rt, Vector2 from, Vector2 to, Action done)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            rt.anchoredPosition = from + (to - from) * k;
            yield return null;
        }
        rt.anchoredPosition = to;
        _co = null;
        if (done != null) done();
    }
}
