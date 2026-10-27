using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================
// 电子表风格的段码字（像卡西欧电子表 / DS-Digital 那种），完全用代码画，不需要字体文件。
//   · 数字用 7 段，字母（D / A / Y 等）用 14 段；整体右斜；
//   · 自带淡淡的发光（把字形模糊一遍垫在下面）；没亮的段可以留一点点"残影"，像真的液晶屏；
//   · 背景透明。
// 用法：挂在带 RawImage 的 UI 物体上，调 SetText("12")；下面那行小字是 subText（默认 "DAY"）。
// 只有文字变化时才重画一次贴图（天数一天才变一次），平时没有开销。
// ============================================================================
[RequireComponent(typeof(RawImage))]
public class SegmentText : MonoBehaviour
{
    [Header("颜色")]
    public Color color = new Color(0.72f, 1f, 0.78f, 1f);      // 淡绿色
    public Color glowColor = new Color(0.45f, 1f, 0.6f, 1f);
    [Range(0f, 1.5f)] public float glowStrength = 0.6f;
    [Tooltip("发光扩散多远（贴图像素）")]
    [Range(1, 16)] public int glowRadius = 7;
    [Tooltip("没亮的段的残影浓度（0 = 不要残影）")]
    [Range(0f, 0.3f)] public float ghostAlpha = 0.06f;

    [Header("字形")]
    [Tooltip("右斜角度")]
    [Range(0f, 20f)] public float slantDeg = 8f;
    [Tooltip("贴图分辨率 = UI 尺寸 × 这个倍数（越大越清晰）")]
    [Range(1, 4)] public int resolution = 2;
    [Tooltip("大数字高度占框高的比例")]
    [Range(0.2f, 0.9f)] public float mainHeight = 0.5f;
    [Tooltip("下面一行小字")]
    public string subText = "DAY";
    [Tooltip("小字高度占框高的比例（0 = 不要小字）")]
    [Range(0f, 0.4f)] public float subHeight = 0.17f;
    [Tooltip("水平对齐：0 = 靠左，0.5 = 居中，1 = 靠右")]
    [Range(0f, 1f)] public float align = 0.5f;

    RawImage _img;
    Texture2D _tex;
    string _text;

    public void SetText(string text)
    {
        if (text == _text && _tex != null) return;
        _text = text;
        Redraw();
    }

    // 换颜色（HudGlow 切换 正常绿 / 警告黄 时调用）；颜色真的变了才重画。
    public void SetPalette(Color core, Color glow)
    {
        if (core == color && glow == glowColor) return;
        color = core;
        glowColor = glow;
        if (_tex != null) Redraw();
    }

    public void Redraw()
    {
        if (_img == null) _img = GetComponent<RawImage>();
        RectTransform rt = (RectTransform)transform;
        int w = Mathf.Max(8, Mathf.RoundToInt(rt.rect.width * resolution));
        int h = Mathf.Max(8, Mathf.RoundToInt(rt.rect.height * resolution));
        SegmentPainter.Style st = new SegmentPainter.Style
        {
            r = color.r, g = color.g, b = color.b,
            gr = glowColor.r, gg = glowColor.g, gb = glowColor.b,
            glow = glowStrength, glowRadius = glowRadius * resolution / 2f,
            ghost = ghostAlpha, slant = slantDeg,
            mainHeight = mainHeight, subHeight = subHeight, align = align,
        };
        byte[] px = SegmentPainter.Paint(_text ?? "", subText ?? "", w, h, st);

        if (_tex == null || _tex.width != w || _tex.height != h)
        {
            if (_tex != null) Destroy(_tex);
            _tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _tex.name = "SegmentText";
            _tex.filterMode = FilterMode.Bilinear;
            _tex.wrapMode = TextureWrapMode.Clamp;
        }
        Color32[] c = new Color32[w * h];
        for (int i = 0; i < c.Length; i++)
            c[i] = new Color32(px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]);
        _tex.SetPixels32(c);
        _tex.Apply(false);
        _img.texture = _tex;
        if (_img.color.a <= 0f) _img.color = Color.white; // 开始时设成透明防闪白；透明度留给 HudGlow 做闪烁

    }

    void OnRectTransformDimensionsChange()
    {
        if (_tex != null && isActiveAndEnabled) Redraw();
    }

    void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
    }
}

