using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Tilemaps;
using NavMeshPlus.Components;

// ============================================================================
// 地形系统：开局按地图种子（MapSeed）生成地形 + 天然资源，画出来，并接进寻路 / 建造 / 移速 / 坐标浮窗。
// WorldManager 在生成 NPC 之前调 TerrainSystem.Ensure().Generate()，场景里不用摆。
// 想在 Inspector 里改参数、填地砖 / 地形图片：场景里建个空物体挂上本脚本即可（会优先用它）。
//
// 【地形】（规则数值在 TerrainGenerator.cs 的 TerrainInfo）
//   土地     未铺装地面：NPC 移速 -10%；以后能长植物（TerrainInfo.CanGrowPlants）。
//   营地地砖 铺装地面：基础移速。三张地砖图（中心标记砖 / 普通砖 / 破损砖）画在场景原来的 Grid 上（1 块砖 = 3×3 小格）。
//   沼泽     NPC 移速 -80%，不能建造；寻路尽量绕开（NavMesh 区域 swampArea，代价 swampAreaCost）。
//   水面     不可行走（NavMesh 挖掉）、不能建造；可以打水（倒进雨水收集器再净化）、钓鱼（亚空间蛋白质）。
// 【天然资源】ResourceNode：草 / 树 / 石头 / 矿石（采集：对 NPC 说"去砍树"等，见 NpcCommands / NpcTaskRunner）。
// 【接入】
//   · 移速：NpcMover 每 0.2 秒按脚下地形调速度（SpeedFactorAt）。
//   · 建造：GridManager.IsBuildable 问 BlocksBuilding（墙、水、沼泽、树 / 石头 / 矿石占着的格不能建；草放建筑时自动清掉）。
//   · 寻路：水 / 挡路的资源 = Not Walkable，沼泽 = 区域 3；生成完立刻重新烘焙 NavMesh，然后 NPC 才生成。
//   · 坐标浮窗：多了"地形 / 资源"两行（TerrainCellInfo）。
//   · 每回合 OnTurn：湖里的鱼慢慢恢复（以后植物生长也放这里）。
// 【地图大小】由 GridManager.mapSize 决定（默认 72×33 块地砖，是原来测试地图的 9 倍）。
//   地图比场景里墙围起来的范围大时，那圈墙是测试用的：自动关掉，在新地图四周外面围一圈墙（replaceTestWalls）。
// 【前后遮挡】镜头改成按 Y 轴排序（ySortWithNpcs）：树 / 石头 / 矿石和 NPC 同一层，谁更靠下谁在前面。
// 场景原来的 ground 地砖层只是不显示了（renderer 关掉），它的碰撞体 / NavMeshModifier 照旧当可行走的底。
// 地形的碰撞体都在 Ignore Raycast 层、是触发器：不挡鼠标点击 / 悬停，也不会推开 NPC。
// ============================================================================
public class TerrainSystem : MonoBehaviour
{
    public static TerrainSystem Instance { get; private set; }

    [Header("种子")]
    [Tooltip("填了就固定用这个种子（测试用）；空 = 用主菜单选的种子")]
    public string overrideSeed = "";

    [Header("营地地砖")]
    [Tooltip("以 NPC 生成点为中心，左右 / 上下各铺多少格地砖（1 格地砖 = 1 世界单位 = 3×3 小格）")]
    public Vector2 campHalfSize = new Vector2(4f, 3f);
    [Tooltip("0 = 中心标记砖（tilmp01），1 = 普通砖（tilmp02），2 = 破损砖（tilmp03）。\n不填：编辑器里自动读 Assets/image 下的三张；打包版退回场景 ground 里用到的砖")]
    public TileBase[] campTiles = new TileBase[3];
    [Range(0f, 0.6f)] public float damagedTileChance = 0.18f;

    [Header("地形贴图（不填 = 程序生成的占位图）")]
    public TileBase soilTile;
    public TileBase swampTile;
    public TileBase waterTile;

    [Header("资源图片（不填 = 先找 Resources/TerrainArt/<类别> 文件夹，再没有就用程序画的占位图；每格可放多张，按变体随机挑）")]
    [Tooltip("图片要求：Pixels Per Unit 决定大小（1 单位 = 1 地砖，推荐 256）；Pivot 设在树干根部 / 石头底部中心。该点会放在占地中心，前后遮挡也按它排。\n" +
             "也可以不填这里，直接把图放进 Assets/Resources/TerrainArt/ 下对应文件夹：Grass / Sapling / Tree / GiantTree / SmallStone / Stone / BigStone / SmallOre / Ore / BigOre")]
    public Sprite[] grassSprites;
    public Sprite[] saplingSprites, treeSprites, giantTreeSprites;
    public Sprite[] smallStoneSprites, stoneSprites, bigStoneSprites;
    public Sprite[] smallOreSprites, oreSprites, bigOreSprites;

    [Header("寻路")]
    [Tooltip("沼泽在 NavMesh 里的区域编号（3 = 第一个自定义区域）")]
    public int swampArea = 3;
    [Tooltip("寻路时走沼泽的代价倍数（沼泽移速只有 20%，5 ≈ 按时间算）。越大越绕开沼泽")]
    public float swampAreaCost = 5f;
    [Tooltip("生成地形后隐藏场景里原来的 ground 地砖层（只关显示，寻路照旧）")]
    public bool hideSceneGround = true;

