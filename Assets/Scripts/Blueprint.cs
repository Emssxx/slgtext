using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// ============================================================================
// 建造蓝图：玩家画的 / NPC 设计的建造计划。
//   · 一份蓝图 = 若干项「建什么、放在哪」（BlueprintItem：建筑 id + 占地左下角 + 占地大小）+ 签名（谁设计的）+ 状态。
//   · 玩家：在蓝图规划（BlueprintDesigner）里画好 → 测试寻路 → 保存 → 发给一名 NPC 指挥建造。
//   · NPC：想建东西时不再自己直接放规划区，而是设计一份蓝图交给玩家（左下角弹出他的头像 + 感叹号，
//     对话框里是计划文件）。玩家打开文件：批准 → 他指挥建造；不允许 → 他会问为什么；修改 → 玩家改完再发出去。
//   · 发出去（Send）= 每一项都在地图上放成规划区（ConstructionSite），标上蓝图编号和指挥的人。
//     材料不在放下时扣，而是送到工地 / 开工时才从仓库取（缺材料的工地先等着，采集的人会去采）。
//   · 全部建成 = 蓝图完工（指挥的人在对话框里汇报一声，右上角也有提示）。
// 已保存的蓝图只存在这一局里（换场景就清空）。由用到的地方调 Ensure() 自动创建。
// ============================================================================

public enum BlueprintStatus { Draft, Saved, Pending, Approved, Rejected, Modified, Building, Done }

// 蓝图里的一项：一座建筑放在哪。
[Serializable]
public class BlueprintItem
{
    public string buildId;
    public Vector2Int origin;                    // 占地左下角那一格（小格）
    public Vector2Int size = Vector2Int.one;     // 占地大小（小格）

    // 占地中心那一格（BuildManager.PlaceBlueprint 要传中心格，它会按大小算回左下角）
    public Vector2Int Center => new Vector2Int(origin.x + (size.x - 1) / 2, origin.y + (size.y - 1) / 2);
    public bool Contains(Vector2Int c) => c.x >= origin.x && c.x < origin.x + size.x && c.y >= origin.y && c.y < origin.y + size.y;
    public bool Overlaps(Vector2Int o, Vector2Int s) => o.x < origin.x + size.x && origin.x < o.x + s.x && o.y < origin.y + size.y && origin.y < o.y + s.y;
    public BlueprintItem Clone() => new BlueprintItem { buildId = buildId, origin = origin, size = size };
}

public class Blueprint
{
    public string id;               // bp_001（保存 / 提交时才分配）
    public string name;             // 显示名：营地围墙计划 / 蓝图 #3
    public string authorId;         // BlueprintSystem.AiAuthor = 玩家；否则是 NPC 的 id
    public string authorName;       // 签名
    public string kind;             // NPC 提案的种类（walls / project:xxx / build:xxx）：同一种同时只提一份；玩家画的为空
    public string note;             // NPC 写的理由
    public string sourceId;         // 修改自哪一份
    public string foremanId;        // 指挥建造的人
    public int day;
    public BlueprintStatus status = BlueprintStatus.Draft;
    public readonly List<BlueprintItem> items = new List<BlueprintItem>();
    public int placed, built;       // 发出去以后：放下了几项 / 建成了几项

    public bool ByAI => authorId == BlueprintSystem.AiAuthor;
    public string FileName => (string.IsNullOrEmpty(id) ? "draft" : id) + ".plan";

    public BlueprintItem ItemAt(Vector2Int c)
    {
        for (int i = items.Count - 1; i >= 0; i--) if (items[i].Contains(c)) return items[i];
        return null;
    }
}

public class BlueprintSystem : MonoBehaviour
{
    public const string AiAuthor = "AI";
    public static BlueprintSystem Instance { get; private set; }

    [Header("NPC 交来的蓝图")]
    [Tooltip("同一种计划被否决后，多少秒内不再提")]
    public float rejectCooldown = 240f;
    [Tooltip("同时最多有几份等玩家批准的计划")]
    public int maxPending = 3;

    // 蓝图增减 / 状态变了（对话框里的文件卡片、建筑菜单里的蓝图列表据此刷新）。
    public static event Action OnChanged;

    readonly List<Blueprint> _all = new List<Blueprint>();
    readonly Dictionary<string, float> _rejectedAt = new Dictionary<string, float>();
    int _next = 1;
    float _tick;