// 纯计算：把段码字画成 RGBA 像素（不依赖 Unity，可以离线出预览图）。
public static class SegmentPainter
{
    public struct Style
    {
        public float r, g, b;        // 字的颜色
        public float gr, gg, gb;     // 发光颜色
        public float glow, glowRadius, ghost, slant;
        public float mainHeight, subHeight;
        public float align;          // 0 靠左 / 0.5 居中 / 1 靠右
    }

    // 段的编号：
    //   0 上横  1 右上竖  2 右下竖  3 下横  4 左下竖  5 左上竖  6 中横（整条，数字用）
    //   7 中横左半  8 中横右半  9 中竖上  10 中竖下  11 左上斜  12 右上斜  13 左下斜  14 右下斜
    //   15 上横（右边缩短）16 下横（右边缩短）→ 右边两个角空出来，就是 D
    const int SegCount = 17;

    static int Glyph(char ch)
    {
        switch (char.ToUpperInvariant(ch))
        {
            case '0': return Bits(0, 1, 2, 3, 4, 5);
            case '1': return Bits(1, 2);
            case '2': return Bits(0, 1, 6, 4, 3);
            case '3': return Bits(0, 1, 6, 2, 3);
            case '4': return Bits(5, 6, 1, 2);
            case '5': return Bits(0, 5, 6, 2, 3);
            case '6': return Bits(0, 5, 4, 3, 2, 6);
            case '7': return Bits(0, 1, 2);
            case '8': return Bits(0, 1, 2, 3, 4, 5, 6);
            case '9': return Bits(0, 1, 2, 3, 5, 6);
            case '-': return Bits(6);
            case '/': return Bits(13, 12);
            case 'A': return Bits(4, 5, 0, 1, 2, 7, 8);
            case 'D': return Bits(15, 16, 1, 2, 4, 5);
            case 'Y': return Bits(11, 12, 10);
            case 'E': return Bits(0, 5, 4, 3, 7);
            case 'H': return Bits(5, 4, 1, 2, 7, 8);
            case 'L': return Bits(5, 4, 3);
            case 'O': return Bits(0, 1, 2, 3, 4, 5);
            case 'P': return Bits(0, 1, 5, 4, 7, 8);
            case 'S': return Bits(0, 5, 6, 2, 3);
            case 'T': return Bits(0, 9, 10);
            case 'U': return Bits(5, 4, 3, 2, 1);
            case 'N': return Bits(4, 5, 11, 14, 2, 1);
            case 'M': return Bits(4, 5, 11, 12, 1, 2);
            case 'W': return Bits(5, 4, 13, 14, 2, 1);
            case 'R': return Bits(0, 1, 5, 4, 7, 8, 14);
            case 'I': return Bits(0, 9, 10, 3);
            case 'C': return Bits(0, 5, 4, 3);
            case 'F': return Bits(0, 5, 4, 7);
            default: return 0;
        }
    }

    static bool IsDigitLike(char ch) => (ch >= '0' && ch <= '9') || ch == '-';

    static int Bits(params int[] s)
    {
        int b = 0;
        foreach (int i in s) b |= 1 << i;
        return b;
    }

    public static byte[] Paint(string main, string sub, int w, int h, Style st)
    {
        float[] core = new float[w * h];
        float[] ghost = new float[w * h];
        float tan = (float)Math.Tan(st.slant * Math.PI / 180.0);

        // 大数字（7 段）
        float mh = h * st.mainHeight;
        float sh = st.subHeight > 0f && sub.Length > 0 ? h * st.subHeight : 0f;
        float gapY = sh > 0f ? h * 0.07f : 0f;
        float total = mh + gapY + sh;
        float mainBase = (h - total) * 0.5f + sh + gapY; // 贴图 y 向上
        DrawLine(core, ghost, w, h, main, mainBase, mh, tan, st.ghost > 0f, 0.2f, st.align, st.glowRadius);
        if (sh > 0f) DrawLine(core, null, w, h, sub, (h - total) * 0.5f, sh, tan, false, 0.42f, st.align, st.glowRadius);
        return Compose(core, ghost, w, h, st);
    }

