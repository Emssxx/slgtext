using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem; // 项目用新输入系统（和 BuildModeToggle 一样）
using TMPro;

// 营地仓库面板：显示公共仓库里的物品，选中后可以"使用"在当前对话的 NPC 身上。
// 设定上：仓库由 AI 终端机管理，玩家（AI）决定稀缺物资给谁用——这是资源取舍的核心体验。
// UI 全部运行时自动生成；按 I 键或调用 Toggle() 开关。
public class StoragePanelUI : MonoBehaviour
{
    [Header("字体（拖入和 NpcCard 相同的中日文 TMP 字体，否则中文会变方块）")]
    public TMP_FontAsset font;

    [Header("设置")]
    public float refreshInterval = 0.3f;  // 打开时每隔多少秒刷新一次数量
    public bool startOpen = false;        // 开局是否打开

    // —— 运行时生成的 UI 引用 ——
    private GameObject _panel;            // 整个面板（开关就是显示/隐藏它）
    private RectTransform _listRoot;      // 左侧物品列表的父物体
    private TMP_Text _title, _targetText; // 标题、"使用对象：X"
    private TMP_Text _detailName, _detailDesc, _detailEffect, _feedback; // 右侧详情
    private Button _useButton;            // "使用"按钮
    private TMP_Text _useButtonText;

    // 每一行物品：按钮 + 名字 + 数量。
    private class Row { public string id; public Image bg; public TMP_Text name; public TMP_Text count; }
    private readonly List<Row> _rows = new List<Row>();
    private string _selectedId;           // 当前选中的物品 id
    private float _timer;
    private NpcAgent _agent;              // 用来知道"当前对话目标"是谁

