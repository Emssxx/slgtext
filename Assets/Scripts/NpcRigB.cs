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
//   【换弹由配件决定】npc2 有胸挂 → 胸挂那套；动作本身是通用的（CharacterRigBase.Reload.cs，枪决定怎么插）。
//   【手枪换弹】战术 / 快速 / 普通三种。一共 5 个弹匣（枪里 1 + 胸挂 4）：
//     新弹匣从胸挂的满弹匣袋拔出来；战术 / 普通换弹的旧弹匣插回胸挂上的空袋（普通是先插回再拿新的），快速换弹直接丢掉。
//     所以第一次换弹（还没有空袋）只能是快速换弹——选了战术 / 普通也会自动改成快速。测试：Test Reload。
//   以后接进游戏：挂在 NPC 上就行——会读 NavMeshAgent 的速度、读旧图的翻转（flipX）决定朝向、把旧图变透明（和 NpcRig 一样）。
//
// 【测试】Inspector 里：
//   Test Cycle 打勾 = 自动循环"站 → 走 → 跑 → 停"（原地踏步），看起步 / 加速 / 急停的惯性；
//   Test Keyboard 打勾 = 按住 D / A 往右 / 往左走，再按住 Shift 跑，松开就停（键盘优先，松开 2 秒后回到自动循环）；
//   Test Speed（≥0）= 固定速度原地踏步（改数值时会按 Test Accel 平滑加减速）。
// ============================================================================
public class NpcRigB : CharacterRigBase
{
    // 和 NpcRig 默认值不同的共用参数（放在 CharacterRigBase 里）：这里改成本角色自己的默认值。
    // （只影响新加的组件；场景里已经存好的数值不受影响，Inspector 照样可以改）
    public NpcRigB()
    {
        testFaceLeft = false;
        deathGroundLift = 0.12f;
        impactScale = 1f;
        fleshChance = 0.6f;
        ricochetChance = 0.274f;
        aimShoulder = new Vector2(0.175f, 0.466f);
        artFolder = "NpcArt2";                     // 部件图在 Resources/NpcArt2
    }

    [Header("惯性开关（整体倍率，0 = 关掉）")]
    [Range(0f, 2f)] public float mountAmount = 1f;            // 胸挂整组（手枪 + 步枪 + 8 个弹匣）
    [Range(0f, 2f)] public float helmetAmount = 1f;           // 头盔（连面罩）
    [Range(0f, 2f)] public float packAmount = 1f;             // 背包

    public bool testCycle = false;                            // 自动循环：站 → 走 → 跑 → 停
    public float cycleIdle = 1.2f;                            // 循环：站着几秒
    public float cycleWalk = 2.5f;                            // 循环：走几秒
    public float cycleRun = 2.0f;                             // 循环：跑几秒
    public float cycleStop = 1.5f;                            // 循环：急停后站几秒（看挂件往前甩）
    public float cycleWalkSpeed = 1.65f;                      // 循环 / 键盘：走路速度
    public float cycleRunSpeed = 3.9f;                        // 循环 / 键盘：跑步速度
    public float testAccel = 6f;                              // 测试时速度变化多快（单位/秒²；越大起步 / 急停越猛）
    public bool testKeyboard = true;                          // 键盘：按住 D / A 走，加 Shift 跑
    public TestHeld testHeld = TestHeld.None;                 // 强制拿什么（手枪 / 步枪 / 战术匕首）
    public string currentState = "";                          // （只读）现在是站 / 走 / 跑，速度多少

    // 手枪 / 步枪的开火手感、枪火、子弹、枪上的手电筒都是枪的属性：在 WeaponDef（RigWeapons.cs，
    //   或 Resources/Weapons/aba_pistol、aba_rifle 资源文件）里调，不再放在角色上。
    [Header("步枪（角色这边：肩膀位置 / 测试连发）")]
    public int rifleBurst = 10;                               // 测试时一次连发几枪（Test Fire / Test Loop）
    public Vector2 rifleShoulder = new Vector2(-0.083f, 0.51f);   // 步枪绕着转的点（枪托顶在肩上的位置，相对脚底中心；选中 NPC 时 Scene 里的青点）

    [Header("步枪弹匣（三种步枪换弹共用）")]
    public string rigRifleMags = "";                          // （只读）胸挂 4 个步枪弹匣袋，从右到左：满 / 空弹匣 / 空袋。一共 5 个 = 枪里 1 个 + 胸挂 4 个
    // 弹匣在手里的位置 / 对准时差多远：枪的属性，挪到 WeaponDef（aba_rifle：magInHand、magReady）
    public float rrPullOut = 0.1f;                            // 从胸挂弹匣袋往外拔多远（步枪弹匣长，拔得多）

    // 步枪式换弹的时间 / 姿势（rt* / rs* / rn*）挪到 CharacterRigBase（所有角色共用；名字没变，调过的值还在）

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

    [Header("胸挂上的步枪")]
    public string rifleWeapon = "aba_rifle";                  // 胸挂上那把步枪是哪把枪（WeaponDef id：aba_rifle = 战斗步枪，aba-x95……；运行中改了马上换）
    // 取出动画的参数（rd…）在 CharacterRigBase.Draw.cs（枪械通用，npc1 也用）


