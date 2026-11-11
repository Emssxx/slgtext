using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// ============================================================================
// ABA 清除小组的一名队员（外观 = NpcRigB 头盔士兵）。纯规则，不走 LLM。
//
// 【谁管什么】小组（AbaSquad）每 0.2 秒下命令：打谁（target）、走到哪（moveTo / run）、警戒哪个方向（sector）、
//   举枪还是放低（alert）、现在能不能战术换弹（小组错开换弹）。队员自己管：开火节奏、火线检查、换弹、转手枪、包扎、体力。
// 【开火】锁定目标 → 枪口转到位（新目标等 acquireTime 秒）→ 看火线（墙挡着不开；队友 / 带终端的人挡着就侧移）→ 点射 / 连发 / 双击。
//   步枪：远 = 两发精确点射，中 = 三发点射，近 / 被扑 = 6 发连发；大家伙多打 2 发。手枪：双击，贴脸 3 发（击退把它推开）。
// 【弹匣】各 5 个（枪里 1 + 4 个备用），每个弹匣单独记还剩几发：打空了快速换弹（空弹匣扔掉），没打空时战术换弹（旧弹匣收回来，之后还能用）。
//   接敌中步枪打空、队友正在换弹或者敌人贴得很近 → 转手枪（比换弹快）；安全了再换回步枪、补弹。
// ============================================================================
public class AbaOperative : MonoBehaviour
{
    public static readonly List<AbaOperative> All = new List<AbaOperative>();

    public AbaSquad Squad { get; private set; }
    public int Slot { get; private set; }
    public string Callsign { get; private set; }
    public float Hp { get; private set; }
    public float MaxHp { get; private set; }
    public float Stamina { get; private set; } = 100f;
    public bool Alive => Hp > 0f;
    public bool Visible { get; private set; } = true;
    public NpcRigB Rig { get; private set; }
    public Vector3 Velocity => _agent != null && _agent.isActiveAndEnabled ? _agent.velocity : Vector3.zero;
    public bool Moving => Velocity.sqrMagnitude > 0.05f;

    // —— 小组下的命令 ——
    [System.NonSerialized] public AbaTarget target;      // 打谁（null = 没有）
    [System.NonSerialized] public Vector3 moveTo;        // 走到哪
    [System.NonSerialized] public bool hasMove;          // 要不要走
    [System.NonSerialized] public bool run;              // 跑（体力 > 15 才真的跑）
    [System.NonSerialized] public Vector2 sector = Vector2.right;   // 警戒方向
    [System.NonSerialized] public bool alert;            // 举枪警戒（false = 枪口朝下）

    // —— 武器 ——
    public AbaWeapon Weapon { get; private set; }
    public bool HasPistol => Weapon != null && Weapon.id == "pistol";
    readonly Dictionary<string, int> _inGun = new Dictionary<string, int>();          // 枪里还剩几发
    readonly Dictionary<string, List<int>> _spare = new Dictionary<string, List<int>>();   // 备用弹匣（每个还剩几发）
    public bool Reloading => Time.time < _reloadUntil;
    public bool Busy => Reloading || (Rig != null && Rig.IsDrawing) || Time.time < _healUntil;

    const float FeetY = -0.453125f;
    NavMeshAgent _agent;
    SpriteRenderer _flip;               // 透明的"旧图"：NpcRigB 读它的 flipX 决定朝向
    Transform _scan, _rigRoot, _bar, _fill;
    AbaTarget _lastTarget;
    float _acquireAt, _shotCd, _reloadUntil, _healUntil, _sidestepUntil, _talkCd, _barUntil, _visTimer, _deadAt = -1f;
    int _burstLeft, _medkits;
    string _reloadGun;
    bool _reloadSpeed;
    Vector3 _sidestep;
    float _pistolSince = -1f;

    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;
    static AbaForce F => AbaForce.Ensure();

    // ============ 生成 ============

    public static AbaOperative Spawn(AbaSquad squad, int slot, Vector3 pos)
    {
        if (NpcNav.Sample(pos, 2f, out Vector3 p)) pos = p;
        GameObject go = new GameObject("ABA_" + (slot + 1));
        go.transform.position = pos;
        AbaOperative op = go.AddComponent<AbaOperative>();
        op.Init(squad, slot);
        return op;
    }

