using System;
using System.Collections.Generic;

// ============================================================================
// 地形生成器（纯规则，不依赖场景，可单独测试）
//   输入：地图布局 TerrainLayout（多大、哪些格是墙、营地在哪）+ 种子（整数，见 MapSeed）
//   输出：TerrainMap = 每个小格的地形 + 天然资源的位置和大小
// 同样的布局 + 同样的种子 → 永远是同一张地图（用自带的随机数，不用 UnityEngine.Random，换电脑也一样）。
// 地图多大都行：湖的个数、沼泽和资源的数量都按面积自动放大（默认地图 72×33 块地砖 = 216×99 小格）。
//
// 规则（对应设计）：
//   【土地】默认地形；连成片的范围不小于 3×3（零碎的一两格土地并进旁边的沼泽 / 水面）。
//   【营地地砖】以 NPC 生成点为中心的矩形（按地砖大格对齐），边上偶尔缺几块（废弃营地）。
//   【沼泽】基本是 3×3 的类圆小块（偶尔 4~5 格），随机散布，一部分长在湖边。
//   【水面】带噪声边缘的湖（有大湖也有小水塘），每片不小于 3×3；每放一片湖都检查：营地还能走到原来能走到的所有陆地。
//   【资源】草 1 格；树 / 石头 / 矿石：小 = 1 格，成年 = 2×2（4 格），巨型 = 3×3（9 格）。
//           占地是树干 / 石头底座，图片比占地大得多（树冠往上长，成年树约两个人高）。
//           2×2 以上会挡路：和墙 / 水 / 别的挡路资源之间至少留 3 格空地，保证 NPC 走得过去。
//           大矿石旁边会再长出几块小矿石（矿脉）。
//   营地 + 外圈 6 格是保护区：不生成水、沼泽、树石矿（留给玩家建造），只长草。
// 每个种子还会随机一种「地貌」（平原 / 湿地 / 林地 / 岩地），让不同种子的差别更明显。
// 各种数值（移速、产量、采集时间、密度）都集中在下面的 TerrainInfo / ResourceInfo / Params 里，改平衡改这里。
// ============================================================================

public enum TerrainType : byte { Soil = 0, CampFloor = 1, Swamp = 2, Water = 3 }
public enum ResourceKind : byte { Grass = 0, Tree = 1, Stone = 2, Ore = 3 }
public enum TerrainFlavor : byte { Plain = 0, Wetland = 1, Forest = 2, Rocky = 3 }

// 一个天然资源（生成器输出；TerrainSystem 按它在场景里摆 ResourceNode）。
[Serializable]
public struct ResourceSpawn
{
    public ResourceKind kind;
    public int size;      // 边长（小格）：1 / 2 / 3
    public int x, y;      // 左下角小格
    public int variant;   // 外观变体（画图用的随机数）
}

// 各种地形的规则数值。
public static class TerrainInfo
{
    // NPC 移速倍率
    public static float SpeedFactor(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Soil: return 0.9f;   // 未铺装：-10%
            case TerrainType.Swamp: return 0.2f;  // -80%
            case TerrainType.Water: return 0f;    // 不可行走（NavMesh 里挖掉了）
            default: return 1f;                   // 营地地砖：基础移速
        }
    }

    public static bool Walkable(TerrainType t) => t != TerrainType.Water;
    public static bool Buildable(TerrainType t) => t == TerrainType.Soil || t == TerrainType.CampFloor;
    public static bool CanGrowPlants(TerrainType t) => t == TerrainType.Soil; // 以后种植物用
    public static bool IsWaterSource(TerrainType t) => t == TerrainType.Water;   // 可取水 / 钓鱼

    public static string Name(TerrainType t, bool jp)
    {
        switch (t)
        {
            case TerrainType.CampFloor: return jp ? "拠点の床タイル" : "营地地砖";
            case TerrainType.Swamp: return jp ? "沼地" : "沼泽";
            case TerrainType.Water: return "水面";
            default: return "土地";
        }
    }

    public static string FlavorName(TerrainFlavor f, bool jp)
    {
        switch (f)
        {
            case TerrainFlavor.Wetland: return "湿地";
            case TerrainFlavor.Forest: return jp ? "森林" : "林地";
            case TerrainFlavor.Rocky: return jp ? "岩場" : "岩地";
            default: return "平原";
        }
    }
}

// 天然资源的规则数值。
public static class ResourceInfo
{
    public const string WoodItem = "wood";
    public const string StoneItem = "stone";
    public const string OreItem = "ore";
    public const string FiberItem = "fiber";
    public const string FishItem = "subspace_protein";

    // 2×2 以上的树 / 石头 / 矿石挡路（NavMesh 挖掉）；草和 1 格的小东西 NPC 可以踩过去。
    public static bool Blocks(ResourceKind k, int size) => k != ResourceKind.Grass && size >= 2;

    public static string YieldItem(ResourceKind k)
    {
        switch (k)
        {
            case ResourceKind.Tree: return WoodItem;
            case ResourceKind.Stone: return StoneItem;
            case ResourceKind.Ore: return OreItem;
            default: return FiberItem;
        }
    }

    // 采完一整个能得多少
    public static int YieldAmount(ResourceKind k, int size)
    {
        size = Math.Max(1, Math.Min(3, size));
        switch (k)
        {
            case ResourceKind.Tree: return size == 1 ? 1 : size == 2 ? 4 : 9;
            case ResourceKind.Stone: return size == 1 ? 1 : size == 2 ? 4 : 9;
            case ResourceKind.Ore: return size == 1 ? 1 : size == 2 ? 3 : 7;
            default: return 1;
        }
    }

    // 采完一整个要干多少秒（健康的人；疲惫 / 生病会变慢，见 NpcHealth.WorkSpeed）
    public static float WorkSeconds(ResourceKind k, int size)
    {
        size = Math.Max(1, Math.Min(3, size));
        switch (k)
        {
            case ResourceKind.Tree: return size == 1 ? 3f : size == 2 ? 8f : 16f;
            case ResourceKind.Stone: return size == 1 ? 3f : size == 2 ? 10f : 20f;
            case ResourceKind.Ore: return size == 1 ? 4f : size == 2 ? 12f : 24f;
            default: return 2f;
        }
    }

    public static string Name(ResourceKind k, int size, bool jp)
    {
        switch (k)
        {
            case ResourceKind.Tree: return size <= 1 ? (jp ? "苗木" : "树苗") : size == 2 ? (jp ? "成木" : "成年树") : (jp ? "巨木" : "巨型树");
            case ResourceKind.Stone: return size <= 1 ? (jp ? "小石" : "小石头") : size == 2 ? (jp ? "岩" : "石头") : (jp ? "巨岩" : "巨石");
            case ResourceKind.Ore: return size <= 1 ? (jp ? "小さな鉱石" : "小矿石") : size == 2 ? (jp ? "鉱石" : "矿石") : (jp ? "巨大鉱石" : "巨型矿石");
            default: return "草";
        }
    }

    // 指令里的资源记号 → 种类（"wood" 树、"stone" 石头、"ore" 矿石、"fiber" 草）。钓鱼 / 打水不是资源点，返回 false。
    public static bool TryParseKind(string res, out ResourceKind kind)
    {
        kind = ResourceKind.Tree;
        switch (res)
        {
            case WoodItem: kind = ResourceKind.Tree; return true;
            case StoneItem: kind = ResourceKind.Stone; return true;
            case OreItem: kind = ResourceKind.Ore; return true;
            case FiberItem: kind = ResourceKind.Grass; return true;
            default: return false;
        }
    }

    // 种类名（指令、提示用）
    public static string KindName(ResourceKind k, bool jp)
    {
        switch (k)
        {
            case ResourceKind.Tree: return jp ? "木" : "树";
            case ResourceKind.Stone: return jp ? "石" : "石头";
            case ResourceKind.Ore: return jp ? "鉱石" : "矿石";
            default: return "草";
        }
    }
}

// 地图布局：生成器需要知道的场景信息（TerrainSystem 从 GridManager / 墙 Tilemap / NPC 生成点读出来）。
[Serializable]
public class TerrainLayout
{
    public int width, height;                 // 小格数
    public int sub = 3;                       // 1 块地砖（大格）= sub×sub 小格
    public int campX, campY, campW, campH;    // 营地地砖矩形（小格，按大格对齐）
    public int spawnX, spawnY;                // NPC 生成点所在的小格
    public bool[] blocked;                    // 墙（不生成任何地形 / 资源）；可为 null

