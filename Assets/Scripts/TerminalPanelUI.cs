using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// AI 终端机面板：点击终端机打开。三个分页——升级 / 合成（建造台）/ 科技。
// 底部显示算力槽和任务队列。UI 运行时自动生成（和仓库面板同一风格）。
// 挂在场景里的常驻空物体上（和 TerminalDatabase 放一起即可），不要挂在终端机预制体上。
public class TerminalPanelUI : MonoBehaviour
{
    public static TerminalPanelUI Instance { get; private set; }

    [Header("字体（拖入中日文 TMP 字体，否则中文会变方块）")]
    public TMP_FontAsset font;

    [Header("设置")]
    public float refreshInterval = 0.25f; // 打开时多久刷新一次（进度条要动，所以快一点）

    enum Tab { Upgrade, Craft, Tech }
    private Tab _tab = Tab.Craft;

    // —— 运行时生成的 UI ——
    private GameObject _panel;
    private TMP_Text _header, _queueText, _dName, _dDesc, _dCost, _dReq, _feedback, _actionText;
    private Button _actionBtn;
    private Button[] _tabBtns = new Button[3];
    private RectTransform _listRoot;

    // 左侧列表的一行
    private class Row { public string id; public Image bg; public TMP_Text label; }
    private readonly List<Row> _rows = new List<Row>();
    private string _selectedId;
    private float _timer;

