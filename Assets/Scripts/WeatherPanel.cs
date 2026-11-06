using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================
// 天气浮窗：一直显示在屏幕右上角，和其它浮窗一样的样式（UiPopup：深色底 + 淡绿细框 + 左上角「weather」标签），
//   进游戏时播一次展开动画，之后常驻；屏幕大小变了也贴着右上角。
//   左边一列（每行 = 发光符号 + 一句说明）：
//     雨：小雨 = 一滴倾斜的水滴；中雨 = 两滴并排；大雨 = 三滴（两滴并排，上面再一滴）。没下雨 → 小雨的符号不亮灯（很暗）
//     雪：小雪 = 一片雪花；中雪 / 大雪的排法和雨一样。没下雪 → 小雪的符号不亮灯
//     风：测风袋（截尖的圆锥、袋口是个洞、袋身有条纹）——微风垂得很低，中风抬起来一些，大风吹到水平。没风 → 微风的符号不亮灯
//     冰雹（没有等级）：空心六边形 + 中间一片雪花。没有冰雹 → 不亮灯
//   右边：营地气温（段码数字）。正常 = 淡绿，寒冷 = 淡蓝，炎热 = 红（极端时颜色更深）。
//   符号都是代码画的（和资源栏的图标同一种画法），每种只画一次。
// 由 HudLayout.Start 调 Ensure() 创建；数据来自 ClimateSystem。
// ============================================================================
public class WeatherPanel : MonoBehaviour
{
    public static WeatherPanel Instance { get; private set; }

    const float W = 340f, RowH = 48f, IconSize = 46f, LeftW = 196f;
    const float DimAlpha = 0.16f;   // 不亮灯的符号

    static readonly Color Green = HudGlow.Green, GreenGlow = HudGlow.GreenGlow;
    static readonly Color ColdC = new Color(0.62f, 0.85f, 1f), ColdGlow = new Color(0.35f, 0.65f, 1f);
    static readonly Color HotC = new Color(1f, 0.45f, 0.4f), HotGlow = new Color(1f, 0.28f, 0.22f);
    static readonly Color ExtColdC = new Color(0.45f, 0.65f, 1f), ExtHotC = new Color(1f, 0.25f, 0.2f);

    enum Shape { RainLight, RainModerate, RainHeavy, SnowLight, SnowModerate, SnowHeavy, WindBreeze, WindModerate, WindStrong, Hail }

    class Row { public RawImage icon; public TextMeshProUGUI label; }

    UiPopup _popup;
    Row _rain, _snow, _wind, _hail;
    SegmentText _temp;
    TextMeshProUGUI _tempLabel;
    float _timer;

    public static WeatherPanel Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("WeatherPanel").AddComponent<WeatherPanel>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _popup = UiPopup.Create("WeatherPopup", 60);
        _popup.transform.SetParent(transform, false);
        _popup.SetTag("weather");
        _popup.KeepTether = false;
        _popup.Interactable = false;
        Build(_popup.Body);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Build(RectTransform body)
    {
        float y0 = UiStyle.TagH + 10f;
        _rain = MakeRow(body, 0, y0);
        _snow = MakeRow(body, 1, y0);
        _wind = MakeRow(body, 2, y0);
        _hail = MakeRow(body, 3, y0);

        // 右边：气温
        RectTransform tr = NewRect("Temp", body);
        tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0f, 1f);
        tr.anchoredPosition = new Vector2(LeftW + 8f, -(y0 + 8f));
        tr.sizeDelta = new Vector2(W - LeftW - 20f, 96f);
        RawImage img = tr.gameObject.AddComponent<RawImage>();
        img.raycastTarget = false;
        _temp = tr.gameObject.AddComponent<SegmentText>();
        _temp.subText = "TEMP";
        _temp.mainHeight = 0.58f;
        _temp.align = 0.5f;
        _temp.glowRadius = 6;

