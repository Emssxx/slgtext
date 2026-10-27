using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// メインメニュー（主界面）のコントローラー。
//   - タイトル表示
//   - ゲーム開始（ゲームシーンをロード）
//   - AIプロバイダー選択（本地Ollama / Claude / OpenAI / Gemini）
//   - 言語切替（中/日）
//   - マップシード（地图种子）：入力 / ランダム / おすすめ、右側にミニマップのプレビュー（MapSeed / TerrainGenerator）
//   - 終了
//
// UIは実行時に自動生成する（あとで自由に美術差し替え可能）。
//
// 【用法】
//   1. 新しいシーン「MainMenu」を作る。
//   2. 空物体に本スクリプトを付ける。
//   3. Build Settings に MainMenu と ゲームシーン の両方を登録。
//   4. gameSceneName に、あなたのゲームシーンの名前を入れる。
public class MainMenuController : MonoBehaviour
{
    [Header("ゲームシーン名（Build Settingsに登録した名前）")]
    public string gameSceneName = "GameScene"; // ←あなたのゲームシーン名に変更

    [Header("タイトル")]
    public string titleText = "亜空間サバイバル";
    public string subtitleText = "SUBSPACE SURVIVAL";

    // 色（世界観に合わせた深い紺＋青紫）
    private readonly Color BG = new Color(0.04f, 0.055f, 0.10f);
    private readonly Color ACCENT = new Color(0.31f, 0.66f, 1f);
    private readonly Color ACCENT2 = new Color(0.49f, 0.36f, 1f);
    private readonly Color TEXT = new Color(0.91f, 0.93f, 0.96f);
    private readonly Color SUB = new Color(0.6f, 0.65f, 0.75f);

    [Header("字体（内置字体不含中日文字，必须指定）")]
    public Font zhFont;   // 中文用：NotoSansSC-Regular.ttf
    public Font jaFont;   // 日文用：NotoSansJP-Regular.ttf

    private Text _providerLabel;
    private Text _langLabel;
    private GameObject _canvasGo;   // 当前UI根节点。切语言时销毁重建。

    // AIClientHub がまだ無い場合に備え、選択を覚えておく。
    private AIProvider _selectedProvider = AIProvider.LocalOllama;

    // —— 地图种子（マップシード）——
    private InputField _seedInput;
    private RawImage _seedPreview;
    private AspectRatioFitter _seedFit;
    private Text _seedInfo;
    private Texture2D _seedTex;
    private string _seedText;          // 切语言重建界面时保留
    private float _previewDelay = -1f; // 打字时等一会儿再刷新预览

    void Start()
    {
        // 既存の選択があれば引き継ぐ。
        if (AIClientHub.Instance != null) _selectedProvider = AIClientHub.Instance.provider;
        BuildUI();
    }

    void BuildUI()
    {
        // 重建前先销毁旧的UI，避免切语言时叠加出第二套界面。
        if (_canvasGo != null) Destroy(_canvasGo);

        // Canvas
        GameObject canvasGo = new GameObject("MainMenuCanvas");
        _canvasGo = canvasGo;
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        // 背景
        Image bg = NewImage(canvasGo.transform, "BG", BG);
        Stretch(bg.rectTransform);

        // 装飾の円（亜空間の渦イメージ）
        Image circle = NewImage(canvasGo.transform, "Circle", new Color(ACCENT2.r, ACCENT2.g, ACCENT2.b, 0.10f));
        SetRect(circle.rectTransform, new Vector2(0.62f, 0.55f), new Vector2(1.05f, 1.15f));

        // タイトル
        Text title = NewText(canvasGo.transform, "Title", titleText, 64, ACCENT, TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(title.rectTransform, new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.85f));
        // サブタイトル
        Text sub = NewText(canvasGo.transform, "Subtitle", subtitleText, 22, SUB, TextAnchor.MiddleLeft, FontStyle.Normal);
        SetRect(sub.rectTransform, new Vector2(0.105f, 0.66f), new Vector2(0.9f, 0.72f));

        // —— ボタン群 ——
        // 開始
        MakeButton(canvasGo.transform, "btn_start", GetLoc("btn_start", "ゲーム開始", "开始游戏"),
            new Vector2(0.1f, 0.50f), new Vector2(0.42f, 0.57f), ACCENT, OnStart);

        // AIプロバイダー選択（トグル式：押すたびに切り替え）
        _providerLabel = MakeButton(canvasGo.transform, "btn_provider", ProviderDisplay(),
            new Vector2(0.1f, 0.41f), new Vector2(0.42f, 0.48f), ACCENT2, OnCycleProvider);

        // 言語切替
        _langLabel = MakeButton(canvasGo.transform, "btn_lang", LangDisplay(),
            new Vector2(0.1f, 0.32f), new Vector2(0.42f, 0.39f), new Color(0.3f,0.35f,0.45f), OnToggleLang);

        // 終了
        MakeButton(canvasGo.transform, "btn_quit", GetLoc("btn_quit", "終了", "退出"),
            new Vector2(0.1f, 0.23f), new Vector2(0.42f, 0.30f), new Color(0.3f,0.2f,0.25f), OnQuit);

        // key設置の注意書き（クラウド選択時のみ意味がある）
        Text hint = NewText(canvasGo.transform, "Hint",
            GetLoc("menu_hint",
                "クラウドAIを使う場合は、各自のAPIキーが必要です。",
                "使用云端AI需要各自的API key。"),
            15, SUB, TextAnchor.LowerLeft, FontStyle.Normal);
        SetRect(hint.rectTransform, new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.12f));

