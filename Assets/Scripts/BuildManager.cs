using UnityEngine;
using System.Collections.Generic;

// 建造管理器：建造模式的核心（阶段1）。
// 负责：进入/退出建造模式、鼠标点击选格、把建造任务派给 NPC。
// 全程纯代码，不走 AI（建造是高频操作，要零延迟）。
public class BuildManager : MonoBehaviour
{
    public static BuildManager Instance;

    [Header("引用")]
    public Camera mainCamera;           // 主相机（把鼠标屏幕坐标转成世界坐标）
    public GameObject buildPreviewPrefab; // 选中格子的高亮预览（可选，拖一个半透明方块）

    [Header("当前要建造的东西")]
    public GameObject currentBuildingPrefab; // 当前选择建造的建筑预制体
    public string currentBuildingName = "墙"; // 建筑名（占用记录用）

    private bool _buildMode = false;     // 是否在建造模式
    private GameObject _previewInstance;  // 预览物体实例

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (mainCamera == null) mainCamera = Camera.main;
    }

    void Update()
    {
        if (!_buildMode) return;

        // 鼠标当前指向的格子
        Vector3 mouseWorld = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0;
        Vector2Int cell = GridManager.Instance.WorldToCell(mouseWorld);

        // 更新预览位置（高亮当前格）
        UpdatePreview(cell);

        // 左键点击 → 在该格建造
        if (Input.GetMouseButtonDown(0))
        {
            TryBuildAt(cell);
        }

        // 右键 / Esc → 退出建造模式
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            ExitBuildMode();
        }
    }

    // 进入建造模式（UI 按钮调用，传入要建什么）。
    public void EnterBuildMode(GameObject buildingPrefab, string buildingName)
    {
        _buildMode = true;
        currentBuildingPrefab = buildingPrefab;
        currentBuildingName = buildingName;

        // 生成预览物体
        if (buildPreviewPrefab != null && _previewInstance == null)
            _previewInstance = Instantiate(buildPreviewPrefab);
    }

    // 退出建造模式。
    public void ExitBuildMode()
    {
        _buildMode = false;
        if (_previewInstance != null) _previewInstance.SetActive(false);
    }

    public bool IsBuildMode => _buildMode;

    // 更新预览高亮到某格。
    void UpdatePreview(Vector2Int cell)
    {
        if (_previewInstance == null) return;
        _previewInstance.SetActive(true);
        _previewInstance.transform.position = GridManager.Instance.CellToWorld(cell);

        // 可建造=绿色，不可=红色（如果预览有 SpriteRenderer）
        SpriteRenderer sr = _previewInstance.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            bool ok = GridManager.Instance.IsBuildable(cell.x, cell.y);
            sr.color = ok ? new Color(0, 1, 0, 0.5f) : new Color(1, 0, 0, 0.5f);
        }
    }

    // 尝试在某格建造。
    void TryBuildAt(Vector2Int cell)
    {
        GridManager grid = GridManager.Instance;

        // 检查能不能建
        if (!grid.IsBuildable(cell.x, cell.y))
        {
            Debug.Log($"格子 {grid.CellToLabel(cell.x, cell.y)} 不能建造（越界或已占用）。");
            return;
        }

        // 派一个 NPC 去建造（阶段1：找第一个空闲且没崩解的 NPC）。
        NpcData builder = FindAvailableBuilder();
        if (builder == null)
        {
            Debug.Log("没有可用的 NPC 来建造。");
            return;
        }

        // 标记格子已占用（先占住，防止重复派单）
        grid.SetOccupied(cell.x, cell.y, currentBuildingName);

        // 让 NPC 走过去建造
        StartBuildTask(builder, cell);

        Debug.Log($"派 {builder.npcName} 去 {grid.CellToLabel(cell.x, cell.y)} 建造 {currentBuildingName}。");
    }

    // 找一个能去建造的 NPC（阶段1：简单找第一个健康、没崩解的）。
    NpcData FindAvailableBuilder()
    {
        WorldState world = WorldManager.Instance.world;
        foreach (NpcData npc in world.npcs)
            if (npc.health > 0f && !npc.isBroken)
                return npc;
        return null;
    }

    // 让 NPC 走到目标格子并建造。
    void StartBuildTask(NpcData builder, Vector2Int cell)
    {
        Vector3 targetPos = GridManager.Instance.CellToWorld(cell);

        // 通过 NpcView 找到这个 NPC 的 Mover，让它走过去。
        NpcMover mover = NpcView.GetMover(builder.id);
        if (mover != null)
        {
            mover.MoveTo(targetPos);
            // 到达后生成建筑：用一个协程等它到达
            StartCoroutine(BuildWhenArrived(mover, cell));
        }
        else
        {
            // 没有 Mover（NPC 没实体）→ 直接生成建筑
            SpawnBuilding(cell);
        }
    }

    // 等 NPC 到达后生成建筑。
    System.Collections.IEnumerator BuildWhenArrived(NpcMover mover, Vector2Int cell)
    {
        yield return null;
        while (mover.IsMoving) yield return null; // 等它走到
        SpawnBuilding(cell);
    }

    // 在格子上生成建筑物。
    void SpawnBuilding(Vector2Int cell)
    {
        if (currentBuildingPrefab == null)
        {
            Debug.LogWarning("没有设置建筑预制体，无法生成。");
            return;
        }
        Vector3 pos = GridManager.Instance.CellToWorld(cell);
        Instantiate(currentBuildingPrefab, pos, Quaternion.identity);
        Debug.Log($"在 {GridManager.Instance.CellToLabel(cell.x, cell.y)} 建好了 {currentBuildingName}。");
    }
}