    // 覆盖率 → RGBA：字本体 + 模糊出来的发光 + 残影。图标也用这个（ghost 传 null）。
    public static byte[] Compose(float[] core, float[] ghost, int w, int h, Style st)
    {
        // 发光：字形模糊几遍
        float[] glow = (float[])core.Clone();
        float[] tmp = new float[w * h];
        int rad = Math.Max(1, (int)Math.Round(st.glowRadius / 1.7f));
        for (int p = 0; p < 3; p++) BoxBlur(glow, tmp, w, h, rad);

        byte[] px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            float c = core[i];
            float gA = Math.Min(1f, glow[i] * st.glow) * (1f - c);
            float hA = ghost != null ? ghost[i] * st.ghost * (1f - c) * (1f - gA) : 0f;
            float a = c + gA + hA;
            if (a <= 0.002f) continue;
            float r = (st.r * c + st.gr * gA + st.r * hA) / a;
            float g = (st.g * c + st.gg * gA + st.g * hA) / a;
            float b = (st.b * c + st.gb * gA + st.b * hA) / a;
            px[i * 4] = ToByte(r);
            px[i * 4 + 1] = ToByte(g);
            px[i * 4 + 2] = ToByte(b);
            px[i * 4 + 3] = ToByte(a);
        }
        return px;
    }

    static byte ToByte(float v) => (byte)Math.Max(0, Math.Min(255, (int)(v * 255f + 0.5f)));

    // 画一行字，水平居中。height = 字高（像素），baseY = 字底边的 y。
    static void DrawLine(float[] core, float[] ghost, int w, int h, string text, float baseY, float height, float tan, bool withGhost, float spacing, float align, float pad)
    {
        if (string.IsNullOrEmpty(text)) return;
        float gw = height * 0.52f;           // 字宽
        float th = height * 0.13f;           // 笔画粗细
        float sp = height * spacing;         // 字间距
        int n = text.Length;
        float lineW = height * tan;          // 斜过去多占的宽度也算上
        for (int i = 0; i < n; i++) lineW += Advance(text, i, gw, th, sp, i == n - 1);
        float maxW = Math.Max(4f, w - pad * 2f);
        if (lineW > maxW)                    // 位数太多就整体缩小
        {
            float k = maxW / lineW;
            baseY += height * (1f - k) * 0.5f; // 缩小后仍然竖直居中
            height *= k; gw *= k; th *= k; sp *= k; lineW = maxW;
        }
        float gx = pad + (maxW - lineW) * align;
        for (int i = 0; i < n; i++)
        {
            char ch = text[i];
            float adv = Advance(text, i, gw, th, sp, i == n - 1);
            if (ch == '.')                   // 小数点：底部一个小方块
            {
                Dot(core, w, h, gx + th * 0.1f, baseY, tan, th * 1.15f);
                gx += adv;
                continue;
            }
            int bits = Glyph(ch);
            int ghostBits = withGhost && IsDigitLike(ch) ? Bits(0, 1, 2, 3, 4, 5, 6) : 0;
            for (int s = 0; s < SegCount; s++)
            {
                bool on = (bits & (1 << s)) != 0;
                bool gh = (ghostBits & (1 << s)) != 0;
                if (!on && !gh) continue;
                Segment(s, gw, height, out float ax, out float ay, out float bx, out float by, out bool diag);
                Stamp(on ? core : ghost, w, h, gx, baseY, tan, ax, ay, bx, by, th, diag, height);
            }
            gx += adv;
        }
    }

    // 一个字占多宽（含后面的间距）；小数点很窄，前后间距也减半。
    static float Advance(string text, int i, float gw, float th, float sp, bool last)
    {
        bool dot = text[i] == '.';
        bool nextDot = !last && text[i + 1] == '.';
        float width = dot ? th * 1.4f : gw;
        if (last) return width;
        return width + (dot || nextDot ? sp * 0.45f : sp);
    }

    static void Dot(float[] buf, int w, int h, float x, float baseY, float tan, float size)
    {
        float r = size * 0.5f, cx = x + r, cy = r;
        int py0 = Math.Max(0, (int)Math.Floor(baseY - 1)), py1 = Math.Min(h - 1, (int)Math.Ceiling(baseY + size + 1));
        int px0 = Math.Max(0, (int)Math.Floor(x - 1)), px1 = Math.Min(w - 1, (int)Math.Ceiling(x + size + size * tan + 2));
        for (int py = py0; py <= py1; py++)
        {
            float fy = py + 0.5f - baseY;
            for (int px = px0; px <= px1; px++)
            {
                float fx = px + 0.5f - fy * tan;
                float d = Math.Max(Math.Abs(fx - cx) - r, Math.Abs(fy - cy) - r);
                float cov = 0.5f - d;
                if (cov <= 0f) continue;
                if (cov > 1f) cov = 1f;
                int i = py * w + px;
                if (cov > buf[i]) buf[i] = cov;
            }
        }
    }

    // 段在字形里的两个端点（字形坐标：左下角 0,0，宽 W 高 H）。
    static void Segment(int s, float W, float H, out float ax, out float ay, out float bx, out float by, out bool diag)
    {
        float M = H * 0.5f, C = W * 0.5f;
        diag = false;
        switch (s)
        {
            case 0: ax = 0; ay = H; bx = W; by = H; break;
            case 1: ax = W; ay = H; bx = W; by = M; break;
            case 2: ax = W; ay = M; bx = W; by = 0; break;
            case 3: ax = 0; ay = 0; bx = W; by = 0; break;
            case 4: ax = 0; ay = 0; bx = 0; by = M; break;
            case 5: ax = 0; ay = M; bx = 0; by = H; break;
            case 6: ax = 0; ay = M; bx = W; by = M; break;
            case 7: ax = 0; ay = M; bx = C; by = M; break;
            case 8: ax = C; ay = M; bx = W; by = M; break;
            case 9: ax = C; ay = H; bx = C; by = M; break;
            case 10: ax = C; ay = M; bx = C; by = 0; break;
            case 11: ax = 0; ay = H; bx = C; by = M; diag = true; break;
            case 12: ax = W; ay = H; bx = C; by = M; diag = true; break;
            case 13: ax = 0; ay = 0; bx = C; by = M; diag = true; break;
            case 15: ax = 0; ay = H; bx = W * 0.8f; by = H; break;
            case 16: ax = 0; ay = 0; bx = W * 0.8f; by = 0; break;
            case 14: ax = W; ay = 0; bx = C; by = M; diag = true; break;
            default: ax = ay = bx = by = 0; break;
        }
    }

    // 把一段画进覆盖率数组（抗锯齿）。直段两头是 45° 尖角（电子表的样子），斜段是平头。
    static void Stamp(float[] buf, int w, int h, float gx, float baseY, float tan,
                      float ax, float ay, float bx, float by, float th, bool diag, float H)
    {
        float gap = th * 0.28f;              // 段和段之间的小缝
        float dx = bx - ax, dy = by - ay;
        float len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f) return;
        float ux = dx / len, uy = dy / len;
        float half = th * 0.5f;
        // 斜段从角上往里缩一点，别和横竖段糊在一起
        float trimA = diag ? th * 1.1f : gap, trimB = diag ? th * 0.9f : gap;
        float L = len - trimA - trimB;
        if (L <= 0f) return;
        float sx = ax + ux * trimA, sy = ay + uy * trimA;

        // 包围盒（贴图像素）
        float minX = Math.Min(ax, bx) - th, maxX = Math.Max(ax, bx) + th;
        float minY = Math.Min(ay, by) - th, maxY = Math.Max(ay, by) + th;
        int py0 = Math.Max(0, (int)Math.Floor(baseY + minY));
        int py1 = Math.Min(h - 1, (int)Math.Ceiling(baseY + maxY));
        int px0 = Math.Max(0, (int)Math.Floor(gx + minX + Math.Min(0f, minY * tan)));
        int px1 = Math.Min(w - 1, (int)Math.Ceiling(gx + maxX + Math.Max(0f, maxY * tan)));
        const float inv = 0.70710678f;
        for (int py = py0; py <= py1; py++)
        {
            float fy = py + 0.5f - baseY;              // 字形坐标 y
            float shift = fy * tan;                    // 斜体：越高越往右
            for (int px = px0; px <= px1; px++)
            {
                float fx = px + 0.5f - gx - shift;
                float rx = fx - sx, ry = fy - sy;
                float t = rx * ux + ry * uy;           // 沿段方向
                float s = Math.Abs(-rx * uy + ry * ux); // 垂直方向
                float d;
                if (diag) d = Math.Max(s - half, Math.Max(-t, t - L));
                else d = Math.Max(s - half, Math.Max((s - t) * inv, (s - (L - t)) * inv));
                float cov = 0.5f - d;
                if (cov <= 0f) continue;
                if (cov > 1f) cov = 1f;
                int i = py * w + px;
                if (cov > buf[i]) buf[i] = cov;
            }
        }
    }

    // 盒式模糊（横一遍竖一遍），三遍近似高斯。
    static void BoxBlur(float[] v, float[] tmp, int w, int h, int r)
    {
        float norm = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            float acc = 0f;
            int row = y * w;
            for (int k = -r; k <= r; k++) acc += v[row + Clamp(k, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[row + x] = acc * norm;
                acc += v[row + Clamp(x + r + 1, 0, w - 1)] - v[row + Clamp(x - r, 0, w - 1)];
            }
        }
        for (int x = 0; x < w; x++)
        {
            float acc = 0f;
            for (int k = -r; k <= r; k++) acc += tmp[Clamp(k, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                v[y * w + x] = acc * norm;
                acc += tmp[Clamp(y + r + 1, 0, h - 1) * w + x] - tmp[Clamp(y - r, 0, h - 1) * w + x];
            }
        }
    }

    static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}

