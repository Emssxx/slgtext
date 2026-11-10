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
//     脸型、肤色、发色用 NPC 的 id 算出来，所以同一个 NPC 每次进游戏长得都一样。
//     挂件（锤子 / 斧头 / 枪套+手枪 / 医疗包 / 工具包 / 水瓶）全部拼上，但只显示物品栏里有的那几件（NpcInventory）；
//     测试场景（没有游戏系统）里全部显示。
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
//
// 【第三步加的】拿东西的手（素材在 Resources/NpcArt/Held/）：腰上挂着那件东西才会拿出来，拿着时腰上那件藏起来。
//   战斗：按武器（手枪 = 双手持枪 / 斧头 / 锤子；空手不换）；包扎：医疗包（NpcTaskRunner 通知）；
//   营地外喝水：水杯；到了工地停下施工：锤子；停下采集：树 = 斧头，石头 / 矿石 / 草 = 锤子。
//   同时满足时：战斗 > 包扎 > 喝水 > 施工 > 采集。
//
// 【第四步加的】挥动和后坐：
//   砍树 / 施工 / 采集时，斧头、锤子一直循环挥（抬起 → 猛地挥下 → 停一下 → 收回）；
//   战斗时每出手一次挥一下、开一枪后坐一次（NpcCombat 出手时调 PlayAttack）：枪往上跳、滑套往后弹；
//   枪口有一个子弹生成点（和 PSD 里的枪管水平对齐），开枪时子弹从这里飞出去（FireBullet / TryGetMuzzle）。
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

    [Header("挥动 / 后坐")]
    public float chopPeriod = 1.2f;                           // 砍一下（含停顿）要几秒
    public Vector2 chopAngles = new Vector2(90f, -37.9f);       // 斧头：抬起的角度 / 挥下的角度（度）
    public float hammerPeriod = 0.7f;                         // 敲一下（含停顿）要几秒
    public Vector2 hammerAngles = new Vector2(30f, -18f);     // 锤子：抬起的角度 / 敲下的角度（度）
    public Vector2 recoilKick = new Vector2(80f, 100f);       // 开枪时枪口上扬多少度：每枪在这个范围里随机（最小 / 最大）
    public float slideKick = 150f;                            // 开枪时滑套往后弹多远（原图像素）（标准值）
    public float bulletSpeed = 100f;                          // 子弹飞多快（单位 / 秒）（标准值）
    // 手的移动（单位；x 正 = 往前，y 正 = 往上）：挥的时候手点沿着这些位置走
    public Vector2 chopHandUp = new Vector2(-0.14f, 0.35f);   // 斧头抬起时手往哪挪（往后、往上）
    public Vector2 chopHandDown = new Vector2(0.32f, -0.02f); // 斧头砍下时手往哪挪（往前、往下）（y 实际用下面的随机范围）
    public Vector2 hammerHandUp = new Vector2(-0.05f, 0.035f);   // 锤子抬起
    public Vector2 hammerHandDown = new Vector2(0.27f, 0.03f); // 锤子敲下（y 实际用下面的随机范围）
    public Vector2 chopHandDownY = new Vector2(-0.09f, 0.15f);   // 斧头砍下时手的 y：每砍一下在这个范围里随机（最小 / 最大）
    public Vector2 hammerHandDownY = new Vector2(-0.1f, 0.23f);  // 锤子敲下时手的 y：每敲一下在这个范围里随机（最小 / 最大）
    public Vector2 recoilHandX = new Vector2(-0.4f, -0.2f);    // 开枪时手往后顶多远：每枪在这个范围里随机（最小 / 最大）
    public Vector2 recoilHandY = new Vector2(0.1f, 0.3f);      // 开枪时手往上抬多高：每枪在这个范围里随机（最小 / 最大）
    public float handRecoilRise = 0.03f;                       // 手被顶回去用几秒（越小越猛）
    public Vector2 handRecoilReturn = new Vector2(0.18f, 0.24f); // 手回正的快慢（越大回得越慢）：每枪随机（最小 / 最大）
    public Vector2 muzzleRise = new Vector2(0.03f, 0.15f);     // 枪口上扬用几秒：每枪随机（最小 / 最大；上扬角度是上面的 Recoil Kick）
    public float muzzleReturn = 0.09f;                         // 枪口回正的快慢（越大回得越慢；可以和手不一样）
    // 身体跟着晃（度；正 = 往后仰，负 = 往前倾）
    public Vector2 chopLean = new Vector2(5f, -7f);           // 斧头：抬起时后仰 / 砍下时前倾
    public Vector2 hammerLean = new Vector2(3f, -4f);         // 锤子：抬起 / 敲下
    public float recoilLean = 3f;                             // 开枪时身体被震得往后仰
    public float actionShift = 0.012f;                        // 身体跟着前后挪多远（单位，乘在前倾 / 后仰的比例上）

    [Header("测试（运行时选中 NPC 用；不受腰上挂件限制）")]
    public TestHeld testHeld = TestHeld.None;                 // 强制拿什么（不测试 = 按游戏里的情况）
    public bool testLoop = true;                              // 斧头 / 锤子一直循环挥；手枪每 1.4 秒开一枪
    public bool testFire;                                     // 勾一下：挥一下 / 开一枪（播完自动取消勾选）
    public TestExpr testExpression = TestExpr.None;           // 强制表情（换表情时照样开合面罩）
    [Range(-1f, 4.5f)] public float testSpeed = -1f;          // 强制移动速度（原地踏步看走跑）：-1 = 不用；1.65 闲逛，3.0 赶路，3.9 逃跑
    public bool testFaceLeft;                                 // 强制朝左
    [Header("测试场景用（没有游戏系统时的外观；改了要重新运行）")]
    public bool testMale = true;                              // 男 / 女（不勾 = 女）
    public int testAge = 30;                                  // 年龄（50 以上用老人表情）

    // 测试用的表情选项（名字和 Resources/NpcArt/Expr/某套/ 里的文件名一样）
    public enum TestExpr
    {
        [InspectorName("不测试")] None,
        [InspectorName("平常")] Normal,
        [InspectorName("说话")] Talk,
        [InspectorName("惊吓")] Scared,
        [InspectorName("疲惫")] Tired,
        [InspectorName("休息")] Rest,
        [InspectorName("极度疲惫")] Exhausted,
        [InspectorName("失常")] Dazed,
        [InspectorName("工作")] Work,
        [InspectorName("崩溃")] Broken,
        [InspectorName("生气（只有老人）")] Angry,
    }

    // 现在朝左吗（测试开关优先，否则读旧图的翻转）
    bool FacingLeft => testFaceLeft || (_aimUntil > _time ? _aimLeft : (_oldSprite != null && _oldSprite.flipX));

    float _aimUntil;               // 瞄准朝向管到什么时候（NpcCombat 边走边打时一直续上）
    bool _aimLeft;                 // 瞄准时朝左吗

    // 外部调用（NpcCombat 拿枪战斗时每帧调）：接下来 0.3 秒角色朝着 target，不管往哪走（往后退时也面朝敌人）
    public void AimAt(Vector3 target)
    {
        float dx = target.x - transform.position.x;
        if (dx < -0.01f) _aimLeft = true;              // 敌人在左边
        else if (dx > 0.01f) _aimLeft = false;         // 敌人在右边（正上 / 正下就保持原来的朝向）
        _aimUntil = _time + 0.3f;
    }

    // 测试用的选项（Inspector 里显示中文）
    public enum TestHeld
    {
        [InspectorName("不测试")] None,
        [InspectorName("斧头")] Axe,
        [InspectorName("锤子")] Hammer,
        [InspectorName("手枪")] Pistol,
        [InspectorName("医疗包")] Medkit,
        [InspectorName("水杯")] Bottle,
    }

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
    // 拿东西的手：转动点在原图上的位置（手腕；持枪是右手中心）。和 Resources/NpcArt/Held/ 里的图对应
    static readonly Dictionary<string, Vector2> HELD_PIVOT = new Dictionary<string, Vector2>
    {
        { "axe", new Vector2(910, 1263) },       // 握斧
        { "hammer", new Vector2(968, 1252.5f) }, // 握锤
        { "medkit", new Vector2(971, 1117.5f) }, // 拿医疗包
        { "bottle", new Vector2(1325, 1143.5f) },// 拿水杯
        { "pistol", new Vector2(1697, 1172) },   // 双手持枪
    };
    static readonly Vector2 P_SLIDE = new Vector2(1729.5f, 981.5f);   // 手枪滑套中心（原图）
    static readonly Vector2 P_MUZZLE = new Vector2(1914f, 979f);      // 枪口：枪管最前端，和枪管中线水平对齐（原图）
    static readonly Dictionary<string, string> HELD_ART = new Dictionary<string, string>
    {
        { "axe", "Held/Axe" }, { "hammer", "Held/Hammer" }, { "medkit", "Held/Medkit" }, { "bottle", "Held/Bottle" }, { "pistol", "Held/Pistol" },
    };
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

    // 只读：身上有没有挂某个挂件（hammer / axe / holster(手枪) / medkit / toolkit / bottle）——挂件只在物品栏里有那件东西时才显示。
    public bool HasGear(string key) => key != null && _gear.TryGetValue(key, out Transform g) && g != null && g.gameObject.activeInHierarchy;

    // 挂件的根节点 ↔ 物品栏里的物品（锤子 / 斧头连着挂环一起藏，手枪连着枪套，拉链跟着医疗包）
    static readonly string[,] GEAR_ITEMS = { { "ringH", "hammer" }, { "ringA", "axe" }, { "holster", "pistol" }, { "medkit", "medkit" }, { "toolkit", "toolkit" }, { "bottle", "bottle" } };
    int _invRev = -1;              // 上次按物品栏刷新挂件时的版本号

    // 按物品栏显示 / 隐藏挂件（物品栏一变 NpcInventory.Revision 就变，Update 里看到就调这里）
    void ApplyInventory()
    {
        _invRev = NpcInventory.Revision;
        bool testScene = WorldManager.Instance == null;                                     // 测试场景：全部显示，方便调动画
        NpcData n = testScene ? null : WorldManager.Instance.GetNpcById(_npcId);
        for (int i = 0; i < GEAR_ITEMS.GetLength(0); i++)
        {
            if (!_gear.TryGetValue(GEAR_ITEMS[i, 0], out Transform root) || root == null) continue;
            bool show = testScene || NpcInventory.Has(n, GEAR_ITEMS[i, 1]);
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        }
        _heldTimer = 0f;                                                                    // 手上拿的东西马上重新判断（比如斧头刚放回仓库）
    }
    readonly Dictionary<string, Vector3> _gearBase = new Dictionary<string, Vector3>();    // 挂件原始位置
    NpcNeedRunner _needRunner;     // 读 NPC 正在做的需求行为（吃 / 休息 / 采集 / 逃跑）
    string _npcId;                 // 这个 NPC 的 id（拼好角色时记下）
    Transform _visorPivot, _glare; // 面罩的转动点（顶边）/ 反光
    SpriteRenderer _heldSprite;    // 拿东西的手（换手时显示）
    SpriteRenderer _handLSprite, _handRSprite;  // 平时的前手 / 后手（拿东西时藏起来）
    string _held;                  // 现在手上拿的是什么（null = 空手）
    float _heldTimer;              // 距离下一次判断拿什么还有多久
    float _speedNow;               // 这一帧的移动速度（判断"停下来干活"用）
    float _lastWater = -1f;        // 上次看到的水分（突然涨了 = 刚喝了水）
    readonly Dictionary<string, float> _holdUntil = new Dictionary<string, float>();       // 临时拿着某件东西到什么时候（医疗包 / 水杯）
    readonly List<SpriteRenderer> _hiddenBelt = new List<SpriteRenderer>();                // 因为拿在手上而藏起来的腰上挂件
    SpriteRenderer _slideSprite, _pistolHands;  // 手枪的滑套 / 持枪的两只手（只有拿枪时显示）
    Transform _handPoint;          // 手点：拿东西的手挂在它下面，挥动 / 开枪时移动它；空手时跟着前手（外部也可以用它挂特效）
    float _actionLean, _actionShift;  // 出手时身体额外的倾斜（度）/ 前后挪动（单位）
    Transform _muzzle;             // 枪口子弹生成点（跟着枪一起动）
    Vector3 _heldBase, _slideBase; // 拿东西的手 / 滑套的原始位置（动画 = 原始位置 + 偏移）
    bool _heldLoop;                // 是在干活（施工 / 采集）：一直循环挥
    float _swingT = -1f;           // 挥动进行了多久（-1 = 没在挥）
    float _recoilT = -1f;          // 开枪后坐进行了多久（-1 = 没在后坐）
    bool _swingNew = true;         // 要开始新的一下（重新随机挥下时手的 y）
    float _swingDownY;             // 这一下挥下时手的 y（随机出来的）
    Vector2 _recoilHandOff;        // 这一枪手被顶回的距离（随机出来的）
    float _shotKick, _shotHandReturn, _shotMuzzleRise;  // 这一枪的枪口上扬角度 / 手回正快慢 / 枪口上扬用时（随机出来的）
    static Sprite _bulletSprite;   // 子弹图（代码生成，所有 NPC 共用）
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
        float noWorld = 0f;                                              // 场景里一直没有 WorldManager 多久了
        while (data == null)                                             // 一直等到能查到这个 NPC 的数据
        {
            if (_view != null && !string.IsNullOrEmpty(_view.npcId) && WorldManager.Instance != null)
                data = WorldManager.Instance.GetNpcById(_view.npcId);
            if (data == null && WorldManager.Instance == null)           // 测试场景：没有游戏系统
            {
                noWorld += 0.2f;
                if (noWorld >= 0.6f)                                     // 等了一会儿还是没有：用测试外观直接拼
                {
                    string testId = _view != null && !string.IsNullOrEmpty(_view.npcId) ? _view.npcId : "test_npc";
                    Build(testId, testMale, testAge);
                    yield break;
                }
            }
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
        // 挂件：以前是 0~3 件随机；现在全部拼上，显示哪些由物品栏决定（ApplyInventory）。
        // 随机数照旧抽一遍，后面用到随机数的外观不会因此变样。
        List<string> pool = new List<string> { "Hammer", "Axe", "Pistol", "Medkit", "Toolkit" };
        int[] counts = { 0, 1, 1, 2, 2, 3 };
        int n = counts[rng.Next(counts.Length)];
        HashSet<string> gear = new HashSet<string>();
        for (int i = 0; i < n; i++) { int k = rng.Next(pool.Count); gear.Add(pool[k]); pool.RemoveAt(k); }
        gear = new HashSet<string> { "Hammer", "Axe", "Pistol", "Medkit", "Toolkit" };

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
        AddGear("bottle", Part("Bottle", _body, "Gear/Bottle", P_BOTTLE, P_BODY, 19).transform);     // 水瓶（物品栏里有才显示）
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
        // 拿东西的手：平时藏着；画在面罩上面（东西拿在面罩外面）
        _handLSprite = _handL.GetComponent<SpriteRenderer>();                         // 平时的前手
        _handRSprite = _handR.GetComponent<SpriteRenderer>();                         // 平时的后手
        GameObject heldGo = new GameObject("Held");                                   // 拿东西的手
        _handPoint = new GameObject("HandPoint").transform;                           // 手点（挥动 / 开枪时移动它）
        _handPoint.SetParent(_body, false);                                           // 跟着身体动（颠动 / 摇摆）
        _handPoint.localPosition = _handL.localPosition;                              // 一开始在前手的位置
        heldGo.transform.SetParent(_handPoint, false);                                // 拿东西的手挂在手点下面
        _heldSprite = heldGo.AddComponent<SpriteRenderer>();
        _heldSprite.sortingOrder = 24;                                                // 比面罩（22）和反光（23）靠前
        _heldSprite.enabled = false;                                                  // 一开始空手
        // 手枪拆成三层：枪身（_heldSprite）→ 滑套 → 两只手；枪口生成点也挂在枪上
        _slideSprite = Part("PistolSlide", heldGo.transform, "Held/PistolSlide", P_SLIDE, HELD_PIVOT["pistol"], 25);  // 滑套
        _slideBase = _slideSprite.transform.localPosition;                            // 记下滑套原位
        _slideSprite.enabled = false;
        _pistolHands = Part("PistolHands", heldGo.transform, "Held/PistolHands", HELD_PIVOT["pistol"], HELD_PIVOT["pistol"], 26);  // 持枪的手（盖在滑套上）
        _pistolHands.enabled = false;
        _muzzle = new GameObject("Muzzle").transform;                                 // 枪口子弹生成点
        _muzzle.SetParent(heldGo.transform, false);                                   // 跟着枪动（后坐、朝左翻转）
        _muzzle.localPosition = CanvasToLocal(P_MUZZLE - HELD_PIVOT["pistol"]);       // 枪管最前端

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
        ApplyInventory();                                        // 挂件按物品栏显示
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
        if (_invRev != NpcInventory.Revision) ApplyInventory();  // 物品栏变了：刷新挂件
        float dt = Mathf.Min(Time.deltaTime, 0.05f);             // 卡顿时不要一步跳太远
        if (dt <= 0f) return;                                     // 暂停时不动

        // 朝向：读旧图的翻转（NpcMover 管它）。朝左 = 整个角色水平翻转
        float facing = FacingLeft ? -1f : 1f;
        _rig.localScale = new Vector3(facing, 1f, 1f);

        // 速度：只看水平面上的移动快慢
        Vector3 v3 = _agent != null && _agent.enabled ? _agent.velocity : Vector3.zero;
        float speed = new Vector2(v3.x, v3.y).magnitude;
        if (testSpeed >= 0f) speed = testSpeed;                   // 测试：强制速度（原地踏步）

        const int SUB = 4;                                        // 每帧分 4 小步算，弹簧更稳
        for (int i = 0; i < SUB; i++) Step(dt / SUB, speed);
        UpdateFace(dt);                                           // 表情 + 面罩开合
        _speedNow = speed;                                        // 记下速度（判断停下来干活用）
        UpdateHeld(dt);                                           // 拿东西的手
        UpdateTest(dt);                                           // 测试开关
        AnimateHeld(dt);                                          // 挥动 / 后坐
        Apply();
    }

    // 测试开关：换了测试选项马上换手；勾了"测试出手"就挥一下 / 开一枪；手枪循环时每 1.4 秒自动开一枪
    TestHeld _lastTest = TestHeld.None;
    float _testFireTimer;
    void UpdateTest(float dt)
    {
        if (testHeld != _lastTest) { _lastTest = testHeld; _heldTimer = 0f; UpdateHeld(0f); }  // 选项变了：马上换手
        bool fire = testFire;                                     // 勾了一下
        if (testHeld == TestHeld.Pistol && testLoop)              // 手枪循环：定时自动开枪
        {
            _testFireTimer -= dt;
            if (_testFireTimer <= 0f) { _testFireTimer = 1.4f; fire = true; }
        }
        if (!fire) return;
        testFire = false;                                         // 自动取消勾选
        PlayAttack();                                             // 挥一下 / 后坐一下
        if (_held == "pistol")                                    // 拿着枪：往正前方 2.5 单位开一枪
        {
            float facing = FacingLeft ? -1f : 1f;
            if (TryGetMuzzle(out Vector3 m)) FireBullet(m + new Vector3(2.5f * facing, 0f, 0f));
        }
    }

    // 测试选项 → 手上拿的东西
    static string TestKey(TestHeld t)
    {
        switch (t)
        {
            case TestHeld.Axe: return "axe";
            case TestHeld.Hammer: return "hammer";
            case TestHeld.Pistol: return "pistol";
            case TestHeld.Medkit: return "medkit";
            case TestHeld.Bottle: return "bottle";
            default: return null;
        }
    }

    // ============================================================================
    // 拿东西的手
    // ============================================================================

    // 外部调用：让这个 NPC 手上拿着某件东西 seconds 秒（现在只有包扎用：key = "medkit"）。腰上没挂这件就不会拿。
    public void HoldItem(string key, float seconds)
    {
        if (string.IsNullOrEmpty(key)) return;                    // 没说拿什么
        _holdUntil[key] = _time + seconds;                        // 记下拿到什么时候
        _heldTimer = 0f;                                          // 下一帧马上重新判断
    }

    void UpdateHeld(float dt)
    {
        if (_heldSprite == null) return;                          // 还没拼好
        _heldTimer -= dt;
        if (_heldTimer > 0f) return;                              // 每 0.25 秒判断一次就够了
        _heldTimer = 0.25f;
        string want = PickHeld();                                 // 现在该拿什么
        if (want != _held) ApplyHeld(want);                       // 变了才换
    }

    // 按优先级选：战斗 > 包扎 > 营地外喝水 > 施工 > 采集。返回 null = 空手。
    string PickHeld()
    {
        _heldLoop = false;                                        // 先当作不是在干活
        if (testHeld != TestHeld.None)                            // 测试开关打开：直接拿测试选的东西
        {
            _heldLoop = testLoop && (testHeld == TestHeld.Axe || testHeld == TestHeld.Hammer);  // 斧头 / 锤子要不要循环挥
            return TestKey(testHeld);
        }
        NpcData n = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(_npcId) : null;  // 这个 NPC 的数据
        if (n == null) return null;

        // 喝水检测：水分突然涨了一大截 = 刚喝了一份水；在营地外喝，就拿水杯 2.5 秒
        if (_lastWater >= 0f && n.water > _lastWater + 15f && !CampZone.Contains(transform.position))
            _holdUntil["bottle"] = _time + 2.5f;
        _lastWater = n.water;                                     // 记下这次的水分

        // 1. 战斗：按武器（武器本来就是按腰上挂的东西选的）
        if (NpcCombat.IsFighting(_npcId))
        {
            string w = NpcCombat.WeaponOf(_npcId).key;            // pistol / axe / hammer / fists
            if (w == "pistol" || w == "axe" || w == "hammer") return w;  // 空手（fists）不换
        }
        // 2. 包扎（NpcTaskRunner 用 HoldItem 通知）
        if (Holding("medkit") && HasGear("medkit")) return "medkit";
        // 3. 营地外喝水（水杯每人都有）
        if (Holding("bottle")) return "bottle";
        bool still = _speedNow < 0.1f;                            // 停下来了（到了地方开始干活）
        if (!still) return null;
        _heldLoop = true;                                         // 下面两种是干活：工具一直循环挥
        // 4. 施工：被施工占用、而且停下来了
        if (NpcBusy.IsHeldBy(_npcId, NpcBusy.Build) && HasGear("hammer")) return "hammer";
        // 5. 采集：正在采的那个资源就在旁边
        ResourceNode node = HarvestingNode();
        if (node != null)
        {
            if (node.kind == ResourceKind.Tree) return HasGear("axe") ? "axe" : null;   // 砍树：斧头
            return HasGear("hammer") ? "hammer" : null;                                  // 石头 / 矿石 / 草：锤子
        }
        return null;
    }

    // 某件临时拿着的东西还没到时间
    bool Holding(string key) { return _holdUntil.TryGetValue(key, out float t) && t > _time; }

    // 这个 NPC 正在采、而且就在旁边的资源（没有返回 null）
    ResourceNode HarvestingNode()
    {
        float cell = GridManager.Instance != null ? GridManager.Instance.cellSize : 0.34f;  // 小格大小
        foreach (ResourceNode r in ResourceNode.All)
        {
            if (r == null || r.depleted || r.Harvester != _npcId) continue;              // 不是他在采
            float reach = r.size * cell * 0.75f + 0.9f;                                  // 和 NpcTaskRunner 的"够得着"一样
            if (Vector2.Distance(transform.position, r.Center) <= reach) return r;       // 就在旁边：正在干
        }
        return null;
    }

    // 换手：先把上一次藏起来的东西还原，再按新的拿法藏手、藏腰上的挂件
    void ApplyHeld(string key)
    {
        _held = key;                                                       // 记下现在拿什么
        if (_handLSprite != null) _handLSprite.enabled = true;            // 两只手先都显示回来
        if (_handRSprite != null) _handRSprite.enabled = true;
        foreach (SpriteRenderer sr in _hiddenBelt) if (sr != null) sr.enabled = true;  // 腰上的挂件放回去
        _hiddenBelt.Clear();

        Sprite art = key != null && HELD_ART.TryGetValue(key, out string path) ? Load(path) : null;  // 拿东西的手的图
        bool isPistol = key == "pistol" && art != null;                     // 拿的是手枪（要显示滑套和两只手）
        if (_slideSprite != null) _slideSprite.enabled = isPistol;
        if (_pistolHands != null) _pistolHands.enabled = isPistol;
        _swingT = -1f; _recoilT = -1f;                                     // 换手时停掉正在播的挥动 / 后坐
        // 持枪：手点挂在角色根节点上，不跟着身体颠动、摇摆、前倾、压扁（边走边跑时枪也稳稳端着）；其他：挂在身体上
        Transform handParent = isPistol ? _rig : _body;
        if (_handPoint.parent != handParent) _handPoint.SetParent(handParent, false);
        if (art == null) { _heldSprite.enabled = false; _held = null; return; }   // 空手（或找不到图）
        _heldSprite.sprite = art;                                          // 换图
        _handPoint.localPosition = CanvasToLocal(HELD_PIVOT[key] - (isPistol ? P_ROOT : P_BODY));  // 手点放到手腕的位置（按挂的父节点换算）
        _handPoint.localRotation = Quaternion.identity;                    // 角度归零
        _heldSprite.transform.localPosition = Vector3.zero;                // 拿东西的手就在手点上
        _heldBase = _handPoint.localPosition;                              // 记下原位（挥动 / 后坐在它上面加偏移）
        _heldSprite.enabled = true;

        if (key == "pistol") { HideHand(_handLSprite); HideHand(_handRSprite); }  // 双手持枪：两只手都换
        else HideHand(_handLSprite);                                        // 其他：换前面那只手

        // 腰上的那件藏起来（挂环 / 枪套留着）
        if (key == "hammer") HideGear("hammer");
        else if (key == "axe") HideGear("axe");
        else if (key == "medkit") { HideGear("medkit"); HideGear("zipper"); }
        else if (key == "bottle") HideGear("bottle");
        else if (key == "pistol" && _gear.TryGetValue("holster", out Transform h))
        {
            Transform pistol = h.Find("Pistol");                            // 手枪插在枪套下面
            if (pistol != null) HideRenderer(pistol.GetComponent<SpriteRenderer>());
        }
    }

    // ---------------- 挥动 / 后坐 ----------------

    // 外部调用（NpcCombat 出手的那一下）：马上换好手，然后挥一下 / 开枪后坐一下
    public void PlayAttack()
    {
        if (_heldSprite == null) return;                                    // 还没拼好
        _rig.localScale = new Vector3(FacingLeft ? -1f : 1f, 1f, 1f);  // 刚转身也先对好朝向（枪口位置才准）
        string want = PickHeld();                                           // 不等 0.25 秒，马上判断拿什么
        if (want != _held) ApplyHeld(want);
        if (_held == "axe" || _held == "hammer") { _swingT = 0f; _swingNew = true; }   // 挥一下（重新随机）
        else if (_held == "pistol")                                         // 后坐一下
        {
            _recoilT = 0f;
            _recoilHandOff = new Vector2(                                   // 这一枪手被顶回多远：在范围里随机
                Random.Range(Mathf.Min(recoilHandX.x, recoilHandX.y), Mathf.Max(recoilHandX.x, recoilHandX.y)),
                Random.Range(Mathf.Min(recoilHandY.x, recoilHandY.y), Mathf.Max(recoilHandY.x, recoilHandY.y)));
            _shotKick = RandIn(recoilKick);                                 // 这一枪枪口上扬多少度
            _shotHandReturn = RandIn(handRecoilReturn);                     // 这一枪手回正的快慢
            _shotMuzzleRise = RandIn(muzzleRise);                           // 这一枪枪口上扬用几秒
        }
    }

    // 枪口子弹生成点的世界坐标（手上拿着枪才有）
    public bool TryGetMuzzle(out Vector3 pos)
    {
        bool ok = _held == "pistol" && _muzzle != null;                     // 拿着枪
        pos = ok ? _muzzle.position : transform.position;                  // 没枪就给角色自己的位置
        return ok;
    }

    // 从枪口生成一颗子弹，飞向 target（只是画面上的子弹；打没打中仍由 NpcCombat 算）。没拿枪返回 false。
    public bool FireBullet(Vector3 target)
    {
        if (!TryGetMuzzle(out Vector3 from)) return false;                  // 没拿枪
        target.z = from.z;                                                  // 同一个平面
        GameObject go = new GameObject("Bullet");                           // 子弹（不挂在 NPC 身上，人死了子弹照样飞完）
        go.transform.position = from;                                       // 从枪口出发
        Vector3 d = target - from;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);  // 朝着目标
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = BulletSprite();
        sr.sortingOrder = 60;                                               // 画在人和建筑上面
        float dur = Mathf.Max(0.04f, d.magnitude / Mathf.Max(1f, bulletSpeed));  // 飞多久
        Destroy(go, dur + 0.5f);                                            // 保险：到时间一定删掉
        StartCoroutine(FlyBullet(go.transform, from, target, dur));         // 飞过去
        return true;
    }

    IEnumerator FlyBullet(Transform b, Vector3 from, Vector3 to, float dur)
    {
        float t = 0f;
        while (t < dur && b != null)
        {
            t += Time.deltaTime;
            b.position = Vector3.Lerp(from, to, t / dur);                   // 沿直线飞
            yield return null;
        }
        if (b != null) Destroy(b.gameObject);                               // 到了：消失
    }

    // 子弹图：一小条亮黄色，尾巴渐隐（代码画，不用素材）
    static Sprite BulletSprite()
    {
        if (_bulletSprite != null) return _bulletSprite;
        const int w = 20, h = 5;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float along = (float)x / (w - 1);                           // 0 = 尾巴，1 = 头
                float across = 1f - Mathf.Abs(y - (h - 1) * 0.5f) / (h * 0.5f);  // 中间亮、上下边淡
                t.SetPixel(x, y, new Color(1f, 0.93f, 0.6f, Mathf.Clamp01(along * 1.4f) * Mathf.Clamp01(across * 1.6f)));
            }
        t.Apply();
        _bulletSprite = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(1f, 0.5f), 160f);  // 轴心在头上（子弹位置 = 弹头）
        return _bulletSprite;
    }

    // 每帧：按挥动 / 后坐的进度摆拿东西的手
    void AnimateHeld(float dt)
    {
        _actionLean = 0f; _actionShift = 0f;                                // 身体的额外晃动先清零
        if (_handPoint == null) return;                                     // 还没拼好
        if (_heldSprite == null || !_heldSprite.enabled)                    // 空手：手点跟着前手
        {
            _handPoint.localPosition = _handL.localPosition;
            _handPoint.localRotation = Quaternion.identity;
            return;
        }
        float angle = 0f, slide = 0f;
        Vector3 off = Vector3.zero;
        if (_held == "axe" || _held == "hammer")
        {
            bool axe = _held == "axe";
            float period = Mathf.Max(0.2f, axe ? chopPeriod : hammerPeriod);
            if (_swingT < 0f && _heldLoop) { _swingT = 0f; _swingNew = true; }   // 在干活：开始循环挥
            if (_swingT >= 0f)
            {
                _swingT += dt;
                if (_swingT >= period)                                       // 这一下挥完了
                {
                    _swingT = _heldLoop ? _swingT - period : -1f;           // 干活一直循环；战斗只挥一次
                    _swingNew = true;                                        // 下一下重新随机
                }
                if (_swingT >= 0f) angle = SwingAngle(axe, _swingT / period);
            }
            if (_swingNew)                                                   // 新的一下：挥下时手的 y 在范围里随机
            {
                Vector2 r = axe ? chopHandDownY : hammerHandDownY;
                _swingDownY = Random.Range(Mathf.Min(r.x, r.y), Mathf.Max(r.x, r.y));
                _swingNew = false;
            }
            // 手点和身体跟着角度走：角度往"抬起"那边多少，就往抬起姿势靠多少；往"挥下"那边同理
            Vector2 ang = axe ? chopAngles : hammerAngles;                  // x = 抬起角度，y = 挥下角度
            Vector2 handUp = axe ? chopHandUp : hammerHandUp, handDown = axe ? chopHandDown : hammerHandDown;
            handDown.y = _swingDownY;                                        // y 用这一下随机到的值
            Vector2 lean = axe ? chopLean : hammerLean;
            float kUp = ang.x != 0f && angle > 0f ? angle / ang.x : 0f;     // 0~1：抬起了多少
            float kDown = ang.y != 0f && angle < 0f ? angle / ang.y : 0f;   // 0~1：挥下了多少
            Vector2 h = handUp * kUp + handDown * kDown;                    // 手点的偏移
            off = new Vector3(h.x, h.y, 0f);
            _actionLean = lean.x * kUp + lean.y * kDown;                    // 抬起后仰、挥下前倾
            _actionShift = actionShift * (kDown - kUp);                     // 挥下时身体往前送，抬起时往后收
        }
        else if (_held == "pistol" && _recoilT >= 0f)
        {
            _recoilT += dt;
            float k = Kick(_recoilT, handRecoilRise, _shotHandReturn);      // 手：一下顶回去，再慢慢回正
            float km = Kick(_recoilT, _shotMuzzleRise, muzzleReturn);       // 枪口：单独的上扬 / 回正节奏
            angle = _shotKick * km;                                         // 枪口往上扬（绕握把转）
            off = new Vector3(_recoilHandOff.x * k, _recoilHandOff.y * k, 0f);  // 手被顶回来（这一枪随机到的距离）
            _actionLean = recoilLean * k;                                   // 身体被震得往后仰
            _actionShift = -actionShift * k;                                // 往后挪一点
            slide = slideKick * (_recoilT < 0.025f ? _recoilT / 0.025f : Mathf.Max(0f, 1f - (_recoilT - 0.025f) / 0.12f));  // 滑套弹回去
            float endT = 0.3f + 6f * Mathf.Max(handRecoilRise + _shotHandReturn, _shotMuzzleRise + muzzleReturn);  // 等手和枪口都回正了
            if (_recoilT > endT) _recoilT = -1f;                            // 播完了
        }
        _handPoint.localPosition = _heldBase + off;                         // 手点移动（拿着的东西跟着走）
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, angle);         // 绕手腕（持枪是握把）转
        if (_slideSprite != null) _slideSprite.transform.localPosition = _slideBase + new Vector3(-slide / PPU_FULL, 0f, 0f);
    }

    // 在范围里随机取一个数（两个数谁大谁小都行）
    static float RandIn(Vector2 r) { return Random.Range(Mathf.Min(r.x, r.y), Mathf.Max(r.x, r.y)); }

    // 后坐曲线：t = 开枪后过了几秒；rise 秒内从 0 冲到 1，然后按 ret 的快慢回到 0
    static float Kick(float t, float rise, float ret)
    {
        rise = Mathf.Max(0.001f, rise); ret = Mathf.Max(0.001f, ret);
        return t < rise ? t / rise : Mathf.Exp(-(t - rise) / ret);
    }

    // 挥动的角度：u = 0~1 是一次挥动的进度
    float SwingAngle(bool axe, float u)
    {
        float up = axe ? chopAngles.x : hammerAngles.x;                     // 抬起的角度
        float down = axe ? chopAngles.y : hammerAngles.y;                   // 挥下的角度
        if (u < 0.30f) { float t = u / 0.30f; return up * (1f - (1f - t) * (1f - t)); }        // 抬起（先快后慢）
        if (u < 0.40f) { float t = (u - 0.30f) / 0.10f; return Mathf.Lerp(up, down, t * t); }  // 猛地挥下（越来越快）
        if (u < 0.48f) return down;                                                             // 砍中，停一下
        if (u < 0.80f) { float t = (u - 0.48f) / 0.32f; return Mathf.Lerp(down, 0f, t * t * (3f - 2f * t)); }  // 收回
        return 0f;                                                                              // 歇一下再来
    }

    void HideHand(SpriteRenderer sr) { if (sr != null) sr.enabled = false; }
    void HideGear(string key) { if (_gear.TryGetValue(key, out Transform t)) HideRenderer(t.GetComponent<SpriteRenderer>()); }
    void HideRenderer(SpriteRenderer sr)
    {
        if (sr == null || !sr.enabled) return;                              // 没有 / 本来就藏着
        sr.enabled = false;                                                 // 藏起来
        _hiddenBelt.Add(sr);                                                // 记下，换回空手时放回去
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
        if (testExpression != TestExpr.None) return testExpression.ToString();   // 测试：强制表情
        NpcData n = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(_npcId) : null;  // 这个 NPC 的数据
        if (n == null) return "Normal";                                           // 查不到就用平常
        SanityLevel lv = MentalState.GetLevel(n);                                 // 精神等级
        if (lv == SanityLevel.Broken) return "Broken";                            // 崩溃
        if (lv == SanityLevel.Dazed || lv == SanityLevel.Hallucinating) return "Dazed";  // 理智低：失常
        bool doing = _needRunner != null && _needRunner.IsRunning;                // 正在执行某个需求行为
        if (NpcHealth.Has(n, HealthTag.Injured) || NpcHealth.Has(n, HealthTag.CriticalWound) || NpcHealth.Has(n, HealthTag.Dying)
            || (doing && _needRunner.Current == NpcDecisionRules.Behavior.Flee)
            || NpcCombat.IsFleeing(_npcId)) return "Scared";  // 受伤 / 逃跑 / 躲敌人：惊吓
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
        _body.localPosition = _bodyBase + new Vector3(_actionShift, _bobUp, 0f);      // 出手时身体前后挪
        _body.localRotation = Quaternion.Euler(0f, 0f, _tilt + _actionLean);          // 出手时身体前倾 / 后仰
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
