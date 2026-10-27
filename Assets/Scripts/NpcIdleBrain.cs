using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 单个 NPC 的闲置行为大脑（纯规则，不走 LLM）。由 NpcIdleManager 自动挂到每个 NPC 身上，不用改预制体。
// 职责：
//   1. 判断现在能不能闲置（死了/崩解/在执行玩家指令/被施工占用/被打断暂停 → 不能）。
//   2. 能闲置时，从 NpcIdleManager 的活动列表里加权随机挑一个来做（发呆/闲逛/闲聊…）。
//   3. 被打断时立刻停下当前活动；恢复后过一两秒再继续。
// 优先级：玩家指令 > 施工等系统占用（NpcBusy）> 打断暂停 > 闲置活动。
// 同一时间这个组件上只跑一个协程（当前活动），打断时 StopAllCoroutines 一次停干净。
[RequireComponent(typeof(NpcView))]
public class NpcIdleBrain : MonoBehaviour
{
    // 所有在场的 Brain（找聊天对象、避免扎堆用）。
    public static readonly List<NpcIdleBrain> All = new List<NpcIdleBrain>();

    public IdleActivity Current { get; private set; }          // 正在做的活动（null = 空闲，准备挑下一个）
    public IdleChatSession ChatSession { get; private set; }   // 正在参与的闲聊（发起方或被拉去的一方）

    NpcView _view;
    NpcMover _mover;
    NpcTaskRunner _runner;
    NavMeshAgent _agent;

    float _nextPickTime;      // 什么时候挑下一个活动
    float _pausedUntil;       // 被打断暂停到什么时候
    float _chatReadyTime;     // 什么时候可以再聊天
    float _holdUntil;         // 在指令终点附近待到什么时候
    Vector3 _holdAnchor;      // 指令终点
    Vector3 _homeFallback;    // 出生位置（找不到营地中心时用）
    bool _wasBlocked;
    bool _blockedByTask;
    string _lastActivityId;
    readonly Dictionary<string, float> _cooldownUntil = new Dictionary<string, float>();

    public string NpcId => _view != null ? _view.npcId : null;

    // 这个 NPC 的数据（每次现查；NPC 数量少，开销可以忽略）。
    public NpcData Data
    {
        get
        {
            if (WorldManager.Instance == null || string.IsNullOrEmpty(NpcId)) return null;
            return WorldManager.Instance.GetNpcById(NpcId);
        }
    }

    public bool IsMoving => _mover != null && _mover.IsMoving;
    public float BaseSpeed => _mover != null ? _mover.BaseSpeed : 3f;
    public bool ChatReady => Time.time >= _chatReadyTime;

    void Awake()
    {
        _view = GetComponent<NpcView>();
        _mover = GetComponent<NpcMover>();
        _runner = GetComponent<NpcTaskRunner>();
        _agent = GetComponent<NavMeshAgent>();
        _homeFallback = transform.position;
        _nextPickTime = Time.time + Random.Range(0.5f, 2.5f);  // 错开各 NPC 的起步时间
        _chatReadyTime = Time.time + Random.Range(5f, 12f);    // 开局先别急着聊天
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
        if (Current != null) StopActivity(true, false);
    }

    void Update()
    {
        NpcIdleManager mgr = NpcIdleManager.Instance;
        if (!CanIdle(mgr, out bool byTask))
        {
            // 不停脚步：这时可能正有玩家指令/施工在控制移动，停了会把它们的移动取消掉。
            if (Current != null) StopActivity(true, false);
            if (byTask) _blockedByTask = true;
            _wasBlocked = true;
            return;
        }

        if (_wasBlocked)
        {
            _wasBlocked = false;
            if (_blockedByTask)
            {
                // 刚做完玩家指令：先在终点附近待一会儿，别立刻跑回营地（玩家让他去那里是有原因的）。
                _holdAnchor = transform.position;
                _holdUntil = Time.time + mgr.holdAfterTaskSeconds;
                _blockedByTask = false;
            }
            _nextPickTime = Mathf.Max(_nextPickTime, Time.time + Random.Range(1.5f, 3f));
        }

        if (Current == null && Time.time >= _nextPickTime)
        {
            IdleContext ctx = BuildContext(mgr);
            IdleActivity pick = mgr.PickActivity(this, ctx);
            if (pick != null) StartActivity(pick, ctx);
            else _nextPickTime = Time.time + 1f;
        }
    }

