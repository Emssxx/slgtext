using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;          // NavMeshAgent：读 NPC 现在走多快
using UnityEngine.Rendering;   // SortingGroup：让拼出来的二十几个部件作为一个整体参与前后遮挡

// ============================================================================
// NpcRig —— 新版 NPC 外观（防护服角色）：运行时把部件拼成角色，并用代码做动画。
//
// 【它做什么】
//   · 游戏开始后，在每个 NPC 身上自动拼出新角色：脚、身体、脸、表情、头发、手、天线、软管、挂件、水瓶。
//     素材都在 Assets/Resources/NpcArt/ 里（从你的 PSD 导出，轴心已经设在每个部件的转动点）。
//   · 外观按 NPC 自己的数据决定：性别 → 男/女的表情套和发型；50 岁以上 → 老人表情；
//     脸型、肤色、发色、挂件用 NPC 的 id 算出来，所以同一个 NPC 每次进游戏长得都一样。水瓶每人都有。
//   · 动画全部是代码算的（不用 Animator、不用 K 帧）：
//       走路 / 跑步：钟摆节奏（慢-快-慢），手脚到最高处时身体也到最高处，落地压扁一下；
//       惯性：天线弹簧、头在面罩里晃、软管中段晃、锤子斧头两节摆、拉链、包类弹动、水瓶摆动。
//   · 只有逃跑那种速度才算"跑"（平时赶路 3.0 还是走路动作）。
//
// 【它不改什么】
//   · 不改预制体、不改场景。由 NpcView 在 Awake 里自动挂上本组件。
//   · 旧的 NPC 图不删除，只是把它变成透明：悬停框（HoverInfo）、对话窗（NpcChatWindow）还靠它的大小定位；
//     NpcMover 仍然只翻转旧图（flipX），本脚本读这个翻转来决定新角色朝左还是朝右。
//
// 【第二步加的】表情跟着状态自动切换；每次换表情，面罩先放下盖住脸、在看不见的时候换图、再收起
//   （例外：换成"说话"、或者从"说话"换回别的表情时，直接换，不开合面罩）；
//   逃跑（跑步）时面罩一直放下；面罩上的反光跟着移动距离扫过，停下就停住，朝左时自动镜像。
//   表情优先级：崩溃 > 失常（理智低）> 惊吓（受伤 / 逃跑）> 说话 > 休息 > 工作 > 极度疲惫 > 疲惫 > 平常。
// ============================================================================
public class NpcRig : MonoBehaviour
{
    // ---------------- 可在 Inspector 里调的参数（运行时选中 NPC 就能看到） ----------------
    [Header("速度判定（单位/秒；闲逛约 1.65，赶路 3.0，逃跑 3.9）")]
    public float walkFullSpeed = 1.2f;    // 速度到这里，走路动作的幅度就是满的（再慢就步子变小）
    public float runStartSpeed = 3.3f;    // 超过它开始从走路变成跑步（平时赶路 3.0 不算跑）
    public float runFullSpeed = 3.8f;     // 到这里完全是跑步动作

    [Header("步伐节奏（每秒几个完整步伐，左右脚各一步）")]
    public float cadenceSlow = 1.15f;     // 慢走（闲逛）
    public float cadenceFast = 1.9f;      // 快走（赶路）
    public float cadenceRun = 2.3f;       // 跑步

    [Header("手脚摆动（单位；第一个数是走，第二个数是跑）")]
    public Vector2 footSwing = new Vector2(0.080f, 0.138f);   // 脚前后摆多远
    public Vector2 footLift = new Vector2(0.031f, 0.092f);    // 脚摆到最前面时抬多高
    public Vector2 footRot = new Vector2(12f, 24f);           // 脚尖上翘角度（度）
    public float flight = 0.023f;                             // 跑步时两脚一起离地的高度
    public Vector2 handSwing = new Vector2(0.069f, 0.134f);   // 手前后摆多远
    public Vector2 handLift = new Vector2(0.019f, 0.054f);    // 手摆到两头时抬多高
    public Vector2 handRot = new Vector2(16f, 34f);           // 手摆动时转多少度
    [Range(0.3f, 1f)] public float easePow = 0.8f;            // <1：在最高处多停一下（摆球的"慢-快-慢"）

    [Header("身体（第一个数是走，第二个数是跑）")]
    public Vector2 bob = new Vector2(0.019f, 0.046f);         // 身体上下颠的高度
    public Vector2 waddle = new Vector2(2.0f, 1.0f);          // 身体左右摇摆的角度
    public float runLean = -7f;                               // 跑步前倾角度（负数 = 往前倾）
    public Vector2 squash = new Vector2(0.025f, 0.07f);       // 落地时压扁多少

    [Header("惯性开关（整体倍率，0 = 关掉）")]
    [Range(0f, 2f)] public float antennaAmount = 1f;          // 天线
    [Range(0f, 2f)] public float gearAmount = 1f;             // 挂件 / 水瓶 / 拉链
    [Range(0f, 2f)] public float hoseAmount = 1f;             // 软管

    [Header("面罩 / 表情")]
    public float visorFreq = 1.6f;            // 面罩放下 / 收起的快慢（越大越快）
    [Range(0.3f, 1f)] public float visorDamp = 0.78f;  // 面罩放到底时回弹多少（越小回弹越明显）
    public float exprDebounce = 0.35f;        // 新状态要持续多久才换表情（防止状态一闪一闪导致面罩一直开合）
    public float exprHold = 0.15f;            // 面罩完全放下后停多久再换表情、再收起
    public float runVisorHold = 0.6f;         // 不跑了以后，面罩再多关多久才收起（防止忽开忽关）
    public float glareSpeed = 2.8f;           // 反光扫过面罩的速度（和移动速度挂钩，停下就停）
    public string currentExpression;          // 只读：现在显示的表情（运行时选中 NPC 可以看到，方便测试）

