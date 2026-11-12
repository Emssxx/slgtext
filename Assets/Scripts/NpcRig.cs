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
//
// 【瞄准】拿枪战斗时锁定怪物（NpcCombat 每帧调 LockOn）：持枪手绕"肩膀"转，枪管这条线一直穿过目标；
//   枪口转过正上 / 正下方（±90°）时角色翻身。换弹时按换弹姿势走，换完再转回去对准。
//
// 【换弹】动作是通用的（CharacterRigBase.Reload.cs）：怎么插、怎么对准由枪决定（自己的手枪 = WeaponDef "suit_pistol"），
//   从哪拿新弹匣由配件决定——npc1 没有胸挂，所以从屁股后面拿，空弹匣也塞回屁股后面（Rl Pouch / Rl Pouch Angle / Rl Behind X）。
//   捡到别人的枪（比如 npc2 的步枪）也能换弹：步枪式动作 + 从屁股后面拿。
//   PlayReload(秒数, 种类)：① 战术换弹——旧弹匣掉进右手，和新弹匣成 V 字，插好新弹匣后带回身后；
//   ② 快速换弹——枪口一往上抬，旧弹匣就顺着握把滑出来掉到地上（不等右手；落地后留在原地，过一会儿淡出），右手直接插新弹匣，其余和战术换弹一样。
//   ③ 普通换弹——右手先到握把下方接住弹出来的旧弹匣，带回身后放好，再拿新弹匣回来插上，插好直接回握把。
//   ④ 颤抖换弹（还是旧的一套，只有自己的手枪；第 ④ 批改成"持枪手类型"）——在普通换弹上加：枪和手一直抖；有概率新弹匣拿回来的路上掉到地上（回身后再拿一个）；
//     有概率插歪了卡住（退出来重新对准再插）。开始换弹时就把这次掉几次、卡几次随机好。
// 【武器操作等级 / 紧张】weaponLevel 越高换弹越快（ReloadSeconds）、等级低平时持枪手也抖；nervous 越大持枪时手抖得越厉害。游戏规则之后接。
// ============================================================================
public class NpcRig : CharacterRigBase
{

    [Header("惯性开关（整体倍率，0 = 关掉）")]
    [Range(0f, 2f)] public float antennaAmount = 1f;          // 天线

    [Header("挥动 / 后坐")]
    public float chopPeriod = 1.2f;                           // 砍一下（含停顿）要几秒
    public Vector2 chopAngles = new Vector2(90f, -37.9f);       // 斧头：抬起的角度 / 挥下的角度（度）
    public float hammerPeriod = 0.7f;                         // 敲一下（含停顿）要几秒
    public Vector2 hammerAngles = new Vector2(30f, -18f);     // 锤子：抬起的角度 / 敲下的角度（度）
    public float sparkTime = 0.05f;                           // 枪火：② 闪光（火花线）亮多久（秒）
    public float glowTime = 0.06f;                            // 枪火：③ 亮光亮多久（秒）
    public bool flashSmoke = true;                            // 开枪后冒烟（代码画的烟；你在测试场景打开了）
    public float smokeTime = 0.6f;                            // 烟飘多久（秒）
    // 手的移动（单位；x 正 = 往前，y 正 = 往上）：挥的时候手点沿着这些位置走
    public Vector2 chopHandUp = new Vector2(-0.14f, 0.35f);   // 斧头抬起时手往哪挪（往后、往上）
    public Vector2 chopHandDown = new Vector2(0.32f, -0.02f); // 斧头砍下时手往哪挪（往前、往下）（y 实际用下面的随机范围）
    public Vector2 hammerHandUp = new Vector2(-0.05f, 0.035f);   // 锤子抬起
    public Vector2 hammerHandDown = new Vector2(0.27f, 0.03f); // 锤子敲下（y 实际用下面的随机范围）
    public Vector2 chopHandDownY = new Vector2(-0.09f, 0.15f);   // 斧头砍下时手的 y：每砍一下在这个范围里随机（最小 / 最大）
    public Vector2 hammerHandDownY = new Vector2(-0.1f, 0.23f);  // 锤子敲下时手的 y：每敲一下在这个范围里随机（最小 / 最大）
    // 身体跟着晃（度；正 = 往后仰，负 = 往前倾）
    public Vector2 chopLean = new Vector2(5f, -7f);           // 斧头：抬起时后仰 / 砍下时前倾
    public Vector2 hammerLean = new Vector2(3f, -4f);         // 锤子：抬起 / 敲下

    public Vector2 rlPouch = new Vector2(-0.21f, 0.29f);      // 身后拿弹匣的位置（相对脚底中心；会被身体挡住）
    public float rlPouchAngle = 40f;                          // 右手在身后时转多少度
    public float rlBehindX = 0.245f;                          // 右手中心走到这个 x 左边就绕到身体后面（相对脚底中心；默认 = 右手刚碰到身体右边缘线）

    // 之后：③ 旧弹匣掉进右手（用 Rl Drop Time）→ ④ 带回身后（Rl Stow Time）→ ⑤ 在身后放下旧的、拿起新的（Rl Grab Time）
    //       → ⑥ 拿到握把下方（Rl Bring Time）→ ⑦ 对准（Rl Align Time）→ ⑧ 插入（Rl Insert Time）→ ⑨ 直接回握把（Rl Return Time）

    [Header("换弹 4：颤抖换弹（基于普通换弹；没列出来的时间 / 姿势用上面的）")]
    [Range(0f, 1f)] public float srFailChance = 0.35f;       // 每次插弹匣插不进去的概率
    public int srMaxFails = 2;                                // 最多连续插不进去几次
    public float srFailDepth = 0.5f;                          // 插不进去时插进去了多少就卡住（0~1）
    public float srFailOffset = 0.012f;                       // 插歪了往前偏多少（按枪的方向，单位）
    public float srFailTilt = 10f;                            // 插歪了歪多少度
    public float srFailBackTime = 0.2f;                       // 卡住后退出来的时间
    public float srFailShake = 0.6f;                          // 卡住时枪磕一下的幅度（乘在 Rl Shake 上）
    [Range(0f, 1f)] public float srDropChance = 0.25f;       // 新弹匣拿回来的路上掉到地上的概率（最多掉 2 次；掉的样子用"换弹 2"的下落参数）
    public Vector2 srSlipOffset = new Vector2(0.03f, -0.1f);  // 新弹匣滑掉时右手在哪（相对握把下方：x 往前，y 往上；默认在身体外面、手枪下面）
    public Vector2 srFling = new Vector2(0.5f, 0.9f);         // 滑掉时弹出去的速度（x 往前，y 往上；单位 / 秒，每次随机 0.7~1.3 倍）
    public Vector2 srFlingSpin = new Vector2(-1000f, -600f);  // 弹出去时转多快：在范围里随机（度 / 秒；负 = 往前翻，正 = 往后翻）
    public float srFumbleTime = 0.3f;                         // 掉了以后手愣住多久，再回身后拿一个

    [Header("武器操作等级 / 紧张（游戏规则之后接；现在可以手动调来测试）")]
    [Range(1, 5)] public int weaponLevel = 3;                 // 武器操作等级：1 最生疏 ~ 5 最熟练
    public float[] levelReloadTime = { 1.5f, 1.2f, 1f, 0.85f, 0.7f };   // 各等级换弹时间的倍数（第 1 个 = 1 级；1 = 按上面调的时间，越小越快）
    public float[] levelTremble = { 0.6f, 0.3f, 0f, 0f, 0f }; // 各等级平时持枪手抖多少（第 1 个 = 1 级；0 = 不抖，1 = 最厉害；和紧张取大的那个）
    [Range(0f, 1f)] public float nervous = 0f;                // 紧张程度：持枪时手抖多厉害（0 = 不抖，1 = 最厉害）
    public float trembleMove = 0.01f;                         // 抖动的位移幅度（单位；紧张 = 1 时）
    public float trembleAngle = 5f;                           // 抖动的角度幅度（度；紧张 = 1 时）
    public float trembleFreq = 9f;                            // 抖得多快
    public float trembleReloadMul = 1.5f;                     // 颤抖换弹时抖动是"紧张 = 1 持枪"的几倍

    [Header("灯光（URP 2D 光；亚空间很暗，靠防护服上的灯照亮）")]
    public bool headlamp = true;                              // 手电筒开不开（头盔上两个淡蓝色的圆灯，PSD 躯干图层）
    public Color headlampColor = new Color(0.78f, 0.92f, 1f); // 手电筒光的颜色（冷白偏蓝）
    public float headlampIntensity = 0.7f;                    // 每盏手电筒的亮度（两盏叠在一起）
    public float headlampRange = 4f;                          // 手电筒照多远（单位）
    public float headlampAngle = 50f;                         // 手电筒光束多宽（度）
    public float headlampDown = -12f;                         // 平时手电筒往下照多少度（拿枪时跟着枪口方向）
    public Color lampGlowColor = new Color(0.6f, 0.95f, 1f);  // 两个圆灯本身的发光颜色
    public float lampGlowSize = 1f;                           // 圆灯发光多大（倍数）
    public Color antennaColor = new Color(0.35f, 1f, 0.45f);  // 天线顶上呼吸灯的颜色（绿）
    public float antennaPeriod = 2.2f;                        // 呼吸灯一呼一吸多少秒
    public float antennaLight = 0.5f;                         // 呼吸灯照亮周围的亮度（最亮时；0 = 只发光不照亮）
    public Color suitGlowColor = new Color(0.7f, 0.85f, 1f);  // 防护服身上的一圈微光颜色
    public float suitGlowIntensity = 0.35f;                   // 身上微光亮度（0 = 关）
    public float suitGlowRadius = 1.1f;                       // 身上微光范围（单位）

    [Header("测试（运行时选中 NPC 用；不受腰上挂件限制）")]
    public TestHeld testHeld = TestHeld.None;                 // 强制拿什么（不测试 = 按游戏里的情况）
    public TestExpr testExpression = TestExpr.None;           // 强制表情（换表情时照样开合面罩）
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
    protected override bool FacingLeft => testFaceLeft || (_aimUntil > _time ? _aimLeft : (_oldSprite != null && _oldSprite.flipX));

    // 外部调用：接下来 0.3 秒瞄准一个固定的点（不管往哪走，身体和枪口都朝着它）
    public void AimAt(Vector3 target)
    {
        if (_aimUntil <= _time) _aimLeft = FacingLeft;  // 刚开始瞄：先保持现在的朝向（转过头顶才翻身）
        _lockTarget = null;
        _aimPoint = target;
        _aimUntil = _time + 0.3f;
    }

