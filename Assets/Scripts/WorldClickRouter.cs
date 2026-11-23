using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

// ============================================================================
// 场景点击统一入口（界面层）：鼠标左键点地图上的东西 → 弹出对应的窗口。
//   · 幸存者（NPC）   → 对话窗口（NpcChatWindow，原来写在 NPC 预制体的 NpcClickHandler 里）
//   · ABA 队员 / 敌人 → 信息浮窗（名字、生命、状态……）
//   · 建筑           → 信息浮窗（名字、类别、耐久、电力）；终端机这种有自己面板的，交给它自己（IWorldClick）
//   · 资源点         → 信息浮窗（能采到什么、采集进度）
//   点空地 / 右键 / Esc 关掉信息浮窗；目标死了 / 没了浮窗自己关。
//   蓝图（规划区）不管：点蓝图照旧由 ConstructionManager 弹施工指派。建造模式 / 接电线时不响应。
//
// 【怎么判断点中了谁】不靠 OnMouseDown（它只发给鼠标下第一个碰撞体，主场景地面 Tilemap 的碰撞体会把 NPC 挡住；
//   敌人、ABA 队员身上也没有碰撞体）。改成：人物看身上精灵图的范围（选离鼠标最近的），建筑 / 资源看鼠标所在的格子。
//   优先级：NPC > ABA 队员 > 敌人 > 建筑 > 资源（人站在建筑上时点的是人）。
//
// 【以后加新东西的弹窗】不用改这里：
//   · 想自己开专用面板：在那个物体上的脚本实现 IWorldClick（返回 true = 自己处理了）。
//   · 想改信息浮窗的内容：实现 IWorldInfo（标题、几行文字、血条）。
// 场景里不用摆：游戏一开始自动生成（跨场景保留）。
// ============================================================================

// 被点中时自己处理（比如终端机打开终端面板）。返回 true = 处理了，不再弹通用信息浮窗。
public interface IWorldClick
{
    bool OnWorldClick();
}

// 自定义信息浮窗的内容（不实现就用这里按类型写好的默认内容）。
public interface IWorldInfo
{
    string InfoTag { get; }                    // 浮窗左上角的小标签（英文短词，如 "turret"）
    string InfoTitle { get; }                  // 标题（名字）
    float InfoHp01 { get; }                    // 血条 0~1；< 0 = 不显示血条
    void InfoLines(List<string> lines);        // 往里加几行说明（可以用 TMP 富文本）
}

public class WorldClickRouter : MonoBehaviour
{
    public static WorldClickRouter Instance { get; private set; }

    [Header("信息浮窗")]
    public float width = 300f;
    public float refreshInterval = 0.25f;      // 浮窗里的数值多久刷新一次（秒）
    [Tooltip("人物精灵范围往外放宽多少（单位），太小的角色也好点")]
    public float pickPadding = 0.06f;

    enum Kind { None, Npc, Aba, Enemy, Building, Resource }

    struct Target
    {
        public Kind kind;
        public GameObject go;
        public bool Valid => kind != Kind.None && go != null;
    }

    Camera _cam;
    UiPopup _popup;
    TextMeshProUGUI _title, _lines;
    RectTransform _barBg, _barFill;
    Image _barFillImg;
    Target _shown;
    float _nextRefresh;
    readonly List<string> _buf = new List<string>();