    // ---------------- 固定换算（和导出素材时的设置一致，不要改） ----------------
    const float PPU_FULL = 1755f / 0.90625f;  // 原图（2048 画布）上每 1 单位有多少像素：身体 1755 像素 = 0.906 单位（和旧 NPC 图一样高）
    const float REF = PPU_FULL * 0.27f;       // 调参用的"参考像素"：惯性参数是在预览视频里按这个刻度调好的
    const float FEET_Y = -0.453125f;          // 旧 NPC 图的轴心在中间，脚底在它下面 0.453 单位（NavMeshAgent 的 baseOffset）

    // 原图（2048 画布）上的关键点：部件绕着这些点转。y 向下数。
    static readonly Vector2 P_ROOT = new Vector2(1162, 1987);     // 两脚中间的地面点（整个角色的原点）
    static readonly Vector2 P_BODY = new Vector2(1200, 1642);     // 身体底部中点（身体颠动、摇摆、压扁都绕它）
    static readonly Vector2 P_FACE = new Vector2(1200, 780);      // 脸的中心（脸、表情、头发一起在面罩里晃）
    static readonly Vector2 P_FOOT_L = new Vector2(906, 1985), P_FOOT_R = new Vector2(1418, 1980);
    static readonly Vector2 P_HAND_L = new Vector2(752, 1175), P_HAND_R = new Vector2(1665, 1178);
    static readonly Vector2 P_ANTENNA = new Vector2(577, 790);    // 天线根部
    static readonly Vector2 P_RING_H = new Vector2(1431, 1337), P_HAMMER = new Vector2(1431, 1398);  // 锤子挂环顶部 / 锤子顶部
    static readonly Vector2 P_RING_A = new Vector2(1239, 1343), P_AXE = new Vector2(1239, 1404);     // 斧头挂环 / 斧头
    static readonly Vector2 P_HOLSTER = new Vector2(1510, 1140);  // 枪套顶部（手枪插在里面，跟着枪套动）
    static readonly Vector2 P_MEDKIT = new Vector2(650, 1338), P_ZIPPER = new Vector2(704, 1439);    // 医疗包顶部 / 拉链顶部
    static readonly Vector2 P_TOOLKIT = new Vector2(876, 1392);
    static readonly Vector2 P_BOTTLE = new Vector2(485, 1082);    // 水瓶瓶口（挂在背包侧面）
    static readonly Vector2 HOSE1_SPAN = new Vector2(591, 1418);  // 软管1 两端的 x（两端固定、中间晃）
    static readonly Vector2 HOSE2_SPAN = new Vector2(627, 931);   // 软管2 两端的 x
    static readonly Vector2 P_VISOR = new Vector2(1211, 345);     // 面罩顶边中点（面罩从这里往下展开）
    const float GLARE_END = -700f, GLARE_ENTER = 230f, GLARE_GAP = 110f;  // 反光：扫到左边多远算出去 / 从右边哪里重新进来 / 出去后再空走多远（原图像素）

    // ---------------- 外观可选项（文件名见 Resources/NpcArt） ----------------
    static readonly string[] TONES = { "Tan", "Pink", "Cream", "Dark" };          // 肤色：橙 / 粉 / 米 / 深
    static readonly string[] HAIR_COLORS = { "Black", "White", "Brown", "Blond" }; // 发色：黑 / 白 / 棕 / 黄
    static readonly string[] EXPR_MALE = { "M1", "M2", "M3" };                    // 男生表情套
    static readonly string[] EXPR_FEMALE = { "F1", "F2" };                        // 女生表情套（女3 文件和女1 重复，先不用）
    const int ELDER_AGE = 50;                                                      // 这个年龄以上用老人表情

    // ---------------- 运行时引用 ----------------
    NpcView _view;                 // 拿 npcId
    NavMeshAgent _agent;           // 读速度
    SpriteRenderer _oldSprite;     // 旧 NPC 图：变透明，但保留（读它的 flipX 决定朝向）
    bool _built;                   // 角色拼好了没有

    Transform _rig, _body, _face;                        // 根（脚底）/ 身体 / 脸
    Transform _footL, _footR, _handL, _handR, _antenna;  // 会动的部件
    Vector3 _footLBase, _footRBase, _handLBase, _handRBase, _faceBase, _bodyBase;  // 它们的原始位置
    SpriteRenderer _torsoSprite, _exprSprite;            // 身体（软管跟着它的透明度淡出）/ 表情
    string _exprSet;                                     // 用哪套表情（M1 / F2 / Old …）
    readonly List<HoseMesh> _hoses = new List<HoseMesh>();
    readonly Dictionary<string, Transform> _gear = new Dictionary<string, Transform>();    // 挂件的转动节点
    readonly Dictionary<string, Vector3> _gearBase = new Dictionary<string, Vector3>();    // 挂件原始位置
    NpcNeedRunner _needRunner;     // 读 NPC 正在做的需求行为（吃 / 休息 / 采集 / 逃跑）
    string _npcId;                 // 这个 NPC 的 id（拼好角色时记下）
    Transform _visorPivot, _glare; // 面罩的转动点（顶边）/ 反光
    SpriteRenderer _visorSprite, _glareSprite;  // 面罩图 / 反光图
    SpriteMask _glareMask;         // 反光的遮罩：只在面罩形状里面显示
    readonly Dictionary<string, Sprite> _exprCache = new Dictionary<string, Sprite>();  // 已经读过的表情图
    string _shownExpr = "", _pendingExpr = "";  // 现在显示的表情 / 状态想要的表情
    float _pendingSince;           // 想要的表情从什么时候开始的
    bool _blink;                   // 正在"关面罩 → 换表情 → 开面罩"
    float _closedTimer;            // 面罩完全放下后已经停了多久
    float _visorK, _visorV;        // 面罩放下的程度（0 = 收起露脸，1 = 放下盖住脸）/ 变化速度
    float _runHold;                // 跑步结束后面罩还要关多久
    float _exprTimer;              // 距离下一次判断表情还有多久
    float _glareX;                 // 反光在面罩上的横向位置（原图像素，0 = 画的原位置）

