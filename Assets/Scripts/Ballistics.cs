using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// ============================================================================
// 弹道（纯规则，不走 LLM）：子弹是真的在飞的东西，打中什么由它飞过的路决定，不再掷命中率。
//
// 【瞄准】瞄准点 = 目标身体中心 + 预判（目标速度 × 子弹飞过去的时间，再带 ±leadError 的误差）。
//   实际射出方向 = 瞄准方向 + 一个随机偏角（正态分布）。偏角的大小（散布 σ，单位：度）由函数算：
//     NPC：σ = 手枪基础 × 武器操作等级倍数 × (1 + 手抖) × 理智 × 疲劳 × 受伤 × (边走边打 ? movingMul : 1) + 后坐力
//       手抖 = max(紧张 NpcRig.nervous, 这个等级平时的手抖 NpcRig.levelTremble)
//       后坐力：每开一枪 +recoilPerShot 度，每秒恢复 recoilRecover 度（连着打越打越飘）
//     敌人（武装掠夺者）：σ = raiderSpread × (边走边打 ? movingMul : 1) + 后坐力
//   偏角是角度，所以离得越远偏得越多——远了自然难打中，不用另写距离规则。
//   基础散布按旧弹道（偏差区域）的命中率校准过：3 级、站着、冷静，离 1 单位约 90%、2 单位约 60%、4 单位约 33%。
//
// 【子弹】运动学弹丸（不用 Rigidbody2D：子弹太快会穿过碰撞体，俯视角也没有重力）：
//   每帧往前飞"速度 × dt"，同时检查这一帧飞过的线段碰到了什么，碰到的第一个东西就是落点：
//     · 人 / 敌人的身体（圆，半径 bodyRadius，敌人再 × 体型）→ 扣血（friendlyFire 开着时流弹也会打中自己人）
//     · 挡路的东西（按小格查）：地形的墙、挡路的树 / 石头 / 矿石、挡路的建筑（墙、发电机……；还没建好的规划区不算）
//     · 都没碰到：手枪是斜着往人身上打的，打偏了会落在"瞄准距离 × groundAt"那么远的地上（最远 maxRange）
//   打在金属上、贴着面斜着打（入射角 < ricochetAngle）→ 有 metalRicochet 的概率弹开，威力减半，最多弹一次。
//
// 【打中东西】每次打中都发一个 OnImpact 事件（打击点、方向、被打的面朝哪、破坏力、材质、被打的物体、小格）：
//   现在只放一点碎屑 / 火花 / 木屑 / 尘土；以后墙掉块、树枝折断、挖矿的破坏效果都接在这个事件上。
//
// 【调试】showSpread 打开：Scene 窗口里画每一枪的散布扇形（±2σ，黄色）和实际弹道（红色）。
// ============================================================================
public enum ImpactMaterial { Ground, Flesh, Creature, Stone, Metal, Wood }

public struct Impact
{
    public Vector3 point;            // 打在哪（世界坐标）
    public Vector2 dir;              // 子弹飞来的方向（单位向量）
    public Vector2 normal;           // 被打的那一面朝哪（墙 / 石头 = 格子的边；人 / 地面 = 子弹反方向）
    public float power;              // 破坏力（这颗子弹的伤害；跳弹后变小）
    public ImpactMaterial material;
    public GameObject target;        // 被打中的物体（墙 / 树 / 石头 / 敌人 / NPC；地面、地形墙为 null）
    public Vector2Int cell;          // 打中的小格（墙 / 树 / 石头 / 建筑才有意义）
    public NpcData byNpc;            // 谁开的枪（NPC）
    public Enemy byEnemy;            // 谁开的枪（敌人）
    public AbaOperative byAba;       // 谁开的枪（ABA 清除小组）
}

public class Ballistics : MonoBehaviour
{
    public static Ballistics Instance { get; private set; }

    // 每颗子弹打中东西（人、敌人、墙、树、石头、金属、地面）都会发这个。以后的破坏效果接这里。
    public static event Action<Impact> OnImpact;

    [Header("子弹")]
    [Tooltip("敌人开枪的子弹速度（NPC 开枪用他自己 NpcRig 上的 Bullet Speed）")]
    public float defaultSpeed = 100f;
    [Tooltip("子弹最远飞多远（单位）")]
    public float maxRange = 14f;
    [Tooltip("没打中的子弹落在瞄准距离的多少倍处（最小 / 最大）：手枪是斜着往人身上打的，打偏了会落在目标后面一点的地上")]
    public Vector2 groundAt = new Vector2(1.15f, 2.2f);
    [Tooltip("流弹会不会打中自己人（NPC 打 NPC / 敌人打敌人）")]
    public bool friendlyFire = true;
    [Tooltip("身体多粗：子弹离身体中心多近算打中（单位；敌人再 × 体型）")]
    public float bodyRadius = 0.3f;

