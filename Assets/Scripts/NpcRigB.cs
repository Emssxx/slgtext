using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;            // NavMeshAgent：读 NPC 现在走多快
using UnityEngine.InputSystem;   // 测试用：键盘 A / D / Shift
using UnityEngine.Rendering;     // SortingGroup：几十个部件当成一个整体参与前后遮挡

// ============================================================================
// NpcRigB —— 第二种 NPC 外观（头盔士兵）：运行时把 PSD 部件拼成角色，并用代码做走路 / 跑步 + 惯性。
//
// 【素材】Assets/Resources/NpcArt2/（从你的第二个 PSD 导出，1/4 尺寸，轴心已设在每个部件的转动点）。
//
// 【部件怎么挂】（括号里是 PSD 图层）
//   脚底（根）→ 右脚（在后）/ 左脚（在前）
//            → 身体（躯干底部）→ 右手（在背包、躯干后面，摆动时被身体挡住）
//                               → 背包（挂在肩上，弹 + 摆）
//                               → 躯干
//                               → 头盔组（脖子）：头盔、面罩、软管（软管自己再摆）
//                               → 胸挂组（肩膀）：挂件 + 手枪 + 步枪 + 4 个步枪弹匣 + 4 个手枪弹匣 + 两个弹挂舱
//                                  —— 整组一起动（不分开晃）；胸前的小 AI 终端、勋章、摄像机挂在胸挂上各自摆
//                               → 腰上挂件：匕首、医药包、小医疗包、两颗手雷、水壶（各自摆）
//                               → 左手（最上层；两只手前后摆）
//
// 【动画】全部代码算（不用 Animator）：走 / 跑的钟摆节奏、身体颠动 / 摇摆 / 前倾 / 落地压扁，和第一种 NPC（NpcRig）同一套参数；
//   惯性：起步 / 加速时挂件往后甩，停下 / 减速时往前甩，落地时往下沉再弹回；跑起来挂件被往后拖。
//
// 【现在能做什么】只做了走路 / 跑步（还没有拿枪、换弹、战斗动作），先在测试场景 Scenes/AnimTest2 里看效果。
//   还没接进游戏（没改 NpcView），游戏里哪些 NPC 用第二种外观之后再定。
//   以后接进游戏：挂在 NPC 上就行——会读 NavMeshAgent 的速度、读旧图的翻转（flipX）决定朝向、把旧图变透明（和 NpcRig 一样）。
//
// 【测试】Inspector 里：
//   Test Cycle 打勾 = 自动循环"站 → 走 → 跑 → 停"（原地踏步），看起步 / 加速 / 急停的惯性；
//   Test Keyboard 打勾 = 按住 D / A 往右 / 往左走，再按住 Shift 跑，松开就停（键盘优先，松开 2 秒后回到自动循环）；
//   Test Speed（≥0）= 固定速度原地踏步（改数值时会按 Test Accel 平滑加减速）。
// ============================================================================
public class NpcRigB : MonoBehaviour
{
    // ---------------- 可在 Inspector 里调的参数 ----------------
    [Header("速度判定（单位/秒；闲逛约 1.65，赶路 3.0，逃跑 3.9）")]
    public float walkFullSpeed = 1.2f;    // 速度到这里，走路动作的幅度就是满的（再慢就步子变小）
    public float runStartSpeed = 3.3f;    // 超过它开始从走路变成跑步
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
    [Range(0f, 2f)] public float mountAmount = 1f;            // 胸挂整组（手枪 + 步枪 + 8 个弹匣）
    [Range(0f, 2f)] public float helmetAmount = 1f;           // 头盔（连面罩）
    [Range(0f, 2f)] public float packAmount = 1f;             // 背包
    [Range(0f, 2f)] public float gearAmount = 1f;             // 小挂件（终端 / 勋章 / 摄像机 / 匕首 / 医疗包 / 手雷 / 水壶）
    [Range(0f, 2f)] public float hoseAmount = 1f;             // 软管

