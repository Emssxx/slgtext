using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// ============================================================================
// 敌人（场景里的一只）：纯代码状态机，不走 LLM。由 EnemyDirector 生成（Enemy.Spawn），不用预制体。
//
// 【来干什么（goal）】
//   Wander  游荡：在自己的地盘里转悠，看见人才动手（安全期内不靠近营地）。
//   Assault 冲营地：直奔营地，被墙挡住就破墙（会破墙的才行），一路见人就打。过一段时间撤走。
//   Raid    抢劫（人类）：冲到营地中心翻仓库，抢到东西就跑。
//
// 【每 0.25 秒想一次（Think）】
//   受伤到 fleeBelow → 逃；袭击时间到 → 撤；视野里有想打的人 → 追 / 打；
//   路被墙挡住（导航算不到目标 / 卡住不动）→ 会破墙的去拆最近那段墙，不会的放弃目标在外面转。
//
// 【看得见吗】在迷雾里（玩家看不清的地方）就把图藏起来；第一次被看见时头顶冒一下名字。
// 【外观】Resources/EnemyArt/<id> 有图就用（轴心设在脚底），没有就用代码画的占位图（造物：一团带眼睛的影子；人类：剪影）。
// ============================================================================
public class Enemy : MonoBehaviour
{
    public static readonly List<Enemy> All = new List<Enemy>();

    public enum Goal { Wander, Assault, Raid }
    public enum State { Roam, Chase, Attack, BreakWall, Loot, Leave, Flee, Dead }

    public EnemyDef Def { get; private set; }
    public Goal goal;
    public State state;
    public float Hp { get; private set; }
    public bool Alive => Def != null && state != State.Dead;
    public bool Visible { get; private set; } = true;   // 不在迷雾里（玩家看得见）
    public string Name => Def != null ? Def.Name : name;
    public NpcData Target => _target;
    // 现在的移动速度（开枪的人预判走位用）
    public Vector3 Velocity => _agent != null && _agent.isActiveAndEnabled ? _agent.velocity : Vector3.zero;
    // 现在是不是冲着人来的：造物永远是；人类只有来袭 / 来抢、正盯着某人、或者挨过打时才算（游荡的拾荒者路过不算）
    public bool Hostile => Def != null && (Def.kind == EnemyKind.Creature || goal != Goal.Wander || _target != null || _lastHitBy != null || _aba != null);
    public bool KilledByAba { get; private set; }   // 被 ABA 清除小组打死的（不掉东西进仓库）

    const float FeetY = -0.453125f;   // 和 NPC 一样：物体中心在脚底上方 0.453（NavMeshAgent 的 baseOffset）

    NavMeshAgent _agent;
    SpriteRenderer _body;
    Transform _bodyT, _bar, _fill;
    float _bodyScale = 1f;
    float _flash, _barUntil, _visTimer, _thinkTimer, _attackCd, _repathTimer, _stuckTimer, _wallTimer, _lootTimer, _fade = 1f;
    float _goalUntil, _loiterUntil, _wallGrace, _lunge, _phase;
    Vector3 _home, _dest, _lastPos;
    bool _hasDest, _looted, _seenOnce;
    NpcData _target;
    GameObject _wall;
    string _lastHitBy;
    AbaOperative _aba;            // 正在对付的 ABA 队员
    bool _vsAba;                  // 这一轮在打 ABA（不是打幸存者）
    float _abaHitAt = -99f;       // 上一次挨 ABA 的打

    static readonly Dictionary<string, Sprite> _placeholders = new Dictionary<string, Sprite>();

    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;

    // ============ 生成 ============

    public static Enemy Spawn(EnemyDef def, Vector3 pos, Goal goal, Vector3 home)
    {
        if (def == null) return null;
        if (NpcNav.Sample(pos, 2f, out Vector3 p)) pos = p;
        GameObject go = new GameObject("Enemy_" + def.id);
        go.transform.position = pos;
        Enemy e = go.AddComponent<Enemy>();
        e.Init(def, goal, home);
        return e;
    }

    void Init(EnemyDef def, Goal g, Vector3 home)
    {
        Def = def;
        goal = g;
        Hp = def.maxHp;
        _home = home;
        _lastPos = transform.position;
        _phase = Random.value * 10f;
        state = g == Goal.Wander ? State.Roam : State.Chase;
        _goalUntil = g == Goal.Wander ? float.MaxValue : Time.time + 150f;   // 袭击最多闹 150 秒，之后撤走
        BuildLook();
        SetupAgent();
        All.Add(this);
    }

