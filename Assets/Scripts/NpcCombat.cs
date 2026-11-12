using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// NPC 遇敌（纯规则，不走 LLM）：打还是躲。由 EnemyDirector 自动挂到每个 NPC 身上，不用改预制体。
//
// 【什么时候管】身边 alertRange 以内有活着的敌人，或者刚挨了打。敌人走远 / 死了，安静 2.5 秒后放手。
//   玩家刚下的指令（60 秒内、还在执行）优先：照玩家说的做，不自己去打 / 躲（想让他打 / 躲，就对他说"迎战" / "快躲"）。
//
// 【打还是躲】
//   · 打不了（致命伤 / 濒死）、血少（和逃跑阈值一样：35 - 勇敢×0.2）、没体力 → 躲。
//   · 有人下了姿态（玩家在对话里说"迎战" / "快躲"，或者以后由领导 AI 下）→ 照做。
//   · 自己判断：胆量 = 勇敢 + 武器（枪 +20 / 斧 +15 / 锤 +10 / 空手 -10）+ 旁边一起打的人×8
//                  - 掉的血×0.4 - 理智低 20 - 受伤 15（已经打起来了 +10，不轻易掉头）
//       弱敌：胆量 ≥ 30 就上，不然躲。
//       强敌：以防守为主——默认躲回营地；只有它在拆墙 / 已经闯进营地，并且胆量 ≥ 70（拿枪的 ≥ 55）才上去打。
//
// 【武器】看物品栏里带着什么（NpcInventory，挂件也按它显示）：手枪（远程，要有子弹或弹匣）> 斧头 > 锤子 > 空手。
//   手枪一发用一颗子弹（NpcData.pistolAmmo），打空了换一个弹匣（时间 = NpcRig 换弹动画的长度）；弹匣也用光了就换成近战。
//   敌人离得远：战术换弹（旧弹匣收回物品栏，变成空弹匣）；敌人离得近（或物品栏放不下空弹匣）：快速换弹（旧弹匣丢在地上）。
//   【弹道】手枪不掷命中率，走 Ballistics：瞄准点（含预判）+ 正态分布的偏角，子弹真的飞过去，路上先碰到什么就打中什么
//     （敌人、挡在中间的自己人、墙 / 树 / 石头；打偏了落在目标后面的地上）。散布 σ 由函数算：
//     手枪基础 × 武器操作等级（NpcRig.weaponLevel：1 级 ×4 … 5 级 ×¼）× 手抖 × 理智 × 疲劳 × 受伤 × 边走边打 + 后坐力，数值在 Ballistics 上调。
//     开枪前看一眼开火线：有自己人挡着就先不开，往旁边挪一步换个角度（Ballistics.friendlyFire 关掉时不用看）。
// 【挨打】伤害 × 防护服减伤（EnemyDirector.suitArmor）；一场仗里累计掉血到 injureAfterDamage → 「受伤」（伤在手臂 / 肋骨 / 大腿）。
// 【占用】打 / 躲期间在 NpcBusy 登记 Combat（优先级 90）：施工、需求行为、闲置行为、自主干活都会让路。
// 【以后接 AI】SetStance(npcId, 姿态, 秒数) 就是给领导（14B）/ 玩家下"打 / 躲"用的口子；不下就按上面的规则自己判断。
// 【小队战术】NpcTactics（团队大脑）在打仗时给每个人排好：角色、打谁、站哪 / 退到哪、能不能开火（集火、交叉火力、放风筝、
//   近战护卫、退回营地防守……，本地 AI 只下方针）。有安排时照安排执行（这里只管走过去、瞄准、开枪 / 挥砍、换弹）；
//   没有安排（团队大脑不在 / 没打仗）时用上面的老规则。要打的对象可以是敌人，也可以是 ABA 队员（CombatFoe）。
// ============================================================================
[DisallowMultipleComponent]
public class NpcCombat : MonoBehaviour
{
    public enum Stance { Auto, Fight, Hide }
    public enum Mode { None, Fight, Flee }

    public struct Weapon
    {
        public string key;      // pistol / axe / hammer / fists（名字：weapon_<key>）
        public float damage, range, interval;
        public int courage;     // 胆量加成
        public bool ranged;
    }

