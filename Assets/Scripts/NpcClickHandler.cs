using UnityEngine;

// 挂在 NPC 物体上，点击 NPC 时打开它的状态卡。
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
        if (StatusPanelManager.Instance == null) return;
        if (_view == null || string.IsNullOrEmpty(_view.npcId)) return;
        StatusPanelManager.Instance.OpenCard(_view.npcId);
    }
}
