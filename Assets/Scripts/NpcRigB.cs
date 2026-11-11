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
//   【步枪开火】Test Held 选"步枪"：连发（Rifle Burst 枪），每枪枪口上扬 + 往后顶 + 拉栓组后弹 + 枪火 + 子弹，连发时后坐接着往上叠；
//     步枪绕枪托（Rifle Shoulder）转。参数都在 Inspector "步枪开火"一栏。
//   【步枪换弹】和手枪一样三种、一样的规则（5 个弹匣 = 枪里 1 + 胸挂 4，第一次只能快速换弹），前手从胸挂步枪弹匣袋拿弹匣。
//     步枪快速换弹也是单独一套（AR 式：枪托先往下、整把枪一沉旧弹匣掉下去，同时右手拿新弹匣 → 抬枪 30° → 直接插上、上下抖 → 右手抓回 → 放大抖动 → 放平抖一下）。
//     步枪普通换弹也是单独一套（先拔下旧弹匣 → 抬枪 80° 同时插回胸挂 → 拿新弹匣直接插上 → 装好后同战术换弹）。
//     步枪战术换弹是单独一套动作（抬枪 → 拿新抓旧 → 插新 → 旧的插回去同时降到 45° → 放大抖动 + 拉栓弹回 → 放平的同时右手抓回护木、抖一下）。
//   【放低持枪】手枪、步枪没接敌（不瞄准、不开枪）时枪口朝下（和第一种 NPC 一样），开枪 / 瞄准时举平。
//   【战术匕首】Test Held 选"战术匕首"：激光刃弹出 → 像开灯泡一样闪亮（稍微过曝再回到正常），有真正的光照；挥动和第一种 NPC 的斧头一样。
//   【步枪取出】拿出步枪时：右手从胸挂抓枪上边 → 拿到身体中间 → 左手拉开枪托 → 左手回握把 → 右手松开（枪口一沉）回护木 → 打开步枪手电。
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
    public TestHeld testHeld = TestHeld.None;                 // 强制拿什么（手枪 / 步枪 / 战术匕首）
    public bool testLoop = true;                              // 每 1.4 秒自动开一次（手枪一枪，步枪连发）
    public bool testFire;                                     // 勾一下：开一次（手枪一枪，步枪连发 Rifle Burst 枪；播完自动取消勾选）
    public Transform testAimTarget;                           // 拖一个物体进来：枪口一直对准它（转过头会翻身），子弹往它打
    public bool testReload;                                   // 勾一下：换一次弹匣（按下面选的种类；手枪 / 步枪都行）
    public ReloadKind testReloadKind = ReloadKind.Tactical;   // 测试换弹用哪一种
    public string currentState = "";                          // （只读）现在是站 / 走 / 跑，速度多少

    [Header("受击 / 死亡测试（勾 Test Hit 挨一下，勾 Test Death 倒下，勾 Test Revive 站起来）")]
    public bool testHit;                                      // 勾一下：挨一下打（自动取消勾选）
    public bool testDeath;                                    // 勾一下：中弹倒地
    public bool testRevive;                                   // 勾一下：站起来（重新测；掉在地上的武器会收走）
    public HitFrom testHitFrom = HitFrom.Front;               // 测试时从哪边打过来
    public float testHitStrength = 1f;                        // 测试时这一下多重（1 = 正常，2 = 很重的一下）
    [Header("受击：被打中时身体吃到冲击的惯性（从前面打 = 往后仰，从背后打 = 往前栽）")]
    public float hitLean = 9f;                                // 身体被打得仰 / 栽多少度
    public float hitShove = 0.03f;                            // 整个人被推开多远（单位；之后自己站回原位）
    public float hitBuckle = 0.012f;                          // 身体被打得往下一沉（单位）
    public float hitFreq = 3.2f;                              // 回正的快慢（越大回得越快）
    [Range(0.05f, 1f)] public float hitDamp = 0.4f;           // 回正时的阻尼（越小来回晃得越多）
    public float hitGunJolt = 0.6f;                           // 手上的东西被震歪的程度（身体仰的角度再乘这个数）
    public float hitHeadWhip = 1f;                            // 头被甩的程度
    public float hitGearKick = 1f;                            // 受击 / 倒地时身上挂件被甩的程度
    [Header("死亡：中弹的冲击 + 重力，朝子弹飞的方向倒下")]
    public float deathImpulse = 37f;                          // 中弹冲击：开始倒的速度（度/秒；越大倒得越猛；原来 110，减弱到 1/3）
    public float deathGravity = 0.6f;                         // 重力（越大后半段摔得越快）
    public float deathCOM = 0.35f;                            // 重心高度（单位；越高前半段倒得越慢、越"沉"）
    public float deathSink = 0.05f;                           // 腿一软，身体先往下沉多少（单位）
    public float deathSinkTime = 0.2f;                        // 往下沉用几秒
    public float deathSlide = 0.027f;                         // 倒的时候整个人被冲击带着滑多远（单位，往倒的方向；原来 0.08）
    [Range(0f, 0.9f)] public float deathBounce = 0.25f;       // 摔到地上弹起来的程度（0 = 不弹）
    public float deathLieAngle = 88f;                         // 躺平时的角度（度）
    public float deathGroundLift = 0.12f;                    // 躺平后离地多高（防止身体 / 背包陷进地里；单位）
    public float deathArmFlop = 1f;                           // 手臂软了跟着甩的程度
    [Header("死亡：武器脱手")]
    public float weaponDropAt = 25f;                          // 倒到多少度时武器脱手
    public float weaponThrow = 0.33f;                         // 脱手时往倒的方向飞多快（单位/秒；原来 1）
    public float weaponUp = 0.33f;                            // 脱手时往上抛多快（单位/秒；原来 1）
    [Range(0f, 1f)] public float weaponCarry = 0.35f;         // 武器带走多少手当时的速度（人倒下时手在甩；越小飞得越近）
    public Vector2 weaponSpin = new Vector2(200f, 500f);      // 飞的时候转多快（度/秒，在范围里随机）
    public float weaponGravity = 1f;                          // 武器下落的重力倍率
    [Range(0f, 0.9f)] public float weaponBounce = 0.3f;       // 落地弹起来的程度
    public float weaponRestLift = 0.02f;                      // 落地后离地多高（单位）
    [Header("着弹点效果（图在 NpcArt/Fx/HitFlesh1~3、HitMetal1~3；子弹没打中落到地上用金属那组）")]
    public bool impactFx = true;                              // 开 / 关着弹点效果
    public float impactScale = 1f;                            // 效果大小
    public float impactHeight = 0.2f;                         // 测试时中弹的位置：腰往上多高（单位，大约胸口）
    [Range(0f, 1f)] public float fleshChance = 0.6f;           // 打中时出血的概率（其余是打在装甲 / 装备上的火花）
    public float fleshLife = 0.45f;                           // 血雾持续多久（秒）
    public float metalLife = 0.12f;                           // 火花持续多久（秒）
    public Color metalLightColor = new Color(1f, 0.8f, 0.55f);   // 火花照亮周围的颜色
    public float metalLight = 0.8f;                           // 火花照亮周围的亮度（微弱；0 = 不照亮）
    public float metalLightRadius = 0.6f;                     // 火花照多远（单位）

    [Header("持枪 / 开枪（ABA 战斗手枪：你调过的手感）")]
    public Vector2 recoilKick = new Vector2(50f, 50f);        // 开枪时枪口上扬多少度：每枪在这个范围里随机（最小 / 最大）
    public float slideKick = 150f;                            // 开枪时滑套（枪套）往后滑多远（原图像素）
    public float slideBackTime = 0.03f;                       // 滑套往后滑用几秒
    public float slideHoldTime = 0.03f;                       // 滑到最后面停多久（秒）
    public float slideReturnTime = 0.15f;                     // 滑回原位用几秒
    public float bulletSpeed = 100f;                          // 子弹飞多快（单位 / 秒）
    public float flashTime = 0.07f;                           // 枪火亮多久（秒）
    public float flashSize = 1f;                              // 枪火多大（倍数；1 = 按 PSD 画的大小）
    [Range(0f, 1f)] public float ricochetChance = 0.274f;     // 子弹没打中、落到地上时出跳弹效果的概率
    public float ricochetTime = 0.15f;                        // 跳弹效果显示多久（秒）
    public Vector2 recoilHandX = new Vector2(-0.05f, -0.1f);  // 开枪时手往后顶多远：每枪随机（两个数之间）
    public Vector2 recoilHandY = new Vector2(0.09f, 0.18f);   // 开枪时手往上抬多高：每枪随机（两个数之间）
    public float handRecoilRise = 0.03f;                      // 手被顶回去用几秒（越小越猛）
    public Vector2 handRecoilReturn = new Vector2(0.09f, 0.24f);   // 手回正的快慢（越大回得越慢）：每枪随机
    public Vector2 muzzleRise = new Vector2(0.09f, 0.15f);    // 枪口上扬用几秒：每枪随机
    public float muzzleReturn = 0.09f;                        // 枪口回正的快慢（越大回得越慢）
    public float recoilLean = 3f;                             // 开枪时身体被震得往后仰（度）
    public float actionShift = 0.012f;                        // 身体跟着往后挪多远（单位）
    public Color muzzleLightColor = new Color(1f, 0.72f, 0.38f);   // 开枪时火光照亮周围的颜色
    public float muzzleLightIntensity = 5f;                   // 开枪火光亮度（原来 2.5，加强了）
    public float muzzleLightRadius = 3.2f;                    // 开枪火光照多远（单位；原来 2.2）
    public Vector2 muzzleLightRandom = new Vector2(0.7f, 1.3f);   // 每一枪火光亮度的倍数（最暗原来 0.4，提到 0.7）：在这个范围里随机（每枪亮度都不一样，闪的过程中也会轻微跳动）
    public Vector2 aimShoulder = new Vector2(0.175f, 0.466f); // 持枪手绕着转的"肩膀"（相对脚底中心，单位；选中 NPC 时 Scene 里的黄点）
    [Header("子弹曳光（所有子弹都有：子弹自己微微发光 + 照亮周围一点点）")]
    public bool tracer = true;                                // 开 / 关曳光
    public Color tracerColor = new Color(1f, 0.75f, 0.42f);   // 曳光颜色（暖橙，和枪火一个色调）
    public float tracerLight = 0.3f;                          // 照亮周围的亮度（微弱；0 = 只发光不照亮）
    public float tracerRadius = 0.6f;                         // 照多远（单位）
    public float tracerGlow = 0.45f;                          // 子弹本身光晕的亮度（0 = 不要光晕）
    public Vector2 tracerGlowSize = new Vector2(0.5f, 0.08f); // 光晕拖尾的长 / 宽（单位；拖在弹头后面）
    [Header("步枪开火（先给了初始值，手感你来调；时间 = 秒，距离 = 单位，角度 = 度）")]
    public int rifleBurst = 10;                               // 测试时一次连发几枪（Test Fire / Test Loop）
    public float rifleRPM = 400f;                             // 射速（发 / 分钟）：400 = 每 0.15 秒一枪（真实突击步枪一般 600~900，400 看得更清楚）
    public Vector2 rifleRecoilKick = new Vector2(5.28f, 1.65f);   // 每枪枪口上扬多少度：随机（两个数之间）
    public Vector2 rifleRecoilHandX = new Vector2(-0.06f, -0.035f);   // 每枪枪往后顶多远（沿枪的方向）：随机
    public Vector2 rifleRecoilHandY = new Vector2(0.005f, 0.02f);     // 每枪枪往上抬多高：随机
    public float rifleHandRecoilRise = 0.02f;                 // 枪被顶回去用几秒（越小越猛）
    public Vector2 rifleHandRecoilReturn = new Vector2(0.06f, 0.09f); // 枪回正的快慢（越大回得越慢）：随机
    public Vector2 rifleMuzzleRise = new Vector2(0.02f, 0.04f);       // 枪口上扬用几秒：随机
    public float rifleMuzzleReturn = 0.07f;                   // 枪口回正的快慢（越大回得越慢）
    public float rifleRecoilLean = 1.5f;                      // 每枪身体往后仰多少度
    public float rifleActionShift = 0.01f;                    // 每枪身体往后挪多远
    public float rifleChargerKick = 120f;                     // 每枪"步枪上弹器"往后弹多远（原图像素；0 = 不动）
    public float rifleBoltKick = 0f;                          // 每枪"步枪拉栓"往后弹多远（原图像素；0 = 不动）
    public float rifleBoltBackTime = 0.02f;                   // 拉栓组往后弹用几秒
    public float rifleBoltHoldTime = 0f;                      // 在最后面停多久
    public float rifleBoltReturnTime = 0.05f;                 // 弹回原位用几秒
    public float rifleFlashTime = 0.05f;                      // 步枪枪火亮多久
    public float rifleFlashSize = 2f;                         // 步枪枪火多大（倍数；你在测试场景调成了 2）
    public Vector2 rifleShoulder = new Vector2(-0.083f, 0.51f);   // 步枪绕着转的点（枪托顶在肩上的位置，相对脚底中心；选中 NPC 时 Scene 里的青点）

    [Header("步枪弹匣（三种步枪换弹共用）")]
    public string rigRifleMags = "";                          // （只读）胸挂 4 个步枪弹匣袋，从右到左：满 / 空弹匣 / 空袋。一共 5 个 = 枪里 1 个 + 胸挂 4 个
    public Vector2 rrMagInHand = new Vector2(0f, -0.04f);     // 步枪弹匣底部在手里的位置（y 越小手抓得越深；原来 0.03 太浅，像指尖捏着）
    public float rrReady = 0.1f;                              // 对准时弹匣还差多远插到底
    public float rrPullOut = 0.1f;                            // 从胸挂弹匣袋往外拔多远（步枪弹匣长，拔得多）

    [Header("步枪战术换弹（单独一套；时间 = 秒，不受 Rifle Reload Speed 影响）")]
    public float rtRaiseTime = 0.3f;                          // ① 左手连枪往后收、枪口往上抬用几秒
    public float rtGunUp = 40f;                               // ① 枪口往上抬多少度
    public Vector2 rtGunBack = new Vector2(-0.3f, 0.03f);     // ① 枪往后（x 负）/ 往上收多少（单位）
    public float rtFetchTime = 0.3f;                          // ① 右手伸到胸挂（和抬枪同时开始）
    public float rtGrabTime = 0.2f;                           // ① 从袋里拔出新弹匣
    public float rtBringTime = 0.25f;                         // ② 拿着新弹匣去抓旧弹匣
    public float rtPullOldTime = 0.15f;                       // ② 拔出旧弹匣
    public float rtPullOldDist = 0.17f;                       // ② 旧弹匣拔出来多远（单位；步枪弹匣约 0.16 长）
    public float rtAlignTime = 0.12f;                         // ③ 新弹匣对准弹匣口
    public float rtInsertTime = 0.1f;                         // ③ 插入新弹匣
    public float rtInsertShake = 8f;                          // ③ 插到底时枪转着抖多少度
    public float rtNewTilt = 15f;                             // 新旧弹匣并排拿着时，新弹匣歪多少度
    public float rtOldTilt = -15f;                            // 并排时旧弹匣歪多少度
    public Vector2 rtOldOffset = new Vector2(0.04f, 0f);      // 并排时旧弹匣在新弹匣旁边多远（手的方向）
    public float rtStowTime = 0.35f;                          // ④ 带着旧弹匣到空袋口（同时枪口往下）
    public float rtPushTime = 0.12f;                          // ④ 把旧弹匣插进袋里
    public float rtGunMid = 45f;                              // ④ 枪口往下到多少度
    public float rtZoom = 0.12f;                              // ⑤ 放大抖动：突然放大多少（0.12 = 大 12%）
    public float rtZoomTime = 0.25f;                          // ⑤ 放大抖动多久
    public float rtZoomShake = 6f;                            // ⑤ 放大抖动时顺便转着抖多少度
    public float rtBoltReturnTime = 0.05f;                    // ⑤ 拉栓组弹回去用几秒（和放大抖动同时）
    public float rtLevelTime = 0.25f;                         // ⑥ 枪口从 45° 放平用几秒（右手插回旧弹匣后马上往回走，枪放平的那一刻正好抓住护木）
    public float rtGripShake = 6f;                            // ⑥ 右手抓住时枪身转着抖多少度
    public float rtGripShakeY = 0.01f;                        // ⑥ 右手抓住时枪身上下抖多少（单位）
    public float rtGripShakeTime = 0.25f;                     // ⑥ 抓住后抖多久

    [Header("步枪快速换弹（单独一套；拔 / 送 / 插入和后半段用上面战术换弹的参数）")]
    public float rsFlickTime = 0.18f;                         // ① 按弹匣释放、往下一抖用几秒：枪托先往下，再带着整把枪往下沉（沉到底时旧弹匣掉下去）
    public float rsFlickY = 0.04f;                            // ① 整把枪往下沉多少（单位）
    public float rsFlickAngle = 8f;                           // ① 枪托往下压多少度（绕握把转，枪口会往上翘）
    public float rsRaiseTime = 0.3f;                          // ② 抖完后枪口抬起来、往后收用几秒
    public float rsGunUp = 30f;                               // ② 枪口往上抬多少度
    public Vector2 rsGunBack = new Vector2(-0.3f, 0.03f);     // ② 枪往后（x 负）/ 往上收多少（单位）
    public float rsInsertShakeY = 0.04f;                      // ③ 插到底时枪上下抖多少（单位；力量感）
    public float rsInsertShakeTime = 0.2f;                    // ③ 上下抖多久
    public float rsRegripTime = 0.12f;                        // ③ 插完右手抓回护木用几秒
    public float rsSettleTime = 0.3f;                         // ③ 插到底后过多久开始放大抖动 + 拉栓组弹回（让上下抖先抖完）

    [Header("步枪普通换弹（单独一套；拔 / 送 / 插和装好后的动作用上面战术换弹的参数）")]
    public float rnReachTime = 0.2f;                          // ① 右手从护木伸过去抓住旧弹匣用几秒
    public float rnRaiseTime = 0.3f;                          // ② 拔下旧弹匣后枪口抬起来、往后收用几秒（同时右手去插回旧弹匣）
    public float rnGunUp = 80f;                               // ② 枪口往上抬多少度
    public Vector2 rnGunBack = new Vector2(-0.3f, 0.03f);     // ② 枪往后（x 负）/ 往上收多少（单位）

    [Header("战术匕首（激光刃；挥动和第一种 NPC 的斧头一样）")]
    public float daggerPopTime = 0.12f;                       // 拿出来时刀刃上半段弹出用几秒
    public float daggerPopDist = 230f;                        // 刀刃上半段从多深的地方弹出来（原图像素，沿刀身方向）
    public float daggerFlickerTime = 0.6f;                    // 弹出后激光像开灯泡一样闪多久（最后从过曝降到正常）
    [Range(1f, 2f)] public float daggerOverexpose = 1.5f;     // 闪动时最亮那一下有多亮（1 = 正常，越大越过曝）
    public float daggerHum = 0.06f;                           // 亮好以后激光轻轻嗡嗡抖的幅度（0 = 不抖）
    public Color daggerLightColor = new Color(0.55f, 0.95f, 1f);   // 激光的颜色（亮光图层 + 照亮周围的光）
    public float daggerLightIntensity = 1.2f;                 // 激光照亮周围的亮度（正常亮度时）
    public float daggerLightRadius = 1.5f;                    // 激光照多远（单位）
    public float daggerPeriod = 1.2f;                         // 挥一下（含停顿）要几秒
    public Vector2 daggerAngles = new Vector2(90f, -37.9f);   // 抬起的角度 / 挥下的角度（度）
    public Vector2 daggerHandUp = new Vector2(-0.14f, 0.35f); // 抬起时手往哪挪（往后、往上）
    public Vector2 daggerHandDown = new Vector2(0.32f, -0.02f);   // 挥下时手往哪挪（往前；y 用下面的随机范围）
    public Vector2 daggerHandDownY = new Vector2(-0.09f, 0.15f);  // 挥下时手的 y：每挥一下在这个范围里随机
    public Vector2 daggerLean = new Vector2(5f, -7f);         // 身体跟着晃：抬起时后仰 / 挥下时前倾（度）
    public float daggerShift = 0.012f;                        // 身体跟着前后挪多远（单位）

    [Header("步枪取出动画（Test Held 换成步枪时播放）")]
    public bool rifleDrawAnim = true;                         // 拿出步枪时播不播取出动画（不播 = 直接拿在手上）
    public float rdReachTime = 0.3f;                          // ① 右手伸过去抓胸挂步枪的上边
    public Vector2 rdGrabPoint = new Vector2(2100f, 745f);    // ① 右手（手心）抓步枪时在哪（武器 PSD 原图坐标；默认 = 枪的前半段（护木）上边再往上一点，让手指正好碰到枪身）
    public float rdGrabAngle = 180f;                          // ① 右手抓枪时相对枪转多少度：180 = 手倒过来，手指（那个小突起）朝下对准枪身（握护木时手指朝上对着枪）
    public float rdLiftTime = 0.35f;                          // ② 右手抓着枪拿到身体中间
    public Vector2 rdCenterGrip = new Vector2(1060f, 1150f);  // ② 拿到身体中间时握把在哪（角色 PSD 原图坐标）
    public float rdCenterAngle = 15f;                         // ② 拿到身体中间时枪口朝上多少度
    public float rdStockGrabTime = 0.2f;                      // ③ 左手伸到枪托后侧
    public Vector2 rdStockGrabPoint = new Vector2(1000f, 1010f);  // ③ 左手抓枪托的哪里（武器 PSD 原图坐标）
    public float rdStockCollapse = 120f;                      // ③ 枪托一开始收起来多少（往前推了多少原图像素；拉开时拉回来）
    public float rdStockPullTime = 0.15f;                     // ③ 往后拉开枪托
    public float rdStockShake = 5f;                           // ③ 枪托拉开时枪震多少度
    public float rdLeftHomeTime = 0.2f;                       // ④ 左手回到握把
    public float rdRightHomeTime = 0.3f;                      // ⑤ 右手松开、回到护木（同时枪口往下一沉）
    public float rdDropAngle = 30f;                           // ⑤ 右手松开时枪口往下沉多少度（重力）
    public float rdSettleTime = 0.3f;                         // ⑥ 回到平时持枪的样子

    [Header("枪挂载手电筒（手枪 / 步枪枪口下方那个小突起；参数两把枪共用）")]
    public bool gunLight = true;                              // 拿着枪时手电筒开不开（步枪：取出动画播完才打开）
    public Color gunLightColor = new Color(0.85f, 0.93f, 1f); // 光的颜色（冷白）
    public float gunLightIntensity = 1f;                      // 亮度
    public float gunLightRange = 5f;                          // 照多远（单位）
    public float gunLightAngle = 35f;                         // 光束多宽（度）
    public float gunLightGlowSize = 0.6f;                     // 灯头本身发光点多大（倍数）
    public float aimTurnSpeed = 720f;                         // 瞄准时枪口转多快（度 / 秒）
    public float aimFlipOver = 5f;                            // 枪口转过正上 / 正下方再多几度才翻身（防止来回翻）
    public float lowReadyAngle = -45f;                        // 没接敌、不瞄准时，枪口朝下多少度（手枪、步枪都用；和第一种 NPC 一样）
    public float lowReadySpeed = 180f;                        // 放下枪有多快（度 / 秒；举枪瞄准用 Aim Turn Speed）
    public float lowReadyHold = 1.5f;                         // 开完枪多久之后才放下枪（秒）
    public Vector2 lowReadyOffset = new Vector2(0f, -0.08f);  // 枪口朝下时整只手（连枪）再往哪挪（x 往前，y 往上；单位）

    [Header("换弹 1：战术换弹（参数和第一种 NPC 一样；时间 = 秒，距离 = 单位，角度 = 度）")]
    public string rigMags = "";                               // （只读）胸挂 4 个手枪弹匣袋，从右到左：满 / 空弹匣（插回去的旧弹匣）/ 空袋。一共 5 个弹匣 = 枪里 1 个 + 胸挂 4 个
    public string lastReloadKind = "";                        // （只读）上一次实际用的换弹（没有空袋放旧弹匣时，战术 / 普通会自动改成快速）
    public bool testRefillMags;                               // 勾一下：胸挂弹匣（手枪 + 步枪）全部补满（满弹匣用完时也会自动补满，测试用）
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
    // 步枪（武器补充 PSD）：整把枪 + 两只持枪手绕握把（左手握着的手枪式握把）转，握把绕"肩膀"（枪托）转
    static readonly Vector2 P_RIFLE_GRIP = new Vector2(1430, 1135);   // 握把 = 步枪持枪左手中心
    static readonly Vector2 P_RIFLE_MUZZLE = new Vector2(2444, 892);  // 步枪枪口
    static readonly Vector2 P_R_CHARGER = new Vector2(1768, 908), P_R_BOLT = new Vector2(1526, 908), P_R_MAG = new Vector2(1860, 1343);   // 步枪弹匣：轴心在弹匣底部
    static readonly Vector2 R_MAG_DIR = new Vector2(-0.102f, 0.995f); // 步枪弹匣插进去的方向（枪的本地坐标，沿弹匣往上；PSD 量的）
    const float R_MAG_AXIS = 95.9f;                                   // 步枪弹匣图本身斜的角度（度）
    const float R_POUCH_MAG_TURN = 96.8f - 95.9f;                     // 胸挂上的步枪弹匣比枪里的多斜多少度
    static readonly Vector2[] P_RIFLE_MAG_BOTTOM = { new Vector2(950, 1451), new Vector2(1153, 1451), new Vector2(1348, 1455), new Vector2(1517, 1455) };   // 胸挂 4 个步枪弹匣的底部
    static readonly int[] R_POUCH_ORDER = { 3, 2, 1, 0 };             // 步枪弹匣从哪个袋子开始拿（从最右边：弹匣 4 → 3 → 2 → 1）
    static readonly Vector2 P_R_INNER = new Vector2(1782, 916);      // 步枪内部结构（图层 42：抛弹口后面那块黑色，拉栓组往后弹时露出来的就是它）
    static readonly Vector2 P_R_BODY = new Vector2(1883, 990), P_R_COVER = new Vector2(1786, 960), P_R_STOCK = new Vector2(1184, 1075);
    static readonly Vector2 P_R_HAND_L = new Vector2(1430, 1135), P_R_HAND_R = new Vector2(1958, 1066);
    static readonly Vector2 P_R_LIGHT = new Vector2(2276, 1018);     // 步枪挂载手电筒的灯头（枪口下方的小突起）
    // 胸挂上的装饰步枪 ↔ 能开火的步枪：取出动画里换枪时，从胸挂步枪的位置、角度、大小开始（PSD 量的）
    static readonly Vector2 MOUNT_RIFLE_CENTER = new Vector2(683.5f, 1175.5f);   // 胸挂步枪的中心（角色 PSD）
    static readonly Vector2 HELD_RIFLE_CENTER = new Vector2(1713f, 1014f);       // 能开火的步枪的中心（武器 PSD）
    const float MOUNT_RIFLE_TURN = -57.6f - 5.6f;                     // 胸挂步枪比能开火的步枪多斜多少度
    const float MOUNT_RIFLE_SCALE = 0.81f;                            // 胸挂步枪比能开火的步枪小多少（长度比）
    // 战术匕首（武器 PSD）：匕首 + 持刀手绕握刀的手转
    static readonly Vector2 P_DAGGER_GRIP = new Vector2(990, 1347);  // 握刀的手（持刀手中心）
    static readonly Vector2 P_DAGGER = new Vector2(1049, 1328);      // 匕首下半段（刀柄 + 发射器）
    static readonly Vector2 P_DAGGER_TIP = new Vector2(1282, 940);   // 刀刃上半段（弹出来的那截）
    static readonly Vector2 P_DAGGER_GLOW = new Vector2(1252, 986);  // 激光亮光图层
    static readonly Vector2 P_DAGGER_LIGHT = new Vector2(1200, 1000);    // 激光照明的中心（刀身中间）
    static readonly Vector2 DAGGER_DIR = new Vector2(0.577f, 0.817f);    // 刀身方向（刀柄 → 刀尖，本地坐标）
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
    string _held;                                      // 现在手上拿的是什么（null = 空手，"pistol" = 手枪，"rifle" = 步枪）
    Transform _handPoint, _muzzle, _slide;             // 手点（整把枪挂在它下面）/ 枪口 / 滑套
    Vector2 _gripBase;                                 // 握把原位（根节点本地坐标）
    Vector3 _slideBase;                                // 滑套原位
    readonly List<SpriteRenderer> _pistolParts = new List<SpriteRenderer>();   // 手枪各部件 + 两只持枪手
    readonly List<SpriteRenderer> _rifleParts = new List<SpriteRenderer>();    // 步枪各部件 + 两只持枪手
    Transform _pistolMuzzle, _rifleMuzzle;             // 两把枪的枪口（拿哪把，_muzzle 就是哪个）
    Transform _rifleCharger, _rifleBolt;               // 步枪上弹器 / 拉栓（开枪时往后弹）
    Vector3 _rifleChargerBase, _rifleBoltBase;         // 它们的原位
    SpriteRenderer _mountRifle;                        // 胸挂上的步枪（拿出来时藏起来）
    float _k0, _km0;                                   // 新一枪开始时，上一枪还没回正的后坐量（连发时接着往上叠，不会跳回 0）
    float _k0Prev, _km0Prev;                           // 上一枪开始时的这个量（算"上一枪现在还剩多少"用）
    int _burstLeft;                                    // 测试连发：还剩几枪
    float _burstTimer;                                 // 测试连发：离下一枪还有多久
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
    Transform _gunHeld, _rifleHeld;                    // 手枪 / 步枪的整体节点（挂在手点下面）
    float _readyUntil, _lowK;                          // 举着枪到什么时候（开枪后一段时间）/ 枪口朝下的程度（0 = 举平，1 = 朝下）
    readonly List<SpriteRenderer> _daggerParts = new List<SpriteRenderer>();   // 匕首各部件 + 持刀手
    SpriteRenderer _daggerTip, _daggerGlow, _daggerBloom; // 刀刃上半段 / 激光亮光 / 过曝时多叠的一层亮光
    Vector3 _daggerTipBase;                            // 刀刃上半段的原位
    Transform _daggerHeld, _daggerLightAt;             // 匕首节点 / 激光照明的位置
    UnityEngine.Rendering.Universal.Light2D _daggerLight;   // 激光照亮周围的光
    float _daggerT;                                    // 拿出匕首后过了多久（弹出 / 闪动用）
    float _swingT = -1f;                               // 挥动进行了多久（-1 = 没在挥）
    bool _swingNew = true;                             // 要开始新的一下（重新随机挥下时手的 y）
    float _swingDownY;                                 // 这一下挥下时手的 y
    float _drawT = -1f;                                // 步枪取出动画进行了多久（-1 = 没在播）
    SpriteRenderer _drawHandL, _rifleHandL;            // 取出动画里单独动的左手 / 枪上的持枪左手
    Sprite _rifleHandRSprite;                          // 持枪右手的图（取出动画用）
    Transform _rifleStock, _rifleLamp;                 // 步枪枪托（取出时拉开）/ 步枪手电灯头
    Vector3 _rifleStockBase;                           // 枪托原位
    SpriteRenderer _rifleLampGlow;                     // 步枪手电灯头发光点
    bool _rifleLightOn;                                // 步枪手电已经打开（取出动画播完才开）
    float _lightOnT = 9f;                              // 手电刚打开多久（开的时候闪一下）
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
    readonly SpriteRenderer[] _mountRifleMags = new SpriteRenderer[4];   // 胸挂上的 4 个步枪弹匣
    readonly int[] _rSlotState = { SLOT_FULL, SLOT_FULL, SLOT_FULL, SLOT_FULL };   // 4 个步枪弹匣袋的状态
    SpriteRenderer _rifleMag, _rifleHandR;             // 步枪里的弹匣 / 步枪前手（换弹时换成单独动的手）
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
            _mountRifleMags[i] = Part("RifleMag" + (i + 1), _mount, "RifleMag" + (i + 1), P_RIFLE_MAG[i], P_MOUNT, 6 + i);    // 步枪弹匣 ×4（图层 6~9；步枪换弹从这里拿）
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
        _mountRifle = Part("Rifle", _mount, "Rifle", P_RIFLE, P_MOUNT, 29);           // 战斗步枪（图层 29）：固定在胸挂上（拿出来时藏起来）
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
        _gunHeld = gun;
        _gunMag = Part("PistolMag", gun, "Weapons/PistolMag", P_GUN_MAG, P_GRIP, 40);   // 枪里的弹匣（轴心在弹匣底部）
        _pistolParts.Add(_gunMag);
        SpriteRenderer slide = Part("PistolSlide", gun, "Weapons/PistolSlide", P_GUN_SLIDE, P_GRIP, 41);
        _pistolParts.Add(slide);
        _slide = slide.transform; _slideBase = _slide.localPosition;                  // 滑套（开枪时往后弹；换弹也用它）
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
        RefillMags(false);                                                            // 胸挂上 4 个手枪弹匣一开始都是满的
        _pistolMuzzle = new GameObject("Muzzle").transform;                           // 枪口子弹生成点（跟着枪动）
        _pistolMuzzle.SetParent(gun, false);
        _pistolMuzzle.localPosition = CanvasToLocal(P_MUZZLE - P_GRIP);
        _muzzle = _pistolMuzzle;
        _gunLamp = new GameObject("GunLight").transform;                              // 手电筒灯头（跟着枪动）
        _gunLamp.SetParent(gun, false);
        _gunLamp.localPosition = CanvasToLocal(P_GUN_LIGHT - P_GRIP);
        _gunLampGlow = GlowDot(_gunLamp);                                             // 灯头一直亮（不受光照）
        if (RenderMats.IsURP)                                                         // URP 2D：真正照亮前面的光
        {
            _gunBeam = WorldLighting.CreatePoint("GunFlashlight", transform, gunLightColor, gunLightIntensity, gunLightRange, gunLightAngle);
            WorldLighting.EnableShadows(_gunBeam);                                    // 照到建筑 / 树石会投影子
        }
        // 步枪：也挂在手点下面（拿步枪时手点放在步枪握把上）。图层顺序和武器 PSD 一样（从下到上）：
        //   内部结构（图层 42）→ 上弹器 → 拉栓 → 弹匣 → 枪身 → 图层 43 → 枪托 → 持枪左手 → 持枪右手
        Transform rifle = new GameObject("HeldRifle").transform;
        rifle.SetParent(_handPoint, false);
        rifle.localPosition = CanvasToLocal(P_RIFLE_GRIP - P_GRIP);                   // 手点在手枪握把时它不会显示，这里只是占位
        _rifleHeld = rifle;
        _rifleParts.Add(Part("RifleInner", rifle, "Weapons/RifleInner", P_R_INNER, P_RIFLE_GRIP, 39));   // 内部结构：垫在最下面，上弹器往后弹时空出来的地方不会透明
        SpriteRenderer rc = Part("RifleCharger", rifle, "Weapons/RifleCharger", P_R_CHARGER, P_RIFLE_GRIP, 40);
        SpriteRenderer rb = Part("RifleBolt", rifle, "Weapons/RifleBolt", P_R_BOLT, P_RIFLE_GRIP, 41);
        _rifleCharger = rc.transform; _rifleChargerBase = _rifleCharger.localPosition;
        _rifleBolt = rb.transform; _rifleBoltBase = _rifleBolt.localPosition;
        _rifleParts.Add(rc); _rifleParts.Add(rb);
        _rifleMag = Part("RifleMag", rifle, "Weapons/RifleMag", P_R_MAG, P_RIFLE_GRIP, 42);   // 步枪里的弹匣（轴心在底部）
        _rifleParts.Add(_rifleMag);
        _rifleParts.Add(Part("RifleBody", rifle, "Weapons/RifleBody", P_R_BODY, P_RIFLE_GRIP, 43));
        _rifleParts.Add(Part("RifleCover", rifle, "Weapons/RifleCover", P_R_COVER, P_RIFLE_GRIP, 44));
        SpriteRenderer stock = Part("RifleStock", rifle, "Weapons/RifleStock", P_R_STOCK, P_RIFLE_GRIP, 45);
        _rifleStock = stock.transform; _rifleStockBase = _rifleStock.localPosition;   // 枪托（取出动画里会被拉开）
        _rifleParts.Add(stock);
        _rifleHandL = Part("RifleHandL", rifle, "Weapons/RifleHandL", P_R_HAND_L, P_RIFLE_GRIP, 46);
        _rifleParts.Add(_rifleHandL);
        _rifleHandR = Part("RifleHandR", rifle, "Weapons/RifleHandR", P_R_HAND_R, P_RIFLE_GRIP, 47);   // 步枪前手（换弹用的那只）
        _rifleParts.Add(_rifleHandR);
        RefillMags(true);                                                             // 胸挂上 4 个步枪弹匣一开始都是满的
        _rifleMuzzle = new GameObject("RifleMuzzle").transform;                       // 步枪枪口
        _rifleMuzzle.SetParent(rifle, false);
        _rifleMuzzle.localPosition = CanvasToLocal(P_RIFLE_MUZZLE - P_RIFLE_GRIP);
        _rifleLamp = new GameObject("RifleLight").transform;                          // 步枪手电灯头（枪口下方的小突起）
        _rifleLamp.SetParent(rifle, false);
        _rifleLamp.localPosition = CanvasToLocal(P_R_LIGHT - P_RIFLE_GRIP);
        _rifleLampGlow = GlowDot(_rifleLamp);
        _rifleHandRSprite = Load("Weapons/RifleHandR");                               // 取出动画：右手换成持枪右手
        _drawHandL = new GameObject("DrawHandL").AddComponent<SpriteRenderer>();      // 取出动画：单独动的持枪左手
        _drawHandL.transform.SetParent(_rig, false);
        _drawHandL.sprite = Load("Weapons/RifleHandL");
        _drawHandL.sortingOrder = 46;
        _drawHandL.enabled = false;

        // 战术匕首：挂在手点下面（拿匕首时手点挂到身体上，跟着身体颠动 / 摇摆，和第一种 NPC 拿斧头一样）。
        //   图层顺序和武器 PSD 一样（从下到上）：刀刃上半段 → 下半段（刀柄）→ 激光亮光 → 持刀手
        Transform dagger = new GameObject("HeldDagger").transform;
        dagger.SetParent(_handPoint, false);
        _daggerHeld = dagger;
        _daggerTip = Part("DaggerTip", dagger, "Weapons/DaggerBlade", P_DAGGER_TIP, P_DAGGER_GRIP, 40);
        _daggerTipBase = _daggerTip.transform.localPosition;
        RenderMats.MakeUnlit(_daggerTip);                                             // 激光刃：自己发亮，不受光
        _daggerParts.Add(_daggerTip);
        _daggerParts.Add(Part("Dagger", dagger, "Weapons/Dagger", P_DAGGER, P_DAGGER_GRIP, 41));
        _daggerGlow = Part("DaggerGlow", dagger, "Weapons/DaggerSlash", P_DAGGER_GLOW, P_DAGGER_GRIP, 42);   // 亮光图层（叠加发光）
        _daggerBloom = Part("DaggerBloom", dagger, "Weapons/DaggerSlash", P_DAGGER_GLOW, P_DAGGER_GRIP, 42);  // 过曝时多叠的一层（大一点）
        _daggerBloom.transform.localScale = Vector3.one * 1.15f;
        foreach (SpriteRenderer g in new[] { _daggerGlow, _daggerBloom })
        {
            if (_additive != null) g.sharedMaterial = _additive; else RenderMats.MakeUnlit(g);
            _daggerParts.Add(g);
        }
        _daggerParts.Add(Part("DaggerHand", dagger, "Weapons/DaggerHand", P_DAGGER_GRIP, P_DAGGER_GRIP, 43));
        _daggerLightAt = new GameObject("DaggerLightAt").transform;                   // 激光照明的位置（刀身中间）
        _daggerLightAt.SetParent(dagger, false);
        _daggerLightAt.localPosition = CanvasToLocal(P_DAGGER_LIGHT - P_DAGGER_GRIP);
        if (RenderMats.IsURP) _daggerLight = WorldLighting.CreatePoint("DaggerLight", transform, daggerLightColor, 0f, daggerLightRadius, 360f);
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
        UpdateReactTest();                                        // 受击 / 死亡的测试开关

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

        if (_dead) speed = 0f;                                    // 死了：不走

        // 朝向：朝左 = 整个角色水平翻转（死了就不再翻身）
        if (!_dead) UpdateAim(dt);                                // 瞄准（可能会翻身，所以放在定朝向前面）
        _rig.localScale = new Vector3((_dead ? _deadLeft : FacingLeft) ? -1f : 1f, 1f, 1f);

        const int SUB = 4;                                        // 每帧分 4 小步算，弹簧更稳
        for (int i = 0; i < SUB; i++) { StepReact(dt / SUB); Step(dt / SUB, speed); }   // 受击 / 倒地先算，挂件才能被它甩
        if (!_dead) UpdateTest(dt);                               // 测试开关（换手 / 开枪）
        AnimateHeld(dt);                                          // 持枪 / 后坐
        Apply();
        ApplyReact();                                             // 叠上受击 / 倒地
        if (_dead && !_weaponDropped && Mathf.Abs(_dieAng) >= weaponDropAt) DropWeapon(dt);   // 倒到一定角度：武器脱手
        if (_handPoint != null) _prevHandWorld = _handPoint.position;   // 记下手点（算脱手速度）
        UpdateGunLight();                                         // 手枪手电筒跟着枪口方向照
        currentState = _dead ? "死亡" : (_run > 0.5f ? "跑步" : (_move > 0.05f ? "走路" : "站立")) + "  " + speed.ToString("0.00");
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
        float breath = (_dead ? 0f : 0.0057f) * (1f - move) * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * 0.45f * _time + _phaseSeed));
        float bobUp = Mathf.Lerp(bob.x, bob.y, run) * sv * sv * move + breath;
        float bobRef = -bobUp * REF;                                              // 换成预览刻度（向下为正）
        float bobV = (bobRef - _prevBob) / dt; float boba = (bobV - _prevBobV) / dt;
        _prevBob = bobRef; _prevBobV = bobV;
        // 身体摇摆 + 跑步前倾
        float tilt = Mathf.Lerp(waddle.x, waddle.y, run) * sv * move + runLean * run;
        float tiltV = (tilt - _prevTilt) / dt; float tilta = (tiltV - _prevTiltV) / dt;
        _prevTilt = tilt; _prevTiltV = tiltV;
        float bodyTilt = tilt;                                                    // 身体自己的摇摆（摆到身体上的还是它）
        tilt += _hitAng + _dieAng; tilta += _reactAcc * hitGearKick;              // 挂件 / 头感受到的倾斜：再加上受击后仰和倒地（被甩、躺下后往下垂）

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

        _move = move; _run = run; _sv = sv; _bobUp = bobUp; _tilt = bodyTilt;
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
        float idle = _dead ? 0f : 1f - move;                          // 死了：手不再轻轻浮动
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
    // 受击 / 死亡（测试开关在 Inspector「受击 / 死亡测试」里；游戏里由战斗系统调 PlayHit / PlayDeath）
    //   受击：身体后仰（弹簧，会回正）+ 整个人被推开一点 + 往下一沉；挂件 / 头被这一下甩起来。
    //   死亡：腿一软往下沉，同时中弹的冲击让人开始倒，之后重力让他越倒越快（像一根绕脚底转的棍子），
    //         摔到地上弹几下停住；手臂软了跟着甩，倒到一定角度武器脱手飞出去、落地弹一下再滑停。
    // ============================================================================
    public enum HitFrom { [InspectorName("前面")] Front, [InspectorName("后面")] Back, [InspectorName("随机")] Random }

    bool _dead, _settled, _weaponDropped, _deadLeft;   // 死了 / 已经躺稳 / 武器已经脱手 / 死的时候朝左
    float _hitAng, _hitV, _shove, _shoveV;             // 受击：身体后仰角度和角速度 / 被推开的距离和速度（本地，正 = 往前）
    float _dieAng, _dieV, _dieSign = 1f, _deadT;       // 倒地：角度和角速度（本地，正 = 往后倒）/ 往哪边倒 / 死了多久
    float _reactAcc, _prevReactV, _groundY;            // 身体整体转动的角加速度（甩挂件用）/ 上一步的角速度 / 地面高度（世界）
    string _heldBeforeDeath;                           // 死之前手上拿着什么（站起来时拿回来）
    Vector3 _prevHandWorld;                            // 上一帧手点的世界坐标（算武器脱手时的速度）
    readonly List<GameObject> _dropped = new List<GameObject>();   // 掉在地上的武器（站起来 / 人删掉时一起收走）
    //                       频率  阻尼  加减速 跑动 跟身体转 颠动 最大角度 吊着的程度
    readonly Pend _armL = new Pend(2.2f, 0.18f, 0f, 0f, 2.5f, 0f, 70f, 0.2f);   // 死后前手：软了，跟着甩
    readonly Pend _armR = new Pend(2.0f, 0.18f, 0f, 0f, 2.5f, 0f, 70f, 0.2f);   // 死后后手（频率不一样，两只手不会一起甩）

    // （只读）死了没有
    public bool IsDead => _dead;

    // 外部调用：挨了一下。dir = 子弹 / 攻击飞的方向（世界坐标，比如 被打的人 - 开枪的人）；strength = 多重（1 = 正常）
    public void PlayHit(Vector2 dir, float strength = 1f)
    {
        if (!_built || _dead) return;                                       // 还没拼好 / 已经死了
        PlayHit(dir, strength, HitPoint(dir));                              // 没给打中的点：当作打在胸口
    }

    // 同上；point = 打中的位置（世界坐标，着弹点效果从这里出来）；fx = 要不要放着弹点效果（子弹打中时 Ballistics 已经放过了，传 false）
    public void PlayHit(Vector2 dir, float strength, Vector3 point, bool fx = true)
    {
        if (!_built || _dead) return;
        HitKick(PushLocal(dir, FacingLeft), Mathf.Max(0f, strength) * Random.Range(0.8f, 1.2f));   // 每一下轻重有点不一样
        if (fx) SpawnImpact(point, dir, strength, false);                   // 着弹点效果
    }

    // 外部调用：被打死。朝 dir（子弹飞的方向）倒下：前面中弹往后倒，背后中弹往前扑
    public void PlayDeath(Vector2 dir, float strength = 1f)
    {
        if (!_built || _dead) return;
        PlayDeath(dir, strength, HitPoint(dir));                            // 没给打中的点：当作打在胸口
    }

    // 同上；point = 打中的位置（世界坐标）；fx = 要不要放着弹点效果（同 PlayHit）
    public void PlayDeath(Vector2 dir, float strength, Vector3 point, bool fx = true)
    {
        if (!_built || _dead) return;
        if (fx) SpawnImpact(point, dir, strength, true);                    // 着弹点效果（致命的一下：大团血雾）
        _deadLeft = FacingLeft;                                             // 倒下后不再翻身
        float push = PushLocal(dir, _deadLeft);                             // 被往哪边推（本地）
        float k = Mathf.Max(0f, strength) * Random.Range(0.85f, 1.15f);
        HitKick(push, k);                                                   // 先吃一下冲击（身体一震、头一甩）
        _dead = true; _settled = false; _weaponDropped = false; _deadT = 0f;
        _dieSign = -push;                                                   // 往推的方向倒（本地角度：正 = 往后倒）
        _dieAng = 0f;
        _dieV = _dieSign * deathImpulse * k;                                // 冲击给的初速度，之后交给重力
        _armL.a = _armR.a = 0f;
        _armL.v = Random.Range(-60f, 60f); _armR.v = Random.Range(-60f, 60f);   // 两只手一开始就甩得不一样
        _heldBeforeDeath = _held;                                           // 记下手上拿的（站起来时拿回来）
        _groundY = transform.TransformPoint(new Vector3(0f, FEET_Y, 0f)).y; // 脚底 = 地面（武器落到这里）
        _prevHandWorld = _handPoint != null ? _handPoint.position : transform.position;
        StopForDeath();                                                     // 停掉开枪 / 换弹 / 挥动 / 瞄准
    }

    // 外部调用：站起来（测试用：重新开始；掉在地上的武器收走，手上拿回原来的东西）
    public void Revive()
    {
        if (!_dead) return;
        _dead = false; _settled = false;
        _dieAng = _dieV = 0f; _hitAng = _hitV = 0f; _shove = _shoveV = 0f; _deadT = 0f; _prevReactV = 0f;   // 全部归零
        foreach (GameObject g in _dropped) if (g != null) Destroy(g);      // 地上的武器收走
        _dropped.Clear();
        _rig.localRotation = Quaternion.identity;                           // 站直
        _rig.localPosition = new Vector3(0f, FEET_Y, 0f);
        RestoreAfterRevive();                                               // 手上拿回原来的东西
    }

    // 受击 / 死亡的测试开关（勾一下做一次，自动取消勾选）
    void UpdateReactTest()
    {
        if (testRevive) { testRevive = false; Revive(); }
        if (testHit) { testHit = false; PlayHit(TestHitDir(), testHitStrength); }
        if (testDeath) { testDeath = false; PlayDeath(TestHitDir(), testHitStrength); }
    }

    // 测试：子弹飞的方向（从前面打来 = 往角色身后飞；带一点上下随机）
    Vector2 TestHitDir()
    {
        float s = FacingLeft ? -1f : 1f;
        bool front = testHitFrom == HitFrom.Front || (testHitFrom == HitFrom.Random && Random.value < 0.5f);
        return new Vector2(front ? -s : s, Random.Range(-0.2f, 0.2f));
    }

    // 中弹的位置（世界坐标）：胸口附近，靠子弹打来的那一面，带一点随机（战斗系统没有准确的点时也用它）
    public Vector3 HitPoint(Vector2 dir)
    {
        if (_body == null) return transform.position;                                 // 还没拼好：就用人的位置
        Vector3 c = _body.TransformPoint(new Vector3(0f, impactHeight, 0f));          // 胸口
        float from = dir.x > 0f ? -1f : 1f;                                           // 子弹从哪边来（世界左右）
        return c + new Vector3(from * 0.06f + Random.Range(-0.03f, 0.03f), Random.Range(-0.06f, 0.06f), 0f);
    }

    // 着弹点效果：多半是血雾，有时打在装备上是火花；致命的一下 / 很重的一下用最大那团血
    void SpawnImpact(Vector3 at, Vector2 dir, float strength, bool fatal)
    {
        if (!impactFx) return;
        if (fatal || Random.value < fleshChance)
        {
            int v = fatal || strength >= 1.5f ? 2 : (Random.value < 0.5f ? 0 : 1);   // 普通一下：小 / 中随机
            HitFx.Flesh(at, dir, v, impactScale * Random.Range(0.85f, 1.15f), fleshLife, ImpactLayer());
        }
        else HitFx.Metal(at, impactScale, metalLife, metalLightColor, metalLight, metalLightRadius, ImpactLayer());
    }

    int ImpactLayer() { return _oldSprite != null ? _oldSprite.sortingLayerID : 0; }   // 和人同一个排序层

    // 子弹飞的方向 → 人被推的方向（本地：+1 = 往角色前面推，-1 = 往后推）
    static float PushLocal(Vector2 dir, bool left)
    {
        float x = dir.x * (left ? -1f : 1f);                                // 换成角色自己的左右
        return Mathf.Abs(x) < 0.001f ? -1f : Mathf.Sign(x);                 // 没方向（正上 / 正下）：当作从前面打来
    }

    // 吃一下冲击：后仰的弹簧、被推开的弹簧都给一个初速度（之后弹簧自己回正）
    void HitKick(float push, float k)
    {
        float w = 2f * Mathf.PI * Mathf.Max(0.1f, hitFreq);                 // 弹簧的角频率
        _hitV += -push * hitLean * w * 1.6f * k;                            // 上身往推的方向仰（往前推 = 往前栽）
        float ws = w * 0.6f;
        _shoveV += push * hitShove * ws * 2.718f * k;                       // 整个人往推的方向挪
        HeadWhip(push, k);                                                  // 头再多甩一下
    }

    // 每小步：受击的两个弹簧 + 倒地（重力让人越倒越快，摔到地上弹几下停住）+ 死后手臂
    void StepReact(float dt)
    {
        float w = 2f * Mathf.PI * Mathf.Max(0.1f, hitFreq);
        _hitV += (-w * w * _hitAng - 2f * hitDamp * w * _hitV) * dt;        // 后仰：弹回来（阻尼小会来回晃几下）
        _hitAng += _hitV * dt;
        float ws = w * 0.6f;
        _shoveV += (-ws * ws * _shove - 2f * ws * _shoveV) * dt;            // 推开：慢慢站回原位（不来回晃）
        _shove += _shoveV * dt;
        if (_dead)
        {
            _deadT += dt;
            if (!_settled)
            {
                float g = 9.81f * Mathf.Max(0f, deathGravity) / Mathf.Max(0.05f, deathCOM);   // 重力 ÷ 重心高度
                float a = g * Mathf.Sin(_dieAng * Mathf.Deg2Rad) + _dieSign * g * 0.03f;     // 歪得越多倒得越快（加一点点，保证不会立在那里）
                _dieV += a * Mathf.Rad2Deg * dt;
                _dieAng += _dieV * dt;
                float lie = Mathf.Clamp(deathLieAngle, 10f, 120f);
                if (Mathf.Abs(_dieAng) >= lie && _dieV * _dieAng > 0f)     // 摔到地上
                {
                    _dieAng = Mathf.Sign(_dieAng) * lie;
                    if (Mathf.Abs(_dieV) > 40f) _dieV = -_dieV * deathBounce;   // 还很快：弹一下
                    else { _dieV = 0f; _settled = true; }                   // 慢了：停住
                }
            }
        }
        float rv = _hitV + _dieV;                                           // 身体整体转动的角速度
        _reactAcc = (rv - _prevReactV) / dt; _prevReactV = rv;              // 角加速度：受击 / 落地那一下很大，挂件 / 头 / 手臂被它甩起来
        if (_dead)
        {
            _armL.Step(dt, 0f, 0f, _dieAng + _hitAng, _reactAcc * deathArmFlop, 0f, 0f);
            _armR.Step(dt, 0f, 0f, _dieAng + _hitAng, _reactAcc * deathArmFlop, 0f, 0f);
        }
    }

    // 摆完部件之后叠上受击 / 倒地
    void ApplyReact()
    {
        float s = (_dead ? _deadLeft : FacingLeft) ? -1f : 1f;
        float sink = _dead ? deathSink * Smooth01(_deadT / Mathf.Max(0.01f, deathSinkTime)) : 0f;     // 腿一软往下沉
        sink += hitBuckle * Mathf.Clamp01(Mathf.Abs(_hitAng) / Mathf.Max(0.1f, hitLean));            // 挨打往下一沉
        _body.localPosition += new Vector3(0f, -sink, 0f);
        _body.localRotation = _body.localRotation * Quaternion.Euler(0f, 0f, _hitAng);               // 上身仰 / 栽（绕腰）
        float fall = _dead ? Mathf.Clamp01(Mathf.Abs(_dieAng) / Mathf.Max(1f, deathLieAngle)) : 0f;   // 倒了多少（0~1）
        float slide = _dead ? -_dieSign * deathSlide * Smooth01(fall) : 0f;                          // 被冲击带着往倒的方向滑
        float lift = _dead ? deathGroundLift * Mathf.Abs(Mathf.Sin(_dieAng * Mathf.Deg2Rad)) : 0f;   // 越躺平越要抬起来一点（身体有厚度）
        _rig.localPosition = new Vector3((_shove + slide) * s, FEET_Y + lift, 0f);                    // 本地的前后 → 世界的左右
        _rig.localRotation = Quaternion.Euler(0f, 0f, _dieAng * s);                                  // 整个人绕脚底倒下
        if (_handPoint != null && _handPoint.parent == _rig && Mathf.Abs(_hitAng) > 0.001f)          // 枪挂在根节点上：跟着上身仰，枪口再多歪一点
        {
            Vector3 p = _handPoint.localPosition - _bodyBase;
            _handPoint.localPosition = _bodyBase + Quaternion.Euler(0f, 0f, _hitAng) * p + new Vector3(0f, -sink, 0f);
            _handPoint.localRotation = _handPoint.localRotation * Quaternion.Euler(0f, 0f, _hitAng * (1f + hitGunJolt));
        }
        if (_dead)                                                          // 死了：两只手臂软了甩
        {
            _handL.localRotation = _handL.localRotation * Quaternion.Euler(0f, 0f, _armL.a);
            _handR.localRotation = _handR.localRotation * Quaternion.Euler(0f, 0f, _armR.a);
        }
    }

    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }   // 0~1 平滑（两头慢中间快）

    // 把手上那件东西复制一份扔进世界（不挂在人身上）。skip = 名字里带这个的部件不复制（持枪手）；
    // single 不为空时不复制，直接用这张图（拿着的图连着手的东西，掉下去用腰上挂的那张）
    void SpawnDropped(Transform src, string skip, Sprite single, int singleOrder, float dt, Color lightColor, float lightIntensity, float lightRadius)
    {
        GameObject root = new GameObject("DroppedWeapon");
        Transform rt = root.transform;
        rt.position = src.position;
        Vector3 ax = src.TransformVector(Vector3.right), ay = src.TransformVector(Vector3.up);       // 这件东西现在的"右"和"上"
        bool mirror = ax.x * ay.y - ax.y * ay.x < 0f;                                               // 朝左（左右翻过来了）
        rt.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-ay.x, ay.y) * Mathf.Rad2Deg);            // 用"上"算角度（左右翻转不影响它）
        rt.localScale = new Vector3(mirror ? -ax.magnitude : ax.magnitude, ay.magnitude, 1f);        // 大小 + 翻转都带上
        SortingGroup mine = GetComponent<SortingGroup>();
        SortingGroup sg = root.AddComponent<SortingGroup>();                                        // 整件当成一个整体排序（和人同一层）
        if (mine != null) { sg.sortingLayerID = mine.sortingLayerID; sg.sortingOrder = mine.sortingOrder; }
        if (single != null)
        {
            SpriteRenderer one = new GameObject("Part").AddComponent<SpriteRenderer>();
            one.transform.SetParent(rt, false);
            one.sprite = single;
            one.sortingOrder = singleOrder;
        }
        else
        {
            foreach (SpriteRenderer sr in src.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!sr.enabled || sr.sprite == null || sr.name.Contains(skip)) continue;          // 藏着的 / 持枪手 不要
                SpriteRenderer c = new GameObject(sr.name).AddComponent<SpriteRenderer>();
                c.transform.SetParent(rt, false);
                c.transform.localPosition = src.InverseTransformPoint(sr.transform.position);       // 相对位置不变
                Vector3 cu = src.InverseTransformVector(sr.transform.TransformVector(Vector3.up));   // 相对角度、大小不变
                c.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-cu.x, cu.y) * Mathf.Rad2Deg);
                c.transform.localScale = new Vector3(cu.magnitude, cu.magnitude, 1f);
                c.sprite = sr.sprite; c.color = sr.color; c.flipX = sr.flipX; c.flipY = sr.flipY;
                c.sortingOrder = sr.sortingOrder;
                c.sharedMaterial = sr.sharedMaterial;                                               // 发光的部件还是发光
            }
        }
        if (lightIntensity > 0f && RenderMats.IsURP)                                                // 激光刃掉在地上还亮着
            WorldLighting.CreatePoint("DroppedLight", rt, lightColor, lightIntensity, lightRadius, 360f);
        Vector3 hv = (src.position - _prevHandWorld) / Mathf.Max(0.001f, dt);                      // 手当时在往哪动（人倒下带着甩）
        float fallW = -_dieSign * (_deadLeft ? -1f : 1f);                                           // 往倒的方向（世界左右）
        Vector2 vel = new Vector2(hv.x * weaponCarry + fallW * weaponThrow, hv.y * weaponCarry + weaponUp);   // 带走一部分手的速度 + 往倒的方向抛
        float spin = RandIn(weaponSpin) * (Random.value < 0.5f ? -1f : 1f);
        _dropped.Add(root);
        StartCoroutine(FlyDropped(rt, vel, spin));
    }

    // 脱手的武器：抛出去 → 砸到地上弹一下 → 在地上滑一下，慢慢放平停住
    IEnumerator FlyDropped(Transform t, Vector2 vel, float spin)
    {
        float ground = _groundY + weaponRestLift, rest = 0f;
        float ang = t.eulerAngles.z;
        while (t != null && rest < 1.2f)
        {
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                vel.y -= 9.81f * weaponGravity * dt;                                                // 重力
                Vector3 p = t.position + new Vector3(vel.x * dt, vel.y * dt, 0f);
                ang += spin * dt;
                if (p.y <= ground)
                {
                    p.y = ground;
                    if (vel.y < -0.5f) { vel.y = -vel.y * weaponBounce; vel.x *= 0.6f; spin *= -0.5f; }   // 砸到地上：弹一下，转向反过来
                    else
                    {
                        vel.y = 0f;
                        vel.x = Mathf.MoveTowards(vel.x, 0f, 4f * dt);                              // 在地上滑一下停住
                        spin = Mathf.MoveTowards(spin, 0f, 2000f * dt);
                        ang = Mathf.MoveTowardsAngle(ang, Mathf.Round(ang / 180f) * 180f, 300f * dt);   // 慢慢放平
                        rest += dt;
                    }
                }
                t.position = p;
                t.rotation = Quaternion.Euler(0f, 0f, ang);
            }
            yield return null;
        }
    }

    // 掉在地上的武器的图（尸体淡出时一起淡出用）
    public List<SpriteRenderer> DroppedRenderers()
    {
        List<SpriteRenderer> list = new List<SpriteRenderer>();
        foreach (GameObject g in _dropped) if (g != null) list.AddRange(g.GetComponentsInChildren<SpriteRenderer>());
        return list;
    }

    // 人删掉时，地上的武器一起收走
    void OnDestroy()
    {
        foreach (GameObject g in _dropped) if (g != null) Destroy(g);
    }

    // 死的时候：停掉开枪 / 换弹 / 挥动 / 瞄准
    void StopForDeath()
    {
        StopReload();
        _recoilT = -1f; _burstLeft = 0; _swingT = -1f;
        _lockTarget = null; _aimUntil = 0f;
    }

    // 受击时头盔多甩一下（转动点在脖子）
    void HeadWhip(float push, float k)
    {
        if (_headRot != null) _headRot.v += push * 120f * k * hitHeadWhip;
    }

    // 武器脱手：复制一份扔出去，手上换回空手；胸挂上对应的那把不再出现（已经掉出去了）
    void DropWeapon(float dt)
    {
        _weaponDropped = true;
        string was = _held;
        if (was == null) return;                                            // 本来就空手
        bool drawing = _drawT >= 0f;                                        // 还在取步枪（枪还在胸挂上）：不扔
        Transform src = was == "pistol" ? _gunHeld : was == "rifle" ? _rifleHeld : _daggerHeld;
        if (src != null && !drawing)
        {
            bool d = was == "dagger";
            SpawnDropped(src, "Hand", null, 0, dt, daggerLightColor, d ? daggerLightIntensity * 0.6f : 0f, daggerLightRadius);
        }
        SetHeld(null);                                                      // 手上空了（两只手换回平时的手）
        if (drawing) return;
        if (was == "pistol" && _mountPistol != null) _mountPistol.enabled = false;
        if (was == "rifle" && _mountRifle != null) _mountRifle.enabled = false;
        if (was == "dagger" && _gear.TryGetValue("Knife", out Transform knife)) knife.GetComponent<SpriteRenderer>().enabled = false;
    }

    // 站起来后拿回死之前的东西
    void RestoreAfterRevive()
    {
        SetHeld(null);
        SetHeld(_heldBeforeDeath);
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

    // 现在拿的是不是步枪；当前这把枪的握把 / 枪口（原图坐标）和"肩膀"
    bool IsRifle => _held == "rifle";
    bool IsGun => _held == "pistol" || _held == "rifle";             // 拿的是枪（匕首不算）
    Vector2 GripCanvas => IsRifle ? P_RIFLE_GRIP : P_GRIP;
    Vector2 MuzzleCanvas => IsRifle ? P_RIFLE_MUZZLE : P_MUZZLE;
    Vector2 Shoulder => IsRifle ? rifleShoulder : aimShoulder;

    // 换手：key = "pistol" 拿出手枪 / "rifle" 拿出步枪（两只手换成持枪手、胸挂上那把藏起来）；null = 空手
    void SetHeld(string key)
    {
        string prev = _held;
        _held = key;                                                        // 记下现在拿什么
        bool p = key == "pistol", r = key == "rifle", d = key == "dagger";
        foreach (SpriteRenderer sr in _pistolParts) if (sr != null) sr.enabled = p;   // 手枪各部件 + 持枪手
        foreach (SpriteRenderer sr in _rifleParts) if (sr != null) sr.enabled = r;    // 步枪各部件 + 持枪手
        foreach (SpriteRenderer sr in _daggerParts) if (sr != null) sr.enabled = d;   // 匕首各部件 + 持刀手
        if (_bodyHandL != null) _bodyHandL.enabled = !p && !r && !d;        // 平时的前手：拿东西时藏起来
        if (_bodyHandR != null) _bodyHandR.enabled = !p && !r;              // 平时的后手：拿枪时藏起来（拿匕首只换前手）
        if (_mountPistol != null) _mountPistol.enabled = !p;                // 胸挂上的手枪 / 步枪：拿出来了就藏起来
        if (_mountRifle != null) _mountRifle.enabled = !r;
        if (_gear.TryGetValue("Knife", out Transform knife))                // 腰上的匕首：拔出来了就藏起来
            knife.GetComponent<SpriteRenderer>().enabled = !d;
        if (_daggerLight != null) _daggerLight.enabled = false;             // 激光先关着（弹出后再亮）
        _daggerT = 0f; _swingT = -1f; _swingNew = true;                    // 匕首：从头弹出
        foreach (SpriteRenderer g in new[] { _daggerGlow, _daggerBloom })   // 激光亮光先是灭的
            if (g != null) { Color c = daggerLightColor; c.a = 0f; g.color = c; }
        if (_daggerTip != null) _daggerTip.enabled = false;                 // 刀刃上半段：弹出时才出现
        _muzzle = r ? _rifleMuzzle : _pistolMuzzle;                         // 子弹 / 枪火从哪把枪的枪口出来
        Transform hpParent = d ? _body : _rig;                              // 枪：手点挂在根节点（稳稳端着）；匕首：挂在身体上（跟着身体晃，和第一种 NPC 拿斧头一样）
        if (_handPoint.parent != hpParent) _handPoint.SetParent(hpParent, false);
        _gripBase = d ? (Vector2)CanvasToLocal(P_DAGGER_GRIP - P_BODY) : (Vector2)CanvasToLocal(GripCanvas - P_ROOT);   // 手点放在握把上
        if (_gunHeld != null) _gunHeld.localPosition = Vector3.zero;        // 手枪节点 = 手点
        if (_rifleHeld != null) _rifleHeld.localPosition = Vector3.zero;    // 步枪节点 = 手点（部件都按步枪握把摆好了）
        _recoilT = -1f; _burstLeft = 0;                                     // 停掉正在播的后坐 / 连发
        StopReload();                                                       // 停掉换弹
        if (key == null) { _aimAng = 0f; _lockTarget = null; _aimUntil = 0f; }   // 收枪：瞄准清零
        _handPoint.localPosition = new Vector3(_gripBase.x, _gripBase.y, 0f);   // 手点回到握把原位
        _handPoint.localRotation = Quaternion.identity;
        if (_slide != null) _slide.localPosition = _slideBase;              // 滑套 / 拉栓组复位
        if (_rifleCharger != null) _rifleCharger.localPosition = _rifleChargerBase;
        if (_rifleBolt != null) _rifleBolt.localPosition = _rifleBoltBase;
        _handPoint.localScale = Vector3.one;
        if (IsGun && prev != "pistol" && prev != "rifle") { _aimAng = lowReadyAngle; _lowK = 1f; }   // 刚拿出枪：先是枪口朝下（没接敌）
        _drawT = -1f;                                                       // 停掉取出动画
        if (_drawHandL != null) _drawHandL.enabled = false;
        if (_rifleStock != null) _rifleStock.localPosition = _rifleStockBase;
        _rifleLightOn = r && !rifleDrawAnim;                                // 步枪手电：不播取出动画就直接开
        if (r && rifleDrawAnim && prev != "rifle")                          // 拿出步枪：播取出动画（从胸挂上取下来）
        {
            _drawT = 0f;
            if (_mountRifle != null) _mountRifle.enabled = true;            // 动画里抓住的那一刻才换
            foreach (SpriteRenderer sr in _rifleParts) if (sr != null) sr.enabled = false;
            if (_bodyHandL != null) _bodyHandL.enabled = true;
            if (_rifleStock != null) _rifleStock.localPosition = _rifleStockBase + new Vector3(rdStockCollapse / PPU_FULL, 0f, 0f);   // 枪托先收起来
        }
    }

    // 测试开关：Test Held 换手；Test Loop 每 1.4 秒自动开一次；勾 Test Fire 开一次（手枪 = 一枪，步枪 = 连发 Rifle Burst 枪）
    void UpdateTest(float dt)
    {
        if (testHeld != _lastTest)                                          // 选项变了：马上换手
        {
            _lastTest = testHeld;
            SetHeld(testHeld == TestHeld.Pistol ? "pistol" : testHeld == TestHeld.Rifle ? "rifle" : testHeld == TestHeld.Dagger ? "dagger" : null);
        }
        if (testRefillMags && _reloadT < 0f) { testRefillMags = false; RefillMags(false); RefillMags(true); }   // 勾了"补满"：胸挂弹匣（手枪 + 步枪）全部补满
        if (testReload) { testReload = false; PlayReload(0f, testReloadKind); }   // 勾了"测试换弹"：按选的种类换一次
        bool fire = testFire;                                               // 勾了一下
        testFire = false;                                                   // 自动取消勾选
        if (_reloadT >= 0f || _drawT >= 0f) { _burstLeft = 0; return; }     // 换弹 / 正在取出步枪时不开枪
        if (_held != null && testLoop)                                      // 循环：定时自动开
        {
            _testFireTimer -= dt;
            if (_testFireTimer <= 0f) { _testFireTimer = _held == "dagger" ? Mathf.Max(0.2f, daggerPeriod) : 1.4f; fire = true; }   // 匕首：一下接一下挥
        }
        if (fire && _held == "dagger") PlayAttack();                        // 匕首：挥一下
        else if (fire && IsGun)
        {
            if (IsRifle && _burstLeft <= 0) { _burstLeft = rifleBurst > 1 ? rifleBurst : 1; _burstTimer = 0f; }   // 步枪：连发（上一组没打完不重来）
            else TestShot();                                                // 手枪：一枪
        }
        if (_burstLeft > 0 && IsRifle)                                      // 步枪连发：每隔 Rifle Shot Interval 一枪
        {
            _burstTimer -= dt;
            if (_burstTimer <= 0f) { TestShot(); _burstLeft--; _burstTimer += 60f / Mathf.Max(60f, rifleRPM); }   // 射速 → 两枪间隔（秒）
        }
    }

    // 测试开一枪：后坐 + 子弹（有测试目标往目标打，没有往正前方 2.5 单位打）
    void TestShot()
    {
        PlayAttack();
        if (testAimTarget != null) FireBullet(testAimTarget.position);
        else if (TryGetMuzzle(out Vector3 m)) FireBullet(m + new Vector3(2.5f * (FacingLeft ? -1f : 1f), 0f, 0f));
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
        if (testAimTarget != null && IsGun)                                 // 测试：一直锁定拖进来的物体
        {
            if (_aimUntil <= _time) _aimLeft = FacingLeft;
            _lockTarget = testAimTarget;
            _aimUntil = _time + 0.3f;
        }
        if (_lockTarget != null && !_lockTarget.gameObject.activeInHierarchy) { _lockTarget = null; _aimUntil = 0f; }   // 目标没了：解除
        bool aiming = _aimUntil > _time && IsGun;
        bool raised = aiming || _time < _readyUntil;                        // 举着枪（在瞄准 / 刚开过枪）
        float want = raised || !IsGun ? 0f : lowReadyAngle;                 // 没接敌、不瞄准：枪口朝下（和第一种 NPC 一样）
        if (aiming)
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
        float speed = raised ? aimTurnSpeed : lowReadySpeed;                // 举枪快，放下慢
        float step = speed > 0f ? speed * dt : 9999f;                       // 这一帧最多转多少度
        _aimAng = Mathf.MoveTowards(_aimAng, want, step);
        float lowTarget = !raised && IsGun ? 1f : 0f;                       // 要不要放低
        float span = Mathf.Max(1f, Mathf.Abs(lowReadyAngle));               // 和枪口转动同样快慢地渐变
        float lowRate = (lowTarget > _lowK ? lowReadySpeed : aimTurnSpeed) / span;
        if (dt > 0f) _lowK = Mathf.MoveTowards(_lowK, lowTarget, lowRate * dt);
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
        Vector2 arm = _gripBase - Shoulder;                                 // 肩膀 → 握把（枪没转时）
        Vector2 muzzle = CanvasToLocal(MuzzleCanvas - GripCanvas);          // 握把 → 枪口
        float h = arm.y + muzzle.y;                                         // 枪管这条线比肩膀高多少
        Vector2 q = t - Shoulder;                                           // 肩膀 → 目标
        float len = Mathf.Max(q.magnitude, 0.001f);
        float phi = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;                  // 从肩膀看目标的角度
        float fix = Mathf.Asin(Mathf.Clamp(h / len, -1f, 1f)) * Mathf.Rad2Deg;   // 枪管不在肩膀高度：补一点角度
        return Mathf.DeltaAngle(0f, phi - fix);
    }

    // 枪转了 ang 度、手被顶开 push 时，握把在哪（根节点本地坐标）
    Vector2 AimHand(float ang, Vector2 push) { return AimHand(ang, push, 1f); }

    // 同上；lowMul = 放低持枪的下移用几成（换弹抬枪时逐渐取消）
    Vector2 AimHand(float ang, Vector2 push, float lowMul) { return Shoulder + Rot(_gripBase - Shoulder + push, ang) + lowReadyOffset * (_lowK * lowMul); }

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
        if (_dead) return;                                                  // 死了
        if (_held == "dagger") { if (_swingT < 0f) { _swingT = 0f; _swingNew = true; } return; }   // 匕首：挥一下（上一下没挥完不重来）
        if (!IsGun || _reloadT >= 0f || _drawT >= 0f) return;               // 没拿枪 / 正在换弹 / 正在取出步枪
        _readyUntil = _time + Mathf.Max(0f, lowReadyHold);                  // 开枪：先把枪举平，过一会儿没事再放下
        _lowK = 0f;                                                         // 手也马上举回来
        bool r = IsRifle;
        _k0 = 0f; _km0 = 0f;
        if (_recoilT >= 0f)                                                 // 上一枪还没回正（连发）：新的一枪从现在的后坐量接着往上冲
        {
            _k0 = Kick(_recoilT, r ? rifleHandRecoilRise : handRecoilRise, _shotHandReturn, _k0Prev);
            _km0 = Kick(_recoilT, _shotMuzzleRise, r ? rifleMuzzleReturn : muzzleReturn, _km0Prev);
        }
        _k0Prev = _k0; _km0Prev = _km0;
        _recoilT = 0f;
        _recoilHandOff = new Vector2(RandIn(r ? rifleRecoilHandX : recoilHandX), RandIn(r ? rifleRecoilHandY : recoilHandY));   // 这一枪手被顶回多远
        _shotKick = RandIn(r ? rifleRecoilKick : recoilKick);               // 这一枪枪口上扬多少度
        _shotHandReturn = RandIn(r ? rifleHandRecoilReturn : handRecoilReturn);   // 这一枪手回正的快慢
        _shotMuzzleRise = RandIn(r ? rifleMuzzleRise : muzzleRise);         // 这一枪枪口上扬用几秒
        UpdateAim(0f);                                                      // 重新算一下瞄准（只算不转）
        _aimAng = _aimWant;                                                 // 开枪这一下枪口直接对准目标
        _rig.localScale = new Vector3(FacingLeft ? -1f : 1f, 1f, 1f);       // 可能刚翻身：对好朝向
        ApplyAimPose();                                                     // 马上摆好，子弹从对准后的枪口出来
    }

    // 每帧：按后坐进度摆持枪手、枪口上扬、滑套、身体后仰
    void AnimateHeld(float dt)
    {
        _actionLean = 0f; _actionShift = 0f;                                // 身体的额外晃动先清零
        if (_held == null) return;
        if (_held == "dagger") { AnimateDagger(dt); return; }               // 匕首：弹出 / 激光 / 挥动
        if (_drawT >= 0f) { AnimateRifleDraw(dt); return; }                 // 步枪取出动画
        if (_reloadT >= 0f)                                                 // 正在换弹：换弹动画接管
        {
            if (IsRifle && _reloadKind == ReloadKind.Tactical) AnimateRifleTactical(dt);   // 步枪战术换弹：单独一套动作
            else if (IsRifle && _reloadKind == ReloadKind.Speed) AnimateRifleSpeed(dt);    // 步枪快速换弹：单独一套动作
            else if (IsRifle) AnimateRifleNormal(dt);                                      // 步枪普通换弹：单独一套动作
            else AnimateReload(dt);
            return;
        }
        bool r = IsRifle;
        float rise = r ? rifleHandRecoilRise : handRecoilRise, mret = r ? rifleMuzzleReturn : muzzleReturn;
        float bBack = r ? rifleBoltBackTime : slideBackTime, bHold = r ? rifleBoltHoldTime : slideHoldTime, bRet = r ? rifleBoltReturnTime : slideReturnTime;
        float angle = 0f, s = 0f;
        Vector2 off = Vector2.zero;
        if (_recoilT >= 0f)
        {
            _recoilT += dt;
            float k = Kick(_recoilT, rise, _shotHandReturn, _k0);           // 手：一下顶回去，再慢慢回正（连发时从上一枪剩下的量接着冲）
            float km = Kick(_recoilT, _shotMuzzleRise, mret, _km0);         // 枪口：单独的上扬 / 回正节奏
            angle = _shotKick * km;                                         // 枪口往上扬（绕握把转）
            off = _recoilHandOff * k;                                       // 手被顶回来
            _actionLean = (r ? rifleRecoilLean : recoilLean) * k;           // 身体被震得往后仰
            _actionShift = -(r ? rifleActionShift : actionShift) * k;       // 往后挪一点
            s = SlideCurve(_recoilT, bBack, bHold, bRet);                   // 滑套 / 拉栓组往后弹 → 停一下 → 弹回来
            float slideEnd = Mathf.Max(0.001f, bBack) + Mathf.Max(0f, bHold) + Mathf.Max(0.001f, bRet);
            float endT = Mathf.Max(slideEnd, 0.3f + 6f * Mathf.Max(rise + _shotHandReturn, _shotMuzzleRise + mret));  // 等手、枪口、滑套都回正了
            if (_recoilT > endT) _recoilT = -1f;                            // 播完了
        }
        Vector2 hp = AimHand(_aimAng, off);                                 // 手绕肩膀转到瞄准角度，后坐把手顶回来
        _handPoint.localPosition = new Vector3(hp.x, hp.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, _aimAng + angle);   // 瞄准角度 + 枪口上扬
        if (r)                                                              // 步枪：上弹器、拉栓沿枪身往后弹
        {
            if (_rifleCharger != null) _rifleCharger.localPosition = _rifleChargerBase + new Vector3(-rifleChargerKick * s / PPU_FULL, 0f, 0f);
            if (_rifleBolt != null) _rifleBolt.localPosition = _rifleBoltBase + new Vector3(-rifleBoltKick * s / PPU_FULL, 0f, 0f);
        }
        else if (_slide != null) _slide.localPosition = _slideBase + new Vector3(-slideKick * s / PPU_FULL, 0f, 0f);   // 手枪：滑套沿枪身往后
    }

    // 滑套曲线：开枪后 t 秒滑套在哪（0 = 原位，1 = 最后面）。往后滑 → 停一下 → 滑回来（快慢都能在 Inspector 里调）
    static float SlideCurve(float t, float back, float hold, float ret)
    {
        back = Mathf.Max(0.001f, back); hold = Mathf.Max(0f, hold); ret = Mathf.Max(0.001f, ret);
        if (t < back) return t / back;                                      // 往后滑
        if (t < back + hold) return 1f;                                     // 停在最后面
        return Mathf.Max(0f, 1f - (t - back - hold) / ret);                 // 滑回来
    }

    // 枪口（子弹生成点）的世界坐标（手上拿着枪才有）
    public bool TryGetMuzzle(out Vector3 pos)
    {
        bool ok = IsGun && _muzzle != null;
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
        sr.sprite = Load(IsRifle ? "Weapons/RifleBullet" : "Weapons/PistolBullet");   // 你画的子弹（轴心在弹头）
        sr.sortingOrder = 60;                                               // 画在人和建筑上面
        if (_oldSprite != null) sr.sortingLayerID = _oldSprite.sortingLayerID;
        RenderMats.MakeUnlit(sr);                                           // 子弹自己发亮，不受光
        AddTracer(go, sr);                                                  // 曳光（微弱发光 + 照亮）
        float dur = Mathf.Max(0.04f, d.magnitude / Mathf.Max(1f, bulletSpeed));   // 飞多久
        Destroy(go, dur + 0.5f);                                            // 保险：一定删掉
        StartCoroutine(FlyBullet(go.transform, from, target, dur, canRicochet));
        MuzzleFlash();                                                      // 枪火
        return true;
    }

    // 给子弹加曳光：一条拖在弹头后面的淡光晕（叠加发光）+ 一盏很弱的点光（2D 光照下照亮经过的地面 / 人）
    // 都挂在子弹下面：子弹删掉时一起删，不用另外收拾
    void AddTracer(GameObject bullet, SpriteRenderer bulletSr)
    {
        if (!tracer) return;                                                // 关了：什么都不加
        if (tracerGlow > 0f)
        {
            if (_glowSprite == null) _glowSprite = GlowSprite();            // 发光圆点图（和枪灯共用）
            if (_additive == null)
            {
                Shader add = Shader.Find("Legacy Shaders/Particles/Additive");   // 叠加发光材质
                if (add != null) _additive = new Material(add);
            }
            SpriteRenderer g = new GameObject("TracerGlow").AddComponent<SpriteRenderer>();
            g.transform.SetParent(bullet.transform, false);                 // 跟着子弹飞、跟着子弹转
            g.sprite = _glowSprite;                                         // 圆形柔光图（约 0.1 单位大）
            g.transform.localScale = new Vector3(tracerGlowSize.x / 0.1f, tracerGlowSize.y / 0.1f, 1f);   // 拉成细长条
            g.transform.localPosition = new Vector3(-tracerGlowSize.x * 0.4f, 0f, 0f);   // 往后挪：拖在弹头后面（子弹朝自己的 +x 飞）
            g.color = new Color(tracerColor.r, tracerColor.g, tracerColor.b, tracerGlow);   // 颜色 + 亮度（透明度）
            g.sortingOrder = bulletSr.sortingOrder - 1;                     // 画在子弹下面一层
            g.sortingLayerID = bulletSr.sortingLayerID;                     // 和子弹同一个排序层
            if (_additive != null) g.sharedMaterial = _additive;            // 叠加发光
            else RenderMats.MakeUnlit(g);                                   // 找不到就用不受光材质
        }
        if (tracerLight > 0f)                                               // 旧管线（没有 2D 光照）时这里返回 null，不影响
            WorldLighting.CreatePoint("TracerLight", bullet.transform, tracerColor, tracerLight, tracerRadius, 360f);
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
        if (canRicochet && impactFx) HitFx.Metal(to, impactScale * 0.8f, metalLife, metalLightColor, metalLight, metalLightRadius, ImpactLayer());   // 没打中、落到地上：火花 + 一点光
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
        string w = IsRifle ? "Weapons/Rifle" : "Weapons/Pistol";            // 手枪 / 步枪各有两种枪火
        Sprite s = Load(w + (Random.value < 0.5f ? "Flash1" : "Flash2"));
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
            float k = Mathf.Max(0.05f, IsRifle ? rifleFlashSize : flashSize) * size, fy = left ? -1f : 1f;   // 朝左：上下翻（配合转向 = 左右镜像）
            StartCoroutine(FxLife(sr, IsRifle ? rifleFlashTime : flashTime, new Vector3(k * 0.9f, k * 0.9f * fy, 1f), new Vector3(k * 1.05f, k * 1.05f * fy, 1f), 0.7f));
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
        bool pOn = gunLight && _held == "pistol" && gunLightIntensity > 0f;
        bool rOn = gunLight && _held == "rifle" && _rifleLightOn && gunLightIntensity > 0f;   // 步枪：取出动画播完才开
        _lightOnT += Time.deltaTime;
        float u = _lightOnT / 0.4f;                                         // 步枪手电刚打开：闪一下再亮稳
        float flick = !IsRifle || u >= 1f ? 1f : (u < 0.12f ? 1f : u < 0.22f ? 0.1f : u < 0.32f ? 1f : u < 0.4f ? 0.3f : 1f);
        Color c = gunLightColor; c.a = flick;
        foreach (SpriteRenderer g in new[] { _gunLampGlow, _rifleLampGlow })
        {
            if (g == null) continue;
            g.enabled = g == _gunLampGlow ? pOn : rOn;
            g.color = c;
            g.transform.localScale = Vector3.one * Mathf.Max(0.01f, gunLightGlowSize);
        }
        bool on = pOn || rOn;
        Transform lamp = rOn ? _rifleLamp : _gunLamp;                       // 光从哪把枪的灯头出来
        if (_gunBeam == null) return;
        _gunBeam.enabled = on;
        if (!on) return;
        WorldLighting.SetPoint(_gunBeam, gunLightColor, gunLightIntensity * flick, gunLightRange, gunLightAngle);   // Inspector 里改了马上生效
        Vector3 dir = _muzzle.TransformVector(Vector3.right);               // 枪口朝哪（含朝左翻转、后坐上扬）
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _gunBeam.transform.position = lamp.position;
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

    // 后坐曲线：t = 开枪后过了几秒；rise 秒内从 start 冲到 1，然后按 ret 的快慢回到 0（start = 上一枪还没回正的量，单发时 = 0）
    static float Kick(float t, float rise, float ret, float start)
    {
        rise = Mathf.Max(0.001f, rise); ret = Mathf.Max(0.001f, ret);
        return t < rise ? Mathf.Lerp(start, 1f, t / rise) : Mathf.Exp(-(t - rise) / ret);
    }

    // 二维向量转一个角度（度）
    static Vector2 Rot(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // 选中时在 Scene 窗口画标记（方便自己调）：黄点 = 手枪绕着转的"肩膀"（Aim Shoulder），绿点 = 手枪握把；青点 = 步枪枪托（Rifle Shoulder）
    void OnDrawGizmosSelected()
    {
        bool left = _rig != null && _rig.lossyScale.x < 0f;                 // 运行时朝左就左右翻
        Vector3 root = transform.position + new Vector3(0f, FEET_Y, 0f);    // 脚底中心
        System.Func<Vector2, Vector3> W = p => root + new Vector3(left ? -p.x : p.x, p.y, 0f);   // 本地 → 世界
        Vector2 grip = CanvasToLocal(P_GRIP - P_ROOT), rgrip = CanvasToLocal(P_RIFLE_GRIP - P_ROOT);
        Gizmos.color = Color.yellow; Gizmos.DrawSphere(W(aimShoulder), 0.012f);
        Gizmos.color = Color.green; Gizmos.DrawSphere(W(grip), 0.012f); Gizmos.DrawLine(W(aimShoulder), W(grip));
        Gizmos.color = Color.cyan; Gizmos.DrawSphere(W(rifleShoulder), 0.012f); Gizmos.DrawLine(W(rifleShoulder), W(rgrip));   // 步枪：青点 = 枪托（肩膀），连到握把
    }

    // ============================================================================
    // 换弹（手枪 / 步枪；战术 / 快速 / 普通）：时间、姿势、参数照搬第一种 NPC；步枪用自己的弹匣、姿势（"步枪换弹"一栏）。
    // 不同的是新弹匣从胸挂上的手枪弹匣袋拿（不是身后）：右手伸到弹匣袋，袋里的弹匣变到手里，胸挂上就少一个。
    // ============================================================================

    // 某种换弹一共要多少"标准时间"（和第一种 NPC 的算法一样；普通换弹多一段"从放旧弹匣的袋子移到拿新弹匣的袋子"）
    public float ReloadTotal(ReloadKind kind)
    {
        if (IsRifle && kind == ReloadKind.Tactical) return RifleTacticalTotal();   // 步枪战术换弹：单独一套时间
        if (IsRifle && kind == ReloadKind.Speed) return RifleSpeedTotal();         // 步枪快速换弹：单独一套时间
        if (IsRifle) return RifleNormalTotal();                                    // 步枪普通换弹：单独一套时间
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

    // 外部调用（ABA 清除小组 AbaOperative 用）：现在手上拿的是什么（null = 空手 / "pistol" / "rifle" / "dagger"）
    public string Held => _held;
    // 外部调用：正在播步枪取出动画（这时不能开枪）
    public bool IsDrawing => _drawT >= 0f;
    // 外部调用：换手（和 Test Held 选一样；已经拿着就不重来；还没拼好时不做）
    public void Hold(string key) { if (_built && key != _held) SetHeld(key); }

    // 现在这把枪的弹匣袋状态 / 拿弹匣的顺序（手枪、步枪各 4 个袋子，各自算）
    int[] Slots => IsRifle ? _rSlotState : _slotState;
    int[] PouchOrder => IsRifle ? R_POUCH_ORDER : POUCH_ORDER;

    // 胸挂上还有几个这把枪的满弹匣
    public int FullMagsOnRig { get { int n = 0; foreach (int s in Slots) if (s == SLOT_FULL) n++; return n; } }

    // 外部调用：想用 want 这种换弹，实际能用哪种。战术 / 普通要把旧弹匣插回胸挂的空袋，没有空袋就只能快速换弹（丢掉旧弹匣）
    public ReloadKind ResolveReloadKind(ReloadKind want)
    {
        if (want != ReloadKind.Speed && FindSlot(SLOT_NONE) < 0) return ReloadKind.Speed;
        return want;
    }

    // 外部调用：拿枪时换一次弹匣。fitSeconds > 0：整段动画压缩 / 拉长到正好这么多秒；0 = 按调的时间 × Reload Speed 播（测试用）
    public void PlayReload(float fitSeconds, ReloadKind kind)
    {
        if (_dead) return;                                                  // 死了
        if (!IsGun || _drawT >= 0f) return;                                 // 没拿枪 / 正在取出步枪就不换
        bool r = IsRifle;
        if (FindSlot(SLOT_FULL) < 0) RefillMags(r);                         // 胸挂上没有满弹匣了：测试先自动补满（游戏规则之后接）
        _rlHandSprite.sprite = Load(r ? "Weapons/RifleHandR" : "Weapons/PistolHandR");   // 换弹用的手 / 弹匣换成这把枪的图
        _rlNewMag.sprite = _rlOldMag.sprite = Load(r ? "Weapons/RifleMag" : "Weapons/PistolMag");
        _rlHandSprite.sortingOrder = r ? 48 : 45;                           // 手在枪的所有部件前面
        _rlNewMag.sortingOrder = r ? 42 : 39;                               // 弹匣在枪身下面（像插在弹匣口里）
        _rlOldMag.sortingOrder = r ? 41 : 38;
        kind = ResolveReloadKind(kind);                                     // 没有空袋放旧弹匣：改成快速换弹
        _takeSlot = FindSlot(SLOT_FULL);                                    // 从哪个袋子拿新弹匣（从最右边的满弹匣开始拿）
        _stowSlot = kind == ReloadKind.Speed ? -1 : FindSlot(SLOT_NONE);    // 旧弹匣插回哪个空袋（快速换弹直接丢掉）
        _reloadKind = kind;
        lastReloadKind = kind.ToString();                                   // Inspector 里显示这次实际用的是哪种
        _qrDropped = false;                                                 // 旧弹匣还在枪里
        _magTaken = false;                                                  // 新弹匣还在袋里
        _oldStowed = false;                                                 // 旧弹匣还没插回去
        _reloadRate = fitSeconds > 0f ? ReloadTotal(kind) / fitSeconds : Mathf.Max(0.05f, reloadSpeed);   // 步枪三种换弹都有自己的时间
        _reloadT = 0f;                                                      // 从头开始
        _recoilT = -1f;                                                     // 停掉后坐
    }

    // 换弹结束 / 被打断：所有东西放回原样
    void StopReload()
    {
        _reloadT = -1f;
        if (_handPoint != null) _handPoint.localScale = Vector3.one;        // 放大抖动复原
        if (_rlHandSprite != null) _rlHandSprite.enabled = false;           // 换弹用的右手、两个弹匣藏起来
        if (_rlNewMag != null) _rlNewMag.enabled = false;
        if (_rlOldMag != null) _rlOldMag.enabled = false;
        bool p = _held == "pistol", r = _held == "rifle";
        if (_gunHandR != null) _gunHandR.enabled = p;                       // 枪上的手、枪里的弹匣显示回来
        if (_gunMag != null) _gunMag.enabled = p;
        if (_rifleHandR != null) _rifleHandR.enabled = r;
        if (_rifleMag != null) _rifleMag.enabled = r;
        if (_slide != null) _slide.localPosition = _slideBase;              // 滑套 / 拉栓组复位
        if (_rifleCharger != null) _rifleCharger.localPosition = _rifleChargerBase;
        if (_rifleBolt != null) _rifleBolt.localPosition = _rifleBoltBase;
    }

    // ---------------- 胸挂弹匣袋：4 个袋子，每个是"空袋 / 满弹匣 / 空弹匣"之一（一共 5 个弹匣 = 枪里 1 个 + 胸挂 4 个）----------------

    // 按拿的顺序（从最右边开始）找第一个是 state 的袋子；没有返回 -1
    int FindSlot(int state)
    {
        int[] slots = Slots;
        foreach (int i in PouchOrder) if (slots[i] == state) return i;
        return -1;
    }

    // 胸挂弹匣全部补满（测试用；rifle = 步枪的，否则手枪的）
    void RefillMags(bool rifle)
    {
        int[] slots = rifle ? _rSlotState : _slotState;
        for (int i = 0; i < 4; i++) slots[i] = SLOT_FULL;
        ApplySlots(rifle);
    }

    // 按每个袋子的状态显示 / 藏起胸挂上的弹匣（空弹匣插回去也显示，样子和满的一样）
    void ApplySlots(bool rifle)
    {
        string[] names = { "空袋", "满", "空弹匣" };
        int[] slots = rifle ? _rSlotState : _slotState;
        SpriteRenderer[] mags = rifle ? _mountRifleMags : _mountMags;
        string text = "";
        foreach (int i in rifle ? R_POUCH_ORDER : POUCH_ORDER)              // 从右到左
        {
            if (mags[i] != null) mags[i].enabled = slots[i] != SLOT_NONE;
            text += (text.Length > 0 ? " / " : "") + names[slots[i]];
        }
        if (rifle) rigRifleMags = text; else rigMags = text;                // Inspector 里显示（从右到左）
    }

    // 右手在某个袋子插 / 拔弹匣的姿势（根节点本地坐标）：让手里的弹匣正好和袋子里的弹匣重合（胸挂晃它也跟着）
    //   magInHand / magTilt = 这个弹匣在手里的位置 / 歪多少（新弹匣正拿着；战术换弹的旧弹匣摆成 V 字）
    //   pos = 插到底时手在哪，outPos = 拔出来 / 准备插进去时手在哪，ang = 手转多少度
    void PouchPose(int slot, Vector2 magInHand, float magTilt, out Vector2 pos, out Vector2 outPos, out float ang)
    {
        bool rifle = IsRifle;
        Transform m = (rifle ? _mountRifleMags : _mountMags)[slot].transform;   // 这个袋子里的弹匣（藏起来也照样能算位置）
        Vector2 bottomCanvas = rifle ? P_RIFLE_MAG_BOTTOM[slot] : P_PISTOL_MAG_BOTTOM[slot];
        Vector2 centerCanvas = rifle ? P_RIFLE_MAG[slot] : P_PISTOL_MAG[slot];
        Vector3 bottom = _rig.InverseTransformPoint(m.TransformPoint(CanvasToLocal(bottomCanvas - centerCanvas)));   // 它的底部
        Vector3 d = _rig.InverseTransformVector(m.TransformVector(Vector3.right));   // 它现在转了多少（含胸挂晃动、身体摇摆）
        float magAng = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + (rifle ? R_POUCH_MAG_TURN : POUCH_MAG_TURN);   // 手里弹匣的图要转多少才和袋里的一样斜
        ang = magAng - magTilt;                                             // 弹匣在手里歪着的话，手要反过来转
        pos = new Vector2(bottom.x, bottom.y) - Rot(magInHand, ang);        // 手的位置 = 弹匣底部 - 弹匣在手里的位置
        float r = (magAng + (rifle ? R_MAG_AXIS : MAG_AXIS)) * Mathf.Deg2Rad;   // 沿着弹匣的方向往外拔
        outPos = pos + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * (rifle ? rrPullOut : rlPullOut);
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
        // 这把枪（手枪 / 步枪）自己的弹匣位置、方向和换弹姿势
        bool rifle = IsRifle;
        Vector2 magCanvas = rifle ? P_R_MAG : P_GUN_MAG;                    // 枪里弹匣底部（原图）
        Vector2 magDir = rifle ? R_MAG_DIR : MAG_DIR;                       // 弹匣插进去的方向
        Vector2 magInHand = rifle ? rrMagInHand : rlMagInHand;              // 弹匣底部在手里的位置
        float readyD = rifle ? rrReady : rlReady, gapD = rlUnderGap;        // （步枪三种换弹都走自己的一套，这里实际只给手枪用）
        Vector2 gunRaiseV = rlGunRaise;
        float gunTiltV = rlGunTilt, gunAlignV = rlGunAlign;
        SpriteRenderer gunHandR = rifle ? _rifleHandR : _gunHandR;          // 枪上那只要去拿弹匣的手
        SpriteRenderer gunMag = rifle ? _rifleMag : _gunMag;                // 枪里的弹匣
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
        float align = t < b7 ? gunAlignV * Ease(Seg(t, b5, rlAlignTime))                                       // ⑥ 转过去
                             : gunAlignV * (1f - Ease(Seg(t, b7, rlReleaseTime)));                             // ⑧ 转回来
        float slideAt = b7 + Mathf.Max(0f, rlSlideDelay);                                                       // 滑套复位的时刻
        float shakeDrop = Shake(t - dropAt, rlDropTime);                                                        // 旧弹匣掉出来时：上下 + 旋转抖
        float shakeInsert = Shake(t - b7, rlReleaseTime);                                                       // ⑦ 新弹匣插到底：上下抖
        float shakeSlide = Shake(t - slideAt, rlReleaseTime);                                                   // ⑧ 滑套复位：旋转抖
        float aimNow = _aimAng * (1f - raise);                              // 瞄准角度：① 抬枪时转回正前方，⑩ 放下时转回去对准
        Vector2 gunPos = AimHand(aimNow, Vector2.zero, 1f - raise) + gunRaiseV * raise + new Vector2(0f, rlShake * (shakeDrop + shakeInsert));
        float gunAng = aimNow + gunTiltV * raise + align + rlShakeAngle * (shakeDrop + shakeSlide);  // 抬枪时的旋转 + ⑥ 配合转 + 旋转抖
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);

        // ---- 2. 滑套：① 空仓挂机，⑧ 复位 ----
        float slideK = t < slideAt ? Ease(t / Mathf.Max(0.001f, rlLockTime)) : 1f - Seg(t, slideAt, rlReleaseTime * 0.4f);   // 到复位时刻才弹回去
        if (rifle)                                                          // 步枪：上弹器 / 拉栓挂在后面，插好新弹匣后弹回来
        {
            if (_rifleCharger != null) _rifleCharger.localPosition = _rifleChargerBase + new Vector3(-rifleChargerKick * rlSlideLock * slideK / PPU_FULL, 0f, 0f);
            if (_rifleBolt != null) _rifleBolt.localPosition = _rifleBoltBase + new Vector3(-rifleBoltKick * rlSlideLock * slideK / PPU_FULL, 0f, 0f);
        }
        else if (_slide != null) _slide.localPosition = _slideBase + new Vector3(-slideKick * rlSlideLock * slideK / PPU_FULL, 0f, 0f);

        // ---- 3. 右手的几个关键姿势（位置 + 角度） ----
        Vector2 magIn = (Vector2)CanvasToLocal(magCanvas - GripCanvas);     // 枪里弹匣底部（枪的本地坐标）
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);   // 枪的本地坐标 → 根节点坐标
        Vector2 restPos = G2R(rifle ? (Vector2)CanvasToLocal(P_R_HAND_R - P_RIFLE_GRIP) : Vector2.zero); float restAng = gunAng;   // 平时的位置（手枪 = 握把，步枪前手 = 护木）
        Vector2 oldInHand = normal ? magInHand : magInHand + rlOldOffset;   // 旧弹匣在手里的位置（战术：V 字；普通：正拿着）
        float oldTiltInHand = normal ? 0f : rlOldTilt;                      // 旧弹匣在手里歪多少
        PouchPose(_takeSlot, magInHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);   // 拔新弹匣的满袋
        Vector2 stowPos = takePos, stowOut = takeOut; float stowAng = takeAng;
        if (_stowSlot >= 0) PouchPose(_stowSlot, oldInHand, oldTiltInHand, out stowPos, out stowOut, out stowAng);   // 插回旧弹匣的空袋
        float underAng = gunAng;                                            // ④ 握把下方：新弹匣竖直对着握把口
        Vector2 underPos = G2R(magIn - magDir * (readyD + gapD)) - Rot(magInHand, underAng);
        if (quick) underPos += Rot(qrWait, underAng);                       // 快速换弹：往旁边让开（默认不让）
        float tilt = quick || normal ? 0f : rlNewTilt;                      // 新弹匣在手里歪多少（只有战术换弹要摆 V 字）
        float readyAng = gunAng - tilt;                                     // ⑥⑦ 新弹匣歪着的角度抵消掉，正好对准
        float depth = t < b6 ? readyD : Mathf.Lerp(readyD, 0f, Ease(Seg(t, b6, rlInsertTime)));   // ⑦ 往里插
        Vector2 readyPos = G2R(magIn - magDir * depth) - Rot(magInHand, readyAng);
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
        if (showNew && !_magTaken) { _magTaken = true; Slots[_takeSlot] = SLOT_NONE; ApplySlots(IsRifle); }
        bool stowedNow = _stowSlot >= 0 && t >= (normal ? c4a : b8 - 0.0001f);
        if (stowedNow && !_oldStowed) { _oldStowed = true; Slots[_stowSlot] = SLOT_EMPTY; ApplySlots(IsRifle); showOld = false; }

        // ---- 4. 摆右手和两个弹匣（右手在胸挂前面拿 / 放，不用绕到身后；图层：旧弹匣 38 < 新弹匣 39 < 枪里的弹匣 40 … < 右手 45）----
        if (gunHandR != null) gunHandR.enabled = !reloadHand;             // 枪上的右手 ↔ 换弹用的右手
        _rlHandSprite.enabled = reloadHand;
        _rlHand.localPosition = new Vector3(hp.x, hp.y, 0f);
        _rlHand.localRotation = Quaternion.Euler(0f, 0f, ha);

        _rlNewMag.enabled = showNew;                                        // 新弹匣：底部在手里
        Vector2 nb = hp + Rot(magInHand, ha);
        _rlNewMag.transform.localPosition = new Vector3(nb.x, nb.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha + newTilt);
        if (gunMag != null) gunMag.enabled = !(t >= dropAt && t < b7);    // 旧弹匣离开后枪里没弹匣，插好了再显示

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
            Vector2 op = G2R(magIn - magDir * s);                          // 弹匣底部现在的位置
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
                Vector2 vel = Rot(-magDir, gunAng) * v + new Vector2(RandIn(qrKick), 0f);   // 顺着握把的方向 + 往前 / 往后飘一点
                DropMag(op, gunAng, vel, RandIn(qrSpin));
            }
        }
    }

    // ============================================================================
    // 步枪战术换弹（单独一套，按你描述的动作）：
    //   ① 左手（握把手）连枪往后收、枪口往上抬到 Rt Gun Up（约 80°），同时右手（前手）去胸挂拔新弹匣
    //   ② 右手拿着新弹匣抓住旧弹匣、拔出来 → ③ 新弹匣对准、插上（枪抖一下）
    //   ④ 右手把旧弹匣插回胸挂空袋，同时枪口往下到 Rt Gun Mid（约 45°）
    //   ⑤ 步枪突然放大一下再弹回（模拟往镜头方向的抖动），同时拉栓组弹回去
    //   ⑥ 右手插回旧弹匣后马上往回走，枪口放平的同时正好抓住护木，抓住的一下枪身抖一下（有力量感）
    // ============================================================================

    // 各段的时间点（标准时间，秒）
    void RifleTacticalTimes(out float tGrabbed, out float t1, out float t2, out float t3, out float t4, out float t5,
                            out float t6, out float t7, out float t8, out float t9, out float t10, out float t11, out float t12)
    {
        tGrabbed = rtFetchTime + rtGrabTime;                               // 新弹匣拔出来了
        t1 = Mathf.Max(rtRaiseTime, tGrabbed);                             // 枪抬好 + 新弹匣在手里
        t2 = t1 + rtBringTime;                                             // 右手抓住旧弹匣
        t3 = t2 + rtPullOldTime;                                           // 旧弹匣拔出来
        t4 = t3 + rtAlignTime;                                             // 新弹匣对准
        t5 = t4 + rtInsertTime;                                            // 新弹匣插到底
        t6 = t5 + rtStowTime;                                              // 旧弹匣到空袋口（枪口同时往下到 45°）
        t7 = t6 + rtPushTime;                                              // 旧弹匣插进袋里 → 放大抖动 + 拉栓组弹回
        t8 = t7 + rtZoomTime;                                              // 放大抖动结束
        t9 = t8 + rtLevelTime;                                             // 枪口放平
        t10 = t7;                                                          // 右手插好旧弹匣就开始往回走（不停在胸挂上等）
        t11 = t9;                                                          // 枪口放平的同时右手抓住护木（枪身抖一下）
        t12 = t11 + rtGripShakeTime;                                       // 结束
    }

    // 步枪战术换弹一共多少秒
    float RifleTacticalTotal()
    {
        RifleTacticalTimes(out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out float t12);
        return Mathf.Max(0.05f, t12);
    }

    // 放大抖动曲线：一下子放大到 1，然后边回弹边衰减（e = 开始后几秒）
    float ZoomPop(float e)
    {
        if (e < 0f || e > rtZoomTime) return 0f;
        const float rise = 0.03f;                                          // 0.03 秒内冲到最大
        if (e < rise) return e / rise;
        float d = e - rise;
        return Mathf.Exp(-d * 14f) * Mathf.Cos(d * 2f * Mathf.PI * 6f);    // 回弹几下就停
    }

    void AnimateRifleTactical(float dt)
    {
        _reloadT += dt * _reloadRate;                                      // 往前走
        float t = _reloadT;
        RifleTacticalTimes(out float tg, out float t1, out float t2, out float t3, out float t4, out float t5,
                           out float t6, out float t7, out float t8, out float t9, out float t10, out float t11, out float t12);
        if (t >= t12) { StopReload(); ApplyAimPose(); return; }            // 播完了：回到平时的样子

        // ---- 1. 枪：后收 + 抬到 80° → 降到 45° → 放大抖动 → 放平；抖动 ----
        float up = rtGunUp * Ease(Seg(t, 0f, rtRaiseTime));               // ① 抬起来
        if (t >= t5) up = Mathf.Lerp(rtGunUp, rtGunMid, Ease(Seg(t, t5, t7 - t5)));   // ④ 插回旧弹匣的同时往下到 45°
        if (t >= t8) up = Mathf.Lerp(rtGunMid, 0f, Ease(Seg(t, t8, rtLevelTime)));    // ⑥ 放平
        float back = t < t8 ? Ease(Seg(t, 0f, rtRaiseTime)) : 1f - Ease(Seg(t, t8, rtLevelTime));   // 往后收的程度（放平时收回来）
        float aimNow = _aimAng * (1f - back);                              // 瞄准角度：抬枪时转回正前方，放平时转回去对准
        float shIns = Shake(t - t5, rlReleaseTime);                        // ③ 新弹匣插到底：抖一下
        float shZoom = Shake(t - t7, rtZoomTime);                          // ⑤ 放大抖动时也带一点旋转
        float shGrip = Shake(t - t11, rtGripShakeTime);                    // ⑥ 右手抓住护木：枪身抖一下
        Vector2 gunPos = AimHand(aimNow, Vector2.zero, 1f - back) + rtGunBack * back
                       + new Vector2(0f, rlShake * 0.5f * shIns + rtGripShakeY * shGrip);
        float gunAng = aimNow + up + rtInsertShake * shIns + rtZoomShake * shZoom + rtGripShake * shGrip;
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
        _handPoint.localScale = Vector3.one * (1f + rtZoom * ZoomPop(t - t7));   // ⑤ 突然放大一下（模拟往镜头方向抖）

        // ---- 2. 拉栓组：一开始挂到后面，⑤ 放大抖动的同时弹回去 ----
        float lockK = t < t7 ? Ease(Seg(t, 0f, rtRaiseTime)) : 1f - Seg(t, t7, rtBoltReturnTime);
        if (_rifleCharger != null) _rifleCharger.localPosition = _rifleChargerBase + new Vector3(-rifleChargerKick * rlSlideLock * lockK / PPU_FULL, 0f, 0f);
        if (_rifleBolt != null) _rifleBolt.localPosition = _rifleBoltBase + new Vector3(-rifleBoltKick * rlSlideLock * lockK / PPU_FULL, 0f, 0f);

        // ---- 3. 右手的几个关键姿势 ----
        Vector2 magIn = (Vector2)CanvasToLocal(P_R_MAG - P_RIFLE_GRIP);    // 枪里弹匣底部（枪的本地坐标）
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);  // 枪的本地坐标 → 根节点坐标
        Vector2 restPos = G2R(CanvasToLocal(P_R_HAND_R - P_RIFLE_GRIP)); float restAng = gunAng;   // 右手平时握着护木
        Vector2 oldInHand = rrMagInHand + rtOldOffset;                     // 旧弹匣在手里的位置（和新弹匣并排）
        PouchPose(_takeSlot, rrMagInHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);   // 拔新弹匣的满袋
        Vector2 stowPos = takePos, stowOut = takeOut; float stowAng = takeAng;
        if (_stowSlot >= 0) PouchPose(_stowSlot, oldInHand, rtOldTilt, out stowPos, out stowOut, out stowAng);   // 插回旧弹匣的空袋
        float grabAng = gunAng - rtOldTilt;                                // ② 抓旧弹匣：手里"旧弹匣的位置"正好对上枪里的弹匣
        Vector2 grabPos = G2R(magIn) - Rot(oldInHand, grabAng);
        Vector2 pulledPos = grabPos + Rot(-R_MAG_DIR, gunAng) * rtPullOldDist;   // 沿弹匣方向拔出来
        float readyAng = gunAng - rtNewTilt;                               // ③ 新弹匣歪着的角度抵消掉，正好对准
        float depth = t < t4 ? rrReady : Mathf.Lerp(rrReady, 0f, Ease(Seg(t, t4, rtInsertTime)));
        Vector2 readyPos = G2R(magIn - R_MAG_DIR * depth) - Rot(rrMagInHand, readyAng);
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));

        Vector2 hp; float ha; float newTilt = 0f;
        if (t < rtFetchTime)                                               // ① 伸到满袋
        {
            float e = Ease(Seg(t, 0f, rtFetchTime));
            hp = Vector2.Lerp(restPos, takePos, e) + Arc(e); ha = Mathf.LerpAngle(restAng, takeAng, e);
        }
        else if (t < tg) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, rtFetchTime, rtGrabTime))); ha = takeAng; }   // ① 拔出新弹匣
        else if (t < t1) { hp = takeOut; ha = takeAng; }                   // 等枪抬好
        else if (t < t2)                                                   // ② 拿着新弹匣去抓旧弹匣（新弹匣慢慢歪到并排的角度）
        {
            float e = Ease(Seg(t, t1, rtBringTime));
            hp = Vector2.Lerp(takeOut, grabPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, grabAng, e);
            newTilt = rtNewTilt * e;
        }
        else if (t < t3) { hp = Vector2.Lerp(grabPos, pulledPos, Ease(Seg(t, t2, rtPullOldTime))); ha = grabAng; newTilt = rtNewTilt; }   // ② 拔出旧弹匣
        else if (t < t4)                                                   // ③ 新弹匣对准弹匣口
        {
            float e = Ease(Seg(t, t3, rtAlignTime));
            hp = Vector2.Lerp(pulledPos, readyPos, e); ha = Mathf.LerpAngle(grabAng, readyAng, e); newTilt = rtNewTilt;
        }
        else if (t < t5) { hp = readyPos; ha = readyAng; newTilt = rtNewTilt; }   // ③ 插进去
        else if (t < t6)                                                   // ④ 带着旧弹匣到空袋口
        {
            float e = Ease(Seg(t, t5, rtStowTime));
            hp = Vector2.Lerp(readyPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(readyAng, stowAng, e);
        }
        else if (t < t7) { hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, t6, rtPushTime))); ha = stowAng; }   // ④ 插进袋里
        else                                                               // ⑤⑥ 右手往回走，枪放平时正好抓住护木
        {
            float e = Ease(Seg(t, t10, t11 - t10));
            hp = Vector2.Lerp(stowPos, restPos, e) + Arc(e); ha = Mathf.LerpAngle(stowAng, restAng, e);
        }

        // 胸挂：新弹匣一到手里那个袋子就空了；旧弹匣插到底，空袋里就多了一个空弹匣
        bool showNew = t >= rtFetchTime && t < t5;
        if (showNew && !_magTaken) { _magTaken = true; _rSlotState[_takeSlot] = SLOT_NONE; ApplySlots(true); }
        if (_stowSlot >= 0 && t >= t7 && !_oldStowed) { _oldStowed = true; _rSlotState[_stowSlot] = SLOT_EMPTY; ApplySlots(true); }

        // ---- 4. 摆手和两个弹匣 ----
        bool regripped = t >= t11;                                         // 右手抓回护木：换回枪上的那只手
        if (_rifleHandR != null) _rifleHandR.enabled = regripped;
        _rlHandSprite.enabled = !regripped;
        _rlHand.localPosition = new Vector3(hp.x, hp.y, 0f);
        _rlHand.localRotation = Quaternion.Euler(0f, 0f, ha);
        _rlNewMag.enabled = showNew;                                       // 新弹匣：底部在手里
        Vector2 nb = hp + Rot(rrMagInHand, ha);
        _rlNewMag.transform.localPosition = new Vector3(nb.x, nb.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha + newTilt);
        if (_rifleMag != null) _rifleMag.enabled = !(t >= t2 && t < t5);   // 旧弹匣被拔走后枪里没弹匣，插好了再显示
        bool showOld = t >= t2 && t < t7 && !_oldStowed;                   // 旧弹匣：被抓住 ~ 插回袋里
        _rlOldMag.enabled = showOld;
        if (showOld)
        {
            Vector2 ob = hp + Rot(oldInHand, ha);
            _rlOldMag.transform.localPosition = new Vector3(ob.x, ob.y, 0f);
            _rlOldMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha + rtOldTilt);
        }
    }

    // ============================================================================
    // 步枪快速换弹（单独一套，AR 式）：
    //   ① 按弹匣释放、枪往下一抖：枪托先往下（绕握把转），再带着整把枪往下一沉，沉到底时旧弹匣掉下去；同时右手去胸挂拔新弹匣
    //   ② 抖完枪口往上抬 Rs Gun Up（约 30°）、往后收 Rs Gun Back
    //   ③ 右手拔出弹匣直接往弹匣口里插（不停下来对准），插到底枪上下抖一下（力量感），右手插完直接抓回护木
    //   ④ 放大抖动 + 拉栓组弹回 → 枪口放平，放平的一下枪身抖一下
    //   （拔弹匣、送弹匣、插入、放大抖动、放平、放平抖动的参数和战术换弹共用，调一处两边都变）
    // ============================================================================

    // 各段的时间点（标准时间，秒）
    void RifleSpeedTimes(out float tDrop, out float tGrabbed, out float t1, out float t2, out float t3, out float tGrip,
                         out float t5, out float t6, out float t7, out float t8)
    {
        tDrop = rsFlickTime * 0.7f;                                        // 整把枪沉到最低点：旧弹匣脱手
        tGrabbed = rtFetchTime + rtGrabTime;                               // 新弹匣拔出来了
        t1 = Mathf.Max(rsFlickTime + rsRaiseTime, tGrabbed);               // 枪抬好 + 新弹匣在手里
        t2 = t1 + rtBringTime;                                             // 新弹匣送到弹匣口
        t3 = t2 + rtInsertTime;                                            // 插到底（枪上下抖一下）
        tGrip = t3 + rsRegripTime;                                         // 右手抓回护木
        t5 = t3 + rsSettleTime;                                            // 放大抖动 + 拉栓组弹回
        t6 = t5 + rtZoomTime;                                              // 放大抖动结束，开始放平
        t7 = t6 + rtLevelTime;                                             // 枪口放平（枪身抖一下）
        t8 = Mathf.Max(t7 + rtGripShakeTime, tGrip);                       // 结束
    }

    // 步枪快速换弹一共多少秒
    float RifleSpeedTotal()
    {
        RifleSpeedTimes(out _, out _, out _, out _, out _, out _, out _, out _, out _, out float t8);
        return Mathf.Max(0.05f, t8);
    }

    // 0 → 1 → 0 的一个鼓包：在 [a, a + len] 里，中间最大
    static float Bump(float t, float a, float len) { return len > 0f && t > a && t < a + len ? Mathf.Sin(Mathf.PI * (t - a) / len) : 0f; }

    void AnimateRifleSpeed(float dt)
    {
        _reloadT += dt * _reloadRate;                                      // 往前走
        float t = _reloadT;
        RifleSpeedTimes(out float tDrop, out float tg, out float t1, out float t2, out float t3, out float tGrip,
                        out float t5, out float t6, out float t7, out float t8);
        if (t >= t8) { StopReload(); ApplyAimPose(); return; }             // 播完了：回到平时的样子

        // ---- 1. 枪：枪托往下 → 整把枪往下沉 → 抬到 30° + 往后收 → 放大抖动 → 放平；抖动 ----
        float stockDown = Bump(t, 0f, rsFlickTime * 0.6f);                 // ① 先：枪托往下（绕握把转，枪口往上翘）
        float sink = Bump(t, rsFlickTime * 0.3f, rsFlickTime * 0.7f);      // ① 后：整把枪往下一沉
        float raiseK = Ease(Seg(t, rsFlickTime, rsRaiseTime));             // ② 抬起来的程度
        float back = t < t6 ? raiseK : 1f - Ease(Seg(t, t6, rtLevelTime));  // 往后收的程度（放平时收回来）
        float up = rsGunUp * back;                                         // 枪口往上抬（放平时一起放下）
        float aimNow = _aimAng * (1f - back);                              // 瞄准角度：抬枪时转回正前方，放平时转回去对准
        float shIns = Shake(t - t3, rsInsertShakeTime);                    // ③ 插到底：上下抖一下（力量感）
        float shZoom = Shake(t - t5, rtZoomTime);                          // ④ 放大抖动时也带一点旋转
        float shLevel = Shake(t - t7, rtGripShakeTime);                    // ④ 放平的一下：枪身抖一下
        Vector2 gunPos = AimHand(aimNow, Vector2.zero, 1f - back) + rsGunBack * back
                       + new Vector2(0f, -rsFlickY * sink + rsInsertShakeY * shIns + rtGripShakeY * shLevel);
        float gunAng = aimNow + up + rsFlickAngle * stockDown + rtZoomShake * shZoom + rtGripShake * shLevel;
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
        _handPoint.localScale = Vector3.one * (1f + rtZoom * ZoomPop(t - t5));   // ④ 突然放大一下（模拟往镜头方向抖）

        // ---- 2. 拉栓组：打空了挂在后面，放大抖动的同时弹回去 ----
        float lockK = t < t5 ? Ease(Seg(t, 0f, rsFlickTime)) : 1f - Seg(t, t5, rtBoltReturnTime);
        if (_rifleCharger != null) _rifleCharger.localPosition = _rifleChargerBase + new Vector3(-rifleChargerKick * rlSlideLock * lockK / PPU_FULL, 0f, 0f);
        if (_rifleBolt != null) _rifleBolt.localPosition = _rifleBoltBase + new Vector3(-rifleBoltKick * rlSlideLock * lockK / PPU_FULL, 0f, 0f);

        // ---- 3. 旧弹匣：整把枪沉到最低点时脱手，自己掉下去（下落参数用"换弹 2：快速换弹"那几个）----
        Vector2 magIn = (Vector2)CanvasToLocal(P_R_MAG - P_RIFLE_GRIP);    // 枪里弹匣底部（枪的本地坐标）
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);  // 枪的本地坐标 → 根节点坐标
        if (t >= tDrop && !_qrDropped)
        {
            _qrDropped = true;
            Vector2 vel = Rot(-R_MAG_DIR, gunAng) * qrSlideSpeed + new Vector2(RandIn(qrKick), -rsFlickY * 6f);   // 顺着弹匣口往下 + 被抖下去的速度
            DropMag(G2R(magIn), gunAng, vel, RandIn(qrSpin) * 0.5f);       // 步枪弹匣重，转得慢一点
        }

        // ---- 4. 右手：拔新弹匣 → 直接插进弹匣口 → 抓回护木 ----
        Vector2 restPos = G2R(CanvasToLocal(P_R_HAND_R - P_RIFLE_GRIP)); float restAng = gunAng;   // 右手平时握着护木
        PouchPose(_takeSlot, rrMagInHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);   // 拔新弹匣的满袋
        float readyAng = gunAng;                                           // 新弹匣正对弹匣口
        float depth = t < t2 ? rrReady : Mathf.Lerp(rrReady, 0f, Ease(Seg(t, t2, rtInsertTime)));
        Vector2 readyPos = G2R(magIn - R_MAG_DIR * depth) - Rot(rrMagInHand, readyAng);   // 弹匣口（往里插）
        Vector2 seatedPos = G2R(magIn) - Rot(rrMagInHand, readyAng);       // 插到底时手在哪
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));

        Vector2 hp; float ha;
        if (t < rtFetchTime)                                               // ① 伸到满袋（和往下抖同时）
        {
            float e = Ease(Seg(t, 0f, rtFetchTime));
            hp = Vector2.Lerp(restPos, takePos, e) + Arc(e); ha = Mathf.LerpAngle(restAng, takeAng, e);
        }
        else if (t < tg) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, rtFetchTime, rtGrabTime))); ha = takeAng; }   // ① 拔出新弹匣
        else if (t < t1) { hp = takeOut; ha = takeAng; }                   // 等枪抬好
        else if (t < t2)                                                   // ③ 拿着弹匣直接送到弹匣口
        {
            float e = Ease(Seg(t, t1, rtBringTime));
            hp = Vector2.Lerp(takeOut, readyPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, readyAng, e);
        }
        else if (t < t3) { hp = readyPos; ha = readyAng; }                 // ③ 插进去
        else                                                               // ③ 插完直接抓回护木
        {
            float e = Ease(Seg(t, t3, rsRegripTime));
            hp = Vector2.Lerp(seatedPos, restPos, e); ha = Mathf.LerpAngle(readyAng, restAng, e);
        }

        // 胸挂：新弹匣一到手里那个袋子就空了（旧弹匣丢在地上，不放回去）
        bool showNew = t >= rtFetchTime && t < t3;
        if (showNew && !_magTaken) { _magTaken = true; _rSlotState[_takeSlot] = SLOT_NONE; ApplySlots(true); }

        // ---- 5. 摆手和弹匣 ----
        bool regripped = t >= tGrip;                                       // 右手抓回护木：换回枪上的那只手
        if (_rifleHandR != null) _rifleHandR.enabled = regripped;
        _rlHandSprite.enabled = !regripped;
        _rlHand.localPosition = new Vector3(hp.x, hp.y, 0f);
        _rlHand.localRotation = Quaternion.Euler(0f, 0f, ha);
        _rlNewMag.enabled = showNew;                                       // 新弹匣：底部在手里
        Vector2 nb = hp + Rot(rrMagInHand, ha);
        _rlNewMag.transform.localPosition = new Vector3(nb.x, nb.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha);
        if (_rifleMag != null) _rifleMag.enabled = !(t >= tDrop && t < t3);   // 旧弹匣掉了以后枪里没弹匣，插好了再显示
        _rlOldMag.enabled = false;                                         // 旧弹匣不在手里（掉地上了）
    }

    // ============================================================================
    // 步枪普通换弹（单独一套）：
    //   ① 右手先去把旧弹匣抓住、拔下来
    //   ② 枪口抬到 Rn Gun Up（约 80°）、往后收 Rn Gun Back，同时右手把旧弹匣插回胸挂空袋
    //   ③ 右手移到满弹匣的袋子，拔出新弹匣，直接插进弹匣口
    //   装好弹匣以后和战术换弹一样：插到底枪抖一下 → 枪口往下到 45° → 放大抖动 + 拉栓组弹回 → 放平，
    //   右手同时往回走、放平那一刻正好抓住护木，抓住时枪身抖一下（这些参数和战术换弹共用）
    // ============================================================================

    // 各段的时间点（标准时间，秒）
    void RifleNormalTimes(out float tA, out float tB, out float t1, out float t2, out float t3, out float t4,
                          out float t5, out float t6, out float t7, out float t8, out float t9, out float t10)
    {
        tA = rnReachTime;                                                  // 右手抓住旧弹匣
        tB = tA + rtPullOldTime;                                           // 旧弹匣拔下来
        t1 = tB + Mathf.Max(rnRaiseTime, rtStowTime);                      // 枪抬好 + 旧弹匣到空袋口
        t2 = t1 + rtPushTime;                                              // 旧弹匣插进袋里
        t3 = t2 + nrSwitchTime;                                            // 手移到满弹匣的袋子
        t4 = t3 + rtGrabTime;                                              // 拔出新弹匣
        t5 = t4 + rtBringTime;                                             // 送到弹匣口
        t6 = t5 + rtInsertTime;                                            // 插到底（枪抖一下）
        t7 = t6 + rtStowTime + rtPushTime;                                 // 枪口降到 45°（和战术换弹"插回旧弹匣"那段一样长）→ 放大抖动 + 拉栓组弹回
        t8 = t7 + rtZoomTime;                                              // 开始放平
        t9 = t8 + rtLevelTime;                                             // 枪口放平，同时右手抓住护木（枪身抖一下）
        t10 = t9 + rtGripShakeTime;                                        // 结束
    }

    // 步枪普通换弹一共多少秒
    float RifleNormalTotal()
    {
        RifleNormalTimes(out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out float t10);
        return Mathf.Max(0.05f, t10);
    }

    void AnimateRifleNormal(float dt)
    {
        _reloadT += dt * _reloadRate;                                      // 往前走
        float t = _reloadT;
        RifleNormalTimes(out float tA, out float tB, out float t1, out float t2, out float t3, out float t4,
                         out float t5, out float t6, out float t7, out float t8, out float t9, out float t10);
        if (t >= t10) { StopReload(); ApplyAimPose(); return; }            // 播完了：回到平时的样子

        // ---- 1. 枪：拔下旧弹匣后抬到 80° + 往后收 → 装好后降到 45° → 放大抖动 → 放平；抖动（装好以后同战术换弹）----
        float raiseK = Ease(Seg(t, tB, rnRaiseTime));                      // ② 抬起来的程度
        float up = rnGunUp * raiseK;                                       // ② 抬到 80°
        if (t >= t6) up = Mathf.Lerp(rnGunUp, rtGunMid, Ease(Seg(t, t6, t7 - t6)));   // 装好后往下到 45°
        if (t >= t8) up = Mathf.Lerp(rtGunMid, 0f, Ease(Seg(t, t8, rtLevelTime)));    // 放平
        float back = t < t8 ? raiseK : 1f - Ease(Seg(t, t8, rtLevelTime)); // 往后收的程度（放平时收回来）
        float aimNow = _aimAng * (1f - back);                              // 瞄准角度：抬枪时转回正前方，放平时转回去对准
        float shIns = Shake(t - t6, rlReleaseTime);                        // 插到底：枪抖一下（同战术换弹）
        float shZoom = Shake(t - t7, rtZoomTime);                          // 放大抖动时也带一点旋转
        float shGrip = Shake(t - t9, rtGripShakeTime);                     // 右手抓住护木：枪身抖一下
        Vector2 gunPos = AimHand(aimNow, Vector2.zero, 1f - back) + rnGunBack * back
                       + new Vector2(0f, rlShake * 0.5f * shIns + rtGripShakeY * shGrip);
        float gunAng = aimNow + up + rtInsertShake * shIns + rtZoomShake * shZoom + rtGripShake * shGrip;
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
        _handPoint.localScale = Vector3.one * (1f + rtZoom * ZoomPop(t - t7));   // 突然放大一下（模拟往镜头方向抖）

        // ---- 2. 拉栓组：打空了挂在后面，放大抖动的同时弹回去 ----
        float lockK = t < t7 ? Ease(Seg(t, 0f, rnReachTime)) : 1f - Seg(t, t7, rtBoltReturnTime);
        if (_rifleCharger != null) _rifleCharger.localPosition = _rifleChargerBase + new Vector3(-rifleChargerKick * rlSlideLock * lockK / PPU_FULL, 0f, 0f);
        if (_rifleBolt != null) _rifleBolt.localPosition = _rifleBoltBase + new Vector3(-rifleBoltKick * rlSlideLock * lockK / PPU_FULL, 0f, 0f);

        // ---- 3. 右手的几个关键姿势 ----
        Vector2 magIn = (Vector2)CanvasToLocal(P_R_MAG - P_RIFLE_GRIP);    // 枪里弹匣底部（枪的本地坐标）
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);  // 枪的本地坐标 → 根节点坐标
        Vector2 restPos = G2R(CanvasToLocal(P_R_HAND_R - P_RIFLE_GRIP)); float restAng = gunAng;   // 右手平时握着护木
        PouchPose(_takeSlot, rrMagInHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);   // 拔新弹匣的满袋
        Vector2 stowPos = takePos, stowOut = takeOut; float stowAng = takeAng;
        if (_stowSlot >= 0) PouchPose(_stowSlot, rrMagInHand, 0f, out stowPos, out stowOut, out stowAng);   // 插回旧弹匣的空袋（旧弹匣正拿着）
        float grabAng = gunAng;                                            // ① 抓旧弹匣：手里弹匣的位置正好对上枪里的弹匣
        Vector2 grabPos = G2R(magIn) - Rot(rrMagInHand, grabAng);
        Vector2 pulledPos = grabPos + Rot(-R_MAG_DIR, gunAng) * rtPullOldDist;   // 沿弹匣方向拔下来
        float readyAng = gunAng;                                           // 新弹匣正对弹匣口
        float depth = t < t5 ? rrReady : Mathf.Lerp(rrReady, 0f, Ease(Seg(t, t5, rtInsertTime)));
        Vector2 readyPos = G2R(magIn - R_MAG_DIR * depth) - Rot(rrMagInHand, readyAng);
        Vector2 seatedPos = G2R(magIn) - Rot(rrMagInHand, readyAng);       // 插到底时手在哪
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));

        Vector2 hp; float ha;
        if (t < tA)                                                        // ① 伸手去抓旧弹匣
        {
            float e = Ease(Seg(t, 0f, rnReachTime));
            hp = Vector2.Lerp(restPos, grabPos, e); ha = Mathf.LerpAngle(restAng, grabAng, e);
        }
        else if (t < tB) { hp = Vector2.Lerp(grabPos, pulledPos, Ease(Seg(t, tA, rtPullOldTime))); ha = grabAng; }   // ① 拔下来
        else if (t < t1)                                                   // ② 带到空袋口（枪同时抬起来）
        {
            float e = Ease(Seg(t, tB, t1 - tB));
            hp = Vector2.Lerp(pulledPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(grabAng, stowAng, e);
        }
        else if (t < t2) { hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, t1, rtPushTime))); ha = stowAng; }   // ② 插进袋里
        else if (t < t3)                                                   // ③ 手移到满弹匣的袋子
        {
            float e = Ease(Seg(t, t2, nrSwitchTime));
            hp = Vector2.Lerp(stowPos, takePos, e); ha = Mathf.LerpAngle(stowAng, takeAng, e);
        }
        else if (t < t4) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, t3, rtGrabTime))); ha = takeAng; }   // ③ 拔出新弹匣
        else if (t < t5)                                                   // ③ 直接送到弹匣口
        {
            float e = Ease(Seg(t, t4, rtBringTime));
            hp = Vector2.Lerp(takeOut, readyPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, readyAng, e);
        }
        else if (t < t6) { hp = readyPos; ha = readyAng; }                 // ③ 插进去
        else                                                               // 往回走，枪放平的那一刻正好抓住护木（同战术换弹）
        {
            float e = Ease(Seg(t, t6, t9 - t6));
            hp = Vector2.Lerp(seatedPos, restPos, e) + Arc(e); ha = Mathf.LerpAngle(readyAng, restAng, e);
        }

        // 胸挂：旧弹匣插到底，空袋里多一个空弹匣；新弹匣一到手里，那个袋子就空了
        if (_stowSlot >= 0 && t >= t2 && !_oldStowed) { _oldStowed = true; _rSlotState[_stowSlot] = SLOT_EMPTY; ApplySlots(true); }
        bool showNew = t >= t3 && t < t6;
        if (showNew && !_magTaken) { _magTaken = true; _rSlotState[_takeSlot] = SLOT_NONE; ApplySlots(true); }

        // ---- 4. 摆手和两个弹匣 ----
        bool regripped = t >= t9;                                          // 右手抓回护木：换回枪上的那只手
        if (_rifleHandR != null) _rifleHandR.enabled = regripped;
        _rlHandSprite.enabled = !regripped;
        _rlHand.localPosition = new Vector3(hp.x, hp.y, 0f);
        _rlHand.localRotation = Quaternion.Euler(0f, 0f, ha);
        Vector2 inHand = hp + Rot(rrMagInHand, ha);                        // 手里弹匣的底部
        bool showOld = t >= tA && t < t2 && !_oldStowed;                   // 旧弹匣：被抓住 ~ 插回袋里
        _rlOldMag.enabled = showOld;
        _rlOldMag.transform.localPosition = new Vector3(inHand.x, inHand.y, 0f);
        _rlOldMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha);
        _rlNewMag.enabled = showNew;
        _rlNewMag.transform.localPosition = new Vector3(inHand.x, inHand.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha);
        if (_rifleMag != null) _rifleMag.enabled = !(t >= tA && t < t6);   // 旧弹匣被拔走后枪里没弹匣，插好了再显示
    }

    // ============================================================================
    // 战术匕首（激光刃）：拿出来时刀刃上半段从下半段弹出 → 激光像开灯泡一样闪几下、亮到稍微过曝、再降到正常亮度；
    //   挥动和第一种 NPC 挥斧头一样（抬起 → 猛地挥下 → 停一下 → 收回）。激光有真正的光照（URP 2D 光）。
    // ============================================================================

    // 开灯泡式闪动：u = 0~1 是闪动的进度，返回亮度（1 = 正常，Dagger Overexpose = 最亮那一下）
    float DaggerFlicker(float u)
    {
        if (u <= 0f) return 0f;
        if (u >= 1f) return 1f;
        float[] at = { 0f, 0.08f, 0.15f, 0.24f, 0.30f, 0.45f, 1f };          // 关键帧时间
        float[] v = { 0f, 0.8f, 0.1f, 1f, 0.3f, daggerOverexpose, 1f };     // 亮 → 灭 → 亮 → 暗 → 过曝 → 慢慢回到正常
        for (int i = 0; i < at.Length - 1; i++)
            if (u <= at[i + 1]) return Mathf.Lerp(v[i], v[i + 1], (u - at[i]) / (at[i + 1] - at[i]));
        return 1f;
    }

    // 挥动的角度：u = 0~1 是一次挥动的进度（和第一种 NPC 的斧头一样）
    float DaggerSwingAngle(float u)
    {
        float up = daggerAngles.x, down = daggerAngles.y;                  // 抬起 / 挥下的角度
        if (u < 0.30f) { float t = u / 0.30f; return up * (1f - (1f - t) * (1f - t)); }        // 抬起（先快后慢）
        if (u < 0.40f) { float t = (u - 0.30f) / 0.10f; return Mathf.Lerp(up, down, t * t); }  // 猛地挥下（越来越快）
        if (u < 0.48f) return down;                                                             // 砍中，停一下
        if (u < 0.80f) { float t = (u - 0.48f) / 0.32f; return Mathf.Lerp(down, 0f, t * t * (3f - 2f * t)); }  // 收回
        return 0f;                                                                              // 歇一下再来
    }

    // 每帧：匕首的弹出 / 激光闪动 / 挥动
    void AnimateDagger(float dt)
    {
        // ---- 1. 拿出来：刀刃弹出 → 激光闪动 ----
        _daggerT += dt;
        float popU = Mathf.Clamp01(_daggerT / Mathf.Max(0.01f, daggerPopTime));
        float c1 = 1.70158f, pu = popU - 1f;
        float pop = popU >= 1f ? 1f : 1f + (c1 + 1f) * pu * pu * pu + c1 * pu * pu;   // 弹出（冲过头一点再回来）
        if (_daggerTip != null)
        {
            Vector2 popOff = DAGGER_DIR * (daggerPopDist / PPU_FULL) * (1f - pop);
            _daggerTip.transform.localPosition = _daggerTipBase - new Vector3(popOff.x, popOff.y, 0f);   // 沿刀身方向从下半段里弹出来
            _daggerTip.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(pop));   // 弹出时从小变大
            _daggerTip.enabled = popU > 0f;
        }
        float glow = DaggerFlicker((_daggerT - daggerPopTime) / Mathf.Max(0.01f, daggerFlickerTime));   // 激光亮度
        if (_daggerT > daggerPopTime + daggerFlickerTime) glow = 1f + daggerHum * Mathf.Sin(_time * 37f) * Mathf.Sin(_time * 13f);   // 亮好后轻轻嗡嗡地抖
        Color lc = daggerLightColor;
        if (_daggerGlow != null) { Color c = lc; c.a = Mathf.Clamp01(glow); _daggerGlow.color = c; }             // 亮光图层
        if (_daggerBloom != null) { Color c = lc; c.a = Mathf.Clamp01(glow - 1f) * 2f; _daggerBloom.color = c; }   // 过曝：再叠一层更大的亮光
        if (_daggerLight != null)                                          // 真正照亮周围的光
        {
            _daggerLight.enabled = glow > 0.01f;
            WorldLighting.SetPoint(_daggerLight, lc, daggerLightIntensity * glow, daggerLightRadius, 360f);
            _daggerLight.transform.position = _daggerLightAt.position;
        }

        // ---- 2. 挥动（和第一种 NPC 挥斧头一样）----
        float period = Mathf.Max(0.2f, daggerPeriod), angle = 0f;
        if (_swingT >= 0f)
        {
            _swingT += dt;
            if (_swingT >= period) _swingT = -1f;                          // 挥完一下
            else angle = DaggerSwingAngle(_swingT / period);
        }
        if (_swingNew)                                                     // 新的一下：挥下时手的 y 在范围里随机
        {
            _swingDownY = RandIn(daggerHandDownY);
            _swingNew = false;
        }
        Vector2 ang = daggerAngles;                                        // x = 抬起角度，y = 挥下角度
        Vector2 handDown = daggerHandDown; handDown.y = _swingDownY;
        float kUp = ang.x != 0f && angle > 0f ? angle / ang.x : 0f;        // 0~1：抬起了多少
        float kDown = ang.y != 0f && angle < 0f ? angle / ang.y : 0f;      // 0~1：挥下了多少
        Vector2 h = daggerHandUp * kUp + handDown * kDown;                 // 手点的偏移
        _actionLean = daggerLean.x * kUp + daggerLean.y * kDown;           // 抬起后仰、挥下前倾
        _actionShift = daggerShift * (kDown - kUp);                        // 挥下时身体往前送，抬起时往后收
        _handPoint.localPosition = new Vector3(_gripBase.x + h.x, _gripBase.y + h.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, angle);        // 绕手腕转
    }

    // ============================================================================
    // 步枪取出动画：
    //   ① 右手换成持枪右手，伸过去抓住胸挂步枪的上边（手朝着枪）→ 抓住的一瞬间，胸挂上的装饰步枪换成能开火的步枪
    //   ② 右手抓着枪拿到身体中间 → ③ 左手换成持枪左手，抓住枪托后侧，往后一拉把枪托拉开（震一下）
    //   ④ 左手回到握把 → ⑤ 右手松开，枪口因为重力往下一沉，右手回到护木 → ⑥ 整体回到平时持枪的样子 → 打开步枪手电
    // ============================================================================

    // 各段的时间点（秒）
    void RifleDrawTimes(out float tGrab, out float tCenter, out float tStock, out float tPulled, out float tLeftHome, out float tRightHome, out float tEnd)
    {
        tGrab = rdReachTime;
        tCenter = tGrab + rdLiftTime;
        tStock = tCenter + rdStockGrabTime;
        tPulled = tStock + rdStockPullTime;
        tLeftHome = tPulled + rdLeftHomeTime;
        tRightHome = tLeftHome + rdRightHomeTime;
        tEnd = tRightHome + rdSettleTime;
    }

    void AnimateRifleDraw(float dt)
    {
        _drawT += dt;
        float t = _drawT;
        RifleDrawTimes(out float tGrab, out float tCenter, out float tStock, out float tPulled, out float tLeftHome, out float tRightHome, out float tEnd);
        if (t >= tEnd) { FinishRifleDraw(); return; }                      // 播完了：平时持枪的样子 + 开手电

        // ---- 1. 胸挂上那把枪现在在哪（随胸挂晃动）：换成能开火的步枪时，从这个位置、角度、大小开始 ----
        Transform m = _mountRifle.transform;
        Vector3 mc = _rig.InverseTransformPoint(m.TransformPoint(CanvasToLocal(MOUNT_RIFLE_CENTER - P_RIFLE)));   // 胸挂步枪的中心
        Vector3 md = _rig.InverseTransformVector(m.TransformVector(Vector3.right));
        float swapAng = Mathf.Atan2(md.y, md.x) * Mathf.Rad2Deg + MOUNT_RIFLE_TURN;   // 换上去的步枪要转多少才和胸挂上的一样斜
        Vector2 swapPos = new Vector2(mc.x, mc.y) + Rot((Vector2)CanvasToLocal(P_RIFLE_GRIP - HELD_RIFLE_CENTER) * MOUNT_RIFLE_SCALE, swapAng);   // 握把在哪
        Vector2 centerPos = CanvasToLocal(rdCenterGrip - P_ROOT);          // 拿到身体中间时握把在哪
        Vector2 normalPos = AimHand(_aimAng, Vector2.zero); float normalAng = _aimAng;   // 平时持枪（没接敌 = 枪口朝下）

        // ---- 2. 枪的位置 / 角度 / 大小 ----
        Vector2 gunPos; float gunAng, scale = 1f;
        float shStock = Shake(t - tPulled, 0.3f);                          // ③ 枪托拉开：震一下
        if (t < tCenter)                                                   // ② 从胸挂拿到身体中间（顺便从胸挂上的大小变回正常大小）
        {
            float e = Ease(Seg(t, tGrab, rdLiftTime));
            gunPos = Vector2.Lerp(swapPos, centerPos, e); gunAng = Mathf.LerpAngle(swapAng, rdCenterAngle, e);
            scale = Mathf.Lerp(MOUNT_RIFLE_SCALE, 1f, e);
        }
        else if (t < tLeftHome) { gunPos = centerPos; gunAng = rdCenterAngle + rdStockShake * shStock; }   // ③④ 在身体中间拉枪托
        else if (t < tRightHome)                                           // ⑤ 右手松开：枪口往下一沉（重力），弹两下停住
        {
            float tau = t - tLeftHome;
            float k = 1f - Mathf.Exp(-tau * 10f) * Mathf.Cos(tau * 18f);
            gunPos = centerPos; gunAng = rdCenterAngle - rdDropAngle * k;
        }
        else                                                               // ⑥ 回到平时持枪的样子
        {
            float e = Ease(Seg(t, tRightHome, rdSettleTime));
            gunPos = Vector2.Lerp(centerPos, normalPos, e); gunAng = Mathf.LerpAngle(rdCenterAngle - rdDropAngle, normalAng, e);
        }
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
        _handPoint.localScale = Vector3.one * scale;
        System.Func<Vector2, Vector2> G = p => gunPos + Rot(p * scale, gunAng);   // 枪的本地坐标 → 根节点坐标

        // ---- 3. 枪托：一开始是收起来的（往前推了 Rd Stock Collapse），③ 被左手往后拉开（拉过头一点再弹回）----
        float stockU = Seg(t, tStock, rdStockPullTime);
        float c1 = 1.70158f, su = stockU - 1f;
        float pulled = stockU <= 0f ? 0f : (stockU >= 1f ? 1f : 1f + (c1 + 1f) * su * su * su + c1 * su * su);
        float stockOff = rdStockCollapse * (1f - pulled);                  // 枪托往前推了多少（原图像素）
        if (_rifleStock != null) _rifleStock.localPosition = _rifleStockBase + new Vector3(stockOff / PPU_FULL, 0f, 0f);

        // ---- 4. 右手：伸过去抓胸挂步枪上边 → 抓着枪 → 松开回护木 ----
        Vector2 grabLocal = CanvasToLocal(rdGrabPoint - P_RIFLE_GRIP);     // 右手抓枪的地方（枪上边）
        Vector2 restLocal = CanvasToLocal(P_R_HAND_R - P_RIFLE_GRIP);      // 右手平时握的护木
        Vector2 handStart = _rig.InverseTransformPoint(_handR.position);   // 右手从身体旁边的手的位置出发
        Vector2 swapGrab = new Vector2(mc.x, mc.y) + Rot(((Vector2)CanvasToLocal(rdGrabPoint - HELD_RIFLE_CENTER)) * MOUNT_RIFLE_SCALE, swapAng);   // 胸挂步枪上边
        Vector2 hp; float ha;
        if (t < tGrab)                                                     // ① 伸过去（手转到朝着枪）
        {
            float e = Ease(Seg(t, 0f, rdReachTime));
            hp = Vector2.Lerp(handStart, swapGrab, e) + new Vector2(0f, 0.04f * Mathf.Sin(Mathf.PI * e));
            ha = Mathf.LerpAngle(0f, swapAng + rdGrabAngle, e);
        }
        else if (t < tLeftHome) { hp = G(grabLocal); ha = gunAng + rdGrabAngle; }   // ②③④ 抓着枪上边
        else                                                               // ⑤ 松开，回到护木
        {
            float e = Ease(Seg(t, tLeftHome, rdRightHomeTime));
            hp = Vector2.Lerp(G(grabLocal), G(restLocal), e); ha = Mathf.LerpAngle(gunAng + rdGrabAngle, gunAng, e);
        }
        bool rightHome = t >= tRightHome;
        _rlHandSprite.sprite = _rifleHandRSprite;                          // 持枪右手的图
        _rlHandSprite.sortingOrder = 48;
        _rlHandSprite.enabled = !rightHome;
        _rlHand.localPosition = new Vector3(hp.x, hp.y, 0f);
        _rlHand.localRotation = Quaternion.Euler(0f, 0f, ha);
        _rlHand.localScale = Vector3.one;

        // ---- 5. 左手：抓枪托后侧 → 跟着枪托往后拉 → 回到握把 ----
        Vector2 stockLocal = (Vector2)CanvasToLocal(rdStockGrabPoint - P_RIFLE_GRIP) + new Vector2(stockOff / PPU_FULL, 0f);   // 枪托后侧（跟着枪托动）
        Vector2 leftStart = _rig.InverseTransformPoint(_handL.position);   // 左手从身体旁边的手的位置出发
        bool leftOut = t >= tCenter && t < tLeftHome;                      // 左手单独动的这段
        Vector2 lp = Vector2.zero; float la = gunAng;
        if (t < tStock) { float e = Ease(Seg(t, tCenter, rdStockGrabTime)); lp = Vector2.Lerp(leftStart, G(stockLocal), e); la = Mathf.LerpAngle(0f, gunAng, e); }   // ③ 伸到枪托后侧
        else if (t < tPulled) lp = G(stockLocal);                          // ③ 往后拉
        else { float e = Ease(Seg(t, tPulled, rdLeftHomeTime)); lp = Vector2.Lerp(G(stockLocal), G(Vector2.zero), e); }   // ④ 回到握把
        if (_drawHandL != null)
        {
            _drawHandL.enabled = leftOut;
            _drawHandL.transform.localPosition = new Vector3(lp.x, lp.y, 0f);
            _drawHandL.transform.localRotation = Quaternion.Euler(0f, 0f, la);
        }

        // ---- 6. 显示 / 藏：抓住的一瞬间胸挂步枪换成能开火的；两只持枪手回到枪上以后用枪上的那两只 ----
        bool grabbed = t >= tGrab;
        if (_mountRifle != null) _mountRifle.enabled = !grabbed;
        foreach (SpriteRenderer sr in _rifleParts) if (sr != null) sr.enabled = grabbed;
        if (_rifleHandL != null) _rifleHandL.enabled = t >= tLeftHome;    // 左手回到握把后用枪上的
        if (_rifleHandR != null) _rifleHandR.enabled = rightHome;          // 右手回到护木后用枪上的
        if (_bodyHandR != null) _bodyHandR.enabled = false;               // 平时的右手一开始就换成持枪右手
        if (_bodyHandL != null) _bodyHandL.enabled = t < tCenter;          // 平时的左手到 ③ 才换成持枪左手
    }

    // 取出动画结束 / 被打断：摆成平时持枪的样子，打开步枪手电
    void FinishRifleDraw()
    {
        _drawT = -1f;
        if (_mountRifle != null) _mountRifle.enabled = _held != "rifle";
        bool r = _held == "rifle";
        foreach (SpriteRenderer sr in _rifleParts) if (sr != null) sr.enabled = r;
        if (_bodyHandL != null) _bodyHandL.enabled = _held == null;
        if (_bodyHandR != null) _bodyHandR.enabled = _held == null;
        if (_drawHandL != null) _drawHandL.enabled = false;
        if (_rlHandSprite != null) _rlHandSprite.enabled = false;
        if (_rifleStock != null) _rifleStock.localPosition = _rifleStockBase;
        if (_handPoint != null) _handPoint.localScale = Vector3.one;
        if (r) { _rifleLightOn = true; _lightOnT = 0f; }                   // 打开步枪手电（下面闪一下再亮）
    }

    // 快速换弹：旧弹匣脱手，自己往下掉（空中时挂在角色下面，跟着人走）
    void DropMag(Vector2 pos, float ang, Vector2 vel, float spin)
    {
        SpriteRenderer sr = new GameObject("DroppedMag").AddComponent<SpriteRenderer>();
        sr.sprite = Load(IsRifle ? "Weapons/RifleMag" : "Weapons/PistolMag");   // 这把枪的弹匣
        sr.transform.SetParent(_rig, false);
        sr.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
        sr.sortingOrder = IsRifle ? 41 : 38;                                // 在枪和手后面，身体前面
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
        [InspectorName("步枪")] Rifle,
        [InspectorName("战术匕首")] Dagger,
    }
}
