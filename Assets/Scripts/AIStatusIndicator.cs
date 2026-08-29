using UnityEngine;
using UnityEngine.UI;

// AI 状态インジケーター：画面隅に、現在の AI 状態を小さく表示する。
//   - 今どちらを使っているか（ローカル Ollama / クラウド Claude）
//   - クラウドの API key が読み込めているか
//
// 打包後は Unity の Console が見えないため、この表示で
// 「key が読めているか」「今どちらを使っているか」を目で確認できる。
//
// 【用法】場面に空物体を作り、本スクリプトを付けるだけ。UIは実行時に自動生成。
public class AIStatusIndicator : MonoBehaviour
{
    [Header("表示設定")]
    public bool showIndicator = true;   // 表示するか（発表本番で隠したい時は false）
    public int fontSize = 18;

    private Text _text;

    void Start()
    {
        BuildUI();
    }

    void Update()
    {
        if (_text == null) return;
        _text.enabled = showIndicator;
        if (!showIndicator) return;

        _text.text = BuildStatusString();
    }

    string BuildStatusString()
    {
        if (AIClientHub.Instance == null) return "AI: 未初期化";

        AIProvider p = AIClientHub.Instance.provider;
        string mode = AIClientHub.Instance.CurrentProviderName();

        // ローカルは key 不要。
        if (p == AIProvider.LocalOllama)
            return $"AI: {mode}";

        // クラウド各社の key 状態を見る。
        bool hasKey = false;
        bool clientExists = true;
        switch (p)
        {
            case AIProvider.Claude:
                clientExists = ClaudeAIClient.Instance != null;
                hasKey = clientExists && ClaudeAIClient.Instance.HasApiKey;
                break;
            case AIProvider.OpenAI:
                clientExists = OpenAIClient.Instance != null;
                hasKey = clientExists && OpenAIClient.Instance.HasApiKey;
                break;
            case AIProvider.Gemini:
                clientExists = GeminiAIClient.Instance != null;
                hasKey = clientExists && GeminiAIClient.Instance.HasApiKey;
                break;
        }

        string keyState = !clientExists ? "クライアント無し"
                        : hasKey ? "APIキー：読込OK"
                        : "APIキー：未検出";

        return $"AI: {mode}   |   {keyState}";
    }

    void BuildUI()
    {
        GameObject canvasGo = new GameObject("AIStatusCanvas");
        canvasGo.transform.parent = transform;
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        GameObject txtGo = new GameObject("AIStatusText");
        txtGo.transform.parent = canvasGo.transform;
        _text = txtGo.AddComponent<Text>();
        _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _text.fontSize = fontSize;
        _text.alignment = TextAnchor.LowerRight;
        _text.color = new Color(0.6f, 0.7f, 0.85f, 0.9f);
        _text.raycastTarget = false;

        // 右下に配置。
        RectTransform r = _text.rectTransform;
        r.anchorMin = new Vector2(0.5f, 0f);
        r.anchorMax = new Vector2(1f, 0.05f);
        r.offsetMin = new Vector2(0, 6);
        r.offsetMax = new Vector2(-12, 30);
    }
}
