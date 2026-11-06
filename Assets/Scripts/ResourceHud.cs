using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 资源栏（运行时生成，不用改场景）：
//   · 屏幕左边、事件 / 日志栏下面，由上到下：电力 | 净化水 | 木材 | 石头 | 矿石。
//     每一个 = 圆形格子刻度环（ArcGauge：24 个一样大的小方格，从正上方顺时针亮起）+ 环正中间的符号图标。
//     不写字、不显示数字，看符号认（闪电 / 水滴 / 树 / 石头 / 晶体，代码画的发光图标）。所有环一样大。
//     刻度环 = 进度：电力 = 储电 / 电池容量；净化水 = 够喝的时间 / 参谋"偏低线"的 2 倍；材料 = 数量 / MatFull。
//     这一列跟着日志抽屉走：抽屉展开时贴在它下沿下面，收起时往上挪（HudLayout.LeftColumnTop）；不会超出屏幕下沿。
//   · 点任意一个资源图标：在右边弹出一个大框，所有资源的详细数据都在里面（再点一下收起）。
//     打开动画和 NPC 对话窗口一样（UiPopup）；每种资源一段，段标题是淡绿底色的英文标签（power / water / wood / stone / ore）。
//   · 顶部中间只剩「终端机」按钮（天数在右上角，见 HudLayout）。
//   · 颜色（HudGlow）：充足 = 淡蓝；平时 = 淡绿；不足 = 淡黄 + 不规则闪烁。
//       电力：充足 = 电池八成以上、没断电、没有没接线的设备；不足 = 有设备断电，或电池快空了还在亏电。
//       净化水：充足 = 刻度环满格；不足 = 有人口渴，或只够喝参谋"紧急线"以内（默认 60 秒）。
//       材料：充足 = 满 MatFull 个；不足 = 领导定的建造项目还缺它 / 营地还没围好、石头不够修一段墙。
// 由 HudLayout 在生成天数之后调用 ResourceHud.Create。数据每半秒刷新一次（用不受暂停影响的时间）。
public class ResourceHud : MonoBehaviour
{
    public static ResourceHud Instance { get; private set; }

    const float PanelH = 64f, TermW = 92f, TopY = -8f;
    const float RingSize = 64f, IconSize = 46f, RingGap = 10f;  // 刻度环直径 / 环中间的图标 / 上下间距（所有资源一样大）
    const int MatFull = 20;                                     // 材料到这么多算刻度环满格（= 充足）
    const float DetailW = 330f;                                 // 详细数据大框的宽度
    static readonly string[] Keys = { "power", "water", ResourceInfo.WoodItem, ResourceInfo.StoneItem, ResourceInfo.OreItem };
    static readonly IconShape[] Shapes = { IconShape.Bolt, IconShape.Drop, IconShape.Tree, IconShape.Rock, IconShape.Ore };

