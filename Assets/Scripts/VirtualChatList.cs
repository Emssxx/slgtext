using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// 一条消息的纯数据（不含任何 GameObject）。要多少条都行，几乎不耗性能。
public class ChatMessage
{
    public string speaker;
    public string content;
    public Color color;
    public float height;     // 缓存高度，-1 表示还没测过

    public ChatMessage(string speaker, string content, Color color)
    {
        this.speaker = speaker;
        this.content = content;
        this.color = color;
        this.height = -1f;
    }

    // speaker 为空时只显示内容，不加“：”
    public string FullText => string.IsNullOrEmpty(speaker) ? content : speaker + "：" + content;
}

// UI 虚拟化滚动列表（动态高度）。无论几千条，场上只保留可视区那十几个真实物体。
public class VirtualChatList : MonoBehaviour
{
    [Header("引用")]
    public ScrollRect scrollRect;
    public RectTransform content;
    public GameObject itemPrefab;

    [Header("布局参数")]
    public float spacing = 8f;
    public float padding = 10f;
    public int bufferCount = 2;

    private List<ChatMessage> _messages = new List<ChatMessage>();
    private List<float> _offsets = new List<float>();
    private float _totalHeight = 0f;

    private Dictionary<int, RectTransform> _activeItems = new Dictionary<int, RectTransform>();
    private Queue<RectTransform> _pool = new Queue<RectTransform>();

    private TMP_Text _measureText;
    private bool _needsScrollToBottom = false; // 标记：本帧加了消息，布局更新后要滚到底

    void Start()
    {
        scrollRect.onValueChanged.AddListener(OnScroll);

        GameObject measureGo = Instantiate(itemPrefab, content);
        measureGo.name = "__MeasureText__";
        _measureText = measureGo.GetComponentInChildren<TMP_Text>();
        measureGo.SetActive(false);
    }

    void OnScroll(Vector2 _)
    {
        Refresh();
    }

    // 【改】LateUpdate：等本帧所有布局都更新完，再做“滚到底”。
    // 这样无论同一帧连发多少条，宽度/高度/位置都已就绪，滚动才准。
    void LateUpdate()
    {
        if (_needsScrollToBottom)
        {
            _needsScrollToBottom = false;
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
            Refresh();
        }
    }

    // 【改】实时获取可视区高度，而不是 Start 时缓存一次（避免首帧读到错的值）。
    float ViewportHeight => scrollRect.viewport.rect.height;

    // 【改】实时获取内容宽度；若还没就绪（<=0），退而用 viewport 宽度兜底。
    float ContentWidth
    {
        get
        {
            float w = content.rect.width;
            if (w <= 1f) w = scrollRect.viewport.rect.width; // 内容宽度还没算出来时的后备
            return w;
        }
    }

    // 外部调用：添加一条消息。
    public void AddMessage(string speaker, string text, Color color)
    {
        ChatMessage msg = new ChatMessage(speaker, text, color);
        msg.height = MeasureHeight(msg);

        _messages.Add(msg);

        float topOffset = padding + _totalHeight;
        _offsets.Add(topOffset);
        _totalHeight += msg.height + spacing;

        content.sizeDelta = new Vector2(content.sizeDelta.x, _totalHeight + padding);

        // 【改】不在这里立即滚动，改为标记，等 LateUpdate 布局稳定后再滚。
        _needsScrollToBottom = true;
        Refresh();
    }

    // 【改】用实时宽度测高；宽度异常时给个保底高度，避免算出 0 把布局搞乱。
    float MeasureHeight(ChatMessage msg)
    {
        float width = ContentWidth;
        Vector2 size = _measureText.GetPreferredValues(msg.FullText, width, 0f);
        float h = size.y;
        if (h <= 1f) h = 24f; // 保底，防止测出 0 导致偏移全乱
        return h;
    }

    void Refresh()
    {
        if (_messages.Count == 0) return;

        float scrollY = content.anchoredPosition.y;
        float viewTop = scrollY;
        float viewBottom = scrollY + ViewportHeight; // 【改】用实时高度

        int first = FindFirstVisible(viewTop) - bufferCount;
        int last = FindFirstVisible(viewBottom) + bufferCount;
        first = Mathf.Clamp(first, 0, _messages.Count - 1);
        last = Mathf.Clamp(last, 0, _messages.Count - 1);

        // 1. 回收不在区间内的（带判空安全网）。
        List<int> toRecycle = new List<int>();
        foreach (var kv in _activeItems)
            if (kv.Key < first || kv.Key > last)
                toRecycle.Add(kv.Key);

        foreach (int idx in toRecycle)
        {
            RectTransform item = _activeItems[idx];
            if (item == null) { _activeItems.Remove(idx); continue; }
            item.gameObject.SetActive(false);
            _pool.Enqueue(item);
            _activeItems.Remove(idx);
        }

        // 2. 显示区间内还没有物体的。
        for (int i = first; i <= last; i++)
        {
            if (_activeItems.ContainsKey(i)) continue;
            RectTransform item = GetFromPool();
            SetupItem(item, i);
            _activeItems[i] = item;
        }
    }

    int FindFirstVisible(float y)
    {
        int lo = 0, hi = _messages.Count - 1, result = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_offsets[mid] <= y) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result;
    }

    RectTransform GetFromPool()
    {
        if (_pool.Count > 0)
        {
            RectTransform r = _pool.Dequeue();
            if (r == null) return GetFromPool(); // 万一池里有被销毁的，跳过
            r.gameObject.SetActive(true);
            return r;
        }
        GameObject go = Instantiate(itemPrefab, content);
        return go.GetComponent<RectTransform>();
    }

    void SetupItem(RectTransform item, int index)
    {
        ChatMessage msg = _messages[index];

        TMP_Text label = item.GetComponentInChildren<TMP_Text>();
        label.text = msg.FullText;
        label.color = msg.color;

        item.anchorMin = new Vector2(0f, 1f);
        item.anchorMax = new Vector2(1f, 1f);
        item.pivot = new Vector2(0.5f, 1f);
        item.offsetMin = new Vector2(0f, item.offsetMin.y);
        item.offsetMax = new Vector2(0f, item.offsetMax.y);

        item.anchoredPosition = new Vector2(0f, -_offsets[index]);
        item.sizeDelta = new Vector2(item.sizeDelta.x, msg.height);
    }
}
