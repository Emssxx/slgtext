using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// 状态面板总管：按需打开指定 NPC 的状态卡，允许多张卡同时存在，每张可单独关闭。
public class StatusPanelManager : MonoBehaviour
{
    public static StatusPanelManager Instance { get; private set; }

    [Header("引用")]
    public Transform contentParent;   // 卡片生成在谁下面（StatusPanel 的 Content）
    public GameObject npcCardPrefab;  // NpcCard 预制体
    public NpcAgent npcAgent;         // 当前对话目标（Inspector 拖入）
    public TMP_FontAsset font;        // 中日文字体，传给关闭按钮的 × 文字用

    [Header("刷新设置")]
    public float refreshInterval = 0.5f; // 每隔多少秒刷新一次数值

    // 关闭某张卡时触发，参数为被关闭的 npcId（LogClickHandler 用来恢复名字颜色）
    public static event System.Action<string> OnCardClosed;

    // 当前已打开的卡片，npcId → 卡实例。
    private Dictionary<string, NpcCardUI> _cards = new Dictionary<string, NpcCardUI>();
    private float _timer;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // 不再开局自动生成所有卡，改成按需打开
    }

    // 打开指定 NPC 的状态卡。
    // 已开着 → 移到最前；没开 → 新建一张。
    public void OpenCard(string npcId)
    {
        // 已经开着：把它移到列表末尾（视觉上最前），直接返回
        if (_cards.ContainsKey(npcId))
        {
            _cards[npcId].transform.SetAsLastSibling();
            return;
        }

        NpcData npc = WorldManager.Instance.GetNpcById(npcId);
        if (npc == null) { Debug.LogWarning("OpenCard: 找不到 NPC id=" + npcId); return; }

        // 实例化卡片并初始化
        GameObject go = Instantiate(npcCardPrefab, contentParent);
        NpcCardUI card = go.GetComponent<NpcCardUI>();
        card.npcAgent = npcAgent;
        card.Refresh(npc);
        _cards[npcId] = card;

        // 在卡片右上角追加关闭按钮
        AddCloseButton(go, npcId);
    }

    // 关闭并销毁指定 NPC 的卡片，同时广播事件让日志里的名字恢复原色。
    void CloseCard(string npcId)
    {
        if (!_cards.ContainsKey(npcId)) return;
        Destroy(_cards[npcId].gameObject);
        _cards.Remove(npcId);
        OnCardClosed?.Invoke(npcId);
    }

    // 在卡片右上角用代码追加一个 × 关闭按钮。
    void AddCloseButton(GameObject cardGo, string npcId)
    {
        // 按钮根节点
        GameObject btnGo = new GameObject("CloseBtn", typeof(RectTransform));
        btnGo.transform.SetParent(cardGo.transform, false);

        // 定位到右上角，20×20
        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(20f, 20f);
        rt.anchoredPosition = new Vector2(-2f, -2f);

        // 背景：深红色半透明
        Image img = btnGo.AddComponent<Image>();
        img.color = new Color(0.75f, 0.15f, 0.15f, 0.9f);

        // 按钮组件：点击关闭
        Button btn = btnGo.AddComponent<Button>();
        string id = npcId; // 闭包捕获
        btn.onClick.AddListener(() => CloseCard(id));

        // × 文字
        GameObject txtGo = new GameObject("X", typeof(RectTransform));
        txtGo.transform.SetParent(btnGo.transform, false);
        RectTransform trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        TextMeshProUGUI txt = txtGo.AddComponent<TextMeshProUGUI>();
        txt.text = "×";
        txt.fontSize = 13f;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = Color.white;
        if (font != null) txt.font = font;
    }

    void Update()
    {
        // 定时刷新已打开的卡片数值
        _timer += Time.deltaTime;
        if (_timer >= refreshInterval)
        {
            _timer = 0f;
            RefreshAll();
        }
    }

    // 刷新所有当前打开的卡片数值。
    void RefreshAll()
    {
        if (WorldManager.Instance == null || WorldManager.Instance.world == null) return;
        foreach (var kv in _cards)
        {
            NpcData npc = WorldManager.Instance.GetNpcById(kv.Key);
            if (npc != null) kv.Value.Refresh(npc);
        }
    }
}
