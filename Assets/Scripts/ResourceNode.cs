using System.Collections.Generic;
using UnityEngine;
using NavMeshPlus.Components;

// ============================================================================
// 天然资源（草 / 树 / 石头 / 矿石）：地图上一个可以采的东西。由 TerrainSystem 按地形生成结果摆出来。
//   · 占地 size×size 小格：草和小的 = 1，成年 = 2×2（4 格），巨型 = 3×3（9 格）。
//     占地是树干 / 石头底座；图片比占地大得多（成年树约两个人高，树冠盖住后面的格子）。
//     占着的格子不能建造（草除外：放建筑时自动清掉）。
//   · 前后遮挡：石头 / 矿石和 NPC 在同一排序层，按"站在地上的那一点"的 Y 排（TerrainSystem 把镜头设成按 Y 轴排序）。
//     树在低一层（0，和建筑同层），永远画在 NPC 下面，不会盖住 NPC。
//   · 2×2 以上挡路：子物体上挂碰撞体 + NavMeshModifier(Not Walkable)，NPC 绕着走；采完重新烘焙 NavMesh。
//     碰撞体在 Ignore Raycast 层、是触发器：不挡鼠标点击 / 悬停，也不会把 NPC 弹开。
//   · 采集（NpcTaskRunner 的 harvest 任务）：NPC 走到旁边干活，累计 WorkSeconds 秒（中途打断进度保留），
//     采完得到 YieldAmount 个 YieldItem，资源消失。产量 / 时间在 TerrainGenerator.cs 的 ResourceInfo 里改。
//   · 鼠标悬停：HoverInfo 显示名字、能采到什么、进度（草太多了不弹框，坐标浮窗里有）。
//   · 树的阴影：脚下一圈柔边椭圆 + 树的剪影压扁斜投在地上（AddShadow），画在第 -1 层，NPC 走在阴影上面。
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
        Sprite custom = TerrainSystem.Instance != null ? TerrainSystem.Instance.CustomResourceSprite(s.kind, size, s.variant) : null;
        sr.sprite = custom != null ? custom : TerrainArt.ResourceSprite(s.kind, size, s.variant); // Inspector 里填了图就用填的
        if (s.kind == ResourceKind.Grass) sr.sortingOrder = -1;  // 草：地面装饰层，永远在脚下
        else
        {
            // 树：第 0 层（和建筑同层），永远在 NPC（1）下面，NPC 走到树后面也不会被树冠挡住
            // 石头 / 矿石：还是和 NPC 同一层，按 Y 前后遮挡
            sr.sortingOrder = s.kind == ResourceKind.Tree ? 0 : 1;
            sr.spriteSortPoint = SpriteSortPoint.Pivot;           // 按轴心（根部）排，不按图片中心
        }
        sr.flipX = (s.variant & 0x100) != 0;
        if (s.kind == ResourceKind.Tree) n.AddShadow(sr);         // 树（树苗 / 成年树 / 巨型树）加阴影

        if (n.Blocks) n.AddNavBlocker(g);
        return n;
    }

    // ============ 树的阴影：脚下椭圆 + 斜投剪影 ============
    // 两块阴影都画在第 -1 层（和草同层）：比建筑（0）、NPC（1）低，所以 NPC 能走在阴影上，阴影也不会盖住建筑。
    // 想调效果，改下面这几个数就行。
    const float ShadowHeight = 0.35f;  // 剪影压扁到树高的多少（0.35 = 35%）
    const float ShadowSlant = 0.385f;  // 剪影往右斜多少（树每高 1，影子顶端往右偏 0.385）
    const float ContactWidth = 0.55f;  // 脚下椭圆的宽度 = 树图宽度 × 这个数
    static readonly Color CastColor = new Color(0.10f, 0.086f, 0.07f, 0.42f);    // 剪影的颜色（最后一个数是不透明度）
    static readonly Color ContactColor = new Color(0.10f, 0.086f, 0.07f, 0.45f); // 椭圆的颜色
    static Sprite _contactSprite;      // 椭圆图：所有树共用一张，只生成一次

    void AddShadow(SpriteRenderer tree)
    {
        if (tree.sprite == null) return;                          // 没有图就不加阴影

        // —— 1. 斜投剪影 ——
        // Unity 的 Transform 只能旋转和缩放，不能直接"斜切"。
        // 但"转一下 → 拉伸 → 再转一下"三步叠起来就等于斜切：外层物体负责前两步，里层物体负责最后一步。
        ShearSteps(ShadowSlant, ShadowHeight, out float outerDeg, out Vector2 stretch, out float innerDeg);
        GameObject outer = new GameObject("Shadow");              // 外层：旋转 + 拉伸
        outer.transform.SetParent(transform, false);              // 挂在树下面，原点 = 树根
        outer.transform.localRotation = Quaternion.Euler(0f, 0f, outerDeg);
        outer.transform.localScale = new Vector3(stretch.x, stretch.y, 1f);
        GameObject inner = new GameObject("Cast");                // 里层：再旋转一下
        inner.transform.SetParent(outer.transform, false);
        inner.transform.localRotation = Quaternion.Euler(0f, 0f, innerDeg);
        SpriteRenderer cast = inner.AddComponent<SpriteRenderer>();
        cast.sprite = tree.sprite;                                // 直接用树自己的图当剪影
        cast.flipX = tree.flipX;                                  // 树左右翻了，剪影也跟着翻（光照方向不变）
        cast.color = CastColor;                                   // 染成半透明的深色
        cast.sortingOrder = -1;                                   // 画在 NPC / 建筑下面

        // —— 2. 脚下椭圆 ——
        GameObject contact = new GameObject("ContactShadow");
        contact.transform.SetParent(transform, false);            // 中心放在树根
        float w = tree.sprite.bounds.size.x * ContactWidth;       // 椭圆宽度跟着树的大小走
        contact.transform.localScale = new Vector3(w, w * 0.32f, 1f); // 压成扁椭圆：高 = 宽 × 0.32
        SpriteRenderer cs = contact.AddComponent<SpriteRenderer>();
        cs.sprite = ContactSprite();                              // 柔边圆图
        cs.color = ContactColor;                                  // 染成半透明的深色
        cs.sortingOrder = -1;                                     // 画在 NPC / 建筑下面
    }

    // 生成一张 64×64 的柔边圆（中间实、边缘慢慢变透明），大小 1 单位。
    static Sprite ContactSprite()
    {
        if (_contactSprite != null) return _contactSprite;        // 已经生成过就直接用
        const int n = 64;                                         // 图片边长（像素）
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.name = "TreeContactShadow";
        t.wrapMode = TextureWrapMode.Clamp;                       // 边缘不重复
        t.filterMode = FilterMode.Bilinear;                       // 放大后平滑，不出锯齿
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f;              // 横向位置：-1（左边）~ 1（右边）
                float dy = (y + 0.5f) / n * 2f - 1f;              // 纵向位置：-1（下边）~ 1（上边）
                float r = Mathf.Sqrt(dx * dx + dy * dy);          // 离中心多远：0 = 中心，1 = 边缘
                float u = Mathf.InverseLerp(0.35f, 1f, r);        // 0.35 以内 = 0，边缘 = 1
                float a = 1f - u * u * (3f - 2f * u);             // 平滑过渡：中心全实，往外渐渐透明
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f)); // 白色，实际颜色由 SpriteRenderer.color 决定
            }
        t.SetPixels32(px);
        t.Apply(false);
        _contactSprite = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n); // 轴心在中心，64 像素 = 1 单位
        return _contactSprite;
    }

    // 把"斜切 + 压扁"拆成三步：外层旋转 → 拉伸 → 里层旋转（数学上叫 2×2 矩阵的奇异值分解，公式照抄即可）。
    // 目标效果：x' = x + slant × y，y' = height × y —— 树根（y = 0）不动，越高的地方被压得越扁、往右偏得越多。
    static void ShearSteps(float slant, float height, out float outerDeg, out Vector2 stretch, out float innerDeg)
    {
        float a = 1f, b = slant, c = 0f, d = height;              // 目标矩阵 [a b; c d]
        float e = (a + d) / 2f, f = (a - d) / 2f;                 // 以下是固定公式
        float g = (c + b) / 2f, h = (c - b) / 2f;
        float q = Mathf.Sqrt(e * e + h * h), r = Mathf.Sqrt(f * f + g * g);
        float a1 = Mathf.Atan2(g, f), a2 = Mathf.Atan2(h, e);
        stretch = new Vector2(q + r, q - r);                      // 两个方向各拉伸多少倍
        outerDeg = (a2 + a1) / 2f * Mathf.Rad2Deg;                // 外层转多少度
        innerDeg = (a2 - a1) / 2f * Mathf.Rad2Deg;                // 里层转多少度
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