    [Header("散布（度）：σ = 基础 × 武器等级 × 状态 + 后坐力")]
    [Tooltip("手枪基础散布（3 级、站着、冷静）")]
    public float pistolSpread = 10f;
    [Tooltip("武装掠夺者的散布")]
    public float raiderSpread = 6f;
    [Tooltip("武器操作等级 1~5 的散布倍数（第 1 个 = 1 级）")]
    public float[] levelSpread = { 4f, 2f, 1f, 0.5f, 0.25f };
    [Tooltip("手抖（紧张 / 生疏）满的时候散布多乘 (1 + 这个)")]
    public float trembleMul = 1f;
    public float movingMul = 1.4f;          // 边走边打
    public float lowSanityMul = 1.3f;       // 理智 < 40
    public float veryLowSanityMul = 1.6f;   // 理智 < 20
    public float fatiguedMul = 1.2f;        // 疲劳
    public float injuredMul = 1.25f;        // 受伤
    [Tooltip("每开一枪散布加多少度")]
    public float recoilPerShot = 2f;
    [Tooltip("后坐力每秒恢复多少度")]
    public float recoilRecover = 1.2f;
    [Tooltip("预判目标走位的误差（0.3 = ±30%）")]
    [Range(0f, 1f)] public float leadError = 0.3f;

    [Header("跳弹（金属）")]
    [Range(0f, 1f)] public float metalRicochet = 0.6f;
    [Tooltip("入射角小于这个（贴着面斜着打）才可能弹开（度）")]
    public float ricochetAngle = 30f;

    [Header("碎屑")]
    [Tooltip("全场最多同时有多少颗碎屑 / 火花")]
    public int maxFx = 400;

    [Header("调试")]
    [Tooltip("Scene 窗口里画每一枪的散布扇形（±2σ，黄）和实际弹道（红）")]
    public bool showSpread = false;

    class Bullet
    {
        public Transform t;
        public Vector2 pos, dir;
        public float speed, damage, traveled, groundDist, ricochet;
        public NpcData byNpc;
        public Enemy byEnemy;
        public AbaOperative byAba;   // ABA 清除小组开的枪：穿过 ABA 队员（不打自己人）
        public float knock;          // 击退（ABA 手枪）
        public int bounces;
    }

    class Fx
    {
        public Transform t;
        public SpriteRenderer sr;
        public Vector2 vel;
        public float age, life, gravity, size;
        public Color color;
    }

    readonly List<Bullet> _bullets = new List<Bullet>();
    readonly List<Fx> _fx = new List<Fx>();
    readonly Dictionary<string, Vector2> _recoil = new Dictionary<string, Vector2>();   // 谁 → (后坐力度数, 记下的时间)
    readonly Dictionary<string, BuildableData> _byName = new Dictionary<string, BuildableData>();
    static Sprite _bulletSprite, _dotSprite, _ricochetSprite, _flashSprite;

    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;

