using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================
// 左下角的「NPC 找你有事」提示（位置浮窗上面，一条一条往上叠）：
//   NPC 交来一份建造蓝图（BlueprintSystem.Propose）→ 弹出一张小卡片：左边是他的头像（四角框），
//   头像左上角一个闪烁的黄色感叹号；右边写「田中：有一份建造计划想给你看 / 营地围墙计划 · 围墙×12」。
//   出现 / 消失的动画和建筑完成提示一样：四个角从中间展开，背景板跟着角走；消失时倒着收回。
//   点卡片 → 打开和他的对话窗口（对话框里有那份计划文件），卡片收起来。
//   玩家用别的方式打开了和他的对话、或者这份计划已经批准 / 否决 / 在修改了，卡片也会自己收起来。
// 由 BlueprintSystem 自动创建。
// ============================================================================
public class BlueprintInbox : MonoBehaviour
{
    public static BlueprintInbox Instance { get; private set; }

    public float width = 330f;
    public float height = 78f;
    public float leftMargin = 18f;
    [Tooltip("最下面一张卡片的下沿离屏幕底多高（下面是位置浮窗）")]
    public float bottom = 150f;
    public float gap = 8f;
    public float expandTime = 0.22f;

    static readonly Color Green = UiStyle.Green;

    class Card
    {
        public string bpId, npcId;
        public RectTransform root, bg, content, badge;
        public RectTransform[] corners = new RectTransform[4];
        public Image badgeImg;
        public float age;
        public bool closing;
        public float closeT;
        public float y;   // 现在的高度（平滑地挪到目标位置）
    }

    RectTransform _canvasRt;
    readonly List<Card> _cards = new List<Card>();   // 新的在前（最下面）
    static Sprite _dot;

    public static BlueprintInbox Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("BlueprintInbox").AddComponent<BlueprintInbox>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        GameObject cgo = new GameObject("BlueprintInboxCanvas");
        cgo.transform.SetParent(transform, false);
        Canvas canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 74;
        CanvasScaler scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();
        _canvasRt = (RectTransform)cgo.transform;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // 新的一份计划：弹出卡片。
    public void Push(Blueprint bp)
    {
        if (bp == null) return;
        foreach (Card c in _cards) if (c.bpId == bp.id) return;
        NpcData n = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(bp.authorId) : null;
        Card card = Build(bp, n);
        card.y = bottom;
        _cards.Insert(0, card);
    }

