using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 敌人图鉴（纯数据，不走 LLM）。
//
// 【两类 × 两档】
//   造物（Creature）：亚空间里无法名状的东西。看见它就掉理智（sanityAura 范围内），见人就扑。
//   人类（Human）：别的幸存者团伙。来抢物资，受了伤会跑；落单的人才会被他们盯上。
//   弱（Weak）：NPC 自动迎战（勇敢 / 有武器 / 有同伴的上，胆小的躲）。
//   强（Strong）：以防守为主——大家躲进营地，靠墙挡；只有很勇敢的人（或拿枪的）在它破墙 / 闯进营地时才上去打。
//
// 【加新敌人】在下面 Build() 里照着加一条；名字在 LocalizationManager 里加 enemy_name_<id>。
//   美术：把图放到 Assets/Resources/EnemyArt/<id>.png（Sprite），没有图就用代码画的占位图。
// ============================================================================
public enum EnemyKind { Creature, Human }
public enum EnemyTier { Weak, Strong }

public class EnemyDef
{
    public string id;
    public EnemyKind kind;
    public EnemyTier tier;
    public float maxHp = 40f;
    public float damage = 5f;           // 每次攻击打人多少血（打在防护服上还要乘 EnemyDirector.suitArmor）
    public float attackRange = 0.75f;   // 攻击距离（> 1.5 = 远程，比如拿枪的）
    public float attackInterval = 1.3f; // 攻击间隔（秒）
    public float speed = 2f;            // 移动速度（NPC 平时 1.65 闲逛 / 3.0 赶路 / 3.9 逃跑）
    public float sight = 4.5f;          // 视野：多远能发现人
    public float sanityAura = 0f;       // 造物：这个距离内看得见它的人会掉理智（0 = 不会）
    public float sanityPerSecond = 0f;  // 每秒掉多少理智（勇敢的人掉得少）
    public float wallDamage = 0f;       // 每次攻击墙打掉多少耐久（0 = 不会破墙，被墙挡住只能绕 / 放弃）
    [Range(0f, 1f)] public float fleeBelow = 0f;  // 生命低于这个比例就逃（0 = 死战到底）
    public int steal = 0;               // 人类：冲到仓库能抢走几份物资
    public int packMin = 1, packMax = 1; // 一次出现几只
    public string dropItem;             // 被打倒后留下的东西（物品 id，直接收进仓库）
    [Range(0f, 1f)] public float dropChance = 0f;
    public Color color = Color.white;   // 占位图的颜色
    public float scale = 1f;            // 体型

    public bool Ranged => attackRange > 1.5f;
    public bool IsStrong => tier == EnemyTier.Strong;
    public string NameKey => "enemy_name_" + id;
    public string Name => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(NameKey) : id;
}

public static class EnemyDefs
{
    static Dictionary<string, EnemyDef> _defs;

    public static EnemyDef Get(string id)
    {
        if (_defs == null) Build();
        return id != null && _defs.TryGetValue(id, out EnemyDef d) ? d : null;
    }

    public static IEnumerable<EnemyDef> All
    {
        get { if (_defs == null) Build(); return _defs.Values; }
    }

    static void Add(EnemyDef d) { _defs[d.id] = d; }

    static void Build()
    {
        _defs = new Dictionary<string, EnemyDef>();

        // —— 造物 · 弱：残影。慢，单个不难打，但看久了脑子发沉 ——
        Add(new EnemyDef
        {
            id = "shade", kind = EnemyKind.Creature, tier = EnemyTier.Weak,
            maxHp = 30f, damage = 3f, attackRange = 0.7f, attackInterval = 1.4f, speed = 1.8f, sight = 4f,
            sanityAura = 2.5f, sanityPerSecond = 0.4f, wallDamage = 0f, fleeBelow = 0f,
            packMin = 1, packMax = 2, dropItem = "crystal", dropChance = 0.1f,
            color = new Color(0.55f, 0.6f, 0.75f, 0.85f), scale = 0.9f
        });

        // —— 造物 · 弱：潜猎者。成群、跑得快，会慢慢啃墙 ——
        Add(new EnemyDef
        {
            id = "stalker", kind = EnemyKind.Creature, tier = EnemyTier.Weak,
            maxHp = 45f, damage = 6f, attackRange = 0.75f, attackInterval = 1.1f, speed = 3.1f, sight = 5.5f,
            sanityAura = 2f, sanityPerSecond = 0.3f, wallDamage = 2f, fleeBelow = 0f,
            packMin = 2, packMax = 3, dropItem = "crystal", dropChance = 0.15f,
            color = new Color(0.35f, 0.15f, 0.2f, 1f), scale = 0.85f
        });

        // —— 造物 · 强：畸变体。慢、皮厚、一下打得很疼，会撞墙；靠近它的人理智狂掉 ——
        Add(new EnemyDef
        {
            id = "aberration", kind = EnemyKind.Creature, tier = EnemyTier.Strong,
            maxHp = 260f, damage = 20f, attackRange = 1.0f, attackInterval = 2.0f, speed = 1.5f, sight = 6f,
            sanityAura = 4f, sanityPerSecond = 1.2f, wallDamage = 25f, fleeBelow = 0f,
            packMin = 1, packMax = 1, dropItem = "crystal", dropChance = 1f,
            color = new Color(0.25f, 0.05f, 0.3f, 1f), scale = 1.7f
        });

        // —— 人类 · 弱：拾荒者。来抢仓库，受伤就跑 ——
        Add(new EnemyDef
        {
            id = "scavenger", kind = EnemyKind.Human, tier = EnemyTier.Weak,
            maxHp = 55f, damage = 6f, attackRange = 0.8f, attackInterval = 1.3f, speed = 2.6f, sight = 5f,
            wallDamage = 4f, fleeBelow = 0.35f, steal = 2,
            packMin = 2, packMax = 3, dropItem = "ration", dropChance = 0.3f,
            color = new Color(0.62f, 0.5f, 0.32f, 1f), scale = 1f
        });

        // —— 人类 · 强：武装掠夺者。带枪（远程），能拆墙，抢得多 ——
        Add(new EnemyDef
        {
            id = "raider", kind = EnemyKind.Human, tier = EnemyTier.Strong,
            maxHp = 140f, damage = 11f, attackRange = 3.5f, attackInterval = 1.6f, speed = 2.4f, sight = 6.5f,
            wallDamage = 10f, fleeBelow = 0.25f, steal = 4,
            packMin = 1, packMax = 1, dropItem = "magazine", dropChance = 0.6f,   // 带枪的：多半留下弹匣
            color = new Color(0.45f, 0.18f, 0.12f, 1f), scale = 1.1f
        });
    }
}
