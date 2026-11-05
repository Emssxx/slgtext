using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using NavMeshPlus.Components;
using UnityEngine.AI;

// 建造管理器：完整流程版（阶段A-E）。
// 流程：B键进入 → 绿红覆盖层 → 菜单选类型 → 连点格子放下蓝图（规划区，不会自动开工；右键取消）
//       → 退出建造模式后点蓝图，指派一名或多名 NPC 去施工（见 Construction.cs 的 ConstructionManager）。
// 纯规则代码，不走 AI。
public class BuildManager : MonoBehaviour
{
    public static BuildManager Instance;

    [Header("引用")]
    public Camera mainCamera; // 主相机，用于屏幕坐标转世界坐标
    public NavMeshSurface navMeshSurface; // 场景里的 NavMeshSurface，施工完毕后重新烘焙

    [Header("建造物数据（拖入 BuildableData 资产，如 Wall.asset / Box.asset）")]
    public BuildableData[] buildables; // 所有可建造物，顺序决定菜单按钮顺序

    // ——状态——
    private bool _buildMode = false;           // 是否在建造模式
    private BuildableData _selectedData = null; // 当前选中的建造物数据（null=未选）

    // ——阶段A：网格覆盖层（一张贴图，一个像素 = 一个小格：绿 = 能建，红 = 已占用）——
    private GameObject _gridOverlay;  // 覆盖层物体
    private Texture2D _overlayTex;
    private Color32[] _overlayPixels;
    private bool _overlayDirty;
    static readonly Color32 OverlayFree = new Color32(0, 255, 0, 90);
    static readonly Color32 OverlayBusy = new Color32(255, 0, 0, 90);
    static readonly Color32 OverlayHoverOk = new Color32(255, 255, 255, 150);
    static readonly Color32 OverlayHoverBad = new Color32(255, 40, 40, 190);

    // ——鼠标所指位置的占地预览（选了建筑后跟着鼠标走）——
    private readonly List<Vector2Int> _hoverCells = new List<Vector2Int>();
    private GameObject _ghost;
    private SpriteRenderer _ghostSr;

    // ——阶段B：建造菜单UI——
    private GameObject _buildMenuUI; // 运行时生成的Canvas

