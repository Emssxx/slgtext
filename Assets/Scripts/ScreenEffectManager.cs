using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 通用屏幕效果系统：提供「闪色」「相机震动」「横幅文字」三种效果。
// 各事件可组合调用，做出各自的视觉反馈。运行时自建所需UI，不依赖预先摆放。
//
// 【用法】
//   场景里建个空物体挂本脚本。事件里调用：
//     ScreenEffectManager.Instance.Flash(color, duration);
//     ScreenEffectManager.Instance.Shake(strength, duration);
//     ScreenEffectManager.Instance.Banner("空间扭曲", color);
//   或用组合快捷方法 PlayEventEffect(type)。
// 横幅是识别框样式（见 UiStyle）：红色细边 + 左上角红标签「anomaly : space_warp」+ 大字 + 一行等宽小字；
//   出现时先画一条横线，再从这条线上下展开。
public class ScreenEffectManager : MonoBehaviour
{
    public static ScreenEffectManager Instance { get; private set; }

    private Canvas _canvas;          // 专用的顶层Canvas（盖在最上面）
    private Image _flashImage;       // 全屏闪色用的Image
    private RectTransform _bannerBox; // 横幅的框
    private TextMeshProUGUI _bannerText;
    private TextMeshProUGUI _bannerSub;  // 框里下面那行等宽小字
    private TMP_Text _bannerTag;
    private Image _bannerFrame;
    private CanvasGroup _bannerGroup;
    private Coroutine _bannerCo;

    private Camera _cam;
    private Vector3 _camBasePos;
    private Coroutine _shakeCo;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void Start()
    {
        _cam = Camera.main;
        if (_cam != null) _camBasePos = _cam.transform.localPosition;
    }

    // 运行时创建一个顶层Canvas + 闪色Image + 横幅Text。
    void BuildUI()
    {
        // 顶层Canvas
        GameObject canvasGo = new GameObject("ScreenEffectCanvas");
        canvasGo.transform.parent = transform;
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 999; // 盖在所有UI之上
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); // 和其它界面同一个基准
        scaler.matchWidthOrHeight = 0.5f;

        // 全屏闪色Image（初始透明，不挡点击）
        GameObject flashGo = new GameObject("FlashImage");
        flashGo.transform.parent = canvasGo.transform;
        _flashImage = flashGo.AddComponent<Image>();
        _flashImage.color = new Color(0, 0, 0, 0);
        _flashImage.raycastTarget = false;
        RectTransform fr = _flashImage.rectTransform;
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
        fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;

        // 横幅（屏幕中上方）：深色半透明底 + 红色细边 + 左上角标签 + 大字 + 等宽小字
        GameObject bannerGo = new GameObject("Banner", typeof(RectTransform));
        bannerGo.transform.SetParent(canvasGo.transform, false);
        _bannerBox = (RectTransform)bannerGo.transform;
        _bannerBox.anchorMin = _bannerBox.anchorMax = new Vector2(0.5f, 0.76f);
        _bannerBox.pivot = new Vector2(0.5f, 0.5f);
        _bannerBox.sizeDelta = new Vector2(640f, 150f);
        Image bbg = bannerGo.AddComponent<Image>();
        bbg.color = new Color(UiStyle.Panel.r, UiStyle.Panel.g, UiStyle.Panel.b, 0.86f);
        bbg.raycastTarget = false;
        _bannerFrame = UiStyle.Frame(_bannerBox, UiStyle.Red, 2f);
        _bannerTag = UiStyle.Tag(_bannerBox, "anomaly : detected", UiStyle.Red, 16f);

        TMP_FontAsset jp = GameFonts.Find();
        _bannerText = BannerText("Text", jp, 46f, new Vector2(0f, -18f));
        _bannerText.fontStyle = FontStyles.Bold;
        _bannerSub = BannerText("Sub", UiStyle.Mono, 17f, new Vector2(0f, -62f));
        _bannerSub.color = UiStyle.RedText;