    [Header("换弹 1：战术换弹（参数和第一种 NPC 一样；时间 = 秒，距离 = 单位，角度 = 度）")]
    public string rigMags = "";                               // （只读）胸挂 4 个手枪弹匣袋，从右到左：满 / 空弹匣（插回去的旧弹匣）/ 空袋。一共 5 个弹匣 = 枪里 1 个 + 胸挂 4 个
    public string lastReloadKind = "";                        // （只读）上一次实际用的换弹（没有空袋放旧弹匣时，战术 / 普通会自动改成快速）
    public bool testRefillMags;                               // 勾一下：胸挂弹匣（手枪 + 步枪）全部补满（满弹匣用完时也会自动补满，测试用）
    public float rlPullOut = 0.06f;                           // ③ 从弹匣袋里往外拔多远（沿弹匣方向；放旧弹匣时反过来往里塞）

    public float nrSwitchTime = 0.2f;                         // 旧弹匣插回空袋后，手移到满弹匣那个袋子用几秒

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
    // 能开火的步枪的中心：枪的属性（WeaponDef.mountCenter）
    const float MOUNT_RIFLE_TURN = -57.6f - 5.6f;                     // 胸挂步枪比能开火的步枪多斜多少度
    const float MOUNT_RIFLE_SCALE = 0.81f;                            // 角色画的胸挂步枪比能开火的步枪小多少（长度比；用部件拼的不缩小）
    static float MountScale(WeaponDef d) => d != null && d.characterMountArt ? MOUNT_RIFLE_SCALE : 1f;   // 胸挂上那把枪的大小
    Transform _mountGunPic;                                           // 用部件拼的胸挂步枪（战斗步枪以外的枪）
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

    Transform _pack, _head, _hose, _mount;
    Vector3 _packBase, _headBase, _mountBase;
    readonly Dictionary<string, Pend> _gearPend = new Dictionary<string, Pend>();         // 小挂件的摆
    readonly Dictionary<string, bool> _onMount = new Dictionary<string, bool>();          // true = 挂在胸挂上

    Spring2 _mountPos, _headPos, _packPos, _beltBounce;   // 位置弹簧（胸挂 / 头盔 / 背包 / 腰上包类上下弹）
    Pend _mountRot, _headRot, _packRot, _hoseRot;      // 转动的摆
    float _prevMountV;                                 // 胸挂上一小步的角速度（算胸前挂件被带动多少）

    // 测试
    float _testV;                                      // 测试时平滑后的速度
    float _cycleT;                                     // 循环计时
    float _keyHold;                                    // 键盘松开后还要再管几秒
    bool _keyLeft;                                     // 键盘最后一次往左
    bool _agentLeft;                                   // 游戏里最后一次往左走

    Transform _slide;   // 手点（整把枪挂在它下面）/ 枪口 / 滑套
    GunRig _pistolGun, _rifleGun;                      // 两把枪（按 WeaponDef 拼出来；拿哪把 _gun 就是哪把）
    readonly List<SpriteRenderer> _pistolParts = new List<SpriteRenderer>();   // 手枪各部件 + 两只持枪手
    readonly List<SpriteRenderer> _rifleParts = new List<SpriteRenderer>();    // 步枪各部件 + 两只持枪手
    Transform _pistolMuzzle, _rifleMuzzle;             // 两把枪的枪口（拿哪把，_muzzle 就是哪个）
    SpriteRenderer _mountRifle;                        // 胸挂上的步枪（拿出来时藏起来）
    int _burstLeft;                                    // 测试连发：还剩几枪
    float _burstTimer;                                 // 测试连发：离下一枪还有多久
    SpriteRenderer _bodyHandL, _bodyHandR, _mountPistol;   // 平时的两只手 / 胸挂上的手枪（拿枪时藏起来）
    TestHeld _lastTest = TestHeld.None;                // 上一帧的测试选项（变了就换手）
    Transform _gunLamp;                                // 手电筒灯头（挂在枪上，跟着枪转、后坐）
    Transform _gunHeld, _rifleHeld;                    // 手枪 / 步枪的整体节点（挂在手点下面）
    readonly List<SpriteRenderer> _daggerParts = new List<SpriteRenderer>();   // 匕首各部件 + 持刀手
    SpriteRenderer _daggerTip, _daggerGlow, _daggerBloom; // 刀刃上半段 / 激光亮光 / 过曝时多叠的一层亮光
    Vector3 _daggerTipBase;                            // 刀刃上半段的原位
    Transform _daggerHeld, _daggerLightAt;             // 匕首节点 / 激光照明的位置
    UnityEngine.Rendering.Universal.Light2D _daggerLight;   // 激光照亮周围的光
    float _daggerT;                                    // 拿出匕首后过了多久（弹出 / 闪动用）
    SpriteRenderer _drawHandL, _rifleHandL;            // 取出动画里单独动的左手 / 枪上的持枪左手
    Sprite _rifleHandRSprite;                          // 持枪右手的图（取出动画用）
    Transform _rifleStock;                             // 步枪枪托（取出时拉开；无托步枪 = null）
    Vector3 _rifleStockBase;                           // 枪托原位
    SpriteRenderer _rifleLampGlow;                     // 步枪手电灯头发光点
    bool _rifleLightOn;                                // 步枪手电已经打开（取出动画播完才开）
    float _lightOnT = 9f;                              // 手电刚打开多久（开的时候闪一下）
    SpriteRenderer _gunLampGlow;                       // 灯头发光点
    static Sprite _glowSprite;                         // 发光圆点图（代码画）
    static Material _additive;                         // 叠加发光材质

