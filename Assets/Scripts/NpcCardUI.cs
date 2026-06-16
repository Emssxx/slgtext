using TMPro;
using UnityEngine.UI;
using UnityEngine;


// 单张 NPC 状态卡片：在 Awake 里用代码自己搭出 名字 + 四条进度条，
// 尺寸全部由代码精确控制，不依赖任何布局组，避免“被布局组锁死/挤扁”的问题。
public class NpcCardUI : MonoBehaviour
{
    [HideInInspector] public string npcId;
    [HideInInspector] public NpcAgent npcAgent; // 由 StatusPanelManager 生成卡片时塞进来

    [Header("字体（把你的中日文 TMP 字体资源拖进来，否则中文会变方块）")]
    public TMP_FontAsset font;

    [Header("尺寸参数（一般不用改）")]
    public float cardWidth = 280f;   // 卡片宽
    public float nameHeight = 22f;   // 名字行高
    public float rowHeight = 22f;    // 每条进度条那一行的高
    public float rowSpacing = 4f;    // 行间距
    public float labelWidth = 46f;   // 左侧标签宽
    public float valueWidth = 36f;   // 右侧数字宽
    public float barHeight = 14f;    // 进度条本身的高
    public float pad = 6f;           // 内边距

    // 把每条进度条要用到的两个东西打包：填充条 + 数字文字。
    private class BarRef { public RectTransform fill; public TMP_Text value; }
    private TMP_Text _nameText;
    private BarRef _hunger, _energy, _sanity, _health;

    // 记下四条进度条的"标签文字"和它们对应的翻译 key，切换语言时用来刷新。
    private TMP_Text _hungerLabel, _energyLabel, _sanityLabel, _healthLabel;

    void Awake()
    {
        BuildUI();
    }

