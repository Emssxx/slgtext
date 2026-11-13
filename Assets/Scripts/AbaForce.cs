using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// ============================================================================
// ABA 清除小组（npc2：头盔士兵 NpcRigB）—— 游戏规则（纯规则，不走 LLM）。
//
// 【他们是谁】ABA 派进亚空间的清除小组，训练有素。一个小组 4 人（AbaSquad），每人一个 AbaOperative。
// 【规则】
//   · 不攻击自己人：从不把 ABA 队员当目标；开枪前看火线，有队友挡着就侧移换角度；自己人的子弹穿过队友（Ballistics）。
//   · 目标：亚空间里所有活着的生物——所有敌人（造物 / 拾荒者 / 掠夺者，游荡的也算）+ 幸存者 NPC。
//     例外：身上带着小型 AI 终端机（物品 id = ai_terminal，就是 npc2 胸挂上那个 Terminal）的人不打，火线上有他也不开枪。
//     （targetSurvivors 关掉 = 只打敌人，调试用）
//   · 训练有素：武器操作等级默认 5；体力消耗 = 普通 NPC 的 staminaFactor（0.3 倍，小于三分之一）。
//   · 专属武器（数值在枪的数据里：WeaponDef 的"战斗数值"一栏，菜单 SLG → 武器 → 生成武器数值文件后在 Inspector 改；这里只选拿哪把）：
//       战术步枪：一个弹匣 35 发；点射（远）/ 连发（近）；散布极小、预判极准、后坐恢复快；有一定几率打中要害（伤害 ×critMul）。
//       手枪：一个弹匣 12 发；双击（一次两枪）；子弹带击退（把敌人往后推 + 打断它的攻击，体型越大推得越少）。
//   · 弹匣：步枪 / 手枪各 5 个（枪里 1 + 胸挂 4），和 NpcRigB 胸挂上画的一样；打空的弹匣扔掉，没打空的战术换弹收回来。
// 【测试】F10：在鼠标位置放一个清除小组；Shift + F10：在营地外随机一个地方放（会一路清过来）。
// 【战术】见 AbaSquad（SWAT：队形 / 扇区警戒 / 交替掩护前进 / 分配目标 + 集火 / 错开换弹 / 转手枪 / 后撤射击 / 区域清除后巩固）。
// ============================================================================
public class AbaWeapon
{
    public string id;               // "rifle" / "pistol"（也是 NpcRigB 里的换手键）
    public int magSize;             // 一个弹匣几发
    public float damage;            // 每发伤害（打幸存者还要乘防护服，和别人一样）
    public float spread;            // 站着、冷静时的散布 σ（度）——已经是 5 级的手感，不再乘等级
    public float movingMul;         // 边走边打散布倍数（训练过，比普通人 1.4 小）
    public float recoilPerShot;     // 每开一枪散布加多少度
    public float recoilRecover;     // 每秒恢复多少度（压枪）
    public float leadError;         // 预判误差（0.06 = ±6%）
    public float bulletSpeed;       // 子弹速度（单位 / 秒）
    public float shotInterval;      // 连发 / 双击时两枪间隔（秒）
    public float pause;             // 两组点射之间停多久（秒）
    public float range;             // 有效射程（超过就往前压，不浪费子弹）
    public float knockback;         // 击退（打中时把目标往后推的初速度，单位 / 秒；0 = 没有）
    public float critChance;        // 打中要害的几率（这一枪伤害 × critMul）
    public float critMul;
    public float speedReload;       // 快速换弹几秒（打空了，旧弹匣直接扔）
    public float tacticalReload;    // 战术换弹几秒（没打空，旧弹匣收回胸挂）
    public string bulletSprite;     // 子弹图（Resources 路径）
    public WeaponDef def;           // 这把枪的数据（瞄准镜 / 镭射这些配件效果也在里面）

    // 从枪的数据拿数值（数值都在 WeaponDef 的"战斗数值"一栏；key = NpcRigB 里的换手键 "rifle" / "pistol"）
    public static AbaWeapon From(WeaponDef d, string key)
    {
        if (d == null) return null;
        return new AbaWeapon
        {
            id = key, def = d, magSize = Mathf.Max(1, d.magSize), damage = d.damage, spread = d.spread, movingMul = d.movingMul,
            recoilPerShot = d.recoilPerShot, recoilRecover = d.recoilRecover, leadError = d.leadError, bulletSpeed = d.bulletSpeed,
            shotInterval = d.ShotInterval, pause = d.burstPause, range = d.range, knockback = d.knockback,
            critChance = d.critChance, critMul = d.critMul, speedReload = d.speedReloadTime, tacticalReload = d.tacticalReloadTime,
            bulletSprite = d.BulletPath,
        };
    }
}