    void SetupAgent()
    {
        _agent = gameObject.AddComponent<NavMeshAgent>();
        NavMeshAgent tpl = TemplateAgent();   // 照着 NPC 的导航设置抄（同一张导航网格）
        if (tpl != null)
        {
            _agent.agentTypeID = tpl.agentTypeID;
            _agent.radius = tpl.radius * Mathf.Clamp(Def.scale, 0.8f, 1.6f);
            _agent.height = tpl.height;
            _agent.baseOffset = tpl.baseOffset;
            _agent.acceleration = tpl.acceleration;
            _agent.angularSpeed = tpl.angularSpeed;
        }
        else
        {
            _agent.radius = 0.2f;
            _agent.acceleration = 12f;
        }
        _agent.updateRotation = false;   // 2D：不转、不抬
        _agent.updateUpAxis = false;
        _agent.stoppingDistance = 0.05f;
        _agent.autoBraking = true;
        _agent.speed = Def.speed;
        if (!_agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            _agent.Warp(hit.position);
    }

    static NavMeshAgent TemplateAgent()
    {
        foreach (NpcView v in NpcView.GetAllViews())
        {
            if (v == null) continue;
            NavMeshAgent a = v.GetComponent<NavMeshAgent>();
            if (a != null) return a;
        }
        return null;
    }

    void OnDestroy()
    {
        All.Remove(this);
    }

    // ============ 每帧 ============

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || Def == null) return;
        if (state == State.Dead) { FadeOut(dt); return; }

        _attackCd -= dt;
        _repathTimer -= dt;
        TrackStuck(dt);
        _visTimer -= dt;
        if (_visTimer <= 0f) { _visTimer = 0.2f; UpdateVisible(); }
        _thinkTimer -= dt;
        if (_thinkTimer <= 0f) { _thinkTimer = 0.25f; Think(); }

