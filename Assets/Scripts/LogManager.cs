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
    // npcName：行为主体的名字（会被高亮）；content：完整行为描述；
    // npcId：可选，有值时名字同时包上 TMP link 标签，点击可打开状态卡。
    public void AddLog(string npcName, string content, string npcId = "")
    {
        if (contentParent == null || logItemPrefab == null) return;

        GameObject item = Instantiate(logItemPrefab, contentParent);
        TMP_Text label = item.GetComponentInChildren<TMP_Text>();
        if (label == null) return;

        // 开启富文本（TMP 默认开启，这里确保一下）
        label.richText = true;
        label.color = logColor;

        // 把内容里出现的 NPC 名字，用颜色+下划线标签包起来高亮。
        // 有 npcId 时额外加 link 标签让名字可点击，并准备点击后变灰的版本。
        string hex = ColorUtility.ToHtmlStringRGB(nameColor);
        string highlighted = content;
        if (!string.IsNullOrEmpty(npcName) && content.Contains(npcName))
        {
            if (string.IsNullOrEmpty(npcId))
            {
                // 无 npcId：只高亮颜色，不可点击
                highlighted = content.Replace(npcName, $"<color=#{hex}>{npcName}</color>");
                label.text = highlighted;
            }
            else
            {
                // 有 npcId：原始版（蓝色+下划线）和点击后版（灰色+下划线）各存一份
                string originalText = content.Replace(npcName,
                    $"<link=\"{npcId}\"><u><color=#{hex}>{npcName}</color></u></link>");
                string clickedText = content.Replace(npcName,
                    $"<link=\"{npcId}\"><u><color=#888888>{npcName}</color></u></link>");
                label.text = originalText;

                // 挂点击处理器，同时把两份文本告诉它
                LogClickHandler handler = label.gameObject.AddComponent<LogClickHandler>();
                handler.npcId       = npcId;
                handler.originalText = originalText;
                handler.clickedText  = clickedText;
            }
        }
        else
        {
            label.text = highlighted;
        }

        // 立刻重建布局，修正新条目位置（和聊天框/事件栏同一个坑的同一个解法）。
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)contentParent);

        Canvas.ForceUpdateCanvases();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 0f; // 滚到底，露出最新日志
    }
}
