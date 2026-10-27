using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI; // 推箱子用 NavMeshAgent

// 任务队列执行器：挂在 NPC 物体上（和 NpcMover 同一个物体）。
// 持有一串任务，一个接一个执行，每个完成后自动取下一个，直到全部做完。
// 支持动作：go（走到地点）、act（原地互动）、push（推箱子到目标，方案A）。
[RequireComponent(typeof(NpcMover))]
public class NpcTaskRunner : MonoBehaviour
{
    public string npcId;             // 对应数据 id（和 NpcView 一致）
    public float actDuration = 1.5f; // act 互动动作持续时间

    private NpcMover _mover;
    private Animator _animator;
    private NpcView _view;
    private Queue<NpcTask> _queue = new Queue<NpcTask>();
    private bool _running = false;
    private string _claimedId; // 在 NpcBusy 登记占用时用的 id（物体销毁后也能正确解除）

    // 是否正在执行玩家指令的任务（闲置行为系统读它：有任务时不去干扰移动）。
    public bool IsBusy => _running;

    void Awake()
    {
        _mover = GetComponent<NpcMover>();
        _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        _view = GetComponent<NpcView>();
    }

    void OnDisable()
    {
        // 物体被关掉/销毁时协程会停，这里把状态和占用登记一起收拾干净。
        if (_running) { _running = false; ReleaseClaim(); }
    }

    // 取消所有任务并立刻停止移动（被事件打断时调用）。
    public void CancelAll()
    {
        _queue.Clear();          // 清空待执行队列
        _running = false;        // 重置运行标志
        StopAllCoroutines();     // 停止正在跑的任务协程（DoGo/DoAct/DoPush）
        _mover.Stop();           // 通知 NpcMover 停下，清路径、停动画
        ReleaseClaim();
    }

    // 执行玩家指令期间在 NpcBusy 登记占用（优先级最高），需求行为、闲置行为都会让路。
    void ClaimNpc()
    {
        _claimedId = (_view != null && !string.IsNullOrEmpty(_view.npcId)) ? _view.npcId : npcId;
        NpcBusy.Claim(_claimedId, NpcBusy.Task);
    }

    void ReleaseClaim()
    {
        if (string.IsNullOrEmpty(_claimedId)) return;
        NpcBusy.Release(_claimedId, NpcBusy.Task);
        _claimedId = null;
    }

    // 下达一串任务（清掉旧的，执行新的）。
    public void RunTasks(List<NpcTask> tasks)
    {
        _queue.Clear();
        foreach (NpcTask t in tasks) _queue.Enqueue(t);
        if (!_running) StartCoroutine(RunLoop());
    }

    IEnumerator RunLoop()
    {
        _running = true;
        ClaimNpc();
        while (_queue.Count > 0)
        {
            NpcTask task = _queue.Dequeue();
            yield return StartCoroutine(DoOneTask(task));
        }
        _running = false;
        ReleaseClaim();
    }

    IEnumerator DoOneTask(NpcTask task)
    {
        switch (task.action)
        {
            case "go":
                yield return StartCoroutine(DoGo(task.destination));
                break;
            case "act":
                yield return StartCoroutine(DoAct());
                break;
            case "push":
                yield return StartCoroutine(DoPush(task.target, task.destination));
                break;
            // —— 以下由指令系统（NpcCommands，AI 词库）排出 ——
            case "use_item":    // 把物品用在自己/别人身上（target = 物品 id，destination = "@self" 或 "@npc:id"）
                yield return StartCoroutine(DoUseItem(task.target, task.destination));
                break;
            case "take_item":   // 从仓库拿一个物品带在身上（target = 物品 id）
                DoTakeItem(task.target);
                yield return new WaitForSeconds(0.8f);
                break;
            case "search_item": // 出营地搜寻（target = 想找的物品 id；空 = 普通搜寻物资）
                yield return StartCoroutine(DoSearch(task.target));
                break;
            case "rest":        // 原地休息一会儿
                yield return StartCoroutine(DoRest());
                break;
            case "eat":         // 从仓库拿吃的吃掉
                yield return new WaitForSeconds(1.5f);
                if (Self != null) NpcDecisionRules.Execute(Self, NpcDecisionRules.Behavior.Eat, WorldManager.Instance.world);
                break;
            default:
                Debug.LogWarning("未知任务动作：" + task.action);
                break;
        }
    }

    // ============ 指令系统的任务（物品 / 搜寻 / 休息）============

    string CurrentId => (_view != null && !string.IsNullOrEmpty(_view.npcId)) ? _view.npcId : npcId;
    NpcData Self => WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(CurrentId) : null;

    // 走到某处（带超时，卡住也不会一直等）。
    IEnumerator WalkTo(Vector3 pos, float timeout)
    {
        _mover.MoveTo(pos);
        yield return null;
        float t = 0f;
        while (_mover.IsMoving && t < timeout) { t += Time.deltaTime; yield return null; }
    }

