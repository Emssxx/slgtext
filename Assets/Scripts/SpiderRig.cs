using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;     // SortingGroup：部件当成一个整体参与前后遮挡

// ============================================================================
// 有骨骼动画的怪物共用的几样（Enemy 用它，不用管是哪一种怪物）
// ============================================================================
public interface IMonsterRig
{
    bool Built { get; }                 // 拼好了没有
    float HitDelay { get; }             // 出手后多久才真的打中（秒）
    void SetShown(bool show);           // 迷雾里看不见：整只藏起来
    void SetAlpha(float a);             // 整只的透明度（死了慢慢淡出）
}

// ============================================================================
// 怪物 2：眼球蜘蛛（外观 + 动画；图：Resources/EnemyArt/spider，坐标 = 蜘蛛 PSD 1538×999 画布，y 往下数）
//   寄生在被感染的研究员体内，研究员被打死后（有概率）钻出来 2~3 只。原图是朝左的。
//   三种大小（Size = 和 NPC 比的高度倍数）：幼体 0.5、青年体 1.2、变异体 3（越大越慢、越耐打——数值在 EnemyDefs）。
//   身体：前壳（胸）+ 后壳（腹，绕连接处晃）；头 = 前壳里伸出来的大眼球，瞳孔里长着一张满是牙的嘴。
//   腿：近处三条（大腿 + 小腿，两节，用两节骨骼 IK 让脚踩在地上），远处三条（只看得到小腿，一节）。
//   动画：
//     走路：三角步态（近 1、近 3、远 2 一组，另外三条一组，轮流抬起往前迈），身体一颠一颠
//     待机：后壳一胀一缩地呼吸、身体轻轻起伏；时不时有一条腿抽一下；瞳孔里的嘴一张一合地嚼
//     眼睛：没目标时瞳孔乱转；锁定敌人（LockOn / 测试目标）后瞳孔盯着它
//     攻击：往后一缩、头抬起来、前腿举起、嘴张大 → 猛地往前扑咬，前腿往前扎下去，嘴咬合 → 慢慢退回
//     受击：身体被打得一歪（通用受击弹簧）+ 瞳孔被震一下、嘴一闭
//     死亡：腿一下子蜷起来缩到肚子底下（死蜘蛛那样）、身体趴到地上、抽搐几下；瞳孔暗下去往下翻，嘴合上
//     钻出来：从小一点一下子弹到原来大小，跳一下；长大：慢慢胀到新的大小
//   测试：Test Fire = 扑咬一下；Test Loop = 一直咬；Test Speed / Test Walk Cycle = 走；Test Aim Target = 瞳孔盯着它；受击 / 死亡开关在通用骨架里
// ============================================================================
public class SpiderRig : CharacterRigBase, IMonsterRig
{
    [Header("大小（和 NPC 比的高度倍数：幼体 0.5 / 青年体 1.2 / 变异体 3）")]
    public float size = 1f;

    [Header("走路（距离 = 原图像素）")]
    public float stride = 55f;                                 // 一步迈多远（脚往前 / 往后各这么多）
    public float stepLift = 45f;                               // 迈步时脚抬多高
    public float maxCadence = 5f;                              // 最多每秒迈几个来回
    public float walkBob = 8f;                                 // 身体一颠一颠多高
    public float walkTilt = 2f;                                // 身体跟着步子左右歪多少度

    [Header("待机")]
    public float breathePeriod = 2.4f;                         // 呼吸一次几秒
    public float breatheAmount = 0.025f;                       // 后壳胀多少（比例）
    public float bodyFloat = 4f;                               // 身体跟着呼吸起伏多少（原图像素）
    public Vector2 twitchEvery = new Vector2(0.8f, 2.5f);      // 隔多久有一条腿抽一下（秒，随机）
    public float twitchTime = 0.22f;                           // 抽一下多久
    public float twitchLift = 30f;                             // 抽的时候脚抬多高（原图像素）
    public Vector2 chompEvery = new Vector2(1.2f, 3.5f);       // 嘴隔多久嚼一下（秒，随机）
    public float chompTime = 0.35f;                            // 嚼一下多久

    [Header("眼睛（瞳孔）")]
    public float pupilRange = 55f;                             // 瞳孔最多离开中间多远（原图像素）
    public float pupilSpeed = 350f;                            // 瞳孔转过去有多快（原图像素 / 秒）
    public Vector2 pupilWander = new Vector2(0.4f, 1.6f);      // 没目标时多久换一个地方看（秒，随机）