// ============================================================================
// HUD 发光组：一块信息（图标 + 标题 + 数字 + 进度条）一起变色、一起闪。
//   正常：淡绿色；SetWarning(true)：转成淡黄色，并且不规则地闪烁（多数时候亮着，偶尔暗一下 / 灭一下 / 连闪）。
//   段码字和发光图标在切换颜色时重画一次；闪烁只改透明度，不重画，几乎没有开销。
// ============================================================================
public class HudGlow : MonoBehaviour
{
    public static readonly Color Green = new Color(0.72f, 1f, 0.78f, 1f);
    public static readonly Color GreenGlow = new Color(0.45f, 1f, 0.6f, 1f);
    public static readonly Color Yellow = new Color(1f, 0.93f, 0.55f, 1f);
    public static readonly Color YellowGlow = new Color(1f, 0.82f, 0.32f, 1f);

    class Target { public Graphic g; public SegmentText seg; public Texture normalTex, warnTex; public float alpha; }
    readonly List<Target> _targets = new List<Target>();

    public bool Warning { get; private set; }
    float _flick = 1f, _next;

    public void AddSegment(SegmentText s, float alpha = 1f)
    {
        if (s == null) return;
        _targets.Add(new Target { g = s.GetComponent<Graphic>(), seg = s, alpha = alpha });
        s.SetPalette(Warning ? Yellow : Green, Warning ? YellowGlow : GreenGlow);
        Apply();
    }

