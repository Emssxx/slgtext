using System.Collections.Generic;
using UnityEngine;
using NavMeshPlus.Components;

// ============================================================================
// 天然资源（草 / 树 / 石头 / 矿石）：地图上一个可以采的东西。由 TerrainSystem 按地形生成结果摆出来。
//   · 占地 size×size 小格：草和小的 = 1，成年 = 2×2（4 格），巨型 = 3×3（9 格）。
//     占地是树干 / 石头底座；图片比占地大得多（成年树约两个人高，树冠盖住后面的格子）。
//     占着的格子不能建造（草除外：放建筑时自动清掉）。
//   · 前后遮挡：树 / 石头 / 矿石和 NPC 在同一排序层，按"站在地上的那一点"的 Y 排（TerrainSystem 把镜头设成按 Y 轴排序），
//     NPC 走到树后面会被树冠挡住，走到前面会挡住树干。
//   · 2×2 以上挡路：子物体上挂碰撞体 + NavMeshModifier(Not Walkable)，NPC 绕着走；采完重新烘焙 NavMesh。
//     碰撞体在 Ignore Raycast 层、是触发器：不挡鼠标点击 / 悬停，也不会把 NPC 弹开。
//   · 采集（NpcTaskRunner 的 harvest 任务）：NPC 走到旁边干活，累计 WorkSeconds 秒（中途打断进度保留），
//     采完得到 YieldAmount 个 YieldItem，资源消失。产量 / 时间在 TerrainGenerator.cs 的 ResourceInfo 里改。
//   · 鼠标悬停：HoverInfo 显示名字、能采到什么、进度（草太多了不弹框，坐标浮窗里有）。
// ============================================================================
public class ResourceNode : MonoBehaviour
{
    public static readonly List<ResourceNode> All = new List<ResourceNode>();

    public ResourceKind kind;
    public int size = 1;              // 边长（小格）
    public Vector2Int origin;         // 左下角小格
    public int variant;               // 外观变体
    public float progress;            // 已经采了多少秒（打断后保留）
    [System.NonSerialized] public bool depleted;

    string _reservedBy;
    float _reservedAt;

    public bool Blocks => ResourceInfo.Blocks(kind, size);
    public string YieldItem => ResourceInfo.YieldItem(kind);
    public int YieldAmount => ResourceInfo.YieldAmount(kind, size);
    public float WorkSeconds => ResourceInfo.WorkSeconds(kind, size);
    public float Progress01 => WorkSeconds > 0f ? Mathf.Clamp01(progress / WorkSeconds) : 1f;
    public string DisplayName(bool jp) => ResourceInfo.Name(kind, size, jp);

    public bool Contains(Vector2Int c) => c.x >= origin.x && c.x < origin.x + size && c.y >= origin.y && c.y < origin.y + size;

    // 某格上的资源（没有返回 null）。
    public static ResourceNode At(Vector2Int c) => TerrainSystem.ResourceAt(c);

    // 占地中心（世界坐标）。
    public Vector3 Center
    {
        get
        {
            GridManager g = GridManager.Instance;
            return g != null ? g.AreaCenter(origin, new Vector2Int(size, size)) : transform.position;
        }
    }

    // —— 谁在采：干活时每帧续一下，人走开 3 秒后自动失效（被打断也不会一直占着）——
    public void Reserve(string npcId) { _reservedBy = npcId; _reservedAt = Time.time; }
    public void Release(string npcId) { if (_reservedBy == npcId) _reservedBy = null; }
    public string Harvester => !string.IsNullOrEmpty(_reservedBy) && Time.time - _reservedAt < 3f ? _reservedBy : null;
    public bool IsTakenByOther(string npcId) { string h = Harvester; return h != null && h != npcId; }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // 按生成结果摆一个资源（TerrainSystem 调）。
    public static ResourceNode Spawn(ResourceSpawn s, Transform parent, GridManager g)
    {
        int size = Mathf.Clamp(s.size, 1, 3);
        GameObject go = new GameObject(ResourceInfo.Name(s.kind, size, false) + " (" + s.x + "," + s.y + ")");
        go.transform.SetParent(parent, false);
        ResourceNode n = go.AddComponent<ResourceNode>();
        n.kind = s.kind;
        n.size = size;
        n.origin = new Vector2Int(s.x, s.y);
        n.variant = s.variant;

        // 图片轴心（树干根部 / 石头底部）放在占地中心
        go.transform.position = g.AreaCenter(n.origin, new Vector2Int(size, size));
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = TerrainArt.ResourceSprite(s.kind, size, s.variant);
        if (s.kind == ResourceKind.Grass) sr.sortingOrder = -1;  // 草：地面装饰层，永远在脚下
        else
        {
            sr.sortingOrder = 1;                                  // 和 NPC 同一层，按 Y 前后遮挡
            sr.spriteSortPoint = SpriteSortPoint.Pivot;           // 按轴心（根部）排，不按图片中心
        }
        sr.flipX = (s.variant & 0x100) != 0;

        if (n.Blocks) n.AddNavBlocker(g);
        return n;
    }