    public static Ballistics Ensure()
    {
        if (Instance != null) return Instance;
        Ballistics b = FindFirstObjectByType<Ballistics>();
        if (b != null) return b;
        return new GameObject("Ballistics").AddComponent<Ballistics>();
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

    // ============ 散布函数 ============

    // NPC 拿手枪时这一枪的散布 σ（度）
    public static float NpcSpread(NpcData n, NpcRig rig, bool moving)
    {
        Ballistics b = Ensure();
        int level = rig != null ? Mathf.Clamp(rig.weaponLevel, 1, 5) : 3;
        float s = b.pistolSpread * At(b.levelSpread, level - 1, 1f);
        float shake = 0f;
        if (rig != null) shake = Mathf.Max(rig.nervous, At(rig.levelTremble, level - 1, 0f));
        s *= 1f + b.trembleMul * Mathf.Clamp01(shake);
        if (n != null)
        {
            if (n.sanity < 20f) s *= b.veryLowSanityMul;
            else if (n.sanity < 40f) s *= b.lowSanityMul;
            if (NpcHealth.Has(n, HealthTag.Fatigued)) s *= b.fatiguedMul;
            if (NpcHealth.Has(n, HealthTag.Injured)) s *= b.injuredMul;
        }
        if (moving) s *= b.movingMul;
        return s + b.RecoilOf(n != null ? n.id : "?");
    }

    // 敌人（武装掠夺者）这一枪的散布 σ（度）
    public static float EnemySpread(Enemy e, bool moving)
    {
        Ballistics b = Ensure();
        float s = b.raiderSpread * (moving ? b.movingMul : 1f);
        return s + b.RecoilOf(EnemyKey(e));
    }

    // 命中率估算（调参用）：σ 度的散布，打 dist 远、半宽 halfWidth 的目标，大概几成能中
    public static float HitChance(float sigmaDeg, float dist, float halfWidth)
    {
        if (sigmaDeg <= 0.0001f) return 1f;
        float half = Mathf.Atan2(halfWidth, Mathf.Max(0.01f, dist)) * Mathf.Rad2Deg;
        return Erf(half / (sigmaDeg * 1.41421356f));
    }

    // ============ 开枪 ============

    // NPC 开一枪：从 from（枪口）打向 target（目标身体中心，targetVel = 目标速度）。打中谁由子弹飞过的路决定。
    public static void FireFromNpc(NpcData n, NpcRig rig, Vector3 from, Vector3 target, Vector3 targetVel, float damage, bool moving)
    {
        Ballistics b = Ensure();
        float speed = rig != null && rig.bulletSpeed > 0f ? rig.bulletSpeed : b.defaultSpeed;
        float sigma = NpcSpread(n, rig, moving);
        Vector2 aim = AimDirection(from, target, targetVel, speed, out float aimDist);
        Vector2 dir = Rotate(aim, Gauss() * sigma);
        b.AddRecoil(n != null ? n.id : "?");
        if (rig != null) rig.MuzzleFlash();
        b.Spawn(from, dir, speed, damage, aimDist, n, null, rig != null ? rig.ricochetChance : 0.3f);
        b.DrawSpread(from, aim, dir, sigma, aimDist);
    }

    // 敌人开一枪（武装掠夺者）
    public static void FireFromEnemy(Enemy e, Vector3 from, Vector3 target, Vector3 targetVel, float damage, bool moving)
    {
        Ballistics b = Ensure();
        float sigma = EnemySpread(e, moving);
        Vector2 aim = AimDirection(from, target, targetVel, b.defaultSpeed, out float aimDist);
        Vector2 dir = Rotate(aim, Gauss() * sigma);
        b.AddRecoil(EnemyKey(e));
        b.Flash(from, dir);
        b.Spawn(from, dir, b.defaultSpeed, damage, aimDist, null, e, 0.3f);
        b.DrawSpread(from, aim, dir, sigma, aimDist);
    }

    // ABA 清除小组开一枪（专属武器，函数见 AbaForce / AbaWeapon）：
    //   σ = 武器散布（已是 5 级手感）× 等级倍数（5 级 = 1）× (边走边打 ? 武器自己的 movingMul : 1) × 体力（< 20：1.2）× 重伤（< 35%：1.15）
    //       + 后坐力（每枪 recoilPerShot，每秒恢复 recoilRecover——训练过，比普通人压得住）
    //   预判误差只有 ±leadError（6~8%）；每一枪有 critChance 的几率打中要害（伤害 × critMul）；手枪子弹带击退。
    public static void FireFromAba(AbaOperative op, Vector3 from, Vector3 target, Vector3 targetVel, AbaWeapon w, bool moving)
    {
        Ballistics b = Ensure();
        if (w == null) return;
        string key = op != null ? "aba#" + op.GetInstanceID() + w.id : "aba";
        int level = AbaForce.Instance != null ? Mathf.Clamp(AbaForce.Instance.weaponLevel, 1, 5) : 5;
        float s = w.spread * At(b.levelSpread, level - 1, 0.25f) / Mathf.Max(0.01f, At(b.levelSpread, 4, 0.25f));
        if (moving) s *= w.movingMul;
        if (op != null && op.Stamina < 20f) s *= 1.2f;
        if (op != null && op.Hp < op.MaxHp * 0.35f) s *= 1.15f;
        float sigma = s + b.RecoilOf(key, w.recoilRecover);
        Vector2 to = target - from;
        float tof = to.magnitude / Mathf.Max(1f, w.bulletSpeed);
        Vector2 aim = to + (Vector2)targetVel * tof * (1f + UnityEngine.Random.Range(-w.leadError, w.leadError));
        float aimDist = aim.magnitude;
        aim = aimDist > 0.0001f ? aim / aimDist : Vector2.right;
        Vector2 dir = Rotate(aim, Gauss() * sigma);
        b.AddRecoil(key, w.recoilPerShot, w.recoilRecover);
        if (op != null && op.Rig != null) op.Rig.MuzzleFlash();
        float dmg = w.damage * (UnityEngine.Random.value < w.critChance ? w.critMul : 1f);
        Bullet bullet = b.Spawn(from, dir, w.bulletSpeed, dmg, Mathf.Max(aimDist, 0.5f), null, null, op != null && op.Rig != null ? op.Rig.ricochetChance : 0.3f);
        bullet.byAba = op;
        bullet.knock = w.knockback;
        // 瞄准射击打偏了也飞得远：落地距离拉到 2.5~4 倍瞄准距离
        bullet.groundDist = Mathf.Clamp(Mathf.Max(aimDist, 0.5f) * UnityEngine.Random.Range(2.5f, 4f), 0.5f, b.maxRange);
        if (!string.IsNullOrEmpty(w.bulletSprite) && bullet.t != null)
        {
            Sprite sp = Resources.Load<Sprite>(w.bulletSprite);
            SpriteRenderer sr = bullet.t.GetComponent<SpriteRenderer>();
            if (sp != null && sr != null) sr.sprite = sp;
        }
        b.DrawSpread(from, aim, dir, sigma, aimDist);
    }

    // a → b 之间有没有挡子弹的东西（墙 / 石头 / 树 / 建筑）。ABA 判断视线、选掩护位置用
    public static bool LineClear(Vector3 a, Vector3 c)
    {
        Ballistics b = Ensure();
        return !b.TraceCells(a, c, out float t, out Vector2 n, out Vector2Int cell, out ImpactMaterial m, out GameObject o);
    }

    // p 周围一圈 8 个小格里有几个能挡子弹（越多越像掩体）
    public static int CoverAround(Vector3 p)
    {
        Ballistics b = Ensure();
        GridManager g = GridManager.Instance;
        if (g == null) return 0;
        Vector2Int c = g.WorldToCell(p);
        int k = 0;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                if (b.Blocking(g, c.x + dx, c.y + dy, out ImpactMaterial m, out GameObject o)) k++;
            }
        return k;
    }