    // use_item：用在别人身上时，先走到对方身边（对方可能在走动，隔一会儿重新瞄准），再用。
    IEnumerator DoUseItem(string itemId, string dest)
    {
        WorldState w = WorldManager.Instance.world;
        NpcData self = Self;
        if (self == null) yield break;
        NpcData target = self;
        if (!string.IsNullOrEmpty(dest) && dest.StartsWith("@npc:"))
            target = WorldManager.Instance.GetNpcById(dest.Substring(5)) ?? self;

        if (target != self)
        {
            float t = 0f;
            while (t < 20f)
            {
                NpcMover tm = NpcView.GetMover(target.id);
                if (tm == null || target.IsDead) break;
                if (Vector2.Distance(transform.position, tm.transform.position) <= 1.0f) break;
                _mover.MoveTo(tm.transform.position);
                yield return new WaitForSeconds(0.4f);
                t += 0.4f;
            }
            _mover.Stop();
            NpcMover tm2 = NpcView.GetMover(target.id);
            if (tm2 != null) _mover.FaceTowards(tm2.transform.position);
        }
        yield return new WaitForSeconds(1f); // 包扎 / 喂药的时间

        // 身上带着的先放回仓库再用：物品效果统一走 ItemUseRules（它从仓库扣 1 个），逻辑只有一份。
        if (TakeFromInventory(self, itemId)) WorldManager.Instance.AddToStorage(itemId, 1);
        bool ok = ItemUseRules.Use(target, itemId, w, false);
        string itemName = GameLexicon.NameOf(itemId);
        string log = !ok ? LF("cmd_log_use_fail", self.npcName, itemName)
                   : target == self ? LF("log_use_item", self.npcName, itemName)
                   : LF("cmd_log_use_on", self.npcName, target.npcName, itemName);
        Report(self, target, log);
    }

    // take_item：从仓库拿一个带在身上（以后"给某人用"时优先用身上的）。
    void DoTakeItem(string itemId)
    {
        NpcData self = Self;
        if (self == null) return;
        string itemName = GameLexicon.NameOf(itemId);
        if (WorldManager.Instance.ConsumeFromStorage(itemId, 1))
        {
            if (self.inventory == null) self.inventory = new List<ItemStack>();
            ItemStack stack = null;
            foreach (ItemStack s in self.inventory) if (s.itemName == itemId) { stack = s; break; }
            if (stack == null) self.inventory.Add(new ItemStack { itemName = itemId, count = 1 });
            else stack.count++;
            Report(self, null, LF("cmd_log_take", self.npcName, itemName));
        }
        else Report(self, null, LF("cmd_log_take_fail", self.npcName, itemName));
    }

    // search_item：走到营地外（优先迷雾里没看清的地方）→ 翻找 → 回营地。
    // 指定了物品时按种类有一定概率找到，找不到也会带回一份营养块。
    IEnumerator DoSearch(string itemId)
    {
        Vector3 camp = NpcNav.CampCenter(transform.position);
        float r = NpcNav.CampRadius;
        if (NpcNav.TryRandomPoint(transform.position, camp, r + 1f, r + 6f, true, out Vector3 field))
        {
            yield return StartCoroutine(WalkTo(field, 20f));
            _mover.Stop();
            float t = 0f, next = 0.8f;
            while (t < 4f) // 原地左右张望着翻找
            {
                if (t >= next) { _mover.FaceTowards(transform.position + (Random.value < 0.5f ? Vector3.left : Vector3.right)); next = t + 1f; }
                t += Time.deltaTime;
                yield return null;
            }
            Vector3 back = camp;
            if (NpcNav.TryRandomPoint(transform.position, camp, 0f, 1.2f, false, out Vector3 near)) back = near;
            yield return StartCoroutine(WalkTo(back, 20f));
            _mover.Stop();
        }

        NpcData self = Self;
        if (self == null) yield break;
        WorldState w = WorldManager.Instance.world;
        if (string.IsNullOrEmpty(itemId))
        {
            NpcDecisionRules.Execute(self, NpcDecisionRules.Behavior.Gather, w); // 仓库 +1 营养块、体力 -15、写日志
            yield break;
        }
        self.energy = Mathf.Clamp(self.energy - 15f * ClimateSystem.EnergyFactor(self), 0f, 100f); // 炎热时消耗更多
        string itemName = GameLexicon.NameOf(itemId);
        if (Random.value < FindChance(itemId))
        {
            WorldManager.Instance.AddToStorage(itemId, 1);
            Report(self, null, LF("cmd_log_found", self.npcName, itemName));
        }
        else
        {
            WorldManager.Instance.AddToStorage("ration", 1);
            Report(self, null, LF("cmd_log_not_found", self.npcName, itemName));
        }
    }

