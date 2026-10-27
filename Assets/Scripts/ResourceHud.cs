using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 顶部资源栏（运行时生成，不用改场景）：
//   天数左边：电力（储电 / 电池容量、本回合发电和用电、断电提示）
//   天数右边：净化水（库存、每天喝多少、还够几天、收集器里的雨水），再往右是「终端机」快捷按钮。
// 由 HudLayout 在生成天数之后调用 ResourceHud.Create。数据每半秒刷新一次（用不受暂停影响的时间）。
public class ResourceHud : MonoBehaviour
{
    public static ResourceHud Instance { get; private set; }

    const float PanelW = 300f, PanelH = 64f, Gap = 8f, DayHalf = 48f, TopY = -8f;

    static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.1f, 0.85f);
    static readonly Color PowerColor = new Color(1f, 0.82f, 0.3f);
    static readonly Color WaterColor = new Color(0.42f, 0.75f, 1f);
    static readonly Color GoodColor = new Color(0.45f, 0.86f, 0.55f);
    static readonly Color BadColor = new Color(1f, 0.42f, 0.42f);
    static readonly Color SubColor = new Color(0.68f, 0.72f, 0.8f);

    TMP_FontAsset _font;
    TextMeshProUGUI _powerTitle, _powerValue, _powerSub, _waterTitle, _waterValue, _waterSub, _termTitle, _termSub;
    RectTransform _powerFill, _waterFill;
    Image _powerFillImg, _waterFillImg, _termBg;
    float _timer;

    // root = 主界面 Canvas。
    public static ResourceHud Create(Transform root, TMP_FontAsset font)
    {
        if (Instance != null || root == null) return Instance;
        RectTransform rt = NewRect("HudResources", root);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        ResourceHud hud = rt.gameObject.AddComponent<ResourceHud>();
        hud._font = font;
        hud.Build(rt);
        return hud;
    }

    void Awake() { if (Instance == null) Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Build(RectTransform root)
    {
        // 电力：天数左边
        RectTransform power = Panel(root, "Power", new Vector2(1f, 1f), new Vector2(-(DayHalf + Gap), TopY), PowerColor);
        Icon(power, IconSprite(true), PowerColor);
        _powerTitle = Text(power, "Title", 17f, SubColor, TextAlignmentOptions.Left, new Vector2(52f, -6f), new Vector2(110f, 24f));
        _powerValue = Text(power, "Value", 22f, Color.white, TextAlignmentOptions.Right, new Vector2(150f, -4f), new Vector2(140f, 26f));
        _powerValue.fontStyle = FontStyles.Bold;
        Bar(power, PowerColor, out _powerFill, out _powerFillImg);
        _powerSub = Text(power, "Sub", 14f, SubColor, TextAlignmentOptions.Left, new Vector2(52f, -40f), new Vector2(240f, 22f));

        // 净化水：天数右边
        RectTransform water = Panel(root, "Water", new Vector2(0f, 1f), new Vector2(DayHalf + Gap, TopY), WaterColor);
        Icon(water, IconSprite(false), WaterColor);
        _waterTitle = Text(water, "Title", 17f, SubColor, TextAlignmentOptions.Left, new Vector2(52f, -6f), new Vector2(110f, 24f));
        _waterValue = Text(water, "Value", 22f, Color.white, TextAlignmentOptions.Right, new Vector2(150f, -4f), new Vector2(140f, 26f));
        _waterValue.fontStyle = FontStyles.Bold;
        Bar(water, WaterColor, out _waterFill, out _waterFillImg);
        _waterSub = Text(water, "Sub", 14f, SubColor, TextAlignmentOptions.Left, new Vector2(52f, -40f), new Vector2(240f, 22f));

        // 终端机快捷按钮：净化水右边
        RectTransform term = NewRect("Terminal", root);
        term.anchorMin = term.anchorMax = new Vector2(0.5f, 1f);
        term.pivot = new Vector2(0f, 1f);
        term.anchoredPosition = new Vector2(DayHalf + Gap + PanelW + Gap, TopY);
        term.sizeDelta = new Vector2(92f, PanelH);
        Image border = term.gameObject.AddComponent<Image>();
        border.color = new Color(0.49f, 0.36f, 1f, 0.6f);
        RectTransform inner = NewRect("Bg", term);
        Stretch(inner, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        _termBg = inner.gameObject.AddComponent<Image>();
        _termBg.color = PanelBg;
        _termBg.raycastTarget = false;
        Button b = term.gameObject.AddComponent<Button>();
        b.targetGraphic = border;
        b.onClick.AddListener(OnTerminalClicked);
        _termTitle = Text(term, "Title", 18f, Color.white, TextAlignmentOptions.Center, new Vector2(0f, -8f), new Vector2(92f, 26f));
        _termTitle.fontStyle = FontStyles.Bold;
        _termSub = Text(term, "Sub", 14f, SubColor, TextAlignmentOptions.Center, new Vector2(0f, -36f), new Vector2(92f, 22f));

        Refresh();
    }

    void Update()
    {
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = 0.5f;
        Refresh();
    }

    void Refresh()
    {
        bool jp = LanguageGuard.IsJp;

        // —— 电力 ——
        PowerGrid.Summary p = PowerGrid.Total();
        _powerTitle.text = jp ? "電力" : "电力";
        if (p.capacity <= 0f && p.produced <= 0f)
        {
            _powerValue.text = "—";
            _powerSub.text = jp ? "発電設備がまだない" : "还没有发电设备";
            SetFill(_powerFill, 0f);
        }
        else
        {
            _powerValue.text = p.stored.ToString("0.#") + "<size=70%><color=#A8B0C0> / " + p.capacity.ToString("0") + "</color></size>";
            float net = p.produced - p.consumed;
            string netCol = net >= 0f ? "#74DB8C" : "#FF6B6B";
            string s = (jp ? "発電 +" : "发电 +") + p.produced.ToString("0.#") + (jp ? "　消費 −" : "　用电 −") + p.consumed.ToString("0.#");
            s += "　<color=" + netCol + ">" + (net >= 0f ? "+" : "−") + Mathf.Abs(net).ToString("0.#") + "</color>";
            if (p.shed > 0) s = "<color=#FF6B6B>" + (jp ? "電力不足 " + p.shed + "台停止" : "电力不足，断电 " + p.shed + " 台") + "</color>";
            else if (p.unlinkedDevices > 0) s += jp ? "　<color=#FFB060>未接続あり</color>" : "　<color=#FFB060>有设备没接线</color>";
            _powerSub.text = s;
            SetFill(_powerFill, p.capacity > 0f ? p.stored / p.capacity : 0f);
            _powerFillImg.color = p.shed > 0 ? BadColor : PowerColor;
        }

        // —— 净化水 ——
        int water = WaterWorks.StoredWater;
        float demand = WaterWorks.DailyDemand;
        float days = WaterWorks.DaysLeft;
        _waterTitle.text = jp ? "浄化水" : "净化水";
        _waterValue.text = water.ToString();
        string daysText = float.IsInfinity(days) ? "—" : days.ToString("0.#");
        string wsub = (jp ? "1日 −" : "每天 −") + demand.ToString("0.#") + (jp ? "　残り " : "　够 ") + daysText + (jp ? " 日" : " 天");
        if (WaterWorks.CollectorCount > 0) wsub += (jp ? "　雨水 " : "　雨水 ") + WaterWorks.RawTotal + "/" + WaterWorks.RawCapacity;
        int thirsty = WaterWorks.ThirstyCount;
        if (thirsty > 0) wsub = "<color=#FF6B6B>" + (jp ? thirsty + "人が渇き" : thirsty + " 人口渴") + "</color>　" + wsub;
        _waterSub.text = wsub;
        float ratio = float.IsInfinity(days) ? (water > 0 ? 1f : 0f) : Mathf.Clamp01(days / 10f); // 够 10 天算满格
        SetFill(_waterFill, ratio);
        _waterFillImg.color = days < 2f ? BadColor : WaterColor;
        _waterValue.color = days < 2f ? BadColor : Color.white;

        // —— 终端机 ——
        TerminalController t = TerminalController.Instance;
        _termTitle.text = jp ? "AI端末" : "终端机";
        if (t == null) { _termSub.text = jp ? "未建設" : "未建造"; _termTitle.color = SubColor; }
        else if (!t.isBuilt) { _termSub.text = jp ? "建設中" : "施工中"; _termTitle.color = SubColor; }
        else
        {
            int lv = WorldManager.Instance != null ? WorldManager.Instance.world.terminalLevel : 1;
            _termSub.text = "Lv" + lv;
            _termTitle.color = Color.white;
        }
    }

    // 点「终端机」：建好了就打开终端面板；没建 / 施工中就告诉玩家怎么办。
    void OnTerminalClicked()
    {
        TerminalController t = TerminalController.Instance;
        string msg = null;
        if (t == null)
            msg = LanguageGuard.Pick("还没有 AI终端机：点右下角「建造」→「基础」→「AI终端机」，放下后退出建造模式，NPC 会去施工。",
                                     "AI端末がまだない：右下の「建築」→「基礎」→「AI端末」を置き、建築モードを出るとNPCが建てに行く。");
        else if (!t.isBuilt)
            msg = LanguageGuard.Pick("AI终端机还在施工，等 NPC 建好再打开。", "AI端末はまだ建設中。NPCが建て終わるまで待とう。");
        else if (TerminalPanelUI.Instance != null) TerminalPanelUI.Instance.Open();
        else msg = LanguageGuard.Pick("场景里没有终端面板（TerminalPanelUI），打不开。", "シーンに端末パネル（TerminalPanelUI）がない。");
        if (msg != null && EventLogManager.Instance != null && WorldManager.Instance != null)
            EventLogManager.Instance.AddEvent(WorldManager.Instance.world.day, msg);
    }

    // ============ 小工具 ============

    RectTransform Panel(RectTransform root, string name, Vector2 pivot, Vector2 pos, Color accent)
    {
        RectTransform rt = NewRect(name, root);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(PanelW, PanelH);
        Image border = rt.gameObject.AddComponent<Image>();
        border.color = new Color(accent.r, accent.g, accent.b, 0.5f);
        border.raycastTarget = false;
        RectTransform inner = NewRect("Bg", rt);
        Stretch(inner, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        Image bg = inner.gameObject.AddComponent<Image>();
        bg.color = PanelBg;
        bg.raycastTarget = false;
        return rt;
    }

    void Icon(RectTransform panel, Sprite sprite, Color c)
    {
        RectTransform rt = NewRect("Icon", panel);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(8f, 0f);
        rt.sizeDelta = new Vector2(38f, 38f);
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = c;
        img.raycastTarget = false;
    }

    void Bar(RectTransform panel, Color c, out RectTransform fill, out Image fillImg)
    {
        RectTransform bg = NewRect("Bar", panel);
        bg.anchorMin = bg.anchorMax = bg.pivot = new Vector2(0f, 1f);
        bg.anchoredPosition = new Vector2(52f, -31f);
        bg.sizeDelta = new Vector2(238f, 6f);
        Image bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(1f, 1f, 1f, 0.12f);
        bgImg.raycastTarget = false;
        fill = NewRect("Fill", bg);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = c;
        fillImg.raycastTarget = false;
    }

    static void SetFill(RectTransform fill, float ratio)
    {
        if (fill == null) return;
        fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
    }

    TextMeshProUGUI Text(RectTransform parent, string name, float size, Color color, TextAlignmentOptions align, Vector2 pos, Vector2 sizeDelta)
    {
        RectTransform rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    // 代码画的图标（白色，用 Image.color 上色）：闪电 / 水滴。
    static Sprite _bolt, _drop;
    static Sprite IconSprite(bool bolt)
    {
        if (bolt && _bolt != null) return _bolt;
        if (!bolt && _drop != null) return _drop;
        const int N = 32;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        Vector2[] boltPoly = { new Vector2(18, 31), new Vector2(6, 14), new Vector2(15, 14), new Vector2(12, 1), new Vector2(26, 19), new Vector2(17, 19), new Vector2(22, 31) };
        for (int x = 0; x < N; x++)
            for (int y = 0; y < N; y++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                bool on;
                if (bolt) on = InPoly(p, boltPoly);
                else
                {
                    // 水滴：下面一个圆 + 上面一个尖角
                    float dx = p.x - 16f, dy = p.y - 12f;
                    bool circle = dx * dx + dy * dy <= 10f * 10f;
                    bool cone = p.y >= 12f && p.y <= 30f && Mathf.Abs(dx) <= 10f * (30f - p.y) / 18f;
                    on = circle || cone;
                }
                t.SetPixel(x, y, on ? Color.white : new Color(1f, 1f, 1f, 0f));
            }
        t.Apply();
        Sprite s = Sprite.Create(t, new UnityEngine.Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 32f);
        if (bolt) _bolt = s; else _drop = s;
        return s;
    }

    static bool InPoly(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }
}
