using UnityEngine;
using TMPro;
using UnityEngine.UI;

// 行为日志管理器：把 NPC 的行为（吃饭/休息/搜寻等）显示成一条条日志。
// 和 EventLogManager 结构一样，但这个只放"NPC 行为"，并且把 NPC 名字高亮显示。
// NpcDecisionRules.Execute 产生行为后，调用 LogManager.Instance.AddLog(npcName, content) 写进来。
public class LogManager : MonoBehaviour
{
    public static LogManager Instance { get; private set; }

    [Header("UI 引用")]
    public Transform contentParent;     // 日志条生成在谁下面（日志栏里的 Content）
    public GameObject logItemPrefab;    // 一条日志的预制体（可复用 MessageItem）
    public ScrollRect scrollRect;       // 滚动视图，用于自动滚到底

    [Header("颜色")]
    public Color logColor = new Color(0.8f, 0.8f, 0.8f);          // 普通日志文字：灰白
    public Color nameColor = new Color(0.4f, 0.85f, 1f);          // NPC 名字高亮：浅蓝

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // 添加一条 NPC 行为日志。
    // npcName：行为主体的名字（会被高亮）；content：完整行为描述（开头通常就是名字）。
    public void AddLog(string npcName, string content)
    {
        if (contentParent == null || logItemPrefab == null) return;

        GameObject item = Instantiate(logItemPrefab, contentParent);
        TMP_Text label = item.GetComponentInChildren<TMP_Text>();
        if (label == null) return;

        // 开启富文本（TMP 默认开启，这里确保一下）
        label.richText = true;
        label.color = logColor;

        // 把内容里出现的 NPC 名字，用颜色标签包起来高亮。
        // 例："老五 吃了一个罐头。" → "<color=#xxxxxx>老五</color> 吃了一个罐头。"
        string hex = ColorUtility.ToHtmlStringRGB(nameColor);
        string highlighted = content;
        if (!string.IsNullOrEmpty(npcName) && content.Contains(npcName))
        {
            highlighted = content.Replace(npcName, $"<color=#{hex}>{npcName}</color>");
        }
        label.text = highlighted;

        // 立刻重建布局，修正新条目位置（和聊天框/事件栏同一个坑的同一个解法）。
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)contentParent);

        Canvas.ForceUpdateCanvases();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 0f; // 滚到底，露出最新日志
    }
}