    // 换弹
    SpriteRenderer _gunMag, _gunHandR;                 // 枪里的弹匣 / 枪上的右手
    readonly SpriteRenderer[] _mountMags = new SpriteRenderer[4];   // 胸挂上的 4 个手枪弹匣
    readonly SpriteRenderer[] _mountRifleMags = new SpriteRenderer[4];   // 胸挂上的 4 个步枪弹匣
    SpriteRenderer _rifleHandR;                        // 步枪辅助手（护木）
    ChestRigReload _chest;                             // 胸挂（换弹配件）：新弹匣从满弹匣的袋子拔，旧弹匣插回空袋（CharacterRigBase.Reload.cs）

    // 朝向：测试强制 > 瞄准 > 键盘 > 旧图翻转（游戏里）> 往哪边走
    protected override bool FacingLeft => testFaceLeft || (_aimUntil > _time ? _aimLeft : (_keyHold > 0f ? _keyLeft : (_oldSprite != null ? _oldSprite.flipX : _agentLeft)));

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
        // 手枪 / 步枪：按武器数据（WeaponDef，RigWeapons.cs）拼出来，挂在手点下面（CharacterRigBase.BuildGun）
        _pistolGun = BuildGun(WeaponDef.Get("aba_pistol"), _handPoint);
        _gunHeld = _pistolGun.root;
        _pistolParts.AddRange(_pistolGun.parts);
        _gunMag = _pistolGun.Part("PistolMag"); _gunHandR = _pistolGun.Part("PistolHandR");
        _slide = _pistolGun.Part("PistolSlide").transform; _slideBase = _slide.localPosition;
        _pistolMuzzle = _pistolGun.muzzle;
        _gunLamp = _pistolGun.lamp; _gunLampGlow = _pistolGun.lampGlow;
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
        BuildChestRig();                                                              // 胸挂 = 换弹配件（8 个弹匣袋一开始都是满的）
        _muzzle = _pistolMuzzle;
        if (RenderMats.IsURP)                                                         // URP 2D：枪上手电筒真正照亮前面的光（颜色 / 亮度跟着拿着的那把枪）
        {
            WeaponDef pd = _pistolGun.def;
            _gunBeam = WorldLighting.CreatePoint("GunFlashlight", transform, pd.lightColor, pd.lightIntensity, pd.lightRange, pd.lightAngle);
            WorldLighting.EnableShadows(_gunBeam);                                    // 照到建筑 / 树石会投影子
        }
        _rifleHandRSprite = GripHandArt(true, HoldType.Rifle);                        // 取出动画：右手换成持枪右手（衣服的手）
        _drawHandL = new GameObject("DrawHandL").AddComponent<SpriteRenderer>();      // 取出动画：单独动的持枪左手
        _drawHandL.transform.SetParent(_rig, false);
        _drawHandL.sprite = GripHandArt(false, HoldType.Rifle);
        _drawHandL.sortingOrder = 46;
        _drawHandL.enabled = false;
        UseRifle(rifleWeapon);                                                        // 步枪：按 Rifle Weapon 拼（胸挂上的图也跟着换）

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

    // 建一个会摆的小挂件，记下原位和它的摆
    void AddGear(string key, Transform parent, Vector2 pivot, Vector2 parentPivot, int order, Pend pend)
    {
        Transform t = Part(key, parent, key, pivot, parentPivot, order).transform;
        _gear[key] = t;                                          // 记下部件
        _gearBase[key] = t.localPosition;                        // 记下原位
        _gearPend[key] = pend;                                   // 记下它的摆
        _onMount[key] = parent == _mount;                        // 挂在胸挂上的会被胸挂带着甩
    }

    // ============================================================================
    // 每帧流程在 CharacterRigBase.Update（通用骨架）；这里只填 npc2 自己要插进去的几步
    // ============================================================================
    protected override float AdjustSpeed(float speed, Vector3 velocity, float dt)
    {
        if (velocity.x < -0.05f) _agentLeft = true; else if (velocity.x > 0.05f) _agentLeft = false;   // 记下往哪边走
        float test = TestTarget(dt);                              // 测试想要的速度（-1 = 不测试）
        if (test >= 0f)
        {
            _testV = Mathf.MoveTowards(_testV, test, testAccel * dt);   // 平滑加减速（惯性就是靠加减速甩出来的）
            speed = _testV;
        }
        return speed;
    }

