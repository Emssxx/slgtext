using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 电力系统（纯规则）——参考 RimWorld / 缺氧 / 冰汽时代这类建造生存游戏的通用做法：
//   · 发电机只发电不存电；电存在「电池」里：AI 终端机自带一块小电池，另外可以建「蓄电池」。
//   · 每回合按这个顺序结算（每张电网各算各的）：
//       1. 电池自放电（每回合漏掉 SelfDischarge）
//       2. 可用电力 = 本回合发电 + 电池里的电
//       3. 设备按优先级依次供电：加温器 / 降温器（保命）先，水源净化器后；
//          电不够时从优先级低的开始断电（拉闸限电），而不是全网一起停。
//       4. 发电有剩：充进电池（充电效率 ChargeEfficiency，有损耗；电池满了多的就浪费）
//          发电不够：差额从电池里放出来。
//   · 设备耗电：
//       加温器 / 降温器：按档位查表 ClimateLevelPower（档位越高越费电，3 档比 1 档贵 4.5 倍——开高档是"烧钱"）。
//       水源净化器：只在真正干活时用电，按这一回合实际净化的雨水量算（没雨水 = 待机不耗电）。
//   · 电线：不是建筑，是"连线"——在建造模式里选「电线」，先点一个节点、再点另一个节点就连上（再连一次就拆掉）。
//     连在一起的节点组成一张电网；没连线的设备拿不到电。
// 数值全部集中在下面的静态字段里，方便以后调平衡。
// ============================================================================
public static class PowerGrid
{
    // —— 电池 ——
    public static float TerminalCapacity = 10f;    // AI 终端机自带的小电池
    public static float BatteryCapacity = 50f;     // 一座蓄电池能存多少电
    public static float ChargeEfficiency = 0.8f;   // 充电效率：充 1 电只存进 0.8
    public static float SelfDischarge = 0.01f;     // 电池每回合自放电比例（1%）

    // —— 设备 ——
    public static float[] ClimateLevelPower = { 0f, 1f, 2.5f, 4.5f }; // 加温器 / 降温器 0~3 档每回合耗电
    public static float PurifierPowerPerLevel = 1f; // 净化器满负荷时每档每回合耗电（实际按处理量折算）
    public static float DegreesPerLevel = 5f;      // 加温器 / 降温器每档改变多少度
    public static float Radius = 5f;               // 加温器 / 降温器的作用半径（世界单位 = 细分前的大格；显示给玩家时换算成小格，见 RadiusCells）
    public static int MaxLevel = 3;                // 最高档位
    public static float MaxLinkLength = 15f;       // 一根电线最长多长（世界单位；显示时换算成小格）

    // 换算成小格数（给玩家看的"半径 X 格""最长 X 格"）
    public static int RadiusCells => Mathf.RoundToInt(Radius / CellSize);
    public static int MaxLinkCells => Mathf.RoundToInt(MaxLinkLength / CellSize);
    static float CellSize => GridManager.Instance != null && GridManager.Instance.cellSize > 0f ? GridManager.Instance.cellSize : 1f;

    class Link
    {
        public PowerNode a, b;
        public LineRenderer line;
    }

    public class Network
    {
        public int id;
        public readonly List<PowerNode> nodes = new List<PowerNode>();
        public float Stored, Capacity;     // 电池里的电 / 电池总容量
        public float Produced;             // 本回合发电
        public float Demand;               // 本回合设备想用的电
        public float Consumed;             // 本回合设备实际用掉的电
        public float Charged, Wasted;      // 充进电池的 / 电池满了浪费掉的
        public int Shed;                   // 本回合因为电不够被断电的设备数
        public int Generators, Batteries;
        public float Net => Produced - Consumed; // 发电 - 用电（正数 = 有富余）
    }

    static readonly List<Link> _links = new List<Link>();
    static readonly List<Network> _networks = new List<Network>();
    static Material _lineMat;
    static bool _dirty = true;

    public static IReadOnlyList<Network> Networks { get { Rebuild(); return _networks; } }

