using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================
// AI 终端机的"命令台"画面（TerminalPanelUI 用）：
//   · 一行一行的等宽绿字（淡绿微光）+ 很淡的扫描线 + 偶尔一下的屏幕闪烁，像老电脑的显示器。
//   · 菜单行（[1] 科技 ……）：鼠标移上去反色（淡绿底 + 深色字），点一下 = 按对应的数字键。
//   · 顶部状态栏（反色的一条）、底部提示符「AI@ABA:~/tech$ 」+ 闪烁的光标块、提示符上面一行反馈文字。
//   · 换页时文字像打字一样很快地刷出来；开机 / 注销时有显像管开机 / 关机的动画（一条亮线展开 / 收拢）。
// 每一行用 Spec 描述（文字、是不是菜单行、能不能选、要不要自动换行……），整页一起交给 SetPage。
// 字体用 UiStyle.Mono（等宽的 Courier Prime，中日文自动回退到游戏字体）；列对齐用 TMP 的 <pos=xx%>。
// ============================================================================
public class TerminalConsole
{
    public const float LineH = 22f;
    public const float FontSize = 17f;
    public const float StatusCol = 58f;   // 开机滚动里 [OK] 那一栏的位置（行宽的百分比）

    public static readonly Color Green = UiStyle.Green;
    public static readonly Color Bright = new Color(0.9f, 1f, 0.92f, 1f);
    public static readonly Color Dim = new Color(0.72f, 1f, 0.78f, 0.55f);
    public const string OkHex = "#74DB8C", WarnHex = "#FFE36A", BadHex = "#FF6B6B", PurpleHex = "#C77DFF", BrightHex = "#E6FFEC";

    // 一行的内容
    public class Spec
    {
        public string text = "";
        public int key = -1;          // 菜单行：数字键 0~9；-1 = 普通行
        public bool enabled = true;   // 菜单行能不能选（不能选的变暗）
        public bool wrap;             // 自动换行（长说明）
        public bool rule;             // 分隔线（自动铺满一行的 -）
        public Color color;           // alpha = 0：默认淡绿
    }

    class Line
    {
        public GameObject go;
        public RectTransform rt;
        public TextMeshProUGUI text;
        public Image hl;
        public string shown;
        public int len;   // 去掉标签后的字数（打字效果用）
    }

    public event Action<int> OnPick;   // 点了某个菜单行（参数 = 它的数字键）
    public event Action OnSkip;        // 开机时点了屏幕（跳过）

    readonly TMP_FontAsset _font;
    readonly Material _glow;
    readonly RectTransform _screen, _content, _lines, _statusBar, _cursor, _crt, _feedbackRt, _promptRt, _skip;
    readonly TextMeshProUGUI _statusL, _statusR, _prompt, _feedback, _measure, _hint;
    readonly CanvasGroup _group;
    readonly Image _crtImg;
    readonly float _w, _h;
    float _linesW, _linesH;
    readonly List<Line> _pool = new List<Line>();
    readonly List<Spec> _specs = new List<Spec>();
    readonly List<Spec> _rawPool = new List<Spec>();
    int _hover = -1, _revealTotal;
    float _reveal = -1f, _revealSpeed = 1400f;          // _reveal < 0 = 全部显示
    float _blink, _flickT = 3f, _flickLeft, _flickA = 1f, _fbLeft;
    string _path, _echo = "";
    bool _chrome = true;
    int _crtMode;                                        // 0 无，1 开机（亮线展开），2 关机（亮线收拢）
    float _crtT, _crtDur;
    Action _crtDone;
    string _ruleText;
    float _ruleW = -1f;
    bool _rendering;

    public bool CrtBusy => _crtMode != 0;
    public int Capacity => Mathf.Max(1, Mathf.FloorToInt(_linesH / LineH));   // 这一屏能放几行
    public float LinesWidth => _linesW - 8f;                                  // 一行字的可用宽度