    [Header("测试（只在测试场景里用）")]
    [Range(-1f, 4.5f)] public float testSpeed = -1f;          // 固定速度原地踏步：-1 = 不用；1.65 闲逛，3.0 赶路，3.9 逃跑
    public bool testCycle = false;                            // 自动循环：站 → 走 → 跑 → 停
    public float cycleIdle = 1.2f;                            // 循环：站着几秒
    public float cycleWalk = 2.5f;                            // 循环：走几秒
    public float cycleRun = 2.0f;                             // 循环：跑几秒
    public float cycleStop = 1.5f;                            // 循环：急停后站几秒（看挂件往前甩）
    public float cycleWalkSpeed = 1.65f;                      // 循环 / 键盘：走路速度
    public float cycleRunSpeed = 3.9f;                        // 循环 / 键盘：跑步速度
    public float testAccel = 6f;                              // 测试时速度变化多快（单位/秒²；越大起步 / 急停越猛）
    public bool testKeyboard = true;                          // 键盘：按住 D / A 走，加 Shift 跑
    public bool testFaceLeft = false;                         // 强制朝左
    public string currentState = "";                          // （只读）现在是站 / 走 / 跑，速度多少

    // ---------------- 原图坐标（第二个 PSD，2048 画布，单位：像素）----------------
    const float PPU_FULL = 1755f / 0.90625f;  // 和第一种 NPC 一样的比例：原图上每 1 单位有多少像素
    const float REF = PPU_FULL * 0.27f;       // 调参用的"参考像素"（惯性参数和 NpcRig 用同一个刻度）
    const float FEET_Y = -0.453125f;          // 旧 NPC 图的轴心在中间，脚底在它下面 0.453 单位
    static readonly Vector2 P_ROOT = new Vector2(1161, 1988);     // 两脚中间的地面点（整个角色的原点）
    static readonly Vector2 P_BODY = new Vector2(1060, 1655);     // 躯干底部中点（身体颠动、摇摆、压扁都绕它）
    static readonly Vector2 P_FOOT_L = new Vector2(905, 1985), P_FOOT_R = new Vector2(1414, 1983);   // 脚底中点（左脚在画面左边、在前；右脚在画面右边、在后）
    static readonly Vector2 P_HAND_L = new Vector2(599, 1300), P_HAND_R = new Vector2(1655, 1290);   // 手腕（手绕它摆）
    static readonly Vector2 P_PACK = new Vector2(760, 860);       // 背包挂在肩上的点
    static readonly Vector2 P_HEAD = new Vector2(1065, 935);      // 脖子（头盔、面罩绕它晃）
    static readonly Vector2 P_HOSE = new Vector2(705, 845);       // 软管上端
    static readonly Vector2 P_MOUNT = new Vector2(1065, 935);     // 胸挂挂在肩上的点（整组绕它摆）
    // 胸挂上的刚性部件：轴心在各自中心（和胸挂一起动，不单独晃）
    static readonly Vector2 P_MOUNT_IMG = new Vector2(1065, 1238);
    static readonly Vector2[] P_RIFLE_MAG = { new Vector2(928, 1308), new Vector2(1132, 1308), new Vector2(1326, 1312), new Vector2(1496, 1312) };
    static readonly Vector2 P_RIFLE_HOLDER = new Vector2(1200, 1343);
    static readonly Vector2[] P_PISTOL_MAG = { new Vector2(968, 1334), new Vector2(1350, 1328), new Vector2(1512, 1334), new Vector2(1156, 1332) };
    static readonly Vector2 P_PISTOL_HOLDER = new Vector2(1227, 1352);
    static readonly Vector2 P_PISTOL = new Vector2(1268, 1160), P_RIFLE = new Vector2(716, 1240);
    // 挂件的挂点（各自绕它摆）
    static readonly Vector2 P_TERMINAL = new Vector2(860, 1025), P_MEDAL = new Vector2(659, 1061), P_CAMERA = new Vector2(1030, 1071);
    static readonly Vector2 P_KNIFE = new Vector2(1490, 1470), P_MEDKIT = new Vector2(642, 1400), P_MEDKIT_S = new Vector2(873, 1448);
    static readonly Vector2 P_GRENADE1 = new Vector2(1076, 1388), P_GRENADE2 = new Vector2(1200, 1382), P_CANTEEN = new Vector2(530, 1172);