    // 订阅语言切换：语言一变就刷新标签文字。
    void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += RefreshLabels;
    }
    void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= RefreshLabels;
    }


    // 用代码搭出整张卡片的内部结构。
    void BuildUI()
    {
        // 卡片背景设成深色半透明，方便白字显示。
        Image bg = GetComponent<Image>();
        if (bg != null) bg.color = new Color(0.1f, 0.1f, 0.12f, 0.85f);

        float startY = pad + nameHeight + 4f;                          // 第一行进度条的起始 Y
        float total = startY + 4f * (rowHeight + rowSpacing) + pad;    // 卡片总高

        // 用 LayoutElement 告诉外层的 Vertical Layout Group：这张卡该多宽多高。
        LayoutElement le = GetComponent<LayoutElement>();
        if (le == null) le = gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = cardWidth;
        le.preferredHeight = total;

        // 名字（顶部一整行）
        _nameText = NewText(transform, "NameText", "—", TextAlignmentOptions.Left);
        SetTopStretch(_nameText.rectTransform, pad, nameHeight, pad);

        // 四条进度条，从上往下依次排。标签文字用翻译表取（跟随当前语言）。
        _hunger = BuildRow("stat_hunger", startY + 0 * (rowHeight + rowSpacing), out _hungerLabel);
        _energy = BuildRow("stat_energy", startY + 1 * (rowHeight + rowSpacing), out _energyLabel);
        _sanity = BuildRow("stat_sanity", startY + 2 * (rowHeight + rowSpacing), out _sanityLabel);
        _health = BuildRow("stat_health", startY + 3 * (rowHeight + rowSpacing), out _healthLabel);

        // 在字段区加（让卡片知道点了之后通知谁）：
        UnityEngine.UI.Button btn = gameObject.GetComponent<UnityEngine.UI.Button>();
        if (btn == null) btn = gameObject.AddComponent<UnityEngine.UI.Button>();
        btn.onClick.AddListener(() =>
        {
            if (npcAgent != null)
                npcAgent.SwitchTarget(npcId); // 切换当前对话目标为本卡的 NPC
        });
    }

    // 语言切换时，重新取一遍标签文字。
    void RefreshLabels()
    {
        if (_hungerLabel != null) _hungerLabel.text = Loc("stat_hunger");
        if (_energyLabel != null) _energyLabel.text = Loc("stat_energy");
        if (_sanityLabel != null) _sanityLabel.text = Loc("stat_sanity");
        if (_healthLabel != null) _healthLabel.text = Loc("stat_health");
    }

    // 取翻译文本的小助手；管理器还没就绪时退回 key，避免报错。
    string Loc(string key)
    {
        return LocalizationManager.Instance != null
            ? LocalizationManager.Instance.Get(key)
            : key;
    }


    // 搭一行：左标签 + 中间进度条(底+填充) + 右数字。
    // labelKey 是翻译 key；out labelText 把标签文字对象传出去，方便切语言时更新。
    BarRef BuildRow(string labelKey, float yTop, out TMP_Text labelText)
    {
        GameObject row = new GameObject("Row_" + labelKey, typeof(RectTransform));
        row.transform.SetParent(transform, false);
        SetTopStretch((RectTransform)row.transform, yTop, rowHeight, pad);

        // 左侧标签：贴左，固定宽。文字用翻译表取。
        TMP_Text lab = NewText(row.transform, "Label", Loc(labelKey), TextAlignmentOptions.Left);
        RectTransform lrt = lab.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0, 1); lrt.pivot = new Vector2(0, 0.5f);
        lrt.sizeDelta = new Vector2(labelWidth, 0); lrt.anchoredPosition = Vector2.zero;
        labelText = lab; // 传出去

        // 进度条底：在标签和数字之间横向拉伸，垂直居中、固定高。
        Image barBg = NewImage(row.transform, "BarBg", new Color(0.25f, 0.25f, 0.25f, 1f));
        RectTransform brt = barBg.rectTransform;
        brt.anchorMin = new Vector2(0, 0.5f); brt.anchorMax = new Vector2(1, 0.5f); brt.pivot = new Vector2(0.5f, 0.5f);
        brt.offsetMin = new Vector2(labelWidth + 4f, -barHeight / 2f);   // 左边给标签让位
        brt.offsetMax = new Vector2(-(valueWidth + 4f), barHeight / 2f); // 右边给数字让位

        // 填充条：贴在底条左边，靠改它的宽度来表示百分比（不用 fillAmount，最稳）。
        Image fill = NewImage(barBg.transform, "BarFill", Color.green);
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1); frt.pivot = new Vector2(0, 0.5f);
        frt.anchoredPosition = Vector2.zero; frt.sizeDelta = Vector2.zero; // 宽度在 Refresh 里设

        // 右侧数字：贴右，固定宽。
        TMP_Text val = NewText(row.transform, "Value", "0", TextAlignmentOptions.Right);
        RectTransform vrt = val.rectTransform;
        vrt.anchorMin = new Vector2(1, 0); vrt.anchorMax = new Vector2(1, 1); vrt.pivot = new Vector2(1, 0.5f);
        vrt.sizeDelta = new Vector2(valueWidth, 0); vrt.anchoredPosition = Vector2.zero;

        return new BarRef { fill = frt, value = val };
    }

    // 用一份 NPC 数据刷新这张卡。
    public void Refresh(NpcData npc)
    {
        if (npc == null) return;
        npcId = npc.id;
        if (_nameText != null) _nameText.text = npc.npcName;
        SetBar(_hunger, npc.hunger);
        SetBar(_energy, npc.energy);
        SetBar(_sanity, npc.sanity);
        SetBar(_health, npc.health);
    }

    // 设置单条进度条：填充宽度 = 底条宽度 × 比例；颜色随高低绿→红。
    void SetBar(BarRef bar, float value)
    {
        if (bar == null || bar.fill == null) return;
        float ratio = Mathf.Clamp01(value / 100f);
        RectTransform barBg = (RectTransform)bar.fill.parent;
        float w = barBg.rect.width;                                  // 底条当前实际宽
        bar.fill.sizeDelta = new Vector2(w * ratio, 0);              // 填充条宽度按比例
        bar.fill.GetComponent<Image>().color = Color.Lerp(Color.red, Color.green, ratio);
        if (bar.value != null) bar.value.text = Mathf.RoundToInt(value).ToString();
    }

    // —— 下面三个是建 UI 的小工具 ——

    // 让一个 RectTransform 在父物体里“顶部横向拉伸”：紧贴顶部、左右留 side 边距、高 height、距顶 yTop。
    void SetTopStretch(RectTransform rt, float yTop, float height, float side)
    {
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(side, -(yTop + height));
        rt.offsetMax = new Vector2(-side, -yTop);
    }

    TMP_Text NewText(Transform parent, string name, string content, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TMP_Text t = go.AddComponent<TextMeshProUGUI>();
        t.text = content; t.fontSize = 16; t.alignment = align;
        t.color = Color.white; t.textWrappingMode = TextWrappingModes.NoWrap; // 用新 API 代替过时的 enableWordWrapping
        if (font != null) t.font = font; // 用你指定的中日文字体
        return t;
    }

    Image NewImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }
}
