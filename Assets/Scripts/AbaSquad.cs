using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// ABA 清除小组的"小组大脑"（模拟 SWAT 战术，纯规则，每 0.2 秒想一次）。
//
// 【共享情报】任何一名队员看见（视野内、没被墙挡）或贴身听见的目标，全组都知道（无线电）；8 秒没再看见就当跟丢。
// 【四个阶段】
//   搜索 Sweep：楔形队形前进。尖兵（1 号）和殿后（最后一名）举枪警戒前方 / 后方，两翼枪口朝下、各看左前 / 右前。
//     往最近的目标方向搜（终端扫描，只知道大概方位 ±3 格）；亚空间里一个活的都没有了 → 撤离。
//   接敌 Contact：
//     · 分配目标 + 集火：每个贴近（4 格内）的威胁至少分一个人盯，剩下的人集火最危险的那个（强敌 / 远程 / 正在打队员的优先）。
//     · 交替掩护前进：打不到（太远 / 被墙挡）时分成两个火力组，一组原地掩护射击，另一组跑到下一个位置（离目标合适的距离、
//       看得见目标、旁边有掩体、不挡队友火线），2.5 秒换一次。
//     · 后撤射击：近战的东西扑到 2.6 格以内，被扑的那个人边退边打，队友集火它；贴脸时拔手枪把它推开（击退）。
//     · 错开换弹：接敌时同一时间只让一个人换弹，其他人打空了就转手枪。
//     · 拉开间距：两个人挤在一起（0.7 格内）时后面那个往旁边让。
//   巩固 Consolidate：3 秒没再看见目标 → 喊"区域清除"，围着组长站成菱形、枪口朝外（360° 警戒），
//     一个一个战术换弹（错开），伤重的包扎。5 秒后继续搜索。
//   撤离 Extract：没有目标了 / 只剩一个人还重伤 → 往地图外走，走远了消失。
// ============================================================================
public class AbaSquad : MonoBehaviour
{
    public static readonly List<AbaSquad> All = new List<AbaSquad>();

    public enum Phase { Sweep, Contact, Consolidate, Extract }
    public Phase phase = Phase.Sweep;

    readonly List<AbaOperative> _m = new List<AbaOperative>();
    readonly List<AbaTarget> _known = new List<AbaTarget>();
    readonly List<AbaTarget> _threats = new List<AbaTarget>();
    Vector3 _waypoint;
    bool _hasWaypoint;
    Vector2 _heading = Vector2.right;
    float _think, _phaseAt, _lastSeen = -99f, _boundAt, _emptySince = -1f, _talkAt, _consolidateReload;
    int _boundTeam;
    bool _killedSomething;

    // 楔形队形（相对前进方向：x = 前后，y = 左右）和各自的警戒方向（度，0 = 正前）
    static readonly Vector2[] Wedge = { new Vector2(0f, 0f), new Vector2(-0.9f, 0.8f), new Vector2(-0.9f, -0.8f), new Vector2(-1.9f, 0f), new Vector2(-2.6f, 0.7f), new Vector2(-2.6f, -0.7f) };
    static readonly float[] WedgeSector = { 0f, 70f, -70f, 180f, 120f, -120f };

    public bool InContact => phase == Phase.Contact;
    public IReadOnlyList<AbaOperative> Members => _m;

    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;
    static AbaForce F => AbaForce.Ensure();

    // ============ 生成 ============

    public static AbaSquad Create(Vector3 at, int count)
    {
        AbaSquad s = new GameObject("AbaSquad").AddComponent<AbaSquad>();
        Vector3 camp = NpcNav.CampCenter(at);
        Vector2 toCamp = camp - at;
        s._heading = toCamp.sqrMagnitude > 0.01f ? toCamp.normalized : Vector2.right;
        for (int i = 0; i < count; i++)
            s._m.Add(AbaOperative.Spawn(s, i, at + (Vector3)s.Slot(i, Vector2.zero, s._heading) * 0.8f));
        s._phaseAt = Time.time;
        All.Add(s);
        if (LogManager.Instance != null) LogManager.Instance.AddLog(AbaForce.L("aba_name"), AbaForce.L("aba_log_deploy"), "");
        if (s._m.Count > 0) s._m[0].Say("aba_call_insert");
        return s;
    }

