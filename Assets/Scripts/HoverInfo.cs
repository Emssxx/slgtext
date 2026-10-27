using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;

// 鼠标悬停信息框：鼠标停在 NPC / 建筑上一小会儿，就在它头顶弹出一个半透明方框：
//   NPC：姓名、职业、正在做什么、生命 / 饥饿 / 体力（缺水时多一行）。
//   建筑：名称、是否在工作（发电 / 储电 / 加温 / 净化 / 接雨水 / 施工中……）。
// 另外：鼠标在地图上停够半秒，鼠标左上方弹出这一格的坐标浮窗（内容由 CellInfo 的各个提供者写，以后加地形信息就加提供者）；
// 同时悬停在 NPC / 建筑上时，坐标浮窗改为显示在它们的信息框下方（左边对齐）。
// 鼠标在 UI 上时不显示。用不受暂停影响的时间（建造模式会暂停游戏）。
// 由 HudLayout 调 HoverInfo.Ensure() 创建，自带一个最上层的 Canvas。
public class HoverInfo : MonoBehaviour
{
    public static HoverInfo Instance { get; private set; }

    [Tooltip("鼠标停多久才弹出（秒）")]
    public float delay = 0.25f;
    [Tooltip("信息框最宽多少（1920×1080 下的像素）")]
    public float maxWidth = 300f;
    [Tooltip("鼠标在同一格停多久才弹出坐标浮窗（秒）")]
    public float cellDelay = 0.5f;

    const float Pad = 10f;
    const float MinSize = 150f; // 最小宽度：短内容也是一个方块，不会缩成一条

    Camera _cam;
    RectTransform _canvasRt, _box, _tail;
    TextMeshProUGUI _text;
    bool _shown;

    GameObject _target;      // 正在悬停的物体（NPC 或建筑；格子里没有物体时是 _cellAnchor）
    NpcView _npc;
    float _hover, _refresh;
    GameObject _cellAnchor;  // 没碰撞体的建筑（如箱子）：用这个空物体代替，记下格子里的建筑名
    string _cellName;
    Vector2Int _cell = new Vector2Int(int.MinValue, int.MinValue);
    GameObject _cellObj;

    // —— 坐标浮窗 ——
    RectTransform _cellBox;
    TextMeshProUGUI _cellText;
    bool _cellShown;
    float _cellHover, _cellRefresh;
    Vector2Int _cellCur = new Vector2Int(int.MinValue, int.MinValue);

    public static HoverInfo Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("HoverInfo").AddComponent<HoverInfo>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
        _cellAnchor = new GameObject("HoverCellAnchor");
        _cellAnchor.transform.SetParent(transform, false);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void BuildUI()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 70; // 在建造菜单（50）上面
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;
        _canvasRt = (RectTransform)transform;

