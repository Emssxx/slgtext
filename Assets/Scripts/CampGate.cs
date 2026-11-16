using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;         // NavMeshAgent：给敌人设"哪些区域能走"
using NavMeshPlus.Components; // NavMeshModifier：把门口那一格标成单独的导航区域
using UnityEngine.Rendering;   // SortingGroup：大门的十几张零件当成一个整体排序（整体在 NPC 下面，和别的建筑一样）

// ============================================================================
// 营地出入口的升降杆大门（临时营地的自动门）。
//   · 出入口在哪由 CampZone 决定（Slot.gate）。这里每秒看一次，在每个出入口摆一座门；出入口挪了门跟着挪。
//   · 一座门 = 转轴门柱 + 托杆柱 + 升降杆 + 自动识别器（红灯 = 关，绿灯 = 开）+ 连杆抬起装置（电机箱、缸体、活塞杆）+ 电线。
//     图在 Resources/CampArt/Gate_*.png，500 像素 = 1 格。
//   · 自动：有活着的 NPC 走到门附近（openRadius）就抬杆，人走开就放下。敌人靠近不开。
//   · 南北边的出入口 = 横门：杆子绕转轴往上转。
//     东西边的出入口 = 竖门：杆子顺着纵深放（远处是转轴柱，近处是托杆柱），抬起来时在画面上先缩短、再翻到上面去。
//   · 【挡敌人，不挡自己人】门口那一格在导航网格里标成单独的区域（第 4 / 5 号）：
//       NPC 一直能走（走近了门自己会开）；
//       敌人和 ABA 队员：门关着的时候这块区域从他们"能走的区域"里去掉 = 门口对他们来说和墙一样走不过去，
//       门开着的那一会儿（有 NPC 正在进出）他们也能跟着冲进来。
//     营地围死、门又关着时，敌人的反应和原来被墙挡住时一样：会破墙的去拆墙，不会的在外面转。门本身拆不掉。
//     防夹：门口那一格里有敌人时杆子不会放下来（不然他会被卡在走不了的格子里动不了）。
// 找不到图就不摆门。由 WorldManager 开局自动建（CampGateManager.Ensure）。
// ============================================================================
public class CampGateManager : MonoBehaviour
{
    public static CampGateManager Instance { get; private set; }

    [Header("自动开门")]
    [Tooltip("NPC 离门中心多近就开门（世界单位）")]
    public float openRadius = 1.6f;
    [Tooltip("抬起 / 放下要几秒")]
    public float openSeconds = 0.9f;
    [Tooltip("杆子抬到多少度（90 = 竖直）")]
    public float liftAngle = 80f;

    [Header("挡敌人")]
    [Tooltip("开 = 门关着时敌人和 ABA 走不过去；关 = 门只是外观（谁都能走）")]
    public bool blockEnemies = true;
    [Tooltip("门口那一格在导航网格里的区域编号（第一座门用这个，第二座门用下一个）。3 已经被沼泽用了")]
    public int firstNavArea = 4;

    readonly Dictionary<int, CampGate> _gates = new Dictionary<int, CampGate>();   // 出入口的编号（CampZone.Slot.index）→ 门
    readonly List<int> _gone = new List<int>();
    float _timer, _maskTimer;

    public static CampGateManager Ensure()
    {
        if (Instance != null) return Instance;
        CampGateManager m = FindFirstObjectByType<CampGateManager>();
        if (m != null) { Instance = m; return m; }
        return new GameObject("CampGates").AddComponent<CampGateManager>();
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
        _maskTimer -= Time.unscaledDeltaTime;
        if (_maskTimer <= 0f) { _maskTimer = 0.2f; ApplyEnemyMask(); }   // 每 0.2 秒：按门的开关更新敌人能走的区域
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = 1f;
        Sync();
    }

    // 敌人 / ABA 能走哪些导航区域 = 全部 − 关着的门口。门一开一关，他们的寻路马上跟着变。
    void ApplyEnemyMask()
    {
        int mask = NavMesh.AllAreas;
        if (blockEnemies)
            foreach (CampGate gate in _gates.Values)
                if (gate != null && !gate.Passable) mask &= ~(1 << gate.NavArea);
        foreach (Enemy e in Enemy.All) SetMask(e != null ? e.GetComponent<NavMeshAgent>() : null, mask);
        foreach (AbaOperative o in AbaOperative.All) SetMask(o != null ? o.GetComponent<NavMeshAgent>() : null, mask);
    }

