using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// ============================================================================
// NPC 任务栏（右下角，替换原来的 AI 调试文字）：大家是怎么分组的、每个人此刻在干什么。
//   · 识别框样式（UiStyle）：左上角标签「crew : 06」，点标签可以收起 / 展开。
//   · 第一行是领导；下面按今天的分工分组（建造组 / 采集组 / 搜寻组 / 医护组 / 休整），没分工的在「自由行动」。
//   · 每人一行：名字 + 正在做的事（NpcData.currentAction）。安全感不足的名字后面有红色「!」，领导的名字是黄色。
//   · 点名字 → 打开和他的对话窗口。
//   · 进入建造模式时自动隐藏（右下角给建造菜单让位）。
// 由 HudLayout.Start 调 Ensure() 创建。
// ============================================================================
public class NpcTaskPanel : MonoBehaviour, IPointerClickHandler
{
    public static NpcTaskPanel Instance { get; private set; }

    public float width = 380f;
    public float refreshInterval = 0.5f;
    public int maxActionChars = 18;      // "正在做的事"最多显示几个字

    GameObject _canvasGo;
    RectTransform _box, _tagRt;
    TextMeshProUGUI _text;
    TMP_Text _tag;
    bool _collapsed;
    float _refresh;

    const float NameX = 14f, ActionX = 118f;

    public static NpcTaskPanel Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("NpcTaskPanel").AddComponent<NpcTaskPanel>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Build()
    {
        _canvasGo = new GameObject("NpcTaskCanvas");
        _canvasGo.transform.SetParent(transform, false);
        Canvas canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60; // 悬停信息框（70）在它上面
        CanvasScaler scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasGo.AddComponent<GraphicRaycaster>();

        GameObject box = new GameObject("Box", typeof(RectTransform));
        box.transform.SetParent(_canvasGo.transform, false);
        _box = (RectTransform)box.transform;
        _box.anchorMin = _box.anchorMax = _box.pivot = new Vector2(1f, 0f);
        _box.anchoredPosition = new Vector2(-180f, 16f);
        _box.sizeDelta = new Vector2(width, 120f);
        Image bg = box.AddComponent<Image>();
        bg.color = new Color(UiStyle.Panel.r, UiStyle.Panel.g, UiStyle.Panel.b, 0.82f);
        bg.raycastTarget = true; // 点在面板上不要穿透到地图

        UiStyle.Frame(_box, new Color(UiStyle.Green.r, UiStyle.Green.g, UiStyle.Green.b, 0.75f));
        _tag = UiStyle.Tag(_box, "crew : 00", UiStyle.Green);
        _tagRt = (RectTransform)_tag.transform.parent;

        GameObject tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(_box, false);
        _text = tgo.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = GameFonts.Find();
        if (f != null) _text.font = f;
        _text.fontSize = 15f;
        _text.color = UiStyle.TextMain;
        _text.richText = true;
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.overflowMode = TextOverflowModes.Overflow;
        _text.raycastTarget = true;
        RectTransform tr = _text.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(0f, 8f);
        tr.offsetMax = new Vector2(-10f, -(UiStyle.TagH + 6f));
    }

    void Update()
    {
        bool hide = (BuildManager.Instance != null && BuildManager.Instance.IsBuildMode) || CountAlive() == 0;
        if (_box.gameObject.activeSelf == hide) _box.gameObject.SetActive(!hide);
        if (hide) return;

        _refresh -= Time.unscaledDeltaTime;
        if (_refresh > 0f) return;
        _refresh = refreshInterval;
        PlaceBesideBuildButton();
        Refresh();
    }

    // 放在建造按钮左边（右下角那一块），不挡按钮。
    void PlaceBesideBuildButton()
    {
        float x = -16f;
        BuildModeToggle t = FindFirstObjectByType<BuildModeToggle>();
        RectTransform rt = t != null ? t.transform as RectTransform : null;
        if (rt != null && rt.gameObject.activeInHierarchy)
        {
            Canvas c = rt.GetComponentInParent<Canvas>();
            float scale = c != null ? c.rootCanvas.transform.localScale.x : 1f;
            float mine = _canvasGo.transform.localScale.x;
            float w = rt.rect.width * (mine > 0f ? scale / mine : 1f);
            x = -(16f + w + 12f);
        }
        _box.anchoredPosition = new Vector2(x, 16f);
    }