    // ============ 每回合结算 ============

    // windPower：这一回合每台发电机发多少电（由天气决定）。
    public static void Tick(float windPower)
    {
        EnsureTerminalNode();
        Rebuild();
        int rawLeft = WaterWorks.RawTotal; // 净化器按收集器里还剩多少雨水来安排工作量（所有电网共用水管）

        foreach (Network n in _networks)
        {
            n.Produced = n.Demand = n.Consumed = n.Charged = n.Wasted = 0f;
            n.Shed = 0;

            // 1. 自放电
            foreach (PowerNode s in n.nodes)
                if (s.IsStorage && s.IsBuilt) s.charge = Mathf.Max(0f, s.charge * (1f - SelfDischarge));

            // 2. 发电
            foreach (PowerNode g in n.nodes)
                if (g.IsGenerator && g.IsBuilt) n.Produced += windPower;
            float available = n.Produced + StoredIn(n);

            // 3. 按优先级供电
            List<PowerNode> devices = new List<PowerNode>();
            foreach (PowerNode d in n.nodes)
            {
                if (!d.IsDevice) continue;
                d.powered = false;
                d.workAmount = 0;
                if (d.IsBuilt && d.level > 0) devices.Add(d);
            }
            devices.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.level.CompareTo(b.level));
            foreach (PowerNode d in devices)
            {
                float need;
                int work = 0;
                if (d.kind == PowerNodeKind.Purifier)
                {
                    work = Mathf.Min(d.level * WaterWorks.PurifyPerLevel, rawLeft);
                    if (work <= 0) continue; // 没雨水：待机
                    need = PurifierPower(work);
                }
                else need = DemandOf(d);
                if (need <= 0f) continue;

                n.Demand += need;
                if (available + 0.0001f < need) { n.Shed++; continue; } // 电不够：这台断电
                available -= need;
                n.Consumed += need;
                d.powered = true;
                d.workAmount = work;
                rawLeft -= work;
            }

            // 4. 富余充电 / 不足放电
            float net = n.Produced - n.Consumed;
            if (net >= 0f) Charge(n, net);
            else Draw(n, -net);
            n.Stored = StoredIn(n);
        }
        RefreshLines();
    }

    // 加温器 / 降温器在某个档位每回合耗多少电。
    public static float DemandOf(PowerNode d)
    {
        if (d == null || !d.IsDevice || d.level <= 0) return 0f;
        if (d.kind == PowerNodeKind.Purifier) return d.level * PurifierPowerPerLevel;
        return ClimateLevelPower[Mathf.Clamp(d.level, 0, ClimateLevelPower.Length - 1)];
    }

    // 净化 work 份雨水要多少电（满负荷 = 每档 PurifyPerLevel 份 / PurifierPowerPerLevel 电）。
    public static float PurifierPower(int work)
    {
        return WaterWorks.PurifyPerLevel > 0 ? work * PurifierPowerPerLevel / WaterWorks.PurifyPerLevel : 0f;
    }

    public static float CapacityOf(PowerNode n)
    {
        if (n == null || !n.IsBuilt) return 0f;
        if (n.kind == PowerNodeKind.Terminal) return TerminalCapacity;
        if (n.kind == PowerNodeKind.Battery) return BatteryCapacity;
        return 0f;
    }

    static float StoredIn(Network n)
    {
        float s = 0f;
        foreach (PowerNode g in n.nodes) if (g.IsStorage) s += g.charge;
        return s;
    }

    static float CapacityIn(Network n)
    {
        float c = 0f;
        foreach (PowerNode g in n.nodes) c += CapacityOf(g);
        return c;
    }

    // 把富余的电充进电池（有损耗）。
    static void Charge(Network n, float amount)
    {
        float energy = amount * ChargeEfficiency;
        foreach (PowerNode s in n.nodes)
        {
            if (!s.IsStorage || energy <= 0f) continue;
            float space = Mathf.Max(0f, CapacityOf(s) - s.charge);
            float put = Mathf.Min(space, energy);
            s.charge += put;
            energy -= put;
            n.Charged += put;
        }
        n.Wasted = ChargeEfficiency > 0f ? energy / ChargeEfficiency : 0f;
    }

    // 从电池里取电（先取存得多的）。
    static void Draw(Network n, float amount)
    {
        while (amount > 0.0001f)
        {
            PowerNode best = null;
            foreach (PowerNode s in n.nodes)
                if (s.IsStorage && s.charge > 0.0001f && (best == null || s.charge > best.charge)) best = s;
            if (best == null) break;
            float take = Mathf.Min(best.charge, amount);
            best.charge -= take;
            amount -= take;
        }
    }

    // 某个位置因为加温器 / 降温器而改变了多少度（只算通电的）。
    public static float TemperatureOffsetAt(Vector3 pos)
    {
        float off = 0f;
        foreach (PowerNode d in PowerNode.All)
        {
            if (d == null || !d.IsDevice || !d.powered || d.level <= 0) continue;
            if (d.kind != PowerNodeKind.Heater && d.kind != PowerNodeKind.Cooler) continue;
            Vector3 p = d.transform.position;
            float dx = p.x - pos.x, dy = p.y - pos.y;
            if (dx * dx + dy * dy > Radius * Radius) continue;
            off += (d.kind == PowerNodeKind.Heater ? 1f : -1f) * d.level * DegreesPerLevel;
        }
        return off;
    }

    // ============ 汇总（顶部资源栏 / 终端机用）============

    public struct Summary
    {
        public float stored, capacity, produced, demand, consumed;
        public int shed, devices, unlinkedDevices;
    }

    // 所有能用上电的电网加在一起（有电池或有设备的）。
    public static Summary Total()
    {
        Rebuild();
        Summary s = new Summary();
        foreach (Network n in _networks)
        {
            int devices = 0;
            foreach (PowerNode p in n.nodes) if (p.IsDevice && p.IsBuilt) devices++;
            if (n.Generators == 0 && n.Batteries == 0) { s.unlinkedDevices += devices; continue; }
            if (n.Batteries == 0 && devices == 0) continue; // 孤零零的发电机：发的电没地方用，不算
            s.stored += StoredIn(n);
            s.capacity += CapacityIn(n);
            s.produced += n.Produced;
            s.demand += n.Demand;
            s.consumed += n.Consumed;
            s.shed += n.Shed;
            s.devices += devices;
        }
        return s;
    }

    // ============ 节点 / 电线 ============

    // 调档位（AI 终端机「能源」页）。加温器 / 降温器调高时，电池里的电够就立刻按差价通电，不用等下一回合。
    public static void SetLevel(PowerNode d, int level)
    {
        if (d == null || !d.IsDevice) return;
        float oldNeed = d.powered ? DemandOf(d) : 0f;
        d.level = Mathf.Clamp(level, 0, MaxLevel);
        if (d.level == 0) { d.powered = false; return; }
        if (d.kind == PowerNodeKind.Purifier || !d.IsBuilt) return; // 净化器在回合结算时才用电
        Rebuild();
        Network n = d.network;
        float extra = DemandOf(d) - oldNeed;
        if (extra <= 0f) return;
        if (n != null && StoredIn(n) + 0.0001f >= extra)
        {
            Draw(n, extra);
            n.Consumed += extra;
            n.Stored = StoredIn(n);
            d.powered = true;
        }
        else d.powered = false;
    }

    public static PowerNode NodeAt(Vector2Int cell)
    {
        EnsureTerminalNode();
        foreach (PowerNode n in PowerNode.All)
        {
            if (n == null) continue;
            BuildingFootprint fp = n.GetComponent<BuildingFootprint>(); // 多格建筑：点到占地里任何一格都算
            if (fp != null ? fp.Contains(cell) : n.Cell == cell) return n;
        }
        return null;
    }

    public static bool AreLinked(PowerNode a, PowerNode b)
    {
        foreach (Link l in _links)
            if ((l.a == a && l.b == b) || (l.a == b && l.b == a)) return true;
        return false;
    }

    // 连上 / 拆掉一根电线。返回给玩家看的提示文字。
    public static string ToggleLink(PowerNode a, PowerNode b)
    {
        if (a == null || b == null || a == b) return "";
        for (int i = 0; i < _links.Count; i++)
        {
            Link l = _links[i];
            if ((l.a == a && l.b == b) || (l.a == b && l.b == a))
            {
                if (l.line != null) Object.Destroy(l.line.gameObject);
                _links.RemoveAt(i);
                _dirty = true;
                return LanguageGuard.Pick("拆掉了电线：", "電線を外した：") + a.DisplayName + " — " + b.DisplayName;
            }
        }
        if (Vector3.Distance(a.transform.position, b.transform.position) > MaxLinkLength)
            return LanguageGuard.Pick("太远了，电线最长 " + MaxLinkCells + " 格。", "遠すぎる。電線は最長 " + MaxLinkCells + " マスまで。");
        Link nl = new Link { a = a, b = b, line = CreateLine() };
        _links.Add(nl);
        _dirty = true;
        RefreshLines();
        return LanguageGuard.Pick("接上了电线：", "電線をつないだ：") + a.DisplayName + " — " + b.DisplayName;
    }

    // AI 终端机自带一台小型风力发电机和一块小电池：给场景里的终端机补一个电力节点。
    public static void EnsureTerminalNode()
    {
        TerminalController t = TerminalController.Instance;
        if (t == null || t.GetComponent<PowerNode>() != null) return;
        PowerNode n = t.gameObject.AddComponent<PowerNode>();
        n.kind = PowerNodeKind.Terminal;
        _dirty = true;
    }

    public static void MarkDirty() { _dirty = true; }

    // 连通的节点合成一张电网（并查集）。
    static void Rebuild()
    {
        // 清掉两端已被销毁的电线
        for (int i = _links.Count - 1; i >= 0; i--)
        {
            Link l = _links[i];
            if (l.a == null || l.b == null)
            {
                if (l.line != null) Object.Destroy(l.line.gameObject);
                _links.RemoveAt(i);
                _dirty = true;
            }
        }
        if (!_dirty) return;
        _dirty = false;

        List<PowerNode> nodes = new List<PowerNode>();
        foreach (PowerNode n in PowerNode.All) if (n != null) nodes.Add(n);
        Dictionary<PowerNode, PowerNode> parent = new Dictionary<PowerNode, PowerNode>();
        foreach (PowerNode n in nodes) parent[n] = n;
        foreach (Link l in _links)
        {
            if (!parent.ContainsKey(l.a) || !parent.ContainsKey(l.b)) continue;
            PowerNode ra = Find(parent, l.a), rb = Find(parent, l.b);
            if (ra != rb) parent[ra] = rb;
        }

        _networks.Clear();
        Dictionary<PowerNode, Network> byRoot = new Dictionary<PowerNode, Network>();
        foreach (PowerNode n in nodes)
        {
            PowerNode r = Find(parent, n);
            if (!byRoot.TryGetValue(r, out Network net))
            {
                net = new Network { id = _networks.Count + 1 };
                byRoot[r] = net;
                _networks.Add(net);
            }
            net.nodes.Add(n);
            n.network = net;
        }
        foreach (Network net in _networks)
        {
            net.Generators = 0;
            net.Batteries = 0;
            foreach (PowerNode n in net.nodes)
            {
                if (n.IsGenerator) net.Generators++;
                if (n.IsStorage) net.Batteries++;
            }
            net.Capacity = CapacityIn(net);
            net.Stored = StoredIn(net);
        }
    }

    static PowerNode Find(Dictionary<PowerNode, PowerNode> parent, PowerNode n)
    {
        while (parent[n] != n) { parent[n] = parent[parent[n]]; n = parent[n]; }
        return n;
    }

    // ============ 电线的画法 ============

    static LineRenderer CreateLine()
    {
        GameObject go = new GameObject("PowerWire");
        LineRenderer lr = go.AddComponent<LineRenderer>();
        if (_lineMat == null) _lineMat = new Material(Shader.Find("Sprites/Default"));
        lr.material = _lineMat;
        lr.positionCount = 2;
        lr.startWidth = lr.endWidth = 0.07f;
        lr.sortingOrder = 2;
        lr.useWorldSpace = true;
        return lr;
    }

    // 有电的电网：黄色；没电：灰色。
    static void RefreshLines()
    {
        Rebuild();
        foreach (Link l in _links)
        {
            if (l.line == null || l.a == null || l.b == null) continue;
            l.line.SetPosition(0, l.a.transform.position);
            l.line.SetPosition(1, l.b.transform.position);
            Network n = l.a.network;
            bool live = n != null && (n.Stored > 0.01f || n.Produced > 0f);
            Color c = live ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(0.55f, 0.55f, 0.6f, 0.8f);
            l.line.startColor = l.line.endColor = c;
        }
    }

    // 新的一局：清空电线。
    public static void Clear()
    {
        foreach (Link l in _links) if (l.line != null) Object.Destroy(l.line.gameObject);
        _links.Clear();
        _networks.Clear();
        _dirty = true;
    }
}

