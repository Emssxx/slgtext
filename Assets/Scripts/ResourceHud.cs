using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 顶部资源栏（运行时生成，不用改场景）：顶部中间并排 电力 | 净化水 | 「终端机」按钮（天数在右上角，见 HudLayout）。
//   电力：储电 / 电池容量、本回合发电和用电、断电提示；净化水：库存、每天喝多少、还够几天、收集器里的雨水。
//   电力和水：透明底，数字用电子表段码字（SegmentText），图标 / 标题 / 数字 / 进度条都是淡绿色发光；
//   不足时（断电、电快用完 / 水不够 2 天、有人口渴）整块转成淡黄色并不规则闪烁（HudGlow）。
// 由 HudLayout 在生成天数之后调用 ResourceHud.Create。数据每半秒刷新一次（用不受暂停影响的时间）。
public class ResourceHud : MonoBehaviour
{
    public static ResourceHud Instance { get; private set; }

    const float PanelW = 300f, PanelH = 64f, Gap = 8f, TermW = 92f, TopY = -8f;
    const float GroupLeft = -(PanelW * 2f + TermW + Gap * 2f) * 0.5f; // 三块整体居中

    static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.1f, 0.85f);
    static readonly Color PowerColor = new Color(1f, 0.82f, 0.3f);
    static readonly Color WaterColor = new Color(0.42f, 0.75f, 1f);
    static readonly Color GoodColor = new Color(0.45f, 0.86f, 0.55f);
    static readonly Color BadColor = new Color(1f, 0.42f, 0.42f);
    static readonly Color SubColor = new Color(0.68f, 0.72f, 0.8f);
    static readonly Color GreenSub = new Color(0.62f, 0.78f, 0.68f); // 电力 / 水的说明小字

    TMP_FontAsset _font;
    TextMeshProUGUI _powerTitle, _powerSub, _waterTitle, _waterSub, _termTitle, _termSub;
    RectTransform _powerFill, _waterFill;
    Image _powerFillImg, _waterFillImg, _termBg;
    SegmentText _powerDigits, _waterDigits;
    HudGlow _powerGlow, _waterGlow;
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
        // 电力 | 净化水 并排（透明底、淡绿发光）
        RectTransform power = Panel(root, "Power", new Vector2(0f, 1f), new Vector2(GroupLeft, TopY), PowerColor);
        BuildInfo(power, true, out _powerGlow, out _powerTitle, out _powerDigits, out _powerFill, out _powerFillImg, out _powerSub);
        RectTransform water = Panel(root, "Water", new Vector2(0f, 1f), new Vector2(GroupLeft + PanelW + Gap, TopY), WaterColor);
        BuildInfo(water, false, out _waterGlow, out _waterTitle, out _waterDigits, out _waterFill, out _waterFillImg, out _waterSub);

        // 终端机快捷按钮：净化水右边
        RectTransform term = NewRect("Terminal", root);
        term.anchorMin = term.anchorMax = new Vector2(0.5f, 1f);
        term.pivot = new Vector2(0f, 1f);
        term.anchoredPosition = new Vector2(GroupLeft + (PanelW + Gap) * 2f, TopY);
        term.sizeDelta = new Vector2(TermW, PanelH);
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
        _termTitle = Text(term, "Title", 18f, Color.white, TextAlignmentOptions.Center, new Vector2(0f, -8f), new Vector2(TermW, 26f));
        _termTitle.fontStyle = FontStyles.Bold;
        _termSub = Text(term, "Sub", 14f, SubColor, TextAlignmentOptions.Center, new Vector2(0f, -36f), new Vector2(TermW, 22f));

        Refresh();
    }

    // 一块信息：发光图标 + 标题 + 段码数字（靠右）+ 细进度条 + 说明小字；图标 / 标题 / 数字 / 进度条归一个 HudGlow 管颜色和闪烁。
    void BuildInfo(RectTransform panel, bool bolt, out HudGlow glow, out TextMeshProUGUI title, out SegmentText digits,
                   out RectTransform fill, out Image fillImg, out TextMeshProUGUI sub)
    {
        glow = panel.gameObject.AddComponent<HudGlow>();
        RawImage icon = Icon(panel);
        glow.AddIcon(icon, GlowIcon(bolt, false), GlowIcon(bolt, true));
        title = Text(panel, "Title", 17f, HudGlow.Green, TextAlignmentOptions.Left, new Vector2(52f, -6f), new Vector2(90f, 24f));
        glow.AddGraphic(title, 0.9f);

        RectTransform drt = NewRect("Value", panel);
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0f, 1f);
        drt.anchoredPosition = new Vector2(140f, -1f);
        drt.sizeDelta = new Vector2(156f, 30f);
        RawImage dimg = drt.gameObject.AddComponent<RawImage>();
        dimg.raycastTarget = false;
        digits = drt.gameObject.AddComponent<SegmentText>();
        digits.subText = "";
        digits.subHeight = 0f;
        digits.mainHeight = 0.66f;
        digits.align = 1f;
        digits.ghostAlpha = 0.04f;
        digits.glowRadius = 5;
        glow.AddSegment(digits);

        Bar(panel, out fill, out fillImg);
        glow.AddGraphic(fillImg, 0.9f);
        sub = Text(panel, "Sub", 14f, GreenSub, TextAlignmentOptions.Left, new Vector2(52f, -40f), new Vector2(240f, 22f));
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
            _powerDigits.SetText("-");
            _powerSub.text = jp ? "発電設備がまだない" : "还没有发电设备";
            SetFill(_powerFill, 0f);
            _powerGlow.SetWarning(false);
        }
        else
        {
            _powerDigits.SetText(p.stored.ToString("0.#") + "/" + p.capacity.ToString("0"));
            float net = p.produced - p.consumed;
            string netCol = net >= 0f ? "#74DB8C" : "#FF6B6B";
            string s = (jp ? "発電 +" : "发电 +") + p.produced.ToString("0.#") + (jp ? "　消費 −" : "　用电 −") + p.consumed.ToString("0.#");
            s += "　<color=" + netCol + ">" + (net >= 0f ? "+" : "−") + Mathf.Abs(net).ToString("0.#") + "</color>";
            if (p.shed > 0) s = "<color=#FF6B6B>" + (jp ? "電力不足 " + p.shed + "台停止" : "电力不足，断电 " + p.shed + " 台") + "</color>";
            else if (p.unlinkedDevices > 0) s += jp ? "　<color=#FFB060>未接続あり</color>" : "　<color=#FFB060>有设备没接线</color>";
            _powerSub.text = s;
            SetFill(_powerFill, p.capacity > 0f ? p.stored / p.capacity : 0f);
            // 电力不足：有设备被断电，或者电池快空了而且还在亏电
            bool lowPower = p.shed > 0 || (p.capacity > 0f && p.stored < p.capacity * 0.15f && net < 0f);
            _powerGlow.SetWarning(lowPower);
        }

        // —— 净化水 ——
        int water = WaterWorks.StoredWater;
        float demand = WaterWorks.DailyDemand;
        float days = WaterWorks.DaysLeft;
        _waterTitle.text = jp ? "浄化水" : "净化水";
        _waterDigits.SetText(water.ToString());
        string daysText = float.IsInfinity(days) ? "—" : days.ToString("0.#");
        string wsub = (jp ? "1日 −" : "每天 −") + demand.ToString("0.#") + (jp ? "　残り " : "　够 ") + daysText + (jp ? " 日" : " 天");
        if (WaterWorks.CollectorCount > 0) wsub += (jp ? "　雨水 " : "　雨水 ") + WaterWorks.RawTotal + "/" + WaterWorks.RawCapacity;
        int thirsty = WaterWorks.ThirstyCount;
        if (thirsty > 0) wsub = "<color=#FF6B6B>" + (jp ? thirsty + "人が渇き" : thirsty + " 人口渴") + "</color>　" + wsub;
        _waterSub.text = wsub;
        float ratio = float.IsInfinity(days) ? (water > 0 ? 1f : 0f) : Mathf.Clamp01(days / 10f); // 够 10 天算满格
        SetFill(_waterFill, ratio);
        _waterGlow.SetWarning(days < 2f || thirsty > 0); // 水不够 2 天，或者已经有人口渴

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
        rt.sizeDelta = new Vector2(PanelW, PanelH); // 透明底：不再加边框和底板
        return rt;
    }

    RawImage Icon(RectTransform panel)
    {
        RectTransform rt = NewRect("Icon", panel);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(52f, 52f); // 贴图四周留了发光的边，实际图标约 30
        RawImage img = rt.gameObject.AddComponent<RawImage>();
        img.raycastTarget = false;
        return img;
    }

    void Bar(RectTransform panel, out RectTransform fill, out Image fillImg)
    {
        RectTransform bg = NewRect("Bar", panel);
        bg.anchorMin = bg.anchorMax = bg.pivot = new Vector2(0f, 1f);
        bg.anchoredPosition = new Vector2(52f, -33f);
        bg.sizeDelta = new Vector2(238f, 4f);
        Image bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0.72f, 1f, 0.78f, 0.13f);
        bgImg.raycastTarget = false;
        fill = NewRect("Fill", bg);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = HudGlow.Green;
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

    // 代码画的发光图标：闪电 / 水滴（淡绿 / 淡黄两套，4 张贴图全局只画一次）。
    static readonly Texture2D[] _iconTex = new Texture2D[4];
    static Texture2D GlowIcon(bool bolt, bool warn)
    {
        int idx = (bolt ? 0 : 2) + (warn ? 1 : 0);
        if (_iconTex[idx] != null) return _iconTex[idx];
        const int N = 96;                    // 贴图 96 像素，图标画在中间 56 像素，四周留给发光
        const float S = 56f / 32f, O = (N - 56) * 0.5f;
        float[] cov = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int hit = 0;                 // 4×4 超采样抗锯齿
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                        if (IconHit(bolt, new Vector2((x + (sx + 0.5f) / 4f - O) / S, (y + (sy + 0.5f) / 4f - O) / S))) hit++;
                cov[y * N + x] = hit / 16f;
            }
        Color c = warn ? HudGlow.Yellow : HudGlow.Green, g = warn ? HudGlow.YellowGlow : HudGlow.GreenGlow;
        SegmentPainter.Style st = new SegmentPainter.Style
        {
            r = c.r, g = c.g, b = c.b, gr = g.r, gg = g.g, gb = g.b,
            glow = 0.75f, glowRadius = 9f,
        };
        byte[] px = SegmentPainter.Compose(cov, null, N, N, st);
        Color32[] cols = new Color32[N * N];
        for (int k = 0; k < cols.Length; k++) cols[k] = new Color32(px[k * 4], px[k * 4 + 1], px[k * 4 + 2], px[k * 4 + 3]);
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.name = bolt ? "IconBolt" : "IconDrop";
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        t.SetPixels32(cols);
        t.Apply(false);
        return _iconTex[idx] = t;
    }

    // 图标形状（32×32 的坐标）
    static readonly Vector2[] BoltPoly = { new Vector2(18, 31), new Vector2(6, 14), new Vector2(15, 14), new Vector2(12, 1), new Vector2(26, 19), new Vector2(17, 19), new Vector2(22, 31) };
    static bool IconHit(bool bolt, Vector2 p)
    {
        if (bolt) return InPoly(p, BoltPoly);
        // 水滴：下面一个圆 + 上面一个尖角
        float dx = p.x - 16f, dy = p.y - 12f;
        bool circle = dx * dx + dy * dy <= 10f * 10f;
        bool cone = p.y >= 12f && p.y <= 30f && Mathf.Abs(dx) <= 10f * (30f - p.y) / 18f;
        return circle || cone;
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
