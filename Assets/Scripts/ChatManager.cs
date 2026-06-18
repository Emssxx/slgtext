using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;

// 聊天框管理器（简单稳定版）：直接生成消息条 + 限制最多条数 + 自动滚到底。
public class ChatManager : MonoBehaviour
{
    public static ChatManager Instance { get; private set; }

    // 供外部（如 BuildModeToggle）查询输入栏是否聚焦，用 isFocused 而不是 EventSystem 选中状态
    public static bool InputFocused => Instance != null && Instance.inputField != null && Instance.inputField.isFocused;

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

    void Awake()
    {
        Instance = this;
        // 清除上次 Play 模式残留的消息子物体
        if (contentParent != null)
            foreach (Transform child in contentParent)
                Destroy(child.gameObject);
    }

    void Start()
    {
        sendButton.onClick.AddListener(OnSend);
        inputField.onSubmit.AddListener(OnInputSubmit);
        RefreshLocalizedTexts(); // 启动时先按当前语言显示一次
    }

    // 订阅语言切换：语言一变就刷新输入框占位文字和发送按钮文字。
    void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += RefreshLocalizedTexts;
    }
    void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= RefreshLocalizedTexts;
    }

    // 刷新输入框占位文字、发送按钮文字（按当前语言）。
    void RefreshLocalizedTexts()
    {
        if (LocalizationManager.Instance == null) return;

        TMP_Text placeholder = inputField.placeholder as TMP_Text;
        if (placeholder != null) placeholder.text = LocalizationManager.Instance.Get("chat_placeholder");

        TMP_Text sendLabel = sendButton.GetComponentInChildren<TMP_Text>();
        if (sendLabel != null) sendLabel.text = LocalizationManager.Instance.Get("chat_send");
    }

    void Update()
    {
        if (Keyboard.current == null || inputField == null) return;

        // 输入栏聚焦中，按 Esc → 释放聚焦（退出对话模式）
        if (inputField.isFocused && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            inputField.DeactivateInputField();
            // 同时清除 EventSystem 选中，否则 currentSelectedGameObject 仍指向输入框
            // 导致 BuildModeToggle 误判为"输入栏仍聚焦"而屏蔽 B 键
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            return;
        }

        // 没聚焦时，按 Y → 激活输入栏（进入对话模式）
        if (!inputField.isFocused && Keyboard.current.yKey.wasPressedThisFrame)
        {
            inputField.ActivateInputField();
        }
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

        // 超过上限：删掉最老的一条（第 0 个子物体）。
        // 注意：Destroy() 是延迟到这一帧结束才真正生效的，如果紧接着调用 Canvas.ForceUpdateCanvases()
        // 强制刷新，Unity 会在这个物体"已标记销毁但还没真正销毁"的中间状态去处理它的渲染/裁切，
        // 在 RectMask2D 下会报 CanvasRenderer 相关报错。所以先把它从层级里摘出来，再 Destroy。
        if (contentParent.childCount > maxMessages)
        {
            Transform oldest = contentParent.GetChild(0);
            oldest.SetParent(null);
            Destroy(oldest.gameObject);
        }

        // 重建布局，修正新条目位置，并滚到底显示最新。
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)contentParent);
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }
}
