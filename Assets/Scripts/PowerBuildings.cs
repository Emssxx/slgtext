using System.Collections.Generic;
using UnityEngine;

// 电力 / 气温 / 水源相关的建筑（运行时生成，不用在 Project 里建资产）：
//   风力发电机（电力类，不需要终端机）、蓄电池（电力类，需要终端机 Lv1）、营地加温器、营地降温器（生活类，需要终端机 Lv1）、
//   雨水收集器、水源净化器（生产类，不需要终端机；净化器要接电）。
// 图片是代码画的像素图；以后有美术素材时，建同名的 BuildableData 资产拖进 BuildManager.buildables 即可
// （id 相同的资产优先，这里就不再重复添加）。
public static class PowerBuildings
{
    public const string TurbineId = "wind_turbine";
    public const string BatteryId = "power_battery";
    public const string HeaterId = "camp_heater";
    public const string CoolerId = "camp_cooler";
    public const string CollectorId = "rain_collector";
    public const string PurifierId = "water_purifier";

    static GameObject _holder;   // 模板的父物体（关着的，所以模板不会出现在场景里）
    static Sprite _outline;
    static readonly Dictionary<string, BuildableData> _data = new Dictionary<string, BuildableData>();

    // 把这些建筑追加到建造菜单列表里（BuildManager.Awake 调用）。
    public static BuildableData[] AppendTo(BuildableData[] list)
    {
        List<BuildableData> result = new List<BuildableData>();
        HashSet<string> ids = new HashSet<string>();
        if (list != null)
            foreach (BuildableData b in list)
                if (b != null) { result.Add(b); ids.Add(b.id); }
        foreach (BuildableData b in Create())
            if (!ids.Contains(b.id)) result.Add(b);
        return result.ToArray();
    }

    static List<BuildableData> Create()
    {
        List<BuildableData> list = new List<BuildableData>();
        list.Add(Get(TurbineId, "风力发电机", "風力発電機", PowerNodeKind.Turbine, BuildCategory.Power, 0, TurbineSprite()));
        list.Add(Get(BatteryId, "蓄电池", "蓄電池", PowerNodeKind.Battery, BuildCategory.Power, 1, BatterySprite()));
        list.Add(Get(HeaterId, "营地加温器", "拠点ヒーター", PowerNodeKind.Heater, BuildCategory.Living, 1, BoxSprite(true)));
        list.Add(Get(CoolerId, "营地降温器", "拠点クーラー", PowerNodeKind.Cooler, BuildCategory.Living, 1, BoxSprite(false)));
        list.Add(Get(CollectorId, "雨水收集器", "雨水タンク", null, BuildCategory.Production, 0, CollectorSprite()));
        list.Add(Get(PurifierId, "水源净化器", "浄水器", PowerNodeKind.Purifier, BuildCategory.Production, 0, PurifierSprite()));
        return list;
    }

    static BuildableData Get(string id, string zh, string ja, PowerNodeKind? kind, BuildCategory cat, int tier, Sprite sprite)
    {
        if (_data.TryGetValue(id, out BuildableData cached) && cached != null && cached.prefab != null) return cached;
        BuildableData d = ScriptableObject.CreateInstance<BuildableData>();
        d.id = id;
        d.displayName = zh;
        d.displayNameJa = ja;
        d.sprite = sprite;
        d.blocksMovement = true;
        d.buildTime = 2f;
        d.buildCategory = cat;
        d.requiredTier = tier;
        d.category = BuildableCategory.Other;
        d.prefab = MakeTemplate(id, kind, sprite);
        _data[id] = d;
        return d;
    }

    // 模板：放在一个关着的父物体下面（自己是开着的），这样 Instantiate 出来的副本是开着的，模板本身却看不见。
    static GameObject MakeTemplate(string id, PowerNodeKind? kind, Sprite sprite)
    {
        if (_holder == null)
        {
            _holder = new GameObject("PowerBuildingTemplates");
            _holder.SetActive(false);
        }
        GameObject go = new GameObject(id);
        go.transform.SetParent(_holder.transform, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 0;
        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.9f, 0.9f);
        PowerNode node = null;
        if (kind.HasValue)
        {
            node = go.AddComponent<PowerNode>();
            node.kind = kind.Value;
            if (kind.Value == PowerNodeKind.Purifier) node.level = 1; // 净化器建好就开 1 档
        }

        if (kind == PowerNodeKind.Battery)
        {
            // 电量条：底边对齐的绿色方块，按存电量拉高
            GameObject g = new GameObject("Gauge");
            g.transform.SetParent(go.transform, false);
            SpriteRenderer gsr = g.AddComponent<SpriteRenderer>();
            gsr.sprite = GaugeSprite();
            gsr.sortingOrder = 1;
            gsr.enabled = false;
            node.gauge = gsr;
        }

        if (id == CollectorId)
        {
            // 水箱里的水位：底边对齐的蓝色方块，按存水量拉高
            GameObject water = new GameObject("WaterLevel");
            water.transform.SetParent(go.transform, false);
            SpriteRenderer wsr = water.AddComponent<SpriteRenderer>();
            wsr.sprite = WaterSprite();
            wsr.sortingOrder = 1;
            wsr.enabled = false;
            RainCollector rc = go.AddComponent<RainCollector>();
            rc.waterLevel = wsr;
        }

        if (kind == PowerNodeKind.Turbine)
        {
            GameObject blades = new GameObject("Blades");
            blades.transform.SetParent(go.transform, false);
            blades.transform.localPosition = new Vector3(0f, 0.19f, 0f);
            SpriteRenderer bsr = blades.AddComponent<SpriteRenderer>();
            bsr.sprite = BladeSprite();
            bsr.sortingOrder = 1;
            TurbineSpinner spin = blades.AddComponent<TurbineSpinner>();
            spin.node = node;
        }
        return go;
    }

