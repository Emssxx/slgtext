using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using NavMeshPlus.Components;   // NavMeshModifier：摆设（铁桌子）占的地方标成"不能走"

// ============================================================================
// 区域导演（新地图规则，第 2 步）：营地外是永久的雾，"地图"靠区域一块一块出现。
//   · 区域 = 紧贴营地的一块长方形。生成时这块地的雾散开（FogManager.ClearRegion），
//     里面按种类放资源 / 敌人；过 lifetimeDays 天后雾合上，区域消失（没采完的资源、还活着的敌人一起消失）。
//   · 四种：废弃营地（资源多，没有敌人）/ 怪物巢穴（一群造物 + 矿）/ 其他人类营地（拾荒者 + 木头石头）/
//           ABA 临时指挥部（一个 ABA 清除小组 + 矿）。
//   · 资源只来自区域：旧的整张地图地形生成已经关掉（TerrainSystem.legacyTerrainGeneration）。
//   · 谁决定生成：
//       ① AI 导演（14B）：能生成时，事件类型清单里多出 region_xxx 四种，它选了就生成（AIDirector → EventExecutor → 这里）。
//          AI 只是从清单里选一种，放在哪、放什么全是这里的代码决定（符合"AI 选、代码执行"）。
//       ② 代码保底：每天开始时按 dailyChance 掷一次；连续 forceAfterDays 天没有区域就一定生成一个。
//   · 安全期内（EnemyDirector.safeDays）只出废弃营地，不出带敌人的区域。
// 【生成规则是先定的一版默认值，都在 Inspector 里能改】场景里不用摆：WorldManager 开局自动建一个；
//   想改参数就在场景里建个空物体挂上本脚本（会优先用它）。
// 测试键：F11 = 马上生成一个（Shift+F11 = 让现有的区域马上消失）。原来是 F9，和 AI 调度器调试面板撞键，改到 F11。
// ============================================================================
public enum RegionType { AbandonedCamp, MonsterNest, HumanCamp, AbaPost }

public class RegionDirector : MonoBehaviour
{
    public static RegionDirector Instance { get; private set; }

    [Header("总开关")]
    public bool regionsEnabled = true;

    [Header("什么时候生成 / 消失")]
    [Tooltip("第几天起才会有区域")]
    public int firstRegionDay = 2;
    [Tooltip("同时最多有几个区域")]
    public int maxActiveRegions = 1;
    [Tooltip("上一个区域消失后，至少隔几天才出下一个")]
    public int minGapDays = 1;
    [Tooltip("每天开始时，代码自己生成一个区域的概率（AI 导演另外也能选）")]
    [Range(0f, 1f)] public float dailyChance = 0.6f;
    [Tooltip("连续这么多天没有区域 → 一定生成一个（保底）")]
    public int forceAfterDays = 3;
    [Tooltip("区域存在几天后消失")]
    public int lifetimeDays = 2;

    [Header("各种区域：最早第几天出现 / 被抽中的权重（0 = 不出）")]
    public float abandonedCampWeight = 4f;
    public int monsterNestMinDay = 3;
    public float monsterNestWeight = 2f;
    public int humanCampMinDay = 3;
    public float humanCampWeight = 2f;
    public int abaPostMinDay = 8;
    public float abaPostWeight = 1f;
    [Tooltip("开 = 安全期内只出废弃营地（带敌人的三种要等安全期过了）")]
    public bool hostileOnlyAfterSafePeriod = true;

    [Header("大小（大格，1 大格 = 1 块地砖）")]
    public Vector2Int abandonedCampSize = new Vector2Int(8, 6);
    public Vector2Int monsterNestSize = new Vector2Int(7, 7);
    public Vector2Int humanCampSize = new Vector2Int(8, 6);
    public Vector2Int abaPostSize = new Vector2Int(6, 5);

    [Header("测试")]
    [Tooltip("F11 马上生成一个区域；Shift+F11 让现有的区域马上消失")]
    public bool debugKeys = true;