    void OnDestroy() { All.Remove(this); }

    void Update()
    {
        _think -= Time.deltaTime;
        if (_think > 0f) return;
        _think = 0.2f;
        Think();
    }

    // ============ 想 ============

    void Think()
    {
        _m.RemoveAll(o => o == null || !o.Alive);
        if (_m.Count == 0) { Destroy(gameObject, 1f); enabled = false; return; }
        Perceive();

        bool seen = _threats.Count > 0;
        if (seen) _lastSeen = Time.time;
        AbaOperative lead = _m[0];

        // 阶段切换
        if (phase != Phase.Extract)
        {
            if (seen && phase != Phase.Contact) { SetPhase(Phase.Contact); Spotter().Say(AnyStrong() ? "aba_call_contact_strong" : "aba_call_contact"); }
            else if (!seen && phase == Phase.Contact && Time.time - _lastSeen > 3f)
            {
                SetPhase(Phase.Consolidate);
                if (_killedSomething) lead.Say("aba_call_clear");
                _killedSomething = false;
            }
            else if (phase == Phase.Consolidate && Time.time - _phaseAt > 5f) SetPhase(Phase.Sweep);
            if (_m.Count == 1 && lead.Hp < lead.MaxHp * 0.4f && !seen) SetPhase(Phase.Extract);
            int rounds = 0;
            foreach (AbaOperative o in _m) rounds += o.Rounds("rifle") + o.Rounds("pistol");
            if (rounds < 6 * _m.Count) SetPhase(Phase.Extract);                // 子弹快打光了：撤
        }

        switch (phase)
        {
            case Phase.Sweep: DoSweep(lead); break;
            case Phase.Contact: DoContact(); break;
            case Phase.Consolidate: DoConsolidate(lead); break;
            case Phase.Extract: DoExtract(lead); break;
        }
    }

    void SetPhase(Phase p)
    {
        if (phase == p) return;
        phase = p;
        _phaseAt = Time.time;
        _hasWaypoint = false;
        _boundAt = Time.time;
        if (p != Phase.Contact) foreach (AbaOperative o in _m) o.target = null;
        if (p == Phase.Extract && _m.Count > 0) _m[0].Say("aba_call_extract");
    }

    // ============ 看 ============

    // 共享情报：谁看见了 / 贴身听见了，全组都知道
    void Perceive()
    {
        float sight = F.sight;
        foreach (Enemy e in Enemy.All)
        {
            if (!AbaForce.IsTarget(e)) continue;
            if (CanSee(e.transform.position, sight)) Note(e, null, e.transform.position);
        }
        WorldState w = World;
        if (w != null)
        {
            foreach (NpcData n in w.npcs)
            {
                if (!AbaForce.IsTarget(n)) continue;
                NpcMover mv = NpcView.GetMover(n.id);
                if (mv != null && CanSee(mv.transform.position, sight)) Note(null, n, mv.transform.position);
            }
        }
        _known.RemoveAll(k => !k.Alive || Time.time - k.seenAt > 8f);
        _threats.Clear();
        foreach (AbaTarget k in _known) if (Time.time - k.seenAt < 1.5f) _threats.Add(k);
    }

    bool CanSee(Vector3 p, float sight)
    {
        foreach (AbaOperative o in _m)
        {
            float d = Vector2.Distance(o.transform.position, p);
            if (d < 1.8f) return true;                                         // 贴身：听得见
            if (d < sight && Ballistics.LineClear(o.transform.position, p)) return true;
        }
        return false;
    }

    AbaTarget Note(Enemy e, NpcData n, Vector3 pos)
    {
        AbaTarget k = _known.Find(t => t.Same(e, n));
        if (k == null) { k = e != null ? AbaTarget.Of(e) : AbaTarget.Of(n); _known.Add(k); }
        k.lastSeen = pos;
        k.seenAt = Time.time;
        return k;
    }

    // ============ 接敌 ============

