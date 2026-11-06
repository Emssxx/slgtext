using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================
// 任务汇报（右上角，天气浮窗下面，右边和天气浮窗对齐）：
//   · 一座建筑建成（ConstructionManager.OnSiteBuilt）→「build : done」+「雨水收集器 建成了 / 施工：田中、铃木」
//   · 营地目标完成（CampGoals.OnGoalDone）→「objective : done」+「目标完成：将营地用围墙围起来 / 负责人：……」
//   动画和 NPC 详细信息的锁定框一样：四个发光的角从中间往外展开到框的四个角；
//   背景板（深色底 + 淡绿细框）始终正好是四个角围出来的那一块——角伸到哪里，背景板就到哪里，里面的字也只露出这一块。
//   显示 showSeconds 秒后倒着播一遍：四个角往中间收回，背景板跟着一起缩小，缩到中间就消失。
//   最新的汇报永远在最上面，旧的往下滑（向下滚动）；每条都完整地放完自己的时间和收回动画，
//   来了新的也不会让旧的提前结束。
// 由 HudLayout.Start 调 Ensure() 创建。
// ============================================================================
public class HudToasts : MonoBehaviour
{
    public static HudToasts Instance { get; private set; }

    public float showSeconds = 6f;     // 完全展开后停留多久
    public float expandTime = 0.22f;   // 展开 / 收回各用多久
    public float width = 300f;
    public float topGap = 8f;          // 和天气浮窗之间
    public float gap = 8f;             // 汇报之间
    public float slideSpeed = 12f;     // 往下滑的快慢（越大越快）

    static readonly Color Green = UiStyle.Green;

    class Toast
    {
        public RectTransform root, bg, content;
        public RectTransform[] corners = new RectTransform[4];
        public Vector2 size;
        public float age;
        public float y = float.NaN;    // 现在的中心高度（画布坐标），往目标位置滑
    }

    RectTransform _canvasRt;
    readonly List<Toast> _toasts = new List<Toast>();   // 新的在前

