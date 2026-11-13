using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;     // SortingGroup：部件当成一个整体参与前后遮挡

// ============================================================================
// 怪物 1：被感染的研究员（外观 + 动画；图：Resources/EnemyArt/infected，坐标 = 怪物 PSD 2048 画布）
//   防护服坏了：没有任何灯光。面罩里是一只大眼球，瞳孔会动、微微发光；两只变异的手（镰刀一样）是武器。
//   走路 / 受击 / 倒地用通用人形骨架（CharacterRigBase）；这里只写它自己的：
//     待机：躯体慢慢转（上扬的时候往上浮、往下转的时候往下沉）+ 不规则的痉挛（身体、两只手、眼睛一起猛地抽一下）
//     眼睛：没有目标时瞳孔乱转（一跳一跳）；锁定敌人（LockOn / 测试目标）后瞳孔一直盯着它
//     攻击：两只手轮流砍——先往后抬蓄力，再猛地往前下方砍下去，停一下，慢慢收回来；身体跟着往前压
//     走路：慢、拖着走（步子小、脚抬得低、身体左右晃得厉害）
//   测试：Test Fire = 砍一下；Test Loop = 一直砍；Test Speed / Test Walk Cycle = 走；Test Aim Target = 瞳孔盯着它；受击 / 死亡开关在通用骨架里
// ============================================================================
public class InfectedRig : CharacterRigBase, IMonsterRig
{
    [Header("待机：躯体上扬下浮（角度 = 度，距离 = 单位）")]
    public float idlePeriod = 3.2f;                            // 转一个来回几秒
    public float idleTilt = 4f;                                // 往上扬 / 往下转多少度
    public float idleFloat = 0.012f;                           // 跟着往上浮 / 往下沉多少

    [Header("待机：不规则痉挛")]
    public Vector2 spasmEvery = new Vector2(1.2f, 4f);         // 隔多久抽一次（秒，随机）
    public Vector2 spasmTime = new Vector2(0.15f, 0.4f);       // 一次抽多久（秒，随机）
    public float spasmBody = 7f;                               // 身体抽多少度
    public float spasmArm = 18f;                               // 手抽多少度
    public float spasmEye = 60f;                               // 瞳孔抽多远（原图像素）
    public float spasmFreq = 22f;                              // 抽得多快（次 / 秒）

    [Header("眼睛（瞳孔）")]
    public float pupilRange = 150f;                            // 瞳孔最多离开中间多远（原图像素）
    public float pupilSpeed = 2.5f;                            // 瞳孔转过去有多快（单位 / 秒）
    public Vector2 pupilWander = new Vector2(0.4f, 1.8f);      // 没目标时多久换一个地方看（秒，随机）
    public Color pupilLightColor = new Color(1f, 0.35f, 0.45f, 1f);   // 瞳孔微微发的光
    public float pupilLightIntensity = 0.35f;                  // 光有多亮（0 = 不照亮周围，瞳孔本身还是自己发亮）
    public float pupilLightRadius = 0.7f;

    [Header("攻击：两只手轮流砍（时间 = 秒，角度 = 度，正 = 手尖往前转）")]
    public float atkWindTime = 0.32f;                          // ① 往后抬（蓄力）
    public float atkSlashTime = 0.1f;                          // ② 砍下去（越短越猛）
    public float atkHoldTime = 0.12f;                          // ③ 砍到底停一下
    public float atkReturnTime = 0.4f;                         // ④ 慢慢收回来
    public float atkWindAngle = -45f;                          // ① 手往后抬多少度
    public float atkSlashAngle = 105f;                         // ② 砍到多少度（往前上方挥过去）
    public Vector2 atkReach = new Vector2(0.18f, 0.05f);       // ② 砍的时候手往前 / 往上伸多远（单位）
    public float atkLean = -8f;                                // ② 砍的时候身体往前压多少度
    public float atkShift = 0.03f;                             // ② 身体往前冲多远（单位）

