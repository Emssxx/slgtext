using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 电力系统（纯规则）
//   · 电力节点（PowerNode）：AI终端机（自带小型风力发电机）、风力发电机、营地加温器、营地降温器、水源净化器。
//   · 电线：不是建筑，是"连线"——在建造模式里选「电线」，先点一个节点、再点另一个节点就连上（再连一次就拆掉）。
//     连在一起的节点组成一张电网；没连线的设备拿不到电。
//   · 每回合：发电机按当前风力发电（微风 0.1 / 中风 3 / 大风 6 / 亚空间风暴 10，终端机自带的那台算法相同），
//     电存在发电机里（每台最多存 CapPerGenerator）；设备按档位耗电，电不够这一回合就断电。
//   · 加温器 / 降温器：档位 0~3（0 = 关），在 AI 终端机的「能源」页调节；通电时让半径 Radius 内的温度 ± 档位×DegreesPerLevel。
//   · 水源净化器：档位 0~3，建好默认 1 档；只有收集器里有雨水时才用电（净化规则见 WaterWorks）。
// 具体数值（耗电算法等）还没定，全部集中在下面的常量里，方便以后改。
// ============================================================================
public static class PowerGrid
{
    public static float CapPerGenerator = 20f;     // 每台发电机最多存多少电
    public static float ConsumptionPerLevel = 1f;  // 加温器 / 降温器每档每回合耗多少电（耗电算法待定）
    public static float DegreesPerLevel = 5f;      // 每档改变多少度
    public static float Radius = 5f;               // 加温器 / 降温器的作用半径（格）
    public static int MaxLevel = 3;                // 最高档位
    public static float MaxLinkLength = 15f;       // 一根电线最长多少格

    class Link
    {
        public PowerNode a, b;
        public LineRenderer line;
    }

    public class Network
    {
        public int id;
        public readonly List<PowerNode> nodes = new List<PowerNode>();
        public float Stored, Capacity, Produced, Consumed;
        public int Generators;
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
        foreach (Network n in _networks)
        {
            n.Produced = 0f;
            n.Consumed = 0f;
            foreach (PowerNode g in n.nodes)
            {
                if (!g.IsGenerator || !g.IsBuilt) continue;
                g.charge = Mathf.Min(CapPerGenerator, g.charge + windPower);
                n.Produced += windPower;
            }
            foreach (PowerNode d in n.nodes)
            {
                if (!d.IsDevice) continue;
                d.powered = false;
                if (!d.IsBuilt || d.level <= 0) continue;
                if (d.kind == PowerNodeKind.Purifier && WaterWorks.RawTotal <= 0) continue; // 没雨水可净化：待机，不耗电
                float need = d.level * ConsumptionPerLevel;
                if (StoredIn(n) + 0.0001f < need) continue; // 电不够：这一回合断电
                Draw(n, need);
                n.Consumed += need;
                d.powered = true;
            }
            n.Stored = StoredIn(n);
        }
        RefreshLines();
    }

    static float StoredIn(Network n)
    {
        float s = 0f;
        foreach (PowerNode g in n.nodes) if (g.IsGenerator) s += g.charge;
        return s;
    }

    static void Draw(Network n, float amount)
    {
        foreach (PowerNode g in n.nodes)
        {
            if (!g.IsGenerator || amount <= 0f) continue;
            float take = Mathf.Min(g.charge, amount);
            g.charge -= take;
            amount -= take;
        }
    }

    // 某个位置因为加温器 / 降温器而改变了多少度（只算通电的）。
    public static float TemperatureOffsetAt(Vector3 pos)
    {
        float off = 0f;
        float cell = GridManager.Instance != null ? GridManager.Instance.cellSize : 1f;
        foreach (PowerNode d in PowerNode.All)
        {
            if (d == null || !d.IsDevice || !d.powered || d.level <= 0) continue;
            if (d.kind != PowerNodeKind.Heater && d.kind != PowerNodeKind.Cooler) continue;
            Vector3 p = d.transform.position;
            float dx = p.x - pos.x, dy = p.y - pos.y;
            if (dx * dx + dy * dy > (Radius * cell) * (Radius * cell)) continue;
            off += (d.kind == PowerNodeKind.Heater ? 1f : -1f) * d.level * DegreesPerLevel;
        }
        return off;
    }

