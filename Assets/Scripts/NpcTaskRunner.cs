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
    private Queue<NpcTask> _queue = new Queue<NpcTask>();
    private bool _running = false;

    void Awake()
    {
        _mover = GetComponent<NpcMover>();
        _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
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
        while (_queue.Count > 0)
        {
            NpcTask task = _queue.Dequeue();
            yield return StartCoroutine(DoOneTask(task));
        }
        _running = false;
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
            default:
                Debug.LogWarning("未知任务动作：" + task.action);
                break;
        }
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