    [Header("测试")]
    public float testSpeed = -1f;                              // 固定速度走（-1 = 不测试；0.9 左右 = 它平时的速度）
    public bool testWalkCycle;                                 // 走几秒 → 停几秒循环

    // —— 原图关键点（y 向下数）——
    static readonly Vector2 P_ROOT = new Vector2(1162, 1987);     // 两脚中间的地面点
    static readonly Vector2 P_BODY = new Vector2(1200, 1642);     // 躯干底部（身体颠动、摇摆、上扬下浮都绕它）
    static readonly Vector2 P_FOOT_L = new Vector2(906, 1989), P_FOOT_R = new Vector2(1418, 1984);
    static readonly Vector2 P_ARM_L = new Vector2(487, 885), P_ARM_R = new Vector2(1731, 830);   // 两只变异手的根部（手绕它转）
    static readonly Vector2 P_EYE = new Vector2(1113, 742), P_PUPIL = new Vector2(1226.5f, 717.5f);   // 眼球中心 / 瞳孔平时的位置（朝前看）
    static readonly Vector2 P_INNER = new Vector2(1212, 769);     // 面罩里的机器（眼球后面）
    static readonly Vector2 P_ANTENNA = new Vector2(577, 790);    // 天线根部
    static readonly Vector2 HOSE1_SPAN = new Vector2(591, 1418);  // 软管1 两端的 x（两端固定、中间晃；和 npc1 同一件防护服）
    static readonly Vector2 HOSE2_SPAN = new Vector2(627, 931);   // 软管2 两端的 x

    Transform _eye, _pupil, _antenna;
    SpriteRenderer _pupilSr;
    UnityEngine.Rendering.Universal.Light2D _pupilLight;
    Pend _antRot;                                              // 天线的摆
    NpcRig.HoseMesh _hose1, _hose2;                            // 两根软管（两端固定、中间晃，和 npc1 一样）
    Spring2 _hose1S, _hose2S;                                  // 软管中段的弹簧
    float _prevSpasm, _spasmNow;                               // 上一帧 / 这一帧抽了多少（痉挛时把软管甩起来）
    Vector3 _pupilRest;                                        // 瞳孔平时的位置（眼球本地）
    Vector2 _pupilNow, _pupilWant;                             // 瞳孔现在 / 想看的地方（相对平时位置，单位）
    float _wanderT;                                            // 离下一次乱看还有多久
    float _spasmT = -1f, _spasmLen, _spasmNext, _spasmSeed;    // 痉挛进行到哪 / 多长 / 下一次什么时候 / 这次的方向
    float _atkT = -1f;                                         // 攻击进行到哪（-1 = 没在砍）
    bool _atkRight;                                            // 这次用哪只手砍（轮流）
    float _cycleT, _testFireTimer;
    float _deadGlow = 1f;                                      // 死了瞳孔慢慢暗下去

    // 朝向：测试强制 > 盯着目标 > 往哪边走
    bool _walkLeft;
    protected override bool FacingLeft => testFaceLeft || (LookTarget(out Vector3 t) ? t.x < transform.position.x : _walkLeft);

    // 挂上组件时：换成它自己的走路手感（慢、拖着走）
    void Reset() { UseMonsterDefaults(); }

