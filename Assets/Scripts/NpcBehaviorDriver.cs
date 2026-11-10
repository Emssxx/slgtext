using UnityEngine;

// NPC 行为驱动者：定期让每个 NPC 按"行为决策状态机"(NpcDecisionRules)自主行动。
// 这是新架构"决策层"的驱动入口——NpcDecisionRules 是纯函数（只判断/只执行），
// 需要这个 MonoBehaviour 定时调用它，NPC 才会自主吃饭、休息、搜寻（像 RimWorld）。
// 全程纯规则，不走 LLM。
public class NpcBehaviorDriver : MonoBehaviour
{
    [Header("设置")]
    public float decideInterval = 5f; // 每隔多少秒，让所有 NPC 重新决策一次
    [Tooltip("需求行为（吃饭/休息/搜寻/逃跑）是否让 NPC 真的走过去做。关掉 = 和以前一样只改数值。")]
    public bool physicalNeeds = true;

    private float _timer;

    void Start()
    {
        // 闲置行为系统（发呆 / 闲逛 / NPC 之间闲聊）：场景里没手动放 NpcIdleManager 的话，自动挂一个到本物体上。
        if (FindFirstObjectByType<NpcIdleManager>() == null)
            gameObject.AddComponent<NpcIdleManager>();
        // NPC 自主意图（社会系统第 1 阶段）：没事做的时候自己决定做什么（建造 / 采集 / 搜寻 / 休息），见 NpcIntents.cs
        if (FindFirstObjectByType<NpcIntentBrain>() == null)
            gameObject.AddComponent<NpcIntentBrain>();
        // 营地范围（玩家看不见）：一圈墙位、出入口、围没围起来，见 CampZone.cs
        if (FindFirstObjectByType<CampZone>() == null)
            gameObject.AddComponent<CampZone>();
        // 安全感（NPC 自发行动的动机）：营地空旷 / 异常度高 / 刚有危险 → 安全感低于底线 → 自己决定怎么办，见 NpcSecurity.cs
        if (FindFirstObjectByType<NpcSecurity>() == null)
            gameObject.AddComponent<NpcSecurity>();
        // 领导与分组（社会系统第 4 阶段）：开会选领导、每天早会分工，见 NpcSociety.cs
        if (FindFirstObjectByType<NpcSociety>() == null)
            gameObject.AddComponent<NpcSociety>();
        // 营地参谋（隐形的 4B）：每 5 秒看一次局势，把"营地眼下最要紧的事"交给 14B（领导分工 / NPC 想事情），见 CampAdvisor.cs
        if (FindFirstObjectByType<CampAdvisor>() == null)
            gameObject.AddComponent<CampAdvisor>();
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

            // 有分工的人（搜寻组除外）：仓库食物见底时不全员出去找吃的——交给搜寻组和没分工的人，自己照常干分工的活。
            // （自己饿了、身边又一点吃的都没有时不算这种情况，照样去找。饥饿门槛和 NpcDecisionRules.Decide 里一致。）
            if (behavior == NpcDecisionRules.Behavior.Gather && NpcSociety.HasRole(npc) && npc.role != "scavenge"
                && npc.hunger < 90f - npc.personality.caution * 0.2f) continue;

            // 正在打 / 躲敌人（NpcCombat）：先顾眼前，吃饭、休息、搜寻都等打完再说（不然会凭空结算一次）
            if (NpcCombat.InCombat(npc.id)) continue;

            // 2. 需求行为（吃饭/休息/搜寻/逃跑）：交给 NpcNeedRunner，让 NPC 真的走过去做，做完再结算。
            if (physicalNeeds && behavior != NpcDecisionRules.Behavior.Idle)
            {
                NpcNeedRunner needs = GetNeedRunner(npc.id);
                if (needs != null)
                {
                    if (needs.IsRunning) continue; // 上一个需求还没做完
                    if (needs.TryStart(npc, behavior, world) != NpcNeedRunner.StartResult.Unavailable) continue;
                }
            }

            // 3. 其余情况（待命 / 正在执行玩家指令或施工 / 场景里没有这个 NPC）：和以前一样直接结算。
            NpcDecisionRules.Execute(npc, behavior, world);
        }
    }

    // 取这个 NPC 的需求执行器（没有就自动挂一个，不用改预制体）。
    static NpcNeedRunner GetNeedRunner(string npcId)
    {
        NpcMover mover = NpcView.GetMover(npcId);
        if (mover == null) return null;
        NpcNeedRunner r = mover.GetComponent<NpcNeedRunner>();
        if (r == null) r = mover.gameObject.AddComponent<NpcNeedRunner>();
        return r;
    }
}
