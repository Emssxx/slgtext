using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================
// 底部任务栏（右下角一排标签按钮，风格统一成任务栏的样子：淡绿底色 + 等宽英文字）：
//   从左到右：crew | logs | events | goals | build（各模块自己 Add 进来，按 order 排）。
//   · 点按钮：在这一排上面弹出它的浮窗（UiPopup，线从按钮伸到浮窗左上角 → 上沿 → 往下展开），再点一下收起。
//   · 同时打开好几个浮窗时不互相遮挡：从屏幕右边开始往左并排；一行放不下就在上面再起一行（并列）。
//   · build 按钮没有浮窗：点了进入 / 退出建造模式。建造模式下只留 build 按钮，其它按钮和浮窗都收起来。
//   · 模块有新东西（新事件、新日志、目标完成）可以把 blink 设成 true：按钮黄绿交替闪，打开浮窗就停。
// 用法：HudDock.Ensure().Add("goals", 50, () => "goals : 2", () => 内容大小, onToggle: open => {...}, popupTag: "objectives");
// ============================================================================
public class HudDock : MonoBehaviour
{
    public static HudDock Instance { get; private set; }

    public float rightMargin = 16f;
    public float bottomMargin = 16f;
    public float buttonGap = 8f;
    public float popupGap = 12f;      // 并排的浮窗之间
    public float popupLift = 14f;     // 浮窗下沿离按钮这一排多高

    const float BtnFont = 15f;
    static readonly Color Green = UiStyle.Green;

    public class Entry
    {
        public string id;
        public int order;                 // 从左到右排的顺序（越大越靠右）
        public Func<string> label;        // 按钮上的字
        public Func<Vector2> measure;     // 浮窗内容大小；null = 没有浮窗
        public Action onClick;            // 没有浮窗的按钮：点了做什么
        public Action<bool> onToggle;     // 浮窗打开（true）/ 收起（false）时
        public bool showInBuildMode;
        public bool blink;                // 有新东西：按钮黄绿交替闪
        public UiPopup popup;
        public RectTransform btn;
        public TMP_Text text;
        public Image fill;
        public bool IsOpen => popup != null && popup.IsOpen;
    }

    readonly List<Entry> _entries = new List<Entry>();
    RectTransform _canvasRt;
    float _blinkT, _labelTimer;

