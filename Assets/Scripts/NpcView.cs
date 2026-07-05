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