    // ---------------- 运行时 ----------------
    bool _built;                                       // 已经拼好
    Transform _rig, _body, _footL, _footR, _handL, _handR, _pack, _head, _hose, _mount;
    Vector3 _bodyBase, _footLBase, _footRBase, _handLBase, _handRBase, _packBase, _headBase, _mountBase;
    readonly Dictionary<string, Transform> _gear = new Dictionary<string, Transform>();   // 小挂件
    readonly Dictionary<string, Vector3> _gearBase = new Dictionary<string, Vector3>();   // 小挂件原位
    readonly Dictionary<string, Pend> _gearPend = new Dictionary<string, Pend>();         // 小挂件的摆
    readonly Dictionary<string, bool> _onMount = new Dictionary<string, bool>();          // true = 挂在胸挂上
    SpriteRenderer _oldSprite;                         // 旧 NPC 图（游戏里才有；读它的翻转）
    NavMeshAgent _agent;                               // 游戏里读速度

    float _time, _theta, _phaseSeed;                   // 时间 / 步伐相位 / 随机相位（呼吸不同步）
    float _prevRefV, _prevBob, _prevBobV, _prevTilt, _prevTiltV;   // 上一小步的数（算速度 / 加速度）
    float _move, _run, _sv, _bobUp, _tilt;             // 这一帧算好的：走的程度 / 跑的程度 / 摆动位置 / 颠动 / 摇摆
    Spring2 _mountPos, _headPos, _packPos, _beltBounce;   // 位置弹簧（胸挂 / 头盔 / 背包 / 腰上包类上下弹）
    Pend _mountRot, _headRot, _packRot, _hoseRot;      // 转动的摆
    float _prevMountV;                                 // 胸挂上一小步的角速度（算胸前挂件被带动多少）

    // 测试
    float _testV;                                      // 测试时平滑后的速度
    float _cycleT;                                     // 循环计时
    float _keyHold;                                    // 键盘松开后还要再管几秒
    bool _keyLeft;                                     // 键盘最后一次往左
    bool _agentLeft;                                   // 游戏里最后一次往左走

    // ============================================================================
    // 拼角色
    // ============================================================================
    void Start()
    {
        _oldSprite = GetComponent<SpriteRenderer>();                                  // 游戏里 NPC 自己的旧图（测试场景里没有）
        _agent = GetComponent<NavMeshAgent>();                                        // 游戏里的寻路（测试场景里没有）
        Build();
    }