public class AbaForce : MonoBehaviour
{
    public static AbaForce Instance { get; private set; }

    [Header("规则")]
    [Tooltip("把没带小型 AI 终端机的幸存者也当目标（关掉 = 只打敌人，调试用）")]
    public bool targetSurvivors = true;
    [Tooltip("一个小组几个人")]
    [Range(2, 6)] public int squadSize = 4;
    [Tooltip("武器操作等级（1~5）")]
    [Range(1, 5)] public int weaponLevel = 5;
    [Tooltip("体力消耗是普通 NPC 的几倍（0.3 = 小于三分之一）")]
    [Range(0.05f, 1f)] public float staminaFactor = 0.3f;

    [Header("队员")]
    public float maxHp = 160f;
    [Tooltip("护甲：受到的伤害 × (1 - 这个)")]
    [Range(0f, 0.9f)] public float armor = 0.4f;
    public float sight = 8f;                 // 视野（单位）
    public float walkSpeed = 1.65f;          // 战术行进
    public float runSpeed = 3.4f;            // 跑（体力 > 15 才能跑）
    public int medkits = 2;                  // 每人带几个急救包（NpcRigB 腰上画着两个）
    public float medkitHeal = 45f;

    [Header("武器")]
    [Tooltip("队员拿哪把步枪（WeaponDef id）：aba_rifle = 战斗步枪，aba-x95 = ABA-X95，aba-ar15 = ABA-AR15，akm = AKM，aba-m2 = ABA-M2 狙击枪（新生成的队员才换）")]
    public string rifleModel = "aba_rifle";
    [Tooltip("队员的手枪（WeaponDef id）：aba_pistol = ABA 战术手枪，aba-p225 = ABA-P225，aba-17 = ABA-17（连发）")]
    public string pistolModel = "aba_pistol";
    // 每把枪的数值（弹匣、伤害、散布、射速、射程、要害、瞄准镜 / 镭射……）在枪的数据里：WeaponDef 的"战斗数值"一栏

    [Header("手雷（腰上两颗；扔的动作在 NpcRigB.Throw.cs，飞 / 炸在 Grenade.cs）")]
    [Tooltip("什么时候扔：目标躲在墙 / 石头后面打不到、目标身边扎堆（≥ 下面的个数）、或者是大家伙")]
    public Vector2 grenadeRange = new Vector2(2.5f, 6.5f);   // 能扔的距离（太近会炸到自己，太远扔不到）
    public float grenadeCooldown = 8f;                       // 同一个人扔完一颗至少等几秒
    public float grenadeSquadGap = 2.5f;                     // 整个小组两颗手雷之间至少隔几秒（不一起扔）
    public int grenadeCluster = 2;                           // 目标身边（爆炸半径里）有几个敌人就值得扔
    public Grenade.Spec grenade = new Grenade.Spec();        // 手雷的伤害 / 半径 / 引信 / 飞多快 / 爆炸大小和光

    [Header("测试")]
    public bool debugKeys = true;            // F10 放一个小组

    // 这把步枪（WeaponDef id）在游戏里的数值：直接读枪的数据（不是步枪 / 没有这把：用战斗步枪）
    public AbaWeapon RifleFor(string weaponId)
    {
        WeaponDef d = string.IsNullOrEmpty(weaponId) ? null : WeaponDef.Get(weaponId);
        if (d == null || d.hold != HoldType.Rifle) { weaponId = "aba_rifle"; d = WeaponDef.Get(weaponId); }
        return Cached(weaponId, d, "rifle");
    }
    public AbaWeapon Pistol => Cached(pistolModel, WeaponDef.Get(pistolModel), "pistol");

