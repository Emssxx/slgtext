using UnityEngine;

// 挂在 NPC 物体上，点击 NPC 时打开它的对话窗口（状态卡已并入对话窗口上方）。
// 需要同节点有 NpcView（提供 npcId）和 Collider2D（让 OnMouseDown 能响应）。
[RequireComponent(typeof(NpcView))]
public class NpcClickHandler : MonoBehaviour
{
    NpcView _view;

    void Awake()
    {
        _view = GetComponent<NpcView>();
    }

    void OnMouseDown()
    {
        if (_view == null || string.IsNullOrEmpty(_view.npcId)) return;
        NpcChatWindow.Ensure().Open(_view.npcId);
    }
}