    void DoContact()
    {
        AssignTargets();
        Vector3 c = ThreatCenter();
        bool needPush = false;
        foreach (AbaOperative o in _m)
        {
            if (o.target == null) { needPush = true; continue; }
            float d = Vector2.Distance(o.target.Position, o.transform.position);
            if (d > o.Weapon.range || !Ballistics.LineClear(o.transform.position, o.target.Position)) needPush = true;
        }
        if (Time.time - _boundAt > 2.5f)                                      // 两个火力组轮换：一组掩护，一组前进
        {
            _boundAt = Time.time;
            _boundTeam = 1 - _boundTeam;
            if (needPush) foreach (AbaOperative o in _m) if (o.Slot % 2 == _boundTeam) { o.Say("aba_call_move"); break; }
        }

        Vector2 toC = c - _m[0].transform.position;
        Vector2 face = toC.sqrMagnitude > 0.01f ? toC.normalized : _heading;
        _heading = face;
        for (int i = 0; i < _m.Count; i++)
        {
            AbaOperative o = _m[i];
            o.alert = true;
            o.sector = Rotate(face, WedgeSector[Mathf.Min(i, WedgeSector.Length - 1)] * 0.5f);   // 没目标的人看两侧 / 后方
            o.run = false;
            o.hasMove = false;

            // 后撤射击：近战的东西扑过来了
            AbaTarget rush = NearestMelee(o.transform.position, 2.6f);
            if (rush != null)
            {
                Vector2 away = o.transform.position - rush.Position;
                away = away.sqrMagnitude > 0.001f ? away.normalized : -face;
                if (NpcNav.Sample(o.transform.position + (Vector3)(away * 1.8f), 1f, out Vector3 back))
                {
                    o.moveTo = back;
                    o.hasMove = true;
                    if (Random.value < 0.25f) o.Say("aba_call_back");
                }
                continue;
            }

            // 交替掩护前进：轮到的那组去下一个位置，另一组原地打
            if (needPush && o.Slot % 2 == _boundTeam && !o.Busy)
            {
                o.moveTo = BoundPoint(o, c);
                o.hasMove = true;
                o.run = true;
                continue;
            }

            // 拉开间距
            foreach (AbaOperative b in _m)
            {
                if (b == o || b.Slot > o.Slot) continue;
                Vector2 gap = o.transform.position - b.transform.position;
                if (gap.magnitude < 0.7f)
                {
                    Vector2 side = new Vector2(-face.y, face.x) * (o.Slot % 2 == 0 ? 1f : -1f);
                    if (NpcNav.Sample(o.transform.position + (Vector3)(side * 0.8f), 0.6f, out Vector3 s)) { o.moveTo = s; o.hasMove = true; }
                    break;
                }
            }
        }
    }

    // 分配目标：贴近的威胁每个至少一个人盯，剩下的集火最危险的
    void AssignTargets()
    {
        _threats.Sort((a, b) => Score(b).CompareTo(Score(a)));
        var free = new List<AbaOperative>(_m);
        foreach (AbaOperative o in _m) o.target = null;

        foreach (AbaTarget t in _threats)
        {
            if (free.Count == 0) break;
            if (MinDist(t.Position) > 4f) continue;
            AbaOperative best = null;
            float bd = float.MaxValue;
            foreach (AbaOperative o in free)
            {
                float d = Vector2.Distance(o.transform.position, t.Position);
                if (d < bd && (d < 1.5f || Ballistics.LineClear(o.transform.position, t.Position))) { bd = d; best = o; }
            }
            if (best != null) { best.target = t; free.Remove(best); }
        }
        foreach (AbaOperative o in free)
        {
            foreach (AbaTarget t in _threats)                                  // 按危险程度从高到低，第一个打得到的
            {
                if (Ballistics.LineClear(o.transform.position, t.Position)) { o.target = t; break; }
            }
            if (o.target == null && _threats.Count > 0) o.target = _threats[0];   // 都打不到：盯着最危险的，等前进
        }
    }

    float Score(AbaTarget t)
    {
        float d = MinDist(t.Position);
        float s = t.Danger * (1f + 2f / (1f + d));
        if (t.enemy != null && t.enemy.state == Enemy.State.Attack) s += 3f;   // 正在打人
        if (t.Melee && d < 2.5f) s += 4f;                                      // 扑到跟前了
        return s;
    }