    void Init(AbaSquad squad, int slot)
    {
        Squad = squad;
        Slot = slot;
        Callsign = AbaForce.LF("aba_callsign", slot + 1);
        MaxHp = F.maxHp;
        Hp = MaxHp;
        _medkits = F.medkits;
        _inGun["rifle"] = F.Rifle.magSize;
        _inGun["pistol"] = F.Pistol.magSize;
        _spare["rifle"] = new List<int> { F.Rifle.magSize, F.Rifle.magSize, F.Rifle.magSize, F.Rifle.magSize };
        _spare["pistol"] = new List<int> { F.Pistol.magSize, F.Pistol.magSize, F.Pistol.magSize, F.Pistol.magSize };
        Weapon = F.Rifle;

        _flip = gameObject.AddComponent<SpriteRenderer>();   // 没有图，只用来存朝向和排序（NpcRigB 会把它变透明）
        _flip.sortingOrder = 1;                              // 和 NPC 同层
        SetupAgent();
        Rig = gameObject.AddComponent<NpcRigB>();
        Rig.testKeyboard = false;                            // 游戏里：关掉测试开关（键盘 / 自动开枪）
        Rig.testLoop = false;
        Rig.testCycle = false;
        Rig.testSpeed = -1f;
        Rig.testHeld = NpcRigB.TestHeld.None;

        _scan = new GameObject("AimScan").transform;         // 没目标时枪口对着的点（扇区警戒）
        _scan.SetParent(transform, false);

        _bar = EnemyDirector.Quad("HpBar", transform, new Color(0f, 0f, 0f, 0.75f), 6);
        _bar.localPosition = new Vector3(0f, FeetY + 1.05f, 0f);
        _bar.localScale = new Vector3(0.6f, 0.06f, 1f);
        _fill = EnemyDirector.Quad("Fill", _bar, new Color(0.35f, 0.85f, 0.95f), 7);
        _bar.gameObject.SetActive(false);
        All.Add(this);
    }

    void SetupAgent()
    {
        _agent = gameObject.AddComponent<NavMeshAgent>();
        NavMeshAgent tpl = null;
        foreach (NpcView v in NpcView.GetAllViews())
            if (v != null && (tpl = v.GetComponent<NavMeshAgent>()) != null) break;
        if (tpl != null)
        {
            _agent.agentTypeID = tpl.agentTypeID;
            _agent.radius = tpl.radius;
            _agent.height = tpl.height;
            _agent.baseOffset = tpl.baseOffset;
            _agent.acceleration = Mathf.Max(tpl.acceleration, 14f);   // 训练有素：起步 / 急停更利索
            _agent.angularSpeed = tpl.angularSpeed;
        }
        else
        {
            _agent.radius = 0.2f;
            _agent.baseOffset = -FeetY;
            _agent.acceleration = 14f;
        }
        _agent.updateRotation = false;
        _agent.updateUpAxis = false;
        _agent.stoppingDistance = 0.05f;
        _agent.autoBraking = true;
        _agent.speed = F.walkSpeed;
        if (!_agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            _agent.Warp(hit.position);
    }

    void OnDestroy() { All.Remove(this); }

    // ============ 每帧 ============

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (!Alive) { if (_deadAt > 0f && Time.time - _deadAt > 15f) Destroy(gameObject); return; }
        if (_rigRoot == null) _rigRoot = transform.Find("RigB");   // NpcRigB 在 Start 里拼好
        _talkCd -= dt;
        _visTimer -= dt;
        if (_visTimer <= 0f) { _visTimer = 0.2f; UpdateVisible(); }

        FinishReload();
        FinishHeal();
        UpdateStamina(dt);
        UpdateWeaponChoice();
        UpdateMove();
        UpdateAim();
        UpdateFire(dt);
        if (_bar != null) _bar.gameObject.SetActive(Visible && Time.time < _barUntil);
    }

    // ============ 走 ============

