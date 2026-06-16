using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 聊天框管理器（简单稳定版）：直接生成消息条 + 限制最多条数 + 自动滚到底。
public class ChatManager : MonoBehaviour
{
    [Header("界面引用（Inspector 拖进来）")]
    public TMP_InputField inputField;      // 输入框
    public Button sendButton;              // 发送按钮
    public Transform contentParent;        // Scroll View 里的 Content
    public GameObject messageItemPrefab;   // MessageItem 预制体
    public ScrollRect scrollRect;          // 滚动视图

    [Header("颜色")]
    public Color playerColor = new Color(0.4f, 0.8f, 1f); // 玩家文字：浅蓝
    public Color npcColor = Color.white;                  // NPC 文字：白

    [Header("性能")]
    public int maxMessages = 80; // 最多保留多少条，超过就删最老的（足够任何演示，也不会卡）

    // 玩家发消息事件，NPC 控制器订阅它来响应。
    public event System.Action<string> OnPlayerMessage;

    void Start()
    {
        sendButton.onClick.AddListener(OnSend);
        inputField.onSubmit.AddListener(OnInputSubmit);
    }

    void OnInputSubmit(string text) { OnSend(); }

    public void OnSend()
    {
        string content = inputField.text.Trim();
        if (string.IsNullOrEmpty(content)) return;

        AddMessage("你", content, playerColor);
        inputField.text = "";
        inputField.ActivateInputField();

        OnPlayerMessage?.Invoke(content); // 通知 NPC 控制器
    }

    // 往聊天框加一条消息。speaker 为空时只显示内容（事件类消息可复用）。
    public void AddMessage(string speaker, string content, Color color)
    {
        GameObject item = Instantiate(messageItemPrefab, contentParent);
        TMP_Text label = item.GetComponentInChildren<TMP_Text>();
        label.text = string.IsNullOrEmpty(speaker) ? content : speaker + "：" + content;
        label.color = color;

        // 超过上限：删掉最老的一条（第 0 个子物体）。无对象池，删除安全。
        if (contentParent.childCount > maxMessages)
            Destroy(contentParent.GetChild(0).gameObject);

        // 重建布局，修正新条目位置，并滚到底显示最新。
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)contentParent);
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }
}