    // 每帧：算瞄准角度、判断要不要翻身，枪口慢慢转过去（dt = 0 时只算不转）
    protected override void UpdateAim(float dt)
    {
        if (_gun != null) { GunUpdateAim(dt); return; }                      // 通用枪械：按枪的属性瞄准
        if (testAimTarget != null)                                          // 测试：一直锁定拖进来的物体
        {
            if (_aimUntil <= _time) _aimLeft = FacingLeft;
            _lockTarget = testAimTarget;
            _aimUntil = _time + 0.3f;
        }
        if (_lockTarget != null && !_lockTarget.gameObject.activeInHierarchy) { _lockTarget = null; _aimUntil = 0f; }   // 目标没了：解除
        bool aiming = _aimUntil > _time;
        if (aiming && _lockTarget != null) _aimPoint = _lockTarget.position;   // 锁定：跟着目标走
        bool raised = aiming || _time < _readyUntil;                        // 举着枪（在瞄准 / 刚开过枪）
        float want = raised ? 0f : (_held == "pistol" ? lowReadyAngle : 0f);  // 没遇敌、不瞄准：手枪朝下（放低持枪）
        if (aiming)
        {
            Vector2 t = AimLocal(_aimPoint);                                // 目标在角色本地的位置（按现在的朝向，x 正 = 前方）
            bool canFlip = !testFaceLeft;                                   // 测试强制朝左时不翻身
            if (_held == "pistol")
            {
                want = AimAngle(t);
                float lim = 90f + Mathf.Max(0f, aimFlipOver);
                if (canFlip && Mathf.Abs(want) > lim)                       // 枪口转过了正上 / 正下方
                {
                    float other = AimAngle(new Vector2(-t.x, t.y));         // 翻过身以后要转多少度
                    if (Mathf.Abs(other) < Mathf.Abs(want))                 // 翻过去更顺：翻身
                    {
                        _aimLeft = !_aimLeft;
                        _aimAng = Mathf.DeltaAngle(0f, 180f - _aimAng);     // 现在的枪口角度也镜像过去（画面上枪口不跳）
                        want = other;
                    }
                }
            }
            else if (canFlip && t.x < -0.01f) _aimLeft = !_aimLeft;         // 没拿枪：目标在身后就转身
        }
        _aimWant = want;
        float speed = raised ? aimTurnSpeed : lowReadySpeed;                 // 举枪快，放下慢
        float step = speed > 0f ? speed * dt : 9999f;                       // 这一帧最多转多少度
        _aimAng = Mathf.MoveTowards(_aimAng, want, step);                   // 从正前方那边转过去（不会从身后绕）
        float lowTarget = !raised && _held == "pistol" ? 1f : 0f;          // 要不要放低
        float span = Mathf.Max(1f, Mathf.Abs(lowReadyAngle));               // 和枪口转动同样快慢地渐变
        float lowRate = (lowTarget > _lowK ? lowReadySpeed : aimTurnSpeed) / span;
        _lowK = dt > 0f ? Mathf.MoveTowards(_lowK, lowTarget, lowRate * dt) : _lowK;
    }

    // 选中 NPC 时在 Scene 窗口画标记（方便自己调）：黄点 = 持枪手绕着转的"肩膀"（Aim Shoulder），
    // 绿点 = 平时举枪时的握把位置，青点 = 放低持枪时的握把位置（Low Ready Angle + Low Ready Offset）
    void OnDrawGizmosSelected()
    {
        bool left = _rig != null && _rig.lossyScale.x < 0f;                   // 运行时朝左就左右翻
        Vector3 root = transform.position + new Vector3(0f, FEET_Y, 0f);       // 脚底中心
        System.Func<Vector2, Vector3> W = p => root + new Vector3(left ? -p.x : p.x, p.y, 0f);   // 本地 → 世界
        Vector2 grip = CanvasToLocal(HELD_PIVOT["pistol"] - P_ROOT);           // 握把（手枪平时的位置）
        Vector2 low = aimShoulder + Rot(grip - aimShoulder, lowReadyAngle) + lowReadyOffset;   // 放低时的握把
        float r = 0.012f;
        Gizmos.color = Color.yellow; Gizmos.DrawSphere(W(aimShoulder), r);
        Gizmos.color = Color.green; Gizmos.DrawSphere(W(grip), r); Gizmos.DrawLine(W(aimShoulder), W(grip));
        Gizmos.color = Color.cyan; Gizmos.DrawSphere(W(low), r); Gizmos.DrawLine(W(aimShoulder), W(low));
    }

    // 枪要转多少度，枪管这条线才正好穿过目标 t（t = 本地坐标）。手绕肩膀转，枪跟手一起转
    float AimAngle(Vector2 t)
    {
        Vector2 arm = (Vector2)_heldBase - aimShoulder;                    // 肩膀 → 握把（枪没转时）
        Vector2 muzzle = CanvasToLocal(P_MUZZLE - HELD_PIVOT["pistol"]);    // 握把 → 枪口（枪的本地坐标）
        float h = arm.y + muzzle.y;                                         // 枪管这条线比肩膀高多少（枪没转时）
        Vector2 q = t - aimShoulder;                                        // 肩膀 → 目标
        float len = Mathf.Max(q.magnitude, 0.001f);
        float phi = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;                  // 从肩膀看目标的角度
        float fix = Mathf.Asin(Mathf.Clamp(h / len, -1f, 1f)) * Mathf.Rad2Deg;   // 枪管不在肩膀高度：补一点角度，让枪管线穿过目标
        return Mathf.DeltaAngle(0f, phi - fix);
    }

    // 枪转了 ang 度、手被顶开 push（枪的方向上）时，握把在哪（根节点本地坐标）
    Vector2 AimHand(float ang, Vector2 push) { return AimHand(ang, push, 1f); }

    // 同上；lowMul = 放低持枪的下移用几成（换弹抬枪时逐渐取消）
    Vector2 AimHand(float ang, Vector2 push, float lowMul)
    {
        return aimShoulder + Rot((Vector2)_heldBase - aimShoulder + push, ang) + lowReadyOffset * (_lowK * lowMul);
    }

    // 马上把枪摆到瞄准的位置（开枪那一下用，枪口位置才准）
    void ApplyAimPose()
    {
        Vector2 hp = AimHand(_aimAng, Vector2.zero);
        _handPoint.localPosition = new Vector3(hp.x, hp.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, _aimAng);
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
        [InspectorName("战斗步枪")] Rifle,
    }

    [Header("通用枪械（拿战斗步枪等）：角色这边的位置 + 持枪手微调（衣服的手：左手握把、右手辅助，握在枪的锚点上）")]
    public Vector2 rifleGripHandOffset;                       // 拿步枪：左手（握把）离锚点再挪多少（单位）
    public float rifleGripHandAngle;                          // 拿步枪：左手再转多少度
    public Vector2 rifleSupportHandOffset;                    // 拿步枪：右手（护木）离锚点再挪多少（单位）
    public float rifleSupportHandAngle;                       // 拿步枪：右手再转多少度
    public Vector2 rifleShoulder = new Vector2(-0.083f, 0.465f);  // 拿步枪时枪绕着转的点（枪托顶在肩上，相对脚底中心；npc1 比 npc2 矮一点，所以低 0.045）

    [Header("面罩 / 表情")]
    public float visorFreq = 1.6f;            // 面罩放下 / 收起的快慢（越大越快）
    [Range(0.3f, 1f)] public float visorDamp = 0.78f;  // 面罩放到底时回弹多少（越小回弹越明显）
    public float exprDebounce = 0.35f;        // 新状态要持续多久才换表情（防止状态一闪一闪导致面罩一直开合）
    public float exprHold = 0.15f;            // 面罩完全放下后停多久再换表情、再收起
    public float runVisorHold = 0.6f;         // 不跑了以后，面罩再多关多久才收起（防止忽开忽关）
    public float glareSpeed = 2.8f;           // 反光扫过面罩的速度（和移动速度挂钩，停下就停）
    public string currentExpression;          // 只读：现在显示的表情（运行时选中 NPC 可以看到，方便测试）

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
    static readonly Vector2 P_RIFLE = new Vector2(856, 1227);     // 步枪挂载点：和 npc2 胸挂上的步枪同一个位置（相对身体底部：npc2 的 (716,1240) − 身体底部 (1060,1655)）
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
    // 用通用枪械动画拿的枪：物品 id → 武器数据 id（RigWeapons.cs）。npc1 自己的手枪还是原来那套（手和枪画在一张图上，等拆开后再换）
    static readonly Dictionary<string, string> GUN_DEFS = new Dictionary<string, string> { { "rifle", "aba_rifle" } };
    readonly Dictionary<string, GunRig> _guns = new Dictionary<string, GunRig>();   // 拼好的通用枪（第一次拿时拼）
    public int testRifleBurst = 5;                    // 测试拿步枪开枪：一组连发几枪
    GunRig _ownGun;                                   // 自己的手枪也登记成一把枪（部件本来就拆好了；换弹用通用换弹）
    Transform _ownMuzzle;                                                            // 自己手枪的枪口

    GunRig GunFor(string key)
    {
        if (key == null || !GUN_DEFS.TryGetValue(key, out string id)) return null;
        if (_guns.TryGetValue(key, out GunRig g) && g != null) return g;
        g = BuildGun(WeaponDef.Get(id), _handPoint);
        if (g != null) _guns[key] = g;
        return g;
    }

    // 持枪手：防护服的两只手（左手握把、右手辅助）拆开握在任何枪的锚点上
    protected override Sprite GripHandArt(bool support, HoldType hold) => Load(support ? "Held/PistolRightHand" : "Held/PistolLeftHand");
    protected override Vector2 GripHandOffset(bool support, HoldType hold) => hold != HoldType.Rifle ? Vector2.zero : support ? rifleSupportHandOffset : rifleGripHandOffset;
    protected override float GripHandAngle(bool support, HoldType hold) => hold != HoldType.Rifle ? 0f : support ? rifleSupportHandAngle : rifleGripHandAngle;

    // 肩膀（角色的属性）：手枪还是原来那套，步枪绕 rifleShoulder 转
    protected override Vector2 ShoulderFor(WeaponDef d) => d.hold == HoldType.Rifle ? rifleShoulder : aimShoulder;

    // 枪上手电筒灯头的发光点（npc1 的画法）
    protected override SpriteRenderer MakeLampGlow(Transform at) { SpriteRenderer sr = GlowDot(at); if (sr != null) { sr.sortingOrder = 48; sr.enabled = false; } return sr; }