    protected override void AfterFrame(float dt, float speed)
    {
        if (_mountGunPic != null && _mountRifle != null && _mountGunPic.gameObject.activeSelf != _mountRifle.enabled)
            _mountGunPic.gameObject.SetActive(_mountRifle.enabled);   // 用部件拼的胸挂步枪：跟着"胸挂上那把枪"一起显示 / 藏起来
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

    protected override void StepSecondary(float dt, Motion mo)
    {
        float acc = mo.acc, refV = mo.refV, tilt = mo.tilt, tilta = mo.tilta, boba = mo.boba;   // 通用骨架算好的走路数据（CharacterRigBase.Step）

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

    }

    // 把算好的数摆到各个部件上
    protected override void ApplyAttachments()
    {
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

    // 人删掉时，地上的武器一起收走
    void OnDestroy()
    {
        foreach (GameObject g in _dropped) if (g != null) Destroy(g);
    }

    // 换弹时枪往后收到胸挂上挂步枪的地方
    protected override bool GunStowSpot(out Vector2 spot)
    {
        spot = Vector2.zero;
        if (_mount == null) return false;
        Vector3 p = _rig.InverseTransformPoint(_mount.TransformPoint(CanvasToLocal(MOUNT_RIFLE_CENTER - P_MOUNT)));   // 胸挂步枪的中心（随胸挂晃）
        spot = new Vector2(p.x, p.y);
        return true;
    }

    // 死的时候：停掉开枪 / 换弹 / 挥动 / 瞄准
    protected override void StopForDeath()
    {
        StopReload();
        _recoilT = -1f; _burstLeft = 0; _swingT = -1f;
        _lockTarget = null; _aimUntil = 0f;
    }

    // 受击时头盔多甩一下（转动点在脖子）
    protected override void HeadWhip(float push, float k)
    {
        if (_headRot != null) _headRot.v += push * 120f * k * hitHeadWhip;
    }

    // 武器脱手：复制一份扔出去，手上换回空手；胸挂上对应的那把不再出现（已经掉出去了）
    protected override void DropWeapon(float dt)
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
    protected override void RestoreAfterRevive()
    {
        SetHeld(null);
        SetHeld(_heldBeforeDeath);
    }

    // ============================================================================
    // 手枪：持枪 / 瞄准 / 开枪后坐 / 枪火 / 子弹（做法和参数都照搬第一种 NPC：NpcRig）
    //   不同的只有素材和位置：手枪、持枪手、枪火、子弹用 NpcArt2/Weapons/ 里你画的图，握把 / 枪口按你的 PSD 量。
    // ============================================================================

    // 现在拿的是不是步枪；当前这把枪的握把 / 枪口（原图坐标）和"肩膀"
    bool IsRifle => _gun != null && _gun.def.hold == HoldType.Rifle;
    bool IsGun => _gun != null;                                      // 拿的是枪（匕首不算）
    // 肩膀（角色的属性）：手枪绕 aimShoulder、步枪绕 rifleShoulder 转（Inspector 里调）
    protected override Vector2 ShoulderFor(WeaponDef d) => d.hold == HoldType.Rifle ? rifleShoulder : aimShoulder;

    // 枪上手电筒灯头的发光点（npc2 的画法）
    protected override SpriteRenderer MakeLampGlow(Transform at) => GlowDot(at);

    // ---------------- 测试窗口 ----------------
    public override List<TestOption> TestHoldExtras() => new List<TestOption> { new TestOption("", "空手"), new TestOption("dagger", "战术匕首") };
    public override bool CanTestEquip(WeaponDef d) => d != null && (d.hold == HoldType.Rifle || d.id == "aba_pistol");   // 手枪位只有战术手枪；步枪位能换任何步枪
    public override string TestGearInfo() => _chest == null ? "" : "胸挂手枪弹匣：" + rigMags + "\n胸挂步枪弹匣：" + rigRifleMags;
    public override void TestRefillGear() { if (_chest != null) _chest.RefillAll(); }

    public override void TestEquip(string key)
    {
        WeaponDef d = string.IsNullOrEmpty(key) || key == "dagger" ? null : WeaponDef.Get(key);
        TestHeld want = key == "dagger" ? TestHeld.Dagger : d == null ? TestHeld.None : d.hold == HoldType.Rifle ? TestHeld.Rifle : TestHeld.Pistol;
        if (want == TestHeld.Rifle)
        {
            if (rifleWeapon != d.id) { rifleWeapon = d.id; UseRifle(d.id); }
            if (_held == "rifle") { SetHeld(null); _lastTest = TestHeld.None; }   // 再点一次：重播取出动画
        }
        testHeld = want;                                                    // 下一帧 UpdateTest 换手
    }

    // 换步枪：按 WeaponDef id 拼一把新的挂在手点下面，胸挂上的图、取出动画用的部件都跟着换
    void UseRifle(string id)
    {
        WeaponDef d = WeaponDef.Get(id);
        if (d == null || d.hold != HoldType.Rifle)                          // 没有这把 / 不是步枪：退回战斗步枪
        {
            Debug.LogWarning("[NpcRigB] Rifle Weapon 不是一把步枪：" + id + "（用 aba_rifle）");
            d = WeaponDef.Get("aba_rifle");
            rifleWeapon = d != null ? d.id : id;
        }
        if (d == null) return;
        if (_reloadT >= 0f) StopReload();
        if (_rifleGun != null && _rifleGun.root != null) Destroy(_rifleGun.root.gameObject);   // 旧的那把拆掉
        GunRig g = BuildGun(d, _handPoint);
        _rifleGun = g;
        _rifleHeld = g.root;
        _rifleParts.Clear(); _rifleParts.AddRange(g.parts);
        _rifleMuzzle = g.muzzle;
        SpriteRenderer stock = string.IsNullOrEmpty(d.stockPart) ? null : g.Part(d.stockPart);   // 能拉开的枪托（无托 = 没有）
        _rifleStock = stock != null ? stock.transform : null;
        _rifleStockBase = _rifleStock != null ? _rifleStock.localPosition : Vector3.zero;
        _rifleHandR = g.Part(d.reloadHand);                                 // 辅助手（护木）
        _rifleHandL = null;                                                 // 持枪手（握把）：另一只手部件
        foreach (WeaponPart p in d.parts) if (p != null && p.hand && p.name != d.reloadHand) { _rifleHandL = g.Part(p.name); break; }
        // 胸挂上的图：战斗步枪用角色自己画的；其他枪用枪的部件直接拼一把整枪（不缩小，和拿在手上一样大），斜着挂在胸挂的步枪位置
        if (_mountGunPic != null) { Destroy(_mountGunPic.gameObject); _mountGunPic = null; }   // 上一把枪拼的图拆掉
        if (_mountRifle != null)
        {
            Transform mt = _mountRifle.transform;
            mt.localPosition = CanvasToLocal(P_RIFLE - P_MOUNT);
            mt.localRotation = Quaternion.identity;
            mt.localScale = Vector3.one;
            if (d.characterMountArt) _mountRifle.sprite = Load("Rifle");
            else
            {
                _mountRifle.sprite = null;                                  // 角色画的那把不显示（只留着管"显示 / 藏起来"）
                _mountGunPic = BuildGunPicture(d, mt.parent, _mountRifle, "MountGun");
                _mountGunPic.localPosition = CanvasToLocal(MOUNT_RIFLE_CENTER - P_MOUNT);   // 枪的中心对上胸挂步枪的中心
                _mountGunPic.localRotation = Quaternion.Euler(0f, 0f, MOUNT_RIFLE_TURN);
                _mountGunPic.gameObject.SetActive(_mountRifle.enabled);
            }
        }
        SyncRifleMagPouches(d, g);                                          // 胸挂上的步枪弹匣换成这把枪的弹匣（配件跟着枪走）
        if (_held == "rifle") SetHeld("rifle");                             // 正拿着步枪：直接换成新的（不重播取出动画）
        else foreach (SpriteRenderer sr in _rifleParts) if (sr != null) sr.enabled = false;
    }

    // 换手：key = "pistol" 拿出手枪 / "rifle" 拿出步枪（两只手换成持枪手、胸挂上那把藏起来）；null = 空手
    void SetHeld(string key)
    {
        string prev = _held;
        _held = key;                                                        // 记下现在拿什么
        _gun = key == "pistol" ? _pistolGun : key == "rifle" ? _rifleGun : null;   // 现在拿着的枪（CharacterRigBase 按它瞄准 / 后坐）
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
        _gripBase = d ? (Vector2)CanvasToLocal(P_DAGGER_GRIP - P_BODY) : (Vector2)CanvasToLocal((_gun ?? _pistolGun).def.restGrip);   // 手点放在握把上
        if (_gunHeld != null) _gunHeld.localPosition = Vector3.zero;        // 手枪节点 = 手点
        if (_rifleHeld != null) _rifleHeld.localPosition = Vector3.zero;    // 步枪节点 = 手点（部件都按步枪握把摆好了）
        _recoilT = -1f; _burstLeft = 0;                                     // 停掉正在播的后坐 / 连发
        StopReload();                                                       // 停掉换弹
        if (key == null) { _aimAng = 0f; _lockTarget = null; _aimUntil = 0f; }   // 收枪：瞄准清零
        _handPoint.localPosition = new Vector3(_gripBase.x, _gripBase.y, 0f);   // 手点回到握把原位
        _handPoint.localRotation = Quaternion.identity;
        if (_slide != null) _slide.localPosition = _slideBase;              // 滑套 / 拉栓组复位
        if (_rifleGun != null) _rifleGun.Kick(0f, PPU_FULL);
        _handPoint.localScale = Vector3.one;
        if (IsGun && prev != "pistol" && prev != "rifle") { _aimAng = lowReadyAngle; _lowK = 1f; }   // 刚拿出枪：先是枪口朝下（没接敌）
        StopGunDraw();                                                      // 停掉取出动画（枪托、单独动的手复原）
        if (_rifleStock != null) _rifleStock.localPosition = _rifleStockBase;
        _rifleLightOn = r;                                                  // 步枪手电：直接拿在手上就开
        if (r && prev != "rifle" && StartGunDraw()) _rifleLightOn = false;  // 拿出步枪：播取出动画（枪械通用，CharacterRigBase.Draw.cs），播完再开手电
    }

    // 测试开关：Test Held 换手；Test Loop 每 1.4 秒自动开一次；勾 Test Fire 开一次（手枪 = 一枪，步枪 = 连发 Rifle Burst 枪）
    protected override void UpdateTest(float dt)
    {
        if (_rifleGun != null && rifleWeapon != _rifleGun.def.id) UseRifle(rifleWeapon);   // Inspector 里换了步枪
        if (testHeld != _lastTest)                                          // 选项变了：马上换手
        {
            _lastTest = testHeld;
            SetHeld(testHeld == TestHeld.Pistol ? "pistol" : testHeld == TestHeld.Rifle ? "rifle" : testHeld == TestHeld.Dagger ? "dagger" : null);
        }
        if (testRefillMags && _reloadT < 0f) { testRefillMags = false; if (_chest != null) _chest.RefillAll(); }   // 勾了"补满"：胸挂弹匣（手枪 + 步枪）全部补满
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
            if (_burstTimer <= 0f) { TestShot(); _burstLeft--; _burstTimer += _rifleGun.def.ShotInterval; }   // 射速 → 两枪间隔（秒）
        }
    }

    // 测试开一枪：后坐 + 子弹（有测试目标往目标打，没有往正前方 2.5 单位打）
    void TestShot()
    {
        PlayAttack();
        if (testAimTarget != null) FireBullet(testAimTarget.position);
        else if (TryGetMuzzle(out Vector3 m)) FireBullet(m + new Vector3(2.5f * (FacingLeft ? -1f : 1f), 0f, 0f));
    }

    // 瞄准：通用骨架按现在拿着的枪算（CharacterRigBase.GunUpdateAim）
    protected override void UpdateAim(float dt) { GunUpdateAim(dt); }









    // 外部调用（出手的那一下）：开枪后坐一下（每一枪的幅度在范围里随机，和第一种 NPC 一样）
    public override void PlayAttack()
    {
        if (_dead) return;                                                  // 死了
        if (_held == "dagger") { if (_swingT < 0f) { _swingT = 0f; _swingNew = true; } return; }   // 匕首：挥一下（上一下没挥完不重来）
        if (!IsGun || _reloadT >= 0f || _drawT >= 0f) return;               // 没拿枪 / 正在换弹 / 正在取出步枪
        GunFire();                                                          // 开枪后坐：按这把枪的手感（CharacterRigBase.GunFire）
    }

    // 每帧：按后坐进度摆持枪手、枪口上扬、滑套、身体后仰
    protected override void AnimateHeld(float dt)
    {
        _actionLean = 0f; _actionShift = 0f;                                // 身体的额外晃动先清零
        if (_held == null) return;
        if (_held == "dagger") { AnimateDagger(dt); return; }               // 匕首：弹出 / 激光 / 挥动
        if (_drawT >= 0f) { AnimateGunDraw(dt); return; }                   // 步枪取出动画（枪械通用）
        if (AnimateGunReload(dt)) return;                                   // 正在换弹：换弹动画接管（枪决定怎么插，胸挂决定从哪拿）
        GunRecoilPose(dt);                                                  // 持枪 + 后坐 + 滑套 / 拉栓（CharacterRigBase.GunRecoilPose，按这把枪的属性）
    }



    // 枪口（子弹生成点）的世界坐标（手上拿着枪才有）
    public override bool TryGetMuzzle(out Vector3 pos)
    {
        bool ok = IsGun && _muzzle != null;
        pos = ok ? _muzzle.position : transform.position;
        return ok;
    }

    // 从枪口生成一颗子弹飞向 target（只是画面；打没打中由战斗系统算）。没拿枪返回 false
    public override bool FireBullet(Vector3 target) { return FireBullet(target, true); }

    // 同上；canRicochet = 落到地上（没打中）时有概率出跳弹
    public override bool FireBullet(Vector3 target, bool canRicochet)
    {
        if (!TryGetMuzzle(out Vector3 from)) return false;                  // 没拿枪
        target.z = from.z;
        GameObject go = new GameObject("Bullet");                           // 子弹不挂在 NPC 身上（人没了子弹照样飞完）
        go.transform.position = from;
        Vector3 d = target - from;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);   // 朝着目标
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = LoadFrom(_gun.def.artFolder, _gun.def.bulletSprite);    // 这把枪的子弹图（轴心在弹头）
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

    // 跳弹：和第一种 NPC 共用一张图（NpcArt/Fx/Ricochet，尖 = 落点；往左飞时左右翻）
    protected override void Ricochet(Vector3 at, bool left)
    {
        Sprite s = Resources.Load<Sprite>("NpcArt/Fx/Ricochet");
        if (s == null) return;
        SpriteRenderer sr = FxSprite("Ricochet", s, 60);
        sr.transform.position = at;
        sr.flipX = left;
        float k = flashSize;
        StartCoroutine(FxLife(sr, ricochetTime, new Vector3(k, k, 1f), new Vector3(k, k, 1f), 1f));
    }

    // 枪火：交给这把枪的动画类（CharacterRigBase.MuzzleFlash → GunAnimator；默认 = 这把枪的枪火图随机一张）

    // 枪上的手电筒（枪的属性：颜色 / 亮度 / 照多远 / 光束宽度都在 WeaponDef）：拿着有灯的枪才亮；光从灯头出发，朝枪口方向照
    //   步枪：取出动画播完才打开（打开时闪一下）
    void UpdateGunLight()
    {
        WeaponDef d = _gun != null ? _gun.def : null;
        bool on = d != null && d.hasLamp && d.lightOn && d.lightIntensity > 0f && (d.hold != HoldType.Rifle || _rifleLightOn);
        _lightOnT += Time.deltaTime;
        float u = _lightOnT / 0.4f;                                         // 刚打开：闪一下再亮稳
        float flick = d == null || !d.flickerOnDraw || u >= 1f ? 1f : (u < 0.12f ? 1f : u < 0.22f ? 0.1f : u < 0.32f ? 1f : u < 0.4f ? 0.3f : 1f);
        foreach (GunRig g in new[] { _pistolGun, _rifleGun })
        {
            if (g == null || g.lampGlow == null) continue;
            bool mine = on && g == _gun;
            g.lampGlow.enabled = mine;
            Color c = g.def.lightColor; c.a = flick;
            g.lampGlow.color = c;
            g.lampGlow.transform.localScale = Vector3.one * Mathf.Max(0.01f, g.def.lightGlowSize);
        }
        if (_gunBeam == null) return;
        _gunBeam.enabled = on;
        if (!on) return;
        WorldLighting.SetPoint(_gunBeam, d.lightColor, d.lightIntensity * flick, d.lightRange, d.lightAngle);   // Inspector 里改了马上生效
        Vector3 dir = _muzzle.TransformVector(Vector3.right);               // 枪口朝哪（含朝左翻转、后坐上扬）
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _gunBeam.transform.position = _gun.lamp.position;
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
    // 换弹（手枪 / 步枪；战术 / 快速 / 普通）：动作在 CharacterRigBase.Reload.cs，所有角色共用。
    //   怎么插、怎么对准由枪决定（WeaponDef）；从哪拿新弹匣由配件决定——npc2 有胸挂，所以从胸挂弹匣袋拔、旧弹匣插回空袋。
    // ============================================================================

    // 外部调用（ABA 清除小组 AbaOperative 用）：现在手上拿的是什么（null = 空手 / "pistol" / "rifle" / "dagger"）
    public string Held => _held;
    // 外部调用：正在播步枪取出动画（这时不能开枪）
    public bool IsDrawing => _drawT >= 0f;
    // 外部调用：换手（和 Test Held 选一样；已经拿着就不重来；还没拼好时不做）
    public override void Hold(string key) { if (_built && key != _held) SetHeld(key); }

    // 持枪手（衣服的属性）：npc2 的手按握法分（手枪 / 步枪各一对：持枪手 + 辅助手），拿任何枪都用这几只
    protected override Sprite GripHandArt(bool support, HoldType hold)
    {
        if (hold == HoldType.Rifle) return Load(support ? "Weapons/RifleHandR" : "Weapons/RifleHandL");
        return Load(support ? "Weapons/PistolHandR" : "Weapons/PistolHandL");
    }

    // 胸挂上还有几个这把枪的满弹匣
    public int FullMagsOnRig => _chest != null && _gun != null ? _chest.FullCount(_gun.def.hold) : 0;

    // 拿枪时换一次弹匣（通用换弹在 CharacterRigBase.Reload.cs）：正在取出步枪时不换
    public override void PlayReload(float fitSeconds, ReloadKind kind)
    {
        if (!IsGun || _drawT >= 0f) return;                                 // 没拿枪 / 正在取出步枪就不换
        base.PlayReload(fitSeconds, kind);
        if (_reloadT >= 0f) lastReloadKind = _reloadKind.ToString();        // Inspector 里显示这次实际用的是哪种
    }

    // 换弹结束 / 被打断
    void StopReload() { StopGunReload(); }

    // 胸挂 = 换弹配件：手枪 / 步枪各 4 个弹匣袋（按拿的顺序：从最右边开始）
    // 胸挂上的 4 个步枪弹匣：跟着现在这把步枪换（弹匣是枪的东西：X95 的弹匣插在胸挂里也是 X95 的弹匣）。
    //   战斗步枪用角色自己画的；其他枪用枪自己的弹匣部件，底部对准弹匣袋的底，斜得和画的一样（换弹时手里拿的、袋里插的是同一个弹匣）
    Sprite[] _pouchArt; Vector3[] _pouchPos; Quaternion[] _pouchRot; Vector2[] _pouchBottom;   // 角色画的原样（换回战斗步枪时复原）
    void SyncRifleMagPouches(WeaponDef d, GunRig g)
    {
        PouchGroup grp = null;
        if (_chest != null) foreach (PouchGroup pg in _chest.groups) if (pg.hold == HoldType.Rifle) grp = pg;
        if (grp == null || d == null) return;
        if (_pouchArt == null)                                              // 第一次：记下角色画的原样
        {
            int n = grp.pouches.Count;
            _pouchArt = new Sprite[n]; _pouchPos = new Vector3[n]; _pouchRot = new Quaternion[n]; _pouchBottom = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                MagPouch p = grp.pouches[k];
                _pouchArt[k] = p.mag.sprite; _pouchPos[k] = p.mag.transform.localPosition; _pouchRot[k] = p.mag.transform.localRotation; _pouchBottom[k] = p.bottom;
            }
        }
        SpriteRenderer gunMag = g != null && !string.IsNullOrEmpty(d.magPart) ? g.Part(d.magPart) : null;
        bool own = d.characterMountArt || gunMag == null || gunMag.sprite == null;   // 战斗步枪（或这把枪没有弹匣部件）：用角色画的
        float magAxis = Mathf.Atan2(d.magDir.y, d.magDir.x) * Mathf.Rad2Deg;    // 这把枪的弹匣在图里斜多少（底 → 顶）
        grp.turn = own ? R_POUCH_MAG_TURN : 0f;                             // 袋里的弹匣 = 手里的弹匣（同一张图，不用再补角度）
        grp.axis = own ? R_MAG_AXIS : magAxis;                              // 沿弹匣往外拔的方向
        for (int k = 0; k < grp.pouches.Count; k++)
        {
            MagPouch p = grp.pouches[k];
            Transform t = p.mag.transform;
            if (own) { p.mag.sprite = _pouchArt[k]; t.localPosition = _pouchPos[k]; t.localRotation = _pouchRot[k]; p.bottom = _pouchBottom[k]; continue; }
            int i = System.Array.IndexOf(_mountRifleMags, p.mag);           // 第几个弹匣袋
            p.mag.sprite = gunMag.sprite;                                   // 这把枪的弹匣（轴心 = 弹匣底部）
            t.localPosition = CanvasToLocal(P_RIFLE_MAG_BOTTOM[Mathf.Max(0, i)] - P_MOUNT);   // 底部对准弹匣袋的底
            t.localRotation = Quaternion.Euler(0f, 0f, R_MAG_AXIS - magAxis);   // 斜得和角色画的步枪弹匣一样
            p.bottom = Vector2.zero;
        }
    }

    void BuildChestRig()
    {
        _chest = new ChestRigReload();                                      // npc2 身上一直有胸挂（gear = null：不用检查）
        PouchGroup pistol = new PouchGroup { hold = HoldType.Pistol, turn = POUCH_MAG_TURN, axis = MAG_AXIS, pullOut = () => rlPullOut };
        foreach (int i in POUCH_ORDER)
            pistol.pouches.Add(new MagPouch { mag = _mountMags[i], bottom = CanvasToLocal(P_PISTOL_MAG_BOTTOM[i] - P_PISTOL_MAG[i]) });
        PouchGroup rifle = new PouchGroup { hold = HoldType.Rifle, turn = R_POUCH_MAG_TURN, axis = R_MAG_AXIS, pullOut = () => rrPullOut };
        foreach (int i in R_POUCH_ORDER)
            rifle.pouches.Add(new MagPouch { mag = _mountRifleMags[i], bottom = CanvasToLocal(P_RIFLE_MAG_BOTTOM[i] - P_RIFLE_MAG[i]) });
        _chest.groups.Add(pistol);
        _chest.groups.Add(rifle);
        _chest.switchTime = () => nrSwitchTime;
        _chest.onChanged = () => { rigMags = _chest.Describe(HoldType.Pistol); rigRifleMags = _chest.Describe(HoldType.Rifle); };
        AddReloadGear(_chest);
        _chest.RefillAll();
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
    // 步枪取出动画：动画本身在 CharacterRigBase.Draw.cs（枪械通用）。这里只告诉它：枪挂在胸挂哪、怎么显示 / 藏、平时的手
    // ============================================================================
    protected override bool DrawMountPose(GunRig g, out Vector2 center, out float angle, out float scale)
    {
        center = Vector2.zero; angle = 0f; scale = 1f;
        if (_mount == null || g == null) return false;
        Vector3 mc = _rig.InverseTransformPoint(_mount.TransformPoint(CanvasToLocal(MOUNT_RIFLE_CENTER - P_MOUNT)));   // 胸挂步枪的中心（随胸挂晃）
        Vector3 md = _rig.InverseTransformVector(_mount.TransformVector(Vector3.right));
        center = new Vector2(mc.x, mc.y);
        angle = Mathf.Atan2(md.y, md.x) * Mathf.Rad2Deg + MOUNT_RIFLE_TURN;   // 和胸挂上的一样斜
        scale = MountScale(g.def);
        return true;
    }

    protected override void ShowMountedGun(GunRig g, bool show) { if (_mountRifle != null) _mountRifle.enabled = show; }   // 用部件拼的那把在 AfterFrame 里跟着它

    protected override void DrawBodyHands(bool main, bool support)
    {
        if (_bodyHandL != null) _bodyHandL.enabled = main;                 // 前手（持枪手那边）
        if (_bodyHandR != null) _bodyHandR.enabled = support;              // 后手（辅助手那边）
    }

    protected override void OnGunDrawFinished() { if (_held == "rifle") { _rifleLightOn = true; _lightOnT = 0f; } }   // 打开步枪手电（闪一下再亮）

    // 测试用的选项（Inspector 里显示中文）
    public enum TestHeld
    {
        [InspectorName("不测试")] None,
        [InspectorName("手枪")] Pistol,
        [InspectorName("步枪")] Rifle,
        [InspectorName("战术匕首")] Dagger,
    }
}