    void Build()
    {
        _phaseSeed = Random.value * 6.28f;                                            // 每个 NPC 呼吸节奏错开

        // —— 层级：Rig（脚底）→ Body（躯干底部）→ 各部件。数字是前后顺序（大的在前面），和你 PSD 的图层顺序一样 ——
        _rig = new GameObject("RigB").transform;
        _rig.SetParent(transform, false);
        _rig.localPosition = new Vector3(0f, FEET_Y, 0f);                            // 原点放在脚底

        _footR = Part("FootR", _rig, "FootR", P_FOOT_R, P_ROOT, 0).transform;         // 右脚（在后，新 PSD 图层 1）
        _footL = Part("FootL", _rig, "FootL", P_FOOT_L, P_ROOT, 1).transform;         // 左脚（在前，新 PSD 图层 2）
        _body = new GameObject("Body").transform;                                     // 身体：颠动、摇摆、压扁都绕躯干底部
        _body.SetParent(_rig, false);
        _body.localPosition = CanvasToLocal(P_BODY - P_ROOT);
        _handR = Part("HandR", _body, "HandR", P_HAND_R, P_BODY, 2).transform;        // 右手（新 PSD 图层 3）：在背包和躯干后面，摆动时被身体挡住
        _pack = Part("Backpack", _body, "Backpack", P_PACK, P_BODY, 3).transform;     // 背包（图层 3）
        Part("Torso", _body, "Torso", P_BODY, P_BODY, 4);                            // 躯干（图层 4）

        // 胸挂组：挂件 + 两把枪 + 8 个弹匣 + 两个弹挂舱，全部挂在同一个节点下，整组一起动
        _mount = new GameObject("Mount").transform;
        _mount.SetParent(_body, false);
        _mount.localPosition = CanvasToLocal(P_MOUNT - P_BODY);                      // 节点放在肩膀（整组绕它摆）
        Part("MountRig", _mount, "Mount", P_MOUNT_IMG, P_MOUNT, 5);                  // 挂件本体（图层 5）
        for (int i = 0; i < 4; i++)
            Part("RifleMag" + (i + 1), _mount, "RifleMag" + (i + 1), P_RIFLE_MAG[i], P_MOUNT, 6 + i);    // 步枪弹匣 ×4（图层 6~9）
        Part("RifleMagHolder", _mount, "RifleMagHolder", P_RIFLE_HOLDER, P_MOUNT, 10);                   // 弹挂舱（步枪，图层 10）
        for (int i = 0; i < 4; i++)
            Part("PistolMag" + (i + 1), _mount, "PistolMag" + (i + 1), P_PISTOL_MAG[i], P_MOUNT, 11 + i); // 手枪弹匣 ×4（图层 11~14）
        Part("PistolMagHolder", _mount, "PistolMagHolder", P_PISTOL_HOLDER, P_MOUNT, 15);                // 手枪挂弹舱（图层 15）

        // 头盔组：头盔 + 面罩 + 软管，绕脖子晃
        _head = new GameObject("Head").transform;
        _head.SetParent(_body, false);
        _head.localPosition = CanvasToLocal(P_HEAD - P_BODY);
        Part("Helmet", _head, "Helmet", P_HEAD, P_HEAD, 16);                         // 头盔（图层 16）
        Part("Mask", _head, "Mask", P_HEAD, P_HEAD, 17);                             // 面罩（图层 17）
        _hose = Part("Hose", _head, "Hose", P_HOSE, P_HEAD, 18).transform;            // 软管（图层 18），绕上端自己摆

        // 小挂件：胸前三件挂在胸挂上，其余挂在腰上（身体）
        //                                                     频率  阻尼  加减速甩 跑动拖后 跟身体摇摆 颠动    最大角度 吊着的程度
        AddGear("Terminal", _mount, P_TERMINAL, P_MOUNT, 19, new Pend(3.2f, 0.22f, 0.012f, 0.006f, 1.2f, 0.0008f, 12f, 0.5f));  // 小型 AI 终端：扣在胸挂上，小幅晃
        AddGear("Medal", _mount, P_MEDAL, P_MOUNT, 20, new Pend(3.4f, 0.12f, 0.040f, 0.016f, 3.0f, 0.0020f, 35f, 1f));       // 勋章：吊着，晃得多
        AddGear("Camera", _mount, P_CAMERA, P_MOUNT, 21, new Pend(3.6f, 0.30f, 0.006f, 0.003f, 0.6f, 0.0003f, 6f, 0.3f));    // 摄像机：夹着，只抖一点
        AddGear("Knife", _body, P_KNIFE, P_BODY, 22, new Pend(3.0f, 0.30f, 0.008f, 0.004f, 0.8f, 0.0004f, 8f, 0.3f));        // 匕首（图层 25）：绑在腰上
        AddGear("Medkit", _body, P_MEDKIT, P_BODY, 23, new Pend(3.0f, 0.30f, 0.008f, 0.004f, 0.8f, 0.0004f, 8f, 0.3f));      // 医药包：绑在腰上
        AddGear("MedkitSmall", _body, P_MEDKIT_S, P_BODY, 24, new Pend(3.2f, 0.28f, 0.010f, 0.005f, 0.9f, 0.0005f, 10f, 0.4f)); // 小医疗包
        AddGear("Grenade1", _body, P_GRENADE1, P_BODY, 25, new Pend(2.8f, 0.18f, 0.025f, 0.012f, 2.0f, 0.0010f, 25f, 0.8f)); // 手雷：挂着，中等幅度
        AddGear("Grenade2", _body, P_GRENADE2, P_BODY, 26, new Pend(3.1f, 0.18f, 0.025f, 0.012f, 2.0f, 0.0010f, 25f, 0.8f)); // 另一颗频率稍不同，不会一起晃
        AddGear("Canteen", _body, P_CANTEEN, P_BODY, 27, new Pend(2.2f, 0.18f, 0.025f, 0.012f, 2.0f, 0.0010f, 35f, 0.8f));  // 水壶：挂在侧面

        Part("Pistol", _mount, "Pistol", P_PISTOL, P_MOUNT, 28);                      // 战斗手枪（图层 28）：固定在胸挂上
        Part("Rifle", _mount, "Rifle", P_RIFLE, P_MOUNT, 29);                         // 战斗步枪（图层 29）：固定在胸挂上
        _handL = Part("HandL", _body, "HandL", P_HAND_L, P_BODY, 31).transform;       // 左手（新 PSD 最上层）：在所有东西前面

        // 记下会动的部件的原始位置（动画 = 原始位置 + 偏移）
        _footLBase = _footL.localPosition; _footRBase = _footR.localPosition;
        _handLBase = _handL.localPosition; _handRBase = _handR.localPosition;
        _bodyBase = _body.localPosition; _packBase = _pack.localPosition;
        _headBase = _head.localPosition; _mountBase = _mount.localPosition;

        // —— 惯性初始化 ——
        _mountPos = new Spring2(Sq(2f * Mathf.PI * 5.0f), 0.45f);                    // 胸挂：勒紧在身上，弹簧硬、阻尼大（紧跟身体，几乎不回弹）
        _headPos = new Spring2(90f, 0.35f);                                           // 头盔：和第一种 NPC 的"头在面罩里晃"一样
        _packPos = new Spring2(Sq(2f * Mathf.PI * 3.6f), 0.25f);                     // 背包
        _beltBounce = new Spring2(Sq(2f * Mathf.PI * 5.0f), 0.30f);                  // 腰上包类上下弹
        //                    频率   阻尼   加减速甩  跑动拖后  跟身体摇摆 颠动     最大角度 吊着的程度
        _mountRot = new Pend(4.0f, 0.45f, 0.003f, 0.001f, 0.3f, 0.0002f, 1.5f, 0f);    // 胸挂勒紧在身上：最多转 1.5 度（步枪很长，角度一大末端就甩得很远）
        _headRot = new Pend(3.0f, 0.25f, -0.008f, -0.002f, 1.0f, 0.0006f, 6f, 0f);     // 头盔：转动点在脖子（下面），加速时头往后仰，所以甩的方向反过来
        _packRot = new Pend(2.6f, 0.25f, 0.008f, 0.004f, 1.0f, 0.0005f, 7f, 0.35f);    // 背包挂在肩上
        _hoseRot = new Pend(3.6f, 0.15f, 0.030f, 0.012f, 2.5f, 0.0020f, 20f, 0.6f);    // 软管：轻，晃得多

        // —— 整体前后遮挡：几十个部件当成一个整体（和旧图同一个排序）——
        SortingGroup group = gameObject.GetComponent<SortingGroup>();
        if (group == null) group = gameObject.AddComponent<SortingGroup>();
        if (_oldSprite != null)
        {
            group.sortingLayerID = _oldSprite.sortingLayerID;    // 和旧图同一个排序层
            group.sortingOrder = _oldSprite.sortingOrder;        // 同一个顺序
            _oldSprite.color = new Color(1f, 1f, 1f, 0f);        // 旧图变透明（不删除：悬停框等还靠它定位）
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

    // 建一个会摆的小挂件，记下原位和它的摆
    void AddGear(string key, Transform parent, Vector2 pivot, Vector2 parentPivot, int order, Pend pend)
    {
        Transform t = Part(key, parent, key, pivot, parentPivot, order).transform;
        _gear[key] = t;                                          // 记下部件
        _gearBase[key] = t.localPosition;                        // 记下原位
        _gearPend[key] = pend;                                   // 记下它的摆
        _onMount[key] = parent == _mount;                        // 挂在胸挂上的会被胸挂带着甩
    }

    static Sprite Load(string path)
    {
        Sprite s = Resources.Load<Sprite>("NpcArt2/" + path);
        if (s == null) Debug.LogWarning("[NpcRigB] 找不到素材：NpcArt2/" + path);
        return s;
    }

    // 原图上的像素距离 → Unity 的本地坐标（原图 y 向下，Unity y 向上，所以 y 取反）
    static Vector3 CanvasToLocal(Vector2 d) { return new Vector3(d.x / PPU_FULL, -d.y / PPU_FULL, 0f); }
    static float Sq(float x) { return x * x; }

    // ============================================================================
    // 每帧：定朝向和速度 → 算物理 → 摆部件
    // ============================================================================
    void Update()
    {
        if (!_built) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);             // 卡顿时不要一步跳太远
        if (dt <= 0f) return;                                     // 暂停时不动

        // 速度：游戏里读 NavMeshAgent；测试开关打开时用测试速度
        Vector3 v3 = _agent != null && _agent.enabled ? _agent.velocity : Vector3.zero;
        float speed = new Vector2(v3.x, v3.y).magnitude;
        if (v3.x < -0.05f) _agentLeft = true; else if (v3.x > 0.05f) _agentLeft = false;   // 记下往哪边走
        float test = TestTarget(dt);                              // 测试想要的速度（-1 = 不测试）
        if (test >= 0f)
        {
            _testV = Mathf.MoveTowards(_testV, test, testAccel * dt);   // 平滑加减速（惯性就是靠加减速甩出来的）
            speed = _testV;
        }

        // 朝向：朝左 = 整个角色水平翻转
        bool left = testFaceLeft || (_keyHold > 0f ? _keyLeft : (_oldSprite != null ? _oldSprite.flipX : _agentLeft));
        _rig.localScale = new Vector3(left ? -1f : 1f, 1f, 1f);

        const int SUB = 4;                                        // 每帧分 4 小步算，弹簧更稳
        for (int i = 0; i < SUB; i++) Step(dt / SUB, speed);
        Apply();
        currentState = (_run > 0.5f ? "跑步" : (_move > 0.05f ? "走路" : "站立")) + "  " + speed.ToString("0.00");
    }

    // 测试：这一帧想要多快（-1 = 不测试，用游戏里的速度）。优先级：键盘 > 自动循环 > 固定速度
    float TestTarget(float dt)
    {
        Keyboard kb = testKeyboard ? Keyboard.current : null;    // 新输入系统的键盘（没有键盘就是 null）
        if (kb != null)
        {
            bool r = kb.dKey.isPressed || kb.rightArrowKey.isPressed;   // 往右
            bool l = kb.aKey.isPressed || kb.leftArrowKey.isPressed;    // 往左
            if (r || l)
            {
                _keyLeft = l && !r;                               // 两个都按就当往右
                _keyHold = 2f;                                    // 松开后再管 2 秒（看急停的惯性）
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                return shift ? cycleRunSpeed : cycleWalkSpeed;    // 按 Shift 跑，不按走
            }
            if (_keyHold > 0f) { _keyHold -= dt; return 0f; }     // 刚松开：停下来
        }
        if (testCycle)
        {
            float total = cycleIdle + cycleWalk + cycleRun + cycleStop;
            _cycleT = Mathf.Repeat(_cycleT + dt, Mathf.Max(0.1f, total));
            float t = _cycleT;
            if (t < cycleIdle) return 0f;                         // ① 站着
            t -= cycleIdle;
            if (t < cycleWalk) return cycleWalkSpeed;             // ② 走
            t -= cycleWalk;
            if (t < cycleRun) return cycleRunSpeed;               // ③ 跑
            return 0f;                                            // ④ 急停
        }
        return testSpeed;                                         // 固定速度（-1 = 不测试）
    }

    // 把速度换算成"预览刻度"的速度，惯性参数就能直接沿用 NpcRig 里调好的数
    float RefSpeed(float speed, float run)
    {
        return 170f * Mathf.Clamp01(speed / 3.0f) + 260f * run;
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

        // 胸挂整组：位置弹簧（加速往后甩、减速往前甩、落地往下沉）+ 绕肩膀小幅摆
        float m = mountAmount;
        _mountPos.Step(dt, new Vector2(-acc * 0.0015f, -boba * 0.0006f) * m, 2f, 1.5f); // 只晃一点点（勒紧在身上），最多偏 2 / 1.5 参考像素
        _mountRot.Step(dt, acc * m, refV * m, tilt, tilta * m, boba * m, 0f);
        float mountAcc = (_mountRot.v - _prevMountV) / dt; _prevMountV = _mountRot.v;   // 胸挂转动的角加速度（带动胸前挂件）

        // 头盔：位置晃 + 绕脖子点头
        float h = helmetAmount;
        _headPos.Step(dt, new Vector2(-acc * 0.008f, -boba * 0.005f) * h, 7f, 5f);
        _headRot.Step(dt, acc * h, refV * h, tilt, tilta * h, boba * h, 0f);

        // 背包：上下弹 + 绕肩膀摆
        float p = packAmount;
        _packPos.Step(dt, new Vector2(-acc * 0.5f, -boba) * 0.004f * p, 5f, 6f);
        _packRot.Step(dt, acc * p, refV * p, tilt, tilta * p, boba * p, 0f);

        // 软管：挂在头盔上，头盔点头时也被带着甩
        float hs = hoseAmount;
        _hoseRot.Step(dt, acc * hs, refV * hs, tilt + _headRot.a, tilta * hs, boba * hs, 0f);

        // 小挂件：胸前的跟着胸挂甩（胸挂一转就被带动），腰上的跟着身体
        float g = gearAmount;
        _beltBounce.Step(dt, new Vector2(0f, -boba) * 0.0035f * g, 0f, 5f);
        foreach (KeyValuePair<string, Pend> kv in _gearPend)
        {
            bool onMount = _onMount[kv.Key];
            float baseTilt = onMount ? tilt + _mountRot.a : tilt;                 // 挂在胸挂上：胸挂也转了
            float extra = onMount ? -0.6f * mountAcc : 0f;                        // 胸挂转动带着甩
            kv.Value.Step(dt, acc * g, refV * g, baseTilt, tilta * g, boba * g, extra);
        }

        _move = move; _run = run; _sv = sv; _bobUp = bobUp; _tilt = tilt;
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
        // 手：右手（画面右边、在身体后）和左脚（画面左边、在前）同方向，左手和右脚同方向；站着时轻轻浮动
        float idle = 1f - move;
        PlaceHand(_handR, _handRBase, sv, idle * 0.0038f * Mathf.Sin(2f * Mathf.PI * 0.45f * _time + 1.3f));
        PlaceHand(_handL, _handLBase, -sv, idle * 0.0038f * Mathf.Sin(2f * Mathf.PI * 0.45f * _time));

        // 胸挂整组 / 头盔 / 背包 / 软管
        _mount.localPosition = _mountBase + SpringOffset(_mountPos);
        _mount.localRotation = Quaternion.Euler(0f, 0f, _mountRot.a);
        _head.localPosition = _headBase + SpringOffset(_headPos);
        _head.localRotation = Quaternion.Euler(0f, 0f, _headRot.a);
        _pack.localPosition = _packBase + SpringOffset(_packPos);
        _pack.localRotation = Quaternion.Euler(0f, 0f, _packRot.a);
        _hose.localRotation = Quaternion.Euler(0f, 0f, _hoseRot.a);

        // 小挂件：腰上的再加一点上下弹
        Vector3 bounce = new Vector3(0f, -_beltBounce.p.y / REF, 0f);
        foreach (KeyValuePair<string, Transform> kv in _gear)
        {
            kv.Value.localPosition = _gearBase[kv.Key] + (_onMount[kv.Key] ? Vector3.zero : bounce);
            kv.Value.localRotation = Quaternion.Euler(0f, 0f, _gearPend[kv.Key].a);
        }
    }

    // 弹簧偏移（预览刻度，y 向下）→ 本地坐标
    static Vector3 SpringOffset(Spring2 s) { return new Vector3(s.p.x / REF, -s.p.y / REF, 0f); }

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

    // ============================================================================
    // 小工具类（和 NpcRig 里的一样）
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
                       + extra;                                          // 被上一节（胸挂）带动
            v += tq * dt;
            a = Mathf.Clamp(a + v * dt, -max, max);
        }
    }
}
