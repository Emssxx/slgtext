using UnityEngine;

// NPC 点击检测：挂在 NPC 预制体上，点击这个 NPC 就把对话目标切换到它。
//
// 【为什么需要它】
//   之前点击 NPC 切换对话，靠的是手摆 NPC 上的点击检测。
//   改用 NpcSpawner 动态生成后，需要把这个点击功能做成预制体自带的组件，
//   这样每个生成出来的 NPC 都能被点击。
//
// 【用法】
//   1. 把本脚本挂到 NPC 预制体上。
//   2. 确保 NPC 预制体上有 Collider2D（OnMouseDown 需要碰撞体才能被点中）。
//   3. NpcView 提供 id，本脚本点击时用该 id 切换对话目标。
[RequireComponent(typeof(NpcView))]
public class NpcClickable : MonoBehaviour
{
    private NpcView _view;

    void Awake()
    {
        _view = GetComponent<NpcView>();
        // 提醒：需要 Collider2D 才能被 OnMouseDown 检测到。
        if (GetComponent<Collider2D>() == null)
            Debug.LogWarning($"[NpcClickable] {name} 缺少 Collider2D，点击将无法生效。");
    }

    // 鼠标点到这个 NPC 的碰撞体时触发。
    void OnMouseDown()
    {
        if (_view == null || string.IsNullOrEmpty(_view.npcId)) return;

        // 找到场景里的 NpcAgent，把对话目标切换到本 NPC。
        NpcAgent agent = FindObjectOfType<NpcAgent>();
        if (agent != null)
            agent.SwitchTarget(_view.npcId);

        // （如果还要同时打开 NPC 状态卡，可在这里调用 StatusPanelManager.OpenCard(_view.npcId)。）
    }
}