        switch (state)
        {
            case State.Attack: AttackTarget(); break;
            case State.BreakWall: AttackWall(dt); break;
            case State.Loot: DoLoot(dt); break;
        }
        Animate(dt);
    }

    // ============ 想（每 0.25 秒）============

    void Think()
    {
        if (World == null) return;
        Vector3 pos = transform.position;
        Vector3 camp = NpcNav.CampCenter(_home);
        bool safe = EnemyDirector.IsSafePeriod;

        // 1. 伤得太重：会跑的就跑
        if (Def.fleeBelow > 0f && Hp < Def.maxHp * Def.fleeBelow && state != State.Flee && state != State.Leave)
        {
            state = State.Flee;
            _target = null;
            _wall = null;
            _hasDest = false;
            EnemyDirector.OnEnemyFled(this);
        }

        // 2. 撤退 / 逃跑：走远了、没人看得见 → 消失
        if (state == State.Flee || state == State.Leave)
        {
            if (!Visible && Dist(pos, camp) > EnemyDirector.LeaveDistance && FarFromNpcs(pos, 10f)) { Despawn(); return; }
            if (!_hasDest || Arrived() || _stuckTimer > 3f) GoTo(ExitPoint(camp, pos), Def.speed * (state == State.Flee ? 1.15f : 0.9f), true);
            return;
        }

        // 3. 袭击闹够了：撤
        if (goal != Goal.Wander && Time.time > _goalUntil)
        {
            state = State.Leave;
            _target = null;
            _wall = null;
            _hasDest = false;
            return;
        }

        // 4. 正在拆墙：除非有人贴到跟前，否则一直拆到墙倒（AttackWall 里处理；走不到墙边 8 秒会放弃）
        _vsAba = false;
        if (_wall != null && Time.time - _abaHitAt < 2f) _wall = null;   // 拆墙时挨了 ABA 的枪：先转过来对付它
        if (_wall != null)
        {
            NpcData close = NearestNpc(pos, Def.attackRange + 0.2f);
            if (close != null && WantsToAttack(close, pos, safe)) { _target = close; state = State.Attack; Halt(); return; }
            state = State.BreakWall;
            return;
        }

        // 5. 找要打的人
        if (_target != null && !KeepTarget(_target, pos, safe)) _target = null;
        if (_target == null)
        {
            NpcData n = NearestNpc(pos, Def.sight);
            if (n != null && WantsToAttack(n, pos, safe)) _target = n;
        }
        // 5b. ABA 清除小组：造物见了就扑；人类只还手（10 秒内挨过它打）。它更近 / 刚打了我 → 先对付它
        if (_aba != null && (!_aba.Alive || Dist(pos, _aba.transform.position) > Def.sight * 1.8f)) _aba = null;
        if (_aba == null)
        {
            AbaOperative a = AbaOperative.Nearest(pos, Def.sight);
            if (a != null && (Def.kind == EnemyKind.Creature || Time.time - _abaHitAt < 10f)) _aba = a;
        }
        if (_aba != null && (_target == null || Time.time - _abaHitAt < 3f || Dist(pos, _aba.transform.position) + 0.5f < Dist(pos, NpcPos(_target))))
        {
            _vsAba = true;
            Vector3 ap = _aba.transform.position;
            if (Dist(pos, ap) <= Def.attackRange) { state = State.Attack; Halt(); }
            else { state = State.Chase; GoTo(ap, Def.speed); }
            return;
        }

        if (_target != null)
        {
            Vector3 tp = NpcPos(_target);
            if (Dist(pos, tp) <= Def.attackRange) { state = State.Attack; Halt(); }
            else { state = State.Chase; GoTo(tp, Def.speed); CheckBlocked(tp); }
            return;
        }

        // 6. 按来意行动
        if (Time.time < _loiterUntil) { Roam(pos, camp, safe, 3f); return; }   // 进不去营地：在外面转一会儿再试
        switch (goal)
        {
            case Goal.Wander:
                Roam(pos, camp, safe, 7f);
                break;
            case Goal.Assault:
                if (Dist(pos, camp) > NpcNav.CampRadius * 0.6f) { state = State.Chase; GoTo(camp, Def.speed); CheckBlocked(camp, 2.2f); }
                else
                {
                    state = State.Roam;   // 冲进营地了但没看见人：在营地里乱转
                    if (!_hasDest || Arrived()) GoTo(RandomNear(camp, NpcNav.CampRadius), Def.speed * 0.7f);
                }
                break;
            case Goal.Raid:
                if (_looted) { state = State.Leave; _hasDest = false; break; }
                // 营地中心可能被终端机占着走不到正中间：走到附近、路走完了也算到了
                bool there = Dist(pos, camp) <= 1.2f || (Dist(pos, camp) < 2.2f && Arrived());
                if (!there) { state = State.Chase; GoTo(camp, Def.speed); CheckBlocked(camp, 2.2f); }
                else { state = State.Loot; Halt(); }
                break;
        }
    }

    void Roam(Vector3 pos, Vector3 camp, bool safe, float radius)
    {
        state = State.Roam;
        if (_hasDest && !Arrived() && _stuckTimer < 3f) return;
        // 过了安全期：游荡的家伙会一点点往营地那边挪
        if (goal == Goal.Wander && !safe && Random.value < 0.15f)
        {
            Vector3 toCamp = camp - _home;
            float keep = NpcNav.CampRadius + 3f;
            if (toCamp.magnitude > keep + 2f) _home += toCamp.normalized * 2f;
        }
        Vector3 center = Time.time < _loiterUntil ? pos : _home;
        for (int i = 0; i < 6; i++)
        {
            if (!NpcNav.TryRandomPoint(pos, center, 0.5f, radius, false, out Vector3 p)) break;
            if (goal == Goal.Wander && safe && Dist(p, camp) < NpcNav.CampRadius + 4f) continue;   // 安全期：别靠近营地
            GoTo(p, Def.speed * 0.5f, true);
            return;
        }
    }

    bool KeepTarget(NpcData n, Vector3 pos, bool safe)
    {
        if (n == null || n.IsDead) return false;
        if (Def.kind == EnemyKind.Creature && n.isBroken) return false;
        NpcMover m = NpcView.GetMover(n.id);
        if (m == null) return false;
        Vector3 tp = m.transform.position;
        if (Dist(pos, tp) > Def.sight * 1.8f) return false;                          // 跟丢了
        if (goal == Goal.Wander && safe && NearCamp(tp, 1.5f)) return false;          // 安全期：人逃回营地就不追了
        return true;
    }

    bool WantsToAttack(NpcData n, Vector3 pos, bool safe)
    {
        Vector3 np = NpcPos(n);
        if (goal == Goal.Wander && safe && NearCamp(np, 2f)) return false;
        if (Def.kind == EnemyKind.Creature) return true;            // 造物：见人就扑
        if (_lastHitBy == n.id) return true;                        // 打过我的
        if (goal == Goal.Raid) return Dist(pos, np) < 2.5f;         // 来抢东西的：先奔仓库，只打挡在跟前的人
        if (goal != Goal.Wander) return true;
        return CountNpcsNear(np, 4f) <= 1 && !NearCamp(np, 2f);     // 游荡时只找落单的人
    }

    // 路被挡住了？（导航只能走到半路、已经走到头 / 卡着不动）→ 会破墙就去拆，不会就放弃、在外面转一会儿
    // closeEnough：离目标这么近就算到了（营地中心可能被终端机占着，站不上去）
    void CheckBlocked(Vector3 target, float closeEnough = 0f)
    {
        if (_agent == null || !_agent.isOnNavMesh || _agent.pathPending) return;
        if (Time.time < _wallGrace) return;   // 刚拆掉一段墙：等导航网格更新完再说（不然会接着去拆旁边那段）
        if (closeEnough > 0f && Dist(transform.position, target) < closeEnough) return;
        bool partial = _agent.pathStatus == NavMeshPathStatus.PathPartial || _agent.pathStatus == NavMeshPathStatus.PathInvalid;
        bool atEnd = partial && _agent.remainingDistance < 0.6f;   // 能走的地方已经走到头了
        bool stuck = _stuckTimer > 2.5f;
        if (!atEnd && !stuck) return;   // 还在往能到的地方走：先走着
        if (Def.wallDamage > 0f)
        {
            GameObject wall = StructureHealth.FindBlocking(transform.position, target, 3.5f);
            if (wall != null)
            {
                _wall = wall;
                _wallTimer = 0f;
                state = State.BreakWall;
                _hasDest = false;
                return;
            }
        }
        if (!atEnd) return;
        _target = null;               // 进不去也拆不了：放弃，在外面转一会儿再试
        _loiterUntil = Time.time + 8f;
        _hasDest = false;
    }

    // ============ 动手 ============

    void AttackTarget()
    {
        if (_vsAba) { AttackAba(); return; }
        if (_target == null || _target.IsDead) { _target = null; return; }
        Vector3 tp = NpcPos(_target);
        Face(tp);
        if (_attackCd > 0f) return;
        if (Dist(transform.position, tp) > Def.attackRange + 0.25f) return;
        _attackCd = Def.attackInterval * Random.Range(0.9f, 1.1f);
        if (Def.Ranged)
        {
            // 远程（带枪的）：子弹真的飞过去，路上先碰到什么就打中什么（Ballistics；不掷命中率）
            Vector2 aim = tp - transform.position;
            Vector2 fwd = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right;
            Vector3 muzzle = transform.position + new Vector3(fwd.x, fwd.y, 0f) * 0.25f * Def.scale + new Vector3(0f, 0.05f, 0f);
            NpcMover tm = NpcView.GetMover(_target.id);
            UnityEngine.AI.NavMeshAgent ta = tm != null ? tm.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;
            Vector3 tv = ta != null && ta.isActiveAndEnabled ? ta.velocity : Vector3.zero;
            Ballistics.FireFromEnemy(this, muzzle, tp, tv, Def.damage, Velocity.sqrMagnitude > 0.05f);
            return;
        }
        _lunge = 1f;
        float hit = Def.kind == EnemyKind.Creature ? 0.8f : 0.7f;   // 近战：照旧按命中率
        if (Random.value < hit) NpcCombat.OnEnemyHit(_target, Def.damage, this);
    }

    // 打 ABA 队员（近战照旧按命中率；带枪的走 Ballistics）
    void AttackAba()
    {
        if (_aba == null || !_aba.Alive) { _aba = null; _vsAba = false; return; }
        Vector3 tp = _aba.transform.position;
        Face(tp);
        if (_attackCd > 0f) return;
        if (Dist(transform.position, tp) > Def.attackRange + 0.25f) return;
        _attackCd = Def.attackInterval * Random.Range(0.9f, 1.1f);
        if (Def.Ranged)
        {
            Vector2 aim = tp - transform.position;
            Vector2 fwd = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right;
            Vector3 muzzle = transform.position + new Vector3(fwd.x, fwd.y, 0f) * 0.25f * Def.scale + new Vector3(0f, 0.05f, 0f);
            Ballistics.FireFromEnemy(this, muzzle, tp, _aba.Velocity, Def.damage, Velocity.sqrMagnitude > 0.05f);
            return;
        }
        _lunge = 1f;
        float hit = Def.kind == EnemyKind.Creature ? 0.8f : 0.7f;
        if (Random.value < hit) _aba.TakeHit(Def.damage, (tp - transform.position).normalized, this, null);
    }

    void AttackWall(float dt)
    {
        if (_wall == null) { state = State.Chase; _hasDest = false; return; }   // 墙已经没了
        Vector3 wp = _wall.transform.position;
        float reach = StructureHealth.Reach(_wall) + Def.attackRange + 0.45f;
        _wallTimer += dt;
        if (Dist(transform.position, wp) > reach)
        {
            if (_wallTimer > 8f) { _wall = null; _loiterUntil = Time.time + 5f; return; }   // 走不到墙边：换个法子
            GoTo(wp, Def.speed);
            return;
        }
        Halt();
        Face(wp);
        if (_attackCd > 0f) return;
        _attackCd = Def.attackInterval;
        _lunge = 1f;
        if (StructureHealth.Damage(_wall, Def.wallDamage, this))
        {
            _wall = null;
            _stuckTimer = 0f;
            _hasDest = false;
            _wallGrace = Time.time + 2.5f;
        }
    }

    void DoLoot(float dt)
    {
        if (_lootTimer <= 0f && Visible) NpcSpeechBubble.Show(transform, LanguageGuard.Pick("（翻找）", "（物色）"), 2f);
        _lootTimer += dt;
        if (_lootTimer < 2.5f) return;
        _looted = true;
        EnemyDirector.Loot(this, Def.steal);
        state = State.Leave;
        _hasDest = false;
    }

    // ============ 挨打 / 死 ============

    public void TakeDamage(float dmg, NpcData by)
    {
        if (!Alive || dmg <= 0f) return;
        Hp -= dmg;
        _flash = 0.15f;
        _barUntil = Time.time + 4f;
        UpdateBar();
        if (by != null && !by.IsDead)
        {
            _lastHitBy = by.id;
            // 人类：谁打我我打谁；造物：多半也会转过来
            if (_target == null || Def.kind == EnemyKind.Human || Random.value < 0.4f) _target = by;
            if (goal == Goal.Wander) _loiterUntil = 0f;
        }
        if (Hp <= 0f) Die(by);
    }

    // 挨了 ABA 清除小组的打（Ballistics 调）
    public void TakeHitFromAba(float dmg, AbaOperative by)
    {
        if (!Alive || dmg <= 0f) return;
        _abaHitAt = Time.time;
        if (by != null && (Def.kind == EnemyKind.Human || _aba == null || Random.value < 0.4f)) _aba = by;
        if (goal == Goal.Wander) _loiterUntil = 0f;
        if (Hp - dmg <= 0f) KilledByAba = true;
        TakeDamage(dmg, null);
    }

    // 被击退时打断一下（这几秒内不能出手）
    public void Stagger(float seconds)
    {
        if (!Alive) return;
        _attackCd = Mathf.Max(_attackCd, seconds);
        _lunge = 0f;
    }

    void Die(NpcData by)
    {
        state = State.Dead;
        Hp = 0f;
        All.Remove(this);
        if (_agent != null)
        {
            if (_agent.isOnNavMesh) _agent.ResetPath();
            _agent.enabled = false;
        }
        if (_bar != null) _bar.gameObject.SetActive(false);
        EnemyDirector.OnEnemyKilled(this, by);
    }

    void Despawn()
    {
        All.Remove(this);
        Destroy(gameObject);
    }

    void FadeOut(float dt)
    {
        _fade -= dt;
        if (_body != null)
        {
            Color c = _body.color;
            c.a = Mathf.Clamp01(_fade);
            _body.color = c;
            _bodyT.localScale = new Vector3(_bodyScale * (1f + (1f - _fade) * 0.3f), _bodyScale * Mathf.Max(0.05f, _fade), 1f);
        }
        if (_fade <= 0f) Destroy(gameObject);
    }

    // ============ 移动小工具 ============

    void GoTo(Vector3 p, float speed, bool force = false)
    {
        if (_agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh) return;
        _agent.speed = speed * Mathf.Max(0.2f, TerrainSystem.SpeedFactorAt(transform.position));
        if (!force && _hasDest && _repathTimer > 0f && (p - _dest).sqrMagnitude < 0.09f) return;
        p.z = transform.position.z;
        _agent.SetDestination(p);
        _dest = p;
        _hasDest = true;
        _repathTimer = 0.3f;
    }

    void Halt()
    {
        if (_hasDest && _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh) _agent.ResetPath();
        _hasDest = false;
    }

    bool Arrived()
    {
        if (_agent == null || !_agent.isOnNavMesh) return true;
        return !_agent.pathPending && _agent.remainingDistance <= 0.25f;
    }

    // 想走却几乎没动 → 卡住了
    void TrackStuck(float dt)
    {
        Vector3 p = transform.position;
        float moved = Vector2.Distance(p, _lastPos);
        _lastPos = p;
        bool trying = _hasDest && _agent != null && _agent.isOnNavMesh && !_agent.pathPending && _agent.remainingDistance > 0.4f;
        if (trying && moved < 0.2f * dt) _stuckTimer += dt;
        else _stuckTimer = 0f;
    }

    Vector3 ExitPoint(Vector3 camp, Vector3 pos)
    {
        Vector2 dir = pos - camp;
        if (dir.sqrMagnitude < 0.01f) dir = Random.insideUnitCircle;
        dir.Normalize();
        Vector3 far = camp + new Vector3(dir.x, dir.y, 0f) * (EnemyDirector.LeaveDistance + 8f);
        far = ClampToGrid(far);
        if (NpcNav.Sample(far, 4f, out Vector3 s)) return s;
        if (NpcNav.TryRandomPoint(pos, pos, 6f, 14f, false, out Vector3 r)) return r;
        return far;
    }

    static Vector3 RandomNear(Vector3 c, float r)
    {
        return NpcNav.TryRandomPoint(c, c, 0f, r, false, out Vector3 p) ? p : c;
    }

    static Vector3 ClampToGrid(Vector3 p)
    {
        GridManager g = GridManager.Instance;
        if (g == null) return p;
        float m = 1f;
        float x0 = g.origin.x + m, y0 = g.origin.y + m;
        float x1 = g.origin.x + g.gridWidth * g.cellSize - m, y1 = g.origin.y + g.gridHeight * g.cellSize - m;
        return new Vector3(Mathf.Clamp(p.x, x0, x1), Mathf.Clamp(p.y, y0, y1), p.z);
    }

    // ============ 查人 ============

    NpcData NearestNpc(Vector3 pos, float range)
    {
        WorldState w = World;
        if (w == null) return null;
        NpcData best = null;
        float bestD = range;
        foreach (NpcData n in w.npcs)
        {
            if (n == null || n.IsDead) continue;
            if (Def.kind == EnemyKind.Creature && n.isBroken) continue;   // 彻底异变的人：造物不再把他当猎物
            NpcMover m = NpcView.GetMover(n.id);
            if (m == null) continue;
            float d = Dist(pos, m.transform.position);
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    static Vector3 NpcPos(NpcData n)
    {
        NpcMover m = n != null ? NpcView.GetMover(n.id) : null;
        return m != null ? m.transform.position : new Vector3(99999f, 99999f, 0f);
    }

    static int CountNpcsNear(Vector3 p, float r)
    {
        WorldState w = World;
        if (w == null) return 0;
        int k = 0;
        foreach (NpcData n in w.npcs)
            if (n != null && !n.IsDead && Dist(NpcPos(n), p) <= r) k++;
        return k;
    }

    static bool FarFromNpcs(Vector3 p, float r)
    {
        WorldState w = World;
        if (w == null) return true;
        foreach (NpcData n in w.npcs)
            if (n != null && !n.IsDead && Dist(NpcPos(n), p) < r) return false;
        return true;
    }

    public static bool NearCamp(Vector3 p, float margin)
    {
        if (CampZone.Instance != null && CampZone.Instance.Ready) return CampZone.Contains(p, margin);
        return Dist(p, NpcNav.CampCenter(p)) <= NpcNav.CampRadius + margin;
    }

    static float Dist(Vector3 a, Vector3 b) => Vector2.Distance(a, b);

    // ============ 给别的系统查 ============

    // 离 p 最近、range 以内的活着的敌人（includeHidden = 迷雾里的也算：NPC 就在旁边，自己看得见；hostileOnly = 只算冲着人来的）
    public static Enemy NearestTo(Vector3 p, float range, bool includeHidden, bool hostileOnly = false)
    {
        Enemy best = null;
        float bestD = range;
        foreach (Enemy e in All)
        {
            if (e == null || !e.Alive || (!includeHidden && !e.Visible) || (hostileOnly && !e.Hostile)) continue;
            if (e.state == State.Leave || e.state == State.Flee) { if (Dist(e.transform.position, p) > 1.5f) continue; }   // 正在跑的不用管（除非贴脸）
            float d = Dist(e.transform.position, p);
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    public static int CountAlive(string id = null)
    {
        int k = 0;
        foreach (Enemy e in All) if (e != null && e.Alive && (id == null || e.Def.id == id)) k++;
        return k;
    }

    // ============ 外观 ============

    void BuildLook()
    {
        GameObject b = new GameObject("Body");
        _bodyT = b.transform;
        _bodyT.SetParent(transform, false);
        _bodyT.localPosition = new Vector3(0f, FeetY, 0f);
        _body = b.AddComponent<SpriteRenderer>();
        Sprite art = Resources.Load<Sprite>("EnemyArt/" + Def.id);
        _body.sprite = art != null ? art : Placeholder(Def);
        _body.color = Color.white;
        _body.sortingOrder = 1;   // 和 NPC 同层（建筑是 0）
        float h = _body.sprite != null ? _body.sprite.bounds.size.y : 1f;
        _bodyScale = h > 0.01f ? 0.9f * Def.scale / h : Def.scale;   // 高度 ≈ 0.9 × 体型（体型 1 和 NPC 差不多高）
        _bodyT.localScale = new Vector3(_bodyScale, _bodyScale, 1f);

        // 血条（挨打后显示几秒）
        _bar = EnemyDirector.Quad("HpBar", transform, new Color(0f, 0f, 0f, 0.75f), 6);
        _bar.localPosition = new Vector3(0f, FeetY + 0.9f * Def.scale + 0.14f, 0f);
        _bar.localScale = new Vector3(0.7f, 0.07f, 1f);
        _fill = EnemyDirector.Quad("Fill", _bar, Def.kind == EnemyKind.Creature ? new Color(0.78f, 0.35f, 0.95f) : new Color(0.95f, 0.38f, 0.25f), 7);
        _bar.gameObject.SetActive(false);
    }

    void UpdateBar()
    {
        if (_fill == null) return;
        float r = Mathf.Clamp01(Hp / Mathf.Max(1f, Def.maxHp));
        _fill.localScale = new Vector3(r, 1f, 1f);
        _fill.localPosition = new Vector3(-(1f - r) * 0.5f, 0f, 0f);
    }

    void UpdateVisible()
    {
        bool vis = !NpcNav.IsUnseen(transform.position);
        if (vis && !_seenOnce)
        {
            _seenOnce = true;
            NpcSpeechBubble.Show(transform, Def.IsStrong ? "！！ " + Def.Name : Def.Name, 2.5f);   // 第一次被看见：头顶冒一下名字
            EnemyDirector.OnEnemySpotted(this);
        }
        Visible = vis;
        if (_body != null) _body.enabled = vis;
        if (_bar != null) _bar.gameObject.SetActive(vis && Time.time < _barUntil);
        if (!vis) NpcSpeechBubble.Hide(transform);
    }

    void Face(Vector3 p)
    {
        if (_body == null) return;
        float dx = p.x - transform.position.x;
        if (dx < -0.01f) _body.flipX = true;
        else if (dx > 0.01f) _body.flipX = false;
    }

    // 代码动画：待机时轻轻起伏，移动时颠得快一点，出手时往前一扑，挨打时闪红
    void Animate(float dt)
    {
        if (_bodyT == null) return;
        Vector3 v = _agent != null && _agent.isActiveAndEnabled ? _agent.velocity : Vector3.zero;
        bool moving = v.sqrMagnitude > 0.01f;
        if (moving && _body != null)
        {
            if (v.x < -0.01f) _body.flipX = true;
            else if (v.x > 0.01f) _body.flipX = false;
        }
        _phase += dt * (moving ? 9f : 2.6f);
        float amp = moving ? 0.07f : 0.035f;
        float s = Mathf.Sin(_phase);
        _lunge = Mathf.Max(0f, _lunge - dt * 5f);
        float dir = _body != null && _body.flipX ? -1f : 1f;
        if (Def.kind == EnemyKind.Creature)
            _bodyT.localScale = new Vector3(_bodyScale * (1f - amp * 0.5f * s + _lunge * 0.12f), _bodyScale * (1f + amp * s - _lunge * 0.1f), 1f);
        else
            _bodyT.localScale = new Vector3(_bodyScale, _bodyScale * (1f + amp * 0.3f * s), 1f);
        float hop = Def.kind == EnemyKind.Human && moving ? Mathf.Abs(s) * 0.04f : 0f;
        _bodyT.localPosition = new Vector3(dir * _lunge * 0.12f, FeetY + hop, 0f);
        if (_body != null)
        {
            _flash = Mathf.Max(0f, _flash - dt);
            _body.color = _flash > 0f ? new Color(1f, 0.45f, 0.45f, 1f) : Color.white;
        }
        if (_bar != null && _bar.gameObject.activeSelf && Time.time >= _barUntil) _bar.gameObject.SetActive(false);
    }

    // 没有美术图时的占位图：颜色画进贴图里（这样眼睛能保持亮色），轴心在脚底，高 1 单位
    static Sprite Placeholder(EnemyDef d)
    {
        if (_placeholders.TryGetValue(d.id, out Sprite cached) && cached != null) return cached;
        const int W = 48, H = 64;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[W * H];
        float seed = Mathf.Abs(d.id.GetHashCode() % 1000) * 0.137f;
        Color baseC = d.color;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W * 2f - 1f;   // -1 ~ 1（左 → 右）
                float v = (y + 0.5f) / H;             // 0 ~ 1（脚 → 头）
                float a;
                float shade;
                bool glow = false;
                if (d.kind == EnemyKind.Creature)
                {
                    // 一团边缘不规则、上宽下窄的影子，底部拖着散开
                    float dy = (v - 0.47f) / 0.47f;
                    float width = Mathf.Sqrt(Mathf.Max(0f, 1f - dy * dy)) * (0.7f + 0.3f * v);
                    float edge = width * (0.8f + 0.35f * Mathf.PerlinNoise(seed + v * 7f, seed + Mathf.Abs(u) * 3f));
                    a = Mathf.Clamp01((edge - Mathf.Abs(u)) * 10f);
                    if (v < 0.15f) a *= Mathf.Clamp01(v / 0.15f + 0.2f * Mathf.PerlinNoise(seed + u * 9f, seed));
                    shade = 0.65f + 0.45f * v;
                    float e1 = (u - 0.24f) * (u - 0.24f) + (v - 0.63f) * (v - 0.63f) * 6f;
                    float e2 = (u + 0.2f) * (u + 0.2f) + (v - 0.66f) * (v - 0.66f) * 6f;
                    glow = a > 0.6f && (e1 < 0.012f || e2 < 0.009f);
                }
                else
                {
                    // 人形剪影：头 + 肩 + 身体，两腿之间留缝；头上一道亮色护目镜
                    float head = (u / 0.3f) * (u / 0.3f) + ((v - 0.83f) / 0.12f) * ((v - 0.83f) / 0.12f);
                    float half = v > 0.62f ? Mathf.Lerp(0.55f, 0.25f, (v - 0.62f) / 0.1f) : Mathf.Lerp(0.42f, 0.55f, v / 0.62f);
                    bool body = v > 0.03f && v < 0.72f && Mathf.Abs(u) < half;
                    if (v < 0.3f && Mathf.Abs(u) < 0.07f) body = false;
                    a = head < 1f || body ? 1f : 0f;
                    shade = 0.75f + 0.25f * v;
                    glow = head < 1f && v > 0.8f && v < 0.86f && Mathf.Abs(u) < 0.24f;
                }
                if (glow) { px[y * W + x] = new Color32(255, 236, 190, 255); continue; }
                px[y * W + x] = new Color32((byte)(255 * Mathf.Clamp01(baseC.r * shade)), (byte)(255 * Mathf.Clamp01(baseC.g * shade)),
                                            (byte)(255 * Mathf.Clamp01(baseC.b * shade)), (byte)(255 * Mathf.Clamp01(a * baseC.a)));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), H);
        _placeholders[d.id] = sp;
        return sp;
    }
}