    // 开火线上有没有自己人挡着（NPC 开枪前看一眼：有人挡着就先别开，换个角度）。ally = 挡着的那个人的位置
    public static bool AllyInLine(Vector3 from, Vector3 target, string selfId, out Vector3 ally)
    {
        ally = Vector3.zero;
        WorldState w = World;
        Ballistics b = Instance;
        if (w == null || b == null || !b.friendlyFire) return false;
        Vector2 a = from, c = target;
        float total = Vector2.Distance(a, c);
        float r = b.bodyRadius + 0.12f;
        foreach (NpcData n in w.npcs)
        {
            if (n == null || n.IsDead || n.id == selfId) continue;
            NpcMover m = NpcView.GetMover(n.id);
            if (m == null) continue;
            Vector2 p = m.transform.position;
            float t = SegmentCircle(a, c, p, r);
            if (t >= 0f && t * total < total - 0.3f) { ally = m.transform.position; return true; }
        }
        return false;
    }

    // 瞄准方向（含预判）。aimDist = 瞄准点有多远
    static Vector2 AimDirection(Vector3 from, Vector3 target, Vector3 targetVel, float speed, out float aimDist)
    {
        Ballistics b = Ensure();
        Vector2 to = target - from;
        float tof = to.magnitude / Mathf.Max(1f, speed);                       // 子弹飞过去要多久
        float k = 1f + UnityEngine.Random.Range(-b.leadError, b.leadError);   // 预判也会判错一点
        Vector2 aim = to + (Vector2)targetVel * tof * k;
        aimDist = aim.magnitude;
        return aimDist > 0.0001f ? aim / aimDist : Vector2.right;
    }

    Bullet Spawn(Vector3 from, Vector2 dir, float speed, float damage, float aimDist, NpcData byNpc, Enemy byEnemy, float ricochet)
    {
        GameObject go = new GameObject("Bullet");
        go.transform.position = from;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = BulletSprite();
        sr.sortingOrder = 60;                                                  // 画在人和建筑上面
        RenderMats.MakeUnlit(sr);                                              // 2D 光照：子弹自己发亮
        float g = aimDist * UnityEngine.Random.Range(Mathf.Min(groundAt.x, groundAt.y), Mathf.Max(groundAt.x, groundAt.y));
        Bullet b = new Bullet
        {
            t = go.transform, pos = from, dir = dir, speed = Mathf.Max(1f, speed), damage = damage,
            groundDist = Mathf.Clamp(g, 0.3f, maxRange), ricochet = ricochet, byNpc = byNpc, byEnemy = byEnemy
        };
        _bullets.Add(b);
        return b;
    }

