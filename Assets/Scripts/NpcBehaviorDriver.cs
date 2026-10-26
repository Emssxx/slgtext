using UnityEngine;

// NPC 行为驱动者：定期让每个 NPC 按"行为决策状态机"(NpcDecisionRules)自主行动。
// 这是新架构"决策层"的驱动入口——NpcDecisionRules 是纯函数（只判断/只执行），
// 需要这个 MonoBehaviour 定时调用它，NPC 才会自主吃饭、休息、搜寻（像 RimWorld）。
// 全程纯规则，不走 LLM。
public class NpcBehaviorDriver : MonoBehaviour
{
    [Header("设置")]
    public float decideInterval = 5f; // 每隔多少秒，让所有 NPC 重新决策一次

    private float _timer;

    void Start()
    {
        // 闲置行为系统（发呆 / 闲逛 / NPC 之间闲聊）：场景里没手动放 NpcIdleManager 的话，自动挂一个到本物体上。
        if (FindFirstObjectByType<NpcIdleManager>() == null)
            gameObject.AddComponent<NpcIdleManager>();
    }

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= decideInterval)
        {
            _timer = 0f;
            TickAllNpcs();
        }
    }

    // 让世界里每个 NPC 决策并执行一次。
    void TickAllNpcs()
    {
        WorldState world = WorldManager.Instance.world;
        if (world == null) return;

        foreach (NpcData npc in world.npcs)
        {
            // 死了或崩解的，不走正常决策。
            if (npc.health <= 0f) continue;

            // 1. 决策：该做什么行为（纯规则）。
            NpcDecisionRules.Behavior behavior = NpcDecisionRules.Decide(npc, world);

            // 崩解的不执行普通行为（它的表现走 BrokenSpeech / 精神系统）。
            if (behavior == NpcDecisionRules.Behavior.Broken) continue;

            // 2. 执行：把决策落到世界上（改数值/物资/记忆）。
            NpcDecisionRules.Execute(npc, behavior, world);

            // 3.（可选）让 NPC 实际移动到对应位置。
            //    比如决定 gather 就走向"搜寻点"、rest 就回"营地"。
            //    阶段先只做数值，移动表现以后接（需要地点数据支持）。
        }
    }
}
