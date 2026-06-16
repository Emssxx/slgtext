using UnityEngine;
using System.Collections.Generic;

// 状态面板总管：开局给每个 NPC 生成一张卡片，之后定时刷新所有卡片的数值。
public class StatusPanelManager : MonoBehaviour
{
    [Header("引用")]
    public Transform contentParent;   // 卡片生成在谁下面（StatusPanel 的 Content）
    public GameObject npcCardPrefab;  // NpcCard 预制体

    [Header("刷新设置")]
    public float refreshInterval = 0.5f; // 每隔多少秒刷新一次数值（不用每帧刷，省性能）

    // 已生成的卡片，按 NPC id 存，方便刷新时找到对应卡。
    private Dictionary<string, NpcCardUI> _cards = new Dictionary<string, NpcCardUI>();
    private float _timer;

    void Start()
    {
        BuildCards();
    }

    // 给世界里每个 NPC 各生成一张卡片。
    void BuildCards()
    {
        WorldState world = WorldManager.Instance.world;
        foreach (NpcData npc in world.npcs)
        {
            GameObject go = Instantiate(npcCardPrefab, contentParent);
            NpcCardUI card = go.GetComponent<NpcCardUI>();
            card.Refresh(npc);          // 初次填充
            _cards[npc.id] = card;      // 记下来
        }
    }

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= refreshInterval)
        {
            _timer = 0f;
            RefreshAll();
        }
    }

    // 刷新所有卡片的数值（从最新的世界数据读）。
    void RefreshAll()
    {
        WorldState world = WorldManager.Instance.world;
        foreach (NpcData npc in world.npcs)
        {
            if (_cards.ContainsKey(npc.id))
                _cards[npc.id].Refresh(npc);
        }
    }
}