    // 下一个跃进位置：离目标合适的距离、看得见目标、旁边有掩体、不挤、不挡掩护组的火线
    Vector3 BoundPoint(AbaOperative o, Vector3 c)
    {
        Vector3 pos = o.transform.position;
        Vector2 dir = c - pos;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : _heading;
        float ideal = o.HasPistol ? 3.5f : 5.5f;
        Vector3 best = pos;
        float bestS = float.MinValue;
        for (int i = 0; i < 10; i++)
        {
            Vector2 d = Rotate(dir, Random.Range(-60f, 60f));
            Vector3 p = pos + (Vector3)(d * Random.Range(1.5f, 3f));
            if (!NpcNav.Sample(p, 0.8f, out p)) continue;
            float s = -Mathf.Abs(Vector2.Distance(p, c) - ideal);
            s += Ballistics.LineClear(p, c) ? 3f : -4f;
            s += Mathf.Min(3, Ballistics.CoverAround(p)) * 0.5f;
            foreach (AbaOperative m in _m)
            {
                if (m == o) continue;
                if (Vector2.Distance(m.transform.position, p) < 1f) s -= 2f;
                if (m.target != null && SegmentDistance(m.transform.position, m.target.Position, p) < 0.5f) s -= 3f;   // 别走到队友枪口前面
            }
            if (s > bestS) { bestS = s; best = p; }
        }
        return best;
    }

    // ============ 搜索 / 巩固 / 撤离 ============

    void DoSweep(AbaOperative lead)
    {
        if (!_hasWaypoint || Vector2.Distance(lead.transform.position, _waypoint) < 1.2f)
        {
            if (!PickHuntPoint(lead.transform.position, out _waypoint))
            {
                if (_emptySince < 0f) _emptySince = Time.time;
                if (Time.time - _emptySince > 15f) { SetPhase(Phase.Extract); return; }   // 一个活的都没有了：撤
                NpcNav.TryRandomPoint(lead.transform.position, lead.transform.position, 3f, 7f, false, out _waypoint);
            }
            else _emptySince = -1f;
            _hasWaypoint = true;
        }
        Vector2 h = _waypoint - lead.transform.position;
        if (h.sqrMagnitude > 0.04f) _heading = Vector2.Lerp(_heading, h.normalized, 0.35f).normalized;
        Formation(lead, _waypoint, false);
        // 走在路上没事干：错开着把没打满的弹匣换掉
        StaggerReload(0.5f);
    }

    void DoConsolidate(AbaOperative lead)
    {
        // 菱形站位、枪口朝外（360° 警戒）
        Vector3 center = lead.transform.position;
        for (int i = 0; i < _m.Count; i++)
        {
            AbaOperative o = _m[i];
            o.target = null;
            o.alert = true;
            o.run = false;
            Vector2 outward = Rotate(_heading, i * (360f / _m.Count));
            o.sector = outward;
            if (i == 0) { o.hasMove = false; continue; }
            if (NpcNav.Sample(center + (Vector3)(outward * 1.0f), 0.8f, out Vector3 p)) { o.moveTo = p; o.hasMove = true; }
        }
        StaggerReload(0.8f);
        foreach (AbaOperative o in _m) if (o.TryHeal()) break;               // 一次一个人包扎
    }

    void DoExtract(AbaOperative lead)
    {
        if (!_hasWaypoint)
        {
            Vector3 camp = NpcNav.CampCenter(lead.transform.position);
            Vector2 away = lead.transform.position - camp;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector2.right;
            Vector3 p = lead.transform.position + (Vector3)(away * 14f);
            if (!NpcNav.Sample(p, 4f, out _waypoint)) _waypoint = p;
            _hasWaypoint = true;
        }
        Formation(lead, _waypoint, true);
        float far = EnemyDirector.LeaveDistance;
        bool gone = true;
        foreach (AbaOperative o in _m)
            if (o.Visible || Vector2.Distance(o.transform.position, NpcNav.CampCenter(o.transform.position)) < far * 0.6f) gone = false;
        bool stuck = Time.time - _phaseAt > 40f && !lead.Visible;             // 走不出去（地图边上）：没人看见就算撤走了
        if (gone || stuck || Vector2.Distance(lead.transform.position, _waypoint) < 1.2f && !lead.Visible)
        {
            foreach (AbaOperative o in _m) if (o != null) Destroy(o.gameObject);
            Destroy(gameObject);
        }
    }