public enum PowerNodeKind { Terminal, Turbine, Heater, Cooler, Purifier, Battery }

// 电力节点：挂在终端机、风力发电机、蓄电池、加温器、降温器、水源净化器上。
public class PowerNode : MonoBehaviour
{
    public static readonly List<PowerNode> All = new List<PowerNode>();

    public PowerNodeKind kind;
    public bool built = true;     // 施工完成才发电 / 用电（建造系统会发 OnBuildPlaced / OnBuildComplete）
    public int level;             // 设备档位（0 = 关）
    public bool powered;          // 这一回合有没有通上电
    public float charge;          // 电池里存的电（终端机 / 蓄电池）
    public SpriteRenderer gauge;  // 蓄电池的电量条（可空）

    [System.NonSerialized] public PowerGrid.Network network;
    [System.NonSerialized] public int workAmount; // 净化器：这一回合要净化多少份雨水

    public bool IsGenerator => kind == PowerNodeKind.Terminal || kind == PowerNodeKind.Turbine;
    public bool IsStorage => kind == PowerNodeKind.Terminal || kind == PowerNodeKind.Battery;
    public bool IsDevice => kind == PowerNodeKind.Heater || kind == PowerNodeKind.Cooler || kind == PowerNodeKind.Purifier;

    // 供电优先级：数字小的先供电（电不够时数字大的先断）。
    public int Priority => kind == PowerNodeKind.Purifier ? 1 : 0;

