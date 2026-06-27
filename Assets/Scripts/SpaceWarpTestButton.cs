using UnityEngine;

// 【临时调试工具】空间扭曲事件测试按钮。
// 测试完成后：先 commit，再删除此脚本和场景里的 SpaceWarpTest 物体。
public class SpaceWarpTestButton : MonoBehaviour
{
    void OnGUI()
    {
        if (GUI.Button(new Rect(10, 10, 160, 40), "测试：空间扭曲"))
        {
            if (EventExecutor.Instance != null)
                EventExecutor.Instance.ExecuteEvent("space_warp");
            else
                Debug.LogWarning("[Test] EventExecutor.Instance 为空，请确认场景里有 EventExecutor 物体。");
        }
    }
}
