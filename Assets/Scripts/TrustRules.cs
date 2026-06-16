using UnityEngine;

// 信任规则：把"NPC 对玩家的信任度"换算成"听从玩家指令的概率"，并做掷骰判定。
// 曲线是分段的、有戏剧性的（低信任基本不听，高信任必听），数值都集中在这里方便调。
public static class TrustRules
{
    // —— 信任度 → 听从率 曲线（可调）——
    // 每一档：信任度达到 threshold 时，听从率为 rate（百分比 0~100）。
    // 从下往上匹配：信任度落在哪一档，就用那一档的听从率。
    // 想调手感，改这里的数字即可。
    private static readonly (float threshold, float rate)[] Curve =
    {
        (0f,   10f),   // 信任 0~20   → 10%（几乎不信你，基本不听）
        (20f,  30f),   // 信任 20~40  → 30%（半信半疑，多半不听）
        (40f,  60f),   // 信任 40~60  → 60%（开始倾向听）
        (60f,  85f),   // 信任 60~80  → 85%（比较信任，大概率听）
        (80f, 100f),   // 信任 80~100 → 100%（完全信任，必听）
    };

    // 根据信任度，返回听从率（0~100）。
    public static float GetObeyRate(float playerTrust)
    {
        float rate = Curve[0].rate;
        foreach (var seg in Curve)
        {
            if (playerTrust >= seg.threshold)
                rate = seg.rate;
            else
                break; // 已经超过当前信任度，前一档就是答案
        }
        return rate;
    }

    // 掷骰判定：这个 NPC 这次听不听玩家的指令。
    // 返回 true = 听从，false = 不听。
    public static bool WillObey(NpcData npc)
    {
        if (npc == null) return false;
        float rate = GetObeyRate(npc.playerTrust);
        float roll = Random.Range(0f, 100f); // 掷一个 0~100 的骰子
        return roll < rate;                  // 骰子小于听从率 → 听
    }
}