    void UpdateMove()
    {
        if (_agent == null || !_agent.isOnNavMesh) return;
        bool healing = Time.time < _healUntil;
        bool canRun = run && Stamina > 15f && !Reloading;
        _agent.speed = canRun ? F.runSpeed : F.walkSpeed;
        Vector3 dest = transform.position;
        bool go = false;
        if (healing) go = false;
        else if (Time.time < _sidestepUntil) { dest = _sidestep; go = true; }
        else if (hasMove) { dest = moveTo; go = true; }
        if (go && Vector2.Distance(dest, transform.position) > 0.12f)
        {
            if (!_agent.hasPath || Vector2.Distance(_agent.destination, dest) > 0.25f) _agent.SetDestination(dest);
        }
        else if (_agent.hasPath) _agent.ResetPath();

        // 朝向（不瞄准时 NpcRigB 看旧图的 flipX）：走的时候看往哪走，站着看警戒方向
        Vector3 v = Velocity;
        if (v.x < -0.1f) _flip.flipX = true;
        else if (v.x > 0.1f) _flip.flipX = false;
        else if (Mathf.Abs(sector.x) > 0.2f) _flip.flipX = sector.x < 0f;
    }

    // 侧移一下换个角度（火线上有队友 / 不能打的人）
    void Sidestep(Vector3 blocker, Vector3 tp)
    {
        if (Time.time < _sidestepUntil) return;
        Vector2 line = (tp - transform.position);
        Vector2 perp = new Vector2(-line.y, line.x).normalized;
        Vector2 toBlocker = blocker - transform.position;
        if (Vector2.Dot(perp, toBlocker) > 0f) perp = -perp;              // 往远离挡着的人那边让
        Vector3 p = transform.position + new Vector3(perp.x, perp.y, 0f) * 0.8f;
        if (!NpcNav.Sample(p, 0.6f, out Vector3 s))
        {
            p = transform.position - new Vector3(perp.x, perp.y, 0f) * 0.8f;
            if (!NpcNav.Sample(p, 0.6f, out s)) return;
        }
        _sidestep = s;
        _sidestepUntil = Time.time + 0.7f;
    }

    // ============ 瞄准 ============

    void UpdateAim()
    {
        if (Rig == null) return;
        if (target != null && target.Alive && target.Transform != null && Vector2.Distance(target.Position, transform.position) < Weapon.range * 1.4f)
        {
            Rig.LockOn(target.Transform);
            return;
        }
        if (alert && !Moving || alert && Slot == 0)
        {
            // 扇区警戒：枪口在负责的方向上左右慢慢扫（±25°）
            float sweep = Mathf.Sin(Time.time * 0.9f + Slot * 1.7f) * 25f;
            Vector2 d = Rotate(sector.sqrMagnitude > 0.01f ? sector.normalized : Vector2.right, sweep);
            _scan.position = transform.position + new Vector3(d.x, d.y, 0f) * 2.5f;
            Rig.LockOn(_scan);
        }
        // 不警戒：不调用 LockOn，NpcRigB 0.3 秒后自己把枪口放低
    }

    // ============ 开火 ============

    void UpdateFire(float dt)
    {
        _shotCd -= dt;
        if (target == null || !target.Alive) { _burstLeft = 0; return; }
        if (target != _lastTarget) { _lastTarget = target; _acquireAt = Time.time + 0.12f; _burstLeft = 0; }   // 换目标：枪口转过去要一点时间
        if (Busy || Rig == null || Rig.Held != Weapon.id) return;
        if (_inGun[Weapon.id] <= 0) { OnEmpty(); return; }
        Vector3 tp = target.Position;
        float dist = Vector2.Distance(tp, transform.position);
        if (dist > Weapon.range * 1.3f) return;                           // 太远：不浪费子弹，等小组往前压
        if (_shotCd > 0f || Time.time < _acquireAt) return;
        Vector3 from = Rig.TryGetMuzzle(out Vector3 m) ? m : transform.position;
        if (!Ballistics.LineClear(transform.position, tp)) { _burstLeft = 0; return; }   // 墙 / 石头挡着
        if (AbaSquad.FriendlyInLine(from, tp, this, out Vector3 blocker)) { _burstLeft = 0; Sidestep(blocker, tp); return; }
        if (_burstLeft <= 0) _burstLeft = BurstFor(dist);
        Fire(tp);
        _burstLeft--;
        _shotCd = _burstLeft > 0 ? Weapon.shotInterval : Weapon.pause * Mathf.Lerp(0.6f, 1.3f, Mathf.InverseLerp(2f, 8f, dist));
    }