    // 指定物品的搜寻成功率（按种类，可调）。
    static float FindChance(string itemId)
    {
        ItemData d = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(itemId) : null;
        if (d == null) return 0.3f;
        switch (d.category)
        {
            case ItemCategory.Food:     return 0.6f;
            case ItemCategory.Medical:  return 0.35f;
            case ItemCategory.Material: return 0.5f;
            default:                    return 0.15f;
        }
    }

    // rest：原地歇一会儿，再按需求规则结算（体力回复）。
    IEnumerator DoRest()
    {
        _mover.Stop();
        yield return new WaitForSeconds(6f);
        if (Self != null) NpcDecisionRules.Execute(Self, NpcDecisionRules.Behavior.Rest, WorldManager.Instance.world);
    }

    static bool TakeFromInventory(NpcData npc, string itemId)
    {
        if (npc.inventory == null) return false;
        foreach (ItemStack s in npc.inventory)
            if (s.itemName == itemId && s.count > 0)
            {
                s.count--;
                if (s.count <= 0) npc.inventory.Remove(s);
                return true;
            }
        return false;
    }

    // 写进当事人的记忆 + 行为日志。
    static void Report(NpcData self, NpcData other, string log)
    {
        int day = WorldManager.Instance != null ? WorldManager.Instance.world.day : 0;
        IdleChatRules.AddMemory(self, day, log);
        if (other != null && other != self) IdleChatRules.AddMemory(other, day, log);
        if (LogManager.Instance != null) LogManager.Instance.AddLog(self.npcName, log, self.id);
    }

    static string LF(string key, params object[] args)
    {
        return LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;
    }

    // go：走到某地点，等到达。
    IEnumerator DoGo(string destination)
    {
        WorldState world = WorldManager.Instance.world;
        if (!CommandParser.TryGetLocationPos(destination, world, out float x, out float y))
        {
            Debug.LogWarning("找不到地点：" + destination);
            yield break;
        }
        _mover.MoveTo(new Vector3(x, y, 0));
        yield return null;
        while (_mover.IsMoving) yield return null;
    }

    // act：原地互动，用 idle2 动画占位。
    IEnumerator DoAct()
    {
        if (_animator != null) _animator.SetTrigger("doAct");
        yield return new WaitForSeconds(actDuration);
    }

    // push（方案A）：箱子用自己的 NavMeshAgent 寻路绕墙去目标，NPC 跟到箱子后方推。
    IEnumerator DoPush(string targetItemName, string destination)
    {
        WorldState world = WorldManager.Instance.world;

        // 找物品数据 + 场景物体
        WorldItem item = null;
        foreach (WorldItem it in world.worldItems)
            if (it.itemName == targetItemName) { item = it; break; }
        if (item == null) { Debug.LogWarning("找不到物品：" + targetItemName); yield break; }

        Transform itemTf = FindItemTransform(item.id);
        if (itemTf == null) { Debug.LogWarning("找不到物品场景物体：" + item.id); yield break; }

        NavMeshAgent boxAgent = itemTf.GetComponent<NavMeshAgent>();
        if (boxAgent == null) { Debug.LogWarning("箱子没有 NavMeshAgent：" + item.id); yield break; }

        if (!CommandParser.TryGetLocationPos(destination, world, out float dx, out float dy))
        { Debug.LogWarning("找不到地点：" + destination); yield break; }
        Vector3 goal = new Vector3(dx, dy, 0);

        // 1. NPC 先走到箱子旁边
        _mover.MoveTo(new Vector3(item.x, item.y, 0));
        yield return null;
        while (_mover.IsMoving) yield return null;

        // 2. 开始推
        if (_animator != null) _animator.SetBool("isPushing", true);

        float npcGap = 0.4f;
        float arrive = 0.2f;
        boxAgent.SetDestination(goal);

        // 3. 每帧：NPC 跟到箱子后方
        while (true)
        {
            if (!boxAgent.pathPending && boxAgent.remainingDistance <= arrive) break;

            Vector3 dir = boxAgent.velocity.normalized;
            if (dir.sqrMagnitude < 0.01f) dir = (goal - itemTf.position).normalized;

            Vector3 npcTarget = itemTf.position - dir * npcGap;
            _mover.MoveTo(npcTarget);

            yield return null;
        }

        // 4. 到了，停止推
        boxAgent.ResetPath();
        if (_animator != null) _animator.SetBool("isPushing", false);

        // 同步箱子坐标回数据
        item.x = itemTf.position.x;
        item.y = itemTf.position.y;
    }

    // 按物品 id 找场景物体。
    Transform FindItemTransform(string itemId)
    {
        foreach (WorldItemMarker m in FindObjectsByType<WorldItemMarker>()) // 新 API 替代过时的 FindObjectsOfType（默认不排序，更快）
            if (m.id == itemId) return m.transform;
        return null;
    }
}