    public bool IsBlocked(int x, int y) => blocked != null && blocked[y * width + x];

    // 主菜单预览的默认布局（还没进过游戏场景、没有缓存时用）：和默认地图一致
    // （GridManager.mapSize = 72×33 块地砖，以 (0,0) 为中心；营地 8×6 块砖）。
    public static TerrainLayout Default()
    {
        return new TerrainLayout { width = 216, height = 99, sub = 3, campX = 96, campY = 39, campW = 24, campH = 18, spawnX = 108, spawnY = 48 };
    }

    // 存进 PlayerPrefs 的文字（主菜单预览用：进过一次游戏后，预览和实际地图一模一样）。
    public string Serialize()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("v1;").Append(width).Append(';').Append(height).Append(';').Append(sub).Append(';')
          .Append(campX).Append(';').Append(campY).Append(';').Append(campW).Append(';').Append(campH).Append(';')
          .Append(spawnX).Append(';').Append(spawnY).Append(';');
        // 墙：游程编码（从"不是墙"开始，交替记连续多少格）
        int n = width * height;
        bool cur = false;
        int run = 0;
        bool first = true;
        for (int i = 0; i < n; i++)
        {
            bool b = blocked != null && blocked[i];
            if (b == cur) { run++; continue; }
            if (!first) sb.Append(',');
            sb.Append(run);
            first = false;
            cur = b;
            run = 1;
        }
        if (!first) sb.Append(',');
        sb.Append(run);
        return sb.ToString();
    }

    public static TerrainLayout Deserialize(string s)
    {
        try
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith("v1;")) return null;
            string[] p = s.Split(';');
            if (p.Length < 11) return null;
            TerrainLayout L = new TerrainLayout
            {
                width = int.Parse(p[1]), height = int.Parse(p[2]), sub = int.Parse(p[3]),
                campX = int.Parse(p[4]), campY = int.Parse(p[5]), campW = int.Parse(p[6]), campH = int.Parse(p[7]),
                spawnX = int.Parse(p[8]), spawnY = int.Parse(p[9])
            };
            if (L.width < 3 || L.height < 3 || L.width * L.height > 4000000) return null;
            int n = L.width * L.height;
            L.blocked = new bool[n];
            string[] runs = p[10].Split(',');
            int idx = 0;
            bool cur = false;
            foreach (string r in runs)
            {
                int c = int.Parse(r);
                for (int k = 0; k < c && idx < n; k++) L.blocked[idx++] = cur;
                cur = !cur;
            }
            return idx == n ? L : null;
        }
        catch { return null; }
    }
}

// 生成结果。
public class TerrainMap
{
    public int width, height, sub, seed;
    public TerrainType[] cells;
    public bool[] blocked;                                         // 墙
    public List<ResourceSpawn> resources = new List<ResourceSpawn>();
    public TerrainFlavor flavor;
    public int spawnX, spawnY;
    public int campX, campY, campW, campH;
    public int lakeCount;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;
    public TerrainType Get(int x, int y) => cells[y * width + x];
    public bool IsBlocked(int x, int y) => blocked != null && blocked[y * width + x];

    public int Count(TerrainType t)
    {
        int c = 0;
        for (int i = 0; i < cells.Length; i++) if (cells[i] == t && (blocked == null || !blocked[i])) c++;
        return c;
    }

    public int OpenCells
    {
        get { int c = 0; for (int i = 0; i < cells.Length; i++) if (blocked == null || !blocked[i]) c++; return c; }
    }

    public int CountResources(ResourceKind k, int minSize = 1)
    {
        int c = 0;
        foreach (ResourceSpawn r in resources) if (r.kind == k && r.size >= minSize) c++;
        return c;
    }
}

// 自带的随机数（SplitMix64）：种子相同 → 序列相同，不依赖 .NET / Unity 的实现。
public sealed class TerrainRng
{
    ulong _s;

    public TerrainRng(int seed)
    {
        unchecked { _s = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL; }
    }