    // ---------------- 物理状态 ----------------
    float _theta;                    // 步伐相位（0~2π 一个完整周期）
    float _phaseSeed;                // 每个 NPC 呼吸节奏错开一点
    float _ant, _antV;               // 天线角度 / 角速度
    Spring2 _faceLag, _hose1, _hose2, _bagBounce;
    Pend _ringH, _hammer, _ringA, _axe, _medkit, _zipper, _toolkit, _holster, _bottle;
    float _prevRefV, _prevBob, _prevBobV, _prevTilt, _prevTiltV, _prevRingHV, _prevRingAV;
    float _move, _run, _sv, _bobUp, _tilt, _time;

    // ============================================================================
    void Awake()
    {
        _view = GetComponent<NpcView>();                 // 同一个物体上的 NpcView
        _agent = GetComponent<NavMeshAgent>();           // 同一个物体上的导航代理
        _oldSprite = GetComponent<SpriteRenderer>();     // 旧 NPC 图
        _needRunner = GetComponent<NpcNeedRunner>();     // 需求行为执行器（没有就是 null，不影响）
    }

    void Start()
    {
        StartCoroutine(BuildWhenReady());                // 等 NPC 的 id 和数据就绪再拼角色
    }

    // 等 NpcSpawner 设好 id、数据能查到，再拼角色（最多等 3 秒拿年龄档案，拿不到就按 30 岁算）。
    IEnumerator BuildWhenReady()
    {
        NpcData data = null;
        while (data == null)                                             // 一直等到能查到这个 NPC 的数据
        {
            if (_view != null && !string.IsNullOrEmpty(_view.npcId) && WorldManager.Instance != null)
                data = WorldManager.Instance.GetNpcById(_view.npcId);
            if (data == null) yield return new WaitForSeconds(0.2f);
        }
        float wait = 0f;
        NpcActorProfile profile = NpcActors.Get(data.id);                // 年龄在演员档案里
        while (profile == null && wait < 3f)                             // 档案可能晚一点才生成
        {
            yield return new WaitForSeconds(0.25f);
            wait += 0.25f;
            profile = NpcActors.Get(data.id);
        }
        bool male = profile != null ? profile.male : (data.gender == null || data.gender.Contains("男"));
        int age = profile != null ? profile.age : 30;
        Build(data.id, male, age);
    }

