using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 発表デモ用のコントローラー（MVP発表状態）。
//   - 1日 = 15秒で自動進行（AIDirector の secondsPerTurn を設定）。
//   - 第 maxDays 日（既定10日）に達したら、進行を止めて「デモ終了」画面を表示。
//   - 終了画面には、生存者数・経過日数などの簡単な統計を出す。
//
// ディレクターの回合推進とイベント生成は既存の AIDirector が担う。
// 本コントローラーは「デモの尺の管理」と「終了演出」だけを担当する。
public class DemoController : MonoBehaviour
{
    [Header("デモ設定")]
    public int maxDays = 10;              // 何日でデモ終了とするか
    public float secondsPerDay = 15f;     // 1日の長さ（秒）。AIDirectorに反映する。

    [Header("参照")]
    public AIDirector director;           // 未指定なら自動で探す

    private bool _ended = false;

    // 終了画面のUI（実行時に生成）。
    private Canvas _canvas;
    private CanvasGroup _group;
    private Text _statText;

    void Start()
    {
        if (director == null) director = FindObjectOfType<AIDirector>();

        // AIDirector の1回合の長さを、デモ用の秒数に設定する。
        if (director != null)
            director.secondsPerTurn = secondsPerDay;

        BuildEndUI();
    }

    void Update()
    {
        if (_ended) return;
        if (WorldManager.Instance == null || WorldManager.Instance.world == null) return;

        // 現在の日数が上限に達したら、デモ終了。
        if (WorldManager.Instance.world.day >= maxDays)
        {
            EndDemo();
        }
    }

    void EndDemo()
    {
        if (_ended) return;
        _ended = true;

        // ディレクターの自動進行を止める（これ以上日が進まないように）。
        if (director != null) director.enabled = false;

        // 統計を集計。
        int total = 0, alive = 0;
        if (WorldManager.Instance != null && WorldManager.Instance.world != null)
        {
            foreach (NpcData npc in WorldManager.Instance.world.npcs)
            {
                total++;
                if (npc.health > 0f) alive++;
            }
        }
        int days = WorldManager.Instance.world.day;

        if (_statText != null)
            _statText.text = $"経過日数：{days} 日\n生存者：{alive} / {total} 名";

        StartCoroutine(FadeInEnd());
        Debug.Log($"[DemoController] デモ終了：{days}日、生存 {alive}/{total}");
    }

    IEnumerator FadeInEnd()
    {
        for (float t = 0; t < 1.5f; t += Time.deltaTime)
        {
            if (_group != null) _group.alpha = Mathf.Clamp01(t / 1.5f);
            yield return null;
        }
        if (_group != null) _group.alpha = 1f;
    }

    // 「デモ終了」画面を実行時に作る（初期は透明）。
    void BuildEndUI()
    {
        GameObject canvasGo = new GameObject("DemoEndCanvas");
        canvasGo.transform.parent = transform;
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1100; // 最前面（GameOverより上でもよい）
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        // 背景（濃紺・半透明。世界観の色に寄せる）
        GameObject bgGo = new GameObject("End_BG");
        bgGo.transform.parent = canvasGo.transform;
        Image bg = bgGo.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.055f, 0.10f, 0.92f); // 深い紺
        RectTransform br = bg.rectTransform;
        br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one;
        br.offsetMin = Vector2.zero; br.offsetMax = Vector2.zero;

        // メインタイトル「デモ終了」
        GameObject titleGo = new GameObject("End_Title");
        titleGo.transform.parent = canvasGo.transform;
        Text title = titleGo.AddComponent<Text>();
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title.text = "デモ終了";
        title.fontSize = 64;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = new Color(0.31f, 0.66f, 1f); // アクセントの青
        RectTransform tr = title.rectTransform;
        tr.anchorMin = new Vector2(0.1f, 0.52f); tr.anchorMax = new Vector2(0.9f, 0.68f);
        tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;

        // 統計テキスト
        GameObject statGo = new GameObject("End_Stat");
        statGo.transform.parent = canvasGo.transform;
        _statText = statGo.AddComponent<Text>();
        _statText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _statText.text = "";
        _statText.fontSize = 30;
        _statText.alignment = TextAnchor.MiddleCenter;
        _statText.color = new Color(0.85f, 0.88f, 0.95f);
        _statText.lineSpacing = 1.4f;
        RectTransform sr = _statText.rectTransform;
        sr.anchorMin = new Vector2(0.1f, 0.36f); sr.anchorMax = new Vector2(0.9f, 0.5f);
        sr.offsetMin = Vector2.zero; sr.offsetMax = Vector2.zero;

        // 副文（世界観のひとこと）
        GameObject subGo = new GameObject("End_Sub");
        subGo.transform.parent = canvasGo.transform;
        Text sub = subGo.AddComponent<Text>();
        sub.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sub.text = "― 亜空間での記録は、ここまで ―";
        sub.fontSize = 22;
        sub.alignment = TextAnchor.MiddleCenter;
        sub.color = new Color(0.6f, 0.65f, 0.75f);
        RectTransform su = sub.rectTransform;
        su.anchorMin = new Vector2(0.1f, 0.28f); su.anchorMax = new Vector2(0.9f, 0.35f);
        su.offsetMin = Vector2.zero; su.offsetMax = Vector2.zero;
    }
}