    static void SetMask(NavMeshAgent a, int mask)
    {
        if (a != null && a.areaMask != mask) a.areaMask = mask;          // 只在变了的时候设（设一次会让它重新算路）
    }

    // 对一遍：每个出入口有一座门，位置对；不再是出入口的地方把门拆掉。
    void Sync()
    {
        CampZone z = CampZone.Instance;
        GridManager g = GridManager.Instance;
        if (z == null || !z.Ready || g == null) return;
        if (CampArt.Load("Gate_Post_Pivot") == null) return;     // 没有图：不摆门

        _gone.Clear();
        foreach (int k in _gates.Keys) _gone.Add(k);
        bool changed = false;                                    // 门的位置 / 数量变了：导航网格要重新烘焙
        int order = 0;                                           // 第几座门（决定它用哪个导航区域编号）
        foreach (CampZone.Slot s in z.Slots)
        {
            if (!s.gate) continue;
            _gone.Remove(s.index);
            int area = Mathf.Clamp(firstNavArea + Mathf.Min(order, 1), 2, 31);   // 最多两座门各用一个编号（再多就共用第二个）
            order++;
            bool vertical = s.side == "east" || s.side == "west";
            Vector3 center = g.AreaCenter(s.origin, z.SlotSize);
            Vector2 size = new Vector2(z.SlotSize.x * g.cellSize, z.SlotSize.y * g.cellSize);
            if (!_gates.TryGetValue(s.index, out CampGate gate) || gate == null || gate.Vertical != vertical)
            {
                if (gate != null) Destroy(gate.gameObject);      // 横竖变了：重摆
                GameObject go = new GameObject("CampGate_" + s.side);
                go.transform.SetParent(transform, false);
                gate = go.AddComponent<CampGate>();
                gate.Build(vertical, size, area);
                _gates[s.index] = gate;
                changed = true;
            }
            if ((gate.transform.position - center).sqrMagnitude > 0.0001f) changed = true;
            gate.transform.position = center;
        }
        foreach (int k in _gone)
        {
            if (_gates[k] != null) Destroy(_gates[k].gameObject);
            _gates.Remove(k);
            changed = true;
        }
        if (changed && TerrainSystem.Instance != null) TerrainSystem.Instance.RequestRebake();   // 门口的导航区域变了：重新烘焙
    }
}

// 一座升降杆大门。物体的位置 = 出入口那一格的正中心。
public class CampGate : MonoBehaviour
{
    public bool Vertical { get; private set; }
    public int NavArea { get; private set; }                 // 门口那一格在导航网格里的区域编号
    // 敌人现在能不能从这座门过：杆子抬过一半了，或者门口那一格里正有敌人（防夹）
    public bool Passable => _t >= 0.5f || _enemyInside;

    const float Px = 1f / 500f;            // 图上 1 像素 = 多少世界单位（图是 500 像素 / 格）
    const float HubHeight = 375f * Px;     // 转轴离地多高（0.75 格，大概在 NPC 胸口）

    Transform _arm, _lug, _rod, _cyl;
    SpriteRenderer _red, _green;
    Vector2 _hub, _mount;                  // 转轴、推杆下支座（相对门中心）
    float _armScale = 1f;                  // 杆子的图是 1 格长；出入口不是 1 格宽时按比例拉长 / 缩短
    float _t;                              // 0 = 关着，1 = 完全抬起
    float _scan;
    bool _wantOpen, _enemyInside;
    Vector2 _half;                         // 出入口那一格的半宽、半高