    // 颜色（和游戏整体的深色科技感一致）
    static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.11f, 0.94f);
    static readonly Color RowColor = new Color(0.14f, 0.16f, 0.22f, 1f);
    static readonly Color RowSelColor = new Color(0.25f, 0.35f, 0.6f, 1f);
    static readonly Color Accent = new Color(0.31f, 0.66f, 1f);
    static readonly Color SubColor = new Color(0.65f, 0.7f, 0.8f);

    void Start()
    {
        BuildUI();
        _panel.SetActive(startOpen);
        if (startOpen) Refresh();
    }

    void Update()
    {
        // 输入栏聚焦时不响应快捷键（和建造模式 B 键同样的处理）
        if (!ChatManager.InputFocused && Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame)
            Toggle();

        if (_panel == null || !_panel.activeSelf) return;
        _timer += Time.deltaTime;
        if (_timer >= refreshInterval) { _timer = 0f; Refresh(); }
    }

    // 开/关面板（也可以绑到 UI 按钮的 OnClick 上）。
    public void Toggle()
    {
        if (_panel == null) return;
        _panel.SetActive(!_panel.activeSelf);
        if (_panel.activeSelf) { _feedback.text = ""; Refresh(); }
    }

    // ================= 刷新显示 =================
    void Refresh()
    {
        if (WorldManager.Instance == null || ItemDatabase.Instance == null) return;
        WorldState world = WorldManager.Instance.world;

        _title.text = L("storage_title") + "  <size=70%><color=#9AA5B8>[I]</color></size>";

        // 当前使用对象 = 当前对话目标 NPC。
        NpcData target = GetTarget();
        string targetName = target != null ? target.npcName : L("storage_no_target");
        _targetText.text = L("storage_target") + targetName;

        // 收集"数量>0"的物品 id（顺序跟仓库一致）。
        List<string> ids = new List<string>();
        foreach (ItemStack s in world.sharedStorage)
            if (s.count > 0) ids.Add(s.itemName);

        // 物品种类变了才重建列表，否则只改数字（避免按钮一直被销毁重建）。
        if (!SameIds(ids)) RebuildRows(ids);
        foreach (Row r in _rows)
        {
            r.name.text = ItemDatabase.Instance.GetName(r.id);
            r.count.text = "×" + ItemUseRules.GetCount(r.id, world);
            r.bg.color = r.id == _selectedId ? RowSelColor : RowColor;
        }

        // 选中的物品没了 → 取消选中。
        if (_selectedId != null && !ids.Contains(_selectedId)) _selectedId = null;
        RefreshDetail(target, world);
    }

    // 右侧详情区：名字、说明、效果、使用按钮是否可点。
    void RefreshDetail(NpcData target, WorldState world)
    {
        if (_selectedId == null)
        {
            _detailName.text = "";
            _detailDesc.text = L(_rows.Count == 0 ? "storage_empty" : "storage_pick_hint");
            _detailEffect.text = "";
            _useButton.gameObject.SetActive(false);
            return;
        }

        ItemData item = ItemDatabase.Instance.Get(_selectedId);
        _detailName.text = ItemDatabase.Instance.GetName(_selectedId);
        _detailDesc.text = ItemDatabase.Instance.GetDesc(_selectedId);
        _detailEffect.text = item != null ? DescribeEffect(item) : "";

        // 只有"可使用"的物品才显示使用按钮；不满足条件时按钮变灰。
        bool usable = item != null && item.IsUsable;
        _useButton.gameObject.SetActive(usable);
        if (usable)
        {
            _useButton.interactable = ItemUseRules.CanUse(target, _selectedId, world, out _);
            _useButtonText.text = target != null
                ? LF("storage_use_on", target.npcName)
                : L("btn_use");
        }
    }

    // 把效果数值写成一行文字，如"饥饿 -30　体力 +10"。
    string DescribeEffect(ItemData item)
    {
        if (!item.IsUsable) return "<color=#9AA5B8>" + L("item_cat_" + item.category.ToString().ToLower()) + "</color>";
        string s = "";
        s += Part("stat_hunger", item.hungerDelta, true);  // 饥饿：减少是好事 → 负数显示绿色
        s += Part("stat_energy", item.energyDelta, false);
        s += Part("stat_sanity", item.sanityDelta, false);
        s += Part("stat_health", item.healthDelta, false);
        return s;
    }

    // 一个数值的显示片段：好的变化绿色，坏的变化红色。
    string Part(string statKey, float delta, bool lowerIsBetter)
    {
        if (Mathf.Approximately(delta, 0f)) return "";
        bool good = lowerIsBetter ? delta < 0 : delta > 0;
        string color = good ? "#5FD69A" : "#FF6B6B";
        string sign = delta > 0 ? "+" : "";
        return $"{L(statKey)} <color={color}>{sign}{delta:0}</color>　";
    }

    // ================= 按钮事件 =================
    void OnClickRow(string id)
    {
        _selectedId = id;
        _feedback.text = "";
        Refresh();
    }

    void OnClickUse()
    {
        if (_selectedId == null) return;
        WorldState world = WorldManager.Instance.world;
        NpcData target = GetTarget();

        if (ItemUseRules.CanUse(target, _selectedId, world, out string failKey))
        {
            string itemName = ItemDatabase.Instance.GetName(_selectedId);
            ItemUseRules.Use(target, _selectedId, world);
            _feedback.text = "<color=#5FD69A>" + LF("log_use_item", target.npcName, itemName) + "</color>";
        }
        else
        {
            _feedback.text = "<color=#FF6B6B>" + L(failKey) + "</color>";
        }
        Refresh();
    }

    // 当前使用对象（当前对话目标）。
    NpcData GetTarget()
    {
        if (_agent == null) _agent = FindFirstObjectByType<NpcAgent>();
        if (_agent == null || WorldManager.Instance == null) return null;
        return WorldManager.Instance.GetNpcById(_agent.npcId);
    }

    bool SameIds(List<string> ids)
    {
        if (ids.Count != _rows.Count) return false;
        for (int i = 0; i < ids.Count; i++) if (ids[i] != _rows[i].id) return false;
        return true;
    }

    // ================= 搭建 UI =================
    void BuildUI()
    {
        // 自己的顶层 Canvas（盖在普通 UI 上面，但低于事件横幅/GameOver）
        GameObject canvasGo = new GameObject("StorageCanvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>(); // 没有它按钮点不了

        // 面板本体：屏幕中央 760×480
        _panel = new GameObject("StoragePanel", typeof(RectTransform));
        _panel.transform.SetParent(canvasGo.transform, false);
        Image bg = _panel.AddComponent<Image>();
        bg.color = PanelColor; // raycastTarget 默认开着 → 点面板不会穿透到地图格子
        RectTransform prt = (RectTransform)_panel.transform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(760, 480);

        // 标题 + 使用对象
        _title = NewText(_panel.transform, "Title", "", 30, Accent, TextAlignmentOptions.Left);
        Place(_title.rectTransform, 24, 18, 500, 40);
        _targetText = NewText(_panel.transform, "Target", "", 20, SubColor, TextAlignmentOptions.Left);
        Place(_targetText.rectTransform, 24, 62, 500, 28);

        // 关闭按钮 ×
        Button close = NewButton(_panel.transform, "Close", new Color(0.3f, 0.2f, 0.25f), out TMP_Text closeText);
        Place((RectTransform)close.transform, 760 - 56, 16, 40, 40);
        closeText.text = "×"; closeText.fontSize = 28;
        close.onClick.AddListener(Toggle);

        // 左侧列表区
        GameObject list = new GameObject("List", typeof(RectTransform));
        list.transform.SetParent(_panel.transform, false);
        _listRoot = (RectTransform)list.transform;
        Place(_listRoot, 24, 104, 320, 352);

        // 右侧详情区
        _detailName = NewText(_panel.transform, "DetailName", "", 26, Color.white, TextAlignmentOptions.Left);
        Place(_detailName.rectTransform, 368, 104, 368, 36);
        _detailDesc = NewText(_panel.transform, "DetailDesc", "", 19, SubColor, TextAlignmentOptions.TopLeft);
        _detailDesc.enableWordWrapping = true;
        Place(_detailDesc.rectTransform, 368, 146, 368, 130);
        _detailEffect = NewText(_panel.transform, "DetailEffect", "", 20, Color.white, TextAlignmentOptions.Left);
        _detailEffect.enableWordWrapping = true;
        Place(_detailEffect.rectTransform, 368, 282, 368, 56);

        _useButton = NewButton(_panel.transform, "UseButton", new Color(0.2f, 0.45f, 0.85f), out _useButtonText);
        Place((RectTransform)_useButton.transform, 368, 348, 368, 52);
        _useButton.onClick.AddListener(OnClickUse);

        _feedback = NewText(_panel.transform, "Feedback", "", 18, Color.white, TextAlignmentOptions.Left);
        _feedback.enableWordWrapping = true;
        Place(_feedback.rectTransform, 368, 408, 368, 48);
    }

    // 按物品 id 列表重建左侧的行。
    void RebuildRows(List<string> ids)
    {
        foreach (Row r in _rows) Destroy(r.bg.gameObject);
        _rows.Clear();

        const float rowH = 40f, gap = 6f;
        for (int i = 0; i < ids.Count; i++)
        {
            string id = ids[i]; // 闭包要用局部变量，不能直接用 ids[i]
            Button b = NewButton(_listRoot, "Row_" + id, RowColor, out TMP_Text nameText);
            Place((RectTransform)b.transform, 0, i * (rowH + gap), 320, rowH);
            nameText.alignment = TextAlignmentOptions.Left;
            nameText.margin = new Vector4(14, 0, 60, 0); // 左边留白、右边给数量让位

            TMP_Text countText = NewText(b.transform, "Count", "", 20, Accent, TextAlignmentOptions.Right);
            RectTransform crt = countText.rectTransform;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(0, 0); crt.offsetMax = new Vector2(-14, 0);

            b.onClick.AddListener(() => OnClickRow(id));
            _rows.Add(new Row { id = id, bg = b.GetComponent<Image>(), name = nameText, count = countText });
        }
    }

    // —— 小工具 ——

    // 以"面板左上角"为原点摆放（x 向右、y 向下），比算锚点直观。
    void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    TMP_Text NewText(Transform parent, string name, string content, float size, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = content; t.fontSize = size; t.color = color; t.alignment = align;
        t.enableWordWrapping = false; t.raycastTarget = false; t.richText = true;
        if (font != null) t.font = font;
        return t;
    }

    Button NewButton(Transform parent, string name, Color color, out TMP_Text label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        Button b = go.AddComponent<Button>();
        label = NewText(go.transform, "Label", "", 21, Color.white, TextAlignmentOptions.Center);
        RectTransform lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        return b;
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] a) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, a) : key;
}