    [Header("地图边界")]
    [Tooltip("地图比场景里墙围起来的范围大时（那是测试用的小地图）：关掉场景里原来的墙，在新地图四周外面围一圈墙")]
    public bool replaceTestWalls = true;
    [Tooltip("围墙用的砖（不填 = 用场景墙里横着 / 竖着的那两种）")]
    public TileBase borderWallHorizontal;
    public TileBase borderWallVertical;

    [Header("显示")]
    [Tooltip("镜头按 Y 轴排前后：树 / 石头和 NPC 互相遮挡（NPC 走到树后面会被树冠挡住）")]
    public bool ySortWithNpcs = true;

    [Header("水 / 鱼")]
    [Tooltip("每多少格水面养一条鱼（鱼的上限）")]
    public int waterCellsPerFish = 12;
    [Tooltip("每回合（一天）恢复几条鱼")]
    public int fishRegenPerDay = 2;
    [Tooltip("钓一次要多少秒")]
    public float fishSeconds = 8f;
    [Range(0f, 1f)] public float fishChance = 0.6f;
    [Tooltip("打一次水要多少秒、能提回几份（倒进雨水收集器，要净化后才能喝）")]
    public float waterSeconds = 3f;
    public int lakeWaterPerTrip = 4;

    public TerrainMap Map { get; private set; }
    public string Seed { get; private set; }
    public int Fish { get; private set; }
    public int FishMax { get; private set; }

    GridManager _grid;
    ResourceNode[] _resAt;
    Tilemap _ground;
    readonly List<Tilemap> _walls = new List<Tilemap>();
    Grid _sceneGrid, _coarseGrid;
    TileBase _wallH, _wallV;
    bool _border;
    Transform _navRoot, _resRoot;
    TerrainCellInfo _cellInfo;
    bool _rebakePending;

    public static TerrainSystem Ensure()
    {
        if (Instance != null) return Instance;
        TerrainSystem t = FindFirstObjectByType<TerrainSystem>();
        if (t != null) { Instance = t; return t; }
        return new GameObject("TerrainSystem").AddComponent<TerrainSystem>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        if (_cellInfo != null) CellInfo.Unregister(_cellInfo);
    }

    // ============ 生成 ============

    public bool Generate()
    {
        if (Map != null) return true;
        _grid = GridManager.Instance;
        if (_grid == null) { Debug.LogWarning("[Terrain] 场景里没有 GridManager，跳过地形生成。"); return false; }

        FindSceneTilemaps();
        PrepareBorder();
        TerrainLayout layout = BuildLayout();
        Seed = !string.IsNullOrEmpty(MapSeed.Normalize(overrideSeed)) ? MapSeed.Normalize(overrideSeed) : MapSeed.Current;
        Map = TerrainGenerator.Generate(layout, MapSeed.Hash(Seed));
        _resAt = new ResourceNode[Map.width * Map.height];
        FishMax = Map.Count(TerrainType.Water) / Mathf.Max(1, waterCellsPerFish);
        Fish = FishMax;

        BuildGround();
        BuildCampFloor();
        BuildBorderWalls();
        SetupSorting();
        BuildNavSources();
        SpawnResources();
        RebuildNavMeshNow();

        _cellInfo = new TerrainCellInfo();
        CellInfo.Register(_cellInfo);
        MapSeed.SaveLayout(layout);
        Announce();
        return true;
    }

    void FindSceneTilemaps()
    {
        _walls.Clear();
        _ground = null;
        foreach (Tilemap tm in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (tm == null || tm.transform.IsChildOf(transform)) continue;
            NavMeshModifier mod = tm.GetComponent<NavMeshModifier>();
            if (mod != null && mod.overrideArea && mod.area == 1) { _walls.Add(tm); continue; } // 墙：Not Walkable
            if (_ground == null && (mod != null || tm.name.ToLowerInvariant().Contains("ground"))) _ground = tm;
        }
        _sceneGrid = _ground != null ? _ground.layoutGrid : _walls.Count > 0 ? _walls[0].layoutGrid : null;
    }

    TerrainLayout BuildLayout()
    {
        GridManager g = _grid;
        int W = g.gridWidth, H = g.gridHeight, sub = Mathf.Max(1, GridManager.Subdivision);
        TerrainLayout L = new TerrainLayout { width = W, height = H, sub = sub, blocked = new bool[W * H] };

        // 墙：墙 Tilemap 上有砖的小格
        foreach (Tilemap tm in _walls)
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (tm.HasTile(tm.WorldToCell(g.CellToWorld(x, y)))) L.blocked[y * W + x] = true;

        // 营地：以 NPC 生成点为中心，按地砖大格对齐
        Vector3 spawn = SpawnCenter();
        Vector2Int sc = g.WorldToCell(spawn);
        L.spawnX = Mathf.Clamp(sc.x, 0, W - 1);
        L.spawnY = Mathf.Clamp(sc.y, 0, H - 1);
        float coarse = g.cellSize * sub;
        int cx0 = Mathf.RoundToInt((spawn.x - campHalfSize.x - g.origin.x) / coarse);
        int cx1 = Mathf.RoundToInt((spawn.x + campHalfSize.x - g.origin.x) / coarse);
        int cy0 = Mathf.RoundToInt((spawn.y - campHalfSize.y - g.origin.y) / coarse);
        int cy1 = Mathf.RoundToInt((spawn.y + campHalfSize.y - g.origin.y) / coarse);
        if (cx1 <= cx0) cx1 = cx0 + 1;
        if (cy1 <= cy0) cy1 = cy0 + 1;
        L.campX = Mathf.Clamp(cx0 * sub, 0, W);
        L.campY = Mathf.Clamp(cy0 * sub, 0, H);
        L.campW = Mathf.Clamp(cx1 * sub, 0, W) - L.campX;
        L.campH = Mathf.Clamp(cy1 * sub, 0, H) - L.campY;
        return L;
    }