    // 计划已经处理了（批准 / 否决 / 修改 / 看过了）：卡片收起来。
    public void Remove(string bpId)
    {
        foreach (Card c in _cards)
            if (c.bpId == bpId && !c.closing) { c.closing = true; c.closeT = 0f; }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        NpcChatWindow cw = NpcChatWindow.Instance;
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            Card c = _cards[i];
            if (c.root == null) { _cards.RemoveAt(i); continue; }
            Blueprint bp = BlueprintSystem.Find(c.bpId);
            if (!c.closing && (bp == null || bp.status != BlueprintStatus.Pending || (cw != null && cw.IsOpen && cw.CurrentNpcId == c.npcId)))
            {
                c.closing = true;
                c.closeT = 0f;
            }
            c.age += dt;
            float e;
            if (c.closing)
            {
                c.closeT += dt;
                e = 1f - Ease(c.closeT / Mathf.Max(0.01f, expandTime));
                if (c.closeT >= expandTime) { Destroy(c.root.gameObject); _cards.RemoveAt(i); continue; }
            }
            else e = Ease(c.age / Mathf.Max(0.01f, expandTime));
            Animate(c, e);
            // 感叹号：一闪一闪
            if (c.badgeImg != null)
            {
                float a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(c.age * 4f));
                c.badgeImg.color = new Color(UiStyle.Yellow.r, UiStyle.Yellow.g, UiStyle.Yellow.b, a);
            }
        }
        // 从下往上排（新的在最下面），平滑地挪过去
        float y = bottom;
        foreach (Card c in _cards)
        {
            if (c.root == null) continue;
            c.y = Mathf.Lerp(c.y, y, 1f - Mathf.Exp(-12f * dt));
            c.root.anchoredPosition = new Vector2(leftMargin + width * 0.5f, c.y + height * 0.5f);
            y += height + gap;
        }
    }

    // 四个角和背景板一起展开（e：0 = 缩在中间，1 = 完全展开）。
    void Animate(Card c, float e)
    {
        Vector2 half = new Vector2(width, height) * 0.5f * e;
        c.corners[0].anchoredPosition = new Vector2(-half.x, half.y);
        c.corners[1].anchoredPosition = new Vector2(half.x, half.y);
        c.corners[2].anchoredPosition = new Vector2(-half.x, -half.y);
        c.corners[3].anchoredPosition = new Vector2(half.x, -half.y);
        c.bg.sizeDelta = half * 2f;
        bool visible = e > 0.01f;
        if (c.bg.gameObject.activeSelf != visible) c.bg.gameObject.SetActive(visible);
        foreach (RectTransform r in c.corners) if (r.gameObject.activeSelf != visible) r.gameObject.SetActive(visible);
        if (c.badge != null && c.badge.gameObject.activeSelf != (e > 0.9f)) c.badge.gameObject.SetActive(e > 0.9f);
    }

    Card Build(Blueprint bp, NpcData n)
    {
        Card c = new Card { bpId = bp.id, npcId = bp.authorId };
        c.root = NewRect("PlanRequest", _canvasRt);
        c.root.anchorMin = c.root.anchorMax = Vector2.zero;
        c.root.pivot = new Vector2(0.5f, 0.5f);
        c.root.sizeDelta = new Vector2(width, height);

        // 背景板（点它 = 打开对话）：大小跟着四个角走，RectMask2D 让内容只露出背景板这一块
        c.bg = NewRect("Bg", c.root);
        c.bg.anchorMin = c.bg.anchorMax = c.bg.pivot = new Vector2(0.5f, 0.5f);
        Image bgImg = c.bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(UiStyle.Panel.r, UiStyle.Panel.g, UiStyle.Panel.b, 0.92f);
        c.bg.gameObject.AddComponent<RectMask2D>();
        UiStyle.Frame(c.bg, new Color(Green.r, Green.g, Green.b, 0.55f));
        Button btn = c.bg.gameObject.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.transition = Selectable.Transition.None;
        string npcId = c.npcId, bpId = c.bpId;
        btn.onClick.AddListener(() => Open(npcId, bpId));

        c.content = NewRect("Content", c.bg);
        c.content.anchorMin = c.content.anchorMax = c.content.pivot = new Vector2(0.5f, 0.5f);
        c.content.sizeDelta = new Vector2(width, height);

        // 头像：他在地图上的样子（四角框框住）
        RectTransform av = NewRect("Avatar", c.content);
        av.anchorMin = av.anchorMax = av.pivot = new Vector2(0f, 0.5f);
        av.anchoredPosition = new Vector2(12f, 0f);
        av.sizeDelta = new Vector2(54f, 54f);
        Image avBg = av.gameObject.AddComponent<Image>();
        avBg.color = new Color(0.06f, 0.1f, 0.08f, 1f);
        avBg.raycastTarget = false;
        Sprite face = NpcSprite(bp.authorId);
        RectTransform pic = NewRect("Pic", av);
        pic.anchorMin = Vector2.zero; pic.anchorMax = Vector2.one;
        pic.offsetMin = new Vector2(4f, 4f); pic.offsetMax = new Vector2(-4f, -4f);
        Image picImg = pic.gameObject.AddComponent<Image>();
        picImg.raycastTarget = false;
        if (face != null) { picImg.sprite = face; picImg.preserveAspect = true; picImg.color = Color.white; }
        else picImg.color = new Color(Green.r, Green.g, Green.b, 0.25f);
        SmallCorners(av, 54f);

        // 感叹号（头像左上角，黄色圆点 + 「!」）
        c.badge = NewRect("Badge", c.content);
        c.badge.anchorMin = c.badge.anchorMax = c.badge.pivot = new Vector2(0.5f, 0.5f);
        c.badge.anchoredPosition = new Vector2(-width * 0.5f + 12f, 27f);
        c.badge.sizeDelta = new Vector2(20f, 20f);
        c.badgeImg = c.badge.gameObject.AddComponent<Image>();
        c.badgeImg.sprite = DotSprite();
        c.badgeImg.color = UiStyle.Yellow;
        c.badgeImg.raycastTarget = false;
        TextMeshProUGUI bang = NewText(c.badge, 15f, UiStyle.Ink, TextAlignmentOptions.Center);
        bang.fontStyle = FontStyles.Bold;
        bang.text = "!";
        Fill(bang.rectTransform);

        // 文字
        TextMeshProUGUI t = NewText(c.content, 14f, UiStyle.TextMain, TextAlignmentOptions.TopLeft);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        string name = n != null ? n.npcName : bp.authorName;
        t.text = "<color=#B8FFC7>" + name + "</color>  " + ProgressionRules.L("bp_inbox_line") +
                 "\n<size=88%>" + bp.name + "</size>\n<size=80%><color=#9DB3A4>" + BlueprintSystem.ItemLine(bp) + "</color></size>";
        RectTransform tr = t.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(78f, 8f); tr.offsetMax = new Vector2(-10f, -10f);

        TMP_Text tag = UiStyle.Tag(c.content, "request : plan", Green, 11f);
        tag.transform.parent.SetAsLastSibling();

        // 四个发光的角（和 NPC 锁定框同一张贴图）
        Texture2D tex = HoverInfo.SharedCornerTexture();
        Vector2[] flips = { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) };
        for (int i = 0; i < 4; i++)
        {
            RectTransform k = NewRect("Corner" + i, c.root);
            k.anchorMin = k.anchorMax = new Vector2(0.5f, 0.5f);
            k.sizeDelta = new Vector2(HoverInfo.CornerSize, HoverInfo.CornerSize);
            k.pivot = new Vector2(HoverInfo.CornerPad / HoverInfo.CornerSize, 1f - HoverInfo.CornerPad / HoverInfo.CornerSize);
            k.localScale = new Vector3(flips[i].x, flips[i].y, 1f);
            RawImage img = k.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.color = Green;
            img.raycastTarget = false;
            c.corners[i] = k;
        }
        Animate(c, 0f);
        return c;
    }

    void Open(string npcId, string bpId)
    {
        Remove(bpId);
        NpcChatWindow.Ensure().Open(npcId);
    }

    // 头像框的四个小角。
    static void SmallCorners(RectTransform box, float size)
    {
        Texture2D tex = HoverInfo.SharedCornerTexture();
        float k = Mathf.Clamp(size / 60f, 0.4f, 1f);
        float cs = HoverInfo.CornerSize * k;
        Vector2[] flips = { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) };
        Vector2[] pos = { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
        for (int i = 0; i < 4; i++)
        {
            RectTransform r = NewRect("C" + i, box);
            r.anchorMin = r.anchorMax = pos[i];
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(cs, cs);
            r.pivot = new Vector2(HoverInfo.CornerPad / HoverInfo.CornerSize, 1f - HoverInfo.CornerPad / HoverInfo.CornerSize);
            r.localScale = new Vector3(flips[i].x, flips[i].y, 1f);
            RawImage img = r.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.color = Green;
            img.raycastTarget = false;
        }
    }

    // NPC 在地图上用的图（当头像）。
    public static Sprite NpcSprite(string npcId)
    {
        NpcMover m = string.IsNullOrEmpty(npcId) ? null : NpcView.GetMover(npcId);
        if (m == null) return null;
        SpriteRenderer sr = m.GetComponentInChildren<SpriteRenderer>();
        return sr != null ? sr.sprite : null;
    }

    static Sprite DotSprite()
    {
        if (_dot != null) return _dot;
        const int n = 32;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((1f - r) * n * 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply(false);
        return _dot = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    static TextMeshProUGUI NewText(Transform parent, float size, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = GameFonts.Find();
        if (f != null) t.font = f;
        t.fontSize = size;
        t.color = color;
        t.richText = true;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
}