    // 一个正在存在的区域。
    public class Region
    {
        public RegionType type;
        public RectInt cells;                 // 范围（小格）
        public string side;                   // 在营地的哪一边：north / south / east / west
        public int fogId;                     // FogManager.ClearRegion 给的编号（消失时用它把雾盖回去）
        public int bornDay, endDay;           // 第几天出现、第几天消失
        public bool warned;                   // 已经提醒过"快消失了"
        public GameObject ground;             // 地面上那层淡淡的底色
        public readonly List<ResourceNode> nodes = new List<ResourceNode>();
        public readonly List<Enemy> enemies = new List<Enemy>();
        public readonly List<GameObject> decor = new List<GameObject>();          // 摆设（铁桌子之类），区域消失时一起拆
        public readonly List<RectInt> decorCells = new List<RectInt>();           // 摆设占的格子，区域消失时还回去
    }

    public readonly List<Region> Active = new List<Region>();

    int _lastDay = -1;          // 上一帧是第几天（用来发现"新的一天开始了"）
    int _lastEndDay = -999;     // 上一个区域是第几天消失的
    int _lastSpawnDay = -999;   // 上一个区域是第几天生成的

    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;

    public static RegionDirector Ensure()
    {
        if (Instance != null) return Instance;
        RegionDirector d = FindFirstObjectByType<RegionDirector>();
        if (d != null) { Instance = d; return d; }
        return new GameObject("RegionDirector").AddComponent<RegionDirector>();
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

    // ============ 每帧：只看"是不是新的一天" ============

    void Update()
    {
        WorldState w = World;
        if (w == null || OpeningDirector.Playing) return;   // 开场演出期间世界静止，不生成

        if (w.day != _lastDay)
        {
            if (_lastDay >= 0) OnNewDay(w);   // 第一次只是记下今天是第几天
            _lastDay = w.day;
        }
        if (debugKeys) DebugKeys(w);
    }

    void OnNewDay(WorldState w)
    {
        // 1. 到期的区域消失；还剩一天的提醒一句
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            Region r = Active[i];
            if (w.day >= r.endDay) Expire(r, w);
            else if (!r.warned && w.day >= r.endDay - 1 && r.endDay - r.bornDay >= 2)
            {
                r.warned = true;
                AddEvent(w, LF("region_event_fading", CampZone.SideName(r.side, LanguageGuard.IsJp), Name(r.type)));
            }
        }

        // 2. 代码保底：按概率 / 太久没有 → 生成一个
        if (!CanSpawn(w)) return;
        bool overdue = w.day - Mathf.Max(_lastSpawnDay, firstRegionDay - 1) >= Mathf.Max(1, forceAfterDays);
        if (overdue || Random.value < dailyChance)
            TrySpawn(PickType(w), w, true);
    }

    // ============ 能不能生成 / 生成哪种 ============

    // 现在能不能生成一个新区域。
    public bool CanSpawn(WorldState w)
    {
        if (!regionsEnabled || w == null) return false;
        if (FogManager.Instance == null || !FogManager.Instance.permanentFog) return false;   // 没开永久雾就没有区域这回事
        if (!TerrainSystem.HasMap || GridManager.Instance == null) return false;
        if (w.day < firstRegionDay) return false;
        if (Active.Count >= Mathf.Max(1, maxActiveRegions)) return false;
        if (w.day - _lastEndDay < minGapDays) return false;
        return true;
    }

    // 这种区域现在允不允许出（天数、安全期、敌人系统开没开）。
    bool Allowed(RegionType t, WorldState w)
    {
        if (Weight(t) <= 0f) return false;
        if (t == RegionType.AbandonedCamp) return true;
        if (EnemyDirector.Instance == null || !EnemyDirector.Instance.enemiesEnabled) return false;   // 敌人系统关着：不出带敌人的
        if (hostileOnlyAfterSafePeriod && EnemyDirector.IsSafePeriod) return false;
        switch (t)
        {
            case RegionType.MonsterNest: return w.day >= monsterNestMinDay;
            case RegionType.HumanCamp: return w.day >= humanCampMinDay;
            default: return w.day >= abaPostMinDay;
        }
    }

