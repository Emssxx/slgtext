using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

// 事件执行器：接收导演给出的"事件类型(type)"，执行对应的客观后果。
//
// 【架构定位】
//   导演(LLM)只负责"选哪个事件 + 写一句描述"（创造）。
//   本执行器(代码)负责"执行事件的后果"（判断/结算）。
//   —— 严格符合"LLM配音、代码做决定"的架构铁律。
//
// 【如何扩展】以后加新事件，就在 ExecuteEvent 的 switch 里加一个 case，
//   再写一个对应的私有方法即可。不影响已有事件。
public class EventExecutor : MonoBehaviour
{
    public static EventExecutor Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // 导演解析出 type 后调用这里。type 是事件类型ID（见事件篇）。
    public void ExecuteEvent(string type)
    {
        if (string.IsNullOrEmpty(type)) return;

        switch (type)
        {
            case "space_warp":      // 空间扭曲：把一个NPC瞬移到随机可站立点
                DoSpaceWarp();
                break;

            // —— 以下事件留位，等对应系统就绪再实现 ——
            // case "storm":         DoStorm(); break;          // 依赖电力系统
            // case "time_disorder": DoTimeDisorder(); break;   // 依赖UI时钟
            // case "causal_reversal": DoCausalReversal(); break; // 依赖建造材料系统
            // case "whisper_vision": DoWhisperVision(); break;  // 依赖幻象NPC生成

            default:
                // 未实现后果的事件类型：只播了描述文字（导演那边已显示），这里不做事。
                Debug.Log($"[EventExecutor] 事件类型 '{type}' 暂未实现后果，仅显示描述。");
                break;
        }
    }

    // ============ 空间扭曲（已实现）============
    // 后果：随机挑1个清醒的NPC，瞬移到地图上一个随机可站立点，
    //       该NPC陷入困惑/恐慌（情绪→数值篇，这里先终止任务作为行为表现）。
    void DoSpaceWarp()
    {
        WorldState world = WorldManager.Instance.world;

        // 1. 选一个清醒（活着、未崩解）的NPC作为被瞬移对象。
        List<NpcData> candidates = new List<NpcData>();
        foreach (NpcData npc in world.npcs)
            if (npc.health > 0f && !npc.isBroken) candidates.Add(npc);
        if (candidates.Count == 0) return; // 没有合适对象，跳过

        NpcData chosen = candidates[Random.Range(0, candidates.Count)];

        // 2. 找一个随机的可站立格子（可建造/合法格 = 可站立）。
        if (!TryGetRandomStandableCell(out Vector3 targetPos))
        {
            Debug.LogWarning("[EventExecutor] 空间扭曲：找不到可站立点，跳过。");
            return;
        }

        // 3. 找到这个NPC的场景物体，瞬移它。
        //    注意：NPC用的是NavMeshAgent，瞬移必须用 agent.Warp()，
        //    直接改 transform.position 会和导航系统冲突、导致NPC卡住。
        NpcMover mover = NpcView.GetMover(chosen.id);
        if (mover == null)
        {
            Debug.LogWarning($"[EventExecutor] 空间扭曲：找不到NPC {chosen.id} 的场景物体。");
            return;
        }

        NavMeshAgent agent = mover.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh)
            agent.Warp(targetPos);            // 用Warp安全瞬移（保留数据，只挪位置）
        else
            mover.transform.position = targetPos; // 兜底：没有agent就直接挪

        // 4. 终止该NPC当前任务（被瞬移后陷入困惑，停下手上的事）。
        NpcTaskRunner runner = mover.GetComponent<NpcTaskRunner>();
        if (runner != null) runner.CancelAll();   // 需要 NpcTaskRunner 提供 CancelAll（见下方说明）

        // 5. 在事件日志记录这个客观后果（瞬移本身可见，再补一句日志强化反馈）。
        if (LogManager.Instance != null)
            LogManager.Instance.AddLog(chosen.npcName, $"{chosen.npcName} 被空间扭曲卷走，出现在了别处。");

        // （情绪：困惑/恐慌的数值影响 → 数值篇。这里先以"终止任务"作为行为层面的表现。）
    }

    // 找一个随机的可站立格子，输出其世界坐标。找到返回true。
    bool TryGetRandomStandableCell(out Vector3 worldPos)
    {
        worldPos = Vector3.zero;
        GridManager grid = GridManager.Instance;
        if (grid == null) return false;

        // 最多尝试若干次，随机找一个合法、可建造（即可站立）的格子。
        for (int i = 0; i < 50; i++)
        {
            int x = Random.Range(0, grid.gridWidth);
            int y = Random.Range(0, grid.gridHeight);
            if (grid.InBounds(x, y) && grid.IsBuildable(x, y))
            {
                worldPos = grid.CellToWorld(x, y);
                return true;
            }
        }
        return false;
    }
}