    [Header("攻击：扑咬（时间 = 秒，距离 = 原图像素，角度 = 度）")]
    public float atkWindTime = 0.28f;                          // ① 往后缩、举前腿、张嘴
    public float atkLungeTime = 0.09f;                         // ② 往前扑（越短越猛）
    public float atkHoldTime = 0.14f;                          // ③ 咬住停一下
    public float atkReturnTime = 0.35f;                        // ④ 慢慢退回
    public float atkWindBack = 25f;                            // ① 往后缩多远
    public float atkRear = -10f;                               // ① 头抬起多少度（负 = 头往上）
    public float atkLunge = 70f;                               // ② 往前扑多远
    public float atkDip = 6f;                                  // ② 头往下压多少度
    public Vector2 atkLegRaise = new Vector2(-60f, 180f);      // ① 近处前腿举起：脚往前 / 往上挪多少
    public float atkLegStab = 80f;                             // ② 近处前腿往前扎多远（脚贴着地）
    public float atkFarRaise = -30f;                           // ① 远处两条前腿往前上方抬多少度
    public float atkFarStab = 12f;                             // ② 远处两条前腿往下扎多少度

    [Header("死亡")]
    public float curlTime = 0.45f;                             // 腿蜷起来用几秒
    public float deathSinkPx = 150f;                           // 身体往下趴多少（原图像素，肚子贴地）
    public float deathFarCurl = 55f;                           // 远处的腿往肚子底下收多少度
    public float deathTwitch = 1f;                             // 死后抽搐多厉害（0 = 不抽）

    [Header("钻出来 / 长大")]
    public float hatchTime = 0.45f;                            // 钻出来弹到原来大小用几秒
    public float hatchHop = 60f;                               // 钻出来跳多高（原图像素）

    [Header("测试")]
    public bool testWalkCycle;                                 // 走几秒 → 停几秒循环（速度用 Test Speed，没设就用 0.9）

    // —— 原图关键点（y 往下数）——
    const float SP_PPU = 719f / 0.90625f;                      // 原图每 1 单位多少像素：腿尖最高处到地面 719 像素 = NPC 的身高（Size = 1 时）
    static readonly Vector2 P_ROOT = new Vector2(800, 885);    // 地面点（脚底）
    static readonly Vector2 P_BODY = new Vector2(800, 600);    // 身体（前壳）的转动点
    static readonly Vector2 P_LINK = new Vector2(880, 600);    // 后壳绕着晃的连接处
    static readonly Vector2 P_CAVITY = new Vector2(475, 507), P_TENDRILS = new Vector2(365, 592), P_VEINS = new Vector2(463, 624);   // 不动的部件：图的中心（轴心设在这里）
    static readonly Vector2 P_EYE = new Vector2(328, 661), P_PUPIL = new Vector2(287, 639);   // 眼球中心 / 瞳孔中心
    static readonly Vector2[] P_HIP = { new Vector2(680, 665), new Vector2(845, 655), new Vector2(1110, 775) };   // 近处三条腿：大腿根
    static readonly Vector2[] P_KNEE = { new Vector2(885, 415), new Vector2(1215, 395), new Vector2(1272, 588) }; // 膝盖
    static readonly Vector2[] P_TIP = { new Vector2(999, 883), new Vector2(1469, 881), new Vector2(1351, 887) };  // 脚尖
    static readonly Vector2[] P_FAR = { new Vector2(505, 465), new Vector2(470, 320), new Vector2(785, 560) };    // 远处三条腿：转动点（藏在身体后面）
    static readonly Vector2[] P_FARTIP = { new Vector2(363, 842), new Vector2(188, 868), new Vector2(732, 839) }; // 远处的脚尖