    float Weight(RegionType t)
    {
        switch (t)
        {
            case RegionType.AbandonedCamp: return abandonedCampWeight;
            case RegionType.MonsterNest: return monsterNestWeight;
            case RegionType.HumanCamp: return humanCampWeight;
            default: return abaPostWeight;
        }
    }

    Vector2Int SizeOf(RegionType t)
    {
        switch (t)
        {
            case RegionType.AbandonedCamp: return abandonedCampSize;
            case RegionType.MonsterNest: return monsterNestSize;
            case RegionType.HumanCamp: return humanCampSize;
            default: return abaPostSize;
        }
    }

    // 按权重随机挑一种现在允许出的（都不允许时退回废弃营地）。
    RegionType PickType(WorldState w)
    {
        float total = 0f;
        for (int i = 0; i < 4; i++) if (Allowed((RegionType)i, w)) total += Weight((RegionType)i);
        if (total <= 0f) return RegionType.AbandonedCamp;
        float roll = Random.value * total;
        for (int i = 0; i < 4; i++)
        {
            RegionType t = (RegionType)i;
            if (!Allowed(t, w)) continue;
            roll -= Weight(t);
            if (roll <= 0f) return t;
        }
        return RegionType.AbandonedCamp;
    }

    // ============ AI 导演那边 ============

    static string TypeId(RegionType t)
    {
        switch (t)
        {
            case RegionType.AbandonedCamp: return "region_abandoned_camp";
            case RegionType.MonsterNest: return "region_monster_nest";
            case RegionType.HumanCamp: return "region_human_camp";
            default: return "region_aba_post";
        }
    }

    // 区域的名字（当前语言）。
    public static string Name(RegionType t) => L("region_name_" + TypeId(t).Substring("region_".Length));

    // 给 AI 导演的提示词：现在能生成区域时，事件类型清单里多出这几种（不能生成时返回空串，导演就选不到）。
    public static string DirectorTypeLines(bool jp)
    {
        RegionDirector d = Instance;
        WorldState w = World;
        if (d == null || w == null || !d.CanSpawn(w)) return "";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        if (d.Allowed(RegionType.AbandonedCamp, w))
            sb.Append(jp ? "  region_abandoned_camp（拠点のそばの霧が晴れ、廃棄された拠点が現れる：資源が残っている）\n"
                         : "  region_abandoned_camp（营地旁的雾散开，露出一处废弃营地：里面有能用的资源）\n");
        if (d.Allowed(RegionType.MonsterNest, w))
            sb.Append(jp ? "  region_monster_nest（拠点のそばの霧が晴れ、怪物の巣が現れる：危険だが鉱石がある）\n"
                         : "  region_monster_nest（营地旁的雾散开，露出一个怪物巢穴：危险，但有矿石）\n");
        if (d.Allowed(RegionType.HumanCamp, w))
            sb.Append(jp ? "  region_human_camp（拠点のそばの霧が晴れ、他の生存者の拠点が現れる）\n"
                         : "  region_human_camp（营地旁的雾散开，露出另一伙幸存者的营地）\n");
        if (d.Allowed(RegionType.AbaPost, w))
            sb.Append(jp ? "  region_aba_post（拠点のそばの霧が晴れ、ABA の臨時指揮所が現れる：武装部隊がいる）\n"
                         : "  region_aba_post（营地旁的雾散开，露出 ABA 的临时指挥部：有武装人员）\n");
        return sb.ToString();
    }

    // AI 导演选了一种区域（EventExecutor 调）。代码照样检查一遍：现在不能生成 / 这种不允许，就不生成（只留下导演那句描述）。
    public bool TriggerFromDirector(string typeId)
    {
        WorldState w = World;
        if (w == null || !CanSpawn(w)) return false;
        for (int i = 0; i < 4; i++)
        {
            RegionType t = (RegionType)i;
            if (TypeId(t) != typeId) continue;
            if (!Allowed(t, w)) return false;
            return TrySpawn(t, w, false);   // 事件编年史由 EventExecutor 统一记，这里不重复记
        }
        return false;
    }

