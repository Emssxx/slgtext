using System.Collections.Generic;
using UnityEngine;

// NPC 场景物体的标识：把"数据里的 npc id"和"场景里的这个物体"关联起来。
// 挂在带 NpcMover 的 NPC 物体上，填上它对应的 id（如 npc_01）。
public class NpcView : MonoBehaviour
{
    public string npcId; // 在 Inspector 填，和数据 id 一致（如 npc_01）

    // 全局登记表：id → NpcView，方便别处按 id 找到这个 NPC 的场景物体。
    static readonly Dictionary<string, NpcView> _registry = new Dictionary<string, NpcView>();

    void OnEnable()
    {
        if (!string.IsNullOrEmpty(npcId))
            _registry[npcId] = this;
    }

    void OnDisable()
    {
        if (!string.IsNullOrEmpty(npcId) && _registry.ContainsKey(npcId))
            _registry.Remove(npcId);
    }

    // 运行时设置id（给 NpcSpawner 用：Instantiate 出NPC后，用数据里的id覆盖预制体上的默认id）。
    // 和 OnEnable 的自动登记不冲突：这里会先清掉旧id的登记，再登记新id，保证注册表里不会留脏数据。
    public void SetId(string id)
    {
        // 如果之前已经登记过一个旧id，先把那个旧登记删掉，
        // 不然注册表里会一直留着一条指向"这个物体"但id已经不对的记录。
        if (!string.IsNullOrEmpty(npcId) && _registry.ContainsKey(npcId))
            _registry.Remove(npcId);

        // 换成新id
        npcId = id;

        // 用新id登记自己，之后 NpcView.GetMover(新id) 才能找到这个物体
        if (!string.IsNullOrEmpty(npcId))
            _registry[npcId] = this;
    }

    // 按 id 找到 NPC 的 NpcMover（找不到返回 null）。
    public static NpcMover GetMover(string id)
    {
        if (_registry.TryGetValue(id, out NpcView view))
            return view.GetComponent<NpcMover>();
        return null;
    }

    // 返回所有已登记的 NpcView（FogVisionUpdater 遍历NPC揭雾用）。
    public static List<NpcView> GetAllViews()
    {
        return new List<NpcView>(_registry.Values);
    }
}