    static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.1f, 0.85f);
    static readonly Color SubColor = new Color(0.68f, 0.72f, 0.8f);

    enum IconShape { Bolt, Drop, Tree, Rock, Ore }

    class Cell
    {
        public string key;
        public HudGlow glow;
        public ArcGauge ring;   // 上层（按比例亮）
        public RectTransform rt;
    }

    class Section
    {
        public string key;
        public RectTransform head;
        public TextMeshProUGUI text;
    }

    TMP_FontAsset _font;
    TextMeshProUGUI _termTitle, _termSub;
    Image _termBg;
    RectTransform _col;
    readonly Cell[] _cells = new Cell[5];
    float _timer, _colY = -1f;

    // 详细数据大框
    UiPopup _detail;
    readonly List<Section> _sections = new List<Section>();
    float _detailTimer;

    // root = 主界面 Canvas。
    public static ResourceHud Create(Transform root, TMP_FontAsset font)
    {
        if (Instance != null || root == null) return Instance;
        RectTransform rt = NewRect("HudResources", root);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        ResourceHud hud = rt.gameObject.AddComponent<ResourceHud>();
        hud._font = font;
        hud.Build(rt, root);
        return hud;
    }

    void Awake() { if (Instance == null) Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Build(RectTransform top, Transform canvas)
    {
        // 顶部中间：终端机快捷按钮
        RectTransform term = NewRect("Terminal", top);
        term.anchorMin = term.anchorMax = new Vector2(0.5f, 1f);
        term.pivot = new Vector2(0.5f, 1f);
        term.anchoredPosition = new Vector2(0f, TopY);
        term.sizeDelta = new Vector2(TermW, PanelH);
        Image border = term.gameObject.AddComponent<Image>();
        border.color = new Color(0.72f, 1f, 0.78f, 0.6f); // 淡绿色边框（和其它界面统一，见 UiStyle）
        RectTransform inner = NewRect("Bg", term);
        Stretch(inner, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        _termBg = inner.gameObject.AddComponent<Image>();
        _termBg.color = PanelBg;
        _termBg.raycastTarget = false;
        Button b = term.gameObject.AddComponent<Button>();
        b.targetGraphic = border;
        b.onClick.AddListener(OnTerminalClicked);
        _termTitle = Text(term, "Title", 18f, Color.white, TextAlignmentOptions.Center, new Vector2(0f, -8f), new Vector2(TermW, 26f));
        _termTitle.fontStyle = FontStyles.Bold;
        _termSub = Text(term, "Sub", 14f, SubColor, TextAlignmentOptions.Center, new Vector2(0f, -36f), new Vector2(TermW, 22f));

        // 左边：电力 | 净化水 | 木材 | 石头 | 矿石（事件 / 日志栏下面，由上到下）
        _col = NewRect("HudResourceColumn", canvas);
        _col.anchorMin = _col.anchorMax = _col.pivot = new Vector2(0f, 1f);
        _col.sizeDelta = new Vector2(RingSize, Keys.Length * (RingSize + RingGap) - RingGap);
        for (int i = 0; i < Keys.Length; i++) _cells[i] = BuildCell(_col, i);
        PlaceColumn(0f);

        Refresh();
    }

    // 一个资源：圆形格子刻度环 + 环正中间的符号图标。点一下：弹出 / 收起详细数据大框。
    Cell BuildCell(RectTransform col, int i)
    {
        Cell c = new Cell { key = Keys[i] };
        c.rt = NewRect("Res_" + c.key, col);
        c.rt.anchorMin = c.rt.anchorMax = c.rt.pivot = new Vector2(0f, 1f);
        c.rt.anchoredPosition = new Vector2(0f, -i * (RingSize + RingGap));
        c.rt.sizeDelta = new Vector2(RingSize, RingSize);

        Image hit = c.rt.gameObject.AddComponent<Image>();   // 看不见，只用来接收点击
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        Button btn = c.rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = hit;
        btn.transition = Selectable.Transition.None;
        RectTransform from = c.rt;
        btn.onClick.AddListener(() => ToggleDetail(from));

        c.glow = c.rt.gameObject.AddComponent<HudGlow>();
        ArcGauge back = Ring(c.rt, "RingBack");             // 刻度底：一整圈、淡
        back.SetFill(1f);
        foreach (Image s in back.Segments) c.glow.AddGraphic(s, 0.32f);
        c.ring = Ring(c.rt, "Ring");                        // 按比例亮
        c.ring.SetFill(0f);
        foreach (Image s in c.ring.Segments) c.glow.AddGraphic(s, 1f);
        RawImage icon = Icon(c.rt, RingSize * 0.5f, IconSize);
        c.glow.AddIcon(icon, GlowIcon(Shapes[i], 0), GlowIcon(Shapes[i], 1), 1f, GlowIcon(Shapes[i], 2));
        return c;
    }

    static ArcGauge Ring(RectTransform cell, string name)
    {
        RectTransform rt = NewRect(name, cell);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(RingSize, RingSize);
        ArcGauge g = rt.gameObject.AddComponent<ArcGauge>();
        g.segments = 24;
        g.thickness = 5f;
        g.gapDeg = 5f;
        g.startDeg = 90f;
        g.Build(RingSize);
        return g;
    }

    void Update()
    {
        PlaceColumn(Time.unscaledDeltaTime);
        if (_detail != null && _detail.IsOpen)
        {
            _detailTimer -= Time.unscaledDeltaTime;
            if (_detailTimer <= 0f) { _detailTimer = 0.5f; _detail.Resize(LayoutDetail()); }
        }
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = 0.5f;
        Refresh();
    }

    // 左边资源栏：贴在事件 / 日志抽屉下面（抽屉高度变了 / 收起了就平滑地挪过去），不超出屏幕下沿。
    void PlaceColumn(float dt)
    {
        if (_col == null) return;
        HudLayout hl = HudLayout.Instance;
        float target = hl != null ? hl.LeftColumnTop() : 10f;
        RectTransform canvasRt = _col.parent as RectTransform;
        if (canvasRt != null) target = Mathf.Min(target, canvasRt.rect.height - _col.sizeDelta.y - 8f);
        target = Mathf.Max(target, 10f);
        float x = hl != null ? hl.LeftMargin : 10f;
        _colY = _colY < 0f || dt <= 0f ? target : Mathf.Lerp(_colY, target, 1f - Mathf.Exp(-12f * dt));
        _col.anchoredPosition = new Vector2(x, -_colY);
    }

    Cell CellOf(string key)
    {
        foreach (Cell c in _cells) if (c != null && c.key == key) return c;
        return null;
    }

    void Refresh()
    {
        bool jp = LanguageGuard.IsJp;

        // —— 电力 ——
        Cell pc = CellOf("power");
        PowerGrid.Summary p = PowerGrid.Total();
        if (p.capacity <= 0f && p.produced <= 0f)
        {
            pc.ring.SetFill(0f);
            pc.glow.SetWarning(false);
            pc.glow.SetGood(false);
        }
        else
        {
            pc.ring.SetFill(p.capacity > 0f ? p.stored / p.capacity : 0f);
            // 电力不足：有设备被断电，或者电池快空了而且还在亏电
            float net = p.produced - p.consumed;
            bool lowPower = p.shed > 0 || (p.capacity > 0f && p.stored < p.capacity * 0.15f && net < 0f);
            pc.glow.SetWarning(lowPower);
            pc.glow.SetGood(!lowPower && p.capacity > 0f && p.stored >= p.capacity * 0.8f && p.unlinkedDevices == 0);
        }

        // —— 净化水 ——
        Cell wc = CellOf("water");
        int water = WaterWorks.StoredWater;
        float ratio = WaterRatio();
        wc.ring.SetFill(ratio);
        bool lowWater = WaterWorks.ThirstyCount > 0 || WaterWorks.DaysLeft < CampAdvisor.WaterUrgentDays;
        wc.glow.SetWarning(lowWater);
        wc.glow.SetGood(!lowWater && water > 0 && ratio >= 1f);

        // —— 材料 ——
        WorldState world = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        bool needWalls = NpcIntentBrain.WallsNeeded();
        int wallCost = WallStone();
        for (int i = 2; i < _cells.Length; i++)
        {
            Cell c = _cells[i];
            int have = world != null ? ItemUseRules.GetCount(c.key, world) : 0;
            c.ring.SetFill((float)have / MatFull);
            bool warn = ProjectLack(c.key, out _) > 0 || (c.key == ResourceInfo.StoneItem && needWalls && have < wallCost);
            c.glow.SetWarning(warn);
            c.glow.SetGood(!warn && have >= MatFull);
        }

        // —— 终端机 ——
        TerminalController t = TerminalController.Instance;
        _termTitle.text = jp ? "AI端末" : "终端机";
        if (t == null) { _termSub.text = jp ? "未建設" : "未建造"; _termTitle.color = SubColor; }
        else if (!t.isBuilt) { _termSub.text = jp ? "建設中" : "施工中"; _termTitle.color = SubColor; }
        else
        {
            int lv = WorldManager.Instance != null ? WorldManager.Instance.world.terminalLevel : 1;
            _termSub.text = "Lv" + lv;
            _termTitle.color = Color.white;
        }
    }

    // 净化水刻度环：够喝到参谋"偏低线"的 2 倍算满格。
    static float WaterRatio()
    {
        int water = WaterWorks.StoredWater;
        float days = WaterWorks.DaysLeft;
        float full = Mathf.Max(0.01f, CampAdvisor.WaterLowDays * 2f);
        return float.IsInfinity(days) ? (water > 0 ? 1f : 0f) : Mathf.Clamp01(days / full);
    }

    // 点「终端机」：建好了就打开终端面板；没建 / 施工中就告诉玩家怎么办。
    void OnTerminalClicked()
    {
        TerminalController t = TerminalController.Instance;
        string msg = null;
        if (t == null)
            msg = LanguageGuard.Pick("还没有 AI终端机：点右下角「建造」→「基础」→「AI终端机」，放下后退出建造模式，NPC 会去施工。",
                                     "AI端末がまだない：右下の「建築」→「基礎」→「AI端末」を置き、建築モードを出るとNPCが建てに行く。");
        else if (!t.isBuilt)
            msg = LanguageGuard.Pick("AI终端机还在施工，等 NPC 建好再打开。", "AI端末はまだ建設中。NPCが建て終わるまで待とう。");
        else if (TerminalPanelUI.Instance != null) TerminalPanelUI.Instance.Open();
        else msg = LanguageGuard.Pick("场景里没有终端面板（TerminalPanelUI），打不开。", "シーンに端末パネル（TerminalPanelUI）がない。");
        if (msg != null && EventLogManager.Instance != null && WorldManager.Instance != null)
            EventLogManager.Instance.AddEvent(WorldManager.Instance.world.day, msg);
    }

    // ============ 详细数据大框（点任意资源图标）============

    void ToggleDetail(RectTransform from)
    {
        if (_detail != null && _detail.IsOpen) { _detail.Close(); return; }
        if (_detail == null) BuildDetail();
        Vector2 size = LayoutDetail();
        _detailTimer = 0.5f;
        Vector3[] c = UiPopup.ScreenCorners(from);   // 0 左下、1 左上、2 右上、3 右下
        Vector3[] col = UiPopup.ScreenCorners(_col);
        float k = _detail.UnitToPixels;
        // 大框在资源栏右边，上沿比点的那个图标低一点：线从图标右上角斜着落到大框右上角，不压在边框上
        Vector2 topLeft = new Vector2(col[2].x + 18f * k, c[2].y - 14f * k);
        _detail.Open(c[2], topLeft, size);
    }

    void BuildDetail()
    {
        _detail = UiPopup.Create("ResourceDetail", 75);   // 自己一个画布（不挂在主界面画布下面）
        _detail.SetTag("resources");
        foreach (string key in Keys)
        {
            Section s = new Section { key = key };
            s.head = NewRect("Head_" + key, _detail.Body);
            s.head.anchorMin = s.head.anchorMax = s.head.pivot = new Vector2(0f, 1f);
            s.head.sizeDelta = new Vector2(DetailW - 24f, UiStyle.TagH);
            UiStyle.Tag(s.head, key, UiStyle.Green);           // 段标题：淡绿底色的英文标签
            s.text = Text(_detail.Body, "Text_" + key, 14f, UiStyle.TextMain, TextAlignmentOptions.TopLeft, Vector2.zero, new Vector2(DetailW - 28f, 20f));
            s.text.richText = true;
            s.text.textWrappingMode = TextWrappingModes.Normal;
            s.text.overflowMode = TextOverflowModes.Overflow;
            _sections.Add(s);
        }
    }

    // 填文字、从上往下排好，返回大框的大小。
    Vector2 LayoutDetail()
    {
        bool jp = LanguageGuard.IsJp;
        float y = UiStyle.TagH + 12f;   // 大框自己的标签下面开始
        foreach (Section s in _sections)
        {
            StringBuilder sb = new StringBuilder();
            switch (s.key)
            {
                case "power": PowerText(sb); break;
                case "water": WaterText(sb, jp); break;
                default:      MaterialText(sb, s.key); break;
            }
            string text = sb.ToString().TrimEnd('\n');
            s.head.anchoredPosition = new Vector2(12f, -y);
            y += UiStyle.TagH + 5f;
            s.text.text = text;
            float h = Mathf.Ceil(s.text.GetPreferredValues(text, DetailW - 28f, 0f).y);
            RectTransform tr = s.text.rectTransform;
            tr.anchoredPosition = new Vector2(16f, -y);
            tr.sizeDelta = new Vector2(DetailW - 28f, h);
            y += h + 12f;
        }
        return new Vector2(DetailW, y);
    }

    void PowerText(StringBuilder sb)
    {
        PowerGrid.Summary p = PowerGrid.Total();
        int turbines = 0, batteries = 0;
        foreach (PowerNode n in PowerNode.All)
        {
            if (n == null || !n.IsBuilt) continue;
            if (n.kind == PowerNodeKind.Turbine) turbines++;
            else if (n.kind == PowerNodeKind.Battery) batteries++;
        }
        sb.AppendLine(L("tip_power_title"));
        if (p.capacity <= 0f && p.produced <= 0f) sb.AppendLine(L("tip_power_none"));
        else
        {
            sb.AppendLine(LF("tip_power_stored", p.stored.ToString("0.#"), p.capacity.ToString("0")));
            sb.AppendLine(LF("tip_power_flow", p.produced.ToString("0.#"), p.consumed.ToString("0.#")));
            sb.AppendLine(LF("tip_power_devices", turbines, batteries));
            if (p.shed > 0) sb.AppendLine(Warn(LF("tip_power_shed", p.shed)));
            if (p.unlinkedDevices > 0) sb.AppendLine(Warn(LF("tip_power_unlinked", p.unlinkedDevices)));
            if (CellOf("power").glow.Good) sb.AppendLine(Good(L("tip_state_good")));
        }
        sb.AppendLine(Hint(L("tip_power_hint")));
    }

    void WaterText(StringBuilder sb, bool jp)
    {
        sb.AppendLine(L("tip_water_title"));
        sb.AppendLine(LF("tip_water_stored", WaterWorks.StoredWater));
        sb.AppendLine(LF("tip_water_demand", WaterWorks.DailyDemand.ToString("0.0"), CampAdvisor.DaysPhrase(WaterWorks.DaysLeft, jp)));
        int c = WaterWorks.CollectorCount, p = WaterWorks.PurifierCount;
        sb.AppendLine(c == 0 ? Warn(L("tip_water_no_collector")) : LF("tip_water_collect", c, WaterWorks.RawTotal, WaterWorks.RawCapacity));
        if (p == 0) sb.AppendLine(Warn(L("tip_water_no_purifier")));
        else
        {
            sb.AppendLine(LF("tip_water_purify", p, WaterWorks.PurifiedLastTurn));
            if (!CampAdvisor.PurifierLinked()) sb.AppendLine(Warn(L("tip_water_unlinked")));
        }
        int thirsty = WaterWorks.ThirstyCount;
        if (thirsty > 0) sb.AppendLine(Warn(LF("tip_water_thirsty", thirsty)));
        ClimateSystem cs = ClimateSystem.Instance;
        if (cs != null && (cs.Rain != PrecipLevel.None || cs.Snow != PrecipLevel.None)) sb.AppendLine(L("tip_water_rain"));
        if (CellOf("water").glow.Good) sb.AppendLine(Good(L("tip_state_good")));
        sb.AppendLine(Hint(L("tip_water_hint")));
    }

    void MaterialText(StringBuilder sb, string id)
    {
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        int have = w != null ? ItemUseRules.GetCount(id, w) : 0;
        sb.AppendLine(ItemName(id) + "　" + LF("tip_mat_have", have));
        sb.AppendLine(L("tip_mat_from_" + id));

        // 能用来建什么（已解锁的建筑里要用到它的）
        List<string> uses = new List<string>();
        foreach (BuildableData d in NpcIntentBrain.BuildCandidates())
            foreach (MaterialCost c in NpcIntentBrain.CostOf(d))
                if (c != null && c.materialName == id && c.amount > 0) uses.Add(d.LocalizedName + "×" + c.amount);
        sb.AppendLine(uses.Count > 0 ? LF("tip_mat_used", string.Join(LanguageGuard.Pick("、", "・"), uses)) : Hint(L("tip_mat_unused")));

        int lack = ProjectLack(id, out BuildableData project);
        if (lack > 0) sb.AppendLine(Warn(LF("tip_mat_project", project.LocalizedName, lack)));
        if (id == ResourceInfo.StoneItem && NpcIntentBrain.WallsNeeded() && CampZone.Instance != null)
            sb.AppendLine(LF("tip_mat_wall", CampZone.Instance.OpenCount, WallStone()));

        // 谁在采
        List<string> names = new List<string>();
        if (w != null)
            foreach (NpcData n in w.npcs)
                if (n != null && !n.IsDead && n.role == "gather" && n.roleTarget == id) names.Add(n.npcName);
        sb.AppendLine(names.Count > 0 ? LF("tip_mat_gatherers", string.Join(LanguageGuard.Pick("、", "・"), names)) : Hint(L("tip_mat_nobody")));
        if (have >= MatFull) sb.AppendLine(Good(L("tip_state_good")));
    }

    static string Warn(string s) => "<color=#FFE36A>" + s + "</color>";
    static string Good(string s) => "<color=#A8E0FF>" + s + "</color>";
    static string Hint(string s) => "<color=#8E9490>" + s + "</color>";

    // 领导定的建造项目还缺多少这种材料（没有项目 / 不缺 = 0）。
    static int ProjectLack(string id, out BuildableData project)
    {
        project = NpcSociety.Project;
        if (project == null) return 0;
        foreach (MaterialCost c in NpcIntentBrain.CostOf(project))
            if (c != null && c.materialName == id)
            {
                WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
                int have = w != null ? ItemUseRules.GetCount(id, w) : 0;
                return Mathf.Max(0, c.amount - have);
            }
        return 0;
    }

    // 修一段墙要几块石头。
    static int WallStone()
    {
        BuildableData wall = CampZone.WallData();
        if (wall == null) return 2;
        foreach (MaterialCost c in NpcIntentBrain.CostOf(wall))
            if (c != null && c.materialName == ResourceInfo.StoneItem) return Mathf.Max(1, c.amount);
        return 2;
    }

    // ============ 小工具 ============

    // 图标：中心在 (centerX, 竖直居中)，size 见方（贴图四周留了发光的边，实际符号约占六成）。
    RawImage Icon(RectTransform panel, float centerX, float size)
    {
        RectTransform rt = NewRect("Icon", panel);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(centerX, 0f);
        rt.sizeDelta = new Vector2(size, size);
        RawImage img = rt.gameObject.AddComponent<RawImage>();
        img.raycastTarget = false;
        return img;
    }

    TextMeshProUGUI Text(RectTransform parent, string name, float size, Color color, TextAlignmentOptions align, Vector2 pos, Vector2 sizeDelta)
    {
        RectTransform rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = _font != null ? _font : GameFonts.Find();
        if (f != null) t.font = f;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    static string ItemName(string id) => string.IsNullOrEmpty(id) ? "" : ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(id) : GameLexicon.NameOf(id);
    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;

    // ============ 代码画的发光符号 ============
    // 闪电 / 水滴 / 树 / 石头 / 矿石 × 淡绿 / 淡黄 / 淡蓝三套（state 0 / 1 / 2），每张全局只画一次。
    static readonly Dictionary<int, Texture2D> _iconTex = new Dictionary<int, Texture2D>();

    static Texture2D GlowIcon(IconShape shape, int state)
    {
        int key = (int)shape * 3 + state;
        if (_iconTex.TryGetValue(key, out Texture2D cached) && cached != null) return cached;
        const int N = 96;                    // 贴图 96 像素，符号画在中间 56 像素，四周留给发光
        const float S = 56f / 32f, O = (N - 56) * 0.5f;
        float[] cov = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int hit = 0;                 // 4×4 超采样抗锯齿
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                        if (IconHit(shape, new Vector2((x + (sx + 0.5f) / 4f - O) / S, (y + (sy + 0.5f) / 4f - O) / S))) hit++;
                cov[y * N + x] = hit / 16f;
            }
        Color c = state == 1 ? HudGlow.Yellow : state == 2 ? HudGlow.Blue : HudGlow.Green;
        Color g = state == 1 ? HudGlow.YellowGlow : state == 2 ? HudGlow.BlueGlow : HudGlow.GreenGlow;
        SegmentPainter.Style st = new SegmentPainter.Style
        {
            r = c.r, g = c.g, b = c.b, gr = g.r, gg = g.g, gb = g.b,
            glow = 0.75f, glowRadius = 9f,
        };
        byte[] px = SegmentPainter.Compose(cov, null, N, N, st);
        Color32[] cols = new Color32[N * N];
        for (int k = 0; k < cols.Length; k++) cols[k] = new Color32(px[k * 4], px[k * 4 + 1], px[k * 4 + 2], px[k * 4 + 3]);
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.name = "Icon" + shape + state;
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        t.SetPixels32(cols);
        t.Apply(false);
        _iconTex[key] = t;
        return t;
    }

    // 符号形状（32×32 的坐标，y 朝上）
    static readonly Vector2[] BoltPoly = { new Vector2(18, 31), new Vector2(6, 14), new Vector2(15, 14), new Vector2(12, 1), new Vector2(26, 19), new Vector2(17, 19), new Vector2(22, 31) };
    // 树：三层三角形的松树 + 树干
    static readonly Vector2[] TreeTop = { new Vector2(16, 31), new Vector2(23, 20), new Vector2(9, 20) };
    static readonly Vector2[] TreeMid = { new Vector2(16, 25), new Vector2(26, 13), new Vector2(6, 13) };
    static readonly Vector2[] TreeLow = { new Vector2(16, 18), new Vector2(29, 6), new Vector2(3, 6) };
    // 石头：一大一小两块有棱角的石头，大的上面一道裂缝
    static readonly Vector2[] RockPoly = { new Vector2(2, 3), new Vector2(21, 3), new Vector2(24, 10), new Vector2(20, 21), new Vector2(11, 25), new Vector2(4, 19), new Vector2(1, 10) };
    static readonly Vector2[] RockSmall = { new Vector2(22.5f, 3), new Vector2(31, 3), new Vector2(31, 9), new Vector2(27.5f, 13), new Vector2(24.5f, 11) };
    static readonly Vector2[] RockCrack = { new Vector2(11, 26), new Vector2(13.5f, 16), new Vector2(9, 10), new Vector2(10.5f, 6) };
    // 矿石：中间一根大晶体 + 两边各一根小晶体
    static readonly Vector2[] OreBig = { new Vector2(16, 31), new Vector2(22, 17), new Vector2(16, 2), new Vector2(10, 17) };
    static readonly Vector2[] OreL = { new Vector2(7, 23), new Vector2(10.5f, 12), new Vector2(7, 3), new Vector2(3.5f, 12) };
    static readonly Vector2[] OreR = { new Vector2(25, 21), new Vector2(28.5f, 11), new Vector2(25, 3), new Vector2(21.5f, 11) };

    static bool IconHit(IconShape shape, Vector2 p)
    {
        switch (shape)
        {
            case IconShape.Bolt: return InPoly(p, BoltPoly);
            case IconShape.Drop:
            {
                // 水滴：下面一个圆 + 上面一个尖角
                float dx = p.x - 16f, dy = p.y - 12f;
                bool circle = dx * dx + dy * dy <= 10f * 10f;
                bool cone = p.y >= 12f && p.y <= 30f && Mathf.Abs(dx) <= 10f * (30f - p.y) / 18f;
                return circle || cone;
            }
            case IconShape.Tree:
                return InPoly(p, TreeTop) || InPoly(p, TreeMid) || InPoly(p, TreeLow) || (p.x >= 13.5f && p.x <= 18.5f && p.y >= 1f && p.y <= 7f);
            case IconShape.Rock:
                return (InPoly(p, RockPoly) && DistToPolyline(p, RockCrack) > 1.1f) || InPoly(p, RockSmall);
            case IconShape.Ore:
                return InPoly(p, OreBig) || InPoly(p, OreL) || InPoly(p, OreR);
            default: return false;
        }
    }

    static bool InPoly(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    static float DistToPolyline(Vector2 p, Vector2[] line)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vector2 a = line[i], b = line[i + 1], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            best = Mathf.Min(best, (a + ab * t - p).magnitude);
        }
        return best;
    }
}
