using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using NavMeshPlus.Components;

// ============================================================================
// 蓝图规划（取代以前的建造模式：B 键 / 底部任务栏的 blueprint 打开；不暂停时间，世界照常运转）。
// 一台老电脑上的规划程序（BPLAN.EXE），风格和 AI 终端机一样：
//   · 中间是地图的示意图：地形、资源、建筑都换成几何图形——树 = 三角形、石头 = 方形、矿石 = 菱形、
//     水面 = 波纹、沼泽 = 点阵、没探明的地方 = 雪花噪点；建好的建筑实线框、施工中的虚线框、营地范围虚线。
//     中键 / 右键拖动平移，滚轮缩放。NPC 是一闪一闪的小方块。
//   · 左上角 [≡ 建筑]：打开建筑菜单（另一个浮窗，终端机风格）：按分类展开选建筑；下面是已经保存的蓝图
//     （自己画的签名 AI，NPC 交来的签他的名字）。
//   · 「四角框」：鼠标停在地图上，那块地显示一个只有四个角的框，微弱地呼吸闪光（绿 = 能放，红 = 放不下）；
//     左键点一下放一个、拖动连着放一排（按住 Shift 拖 = 沿着拖出的方框放一圈）——拖动时只有一个四角框框住整片范围；
//     放下的瞬间四角框亮一下再回到正常。右键点一下删掉那一项（没点到东西 = 取消选择）。
//     什么都没选时点蓝图里已有的建筑 = 选中同一种（吸管）。
//   · 下面的工具：撤回 / 删除（点或拖着框选删）/ 保存 / 测试。
//     测试：从营地中心出发把整张图的寻路扫一遍（扫描的波纹 + 到每个建筑、每个人的路线），
//       走不到的地方在命令行里报出来（坐标）；点那一行 = 地图移过去，红色十字加方框闪几下标出那个位置。
//   · 测试完问要不要保存；保存后问「您要将这份蓝图发给谁」，从上到下列出在场的 NPC，选一个 = 他来指挥建造。
//   · NPC 交来的蓝图（对话框里的计划文件）在这里打开：[1] 批准 / [2] 不允许（他会问为什么）/ [3] 修改（复制一份来改）。
// 最下面是命令行（TerminalConsole）：开机、读取地形、测试的字符进度条，提问、选项、报错都在这里；滚轮可以往上翻。
// 电线不进蓝图：电线工具（菜单 → 电力）点两台设备直接连上 / 断开，和以前一样。
// ============================================================================
public class BlueprintDesigner : MonoBehaviour
{
    public static BlueprintDesigner Instance { get; private set; }
    public static bool IsOpenNow => Instance != null && Instance.IsOpen && !Instance.Closing;

    [Header("窗口（1920×1080 下的界面单位）")]
    public Vector2 windowSize = new Vector2(1320f, 820f);

    [Header("示意图")]
    [Tooltip("每个小格画几个像素（越大越清楚，也越占显存）")]
    [Range(3, 10)] public int pixelsPerCell = 6;
    [Tooltip("打开时的缩放：每个小格占几个界面单位")]
    public float defaultZoom = 12f;
    public float maxZoom = 30f;

    [Header("测试动画（秒）")]
    [Tooltip("寻路扫描大约几秒扫完")]
    public float scanSeconds = 1.2f;
    [Tooltip("路线大约几秒画完")]
    public float pathSeconds = 0.9f;

    public bool IsOpen => _popup != null && _popup.IsOpen;
    public bool Closing => _closeAnim >= 0f;

    enum Mode { Edit, Review, View }
    enum Ask { None, Save, Send, Review }
    enum Tool { Place, Delete, Wire }
    enum Flush { Type, Keep, Instant }   // 新行打字 / 只刷新内容（打字继续）/ 立刻全部显示

    const float Pad = 14f, MapTop = 62f, ToolH = 26f, ConH = 200f, MinMapH = 240f, CloseDur = 0.26f;
    static readonly Color Green = UiStyle.Green;
    static readonly Color OkC = new Color(0.455f, 0.86f, 0.55f, 1f);
    static readonly Color BadC = new Color(1f, 0.42f, 0.42f, 1f);
    static readonly Color WarnC = new Color(1f, 0.89f, 0.42f, 1f);
    static readonly Color DimC = new Color(0.72f, 1f, 0.78f, 0.55f);
    static readonly Color PlanC = new Color(0.45f, 0.95f, 1f, 1f);     // 蓝图里的建筑（青色）
    static readonly Color AmberC = new Color(1f, 0.75f, 0.3f, 1f);

    // —— 界面 ——
    UiPopup _popup;
    TerminalConsole _con;
    RectTransform _viewRt, _ov, _itemsRoot, _ghostRoot, _npcRoot, _fxRoot, _cover, _scanLine, _conRect, _buildBtnRt;
    RawImage _mapImg, _testImg;
    Texture2D _mapTex, _testTex;
    Color32[] _basePx, _mapPx, _testPx;
    int _W, _H;
    float _cx, _cy, _zoom = 12f, _viewW, _viewH, _conH = ConH;
    Corners _hover, _drag, _wireSel, _flashC;
    TextMeshProUGUI _selText, _infoText, _matText, _buildLabel, _dragTag;
    readonly List<ToolBtn> _tools = new List<ToolBtn>();
    ToolBtn _btnUndo, _btnDelete, _btnSave, _btnTest, _btnApprove, _btnReject, _btnModify, _btnCopy;
    readonly List<ItemView> _itemViews = new List<ItemView>();
    readonly List<ItemView> _ghostViews = new List<ItemView>();
    readonly List<NpcDot> _npcDots = new List<NpcDot>();
    readonly List<Marker> _markers = new List<Marker>();
    readonly Dictionary<BlueprintItem, bool> _okCache = new Dictionary<BlueprintItem, bool>();
    bool _okDirty = true, _itemsDirty = true;

    // —— 状态 ——
    Mode _mode = Mode.Edit;
    Ask _ask = Ask.None;
    Tool _tool = Tool.Place;
    Blueprint _bp, _draft;
    BuildableData _sel;
    PowerNode _wireA;
    readonly List<UndoStep> _undo = new List<UndoStep>();
    bool _dragging, _dragShift, _panning, _rightDown, _middleDown, _introPending;
    Vector2Int _dragStart, _dragNow, _flashMin, _flashMax;
    Vector2 _panLast, _rightDownPos, _closeTl;
    Color _flashCol = Color.white;
    int _dragCount, _dragOkCount;
    float _flash, _flashT, _time, _boot = -1f, _closeAfter = -1f, _closeAnim = -1f, _sigTimer, _matTimer;
    string _openChatAfter;
    int _mapSig = -1, _fogSig = -1;
    readonly Dictionary<string, string> _shortNames = new Dictionary<string, string>();

    // —— 命令行 ——
    class LogLine { public string text; public Color color; public Action act; public int key = -1; public bool choice; }
    readonly List<LogLine> _log = new List<LogLine>();
    readonly List<TerminalConsole.Spec> _specs = new List<TerminalConsole.Spec>();
    int _printed;           // 已经显示过的行数（之后加的行打字）
    int _nextKey = 1000;    // 可点的报错行用的编号（数字键选项用 0~9）
    int _scrollBack;        // 滚轮往上翻了几行
    LogLine _progressLine;

    TestRun _test;
    BuildMenu _menu;

    // ================= 打开 / 关闭 =================