    // parent：浮窗的 Body（左上角为原点）；x, y, w, h：屏幕区域（界面单位）
    public TerminalConsole(RectTransform parent, float x, float y, float w, float h)
    {
        _w = w; _h = h;
        _font = UiStyle.Mono;   // 等宽（Courier Prime），中日文回退到游戏字体
        if (_font != null && _font.material != null)
        {
            // 所有行共用一个带微光的材质（TMP Underlay：同色柔光，不偏移）
            _glow = new Material(_font.material);
            _glow.EnableKeyword("UNDERLAY_ON");
            _glow.SetColor("_UnderlayColor", new Color(0.45f, 1f, 0.6f, 0.3f));
            _glow.SetFloat("_UnderlayOffsetX", 0f);
            _glow.SetFloat("_UnderlayOffsetY", 0f);
            _glow.SetFloat("_UnderlayDilate", 0f);
            _glow.SetFloat("_UnderlaySoftness", 0.45f);
        }

        _screen = NewRect("Screen", parent);
        Place(_screen, x, y, w, h);
        Image bg = _screen.gameObject.AddComponent<Image>();
        bg.color = new Color(0.02f, 0.06f, 0.04f, 0.6f);   // 显示器的底色（微微发绿）
        bg.raycastTarget = false;

        _content = NewRect("Content", _screen);
        Stretch(_content, 0f, 0f);
        _group = _content.gameObject.AddComponent<CanvasGroup>();

        // 顶部状态栏（反色的一条）
        _statusBar = NewRect("StatusBar", _content);
        Place(_statusBar, 0f, 0f, w, 22f);
        Image sbi = _statusBar.gameObject.AddComponent<Image>();
        sbi.color = new Color(Green.r, Green.g, Green.b, 0.85f);
        sbi.raycastTarget = false;
        _statusL = NewText(_statusBar, 14f, UiStyle.Ink, TextAlignmentOptions.Left, false);
        Stretch(_statusL.rectTransform, 8f, 8f);
        _statusR = NewText(_statusBar, 14f, UiStyle.Ink, TextAlignmentOptions.Right, false);
        Stretch(_statusR.rectTransform, 8f, 8f);

        // 行区（超出的部分裁掉）
        _lines = NewRect("Lines", _content);
        _lines.gameObject.AddComponent<RectMask2D>();

        // 提示符上面一行：操作结果 / 错误
        _feedbackRt = NewRect("Feedback", _content);
        Place(_feedbackRt, 10f, h - 52f, w - 20f, LineH);
        _feedback = NewText(_feedbackRt, FontSize - 1f, Green, TextAlignmentOptions.Left, true);
        Stretch(_feedback.rectTransform, 4f, 4f);

        // 提示符 + 光标块
        _promptRt = NewRect("Prompt", _content);
        Place(_promptRt, 10f, h - 28f, w - 20f, LineH);
        _prompt = NewText(_promptRt, FontSize, Green, TextAlignmentOptions.Left, true);
        Stretch(_prompt.rectTransform, 4f, 4f);
        _cursor = NewRect("Cursor", _promptRt);
        _cursor.anchorMin = _cursor.anchorMax = new Vector2(0f, 0.5f);
        _cursor.pivot = new Vector2(0f, 0.5f);
        _cursor.sizeDelta = new Vector2(9f, 16f);
        Image ci = _cursor.gameObject.AddComponent<Image>();
        ci.color = Green;
        ci.raycastTarget = false;

        // 开机时右下角的「按任意键跳过」
        _hint = NewText(_content, 13f, Dim, TextAlignmentOptions.Right, false);
        Place(_hint.rectTransform, 10f, h - 24f, w - 20f, 20f);
        _hint.gameObject.SetActive(false);

        // 量字宽用（看不见）
        _measure = NewText(_content, FontSize, new Color(0f, 0f, 0f, 0f), TextAlignmentOptions.Left, false);
        Place(_measure.rectTransform, 0f, 0f, 10f, 10f);

        // 跳过区：开机时点屏幕任意处
        _skip = NewRect("SkipZone", _screen);
        Stretch(_skip, 0f, 0f);
        Image sk = _skip.gameObject.AddComponent<Image>();
        sk.color = new Color(0f, 0f, 0f, 0f);
        Button skb = _skip.gameObject.AddComponent<Button>();
        skb.transition = Selectable.Transition.None;
        skb.targetGraphic = sk;
        skb.onClick.AddListener(() => OnSkip?.Invoke());
        _skip.gameObject.SetActive(false);

        // 扫描线（盖在字上面，很淡）
        RectTransform scan = NewRect("Scanlines", _screen);
        Stretch(scan, 0f, 0f);
        RawImage si = scan.gameObject.AddComponent<RawImage>();
        si.texture = ScanTexture();
        si.uvRect = new Rect(0f, 0f, 1f, h / 3f);
        si.color = new Color(0f, 0f, 0f, 0.28f);
        si.raycastTarget = false;

        // 显像管开机 / 关机的亮线
        _crt = NewRect("CrtLine", _screen);
        _crt.anchorMin = _crt.anchorMax = _crt.pivot = new Vector2(0.5f, 0.5f);
        _crtImg = _crt.gameObject.AddComponent<Image>();
        _crtImg.raycastTarget = false;
        _crt.gameObject.SetActive(false);

        SetChrome(true);
    }