        // 地图种子（右侧）
        BuildSeedPanel(canvasGo.transform);
    }

    // ============ 地图种子（マップシード）============
    // 同じシード → 同じ地形（湖・沼・木・石・鉱石の配置）。ゲーム開始時に MapSeed.Set() で保存し、ゲームシーンの TerrainSystem が読む。

    void BuildSeedPanel(Transform root)
    {
        if (string.IsNullOrEmpty(_seedText)) _seedText = MapSeed.NewRandom(); // 毎回新しいランダムシードから

        Image panel = NewImage(root, "SeedPanel", new Color(0.07f, 0.09f, 0.15f, 0.88f));
        SetRect(panel.rectTransform, new Vector2(0.50f, 0.16f), new Vector2(0.92f, 0.64f));
        Transform p = panel.transform;

        Text title = NewText(p, "SeedTitle", GetLoc("seed_title", "マップシード", "地图种子"), 22, ACCENT, TextAnchor.MiddleLeft, FontStyle.Bold);
        SetRect(title.rectTransform, new Vector2(0.05f, 0.87f), new Vector2(0.95f, 0.97f));

        // 入力欄 + ランダム
        _seedInput = MakeInput(p, _seedText, new Vector2(0.05f, 0.72f), new Vector2(0.68f, 0.84f));
        _seedInput.onValueChanged.AddListener(v => { _seedText = v; _previewDelay = 0.3f; });
        Text rnd = MakeButton(p, "btn_seed_random", GetLoc("seed_random", "ランダム", "随机"),
            new Vector2(0.70f, 0.72f), new Vector2(0.95f, 0.84f), ACCENT2, () => SetSeed(MapSeed.NewRandom()));
        rnd.fontSize = 18;

        // おすすめ / 前回のシード
        string last = MapSeed.Last;
        bool hasLast = !string.IsNullOrEmpty(last);
        int n = 4;
        string[] chips = new string[n];
        string[] labels = new string[n];
        int k = 0;
        if (hasLast) { chips[k] = last; labels[k] = GetLoc("seed_last", "前回", "上次"); k++; }
        for (int i = 0; k < n && i < MapSeed.Presets.Length; i++) { chips[k] = MapSeed.Presets[i]; labels[k] = MapSeed.Presets[i]; k++; }
        float x0 = 0.05f, w = 0.90f / n;
        for (int i = 0; i < k; i++)
        {
            string seed = chips[i];
            Color c = hasLast && i == 0 ? new Color(0.35f, 0.3f, 0.5f) : new Color(0.22f, 0.27f, 0.38f);
            Text chip = MakeButton(p, "btn_seed_" + i, labels[i], new Vector2(x0 + i * w + 0.006f, 0.60f), new Vector2(x0 + (i + 1) * w - 0.006f, 0.69f), c, () => SetSeed(seed));
            chip.fontSize = 14;
        }

        // ミニマップ（1 マス = 1 ピクセル）
        Image frame = NewImage(p, "PreviewFrame", new Color(1f, 1f, 1f, 0.10f));
        SetRect(frame.rectTransform, new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.57f));
        GameObject pv = new GameObject("Preview");
        pv.transform.SetParent(frame.transform, false);
        _seedPreview = pv.AddComponent<RawImage>();
        _seedPreview.raycastTarget = false;
        RectTransform prt = _seedPreview.rectTransform;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(3f, 3f); prt.offsetMax = new Vector2(-3f, -3f);
        _seedFit = pv.AddComponent<AspectRatioFitter>();
        _seedFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _seedFit.aspectRatio = 72f / 33f;

        _seedInfo = NewText(p, "SeedInfo", "", 14, SUB, TextAnchor.MiddleLeft, FontStyle.Normal);
        SetRect(_seedInfo.rectTransform, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.15f));

        RefreshSeedPreview();
    }

    InputField MakeInput(Transform parent, string value, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject go = new GameObject("SeedInput");
        go.transform.SetParent(parent, false);
        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.02f, 0.03f, 0.06f, 0.95f);
        SetRect(bg.rectTransform, anchorMin, anchorMax);
        InputField f = go.AddComponent<InputField>();

        Text txt = NewText(go.transform, "Text", "", 20, TEXT, TextAnchor.MiddleLeft, FontStyle.Normal);
        txt.supportRichText = false;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap; // 入力欄の文字は InputField が自分でスクロールさせる
        Stretch(txt.rectTransform);
        txt.rectTransform.offsetMin = new Vector2(10f, 2f); txt.rectTransform.offsetMax = new Vector2(-10f, -2f);

        Text ph = NewText(go.transform, "Placeholder", GetLoc("seed_placeholder", "シードを入力（空欄＝ランダム）", "输入种子（留空 = 随机）"), 16, SUB, TextAnchor.MiddleLeft, FontStyle.Italic);
        Stretch(ph.rectTransform);
        ph.rectTransform.offsetMin = new Vector2(10f, 2f); ph.rectTransform.offsetMax = new Vector2(-10f, -2f);

        f.targetGraphic = bg;
        f.textComponent = txt;
        f.placeholder = ph;
        f.lineType = InputField.LineType.SingleLine;
        f.characterLimit = 32;
        f.text = value;
        return f;
    }

    void SetSeed(string seed)
    {
        _seedText = seed;
        if (_seedInput != null) _seedInput.text = seed; // onValueChanged でもう一度予約されるが、すぐ描き直しておく
        RefreshSeedPreview();
    }

    void Update()
    {
        if (_previewDelay < 0f) return;
        _previewDelay -= Time.unscaledDeltaTime;
        if (_previewDelay <= 0f) { _previewDelay = -1f; RefreshSeedPreview(); }
    }

    void RefreshSeedPreview()
    {
        if (_seedPreview == null || _seedInfo == null) return;
        try
        {
            string seed = MapSeed.Normalize(_seedText);
            if (string.IsNullOrEmpty(seed))
            {
                _seedPreview.enabled = false;
                _seedInfo.text = GetLoc("seed_empty", "空欄のまま始めるとランダムなマップになる。", "留空开始 = 随机地图。");
                return;
            }
            TerrainMap map = MapSeed.Preview(seed);
            _seedTex = TerrainArt.Preview(map, _seedTex);
            _seedPreview.texture = _seedTex;
            _seedPreview.enabled = true;
            if (_seedFit != null) _seedFit.aspectRatio = map.width / (float)Mathf.Max(1, map.height);
            _seedInfo.text = TerrainArt.Summary(map, IsJp);
        }
        catch (System.Exception e) { Debug.LogWarning("[MainMenu] シードのプレビューに失敗：" + e.Message); }
    }

    bool IsJp => LocalizationManager.Instance != null && LocalizationManager.Instance.current == Language.Japanese;

    // ============ ボタンのアクション ============

    void OnStart()
    {
        // 選んだ provider を Hub に反映（Hubは DontDestroyOnLoad なのでゲームへ引き継がれる）。
        if (AIClientHub.Instance != null)
            AIClientHub.Instance.SetProvider(_selectedProvider);

        // マップシードを保存（空欄ならランダム）→ ゲームシーンの TerrainSystem がこれで地形を作る
        MapSeed.Set(_seedInput != null ? _seedInput.text : _seedText);

        if (string.IsNullOrEmpty(gameSceneName))
        { Debug.LogError("[MainMenu] gameSceneName が未設定"); return; }
        SceneManager.LoadScene(gameSceneName);
    }

    void OnCycleProvider()
    {
        // 4つを順番に切り替え。
        _selectedProvider = (AIProvider)(((int)_selectedProvider + 1) % 4);
        if (AIClientHub.Instance != null) AIClientHub.Instance.SetProvider(_selectedProvider);
        if (_providerLabel != null) _providerLabel.text = ProviderDisplay();
    }

    void OnToggleLang()
    {
        if (LocalizationManager.Instance != null) LocalizationManager.Instance.Toggle();
        // UIを丸ごと作り直す：字体も文言も新しい言語に切り替わる。
        // （ラベルだけ差し替えると、タイトルや他のボタンが古い言語のまま残る）
        BuildUI();
    }

    void OnQuit()
    {
        Debug.Log("[MainMenu] 終了");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ============ 表示文字列 ============

    string ProviderDisplay()
    {
        string name;
        switch (_selectedProvider)
        {
            case AIProvider.Claude: name = "Claude"; break;
            case AIProvider.OpenAI: name = "ChatGPT"; break;
            case AIProvider.Gemini: name = "Gemini"; break;
            default:                name = GetLoc("provider_local", "ローカル", "本地"); break;
        }
        string prefix = GetLoc("provider_prefix", "AI：", "AI：");
        return prefix + name;
    }

    string LangDisplay()
    {
        // 「押すと切り替わる先」を表示。
        bool isJp = LocalizationManager.Instance != null
                    && LocalizationManager.Instance.current == Language.Japanese;
        return isJp ? "言語：日本語 / 中文" : "语言：中文 / 日本語";
    }

    // ローカライズ取得（LocalizationManager にキーがあればそれを、無ければ引数のフォールバックを使う）。
    string GetLoc(string key, string ja, string zh)
    {
        if (LocalizationManager.Instance != null)
        {
            string v = LocalizationManager.Instance.Get(key);
            if (v != key) return v; // 見つかった
        }
        bool isJp = LocalizationManager.Instance != null
                    && LocalizationManager.Instance.current == Language.Japanese;
        return isJp ? ja : zh;
    }

    // ============ UI生成ヘルパー ============

    Text MakeButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name);
        go.transform.parent = parent;
        Image img = go.AddComponent<Image>();
        img.color = new Color(color.r, color.g, color.b, 0.9f);
        SetRect(img.rectTransform, anchorMin, anchorMax);

        Button btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        Text txt = NewText(go.transform, "Label", label, 26, TEXT, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(txt.rectTransform);
        return txt;
    }

    Image NewImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.parent = parent;
        Image img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    // 按当前语言取字体：日文用 jaFont，中文用 zhFont。
    // 两种字体的汉字字形不同（如「间」/「間」），分开才正确。
    // 没在 Inspector 指定时退回内置字体（只能显示英文，用于兜底提醒）。
    Font CurrentFont()
    {
        bool isJp = LocalizationManager.Instance != null
                    && LocalizationManager.Instance.current == Language.Japanese;
        Font f = isJp ? jaFont : zhFont;
        if (f == null) f = zhFont != null ? zhFont : jaFont;   // 只配了一个就都用它
        if (f == null)
        {
            Debug.LogWarning("[MainMenu] 未指定 zhFont/jaFont，中日文字将无法显示。");
            f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return f;
    }

    Text NewText(Transform parent, string name, string content, int size, Color color, TextAnchor anchor, FontStyle style)
    {
        GameObject go = new GameObject(name);
        go.transform.parent = parent;
        Text t = go.AddComponent<Text>();
        t.font = CurrentFont();
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.fontStyle = style;
        // 【重要】溢出模式必须设为 Overflow。
        // legacy Text 默认 verticalOverflow=Truncate：只要一行文字的行高
        // 超过框的高度，Unity 就把整行【完全不画出来】（不是裁一半）。
        // 本界面标题字号64、框高只有52，默认设置下会一个字都看不见。
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }
    void SetRect(RectTransform r, Vector2 min, Vector2 max)
    {
        r.anchorMin = min; r.anchorMax = max;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }
}