    static readonly Color HpGreen = new Color(0.47f, 0.82f, 0.5f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance == null) new GameObject("WorldClickRouter").AddComponent<WorldClickRouter>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ============ 每帧 ============

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame) HandleClick(mouse);
            else if (mouse.rightButton.wasPressedThisFrame && !PointerOverUI(mouse)) CloseInfo();
        }
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) CloseInfo();

        if (_popup != null && _popup.IsOpen)
        {
            if (!StillValid(_shown)) { CloseInfo(); return; }
            if (Time.unscaledTime >= _nextRefresh) { Fill(_shown); _nextRefresh = Time.unscaledTime + refreshInterval; }
            Follow(_shown);
        }
    }

    void HandleClick(Mouse mouse)
    {
        if (PointerOverUI(mouse)) return;                                        // 点在界面上：让按钮 / 输入框处理
        if (WireTool.Active || (BuildManager.Instance != null && BuildManager.Instance.IsBuildMode)) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        Vector2 sp = mouse.position.ReadValue();
        Vector3 wp = _cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -_cam.transform.position.z));
        wp.z = 0f;

        Target t = Pick(wp);
        if (!t.Valid) { CloseInfo(); return; }

        if (t.kind == Kind.Npc) { CloseInfo(); OpenNpc(t.go.GetComponent<NpcView>()); return; }

        IWorldClick custom = t.go.GetComponentInParent<IWorldClick>();
        if (custom != null && custom.OnWorldClick()) { CloseInfo(); return; }  // 有专用面板的（终端机等）

        ShowInfo(t);
    }

    // ============ 点中了谁 ============

    Target Pick(Vector3 wp)
    {
        GridManager g = GridManager.Instance;
        Vector2Int cell = g != null ? g.WorldToCell(wp) : Vector2Int.zero;
        bool onSite = g != null && ConstructionSite.At(cell) != null;           // 蓝图：交给施工指派

        // 1. 幸存者
        GameObject best = null; float bestD = float.MaxValue;
        foreach (NpcView v in NpcView.GetAllViews())
            if (v != null && !string.IsNullOrEmpty(v.npcId) && v.gameObject.activeInHierarchy) Consider(v.gameObject, wp, ref best, ref bestD);
        if (best == null)
            foreach (Collider2D h in Physics2D.OverlapPointAll(wp))            // 兜底：NPC 身上的碰撞体
            {
                NpcView v = h != null ? h.GetComponentInParent<NpcView>() : null;
                if (v != null && !string.IsNullOrEmpty(v.npcId)) { best = v.gameObject; break; }
            }
        if (best != null) return new Target { kind = Kind.Npc, go = best };
        if (onSite) return default(Target);

        // 2. ABA 队员
        foreach (AbaOperative a in AbaOperative.All)
            if (a != null && a.Alive && a.Visible) Consider(a.gameObject, wp, ref best, ref bestD);
        if (best != null) return new Target { kind = Kind.Aba, go = best };

        // 3. 敌人
        foreach (Enemy e in Enemy.All)
            if (e != null && e.Alive && e.Visible) Consider(e.gameObject, wp, ref best, ref bestD);
        if (best != null) return new Target { kind = Kind.Enemy, go = best };

        // 4. 有自己点击处理的东西（IWorldClick，比如场景里摆的终端机）：看碰撞体
        foreach (Collider2D h in Physics2D.OverlapPointAll(wp))
        {
            if (h == null) continue;
            if (h.GetComponentInParent<IWorldClick>() is Component c) return new Target { kind = Kind.Building, go = c.gameObject };
        }

        // 5. 建筑：先看鼠标所在的格子，再看高出占地的那部分图（风机、终端机的上半截）
        if (g != null)
        {
            BuildingFootprint fp = BuildingFootprint.At(cell);
            if (fp == null)
                foreach (BuildingFootprint f in BuildingFootprint.All)
                    if (f != null && f.GetComponent<ConstructionSite>() == null) Consider(f.gameObject, wp, ref best, ref bestD);
            if (fp != null && fp.GetComponent<ConstructionSite>() == null) best = fp.gameObject;
            if (best != null) return new Target { kind = Kind.Building, go = best };

            // 6. 资源点
            ResourceNode r = ResourceNode.At(cell);
            if (r != null) return new Target { kind = Kind.Resource, go = r.gameObject };
        }
        return default(Target);
    }

    // 鼠标在这个物体的精灵图范围里 → 记下离中心最近的那个
    void Consider(GameObject go, Vector3 wp, ref GameObject best, ref float bestD)
    {
        if (!SpriteBounds(go, out Bounds b)) return;
        b.Expand(pickPadding * 2f);
        if (!b.Contains(new Vector3(wp.x, wp.y, b.center.z))) return;
        float d = ((Vector2)b.center - (Vector2)wp).sqrMagnitude;
        if (d < bestD) { bestD = d; best = go; }
    }

    // 物体身上看得见的精灵图合起来的范围（不算地面层的影子、血迹）
    static bool SpriteBounds(GameObject go, out Bounds b)
    {
        b = default(Bounds);
        bool any = false;
        foreach (SpriteRenderer sr in go.GetComponentsInChildren<SpriteRenderer>())
        {
            if (sr == null || !sr.enabled || sr.sprite == null || sr.sortingOrder < 0 || sr.color.a < 0.05f) continue;
            if (!any) { b = sr.bounds; any = true; } else b.Encapsulate(sr.bounds);
        }
        return any;
    }

    static bool StillValid(Target t)
    {
        if (!t.Valid || !t.go.activeInHierarchy) return false;
        switch (t.kind)
        {
            case Kind.Aba: { AbaOperative a = t.go.GetComponent<AbaOperative>(); return a != null && a.Alive; }
            case Kind.Enemy: { Enemy e = t.go.GetComponent<Enemy>(); return e != null && e.Alive; }
        }
        return true;
    }

    // ============ 幸存者：对话窗口 ============

    static void OpenNpc(NpcView v)
    {
        if (v == null || string.IsNullOrEmpty(v.npcId)) return;
        NpcAgent agent = FindAnyObjectByType<NpcAgent>();
        if (agent != null) agent.SwitchTarget(v.npcId);    // 同时切换对话目标（里面会打开窗口）
        else NpcChatWindow.Ensure().Open(v.npcId);
    }

    // ============ 信息浮窗 ============

    void ShowInfo(Target t)
    {
        EnsurePopup();
        _shown = t;
        Vector2 size = Fill(t);
        _nextRefresh = Time.unscaledTime + refreshInterval;
        Vector2 origin = AnchorCanvas(t);
        _popup.OpenCanvas(origin, TopLeftFor(origin, size), size, true);
    }

    public void CloseInfo()
    {
        if (_popup != null && _popup.IsOpen) _popup.Close();
        _shown = default(Target);
    }

    // 浮窗跟着目标走（目标在动的时候）
    void Follow(Target t)
    {
        if (_popup.Animating) return;
        Vector2 origin = AnchorCanvas(t);
        _popup.PlaceCanvas(TopLeftFor(origin, _popup.Size));
    }

    Vector2 AnchorCanvas(Target t)
    {
        if (_cam == null) _cam = Camera.main;
        Vector3 p = t.go.transform.position;
        if (SpriteBounds(t.go, out Bounds b)) p = new Vector3(b.max.x, b.center.y, 0f);   // 目标的右边缘
        Vector3 s = _cam != null ? _cam.WorldToScreenPoint(p) : Vector3.zero;
        return _popup.ToCanvas(s);
    }

    Vector2 TopLeftFor(Vector2 origin, Vector2 size)
    {
        Vector2 tl = origin + new Vector2(40f, size.y * 0.5f + 30f);
        if (tl.x + size.x > _popup.CanvasSize.x - 8f) tl.x = origin.x - 40f - size.x - 120f;   // 右边放不下就放左边
        return tl;
    }

    // 按目标类型写内容，返回浮窗大小
    Vector2 Fill(Target t)
    {
        string tag, title; float hp01 = -1f;
        _buf.Clear();
        IWorldInfo info = t.go.GetComponentInParent<IWorldInfo>();
        if (info != null) { tag = info.InfoTag; title = info.InfoTitle; hp01 = info.InfoHp01; info.InfoLines(_buf); }
        else
            switch (t.kind)
            {
                case Kind.Aba: FillAba(t.go.GetComponent<AbaOperative>(), out tag, out title, out hp01); break;
                case Kind.Enemy: FillEnemy(t.go.GetComponent<Enemy>(), out tag, out title, out hp01); break;
                case Kind.Building: FillBuilding(t.go, out tag, out title, out hp01); break;
                case Kind.Resource: FillResource(t.go.GetComponent<ResourceNode>(), out tag, out title); break;
                default: tag = "info"; title = t.go.name; break;
            }

        _popup.SetTag(string.IsNullOrEmpty(tag) ? "info" : tag);
        _title.text = title;
        _lines.text = string.Join("\n", _buf);

        float y = UiStyle.TagH + 10f;
        Place(_title.rectTransform, 14f, y, width - 28f, 26f);
        y += 30f;
        bool bar = hp01 >= 0f;
        _barBg.gameObject.SetActive(bar);
        if (bar)
        {
            Place(_barBg, 14f, y, width - 28f, 6f);
            _barFill.anchorMax = new Vector2(Mathf.Clamp01(hp01), 1f);
            _barFillImg.color = hp01 > 0.5f ? HpGreen : (hp01 > 0.25f ? UiStyle.Yellow : UiStyle.Red);
            y += 14f;
        }
        float lh = _buf.Count > 0 ? _lines.GetPreferredValues(_lines.text, width - 28f, 0f).y : 0f;
        Place(_lines.rectTransform, 14f, y, width - 28f, lh);
        y += lh + 12f;

        Vector2 size = new Vector2(width, y);
        if (_popup.IsOpen && !_popup.Animating && _popup.Size != size) _popup.Resize(size);
        return size;
    }

    void FillEnemy(Enemy e, out string tag, out string title, out float hp01)
    {
        tag = "enemy"; title = e != null ? e.Name : "?"; hp01 = -1f;
        if (e == null || e.Def == null) return;
        EnemyDef d = e.Def;
        float max = Mathf.Max(1f, Mathf.Max(d.maxHp, e.Hp));
        hp01 = e.Hp / max;
        _buf.Add(Row("wclick_hp", Mathf.CeilToInt(e.Hp) + " / " + Mathf.CeilToInt(max)));
        _buf.Add(Row("wclick_kind", L(d.kind == EnemyKind.Creature ? "wclick_creature" : "wclick_human") + " · " + L(d.tier == EnemyTier.Strong ? "wclick_strong" : "wclick_weak")));
        _buf.Add(Row("wclick_goal", L("wclick_goal_" + e.goal.ToString().ToLowerInvariant())));
        _buf.Add(Row("wclick_state", L("wclick_st_" + e.state.ToString().ToLowerInvariant())));
        _buf.Add(Row("wclick_dmg", d.damage.ToString("0.#") + "  (" + L(d.attackRange > 1.5f ? "wclick_ranged" : "wclick_melee") + ")"));
        if (d.wallDamage > 0f) _buf.Add(Warn(L("wclick_wallbreaker")));
        if (d.sanityAura > 0f) _buf.Add(Warn(L("wclick_sanity")));
    }

    void FillAba(AbaOperative a, out string tag, out string title, out float hp01)
    {
        tag = "aba"; title = "?"; hp01 = -1f;
        if (a == null) return;
        title = LF("wclick_aba_title", a.Callsign);
        float max = Mathf.Max(1f, a.MaxHp);
        hp01 = a.Hp / max;
        _buf.Add(Row("wclick_hp", Mathf.CeilToInt(a.Hp) + " / " + Mathf.CeilToInt(max)));
        _buf.Add(Row("wclick_stamina", Mathf.RoundToInt(a.Stamina) + " / 100"));
        if (a.Weapon != null)
        {
            string w = a.Weapon.def != null ? a.Weapon.def.Label : a.Weapon.id;
            string mag = a.Reloading ? L("wclick_reloading") : Mathf.RoundToInt(a.MagFraction * a.Weapon.magSize) + " / " + a.Weapon.magSize;
            _buf.Add(Row("wclick_weapon", w + "   " + mag));
        }
    }

    void FillBuilding(GameObject go, out string tag, out string title, out float hp01)
    {
        tag = "building"; hp01 = -1f;
        BuildingFootprint fp = go.GetComponent<BuildingFootprint>();
        BuildableData d = StructureHealth.DataOf(fp);
        title = d != null ? d.LocalizedName : L("wclick_building");
        if (d != null) _buf.Add(Row("wclick_category", L("wclick_cat_" + d.category.ToString().ToLowerInvariant())));
        StructureHealth sh = go.GetComponent<StructureHealth>();
        if (sh != null && sh.maxHp > 0f)
        {
            hp01 = sh.hp / sh.maxHp;
            _buf.Add(Row("wclick_dura", Mathf.CeilToInt(sh.hp) + " / " + Mathf.CeilToInt(sh.maxHp)));
        }
        else _buf.Add(Row("wclick_dura", L("wclick_intact")));
        PowerNode pn = go.GetComponentInChildren<PowerNode>();
        if (pn != null)
        {
            string p = L(pn.powered || pn.IsGenerator ? "wclick_powered" : "wclick_unpowered");
            if (pn.IsStorage) p += "   " + L("wclick_charge") + " " + Mathf.RoundToInt(pn.charge);
            _buf.Add(Row("wclick_power", p));
        }
        if (fp != null) _buf.Add(Row("wclick_size", fp.size.x + " × " + fp.size.y));
    }

    void FillResource(ResourceNode r, out string tag, out string title)
    {
        tag = "resource"; title = "?";
        if (r == null) return;
        bool jp = IsJp;
        title = r.DisplayName(jp);
        // HoverText 第一行是加粗的名字（标题已经有了），后面几行是采集信息
        string[] parts = r.HoverText(jp).Split('\n');
        for (int i = 1; i < parts.Length; i++) _buf.Add(parts[i]);
    }

    // ============ 浮窗外观 ============

    void EnsurePopup()
    {
        if (_popup != null) return;
        _popup = UiPopup.Create("WorldInfoPopup", 70);
        _popup.transform.SetParent(transform, false);
        _popup.KeepTether = false;      // 目标会动：连线会跟不上，打开完就收掉
        _popup.Interactable = false;    // 鼠标穿过去（不挡地图点击）
        RectTransform body = _popup.Body;

        _title = NewText(body, 19f);
        _title.fontStyle = FontStyles.Bold;

        _barBg = NewRect("HpBar", body);
        Image bg = _barBg.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.12f);
        bg.raycastTarget = false;
        _barFill = NewRect("Fill", _barBg);
        _barFill.anchorMin = Vector2.zero; _barFill.anchorMax = Vector2.one;
        _barFill.offsetMin = _barFill.offsetMax = Vector2.zero;
        _barFillImg = _barFill.gameObject.AddComponent<Image>();
        _barFillImg.raycastTarget = false;

        _lines = NewText(body, 15f);
        _lines.color = UiStyle.TextSub;
        _lines.textWrappingMode = TextWrappingModes.Normal;
        _lines.overflowMode = TextOverflowModes.Overflow;
        _lines.lineSpacing = 6f;
    }

    static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
    }

    static TextMeshProUGUI NewText(Transform parent, float size)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = GameFonts.Find();
        if (f != null) t.font = f;
        t.fontSize = size;
        t.color = UiStyle.TextMain;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // ============ 小工具 ============

    static string Row(string labelKey, string value) => "<color=#8E9490>" + L(labelKey) + "</color>   " + value;
    static string Warn(string text) => "<color=#FF6B61>" + text + "</color>";

    static bool PointerOverUI(Mouse mouse)
    {
        if (EventSystem.current == null) return false;
        PointerEventData data = new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, results);
        return results.Count > 0;
    }

    static bool IsJp => LocalizationManager.Instance != null && LocalizationManager.Instance.current == Language.Japanese;
    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] a) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, a) : string.Format(key, a);
}
