using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

// AI 终端机面板：点击终端机打开。画面是一台老电脑的命令台（TerminalConsole）：
//   · 第一次启动（以及断电后重新上线）：显像管开机 → 一屏 BIOS / DOS 风格的开机滚动（TerminalBoot，脚本在本地化表 term_boot_xx）
//     → login: AI → ACCESS GRANTED → 主菜单。按任意键 / 点屏幕可以跳过。之后再打开直接进主菜单。
//   · 主菜单：终端机信息（等级、算力槽、发电、储电、用电、天气、净化水）+ 分级菜单：
//       [1] 科技     → 科技列表 → 科技详情（研究）
//       [2] 登录成员 → 成员列表（NPC）→ 成员档案（建立通讯 = 打开对话 / 定位 = 镜头移过去）
//       [3] 合成     → 配方列表 → 配方详情（合成）
//       [4] 升级     → 升级列表 → 升级详情（升级）
//       [5] 能源管理 → 电网 / 水源概况 + 用电设备 → 设备（调档位）
//       [6] 任务队列 （算力槽里正在处理 / 排队的任务）
//       [0] 注销     （显像管关机，关掉面板）
//   · 操作：数字键选菜单（或鼠标点那一行），Backspace / Esc 返回上一级（主菜单按 Esc = 注销）。
//     列表一页 7 项，[8] 上一页 [9] 下一页。按下的数字会先出现在提示符后面，再执行。
//   · 终端机断电 / 停机：显示离线画面；恢复后自动重新开机。
// 窗口还是游戏里统一的浮窗（UiPopup：深色底 + 淡绿细框 + 左上角标签「terminal : online」），打开时上沿拉开再往下展开（没有从终端机伸过来的线）。
// 挂在场景里的常驻空物体上（和 TerminalDatabase 放一起即可），不要挂在终端机预制体上。
public class TerminalPanelUI : MonoBehaviour
{
    public static TerminalPanelUI Instance { get; private set; }

    [Header("字体（拖入中日文 TMP 字体，否则中文会变方块）")]
    public TMP_FontAsset font;

    [Header("设置")]
    public float refreshInterval = 0.25f; // 打开时多久刷新一次（进度条要动，所以快一点）
    [Tooltip("每次打开都播开机滚动（调试用）；关掉 = 只在第一次启动 / 断电后重新上线时播")]
    public bool bootEveryTime = false;

    enum TPage { Offline, Home, TechList, TechDetail, Users, UserDetail, CraftList, CraftDetail, UpgradeList, UpgradeDetail, Power, Device, Jobs }

    const float W = 960f, H = 620f;   // 浮窗大小（1920×1080 下的界面单位）
    const int PerPage = 7;            // 列表一页几项（数字键 1~7；8 / 9 翻页；0 返回）
    const int DevPerPage = 5;         // 能源页上面还有概况，设备一页少放几个

    const string OkHex = TerminalConsole.OkHex, WarnHex = TerminalConsole.WarnHex, BadHex = TerminalConsole.BadHex;
    const string PurpleHex = TerminalConsole.PurpleHex, BrightHex = TerminalConsole.BrightHex;
    static readonly Color OkColor = new Color(0.455f, 0.86f, 0.55f, 1f);
    static readonly Color BadColor = new Color(1f, 0.42f, 0.42f, 1f);

    UiPopup _popup;
    TerminalConsole _con;
    TerminalBoot _boot;
    TPage _page = TPage.Home;
    int _pageNo, _listPageNo;   // 列表现在第几页 / 进详情前列表在第几页
    string _sel;                // 详情页的对象：配方 / 科技 id、升级到的等级、NPC id
    PowerNode _selDev;
    float _timer, _bootEndWait = -1f, _pendingT;
    Action _pending;            // 按了数字键：先在提示符上显示，过一小会儿再执行
    bool _opening, _booting, _closing;
    int _bootedFor;             // 已经开过机的终端机（GetInstanceID）；断电 / 换了一台就要重新开机
    string _tagText;

    readonly List<TerminalConsole.Spec> _specs = new List<TerminalConsole.Spec>();
    readonly List<TerminalConsole.Spec> _specPool = new List<TerminalConsole.Spec>();
    int _specN;
    readonly Dictionary<int, Action> _acts = new Dictionary<int, Action>();   // 这一页每个数字键做什么
    readonly Dictionary<int, string> _why = new Dictionary<int, string>();    // 不能选的菜单：为什么

    public bool IsOpen => _popup != null && _popup.IsOpen;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    // ================= 打开 / 关闭 =================

    // 打开面板（TerminalClickHandler 点击终端机时调用）。已经开着就什么都不做。
    public void Open()
    {
        TerminalController tc = TerminalController.Instance;
        if (tc == null || IsOpen) return;
        EnsureConsole();
        Vector2 cs = _popup.CanvasSize;
        Vector2 tl = new Vector2((cs.x - W) * 0.5f, (cs.y + H) * 0.5f);   // 屏幕正中
        _closing = false;
        _pending = null;
        _con.CancelCrt();
        _con.ClearScreen();          // 浮窗展开时是黑屏，展开完再开机 / 进菜单
        _con.SetSkipZone(false);
        _con.SetChrome(false);
        _popup.OpenCanvas(tl, tl, new Vector2(W, H), false);   // 没有起点：不画从终端机伸过来的线，直接上沿展开
        _opening = true;
        RefreshTag(tc);
    }

    // 立刻关掉（右上角 [x]、进建造模式、打开对话……）。
    public void Close()
    {
        _closing = false;
        _opening = false;
        _pending = null;
        _booting = false;
        _boot = null;
        if (_con != null) _con.CancelCrt();
        if (_popup != null) _popup.Close();
    }

    // 注销：显像管关机动画，播完关掉。
    void Logout()
    {
        if (_closing) return;
        _closing = true;
        _con.Feedback("> " + L("term_logout_msg"), TerminalConsole.Dim, 1f);
        _con.CrtOff(() => { _closing = false; if (_popup != null) _popup.Close(); });
    }

    // ================= 每帧 =================