        _tempLabel = NewText(body, 15f);
        RectTransform lr = _tempLabel.rectTransform;
        lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0f, 1f);
        lr.anchoredPosition = new Vector2(LeftW + 8f, -(y0 + 110f));
        lr.sizeDelta = new Vector2(W - LeftW - 20f, 24f);
        _tempLabel.alignment = TextAlignmentOptions.Center;

        // 中间一条淡淡的竖线分开左右
        RectTransform sep = NewRect("Sep", body);
        sep.anchorMin = sep.anchorMax = sep.pivot = new Vector2(0f, 1f);
        sep.anchoredPosition = new Vector2(LeftW, -(y0 + 2f));
        sep.sizeDelta = new Vector2(1f, RowH * 4f - 4f);
        Image si = sep.gameObject.AddComponent<Image>();
        si.color = new Color(Green.r, Green.g, Green.b, 0.25f);
        si.raycastTarget = false;
    }

    Row MakeRow(RectTransform body, int i, float y0)
    {
        Row r = new Row();
        RectTransform ir = NewRect("Icon" + i, body);
        ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(0f, 1f);
        ir.anchoredPosition = new Vector2(12f, -(y0 + i * RowH));
        ir.sizeDelta = new Vector2(IconSize, IconSize);
        r.icon = ir.gameObject.AddComponent<RawImage>();
        r.icon.raycastTarget = false;
        r.label = NewText(body, 15f);
        RectTransform lr = r.label.rectTransform;
        lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0f, 1f);
        lr.anchoredPosition = new Vector2(12f + IconSize + 10f, -(y0 + i * RowH + 12f));
        lr.sizeDelta = new Vector2(LeftW - IconSize - 30f, 22f);
        return r;
    }

    public static float PanelHeight => UiStyle.TagH + 10f + RowH * 4f + 8f;
    static Vector2 PanelSize => new Vector2(W, PanelHeight);

    public float rightMargin = 14f, topMargin = 8f;

    // 天气浮窗的下沿离屏幕顶边多远、右边距多少（任务汇报 HudToasts 排在它下面、右边对齐）。
    public static float BottomFromTop => (Instance != null ? Instance.topMargin : 8f) + PanelHeight;
    public static float RightMargin => Instance != null ? Instance.rightMargin : 14f;

    // 贴着屏幕右上角（画布坐标，左下角为原点）。
    Vector2 TopLeft()
    {
        Vector2 cs = _popup.CanvasSize;
        return new Vector2(cs.x - rightMargin - W, cs.y - topMargin);
    }

    void Update()
    {
        if (!_popup.IsOpen)
        {
            Refresh();
            _popup.OpenCanvas(Vector2.zero, TopLeft(), PanelSize, false);   // 进游戏时展开一次，之后一直开着
            return;
        }
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = 0.5f;
        Refresh();
        _popup.PlaceCanvas(TopLeft());   // 屏幕大小变了也贴着天数框
    }

    string ButtonLabel()
    {
        ClimateSystem cs = ClimateSystem.Instance;
        return cs != null ? "weather : " + Mathf.RoundToInt(cs.CampTemperature) + "°" : "weather";
    }

    void Refresh()
    {
        ClimateSystem cs = ClimateSystem.Instance;
        PrecipLevel rain = cs != null ? cs.Rain : PrecipLevel.None;
        PrecipLevel snow = cs != null ? cs.Snow : PrecipLevel.None;
        WindLevel wind = cs != null ? cs.Wind : WindLevel.None;
        bool hail = cs != null && cs.Hail;

        SetRow(_rain, rain == PrecipLevel.Heavy ? Shape.RainHeavy : rain == PrecipLevel.Moderate ? Shape.RainModerate : Shape.RainLight,
               rain != PrecipLevel.None, L("wx_rain_" + rain.ToString().ToLowerInvariant()));
        SetRow(_snow, snow == PrecipLevel.Heavy ? Shape.SnowHeavy : snow == PrecipLevel.Moderate ? Shape.SnowModerate : Shape.SnowLight,
               snow != PrecipLevel.None, L("wx_snow_" + snow.ToString().ToLowerInvariant()));
        SetRow(_wind, wind == WindLevel.Strong ? Shape.WindStrong : wind == WindLevel.Moderate ? Shape.WindModerate : Shape.WindBreeze,
               wind != WindLevel.None, L("wx_wind_" + wind.ToString().ToLowerInvariant()));
        SetRow(_hail, Shape.Hail, hail, L(hail ? "wx_hail" : "wx_hail_none"));

        // 气温：正常淡绿 / 寒冷淡蓝 / 炎热红
        float t = cs != null ? cs.CampTemperature : 20f;
        TempState st = cs != null ? cs.StateOf(t) : TempState.Normal;
        Color c = Green, g = GreenGlow;
        string key = "wx_temp_normal";
        switch (st)
        {
            case TempState.Cold: c = ColdC; g = ColdGlow; key = "wx_temp_cold"; break;
            case TempState.ExtremeCold: c = ExtColdC; g = ColdGlow; key = "wx_temp_ext_cold"; break;
            case TempState.Hot: c = HotC; g = HotGlow; key = "wx_temp_hot"; break;
            case TempState.ExtremeHot: c = ExtHotC; g = HotGlow; key = "wx_temp_ext_hot"; break;
        }
        _temp.SetPalette(c, g);
        _temp.SetText(Mathf.RoundToInt(t).ToString());
        _tempLabel.text = t.ToString("0.#") + "℃　" + L(key);
        _tempLabel.color = c;
    }

    static void SetRow(Row r, Shape shape, bool lit, string text)
    {
        r.icon.texture = Icon(shape);
        r.icon.color = new Color(1f, 1f, 1f, lit ? 1f : DimAlpha);
        r.label.text = text;
        r.label.color = lit ? UiStyle.TextMain : new Color(0.55f, 0.58f, 0.56f, 1f);
    }

    // ============ 代码画的天气符号（32×32 的坐标，y 朝上）============

    static readonly Dictionary<Shape, Texture2D> _tex = new Dictionary<Shape, Texture2D>();

    static Texture2D Icon(Shape shape)
    {
        if (_tex.TryGetValue(shape, out Texture2D cached) && cached != null) return cached;
        const int N = 96;
        const float S = 72f / 32f, O = (N - 72) * 0.5f;   // 符号画在中间 72 像素，四周留给发光
        float[] cov = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                        if (Hit(shape, new Vector2((x + (sx + 0.5f) / 4f - O) / S, (y + (sy + 0.5f) / 4f - O) / S))) hit++;
                cov[y * N + x] = hit / 16f;
            }
        SegmentPainter.Style st = new SegmentPainter.Style
        {
            r = Green.r, g = Green.g, b = Green.b, gr = GreenGlow.r, gg = GreenGlow.g, gb = GreenGlow.b,
            glow = 0.7f, glowRadius = 8f,
        };
        byte[] px = SegmentPainter.Compose(cov, null, N, N, st);
        Color32[] cols = new Color32[N * N];
        for (int k = 0; k < cols.Length; k++) cols[k] = new Color32(px[k * 4], px[k * 4 + 1], px[k * 4 + 2], px[k * 4 + 3]);
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.name = "Weather" + shape;
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        t.SetPixels32(cols);
        t.Apply(false);
        _tex[shape] = t;
        return t;
    }

    static bool Hit(Shape s, Vector2 p)
    {
        switch (s)
        {
            // 雨：小雨一滴（倾斜）/ 中雨两滴并排 / 大雨三滴（两滴并排，上面再一滴）
            case Shape.RainLight:    return Drop(p, new Vector2(14.5f, 11.5f), 6.8f);
            case Shape.RainModerate: return Drop(p, new Vector2(8.5f, 12f), 5.2f) || Drop(p, new Vector2(21.5f, 12f), 5.2f);
            case Shape.RainHeavy:    return Drop(p, new Vector2(8f, 6.5f), 4f) || Drop(p, new Vector2(21f, 6.5f), 4f) || Drop(p, new Vector2(14.5f, 20f), 4f);
            // 雪：排法和雨一样
            case Shape.SnowLight:    return Flake(p, new Vector2(16f, 16f), 13f);
            case Shape.SnowModerate: return Flake(p, new Vector2(8.5f, 16f), 7.5f) || Flake(p, new Vector2(23.5f, 16f), 7.5f);
            case Shape.SnowHeavy:    return Flake(p, new Vector2(8.5f, 9f), 7f) || Flake(p, new Vector2(23.5f, 9f), 7f) || Flake(p, new Vector2(16f, 23.5f), 7f);
            // 冰雹（没有等级）：空心六边形，中间一片雪花
            case Shape.Hail:         return HexRing(p, new Vector2(16f, 16f), 14.5f, 1.5f) || Flake(p, new Vector2(16f, 16f), 8f);
            // 风：测风袋（风向袋）——小风垂得很低，中风抬起来一些，大风吹到水平
            case Shape.WindBreeze:   return WindSock(p, 58f);
            case Shape.WindModerate: return WindSock(p, 30f);
            case Shape.WindStrong:   return WindSock(p, 0f);
            default: return false;
        }
    }

    // 倾斜的水滴：下面一个圆，往右上方收成尖；里面一道小弧当高光。c = 圆心，r = 圆的半径。
    static bool Drop(Vector2 p, Vector2 c, float r)
    {
        Vector2 q = Rotate(p - c, 22f) / r;   // 转回"尖朝正上方"的坐标，再按大小缩放
        const float h = 2.3f;                 // 尖离圆心多高（半径的倍数）
        bool body;
        if (q.x * q.x + q.y * q.y <= 1f) body = true;
        else
        {
            float ty = 1f / h, tx = Mathf.Sqrt(1f - ty * ty);   // 从尖到圆的切点
            body = q.y >= ty && q.y <= h && Mathf.Abs(q.x) <= tx * (h - q.y) / (h - ty);
        }
        if (!body) return false;
        // 高光：左下方一小段弧（挖空）
        float d = q.magnitude, a = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;
        return !(d > 0.55f && d < 0.72f && a > 150f && a < 235f) && !(d > 0.55f && d < 0.72f && a < -125f);
    }

    // 雪花：六条主枝，每条枝上一对小叉。c = 中心，r = 枝长。
    static bool Flake(Vector2 p, Vector2 c, float r)
    {
        float w = Mathf.Max(0.75f, r * 0.085f);
        for (int k = 0; k < 6; k++)
        {
            float a = (90f + k * 60f) * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            if (Seg(p, c, c + d * r) < w) return true;
            Vector2 m = c + d * r * 0.58f;                         // 小叉长在枝的六成处
            for (int side = -1; side <= 1; side += 2)
            {
                float b = a + side * 45f * Mathf.Deg2Rad;
                Vector2 e = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                if (Seg(p, m, m + e * r * 0.32f) < w) return true;
            }
        }
        return Circle(p, c.x, c.y, w * 1.4f);
    }

    // 空心六边形（尖朝上）：边线粗 w。
    static bool HexRing(Vector2 p, Vector2 c, float r, float w)
    {
        for (int k = 0; k < 6; k++)
        {
            float a0 = (90f + k * 60f) * Mathf.Deg2Rad, a1 = (90f + (k + 1) * 60f) * Mathf.Deg2Rad;
            if (Seg(p, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r) < w) return true;
        }
        return false;
    }

    // 测风袋：左边一根竖杆，杆顶挂一只截了尖的圆锥（大口朝杆、口是一个空心椭圆 = 中间的洞），
    // 袋身一圈圈条纹（实心、空心交替）。droopDeg = 袋子往下垂的角度（0 = 水平）。
    static bool WindSock(Vector2 p, float droopDeg)
    {
        Vector2 pole0 = new Vector2(4.5f, 1.5f), pole1 = new Vector2(4.5f, 29.5f);
        if (Seg(p, pole0, pole1) < 1.1f || Circle(p, pole1.x, pole1.y + 0.6f, 1.6f)) return true;   // 杆 + 杆顶的小球

        Vector2 root = new Vector2(6.5f, 26f);            // 袋口挂在杆顶下面一点
        float a = -droopDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), nrm = new Vector2(-dir.y, dir.x);
        Vector2 q = p - root;
        float u = Vector2.Dot(q, dir), v = Vector2.Dot(q, nrm);
        const float L = 23f, w0 = 5f, w1 = 2.2f, lip = 1.6f, line = 0.9f;
        // 袋口：空心椭圆（看得到里面的洞）
        float eu = u / lip, ev = v / w0, e = eu * eu + ev * ev;
        float eIn = (u / (lip - line)) * (u / (lip - line)) + (v / (w0 - line)) * (v / (w0 - line));
        if (e <= 1f && eIn > 1f) return true;
        if (u < 0f || u > L) return false;
        float half = w0 - (w0 - w1) * u / L;              // 越往尖越细
        if (Mathf.Abs(v) > half) return false;
        if (u <= lip * 0.2f) return false;
        // 条纹：五段，实心 / 空心交替；空心段只画上下两条边和段与段之间的线
        int band = Mathf.Min(4, (int)((u - lip * 0.2f) / ((L - lip * 0.2f) / 5f)));
        if (band % 2 == 0) return true;
        float bandStart = lip * 0.2f + band * (L - lip * 0.2f) / 5f, bandEnd = bandStart + (L - lip * 0.2f) / 5f;
        return Mathf.Abs(v) > half - line || u - bandStart < line * 0.6f || bandEnd - u < line * 0.6f || (band == 4 && u > L - line);
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
        return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);
    }

    static bool Circle(Vector2 p, float x, float y, float r) { float dx = p.x - x, dy = p.y - y; return dx * dx + dy * dy <= r * r; }

    static float Seg(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return (a + ab * t - p).magnitude;
    }

    // ============ 小工具 ============

    static TextMeshProUGUI NewText(Transform parent, float size)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = GameFonts.Find();
        if (f != null) t.font = f;
        t.fontSize = size;
        t.color = UiStyle.TextMain;
        t.alignment = TextAlignmentOptions.Left;
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

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
}