    public void AddIcon(RawImage img, Texture normal, Texture warn, float alpha = 1f)
    {
        if (img == null) return;
        _targets.Add(new Target { g = img, normalTex = normal, warnTex = warn, alpha = alpha });
        img.texture = Warning ? warn : normal;
        Apply();
    }

    public void AddGraphic(Graphic g, float alpha = 1f)
    {
        if (g == null) return;
        _targets.Add(new Target { g = g, alpha = alpha });
        Apply();
    }

    public void SetWarning(bool on)
    {
        if (on == Warning) return;
        Warning = on;
        foreach (Target t in _targets)
        {
            if (t.seg != null) t.seg.SetPalette(on ? Yellow : Green, on ? YellowGlow : GreenGlow);
            else if (t.normalTex != null && t.g is RawImage ri) ri.texture = on ? t.warnTex : t.normalTex;
        }
        _flick = 1f;
        _next = Time.unscaledTime + UnityEngine.Random.Range(0.1f, 0.5f);
        Apply();
    }

    void Update()
    {
        if (!Warning) return;
        float now = Time.unscaledTime;
        if (now < _next) return;
        if (_flick < 0.99f)
        {
            // 暗完了：多数时候恢复亮，偶尔紧接着再闪一下（连闪）
            if (UnityEngine.Random.value < 0.3f) { _flick = UnityEngine.Random.Range(0.15f, 0.5f); _next = now + UnityEngine.Random.Range(0.03f, 0.08f); }
            else { _flick = 1f; _next = now + UnityEngine.Random.Range(0.12f, 1.1f); }
        }
        else
        {
            float r = UnityEngine.Random.value;
            if (r < 0.55f) { _flick = UnityEngine.Random.Range(0.3f, 0.6f); _next = now + UnityEngine.Random.Range(0.03f, 0.1f); }      // 暗一下
            else if (r < 0.85f) { _flick = 0.08f; _next = now + UnityEngine.Random.Range(0.04f, 0.15f); }                              // 灭一下
            else { _flick = 0.75f; _next = now + UnityEngine.Random.Range(0.2f, 0.5f); }                                              // 稍暗一会儿
        }
        Apply();
    }

    void Apply()
    {
        Color baseCol = Warning ? Yellow : Green;
        float f = Warning ? _flick : 1f;
        foreach (Target t in _targets)
        {
            if (t.g == null) continue;
            float a = t.alpha * f;
            if (t.seg != null || t.normalTex != null) t.g.color = new Color(1f, 1f, 1f, a); // 颜色画在贴图里
            else t.g.color = new Color(baseCol.r, baseCol.g, baseCol.b, a);
        }
    }
}
