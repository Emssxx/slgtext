using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;

// ============================================================================
// 规划区建造（参考《Farthest Frontier》）：
//   1. 建造模式里选好建筑、点格子放下 → 只是一块「规划区」（蓝色半透明蓝图），不会自动开工。
//      建造模式里右键蓝图 = 取消规划。
//   2. 退出建造模式后，点一下蓝图 → 弹出「施工指派」面板，选一名或多名 NPC 去施工（也可以一键指派所有空闲的人）。
//   3. 被指派的人走到蓝图旁边各自站一个位置一起干：每人每秒推进 builderSpeed × 工作速度（疲惫时 50%），
//      同时每秒消耗 1 点体力（NpcHealth）。受伤 / 致命伤 / 濒死干不了活；要吃饭、逃命、体力耗尽或被玩家指令叫走时先放下，
//      之后自己回来接着干。进度到 100% 就建成。
//   施工工作量 = BuildableData.buildTime × workPerBuildSecond（建造速度算法以后再细调，数值都在 ConstructionManager 上）。
// ============================================================================

// 挂在蓝图（预览物体）上：一块还没建好的规划区。
public class ConstructionSite : MonoBehaviour
{
    public static readonly List<ConstructionSite> All = new List<ConstructionSite>();

    public BuildableData data;
    public Vector2Int cell;                     // 中心那一格（显示位置用）
    public Vector2Int origin;                   // 占地左下角那一格
    public Vector2Int size = Vector2Int.one;    // 占地大小（小格）
    public float workRequired = 1f;   // 需要的工作量（人·秒）
    public float progress;            // 已完成的工作量
    public readonly List<string> builders = new List<string>(); // 指派的施工者（npc id）

    // —— 来自蓝图（BlueprintSystem.Send）的规划区 ——
    public string blueprintId;        // 哪份蓝图（空 = 以前直接放下的规划区）
    public string foremanId;          // 指挥建造的人
    public MaterialCost[] cost;       // 开工前要从仓库取的材料
    public bool materialsIn = true;   // 材料到位了没有（蓝图的工地放下时是 false：搬运的人送来 / 开工时从仓库取）

    SpriteRenderer[] _renderers;
    Color[] _colors;
    Transform _bar, _fill;
    float _barWidth = 0.8f;
    static Sprite _white;

    public float Ratio => workRequired > 0f ? Mathf.Clamp01(progress / workRequired) : 1f;

    public string DisplayName
    {
        get
        {
            string name = data != null ? data.LocalizedName : gameObject.name;
            string label = GridManager.Instance != null ? GridManager.Instance.CellToLabel(cell.x, cell.y) : "";
            return name + (label.Length > 0 ? "（" + label + "）" : "");
        }
    }

    public bool Contains(Vector2Int c)
    {
        return c.x >= origin.x && c.x < origin.x + size.x && c.y >= origin.y && c.y < origin.y + size.y;
    }

    // 点到占地里的任何一格都算点到这块规划区。
    public static ConstructionSite At(Vector2Int c)
    {
        foreach (ConstructionSite s in All) if (s != null && s.Contains(c)) return s;
        return null;
    }

    // 占地的半宽 / 半高（世界单位）。
    public Vector2 HalfExtents
    {
        get
        {
            float cs = GridManager.Instance != null ? GridManager.Instance.cellSize : 1f;
            return new Vector2(size.x * cs * 0.5f, size.y * cs * 0.5f);
        }
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public void Setup(BuildableData d, Vector2Int o, Vector2Int s, float work)
    {
        data = d;
        origin = o;
        size = new Vector2Int(Mathf.Max(1, s.x), Mathf.Max(1, s.y));
        cell = new Vector2Int(o.x + (size.x - 1) / 2, o.y + (size.y - 1) / 2);
        workRequired = Mathf.Max(0.1f, work);
        _renderers = GetComponentsInChildren<SpriteRenderer>();
        _colors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            _colors[i] = _renderers[i].color;
            if (_renderers[i].transform == transform) _colors[i].a = 1f; // 本体被建造系统调成了半透明，记下原本的不透明颜色
        }
        BuildBar();
        Refresh();
    }

    // 蓝图外观：偏蓝、半透明；进度越高越实。头顶一条进度条（有人在干或干过一些时才显示）。
    void Update() { Refresh(); }

