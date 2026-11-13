using UnityEngine;

// ============================================================================
// npc2 扔手雷（NpcRigB 的一部分）：腰上挂着两颗（Grenade1 / Grenade2），扔一颗少一颗。
//   扔的动作所有角色共用（CharacterRigBase.Throw.cs）；这里只告诉它手雷挂在哪、用哪只手。
// ============================================================================
public partial class NpcRigB
{
    bool GearShown(string key) => _gear.TryGetValue(key, out Transform t) && t != null && t.GetComponent<SpriteRenderer>().enabled;
    SpriteRenderer GearSr(string key) => _gear.TryGetValue(key, out Transform t) && t != null ? t.GetComponent<SpriteRenderer>() : null;

    // 腰上还有几颗
    public override int GrenadesLeft => (GearShown("Grenade1") ? 1 : 0) + (GearShown("Grenade2") ? 1 : 0);

    // 下一颗：先拿 Grenade1，没了拿 Grenade2
    protected override SpriteRenderer NextGrenade() => GearShown("Grenade1") ? GearSr("Grenade1") : GearShown("Grenade2") ? GearSr("Grenade2") : null;

    // 腰上的手雷补满（测试 / 补给）
    public override void RefillGrenades()
    {
        foreach (string k in new[] { "Grenade1", "Grenade2" }) { SpriteRenderer sr = GearSr(k); if (sr != null) sr.enabled = true; }
    }

    protected override SpriteRenderer ThrowBodyHand => _bodyHandL;          // 空手：平时的前手
    protected override bool ThrowHandsFree => _held != "dagger";             // 拿着匕首不扔
    protected override int ThrowHandOrder => 31;                            // 和前手一样：在身上所有东西前面
}
