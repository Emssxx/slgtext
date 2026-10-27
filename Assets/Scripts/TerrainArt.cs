using System.Collections.Generic;
using UnityEngine;

// 把 TerrainPainter 画的像素变成 Unity 的贴图 / Sprite。
//   · 地面：整张地图一张贴图（每小格 8 像素）。
//   · 资源（草 / 树 / 石头 / 矿石）：所有图拼进一张图集（32 像素 = 1 单位，和 NPC、地砖一样），
//     上千个资源共用一张贴图，渲染时能合批，不会卡。
// 都是占位美术：以后有了正式的图，地面在 TerrainSystem 的 Inspector 里填 Tile，资源换 ResourceNode 的 Sprite 即可。
public static class TerrainArt
{
    public const int PixelsPerCell = 8; // 地面：每个小格画几个像素（1 小格 = 1/3 世界单位 → 24 像素 / 单位）

    public static Texture2D ToTexture(byte[] rgba, int w, int h, Texture2D reuse = null)
    {
        Texture2D t = reuse;
        if (t == null || t.width != w || t.height != h) t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] c = new Color32[w * h];
        for (int i = 0; i < c.Length; i++)
            c[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
        t.SetPixels32(c);
        t.Apply(false);
        return t;
    }

    // 整张地图的地面（一张贴图，左下角对齐网格原点）。
    public static Sprite GroundSprite(TerrainMap m, float cellSize, int k = PixelsPerCell)
    {
        Texture2D t = ToTexture(TerrainPainter.PaintGround(m, k), m.width * k, m.height * k);
        t.name = "TerrainGround";
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), Vector2.zero, k / cellSize, 0, SpriteMeshType.FullRect);
    }

    // ============ 资源图集 ============

    const int Variants = 4;
    static Texture2D _atlas;
    static readonly Dictionary<int, Sprite> _res = new Dictionary<int, Sprite>();

    static int Key(ResourceKind kind, int size, int v) => ((int)kind * 4 + size) * Variants + v;

    // 资源的图：轴心是"站在地上的那一点"（树干根部 / 石头底部），放在占地中心。
    public static Sprite ResourceSprite(ResourceKind kind, int size, int variant)
    {
        if (_atlas == null) BuildAtlas();
        int v = ((variant % Variants) + Variants) % Variants;
        _res.TryGetValue(Key(kind, Mathf.Clamp(size, 1, 3), v), out Sprite s);
        return s;
    }

    class Item { public int key, w, h, pvx, pvy, x, y; public byte[] px; }

    static void BuildAtlas()
    {
        _res.Clear();
        List<Item> items = new List<Item>();
        for (int k = 0; k < 4; k++)
            for (int size = 1; size <= 3; size++)
            {
                if (k == (int)ResourceKind.Grass && size > 1) continue;
                for (int v = 0; v < Variants; v++)
                {
                    Item it = new Item { key = Key((ResourceKind)k, size, v) };
                    it.px = TerrainPainter.PaintResource((ResourceKind)k, size, v, out it.w, out it.h, out it.pvx, out it.pvy);
                    items.Add(it);
                }
            }
        // 货架式排布：从高到低，一行放满换下一行，图之间留 2 像素
        items.Sort((a, b) => b.h.CompareTo(a.h));
        const int atlasW = 512, pad = 2;
        int cx = pad, cy = pad, rowH = 0;
        foreach (Item it in items)
        {
            if (cx + it.w + pad > atlasW) { cx = pad; cy += rowH + pad; rowH = 0; }
            it.x = cx;
            it.y = cy;
            cx += it.w + pad;
            if (it.h > rowH) rowH = it.h;
        }
        int atlasH = 64;
        while (atlasH < cy + rowH + pad) atlasH *= 2;

        Color32[] all = new Color32[atlasW * atlasH];
        foreach (Item it in items)
            for (int y = 0; y < it.h; y++)
                for (int x = 0; x < it.w; x++)
                {
                    int o = (y * it.w + x) * 4;
                    all[(it.y + y) * atlasW + it.x + x] = new Color32(it.px[o], it.px[o + 1], it.px[o + 2], it.px[o + 3]);
                }
        _atlas = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);
        _atlas.name = "TerrainResourceAtlas";
        _atlas.filterMode = FilterMode.Point;
        _atlas.wrapMode = TextureWrapMode.Clamp;
        _atlas.SetPixels32(all);
        _atlas.Apply(false);
        foreach (Item it in items)
            _res[it.key] = Sprite.Create(_atlas, new Rect(it.x, it.y, it.w, it.h),
                new Vector2(it.pvx / (float)it.w, it.pvy / (float)it.h), TerrainPainter.ResourcePPU);
    }

    // 主菜单的小地图预览（1 小格 = 1 像素）。
    public static Texture2D Preview(TerrainMap m, Texture2D reuse = null)
    {
        return ToTexture(TerrainPainter.PaintPreview(m), m.width, m.height, reuse);
    }

    // 两行概况：地貌 / 水面、沼泽占比和资源数量。
    public static string Summary(TerrainMap m, bool jp)
    {
        if (m == null) return "";
        float open = Mathf.Max(1, m.OpenCells);
        int water = Mathf.RoundToInt(m.Count(TerrainType.Water) * 100f / open);
        int swamp = Mathf.RoundToInt(m.Count(TerrainType.Swamp) * 100f / open);
        string line1 = (jp ? "地形：" : "地貌：") + TerrainInfo.FlavorName(m.flavor, jp) + "　" + (m.width / Mathf.Max(1, m.sub)) + "×" + (m.height / Mathf.Max(1, m.sub));
        string line2 = jp
            ? "水面 " + water + "%　沼地 " + swamp + "%　木 " + m.CountResources(ResourceKind.Tree) + "　岩 " + m.CountResources(ResourceKind.Stone) + "　鉱石 " + m.CountResources(ResourceKind.Ore)
            : "水面 " + water + "%　沼泽 " + swamp + "%　树 " + m.CountResources(ResourceKind.Tree) + "　石头 " + m.CountResources(ResourceKind.Stone) + "　矿石 " + m.CountResources(ResourceKind.Ore);
        return line1 + "\n" + line2;
    }
}