    // 数值每秒按枪的数据刷新一次（运行中改了武器数值文件也会生效）
    readonly Dictionary<string, AbaWeapon> _weapons = new Dictionary<string, AbaWeapon>();
    readonly Dictionary<string, float> _weaponAt = new Dictionary<string, float>();
    AbaWeapon Cached(string id, WeaponDef d, string key)
    {
        string k = key + ":" + id;
        if (_weapons.TryGetValue(k, out AbaWeapon w) && _weaponAt.TryGetValue(k, out float at) && Time.unscaledTime - at < 1f) return w;
        AbaWeapon fresh = AbaWeapon.From(d, key);
        if (fresh == null) return w;
        if (w != null)                                                      // 已经有人拿着这个对象：原地更新（不换引用）
        {
            w.magSize = fresh.magSize; w.damage = fresh.damage; w.spread = fresh.spread; w.movingMul = fresh.movingMul;
            w.recoilPerShot = fresh.recoilPerShot; w.recoilRecover = fresh.recoilRecover; w.leadError = fresh.leadError;
            w.bulletSpeed = fresh.bulletSpeed; w.shotInterval = fresh.shotInterval; w.pause = fresh.pause; w.range = fresh.range;
            w.knockback = fresh.knockback; w.critChance = fresh.critChance; w.critMul = fresh.critMul;
            w.speedReload = fresh.speedReload; w.tacticalReload = fresh.tacticalReload; w.bulletSprite = fresh.bulletSprite; w.def = d;
        }
        else { w = fresh; _weapons[k] = w; }
        _weaponAt[k] = Time.unscaledTime;
        return w;
    }

    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;

    public static AbaForce Ensure()
    {
        if (Instance != null) return Instance;
        AbaForce f = FindFirstObjectByType<AbaForce>();
        if (f != null) return f;
        return new GameObject("AbaForce").AddComponent<AbaForce>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }


    void Update()
    {
        if (debugKeys) DebugKeys();
    }

    // ============ 规则：谁是目标 ============

    // 身上有没有小型 AI 终端机（有 = ABA 的人 / 合作者，不打）
    public static bool HasTerminal(NpcData n) => n != null && NpcInventory.Has(n, TerminalItem);
    public const string TerminalItem = "ai_terminal";

    // 这个幸存者是不是清除目标
    public static bool IsTarget(NpcData n)
    {
        if (n == null || n.IsDead) return false;
        if (Instance != null && !Instance.targetSurvivors) return false;
        return !HasTerminal(n);
    }

    // 敌人：亚空间里的生物，全部是目标（游荡的也算）
    public static bool IsTarget(Enemy e) => e != null && e.Alive;

    // ============ 体力 ============

    // 普通 NPC 战斗每秒消耗多少体力 × 系数
    public static float CombatDrain => (NpcHealth.Instance != null ? NpcHealth.Instance.combatEnergyPerSecond : 2f) * Factor;
    public static float Factor => Instance != null ? Instance.staminaFactor : 0.3f;

    // ============ 部署 ============

    // 在 at 附近放一个小组（count = 人数，0 = 用 squadSize）
    public static AbaSquad Deploy(Vector3 at, int count = 0)
    {
        AbaForce f = Ensure();
        if (count <= 0) count = f.squadSize;
        return AbaSquad.Create(at, count);
    }

    void DebugKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || ChatManager.InputFocused || !kb.f10Key.wasPressedThisFrame) return;
        bool shift = kb.shiftKey.isPressed;
        Vector3 at;
        if (!shift && Camera.main != null && Mouse.current != null)
        {
            Vector2 mp = Mouse.current.position.ReadValue();
            at = Camera.main.ScreenToWorldPoint(new Vector3(mp.x, mp.y, 0f));
            at.z = 0f;
        }
        else
        {
            Vector3 camp = NpcNav.CampCenter(Vector3.zero);
            float a = Random.value * Mathf.PI * 2f;
            at = camp + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * Random.Range(14f, 20f);
        }
        if (!NpcNav.Sample(at, 3f, out Vector3 p)) return;
        Deploy(p);
    }

    // ============ 本地化 ============

    public static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    public static string LF(string key, params object[] a) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, a) : key;
}

// ============================================================================
// 击退：打中时给目标一个往后的速度，很快减速停下（走导航网格，不会被推进墙里）。人、敌人都能用。
// ============================================================================
public class Knockback : MonoBehaviour
{
    Vector2 _v;
    NavMeshAgent _agent;
    const float Drag = 10f;   // 越大停得越快（推出去的距离 ≈ 初速度 / Drag）

    public static void Push(GameObject go, Vector2 velocity)
    {
        if (go == null || velocity.sqrMagnitude < 0.0001f) return;
        Knockback k = go.GetComponent<Knockback>();
        if (k == null) k = go.AddComponent<Knockback>();
        k._v += velocity;
        if (k._agent == null) k._agent = go.GetComponent<NavMeshAgent>();
        k.enabled = true;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 step = new Vector3(_v.x, _v.y, 0f) * dt;
        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh) _agent.Move(step);
        else transform.position += step;
        _v *= Mathf.Exp(-Drag * dt);
        if (_v.sqrMagnitude < 0.0004f) { _v = Vector2.zero; enabled = false; }
    }
}