    // 摆零件。vertical = 竖门；size = 出入口那一格的宽高（世界单位）。
    public void Build(bool vertical, Vector2 size, int navArea)
    {
        Vertical = vertical;
        NavArea = navArea;
        _half = size * 0.5f;

        // 导航：门口这一格标成单独的区域（一个看不见的方块，和 TerrainSystem 标沼泽的做法一样）
        GameObject nav = new GameObject("GateNav");
        nav.layer = 2;                                            // Ignore Raycast：不挡鼠标点击
        nav.transform.SetParent(transform, false);
        BoxCollider2D box = nav.AddComponent<BoxCollider2D>();
        box.isTrigger = true;                                     // 触发器：不会把人弹开
        box.size = size;
        NavMeshModifier mod = nav.AddComponent<NavMeshModifier>();
        mod.overrideArea = true;
        mod.area = navArea;
        SortingGroup sg = gameObject.AddComponent<SortingGroup>();
        sg.sortingOrder = 0;                                      // 和别的建筑同层（NPC 是 1，永远画在门上面）

        float hx = size.x * 0.5f, hy = size.y * 0.5f;
        if (!vertical)
        {
            // —— 横门：转轴柱在左下角，托杆柱在右下角，杆子横在中间 ——
            Vector2 pivotBase = new Vector2(-hx, -hy), restBase = new Vector2(hx, -hy);
            _hub = pivotBase + new Vector2(0f, HubHeight);
            _mount = pivotBase + new Vector2(36f * Px, 170f * Px);
            _armScale = size.x;                                   // 杆长 = 出入口的宽
            Vector2 sensor = pivotBase + new Vector2(-0.78f, 0f);
            Part("Gate_Sensor", 0, sensor);
            _red = Part("Gate_Sensor_LightRed", 1, sensor, true);
            _green = Part("Gate_Sensor_LightGreen", 1, sensor, true);
            Part("Gate_Wire", 2, sensor + new Vector2(0.03f, 0.44f)).transform.localScale = new Vector3(0.72f, 0.7f, 1f);
            Part("Gate_Lifter_Base", 3, pivotBase + new Vector2(-0.27f, 0.016f)).transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            Part("Gate_Post_Pivot", 4, pivotBase);
            _rod = Part("Gate_Lifter_Rod", 5, _hub).transform;
            _cyl = Part("Gate_Lifter_Cylinder", 6, _mount).transform;
            _arm = Part("Gate_Arm_1", 7, _hub).transform;
            _lug = Part("Gate_Arm_Lug", 8, _hub).transform;
            Part("Gate_Post_Rest", 9, restBase);
        }
        else
        {
            // —— 竖门：转轴柱在远处（这一格的上沿），托杆柱在近处（下沿），杆子顺着纵深 ——
            Vector2 pivotBase = new Vector2(0f, hy), restBase = new Vector2(0f, -hy);
            _hub = pivotBase + new Vector2(0f, HubHeight);
            _mount = pivotBase + new Vector2(0.078f, 0.034f);
            _armScale = size.y;
            Vector2 sensor = pivotBase + new Vector2(-0.547f, -0.047f);
            Part("Gate_Sensor", 0, sensor);
            _red = Part("Gate_Sensor_LightRed", 1, sensor, true);
            _green = Part("Gate_Sensor_LightGreen", 1, sensor, true);
            Part("Gate_Wire", 2, sensor + new Vector2(-0.03f, 0.5f)).transform.localScale = new Vector3(0.5f, 0.6f, 1f);
            Part("Gate_Post_Pivot", 3, pivotBase);
            Part("Gate_Lifter_Base", 4, pivotBase + new Vector2(-0.234f, 0.016f)).transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            _rod = Part("Gate_Lifter_Rod", 5, _hub).transform;
            _cyl = Part("Gate_Lifter_Cylinder", 6, _mount).transform;
            _arm = Part("Gate_Arm_1", 7, _hub).transform;
            _lug = Part("Gate_Arm_Lug", 8, _hub).transform;
            Part("Gate_Post_Rest", 9, restBase);
        }
        Pose(0f);
    }