    void Update()
    {
        TerminalController tc = TerminalController.Instance;
        if (tc != null && !tc.IsOnline()) _bootedFor = 0;   // 断电 / 停机：下次要重新开机
        if (_popup == null || !_popup.IsOpen) return;
        if (tc == null) { Close(); return; }                // 终端机被拆了
        if (BuildManager.Instance != null && BuildManager.Instance.IsBuildMode) { Close(); return; }   // 进建造模式：收起

        float dt = Time.unscaledDeltaTime;
        _con.Tick(dt);
        RefreshTag(tc);

        if (_opening)
        {
            if (!_popup.Animating) AfterOpen(tc);
            return;
        }
        if (_booting) { TickBoot(dt); return; }
        if (_closing) return;

        // 断电 → 离线画面；恢复供电 → 重新开机
        if (!tc.IsOnline() && _page != TPage.Offline) { ShowOffline(); return; }
        if (tc.IsOnline() && _page == TPage.Offline) { StartBoot(); return; }

        if (_pending != null)
        {
            _pendingT -= dt;
            if (_pendingT <= 0f) RunPending();
        }
        else HandleKeys();

        _timer += dt;
        if (_timer >= refreshInterval) { _timer = 0f; Render(false); }
    }

    // 浮窗展开完了：第一次（或断电重启后）播开机滚动，否则直接进主菜单。
    void AfterOpen(TerminalController tc)
    {
        _opening = false;
        if (!tc.IsOnline()) { ShowOffline(); return; }
        if (bootEveryTime || _bootedFor != tc.GetInstanceID()) { StartBoot(); return; }
        _con.SetChrome(true);
        _con.CrtOn();
        _page = TPage.Home;
        _pageNo = 0;
        Render(true);
        _con.Feedback("> " + L("term_welcome"), TerminalConsole.Dim, 3f);
    }

    void StartBoot()
    {
        _booting = true;
        _bootEndWait = -1f;
        _pending = null;
        _con.ClearScreen();
        _con.SetChrome(false);
        _con.SetSkipZone(true, L("term_boot_skip"));
        _con.CrtOn();
        _boot = new TerminalBoot(_con, BootLines(), BootVars());
    }

    void TickBoot(float dt)
    {
        if (AnyKeyDown()) { EndBoot(); return; }
        if (_con.CrtBusy || _boot == null) return;
        _boot.Tick(dt);
        _con.SetRawLines(_boot.Visible(_con.Capacity));
        if (!_boot.Finished) return;
        if (_bootEndWait < 0f) _bootEndWait = 0.5f;   // ACCESS GRANTED 停一下
        _bootEndWait -= dt;
        if (_bootEndWait <= 0f) EndBoot();
    }

    // 开机结束（播完或跳过）：清屏（亮线闪一下）→ 主菜单。
    void EndBoot()
    {
        if (!_booting) return;
        _booting = false;
        _boot = null;
        TerminalController tc = TerminalController.Instance;
        if (tc != null) _bootedFor = tc.GetInstanceID();
        _con.SetSkipZone(false);
        _con.SetChrome(true);
        _con.CrtOn();
        _page = TPage.Home;
        _pageNo = 0;
        Render(true);
    }

    void ShowOffline()
    {
        _booting = false;
        _boot = null;
        _pending = null;
        _con.SetSkipZone(false);
        _con.SetChrome(true);
        _page = TPage.Offline;
        Render(true);
    }

    // ================= 输入 =================

