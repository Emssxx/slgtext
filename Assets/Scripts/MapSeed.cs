using UnityEngine;

// ============================================================================
// 地图种子：一串文字（字母、数字、中文、日文都行）→ 一个整数 → 决定地形怎么生成（TerrainGenerator）。
// 同一个种子永远是同一张地图（地形、湖、沼泽、树、石头、矿的位置都一样），玩家可以记下来 / 分享给别人。
//   · 主菜单：输入种子 / 点「随机」/ 点推荐种子，右边有小地图预览；点开始游戏时 MapSeed.Set() 存进 PlayerPrefs。
//   · 游戏场景开局：TerrainSystem 读 MapSeed.Current 生成地图，并在事件栏写一行「地图种子：xxx」。
//   · 直接从编辑器启动游戏场景（不经过主菜单）：用上一次的种子；从没设过就随机一个。
//     想固定测试某个种子：场景里 TerrainSystem 的 overrideSeed 填上即可。
// 大小写不敏感（"vortex" = "VORTEX"）；纯数字的种子直接当整数用（"12345" 就是 12345）。
// ============================================================================
public static class MapSeed
{
    const string PrefKey = "map_seed";
    const string LayoutKey = "map_layout_v2"; // v2：地图变大后旧的缓存作废

    // 主菜单的推荐种子（随便挑了几个，想换就改这里）。
    public static readonly string[] Presets = { "ABA-0001", "VORTEX", "深渊湖畔", "雾沼", "静寂林" };

    static string _current;

    // 这一局用的种子。
    public static string Current
    {
        get
        {
            if (string.IsNullOrEmpty(_current))
            {
                _current = Normalize(PlayerPrefs.GetString(PrefKey, ""));
                if (string.IsNullOrEmpty(_current))
                {
                    _current = NewRandom();
                    PlayerPrefs.SetString(PrefKey, _current);
                }
            }
            return _current;
        }
    }

    // 上一次开局用的种子（没有返回空串）。
    public static string Last => Normalize(PlayerPrefs.GetString(PrefKey, ""));

    // 设定这一局的种子（主菜单点开始时调用）。空 = 随机一个。
    public static void Set(string seed)
    {
        seed = Normalize(seed);
        if (string.IsNullOrEmpty(seed)) seed = NewRandom();
        _current = seed;
        PlayerPrefs.SetString(PrefKey, seed);
        PlayerPrefs.Save();
    }

    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        return s.Length > 32 ? s.Substring(0, 32) : s;
    }

    // 随机一个好记的种子，如 "K7Q2-M9XA"（去掉了容易看错的 0/O/1/I）。
    public static string NewRandom()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        System.Random r = new System.Random(System.Guid.NewGuid().GetHashCode());
        char[] c = new char[9];
        for (int i = 0; i < c.Length; i++) c[i] = i == 4 ? '-' : chars[r.Next(chars.Length)];
        return new string(c);
    }

    // 种子文字 → 整数（FNV-1a，大小写不敏感）。纯数字直接用数字。
    public static int Hash(string seed)
    {
        seed = Normalize(seed);
        if (int.TryParse(seed, out int n)) return n;
        unchecked
        {
            uint h = 2166136261;
            foreach (char ch in seed.ToUpperInvariant()) { h ^= ch; h *= 16777619; }
            return (int)h;
        }
    }

    // ============ 地图布局缓存（主菜单预览用）============
    // 游戏场景生成地形时把布局（地图大小、墙、营地位置）存下来；主菜单预览就用它，预览和实际地图一模一样。
    // 还没进过游戏场景时用 TerrainLayout.Default()（没有墙）。

    public static void SaveLayout(TerrainLayout layout)
    {
        if (layout == null) return;
        PlayerPrefs.SetString(LayoutKey, layout.Serialize());
        PlayerPrefs.Save();
    }

    public static TerrainLayout LoadLayout()
    {
        TerrainLayout L = TerrainLayout.Deserialize(PlayerPrefs.GetString(LayoutKey, ""));
        return L ?? TerrainLayout.Default();
    }

    // 预览 / 测试：按种子生成一张地图（不放进场景）。
    public static TerrainMap Preview(string seed)
    {
        return TerrainGenerator.Generate(LoadLayout(), Hash(seed));
    }
}
