using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================
// 任务浮窗（右下角）：营地现在的目标、完成了没有、谁负责。
//   · 右下角（建造按钮左边）一个标签按钮「goals : 2」：点一下弹出浮窗，再点一下收起。
//     有目标完成了但你还没看到时，按钮黄绿交替闪。
//   · 浮窗打开动画和 NPC 对话窗口一样（UiPopup：线从按钮伸出来 → 变成上沿 → 往下展开）。
//   · 每个目标两行：
//       □ 目标1　将营地用围墙围起来
//         负责人：田中、铃木
//     完成时：方块里画一个勾（两笔），接着一条线把目标文字划掉、文字变灰；过几秒从列表里拿掉。
//     浮窗关着的时候完成的目标，下次打开时再播这段动画。
//   · 目标从哪来、怎么算完成、负责人是谁：见 CampGoals。
//   · 进入建造模式时按钮和浮窗都收起来（右下角给建造菜单让位）。
// 由 HudLayout.Start 调 Ensure() 创建。
// ============================================================================
public class NpcTaskPanel : MonoBehaviour
{
    public static NpcTaskPanel Instance { get; private set; }

    public float width = 400f;
    public float refreshInterval = 0.5f;
    [Header("完成动画（秒）")]
    public float checkTime = 0.3f;    // 方块里画勾
    public float strikeTime = 0.4f;   // 划掉目标文字

    const float RowH = 46f, BottomPad = 10f, BoxSize = 16f, TextX = 38f;
    static float TopPad => UiStyle.TagH + 10f;
    static readonly Color Green = UiStyle.Green;
    static readonly Color DoneGray = new Color(0.56f, 0.58f, 0.56f, 1f);
    static readonly Color OwnerColor = new Color(0.62f, 0.7f, 0.65f, 1f);

    class Row
    {
        public CampGoals.Goal goal;
        public RectTransform root, stroke1, stroke2, strike;
        public TextMeshProUGUI title, owner;
        public float animT = -1f;    // -1 = 没在播；>= 0 = 播到第几秒
    }

    GameObject _canvasGo;
    RectTransform _btn;
    TMP_Text _btnText;
    Image _btnFill;
    UiPopup _popup;
    TextMeshProUGUI _empty;
    readonly List<Row> _rows = new List<Row>();
    int _rowsVersion = -1;
    float _refresh, _blink;

    // 勾的三个点（方块左下角为原点，方块 16×16）
    static readonly Vector2 CheckA = new Vector2(3.5f, 8.5f), CheckB = new Vector2(7f, 4.5f), CheckC = new Vector2(13.5f, 13f);

    public static NpcTaskPanel Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("NpcTaskPanel").AddComponent<NpcTaskPanel>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Build()
    {
        _canvasGo = new GameObject("NpcTaskCanvas");
        _canvasGo.transform.SetParent(transform, false);
        Canvas canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60; // 悬停信息框（70）在它上面
        CanvasScaler scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasGo.AddComponent<GraphicRaycaster>();

        // 右下角的标签按钮
        GameObject b = new GameObject("GoalsButton", typeof(RectTransform));
        b.transform.SetParent(_canvasGo.transform, false);
        _btn = (RectTransform)b.transform;
        _btn.anchorMin = _btn.anchorMax = new Vector2(1f, 0f);
        _btn.pivot = new Vector2(1f, 0f);
        _btn.anchoredPosition = new Vector2(-180f, 16f);
        _btnText = UiStyle.Tag(_btn, "goals : 0", Green, 15f);
        _btnFill = _btnText.transform.parent.GetComponent<Image>();
        _btnFill.raycastTarget = true;
        Button button = _btnFill.gameObject.AddComponent<Button>();
        button.targetGraphic = _btnFill;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Toggle);