    // ============ 生成 ============

    // 生成一个区域。record = 自己记进事件编年史（从 EventExecutor 来的由它记）。找不到地方放就返回 false。
    public bool TrySpawn(RegionType type, WorldState w, bool record)
    {
        if (w == null || FogManager.Instance == null || GridManager.Instance == null) return false;
        if (!FindSpot(type, out RectInt cells, out string side)) return false;

        Region r = new Region { type = type, cells = cells, side = side, bornDay = w.day, endDay = w.day + Mathf.Max(1, lifetimeDays) };
        r.fogId = FogManager.Instance.ClearRegion(cells);   // 这块地的雾散开
        r.ground = MakeGround(r);
        Populate(r, w);
        Active.Add(r);
        _lastSpawnDay = w.day;

        string text = LF("region_event_appear", CampZone.SideName(side, LanguageGuard.IsJp), Name(type));
        AddEvent(w, text);
        if (HudToasts.Instance != null) HudToasts.Instance.Show("region : " + TypeId(type).Substring("region_".Length), L("region_toast_appear"), Name(type));
        if (record) EventChronicle.Record("other", text, null);
        if (type != RegionType.AbandonedCamp) NpcSecurity.NotifyDanger(8f);   // 旁边冒出危险的地方：大家心里不踏实
        Debug.Log("[Region] 生成 " + type + " @ " + cells + "（营地" + side + "），第 " + r.endDay + " 天消失");
        return true;
    }