    // ============ 节点 / 电线 ============

    // 调档位（AI 终端机「能源」页）。调高时如果电网里的电够，这一回合就立刻通电，不用等下一回合。
    public static void SetLevel(PowerNode d, int level)
    {
        if (d == null || !d.IsDevice) return;
        d.level = Mathf.Clamp(level, 0, MaxLevel);
        if (d.level == 0) { d.powered = false; return; }
        if (d.kind == PowerNodeKind.Purifier) return; // 净化器在回合结算时才用电
        if (d.powered || !d.IsBuilt) return;
        Rebuild();
        Network n = d.network;
        float need = d.level * ConsumptionPerLevel;
        if (n != null && StoredIn(n) + 0.0001f >= need)
        {
            Draw(n, need);
            n.Consumed += need;
            n.Stored = StoredIn(n);
            d.powered = true;
        }
    }

    public static PowerNode NodeAt(Vector2Int cell)
    {
        EnsureTerminalNode();
        foreach (PowerNode n in PowerNode.All)
            if (n != null && n.Cell == cell) return n;
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
        float cell = GridManager.Instance != null ? GridManager.Instance.cellSize : 1f;
        if (Vector3.Distance(a.transform.position, b.transform.position) > MaxLinkLength * cell)
            return LanguageGuard.Pick("太远了，电线最长 " + MaxLinkLength + " 格。", "遠すぎる。電線は最長 " + MaxLinkLength + " マスまで。");
        Link nl = new Link { a = a, b = b, line = CreateLine() };
        _links.Add(nl);
        _dirty = true;
        RefreshLines();
        return LanguageGuard.Pick("接上了电线：", "電線をつないだ：") + a.DisplayName + " — " + b.DisplayName;
    }

    // AI 终端机自带一台小型风力发电机：给场景里的终端机补一个电力节点。
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
            foreach (PowerNode n in net.nodes) if (n.IsGenerator) net.Generators++;
            net.Capacity = net.Generators * CapPerGenerator;
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

public enum PowerNodeKind { Terminal, Turbine, Heater, Cooler, Purifier }

// 电力节点：挂在终端机、风力发电机、加温器、降温器、水源净化器上。
public class PowerNode : MonoBehaviour
{
    public static readonly List<PowerNode> All = new List<PowerNode>();

    public PowerNodeKind kind;
    public bool built = true;     // 施工完成才发电 / 用电（建造系统会发 OnBuildPlaced / OnBuildComplete）
    public int level;             // 设备档位（0 = 关）
    public bool powered;          // 这一回合有没有通上电
    public float charge;          // 发电机里存的电

    [System.NonSerialized] public PowerGrid.Network network;

    public bool IsGenerator => kind == PowerNodeKind.Terminal || kind == PowerNodeKind.Turbine;
    public bool IsDevice => kind == PowerNodeKind.Heater || kind == PowerNodeKind.Cooler || kind == PowerNodeKind.Purifier;

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
            Say(LanguageGuard.Pick("这里没有可以接电线的设备（终端机、风力发电机、加温器、降温器、水源净化器）。", "ここには電線をつなげる設備がない（端末・風力発電機・ヒーター・クーラー・浄水器）。"));
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
        SpriteRenderer sr = _marker.AddComponent<SpriteRenderer>();
        sr.sprite = PowerBuildings.OutlineSprite();
        sr.color = new Color(1f, 0.85f, 0.3f, 0.95f);
        sr.sortingOrder = 12;
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