    // ============ 像素图（32×32，1 格 = 1 单位）============

    static Texture2D NewTex()
    {
        Texture2D t = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(0f, 0f, 0f, 0f);
        for (int x = 0; x < 32; x++) for (int y = 0; y < 32; y++) t.SetPixel(x, y, clear);
        return t;
    }

    static void Rect(Texture2D t, int x0, int y0, int x1, int y1, Color c)
    {
        for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++)
            if (x >= 0 && x < 32 && y >= 0 && y < 32) t.SetPixel(x, y, c);
    }

    static Sprite Make(Texture2D t, Vector2 pivot)
    {
        t.Apply();
        return Sprite.Create(t, new UnityEngine.Rect(0, 0, 32, 32), pivot, 32f);
    }

    // 风力发电机：底座 + 塔杆 + 机舱（叶片单独一张图，会转）
    static Sprite TurbineSprite()
    {
        Texture2D t = NewTex();
        Color dark = new Color(0.25f, 0.27f, 0.3f), mid = new Color(0.55f, 0.58f, 0.62f), light = new Color(0.8f, 0.82f, 0.85f);
        Rect(t, 9, 1, 22, 4, dark);        // 底座
        Rect(t, 14, 5, 17, 21, mid);       // 塔杆
        Rect(t, 14, 5, 14, 21, light);
        Rect(t, 12, 20, 19, 24, dark);     // 机舱
        return Make(t, new Vector2(0.5f, 0.5f));
    }

    static Sprite BladeSprite()
    {
        Texture2D t = NewTex();
        Color c = new Color(0.92f, 0.94f, 0.96f);
        // 三片叶子：从中心向 90°、210°、330° 画线
        float[] angles = { 90f, 210f, 330f };
        foreach (float a in angles)
        {
            float rad = a * Mathf.Deg2Rad;
            for (int r = 2; r <= 14; r++)
            {
                int x = Mathf.RoundToInt(15.5f + Mathf.Cos(rad) * r);
                int y = Mathf.RoundToInt(15.5f + Mathf.Sin(rad) * r);
                Rect(t, x, y, x, y, c);
                if (r < 10) Rect(t, x + 1, y, x + 1, y, c);
            }
        }
        Rect(t, 14, 14, 17, 17, new Color(0.3f, 0.32f, 0.35f)); // 轴心
        return Make(t, new Vector2(0.5f, 0.5f));
    }

    // 加温器（橙红 + 发热条） / 降温器（蓝 + 雪花）
    static Sprite BoxSprite(bool heater)
    {
        Texture2D t = NewTex();
        Color body = heater ? new Color(0.72f, 0.3f, 0.16f) : new Color(0.18f, 0.42f, 0.72f);
        Color edge = heater ? new Color(0.45f, 0.17f, 0.08f) : new Color(0.1f, 0.25f, 0.45f);
        Color mark = heater ? new Color(1f, 0.72f, 0.38f) : new Color(0.88f, 0.96f, 1f);
        Rect(t, 5, 3, 26, 26, edge);
        Rect(t, 7, 5, 24, 24, body);
        Rect(t, 12, 27, 19, 29, edge); // 顶部管子
        if (heater)
        {
            for (int i = 0; i < 3; i++) Rect(t, 10 + i * 5, 8, 11 + i * 5, 21, mark); // 三根发热条
        }
        else
        {
            Rect(t, 15, 8, 16, 21, mark);   // 竖
            Rect(t, 9, 14, 22, 15, mark);   // 横
            for (int i = 0; i < 5; i++) { Rect(t, 11 + i, 10 + i, 11 + i, 10 + i, mark); Rect(t, 20 - i, 10 + i, 20 - i, 10 + i, mark); } // 斜
            for (int i = 0; i < 5; i++) { Rect(t, 11 + i, 19 - i, 11 + i, 19 - i, mark); Rect(t, 20 - i, 19 - i, 20 - i, 19 - i, mark); }
        }
        return Make(t, new Vector2(0.5f, 0.5f));
    }

    // 蓄电池：深色电池外壳 + 正极帽（电量条单独一张图）
    static Sprite BatterySprite()
    {
        Texture2D t = NewTex();
        Color edge = new Color(0.16f, 0.18f, 0.2f), body = new Color(0.1f, 0.11f, 0.13f), cap = new Color(0.75f, 0.62f, 0.2f);
        Rect(t, 8, 2, 23, 25, edge);      // 外壳
        Rect(t, 10, 4, 21, 23, body);     // 内部（空的部分）
        Rect(t, 13, 26, 18, 28, cap);     // 正极帽
        Rect(t, 10, 9, 21, 9, edge);      // 刻度线
        Rect(t, 10, 14, 21, 14, edge);
        Rect(t, 10, 19, 21, 19, edge);
        return Make(t, new Vector2(0.5f, 0.5f));
    }

    // 电量条：只画电池内部，轴心放在底部，缩放 Y 时从下往上涨
    static Sprite GaugeSprite()
    {
        Texture2D t = NewTex();
        Rect(t, 11, 4, 20, 22, new Color(0.35f, 0.9f, 0.45f, 0.85f));
        return Make(t, new Vector2(0.5f, 4f / 32f));
    }

    // 雨水收集器：上面一个漏斗，下面一个桶（水位单独一张图）
    static Sprite CollectorSprite()
    {
        Texture2D t = NewTex();
        Color edge = new Color(0.22f, 0.26f, 0.3f), body = new Color(0.42f, 0.48f, 0.54f), inner = new Color(0.16f, 0.19f, 0.23f);
        Color funnel = new Color(0.58f, 0.64f, 0.7f);
        Rect(t, 8, 2, 23, 20, edge);      // 桶
        Rect(t, 10, 4, 21, 18, inner);    // 桶内（空的部分）
        Rect(t, 8, 10, 23, 10, body);     // 桶箍
        for (int i = 0; i < 6; i++) Rect(t, 4 + i, 27 - i, 27 - i, 27 - i, funnel); // 漏斗（倒梯形）
        Rect(t, 14, 21, 17, 22, funnel);  // 漏斗口
        Rect(t, 10, 4, 10, 18, body);     // 桶内左边的高光边
        return Make(t, new Vector2(0.5f, 0.5f));
    }

    // 水位：只画桶内那一块，轴心放在桶底，这样缩放 Y 时水面从底部往上涨
    static Sprite WaterSprite()
    {
        Texture2D t = NewTex();
        Rect(t, 11, 4, 21, 18, new Color(0.3f, 0.62f, 0.95f, 0.9f));
        Rect(t, 11, 18, 21, 18, new Color(0.65f, 0.85f, 1f, 0.95f)); // 水面亮线
        return Make(t, new Vector2(0.5f, 4f / 32f));
    }

    // 水源净化器：机箱 + 水滴 + 过滤层
    static Sprite PurifierSprite()
    {
        Texture2D t = NewTex();
        Color edge = new Color(0.12f, 0.3f, 0.36f), body = new Color(0.2f, 0.55f, 0.6f), drop = new Color(0.75f, 0.95f, 1f);
        Color filter = new Color(0.9f, 0.9f, 0.82f);
        Rect(t, 5, 3, 26, 26, edge);
        Rect(t, 7, 5, 24, 24, body);
        Rect(t, 9, 7, 22, 9, filter);     // 过滤层
        Rect(t, 9, 11, 22, 11, filter);
        // 水滴
        Rect(t, 15, 21, 16, 22, drop);
        Rect(t, 14, 18, 17, 20, drop);
        Rect(t, 13, 14, 18, 17, drop);
        Rect(t, 14, 13, 17, 13, drop);
        Rect(t, 3, 14, 4, 16, edge);      // 进水管
        Rect(t, 27, 8, 28, 10, edge);     // 出水管
        return Make(t, new Vector2(0.5f, 0.5f));
    }

    // 电线工具选中时的方框
    public static Sprite OutlineSprite()
    {
        if (_outline != null) return _outline;
        Texture2D t = NewTex();
        Color c = Color.white;
        Rect(t, 0, 0, 31, 1, c); Rect(t, 0, 30, 31, 31, c); Rect(t, 0, 0, 1, 31, c); Rect(t, 30, 0, 31, 31, c);
        _outline = Make(t, new Vector2(0.5f, 0.5f));
        return _outline;
    }
}