    // ============ 外部调用 ============

    // 状态栏 + 提示符 + 反馈行（开机滚动时关掉，整屏都给开机文字）。
    public void SetChrome(bool on)
    {
        _chrome = on;
        _statusBar.gameObject.SetActive(on);
        _promptRt.gameObject.SetActive(on);
        _feedbackRt.gameObject.SetActive(on);
        _hint.gameObject.SetActive(!on && _skip.gameObject.activeSelf);
        float top = on ? 30f : 8f, bottom = on ? 58f : 30f;
        _linesW = _w - 20f;
        _linesH = _h - top - bottom;
        Place(_lines, 10f, top, _linesW, _linesH);
        _ruleW = -1f;
    }

    // 开机时：点屏幕跳过 + 右下角提示。
    public void SetSkipZone(bool on, string hint = null)
    {
        _skip.gameObject.SetActive(on);
        if (hint != null) _hint.text = hint;
        _hint.gameObject.SetActive(on && !_chrome);
    }

    // 换一整页（animate = 新的一页：像打字一样刷出来；false = 只是数据刷新，不重放）。
    public void SetPage(List<Spec> specs, bool animate)
    {
        _specs.Clear();
        _specs.AddRange(specs);
        if (animate) { _hover = -1; _reveal = 0f; }
        Render();
        if (animate) _revealSpeed = Mathf.Max(1400f, _revealTotal / 0.3f);   // 一页最多 0.3 秒刷完
    }

    // 开机滚动：直接给一串文字（每帧都可能变）。
    public void SetRawLines(List<string> lines)
    {
        while (_rawPool.Count < lines.Count) _rawPool.Add(new Spec());
        _specs.Clear();
        for (int i = 0; i < lines.Count; i++)
        {
            Spec s = _rawPool[i];
            s.text = lines[i]; s.key = -1; s.enabled = true; s.wrap = false; s.rule = false; s.color = default(Color);
            _specs.Add(s);
        }
        _hover = -1;
        _reveal = -1f;
        Render();
    }

    public void ClearScreen()
    {
        _specs.Clear();
        _hover = -1;
        _reveal = -1f;
        Render();
        _feedback.text = "";
        _fbLeft = 0f;
    }

    public void SetStatus(string left, string right)
    {
        if (_statusL.text != left) _statusL.text = left;
        if (_statusR.text != right) _statusR.text = right;
    }

    // 提示符「AI@ABA:<path>$ 」（换了路径就清掉刚输入的字）。
    public void SetPrompt(string path)
    {
        if (path == _path) return;
        _path = path;
        _echo = "";
        UpdatePrompt();
    }

    // 提示符后面"输入"的字（按数字键时先显示出来，再执行）。
    public void Echo(string typed)
    {
        _echo = typed ?? "";
        UpdatePrompt();
    }