    public ulong NextULong()
    {
        unchecked
        {
            ulong z = (_s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public int Next(int max) => max <= 1 ? 0 : (int)(NextULong() % (ulong)max);
    public int Range(int min, int maxExclusive) => maxExclusive <= min ? min : min + Next(maxExclusive - min);
    public float Value() => (NextULong() >> 40) / 16777216f; // [0,1)
    public float Range(float a, float b) => a + (b - a) * Value();
    public bool Chance(float p) => Value() < p;
}

// 值噪声（确定性）：生成湖的边缘、树林 / 岩区的分布、贴图的颗粒。
public static class TerrainNoise
{
    public static float Hash01(int seed, int x, int y)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u;
            h ^= (uint)x * 0x85EBCA77u; h = (h << 13) | (h >> 19);
            h ^= (uint)y * 0xC2B2AE3Du; h = (h << 17) | (h >> 15);
            h ^= h >> 16; h *= 0x7FEB352Du;
            h ^= h >> 15; h *= 0x846CA68Bu;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    public static float Value(int seed, float x, float y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash01(seed, x0, y0), b = Hash01(seed, x0 + 1, y0);
        float c = Hash01(seed, x0, y0 + 1), d = Hash01(seed, x0 + 1, y0 + 1);
        float ab = a + (b - a) * fx, cd = c + (d - c) * fx;
        return ab + (cd - ab) * fy;
    }

    public static float Fractal(int seed, float x, float y, int octaves)
    {
        float sum = 0f, amp = 1f, norm = 0f, f = 1f;
        for (int o = 0; o < octaves; o++)
        {
            sum += Value(seed + o * 1013, x * f, y * f) * amp;
            norm += amp;
            amp *= 0.5f;
            f *= 2f;
        }
        return norm > 0f ? sum / norm : 0f;
    }

    public static float Smooth(float a, float b, float v)
    {
        if (b <= a) return v >= b ? 1f : 0f;
        float t = (v - a) / (b - a);
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        return t * t * (3f - 2f * t);
    }
}

public static class TerrainGenerator
{
    // 每种地貌的生成参数（密度都是"每多少格土地放一个"的倍率）。
    class Params
    {
        public float waterRatio;    // 湖面积占可用陆地的比例
        public float lakes;         // 湖的个数（测试小地图的个数；大地图按面积平方根放大）
        public float swampMul, treeMul, stoneMul, oreMul, grassMul;
        public float missingEdge = 0.12f; // 营地边上缺砖的概率

        public static Params For(TerrainFlavor f, TerrainRng rng)
        {
            Params p = new Params();
            switch (f)
            {
                case TerrainFlavor.Wetland:
                    p.waterRatio = rng.Range(0.10f, 0.16f); p.lakes = rng.Range(2f, 3.2f);
                    p.swampMul = 1.9f; p.treeMul = 0.9f; p.stoneMul = 0.8f; p.oreMul = 0.8f; p.grassMul = 1.4f;
                    break;
                case TerrainFlavor.Forest:
                    p.waterRatio = rng.Range(0.04f, 0.09f); p.lakes = rng.Range(1f, 2.2f);
                    p.swampMul = 0.8f; p.treeMul = 2.0f; p.stoneMul = 0.7f; p.oreMul = 0.8f; p.grassMul = 1.3f;
                    break;
                case TerrainFlavor.Rocky:
                    p.waterRatio = rng.Range(0.03f, 0.07f); p.lakes = rng.Range(1f, 1.6f);
                    p.swampMul = 0.6f; p.treeMul = 0.6f; p.stoneMul = 2.0f; p.oreMul = 2.0f; p.grassMul = 0.7f;
                    break;
                default:
                    p.waterRatio = rng.Range(0.05f, 0.10f); p.lakes = rng.Range(1f, 2.2f);
                    p.swampMul = 1f; p.treeMul = 1f; p.stoneMul = 1f; p.oreMul = 1f; p.grassMul = 1f;
                    break;
            }
            return p;
        }
    }

    public const int ProtectMargin = 6;   // 营地外圈多少格（6 = 2 块地砖）不生成水 / 沼泽 / 树石矿
    public const int BlockGap = 3;        // 挡路的资源和墙 / 水 / 别的挡路资源之间至少留几格空地
    const float BaseFree = 1300f;         // 参照面积：测试小地图的可用格数（湖的个数按 √(面积/它) 放大）

    public static TerrainMap Generate(TerrainLayout L, int seed)
    {
        int W = Math.Max(1, L.width), H = Math.Max(1, L.height), N = W * H;
        TerrainMap m = new TerrainMap
        {
            width = W, height = H, sub = Math.Max(1, L.sub), seed = seed,
            cells = new TerrainType[N],
            blocked = L.blocked != null && L.blocked.Length == N ? L.blocked : new bool[N],
            spawnX = Clamp(L.spawnX, 0, W - 1), spawnY = Clamp(L.spawnY, 0, H - 1),
            campX = L.campX, campY = L.campY, campW = L.campW, campH = L.campH
        };
        TerrainRng rng = new TerrainRng(seed);
        m.flavor = (TerrainFlavor)rng.Next(4);
        Params p = Params.For(m.flavor, rng);

        // 0. 营地地砖 + 保护区
        bool[] protect = new bool[N];
        PaintCamp(m, rng, p, protect);

        // 1. 湖
        bool[] water = new bool[N];
        PlaceLakes(m, rng, p, protect, water);

        // 2. 沼泽
        bool[] swamp = new bool[N];
        PlaceSwamps(m, rng, p, protect, water, swamp);

        // 3. 写进地图，处理零碎的土地
        for (int i = 0; i < N; i++)
        {
            if (m.blocked[i]) continue;
            if (water[i]) m.cells[i] = TerrainType.Water;
            else if (swamp[i]) m.cells[i] = TerrainType.Swamp;
        }
        FixSoilSlivers(m, protect);

        // 4. 资源
        PlaceResources(m, rng, p, protect);
        return m;
    }

    // ============ 营地 ============

    static void PaintCamp(TerrainMap m, TerrainRng rng, Params p, bool[] protect)
    {
        int W = m.width, H = m.height, s = m.sub;
        if (m.campW <= 0 || m.campH <= 0)
        {
            // 没有营地矩形：只保护生成点周围
            MarkRect(protect, W, H, m.spawnX - 6, m.spawnY - 6, 13, 13);
            return;
        }
        int x0 = m.campX, y0 = m.campY, x1 = m.campX + m.campW, y1 = m.campY + m.campH;
        int scx = FloorDiv(m.spawnX, s), scy = FloorDiv(m.spawnY, s);
        for (int cy = FloorDiv(y0, s); cy * s < y1; cy++)
            for (int cx = FloorDiv(x0, s); cx * s < x1; cx++)
            {
                bool edge = cx * s <= x0 || cy * s <= y0 || (cx + 1) * s >= x1 || (cy + 1) * s >= y1;
                bool corner = (cx * s <= x0 || (cx + 1) * s >= x1) && (cy * s <= y0 || (cy + 1) * s >= y1);
                bool nearSpawn = Math.Abs(cx - scx) <= 1 && Math.Abs(cy - scy) <= 1;
                float chance = corner ? p.missingEdge * 2f : p.missingEdge;
                bool missing = edge && !nearSpawn && rng.Chance(chance);
                if (missing) continue;
                for (int y = cy * s; y < (cy + 1) * s; y++)
                    for (int x = cx * s; x < (cx + 1) * s; x++)
                    {
                        if (x < x0 || y < y0 || x >= x1 || y >= y1 || !m.InBounds(x, y)) continue;
                        if (m.blocked[y * W + x]) continue;
                        m.cells[y * W + x] = TerrainType.CampFloor;
                    }
            }
        MarkRect(protect, W, H, x0 - ProtectMargin, y0 - ProtectMargin, m.campW + ProtectMargin * 2, m.campH + ProtectMargin * 2);
    }

    // ============ 湖 ============

    static void PlaceLakes(TerrainMap m, TerrainRng rng, Params p, bool[] protect, bool[] water)
    {
        int W = m.width, H = m.height, N = W * H;
        int free = 0;
        for (int i = 0; i < N; i++) if (!m.blocked[i] && !protect[i]) free++;
        float target = free * p.waterRatio;
        int lakes = Math.Max(1, (int)Math.Round(p.lakes * Math.Sqrt(Math.Max(1f, free / BaseFree))));
        Reach(m, water, out bool[] passBefore, out bool[] reachBefore);
        int waterCells = 0, placed = 0;

        for (int attempt = 0; attempt < lakes * 40 && placed < lakes; attempt++)
        {
            float remaining = target - waterCells;
            if (remaining < 12f && placed > 0) break;
            // 有大湖也有小水塘：每片的面积在平均值的 0.4~1.8 倍之间
            float area = Math.Max(12f, Math.Min(Math.Max(remaining, 12f), target / lakes * rng.Range(0.4f, 1.8f)));
            if (attempt >= lakes * 25) area = Math.Max(12f, area * 0.5f); // 老是放不下：缩小再试
            float aspect = rng.Range(0.6f, 1.7f);
            float ry = (float)Math.Sqrt(area / (Math.PI * aspect));
            float rx = ry * aspect;
            rx = Math.Max(2f, Math.Min(rx, W / 3f));
            ry = Math.Max(2f, Math.Min(ry, H / 2.5f));
            float cx = rng.Range(0f, W), cy = rng.Range(0f, H);
            int nseed = m.seed ^ (0x51ED27 + attempt * 7919);

            bool[] cand = (bool[])water.Clone();
            int bx0 = Math.Max(0, (int)(cx - rx - 2)), bx1 = Math.Min(W - 1, (int)(cx + rx + 2));
            int by0 = Math.Max(0, (int)(cy - ry - 2)), by1 = Math.Min(H - 1, (int)(cy + ry + 2));
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    int i = y * W + x;
                    if (m.blocked[i] || protect[i]) continue;
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    float n = TerrainNoise.Fractal(nseed, x * 0.33f, y * 0.33f, 2) - 0.5f;
                    if (dx * dx + dy * dy < 1f + n * 0.8f) cand[i] = true;
                }
            Open3x3(cand, W, H, bx0 - 2, by0 - 2, bx1 + 2, by1 + 2);
            int total = CountTrue(cand);
            if (total - waterCells < 9) continue;
            if (!StillConnected(m, cand, reachBefore)) continue;
            Array.Copy(cand, water, N);
            waterCells = total;
            placed++;
        }
        m.lakeCount = placed;
    }

    // ============ 沼泽 ============

    static void PlaceSwamps(TerrainMap m, TerrainRng rng, Params p, bool[] protect, bool[] water, bool[] swamp)
    {
        int W = m.width, H = m.height, N = W * H;
        int free = 0;
        for (int i = 0; i < N; i++) if (!m.blocked[i] && !protect[i] && !water[i]) free++;
        int target = Clamp((int)Math.Round(free / 240f * p.swampMul), 1, 1000);

        // 湖边的格子（给"长在湖边的沼泽"选位置）
        List<int> shore = new List<int>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!water[y * W + x]) continue;
                if ((x > 0 && !water[y * W + x - 1]) || (x < W - 1 && !water[y * W + x + 1]) ||
                    (y > 0 && !water[(y - 1) * W + x]) || (y < H - 1 && !water[(y + 1) * W + x])) shore.Add(y * W + x);
            }

        int placed = 0;
        List<int> cellsList = new List<int>();
        for (int attempt = 0; attempt < target * 40 && placed < target; attempt++)
        {
            float r = rng.Value();
            int d = r < 0.65f ? 3 : r < 0.9f ? 4 : 5;
            int cx, cy;
            if (shore.Count > 0 && rng.Chance(0.4f))
            {
                int si = shore[rng.Next(shore.Count)];
                int sx = si % W, sy = si / W;
                int dir = rng.Next(4);
                int ox = dir == 0 ? 1 : dir == 1 ? -1 : 0, oy = dir == 2 ? 1 : dir == 3 ? -1 : 0;
                cx = sx + ox * (d / 2 + 1);
                cy = sy + oy * (d / 2 + 1);
            }
            else { cx = rng.Next(W); cy = rng.Next(H); }
            int x0 = cx - (d - 1) / 2, y0 = cy - (d - 1) / 2;

            // 形状：3×3 是整块；4、5 切掉四个角，像个圆
            bool ok = true;
            cellsList.Clear();
            for (int dy = 0; dy < d && ok; dy++)
                for (int dx = 0; dx < d && ok; dx++)
                {
                    if (d > 3)
                    {
                        float fx = dx - (d - 1) * 0.5f, fy = dy - (d - 1) * 0.5f;
                        if (fx * fx + fy * fy > d * d * 0.25f + 0.3f) continue;
                    }
                    int x = x0 + dx, y = y0 + dy;
                    if (!m.InBounds(x, y)) { ok = false; break; }
                    int i = y * W + x;
                    if (m.blocked[i] || protect[i] || water[i]) { ok = false; break; }
                    cellsList.Add(i);
                }
            if (!ok || cellsList.Count == 0) continue;
            // 不和已有的沼泽连成一大片（保持"一小块一小块"）：四周 3 格内不能已有沼泽
            bool touches = false;
            for (int y = Math.Max(0, y0 - 3); y < Math.Min(H, y0 + d + 3) && !touches; y++)
                for (int x = Math.Max(0, x0 - 3); x < Math.Min(W, x0 + d + 3); x++)
                    if (swamp[y * W + x]) { touches = true; break; }
            if (touches) continue;
            foreach (int i in cellsList) swamp[i] = true;
            placed++;
        }
    }

