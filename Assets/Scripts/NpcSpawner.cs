using UnityEngine;
using UnityEngine.AI;

// NPC 生成器：根据 world.npcs 的数据，自动在场景里生成对应数量的 NPC 物体。
//
// 【为什么需要它】
//   原本 NpcView 的 npcId 是在 Inspector 手动填的，NPC 物体也是手动摆的。
//   结果：数据层生成了 3 个 NPC，但场景里只手摆了 1 个 → 只显示 1 个。
//   本 Spawner 让「场景 NPC」根据「数据」自动生成，数据有几个就生成几个，永远同步。
//
// 【用法】
//   1. 场景里【删掉手动摆放的 NPC 物体】（改由本 Spawner 生成）。
//   2. 把一个 NPC 预制体（带 NpcView / NpcMover / NpcTaskRunner / NavMeshAgent 等）
//      拖到本脚本的 npcPrefab 字段。
//   3. 本 Spawner 在世界创建后，按 world.npcs 生成，并给每个绑定正确的 id。
public class NpcSpawner : MonoBehaviour
{
    [Header("NPC 预制体（带 NpcView/NpcMover/NavMeshAgent 等）")]
    public GameObject npcPrefab;

    [Header("生成位置")]
    public Transform spawnCenter;      // 生成中心（不填则用本物体位置，通常放营地中心/终端机附近）
    public float spawnRadius = 3f;     // 在中心周围多大半径内随机散开

    private bool _spawned = false;

    // 由 WorldManager 在世界创建完成后调用（见下方接入说明）。
    public void SpawnAll()
    {
        if (_spawned) return;
        _spawned = true;

        if (npcPrefab == null) { Debug.LogError("[NpcSpawner] 没有指定 npcPrefab"); return; }
        if (WorldManager.Instance == null || WorldManager.Instance.world == null)
        { Debug.LogError("[NpcSpawner] world 还没准备好"); return; }

        Vector3 center = spawnCenter != null ? spawnCenter.position : transform.position;

        // 遍历数据里的每个 NPC，各生成一个场景物体。
        foreach (NpcData data in WorldManager.Instance.world.npcs)
        {
            // 1. 在营地中心周围，找一个随机的可站立点。
            Vector3 pos = FindSpawnPos(center);

            // 2. 实例化 NPC 预制体。
            GameObject go = Instantiate(npcPrefab, pos, Quaternion.identity);
            go.name = "NPC_" + data.id;

            // 3. 绑定 id：让这个场景物体和数据里的 npc 对应起来。
            NpcView view = go.GetComponent<NpcView>();
            if (view != null) view.SetId(data.id);   // 需要给 NpcView 加 SetId（见说明）
            else Debug.LogWarning($"[NpcSpawner] 预制体缺少 NpcView：{data.id}");

            // 4. 如果有 NavMeshAgent，确保它落在 NavMesh 上（用 Warp 放置更安全）。
            NavMeshAgent agent = go.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh == false)
            {
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
            }
        }

        Debug.Log($"[NpcSpawner] 已生成 {WorldManager.Instance.world.npcs.Count} 个 NPC");
    }

    // 在中心周围找一个随机、且在 NavMesh 上的可站立点。
    Vector3 FindSpawnPos(Vector3 center)
    {
        for (int i = 0; i < 30; i++)
        {
            Vector2 r = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = center + new Vector3(r.x, r.y, 0f);
            // 采样到最近的 NavMesh 点，确保 NPC 能站、能走。
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                return hit.position;
        }
        return center; // 兜底：实在找不到就用中心
    }
}