    private bool _bakePending = false; // 是否已经安排了 NavMesh 重新烘焙（同一时间建成好几座只烘焙一次）

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (mainCamera == null) mainCamera = Camera.main;
        buildables = PowerBuildings.AppendTo(buildables); // 追加风力发电机、营地加温器、营地降温器（运行时生成）
        ConstructionManager.Ensure();                      // 规划区施工
    }

    void Update()
    {
        if (!_buildMode) return;
        UpdateHoverPreview();
        FlushOverlay();

        // 左键点击：选格子加入队列（需要先选好类型）
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // 鼠标在 UI 上时不处理地图点击，避免点菜单按钮穿透到网格
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Vector3 mouseWorld = mainCamera.ScreenToWorldPoint(
                new Vector3(mouseScreen.x, mouseScreen.y, 0));
            mouseWorld.z = 0;
            Vector2Int cell = GridManager.Instance.WorldToCell(mouseWorld);
            HandleCellClick(cell);
        }

        // 右键：取消这一格的规划（蓝图）
        if (Mouse.current.rightButton.wasPressedThisFrame && GridManager.Instance != null)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            Vector2 ms = Mouse.current.position.ReadValue();
            Vector3 mw = mainCamera.ScreenToWorldPoint(new Vector3(ms.x, ms.y, 0));
            ConstructionSite site = ConstructionSite.At(GridManager.Instance.WorldToCell(mw));
            if (site != null && ConstructionManager.Instance != null)
            {
                Vector2Int so = site.origin, ss = site.size;
                ConstructionManager.Instance.Cancel(site);
                RefreshOverlayArea(so, ss); // 格子空出来了：变回绿色
            }
        }

        // Esc退出建造模式
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            ExitBuildMode();
    }

    // ——进入建造模式——
    public void EnterBuildMode()
    {
        _buildMode = true;
        _selectedData = null;
        Time.timeScale = 0f;     // 暂停游戏时间，世界静止
        ShowGridOverlay();
        ShowBuildMenu();
    }

    // ——退出建造模式——
    public void ExitBuildMode()
    {
        _buildMode = false;
        _selectedData = null;
        WireTool.Deactivate();
        ClearHoverPreview();
        HideGridOverlay();
        HideBuildMenu();
        Time.timeScale = 1f;     // 恢复游戏时间
        // 规划区不会自动开工：提醒玩家去点蓝图指派施工的人
        int unassigned = 0;
        foreach (ConstructionSite s in ConstructionSite.All) if (s != null && s.builders.Count == 0) unassigned++;
        if (unassigned > 0 && EventLogManager.Instance != null && WorldManager.Instance != null)
            EventLogManager.Instance.AddEvent(WorldManager.Instance.world.day, LanguageGuard.Pick(
                "有 " + unassigned + " 处规划还没人施工：点一下蓝图，指派一名或多名幸存者去建。",
                unassigned + " か所の計画がまだ手つかず：青図をクリックして、建てる人を割り当てよう。"));
    }

    public bool IsBuildMode => _buildMode;

    // ——阶段B：菜单——————————————————————————————————————

    void ShowBuildMenu()
    {
        if (_buildMenuUI != null) return;

        // 新建Canvas
        _buildMenuUI = new GameObject("BuildMenuUI");
        Canvas canvas = _buildMenuUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        // 和主界面同一套缩放（1920×1080），菜单才能刚好停在右下角建造按钮的上方
        CanvasScaler scaler = _buildMenuUI.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;
        _buildMenuUI.AddComponent<GraphicRaycaster>();

        // 1. 按五大类分组（基础→防御→生产→电力→生活），没有建筑的分类不显示。
        BuildCategory[] order = { BuildCategory.Basic, BuildCategory.Defense, BuildCategory.Production, BuildCategory.Power, BuildCategory.Living };
        List<BuildCategory> cats = new List<BuildCategory>();
        List<List<BuildableData>> groups = new List<List<BuildableData>>();
        foreach (BuildCategory cat in order)
        {
            List<BuildableData> g = new List<BuildableData>();
            if (buildables != null)
                foreach (BuildableData bd in buildables)
                    if (bd != null && bd.buildCategory == cat) g.Add(bd);
            if (g.Count > 0) { cats.Add(cat); groups.Add(g); }
        }

        // 2. 面板高度：标题 + 每个分类小标题26 + 每个按钮40（最后多一个「电线」按钮）
        int btnCount = 0;
        foreach (List<BuildableData> g in groups) btnCount += g.Count;
        float panelHeight = 44f + cats.Count * 26f + btnCount * 40f + 48f;

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(_buildMenuUI.transform, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.75f);
        RectTransform pr = panel.GetComponent<RectTransform>();
        // 右下角，停在建造按钮上方（位置见 HudLayout.BuildMenuPos）；先放在屏幕右边外面，最后滑进来
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(1f, 0f);
        pr.sizeDelta = new Vector2(160f, panelHeight);
        Vector2 shownPos = HudLayout.BuildMenuPos;
        pr.anchoredPosition = shownPos + new Vector2(pr.sizeDelta.x + 40f, 0f);

        MakeLabel(panel.transform, ProgressionRules.L("build_menu_title"), new Vector2(80f, -18f));

        // 3. 逐类生成：分类小标题 + 该类建筑按钮。没解锁的按钮变灰、标出原因，点了在事件栏提示。
        float y = -40f;
        for (int c = 0; c < cats.Count; c++)
        {
            MakeLabel(panel.transform, "— " + ProgressionRules.L("build_cat_" + cats[c].ToString().ToLower()) + " —", new Vector2(80f, y - 2f));
            y -= 26f;
            foreach (BuildableData bd in groups[c])
            {
                BuildableData captured = bd; // 闭包捕获
                bool unlocked = ProgressionRules.IsBuildableUnlocked(bd, out _);
                string label = unlocked ? bd.LocalizedName : bd.LocalizedName + " [" + ProgressionRules.ShortLockReason(bd) + "]";
                UnityEngine.Events.UnityAction onClick;
                if (unlocked) onClick = () => SelectBuildType(captured);
                else onClick = () => ShowLockedReason(captured);
                MakeButton(panel.transform, label, new Vector2(80f, y - 8f), onClick);
                if (!unlocked) // 刚生成的按钮是最后一个子物体，改成灰色
                    panel.transform.GetChild(panel.transform.childCount - 1).GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
                y -= 40f;
            }
        }

        // 4. 电线（连线工具，不是建筑）：先点一个电力设备，再点另一个，把它们连起来
        y -= 8f;
        MakeButton(panel.transform, ProgressionRules.L("build_wire"), new Vector2(80f, y - 8f), () => { });
        Image wireImg = panel.transform.GetChild(panel.transform.childCount - 1).GetComponent<Image>();
        Color wireIdle = new Color(0.35f, 0.3f, 0.1f, 1f), wireOn = new Color(0.85f, 0.65f, 0.15f, 1f);
        wireImg.color = WireTool.Active ? wireOn : wireIdle;
        wireImg.GetComponent<Button>().onClick.AddListener(() =>
        {
            _selectedData = null;
            WireTool.Toggle();
            wireImg.color = WireTool.Active ? wireOn : wireIdle;
        });

        panel.AddComponent<UiSlider>().SlideTo(shownPos); // 从右边滑出来
    }

    // 点了未解锁的建筑：在事件栏告诉玩家为什么还不能建。
    void ShowLockedReason(BuildableData bd)
    {
        ProgressionRules.IsBuildableUnlocked(bd, out string reason);
        string msg = ProgressionRules.LF("build_locked_msg", bd.LocalizedName, reason);
        Debug.Log("[BuildManager] " + msg);
        if (EventLogManager.Instance != null && WorldManager.Instance != null)
            EventLogManager.Instance.AddEvent(WorldManager.Instance.world.day, msg);
    }

    void HideBuildMenu()
    {
        if (_buildMenuUI == null) return;
        GameObject old = _buildMenuUI;
        _buildMenuUI = null; // 先断开引用：滑出途中又进建造模式的话，会新建一个菜单
        Transform p = old.transform.Find("Panel");
        UiSlider slider = p != null ? p.GetComponent<UiSlider>() : null;
        if (slider != null && slider.isActiveAndEnabled)
        {
            RectTransform pr = (RectTransform)p;
            slider.SlideTo(pr.anchoredPosition + new Vector2(pr.sizeDelta.x + 40f, 0f), () => { if (old != null) Destroy(old); });
        }
        else Destroy(old);
    }

    // 菜单按钮回调：记录当前选中的建造物数据
    public void SelectBuildType(BuildableData data)
    {
        if (WireTool.Active) WireTool.Toggle(); // 选了建筑就退出电线模式
        _selectedData = data;
        Debug.Log($"[BuildManager] 已选择：{data.displayName}，点格子开始建造。");
    }

    // ——阶段A：网格覆盖层——————————————————————————————————

    void ShowGridOverlay()
    {
        if (_gridOverlay != null) return;

        GridManager grid = GridManager.Instance;
        if (grid == null) return;

        // 一张 gridWidth × gridHeight 的贴图铺满地图：比每格一个物体省得多（细分后格子很多）
        int w = grid.gridWidth, h = grid.gridHeight;
        _overlayTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        _overlayTex.filterMode = FilterMode.Point;
        _overlayTex.wrapMode = TextureWrapMode.Clamp;
        _overlayPixels = new Color32[w * h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                _overlayPixels[y * w + x] = grid.IsBuildable(x, y) ? OverlayFree : OverlayBusy;
        _overlayTex.SetPixels32(_overlayPixels);
        _overlayTex.Apply();

        _gridOverlay = new GameObject("GridOverlay");
        _gridOverlay.transform.position = new Vector3(grid.origin.x, grid.origin.y, 0f);
        SpriteRenderer sr = _gridOverlay.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(_overlayTex, new Rect(0, 0, w, h), Vector2.zero, 1f / grid.cellSize); // 1 像素 = 1 小格
        sr.sortingOrder = 10; // 在Tilemap地图层上方
        _overlayDirty = false;
    }

    void HideGridOverlay()
    {
        if (_gridOverlay != null) { Destroy(_gridOverlay); _gridOverlay = null; }
        if (_overlayTex != null) { Destroy(_overlayTex); _overlayTex = null; }
        _overlayPixels = null;
        _hoverCells.Clear();
    }

    void SetOverlay(int x, int y, Color32 c)
    {
        GridManager grid = GridManager.Instance;
        if (_overlayPixels == null || grid == null || !grid.InBounds(x, y)) return;
        _overlayPixels[y * grid.gridWidth + x] = c;
        _overlayDirty = true;
    }

    // 按格子当前的占用情况恢复颜色（绿 / 红）。
    void RefreshOverlayArea(Vector2Int origin, Vector2Int size)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null) return;
        for (int x = origin.x; x < origin.x + size.x; x++)
            for (int y = origin.y; y < origin.y + size.y; y++)
                SetOverlay(x, y, grid.IsBuildable(x, y) ? OverlayFree : OverlayBusy);
    }

    void FlushOverlay()
    {
        if (!_overlayDirty || _overlayTex == null || _overlayPixels == null) return;
        _overlayTex.SetPixels32(_overlayPixels);
        _overlayTex.Apply();
        _overlayDirty = false;
    }

    // 选了建筑后：鼠标所在位置显示它会占哪些格（白 = 能放，红 = 被挡住）和一个半透明的建筑影子。
    void UpdateHoverPreview()
    {
        GridManager grid = GridManager.Instance;
        bool show = _selectedData != null && !WireTool.Active && grid != null && Mouse.current != null
                    && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        // 先把上一帧高亮的格子恢复
        foreach (Vector2Int c in _hoverCells) SetOverlay(c.x, c.y, grid != null && grid.IsBuildable(c.x, c.y) ? OverlayFree : OverlayBusy);
        _hoverCells.Clear();
        if (!show) { if (_ghost != null) _ghost.SetActive(false); return; }

        Vector2 ms = Mouse.current.position.ReadValue();
        Vector3 mw = mainCamera.ScreenToWorldPoint(new Vector3(ms.x, ms.y, 0));
        Vector2Int cell = grid.WorldToCell(mw);
        Vector2Int size = _selectedData.FootprintSize(grid.cellSize);
        Vector2Int origin = grid.FootprintOrigin(cell, size);
        bool ok = grid.IsAreaBuildable(origin, size);
        for (int x = origin.x; x < origin.x + size.x; x++)
            for (int y = origin.y; y < origin.y + size.y; y++)
            {
                if (!grid.InBounds(x, y)) continue;
                _hoverCells.Add(new Vector2Int(x, y));
                SetOverlay(x, y, ok ? OverlayHoverOk : OverlayHoverBad);
            }

        // 建筑影子
        if (_ghost == null)
        {
            _ghost = new GameObject("BuildGhost");
            _ghostSr = _ghost.AddComponent<SpriteRenderer>();
            _ghostSr.sortingOrder = 11;
        }
        SpriteRenderer psr = _selectedData.prefab != null ? _selectedData.prefab.GetComponent<SpriteRenderer>() : null;
        _ghostSr.sprite = psr != null && psr.sprite != null ? psr.sprite : _selectedData.sprite;
        _ghost.transform.localScale = _selectedData.prefab != null ? _selectedData.prefab.transform.localScale : Vector3.one;
        _ghost.transform.position = grid.AreaCenter(origin, size);
        _ghostSr.color = ok ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 0.35f, 0.35f, 0.45f);
        _ghost.SetActive(_ghostSr.sprite != null);
    }

    void ClearHoverPreview()
    {
        _hoverCells.Clear();
        if (_ghost != null) { Destroy(_ghost); _ghost = null; _ghostSr = null; }
    }

    // ——阶段C：点击格子——————————————————————————————————————

    void HandleCellClick(Vector2Int cell)
    {
        // 电线模式：点的是电力设备，不是建造
        if (WireTool.Active) { WireTool.HandleClick(cell); return; }

        // 还没选类型，提示玩家先选
        if (_selectedData == null)
        {
            Debug.Log("[BuildManager] 请先在左上角菜单选择建造类型。");
            return;
        }

        // 解锁检查：终端机等级/科技不够、或唯一建筑（终端机）已放置 → 不能建
        if (!ProgressionRules.IsBuildableUnlocked(_selectedData, out _))
        {
            ShowLockedReason(_selectedData);
            return;
        }

        GridManager grid = GridManager.Instance;

        // 占地：以点的格子为中心，按建筑大小占若干小格
        Vector2Int size = _selectedData.FootprintSize(grid.cellSize);
        Vector2Int origin = grid.FootprintOrigin(cell, size);

        // 有任何一格不可建（越界或已占用）
        if (!grid.IsAreaBuildable(origin, size))
        {
            Debug.Log($"[BuildManager] {grid.CellToLabel(cell.x, cell.y)} 放不下（占地 {size.x}×{size.y} 格）。");
            return;
        }

        PlaceBlueprint(_selectedData, cell);
        Debug.Log($"[BuildManager] 规划：{_selectedData.displayName} @ {grid.CellToLabel(cell.x, cell.y)}");
        if (_selectedData.uniqueBuild) _selectedData = null;    // 唯一建筑放一次就取消选中
    }

    // 放下一块规划区（蓝图）。玩家点格子、NPC 自己决定建造（NpcIntentBrain）都走这里。
    // cell = 占地中心那一格。放不下（越界 / 被占）返回 null。解锁检查由调用方做。
    public ConstructionSite PlaceBlueprint(BuildableData data, Vector2Int cell)
    {
        GridManager grid = GridManager.Instance;
        if (data == null || grid == null) return null;
        Vector2Int size = data.FootprintSize(grid.cellSize);
        Vector2Int origin = grid.FootprintOrigin(cell, size);
        if (!grid.IsAreaBuildable(origin, size)) return null;

        // 先标记占用，防止连点同一处；覆盖层这些格子变红
        grid.SetAreaOccupied(origin, size, data.displayName);
        TerrainSystem.ClearGrass(origin, size); // 地上的草清掉（树 / 石头 / 矿石占着的格本来就放不下）
        RefreshOverlayArea(origin, size);
        Vector3 center = grid.AreaCenter(origin, size);

        // 生成半透明预览（用 BuildableData.prefab 实例化，alpha调到0.5）
        GameObject previewGO = null;
        if (data.prefab != null)
        {
            previewGO = Instantiate(data.prefab, center, Quaternion.identity);
            previewGO.AddComponent<BuildingFootprint>().Set(origin, size); // 记下占了哪些格（点击、电线、悬停信息用）
            previewGO.SendMessage("OnBuildPlaced", SendMessageOptions.DontRequireReceiver); // 通知：施工中（终端机用）
            // SortingOrder 设为 0，与地图同层；NPC 用 1，保证 NPC 始终显示在建筑上方
            SpriteRenderer psr = previewGO.GetComponent<SpriteRenderer>();
            if (psr != null) psr.sortingOrder = 0;
            SetSpriteAlpha(previewGO, 0.5f);
        }
        else
        {
            Debug.LogWarning($"[BuildManager] {data.displayName} 的 prefab 未赋值，跳过预览实例化。");
        }

        // 变成一块规划区（蓝图）：不自动开工，等玩家点它指派施工的人（或 NPC 自己去施工）
        ConstructionSite site = ConstructionManager.Ensure().CreateSite(data, origin, size, previewGO);
        ProgressionRules.NotifyPlaced(data);           // 唯一建筑记一笔
        return site;
    }

    // ——阶段D：施工完成（ConstructionManager 调）——————————————————————

    // 蓝图建成：变成实体、通知建筑自己（终端机上线、发电机开始发电…）；阻挡移动的建筑重新烘焙 NavMesh。
    public void FinishConstruction(GameObject go, bool blocksMovement)
    {
        if (go == null) return;
        SetSpriteAlpha(go, 1f);
        go.SendMessage("OnBuildComplete", SendMessageOptions.DontRequireReceiver); // 通知：施工完成

        // 阻挡移动的建筑（如墙）：挂 NavMeshModifier 标记为 Not Walkable，烘焙后 NPC 绕开
        if (blocksMovement)
        {
            NavMeshModifier mod = go.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = 1; // 1 = Not Walkable
            if (!_bakePending) StartCoroutine(BakeSoon());
        }
    }

    // 稍等一下再烘焙：同一时间建成好几座只烘焙一次。
    IEnumerator BakeSoon()
    {
        _bakePending = true;
        yield return new WaitForSeconds(0.3f);
        _bakePending = false;
        if (navMeshSurface != null)
        {
            Physics2D.SyncTransforms(); // 确保新碰撞体已注册到物理系统，烘焙结果才准确
            navMeshSurface.BuildNavMeshAsync();
            Debug.Log("[BuildManager] 有阻挡建筑施工完成，已触发 NavMesh 重新烘焙。");
        }
        else Debug.LogWarning("[BuildManager] navMeshSurface 未赋值，跳过 NavMesh 烘焙。");
    }

    // ——工具方法——————————————————————————————————————————

    // 设置GameObject根级SpriteRenderer的透明度
    void SetSpriteAlpha(GameObject go, float alpha)
    {
        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) return;
        Color c = sr.color;
        c.a = alpha;
        sr.color = c;
    }

    // 运行时生成Text标签（供ShowBuildMenu用）
    void MakeLabel(Transform parent, string text, Vector2 pos)
    {
        GameObject go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 14;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        RectTransform rt = go.GetComponent<RectTransform>();
        // pos 是相对菜单面板左上角的坐标（x=80 即 160 宽面板的正中），所以锚点放在左上角
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(140f, 24f);
    }

    // 运行时生成按钮（供ShowBuildMenu用）
    void MakeButton(Transform parent, string label, Vector2 pos,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.15f, 0.45f, 0.15f, 1f); // 深绿色按钮

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); // 同上：pos 相对面板左上角
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(120f, 32f);

        // 按钮文字
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        Text t = textGO.AddComponent<Text>();
        t.text = label;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 13;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
    }
}