    // ============ 零碎的土地 ============

    // 不在任何一个 3×3 "土地（含营地地砖）" 方块里的土地格：并进旁边的沼泽（优先）或水面。
    // 并进水面后再查一次连通性，切断了就改回沼泽。
    static void FixSoilSlivers(TerrainMap m, bool[] protect)
    {
        int W = m.width, H = m.height, N = W * H;
        bool[] waterBefore = new bool[N];
        for (int i = 0; i < N; i++) waterBefore[i] = m.cells[i] == TerrainType.Water && !m.blocked[i];
        Reach(m, waterBefore, out bool[] passBefore, out bool[] reachBefore);
        List<int> toWater = new List<int>();

        for (int iter = 0; iter < 4; iter++)
        {
            bool[] covered = new bool[N];
            for (int y = 0; y + 2 < H; y++)
                for (int x = 0; x + 2 < W; x++)
                {
                    bool all = true;
                    for (int yy = y; yy < y + 3 && all; yy++)
                        for (int xx = x; xx < x + 3; xx++)
                        {
                            int i = yy * W + xx;
                            if (m.blocked[i] || (m.cells[i] != TerrainType.Soil && m.cells[i] != TerrainType.CampFloor)) { all = false; break; }
                        }
                    if (!all) continue;
                    for (int yy = y; yy < y + 3; yy++)
                        for (int xx = x; xx < x + 3; xx++) covered[yy * W + xx] = true;
                }

            bool changed = false;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (m.blocked[i] || protect[i] || m.cells[i] != TerrainType.Soil || covered[i]) continue;
                    bool nearSwamp = false, nearWater = false;
                    for (int yy = y - 1; yy <= y + 1; yy++)
                        for (int xx = x - 1; xx <= x + 1; xx++)
                        {
                            if (!m.InBounds(xx, yy) || m.blocked[yy * W + xx]) continue;
                            TerrainType t = m.cells[yy * W + xx];
                            if (t == TerrainType.Swamp) nearSwamp = true;
                            else if (t == TerrainType.Water) nearWater = true;
                        }
                    if (nearSwamp) { m.cells[i] = TerrainType.Swamp; changed = true; }
                    else if (nearWater) { m.cells[i] = TerrainType.Water; toWater.Add(i); changed = true; }
                }
            if (!changed) break;
        }