    void AddNavBlocker(GridManager g)
    {
        GameObject nav = new GameObject("NavBlock");
        nav.layer = 2; // Ignore Raycast
        nav.transform.SetParent(transform, false);
        nav.transform.position = Center;
        BoxCollider2D box = nav.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(size * g.cellSize, size * g.cellSize);
        NavMeshModifier mod = nav.AddComponent<NavMeshModifier>();
        mod.overrideArea = true;
        mod.area = 1; // Not Walkable
    }

    // 悬停信息框的内容。
    public string HoverText(bool jp)
    {
        string s = "<b>" + DisplayName(jp) + "</b>";
        s += "\n<size=85%><color=#C8CFDC>" + (jp ? "採集すると " : "采集可得 ") + ItemName(YieldItem) + " ×" + YieldAmount + "</color></size>";
        string who = Harvester;
        int pct = Mathf.RoundToInt(Progress01 * 100f);
        if (who != null)
        {
            NpcData n = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(who) : null;
            s += "\n<size=85%><color=#9BE29B>" + (n != null ? n.npcName : "?") + (jp ? " が採集中 " : " 正在采 ") + pct + "%</color></size>";
        }
        else if (progress > 0f) s += "\n<size=85%><color=#E0C080>" + (jp ? "採集途中 " : "采了一半 ") + pct + "%</color></size>";
        else s += "\n<size=82%><color=#9AA5B8>" + Hint(jp) + "</color></size>";
        return s;
    }

    string Hint(bool jp)
    {
        switch (kind)
        {
            case ResourceKind.Tree: return jp ? "NPCに「木を切って」と指示" : "对 NPC 说「去砍树」";
            case ResourceKind.Stone: return jp ? "NPCに「石を採って」と指示" : "对 NPC 说「去采石头」";
            case ResourceKind.Ore: return jp ? "NPCに「鉱石を掘って」と指示" : "对 NPC 说「去挖矿」";
            default: return jp ? "NPCに「草を刈って」と指示" : "对 NPC 说「去割草」";
        }
    }

    static string ItemName(string id) => ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(id) : id;

    // 采完：资源消失（TerrainSystem 清掉占地；挡路的重新烘焙 NavMesh）。
    public void Deplete()
    {
        if (depleted) return;
        depleted = true;
        TerrainSystem.OnResourceRemoved(this);
        Destroy(gameObject);
    }

    // NPC 站哪儿采：挡路的站到旁边一圈（离 from 最近、在导航网格上的点）；不挡路的直接站上去。
    public Vector3 StandPoint(Vector3 from)
    {
        GridManager g = GridManager.Instance;
        Vector3 c = Center;
        if (g == null) return c;
        if (!Blocks) return NpcNav.Sample(c, 0.6f, out Vector3 on) ? on : c;

        float best = float.MaxValue;
        Vector3 bestP = c;
        bool found = false;
        for (int x = origin.x - 1; x <= origin.x + size; x++)
            for (int y = origin.y - 1; y <= origin.y + size; y++)
            {
                bool ring = x == origin.x - 1 || y == origin.y - 1 || x == origin.x + size || y == origin.y + size;
                if (!ring || !g.InBounds(x, y)) continue;
                Vector3 w = g.CellToWorld(x, y);
                Vector3 dir = w - c;
                dir.z = 0f;
                w += dir.normalized * 0.15f; // NPC 有半径：往外让一点才站得上去
                if (!NpcNav.Sample(w, 0.5f, out Vector3 p)) continue;
                float d = (p - from).sqrMagnitude;
                if (d < best) { best = d; bestP = p; found = true; }
            }
        if (!found && NpcNav.Sample(c, 1.2f, out Vector3 q)) return q;
        return bestP;
    }
}
