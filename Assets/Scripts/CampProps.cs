using UnityEngine;
using NavMeshPlus.Components;   // NavMeshModifier：把集装箱占的地方标成"不能走"

// ============================================================================
// 营地里的摆设（不是建造物，开局自己出现）。现在只有一个：ABA 的旧集装箱。
//   · 图：Resources/CampArt/Camp_Container（横放：看到长的那一面，约 3 格宽、1.6 格高，能看到一条箱顶）
//         Resources/CampArt/Camp_Container_V（竖放：看到开门的那一头，约 1.7 格宽，箱顶往后延伸）
//         两张都是 333 像素 = 1 格，轴心在箱底正中。
//   · 位置：营地围墙里面的左上角，贴着北边和西边那一圈墙位；那里放不下就依次试右上、左下、右下，都放不下就不放。
//   · 占地：宽 = 图的宽度；深 = 横放 1.33 格、竖放 3 格。这几格记成"集装箱"（别的建筑不能再建在上面），NPC 和敌人绕着走。
//     子弹不会被它挡住（它不是建造物，弹道那边不认识它）。
//   · 只在开局放一次。之后营地范围如果挪了（比如终端机建在别处），集装箱不会跟着挪。
// 找不到图就什么都不做。由 WorldManager 开局自动建（CampProps.Ensure）。
// ============================================================================
public class CampProps : MonoBehaviour
{
    public static CampProps Instance { get; private set; }

    public const string ContainerName = "集装箱";   // 记在格子上的名字

    [Header("集装箱")]
    [Tooltip("关掉 = 开局不放集装箱")]
    public bool placeContainer = true;
    [Tooltip("开 = 竖着放（门朝南，占地窄而深）；关 = 横着放（长的一面朝南）")]
    public bool containerVertical = false;
    [Tooltip("占地有多深（单位 = 格地砖）：横放 / 竖放")]
    public float depthHorizontal = 1.33f, depthVertical = 3f;
    [Tooltip("开 = NPC 和敌人绕着它走；关 = 能从它身上走过去（只是一张图）")]
    public bool containerBlocksMovement = true;

    bool _done;
    float _timer;

    public static CampProps Ensure()
    {
        if (Instance != null) return Instance;
        CampProps p = FindFirstObjectByType<CampProps>();
        if (p != null) { Instance = p; return p; }
        return new GameObject("CampProps").AddComponent<CampProps>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (_done) return;
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = 0.5f;                       // 营地范围（CampZone）要开局几秒后才算好，所以隔半秒看一次，放好就不再看
        TryPlaceContainer();
    }

    void TryPlaceContainer()
    {
        if (!placeContainer) { _done = true; return; }
        CampZone z = CampZone.Instance;
        GridManager g = GridManager.Instance;
        if (z == null || !z.Ready || g == null || !TerrainSystem.HasMap) return;   // 还没准备好：等下一次
        Sprite sprite = CampArt.Load(containerVertical ? "Camp_Container_V" : "Camp_Container");
        if (sprite == null) sprite = CampArt.Load("Camp_Container");                // 竖放的图没有就用横放的
        if (sprite == null) { _done = true; return; }                               // 没有图：不放

        // 占地（小格）：宽 = 图有多宽；深 = 横放 / 竖放各自的深度
        Vector2Int slot = z.SlotSize;
        float depthTiles = containerVertical ? depthVertical : depthHorizontal;
        Vector2Int size = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(sprite.bounds.size.x / g.cellSize)),
            Mathf.Max(1, Mathf.RoundToInt(depthTiles * GridManager.Subdivision)));
        int left = z.Origin.x + slot.x, right = z.Origin.x + z.Size.x - slot.x - size.x;   // 围墙里面最左 / 最右能放的位置
        int top = z.Origin.y + z.Size.y - slot.y - size.y, bottom = z.Origin.y + slot.y;   // 最上 / 最下
        Vector2Int[] tries = { new Vector2Int(left, top), new Vector2Int(right, top), new Vector2Int(left, bottom), new Vector2Int(right, bottom) };

        foreach (Vector2Int origin in tries)
        {
            if (!g.IsAreaBuildable(origin, size)) continue;   // 这块地被占了 / 地形不行：试下一个角
            Place(g, sprite, origin, size);
            break;
        }
        _done = true;                                         // 放没放成都只试这一轮
    }

    void Place(GridManager g, Sprite sprite, Vector2Int origin, Vector2Int size)
    {
        g.SetAreaOccupied(origin, size, ContainerName);       // 这几格记成集装箱：别的建筑建不上来
        TerrainSystem.ClearGrass(origin, size);               // 地上的草清掉

        Vector3 center = g.AreaCenter(origin, size);
        float depth = size.y * g.cellSize;                    // 占地的深度（世界单位）
        GameObject go = new GameObject("Camp_Container");
        go.transform.SetParent(transform, false);
        go.transform.position = center + new Vector3(0f, -depth * 0.5f, 0f);   // 图的轴心在箱底：放在占地的下沿
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 0;                                  // 和别的建筑同层（NPC 是 1，画在它上面）

        if (!containerBlocksMovement) return;
        // 挡路：一个看不见的方块标成"不能走"，然后重新烘焙导航网格（和 TerrainSystem 标水面的做法一样）
        GameObject nav = new GameObject("ContainerNav");
        nav.layer = 2;                                        // Ignore Raycast：不挡鼠标点击
        nav.transform.SetParent(go.transform, false);
        nav.transform.position = center;
        BoxCollider2D box = nav.AddComponent<BoxCollider2D>();
        box.isTrigger = true;                                 // 触发器：不会把人弹开
        box.size = new Vector2(size.x * g.cellSize, depth);
        NavMeshModifier mod = nav.AddComponent<NavMeshModifier>();
        mod.overrideArea = true;
        mod.area = 1;                                         // 1 = Not Walkable
        if (TerrainSystem.Instance != null) TerrainSystem.Instance.RequestRebake();
    }
}