    static readonly Weapon Pistol = new Weapon { key = "pistol", damage = 9f, range = 4f, interval = 1.4f, courage = 20, ranged = true };
    static readonly Weapon Axe = new Weapon { key = "axe", damage = 11f, range = 0.85f, interval = 1.25f, courage = 15 };
    static readonly Weapon Hammer = new Weapon { key = "hammer", damage = 8f, range = 0.8f, interval = 1.1f, courage = 10 };
    static readonly Weapon Fists = new Weapon { key = "fists", damage = 3.5f, range = 0.7f, interval = 1.0f, courage = -10 };

    static readonly Dictionary<string, NpcCombat> _byId = new Dictionary<string, NpcCombat>();
    static readonly Dictionary<string, KeyValuePair<Stance, float>> _stances = new Dictionary<string, KeyValuePair<Stance, float>>();
    static readonly Dictionary<string, float> _playerOrderAt = new Dictionary<string, float>();

    public Mode Current { get; private set; }
    public Enemy Foe => _foe != null ? _foe.enemy : null;   // 正在打的敌人（打的是 ABA 队员时为 null）
    public CombatFoe Target => _foe;                         // 正在打的对象（敌人或 ABA 队员）

    NpcView _view;
    NpcMover _mover;
    NpcTaskRunner _runner;
    CombatFoe _foe, _lastAttacker;
    NpcTactics.Plan _plan;   // 团队大脑给的安排（null = 自己判断）
    float _decide, _attackCd, _calm, _repath, _barkCd, _logCd, _lastHit = -99f, _hurt;
    float _flankUntil;     // 开火线被自己人挡着、正在往旁边挪（这段时间不站定）
    bool _claimed, _injuredThisFight, _hasDest;
    string _claimId;
    Vector3 _dest;

    string Id => _view != null ? _view.npcId : null;
    NpcData Data => WorldManager.Instance != null && !string.IsNullOrEmpty(Id) ? WorldManager.Instance.GetNpcById(Id) : null;

    void Awake()
    {
        _view = GetComponent<NpcView>();
        _mover = GetComponent<NpcMover>();
        _runner = GetComponent<NpcTaskRunner>();
        _decide = Random.value * 0.3f;   // 错开，别所有人同一帧想
    }