        if (toWater.Count == 0) return;
        bool[] waterAfter = new bool[N];
        for (int i = 0; i < N; i++) waterAfter[i] = m.cells[i] == TerrainType.Water && !m.blocked[i];
        if (!StillConnected(m, waterAfter, reachBefore))
            foreach (int i in toWater) m.cells[i] = TerrainType.Swamp;
    }

    // ============ 资源 ============

    static void PlaceResources(TerrainMap m, TerrainRng rng, Params p, bool[] protect)
    {
        int W = m.width, H = m.height, N = W * H;
        int[] occ = new int[N];        // 0 = 空，1 = 草，2 = 小东西，3 = 挡路的
        int area = 0;
        for (int i = 0; i < N; i++) if (!m.blocked[i] && !protect[i] && m.cells[i] == TerrainType.Soil) area++;
        if (area <= 0) return;

        int fs = m.seed ^ 0x0F0E57, rs = m.seed ^ 0x0A5A5A, gs = m.seed ^ 0x06E6E6, os = m.seed ^ 0x03E03E;
        Func<int, int, float> forest = (x, y) => TerrainNoise.Fractal(fs, x * 0.09f, y * 0.09f, 3);
        Func<int, int, float> rock = (x, y) => TerrainNoise.Fractal(rs, x * 0.11f, y * 0.11f, 3);
        Func<int, int, float> meadow = (x, y) => TerrainNoise.Fractal(gs, x * 0.15f, y * 0.15f, 2);
        Func<int, int, float> oreN = (x, y) => TerrainNoise.Fractal(os, x * 0.13f, y * 0.13f, 2);
        Func<int, int, float> treeW = (x, y) => 0.05f + 0.95f * TerrainNoise.Smooth(0.4f, 0.7f, forest(x, y)); // 树成片长，空地上偶尔一棵
        Func<int, int, float> stoneW = (x, y) => 0.1f + 0.9f * TerrainNoise.Smooth(0.42f, 0.75f, rock(x, y));
        Func<int, int, float> oreW = (x, y) => 0.15f + 0.85f * TerrainNoise.Smooth(0.42f, 0.75f, rock(x, y) * 0.6f + oreN(x, y) * 0.4f);
        Func<int, int, float> grassW = (x, y) => 0.06f + 0.94f * TerrainNoise.Smooth(0.35f, 0.7f, meadow(x, y));

        // 先放挡路的大家伙（间距要求最严；越稀有越先放），再放小的，最后铺草
        Scatter(m, rng, occ, protect, ResourceKind.Ore, Count(area, 220f, p.oreMul * rng.Range(0.7f, 1.3f)), oreW, true);
        Scatter(m, rng, occ, protect, ResourceKind.Stone, Count(area, 240f, p.stoneMul * rng.Range(0.7f, 1.3f)), stoneW, true);
        Scatter(m, rng, occ, protect, ResourceKind.Tree, Count(area, 120f, p.treeMul * rng.Range(0.7f, 1.3f)), treeW, true);
        Scatter(m, rng, occ, protect, ResourceKind.Ore, Count(area, 250f, p.oreMul * rng.Range(0.7f, 1.3f)), oreW, false);
        Scatter(m, rng, occ, protect, ResourceKind.Stone, Count(area, 140f, p.stoneMul * rng.Range(0.7f, 1.3f)), stoneW, false);
        Scatter(m, rng, occ, protect, ResourceKind.Tree, Count(area, 85f, p.treeMul * rng.Range(0.7f, 1.3f)), treeW, false);
        AddOreVeins(m, rng, occ, protect); // 大矿石旁边再长几块小矿石（矿脉）

        // 每种至少有一个（随机密度太低时补上，不看噪声）
        foreach (ResourceKind k in new[] { ResourceKind.Tree, ResourceKind.Stone, ResourceKind.Ore })
            if (m.CountResources(k) == 0) Scatter(m, rng, occ, protect, k, 1, (x, y) => 1f, false);

        Scatter(m, rng, occ, protect, ResourceKind.Grass, Count(area, 20f, p.grassMul), grassW, false);
    }

    static int Count(int area, float per, float mul) => Math.Max(0, (int)Math.Round(area / per * mul));

    static void Scatter(TerrainMap m, TerrainRng rng, int[] occ, bool[] protect, ResourceKind kind, int count,
                        Func<int, int, float> weight, bool big)
    {
        if (count <= 0) return;
        int placed = 0;
        int attempts = count * (big ? 60 : 30);
        for (int a = 0; a < attempts && placed < count; a++)
        {
            int size = kind == ResourceKind.Grass || !big ? 1 : (rng.Chance(0.28f) ? 3 : 2);
            int x = rng.Next(m.width), y = rng.Next(m.height);
            int variant = rng.Next(1 << 16);
            if (rng.Value() > weight(x + size / 2, y + size / 2)) continue;
            if (!CanPlace(m, occ, protect, kind, size, x, y)) continue;
            Place(m, occ, kind, size, x, y, variant);
            placed++;
        }
    }

    static void Place(TerrainMap m, int[] occ, ResourceKind kind, int size, int x, int y, int variant)
    {
        int mark = kind == ResourceKind.Grass ? 1 : ResourceInfo.Blocks(kind, size) ? 3 : 2;
        for (int yy = y; yy < y + size; yy++)
            for (int xx = x; xx < x + size; xx++) occ[yy * m.width + xx] = mark;
        m.resources.Add(new ResourceSpawn { kind = kind, size = size, x = x, y = y, variant = variant });
    }

    // 矿脉：每块大矿石旁边 2~3 格内再长 1~2 块小矿石。
    static void AddOreVeins(TerrainMap m, TerrainRng rng, int[] occ, bool[] protect)
    {
        List<ResourceSpawn> big = new List<ResourceSpawn>();
        foreach (ResourceSpawn r in m.resources) if (r.kind == ResourceKind.Ore && r.size >= 2) big.Add(r);
        foreach (ResourceSpawn b in big)
        {
            int want = 1 + rng.Next(2);
            for (int a = 0; a < 12 && want > 0; a++)
            {
                int x = b.x - 3 + rng.Next(b.size + 6), y = b.y - 3 + rng.Next(b.size + 6);
                int variant = rng.Next(1 << 16);
                if (!CanPlace(m, occ, protect, ResourceKind.Ore, 1, x, y)) continue;
                Place(m, occ, ResourceKind.Ore, 1, x, y, variant);
                want--;
            }
        }
    }

    static bool CanPlace(TerrainMap m, int[] occ, bool[] protect, ResourceKind kind, int size, int x, int y)
    {
        int W = m.width;
        for (int yy = y; yy < y + size; yy++)
            for (int xx = x; xx < x + size; xx++)
            {
                if (!m.InBounds(xx, yy)) return false;
                int i = yy * W + xx;
                if (m.blocked[i] || m.cells[i] != TerrainType.Soil || occ[i] != 0) return false;
                if (kind != ResourceKind.Grass && protect[i]) return false;
            }
        if (kind == ResourceKind.Grass) return true;

        if (ResourceInfo.Blocks(kind, size))
        {
            // 挡路的：四周 BlockGap 格内不能有墙、水、地图边、别的挡路资源（NPC 半径约 1 小格，留 3 格才走得过去）
            for (int yy = y - BlockGap; yy < y + size + BlockGap; yy++)
                for (int xx = x - BlockGap; xx < x + size + BlockGap; xx++)
                {
                    if (!m.InBounds(xx, yy)) return false;
                    int i = yy * W + xx;
                    if (m.blocked[i] || m.cells[i] == TerrainType.Water || occ[i] == 3) return false;
                }
            // 小东西也别贴着
            for (int yy = y - 1; yy <= y + size; yy++)
                for (int xx = x - 1; xx <= x + size; xx++)
                    if (m.InBounds(xx, yy) && occ[yy * W + xx] == 2) return false;
            return true;
        }

        // 1 格的小东西：不贴着别的非草资源
        for (int yy = y - 1; yy <= y + 1; yy++)
            for (int xx = x - 1; xx <= x + 1; xx++)
                if (m.InBounds(xx, yy) && occ[yy * W + xx] >= 2) return false;
        return true;
    }

    // ============ 工具 ============

    // 形态学开运算：只保留能被某个完整 3×3 方块盖住的格子（去掉一两格宽的零碎部分）。
    // 只处理 [x0,x1]×[y0,y1] 这一块（新加的湖 + 2 格边距）：区域外的格子本来就满足条件，不会变。
    static void Open3x3(bool[] mask, int W, int H, int x0, int y0, int x1, int y1)
    {
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(W - 1, x1); y1 = Math.Min(H - 1, y1);
        if (x1 < x0 || y1 < y0) return;
        int rw = x1 - x0 + 1;
        bool[] keep = new bool[rw * (y1 - y0 + 1)];
        for (int wy = Math.Max(0, y0 - 2); wy <= Math.Min(H - 3, y1); wy++)
            for (int wx = Math.Max(0, x0 - 2); wx <= Math.Min(W - 3, x1); wx++)
            {
                bool all = true;
                for (int yy = wy; yy < wy + 3 && all; yy++)
                    for (int xx = wx; xx < wx + 3; xx++)
                        if (!mask[yy * W + xx]) { all = false; break; }
                if (!all) continue;
                for (int yy = wy; yy < wy + 3; yy++)
                    for (int xx = wx; xx < wx + 3; xx++)
                        if (xx >= x0 && xx <= x1 && yy >= y0 && yy <= y1) keep[(yy - y0) * rw + (xx - x0)] = true;
            }
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) mask[y * W + x] = keep[(y - y0) * rw + (x - x0)];
    }

    // 从生成点出发能走到哪些格。pass = 能站的格：墙、水、地图边外扩 1 格算挡住（近似 NPC 的身体半径）。
    static void Reach(TerrainMap m, bool[] water, out bool[] pass, out bool[] reached)
    {
        int W = m.width, H = m.height, N = W * H;
        // 外扩 1 格（3×3 范围内有障碍就站不了）：先横向再纵向，比逐格查 9 个邻居快
        bool[] rowHit = new bool[N];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                rowHit[i] = x == 0 || x == W - 1 || m.blocked[i] || water[i]
                            || m.blocked[i - 1] || water[i - 1] || m.blocked[i + 1] || water[i + 1];
            }
        pass = new bool[N];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                pass[i] = !(y == 0 || y == H - 1 || rowHit[i] || rowHit[i - W] || rowHit[i + W]);
            }

        reached = new bool[N];
        int start = NearestPassable(pass, W, H, m.spawnX, m.spawnY);
        if (start < 0) return;
        Queue<int> q = new Queue<int>();
        q.Enqueue(start);
        reached[start] = true;
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int x = i % W, y = i / W;
            if (x > 0) Visit(i - 1, pass, reached, q);
            if (x < W - 1) Visit(i + 1, pass, reached, q);
            if (y > 0) Visit(i - W, pass, reached, q);
            if (y < H - 1) Visit(i + W, pass, reached, q);
        }
    }

    static void Visit(int j, bool[] pass, bool[] reached, Queue<int> q)
    {
        if (!pass[j] || reached[j]) return;
        reached[j] = true;
        q.Enqueue(j);
    }

    static int NearestPassable(bool[] pass, int W, int H, int sx, int sy)
    {
        if (sx >= 0 && sy >= 0 && sx < W && sy < H && pass[sy * W + sx]) return sy * W + sx;
        int best = -1, bestD = int.MaxValue;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!pass[y * W + x]) continue;
                int d = (x - sx) * (x - sx) + (y - sy) * (y - sy);
                if (d < bestD) { bestD = d; best = y * W + x; }
            }
        return best;
    }

    // 加了水以后：原来走得到、现在也还能站的格子，是不是都还走得到。
    static bool StillConnected(TerrainMap m, bool[] water, bool[] reachBefore)
    {
        Reach(m, water, out bool[] pass, out bool[] after);
        for (int i = 0; i < after.Length; i++)
            if (reachBefore[i] && !after[i] && pass[i]) return false;
        return true;
    }

    static void MarkRect(bool[] mask, int W, int H, int x0, int y0, int w, int h)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(H, y0 + h); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(W, x0 + w); x++) mask[y * W + x] = true;
    }

    static int CountTrue(bool[] a) { int c = 0; foreach (bool b in a) if (b) c++; return c; }
    static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
    static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}

// ============================================================================
// 程序画的占位美术（纯像素，不依赖 Unity；TerrainArt 把它变成贴图 / Sprite）。
// 以后有了美术：地面在 TerrainSystem 的 Inspector 里填 Tile；资源换 ResourceNode 的 Sprite 即可。
// 像素数组：RGBA，左下角为原点、一行一行往上（和 Texture2D 一致）。
// ============================================================================
public static class TerrainPainter
{
    struct Col
    {
        public float r, g, b;
        public Col(float r, float g, float b) { this.r = r; this.g = g; this.b = b; }
        public Col Add(float d) => new Col(r + d, g + d, b + d);
        public static Col Lerp(Col a, Col c, float t) => new Col(a.r + (c.r - a.r) * t, a.g + (c.g - a.g) * t, a.b + (c.b - a.b) * t);
    }

    static readonly Col SoilBase = new Col(84, 70, 58);
    static readonly Col SoilDark = new Col(66, 54, 46);
    static readonly Col SoilPebble = new Col(118, 104, 88);
    static readonly Col SwampBase = new Col(50, 72, 54);
    static readonly Col SwampDark = new Col(32, 50, 42);
    static readonly Col SwampAlgae = new Col(88, 112, 62);
    static readonly Col SwampGlint = new Col(104, 132, 128);
    static readonly Col SwampReed = new Col(116, 126, 72);
    static readonly Col WaterDeep = new Col(30, 60, 92);
    static readonly Col WaterShallow = new Col(52, 100, 128);
    static readonly Col WaterFoam = new Col(132, 176, 192);
    static readonly Col CampGrey = new Col(196, 196, 196);