    // 一组打几发
    int BurstFor(float dist)
    {
        bool strong = target.IsStrong;
        bool rushing = target.Melee && dist < 3f;                         // 近战的东西扑过来了
        if (HasPistol) return dist < 1.5f ? 3 : 2;                         // 双击；贴脸三发
        int n = rushing || dist < 2.5f ? 6 : dist < 6f ? 3 : 2;
        return strong ? n + 2 : n;
    }

    void Fire(Vector3 tp)
    {
        Rig.PlayAttack();                                                  // 先摆好瞄准姿势，子弹才从对准后的枪口出来
        Vector3 from = Rig.TryGetMuzzle(out Vector3 m) ? m : transform.position;
        Ballistics.FireFromAba(this, from, tp, target.Velocity, Weapon, Moving);
        _inGun[Weapon.id]--;
    }

    // 打空了
    void OnEmpty()
    {
        if (Busy) return;
        bool contact = Squad != null && Squad.InContact;
        float d = target != null ? Vector2.Distance(target.Position, transform.position) : 99f;
        if (Weapon.id == "rifle")
        {
            // 接敌中：队友在换弹 / 敌人贴得近 → 转手枪（比换弹快）
            bool pistolOk = Rounds("pistol") > 0;
            if (contact && pistolOk && (d < 2.5f || (Squad != null && Squad.SomeoneReloading(this))))
            {
                Equip("pistol");
                Say("aba_call_sidearm");
                return;
            }
            if (!StartReload(true)) { if (pistolOk) Equip("pistol"); }
        }
        else if (!StartReload(true) && Rounds("rifle") > 0) Equip("rifle");
    }

    // 这把枪一共还有几发（枪里 + 备用）
    public int Rounds(string gun)
    {
        int n = _inGun[gun];
        foreach (int r in _spare[gun]) n += r;
        return n;
    }

    public float MagFraction => Weapon != null ? (float)_inGun[Weapon.id] / Weapon.magSize : 1f;

    // 现在这把枪里还剩几发 / 还有几个有子弹的备用弹匣（测试面板显示用）
    public int InGun => Weapon != null ? _inGun[Weapon.id] : 0;
    public int SpareMags { get { int k = 0; if (Weapon != null) foreach (int r in _spare[Weapon.id]) if (r > 0) k++; return k; } }

    // 测试用（战斗测试的"无限弹匣"）：备用弹匣补回 4 个满的
    public void RefillSpare()
    {
        foreach (string gun in new[] { "rifle", "pistol" })
        {
            int size = gun == "rifle" ? F.Rifle.magSize : F.Pistol.magSize;
            List<int> s = _spare[gun];
            s.RemoveAll(r => r <= 0);
            while (s.Count < 4) s.Add(size);
            for (int i = 0; i < s.Count; i++) s[i] = size;
        }
    }

    // ============ 换弹 ============

    // speed = 快速换弹（旧弹匣扔掉）；否则战术换弹（旧弹匣收回来）。没有备用弹匣返回 false
    bool StartReload(bool speed)
    {
        if (Busy || Rig == null || Rig.Held != Weapon.id) return false;
        List<int> spare = _spare[Weapon.id];
        int best = -1;
        for (int i = 0; i < spare.Count; i++) if (spare[i] > 0 && (best < 0 || spare[i] > spare[best])) best = i;
        if (best < 0) return false;
        if (!speed && spare[best] <= _inGun[Weapon.id]) return false;       // 备用的还没枪里的多：不换
        NpcRigB.ReloadKind kind = Rig.ResolveReloadKind(speed ? NpcRigB.ReloadKind.Speed : NpcRigB.ReloadKind.Tactical);
        _reloadSpeed = kind == NpcRigB.ReloadKind.Speed;
        float sec = _reloadSpeed ? Weapon.speedReload : Weapon.tacticalReload;
        Rig.PlayReload(sec, kind);
        _reloadGun = Weapon.id;
        _reloadUntil = Time.time + sec;
        _burstLeft = 0;
        if (Squad != null && Squad.InContact) Say("aba_call_reload");
        return true;
    }