    static Vector3 SpawnCenter()
    {
        NpcSpawner sp = FindFirstObjectByType<NpcSpawner>();
        if (sp != null) return sp.spawnCenter != null ? sp.spawnCenter.position : sp.transform.position;
        if (NpcIdleManager.Instance != null) return NpcIdleManager.Instance.GetCampCenter(Vector3.zero);
        return Vector3.zero;
    }

    // ============ 画出来 ============

    void BuildGround()
    {
        GridManager g = _grid;
        GameObject go = new GameObject("TerrainGround");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(g.origin.x, g.origin.y, 0f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = TerrainArt.GroundSprite(Map, g.cellSize);
        sr.sortingOrder = -3; // 最底层：地形贴图 -3 < 地形 Tile -2 < 营地地砖 / 草 -1 < 建筑 0 < NPC 1

        // Inspector 里填了图片的地形：再盖一层小格 Tilemap（以后换正式美术用）
        if (soilTile == null && swampTile == null && waterTile == null) return;
        GameObject gg = new GameObject("TerrainTiles");
        gg.transform.SetParent(transform, false);
        gg.transform.position = new Vector3(g.origin.x, g.origin.y, 0f);
        Grid grid = gg.AddComponent<Grid>();
        grid.cellSize = new Vector3(g.cellSize, g.cellSize, 0f);
        GameObject tg = new GameObject("Tiles");
        tg.transform.SetParent(gg.transform, false);
        Tilemap tm = tg.AddComponent<Tilemap>();
        TilemapRenderer tr = tg.AddComponent<TilemapRenderer>();
        tr.sortingOrder = -2;
        for (int y = 0; y < Map.height; y++)
            for (int x = 0; x < Map.width; x++)
            {
                if (Map.IsBlocked(x, y)) continue;
                TerrainType t = Map.Get(x, y);
                TileBase tile = t == TerrainType.Soil ? soilTile : t == TerrainType.Swamp ? swampTile : t == TerrainType.Water ? waterTile : null;
                if (tile != null) tm.SetTile(new Vector3Int(x, y, 0), tile);
            }
    }

    void BuildCampFloor()
    {
        GridManager g = _grid;
        TileBase[] tiles = ResolveCampTiles();
        int sub = Map.sub;
        float coarseSize = g.cellSize * sub;

        // 用场景原来的 Grid（和墙、原来的地面对齐）；格子大小对不上就自己建一个
        Grid coarse = _sceneGrid;
        if (coarse == null || Mathf.Abs(coarse.cellSize.x - coarseSize) > 0.01f)
        {
            GameObject cg = new GameObject("TerrainCoarseGrid");
            cg.transform.SetParent(transform, false);
            cg.transform.position = new Vector3(g.origin.x, g.origin.y, 0f);
            coarse = cg.AddComponent<Grid>();
            coarse.cellSize = new Vector3(coarseSize, coarseSize, 0f);
        }
        _coarseGrid = coarse;
        GameObject go = new GameObject("TerrainCampFloor");
        go.transform.SetParent(coarse.transform, false);
        Tilemap tm = go.AddComponent<Tilemap>();
        TilemapRenderer tr = go.AddComponent<TilemapRenderer>();
        tr.sortingOrder = -1;

        int scx = Map.spawnX / sub, scy = Map.spawnY / sub;
        for (int cy = 0; cy * sub < Map.height; cy++)
            for (int cx = 0; cx * sub < Map.width; cx++)
            {
                if (!CoarseIsCamp(cx, cy, sub)) continue;
                TileBase t = tiles[1];
                if (cx == scx && cy == scy && tiles[0] != null) t = tiles[0];                                         // 生成点那块：中心标记砖
                else if (tiles[2] != null && TerrainNoise.Hash01(Map.seed ^ 0x7A11, cx, cy) < damagedTileChance) t = tiles[2]; // 破损砖
                Vector3 center = new Vector3(g.origin.x + (cx + 0.5f) * coarseSize, g.origin.y + (cy + 0.5f) * coarseSize, 0f);
                tm.SetTile(coarse.WorldToCell(center), t);
            }

        if (hideSceneGround && _ground != null)
        {
            TilemapRenderer r = _ground.GetComponent<TilemapRenderer>();
            if (r != null) r.enabled = false;
        }
    }

    // ============ 地图边界 ============

    // 场景里的墙如果只围住了地图中间一小块（测试用的小地图）：关掉它们，记下横墙 / 竖墙用的砖，等会儿在新地图四周围一圈。
    void PrepareBorder()
    {
        _border = false;
        if (!replaceTestWalls || _walls.Count == 0) return;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (Tilemap tm in _walls)
            foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
            {
                if (!tm.HasTile(p)) continue;
                Vector3 w = tm.GetCellCenterWorld(p);
                minX = Mathf.Min(minX, w.x); maxX = Mathf.Max(maxX, w.x);
                minY = Mathf.Min(minY, w.y); maxY = Mathf.Max(maxY, w.y);
            }
        if (minX > maxX) return;
        GridManager g = _grid;
        float gx0 = g.origin.x, gy0 = g.origin.y, gx1 = gx0 + g.gridWidth * g.cellSize, gy1 = gy0 + g.gridHeight * g.cellSize;
        // 地图任何一边比墙多出 2 格以上 → 墙是测试小地图的，换掉；否则保留场景里的墙
        if (!(gx0 < minX - 2f || gx1 > maxX + 2f || gy0 < minY - 2f || gy1 > maxY + 2f)) return;

        // 最上面一行用得最多的砖 = 横墙；最左边一列用得最多的 = 竖墙（拐角也用竖墙，和原来一样）
        Dictionary<TileBase, int> top = new Dictionary<TileBase, int>(), left = new Dictionary<TileBase, int>(), all = new Dictionary<TileBase, int>();
        foreach (Tilemap tm in _walls)
            foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
            {
                TileBase t = tm.GetTile(p);
                if (t == null) continue;
                Vector3 w = tm.GetCellCenterWorld(p);
                Inc(all, t);
                if (Mathf.Abs(w.y - maxY) < 0.01f && w.x > minX + 0.5f && w.x < maxX - 0.5f) Inc(top, t);
                if (Mathf.Abs(w.x - minX) < 0.01f) Inc(left, t);
            }
        _wallH = borderWallHorizontal != null ? borderWallHorizontal : Most(top);
        if (_wallH == null) _wallH = Most(all);
        _wallV = borderWallVertical != null ? borderWallVertical : Most(left);
        if (_wallV == null) _wallV = _wallH;

        foreach (Tilemap tm in _walls) tm.gameObject.SetActive(false); // 显示、碰撞、寻路一起关掉
        _walls.Clear();
        _border = _wallH != null;
        Debug.Log("[Terrain] 地图比场景里的墙大：关掉了测试用的墙，改在新地图四周围一圈。");
    }

    static void Inc(Dictionary<TileBase, int> d, TileBase t)
    {
        d.TryGetValue(t, out int c);
        d[t] = c + 1;
    }

    static TileBase Most(Dictionary<TileBase, int> d)
    {
        TileBase best = null;
        int n = 0;
        foreach (KeyValuePair<TileBase, int> kv in d) if (kv.Value > n) { n = kv.Value; best = kv.Key; }
        return best;
    }

    // 新地图四周外面一圈墙（在网格外，NPC 本来就走不出网格，这圈墙只是让边界看得见）。
    void BuildBorderWalls()
    {
        if (!_border || _coarseGrid == null) return;
        int sub = Map.sub;
        float coarseSize = _grid.cellSize * sub;
        GameObject go = new GameObject("TerrainBorderWalls");
        go.transform.SetParent(_coarseGrid.transform, false);
        Tilemap tm = go.AddComponent<Tilemap>();
        TilemapRenderer tr = go.AddComponent<TilemapRenderer>();
        tr.sortingOrder = 0; // 和原来的墙一样
        int wc = Map.width / sub, hc = Map.height / sub;
        for (int cx = -1; cx <= wc; cx++)
        {
            TileBase t = cx == -1 || cx == wc ? _wallV : _wallH;
            PutWall(tm, cx, -1, t, coarseSize);
            PutWall(tm, cx, hc, t, coarseSize);
        }
        for (int cy = 0; cy < hc; cy++)
        {
            PutWall(tm, -1, cy, _wallV, coarseSize);
            PutWall(tm, wc, cy, _wallV, coarseSize);
        }
    }

    void PutWall(Tilemap tm, int cx, int cy, TileBase t, float coarseSize)
    {
        Vector3 center = new Vector3(_grid.origin.x + (cx + 0.5f) * coarseSize, _grid.origin.y + (cy + 0.5f) * coarseSize, 0f);
        tm.SetTile(_coarseGrid.WorldToCell(center), t);
    }

    // 镜头按 Y 轴排序：同一排序层里越靠下越在前（树 / 石头 / 矿石和 NPC 都在第 1 层）。
    void SetupSorting()
    {
        if (!ySortWithNpcs) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.transparencySortMode = TransparencySortMode.CustomAxis;
        cam.transparencySortAxis = new Vector3(0f, 1f, 0f);
    }

    bool CoarseIsCamp(int cx, int cy, int sub)
    {
        for (int y = cy * sub; y < (cy + 1) * sub; y++)
            for (int x = cx * sub; x < (cx + 1) * sub; x++)
                if (!Map.InBounds(x, y) || Map.IsBlocked(x, y) || Map.Get(x, y) != TerrainType.CampFloor) return false;
        return true;
    }

    TileBase[] ResolveCampTiles()
    {
        TileBase[] r = new TileBase[3];
        if (campTiles != null)
            for (int i = 0; i < 3 && i < campTiles.Length; i++) r[i] = campTiles[i];
#if UNITY_EDITOR
        string[] paths = { "Assets/image/tilmp01_0.asset", "Assets/image/tilmp02_0.asset", "Assets/image/tilmp03_0.asset" };
        for (int i = 0; i < 3; i++)
            if (r[i] == null) r[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<TileBase>(paths[i]);
#endif
        if (r[1] == null && _ground != null)
        {
            // 打包版没在 Inspector 里填：从场景 ground 里找砖（用得最多的当普通砖，第二多的当破损砖）
            Dictionary<TileBase, int> count = new Dictionary<TileBase, int>();
            foreach (Vector3Int p in _ground.cellBounds.allPositionsWithin)
            {
                TileBase t = _ground.GetTile(p);
                if (t == null) continue;
                count.TryGetValue(t, out int c);
                count[t] = c + 1;
            }
            List<KeyValuePair<TileBase, int>> sorted = new List<KeyValuePair<TileBase, int>>(count);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (sorted.Count > 0) r[1] = sorted[0].Key;
            if (sorted.Count > 1 && r[2] == null) r[2] = sorted[1].Key;
        }
        if (r[1] == null)
        {
            Debug.LogWarning("[Terrain] 找不到营地地砖图片：营地画成灰色方块。可以在场景里挂一个 TerrainSystem，把三张地砖拖进 Camp Tiles。");
            r[1] = MakeColorTile(new Color32(196, 196, 196, 255));
        }
        return r;
    }

    static TileBase MakeColorTile(Color32 c)
    {
        Texture2D t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        Color32[] px = new Color32[16];
        for (int i = 0; i < 16; i++) px[i] = c;
        t.SetPixels32(px);
        t.Apply(false);
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        return tile;
    }

    // ============ 寻路 ============

    void BuildNavSources()
    {
        GridManager g = _grid;
        _navRoot = new GameObject("TerrainNav").transform;
        _navRoot.SetParent(transform, false);
        // 整张地图一层可行走的底（场景原来的 ground 已经有；这里再铺一层，以后地图扩大也不用补地砖）
        AddNavBox("Walkable", 0, 0, g.gridWidth, g.gridHeight, 0);
        foreach (CellRect r in Rects(TerrainType.Water)) AddNavBox("Water", r.x, r.y, r.w, r.h, 1);           // 1 = Not Walkable
        foreach (CellRect r in Rects(TerrainType.Swamp)) AddNavBox("Swamp", r.x, r.y, r.w, r.h, swampArea);
    }

    void AddNavBox(string name, int x, int y, int w, int h, int area)
    {
        GameObject go = new GameObject(name);
        go.layer = 2; // Ignore Raycast
        go.transform.SetParent(_navRoot, false);
        float cs = _grid.cellSize;
        go.transform.position = new Vector3(_grid.origin.x + (x + w * 0.5f) * cs, _grid.origin.y + (y + h * 0.5f) * cs, 0f);
        BoxCollider2D b = go.AddComponent<BoxCollider2D>();
        b.isTrigger = true;
        b.size = new Vector2(w * cs, h * cs);
        NavMeshModifier mod = go.AddComponent<NavMeshModifier>();
        mod.overrideArea = area != 0;
        mod.area = area;
    }

    struct CellRect { public int x, y, w, h; }

    // 把某种地形的格子合并成尽量少的矩形（一个矩形一个碰撞体）。
    List<CellRect> Rects(TerrainType t)
    {
        int W = Map.width, H = Map.height;
        bool[] used = new bool[W * H];
        List<CellRect> list = new List<CellRect>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (used[y * W + x] || !Is(x, y, t)) continue;
                int w = 1;
                while (x + w < W && !used[y * W + x + w] && Is(x + w, y, t)) w++;
                int h = 1;
                while (y + h < H)
                {
                    bool row = true;
                    for (int xx = x; xx < x + w; xx++)
                        if (used[(y + h) * W + xx] || !Is(xx, y + h, t)) { row = false; break; }
                    if (!row) break;
                    h++;
                }
                for (int yy = y; yy < y + h; yy++)
                    for (int xx = x; xx < x + w; xx++) used[yy * W + xx] = true;
                list.Add(new CellRect { x = x, y = y, w = w, h = h });
            }
        return list;
    }

    bool Is(int x, int y, TerrainType t) => !Map.IsBlocked(x, y) && Map.Get(x, y) == t;

    NavMeshSurface Surface
    {
        get
        {
            if (BuildManager.Instance != null && BuildManager.Instance.navMeshSurface != null) return BuildManager.Instance.navMeshSurface;
            return FindFirstObjectByType<NavMeshSurface>();
        }
    }

    void RebuildNavMeshNow()
    {
        NavMesh.SetAreaCost(swampArea, Mathf.Max(1f, swampAreaCost));
        NavMeshSurface s = Surface;
        if (s == null) { Debug.LogWarning("[Terrain] 场景里没有 NavMeshSurface：地形不会影响寻路。"); return; }
        Physics2D.SyncTransforms();
        s.BuildNavMesh(); // 同步烘焙：NPC 接下来就在新的导航网格上生成
    }

    // 挡路的资源采完后：稍等一下重新烘焙（同一时间采完好几个只烘焙一次）。
    public void RequestRebake()
    {
        if (_rebakePending || !isActiveAndEnabled) return;
        StartCoroutine(RebakeSoon());
    }

    IEnumerator RebakeSoon()
    {
        _rebakePending = true;
        yield return new WaitForSeconds(0.3f);
        _rebakePending = false;
        NavMeshSurface s = Surface;
        if (s == null) yield break;
        Physics2D.SyncTransforms();
        // 在原来的导航网格上异步更新（BuildNavMeshAsync 会先清空网格，更新完之前 NPC 会掉出导航网格）
        if (s.navMeshData != null) s.UpdateNavMesh(s.navMeshData);
        else s.BuildNavMesh();
    }

    // ============ 资源 ============

    void SpawnResources()
    {
        _resRoot = new GameObject("TerrainResources").transform;
        _resRoot.SetParent(transform, false);
        foreach (ResourceSpawn s in Map.resources)
            Occupy(ResourceNode.Spawn(s, _resRoot, _grid), true);
    }

    void Occupy(ResourceNode n, bool on)
    {
        if (n == null || _resAt == null) return;
        for (int y = n.origin.y; y < n.origin.y + n.size; y++)
            for (int x = n.origin.x; x < n.origin.x + n.size; x++)
            {
                if (!Map.InBounds(x, y)) continue;
                int i = y * Map.width + x;
                if (on) _resAt[i] = n;
                else if (_resAt[i] == n) _resAt[i] = null;
            }
    }

    // Inspector 里填了资源图片就用它（没填返回 null，用程序画的占位图）。
    public Sprite CustomResourceSprite(ResourceKind kind, int size, int variant)
    {
        Sprite[] arr;
        switch (kind)
        {
            case ResourceKind.Tree: arr = size <= 1 ? saplingSprites : size == 2 ? treeSprites : giantTreeSprites; break;
            case ResourceKind.Stone: arr = size <= 1 ? smallStoneSprites : size == 2 ? stoneSprites : bigStoneSprites; break;
            case ResourceKind.Ore: arr = size <= 1 ? smallOreSprites : size == 2 ? oreSprites : bigOreSprites; break;
            default: arr = grassSprites; break;
        }
        if (arr == null || arr.Length == 0) arr = FolderSprites(kind, size);
        if (arr == null || arr.Length == 0) return null;
        int start = ((variant % arr.Length) + arr.Length) % arr.Length;
        for (int i = 0; i < arr.Length; i++)
        {
            Sprite s = arr[(start + i) % arr.Length];
            if (s != null) return s;
        }
        return null;
    }

    // Resources/TerrainArt/<类别> 里的图（按文件名排序，Sapling_01、Sapling_02……），每类只读一次。
    static readonly Dictionary<int, Sprite[]> _folderSprites = new Dictionary<int, Sprite[]>();
    static Sprite[] FolderSprites(ResourceKind kind, int size)
    {
        size = Mathf.Clamp(size, 1, 3);
        int key = (int)kind * 4 + size;
        if (_folderSprites.TryGetValue(key, out Sprite[] cached)) return cached;
        string folder;
        switch (kind)
        {
            case ResourceKind.Tree: folder = size <= 1 ? "Sapling" : size == 2 ? "Tree" : "GiantTree"; break;
            case ResourceKind.Stone: folder = size <= 1 ? "SmallStone" : size == 2 ? "Stone" : "BigStone"; break;
            case ResourceKind.Ore: folder = size <= 1 ? "SmallOre" : size == 2 ? "Ore" : "BigOre"; break;
            default: folder = "Grass"; break;
        }
        Sprite[] arr = Resources.LoadAll<Sprite>("TerrainArt/" + folder);
        if (arr != null && arr.Length > 1) System.Array.Sort(arr, (a, b) => string.CompareOrdinal(a.name, b.name));
        _folderSprites[key] = arr;
        if (arr != null && arr.Length > 0)
            Debug.Log("[TerrainSystem] 读到 TerrainArt/" + folder + " 的图 " + arr.Length + " 张：" + arr[0].name +
                      "（" + arr[0].rect.width + "×" + arr[0].rect.height + "，PPU " + arr[0].pixelsPerUnit + "，世界尺寸 " + arr[0].bounds.size + "）");
        return arr;
    }

    // ============ 查询（没有地形系统时都返回"普通土地"的结果，别的系统可以放心调）============

    public static bool HasMap => Instance != null && Instance.Map != null;

    public static TerrainType TypeAt(Vector2Int c)
    {
        TerrainSystem t = Instance;
        if (t == null || t.Map == null || !t.Map.InBounds(c.x, c.y)) return TerrainType.Soil;
        return t.Map.Get(c.x, c.y);
    }

    public static TerrainType TypeAtWorld(Vector3 p) => HasMap && Instance._grid != null ? TypeAt(Instance._grid.WorldToCell(p)) : TerrainType.Soil;

    public static bool IsWall(Vector2Int c)
    {
        TerrainSystem t = Instance;
        return t != null && t.Map != null && t.Map.InBounds(c.x, c.y) && t.Map.IsBlocked(c.x, c.y);
    }

    // 脚下地形给的移速倍率（NpcMover 用）。
    public static float SpeedFactorAt(Vector3 p)
    {
        TerrainSystem t = Instance;
        if (t == null || t.Map == null || t._grid == null) return 1f;
        Vector2Int c = t._grid.WorldToCell(p);
        if (!t.Map.InBounds(c.x, c.y) || t.Map.IsBlocked(c.x, c.y)) return 1f;
        TerrainType tt = t.Map.Get(c.x, c.y);
        return tt == TerrainType.Water ? 1f : TerrainInfo.SpeedFactor(tt); // 水上本来走不了；贴着岸时别把速度变成 0
    }

    // 这一格能不能建造（GridManager.IsBuildable 调）：墙、水、沼泽、树 / 石头 / 矿石占着 → 不能。
    public static bool BlocksBuilding(int x, int y)
    {
        TerrainSystem t = Instance;
        if (t == null || t.Map == null || !t.Map.InBounds(x, y)) return false;
        if (t.Map.IsBlocked(x, y)) return true;
        if (!TerrainInfo.Buildable(t.Map.Get(x, y))) return true;
        ResourceNode r = t._resAt[y * t.Map.width + x];
        return r != null && r.kind != ResourceKind.Grass;
    }

    // 闲逛 / 随机找落点时尽量别选的地方（沼泽：走进去就慢得要命）。
    public static bool AvoidForWander(Vector3 p) => TypeAtWorld(p) == TerrainType.Swamp;

    public static ResourceNode ResourceAt(Vector2Int c)
    {
        TerrainSystem t = Instance;
        if (t == null || t.Map == null || t._resAt == null || !t.Map.InBounds(c.x, c.y)) return null;
        ResourceNode r = t._resAt[c.y * t.Map.width + c.x];
        return r != null && !r.depleted ? r : null;
    }

    // 放建筑时清掉这块地上的草。
    public static void ClearGrass(Vector2Int origin, Vector2Int size)
    {
        for (int y = origin.y; y < origin.y + size.y; y++)
            for (int x = origin.x; x < origin.x + size.x; x++)
            {
                ResourceNode r = ResourceAt(new Vector2Int(x, y));
                if (r != null && r.kind == ResourceKind.Grass) r.Deplete();
            }
    }

    public static void OnResourceRemoved(ResourceNode n)
    {
        TerrainSystem t = Instance;
        if (t == null || n == null) return;
        t.Occupy(n, false);
        if (n.Blocks) t.RequestRebake();
    }

    // 离 from 最近的某种资源（别人正在采的跳过；near 有值时只找那个坐标附近 radius 格内的）。优先选走得到的。
    public static ResourceNode FindNearest(ResourceKind kind, Vector3 from, string npcId, Vector2Int? near = null, int radius = 4)
    {
        List<KeyValuePair<float, ResourceNode>> cands = new List<KeyValuePair<float, ResourceNode>>();
        foreach (ResourceNode r in ResourceNode.All)
        {
            if (r == null || r.depleted || r.kind != kind || r.IsTakenByOther(npcId)) continue;
            float d;
            if (near.HasValue)
            {
                Vector2Int c = near.Value;
                int dx = Mathf.Max(0, Mathf.Max(r.origin.x - c.x, c.x - (r.origin.x + r.size - 1)));
                int dy = Mathf.Max(0, Mathf.Max(r.origin.y - c.y, c.y - (r.origin.y + r.size - 1)));
                int cheb = Mathf.Max(dx, dy);
                if (cheb > radius) continue;
                d = cheb * 100f + (r.Center - from).sqrMagnitude * 0.01f;
            }
            else d = (r.Center - from).sqrMagnitude;
            cands.Add(new KeyValuePair<float, ResourceNode>(d, r));
        }
        if (cands.Count == 0) return null;
        cands.Sort((a, b) => a.Key.CompareTo(b.Key));
        for (int i = 0; i < cands.Count && i < 6; i++)
            if (NpcNav.Reachable(from, cands[i].Value.StandPoint(from))) return cands[i].Value;
        return cands[0].Value;
    }

    // 离 from 最近、走得到的岸边（打水 / 钓鱼站的地方）。water = 旁边那格水的位置（面朝它）。
    public static bool TryFindShore(Vector3 from, out Vector3 stand, out Vector3 water)
    {
        stand = from;
        water = from;
        TerrainSystem t = Instance;
        if (t == null || t.Map == null || t._grid == null) return false;
        TerrainMap m = t.Map;
        GridManager g = t._grid;
        List<KeyValuePair<float, int>> cands = new List<KeyValuePair<float, int>>();
        for (int y = 0; y < m.height; y++)
            for (int x = 0; x < m.width; x++)
            {
                int i = y * m.width + x;
                if (m.IsBlocked(x, y) || m.cells[i] == TerrainType.Water) continue;
                ResourceNode r = t._resAt[i];
                if (r != null && r.Blocks) continue;
                if (WaterNeighbor(m, x, y) < 0) continue;
                cands.Add(new KeyValuePair<float, int>((g.CellToWorld(x, y) - from).sqrMagnitude, i));
            }
        cands.Sort((a, b) => a.Key.CompareTo(b.Key));
        int tries = 0;
        foreach (KeyValuePair<float, int> c in cands)
        {
            int x = c.Value % m.width, y = c.Value / m.width;
            if (!NpcNav.Sample(g.CellToWorld(x, y), 0.6f, out Vector3 p)) continue;
            if (++tries > 10) break;
            if (!NpcNav.Reachable(from, p)) continue;
            int wi = WaterNeighbor(m, x, y);
            stand = p;
            water = g.CellToWorld(wi % m.width, wi / m.width);
            return true;
        }
        return false;
    }

    static int WaterNeighbor(TerrainMap m, int x, int y)
    {
        int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
        for (int d = 0; d < 4; d++)
        {
            int xx = x + dx[d], yy = y + dy[d];
            if (m.InBounds(xx, yy) && !m.IsBlocked(xx, yy) && m.Get(xx, yy) == TerrainType.Water) return yy * m.width + xx;
        }
        return -1;
    }

    // 指令里的坐标 "x,y" → 格子（没有 / 认不出返回 null）。
    public static Vector2Int? ParseCell(string dest)
    {
        if (string.IsNullOrEmpty(dest) || GridManager.Instance == null) return null;
        if (dest[0] == '@') return null;
        if (GridManager.Instance.LabelToCell(dest, out Vector2Int c)) return c;
        return null;
    }

    // ============ 每回合 / 鱼 ============

    // WorldManager.RunOneTurnResolve 调：湖里的鱼慢慢恢复。以后"土地长植物"（草重新长出来、树苗长大）也放这里。
    public static void OnTurn(WorldState w)
    {
        TerrainSystem t = Instance;
        if (t == null || t.Map == null) return;
        t.Fish = Mathf.Min(t.FishMax, t.Fish + Mathf.Max(0, t.fishRegenPerDay));
    }

    // 钓一次：湖里有鱼、运气好才钓得到。
    public static bool TryCatchFish()
    {
        TerrainSystem t = Instance;
        if (t == null || t.Fish <= 0) return false;
        if (Random.value > t.fishChance) return false;
        t.Fish--;
        return true;
    }

    // ============ 采集指令（NpcCommands 用）============

    // 命令内容（显示在对话窗口，也交给 LLM 让 NPC 用自己的口吻回应）。
    public static string HarvestOrder(string res, string dest, bool jp)
    {
        string where = ParseCell(dest).HasValue ? "(" + dest + ")" : "";
        switch (res)
        {
            case ResourceInfo.WoodItem: return jp ? where + "木を切りに行く" : "去" + where + "砍树";
            case ResourceInfo.StoneItem: return jp ? where + "石を採りに行く" : "去" + where + "采石头";
            case ResourceInfo.OreItem: return jp ? where + "鉱石を掘りに行く" : "去" + where + "挖矿";
            case ResourceInfo.FiberItem: return jp ? where + "草を刈りに行く" : "去" + where + "割草";
            case "fish": return jp ? "湖で釣りをする" : "去湖边钓鱼";
            case "water": return jp ? "湖で水を汲んで、雨水タンクに入れる" : "去湖边打水，倒进雨水收集器";
        }
        return jp ? "採集に行く" : "去采集";
    }

    // 做不到的原因（做得到返回 null）。
    public static string HarvestProblem(NpcData actor, string res, string dest, bool jp)
    {
        TerrainSystem t = Instance;
        if (t == null || t.Map == null) return jp ? "この辺りの地形がまだ分からない" : "还不清楚这附近的地形";
        if (res == "fish" || res == "water")
        {
            if (t.Map.Count(TerrainType.Water) == 0) return jp ? "近くに水辺がない" : "附近没有水";
            if (res == "fish" && t.Fish <= 0) return jp ? "湖の魚はしばらく釣れそうにない" : "湖里的鱼暂时钓不到了，过几天再来";
            if (res == "water")
            {
                if (WaterWorks.CollectorCount == 0) return jp ? "汲んだ水を入れる雨水タンクがない（湖の水は浄化しないと飲めない）" : "没有雨水收集器装水（湖水要倒进收集器、经净化器净化才能喝）";
                if (WaterWorks.FreestCollector() == null) return jp ? "雨水タンクがもう満杯" : "雨水收集器都满了";
            }
            return null;
        }
        if (!ResourceInfo.TryParseKind(res, out ResourceKind kind)) return jp ? "何を採ればいいか分からない" : "不知道要采什么";
        NpcMover m = actor != null ? NpcView.GetMover(actor.id) : null;
        Vector3 from = m != null ? m.transform.position : NpcNav.CampCenter(Vector3.zero);
        Vector2Int? near = ParseCell(dest);
        if (FindNearest(kind, from, actor != null ? actor.id : null, near) != null) return null;
        string what = ResourceInfo.KindName(kind, jp);
        if (near.HasValue) return jp ? "そこには" + what + "がない" : "那里没有" + what;
        return jp ? "近くに採れる" + what + "がない" : "附近没有能采的" + what;
    }

    // ============ 事件栏 ============

    void Announce()
    {
        bool jp = LanguageGuard.IsJp;
        string msg = (jp ? "マップシード：" : "地图种子：") + Seed + (jp ? "（地形：" : "（地貌：") + TerrainInfo.FlavorName(Map.flavor, jp) + "）";
        Debug.Log("[Terrain] " + msg + "  " + TerrainArt.Summary(Map, jp).Replace("\n", "  "));
        try
        {
            if (EventLogManager.Instance != null && WorldManager.Instance != null && WorldManager.Instance.world != null)
                EventLogManager.Instance.AddEvent(WorldManager.Instance.world.day,
                    msg + (jp ? "　メインメニューで同じシードを入れると、同じマップで遊べる。" : "　在主菜单输入同一个种子，可以再玩这张地图。"));
        }
        catch (System.Exception e) { Debug.LogWarning("[Terrain] " + e.Message); }
    }
}

// 坐标浮窗里的"地形 / 资源"两行。
public class TerrainCellInfo : ICellInfoProvider
{
    public int Order => 10; // 排在坐标（0）后面