        // 方框：细边框 + 半透明深色底 + 文字；下面一个小尖角指向物体
        _box = NewRect("Box", transform);
        _box.anchorMin = _box.anchorMax = new Vector2(0.5f, 0.5f);
        _box.pivot = new Vector2(0.5f, 0f);
        Image border = _box.gameObject.AddComponent<Image>();
        border.color = new Color(1f, 1f, 1f, 0.28f);
        border.raycastTarget = false;
        RectTransform bg = NewRect("Bg", _box);
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = new Vector2(1f, 1f); bg.offsetMax = new Vector2(-1f, -1f);
        Image bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.05f, 0.08f, 0.72f);
        bgImg.raycastTarget = false;

        RectTransform tr = NewRect("Text", _box);
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(Pad, Pad); tr.offsetMax = new Vector2(-Pad, -Pad);
        _text = tr.gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = GameFonts.Find();
        if (font != null) _text.font = font;
        _text.fontSize = 17f;
        _text.color = Color.white;
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.textWrappingMode = TextWrappingModes.Normal;
        _text.raycastTarget = false;

        _tail = NewRect("Tail", _box);
        _tail.anchorMin = _tail.anchorMax = new Vector2(0.5f, 0f);
        _tail.pivot = new Vector2(0.5f, 1f);
        _tail.sizeDelta = new Vector2(14f, 8f);
        Image tailImg = _tail.gameObject.AddComponent<Image>();
        tailImg.sprite = TailSprite();
        tailImg.color = new Color(0.04f, 0.05f, 0.08f, 0.72f);
        tailImg.raycastTarget = false;

        _box.gameObject.SetActive(false);

        // 坐标浮窗：同样的半透明方框，没有尖角
        _cellBox = NewRect("CellBox", transform);
        _cellBox.anchorMin = _cellBox.anchorMax = new Vector2(0.5f, 0.5f);
        Image cborder = _cellBox.gameObject.AddComponent<Image>();
        cborder.color = new Color(1f, 1f, 1f, 0.22f);
        cborder.raycastTarget = false;
        RectTransform cbg = NewRect("Bg", _cellBox);
        cbg.anchorMin = Vector2.zero; cbg.anchorMax = Vector2.one;
        cbg.offsetMin = new Vector2(1f, 1f); cbg.offsetMax = new Vector2(-1f, -1f);
        Image cbgImg = cbg.gameObject.AddComponent<Image>();
        cbgImg.color = new Color(0.04f, 0.05f, 0.08f, 0.62f);
        cbgImg.raycastTarget = false;
        RectTransform ctr = NewRect("Text", _cellBox);
        ctr.anchorMin = Vector2.zero; ctr.anchorMax = Vector2.one;
        ctr.offsetMin = new Vector2(CellPad, CellPad - 2f); ctr.offsetMax = new Vector2(-CellPad, -(CellPad - 2f));
        _cellText = ctr.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) _cellText.font = font;
        _cellText.fontSize = 15f;
        _cellText.color = Color.white;
        _cellText.alignment = TextAlignmentOptions.TopLeft;
        _cellText.textWrappingMode = TextWrappingModes.Normal;
        _cellText.raycastTarget = false;
        _cellBox.gameObject.SetActive(false);
    }

    const float CellPad = 8f;

    void Update()
    {
        if (_cam == null) _cam = Camera.main;
        GameObject t = null;
        NpcView npc = null;
        bool onMap = false;
        Vector2 sp = Vector2.zero;
        Vector3 wp = Vector3.zero;
        if (_cam != null && Mouse.current != null && !PointerOverUI())
        {
            sp = Mouse.current.position.ReadValue();
            wp = _cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -_cam.transform.position.z));
            wp.z = 0f;
            onMap = true;
            FindTarget(wp, out t, out npc);
        }
        UpdateTarget(t, npc);
        UpdateCell(onMap, sp, wp); // 放在后面：要读 NPC / 建筑信息框这一帧的位置
    }

    // NPC / 建筑的信息框。
    void UpdateTarget(GameObject t, NpcView npc)
    {
        if (t != _target)
        {
            _target = t;
            _npc = npc;
            _hover = 0f;
            Hide();
        }
        if (_target == null) return;

        _hover += Time.unscaledDeltaTime;
        if (_hover < delay) return;

        _refresh -= Time.unscaledDeltaTime;
        if (!_shown || _refresh <= 0f)
        {
            _refresh = 0.25f;
            string text = _npc != null ? NpcText(_npc) : BuildingText(_target);
            if (string.IsNullOrEmpty(text)) { Hide(); return; }
            SetText(text);
        }
        Place(_target);
    }

    // ============ 坐标浮窗 ============

    void UpdateCell(bool onMap, Vector2 sp, Vector3 wp)
    {
        GridManager g = GridManager.Instance;
        Vector2Int cell = g != null ? g.WorldToCell(wp) : Vector2Int.zero;
        if (!onMap || g == null || !g.InBounds(cell.x, cell.y))
        {
            _cellCur = new Vector2Int(int.MinValue, int.MinValue);
            _cellHover = 0f;
            HideCell();
            return;
        }
        if (cell != _cellCur) // 换了一格：重新计时（一定要在同一格停满半秒）
        {
            _cellCur = cell;
            _cellHover = 0f;
            HideCell();
        }
        _cellHover += Time.unscaledDeltaTime;
        if (_cellHover < cellDelay) return;

        _cellRefresh -= Time.unscaledDeltaTime;
        if (!_cellShown || _cellRefresh <= 0f)
        {
            _cellRefresh = 0.25f;
            string text = CellInfo.Describe(cell, LanguageGuard.IsJp);
            if (string.IsNullOrEmpty(text)) { HideCell(); return; }
            _cellText.text = text;
            Vector2 pref = _cellText.GetPreferredValues(text, maxWidth - CellPad * 2f, 0f);
            _cellBox.sizeDelta = new Vector2(Mathf.Min(pref.x, maxWidth - CellPad * 2f) + CellPad * 2f, pref.y + (CellPad - 2f) * 2f);
            if (!_cellShown) { _cellBox.gameObject.SetActive(true); _cellShown = true; }
        }
        PlaceCell(sp);
    }

    void HideCell()
    {
        if (!_cellShown) return;
        _cellShown = false;
        _cellBox.gameObject.SetActive(false);
    }

    // 有 NPC / 建筑信息框时：放在它下方、左边对齐；没有时：鼠标左上方。都不超出屏幕。
    void PlaceCell(Vector2 sp)
    {
        Rect r = _canvasRt.rect;
        Vector2 size = _cellBox.sizeDelta;
        Vector2 pos;
        if (_shown)
        {
            Vector2 bp = _box.anchoredPosition, bs = _box.sizeDelta;
            float bottom = bp.y - _box.pivot.y * bs.y;
            float left = bp.x - _box.pivot.x * bs.x;          // 信息框的左边
            float gap = _box.pivot.y < 0.5f ? 12f : 4f; // 信息框在物体上方时，下面挂着尖角：多空一点
            _cellBox.pivot = new Vector2(0f, 1f);              // 左上角对齐：和信息框左边对齐
            pos = new Vector2(left, bottom - gap);
            pos.x = Mathf.Clamp(pos.x, r.xMin + 4f, r.xMax - size.x - 4f);
            pos.y = Mathf.Max(pos.y, r.yMin + size.y + 4f);
        }
        else
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, sp, null, out Vector2 local)) return;
            const float off = 14f;
            bool flipX = local.x - off - size.x < r.xMin; // 左边放不下：放到鼠标右边
            bool flipY = local.y + off + size.y > r.yMax; // 上面放不下：放到鼠标下面
            _cellBox.pivot = new Vector2(flipX ? 0f : 1f, flipY ? 1f : 0f);
            pos = new Vector2(local.x + (flipX ? off : -off), local.y + (flipY ? -off : off));
        }
        _cellBox.anchoredPosition = pos;
    }

    // ============ 找悬停的对象 ============

    void FindTarget(Vector3 wp, out GameObject target, out NpcView npc)
    {
        target = null;
        npc = null;
        GameObject building = null;
        Collider2D[] hits = Physics2D.OverlapPointAll(wp);
        foreach (Collider2D h in hits)
        {
            if (h == null) continue;
            NpcView v = h.GetComponentInParent<NpcView>();
            if (v != null && !string.IsNullOrEmpty(v.npcId)) { npc = v; target = v.gameObject; return; } // NPC 优先
            if (building == null && IsBuilding(h.gameObject)) building = h.gameObject;
        }
        if (building != null) { target = building; return; }

        // 没有碰撞体的建筑：看格子占用
        GridManager g = GridManager.Instance;
        if (g == null) return;
        Vector2Int cell = g.WorldToCell(wp);
        if (!g.InBounds(cell.x, cell.y)) return;
        string occ = g.GetOccupied(cell.x, cell.y);
        if (string.IsNullOrEmpty(occ)) return;
        ConstructionSite site = ConstructionSite.At(cell);
        if (site != null) { target = site.gameObject; return; }
        BuildingFootprint fp = BuildingFootprint.At(cell); // 多格建筑：占地里任何一格都指向建筑本体
        if (fp != null) { target = fp.gameObject; return; }
        if (cell != _cell) { _cell = cell; _cellObj = FindObjectAtCell(cell); }
        if (_cellObj != null) { target = _cellObj; return; }
        _cellName = occ;
        _cellAnchor.transform.position = g.CellToWorld(cell);
        target = _cellAnchor;
    }

    static bool IsBuilding(GameObject go)
    {
        if (go.GetComponent<PowerNode>() != null || go.GetComponent<RainCollector>() != null || go.GetComponent<TerminalController>() != null) return true;
        GridManager g = GridManager.Instance;
        if (g == null) return false;
        Vector2Int c = g.WorldToCell(go.transform.position);
        return g.InBounds(c.x, c.y) && !string.IsNullOrEmpty(g.GetOccupied(c.x, c.y));
    }

    // 在格子中心附近找一个建筑的图（只在鼠标换格子时找一次）。
    static GameObject FindObjectAtCell(Vector2Int cell)
    {
        GridManager g = GridManager.Instance;
        Vector3 center = g.CellToWorld(cell);
        float r = g.cellSize * 0.45f;
        foreach (SpriteRenderer sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (sr == null || !sr.enabled) continue;
            Transform tr = sr.transform;
            if (tr.parent != null && (tr.parent.name == "GridOverlay" || tr.parent.name == "PowerBuildingTemplates")) continue;
            if (sr.GetComponentInParent<NpcView>() != null) continue;
            Vector3 p = tr.position;
            if (Mathf.Abs(p.x - center.x) > r || Mathf.Abs(p.y - center.y) > r) continue;
            return tr.parent != null && tr.parent.GetComponent<SpriteRenderer>() != null ? tr.parent.gameObject : sr.gameObject;
        }
        return null;
    }

    bool PointerOverUI()
    {
        if (EventSystem.current == null || Mouse.current == null) return false;
        PointerEventData data = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, results);
        return results.Count > 0;
    }

    // ============ 文字 ============

    static string NpcText(NpcView view)
    {
        NpcData n = FindNpc(view.npcId);
        if (n == null) return null;
        bool jp = LanguageGuard.IsJp;
        string doing;
        if (n.IsDead) doing = "<color=#FF6B6B>" + (jp ? "死亡" : "已死亡") + "</color>";
        else if (n.isBroken) doing = "<color=#C77DFF>" + (jp ? "人格崩壊" : "人格崩解") + "</color>";
        else if (!string.IsNullOrEmpty(n.currentAction)) doing = n.currentAction;
        else doing = jp ? "待機中" : "空闲";

        string s = "<b>" + n.npcName + "</b>";
        if (!string.IsNullOrEmpty(n.profession)) s += "  <size=80%><color=#9AA5B8>" + n.profession + "</color></size>";
        s += "\n" + (jp ? "行動：" : "正在：") + doing;
        if (!n.IsDead)
        {
            // 饥饿值 / 水分：100 = 不饿 / 不渴
            s += "\n<size=82%><color=#C8CFDC>" + (jp ? "生命 " : "生命 ") + Mathf.RoundToInt(n.health) +
                 (jp ? "　満腹 " : "　饥饿 ") + Mathf.RoundToInt(n.satiety) +
                 (jp ? "　水分 " : "　水分 ") + Mathf.RoundToInt(n.water) +
                 (jp ? "　体力 " : "　体力 ") + Mathf.RoundToInt(n.energy) + "</color></size>";
            // 状态词条（濒死 / 受伤 / 生病 / 饥饿 / 口渴 / 疲惫……）
            List<HealthTag> tags = NpcHealth.Tags(n);
            if (tags.Count == 0) s += "\n<size=82%>" + Good(jp ? "健康" : "健康") + "</size>";
            else
            {
                List<string> names = new List<string>();
                foreach (HealthTag t in tags) names.Add("<color=#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(NpcHealth.TagColor(t), Color.white, 0.35f)) + ">" + NpcHealth.TagName(n, t, jp) + "</color>");
                s += "\n<size=82%>" + string.Join("・", names) + "</size>";
            }
        }
        return s;
    }

    string BuildingText(GameObject go)
    {
        bool jp = LanguageGuard.IsJp;
        if (go == _cellAnchor) return "<b>" + LocalizedBuildingName(_cellName) + "</b>\n" + Grey(jp ? "設置済み" : "已建成");

        // 规划区（蓝图）：进度和施工的人
        ConstructionSite site = go.GetComponent<ConstructionSite>();
        if (site != null) return SiteText(site, jp);

        TerminalController term = go.GetComponent<TerminalController>();
        PowerNode pn = go.GetComponent<PowerNode>();
        RainCollector rc = go.GetComponent<RainCollector>();

        string name;
        if (term != null) name = jp ? "AI端末" : "AI终端机";
        else if (pn != null) name = pn.KindName;
        else if (rc != null) name = jp ? "雨水タンク" : "雨水收集器";
        else
        {
            GridManager g = GridManager.Instance;
            Vector2Int c = g != null ? g.WorldToCell(go.transform.position) : Vector2Int.zero;
            string occ = g != null && g.InBounds(c.x, c.y) ? g.GetOccupied(c.x, c.y) : null;
            name = !string.IsNullOrEmpty(occ) ? LocalizedBuildingName(occ) : go.name.Replace("(Clone)", "").Trim();
        }

        string s = "<b>" + name + "</b>\n";
        bool building = (term != null && !term.isBuilt) || (pn != null && !pn.IsBuilt) || (rc != null && !rc.built) || SpriteAlpha(go) < 0.9f;
        if (building) return s + Grey(jp ? "建設中：NPCが建てに来るのを待っている" : "施工中：等 NPC 来建");

        if (term != null)
        {
            int lv = WorldManager.Instance != null ? WorldManager.Instance.world.terminalLevel : 1;
            s += term.IsOnline() ? Good((jp ? "オンライン　" : "在线　") + ProgressionRules.TierLabel(lv)) : Bad(jp ? "オフライン" : "离线");
            if (pn != null) s += "\n" + Sub((jp ? "内蔵バッテリー " : "内置电池 ") + pn.charge.ToString("0.#") + "/" + PowerGrid.CapacityOf(pn).ToString("0"));
            return s + "\n" + Sub(jp ? "クリックで端末を開く" : "点击打开终端面板");
        }

        if (pn != null) return s + PowerStatus(pn, jp);

        if (rc != null)
        {
            s += Sub((jp ? "雨水 " : "存雨水 ") + rc.stored + "/" + WaterWorks.CollectorCapacity);
            ClimateSystem cs = ClimateSystem.Instance;
            bool raining = cs != null && (cs.Rain != PrecipLevel.None || cs.Snow != PrecipLevel.None);
            if (rc.stored >= WaterWorks.CollectorCapacity) s += "\n" + Bad(jp ? "満杯（浄水器で浄化しよう）" : "已装满（要用净化器净化）");
            else if (raining) s += "\n" + Good(jp ? "雨水を集めている" : "正在接雨水");
            else s += "\n" + Grey(jp ? "雨待ち" : "等下雨");
            return s;
        }

        return s + Grey(jp ? "設置済み" : "已建成");
    }

    static string SiteText(ConstructionSite site, bool jp)
    {
        string s = "<b>" + site.DisplayName + "</b>\n";
        int pct = Mathf.RoundToInt(site.Ratio * 100f);
        if (site.builders.Count == 0)
            return s + Grey((jp ? "計画中（進捗 " : "规划中（进度 ") + pct + "%）") + "\n" + Sub(jp ? "クリックで建てる人を割り当てる" : "点击指派施工的人");
        List<string> names = new List<string>();
        bool anyWorking = false;
        ConstructionManager cm = ConstructionManager.Instance;
        foreach (string id in site.builders)
        {
            NpcData n = FindNpc(id);
            if (n == null) continue;
            string st = cm != null ? cm.BuilderState(id, jp) : null;
            bool working = st != null && (st == "施工中" || st == "建設中");
            anyWorking |= working;
            names.Add(n.npcName + (working ? "" : "（" + st + "）"));
        }
        s += (anyWorking ? Good((jp ? "建設中 " : "施工中 ") + pct + "%") : Bad((jp ? "中断中 " : "暂停 ") + pct + "%")) + "\n";
        return s + Sub((jp ? "担当：" : "施工：") + string.Join("、", names));
    }

    static string PowerStatus(PowerNode pn, bool jp)
    {
        PowerGrid.Network net = pn.network;
        bool linked = net != null && (net.Generators > 0 || net.Batteries > 0) && net.nodes.Count > 1;
        switch (pn.kind)
        {
            case PowerNodeKind.Turbine:
            {
                float w = ClimateSystem.Instance != null ? ClimateSystem.Instance.WindPowerPerTurn : 0f;
                string st = w > 0f ? Good((jp ? "発電中 +" : "发电中 +") + w.ToString("0.#") + (jp ? "/ターン" : "/回合")) : Grey(jp ? "無風で停止中" : "没有风，停转");
                if (!linked) st += "\n" + Bad(jp ? "電線未接続：電気が使われない" : "没接电线：发的电用不上");
                return st;
            }
            case PowerNodeKind.Battery:
            {
                string st = Sub((jp ? "蓄電 " : "储电 ") + pn.charge.ToString("0.#") + "/" + PowerGrid.CapacityOf(pn).ToString("0"));
                if (net != null && net.Net > 0.01f) st += "\n" + Good(jp ? "充電中" : "充电中");
                else if (net != null && net.Net < -0.01f) st += "\n" + Bad(jp ? "放電中" : "放电中");
                if (!linked) st += "\n" + Bad(jp ? "電線未接続" : "没接电线");
                return st;
            }
            default: // 加温器 / 降温器 / 净化器
            {
                if (pn.level <= 0) return Grey(jp ? "オフ（端末の「エネルギー」で調整）" : "关闭（在终端机「能源」页调档）");
                string lv = (jp ? "段階 " : "档位 ") + pn.level;
                if (pn.kind == PowerNodeKind.Purifier)
                {
                    if (pn.powered && pn.workAmount > 0)
                        return Good((jp ? "浄化中　" : "净化中　") + lv) + "\n" + Sub((jp ? "今ターン " : "本回合净化 ") + pn.workAmount + (jp ? " 杯　消費 " : " 份　耗电 ") + PowerGrid.PurifierPower(pn.workAmount).ToString("0.#"));
                    if (WaterWorks.RawTotal <= 0) return Grey((jp ? "待機：雨水がない　" : "待机：收集器里没有雨水　") + lv);
                }
                else if (pn.powered)
                {
                    string deg = (pn.kind == PowerNodeKind.Heater ? "+" : "−") + (pn.level * PowerGrid.DegreesPerLevel) + "℃";
                    return Good((jp ? "稼働中　" : "运行中　") + lv + "　" + deg) + "\n" + Sub((jp ? "消費 " : "耗电 ") + PowerGrid.DemandOf(pn).ToString("0.#") + (jp ? "/ターン" : "/回合"));
                }
                if (!linked) return Bad(jp ? "電線未接続" : "没接电线");
                return Bad(jp ? "電力不足で停止中" : "电力不足，已断电");
            }
        }
    }

    static string Good(string s) => "<color=#74DB8C>" + s + "</color>";
    static string Bad(string s) => "<color=#FF7B7B>" + s + "</color>";
    static string Grey(string s) => "<color=#9AA5B8>" + s + "</color>";
    static string Sub(string s) => "<color=#C8CFDC>" + s + "</color>";

    static float SpriteAlpha(GameObject go)
    {
        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        return sr != null ? sr.color.a : 1f;
    }

    // 格子占用记录里存的是中文名：按建造菜单的数据换成当前语言。
    static string LocalizedBuildingName(string zhName)
    {
        BuildManager bm = BuildManager.Instance;
        if (bm != null && bm.buildables != null)
            foreach (BuildableData b in bm.buildables)
                if (b != null && b.displayName == zhName) return b.LocalizedName;
        return zhName;
    }

    static NpcData FindNpc(string id)
    {
        if (WorldManager.Instance == null || WorldManager.Instance.world == null) return null;
        foreach (NpcData n in WorldManager.Instance.world.npcs) if (n.id == id) return n;
        return null;
    }

    // ============ 显示 / 摆放 ============

    void SetText(string text)
    {
        _text.text = text;
        float inner = maxWidth - Pad * 2f;
        Vector2 pref = _text.GetPreferredValues(text, inner, 0f);
        float w = Mathf.Clamp(pref.x, MinSize - Pad * 2f, inner) + Pad * 2f;
        float h = pref.y + Pad * 2f;
        _box.sizeDelta = new Vector2(w, h);
        if (!_shown) { _box.gameObject.SetActive(true); _shown = true; }
    }

    void Hide()
    {
        if (!_shown) return;
        _shown = false;
        _box.gameObject.SetActive(false);
    }

    // 摆在物体头顶；超出屏幕就往里挪（顶上放不下就放到物体下面）。
    void Place(GameObject go)
    {
        if (!_shown || go == null || _cam == null) return;
        Vector3 top, bottom;
        SpriteRenderer sr = go == _cellAnchor ? null : go.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            Bounds b = sr.bounds;
            top = new Vector3(b.center.x, b.max.y, 0f);
            bottom = new Vector3(b.center.x, b.min.y, 0f);
        }
        else
        {
            float half = GridManager.Instance != null ? GridManager.Instance.cellSize * 0.5f : 0.5f;
            top = go.transform.position + new Vector3(0f, half, 0f);
            bottom = go.transform.position - new Vector3(0f, half, 0f);
        }

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, _cam.WorldToScreenPoint(top), null, out local)) return;
        Vector2 size = _box.sizeDelta;
        Rect r = _canvasRt.rect;
        bool below = local.y + 12f + size.y > r.yMax;
        if (below && RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, _cam.WorldToScreenPoint(bottom), null, out Vector2 lb))
            local = lb;

        _box.pivot = new Vector2(0.5f, below ? 1f : 0f);
        float x = Mathf.Clamp(local.x, r.xMin + size.x * 0.5f + 4f, r.xMax - size.x * 0.5f - 4f);
        float y = below ? local.y - 12f : local.y + 12f;
        _box.anchoredPosition = new Vector2(x, y);

        // 尖角始终指向物体（方框被挪动时尖角跟着偏）
        // 轴心固定在尖角的宽边：方框在上面时尖角朝下挂在底边；方框在下面时转 180° 立在顶边
        _tail.anchorMin = _tail.anchorMax = new Vector2(0.5f, below ? 1f : 0f);
        _tail.pivot = new Vector2(0.5f, 1f);
        _tail.localEulerAngles = new Vector3(0f, 0f, below ? 180f : 0f);
        float off = Mathf.Clamp(local.x - x, -size.x * 0.5f + 10f, size.x * 0.5f - 10f);
        _tail.anchoredPosition = new Vector2(off, 0f);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Sprite _tailSprite;
    static Sprite TailSprite()
    {
        if (_tailSprite != null) return _tailSprite;
        const int W = 14, H = 8;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                // 朝下的三角：y 越小越窄
                float half = (y + 0.5f) / H * (W * 0.5f);
                bool on = Mathf.Abs(x + 0.5f - W * 0.5f) <= half;
                t.SetPixel(x, y, on ? Color.white : new Color(1f, 1f, 1f, 0f));
            }
        t.Apply();
        _tailSprite = Sprite.Create(t, new UnityEngine.Rect(0, 0, W, H), new Vector2(0.5f, 1f), 100f);
        return _tailSprite;
    }
}