    void FinishReload()
    {
        if (_reloadGun == null || Time.time < _reloadUntil) return;
        string gun = _reloadGun;
        _reloadGun = null;
        List<int> spare = _spare[gun];
        int best = -1;
        for (int i = 0; i < spare.Count; i++) if (spare[i] > 0 && (best < 0 || spare[i] > spare[best])) best = i;
        if (best < 0) return;
        int old = _inGun[gun];
        _inGun[gun] = spare[best];
        spare.RemoveAt(best);
        if (!_reloadSpeed && old > 0) spare.Add(old);                       // 战术换弹：没打空的旧弹匣收回胸挂
    }

    // 小组说现在安全：没打满的弹匣换掉
    public bool TryTacticalReload(float below)
    {
        if (Busy || Weapon == null || MagFraction >= below) return false;
        return StartReload(false);
    }

    // ============ 换枪 ============

    void Equip(string gun)
    {
        if (Rig == null) return;
        Weapon = gun == "pistol" ? F.Pistol : F.Rifle;
        Rig.Hold(gun);
        _burstLeft = 0;
        _pistolSince = gun == "pistol" ? Time.time : -1f;
    }

    void UpdateWeaponChoice()
    {
        if (Rig == null) return;
        if (Rig.Held == null) { Rig.Hold(Weapon.id); return; }             // NpcRigB 拼好以后才能拿（出场先取出步枪）
        if (Busy) return;
        float d = target != null && target.Alive ? Vector2.Distance(target.Position, transform.position) : 99f;
        // 近战的东西贴到 1.3 以内：拔手枪把它推开（击退）
        if (Weapon.id == "rifle" && target != null && target.Melee && d < 1.3f && _inGun["pistol"] > 0) { Equip("pistol"); return; }
        // 拿着手枪、附近没威胁 2 秒 → 换回步枪
        if (Weapon.id == "pistol" && Rounds("rifle") > 0 && (target == null || d > 3.5f) && _pistolSince > 0f && Time.time - _pistolSince > 2f)
        {
            if (_inGun["pistol"] < Weapon.magSize && (Squad == null || !Squad.InContact)) { if (StartReload(false)) return; }   // 先把手枪补满
            Equip("rifle");
        }
    }

    // ============ 体力 / 包扎 ============

    void UpdateStamina(float dt)
    {
        bool contact = Squad != null && Squad.InContact;
        bool running = run && Moving && _agent != null && _agent.speed > F.walkSpeed + 0.1f;
        float drain = 0f;
        if (contact) drain += AbaForce.CombatDrain;              // 普通 NPC 战斗每秒消耗 × 0.3
        if (running) drain += AbaForce.CombatDrain;              // 跑步按战斗一样算
        if (drain > 0f) Stamina = Mathf.Max(0f, Stamina - drain * dt);
        else Stamina = Mathf.Min(100f, Stamina + 1.5f * dt);     // 不打不跑：慢慢回
    }

    // 小组说安全了：伤重的包扎一下
    public bool TryHeal()
    {
        if (Busy || _medkits <= 0 || Hp > MaxHp * 0.55f) return false;
        _medkits--;
        _healUntil = Time.time + 2.5f;
        Say("aba_call_heal");
        return true;
    }

    void FinishHeal()
    {
        if (_healUntil <= 0f || Time.time < _healUntil) return;
        _healUntil = 0f;
        Hp = Mathf.Min(MaxHp, Hp + F.medkitHeal);
        UpdateBar();
    }

    // ============ 挨打 / 击杀 ============

    // 被打（敌人近战 / 子弹）。dir = 打过来的方向
    public void TakeHit(float dmg, Vector2 dir, Enemy byEnemy, NpcData byNpc)
    {
        if (!Alive || dmg <= 0f) return;
        Hp -= dmg * (1f - F.armor);
        _barUntil = Time.time + 4f;
        UpdateBar();
        if (Squad != null) Squad.OnMemberHit(this, byEnemy, byNpc);
        if (Hp <= 0f) { Die(); return; }
        if (Random.value < 0.35f) Say("aba_call_hit");
    }

    // 打死了一个目标（Ballistics 调）
    public void OnKill(Enemy e, NpcData n)
    {
        if (n != null)
        {
            string text = AbaForce.LF("aba_log_kill_npc", n.npcName);
            if (LogManager.Instance != null) LogManager.Instance.AddLog(AbaForce.L("aba_name"), text, "");
        }
        if (Squad != null) Squad.OnKill(this);
    }