    // 终端机按它自己的"建成"状态算。
    public bool IsBuilt => kind == PowerNodeKind.Terminal
        ? (TerminalController.Instance != null && TerminalController.Instance.isBuilt)
        : built;

    public Vector2Int Cell => GridManager.Instance != null ? GridManager.Instance.WorldToCell(transform.position) : Vector2Int.zero;

    public string KindName
    {
        get
        {
            switch (kind)
            {
                case PowerNodeKind.Terminal: return LanguageGuard.Pick("AI终端机", "AI端末");
                case PowerNodeKind.Turbine: return LanguageGuard.Pick("风力发电机", "風力発電機");
                case PowerNodeKind.Heater: return LanguageGuard.Pick("营地加温器", "拠点ヒーター");
                case PowerNodeKind.Purifier: return LanguageGuard.Pick("水源净化器", "浄水器");
                case PowerNodeKind.Battery: return LanguageGuard.Pick("蓄电池", "蓄電池");
                default: return LanguageGuard.Pick("营地降温器", "拠点クーラー");
            }
        }
    }

    public string DisplayName
    {
        get
        {
            Vector2Int c = Cell;
            string label = GridManager.Instance != null ? GridManager.Instance.CellToLabel(c.x, c.y) : "";
            return KindName + (label.Length > 0 ? "（" + label + "）" : "");
        }
    }