    public static HudDock Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("HudDock").AddComponent<HudDock>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        GameObject cgo = new GameObject("HudDockCanvas");
        cgo.transform.SetParent(transform, false);
        Canvas canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60; // 悬停信息框（70）在它上面
        CanvasScaler scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();
        _canvasRt = (RectTransform)cgo.transform;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public Entry Add(string id, int order, Func<string> label, Func<Vector2> measure = null, Action onClick = null,
                     Action<bool> onToggle = null, string popupTag = null, bool interactable = false, bool showInBuildMode = false)
    {
        Entry e = new Entry
        {
            id = id, order = order, label = label, measure = measure, onClick = onClick, onToggle = onToggle,
            showInBuildMode = showInBuildMode,
        };
        GameObject b = new GameObject("Dock_" + id, typeof(RectTransform));
        b.transform.SetParent(_canvasRt, false);
        e.btn = (RectTransform)b.transform;
        e.btn.anchorMin = e.btn.anchorMax = e.btn.pivot = new Vector2(1f, 0f);
        e.text = UiStyle.Tag(e.btn, id, Green, BtnFont);
        e.fill = e.text.transform.parent.GetComponent<Image>();
        e.fill.raycastTarget = true;
        Button button = e.fill.gameObject.AddComponent<Button>();
        button.targetGraphic = e.fill;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Press(e));
        if (measure != null)
        {
            e.popup = UiPopup.Create("DockPopup_" + id, 65);
            e.popup.transform.SetParent(transform, false);
            e.popup.SetTag(popupTag ?? id);
            e.popup.Interactable = interactable;
            e.popup.KeepTether = false;   // 并排之后线会穿过别的浮窗：打开完就收掉
        }
        _entries.Add(e);
        _entries.Sort((x, y) => x.order.CompareTo(y.order));
        RefreshLabel(e);
        return e;
    }

    void Press(Entry e)
    {
        if (e.popup == null) { e.onClick?.Invoke(); RefreshLabel(e); return; }
        if (e.IsOpen) Close(e);
        else Open(e);
    }

    public void Open(Entry e)
    {
        if (e == null || e.popup == null || e.IsOpen) return;
        e.blink = false;
        e.onToggle?.Invoke(true);
        Dictionary<Entry, Vector2> tl = LayoutPopups(e);
        foreach (KeyValuePair<Entry, Vector2> kv in tl)
        {
            Vector2 size = kv.Key.measure();
            if (kv.Key == e) kv.Key.popup.OpenCanvas(ButtonTopLeft(e), kv.Value, size);
            else { kv.Key.popup.Resize(size); kv.Key.popup.PlaceCanvas(kv.Value); }
        }
        RefreshLabel(e);
    }

    public void Close(Entry e)
    {
        if (e == null || !e.IsOpen) return;
        e.popup.Close();
        e.onToggle?.Invoke(false);
        RefreshLabel(e);
    }

    void Update()
    {
        bool build = BuildManager.Instance != null && BuildManager.Instance.IsBuildMode;

        // 按钮从右往左排；建造模式下只留 build
        float x = -rightMargin;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry e = _entries[i];
            bool show = !build || e.showInBuildMode;
            if (e.btn.gameObject.activeSelf != show) e.btn.gameObject.SetActive(show);
            if (!show) { if (e.IsOpen) Close(e); continue; }
            e.btn.anchoredPosition = new Vector2(x, bottomMargin);
            x -= e.btn.sizeDelta.x + buttonGap;
        }

        _labelTimer -= Time.unscaledDeltaTime;
        if (_labelTimer <= 0f)
        {
            _labelTimer = 0.25f;
            foreach (Entry e in _entries) RefreshLabel(e);
        }

        // 有新东西：黄绿交替闪
        _blinkT += Time.unscaledDeltaTime;
        bool on = Mathf.Repeat(_blinkT, 1f) < 0.5f;
        foreach (Entry e in _entries)
        {
            Color c = e.blink && !e.IsOpen && on ? UiStyle.Yellow : Green;
            if (e.fill.color != c) e.fill.color = c;
        }

        // 打开着的浮窗：内容大小会变，每帧重新并排一次
        bool any = false;
        foreach (Entry e in _entries) if (e.IsOpen) { any = true; break; }
        if (!any) return;
        foreach (KeyValuePair<Entry, Vector2> kv in LayoutPopups(null))
        {
            kv.Key.popup.Resize(kv.Key.measure());
            kv.Key.popup.PlaceCanvas(kv.Value);
        }
    }

    // 打开着的浮窗（加上正要打开的 opening）从右往左并排，一行放不下就往上再起一行。返回每个浮窗的左上角（画布坐标）。
    Dictionary<Entry, Vector2> LayoutPopups(Entry opening)
    {
        Dictionary<Entry, Vector2> result = new Dictionary<Entry, Vector2>();
        float w = _canvasRt.rect.width;
        float right = w - rightMargin;
        float bottom = bottomMargin + ButtonHeight() + popupLift;
        float rowH = 0f;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry e = _entries[i];
            if (e.popup == null || (!e.IsOpen && e != opening)) continue;
            Vector2 s = e.measure();
            if (right - s.x < 16f && rowH > 0f)
            {
                bottom += rowH + popupGap;
                right = w - rightMargin;
                rowH = 0f;
            }
            result[e] = new Vector2(right - s.x, bottom + s.y);
            right -= s.x + popupGap;
            rowH = Mathf.Max(rowH, s.y);
        }
        return result;
    }

    // 给别的东西（建筑完成提示）用：在 [xMin, xMax] 这一段（画布坐标），任务栏按钮和打开着的浮窗上面从多高开始是空的。
    public float FreeTop(float xMin, float xMax)
    {
        float top = bottomMargin + ButtonHeight() + popupLift;
        foreach (Entry e in _entries)
        {
            if (!e.IsOpen) continue;
            Vector2 tl = e.popup.Window.anchoredPosition;
            Vector2 s = e.popup.Size;
            if (tl.x + s.x < xMin || tl.x > xMax) continue;
            top = Mathf.Max(top, tl.y + popupGap);
        }
        return top;
    }

    float ButtonHeight()
    {
        foreach (Entry e in _entries) return e.btn.sizeDelta.y;
        return BtnFont + 7f;
    }

    // 按钮左上角（画布坐标，左下角为原点）。
    Vector2 ButtonTopLeft(Entry e)
    {
        float w = _canvasRt.rect.width;
        Vector2 p = e.btn.anchoredPosition;
        return new Vector2(w + p.x - e.btn.sizeDelta.x, p.y + e.btn.sizeDelta.y);
    }

    void RefreshLabel(Entry e)
    {
        string text = e.label != null ? e.label() : e.id;
        if (e.popup != null) text += e.IsOpen ? "  [-]" : "  [+]";
        UiStyle.SetTag(e.text, text, e.fill.color);
        RectTransform tag = (RectTransform)e.text.transform.parent;
        if (tag.sizeDelta.x < 14f) UiStyle.FitTag(e.text);
        e.btn.sizeDelta = tag.sizeDelta;
    }
}