    // —— 部件 ——
    Transform _abd, _eye, _pupil, _mouth;
    SpriteRenderer _pupilSr, _mouthSr;
    readonly Transform[] _femur = new Transform[3], _tibia = new Transform[3], _far = new Transform[3];
    // —— 拼的时候算好的数（单位）——
    Vector3 _abdBase, _eyeBase, _pupilBase;
    readonly Vector2[] _hip = new Vector2[3], _footRest = new Vector2[3], _farPivot = new Vector2[3], _farRest = new Vector2[3];
    readonly Vector3[] _farBase = new Vector3[3];
    readonly float[] _len1 = new float[3], _len2 = new float[3], _femurRest = new float[3], _tibiaRest = new float[3], _farRestAng = new float[3];
    // —— 动画状态 ——
    float _gait;                                               // 步态相位
    float _atkT = -1f;                                         // 扑咬进行到哪（-1 = 没在咬）
    int _twitchLeg = -1; float _twitchT, _twitchNext;          // 正在抽的那条腿（0~2 近处，3~5 远处）
    float _chompT = -1f, _chompNext;                           // 嚼一下进行到哪
    Vector2 _pupilNow, _pupilWant; float _wanderT;             // 瞳孔现在 / 想看的地方（单位，眼球本地）
    float _flinch;                                             // 挨打：嘴一闭（1 → 0）
    float _deadGlow = 1f;                                      // 死了瞳孔慢慢暗下去
    float _alpha = 1f;                                         // 整只的透明度
    float _hatchT = -1f;                                       // 钻出来进行到哪（-1 = 没有）
    float _growFrom = -1f, _growT, _growLen;                   // 长大：从多大、进行到哪、用几秒
    float _cycleT, _testFireTimer;
    bool _walkLeft = true;

    // 朝向：测试强制 > 盯着目标 > 往哪边走（原图朝左）
    protected override bool FacingLeft => testFaceLeft || (LookTarget(out Vector3 t) ? t.x < transform.position.x : _walkLeft);

    // 挂上组件时：用它自己的默认值
    void Reset() { UseMonsterDefaults(); }

