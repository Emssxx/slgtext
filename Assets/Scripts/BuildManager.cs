using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using NavMeshPlus.Components;
using UnityEngine.AI;

// 建造管理器：完整流程版（阶段A-E）。
// 流程：B键进入 → 绿红覆盖层 → 菜单选类型 → 连点格子生成半透明预览 → NPC逐个走过去把预览变实体。
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

    // ——阶段A：网格覆盖层——
    private GameObject _gridOverlay; // 覆盖层父物体
    private Dictionary<Vector2Int, SpriteRenderer> _overlayCells // 格子坐标 → 覆盖层SpriteRenderer，供高亮用
        = new Dictionary<Vector2Int, SpriteRenderer>();

    // ——阶段B：建造菜单UI——
    private GameObject _buildMenuUI; // 运行时生成的Canvas

    // ——阶段C/D：预览队列——
    private class BuildTask
    {
        public Vector2Int cell;        // 目标格子坐标
        public string buildingName;    // 建筑名（用于 GridManager 占用记录）
        public GameObject previewGO;   // 半透明预览实例（NPC到达后alpha改为1）
        public float buildTime;        // NPC到达后需等待的建造耗时（秒）
        public bool blocksMovement;    // 是否阻挡移动（决定挂NavMeshModifier + 是否重新烘焙）
    }
    private List<BuildTask> _buildQueue = new List<BuildTask>(); // 待施工队列
    private bool _isProcessingQueue = false; // 是否有协程在处理队列

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (mainCamera == null) mainCamera = Camera.main;
    }

    void Update()
    {
        if (!_buildMode) return;

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
        HideGridOverlay();
        HideBuildMenu();
        Time.timeScale = 1f;     // 恢复游戏时间
        // 时间恢复后启动施工，NPC 开始逐个去建蓝图
        if (_buildQueue.Count > 0 && !_isProcessingQueue)
            StartCoroutine(ProcessBuildQueue());
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

        // 2. 面板高度：标题 + 每个分类小标题26 + 每个按钮40
        int btnCount = 0;
        foreach (List<BuildableData> g in groups) btnCount += g.Count;
        float panelHeight = 44f + cats.Count * 26f + btnCount * 40f;

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
        _selectedData = data;
        Debug.Log($"[BuildManager] 已选择：{data.displayName}，点格子开始建造。");
    }

    // ——阶段A：网格覆盖层——————————————————————————————————

    void ShowGridOverlay()
    {
        if (_gridOverlay != null) return;

        GridManager grid = GridManager.Instance;
        if (grid == null) return;

        _gridOverlay = new GameObject("GridOverlay");
        _overlayCells.Clear();

        // 运行时生成1×1白色Sprite，600个格子共用
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        Sprite whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        for (int x = 0; x < grid.gridWidth; x++)
        {
            for (int y = 0; y < grid.gridHeight; y++)
            {
                GameObject go = new GameObject($"Cell_{x}_{y}");
                go.transform.SetParent(_gridOverlay.transform);
                go.transform.position = grid.CellToWorld(x, y);
                go.transform.localScale = Vector3.one * grid.cellSize;

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = whiteSprite;
                sr.sortingOrder = 10; // 在Tilemap地图层上方

                bool canBuild = grid.IsBuildable(x, y);
                sr.color = canBuild
                    ? new Color(0f, 1f, 0f, 0.4f)  // 绿：可建
                    : new Color(1f, 0f, 0f, 0.4f);  // 红：不可建

                _overlayCells[new Vector2Int(x, y)] = sr; // 存引用，供阶段E高亮用
            }
        }
    }

    void HideGridOverlay()
    {
        if (_gridOverlay != null) { Destroy(_gridOverlay); _gridOverlay = null; }
        _overlayCells.Clear();
    }

    // ——阶段C：点击格子——————————————————————————————————————

    void HandleCellClick(Vector2Int cell)
    {
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

        // 格子不可建（越界或已占用）
        if (!grid.IsBuildable(cell.x, cell.y))
        {
            Debug.Log($"[BuildManager] {grid.CellToLabel(cell.x, cell.y)} 不可建造。");
            return;
        }

        // 阶段E：点击高亮反馈
        FlashCell(cell);

        // 先标记占用，防止连点同一格
        grid.SetOccupied(cell.x, cell.y, _selectedData.displayName);

        // 覆盖层格子变红（已占用）
        if (_overlayCells.TryGetValue(cell, out SpriteRenderer overlaySR))
            overlaySR.color = new Color(1f, 0f, 0f, 0.4f);

        // 生成半透明预览（用 BuildableData.prefab 实例化，alpha调到0.5）
        GameObject previewGO = null;
        if (_selectedData.prefab != null)
        {
            previewGO = Instantiate(_selectedData.prefab, grid.CellToWorld(cell), Quaternion.identity);
            previewGO.SendMessage("OnBuildPlaced", SendMessageOptions.DontRequireReceiver); // 通知：施工中（终端机用）
            // SortingOrder 设为 0，与地图同层；NPC 用 1，保证 NPC 始终显示在建筑上方
            SpriteRenderer psr = previewGO.GetComponent<SpriteRenderer>();
            if (psr != null) psr.sortingOrder = 0;
            SetSpriteAlpha(previewGO, 0.5f);
        }
        else
        {
            Debug.LogWarning($"[BuildManager] {_selectedData.displayName} 的 prefab 未赋值，跳过预览实例化。");
        }

        // 加入待建队列（把数据驱动的字段一并存入，施工时直接读 task）
        _buildQueue.Add(new BuildTask
        {
            cell            = cell,
            buildingName    = _selectedData.displayName,
            previewGO       = previewGO,
            buildTime       = _selectedData.buildTime,
            blocksMovement  = _selectedData.blocksMovement
        });

        Debug.Log($"[BuildManager] 排队：{_selectedData.displayName} @ {grid.CellToLabel(cell.x, cell.y)}（队列共 {_buildQueue.Count} 个）");
        ProgressionRules.NotifyPlaced(_selectedData);           // 唯一建筑记一笔
        if (_selectedData.uniqueBuild) _selectedData = null;    // 唯一建筑放一次就取消选中
        // 点格子只入队，不派NPC——退出建造模式后才施工
    }

    // ——阶段D：NPC逐个施工协程——————————————————————————————

    IEnumerator ProcessBuildQueue()
    {
        _isProcessingQueue = true;
        bool needsNavBake = false; // 本批次是否有阻挡移动的建筑，决定完成后是否重新烘焙

        while (_buildQueue.Count > 0)
        {
            BuildTask task = _buildQueue[0];
            _buildQueue.RemoveAt(0);

            // 找一个可用的NPC
            NpcData builder = FindAvailableBuilder();
            if (builder == null)
            {
                // 暂时没NPC，把任务放回去，等1秒再试
                _buildQueue.Insert(0, task);
                yield return new WaitForSeconds(1f);
                continue;
            }

            // 登记占用：施工期间闲置行为（闲逛/闲聊）不会来抢这个 NPC 的移动
            NpcBusy.Claim(builder.id, "build");

            // 让NPC走到目标格子
            NpcMover mover = NpcView.GetMover(builder.id);
            if (mover != null)
            {
                mover.MoveTo(GridManager.Instance.CellToWorld(task.cell));
                yield return null; // 等一帧让MoveTo生效
                while (mover.IsMoving) yield return null; // 等它到达
            }

            // NPC到达后等待建造耗时（模拟施工过程，时间来自 BuildableData.buildTime）
            if (task.buildTime > 0f)
                yield return new WaitForSeconds(task.buildTime);

            // 把半透明预览变成实体（alpha → 1）
            if (task.previewGO != null)
            {
                SetSpriteAlpha(task.previewGO, 1f);
                task.previewGO.SendMessage("OnBuildComplete", SendMessageOptions.DontRequireReceiver); // 通知：施工完成
            }

            // 阻挡移动的建筑（如墙）：挂 NavMeshModifier 标记为 Not Walkable，烘焙后 NPC 绕开
            if (task.blocksMovement && task.previewGO != null)
            {
                NavMeshModifier mod = task.previewGO.AddComponent<NavMeshModifier>();
                mod.overrideArea = true;
                mod.area = 1; // 1 = Not Walkable
                needsNavBake = true; // 本批次有阻挡建筑，需要重新烘焙
            }

            NpcBusy.Release(builder.id, "build"); // 施工完成，交还给闲置行为
            Debug.Log($"[BuildManager] {builder.npcName} 完成：{task.buildingName} @ "
                + GridManager.Instance.CellToLabel(task.cell.x, task.cell.y));
        }

        _isProcessingQueue = false;

        // 只有 blocksMovement=true 的建筑才需要重新烘焙（箱子等可穿过的跳过，省性能）
        if (needsNavBake)
        {
            if (navMeshSurface != null)
            {
                Physics2D.SyncTransforms(); // 确保新碰撞体已注册到物理系统，烘焙结果才准确
                navMeshSurface.BuildNavMeshAsync();
                Debug.Log("[BuildManager] 有阻挡建筑施工完成，已触发 NavMesh 重新烘焙。");
            }
            else
            {
                Debug.LogWarning("[BuildManager] navMeshSurface 未赋值，跳过 NavMesh 烘焙。");
            }
        }
    }

    // ——阶段E：点击格子短暂高亮—————————————————————————————

    void FlashCell(Vector2Int cell)
    {
        if (_overlayCells.TryGetValue(cell, out SpriteRenderer sr))
            StartCoroutine(FlashCoroutine(sr));
    }

    IEnumerator FlashCoroutine(SpriteRenderer sr)
    {
        if (sr == null) yield break;
        sr.color = new Color(1f, 1f, 1f, 0.8f); // 短暂变白
        yield return new WaitForSecondsRealtime(0.15f); // 用真实时间，时间暂停时也能正常闪烁
        if (sr != null) sr.color = new Color(1f, 0f, 0f, 0.4f); // 恢复红（已排队）
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

    // 找一个健康、没崩解的NPC作为建造者：优先找手上没事的（没在执行玩家指令、没被别的系统占用），
    // 大家都在忙时沿用原来的做法，找第一个能动的。
    NpcData FindAvailableBuilder()
    {
        WorldState world = WorldManager.Instance.world;
        NpcData fallback = null;
        foreach (NpcData npc in world.npcs)
        {
            if (npc.health <= 0f || npc.isBroken) continue;
            if (fallback == null) fallback = npc;
            NpcMover m = NpcView.GetMover(npc.id);
            NpcTaskRunner runner = m != null ? m.GetComponent<NpcTaskRunner>() : null;
            if ((runner == null || !runner.IsBusy) && !NpcBusy.IsBusy(npc.id))
                return npc;
        }
        return fallback;
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
