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

    void Awake()
    {
        BuildUI();
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

        // 文字：锚定在屏幕右下角，字号调小（14）。
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
        _modelLabel.fontSize = 14;
        _modelLabel.color = Color.white;
        if (font != null) _modelLabel.font = font;
    }

    // 根据 AIClientHub 当前用的是本地还是云端，刷新右下角文字。
    void RefreshModelLabel()
    {
        if (_modelLabel == null || LocalizationManager.Instance == null) return;

        bool useCloud = AIClientHub.Instance != null && AIClientHub.Instance.useCloud;
        string prefix = LocalizationManager.Instance.Get(useCloud ? "ai_debug_label_cloud" : "ai_debug_label_local");

        string modelName = "?";
        if (useCloud && ClaudeAIClient.Instance != null) modelName = ClaudeAIClient.Instance.modelName;
        else if (!useCloud && OllamaAIClient.Instance != null) modelName = OllamaAIClient.Instance.modelName;

        _modelLabel.text = $"{prefix}: {modelName}";
    }
}