        _bannerGroup = bannerGo.AddComponent<CanvasGroup>();
        _bannerGroup.alpha = 0;
        _bannerGroup.blocksRaycasts = false;
        _bannerGroup.interactable = false;
    }

    TextMeshProUGUI BannerText(string name, TMP_FontAsset font, float size, Vector2 centerOffset)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_bannerBox, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = UiStyle.TextMain;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.richText = false;
        t.raycastTarget = false;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(-24f, size + 14f);
        rt.anchoredPosition = centerOffset + new Vector2(0f, 26f);
        return t;
    }

    // ============ 三种基础效果 ============

    // 屏幕闪色：color淡入到峰值透明度再淡出。
    public void Flash(Color color, float duration = 0.6f, float maxAlpha = 0.5f)
    {
        StartCoroutine(FlashCo(color, duration, maxAlpha));
    }
    IEnumerator FlashCo(Color color, float duration, float maxAlpha)
    {
        float half = duration * 0.5f;
        // 淡入
        for (float t = 0; t < half; t += Time.deltaTime)
        {
            float a = Mathf.Lerp(0, maxAlpha, t / half);
            _flashImage.color = new Color(color.r, color.g, color.b, a);
            yield return null;
        }
        // 淡出
        for (float t = 0; t < half; t += Time.deltaTime)
        {
            float a = Mathf.Lerp(maxAlpha, 0, t / half);
            _flashImage.color = new Color(color.r, color.g, color.b, a);
            yield return null;
        }
        _flashImage.color = new Color(color.r, color.g, color.b, 0);
    }

    // 相机震动：只算一个偏移量。有 CameraController（缩放 / 拖动）时由它叠加到镜头上；
    // 没有时这里直接加减偏移——都不会把镜头"复位"到开局位置（否则缩放、拖动过的画面会被拉回去）。
    public static Vector3 ShakeOffset { get; private set; }

    public void Shake(float strength = 0.15f, float duration = 0.4f)
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        if (_shakeCo != null) StopCoroutine(_shakeCo);
        SetShake(Vector3.zero);
        _shakeCo = StartCoroutine(ShakeCo(strength, duration));
    }
    IEnumerator ShakeCo(float strength, float duration)
    {
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float damper = 1f - (t / duration); // 逐渐减弱
            Vector2 off = Random.insideUnitCircle * strength * damper;
            SetShake(new Vector3(off.x, off.y, 0));
            yield return null;
        }
        SetShake(Vector3.zero); // 复位（只去掉偏移）
        _shakeCo = null;
    }

    void SetShake(Vector3 off)
    {
        if (CameraController.Instance == null && _cam != null)
            _cam.transform.localPosition += off - ShakeOffset;
        ShakeOffset = off;
    }

    // 横幅：短暂显示一个识别框（大字 = text）。color 用在大字的微光上（各事件自己的颜色）；
    // tag = 左上角标签里冒号后面的词（如 "space_warp"），不给就是 "detected"。
    public void Banner(string text, Color color, float holdTime = 1.5f, string tag = null)
    {
        if (_bannerCo != null) StopCoroutine(_bannerCo); // 连着来两个事件：后一个直接替换前一个
        _bannerCo = StartCoroutine(BannerCo(text, color, holdTime, tag));
    }
    IEnumerator BannerCo(string text, Color color, float holdTime, string tag)
    {
        _bannerText.text = text;
        _bannerText.color = Color.Lerp(UiStyle.TextMain, color, 0.35f);
        UiStyle.SetTag(_bannerTag, "anomaly : " + (string.IsNullOrEmpty(tag) ? "detected" : tag), UiStyle.Red);
        int day = WorldManager.Instance != null && WorldManager.Instance.world != null ? WorldManager.Instance.world.day : 0;
        _bannerSub.text = ">> ANOMALY DETECTED // DAY " + day.ToString("00");

        // 框的宽度跟着大字走
        float w = Mathf.Max(_bannerText.GetPreferredValues(text).x, _bannerSub.GetPreferredValues(_bannerSub.text).x) + 96f;
        w = Mathf.Max(w, UiStyle.TagWidth(_bannerTag) + 140f);
        _bannerBox.sizeDelta = new Vector2(Mathf.Clamp(w, 520f, 1500f), 150f);

        // 出现：先画一条横线（从中间往两边），再从横线上下展开，最后文字闪两下亮起来
        _bannerGroup.alpha = 1;
        _bannerText.alpha = 0f; _bannerSub.alpha = 0f;
        const float lineTime = 0.16f, openTime = 0.16f;
        for (float t = 0; t < lineTime; t += Time.unscaledDeltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / lineTime, 3f);
            _bannerBox.localScale = new Vector3(k, 0.03f, 1f);
            yield return null;
        }
        for (float t = 0; t < openTime; t += Time.unscaledDeltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / openTime, 3f);
            _bannerBox.localScale = new Vector3(1f, Mathf.Lerp(0.03f, 1f, k), 1f);
            yield return null;
        }
        _bannerBox.localScale = Vector3.one;
        float[] blink = { 1f, 0.2f, 1f, 0.4f, 1f };
        foreach (float a in blink)
        {
            _bannerText.alpha = a; _bannerSub.alpha = a;
            yield return new WaitForSecondsRealtime(0.04f);
        }

        yield return new WaitForSeconds(holdTime);
        // 淡出
        for (float t = 0; t < 0.5f; t += Time.deltaTime)
        { _bannerGroup.alpha = 1 - t / 0.5f; yield return null; }
        _bannerGroup.alpha = 0;
        _bannerCo = null;
    }

    // ============ 事件专用组合效果 ============
    // 事件里直接调这个，传事件type，播放对应的组合视觉。
    public void PlayEventEffect(string type)
    {
        switch (type)
        {
            case "space_warp": // 空间扭曲：紫色闪 + 较强震动 + 横幅
                Flash(new Color(0.49f, 0.36f, 1f), 0.5f, 0.45f); // 紫
                Shake(0.25f, 0.5f);
                Banner(GetBannerText("event_banner_space_warp"), new Color(0.49f, 0.36f, 1f), 1.5f, "space_warp");
                break;

            case "time_disorder": // 时间失序：青白闪 + 轻微震动 + 横幅
                Flash(new Color(0.6f, 0.9f, 1f), 0.8f, 0.4f); // 青白
                Shake(0.08f, 0.3f);
                Banner(GetBannerText("event_banner_time_disorder"), new Color(0.31f, 0.66f, 1f), 1.5f, "time_disorder");
                break;

            case "storm": // 亚空间风暴：铅灰闪 + 持续震动 + 横幅
                Flash(new Color(0.35f, 0.4f, 0.45f), 0.9f, 0.5f);
                Shake(0.18f, 1.2f);
                Banner(GetBannerText("event_banner_storm"), new Color(0.75f, 0.85f, 0.95f), 1.5f, "storm");
                break;

            case "creature_incursion": // 造物来袭：暗红闪 + 震动 + 横幅
                Flash(new Color(0.45f, 0.05f, 0.1f), 0.7f, 0.5f);
                Shake(0.15f, 0.6f);
                Banner(GetBannerText("event_banner_creature_incursion"), new Color(1f, 0.45f, 0.5f), 1.6f, "creature_incursion");
                break;

            case "raid": // 掠夺者来袭：橙闪 + 轻震 + 横幅
                Flash(new Color(0.9f, 0.45f, 0.1f), 0.5f, 0.4f);
                Shake(0.08f, 0.4f);
                Banner(GetBannerText("event_banner_raid"), new Color(1f, 0.7f, 0.35f), 1.6f, "raid");
                break;

            case "whisper_vision": // 低语与幻象：暗紫黑雾 + 无震动 + 横幅
                Flash(new Color(0.15f, 0.05f, 0.2f), 1.2f, 0.55f); // 暗紫
                Banner(GetBannerText("event_banner_whisper_vision"), new Color(0.7f, 0.5f, 0.9f), 1.5f, "whisper_vision");
                break;

            default:
                break;
        }
    }

    // 按当前语言取横幅文字（LocalizationManager 不存在时兜底返回 key本身，避免报空）。
    string GetBannerText(string key)
    {
        return LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    }
}
