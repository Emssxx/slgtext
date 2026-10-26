using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

// 挂在每条日志 item 的 TMP_Text 节点上。
// 点击 NPC 名字 link → 打开他的对话窗口 + 名字变灰；窗口关掉或换人时名字恢复原色。
[RequireComponent(typeof(TMP_Text))]
public class LogClickHandler : MonoBehaviour, IPointerClickHandler
{
    // 由 LogManager 在 AddComponent 之后立即写入
    public string npcId;
    public string originalText;  // 蓝色+下划线版本
    public string clickedText;   // 灰色+下划线版本

    TMP_Text _text;

    void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    void Start()
    {
        // 订阅"对话窗口关掉 / 换人"事件，用于恢复名字颜色
        NpcChatWindow.OnNpcClosed += OnCardClosed;
    }

    void OnDestroy()
    {
        NpcChatWindow.OnNpcClosed -= OnCardClosed;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_text == null) return;

        // 找到点击位置命中的 link 索引（Screen Space Overlay 不需要相机，传 null）
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(_text, eventData.position, null);
        if (linkIndex < 0) return;

        string id = _text.textInfo.linkInfo[linkIndex].GetLinkID();
        if (id != npcId) return;

        // 打开他的对话窗口，同时把名字换成灰色（表示"已查看"）
        NpcChatWindow.Ensure().Open(npcId);
        _text.text = clickedText;
    }

    // 对话窗口关掉 / 换成别人时触发，npcId 匹配就恢复名字原色
    void OnCardClosed(string closedNpcId)
    {
        if (closedNpcId == npcId)
            _text.text = originalText;
    }
}