    void Die()
    {
        Hp = 0f;
        _deadAt = Time.time;
        All.Remove(this);
        if (_agent != null) { if (_agent.isOnNavMesh) _agent.ResetPath(); _agent.enabled = false; }
        if (Rig != null) Rig.LockOn(null);
        if (_bar != null) _bar.gameObject.SetActive(false);
        transform.rotation = Quaternion.Euler(0f, 0f, _flip != null && _flip.flipX ? -90f : 90f);   // 倒地
        if (Squad != null) Squad.OnMemberDown(this);
    }

    void UpdateBar()
    {
        if (_fill == null) return;
        float r = Mathf.Clamp01(Hp / Mathf.Max(1f, MaxHp));
        _fill.localScale = new Vector3(r, 1f, 1f);
        _fill.localPosition = new Vector3(-(1f - r) * 0.5f, 0f, 0f);
    }

    // ============ 迷雾 / 喊话 ============

    void UpdateVisible()
    {
        bool vis = !NpcNav.IsUnseen(transform.position);
        Visible = vis;
        if (_rigRoot != null && _rigRoot.gameObject.activeSelf != vis) _rigRoot.gameObject.SetActive(vis);
        if (!vis) NpcSpeechBubble.Hide(transform);
    }

    // 喊一句（同一个人 2.5 秒内不重复喊；小组也会限流）
    public void Say(string key)
    {
        if (_talkCd > 0f || !Visible || !Alive) return;
        if (Squad != null && !Squad.MayTalk()) return;
        _talkCd = 2.5f;
        NpcSpeechBubble.Show(transform, AbaForce.L(key), 1.6f);
    }

    // ============ 查找 ============

    // 离 p 最近、range 以内活着的队员（敌人找 ABA 用）
    public static AbaOperative Nearest(Vector3 p, float range)
    {
        AbaOperative best = null;
        float bestD = range;
        foreach (AbaOperative o in All)
        {
            if (o == null || !o.Alive) continue;
            float d = Vector2.Distance(o.transform.position, p);
            if (d < bestD) { bestD = d; best = o; }
        }
        return best;
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
}

// ============================================================================
// 清除目标：一个敌人或一个幸存者（统一成一样的接口，小组 / 队员不用分开写）
// ============================================================================
public class AbaTarget
{
    public readonly Enemy enemy;
    public readonly NpcData npc;
    public Vector3 lastSeen;
    public float seenAt = -99f;

    AbaTarget(Enemy e, NpcData n) { enemy = e; npc = n; }
    public static AbaTarget Of(Enemy e) => new AbaTarget(e, null);
    public static AbaTarget Of(NpcData n) => new AbaTarget(null, n);

    public bool Same(Enemy e, NpcData n) => (e != null && e == enemy) || (n != null && n == npc);

    public bool Alive => enemy != null ? enemy.Alive : (npc != null && !npc.IsDead && AbaForce.IsTarget(npc) && Mover != null);
    NpcMover Mover => npc != null ? NpcView.GetMover(npc.id) : null;
    public Transform Transform => enemy != null ? enemy.transform : (Mover != null ? Mover.transform : null);
    public Vector3 Position { get { Transform t = Transform; return t != null ? t.position : lastSeen; } }
    public Vector3 Velocity
    {
        get
        {
            if (enemy != null) return enemy.Velocity;
            NpcMover m = Mover;
            NavMeshAgent a = m != null ? m.GetComponent<NavMeshAgent>() : null;
            return a != null && a.isActiveAndEnabled ? a.velocity : Vector3.zero;
        }
    }
    public bool IsStrong => enemy != null && enemy.Def.IsStrong;
    public bool Melee => enemy != null && !enemy.Def.Ranged;

    // 危险程度（每秒能打多少血；强敌 / 远程再加权）。幸存者：带着能开的枪才算有威胁
    public float Danger
    {
        get
        {
            if (enemy != null)
            {
                EnemyDef d = enemy.Def;
                float dps = d.damage / Mathf.Max(0.3f, d.attackInterval);
                return dps * (d.IsStrong ? 1.5f : 1f) * (d.Ranged ? 1.3f : 1f);
            }
            return npc != null && NpcInventory.PistolReady(npc) ? 5f : 1.5f;
        }
    }
}