    // 现在能不能做闲置活动。byTask = 是否因为玩家指令而不能。
    bool CanIdle(NpcIdleManager mgr, out bool byTask)
    {
        byTask = false;
        if (mgr == null || !mgr.idleEnabled) return false;
        if (string.IsNullOrEmpty(NpcId) || _mover == null || !AgentReady) return false;
        NpcData npc = Data;
        if (npc == null || npc.IsDead || npc.isBroken) return false;          // 死亡/崩解：不闲置（崩解表现走精神系统）
        if (_runner != null && _runner.IsBusy) { byTask = true; return false; } // 玩家指令优先
        if (NpcBusy.IsBusy(NpcId)) return false;                                 // 施工等系统占用
        if (Time.time < _pausedUntil) return false;                              // 被打断暂停中
        return true;
    }

    bool AgentReady => _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh;

    IdleContext BuildContext(NpcIdleManager mgr)
    {
        NpcData npc = Data;
        WorldState world = WorldManager.Instance.world;
        IdleContext ctx = new IdleContext
        {
            npc = npc,
            world = world,
            need = NpcDecisionRules.Decide(npc, world),   // 需求层（饿/累/危急）直接复用状态机的判断
            mental = MentalState.GetLevel(npc),
            lastActivityId = _lastActivityId
        };
        if (Time.time < _holdUntil)
        {
            ctx.anchor = _holdAnchor;
            ctx.anchorRadius = mgr.holdRadius;
            ctx.holding = true;
        }
        else
        {
            ctx.anchor = mgr.GetCampCenter(_homeFallback);
            ctx.anchorRadius = mgr.campRadius;
        }
        return ctx;
    }

    // ============ 活动的启动 / 结束 / 打断 ============

    void StartActivity(IdleActivity act, IdleContext ctx)
    {
        StopAllCoroutines();
        Current = act;
        _lastActivityId = act.Id;
        StartCoroutine(RunActivity(act, ctx));
    }

    IEnumerator RunActivity(IdleActivity act, IdleContext ctx)
    {
        yield return act.Run(this, ctx); // 嵌套执行活动的协程（StopAllCoroutines 能一起停掉）
        if (Current == act) FinishActivity(act);
    }

    void FinishActivity(IdleActivity act)
    {
        Current = null;
        act.OnStop(this, false);
        Cleanup(false, false);
        StartCooldown(act);
        NpcIdleManager mgr = NpcIdleManager.Instance;
        Vector2 gap = mgr != null ? mgr.gapSeconds : new Vector2(0.5f, 1.5f);
        _nextPickTime = Time.time + Random.Range(gap.x, gap.y) * NpcHealth.IdleGapMultiplier(Data); // 累了、饿了：活动没那么频繁
    }

    // 打断当前活动。stopMover：是否顺便停下脚步（被玩家指令/施工接管时不能停，否则会取消它们的移动）。
    public void StopActivity(bool interrupted, bool stopMover)
    {
        if (Current == null) return;
        IdleActivity act = Current;
        Current = null;
        StopAllCoroutines();
        act.OnStop(this, interrupted);
        Cleanup(stopMover, interrupted);
        StartCooldown(act);
    }

    void Cleanup(bool stopMover, bool interrupted)
    {
        if (_mover != null)
        {
            _mover.SetSpeedMultiplier(1f); // 闲逛时调慢的速度恢复正常
            if (stopMover && AgentReady) _mover.Stop();
        }
        if (ChatSession != null)
        {
            IdleChatSession s = ChatSession;
            ChatSession = null;
            if (!s.Ended) s.Abort(); // 自己这边退出了，这场聊天就散了，对方也会跟着结束
        }
        if (interrupted) HideBubble();
    }

    void StartCooldown(IdleActivity act)
    {
        if (act.Cooldown > 0f) _cooldownUntil[act.Id] = Time.time + act.Cooldown;
    }

