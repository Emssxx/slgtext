using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// AI 终端机面板：点击终端机打开。四个分页——升级 / 合成（建造台）/ 科技 / 能源（电网 + 加温器、降温器的档位）。
// 底部显示算力槽和任务队列。UI 运行时自动生成。
// 风格和游戏里其他浮窗一致（UiPopup）：深色底 + 淡绿细框 + 左上角标签「terminal : online」；
//   打开时一条细线从终端机伸到浮窗左上角 → 变成上沿 → 往下展开。分页 / 按钮是淡绿标签样式（英文等宽字）。
// 挂在场景里的常驻空物体上（和 TerminalDatabase 放一起即可），不要挂在终端机预制体上。
public class TerminalPanelUI : MonoBehaviour
{
    public static TerminalPanelUI Instance { get; private set; }

    [Header("字体（拖入中日文 TMP 字体，否则中文会变方块）")]
    public TMP_FontAsset font;

    [Header("设置")]
    public float refreshInterval = 0.25f; // 打开时多久刷新一次（进度条要动，所以快一点）

    enum Tab { Upgrade, Craft, Tech, Power }
    private Tab _tab = Tab.Craft;

    // —— 运行时生成的 UI ——
    private UiPopup _popup;
    private GameObject _panel;            // = 浮窗的 Body，内容都放在这里
    private string _tagText;
    private readonly TMP_Text[] _tabTexts = new TMP_Text[4];
    private readonly List<TMP_Text> _tags = new List<TMP_Text>();       // 段标题：打开（激活）后要重新量宽度
    private readonly List<GameObject> _mainOnly = new List<GameObject>(); // 只在升级 / 合成 / 科技页显示的装饰（段标题、分隔线）
    private TMP_Text _header, _queueText, _dName, _dDesc, _dCost, _dReq, _feedback, _actionText;
    private Button _actionBtn;
    private Button[] _tabBtns = new Button[4];

    // —— 能源页 ——
    private GameObject _powerRoot;
    private TMP_Text _powerSummary, _powerHeader;
    private class DevRow { public PowerNode node; public GameObject root; public TMP_Text label, status; }
    private readonly List<DevRow> _devRows = new List<DevRow>();
    const int MaxDevRows = 6;
    private RectTransform _listRoot;

    // 左侧列表的一行
    private class Row { public string id; public Image bg; public TMP_Text label; }
    private readonly List<Row> _rows = new List<Row>();
    private string _selectedId;
    private float _timer;

    // 识别框风格的颜色（UiStyle）：淡绿为主色，行底色很淡
    static readonly Color Green = UiStyle.Green;
    static readonly Color RowColor = new Color(1f, 1f, 1f, 0.035f);
    static readonly Color RowSelColor = new Color(0.72f, 1f, 0.78f, 0.16f);
    static readonly Color Accent = UiStyle.Green;
    static readonly Color SubColor = UiStyle.TextSub;
    const float W = 900f, H = 600f;   // 浮窗大小（1920×1080 下的界面单位）

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void Update()
    {
        if (_popup == null || !_popup.IsOpen) return;
        // 终端机被销毁了就关掉面板
        if (TerminalController.Instance == null) { Close(); return; }
        _timer += Time.deltaTime;
        if (_timer >= refreshInterval) { _timer = 0f; Refresh(); }
    }

    // 打开面板（TerminalClickHandler 点击终端机时调用）。
    public void Open()
    {
        TerminalController tc = TerminalController.Instance;
        if (tc == null) return;
        _feedback.text = "";
        if (_popup.IsOpen) { Refresh(); return; }   // 已经开着：不重放动画
        // 浮窗在屏幕正中；线从终端机（屏幕上的位置）伸到浮窗左上角
        Vector2 cs = _popup.CanvasSize;
        Vector2 tl = new Vector2((cs.x - W) * 0.5f, (cs.y + H) * 0.5f);
        Camera cam = Camera.main;
        Vector2 origin = cam != null ? _popup.ToCanvas(cam.WorldToScreenPoint(tc.transform.position)) : tl;
        _popup.OpenCanvas(origin, tl, new Vector2(W, H), cam != null);
        foreach (TMP_Text t in _tags) UiStyle.FitTag(t);   // 标签是在没激活的时候建的：激活后重新量宽度
        Refresh();
    }

