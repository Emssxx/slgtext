using UnityEngine;

// 战争迷雾数据层调试用（第1批临时脚本，第2批完成后可删）。
// 挂在 FogManager 物体上，通过右键菜单触发测试，不改 FogManager.cs。
public class FogTestHelper : MonoBehaviour
{
    // 测试1：揭开地图中心半径3的圆形视野，验证 RevealArea 数据写入是否正确。
    [ContextMenu("Test 1: RevealArea(15,10,3)")]
    void TestReveal()
    {
        if (FogManager.Instance == null) { Debug.LogError("[FogTestHelper] FogManager 未初始化"); return; }
        FogManager.Instance.RevealArea(15, 10, 3);
        Debug.Log("[FogTestHelper] 已调用 RevealArea(15,10,3)，右键选 Debug Print Fog 查看结果。");
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
}
