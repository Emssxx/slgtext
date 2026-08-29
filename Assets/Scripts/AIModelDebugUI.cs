using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 【AI模型调试UI】右下角显示当前用的是本地Ollama还是云端Claude（含具体模型名），
// 纯展示，不提供切换功能。
public class AIModelDebugUI : MonoBehaviour
{
    [Header("字体（拖入 NotoSansSC，保证中日文都能显示）")]
    public TMP_FontAsset font;

    // 右下角显示"本地/云端 + 具体模型名"的文字组件。
    private TextMeshProUGUI _modelLabel;

    // 上一次显示时用的 provider 和模型名。用来判断"要不要重新刷新文字"，
    // 避免每帧都拼字符串（那会产生垃圾内存）。
    private AIProvider _lastProvider;
    private string _lastModelName = null;

    void Awake()
    {
        BuildUI();
        RefreshModelLabel();
    }

    void OnEnable()
    {
        // 切换中/日文时，前缀（本地/云端）也要跟着重刷。
        LocalizationManager.OnLanguageChanged += RefreshModelLabel;
    }

    void OnDisable()
    {
        // 物体销毁时取消订阅，防止残留引用报错。
        LocalizationManager.OnLanguageChanged -= RefreshModelLabel;
    }

    void Update()
    {
        // 当前 Hub 选的是哪家（Hub 还没初始化时按本地算）。
        AIProvider now = AIClientHub.Instance != null ? AIClientHub.Instance.provider : AIProvider.LocalOllama;

        // 两种情况需要重刷：
        //   1) provider 变了（Inspector 里改了下拉、或运行中调了 SetProvider）
        //   2) 上次没取到模型名（显示成 "?"，说明那时客户端还没 Awake 好），要再试一次
        if (now != _lastProvider || _lastModelName == "?")
            RefreshModelLabel();
    }

    // ============ 搭建UI：右下角的模型名文字 ============
    void BuildUI()
    {
        // 专用Canvas，盖在最上层。
        GameObject canvasGo = new GameObject("AIModelDebugCanvas");
        canvasGo.transform.parent = transform;
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        // 文字：锚定在屏幕右下角，字号调小（12）。
        GameObject labelGo = new GameObject("ModelLabel");
        labelGo.transform.parent = canvasGo.transform;
        RectTransform labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(1, 0); // 锚点定在右下角
        labelRt.anchorMax = new Vector2(1, 0);
        labelRt.pivot = new Vector2(1, 0);
        labelRt.anchoredPosition = new Vector2(-20, 20); // 离右下角边缘留一点距离
        labelRt.sizeDelta = new Vector2(220, 30);

        _modelLabel = labelGo.AddComponent<TextMeshProUGUI>();
        _modelLabel.alignment = TextAlignmentOptions.Right;
        _modelLabel.fontSize = 12;
        _modelLabel.color =  new Color(0.68f, 0.85f, 1f, 0.85f);
        if (font != null) _modelLabel.font = font;
    }

    // 根据 AIClientHub 当前用的是本地还是云端，刷新右下角文字。
    void RefreshModelLabel()
    {
        if (_modelLabel == null || LocalizationManager.Instance == null) return;

        // Hub 现在用枚举 provider（LocalOllama / Claude / OpenAI / Gemini），不再是 useCloud 布尔。
        // 没有 Hub 时按「本地」显示，保证不报空。
        AIProvider provider = AIClientHub.Instance != null ? AIClientHub.Instance.provider : AIProvider.LocalOllama;
        bool useCloud = provider != AIProvider.LocalOllama;   // 除本地 Ollama 外，其余三家都算云端
        string prefix = LocalizationManager.Instance.Get(useCloud ? "ai_debug_label_cloud" : "ai_debug_label_local");

        // 按当前 provider 取对应客户端的模型名；客户端还没挂上时显示 "?"。
        string modelName = "?";
        switch (provider)
        {
            case AIProvider.Claude:
                if (ClaudeAIClient.Instance != null) modelName = ClaudeAIClient.Instance.modelName;
                break;
            case AIProvider.OpenAI:
                if (OpenAIClient.Instance != null) modelName = OpenAIClient.Instance.modelName;
                break;
            case AIProvider.Gemini:
                if (GeminiAIClient.Instance != null) modelName = GeminiAIClient.Instance.modelName;
                break;
            default: // LocalOllama
                if (OllamaAIClient.Instance != null) modelName = OllamaAIClient.Instance.modelName;
                break;
        }

        _modelLabel.text = $"{prefix}: {modelName}";

        // 记下这次用的 provider 和模型名，供 Update 判断下次要不要再刷。
        _lastProvider = provider;
        _lastModelName = modelName;
    }
}