    // ============================================================================
    // 拼角色
    // ============================================================================
    void Build(string id, bool male, int age)
    {
        System.Random rng = new System.Random(StableHash(id));   // 用 id 算随机数：同一个 NPC 每次都一样
        bool elder = age >= ELDER_AGE;
        _phaseSeed = (float)rng.NextDouble() * 6.28f;

        // —— 选外观 ——
        string face = "Face/Face_" + (rng.Next(4) + 1) + "_" + TONES[rng.Next(4)];   // 4 种脸型 × 4 种肤色
        _exprSet = elder ? "Old" : (male ? EXPR_MALE[rng.Next(EXPR_MALE.Length)] : EXPR_FEMALE[rng.Next(EXPR_FEMALE.Length)]);
        string hairColor = HAIR_COLORS[rng.Next(4)];
        if (elder && rng.NextDouble() < 0.6) hairColor = "White";                    // 老人多半白头发
        string hair;
        if (male)
        {
            int style = rng.Next(7);                                                  // 0 = 光头，1~6 = 发型1~6
            hair = style == 0 ? "Hair/M_Bald" : "Hair/M_" + style + "_" + hairColor;
        }
        else hair = "Hair/F_" + (rng.Next(5) + 1) + "_" + hairColor;                // 女生发型 1~5
        // 挂件：0~3 件随机（水瓶不在这里，每人都有）
        List<string> pool = new List<string> { "Hammer", "Axe", "Pistol", "Medkit", "Toolkit" };
        int[] counts = { 0, 1, 1, 2, 2, 3 };
        int n = counts[rng.Next(counts.Length)];
        HashSet<string> gear = new HashSet<string>();
        for (int i = 0; i < n; i++) { int k = rng.Next(pool.Count); gear.Add(pool[k]); pool.RemoveAt(k); }

        // —— 层级：Rig（脚底）→ Body（身体底部）→ 各部件。数字是前后顺序（大的在前面），按你 PSD 的图层顺序 ——
        _rig = new GameObject("Rig").transform;
        _rig.SetParent(transform, false);
        _rig.localPosition = new Vector3(0f, FEET_Y, 0f);                            // 原点放在脚底

        _footR = Part("FootR", _rig, "Body/FootR", P_FOOT_R, P_ROOT, 0).transform;
        _footL = Part("FootL", _rig, "Body/FootL", P_FOOT_L, P_ROOT, 1).transform;
        _body = new GameObject("Body").transform;
        _body.SetParent(_rig, false);
        _body.localPosition = CanvasToLocal(P_BODY - P_ROOT);
        Part("Inner", _body, "Body/Inner", P_BODY, P_BODY, 2);
        _face = new GameObject("Face").transform;                                     // 脸、表情、头发放一起，一起晃
        _face.SetParent(_body, false);
        _face.localPosition = CanvasToLocal(P_FACE - P_BODY);
        Part("FaceSkin", _face, face, P_FACE, P_FACE, 3);
        _exprSprite = Part("Expr", _face, "Expr/" + _exprSet + "/Normal", P_FACE, P_FACE, 4);
        Part("Hair", _face, hair, P_FACE, P_FACE, 5);
        _handR = Part("HandR", _body, "Body/HandR", P_HAND_R, P_BODY, 6).transform;  // 右手在躯干后面
        _torsoSprite = Part("Torso", _body, "Body/Torso", P_BODY, P_BODY, 7);
        _antenna = Part("Antenna", _body, "Body/Antenna", P_ANTENNA, P_BODY, 8).transform;
        _hoses.Add(new HoseMesh("Hose2", _body, Load("Body/Hose2"), HOSE2_SPAN, 9));
        _hoses.Add(new HoseMesh("Hose1", _body, Load("Body/Hose1"), HOSE1_SPAN, 10));
        if (gear.Contains("Hammer"))
        {
            Transform ring = Part("HammerRing", _body, "Gear/HammerRing", P_RING_H, P_BODY, 12).transform;
            AddGear("ringH", ring);
            AddGear("hammer", Part("Hammer", ring, "Gear/Hammer", P_HAMMER, P_RING_H, 11).transform);
        }
        if (gear.Contains("Axe"))
        {
            Transform ring = Part("AxeRing", _body, "Gear/AxeRing", P_RING_A, P_BODY, 14).transform;
            AddGear("ringA", ring);
            AddGear("axe", Part("Axe", ring, "Gear/Axe", P_AXE, P_RING_A, 13).transform);
        }
        if (gear.Contains("Pistol"))
        {
            Transform holster = Part("Holster", _body, "Gear/Holster", P_HOLSTER, P_BODY, 16).transform;
            AddGear("holster", holster);
            Part("Pistol", holster, "Gear/Pistol", P_HOLSTER, P_HOLSTER, 15);         // 手枪插在枪套里，跟着枪套动
        }
        if (gear.Contains("Medkit"))
        {
            Transform med = Part("Medkit", _body, "Gear/Medkit", P_MEDKIT, P_BODY, 17).transform;
            AddGear("medkit", med);
            AddGear("zipper", Part("Zipper", med, "Gear/Zipper", P_ZIPPER, P_MEDKIT, 20).transform);  // 拉链挂在医疗包上
        }
        if (gear.Contains("Toolkit")) AddGear("toolkit", Part("Toolkit", _body, "Gear/Toolkit", P_TOOLKIT, P_BODY, 18).transform);
        AddGear("bottle", Part("Bottle", _body, "Gear/Bottle", P_BOTTLE, P_BODY, 19).transform);     // 水瓶：每人都有
        _handL = Part("HandL", _body, "Body/HandL", P_HAND_L, P_BODY, 21).transform;  // 左手
        // 面罩：挂在身体上，转动点在顶边，往下"展开"就是把它在竖直方向从 0 拉到 1（按你 PSD，面罩和光在最上层）
        _visorPivot = new GameObject("Visor").transform;                              // 面罩的转动点
        _visorPivot.SetParent(_body, false);                                          // 跟着身体动
        _visorPivot.localPosition = CanvasToLocal(P_VISOR - P_BODY);                  // 放在面罩顶边
        _visorSprite = Part("VisorPlate", _visorPivot, "Body/Visor", P_VISOR, P_VISOR, 22);  // 面罩图
        _glareSprite = Part("Glare", _visorPivot, "Body/Glare", P_VISOR, P_VISOR, 23);      // 反光图
        _glareSprite.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;       // 反光只在遮罩里面显示
        _glare = _glareSprite.transform;                                              // 记下反光，之后左右移动它
        GameObject maskGo = new GameObject("GlareMask");                              // 反光遮罩（面罩形状，右边放宽一点保留光晕）
        maskGo.transform.SetParent(_visorPivot, false);                               // 跟着面罩一起展开
        _glareMask = maskGo.AddComponent<SpriteMask>();                               // Unity 自带的精灵遮罩
        _glareMask.sprite = Load("Body/GlareMask");                                   // 遮罩形状图
        // 一开始面罩收起（露脸），表情直接用当前状态的，不做开合
        _npcId = id;                                                                  // 记下 id，判断表情要用
        string first = PickExpression();                                              // 现在该用什么表情
        SetExpression(first);                                                         // 直接换上
        _pendingExpr = first;                                                         // 当作"想要的"也是它
        ApplyVisor();                                                                 // 立刻把面罩摆成收起状态（不然第一帧会闪一下）

        // 记下会动的部件的原始位置（动画 = 原始位置 + 偏移）
        _footLBase = _footL.localPosition; _footRBase = _footR.localPosition;
        _handLBase = _handL.localPosition; _handRBase = _handR.localPosition;
        _faceBase = _face.localPosition; _bodyBase = _body.localPosition;

        // —— 物理初始化（参数和预览视频里一样）——
        _faceLag = new Spring2(90f, 0.35f);
        _hose1 = new Spring2(Sq(2f * Mathf.PI * 3.6f), 0.18f);
        _hose2 = new Spring2(Sq(2f * Mathf.PI * 4.4f), 0.18f);
        _bagBounce = new Spring2(Sq(2f * Mathf.PI * 5.0f), 0.30f);
        //            频率   阻尼  加减速甩 跑动拖后 跟身体摇摆 颠动   最大角度  吊着的程度
        _ringH   = new Pend(2.6f, 0.22f, 0.020f, 0.010f, 1.6f, 0.0006f, 24f, 1f);
        _hammer  = new Pend(1.7f, 0.20f, 0.030f, 0.014f, 2.2f, 0.0010f, 38f, 1f);   // 锤子 / 斧头：比挂环慢半拍
        _ringA   = new Pend(2.6f, 0.22f, 0.020f, 0.010f, 1.6f, 0.0006f, 24f, 1f);
        _axe     = new Pend(1.7f, 0.20f, 0.030f, 0.014f, 2.2f, 0.0010f, 38f, 1f);
        _zipper  = new Pend(3.4f, 0.10f, 0.060f, 0.020f, 3.5f, 0.0030f, 55f, 1f);   // 拉链：轻，晃得多
        _medkit  = new Pend(3.0f, 0.30f, 0.008f, 0.004f, 0.8f, 0.0004f, 8f, 0.3f);  // 包类：绑在身上，小幅晃
        _toolkit = new Pend(3.0f, 0.30f, 0.008f, 0.004f, 0.8f, 0.0004f, 8f, 0.3f);
        _holster = new Pend(3.0f, 0.30f, 0.008f, 0.004f, 0.8f, 0.0004f, 8f, 0.3f);
        _bottle  = new Pend(2.2f, 0.18f, 0.025f, 0.012f, 2.0f, 0.0010f, 35f, 0.8f); // 水瓶：挂在侧面，中等幅度

        // —— 整体前后遮挡：二十几个部件当成一个整体，按 NPC 的位置排序（和旧图一样）——
        SortingGroup group = gameObject.GetComponent<SortingGroup>();
        if (group == null) group = gameObject.AddComponent<SortingGroup>();
        if (_oldSprite != null)
        {
            group.sortingLayerID = _oldSprite.sortingLayerID;    // 和旧图同一个排序层
            group.sortingOrder = _oldSprite.sortingOrder;        // 同一个顺序（NPC = 1）
            _oldSprite.color = new Color(1f, 1f, 1f, 0f);        // 旧图变透明（不删除，见文件开头说明）
        }
        _built = true;
    }