    public bool IsOpen => _popup != null && _popup.IsOpen;

    public void Close() { if (_popup != null) _popup.Close(); }

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
        string stColor = !tc.isBuilt ? "#FFE36A" : (tc.IsOnline() ? "#74DB8C" : "#FF6B6B");
        _header.text = $"{L("terminal_name")}　<color=#B8FFC7>{ProgressionRules.TierLabel(ProgressionRules.Tier)}</color>　<size=75%><color={stColor}>{status}</color></size>";
        // 左上角标签：terminal : online / offline / building
        string tag = !tc.isBuilt ? "terminal : building" : (tc.IsOnline() ? "terminal : online" : "terminal : offline");
        if (tag != _tagText) { _tagText = tag; _popup.SetTag(tag); }

        // 分页按钮：选中的实心淡绿，没选中的只有细框
        for (int i = 0; i < _tabBtns.Length; i++) StyleTagButton(_tabBtns[i], _tabTexts[i], (int)_tab == i);

        // 能源页：另一套界面
        bool power = _tab == Tab.Power;
        SetMainVisible(!power);
        if (power) { RefreshPower(); return; }

        // 左侧列表
        List<(string id, string label, bool dim)> entries = BuildEntries();
        if (!SameIds(entries)) RebuildRows(entries);
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].label.text = entries[i].label;
            _rows[i].label.color = entries[i].dim ? UiStyle.Gray : UiStyle.TextMain;
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
                    else if (researching) label += "  <size=80%><color=#B8FFC7>" + L("tech_researching") + "</color></size>";
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
        // 能点：实心淡绿 + 深色字；不能点：很淡的灰底 + 灰字
        _actionBtn.GetComponent<Image>().color = can ? Green : new Color(UiStyle.Gray.r, UiStyle.Gray.g, UiStyle.Gray.b, 0.16f);
        _actionText.color = can ? UiStyle.Ink : UiStyle.TextSub;
    }

    // 底部：算力槽 + 正在处理的任务进度 + 排队数
    void RefreshQueue()
    {
        TerminalWorkshop ws = TerminalWorkshop.Current;
        if (ws == null) { _queueText.text = "<color=#FF6B6B>TerminalWorkshop ?</color>"; return; }

        string s = ProgressionRules.LF("term_slots", ws.running.Count, ws.Slots, ws.waiting.Count) + "\n";
        if (ws.running.Count == 0) s += "<color=#9DB3A4>" + L("term_idle") + "</color>";
        foreach (TerminalJob j in ws.running)
            s += $"→ {TerminalWorkshop.JobLabel(j)}  {Bar(j.Progress01)}  {j.remaining:0}s　";
        _queueText.text = s;
    }

    // 文字进度条 ■■■□□
    static string Bar(float p)
    {
        int n = Mathf.RoundToInt(p * 10f);
        return "<color=#B8FFC7>" + new string('■', n) + "</color><color=#34403A>" + new string('■', 10 - n) + "</color>";
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
        if (font == null) font = GameFonts.Find();

        // 浮窗（和资源详细、任务栏浮窗同一个样式），自己一个画布
        _popup = UiPopup.Create("TerminalPopup", 410);   // 比仓库面板高一点
        _popup.transform.SetParent(transform, false);
        _popup.Interactable = true;    // 挡住点击，不穿透到地图
        _popup.KeepTether = false;     // 镜头会动：打开完就把线收掉
        _tagText = "terminal : online";
        _popup.SetTag(_tagText);
        _panel = _popup.Body.gameObject;
        _popup.Body.sizeDelta = new Vector2(W, H);

        _header = NewText(_panel.transform, "Header", "", 24, UiStyle.TextMain, TextAlignmentOptions.Left);
        Place(_header.rectTransform, 24, 30, 760, 36);

        // 右上角关闭：和底部任务栏按钮一样的淡绿标签
        Button close = NewTagButton(_panel.transform, "Close", "[x]", out TMP_Text ct);
        Place((RectTransform)close.transform, W - 56, 10, 44, UiStyle.TagH + 2f);
        StyleTagButton(close, ct, true);
        close.onClick.AddListener(Close);

        // 四个分页（英文等宽字）
        string[] tabNames = { "upgrade", "craft", "tech", "power" };
        for (int i = 0; i < tabNames.Length; i++)
        {
            Tab t = (Tab)i;
            _tabBtns[i] = NewTagButton(_panel.transform, "Tab" + i, tabNames[i], out _tabTexts[i]);
            Place((RectTransform)_tabBtns[i].transform, 24 + i * 112, 76, 104, 26);
            _tabBtns[i].onClick.AddListener(() => SwitchTab(t));
        }
        Divider(_panel.transform, 24, 114, W - 48);

        // 左：列表区
        GameObject list = new GameObject("List", typeof(RectTransform));
        list.transform.SetParent(_panel.transform, false);
        _listRoot = (RectTransform)list.transform;
        Place(_listRoot, 24, 128, 340, 320);

        // 右：详情区
        _dName = NewText(_panel.transform, "DName", "", 22, Accent, TextAlignmentOptions.Left);
        Place(_dName.rectTransform, 388, 128, 488, 32);
        _dDesc = NewText(_panel.transform, "DDesc", "", 17, SubColor, TextAlignmentOptions.TopLeft);
        _dDesc.enableWordWrapping = true;
        Place(_dDesc.rectTransform, 388, 164, 488, 92);
        _dCost = NewText(_panel.transform, "DCost", "", 17, UiStyle.TextMain, TextAlignmentOptions.TopLeft);
        _dCost.enableWordWrapping = true;
        Place(_dCost.rectTransform, 388, 260, 488, 52);
        _dReq = NewText(_panel.transform, "DReq", "", 16, SubColor, TextAlignmentOptions.TopLeft);
        _dReq.enableWordWrapping = true;
        Place(_dReq.rectTransform, 388, 314, 488, 28);

        // 操作按钮：淡绿标签样式（字用游戏字体，要显示中日文）
        _actionBtn = NewTagButton(_panel.transform, "Action", "", out _actionText);
        if (font != null) _actionText.font = font;
        _actionText.richText = true;
        _actionText.fontSize = 18;
        Place((RectTransform)_actionBtn.transform, 388, 350, 488, 38);
        _actionBtn.onClick.AddListener(OnClickAction);

        _feedback = NewText(_panel.transform, "Feedback", "", 17, UiStyle.TextMain, TextAlignmentOptions.Left);
        _feedback.enableWordWrapping = true;
        Place(_feedback.rectTransform, 388, 396, 488, 44);

        // 底：任务队列（淡绿段标题「queue」）
        _mainOnly.Add(Divider(_panel.transform, 24, 456, W - 48));
        _mainOnly.Add(SectionTag(_panel.transform, "queue", 24, 468));
        _queueText = NewText(_panel.transform, "Queue", "", 17, UiStyle.TextMain, TextAlignmentOptions.TopLeft);
        _queueText.enableWordWrapping = true;
        Place(_queueText.rectTransform, 24, 496, 852, 92);

        BuildPowerUI();
    }

    // 淡绿标签样式的按钮（实心 = 选中 / 可点，只有细框 = 没选中）。字是等宽英文小字。
    Button NewTagButton(Transform parent, string name, string text, out TMP_Text label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = Green;
        UiStyle.Frame((RectTransform)go.transform, new Color(Green.r, Green.g, Green.b, 0.8f), 1f);
        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        label = NewText(go.transform, "Label", text, UiStyle.TagFont + 1f, UiStyle.Ink, TextAlignmentOptions.Center);
        TMP_FontAsset mono = UiStyle.Mono;
        if (mono != null) label.font = mono;
        RectTransform lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(6f, 0f); lrt.offsetMax = new Vector2(-6f, 0f);
        return b;
    }

    static void StyleTagButton(Button b, TMP_Text label, bool on)
    {
        b.GetComponent<Image>().color = on ? Green : new Color(Green.r, Green.g, Green.b, 0.06f);
        label.color = on ? UiStyle.Ink : Green;
    }

    // 段标题：淡绿底色的英文小标签（和资源详细浮窗一样）。返回标签所在的物体（切换分页时显示 / 隐藏用）。
    GameObject SectionTag(Transform parent, string text, float x, float y)
    {
        GameObject go = new GameObject("Section_" + text, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, x, y, 200, UiStyle.TagH);
        _tags.Add(UiStyle.Tag((RectTransform)go.transform, text, Green));
        return go;
    }

    // 一条很淡的淡绿细线（分隔用）。
    GameObject Divider(Transform parent, float x, float y, float w)
    {
        GameObject go = new GameObject("Divider", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, x, y, w, 1f);
        Image img = go.AddComponent<Image>();
        img.color = new Color(Green.r, Green.g, Green.b, 0.22f);
        img.raycastTarget = false;
        return go;
    }

    // ================= 能源页 =================

    void BuildPowerUI()
    {
        _powerRoot = new GameObject("PowerTab", typeof(RectTransform));
        _powerRoot.transform.SetParent(_panel.transform, false);
        Place((RectTransform)_powerRoot.transform, 24, 128, 852, 460);

        _powerSummary = NewText(_powerRoot.transform, "Summary", "", 17, UiStyle.TextMain, TextAlignmentOptions.TopLeft);
        _powerSummary.enableWordWrapping = true;
        Place(_powerSummary.rectTransform, 0, 0, 852, 154);

        SectionTag(_powerRoot.transform, "devices", 0, 160);
        _powerHeader = NewText(_powerRoot.transform, "DevHeader", "", 16, SubColor, TextAlignmentOptions.Left);
        Place(_powerHeader.rectTransform, 0, 184, 852, 24);
        _powerRoot.SetActive(false);
    }

    void SetMainVisible(bool on)
    {
        _listRoot.gameObject.SetActive(on);
        _dName.gameObject.SetActive(on);
        _dDesc.gameObject.SetActive(on);
        _dCost.gameObject.SetActive(on);
        _dReq.gameObject.SetActive(on);
        _feedback.gameObject.SetActive(on);
        _queueText.gameObject.SetActive(on);
        foreach (GameObject g in _mainOnly) if (g != null) g.SetActive(on);
        if (!on) _actionBtn.gameObject.SetActive(false);
        if (_powerRoot != null) _powerRoot.SetActive(!on);
    }

    void RefreshPower()
    {
        bool jp = LanguageGuard.IsJp;
        ClimateSystem cs = ClimateSystem.Instance;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        if (cs != null)
        {
            sb.AppendLine((jp ? "天気：" : "天气：") + cs.HudText(jp) +
                          (jp ? "　風力発電：1台 " : "　风力发电：每台 ") + cs.WindPowerPerTurn.ToString("0.#") + (jp ? " /ターン（AI端末にも内蔵）" : " /回合（终端机也自带一台）"));
        }
        // 水源：收集器存的雨水 → 净化器 → 仓库 → 每日配给
        sb.AppendLine(jp
            ? "水：浄化水 " + WaterWorks.StoredWater + "（1日 " + WaterWorks.DailyDemand.ToString("0.#") + " 消費）　雨水タンク " + WaterWorks.CollectorCount + " 基（雨水 " + WaterWorks.RawTotal + "/" + WaterWorks.RawCapacity + "）　浄水器 " + WaterWorks.PurifierCount + " 台（前ターン +" + WaterWorks.PurifiedLastTurn + "）"
            : "水：净化水 " + WaterWorks.StoredWater + "（每天喝 " + WaterWorks.DailyDemand.ToString("0.#") + "）　雨水收集器 " + WaterWorks.CollectorCount + " 台（存雨水 " + WaterWorks.RawTotal + "/" + WaterWorks.RawCapacity + "）　净化器 " + WaterWorks.PurifierCount + " 台（上回合 +" + WaterWorks.PurifiedLastTurn + "）");

        List<string> unlinked = new List<string>();
        foreach (PowerGrid.Network n in PowerGrid.Networks)
        {
            int devices = 0;
            foreach (PowerNode p in n.nodes) if (p.IsDevice) devices++;
            if (n.Generators == 0 && n.Batteries == 0)
            {
                foreach (PowerNode p in n.nodes) if (p.IsDevice) unlinked.Add(p.DisplayName);
                continue;
            }
            if (n.nodes.Count == 1 && devices == 0 && n.nodes[0].kind == PowerNodeKind.Turbine)
            {
                unlinked.Add(n.nodes[0].DisplayName);
                continue;
            }
            string shed = n.Shed > 0 ? (jp ? "　<color=#FF6B6B>停止 " + n.Shed + " 台</color>" : "　<color=#FF6B6B>断电 " + n.Shed + " 台</color>") : "";
            sb.AppendLine((jp ? "電力網" : "电网") + n.id + "：" + (jp ? "発電 " : "发电 ") + n.Produced.ToString("0.#") +
                          (jp ? "　消費 " : "　用电 ") + n.Consumed.ToString("0.#") + "/" + n.Demand.ToString("0.#") +
                          (jp ? "　蓄電 " : "　储电 ") + n.Stored.ToString("0.#") + "/" + n.Capacity.ToString("0") +
                          (jp ? "（発電機 " : "（发电机 ") + n.Generators + (jp ? "・電池 " : "、电池 ") + n.Batteries + (jp ? "・設備 " : "、设备 ") + devices + (jp ? "）" : "）") + shed);
        }
        if (unlinked.Count > 0)
            sb.AppendLine((jp ? "<color=#FFB060>未接続：" : "<color=#FFB060>没接电线：") + string.Join("、", unlinked) + "</color>");
        sb.Append(jp
            ? "<size=85%><color=#9DB3A4>「電線」で発電機・蓄電池・設備をつなぐ。余った電気は蓄電池へ（効率 " + (PowerGrid.ChargeEfficiency * 100f).ToString("0") + "%）。足りない時は浄水器から先に止まる。ヒーター／クーラー 1/2/3段階：消費 " + PowerGrid.ClimateLevelPower[1] + "/" + PowerGrid.ClimateLevelPower[2] + "/" + PowerGrid.ClimateLevelPower[3] + "、±" + PowerGrid.DegreesPerLevel + "℃ずつ（半径" + PowerGrid.RadiusCells + "マス）。浄水器：1段階で最大 " + WaterWorks.PurifyPerLevel + " 杯、浄化した分だけ消費。</color></size>"
            : "<size=85%><color=#9DB3A4>用「电线」把发电机、蓄电池和设备连起来。多余的电充进电池（效率 " + (PowerGrid.ChargeEfficiency * 100f).ToString("0") + "%），电不够时先停净化器。加温器/降温器 1/2/3 档耗电 " + PowerGrid.ClimateLevelPower[1] + "/" + PowerGrid.ClimateLevelPower[2] + "/" + PowerGrid.ClimateLevelPower[3] + "，每档 ±" + PowerGrid.DegreesPerLevel + "℃（半径 " + PowerGrid.RadiusCells + " 格）；净化器每档最多净化 " + WaterWorks.PurifyPerLevel + " 份，按净化量耗电。</color></size>");
        _powerSummary.text = sb.ToString();

        // 设备列表（加温器 / 降温器 / 水源净化器）
        List<PowerNode> devs = new List<PowerNode>();
        foreach (PowerNode p in PowerNode.All) if (p != null && p.IsDevice) devs.Add(p);
        devs.Sort((a, b) => a.kind != b.kind ? a.kind.CompareTo(b.kind) : a.Cell.x != b.Cell.x ? a.Cell.x.CompareTo(b.Cell.x) : a.Cell.y.CompareTo(b.Cell.y));
        _powerHeader.text = devs.Count == 0
            ? (jp ? "電気を使う設備はまだない（ヒーター／クーラーは「生活」・端末 Lv1、浄水器は「生産」から）" : "还没有用电设备（加温器 / 降温器在「生活」里建，需要终端机 Lv1；水源净化器在「生产」里）")
            : (jp ? "設備（段階 0＝オフ、最大 " : "设备（档位 0 = 关，最高 ") + PowerGrid.MaxLevel + (jp ? "）" + (devs.Count > MaxDevRows ? "　ほか " + (devs.Count - MaxDevRows) + " 台" : "") : "）" + (devs.Count > MaxDevRows ? "　还有 " + (devs.Count - MaxDevRows) + " 台没显示" : ""));

        bool same = devs.Count == _devRows.Count || (devs.Count > MaxDevRows && _devRows.Count == MaxDevRows);
        if (same) for (int i = 0; i < _devRows.Count; i++) if (_devRows[i].node != devs[i]) { same = false; break; }
        if (!same) RebuildDevRows(devs);

        foreach (DevRow r in _devRows)
        {
            if (r.node == null) continue;
            PowerNode d = r.node;
            r.label.text = d.DisplayName + (jp ? "　段階 " : "　档位 ") + "<color=#B8FFC7>" + d.level + "</color>";
            string st;
            if (!d.IsBuilt) st = jp ? "<color=#9DB3A4>建設中</color>" : "<color=#9DB3A4>施工中</color>";
            else if (d.level == 0) st = jp ? "<color=#9DB3A4>オフ</color>" : "<color=#9DB3A4>关闭</color>";
            else if (d.kind == PowerNodeKind.Purifier && d.powered) st = "<color=#5FD69A>" + (jp ? "浄化中 " : "净化中 ") + d.workAmount + (jp ? "杯" : "份") + " −" + PowerGrid.PurifierPower(d.workAmount).ToString("0.#") + (jp ? "電" : "电") + "</color>";
            else if (d.kind == PowerNodeKind.Purifier && WaterWorks.RawTotal <= 0) st = jp ? "<color=#9DB3A4>待機（雨水なし）</color>" : "<color=#9DB3A4>待机（没有雨水）</color>";
            else if (d.powered) st = "<color=#5FD69A>" + (d.kind == PowerNodeKind.Heater ? "+" : "−") + (d.level * PowerGrid.DegreesPerLevel) + "℃ −" + PowerGrid.DemandOf(d).ToString("0.#") + (jp ? "電" : "电") + "</color>";
            else if (d.network == null || (d.network.Generators == 0 && d.network.Batteries == 0)) st = jp ? "<color=#FF6B6B>未接続</color>" : "<color=#FF6B6B>没接电</color>";
            else st = jp ? "<color=#FF6B6B>電力不足</color>" : "<color=#FF6B6B>电力不足</color>";
            r.status.text = st;
        }
    }

    void RebuildDevRows(List<PowerNode> devs)
    {
        foreach (DevRow r in _devRows) if (r.root != null) Destroy(r.root);
        _devRows.Clear();
        for (int i = 0; i < devs.Count && i < MaxDevRows; i++)
        {
            PowerNode node = devs[i];
            float y = 214 + i * 40;
            GameObject root = new GameObject("Dev_" + i, typeof(RectTransform));
            root.transform.SetParent(_powerRoot.transform, false);
            Place((RectTransform)root.transform, 0, y, 852, 36);
            Image bg = root.AddComponent<Image>();
            bg.color = RowColor;

            TMP_Text label = NewText(root.transform, "Label", "", 18, UiStyle.TextMain, TextAlignmentOptions.Left);
            Place(label.rectTransform, 12, 0, 520, 36);
            Button minus = NewTagButton(root.transform, "Minus", "-", out TMP_Text mt);
            StyleTagButton(minus, mt, false);
            Place((RectTransform)minus.transform, 540, 5, 44, 26);
            minus.onClick.AddListener(() => { PowerGrid.SetLevel(node, node.level - 1); RefreshPower(); });
            Button plus = NewTagButton(root.transform, "Plus", "+", out TMP_Text pt);
            StyleTagButton(plus, pt, false);
            Place((RectTransform)plus.transform, 592, 5, 44, 26);
            plus.onClick.AddListener(() => { PowerGrid.SetLevel(node, node.level + 1); RefreshPower(); });
            TMP_Text status = NewText(root.transform, "Status", "", 17, UiStyle.TextMain, TextAlignmentOptions.Left);
            Place(status.rectTransform, 648, 0, 200, 36);

            _devRows.Add(new DevRow { node = node, root = root, label = label, status = status });
        }
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
            label.fontSize = 18;
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
        label = NewText(go.transform, "Label", "", 20, UiStyle.TextMain, TextAlignmentOptions.Center);
        RectTransform lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        return b;
    }

    static string L(string key) => ProgressionRules.L(key);
}