    public void Feedback(string text, Color color, float seconds)
    {
        _feedback.text = text;
        _feedback.color = color;
        _feedback.alpha = 1f;
        _fbLeft = seconds;
    }

    // 显像管开机：中间一条亮线横向拉开，再上下展开成整屏，字跟着亮起来。
    public void CrtOn()
    {
        _crtMode = 1; _crtT = 0f; _crtDur = 0.34f; _crtDone = null;
        _crt.gameObject.SetActive(true);
        _crt.sizeDelta = new Vector2(4f, 2f);
    }

    // 显像管关机：整屏压成一条亮线，再缩成一个点；播完调 done。
    public void CrtOff(Action done)
    {
        _crtMode = 2; _crtT = 0f; _crtDur = 0.3f; _crtDone = done;
        _crt.gameObject.SetActive(true);
    }

    // 取消显像管动画（面板被直接关掉时：别让关机动画的回调留到下次打开再执行）。
    public void CancelCrt()
    {
        _crtMode = 0;
        _crtDone = null;
        _crt.gameObject.SetActive(false);
    }

    // 一段文字的宽度（界面单位，含行尾空格）。
    public float Measure(string s)
    {
        string t = StripTags(s ?? "");
        return _measure.GetPreferredValues(t + "|").x - _measure.GetPreferredValues("|").x;
    }

    public void Tick(float dt)
    {
        // 光标块闪烁
        _blink += dt;
        bool on = _chrome && _crtMode == 0 && Mathf.Repeat(_blink, 1f) < 0.55f;
        if (_cursor.gameObject.activeSelf != on) _cursor.gameObject.SetActive(on);

        // 打字效果（显像管动画播完才开始）
        if (_reveal >= 0f && _crtMode == 0)
        {
            _reveal += dt * _revealSpeed;
            if (_reveal >= _revealTotal) _reveal = -1f;
            ApplyReveal();
        }

        // 反馈行：到时间淡出
        if (_fbLeft > 0f)
        {
            _fbLeft -= dt;
            _feedback.alpha = Mathf.Clamp01(_fbLeft / 0.6f);
            if (_fbLeft <= 0f) _feedback.text = "";
        }

        // 偶尔闪一下（很短、很轻）
        _flickT -= dt;
        float flick = 1f;
        if (_flickLeft > 0f) { _flickLeft -= dt; flick = _flickA; }
        else if (_flickT <= 0f)
        {
            _flickT = UnityEngine.Random.Range(2.5f, 7f);
            _flickLeft = UnityEngine.Random.Range(0.03f, 0.08f);
            _flickA = UnityEngine.Random.Range(0.8f, 0.92f);
        }
        _group.alpha = flick * TickCrt(dt);
    }

    // ============ 画 ============

    void Render()
    {
        _rendering = true;
        float y = 0f;
        for (int i = 0; i < _specs.Count; i++)
        {
            Spec s = _specs[i];
            Line L = GetLine(i);
            if (!L.go.activeSelf) L.go.SetActive(true);
            bool menu = s.key >= 0;
            bool hov = menu && i == _hover;
            string txt = s.rule ? RuleText() : (s.text ?? "");
            if (hov) txt = StripColors(txt);   // 反色时去掉行里的颜色，统一深色字
            Color col = s.color.a > 0f ? s.color : Green;
            if (menu && !s.enabled) col = Dim;
            L.text.color = hov ? UiStyle.Ink : col;
            L.hl.color = hov ? new Color(Green.r, Green.g, Green.b, 0.88f) : new Color(0f, 0f, 0f, 0f);
            L.hl.raycastTarget = menu;
            L.text.textWrappingMode = s.wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            L.text.alignment = s.wrap ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
            if (L.shown != txt) { L.text.text = txt; L.shown = txt; L.len = StripTags(txt).Length; }
            float h = LineH;
            if (s.wrap) h = Mathf.Max(LineH, Mathf.Ceil(L.text.GetPreferredValues(txt, LinesWidth, 0f).y) + 4f);
            L.rt.anchoredPosition = new Vector2(0f, -y);
            L.rt.sizeDelta = new Vector2(_linesW, h);
            y += h;
        }
        for (int i = _specs.Count; i < _pool.Count; i++)
            if (_pool[i].go.activeSelf) _pool[i].go.SetActive(false);
        ApplyReveal();
        _rendering = false;
    }