    // 楔形队形跟着组长走
    void Formation(AbaOperative lead, Vector3 dest, bool hurry)
    {
        lead.moveTo = dest;
        lead.hasMove = true;
        lead.run = hurry;
        for (int i = 0; i < _m.Count; i++)
        {
            AbaOperative o = _m[i];
            int k = Mathf.Min(i, Wedge.Length - 1);
            o.sector = Rotate(_heading, WedgeSector[k]);
            o.alert = i == 0 || i == _m.Count - 1;                             // 尖兵、殿后举枪；两翼枪口朝下
            o.target = null;
            if (i == 0) continue;
            Vector3 slot = lead.transform.position + (Vector3)Slot(k, Vector2.zero, _heading);
            if (NpcNav.Sample(slot, 0.8f, out Vector3 p)) slot = p;
            o.moveTo = slot;
            o.hasMove = true;
            o.run = hurry || Vector2.Distance(o.transform.position, slot) > 2.5f;   // 掉队了跑两步跟上
        }
    }

    // 往哪搜：离组长最近的活目标附近（终端扫描只知道大概方位 ±3 格）
    bool PickHuntPoint(Vector3 from, out Vector3 point)
    {
        point = from;
        float best = float.MaxValue;
        bool found = false;
        foreach (AbaTarget k in _known)                                         // 先追刚跟丢的
        {
            float d = Vector2.Distance(k.lastSeen, from);
            if (d < best) { best = d; point = k.lastSeen; found = true; }
        }
        if (found) return true;
        foreach (Enemy e in Enemy.All)
        {
            if (!AbaForce.IsTarget(e)) continue;
            float d = Vector2.Distance(e.transform.position, from);
            if (d < best) { best = d; point = e.transform.position; found = true; }
        }
        WorldState w = World;
        if (w != null)
            foreach (NpcData n in w.npcs)
            {
                if (!AbaForce.IsTarget(n)) continue;
                NpcMover mv = NpcView.GetMover(n.id);
                if (mv == null) continue;
                float d = Vector2.Distance(mv.transform.position, from);
                if (d < best) { best = d; point = mv.transform.position; found = true; }
            }
        if (!found) return false;
        point += (Vector3)(Random.insideUnitCircle * 3f);
        if (NpcNav.Sample(point, 3f, out Vector3 s)) point = s;
        return true;
    }

    // 错开换弹：一次只让一个人换（弹匣最空的那个）
    void StaggerReload(float below)
    {
        if (Time.time < _consolidateReload) return;
        foreach (AbaOperative o in _m) if (o.Reloading) return;
        AbaOperative worst = null;
        foreach (AbaOperative o in _m) if (!o.Busy && o.MagFraction < below && (worst == null || o.MagFraction < worst.MagFraction)) worst = o;
        if (worst != null && worst.TryTacticalReload(below)) _consolidateReload = Time.time + 0.5f;
    }

    // ============ 队员报告 ============

    public void OnMemberHit(AbaOperative o, Enemy byEnemy, NpcData byNpc)
    {
        // 谁打的：马上全组都知道（打人的幸存者如果带着终端就不还手）
        if (byEnemy != null && byEnemy.Alive) Note(byEnemy, null, byEnemy.transform.position);
        else if (byNpc != null && AbaForce.IsTarget(byNpc))
        {
            NpcMover mv = NpcView.GetMover(byNpc.id);
            if (mv != null) Note(null, byNpc, mv.transform.position);
        }
    }

    public void OnKill(AbaOperative o)
    {
        _killedSomething = true;
        if (Random.value < 0.5f) o.Say("aba_call_down");
    }