    // ============ 每帧：子弹往前飞、查碰撞 ============

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;                                                  // 暂停时子弹停在半空
        for (int i = _bullets.Count - 1; i >= 0; i--)
            if (!Step(_bullets[i], dt)) { if (_bullets[i].t != null) Destroy(_bullets[i].t.gameObject); _bullets.RemoveAt(i); }
        UpdateFx(dt);
    }

    // 飞一帧。返回 false = 这颗子弹结束了
    bool Step(Bullet b, float dt)
    {
        float step = b.speed * dt;
        bool landing = false;
        float remain = b.groundDist - b.traveled;
        if (step >= remain) { step = Mathf.Max(0f, remain); landing = true; }
        Vector2 a = b.pos, c = a + b.dir * step;

        // 1. 身体（人 / 敌人）
        float best = 2f;
        Enemy hitEnemy = null;
        NpcData hitNpc = null;
        AbaOperative hitAba = null;
        FindBody(b, a, c, ref best, ref hitEnemy, ref hitNpc);
        FindAba(b, a, c, ref best, ref hitEnemy, ref hitNpc, ref hitAba);

        // 2. 挡路的东西（按小格）
        bool wall = TraceCells(a, c, out float tw, out Vector2 normal, out Vector2Int cell, out ImpactMaterial mat, out GameObject obj);
        if (wall && tw < best)
        {
            Vector2 p = Vector2.Lerp(a, c, tw);
            Raise(p, b, normal, mat, obj, cell);
            ImpactFx(p, b.dir, normal, mat);
            // 金属：贴着面斜着打 → 可能弹开
            float incidence = 90f - Vector2.Angle(-b.dir, normal);            // 和那一面的夹角
            if (mat == ImpactMaterial.Metal && b.bounces == 0 && incidence < ricochetAngle && UnityEngine.Random.value < metalRicochet)
            {
                b.dir = Vector2.Reflect(b.dir, normal).normalized;
                b.pos = p + b.dir * 0.02f;
                b.damage *= 0.5f;
                b.bounces++;
                b.traveled += step * tw;
                b.groundDist = b.traveled + UnityEngine.Random.Range(1f, 3f);
                Place(b);
                return true;
            }
            return false;
        }

        if (hitAba != null)
        {
            Vector2 p = Vector2.Lerp(a, c, best);
            Raise(p, b, -b.dir, ImpactMaterial.Flesh, hitAba.gameObject, Vector2Int.zero);
            ImpactFx(p, b.dir, -b.dir, ImpactMaterial.Flesh);
            hitAba.TakeHit(b.damage, b.dir, b.byEnemy, b.byNpc);
            return false;
        }

        if (hitEnemy != null || hitNpc != null)
        {
            Vector2 p = Vector2.Lerp(a, c, best);
            GameObject who = hitEnemy != null ? hitEnemy.gameObject : (NpcView.GetMover(hitNpc.id) != null ? NpcView.GetMover(hitNpc.id).gameObject : null);
            ImpactMaterial m = hitEnemy != null && hitEnemy.Def.kind == EnemyKind.Creature ? ImpactMaterial.Creature : ImpactMaterial.Flesh;
            Raise(p, b, -b.dir, m, who, Vector2Int.zero);
            ImpactFx(p, b.dir, -b.dir, m);
            if (b.byAba != null) { HitByAba(b, hitEnemy, hitNpc, who); return false; }
            if (hitEnemy != null) hitEnemy.TakeDamage(b.damage, b.byNpc);
            else NpcCombat.OnHit(hitNpc, b.damage, b.byEnemy, b.byNpc);
            return false;
        }

        // 3. 什么都没碰到：往前飞；飞到落地距离就落地
        b.pos = c;
        b.traveled += step;
        Place(b);
        if (!landing) return true;
        Raise(c, b, -b.dir, ImpactMaterial.Ground, null, CellOf(c));
        if (UnityEngine.Random.value < b.ricochet) Ricochet(c, b.dir.x < 0f);  // 跳弹（和以前一样的 V 字）
        else ImpactFx(c, b.dir, -b.dir, ImpactMaterial.Ground);
        return false;
    }

    void Place(Bullet b)
    {
        if (b.t == null) return;
        b.t.position = new Vector3(b.pos.x, b.pos.y, b.t.position.z);
        b.t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.dir.y, b.dir.x) * Mathf.Rad2Deg);
    }

    // ABA 的子弹打中人 / 敌人：扣血 + 击退（手枪，体型越大推得越少）+ 打断攻击；打死了告诉开枪的人
    void HitByAba(Bullet b, Enemy hitEnemy, NpcData hitNpc, GameObject who)
    {
        AbaOperative op = b.byAba;
        if (hitEnemy != null)
        {
            hitEnemy.TakeHitFromAba(b.damage, op);
            if (b.knock > 0f && hitEnemy.Alive)
            {
                float mass = Mathf.Max(0.6f, hitEnemy.Def.scale * hitEnemy.Def.scale);
                Knockback.Push(hitEnemy.gameObject, b.dir * (b.knock / mass));
                hitEnemy.Stagger(0.35f / mass);
            }
            if (!hitEnemy.Alive && op != null) op.OnKill(hitEnemy, null);
            return;
        }
        if (hitNpc == null) return;
        NpcCombat.OnHit(hitNpc, b.damage, null, null, op);
        if (b.knock > 0f && !hitNpc.IsDead && who != null) Knockback.Push(who, b.dir * b.knock);
        if (hitNpc.IsDead && op != null) op.OnKill(null, hitNpc);
    }

    // ABA 队员的身体（ABA 自己开的枪穿过队员：不打自己人）
    void FindAba(Bullet b, Vector2 a, Vector2 c, ref float best, ref Enemy hitEnemy, ref NpcData hitNpc, ref AbaOperative hitAba)
    {
        if (b.byAba != null) return;
        foreach (AbaOperative o in AbaOperative.All)
        {
            if (o == null || !o.Alive) continue;
            float t = SegmentCircle(a, c, o.transform.position, bodyRadius);
            if (t >= 0f && t < best) { best = t; hitAba = o; hitEnemy = null; hitNpc = null; }
        }
    }

    // 这一段 a→c 上最先碰到的身体
    void FindBody(Bullet b, Vector2 a, Vector2 c, ref float best, ref Enemy hitEnemy, ref NpcData hitNpc)
    {
        bool shooterIsNpc = b.byNpc != null || b.byAba != null;   // ABA 的子弹和 NPC 一样：打敌人
        foreach (Enemy e in Enemy.All)
        {
            if (e == null || !e.Alive || e == b.byEnemy) continue;
            if (!friendlyFire && !shooterIsNpc) continue;                     // 敌人的流弹不打敌人
            float t = SegmentCircle(a, c, e.transform.position, bodyRadius * Mathf.Max(0.3f, e.Def.scale));
            if (t >= 0f && t < best) { best = t; hitEnemy = e; hitNpc = null; }
        }
        WorldState w = World;
        if (w == null) return;
        foreach (NpcData n in w.npcs)
        {
            if (n == null || n.IsDead || n == b.byNpc) continue;
            if (!friendlyFire && b.byNpc != null) continue;                    // NPC 的流弹不打 NPC（ABA 的子弹照打）
            NpcMover m = NpcView.GetMover(n.id);
            if (m == null) continue;
            float t = SegmentCircle(a, c, m.transform.position, bodyRadius);
            if (t >= 0f && t < best) { best = t; hitNpc = n; hitEnemy = null; }
        }
    }

    // ============ 挡路的东西：沿着线段一格一格查（DDA）============

    bool TraceCells(Vector2 a, Vector2 c, out float tHit, out Vector2 normal, out Vector2Int cell, out ImpactMaterial mat, out GameObject obj)
    {
        tHit = 0f; normal = Vector2.zero; cell = Vector2Int.zero; mat = ImpactMaterial.Stone; obj = null;
        GridManager g = GridManager.Instance;
        if (g == null) return false;
        Vector2 d = c - a;
        if (d.sqrMagnitude < 1e-10f) return false;
        float s = g.cellSize;
        Vector2 o = g.origin;
        int x = Mathf.FloorToInt((a.x - o.x) / s), y = Mathf.FloorToInt((a.y - o.y) / s);
        int stepX = d.x > 0f ? 1 : -1, stepY = d.y > 0f ? 1 : -1;
        float tDeltaX = Mathf.Abs(d.x) > 1e-8f ? Mathf.Abs(s / d.x) : float.MaxValue;
        float tDeltaY = Mathf.Abs(d.y) > 1e-8f ? Mathf.Abs(s / d.y) : float.MaxValue;
        float tMaxX = Mathf.Abs(d.x) > 1e-8f ? ((o.x + (x + (stepX > 0 ? 1 : 0)) * s) - a.x) / d.x : float.MaxValue;
        float tMaxY = Mathf.Abs(d.y) > 1e-8f ? ((o.y + (y + (stepY > 0 ? 1 : 0)) * s) - a.y) / d.y : float.MaxValue;
        for (int i = 0; i < 256; i++)                                          // 出发的那一格不查（枪口可能贴着墙）
        {
            float t;
            if (tMaxX < tMaxY) { t = tMaxX; x += stepX; tMaxX += tDeltaX; normal = new Vector2(-stepX, 0f); }
            else { t = tMaxY; y += stepY; tMaxY += tDeltaY; normal = new Vector2(0f, -stepY); }
            if (t > 1f) return false;
            if (Blocking(g, x, y, out mat, out obj)) { tHit = t; cell = new Vector2Int(x, y); return true; }
        }
        return false;
    }

    // 这一小格挡不挡子弹、是什么材质
    bool Blocking(GridManager g, int x, int y, out ImpactMaterial mat, out GameObject obj)
    {
        mat = ImpactMaterial.Stone;
        obj = null;
        if (!g.InBounds(x, y)) return false;
        Vector2Int c = new Vector2Int(x, y);
        if (TerrainSystem.IsWall(c)) return true;                              // 地形的墙（岩壁）
        ResourceNode r = TerrainSystem.ResourceAt(c);
        if (r != null && r.Blocks)                                             // 挡路的树 / 石头 / 矿石（草、小石子不挡）
        {
            mat = r.kind == ResourceKind.Tree ? ImpactMaterial.Wood : ImpactMaterial.Stone;
            obj = r.gameObject;
            return true;
        }
        string occ = g.GetOccupied(x, y);
        if (string.IsNullOrEmpty(occ)) return false;
        BuildableData d = DataByName(occ);
        if (d == null || !d.blocksMovement) return false;                      // 不挡路的建筑（床、农田……）子弹飞过去
        BuildingFootprint fp = FootprintAt(c);
        if (fp == null || fp.GetComponent<ConstructionSite>() != null) return false;   // 还没建好的规划区
        mat = MaterialOf(d);
        obj = fp.gameObject;
        return true;
    }

    // 建筑是什么材质（按编号 / 名字里的字认；认不出：防御类 = 石头，其他 = 金属）。想改就改这里。
    static readonly string[] MetalWords = { "generator", "turbine", "solar", "battery", "terminal", "pump", "collector", "purifier", "lamp", "heater", "machine", "metal",
                                            "发电", "风力", "太阳", "电池", "终端", "泵", "收集", "净水", "灯", "机", "铁", "金属" };
    static readonly string[] WoodWords = { "box", "crate", "wood", "bed", "table", "chair", "fence", "箱", "木", "床", "桌", "椅", "栅栏" };
    static readonly string[] StoneWords = { "wall", "stone", "rock", "concrete", "brick", "墙", "石", "砖", "混凝土" };

    public static ImpactMaterial MaterialOf(BuildableData d)
    {
        if (d == null) return ImpactMaterial.Stone;
        string key = ((d.id ?? "") + " " + (d.displayName ?? "")).ToLowerInvariant();
        if (Any(key, StoneWords)) return ImpactMaterial.Stone;
        if (Any(key, WoodWords)) return ImpactMaterial.Wood;
        if (Any(key, MetalWords)) return ImpactMaterial.Metal;
        return d.category == BuildableCategory.Defense ? ImpactMaterial.Stone : ImpactMaterial.Metal;
    }

    BuildableData DataByName(string displayName)
    {
        if (_byName.TryGetValue(displayName, out BuildableData d)) return d;
        d = null;
        if (BuildManager.Instance != null && BuildManager.Instance.buildables != null)
            foreach (BuildableData b in BuildManager.Instance.buildables)
                if (b != null && b.displayName == displayName) { d = b; break; }
        _byName[displayName] = d;
        return d;
    }

    static BuildingFootprint FootprintAt(Vector2Int c)
    {
        foreach (BuildingFootprint fp in BuildingFootprint.All)
            if (fp != null && fp.gameObject.activeInHierarchy && fp.Contains(c)) return fp;
        return null;
    }

    static Vector2Int CellOf(Vector2 p)
    {
        GridManager g = GridManager.Instance;
        return g != null ? g.WorldToCell(p) : Vector2Int.zero;
    }

    void Raise(Vector2 p, Bullet b, Vector2 normal, ImpactMaterial mat, GameObject obj, Vector2Int cell)
    {
        if (OnImpact == null) return;
        OnImpact(new Impact
        {
            point = new Vector3(p.x, p.y, 0f), dir = b.dir, normal = normal, power = b.damage, material = mat,
            target = obj, cell = cell, byNpc = b.byNpc, byEnemy = b.byEnemy, byAba = b.byAba
        });
    }

    // ============ 后坐力 ============

    float RecoilOf(string key) => RecoilOf(key, recoilRecover);

    float RecoilOf(string key, float recover)
    {
        if (string.IsNullOrEmpty(key) || !_recoil.TryGetValue(key, out Vector2 r)) return 0f;
        return Mathf.Max(0f, r.x - recover * (Time.time - r.y));
    }

    void AddRecoil(string key) => AddRecoil(key, recoilPerShot, recoilRecover);

    void AddRecoil(string key, float amount, float recover)
    {
        if (string.IsNullOrEmpty(key)) return;
        _recoil[key] = new Vector2(RecoilOf(key, recover) + amount, Time.time);
    }

    static string EnemyKey(Enemy e) => e != null ? "enemy#" + e.GetInstanceID() : "enemy";

    // ============ 特效：碎屑 / 火花 / 木屑 / 尘土 / 血点 ============

    void ImpactFx(Vector2 p, Vector2 dir, Vector2 normal, ImpactMaterial mat)
    {
        Vector2 back = normal.sqrMagnitude > 0.0001f ? normal.normalized : -dir;   // 碎屑往被打的那一面外面溅
        switch (mat)
        {
            case ImpactMaterial.Stone:
                Burst(p, back, 7, new Color(0.62f, 0.6f, 0.56f), 1.2f, 0.35f, 2.5f, 0.05f);   // 石屑（会往下掉）
                Burst(p, back, 4, new Color(0.75f, 0.73f, 0.7f, 0.6f), 0.4f, 0.5f, 0f, 0.09f); // 灰尘
                break;
            case ImpactMaterial.Metal:
                Burst(p, back, 9, new Color(1f, 0.85f, 0.4f), 2.6f, 0.15f, 1.5f, 0.035f);      // 火花
                break;
            case ImpactMaterial.Wood:
                Burst(p, back, 6, new Color(0.55f, 0.38f, 0.2f), 1.1f, 0.4f, 2.5f, 0.05f);     // 木屑
                break;
            case ImpactMaterial.Flesh:
                Burst(p, dir, 5, new Color(0.6f, 0.08f, 0.08f), 0.9f, 0.3f, 2f, 0.045f);       // 血点（顺着子弹方向）
                break;
            case ImpactMaterial.Creature:
                Burst(p, dir, 6, new Color(0.35f, 0.12f, 0.45f), 0.9f, 0.35f, 1.5f, 0.05f);    // 造物的黑紫色碎片
                break;
            default:
                Burst(p, back, 5, new Color(0.55f, 0.5f, 0.42f, 0.8f), 0.5f, 0.45f, 0.5f, 0.07f); // 地上扬起的土
                break;
        }
    }

    // 往 dir 那边喷 count 颗小点：speed 速度、life 秒、gravity 往下掉、size 大小
    void Burst(Vector2 p, Vector2 dir, int count, Color color, float speed, float life, float gravity, float size)
    {
        if (NpcNav.IsUnseen(p)) return;                                        // 迷雾里看不见
        float baseAng = Mathf.Atan2(dir.y, dir.x);
        for (int i = 0; i < count && _fx.Count < maxFx; i++)
        {
            float ang = baseAng + UnityEngine.Random.Range(-1.1f, 1.1f);
            float v = speed * UnityEngine.Random.Range(0.4f, 1.2f);
            GameObject go = new GameObject("ImpactFx");
            go.transform.position = new Vector3(p.x, p.y, 0f);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = DotSprite();
            sr.color = color;
            sr.sortingOrder = 59;
            RenderMats.MakeUnlit(sr);
            float sz = size * UnityEngine.Random.Range(0.6f, 1.4f);
            go.transform.localScale = new Vector3(sz, sz, 1f);
            _fx.Add(new Fx
            {
                t = go.transform, sr = sr, vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * v,
                life = life * UnityEngine.Random.Range(0.7f, 1.3f), gravity = gravity, size = sz, color = color
            });
        }
    }

    void UpdateFx(float dt)
    {
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            Fx f = _fx[i];
            f.age += dt;
            if (f.t == null || f.age >= f.life)
            {
                if (f.t != null) Destroy(f.t.gameObject);
                _fx.RemoveAt(i);
                continue;
            }
            f.vel.y -= f.gravity * dt;
            f.vel *= 1f - Mathf.Min(1f, 3f * dt);                              // 空气阻力
            f.t.position += new Vector3(f.vel.x, f.vel.y, 0f) * dt;
            float k = f.age / f.life;
            Color c = f.color;
            c.a = f.color.a * (1f - k * k);
            f.sr.color = c;
        }
    }

    // 跳弹：地上弹起来的 V 字（和 NpcRig 用同一张图 NpcArt/Fx/Ricochet）
    void Ricochet(Vector2 at, bool left)
    {
        if (NpcNav.IsUnseen(at)) return;
        if (_ricochetSprite == null) _ricochetSprite = Resources.Load<Sprite>("NpcArt/Fx/Ricochet");
        if (_ricochetSprite == null) { ImpactFx(at, Vector2.right, Vector2.up, ImpactMaterial.Metal); return; }
        GameObject go = new GameObject("Ricochet");
        go.transform.position = new Vector3(at.x, at.y, 0f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _ricochetSprite;
        sr.flipX = left;
        sr.sortingOrder = 60;
        RenderMats.MakeUnlit(sr);
        _fx.Add(new Fx { t = go.transform, sr = sr, life = 0.15f, color = Color.white, size = 1f });
    }

    // 敌人开枪的枪火（NPC 的枪火由 NpcRig 画）
    void Flash(Vector3 at, Vector2 dir)
    {
        if (NpcNav.IsUnseen(at)) return;
        if (_flashSprite == null) _flashSprite = Resources.Load<Sprite>("NpcArt/Fx/MuzzleFlash1");
        GameObject go = new GameObject("EnemyMuzzleFlash");
        go.transform.position = at;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 57;
        RenderMats.MakeUnlit(sr);
        if (_flashSprite != null) sr.sprite = _flashSprite;
        else { sr.sprite = DotSprite(); go.transform.localScale = new Vector3(0.18f, 0.12f, 1f); }
        _fx.Add(new Fx { t = go.transform, sr = sr, life = 0.06f, color = new Color(1f, 0.9f, 0.6f, 1f), size = 1f });
    }

    void DrawSpread(Vector3 from, Vector2 aim, Vector2 dir, float sigma, float dist)
    {
        if (!showSpread) return;
        float len = Mathf.Max(1f, dist * 1.3f);
        Debug.DrawLine(from, from + (Vector3)(Rotate(aim, 2f * sigma) * len), Color.yellow, 0.6f);
        Debug.DrawLine(from, from + (Vector3)(Rotate(aim, -2f * sigma) * len), Color.yellow, 0.6f);
        Debug.DrawLine(from, from + (Vector3)(dir * len), Color.red, 0.6f);
    }

    // ============ 图 ============

    // 子弹图：PSD 画的（NpcArt/Fx/Bullet，和 NpcRig 同一张）；没有就用代码画的亮黄小条
    static Sprite BulletSprite()
    {
        if (_bulletSprite != null) return _bulletSprite;
        _bulletSprite = Resources.Load<Sprite>("NpcArt/Fx/Bullet");
        if (_bulletSprite != null) return _bulletSprite;
        const int w = 20, h = 5;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float along = (float)x / (w - 1);
                float across = 1f - Mathf.Abs(y - (h - 1) * 0.5f) / (h * 0.5f);
                t.SetPixel(x, y, new Color(1f, 0.93f, 0.6f, Mathf.Clamp01(along * 1.4f) * Mathf.Clamp01(across * 1.6f)));
            }
        t.Apply();
        _bulletSprite = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(1f, 0.5f), 160f);   // 轴心在弹头
        return _bulletSprite;
    }

    // 碎屑用的小圆点（1 单位大，靠缩放调大小）
    static Sprite DotSprite()
    {
        if (_dotSprite != null) return _dotSprite;
        const int n = 8;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * 3f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        _dotSprite = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        return _dotSprite;
    }

    // ============ 数学 ============

    // 线段 a→c 和圆（center, r）最早相交的位置（0~1；-1 = 不相交）。开枪的人自己的身体在调用前就排除了
    static float SegmentCircle(Vector2 a, Vector2 c, Vector2 center, float r)
    {
        Vector2 d = c - a, f = a - center;
        float A = Vector2.Dot(d, d);
        if (A < 1e-10f) return -1f;
        float B = 2f * Vector2.Dot(f, d);
        float C = Vector2.Dot(f, f) - r * r;
        if (C <= 0f) return 0f;                                                // 起点就在身体里（贴脸开枪 / 有人贴着枪口）
        float disc = B * B - 4f * A * C;
        if (disc < 0f) return -1f;
        float t = (-B - Mathf.Sqrt(disc)) / (2f * A);
        return t >= 0f && t <= 1f ? t : -1f;
    }

    // 正态分布随机数（Box–Muller），限制在 ±3
    static float Gauss()
    {
        float u1 = 1f - UnityEngine.Random.value;
        float u2 = UnityEngine.Random.value;
        float z = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-7f, u1))) * Mathf.Sin(2f * Mathf.PI * u2);
        return Mathf.Clamp(z, -3f, 3f);
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
        return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);
    }

    // 误差函数（近似，误差 < 1.5e-7）
    static float Erf(float x)
    {
        float sign = x < 0f ? -1f : 1f;
        x = Mathf.Abs(x);
        float t = 1f / (1f + 0.3275911f * x);
        float y = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);
        return sign * y;
    }

    static float At(float[] arr, int i, float fallback) => arr != null && i >= 0 && i < arr.Length ? arr[i] : fallback;

    static bool Any(string s, string[] words)
    {
        foreach (string w in words) if (s.Contains(w)) return true;
        return false;
    }
}