    void ApplyReveal()
    {
        int acc = 0;
        for (int i = 0; i < _specs.Count && i < _pool.Count; i++)
        {
            Line L = _pool[i];
            int vis = _reveal < 0f ? 99999 : Mathf.Clamp(Mathf.FloorToInt(_reveal) - acc, 0, 99999);
            if (L.text.maxVisibleCharacters != vis) L.text.maxVisibleCharacters = vis;
            acc += L.len + 3;   // 行和行之间稍微停一下
        }
        _revealTotal = acc;
    }

    Line GetLine(int i)
    {
        while (_pool.Count <= i)
        {
            int idx = _pool.Count;
            Line L = new Line();
            L.rt = NewRect("Line" + idx, _lines);
            L.rt.anchorMin = L.rt.anchorMax = L.rt.pivot = new Vector2(0f, 1f);
            L.go = L.rt.gameObject;
            L.hl = L.go.AddComponent<Image>();   // 反色的底（平时透明），也是鼠标的点击区域
            L.hl.color = new Color(0f, 0f, 0f, 0f);
            Button b = L.go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = L.hl;
            b.onClick.AddListener(() => Click(idx));
            UiHoverZone hz = L.go.AddComponent<UiHoverZone>();
            hz.onEnter = () => SetHover(idx);
            hz.onExit = () => { if (_hover == idx) SetHover(-1); };
            L.text = NewText(L.rt, FontSize, Green, TextAlignmentOptions.Left, true);
            Stretch(L.text.rectTransform, 4f, 4f);
            _pool.Add(L);
        }
        return _pool[i];
    }

    void Click(int idx)
    {
        if (idx < 0 || idx >= _specs.Count || _specs[idx].key < 0) return;
        OnPick?.Invoke(_specs[idx].key);
    }

    void SetHover(int idx)
    {
        if (_hover == idx) return;
        _hover = idx;
        // 正在画（隐藏多余的行会触发移出）或者面板已经关了：只记下来，不重画
        if (_rendering || !_screen.gameObject.activeInHierarchy) return;
        Render();
    }

    void UpdatePrompt()
    {
        string text = "AI@ABA:" + (_path ?? "~") + "$ " + _echo;
        _prompt.text = text;
        _cursor.anchoredPosition = new Vector2(4f + Measure(text) + 1f, 0f);
    }

    // 显像管动画；返回字的透明度（动画中 0，开机后半段慢慢亮起来）。
    float TickCrt(float dt)
    {
        if (_crtMode == 0) return 1f;
        _crtT += dt;
        float p = Mathf.Clamp01(_crtT / Mathf.Max(0.01f, _crtDur));
        float w, h, a, content;
        if (_crtMode == 1)
        {
            if (p < 0.45f) { float k = Ease(p / 0.45f); w = Mathf.Lerp(4f, _w, k); h = 2f; a = 1f; content = 0f; }
            else { float k = Ease((p - 0.45f) / 0.55f); w = _w; h = Mathf.Lerp(2f, _h, k); a = 1f - k; content = k; }
        }
        else
        {
            if (p < 0.5f) { float k = Ease(p / 0.5f); w = _w; h = Mathf.Lerp(_h, 2f, k); a = Mathf.Lerp(0.15f, 1f, k); }
            else { float k = Ease((p - 0.5f) / 0.5f); w = Mathf.Lerp(_w, 2f, k); h = 2f; a = 1f - k * 0.3f; }
            content = 0f;
        }
        _crt.sizeDelta = new Vector2(w, h);
        _crtImg.color = new Color(0.85f, 1f, 0.9f, a);
        if (p < 1f) return content;

        int mode = _crtMode;
        _crtMode = 0;
        _crt.gameObject.SetActive(false);
        if (mode == 2)
        {
            Action cb = _crtDone;
            _crtDone = null;
            cb?.Invoke();
            return 0f;
        }
        return 1f;
    }