    // 它自己的默认值（编辑器里挂上组件时自动用；代码里加的组件要自己调一下）
    public void UseMonsterDefaults()
    {
        artFolder = "EnemyArt/spider";
        testLoop = false;
        hitLean = 7f; hitShove = 0.03f; hitFreq = 3.6f;                       // 挨打：歪得少一点、回得快一点（虫子）
        impactHeight = 0f;                                                    // 中弹的位置：身体中间
    }

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        Build();
    }

    // 原图坐标 → 本地单位（相对 basePt；y 往上为正）
    static Vector3 C(Vector2 p, Vector2 basePt) => new Vector3((p.x - basePt.x) / SP_PPU, -(p.y - basePt.y) / SP_PPU, 0f);
    static float PX(float px) => px / SP_PPU;                  // 原图像素 → 单位
    static float Ang(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
    static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

    // 建一个部件（图的轴心已经设在 pivot 上）
    SpriteRenderer SPart(string name, Transform parent, string file, Vector2 pivot, Vector2 parentPivot, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = C(pivot, parentPivot);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Load(file);
        sr.sortingOrder = order;
        return sr;
    }

    void Build()
    {
        if (string.IsNullOrEmpty(artFolder) || artFolder == "NpcArt") artFolder = "EnemyArt/spider";   // 代码里直接加的组件（没走 Reset）
        _phaseSeed = Random.value * 6.28f;
        _rig = new GameObject("RigSpider").transform;
        _rig.SetParent(transform, false);
        _rig.localPosition = new Vector3(0f, FEET_Y, 0f);
        _body = new GameObject("Body").transform;
        _body.SetParent(_rig, false);
        _body.localPosition = C(P_BODY, P_ROOT);
        _bodyBase = _body.localPosition;

        // 图层顺序和 PSD 一样（从下到上）：远处的腿 → 眼球后面的腔 → 眼球 → 嘴 → 瞳孔 → 触须 → 血管 → 连接处 → 后壳 → 前壳 → 近处的腿（3 → 2 → 1）
        string[] farName = { "LegFar1", "LegFar2", "LegFar3" };
        int[] farOrder = { 2, 1, 0 };
        for (int i = 0; i < 3; i++) { _far[i] = SPart(farName[i], _body, farName[i], P_FAR[i], P_BODY, farOrder[i]).transform; _farBase[i] = _far[i].localPosition; }
        SPart("Cavity", _body, "Cavity", P_CAVITY, P_BODY, 3);
        _eye = SPart("Eyeball", _body, "Eyeball", P_EYE, P_BODY, 4).transform;
        _eyeBase = _eye.localPosition;
        _pupil = new GameObject("PupilGroup").transform;                       // 瞳孔 + 瞳孔里的嘴一起动
        _pupil.SetParent(_eye, false);
        _pupil.localPosition = C(P_PUPIL, P_EYE);
        _pupilBase = _pupil.localPosition;
        _mouthSr = SPart("Mouth", _pupil, "Mouth", P_PUPIL, P_PUPIL, 5);
        _mouth = _mouthSr.transform;
        _pupilSr = SPart("Pupil", _pupil, "Pupil", P_PUPIL, P_PUPIL, 6);
        SPart("Tendrils", _body, "Tendrils", P_TENDRILS, P_BODY, 7);
        SPart("Veins", _body, "Veins", P_VEINS, P_BODY, 8);
        _abd = new GameObject("AbdomenRoot").transform;                        // 后壳绕连接处晃
        _abd.SetParent(_body, false);
        _abd.localPosition = C(P_LINK, P_BODY);
        _abdBase = _abd.localPosition;
        SPart("Link", _abd, "Link", P_LINK, P_LINK, 9);
        SPart("Abdomen", _abd, "Abdomen", P_LINK, P_LINK, 10);
        SPart("Thorax", _body, "Thorax", P_BODY, P_BODY, 11);
        int[] femOrder = { 16, 14, 12 };
        for (int i = 0; i < 3; i++)                                             // 近处的腿：大腿挂在身体上，小腿挂在大腿上
        {
            _femur[i] = SPart("Femur" + (i + 1), _body, "Femur" + (i + 1), P_HIP[i], P_BODY, femOrder[i]).transform;
            _tibia[i] = SPart("Tibia" + (i + 1), _femur[i], "Tibia" + (i + 1), P_KNEE[i], P_HIP[i], femOrder[i] + 1).transform;
            Vector2 hip = C(P_HIP[i], P_ROOT), knee = C(P_KNEE[i], P_ROOT), tip = C(P_TIP[i], P_ROOT);
            _hip[i] = C(P_HIP[i], P_BODY);                                       // 大腿根（身体本地）
            _len1[i] = (knee - hip).magnitude; _len2[i] = (tip - knee).magnitude; // 大腿 / 小腿多长
            _femurRest[i] = Ang(knee - hip); _tibiaRest[i] = Ang(tip - knee);   // 原图里的角度
            _footRest[i] = tip;                                                 // 平时脚踩在哪（根节点本地）
            Vector2 fp = C(P_FAR[i], P_ROOT), ft = C(P_FARTIP[i], P_ROOT);
            _farPivot[i] = C(P_FAR[i], P_BODY);
            _farRest[i] = ft;
            _farRestAng[i] = Ang(ft - fp);
        }

        SortingGroup group = gameObject.GetComponent<SortingGroup>();
        if (group == null) group = gameObject.AddComponent<SortingGroup>();
        _twitchNext = RandIn(twitchEvery);
        _chompNext = RandIn(chompEvery);
        _built = true;
    }

    // 现在实际多大（钻出来 / 长大的过程中会变）
    float CurSize()
    {
        float s = size;
        if (_growFrom > 0f) s = Mathf.LerpUnclamped(_growFrom, size, GrowCurve(_growT / Mathf.Max(0.05f, _growLen)));
        if (_hatchT >= 0f) s *= Mathf.LerpUnclamped(0.2f, 1f, BackOut(_hatchT / Mathf.Max(0.05f, hatchTime)));
        return s;
    }
    static float BackOut(float u) { u = Mathf.Clamp01(u) - 1f; return 1f + u * u * (2.7f * u + 1.7f); }   // 冲过头一点再回来
    static float GrowCurve(float u) { u = Mathf.Clamp01(u); return Ease(u) + Mathf.Sin(u * Mathf.PI * 3f) * (1f - u) * 0.06f; }   // 一胀一胀地长大

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
            return _cycleT < 4f ? Mathf.Max(0.3f, testSpeed > 0f ? testSpeed : 0.9f) : 0f;
        }
        return testSpeed >= 0f ? testSpeed : speed;
    }

    // ============================================================================
    // 每帧（不用通用人形骨架的走路：自己摆六条腿；受击弹簧 / 死亡状态还是用通用的）
    // ============================================================================
    protected override void Update()
    {
        if (!_built) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);                            // 卡顿时不要一步跳太远
        if (dt <= 0f) return;
        UpdateReactTest();                                                      // 受击 / 死亡的测试开关
        Vector3 v3 = _agent != null && _agent.enabled ? _agent.velocity : Vector3.zero;
        float speed = AdjustSpeed(new Vector2(v3.x, v3.y).magnitude, v3, dt);
        if (_dead) speed = 0f;
        if (!_dead) UpdateAim(dt);                                              // 瞳孔
        if (_hatchT >= 0f) { _hatchT += dt; if (_hatchT >= hatchTime) _hatchT = -1f; }
        if (_growFrom > 0f) { _growT += dt; if (_growT >= _growLen) _growFrom = -1f; }
        float s = CurSize();
        bool left = _dead ? _deadLeft : FacingLeft;
        _rig.localScale = new Vector3(left ? s : -s, s, 1f);                    // 原图朝左：朝右就水平翻过来
        for (int i = 0; i < 4; i++) StepReact(dt / 4f);                         // 受击弹簧（分小步算更稳）+ 死了多久
        _time += dt;
        if (!_dead) UpdateTest(dt);
        Pose(dt, speed / Mathf.Max(0.05f, s), s, left);
    }

    // 摆姿势。speedLocal = 按自己大小换算的速度（大的走同样快，腿迈得慢）
    void Pose(float dt, float speedLocal, float s, bool left)
    {
        // —— 走路相位 ——
        float move = _dead ? 0f : Mathf.Clamp01(speedLocal / 0.3f);              // 0 = 站着，1 = 正常走
        if (speedLocal > 0.02f && !_dead) _gait += 2f * Mathf.PI * Mathf.Min(maxCadence, speedLocal / (4f * PX(stride))) * dt;
        if (_gait > 1000f) _gait -= 2f * Mathf.PI * 150f;

        // —— 待机：呼吸、抽腿、嚼 ——
        float alive = _dead ? 0f : 1f;
        float breathe = Mathf.Sin(2f * Mathf.PI * _time / Mathf.Max(0.3f, breathePeriod) + _phaseSeed) * alive;
        bool busy = _atkT >= 0f || _dead;
        if (!busy && move < 0.3f)
        {
            _twitchNext -= dt;
            if (_twitchNext <= 0f && _twitchLeg < 0) { _twitchLeg = Random.Range(0, 6); _twitchT = 0f; _twitchNext = RandIn(twitchEvery); }
        }
        float twitch = 0f;
        if (_twitchLeg >= 0)
        {
            _twitchT += dt;
            if (_twitchT >= twitchTime || busy) _twitchLeg = -1;
            else twitch = Mathf.Sin(Mathf.PI * _twitchT / twitchTime);
        }
        float mouth = 1f;                                                       // 嘴张多大（1 = 平时）
        if (!busy) { _chompNext -= dt; if (_chompNext <= 0f && _chompT < 0f) { _chompT = 0f; _chompNext = RandIn(chompEvery); } }
        if (_chompT >= 0f)
        {
            _chompT += dt;
            float u = _chompT / Mathf.Max(0.05f, chompTime);
            if (u >= 1f || busy) _chompT = -1f;
            else mouth = 1f + 0.25f * Mathf.Sin(u * Mathf.PI) - 0.15f * Mathf.Sin(u * Mathf.PI * 2f) * (u > 0.5f ? 1f : 0f);   // 张开 → 咬一下 → 回来
        }

        // —— 攻击 ——
        float shift = 0f, rear = 0f, frontK = 0f, stabK = 0f;
        if (_atkT >= 0f)
        {
            _atkT += dt;
            if (_atkT >= AttackLength()) _atkT = -1f;
            else AttackPose(out shift, out rear, out mouth, out frontK, out stabK);
        }

        // —— 死亡：腿蜷起来、趴下、抽搐 ——
        float dk = _dead ? Ease(_deadT / Mathf.Max(0.05f, curlTime)) : 0f;
        float spasm = _dead ? deathTwitch * Mathf.Exp(-_deadT * 1.8f) * Mathf.Sin(_deadT * 23f) : 0f;
        if (_dead) mouth = Mathf.Lerp(1f, 0.6f, dk);

        // —— 挨打：嘴一闭 ——
        _flinch = Mathf.MoveTowards(_flinch, 0f, dt * 4f);
        mouth *= 1f - 0.3f * _flinch;

        // —— 根节点：钻出来跳一下 + 被推开 ——
        float hop = 0f;
        if (_hatchT >= 0f) { float u = _hatchT / Mathf.Max(0.05f, hatchTime); hop = PX(hatchHop) * 4f * u * (1f - u) * s; }
        _rig.localPosition = new Vector3(_shove * (left ? -1f : 1f), FEET_Y + hop, 0f);   // 被推开（世界左右，正 = 往前）

        // —— 身体 ——
        float bob = PX(walkBob) * move * (0.5f + 0.5f * Mathf.Cos(2f * _gait));             // 一步一颠（三角步态：一个来回颠两下）
        float bodyY = bob + PX(bodyFloat) * breathe * (1f - move) - PX(deathSinkPx) * dk;
        float bodyRot = walkTilt * Mathf.Sin(_gait) * move + 1.2f * breathe * (1f - move) + rear - _hitAng + 3f * dk + 2f * spasm;   // 原图朝左：受击后仰要反过来
        Vector2 bodyPos = (Vector2)_bodyBase + new Vector2(-PX(shift), bodyY);           // 原图朝左：往前 = x 负
        _body.localPosition = new Vector3(bodyPos.x, bodyPos.y, 0f);
        _body.localRotation = Quaternion.Euler(0f, 0f, bodyRot);

        // —— 后壳：呼吸胀缩 + 跟着步子 / 挨打晃 + 死了往下耷拉 ——
        _abd.localPosition = _abdBase;
        _abd.localRotation = Quaternion.Euler(0f, 0f, 1.5f * breathe + 2f * Mathf.Sin(2f * _gait + 0.8f) * move + 0.6f * _hitAng - 6f * dk + 3f * spasm);
        float sw = 1f + breatheAmount * breathe;
        _abd.localScale = new Vector3(sw, sw, 1f);

        // —— 近处三条腿：脚的目标 → 两节 IK ——
        float rad = bodyRot * Mathf.Deg2Rad, cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
        for (int i = 0; i < 3; i++)
        {
            Vector2 foot = FootTarget(_footRest[i], i == 1 ? Mathf.PI : 0f, move);    // 近 1、近 3 一组，近 2 另一组
            if (_twitchLeg == i) foot.y += PX(twitchLift) * twitch;
            if (i == 0)                                                               // 近处前腿：攻击时举起来 / 往前扎
                foot += new Vector2(PX(atkLegRaise.x), PX(atkLegRaise.y)) * frontK + new Vector2((-PX(atkLegStab) - PX(shift)) * stabK, 0f);
            Vector2 hipR = bodyPos + new Vector2(_hip[i].x * cs - _hip[i].y * sn, _hip[i].x * sn + _hip[i].y * cs);   // 大腿根（根节点本地）
            if (dk > 0f)                                                              // 死了：脚收到大腿根后下方（肚子底下），膝盖高高拱起
            {
                Vector2 curl = _hip[i] + new Vector2(PX(70f), -PX(80f + 20f * i)) + new Vector2(0f, PX(25f) * spasm * (i == 1 ? -1f : 1f));
                Vector2 curlR = bodyPos + new Vector2(curl.x * cs - curl.y * sn, curl.x * sn + curl.y * cs);
                foot = Vector2.Lerp(foot, curlR, dk);
            }
            SolveLeg(i, hipR, foot, bodyRot);
        }

        // —— 远处三条腿（一节）：转到脚的目标方向 ——
        for (int i = 0; i < 3; i++)
        {
            Vector2 foot = FootTarget(_farRest[i], i == 1 ? 0f : Mathf.PI, move);     // 远 2 和近 1、近 3 一组
            float lift = foot.y - _farRest[i].y;
            if (_twitchLeg == i + 3) lift += PX(twitchLift) * twitch;
            Vector2 piv = bodyPos + new Vector2(_farPivot[i].x * cs - _farPivot[i].y * sn, _farPivot[i].x * sn + _farPivot[i].y * cs);
            float a = Ang(foot - piv) - _farRestAng[i] - bodyRot;
            if (i < 2) a += atkFarRaise * frontK + atkFarStab * stabK;              // 远处两条前腿：攻击时抬起来 / 扎下去
            a += (deathFarCurl + 8f * spasm * (i == 1 ? -1f : 1f)) * dk;            // 死了：往肚子底下收
            _far[i].localRotation = Quaternion.Euler(0f, 0f, a);
            _far[i].localPosition = _farBase[i] + new Vector3(0f, lift * 0.5f, 0f); // 抬腿：整条往上提一点
        }

        // —— 眼睛 ——
        _eye.localPosition = _eyeBase + new Vector3(-PX(10f) * stabK + PX(6f) * frontK, PX(3f) * Mathf.Sin(2f * _gait) * move, 0f);   // 扑咬：眼球往前顶
        _eye.localRotation = Quaternion.Euler(0f, 0f, 2f * spasm);
        if (_dead)
        {
            _deadGlow = Mathf.MoveTowards(_deadGlow, 0.4f, dt * 0.5f);
            _pupilNow = Vector2.MoveTowards(_pupilNow, new Vector2(PX(10f), -PX(pupilRange)), PX(pupilSpeed) * 0.15f * dt);   // 死了：瞳孔慢慢往下翻
        }
        _pupil.localPosition = _pupilBase + new Vector3(_pupilNow.x, _pupilNow.y, 0f);
        float ring = 1f + (mouth - 1f) * 0.4f;                                      // 瞳孔跟着嘴一起胀缩
        _pupil.localScale = new Vector3(ring, ring, 1f);
        float m = mouth / ring;
        _mouth.localScale = new Vector3(m, m, 1f);
        Color c = new Color(_deadGlow, _deadGlow, _deadGlow, _alpha);
        _pupilSr.color = c; _mouthSr.color = c;
    }

    // 走路时脚踩哪：phase = 这一组的相位差；脚往前迈时抬起来，往后蹬时贴着地（原图朝左：往前 = x 负）
    Vector2 FootTarget(Vector2 rest, float phase, float move)
    {
        float ph = _gait + phase;
        float c = Mathf.Cos(ph), lift = c > 0f ? c * c : 0f;
        return rest + new Vector2(-PX(stride) * Mathf.Sin(ph) * move, PX(stepLift) * lift * move);
    }

    // 两节腿 IK：大腿根 hip、脚 foot（根节点本地）→ 大腿 / 小腿各转多少（膝盖往上拱）
    void SolveLeg(int i, Vector2 hip, Vector2 foot, float bodyRot)
    {
        float l1 = _len1[i], l2 = _len2[i];
        Vector2 d = foot - hip;
        float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.0001f, l1 + l2 - 0.0001f);   // 够不着就伸直，太近就折到最紧
        float baseAng = Ang(d);
        float cosA = (l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist);
        float femurAbs = baseAng + Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f)) * Mathf.Rad2Deg;   // 膝盖在上面（腿往右下方伸，逆时针转 = 往上）
        Vector2 knee = hip + Dir(femurAbs) * l1;
        Vector2 end = hip + Dir(baseAng) * dist;
        float tibiaAbs = Ang(end - knee);
        float femurLocal = femurAbs - _femurRest[i];                           // 大腿比原图多转多少（根节点里）
        _femur[i].localRotation = Quaternion.Euler(0f, 0f, femurLocal - bodyRot);
        _tibia[i].localRotation = Quaternion.Euler(0f, 0f, (tibiaAbs - _tibiaRest[i]) - femurLocal);
    }

    // 眼睛：有目标就盯着，没有就乱看
    protected override void UpdateAim(float dt)
    {
        Vector2 want;
        float range = PX(pupilRange);
        if (LookTarget(out Vector3 tp))
        {
            Vector3 e = _eye != null ? _eye.position : transform.position;
            Vector2 d = new Vector2(tp.x - e.x, tp.y - e.y);
            d.x *= FacingLeft ? 1f : -1f;                                       // 原图朝左：朝右时整只翻过来了，x 要反
            want = d.sqrMagnitude > 0.0001f ? d.normalized * range : Vector2.zero;
        }
        else
        {
            _wanderT -= dt;
            if (_wanderT <= 0f) { _wanderT = RandIn(pupilWander); _pupilWant = Random.insideUnitCircle * range * 0.8f; }   // 一跳：换个地方看
            want = _pupilWant;
        }
        _pupilNow = Vector2.MoveTowards(_pupilNow, want, PX(pupilSpeed) * dt);
    }

    protected override void UpdateTest(float dt)
    {
        bool fire = testFire;
        testFire = false;
        if (testLoop)
        {
            _testFireTimer -= dt;
            if (_testFireTimer <= 0f) { _testFireTimer = AttackLength() + 0.5f; fire = true; }
        }
        if (fire) PlayAttack();
    }

    float AttackLength() => atkWindTime + atkLungeTime + atkHoldTime + atkReturnTime;

    // 外部调用（攻击的那一下）：扑咬
    public override void PlayAttack()
    {
        if (_dead || !_built || _atkT >= 0f) return;                            // 上一下没咬完不重来
        _atkT = 0f;
        _chompT = -1f; _twitchLeg = -1;
    }

    // 扑咬的姿势：shift = 身体往前多远（原图像素，负 = 往后缩），rear = 头抬 / 压多少度，mouth = 嘴张多大，
    //   frontK = 前腿举起的程度，stabK = 前腿往前扎的程度
    void AttackPose(out float shift, out float rear, out float mouth, out float frontK, out float stabK)
    {
        float t = _atkT, a = atkWindTime, b = a + atkLungeTime, c = b + atkHoldTime;
        if (t < a) { float u = Ease(t / a); shift = -atkWindBack * u; rear = atkRear * u; mouth = 1f + 0.35f * u; frontK = u; stabK = 0f; }
        else if (t < b) { float u = (t - a) / atkLungeTime; u *= u; shift = Mathf.Lerp(-atkWindBack, atkLunge, u); rear = Mathf.Lerp(atkRear, atkDip, u); mouth = Mathf.Lerp(1.35f, 0.7f, u); frontK = 1f - u; stabK = u; }   // 越扑越快
        else if (t < c) { shift = atkLunge; rear = atkDip; mouth = 0.7f; frontK = 0f; stabK = 1f; }
        else { float u = Ease((t - c) / atkReturnTime); shift = atkLunge * (1f - u); rear = atkDip * (1f - u); mouth = Mathf.Lerp(0.7f, 1f, u); frontK = 0f; stabK = 1f - u; }
    }

    // ---------------- 游戏里用（Enemy 调）----------------
    public bool Built => _built;
    // 咬下去那一刻离开始攻击多久（伤害在这时候才算）
    public float HitDelay => atkWindTime + atkLungeTime * 0.8f;
    // 迷雾里看不见：整只藏起来
    public void SetShown(bool show) { if (_rig != null && _rig.gameObject.activeSelf != show) _rig.gameObject.SetActive(show); }
    // 整只的透明度（死了慢慢淡出）
    public void SetAlpha(float a)
    {
        _alpha = Mathf.Clamp01(a);
        if (_rig == null) return;
        foreach (SpriteRenderer sr in _rig.GetComponentsInChildren<SpriteRenderer>(true)) { Color c = sr.color; c.a = _alpha; sr.color = c; }
    }
    // 刚从研究员身体里钻出来：从小一下子弹到原来大小、跳一下
    public void PlayHatch() { _hatchT = 0f; }
    // 长大到 newSize（用 seconds 秒一胀一胀地长）
    public void GrowTo(float newSize, float seconds)
    {
        _growFrom = CurSize();
        _growT = 0f;
        _growLen = Mathf.Max(0.1f, seconds);
        size = newSize;
    }

    // ---------------- 通用骨架要的钩子 ----------------
    protected override void HeadWhip(float push, float k) { _pupilNow += new Vector2(-push, 0.4f) * PX(25f) * k; _flinch = 1f; }   // 挨打：瞳孔被震一下、嘴一闭
    protected override void Ricochet(Vector3 at, bool left) { }                 // 不开枪
    protected override void RestoreAfterRevive() { _deadGlow = 1f; _atkT = -1f; _pupilNow = Vector2.zero; }
    protected override void StopForDeath() { _atkT = -1f; _twitchLeg = -1; _chompT = -1f; _lockTarget = null; _aimUntil = 0f; }
    protected override void DropWeapon(float dt) { _weaponDropped = true; }    // 没有武器
    protected override void StepSecondary(float dt, Motion mo) { }              // 走路自己算（Pose）
    protected override void AnimateHeld(float dt) { }
    protected override void ApplyAttachments() { }
    public override bool TryGetMuzzle(out Vector3 pos) { pos = transform.position; return false; }
    public override bool FireBullet(Vector3 target) => false;
    public override bool FireBullet(Vector3 target, bool canRicochet) => false;
    public override bool CanTestEquip(WeaponDef d) => false;                    // 不拿枪
    public override bool IsMonster => true;                                     // 怪物（动画测试里放在怪物栏）
}