    void Refresh()
    {
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w == null) return;
        bool jp = LanguageGuard.IsJp;
        int alive = CountAlive();
        UiStyle.SetTag(_tag, "crew : " + alive.ToString("00") + (_collapsed ? "  [+]" : "  [-]"), UiStyle.Green);

        StringBuilder sb = new StringBuilder();
        NpcData leader = NpcSociety.Leader;
        sb.Append("<color=#8E9490>").Append(L("taskpanel_leader")).Append("</color><pos=").Append(ActionX).Append(">")
          .Append(leader != null ? "<color=#FFE36A>" + Esc(leader.npcName) + "</color>" : "<color=#8E9490>—</color>").Append('\n');

        if (!_collapsed)
        {
            // 按分工分组：建造 / 采集 / 搜寻 / 医护 / 休整，最后是没分工的
            Dictionary<string, List<NpcData>> groups = new Dictionary<string, List<NpcData>>();
            List<NpcData> free = new List<NpcData>();
            foreach (NpcData n in w.npcs)
            {
                if (n == null || n.IsDead) continue;
                if (NpcSociety.HasRole(n))
                {
                    if (!groups.TryGetValue(n.role, out List<NpcData> list)) { list = new List<NpcData>(); groups[n.role] = list; }
                    list.Add(n);
                }
                else free.Add(n);
            }
            foreach (string role in NpcSociety.Roles)
                if (groups.TryGetValue(role, out List<NpcData> list)) AppendGroup(sb, NpcSociety.RoleName(role), list, leader, true);
            if (free.Count > 0) AppendGroup(sb, L("taskpanel_free"), free, leader, false);
        }

        string text = sb.ToString().TrimEnd('\n');
        _text.text = text;
        float h = Mathf.Ceil(_text.GetPreferredValues(text, width - 10f, 0f).y);
        _box.sizeDelta = new Vector2(width, h + UiStyle.TagH + 16f);
    }

    void AppendGroup(StringBuilder sb, string title, List<NpcData> list, NpcData leader, bool assigned)
    {
        sb.Append(assigned ? "<color=#B8FFC7>" : "<color=#8E9490>").Append("■ ").Append(title)
          .Append("  <size=80%>").Append(list.Count).Append("</size></color>\n");
        foreach (NpcData n in list)
        {
            string name = Esc(n.npcName);
            if (n == leader) name = "<color=#FFE36A>" + name + "</color>";
            if (n.isBroken) name = "<color=#C77DFF>" + name + "</color>";
            string mark = NpcSecurity.Insecure(n) ? "<color=#FF6A60>!</color>" : "";
            string act = string.IsNullOrEmpty(n.currentAction) ? L("taskpanel_idle") : n.currentAction;
            if (act.Length > maxActionChars) act = act.Substring(0, maxActionChars) + "…";
            sb.Append("<pos=").Append(NameX).Append("><link=\"").Append(n.id).Append("\">").Append(name).Append("</link>").Append(mark)
              .Append("<pos=").Append(ActionX).Append("><color=#C3C9C4>").Append(Esc(act)).Append("</color>\n");
        }
    }

    // 点标签：收起 / 展开；点名字：打开和他的对话窗口。
    public void OnPointerClick(PointerEventData e)
    {
        if (_tagRt != null && RectTransformUtility.RectangleContainsScreenPoint(_tagRt, e.position, null))
        {
            _collapsed = !_collapsed;
            _refresh = 0f;
            return;
        }
        if (_text == null) return;
        int link = TMP_TextUtilities.FindIntersectingLink(_text, e.position, null);
        if (link < 0) return;
        string id = _text.textInfo.linkInfo[link].GetLinkID();
        if (!string.IsNullOrEmpty(id)) NpcChatWindow.Ensure().Open(id);
    }

    static int CountAlive()
    {
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w == null) return 0;
        int k = 0;
        foreach (NpcData n in w.npcs) if (n != null && !n.IsDead) k++;
        return k;
    }

    static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹").Replace(">", "›");
    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
}
