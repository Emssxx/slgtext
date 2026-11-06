using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================
// 弹出浮窗（识别框样式：深色底 + 淡绿细框 + 左上角标签），打开动画：
//   ① 一条细线从"起点"（点的那个图标 / 按钮）伸到浮窗【左上角】
//   ② 到了左上角拐成水平，往右伸到右上角，变成浮窗上沿
//   ③ 浮窗从上沿往下展开（内容不动，被窗口边缘逐渐露出来）
//   KeepTether = true：打开后那条线一直连着起点和浮窗左上角；false：打开后线就收掉（底部任务栏那一排用）。
// 用法：
//   UiPopup p = UiPopup.Create("名字", 层级);  p.SetTag("power");
//   往 p.Body 里放内容（Body 固定为完整大小，左上角对齐）；
//   p.Open(起点, 浮窗左上角, 大小)（屏幕坐标）或 p.OpenCanvas(...)（画布坐标：1920×1080 参考分辨率下的界面单位，左下角为原点）；
//   内容变高了 p.Resize(新大小)；挪位置 p.PlaceCanvas(左上角)；不用了 p.Close()。
// 资源详细数据（ResourceHud）、底部任务栏那一排浮窗（HudDock）、建筑完成提示（HudToasts）都用它。
// ============================================================================
public class UiPopup : MonoBehaviour
{
    [Header("打开动画（秒）")]
    public float lineTime = 0.16f;
    public float edgeTime = 0.16f;
    public float unfoldTime = 0.2f;

    public RectTransform Body { get; private set; }
    public RectTransform Window => _window;
    public bool IsOpen => _window != null && _window.gameObject.activeSelf;
    public bool Animating => _animating;
    public bool Interactable { get; set; }        // true：浮窗挡鼠标（里面有能点 / 能滚的东西）；false：鼠标穿过去
    public bool KeepTether { get; set; } = true;  // 打开后连线留着
    public Vector2 Size => _size;
    public Vector2 CanvasSize => _canvasRt != null ? _canvasRt.rect.size : new Vector2(1920f, 1080f);

    static readonly Color Green = UiStyle.Green;
    const float LineWidth = 1f;   // 动画里那条线的粗细（和窗口边框差不多细）

    RectTransform _canvasRt, _window, _segLine, _segEdge;
    CanvasGroup _group;
    TextMeshProUGUI _tag;
    bool _animating, _hasOrigin;
    float _t, _alpha = 1f;
    Vector2 _origin, _tl, _size;   // 画布左下角为原点

    public static UiPopup Create(string name, int sortingOrder)
    {
        GameObject go = new GameObject(name);
        UiPopup p = go.AddComponent<UiPopup>();
        p.Build(sortingOrder);
        return p;
    }

    void Build(int sortingOrder)
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _canvasRt = (RectTransform)transform;

        _window = NewRect("Window", transform);
        _window.anchorMin = _window.anchorMax = Vector2.zero;   // 锚点在画布左下角
        _window.pivot = new Vector2(0f, 1f);                     // 轴心在窗口左上角
        Image bg = _window.gameObject.AddComponent<Image>();
        bg.color = new Color(UiStyle.Panel.r, UiStyle.Panel.g, UiStyle.Panel.b, 0.92f);
        _window.gameObject.AddComponent<RectMask2D>();
        _group = _window.gameObject.AddComponent<CanvasGroup>();

        Body = NewRect("Body", _window);
        Body.anchorMin = Body.anchorMax = Body.pivot = new Vector2(0f, 1f);
        Body.anchoredPosition = Vector2.zero;

        UiStyle.Frame(_window, new Color(Green.r, Green.g, Green.b, 0.8f));
        _tag = UiStyle.Tag(_window, "info", Green);
        _tag.transform.parent.SetAsLastSibling();   // 标签压在内容上面