    public static BlueprintSystem Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("BlueprintSystem").AddComponent<BlueprintSystem>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        ConstructionManager.OnSiteCompleted += OnSiteCompleted;
    }

    void OnDestroy()
    {
        ConstructionManager.OnSiteCompleted -= OnSiteCompleted;
        if (Instance == this) Instance = null;
    }

    // 建造中的蓝图：工地都没了（建完 / 被取消）= 完工。
    void Update()
    {
        _tick -= Time.unscaledDeltaTime;
        if (_tick > 0f) return;
        _tick = 1f;
        foreach (Blueprint bp in _all)
        {
            if (bp.status != BlueprintStatus.Building || HasOpenSites(bp.id)) continue;
            bp.status = BlueprintStatus.Done;
            NpcData f = Npc(bp.foremanId);
            if (f != null && !f.IsDead && bp.built > 0)
            {
                NpcChat.Post(f.id, ChatRole.Npc, LF("bp_npc_done", bp.name, bp.built), true);
                IdleChatRules.AddMemory(f, Day(), LF("bp_mem_done", bp.name));
            }
            if (bp.built > 0 && HudToasts.Instance != null)
                HudToasts.Instance.Show("plan : done", LF("toast_plan_done", bp.name), f != null ? LF("toast_plan_foreman", f.npcName) : null);
            Changed();
        }
    }

    void OnSiteCompleted(ConstructionSite site)
    {
        if (site == null || string.IsNullOrEmpty(site.blueprintId)) return;
        Blueprint bp = Find(site.blueprintId);
        if (bp != null) { bp.built++; Changed(); }
    }

    // ============ 查询 ============

    public static IReadOnlyList<Blueprint> All => Ensure()._all;

    public static Blueprint Find(string id)
    {
        if (string.IsNullOrEmpty(id) || Instance == null) return null;
        foreach (Blueprint bp in Instance._all) if (bp.id == id) return bp;
        return null;
    }

    public static int PendingCount
    {
        get
        {
            if (Instance == null) return 0;
            int k = 0;
            foreach (Blueprint bp in Instance._all) if (bp.status == BlueprintStatus.Pending) k++;
            return k;
        }
    }

    // 对话框里的计划文件：存成一条系统消息「[[bp:bp_003]]」（提示词默认不读系统消息，不会干扰 AI）。
    public static string FileMarker(string id) => "[[bp:" + id + "]]";

    public static bool TryParseMarker(string text, out string id)
    {
        id = null;
        if (string.IsNullOrEmpty(text) || !text.StartsWith("[[bp:") || !text.EndsWith("]]")) return false;
        id = text.Substring(5, text.Length - 7);
        return id.Length > 0;
    }

    public static BuildableData FindData(string id)
    {
        BuildManager bm = BuildManager.Instance;
        if (bm == null || bm.buildables == null || string.IsNullOrEmpty(id)) return null;
        foreach (BuildableData d in bm.buildables) if (d != null && d.id == id) return d;
        return null;
    }

    // 这份蓝图（bpId = null：所有蓝图）还有没建完的工地吗。
    public static bool HasOpenSites(string bpId)
    {
        foreach (ConstructionSite s in ConstructionSite.All)
            if (s != null && !string.IsNullOrEmpty(s.blueprintId) && (bpId == null || s.blueprintId == bpId)) return true;
        return false;
    }

    public static List<ConstructionSite> SitesOf(string bpId)
    {
        List<ConstructionSite> list = new List<ConstructionSite>();
        foreach (ConstructionSite s in ConstructionSite.All)
            if (s != null && !string.IsNullOrEmpty(s.blueprintId) && (bpId == null || s.blueprintId == bpId)) list.Add(s);
        return list;
    }

    // 蓝图要的材料合计。
    public static Dictionary<string, int> TotalCost(Blueprint bp)
    {
        Dictionary<string, int> sum = new Dictionary<string, int>();
        if (bp == null) return sum;
        foreach (BlueprintItem it in bp.items) AddCost(sum, NpcIntentBrain.CostOf(FindData(it.buildId)));
        return sum;
    }

    static void AddCost(Dictionary<string, int> sum, MaterialCost[] cost)
    {
        if (cost == null) return;
        foreach (MaterialCost c in cost)
        {
            if (c == null || c.amount <= 0 || string.IsNullOrEmpty(c.materialName)) continue;
            sum.TryGetValue(c.materialName, out int v);
            sum[c.materialName] = v + c.amount;
        }
    }

    // 「石材×24（有 10）　木材×2」：够 = 绿，不够 = 红。
    public static string CostLine(Blueprint bp)
    {
        Dictionary<string, int> sum = TotalCost(bp);
        if (sum.Count == 0) return L("cost_none");
        StringBuilder sb = new StringBuilder();
        foreach (KeyValuePair<string, int> kv in sum)
        {
            int have = ProgressionRules.Have(kv.Key);
            if (sb.Length > 0) sb.Append("　");
            sb.Append(ItemName(kv.Key)).Append("×").Append(kv.Value)
              .Append("<color=").Append(have >= kv.Value ? "#74DB8C" : "#FF6B6B").Append(">（").Append(LF("bp_have", have)).Append("）</color>");
        }
        return sb.ToString();
    }

    // 「围墙×12、储物箱×1」
    public static string ItemLine(Blueprint bp)
    {
        if (bp == null || bp.items.Count == 0) return L("bp_empty");
        Dictionary<string, int> count = new Dictionary<string, int>();
        List<string> order = new List<string>();
        foreach (BlueprintItem it in bp.items)
        {
            if (!count.ContainsKey(it.buildId)) { count[it.buildId] = 0; order.Add(it.buildId); }
            count[it.buildId]++;
        }
        List<string> parts = new List<string>();
        foreach (string id in order)
        {
            BuildableData d = FindData(id);
            parts.Add((d != null ? d.LocalizedName : id) + "×" + count[id]);
        }
        return string.Join("、", parts);
    }

    public static string StatusText(BlueprintStatus st) => L("bp_status_" + st.ToString().ToLowerInvariant());

    // 现在缺得最多的材料（建造中的蓝图里材料还没到的工地 + 等批准的蓝图，减去仓库里有的）；不缺返回 null。
    // bpId 不为空时只看这一份。采集的人据此去采。
    public static string MissingMaterial(string bpId)
    {
        Dictionary<string, int> need = new Dictionary<string, int>();
        foreach (ConstructionSite s in ConstructionSite.All)
            if (s != null && !s.materialsIn && !string.IsNullOrEmpty(s.blueprintId) && (bpId == null || s.blueprintId == bpId))
                AddCost(need, s.cost);
        if (Instance != null && bpId == null)
            foreach (Blueprint bp in Instance._all)
                if (bp.status == BlueprintStatus.Pending)
                    foreach (BlueprintItem it in bp.items) AddCost(need, NpcIntentBrain.CostOf(FindData(it.buildId)));
        string best = null;
        int bestLack = 0;
        foreach (KeyValuePair<string, int> kv in need)
        {
            int lack = kv.Value - ProgressionRules.Have(kv.Key);
            if (lack > bestLack) { bestLack = lack; best = kv.Key; }
        }
        return best;
    }

    // 这一块地现在能不能放进蓝图：越界 / 没探明 / 地形 / 已被占 / 和这份蓝图里别的项重叠（except 那一项不算）。
    public static bool CheckArea(Vector2Int origin, Vector2Int size, Blueprint bp, BlueprintItem except, out string why)
    {
        why = null;
        GridManager g = GridManager.Instance;
        if (g == null) { why = "?"; return false; }
        for (int x = origin.x; x < origin.x + size.x; x++)
            for (int y = origin.y; y < origin.y + size.y; y++)
            {
                if (!g.InBounds(x, y)) { why = L("bp_why_bounds"); return false; }
                if (!Explored(x, y)) { why = L("bp_why_fog"); return false; }
                if (g.IsBuildable(x, y)) continue;
                Vector2Int c = new Vector2Int(x, y);
                if (g.GetOccupied(x, y) != null) why = L("bp_why_occupied");
                else if (TerrainSystem.IsWall(c)) why = L("bp_why_wall");
                else if (TerrainSystem.TypeAt(c) == TerrainType.Water) why = L("bp_why_water");
                else if (TerrainSystem.TypeAt(c) == TerrainType.Swamp) why = L("bp_why_swamp");
                else why = L("bp_why_resource");
                return false;
            }
        if (bp != null)
            foreach (BlueprintItem it in bp.items)
                if (it != except && it.Overlaps(origin, size)) { why = L("bp_why_overlap"); return false; }
        return true;
    }

    // 迷雾：没有迷雾系统 = 都算探明。
    public static bool Explored(int x, int y) => FogManager.Instance == null || FogManager.Instance.GetState(x, y) != FogState.Unexplored;

    // ============ 玩家的蓝图 ============

    // 保存（第一次保存时分配编号）。
    public static Blueprint Save(Blueprint bp)
    {
        if (bp == null) return null;
        BlueprintSystem s = Ensure();
        if (string.IsNullOrEmpty(bp.id)) bp.id = s.NextId();
        if (string.IsNullOrEmpty(bp.name)) bp.name = LF("bp_name_player", s._next - 1);
        if (!s._all.Contains(bp)) s._all.Add(bp);
        if (bp.status == BlueprintStatus.Draft) bp.status = BlueprintStatus.Saved;
        if (bp.day == 0) bp.day = Day();
        Changed();
        return bp;
    }

    // 新的空白蓝图（玩家的草稿，还没编号）。
    public static Blueprint NewDraft()
    {
        return new Blueprint { authorId = AiAuthor, authorName = AiAuthor, status = BlueprintStatus.Draft, day = Day() };
    }

    // 复制一份来改（NPC 交来的计划选「修改」、或者已经发出去的蓝图想再来一份）。
    public static Blueprint Modify(Blueprint src)
    {
        if (src == null) return null;
        Blueprint copy = NewDraft();
        copy.name = src.name;
        copy.sourceId = src.id;
        copy.note = src.note;
        copy.authorName = src.ByAI ? AiAuthor : LF("bp_sign_modified", src.authorName);
        foreach (BlueprintItem it in src.items) copy.items.Add(it.Clone());
        if (src.status == BlueprintStatus.Pending)
        {
            src.status = BlueprintStatus.Modified;
            NpcData n = Npc(src.authorId);
            if (n != null)
            {
                NpcChat.Post(n.id, ChatRole.System, LF("bp_sys_modifying", src.name));
                IdleChatRules.AddMemory(n, Day(), LF("bp_mem_modified", src.name));
            }
            if (BlueprintInbox.Instance != null) BlueprintInbox.Instance.Remove(src.id);
        }
        Changed();
        return copy;
    }

    // 发给一名 NPC 指挥建造：每一项放成规划区。返回放下了几项（放不下的写在对话框里）。
    public static int Send(Blueprint bp, string foremanId) => Send(bp, foremanId, false);

    // 玩家在对话里直接叫某人建某样东西（"去把 AI 终端机建起来"）：玩家亲口下的令 = 不用再批。
    //   · 已经有这种建筑的工地：让他去接手指挥（helping = true）。
    //   · 围墙：把营地还空着的墙位一次画全；其他建筑：代码在营地里挑位置。
    //   · 画成一份蓝图（签名 AI）发给他 → 放下规划区，他来指挥建造（缺材料先采 / 送材料 / 施工）。
    //   NPC 的回答由 NpcAgent 让 LLM 配，这里不发固定台词。返回 null 且 why 不为空 = 建不了。
    public static Blueprint OrderBuild(BuildableData d, NpcData foreman, out int placed, out bool helping, out string why)
    {
        placed = 0;
        helping = false;
        why = null;
        GridManager g = GridManager.Instance;
        if (d == null || foreman == null || g == null || BuildManager.Instance == null) { why = L("fail_build_spot"); return null; }

        foreach (ConstructionSite site in ConstructionSite.All)
        {
            if (site == null || site.data == null || site.data.id != d.id) continue;
            helping = true;
            if (!string.IsNullOrEmpty(site.blueprintId) && Find(site.blueprintId) != null) NpcIntentBrain.SetForeman(foreman.id, site.blueprintId);
            else if (ConstructionManager.Instance != null) ConstructionManager.Instance.Assign(foreman.id, site);
            return Find(site.blueprintId);
        }
        if (!ProgressionRules.IsBuildableUnlocked(d, out why)) return null;

        bool wall = d == CampZone.WallData() || d.id == "wall";
        List<BlueprintItem> items = wall ? NpcIntentBrain.OpenWallItems() : new List<BlueprintItem>();
        if (wall && items.Count == 0) { why = L("fail_build_walls_done"); return null; }
        if (!wall)
        {
            if (!NpcIntentBrain.PickSpot(d, out Vector2Int cell)) { why = L("fail_build_spot"); return null; }
            Vector2Int size = d.FootprintSize(g.cellSize);
            items.Add(new BlueprintItem { buildId = d.id, origin = g.FootprintOrigin(cell, size), size = size });
        }
        Blueprint bp = NewDraft();
        bp.name = wall ? L("bp_name_walls") : LF("bp_name_build", d.LocalizedName);
        bp.kind = wall ? "walls" : "build:" + d.id;
        bp.note = LF("bp_note_order", foreman.npcName);
        bp.items.AddRange(items);
        placed = Send(bp, foreman.id, false, true);
        if (placed == 0) { why = L("fail_build_spot"); return null; }
        return bp;
    }

    // quiet = true：不让 NPC 说固定的回答（玩家在对话里下的令，回答交给 LLM）
    static int Send(Blueprint bp, string foremanId, bool approval, bool quiet = false)
    {
        if (bp == null) return 0;
        Save(bp);
        bp.foremanId = foremanId;
        int placed = Execute(bp, out List<string> skipped);
        bp.placed = placed;
        bp.built = 0;
        bp.status = placed > 0 ? BlueprintStatus.Building : (approval ? BlueprintStatus.Approved : BlueprintStatus.Saved);

        NpcData f = Npc(foremanId);
        if (f != null)
        {
            if (!approval) NpcChat.Post(f.id, ChatRole.System, FileMarker(bp.id));   // 玩家发来的文件也显示在对话框里
            string skip = skipped.Count > 0 ? LF("bp_sys_skipped", skipped.Count, string.Join("、", skipped)) : "";
            NpcChat.Post(f.id, ChatRole.System, LF(approval ? "bp_sys_approved" : "bp_sys_sent", bp.name, placed) + skip);
            if (!f.IsDead)
            {
                string line = placed == 0 ? L("bp_npc_nothing") : L(approval ? "bp_npc_ack_approve" : "bp_npc_ack_send");
                if (!quiet) NpcChat.Post(f.id, ChatRole.Npc, line, true);
                IdleChatRules.AddMemory(f, Day(), LF(approval ? "bp_mem_approved" : "bp_mem_sent", bp.name));
            }
            if (placed > 0) NpcIntentBrain.SetForeman(f.id, bp.id);
        }
        if (EventLogManager.Instance != null)
            EventLogManager.Instance.AddEvent(Day(), LF("bp_log_sent", bp.name, f != null ? f.npcName : "-", placed));
        if (BlueprintInbox.Instance != null) BlueprintInbox.Instance.Remove(bp.id);
        Changed();
        return placed;
    }

    // 每一项放成规划区（标上蓝图编号；材料等送到 / 开工时再扣）。
    static int Execute(Blueprint bp, out List<string> skipped)
    {
        skipped = new List<string>();
        BuildManager bm = BuildManager.Instance;
        GridManager g = GridManager.Instance;
        if (bm == null || g == null) return 0;
        int placed = 0;
        foreach (BlueprintItem it in bp.items)
        {
            BuildableData d = FindData(it.buildId);
            string where = g.CellToLabel(it.Center.x, it.Center.y);
            if (d == null) { skipped.Add(it.buildId + "（" + where + "）"); continue; }
            Vector2Int size = d.FootprintSize(g.cellSize);
            if (!ProgressionRules.IsBuildableUnlocked(d, out _) || !g.IsAreaBuildable(it.origin, size))
            {
                skipped.Add(d.LocalizedName + "（" + where + "）");
                continue;
            }
            Vector2Int center = new Vector2Int(it.origin.x + (size.x - 1) / 2, it.origin.y + (size.y - 1) / 2);
            ConstructionSite site = bm.PlaceBlueprint(d, center);
            if (site == null) { skipped.Add(d.LocalizedName + "（" + where + "）"); continue; }
            site.blueprintId = bp.id;
            site.foremanId = bp.foremanId;
            site.cost = NpcIntentBrain.CostOf(d);
            site.materialsIn = site.cost == null || site.cost.Length == 0;
            placed++;
        }
        return placed;
    }

    // ============ NPC 交来的蓝图 ============

    // NPC 能不能提这一种计划：同一种还在等批准 / 还在建 → 不提；刚被否决 → 冷却；等批准的太多 → 不提。
    public static bool CanPropose(string kind)
    {
        BlueprintSystem s = Ensure();
        int pending = 0;
        foreach (Blueprint bp in s._all)
        {
            if (bp.status == BlueprintStatus.Pending) pending++;
            if (!string.IsNullOrEmpty(kind) && bp.kind == kind && (bp.status == BlueprintStatus.Pending || bp.status == BlueprintStatus.Building)) return false;
        }
        if (pending >= s.maxPending) return false;
        if (!string.IsNullOrEmpty(kind) && s._rejectedAt.TryGetValue(kind, out float t) && Time.time - t < s.rejectCooldown) return false;
        return true;
    }

    // 同一种计划现在是不是在等批准 / 在建。
    public static bool HasActive(string kind)
    {
        if (Instance == null || string.IsNullOrEmpty(kind)) return false;
        foreach (Blueprint bp in Instance._all)
            if (bp.kind == kind && (bp.status == BlueprintStatus.Pending || bp.status == BlueprintStatus.Building)) return true;
        return false;
    }

    // NPC 设计好一份蓝图交给玩家：左下角弹出他的头像 + 感叹号，对话框里是他说的话和计划文件。
    public static Blueprint Propose(NpcData author, string kind, string name, string note, List<BlueprintItem> items, string dialogue, bool bubble = true)
    {
        if (author == null || author.IsDead || items == null || items.Count == 0) return null;
        BlueprintSystem s = Ensure();
        Blueprint bp = new Blueprint
        {
            id = s.NextId(), name = name, authorId = author.id, authorName = author.npcName,
            kind = kind, note = note, day = Day(), status = BlueprintStatus.Pending,
        };
        bp.items.AddRange(items);
        s._all.Add(bp);
        if (!string.IsNullOrEmpty(dialogue)) NpcChat.Post(author.id, ChatRole.Npc, dialogue, bubble);
        NpcChat.Post(author.id, ChatRole.System, FileMarker(bp.id));
        IdleChatRules.AddMemory(author, Day(), LF("bp_mem_proposed", bp.name));
        if (LogManager.Instance != null) LogManager.Instance.AddLog(author.npcName, LF("bp_log_proposed", author.npcName, bp.name, bp.items.Count), author.id);
        BlueprintInbox.Ensure().Push(bp);
        Changed();
        return bp;
    }

    // 批准：提交的人指挥建造（他不在了就交给第一个活着的人）。
    public static int Approve(Blueprint bp)
    {
        if (bp == null || bp.status != BlueprintStatus.Pending) return 0;
        NpcData a = Npc(bp.authorId);
        string foreman = a != null && !a.IsDead ? a.id : FirstAliveId();
        return Send(bp, foreman, true);
    }

    // 不允许：他会问为什么（玩家在对话框里回答，AI 看得到这件事）。同一种计划一段时间内不再提。
    public static void Reject(Blueprint bp)
    {
        if (bp == null || bp.status != BlueprintStatus.Pending) return;
        bp.status = BlueprintStatus.Rejected;
        Ensure()._rejectedAt[string.IsNullOrEmpty(bp.kind) ? bp.id : bp.kind] = Time.time;
        NpcData n = Npc(bp.authorId);
        if (n != null)
        {
            NpcChat.Post(n.id, ChatRole.System, LF("bp_sys_rejected", bp.name));
            if (!n.IsDead) NpcChat.Post(n.id, ChatRole.Npc, L("bp_npc_ask_reason"), true);
            IdleChatRules.AddMemory(n, Day(), LF("bp_mem_rejected", bp.name));
        }
        if (BlueprintInbox.Instance != null) BlueprintInbox.Instance.Remove(bp.id);
        Changed();
    }

    // ============ 小工具 ============

    string NextId() => "bp_" + (_next++).ToString("000");

    static void Changed()
    {
        try { OnChanged?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    static NpcData Npc(string id) => string.IsNullOrEmpty(id) || WorldManager.Instance == null ? null : WorldManager.Instance.GetNpcById(id);

    static string FirstAliveId()
    {
        if (WorldManager.Instance == null || WorldManager.Instance.world == null) return null;
        foreach (NpcData n in WorldManager.Instance.world.npcs) if (n != null && !n.IsDead) return n.id;
        return null;
    }

    static int Day() => WorldManager.Instance != null && WorldManager.Instance.world != null ? WorldManager.Instance.world.day : 1;

    static string ItemName(string id) => ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(id) : GameLexicon.NameOf(id);
    static string L(string key) => ProgressionRules.L(key);
    static string LF(string key, params object[] a) => ProgressionRules.LF(key, a);
}