    static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.10f, 0.95f);
    static readonly Color RowColor = new Color(0.14f, 0.16f, 0.22f, 1f);
    static readonly Color RowSelColor = new Color(0.25f, 0.35f, 0.6f, 1f);
    static readonly Color TabColor = new Color(0.16f, 0.18f, 0.26f, 1f);
    static readonly Color TabSelColor = new Color(0.49f, 0.36f, 1f, 1f);
    static readonly Color Accent = new Color(0.31f, 0.66f, 1f);
    static readonly Color SubColor = new Color(0.65f, 0.7f, 0.8f);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
        _panel.SetActive(false);
    }

    void Update()
    {
        if (_panel == null || !_panel.activeSelf) return;
        // 终端机被销毁了就关掉面板
        if (TerminalController.Instance == null) { Close(); return; }
        _timer += Time.deltaTime;
        if (_timer >= refreshInterval) { _timer = 0f; Refresh(); }
    }

    // 打开面板（TerminalClickHandler 点击终端机时调用）。
    public void Open()
    {
        if (TerminalController.Instance == null) return;
        _panel.SetActive(true);
        _feedback.text = "";
        Refresh();
    }

    public void Close() { if (_panel != null) _panel.SetActive(false); }

    void SwitchTab(Tab t)
    {
        _tab = t;
        _selectedId = null;
        _feedback.text = "";
        Refresh();
    }

    // ================= 刷新 =================
    void Refresh()
    {
        TerminalController tc = TerminalController.Instance;
        if (tc == null) return;

        // 标题：AI终端机　Lv1·一产　在线
        string status = !tc.isBuilt ? L("term_status_building") : (tc.IsOnline() ? L("term_status_online") : L("term_status_offline"));
        _header.text = $"{L("terminal_name")}　<color=#4EA8FF>{ProgressionRules.TierLabel(ProgressionRules.Tier)}</color>　<size=75%><color=#9AA5B8>{status}</color></size>";

        // 分页按钮高亮
        for (int i = 0; i < 3; i++) _tabBtns[i].GetComponent<Image>().color = (int)_tab == i ? TabSelColor : TabColor;

        // 左侧列表
        List<(string id, string label, bool dim)> entries = BuildEntries();
        if (!SameIds(entries)) RebuildRows(entries);
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].label.text = entries[i].label;
            _rows[i].label.color = entries[i].dim ? new Color(0.5f, 0.52f, 0.58f) : Color.white;
            _rows[i].bg.color = _rows[i].id == _selectedId ? RowSelColor : RowColor;
        }

        RefreshDetail();
        RefreshQueue();
    }

    // 当前分页要列出的条目：(id, 显示文字, 是否变灰)。
    List<(string, string, bool)> BuildEntries()
    {
        var list = new List<(string, string, bool)>();
        TerminalDatabase db = TerminalDatabase.Instance;
        if (db == null) return list;
        TerminalWorkshop ws = TerminalWorkshop.Current;

        switch (_tab)
        {
            case Tab.Craft:
                foreach (RecipeData r in db.recipes)
                {
                    bool unlocked = ProgressionRules.IsRecipeUnlocked(r, out _);
                    string label = ProgressionRules.ItemName(r.outputItemId) + " ×" + r.outputCount;
                    if (!unlocked) label += "  <size=80%>[" + LockShort(r.requiredTier, r.requiredTechId) + "]</size>";
                    list.Add((r.id, label, !unlocked));
                }
                break;

            case Tab.Tech:
                foreach (TechData t in db.techs)
                {
                    bool done = ProgressionRules.HasTech(t.id);
                    bool unlocked = ProgressionRules.IsTechUnlocked(t, out _);
                    bool researching = ws != null && ws.IsQueued(TerminalJobType.Research, t.id);
                    string label = db.TechName(t.id);
                    if (done) label = "<color=#5FD69A>●</color> " + label;
                    else if (researching) label += "  <size=80%><color=#4EA8FF>" + L("tech_researching") + "</color></size>";
                    else if (!unlocked) label += "  <size=80%>[" + L("lock_short_tech") + "/Lv" + t.requiredTier + "]</size>";
                    list.Add((t.id, label, !done && !unlocked));
                }
                break;

            case Tab.Upgrade:
                foreach (TerminalUpgradeStep s in db.upgrades)
                {
                    bool done = ProgressionRules.Tier >= s.toLevel;
                    string label = ProgressionRules.LF("upgrade_to", ProgressionRules.TierLabel(s.toLevel));
                    if (done) label = "<color=#5FD69A>●</color> " + label;
                    list.Add((s.toLevel.ToString(), label, !done && ProgressionRules.Tier < s.toLevel - 1));
                }
                break;
        }
        return list;
    }

    // 右侧详情 + 操作按钮
    void RefreshDetail()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        TerminalWorkshop ws = TerminalWorkshop.Current;
        _dName.text = _dDesc.text = _dCost.text = _dReq.text = "";
        _actionBtn.gameObject.SetActive(false);

        if (db == null) return;
        if (_selectedId == null)
        {
            // 没选条目：升级页显示当前等级概况，其它页显示提示
            if (_tab == Tab.Upgrade)
            {
                _dName.text = ProgressionRules.TierLabel(ProgressionRules.Tier);
                _dDesc.text = ProgressionRules.LF("term_slots_info", ws != null ? ws.Slots : 1)
                            + (ProgressionRules.Tier >= ProgressionRules.MaxTier ? "\n" + L("term_max_level") : "");
            }
            else _dDesc.text = L("term_pick_hint");
            return;
        }

        string reason;
        bool can;
        switch (_tab)
        {
            case Tab.Craft:
            {
                RecipeData r = db.GetRecipe(_selectedId);
                if (r == null) return;
                _dName.text = ProgressionRules.LF("craft_out", ProgressionRules.ItemName(r.outputItemId), r.outputCount);
                _dDesc.text = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetDesc(r.outputItemId) : "";
                _dCost.text = L("term_cost") + ProgressionRules.DescribeCost(r.inputs) + "\n" + ProgressionRules.LF("term_time", r.craftTime.ToString("0"));
                can = ProgressionRules.CanCraft(r, out reason);
                ShowAction(L("btn_craft"), can, reason);
                break;
            }
            case Tab.Tech:
            {
                TechData t = db.GetTech(_selectedId);
                if (t == null) return;
                _dName.text = db.TechName(t.id);
                _dDesc.text = db.TechDesc(t.id);
                _dCost.text = L("term_cost") + ProgressionRules.DescribeCost(t.cost) + "\n" + ProgressionRules.LF("term_time", t.researchTime.ToString("0"));
                _dReq.text = L("term_require") + ProgressionRules.TierLabel(t.requiredTier) + PrereqText(t.prerequisites);
                can = ProgressionRules.CanResearch(t, out reason);
                if (ws != null && ws.IsQueued(TerminalJobType.Research, t.id)) { can = false; reason = L("job_already_queued"); }
                ShowAction(L("btn_research"), can, reason);
                break;
            }
            case Tab.Upgrade:
            {
                int.TryParse(_selectedId, out int lv);
                TerminalUpgradeStep s = db.GetUpgrade(lv);
                if (s == null) return;
                _dName.text = ProgressionRules.LF("upgrade_to", ProgressionRules.TierLabel(s.toLevel));
                _dDesc.text = db.UpgradeNote(s);
                _dCost.text = L("term_cost") + ProgressionRules.DescribeCost(s.cost) + "\n" + ProgressionRules.LF("term_time", s.upgradeTime.ToString("0"));
                _dReq.text = L("term_require") + ProgressionRules.TierLabel(s.toLevel - 1)
                           + (string.IsNullOrEmpty(s.requiredTechId) ? "" : "　" + TechCheck(s.requiredTechId));
                can = ProgressionRules.CanUpgrade(s, out reason);
                if (ws != null && ws.HasJobOfType(TerminalJobType.Upgrade)) { can = false; reason = L("job_already_queued"); }
                ShowAction(L("btn_upgrade"), can, reason);
                break;
            }
        }
    }

    void ShowAction(string label, bool can, string reason)
    {
        _actionBtn.gameObject.SetActive(true);
        _actionBtn.interactable = can;
        _actionText.text = can ? label : label + "  <size=75%>(" + reason + ")</size>";
    }

    // 底部：算力槽 + 正在处理的任务进度 + 排队数
    void RefreshQueue()
    {
        TerminalWorkshop ws = TerminalWorkshop.Current;
        if (ws == null) { _queueText.text = "<color=#FF6B6B>TerminalWorkshop ?</color>"; return; }

        string s = ProgressionRules.LF("term_slots", ws.running.Count, ws.Slots, ws.waiting.Count) + "\n";
        if (ws.running.Count == 0) s += "<color=#9AA5B8>" + L("term_idle") + "</color>";
        foreach (TerminalJob j in ws.running)
            s += $"→ {TerminalWorkshop.JobLabel(j)}  {Bar(j.Progress01)}  {j.remaining:0}s　";
        _queueText.text = s;
    }

    // 文字进度条 ■■■□□
    static string Bar(float p)
    {
        int n = Mathf.RoundToInt(p * 10f);
        return "<color=#4EA8FF>" + new string('■', n) + "</color><color=#3A4050>" + new string('■', 10 - n) + "</color>";
    }

    // ================= 按钮 =================
    void OnClickAction()
    {
        TerminalWorkshop ws = TerminalWorkshop.Current;
        TerminalDatabase db = TerminalDatabase.Instance;
        if (ws == null || db == null || _selectedId == null) return;

        bool ok = false; string msg = "";
        switch (_tab)
        {
            case Tab.Craft:   ok = ws.TryEnqueueCraft(db.GetRecipe(_selectedId), out msg); break;
            case Tab.Tech:    ok = ws.TryEnqueueResearch(db.GetTech(_selectedId), out msg); break;
            case Tab.Upgrade: int.TryParse(_selectedId, out int lv); ok = ws.TryEnqueueUpgrade(db.GetUpgrade(lv), out msg); break;
        }
        _feedback.text = (ok ? "<color=#5FD69A>" : "<color=#FF6B6B>") + msg + "</color>";
        Refresh();
    }

    void OnClickRow(string id) { _selectedId = id; _feedback.text = ""; Refresh(); }

    // ================= 小工具 =================

    string LockShort(int tier, string techId)
    {
        if (ProgressionRules.Tier < tier) return "Lv" + tier;
        if (!ProgressionRules.HasTech(techId)) return L("lock_short_tech");
        return "";
    }

    string PrereqText(string[] pres)
    {
        if (pres == null || pres.Length == 0) return "";
        string s = "";
        foreach (string p in pres) s += "　" + TechCheck(p);
        return s;
    }

    // 科技名 + 是否已满足（○ 绿 / × 红）。符号都选了日文字体里一定有的字，避免变方块
    string TechCheck(string techId)
    {
        bool ok = ProgressionRules.HasTech(techId);
        return (ok ? "<color=#5FD69A>○" : "<color=#FF6B6B>×") + ProgressionRules.TechName(techId) + "</color>";
    }

    bool SameIds(List<(string id, string label, bool dim)> entries)
    {
        if (entries.Count != _rows.Count) return false;
        for (int i = 0; i < entries.Count; i++) if (entries[i].id != _rows[i].id) return false;
        return true;
    }

    // ================= 搭 UI =================
    void BuildUI()
    {
        GameObject canvasGo = new GameObject("TerminalCanvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 410; // 比仓库面板高一点
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        // 面板 900×580，屏幕正中
        _panel = new GameObject("TerminalPanel", typeof(RectTransform));
        _panel.transform.SetParent(canvasGo.transform, false);
        _panel.AddComponent<Image>().color = PanelColor; // 挡住点击，不穿透到地图
        RectTransform prt = (RectTransform)_panel.transform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(900, 580);

        _header = NewText(_panel.transform, "Header", "", 30, Color.white, TextAlignmentOptions.Left);
        Place(_header.rectTransform, 24, 16, 760, 44);

        Button close = NewButton(_panel.transform, "Close", new Color(0.3f, 0.2f, 0.25f), out TMP_Text ct);
        Place((RectTransform)close.transform, 900 - 56, 16, 40, 40);
        ct.text = "×"; ct.fontSize = 28;
        close.onClick.AddListener(Close);

        // 三个分页按钮
        string[] tabKeys = { "term_tab_upgrade", "term_tab_craft", "term_tab_tech" };
        for (int i = 0; i < 3; i++)
        {
            Tab t = (Tab)i;
            _tabBtns[i] = NewButton(_panel.transform, "Tab" + i, TabColor, out TMP_Text tt);
            tt.text = L(tabKeys[i]);
            Place((RectTransform)_tabBtns[i].transform, 24 + i * 132, 68, 124, 38);
            _tabBtns[i].onClick.AddListener(() => SwitchTab(t));
        }

        // 左：列表区
        GameObject list = new GameObject("List", typeof(RectTransform));
        list.transform.SetParent(_panel.transform, false);
        _listRoot = (RectTransform)list.transform;
        Place(_listRoot, 24, 120, 340, 340);

        // 右：详情区
        _dName = NewText(_panel.transform, "DName", "", 26, Accent, TextAlignmentOptions.Left);
        Place(_dName.rectTransform, 388, 120, 488, 36);
        _dDesc = NewText(_panel.transform, "DDesc", "", 19, SubColor, TextAlignmentOptions.TopLeft);
        _dDesc.enableWordWrapping = true;
        Place(_dDesc.rectTransform, 388, 160, 488, 96);
        _dCost = NewText(_panel.transform, "DCost", "", 19, Color.white, TextAlignmentOptions.TopLeft);
        _dCost.enableWordWrapping = true;
        Place(_dCost.rectTransform, 388, 260, 488, 56);
        _dReq = NewText(_panel.transform, "DReq", "", 18, SubColor, TextAlignmentOptions.TopLeft);
        _dReq.enableWordWrapping = true;
        Place(_dReq.rectTransform, 388, 318, 488, 30);

        _actionBtn = NewButton(_panel.transform, "Action", new Color(0.2f, 0.45f, 0.85f), out _actionText);
        Place((RectTransform)_actionBtn.transform, 388, 356, 488, 50);
        _actionBtn.onClick.AddListener(OnClickAction);

        _feedback = NewText(_panel.transform, "Feedback", "", 18, Color.white, TextAlignmentOptions.Left);
        _feedback.enableWordWrapping = true;
        Place(_feedback.rectTransform, 388, 412, 488, 44);

        // 底：任务队列
        _queueText = NewText(_panel.transform, "Queue", "", 18, Color.white, TextAlignmentOptions.TopLeft);
        _queueText.enableWordWrapping = true;
        Place(_queueText.rectTransform, 24, 476, 852, 90);
    }

    void RebuildRows(List<(string id, string label, bool dim)> entries)
    {
        foreach (Row r in _rows) Destroy(r.bg.gameObject);
        _rows.Clear();
        const float rowH = 38f, gap = 5f;
        for (int i = 0; i < entries.Count; i++)
        {
            string id = entries[i].id;
            Button b = NewButton(_listRoot, "Row_" + id, RowColor, out TMP_Text label);
            Place((RectTransform)b.transform, 0, i * (rowH + gap), 340, rowH);
            label.alignment = TextAlignmentOptions.Left;
            label.margin = new Vector4(12, 0, 8, 0);
            b.onClick.AddListener(() => OnClickRow(id));
            _rows.Add(new Row { id = id, bg = b.GetComponent<Image>(), label = label });
        }
    }

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
        label = NewText(go.transform, "Label", "", 20, Color.white, TextAlignmentOptions.Center);
        RectTransform lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        return b;
    }

    static string L(string key) => ProgressionRules.L(key);
}