    // 它自己的默认值（编辑器里挂上组件时自动用；代码里加的组件要自己调一下，比如动画测试自动生成的那只）
    public void UseMonsterDefaults()
    {
        artFolder = "EnemyArt/infected";
        walkFullSpeed = 0.8f;
        cadenceSlow = 0.75f; cadenceFast = 1f; cadenceRun = 1.3f;
        footSwing = new Vector2(0.05f, 0.08f); footLift = new Vector2(0.012f, 0.03f); footRot = new Vector2(5f, 10f);
        handSwing = new Vector2(0.02f, 0.04f); handLift = new Vector2(0.005f, 0.01f); handRot = new Vector2(6f, 12f);
        bob = new Vector2(0.012f, 0.03f); waddle = new Vector2(4.5f, 3f); runLean = -4f;
        testLoop = false;
    }

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        Build();
    }

    void Build()
    {
        if (string.IsNullOrEmpty(artFolder) || artFolder == "NpcArt") artFolder = "EnemyArt/infected";   // 代码里直接加的组件（没走 Reset）
        _phaseSeed = Random.value * 6.28f;
        _rig = new GameObject("RigInfected").transform;
        _rig.SetParent(transform, false);
        _rig.localPosition = new Vector3(0f, FEET_Y, 0f);

        // 图层顺序和 PSD 一样（从下到上）：右脚 → 左脚 → 面罩里的机器 → 眼球 → 瞳孔 → 右手（在身体后面）→ 躯干 → 天线 → 软管2 → 软管1 → 左手
        _footR = Part("FootR", _rig, "FootR", P_FOOT_R, P_ROOT, 0).transform;
        _footL = Part("FootL", _rig, "FootL", P_FOOT_L, P_ROOT, 1).transform;
        _body = new GameObject("Body").transform;
        _body.SetParent(_rig, false);
        _body.localPosition = CanvasToLocal(P_BODY - P_ROOT);
        Part("Inner", _body, "Inner", P_INNER, P_BODY, 2);
        _eye = Part("Eyeball", _body, "Eyeball", P_EYE, P_BODY, 3).transform;        // 眼球（在躯干后面，从面罩窗口里看到）
        _pupilSr = Part("Pupil", _eye, "Pupil", P_PUPIL, P_EYE, 4);                  // 瞳孔（挂在眼球上）
        _pupil = _pupilSr.transform;
        _pupilRest = _pupil.localPosition;
        RenderMats.MakeUnlit(_pupilSr);                                               // 瞳孔自己发亮（暗处也看得见）
        _handR = Part("ArmR", _body, "ArmR", P_ARM_R, P_BODY, 5).transform;          // 右边的变异手（在躯干后面）
        Part("Torso", _body, "Torso", P_BODY, P_BODY, 6);
        _antenna = Part("Antenna", _body, "Antenna", P_ANTENNA, P_BODY, 7).transform;
        _hose2 = new NpcRig.HoseMesh("Hose2", _body, Load("Hose2"), HOSE2_SPAN, 8);   // 软管：两端固定、中间晃
        _hose1 = new NpcRig.HoseMesh("Hose1", _body, Load("Hose1"), HOSE1_SPAN, 9);
        _handL = Part("ArmL", _body, "ArmL", P_ARM_L, P_BODY, 10).transform;         // 左边的变异手（在最前面）
        if (RenderMats.IsURP && pupilLightIntensity > 0f)
            _pupilLight = WorldLighting.CreatePoint("PupilLight", _pupil, pupilLightColor, pupilLightIntensity, pupilLightRadius, 360f);

        _footLBase = _footL.localPosition; _footRBase = _footR.localPosition;
        _handLBase = _handL.localPosition; _handRBase = _handR.localPosition;
        _bodyBase = _body.localPosition;
        //                  频率   阻尼   加减速甩 跑动拖后 跟身体摇摆 颠动    最大角度 吊着的程度
        _antRot = new Pend(2.4f, 0.12f, 0.02f, 0.008f, 2.0f, 0.0015f, 25f, 0.5f);   // 天线：断了一半似的晃得多
        _hose1S = new Spring2(Sq(2f * Mathf.PI * 3.6f), 0.18f);                      // 软管（和 npc1 一样的弹簧）
        _hose2S = new Spring2(Sq(2f * Mathf.PI * 4.4f), 0.18f);

        SortingGroup group = gameObject.GetComponent<SortingGroup>();
        if (group == null) group = gameObject.AddComponent<SortingGroup>();
        _spasmNext = RandIn(spasmEvery);
        _built = true;
    }

    // 盯着谁：游戏里 LockOn 的目标 > 测试目标
    bool LookTarget(out Vector3 p)
    {
        Transform t = _aimUntil > _time && _lockTarget != null ? _lockTarget : testAimTarget;
        p = t != null ? t.position : Vector3.zero;
        return t != null && t.gameObject.activeInHierarchy;
    }

    protected override float AdjustSpeed(float speed, Vector3 velocity, float dt)
    {
        if (velocity.x < -0.05f) _walkLeft = true; else if (velocity.x > 0.05f) _walkLeft = false;
        if (testWalkCycle)                                                      // 走 4 秒 → 停 3 秒
        {
            _cycleT = Mathf.Repeat(_cycleT + dt, 7f);
            return _cycleT < 4f ? Mathf.Max(0.5f, testSpeed > 0f ? testSpeed : 0.9f) : 0f;
        }
        return testSpeed >= 0f ? testSpeed : speed;
    }

    // 眼睛：有目标就盯着，没有就乱看；痉挛时猛地抽一下
    protected override void UpdateAim(float dt)
    {
        Vector2 want;
        float range = pupilRange / PPU_FULL;
        if (LookTarget(out Vector3 tp) && !_dead)
        {
            Vector3 eyeWorld = _eye != null ? _eye.position : transform.position;
            Vector2 d = AimLocal(tp) - AimLocal(eyeWorld);                      // 目标在眼睛的哪边（x 正 = 前方）
            want = d.sqrMagnitude > 0.0001f ? d.normalized * range : Vector2.zero;
            want -= (Vector2)CanvasDelta(P_PUPIL - P_EYE) * 0.5f;               // 平时的位置偏前：往后看时能多转一点
        }
        else
        {
            _wanderT -= dt;
            if (_wanderT <= 0f && !_dead) { _wanderT = RandIn(pupilWander); _pupilWant = Random.insideUnitCircle * range * 0.7f; }   // 一跳：换个地方看
            want = _pupilWant;
        }
        _pupilNow = Vector2.MoveTowards(_pupilNow, want, pupilSpeed * dt);
    }

    protected override void StepSecondary(float dt, Motion mo)
    {
        _antRot.Step(dt, mo.acc, mo.refV, mo.tilt, mo.tilta, mo.boba, 0f);
        float jerk = (_prevSpasm - _spasmNow) * 60f;                        // 痉挛抽一下：软管被甩起来
        _hose1S.Step(dt, new Vector2(-mo.acc + jerk, -mo.boba - Mathf.Abs(jerk) * 0.5f) * 0.0016f * hoseAmount, 4f, 6f);
        _hose2S.Step(dt, new Vector2(-mo.acc + jerk, -mo.boba - Mathf.Abs(jerk) * 0.5f) * 0.0016f * hoseAmount, 4f, 6f);
    }

    protected override void UpdateTest(float dt)
    {
        bool fire = testFire;
        testFire = false;
        if (testLoop)
        {
            _testFireTimer -= dt;
            if (_testFireTimer <= 0f) { _testFireTimer = AttackLength() + 0.4f; fire = true; }
        }
        if (fire) PlayAttack();
    }

    float AttackLength() => atkWindTime + atkSlashTime + atkHoldTime + atkReturnTime;

    // 外部调用（攻击的那一下）：轮流用两只手砍
    public override void PlayAttack()
    {
        if (_dead || !_built || _atkT >= 0f) return;                            // 上一下没砍完不重来
        _atkT = 0f;
        _atkRight = !_atkRight;
    }

    // 每帧：攻击进度 + 身体跟着压
    protected override void AnimateHeld(float dt)
    {
        _actionLean = 0f; _actionShift = 0f;
        if (_atkT < 0f) return;
        _atkT += dt;
        if (_atkT >= AttackLength()) { _atkT = -1f; return; }
        AttackPose(out _, out _, out float k);
        _actionLean = atkLean * k;                                              // 砍：身体往前压
        _actionShift = atkShift * k;
    }

    // 攻击姿势：ang = 那只手转多少度，reach = 往前伸多远，k = 砍的力度（0~1，身体跟着压）
    void AttackPose(out float ang, out Vector2 reach, out float k)
    {
        float t = _atkT, a = atkWindTime, b = a + atkSlashTime, c = b + atkHoldTime;
        if (t < a) { float u = Ease(t / a); ang = atkWindAngle * u; reach = Vector2.zero; k = -0.3f * u; }   // 往后抬：身体也往后收一点
        else if (t < b) { float u = (t - a) / atkSlashTime; u = u * u; ang = Mathf.Lerp(atkWindAngle, atkSlashAngle, u); reach = atkReach * u; k = Mathf.Lerp(-0.3f, 1f, u); }   // 越砍越快
        else if (t < c) { ang = atkSlashAngle; reach = atkReach; k = 1f; }
        else { float u = Ease((t - c) / atkReturnTime); ang = Mathf.Lerp(atkSlashAngle, 0f, u); reach = atkReach * (1f - u); k = 1f - u; }
    }

    // 摆部件（通用骨架摆完身体 / 脚 / 手之后）：上扬下浮、痉挛、攻击、眼睛、天线
    protected override void ApplyAttachments()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        // 痉挛：隔一阵子随机抽一下
        if (!_dead)
        {
            _spasmNext -= dt;
            if (_spasmNext <= 0f && _spasmT < 0f) { _spasmT = 0f; _spasmLen = RandIn(spasmTime); _spasmSeed = Random.Range(0f, 100f); _spasmNext = RandIn(spasmEvery); }
        }
        float sp = 0f;                                                          // 这一帧抽多少（-1~1）
        if (_spasmT >= 0f)
        {
            _spasmT += dt;
            float u = _spasmT / Mathf.Max(0.05f, _spasmLen);
            if (u >= 1f || _dead) _spasmT = -1f;
            else sp = (1f - u) * (Mathf.PerlinNoise(_spasmSeed, _spasmT * spasmFreq) * 2f - 1f) * 1.6f;   // 一下子抽得猛，慢慢停下来
        }
        // 躯体上扬下浮（站着的时候明显，走路时小一点）
        float idle = _dead ? 0f : 1f - _move * 0.6f;
        float ph = 2f * Mathf.PI * _time / Mathf.Max(0.5f, idlePeriod) + _phaseSeed;
        float rise = Mathf.Sin(ph);
        _body.localPosition += new Vector3(0f, idleFloat * rise * idle, 0f);
        _body.localRotation = _body.localRotation * Quaternion.Euler(0f, 0f, idleTilt * rise * idle + spasmBody * sp);   // 往上浮时往后仰（上扬）

        // 两只手：攻击 + 痉挛（走路摆动通用骨架已经摆好了）
        float armL = spasmArm * sp, armR = -spasmArm * sp * 0.8f;
        Vector2 reachL = Vector2.zero, reachR = Vector2.zero;
        if (_atkT >= 0f)
        {
            AttackPose(out float ang, out Vector2 reach, out _);
            if (_atkRight) { armR += ang; reachR = reach; } else { armL += ang; reachL = reach; }
        }
        _handL.localPosition += (Vector3)reachL;
        _handL.localRotation = _handL.localRotation * Quaternion.Euler(0f, 0f, armL);
        _handR.localPosition += (Vector3)reachR;
        _handR.localRotation = _handR.localRotation * Quaternion.Euler(0f, 0f, armR);

        // 眼睛：瞳孔 + 痉挛时乱抖；死了慢慢暗下去
        Vector2 jitter = sp != 0f ? new Vector2(Mathf.PerlinNoise(_spasmSeed + 7f, _spasmT * spasmFreq) - 0.5f, Mathf.PerlinNoise(_spasmSeed + 13f, _spasmT * spasmFreq) - 0.5f) * 2f * spasmEye / PPU_FULL : Vector2.zero;
        _pupil.localPosition = _pupilRest + (Vector3)(_pupilNow + jitter);
        if (_dead)
        {
            _deadGlow = Mathf.MoveTowards(_deadGlow, 0.35f, dt * 0.6f);
            _pupilNow = Vector2.MoveTowards(_pupilNow, new Vector2(0f, -pupilRange * 0.4f / PPU_FULL), pupilSpeed * 0.3f * dt);   // 死了：眼睛慢慢往下翻
        }
        Color c = _pupilSr.color; c.r = c.g = c.b = _deadGlow; _pupilSr.color = c;
        if (_pupilLight != null) _pupilLight.intensity = pupilLightIntensity * Mathf.Clamp01((_deadGlow - 0.35f) / 0.65f) * (1f + 0.15f * Mathf.Sin(_time * 3.1f));   // 微微闪

        // 天线
        _antenna.localRotation = Quaternion.Euler(0f, 0f, _antRot.a + spasmBody * sp * 1.5f);

        // 软管：中段跟着弹簧晃
        Color tint = new Color(1f, 1f, 1f, _alpha);                        // 跟着整个人一起淡出（死了消失时）
        if (_hose1 != null) _hose1.Apply(_hose1S.p.x / REF, -_hose1S.p.y / REF, tint);
        if (_hose2 != null) _hose2.Apply(_hose2S.p.x / REF, -_hose2S.p.y / REF, tint);
        _prevSpasm = _spasmNow; _spasmNow = sp;
    }

    static Vector2 CanvasDelta(Vector2 d) => new Vector2(d.x / PPU_FULL, -d.y / PPU_FULL);   // 原图像素差 → 单位

    // ---------------- 游戏里用（Enemy 调）----------------
    float _alpha = 1f;
    public bool Built => _built;
    // 砍下去那一刻离开始攻击多久（伤害在这时候才算）
    public float HitDelay => atkWindTime + atkSlashTime * 0.8f;
    // 迷雾里看不见：整个人藏起来
    public void SetShown(bool show) { if (_rig != null && _rig.gameObject.activeSelf != show) _rig.gameObject.SetActive(show); }
    // 整个人的透明度（死了慢慢淡出）
    public void SetAlpha(float a)
    {
        _alpha = Mathf.Clamp01(a);
        if (_rig == null) return;
        foreach (SpriteRenderer sr in _rig.GetComponentsInChildren<SpriteRenderer>(true)) { Color c = sr.color; c.a = _alpha; sr.color = c; }
        if (_pupilLight != null && _alpha < 1f) _pupilLight.intensity *= _alpha;
    }

    // ---------------- 通用骨架要的钩子 ----------------
    protected override void HeadWhip(float push, float k) { _pupilNow += new Vector2(-push, 0.3f) * 0.04f * k; }   // 挨打：眼睛被震得一歪
    protected override void Ricochet(Vector3 at, bool left) { }                 // 不开枪
    protected override void RestoreAfterRevive() { _deadGlow = 1f; _atkT = -1f; }
    protected override void StopForDeath() { _atkT = -1f; _spasmT = -1f; _lockTarget = null; _aimUntil = 0f; }
    protected override void DropWeapon(float dt) { _weaponDropped = true; }    // 手是长在身上的，掉不了
    public override bool TryGetMuzzle(out Vector3 pos) { pos = transform.position; return false; }
    public override bool FireBullet(Vector3 target) => false;
    public override bool FireBullet(Vector3 target, bool canRicochet) => false;
    public override bool CanTestEquip(WeaponDef d) => false;                    // 不拿枪
    public override bool IsMonster => true;                                     // 怪物（动画测试里放在怪物栏）
}