    // 分隔线：一行 -，铺满宽度（量一次 - 的宽度）。
    string RuleText()
    {
        if (_ruleText != null && Mathf.Abs(_ruleW - _linesW) < 0.5f) return _ruleText;
        float dash = _measure.GetPreferredValues("----------").x / 10f;
        int n = dash > 0.5f ? Mathf.FloorToInt((LinesWidth - 4f) / dash) : 80;
        _ruleText = "<alpha=#55>" + new string('-', Mathf.Clamp(n, 4, 220));
        if (dash > 0.5f) _ruleW = _linesW;   // 量不出来（还没激活）就下次再量
        return _ruleText;
    }

    // ============ 小工具 ============

    static readonly Regex TagRx = new Regex("<[^>]*>");
    static readonly Regex ColorRx = new Regex("</?color[^>]*>");
    public static string StripTags(string s) => TagRx.Replace(s, "");
    static string StripColors(string s) => ColorRx.Replace(s, "");
    static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }

    static Texture2D _scanTex;
    static Texture2D ScanTexture()
    {
        if (_scanTex != null) return _scanTex;
        Texture2D t = new Texture2D(1, 3, TextureFormat.RGBA32, false);
        t.name = "TerminalScanlines";
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Repeat;
        t.SetPixels32(new Color32[] { new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 255) });
        t.Apply(false);
        return _scanTex = t;
    }

    TextMeshProUGUI NewText(Transform parent, float size, Color c, TextAlignmentOptions align, bool glow)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        if (glow && _glow != null) t.fontSharedMaterial = _glow;   // 先设字体再设材质（设字体会重置材质）
        t.fontSize = size;
        t.color = c;
        t.alignment = align;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // 左上角为原点放一个框。
    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void Stretch(RectTransform rt, float left, float right)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, 0f);
        rt.offsetMax = new Vector2(-right, 0f);
    }
}

// ============================================================================
// 开机滚动：一行一行播开机脚本（文字来自本地化表 term_boot_01、02……，可以直接改那里）。
// 行里可以写这些指令：
//   {dots}        点点点一直铺到状态那一栏，后面的字（[OK] 之类）对齐在同一列
//   {count:4096}  数字从 0 数到 4096（补零）
//   {bar}         进度条 [####....] 从 0% 到 100%
//   {pw:8}        一个一个打出 8 个 *
//   {wait:0.5}    停 0.5 秒
//   {hex:12}      （单独一行）一口气刷 12 行内存数据，屏幕往上滚
//   {users} {alive} {turbines} {slots} {tier} {day}：换成当前的数字
// [OK] [ON] [LOCKED] 绿色，[WARN] 黄色，[FAIL] [NO CARRIER] [OFF] 红色，ACCESS GRANTED 加亮。
// ============================================================================
public class TerminalBoot
{
    enum K { Text, Dots, Count, Bar, Pw, Wait }
    class Seg { public K k; public string s = ""; public float f; public int n; }
    class BLine { public readonly List<Seg> segs = new List<Seg>(); public int hex; public int dots = -1; }

    static readonly Regex Directive = new Regex(@"\{(dots|count|bar|pw|wait|hex)(?::([0-9.]+))?\}");
    static readonly Regex Var = new Regex(@"\{(\w+)\}");
    static readonly Regex OkTok = new Regex(@"\[(OK|ON|LOCKED|READY)\]");
    static readonly Regex WarnTok = new Regex(@"\[(WARN|SKIP)\]");
    static readonly Regex BadTok = new Regex(@"\[(FAIL|OFF|LOST|NO CARRIER|ERROR)\]");