    // 放一张零件。unlit = 不受光照（指示灯：黑暗里也看得见）。
    SpriteRenderer Part(string spriteName, int order, Vector2 localPos, bool unlit = false)
    {
        GameObject go = new GameObject(spriteName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(localPos.x, localPos.y, 0f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = CampArt.Load(spriteName);
        sr.sortingOrder = order;                                  // 在 SortingGroup 里面，只决定零件之间谁压谁
        if (unlit) RenderMats.MakeUnlit(sr);
        return sr;
    }

    void Update()
    {
        CampGateManager m = CampGateManager.Instance;
        float radius = m != null ? m.openRadius : 1.6f;
        float seconds = m != null ? Mathf.Max(0.05f, m.openSeconds) : 0.9f;

        // 每 0.2 秒看一次附近有没有人（不用每帧查）
        _scan -= Time.deltaTime;
        if (_scan <= 0f)
        {
            _scan = 0.2f;
            _enemyInside = EnemyInside();
            _wantOpen = AnyNpcNear(radius) || (_enemyInside && _t > 0f);   // 防夹：杆子抬着、门口有敌人时不往下放
        }
        float target = _wantOpen ? 1f : 0f;
        if (Mathf.Approximately(_t, target)) return;
        _t = Mathf.MoveTowards(_t, target, Time.deltaTime / seconds);
        Pose(_t);
    }

    bool AnyNpcNear(float radius)
    {
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w == null) return false;
        Vector2 c = transform.position;
        foreach (NpcData n in w.npcs)
        {
            if (n == null || n.IsDead) continue;
            NpcMover mv = NpcView.GetMover(n.id);
            if (mv != null && Vector2.Distance(mv.transform.position, c) <= radius) return true;
        }
        return false;
    }

    // 门口那一格里有没有活着的敌人 / ABA 队员。
    bool EnemyInside()
    {
        Vector2 c = transform.position;
        foreach (Enemy e in Enemy.All)
            if (e != null && e.Alive && InBox(e.transform.position, c)) return true;
        foreach (AbaOperative o in AbaOperative.All)
            if (o != null && o.Alive && InBox(o.transform.position, c)) return true;
        return false;
    }

    bool InBox(Vector2 p, Vector2 c)
    {
        return Mathf.Abs(p.x - c.x) <= _half.x + 0.1f && Mathf.Abs(p.y - c.y) <= _half.y + 0.1f;
    }

    // 按开合程度 t（0~1）摆杆子、推杆和指示灯。
    void Pose(float t)
    {
        CampGateManager m = CampGateManager.Instance;
        float lift = m != null ? m.liftAngle : 80f;
        float e = t * t * (3f - 2f * t);                          // 先慢后快再慢
        float deg = lift * e, rad = deg * Mathf.Deg2Rad;

        Vector2 lug;
        if (!Vertical)
        {
            // 横门：杆子绕转轴逆时针转
            _arm.localRotation = Quaternion.Euler(0f, 0f, deg);
            _arm.localScale = new Vector3(_armScale, 1f, 1f);
            Vector2 a = new Vector2(150f * Px, -25f * Px);        // 耳片在杆子上的位置：离转轴 150 像素、在杆子下沿
            lug = _hub + new Vector2(a.x * Mathf.Cos(rad) - a.y * Mathf.Sin(rad), a.x * Mathf.Sin(rad) + a.y * Mathf.Cos(rad));
            _lug.localRotation = Quaternion.Euler(0f, 0f, deg);
        }
        else
        {
            // 竖门：杆子顺着纵深。画面上的长度 = 朝下（朝镜头）的分量 − 抬起来的分量；变成负数 = 翻到转轴上面去了
            float f = Mathf.Cos(rad) - Mathf.Sin(rad);
            if (Mathf.Abs(f) < 0.04f) f = f < 0f ? -0.04f : 0.04f;
            _arm.localRotation = Quaternion.Euler(0f, 0f, -90f);
            _arm.localScale = new Vector3(_armScale * f, 1f, 1f);
            lug = _hub + new Vector2(0.078f, -(0.12f * f + 0.024f));
        }
        _lug.localPosition = new Vector3(lug.x, lug.y, 0f);

        // 推杆：缸体装在下支座上、指向耳片；活塞杆挂在耳片上、插进缸体里
        Vector2 d = lug - _mount;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;  // 两张图都是竖着画的（朝上），所以减 90 度
        _cyl.localRotation = Quaternion.Euler(0f, 0f, ang);
        _rod.localPosition = new Vector3(lug.x, lug.y, 0f);
        _rod.localRotation = Quaternion.Euler(0f, 0f, ang);

        // 指示灯：关死 = 红；一开始抬 = 绿
        bool open = t > 0.02f;
        if (_red != null) _red.enabled = !open;
        if (_green != null) _green.enabled = open;
    }
}