    static readonly Dictionary<string, string> HELD_ART = new Dictionary<string, string>
    {
        { "axe", "Held/Axe" }, { "hammer", "Held/Hammer" }, { "medkit", "Held/Medkit" }, { "bottle", "Held/Bottle" }, { "pistol", "Held/PistolFrame" },
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

    Transform _face;   // 根（脚底）/ 身体 / 脸
    Transform _antenna;   // 会动的部件
    Vector3 _faceBase;   // 它们的原始位置
    SpriteRenderer _torsoSprite, _exprSprite;            // 身体（软管跟着它的透明度淡出）/ 表情
    string _exprSet;                                     // 用哪套表情（M1 / F2 / Old …）
    readonly List<HoseMesh> _hoses = new List<HoseMesh>();

    // 只读：身上有没有挂某个挂件（hammer / axe / holster(手枪) / medkit / toolkit / bottle）——挂件只在物品栏里有那件东西时才显示。
    public override bool HasGear(string key) => key != null && _gear.TryGetValue(key, out Transform g) && g != null && g.gameObject.activeInHierarchy;

    // 挂件的根节点 ↔ 物品栏里的物品（锤子 / 斧头连着挂环一起藏，手枪连着枪套，拉链跟着医疗包）
    int _invRev = -1;              // 上次按物品栏刷新挂件时的版本号

    // 按物品栏显示 / 隐藏挂件（物品栏一变 NpcInventory.Revision 就变，Update 里看到就调这里）
    void ApplyInventory()
    {
        _invRev = NpcInventory.Revision;
        bool testScene = WorldManager.Instance == null;                                     // 测试场景：全部显示，方便调动画
        NpcData n = testScene ? null : WorldManager.Instance.GetNpcById(_npcId);
        LoadMounts(item => testScene || NpcInventory.Has(n, item));                         // 挂载点按物品栏挂上 / 取下挂件（衣服属性一直在）
        _heldTimer = 0f;                                                                    // 手上拿的东西马上重新判断（比如斧头刚放回仓库）
    }
    NpcNeedRunner _needRunner;     // 读 NPC 正在做的需求行为（吃 / 休息 / 采集 / 逃跑）
    string _npcId;                 // 这个 NPC 的 id（拼好角色时记下）
    Transform _visorPivot, _glare; // 面罩的转动点（顶边）/ 反光
    SpriteRenderer _heldSprite;    // 拿东西的手（换手时显示）
    SpriteRenderer _handLSprite, _handRSprite;  // 平时的前手 / 后手（拿东西时藏起来）
    float _heldTimer;              // 距离下一次判断拿什么还有多久
    float _speedNow;               // 这一帧的移动速度（判断"停下来干活"用）
    float _lastWater = -1f;        // 上次看到的水分（突然涨了 = 刚喝了水）
    readonly Dictionary<string, float> _holdUntil = new Dictionary<string, float>();       // 临时拿着某件东西到什么时候（医疗包 / 水杯）
    readonly List<SpriteRenderer> _hiddenBelt = new List<SpriteRenderer>();                // 因为拿在手上而藏起来的腰上挂件
    SpriteRenderer _slideSprite;   // 手枪的滑套（只有拿枪时显示）
    SpriteRenderer _magInGun, _leftHand, _gunRightHand;   // 枪里的弹匣 / 持枪左手 / 持枪右手（都挂在枪上）
    static readonly Vector2 P_MAG = new Vector2(1610f, 1222f);     // 弹匣底部中间（原图；手抓这里）
    static readonly Vector2 MAG_DIR = new Vector2(0.135f, 0.991f); // 弹匣插进去的方向（枪的本地坐标，沿弹匣往上）
    Vector3 _heldBase;   // 拿东西的手 / 滑套的原始位置（动画 = 原始位置 + 偏移）
    bool _heldLoop;                // 是在干活（施工 / 采集）：一直循环挥
    static Sprite _bulletSprite;   // 子弹图（代码生成，所有 NPC 共用）
    static Sprite[] _fxFire, _fxFlash, _fxGlow, _fxSmoke;   // 枪火图：枪火 / 闪光 / 亮光（PSD 画的，各 2 种）、烟（代码画，3 种）
    static Sprite _fxRicochet;                      // 跳弹图（PSD 画的）
    static Material _fxAdditive;                    // 亮光 1 用的叠加发光材质（PSD 里是"亮光"混合模式）
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

    float _ant, _antV;               // 天线角度 / 角速度
    Spring2 _faceLag, _hose1, _hose2, _bagBounce;
    Pend _ringH, _hammer, _ringA, _axe, _medkit, _zipper, _toolkit, _holster, _bottle, _rifleMount;
    float _prevRingHV, _prevRingAV;

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
        TryStartBuild();                                 // 等 NPC 的 id 和数据就绪再拼角色
    }

    // 物体被藏起来时（比如动画测试中心切换显示），正在等的协程会被 Unity 停掉、之后也不会再调 Start：
    // 所以重新显示时，如果还没拼好，就重新开始等（不然角色一直是一张不会动的旧图）
    bool _buildStarted, _building;                       // Start 已经跑过 / 正在等着拼
    void OnEnable() { if (_buildStarted) TryStartBuild(); }
    void OnDisable() { _building = false; }              // 协程被停掉了

    void TryStartBuild()
    {
        _buildStarted = true;
        if (_built || _building) return;                 // 拼好了 / 已经在等了
        _building = true;
        StartCoroutine(BuildWhenReady());
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
        // —— 衣服：防护服 ——
        //   衣服属性（一直在）：天线、软管（上面已经拼好）、挂环、枪套、面罩、头灯（下面）
        //   挂载点（类型固定，CharacterRigBase.MountKind）：锤子 / 斧子 / 武器 / 医疗包 / 工具包 / 水瓶 / 步枪
        //     挂载点上的挂件按物品栏显示（有那件东西才挂上）；"武器"挂载点（枪套）能挂不同的武器，其余只挂自己那种
        if (gear.Contains("Hammer"))
        {
            Transform ring = Part("HammerRing", _body, "Gear/HammerRing", P_RING_H, P_BODY, 12).transform;   // 挂环（衣服属性）
            AddGear("ringH", ring);
            Transform hammer = Part("Hammer", ring, "Gear/Hammer", P_HAMMER, P_RING_H, 11).transform;
            AddGear("hammer", hammer);
            AddMount("hammer", MountKind.Hammer, ring, hammer);                       // 挂载点：锤子
        }
        if (gear.Contains("Axe"))
        {
            Transform ring = Part("AxeRing", _body, "Gear/AxeRing", P_RING_A, P_BODY, 14).transform;         // 挂环（衣服属性）
            AddGear("ringA", ring);
            Transform axe = Part("Axe", ring, "Gear/Axe", P_AXE, P_RING_A, 13).transform;
            AddGear("axe", axe);
            AddMount("axe", MountKind.Axe, ring, axe);                                // 挂载点：斧子
        }
        if (gear.Contains("Pistol"))
        {
            Transform holster = Part("Holster", _body, "Gear/Holster", P_HOLSTER, P_BODY, 16).transform;     // 枪套（衣服属性）
            AddGear("holster", holster);
            SpriteRenderer gun = Part("Pistol", holster, "Gear/Pistol", P_HOLSTER, P_HOLSTER, 15);           // 插在枪套里，跟着枪套动
            AddMount("weapon", MountKind.Weapon, holster, gun.transform, gun);       // 挂载点：武器（能换成别的武器）
        }
        if (gear.Contains("Medkit"))
        {
            Transform med = Part("Medkit", _body, "Gear/Medkit", P_MEDKIT, P_BODY, 17).transform;
            AddGear("medkit", med);
            AddGear("zipper", Part("Zipper", med, "Gear/Zipper", P_ZIPPER, P_MEDKIT, 20).transform);  // 拉链挂在医疗包上
            AddMount("medkit", MountKind.Medkit, _body, med);                        // 挂载点：医疗包
        }
        if (gear.Contains("Toolkit"))
        {
            Transform tk = Part("Toolkit", _body, "Gear/Toolkit", P_TOOLKIT, P_BODY, 18).transform;
            AddGear("toolkit", tk);
            AddMount("toolkit", MountKind.Toolkit, _body, tk);                       // 挂载点：工具包
        }
        Transform bottle = Part("Bottle", _body, "Gear/Bottle", P_BOTTLE, P_BODY, 19).transform;
        AddGear("bottle", bottle);
        AddMount("bottle", MountKind.Bottle, _body, bottle);                         // 挂载点：水瓶
        // 步枪挂载点（新加）：位置和 npc2 胸挂上的步枪一样；图用 npc2 的（NpcArt2/Rifle）
        SpriteRenderer rifle = PartFrom("Rifle", _body, "NpcArt2", "Rifle", P_RIFLE, P_BODY, 20);
        AddGear("rifle", rifle.transform);
        AddMount("rifle", MountKind.Rifle, _body, rifle.transform, rifle);         // 挂载点：步枪
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
        // 图层（从下到上）：弹匣 25 → 枪身 26 → 滑套 27 → 左手 28 → 右手 29（和 PSD 一样）
        Vector2 grip = HELD_PIVOT["pistol"];                                          // 握把（枪的转动点）
        _magInGun = Part("PistolMag", heldGo.transform, "Held/PistolMag", P_MAG, grip, 25);           // 枪里的弹匣
        _slideSprite = Part("PistolSlide", heldGo.transform, "Held/PistolSlide", P_SLIDE, grip, 27);  // 滑套
        _slideBase = _slideSprite.transform.localPosition;                            // 记下滑套原位
        _leftHand = Part("PistolLeftHand", heldGo.transform, "Held/PistolLeftHand", grip, grip, 28);  // 持枪左手（一直握着枪）
        _gunRightHand = Part("PistolRightHand", heldGo.transform, "Held/PistolRightHand", grip, grip, 29);  // 持枪右手
        _magInGun.enabled = _slideSprite.enabled = _leftHand.enabled = _gunRightHand.enabled = false;
        // 换弹用：右手 + 新弹匣 + 旧弹匣，挂在角色根节点上（位置每帧按枪算，可以绕到身体后面）
        _rlHand = new GameObject("ReloadHand").transform;
        _rlHand.SetParent(_rig, false);
        _rlHandSprite = _rlHand.gameObject.AddComponent<SpriteRenderer>();
        _rlHandSprite.sprite = Load("Held/PistolRightHand");
        _rlNewMag = new GameObject("ReloadNewMag").AddComponent<SpriteRenderer>();
        _rlNewMag.transform.SetParent(_rig, false);
        _rlNewMag.sprite = Load("Held/PistolMag");
        _rlOldMag = new GameObject("ReloadOldMag").AddComponent<SpriteRenderer>();
        _rlOldMag.transform.SetParent(_rig, false);
        _rlOldMag.sprite = Load("Held/PistolMag");
        _rlHandSprite.enabled = _rlNewMag.enabled = _rlOldMag.enabled = false;
        _muzzle = new GameObject("Muzzle").transform;                                 // 枪口子弹生成点
        _ownMuzzle = _muzzle;
        _muzzle.SetParent(heldGo.transform, false);                                   // 跟着枪动（后坐、朝左翻转）
        _muzzle.localPosition = CanvasToLocal(P_MUZZLE - HELD_PIVOT["pistol"]);       // 枪管最前端
        _ownGun = BindOwnPistol(heldGo.transform);                                    // 自己的手枪登记成"枪"（换弹：枪决定怎么插）

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
        _rifleMount = new Pend(2.6f, 0.30f, 0.006f, 0.003f, 0.6f, 0.0003f, 6f, 0.3f);  // 步枪：背带勒在胸前，只晃一点点

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

    void AddGear(string key, Transform t) { _gear[key] = t; _gearBase[key] = t.localPosition; }

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

    // ============================================================================
    // 每帧流程在 CharacterRigBase.Update（通用骨架）；这里只填 npc1 自己要插进去的几步
    // ============================================================================
    protected override void BeforeFrame()
    {
        if (_invRev != NpcInventory.Revision) ApplyInventory();  // 物品栏变了：刷新挂件
    }

    protected override float AdjustSpeed(float speed, Vector3 velocity, float dt)
    {
        return testSpeed >= 0f ? testSpeed : speed;              // 测试：强制速度（原地踏步）
    }

    protected override void AfterLocomotion(float dt, float speed)
    {
        UpdateFace(dt);                                           // 表情 + 面罩开合
        _speedNow = speed;                                        // 记下速度（判断停下来干活用）
        if (!_dead) UpdateHeld(dt);                               // 拿东西的手（死了不再换）
    }

    protected override void AfterFrame(float dt, float speed)
    {
        UpdateLights();                                           // 头灯 / 身上的光（跟着朝向和枪口）
        GunLampUpdate();                                          // 通用枪上的手电筒（枪的属性）
        if (_gun != null) PlaceGripHands(_gun);                   // 持枪手的微调（Inspector 改了马上看得到）
    }

    // ---------------- 灯光 ----------------
    // 头盔上两个淡蓝色圆灯 = 手电筒（P_LAMP_A 前面那盏、P_LAMP_B 后面那盏）；天线顶上的绿点 = 呼吸灯（都从 npc12.PSD 量的，原图像素）
    static readonly Vector2 P_LAMP_A = new Vector2(1444.5f, 495.2f), P_LAMP_B = new Vector2(1015.1f, 502.5f);
    static readonly Vector2 P_ANT_LED = new Vector2(578.6f, 138.9f);
    UnityEngine.Rendering.Universal.Light2D _beamA, _beamB, _suitLight, _antLight;   // 两道手电筒光 / 身上的微光 / 呼吸灯的光
    Transform _lampA, _lampB, _antLed;                                // 两个圆灯、天线绿点的位置（挂在身体 / 天线上，跟着晃）
    SpriteRenderer _lampGlowA, _lampGlowB, _antGlow;                  // 它们自己发的光（不受光照，一直亮）
    bool _lightsMade;                                                 // 灯已经加过了
    static Sprite _glowSprite;                                        // 发光圆点图（代码画）

    // 每帧：手电筒跟着朝向照（拿枪时跟着枪口方向），呼吸灯一明一暗；Inspector 里改的参数马上生效。旧管线下只有发光点、没有照明
    void UpdateLights()
    {
        if (!_lightsMade) MakeLights();
        float breath = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * _time / Mathf.Max(0.2f, antennaPeriod));   // 呼吸：0 ~ 1
        SetGlow(_lampGlowA, lampGlowColor, headlamp ? 1f : 0.15f, lampGlowSize);                         // 圆灯本身
        SetGlow(_lampGlowB, lampGlowColor, headlamp ? 1f : 0.15f, lampGlowSize);
        SetGlow(_antGlow, antennaColor, 0.25f + 0.75f * breath, 0.6f + 0.4f * breath);                   // 呼吸灯本身
        if (!RenderMats.IsURP) return;
        bool left = FacingLeft;
        float local = _held == "pistol" || _gun != null ? _aimAng : headlampDown;   // 头灯朝哪照（按角色朝向的角度，0 = 正前方）
        float world = left ? 180f - local : local;                       // 朝左时镜像
        Beam(_beamA, _lampA, world);
        Beam(_beamB, _lampB, world);
        if (_suitLight != null)
        {
            _suitLight.enabled = suitGlowIntensity > 0f;
            WorldLighting.SetPoint(_suitLight, suitGlowColor, suitGlowIntensity, suitGlowRadius, 360f);
            _suitLight.transform.localPosition = new Vector3(0f, FEET_Y + 0.4f, 0f);   // 身体中间
        }
        if (_antLight != null && _antLed != null)
        {
            _antLight.enabled = antennaLight > 0f;
            WorldLighting.SetPoint(_antLight, antennaColor, antennaLight * breath, 0.7f, 360f);   // 跟着呼吸一明一暗
            _antLight.transform.position = _antLed.position;
        }
    }