    void Refresh()
    {
        if (_renderers == null) return;
        float a = Mathf.Lerp(0.4f, 0.85f, Ratio);
        Color tint = new Color(0.55f, 0.8f, 1f, a);
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null) continue;
            Color c = _colors[i];
            _renderers[i].color = new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, a);
        }
        if (_bar != null)
        {
            bool show = builders.Count > 0 || progress > 0f;
            _bar.gameObject.SetActive(show);
            if (show && _fill != null) _fill.localScale = new Vector3(_barWidth * Ratio, 0.1f, 1f);
        }
    }

    // 建成：颜色还原、去掉进度条（之后 ConstructionManager 把这个组件删掉）。
    public void RestoreLook()
    {
        if (_renderers != null)
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].color = _colors[i];
        if (_bar != null) Destroy(_bar.gameObject);
    }

    void BuildBar()
    {
        if (_white == null)
        {
            Texture2D t = new Texture2D(1, 1);
            t.SetPixel(0, 0, Color.white);
            t.Apply();
            _white = Sprite.Create(t, new UnityEngine.Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 1f); // 轴心在左边：缩放时从左往右长
        }
        // 进度条：按占地宽度，放在建筑上方
        Vector2 half = HalfExtents;
        _barWidth = Mathf.Clamp(half.x * 2f * 0.8f, 0.4f, 3f);
        GameObject bar = new GameObject("SiteBar");
        bar.transform.SetParent(transform, false);
        bar.transform.localPosition = new Vector3(-_barWidth * 0.5f, half.y + 0.12f, 0f);
        _bar = bar.transform;

        GameObject bg = new GameObject("Bg");
        bg.transform.SetParent(_bar, false);
        bg.transform.localScale = new Vector3(_barWidth, 0.1f, 1f);
        SpriteRenderer bsr = bg.AddComponent<SpriteRenderer>();
        bsr.sprite = _white;
        bsr.color = new Color(0f, 0f, 0f, 0.6f);
        bsr.sortingOrder = 15;
        RenderMats.MakeUnlit(bsr);                                  // 2D 光照：进度条一直看得清

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(_bar, false);
        fill.transform.localScale = new Vector3(0f, 0.1f, 1f);
        SpriteRenderer fsr = fill.AddComponent<SpriteRenderer>();
        fsr.sprite = _white;
        fsr.color = new Color(0.45f, 0.9f, 0.5f, 0.95f);
        fsr.sortingOrder = 16;
        RenderMats.MakeUnlit(fsr);
        _fill = fill.transform;
    }
}

// 管所有规划区：指派施工者、推进进度、点击蓝图弹出指派面板。由 WorldManager.Start 调 Ensure() 创建。
public class ConstructionManager : MonoBehaviour
{
    public static ConstructionManager Instance { get; private set; }

    [Header("施工速度（以后再细调）")]
    [Tooltip("需要的工作量 = BuildableData.buildTime × 这个值（人·秒）")]
    public float workPerBuildSecond = 1f;
    [Tooltip("每名施工者每秒推进多少工作量（再乘上工作速度：疲惫 50%）")]
    public float builderSpeed = 1f;
    [Tooltip("体力低到这个值就先放下活去休息")]
    public float pauseEnergy = 1f;
    [Tooltip("体力恢复到这个值才回来接着干")]
    public float resumeEnergy = 30f;
    [Tooltip("蓝图的工地材料不够时，施工的人最多等几秒（之后先去干别的）")]
    public float materialWait = 8f;

    class Builder
    {
        public ConstructionSite site;
        public bool paused;
        public bool pausedForEnergy;
        public bool claimed;
        public string reason;
        public float moveCmdTimer;
        public int moveTries;      // 走不到自己的位置时重试了几次（多次走不到就在附近干）
        public float waitMat;      // 到了工地但材料不够，已经等了多久
    }