    public static BlueprintDesigner Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("BlueprintDesigner").AddComponent<BlueprintDesigner>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _zoom = defaultZoom;
        BlueprintSystem.Ensure();
        BuildUI();
        BlueprintSystem.OnChanged += OnBlueprintsChanged;
        LocalizationManager.OnLanguageChanged += OnLanguageChanged;
    }

    void OnDestroy()
    {
        BlueprintSystem.OnChanged -= OnBlueprintsChanged;
        LocalizationManager.OnLanguageChanged -= OnLanguageChanged;
        if (Instance == this) Instance = null;
        if (_mapTex != null) Destroy(_mapTex);
        if (_testTex != null) Destroy(_testTex);
    }

    // 打开（继续上次没保存的草稿；没有就新建一张）。
    public void Open()
    {
        Blueprint bp = _draft ?? BlueprintSystem.NewDraft();
        _draft = bp;
        Show(bp, Mode.Edit);
    }

    // 打开一份蓝图文件（对话框里的计划文件、建筑菜单里的已保存蓝图）。
    public static void OpenFile(string bpId)
    {
        BlueprintDesigner d = Ensure();
        Blueprint bp = BlueprintSystem.Find(bpId);
        if (bp == null)
        {
            d.Open();
            d.Print("ERROR: " + L("bp_file_missing"), BadC);
            d.FlushLog(Flush.Type);
            return;
        }
        if (bp.status == BlueprintStatus.Pending) d.Show(bp, Mode.Review);
        else if (bp.ByAI && (bp.status == BlueprintStatus.Saved || bp.status == BlueprintStatus.Draft)) d.Show(bp, Mode.Edit);
        else d.Show(bp, Mode.View);
    }

    // 关闭：显像管关机（整个窗口压成一条亮线，再缩没）。
    public void Close()
    {
        if (!IsOpen || _closeAnim >= 0f) return;
        CloseMenu();
        CancelDrag();
        _ask = Ask.None;
        _closeAfter = -1f;
        _hover.Show(false);
        _drag.Show(false);
        _flashC.Show(false);
        _wireSel.Show(false);
        HideGhosts();
        SetDragTag(null, default(Rect));
        _closeTl = _popup.Window.anchoredPosition;
        _closeAnim = 0f;
    }

    void TickClose(float dt)
    {
        _closeAnim += dt;
        float p = Mathf.Clamp01(_closeAnim / CloseDur);
        float sx = 1f, sy;
        if (p < 0.6f) sy = Mathf.Lerp(1f, 0.004f, Ease(p / 0.6f));
        else { sy = 0.004f; sx = Mathf.Lerp(1f, 0f, Ease((p - 0.6f) / 0.4f)); }
        RectTransform w = _popup.Window;
        w.localScale = new Vector3(Mathf.Max(0.0001f, sx), sy, 1f);
        // 轴心在左上角：挪一下让它往中间收
        w.anchoredPosition = new Vector2(_closeTl.x + windowSize.x * (1f - sx) * 0.5f, _closeTl.y - windowSize.y * (1f - sy) * 0.5f);
        if (p >= 1f) FinishClose();
    }

    void FinishClose()
    {
        _closeAnim = -1f;
        RectTransform w = _popup.Window;
        w.localScale = Vector3.one;
        w.anchoredPosition = _closeTl;
        _popup.Close();
        if (!string.IsNullOrEmpty(_openChatAfter))
        {
            string id = _openChatAfter;
            _openChatAfter = null;
            NpcChatWindow cw = NpcChatWindow.Ensure();
            if (!cw.IsOpen || cw.CurrentNpcId != id) cw.Open(id);   // 已经开着他的对话就不重播动画
        }
    }

    void Show(Blueprint bp, Mode mode)
    {
        if (_closeAnim >= 0f) FinishClose();
        bool wasOpen = IsOpen;
        _bp = bp;
        _mode = mode;
        _ask = Ask.None;
        _tool = Tool.Place;
        _wireA = null;
        _undo.Clear();
        _closeAfter = -1f;
        _openChatAfter = null;
        if (mode == Mode.Edit && bp.status == BlueprintStatus.Draft) _draft = bp;
        if (mode != Mode.Edit) _sel = null;
        CancelDrag();
        CloseMenu();
        ClearTest();
        _okDirty = true;
        _itemsDirty = true;
        _conH = ConH;
        if (!wasOpen)
        {
            if (TerminalPanelUI.Instance != null && TerminalPanelUI.Instance.IsOpen) TerminalPanelUI.Instance.Close();
            Vector2 cs = _popup.CanvasSize;
            if (cs.x < 800f || cs.y < 600f) cs = new Vector2(1920f, 1080f);
            Vector2 tl = new Vector2((cs.x - windowSize.x) * 0.5f, (cs.y + windowSize.y) * 0.5f);
            _popup.OpenCanvas(tl, tl, windowSize, false);
            Relabel();          // 字宽要在窗口激活之后量才准（按钮宽度、提示符光标位置）
            _con.Echo("");
            _log.Clear();
            _printed = 0;
            _scrollBack = 0;
            _mapSig = -1;
            _fogSig = -1;
            Layout();
            RenderMap(true);
            _con.CrtOn();
            BootLines();
            _introPending = true;   // 开机进度条走完再打说明
            _boot = 0f;
        }
        else
        {
            Layout();
            Print("");
            Print("> " + LF("bp_con_open", bp.FileName), TerminalConsole.Bright);
            if (_boot >= 0f) _introPending = true; else IntroLines(false);
        }
        CenterOnBlueprint();
        RefreshTools();
        RefreshTop();
        RefreshMaterials();
        if (_mode == Mode.Review && !_introPending) FitConsole(11); else FlushLog(Flush.Type);
    }

    // ================= 每帧 =================

    void Update()
    {
        if (!IsOpen) return;
        float dt = Time.unscaledDeltaTime;
        _time += dt;
        if (_closeAnim >= 0f) { TickClose(dt); return; }
        _con.Tick(dt);
        if (_menu != null && _menu.popup.IsOpen) _menu.con.Tick(dt);

        // 批准 / 发出去之后停一下让玩家看到结果再关
        if (_closeAfter >= 0f)
        {
            _closeAfter -= dt;
            if (_closeAfter < 0f) { Close(); return; }
        }
        if (GridManager.Instance == null) { Close(); return; }

        TickBoot(dt);
        _flash = Mathf.Max(0f, _flash - dt * 4f);
        _flashT = Mathf.Max(0f, _flashT - dt * 2.6f);

        // 地图有变化（资源被采、建筑建成、迷雾揭开）：隔一会儿重画
        _sigTimer -= dt;
        if (_sigTimer <= 0f) { _sigTimer = 1.5f; RenderMap(false); }

        if (!_popup.Animating)
        {
            HandleMouse();
            HandleKeys();
        }
        UpdateFlash();
        UpdateWireSel();
        if (_test != null && !_test.done) TickTest(dt);
        if (_itemsDirty) { _itemsDirty = false; RefreshItems(); }
        UpdateNpcDots();
        UpdateMarkers(dt);
        _matTimer -= dt;
        if (_matTimer <= 0f) { _matTimer = 0.5f; RefreshMaterials(); }
    }

    // ================= 鼠标 =================

    void HandleMouse()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 sp = mouse.position.ReadValue();
        bool overMenu = _menu != null && _menu.popup.IsOpen && RectTransformUtility.RectangleContainsScreenPoint(_menu.popup.Window, sp, null);
        bool overCon = !overMenu && RectTransformUtility.RectangleContainsScreenPoint(_conRect, sp, null);
        bool inView = !overMenu && !overCon && RectTransformUtility.RectangleContainsScreenPoint(_viewRt, sp, null);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_ov, sp, null, out Vector2 local);

        // 平移：中键 / 右键拖
        if (inView && mouse.middleButton.wasPressedThisFrame) { _middleDown = true; _panning = true; _panLast = sp; }
        if (inView && mouse.rightButton.wasPressedThisFrame) { _rightDown = true; _rightDownPos = sp; _panLast = sp; }
        if (_rightDown && !_panning && (sp - _rightDownPos).sqrMagnitude > 36f) _panning = true;
        if (_panning)
        {
            Vector2 d = (sp - _panLast) / Mathf.Max(0.01f, _popup.UnitToPixels);
            _panLast = sp;
            _cx -= d.x / _zoom;
            _cy -= d.y / _zoom;
            ClampView();
        }
        if (mouse.middleButton.wasReleasedThisFrame) { _middleDown = false; if (!_rightDown) _panning = false; }
        if (mouse.rightButton.wasReleasedThisFrame)
        {
            bool click = _rightDown && !_panning;
            _rightDown = false;
            if (!_middleDown) _panning = false;
            if (click && inView) RightClick(LocalToCell(local));
        }

        // 滚轮：地图上 = 缩放（以鼠标为中心）；命令行上 = 往上 / 往下翻
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            int notches = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(scroll) / 120f), 1, 3);
            if (overCon) ScrollLog(scroll > 0f ? notches * 2 : -notches * 2);
            else if (inView)
            {
                Vector2 before = LocalToCellF(local);
                float f = scroll > 0f ? Mathf.Pow(1.18f, notches) : Mathf.Pow(1f / 1.18f, notches);
                _zoom = Mathf.Clamp(_zoom * f, MinZoom, maxZoom);
                Vector2 after = LocalToCellF(local);
                _cx += before.x - after.x;
                _cy += before.y - after.y;
                ClampView();
            }
        }

        Vector2Int cell = LocalToCell(local);
        UpdateStatusBar(inView, cell);

        // 建筑菜单开着时点了别处：先把菜单收起来（这一下不算放置）
        bool consumed = false;
        if (mouse.leftButton.wasPressedThisFrame && _menu != null && _menu.popup.IsOpen && !overMenu
            && !RectTransformUtility.RectangleContainsScreenPoint(_buildBtnRt, sp, null))
        {
            CloseMenu();
            consumed = true;
        }

        // 左键：放置 / 删除 / 连电线
        if (inView && !consumed && mouse.leftButton.wasPressedThisFrame && !_panning && _boot < 0f)
        {
            if (_tool == Tool.Wire) WireClick(cell);
            else if (_mode == Mode.Edit && (_tool == Tool.Delete || _sel != null))
            {
                _dragging = true;
                _dragShift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
                _dragStart = _dragNow = cell;
            }
            else if (_mode == Mode.Edit)
            {
                // 什么都没选：点到蓝图里已有的建筑 = 选中同一种（吸管）
                BlueprintItem it = _bp != null ? _bp.ItemAt(cell) : null;
                BuildableData d = it != null ? BlueprintSystem.FindData(it.buildId) : null;
                if (d != null) Select(d);
                else _con.Feedback("> " + L("bp_con_pick_first"), WarnC, 2.5f);
            }
        }
        if (_dragging)
        {
            _dragNow = cell;
            if (Keyboard.current != null) _dragShift = Keyboard.current.shiftKey.isPressed;
            if (mouse.leftButton.wasReleasedThisFrame) FinishDrag();
        }
        UpdateHover(inView, cell);
    }

    void UpdateHover(bool inView, Vector2Int cell)
    {
        float breath = 0.42f + 0.3f * (0.5f + 0.5f * Mathf.Sin(_time * 3.2f));   // 微弱的呼吸
        float a = Mathf.Lerp(breath, 1f, _flash);
        HideGhosts();
        _drag.Show(false);
        SetDragTag(null, default(Rect));
        if (!inView || _boot >= 0f) { _hover.Show(false); return; }
        GridManager g = GridManager.Instance;

        if (_tool == Tool.Wire)
        {
            PowerNode node = PowerGrid.NodeAt(cell);
            if (node != null) _hover.Set(NodeRect(node), WithA(AmberC, a));
            else _hover.Set(AreaRect(cell, Vector2Int.one), WithA(DimC, a * 0.6f));
            return;
        }

        if (_mode != Mode.Edit || (_tool == Tool.Place && _sel == null))
        {
            BlueprintItem it = _bp != null ? _bp.ItemAt(cell) : null;
            if (it != null) _hover.Set(AreaRect(it.origin, it.size), WithA(PlanC, a));
            else _hover.Set(AreaRect(cell, Vector2Int.one), WithA(DimC, a * 0.7f));
            return;
        }

        if (_tool == Tool.Delete)
        {
            if (_dragging)
            {
                _hover.Show(false);
                _drag.Set(CellBox(_dragStart, _dragNow), WithA(BadC, Mathf.Max(a, 0.7f)));
                return;
            }
            BlueprintItem it = _bp.ItemAt(cell);
            _hover.Set(it != null ? AreaRect(it.origin, it.size) : AreaRect(cell, Vector2Int.one), WithA(BadC, it != null ? a : a * 0.5f));
            return;
        }

        // 放置
        Vector2Int size = _sel.FootprintSize(g.cellSize);
        if (_dragging)
        {
            List<Vector2Int> origins = DragOrigins(_dragStart, _dragNow, size, _dragShift);
            Rect box = default(Rect);
            bool any = false;
            int k = 0, ok = 0;
            foreach (Vector2Int o in origins)
            {
                bool can = CanPlace(o, size, out _);
                if (can) ok++;
                Rect r = AreaRect(o, size);
                Ghost(k++, r, can);
                box = any ? Union(box, r) : r;
                any = true;
            }
            _dragCount = origins.Count;
            _dragOkCount = ok;
            _hover.Show(false);
            if (any)
            {
                // 拖动：只有一个四角框框住整片范围
                _drag.Set(box, WithA(ok > 0 ? Green : BadC, Mathf.Max(a, 0.75f)));
                SetDragTag(ok == origins.Count ? "×" + ok : ok + "/" + origins.Count, box);
            }
            return;
        }
        Vector2Int origin = g.FootprintOrigin(cell, size);
        bool canPlace = CanPlace(origin, size, out _);
        Rect rr = AreaRect(origin, size);
        Ghost(0, rr, canPlace);
        Color col = canPlace ? Green : BadC;
        if (_flash > 0f) col = Color.Lerp(col, Color.white, _flash * 0.5f);
        _hover.Set(rr, WithA(col, a));
    }

    // 刚放下 / 删掉的那片范围：四角框亮一下再淡掉。
    void UpdateFlash()
    {
        if (_flashT <= 0f) { _flashC.Show(false); return; }
        Rect r = AreaRect(_flashMin, _flashMax - _flashMin + Vector2Int.one);
        _flashC.Set(r, WithA(Color.Lerp(_flashCol, Color.white, 0.55f * _flashT), _flashT));
    }

    void SetFlashArea(List<BlueprintItem> items, Color col)
    {
        if (items == null || items.Count == 0) return;
        Vector2Int min = items[0].origin, max = items[0].origin + items[0].size - Vector2Int.one;
        foreach (BlueprintItem it in items)
        {
            min = Vector2Int.Min(min, it.origin);
            max = Vector2Int.Max(max, it.origin + it.size - Vector2Int.one);
        }
        _flashMin = min;
        _flashMax = max;
        _flashCol = col;
        _flashT = 1f;
        _flash = 1f;
    }

    void UpdateWireSel()
    {
        if (_tool == Tool.Wire && _wireA != null) _wireSel.Set(NodeRect(_wireA), WithA(AmberC, 0.75f + 0.25f * Mathf.Sin(_time * 6f)));
        else _wireSel.Show(false);
    }

    void SetDragTag(string text, Rect box)
    {
        if (_dragTag == null) return;
        bool on = !string.IsNullOrEmpty(text);
        if (_dragTag.gameObject.activeSelf != on) _dragTag.gameObject.SetActive(on);
        if (!on) return;
        _dragTag.text = text;
        _dragTag.rectTransform.anchoredPosition = new Vector2(box.xMin, box.yMax + 6f);
    }

    void UpdateStatusBar(bool inView, Vector2Int cell)
    {
        string left = "BPLAN v1.0  //  " + ModeName() + "  //  " + (_bp != null ? _bp.FileName : "-");
        string right = "";
        GridManager g = GridManager.Instance;
        if (_dragging && _tool == Tool.Place && _sel != null)
            right = _dragOkCount + "/" + _dragCount + " × " + _sel.LocalizedName;
        else if (inView && g != null && g.InBounds(cell.x, cell.y))
        {
            right = "X " + cell.x + "  Y " + cell.y;
            if (_mode == Mode.Edit && _tool == Tool.Place && _sel != null)
            {
                Vector2Int size = _sel.FootprintSize(g.cellSize);
                right += CanPlace(g.FootprintOrigin(cell, size), size, out string why) ? "  OK" : "  × " + why;
            }
        }
        if (_scrollBack > 0) right = "↑" + _scrollBack + "   " + right;
        _con.SetStatus(left, right);
    }

    string ModeName()
    {
        switch (_mode)
        {
            case Mode.Review: return "REVIEW";
            case Mode.View: return "VIEW";
            default: return _tool == Tool.Delete ? "EDIT:DEL" : _tool == Tool.Wire ? "EDIT:WIRE" : "EDIT";
        }
    }

    // 右键点一下：删掉那一项；没点到东西 = 取消选择。
    void RightClick(Vector2Int cell)
    {
        if (_tool == Tool.Wire) { SetTool(Tool.Place); return; }
        if (_mode != Mode.Edit || _bp == null) return;
        BlueprintItem it = _bp.ItemAt(cell);
        if (it == null)
        {
            if (_sel != null || _tool != Tool.Place)
            {
                _sel = null;
                SetTool(Tool.Place);
                _con.Feedback("> " + L("bp_con_deselect"), DimC, 2f);
            }
            return;
        }
        UndoStep step = new UndoStep();
        step.removed.Add(it);
        _bp.items.Remove(it);
        PushUndo(step);
        SetFlashArea(step.removed, BadC);
        _con.Feedback("> " + LF("bp_con_removed", 1), DimC, 2f);
        Edited();
    }

    // 拖完（或点一下）：放置 / 删除。
    void FinishDrag()
    {
        _dragging = false;
        if (_mode != Mode.Edit || _bp == null) return;
        GridManager g = GridManager.Instance;
        UndoStep step = new UndoStep();
        if (_tool == Tool.Delete)
        {
            Vector2Int min = Vector2Int.Min(_dragStart, _dragNow), max = Vector2Int.Max(_dragStart, _dragNow);
            Vector2Int sz = max - min + Vector2Int.one;
            for (int i = _bp.items.Count - 1; i >= 0; i--)
                if (_bp.items[i].Overlaps(min, sz)) { step.removed.Add(_bp.items[i]); _bp.items.RemoveAt(i); }
            if (step.removed.Count > 0)
            {
                PushUndo(step);
                SetFlashArea(step.removed, BadC);
                _con.Feedback("> " + LF("bp_con_removed", step.removed.Count), DimC, 2f);
                Edited();
            }
            return;
        }
        if (_sel == null) return;
        if (!ProgressionRules.IsBuildableUnlocked(_sel, out string lockWhy))
        {
            _con.Feedback("ERROR: " + lockWhy, BadC, 3f);
            return;
        }
        Vector2Int size = _sel.FootprintSize(g.cellSize);
        Dictionary<string, int> fails = new Dictionary<string, int>();
        foreach (Vector2Int o in DragOrigins(_dragStart, _dragNow, size, _dragShift))
        {
            if (!CanPlace(o, size, out string why))
            {
                string k = why ?? "?";
                fails.TryGetValue(k, out int c);
                fails[k] = c + 1;
                continue;
            }
            BlueprintItem it = new BlueprintItem { buildId = _sel.id, origin = o, size = size };
            _bp.items.Add(it);
            step.added.Add(it);
        }
        if (step.added.Count > 0)
        {
            PushUndo(step);
            SetFlashArea(step.added, Green);   // 四角框亮一下 = 放下了
            Edited();
        }
        if (fails.Count > 0)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in fails) parts.Add(kv.Key + " ×" + kv.Value);
            _con.Feedback("> " + LF("bp_con_placed", step.added.Count) + "  " + L("bp_con_skip") + string.Join("、", parts), WarnC, 3.5f);
        }
        else if (step.added.Count > 1) _con.Feedback("> " + LF("bp_con_placed", step.added.Count), DimC, 2f);
        if (_sel != null && _sel.uniqueBuild && step.added.Count > 0) _sel = null;   // 唯一建筑放一个就够了
        RefreshTop();
    }

    void CancelDrag()
    {
        _dragging = false;
        _panning = false;
        _rightDown = false;
        _middleDown = false;
    }

    // 拖出来的一排（直线：沿着拖得更远的方向一个挨一个）/ 一圈（Shift：沿方框四条边）。
    List<Vector2Int> DragOrigins(Vector2Int start, Vector2Int end, Vector2Int size, bool ring)
    {
        GridManager g = GridManager.Instance;
        List<Vector2Int> list = new List<Vector2Int>();
        Vector2Int o0 = g.FootprintOrigin(start, size), o1 = g.FootprintOrigin(end, size);
        int dx = o1.x - o0.x, dy = o1.y - o0.y;
        int sx = dx >= 0 ? size.x : -size.x, sy = dy >= 0 ? size.y : -size.y;
        int nx = Mathf.Abs(dx) / Mathf.Max(1, size.x), ny = Mathf.Abs(dy) / Mathf.Max(1, size.y);
        if (!ring || nx == 0 || ny == 0)
        {
            if (Mathf.Abs(dx) >= Mathf.Abs(dy)) for (int i = 0; i <= nx && list.Count < 240; i++) list.Add(new Vector2Int(o0.x + i * sx, o0.y));
            else for (int i = 0; i <= ny && list.Count < 240; i++) list.Add(new Vector2Int(o0.x, o0.y + i * sy));
            return list;
        }
        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
        for (int i = 0; i <= nx; i++)
        {
            AddUnique(list, seen, new Vector2Int(o0.x + i * sx, o0.y));
            AddUnique(list, seen, new Vector2Int(o0.x + i * sx, o0.y + ny * sy));
        }
        for (int j = 1; j < ny; j++)
        {
            AddUnique(list, seen, new Vector2Int(o0.x, o0.y + j * sy));
            AddUnique(list, seen, new Vector2Int(o0.x + nx * sx, o0.y + j * sy));
        }
        return list;
    }

    static void AddUnique(List<Vector2Int> list, HashSet<Vector2Int> seen, Vector2Int v)
    {
        if (list.Count < 240 && seen.Add(v)) list.Add(v);
    }

    // 这块地能不能放 _sel：地形 / 占用 / 蓝图里重叠 / 唯一建筑。
    bool CanPlace(Vector2Int origin, Vector2Int size, out string why)
    {
        if (_sel != null && _sel.uniqueBuild)
        {
            WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
            bool taken = w != null && w.placedUniqueBuilds.Contains(_sel.id);
            if (!taken && _bp != null) foreach (BlueprintItem it in _bp.items) if (it.buildId == _sel.id) { taken = true; break; }
            if (taken) { why = L("bp_why_unique"); return false; }
        }
        return BlueprintSystem.CheckArea(origin, size, _bp, null, out why);
    }

    void WireClick(Vector2Int cell)
    {
        PowerNode node = PowerGrid.NodeAt(cell);
        if (node == null) { _wireA = null; _con.Feedback("> " + L("bp_con_wire_none"), WarnC, 3f); return; }
        if (_wireA == null) { _wireA = node; _flash = 1f; _con.Feedback("> " + LF("bp_con_wire_from", node.DisplayName), AmberC, 3f); return; }
        if (_wireA == node) { _wireA = null; return; }
        string msg = PowerGrid.ToggleLink(_wireA, node);
        _wireA = null;
        _flash = 1f;
        Print("> " + msg, AmberC);
        FlushLog(Flush.Type);
        RenderMap(true);
    }

    // ================= 键盘 =================

    void HandleKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || ChatManager.InputFocused) return;
        if (kb.escapeKey.wasPressedThisFrame)
        {
            if (_dragging) CancelDrag();
            else if (_menu != null && _menu.popup.IsOpen) CloseMenu();
            else if (_ask == Ask.Save || _ask == Ask.Send) CancelAsk();
            else if (_tool != Tool.Place) SetTool(Tool.Place);
            else Close();
            return;
        }
        if (_boot >= 0f) return;
        for (int d = 0; d <= 9; d++) if (DigitDown(kb, d)) { PickKey(d); return; }
        if ((kb.ctrlKey.isPressed || kb.leftCtrlKey.isPressed) && kb.zKey.wasPressedThisFrame) { Undo(); return; }
        if (kb.deleteKey.wasPressedThisFrame && _mode == Mode.Edit) SetTool(_tool == Tool.Delete ? Tool.Place : Tool.Delete);
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

    // 数字键 / 点命令行里的一行。
    void PickKey(int key)
    {
        for (int i = _log.Count - 1; i >= 0; i--)
        {
            LogLine l = _log[i];
            if (l.key != key || l.act == null) continue;
            if (key < 1000) _con.Echo(key.ToString());
            l.act();
            return;
        }
    }

    // ================= 工具 =================

    void SetTool(Tool t)
    {
        _tool = t;
        _wireA = null;
        if (t == Tool.Delete) _con.Feedback("> " + L("bp_con_delete_on"), BadC, 3f);
        if (t == Tool.Wire) _con.Feedback("> " + L("bp_con_wire_on"), AmberC, 4f);
        RefreshTools();
        RefreshTop();
    }

    public void Select(BuildableData d)
    {
        _sel = d;
        SetTool(Tool.Place);
        if (d != null)
        {
            Vector2Int size = d.FootprintSize(GridManager.Instance != null ? GridManager.Instance.cellSize : 1f);
            _con.Feedback("> " + LF("bp_con_selected", d.LocalizedName, size.x + "×" + size.y), DimC, 3f);
        }
    }

    void Undo()
    {
        if (_mode != Mode.Edit || _undo.Count == 0 || _bp == null) { _con.Feedback("> " + L("bp_con_undo_none"), DimC, 2f); return; }
        UndoStep s = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        foreach (BlueprintItem it in s.added) _bp.items.Remove(it);
        foreach (BlueprintItem it in s.removed) if (!_bp.items.Contains(it)) _bp.items.Add(it);
        if (s.removed.Count > 0) SetFlashArea(s.removed, PlanC);
        else if (s.added.Count > 0) SetFlashArea(s.added, BadC);
        _con.Feedback("> UNDO", DimC, 1.5f);
        Edited();
    }

    void PushUndo(UndoStep s)
    {
        _undo.Add(s);
        if (_undo.Count > 60) _undo.RemoveAt(0);
    }

    // 蓝图内容改了：之前的测试结果作废；正在问的保存 / 发送也作废。
    void Edited()
    {
        _itemsDirty = true;
        _okDirty = true;
        ClearTest();
        if (_ask == Ask.Save || _ask == Ask.Send) CancelAsk();
        RefreshMaterials();
        RefreshTop();
    }

    void ClickSave()
    {
        if (_mode != Mode.Edit || _bp == null || _boot >= 0f) return;
        if (_bp.items.Count == 0) { Print("ERROR: " + L("bp_con_empty"), BadC); FlushLog(Flush.Type); return; }
        AskSave(true);
    }

    void ClickTest()
    {
        if (_bp == null || _boot >= 0f) return;
        StartTest();
    }

    // ================= 提问（保存 / 发给谁 / 批不批）=================

    void AskSave(bool flush)
    {
        RemoveChoices();
        _ask = Ask.Save;
        if (_test == null || !_test.done) Print("  " + L("bp_con_untested"), WarnC);
        else if (_test.errors.Count > 0) Print("  " + LF("bp_con_save_errors", _test.errors.Count), WarnC);
        Print("> " + L("bp_ask_save"), Color.white);
        Choice(1, L("bp_opt_save"), DoSave);
        Choice(2, L("bp_opt_keep_editing"), CancelAsk);
        if (flush) FlushLog(Flush.Type);
    }

    void DoSave()
    {
        RemoveChoices();
        _ask = Ask.None;
        BlueprintSystem.Save(_bp);
        if (_draft == _bp) _draft = null;
        Print("> " + LF("bp_con_saved", _bp.id, _bp.name), OkC);
        RefreshTop();
        AskSend();
    }

    void AskSend()
    {
        RemoveChoices();
        _ask = Ask.Send;
        Print("> " + L("bp_ask_send"), Color.white);
        List<NpcData> list = AliveNpcs();
        Blueprint src = BlueprintSystem.Find(_bp.sourceId);
        for (int i = 0; i < list.Count; i++)
        {
            NpcData n = list[i];
            string role = NpcSociety.IsLeader(n) ? NpcSociety.RoleName("leader") : "";
            if (NpcSociety.HasRole(n)) role += (role.Length > 0 ? "·" : "") + NpcSociety.RoleName(n.role);
            string mark = src != null && src.authorId == n.id ? "  " + L("bp_mark_author") : "";
            string doing = string.IsNullOrEmpty(n.currentAction) ? L("term_user_idle") : n.currentAction;
            string line = n.npcName + (role.Length > 0 ? "  <alpha=#99>" + role + "<alpha=#FF>" : "") + mark + "<pos=46%><alpha=#80>" + doing + "<alpha=#FF>";
            NpcData captured = n;
            Choice(i < 9 ? i + 1 : _nextKey++, line, () => DoSend(captured));
        }
        if (list.Count == 0) Print("  " + L("term_user_none"), DimC);
        Choice(0, L("bp_opt_no_send"), () =>
        {
            RemoveChoices();
            _ask = Ask.None;
            Print("> " + L("bp_con_not_sent"), DimC);
            SetConsoleHeight(ConH);
        });
        FitConsole(list.Count + 5);
    }

    void DoSend(NpcData n)
    {
        if (n == null || _bp == null) return;
        RemoveChoices();
        _ask = Ask.None;
        int placed = BlueprintSystem.Send(_bp, n.id);
        Print("> " + LF("bp_con_sent", n.npcName, placed, _bp.items.Count), placed > 0 ? OkC : BadC);
        if (_draft == _bp) _draft = null;
        _mode = Mode.View;
        RefreshTools();
        RefreshTop();
        RenderMap(true);
        _okDirty = true;
        _itemsDirty = true;
        SetConsoleHeight(ConH);
        if (placed > 0) _closeAfter = 1.8f;
    }

    void AskReview(bool flush)
    {
        if (_bp == null || _bp.status != BlueprintStatus.Pending) return;
        RemoveChoices();
        _ask = Ask.Review;
        NpcData a = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(_bp.authorId) : null;
        string who = a != null ? a.npcName : _bp.authorName;
        Choice(1, LF("bp_opt_approve", who), DoApprove);
        Choice(2, LF("bp_opt_reject", who), DoReject);
        Choice(3, L("bp_opt_modify"), DoModify);
        if (flush) FlushLog(Flush.Type);
    }

    void DoApprove()
    {
        if (_bp == null || _bp.status != BlueprintStatus.Pending) return;
        RemoveChoices();
        _ask = Ask.None;
        int placed = BlueprintSystem.Approve(_bp);
        Print("> " + LF("bp_con_approved", _bp.authorName, placed, _bp.items.Count), placed > 0 ? OkC : BadC);
        if (placed == 0) Print("> " + L("bp_help_view"), DimC);   // 一处都放不下：留着窗口，可以复制一份改
        FlushLog(Flush.Type);
        _mode = Mode.View;
        RefreshTools();
        RefreshTop();
        RenderMap(true);
        _okDirty = true;
        _itemsDirty = true;
        if (placed > 0) _closeAfter = 1.8f;
    }

    void DoReject()
    {
        if (_bp == null || _bp.status != BlueprintStatus.Pending) return;
        RemoveChoices();
        _ask = Ask.None;
        BlueprintSystem.Reject(_bp);
        Print("> " + LF("bp_con_rejected", _bp.authorName), WarnC);
        FlushLog(Flush.Type);
        _mode = Mode.View;
        RefreshTools();
        RefreshTop();
        string author = _bp.authorId;
        _closeAfter = 1.5f;
        _openChatAfter = author;   // 关掉之后打开和他的对话：他会问为什么
    }

    void DoModify()
    {
        if (_bp == null) return;
        Blueprint copy = BlueprintSystem.Modify(_bp);
        if (copy == null) return;
        RemoveChoices();
        Show(copy, Mode.Edit);
        Print("> " + LF("bp_con_modify", copy.authorName), PlanC);
        FlushLog(Flush.Type);
    }

    void CancelAsk()
    {
        RemoveChoices();
        _ask = Ask.None;
        FlushLog(Flush.Keep);
    }

    void Choice(int key, string text, Action act)
    {
        string k = key < 1000 ? key.ToString() : "-";
        AddLine(new LogLine { text = " [" + k + "] " + text, act = act, key = key, choice = true });
    }

    // 旧的选项行失效（变成普通的灰字，留在命令行里当记录）。
    void RemoveChoices()
    {
        foreach (LogLine l in _log)
            if (l.choice && l.act != null) { l.act = null; l.key = -1; l.color = DimC; }
    }

    // ================= 命令行 =================

    void Print(string text, Color color = default(Color), Action act = null)
    {
        LogLine l = new LogLine { text = text ?? "", color = color, act = act };
        if (act != null) l.key = _nextKey++;
        AddLine(l);
    }

    void AddLine(LogLine l)
    {
        _log.Add(l);
        _scrollBack = 0;
        if (_log.Count > 300)
        {
            int n = _log.Count - 300;
            _log.RemoveRange(0, n);
            _printed = Mathf.Max(0, _printed - n);
        }
    }

    // 把命令行画出来（显示最后一屏；滚轮往上翻过就往前挪）。
    void FlushLog(Flush mode)
    {
        if (_con == null) return;
        int cap = _con.Capacity;
        _scrollBack = Mathf.Clamp(_scrollBack, 0, Mathf.Max(0, _log.Count - cap));
        int end = _log.Count - _scrollBack;
        int start = Mathf.Max(0, end - cap);
        _specs.Clear();
        for (int i = start; i < end; i++)
        {
            LogLine l = _log[i];
            _specs.Add(new TerminalConsole.Spec { text = l.text, color = l.color, key = l.act != null ? l.key : -1 });
        }
        if (mode == Flush.Type && (_printed >= _log.Count || _scrollBack > 0)) mode = _scrollBack > 0 ? Flush.Instant : Flush.Keep;
        switch (mode)
        {
            case Flush.Type: _con.SetPageFrom(_specs, Mathf.Clamp(_printed - start, 0, _specs.Count), 1400f); break;
            case Flush.Keep: _con.SetPage(_specs, false); break;
            default: _con.SetPageFrom(_specs, _specs.Count); break;
        }
        _printed = _log.Count;
    }

    void ScrollLog(int lines)
    {
        int max = Mathf.Max(0, _log.Count - _con.Capacity);
        int next = Mathf.Clamp(_scrollBack + lines, 0, max);
        if (next == _scrollBack) return;
        _scrollBack = next;
        FlushLog(Flush.Instant);
    }

    // 命令行要显示 lines 行：往上长高（地图跟着变矮），最矮是默认高度。
    void FitConsole(int lines)
    {
        float maxH = windowSize.y - MapTop - 10f - ToolH - 8f - 10f - MinMapH;
        float h = Mathf.Clamp(88f + lines * TerminalConsole.LineH + 2f, ConH, Mathf.Max(ConH, maxH));
        if (Mathf.Abs(h - _conH) > 0.5f) SetConsoleHeight(h);
        else FlushLog(Flush.Type);
    }

    void SetConsoleHeight(float h)
    {
        _conH = h;
        Layout();
    }

    // 地图 / 工具栏 / 命令行从上往下排。
    void Layout()
    {
        float conY = windowSize.y - _conH - 10f;
        float toolY = conY - ToolH - 8f;
        _viewW = windowSize.x - Pad * 2f;
        _viewH = Mathf.Max(120f, toolY - 10f - MapTop);
        Place(_viewRt, Pad, MapTop, _viewW, _viewH);
        float w = windowSize.x - Pad * 2f;
        Place(_conRect, Pad, conY, w, _conH);
        _con.SetRect(Pad, conY, w, _conH);
        RefreshTools();
        if (_zoom < MinZoom) _zoom = MinZoom;
        ClampView();
        FlushLog(Flush.Type);
    }

    // ================= 开机 / 说明 =================

    void BootLines()
    {
        Print("BPLAN.EXE v1.0  —  " + L("bp_boot_title"), TerminalConsole.Bright);
        _progressLine = new LogLine { text = "", color = Green };
        AddLine(_progressLine);
        SetBar(_progressLine, L("bp_boot_load"), 0f, "");
    }

    static void SetBar(LogLine line, string label, float p, string extra)
    {
        if (line == null) return;
        p = Mathf.Clamp01(p);
        int k = Mathf.RoundToInt(20f * p);
        line.text = "> " + label + " [" + new string('#', k) + new string('.', 20 - k) + "] " +
                    Mathf.RoundToInt(p * 100f).ToString().PadLeft(3) + "%" + (string.IsNullOrEmpty(extra) ? "" : "  " + extra);
    }

    // 地图从上往下扫出来（老显示器刷屏），同时命令行的进度条走完。
    void TickBoot(float dt)
    {
        if (_boot < 0f || _popup.Animating || _con.CrtBusy) return;
        _boot += dt;
        const float dur = 0.75f;
        float p = Mathf.Clamp01(_boot / dur);
        _cover.gameObject.SetActive(p < 1f);
        _scanLine.gameObject.SetActive(p < 1f);
        _cover.anchorMin = Vector2.zero;
        _cover.anchorMax = new Vector2(1f, 1f - p);
        _cover.offsetMin = _cover.offsetMax = Vector2.zero;
        _scanLine.anchorMin = new Vector2(0f, 1f - p);
        _scanLine.anchorMax = new Vector2(1f, 1f - p);
        _scanLine.sizeDelta = new Vector2(0f, 2f);
        _scanLine.anchoredPosition = Vector2.zero;
        SetBar(_progressLine, L("bp_boot_load"), p, "");
        FlushLog(Flush.Keep);
        if (p < 1f) return;
        _boot = -1f;
        _progressLine = null;
        int res = 0, built = 0;
        foreach (ResourceNode r in ResourceNode.All) if (r != null && !r.depleted && r.kind != ResourceKind.Grass) res++;
        foreach (BuildingFootprint fp in BuildingFootprint.All) if (fp != null && fp.GetComponent<ConstructionSite>() == null) built++;
        Print("> " + LF("bp_boot_stats", res, built, ConstructionSite.All.Count), DimC);
        if (_introPending)
        {
            _introPending = false;
            IntroLines(false);
        }
        if (_mode == Mode.Review) FitConsole(11); else FlushLog(Flush.Type);
    }

    void IntroLines(bool flush)
    {
        if (_bp == null) return;
        switch (_mode)
        {
            case Mode.Edit:
                Print("> " + L("bp_help_edit"), DimC);
                Print("> " + L("bp_help_edit2"), DimC);
                if (_bp.items.Count > 0) Print("> " + LF("bp_con_draft", _bp.items.Count), DimC);
                break;
            case Mode.Review:
            {
                NpcData a = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(_bp.authorId) : null;
                string role = a != null && NpcSociety.HasRole(a) ? "（" + NpcSociety.RoleName(a.role) + "）" : "";
                Print("> " + LF("bp_con_review_head", _bp.authorName + role, _bp.day), Color.white);
                Print("> " + _bp.name + "：" + BlueprintSystem.ItemLine(_bp), PlanC);
                Print("> " + L("bp_lbl_cost") + BlueprintSystem.CostLine(_bp));
                if (!string.IsNullOrEmpty(_bp.note)) Print("> " + L("bp_lbl_reason") + "“" + _bp.note + "”", DimC);
                Print("> " + L("bp_help_review"), DimC);
                AskReview(false);
                break;
            }
            default:
                Print("> " + _bp.name + "  —  " + _bp.authorName + "  /  " + BlueprintSystem.StatusText(_bp.status), Color.white);
                Print("> " + BlueprintSystem.ItemLine(_bp), PlanC);
                if (_bp.status == BlueprintStatus.Building || _bp.status == BlueprintStatus.Done)
                    Print("> " + LF("bp_con_progress", _bp.built, _bp.placed), DimC);
                Print("> " + L("bp_help_view"), DimC);
                break;
        }
        if (flush) FlushLog(Flush.Type);
    }

    // ================= 视图 =================

    float MinZoom => Mathf.Max(1.5f, Mathf.Min(_viewW / Mathf.Max(1, _W), _viewH / Mathf.Max(1, _H)) * 0.95f);

    void CenterOnBlueprint()
    {
        _zoom = Mathf.Clamp(defaultZoom, MinZoom, maxZoom);
        if (_bp != null && _bp.items.Count > 0)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (BlueprintItem it in _bp.items)
            {
                min = Vector2.Min(min, it.origin);
                max = Vector2.Max(max, it.origin + it.size);
            }
            _cx = (min.x + max.x) * 0.5f;
            _cy = (min.y + max.y) * 0.5f;
            // 整份蓝图放得下（四周留几格）
            float fit = Mathf.Min(_viewW / Mathf.Max(1f, max.x - min.x + 8f), _viewH / Mathf.Max(1f, max.y - min.y + 8f));
            _zoom = Mathf.Clamp(Mathf.Min(_zoom, fit), MinZoom, maxZoom);
        }
        else
        {
            GridManager g = GridManager.Instance;
            Vector2Int c = g != null ? g.WorldToCell(NpcNav.CampCenter(Vector3.zero)) : Vector2Int.zero;
            _cx = c.x + 0.5f;
            _cy = c.y + 0.5f;
        }
        ClampView();
    }

    // 地图贴图按缩放和中心摆好（贴图外面露出底色，不拉伸边缘）。
    void ClampView()
    {
        _cx = Mathf.Clamp(_cx, 0f, Mathf.Max(1, _W));
        _cy = Mathf.Clamp(_cy, 0f, Mathf.Max(1, _H));
        Vector2 o = CellToLocal(0f, 0f);
        Vector2 size = new Vector2(_W * _zoom, _H * _zoom);
        PlaceMap(_mapImg, o, size);
        PlaceMap(_testImg, o, size);
        _itemsDirty = true;
    }

    static void PlaceMap(RawImage img, Vector2 o, Vector2 size)
    {
        if (img == null) return;
        RectTransform r = img.rectTransform;
        r.anchoredPosition = o;
        r.sizeDelta = size;
    }

    Vector2 CellToLocal(float x, float y) => new Vector2((x - _cx) * _zoom + _viewW * 0.5f, (y - _cy) * _zoom + _viewH * 0.5f);
    Vector2 LocalToCellF(Vector2 local) => new Vector2((local.x - _viewW * 0.5f) / _zoom + _cx, (local.y - _viewH * 0.5f) / _zoom + _cy);

    Vector2Int LocalToCell(Vector2 local)
    {
        Vector2 c = LocalToCellF(local);
        return new Vector2Int(Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.y));
    }

    Rect AreaRect(Vector2Int o, Vector2Int s)
    {
        Vector2 a = CellToLocal(o.x, o.y), b = CellToLocal(o.x + s.x, o.y + s.y);
        return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
    }

    Rect CellBox(Vector2Int a, Vector2Int b)
    {
        Vector2Int min = Vector2Int.Min(a, b), max = Vector2Int.Max(a, b);
        return AreaRect(min, max - min + Vector2Int.one);
    }

    Rect NodeRect(PowerNode n)
    {
        BuildingFootprint fp = n != null ? n.GetComponent<BuildingFootprint>() : null;
        if (fp != null) return AreaRect(fp.origin, fp.size);
        return AreaRect(n != null ? n.Cell : Vector2Int.zero, Vector2Int.one);
    }

    static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
    static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
    static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    // ================= 蓝图里的建筑（青色框）=================

    void RefreshItems()
    {
        int n = _bp != null ? _bp.items.Count : 0;
        bool placedAlready = _bp != null && (_bp.status == BlueprintStatus.Building || _bp.status == BlueprintStatus.Done);
        if (_okDirty) { _okCache.Clear(); _okDirty = false; }
        for (int i = 0; i < n; i++)
        {
            BlueprintItem it = _bp.items[i];
            ItemView v = GetItemView(_itemViews, _itemsRoot, i, true);
            Rect r = AreaRect(it.origin, it.size);
            bool visible = r.xMax > -4f && r.xMin < _viewW + 4f && r.yMax > -4f && r.yMin < _viewH + 4f;
            if (v.rt.gameObject.activeSelf != visible) v.rt.gameObject.SetActive(visible);
            if (!visible) continue;
            bool ok = placedAlready || ItemOk(it);
            bool err = _test != null && _test.done && _test.ItemError(it);
            Color c = placedAlready ? DimC : (ok && !err) ? PlanC : BadC;
            v.rt.anchoredPosition = r.min;
            v.rt.sizeDelta = r.size;
            v.fill.color = new Color(c.r, c.g, c.b, placedAlready ? 0.08f : 0.16f);
            v.frame.color = new Color(c.r, c.g, c.b, placedAlready ? 0.5f : 0.9f);
            string label = r.width >= 16f && r.height >= 12f ? ShortName(it.buildId, r.width) : "";
            if (v.label.text != label) v.label.text = label;
            v.label.color = new Color(c.r, c.g, c.b, 0.95f);
            v.label.fontSize = Mathf.Clamp(r.height * 0.42f, 9f, 15f);
        }
        for (int i = n; i < _itemViews.Count; i++) if (_itemViews[i].rt.gameObject.activeSelf) _itemViews[i].rt.gameObject.SetActive(false);
    }

    // 这一项现在还放不放得下（结果缓存起来，地图 / 蓝图变了才重新算）。
    bool ItemOk(BlueprintItem it)
    {
        if (_okCache.TryGetValue(it, out bool ok)) return ok;
        ok = BlueprintSystem.CheckArea(it.origin, it.size, _bp, it, out _);
        _okCache[it] = ok;
        return ok;
    }

    // 框里写的名字：放得下就写全名，放不下写第一个字。
    string ShortName(string id, float width)
    {
        if (!_shortNames.TryGetValue(id, out string full))
        {
            BuildableData d = BlueprintSystem.FindData(id);
            full = d != null ? d.LocalizedName : id;
            _shortNames[id] = full;
        }
        if (string.IsNullOrEmpty(full)) return "";
        return width >= full.Length * 13f + 6f ? full : full.Substring(0, 1);
    }

    void Ghost(int i, Rect r, bool ok)
    {
        ItemView v = GetItemView(_ghostViews, _ghostRoot, i, false);
        if (!v.rt.gameObject.activeSelf) v.rt.gameObject.SetActive(true);
        Color c = ok ? Green : BadC;
        v.rt.anchoredPosition = r.min;
        v.rt.sizeDelta = r.size;
        v.fill.color = new Color(c.r, c.g, c.b, ok ? 0.14f : 0.22f);
        v.frame.color = new Color(c.r, c.g, c.b, 0.35f);
    }

    void HideGhosts()
    {
        foreach (ItemView v in _ghostViews) if (v.rt.gameObject.activeSelf) v.rt.gameObject.SetActive(false);
    }

    ItemView GetItemView(List<ItemView> pool, RectTransform parent, int i, bool label)
    {
        while (pool.Count <= i)
        {
            ItemView v = new ItemView();
            v.rt = NewRect("Item", parent);
            v.rt.anchorMin = v.rt.anchorMax = v.rt.pivot = Vector2.zero;
            v.fill = v.rt.gameObject.AddComponent<Image>();
            v.fill.raycastTarget = false;
            v.frame = UiStyle.Frame(v.rt, PlanC, 1.2f);
            if (label)
            {
                v.label = NewText(v.rt, 11f, PlanC, TextAlignmentOptions.Center);
                Fill(v.label.rectTransform);
                v.label.textWrappingMode = TextWrappingModes.NoWrap;
                v.label.overflowMode = TextOverflowModes.Ellipsis;
            }
            pool.Add(v);
        }
        return pool[i];
    }

    // ================= NPC 小方块 =================

    void UpdateNpcDots()
    {
        GridManager g = GridManager.Instance;
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        int k = 0;
        if (g != null && w != null)
            foreach (NpcData n in w.npcs)
            {
                if (n == null || n.IsDead) continue;
                NpcMover m = NpcView.GetMover(n.id);
                if (m == null) continue;
                Vector3 p = m.transform.position;
                Vector2 local = CellToLocal((p.x - g.origin.x) / g.cellSize, (p.y - g.origin.y) / g.cellSize);
                NpcDot d = GetDot(k++);
                bool vis = local.x > 0f && local.x < _viewW && local.y > 0f && local.y < _viewH;
                if (d.rt.gameObject.activeSelf != vis) d.rt.gameObject.SetActive(vis);
                if (!vis) continue;
                d.rt.anchoredPosition = local;
                d.img.color = new Color(1f, 1f, 1f, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_time * 2.4f + k)));
                bool showName = _zoom >= 9f;
                if (d.label.gameObject.activeSelf != showName) d.label.gameObject.SetActive(showName);
                if (showName && d.label.text != n.npcName) d.label.text = n.npcName;
            }
        for (int i = k; i < _npcDots.Count; i++) if (_npcDots[i].rt.gameObject.activeSelf) _npcDots[i].rt.gameObject.SetActive(false);
    }

    NpcDot GetDot(int i)
    {
        while (_npcDots.Count <= i)
        {
            NpcDot d = new NpcDot();
            d.rt = NewRect("Npc", _npcRoot);
            d.rt.anchorMin = d.rt.anchorMax = Vector2.zero;
            d.rt.pivot = new Vector2(0.5f, 0.5f);
            d.rt.sizeDelta = new Vector2(7f, 7f);
            d.img = d.rt.gameObject.AddComponent<Image>();
            d.img.raycastTarget = false;
            UiStyle.Frame(d.rt, new Color(0f, 0f, 0f, 0.8f), 1f);
            d.label = NewText(d.rt, 11f, Green, TextAlignmentOptions.Left);
            RectTransform lr = d.label.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(1f, 0.5f);
            lr.pivot = new Vector2(0f, 0.5f);
            lr.anchoredPosition = new Vector2(4f, 0f);
            lr.sizeDelta = new Vector2(120f, 16f);
            d.label.textWrappingMode = TextWrappingModes.NoWrap;
            _npcDots.Add(d);
        }
        return _npcDots[i];
    }

    // ================= 红色十字 + 方框（测试报错的位置）=================

    void FocusCell(Vector2Int cell)
    {
        _cx = cell.x + 0.5f;
        _cy = cell.y + 0.5f;
        _zoom = Mathf.Clamp(Mathf.Max(_zoom, 16f), MinZoom, maxZoom);
        ClampView();
        Marker mk = new Marker { cell = cell, t = 0f };
        mk.rt = NewRect("Marker", _fxRoot);
        mk.rt.anchorMin = mk.rt.anchorMax = Vector2.zero;
        mk.rt.pivot = new Vector2(0.5f, 0.5f);
        mk.box = UiStyle.Frame(mk.rt, BadC, 2f);
        for (int i = 0; i < 2; i++)
        {
            RectTransform bar = NewRect("X" + i, mk.rt);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.localEulerAngles = new Vector3(0f, 0f, i == 0 ? 45f : -45f);
            Image img = bar.gameObject.AddComponent<Image>();
            img.color = BadC;
            img.raycastTarget = false;
            mk.bars[i] = bar;
            mk.barImgs[i] = img;
        }
        _markers.Add(mk);
    }

    void UpdateMarkers(float dt)
    {
        for (int i = _markers.Count - 1; i >= 0; i--)
        {
            Marker m = _markers[i];
            m.t += dt;
            if (m.t > 2.4f || m.rt == null) { if (m.rt != null) Destroy(m.rt.gameObject); _markers.RemoveAt(i); continue; }
            float size = Mathf.Max(26f, _zoom * 3f);
            m.rt.anchoredPosition = CellToLocal(m.cell.x + 0.5f, m.cell.y + 0.5f);
            m.rt.sizeDelta = new Vector2(size, size);
            foreach (RectTransform b in m.bars) b.sizeDelta = new Vector2(size * 1.25f, 2.5f);
            // 先快闪几下，最后淡出
            float a = m.t < 1.6f ? (Mathf.Repeat(m.t, 0.32f) < 0.2f ? 1f : 0.25f) : Mathf.Clamp01((2.4f - m.t) / 0.8f);
            Color c = WithA(BadC, a);
            m.box.color = c;
            foreach (Image img in m.barImgs) img.color = c;
        }
    }

    // ================= 寻路测试 =================

    class TestTarget
    {
        public string error;        // 走不到时报的错
        public Vector2Int cell;     // 报错的位置
        public readonly List<int> ring = new List<int>();  // 能站的格（到了任何一格就算走得到）
        public BlueprintItem item;
        public int hit = -1;
    }

    class TestRun
    {
        public int W, H, start, visited, walkable, qHead, qTail, phase, pathIdx, pathStep, pathTotal, pathDrawn, logStart;
        public bool[] block;
        public int[] parent, queue;
        public float t;
        public bool done;
        public readonly List<TestTarget> targets = new List<TestTarget>();
        public readonly List<TestTarget> errors = new List<TestTarget>();
        public readonly HashSet<BlueprintItem> errItems = new HashSet<BlueprintItem>();
        public readonly List<List<int>> paths = new List<List<int>>();
        public readonly List<int> front = new List<int>();
        public bool ItemError(BlueprintItem it) => it != null && errItems.Contains(it);
    }

    static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
    static readonly Color32 ScanSeen = new Color32(40, 170, 210, 64);
    static readonly Color32 ScanFront = new Color32(170, 250, 255, 210);
    static readonly Color32 ScanPath = new Color32(150, 255, 255, 235);
    static readonly Color32 ScanErr = new Color32(255, 90, 90, 210);

    void StartTest()
    {
        GridManager g = GridManager.Instance;
        if (g == null || _bp == null) return;
        RemoveChoices();
        _ask = Ask.None;
        ClearTest();
        int W = g.gridWidth, H = g.gridHeight;
        TestRun t = new TestRun { W = W, H = H, block = new bool[W * H], parent = new int[W * H], queue = new int[W * H] };
        for (int i = 0; i < t.parent.Length; i++) t.parent[i] = -1;

        // 1. 哪里走不过去：墙、水、挡路的树石矿、挡路的建筑（建好的 / 施工中按建好以后算）、蓝图里挡路的建筑
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Vector2Int c = new Vector2Int(x, y);
                bool b = TerrainSystem.IsWall(c) || TerrainSystem.TypeAt(c) == TerrainType.Water;
                if (!b)
                {
                    ResourceNode r = TerrainSystem.ResourceAt(c);
                    b = r != null && r.Blocks;
                }
                t.block[y * W + x] = b;
            }
        foreach (BuildingFootprint fp in BuildingFootprint.All)
            if (fp != null && BuildingBlocks(fp)) MarkArea(t, fp.origin, fp.size);
        foreach (BlueprintItem it in _bp.items)
        {
            BuildableData d = BlueprintSystem.FindData(it.buildId);
            if (d != null && d.blocksMovement) MarkArea(t, it.origin, it.size);
        }
        for (int i = 0; i < t.block.Length; i++) if (!t.block[i]) t.walkable++;

        // 2. 起点：营地中心（被挡住了就找最近的空地）
        Vector2Int sc = g.WorldToCell(NpcNav.CampCenter(Vector3.zero));
        t.start = NearestFree(t, sc);
        if (t.start < 0) { Print("ERROR: " + L("bp_test_no_start"), BadC); FlushLog(Flush.Type); return; }
        t.parent[t.start] = t.start;
        t.queue[t.qTail++] = t.start;

        // 3. 要走到的地方：蓝图里每一项旁边、每个人、每座建好的（不挡路的）建筑
        foreach (BlueprintItem it in _bp.items)
        {
            BuildableData d = BlueprintSystem.FindData(it.buildId);
            string name = d != null ? d.LocalizedName : it.buildId;
            TestTarget tt = new TestTarget { item = it, cell = it.Center, error = LF("bp_test_item", name, g.CellToLabel(it.Center.x, it.Center.y)) };
            RingCells(t, it.origin, it.size, tt.ring);
            t.targets.Add(tt);
        }
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w != null)
            foreach (NpcData n in w.npcs)
            {
                if (n == null || n.IsDead) continue;
                NpcMover m = NpcView.GetMover(n.id);
                if (m == null) continue;
                Vector2Int c = g.WorldToCell(m.transform.position);
                if (!g.InBounds(c.x, c.y)) continue;
                TestTarget tt = new TestTarget { cell = c, error = LF("bp_test_npc", n.npcName, g.CellToLabel(c.x, c.y)) };
                int idx = c.y * W + c.x;
                if (!t.block[idx]) tt.ring.Add(idx); else RingCells(t, c, Vector2Int.one, tt.ring);
                t.targets.Add(tt);
            }
        foreach (BuildingFootprint fp in BuildingFootprint.All)
        {
            if (fp == null || BuildingBlocks(fp)) continue;
            string name = g.GetOccupied(fp.origin.x, fp.origin.y);
            if (string.IsNullOrEmpty(name)) name = fp.gameObject.name.Replace("(Clone)", "");
            Vector2Int c = new Vector2Int(fp.origin.x + (fp.size.x - 1) / 2, fp.origin.y + (fp.size.y - 1) / 2);
            TestTarget tt = new TestTarget { cell = c, error = LF("bp_test_building", name, g.CellToLabel(c.x, c.y)) };
            RingCells(t, fp.origin, fp.size, tt.ring);
            t.targets.Add(tt);
        }

        t.logStart = _log.Count;
        Print("> TEST  " + LF("bp_test_start", t.targets.Count), Color.white);
        _progressLine = new LogLine { text = "", color = PlanC };
        AddLine(_progressLine);
        SetBar(_progressLine, L("bp_test_scan"), 0f, "");
        _test = t;
        EnsureTestTexture(W, H);
        FlushLog(Flush.Type);
    }

    static bool BuildingBlocks(BuildingFootprint fp)
    {
        ConstructionSite site = fp.GetComponent<ConstructionSite>();
        if (site != null) return site.data != null && site.data.blocksMovement;   // 施工中：按建好以后算
        NavMeshModifier mod = fp.GetComponent<NavMeshModifier>();
        return mod != null && mod.overrideArea && mod.area == 1;
    }

    static void MarkArea(TestRun t, Vector2Int o, Vector2Int s)
    {
        for (int y = o.y; y < o.y + s.y; y++)
            for (int x = o.x; x < o.x + s.x; x++)
                if (x >= 0 && y >= 0 && x < t.W && y < t.H) t.block[y * t.W + x] = true;
    }

    static void RingCells(TestRun t, Vector2Int o, Vector2Int s, List<int> ring)
    {
        for (int y = o.y - 1; y <= o.y + s.y; y++)
            for (int x = o.x - 1; x <= o.x + s.x; x++)
            {
                bool inside = x >= o.x && x < o.x + s.x && y >= o.y && y < o.y + s.y;
                if (inside || x < 0 || y < 0 || x >= t.W || y >= t.H) continue;
                int i = y * t.W + x;
                if (!t.block[i]) ring.Add(i);
            }
    }

    static int NearestFree(TestRun t, Vector2Int c)
    {
        for (int r = 0; r <= 8; r++)
            for (int y = c.y - r; y <= c.y + r; y++)
                for (int x = c.x - r; x <= c.x + r; x++)
                {
                    if (Mathf.Max(Mathf.Abs(x - c.x), Mathf.Abs(y - c.y)) != r) continue;
                    if (x < 0 || y < 0 || x >= t.W || y >= t.H) continue;
                    if (!t.block[y * t.W + x]) return y * t.W + x;
                }
        return -1;
    }

    void TickTest(float dt)
    {
        TestRun t = _test;
        t.t += dt;
        EnsureTestTexture(t.W, t.H);
        if (t.phase == 0) ScanStep(t, dt);
        else if (t.phase == 1) PathStep(t, dt);
        else FinishTest(t);
    }

    // 扫描：从营地中心一圈圈往外扫（八个方向，斜着走不能切墙角），最前面一圈是亮的。
    void ScanStep(TestRun t, float dt)
    {
        int budget = Mathf.Max(24, Mathf.CeilToInt(t.walkable * dt / Mathf.Max(0.2f, scanSeconds)));
        foreach (int i in t.front) _testPx[i] = ScanSeen;
        t.front.Clear();
        while (budget-- > 0 && t.qHead < t.qTail)
        {
            int cur = t.queue[t.qHead++];
            t.visited++;
            _testPx[cur] = ScanFront;
            t.front.Add(cur);
            int x = cur % t.W, y = cur / t.W;
            for (int k = 0; k < 8; k++)
            {
                int nx = x + DX[k], ny = y + DY[k];
                if (nx < 0 || ny < 0 || nx >= t.W || ny >= t.H) continue;
                int ni = ny * t.W + nx;
                if (t.block[ni] || t.parent[ni] != -1) continue;
                if (k >= 4 && (t.block[y * t.W + nx] || t.block[ny * t.W + x])) continue;
                t.parent[ni] = cur;
                t.queue[t.qTail++] = ni;
            }
        }
        bool finished = t.qHead >= t.qTail;
        if (finished) { foreach (int i in t.front) _testPx[i] = ScanSeen; t.front.Clear(); }
        _testTex.SetPixels32(_testPx);
        _testTex.Apply(false);
        float p = finished ? 1f : t.walkable > 0 ? Mathf.Clamp01((float)t.visited / t.walkable) : 1f;
        SetBar(_progressLine, L("bp_test_scan"), p, t.visited.ToString("N0") + L("bp_test_cells"));
        FlushLog(Flush.Keep);
        if (!finished) return;

        // 哪些目标走得到（走得到的记下路线，下一步画出来）
        foreach (TestTarget tt in t.targets)
        {
            foreach (int i in tt.ring) if (t.parent[i] != -1) { tt.hit = i; break; }
            if (tt.hit < 0)
            {
                t.errors.Add(tt);
                if (tt.item != null) t.errItems.Add(tt.item);
            }
            else if (t.paths.Count < 60)
            {
                List<int> path = TracePath(t, tt.hit);
                t.paths.Add(path);
                t.pathTotal += path.Count;
            }
        }
        t.phase = 1;
        _progressLine = new LogLine { text = "", color = PlanC };
        AddLine(_progressLine);
        SetBar(_progressLine, L("bp_test_paths"), 0f, "0/" + t.paths.Count);
        FlushLog(Flush.Type);
    }

    // 画路线：营地中心 → 每一个走得到的目标（青色亮线，一段段画出来）。
    void PathStep(TestRun t, float dt)
    {
        int budget = Mathf.Max(8, Mathf.CeilToInt(t.pathTotal * dt / Mathf.Max(0.2f, pathSeconds)));
        while (budget > 0 && t.pathIdx < t.paths.Count)
        {
            List<int> path = t.paths[t.pathIdx];
            while (budget > 0 && t.pathStep < path.Count) { _testPx[path[t.pathStep++]] = ScanPath; budget--; t.pathDrawn++; }
            if (t.pathStep >= path.Count) { t.pathIdx++; t.pathStep = 0; }
        }
        _testTex.SetPixels32(_testPx);
        _testTex.Apply(false);
        bool finished = t.pathIdx >= t.paths.Count;
        float p = finished ? 1f : t.pathTotal > 0 ? (float)t.pathDrawn / t.pathTotal : 1f;
        SetBar(_progressLine, L("bp_test_paths"), p, Mathf.Min(t.pathIdx, t.paths.Count) + "/" + t.paths.Count);
        FlushLog(Flush.Keep);
        if (finished) t.phase = 2;
    }

    static List<int> TracePath(TestRun t, int end)
    {
        List<int> path = new List<int>();
        int cur = end, guard = 0;
        while (cur >= 0 && guard++ < t.W * t.H)
        {
            path.Add(cur);
            int p = t.parent[cur];
            if (p == cur || p < 0) break;
            cur = p;
        }
        path.Reverse();
        return path;
    }

    void FinishTest(TestRun t)
    {
        t.phase = 3;
        t.done = true;
        _progressLine = null;
        GridManager g = GridManager.Instance;
        Vector2Int sc = new Vector2Int(t.start % t.W, t.start / t.W);

        // 营地被围死了吗：营地范围（往外两格）外面几乎扫不到 = 出不去
        CampZone z = CampZone.Instance;
        if (z != null && z.Ready)
        {
            int x0 = z.Origin.x - 2, y0 = z.Origin.y - 2, x1 = z.Origin.x + z.Size.x + 2, y1 = z.Origin.y + z.Size.y + 2;
            int outside = 0;
            for (int i = 0; i < t.W * t.H && outside < 30; i++)
            {
                if (t.parent[i] == -1) continue;
                int x = i % t.W, y = i / t.W;
                if (x < x0 || y < y0 || x >= x1 || y >= y1) outside++;
            }
            if (outside < 30) t.errors.Insert(0, new TestTarget { cell = sc, error = LF("bp_test_enclosed", g.CellToLabel(sc.x, sc.y)) });
        }

        // 探明的水边还走不走得到（有水的话）
        int shore = -1, shoreBest = int.MaxValue;
        bool anyWater = false, waterOk = false;
        for (int i = 0; i < t.W * t.H && !waterOk; i++)
        {
            int x = i % t.W, y = i / t.W;
            if (!BlueprintSystem.Explored(x, y) || TerrainSystem.TypeAt(new Vector2Int(x, y)) != TerrainType.Water) continue;
            anyWater = true;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + DX[k], ny = y + DY[k];
                if (nx < 0 || ny < 0 || nx >= t.W || ny >= t.H) continue;
                int ni = ny * t.W + nx;
                if (t.block[ni]) continue;
                if (t.parent[ni] != -1) { waterOk = true; break; }
                int d = Mathf.Abs(nx - sc.x) + Mathf.Abs(ny - sc.y);
                if (d < shoreBest) { shoreBest = d; shore = ni; }
            }
        }
        if (anyWater && !waterOk && shore >= 0)
        {
            Vector2Int c = new Vector2Int(shore % t.W, shore / t.W);
            t.errors.Add(new TestTarget { cell = c, error = LF("bp_test_water", g.CellToLabel(c.x, c.y)) });
        }

        // 走不到的地方涂红
        foreach (TestTarget e in t.errors)
        {
            if (e.item != null)
            {
                for (int y = e.item.origin.y; y < e.item.origin.y + e.item.size.y; y++)
                    for (int x = e.item.origin.x; x < e.item.origin.x + e.item.size.x; x++)
                        if (x >= 0 && y >= 0 && x < t.W && y < t.H) _testPx[y * t.W + x] = ScanErr;
            }
            else if (e.cell.x >= 0 && e.cell.y >= 0 && e.cell.x < t.W && e.cell.y < t.H) _testPx[e.cell.y * t.W + e.cell.x] = ScanErr;
        }
        _testTex.SetPixels32(_testPx);
        _testTex.Apply(false);

        Print("> " + LF("bp_test_done", t.visited.ToString("N0"), t.t.ToString("0.0")), Color.white);
        if (t.errors.Count == 0) Print("> " + LF("bp_test_ok", t.targets.Count), OkC);
        else
        {
            Print("> " + LF("bp_test_errors", t.errors.Count), BadC);
            int shown = 0;
            foreach (TestTarget e in t.errors)
            {
                if (shown++ >= 12) { Print("  " + LF("bp_test_more", t.errors.Count - 12), BadC); break; }
                Vector2Int cell = e.cell;
                Print("  ! " + e.error, BadC, () => FocusCell(cell));
            }
            Print("  " + L("bp_test_click"), DimC);
        }
        _itemsDirty = true;
        if (_mode == Mode.Edit && _bp.items.Count > 0) AskSave(false);
        else if (_mode == Mode.Review) AskReview(false);
        FitConsole(_log.Count - t.logStart);
    }

    void EnsureTestTexture(int W, int H)
    {
        if (_testTex != null && _testTex.width == W && _testTex.height == H) return;
        if (_testTex != null) Destroy(_testTex);
        _testTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "BlueprintTest" };
        _testPx = new Color32[W * H];
        _testTex.SetPixels32(_testPx);
        _testTex.Apply(false);
        _testImg.texture = _testTex;
        _testImg.gameObject.SetActive(true);
        ClampView();
    }

    void ClearTest()
    {
        _test = null;
        if (_testPx != null)
        {
            for (int i = 0; i < _testPx.Length; i++) _testPx[i] = new Color32(0, 0, 0, 0);
            if (_testTex != null) { _testTex.SetPixels32(_testPx); _testTex.Apply(false); }
        }
        foreach (Marker m in _markers) if (m.rt != null) Destroy(m.rt.gameObject);
        _markers.Clear();
        _itemsDirty = true;
    }

    // ================= 示意图（几何图形）=================

    static readonly Color32 CBg = new Color32(5, 12, 8, 255);
    static readonly Color32 CGrid = new Color32(13, 28, 19, 255);
    static readonly Color32 CGridMajor = new Color32(22, 46, 31, 255);
    static readonly Color32 CWater = new Color32(6, 22, 34, 255);
    static readonly Color32 CWave = new Color32(36, 96, 138, 255);
    static readonly Color32 CSwamp = new Color32(11, 21, 10, 255);
    static readonly Color32 CSwampDot = new Color32(58, 76, 30, 255);
    static readonly Color32 CFloor = new Color32(9, 22, 15, 255);
    static readonly Color32 CFloorEdge = new Color32(22, 52, 35, 255);
    static readonly Color32 CWall = new Color32(26, 38, 31, 255);
    static readonly Color32 CWallHatch = new Color32(46, 64, 53, 255);
    static readonly Color32 CFog = new Color32(3, 6, 4, 255);
    static readonly Color32 CFogDot = new Color32(18, 28, 22, 255);
    static readonly Color32 CTree = new Color32(96, 205, 122, 255);
    static readonly Color32 CStone = new Color32(150, 160, 152, 255);
    static readonly Color32 COre = new Color32(96, 205, 225, 255);
    static readonly Color32 CBuilt = new Color32(130, 245, 160, 255);
    static readonly Color32 CBuiltFill = new Color32(34, 80, 50, 255);
    static readonly Color32 CSite = new Color32(240, 210, 96, 255);
    static readonly Color32 CWire = new Color32(230, 170, 64, 255);
    static readonly Color32 CCamp = new Color32(60, 150, 120, 255);

    // full = true：连地形底图一起重画（打开时、迷雾变了）；false：只在有变化时重画资源 / 建筑。
    void RenderMap(bool full)
    {
        GridManager g = GridManager.Instance;
        if (g == null || _mapImg == null) return;
        int W = g.gridWidth, H = g.gridHeight, P = Mathf.Clamp(pixelsPerCell, 3, 10);
        int tw = W * P, th = H * P;
        if (_mapTex == null || _mapTex.width != tw || _mapTex.height != th)
        {
            if (_mapTex != null) Destroy(_mapTex);
            _mapTex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "BlueprintMap" };
            _mapPx = new Color32[tw * th];
            _basePx = new Color32[tw * th];
            _mapImg.texture = _mapTex;
            full = true;
        }
        bool resized = _W != W || _H != H;
        _W = W;
        _H = H;
        if (resized) ClampView();

        int fogSig = FogSignature(W, H);
        int sig = ContentSignature();
        if (!full && fogSig == _fogSig && sig == _mapSig) return;
        if (full || fogSig != _fogSig) { RenderBase(W, H, P); _fogSig = fogSig; }
        _mapSig = sig;
        _okDirty = true;      // 地图变了：蓝图里每一项放不放得下要重新算
        _itemsDirty = true;

        Array.Copy(_basePx, _mapPx, _basePx.Length);
        // 资源：树 = 三角形，石头 = 方形，矿石 = 菱形（草不画）
        foreach (ResourceNode r in ResourceNode.All)
        {
            if (r == null || r.depleted || r.kind == ResourceKind.Grass) continue;
            if (!BlueprintSystem.Explored(r.origin.x, r.origin.y)) continue;
            int x0 = r.origin.x * P, y0 = r.origin.y * P, sz = r.size * P, ins = r.size == 1 ? 1 : 2;
            switch (r.kind)
            {
                case ResourceKind.Tree: Triangle(tw, x0 + ins, y0 + ins, sz - ins * 2, CTree, r.size >= 3); break;
                case ResourceKind.Stone: RectOutline(tw, x0 + ins, y0 + ins, sz - ins * 2, sz - ins * 2, CStone); break;
                default: Diamond(tw, x0 + ins, y0 + ins, sz - ins * 2, COre); break;
            }
        }
        // 营地范围（虚线）
        CampZone z = CampZone.Instance;
        if (z != null && z.Ready) DashedRect(tw, z.Origin.x * P, z.Origin.y * P, z.Size.x * P, z.Size.y * P, CCamp, 3);
        // 建筑：建好的实线框（挡路的填实），施工中的虚线框
        foreach (BuildingFootprint fp in BuildingFootprint.All)
        {
            if (fp == null) continue;
            int x0 = fp.origin.x * P, y0 = fp.origin.y * P, w = fp.size.x * P, h = fp.size.y * P;
            if (fp.GetComponent<ConstructionSite>() != null) { DashedRect(tw, x0 + 1, y0 + 1, w - 2, h - 2, CSite, 2); continue; }
            if (BuildingBlocks(fp)) { FillRect(tw, th, x0 + 1, y0 + 1, w - 2, h - 2, CBuiltFill); RectOutline(tw, x0, y0, w, h, CBuilt); }
            else { RectOutline(tw, x0 + 1, y0 + 1, w - 2, h - 2, CBuilt); if (w > 8 && h > 8) RectOutline(tw, x0 + 3, y0 + 3, w - 6, h - 6, CBuilt); }
        }
        // 电线
        List<PowerNode> nodes = new List<PowerNode>();
        foreach (PowerNode n in PowerNode.All) if (n != null) nodes.Add(n);
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
                if (PowerGrid.AreLinked(nodes[i], nodes[j]))
                {
                    Vector2Int a = nodes[i].Cell, b = nodes[j].Cell;
                    Line(tw, th, a.x * P + P / 2, a.y * P + P / 2, b.x * P + P / 2, b.y * P + P / 2, CWire);
                }
        _mapTex.SetPixels32(_mapPx);
        _mapTex.Apply(false);
    }

    // 地形底图：地形 + 迷雾 + 网格线。
    void RenderBase(int W, int H, int P)
    {
        int tw = W * P, th = H * P;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int px0 = x * P, py0 = y * P;
                Vector2Int c = new Vector2Int(x, y);
                if (!BlueprintSystem.Explored(x, y))
                {
                    for (int py = py0; py < py0 + P; py++)
                        for (int px = px0; px < px0 + P; px++)
                            _basePx[py * tw + px] = Hash(px, py) % 23 == 0 ? CFogDot : CFog;   // 没探明：雪花噪点
                    continue;
                }
                if (TerrainSystem.IsWall(c))
                {
                    for (int py = py0; py < py0 + P; py++)
                        for (int px = px0; px < px0 + P; px++)
                            _basePx[py * tw + px] = (px + py) % 4 == 0 ? CWallHatch : CWall;   // 岩壁：斜线
                    continue;
                }
                TerrainType tt = TerrainSystem.TypeAt(c);
                for (int py = py0; py < py0 + P; py++)
                    for (int px = px0; px < px0 + P; px++)
                    {
                        Color32 col;
                        switch (tt)
                        {
                            case TerrainType.Water: col = (py % 4 == 1 && (px + (py / 4) * 3) % 8 < 4) ? CWave : CWater; break;       // 波纹
                            case TerrainType.Swamp: col = (px % 3 == 1 && py % 3 == 1) ? CSwampDot : CSwamp; break;                   // 点阵
                            case TerrainType.CampFloor: col = (px % (P * 3) == 0 || py % (P * 3) == 0) ? CFloorEdge : CFloor; break;  // 地砖缝
                            default: col = CBg; break;
                        }
                        _basePx[py * tw + px] = col;
                    }
            }
        // 网格线：每 3 小格（1 块地砖）一条淡线，每 30 小格一条亮一点的（只画在空地上）
        for (int x = 0; x <= W; x += 3)
        {
            int px = Mathf.Min(x * P, tw - 1);
            bool major = x % 30 == 0;
            for (int py = 0; py < th; py += major ? 1 : 2)
                if (IsPlain(_basePx[py * tw + px])) _basePx[py * tw + px] = major ? CGridMajor : CGrid;
        }
        for (int y = 0; y <= H; y += 3)
        {
            int py = Mathf.Min(y * P, th - 1);
            bool major = y % 30 == 0;
            for (int px = 0; px < tw; px += major ? 1 : 2)
                if (IsPlain(_basePx[py * tw + px])) _basePx[py * tw + px] = major ? CGridMajor : CGrid;
        }
    }

    static bool IsPlain(Color32 c) => c.r == CBg.r && c.g == CBg.g && c.b == CBg.b;

    static int Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) & 0x7fffffff;
        }
    }

    int FogSignature(int W, int H)
    {
        if (FogManager.Instance == null) return 0;
        int k = 0;
        for (int y = 0; y < H; y += 3)
            for (int x = 0; x < W; x += 3)
                if (FogManager.Instance.GetState(x, y) != FogState.Unexplored) k++;
        return k / 8;   // 揭开一点点不重画底图（省得一直重画）
    }

    static int ContentSignature()
    {
        unchecked
        {
            int h = 17;
            int res = 0;
            foreach (ResourceNode r in ResourceNode.All) if (r != null && !r.depleted && r.kind != ResourceKind.Grass) res++;
            h = h * 31 + res;
            h = h * 31 + BuildingFootprint.All.Count;
            h = h * 31 + ConstructionSite.All.Count;
            foreach (BuildingFootprint fp in BuildingFootprint.All) if (fp != null) h = h * 7 + fp.origin.x * 3 + fp.origin.y;
            return h;
        }
    }

    // —— 画图小工具（像素坐标，左下角为原点）——

    void Px(int tw, int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= tw) return;
        int i = y * tw + x;
        if (i < _mapPx.Length) _mapPx[i] = c;
    }

    void FillRect(int tw, int th, int x0, int y0, int w, int h, Color32 c)
    {
        for (int y = Mathf.Max(0, y0); y < Mathf.Min(th, y0 + h); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(tw, x0 + w); x++)
                _mapPx[y * tw + x] = c;
    }

    void RectOutline(int tw, int x0, int y0, int w, int h, Color32 c)
    {
        if (w <= 0 || h <= 0) return;
        for (int x = x0; x < x0 + w; x++) { Px(tw, x, y0, c); Px(tw, x, y0 + h - 1, c); }
        for (int y = y0; y < y0 + h; y++) { Px(tw, x0, y, c); Px(tw, x0 + w - 1, y, c); }
    }

    void DashedRect(int tw, int x0, int y0, int w, int h, Color32 c, int dash)
    {
        if (w <= 0 || h <= 0) return;
        for (int x = x0; x < x0 + w; x++) if ((x - x0) / dash % 2 == 0) { Px(tw, x, y0, c); Px(tw, x, y0 + h - 1, c); }
        for (int y = y0; y < y0 + h; y++) if ((y - y0) / dash % 2 == 0) { Px(tw, x0, y, c); Px(tw, x0 + w - 1, y, c); }
    }

    void Triangle(int tw, int x0, int y0, int s, Color32 c, bool thick)
    {
        if (s <= 1) { Px(tw, x0, y0, c); return; }
        int apexX = x0 + s / 2, apexY = y0 + s - 1;
        Line(tw, int.MaxValue, x0, y0, x0 + s - 1, y0, c);
        Line(tw, int.MaxValue, x0, y0, apexX, apexY, c);
        Line(tw, int.MaxValue, x0 + s - 1, y0, apexX, apexY, c);
        if (thick && s > 6) Triangle(tw, x0 + 2, y0 + 1, s - 4, c, false);
    }

    void Diamond(int tw, int x0, int y0, int s, Color32 c)
    {
        if (s <= 1) { Px(tw, x0, y0, c); return; }
        int cx = x0 + s / 2, cy = y0 + s / 2, r = s - 1;
        Line(tw, int.MaxValue, cx, y0, x0 + r, cy, c);
        Line(tw, int.MaxValue, x0 + r, cy, cx, y0 + r, c);
        Line(tw, int.MaxValue, cx, y0 + r, x0, cy, c);
        Line(tw, int.MaxValue, x0, cy, cx, y0, c);
    }

    void Line(int tw, int th, int x0, int y0, int x1, int y1, Color32 c)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy, guard = 0;
        while (guard++ < 20000)
        {
            if (y0 >= 0 && y0 < th) Px(tw, x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    // ================= 顶栏 / 工具栏 =================

    void RefreshTop()
    {
        if (_selText == null) return;
        string sel;
        if (_mode != Mode.Edit) sel = _mode == Mode.Review ? L("bp_top_review") : L("bp_top_view");
        else if (_tool == Tool.Delete) sel = "<color=#FF6B6B>" + L("bp_top_delete") + "</color>";
        else if (_tool == Tool.Wire) sel = "<color=#FFC04D>" + L("bp_top_wire") + "</color>";
        else if (_sel == null) sel = "<alpha=#80>" + L("bp_top_pick") + "<alpha=#FF>";
        else
        {
            Vector2Int size = _sel.FootprintSize(GridManager.Instance != null ? GridManager.Instance.cellSize : 1f);
            sel = L("bp_top_selected") + "<color=#B8FFC7>" + _sel.LocalizedName + "</color>  <alpha=#80>" + size.x + "×" + size.y + "<alpha=#FF>";
        }
        _selText.text = sel;
        if (_bp != null)
            _infoText.text = (string.IsNullOrEmpty(_bp.name) ? L("bp_untitled") : _bp.name) +
                             "  <alpha=#80>" + _bp.FileName + "  ·  SIGN: " + _bp.authorName + "  ·  " + BlueprintSystem.StatusText(_bp.status) + "<alpha=#FF>";
        _popup.SetTag(_mode == Mode.Review ? "blueprint : review" : _mode == Mode.View ? "blueprint : view" : "blueprint : edit");
    }

    void RefreshMaterials()
    {
        if (_matText == null || _bp == null) return;
        _matText.text = LF("bp_items", _bp.items.Count) + "   " + BlueprintSystem.CostLine(_bp);
    }

    void RefreshTools()
    {
        if (_btnUndo == null || _btnCopy == null) return;
        bool edit = _mode == Mode.Edit, review = _mode == Mode.Review, view = _mode == Mode.View;
        bool pending = _bp != null && _bp.status == BlueprintStatus.Pending;
        _btnUndo.Show(edit);
        _btnDelete.Show(edit);
        _btnSave.Show(edit);
        _btnTest.Show(true);
        _btnApprove.Show(review && pending);
        _btnReject.Show(review && pending);
        _btnModify.Show(review);
        _btnCopy.Show(view);
        _btnDelete.SetOn(_tool == Tool.Delete);
        // 从左往右排（看得见的）
        float x = Pad;
        float y = windowSize.y - _conH - 10f - ToolH - 8f;
        foreach (ToolBtn b in _tools)
        {
            if (!b.rt.gameObject.activeSelf) continue;
            Place(b.rt, x, y, b.width, ToolH);
            x += b.width + 8f;
        }
        if (_matText != null) Place((RectTransform)_matText.transform, x + 10f, y, windowSize.x - x - Pad - 10f, ToolH);
    }

    // 按钮上的字（中日文 + 淡淡的英文）：换语言时重排。
    void Relabel()
    {
        _buildLabel.text = "[≡] " + L("bp_btn_build") + "  <alpha=#99>BUILD";
        float bw = Mathf.Ceil(_buildLabel.GetPreferredValues(_buildLabel.text).x) + 22f;
        Place(_buildBtnRt, Pad, 30f, bw, 24f);
        float x = Pad + bw + 12f;
        Place(_selText.rectTransform, x, 30f, 440f, 24f);
        Place(_infoText.rectTransform, x + 450f, 30f, Mathf.Max(100f, windowSize.x - Pad - 56f - (x + 450f)), 24f);
        foreach (ToolBtn t in _tools)
        {
            t.label.text = L(t.key) + " <alpha=#80>" + t.en;
            t.width = Mathf.Ceil(t.label.GetPreferredValues(t.label.text).x) + 22f;
        }
        RefreshTools();
    }

    void OnBlueprintsChanged()
    {
        if (!IsOpen) return;
        RefreshTop();
        RefreshTools();
        RefreshMaterials();
        _okDirty = true;
        _itemsDirty = true;
        if (_menu != null && _menu.popup.IsOpen) RenderMenu(false);
    }

    void OnLanguageChanged()
    {
        _shortNames.Clear();
        Relabel();
        if (!IsOpen) return;
        RefreshTop();
        RefreshMaterials();
        _itemsDirty = true;
        if (_menu != null && _menu.popup.IsOpen) RenderMenu(false);
    }

    // ================= 建筑菜单（另一个浮窗，终端机风格）=================

    class BuildMenu
    {
        public UiPopup popup;
        public TerminalConsole con;
        public readonly HashSet<BuildCategory> open = new HashSet<BuildCategory>();
        public readonly Dictionary<int, Action> acts = new Dictionary<int, Action>();
        public readonly List<TerminalConsole.Spec> specs = new List<TerminalConsole.Spec>();
        public bool savedOpen = true;
    }

    const float MenuW = 520f;

    void ToggleMenu()
    {
        if (_boot >= 0f || _closeAnim >= 0f) return;
        if (_menu != null && _menu.popup.IsOpen) { CloseMenu(); return; }
        if (_menu == null)
        {
            _menu = new BuildMenu();
            _menu.popup = UiPopup.Create("BlueprintBuildMenu", 402);
            _menu.popup.transform.SetParent(transform, false);
            _menu.popup.Interactable = true;
            _menu.popup.KeepTether = false;
            _menu.popup.SetTag("build : menu");
            _menu.con = new TerminalConsole(_menu.popup.Body, 8f, 28f, MenuW - 16f, 400f);
            _menu.con.OnPick += k => { if (_menu.acts.TryGetValue(k, out Action a)) a(); };
            _menu.open.Add(_sel != null ? _sel.buildCategory : BuildCategory.Basic);
        }
        RenderMenu(true);
    }

    void CloseMenu()
    {
        if (_menu != null && _menu.popup.IsOpen) _menu.popup.Close();
    }

    void RenderMenu(bool open)
    {
        BuildMenu m = _menu;
        m.specs.Clear();
        m.acts.Clear();
        int key = 0;
        BuildCategory[] order = { BuildCategory.Basic, BuildCategory.Defense, BuildCategory.Production, BuildCategory.Power, BuildCategory.Living };
        string[] en = { "BASIC", "DEFENSE", "PRODUCTION", "POWER", "LIVING" };
        BuildManager bm = BuildManager.Instance;
        bool edit = _mode == Mode.Edit;
        float cell = GridManager.Instance != null ? GridManager.Instance.cellSize : 1f;
        for (int ci = 0; ci < order.Length; ci++)
        {
            BuildCategory cat = order[ci];
            List<BuildableData> list = new List<BuildableData>();
            if (bm != null && bm.buildables != null) foreach (BuildableData d in bm.buildables) if (d != null && d.buildCategory == cat) list.Add(d);
            if (list.Count == 0 && cat != BuildCategory.Power) continue;
            bool isOpen = m.open.Contains(cat);
            BuildCategory cc = cat;
            MenuLine(ref key, " [" + (isOpen ? "-" : "+") + "] " + L("build_cat_" + cat.ToString().ToLowerInvariant()) + "<pos=44%><alpha=#80>" + en[ci] + "<alpha=#FF>",
                     Color.white, () => { if (!m.open.Remove(cc)) m.open.Add(cc); RenderMenu(false); });
            if (!isOpen) continue;
            foreach (BuildableData d in list)
            {
                bool unlocked = ProgressionRules.IsBuildableUnlocked(d, out _);
                Vector2Int size = d.FootprintSize(cell);
                string mark = _sel == d && _tool == Tool.Place ? "> " : "  ";
                string text = "     " + mark + d.LocalizedName + "<pos=44%><alpha=#80>" + size.x + "×" + size.y + "<pos=56%>" + ShortCost(NpcIntentBrain.CostOf(d)) + "<alpha=#FF>" +
                              (unlocked ? "" : "  <color=#FF6B6B>[" + ProgressionRules.ShortLockReason(d) + "]</color>");
                BuildableData dd = d;
                MenuLine(ref key, text, unlocked ? Green : DimC, () =>
                {
                    if (!edit) { Print("> " + L("bp_con_readonly"), WarnC); FlushLog(Flush.Type); return; }
                    if (!ProgressionRules.IsBuildableUnlocked(dd, out string why)) { _con.Feedback("ERROR: " + why, BadC, 3f); return; }
                    Select(dd);
                    CloseMenu();
                });
            }
            if (cat == BuildCategory.Power)
                MenuLine(ref key, "     " + (_tool == Tool.Wire ? "> " : "  ") + L("build_wire") + "<pos=44%><alpha=#80>WIRE<alpha=#FF>", AmberC, () =>
                {
                    SetTool(_tool == Tool.Wire ? Tool.Place : Tool.Wire);
                    CloseMenu();
                });
        }
        // 已经保存的蓝图（签名：AI = 自己画的，其他 = NPC 交来的）
        m.specs.Add(new TerminalConsole.Spec { rule = true });
        MenuLine(ref key, " [" + (m.savedOpen ? "-" : "+") + "] " + L("bp_menu_saved") + "<pos=44%><alpha=#80>SAVED PLANS<alpha=#FF>", Color.white,
                 () => { m.savedOpen = !m.savedOpen; RenderMenu(false); });
        if (m.savedOpen)
        {
            IReadOnlyList<Blueprint> all = BlueprintSystem.All;
            int shown = 0;
            for (int i = all.Count - 1; i >= 0 && shown < 12; i--)
            {
                Blueprint bp = all[i];
                string id = bp.id;
                string col = bp.status == BlueprintStatus.Pending ? "#FFE36A" : bp.status == BlueprintStatus.Rejected ? "#FF6B6B" : "#9DB3A4";
                string text = "     " + (bp == _bp ? "> " : "  ") + bp.id + "  " + bp.name + "<pos=58%><alpha=#99>— " + bp.authorName + "<alpha=#FF><pos=80%><color=" + col + ">" + BlueprintSystem.StatusText(bp.status) + "</color>";
                MenuLine(ref key, text, bp.ByAI ? Green : PlanC, () => { CloseMenu(); OpenFile(id); });
                shown++;
            }
            if (all.Count == 0) m.specs.Add(new TerminalConsole.Spec { text = "       " + L("bp_menu_none"), color = DimC });
            MenuLine(ref key, "     + " + L("bp_menu_new") + "<pos=58%><alpha=#80>NEW<alpha=#FF>", Green, () =>
            {
                CloseMenu();
                _draft = BlueprintSystem.NewDraft();
                Show(_draft, Mode.Edit);
            });
        }

        float conH = 88f + m.specs.Count * TerminalConsole.LineH + 4f;
        float h = Mathf.Min(_popup.CanvasSize.y - 40f, conH + 36f);
        Vector2 winSize = new Vector2(MenuW, h);
        if (open)
        {
            Vector2 tl = _popup.Window.anchoredPosition + new Vector2(Pad, -MapTop + 2f);
            m.popup.OpenCanvas(tl, tl, winSize, false);   // 先激活再量字宽（分隔线、提示符）
            m.con.CrtOn();
        }
        else m.popup.Resize(winSize);
        m.con.SetRect(8f, 28f, MenuW - 16f, h - 36f);
        m.con.SetStatus("BUILD MENU", _bp != null ? _bp.FileName : "");
        m.con.SetPrompt("~/blueprint/build");
        m.con.Echo("");
        m.con.SetPage(m.specs, open);
    }

    void MenuLine(ref int key, string text, Color color, Action act)
    {
        int k = key++;
        _menu.acts[k] = act;
        _menu.specs.Add(new TerminalConsole.Spec { text = text, color = color, key = k });
    }

    // ================= 搭界面 =================

    void BuildUI()
    {
        _popup = UiPopup.Create("BlueprintDesignerPopup", 400);
        _popup.transform.SetParent(transform, false);
        _popup.Interactable = true;
        _popup.KeepTether = false;
        _popup.SetTag("blueprint : edit");
        RectTransform body = _popup.Body;
        body.sizeDelta = windowSize;

        // 顶栏：左上角 [≡ 建筑]（建筑菜单）、当前选择；右边蓝图信息、[x]
        Button build = TagButton(body, "", out _buildLabel);
        _buildBtnRt = (RectTransform)build.transform;
        build.onClick.AddListener(ToggleMenu);
        _selText = NewText(body, 15f, UiStyle.TextMain, TextAlignmentOptions.Left);
        _selText.textWrappingMode = TextWrappingModes.NoWrap;
        _infoText = NewText(body, 14f, Green, TextAlignmentOptions.Right);
        _infoText.textWrappingMode = TextWrappingModes.NoWrap;
        _infoText.overflowMode = TextOverflowModes.Ellipsis;
        Button close = TagButton(body, "[x]", out _);
        Place((RectTransform)close.transform, windowSize.x - 58f, 5f, 44f, UiStyle.TagH);
        close.onClick.AddListener(Close);

        // 地图区
        _viewW = windowSize.x - Pad * 2f;
        _viewRt = NewRect("Map", body);
        Image viewBg = _viewRt.gameObject.AddComponent<Image>();
        viewBg.color = new Color(0.02f, 0.05f, 0.035f, 1f);
        _viewRt.gameObject.AddComponent<RectMask2D>();
        _mapImg = NewRaw(_viewRt, "Schematic");
        _testImg = NewRaw(_viewRt, "TestScan");
        _testImg.gameObject.SetActive(false);
        _ov = NewRect("Overlay", _viewRt);
        Fill(_ov);
        _ov.pivot = Vector2.zero;
        _itemsRoot = Layer("Items");
        _ghostRoot = Layer("Ghosts");
        _npcRoot = Layer("Npcs");
        _fxRoot = Layer("Fx");
        _hover = new Corners(_fxRoot, "Hover");
        _drag = new Corners(_fxRoot, "Drag");
        _wireSel = new Corners(_fxRoot, "WireSel");
        _flashC = new Corners(_fxRoot, "Flash");
        _dragTag = NewText(_fxRoot, 13f, Green, TextAlignmentOptions.BottomLeft);
        RectTransform dt = _dragTag.rectTransform;
        dt.anchorMin = dt.anchorMax = dt.pivot = Vector2.zero;
        dt.sizeDelta = new Vector2(160f, 18f);
        _dragTag.textWrappingMode = TextWrappingModes.NoWrap;
        _dragTag.gameObject.SetActive(false);
        UiStyle.Frame(_viewRt, new Color(Green.r, Green.g, Green.b, 0.35f), 1f);
        // 开机时从上往下扫出来的遮罩 + 扫描线
        _cover = NewRect("Cover", _viewRt);
        Image cov = _cover.gameObject.AddComponent<Image>();
        cov.color = new Color(0.01f, 0.025f, 0.018f, 1f);
        cov.raycastTarget = false;
        _scanLine = NewRect("ScanLine", _viewRt);
        Image sl = _scanLine.gameObject.AddComponent<Image>();
        sl.color = new Color(0.8f, 1f, 0.88f, 0.9f);
        sl.raycastTarget = false;
        _cover.gameObject.SetActive(false);
        _scanLine.gameObject.SetActive(false);

        // 工具栏
        _btnUndo = MakeTool(body, "bp_tool_undo", "UNDO", Undo);
        _btnDelete = MakeTool(body, "bp_tool_delete", "DELETE", () => SetTool(_tool == Tool.Delete ? Tool.Place : Tool.Delete));
        _btnSave = MakeTool(body, "bp_tool_save", "SAVE", ClickSave);
        _btnTest = MakeTool(body, "bp_tool_test", "TEST", ClickTest);
        _btnApprove = MakeTool(body, "bp_tool_approve", "APPROVE", DoApprove);
        _btnReject = MakeTool(body, "bp_tool_reject", "REJECT", DoReject);
        _btnModify = MakeTool(body, "bp_tool_modify", "MODIFY", DoModify);
        _btnCopy = MakeTool(body, "bp_tool_copy", "EDIT COPY", DoCopy);
        _matText = NewText(body, 14f, UiStyle.TextSub, TextAlignmentOptions.Right);
        _matText.textWrappingMode = TextWrappingModes.NoWrap;
        _matText.overflowMode = TextOverflowModes.Ellipsis;

        // 命令行
        _conRect = NewRect("ConsoleHit", body);
        _con = new TerminalConsole(body, Pad, windowSize.y - ConH - 10f, windowSize.x - Pad * 2f, ConH);
        _con.OnPick += PickKey;
        _con.SetPrompt("~/blueprint");
        Relabel();
        Layout();
    }

    void DoCopy()
    {
        if (_bp == null) return;
        Blueprint copy = BlueprintSystem.Modify(_bp);
        if (copy == null) return;
        Show(copy, Mode.Edit);
        Print("> " + LF("bp_con_modify", copy.authorName), PlanC);
        FlushLog(Flush.Type);
    }

    RectTransform Layer(string name)
    {
        RectTransform r = NewRect(name, _ov);
        Fill(r);
        r.pivot = Vector2.zero;
        return r;
    }

    static RawImage NewRaw(RectTransform parent, string name)
    {
        RectTransform r = NewRect(name, parent);
        r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
        RawImage img = r.gameObject.AddComponent<RawImage>();
        img.raycastTarget = false;
        return img;
    }

    ToolBtn MakeTool(RectTransform parent, string key, string en, Action onClick)
    {
        ToolBtn t = new ToolBtn { key = key, en = en, width = 90f };
        Button b = TagButton(parent, en, out TextMeshProUGUI label);
        t.rt = (RectTransform)b.transform;
        t.img = b.GetComponent<Image>();
        t.label = label;
        b.onClick.AddListener(() => onClick());
        UiHoverZone hz = t.rt.gameObject.AddComponent<UiHoverZone>();
        hz.onEnter = () => { t.hover = true; t.Refresh(); };
        hz.onExit = () => { t.hover = false; t.Refresh(); };
        t.Refresh();
        _tools.Add(t);
        return t;
    }

    // 淡绿标签样式的按钮（和终端机的 [x] 一样）。
    static Button TagButton(Transform parent, string text, out TextMeshProUGUI label)
    {
        GameObject go = new GameObject("Btn_" + text, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = Green;
        UiStyle.Frame((RectTransform)go.transform, new Color(Green.r, Green.g, Green.b, 0.85f), 1f);
        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        label = NewText(go.transform, UiStyle.TagFont + 1f, UiStyle.Ink, TextAlignmentOptions.Center);
        label.text = text;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Fill(label.rectTransform);
        return b;
    }

    // ================= 小工具 =================

    // 菜单里的短材料表：「木材×3 石头×2」（库存在下面工具栏）。
    static string ShortCost(MaterialCost[] cost)
    {
        if (cost == null || cost.Length == 0) return L("cost_none");
        StringBuilder sb = new StringBuilder();
        foreach (MaterialCost c in cost)
        {
            if (c == null || c.amount <= 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(ProgressionRules.ItemName(c.materialName)).Append('×').Append(c.amount);
        }
        return sb.Length > 0 ? sb.ToString() : L("cost_none");
    }

    static List<NpcData> AliveNpcs()
    {
        List<NpcData> list = new List<NpcData>();
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w != null) foreach (NpcData n in w.npcs) if (n != null && !n.IsDead && !n.isBroken) list.Add(n);
        return list;
    }

    static string L(string key) => ProgressionRules.L(key);
    static string LF(string key, params object[] a) => ProgressionRules.LF(key, a);

    static TextMeshProUGUI NewText(Transform parent, float size, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = UiStyle.Mono;
        if (f != null) t.font = f;
        t.fontSize = size;
        t.color = color;
        t.richText = true;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // —— 小类 ——

    class UndoStep
    {
        public readonly List<BlueprintItem> added = new List<BlueprintItem>();
        public readonly List<BlueprintItem> removed = new List<BlueprintItem>();
    }

    class ItemView { public RectTransform rt; public Image fill, frame; public TextMeshProUGUI label; }
    class NpcDot { public RectTransform rt; public Image img; public TextMeshProUGUI label; }

    class Marker
    {
        public RectTransform rt;
        public Image box;
        public readonly RectTransform[] bars = new RectTransform[2];
        public readonly Image[] barImgs = new Image[2];
        public Vector2Int cell;
        public float t;
    }

    class ToolBtn
    {
        public RectTransform rt;
        public Image img;
        public TextMeshProUGUI label;
        public string key, en;
        public float width;
        public bool on, hover;
        public void Show(bool v) { if (rt.gameObject.activeSelf != v) rt.gameObject.SetActive(v); }
        public void SetOn(bool v) { on = v; Refresh(); }
        public void Refresh()
        {
            // 开着（删除模式）= 实心淡绿 + 深色字；平时 = 只有细框，鼠标移上去底色亮一点
            img.color = on ? Green : new Color(Green.r, Green.g, Green.b, hover ? 0.2f : 0.06f);
            label.color = on ? UiStyle.Ink : Green;
        }
    }

    // 四角框：只有四个角的框（两条短线拼成一个角，外面一层淡淡的光）。
    // 角的长度跟着框的大小变（框小角也短，四个角之间总留着空隙），线粗细不变。
    class Corners
    {
        const float Outset = 2f, Thick = 2f, GlowThick = 6f;
        readonly RectTransform _root;
        readonly Image[] _core = new Image[8];
        readonly Image[] _glow = new Image[8];
        bool _visible = true;

        public Corners(Transform parent, string name)
        {
            _root = NewRect(name, parent);
            _root.anchorMin = _root.anchorMax = _root.pivot = Vector2.zero;
            _root.sizeDelta = Vector2.zero;
            for (int i = 0; i < 8; i++) _glow[i] = Bar(name + "Glow" + i);
            for (int i = 0; i < 8; i++) _core[i] = Bar(name + "Line" + i);
            Show(false);
        }

        Image Bar(string n)
        {
            RectTransform r = NewRect(n, _root);
            r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
            Image img = r.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        public void Show(bool on)
        {
            if (_visible == on) return;
            _visible = on;
            _root.gameObject.SetActive(on);
        }

        public void Set(Rect r, Color col)
        {
            Show(true);
            r = Rect.MinMaxRect(r.xMin - Outset, r.yMin - Outset, r.xMax + Outset, r.yMax + Outset);
            float arm = Mathf.Clamp(Mathf.Min(r.width, r.height) * 0.28f, 3f, 16f);
            for (int c = 0; c < 4; c++)
            {
                bool right = c == 1 || c == 3, top = c < 2;
                float x = right ? r.xMax : r.xMin, y = top ? r.yMax : r.yMin;
                SetBar(c * 2, right ? x - arm : x, top ? y - Thick : y, arm, Thick, col);          // 横
                SetBar(c * 2 + 1, right ? x - Thick : x, top ? y - arm : y, Thick, arm, col);      // 竖
            }
        }

        void SetBar(int i, float x, float y, float w, float h, Color col)
        {
            RectTransform cr = _core[i].rectTransform;
            cr.anchoredPosition = new Vector2(x, y);
            cr.sizeDelta = new Vector2(w, h);
            _core[i].color = col;
            float g = (GlowThick - Thick) * 0.5f;
            RectTransform gr = _glow[i].rectTransform;
            gr.anchoredPosition = new Vector2(x - g, y - g);
            gr.sizeDelta = new Vector2(w + g * 2f, h + g * 2f);
            _glow[i].color = new Color(col.r, col.g, col.b, col.a * 0.2f);
        }
    }
}