        // 弹出浮窗
        _popup = UiPopup.Create("NpcTaskPopup", 65);
        _popup.transform.SetParent(transform, false);
        _popup.SetTag("objectives");
        _empty = NewText(_popup.Body, "Empty", 14f, OwnerColor);
        RectTransform er = _empty.rectTransform;
        er.anchorMin = er.anchorMax = er.pivot = new Vector2(0f, 1f);
        er.anchoredPosition = new Vector2(14f, -TopPad);
        er.sizeDelta = new Vector2(width - 28f, 22f);
        RefreshButton();
    }

    void Update()
    {
        bool hide = (BuildManager.Instance != null && BuildManager.Instance.IsBuildMode) || CountAlive() == 0;
        if (_btn.gameObject.activeSelf == hide) _btn.gameObject.SetActive(!hide);
        if (hide)
        {
            if (_popup.IsOpen) _popup.Close();
            return;
        }

        _refresh -= Time.unscaledDeltaTime;
        if (_refresh <= 0f)
        {
            _refresh = refreshInterval;
            CampGoals.Tick();
            PlaceBesideBuildButton();
            RefreshButton();
            if (_popup.IsOpen) SyncRows();
        }
        if (_popup.IsOpen && !_popup.Animating) Animate(Time.unscaledDeltaTime);

        // 有完成了但还没看到的目标：按钮黄绿交替闪
        bool unseen = false;
        foreach (CampGoals.Goal g in CampGoals.All) if (g.Done && !g.animated) { unseen = true; break; }
        if (unseen && !_popup.IsOpen)
        {
            _blink += Time.unscaledDeltaTime;
            _btnFill.color = Mathf.Repeat(_blink, 1f) < 0.5f ? UiStyle.Yellow : Green;
        }
        else if (_btnFill.color != Green) { _btnFill.color = Green; _blink = 0f; }
    }

    void RefreshButton()
    {
        int open = 0;
        foreach (CampGoals.Goal g in CampGoals.All) if (!g.Done) open++;
        UiStyle.SetTag(_btnText, "goals : " + open + (_popup != null && _popup.IsOpen ? "  [-]" : "  [+]"), _btnFill.color);
        RectTransform tag = (RectTransform)_btnText.transform.parent;
        tag.anchorMin = tag.anchorMax = tag.pivot = new Vector2(0f, 1f);
        tag.anchoredPosition = Vector2.zero;
        _btn.sizeDelta = tag.sizeDelta;
    }

    // 放在建造按钮左边（右下角那一块），不挡按钮。
    void PlaceBesideBuildButton()
    {
        float x = -16f;
        BuildModeToggle t = FindFirstObjectByType<BuildModeToggle>();
        RectTransform rt = t != null ? t.transform as RectTransform : null;
        if (rt != null && rt.gameObject.activeInHierarchy)
        {
            Canvas c = rt.GetComponentInParent<Canvas>();
            float scale = c != null ? c.rootCanvas.transform.localScale.x : 1f;
            float mine = _canvasGo.transform.localScale.x;
            float w = rt.rect.width * (mine > 0f ? scale / mine : 1f);
            x = -(16f + w + 12f);
        }
        _btn.anchoredPosition = new Vector2(x, 16f);
    }

    // ============ 浮窗 ============

    public void Toggle()
    {
        if (_popup.IsOpen) { _popup.Close(); RefreshButton(); return; }
        CampGoals.Tick();
        SyncRows();
        Vector2 size = PopupSize();
        Vector3[] c = UiPopup.ScreenCorners(_btn);     // 0 左下、1 左上、2 右上
        float k = _popup.UnitToPixels;
        // 浮窗在按钮正上方、右边对齐：线从按钮右上角往上伸到浮窗右上角，再变成上沿往左展开
        Vector2 topLeft = new Vector2(c[2].x - size.x * k, c[2].y + (14f + size.y) * k);
        _popup.Open(c[2], topLeft, size);
        RefreshButton();
    }

    Vector2 PopupSize()
    {
        int n = Mathf.Max(1, _rows.Count);
        return new Vector2(Mathf.Max(width, _popup.TagWidth + 24f), TopPad + n * RowH + BottomPad);
    }

    // 目标列表变了就重排；没变就只更新文字（负责人会变）。
    void SyncRows()
    {
        IReadOnlyList<CampGoals.Goal> goals = CampGoals.All;
        bool same = _rowsVersion == CampGoals.Version && _rows.Count == goals.Count;
        if (!same)
        {
            foreach (Row r in _rows) if (r.root != null) Destroy(r.root.gameObject);
            _rows.Clear();
            for (int i = 0; i < goals.Count; i++) _rows.Add(MakeRow(goals[i], i));
            _rowsVersion = CampGoals.Version;
            _popup.Resize(PopupSize());
        }
        _empty.gameObject.SetActive(_rows.Count == 0);
        _empty.text = L("taskpanel_empty");

        for (int i = 0; i < _rows.Count; i++)
        {
            Row r = _rows[i];
            r.title.text = LF("taskpanel_goal_no", i + 1) + "　" + CampGoals.Text(r.goal);
            List<string> owners = CampGoals.Owners(r.goal);
            r.owner.text = owners == null ? LF("taskpanel_owner", L("taskpanel_owner_ai"))
                         : owners.Count == 0 ? L("taskpanel_owner_none")
                         : LF("taskpanel_owner", string.Join(LanguageGuard.Pick("、", "・"), owners));
            if (r.animT < 0f) ShowDone(r, r.goal.Done && r.goal.animated);
        }
    }

    Row MakeRow(CampGoals.Goal g, int i)
    {
        Row r = new Row { goal = g };
        r.root = NewRect("Goal" + i, _popup.Body);
        r.root.anchorMin = r.root.anchorMax = r.root.pivot = new Vector2(0f, 1f);
        r.root.anchoredPosition = new Vector2(0f, -(TopPad + i * RowH));
        r.root.sizeDelta = new Vector2(width, RowH);

        RectTransform box = NewRect("Box", r.root);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 1f);
        box.anchoredPosition = new Vector2(14f, -3f);
        box.sizeDelta = new Vector2(BoxSize, BoxSize);
        UiStyle.Frame(box, new Color(Green.r, Green.g, Green.b, 0.9f), 1.5f);
        r.stroke1 = Stroke(box);
        r.stroke2 = Stroke(box);

        r.title = NewText(r.root, "Title", 15f, UiStyle.TextMain);
        RectTransform tr = r.title.rectTransform;
        tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0f, 1f);
        tr.anchoredPosition = new Vector2(TextX, 0f);
        tr.sizeDelta = new Vector2(width - TextX - 12f, 22f);

        r.strike = NewRect("Strike", tr);
        r.strike.anchorMin = r.strike.anchorMax = new Vector2(0f, 0.5f);
        r.strike.pivot = new Vector2(0f, 0.5f);
        r.strike.anchoredPosition = Vector2.zero;
        r.strike.sizeDelta = new Vector2(0f, 2f);
        Image si = r.strike.gameObject.AddComponent<Image>();
        si.color = new Color(Green.r, Green.g, Green.b, 0.85f);
        si.raycastTarget = false;

        r.owner = NewText(r.root, "Owner", 13f, OwnerColor);
        RectTransform or = r.owner.rectTransform;
        or.anchorMin = or.anchorMax = or.pivot = new Vector2(0f, 1f);
        or.anchoredPosition = new Vector2(TextX, -22f);
        or.sizeDelta = new Vector2(width - TextX - 12f, 20f);
        return r;
    }

    // 已完成（并且动画放过了）：勾画满、文字划掉变灰；没完成：都收起来。
    void ShowDone(Row r, bool done)
    {
        SetSegment(r.stroke1, CheckA, done ? CheckB : CheckA);
        SetSegment(r.stroke2, CheckB, done ? CheckC : CheckB);
        r.strike.sizeDelta = new Vector2(done ? StrikeWidth(r) : 0f, 2f);
        r.title.color = done ? DoneGray : UiStyle.TextMain;
        r.owner.color = done ? DoneGray : OwnerColor;
    }

    // 刚完成的目标：画勾（两笔）→ 划掉文字。
    void Animate(float dt)
    {
        foreach (Row r in _rows)
        {
            if (r.goal == null || !r.goal.Done || r.goal.animated) continue;
            if (r.animT < 0f) r.animT = 0f;
            r.animT += dt;
            float t = r.animT;
            float first = checkTime * 0.4f, second = checkTime - first;
            SetSegment(r.stroke1, CheckA, Vector2.Lerp(CheckA, CheckB, EaseOut(t / Mathf.Max(0.001f, first))));
            SetSegment(r.stroke2, CheckB, Vector2.Lerp(CheckB, CheckC, EaseOut((t - first) / Mathf.Max(0.001f, second))));
            float s = EaseOut((t - checkTime) / Mathf.Max(0.001f, strikeTime));
            r.strike.sizeDelta = new Vector2(StrikeWidth(r) * s, 2f);
            r.title.color = Color.Lerp(UiStyle.TextMain, DoneGray, s);
            r.owner.color = Color.Lerp(OwnerColor, DoneGray, s);
            if (t >= checkTime + strikeTime)
            {
                r.goal.animated = true;
                r.goal.animatedAt = Time.time;
                r.animT = -1f;
                ShowDone(r, true);
            }
        }
    }

    static float StrikeWidth(Row r)
    {
        float w = r.title.GetPreferredValues(r.title.text).x;
        return Mathf.Min(w + 4f, r.title.rectTransform.rect.width);
    }

    // ============ 小工具 ============

    RectTransform Stroke(RectTransform box)
    {
        RectTransform s = NewRect("Check", box);
        s.anchorMin = s.anchorMax = Vector2.zero;   // 方块左下角为原点
        s.pivot = new Vector2(0f, 0.5f);
        s.sizeDelta = new Vector2(0f, 2.5f);
        Image img = s.gameObject.AddComponent<Image>();
        img.color = Green;
        img.raycastTarget = false;
        s.gameObject.SetActive(false);
        return s;
    }

    static void SetSegment(RectTransform seg, Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        float len = d.magnitude;
        bool on = len > 0.3f;
        if (seg.gameObject.activeSelf != on) seg.gameObject.SetActive(on);
        if (!on) return;
        seg.anchoredPosition = from;
        seg.sizeDelta = new Vector2(len + 1f, seg.sizeDelta.y);   // +1：两笔接头处不留缝
        seg.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    static TextMeshProUGUI NewText(Transform parent, string name, float size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = GameFonts.Find();
        if (f != null) t.font = f;
        t.fontSize = size;
        t.color = color;
        t.richText = true;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static int CountAlive()
    {
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w == null) return 0;
        int k = 0;
        foreach (NpcData n in w.npcs) if (n != null && !n.IsDead) k++;
        return k;
    }

    static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;
}
