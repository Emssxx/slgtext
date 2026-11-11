using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

// ============================================================================
// 幸存者的小队战术（"团队大脑"，纯规则执行；本地 AI 只下指令）。
//
// 【和 ABA 的区别】ABA 清除小组强在单兵素质；幸存者单兵很弱（老手枪、没护甲），强在配合——
//   共享视野、集火、交叉火力、近战护住射手、放风筝、打不过就退到能守的地方。每个人的数值不变，变的是"怎么打"。
//
// 【谁指挥】
//   · 本地 AI（14B，以领导的身份）：开打时 / 局势大变时（来了强敌、有人倒下……）读一段战况，只回一个指令：
//       engage 迎战 / defend 退回营地防守 / evade 撤退躲开 / hold 原地坚守 / flank 包抄 / auto 自由交战，可附带"集火谁"、"守哪边"、喊一句话。
//     AI 没回 / 回得没法用 → 代码按局势自己定（下面的"自动判断"），战斗从来不等 AI。
//   · 玩家：在对话里说"集火那个大家伙""全员退回营地""包抄""原地守住"等（TryParseOrder），60 秒内比 AI 的指令优先。
//   · 具体每个人站哪、打谁、往哪退，永远由代码算（每 0.3 秒）。
//
// 【自动判断（没有指令时）】按"谁先把谁打光"估算：我方打光敌人要几秒 vs 敌人打光我方要几秒。
//   明显占优 → 迎战；敌人闯进营地 → 就地迎战；打不过但营地围得好 → 退回营地防守；打不过又没地方守 → 撤退。
//
// 【每个人的角色】射手（手枪能打）/ 近战（斧头、锤子，胆子够）/ 平民（空手、胆小）/ 伤员（血少、没体力、致命伤）。
//   伤员和平民：去集合点（离敌人最远、四面通畅、营地里优先、远程敌人看不见的地方），一起躲，不各跑各的。
//
// 【聪明在哪】
//   · 共享视野：任何一个人看见 / 被打，全队都知道是谁、在哪。
//   · 集火：射手一起打同一个目标（危险高、快打死的优先；AI / 玩家点名的最优先）；但有东西扑到某个射手跟前时，他先自卫。
//   · 交叉火力：射手按角度分散站位（彼此错开 50°），不挡彼此的火线，目标躲不到谁的死角。
//   · 选位置：每个候选点打分——离目标的距离合适、看得见目标、旁边有掩体、四面通畅（不进死角）、不挡队友火线、
//     离别的敌人远、走过去不用穿过敌人。对远程敌人（掠夺者 / ABA）掩体加倍重要。
//   · 放风筝：近战的东西扑到 2.4 格内 → 往"开阔、靠近队友 / 营地、离所有敌人都远"的方向退，边退边打；绝不往墙角、死胡同里退。
//   · 近战护卫：拿斧头锤子的人去拦扑向射手 / 平民的近战敌人（最多两人围一个）；打远程敌人从侧面绕过去，不正面冲枪口。
//   · 换弹退后：手枪打空、敌人又在附近 → 先退到队友身后再换。
//   · 防守：射手在营地里靠威胁那一侧、有掩体又看得见敌人的位置；近战守在被拆的墙后面，墙一倒就上；平民待在另一侧。
//     对远程敌人（ABA）：离得远时躲在它看不见的地方，等它走近 / 换弹的时候再探头打（伏击）。
//   · 包抄：射手分两路绕到目标左右两侧，近战从背后上。
// ============================================================================
public class NpcTactics : MonoBehaviour
{
    public static NpcTactics Instance { get; private set; }

    public enum Order { Auto, Engage, Defend, Evade, Hold, Flank }
    public enum Role { None, Shooter, Melee, Civilian, Wounded }
    public enum Act { None, Fight, Flee }

    // 给某个 NPC 的安排（NpcCombat 照着执行）
    public class Plan
    {
        public Act act;
        public Role role;
        public CombatFoe foe;          // 打谁
        public bool hasSpot;           // 要不要走到某个位置
        public Vector3 spot;           // 站哪 / 退到哪
        public bool run;               // 跑过去
        public bool mayFire = true;    // 能不能开火（撤退时只在贴近时还手）
        public float spotAt = -99f;    // 位置是什么时候算的（别每 0.3 秒都换位置）
        public CombatFoe spotFoe;      // 算位置时针对的是谁
        public int slot;               // 交叉火力的角度槽
        public bool stay;              // 原地不动（原地坚守 / 近战守着没贴上来的）
    }

    [Header("范围")]
    public float alertRange = 6f;          // 离任何一个人这么近的敌人算"发现了"
    public float memory = 4f;              // 看不见以后还记得几秒
    [Header("射手")]
    public float idealRange = 3f;          // 拿手枪想离目标多远（手枪射程 4）
    public float kiteDistance = 2.4f;      // 近战的东西扑到这么近就后退
    public float crossfireAngle = 50f;     // 射手之间错开多少度
    [Header("本地 AI 指挥")]
    public bool aiCommand = true;          // 开打 / 局势大变时让本地 AI（领导）下指令
    public float aiMinInterval = 12f;      // 两次问 AI 至少隔几秒
    public float aiOrderSeconds = 45f;     // AI 的指令管多久（之后回到自动判断，局势变了会再问）
    public float playerOrderSeconds = 60f; // 玩家的指令管多久

    public Order Effective { get; private set; } = Order.Auto;   // 现在实际在执行的
    public Order AutoPosture { get; private set; } = Order.Auto; // 代码自己判断的
    public bool InCombat { get; private set; }
    public string CommandBy { get; private set; } = "";

    readonly Dictionary<string, Plan> _plans = new Dictionary<string, Plan>();
    readonly Dictionary<CombatFoe, float> _known = new Dictionary<CombatFoe, float>();
    readonly List<CombatFoe> _foes = new List<CombatFoe>();
    readonly List<NpcData> _team = new List<NpcData>();
    readonly Dictionary<CombatFoe, int> _meleeOn = new Dictionary<CombatFoe, int>();
    Order _commanded = Order.Auto;
    float _commandUntil, _commandPriorityUntil, _think, _aiAt = -99f, _barkAt = -99f, _evalSeed;
    string _focusName = "", _side = "";
    CombatFoe _focus;
    Vector3 _rally;
    int _lastFoeCount, _lastTeamCount, _lastStrong;
    Order _lastEffective = Order.Auto;
    bool _aiPending;

    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;

