using UnityEngine;

// 【临时调试用】NPC自主行为测试面板。
// 只直接改 WorldManager 里的数据（npc.hunger / npc.energy / world.sharedStorage），
// 不调用 NpcDecisionRules / NpcBehaviorDriver 的任何方法——改完数据后，
// 交给 NpcBehaviorDriver 每隔几秒自己跑现有决策逻辑，方便快速验证效果。
//
// 这是临时脚本，测完后可以直接删掉这个文件 + 场景里挂它的物体，不会影响正式代码。
public class DebugBehaviorTestPanel : MonoBehaviour
{
    void OnGUI()
    {
        if (WorldManager.Instance == null) return;
        WorldState world = WorldManager.Instance.world;

        GUILayout.BeginArea(new Rect(Screen.width - 330, 20, 310, 420), GUI.skin.box);
        GUILayout.Label("【调试面板】NPC自主行为测试");

        GUILayout.Space(6);

        foreach (NpcData npc in world.npcs)
        {
            GUILayout.Label($"{npc.npcName}   饥饿:{npc.hunger:0}  体力:{npc.energy:0}");
            GUILayout.Label($"  当前行为: {npc.currentAction}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("饥饿设90")) npc.hunger = 90f;
            if (GUILayout.Button("体力设10")) npc.energy = 10f;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        GUILayout.Space(6);
        int canCount = 0;
        foreach (ItemStack stack in world.sharedStorage)
            if (stack.itemName == "罐头") canCount = stack.count;
        GUILayout.Label($"仓库罐头数量: {canCount}");
        if (GUILayout.Button("清空仓库（罐头设0）"))
        {
            foreach (ItemStack stack in world.sharedStorage)
                if (stack.itemName == "罐头") stack.count = 0;
        }

        GUILayout.EndArea();
    }
}
