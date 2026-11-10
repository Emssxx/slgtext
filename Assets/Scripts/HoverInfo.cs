using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;

// 鼠标悬停信息框：
//   NPC：鼠标停在 NPC 上一小会儿，弹出锁定框（姓名、职业、正在做什么、生命 / 饥饿 / 体力 ……）。
//   建筑 / 资源的悬停信息框已经去掉（会在鼠标没指着终端机时也弹出来）。
// 左下角的位置浮窗：一直显示，淡绿标签「location」+ 鼠标指着的这一格的地形、资源（内容由 CellInfo 的各个提供者写，不显示 xy 坐标）。
//   不用点、不用停，鼠标移到哪一格就显示哪一格。
// 点开某个 NPC 的对话窗口后，鼠标没指着别的 NPC 时，他的锁定框一直显示（窗口关掉才消失）。
// NPC 的锁定框：四个角框住 NPC（淡绿色微光，和天数数字同色），信息写在左上角外面；出现时四个角从中间展开（expandTime 秒）。
//   和生命值联动：生命从 100 往 0 掉，颜色 绿 → 黄 → 红 平滑过渡，故障抖动越来越大（有上限）、电流闪烁越来越频繁。
//   NPC 死亡瞬间：不用悬停也自动弹出锁定框（盖过当前悬停），四条线从四个角往中心延伸（deathCrossTime 秒），
//   然后每行文字从左到右划掉（默认一共 0.8 秒播完），停留 deathHoldTime 秒后淡出；文字后面垫一块半透明黑色底板。悬停在已死亡的 NPC 上直接显示划掉后的样子。
// 鼠标在 UI 上时不显示锁定框（坐标浮窗保留最后一格）。用不受暂停影响的时间（建造模式会暂停游戏）。
// 由 HudLayout 调 HoverInfo.Ensure() 创建，自带一个最上层的 Canvas。
public class HoverInfo : MonoBehaviour
{
    public static HoverInfo Instance { get; private set; }

    [Tooltip("鼠标停多久才弹出（秒）")]
    public float delay = 0.25f;
    [Tooltip("信息框最宽多少（1920×1080 下的像素）")]
    public float maxWidth = 300f;

    [Header("左下角位置浮窗（一直显示）")]
    [Tooltip("离屏幕左下角多远（1920 宽的画布像素）")]
    public Vector2 cellPanelMargin = new Vector2(18f, 18f);
    [Tooltip("浮窗宽度")]
    public float cellPanelWidth = 280f;
    [Tooltip("垫多暗的底（0 = 没有底板；地面太亮看不清字时调高，比如 0.5）")]
    [Range(0f, 1f)] public float cellPanelBackdrop = 0f;

    [Header("NPC 锁定框")]
    [Tooltip("四个角从中间展开用多久（秒）")]
    public float expandTime = 0.1f;
    [Tooltip("四个角离 NPC 图片边缘留多少（像素）")]
    public float framePadding = 6f;
    [Tooltip("文字微光（关掉 = 纯色文字）")]
    public bool textGlow = true;

    [Header("NPC 锁定框：生命值联动")]
    public Color hpMidColor = new Color(1f, 0.9f, 0.35f, 1f);    // 一半血：黄（比之前更饱和，浅色地面上也看得清）
    public Color hpLowColor = new Color(1f, 0.38f, 0.32f, 1f);   // 没血：红
    [Tooltip("生命为 0 时最大抖动幅度（像素）")]
    public float maxJitter = 3f;
    [Tooltip("生命为 0 时每秒电流闪烁次数")]
    public float maxFlickerRate = 7f;

    [Header("NPC 锁定框：死亡")]
    [Tooltip("四条线从四个角延伸到中心用多久（秒）")]
    public float deathCrossTime = 0.3f;
    [Tooltip("每行文字被划掉用多久（秒）")]
    public float deathStrikeTime = 0.3f;
    [Tooltip("下一行比上一行晚多久开始划（秒）。默认值下十字 + 五行划线正好 0.8 秒播完")]
    public float deathStrikeStagger = 0.05f;
    [Tooltip("动画播完后停留多久（秒）")]
    public float deathHoldTime = 2f;
    [Tooltip("最后淡出用多久（秒）")]
    public float deathFadeTime = 0.4f;

    // 死亡锁定框一共显示多久：动画（十字 + 五行划线）+ 停留 + 淡出
    float DeathShowTime => deathCrossTime + deathStrikeStagger * 4f + deathStrikeTime + deathHoldTime + deathFadeTime;

    [Header("NPC 锁定框：文字底板")]
    public Color textBgColor = new Color(0.04f, 0.045f, 0.05f, 0.82f);
    public Vector2 textBgPadding = new Vector2(6f, 4f);

    Camera _cam;
    RectTransform _canvasRt;

    GameObject _target;      // 正在悬停的 NPC
    NpcView _npc;
    float _hover, _refresh;

    // —— NPC 锁定框 ——
    RectTransform _npcRoot, _npcTextRt;
    readonly RectTransform[] _corners = new RectTransform[4]; // 左上 / 右上 / 左下 / 右下
    readonly RawImage[] _cornerImgs = new RawImage[4];
    TextMeshProUGUI _npcText;
    bool _npcShown;
    float _npcAnim;
    float _hpTarget = 1f, _hpShown = 1f;        // 生命比例（目标 / 显示用，平滑靠近）
    float _glitchTimer, _flickTimer, _flickLeft, _flickAlpha = 1f, _flickBright;
    readonly Vector2[] _cornerJitter = new Vector2[4];
    Vector2 _textJitter;
    Material _npcMat;

