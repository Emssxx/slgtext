using UnityEngine;

// 终端机点击处理：玩家点击终端机时打开终端面板，并在事件日志里显示当前在线/离线状态。
// 点击由界面层 WorldClickRouter 统一检测，点中终端机时调用这里的 OnWorldClick（终端机有自己的面板，不弹通用信息浮窗）。
// 依赖同物体上的 TerminalController，以及场景里的 EventLogManager / WorldManager。
[RequireComponent(typeof(TerminalController))]
public class TerminalClickHandler : MonoBehaviour, IWorldClick
{
    private TerminalController _tc; // 同物体上的 TerminalController

    void Awake()
    {
        _tc = GetComponent<TerminalController>();
    }

    // 返回 true = 处理了（开了终端面板）；false = 交给 WorldClickRouter 弹通用信息浮窗
    public bool OnWorldClick()
    {
        // 还是蓝图（规划区）：点它是去指派施工的人（ConstructionManager 处理），不开终端面板
        if (_tc != null && !_tc.isBuilt) return false;
        // 打开终端机面板（升级 / 合成 / 科技）
        if (TerminalPanelUI.Instance == null) return false;
        TerminalPanelUI.Instance.Open();
        if (EventLogManager.Instance != null && _tc != null)
        {
            int day = WorldManager.Instance != null ? WorldManager.Instance.world.day : 1;
            // 把状态写入事件日志，文字来自 TerminalController.GetStatusInfo()
            EventLogManager.Instance.AddEvent(day, _tc.GetStatusInfo());
        }
        return true;
    }
}
