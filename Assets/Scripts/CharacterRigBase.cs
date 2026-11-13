using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;          // NavMeshAgent：读 NPC 现在走多快
using UnityEngine.Rendering;   // SortingGroup：让拼出来的二十几个部件作为一个整体参与前后遮挡
using UnityEngine.InputSystem;   // 测试用：键盘 A / D / Shift

// ============================================================================
// CharacterRigBase —— 拼装角色（NpcRig / NpcRigB / 以后的 npc3……）共用的部分。
//
// 【这里放什么】两个角色里一字不差的代码和参数：受击 / 倒地 / 复活、掉落的武器和弹匣、着弹点效果、
//   枪火灯光、子弹飞行、脚步 / 手的摆动、锁定目标、以及一堆小工具（Rot、Ease、Sq……）。
//   以后修这些地方的 bug，只改这里一次，所有角色一起生效。
// 【每个角色自己写的】下面标 abstract 的几样（朝向、动画主循环 Step、头甩、跳弹、复活后的恢复）
//   在 NpcRig / NpcRigB 里各写各的（override）。
// 【默认值】个别参数两个角色默认值不同（比如 impactScale）：这里用 NpcRig 的，NpcRigB 在构造函数里改成自己的。
// 【Inspector】放在这里的参数在 Inspector 里排在最上面（Unity 先显示父类的字段），数值照旧保存，不会丢。
// ============================================================================
public abstract partial class CharacterRigBase : MonoBehaviour
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

    [Range(0f, 2f)] public float gearAmount = 1f;             // 挂件 / 水瓶 / 拉链

    [Range(0f, 2f)] public float hoseAmount = 1f;             // 软管
    public Vector2 recoilKick = new Vector2(80f, 100f);       // 开枪时枪口上扬多少度：每枪在这个范围里随机（最小 / 最大）
    public float slideKick = 150f;                            // 开枪时滑套往后弹多远（原图像素）（标准值）
    public float bulletSpeed = 100f;                          // 子弹飞多快（单位 / 秒）（标准值）
    public float flashTime = 0.07f;                           // 枪火：① 枪火（火团）亮多久（秒）
    public float flashSize = 2f;                              // 枪火整体多大（倍数；1 = 按 PSD 画的大小；你在测试场景调成了 2）

    [Range(0f, 1f)] public float ricochetChance = 0.3f;       // 子弹没打中、落到地上时出跳弹效果的概率
    public float ricochetTime = 0.15f;                        // 跳弹效果显示多久（秒）
    public Vector2 recoilHandX = new Vector2(-0.4f, -0.2f);    // 开枪时手往后顶多远：每枪在这个范围里随机（最小 / 最大）
    public Vector2 recoilHandY = new Vector2(0.1f, 0.3f);      // 开枪时手往上抬多高：每枪在这个范围里随机（最小 / 最大）
    public float handRecoilRise = 0.03f;                       // 手被顶回去用几秒（越小越猛）
    public Vector2 handRecoilReturn = new Vector2(0.18f, 0.24f); // 手回正的快慢（越大回得越慢）：每枪随机（最小 / 最大）
    public Vector2 muzzleRise = new Vector2(0.03f, 0.15f);     // 枪口上扬用几秒：每枪随机（最小 / 最大；上扬角度是上面的 Recoil Kick）
    public float muzzleReturn = 0.09f;                         // 枪口回正的快慢（越大回得越慢；可以和手不一样）
    public float recoilLean = 3f;                             // 开枪时身体被震得往后仰
    public float actionShift = 0.012f;                        // 身体跟着前后挪多远（单位，乘在前倾 / 后仰的比例上）

    [Header("换弹 1：战术换弹（时间 = 秒，距离 = 单位，角度 = 度）")]
    public float reloadSpeed = 1f;                            // 测试时整体播放速度（游戏里会自动对齐战斗的换弹时间 2.1 秒；各段时间加起来改了，记得同步 NpcCombat 的 ReloadTime）
    public float rlLockTime = 0.12f;                          // ① 滑套空仓挂机，整把枪往上抬、往前送
    public float rlFetchTime = 0.2f;                         // ② 右手离开握把，伸到身体后面
    public float rlGrabTime = 0.28f;                          // ③ 在身后拿到新弹匣
    public float rlBringTime = 0.30f;                         // ④ 拿着新弹匣到握把下方
    public float rlDropTime = 0.3f;                          // ⑤ 枪抖一下，旧弹匣掉进右手（两个弹匣成 V 字）
    public float rlAlignTime = 0.1f;                         // ⑥ 右手和枪一起转，新弹匣对准握把口
    public float rlInsertTime = 0.10f;                        // ⑦ 插入新弹匣
    public float rlReleaseTime = 0.3f;                       // ⑧ 弹匣插到底枪上下抖 → 滑套复位枪旋转抖（和 ⑨ 同时开始）
    public float rlStowTime = 0.4f;                          // ⑨ 右手带着旧弹匣回到身后（④ 倒着走）
    public float rlReturnTime = 0.30f;                        // ⑩ 右手回到握把，枪放下（② 倒着走）
    public float rlSlideLock = 1f;                            // 空仓挂机时滑套往后多少（1 = 开枪时滑套最远的位置）
    public Vector2 rlGunRaise = new Vector2(0.03f, 0.06f);    // 枪往前（x）/ 往上（y）抬多少
    public float rlGunTilt = 30f;                              // 换弹时持枪手（连枪）整体转多少度（正 = 枪口往上，负 = 往下）；① 抬枪时转过去，⑩ 放下时转回来
    public float rlGunAlign = 30f;                           // ⑥ 枪配合右手转多少度（正 = 枪口往上，负 = 往下）
    public float rlArc = -0.05f;                               // 右手来回走弧线时往上拱多高
    public Vector2 rlMagInHand = new Vector2(0f, 0.012f);     // 弹匣底部在右手里的位置（手盖住弹匣底部）
    public float rlUnderGap = 0.03f;                          // ④ 新弹匣顶端离握把口还有多远
    public float rlReady = 0.085f;                            // ⑥ 对准时新弹匣还差多远插到底
    public float rlNewTilt = 18f;                             // V 字：新弹匣往后歪多少度
    public float rlOldTilt = -18f;                            // V 字：旧弹匣往前歪多少度
    public Vector2 rlOldOffset = new Vector2(0.012f, 0f);     // V 字：旧弹匣相对新弹匣往前挪多少
    public float rlShake = 0.1f;                              // 上下抖的幅度（位移）：⑤ 旧弹匣掉出来、⑦ 新弹匣插到底
    public float rlShakeAngle = 45f;                          // 旋转抖的幅度（角度）：⑤ 旧弹匣掉出来、⑧ 滑套复位
    public float rlSlideDelay = 0.3f;                         // 弹匣插到底后隔多久滑套复位（0 = 同时）
    public float rlShakeFreq = 5f;                           // 抖动有多快（次 / 秒）

    [Header("换弹 2：快速换弹（只列和战术换弹不一样的；其他时间 / 姿势都用上面战术换弹的）")]
    public float qrDropAt = 0f;                               // 开始换弹后多久旧弹匣掉出来（0 = 枪口一往上抬就掉，不等右手）
    public float qrDropTime = 0f;                             // ⑤ 右手拿到握把下方后停多久再对准（旧弹匣早就掉了；0 = 不停，代替战术换弹的 ⑤）
    public Vector2 qrWait = Vector2.zero;                     // ④ 右手拿着新弹匣在握把下方往旁边让多少（按枪的方向：x 负 = 往后；旧弹匣早掉了，一般不用让）
    public float qrSlideSpeed = 0.6f;                         // 旧弹匣从握把里滑出来的初速度（单位 / 秒；越大掉得越远）
    public float qrGravity = 5f;                              // 旧弹匣往下掉的重力（单位 / 秒²；越大掉得越快）
    public Vector2 qrKick = new Vector2(-0.1f, 0.1f);         // 旧弹匣滑出来后往前（正）/ 往后（负）飘的速度：在范围里随机（单位 / 秒）
    public Vector2 qrSpin = new Vector2(-500f, -250f);        // 旧弹匣边掉边转多快：在范围里随机（度 / 秒；负 = 往前翻，正 = 往后翻）
    public Vector2 qrGroundY = new Vector2(-0.05f, 0.03f);    // 落到多高算落地：在范围里随机（相对脚底；0 = 和脚底平齐）
    public float qrGroundTime = 3f;                           // 在地上躺多久再淡出消失（秒）

    [Header("换弹 3：普通换弹（只列和战术换弹不一样的；其他时间 / 姿势都用上面战术换弹的）")]
    public float nrCatchTime = 0.15f;                         // ② 右手从握把移到握把下方，准备接旧弹匣

    [Header("瞄准（拿枪时锁定目标，枪口一直对着它；角度 = 度）")]
    public Vector2 aimShoulder = new Vector2(0.175f, 0.421f); // 持枪手绕着转的点（相对脚底中心；默认在身体右边缘里面、和握把一样高；设成握把位置 = 原地转）
    public float aimTurnSpeed = 720f;                         // 枪口转向目标有多快（度 / 秒；开枪那一下直接对准）
    public float aimFlipOver = 5f;                            // 枪口超过 ±90° 后再多转多少度才翻身（防止目标在正上 / 正下方时来回翻；0 = 正好 90°）
    public float lowReadyAngle = -45f;                        // 没遇敌、不瞄准时，持枪手（手枪）朝下多少度（负 = 朝下）
    public float lowReadySpeed = 180f;                        // 放下枪有多快（度 / 秒；举枪瞄准用上面的 Aim Turn Speed）
    public float lowReadyHold = 1.5f;                         // 开完枪多久之后才放下枪（秒；没锁定目标时开枪也会先把枪举平）
    public Vector2 lowReadyOffset = new Vector2(0f, -0.08f);  // 放低持枪时整只手（连枪）再往哪挪（x 往前，y 往上；单位）。选中 NPC 时 Scene 窗口里有标记
    public Color muzzleLightColor = new Color(1f, 0.72f, 0.38f);   // 开枪时火光照亮周围的颜色
    public float muzzleLightIntensity = 5f;                   // 开枪火光亮度（原来 2.5，加强了）
    public float muzzleLightRadius = 3.2f;                    // 开枪火光照多远（单位；原来 2.2）
    public Vector2 muzzleLightRandom = new Vector2(0.7f, 1.3f);   // 每一枪火光亮度的倍数（最暗原来 0.4，提到 0.7）：在这个范围里随机（每枪亮度都不一样，闪的过程中也会轻微跳动）

    [Header("子弹曳光（所有子弹都有：子弹自己微微发光 + 照亮周围一点点）")]
    public bool tracer = true;                                // 开 / 关曳光
    public Color tracerColor = new Color(1f, 0.75f, 0.42f);   // 曳光颜色（暖橙，和枪火一个色调）
    public float tracerLight = 0.3f;                          // 照亮周围的亮度（微弱；0 = 只发光不照亮）
    public float tracerRadius = 0.6f;                         // 照多远（单位）
    public float tracerGlow = 0.45f;                          // 子弹本身光晕的亮度（0 = 不要光晕）
    public Vector2 tracerGlowSize = new Vector2(0.5f, 0.08f); // 光晕拖尾的长 / 宽（单位；拖在弹头后面）

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
    public float deathGroundLift = 0.08f;                    // 躺平后离地多高（防止身体 / 背包陷进地里；单位）
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
    public float impactScale = 2f;                            // 效果大小
    public float impactHeight = 0.2f;                         // 测试时中弹的位置：腰往上多高（单位，大约胸口）

    [Range(0f, 1f)] public float fleshChance = 1f;           // 打中时出血的概率（其余是打在装甲 / 装备上的火花）
    public float fleshLife = 0.45f;                           // 血雾持续多久（秒）
    public float metalLife = 0.12f;                           // 火花持续多久（秒）
    public Color metalLightColor = new Color(1f, 0.8f, 0.55f);   // 火花照亮周围的颜色
    public float metalLight = 0.8f;                           // 火花照亮周围的亮度（微弱；0 = 不照亮）
    public float metalLightRadius = 0.6f;                     // 火花照多远（单位）
    public bool testLoop = true;                              // 斧头 / 锤子一直循环挥；手枪每 1.4 秒开一枪
    public bool testFire;                                     // 勾一下：挥一下 / 开一枪（播完自动取消勾选）
    public bool testReload;                                   // 勾一下：拿枪时换一次弹匣（播完自动取消勾选；手枪循环开枪时会先暂停）

    [Range(-1f, 4.5f)] public float testSpeed = -1f;          // 强制移动速度（原地踏步看走跑）：-1 = 不用；1.65 闲逛，3.0 赶路，3.9 逃跑
    public bool testFaceLeft;                                 // 强制朝左（勾上时不会自动翻身）
    public Transform testAimTarget;                           // 拖一个场景里的物体进来：一直锁定它（拿枪时运行中拖动它，看枪口跟着转、过头顶翻身）

    protected abstract bool FacingLeft { get; }
    protected float _aimUntil;               // 瞄准朝向管到什么时候（NpcCombat 边走边打时一直续上）
    protected bool _aimLeft;                 // 瞄准时朝左吗
    protected Transform _lockTarget;         // 锁定的目标（每帧跟着它的位置瞄）
    protected Vector3 _aimPoint;             // 瞄准点（世界坐标）
    protected float _aimAng;                 // 现在枪口的瞄准角度（度；0 = 正前方，正 = 往上；按角色朝向算）
    protected float _aimWant;                // 想转到的瞄准角度
    protected float _readyUntil;             // 枪举平到什么时候（开完枪一会儿内不放下）
    protected float _lowK;                   // 放低持枪的程度（0 = 举着，1 = 完全放低），手的整体下移按它渐变

    // 外部调用（NpcCombat 拿枪战斗时每帧调）：锁定一个目标，枪口一直跟着它转；停止调用 0.3 秒后自动解除。传 null = 马上解除
    public void LockOn(Transform target)
    {
        if (target == null) { _lockTarget = null; _aimUntil = 0f; return; }   // 解除锁定
        if (_aimUntil <= _time) _aimLeft = FacingLeft;  // 刚开始锁定：先保持现在的朝向
        _lockTarget = target;
        _aimPoint = target.position;
        _aimUntil = _time + 0.3f;
    }

    // 世界坐标 → 角色根节点（脚底中心）的本地坐标，朝左时 x 反过来（x 正 = 前方）
    protected Vector2 AimLocal(Vector3 world)
    {
        Vector3 p = transform.InverseTransformPoint(world) - (_rig != null ? _rig.localPosition : Vector3.zero);
        return new Vector2(FacingLeft ? -p.x : p.x, p.y);
    }

    // 换弹的种类（Inspector 里显示中文）
    public enum ReloadKind
    {
        [InspectorName("战术换弹")] Tactical,
        [InspectorName("快速换弹")] Speed,
        [InspectorName("普通换弹")] Normal,
        [InspectorName("颤抖换弹")] Shaky,
    }

    // ---------------- 固定换算（和导出素材时的设置一致，不要改） ----------------
    protected const float PPU_FULL = 1755f / 0.90625f;  // 原图（2048 画布）上每 1 单位有多少像素：身体 1755 像素 = 0.906 单位（和旧 NPC 图一样高）
    protected const float REF = PPU_FULL * 0.27f;       // 调参用的"参考像素"：惯性参数是在预览视频里按这个刻度调好的
    protected const float FEET_Y = -0.453125f;          // 旧 NPC 图的轴心在中间，脚底在它下面 0.453 单位（NavMeshAgent 的 baseOffset）
    protected NavMeshAgent _agent;           // 读速度
    protected SpriteRenderer _oldSprite;     // 旧 NPC 图：变透明，但保留（读它的 flipX 决定朝向）
    protected bool _built;                   // 角色拼好了没有
    protected Transform _rig;
    protected Transform _body;
    protected Transform _footL;
    protected Transform _footR;
    protected Transform _handL;
    protected Transform _handR;
    protected Vector3 _footLBase;
    protected Vector3 _footRBase;
    protected Vector3 _handLBase;
    protected Vector3 _handRBase;
    protected Vector3 _bodyBase;
    protected readonly Dictionary<string, Transform> _gear = new Dictionary<string, Transform>();    // 挂件的转动节点
    protected readonly Dictionary<string, Vector3> _gearBase = new Dictionary<string, Vector3>();    // 挂件原始位置
    protected string _held;                  // 现在手上拿的是什么（null = 空手）
    protected Transform _rlHand;             // 换弹时的右手（挂在角色根节点上，单独移动）
    protected SpriteRenderer _rlHandSprite, _rlNewMag, _rlOldMag;   // 换弹时的右手 / 新弹匣 / 旧弹匣
    protected float _reloadT = -1f;          // 换弹进行到哪（"标准时间"秒；-1 = 没在换弹）
    protected float _reloadRate = 1f;        // 换弹播放速度（游戏里按战斗的换弹时间算出来）
    protected bool _qrDropped;               // 快速换弹：旧弹匣已经整个滑出来、自己往下掉了
    protected Transform _handPoint;          // 手点：拿东西的手挂在它下面，挥动 / 开枪时移动它；空手时跟着前手（外部也可以用它挂特效）
    protected float _actionLean, _actionShift;  // 出手时身体额外的倾斜（度）/ 前后挪动（单位）
    protected Transform _muzzle;             // 枪口子弹生成点（跟着枪一起动）
    protected Vector3 _slideBase;
    protected float _swingT = -1f;           // 挥动进行了多久（-1 = 没在挥）
    protected float _recoilT = -1f;          // 开枪后坐进行了多久（-1 = 没在后坐）
    protected bool _swingNew = true;         // 要开始新的一下（重新随机挥下时手的 y）
    protected float _swingDownY;             // 这一下挥下时手的 y（随机出来的）
    protected Vector2 _recoilHandOff;        // 这一枪手被顶回的距离（随机出来的）
    protected float _shotKick, _shotHandReturn, _shotMuzzleRise;  // 这一枪的枪口上扬角度 / 手回正快慢 / 枪口上扬用时（随机出来的）

    // ---------------- 物理状态 ----------------
    protected float _theta;                    // 步伐相位（0~2π 一个完整周期）
    protected float _phaseSeed;                // 每个 NPC 呼吸节奏错开一点
    protected float _prevRefV;
    protected float _prevBob;
    protected float _prevBobV;
    protected float _prevTilt;
    protected float _prevTiltV;
    protected float _move, _run, _sv, _bobUp, _tilt, _time;

    // 原图上的像素距离 → Unity 的本地坐标（原图 y 向下，Unity y 向上，所以 y 取反）
    protected static Vector3 CanvasToLocal(Vector2 d) { return new Vector3(d.x / PPU_FULL, -d.y / PPU_FULL, 0f); }

    protected static float Sq(float x) { return x * x; }

    // 发光圆点图：中间亮、往外柔和变暗的白色圆（颜色由 SpriteRenderer 染）
    protected static Sprite GlowSprite()
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
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 640f);   // 约 0.1 单位大（圆灯的 3 倍左右）
    }

    // 开枪火光：枪口那一下把周围照亮，很快暗下去
    protected IEnumerator MuzzleLight(Vector3 at)
    {
        UnityEngine.Rendering.Universal.Light2D l = WorldLighting.CreatePoint("MuzzleLight", null, muzzleLightColor, muzzleLightIntensity, muzzleLightRadius, 360f);
        if (l == null) yield break;
        WorldLighting.EnableShadows(l);                                   // 枪火也投影子
        l.transform.position = at;
        Destroy(l.gameObject, 1f);                                        // 保险：一定删掉
        float life = Mathf.Max(0.03f, flashTime * 1.3f), t = 0f;
        float peak = muzzleLightIntensity * RandIn(muzzleLightRandom);    // 这一枪有多亮（随机）
        while (l != null && t < life)
        {
            l.intensity = peak * (1f - t / life) * Random.Range(0.8f, 1f); // 越来越暗，中间轻微跳动
            t += Time.deltaTime;
            yield return null;
        }
        if (l != null) Destroy(l.gameObject);
    }
    protected float _testFireTimer;

    protected IEnumerator FlyBullet(Transform b, Vector3 from, Vector3 to, float dur, bool canRicochet)
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
        if (canRicochet && Random.value < ricochetChance) Ricochet(to, to.x < from.x);   // 落到地上：有概率跳弹
    }

    protected abstract void Ricochet(Vector3 at, bool left);

    protected static float Smooth01(float a) { a = Mathf.Clamp01(a); return a * a * (3f - 2f * a); }

    // 在范围里随机取一个数（两个数谁大谁小都行）
    protected static float RandIn(Vector2 r) { return Random.Range(Mathf.Min(r.x, r.y), Mathf.Max(r.x, r.y)); }

    // 0~1 平滑过渡（两头慢、中间快）
    protected static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // 某一段的进度：t 在 [a, a+len] 之间时从 0 走到 1
    protected static float Seg(float t, float a, float len) { return len <= 0f ? (t >= a ? 1f : 0f) : Mathf.Clamp01((t - a) / len); }

    // 二维向量转一个角度（度）
    protected static Vector2 Rot(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // 抖一下：开始后 tau 秒的抖动量（-1~1，很快衰减）
    protected float Shake(float tau, float len)
    {
        if (tau < 0f || tau > len) return 0f;
        return Mathf.Sin(2f * Mathf.PI * rlShakeFreq * tau) * Mathf.Exp(-tau * 12f);
    }

    // 旧弹匣往下掉 → 落地躺平 → 留在原地（不再跟着人走）→ 过一会儿淡出消失
    protected IEnumerator FallMag(SpriteRenderer sr, Vector2 pos, float ang, Vector2 vel, float spin, float groundY)
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
        if (_oldSprite != null) sr.sortingLayerID = _oldSprite.sortingLayerID;   // 和人同一个排序层
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

    // 正在换弹吗（颤抖换弹掉弹匣 / 卡住会比算好的时间长，游戏规则之后可以用它等换完）
    public bool IsReloading => _reloadT >= 0f;

    // 把游戏里的速度换算成"预览刻度"的速度（像素/秒），惯性参数就能直接沿用预览里调好的数。
    protected float RefSpeed(float speed, float run)
    {
        return 170f * Mathf.Clamp01(speed / 3.0f) + 260f * run;  // 赶路 3.0 ≈ 预览的走路，逃跑 ≈ 预览的跑步
    }


    protected void PlaceFoot(Transform t, Vector3 basePos, float ph)
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

    protected void PlaceHand(Transform t, Vector3 basePos, float sw, float idleY)
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
    protected bool _dead, _settled, _weaponDropped, _deadLeft;   // 死了 / 已经躺稳 / 武器已经脱手 / 死的时候朝左
    protected float _hitAng, _hitV, _shove, _shoveV;             // 受击：身体后仰角度和角速度 / 被推开的距离和速度（本地，正 = 往前）
    protected float _deathSlideMul = 1f;                         // 倒下时往后滑的倍数（步枪打死 = 0：原地倒下）
    protected float _dieAng, _dieV, _dieSign = 1f, _deadT;       // 倒地：角度和角速度（本地，正 = 往后倒）/ 往哪边倒 / 死了多久
    protected float _reactAcc, _prevReactV, _groundY;            // 身体整体转动的角加速度（甩挂件用）/ 上一步的角速度 / 地面高度（世界）
    protected string _heldBeforeDeath;                           // 死之前手上拿着什么（站起来时拿回来）
    protected Vector3 _prevHandWorld;                            // 上一帧手点的世界坐标（算武器脱手时的速度）
    protected readonly List<GameObject> _dropped = new List<GameObject>();   // 掉在地上的武器（站起来 / 人删掉时一起收走）

    //                       频率  阻尼  加减速 跑动 跟身体转 颠动 最大角度 吊着的程度
    protected readonly Pend _armL = new Pend(2.2f, 0.18f, 0f, 0f, 2.5f, 0f, 70f, 0.2f);   // 死后前手：软了，跟着甩
    protected readonly Pend _armR = new Pend(2.0f, 0.18f, 0f, 0f, 2.5f, 0f, 70f, 0.2f);   // 死后后手（频率不一样，两只手不会一起甩）

    // （只读）死了没有
    public bool IsDead => _dead;

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

    // 测试：子弹飞的方向（从前面打来 = 往角色身后飞；带一点上下随机）
    protected Vector2 TestHitDir()
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
    protected void SpawnImpact(Vector3 at, Vector2 dir, float strength, bool fatal)
    {
        if (!impactFx) return;
        if (fatal || Random.value < fleshChance)
        {
            int v = fatal || strength >= 1.5f ? 2 : (Random.value < 0.5f ? 0 : 1);   // 普通一下：小 / 中随机
            HitFx.Flesh(at, dir, v, impactScale * Random.Range(0.85f, 1.15f), fleshLife, ImpactLayer());
        }
        else HitFx.Metal(at, impactScale, metalLife, metalLightColor, metalLight, metalLightRadius, ImpactLayer());
    }

    protected int ImpactLayer() { return _oldSprite != null ? _oldSprite.sortingLayerID : 0; }   // 和人同一个排序层

    // 子弹飞的方向 → 人被推的方向（本地：+1 = 往角色前面推，-1 = 往后推）
    protected static float PushLocal(Vector2 dir, bool left)
    {
        float x = dir.x * (left ? -1f : 1f);                                // 换成角色自己的左右
        return Mathf.Abs(x) < 0.001f ? -1f : Mathf.Sign(x);                 // 没方向（正上 / 正下）：当作从前面打来
    }

    // 吃一下冲击：后仰的弹簧、被推开的弹簧都给一个初速度（之后弹簧自己回正）
    protected void HitKick(float push, float k, bool knock = true)
    {
        float w = 2f * Mathf.PI * Mathf.Max(0.1f, hitFreq);                 // 弹簧的角频率
        _hitV += -push * hitLean * w * 1.6f * k;                            // 上身往推的方向仰（往前推 = 往前栽）
        float ws = w * 0.6f;
        if (knock) _shoveV += push * hitShove * ws * 2.718f * k;            // 整个人往推的方向挪（knock = false：不推开，只是上身一震）
        HeadWhip(push, k);                                                  // 头再多甩一下
    }

    // 每小步：受击的两个弹簧 + 倒地（重力让人越倒越快，摔到地上弹几下停住）+ 死后手臂
    protected void StepReact(float dt)
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
    protected void ApplyReact()
    {
        float s = (_dead ? _deadLeft : FacingLeft) ? -1f : 1f;
        float sink = _dead ? deathSink * Smooth01(_deadT / Mathf.Max(0.01f, deathSinkTime)) : 0f;     // 腿一软往下沉
        sink += hitBuckle * Mathf.Clamp01(Mathf.Abs(_hitAng) / Mathf.Max(0.1f, hitLean));            // 挨打往下一沉
        _body.localPosition += new Vector3(0f, -sink, 0f);
        _body.localRotation = _body.localRotation * Quaternion.Euler(0f, 0f, _hitAng);               // 上身仰 / 栽（绕腰）
        float fall = _dead ? Mathf.Clamp01(Mathf.Abs(_dieAng) / Mathf.Max(1f, deathLieAngle)) : 0f;   // 倒了多少（0~1）
        float slide = _dead ? -_dieSign * deathSlide * _deathSlideMul * Smooth01(fall) : 0f;         // 被冲击带着往倒的方向滑
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

    // 把手上那件东西复制一份扔进世界（不挂在人身上）。skip = 名字里带这个的部件不复制（持枪手）；
    // single 不为空时不复制，直接用这张图（拿着的图连着手的东西，掉下去用腰上挂的那张）
    protected void SpawnDropped(Transform src, string skip, Sprite single, int singleOrder, float dt, Color lightColor, float lightIntensity, float lightRadius)
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
    protected IEnumerator FlyDropped(Transform t, Vector2 vel, float spin)
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

    protected abstract void HeadWhip(float push, float k);

    protected abstract void RestoreAfterRevive();

    // ============================================================================
    // 小工具类
    // ============================================================================

    // 二维弹簧（偏移量，单位：预览像素；y 向下为正）
    protected class Spring2
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
    protected class Pend
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


    // ================= 角色自己的属性：素材文件夹 =================
    [Header("素材")]
    [Tooltip("这个角色的部件图在 Resources 下的哪个文件夹（npc1 = NpcArt，npc2 = NpcArt2，以后的 npc3 填 NpcArt3……）。同一套代码按这个去读各自的图")]
    public string artFolder = "NpcArt";

    // 读一张部件图（按这个角色的素材文件夹）
    protected Sprite Load(string path)
    {
        Sprite s = Resources.Load<Sprite>(artFolder + "/" + path);
        if (s == null) Debug.LogWarning("[" + GetType().Name + "] 找不到素材：" + artFolder + "/" + path);
        return s;
    }

    // 建一个部件：精灵放在 parent 下，位置 = 它的转动点相对 parent 转动点的距离。
    protected SpriteRenderer Part(string name, Transform parent, string path, Vector2 pivot, Vector2 parentPivot, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = CanvasToLocal(pivot - parentPivot);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Load(path);
        sr.sortingOrder = order;                                 // 在 SortingGroup 里的前后顺序
        return sr;
    }

    // ================= 受击 / 倒地（所有角色同一套；轻重、倒向由各自的参数 hit… / death… 决定）=================
    // 倒下时停掉手上正在做的动作（每个角色手上的东西不一样，各自写）
    protected abstract void StopForDeath();

    // 外部调用：挨了一下。dir = 子弹 / 攻击飞的方向（世界坐标，比如 被打的人 - 开枪的人）；strength = 多重（1 = 正常）
    public void PlayHit(Vector2 dir, float strength = 1f)
    {
        if (!_built || _dead) return;                                       // 还没拼好 / 已经死了
        PlayHit(dir, strength, HitPoint(dir));                              // 没给打中的点：当作打在胸口
    }

    // 同上；point = 打中的位置（世界坐标，着弹点效果从这里出来）；fx = 要不要放着弹点效果（子弹打中时 Ballistics 已经放过了，传 false）
    // knock = 要不要把人推开（步枪子弹传 false：只是上身一震，人不被推走）
    public void PlayHit(Vector2 dir, float strength, Vector3 point, bool fx = true, bool knock = true)
    {
        if (!_built || _dead) return;
        HitKick(PushLocal(dir, FacingLeft), Mathf.Max(0f, strength) * Random.Range(0.8f, 1.2f), knock);   // 每一下轻重有点不一样
        if (fx) SpawnImpact(point, dir, strength, false);                   // 着弹点效果
    }

    // 外部调用：被打死。朝 dir（子弹飞的方向）倒下：前面中弹往后倒，背后中弹往前扑
    public void PlayDeath(Vector2 dir, float strength = 1f)
    {
        if (!_built || _dead) return;
        PlayDeath(dir, strength, HitPoint(dir));                            // 没给打中的点：当作打在胸口
    }

    // 同上；point = 打中的位置（世界坐标）；fx = 要不要放着弹点效果（同 PlayHit）
    // knock = 倒下时要不要被冲击带着往后滑（步枪传 false：原地倒下）
    public void PlayDeath(Vector2 dir, float strength, Vector3 point, bool fx = true, bool knock = true)
    {
        if (!_built || _dead) return;
        if (fx) SpawnImpact(point, dir, strength, true);                    // 着弹点效果（致命的一下：大团血雾）
        _deadLeft = FacingLeft;                                             // 倒下后不再翻身
        float push = PushLocal(dir, _deadLeft);                             // 被往哪边推（本地）
        float k = Mathf.Max(0f, strength) * Random.Range(0.85f, 1.15f);
        HitKick(push, k, knock);                                            // 先吃一下冲击（身体一震、头一甩）
        _deathSlideMul = knock ? 1f : 0f;                                   // 步枪：不往后滑
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

    // ================= 后来合并进来的共用部分（两个角色一模一样）=================
    public ReloadKind testReloadKind = ReloadKind.Tactical;   // 勾"测试换弹"时换哪一种
    protected ReloadKind _reloadKind = ReloadKind.Tactical;   // 这次换弹是哪一种
    // 受击 / 死亡的测试开关（勾一下做一次，自动取消勾选）
    protected void UpdateReactTest()
    {
        if (testRevive) { testRevive = false; Revive(); }
        if (testHit) { testHit = false; PlayHit(TestHitDir(), testHitStrength); }
        if (testDeath) { testDeath = false; PlayDeath(TestHitDir(), testHitStrength); }
    }


    // ============================================================================
    // 通用人形骨架：每帧流程（所有角色一样；角色自己的部分通过下面的钩子插进来）
    //   速度 → 瞄准 → 朝向 → 走路物理（受击先算）→ 角色自己的晃动件 → 手上的动作 → 摆部件 → 受击 / 倒地 → 灯光
    // ============================================================================
    protected virtual void Update()
    {
        if (!_built) return;
        BeforeFrame();                                            // 角色自己：每帧最先做的事
        float dt = Mathf.Min(Time.deltaTime, 0.05f);              // 卡顿时不要一步跳太远
        if (dt <= 0f) return;                                     // 暂停时不动
        UpdateReactTest();                                        // 受击 / 死亡的测试开关

        // 速度：只看水平面上的移动快慢（NavMeshAgent）；角色可以改（测试速度等）
        Vector3 v3 = _agent != null && _agent.enabled ? _agent.velocity : Vector3.zero;
        float speed = AdjustSpeed(new Vector2(v3.x, v3.y).magnitude, v3, dt);
        if (_dead) speed = 0f;                                    // 死了：不走

        if (!_dead) UpdateAim(dt);                                // 瞄准（可能会翻身，所以放在定朝向前面）
        _rig.localScale = new Vector3((_dead ? _deadLeft : FacingLeft) ? -1f : 1f, 1f, 1f);   // 朝左 = 整个角色水平翻转

        const int SUB = 4;                                        // 每帧分 4 小步算，弹簧更稳
        for (int i = 0; i < SUB; i++) { StepReact(dt / SUB); Step(dt / SUB, speed); }   // 受击 / 倒地先算，挂件才能被它甩
        AfterLocomotion(dt, speed);                               // 角色自己：表情、换手……
        if (!_dead) UpdateTest(dt);                               // 测试开关
        AnimateHeld(dt);                                          // 手上的动作（挥动 / 后坐 / 换弹）
        if (_boltT >= 0f && (_dead || _gun == null || _reloadT >= 0f || _drawT >= 0f)) StopBolt();   // 拉栓途中换枪 / 换弹 / 取出 / 倒下：停掉
        ApplyPose();                                              // 通用骨架：身体 / 脚 / 手
        ApplyAttachments();                                       // 角色自己：挂件、头、软管……
        ApplyReact();                                             // 叠上受击 / 倒地
        if (_dead && !_weaponDropped && Mathf.Abs(_dieAng) >= weaponDropAt) DropWeapon(dt);   // 倒到一定角度：手上的东西脱手
        if (_handPoint != null) _prevHandWorld = _handPoint.position;   // 记下手点（算脱手速度）
        AfterFrame(dt, speed);                                    // 角色自己：灯光、状态显示……
    }

    // 走路物理算出来的、给角色自己的晃动件（挂件 / 头盔 / 天线……）用的数
    protected struct Motion
    {
        public float speed;   // 移动速度（单位 / 秒）
        public float move;    // 0 = 站着，1 = 正常走
        public float run;     // 0 = 走路，1 = 跑步
        public float refV;    // 参考速度（预览刻度）
        public float acc;     // 加速度（往前加速为正）
        public float sv;      // 步伐摆动位置 -1~1
        public float boba;    // 身体上下颠的加速度
        public float tilt;    // 挂件感受到的倾斜（身体摇摆 + 受击后仰 + 倒地）
        public float tilta;   // 倾斜的角加速度
    }

    // 通用走路：步伐相位、身体上下颠 / 摇摆 / 前倾；算完交给角色自己的晃动件
    protected void Step(float dt, float speed)
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

        _move = move; _run = run; _sv = sv; _bobUp = bobUp; _tilt = bodyTilt;
        StepSecondary(dt, new Motion { speed = speed, move = move, run = run, refV = refV, acc = acc, sv = sv, boba = boba, tilt = tilt, tilta = tilta });
    }

    // 通用摆位：身体（颠动 / 摇摆 / 压扁）、两只脚、两只手
    protected void ApplyPose()
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
        float idle = _dead ? 0f : 1f - move;                          // 死了：手不再轻轻浮动
        PlaceHand(_handR, _handRBase, sv, idle * 0.0038f * Mathf.Sin(2f * Mathf.PI * 0.45f * _time + 1.3f));
        PlaceHand(_handL, _handLBase, -sv, idle * 0.0038f * Mathf.Sin(2f * Mathf.PI * 0.45f * _time));
    }

    // ---------------- 角色自己填的钩子 ----------------
    protected virtual void BeforeFrame() { }                                              // 每帧最先做的事
    protected virtual float AdjustSpeed(float speed, Vector3 velocity, float dt) => speed; // 改速度（测试速度、记下走的方向……）
    protected abstract void UpdateAim(float dt);                                          // 瞄准（第 ② 批会改成由枪械决定）
    protected abstract void StepSecondary(float dt, Motion m);                            // 自己的晃动件（挂件 / 头盔 / 天线……）
    protected virtual void AfterLocomotion(float dt, float speed) { }                     // 走路算完之后（表情、换手……）
    protected abstract void UpdateTest(float dt);                                         // 测试开关
    protected abstract void AnimateHeld(float dt);                                        // 手上的动作（第 ② 批会改成由枪械决定）
    protected abstract void ApplyAttachments();                                           // 摆自己的挂件
    protected abstract void DropWeapon(float dt);                                         // 倒地时手上的东西脱手
    protected virtual void AfterFrame(float dt, float speed) { }                          // 每帧最后（灯光、状态显示……）

    // ============================================================================
    // 衣服上的挂载点（通用）
    //   骨架外面穿一层"衣服"（npc1 = 防护服，npc2 = 战术服 + 胸挂……）。衣服有两种东西：
    //     · 衣服属性：一直在的部件（天线、灯、挂环、面罩、软管、枪套……），每件衣服自己拼；
    //     · 挂载点：类型固定（MountKind），按物品栏挂上对应的挂件。只有"武器"挂载点能挂不同的武器。
    // ============================================================================
    public enum MountKind
    {
        [InspectorName("锤子")] Hammer,
        [InspectorName("斧子")] Axe,
        [InspectorName("医疗包")] Medkit,
        [InspectorName("工具包")] Toolkit,
        [InspectorName("水瓶")] Bottle,
        [InspectorName("武器（可换）")] Weapon,
        [InspectorName("步枪")] Rifle,
    }

    // 工具类挂载点（锤子 / 斧头挂环）：自己那件不在时，能挂这些工具类挂件（手雷）
    public static bool IsToolMount(MountKind k) => k == MountKind.Hammer || k == MountKind.Axe;
    public static readonly string[] ToolItems = { "hammer", "axe", "grenade" };   // 工具类挂载点能挂的东西

    // 能挂在"武器"挂载点上的东西（按优先顺序；已经有专用挂载点的武器不挂这里）
    public static readonly string[] WeaponItems = { "pistol", "rifle" };

    // 一种挂载点固定挂哪件物品（武器挂载点 = null：看 WeaponItems）
    public static string ItemOf(MountKind k)
    {
        switch (k)
        {
            case MountKind.Hammer: return "hammer";
            case MountKind.Axe: return "axe";
            case MountKind.Medkit: return "medkit";
            case MountKind.Toolkit: return "toolkit";
            case MountKind.Bottle: return "bottle";
            case MountKind.Rifle: return "rifle";
            default: return null;
        }
    }

    public class MountPoint
    {
        public string id;              // 名字（"axe" / "weapon" / "rifle"……）
        public MountKind kind;         // 类型（固定）
        public Transform anchor;       // 挂载点本身（衣服上的挂环 / 枪套 / 背带；也可以就是身体）
        public Transform attachment;   // 挂件（斧子 / 锤子 / 手枪……）
        public SpriteRenderer art;     // 挂件的图（武器挂载点换武器时换这张）
        public string item;            // 现在挂着的物品 id（null = 空着）
    }

    protected readonly List<MountPoint> _mounts = new List<MountPoint>();
    public IReadOnlyList<MountPoint> Mounts => _mounts;

    // 衣服上加一个挂载点（拼角色时调）
    protected MountPoint AddMount(string id, MountKind kind, Transform anchor, Transform attachment, SpriteRenderer art = null)
    {
        MountPoint m = new MountPoint { id = id, kind = kind, anchor = anchor, attachment = attachment, art = art };
        _mounts.Add(m);
        return m;
    }

    public MountPoint MountOf(string id)
    {
        foreach (MountPoint m in _mounts) if (m.id == id) return m;
        return null;
    }

    // 这件武器有没有自己的专用挂载点（有就不占"武器"挂载点）
    bool HasDedicatedMount(string item)
    {
        foreach (MountPoint m in _mounts) if (m.kind != MountKind.Weapon && ItemOf(m.kind) == item) return true;
        return false;
    }

    // 按物品栏把挂件挂上 / 取下：固定类型的挂载点看自己那件；武器挂载点挂一件没有专用位置的武器
    protected void LoadMounts(System.Func<string, bool> has)
    {
        bool grenadeHung = false;                                          // 手雷已经挂到一个工具挂环上了
        foreach (MountPoint m in _mounts)
        {
            string want = null;
            if (m.kind == MountKind.Weapon)
            {
                foreach (string w in WeaponItems)
                    if (has(w) && !HasDedicatedMount(w)) { want = w; break; }
            }
            else
            {
                string it = ItemOf(m.kind);
                if (it != null && has(it)) want = it;
                else if (IsToolMount(m.kind) && !grenadeHung && has("grenade")) { want = "grenade"; grenadeHung = true; }   // 工具挂环空着：挂手雷
            }
            SetMountItem(m, want);
        }
    }

    // 某个挂载点挂上 item（null = 取下）
    protected void SetMountItem(MountPoint m, string item)
    {
        if (m == null) return;
        if (item != null && (m.kind == MountKind.Weapon || m.kind == MountKind.Rifle || IsToolMount(m.kind)) && m.art != null && item != m.item)
        {
            Sprite s = MountArt(m, item);                              // 武器 / 步枪挂载点：换成这件武器的图
            if (s != null) m.art.sprite = s;
        }
        m.item = item;
        if (m.attachment == null) return;
        bool show = item != null;
        if (m.attachment.gameObject.activeSelf != show) m.attachment.gameObject.SetActive(show);
    }

    // 武器挂载点换成别的武器时用哪张图（角色自己给；null = 保持原图）
    protected virtual Sprite MountArt(MountPoint m, string item) => null;

    // 斜挂（npc2 胸挂 / npc1 背带）的枪：比拿在手上多斜多少度；角色画的那张斜挂步枪图里，枪的中心相对图的轴心（原图像素）
    protected const float SLUNG_GUN_TURN = -57.6f - 5.6f;
    protected static readonly Vector2 SLUNG_GUN_SHIFT = new Vector2(-32.5f, -64.5f);

    // 用枪的部件拼一把整枪（挂在身上用；以后的新枪不用另外画挂件图）：不缩小，和拿在手上一样大，不要持枪手。
    //   原点 = 枪的中心（WeaponDef.mountCenter；没填就用握把）。like = 照着它的排序层 / 前后顺序 / 材质（整把枪当一层）
    protected Transform BuildGunPicture(WeaponDef d, Transform parent, SpriteRenderer like, string name)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        SortingGroup sg = root.AddComponent<SortingGroup>();                  // 整把枪当一层（部件之间按枪自己的前后顺序）
        if (like != null) { sg.sortingLayerID = like.sortingLayerID; sg.sortingOrder = like.sortingOrder; }
        Vector2 center = d.mountCenter != Vector2.zero ? d.mountCenter : d.grip;
        foreach (WeaponPart p in d.parts)
        {
            if (p == null || p.hand || string.IsNullOrEmpty(p.path)) continue;   // 持枪手 / 没有图的锚点不要
            Sprite s = LoadFrom(d.artFolder, p.path);
            if (s == null) continue;
            SpriteRenderer sr = new GameObject(p.name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root.transform, false);
            sr.transform.localPosition = CanvasToLocal(p.pivot - center);      // 部件相对枪中心的位置（和拿在手上一样）
            sr.sprite = s;
            sr.sortingOrder = p.order;
            if (like != null) sr.sharedMaterial = like.sharedMaterial;         // 和身上其他部件一样受光
        }
        return root.transform;
    }

    // 换弹时枪往后收到哪（挂载位置的中心，根节点本地坐标）；角色没有挂载位置就返回 false
    protected virtual bool GunStowSpot(out Vector2 spot) { spot = Vector2.zero; return false; }

    // 从别的素材文件夹读图（比如 npc1 的步枪挂载点用 npc2 的步枪图）
    protected Sprite LoadFrom(string folder, string path)
    {
        Sprite s = Resources.Load<Sprite>(folder + "/" + path);
        if (s == null) Debug.LogWarning("[" + GetType().Name + "] 找不到素材：" + folder + "/" + path);
        return s;
    }

    // 和 Part 一样，但图从别的素材文件夹读
    protected SpriteRenderer PartFrom(string name, Transform parent, string folder, string path, Vector2 pivot, Vector2 parentPivot, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = CanvasToLocal(pivot - parentPivot);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = LoadFrom(folder, path);
        sr.sortingOrder = order;
        return sr;
    }


    // ============================================================================
    // 枪械（通用）：拿在手上的枪按 WeaponDef 拼出来（RigWeapons.cs）；瞄准 / 开枪后坐都按枪的属性算。
    //   角色给：肩膀（ShoulderFor）、持枪手的图（HandArt）、灯头发光点的画法（MakeLampGlow）、持枪手类型（Grip）。
    // ============================================================================
    protected GunRig _gun;                     // 现在拿着的枪（null = 没拿枪）
    protected Vector2 _gripBase;               // 端枪时握把原位（根节点本地坐标）
    protected float _k0, _km0;                 // 新一枪开始时，上一枪还没回正的后坐量（连发时接着往上叠，不会跳回 0）
    protected float _k0Prev, _km0Prev;         // 上一枪开始时的这个量
    protected UnityEngine.Rendering.Universal.Light2D _gunBeam;   // 枪上手电筒的光（URP 2D 聚光）

    // 按武器数据拼一把枪，挂在 hand（手点）下面；部件一开始都藏着
    protected GunRig BuildGun(WeaponDef d, Transform hand)
    {
        if (d == null) return null;
        GunRig g = new GunRig { def = d };
        g.root = new GameObject("Held_" + d.id).transform;
        g.root.SetParent(hand, false);
        foreach (WeaponPart p in d.parts)
        {
            GameObject go = new GameObject(p.name);
            go.transform.SetParent(g.root, false);
            go.transform.localPosition = CanvasToLocal(p.pivot - d.grip);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            Sprite own = p.hand ? GripHandArt(p.name == d.reloadHand, d.hold) : null;   // 持枪手：用角色衣服上的手（换弹手 = 辅助手，另一只 = 持枪手）
            sr.sprite = own != null ? own : (string.IsNullOrEmpty(p.path) ? null : LoadFrom(d.artFolder, p.path));   // 角色没有就用枪自带的
            sr.sortingOrder = p.order;
            sr.enabled = false;
            g.Add(p, sr);
        }
        PlaceGripHands(g);                                                      // 手放到枪的锚点上（+ 衣服自己的微调）
        g.muzzle = new GameObject("Muzzle").transform;
        g.muzzle.SetParent(g.root, false);
        g.muzzle.localPosition = CanvasToLocal(d.muzzle - d.grip);
        if (d.hasLamp)
        {
            g.lamp = new GameObject("GunLight").transform;
            g.lamp.SetParent(g.root, false);
            g.lamp.localPosition = CanvasToLocal(d.lamp - d.grip);
            g.lampGlow = MakeLampGlow(g.lamp);
        }
        if (d.hasLaser)                                                         // 镭射瞄准线的发射点
        {
            g.laser = new GameObject("GunLaser").transform;
            g.laser.SetParent(g.root, false);
            g.laser.localPosition = CanvasToLocal(d.laser - d.grip);
            g.laserGlow = MakeLampGlow(g.laser);                                // 发射点那一小团光（和手电灯头一样的发光点）
        }
        return g;
    }

    // 持枪手（衣服的属性）：枪只标锚点（手部件的轴心 = 手握在哪），手的图由角色衣服给，所以做新枪不用画手。
    //   support = 辅助手（就是换弹时离开枪的那只，WeaponDef.reloadHand），否则 = 持枪手（握把）
    protected virtual Sprite GripHandArt(bool support, HoldType hold) => null;               // null = 用枪自带的手
    protected virtual Vector2 GripHandOffset(bool support, HoldType hold) => Vector2.zero;    // 手离锚点再挪多少（单位，枪的本地坐标）
    protected virtual float GripHandAngle(bool support, HoldType hold) => 0f;                // 手再转多少度

    // 把枪上的两只手摆到锚点（+ 微调）
    protected void PlaceGripHands(GunRig g)
    {
        if (g == null) return;
        WeaponDef d = g.def;
        foreach (WeaponPart p in d.parts)
        {
            if (p == null || !p.hand) continue;
            SpriteRenderer sr = g.Part(p.name);
            if (sr == null) continue;
            bool support = p.name == d.reloadHand;
            sr.transform.localPosition = CanvasToLocal(p.pivot - d.grip) + (Vector3)GripHandOffset(support, d.hold);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, GripHandAngle(support, d.hold));
        }
    }
    protected virtual SpriteRenderer MakeLampGlow(Transform at) => null;          // 灯头发光点
    protected virtual Vector2 ShoulderFor(WeaponDef d) => d.shoulder;              // 肩膀（枪绕它转）
    protected virtual GripStyle Grip => GripStyle.Steady;                          // 持枪手类型（稳 / 颤抖……）
    protected Vector2 GunShoulder => _gun != null ? ShoulderFor(_gun.def) : aimShoulder;

    // 某把枪某个部件开枪时往后弹多远（原图像素）
    protected static float KickOf(GunRig g, string part)
    {
        if (g == null || g.def == null) return 0f;
        foreach (WeaponPart p in g.def.parts) if (p.name == part) return p.kick;
        return 0f;
    }

    // 枪要转多少度，枪管这条线才正好穿过目标 t（本地坐标）。手绕肩膀转，枪跟手一起转
    protected float GunAimAngle(Vector2 t)
    {
        Vector2 sh = GunShoulder;
        Vector2 arm = _gripBase - sh;                                           // 肩膀 → 握把（枪没转时）
        Vector2 muzzle = _gun != null ? (Vector2)CanvasToLocal(_gun.def.muzzle - _gun.def.grip) : Vector2.zero;   // 握把 → 枪口
        float h = arm.y + muzzle.y;                                             // 枪管这条线比肩膀高多少
        Vector2 q = t - sh;                                                     // 肩膀 → 目标
        float len = Mathf.Max(q.magnitude, 0.001f);
        float phi = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;                      // 从肩膀看目标的角度
        float fix = Mathf.Asin(Mathf.Clamp(h / len, -1f, 1f)) * Mathf.Rad2Deg;  // 枪管不在肩膀高度：补一点角度
        return Mathf.DeltaAngle(0f, phi - fix);
    }

    // 枪转了 ang 度、手被顶开 push 时，握把在哪（根节点本地坐标）；lowMul = 放低持枪的下移用几成（换弹抬枪时逐渐取消）
    protected Vector2 GunAimHand(float ang, Vector2 push, float lowMul = 1f)
    {
        Vector2 sh = GunShoulder;
        return sh + Rot(_gripBase - sh + push, ang) + lowReadyOffset * (_lowK * lowMul);
    }

    // 马上把枪摆到瞄准的位置（开枪那一下用，枪口位置才准）
    protected void GunAimPose()
    {
        Vector2 hp = GunAimHand(_aimAng, Vector2.zero);
        _handPoint.localPosition = new Vector3(hp.x, hp.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, _aimAng);
    }

    // 每帧：算瞄准角度、判断要不要翻身，枪口慢慢转过去（dt = 0 时只算不转）
    protected void GunUpdateAim(float dt)
    {
        bool gun = _gun != null;
        if (testAimTarget != null && gun)                                       // 测试：一直锁定拖进来的物体
        {
            if (_aimUntil <= _time) _aimLeft = FacingLeft;
            _lockTarget = testAimTarget;
            _aimUntil = _time + 0.3f;
        }
        if (_lockTarget != null && !_lockTarget.gameObject.activeInHierarchy) { _lockTarget = null; _aimUntil = 0f; }   // 目标没了：解除
        bool aiming = _aimUntil > _time && gun;
        bool raised = aiming || _time < _readyUntil;                            // 举着枪（在瞄准 / 刚开过枪）
        float want = raised || !gun ? 0f : lowReadyAngle;                       // 没接敌、不瞄准：枪口朝下
        if (aiming)
        {
            if (_lockTarget != null) _aimPoint = _lockTarget.position;          // 锁定：跟着目标走
            Vector2 t = AimLocal(_aimPoint);                                    // 目标在角色本地的位置（x 正 = 前方）
            want = GunAimAngle(t);
            float lim = 90f + Mathf.Max(0f, aimFlipOver);
            if (!testFaceLeft && Mathf.Abs(want) > lim)                         // 枪口转过了正上 / 正下方
            {
                float other = GunAimAngle(new Vector2(-t.x, t.y));              // 翻过身以后要转多少度
                if (Mathf.Abs(other) < Mathf.Abs(want))                         // 翻过去更顺：翻身
                {
                    _aimLeft = !_aimLeft;
                    _aimAng = Mathf.DeltaAngle(0f, 180f - _aimAng);             // 现在的枪口角度也镜像过去（画面上枪口不跳）
                    want = other;
                }
            }
        }
        _aimWant = want;
        float speed = raised ? aimTurnSpeed : lowReadySpeed;                    // 举枪快，放下慢
        float step = speed > 0f ? speed * dt : 9999f;                           // 这一帧最多转多少度
        _aimAng = Mathf.MoveTowards(_aimAng, want, step);
        float lowTarget = !raised && gun ? 1f : 0f;                             // 要不要放低
        float span = Mathf.Max(1f, Mathf.Abs(lowReadyAngle));                   // 和枪口转动同样快慢地渐变
        float lowRate = (lowTarget > _lowK ? lowReadySpeed : aimTurnSpeed) / span;
        if (dt > 0f) _lowK = Mathf.MoveTowards(_lowK, lowTarget, lowRate * dt);
    }

    // 开一枪的后坐（每一枪的幅度在这把枪的范围里随机）；连发时从上一枪还没回正的量接着往上冲
    protected void GunFire()
    {
        if (_gun == null) return;
        WeaponDef d = _gun.def;
        _readyUntil = _time + Mathf.Max(0f, lowReadyHold);                      // 开枪：先把枪举平，过一会儿没事再放下
        _lowK = 0f;                                                             // 手也马上举回来
        _k0 = 0f; _km0 = 0f;
        if (_recoilT >= 0f)                                                     // 上一枪还没回正（连发）
        {
            _k0 = GunKick(_recoilT, d.handRecoilRise, _shotHandReturn, _k0Prev);
            _km0 = GunKick(_recoilT, _shotMuzzleRise, d.muzzleReturn, _km0Prev);
        }
        _k0Prev = _k0; _km0Prev = _km0;
        _recoilT = 0f;
        _recoilHandOff = new Vector2(RandIn(d.recoilHandX), RandIn(d.recoilHandY));   // 这一枪手被顶回多远
        _shotKick = RandIn(d.recoilKick);                                       // 这一枪枪口上扬多少度
        _shotHandReturn = RandIn(d.handRecoilReturn);                           // 这一枪手回正的快慢
        _shotMuzzleRise = RandIn(d.muzzleRise);                                 // 这一枪枪口上扬用几秒
        UpdateAim(0f);                                                          // 重新算一下瞄准（只算不转）
        _aimAng = _aimWant;                                                     // 开枪这一下枪口直接对准目标
        _rig.localScale = new Vector3(FacingLeft ? -1f : 1f, 1f, 1f);           // 可能刚翻身：对好朝向
        GunAimPose();                                                           // 马上摆好，子弹从对准后的枪口出来
        GunAnimator.For(d).OnFire(this, _gun);                                  // 这把枪自己开枪时还要做的
        if (d.boltAction) StartBolt();                                          // 拉栓枪：开完这一枪拉一次栓（CharacterRigBase.Bolt.cs）
    }

    // 每帧：持枪 + 后坐（手被顶回、枪口上扬、身体后仰、滑套 / 拉栓往后弹）+ 持枪手类型（稳 / 颤抖）
    protected void GunRecoilPose(float dt)
    {
        if (_gun == null) return;
        WeaponDef d = _gun.def;
        float angle = 0f, s = 0f;
        Vector2 off = Vector2.zero;
        if (_recoilT >= 0f)
        {
            _recoilT += dt;
            float k = GunKick(_recoilT, d.handRecoilRise, _shotHandReturn, _k0);      // 手：一下顶回去，再慢慢回正
            float km = GunKick(_recoilT, _shotMuzzleRise, d.muzzleReturn, _km0);      // 枪口：单独的上扬 / 回正节奏
            angle = _shotKick * km;
            off = _recoilHandOff * k;
            _actionLean = d.recoilLean * k;                                     // 身体被震得往后仰
            _actionShift = -d.actionShift * k;                                  // 往后挪一点
            s = GunSlideCurve(_recoilT, d.kickBack, d.kickHold, d.kickReturn);  // 滑套 / 拉栓往后弹 → 停一下 → 弹回来
            float slideEnd = Mathf.Max(0.001f, d.kickBack) + Mathf.Max(0f, d.kickHold) + Mathf.Max(0.001f, d.kickReturn);
            float endT = Mathf.Max(slideEnd, 0.3f + 6f * Mathf.Max(d.handRecoilRise + _shotHandReturn, _shotMuzzleRise + d.muzzleReturn));
            if (_recoilT > endT) _recoilT = -1f;                                // 播完了
        }
        Vector2 hp = GunAimHand(_aimAng, off);                                  // 手绕肩膀转到瞄准角度，后坐把手顶回来
        hp += Grip.Offset(_time, out float tremA);                              // 持枪手类型：稳 = 不动；颤抖 = 小幅乱抖
        _handPoint.localPosition = new Vector3(hp.x, hp.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, _aimAng + angle + tremA);
        _gun.Kick(s, PPU_FULL);
        GunAnimator.For(d).Pose(this, _gun, dt);                                // 这把枪自己每帧还要动的
    }

    // 后坐曲线：t 秒时在哪；rise 秒内从 start 冲到 1，然后按 ret 的快慢回到 0
    protected static float GunKick(float t, float rise, float ret, float start)
    {
        rise = Mathf.Max(0.001f, rise); ret = Mathf.Max(0.001f, ret);
        return t < rise ? Mathf.Lerp(start, 1f, t / rise) : Mathf.Exp(-(t - rise) / ret);
    }

    // 滑套曲线：往后滑 → 停一下 → 滑回来（0 = 原位，1 = 最后面）
    protected static float GunSlideCurve(float t, float back, float hold, float ret)
    {
        back = Mathf.Max(0.001f, back); hold = Mathf.Max(0f, hold); ret = Mathf.Max(0.001f, ret);
        if (t < back) return t / back;
        if (t < back + hold) return 1f;
        return Mathf.Max(0f, 1f - (t - back - hold) / ret);
    }

    // 枪火（图片式，枪的属性）：这把枪的枪火图随机一张，放在开枪那一刻的枪口上，朝着那一刻的枪口方向
    public void GunFlash()
    {
        if (_gun == null || _muzzle == null) return;
        WeaponDef d = _gun.def;
        Vector3 at = _muzzle.position;
        Vector3 dir = _muzzle.TransformVector(Vector3.right); dir.z = 0f;
        bool left = _muzzle.lossyScale.x < 0f;                                  // 角色朝左（整个角色左右翻过来）
        float size = Mathf.Abs(_muzzle.lossyScale.y);
        float ang = dir.sqrMagnitude > 0.000001f ? Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg : (left ? 180f : 0f);
        string[] fl = d.flashSprites;
        Sprite s = fl != null && fl.Length > 0 ? LoadFrom(d.artFolder, fl[Random.Range(0, fl.Length)]) : null;
        if (s != null)
        {
            SpriteRenderer sr = new GameObject("MuzzleFlash").AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingOrder = 55;                                               // 画在人上面、子弹下面
            if (_oldSprite != null) sr.sortingLayerID = _oldSprite.sortingLayerID;
            RenderMats.MakeUnlit(sr);
            sr.transform.position = at;
            sr.transform.rotation = Quaternion.Euler(0f, 0f, ang);
            float k = Mathf.Max(0.05f, d.flashSize) * size, fy = left ? -1f : 1f;   // 朝左：上下翻（配合转向 = 左右镜像）
            StartCoroutine(GunFxFade(sr, d.flashTime, new Vector3(k * 0.9f, k * 0.9f * fy, 1f), new Vector3(k * 1.05f, k * 1.05f * fy, 1f), 0.7f));
        }
        if (RenderMats.IsURP && muzzleLightIntensity > 0f) StartCoroutine(MuzzleLight(at));   // 火光照亮周围
    }

    IEnumerator GunFxFade(SpriteRenderer sr, float life, Vector3 s0, Vector3 s1, float pow)
    {
        life = Mathf.Max(0.02f, life);
        Destroy(sr.gameObject, life + 0.5f);
        float t = 0f;
        while (sr != null && t < life)
        {
            float k = t / life, e = 1f - (1f - k) * (1f - k);
            sr.transform.localScale = Vector3.Lerp(s0, s1, e);
            Color c = sr.color; c.a = Mathf.Pow(1f - k, pow); sr.color = c;
            t += Time.deltaTime;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    // 枪上的手电筒（枪的属性）：拿着有灯的枪才亮，光从灯头朝枪口方向照
    protected void GunLampUpdate()
    {
        WeaponDef d = _gun != null ? _gun.def : null;
        bool on = d != null && d.hasLamp && d.lightOn && d.lightIntensity > 0f && _gun.lamp != null;
        if (_gun != null && _gun.lampGlow != null)
        {
            _gun.lampGlow.enabled = on;
            Color c = d.lightColor; c.a = 1f;
            _gun.lampGlow.color = c;
            _gun.lampGlow.transform.localScale = Vector3.one * Mathf.Max(0.01f, d.lightGlowSize);
        }
        if (on && _gunBeam == null && RenderMats.IsURP)
        {
            _gunBeam = WorldLighting.CreatePoint("GunFlashlight", transform, d.lightColor, d.lightIntensity, d.lightRange, d.lightAngle);
            WorldLighting.EnableShadows(_gunBeam);
        }
        if (_gunBeam == null) return;
        _gunBeam.enabled = on;
        if (!on) return;
        WorldLighting.SetPoint(_gunBeam, d.lightColor, d.lightIntensity, d.lightRange, d.lightAngle);
        Vector3 dir = _muzzle != null ? _muzzle.TransformVector(Vector3.right) : Vector3.right;
        _gunBeam.transform.position = _gun.lamp.position;
        _gunBeam.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
    }

    // 枪上的镭射瞄准线（枪的属性）：拿着有镭射的枪就开着（取出动画播完才开、死了关掉），
    //   从镭射十字沿枪管方向射出一条细激光，越远越淡；发射点有一小团光。都不受光照（黑暗里也看得见）
    LineRenderer _laserLine;                                   // 激光
    UnityEngine.Rendering.Universal.Light2D[] _laserLights;    // 沿着激光的几团小光（照亮周围一点点）
    const int LASER_LIGHTS = 5;
    SpriteRenderer _laserGlowLast;                             // 上一帧亮着的发射点（换枪时关掉）
    static Material _laserMat;
    protected void GunLaserUpdate()
    {
        WeaponDef d = _gun != null ? _gun.def : null;
        bool on = d != null && d.hasLaser && _gun.laser != null && _drawT < 0f && !_dead && _gun.root.gameObject.activeInHierarchy;
        if (on && _laserLine == null)                                           // 第一次用：做一条激光
        {
            GameObject go = new GameObject("LaserLine");
            go.transform.SetParent(transform, false);
            _laserLine = go.AddComponent<LineRenderer>();
            _laserLine.useWorldSpace = true;
            _laserLine.positionCount = 2;
            _laserLine.numCapVertices = 2;
            if (_laserMat == null)
            {
                Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");  // 叠加发光
                if (sh == null) sh = Shader.Find("Sprites/Default");
                if (sh != null) _laserMat = new Material(sh);
            }
            if (_laserMat != null) _laserLine.sharedMaterial = _laserMat;
            _laserLine.sortingOrder = 55;                                        // 在枪上面
        }
        SpriteRenderer glow = _gun != null ? _gun.laserGlow : null;
        if (_laserGlowLast != null && _laserGlowLast != glow) _laserGlowLast.enabled = false;   // 换了枪：上一把的发光点关掉
        _laserGlowLast = glow;
        if (_gun != null && _gun.laserGlow != null)                             // 发射点那一小团光
        {
            _gun.laserGlow.enabled = on && d.laserDotSize > 0f;
            Color gc = d.laserColor; gc.a = 1f;
            _gun.laserGlow.color = gc;
            _gun.laserGlow.transform.localScale = Vector3.one * Mathf.Max(0.01f, d.laserDotSize);
        }
        if (_laserLine == null) return;
        _laserLine.enabled = on;
        bool lit = on && d.laserLight > 0f;
        if (lit && _laserLights == null)                                        // 第一次用：沿线放几团小点光
        {
            _laserLights = new UnityEngine.Rendering.Universal.Light2D[LASER_LIGHTS];
            for (int i = 0; i < LASER_LIGHTS; i++) _laserLights[i] = WorldLighting.CreatePoint("LaserLight", transform, d.laserColor, 0f, d.laserLightRadius, 360f);
        }
        if (_laserLights != null)
            foreach (UnityEngine.Rendering.Universal.Light2D l in _laserLights) if (l != null && l.enabled != lit) l.enabled = lit;
        if (!on) return;
        Vector3 dir = _muzzle != null ? _muzzle.TransformVector(Vector3.right) : Vector3.right;   // 沿枪管方向（含朝左翻转、后坐上扬）
        dir.z = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();
        Vector3 a = _gun.laser.position;
        float scale = Mathf.Abs(transform.lossyScale.y);                        // 角色本身的缩放
        _laserLine.SetPosition(0, a);
        _laserLine.SetPosition(1, a + dir * d.laserLength * scale);
        _laserLine.widthMultiplier = d.laserWidth * scale;
        Color c0 = d.laserColor, c1 = d.laserColor; c1.a = 0f;                  // 越远越淡
        _laserLine.startColor = c0; _laserLine.endColor = c1;
        if (!lit || _laserLights == null) return;
        Color lc = d.laserColor; lc.a = 1f;
        for (int i = 0; i < LASER_LIGHTS; i++)                                  // 越远越暗（和激光一样淡出）
        {
            UnityEngine.Rendering.Universal.Light2D l = _laserLights[i];
            if (l == null) continue;
            float u = (i + 0.5f) / LASER_LIGHTS;
            l.transform.position = a + dir * d.laserLength * scale * u * 0.8f;
            WorldLighting.SetPoint(l, lc, d.laserLight * (1f - u), d.laserLightRadius * scale, 360f);
        }
    }

    // ================= 统一对外接口：战斗 / 弹道 / 死亡 / ABA 这些系统只认这些，不用管是哪个角色 =================
    // 每个角色自己实现（override）；以后的 npc3 也照着实现这几样就能直接接进所有系统。
    public abstract void PlayAttack();
    public abstract bool TryGetMuzzle(out Vector3 pos);
    public abstract bool FireBullet(Vector3 target);
    public abstract bool FireBullet(Vector3 target, bool canRicochet);
    // MuzzleFlash：见 CharacterRigBase.GunAnim.cs（枪火由这把枪的动画类画）
    // 换弹 ReloadTotal / ReloadSeconds / PlayReload / ResolveReloadKind：见 CharacterRigBase.Reload.cs（枪决定怎么插，配件决定从哪拿）

    // ---------------- 测试窗口（AnimTestHub 的"武器测试"）用 ----------------
    public struct TestOption { public string key, label; public TestOption(string k, string l) { key = k; label = l; } }
    public virtual List<TestOption> TestHoldExtras() => new List<TestOption>();   // 除了枪还能拿什么（空手 / 斧头 / 匕首……）
    public virtual bool CanTestEquip(WeaponDef d) => true;                         // 这个角色能不能拿这把枪
    public virtual void TestEquip(string key) { }                                  // 拿起：枪 = WeaponDef id，其他 = TestHoldExtras 的 key
    public virtual string TestGearInfo() => "";                                    // 配件状态（比如胸挂弹匣袋）
    public virtual void TestRefillGear() { }                                       // 配件补满（比如胸挂弹匣）
    public string HeldKey => _held;                                                // 现在手上拿着什么
    public GunRig HeldGun => _gun;                                                 // 现在拿着的枪（null = 不是枪）

    // 挂饰开关：挂上这个挂载点该挂的东西 / 取下；item 不为空 = 挂这件（武器挂载点挂别的枪）
    public void TestSetMount(MountPoint m, bool on, string item = null)
    {
        if (m == null) return;
        string it = item ?? ItemOf(m.kind);
        if (it == null) it = m.kind == MountKind.Weapon ? "pistol" : m.id;
        SetMountItem(m, on ? it : null);
    }

    // 这个挂载点能不能挂这把枪（武器挂载点 = 有枪套图或能用部件拼的手枪，步枪挂载点 = 用部件拼或有斜挂图的步枪）
    public virtual bool CanMount(MountPoint m, WeaponDef d)
    {
        if (m == null || d == null) return false;
        if (m.kind == MountKind.Rifle) return d.hold == HoldType.Rifle && (!d.characterMountArt || !string.IsNullOrEmpty(d.slingArt));   // 步枪：用部件拼，或者有斜挂图
        if (m.kind == MountKind.Weapon) return !string.IsNullOrEmpty(d.holsterArt) || (d.hold == HoldType.Pistol && d.mountCenter != Vector2.zero);   // 手枪：有枪套图，或者能用部件拼
        return false;
    }
    // 这个挂载点在测试窗口里能选挂哪几样（null = 只能开 / 关；工具挂环 = 锤子 / 斧头 / 手雷）
    public virtual string[] MountChoices(MountPoint m) => null;
    // 挂载点上挂的东西换成测试窗口里显示的物品 id（武器：WeaponDef id）
    public virtual string MountItemFor(WeaponDef d) => d != null ? d.id : null;

    // 只有部分角色有的功能：父类给一个"什么都不做"的默认，有这个功能的角色自己 override
    public virtual bool HasGear(string key) => false;
    public virtual bool CanTestThrow => false;                                     // 这个角色能不能扔手雷（测试窗口显示按钮）
    public virtual void TestThrow() { }                                            // 测试：扔一颗手雷
    public virtual void Hold(string key) { }
}