    static bool Round(TerrainType t) => t == TerrainType.Swamp || t == TerrainType.Water;

    static TerrainType At(TerrainMap m, int x, int y, TerrainType fallback)
    {
        if (!m.InBounds(x, y)) return fallback;
        if (m.IsBlocked(x, y)) return fallback;
        return m.Get(x, y);
    }

    // 地面：每个小格 k×k 像素。营地地砖的位置画成土地（上面会盖地砖 Tilemap）。
    public static byte[] PaintGround(TerrainMap m, int k)
    {
        int W = m.width * k, H = m.height * k;
        byte[] px = new byte[W * H * 4];
        int seed = m.seed;
        // 每个水格到岸边的距离（格），用来画深浅
        int[] dist = WaterDistance(m);
        float half = k * 0.5f;

        byte[] kind = new byte[W * H]; // 每个像素最后画成了哪种地形（第二遍加湿痕用）
        for (int cy = 0; cy < m.height; cy++)
            for (int cx = 0; cx < m.width; cx++)
            {
                TerrainType self = m.Get(cx, cy);
                if (m.IsBlocked(cx, cy)) self = TerrainType.Soil;
                if (self == TerrainType.CampFloor) self = TerrainType.Soil;
                for (int py = 0; py < k; py++)
                    for (int pxx = 0; pxx < k; pxx++)
                    {
                        TerrainType draw = self;
                        // 外凸的角（水 / 沼泽，这一角的两边都不是自己）：以对角为圆心、半径 1 格切成四分之一圆 → 3×3 的沼泽看起来是圆的
                        if (Round(self))
                            for (int c4 = 0; c4 < 4 && draw == self; c4++)
                            {
                                int qx = (c4 & 1) == 0 ? -1 : 1, qy = (c4 & 2) == 0 ? -1 : 1;
                                TerrainType nx = Norm(At(m, cx + qx, cy, self)), ny = Norm(At(m, cx, cy + qy, self));
                                if (nx == self || ny == self) continue;
                                float ox = qx < 0 ? k : 0f, oy = qy < 0 ? k : 0f;
                                float dx = pxx + 0.5f - ox, dy = py + 0.5f - oy;
                                if (dx * dx + dy * dy > k * k) draw = nx == ny ? nx : TerrainType.Soil;
                            }
                        // 内凹的角（两边和斜对面都是水 / 沼泽）：填一个小圆角
                        if (draw == self)
                        {
                            int qx = pxx < half ? -1 : 1, qy = py < half ? -1 : 1;
                            TerrainType nx = Norm(At(m, cx + qx, cy, self)), ny = Norm(At(m, cx, cy + qy, self)), nd = Norm(At(m, cx + qx, cy + qy, self));
                            float ddx = pxx + 0.5f - half, ddy = py + 0.5f - half;
                            if (nx == ny && nx == nd && nx != self && Round(nx) && ddx * qx > 0f && ddy * qy > 0f && ddx * ddx + ddy * ddy > half * half)
                                draw = nx;
                        }
                        int gx = cx * k + pxx, gy = cy * k + py;
                        Col c = Shade(m, draw, gx, gy, cx, cy, k, seed, dist);
                        int o = (gy * W + gx) * 4;
                        px[o] = B(c.r); px[o + 1] = B(c.g); px[o + 2] = B(c.b); px[o + 3] = 255;
                        kind[gy * W + gx] = (byte)draw;
                    }
            }

        // 第二遍：水边一圈浅色泡沫；水 / 沼泽旁边的土地一圈湿痕（深一点）
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                TerrainType t = (TerrainType)kind[y * W + x];
                bool nearWater = false, nearSwamp = false, nearOther = false;
                for (int d = 0; d < 4; d++)
                {
                    int xx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), yy = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                    TerrainType n = (TerrainType)kind[yy * W + xx];
                    if (n == TerrainType.Water) nearWater = true;
                    else if (n == TerrainType.Swamp) nearSwamp = true;
                    if (n != t) nearOther = true;
                }
                int o = (y * W + x) * 4;
                if (t == TerrainType.Water && nearOther)
                {
                    Col c = Col.Lerp(new Col(px[o], px[o + 1], px[o + 2]), WaterFoam, 0.45f);
                    px[o] = B(c.r); px[o + 1] = B(c.g); px[o + 2] = B(c.b);
                }
                else if (t == TerrainType.Soil && (nearWater || nearSwamp))
                {
                    px[o] = B(px[o] * 0.78f); px[o + 1] = B(px[o + 1] * 0.8f); px[o + 2] = B(px[o + 2] * 0.8f);
                }
            }
        return px;
    }

    static TerrainType Norm(TerrainType t) => t == TerrainType.CampFloor ? TerrainType.Soil : t;

    static Col Shade(TerrainMap m, TerrainType t, int gx, int gy, int cx, int cy, int k, int seed, int[] dist)
    {
        float fine = TerrainNoise.Hash01(seed ^ 0x1111, gx, gy) - 0.5f;
        float low = TerrainNoise.Value(seed ^ 0x2222, gx / (k * 2.5f), gy / (k * 2.5f)) - 0.5f;
        switch (t)
        {
            case TerrainType.Swamp:
            {
                float murk = TerrainNoise.Value(seed ^ 0x3333, gx / (k * 0.9f), gy / (k * 0.9f));
                Col c = Col.Lerp(SwampBase, SwampDark, TerrainNoise.Smooth(0.4f, 0.75f, murk));
                if (TerrainNoise.Hash01(seed ^ 0x4444, gx, gy) > 0.92f) c = SwampAlgae;
                if (TerrainNoise.Hash01(seed ^ 0x5555, gx / 2, gy / 2) > 0.975f) c = SwampGlint;          // 积水反光
                if (TerrainNoise.Hash01(seed ^ 0x5A5A, gx, gy / 3) > 0.985f) c = SwampReed;              // 芦苇
                return c.Add(fine * 6f + low * 8f);
            }
            case TerrainType.Water:
            {
                int d = dist != null ? dist[cy * m.width + cx] : 3;
                float depth = Math.Min(1f, d / 3f);
                Col c = Col.Lerp(WaterShallow, WaterDeep, depth);
                // 波纹：短横线
                if (TerrainNoise.Hash01(seed ^ 0x6666, gx / 3, gy) > 0.965f) c = Col.Lerp(c, WaterFoam, 0.35f);
                return c.Add(low * 8f + fine * 3f);
            }
            case TerrainType.CampFloor:
                return CampGrey.Add(fine * 6f);
            default:
            {
                Col c = Col.Lerp(SoilBase, SoilDark, TerrainNoise.Smooth(0.4f, 0.8f, TerrainNoise.Value(seed ^ 0x7777, gx / (k * 1.6f), gy / (k * 1.6f))));
                if (TerrainNoise.Hash01(seed ^ 0x8888, gx, gy) > 0.975f) c = SoilPebble;
                return c.Add(fine * 10f + low * 12f);
            }
        }
    }

    static int[] WaterDistance(TerrainMap m)
    {
        int W = m.width, H = m.height, N = W * H;
        int[] d = new int[N];
        Queue<int> q = new Queue<int>();
        for (int i = 0; i < N; i++)
        {
            if (m.cells[i] == TerrainType.Water && !m.blocked[i]) d[i] = int.MaxValue;
            else { d[i] = 0; q.Enqueue(i); }
        }
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int x = i % W, y = i / W;
            int[] nb = { x > 0 ? i - 1 : -1, x < W - 1 ? i + 1 : -1, y > 0 ? i - W : -1, y < H - 1 ? i + W : -1 };
            foreach (int j in nb)
            {
                if (j < 0 || d[j] <= d[i] + 1) continue;
                d[j] = d[i] + 1;
                q.Enqueue(j);
            }
        }
        return d;
    }

    // 小地图预览（主菜单）：1 个小格 = 1 像素。
    public static byte[] PaintPreview(TerrainMap m)
    {
        int W = m.width, H = m.height;
        byte[] px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Col c;
                if (m.IsBlocked(x, y)) c = new Col(26, 27, 34);
                else
                {
                    switch (m.Get(x, y))
                    {
                        case TerrainType.CampFloor: c = new Col(190, 192, 196); break;
                        case TerrainType.Swamp: c = new Col(66, 84, 52); break;
                        case TerrainType.Water: c = new Col(48, 98, 150); break;
                        default: c = new Col(98, 80, 62); break;
                    }
                }
                Put(px, W, x, y, c);
            }
        foreach (ResourceSpawn r in m.resources)
        {
            Col c;
            switch (r.kind)
            {
                case ResourceKind.Tree: c = r.size >= 2 ? new Col(36, 104, 50) : new Col(70, 132, 70); break;
                case ResourceKind.Stone: c = new Col(150, 150, 154); break;
                case ResourceKind.Ore: c = new Col(96, 214, 226); break;
                default: c = new Col(112, 124, 70); break;
            }
            for (int yy = r.y; yy < r.y + r.size; yy++)
                for (int xx = r.x; xx < r.x + r.size; xx++)
                    if (m.InBounds(xx, yy)) Put(px, W, xx, yy, c);
        }
        if (m.InBounds(m.spawnX, m.spawnY)) Put(px, W, m.spawnX, m.spawnY, new Col(255, 210, 90));
        return px;
    }

    // ============ 资源的像素画（32 像素 = 1 世界单位，和 NPC、地砖一样）============
    // NPC 大约 0.9 单位高。成年树约 2.1 单位高（两个多人高），巨型树约 3.3 单位，树苗到人胸口。
    // pivotX / pivotY：图上"站在地上的那一点"（树干根部 / 石头底部中心），放在占地的中心，NPC 前后遮挡也按这一点排。

    public const int ResourcePPU = 32;

    public static byte[] PaintResource(ResourceKind kind, int size, int variant, out int w, out int h, out int pivotX, out int pivotY)
    {
        size = Math.Max(1, Math.Min(3, size));
        TerrainRng r = new TerrainRng(variant * 7919 + (int)kind * 131 + size * 17 + 1);
        switch (kind)
        {
            case ResourceKind.Tree:
                if (size == 1) { w = 16; h = 24; } else if (size == 2) { w = 48; h = 68; } else { w = 72; h = 104; }
                break;
            case ResourceKind.Stone:
                if (size == 1) { w = 14; h = 13; } else if (size == 2) { w = 28; h = 25; } else { w = 42; h = 37; }
                break;
            case ResourceKind.Ore:
                if (size == 1) { w = 14; h = 17; } else if (size == 2) { w = 28; h = 31; } else { w = 42; h = 46; }
                break;
            default: w = 12; h = 11; break;
        }
        pivotX = w / 2;
        pivotY = kind == ResourceKind.Grass ? 1 : Math.Max(2, (int)Math.Round(w * 0.1f)) + 1;
        byte[] px = new byte[w * h * 4];
        switch (kind)
        {
            case ResourceKind.Tree: PaintTree(px, w, h, pivotX, pivotY, size, r); break;
            case ResourceKind.Stone: PaintRock(px, w, h, pivotX, pivotY, size, r, false); break;
            case ResourceKind.Ore: PaintRock(px, w, h, pivotX, pivotY, size, r, true); break;
            default: PaintGrass(px, w, h, r); break;
        }
        return px;
    }

    static void PaintGrass(byte[] px, int w, int h, TerrainRng r)
    {
        Col dark = new Col(58, 96, 52), mid = new Col(86, 130, 64), light = new Col(126, 164, 80), tip = new Col(172, 170, 98);
        for (int x = 3; x < w - 3; x++) Put(px, w, h, x, 0, new Col(0, 0, 0), 50); // 一点点影子
        int blades = 5 + r.Next(3);
        for (int b = 0; b < blades; b++)
        {
            int x = 1 + r.Next(w - 2);
            int len = 4 + r.Next(Math.Max(1, h - 4));
            float lean = r.Range(-0.35f, 0.35f);
            Col c = b % 3 == 0 ? dark : b % 3 == 1 ? mid : light;
            for (int y = 1; y <= len && y < h; y++)
            {
                int xx = x + (int)Math.Round(lean * y);
                Put(px, w, h, xx, y, y >= len - 1 && r.Chance(0.4f) ? tip : c, 255);
            }
        }
    }

    // 树：影子 → 树干（下粗上细、根部外撇）→ 树冠（一团团叠起来，后面的先画；每团左上亮右下暗）→ 外描边。
    static void PaintTree(byte[] px, int w, int h, int pvx, int pvy, int size, TerrainRng r)
    {
        float hue = r.Range(-10f, 10f);
        Col outline = new Col(18, 40 + hue * 0.3f, 36), dark = new Col(30, 70 + hue * 0.5f, 58), mid = new Col(48 + hue * 0.3f, 108 + hue, 80);
        Col light = new Col(86 + hue * 0.5f, 152 + hue, 104), high = new Col(140, 198 + hue * 0.5f, 132);
        Col barkO = new Col(36, 26, 20), barkD = new Col(56, 40, 30), barkM = new Col(88, 62, 44), barkL = new Col(120, 88, 60);

        // 1. 影子
        Shadow(px, w, h, pvx, pvy, w * 0.36f, Math.Max(2f, w * 0.09f));

        // 2. 树干
        float tw = Math.Max(2f, (float)Math.Round(w * 0.15f));
        int trunkTop = pvy + (int)Math.Round(h * (size == 1 ? 0.4f : 0.34f));
        for (int y = pvy - 1; y <= trunkTop && y < h; y++)
        {
            float t = (y - pvy) / (float)Math.Max(1, trunkTop - pvy);
            float half = tw * (1f - 0.3f * Math.Max(0f, t)) * 0.5f;
            if (size > 1 && y <= pvy + 1) half += 1.6f - (y - pvy + 1) * 0.6f; // 根部往外撇
            int x0 = (int)Math.Round(pvx - half), x1 = (int)Math.Round(pvx + half) - 1;
            for (int x = x0; x <= x1; x++)
            {
                Col c = x == x0 || x == x1 ? barkO : x < pvx - half * 0.3f ? barkL : x > pvx + half * 0.3f ? barkD : barkM;
                if (x != x0 && x != x1 && r.Chance(0.08f)) c = barkD; // 树皮纹理
                Put(px, w, h, x, y, c, 255);
            }
        }

        // 3. 树冠：一个椭圆范围里摆一圈团子 + 中间一团（底下也有一排，把树干上半截盖住）
        float canopyBottom = pvy + h * (size == 1 ? 0.32f : 0.24f);
        float ax = w * 0.44f, ay = (h - 1 - canopyBottom) * 0.5f;
        float ccx = pvx + r.Range(-1f, 1f), ccy = canopyBottom + ay;
        float rmin = Math.Min(ax, ay);
        List<float[]> blobs = new List<float[]>(); // x, y, 半径
        int n = size == 1 ? 4 : size == 2 ? 7 + r.Next(2) : 9 + r.Next(3);
        float a0 = r.Range(0f, 6.28f);
        for (int i = 0; i < n; i++)
        {
            float ang = a0 + 6.2832f * i / n + r.Range(-0.2f, 0.2f);
            float br = rmin * r.Range(0.45f, 0.58f);
            blobs.Add(new[] { ccx + (float)Math.Cos(ang) * (ax - br) * r.Range(0.85f, 1f), ccy + (float)Math.Sin(ang) * (ay - br) * r.Range(0.85f, 1f), br });
        }
        blobs.Add(new[] { ccx, ccy, rmin * 0.72f }); // 中间
        float R = rmin;
        blobs.Sort((a, b) => b[1].CompareTo(a[1])); // 上面的（后排）先画
        bool[] canopy = new bool[w * h];
        int ns = r.Next(1 << 20);
        foreach (float[] b in blobs)
        {
            int bx0 = Math.Max(0, (int)(b[0] - b[2] - 1)), bx1 = Math.Min(w - 1, (int)(b[0] + b[2] + 1));
            int by0 = Math.Max(0, (int)(b[1] - b[2] - 1)), by1 = Math.Min(h - 1, (int)(b[1] + b[2] + 1));
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    float dx = (x + 0.5f - b[0]) / b[2], dy = (y + 0.5f - b[1]) / b[2];
                    float d2 = dx * dx + dy * dy;
                    if (d2 > 1f) continue;
                    float ln = -dx * 0.55f + dy * 0.85f;                 // 左上亮
                    Col c = ln > 0.55f ? high : ln > 0.15f ? light : ln > -0.35f ? mid : dark;
                    if (d2 > 0.8f && ln < 0.1f) c = dark;                 // 每团右下边缘一道暗线：一团团分得开
                    float nz = TerrainNoise.Hash01(ns, x, y);
                    if (nz > 0.93f) c = c.Add(-14f); else if (nz < 0.04f) c = c.Add(12f); // 叶子颗粒
                    Put(px, w, h, x, y, c, 255);
                    canopy[y * w + x] = true;
                }
        }

        // 4. 树冠描边
        Outline(px, w, h, canopy, outline);

        // 5. 巨型树：几点亚空间的微光
        if (size == 3)
            for (int i = 0; i < 6; i++)
            {
                int x = (int)(ccx + r.Range(-R * 0.8f, R * 0.8f)), y = (int)(ccy + r.Range(-R * 0.6f, R * 0.8f));
                if (x >= 0 && y >= 0 && x < w && y < h && canopy[y * w + x]) Put(px, w, h, x, y, new Col(150, 240, 230), 255);
            }
    }

    // 石头 / 矿石：影子 → 2~3 个椭圆拼成的石块（左上亮）→ 裂缝 / 青苔 → 描边 → 矿石再长出发光的结晶。
    static void PaintRock(byte[] px, int w, int h, int pvx, int pvy, int size, TerrainRng r, bool ore)
    {
        float tint = r.Range(-6f, 6f);
        Col o, d, m, l, hi;
        if (ore) { o = new Col(34, 30, 44); d = new Col(56 + tint, 50, 70); m = new Col(80 + tint, 74, 98); l = new Col(108 + tint, 102, 128); hi = new Col(140, 132, 160); }
        else { o = new Col(54, 52, 54); d = new Col(90 + tint, 88 + tint, 88); m = new Col(128 + tint, 126 + tint, 122); l = new Col(162 + tint, 160 + tint, 154); hi = new Col(196, 194, 186); }

        Shadow(px, w, h, pvx, pvy, w * 0.42f, Math.Max(2f, w * 0.1f));

        float rx = w * 0.44f, ry = ore ? h * 0.30f : h * 0.34f;
        float cy = pvy - 1 + ry; // 底边贴着地面
        List<float[]> lumps = new List<float[]> { new[] { pvx + r.Range(-1f, 1f), cy, rx, ry } };
        int extra = size == 1 ? 0 : 1 + r.Next(2);
        for (int i = 0; i < extra; i++)
            lumps.Add(new[] { pvx + r.Range(-rx * 0.45f, rx * 0.45f), cy + ry * r.Range(0.2f, 0.35f), rx * r.Range(0.5f, 0.7f), ry * r.Range(0.6f, 0.8f) });
        bool[] body = new bool[w * h];
        foreach (float[] b in lumps)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f - b[0]) / b[2], dy = (y + 0.5f - b[1]) / b[3];
                    float d2 = dx * dx + dy * dy;
                    if (d2 > 1f) continue;
                    float ln = -dx * 0.45f + dy * 0.9f;
                    Col c = ln > 0.6f ? hi : ln > 0.2f ? l : ln > -0.3f ? m : d;
                    Put(px, w, h, x, y, c, 255);
                    body[y * w + x] = true;
                }

        // 裂缝
        if (size >= 2)
            for (int c = 0; c < size; c++)
            {
                int x = (int)(pvx + r.Range(-rx * 0.5f, rx * 0.5f)), y = (int)(cy + r.Range(-ry * 0.3f, ry * 0.5f));
                int len = 3 + r.Next(3 + size);
                for (int s = 0; s < len; s++)
                {
                    if (x >= 0 && y >= 0 && x < w && y < h && body[y * w + x]) Put(px, w, h, x, y, d.Add(-18f), 255);
                    x += r.Next(3) - 1;
                    y -= 1;
                }
            }
        // 青苔（部分石头）
        if (!ore && r.Chance(0.4f))
            for (int i = 0; i < 4 + size * 4; i++)
            {
                int x = (int)(pvx + r.Range(-rx * 0.7f, rx * 0.7f)), y = (int)(cy + r.Range(ry * 0.1f, ry * 0.9f));
                if (x >= 0 && y >= 0 && x < w && y < h && body[y * w + x]) Put(px, w, h, x, y, new Col(96, 128, 70), 255);
            }

        Outline(px, w, h, body, o);
        if (ore) Crystals(px, w, h, size, r, pvx, cy, rx, ry);
    }

    // 矿石上的结晶：几根往上长的尖晶（青色 / 紫色），左亮右暗，周围一圈淡淡的光。
    static void Crystals(byte[] px, int w, int h, int size, TerrainRng r, float cx, float cy, float rx, float ry)
    {
        int n = size == 1 ? 2 : size == 2 ? 4 : 6 + r.Next(2);
        bool cyan = r.Chance(0.6f);
        for (int i = 0; i < n; i++)
        {
            bool c2 = r.Chance(0.25f) ? !cyan : cyan; // 偶尔混一根别的颜色
            Col cl = c2 ? new Col(196, 250, 255) : new Col(230, 204, 255);
            Col cm = c2 ? new Col(92, 222, 236) : new Col(172, 124, 250);
            Col cd = c2 ? new Col(36, 136, 168) : new Col(98, 62, 168);
            int bx = (int)(cx + r.Range(-rx * 0.6f, rx * 0.6f));
            int by = (int)(cy + r.Range(-ry * 0.1f, ry * 0.5f)); // 从石头上半部长出来
            int hs = size == 1 ? 4 + r.Next(3) : size == 2 ? 6 + r.Next(6) : 9 + r.Next(8);
            int ws = size == 1 ? 1 : 2 + r.Next(size == 3 ? 3 : 2);
            int lean = r.Next(3) - 1;
            Glow(px, w, h, bx, by + hs * 0.5f, hs * 0.55f, cm, 45);
            for (int y = 0; y < hs; y++)
            {
                float t = y / (float)hs;
                int half = Math.Max(0, (int)Math.Round(ws * (1f - t))); // 越往上越尖
                int sx = bx + (int)Math.Round(lean * t * 2f);
                for (int x = sx - half; x <= sx + half; x++)
                {
                    Col c = x < sx ? cl : x > sx ? cd : cm;
                    if (y == hs - 1) c = cl; // 尖端亮
                    Put(px, w, h, x, by + y, c, 255);
                }
            }
        }
    }

    // 半透明的光晕：只画在还透明的像素上。
    static void Glow(byte[] px, int w, int h, float cx, float cy, float rad, Col c, int alpha)
    {
        for (int y = (int)(cy - rad - 1); y <= (int)(cy + rad + 1); y++)
            for (int x = (int)(cx - rad - 1); x <= (int)(cx + rad + 1); x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy > rad * rad) continue;
                int o = (y * w + x) * 4;
                if (px[o + 3] != 0) continue;
                px[o] = B(c.r); px[o + 1] = B(c.g); px[o + 2] = B(c.b); px[o + 3] = (byte)alpha;
            }
    }

    // 地上的影子（半透明黑，中间深一点）。
    static void Shadow(byte[] px, int w, int h, float cx, float cy, float rx, float ry)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                float d2 = dx * dx + dy * dy;
                if (d2 > 1f) continue;
                int a = d2 < 0.55f ? 95 : 55;
                int o = (y * w + x) * 4;
                if (px[o + 3] >= a) continue;
                px[o] = 0; px[o + 1] = 0; px[o + 2] = 0; px[o + 3] = (byte)a;
            }
    }

    // 给 mask 的外沿描一圈边。
    static void Outline(byte[] px, int w, int h, bool[] mask, Col c)
    {
        List<int> edge = new List<int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!mask[i]) continue;
                if (x == 0 || x == w - 1 || y == 0 || y == h - 1 || !mask[i - 1] || !mask[i + 1] || !mask[i - w] || !mask[i + w]) edge.Add(i);
            }
        foreach (int i in edge) Put(px, w, h, i % w, i / w, c, 255);
    }

    static void Put(byte[] px, int W, int x, int y, Col c)
    {
        int o = (y * W + x) * 4;
        px[o] = B(c.r); px[o + 1] = B(c.g); px[o + 2] = B(c.b); px[o + 3] = 255;
    }

    static void Put(byte[] px, int W, int H, int x, int y, Col c, int a)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        int o = (y * W + x) * 4;
        px[o] = B(c.r); px[o + 1] = B(c.g); px[o + 2] = B(c.b); px[o + 3] = (byte)a;
    }

    static byte B(float v) => (byte)(v < 0f ? 0 : v > 255f ? 255 : (int)v);
}
