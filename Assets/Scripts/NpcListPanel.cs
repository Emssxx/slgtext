using UnityEngine;
using UnityEngine.UI;
using TMPro;

// NPC 列表面板：左上角按钮切换显示，列出所有 NPC，点击名字打开状态卡。
// 纯代码建 UI，不需要在 Prefab / Inspector 里手动拼节点。
public class NpcListPanel : MonoBehaviour
{
    [Header("字体（Inspector 拖入 NotoSansSC）")]
    public TMP_FontAsset font; // 用于渲染中日文 NPC 名字

    GameObject _panel;   // 列表面板根节点
    Transform  _content; // 每个 NPC 按钮的父节点
    bool _isVisible;

    void Start()
    {
        // 找到场景里的 Canvas 作为 UI 父节点
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) { Debug.LogWarning("NpcListPanel: 找不到 Canvas"); return; }

        BuildToggleButton(canvas.transform);
        BuildPanel(canvas.transform);
        _panel.SetActive(false); // 默认隐藏
    }

    // ─── 切换按钮（左上角，BuildModeBtn 正下方）─────────────────────────
    void BuildToggleButton(Transform parent)
    {
        GameObject go = new GameObject("NpcListBtn", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        // 锚在左上角，BuildModeBtn 占了 y=-10 到 -48，我们从 -54 开始
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(10f, -54f);
        rt.sizeDelta = new Vector2(120f, 38f);

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.18f, 0.25f, 0.45f, 0.9f); // 蓝调，区别于绿色建造按钮

        Button btn = go.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor      = new Color(0.18f, 0.25f, 0.45f, 0.9f);
        cb.highlightedColor = new Color(0.23f, 0.32f, 0.58f, 1.0f);
        cb.pressedColor     = new Color(0.11f, 0.15f, 0.27f, 0.9f);
        btn.colors = cb;
        btn.onClick.AddListener(Toggle);

        // 文字：挂 LocalizedText 跟随语言切换（幸存者列表 / 生存者リスト）
        TextMeshProUGUI txt = MakeTMPLabel(go.transform, "", 16);
        txt.alignment = TextAlignmentOptions.Center;
        LocalizedText lt = txt.gameObject.AddComponent<LocalizedText>();
        lt.key = "npc_list_title";
        lt.Refresh(); // AddComponent 时 key 还未赋值导致 OnEnable 里刷新无效，手动补一次
    }

    // ─── 列表面板（左上角，切换按钮下方）────────────────────────────────
    void BuildPanel(Transform parent)
    {
        // 面板背景
        _panel = new GameObject("NpcListPanel", typeof(RectTransform));
        _panel.transform.SetParent(parent, false);

        RectTransform panelRt = _panel.GetComponent<RectTransform>();
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0f, 1f);
        panelRt.pivot     = new Vector2(0f, 1f);
        panelRt.anchoredPosition = new Vector2(10f, -98f); // 切换按钮(38px)下方 + 6px 间距
        panelRt.sizeDelta = new Vector2(150f, 260f);

        _panel.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.15f, 0.92f);

        // ScrollRect
        ScrollRect scroll = _panel.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        // Viewport（带 Mask，防止内容溢出）
        GameObject vp = new GameObject("Viewport", typeof(RectTransform));
        vp.transform.SetParent(_panel.transform, false);
        RectTransform vpRt = vp.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = new Vector2(4f, 4f);
        vpRt.offsetMax = new Vector2(-4f, -4f);
        Image vpImg = vp.AddComponent<Image>();
        vpImg.color = new Color(0f, 0f, 0f, 0.01f); // Mask 需要有 Graphic 才能遮罩
        vp.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = vpRt;

        // Content（NPC 按钮的父节点，高度自适应）
        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(vp.transform, false);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot     = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing             = 4f;
        vlg.padding             = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth   = true;
        vlg.childControlHeight  = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = contentRt;
        _content = content.transform;

        // 面板初次填充（此时可能 WorldManager 还没完全就绪，用 Invoke 延一帧）
        Invoke(nameof(RefreshList), 0.1f);
    }

    // ─── 切换显示 / 隐藏 ─────────────────────────────────────────────────
    void Toggle()
    {
        _isVisible = !_isVisible;
        if (_isVisible) RefreshList(); // 每次打开时刷新（以防 NPC 数量变化）
        _panel.SetActive(_isVisible);
    }

    // ─── 重建 NPC 按钮列表 ───────────────────────────────────────────────
    void RefreshList()
    {
        // 清除旧按钮
        foreach (Transform child in _content)
            Destroy(child.gameObject);

        if (WorldManager.Instance?.world?.npcs == null) return;

        foreach (var npc in WorldManager.Instance.world.npcs)
        {
            string capturedId = npc.id; // 闭包需要独立变量

            // 按钮背景
            GameObject btnGo = new GameObject(npc.npcName, typeof(RectTransform));
            btnGo.transform.SetParent(_content, false);
            btnGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 32f);

            Image img = btnGo.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.3f, 0.8f);

            Button btn = btnGo.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor      = new Color(0.20f, 0.20f, 0.30f, 0.8f);
            cb.highlightedColor = new Color(0.30f, 0.30f, 0.45f, 1.0f);
            cb.pressedColor     = new Color(0.12f, 0.12f, 0.18f, 0.8f);
            btn.colors = cb;
            btn.onClick.AddListener(() =>
            {
                if (StatusPanelManager.Instance != null)
                    StatusPanelManager.Instance.OpenCard(capturedId);
            });

            // 名字文字（左对齐，留 8px 缩进）
            TextMeshProUGUI txt = MakeTMPLabel(btnGo.transform, npc.npcName, 15);
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform trt = txt.GetComponent<RectTransform>();
            trt.offsetMin = new Vector2(8f, 0f);
            trt.offsetMax = new Vector2(-8f, 0f);
        }
    }

    // ─── 工具：在 parent 下创建一个铺满的 TMP 文字节点 ──────────────────
    TextMeshProUGUI MakeTMPLabel(Transform parent, string text, float size)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = Color.white;
        if (font != null) tmp.font = font;

        return tmp;
    }
}
