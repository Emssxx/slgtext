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

    // 按当前界面语言选中文或日文（事件后果的固定日志文案用）。
    static string L(string zh, string ja)
    {
        bool isJp = LocalizationManager.Instance != null
                    && LocalizationManager.Instance.current == Language.Japanese;
        return isJp ? ja : zh;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // 最近一次事件落在了谁身上（空间扭曲 / 低语与幻象挑中的 NPC；没有就是 null）。
    public string LastSubjectId { get; private set; }

    // 导演解析出 type 后调用这里。type 是事件类型ID（见事件篇）。
    public void ExecuteEvent(string type) { ExecuteEvent(type, null); }

    // description：导演写的那句描述（调试按钮触发时为空，事件记录会用默认描述）。
    public void ExecuteEvent(string type, string description)
    {
        if (string.IsNullOrEmpty(type)) return;
        LastSubjectId = null;

        // 先播放该事件的屏幕视觉效果（闪色+震动+横幅），让玩家直观感知事件发生。
        if (ScreenEffectManager.Instance != null)
            ScreenEffectManager.Instance.PlayEventEffect(type);

        switch (type)
        {
            case "space_warp":      // 空间扭曲：把一个NPC瞬移到随机可站立点
                DoSpaceWarp();
                break;

            case "time_disorder":   // 时间失序：游戏时间异常快进（连消耗一起结算）
                DoTimeDisorder();
                break;

            case "whisper_vision":  // 低语与幻象：点名NPC说一句受幻象影响的话（简化版）
                DoWhisperVision();
                break;

            case "storm":           // 亚空间风暴：天气变成暴雨 + 大风（风力发电机满负荷；天气系统也会在暴雨+大风时触发它）
                ClimateSystem.Ensure().ForceStorm(2);
                break;

            // —— 以下事件留位，等对应系统就绪再实现 ——
            // case "causal_reversal": DoCausalReversal(); break; // 依赖建造材料系统

            default:
                // 未实现后果的事件类型：只播了描述文字（导演那边已显示），这里不做事。
                Debug.Log($"[EventExecutor] 事件类型 '{type}' 暂未实现后果，仅显示描述。");
                break;
        }

        // 记进事件编年史：第几天、什么事、落在谁身上，以及每个 NPC 的经历和看法（之后被问起时能回答）。
        EventChronicle.Record(type, description, LastSubjectId);
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
        LastSubjectId = chosen.id;

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
        NpcNeedRunner.CancelFor(chosen.id);       // 正在去吃饭/搜寻的，也一并中止
        NpcIdleBrain.InterruptFor(chosen.id, 5f); // 被卷走后愣在原地几秒（暂停闲逛/闲聊）

        // 5. 在事件日志记录这个客观后果（瞬移本身可见，再补一句日志强化反馈）。
        if (LogManager.Instance != null)
            LogManager.Instance.AddLog(chosen.npcName, L(
                $"{chosen.npcName} 被空间扭曲卷走，出现在了别处。",
                $"{chosen.npcName} は空間のゆがみに巻き込まれ、別の場所に現れた。"));

        // （情绪：困惑/恐慌的数值影响 → 数值篇。这里先以"终止任务"作为行为层面的表现。）
    }

    // ============ 时间失序（已实现：快进 + 结算消耗）============
    // 后果：游戏时间异常快进若干"回合"（天），并把跳过期间的消耗一并结算。
    //       实现方式 = 把"一回合的完整结算"连续跑 N 次（复用现有回合逻辑，不重复造）。
    //       注意：UI 时钟的"故障动画"留待 UI 时钟做好后补（见事件篇前提依赖）。
    void DoTimeDisorder()
    {
        // 快进的回合数（MVP 先固定 2，正式数值 → 数值篇）。
        int fastForwardTurns = 2;

        WorldState world = WorldManager.Instance.world;

        for (int i = 0; i < fastForwardTurns; i++)
        {
            // 复用"跑一回合结算"的公共方法（由 WorldManager 提供，AIDirector 也调它）。
            // 这样时间真的前进、消耗（饥饿/SAN/异常度）也一并结算，逻辑只有一份。
            WorldManager.Instance.RunOneTurnResolve();
            NpcHealth.Advance(world, NpcHealth.Instance != null ? NpcHealth.Instance.secondsPerDay : 20f); // 跳过的这一天里的饥饿、口渴、体力也一起结算
        }

        // 记录这个事件的客观后果到日志。
        if (EventLogManager.Instance != null)
            EventLogManager.Instance.AddEvent(world.day, L(
                $"时间失序：时间向前跳跃了，一切都已不同。（第 {world.day} 天）",
                $"時間のみだれ：時が前へ飛び、何もかもが変わってしまった。（{world.day} 日目）"));

        // （快进的具体回合数、是否伴随UI时钟故障动画 → 数值篇 / UI 时钟做好后补。）
    }

    // ============ 低语与幻象（简化版：点名NPC说一句受幻象影响的话）============
    // 后果：营地里有人听见低语/看见死人。简化版 = 点名一个NPC（优先挑正在幻觉的），
    //       让他头顶冒出一句受幻象影响的话（本地台词，不调 LLM；也记进他的对话记录）。
    //   完整版（可对话的先遣队员/死者幻象返场）留待以后，见事件篇。
    void DoWhisperVision()
    {
        WorldState world = WorldManager.Instance.world;

        // 1. 优先挑"正在幻觉中"的清醒NPC；没有的话，从所有清醒NPC里挑。
        List<NpcData> hallucinating = new List<NpcData>();
        List<NpcData> awake = new List<NpcData>();
        foreach (NpcData npc in world.npcs)
        {
            if (npc.health <= 0f || npc.isBroken) continue;
            awake.Add(npc);
            if (npc.isHallucinating) hallucinating.Add(npc);
        }

        List<NpcData> pool = hallucinating.Count > 0 ? hallucinating : awake;
        if (pool.Count == 0) return;

        NpcData chosen = pool[Random.Range(0, pool.Count)];
        LastSubjectId = chosen.id;

        // 2. （可选）播放低语音效：目前没有音效系统，留接口。
        //    以后有 SoundManager 时，在这里播放低语 AudioClip。
        // if (SoundManager.Instance != null) SoundManager.Instance.Play("whisper");

        // 3. 让这个NPC头顶冒出一句受幻象影响的话（本地台词，不调 LLM），同时记进他的对话记录。
        string[] lines = LanguageGuard.IsJp ? WhisperLinesJa : WhisperLinesZh;
        NpcChat.Post(chosen.id, ChatRole.Npc, lines[Random.Range(0, lines.Length)], true);

        // 4. 记录事件日志。
        if (EventLogManager.Instance != null)
            EventLogManager.Instance.AddEvent(world.day, L(
                "营地里，有人开始听见不存在的低语……",
                "拠点で、誰かが「あるはずのない囁き」を聞きはじめた……"));
    }

    // 低语与幻象时 NPC 说的话（本地台词）。
    static readonly string[] WhisperLinesZh =
    {
        "……你们听见了吗？有人在叫我的名字。",
        "刚才那边……站着一个人。现在不见了。",
        "别出声……它就在雾里看着我们。",
        "我好像听见我妈在喊我……不可能的。",
        "墙后面有人在敲……一下、两下……",
    };
    static readonly string[] WhisperLinesJa =
    {
        "……聞こえたか？誰かが私の名前を呼んでる。",
        "さっき、あそこに……人が立ってた。もういない。",
        "静かに……霧の中から、あいつがこっちを見てる。",
        "母さんが呼ぶ声がした気がする……そんなはずないのに。",
        "壁の向こうで誰かが叩いてる……一回、二回……",
    };

    // 找一个随机的可站立格子，输出其世界坐标。找到返回true。
    bool TryGetRandomStandableCell(out Vector3 worldPos)
    {
        worldPos = Vector3.zero;
        GridManager grid = GridManager.Instance;
        if (grid == null) return false;

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