    // 第一次：在身体 / 天线上找好位置，加发光点和灯
    void MakeLights()
    {
        _lightsMade = true;
        _lampA = Anchor("LampA", _body, P_LAMP_A, P_BODY);                // 跟着身体颠动、摇摆、压扁
        _lampB = Anchor("LampB", _body, P_LAMP_B, P_BODY);
        _antLed = Anchor("AntennaLED", _antenna, P_ANT_LED, P_ANTENNA);   // 跟着天线晃
        _lampGlowA = GlowDot(_lampA);
        _lampGlowB = GlowDot(_lampB);
        _antGlow = GlowDot(_antLed);
        if (!RenderMats.IsURP) return;
        // 照明的灯挂在角色根物体上（不跟着左右翻），每帧自己对位置、算方向
        _beamA = WorldLighting.CreatePoint("FlashlightA", transform, headlampColor, headlampIntensity, headlampRange, headlampAngle);
        _beamB = WorldLighting.CreatePoint("FlashlightB", transform, headlampColor, headlampIntensity, headlampRange, headlampAngle);
        WorldLighting.EnableShadows(_beamA);                              // 手电筒照到建筑 / 树石会投影子
        WorldLighting.EnableShadows(_beamB);
        _suitLight = WorldLighting.CreatePoint("SuitGlow", transform, suitGlowColor, suitGlowIntensity, suitGlowRadius, 360f);
        _antLight = WorldLighting.CreatePoint("AntennaLight", transform, antennaColor, antennaLight, 0.7f, 360f);
    }

    // 在某个部件上放一个空物体当位置标记（pivot = 部件自己的轴心，原图像素）
    static Transform Anchor(string name, Transform parent, Vector2 at, Vector2 pivot)
    {
        Transform t = new GameObject(name).transform;
        if (parent != null) t.SetParent(parent, false);
        t.localPosition = CanvasToLocal(at - pivot);
        return t;
    }

    // 一个发光圆点：叠加发光（找不到就用不受光材质），画在面罩上面，所以在黑暗里也一直亮
    SpriteRenderer GlowDot(Transform at)
    {
        if (at == null) return null;
        if (_glowSprite == null) _glowSprite = GlowSprite();
        EnsureFx();                                                       // 借用枪火的叠加发光材质
        SpriteRenderer sr = new GameObject("Glow").AddComponent<SpriteRenderer>();
        sr.transform.SetParent(at, false);
        sr.sprite = _glowSprite;
        sr.sortingOrder = 23;                                             // 面罩（22）上面
        if (_fxAdditive != null) sr.sharedMaterial = _fxAdditive;
        else RenderMats.MakeUnlit(sr);
        return sr;
    }

    // 发光点的颜色 / 亮度 / 大小
    static void SetGlow(SpriteRenderer sr, Color c, float bright, float size)
    {
        if (sr == null) return;
        c.a = Mathf.Clamp01(bright);
        sr.color = c;
        sr.transform.localScale = Vector3.one * Mathf.Max(0.01f, size);
    }

    // 一道手电筒光：放在圆灯的位置，朝 world 度照（2D 聚光是朝自己的"上方"照的）
    void Beam(UnityEngine.Rendering.Universal.Light2D l, Transform lamp, float world)
    {
        if (l == null || lamp == null) return;
        l.enabled = headlamp && headlampIntensity > 0f;
        WorldLighting.SetPoint(l, headlampColor, headlampIntensity, headlampRange, headlampAngle);
        l.transform.position = lamp.position;
        l.transform.rotation = Quaternion.Euler(0f, 0f, world - 90f);
    }

    // 测试开关：换了测试选项马上换手；勾了"测试出手"就挥一下 / 开一枪；手枪循环时每 1.4 秒自动开一枪
    TestHeld _lastTest = TestHeld.None;
    protected override void UpdateTest(float dt)
    {
        if (testHeld != _lastTest) { _lastTest = testHeld; _heldTimer = 0f; UpdateHeld(0f); }  // 选项变了：马上换手
        if (testReload) { testReload = false; PlayReload(0f, testReloadKind); }  // 勾了"测试换弹"：按选的种类换一次弹匣
        bool fire = testFire;                                     // 勾了一下
        bool gun = _held == "pistol" || _gun != null;             // 拿着枪（自己的手枪 / 捡来的枪）
        if (_reloadT >= 0f) _testBurst = 0;                       // 换弹时不开枪
        if (gun && testLoop && _reloadT < 0f)                     // 枪循环：定时自动开枪（换弹时不开）
        {
            _testFireTimer -= dt;
            if (_testFireTimer <= 0f) { _testFireTimer = 1.4f; fire = true; }
        }
        if (_testBurst > 0)                                       // 步枪连发：按这把枪的射速一枪接一枪
        {
            _testBurstTimer -= dt;
            if (_testBurstTimer <= 0f) { TestShot(); _testBurst--; _testBurstTimer += _gun != null ? _gun.def.ShotInterval : 0.15f; }
        }
        if (!fire) return;
        testFire = false;                                         // 自动取消勾选
        if (!gun) { PlayAttack(); return; }                       // 斧头 / 锤子：挥一下
        if (_reloadT >= 0f) return;
        if (_gun != null && _gun.def.hold == HoldType.Rifle)      // 步枪：连发（上一组没打完不重来）
        {
            if (_testBurst <= 0) { _testBurst = Mathf.Max(1, testRifleBurst); _testBurstTimer = 0f; }
        }
        else TestShot();                                          // 手枪：一枪
    }

    int _testBurst;                                               // 测试连发还剩几枪
    float _testBurstTimer;

    // 测试开一枪：后坐 + 子弹 + 枪火（有测试目标往目标打，没有往正前方 2.5 单位打）
    void TestShot()
    {
        PlayAttack();
        if (testAimTarget != null) FireBullet(testAimTarget.position);
        else if (TryGetMuzzle(out Vector3 m)) FireBullet(m + new Vector3(2.5f * (FacingLeft ? -1f : 1f), 0f, 0f));
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
            case TestHeld.Rifle: return "rifle";
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
            if (w == "pistol" || w == "axe" || w == "hammer" || GUN_DEFS.ContainsKey(w)) return w;  // 空手（fists）不换
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

        bool wasGun = _gun != null;
        foreach (GunRig og in _guns.Values) if (og != null) og.Show(false);  // 通用枪先都收起来
        _gun = null;
        _muzzle = _ownMuzzle;
        GunRig g = GunFor(key);
        if (g != null)                                                     // 通用枪械动画（比如 npc2 的步枪）：枪自己的部件和持枪手
        {
            _gun = g;
            _heldSprite.enabled = false;
            if (_slideSprite != null) _slideSprite.enabled = false;
            if (_magInGun != null) _magInGun.enabled = false;
            if (_leftHand != null) _leftHand.enabled = false;
            if (_gunRightHand != null) _gunRightHand.enabled = false;
            StopReload();
            _swingT = -1f; _recoilT = -1f;
            if (_handPoint.parent != _rig) _handPoint.SetParent(_rig, false);   // 持枪：手点挂在根节点上（边走边跑也稳稳端着）
            _gripBase = (Vector2)CanvasToLocal(g.def.restGrip) + (ShoulderFor(g.def) - g.def.shoulder);   // 端枪时握把的位置：枪给的，跟着这个角色的肩膀挪
            _handPoint.localPosition = new Vector3(_gripBase.x, _gripBase.y, 0f);
            _handPoint.localRotation = Quaternion.identity;
            g.root.localPosition = Vector3.zero;
            g.Kick(0f, PPU_FULL);
            g.Show(true);
            _muzzle = g.muzzle;
            HideHand(_handLSprite); HideHand(_handRSprite);                // 双手持枪：两只手都换成枪上的持枪手
            MountPoint mp = MountOf(key);                                  // 挂载点上的那把藏起来（拿在手上了）
            if (mp != null && mp.art != null) HideRenderer(mp.art);
            if (!wasGun) { _aimAng = lowReadyAngle; _lowK = 1f; }          // 刚拿出来：先是枪口朝下
            return;
        }

        Sprite art = key != null && HELD_ART.TryGetValue(key, out string path) ? Load(path) : null;  // 拿东西的手的图
        bool isPistol = key == "pistol" && art != null;                     // 拿的是手枪（要显示滑套和两只手）
        if (_slideSprite != null) _slideSprite.enabled = isPistol;
        if (_magInGun != null) _magInGun.enabled = isPistol;               // 枪里的弹匣
        if (_leftHand != null) _leftHand.enabled = isPistol;               // 持枪左手
        if (_gunRightHand != null) _gunRightHand.enabled = isPistol;       // 持枪右手
        _heldSprite.sortingOrder = isPistol ? 26 : 24;                     // 手枪的枪身要压在弹匣上面
        StopReload();                                                      // 换手时停掉换弹
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
    public override void PlayAttack()
    {
        if (_heldSprite == null || _dead) return;                           // 还没拼好 / 死了
        _rig.localScale = new Vector3(FacingLeft ? -1f : 1f, 1f, 1f);  // 刚转身也先对好朝向（枪口位置才准）
        string want = PickHeld();                                           // 不等 0.25 秒，马上判断拿什么
        if (want != _held) ApplyHeld(want);
        if (_gun != null) { GunFire(); return; }                            // 通用枪械：按这把枪的手感后坐
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
            _readyUntil = _time + Mathf.Max(0f, lowReadyHold);              // 开枪：先把枪举平，过一会儿没事再放下
            UpdateAim(0f);                                                  // 重新算一下瞄准（只算不转）
            _aimAng = _aimWant;                                             // 开枪这一下枪口直接对准目标
            _lowK = 0f;                                                     // 手也马上举回来
            _rig.localScale = new Vector3(FacingLeft ? -1f : 1f, 1f, 1f);   // 可能刚翻身：再对一次朝向
            ApplyAimPose();                                                 // 马上摆好，子弹从对准后的枪口出来
        }
    }

