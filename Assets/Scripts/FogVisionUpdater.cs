using UnityEngine;
using System.Collections.Generic;

// 战争迷雾 · NPC揭雾（第3批）。
// 定期遍历所有NPC，把每个NPC视野范围内的格子设为"视野内"(Visible)，
// NPC离开的地方自动降级为"探索过"(Explored)。让迷雾随NPC移动而揭开。
//
// 依赖：
//   - FogManager（数据层，第1批）：调用它的 DemoteVisibleToExplored / RevealArea。
//   - NpcView 需要提供"遍历所有NPC视图"的接口（见下方说明，需给 NpcView 加）。
public class FogVisionUpdater : MonoBehaviour
{
    [Header("视野")]
    public int sightRadius = 4;          // NPC视野半径（格）。以后可做成每个NPC不同。

    [Header("更新频率")]
    public float updateInterval = 0.3f;  // 每隔多久更新一次揭雾（秒）。不用每帧，省性能。

    private float _timer;

    // 总开关：终端机未激活时，NPC揭雾关闭（第4批接终端机时用）。
    // 第3批先默认开启，方便单独测试。
    public bool enableNpcReveal = true;

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < updateInterval) return;
        _timer = 0f;

        UpdateVision();
    }

    void UpdateVision()
    {
        if (FogManager.Instance == null || GridManager.Instance == null) return;
        if (!enableNpcReveal) return;   // 揭雾关闭时（如终端机未激活），不做NPC揭雾

        // 1. 先把所有"视野内"降级为"探索过"。
        //    （因为NPC会移动，重新计算每个NPC当前能看到的范围。）
        FogManager.Instance.DemoteVisibleToExplored();

        // 2. 遍历所有NPC，把每个NPC视野范围内的格子设为"视野内"。
        //    需要 NpcView 提供遍历所有视图的接口（见文件末尾说明）。
        List<NpcView> allViews = NpcView.GetAllViews();
        if (allViews == null) return;

        foreach (NpcView view in allViews)
        {
            if (view == null) continue;
            // 把NPC的世界位置转成格子坐标。
            Vector2Int cell = GridManager.Instance.WorldToCell(view.transform.position);
            // 揭开这个NPC周围 sightRadius 范围。
            FogManager.Instance.RevealArea(cell.x, cell.y, sightRadius * GridManager.Subdivision); // 半径按细分前的大格填，换算成小格
        }
    }
}

// ============================================================
// 【需要给 NpcView 加的接口】—— 由 Claude Code 添加，不在本文件改：
//
// NpcView 现在的 _registry 是 private，只能按id取单个。
// 揭雾要遍历所有NPC，请给 NpcView 加一个静态方法：
//
//   public static List<NpcView> GetAllViews()
//   {
//       return new List<NpcView>(_registry.Values);
//   }
//
// （需要 using System.Collections.Generic;）
// ============================================================
