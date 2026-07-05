using UnityEngine;

// 战争迷雾数据层调试用（第1批临时脚本，第2批完成后可删）。
// 挂在 FogManager 物体上，通过右键菜单触发测试，不改 FogManager.cs。
public class FogTestHelper : MonoBehaviour
{
    // 测试1：揭开地图中心半径3的圆形视野，验证 RevealArea 数据写入是否正确。
    [ContextMenu("Test 1: RevealArea Center,3")]
    void TestReveal()
    {
        if (FogManager.Instance == null) { Debug.LogError("[FogTestHelper] FogManager 未初始化"); return; }
        // 动态取网格中心，不写死坐标，兼容任意网格尺寸
        var grid = GridManager.Instance;
        int cx = grid.gridWidth / 2;
        int cy = grid.gridHeight / 2;
        FogManager.Instance.RevealArea(cx, cy, 3);
        Debug.Log($"[FogTestHelper] 已调用 RevealArea({cx},{cy},3)，右键选 Debug Print Fog 查看结果。");
    }

    // 测试2：把所有"视野内"降级为"探索过"，验证 DemoteVisibleToExplored 是否正确。
    [ContextMenu("Test 2: DemoteVisibleToExplored")]
    void TestDemote()
    {
        if (FogManager.Instance == null) { Debug.LogError("[FogTestHelper] FogManager 未初始化"); return; }
        FogManager.Instance.DemoteVisibleToExplored();
        Debug.Log("[FogTestHelper] 已调用 DemoteVisibleToExplored，右键选 Debug Print Fog 查看结果。");
    }

    // 测试3：打印当前迷雾状态（转发给 FogManager.DebugPrintFog）。
    [ContextMenu("Test 3: Debug Print Fog")]
    void TestPrint()
    {
        if (FogManager.Instance == null) { Debug.LogError("[FogTestHelper] FogManager 未初始化"); return; }
        FogManager.Instance.DebugPrintFog();
    }

    // 终端机调试开关：激活终端机（isActive=true），触发NPC揭雾解锁。
    [ContextMenu("Terminal: 激活")]
    void ActivateTerminal()
    {
        if (TerminalController.Instance == null) { Debug.LogError("[FogTestHelper] 场景里没有终端机，先建造一个。"); return; }
        TerminalController.Instance.isActive = true;
        Debug.Log("[FogTestHelper] 终端机已激活，NPC揭雾解锁。");
    }

    // 终端机调试开关：关闭终端机（isActive=false），视野退回营地周围。
    [ContextMenu("Terminal: 关闭")]
    void DeactivateTerminal()
    {
        if (TerminalController.Instance == null) { Debug.LogError("[FogTestHelper] 场景里没有终端机。"); return; }
        TerminalController.Instance.isActive = false;
        Debug.Log("[FogTestHelper] 终端机已关闭，视野限制回营地周围。");
    }
}