    void HandleKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || ChatManager.InputFocused) return;   // 在对话框里打字时不抢键
        for (int d = 0; d <= 9; d++) if (DigitDown(kb, d)) { Press(d); return; }
        if (kb.backspaceKey.wasPressedThisFrame) { BackKey(); return; }
        // Esc：对话窗口开着时先让它关对话
        if (kb.escapeKey.wasPressedThisFrame && !(NpcChatWindow.Instance != null && NpcChatWindow.Instance.IsOpen)) BackKey();
    }

    static bool AnyKeyDown()
    {
        Keyboard kb = Keyboard.current;
        return kb != null && !ChatManager.InputFocused && kb.anyKey.wasPressedThisFrame;
    }

    static bool DigitDown(Keyboard kb, int d)
    {
        switch (d)
        {
            case 0: return kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame;
            case 1: return kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame;
            case 2: return kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame;
            case 3: return kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame;
            case 4: return kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame;
            case 5: return kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame;
            case 6: return kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame;
            case 7: return kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame;
            case 8: return kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame;
            default: return kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame;
        }
    }

    // 选菜单（数字键 / 点那一行）：数字先出现在提示符后面，过 0.09 秒再执行；不能选的显示原因。
    void Press(int key)
    {
        if (_booting || _opening || _closing || _pending != null) return;
        if (!_acts.TryGetValue(key, out Action act)) return;
        _con.Echo(key.ToString());
        if (_why.TryGetValue(key, out string why))
        {
            _con.Feedback("ERROR: " + why, BadColor, 3.5f);
            _pending = () => { };   // 让输入的数字在提示符上停一下再清掉
            _pendingT = 0.3f;
            return;
        }
        _pending = act;
        _pendingT = 0.09f;
    }

    void BackKey()
    {
        if (_booting || _opening || _closing || _pending != null) return;
        _con.Echo(_page == TPage.Home ? "logout" : "cd ..");
        _pending = Back;
        _pendingT = 0.12f;
    }

    void RunPending()
    {
        Action a = _pending;
        _pending = null;
        _con.Echo("");
        TPage before = _page;
        a?.Invoke();
        if (_page == before && !_closing && IsOpen) Render(false);
    }

    // ================= 页面切换 =================

    void Go(TPage p) { _page = p; Render(true); }
    void Enter(TPage p) { _pageNo = 0; Go(p); }
    void Detail(TPage p, string id) { _listPageNo = _pageNo; _sel = id; Go(p); }
    void DetailDev(PowerNode d) { _listPageNo = _pageNo; _selDev = d; Go(TPage.Device); }

    void Back()
    {
        switch (_page)
        {
            case TPage.Home: Logout(); return;
            case TPage.Offline: Close(); return;
            case TPage.TechDetail: _pageNo = _listPageNo; Go(TPage.TechList); return;
            case TPage.UserDetail: _pageNo = _listPageNo; Go(TPage.Users); return;
            case TPage.CraftDetail: _pageNo = _listPageNo; Go(TPage.CraftList); return;
            case TPage.UpgradeDetail: _pageNo = _listPageNo; Go(TPage.UpgradeList); return;
            case TPage.Device: _pageNo = _listPageNo; Go(TPage.Power); return;
            default: Go(TPage.Home); return;
        }
    }

    void Render(bool animate)
    {
        if (_con == null || _booting) return;
        Begin();
        switch (_page)
        {
            case TPage.Offline: PageOffline(); break;
            case TPage.Home: PageHome(); break;
            case TPage.TechList: PageTechList(); break;
            case TPage.TechDetail: PageTechDetail(); break;
            case TPage.Users: PageUsers(); break;
            case TPage.UserDetail: PageUserDetail(); break;
            case TPage.CraftList: PageCraftList(); break;
            case TPage.CraftDetail: PageCraftDetail(); break;
            case TPage.UpgradeList: PageUpgradeList(); break;
            case TPage.UpgradeDetail: PageUpgradeDetail(); break;
            case TPage.Power: PagePower(); break;
            case TPage.Device: PageDevice(); break;
            case TPage.Jobs: PageJobs(); break;
        }
        _con.SetPage(_specs, animate);
        _con.SetPrompt(PathText());
        _con.SetStatus("ABA-DOS 6.22  //  SURVIVAL SUPPORT AI", StatusRight());
    }

    // ================= 各页 =================

    void PageHome()
    {
        bool jp = LanguageGuard.IsJp;
        TerminalWorkshop ws = TerminalWorkshop.Current;
        PowerGrid.Summary p = PowerGrid.Total();
        Title(L("term_home_title"));
        Txt(Lbl("term_lbl_level") + ProgressionRules.TierLabel(ProgressionRules.Tier) +
            Col(52) + Faint(ws != null ? LF("term_slots", ws.running.Count, ws.Slots, ws.waiting.Count) : "-"));
        if (p.capacity <= 0f && p.produced <= 0f) Txt(Lbl("term_lbl_produce") + Faint(L("term_no_power")));
        else
        {
            Txt(Lbl("term_lbl_produce") + "+" + p.produced.ToString("0.#") + L("term_per_turn"));
            float r = p.capacity > 0f ? p.stored / p.capacity : 0f;
            Txt(Lbl("term_lbl_store") + p.stored.ToString("0.#") + " / " + p.capacity.ToString("0") + Col(52) + Bar(r, 16));
            string use = p.consumed.ToString("0.#") + " / " + p.demand.ToString("0.#") + L("term_per_turn");
            if (p.shed > 0) use += "  " + C(BadHex, LF("term_shed", p.shed));
            Txt(Lbl("term_lbl_use") + use);
        }
        ClimateSystem cs = ClimateSystem.Instance;
        if (cs != null) Txt(Lbl("term_lbl_weather") + cs.HudText(jp));
        Txt(Lbl("term_lbl_water") + LF("term_water_line", WaterWorks.StoredWater, CampAdvisor.DaysPhrase(WaterWorks.DaysLeft, jp)));
        Rule();
        Item(1, Menu("term_menu_tech", "TECH", TechSummary()), () => Enter(TPage.TechList));
        Item(2, Menu("term_menu_users", "USERS", UsersSummary()), () => Enter(TPage.Users));
        Item(3, Menu("term_menu_craft", "CRAFT", CraftSummary()), () => Enter(TPage.CraftList));
        Item(4, Menu("term_menu_upgrade", "UPGRADE", UpgradeSummary()), () => Enter(TPage.UpgradeList));
        Item(5, Menu("term_menu_power", "POWER", LF("term_sum_power", Devices().Count)), () => Enter(TPage.Power));
        Item(6, Menu("term_menu_jobs", "JOBS", ws != null ? LF("term_sum_jobs", ws.running.Count, ws.waiting.Count) : "-"), () => Enter(TPage.Jobs));
        Gap();
        Item(0, Menu("term_menu_logout", "LOGOUT", ""), Logout);
    }

    // —— 科技 ——
    void PageTechList()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        Title(L("term_menu_tech") + Col(62) + Faint(TechSummary()));
        int count = db != null ? db.techs.Count : 0;
        int start = ClampPage(count, PerPage), end = Mathf.Min(count, start + PerPage);
        if (count == 0) Txt("  " + L("term_empty"), TerminalConsole.Dim);
        for (int i = start; i < end; i++)
        {
            TechData t = db.techs[i];
            string id = t.id;
            Item(i - start + 1, (ProgressionRules.HasTech(id) ? "● " : "  ") + db.TechName(id) + Col(46) + TechState(t), () => Detail(TPage.TechDetail, id));
        }
        Pager(count, PerPage);
    }

    void PageTechDetail()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        TechData t = db != null ? db.GetTech(_sel) : null;
        if (t == null) { NotFound(); return; }
        Title(db.TechName(t.id) + Col(62) + TechState(t));
        string desc = db.TechDesc(t.id);
        if (!string.IsNullOrEmpty(desc)) Para("  " + desc, TerminalConsole.Dim);
        Txt(Lbl("term_lbl_cost") + ProgressionRules.DescribeCost(t.cost));
        Txt(Lbl("term_lbl_time") + LF("term_seconds", t.researchTime.ToString("0")));
        Txt(Lbl("term_lbl_require") + ProgressionRules.TierLabel(t.requiredTier) + PrereqText(t.prerequisites));
        TerminalJob j = FindJob(TerminalJobType.Research, t.id, out bool running);
        if (j != null && running) Txt(Lbl("term_lbl_state") + Bar(j.Progress01, 20) + "  " + LF("term_remaining", j.remaining.ToString("0")));
        Rule();
        if (!ProgressionRules.HasTech(t.id))
        {
            bool can = ProgressionRules.CanResearch(t, out string reason);
            if (j != null) { can = false; reason = L("job_already_queued"); }
            string id = t.id;
            Item(1, L("btn_research") + (can ? "" : Col(40) + Faint(reason)), () => DoResearch(id), can ? null : reason);
        }
        Gap();
        Item(0, L("term_back"), Back);
    }

    string TechState(TechData t)
    {
        if (ProgressionRules.HasTech(t.id)) return C(OkHex, L("term_done"));
        TerminalJob j = FindJob(TerminalJobType.Research, t.id, out bool running);
        if (j != null) return running ? C(BrightHex, L("tech_researching")) + " " + Bar(j.Progress01, 10) : Faint(L("term_queued"));
        if (!ProgressionRules.IsTechUnlocked(t, out _)) return Faint("[" + L("lock_short_tech") + "/" + ProgressionRules.TierLabel(t.requiredTier) + "]");
        return L("term_ready");
    }

    // —— 登录成员（NPC）——
    void PageUsers()
    {
        List<NpcData> list = Npcs();
        Title(L("term_menu_users") + Col(62) + Faint(UsersSummary()));
        int count = list.Count;
        if (count == 0) Txt("  " + L("term_user_none"), TerminalConsole.Dim);
        else Txt(Faint("     UID" + Col(17) + L("term_col_name") + Col(38) + L("term_col_role") + Col(60) + L("term_col_state")));
        int start = ClampPage(count, PerPage), end = Mathf.Min(count, start + PerPage);
        for (int i = start; i < end; i++)
        {
            NpcData n = list[i];
            if (n == null) continue;
            string id = n.id;
            Item(i - start + 1, Uid(i) + Col(17) + n.npcName + Col(38) + RoleText(n) + Col(60) + StateText(n, true), () => Detail(TPage.UserDetail, id));
        }
        Pager(count, PerPage);
    }

    void PageUserDetail()
    {
        List<NpcData> list = Npcs();
        NpcData n = FindNpc(_sel);
        if (n == null) { NotFound(); return; }
        bool jp = LanguageGuard.IsJp;
        Title(Uid(list.IndexOf(n)) + "  " + n.npcName + Col(62) + StateText(n, false));
        Txt(Lbl("term_user_job") + (string.IsNullOrEmpty(n.profession) ? "—" : n.profession));
        Txt(Lbl("term_user_role") + RoleText(n));
        string doing;
        if (n.IsDead) doing = C(BadHex, L("term_user_dead"));
        else if (n.isBroken) doing = C(PurpleHex, L("term_user_broken"));
        else doing = string.IsNullOrEmpty(n.currentAction) ? L("term_user_idle") : n.currentAction;
        Txt(Lbl("term_user_doing") + doing);
        Txt(Lbl("term_user_vitals") + LF("term_user_vitals_fmt", R(n.health), R(n.satiety), R(n.water), R(n.energy)));
        bool uneasy = NpcSecurity.Insecure(n);
        Txt(Lbl("term_user_security") + (uneasy ? C(WarnHex, R(n.security) + L("term_user_uneasy")) : R(n.security).ToString()));
        Txt(Lbl("term_user_trust") + R(n.playerTrust) + Col(36) + Bar(n.playerTrust / 100f, 10));
        Txt(Lbl("term_user_state") + HealthText(n, jp));
        if (n.traits != null && n.traits.Count > 0) Txt(Lbl("term_user_traits") + string.Join("・", n.traits));
        Rule();
        string id = n.id;
        bool canTalk = !n.IsDead && NpcChatWindow.Instance != null;
        Item(1, L("term_user_call"), () => CallNpc(id), canTalk ? null : L("term_user_offline"));
        bool canLocate = NpcView.GetMover(id) != null && CameraController.Instance != null;
        Item(2, L("term_user_locate"), () => LocateNpc(id), canLocate ? null : L("term_user_offline"));
        Gap();
        Item(0, L("term_back"), Back);
    }

    // —— 合成 ——
    void PageCraftList()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        Title(L("term_menu_craft") + Col(62) + Faint(CraftSummary()));
        int count = db != null ? db.recipes.Count : 0;
        int start = ClampPage(count, PerPage), end = Mathf.Min(count, start + PerPage);
        if (count == 0) Txt("  " + L("term_empty"), TerminalConsole.Dim);
        for (int i = start; i < end; i++)
        {
            RecipeData r = db.recipes[i];
            string id = r.id;
            Item(i - start + 1, ProgressionRules.ItemName(r.outputItemId) + " ×" + r.outputCount + Col(46) + CraftState(r), () => Detail(TPage.CraftDetail, id));
        }
        Pager(count, PerPage);
    }

    void PageCraftDetail()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        RecipeData r = db != null ? db.GetRecipe(_sel) : null;
        if (r == null) { NotFound(); return; }
        Title(ProgressionRules.ItemName(r.outputItemId) + " ×" + r.outputCount + Col(62) + CraftState(r));
        string desc = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetDesc(r.outputItemId) : "";
        if (!string.IsNullOrEmpty(desc)) Para("  " + desc, TerminalConsole.Dim);
        Txt(Lbl("term_lbl_cost") + ProgressionRules.DescribeCost(r.inputs));
        Txt(Lbl("term_lbl_time") + LF("term_seconds", r.craftTime.ToString("0")));
        Txt(Lbl("term_lbl_require") + ProgressionRules.TierLabel(r.requiredTier) + (string.IsNullOrEmpty(r.requiredTechId) ? "" : "　" + TechCheck(r.requiredTechId)));
        TerminalJob j = FindJob(TerminalJobType.Craft, r.id, out bool running);
        if (j != null && running) Txt(Lbl("term_lbl_state") + Bar(j.Progress01, 20) + "  " + LF("term_remaining", j.remaining.ToString("0")));
        Rule();
        bool can = ProgressionRules.CanCraft(r, out string reason);
        string id = r.id;
        Item(1, L("btn_craft") + (can ? "" : Col(40) + Faint(reason)), () => DoCraft(id), can ? null : reason);
        Gap();
        Item(0, L("term_back"), Back);
    }

    string CraftState(RecipeData r)
    {
        if (!ProgressionRules.IsRecipeUnlocked(r, out _)) return Faint("[" + LockShort(r.requiredTier, r.requiredTechId) + "]");
        int queued = QueuedCount(TerminalJobType.Craft, r.id);
        string q = queued > 0 ? "  " + Faint(L("term_queued") + " ×" + queued) : "";
        return (ProgressionRules.CanCraft(r, out string why) ? L("term_can_craft") : Faint(why)) + q;
    }

    // —— 升级 ——
    void PageUpgradeList()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        Title(L("term_menu_upgrade") + Col(62) + Faint(L("term_current") + " " + ProgressionRules.TierLabel(ProgressionRules.Tier)));
        int count = db != null ? db.upgrades.Count : 0;
        int start = ClampPage(count, PerPage), end = Mathf.Min(count, start + PerPage);
        if (count == 0) Txt("  " + L("term_empty"), TerminalConsole.Dim);
        for (int i = start; i < end; i++)
        {
            TerminalUpgradeStep s = db.upgrades[i];
            string lv = s.toLevel.ToString();
            Item(i - start + 1, LF("upgrade_to", ProgressionRules.TierLabel(s.toLevel)) + Col(46) + UpgradeState(s), () => Detail(TPage.UpgradeDetail, lv));
        }
        Pager(count, PerPage);
    }

    void PageUpgradeDetail()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        int.TryParse(_sel, out int lv);
        TerminalUpgradeStep s = db != null ? db.GetUpgrade(lv) : null;
        if (s == null) { NotFound(); return; }
        TerminalWorkshop ws = TerminalWorkshop.Current;
        Title(LF("upgrade_to", ProgressionRules.TierLabel(s.toLevel)) + Col(62) + UpgradeState(s));
        string note = db.UpgradeNote(s);
        if (!string.IsNullOrEmpty(note)) Para("  " + note, TerminalConsole.Dim);
        Txt(Lbl("term_lbl_slots") + LF("term_slots_info", ws != null ? ws.Slots : 1));
        Txt(Lbl("term_lbl_cost") + ProgressionRules.DescribeCost(s.cost));
        Txt(Lbl("term_lbl_time") + LF("term_seconds", s.upgradeTime.ToString("0")));
        Txt(Lbl("term_lbl_require") + ProgressionRules.TierLabel(s.toLevel - 1) + (string.IsNullOrEmpty(s.requiredTechId) ? "" : "　" + TechCheck(s.requiredTechId)));
        TerminalJob j = FindJob(TerminalJobType.Upgrade, s.toLevel.ToString(), out bool running);
        if (j != null && running) Txt(Lbl("term_lbl_state") + Bar(j.Progress01, 20) + "  " + LF("term_remaining", j.remaining.ToString("0")));
        Rule();
        if (ProgressionRules.Tier < s.toLevel)
        {
            bool can = ProgressionRules.CanUpgrade(s, out string reason);
            if (ws != null && ws.HasJobOfType(TerminalJobType.Upgrade)) { can = false; reason = L("job_already_queued"); }
            Item(1, L("btn_upgrade") + (can ? "" : Col(40) + Faint(reason)), () => DoUpgrade(lv), can ? null : reason);
        }
        else if (ProgressionRules.Tier >= ProgressionRules.MaxTier) Txt("  " + L("term_max_level"), TerminalConsole.Dim);
        Gap();
        Item(0, L("term_back"), Back);
    }

    string UpgradeState(TerminalUpgradeStep s)
    {
        if (ProgressionRules.Tier >= s.toLevel) return C(OkHex, L("upgrade_done"));
        TerminalJob j = FindJob(TerminalJobType.Upgrade, s.toLevel.ToString(), out bool running);
        if (j != null) return running ? Bar(j.Progress01, 10) : Faint(L("term_queued"));
        if (ProgressionRules.Tier < s.toLevel - 1) return Faint("[" + ProgressionRules.TierLabel(s.toLevel - 1) + "]");
        return ProgressionRules.CanUpgrade(s, out string why) ? L("term_can_upgrade") : Faint(why);
    }

    // —— 能源 ——
    void PagePower()
    {
        bool jp = LanguageGuard.IsJp;
        Title(L("term_menu_power"));
        foreach (string line in PowerLines(jp)) Para("  " + line);
        List<PowerNode> devs = Devices();
        if (devs.Count == 0) Para("  " + PowerHint(jp), TerminalConsole.Dim);
        Rule();
        Txt("  " + DevHeader(devs.Count, jp), TerminalConsole.Dim);
        int start = ClampPage(devs.Count, DevPerPage), end = Mathf.Min(devs.Count, start + DevPerPage);
        for (int i = start; i < end; i++)
        {
            PowerNode d = devs[i];
            Item(i - start + 1, d.DisplayName + Col(44) + LF("term_dev_short", d.level) + Col(58) + DevStatus(d, jp), () => DetailDev(d));
        }
        Pager(devs.Count, DevPerPage);
    }

    void PageDevice()
    {
        PowerNode d = _selDev;
        if (d == null) { NotFound(); return; }
        bool jp = LanguageGuard.IsJp;
        Title(d.DisplayName);
        Txt(Lbl("term_lbl_gear") + LF("term_dev_level", d.level, PowerGrid.MaxLevel));
        Txt(Lbl("term_lbl_state") + DevStatus(d, jp));
        Para("  " + PowerHint(jp), TerminalConsole.Dim);
        Rule();
        Item(1, L("term_dev_up"), () => SetDevLevel(d, d.level + 1), d.level >= PowerGrid.MaxLevel ? L("term_dev_max") : null);
        Item(2, L("term_dev_down"), () => SetDevLevel(d, d.level - 1), d.level <= 0 ? L("term_dev_min") : null);
        Item(3, L("term_dev_off"), () => SetDevLevel(d, 0), d.level <= 0 ? L("term_dev_min") : null);
        Gap();
        Item(0, L("term_back"), Back);
    }

    // —— 任务队列 ——
    void PageJobs()
    {
        TerminalWorkshop ws = TerminalWorkshop.Current;
        Title(L("term_menu_jobs") + Col(62) + Faint(ws != null ? LF("term_slots", ws.running.Count, ws.Slots, ws.waiting.Count) : ""));
        if (ws == null) Txt("  TerminalWorkshop ?", BadColor);
        else
        {
            if (ws.running.Count == 0 && ws.waiting.Count == 0) Txt("  " + L("term_idle"), TerminalConsole.Dim);
            foreach (TerminalJob j in ws.running)
                Txt("  " + C(OkHex, "RUN ") + " " + TerminalWorkshop.JobLabel(j) + Faint("  (" + JobKind(j) + ")") +
                    Col(52) + Bar(j.Progress01, 14) + "  " + j.remaining.ToString("0") + "s");
            foreach (TerminalJob j in ws.waiting)
                Txt("  " + Faint("WAIT") + " " + TerminalWorkshop.JobLabel(j) + Faint("  (" + JobKind(j) + ")"));
            if (TerminalController.Instance != null && !TerminalController.Instance.IsOnline()) Txt("  " + C(WarnHex, L("term_jobs_paused")));
        }
        Gap();
        Item(0, L("term_back"), Back);
    }

    void PageOffline()
    {
        Txt("ABA-BIOS Ver 4.03");
        Txt(L("term_power") + " ........ " + C(BadHex, "[OFF]"));
        Gap();
        Para("  " + L("term_offline_msg"), TerminalConsole.Dim);
        Gap();
        Item(0, L("btn_close"), Close);
    }

    void NotFound()
    {
        Txt("  " + L("term_not_found"), BadColor);
        Gap();
        Item(0, L("term_back"), Back);
    }

    // ================= 操作 =================

    void DoResearch(string id)
    {
        TerminalWorkshop ws = TerminalWorkshop.Current;
        TerminalDatabase db = TerminalDatabase.Instance;
        if (ws == null || db == null) return;
        bool ok = ws.TryEnqueueResearch(db.GetTech(id), out string msg);
        Result(ok, msg);
    }

    void DoCraft(string id)
    {
        TerminalWorkshop ws = TerminalWorkshop.Current;
        TerminalDatabase db = TerminalDatabase.Instance;
        if (ws == null || db == null) return;
        bool ok = ws.TryEnqueueCraft(db.GetRecipe(id), out string msg);
        Result(ok, msg);
    }

    void DoUpgrade(int lv)
    {
        TerminalWorkshop ws = TerminalWorkshop.Current;
        TerminalDatabase db = TerminalDatabase.Instance;
        if (ws == null || db == null) return;
        bool ok = ws.TryEnqueueUpgrade(db.GetUpgrade(lv), out string msg);
        Result(ok, msg);
    }

    void SetDevLevel(PowerNode d, int lv)
    {
        if (d == null) return;
        PowerGrid.SetLevel(d, lv);
        Result(true, LF("term_dev_set", d.DisplayName, d.level));
    }

    // 建立通讯：关掉终端机，打开和他的对话窗口。
    void CallNpc(string id)
    {
        NpcChatWindow w = NpcChatWindow.Instance;
        if (w == null) return;
        Close();
        w.Open(id);
    }

    // 定位：关掉终端机（不然挡在画面中间），镜头移到他那里。
    void LocateNpc(string id)
    {
        NpcMover m = NpcView.GetMover(id);
        if (m == null || CameraController.Instance == null) return;
        Close();
        CameraController.Instance.LookAt(m.transform.position);
    }

    void Result(bool ok, string msg)
    {
        _con.Feedback((ok ? "> " : "ERROR: ") + msg, ok ? OkColor : BadColor, 4f);
        Render(false);
    }

    // ================= 写一页的小工具 =================

    void Begin()
    {
        _specs.Clear();
        _specN = 0;
        _acts.Clear();
        _why.Clear();
    }

    TerminalConsole.Spec NewSpec()
    {
        if (_specN >= _specPool.Count) _specPool.Add(new TerminalConsole.Spec());
        TerminalConsole.Spec s = _specPool[_specN++];
        s.text = ""; s.key = -1; s.enabled = true; s.wrap = false; s.rule = false; s.color = default(Color);
        _specs.Add(s);
        return s;
    }

    void Txt(string t, Color c = default(Color)) { TerminalConsole.Spec s = NewSpec(); s.text = t; s.color = c; }
    void Para(string t, Color c = default(Color)) { TerminalConsole.Spec s = NewSpec(); s.text = t; s.color = c; s.wrap = true; }
    void Rule() { NewSpec().rule = true; }
    void Gap() { NewSpec(); }
    void Title(string t) { Txt("<b>" + t + "</b>", TerminalConsole.Bright); Rule(); }

    // 菜单行：「 [1] 科技 ……」；why != null = 不能选（变暗，选了显示原因）。
    void Item(int key, string label, Action act, string why = null)
    {
        TerminalConsole.Spec s = NewSpec();
        s.key = key;
        s.text = " [" + key + "] " + label;
        s.enabled = why == null;
        _acts[key] = act;
        if (why != null) _why[key] = why;
    }

    int ClampPage(int count, int per)
    {
        int pages = Mathf.Max(1, (count + per - 1) / per);
        _pageNo = Mathf.Clamp(_pageNo, 0, pages - 1);
        return _pageNo * per;
    }

    // 列表最后：[8] 上一页 / [9] 下一页（需要时）+ [0] 返回。
    void Pager(int count, int per)
    {
        int pages = Mathf.Max(1, (count + per - 1) / per);
        Gap();
        if (pages > 1)
        {
            if (_pageNo > 0) Item(8, L("term_prev"), () => { _pageNo--; Render(true); });
            if (_pageNo < pages - 1) Item(9, L("term_next"), () => { _pageNo++; Render(true); });
            Txt("      " + Faint(LF("term_page", _pageNo + 1, pages)));
        }
        Item(0, L("term_back"), Back);
    }

    static string Col(float pct) => "<pos=" + pct.ToString("0") + "%>";
    static string Faint(string s) => "<alpha=#80>" + s + "<alpha=#FF>";
    static string C(string hex, string s) => "<color=" + hex + ">" + s + "</color>";
    static string Lbl(string key) => "  " + Faint(L(key)) + Col(20);
    static string Menu(string key, string en, string sum) => L(key) + Col(24) + Faint(en) + Col(40) + Faint(sum);
    static int R(float v) => Mathf.RoundToInt(Mathf.Max(0f, v));

    // 文字进度条 [####......] 45%
    static string Bar(float r, int n)
    {
        r = Mathf.Clamp01(r);
        int k = Mathf.RoundToInt(r * n);
        return "[" + C(BrightHex, new string('#', k)) + Faint(new string('.', n - k)) + "] " + Mathf.RoundToInt(r * 100f) + "%";
    }

    // ================= 数据 =================

    static List<NpcData> Npcs()
    {
        WorldManager wm = WorldManager.Instance;
        return wm != null && wm.world != null && wm.world.npcs != null ? wm.world.npcs : new List<NpcData>();
    }

    static NpcData FindNpc(string id)
    {
        foreach (NpcData n in Npcs()) if (n != null && n.id == id) return n;
        return null;
    }

    static string Uid(int i) => "U-" + (Mathf.Max(0, i) + 1).ToString("000");
    static int Day() => WorldManager.Instance != null && WorldManager.Instance.world != null ? WorldManager.Instance.world.day : 1;

    static string RoleText(NpcData n)
    {
        string s = NpcSociety.IsLeader(n) ? NpcSociety.RoleName("leader") : "";
        if (NpcSociety.HasRole(n)) s += (s.Length > 0 ? "·" : "") + NpcSociety.RoleName(n.role);
        return s.Length > 0 ? s : Faint(L("term_user_idle"));
    }

    // ONLINE（活着）/ OFFLINE（死亡）/ CORRUPTED（人格崩解）；brief = 列表里顺便写生命和第一个状态词条。
    static string StateText(NpcData n, bool brief)
    {
        if (n.IsDead) return C(BadHex, "OFFLINE");
        if (n.isBroken) return C(PurpleHex, "CORRUPTED");
        string s = C(OkHex, "ONLINE") + "  HP " + R(n.health);
        if (brief)
        {
            List<HealthTag> tags = NpcHealth.Tags(n);
            if (tags.Count > 0) s += "  " + C(WarnHex, NpcHealth.TagName(n, tags[0], LanguageGuard.IsJp));
        }
        return s;
    }

    static string HealthText(NpcData n, bool jp)
    {
        if (n.IsDead) return C(BadHex, L("term_user_dead"));
        List<HealthTag> tags = NpcHealth.Tags(n);
        if (tags.Count == 0) return C(OkHex, L("term_user_healthy"));
        List<string> names = new List<string>();
        foreach (HealthTag t in tags)
            names.Add("<color=#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(NpcHealth.TagColor(t), Color.white, 0.35f)) + ">" + NpcHealth.TagName(n, t, jp) + "</color>");
        return string.Join("・", names);
    }

    static TerminalJob FindJob(TerminalJobType type, string id, out bool running)
    {
        running = false;
        TerminalWorkshop ws = TerminalWorkshop.Current;
        if (ws == null) return null;
        foreach (TerminalJob j in ws.running) if (j.type == type && j.targetId == id) { running = true; return j; }
        foreach (TerminalJob j in ws.waiting) if (j.type == type && j.targetId == id) return j;
        return null;
    }

    static int QueuedCount(TerminalJobType type, string id)
    {
        TerminalWorkshop ws = TerminalWorkshop.Current;
        if (ws == null) return 0;
        int c = 0;
        foreach (TerminalJob j in ws.running) if (j.type == type && j.targetId == id) c++;
        foreach (TerminalJob j in ws.waiting) if (j.type == type && j.targetId == id) c++;
        return c;
    }

    static string JobKind(TerminalJob j) =>
        L(j.type == TerminalJobType.Craft ? "term_tab_craft" : j.type == TerminalJobType.Research ? "term_tab_tech" : "term_tab_upgrade");

    static string TechSummary()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        if (db == null) return "-";
        int done = 0;
        foreach (TechData t in db.techs) if (ProgressionRules.HasTech(t.id)) done++;
        return LF("term_sum_tech", done, db.techs.Count);
    }

    static string UsersSummary()
    {
        int alive = 0, dead = 0;
        foreach (NpcData n in Npcs()) { if (n == null) continue; if (n.IsDead) dead++; else alive++; }
        return LF("term_sum_users", alive, dead);
    }

    static string CraftSummary()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        if (db == null) return "-";
        int can = 0;
        foreach (RecipeData r in db.recipes) if (ProgressionRules.CanCraft(r, out _)) can++;
        return LF("term_sum_craft", can, db.recipes.Count);
    }

    static string UpgradeSummary()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        int next = ProgressionRules.Tier + 1;
        if (db == null || next > ProgressionRules.MaxTier || db.GetUpgrade(next) == null) return L("term_max_level");
        return LF("term_sum_next", ProgressionRules.TierLabel(next));
    }

    // 用电设备（加温器 / 降温器 / 水源净化器），按种类、位置排好。
    static List<PowerNode> Devices()
    {
        List<PowerNode> devs = new List<PowerNode>();
        foreach (PowerNode p in PowerNode.All) if (p != null && p.IsDevice) devs.Add(p);
        devs.Sort((a, b) => a.kind != b.kind ? a.kind.CompareTo(b.kind) : a.Cell.x != b.Cell.x ? a.Cell.x.CompareTo(b.Cell.x) : a.Cell.y.CompareTo(b.Cell.y));
        return devs;
    }

    static string DevHeader(int count, bool jp)
    {
        if (count == 0)
            return jp ? "電気を使う設備はまだない（ヒーター／クーラーは「生活」・端末 Lv1、浄水器は「生産」から）"
                      : "还没有用电设备（加温器 / 降温器在「生活」里建，需要终端机 Lv1；水源净化器在「生产」里）";
        return jp ? "設備（段階 0＝オフ、最大 " + PowerGrid.MaxLevel + "）" : "设备（档位 0 = 关，最高 " + PowerGrid.MaxLevel + "）";
    }

    static string DevStatus(PowerNode d, bool jp)
    {
        if (!d.IsBuilt) return C("#9DB3A4", jp ? "建設中" : "施工中");
        if (d.level == 0) return C("#9DB3A4", jp ? "オフ" : "关闭");
        if (d.kind == PowerNodeKind.Purifier && d.powered)
            return C(OkHex, (jp ? "浄化中 " : "净化中 ") + d.workAmount + (jp ? "杯" : "份") + " -" + PowerGrid.PurifierPower(d.workAmount).ToString("0.#") + (jp ? "電" : "电"));
        if (d.kind == PowerNodeKind.Purifier && WaterWorks.RawTotal <= 0) return C("#9DB3A4", jp ? "待機（雨水なし）" : "待机（没有雨水）");
        if (d.powered)
            return C(OkHex, (d.kind == PowerNodeKind.Heater ? "+" : "-") + (d.level * PowerGrid.DegreesPerLevel) + "℃ -" + PowerGrid.DemandOf(d).ToString("0.#") + (jp ? "電" : "电"));
        if (d.network == null || (d.network.Generators == 0 && d.network.Batteries == 0)) return C(BadHex, jp ? "未接続" : "没接电");
        return C(BadHex, jp ? "電力不足" : "电力不足");
    }

    // 能源页的概况：天气 / 风力、水源、每个电网、没接上的设备。
    static List<string> PowerLines(bool jp)
    {
        List<string> lines = new List<string>();
        ClimateSystem cs = ClimateSystem.Instance;
        if (cs != null)
            lines.Add((jp ? "天気：" : "天气：") + cs.HudText(jp) +
                      (jp ? "　風力発電：1台 " : "　风力发电：每台 ") + cs.WindPowerPerTurn.ToString("0.#") + (jp ? " /ターン（AI端末にも内蔵）" : " /回合（终端机也自带一台）"));
        lines.Add(jp
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
            string shed = n.Shed > 0 ? C(BadHex, jp ? "　停止 " + n.Shed + " 台" : "　断电 " + n.Shed + " 台") : "";
            lines.Add((jp ? "電力網" : "电网") + n.id + "：" + (jp ? "発電 " : "发电 ") + n.Produced.ToString("0.#") +
                      (jp ? "　消費 " : "　用电 ") + n.Consumed.ToString("0.#") + "/" + n.Demand.ToString("0.#") +
                      (jp ? "　蓄電 " : "　储电 ") + n.Stored.ToString("0.#") + "/" + n.Capacity.ToString("0") +
                      (jp ? "（発電機 " : "（发电机 ") + n.Generators + (jp ? "・電池 " : "、电池 ") + n.Batteries + (jp ? "・設備 " : "、设备 ") + devices + "）" + shed);
        }
        if (unlinked.Count > 0) lines.Add(C("#FFB060", (jp ? "未接続：" : "没接电线：") + string.Join("、", unlinked)));
        return lines;
    }

    static string PowerHint(bool jp)
    {
        return jp
            ? "「電線」で発電機・蓄電池・設備をつなぐ。余った電気は蓄電池へ（効率 " + (PowerGrid.ChargeEfficiency * 100f).ToString("0") + "%）。足りない時は浄水器から先に止まる。ヒーター／クーラー 1/2/3段階：消費 " + PowerGrid.ClimateLevelPower[1] + "/" + PowerGrid.ClimateLevelPower[2] + "/" + PowerGrid.ClimateLevelPower[3] + "、±" + PowerGrid.DegreesPerLevel + "℃ずつ（半径" + PowerGrid.RadiusCells + "マス）。浄水器：1段階で最大 " + WaterWorks.PurifyPerLevel + " 杯、浄化した分だけ消費。"
            : "用「电线」把发电机、蓄电池和设备连起来。多余的电充进电池（效率 " + (PowerGrid.ChargeEfficiency * 100f).ToString("0") + "%），电不够时先停净化器。加温器/降温器 1/2/3 档耗电 " + PowerGrid.ClimateLevelPower[1] + "/" + PowerGrid.ClimateLevelPower[2] + "/" + PowerGrid.ClimateLevelPower[3] + "，每档 ±" + PowerGrid.DegreesPerLevel + "℃（半径 " + PowerGrid.RadiusCells + " 格）；净化器每档最多净化 " + WaterWorks.PurifyPerLevel + " 份，按净化量耗电。";
    }

    string LockShort(int tier, string techId)
    {
        if (ProgressionRules.Tier < tier) return ProgressionRules.TierLabel(tier);
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

    // 科技名 + 是否已满足（○ 绿 / × 红）
    string TechCheck(string techId)
    {
        bool ok = ProgressionRules.HasTech(techId);
        return (ok ? "<color=" + OkHex + ">○" : "<color=" + BadHex + ">×") + ProgressionRules.TechName(techId) + "</color>";
    }

    // 提示符里的路径：~ / ~/tech / ~/tech/雨水净化 ……
    string PathText()
    {
        TerminalDatabase db = TerminalDatabase.Instance;
        switch (_page)
        {
            case TPage.TechList: return "~/tech";
            case TPage.TechDetail: return "~/tech/" + (db != null ? db.TechName(_sel) : _sel);
            case TPage.Users: return "~/users";
            case TPage.UserDetail: { NpcData n = FindNpc(_sel); return "~/users/" + (n != null ? Uid(Npcs().IndexOf(n)) : "?"); }
            case TPage.CraftList: return "~/craft";
            case TPage.CraftDetail: { RecipeData r = db != null ? db.GetRecipe(_sel) : null; return "~/craft/" + (r != null ? ProgressionRules.ItemName(r.outputItemId) : "?"); }
            case TPage.UpgradeList: return "~/upgrade";
            case TPage.UpgradeDetail: return "~/upgrade/lv" + _sel;
            case TPage.Power: return "~/power";
            case TPage.Device: return "~/power/" + (_selDev != null ? _selDev.DisplayName : "?");
            case TPage.Jobs: return "~/jobs";
            default: return "~";
        }
    }

    string StatusRight()
    {
        TerminalController tc = TerminalController.Instance;
        bool online = tc != null && tc.IsOnline();
        return "DAY " + Day() + "  |  " + ProgressionRules.TierLabel(ProgressionRules.Tier) + "  |  " + (online ? "ONLINE" : "OFFLINE");
    }

    void RefreshTag(TerminalController tc)
    {
        string tag = !tc.isBuilt ? "terminal : building" : (tc.IsOnline() ? "terminal : online" : "terminal : offline");
        if (tag != _tagText) { _tagText = tag; _popup.SetTag(tag); }
    }

    // 开机脚本里 {users} 之类要换成的数字。
    Dictionary<string, string> BootVars()
    {
        int generators = 0;
        foreach (PowerNode n in PowerNode.All) if (n != null && n.IsBuilt && n.IsGenerator) generators++;
        int alive = 0;
        List<NpcData> npcs = Npcs();
        foreach (NpcData n in npcs) if (n != null && !n.IsDead) alive++;
        TerminalWorkshop ws = TerminalWorkshop.Current;
        return new Dictionary<string, string>
        {
            { "users", npcs.Count.ToString() },
            { "alive", alive.ToString() },
            { "turbines", generators.ToString() },
            { "slots", (ws != null ? ws.Slots : 1).ToString() },
            { "tier", ProgressionRules.TierLabel(ProgressionRules.Tier) },
            { "day", Day().ToString() },
        };
    }

    // 开机脚本：本地化表里的 term_boot_01、02……（读到没有的编号为止）。
    static List<string> BootLines()
    {
        List<string> list = new List<string>();
        for (int i = 1; i <= 99; i++)
        {
            string key = "term_boot_" + i.ToString("00");
            string s = L(key);
            if (s == key) break;
            list.Add(s);
        }
        return list;
    }

    // ================= 搭 UI =================

    void BuildUI()
    {
        // 浮窗（和资源详细、任务栏浮窗同一个样式），自己一个画布
        _popup = UiPopup.Create("TerminalPopup", 410);   // 比仓库面板高一点
        _popup.transform.SetParent(transform, false);
        _popup.Interactable = true;    // 挡住点击，不穿透到地图
        _popup.KeepTether = false;     // 镜头会动：打开完就把线收掉
        _tagText = "terminal : online";
        _popup.SetTag(_tagText);
        _popup.Body.sizeDelta = new Vector2(W, H);

        // 右上角关闭：和底部任务栏按钮一样的淡绿标签
        Button close = NewTagButton(_popup.Body, "Close", "[x]");
        Place((RectTransform)close.transform, W - 58f, 5f, 44f, UiStyle.TagH);
        close.onClick.AddListener(Close);
    }

    // 命令台画面（标签下面）：第一次打开时才建（这时字体肯定都找得到了）。
    void EnsureConsole()
    {
        if (_con != null) return;
        if (font == null) font = GameFonts.Find();
        _con = new TerminalConsole(_popup.Body, 14f, 30f, W - 28f, H - 44f);
        _con.OnPick += Press;
        _con.OnSkip += EndBoot;
    }

    Button NewTagButton(Transform parent, string name, string text)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = UiStyle.Green;
        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        GameObject tgo = new GameObject("Label", typeof(RectTransform));
        tgo.transform.SetParent(go.transform, false);
        TextMeshProUGUI label = tgo.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset mono = UiStyle.Mono;
        if (mono != null) label.font = mono;
        label.text = text;
        label.fontSize = UiStyle.TagFont + 1f;
        label.color = UiStyle.Ink;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        RectTransform lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        return b;
    }

    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static string L(string key) => ProgressionRules.L(key);
    static string LF(string key, params object[] a) => ProgressionRules.LF(key, a);
}
