using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 事件日志面板管理器：只负责把一条条事件文字显示在左上角面板里，并自动滚到底。
// 结构和 ChatManager 类似，但没有输入框——它是只读的。
public class EventLogManager : MonoBehaviour
{
    public static EventLogManager Instance { get; private set; } // 单例，方便别处直接调用

    [Header("界面引用（在 Inspector 里拖进来）")]
    public Transform contentParent;       // 事件条生成在谁下面（EventLogPanel 里的 Content）
    public GameObject eventItemPrefab;    // 一条事件的预制体（复用 MessageItem）
    public ScrollRect scrollRect;         // 滚动视图，用于自动滚到底

    public Color eventColor = new Color(1f, 0.6f, 0.3f); // 事件文字颜色，橙色（亚空间氛围）

    void Awake()
    {
        // 简单单例：保证全局只有一个，方便 AIDirector 直接 EventLogManager.Instance.AddEvent(...)
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        // 清除上次 Play 模式残留的事件条目
        if (contentParent != null)
            foreach (Transform child in contentParent)
                Destroy(child.gameObject);
    }

    // 往日志里加一条事件。day=第几天，content=事件描述。
    public void AddEvent(int day, string content)
    {
        GameObject item = Instantiate(eventItemPrefab, contentParent);
        TMP_Text label = item.GetComponentInChildren<TMP_Text>();

        // 前面带上天数，像“[第3天] 墙壁渗出黑雾……”
        // 前缀走翻译表（中文"[第3天] "，日语"[3日目] "）
        string prefix = LocalizationManager.Instance != null
            ? LocalizationManager.Instance.GetFormat("event_day_prefix", day)
            : "[第" + day + "天] ";
        label.text = prefix + content;
        label.color = eventColor;

        // 立刻重建布局，修正新条目位置（和聊天框同一个坑的同一个解法）。
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)contentParent);

        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f; // 滚到底，露出最新事件
    }
}