    public bool IsOnCooldown(string activityId)
    {
        return _cooldownUntil.TryGetValue(activityId, out float until) && Time.time < until;
    }

    // ============ 打断（给其他系统调用）============

    // 暂停闲置行为几秒（被玩家搭话、被事件卷走等）。正在执行玩家指令时不会动它的移动。
    public void Pause(float seconds)
    {
        if (Current != null) StopActivity(true, true);
        _pausedUntil = Mathf.Max(_pausedUntil, Time.time + seconds);
    }

    // 按 id 暂停某个 NPC 的闲置行为（找不到就什么都不做，其他系统可以放心调）。
    public static void InterruptFor(string npcId, float seconds)
    {
        NpcIdleBrain b = Get(npcId);
        if (b != null) b.Pause(seconds);
    }

    public static NpcIdleBrain Get(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return null;
        foreach (NpcIdleBrain b in All)
            if (b != null && b.NpcId == npcId) return b;
        return null;
    }

    // ============ 闲聊（给 ChatActivity 用）============

    // 能不能被别人拉去聊天。
    public bool IsAvailableForChat()
    {
        if (ChatSession != null || Time.time < _chatReadyTime) return false;
        if (Current != null && !Current.Interruptible) return false;
        return CanIdle(NpcIdleManager.Instance, out _);
    }

    public void SetChatCooldown(float seconds)
    {
        _chatReadyTime = Time.time + seconds;
    }

    // 发起方：登记自己正在主持这场聊天。
    public void BeginChatAsInitiator(IdleChatSession s)
    {
        ChatSession = s;
    }

    // 被拉去聊天：停下手上的事，进入"陪聊"活动，直到聊天结束。
    public bool TryJoinAsPartner(IdleChatSession s)
    {
        NpcIdleManager mgr = NpcIdleManager.Instance;
        if (mgr == null || s == null || !IsAvailableForChat()) return false;
        if (Current != null) StopActivity(false, true);
        ChatSession = s;
        StartActivity(mgr.partnerActivity, BuildContext(mgr));
        return true;
    }

    // ============ 给活动用的小工具 ============

    // 走到某处（会先确认没有被玩家指令/施工接管）。speedMul：速度倍率。
    public bool MoveTo(Vector3 pos, float speedMul = 1f)
    {
        if (_mover == null || !AgentReady) return false;
        if ((_runner != null && _runner.IsBusy) || NpcBusy.IsBusy(NpcId)) return false;
        _mover.SetSpeedMultiplier(speedMul);
        _mover.MoveTo(pos);
        return true;
    }

    // 等走到目的地（或超时）。在活动协程里这样用：yield return brain.WaitArrive(8f);
    public IEnumerator WaitArrive(float timeout)
    {
        yield return null; // 等一帧让寻路生效（和 NpcTaskRunner 一样）
        float t = 0f;
        while (IsMoving && t < timeout)
        {
            t += Time.deltaTime;
            yield return null;
        }
    }

    public void StopMoving()
    {
        if (_mover == null) return;
        _mover.SetSpeedMultiplier(1f);
        if (AgentReady) _mover.Stop();
    }

    public void Face(Vector3 worldPos)
    {
        if (_mover != null) _mover.FaceTowards(worldPos);
    }

    public void FaceRandom()
    {
        Face(transform.position + (Random.value < 0.5f ? Vector3.left : Vector3.right));
    }

    // 头顶冒一句话（对话气泡）。
    public void Say(string text, float seconds)
    {
        NpcSpeechBubble.Show(transform, text, seconds);
    }

    public void HideBubble()
    {
        NpcSpeechBubble.Hide(transform);
    }

    // 记下 NPC 当前在做的事（按界面语言；纯数据，以后状态卡/AI 上下文可以直接用）。
    public void SetAction(string key, params object[] args)
    {
        NpcData npc = Data;
        if (npc == null) return;
        LocalizationManager loc = LocalizationManager.Instance;
        npc.currentAction = loc != null ? loc.GetFormat(key, args) : key;
    }
}