    public void Append(Vector2Int cell, CellInfoBuilder b, bool jp)
    {
        TerrainSystem t = TerrainSystem.Instance;
        if (t == null || t.Map == null || !t.Map.InBounds(cell.x, cell.y)) return;
        string label = jp ? "地形" : "地形";
        if (t.Map.IsBlocked(cell.x, cell.y)) { b.Line(label, jp ? "壁" : "墙", "#C8C8C8"); return; }

        TerrainType tt = t.Map.Get(cell.x, cell.y);
        string name = TerrainInfo.Name(tt, jp), effect = null, color = null;
        switch (tt)
        {
            case TerrainType.Soil: effect = jp ? "移動 -10%" : "移速 -10%"; color = "#C8B48A"; break;
            case TerrainType.Swamp: effect = jp ? "移動 -80%・建設不可" : "移速 -80% · 不能建造"; color = "#E0A060"; break;
            case TerrainType.Water: effect = jp ? "通行不可・水汲み / 釣り" : "不可通行 · 可打水 / 钓鱼"; color = "#7FC8FF"; break;
        }
        b.Line(label, effect != null ? name + "  <size=85%><color=" + color + ">" + effect + "</color></size>" : name);

        ResourceNode r = TerrainSystem.ResourceAt(cell);
        if (r != null)
        {
            string item = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(r.YieldItem) : r.YieldItem;
            b.Line(jp ? "資源" : "资源", r.DisplayName(jp) + "  <size=85%><color=#9BE29B>" + item + " ×" + r.YieldAmount + "</color></size>");
        }
    }
}