    readonly TerminalConsole _con;
    readonly List<BLine> _script = new List<BLine>();
    readonly List<string> _out = new List<string>();    // 已经打完的行
    readonly List<string> _view = new List<string>();
    readonly System.Random _rng = new System.Random(2071);
    int _line, _seg, _hexDone, _addr = 0x7C00;
    float _t, _delay;

    public bool Finished { get; private set; }

    public TerminalBoot(TerminalConsole con, IList<string> lines, Dictionary<string, string> vars)
    {
        _con = con;
        if (lines != null) foreach (string raw in lines) _script.Add(Parse(raw, vars));
        if (_script.Count == 0) Finished = true;
    }

    public void Tick(float dt)
    {
        float budget = dt;
        for (int guard = 0; guard < 200 && budget > 0f && !Finished; guard++)
        {
            if (_delay > 0f)
            {
                float use = Mathf.Min(_delay, budget);
                _delay -= use;
                budget -= use;
                continue;
            }
            if (_line >= _script.Count) { Finished = true; break; }
            BLine L = _script[_line];
            if (L.hex > 0)
            {
                // 内存数据：每 0.022 秒一行，刷得飞快
                _t += budget;
                budget = 0f;
                while (_t >= 0.022f && _hexDone < L.hex) { _t -= 0.022f; _out.Add(HexRow()); _hexDone++; }
                if (_hexDone >= L.hex) Next(0.06f);
                break;
            }
            if (_seg >= L.segs.Count)
            {
                _out.Add(Build(L, L.segs.Count, 1f));
                Next(0.03f + (float)_rng.NextDouble() * 0.06f);   // 行和行之间停一下（不规则）
                continue;
            }
            float need = Duration(L.segs[_seg]) - _t;
            if (budget >= need) { budget -= need; _t = 0f; _seg++; }
            else { _t += budget; budget = 0f; }
        }
        if (!Finished && _line >= _script.Count && _delay <= 0f) Finished = true;
    }

    // 屏幕上要显示的行：最后 capacity 行（打到一半的那行也算，后面跟一个光标）。
    public List<string> Visible(int capacity)
    {
        _view.Clear();
        string cur = null;
        if (!Finished && _line < _script.Count && _script[_line].hex == 0 && _delay <= 0f)
        {
            BLine L = _script[_line];
            float p = _seg < L.segs.Count ? _t / Mathf.Max(0.0001f, Duration(L.segs[_seg])) : 1f;
            cur = Build(L, _seg, p) + "<alpha=#AA>_";
        }
        int total = _out.Count + (cur != null ? 1 : 0);
        int start = Mathf.Max(0, total - capacity);
        for (int i = start; i < _out.Count; i++) _view.Add(_out[i]);
        if (cur != null) _view.Add(cur);
        return _view;
    }

    void Next(float delay)
    {
        _line++;
        _seg = 0;
        _t = 0f;
        _hexDone = 0;
        _delay = delay;
    }

    static float Duration(Seg s)
    {
        switch (s.k)
        {
            case K.Text: return s.s.Length / 900f;
            case K.Dots: return 0.1f;
            case K.Count: return 0.5f;
            case K.Bar: return 0.85f;
            case K.Pw: return s.n * 0.07f;
            default: return s.f;
        }
    }