    public static NpcTactics Ensure()
    {
        if (Instance != null) return Instance;
        NpcTactics t = FindFirstObjectByType<NpcTactics>();
        if (t != null) return t;
        return new GameObject("NpcTactics").AddComponent<NpcTactics>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ============ 对外 ============

    // 这个 NPC 现在的安排（没在打仗 / 不归团队管 → null，NpcCombat 用自己的老规则）
    public static Plan PlanFor(string npcId)
    {
        if (Instance == null || !Instance.InCombat || string.IsNullOrEmpty(npcId)) return null;
        return Instance._plans.TryGetValue(npcId, out Plan p) ? p : null;
    }

    // 下指令（本地 AI / 玩家）。focus = 集火谁（敌人名字的一部分，可空）；side = 守哪边（north/east/south/west，可空）
    public static void Command(Order o, string focus, string side, float seconds, string by, bool fromPlayer)
    {
        NpcTactics t = Ensure();
        if (!fromPlayer && Time.time < t._commandPriorityUntil) return;   // 玩家刚下过：AI 的不覆盖
        t._commanded = o;
        t._commandUntil = Time.time + seconds;
        if (fromPlayer) t._commandPriorityUntil = Time.time + seconds;
        t._focusName = focus ?? "";
        t._side = side ?? "";
        t.CommandBy = by ?? "";
        foreach (Plan p in t._plans.Values) p.spotAt = -99f;            // 换指令：马上重新选位置
        t._think = 0f;
        WorldState w = World;
        string text = AbaForce.LF("tac_log_order", string.IsNullOrEmpty(by) ? "?" : by, OrderName(o) + (string.IsNullOrEmpty(focus) ? "" : AbaForce.LF("tac_log_focus", focus)));
        if (LogManager.Instance != null) LogManager.Instance.AddLog(string.IsNullOrEmpty(by) ? AbaForce.L("tac_name") : by, text, "");
    }

    public static string OrderName(Order o) => AbaForce.L("tac_order_" + o.ToString().ToLowerInvariant());

    // 敌人打了某个人：全队马上知道它
    public static void NoteAttack(CombatFoe f)
    {
        if (Instance == null || f == null || !f.Alive) return;
        Instance._known[f] = Time.time;
    }

    // ============ 每 0.3 秒 ============

    void Update()
    {
        _think -= Time.deltaTime;
        if (_think > 0f) return;
        _think = 0.3f;
        Think();
    }

    void Think()
    {
        WorldState w = World;
        if (w == null) { InCombat = false; return; }
        GatherTeam(w);
        GatherFoes();
        if (_foes.Count == 0 || _team.Count == 0)
        {
            if (InCombat) EndCombat();
            return;
        }
        if (!InCombat)
        {
            InCombat = true;
            _aiAt = -99f;
            _commanded = Order.Auto;            // 上一仗的指令不带到这一仗
            _commandUntil = 0f;
        }
        _rally = RallyPoint();
        AutoPosture = Decide();
        Effective = Time.time < _commandUntil ? _commanded : AutoPosture;
        _focus = PickFocus();
        AssignAll();
        Announce();
        MaybeAskAi();
    }

    void EndCombat()
    {
        InCombat = false;
        _plans.Clear();
        _known.Clear();
        Effective = Order.Auto;
        _lastEffective = Order.Auto;
        _commandUntil = 0f;
        _commandPriorityUntil = 0f;
    }

    // ============ 看 ============

    void GatherTeam(WorldState w)
    {
        _team.Clear();
        foreach (NpcData n in w.npcs)
        {
            if (n == null || n.IsDead || n.isBroken || !NpcHealth.CanMove(n)) continue;
            NpcCombat c = NpcCombat.Of(n.id);
            if (c == null || NpcCombat.UnderPlayerTask(n.id)) continue;      // 正在做玩家交代的事：不归团队管
            _team.Add(n);
        }
        // 已经不在队里的人：撤掉安排
        List<string> gone = null;
        foreach (string id in _plans.Keys)
            if (!_team.Exists(n => n.id == id)) (gone ?? (gone = new List<string>())).Add(id);
        if (gone != null) foreach (string id in gone) _plans.Remove(id);
    }

    void GatherFoes()
    {
        float now = Time.time;
        foreach (Enemy e in Enemy.All)
        {
            if (e == null || !e.Alive) continue;
            if (e.state == Enemy.State.Leave || e.state == Enemy.State.Flee) continue;   // 正在走的不追
            float d = NearestTeamDist(e.transform.position);
            bool camp = Enemy.NearCamp(e.transform.position, 1.5f);
            if ((e.Hostile && (d < alertRange || camp)) || d < 2f) _known[CombatFoe.Of(e)] = now;
        }
        if (AbaForce.Instance == null || AbaForce.Instance.targetSurvivors)
            foreach (AbaOperative o in AbaOperative.All)
            {
                if (o == null || !o.Alive) continue;
                if (NearestTeamDist(o.transform.position) < alertRange + 2f || Enemy.NearCamp(o.transform.position, 2f)) _known[CombatFoe.Of(o)] = now;
            }
        _foes.Clear();
        List<CombatFoe> drop = null;
        foreach (KeyValuePair<CombatFoe, float> kv in _known)
        {
            if (!kv.Key.Alive || now - kv.Value > memory) { (drop ?? (drop = new List<CombatFoe>())).Add(kv.Key); continue; }
            _foes.Add(kv.Key);
        }
        if (drop != null) foreach (CombatFoe f in drop) _known.Remove(f);
    }

    float NearestTeamDist(Vector3 p)
    {
        float best = float.MaxValue;
        foreach (NpcData n in _team) { NpcMover m = NpcView.GetMover(n.id); if (m != null) best = Mathf.Min(best, Dist(m.transform.position, p)); }
        return best;
    }

    // ============ 自动判断：打 / 守 / 撤 ============

    Order Decide()
    {
        float ourDps = 0f, ourHp = 0f;
        foreach (NpcData n in _team)
        {
            if (!NpcHealth.CanFight(n)) continue;
            NpcCombat.Weapon wpn = NpcCombat.WeaponOf(n.id);
            float hit = wpn.ranged ? 0.55f : 0.7f;
            ourDps += wpn.damage / Mathf.Max(0.3f, wpn.interval) * hit;
            ourHp += n.health;
        }
        float armor = EnemyDirector.Instance != null ? EnemyDirector.Instance.suitArmor : 0.85f;
        ourHp /= Mathf.Max(0.2f, armor);
        float foeDps = 0f, foeHp = 0f;
        bool inside = false;
        foreach (CombatFoe f in _foes)
        {
            foeDps += f.Dps;
            foeHp += f.EffectiveHp;
            if (f.InCamp) inside = true;
        }
        float tWin = foeHp / Mathf.Max(0.1f, ourDps);        // 我们打光它们要几秒
        float tLose = ourHp / Mathf.Max(0.1f, foeDps);       // 它们打光我们要几秒
        float adv = tLose / Mathf.Max(0.1f, tWin);
        bool defensible = CampZone.Instance != null && CampZone.Instance.Ready && NpcSecurity.Instance != null && NpcSecurity.Instance.Coverage > 0.5f;
        if (inside) return adv < 0.4f ? Order.Evade : Order.Engage;   // 闯进来了：只能就地打（实在打不过就跑）
        if (adv >= 1.4f) return Order.Engage;
        if (defensible) return Order.Defend;
        if (adv >= 0.9f) return Order.Engage;
        return Order.Evade;
    }

    // 集火目标：点名的 > 危险又快打死的
    CombatFoe PickFocus()
    {
        CombatFoe best = null;
        float bs = float.MinValue;
        foreach (CombatFoe f in _foes)
        {
            float s = f.Dps * (f.AttackingSomeone ? 1.6f : 1f) * (f.InCamp ? 1.5f : 1f) / Mathf.Max(0.3f, f.EffectiveHp / 40f);
            s -= NearestTeamDist(f.Position) * 0.4f;
            if (!string.IsNullOrEmpty(_focusName) && f.Name.Contains(_focusName)) s += 1000f;
            if (s > bs) { bs = s; best = f; }
        }
        return best;
    }

    // ============ 分配 ============

    void AssignAll()
    {
        _meleeOn.Clear();
        int shooterSlot = 0;
        // 先给近战排（要算谁去拦谁），再给射手
        List<NpcData> shooters = new List<NpcData>(), melee = new List<NpcData>(), others = new List<NpcData>();
        foreach (NpcData n in _team)
        {
            Plan p = GetPlan(n.id);
            p.role = RoleOf(n);
            if (p.role == Role.Shooter) shooters.Add(n);
            else if (p.role == Role.Melee) melee.Add(n);
            else others.Add(n);
        }
        foreach (NpcData n in melee) PlanMelee(n, _plans[n.id]);
        foreach (NpcData n in shooters) PlanShooter(n, _plans[n.id], shooterSlot++);
        int k = 0;
        foreach (NpcData n in others) PlanSafe(n, _plans[n.id], k++);
    }

    Plan GetPlan(string id)
    {
        if (!_plans.TryGetValue(id, out Plan p)) { p = new Plan(); _plans[id] = p; }
        return p;
    }

    Role RoleOf(NpcData n)
    {
        NpcCombat.Stance st = NpcCombat.StanceOf(n.id);
        if (!NpcHealth.CanFight(n) || n.energy < 8f) return Role.Wounded;
        float fleeHp = 35f - (n.personality != null ? n.personality.bravery : 50) * 0.2f;
        if (n.health <= fleeHp) return Role.Wounded;
        if (st == NpcCombat.Stance.Hide) return Role.Civilian;
        NpcCombat.Weapon w = NpcCombat.WeaponOf(n.id);
        if (w.ranged) return Role.Shooter;
        if (st == NpcCombat.Stance.Fight) return Role.Melee;
        // 胆量：勇敢 + 武器 + 有人指挥（纪律）+ 一起打的人
        float c = (n.personality != null ? n.personality.bravery : 50) + w.courage - (100f - n.health) * 0.4f;
        if (n.sanity < 40f) c -= 20f;
        if (Effective == Order.Engage || Effective == Order.Flank || Effective == Order.Defend) c += 15f;
        c += 6f * Mathf.Min(3, _team.Count - 1);
        if (w.key == "fists") return c >= 75f ? Role.Melee : Role.Civilian;
        return c >= 35f ? Role.Melee : Role.Civilian;
    }

    // —— 射手 ——
    void PlanShooter(NpcData n, Plan p, int slot)
    {
        Vector3 me = PosOf(n);
        p.act = Act.Fight;
        p.mayFire = true;
        p.run = false;
        p.slot = slot;
        p.stay = false;

        // 1. 有近战的东西扑到跟前：先自卫，往开阔的地方退
        CombatFoe rush = NearestMelee(me, kiteDistance);
        if (rush != null)
        {
            p.foe = rush;
            SetSpot(p, KitePoint(me, rush), true, rush);
            return;
        }
        // 2. 打空了、敌人在附近：退到队友身后再换弹
        CombatFoe near = NearestFoe(me, 99f);
        if (n.pistolAmmo <= 0 && near != null && Dist(near.Position, me) < 3.5f)
        {
            p.foe = near;
            SetSpot(p, FallbackPoint(me), true, near);
            return;
        }
        // 3. 打谁：集火目标打得到就打它，打不到打最近、看得见的
        CombatFoe target = _focus;
        if (target == null || !Visible(me, target) && Dist(target.Position, me) > idealRange + 3f) target = NearestVisible(me) ?? _focus;
        p.foe = target;
        if (target == null) { p.hasSpot = false; return; }

        switch (Effective)
        {
            case Order.Evade:
                p.mayFire = Dist(target.Position, me) < 3f;                    // 撤退：只在贴近时还手
                SetSpot(p, SpreadAround(_rally, slot), true, null);
                break;
            case Order.Hold:
                p.hasSpot = false;                                             // 原地：不挪
                p.stay = true;
                break;
            case Order.Defend:
                if (!target.InCamp) { if (NeedRespot(p, target, me)) SetSpot(p, DefendSpot(me, target, true), Dist(me, NpcNav.CampCenter(me)) > NpcNav.CampRadius, target); }
                else if (NeedRespot(p, target, me)) SetSpot(p, FirePosition(me, target, SlotAngle(slot)), false, target);
                break;
            case Order.Flank:
                if (NeedRespot(p, target, me)) SetSpot(p, FirePosition(me, target, slot % 2 == 0 ? 90f : -90f), true, target);
                break;
            default:   // Engage / Auto
                if (NeedRespot(p, target, me)) SetSpot(p, FirePosition(me, target, SlotAngle(slot)), false, target);
                break;
        }
    }

    // —— 近战 ——
    void PlanMelee(NpcData n, Plan p)
    {
        Vector3 me = PosOf(n);
        p.act = Act.Fight;
        p.mayFire = true;
        p.run = true;
        p.stay = false;

        if (Effective == Order.Evade) { PlanSafe(n, p, 0); return; }

        // 要拦的：扑向射手 / 平民的近战敌人（最多两个人围一个）；没有就打集火目标
        CombatFoe pick = null;
        float bs = float.MaxValue;
        foreach (CombatFoe f in _foes)
        {
            _meleeOn.TryGetValue(f, out int k);
            if (k >= 2) continue;
            float d = Dist(f.Position, me);
            float s = d - (f.Melee && f.AttackingSomeone ? 3f : 0f) - (f == _focus ? 1f : 0f) + (f.Ranged ? 1.5f : 0f) + k * 1.5f;
            if (f.IsStrong && !f.InCamp && !f.BreakingWall) s += 4f;          // 强敌：不主动出去送
            if (s < bs) { bs = s; pick = f; }
        }
        if (pick == null) pick = _focus;
        p.foe = pick;
        if (pick == null) { p.hasSpot = false; return; }
        _meleeOn.TryGetValue(pick, out int c);
        _meleeOn[pick] = c + 1;

        if (Effective == Order.Hold)
        {
            p.hasSpot = false;
            p.stay = Dist(pick.Position, me) > 1.6f;                            // 原地守：没贴上来不出去
            return;
        }
        if (Effective == Order.Defend && !pick.InCamp)
        {
            SetSpot(p, GuardSpot(pick), false, pick);                          // 守在墙后面，墙一倒 / 闯进来就上
            if (Dist(pick.Position, me) < 1.4f) p.hasSpot = false;              // 贴上来了：打
            return;
        }
        // 冲上去打；远程的目标：先从侧面绕，不正面冲枪口
        float d0 = Dist(pick.Position, me);
        if (pick.Ranged && d0 > 2.2f)
        {
            Vector2 toMe = (me - pick.Position);
            Vector2 side = new Vector2(-toMe.y, toMe.x).normalized * (n.id.GetHashCode() % 2 == 0 ? 1f : -1f);
            Vector3 flank = pick.Position + (Vector3)(toMe.normalized * 1.2f + side * 1.6f);
            if (NpcNav.Sample(flank, 0.8f, out Vector3 s)) { SetSpot(p, s, true, pick); return; }
        }
        if (Effective == Order.Flank && d0 > 1.5f)                              // 包抄：从背后上
        {
            Vector2 back = (Vector2)pick.Position - CentroidOfTeam();
            Vector3 behind = pick.Position + (Vector3)(back.sqrMagnitude > 0.01f ? back.normalized * 1.0f : Vector2.zero);
            if (NpcNav.Sample(behind, 0.8f, out Vector3 s)) { SetSpot(p, s, true, pick); return; }
        }
        p.hasSpot = false;                                                      // NpcCombat 的近战会自己冲到它跟前
    }

    // —— 平民 / 伤员：去集合点（一起躲）——
    void PlanSafe(NpcData n, Plan p, int k)
    {
        Vector3 me = PosOf(n);
        p.act = Act.Flee;
        p.foe = NearestFoe(me, 99f);
        p.mayFire = false;
        p.run = true;
        p.stay = false;
        CombatFoe rush = NearestMelee(me, 2.2f);
        if (rush != null && (p.spotAt < 0f || Time.time - p.spotAt > 0.6f)) { SetSpot(p, KitePoint(me, rush), true, rush); return; }
        Vector3 to = SpreadAround(_rally, k + 3);
        if (!p.hasSpot || Dist(p.spot, to) > 1.2f) SetSpot(p, to, true, null);
    }

    // ============ 选位置 ============

    bool NeedRespot(Plan p, CombatFoe f, Vector3 me)
    {
        if (!p.hasSpot || p.spotFoe != f) return true;
        if (Time.time - p.spotAt > 1.2f) return true;
        if (!Visible(p.spot, f) && Dist(me, p.spot) < 0.4f) return true;       // 到了却看不见目标
        return false;
    }

    void SetSpot(Plan p, Vector3 s, bool run, CombatFoe f)
    {
        p.hasSpot = true;
        p.spot = s;
        p.run = run;
        p.spotAt = Time.time;
        p.spotFoe = f;
    }

    // 射手开火位置：围着目标按角度槽站（交叉火力），离目标 idealRange，看得见、有掩体、四面通畅、不挡队友
    Vector3 FirePosition(Vector3 me, CombatFoe target, float angle)
    {
        Vector3 tp = target.Position;
        Vector2 baseDir = me - tp;
        Vector2 toTeam = CentroidOfTeam() - (Vector2)tp;
        if (toTeam.sqrMagnitude > 0.04f) baseDir = toTeam;
        baseDir = baseDir.sqrMagnitude > 0.0001f ? baseDir.normalized : Vector2.right;
        float ideal = target.Melee ? idealRange + 0.4f : idealRange;           // 近战目标：多留点距离
        Vector3 best = me;
        float bs = Score(me, me, target, ideal, false);
        for (int i = 0; i < 9; i++)
        {
            float a = angle + (i == 0 ? 0f : Random.Range(-35f, 35f));
            float r = ideal + (i == 0 ? 0f : Random.Range(-0.8f, 0.8f));
            Vector3 c = tp + (Vector3)(Rot(baseDir, a) * r);
            if (!NpcNav.Sample(c, 0.6f, out c) || !NpcNav.InGrid(c)) continue;
            float s = Score(c, me, target, ideal, false);
            if (s > bs) { bs = s; best = c; }
        }
        return best;
    }

    float Score(Vector3 p, Vector3 me, CombatFoe target, float ideal, bool hide)
    {
        float s = -Mathf.Abs(Dist(p, target.Position) - ideal) * 1.2f;
        bool see = Visible(p, target);
        s += hide ? (see ? -2f : 2f) : (see ? 3f : -5f);
        int cover = Mathf.Min(3, Ballistics.CoverAround(p));
        s += cover * (target.Ranged ? 0.8f : 0.35f);
        int open = Openness(p);
        if (open < 4) s -= (4 - open) * 1.5f;                                   // 死角
        foreach (CombatFoe f in _foes)
        {
            if (f == target) continue;
            float d = Dist(p, f.Position);
            if (f.Melee && d < 2.2f) s -= (2.2f - d) * 2f;
            if (f.Ranged && Visible(p, f)) s -= 0.8f;                           // 被另一个远程敌人看着
        }
        foreach (NpcData n in _team)
        {
            Vector3 q = PosOf(n);
            if (Dist(q, me) < 0.05f) continue;
            if (Dist(q, p) < 0.8f) s -= 1.5f;                                   // 别挤一起
            if (SegDist(p, target.Position, q) < 0.4f) s -= 2f;                 // 队友挡在我的火线上
            if (SegDist(q, target.Position, p) < 0.4f) s -= 2f;                 // 我挡在队友的火线上
        }
        s -= Dist(p, me) * 0.25f;
        if (PathNearFoe(me, p, target)) s -= 3f;                               // 走过去要贴着敌人
        return s;
    }

    // 放风筝：往开阔、离所有敌人都远、靠近队友 / 营地的方向退；看得见追兵更好（边退边打）
    Vector3 KitePoint(Vector3 me, CombatFoe rush)
    {
        Vector2 away = me - rush.Position;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : Random.insideUnitCircle.normalized;
        Vector3 best = me;
        float bs = float.MinValue;
        Vector2 home = (Vector2)_rally - (Vector2)me;
        for (int i = 0; i < 12; i++)
        {
            Vector2 d = Rot(away, Random.Range(-80f, 80f));
            Vector3 c = me + (Vector3)(d * Random.Range(1.8f, 3.2f));
            if (!NpcNav.Sample(c, 0.6f, out c) || !NpcNav.InGrid(c)) continue;
            float s = 0f;
            float minD = float.MaxValue;
            foreach (CombatFoe f in _foes) minD = Mathf.Min(minD, Dist(c, f.Position));
            s += Mathf.Min(minD, 6f) * 1.2f;
            int open = Openness(c);
            s += open * 0.6f;
            if (open < 4) s -= 6f;                                              // 死角：绝不去
            if (home.sqrMagnitude > 0.01f) s += Vector2.Dot(d, home.normalized) * 1.0f;
            if (Visible(c, rush)) s += 1f;
            if (PathNearFoe(me, c, null)) s -= 4f;
            if (s > bs) { bs = s; best = c; }
        }
        return best;
    }

    // 换弹时退到队友身后（队伍重心那边、离敌人远）
    Vector3 FallbackPoint(Vector3 me)
    {
        Vector2 c = CentroidOfTeam();
        Vector2 dir = c - new Vector2(me.x, me.y);
        CombatFoe f = NearestFoe(me, 99f);
        if (f != null) dir += ((Vector2)me - (Vector2)f.Position).normalized * 1.5f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.down;
        Vector3 to = me + (Vector3)(dir * 1.8f);
        return NpcNav.Sample(to, 0.8f, out Vector3 s) && Openness(s) >= 3 ? s : KitePoint(me, f ?? CombatFoe.None);
    }

    // 防守位置（射手）：营地里、靠威胁那一侧、有掩体；远程敌人还远时躲在它看不见的地方（伏击）
    Vector3 DefendSpot(Vector3 me, CombatFoe target, bool shooter)
    {
        Vector3 camp = NpcNav.CampCenter(me);
        Vector2 dir = target.Position - camp;
        if (!string.IsNullOrEmpty(_side)) dir = SideDir(_side);
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.up;
        float R = NpcNav.CampRadius;
        bool ambush = target.Ranged && Dist(target.Position, camp) > R + idealRange + 1f;
        Vector3 best = me;
        float bs = float.MinValue;
        for (int i = 0; i < 10; i++)
        {
            Vector2 d = Rot(dir, Random.Range(-65f, 65f));
            Vector3 c = camp + (Vector3)(d * R * Random.Range(0.35f, 0.85f));
            if (!NpcNav.Sample(c, 0.6f, out c)) continue;
            if (CampZone.Instance != null && CampZone.Instance.Ready && !CampZone.Contains(c, -0.2f)) continue;
            float s = Score(c, me, target, idealRange, ambush);
            s += Ballistics.CoverAround(c) * 0.3f;
            if (s > bs) { bs = s; best = c; }
        }
        return best;
    }

    // 近战守墙：营地里、在敌人和营地中心之间（它在拆墙就站那段墙后面）
    Vector3 GuardSpot(CombatFoe f)
    {
        Vector3 camp = NpcNav.CampCenter(f.Position);
        Vector2 dir = f.Position - camp;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.up;
        float r = f.BreakingWall ? Mathf.Max(0.5f, Dist(f.Position, camp) - 1.1f) : NpcNav.CampRadius * 0.55f;
        Vector3 to = camp + (Vector3)(dir * r);
        return NpcNav.Sample(to, 1f, out Vector3 s) ? s : camp;
    }

    // 集合点：离所有敌人最远、四面通畅、营地里优先、远程敌人看不见
    Vector3 RallyPoint()
    {
        Vector3 camp = NpcNav.CampCenter(Vector3.zero);
        bool foesInside = false;
        foreach (CombatFoe f in _foes) if (f.InCamp) foesInside = true;
        Vector3 best = camp;
        float bs = float.MinValue;
        int tries = 14;
        for (int i = 0; i < tries; i++)
        {
            Vector3 c;
            if (!foesInside) c = camp + (Vector3)(Random.insideUnitCircle * NpcNav.CampRadius * 0.7f);
            else
            {
                Vector2 away = (Vector2)camp - CentroidOfFoes();
                away = away.sqrMagnitude > 0.01f ? away.normalized : Random.insideUnitCircle.normalized;
                c = camp + (Vector3)(Rot(away, Random.Range(-60f, 60f)) * Random.Range(NpcNav.CampRadius * 0.5f, NpcNav.CampRadius + 5f));
            }
            if (!NpcNav.Sample(c, 1f, out c)) continue;
            float s = 0f, minD = float.MaxValue;
            foreach (CombatFoe f in _foes)
            {
                minD = Mathf.Min(minD, Dist(c, f.Position));
                if (f.Ranged && Visible(c, f)) s -= 2f;
            }
            s += Mathf.Min(minD, 10f);
            s += Openness(c) * 0.4f;
            s += Ballistics.CoverAround(c) * 0.3f;
            if (!foesInside && i == 0) s += 0.5f;
            if (s > bs) { bs = s; best = c; }
        }
        return best;
    }

    Vector3 SpreadAround(Vector3 c, int k)
    {
        if (k == 0) return c;
        float a = k * 137.5f;
        Vector3 p = c + (Vector3)(Rot(Vector2.right, a) * (0.5f + 0.15f * (k % 4)));
        return NpcNav.Sample(p, 0.5f, out Vector3 s) ? s : c;
    }

    // 四面通畅：8 个方向里有几个能走、没被挡（< 4 = 死角 / 墙角）
    static int Openness(Vector3 p)
    {
        int k = 0;
        for (int i = 0; i < 8; i++)
        {
            Vector2 d = Rot(Vector2.right, i * 45f);
            Vector3 q = p + (Vector3)(d * 1.1f);
            if (!NpcNav.InGrid(q)) continue;
            if (!NpcNav.Sample(q, 0.25f, out Vector3 s) || Dist(s, q) > 0.3f) continue;
            if (Ballistics.Instance != null && !Ballistics.LineClear(p, q)) continue;
            k++;
        }
        return k;
    }

    // 从 a 走到 b（直线近似）会不会贴着敌人（except 不算）
    bool PathNearFoe(Vector3 a, Vector3 b, CombatFoe except)
    {
        foreach (CombatFoe f in _foes)
        {
            if (f == except) continue;
            float r = f.Melee ? 1.3f : 0.8f;
            if (SegDist(a, b, f.Position) < r && Dist(a, f.Position) > r) return true;
        }
        return false;
    }

    static bool Visible(Vector3 p, CombatFoe f) => Ballistics.Instance == null || Ballistics.LineClear(p, f.Position);

    CombatFoe NearestFoe(Vector3 p, float range)
    {
        CombatFoe best = null;
        float bd = range;
        foreach (CombatFoe f in _foes) { float d = Dist(p, f.Position); if (d < bd) { bd = d; best = f; } }
        return best;
    }

    CombatFoe NearestMelee(Vector3 p, float range)
    {
        CombatFoe best = null;
        float bd = range;
        foreach (CombatFoe f in _foes) { if (!f.Melee) continue; float d = Dist(p, f.Position); if (d < bd) { bd = d; best = f; } }
        return best;
    }

    CombatFoe NearestVisible(Vector3 p)
    {
        CombatFoe best = null;
        float bd = idealRange + 3f;
        foreach (CombatFoe f in _foes) { float d = Dist(p, f.Position); if (d < bd && Visible(p, f)) { bd = d; best = f; } }
        return best;
    }

    float SlotAngle(int slot)
    {
        if (slot == 0) return 0f;
        int k = (slot + 1) / 2;
        return (slot % 2 == 1 ? 1f : -1f) * crossfireAngle * k;               // 0, +50, -50, +100, -100 …
    }

    Vector2 CentroidOfTeam()
    {
        Vector2 s = Vector2.zero;
        int k = 0;
        foreach (NpcData n in _team) { s += (Vector2)PosOf(n); k++; }
        return k > 0 ? s / k : (Vector2)NpcNav.CampCenter(Vector3.zero);
    }

    Vector2 CentroidOfFoes()
    {
        Vector2 s = Vector2.zero;
        foreach (CombatFoe f in _foes) s += (Vector2)f.Position;
        return _foes.Count > 0 ? s / _foes.Count : Vector2.zero;
    }

    static Vector3 PosOf(NpcData n)
    {
        NpcMover m = NpcView.GetMover(n.id);
        return m != null ? m.transform.position : Vector3.zero;
    }

    static Vector2 SideDir(string side)
    {
        switch (side)
        {
            case "north": return Vector2.up;
            case "south": return Vector2.down;
            case "east": return Vector2.right;
            default: return Vector2.left;
        }
    }

    // ============ 喊话 ============

    void Announce()
    {
        bool changed = Effective != _lastEffective;
        _lastEffective = Effective;
        if (!changed || Time.time - _barkAt < 4f) return;
        _barkAt = Time.time;
        NpcData who = Commander();
        if (who == null) return;
        NpcMover m = NpcView.GetMover(who.id);
        if (m == null) return;
        string line = AbaForce.L("tac_call_" + Effective.ToString().ToLowerInvariant());
        if (Effective == Order.Engage && _focus != null && _foes.Count > 1) line = AbaForce.LF("tac_call_focus", _focus.Name);
        NpcSpeechBubble.Show(m.transform, line, 2f);
    }

    // 谁来喊 / 以谁的身份指挥：领导 > 最勇敢的
    NpcData Commander()
    {
        NpcData leader = NpcSociety.Leader;
        if (leader != null && _team.Contains(leader)) return leader;
        NpcData best = null;
        foreach (NpcData n in _team) if (best == null || (n.personality != null && best.personality != null && n.personality.bravery > best.personality.bravery)) best = n;
        return best;
    }

    // ============ 本地 AI 指挥 ============

    void MaybeAskAi()
    {
        if (!aiCommand || _aiPending || Time.time < _commandPriorityUntil) return;
        int strong = 0;
        foreach (CombatFoe f in _foes) if (f.IsStrong) strong++;
        bool bigChange = _foes.Count >= _lastFoeCount + 2 || strong > _lastStrong || _team.Count < _lastTeamCount;
        bool first = _aiAt < 0f;
        bool expired = Time.time >= _commandUntil && Time.time - _aiAt > aiMinInterval * 2f;
        if (!first && !(bigChange && Time.time - _aiAt > aiMinInterval) && !expired) return;
        _lastFoeCount = _foes.Count;
        _lastStrong = strong;
        _lastTeamCount = _team.Count;
        _aiAt = Time.time;
        NpcData cmd = Commander();
        if (cmd == null) return;
        string cmdId = cmd.id, cmdName = cmd.npcName;
        bool jp = LanguageGuard.IsJp;
        _aiPending = true;
        bool queued = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.CombatCommand, key = "tactics", json = true, maxTokens = 120, replacePending = true,
            stillValid = () => InCombat,
            system = () => AiSystem(cmdName, jp),
            user = () => Situation(jp),
            onResult = raw => { _aiPending = false; ApplyAi(raw, cmdId, cmdName); },
        });
        if (!queued) _aiPending = false;
    }

    static string AiSystem(string name, bool jp)
    {
        return (jp
            ? "あなたは拠点の戦闘指揮官（" + name + "として）。下の戦況を読み、全員への戦術指示を一つだけ出す。誰がどこに立つかはこちらで計算するので、方針だけ決めればいい。\n"
            : "你是营地的战斗指挥（以" + name + "的身份）。读下面的战况，给所有人下一个战术指令。每个人具体站哪、怎么打由下面的人自己算，你只定方针。\n") +
            "order: engage（" + (jp ? "迎え撃つ・集中射撃" : "迎战、集火") + "）/ defend（" + (jp ? "拠点に戻って守る" : "退回营地守住") + "）/ evade（" + (jp ? "撤退して隠れる" : "撤退躲开，不硬拼") + "）/ hold（" + (jp ? "その場で踏みとどまる" : "原地坚守") + "）/ flank（" + (jp ? "左右から挟み撃ち" : "分两路包抄") + "）/ auto（" + (jp ? "各自の判断" : "自由交战") + "）\n" +
            (jp ? "focus: 集中して狙う敵の名前（なければ空）。side: 守る方角 north/east/south/west（なければ空）。say: 指揮官が叫ぶ一言（10字以内、日本語）。\n"
                : "focus：集火的敌人名字（没有就空）。side：守哪边 north/east/south/west（没有就空）。say：指挥喊的一句话（10 字以内，中文）。\n") +
            (jp ? "勝てない相手に突っ込ませるな。人命が最優先。" : "别让大家去送死，保住人最要紧。") +
            "\n只输出 JSON：{\"order\":\"...\",\"focus\":\"...\",\"side\":\"...\",\"say\":\"...\"}";
    }

    // 战况（也给领导 / 导演的提示词用）
    public string Situation(bool jp)
    {
        StringBuilder sb = new StringBuilder();
        Vector3 camp = NpcNav.CampCenter(Vector3.zero);
        sb.AppendLine(jp ? "【味方】" : "【我方】");
        foreach (NpcData n in _team)
        {
            Plan p = _plans.TryGetValue(n.id, out Plan pp) ? pp : null;
            NpcCombat.Weapon w = NpcCombat.WeaponOf(n.id);
            Vector3 pos = PosOf(n);
            string where = Enemy.NearCamp(pos, 0f) ? (jp ? "拠点内" : "营地里") : (jp ? "拠点の外" : "营地外") + CampZone.SideName(SideOf(pos), jp);
            sb.Append("・").Append(n.npcName).Append("：").Append(AbaForce.L("weapon_" + w.key))
              .Append(w.key == "pistol" ? "(" + n.pistolAmmo + "+" + NpcInventory.Count(n, "magazine") + (jp ? "本)" : "匣)") : "")
              .Append(jp ? "、体力 " : "，生命 ").Append(Mathf.RoundToInt(n.health)).Append(jp ? "、" : "，").Append(where);
            if (p != null) sb.Append(jp ? "、" : "，").Append(AbaForce.L("tac_role_" + p.role.ToString().ToLowerInvariant()));
            sb.AppendLine();
        }
        sb.AppendLine(jp ? "【敵】" : "【敌人】");
        foreach (CombatFoe f in _foes)
        {
            float d = Dist(f.Position, camp);
            string st = f.BreakingWall ? (jp ? "壁を壊している" : "正在拆墙") : f.InCamp ? (jp ? "拠点に侵入" : "已经闯进营地") : (jp ? "拠点の" : "营地") + CampZone.SideName(SideOf(f.Position), jp) + (jp ? "、約" : "，约 ") + Mathf.RoundToInt(d) + (jp ? "マス" : " 格");
            sb.Append("・").Append(f.Name).Append(jp ? "（" : "（").Append(f.IsStrong ? (jp ? "強敵" : "强敌") : (jp ? "雑魚" : "弱敌")).Append(jp ? "、" : "，")
              .Append(f.Ranged ? (jp ? "銃持ち" : "带枪") : (jp ? "近接" : "近战")).Append(jp ? "、体力 " : "，血 ").Append(Mathf.RoundToInt(f.HpFrac * 100f)).Append("%）：").Append(st).AppendLine();
        }
        float cov = NpcSecurity.Instance != null ? NpcSecurity.Instance.Coverage : 0f;
        sb.Append(jp ? "【拠点の囲い】" : "【营地围墙】").Append(Mathf.RoundToInt(cov * 100f)).AppendLine("%");
        sb.Append(jp ? "【こちらの見立て】" : "【我们的估计】").Append(OrderName(AutoPosture));
        return sb.ToString();
    }

    void ApplyAi(string raw, string cmdId, string cmdName)
    {
        if (string.IsNullOrEmpty(raw) || !InCombat) return;
        try
        {
            int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
            if (a < 0 || b <= a) return;
            JObject o = JObject.Parse(raw.Substring(a, b - a + 1));
            string os = ((string)o["order"] ?? "").Trim().ToLowerInvariant();
            Order order;
            switch (os)
            {
                case "engage": order = Order.Engage; break;
                case "defend": order = Order.Defend; break;
                case "evade": order = Order.Evade; break;
                case "hold": order = Order.Hold; break;
                case "flank": order = Order.Flank; break;
                case "auto": order = Order.Auto; break;
                default: return;
            }
            string focus = ((string)o["focus"] ?? "").Trim();
            string side = ((string)o["side"] ?? "").Trim().ToLowerInvariant();
            if (side != "north" && side != "east" && side != "south" && side != "west") side = "";
            string say = ((string)o["say"] ?? "").Trim();
            if (say.Length > 20) say = say.Substring(0, 20);
            if (focus.Length > 0 && !_foes.Exists(f => f.Name.Contains(focus))) focus = "";   // 点了个不存在的：不管
            if (order == Order.Auto) { _commandUntil = 0f; Effective = AutoPosture; }
            else Command(order, focus, side, aiOrderSeconds, cmdName, false);
            NpcMover m = NpcView.GetMover(cmdId);
            if (m != null && say.Length > 0 && (LocalizationManager.Instance == null || LanguageGuard.TextMatches(say, LocalizationManager.Instance.current, false))) { NpcSpeechBubble.Show(m.transform, say, 2.5f); _barkAt = Time.time; _lastEffective = Effective; }
        }
        catch { }
    }

    // ============ 玩家在对话里下的团队指令 ============

    static readonly string[] FocusWords = { "集火", "集中火力", "先打", "优先打", "集中射撃", "集中攻撃", "先に倒せ", "狙え" };
    static readonly string[] DefendWords = { "退回营地", "撤回营地", "回营地守", "守住营地", "防守", "守营地", "守墙", "拠点を守", "拠点に戻", "守れ", "防衛" };
    static readonly string[] HoldWords = { "原地坚守", "原地守", "守住这里", "顶住", "别退", "その場で", "踏みとどま", "持ちこたえ" };
    static readonly string[] FlankWords = { "包抄", "包围", "两边夹", "夹击", "左右包", "挟み撃ち", "回り込", "包囲" };
    static readonly string[] EvadeWords = { "全员撤退", "大家撤退", "都撤", "撤退", "别硬拼", "全員撤退", "撤退しろ", "引け" };
    static readonly string[] EngageWords = { "全员迎战", "大家迎战", "全员进攻", "一起上", "进攻", "全員でかか", "総攻撃", "突撃" };
    static readonly string[] AutoWords = { "自由交战", "各自为战", "自由に戦", "各自で" };

    // 认出团队指令返回 true（"集火那个畸变体" "全员退回营地" "包抄" "原地守住"）。focus = 点名的敌人（认得出来的话）
    public static bool TryParseOrder(string text, out Order order, out string focus)
    {
        order = Order.Auto;
        focus = "";
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains("?") || text.Contains("？") || text.Contains("吗")) return false;
        bool f = Has(text, FocusWords);
        if (Has(text, FlankWords)) order = Order.Flank;
        else if (Has(text, DefendWords)) order = Order.Defend;
        else if (Has(text, HoldWords)) order = Order.Hold;
        else if (Has(text, EvadeWords)) order = Order.Evade;
        else if (Has(text, AutoWords)) order = Order.Auto;
        else if (Has(text, EngageWords) || f) order = Order.Engage;
        else return false;
        if (f || order == Order.Engage || order == Order.Flank)
        {
            foreach (Enemy e in Enemy.All) if (e != null && e.Alive && text.Contains(e.Name)) { focus = e.Name; break; }
            if (focus.Length == 0 && AbaOperative.All.Count > 0 && (text.Contains("ABA") || text.Contains("清除") || text.Contains("掃討"))) focus = AbaForce.L("aba_short");
            if (focus.Length == 0 && (text.Contains("大家伙") || text.Contains("大的") || text.Contains("大物") || text.Contains("でかい")))
                foreach (Enemy e in Enemy.All) if (e != null && e.Alive && e.Def.IsStrong) { focus = e.Name; break; }
        }
        return true;
    }

    static bool Has(string text, string[] words)
    {
        foreach (string w in words) if (text.IndexOf(w, System.StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    // ============ 小工具 ============

    static string SideOf(Vector3 p)
    {
        Vector3 d = p - NpcNav.CampCenter(p);
        if (Mathf.Abs(d.x) > Mathf.Abs(d.y)) return d.x >= 0f ? "east" : "west";
        return d.y >= 0f ? "north" : "south";
    }

    static float Dist(Vector3 a, Vector3 b) => Vector2.Distance(a, b);

    static float SegDist(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 d = b - a;
        float l2 = d.sqrMagnitude;
        if (l2 < 0.0001f) return Vector2.Distance(a, p);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / l2);
        return Vector2.Distance(a + d * t, p);
    }

    static Vector2 Rot(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
}

// ============================================================================
// 幸存者要打的对象：一个敌人（Enemy）或一名 ABA 队员（AbaOperative），统一成一样的接口。
// 同一个敌人永远是同一个 CombatFoe（可以拿来当字典的键、比较是不是同一个）。
// ============================================================================
public class CombatFoe
{
    public readonly Enemy enemy;
    public readonly AbaOperative aba;
    static readonly Dictionary<Object, CombatFoe> _cache = new Dictionary<Object, CombatFoe>();
    public static readonly CombatFoe None = new CombatFoe(null, null);

    CombatFoe(Enemy e, AbaOperative a) { enemy = e; aba = a; }

    public static CombatFoe Of(Enemy e)
    {
        if (e == null) return null;
        if (!_cache.TryGetValue(e, out CombatFoe f)) { Prune(); f = new CombatFoe(e, null); _cache[e] = f; }
        return f;
    }

    public static CombatFoe Of(AbaOperative a)
    {
        if (a == null) return null;
        if (!_cache.TryGetValue(a, out CombatFoe f)) { Prune(); f = new CombatFoe(null, a); _cache[a] = f; }
        return f;
    }

    static void Prune()
    {
        if (_cache.Count < 64) return;
        List<Object> dead = new List<Object>();
        foreach (KeyValuePair<Object, CombatFoe> kv in _cache) if (kv.Key == null) dead.Add(kv.Key);
        foreach (Object k in dead) _cache.Remove(k);
    }

    public bool Alive => enemy != null ? enemy.Alive : (aba != null && aba.Alive);
    public Transform Transform => enemy != null ? enemy.transform : (aba != null ? aba.transform : null);
    public Vector3 Position { get { Transform t = Transform; return t != null ? t.position : new Vector3(99999f, 99999f, 0f); } }
    public Vector3 Velocity => enemy != null ? enemy.Velocity : (aba != null ? aba.Velocity : Vector3.zero);
    public string Name => enemy != null ? enemy.Name : (aba != null ? AbaForce.L("aba_short") : "?");
    public float Scale => enemy != null && enemy.Def != null ? enemy.Def.scale : 1f;
    public bool IsStrong => enemy != null ? enemy.Def != null && enemy.Def.IsStrong : aba != null;
    public bool Ranged => enemy != null ? enemy.Def != null && enemy.Def.Ranged : aba != null;
    public bool Melee => !Ranged;
    public bool BreakingWall => enemy != null && enemy.state == Enemy.State.BreakWall;
    public bool InCamp => Alive && Enemy.NearCamp(Position, 0f);
    public float HpFrac => enemy != null ? enemy.Hp / Mathf.Max(1f, enemy.Def.maxHp) : (aba != null ? aba.Hp / Mathf.Max(1f, aba.MaxHp) : 0f);
    public bool AttackingSomeone => enemy != null ? (enemy.Target != null && enemy.state == Enemy.State.Attack) : (aba != null && aba.target != null);
    public float Range => enemy != null ? enemy.Def.attackRange : (aba != null && aba.Weapon != null ? aba.Weapon.range : 6f);

    // 每秒能打多少血（估算）
    public float Dps
    {
        get
        {
            if (enemy != null) { EnemyDef d = enemy.Def; return d.damage / Mathf.Max(0.3f, d.attackInterval) * (d.Ranged ? 0.6f : 0.75f); }
            if (aba != null && aba.Weapon != null) { AbaWeapon w = aba.Weapon; return w.damage * 3f / (w.shotInterval * 2f + w.pause) * 0.85f; }
            return 0f;
        }
    }

    // 要打多少血才能打死（ABA 有护甲）
    public float EffectiveHp
    {
        get
        {
            if (enemy != null) return enemy.Hp;
            if (aba != null) return aba.Hp / Mathf.Max(0.1f, 1f - (AbaForce.Instance != null ? AbaForce.Instance.armor : 0.4f));
            return 0f;
        }
    }

    // 近战打它一下
    public void TakeMelee(float dmg, NpcData by)
    {
        if (enemy != null) { enemy.TakeDamage(dmg, by); return; }
        if (aba != null)
        {
            NpcMover m = by != null ? NpcView.GetMover(by.id) : null;
            Vector2 dir = m != null ? (Vector2)(aba.transform.position - m.transform.position) : Vector2.right;
            aba.TakeHit(dmg, dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right, null, by);
        }
    }
}
