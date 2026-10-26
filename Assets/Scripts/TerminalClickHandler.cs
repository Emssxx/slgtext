using UnityEngine;

// 终端机点击处理：玩家点击终端机时，在事件日志里显示当前在线/离线状态。
// 依赖同物体上的 TerminalController，以及场景里的 EventLogManager / WorldManager。
[RequireComponent(typeof(TerminalController))]
public class TerminalClickHandler : MonoBehaviour
{
    private TerminalController _tc; // 同物体上的 TerminalController

    void Awake()
    {
        _tc = GetComponent<TerminalController>();
    }

    // 鼠标点击终端机时触发（需要 Collider2D，prefab 上已有 BoxCollider2D）
    void OnMouseDown()
    {
        // 打开终端机面板（升级 / 合成 / 科技）
        if (TerminalPanelUI.Instance != null) TerminalPanelUI.Instance.Open();
        if (EventLogManager.Instance == null) return;
        int day = WorldManager.Instance != null ? WorldManager.Instance.world.day : 1;
        // 把状态写入事件日志，文字来自 TerminalController.GetStatusInfo()
        EventLogManager.Instance.AddEvent(day, _tc.GetStatusInfo());
    }
}