    void OnEnable() { All.Add(this); PowerGrid.MarkDirty(); }
    void OnDisable() { All.Remove(this); PowerGrid.MarkDirty(); }

    void OnBuildPlaced() { built = false; }
    void OnBuildComplete() { built = true; PowerGrid.MarkDirty(); }

    void Update()
    {
        if (gauge == null) return;
        float cap = PowerGrid.CapacityOf(this);
        float fill = cap > 0f ? Mathf.Clamp01(charge / cap) : 0f;
        gauge.enabled = IsBuilt && fill > 0.001f;
        gauge.transform.localScale = new Vector3(1f, fill, 1f);
    }
}

// 建造模式里的「电线」工具：先点一个电力节点，再点另一个 → 连上（已连着就拆掉）。
public static class WireTool
{
    public static bool Active { get; private set; }
    static PowerNode _selected;
    static GameObject _marker;

    public static void Toggle()
    {
        if (Active) Deactivate();
        else { Active = true; Say(LanguageGuard.Pick("电线模式：先点一个设备，再点另一个设备把它们连起来（再连一次就拆掉）。", "電線モード：設備を一つクリックし、次に別の設備をクリックするとつながる（もう一度つなぐと外れる）。")); }
    }

    public static void Deactivate()
    {
        Active = false;
        Select(null);
    }

