using System.Collections;          // IEnumerator：子弹飞行、枪火淡出
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
// 【现在能做什么】走路 / 跑步 + 手枪（持枪、开枪、换弹）；还没有步枪、匕首。在动画测试场景 Scenes/AnimTest 里看效果。
//   还没接进游戏（没改 NpcView），游戏里哪些 NPC 用第二种外观之后再定。
//   【手枪】持枪、开枪后坐（枪口上扬 + 手被顶回 + 滑套 + 身体后仰）、枪火、子弹、跳弹、锁定瞄准 / 翻身：做法和参数照搬第一种 NPC，
//   素材用武器补充 PSD（NpcArt2/Weapons/）。测试：Test Held 选"手枪"，Test Loop / Test Fire 开枪，Test Aim Target 拖目标进来瞄准。
//   【手枪手电筒】拿着手枪时，枪口下方的挂载手电筒亮起来，朝枪口方向照（URP 2D 聚光，会投影子）。
//   【手枪换弹】战术 / 快速 / 普通三种（动作和参数照搬第一种 NPC）。一共 5 个弹匣（枪里 1 + 胸挂 4）：
//     新弹匣从胸挂的满弹匣袋拔出来；战术 / 普通换弹的旧弹匣插回胸挂上的空袋（普通是先插回再拿新的），快速换弹直接丢掉。
//     所以第一次换弹（还没有空袋）只能是快速换弹——选了战术 / 普通也会自动改成快速。测试：Test Reload。
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
    public TestHeld testHeld = TestHeld.None;                 // 强制拿什么（现在只有手枪）
    public bool testLoop = true;                              // 手枪每 1.4 秒自动开一枪
    public bool testFire;                                     // 勾一下：开一枪（播完自动取消勾选）
    public Transform testAimTarget;                           // 拖一个物体进来：枪口一直对准它（转过头会翻身），子弹往它打
    public bool testReload;                                   // 勾一下：换一次弹匣（按下面选的种类；拿着手枪才换）
    public ReloadKind testReloadKind = ReloadKind.Tactical;   // 测试换弹用哪一种
    public string currentState = "";                          // （只读）现在是站 / 走 / 跑，速度多少

    [Header("持枪 / 开枪（参数和第一种 NPC 一样）")]
    public Vector2 recoilKick = new Vector2(80f, 100f);       // 开枪时枪口上扬多少度：每枪在这个范围里随机（最小 / 最大）
    public float slideKick = 150f;                            // 开枪时滑套（枪套）往后滑多远（原图像素）
    public float slideBackTime = 0.03f;                       // 滑套往后滑用几秒
    public float slideHoldTime = 0.03f;                       // 滑到最后面停多久（秒）
    public float slideReturnTime = 0.15f;                     // 滑回原位用几秒
    public float bulletSpeed = 100f;                          // 子弹飞多快（单位 / 秒）
    public float flashTime = 0.07f;                           // 枪火亮多久（秒）
    public float flashSize = 1f;                              // 枪火多大（倍数；1 = 按 PSD 画的大小）
    [Range(0f, 1f)] public float ricochetChance = 0.3f;       // 子弹没打中、落到地上时出跳弹效果的概率
    public float ricochetTime = 0.15f;                        // 跳弹效果显示多久（秒）
    public Vector2 recoilHandX = new Vector2(-0.4f, -0.2f);   // 开枪时手往后顶多远：每枪随机（最小 / 最大）
    public Vector2 recoilHandY = new Vector2(0.1f, 0.3f);     // 开枪时手往上抬多高：每枪随机（最小 / 最大）
    public float handRecoilRise = 0.03f;                      // 手被顶回去用几秒（越小越猛）
    public Vector2 handRecoilReturn = new Vector2(0.18f, 0.24f);   // 手回正的快慢（越大回得越慢）：每枪随机
    public Vector2 muzzleRise = new Vector2(0.03f, 0.15f);    // 枪口上扬用几秒：每枪随机
    public float muzzleReturn = 0.09f;                        // 枪口回正的快慢（越大回得越慢）
    public float recoilLean = 3f;                             // 开枪时身体被震得往后仰（度）
    public float actionShift = 0.012f;                        // 身体跟着往后挪多远（单位）
    public Color muzzleLightColor = new Color(1f, 0.72f, 0.38f);   // 开枪时火光照亮周围的颜色
    public float muzzleLightIntensity = 2.5f;                 // 开枪火光亮度
    public float muzzleLightRadius = 2.2f;                    // 开枪火光照多远（单位）
    public Vector2 muzzleLightRandom = new Vector2(0.4f, 1.3f);   // 每一枪火光亮度的倍数：在这个范围里随机（每枪亮度都不一样，闪的过程中也会轻微跳动）
    public Vector2 aimShoulder = new Vector2(0.175f, 0.466f); // 持枪手绕着转的"肩膀"（相对脚底中心，单位；选中 NPC 时 Scene 里的黄点）
    [Header("手枪挂载手电筒（枪口下方、握把前面那个小突起）")]
    public bool gunLight = true;                              // 拿着手枪时手电筒开不开
    public Color gunLightColor = new Color(0.85f, 0.93f, 1f); // 光的颜色（冷白）
    public float gunLightIntensity = 1f;                      // 亮度
    public float gunLightRange = 5f;                          // 照多远（单位）
    public float gunLightAngle = 35f;                         // 光束多宽（度）
    public float gunLightGlowSize = 0.6f;                     // 灯头本身发光点多大（倍数）
    public float aimTurnSpeed = 720f;                         // 瞄准时枪口转多快（度 / 秒）
    public float aimFlipOver = 5f;                            // 枪口转过正上 / 正下方再多几度才翻身（防止来回翻）

    [Header("换弹 1：战术换弹（参数和第一种 NPC 一样；时间 = 秒，距离 = 单位，角度 = 度）")]
    public string rigMags = "";                               // （只读）胸挂 4 个手枪弹匣袋，从右到左：满 / 空弹匣（插回去的旧弹匣）/ 空袋。一共 5 个弹匣 = 枪里 1 个 + 胸挂 4 个
    public string lastReloadKind = "";                        // （只读）上一次实际用的换弹（没有空袋放旧弹匣时，战术 / 普通会自动改成快速）
    public bool testRefillMags;                               // 勾一下：胸挂弹匣全部补满（满弹匣用完时也会自动补满，测试用）
    public float reloadSpeed = 1f;                            // 测试时整体播放速度
    public float rlLockTime = 0.12f;                          // ① 滑套空仓挂机，整把枪往上抬、往前送
    public float rlFetchTime = 0.2f;                          // ② 右手离开握把，伸到胸挂弹匣袋
    public float rlGrabTime = 0.28f;                          // ③ 从弹匣袋拔出新弹匣
    public float rlBringTime = 0.30f;                         // ④ 拿着新弹匣到握把下方
    public float rlDropTime = 0.3f;                           // ⑤ 枪抖一下，旧弹匣掉进右手（两个弹匣成 V 字）
    public float rlAlignTime = 0.1f;                          // ⑥ 右手和枪一起转，新弹匣对准握把口
    public float rlInsertTime = 0.10f;                        // ⑦ 插入新弹匣
    public float rlReleaseTime = 0.3f;                        // ⑧ 弹匣插到底枪上下抖 → 滑套复位枪旋转抖（和 ⑨ 同时开始）
    public float rlStowTime = 0.4f;                           // ⑨ 右手带着旧弹匣回到弹匣袋
    public float rlReturnTime = 0.30f;                        // ⑩ 右手回到握把，枪放下
    public float rlSlideLock = 1f;                            // 空仓挂机时滑套往后多少（1 = 开枪时滑套最远的位置）
    public Vector2 rlGunRaise = new Vector2(0.03f, 0.06f);    // 枪往前（x）/ 往上（y）抬多少
    public float rlGunTilt = 30f;                             // 换弹时持枪手（连枪）整体转多少度（正 = 枪口往上）
    public float rlGunAlign = 30f;                            // ⑥ 枪配合右手转多少度
    public float rlArc = -0.05f;                              // 右手来回走弧线时往上拱多高（负 = 往下兜）
    public float rlPullOut = 0.06f;                           // ③ 从弹匣袋里往外拔多远（沿弹匣方向；放旧弹匣时反过来往里塞）
    public Vector2 rlMagInHand = new Vector2(0f, 0.012f);     // 弹匣底部在右手里的位置（手盖住弹匣底部）
    public float rlUnderGap = 0.03f;                          // ④ 新弹匣顶端离握把口还有多远
    public float rlReady = 0.085f;                            // ⑥ 对准时新弹匣还差多远插到底
    public float rlNewTilt = 18f;                             // V 字：新弹匣往后歪多少度
    public float rlOldTilt = -18f;                            // V 字：旧弹匣往前歪多少度
    public Vector2 rlOldOffset = new Vector2(0.012f, 0f);     // V 字：旧弹匣相对新弹匣往前挪多少
    public float rlShake = 0.1f;                              // 上下抖的幅度（位移）：⑤ 旧弹匣掉出来、⑦ 新弹匣插到底
    public float rlShakeAngle = 45f;                          // 旋转抖的幅度（角度）：⑤ 旧弹匣掉出来、⑧ 滑套复位
    public float rlSlideDelay = 0.3f;                         // 弹匣插到底后隔多久滑套复位（0 = 同时）
    public float rlShakeFreq = 5f;                            // 抖动有多快（次 / 秒）

    [Header("换弹 2：快速换弹（只列和战术换弹不一样的）")]
    public float qrDropAt = 0f;                               // 开始换弹后多久旧弹匣掉出来（0 = 枪口一往上抬就掉）
    public float qrDropTime = 0f;                             // ⑤ 右手拿到握把下方后停多久再对准
    public Vector2 qrWait = Vector2.zero;                     // ④ 右手在握把下方往旁边让多少（按枪的方向）
    public float qrSlideSpeed = 0.6f;                         // 旧弹匣从握把里滑出来的初速度（单位 / 秒）
    public float qrGravity = 5f;                              // 旧弹匣往下掉的重力（单位 / 秒²）
    public Vector2 qrKick = new Vector2(-0.1f, 0.1f);         // 旧弹匣滑出来后往前 / 往后飘的速度：随机（单位 / 秒）
    public Vector2 qrSpin = new Vector2(-500f, -250f);        // 旧弹匣边掉边转多快：随机（度 / 秒）
    public Vector2 qrGroundY = new Vector2(-0.05f, 0.03f);    // 落到多高算落地：随机（相对脚底）
    public float qrGroundTime = 3f;                           // 在地上躺多久再淡出消失（秒）

    [Header("换弹 3：普通换弹（只列和战术换弹不一样的）")]
    public float nrCatchTime = 0.15f;                         // ② 右手从握把移到握把下方，准备接旧弹匣
    public float nrSwitchTime = 0.2f;                         // 旧弹匣插回空袋后，手移到满弹匣那个袋子用几秒

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
    static readonly Vector2[] P_PISTOL_MAG_BOTTOM = { new Vector2(905, 1418), new Vector2(1287, 1412), new Vector2(1450, 1418), new Vector2(1094, 1416) };   // 胸挂上 4 个手枪弹匣的底部
    static readonly int[] POUCH_ORDER = { 2, 1, 3, 0 };           // 换弹时从哪个弹匣袋拿（从最右边开始：弹匣 3 → 2 → 4 → 1）
    const float POUCH_MAG_TURN = 54f - 74.8f;                     // 胸挂上的弹匣比枪里的弹匣多斜多少度（PSD 量的）
    const float MAG_AXIS = 74.8f;                                 // 弹匣图本身斜的角度（弹匣底 → 顶的方向，度）
    static readonly Vector2 MAG_DIR = new Vector2(0.262f, 0.965f); // 弹匣插进去的方向（枪的本地坐标，沿弹匣往上；PSD 量的）
    static readonly Vector2 P_PISTOL_HOLDER = new Vector2(1227, 1352);
    static readonly Vector2 P_PISTOL = new Vector2(1268, 1160), P_RIFLE = new Vector2(716, 1240);
    // 挂件的挂点（各自绕它摆）
    static readonly Vector2 P_TERMINAL = new Vector2(860, 1025), P_MEDAL = new Vector2(659, 1061), P_CAMERA = new Vector2(1030, 1071);
    static readonly Vector2 P_KNIFE = new Vector2(1490, 1470), P_MEDKIT = new Vector2(642, 1400), P_MEDKIT_S = new Vector2(873, 1448);
    // 手枪（武器补充 PSD，和角色同一个坐标系；轴心和 Docs/ABA武装人员_武器素材.md 里的表一样）
    static readonly Vector2 P_GRIP = new Vector2(1792, 1085);     // 握把 = 持枪右手中心（整把枪 + 两只持枪手绕它转）
    static readonly Vector2 P_MUZZLE = new Vector2(2061, 855);    // 枪口（子弹生成点、枪火从这里出来）
    static readonly Vector2 P_GUN_MAG = new Vector2(1699, 1144), P_GUN_SLIDE = new Vector2(1858, 870), P_GUN_BODY = new Vector2(1852, 981);
    static readonly Vector2 P_GUN_LIGHT = new Vector2(2056, 962);  // 手枪挂载手电筒的灯头（枪口下方、握把前面的小突起）
    static readonly Vector2 P_GUN_HAND_L = new Vector2(1720, 1036), P_GUN_HAND_R = new Vector2(1792, 1085);
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

    // 手枪
    string _held;                                      // 现在手上拿的是什么（null = 空手，"pistol" = 手枪）
    Transform _handPoint, _muzzle, _slide;             // 手点（整把枪挂在它下面）/ 枪口 / 滑套
    Vector2 _gripBase;                                 // 握把原位（根节点本地坐标）
    Vector3 _slideBase;                                // 滑套原位
    readonly List<SpriteRenderer> _pistolParts = new List<SpriteRenderer>();   // 手枪各部件 + 两只持枪手
    SpriteRenderer _bodyHandL, _bodyHandR, _mountPistol;   // 平时的两只手 / 胸挂上的手枪（拿枪时藏起来）
    float _recoilT = -1f;                              // 开枪后坐进行了多久（-1 = 没在后坐）
    Vector2 _recoilHandOff;                            // 这一枪手被顶回的距离（随机出来的）
    float _shotKick, _shotHandReturn, _shotMuzzleRise; // 这一枪的枪口上扬角度 / 手回正快慢 / 枪口上扬用时
    float _actionLean, _actionShift;                   // 开枪时身体额外的后仰（度）/ 往后挪（单位）
    float _aimAng, _aimWant, _aimUntil;                // 现在的枪口角度 / 想转到的角度 / 瞄准到什么时候
    bool _aimLeft;                                     // 瞄准时朝左
    Vector3 _aimPoint;                                 // 瞄准点（世界坐标）
    Transform _lockTarget;                             // 锁定的目标
    TestHeld _lastTest = TestHeld.None;                // 上一帧的测试选项（变了就换手）
    Transform _gunLamp;                                // 手电筒灯头（挂在枪上，跟着枪转、后坐）
    SpriteRenderer _gunLampGlow;                       // 灯头发光点
    UnityEngine.Rendering.Universal.Light2D _gunBeam;  // 手电筒的光（URP 2D 聚光）
    static Sprite _glowSprite;                         // 发光圆点图（代码画）
    static Material _additive;                         // 叠加发光材质
    float _testFireTimer;                              // 手枪循环开枪的计时

    // 换弹
    SpriteRenderer _gunMag, _gunHandR;                 // 枪里的弹匣 / 枪上的右手
    Transform _rlHand;                                 // 换弹时的右手（单独移动）
    SpriteRenderer _rlHandSprite, _rlNewMag, _rlOldMag;   // 换弹时的右手 / 新弹匣 / 旧弹匣
    readonly SpriteRenderer[] _mountMags = new SpriteRenderer[4];   // 胸挂上的 4 个手枪弹匣
    readonly int[] _slotState = { SLOT_FULL, SLOT_FULL, SLOT_FULL, SLOT_FULL };   // 4 个弹匣袋的状态（下标 = 弹匣 1~4）
    const int SLOT_NONE = 0, SLOT_FULL = 1, SLOT_EMPTY = 2;  // 空袋 / 满弹匣 / 空弹匣（插回去的旧弹匣）
    int _takeSlot, _stowSlot = -1;                     // 这次从哪个袋子拿新弹匣 / 旧弹匣插回哪个空袋（-1 = 丢掉）
    bool _oldStowed;                                   // 这次的旧弹匣已经插回去了
    float _reloadT = -1f;                              // 换弹进行到哪（"标准时间"秒；-1 = 没在换弹）
    float _reloadRate = 1f;                            // 换弹播放速度
    ReloadKind _reloadKind = ReloadKind.Tactical;      // 这次换弹是哪一种
    bool _qrDropped;                                   // 快速换弹：旧弹匣已经整个滑出来、自己往下掉了
    bool _magTaken;                                    // 这次的新弹匣已经从袋里拿走了

    // 朝向：测试强制 > 瞄准 > 键盘 > 旧图翻转（游戏里）> 往哪边走
    bool FacingLeft => testFaceLeft || (_aimUntil > _time ? _aimLeft : (_keyHold > 0f ? _keyLeft : (_oldSprite != null ? _oldSprite.flipX : _agentLeft)));

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
            _mountMags[i] = Part("PistolMag" + (i + 1), _mount, "PistolMag" + (i + 1), P_PISTOL_MAG[i], P_MOUNT, 11 + i); // 手枪弹匣 ×4（图层 11~14；换弹时从这里拿）
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

        _mountPistol = Part("Pistol", _mount, "Pistol", P_PISTOL, P_MOUNT, 28);       // 战斗手枪（图层 28）：固定在胸挂上（拔枪时藏起来）
        Part("Rifle", _mount, "Rifle", P_RIFLE, P_MOUNT, 29);                         // 战斗步枪（图层 29）：固定在胸挂上
        _handL = Part("HandL", _body, "HandL", P_HAND_L, P_BODY, 31).transform;       // 左手（新 PSD 最上层）：在所有东西前面

        _bodyHandR = _handR.GetComponent<SpriteRenderer>();                           // 平时的两只手（拿枪时换成持枪手）
        _bodyHandL = _handL.GetComponent<SpriteRenderer>();

        // 手枪：挂在角色根节点上（和第一种 NPC 一样：不跟着身体颠动 / 摇摆，边走边跑枪也稳稳端着）
        // 图层顺序和武器 PSD 一样（从下到上）：弹匣 → 滑套 → 枪身 → 持枪左手 → 持枪右手；都画在身上所有东西前面
        _handPoint = new GameObject("HandPoint").transform;
        _handPoint.SetParent(_rig, false);
        _gripBase = CanvasToLocal(P_GRIP - P_ROOT);                                   // 握把原位
        _handPoint.localPosition = new Vector3(_gripBase.x, _gripBase.y, 0f);
        Transform gun = new GameObject("HeldPistol").transform;                       // 整把枪 + 持枪手
        gun.SetParent(_handPoint, false);
        _gunMag = Part("PistolMag", gun, "Weapons/PistolMag", P_GUN_MAG, P_GRIP, 40);   // 枪里的弹匣（轴心在弹匣底部）
        _pistolParts.Add(_gunMag);
        SpriteRenderer slide = Part("PistolSlide", gun, "Weapons/PistolSlide", P_GUN_SLIDE, P_GRIP, 41);
        _pistolParts.Add(slide);
        _slide = slide.transform; _slideBase = _slide.localPosition;                  // 滑套（开枪时往后弹）
        _pistolParts.Add(Part("PistolBody", gun, "Weapons/PistolBody", P_GUN_BODY, P_GRIP, 42));
        _pistolParts.Add(Part("PistolHandL", gun, "Weapons/PistolHandL", P_GUN_HAND_L, P_GRIP, 43));
        _gunHandR = Part("PistolHandR", gun, "Weapons/PistolHandR", P_GUN_HAND_R, P_GRIP, 44);   // 持枪右手（换弹时换成下面那只单独动的右手）
        _pistolParts.Add(_gunHandR);
        // 换弹用：右手 + 新弹匣 + 旧弹匣，挂在角色根节点上（位置每帧按枪算）
        _rlHand = new GameObject("ReloadHand").transform;
        _rlHand.SetParent(_rig, false);
        _rlHandSprite = _rlHand.gameObject.AddComponent<SpriteRenderer>();
        _rlHandSprite.sprite = Load("Weapons/PistolHandR");
        _rlHandSprite.sortingOrder = 45;                                              // 在枪的所有部件前面
        _rlNewMag = new GameObject("ReloadNewMag").AddComponent<SpriteRenderer>();
        _rlNewMag.transform.SetParent(_rig, false);
        _rlNewMag.sprite = Load("Weapons/PistolMag");
        _rlNewMag.sortingOrder = 39;                                                  // 在枪身下面（像插在握把里）
        _rlOldMag = new GameObject("ReloadOldMag").AddComponent<SpriteRenderer>();
        _rlOldMag.transform.SetParent(_rig, false);
        _rlOldMag.sprite = Load("Weapons/PistolMag");
        _rlOldMag.sortingOrder = 38;
        _rlHandSprite.enabled = _rlNewMag.enabled = _rlOldMag.enabled = false;
        RefillMags();                                                                 // 胸挂上 4 个手枪弹匣一开始都是满的
        _muzzle = new GameObject("Muzzle").transform;                                 // 枪口子弹生成点（跟着枪动）
        _muzzle.SetParent(gun, false);
        _muzzle.localPosition = CanvasToLocal(P_MUZZLE - P_GRIP);
        _gunLamp = new GameObject("GunLight").transform;                              // 手电筒灯头（跟着枪动）
        _gunLamp.SetParent(gun, false);
        _gunLamp.localPosition = CanvasToLocal(P_GUN_LIGHT - P_GRIP);
        _gunLampGlow = GlowDot(_gunLamp);                                             // 灯头一直亮（不受光照）
        if (RenderMats.IsURP)                                                         // URP 2D：真正照亮前面的光
        {
            _gunBeam = WorldLighting.CreatePoint("GunFlashlight", transform, gunLightColor, gunLightIntensity, gunLightRange, gunLightAngle);
            WorldLighting.EnableShadows(_gunBeam);                                    // 照到建筑 / 树石会投影子
        }
        SetHeld(null);                                                                // 一开始空手

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
        UpdateAim(dt);                                            // 瞄准（可能会翻身，所以放在定朝向前面）
        _rig.localScale = new Vector3(FacingLeft ? -1f : 1f, 1f, 1f);

        const int SUB = 4;                                        // 每帧分 4 小步算，弹簧更稳
        for (int i = 0; i < SUB; i++) Step(dt / SUB, speed);
        UpdateTest(dt);                                           // 测试开关（换手 / 开枪）
        AnimateHeld(dt);                                          // 持枪 / 后坐
        Apply();
        UpdateGunLight();                                         // 手枪手电筒跟着枪口方向照
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
        _body.localPosition = _bodyBase + new Vector3(_actionShift, _bobUp, 0f);     // 开枪时身体往后挪
        _body.localRotation = Quaternion.Euler(0f, 0f, _tilt + _actionLean);          // 开枪时身体往后仰
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

    // ============================================================================
    // 手枪：持枪 / 瞄准 / 开枪后坐 / 枪火 / 子弹（做法和参数都照搬第一种 NPC：NpcRig）
    //   不同的只有素材和位置：手枪、持枪手、枪火、子弹用 NpcArt2/Weapons/ 里你画的图，握把 / 枪口按你的 PSD 量。
    // ============================================================================

    // 换手：key = "pistol" 拿出手枪（两只手换成持枪手、胸挂上的手枪藏起来）；null = 空手
    void SetHeld(string key)
    {
        _held = key;                                                        // 记下现在拿什么
        bool p = key == "pistol";                                           // 拿的是手枪
        foreach (SpriteRenderer sr in _pistolParts) if (sr != null) sr.enabled = p;   // 手枪各部件 + 持枪手
        if (_bodyHandL != null) _bodyHandL.enabled = !p;                    // 平时的两只手：拿枪时藏起来
        if (_bodyHandR != null) _bodyHandR.enabled = !p;
        if (_mountPistol != null) _mountPistol.enabled = !p;                // 胸挂上的手枪：拔出来了就藏起来
        _recoilT = -1f;                                                     // 停掉正在播的后坐
        StopReload();                                                       // 停掉换弹
        if (!p) { _aimAng = 0f; _lockTarget = null; _aimUntil = 0f; }       // 收枪：瞄准清零
        _handPoint.localPosition = new Vector3(_gripBase.x, _gripBase.y, 0f);                               // 手点回到握把原位
        _handPoint.localRotation = Quaternion.identity;
        if (_slide != null) _slide.localPosition = _slideBase;              // 滑套复位
    }

    // 测试开关：Test Held 换手；手枪 + Test Loop 每 1.4 秒自动开一枪；勾 Test Fire 开一枪（和第一种 NPC 一样）
    void UpdateTest(float dt)
    {
        if (testHeld != _lastTest) { _lastTest = testHeld; SetHeld(testHeld == TestHeld.Pistol ? "pistol" : null); }   // 选项变了：马上换手
        if (testRefillMags && _reloadT < 0f) { testRefillMags = false; RefillMags(); }   // 勾了"补满"：胸挂弹匣全部补满
        if (testReload) { testReload = false; PlayReload(0f, testReloadKind); }   // 勾了"测试换弹"：按选的种类换一次
        bool fire = testFire;                                               // 勾了一下
        if (_reloadT >= 0f) { testFire = false; return; }                   // 换弹时不开枪
        if (_held == "pistol" && testLoop)                                  // 手枪循环：定时自动开枪
        {
            _testFireTimer -= dt;
            if (_testFireTimer <= 0f) { _testFireTimer = 1.4f; fire = true; }
        }
        if (!fire) return;
        testFire = false;                                                   // 自动取消勾选
        if (_held != "pistol") return;                                      // 没拿枪不开
        PlayAttack();                                                       // 后坐一下
        if (testAimTarget != null) FireBullet(testAimTarget.position);      // 有测试目标：往目标打
        else if (TryGetMuzzle(out Vector3 m))                               // 没有：往正前方 2.5 单位打
            FireBullet(m + new Vector3(2.5f * (FacingLeft ? -1f : 1f), 0f, 0f));
    }

    // 外部调用（以后 NpcCombat 拿枪战斗时每帧调）：锁定一个目标，枪口一直跟着它转；停止调用 0.3 秒后自动解除。传 null = 马上解除
    public void LockOn(Transform target)
    {
        if (target == null) { _lockTarget = null; _aimUntil = 0f; return; } // 解除锁定
        if (_aimUntil <= _time) _aimLeft = FacingLeft;                      // 刚开始锁定：先保持现在的朝向
        _lockTarget = target;
        _aimPoint = target.position;
        _aimUntil = _time + 0.3f;
    }

    // 每帧：算瞄准角度、判断要不要翻身，枪口慢慢转过去（dt = 0 时只算不转）
    void UpdateAim(float dt)
    {
        if (testAimTarget != null && _held == "pistol")                     // 测试：一直锁定拖进来的物体
        {
            if (_aimUntil <= _time) _aimLeft = FacingLeft;
            _lockTarget = testAimTarget;
            _aimUntil = _time + 0.3f;
        }
        if (_lockTarget != null && !_lockTarget.gameObject.activeInHierarchy) { _lockTarget = null; _aimUntil = 0f; }   // 目标没了：解除
        float want = 0f;                                                    // 没瞄准：枪口平端
        if (_aimUntil > _time && _held == "pistol")
        {
            if (_lockTarget != null) _aimPoint = _lockTarget.position;      // 锁定：跟着目标走
            Vector2 t = AimLocal(_aimPoint);                                // 目标在角色本地的位置（x 正 = 前方）
            want = AimAngle(t);
            float lim = 90f + Mathf.Max(0f, aimFlipOver);
            if (!testFaceLeft && Mathf.Abs(want) > lim)                     // 枪口转过了正上 / 正下方
            {
                float other = AimAngle(new Vector2(-t.x, t.y));             // 翻过身以后要转多少度
                if (Mathf.Abs(other) < Mathf.Abs(want))                     // 翻过去更顺：翻身
                {
                    _aimLeft = !_aimLeft;
                    _aimAng = Mathf.DeltaAngle(0f, 180f - _aimAng);         // 现在的枪口角度也镜像过去（画面上枪口不跳）
                    want = other;
                }
            }
        }
        _aimWant = want;
        float step = aimTurnSpeed > 0f ? aimTurnSpeed * dt : 9999f;         // 这一帧最多转多少度
        _aimAng = Mathf.MoveTowards(_aimAng, want, step);
    }

    // 世界坐标 → 角色根节点（脚底中心）的本地坐标，朝左时 x 反过来（x 正 = 前方）
    Vector2 AimLocal(Vector3 world)
    {
        Vector3 p = transform.InverseTransformPoint(world) - (_rig != null ? _rig.localPosition : Vector3.zero);
        return new Vector2(FacingLeft ? -p.x : p.x, p.y);
    }

    // 枪要转多少度，枪管这条线才正好穿过目标 t。手绕肩膀转，枪跟手一起转
    float AimAngle(Vector2 t)
    {
        Vector2 arm = _gripBase - aimShoulder;                              // 肩膀 → 握把（枪没转时）
        Vector2 muzzle = CanvasToLocal(P_MUZZLE - P_GRIP);                  // 握把 → 枪口
        float h = arm.y + muzzle.y;                                         // 枪管这条线比肩膀高多少
        Vector2 q = t - aimShoulder;                                        // 肩膀 → 目标
        float len = Mathf.Max(q.magnitude, 0.001f);
        float phi = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;                  // 从肩膀看目标的角度
        float fix = Mathf.Asin(Mathf.Clamp(h / len, -1f, 1f)) * Mathf.Rad2Deg;   // 枪管不在肩膀高度：补一点角度
        return Mathf.DeltaAngle(0f, phi - fix);
    }

    // 枪转了 ang 度、手被顶开 push 时，握把在哪（根节点本地坐标）
    Vector2 AimHand(float ang, Vector2 push) { return aimShoulder + Rot(_gripBase - aimShoulder + push, ang); }

    // 马上把枪摆到瞄准的位置（开枪那一下用，枪口位置才准）
    void ApplyAimPose()
    {
        Vector2 hp = AimHand(_aimAng, Vector2.zero);
        _handPoint.localPosition = new Vector3(hp.x, hp.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, _aimAng);
    }

    // 外部调用（出手的那一下）：开枪后坐一下（每一枪的幅度在范围里随机，和第一种 NPC 一样）
    public void PlayAttack()
    {
        if (_held != "pistol") return;                                      // 没拿枪
        _recoilT = 0f;
        _recoilHandOff = new Vector2(RandIn(recoilHandX), RandIn(recoilHandY));   // 这一枪手被顶回多远
        _shotKick = RandIn(recoilKick);                                     // 这一枪枪口上扬多少度
        _shotHandReturn = RandIn(handRecoilReturn);                         // 这一枪手回正的快慢
        _shotMuzzleRise = RandIn(muzzleRise);                               // 这一枪枪口上扬用几秒
        UpdateAim(0f);                                                      // 重新算一下瞄准（只算不转）
        _aimAng = _aimWant;                                                 // 开枪这一下枪口直接对准目标
        _rig.localScale = new Vector3(FacingLeft ? -1f : 1f, 1f, 1f);       // 可能刚翻身：对好朝向
        ApplyAimPose();                                                     // 马上摆好，子弹从对准后的枪口出来
    }

    // 每帧：按后坐进度摆持枪手、枪口上扬、滑套、身体后仰
    void AnimateHeld(float dt)
    {
        _actionLean = 0f; _actionShift = 0f;                                // 身体的额外晃动先清零
        if (_held != "pistol") return;
        if (_reloadT >= 0f) { AnimateReload(dt); return; }                  // 正在换弹：换弹动画接管
        float angle = 0f, slide = 0f;
        Vector2 off = Vector2.zero;
        if (_recoilT >= 0f)
        {
            _recoilT += dt;
            float k = Kick(_recoilT, handRecoilRise, _shotHandReturn);      // 手：一下顶回去，再慢慢回正
            float km = Kick(_recoilT, _shotMuzzleRise, muzzleReturn);       // 枪口：单独的上扬 / 回正节奏
            angle = _shotKick * km;                                         // 枪口往上扬（绕握把转）
            off = _recoilHandOff * k;                                       // 手被顶回来
            _actionLean = recoilLean * k;                                   // 身体被震得往后仰
            _actionShift = -actionShift * k;                                // 往后挪一点
            slide = slideKick * SlideCurve(_recoilT);                        // 滑套往后滑 → 停一下 → 滑回来
            float slideEnd = Mathf.Max(0.001f, slideBackTime) + Mathf.Max(0f, slideHoldTime) + Mathf.Max(0.001f, slideReturnTime);
            float endT = Mathf.Max(slideEnd, 0.3f + 6f * Mathf.Max(handRecoilRise + _shotHandReturn, _shotMuzzleRise + muzzleReturn));  // 等手、枪口、滑套都回正了
            if (_recoilT > endT) _recoilT = -1f;                            // 播完了
        }
        Vector2 hp = AimHand(_aimAng, off);                                 // 手绕肩膀转到瞄准角度，后坐把手顶回来
        _handPoint.localPosition = new Vector3(hp.x, hp.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, _aimAng + angle);   // 瞄准角度 + 枪口上扬
        if (_slide != null) _slide.localPosition = _slideBase + new Vector3(-slide / PPU_FULL, 0f, 0f);   // 滑套沿枪身往后
    }

    // 滑套曲线：开枪后 t 秒滑套在哪（0 = 原位，1 = 最后面）。往后滑 → 停一下 → 滑回来（快慢都能在 Inspector 里调）
    float SlideCurve(float t)
    {
        float back = Mathf.Max(0.001f, slideBackTime), hold = Mathf.Max(0f, slideHoldTime), ret = Mathf.Max(0.001f, slideReturnTime);
        if (t < back) return t / back;                                      // 往后滑
        if (t < back + hold) return 1f;                                     // 停在最后面
        return Mathf.Max(0f, 1f - (t - back - hold) / ret);                 // 滑回来
    }

    // 枪口（子弹生成点）的世界坐标（手上拿着枪才有）
    public bool TryGetMuzzle(out Vector3 pos)
    {
        bool ok = _held == "pistol" && _muzzle != null;
        pos = ok ? _muzzle.position : transform.position;
        return ok;
    }

    // 从枪口生成一颗子弹飞向 target（只是画面；打没打中由战斗系统算）。没拿枪返回 false
    public bool FireBullet(Vector3 target) { return FireBullet(target, true); }

    // 同上；canRicochet = 落到地上（没打中）时有概率出跳弹
    public bool FireBullet(Vector3 target, bool canRicochet)
    {
        if (!TryGetMuzzle(out Vector3 from)) return false;                  // 没拿枪
        target.z = from.z;
        GameObject go = new GameObject("Bullet");                           // 子弹不挂在 NPC 身上（人没了子弹照样飞完）
        go.transform.position = from;
        Vector3 d = target - from;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);   // 朝着目标
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Load("Weapons/PistolBullet");                           // 你画的手枪子弹（轴心在弹头）
        sr.sortingOrder = 60;                                               // 画在人和建筑上面
        if (_oldSprite != null) sr.sortingLayerID = _oldSprite.sortingLayerID;
        RenderMats.MakeUnlit(sr);                                           // 子弹自己发亮，不受光
        float dur = Mathf.Max(0.04f, d.magnitude / Mathf.Max(1f, bulletSpeed));   // 飞多久
        Destroy(go, dur + 0.5f);                                            // 保险：一定删掉
        StartCoroutine(FlyBullet(go.transform, from, target, dur, canRicochet));
        MuzzleFlash();                                                      // 枪火
        return true;
    }

    IEnumerator FlyBullet(Transform b, Vector3 from, Vector3 to, float dur, bool canRicochet)
    {
        float t = 0f;
        while (t < dur && b != null)
        {
            t += Time.deltaTime;
            b.position = Vector3.Lerp(from, to, t / dur);                   // 沿直线飞
            yield return null;
        }
        if (b != null) Destroy(b.gameObject);                               // 到了：消失
        if (canRicochet && Random.value < ricochetChance) Ricochet(to, to.x < from.x);   // 落地：有概率跳弹
    }

    // 跳弹：和第一种 NPC 共用一张图（NpcArt/Fx/Ricochet，尖 = 落点；往左飞时左右翻）
    void Ricochet(Vector3 at, bool left)
    {
        Sprite s = Resources.Load<Sprite>("NpcArt/Fx/Ricochet");
        if (s == null) return;
        SpriteRenderer sr = FxSprite("Ricochet", s, 60);
        sr.transform.position = at;
        sr.flipX = left;
        float k = flashSize;
        StartCoroutine(FxLife(sr, ricochetTime, new Vector3(k, k, 1f), new Vector3(k, k, 1f), 1f));
    }

    // 枪火：你画的两种（PistolFlash1 / 2）随机一种，放在开枪那一刻的枪口上，朝着那一刻的枪口方向；之后枪往上跳也不跟着动
    public void MuzzleFlash()
    {
        if (_muzzle == null) return;
        Sprite s = Load(Random.value < 0.5f ? "Weapons/PistolFlash1" : "Weapons/PistolFlash2");
        Vector3 at = _muzzle.position;                                      // 枪口
        Vector3 dir = _muzzle.TransformVector(Vector3.right);               // 枪口朝哪（含朝左翻转）
        dir.z = 0f;
        bool left = _muzzle.lossyScale.x < 0f;                              // 角色朝左（整个角色左右翻过来）
        float size = Mathf.Abs(_muzzle.lossyScale.y);                       // 角色本身的缩放
        float ang = dir.sqrMagnitude > 0.000001f ? Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg : (left ? 180f : 0f);
        if (s != null)
        {
            SpriteRenderer sr = FxSprite("MuzzleFlash", s, 55);             // 画在人上面、子弹下面
            sr.transform.position = at;
            sr.transform.rotation = Quaternion.Euler(0f, 0f, ang);
            float k = Mathf.Max(0.05f, flashSize) * size, fy = left ? -1f : 1f;   // 朝左：上下翻（配合转向 = 左右镜像）
            StartCoroutine(FxLife(sr, flashTime, new Vector3(k * 0.9f, k * 0.9f * fy, 1f), new Vector3(k * 1.05f, k * 1.05f * fy, 1f), 0.7f));
        }
        if (RenderMats.IsURP && muzzleLightIntensity > 0f) StartCoroutine(MuzzleLight(at));   // 火光照亮周围
    }

    // 开枪火光：枪口那一下把周围照亮，很快暗下去（和第一种 NPC 一样）
    IEnumerator MuzzleLight(Vector3 at)
    {
        UnityEngine.Rendering.Universal.Light2D l = WorldLighting.CreatePoint("MuzzleLight", null, muzzleLightColor, muzzleLightIntensity, muzzleLightRadius, 360f);
        if (l == null) yield break;
        WorldLighting.EnableShadows(l);                                     // 枪火也投影子
        l.transform.position = at;
        Destroy(l.gameObject, 1f);                                          // 保险：一定删掉
        float life = Mathf.Max(0.03f, flashTime * 1.3f), t = 0f;
        float peak = muzzleLightIntensity * RandIn(muzzleLightRandom);      // 这一枪有多亮（随机）
        while (l != null && t < life)
        {
            l.intensity = peak * (1f - t / life) * Random.Range(0.8f, 1f);  // 越来越暗，中间轻微跳动
            t += Time.deltaTime;
            yield return null;
        }
        if (l != null) Destroy(l.gameObject);
    }

    // 手枪手电筒：拿着手枪才亮；光从灯头出发，朝枪口方向照（2D 聚光朝自己的"上方"照）
    void UpdateGunLight()
    {
        bool on = gunLight && _held == "pistol" && gunLightIntensity > 0f;
        if (_gunLampGlow != null)
        {
            _gunLampGlow.enabled = on;
            Color c = gunLightColor; c.a = 1f;
            _gunLampGlow.color = c;
            _gunLampGlow.transform.localScale = Vector3.one * Mathf.Max(0.01f, gunLightGlowSize);
        }
        if (_gunBeam == null) return;
        _gunBeam.enabled = on;
        if (!on) return;
        WorldLighting.SetPoint(_gunBeam, gunLightColor, gunLightIntensity, gunLightRange, gunLightAngle);   // Inspector 里改了马上生效
        Vector3 dir = _muzzle.TransformVector(Vector3.right);               // 枪口朝哪（含朝左翻转、后坐上扬）
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _gunBeam.transform.position = _gunLamp.position;
        _gunBeam.transform.rotation = Quaternion.Euler(0f, 0f, ang - 90f);
    }

    // 一个发光圆点：叠加发光（找不到就用不受光材质），画在枪的所有部件上面，所以在黑暗里也一直亮
    SpriteRenderer GlowDot(Transform at)
    {
        if (_glowSprite == null) _glowSprite = GlowSprite();
        if (_additive == null)
        {
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive");   // 和第一种 NPC 的发光点同一种材质
            if (add != null) _additive = new Material(add);
        }
        SpriteRenderer sr = new GameObject("Glow").AddComponent<SpriteRenderer>();
        sr.transform.SetParent(at, false);
        sr.sprite = _glowSprite;
        sr.sortingOrder = 46;                                               // 持枪右手（44）、换弹右手（45）上面
        if (_additive != null) sr.sharedMaterial = _additive;
        else RenderMats.MakeUnlit(sr);
        sr.enabled = false;                                                 // 一开始空手：不亮
        return sr;
    }

    // 发光圆点图：中间亮、往外柔和变暗的白色圆（颜色由 SpriteRenderer 染；和第一种 NPC 一样）
    static Sprite GlowSprite()
    {
        const int n = 64;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2((n - 1) * 0.5f, (n - 1) * 0.5f)) / (n * 0.5f);   // 0 = 中心，1 = 边
                float core = Mathf.Clamp01(1f - d / 0.3f);                // 中间一小圈实心
                float halo = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);      // 外面一圈柔光
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(core + halo * 0.7f)));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 640f);
    }

    // 生成一个特效图（放在世界里，不挂在 NPC 身上）
    SpriteRenderer FxSprite(string name, Sprite sprite, int order)
    {
        SpriteRenderer sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        if (_oldSprite != null) sr.sortingLayerID = _oldSprite.sortingLayerID;   // 和人同一个排序层
        RenderMats.MakeUnlit(sr);                                           // 特效自己发亮，不受光
        return sr;
    }

    // 特效的一生：大小从 s0 变到 s1，然后按 pow 的快慢淡出
    IEnumerator FxLife(SpriteRenderer sr, float life, Vector3 s0, Vector3 s1, float pow)
    {
        life = Mathf.Max(0.02f, life);
        Destroy(sr.gameObject, life + 0.5f);                                // 保险：一定删掉
        float t = 0f;
        while (sr != null && t < life)
        {
            float k = t / life;                                             // 0 → 1
            float e = 1f - (1f - k) * (1f - k);                             // 先快后慢
            sr.transform.localScale = Vector3.Lerp(s0, s1, e);
            Color c = sr.color;
            c.a = Mathf.Pow(1f - k, pow);                                   // 淡出
            sr.color = c;
            t += Time.deltaTime;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    // 在范围里随机取一个数（两个数谁大谁小都行）
    static float RandIn(Vector2 r) { return Random.Range(Mathf.Min(r.x, r.y), Mathf.Max(r.x, r.y)); }

    // 后坐曲线：t = 开枪后过了几秒；rise 秒内从 0 冲到 1，然后按 ret 的快慢回到 0
    static float Kick(float t, float rise, float ret)
    {
        rise = Mathf.Max(0.001f, rise); ret = Mathf.Max(0.001f, ret);
        return t < rise ? t / rise : Mathf.Exp(-(t - rise) / ret);
    }

    // 二维向量转一个角度（度）
    static Vector2 Rot(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // 选中时在 Scene 窗口画标记（方便自己调）：黄点 = 持枪手绕着转的"肩膀"（Aim Shoulder），绿点 = 握把（持枪手的位置）
    void OnDrawGizmosSelected()
    {
        bool left = _rig != null && _rig.lossyScale.x < 0f;                 // 运行时朝左就左右翻
        Vector3 root = transform.position + new Vector3(0f, FEET_Y, 0f);    // 脚底中心
        System.Func<Vector2, Vector3> W = p => root + new Vector3(left ? -p.x : p.x, p.y, 0f);   // 本地 → 世界
        Vector2 grip = CanvasToLocal(P_GRIP - P_ROOT);
        Gizmos.color = Color.yellow; Gizmos.DrawSphere(W(aimShoulder), 0.012f);
        Gizmos.color = Color.green; Gizmos.DrawSphere(W(grip), 0.012f); Gizmos.DrawLine(W(aimShoulder), W(grip));
    }

    // ============================================================================
    // 手枪换弹（战术 / 快速 / 普通）：时间、姿势、参数照搬第一种 NPC；
    // 不同的是新弹匣从胸挂上的手枪弹匣袋拿（不是身后）：右手伸到弹匣袋，袋里的弹匣变到手里，胸挂上就少一个。
    // ============================================================================

    // 某种换弹一共要多少"标准时间"（和第一种 NPC 的算法一样；普通换弹多一段"从放旧弹匣的袋子移到拿新弹匣的袋子"）
    public float ReloadTotal(ReloadKind kind)
    {
        // 三种换弹插好新弹匣后，枪都再举着 Rl Stow Time 这么久（滑套复位、枪抖完）再放下，手感一样
        if (kind == ReloadKind.Normal)                                      // 普通：接旧弹匣 → 插回空袋 → 换到满袋 → 拔新的 → 插上 → 回握把
            return Mathf.Max(0.05f, rlLockTime + nrCatchTime + rlDropTime + rlStowTime + rlGrabTime + nrSwitchTime
                                  + rlBringTime + rlAlignTime + rlInsertTime + rlStowTime + rlReturnTime);
        if (kind == ReloadKind.Speed)                                       // 快速：旧弹匣丢掉，插好新的右手直接回握把
            return Mathf.Max(0.05f, rlLockTime + rlFetchTime + rlGrabTime + rlBringTime + qrDropTime
                                  + rlAlignTime + rlInsertTime + rlStowTime + rlReturnTime);
        return Mathf.Max(0.05f, rlLockTime + rlFetchTime + rlGrabTime + rlBringTime + rlDropTime
                              + rlAlignTime + rlInsertTime + rlStowTime + rlReturnTime);
    }

    // 外部调用：这种换弹要几秒（以后战斗系统用）
    public float ReloadSeconds(ReloadKind kind) { return ReloadTotal(kind); }

    // 正在换弹吗
    public bool IsReloading => _reloadT >= 0f;

    // 胸挂上还有几个满弹匣
    public int FullMagsOnRig { get { int n = 0; foreach (int s in _slotState) if (s == SLOT_FULL) n++; return n; } }

    // 外部调用：想用 want 这种换弹，实际能用哪种。战术 / 普通要把旧弹匣插回胸挂的空袋，没有空袋就只能快速换弹（丢掉旧弹匣）
    public ReloadKind ResolveReloadKind(ReloadKind want)
    {
        if (want != ReloadKind.Speed && FindSlot(SLOT_NONE) < 0) return ReloadKind.Speed;
        return want;
    }

    // 外部调用：拿枪时换一次弹匣。fitSeconds > 0：整段动画压缩 / 拉长到正好这么多秒；0 = 按调的时间 × Reload Speed 播（测试用）
    public void PlayReload(float fitSeconds, ReloadKind kind)
    {
        if (_held != "pistol") return;                                      // 没拿枪就不换
        if (FindSlot(SLOT_FULL) < 0) RefillMags();                          // 胸挂上没有满弹匣了：测试先自动补满（游戏规则之后接）
        kind = ResolveReloadKind(kind);                                     // 没有空袋放旧弹匣：改成快速换弹
        _takeSlot = FindSlot(SLOT_FULL);                                    // 从哪个袋子拿新弹匣（从最右边的满弹匣开始拿）
        _stowSlot = kind == ReloadKind.Speed ? -1 : FindSlot(SLOT_NONE);    // 旧弹匣插回哪个空袋（快速换弹直接丢掉）
        _reloadKind = kind;
        lastReloadKind = kind.ToString();                                   // Inspector 里显示这次实际用的是哪种
        _qrDropped = false;                                                 // 旧弹匣还在枪里
        _magTaken = false;                                                  // 新弹匣还在袋里
        _oldStowed = false;                                                 // 旧弹匣还没插回去
        _reloadRate = fitSeconds > 0f ? ReloadTotal(kind) / fitSeconds : Mathf.Max(0.05f, reloadSpeed);
        _reloadT = 0f;                                                      // 从头开始
        _recoilT = -1f;                                                     // 停掉后坐
    }

    // 换弹结束 / 被打断：所有东西放回原样
    void StopReload()
    {
        _reloadT = -1f;
        if (_rlHandSprite != null) _rlHandSprite.enabled = false;           // 换弹用的右手、两个弹匣藏起来
        if (_rlNewMag != null) _rlNewMag.enabled = false;
        if (_rlOldMag != null) _rlOldMag.enabled = false;
        bool p = _held == "pistol";
        if (_gunHandR != null) _gunHandR.enabled = p;                       // 枪上的右手、枪里的弹匣显示回来
        if (_gunMag != null) _gunMag.enabled = p;
        if (_slide != null) _slide.localPosition = _slideBase;              // 滑套复位
    }

    // ---------------- 胸挂弹匣袋：4 个袋子，每个是"空袋 / 满弹匣 / 空弹匣"之一（一共 5 个弹匣 = 枪里 1 个 + 胸挂 4 个）----------------

    // 按拿的顺序（从最右边开始）找第一个是 state 的袋子；没有返回 -1
    int FindSlot(int state)
    {
        foreach (int i in POUCH_ORDER) if (_slotState[i] == state) return i;
        return -1;
    }

    // 胸挂弹匣全部补满（测试用）
    void RefillMags()
    {
        for (int i = 0; i < 4; i++) _slotState[i] = SLOT_FULL;
        ApplySlots();
    }

    // 按每个袋子的状态显示 / 藏起胸挂上的弹匣（空弹匣插回去也显示，样子和满的一样）
    void ApplySlots()
    {
        string[] names = { "空袋", "满", "空弹匣" };
        string text = "";
        foreach (int i in POUCH_ORDER)                                      // 从右到左
        {
            if (_mountMags[i] != null) _mountMags[i].enabled = _slotState[i] != SLOT_NONE;
            text += (text.Length > 0 ? " / " : "") + names[_slotState[i]];
        }
        rigMags = text;                                                     // Inspector 里显示（从右到左）
    }

    // 右手在某个袋子插 / 拔弹匣的姿势（根节点本地坐标）：让手里的弹匣正好和袋子里的弹匣重合（胸挂晃它也跟着）
    //   magInHand / magTilt = 这个弹匣在手里的位置 / 歪多少（新弹匣正拿着；战术换弹的旧弹匣摆成 V 字）
    //   pos = 插到底时手在哪，outPos = 拔出来 / 准备插进去时手在哪，ang = 手转多少度
    void PouchPose(int slot, Vector2 magInHand, float magTilt, out Vector2 pos, out Vector2 outPos, out float ang)
    {
        Transform m = _mountMags[slot].transform;                           // 这个袋子里的弹匣（藏起来也照样能算位置）
        Vector3 bottom = _rig.InverseTransformPoint(m.TransformPoint(CanvasToLocal(P_PISTOL_MAG_BOTTOM[slot] - P_PISTOL_MAG[slot])));   // 它的底部
        Vector3 d = _rig.InverseTransformVector(m.TransformVector(Vector3.right));   // 它现在转了多少（含胸挂晃动、身体摇摆）
        float magAng = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + POUCH_MAG_TURN;   // 手里弹匣的图要转多少才和袋里的一样斜
        ang = magAng - magTilt;                                             // 弹匣在手里歪着的话，手要反过来转
        pos = new Vector2(bottom.x, bottom.y) - Rot(magInHand, ang);        // 手的位置 = 弹匣底部 - 弹匣在手里的位置
        float r = (magAng + MAG_AXIS) * Mathf.Deg2Rad;                      // 沿着弹匣的方向往外拔
        outPos = pos + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * rlPullOut;
    }

    // 0~1 平滑过渡（两头慢、中间快）
    static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // 某一段的进度：t 在 [a, a+len] 之间时从 0 走到 1
    static float Seg(float t, float a, float len) { return len <= 0f ? (t >= a ? 1f : 0f) : Mathf.Clamp01((t - a) / len); }

    // 抖一下：开始后 tau 秒的抖动量（-1~1，很快衰减）
    float Shake(float tau, float len)
    {
        if (tau < 0f || tau > len) return 0f;
        return Mathf.Sin(2f * Mathf.PI * rlShakeFreq * tau) * Mathf.Exp(-tau * 12f);
    }

    // 每帧：按换弹进度摆枪、滑套、右手和两个弹匣（坐标都是角色根节点的本地坐标）
    void AnimateReload(float dt)
    {
        _reloadT += dt * _reloadRate;                                       // 往前走
        float t = _reloadT;
        bool quick = _reloadKind == ReloadKind.Speed;                       // 快速换弹（右手不接旧弹匣，旧弹匣丢掉）
        bool normal = _reloadKind == ReloadKind.Normal;                     // 普通换弹（先插回旧弹匣，再拿新弹匣）
        float dropLen = quick ? qrDropTime : rlDropTime;                    // ⑤ 的长度
        // 每一段的起点（标准时间）
        float b1 = rlLockTime, b2 = b1 + rlFetchTime, b3 = b2 + rlGrabTime, b4 = b3 + rlBringTime;
        float b5 = b4 + dropLen, b6 = b5 + rlAlignTime, b7 = b6 + rlInsertTime, b8 = b7 + rlStowTime, b9 = b8 + rlReturnTime;
        float dropAt = quick ? Mathf.Max(0f, qrDropAt) : b4;                // 旧弹匣离开枪的时刻（快速：枪口一抬就掉；战术：⑤ 开始）
        // 普通换弹的时间点：c2 右手到握把下方，c3 旧弹匣在手里，c4 到空袋口，c4a 旧弹匣插好，c4b 到满弹匣的袋子，c5 拔出新弹匣
        float half = rlGrabTime * 0.5f;
        float c2 = b1 + nrCatchTime, c3 = c2 + rlDropTime, c4 = c3 + rlStowTime, c4a = c4 + half, c4b = c4a + nrSwitchTime, c5 = c4b + half;
        if (normal)                                                         // 普通换弹：后面几段的起点跟着往后挪
        {
            dropAt = c2;                                                    // 右手到了才弹出旧弹匣
            b5 = c5 + rlBringTime; b6 = b5 + rlAlignTime; b7 = b6 + rlInsertTime;
            b8 = b7 + rlStowTime;                                           // 插好后枪再举一会儿（滑套复位、枪抖，和战术换弹一样），右手同时回握把
            b9 = b8 + rlReturnTime;
        }
        if (t >= b9) { StopReload(); ApplyAimPose(); return; }              // 播完了：回到平时的样子

        // ---- 1. 枪：抬起 / 配合转 / 抖动 ----
        float raise = t < b8 ? Ease(t / Mathf.Max(0.001f, rlLockTime)) : 1f - Ease(Seg(t, b8, rlReturnTime));   // ① 抬起，⑩ 放下
        float align = t < b7 ? rlGunAlign * Ease(Seg(t, b5, rlAlignTime))                                       // ⑥ 转过去
                             : rlGunAlign * (1f - Ease(Seg(t, b7, rlReleaseTime)));                             // ⑧ 转回来
        float slideAt = b7 + Mathf.Max(0f, rlSlideDelay);                                                       // 滑套复位的时刻
        float shakeDrop = Shake(t - dropAt, rlDropTime);                                                        // 旧弹匣掉出来时：上下 + 旋转抖
        float shakeInsert = Shake(t - b7, rlReleaseTime);                                                       // ⑦ 新弹匣插到底：上下抖
        float shakeSlide = Shake(t - slideAt, rlReleaseTime);                                                   // ⑧ 滑套复位：旋转抖
        float aimNow = _aimAng * (1f - raise);                              // 瞄准角度：① 抬枪时转回正前方，⑩ 放下时转回去对准
        Vector2 gunPos = AimHand(aimNow, Vector2.zero) + rlGunRaise * raise + new Vector2(0f, rlShake * (shakeDrop + shakeInsert));
        float gunAng = aimNow + rlGunTilt * raise + align + rlShakeAngle * (shakeDrop + shakeSlide);  // 抬枪时的旋转 + ⑥ 配合转 + 旋转抖
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);

        // ---- 2. 滑套：① 空仓挂机，⑧ 复位 ----
        float slideK = t < slideAt ? Ease(t / Mathf.Max(0.001f, rlLockTime)) : 1f - Seg(t, slideAt, rlReleaseTime * 0.4f);   // 到复位时刻才弹回去
        if (_slide != null) _slide.localPosition = _slideBase + new Vector3(-slideKick * rlSlideLock * slideK / PPU_FULL, 0f, 0f);

        // ---- 3. 右手的几个关键姿势（位置 + 角度） ----
        Vector2 magIn = (Vector2)CanvasToLocal(P_GUN_MAG - P_GRIP);         // 枪里弹匣底部（枪的本地坐标）
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);   // 枪的本地坐标 → 根节点坐标
        Vector2 restPos = G2R(Vector2.zero); float restAng = gunAng;        // 握着握把（平时的位置）
        Vector2 oldInHand = normal ? rlMagInHand : rlMagInHand + rlOldOffset;   // 旧弹匣在手里的位置（战术：V 字；普通：正拿着）
        float oldTiltInHand = normal ? 0f : rlOldTilt;                      // 旧弹匣在手里歪多少
        PouchPose(_takeSlot, rlMagInHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);   // 拔新弹匣的满袋
        Vector2 stowPos = takePos, stowOut = takeOut; float stowAng = takeAng;
        if (_stowSlot >= 0) PouchPose(_stowSlot, oldInHand, oldTiltInHand, out stowPos, out stowOut, out stowAng);   // 插回旧弹匣的空袋
        float underAng = gunAng;                                            // ④ 握把下方：新弹匣竖直对着握把口
        Vector2 underPos = G2R(magIn - MAG_DIR * (rlReady + rlUnderGap)) - Rot(rlMagInHand, underAng);
        if (quick) underPos += Rot(qrWait, underAng);                       // 快速换弹：往旁边让开（默认不让）
        float tilt = quick || normal ? 0f : rlNewTilt;                      // 新弹匣在手里歪多少（只有战术换弹要摆 V 字）
        float readyAng = gunAng - tilt;                                     // ⑥⑦ 新弹匣歪着的角度抵消掉，正好对准
        float depth = t < b6 ? rlReady : Mathf.Lerp(rlReady, 0f, Ease(Seg(t, b6, rlInsertTime)));   // ⑦ 往里插
        Vector2 readyPos = G2R(magIn - MAG_DIR * depth) - Rot(rlMagInHand, readyAng);
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));   // 来回走时往上拱 / 往下兜

        Vector2 hp; float ha;                                               // 这一帧右手的位置 / 角度
        float newTilt = 0f;                                                 // 新弹匣在手里歪多少
        bool reloadHand = t >= b1;                                          // ② 开始右手离开枪
        bool showNew = t >= b2 && t < b7;                                   // ③ 在满袋拿到 ~ ⑦ 插进去
        float stowDone = b7 + rlStowTime * 0.7f;                            // 战术：旧弹匣到空袋口的时刻（之后 0.3 的时间往里插）
        bool showOld = !quick && t >= b4 && t < b8;                         // ⑤ 掉进手里 ~ ⑨ 插回空袋（快速换弹右手不接）
        if (normal)                                                         // ---- 普通换弹 ----
        {
            showOld = t >= dropAt && t < c4a;                               // 旧弹匣：弹出来 ~ 插回空袋
            showNew = t >= c4b && t < b7;                                   // 新弹匣：在满袋拿到 ~ 插进去
            if (t < b1) { hp = restPos; ha = restAng; }                     // ① 右手还握着握把
            else if (t < c2)                                                // ② 移到握把下方
            {
                float e = Ease(Seg(t, b1, nrCatchTime));
                hp = Vector2.Lerp(restPos, underPos, e); ha = Mathf.LerpAngle(restAng, underAng, e);
            }
            else if (t < c3) { hp = underPos; ha = underAng; }              // ③ 旧弹匣掉进右手
            else if (t < c4)                                                // ④ 带着旧弹匣到空袋口（走弧线）
            {
                float e = Ease(Seg(t, c3, rlStowTime));
                hp = Vector2.Lerp(underPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(underAng, stowAng, e);
            }
            else if (t < c4a)                                               // ⑤ 把旧弹匣插进空袋
            {
                hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, c4, half))); ha = stowAng;
            }
            else if (t < c4b)                                               // ⑥ 手移到满弹匣的袋子
            {
                float e = Ease(Seg(t, c4a, nrSwitchTime));
                hp = Vector2.Lerp(stowPos, takePos, e); ha = Mathf.LerpAngle(stowAng, takeAng, e);
            }
            else if (t < c5)                                                // ⑦ 拔出新弹匣
            {
                hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, c4b, half))); ha = takeAng;
            }
            else if (t < b5)                                                // ⑧ 拿着新弹匣到握把下方
            {
                float e = Ease(Seg(t, c5, rlBringTime));
                hp = Vector2.Lerp(takeOut, underPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, underAng, e);
            }
            else if (t < b6)                                                // ⑨ 和枪一起转，对准握把口
            {
                float e = Ease(Seg(t, b5, rlAlignTime));
                hp = Vector2.Lerp(underPos, readyPos, e); ha = Mathf.LerpAngle(underAng, readyAng, e);
            }
            else if (t < b7) { hp = readyPos; ha = readyAng; }              // ⑩ 插进去
            else                                                            // ⑪ 右手回到握把（枪还举着，滑套复位、抖完再一起放下）
            {
                float e = Ease(Seg(t, b7, rlReturnTime));
                hp = Vector2.Lerp(readyPos, restPos, e); ha = Mathf.LerpAngle(readyAng, restAng, e);
            }
        }
        else if (t < b1) { hp = restPos; ha = restAng; }
        else if (t < b2)                                                    // ② 伸到满袋（走弧线）
        {
            float e = Ease(Seg(t, b1, rlFetchTime));
            hp = Vector2.Lerp(restPos, takePos, e) + Arc(e); ha = Mathf.LerpAngle(restAng, takeAng, e);
        }
        else if (t < b3)                                                    // ③ 从袋里拔出新弹匣（沿弹匣方向往外拔）
        {
            hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, b2, rlGrabTime))); ha = takeAng;
        }
        else if (t < b4)                                                    // ④ 拿到握把下方
        {
            float e = Ease(Seg(t, b3, rlBringTime));
            hp = Vector2.Lerp(takeOut, underPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, underAng, e);
        }
        else if (t < b5)                                                    // ⑤ 战术：等旧弹匣掉进手里，新弹匣歪过去摆 V 字；快速：停一下（默认不停）
        {
            hp = underPos; ha = underAng;
            newTilt = tilt * Ease(Seg(t, b4, dropLen));
        }
        else if (t < b6)                                                    // ⑥ 和枪一起转，对准握把口
        {
            float e = Ease(Seg(t, b5, rlAlignTime));
            hp = Vector2.Lerp(underPos, readyPos, e); ha = Mathf.LerpAngle(underAng, readyAng, e);
            newTilt = tilt;
        }
        else if (t < b7) { hp = readyPos; ha = readyAng; newTilt = tilt; }  // ⑦ 插进去
        else if (quick)                                                     // 快速：插好后右手直接回握把（枪还举着，等滑套复位、抖完再放下）
        {
            float e = Ease(Seg(t, b7, rlReturnTime));
            hp = Vector2.Lerp(readyPos, restPos, e); ha = Mathf.LerpAngle(readyAng, restAng, e);
        }
        else if (t < stowDone)                                              // ⑨ 带着旧弹匣到空袋口
        {
            float e = Ease(Seg(t, b7, rlStowTime * 0.7f));
            hp = Vector2.Lerp(readyPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(readyAng, stowAng, e);
        }
        else if (t < b8)                                                    // ⑨ 把旧弹匣插进空袋
        {
            hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, stowDone, rlStowTime * 0.3f))); ha = stowAng;
        }
        else                                                                // ⑩ 回到握把
        {
            float e = Ease(Seg(t, b8, rlReturnTime));
            hp = Vector2.Lerp(stowPos, restPos, e) + Arc(e); ha = Mathf.LerpAngle(stowAng, restAng, e);
        }

        // 胸挂上的弹匣：新弹匣一到手里，那个袋子就空了；旧弹匣插到底，那个空袋就有了一个空弹匣
        if (showNew && !_magTaken) { _magTaken = true; _slotState[_takeSlot] = SLOT_NONE; ApplySlots(); }
        bool stowedNow = _stowSlot >= 0 && t >= (normal ? c4a : b8 - 0.0001f);
        if (stowedNow && !_oldStowed) { _oldStowed = true; _slotState[_stowSlot] = SLOT_EMPTY; ApplySlots(); showOld = false; }

        // ---- 4. 摆右手和两个弹匣（右手在胸挂前面拿 / 放，不用绕到身后；图层：旧弹匣 38 < 新弹匣 39 < 枪里的弹匣 40 … < 右手 45）----
        if (_gunHandR != null) _gunHandR.enabled = !reloadHand;             // 枪上的右手 ↔ 换弹用的右手
        _rlHandSprite.enabled = reloadHand;
        _rlHand.localPosition = new Vector3(hp.x, hp.y, 0f);
        _rlHand.localRotation = Quaternion.Euler(0f, 0f, ha);

        _rlNewMag.enabled = showNew;                                        // 新弹匣：底部在手里
        Vector2 nb = hp + Rot(rlMagInHand, ha);
        _rlNewMag.transform.localPosition = new Vector3(nb.x, nb.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha + newTilt);
        if (_gunMag != null) _gunMag.enabled = !(t >= dropAt && t < b7);    // 旧弹匣离开后枪里没弹匣，插好了再显示

        _rlOldMag.enabled = showOld && !_oldStowed;                         // 旧弹匣（插回袋里以后就是袋里那个了）
        if (_rlOldMag.enabled)
        {
            Vector2 heldPos = hp + Rot(oldInHand, ha);
            float heldAng = ha + oldTiltInHand;
            if (t < dropAt + rlDropTime)                                    // 从枪里掉进手里
            {
                float e = Ease(Seg(t, dropAt, rlDropTime));
                Vector2 op = Vector2.Lerp(G2R(magIn), heldPos, e);
                _rlOldMag.transform.localPosition = new Vector3(op.x, op.y, 0f);
                _rlOldMag.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(gunAng, heldAng, e));
            }
            else
            {
                _rlOldMag.transform.localPosition = new Vector3(heldPos.x, heldPos.y, 0f);
                _rlOldMag.transform.localRotation = Quaternion.Euler(0f, 0f, heldAng);
            }
        }

        if (quick && t >= dropAt && !_qrDropped)                            // 快速换弹：旧弹匣顺着握把滑出来（枪口往上抬的同时），丢掉
        {
            float tau = (t - dropAt) / Mathf.Max(0.05f, _reloadRate);        // 滑了几秒（真实时间）
            float v = qrSlideSpeed + qrGravity * tau;                       // 越滑越快
            float s = qrSlideSpeed * tau + 0.5f * qrGravity * tau * tau;    // 滑出来多远
            float magLen = _rlOldMag.sprite != null ? _rlOldMag.sprite.bounds.max.y : 0.107f;   // 弹匣底部到顶端多长
            Vector2 op = G2R(magIn - MAG_DIR * s);                          // 弹匣底部现在的位置
            if (s < magLen)                                                 // 还没整个滑出来：跟着枪走
            {
                _rlOldMag.enabled = true;
                _rlOldMag.transform.localPosition = new Vector3(op.x, op.y, 0f);
                _rlOldMag.transform.localRotation = Quaternion.Euler(0f, 0f, gunAng);
            }
            else                                                            // 整个滑出来了：交给它自己往下掉
            {
                _rlOldMag.enabled = false;
                _qrDropped = true;
                Vector2 vel = Rot(-MAG_DIR, gunAng) * v + new Vector2(RandIn(qrKick), 0f);   // 顺着握把的方向 + 往前 / 往后飘一点
                DropMag(op, gunAng, vel, RandIn(qrSpin));
            }
        }
    }

    // 快速换弹：旧弹匣脱手，自己往下掉（空中时挂在角色下面，跟着人走）
    void DropMag(Vector2 pos, float ang, Vector2 vel, float spin)
    {
        SpriteRenderer sr = new GameObject("DroppedMag").AddComponent<SpriteRenderer>();
        sr.sprite = Load("Weapons/PistolMag");
        sr.transform.SetParent(_rig, false);
        sr.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
        sr.sortingOrder = 38;                                               // 在枪和右手后面，身体前面
        Destroy(sr.gameObject, qrGroundTime + 3f);                          // 保险：中途被打断也一定会删掉
        StartCoroutine(FallMag(sr, pos, ang, vel, spin, RandIn(qrGroundY)));
    }

    // 旧弹匣往下掉 → 落地躺平 → 留在原地（不再跟着人走）→ 过一会儿淡出消失（和第一种 NPC 一样）
    IEnumerator FallMag(SpriteRenderer sr, Vector2 pos, float ang, Vector2 vel, float spin, float groundY)
    {
        while (sr != null && pos.y > groundY)                               // 还在空中
        {
            float dt = Time.deltaTime;
            vel.y -= qrGravity * dt;                                        // 重力
            pos += vel * dt;
            ang += spin * dt;                                               // 边掉边转
            sr.transform.localPosition = new Vector3(pos.x, Mathf.Max(pos.y, groundY), 0f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
            yield return null;
        }
        if (sr == null) yield break;
        float lie = Mathf.DeltaAngle(0f, ang) >= 0f ? 90f : -90f;           // 落地：转到最近的横躺角度
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, lie + Random.Range(-12f, 12f));   // 再随机歪一点
        sr.transform.SetParent(null, true);                                 // 离开角色：留在地上
        if (_oldSprite != null) sr.sortingLayerID = _oldSprite.sortingLayerID;
        sr.sortingOrder = -1;                                               // 地面层：画在人和建筑下面
        float t = 0f;
        while (sr != null && t < qrGroundTime + 0.5f)                       // 躺着，最后 0.5 秒淡出
        {
            t += Time.deltaTime;
            Color c = sr.color;
            c.a = 1f - Mathf.Clamp01((t - qrGroundTime) / 0.5f);
            sr.color = c;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    // 换弹的种类（Inspector 里显示中文；ABA 武装人员不用颤抖换弹）
    public enum ReloadKind
    {
        [InspectorName("战术换弹")] Tactical,
        [InspectorName("快速换弹")] Speed,
        [InspectorName("普通换弹")] Normal,
    }

    // 测试用的选项（Inspector 里显示中文）
    public enum TestHeld
    {
        [InspectorName("不测试")] None,
        [InspectorName("手枪")] Pistol,
    }
}