    void OnDisable()
    {
        Release();
        string id = Id;
        if (!string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out NpcCombat c) && c == this) _byId.Remove(id);
    }

    // ============ 对外 ============

    public static NpcCombat Of(string npcId)
    {
        return !string.IsNullOrEmpty(npcId) && _byId.TryGetValue(npcId, out NpcCombat c) && c != null ? c : null;
    }

    public static bool InCombat(string npcId) { NpcCombat c = Of(npcId); return c != null && c.Current != Mode.None; }
    public static bool IsFleeing(string npcId) { NpcCombat c = Of(npcId); return c != null && c.Current == Mode.Flee; }
    public static bool IsFighting(string npcId) { NpcCombat c = Of(npcId); return c != null && c.Current == Mode.Fight; }

    // 下姿态：迎战 / 躲起来 / 自己判断（Auto = 撤销）。玩家在对话里下，以后领导 AI 也走这里。
    public static void SetStance(string npcId, Stance s, float seconds = 180f)
    {
        if (string.IsNullOrEmpty(npcId)) return;
        if (s == Stance.Auto) _stances.Remove(npcId);
        else _stances[npcId] = new KeyValuePair<Stance, float>(s, Time.time + seconds);
        NpcCombat c = Of(npcId);
        if (c != null) c._decide = 0f;
    }

    public static Stance StanceOf(string npcId)
    {
        if (!string.IsNullOrEmpty(npcId) && _stances.TryGetValue(npcId, out KeyValuePair<Stance, float> kv))
        {
            if (Time.time < kv.Value) return kv.Key;
            _stances.Remove(npcId);
        }
        return Stance.Auto;
    }

    // 玩家刚给他下了指令（NpcAgent 调）：之后 60 秒内只要指令还在执行，就不自己去打 / 躲
    public static void NotePlayerOrder(string npcId)
    {
        if (!string.IsNullOrEmpty(npcId)) _playerOrderAt[npcId] = Time.time;
    }

    // 正在做玩家交代的事（团队大脑不调动他）
    public static bool UnderPlayerTask(string npcId)
    {
        NpcCombat c = Of(npcId);
        return c != null && PlayerOrderActive(npcId, c._runner);
    }

    static bool PlayerOrderActive(string npcId, NpcTaskRunner r)
    {
        return r != null && r.IsBusy && !string.IsNullOrEmpty(npcId)
               && _playerOrderAt.TryGetValue(npcId, out float t) && Time.time - t < 60f;
    }

    // 看他物品栏里带着什么（手枪要有子弹或弹匣才算）
    public static Weapon WeaponOf(string npcId)
    {
        NpcData n = WorldManager.Instance != null && !string.IsNullOrEmpty(npcId) ? WorldManager.Instance.GetNpcById(npcId) : null;
        if (n != null)
        {
            if (NpcInventory.PistolReady(n)) return Pistol;
            if (NpcInventory.Has(n, "axe")) return Axe;
            if (NpcInventory.Has(n, "hammer")) return Hammer;
        }
        return Fists;
    }

    const float ReloadTime = 2.1f;   // 换弹匣要几秒（没有 NpcRig 外观时才用；有外观时按换弹动画的长度）
    const float SpeedReloadRange = 0.6f;   // 敌人比"射程 × 这个数"还近：来不及收旧弹匣，快速换弹
    // 敌人打中了这个 NPC（Enemy 近战调）。近战没有子弹，Ballistics 不会放着弹点效果，所以记下"这一下是近战"，让外观自己放
    static bool _meleeHit;
    public static void OnEnemyHit(NpcData n, float damage, Enemy by)
    {
        _meleeHit = true;
        try { OnHit(n, damage, by, null); }
        finally { _meleeHit = false; }
    }

    // 这个 NPC 挨了一下：byEnemy = 敌人打的；byNpc = 自己人的流弹（Ballistics 调）
    public static void OnHit(NpcData n, float damage, Enemy byEnemy, NpcData byNpc, AbaOperative byAba = null)
    {
        if (n == null || n.IsDead) return;
        Enemy by = byEnemy;
        if (byNpc != null && byEnemy == null)
        {
            NpcCombat shooter = Of(byNpc.id);
            Log(n, LF("combat_log_friendly", n.npcName, byNpc.npcName), true);   // 被自己人的流弹打中
            if (shooter != null && shooter._barkCd <= 0f) shooter.Bark(LanguageGuard.Pick("糟了，打到自己人了！", "しまった、味方に当たった！"));
        }
        EnemyDirector dir = EnemyDirector.Instance;
        float d = damage * (dir != null ? dir.suitArmor : 0.85f);
        n.health = Mathf.Clamp(n.health - d, 0f, 100f);
        NpcSecurity.NotifyDanger(2f);
        NpcCombat c = Of(n.id);
        float hurt = d;
        if (c != null)
        {
            c._lastHit = Time.time;
            c._lastAttacker = by != null ? CombatFoe.Of(by) : (byAba != null ? CombatFoe.Of(byAba) : null);
            NpcTactics.NoteAttack(c._lastAttacker);   // 全队马上知道是谁在打人
            c._hurt += d;
            c._decide = 0f;   // 马上重新想（还手还是跑）
            hurt = c._hurt;
            if (c._barkCd <= 0f) c.Bark(Pick(HurtZh, HurtJa));
        }
        float limit = dir != null ? dir.injureAfterDamage : 18f;
        if (!n.IsDead && hurt >= limit && (c == null || !c._injuredThisFight))
        {
            bool wasInjured = NpcHealth.Has(n, HealthTag.Injured);
            NpcHealth.MarkInjured(n, InjuryCause.Combat);
            if (c != null) c._injuredThisFight = true;
            if (!wasInjured) Log(n, LF("combat_log_injured", n.npcName, by != null ? by.Name : (byNpc != null ? byNpc.npcName : (byAba != null ? AbaForce.L("aba_name") : "?")), NpcHealth.InjuryPart(n, LanguageGuard.IsJp)), true);
        }
        if (n.IsDead) EnemyDirector.OnNpcKilled(n, by, byAba != null ? AbaForce.L("aba_name") : null);
        PlayRigHit(n, damage, byEnemy, byNpc, byAba);   // 外观：受击动画；这一下打死了就倒下
    }

    // 外观（NpcRig）：被往打来的方向推一下；打死了就朝那个方向倒下、武器脱手。伤害越大推得越狠
    static void PlayRigHit(NpcData n, float damage, Enemy byEnemy, NpcData byNpc, AbaOperative byAba)
    {
        NpcMover m = NpcView.GetMover(n.id);
        NpcRig rig = m != null ? m.GetComponent<NpcRig>() : null;
        if (rig == null) return;                                                  // 没有新外观
        Vector3 me = m.transform.position, from = me;
        if (byEnemy != null) from = byEnemy.transform.position;                   // 谁打的：从他那边打过来
        else if (byAba != null) from = byAba.transform.position;
        else if (byNpc != null) { NpcMover s = NpcView.GetMover(byNpc.id); if (s != null) from = s.transform.position; }
        Vector2 dir = new Vector2(me.x - from.x, me.y - from.y);                  // 打过来的方向（不知道是谁就当从正面）
        float k = Mathf.Clamp(damage / 10f, 0.5f, 1.8f);                          // 轻重：10 点伤害 = 正常的一下
        bool fx = _meleeHit;                                                      // 子弹打中时 Ballistics 已经放了着弹点效果；近战才让外观放
        bool knock = !(byAba != null && byAba.Weapon != null && byAba.Weapon.id == "rifle");   // 步枪不击退：只是上身一震，人不被推走
        if (n.IsDead) rig.PlayDeath(dir, k, rig.HitPoint(dir), fx, knock);
        else rig.PlayHit(dir, k, rig.HitPoint(dir), fx, knock);
    }

    // ============ 每帧 ============

    void Update()
    {
        string id = Id;
        if (string.IsNullOrEmpty(id)) return;
        if (!_byId.TryGetValue(id, out NpcCombat me) || me != this) _byId[id] = this;   // id 是生成后才设的，在这里登记
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        NpcData n = Data;
        if (n == null) return;
        if (n.IsDead || n.isBroken || !NpcHealth.CanMove(n))
        {
            if (Current != Mode.None) Stop(n, false);
            return;
        }
        _attackCd -= dt;
        _repath -= dt;
        _barkCd -= dt;
        _logCd -= dt;
        _decide -= dt;
        if (_decide <= 0f) { _decide = 0.3f; Decide(n); }
        // 打 / 躲的时候，自己的任务（领导分的活等）又被排上了：停掉，免得两边抢着走
        if (Current != Mode.None && _runner != null && _runner.IsBusy)
        {
            if (PlayerOrderActive(id, _runner)) { Stop(n, false); return; }
            _runner.CancelAll();
        }
        if (Current == Mode.Fight) DoFight(n, dt);
        else if (Current == Mode.Flee) DoFlee();
    }

    void Decide(NpcData n)
    {
        // 团队大脑在打仗：照它的安排（玩家刚交代的事除外）
        _plan = NpcTactics.PlanFor(Id);
        if (_plan != null)
        {
            if (PlayerOrderActive(Id, _runner)) { if (Current != Mode.None) Stop(n, false); return; }
            CombatFoe pf = _plan.foe != null && _plan.foe.Alive ? _plan.foe : null;
            Mode pm = _plan.act == NpcTactics.Act.Flee ? Mode.Flee : _plan.act == NpcTactics.Act.Fight ? Mode.Fight : Mode.None;
            if (pm == Mode.Fight && pf == null) pm = _plan.hasSpot ? Mode.Flee : Mode.None;   // 没有可打的：走到位置上就好
            if (pm == Mode.None)
            {
                if (Current != Mode.None) { _calm += 0.3f; if (_calm >= 1f) Stop(n, true); }
                return;
            }
            _calm = 0f;
            if (pm != Current || pf != _foe) Begin(n, pm, pf);
            return;
        }

        Vector3 pos = transform.position;
        float alert = EnemyDirector.AlertRange;
        CombatFoe threat = CombatFoe.Of(Enemy.NearestTo(pos, alert, true, true));
        bool hitRecently = Time.time - _lastHit < 4f && _lastAttacker != null && _lastAttacker.Alive;
        if (threat == null && hitRecently) threat = _lastAttacker;
        // 正在打的那个还活着、没跑远：接着盯它（不要一会儿换一个）
        if (Current == Mode.Fight && _foe != null && _foe.Alive && Dist(pos, _foe.Position) < alert * 1.6f) threat = _foe;

        if (threat == null)
        {
            if (Current != Mode.None)
            {
                _calm += 0.3f;
                if (_calm >= 2.5f) Stop(n, true);
            }
            return;
        }
        _calm = 0f;

        // 玩家刚下的指令还在执行：先听玩家的
        if (PlayerOrderActive(Id, _runner))
        {
            if (Current != Mode.None) Stop(n, false);
            return;
        }

        Mode want = Choose(n, threat);
        if (want != Current || threat != _foe) Begin(n, want, threat);
    }

    Mode Choose(NpcData n, CombatFoe foe)
    {
        if (!NpcHealth.CanFight(n)) return Mode.Flee;
        float fleeHp = 35f - n.personality.bravery * 0.2f;   // 和 NpcDecisionRules 的逃跑阈值一致
        if (n.health <= fleeHp || n.energy < 8f) return Mode.Flee;
        Stance st = StanceOf(Id);
        if (st == Stance.Hide) return Mode.Flee;
        if (st == Stance.Fight) return Mode.Fight;
        float c = Courage(n, foe);
        if (!foe.IsStrong) return c >= 30f ? Mode.Fight : Mode.Flee;
        // 强敌：以防守为主
        bool defend = Enemy.NearCamp(transform.position, 0.5f) && (foe.BreakingWall || foe.InCamp);
        if (defend && c >= (WeaponOf(Id).ranged ? 55f : 70f)) return Mode.Fight;
        return Mode.Flee;
    }

    float Courage(NpcData n, CombatFoe foe)
    {
        float c = n.personality.bravery + WeaponOf(Id).courage;
        c += 8f * Mathf.Min(3, AlliesFighting());
        c -= (100f - n.health) * 0.4f;
        if (n.sanity < 40f) c -= 20f;
        if (NpcHealth.Has(n, HealthTag.Injured)) c -= 15f;
        if (Current == Mode.Fight) c += 10f;
        return c;
    }

    int AlliesFighting()
    {
        int k = 0;
        Vector3 p = transform.position;
        foreach (NpcCombat c in _byId.Values)
            if (c != null && c != this && c.Current == Mode.Fight && Dist(c.transform.position, p) < 5f) k++;
        return k;
    }

    void Begin(NpcData n, Mode m, CombatFoe foe)
    {
        bool fresh = Current == Mode.None;
        bool changed = m != Current;
        // 手上的活先放下（领导分的工 / 自己找的事；玩家的指令不会走到这里）
        if (_runner != null && _runner.IsBusy) _runner.CancelAll();
        if (!_claimed)
        {
            _claimId = Id;
            NpcBusy.Claim(_claimId, NpcBusy.Combat);
            _claimed = true;
        }
        NpcIdleBrain.InterruptFor(Id, 3f);
        Current = m;
        _foe = foe;
        _hasDest = false;
        _repath = 0f;
        if (fresh) { _hurt = 0f; _injuredThisFight = false; }
        if (!changed) return;

        if (m == Mode.Fight)
        {
            Weapon w = WeaponOf(Id);
            string fn = foe != null ? foe.Name : "?";
            n.currentAction = LF("combat_act_fight", fn);
            Log(n, LF("combat_log_fight", n.npcName, fn, L("weapon_" + w.key)));
            Bark(Pick(FightZh, FightJa));
        }
        else
        {
            string fn = foe != null ? foe.Name : "?";
            n.currentAction = LF("combat_act_flee", fn);
            Log(n, LF("combat_log_flee", n.npcName, fn));
            Bark(Pick(FleeZh, FleeJa));
        }
    }

    void DoFight(NpcData n, float dt)
    {
        if (_foe == null || !_foe.Alive) { _foe = null; _decide = 0f; return; }
        if (_mover == null) return;
        NpcHealth.ConsumeCombat(n, dt);
        Weapon w = WeaponOf(Id);
        Vector3 pos = transform.position, fp = _foe.Position;
        float d = Dist(pos, fp);
        float reach = w.range + Mathf.Max(0f, _foe.Scale - 1f) * 0.35f;       // 大块头好打到一点
        if (_plan != null && (_plan.hasSpot || _plan.stay))
        {
            // 团队安排的位置：走过去（放风筝 / 交叉火力位 / 守墙位……）；stay = 原地不动
            if (_plan.hasSpot) TacticalMove(pos, _plan.spot, _plan.run);
            else if (_hasDest && Time.time >= _flankUntil) { _mover.Stop(); _hasDest = false; }
            if (w.ranged)
            {
                CharacterRigBase aimT = GetComponent<CharacterRigBase>();   // 任何拼装角色都行
                if (aimT != null) aimT.LockOn(_foe.Transform);                  // 边走边打也一直瞄着它
                if (!_plan.mayFire || d > reach) return;
                if (!_hasDest) _mover.FaceTowards(fp);
            }
            else
            {
                if (d > reach) return;
                if (_hasDest) { _mover.Stop(); _hasDest = false; }
                _mover.FaceTowards(fp);
            }
        }
        else if (w.ranged)
        {
            // 拿枪：边走边打——保持在射程里合适的距离（太远就靠近，太近就往后退），射程内随时开枪，不用停下
            float keep = reach * 0.75f;                                        // 想和敌人保持的距离
            if (d > reach * 0.9f || d < reach * 0.45f)                         // 太远 / 太近：走到合适的距离
            {
                _mover.SetSpeedMultiplier(1.15f);
                if (_repath <= 0f || !_hasDest)
                {
                    _repath = 0.3f;
                    Vector3 away = pos - fp; away.z = 0f;                      // 从敌人指向自己的方向
                    if (away.sqrMagnitude < 0.0001f) away = Vector3.right;     // 贴在一起时随便选个方向
                    Vector3 to = fp + away.normalized * keep;                  // 离敌人 keep 远的位置（靠近或后退）
                    _mover.MoveTo(to);
                    _dest = to;
                    _hasDest = true;
                }
            }
            else if (_hasDest && Time.time >= _flankUntil) { _mover.Stop(); _hasDest = false; }   // 距离合适：站定（正在侧移就先走完）
            CharacterRigBase aim = GetComponent<CharacterRigBase>();
            if (aim != null) aim.LockOn(_foe.Transform);                       // 外观：锁定这个敌人——枪口一直对着它，边走边打（往后退）也面朝它
            if (d > reach) return;                                             // 还在射程外：只走不打
            if (!_hasDest) _mover.FaceTowards(fp);
        }
        else
        {
            if (d > reach)
            {
                _mover.SetSpeedMultiplier(1.15f);
                if (_repath <= 0f || !_hasDest)
                {
                    _repath = 0.3f;
                    Vector3 to = fp;                                           // 近战：直接冲到敌人跟前
                    _mover.MoveTo(to);
                    _dest = to;
                    _hasDest = true;
                }
                return;
            }
            if (_hasDest) { _mover.Stop(); _hasDest = false; }
            _mover.FaceTowards(fp);
        }
        if (_attackCd > 0f) return;
        if (w.ranged && n.pistolAmmo <= 0)
        {
            // 打空了：换一个弹匣（这段时间不开枪）
            if (NpcInventory.Reload(n))
            {
                bool close = d < reach * SpeedReloadRange;                    // 敌人离得近
                bool tactical = !close && NpcInventory.StowEmptyMag(n);       // 不急：旧弹匣收回物品栏（空弹匣）；放不下就只能丢
                CharacterRigBase.ReloadKind kind = tactical ? CharacterRigBase.ReloadKind.Tactical : CharacterRigBase.ReloadKind.Speed;
                CharacterRigBase reloadRig = GetComponent<CharacterRigBase>();
                float time = reloadRig != null ? reloadRig.ReloadSeconds(kind) : ReloadTime;   // 换弹时间 = 这种换弹动画的长度 × 武器操作等级的倍数
                _attackCd = time;
                if (reloadRig != null) reloadRig.PlayReload(time, kind);       // 外观：播对应的换弹动画（快速换弹时旧弹匣掉到地上）
                if (_barkCd <= 0f) Bark(LanguageGuard.Pick("换弹匣！", "リロード！"));
            }
            return;
        }
        // 手枪：开火线上有自己人挡着 → 先不开，往旁边挪一步换个角度
        if (w.ranged && Ballistics.AllyInLine(pos, fp, Id, out Vector3 ally))
        {
            _attackCd = 0.25f;
            if (Time.time >= _flankUntil) Flank(pos, fp, ally);
            return;
        }
        _attackCd = w.interval / (NpcHealth.Has(n, HealthTag.Fatigued) ? 0.7f : 1f);
        if (w.ranged) NpcInventory.SpendRound(n);
        NpcRig rig = GetComponent<NpcRig>();                                  // 外观：出手动作（挥一下 / 开枪后坐）
        if (rig != null) rig.PlayAttack();
        if (w.ranged)                                                         // 手枪：子弹真的飞过去，路上先碰到什么就打中什么（Ballistics）
        {
            Vector3 from = pos;
            if (rig != null) rig.TryGetMuzzle(out from);                      // 从枪口出发（没拿出枪就从人身上）
            bool moving = _hasDest || _mover.IsMoving;                        // 边走边打：散布更大
            Ballistics.FireFromNpc(n, rig, from, fp, _foe.Velocity, w.damage, moving);
            return;
        }
        float acc = 0.65f + n.personality.bravery * 0.002f - (n.sanity < 40f ? 0.15f : 0f);   // 近战：照旧按命中率
        if (Random.value < acc) _foe.TakeMelee(w.damage, n);
    }

    // 走到团队安排的位置（到了就站定；位置变了才重新寻路）
    void TacticalMove(Vector3 pos, Vector3 spot, bool run)
    {
        if (Time.time < _flankUntil) return;                                  // 正在侧移换角度（火线被自己人挡着）
        if (Dist(pos, spot) < 0.25f) { if (_hasDest) { _mover.Stop(); _hasDest = false; } return; }
        _mover.SetSpeedMultiplier(run ? 1.3f : 1.1f);
        if (!_hasDest || Dist(_dest, spot) > 0.2f || _repath <= 0f)
        {
            _repath = 0.6f;
            _mover.MoveTo(spot);
            _dest = spot;
            _hasDest = true;
        }
    }

    // 开火线被 ally 挡着：往开火线的一侧挪一步（挑离 ally 远的那一侧），换个角度再打
    void Flank(Vector3 pos, Vector3 foe, Vector3 ally)
    {
        Vector2 line = foe - pos;
        if (line.sqrMagnitude < 0.0001f) return;
        Vector2 side = new Vector2(-line.y, line.x).normalized;               // 垂直于开火线
        Vector2 toAlly = ally - pos;
        if (Vector2.Dot(side, toAlly) > 0f) side = -side;                     // 往 ally 的另一边挪
        Vector3 to = pos + new Vector3(side.x, side.y, 0f) * 0.9f;
        if (NpcNav.Sample(to, 0.6f, out Vector3 s)) to = s;
        _mover.SetSpeedMultiplier(1.15f);
        _mover.MoveTo(to);
        _dest = to;
        _hasDest = true;
        _flankUntil = Time.time + 1.2f;
        _repath = 1.2f;
    }

    void DoFlee()
    {
        if (_mover == null) return;
        _mover.SetSpeedMultiplier(1.3f);   // 跑（NpcRig 会换成跑步动作）；每帧设，别被别的系统改回去
        if (_plan != null && _plan.hasSpot)  // 团队安排的集合点 / 后撤点（一起躲，不各跑各的；不往死角跑）
        {
            if (!_hasDest || Dist(_dest, _plan.spot) > 0.3f) { _mover.MoveTo(_plan.spot); _dest = _plan.spot; _hasDest = true; }
            return;
        }
        if (_repath > 0f && _hasDest) return;
        _repath = 1f;
        Vector3 pos = transform.position;
        Vector3 camp = NpcNav.CampCenter(pos);
        CombatFoe foe = _foe != null && _foe.Alive ? _foe : null;
        Vector3 target;
        if (foe == null || Dist(foe.Position, camp) > NpcNav.CampRadius + 1.5f)
        {
            // 敌人在营地外：躲回营地中心（已经在往那跑了就别换点）
            if (_hasDest && Dist(_dest, camp) < NpcNav.CampRadius && Dist(pos, _dest) > 0.4f) return;
            target = NpcNav.TryRandomPoint(pos, camp, 0f, Mathf.Max(0.8f, NpcNav.CampRadius * 0.35f), false, out Vector3 p) ? p : camp;
        }
        else
        {
            // 敌人已经闯进营地：往它的反方向跑
            Vector2 away = pos - foe.Position;
            if (away.sqrMagnitude < 0.01f) away = Random.insideUnitCircle;
            Vector3 want = pos + new Vector3(away.x, away.y, 0f).normalized * 5f;
            target = NpcNav.Sample(want, 3f, out Vector3 s) ? s : pos;
        }
        _mover.MoveTo(target);
        _dest = target;
        _hasDest = true;
    }

    // calm = 敌人走了，自然结束（停下脚步）；false = 交给别的系统（玩家指令 / 倒下了），不动它的移动
    void Stop(NpcData n, bool calm)
    {
        if (_mover != null)
        {
            _mover.SetSpeedMultiplier(1f);
            if (calm && _hasDest) _mover.Stop();
        }
        _hasDest = false;
        Current = Mode.None;
        _foe = null;
        _hurt = 0f;
        Release();
        if (calm && n != null && !n.IsDead) n.currentAction = L("combat_act_calm");
    }

    void Release()
    {
        if (!_claimed) return;
        NpcBusy.Release(_claimId, NpcBusy.Combat);
        _claimed = false;
    }

    // ============ 玩家在对话里下的姿态（"迎战！" / "快躲起来" / "大家自己看着办"）============

    static readonly string[] FightWords = { "迎战", "应战", "迎击", "打它", "打他", "打那", "打怪", "打死它", "干掉", "揍它", "攻击", "反击",
                                            "戦え", "迎え撃", "攻撃", "倒せ", "やっつけ", "反撃" };
    static readonly string[] HideWords = { "躲起来", "躲好", "快躲", "躲进", "撤退", "快跑", "快逃", "逃命", "别打了", "别跟它打", "别和它打", "不要打",
                                           "隠れ", "逃げろ", "逃げて", "退却", "下がれ", "戦うな" };
    static readonly string[] AutoWords = { "自己判断", "自由行动", "见机行事", "看着办", "自分で判断", "任せる", "臨機応変" };
    static readonly string[] AllWords = { "大家", "所有人", "全员", "全体", "你们", "みんな", "全員", "皆" };
    static readonly string[] NegPrefix = { "别", "不要", "不用", "不许", "不准" };

    // 认不出来返回 false（交给后面的指令 / 聊天）。everyone = 对所有人说的。
    public static bool TryParseStance(string text, out Stance stance, out bool everyone)
    {
        stance = Stance.Auto;
        everyone = false;
        if (string.IsNullOrEmpty(text)) return false;
        bool fight = HasWord(text, FightWords), hide = HasWord(text, HideWords), auto = HasWord(text, AutoWords);
        if (!fight && !hide && !auto) return false;
        if (text.Contains("?") || text.Contains("？") || text.Contains("吗")) return false;   // 在商量，不是下令
        stance = auto ? Stance.Auto : hide ? Stance.Hide : Stance.Fight;   // "别打了快躲" = 躲
        everyone = HasWord(text, AllWords);
        return true;
    }

    static bool HasWord(string text, string[] words)
    {
        foreach (string w in words)
        {
            int i = text.IndexOf(w, System.StringComparison.Ordinal);
            while (i >= 0)
            {
                bool neg = false;
                foreach (string p in NegPrefix)
                    if (i >= p.Length && string.CompareOrdinal(text, i - p.Length, p, 0, p.Length) == 0) { neg = true; break; }
                if (!neg) return true;
                i = text.IndexOf(w, i + 1, System.StringComparison.Ordinal);
            }
        }
        return false;
    }

    // ============ 小工具 ============

    static readonly string[] FightZh = { "来啊！", "别过来……我不怕你！", "一起上！", "打它！" };
    static readonly string[] FightJa = { "来い！", "来るな……怖くないぞ！", "一緒にかかれ！", "やっちまえ！" };
    static readonly string[] FleeZh = { "快跑！", "那是什么东西……！", "回营地！", "别管了，先躲起来！" };
    static readonly string[] FleeJa = { "逃げろ！", "なんだあれ……！", "拠点に戻れ！", "いいから隠れろ！" };
    static readonly string[] HurtZh = { "呃啊！", "好痛……！", "被打中了！" };
    static readonly string[] HurtJa = { "ぐあっ！", "痛っ……！", "やられた！" };

    static string Pick(string[] zh, string[] ja)
    {
        string[] a = LanguageGuard.IsJp ? ja : zh;
        return a[Random.Range(0, a.Length)];
    }

    void Bark(string text)
    {
        if (_barkCd > 0f) return;
        _barkCd = 6f;   // 喊话别刷屏
        NpcSpeechBubble.Show(transform, text, 1.8f);
    }

    static void Log(NpcData n, string text, bool force = false)
    {
        if (n == null || string.IsNullOrEmpty(text)) return;
        NpcCombat c = Of(n.id);
        if (c != null && !force)
        {
            if (c._logCd > 0f) return;   // 同一个人 4 秒内只记一条
            c._logCd = 4f;
        }
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w != null) n.memories.Add(new MemoryEntry { day = w.day, content = text });
        if (LogManager.Instance != null) LogManager.Instance.AddLog(n.npcName, text, n.id);
    }

    static float Dist(Vector3 a, Vector3 b) => Vector2.Distance(a, b);

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] a) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, a) : key;
}