    // —— 死亡效果 ——
    readonly RectTransform[] _crossLines = new RectTransform[4];
    readonly RawImage[] _crossImgs = new RawImage[4];
    readonly List<RectTransform> _strikes = new List<RectTransform>();
    readonly List<RawImage> _strikeImgs = new List<RawImage>();
    bool _deathFx;            // 这次显示要画死亡效果（十字线 + 划线）
    float _deathFxT;          // 死亡效果的时间轴（悬停已死亡的 NPC 时直接给一个很大的值 = 画完的样子）
    string _deathId;          // 正在播放死亡效果的 NPC（不用悬停）
    GameObject _deathGo;
    float _deathT = -1f;      // < 0 = 没有在播放
    Bounds _lastBounds;       // NPC 物体淡出销毁后，框停在最后的位置
    readonly HashSet<string> _knownDead = new HashSet<string>();
    bool _deadInit;
    float _deathCheck;
    Color _fxColor = Color.white;
    RectTransform _npcBg;
    Image _npcBgImg;
    Image _npcFrame;                          // 识别框的细边（UiStyle.Frame）
    TMP_Text _npcTag, _cellTag;               // 框左上角的标签（UiStyle.Tag）
    float _fxAlpha = 1f;

    // —— 左下角位置浮窗 ——
    RectTransform _cellRoot, _cellTextRt;
    TextMeshProUGUI _cellText;
    CanvasGroup _cellGroup;
    Image _cellBackdrop;
    Vector2 _cellTarget, _cellSize;   // 目标大小 / 现在显示的大小（平滑靠近）
    float _cellAnim, _cellRefresh;
    Vector2Int _cellCur = new Vector2Int(int.MinValue, int.MinValue);   // 最后指着的格子（鼠标离开地图后保留）

    public static HoverInfo Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("HoverInfo").AddComponent<HoverInfo>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void BuildUI()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 70; // 在建造菜单（50）上面
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;
        _canvasRt = (RectTransform)transform;

