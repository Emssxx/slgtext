using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================
// 界面统一样式（"ABA 监控终端"风格，和企划书同一套）：
//   · 识别框：深色半透明底 + 细边框（Outline），直角；
//   · 标签：贴在框左上角的实心色块，里面是等宽字体的小写文字，如「event_log : day_12」；
//   · 颜色：绿 = 常态 / 在线，红 = 异常 / 警告，黄 = 注意，灰 = 中性；系统行用「>> 」开头。
// 等宽字体：Resources/Fonts/CourierPrime-Regular.ttf（SIL OFL 开源协议，可随游戏发布），
//           运行时生成 TMP 字体；中日文字符自动回退到游戏现有的中日文字体。
// 对话窗口、悬停浮窗、坐标浮窗、事件 / 日志抽屉、事件横幅都用这里的颜色和 Tag()。
// ============================================================================
public static class UiStyle
{
    public static readonly Color Green = new Color(0.72f, 1f, 0.78f, 1f);      // #B8FFC7 和天数数字同色
    public static readonly Color Red = new Color(0.91f, 0.2f, 0.17f, 1f);       // #E8322B 标签 / 边框用
    public static readonly Color RedText = new Color(1f, 0.42f, 0.38f, 1f);     // 红色文字（深底上更好读）
    public static readonly Color Yellow = new Color(1f, 0.89f, 0.42f, 1f);
    public static readonly Color Gray = new Color(0.56f, 0.58f, 0.56f, 1f);     // #8E9490
    public static readonly Color Ink = new Color(0.043f, 0.047f, 0.055f, 1f);   // 标签上的深色字
    public static readonly Color Panel = new Color(0.04f, 0.045f, 0.05f, 0.9f); // 框的底色
    public static readonly Color TextMain = new Color(0.9f, 0.91f, 0.89f, 1f);
    public static readonly Color TextSub = new Color(0.76f, 0.79f, 0.77f, 1f);

    public const float TagFont = 13f;
    public const float TagH = 20f;     // 标签高度（字号 13 时）

    static TMP_FontAsset _mono;
    static bool _monoTried;

    // 等宽字体（找不到就用游戏的中日文字体，样式照样成立，只是不是等宽）。
    public static TMP_FontAsset Mono
    {
        get
        {
            if (!_monoTried)
            {
                _monoTried = true;
                Font f = Resources.Load<Font>("Fonts/CourierPrime-Regular");
                if (f != null)
                {
                    try { _mono = TMP_FontAsset.CreateFontAsset(f); }
                    catch (System.Exception e) { Debug.LogWarning("[UiStyle] 等宽字体生成失败，改用默认字体：" + e.Message); _mono = null; }
                }
                if (_mono != null)
                {
                    _mono.name = "CourierPrime (runtime)";
                    TMP_FontAsset jp = GameFonts.Find();
                    if (jp != null)
                    {
                        if (_mono.fallbackFontAssetTable == null) _mono.fallbackFontAssetTable = new List<TMP_FontAsset>();
                        _mono.fallbackFontAssetTable.Add(jp);   // 中日文、符号回退到原来的字体
                    }
                }
            }
            return _mono != null ? _mono : GameFonts.Find();
        }
    }

    // 空心方框：贴满 box 的一圈细线（9 宫格图，中间不画）。半透明的框用这个描边——
    // Outline 会在底下多画几份整块图，半透明底会被染上边框色。返回这圈线，之后可以改颜色 / 透明度。
    public static Image Frame(RectTransform box, Color c, float width = 1.5f)
    {
        GameObject go = new GameObject("Frame", typeof(RectTransform));
        go.transform.SetParent(box, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        Image img = go.AddComponent<Image>();
        img.sprite = FrameSprite();
        img.type = Image.Type.Sliced;
        img.fillCenter = false;
        img.pixelsPerUnitMultiplier = FrameLine / Mathf.Max(0.5f, width); // 线宽 = width 个界面单位
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    const int FrameLine = 4;   // 贴图里线条的像素宽
    static Sprite _frameSprite;

    static Sprite FrameSprite()
    {
        if (_frameSprite != null) return _frameSprite;
        const int N = FrameLine * 3;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                bool edge = x < FrameLine || y < FrameLine || x >= N - FrameLine || y >= N - FrameLine;
                px[y * N + x] = new Color32(255, 255, 255, (byte)(edge ? 255 : 0));
            }
        t.SetPixels32(px);
        t.Apply();
        _frameSprite = Sprite.Create(t, new UnityEngine.Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0,
                                     SpriteMeshType.FullRect, new Vector4(FrameLine, FrameLine, FrameLine, FrameLine));
        return _frameSprite;
    }

    // 给一个 Image 加细边框（Outline），返回它，之后可以改颜色。只用在不透明的底上。
    public static Outline Border(Graphic g, Color c, float width = 1.5f)
    {
        Outline o = g.gameObject.AddComponent<Outline>();
        o.effectColor = c;
        o.effectDistance = new Vector2(width, -width);
        return o;
    }

    // 在框的左上角贴一个标签（实心色块 + 等宽小字），宽度跟着文字走。返回标签上的文字。
    public static TextMeshProUGUI Tag(RectTransform box, string text, Color fill, float size = TagFont)
    {
        GameObject go = new GameObject("Tag", typeof(RectTransform));
        go.transform.SetParent(box, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(60f, size + 7f);
        Image img = go.AddComponent<Image>();
        img.color = fill;
        img.raycastTarget = false;

        GameObject tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(rt, false);
        TextMeshProUGUI t = tgo.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = Mono;
        if (f != null) t.font = f;
        t.fontSize = size;
        t.color = Ink;
        t.alignment = TextAlignmentOptions.Left;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.richText = false;
        t.raycastTarget = false;
        RectTransform tr = t.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(6f, 0f);
        tr.offsetMax = new Vector2(-4f, 0f);
        SetTag(t, text, fill);
        return t;
    }

    // 改标签的文字（宽度跟着变）和底色。
    public static void SetTag(TMP_Text t, string text, Color fill)
    {
        if (t == null) return;
        RectTransform rt = t.transform.parent as RectTransform;
        if (t.text != text)
        {
            t.text = text;
            if (rt != null) rt.sizeDelta = new Vector2(Mathf.Ceil(t.GetPreferredValues(text).x) + 12f, rt.sizeDelta.y);
        }
        Image img = rt != null ? rt.GetComponent<Image>() : null;
        if (img != null) img.color = fill;
    }

    // 重新量一次标签宽度：标签建在没激活的物体下面时，第一次量出来是 0（只露出第一个字母），激活之后要再量。
    public static void FitTag(TMP_Text t)
    {
        if (t == null) return;
        RectTransform rt = t.transform.parent as RectTransform;
        if (rt == null) return;
        t.ForceMeshUpdate();
        float w = Mathf.Ceil(t.GetPreferredValues(t.text).x) + 12f;
        if (w > 13f) rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
    }

    // 标签现在有多宽（框的最小宽度要比它大）。
    public static float TagWidth(TMP_Text t)
    {
        RectTransform rt = t != null ? t.transform.parent as RectTransform : null;
        return rt != null ? rt.sizeDelta.x : 0f;
    }
}
