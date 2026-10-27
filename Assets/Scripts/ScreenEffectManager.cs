using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 通用屏幕效果系统：提供「闪色」「相机震动」「横幅文字」三种效果。
// 各事件可组合调用，做出各自的视觉反馈。运行时自建所需UI，不依赖预先摆放。
//
// 【用法】
//   场景里建个空物体挂本脚本。事件里调用：
//     ScreenEffectManager.Instance.Flash(color, duration);
//     ScreenEffectManager.Instance.Shake(strength, duration);
//     ScreenEffectManager.Instance.Banner("空间扭曲", color);
//   或用组合快捷方法 PlayEventEffect(type)。
public class ScreenEffectManager : MonoBehaviour
{
    public static ScreenEffectManager Instance { get; private set; }

    private Canvas _canvas;          // 专用的顶层Canvas（盖在最上面）
    private Image _flashImage;       // 全屏闪色用的Image
    private Text _bannerText;        // 横幅文字
    private CanvasGroup _bannerGroup;

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
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        // 全屏闪色Image（初始透明，不挡点击）
        GameObject flashGo = new GameObject("FlashImage");
        flashGo.transform.parent = canvasGo.transform;
        _flashImage = flashGo.AddComponent<Image>();
        _flashImage.color = new Color(0, 0, 0, 0);
        _flashImage.raycastTarget = false;
        RectTransform fr = _flashImage.rectTransform;
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
        fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;

        // 横幅文字（屏幕中上方）
        GameObject bannerGo = new GameObject("BannerText");
        bannerGo.transform.parent = canvasGo.transform;
        _bannerText = bannerGo.AddComponent<Text>();
        _bannerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // 内置字体（兜底）
        _bannerText.fontSize = 44;
        _bannerText.alignment = TextAnchor.MiddleCenter;
        _bannerText.color = Color.white;
        _bannerText.raycastTarget = false;
        RectTransform br = _bannerText.rectTransform;
        br.anchorMin = new Vector2(0.1f, 0.7f); br.anchorMax = new Vector2(0.9f, 0.85f);
        br.offsetMin = Vector2.zero; br.offsetMax = Vector2.zero;
        _bannerGroup = bannerGo.AddComponent<CanvasGroup>();
        _bannerGroup.alpha = 0;
        _bannerGroup.blocksRaycasts = false;
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

    // 横幅文字：短暂显示一行大字。
    public void Banner(string text, Color color, float holdTime = 1.5f)
    {
        StartCoroutine(BannerCo(text, color, holdTime));
    }
    IEnumerator BannerCo(string text, Color color, float holdTime)
    {
        _bannerText.text = text;
        _bannerText.color = color;
        // 淡入
        for (float t = 0; t < 0.3f; t += Time.deltaTime)
        { _bannerGroup.alpha = t / 0.3f; yield return null; }
        _bannerGroup.alpha = 1;
        yield return new WaitForSeconds(holdTime);
        // 淡出
        for (float t = 0; t < 0.5f; t += Time.deltaTime)
        { _bannerGroup.alpha = 1 - t / 0.5f; yield return null; }
        _bannerGroup.alpha = 0;
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
                Banner(GetBannerText("event_banner_space_warp"), new Color(0.49f, 0.36f, 1f));
                break;

            case "time_disorder": // 时间失序：青白闪 + 轻微震动 + 横幅
                Flash(new Color(0.6f, 0.9f, 1f), 0.8f, 0.4f); // 青白
                Shake(0.08f, 0.3f);
                Banner(GetBannerText("event_banner_time_disorder"), new Color(0.31f, 0.66f, 1f));
                break;

            case "storm": // 亚空间风暴：铅灰闪 + 持续震动 + 横幅
                Flash(new Color(0.35f, 0.4f, 0.45f), 0.9f, 0.5f);
                Shake(0.18f, 1.2f);
                Banner(GetBannerText("event_banner_storm"), new Color(0.75f, 0.85f, 0.95f));
                break;

            case "whisper_vision": // 低语与幻象：暗紫黑雾 + 无震动 + 横幅
                Flash(new Color(0.15f, 0.05f, 0.2f), 1.2f, 0.55f); // 暗紫
                Banner(GetBannerText("event_banner_whisper_vision"), new Color(0.7f, 0.5f, 0.9f));
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
