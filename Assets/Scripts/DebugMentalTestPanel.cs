using UnityEngine;

// 【临时调试用】精神状态/崩解系统测试面板。
// 只直接改 WorldManager 里的数据（npc.sanity / world.anomalyLevel），
// 不调用 MentalState / WorldRules / BrokenSpeech 的任何方法——改完数据后，
// 交给 AIDirector 下一回合自己跑现有逻辑，方便快速验证效果。
//
// 这是临时脚本，测完后可以直接删掉这个文件 + 场景里挂它的物体，不会影响正式代码。
public class DebugMentalTestPanel : MonoBehaviour
{
    void OnGUI()
    {
        if (WorldManager.Instance == null) return;
        WorldState world = WorldManager.Instance.world;

        // 面板放屏幕右侧，宽 320，高度跟内容走。
        GUILayout.BeginArea(new Rect(Screen.width - 330, 20, 310, 400), GUI.skin.box);
        GUILayout.Label("【调试面板】精神状态/崩解测试");

        GUILayout.Space(6);
        GUILayout.Label($"异常度 anomalyLevel: {world.anomalyLevel:0}");
        if (GUILayout.Button("设为 80（加速全员SAN衰减）"))
            world.anomalyLevel = 80f;

        GUILayout.Space(10);

        foreach (NpcData npc in world.npcs)
        {
            GUILayout.Label($"{npc.npcName}   SAN:{npc.sanity:0}   崩解:{(npc.isBroken ? "是" : "否")}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("设50")) npc.sanity = 50f;
            if (GUILayout.Button("设20")) npc.sanity = 20f;
            if (GUILayout.Button("设0")) npc.sanity = 0f;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        GUILayout.EndArea();
    }
}