        _segLine = NewSegment("Tether", transform);
        _segEdge = NewSegment("OpenEdge", transform);
        _window.gameObject.SetActive(false);
    }

    public void SetTag(string text)
    {
        UiStyle.SetTag(_tag, text, Green);
        if (IsOpen) UiStyle.FitTag(_tag);
    }
    public float TagWidth => UiStyle.TagWidth(_tag);

    // 打开（重新打开会重放动画）。origin / topLeft：屏幕坐标。
    public void Open(Vector2 originScreen, Vector2 topLeftScreen, Vector2 size)
    {
        OpenCanvas(ToCanvas(originScreen), ToCanvas(topLeftScreen), size, true);
    }

    // 同上，画布坐标；hasOrigin = false：没有起点，跳过斜线那一段。
    public void OpenCanvas(Vector2 origin, Vector2 topLeft, Vector2 size, bool hasOrigin = true)
    {
        _size = size;
        Body.sizeDelta = size;
        _window.gameObject.SetActive(true);
        UiStyle.FitTag(_tag);   // 标签是在没激活的时候建的，第一次量出来的宽度不对：激活后重新量
        _tl = ClampTopLeft(topLeft, size);
        _window.anchoredPosition = _tl;
        _origin = origin;
        _hasOrigin = hasOrigin && (_origin - _tl).sqrMagnitude > 16f;

        _t = 0f;
        _animating = true;
        _window.sizeDelta = new Vector2(size.x, 0f);
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _segLine.gameObject.SetActive(false);
        _segEdge.gameObject.SetActive(false);
        Tick(0f);
    }

    // 内容变了：改大小（不重放动画；左上角不动）。
    public void Resize(Vector2 size)
    {
        _size = size;
        Body.sizeDelta = size;
        if (!IsOpen) return;
        _tl = ClampTopLeft(_tl, size);
        _window.anchoredPosition = _tl;
        if (!_animating) _window.sizeDelta = size;
    }

    // 挪到新位置（画布坐标的左上角；不重放动画）。
    public void PlaceCanvas(Vector2 topLeft)
    {
        _tl = ClampTopLeft(topLeft, _size);
        if (IsOpen) _window.anchoredPosition = _tl;
    }

    // 整个浮窗的透明度（提示淡出用）。
    public void SetAlpha(float a)
    {
        _alpha = Mathf.Clamp01(a);
        if (!_animating) _group.alpha = _alpha;
    }

    public void Close()
    {
        _animating = false;
        _segLine.gameObject.SetActive(false);
        _segEdge.gameObject.SetActive(false);
        _window.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!IsOpen) return;
        if (_animating) { Tick(Time.unscaledDeltaTime); return; }
        if (_hasOrigin && KeepTether) SetSegment(_segLine, _origin, _window.anchoredPosition); // 连线一直连着左上角
    }

    void Tick(float dt)
    {
        _t += dt;
        float a = _hasOrigin ? Mathf.Max(0.0001f, lineTime) : 0f;   // 没有起点：跳过斜线这一段
        float b = Mathf.Max(0.0001f, edgeTime);
        float c = Mathf.Max(0.0001f, unfoldTime);
        float t = _t;
        Vector2 tl = _tl, tr = _tl + new Vector2(_size.x, 0f);

        if (t < a)
        {
            // ① 线从起点伸向窗口左上角
            SetSegment(_segLine, _origin, Vector2.Lerp(_origin, tl, EaseOut(t / a)));
            _segEdge.gameObject.SetActive(false);
            return;
        }
        t -= a;
        if (_hasOrigin) SetSegment(_segLine, _origin, tl);
        else _segLine.gameObject.SetActive(false);
        if (t < b)
        {
            // ② 拐成水平，往右伸到右上角，变成窗口上沿
            SetSegment(_segEdge, tl, Vector2.Lerp(tl, tr, EaseInOut(t / b)));
            return;
        }
        t -= b;
        SetSegment(_segEdge, tl, tr);
        _group.alpha = _alpha;
        if (t < c)
        {
            // ③ 窗口从上沿往下展开
            _window.sizeDelta = new Vector2(_size.x, _size.y * EaseOut(t / c));
            return;
        }
        _animating = false;
        _window.sizeDelta = _size;
        _group.alpha = _alpha;
        _group.blocksRaycasts = Interactable;
        _segEdge.gameObject.SetActive(false);                                   // 上沿交给窗口自己的边框
        if (!KeepTether) _segLine.gameObject.SetActive(false);
    }

    // 屏幕坐标 → 画布坐标（画布左下角为原点）。
    public Vector2 ToCanvas(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, screen, null, out Vector2 local);
        return local + Vector2.Scale(_canvasRt.rect.size, _canvasRt.pivot);
    }

    Vector2 ClampTopLeft(Vector2 tl, Vector2 size)
    {
        Vector2 cs = _canvasRt.rect.size;
        tl.x = Mathf.Clamp(tl.x, 4f, Mathf.Max(4f, cs.x - size.x - 4f));
        tl.y = Mathf.Clamp(tl.y, Mathf.Min(size.y + 4f, cs.y - 4f), cs.y - 4f);
        return tl;
    }

    // 一个界面元素在屏幕上的四个角（左下、左上、右上、右下）。Overlay 画布的世界坐标就是屏幕像素。
    public static Vector3[] ScreenCorners(RectTransform rt)
    {
        Vector3[] c = new Vector3[4];
        if (rt != null) rt.GetWorldCorners(c);
        Canvas cv = rt != null ? rt.GetComponentInParent<Canvas>() : null;
        if (cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            Camera cam = cv.worldCamera;
            for (int i = 0; i < 4; i++) c[i] = RectTransformUtility.WorldToScreenPoint(cam, c[i]);
        }
        return c;
    }

    // 浮窗的 1 个界面单位 = 多少屏幕像素。
    public float UnitToPixels => _canvasRt != null && _canvasRt.localScale.x > 0f ? _canvasRt.localScale.x : 1f;

    // ============ 小工具 ============

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

    static RectTransform NewSegment(string name, Transform parent)
    {
        RectTransform seg = NewRect(name, parent);
        seg.anchorMin = seg.anchorMax = Vector2.zero;
        seg.pivot = new Vector2(0f, 0.5f);
        seg.sizeDelta = new Vector2(0f, LineWidth);
        Image core = seg.gameObject.AddComponent<Image>();
        core.color = new Color(Green.r, Green.g, Green.b, 0.9f);
        core.raycastTarget = false;
        RectTransform halo = NewRect("Glow", seg);                 // 线两侧很淡的一点光
        halo.anchorMin = new Vector2(0f, 0.5f);
        halo.anchorMax = new Vector2(1f, 0.5f);
        halo.offsetMin = new Vector2(-1f, -1.5f);
        halo.offsetMax = new Vector2(1f, 1.5f);
        Image hi = halo.gameObject.AddComponent<Image>();
        hi.color = new Color(Green.r, Green.g, Green.b, 0.1f);
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

    static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}

// 鼠标移进 / 移出一块界面时回调。这块界面要有一个能接收射线的 Graphic。
public class UiHoverZone : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public System.Action onEnter, onExit;
    public bool Hovered { get; private set; }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { Hovered = true; onEnter?.Invoke(); }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { Hovered = false; onExit?.Invoke(); }
    void OnDisable() { if (Hovered) { Hovered = false; onExit?.Invoke(); } }
}