    public void OnMemberDown(AbaOperative o)
    {
        foreach (AbaOperative m in _m) if (m != o && m.Alive) { m.Say("aba_call_man_down"); break; }
    }

    public bool SomeoneReloading(AbaOperative except)
    {
        foreach (AbaOperative o in _m) if (o != except && o.Alive && o.Reloading) return true;
        return false;
    }

    // 全组喊话限流（0.8 秒一句，别刷屏）
    public bool MayTalk()
    {
        if (Time.time - _talkAt < 0.8f) return false;
        _talkAt = Time.time;
        return true;
    }

    // ============ 火线 ============

    // from → to 这条火线上有没有不能打的人（ABA 队员 / 带终端的幸存者）。who = 挡着的人
    public static bool FriendlyInLine(Vector3 from, Vector3 to, AbaOperative self, out Vector3 who)
    {
        who = Vector3.zero;
        float total = Vector2.Distance(from, to);
        const float r = 0.42f;
        foreach (AbaOperative o in AbaOperative.All)
        {
            if (o == self || o == null || !o.Alive) continue;
            if (Blocks(from, to, total, o.transform.position, r)) { who = o.transform.position; return true; }
        }
        WorldState w = World;
        if (w == null) return false;
        foreach (NpcData n in w.npcs)
        {
            if (n == null || n.IsDead || AbaForce.IsTarget(n)) continue;       // 是目标的不算"挡着"
            NpcMover mv = NpcView.GetMover(n.id);
            if (mv != null && Blocks(from, to, total, mv.transform.position, r)) { who = mv.transform.position; return true; }
        }
        return false;
    }

    static bool Blocks(Vector2 a, Vector2 b, float total, Vector2 p, float r)
    {
        Vector2 d = b - a;
        float len2 = d.sqrMagnitude;
        if (len2 < 0.0001f) return false;
        float t = Vector2.Dot(p - a, d) / len2;                                // 0 = 枪口，1 = 目标
        if (t < 0.02f || t * total > total + 0.6f) return false;              // 身后的不算；目标后面 0.6 格以内也算（打偏了会打到）
        return Vector2.Distance(a + d * t, p) < r;
    }

    // ============ 小工具 ============

    Vector2 Slot(int i, Vector2 origin, Vector2 heading)
    {
        Vector2 o = Wedge[Mathf.Clamp(i, 0, Wedge.Length - 1)];
        Vector2 side = new Vector2(-heading.y, heading.x);
        return origin + heading * o.x + side * o.y;
    }

    Vector3 ThreatCenter()
    {
        if (_threats.Count == 0) return _m[0].transform.position + (Vector3)_heading;
        Vector3 sum = Vector3.zero;
        float wsum = 0f;
        foreach (AbaTarget t in _threats) { float w = Score(t); sum += t.Position * w; wsum += w; }
        return wsum > 0f ? sum / wsum : _threats[0].Position;
    }

    AbaTarget NearestMelee(Vector3 p, float range)
    {
        AbaTarget best = null;
        float bd = range;
        foreach (AbaTarget t in _threats)
        {
            if (!t.Melee) continue;
            float d = Vector2.Distance(t.Position, p);
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    float MinDist(Vector3 p)
    {
        float best = float.MaxValue;
        foreach (AbaOperative o in _m) best = Mathf.Min(best, Vector2.Distance(o.transform.position, p));
        return best;
    }

    bool AnyStrong()
    {
        foreach (AbaTarget t in _threats) if (t.IsStrong) return true;
        return false;
    }

    // 最先看见目标的那个人（喊"接敌"）
    AbaOperative Spotter()
    {
        AbaOperative best = _m[0];
        float bd = float.MaxValue;
        foreach (AbaOperative o in _m)
            foreach (AbaTarget t in _threats)
            {
                float d = Vector2.Distance(o.transform.position, t.Position);
                if (d < bd) { bd = d; best = o; }
            }
        return best;
    }

    static float SegmentDistance(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 d = b - a;
        float len2 = d.sqrMagnitude;
        if (len2 < 0.0001f) return Vector2.Distance(a, p);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / len2);
        return Vector2.Distance(a + d * t, p);
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
}