    // 枪口子弹生成点的世界坐标（手上拿着枪才有）
    public override bool TryGetMuzzle(out Vector3 pos)
    {
        bool ok = (_held == "pistol" || _gun != null) && _muzzle != null;   // 拿着枪
        pos = ok ? _muzzle.position : transform.position;                  // 没枪就给角色自己的位置
        return ok;
    }

    // 从枪口生成一颗子弹，飞向 target（只是画面上的子弹；打没打中仍由 NpcCombat 算）。没拿枪返回 false。
    public override bool FireBullet(Vector3 target) { return FireBullet(target, true); }

    // 同上；canRicochet = 这一枪落到地上（没打中人）时有概率出跳弹效果
    public override bool FireBullet(Vector3 target, bool canRicochet)
    {
        if (!TryGetMuzzle(out Vector3 from)) return false;                  // 没拿枪
        target.z = from.z;                                                  // 同一个平面
        GameObject go = new GameObject("Bullet");                           // 子弹（不挂在 NPC 身上，人死了子弹照样飞完）
        go.transform.position = from;                                       // 从枪口出发
        Vector3 d = target - from;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);  // 朝着目标
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _gun != null ? LoadFrom(_gun.def.artFolder, _gun.def.bulletSprite) : BulletSprite();   // 通用枪：这把枪的子弹图
        sr.sortingOrder = 60;                                               // 画在人和建筑上面
        RenderMats.MakeUnlit(sr);                                           // 2D 光照：子弹自己发亮，不受光
        AddTracer(go, sr);                                                  // 曳光（微弱发光 + 照亮）
        float dur = Mathf.Max(0.04f, d.magnitude / Mathf.Max(1f, bulletSpeed));  // 飞多久
        Destroy(go, dur + 0.5f);                                            // 保险：到时间一定删掉
        StartCoroutine(FlyBullet(go.transform, from, target, dur, canRicochet));   // 飞过去
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
            if (_glowSprite == null) _glowSprite = GlowSprite();            // 发光圆点图（和灯头共用）
            EnsureFx();                                                     // 借用枪火的叠加发光材质
            SpriteRenderer g = new GameObject("TracerGlow").AddComponent<SpriteRenderer>();
            g.transform.SetParent(bullet.transform, false);                 // 跟着子弹飞、跟着子弹转
            g.sprite = _glowSprite;                                         // 圆形柔光图（约 0.1 单位大）
            g.transform.localScale = new Vector3(tracerGlowSize.x / 0.1f, tracerGlowSize.y / 0.1f, 1f);   // 拉成细长条
            g.transform.localPosition = new Vector3(-tracerGlowSize.x * 0.4f, 0f, 0f);   // 往后挪：拖在弹头后面（子弹朝自己的 +x 飞）
            g.color = new Color(tracerColor.r, tracerColor.g, tracerColor.b, tracerGlow);   // 颜色 + 亮度（透明度）
            g.sortingOrder = bulletSr.sortingOrder - 1;                     // 画在子弹下面一层
            g.sortingLayerID = bulletSr.sortingLayerID;                     // 和子弹同一个排序层
            if (_fxAdditive != null) g.sharedMaterial = _fxAdditive;        // 叠加发光
            else RenderMats.MakeUnlit(g);                                   // 找不到就用不受光材质
        }
        if (tracerLight > 0f)                                               // 旧管线（没有 2D 光照）时这里返回 null，不影响
            WorldLighting.CreatePoint("TracerLight", bullet.transform, tracerColor, tracerLight, tracerRadius, 360f);
    }

    // 跳弹：子弹打到地上弹起来（PSD 画的 V 字，尖 = 落点；往左飞的子弹左右翻过来）
    protected override void Ricochet(Vector3 at, bool left)
    {
        EnsureFx();
        if (_fxRicochet == null) return;
        SpriteRenderer sr = FxSprite("Ricochet", _fxRicochet, 60, null);   // 和子弹一样画在人和建筑上面
        sr.transform.position = at;
        sr.flipX = left;
        float k = flashSize;
        StartCoroutine(FxLife(sr, ricochetTime, new Vector3(k, k, 1f), new Vector3(k, k, 1f), Vector3.zero, false, 0f, 1f, 1f));
    }

    // ---------------- 枪火：三层（① 火焰 ② 火花 + 亮光 ③ 烟），每层 3 种随机，都从枪口（子弹生成点）出来 ----------------

    // 开一枪：枪火（2 种）+ 闪光（2 种）+ 亮光（2 种）各随机挑一种拼起来。
    // 都放在开枪那一刻的子弹生成点上，朝着那一刻的枪口方向（朝左时左右翻）；之后枪往上跳（后坐）枪火也不跟着动
    public override void MuzzleFlash()   // public：Ballistics 开枪时调（子弹由 Ballistics 飞，枪火还是这里画）
    {
        if (_muzzle == null) return;
        if (_gun != null) { GunFlash(); return; }                           // 通用枪械：这把枪自己的枪火图
        EnsureFx();
        float k = Mathf.Max(0.05f, flashSize);
        Vector3 at = _muzzle.position;                                         // 子弹生成点
        Vector3 dir = _muzzle.TransformVector(Vector3.right);                  // 枪口朝哪（含朝左翻转）
        dir.z = 0f;
        bool left = _muzzle.lossyScale.x < 0f;                                 // 角色朝左（整个角色是左右翻过来的）
        float size = Mathf.Abs(_muzzle.lossyScale.y);                          // 角色本身的缩放
        float ang = dir.sqrMagnitude > 0.000001f ? Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg : (left ? 180f : 0f);
        // 图层顺序和 PSD 一样：枪火在最下面，亮光在中间，闪光在最上面（都画在人上面、子弹下面）
        FxLayer("MuzzleFire", Pick(_fxFire), 55, flashTime, k * size, 0.9f, 1.05f, false, at, ang, left);
        int g = _fxGlow.Length > 0 ? Random.Range(0, _fxGlow.Length) : -1;
        if (g >= 0) FxLayer("MuzzleGlow", _fxGlow[g], 56, glowTime, k * size, 0.95f, 1.05f, g == 0, at, ang, left);   // 亮光 1 = 叠加发光，亮光 2 = 普通
        FxLayer("MuzzleFlash", Pick(_fxFlash), 57, sparkTime, k * size, 0.9f, 1.1f, false, at, ang, left);
        if (flashSmoke) MuzzleSmoke(k);
        if (RenderMats.IsURP && muzzleLightIntensity > 0f) StartCoroutine(MuzzleLight(at));   // 火光照亮周围
    }

    // 从几张图里随机挑一张（没有就返回 null）
    static Sprite Pick(Sprite[] list) { return list != null && list.Length > 0 ? list[Random.Range(0, list.Length)] : null; }

    // 枪火的一层：固定在世界里的 at 点（不挂在枪上），转到 ang 度（朝左时上下翻 = 左右镜像），从 grow0 倍长到 grow1 倍，然后淡掉
    void FxLayer(string name, Sprite sprite, int order, float life, float k, float grow0, float grow1, bool additive, Vector3 at, float ang, bool left)
    {
        if (sprite == null) return;
        SpriteRenderer sr = FxSprite(name, sprite, order, null);
        if (_oldSprite != null) sr.sortingLayerID = _oldSprite.sortingLayerID;  // 和人同一个排序层
        sr.transform.position = at;
        sr.transform.rotation = Quaternion.Euler(0f, 0f, ang);
        if (additive && _fxAdditive != null) sr.sharedMaterial = _fxAdditive;
        float fy = left ? -1f : 1f;                                            // 朝左：上下翻（配合转向，等于左右镜像）
        StartCoroutine(FxLife(sr, life, new Vector3(k * grow0, k * grow0 * fy, 1f), new Vector3(k * grow1, k * grow1 * fy, 1f), Vector3.zero, false, 0f, 1f, 0.7f));
    }

    // 烟：从枪口出来，不跟着人走；朝枪口方向飘一点、往上飘，慢慢散开变淡
    void MuzzleSmoke(float k)
    {
        Vector3 dir = _muzzle.TransformVector(Vector3.right);                  // 枪口朝哪（含朝左翻转）
        dir.z = 0f;
        dir = dir.sqrMagnitude > 0.000001f ? dir.normalized : Vector3.right;
        float s3 = k * Random.Range(0.85f, 1.15f), f3 = Random.value < 0.5f ? 1f : -1f;
        SpriteRenderer smoke = FxSprite("MuzzleSmoke", _fxSmoke[Random.Range(0, _fxSmoke.Length)], 54, null);   // 在人上面、枪火下面
        smoke.transform.position = _muzzle.position;
        smoke.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        Vector3 drift = (dir * 0.08f + Vector3.up * 0.1f) * k;                   // 一共飘多远
        StartCoroutine(FxLife(smoke, smokeTime, new Vector3(s3 * 0.5f, s3 * 0.5f * f3, 1f), new Vector3(s3 * 1.6f, s3 * 1.6f * f3, 1f),
                              drift, true, 0.15f, 0.55f, 1.2f));
    }

    // 生成一个枪火图层（parent = null 时放在世界里）
    SpriteRenderer FxSprite(string name, Sprite sprite, int order, Transform parent)
    {
        SpriteRenderer sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        RenderMats.MakeUnlit(sr);                                            // 2D 光照：枪火 / 跳弹 / 烟自己发亮，不受光（亮光 1 之后会换成叠加材质）
        if (parent != null) sr.transform.SetParent(parent, false);
        return sr;
    }

    // 一个枪火图层的一生：大小从 s0 变到 s1、（世界里的）位置飘 drift、透明度先淡入（fadeIn = 占一生的比例）再按 pow 的快慢淡出
    IEnumerator FxLife(SpriteRenderer sr, float life, Vector3 s0, Vector3 s1, Vector3 drift, bool world, float fadeIn, float alphaMax, float pow)
    {
        life = Mathf.Max(0.02f, life);
        Destroy(sr.gameObject, life + 0.5f);                                  // 保险：一定删掉
        Vector3 start = sr.transform.position;
        float t = 0f;
        while (sr != null && t < life)
        {
            float k = t / life;                                               // 0 → 1
            float e = 1f - (1f - k) * (1f - k);                               // 先快后慢
            sr.transform.localScale = Vector3.Lerp(s0, s1, e);
            if (world) sr.transform.position = start + drift * e;
            float a = k < fadeIn ? k / fadeIn : Mathf.Pow(1f - (k - fadeIn) / Mathf.Max(0.001f, 1f - fadeIn), pow);
            Color c = sr.color;
            c.a = alphaMax * Mathf.Clamp01(a);
            sr.color = c;
            t += Time.deltaTime;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    // 第一次开枪时读好枪火 / 跳弹的图（Resources/NpcArt/Fx/，从 PSD 导出；所有 NPC 共用），烟用代码画
    static void EnsureFx()
    {
        if (_fxFire != null) return;
        _fxFire = FxLoad("MuzzleFire1", "MuzzleFire2");
        _fxFlash = FxLoad("MuzzleFlash1", "MuzzleFlash2");
        _fxGlow = FxLoad("MuzzleGlow1", "MuzzleGlow2");                       // 第 1 张 = 亮光 1（叠加发光），第 2 张 = 亮光 2（PSD 图层 38）
        _fxRicochet = Resources.Load<Sprite>("NpcArt/Fx/Ricochet");
        _fxSmoke = new[] { SmokeSprite(0), SmokeSprite(1), SmokeSprite(2) };
        Shader add = Shader.Find("Legacy Shaders/Particles/Additive");         // 叠加发光（找不到就用普通的，亮度差一点）
        if (add != null) _fxAdditive = new Material(add);
    }

    // 读几张枪火图（找不到的跳过；亮光要保持"第 1 张 = 亮光 1"，所以找不到时整组按读到的算）
    static Sprite[] FxLoad(params string[] names)
    {
        List<Sprite> list = new List<Sprite>();
        foreach (string n in names)
        {
            Sprite sp = Resources.Load<Sprite>("NpcArt/Fx/" + n);
            if (sp != null) list.Add(sp);
        }
        return list.ToArray();
    }

    // —— 画图用的小工具：平滑噪声（每次画出来都一样）——
    static float FxHash(int ix, int iy, int seed)
    {
        unchecked
        {
            uint h = (uint)ix * 374761393u + (uint)iy * 668265263u + (uint)seed * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xffff) / 65535f;
        }
    }

    static float FxNoise(float x, float y, int seed)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = FxHash(ix, iy, seed), b = FxHash(ix + 1, iy, seed), c = FxHash(ix, iy + 1, seed), d = FxHash(ix + 1, iy + 1, seed);
        float top = a + (b - a) * fx, bottom = c + (d - c) * fx;
        return top + (bottom - top) * fy;
    }

    static float FxFbm(float x, float y, int seed)                            // 4 层叠加：大团块 + 小细节
    {
        float s = 0f, amp = 0.5f, f = 1f, tot = 0f;
        for (int i = 0; i < 4; i++) { s += FxNoise(x * f, y * f, seed + i * 31) * amp; tot += amp; amp *= 0.5f; f *= 2f; }
        return s / tot;
    }

    // 按 0~1 的"温度"取颜色（几个颜色节点之间渐变）
    static Color FxRamp(float[] at, Color[] cols, float t)
    {
        t = Mathf.Clamp01(t);
        for (int i = 0; i < at.Length - 1; i++)
            if (t <= at[i + 1]) return Color.Lerp(cols[i], cols[i + 1], (t - at[i]) / Mathf.Max(0.000001f, at[i + 1] - at[i]));
        return cols[cols.Length - 1];
    }

    // 烟图：0 = 圆团，1 = 往前拉长的一团，2 = 一缕一缕的
    static Sprite SmokeSprite(int kind)
    {
        const int w = 96, h = 64;
        int seed = 500 + kind * 11;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1), v = (y - (h - 1) * 0.5f) / ((h - 1) * 0.5f);
                float n = FxFbm(u * 4f, v * 3f, seed), st = FxFbm(u * 2f, v * 7f, seed + 3);
                float d;
                if (kind == 0) d = Mathf.Sqrt(Sq((u - 0.45f) / 0.4f) + Sq(v / 0.85f)) + (n - 0.5f) * 1f;
                else if (kind == 1) d = Mathf.Sqrt(Sq((u - 0.5f) / 0.48f) + Sq(v / 0.55f)) + (n - 0.5f) * 0.9f;
                else d = Mathf.Sqrt(Sq((u - 0.5f) / 0.5f) + Sq(v / 0.5f)) + (st - 0.5f) * 1.3f;
                float a = Mathf.Clamp01((1f - d) / 0.6f) * (0.55f + 0.45f * n);
                float g = 0.62f + 0.3f * n;                                   // 浓的地方亮一点
                t.SetPixel(x, y, new Color(g, g, g + 0.02f, Smooth01(a)));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.25f, 0.5f), 960f);   // 轴心靠后一点 = 枪口；约 0.1 单位
    }

    // 子弹图：一小条亮黄色，尾巴渐隐（代码画，不用素材）
    static Sprite BulletSprite()
    {
        if (_bulletSprite != null) return _bulletSprite;
        _bulletSprite = Resources.Load<Sprite>("NpcArt/Fx/Bullet");           // PSD 画的子弹（轴心在弹头）
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
    protected override void AnimateHeld(float dt)
    {
        _actionLean = 0f; _actionShift = 0f;                                // 身体的额外晃动先清零
        if (_handPoint == null) return;                                     // 还没拼好
        if (_gun != null) { if (!AnimateGunReload(dt)) GunRecoilPose(dt); return; }   // 通用枪械：换弹 / 持枪 + 后坐（CharacterRigBase）
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
        else if (_held == "pistol" && _reloadT >= 0f)                       // 正在换弹：换弹动画接管
        {
            AnimateReload(dt);
            return;
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
        if (_held == "pistol")                                              // 持枪：手绕肩膀转到瞄准角度，后坐沿枪的方向顶回来
        {
            Vector2 hp = AimHand(_aimAng, (Vector2)off);
            float tremA = 0f;                                               // 持枪手抖：紧张 / 武器等级低（取抖得厉害的那个）
            float trem = Mathf.Max(nervous, LevelTremble());
            if (trem > 0f) hp += Tremble(trem, 0f, out tremA);
            _handPoint.localPosition = new Vector3(hp.x, hp.y, 0f);
            _handPoint.localRotation = Quaternion.Euler(0f, 0f, _aimAng + angle + tremA);   // 瞄准角度 + 枪口上扬 + 手抖
        }
        else
        {
            _handPoint.localPosition = _heldBase + off;                     // 手点移动（拿着的东西跟着走）
            _handPoint.localRotation = Quaternion.Euler(0f, 0f, angle);     // 绕手腕转
        }
        if (_slideSprite != null) _slideSprite.transform.localPosition = _slideBase + new Vector3(-slide / PPU_FULL, 0f, 0f);
    }

    // 后坐曲线：t = 开枪后过了几秒；rise 秒内从 0 冲到 1，然后按 ret 的快慢回到 0
    static float Kick(float t, float rise, float ret)
    {
        rise = Mathf.Max(0.001f, rise); ret = Mathf.Max(0.001f, ret);
        return t < rise ? t / rise : Mathf.Exp(-(t - rise) / ret);
    }

    // ---------------- 换弹 1：战术换弹 ----------------

    // 这次换弹一共要多少"标准时间"
    float ReloadTotal() { return ReloadTotal(_reloadKind); }

    // 某种换弹一共要多少"标准时间"（通用换弹按枪 + 配件算；颤抖换弹还是旧的一套）
    public override float ReloadTotal(ReloadKind kind)
    {
        if (kind == ReloadKind.Shaky && _gun == null)                       // 颤抖换弹：普通换弹的时间（不算掉弹匣 / 卡住多花的时间）
            return Mathf.Max(0.05f, rlLockTime + nrCatchTime + rlDropTime + rlStowTime + rlGrabTime
                                  + rlBringTime + rlAlignTime + rlInsertTime + rlReturnTime);
        return base.ReloadTotal(kind);
    }

    // 外部调用：拿枪时换一次弹匣（战术换弹）
    public void PlayReload(float fitSeconds) { PlayReload(fitSeconds, ReloadKind.Tactical); }

    // 外部调用：拿枪时换一次弹匣。fitSeconds > 0 时整段动画压缩 / 拉长到正好这么多秒（NpcCombat 传换弹时间）；
    // 0 = 按调的时间 × reloadSpeed 播（测试用）。自己的手枪 / 捡来的枪都走通用换弹（CharacterRigBase.Reload.cs）
    public override void PlayReload(float fitSeconds, ReloadKind kind)
    {
        if (_handPoint == null || _dead) return;                            // 还没拼好 / 死了
        string want = PickHeld();                                           // 先确认手上拿的是枪
        if (want != _held) ApplyHeld(want);
        SyncHipPouch();                                                     // 屁股后面拿弹匣的位置（Inspector 调的）
        if (kind == ReloadKind.Shaky && _gun == null)                       // 颤抖换弹：自己的手枪，旧的一套
        {
            if (_held != "pistol") return;
            _reloadKind = kind;
            _qrDropped = false;
            BuildShakyPlan();                                               // 先随机好这次掉几次、卡几次
            _reloadRate = fitSeconds > 0f ? ReloadTotal() / fitSeconds : Mathf.Max(0.05f, reloadSpeed) / LevelTimeMul();
            _reloadT = 0f;
            _recoilT = -1f;
            return;
        }
        base.PlayReload(fitSeconds, kind);
    }

    // 通用换弹的几处：要换弹的枪 / 枪在哪 / 换完的样子 / 时间倍数
    protected override GunRig ReloadGun() => _gun != null ? _gun : (_held == "pistol" ? _ownGun : null);
    protected override GunRig TimingGun() => _gun != null ? _gun : _ownGun;   // 还没拿出手枪时（NpcCombat 先问时间再换弹）按自己的手枪算
    protected override Vector2 ReloadAimHand(float ang, float lowMul) => _gun == null ? AimHand(ang, Vector2.zero, lowMul) : base.ReloadAimHand(ang, lowMul);
    protected override void ReloadDonePose() { if (_gun == null) AnimateHeldRest(); else base.ReloadDonePose(); }
    protected override float ReloadTimeMul() => LevelTimeMul();

    // 没有胸挂：从屁股后面拿弹匣，空弹匣也塞回去（位置用 Inspector 里的 Rl Pouch 等）
    void SyncHipPouch()
    {
        _hipReload.point = rlPouch;
        _hipReload.angle = rlPouchAngle;
        _hipReload.behindX = rlBehindX;
    }

    // 自己的手枪登记成一把枪：部件本来就拆好了（弹匣 / 枪身 / 滑套 / 两只持枪手），按 WeaponDef "suit_pistol" 的名字对上
    GunRig BindOwnPistol(Transform root)
    {
        WeaponDef d = WeaponDef.Get("suit_pistol");
        if (d == null) return null;
        GunRig g = new GunRig { def = d, root = root, muzzle = _muzzle };
        Dictionary<string, SpriteRenderer> mine = new Dictionary<string, SpriteRenderer>
        {
            { "PistolMag", _magInGun }, { "PistolFrame", _heldSprite }, { "PistolSlide", _slideSprite },
            { "PistolLeftHand", _leftHand }, { "PistolRightHand", _gunRightHand },
        };
        foreach (WeaponPart p in d.parts)
        {
            if (p == null || !mine.TryGetValue(p.name, out SpriteRenderer sr) || sr == null) continue;
            WeaponPart q = p;
            if (p.name == "PistolSlide")                                    // 滑套往后弹多远：用这个角色调的 Slide Kick
                q = new WeaponPart { name = p.name, path = p.path, pivot = p.pivot, order = p.order, kick = slideKick, hand = p.hand };
            g.Add(q, sr);
        }
        return g;
    }

    // 换弹结束 / 被打断：所有东西放回原样
    void StopReload()
    {
        StopGunReload();                                                    // 通用换弹的换弹手、两个弹匣、枪上的手、滑套
        if (_rlHandSprite != null) _rlHandSprite.enabled = false;           // 颤抖换弹（旧的一套）也放回去
        if (_rlNewMag != null) _rlNewMag.enabled = false;
        if (_rlOldMag != null) _rlOldMag.enabled = false;
        bool pistol = _held == "pistol" && _leftHand != null && _leftHand.enabled;   // 正拿着自己的手枪（左手显示着）
        if (_gunRightHand != null) _gunRightHand.enabled = pistol;          // 枪上的右手、弹匣显示回来
        if (_magInGun != null) _magInGun.enabled = pistol;
        if (_slideSprite != null) _slideSprite.transform.localPosition = _slideBase;  // 滑套复位
    }

    // 每帧：换弹（颤抖换弹走旧的一套，其他走通用换弹）
    void AnimateReload(float dt)
    {
        if (_reloadKind == ReloadKind.Shaky && _rlGun == null) { AnimateShakyReload(dt); return; }
        if (!AnimateGunReload(dt)) StopReload();
    }

    // 快速换弹：旧弹匣脱手，自己往下掉（空中时挂在角色下面，跟着人走、按部件顺序遮挡）
    void DropMag(Vector2 pos, float ang, Vector2 vel) { DropMag(pos, ang, vel, RandIn(qrSpin)); }

    // 同上，自己指定转多快（度 / 秒）
    void DropMag(Vector2 pos, float ang, Vector2 vel, float spin)
    {
        SpriteRenderer sr = new GameObject("DroppedMag").AddComponent<SpriteRenderer>();
        sr.sprite = Load("Held/PistolMag");
        sr.transform.SetParent(_rig, false);
        sr.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
        sr.sortingOrder = 24;                                               // 在右手（29）、新弹匣（25）、枪身（26）后面，身体前面
        Destroy(sr.gameObject, qrGroundTime + 3f);                         // 保险：中途被打断也一定会删掉
        StartCoroutine(FallMag(sr, pos, ang, vel, spin, RandIn(qrGroundY)));
    }

    // ---------------- 武器操作等级 / 颤抖 ----------------

    // 按武器等级，换弹时间要乘几（1 级最慢，5 级最快）
    float LevelTimeMul()
    {
        if (levelReloadTime == null || levelReloadTime.Length == 0) return 1f;
        int i = Mathf.Clamp(weaponLevel, 1, levelReloadTime.Length) - 1;
        return Mathf.Max(0.1f, levelReloadTime[i]);
    }

    // 按武器等级，平时持枪手抖多少（0 = 不抖）
    float LevelTremble()
    {
        if (levelTremble == null || levelTremble.Length == 0) return 0f;
        int i = Mathf.Clamp(weaponLevel, 1, levelTremble.Length) - 1;
        return Mathf.Clamp01(levelTremble[i]);
    }

    // 这个人换这种弹匣要几秒 = 动画标准时间 × 武器等级倍数（CharacterRigBase.ReloadSeconds，倍数来自 ReloadTimeMul）

    // 颤抖：用平滑噪声让位置 / 角度一直小幅乱抖（k = 幅度倍数；seed 不同 = 各抖各的）
    Vector2 Tremble(float k, float seed, out float ang)
    {
        float ts = _time * trembleFreq;
        float nx = Mathf.PerlinNoise(seed + 0.3f, ts) * 2f - 1f;
        float ny = Mathf.PerlinNoise(seed + 5.1f, ts * 1.13f) * 2f - 1f;
        float na = Mathf.PerlinNoise(seed + 9.7f, ts * 0.91f) * 2f - 1f;
        ang = na * trembleAngle * k;
        return new Vector2(nx, ny) * (trembleMove * k);
    }

    // ---------------- 换弹 4：颤抖换弹（基于普通换弹） ----------------

    // 颤抖换弹的一段：右手从哪个姿势到哪个姿势、拿着什么、这段开始时发生什么
    struct ShakySeg
    {
        public int from, to;       // 姿势（下面的 Pose 常量）
        public float len;          // 多长（标准时间）
        public float start;        // 从第几秒开始（算好的）
        public int item;           // 右手拿着：0 没有 / 1 旧弹匣 / 2 新弹匣 / 3 前半段旧、后半段新 / 4 前半段空、后半段新
        public int evt;            // 开始时：0 无 / 1 旧弹匣弹出 / 2 新弹匣掉了 / 3 卡住了（枪磕一下）/ 4 第一次对准 / 5 插好了
    }
    const int PoseRest = 0, PoseUnder = 1, PosePouch = 2, PoseReady = 3, PoseIn = 4, PoseFail = 5, PoseSlip = 6;   // 握把 / 握把下方 / 身后 / 对准 / 插好 / 插歪卡住 / 掉弹匣的地方
    readonly List<ShakySeg> _shaky = new List<ShakySeg>();   // 这次颤抖换弹的全部分段
    readonly List<float> _tFails = new List<float>();        // 每次卡住（枪磕一下）的时刻
    int _shakyFired;                                         // 下一段要触发事件的序号
    float _tOldOut, _tAlign0, _tSeated, _tShakyEnd;          // 旧弹匣弹出 / 第一次对准 / 插好 / 结束 的时刻
    readonly Vector2[] _poseP = new Vector2[7];              // 这一帧各个姿势的位置
    readonly float[] _poseA = new float[7];                  // 这一帧各个姿势的角度
    public System.Action OnNewMagDropped;                    // 颤抖换弹时新弹匣掉到地上了（游戏规则之后接：物品栏再扣一个弹匣）

    // 加一段（接在上一段后面）
    void AddShaky(int from, int to, float len, int item, int evt)
    {
        float start = 0f;
        if (_shaky.Count > 0) { ShakySeg last = _shaky[_shaky.Count - 1]; start = last.start + last.len; }
        _shaky.Add(new ShakySeg { from = from, to = to, len = Mathf.Max(0f, len), start = start, item = item, evt = evt });
    }

    // 开始颤抖换弹时：随机好这次新弹匣掉几次、插歪几次，排出整段动作
    void BuildShakyPlan()
    {
        _shaky.Clear(); _tFails.Clear(); _shakyFired = 0;
        AddShaky(PoseRest, PoseRest, rlLockTime, 0, 0);                     // ① 抬枪、滑套挂机（右手还握着握把）
        AddShaky(PoseRest, PoseUnder, nrCatchTime, 0, 0);                   // ② 右手移到握把下方
        AddShaky(PoseUnder, PoseUnder, rlDropTime, 1, 1);                   // ③ 旧弹匣弹出，掉进右手
        AddShaky(PoseUnder, PosePouch, rlStowTime, 1, 0);                   // ④ 带回身后
        AddShaky(PosePouch, PosePouch, rlGrabTime, 3, 0);                   // ⑤ 放下旧的，拿起新的
        for (int i = 0; i < 2 && Random.value < srDropChance; i++)          // 新弹匣掉了（最多 2 次）：回身后再拿一个
        {
            AddShaky(PosePouch, PoseSlip, rlBringTime, 2, 0);               //   拿到手枪下方……
            AddShaky(PoseSlip, PoseSlip, srFumbleTime, 0, 2);               //   手一滑掉了，愣一下
            AddShaky(PoseSlip, PosePouch, rlBringTime, 0, 0);               //   回身后
            AddShaky(PosePouch, PosePouch, rlGrabTime, 4, 0);               //   再拿一个
        }
        AddShaky(PosePouch, PoseUnder, rlBringTime, 2, 0);                  // ⑥ 拿到握把下方
        AddShaky(PoseUnder, PoseReady, rlAlignTime, 2, 4);                  // ⑦ 对准
        for (int i = 0; i < srMaxFails && Random.value < srFailChance; i++) // 插不进去：退出来重新对准
        {
            AddShaky(PoseReady, PoseFail, rlInsertTime, 2, 0);              //   插歪了，卡住
            AddShaky(PoseFail, PoseUnder, srFailBackTime, 2, 3);            //   枪磕一下，退出来
            AddShaky(PoseUnder, PoseReady, rlAlignTime, 2, 0);              //   重新对准
        }
        AddShaky(PoseReady, PoseIn, rlInsertTime, 2, 0);                    // ⑧ 插进去
        AddShaky(PoseIn, PoseRest, rlReturnTime, 0, 5);                     // ⑨ 插好了，回握把
        _tOldOut = _shaky[2].start;                                         // 记下几个时刻（枪的动作用）
        foreach (ShakySeg g in _shaky)
        {
            if (g.evt == 4) _tAlign0 = g.start;
            if (g.evt == 3) _tFails.Add(g.start);
            if (g.evt == 5) _tSeated = g.start;
        }
        _tShakyEnd = _tSeated + rlReturnTime;
    }

    // 每帧：颤抖换弹（枪的动作和普通换弹一样，多了一直抖、卡住时磕一下）
    void AnimateShakyReload(float dt)
    {
        _reloadT += dt * _reloadRate;                                       // 往前走
        float t = _reloadT;
        if (_shaky.Count == 0 || t >= _tShakyEnd) { StopReload(); AnimateHeldRest(); return; }   // 播完了
        int si = 0;                                                         // 现在在第几段
        while (si < _shaky.Count - 1 && t >= _shaky[si].start + _shaky[si].len) si++;
        ShakySeg cur = _shaky[si];
        float k = cur.len > 0f ? Mathf.Clamp01((t - cur.start) / cur.len) : 1f;   // 这一段走了多少（0~1）
        float e = Ease(k);

        // ---- 1. 枪 ----
        float raise = t < _tSeated ? Ease(t / Mathf.Max(0.001f, rlLockTime)) : 1f - Ease(Seg(t, _tSeated, rlReturnTime));   // ① 抬起，⑨ 放下
        float align = t < _tSeated ? rlGunAlign * Ease(Seg(t, _tAlign0, rlAlignTime))                                       // ⑦ 转过去
                                   : rlGunAlign * (1f - Ease(Seg(t, _tSeated, rlReleaseTime)));                             // 插好后转回来
        float slideAt = _tSeated + Mathf.Max(0f, rlSlideDelay);                                                             // 滑套复位的时刻
        float shakeDrop = Shake(t - _tOldOut, rlDropTime);                                                                  // 旧弹匣弹出：上下 + 旋转抖
        float shakeInsert = Shake(t - _tSeated, rlReleaseTime);                                                             // 插到底：上下抖
        float shakeSlide = Shake(t - slideAt, rlReleaseTime);                                                               // 滑套复位：旋转抖
        float bump = 0f;
        foreach (float f in _tFails) bump += Shake(t - f, rlDropTime);                                                      // 卡住：枪磕一下
        Vector2 gunTrem = Tremble(trembleReloadMul, 0f, out float gunTremA);                                                // 枪一直抖
        float aimNow = _aimAng * (1f - raise);                                                                              // 抬枪时转回正前方，放下时转回去对准
        Vector2 gunPos = AimHand(aimNow, Vector2.zero, 1f - raise) + rlGunRaise * raise
                       + new Vector2(0f, rlShake * (shakeDrop + shakeInsert + srFailShake * bump)) + gunTrem;
        float gunAng = aimNow + rlGunTilt * raise + align + rlShakeAngle * (shakeDrop + shakeSlide) + gunTremA;
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
        float slideK = t < slideAt ? Ease(t / Mathf.Max(0.001f, rlLockTime)) : 1f - Seg(t, slideAt, rlReleaseTime * 0.4f);   // 滑套：挂机 → 复位
        if (_slideSprite != null) _slideSprite.transform.localPosition = _slideBase + new Vector3(-slideKick * rlSlideLock * slideK / PPU_FULL, 0f, 0f);

        // ---- 2. 这一帧各个姿势在哪（跟着枪走） ----
        Vector2 magIn = (Vector2)CanvasToLocal(P_MAG - HELD_PIVOT["pistol"]);           // 枪里弹匣底部（枪的本地坐标）
        _poseP[PoseRest] = gunPos + Rot(Vector2.zero, gunAng); _poseA[PoseRest] = gunAng;
        _poseP[PoseUnder] = gunPos + Rot(magIn - MAG_DIR * (rlReady + rlUnderGap), gunAng) - Rot(rlMagInHand, gunAng); _poseA[PoseUnder] = gunAng;
        _poseP[PosePouch] = rlPouch; _poseA[PosePouch] = rlPouchAngle;
        _poseP[PoseReady] = gunPos + Rot(magIn - MAG_DIR * rlReady, gunAng) - Rot(rlMagInHand, gunAng); _poseA[PoseReady] = gunAng;
        _poseP[PoseIn] = gunPos + Rot(magIn, gunAng) - Rot(rlMagInHand, gunAng); _poseA[PoseIn] = gunAng;
        float failAng = gunAng + srFailTilt;                                             // 插歪了：歪一点、偏一点、只插进去一半
        _poseA[PoseFail] = failAng;
        _poseP[PoseFail] = gunPos + Rot(magIn - MAG_DIR * (rlReady * (1f - Mathf.Clamp01(srFailDepth))) + new Vector2(srFailOffset, 0f), gunAng) - Rot(rlMagInHand, failAng);
        _poseP[PoseSlip] = _poseP[PoseUnder] + srSlipOffset;                            // 掉弹匣的地方：握把下方再偏一点（身体外面、手枪下面）
        _poseA[PoseSlip] = _poseA[PoseUnder];

        // ---- 3. 右手 ----
        Vector2 hp = Vector2.Lerp(_poseP[cur.from], _poseP[cur.to], e);
        float ha = Mathf.LerpAngle(_poseA[cur.from], _poseA[cur.to], e);
        bool viaPouch = cur.from == PosePouch || cur.to == PosePouch;                    // 这段要去 / 从身后
        if (viaPouch && cur.from != cur.to) hp += new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));   // 走弧线
        bool behind = cur.from == PosePouch && cur.to == PosePouch ? true : viaPouch && hp.x < rlBehindX;   // 过了身体右边缘线：绕到身后
        Vector2 handTrem = Tremble(trembleReloadMul, 7.3f, out float handTremA);         // 右手也一直抖（和枪抖得不一样）
        hp += handTrem; ha += handTremA;
        bool second = k >= 0.5f;                                                         // 这段的后半段
        bool holdOld = cur.item == 1 || (cur.item == 3 && !second);
        bool holdNew = cur.item == 2 || ((cur.item == 3 || cur.item == 4) && second);
        bool reloadHand = si > 0;                                                        // ② 开始右手离开枪

        // ---- 4. 摆右手和两个弹匣 ----
        if (_gunRightHand != null) _gunRightHand.enabled = !reloadHand;                  // 枪上的右手 ↔ 换弹用的右手
        _rlHandSprite.enabled = reloadHand;
        _rlHand.localPosition = new Vector3(hp.x, hp.y, 0f);
        _rlHand.localRotation = Quaternion.Euler(0f, 0f, ha);
        _rlHandSprite.sortingOrder = behind ? 6 : 29;
        _rlNewMag.sortingOrder = behind ? 5 : 25;
        _rlOldMag.sortingOrder = behind ? 4 : 24;
        Vector2 nb = hp + Rot(rlMagInHand, ha);                                          // 手里弹匣底部的位置
        _rlNewMag.enabled = holdNew;
        _rlNewMag.transform.localPosition = new Vector3(nb.x, nb.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha);
        if (_magInGun != null) _magInGun.enabled = !(t >= _tOldOut && t < _tSeated);    // 旧弹匣弹出后枪里没弹匣，插好了再显示
        _rlOldMag.enabled = holdOld;
        if (holdOld)
        {
            Vector2 op = nb; float oa = ha;
            if (cur.evt == 1)                                                            // ③ 从枪里掉进手里
            {
                op = Vector2.Lerp(gunPos + Rot(magIn, gunAng), nb, e);
                oa = Mathf.LerpAngle(gunAng, ha, e);
            }
            _rlOldMag.transform.localPosition = new Vector3(op.x, op.y, 0f);
            _rlOldMag.transform.localRotation = Quaternion.Euler(0f, 0f, oa);
        }

        // ---- 5. 每段开始时的事件（只触发一次） ----
        while (_shakyFired <= si)
        {
            ShakySeg g = _shaky[_shakyFired];
            _shakyFired++;
            if (g.evt == 2)                                                              // 新弹匣从手里滑掉，掉到地上
            {
                DropMag(nb, ha, srFling * Random.Range(0.7f, 1.3f), RandIn(srFlingSpin));   // 手一甩：往前上方弹出去，边飞边转
                if (OnNewMagDropped != null) OnNewMagDropped();
            }
        }
    }

    // 换弹播完的那一帧：枪摆回瞄准的位置（没瞄准 = 平时的位置）
    void AnimateHeldRest()
    {
        ApplyAimPose();
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

    protected override void StepSecondary(float dt, Motion mo)
    {
        float acc = mo.acc, refV = mo.refV, tilt = mo.tilt, tilta = mo.tilta, boba = mo.boba;   // 通用骨架算好的走路数据（CharacterRigBase.Step）

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
        _rifleMount.Step(dt, acc * g, refV * g, tilt, tilta * g, boba * g, 0f);
        _holster.Step(dt, acc * g, refV * g, tilt, tilta * g, boba * g, 0f);
        _bottle.Step(dt, acc * g, refV * g, tilt, tilta * g, boba * g, 0f);
        // 拉链挂在医疗包上：医疗包一晃，它就被甩
        _zipper.Step(dt, acc * g, refV * g, tilt + _medkit.a, tilta * g, boba * g, -2.0f * _medkit.v * (2f * Mathf.PI * 3.4f));

        // 反光：走多少就扫多少（跑得快扫得快），停下就停在原地
        _glareX -= glareSpeed * refV * dt;                                        // 往身后方向扫
        if (_glareX < GLARE_END - GLARE_GAP)                                      // 扫出面罩左边、再空走一小段后
            _glareX += GLARE_ENTER - (GLARE_END - GLARE_GAP);                     // 从右边重新进来

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
    protected override void ApplyAttachments()
    {
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
        SetGear("rifle", _rifleMount.a, Vector3.zero);
        SetGear("holster", _holster.a, bounce);
        SetGear("bottle", _bottle.a, bounce);

        ApplyVisor();                                                  // 面罩 + 反光
    }

    // 武器挂载点（枪套）换成别的武器时用哪张图（以后加新武器在这里加一行）
    protected override Sprite MountArt(MountPoint m, string item)
    {
        switch (item)
        {
            case "pistol": return Load("Gear/Pistol");
            case "rifle": return LoadFrom("NpcArt2", "Rifle");
            default: return null;                                       // 没有专门的图：保持原来的
        }
    }

    void SetGear(string key, float angle, Vector3 offset)
    {
        if (!_gear.TryGetValue(key, out Transform t)) return;         // 这个 NPC 没带这件挂件
        t.localPosition = _gearBase[key] + offset;
        t.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    // 人删掉时，地上的武器一起收走
    void OnDestroy()
    {
        foreach (GameObject g in _dropped) if (g != null) Destroy(g);
    }

    // 死的时候：停掉开枪 / 换弹 / 挥动 / 瞄准
    protected override void StopForDeath()
    {
        StopReload();
        _recoilT = -1f; _swingT = -1f; _heldLoop = false;
        _aimUntil = 0f;
    }

    // 受击时头在面罩里多甩一下
    protected override void HeadWhip(float push, float k)
    {
        if (_faceLag != null) _faceLag.v += new Vector2(-push * 60f * k * hitHeadWhip, 0f);
    }

    // 拿着的图连着手的东西：掉下去用腰上挂的那张图
    static readonly Dictionary<string, string> DROP_ART = new Dictionary<string, string>
    {
        { "axe", "Gear/Axe" }, { "hammer", "Gear/Hammer" }, { "medkit", "Gear/Medkit" }, { "bottle", "Gear/Bottle" },
    };

    // 武器脱手：复制一份扔出去，手上换回空手；腰上对应的那件不再出现（已经掉出去了）
    protected override void DropWeapon(float dt)
    {
        _weaponDropped = true;
        string was = _held;
        if (_gun != null)                                                   // 通用枪：枪身 + 弹匣（不要持枪手）扔出去
        {
            SpawnDropped(_gun.root, "Hand", null, 0, dt, Color.white, 0f, 0f);
            ApplyHeld(null);
            MountPoint mp = MountOf(was);
            if (mp != null && mp.art != null) HideRenderer(mp.art);          // 掉出去了，挂载点上也没有了
            return;
        }
        if (was == null || _heldSprite == null) return;                    // 本来就空手
        Transform src = _heldSprite.transform;
        if (was == "pistol") SpawnDropped(src, "Hand", null, 0, dt, Color.white, 0f, 0f);          // 手枪：枪身 + 弹匣 + 滑套（不要两只持枪手）
        else if (DROP_ART.TryGetValue(was, out string art)) SpawnDropped(src, "Hand", Load(art), 24, dt, Color.white, 0f, 0f);
        ApplyHeld(null);                                                    // 手上空了（两只手换回来，腰上的挂件放回去）
        if (was == "axe" || was == "hammer" || was == "bottle") HideGear(was);   // 掉出去的那件不在腰上了
        else if (was == "medkit") { HideGear("medkit"); HideGear("zipper"); }
        else if (was == "pistol" && _gear.TryGetValue("holster", out Transform h))
        {
            Transform pistol = h.Find("Pistol");                            // 枪套里的手枪
            if (pistol != null) HideRenderer(pistol.GetComponent<SpriteRenderer>());
        }
    }

    // 站起来后按情况重新拿东西（腰上的挂件也放回去）
    protected override void RestoreAfterRevive()
    {
        ApplyHeld(null);
        _heldTimer = 0f;
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
            if (_mat == null) _mat = RenderMats.MeshSpriteMaterial();    // 2D 光照：软管是身体的一部分，受光（旧管线 = Sprites/Default）
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