    public static void HandleClick(Vector2Int cell)
    {
        PowerNode node = PowerGrid.NodeAt(cell);
        if (node == null)
        {
            Select(null);
            Say(LanguageGuard.Pick("这里没有可以接电线的设备（终端机、风力发电机、蓄电池、加温器、降温器、水源净化器）。", "ここには電線をつなげる設備がない（端末・風力発電機・蓄電池・ヒーター・クーラー・浄水器）。"));
            return;
        }
        if (_selected == null) { Select(node); return; }
        if (_selected == node) { Select(null); return; }
        Say(PowerGrid.ToggleLink(_selected, node));
        Select(null);
    }

    static void Select(PowerNode n)
    {
        _selected = n;
        if (_marker != null) { Object.Destroy(_marker); _marker = null; }
        if (n == null) return;
        _marker = new GameObject("WireSelection");
        _marker.transform.position = n.transform.position;
        BuildingFootprint fp = n.GetComponent<BuildingFootprint>(); // 选中框和建筑一样大
        if (fp != null && GridManager.Instance != null)
            _marker.transform.localScale = new Vector3(fp.size.x * GridManager.Instance.cellSize, fp.size.y * GridManager.Instance.cellSize, 1f);
        SpriteRenderer sr = _marker.AddComponent<SpriteRenderer>();
        sr.sprite = PowerBuildings.OutlineSprite();
        sr.color = new Color(1f, 0.85f, 0.3f, 0.95f);
        sr.sortingOrder = 12;
        RenderMats.MakeUnlit(sr);                                   // 2D 光照：选中框一直看得清
    }

    static void Say(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        Debug.Log("[电线] " + msg);
        if (EventLogManager.Instance != null && WorldManager.Instance != null)
            EventLogManager.Instance.AddEvent(WorldManager.Instance.world.day, msg);
    }
}

// 风力发电机的叶片：按风力转（终端机自带的小风机没有画）。
public class TurbineSpinner : MonoBehaviour
{
    public PowerNode node;
    SpriteRenderer _sr;

    void Awake() { _sr = GetComponent<SpriteRenderer>(); }

    void Update()
    {
        bool built = node == null || node.IsBuilt;
        if (_sr != null) { Color c = _sr.color; c.a = built ? 1f : 0.5f; _sr.color = c; }
        if (!built) return;
        float speed = 0f;
        ClimateSystem cs = ClimateSystem.Instance;
        if (cs != null)
        {
            if (cs.StormActive) speed = 900f;
            else if (cs.Wind == WindLevel.Strong) speed = 600f;
            else if (cs.Wind == WindLevel.Moderate) speed = 320f;
            else if (cs.Wind == WindLevel.Breeze) speed = 60f;
        }
        transform.Rotate(0f, 0f, -speed * Time.deltaTime);
    }
}