    // 找一块紧贴营地的空地：四个方向随机排个顺序，挨个试。
    bool FindSpot(RegionType type, out RectInt cells, out string side)
    {
        cells = new RectInt();
        side = "east";
        GridManager g = GridManager.Instance;
        RectInt camp = FogManager.Instance.CampRect;
        if (camp.width <= 0 || camp.height <= 0) return false;   // 营地范围还没算出来
        int sub = Mathf.Max(1, GridManager.Subdivision);
        Vector2Int big = SizeOf(type);
        int w = Mathf.Max(2, big.x) * sub, h = Mathf.Max(2, big.y) * sub;

        string[] sides = { "north", "south", "east", "west" };
        for (int i = sides.Length - 1; i > 0; i--)   // 洗牌
        {
            int j = Random.Range(0, i + 1);
            string tmp = sides[i]; sides[i] = sides[j]; sides[j] = tmp;
        }

        foreach (string s in sides)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                RectInt r;
                if (s == "east" || s == "west")
                {
                    // 左右两边：竖着可以上下错开一点，但要和营地的边至少重叠一半（保证连着、走得过去）
                    int slide = Mathf.Max(0, (camp.height + h) / 2 - Mathf.Min(camp.height, h) / 2 - 1) / 2;
                    int y = camp.yMin + (camp.height - h) / 2 + Random.Range(-slide, slide + 1);
                    int x = s == "east" ? camp.xMax : camp.xMin - w;
                    r = new RectInt(x, y, w, h);
                }
                else
                {
                    int slide = Mathf.Max(0, (camp.width + w) / 2 - Mathf.Min(camp.width, w) / 2 - 1) / 2;
                    int x = camp.xMin + (camp.width - w) / 2 + Random.Range(-slide, slide + 1);
                    int y = s == "north" ? camp.yMax : camp.yMin - h;
                    r = new RectInt(x, y, w, h);
                }
                if (!Fits(r, g)) continue;
                cells = r;
                side = s;
                return true;
            }
        }
        return false;
    }

    // 这块地能不能放区域：整块在地图里（离边留 1 格）、不和别的区域重叠、里面没有墙。
    bool Fits(RectInt r, GridManager g)
    {
        if (r.xMin < 1 || r.yMin < 1 || r.xMax > g.gridWidth - 1 || r.yMax > g.gridHeight - 1) return false;
        foreach (Region o in Active) if (o.cells.Overlaps(r)) return false;
        for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                if (TerrainSystem.IsWall(new Vector2Int(x, y))) return false;
        return true;
    }

    // 地面上铺一层很淡的底色，让人看得出这块地和营地不一样（以后换成正式的地面美术）。
    GameObject MakeGround(Region r)
    {
        GridManager g = GridManager.Instance;
        GameObject go = new GameObject("Region_" + r.type);
        go.transform.SetParent(transform, false);
        go.transform.position = g.AreaCenter(r.cells.position, r.cells.size);
        go.transform.localScale = new Vector3(r.cells.width * g.cellSize, r.cells.height * g.cellSize, 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = EnemyDirector.WhiteSprite;   // 1×1 的白方块，靠缩放铺满
        sr.sortingOrder = -2;                    // 地形贴图（-3）上面、营地地砖和草（-1）下面
        switch (r.type)
        {
            case RegionType.AbandonedCamp: sr.color = new Color(0.62f, 0.58f, 0.48f, 0.22f); break;   // 灰黄：旧水泥地
            case RegionType.MonsterNest: sr.color = new Color(0.50f, 0.22f, 0.42f, 0.26f); break;     // 紫红：巢穴
            case RegionType.HumanCamp: sr.color = new Color(0.55f, 0.42f, 0.26f, 0.22f); break;       // 棕：踩实的土
            default: sr.color = new Color(0.30f, 0.42f, 0.55f, 0.24f); break;                         // 蓝灰：ABA
        }
        return go;
    }

    // 按种类往区域里放东西。
    void Populate(Region r, WorldState w)
    {
        GridManager g = GridManager.Instance;
        Vector3 center = g.AreaCenter(r.cells.position, r.cells.size);
        switch (r.type)
        {
            case RegionType.AbandonedCamp:
                // 废弃营地：资源最多，没有敌人；留着几张溅了干血的铁桌子（先摆桌子，资源再往空地上撒）
                PlaceTables(r, Random.Range(2, 4));
                Scatter(r, ResourceKind.Tree, 3, 1, 2);
                Scatter(r, ResourceKind.Stone, 3, 1, 2);
                Scatter(r, ResourceKind.Ore, 2, 1, 2);
                Scatter(r, ResourceKind.Grass, 6, 1, 1);
                break;

            case RegionType.MonsterNest:
                // 怪物巢穴：一群造物守着矿
                Scatter(r, ResourceKind.Ore, 4, 1, 2);
                Scatter(r, ResourceKind.Stone, 2, 1, 2);
                int creatures = Random.Range(3, 6);
                for (int i = 0; i < creatures; i++)
                {
                    float roll = Random.value;
                    string id = w.day >= 4 && roll < 0.3f ? "infected" : w.day >= 5 && roll < 0.65f ? "stalker" : "shade";
                    SpawnEnemy(r, id, center);
                }
                break;

            case RegionType.HumanCamp:
                // 其他人类的营地：几个拾荒者（后期带一个拿枪的），周围有木头石头
                Scatter(r, ResourceKind.Tree, 3, 1, 2);
                Scatter(r, ResourceKind.Stone, 2, 1, 2);
                int people = Random.Range(2, 4);
                for (int i = 0; i < people; i++) SpawnEnemy(r, "scavenger", center);
                if (w.day >= 15) SpawnEnemy(r, "raider", center);
                break;

            default:
                // ABA 临时指挥部：一个清除小组（人数用 AbaForce 的 Squad Size），有矿
                Scatter(r, ResourceKind.Ore, 2, 1, 2);
                Scatter(r, ResourceKind.Stone, 2, 1, 1);
                if (NpcNav.Sample(center, 3f, out Vector3 p)) AbaForce.Deploy(p);
                break;
        }
    }

    // 在区域里摆 count 张带干血渍的铁桌子（横放 / 竖放随机）。图在 Resources/CampArt/Camp_Table_Blood、Camp_Table_V_Blood，没有图就不摆。
    // 桌子挡路（NPC 和敌人绕着走），占的格子别的东西放不上来；不挡子弹。离区域的边留 2 格，桌子之间留 2 格。
    void PlaceTables(Region r, int count)
    {
        GridManager g = GridManager.Instance;
        const int Inset = 2, Gap = 2;
        bool placed = false;
        for (int i = 0; i < count; i++)
        {
            bool vertical = Random.value < 0.5f;
            Sprite sprite = CampArt.Load(vertical ? "Camp_Table_V_Blood" : "Camp_Table_Blood");
            if (sprite == null) continue;
            // 占地（小格）：宽 = 图的宽；深 = 横放 0.6 格、竖放 = 图的高减去桌子自己的高（约 0.6 格）
            float depthUnits = vertical ? Mathf.Max(0.6f, sprite.bounds.size.y - 0.6f) : 0.6f;
            Vector2Int size = new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(sprite.bounds.size.x / g.cellSize)),
                Mathf.Max(1, Mathf.RoundToInt(depthUnits / g.cellSize)));
            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector2Int o = new Vector2Int(
                    Random.Range(r.cells.xMin + Inset, r.cells.xMax - Inset - size.x + 1),
                    Random.Range(r.cells.yMin + Inset, r.cells.yMax - Inset - size.y + 1));
                // 连同四周 Gap 格都要是空的（没有建筑、没有别的桌子、不是墙和水）
                if (!g.IsAreaBuildable(new Vector2Int(o.x - Gap, o.y - Gap), new Vector2Int(size.x + Gap * 2, size.y + Gap * 2))) continue;

                g.SetAreaOccupied(o, size, "铁桌子");
                r.decorCells.Add(new RectInt(o.x, o.y, size.x, size.y));
                Vector3 c = g.AreaCenter(o, size);
                float depth = size.y * g.cellSize;
                GameObject go = new GameObject(sprite.name);
                go.transform.SetParent(transform, false);
                go.transform.position = c + new Vector3(0f, -depth * 0.5f, 0f);   // 图的轴心在桌脚：放在占地的下沿
                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = 0;                                              // 和建筑同层
                GameObject nav = new GameObject("TableNav");                      // 看不见的方块：这块地不能走
                nav.layer = 2;
                nav.transform.SetParent(go.transform, false);
                nav.transform.position = c;
                BoxCollider2D box = nav.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = new Vector2(size.x * g.cellSize, depth);
                NavMeshModifier mod = nav.AddComponent<NavMeshModifier>();
                mod.overrideArea = true;
                mod.area = 1;                                                     // 1 = Not Walkable
                r.decor.Add(go);
                placed = true;
                break;
            }
        }
        if (placed && TerrainSystem.Instance != null) TerrainSystem.Instance.RequestRebake();   // 多了挡路的东西：重新烘焙导航
    }

    // 在区域里随机撒 count 个资源（边长 sizeMin~sizeMax 小格）。
    // 离区域的边留 2 格、挡路的资源之间留 3 格空地，保证 NPC 走得进去、绕得过去。
    void Scatter(Region r, ResourceKind kind, int count, int sizeMin, int sizeMax)
    {
        const int Inset = 2;
        for (int i = 0; i < count; i++)
        {
            int size = Random.Range(sizeMin, sizeMax + 1);
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int x = Random.Range(r.cells.xMin + Inset, r.cells.xMax - Inset - size + 1);
                int y = Random.Range(r.cells.yMin + Inset, r.cells.yMax - Inset - size + 1);
                Vector2Int o = new Vector2Int(x, y);
                int gap = ResourceInfo.Blocks(kind, size) ? TerrainGenerator.BlockGap : 0;
                if (!AreaFree(o, size, gap)) continue;
                ResourceNode n = TerrainSystem.AddResource(kind, size, o, Random.Range(0, 1024));
                if (n == null) continue;
                r.nodes.Add(n);
                break;
            }
        }
    }

    // 这块地（往外多看 gap 格）上有没有别的挡路资源。草不算。
    static bool AreaFree(Vector2Int origin, int size, int gap)
    {
        for (int y = origin.y - gap; y < origin.y + size + gap; y++)
            for (int x = origin.x - gap; x < origin.x + size + gap; x++)
            {
                ResourceNode other = TerrainSystem.ResourceAt(new Vector2Int(x, y));
                if (other == null) continue;
                bool inside = x >= origin.x && x < origin.x + size && y >= origin.y && y < origin.y + size;
                if (inside || other.Blocks) return false;   // 占地里不能有任何资源；外圈只在意挡路的
            }
        return true;
    }

    // 在区域中心附近放一个敌人（在这一带游荡，以区域中心为家）。
    void SpawnEnemy(Region r, string defId, Vector3 center)
    {
        EnemyDef def = EnemyDefs.Get(defId);
        if (def == null) return;
        GridManager g = GridManager.Instance;
        float rx = Mathf.Max(0.5f, r.cells.width * g.cellSize * 0.5f - 1f), ry = Mathf.Max(0.5f, r.cells.height * g.cellSize * 0.5f - 1f);
        Vector3 pos = center + new Vector3(Random.Range(-rx, rx), Random.Range(-ry, ry), 0f);
        Enemy e = Enemy.Spawn(def, pos, Enemy.Goal.Wander, center);
        if (e != null) r.enemies.Add(e);
    }

    // ============ 消失 ============

    void Expire(Region r, WorldState w)
    {
        Active.Remove(r);
        _lastEndDay = w != null ? w.day : _lastEndDay;
        if (FogManager.Instance != null) FogManager.Instance.CoverRegion(r.fogId);   // 雾合上

        foreach (ResourceNode n in r.nodes)
            if (n != null && !n.depleted) n.Deplete();        // 没采完的资源一起消失（Deplete 会清占地、重新烘焙导航）
        foreach (Enemy e in r.enemies)
        {
            if (e == null || !e.Alive) continue;              // 死了的自己会淡出
            Enemy.All.Remove(e);                              // 还活着的：跟着区域一起消失
            Destroy(e.gameObject);
        }
        if (r.ground != null) Destroy(r.ground);
        if (r.decor.Count > 0)                                // 摆设：拆掉、把格子还回去、重新烘焙导航
        {
            foreach (GameObject go in r.decor) if (go != null) Destroy(go);
            if (GridManager.Instance != null)
                foreach (RectInt c in r.decorCells) GridManager.Instance.ClearArea(c.position, c.size);
            if (TerrainSystem.Instance != null) TerrainSystem.Instance.RequestRebake();
        }
        // ABA 小组不跟着消失：他们是来清除一切的，放出来就留在世界里。

        if (w != null) AddEvent(w, LF("region_event_gone", CampZone.SideName(r.side, LanguageGuard.IsJp), Name(r.type)));
        if (HudToasts.Instance != null) HudToasts.Instance.Show("region : closed", L("region_toast_gone"), Name(r.type));
        Debug.Log("[Region] 消失 " + r.type + " @ " + r.cells);
    }

    // ============ 测试键 ============

    void DebugKeys(WorldState w)
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || ChatManager.InputFocused || !kb.f11Key.wasPressedThisFrame) return;   // 按下 F11 才往下走（F9 留给 AI 调度器面板）
        if (kb.shiftKey.isPressed)
        {
            for (int i = Active.Count - 1; i >= 0; i--) Expire(Active[i], w);   // Shift+F11：现有的区域马上消失
            return;
        }
        // F11：不管天数和间隔，马上生成一个（已经有 maxActiveRegions 个时先让最早的那个消失）
        if (Active.Count >= Mathf.Max(1, maxActiveRegions)) Expire(Active[0], w);
        if (!TrySpawn(PickType(w), w, true)) Debug.LogWarning("[Region] 找不到地方放区域（营地四周都放不下？）");
    }

    // ============ 小工具 ============

    static void AddEvent(WorldState w, string text)
    {
        if (w == null || string.IsNullOrEmpty(text)) return;
        if (EventLogManager.Instance != null) EventLogManager.Instance.AddEvent(w.day, text);
        w.eventLog.Add(new MemoryEntry { day = w.day, content = text });   // NPC 聊天时"最近发生的事"里也会有
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] a) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, a) : key;
}
