using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

// 建造管理器：完整流程版（阶段A-E）。
// 流程：B键进入 → 绿红覆盖层 → 菜单选类型 → 连点格子生成半透明预览 → NPC逐个走过去把预览变实体。
// 纯规则代码，不走 AI。
public class BuildManager : MonoBehaviour
{
    public static BuildManager Instance;

    [Header("引用")]
    public Camera mainCamera; // 主相机，用于屏幕坐标转世界坐标

    [Header("建造类型预制体（拖入 Inspector）")]
    public GameObject wallPrefab; // 墙的预制体
    public GameObject boxPrefab;  // 箱子的预制体

    // ——状态——
    private bool _buildMode = false;       // 是否在建造模式
    private string _selectedType = null;   // 当前选择的类型（null=未选；"墙"/"箱子"）
    private GameObject _selectedPrefab = null; // 当前类型对应的预制体

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
        _selectedType = null;    // 进入时清空选择，强制玩家重新选类型
        _selectedPrefab = null;
        AutoWirePrefabs();       // Inspector没拖时，从场景BuildButton里自动读取预制体
        ShowGridOverlay();
        ShowBuildMenu();
    }

    // 从场景里已有的 BuildButton 组件读取 wall/box 预制体引用（避免手动拖Inspector）
    void AutoWirePrefabs()
    {
        if (wallPrefab != null && boxPrefab != null) return; // 已手动赋值，跳过
        foreach (BuildButton btn in FindObjectsOfType<BuildButton>())
        {
            if (wallPrefab == null && btn.buildingName == "墙")   wallPrefab = btn.buildingPrefab;
            if (boxPrefab  == null && btn.buildingName == "箱子") boxPrefab  = btn.buildingPrefab;
        }
        if (wallPrefab == null) Debug.LogWarning("[BuildManager] 找不到墙预制体，请在Inspector或BuildButton上赋值。");
        if (boxPrefab  == null) Debug.LogWarning("[BuildManager] 找不到箱子预制体，请在Inspector或BuildButton上赋值。");
    }

    // 旧接口兼容（外部代码如果还在用旧版参数签名）
    public void EnterBuildMode(GameObject buildingPrefab, string buildingName)
    {
        EnterBuildMode();
    }

    // ——退出建造模式——
    public void ExitBuildMode()
    {
        _buildMode = false;
        _selectedType = null;
        _selectedPrefab = null;
        HideGridOverlay();
        HideBuildMenu();
        // 已排队的任务不清除，让NPC把剩余的建完
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
        _buildMenuUI.AddComponent<CanvasScaler>();
        _buildMenuUI.AddComponent<GraphicRaycaster>();

        // 半透明黑色面板，固定在左上角
        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(_buildMenuUI.transform, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.75f);
        RectTransform pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0f, 1f);
        pr.anchoredPosition = new Vector2(20f, -20f);
        pr.sizeDelta = new Vector2(160f, 130f);

        // 标题
        MakeLabel(panel.transform, "建造模式", new Vector2(80f, -18f));

        // 墙按钮
        MakeButton(panel.transform, "墙", new Vector2(80f, -55f),
            () => SelectBuildType("墙", wallPrefab));

        // 箱子按钮
        MakeButton(panel.transform, "箱子", new Vector2(80f, -95f),
            () => SelectBuildType("箱子", boxPrefab));
    }

    void HideBuildMenu()
    {
        if (_buildMenuUI != null) { Destroy(_buildMenuUI); _buildMenuUI = null; }
    }

    // 菜单按钮回调：记录当前选择的类型和预制体
    public void SelectBuildType(string typeName, GameObject prefab)
    {
        _selectedType = typeName;
        _selectedPrefab = prefab;
        Debug.Log($"[BuildManager] 已选择：{typeName}，点格子开始建造。");
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
        if (_selectedType == null)
        {
            Debug.Log("[BuildManager] 请先在左上角菜单选择建造类型。");
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
        grid.SetOccupied(cell.x, cell.y, _selectedType);

        // 覆盖层格子变红（已占用）
        if (_overlayCells.TryGetValue(cell, out SpriteRenderer overlaySR))
            overlaySR.color = new Color(1f, 0f, 0f, 0.4f);

        // 生成半透明预览（用预制体实例化，alpha调到0.5）
        GameObject previewGO = null;
        if (_selectedPrefab != null)
        {
            previewGO = Instantiate(_selectedPrefab, grid.CellToWorld(cell), Quaternion.identity);
            // SortingOrder 设为 5，保证在 Tilemap（通常为0）上方可见
            SpriteRenderer psr = previewGO.GetComponent<SpriteRenderer>();
            if (psr != null) psr.sortingOrder = 5;
            SetSpriteAlpha(previewGO, 0.5f);
        }

        // 加入待建队列
        _buildQueue.Add(new BuildTask
        {
            cell          = cell,
            buildingName  = _selectedType,
            previewGO     = previewGO
        });

        Debug.Log($"[BuildManager] 排队：{_selectedType} @ {grid.CellToLabel(cell.x, cell.y)}（队列共 {_buildQueue.Count} 个）");

        // 如果队列处理协程还没在跑，启动它
        if (!_isProcessingQueue)
            StartCoroutine(ProcessBuildQueue());
    }

    // ——阶段D：NPC逐个施工协程——————————————————————————————

    IEnumerator ProcessBuildQueue()
    {
        _isProcessingQueue = true;

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

            // 让NPC走到目标格子
            NpcMover mover = NpcView.GetMover(builder.id);
            if (mover != null)
            {
                mover.MoveTo(GridManager.Instance.CellToWorld(task.cell));
                yield return null; // 等一帧让MoveTo生效
                while (mover.IsMoving) yield return null; // 等它到达
            }

            // NPC到达：把半透明预览变成实体（alpha → 1）
            if (task.previewGO != null)
                SetSpriteAlpha(task.previewGO, 1f);

            Debug.Log($"[BuildManager] {builder.npcName} 完成：{task.buildingName} @ "
                + GridManager.Instance.CellToLabel(task.cell.x, task.cell.y));
        }

        _isProcessingQueue = false;
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
        yield return new WaitForSeconds(0.15f);
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

    // 找第一个健康、没崩解的NPC作为建造者
    NpcData FindAvailableBuilder()
    {
        WorldState world = WorldManager.Instance.world;
        foreach (NpcData npc in world.npcs)
            if (npc.health > 0f && !npc.isBroken)
                return npc;
        return null;
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
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
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
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
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