        TMP_FontAsset font = GameFonts.Find();
        BuildNpcFrame(font);
        BuildCellPanel(font);
    }

    // NPC 锁定框：四个发光的角 + 左上角的文字（透明底、淡绿色微光）。
    void BuildNpcFrame(TMP_FontAsset font)
    {
        _npcRoot = NewRect("NpcFrame", transform);
        _npcRoot.anchorMin = _npcRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _npcRoot.sizeDelta = Vector2.zero;
        Texture2D tex = CornerTexture();
        Vector2[] flips = { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) };
        for (int i = 0; i < 4; i++)
        {
            RectTransform c = NewRect("Corner" + i, _npcRoot);
            c.anchorMin = c.anchorMax = new Vector2(0.5f, 0.5f);
            c.sizeDelta = new Vector2(CornerSize, CornerSize);
            c.pivot = new Vector2(CornerPad / CornerSize, 1f - CornerPad / CornerSize); // 轴心 = 角的拐点
            c.localScale = new Vector3(flips[i].x, flips[i].y, 1f);                      // 翻转出另外三个角
            RawImage img = c.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            _corners[i] = c;
            _cornerImgs[i] = img;
        }

        // 死亡时的四条线（从中心到四个角），和角框同样的发光线条
        for (int i = 0; i < 4; i++)
        {
            RectTransform l = NewRect("DeathLine" + i, _npcRoot);
            l.anchorMin = l.anchorMax = new Vector2(0.5f, 0.5f);
            l.pivot = new Vector2(0f, 0.5f);
            RawImage li = l.gameObject.AddComponent<RawImage>();
            li.texture = LineTexture();
            li.raycastTarget = false;
            l.gameObject.SetActive(false);
            _crossLines[i] = l;
            _crossImgs[i] = li;
        }

        // 文字底板（识别框样式：深色半透明底 + 细边框 + 左上角标签「human : online」），在文字下面。
        // 边框和标签的颜色跟着生命值变（绿 → 黄 → 红），也跟着闪烁
        _npcBg = NewRect("TextBg", _npcRoot);
        _npcBg.anchorMin = _npcBg.anchorMax = new Vector2(0.5f, 0.5f);
        _npcBgImg = _npcBg.gameObject.AddComponent<Image>();
        _npcBgImg.color = textBgColor;
        _npcBgImg.raycastTarget = false;
        _npcFrame = UiStyle.Frame(_npcBg, HudGlow.Green);
        _npcTag = UiStyle.Tag(_npcBg, "human : online", HudGlow.Green);

        _npcTextRt = NewRect("Text", _npcRoot);
        _npcTextRt.anchorMin = _npcTextRt.anchorMax = new Vector2(0.5f, 0.5f);
        _npcTextRt.pivot = new Vector2(0f, 0f);
        _npcText = _npcTextRt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) _npcText.font = font;
        _npcText.fontSize = 17f;
        _npcText.color = HudGlow.Green;
        _npcText.alignment = TextAlignmentOptions.BottomLeft;
        _npcText.textWrappingMode = TextWrappingModes.Normal;
        _npcText.raycastTarget = false;
        // 微光：TMP 的 Underlay（柔和的同色阴影，不偏移）。
        // 改完材质要让 TMP 重算每个字的网格边距，否则光晕被字块边缘切掉，变成一块块半透明底色。
        Material m = textGlow ? _npcText.fontMaterial : null;
        if (m != null)
        {
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(HudGlow.GreenGlow.r, HudGlow.GreenGlow.g, HudGlow.GreenGlow.b, 0.4f));
            m.SetFloat("_UnderlayOffsetX", 0f);
            m.SetFloat("_UnderlayOffsetY", 0f);
            m.SetFloat("_UnderlayDilate", 0f);
            m.SetFloat("_UnderlaySoftness", 0.45f);
            _npcText.extraPadding = true;
            _npcText.UpdateMeshPadding();
            _npcMat = m;
        }
        _npcRoot.gameObject.SetActive(false);
    }

    public const float CornerSize = 32f, CornerPad = 9f, CornerArm = 14f, CornerThick = 2.5f;

    // 角框贴图给别的界面用（建筑完成提示 HudToasts 也是四个角展开 / 收回）。
    public static Texture2D SharedCornerTexture() => CornerTexture();

    // 一个左上角的角框（┌），四周留出发光的位置；颜色和天数数字一样（淡绿 + 微光）。
    static Texture2D _cornerTex;
    static Texture2D CornerTexture()
    {
        if (_cornerTex != null) return _cornerTex;
        const int S = 2;                                        // 2 倍分辨率
        int N = (int)(CornerSize * S);
        float pad = CornerPad * S, arm = CornerArm * S, th = CornerThick * S;
        float top = N - pad;                                    // 贴图 y 向上：拐点在 (pad, N - pad)
        float[] cov = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float h = Overlap(x, x + 1, pad, pad + arm) * Overlap(y, y + 1, top - th, top);   // 横
                float v = Overlap(x, x + 1, pad, pad + th) * Overlap(y, y + 1, top - arm, top);   // 竖
                cov[y * N + x] = Mathf.Min(1f, Mathf.Max(h, v));
            }
        // 画成白色，用 RawImage.color 上色（生命值联动要随时变色）；光晕稍暗一点，染色后显得更饱和
        SegmentPainter.Style st = new SegmentPainter.Style
        {
            r = 1f, g = 1f, b = 1f, gr = 0.85f, gg = 0.85f, gb = 0.85f,
            glow = 0.85f, glowRadius = 6f * S,
        };
        byte[] px = SegmentPainter.Compose(cov, null, N, N, st);
        Color32[] cols = new Color32[N * N];
        for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]);
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.name = "HoverCorner";
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        t.SetPixels32(cols);
        t.Apply(false);
        return _cornerTex = t;
    }

    // 一条横向的发光线（宽度可以随便拉伸）：中间实线 CornerThick 粗，上下留发光。
    static Texture2D _lineTex;
    static Texture2D LineTexture()
    {
        if (_lineTex != null) return _lineTex;
        const int S = 2, W = 4;
        int H = (int)((CornerThick + CornerPad * 2f) * S);
        float c = H * 0.5f, half = CornerThick * S * 0.5f;
        float[] cov = new float[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = Overlap(y, y + 1, c - half, c + half);
            for (int x = 0; x < W; x++) cov[y * W + x] = v;
        }
        SegmentPainter.Style st = new SegmentPainter.Style { r = 1f, g = 1f, b = 1f, gr = 0.85f, gg = 0.85f, gb = 0.85f, glow = 0.85f, glowRadius = 6f * S };
        byte[] px = SegmentPainter.Compose(cov, null, W, H, st);
        Color32[] cols = new Color32[W * H];
        for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]);
        Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, false);
        t.name = "HoverLine";
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        t.SetPixels32(cols);
        t.Apply(false);
        return _lineTex = t;
    }

    static float LineHeight => CornerThick + CornerPad * 2f;

    static float Overlap(float a0, float a1, float b0, float b1) => Mathf.Clamp01(Mathf.Min(a1, b1) - Mathf.Max(a0, b0));

    // NPC 文字：去掉原来各行的颜色，统一淡绿色；原来的灰色副信息改成稍淡一点（保留层次）。
    static readonly System.Text.RegularExpressions.Regex ColorOpen = new System.Text.RegularExpressions.Regex("<color=#?[0-9A-Fa-f]{6,8}>");
    static string GreenText(string s)
    {
        s = ColorOpen.Replace(s, "<alpha=#B8>");
        return s.Replace("</color>", "<alpha=#FF>");
    }

    void SetNpcText(string text, float hp01)
    {
        _hpTarget = Mathf.Clamp01(hp01);
        _npcText.text = GreenText(text);
        Vector2 pref = _npcText.GetPreferredValues(_npcText.text, maxWidth, 0f);
        _npcTextRt.sizeDelta = new Vector2(Mathf.Min(pref.x, maxWidth) + 2f, pref.y);
        if (!_npcShown)
        {
            _npcShown = true;
            _npcAnim = 0f;
            _hpShown = _hpTarget;   // 刚弹出：直接用当前生命，不从满血渐变
            _flickLeft = 0f; _flickAlpha = 1f; _flickBright = 0f;
            _npcRoot.gameObject.SetActive(true);
        }
    }

    // 四个角框住 NPC 图片（屏幕上的范围），文字放在左上角外面；刚出现时从中间展开。
    void PlaceNpc(GameObject go)
    {
        if (!_npcShown || _cam == null) return;
        if (go != null)
        {
            SpriteRenderer sr = go.GetComponentInChildren<SpriteRenderer>();
            _lastBounds = sr != null ? sr.bounds : new Bounds(go.transform.position, new Vector3(0.6f, 0.9f, 0f));
        }
        Bounds b = _lastBounds; // 物体已经销毁（死亡淡出后）：停在最后的位置
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, _cam.WorldToScreenPoint(b.min), null, out Vector2 lo)) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, _cam.WorldToScreenPoint(b.max), null, out Vector2 hi)) return;
        lo -= new Vector2(framePadding, framePadding);
        hi += new Vector2(framePadding, framePadding);
        Vector2 center = (lo + hi) * 0.5f;

        _npcAnim = Mathf.Min(1f, _npcAnim + (expandTime > 0f ? Time.unscaledDeltaTime / expandTime : 1f));
        float e = 1f - (1f - _npcAnim) * (1f - _npcAnim) * (1f - _npcAnim); // 缓出：先快后慢
        Vector2[] pts = { new Vector2(lo.x, hi.y), new Vector2(hi.x, hi.y), new Vector2(lo.x, lo.y), new Vector2(hi.x, lo.y) };
        UpdateHpFx(Time.unscaledDeltaTime);
        for (int i = 0; i < 4; i++) _corners[i].anchoredPosition = Vector2.LerpUnclamped(center, pts[i], e) + _cornerJitter[i];

        // 文字：左上角外面；顶上放不下就放到框的下面
        Rect r = _canvasRt.rect;
        Vector2 size = _npcTextRt.sizeDelta;
        float left = Mathf.Lerp(center.x, lo.x, e);
        float topY = Mathf.Lerp(center.y, hi.y, e);
        float botY = Mathf.Lerp(center.y, lo.y, e);
        float th = UiStyle.TagH;                     // 底板上面多出来的标签高度
        bool below = hi.y + 4f + size.y + textBgPadding.y * 2f + th > r.yMax;
        _npcTextRt.pivot = new Vector2(0f, below ? 1f : 0f);
        _npcText.alignment = below ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.BottomLeft;
        float x = Mathf.Clamp(left, r.xMin + 4f + textBgPadding.x, r.xMax - size.x - 4f - textBgPadding.x);
        float gapY = 4f + textBgPadding.y; // 留出底板的边，别压到角框
        _npcTextRt.anchoredPosition = new Vector2(x, below ? botY - gapY - th : topY + gapY) + _textJitter;
        _npcText.alpha = e * _flickAlpha * _fxAlpha;

        // 底板跟着文字（含抖动），四周多留一点；随展开 / 淡出变透明，不跟着闪烁
        _npcBg.pivot = _npcTextRt.pivot;
        _npcBg.anchoredPosition = _npcTextRt.anchoredPosition + new Vector2(-textBgPadding.x, below ? textBgPadding.y + th : -textBgPadding.y);
        _npcBg.sizeDelta = new Vector2(Mathf.Max(size.x + textBgPadding.x * 2f, UiStyle.TagWidth(_npcTag) + 10f),
                                       size.y + textBgPadding.y * 2f + th);
        _npcBgImg.color = new Color(textBgColor.r, textBgColor.g, textBgColor.b, textBgColor.a * e * _fxAlpha);

        // 边框和标签：生命值的颜色，跟着闪烁
        float fa = e * _fxAlpha * _flickAlpha;
        _npcFrame.color = new Color(_fxColor.r, _fxColor.g, _fxColor.b, fa);
        UiStyle.SetTag(_npcTag, NpcTagText(), new Color(_fxColor.r, _fxColor.g, _fxColor.b, fa));
        _npcTag.alpha = fa;

        UpdateDeathFx(center);
    }

    // 锁定框标签：生命 > 75% 在线，> 20% 警告，再低是危险；死了是 lost。
    string NpcTagText()
    {
        if (_deathFx) return "human : lost";
        if (_hpShown > 0.75f) return "human : online";
        return _hpShown > 0.2f ? "human : warning" : "human : critical";
    }

    // 死亡效果：① 四条线从四个角延伸到中心（deathCrossTime）；② 之后每行文字从左到右划一条线（一行比一行晚一点）。
    void UpdateDeathFx(Vector2 center)
    {
        if (!_deathFx)
        {
            foreach (RectTransform l in _crossLines) if (l.gameObject.activeSelf) l.gameObject.SetActive(false);
            foreach (RectTransform l in _strikes) if (l.gameObject.activeSelf) l.gameObject.SetActive(false);
            return;
        }
        float a = _flickAlpha * _fxAlpha;
        Color lineCol = new Color(_fxColor.r, _fxColor.g, _fxColor.b, a);

        // ① 十字线：从四个角往中心延伸（跟着角的抖动走）
        float p = deathCrossTime > 0f ? Mathf.Clamp01(_deathFxT / deathCrossTime) : 1f;
        float ep = 1f - (1f - p) * (1f - p);
        for (int i = 0; i < 4; i++)
        {
            RectTransform l = _crossLines[i];
            Vector2 from = _corners[i].anchoredPosition;
            Vector2 d = center - from;
            float len = d.magnitude * ep;
            bool on = len > 0.5f;
            if (l.gameObject.activeSelf != on) l.gameObject.SetActive(on);
            if (!on) continue;
            l.anchoredPosition = from;
            l.sizeDelta = new Vector2(len, LineHeight);
            l.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            _crossImgs[i].color = lineCol;
        }

        // ② 划线：沿每行文字的中线，从左到右
        TMP_TextInfo info = _npcText.textInfo;
        int lines = info != null ? info.lineCount : 0;
        int k = 0;
        for (int i = 0; i < lines && i < info.lineInfo.Length; i++)
        {
            TMP_LineInfo li = info.lineInfo[i];
            if (li.visibleCharacterCount <= 0) continue;
            float x0 = li.lineExtents.min.x, x1 = li.lineExtents.max.x;
            float yc = (li.lineExtents.min.y + li.lineExtents.max.y) * 0.5f;
            float start = deathCrossTime + k * deathStrikeStagger;
            float q = deathStrikeTime > 0f ? Mathf.Clamp01((_deathFxT - start) / deathStrikeTime) : 1f;
            float eq = 1f - (1f - q) * (1f - q);
            RectTransform st = Strike(k);
            bool on = eq > 0.001f;
            if (st.gameObject.activeSelf != on) st.gameObject.SetActive(on);
            k++;
            if (!on) continue;
            st.anchorMin = st.anchorMax = _npcTextRt.pivot;   // TMP 的坐标以文字框的轴心为原点
            st.anchoredPosition = new Vector2(x0 - 2f, yc) + Vector2.zero;
            st.sizeDelta = new Vector2((x1 - x0 + 4f) * eq, LineHeight);
            _strikeImgs[k - 1].color = new Color(_fxColor.r, _fxColor.g, _fxColor.b, _npcText.alpha);
        }
        for (int i = k; i < _strikes.Count; i++) if (_strikes[i].gameObject.activeSelf) _strikes[i].gameObject.SetActive(false);
    }

    RectTransform Strike(int i)
    {
        while (_strikes.Count <= i)
        {
            RectTransform l = NewRect("Strike" + _strikes.Count, _npcTextRt);
            l.pivot = new Vector2(0f, 0.5f);
            RawImage img = l.gameObject.AddComponent<RawImage>();
            img.texture = LineTexture();
            img.raycastTarget = false;
            _strikes.Add(l);
            _strikeImgs.Add(img);
        }
        return _strikes[i];
    }

    // 生命值联动：颜色（绿→黄→红）、故障抖动、电流闪烁。danger = 0（满血）… 1（没血）。
    void UpdateHpFx(float dt)
    {
        _hpShown = Mathf.MoveTowards(_hpShown, _hpTarget, dt * 0.8f); // 掉血 / 回血时颜色平滑变化
        float d = 1f - _hpShown;

        // 颜色：0 → 0.5 绿到黄，0.5 → 1 黄到红（用平滑曲线，过渡不生硬）
        Color col = d < 0.5f ? Color.Lerp(HudGlow.Green, hpMidColor, Mathf.SmoothStep(0f, 1f, d * 2f))
                             : Color.Lerp(hpMidColor, hpLowColor, Mathf.SmoothStep(0f, 1f, (d - 0.5f) * 2f));

        // 电流闪烁：频率随危险程度升高；闪的时候大多是暗一下，偶尔是一下发白的"电弧"
        float rate = maxFlickerRate * Mathf.Pow(d, 1.3f);
        if (_flickLeft > 0f)
        {
            _flickLeft -= dt;
            if (_flickLeft <= 0f) { _flickAlpha = 1f; _flickBright = 0f; }
        }
        else if (rate > 0.01f)
        {
            _flickTimer -= dt;
            if (_flickTimer <= 0f)
            {
                _flickTimer = Random.Range(0.4f, 1.6f) / rate;      // 下一次闪烁（不规则）
                _flickLeft = Random.Range(0.03f, 0.09f);
                if (Random.value < 0.7f) { _flickAlpha = Random.Range(0.15f, 0.55f); _flickBright = 0f; }
                else { _flickAlpha = 1f; _flickBright = Random.Range(0.45f, 0.8f); } // 一下变黑的"断电"闪
                if (Random.value < 0.25f * d) _flickTimer = Random.Range(0.04f, 0.1f); // 危险时常常连闪
            }
        }
        Color baseCol = col;
        col = Color.Lerp(col, Color.black, _flickBright);

        // 故障抖动：幅度随危险程度平滑变大（最大 maxJitter 像素）；隔一小段时间跳一次位置，横向为主，像信号不稳
        float amp = maxJitter * d * d;
        _glitchTimer -= dt;
        if (_glitchTimer <= 0f)
        {
            _glitchTimer = Mathf.Lerp(0.12f, 0.035f, d);
            bool jump = amp > 0.05f && Random.value < Mathf.Lerp(0.25f, 0.9f, d);
            for (int i = 0; i < 4; i++)
                _cornerJitter[i] = jump ? new Vector2(Random.Range(-amp, amp), Random.Range(-amp, amp) * 0.4f) : Vector2.zero;
            _textJitter = jump && Random.value < 0.6f ? new Vector2(Random.Range(-amp, amp) * 1.3f, 0f) : Vector2.zero;
        }
        if (_flickLeft > 0f && _flickAlpha < 1f) _textJitter.x += Random.Range(-amp, amp); // 闪烁那一下顺便错位

        float a = _flickAlpha * _fxAlpha;
        _fxColor = col;
        foreach (RawImage img in _cornerImgs) img.color = new Color(col.r, col.g, col.b, a);
        _npcText.color = new Color(col.r, col.g, col.b, 1f); // 透明度由 PlaceNpc 里的 alpha 控制
        if (_npcMat != null)
        {
            // 绿色时是同色微光；变黄 / 变红后微光逐渐换成深色柔边，浅色地面上也看得清
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.45f, d));
            Color glow = new Color(baseCol.r * 0.75f, baseCol.g * 0.75f, baseCol.b * 0.75f, 0.4f);
            Color shade = new Color(0.05f, 0.03f, 0.02f, 0.65f);
            Color u = Color.Lerp(glow, shade, k);
            _npcMat.SetColor("_UnderlayColor", new Color(u.r, u.g, u.b, u.a * a));
        }
    }

    void HideNpc()
    {
        _deathFx = false;
        _fxAlpha = 1f;
        if (!_npcShown) return;
        _npcShown = false;
        _npcRoot.gameObject.SetActive(false);
    }

    void Update()
    {
        if (_cam == null) _cam = Camera.main;
        GameObject t = null;
        NpcView npc = null;
        bool onMap = false;
        Vector2 sp = Vector2.zero;
        Vector3 wp = Vector3.zero;
        if (_cam != null && Mouse.current != null && !PointerOverUI())
        {
            sp = Mouse.current.position.ReadValue();
            wp = _cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -_cam.transform.position.z));
            wp.z = 0f;
            onMap = true;
            FindTarget(wp, out t, out npc);
        }
        // 正在和某个 NPC 对话：鼠标没指着别的东西时，他的锁定框一直留着（对话窗口的连线就接在框的左上角）
        if (t == null)
        {
            NpcView pinned = ChatPinnedNpc();
            if (pinned != null) { t = pinned.gameObject; npc = pinned; }
        }
        UpdateTarget(t, npc);
        UpdateCell(onMap, wp);
    }

    // 对话窗口开着的那个 NPC（没开 / 他已经不在场景里时返回 null）。
    static NpcView ChatPinnedNpc()
    {
        NpcChatWindow w = NpcChatWindow.Instance;
        if (w == null || !w.IsOpen || string.IsNullOrEmpty(w.CurrentNpcId)) return null;
        NpcMover m = NpcView.GetMover(w.CurrentNpcId);
        return m != null ? m.GetComponent<NpcView>() : null;
    }

    // NPC 的锁定框。
    void UpdateTarget(GameObject t, NpcView npc)
    {
        CheckDeaths();
        if (_deathT >= 0f) { UpdateDeathPin(); return; } // 正在播放死亡效果：盖过悬停

        if (t != _target)
        {
            _target = t;
            _npc = npc;
            _hover = 0f;
            Hide();
        }
        if (_target == null || _npc == null) return;

        _hover += Time.unscaledDeltaTime;
        if (_hover < delay) return;

        _refresh -= Time.unscaledDeltaTime;
        if (!_npcShown || _refresh <= 0f)
        {
            _refresh = 0.25f;
            string text = NpcText(_npc);
            if (string.IsNullOrEmpty(text)) { Hide(); return; }
            SetNpcText(text, NpcHp01(_npc));
            NpcData nd = FindNpc(_npc.npcId);
            if (nd != null && nd.IsDead && !_deathFx) { _deathFx = true; _deathFxT = 99f; } // 已死亡：直接画划掉后的样子
        }
        PlaceNpc(_target);
    }

    // 每 0.2 秒看一次有没有 NPC 刚死（开局就已经死的不算）。
    void CheckDeaths()
    {
        _deathCheck -= Time.unscaledDeltaTime;
        if (_deathCheck > 0f) return;
        _deathCheck = 0.2f;
        if (WorldManager.Instance == null || WorldManager.Instance.world == null || WorldManager.Instance.world.npcs == null) return;
        foreach (NpcData n in WorldManager.Instance.world.npcs)
        {
            if (n == null || !n.IsDead || _knownDead.Contains(n.id)) continue;
            _knownDead.Add(n.id);
            if (_deadInit) StartDeathPin(n.id);
        }
        _deadInit = true;
    }

    void StartDeathPin(string id)
    {
        NpcMover mover = NpcView.GetMover(id);
        if (mover == null) return; // 场景里没有这个 NPC 的物体：不弹
        Hide();
        _target = null;
        _npc = null;
        _deathId = id;
        _deathGo = mover.gameObject;
        _deathT = 0f;
        _refresh = 0f;
    }

    // 死亡锁定框：跟着 NPC（淡出销毁后停在最后的位置），播完十字线和划线，到时间淡出。
    void UpdateDeathPin()
    {
        _deathT += Time.unscaledDeltaTime;
        if (_deathT >= DeathShowTime)
        {
            _deathT = -1f;
            _deathId = null;
            _deathGo = null;
            HideNpc();
            return;
        }
        _refresh -= Time.unscaledDeltaTime;
        if (!_npcShown || _refresh <= 0f)
        {
            _refresh = 0.25f;
            string text = NpcTextById(_deathId);
            if (string.IsNullOrEmpty(text)) { _deathT = -1f; HideNpc(); return; }
            SetNpcText(text, 0f);
        }
        _deathFx = true;
        _deathFxT = _deathT;
        _fxAlpha = deathFadeTime > 0f ? Mathf.Clamp01((DeathShowTime - _deathT) / deathFadeTime) : 1f; // 最后淡出
        PlaceNpc(_deathGo != null ? _deathGo : null);
    }

    // ============ 左下角位置浮窗 ============
    // 一直显示：淡绿标签「location」+ 这一格的信息（CellInfo：地形、资源……，不显示 xy 坐标）。
    // 不用点、不用停：鼠标指到哪一格就立刻显示哪一格；鼠标移到界面上 / 地图外时保留最后一格。
    // 内容行数变了，高度平滑地跟过去（下沿贴着屏幕底，往上长）；游戏开始时淡入一次。

    const float CellPad = 8f;           // 文字离浮窗边缘多远（垫了暗底时才看得出来）
    const float CellGap = 5f;           // 标签和文字之间
    const float CellExpandTime = 0.25f;

    void BuildCellPanel(TMP_FontAsset font)
    {
        _cellRoot = NewRect("CellPanel", transform);
        _cellRoot.anchorMin = _cellRoot.anchorMax = _cellRoot.pivot = Vector2.zero;   // 画布左下角

        // 可选的暗底（默认 0 = 没有底板）
        RectTransform bg = NewRect("Backdrop", _cellRoot);
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;
        _cellBackdrop = bg.gameObject.AddComponent<Image>();
        _cellBackdrop.color = new Color(textBgColor.r, textBgColor.g, textBgColor.b, 0f);
        _cellBackdrop.raycastTarget = false;
        bg.gameObject.SetActive(false);

        // 标签 + 文字：贴着浮窗的左上角（往上长时跟着上沿走），开始时淡入
        RectTransform content = NewRect("Content", _cellRoot);
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = new Vector2(CellPad, -CellPad);
        content.sizeDelta = new Vector2(cellPanelWidth - CellPad * 2f, UiStyle.TagH);
        _cellGroup = content.gameObject.AddComponent<CanvasGroup>();
        _cellGroup.blocksRaycasts = false;
        _cellGroup.interactable = false;
        _cellTag = UiStyle.Tag(content, "location", HudGlow.Green);

        _cellTextRt = NewRect("Text", content);
        _cellTextRt.anchorMin = _cellTextRt.anchorMax = _cellTextRt.pivot = new Vector2(0f, 1f);
        _cellTextRt.anchoredPosition = new Vector2(0f, -(UiStyle.TagH + CellGap));
        _cellText = _cellTextRt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) _cellText.font = font;
        _cellText.fontSize = 15f;
        _cellText.color = HudGlow.Green;
        _cellText.richText = true;
        _cellText.alignment = TextAlignmentOptions.TopLeft;
        _cellText.textWrappingMode = TextWrappingModes.Normal;
        _cellText.raycastTarget = false;
        // 没有底板：给字加一圈柔和的暗边（TMP 的 Underlay），亮的地面上也看得清
        Material m = _cellText.fontMaterial;
        if (m != null)
        {
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.75f));
            m.SetFloat("_UnderlayOffsetX", 0f);
            m.SetFloat("_UnderlayOffsetY", 0f);
            m.SetFloat("_UnderlayDilate", 0.2f);
            m.SetFloat("_UnderlaySoftness", 0.5f);
            _cellText.extraPadding = true;
            _cellText.UpdateMeshPadding();
        }

        RefreshCell();
        _cellSize = _cellTarget;
        AnimateCell(0f);
    }

    void UpdateCell(bool onMap, Vector3 wp)
    {
        float dt = Time.unscaledDeltaTime;
        bool changed = false;
        GridManager g = GridManager.Instance;
        if (onMap && g != null)
        {
            Vector2Int cell = g.WorldToCell(wp);
            if (g.InBounds(cell.x, cell.y) && cell != _cellCur) { _cellCur = cell; changed = true; }
        }
        _cellRefresh -= dt;
        if (changed || _cellRefresh <= 0f) RefreshCell();   // 换了一格立刻刷新；同一格每 0.25 秒刷新一次（资源被采走、切换语言等）
        AnimateCell(dt);
    }

    void RefreshCell()
    {
        _cellRefresh = 0.25f;
        GridManager g = GridManager.Instance;
        bool has = g != null && _cellCur.x != int.MinValue && g.InBounds(_cellCur.x, _cellCur.y);
        string text = has ? CellInfo.Describe(_cellCur, LanguageGuard.IsJp) : null;
        if (string.IsNullOrEmpty(text)) text = L("grid_idle");
        if (UiStyle.TagWidth(_cellTag) < 14f) UiStyle.FitTag(_cellTag);   // 第一次量不出来时再量一次
        if (_cellText.text != text) _cellText.text = text;

        float inner = Mathf.Max(60f, cellPanelWidth - CellPad * 2f);
        float h = Mathf.Ceil(_cellText.GetPreferredValues(text, inner, 0f).y);
        if (h < 14f) h = 19f * text.Split('\n').Length;              // 量不出来：按行数估
        _cellTextRt.sizeDelta = new Vector2(inner, h);
        float w = Mathf.Max(cellPanelWidth, UiStyle.TagWidth(_cellTag) + CellPad * 2f);
        _cellTarget = new Vector2(w, CellPad * 2f + UiStyle.TagH + CellGap + h);
    }

    // 大小平滑靠近目标；刚出现时淡入。
    void AnimateCell(float dt)
    {
        _cellAnim = Mathf.Min(1f, _cellAnim + dt / CellExpandTime);
        float e = 1f - (1f - _cellAnim) * (1f - _cellAnim) * (1f - _cellAnim);   // 缓出
        _cellSize = Vector2.Lerp(_cellSize, _cellTarget, 1f - Mathf.Exp(-18f * dt));
        _cellRoot.anchoredPosition = cellPanelMargin;
        _cellRoot.sizeDelta = _cellSize;
        _cellGroup.alpha = e;
        float ba = cellPanelBackdrop * e;
        bool bOn = ba > 0.001f;
        if (_cellBackdrop.gameObject.activeSelf != bOn) _cellBackdrop.gameObject.SetActive(bOn);
        if (bOn) _cellBackdrop.color = new Color(textBgColor.r, textBgColor.g, textBgColor.b, ba);
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;

    // ============ 找悬停的对象 ============

    // 只找 NPC（建筑 / 资源的悬停信息框已经去掉；地形、资源看左下角的坐标浮窗）。
    void FindTarget(Vector3 wp, out GameObject target, out NpcView npc)
    {
        target = null;
        npc = null;
        foreach (Collider2D h in Physics2D.OverlapPointAll(wp))
        {
            if (h == null) continue;
            NpcView v = h.GetComponentInParent<NpcView>();
            if (v != null && !string.IsNullOrEmpty(v.npcId)) { npc = v; target = v.gameObject; return; }
        }
    }

    bool PointerOverUI()
    {
        if (EventSystem.current == null || Mouse.current == null) return false;
        PointerEventData data = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, results);
        return results.Count > 0;
    }

    // ============ 文字 ============

    static float NpcHp01(NpcView view)
    {
        NpcData n = FindNpc(view.npcId);
        return n != null ? Mathf.Clamp01(n.health / 100f) : 1f;
    }

    static string NpcText(NpcView view) => NpcTextById(view.npcId);

    static string NpcTextById(string id)
    {
        NpcData n = FindNpc(id);
        if (n == null) return null;
        bool jp = LanguageGuard.IsJp;
        string doing;
        if (n.IsDead) doing = "<color=#FF6B6B>" + (jp ? "死亡" : "已死亡") + "</color>";
        else if (n.isBroken) doing = "<color=#C77DFF>" + (jp ? "人格崩壊" : "人格崩解") + "</color>";
        else if (!string.IsNullOrEmpty(n.currentAction)) doing = n.currentAction;
        else doing = jp ? "待機中" : "空闲";

        string s = "<b>" + n.npcName + "</b>";
        if (!string.IsNullOrEmpty(n.profession)) s += "  <size=80%><color=#9AA5B8>" + n.profession + "</color></size>";
        // 领导 / 今天的分工（NpcSociety）
        string roleTag = NpcSociety.IsLeader(n) ? NpcSociety.RoleName("leader") : "";
        if (NpcSociety.HasRole(n)) roleTag += (roleTag.Length > 0 ? "・" : "") + NpcSociety.RoleName(n.role);
        if (roleTag.Length > 0) s += "  <size=80%>[" + roleTag + "]</size>";
        s += "\n" + (jp ? "行動：" : "正在：") + doing;
        // 饥饿值 / 水分：100 = 不饿 / 不渴（死亡时也显示，死亡效果要把这几行划掉）
        s += "\n<size=82%><color=#C8CFDC>" + (jp ? "生命 " : "生命 ") + Mathf.RoundToInt(Mathf.Max(0f, n.health)) +
             (jp ? "　満腹 " : "　饥饿 ") + Mathf.RoundToInt(n.satiety) + "</color></size>";
        s += "\n<size=82%><color=#C8CFDC>" + (jp ? "水分 " : "水分 ") + Mathf.RoundToInt(n.water) +
             (jp ? "　体力 " : "　体力 ") + Mathf.RoundToInt(n.energy) + "</color>";
        // 安全感：低于他自己的底线时标黄（"不安"），这时他会自己想办法（NpcSecurity / NpcIntentBrain）
        bool uneasy = !n.IsDead && NpcSecurity.Insecure(n);
        s += (jp ? "<color=#C8CFDC>　安心感 </color>" : "<color=#C8CFDC>　安全感 </color>") +
             (uneasy ? "<color=#FFE36A>" + Mathf.RoundToInt(n.security) + (jp ? "（不安）" : "（不安）") + "</color>"
                     : "<color=#C8CFDC>" + Mathf.RoundToInt(n.security) + "</color>") + "</size>";
        // 身上带着什么（物品栏 NpcInventory）
        if (!n.IsDead) s += "\n<size=82%><color=#8E9490>" + (jp ? "所持 " : "携带 ") + "</color><color=#C8CFDC>" + NpcInventory.Describe(n, jp) + "</color></size>";
        if (n.IsDead) s += "\n<size=82%>" + (jp ? "バイタル反応なし" : "无生命体征") + "</size>";
        else
        {
            // 状态词条（濒死 / 受伤 / 生病 / 饥饿 / 口渴 / 疲惫……）
            List<HealthTag> tags = NpcHealth.Tags(n);
            if (tags.Count == 0) s += "\n<size=82%>" + Good(jp ? "健康" : "健康") + "</size>";
            else
            {
                List<string> names = new List<string>();
                foreach (HealthTag t in tags) names.Add("<color=#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(NpcHealth.TagColor(t), Color.white, 0.35f)) + ">" + NpcHealth.TagName(n, t, jp) + "</color>");
                s += "\n<size=82%>" + string.Join("・", names) + "</size>";
            }
        }
        return s;
    }

    static string Good(string s) => "<color=#74DB8C>" + s + "</color>";

    static NpcData FindNpc(string id)
    {
        if (WorldManager.Instance == null || WorldManager.Instance.world == null) return null;
        foreach (NpcData n in WorldManager.Instance.world.npcs) if (n.id == id) return n;
        return null;
    }

    void Hide() => HideNpc();

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
