using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// NPC 死亡处理 + 全灭判定（MVP 用）。
//   1. 检测数据里死亡（health<=0）的 NPC，让其场景物体淡出后消失。
//   2. 当全员死亡时，触发 Game Over。
//
// 死亡判定本身已由 WorldRules 处理（health<=0 → currentAction="已死亡"）。
// 本脚本负责「场景表现（淡出消失）」和「全灭 → Game Over」。
public class NpcDeathManager : MonoBehaviour
{
    [Header("淡出设置")]
    public float fadeDuration = 1.5f;   // 淡出时长（秒）

    [Header("检查频率")]
    public float checkInterval = 0.5f;  // 每隔多久检查一次死亡（秒）

    private float _timer;
    // 已经在淡出/已处理过的 NPC id，避免重复处理。
    private readonly HashSet<string> _handled = new HashSet<string>();
    private bool _gameOver = false;

    // Game Over 的 UI（运行时创建）。
    private Canvas _canvas;
    private CanvasGroup _goGroup;

    void Start()
    {
        BuildGameOverUI();
    }

    void Update()
    {
        if (_gameOver) return;
        _timer += Time.deltaTime;
        if (_timer < checkInterval) return;
        _timer = 0f;
        CheckDeaths();
    }

    void CheckDeaths()
    {
        if (WorldManager.Instance == null || WorldManager.Instance.world == null) return;
        List<NpcData> npcs = WorldManager.Instance.world.npcs;
        if (npcs == null || npcs.Count == 0) return;

        int deadCount = 0;

        foreach (NpcData npc in npcs)
        {
            bool isDead = npc.health <= 0f;
            if (isDead) deadCount++;

            // 新死亡且还没处理过 → 让场景物体淡出消失。
            if (isDead && !_handled.Contains(npc.id))
            {
                _handled.Add(npc.id);
                StartFadeOut(npc.id);
            }
        }

        // 全员死亡 → Game Over。
        if (deadCount >= npcs.Count && npcs.Count > 0)
        {
            TriggerGameOver();
        }
    }

    // 让某个 NPC 的场景物体淡出后销毁。
    void StartFadeOut(string npcId)
    {
        NpcMover mover = NpcView.GetMover(npcId);
        if (mover == null) return; // 场景里没有这个物体（可能已消失）
        StartCoroutine(FadeOutCo(mover.gameObject));
    }

    IEnumerator FadeOutCo(GameObject go)
    {
        // 收集这个 NPC 物体（及其子物体）上所有 SpriteRenderer，一起淡出。
        SpriteRenderer[] sprites = go.GetComponentsInChildren<SpriteRenderer>();

        // 停止它的移动/导航（死了就别再走了）。
        var agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, t / fadeDuration);
            foreach (var sr in sprites)
            {
                if (sr == null) continue;
                Color c = sr.color;
                c.a = a;
                sr.color = c;
            }
            yield return null;
        }

        // 淡出完毕，销毁场景物体（数据保留在 world.npcs 里，标记为已死亡）。
        Destroy(go);
    }

    // 触发 Game Over。
    void TriggerGameOver()
    {
        if (_gameOver) return;
        _gameOver = true;
        Debug.Log("[NpcDeathManager] 全員死亡 → Game Over");
        StartCoroutine(ShowGameOverCo());
    }

    IEnumerator ShowGameOverCo()
    {
        // 淡入 Game Over 画面。
        for (float t = 0; t < 1.2f; t += Time.deltaTime)
        {
            if (_goGroup != null) _goGroup.alpha = Mathf.Clamp01(t / 1.2f);
            yield return null;
        }
        if (_goGroup != null) _goGroup.alpha = 1f;
        // （以后可在这里加：显示存活天数、重新开始按钮等。）
    }

    // 运行时创建一个全屏 Game Over 画面（初始透明）。
    void BuildGameOverUI()
    {
        GameObject canvasGo = new GameObject("GameOverCanvas");
        canvasGo.transform.parent = transform;
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1000; // 盖在最上面
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        _goGroup = canvasGo.AddComponent<CanvasGroup>();
        _goGroup.alpha = 0f;

        // 黑色背景
        GameObject bgGo = new GameObject("GO_BG");
        bgGo.transform.parent = canvasGo.transform;
        Image bg = bgGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.85f);
        RectTransform br = bg.rectTransform;
        br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one;
        br.offsetMin = Vector2.zero; br.offsetMax = Vector2.zero;

        // GAME OVER 文字
        GameObject txtGo = new GameObject("GO_Text");
        txtGo.transform.parent = canvasGo.transform;
        Text txt = txtGo.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.text = "GAME OVER";
        txt.fontSize = 72;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.9f, 0.2f, 0.2f);
        RectTransform tr = txt.rectTransform;
        tr.anchorMin = new Vector2(0.1f, 0.4f); tr.anchorMax = new Vector2(0.9f, 0.6f);
        tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;

        // 副文字（幸存者全灭）
        GameObject subGo = new GameObject("GO_Sub");
        subGo.transform.parent = canvasGo.transform;
        Text sub = subGo.AddComponent<Text>();
        sub.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sub.text = "全ての生存者が失われた";
        sub.fontSize = 28;
        sub.alignment = TextAnchor.MiddleCenter;
        sub.color = new Color(0.8f, 0.8f, 0.8f);
        RectTransform sr2 = sub.rectTransform;
        sr2.anchorMin = new Vector2(0.1f, 0.32f); sr2.anchorMax = new Vector2(0.9f, 0.4f);
        sr2.offsetMin = Vector2.zero; sr2.offsetMax = Vector2.zero;
    }
}