    public static HudToasts Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("HudToasts").AddComponent<HudToasts>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        GameObject cgo = new GameObject("HudToastCanvas");
        cgo.transform.SetParent(transform, false);
        Canvas canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 72;
        CanvasScaler scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRt = (RectTransform)cgo.transform;
        ConstructionManager.OnSiteBuilt += OnBuilt;
        CampGoals.OnGoalDone += OnGoalDone;
    }

    void OnDestroy()
    {
        ConstructionManager.OnSiteBuilt -= OnBuilt;
        CampGoals.OnGoalDone -= OnGoalDone;
        if (Instance == this) Instance = null;
    }

    void OnBuilt(string site, List<string> crew)
    {
        string sub = crew != null && crew.Count > 0 ? LF("toast_crew", string.Join(LanguageGuard.Pick("、", "・"), crew)) : null;
        Show("build : done", LF("toast_built", site), sub);
    }

    void OnGoalDone(CampGoals.Goal g)
    {
        if (g == null) return;
        List<string> owners = CampGoals.Owners(g);
        string sub = owners == null ? LF("taskpanel_owner", L("taskpanel_owner_ai"))
                   : owners.Count > 0 ? LF("taskpanel_owner", string.Join(LanguageGuard.Pick("、", "・"), owners)) : null;
        Show("objective : done", LF("toast_goal_done", CampGoals.Text(g)), sub);
    }

    public void Show(string tag, string title, string sub)
    {
        Toast t = new Toast();
        t.root = NewRect("Toast", _canvasRt);
        t.root.anchorMin = t.root.anchorMax = Vector2.zero;            // 画布左下角为原点
        t.root.pivot = new Vector2(0.5f, 0.5f);

        // 背景板：大小跟着四个角走；RectMask2D 让里面的内容只露出背景板这一块
        t.bg = NewRect("Bg", t.root);
        t.bg.anchorMin = t.bg.anchorMax = t.bg.pivot = new Vector2(0.5f, 0.5f);
        Image bgImg = t.bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(UiStyle.Panel.r, UiStyle.Panel.g, UiStyle.Panel.b, 0.9f);
        bgImg.raycastTarget = false;
        t.bg.gameObject.AddComponent<RectMask2D>();
        UiStyle.Frame(t.bg, new Color(Green.r, Green.g, Green.b, 0.55f));

        // 内容：固定成完整大小、居中，被背景板裁着（背景板从中间长大，字也从中间往外露出来）
        t.content = NewRect("Content", t.bg);
        t.content.anchorMin = t.content.anchorMax = t.content.pivot = new Vector2(0.5f, 0.5f);
        TMP_Text tagText = UiStyle.Tag(t.content, tag, Green);
        TextMeshProUGUI text = NewText(t.content, 15f);
        text.text = title + (string.IsNullOrEmpty(sub) ? "" : "\n<size=85%><color=#9DB3A4>" + sub + "</color></size>");
        float tw = width - 24f;
        float h = Mathf.Ceil(text.GetPreferredValues(text.text, tw, 0f).y);
        if (h < 18f) h = string.IsNullOrEmpty(sub) ? 20f : 38f;
        RectTransform tr = text.rectTransform;
        tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0f, 1f);
        tr.anchoredPosition = new Vector2(12f, -(UiStyle.TagH + 6f));
        tr.sizeDelta = new Vector2(tw, h);
        UiStyle.FitTag(tagText);

        t.size = new Vector2(width, UiStyle.TagH + 6f + h + 12f);
        t.root.sizeDelta = t.size;
        t.content.sizeDelta = t.size;

        // 四个发光的角（和 NPC 锁定框同一张贴图）：轴心 = 角的拐点，翻转出另外三个角
        Texture2D tex = HoverInfo.SharedCornerTexture();
        Vector2[] flips = { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) }; // 左上 / 右上 / 左下 / 右下
        for (int i = 0; i < 4; i++)
        {
            RectTransform c = NewRect("Corner" + i, t.root);
            c.anchorMin = c.anchorMax = new Vector2(0.5f, 0.5f);
            c.sizeDelta = new Vector2(HoverInfo.CornerSize, HoverInfo.CornerSize);
            c.pivot = new Vector2(HoverInfo.CornerPad / HoverInfo.CornerSize, 1f - HoverInfo.CornerPad / HoverInfo.CornerSize);
            c.localScale = new Vector3(flips[i].x, flips[i].y, 1f);
            RawImage img = c.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.color = Green;
            img.raycastTarget = false;
            t.corners[i] = c;
        }

        _toasts.Insert(0, t);   // 最新的放最上面；旧的不提前结束，只是往下滑
        Animate(t, 0f);
        Restack(0f);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float life = expandTime * 2f + showSeconds;
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            Toast t = _toasts[i];
            t.age += dt;
            if (t.root == null || t.age >= life)
            {
                if (t.root != null) Destroy(t.root.gameObject);
                _toasts.RemoveAt(i);
                continue;
            }
            Animate(t, t.age);
        }
        Restack(dt);
    }

    // 展开程度 e（0 = 缩在中间，1 = 完全展开）：先展开（缓出），停留，再倒着播回去。
    float Expand(float age)
    {
        float k = Mathf.Max(0.0001f, expandTime);
        if (age < k) return EaseOut(age / k);
        if (age < k + showSeconds) return 1f;
        return EaseOut(1f - (age - k - showSeconds) / k);   // 倒着播：角往中间收
    }

    // 四个角和背景板一起：角伸到哪里，背景板就到哪里。
    void Animate(Toast t, float age)
    {
        float e = Expand(age);
        Vector2 half = t.size * 0.5f * e;
        t.corners[0].anchoredPosition = new Vector2(-half.x, half.y);
        t.corners[1].anchoredPosition = new Vector2(half.x, half.y);
        t.corners[2].anchoredPosition = new Vector2(-half.x, -half.y);
        t.corners[3].anchoredPosition = new Vector2(half.x, -half.y);
        t.bg.sizeDelta = half * 2f;
        bool visible = e > 0.01f;
        if (t.bg.gameObject.activeSelf != visible) t.bg.gameObject.SetActive(visible);
        foreach (RectTransform c in t.corners) if (c.gameObject.activeSelf != visible) c.gameObject.SetActive(visible);
    }

    // 从上往下排：最新的在最上面（贴着天气浮窗下沿），旧的依次往下；位置变了就平滑地滑过去（向下滚动）。
    // 一条汇报收回消失之前一直占着自己的位置，消失后下面的再往上补。
    void Restack(float dt)
    {
        if (_toasts.Count == 0 || _canvasRt == null) return;
        Vector2 cs = _canvasRt.rect.size;
        float xMax = cs.x - WeatherPanel.RightMargin;
        float top = cs.y - WeatherPanel.BottomFromTop - topGap;
        float k = dt > 0f ? 1f - Mathf.Exp(-slideSpeed * dt) : 1f;
        foreach (Toast t in _toasts)
        {
            if (t.root == null) continue;
            float target = top - t.size.y * 0.5f;
            t.y = float.IsNaN(t.y) ? target : Mathf.Lerp(t.y, target, k);
            t.root.anchoredPosition = new Vector2(xMax - width * 0.5f, t.y);
            top -= t.size.y + gap;
        }
    }

    static TextMeshProUGUI NewText(Transform parent, float size)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = GameFonts.Find();
        if (f != null) t.font = f;
        t.fontSize = size;
        t.color = UiStyle.TextMain;
        t.richText = true;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;
}
