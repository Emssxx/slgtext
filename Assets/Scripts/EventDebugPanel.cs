using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 【临时调试工具】测试用面板：右上角摆3个按钮，点一下就触发对应的AI导演事件，
// 方便测试 ScreenEffectManager 的视觉效果（闪色/震动/横幅）好不好使。
// 注意：这不是正式功能，测试完毕后应该把这个脚本、场景里的物体、
// 以及 LocalizationManager.cs 里对应的3个翻译key一起删掉。
public class EventDebugPanel : MonoBehaviour
{
    [Header("字体（Inspector 拖入 NotoSansSC，和项目其它UI保持一致，支持中日文显示）")]
    public TMP_FontAsset font;

    void Awake()
    {
        BuildUI();
    }

    // 运行时自己搭UI：一个Canvas + 一个竖直排列的按钮容器 + 3个按钮。
    void BuildUI()
    {
        // 1. 专用Canvas，盖在最上层（比ScreenEffectManager的999再高一点，确保按钮点得到、不被闪色层挡住）。
        GameObject canvasGo = new GameObject("EventDebugCanvas");
        canvasGo.transform.parent = transform;
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>(); // 按钮要能被点击，必须有这个组件

        // 2. 按钮容器，锚定在屏幕右上角，纵向排列3个按钮。
        GameObject containerGo = new GameObject("ButtonContainer");
        containerGo.transform.parent = canvasGo.transform;
        RectTransform containerRt = containerGo.AddComponent<RectTransform>();
        containerRt.anchorMin = new Vector2(1, 1); // 锚点定在右上角
        containerRt.anchorMax = new Vector2(1, 1);
        containerRt.pivot = new Vector2(1, 1);
        containerRt.anchoredPosition = new Vector2(-20, -20); // 离右上角边缘留一点距离
        containerRt.sizeDelta = new Vector2(180, 160);

        VerticalLayoutGroup layout = containerGo.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8;                    // 按钮之间的间距
        layout.childForceExpandWidth = true;   // 按钮宽度撑满容器
        layout.childForceExpandHeight = false; // 按钮高度用自己的，不被拉伸
        // 关键：必须打开这两个，VerticalLayoutGroup 才会真正按下面LayoutElement给的尺寸摆放子物体，
        // 否则子物体的可点击范围（Image实际大小）可能和肉眼看到的文字位置对不上，
        // 导致文字挤在一起、点击点不中按钮（会穿透到按钮下面的网格）。
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        // 3. 三个按钮，分别对应三个事件类型（type要和EventExecutor.ExecuteEvent里的case一致）。
        CreateEventButton(containerGo.transform, "space_warp", "debug_space_warp");
        CreateEventButton(containerGo.transform, "time_disorder", "debug_time_disorder");
        CreateEventButton(containerGo.transform, "whisper_vision", "debug_whisper_vision");
    }

    // 建一个按钮：文字用本地化key（跟随游戏当前语言），点击触发对应事件类型。
    void CreateEventButton(Transform parent, string eventType, string localizedKey)
    {
        // 按钮本体（带背景色的Image + Button组件）
        GameObject btnGo = new GameObject("Btn_" + eventType);
        btnGo.transform.parent = parent;
        RectTransform rt = btnGo.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(180, 40); // 按钮大小（下面的LayoutElement会覆盖实际布局尺寸，这里保留做兜底）

        Image bg = btnGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f); // 半透明黑底，看得清按钮范围又不太抢眼

        Button button = btnGo.AddComponent<Button>();

        // 明确告诉 VerticalLayoutGroup 这个按钮该多大，避免它自己瞎猜导致尺寸和显示对不上。
        LayoutElement layoutElement = btnGo.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = 180;
        layoutElement.preferredHeight = 40;
        layoutElement.minHeight = 40;

        // 按钮上的文字
        GameObject textGo = new GameObject("Text");
        textGo.transform.parent = btnGo.transform;
        RectTransform textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one; // 铺满整个按钮
        textRt.offsetMin = Vector2.zero; textRt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 20;
        tmp.color = Color.white;
        if (font != null) tmp.font = font; // 用拖入的中日文字体，避免方块字

        // 用项目已有的 LocalizedText 组件，让按钮文字自动跟随语言切换，不用自己写监听逻辑。
        LocalizedText loc = textGo.AddComponent<LocalizedText>();
        loc.key = localizedKey;

        // 点击按钮时，调用 EventExecutor 触发对应事件。
        button.onClick.AddListener(() =>
        {
            if (EventExecutor.Instance != null)
                EventExecutor.Instance.ExecuteEvent(eventType);
        });
    }
}