    // 拼出一行：upto 之前的段完整，第 upto 段播到 p（0~1）。
    string Build(BLine L, int upto, float p)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < L.segs.Count && i <= upto; i++)
        {
            Seg s = L.segs[i];
            float q = i < upto ? 1f : Mathf.Clamp01(p);
            switch (s.k)
            {
                case K.Text:
                    sb.Append(q >= 1f ? s.s : s.s.Substring(0, Mathf.Clamp(Mathf.FloorToInt(s.s.Length * q), 0, s.s.Length)));
                    break;
                case K.Dots:
                    if (L.dots < 0) L.dots = DotsFor(sb.ToString());
                    sb.Append('.', Mathf.FloorToInt(L.dots * q));
                    if (q >= 1f) sb.Append("<pos=").Append(TerminalConsole.StatusCol.ToString("0", CultureInfo.InvariantCulture)).Append("%>");
                    break;
                case K.Count:
                    string target = s.n.ToString(CultureInfo.InvariantCulture);
                    sb.Append(Mathf.RoundToInt(s.n * q).ToString(CultureInfo.InvariantCulture).PadLeft(target.Length, '0'));
                    break;
                case K.Bar:
                    int c = Mathf.RoundToInt(20f * q);
                    sb.Append('[').Append('#', c).Append('.', 20 - c).Append("] ")
                      .Append(Mathf.RoundToInt(100f * q).ToString(CultureInfo.InvariantCulture).PadLeft(3)).Append('%');
                    break;
                case K.Pw:
                    sb.Append('*', Mathf.FloorToInt(s.n * q));
                    break;
            }
        }
        return Colorize(sb.ToString());
    }

    // 点点点要铺多少个：从这行已有的字一直到状态栏那一列。
    int DotsFor(string prefix)
    {
        float col = _con.LinesWidth * TerminalConsole.StatusCol / 100f;
        float dot = _con.Measure("..........") / 10f;
        if (dot <= 0.5f) return 8;
        return Mathf.Clamp(Mathf.FloorToInt((col - _con.Measure(prefix) - 10f) / dot), 2, 160);
    }

    // 一行假的内存数据：地址 + 16 个字节 + 对应的字符。
    string HexRow()
    {
        StringBuilder sb = new StringBuilder("<alpha=#88>  ");
        sb.Append(_addr.ToString("X8")).Append("  ");
        char[] asc = new char[16];
        for (int i = 0; i < 16; i++)
        {
            int b = _rng.Next(256);
            sb.Append(b.ToString("X2")).Append(i == 7 ? "  " : " ");
            asc[i] = b >= 0x21 && b < 0x7F && b != '<' && b != '>' ? (char)b : '.';
        }
        sb.Append(" |").Append(asc).Append('|');
        _addr += 16;
        return sb.ToString();
    }

    static string Colorize(string s)
    {
        s = OkTok.Replace(s, "<color=" + TerminalConsole.OkHex + ">[$1]</color>");
        s = WarnTok.Replace(s, "<color=" + TerminalConsole.WarnHex + ">[$1]</color>");
        s = BadTok.Replace(s, "<color=" + TerminalConsole.BadHex + ">[$1]</color>");
        return s.Replace("ACCESS GRANTED", "<color=" + TerminalConsole.BrightHex + "><b>ACCESS GRANTED</b></color>");
    }

    static BLine Parse(string raw, Dictionary<string, string> vars)
    {
        // 先把 {users} 之类换成数字（指令名不在 vars 里，保持原样）
        string s = Var.Replace(raw ?? "", m => vars != null && vars.TryGetValue(m.Groups[1].Value, out string v) ? v : m.Value);
        BLine L = new BLine();
        int pos = 0;
        foreach (Match m in Directive.Matches(s))
        {
            if (m.Index > pos) L.segs.Add(new Seg { k = K.Text, s = s.Substring(pos, m.Index - pos) });
            string name = m.Groups[1].Value;
            float f = 0f;
            if (m.Groups[2].Success) float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
            switch (name)
            {
                case "dots": L.segs.Add(new Seg { k = K.Dots }); break;
                case "count": L.segs.Add(new Seg { k = K.Count, n = Mathf.Max(1, (int)f) }); break;
                case "bar": L.segs.Add(new Seg { k = K.Bar }); break;
                case "pw": L.segs.Add(new Seg { k = K.Pw, n = Mathf.Max(1, (int)f) }); break;
                case "wait": L.segs.Add(new Seg { k = K.Wait, f = f }); break;
                case "hex": L.hex = Mathf.Max(1, (int)f); break;
            }
            pos = m.Index + m.Length;
        }
        if (pos < s.Length) L.segs.Add(new Seg { k = K.Text, s = s.Substring(pos) });
        return L;
    }
}