    readonly Dictionary<string, Builder> _builders = new Dictionary<string, Builder>();
    // 施工位置的方向（乘上占地半宽 / 半高 + 一点空隙）：左右下上，再四个角
    static readonly Vector2[] Spots =
    {
        new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(0f, 1f),
        new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f)
    };
    const float SpotGap = 0.3f; // 站在占地外面多远

    // —— 指派面板 ——
    GameObject _panel;
    RectTransform _panelRt, _rowsRoot;
    TextMeshProUGUI _title, _status;
    ConstructionSite _panelSite;
    readonly List<Row> _rows = new List<Row>();
    float _panelRefresh;
    TMP_FontAsset _font;
    Camera _cam;

    class Row
    {
        public string npcId;
        public GameObject root;
        public TextMeshProUGUI name, state, btnLabel;
        public Button button;
        public Image btnImage;
    }

    public static ConstructionManager Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("ConstructionManager").AddComponent<ConstructionManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ============ 规划区的创建 / 取消 ============

    public ConstructionSite CreateSite(BuildableData data, Vector2Int origin, Vector2Int size, GameObject preview)
    {
        if (data == null) return null;
        if (preview == null)
        {
            preview = new GameObject(data.id);
            preview.transform.position = GridManager.Instance != null ? GridManager.Instance.AreaCenter(origin, size) : Vector3.zero;
            SpriteRenderer sr = preview.AddComponent<SpriteRenderer>();
            sr.sprite = data.sprite;
        }
        if (preview.GetComponent<BuildingFootprint>() == null) preview.AddComponent<BuildingFootprint>().Set(origin, size);
        ConstructionSite site = preview.AddComponent<ConstructionSite>();
        site.Setup(data, origin, size, data.buildTime * workPerBuildSecond);
        return site;
    }

    // 取消规划：拆掉蓝图、空出格子、施工者散伙。
    public void Cancel(ConstructionSite site)
    {
        if (site == null) return;
        foreach (string id in new List<string>(site.builders)) Unassign(id);
        GridManager g = GridManager.Instance;
        if (g != null) g.ClearArea(site.origin, site.size);
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w != null && site.data != null && site.data.uniqueBuild) w.placedUniqueBuilds.Remove(site.data.id); // 唯一建筑（终端机）可以重新放
        Say(LanguageGuard.Pick("取消了规划：", "計画を取り消した：") + site.DisplayName);
        if (_panelSite == site) ClosePanel();
        Destroy(site.gameObject);
    }

    // ============ 指派 ============

    public void Assign(string npcId, ConstructionSite site)
    {
        if (string.IsNullOrEmpty(npcId) || site == null) return;
        Unassign(npcId); // 一个人同一时间只干一处
        site.builders.Add(npcId);
        _builders[npcId] = new Builder { site = site };
        NpcData n = Npc(npcId);
        if (n != null) Say(LanguageGuard.Pick(n.npcName + " 被指派去施工：", n.npcName + "を建設に割り当てた：") + site.DisplayName);
    }

    public void Unassign(string npcId)
    {
        if (!_builders.TryGetValue(npcId, out Builder b)) return;
        if (b.site != null) b.site.builders.Remove(npcId);
        Release(npcId, b);
        _builders.Remove(npcId);
    }

    public ConstructionSite SiteOf(string npcId) => _builders.TryGetValue(npcId, out Builder b) ? b.site : null;

    // 一键指派：所有能干活、手上没事（没被指派、没在执行玩家指令）的人。
    public int AssignIdle(ConstructionSite site)
    {
        int count = 0;
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w == null || site == null) return 0;
        foreach (NpcData n in w.npcs)
        {
            if (!NpcHealth.CanWork(n) || _builders.ContainsKey(n.id)) continue;
            NpcMover m = NpcView.GetMover(n.id);
            NpcTaskRunner r = m != null ? m.GetComponent<NpcTaskRunner>() : null;
            if (m == null || (r != null && r.IsBusy)) continue;
            Assign(n.id, site);
            count++;
        }
        return count;
    }

    // ============ 每帧：推进施工 + 点击蓝图 ============

    void Update()
    {
        HandleClick();
        TickBuilders(Time.deltaTime);
        if (_panel != null && _panel.activeSelf)
        {
            if (_panelSite == null) { ClosePanel(); return; }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { ClosePanel(); return; }
            _panelRefresh -= Time.unscaledDeltaTime;
            if (_panelRefresh <= 0f) { _panelRefresh = 0.3f; RefreshPanel(); }
        }
    }

    void TickBuilders(float dt)
    {
        if (_builders.Count == 0) return;
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w == null) return;
        List<ConstructionSite> done = null;
        foreach (KeyValuePair<string, Builder> kv in new List<KeyValuePair<string, Builder>>(_builders))
        {
            string id = kv.Key;
            Builder b = kv.Value;
            NpcData n = Npc(id);
            if (b.site == null || n == null || n.IsDead || n.isBroken) { Unassign(id); continue; }
            NpcMover m = NpcView.GetMover(id);
            if (m == null) continue;
            NpcTaskRunner r = m.GetComponent<NpcTaskRunner>();

            // 能不能干：受伤、被玩家指令叫走、要吃饭 / 逃命、体力耗尽 → 先放下
            string reason = null;
            bool jp = LanguageGuard.IsJp;
            if (!NpcHealth.CanWork(n)) reason = NpcHealth.CannotWorkReason(n, jp);
            else if (NpcCombat.InCombat(id)) reason = NpcCombat.IsFleeing(id) ? (jp ? "敵から逃げている" : "在躲敌人") : (jp ? "敵と戦っている" : "在和敌人打");
            else if (r != null && r.IsBusy) reason = jp ? "指示を実行中" : "在执行指令";
            else
            {
                NpcDecisionRules.Behavior need = dt > 0f ? NpcDecisionRules.Decide(n, w) : NpcDecisionRules.Behavior.Idle;
                if (need == NpcDecisionRules.Behavior.Flee) reason = jp ? "逃げている" : "在逃命";
                else if (need == NpcDecisionRules.Behavior.Eat) reason = jp ? "食事に行った" : "去吃东西了";
                else if (n.energy <= pauseEnergy || (b.pausedForEnergy && n.energy < resumeEnergy)) { reason = jp ? "疲れ切って休憩中" : "累垮了，在休息"; b.pausedForEnergy = true; }
            }
            if (reason != null)
            {
                b.paused = true;
                b.reason = reason;
                Release(id, b);
                continue;
            }
            b.paused = false;
            b.pausedForEnergy = false;
            b.reason = null;
            if (!b.claimed) { NpcBusy.Claim(id, NpcBusy.Build); b.claimed = true; }

            // 走到自己的位置
            int idx = Mathf.Max(0, b.site.builders.IndexOf(id)) % Spots.Length;
            Vector3 center = b.site.transform.position;
            Vector2 half = b.site.HalfExtents;
            Vector3 spot = center + new Vector3(Spots[idx].x * (half.x + SpotGap), Spots[idx].y * (half.y + SpotGap), 0f);
            float dist = Vector2.Distance(m.transform.position, spot);
            float near = Mathf.Max(half.x, half.y) + 1.2f;
            bool arrived = dist <= 0.3f || (!m.IsMoving && dist <= 0.9f)
                           || (!m.IsMoving && b.moveTries >= 3 && Vector2.Distance(m.transform.position, center) <= near); // 位置被挡住：在附近也能干
            if (!arrived)
            {
                b.moveCmdTimer -= dt;
                if (!m.IsMoving || b.moveCmdTimer <= 0f) { m.MoveTo(spot); b.moveCmdTimer = 2f; b.moveTries++; }
                n.currentAction = (jp ? "建設に向かう：" : "前往施工：") + b.site.DisplayName;
                continue;
            }
            if (m.IsMoving) m.Stop();
            m.FaceTowards(center);

            // 蓝图的工地：材料还没送到 → 开工前从仓库取；仓库不够就等一会儿，等太久先去干别的（采集的人会去采）
            if (!b.site.materialsIn)
            {
                if (ProgressionRules.ConsumeMaterials(b.site.cost))
                {
                    b.site.materialsIn = true;
                    b.waitMat = 0f;
                }
                else
                {
                    b.waitMat += dt;
                    b.reason = jp ? "素材待ち" : "等材料";
                    n.currentAction = (jp ? "素材待ち：" : "等材料：") + b.site.DisplayName;
                    if (b.waitMat > materialWait) Unassign(id);
                    continue;
                }
            }
            b.reason = null;

            // 干活
            if (dt > 0f)
            {
                b.site.progress += dt * builderSpeed * NpcHealth.WorkSpeed(n);
                NpcHealth.ConsumeBuild(n, dt);
            }
            n.currentAction = (jp ? "建設中：" : "施工中：") + b.site.DisplayName;
            if (b.site.progress >= b.site.workRequired)
            {
                if (done == null) done = new List<ConstructionSite>();
                if (!done.Contains(b.site)) done.Add(b.site);
            }
        }
        if (done != null) foreach (ConstructionSite s in done) Complete(s);
    }

    // 一座建筑建成了（建筑名、施工的人）。左上角的建筑完成提示（HudToasts）用。
    public static event System.Action<string, List<string>> OnSiteBuilt;
    // 一块规划区建成了（组件删掉之前调，能读到它来自哪份蓝图）。蓝图完工的统计用。
    public static event System.Action<ConstructionSite> OnSiteCompleted;

    void Complete(ConstructionSite site)
    {
        if (site == null) return;
        List<string> crew = new List<string>(site.builders); // 关系结算用（下面会逐个解除指派）
        bool jp = LanguageGuard.IsJp;
        List<string> names = new List<string>();
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        foreach (string id in new List<string>(site.builders))
        {
            NpcData n = Npc(id);
            if (n != null)
            {
                names.Add(n.npcName);
                n.currentAction = jp ? "建設を終えた" : "刚完成施工";
                if (w != null) n.memories.Add(new MemoryEntry { day = w.day, content = (jp ? "建設を終えた：" : "完成了施工：") + site.DisplayName });
            }
            Unassign(id);
        }
        GameObject go = site.gameObject;
        string siteName = site.DisplayName;
        bool blocks = site.data != null && site.data.blocksMovement;
        try { OnSiteCompleted?.Invoke(site); }
        catch (System.Exception e) { Debug.LogException(e); }
        site.RestoreLook();
        Destroy(site);
        if (_panelSite == site) ClosePanel();
        if (BuildManager.Instance != null) BuildManager.Instance.FinishConstruction(go, blocks);
        else go.SendMessage("OnBuildComplete", SendMessageOptions.DontRequireReceiver);
        Say((jp ? "建設完了：" : "建成：") + siteName + (names.Count > 0 ? (jp ? "（担当：" : "（施工：") + string.Join("、", names) + "）" : ""));
        RelationshipManager.OnBuilt(crew, siteName); // 一起干活的人更亲近；其他人对施工的人多一点尊重
        OnSiteBuilt?.Invoke(siteName, names);
    }

    void Release(string id, Builder b)
    {
        if (!b.claimed) return;
        NpcBusy.Release(id, NpcBusy.Build);
        b.claimed = false;
    }

    // 施工者此刻的情况（面板、悬停信息框用）。
    public string BuilderState(string npcId, bool jp)
    {
        if (!_builders.TryGetValue(npcId, out Builder b)) return null;
        if (b.paused) return (jp ? "中断：" : "暂停：") + b.reason;
        if (!string.IsNullOrEmpty(b.reason)) return b.reason;
        return jp ? "建設中" : "施工中";
    }

    // ============ 点击蓝图 → 指派面板 ============

    void HandleClick()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (BuildManager.Instance != null && BuildManager.Instance.IsBuildMode) return;
        if (ConstructionSite.All.Count == 0 || PointerOverUI()) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null || GridManager.Instance == null) return;
        Vector2 sp = Mouse.current.position.ReadValue();
        Vector3 wp = _cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -_cam.transform.position.z));
        wp.z = 0f;
        Collider2D hit = Physics2D.OverlapPoint(wp);
        if (hit != null && hit.GetComponentInParent<NpcView>() != null) return; // 点到人：交给 NPC 对话窗口
        ConstructionSite site = ConstructionSite.At(GridManager.Instance.WorldToCell(wp));
        if (site != null) OpenPanel(site);
    }

    static bool PointerOverUI()
    {
        if (EventSystem.current == null || Mouse.current == null) return false;
        PointerEventData data = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, results);
        return results.Count > 0;
    }

    public void OpenPanel(ConstructionSite site)
    {
        if (site == null) return;
        if (_panel == null) BuildPanel();
        _panelSite = site;
        _panel.SetActive(true);
        RebuildRows();
        RefreshPanel();
    }

    public void ClosePanel()
    {
        _panelSite = null;
        if (_panel != null) _panel.SetActive(false);
    }

    const float PanelW = 460f, RowH = 40f, HeadH = 92f, FootH = 58f;

    void BuildPanel()
    {
        _font = GameFonts.Find();
        _panel = new GameObject("SiteAssignPanel", typeof(RectTransform));
        Canvas c = _panel.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 60;
        CanvasScaler scaler = _panel.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;
        _panel.AddComponent<GraphicRaycaster>();

        _panelRt = NewRect("Window", _panel.transform);
        _panelRt.anchorMin = _panelRt.anchorMax = _panelRt.pivot = new Vector2(0.5f, 0.5f);
        _panelRt.anchoredPosition = new Vector2(330f, 40f);
        Image border = _panelRt.gameObject.AddComponent<Image>();
        border.color = new Color(0.55f, 0.8f, 1f, 0.55f);
        RectTransform bg = NewRect("Bg", _panelRt);
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = new Vector2(2f, 2f); bg.offsetMax = new Vector2(-2f, -2f);
        bg.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.1f, 0.95f);

        _title = Text(_panelRt, "Title", 22f, Color.white, TextAlignmentOptions.Left);
        _title.fontStyle = FontStyles.Bold;
        Place(_title.rectTransform, 16f, 12f, PanelW - 70f, 30f);
        _status = Text(_panelRt, "Status", 15f, new Color(0.7f, 0.76f, 0.86f), TextAlignmentOptions.Left);
        _status.textWrappingMode = TextWrappingModes.Normal;
        Place(_status.rectTransform, 16f, 44f, PanelW - 32f, 44f);

        Button close = MakeButton(_panelRt, "×", new Color(0f, 0f, 0f, 0f), out _);
        Place((RectTransform)close.transform, PanelW - 48f, 8f, 40f, 36f);
        close.onClick.AddListener(ClosePanel);

        _rowsRoot = NewRect("Rows", _panelRt);
        _rowsRoot.anchorMin = _rowsRoot.anchorMax = _rowsRoot.pivot = new Vector2(0f, 1f);
        _rowsRoot.anchoredPosition = new Vector2(0f, -HeadH);
        _rowsRoot.sizeDelta = new Vector2(PanelW, 0f);
    }

    void RebuildRows()
    {
        foreach (Row r in _rows) if (r.root != null) Destroy(r.root);
        _rows.Clear();
        foreach (Transform t in _panelRt) if (t.name == "Footer") Destroy(t.gameObject);

        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        List<NpcData> list = new List<NpcData>();
        if (w != null) foreach (NpcData n in w.npcs) if (!n.IsDead) list.Add(n);

        for (int i = 0; i < list.Count; i++)
        {
            NpcData n = list[i];
            Row row = new Row { npcId = n.id };
            RectTransform rt = NewRect("Row_" + i, _rowsRoot);
            Place(rt, 12f, i * RowH, PanelW - 24f, RowH - 4f);
            rt.gameObject.AddComponent<Image>().color = new Color(0.14f, 0.16f, 0.22f, 1f);
            row.root = rt.gameObject;
            row.name = Text(rt, "Name", 17f, Color.white, TextAlignmentOptions.Left);
            Place(row.name.rectTransform, 10f, 0f, 110f, RowH - 4f);
            row.state = Text(rt, "State", 14f, new Color(0.7f, 0.76f, 0.86f), TextAlignmentOptions.Left);
            Place(row.state.rectTransform, 122f, 0f, 210f, RowH - 4f);
            row.button = MakeButton(rt, "", new Color(0.2f, 0.36f, 0.6f, 1f), out row.btnLabel);
            row.btnImage = row.button.GetComponent<Image>();
            Place((RectTransform)row.button.transform, PanelW - 24f - 92f, 4f, 84f, RowH - 12f);
            string id = n.id;
            row.button.onClick.AddListener(() => ToggleAssign(id));
            _rows.Add(row);
        }

        float listH = list.Count * RowH;
        RectTransform foot = NewRect("Footer", _panelRt);
        foot.anchorMin = foot.anchorMax = foot.pivot = new Vector2(0f, 1f);
        foot.anchoredPosition = new Vector2(0f, -(HeadH + listH + 6f));
        foot.sizeDelta = new Vector2(PanelW, FootH);
        bool jp = LanguageGuard.IsJp;
        Button auto = MakeButton(foot, jp ? "空いている人を全員割り当て" : "指派所有空闲的人", new Color(0.18f, 0.42f, 0.3f, 1f), out _);
        Place((RectTransform)auto.transform, 12f, 6f, 250f, 40f);
        auto.onClick.AddListener(() =>
        {
            int c = AssignIdle(_panelSite);
            if (c == 0) Say(LanguageGuard.Pick("现在没有空闲、能干活的人。", "今、手の空いている動ける人がいない。"));
            RefreshPanel();
        });
        Button cancel = MakeButton(foot, jp ? "計画を取り消す" : "取消规划", new Color(0.5f, 0.18f, 0.18f, 1f), out _);
        Place((RectTransform)cancel.transform, 272f, 6f, PanelW - 284f, 40f);
        cancel.onClick.AddListener(() => Cancel(_panelSite));

        _panelRt.sizeDelta = new Vector2(PanelW, HeadH + listH + 6f + FootH);
    }

    void ToggleAssign(string npcId)
    {
        if (_panelSite == null) return;
        if (SiteOf(npcId) == _panelSite) Unassign(npcId);
        else
        {
            NpcData n = Npc(npcId);
            string why = NpcHealth.CannotWorkReason(n, LanguageGuard.IsJp);
            if (why != null) { Say((n != null ? n.npcName : "") + LanguageGuard.Pick(" 干不了活：", "は作業できない：") + why); return; }
            Assign(npcId, _panelSite);
        }
        RefreshPanel();
    }

    void RefreshPanel()
    {
        if (_panelSite == null) return;
        bool jp = LanguageGuard.IsJp;
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        int alive = 0;
        if (w != null) foreach (NpcData n in w.npcs) if (!n.IsDead) alive++;
        if (alive != _rows.Count) RebuildRows();

        _title.text = _panelSite.DisplayName;
        int pct = Mathf.RoundToInt(_panelSite.Ratio * 100f);
        _status.text = _panelSite.builders.Count == 0
            ? (jp ? "計画中：まだ誰も割り当てられていない（進捗 " + pct + "%）。建てる人を選んでください。" : "规划中：还没有指派施工人员（进度 " + pct + "%）。请选择去施工的人。")
            : (jp ? "建設中 " + pct + "%　担当 " + _panelSite.builders.Count + " 人" : "施工中 " + pct + "%　共 " + _panelSite.builders.Count + " 人");

        foreach (Row r in _rows)
        {
            NpcData n = Npc(r.npcId);
            if (n == null) continue;
            r.name.text = n.npcName;
            ConstructionSite mine = SiteOf(r.npcId);
            string why = NpcHealth.CannotWorkReason(n, jp);
            string state;
            if (mine == _panelSite) state = "<color=#74DB8C>" + BuilderState(r.npcId, jp) + "</color>";
            else if (mine != null) state = (jp ? "別の建設：" : "在建别处：") + mine.DisplayName;
            else if (why != null) state = "<color=#FF7B7B>" + why + (jp ? "：作業不可" : "：无法工作") + "</color>";
            else
            {
                NpcMover m = NpcView.GetMover(r.npcId);
                NpcTaskRunner tr = m != null ? m.GetComponent<NpcTaskRunner>() : null;
                state = tr != null && tr.IsBusy ? (jp ? "指示を実行中" : "在执行指令") : (jp ? "手が空いている" : "空闲");
            }
            if (NpcHealth.Has(n, HealthTag.Fatigued)) state += jp ? "（疲労：作業 -50%）" : "（疲惫：效率 -50%）";
            r.state.text = state;

            bool assigned = mine == _panelSite;
            r.btnLabel.text = assigned ? (jp ? "外す" : "撤回") : (jp ? "割り当て" : "指派");
            r.btnImage.color = assigned ? new Color(0.45f, 0.3f, 0.12f, 1f) : (why != null ? new Color(0.25f, 0.25f, 0.28f, 1f) : new Color(0.2f, 0.36f, 0.6f, 1f));
        }
    }

    // ============ 小工具 ============

    static NpcData Npc(string id) => WorldManager.Instance != null && !string.IsNullOrEmpty(id) ? WorldManager.Instance.GetNpcById(id) : null;

    static void Say(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        Debug.Log("[施工] " + msg);
        if (EventLogManager.Instance != null && WorldManager.Instance != null)
            EventLogManager.Instance.AddEvent(WorldManager.Instance.world.day, msg);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // 左上角定位：距左 x、距顶 y、宽 w、高 h。
    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    TextMeshProUGUI Text(Transform parent, string name, float size, Color color, TextAlignmentOptions align)
    {
        RectTransform rt = NewRect(name, parent);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    Button MakeButton(Transform parent, string label, Color color, out TextMeshProUGUI text)
    {
        RectTransform rt = NewRect("Button", parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        Button b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        text = Text(rt, "Label", 16f, Color.white, TextAlignmentOptions.Center);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.text = label;
        return b;
    }
}