    // 建一个部件：精灵放在 parent 下，位置 = 它的转动点相对 parent 转动点的距离。
    SpriteRenderer Part(string name, Transform parent, string path, Vector2 pivot, Vector2 parentPivot, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = CanvasToLocal(pivot - parentPivot);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Load(path);
        sr.sortingOrder = order;                                 // 在 SortingGroup 里的前后顺序
        return sr;
    }

    void AddGear(string key, Transform t) { _gear[key] = t; _gearBase[key] = t.localPosition; }

    static Sprite Load(string path)
    {
        Sprite s = Resources.Load<Sprite>("NpcArt/" + path);
        if (s == null) Debug.LogWarning("[NpcRig] 找不到素材：NpcArt/" + path);
        return s;
    }

    // 原图上的像素距离 → Unity 的本地坐标（原图 y 向下，Unity y 向上，所以 y 取反）
    static Vector3 CanvasToLocal(Vector2 d) { return new Vector3(d.x / PPU_FULL, -d.y / PPU_FULL, 0f); }

    // 稳定的字符串哈希（每次运行结果一样；C# 自带的 GetHashCode 不保证）
    static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }
    static float Sq(float x) { return x * x; }

    // ============================================================================
    // 每帧：算物理 → 摆部件
    // ============================================================================
    void Update()
    {
        if (!_built) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);             // 卡顿时不要一步跳太远
        if (dt <= 0f) return;                                     // 暂停时不动

        // 朝向：读旧图的翻转（NpcMover 管它）。朝左 = 整个角色水平翻转
        float facing = (_oldSprite != null && _oldSprite.flipX) ? -1f : 1f;
        _rig.localScale = new Vector3(facing, 1f, 1f);

        // 速度：只看水平面上的移动快慢
        Vector3 v3 = _agent != null && _agent.enabled ? _agent.velocity : Vector3.zero;
        float speed = new Vector2(v3.x, v3.y).magnitude;

        const int SUB = 4;                                        // 每帧分 4 小步算，弹簧更稳
        for (int i = 0; i < SUB; i++) Step(dt / SUB, speed);
        UpdateFace(dt);                                           // 表情 + 面罩开合
        Apply();
    }

    // 把游戏里的速度换算成"预览刻度"的速度（像素/秒），惯性参数就能直接沿用预览里调好的数。
    float RefSpeed(float speed, float run)
    {
        return 170f * Mathf.Clamp01(speed / 3.0f) + 260f * run;  // 赶路 3.0 ≈ 预览的走路，逃跑 ≈ 预览的跑步
    }

    void Step(float dt, float speed)
    {
        _time += dt;
        float move = Mathf.Clamp01(speed / walkFullSpeed);                        // 0 = 站着，1 = 正常走
        float run = Mathf.InverseLerp(runStartSpeed, runFullSpeed, speed);       // 0 = 走路，1 = 跑步
        float refV = RefSpeed(speed, run);
        float acc = (refV - _prevRefV) / dt; _prevRefV = refV;                    // 加速度（往前加速为正）

        // 步伐相位：走得越快，步子越密
        if (speed > 0.05f)
        {
            float walkCad = Mathf.Lerp(cadenceSlow, cadenceFast, Mathf.InverseLerp(1.0f, 3.0f, speed));
            _theta += 2f * Mathf.PI * Mathf.Lerp(walkCad, cadenceRun, run) * dt;
            if (_theta > 1000f) _theta -= 2f * Mathf.PI * 150f;                  // 防止数字越来越大
        }
        float c = Mathf.Cos(_theta);
        float sv = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), easePow);             // 摆动位置 -1~1（两头停留更久）

        // 身体上下颠：手脚在两头（最高处）时身体也最高；站着时轻轻呼吸
        float breath = 0.0057f * (1f - move) * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * 0.45f * _time + _phaseSeed));
        float bobUp = Mathf.Lerp(bob.x, bob.y, run) * sv * sv * move + breath;
        float bobRef = -bobUp * REF;                                              // 换成预览刻度（向下为正）
        float bobV = (bobRef - _prevBob) / dt; float boba = (bobV - _prevBobV) / dt;
        _prevBob = bobRef; _prevBobV = bobV;
        // 身体摇摆 + 跑步前倾
        float tilt = Mathf.Lerp(waddle.x, waddle.y, run) * sv * move + runLean * run;
        float tiltV = (tilt - _prevTilt) / dt; float tilta = (tiltV - _prevTiltV) / dt;
        _prevTilt = tilt; _prevTiltV = tiltV;

        // 天线：弹簧 + 加减速甩 + 跑动后仰 + 跟着身体摇摆
        float k = Sq(2f * Mathf.PI * 3.2f), cc = 2f * 0.13f * 2f * Mathf.PI * 3.2f;
        float tq = -k * _ant - cc * _antV + antennaAmount * (10.0f * acc + 6.9f * refV - 2.5f * tilta)
                   + boba * Mathf.Sin(_ant * Mathf.Deg2Rad) * 3f;
        _antV += tq * dt; _ant = Mathf.Clamp(_ant + _antV * dt, -32f, 32f);

        // 头在面罩里晃、软管晃、包类上下弹
        _faceLag.Step(dt, new Vector2(-acc, -boba) * 0.010f, 7f, 7f);
        _hose1.Step(dt, new Vector2(-acc, -boba) * 0.0016f * hoseAmount, 4f, 6f);
        _hose2.Step(dt, new Vector2(-acc, -boba) * 0.0016f * hoseAmount, 4f, 6f);
        _bagBounce.Step(dt, new Vector2(0f, -boba) * 0.0035f * gearAmount, 0f, 5f);

        // 挂件：挂环先动，锤子 / 斧头挂在下面、慢半拍（两节摆）
        float g = gearAmount;
        _ringH.Step(dt, acc * g, refV * g, tilt, tilta * g, boba * g, 0f);
        float rHa = (_ringH.v - _prevRingHV) / dt; _prevRingHV = _ringH.v;
        _hammer.Step(dt, acc * g, refV * g, tilt + _ringH.a, tilta * g, boba * g, -0.6f * rHa);
        _ringA.Step(dt, acc * g, refV * g, tilt, tilta * g, boba * g, 0f);
        float rAa = (_ringA.v - _prevRingAV) / dt; _prevRingAV = _ringA.v;
        _axe.Step(dt, acc * g, refV * g, tilt + _ringA.a, tilta * g, boba * g, -0.6f * rAa);
        _medkit.Step(dt, acc * g, refV * g, tilt, tilta * g, boba * g, 0f);
        _toolkit.Step(dt, acc * g, refV * g, tilt, tilta * g, boba * g, 0f);
        _holster.Step(dt, acc * g, refV * g, tilt, tilta * g, boba * g, 0f);
        _bottle.Step(dt, acc * g, refV * g, tilt, tilta * g, boba * g, 0f);
        // 拉链挂在医疗包上：医疗包一晃，它就被甩
        _zipper.Step(dt, acc * g, refV * g, tilt + _medkit.a, tilta * g, boba * g, -2.0f * _medkit.v * (2f * Mathf.PI * 3.4f));

        // 反光：走多少就扫多少（跑得快扫得快），停下就停在原地
        _glareX -= glareSpeed * refV * dt;                                        // 往身后方向扫
        if (_glareX < GLARE_END - GLARE_GAP)                                      // 扫出面罩左边、再空走一小段后
            _glareX += GLARE_ENTER - (GLARE_END - GLARE_GAP);                     // 从右边重新进来

        _move = move; _run = run; _sv = sv; _bobUp = bobUp; _tilt = tilt;
    }

    // ============================================================================
    // 表情 + 面罩
    // ============================================================================
    void UpdateFace(float dt)
    {
        // 1. 每 0.25 秒看一次状态，决定"该用什么表情"
        _exprTimer -= dt;
        if (_exprTimer <= 0f)
        {
            _exprTimer = 0.25f;                                                   // 下次 0.25 秒后再看
            string want = PickExpression();                                       // 按优先级选表情
            if (want != _pendingExpr) { _pendingExpr = want; _pendingSince = _time; }  // 想要的变了 → 从现在开始计时
        }
        // 2. 想要的表情稳定了一小会儿、又和现在显示的不一样 → 换表情
        if (_pendingExpr != _shownExpr && _time - _pendingSince >= exprDebounce)
        {
            if (_pendingExpr == "Talk" || _shownExpr == "Talk")                   // 换成"说话"或从"说话"换回去：直接换，不开合面罩
            {
                SetExpression(_pendingExpr);                                      // 立刻换上新表情
                _blink = false;                                                   // 如果正在为别的表情关面罩，取消（面罩会收回去）
                _closedTimer = 0f;                                                // 清掉"已经盖住多久"的计时
            }
            else if (!_blink) _blink = true;                                      // 其他表情：开始"关面罩 → 换表情 → 开面罩"
        }

        // 3. 跑步时面罩一直放下；不跑以后再多关一会儿
        if (_run > 0.5f) _runHold = runVisorHold;
        else _runHold = Mathf.Max(0f, _runHold - dt);
        bool wantClosed = _blink || _runHold > 0f;                                // 要不要放下面罩

        // 4. 面罩用弹簧动：放到底会轻轻回弹一下
        float target = wantClosed ? 1f : 0f;                                      // 1 = 放下，0 = 收起
        float w = 2f * Mathf.PI * visorFreq;                                      // 弹簧的"硬度"
        _visorV += (-w * w * (_visorK - target) - 2f * visorDamp * w * _visorV) * dt;  // 拉回目标 + 阻尼
        _visorK += _visorV * dt;                                                  // 更新放下的程度
        if (_visorK < 0f) { _visorK = 0f; if (_visorV < 0f) _visorV = 0f; }       // 收到顶就停住（不能缩成负的）

        // 5. 面罩完全盖住脸后停一下，在看不见脸的时候换表情，然后收起
        if (_blink && _visorK >= 0.97f)
        {
            _closedTimer += dt;                                                   // 已经盖住多久了
            if (_closedTimer >= exprHold)                                         // 停够了
            {
                SetExpression(_pendingExpr);                                      // 换上新表情
                _blink = false;                                                   // 这次开合结束 → 面罩开始收起
                _closedTimer = 0f;
            }
        }
    }

    // 按优先级选表情（名字就是 Resources/NpcArt/Expr/某套/ 里的文件名）
    string PickExpression()
    {
        NpcData n = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(_npcId) : null;  // 这个 NPC 的数据
        if (n == null) return "Normal";                                           // 查不到就用平常
        SanityLevel lv = MentalState.GetLevel(n);                                 // 精神等级
        if (lv == SanityLevel.Broken) return "Broken";                            // 崩溃
        if (lv == SanityLevel.Dazed || lv == SanityLevel.Hallucinating) return "Dazed";  // 理智低：失常
        bool doing = _needRunner != null && _needRunner.IsRunning;                // 正在执行某个需求行为
        if (NpcHealth.Has(n, HealthTag.Injured) || NpcHealth.Has(n, HealthTag.CriticalWound) || NpcHealth.Has(n, HealthTag.Dying)
            || (doing && _needRunner.Current == NpcDecisionRules.Behavior.Flee)) return "Scared";  // 受伤 / 逃跑：惊吓
        NpcChatWindow chat = NpcChatWindow.Instance;                              // 玩家的对话窗
        if ((chat != null && chat.IsOpen && chat.CurrentNpcId == _npcId)          // 玩家正在和他聊天
            || NpcSpeechBubble.IsShowing(transform)) return "Talk";               // 或者头上有气泡：说话
        if (doing && _needRunner.Current == NpcDecisionRules.Behavior.Rest) return "Rest";  // 休息
        if (NpcBusy.IsHeldBy(_npcId, NpcBusy.Task) || NpcBusy.IsHeldBy(_npcId, NpcBusy.Build)
            || (doing && _needRunner.Current == NpcDecisionRules.Behavior.Gather)) return "Work";  // 做任务 / 建造 / 采集：工作
        if (n.energy < 12f) return "Exhausted";                                   // 体力快没了：极度疲惫
        if (NpcHealth.Has(n, HealthTag.Fatigued)) return "Tired";                 // 疲惫
        return "Normal";                                                          // 平常
    }

    // 换上某个表情（这套里没有这张图就用"平常"）
    void SetExpression(string expr)
    {
        Sprite s = ExprSprite(expr);                                              // 读这张表情图
        if (s == null) s = ExprSprite("Normal");                                  // 没有就退回平常
        if (_exprSprite != null && s != null) _exprSprite.sprite = s;             // 换图
        _shownExpr = expr;                                                        // 记下现在显示的是哪个
        currentExpression = expr;                                                 // Inspector 里显示出来
    }

    // 读表情图（读过一次就记住，不重复读，找不到也只警告一次）
    Sprite ExprSprite(string expr)
    {
        if (_exprCache.TryGetValue(expr, out Sprite s)) return s;                 // 读过了直接用
        s = Load("Expr/" + _exprSet + "/" + expr);                                // 第一次：从 Resources 读
        _exprCache[expr] = s;                                                     // 记住（包括"没有"）
        return s;
    }

    // 把面罩摆到当前放下的程度（反光跟着面罩一起展开，再按 _glareX 左右移动）
    void ApplyVisor()
    {
        if (_visorPivot == null) return;                                          // 还没拼好
        _visorPivot.localScale = new Vector3(1f, Mathf.Max(_visorK, 0.0001f), 1f); // 竖直方向拉伸 = 从顶边往下展开（不能是 0）
        bool show = _visorK > 0.002f;                                             // 收起时整个藏起来
        if (_visorSprite != null) _visorSprite.enabled = show;
        if (_glareSprite != null) _glareSprite.enabled = show;
        if (_glareMask != null) _glareMask.enabled = show;
        if (_glare != null) _glare.localPosition = new Vector3(_glareX / PPU_FULL, 0f, 0f);  // 反光的横向位置
    }

    // 把算好的数摆到各个部件上
    void Apply()
    {
        float move = _move, run = _run, sv = _sv;
        // 身体：颠动 + 摇摆/前倾 + 落地压扁
        float contact = Mathf.Pow(1f - sv * sv, 3f) * move;
        float sq = Mathf.Lerp(squash.x, squash.y, run) * contact;
        _body.localPosition = _bodyBase + new Vector3(0f, _bobUp, 0f);
        _body.localRotation = Quaternion.Euler(0f, 0f, _tilt);
        _body.localScale = new Vector3(1f + sq * 0.7f, 1f - sq, 1f);

        // 脚：左右反相
        PlaceFoot(_footL, _footLBase, _theta);
        PlaceFoot(_footR, _footRBase, _theta + Mathf.PI);
        // 手：右手和左脚同方向，左手和右脚同方向；站着时轻轻浮动
        float idle = 1f - move;
        PlaceHand(_handR, _handRBase, sv, idle * 0.0038f * Mathf.Sin(2f * Mathf.PI * 0.45f * _time + 1.3f));
        PlaceHand(_handL, _handLBase, -sv, idle * 0.0038f * Mathf.Sin(2f * Mathf.PI * 0.45f * _time));

        _antenna.localRotation = Quaternion.Euler(0f, 0f, _ant);
        _face.localPosition = _faceBase + new Vector3(_faceLag.p.x / REF, -_faceLag.p.y / REF, 0f);

        // 软管：中段偏移（跟着身体的透明度一起淡出，比如 NPC 死亡时）
        Color tint = _torsoSprite != null ? _torsoSprite.color : Color.white;
        foreach (HoseMesh h in _hoses)
        {
            Spring2 s = h.name == "Hose1" ? _hose1 : _hose2;
            h.Apply(s.p.x / REF, -s.p.y / REF, tint);
        }

        // 挂件
        Vector3 bounce = new Vector3(0f, -_bagBounce.p.y / REF, 0f);
        SetGear("ringH", _ringH.a, Vector3.zero);
        SetGear("hammer", _hammer.a, Vector3.zero);
        SetGear("ringA", _ringA.a, Vector3.zero);
        SetGear("axe", _axe.a, Vector3.zero);
        SetGear("medkit", _medkit.a, bounce);
        SetGear("zipper", _zipper.a, Vector3.zero);
        SetGear("toolkit", _toolkit.a, bounce);
        SetGear("holster", _holster.a, bounce);
        SetGear("bottle", _bottle.a, bounce);

        ApplyVisor();                                                  // 面罩 + 反光
    }

    void PlaceFoot(Transform t, Vector3 basePos, float ph)
    {
        ph = Mathf.Repeat(ph, 2f * Mathf.PI);
        float c = Mathf.Cos(ph);
        c = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), easePow);
        float g;                                                       // 抬脚程度 0~1
        const float LAND = 0.4f * Mathf.PI;                            // 在最前面停一下后多久落地
        if (ph >= Mathf.PI) g = (1f - Mathf.Cos(ph - Mathf.PI)) / 2f;  // 从最后面慢慢抬，到最前面最高
        else if (ph < LAND) g = Sq(Mathf.Cos(ph / LAND * Mathf.PI / 2f)); // 在最高处停一下再落地
        else g = 0f;                                                   // 落地往后蹬
        float amp = Mathf.Lerp(footSwing.x, footSwing.y, _run) * _move;
        float up = (Mathf.Lerp(footLift.x, footLift.y, _run) * g + flight * _run * c * c) * _move;
        float rot = Mathf.Lerp(footRot.x, footRot.y, _run) * (g - 0.35f * Mathf.Max(0f, -c) * (1f - g)) * _move;
        t.localPosition = basePos + new Vector3(amp * c, up, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, rot);
    }

    void PlaceHand(Transform t, Vector3 basePos, float sw, float idleY)
    {
        float amp = Mathf.Lerp(handSwing.x, handSwing.y, _run) * _move;
        float up = Mathf.Lerp(handLift.x, handLift.y, _run) * sw * sw * _move;
        float rot = Mathf.Lerp(handRot.x, handRot.y, _run) * sw * _move;
        t.localPosition = basePos + new Vector3(amp * sw, up + idleY, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, rot);
    }

    void SetGear(string key, float angle, Vector3 offset)
    {
        if (!_gear.TryGetValue(key, out Transform t)) return;         // 这个 NPC 没带这件挂件
        t.localPosition = _gearBase[key] + offset;
        t.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    // ============================================================================
    // 小工具类
    // ============================================================================

    // 二维弹簧（偏移量，单位：预览像素；y 向下为正）
    class Spring2
    {
        public Vector2 p, v;          // 位置 / 速度
        readonly float k, damp;       // 硬度 / 阻尼
        public Spring2(float k, float damp) { this.k = k; this.damp = damp; }
        public void Step(float dt, Vector2 force, float maxX, float maxY)
        {
            v += (-k * p - 2f * damp * Mathf.Sqrt(k) * v + force * k) * dt;   // 弹回原位 + 阻尼 + 外力
            p += v * dt;
            p.x = Mathf.Clamp(p.x, -maxX, maxX); p.y = Mathf.Clamp(p.y, -maxY, maxY);
        }
    }

    // 摆球（角度，度；正 = 逆时针）。hang = 1：完全吊着，身体倾斜时仍然往下垂；0.3：绑在身上只跟一点。
    class Pend
    {
        public float a, v;            // 角度 / 角速度
        readonly float f, z, ga, gd, gw, gb, max, hang;
        public Pend(float freq, float damp, float accGain, float dragGain, float wobbleGain, float bobGain, float maxDeg, float hang)
        { f = freq; z = damp; ga = accGain; gd = dragGain; gw = wobbleGain; gb = bobGain; max = maxDeg; this.hang = hang; }
        public void Step(float dt, float acc, float refV, float tilt, float tilta, float boba, float extra)
        {
            float k = Sq(2f * Mathf.PI * f), c = 2f * z * 2f * Mathf.PI * f;
            float rest = -tilt * hang;                                   // 重力：身体前倾时，吊着的东西还是往下垂
            float tq = -k * (a - rest) - c * v
                       - (ga * acc + gd * refV) * k                      // 往前加速 / 跑动：下端往后甩、往后拖
                       - gw * tilta                                      // 身体摇摆带动
                       + gb * boba * k * 0.5f                            // 上下颠动带一点晃
                       + extra;                                          // 被上一节（挂环 / 医疗包）带动
            v += tq * dt;
            a = Mathf.Clamp(a + v * dt, -max, max);
        }
    }

    // 软管：用一条网格画（两端固定，中间可以弯），这样才能做出"中段晃"的效果。
    class HoseMesh
    {
        public readonly string name;
        readonly Mesh mesh;
        readonly Vector3[] basePos, pos;      // 原始顶点 / 当前顶点
        readonly float[] weight;              // 每个顶点能晃多少（两端 0，中间 1）
        readonly Color[] colors;
        static Material _mat;                 // 所有软管共用一个材质

        public HoseMesh(string name, Transform parent, Sprite sprite, Vector2 span, int order)
        {
            this.name = name;
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);                       // 放在身体下，原点 = 身体转动点（软管素材的轴心也在这）
            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (_mat == null) _mat = new Material(Shader.Find("Sprites/Default"));
            mr.sharedMaterial = _mat;
            mr.sortingOrder = order;
            if (sprite == null) return;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetTexture("_MainTex", sprite.texture);                  // 用软管的贴图
            mr.SetPropertyBlock(mpb);

            const int COLS = 16;                                         // 横向分 16 段，每段可以单独上下偏移
            Rect r = sprite.rect; Vector2 piv = sprite.pivot; float ppu = sprite.pixelsPerUnit;
            Texture2D tex = sprite.texture;
            basePos = new Vector3[(COLS + 1) * 2]; pos = new Vector3[basePos.Length];
            weight = new float[basePos.Length]; colors = new Color[basePos.Length];
            Vector2[] uv = new Vector2[basePos.Length];
            int[] tris = new int[COLS * 6];
            for (int i = 0; i <= COLS; i++)
            {
                float u = (float)i / COLS;
                float x = (u * r.width - piv.x) / ppu;                   // 本地坐标 x（单位）
                float canvasX = P_BODY.x + x * PPU_FULL;                 // 换回原图坐标，算它在软管两端之间的位置
                float s = Mathf.Clamp01((canvasX - span.x) / (span.y - span.x));
                float w = Mathf.Pow(Mathf.Sin(Mathf.PI * s), 1.5f);      // 两端 0，中间 1
                for (int j = 0; j < 2; j++)
                {
                    int idx = i * 2 + j;
                    float y = (j * r.height - piv.y) / ppu;              // j=0 底边，j=1 顶边
                    basePos[idx] = new Vector3(x, y, 0f);
                    weight[idx] = w;
                    uv[idx] = new Vector2((r.x + u * r.width) / tex.width, (r.y + j * r.height) / tex.height);
                }
                if (i < COLS)
                {
                    int t = i * 6, a = i * 2;                            // 两个三角形拼一个小矩形
                    tris[t] = a; tris[t + 1] = a + 1; tris[t + 2] = a + 3;
                    tris[t + 3] = a; tris[t + 4] = a + 3; tris[t + 5] = a + 2;
                }
            }
            mesh = new Mesh { name = name };
            mesh.vertices = basePos; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
        }

        // dx, dy：中段偏移（单位）；tint：颜色（跟身体一起淡出）
        public void Apply(float dx, float dy, Color tint)
        {
            if (mesh == null) return;
            for (int i = 0; i < basePos.Length; i++)
            {
                pos[i] = basePos[i] + new Vector3(dx, dy, 0f) * weight[i];
                colors[i] = tint;
            }
            mesh.vertices = pos;
            mesh.colors = colors;
        }
    }
}
