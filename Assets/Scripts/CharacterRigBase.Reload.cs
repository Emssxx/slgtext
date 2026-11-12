using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 换弹（CharacterRigBase 的一部分，partial）
//   【枪决定】弹匣怎么拔、怎么对准、怎么插：WeaponDef 的"换弹"一栏（手枪式 / 步枪式、弹匣部件、插入方向、换弹手）。
//   【配件决定】新弹匣从哪拿、空弹匣往哪收：ReloadStyle。
//     有胸挂 → 胸挂弹匣袋（ChestRigReload，npc2 那套）；什么都没有 → 屁股后面（HipPouchReload，npc1 那套，空弹匣也塞回去）。
//     角色把身上配件提供的换弹方式用 AddReloadGear 登记进来，换弹时按顺序挑第一个能用的，都不能用就从屁股后面拿。
//     以后加新配件（腿挂、腰包……）只要写一个新的 ReloadStyle 子类，换了装备换弹动画自动跟着换。
//   【换弹手】只是在移动：图就用枪上那只持枪手（WeaponDef.reloadHand）。
//   时间 / 姿势参数还是 Inspector 里那几栏（换弹 1~3、步枪战术 / 快速 / 普通换弹），角色之间共用一套名字。
// ============================================================================
public abstract partial class CharacterRigBase
{
    // ======================= 配件：新弹匣从哪拿、空弹匣往哪收 =======================
    public abstract class ReloadStyle
    {
        public string gear;     // 身上要有这个配件才能用（HasGear 检查；null = 不用检查）
        public string label = "配件";   // 名字（测试窗口里显示）
        public bool disabled;           // 测试：当作没穿这个配件（换弹改用下一个配件 / 屁股后面）

        // 这把枪能不能用这种方式换弹
        public virtual bool Fits(CharacterRigBase rig, GunRig gun) => !disabled && (gear == null || rig.HasGear(gear));
        // 换弹前的准备（比如弹匣袋里没有满弹匣了：测试先补满）
        public virtual void Prepare(CharacterRigBase rig, GunRig gun) { }
        // 旧弹匣有没有地方收（没有 → 只能快速换弹，把旧弹匣丢地上）
        public virtual bool CanStow(CharacterRigBase rig, GunRig gun) => true;
        // 开始这次换弹：stow = 旧弹匣要收回来
        public virtual void Begin(CharacterRigBase rig, GunRig gun, bool stow) { }
        // 手在哪拿新弹匣（根节点本地坐标）：pos = 插到底时手在哪，outPos = 拔出来时手在哪，ang = 手转多少度
        //   magInHand / magTilt = 弹匣底部在手里的位置 / 弹匣在手里歪多少
        public abstract void TakePose(CharacterRigBase rig, Vector2 magInHand, float magTilt, out Vector2 pos, out Vector2 outPos, out float ang);
        // 手在哪收旧弹匣（默认和拿新弹匣同一个地方）
        public virtual void StowPose(CharacterRigBase rig, Vector2 magInHand, float magTilt, out Vector2 pos, out Vector2 outPos, out float ang)
        { TakePose(rig, magInHand, magTilt, out pos, out outPos, out ang); }
        public virtual void Taken(CharacterRigBase rig) { }      // 新弹匣离开了配件
        public virtual void Stowed(CharacterRigBase rig) { }     // 旧弹匣收进了配件
        // 这一帧换弹手要不要压到身体后面：leg = 0 在枪旁边，1 正在去 / 离开配件，2 就在配件那里
        public virtual bool Behind(CharacterRigBase rig, Vector2 hand, int leg) => false;
        // 压到身后时的图层（k：0 = 手，1 = 新弹匣，2 = 旧弹匣）
        public virtual int BehindOrder(int k) => k;
        public virtual float SwitchTime(CharacterRigBase rig) => 0f;               // 普通换弹：从收旧弹匣的地方移到拿新弹匣的地方
        public virtual float NormalHold(CharacterRigBase rig) => rig.rlStowTime;   // 手枪式普通换弹：插好后枪再举多久
        public virtual float StowReach => 0.7f;    // 战术换弹带回旧弹匣：先到"袋口"占这段时间的多少（剩下的往里塞）
    }

    // 屁股后面（npc1：没有胸挂。手绕到身后拿新弹匣，空弹匣也塞回身后）
    public class HipPouchReload : ReloadStyle
    {
        public HipPouchReload() { label = "屁股后面"; }
        public Vector2 point = new Vector2(-0.21f, 0.29f);    // 手在身后拿 / 塞弹匣的位置（根节点本地坐标）
        public float angle = 40f;                              // 手转多少度
        public float behindX = 0.245f;                         // 手的 x 小于它就算绕到身后了（身体右边缘线）
        public int handOrder = 6, newMagOrder = 5, oldMagOrder = 4;   // 在身后时的图层（压在身体下面）

        public override void TakePose(CharacterRigBase rig, Vector2 magInHand, float magTilt, out Vector2 pos, out Vector2 outPos, out float ang)
        { pos = outPos = point; ang = angle; }
        public override bool Behind(CharacterRigBase rig, Vector2 hand, int leg) => leg == 2 || (leg == 1 && hand.x < behindX);
        public override int BehindOrder(int k) => k == 0 ? handOrder : k == 1 ? newMagOrder : oldMagOrder;
        public override float NormalHold(CharacterRigBase rig) => 0f;   // 插好直接回握把
        public override float StowReach => 1f;                         // 塞回身后不用"往里插"那一下
    }

    // 胸挂上的一个弹匣袋
    public class MagPouch
    {
        public SpriteRenderer mag;   // 袋里的弹匣（藏起来也照样能算位置）
        public Vector2 bottom;       // 弹匣底部在这张弹匣图里的本地坐标（单位）
        public int state = PouchFull;
    }
    public const int PouchNone = 0, PouchFull = 1, PouchEmpty = 2;   // 空袋 / 满弹匣 / 空弹匣（插回去的旧弹匣）

    // 一组弹匣袋（同一种枪的弹匣；按拿的顺序排）
    public class PouchGroup
    {
        public HoldType hold;                                 // 给哪种枪用
        public readonly List<MagPouch> pouches = new List<MagPouch>();
        public float turn;                                    // 袋里的弹匣比枪里的弹匣多斜多少度
        public float axis;                                    // 弹匣图本身斜的角度（弹匣底 → 顶，度；沿它往外拔）
        public Func<float> pullOut = () => 0.06f;             // 从袋里往外拔多远
        public int Find(int state) { for (int i = 0; i < pouches.Count; i++) if (pouches[i].state == state) return i; return -1; }
        public int Count(int state) { int n = 0; foreach (MagPouch p in pouches) if (p.state == state) n++; return n; }
    }

    // 胸挂（npc2）：新弹匣从满弹匣的袋子拔，旧弹匣插回空袋；没有空袋就只能快速换弹
    public class ChestRigReload : ReloadStyle
    {
        public ChestRigReload() { label = "胸挂"; }
        public readonly List<PouchGroup> groups = new List<PouchGroup>();
        public Func<float> switchTime = () => 0.2f;           // 普通换弹：插回旧弹匣后，手移到满弹匣那个袋子用几秒
        public Action onChanged;                              // 弹匣袋变了（角色在 Inspector 里显示用）
        PouchGroup _g; int _take = -1, _stow = -1;

        public PouchGroup GroupFor(GunRig gun)
        {
            if (gun == null) return null;
            foreach (PouchGroup g in groups) if (g.hold == gun.def.hold && g.pouches.Count > 0) return g;
            return null;
        }
        public override bool Fits(CharacterRigBase rig, GunRig gun) => base.Fits(rig, gun) && GroupFor(gun) != null;
        public override void Prepare(CharacterRigBase rig, GunRig gun)
        {
            PouchGroup g = GroupFor(gun);
            if (g != null && g.Find(PouchFull) < 0) Refill(g);              // 没有满弹匣了：测试先自动补满（游戏规则之后接）
        }
        public override bool CanStow(CharacterRigBase rig, GunRig gun) { PouchGroup g = GroupFor(gun); return g != null && g.Find(PouchNone) >= 0; }
        public override void Begin(CharacterRigBase rig, GunRig gun, bool stow)
        {
            _g = GroupFor(gun);
            _take = Mathf.Max(0, _g.Find(PouchFull));                       // 从哪个袋子拿新弹匣（按顺序第一个满的）
            _stow = stow ? _g.Find(PouchNone) : -1;                         // 旧弹匣插回哪个空袋（-1 = 丢掉）
        }
        public override void TakePose(CharacterRigBase rig, Vector2 magInHand, float magTilt, out Vector2 pos, out Vector2 outPos, out float ang)
        { Pose(rig, _take, magInHand, magTilt, out pos, out outPos, out ang); }
        public override void StowPose(CharacterRigBase rig, Vector2 magInHand, float magTilt, out Vector2 pos, out Vector2 outPos, out float ang)
        { Pose(rig, _stow >= 0 ? _stow : _take, magInHand, magTilt, out pos, out outPos, out ang); }

        // 让手里的弹匣正好和袋子里的弹匣重合（胸挂晃它也跟着）
        void Pose(CharacterRigBase rig, int i, Vector2 magInHand, float magTilt, out Vector2 pos, out Vector2 outPos, out float ang)
        {
            MagPouch p = _g.pouches[i];
            Transform m = p.mag.transform;
            Vector3 bottom = rig._rig.InverseTransformPoint(m.TransformPoint(p.bottom));   // 袋里弹匣的底部
            Vector3 d = rig._rig.InverseTransformVector(m.TransformVector(Vector3.right)); // 它现在转了多少（含胸挂晃动、身体摇摆）
            float magAng = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + _g.turn;                // 手里弹匣的图要转多少才和袋里的一样斜
            ang = magAng - magTilt;                                                        // 弹匣在手里歪着的话，手要反过来转
            pos = new Vector2(bottom.x, bottom.y) - Rot(magInHand, ang);                   // 手 = 弹匣底部 - 弹匣在手里的位置
            float r = (magAng + _g.axis) * Mathf.Deg2Rad;                                  // 沿着弹匣的方向往外拔
            outPos = pos + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * _g.pullOut();
        }

        public override void Taken(CharacterRigBase rig) { if (_g != null && _take >= 0) { _g.pouches[_take].state = PouchNone; Apply(); } }
        public override void Stowed(CharacterRigBase rig) { if (_g != null && _stow >= 0) { _g.pouches[_stow].state = PouchEmpty; Apply(); } }
        public override float SwitchTime(CharacterRigBase rig) => switchTime();

        // 补满一组 / 全部
        public void Refill(PouchGroup g) { foreach (MagPouch p in g.pouches) p.state = PouchFull; Apply(); }
        public void RefillAll() { foreach (PouchGroup g in groups) foreach (MagPouch p in g.pouches) p.state = PouchFull; Apply(); }
        // 按状态显示 / 藏起袋里的弹匣（空弹匣插回去也显示，样子和满的一样）
        public void Apply()
        {
            foreach (PouchGroup g in groups) foreach (MagPouch p in g.pouches) if (p.mag != null) p.mag.enabled = p.state != PouchNone;
            if (onChanged != null) onChanged();
        }
        // 文字（按拿的顺序）：满 / 空弹匣 / 空袋
        public string Describe(HoldType hold)
        {
            string[] names = { "空袋", "满", "空弹匣" };
            string text = "";
            foreach (PouchGroup g in groups)
                if (g.hold == hold) foreach (MagPouch p in g.pouches) text += (text.Length > 0 ? " / " : "") + names[p.state];
            return text;
        }
        public int FullCount(HoldType hold) { int n = 0; foreach (PouchGroup g in groups) if (g.hold == hold) n += g.Count(PouchFull); return n; }
    }

    // ======================= 步枪式换弹的时间 / 姿势（Inspector；原来在 npc2 上，名字没变，场景里调过的值还在）=======================
    [Header("步枪式换弹：战术（时间 = 秒；\"右手\" = 前手，从配件拿弹匣）")]
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

    [Header("步枪式换弹：快速（拔 / 送 / 插入和后半段用上面战术换弹的参数）")]
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

    [Header("步枪式换弹：普通（拔 / 送 / 插和装好后的动作用上面战术换弹的参数）")]
    public float rnReachTime = 0.2f;                          // ① 右手从护木伸过去抓住旧弹匣用几秒
    public float rnRaiseTime = 0.3f;                          // ② 拔下旧弹匣后枪口抬起来、往后收用几秒（同时右手去插回旧弹匣）
    public float rnGunUp = 80f;                               // ② 枪口往上抬多少度
    public Vector2 rnGunBack = new Vector2(-0.3f, 0.03f);     // ② 枪往后（x 负）/ 往上收多少（单位）

    // ======================= 选配件 / 换弹状态 =======================
    protected readonly List<ReloadStyle> _reloadGear = new List<ReloadStyle>();   // 身上配件提供的换弹方式（按优先顺序）
    protected readonly HipPouchReload _hipReload = new HipPouchReload();           // 什么配件都没有：从屁股后面拿
    protected GunRig _rlGun;                 // 正在换弹的枪（null = 没在换弹）
    protected ReloadStyle _rlStyle;          // 这次用哪个配件
    protected bool _rlStowing;               // 这次旧弹匣要收回去（不是丢掉）
    protected bool _rlMagTaken, _rlOldStowed;    // 新弹匣已经从配件里拿走了 / 旧弹匣已经收回去了

    public IReadOnlyList<ReloadStyle> ReloadGear => _reloadGear;   // 测试窗口：列出 / 开关这些配件
    // 换弹状态（测试窗口显示）：正在用哪种换弹、哪个配件
    public string ReloadStatus => _reloadT >= 0f && _rlGun != null
        ? "换弹中：" + KindLabel(_reloadKind) + "（" + (_rlStyle != null ? _rlStyle.label : "?") + "，" + _rlGun.def.Label + "）"
        : "没在换弹";
    public static string KindLabel(ReloadKind k) => k == ReloadKind.Tactical ? "战术" : k == ReloadKind.Speed ? "快速" : k == ReloadKind.Normal ? "普通" : "颤抖";
    // 现在拿的枪会用哪个配件换弹
    public string ReloadGearFor => _gun != null ? PickReloadStyle(_gun).label : "-";

    // 登记一个配件提供的换弹方式（先登记的优先）
    protected void AddReloadGear(ReloadStyle s) { if (s != null && !_reloadGear.Contains(s)) _reloadGear.Add(s); }

    // 这把枪现在用哪个配件换弹
    protected ReloadStyle PickReloadStyle(GunRig gun)
    {
        foreach (ReloadStyle s in _reloadGear) if (s.Fits(this, gun)) return s;
        return _hipReload;
    }

    // 子类可以改的几处
    protected virtual GunRig ReloadGun() => _gun;                                                        // 要换弹的是哪把枪
    protected virtual GunRig TimingGun() => ReloadGun();                                                 // 算换弹时间时按哪把枪（还没拿出来时也要能算）
    protected virtual Vector2 ReloadAimHand(float ang, float lowMul) => GunAimHand(ang, Vector2.zero, lowMul);   // 枪在哪（绕肩膀转）
    protected virtual void ReloadDonePose() { GunAimPose(); }                                            // 换完回到平时的样子
    protected virtual float ReloadTimeMul() => 1f;                                                       // 换弹时间倍数（武器等级）

    // 颤抖换弹只有手枪式有（步枪式按普通换弹）；持枪手抖得厉害时战术 / 普通换弹也会变成颤抖换弹
    ReloadKind Simplify(ReloadKind k, GunRig g)
    {
        bool pistol = g != null && g.def.reloadMotion == ReloadMotion.Pistol;
        if (pistol && k != ReloadKind.Speed && Grip.Fumbles) k = ReloadKind.Shaky;
        if (k == ReloadKind.Shaky && !pistol) k = ReloadKind.Normal;
        return k;
    }

    // ======================= 外部调用 =======================
    // 某种换弹一共要多少"标准时间"
    public virtual float ReloadTotal(ReloadKind kind)
    {
        GunRig g = TimingGun();
        if (g == null) return 1f;
        return GunReloadTotal(g, PickReloadStyle(g), Simplify(kind, g));
    }

    // 这个人换这种弹匣要几秒（标准时间 × 武器等级倍数；NpcCombat 用）
    public virtual float ReloadSeconds(ReloadKind kind) => ReloadTotal(kind) * ReloadTimeMul();

    // 想用 want 这种换弹，实际能用哪种：旧弹匣没地方收就只能快速换弹
    public virtual ReloadKind ResolveReloadKind(ReloadKind want)
    {
        GunRig g = ReloadGun();
        if (g == null) return want;
        ReloadStyle s = PickReloadStyle(g);
        s.Prepare(this, g);
        if (Simplify(want, g) != ReloadKind.Speed && !s.CanStow(this, g)) return ReloadKind.Speed;
        return want;
    }

    // 拿枪时换一次弹匣。fitSeconds > 0：整段动画压缩 / 拉长到正好这么多秒；0 = 按调的时间 × Reload Speed 播（测试用）
    public virtual void PlayReload(float fitSeconds, ReloadKind kind)
    {
        if (_dead || _handPoint == null) return;
        GunRig g = ReloadGun();
        if (g == null) return;
        BeginGunReload(g, fitSeconds, kind);
    }

    // 开始换弹
    protected void BeginGunReload(GunRig g, float fitSeconds, ReloadKind kind)
    {
        ReloadStyle s = PickReloadStyle(g);
        s.Prepare(this, g);
        kind = Simplify(kind, g);
        if (kind != ReloadKind.Speed && !s.CanStow(this, g)) kind = ReloadKind.Speed;   // 旧弹匣没地方收：改成快速换弹
        _rlStowing = kind != ReloadKind.Speed;
        s.Begin(this, g, _rlStowing);
        _rlGun = g; _rlStyle = s;
        SpriteRenderer hand = g.Part(g.def.reloadHand), mag = g.Part(g.def.magPart);
        if (_rlHandSprite != null && hand != null) _rlHandSprite.sprite = hand.sprite;   // 换弹手 = 枪上那只持枪手的图
        Sprite ms = mag != null ? mag.sprite : null;                                       // 两个弹匣 = 这把枪的弹匣图
        if (_rlNewMag != null) _rlNewMag.sprite = ms;
        if (_rlOldMag != null) _rlOldMag.sprite = ms;
        _reloadKind = kind;
        _qrDropped = false; _rlMagTaken = false; _rlOldStowed = false;
        float total = kind == ReloadKind.Shaky ? BuildShakyPlan() : GunReloadTotal(g, s, kind);   // 颤抖换弹：先随机好掉几次、卡几次
        _reloadRate = fitSeconds > 0f ? total / fitSeconds : Mathf.Max(0.05f, reloadSpeed) / ReloadTimeMul();
        _reloadT = 0f;                                                      // 从头开始
        _recoilT = -1f;                                                     // 停掉后坐
    }

    // 换弹结束 / 被打断：所有东西放回原样
    protected void StopGunReload()
    {
        _reloadT = -1f;
        if (_handPoint != null) _handPoint.localScale = Vector3.one;        // 放大抖动复原
        if (_rlHandSprite != null) _rlHandSprite.enabled = false;           // 换弹手、两个弹匣藏起来
        if (_rlNewMag != null) _rlNewMag.enabled = false;
        if (_rlOldMag != null) _rlOldMag.enabled = false;
        GunRig g = _rlGun;
        _rlGun = null;
        if (g == null) return;
        bool shown = GunShown(g);                                           // 枪还拿在手上：枪上的手、弹匣显示回来
        SpriteRenderer hand = g.Part(g.def.reloadHand), mag = g.Part(g.def.magPart);
        if (hand != null) hand.enabled = shown;
        if (mag != null) mag.enabled = shown;
        g.Kick(0f, PPU_FULL);                                               // 滑套 / 拉栓组复位
    }

    // 枪现在显示着没有（看换弹时不会藏起来的那只持枪手 / 其他部件）
    static bool GunShown(GunRig g)
    {
        SpriteRenderer any = null;
        foreach (WeaponPart p in g.def.parts)
        {
            if (p == null || p.name == g.def.reloadHand || p.name == g.def.magPart) continue;
            SpriteRenderer sr = g.Part(p.name);
            if (sr == null) continue;
            if (p.hand) return sr.enabled;
            if (any == null) any = sr;
        }
        return any != null && any.enabled;
    }

    // 每帧：正在换弹就播换弹动画（返回 false = 没在换弹）
    protected bool AnimateGunReload(float dt)
    {
        if (_reloadT < 0f || _rlGun == null) return false;
        GunAnimator.For(_rlGun.def).Reload(this, _rlGun, dt);               // 这把枪的动画类（默认 = 下面的通用换弹）
        return true;
    }

    // 通用换弹：手枪式 / 步枪式（颤抖换弹只有手枪式）
    public void DefaultGunReload(float dt)
    {
        if (_reloadT < 0f || _rlGun == null) return;
        if (_reloadKind == ReloadKind.Shaky) ShakyReload(dt);
        else if (_rlGun.def.reloadMotion == ReloadMotion.Rifle)
        {
            if (_reloadKind == ReloadKind.Tactical) RifleTacticalReload(dt);
            else if (_reloadKind == ReloadKind.Speed) RifleSpeedReload(dt);
            else RifleNormalReload(dt);
        }
        else PistolReload(dt);
    }

    // 某把枪 + 某个配件 + 某种换弹一共多少"标准时间"（交给这把枪的动画类）
    float GunReloadTotal(GunRig g, ReloadStyle s, ReloadKind kind) { return GunAnimator.For(g.def).ReloadTotal(this, g, s, kind); }

    // 通用换弹的时间（颤抖换弹按普通换弹算，掉弹匣 / 卡住多花的时间开始时才知道）
    public float DefaultGunReloadTotal(GunRig g, ReloadStyle s, ReloadKind kind)
    {
        if (kind == ReloadKind.Shaky) kind = ReloadKind.Normal;
        if (g.def.reloadMotion == ReloadMotion.Rifle)
        {
            if (kind == ReloadKind.Tactical) return RifleTacticalTotal();
            if (kind == ReloadKind.Speed) return RifleSpeedTotal();
            return RifleNormalTotal(s);
        }
        // 三种换弹插好新弹匣后，枪都再举一会儿（滑套复位、枪抖完）再放下
        if (kind == ReloadKind.Normal)                                      // 普通：接旧弹匣 → 收回配件 → 拿新的 → 插上 → 回握把
            return Mathf.Max(0.05f, rlLockTime + nrCatchTime + rlDropTime + rlStowTime + rlGrabTime + s.SwitchTime(this)
                                  + rlBringTime + rlAlignTime + rlInsertTime + s.NormalHold(this) + rlReturnTime);
        float drop = kind == ReloadKind.Speed ? qrDropTime : rlDropTime;   // 快速：旧弹匣丢掉；战术：接住、插好后收回去
        return Mathf.Max(0.05f, rlLockTime + rlFetchTime + rlGrabTime + rlBringTime + drop
                              + rlAlignTime + rlInsertTime + rlStowTime + rlReturnTime);
    }

    // 快速换弹：旧弹匣脱手，自己往下掉（空中时挂在角色下面，跟着人走）
    protected void DropGunMag(Sprite s, int order, Vector2 pos, float ang, Vector2 vel, float spin)
    {
        if (s == null) return;
        SpriteRenderer sr = new GameObject("DroppedMag").AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.transform.SetParent(_rig, false);
        sr.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
        sr.sortingOrder = order;                                            // 在枪和手后面，身体前面
        Destroy(sr.gameObject, qrGroundTime + 3f);                          // 保险：中途被打断也一定会删掉
        StartCoroutine(FallMag(sr, pos, ang, vel, spin, RandIn(qrGroundY)));
    }

    // 这把枪换弹时的几个量（枪的本地坐标 / 图层）
    struct GunReloadGeo
    {
        public Vector2 magIn;        // 枪里弹匣底部
        public Vector2 rest;         // 换弹手平时握在哪（手枪 = 握把，步枪前手 = 护木）
        public float restAng;        // 换弹手平时多转的角度（衣服的微调）
        public Vector2 magDir;       // 弹匣插进去的方向
        public Vector2 magInHand;    // 弹匣底部在手里的位置
        public float ready;          // 对准时还差多远插到底
        public SpriteRenderer hand, mag;    // 枪上的换弹手 / 枪里的弹匣
        public int handOrder, newOrder, oldOrder;   // 在枪旁边时的图层
    }

    GunReloadGeo Geo(GunRig g)
    {
        WeaponDef d = g.def;
        GunReloadGeo o = new GunReloadGeo();
        WeaponPart mp = d.PartDef(d.magPart), hp = d.PartDef(d.reloadHand);
        o.magIn = mp != null ? (Vector2)CanvasToLocal(mp.pivot - d.grip) : Vector2.zero;
        o.hand = g.Part(d.reloadHand); o.mag = g.Part(d.magPart);
        o.rest = o.hand != null ? (Vector2)o.hand.transform.localPosition                 // 辅助手平时握在哪（锚点 + 衣服的微调）
                                : hp != null ? (Vector2)CanvasToLocal(hp.pivot - d.grip) : Vector2.zero;
        o.restAng = o.hand != null ? o.hand.transform.localEulerAngles.z : 0f;
        o.magDir = d.magDir;
        o.magInHand = d.magInHand;
        o.ready = d.magReady;
        int top = 0;
        foreach (WeaponPart p in d.parts) if (p != null) top = Mathf.Max(top, p.order);
        o.handOrder = top + 1;                                              // 换弹手在枪的所有部件前面
        o.newOrder = mp != null ? mp.order : top;                           // 新弹匣和枪里的弹匣同一层（像插在弹匣口里）
        o.oldOrder = o.newOrder - 1;                                        // 旧弹匣再往下一层
        return o;
    }

    // 摆换弹手 + 按在不在身后定图层
    void PlaceReloadHand(GunReloadGeo geo, Vector2 hp, float ha, bool behind)
    {
        _rlHand.localPosition = new Vector3(hp.x, hp.y, 0f);
        _rlHand.localRotation = Quaternion.Euler(0f, 0f, ha);
        _rlHandSprite.sortingOrder = behind ? _rlStyle.BehindOrder(0) : geo.handOrder;
        _rlNewMag.sortingOrder = behind ? _rlStyle.BehindOrder(1) : geo.newOrder;
        _rlOldMag.sortingOrder = behind ? _rlStyle.BehindOrder(2) : geo.oldOrder;
    }

    void MarkTaken() { if (!_rlMagTaken) { _rlMagTaken = true; _rlStyle.Taken(this); } }
    void MarkStowed() { if (_rlStowing && !_rlOldStowed) { _rlOldStowed = true; _rlStyle.Stowed(this); } }

    // ============================================================================
    // 手枪式换弹（战术 / 快速 / 普通）：枪抬起、空仓挂机 → 换弹手去配件拿新弹匣 → 握把下方 → 旧弹匣掉出来
    //   → 对准、插进去 → 滑套复位 → 旧弹匣收回配件（快速换弹丢地上）→ 回握把。坐标都是角色根节点的本地坐标。
    // ============================================================================
    void PistolReload(float dt)
    {
        _reloadT += dt * _reloadRate;                                       // 往前走
        float t = _reloadT;
        GunRig g = _rlGun;
        GunReloadGeo geo = Geo(g);
        ReloadStyle st = _rlStyle;
        bool quick = _reloadKind == ReloadKind.Speed;                       // 快速换弹（换弹手不接旧弹匣，旧弹匣丢掉）
        bool normal = _reloadKind == ReloadKind.Normal;                     // 普通换弹（先收旧弹匣，再拿新弹匣）
        float dropLen = quick ? qrDropTime : rlDropTime;                    // ⑤ 的长度
        // 每一段的起点（标准时间）
        float b1 = rlLockTime, b2 = b1 + rlFetchTime, b3 = b2 + rlGrabTime, b4 = b3 + rlBringTime;
        float b5 = b4 + dropLen, b6 = b5 + rlAlignTime, b7 = b6 + rlInsertTime, b8 = b7 + rlStowTime, b9 = b8 + rlReturnTime;
        float dropAt = quick ? Mathf.Max(0f, qrDropAt) : b4;                // 旧弹匣离开枪的时刻（快速：枪口一抬就掉；战术：⑤ 开始）
        // 普通换弹的时间点：c2 换弹手到握把下方，c3 旧弹匣在手里，c4 到收弹匣的地方，c4a 收好，c4b 到拿新弹匣的地方，c5 拿出新弹匣
        float half = rlGrabTime * 0.5f;
        float c2 = b1 + nrCatchTime, c3 = c2 + rlDropTime, c4 = c3 + rlStowTime, c4a = c4 + half, c4b = c4a + st.SwitchTime(this), c5 = c4b + half;
        if (normal)                                                         // 普通换弹：后面几段的起点跟着往后挪
        {
            dropAt = c2;                                                    // 换弹手到了才弹出旧弹匣
            b5 = c5 + rlBringTime; b6 = b5 + rlAlignTime; b7 = b6 + rlInsertTime;
            b8 = b7 + st.NormalHold(this);                                  // 插好后枪再举一会儿（胸挂那套；屁股后面那套直接回握把）
            b9 = b8 + rlReturnTime;
        }
        if (t >= b9) { StopGunReload(); ReloadDonePose(); return; }        // 播完了：回到平时的样子

        // ---- 1. 枪：抬起 / 配合转 / 抖动 ----
        float raise = t < b8 ? Ease(t / Mathf.Max(0.001f, rlLockTime)) : 1f - Ease(Seg(t, b8, rlReturnTime));   // ① 抬起，⑩ 放下
        float align = t < b7 ? rlGunAlign * Ease(Seg(t, b5, rlAlignTime))                                       // ⑥ 转过去
                             : rlGunAlign * (1f - Ease(Seg(t, b7, rlReleaseTime)));                             // ⑧ 转回来
        float slideAt = b7 + Mathf.Max(0f, rlSlideDelay);                                                       // 滑套复位的时刻
        float shakeDrop = Shake(t - dropAt, rlDropTime);                                                        // 旧弹匣掉出来时：上下 + 旋转抖
        float shakeInsert = Shake(t - b7, rlReleaseTime);                                                       // ⑦ 新弹匣插到底：上下抖
        float shakeSlide = Shake(t - slideAt, rlReleaseTime);                                                   // ⑧ 滑套复位：旋转抖
        float aimNow = _aimAng * (1f - raise);                              // 瞄准角度：① 抬枪时转回正前方，⑩ 放下时转回去对准
        Vector2 gunPos = ReloadAimHand(aimNow, 1f - raise) + rlGunRaise * raise + new Vector2(0f, rlShake * (shakeDrop + shakeInsert));
        float gunAng = aimNow + rlGunTilt * raise + align + rlShakeAngle * (shakeDrop + shakeSlide);  // 抬枪时的旋转 + ⑥ 配合转 + 旋转抖
        { Vector2 tr = Grip.Offset(_time, out float trA); gunPos += tr; gunAng += trA; }   // 持枪手类型：颤抖的手换弹时也抖
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);

        // ---- 2. 滑套：① 空仓挂机，⑧ 复位 ----
        float slideK = t < slideAt ? Ease(t / Mathf.Max(0.001f, rlLockTime)) : 1f - Seg(t, slideAt, rlReleaseTime * 0.4f);   // 到复位时刻才弹回去
        g.Kick(rlSlideLock * slideK, PPU_FULL);

        // ---- 3. 换弹手的几个关键姿势（位置 + 角度） ----
        Vector2 magIn = geo.magIn, magDir = geo.magDir, magInHand = geo.magInHand;
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);   // 枪的本地坐标 → 根节点坐标
        Vector2 restPos = G2R(geo.rest); float restAng = gunAng + geo.restAng;            // 平时握在哪
        Vector2 oldInHand = normal ? magInHand : magInHand + rlOldOffset;   // 旧弹匣在手里的位置（战术：V 字；普通：正拿着）
        float oldTiltInHand = normal ? 0f : rlOldTilt;                      // 旧弹匣在手里歪多少
        st.TakePose(this, magInHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);                  // 拿新弹匣的地方
        st.StowPose(this, oldInHand, oldTiltInHand, out Vector2 stowPos, out Vector2 stowOut, out float stowAng);       // 收旧弹匣的地方
        float underAng = gunAng;                                            // ④ 握把下方：新弹匣竖直对着握把口
        Vector2 underPos = G2R(magIn - magDir * (geo.ready + rlUnderGap)) - Rot(magInHand, underAng);
        if (quick) underPos += Rot(qrWait, underAng);                       // 快速换弹：往旁边让开（默认不让）
        float tilt = quick || normal ? 0f : rlNewTilt;                      // 新弹匣在手里歪多少（只有战术换弹要摆 V 字）
        float readyAng = gunAng - tilt;                                     // ⑥⑦ 新弹匣歪着的角度抵消掉，正好对准
        float depth = t < b6 ? geo.ready : Mathf.Lerp(geo.ready, 0f, Ease(Seg(t, b6, rlInsertTime)));   // ⑦ 往里插
        Vector2 readyPos = G2R(magIn - magDir * depth) - Rot(magInHand, readyAng);
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));   // 来回走时往上拱 / 往下兜

        Vector2 hp; float ha; int leg = 0;                                  // 这一帧换弹手的位置 / 角度 / 离配件多近
        float newTilt = 0f;                                                 // 新弹匣在手里歪多少
        bool reloadHand = t >= b1;                                          // ② 开始换弹手离开枪
        bool showNew = t >= b2 && t < b7;                                   // ③ 拿到 ~ ⑦ 插进去
        float reach = st.StowReach;
        float stowDone = b7 + rlStowTime * reach;                           // 战术：旧弹匣到"袋口"的时刻（之后往里塞）
        bool showOld = !quick && t >= b4 && t < b8;                         // ⑤ 掉进手里 ~ ⑨ 收回去（快速换弹手不接）
        if (normal)                                                         // ---- 普通换弹 ----
        {
            showOld = t >= dropAt && t < c4a;                               // 旧弹匣：弹出来 ~ 收回去
            showNew = t >= c4b && t < b7;                                   // 新弹匣：拿到 ~ 插进去
            if (t < b1) { hp = restPos; ha = restAng; }                     // ① 换弹手还握着枪
            else if (t < c2)                                                // ② 移到握把下方
            {
                float e = Ease(Seg(t, b1, nrCatchTime));
                hp = Vector2.Lerp(restPos, underPos, e); ha = Mathf.LerpAngle(restAng, underAng, e);
            }
            else if (t < c3) { hp = underPos; ha = underAng; }              // ③ 旧弹匣掉进手里
            else if (t < c4)                                                // ④ 带着旧弹匣到收弹匣的地方（走弧线）
            {
                float e = Ease(Seg(t, c3, rlStowTime));
                hp = Vector2.Lerp(underPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(underAng, stowAng, e); leg = 1;
            }
            else if (t < c4a)                                               // ⑤ 把旧弹匣收进去
            {
                hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, c4, half))); ha = stowAng; leg = 2;
            }
            else if (t < c4b)                                               // ⑥ 手移到拿新弹匣的地方
            {
                float e = Ease(Seg(t, c4a, c4b - c4a));
                hp = Vector2.Lerp(stowPos, takePos, e); ha = Mathf.LerpAngle(stowAng, takeAng, e); leg = 2;
            }
            else if (t < c5)                                                // ⑦ 拿出新弹匣
            {
                hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, c4b, half))); ha = takeAng; leg = 2;
            }
            else if (t < b5)                                                // ⑧ 拿着新弹匣到握把下方
            {
                float e = Ease(Seg(t, c5, rlBringTime));
                hp = Vector2.Lerp(takeOut, underPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, underAng, e); leg = 1;
            }
            else if (t < b6)                                                // ⑨ 和枪一起转，对准握把口
            {
                float e = Ease(Seg(t, b5, rlAlignTime));
                hp = Vector2.Lerp(underPos, readyPos, e); ha = Mathf.LerpAngle(underAng, readyAng, e);
            }
            else if (t < b7) { hp = readyPos; ha = readyAng; }              // ⑩ 插进去
            else                                                            // ⑪ 换弹手回到枪上（枪还举着，滑套复位、抖完再一起放下）
            {
                float e = Ease(Seg(t, b7, rlReturnTime));
                hp = Vector2.Lerp(readyPos, restPos, e); ha = Mathf.LerpAngle(readyAng, restAng, e);
            }
        }
        else if (t < b1) { hp = restPos; ha = restAng; }
        else if (t < b2)                                                    // ② 伸到配件（走弧线）
        {
            float e = Ease(Seg(t, b1, rlFetchTime));
            hp = Vector2.Lerp(restPos, takePos, e) + Arc(e); ha = Mathf.LerpAngle(restAng, takeAng, e); leg = 1;
        }
        else if (t < b3)                                                    // ③ 拿出新弹匣（胸挂：沿弹匣方向往外拔）
        {
            hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, b2, rlGrabTime))); ha = takeAng; leg = 2;
        }
        else if (t < b4)                                                    // ④ 拿到握把下方
        {
            float e = Ease(Seg(t, b3, rlBringTime));
            hp = Vector2.Lerp(takeOut, underPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, underAng, e); leg = 1;
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
        else if (quick)                                                     // 快速：插好后换弹手直接回枪上（枪还举着，等滑套复位、抖完再放下）
        {
            float e = Ease(Seg(t, b7, rlReturnTime));
            hp = Vector2.Lerp(readyPos, restPos, e); ha = Mathf.LerpAngle(readyAng, restAng, e);
        }
        else if (t < stowDone)                                              // ⑨ 带着旧弹匣到收弹匣的地方
        {
            float e = Ease(Seg(t, b7, rlStowTime * reach));
            hp = Vector2.Lerp(readyPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(readyAng, stowAng, e); leg = 1;
        }
        else if (t < b8)                                                    // ⑨ 把旧弹匣塞进去
        {
            hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, stowDone, rlStowTime * (1f - reach)))); ha = stowAng; leg = 2;
        }
        else                                                                // ⑩ 回到枪上
        {
            float e = Ease(Seg(t, b8, rlReturnTime));
            hp = Vector2.Lerp(stowPos, restPos, e) + Arc(e); ha = Mathf.LerpAngle(stowAng, restAng, e); leg = 1;
        }

        // 配件里的弹匣：新弹匣一到手里，配件里就少一个；旧弹匣收到底，配件里就多一个空弹匣
        if (showNew) MarkTaken();
        if (_rlStowing && t >= (normal ? c4a : b8 - 0.0001f)) { MarkStowed(); showOld = false; }

        // ---- 4. 摆换弹手和两个弹匣 ----
        bool behind = st.Behind(this, hp, leg);                             // 绕到身后时压到身体下面（露在身体外面的部分照样看得到）
        if (geo.hand != null) geo.hand.enabled = !reloadHand;               // 枪上的手 ↔ 换弹手
        _rlHandSprite.enabled = reloadHand;
        PlaceReloadHand(geo, hp, ha, behind);

        _rlNewMag.enabled = showNew;                                        // 新弹匣：底部在手里
        Vector2 nb = hp + Rot(magInHand, ha);
        _rlNewMag.transform.localPosition = new Vector3(nb.x, nb.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha + newTilt);
        if (geo.mag != null) geo.mag.enabled = !(t >= dropAt && t < b7);   // 旧弹匣离开后枪里没弹匣，插好了再显示

        _rlOldMag.enabled = showOld && !_rlOldStowed;                       // 旧弹匣（收回去以后就是配件里那个了）
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
                _rlOldMag.sortingOrder = geo.oldOrder;                      // 在枪身下面，像还插在握把里
                _rlOldMag.transform.localPosition = new Vector3(op.x, op.y, 0f);
                _rlOldMag.transform.localRotation = Quaternion.Euler(0f, 0f, gunAng);
            }
            else                                                            // 整个滑出来了：交给它自己往下掉
            {
                _rlOldMag.enabled = false;
                _qrDropped = true;
                Vector2 vel = Rot(-magDir, gunAng) * v + new Vector2(RandIn(qrKick), 0f);   // 顺着握把的方向 + 往前 / 往后飘一点
                DropGunMag(_rlOldMag.sprite, geo.oldOrder, op, gunAng, vel, RandIn(qrSpin));
            }
        }
    }

    // ============================================================================
    // 步枪式换弹（三种；"换弹手" = 前手，平时握着护木）
    //   战术：① 枪往后收、枪口抬高，同时前手去配件拿新弹匣 ② 拿着新弹匣抓住旧弹匣、拔出来 ③ 新弹匣对准、插上（枪抖一下）
    //         ④ 旧弹匣收回配件，同时枪口往下到 Rt Gun Mid ⑤ 放大抖动 + 拉栓组弹回 ⑥ 枪口放平的同时前手抓住护木（枪身抖一下）
    //   快速：① 按弹匣释放、枪往下一抖（旧弹匣掉下去），前手去配件拿新弹匣 ② 枪口抬起 ③ 直接插（不停下来对准），前手抓回护木 ④ 放大抖动 → 放平
    //   普通：① 前手先抓住旧弹匣拔下来 ② 枪口抬起，同时把旧弹匣收回配件 ③ 拿新弹匣，直接插 → 后面同战术
    // ============================================================================

    // 战术换弹各段的时间点（标准时间，秒）
    void RifleTacticalTimes(out float tGrabbed, out float t1, out float t2, out float t3, out float t4, out float t5,
                            out float t6, out float t7, out float t8, out float t9, out float t10, out float t11, out float t12)
    {
        tGrabbed = rtFetchTime + rtGrabTime;                               // 新弹匣拿出来了
        t1 = Mathf.Max(rtRaiseTime, tGrabbed);                             // 枪抬好 + 新弹匣在手里
        t2 = t1 + rtBringTime;                                             // 前手抓住旧弹匣
        t3 = t2 + rtPullOldTime;                                           // 旧弹匣拔出来
        t4 = t3 + rtAlignTime;                                             // 新弹匣对准
        t5 = t4 + rtInsertTime;                                            // 新弹匣插到底
        t6 = t5 + rtStowTime;                                              // 旧弹匣到配件（枪口同时往下）
        t7 = t6 + rtPushTime;                                              // 旧弹匣收进去 → 放大抖动 + 拉栓组弹回
        t8 = t7 + rtZoomTime;                                              // 放大抖动结束
        t9 = t8 + rtLevelTime;                                             // 枪口放平
        t10 = t7;                                                          // 收好旧弹匣就开始往回走（不停在配件那里等）
        t11 = t9;                                                          // 枪口放平的同时前手抓住护木（枪身抖一下）
        t12 = t11 + rtGripShakeTime;                                       // 结束
    }

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

    // 0 → 1 → 0 的一个鼓包：在 [a, a + len] 里，中间最大
    static float Bump(float t, float a, float len) { return len > 0f && t > a && t < a + len ? Mathf.Sin(Mathf.PI * (t - a) / len) : 0f; }

    void RifleTacticalReload(float dt)
    {
        _reloadT += dt * _reloadRate;                                      // 往前走
        float t = _reloadT;
        RifleTacticalTimes(out float tg, out float t1, out float t2, out float t3, out float t4, out float t5,
                           out float t6, out float t7, out float t8, out float t9, out float t10, out float t11, out float t12);
        if (t >= t12) { StopGunReload(); ReloadDonePose(); return; }      // 播完了：回到平时的样子
        GunRig g = _rlGun;
        GunReloadGeo geo = Geo(g);
        ReloadStyle st = _rlStyle;

        // ---- 1. 枪：后收 + 抬高 → 降到 Rt Gun Mid → 放大抖动 → 放平；抖动 ----
        float up = rtGunUp * Ease(Seg(t, 0f, rtRaiseTime));               // ① 抬起来
        if (t >= t5) up = Mathf.Lerp(rtGunUp, rtGunMid, Ease(Seg(t, t5, t7 - t5)));   // ④ 收旧弹匣的同时往下
        if (t >= t8) up = Mathf.Lerp(rtGunMid, 0f, Ease(Seg(t, t8, rtLevelTime)));    // ⑥ 放平
        float back = t < t8 ? Ease(Seg(t, 0f, rtRaiseTime)) : 1f - Ease(Seg(t, t8, rtLevelTime));   // 往后收的程度（放平时收回来）
        float aimNow = _aimAng * (1f - back);                              // 瞄准角度：抬枪时转回正前方，放平时转回去对准
        float shIns = Shake(t - t5, rlReleaseTime);                        // ③ 新弹匣插到底：抖一下
        float shZoom = Shake(t - t7, rtZoomTime);                          // ⑤ 放大抖动时也带一点旋转
        float shGrip = Shake(t - t11, rtGripShakeTime);                    // ⑥ 前手抓住护木：枪身抖一下
        Vector2 gunPos = ReloadAimHand(aimNow, 1f - back) + rtGunBack * back
                       + new Vector2(0f, rlShake * 0.5f * shIns + rtGripShakeY * shGrip);
        float gunAng = aimNow + up + rtInsertShake * shIns + rtZoomShake * shZoom + rtGripShake * shGrip;
        { Vector2 tr = Grip.Offset(_time, out float trA); gunPos += tr; gunAng += trA; }   // 持枪手类型：颤抖的手换弹时也抖
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
        _handPoint.localScale = Vector3.one * (1f + rtZoom * ZoomPop(t - t7));   // ⑤ 突然放大一下（模拟往镜头方向抖）

        // ---- 2. 拉栓组：一开始挂到后面，⑤ 放大抖动的同时弹回去 ----
        float lockK = t < t7 ? Ease(Seg(t, 0f, rtRaiseTime)) : 1f - Seg(t, t7, rtBoltReturnTime);
        g.Kick(rlSlideLock * lockK, PPU_FULL);

        // ---- 3. 前手的几个关键姿势 ----
        Vector2 magIn = geo.magIn, magDir = geo.magDir, magInHand = geo.magInHand;
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);  // 枪的本地坐标 → 根节点坐标
        Vector2 restPos = G2R(geo.rest); float restAng = gunAng + geo.restAng;           // 前手平时握着护木
        Vector2 oldInHand = magInHand + rtOldOffset;                       // 旧弹匣在手里的位置（和新弹匣并排）
        st.TakePose(this, magInHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);           // 拿新弹匣的地方
        st.StowPose(this, oldInHand, rtOldTilt, out Vector2 stowPos, out Vector2 stowOut, out float stowAng);    // 收旧弹匣的地方
        float grabAng = gunAng - rtOldTilt;                                // ② 抓旧弹匣：手里"旧弹匣的位置"正好对上枪里的弹匣
        Vector2 grabPos = G2R(magIn) - Rot(oldInHand, grabAng);
        Vector2 pulledPos = grabPos + Rot(-magDir, gunAng) * rtPullOldDist;   // 沿弹匣方向拔出来
        float readyAng = gunAng - rtNewTilt;                               // ③ 新弹匣歪着的角度抵消掉，正好对准
        float depth = t < t4 ? geo.ready : Mathf.Lerp(geo.ready, 0f, Ease(Seg(t, t4, rtInsertTime)));
        Vector2 readyPos = G2R(magIn - magDir * depth) - Rot(magInHand, readyAng);
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));

        Vector2 hp; float ha; float newTilt = 0f; int leg = 0;
        if (t < rtFetchTime)                                               // ① 伸到配件
        {
            float e = Ease(Seg(t, 0f, rtFetchTime));
            hp = Vector2.Lerp(restPos, takePos, e) + Arc(e); ha = Mathf.LerpAngle(restAng, takeAng, e); leg = 1;
        }
        else if (t < tg) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, rtFetchTime, rtGrabTime))); ha = takeAng; leg = 2; }   // ① 拿出新弹匣
        else if (t < t1) { hp = takeOut; ha = takeAng; leg = 2; }          // 等枪抬好
        else if (t < t2)                                                   // ② 拿着新弹匣去抓旧弹匣（新弹匣慢慢歪到并排的角度）
        {
            float e = Ease(Seg(t, t1, rtBringTime));
            hp = Vector2.Lerp(takeOut, grabPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, grabAng, e); leg = 1;
            newTilt = rtNewTilt * e;
        }
        else if (t < t3) { hp = Vector2.Lerp(grabPos, pulledPos, Ease(Seg(t, t2, rtPullOldTime))); ha = grabAng; newTilt = rtNewTilt; }   // ② 拔出旧弹匣
        else if (t < t4)                                                   // ③ 新弹匣对准弹匣口
        {
            float e = Ease(Seg(t, t3, rtAlignTime));
            hp = Vector2.Lerp(pulledPos, readyPos, e); ha = Mathf.LerpAngle(grabAng, readyAng, e); newTilt = rtNewTilt;
        }
        else if (t < t5) { hp = readyPos; ha = readyAng; newTilt = rtNewTilt; }   // ③ 插进去
        else if (t < t6)                                                   // ④ 带着旧弹匣到配件
        {
            float e = Ease(Seg(t, t5, rtStowTime));
            hp = Vector2.Lerp(readyPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(readyAng, stowAng, e); leg = 1;
        }
        else if (t < t7) { hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, t6, rtPushTime))); ha = stowAng; leg = 2; }   // ④ 收进去
        else                                                               // ⑤⑥ 前手往回走，枪放平时正好抓住护木
        {
            float e = Ease(Seg(t, t10, t11 - t10));
            hp = Vector2.Lerp(stowPos, restPos, e) + Arc(e); ha = Mathf.LerpAngle(stowAng, restAng, e); leg = 1;
        }

        // 配件：新弹匣一到手里配件就少一个；旧弹匣收到底，配件里多一个空弹匣
        bool showNew = t >= rtFetchTime && t < t5;
        if (showNew) MarkTaken();
        if (t >= t7) MarkStowed();

        // ---- 4. 摆手和两个弹匣 ----
        bool regripped = t >= t11;                                         // 前手抓回护木：换回枪上的那只手
        if (geo.hand != null) geo.hand.enabled = regripped;
        _rlHandSprite.enabled = !regripped;
        PlaceReloadHand(geo, hp, ha, st.Behind(this, hp, leg));
        _rlNewMag.enabled = showNew;                                       // 新弹匣：底部在手里
        Vector2 nb = hp + Rot(magInHand, ha);
        _rlNewMag.transform.localPosition = new Vector3(nb.x, nb.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha + newTilt);
        if (geo.mag != null) geo.mag.enabled = !(t >= t2 && t < t5);       // 旧弹匣被拔走后枪里没弹匣，插好了再显示
        bool showOld = t >= t2 && t < t7 && !_rlOldStowed;                 // 旧弹匣：被抓住 ~ 收回去
        _rlOldMag.enabled = showOld;
        if (showOld)
        {
            Vector2 ob = hp + Rot(oldInHand, ha);
            _rlOldMag.transform.localPosition = new Vector3(ob.x, ob.y, 0f);
            _rlOldMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha + rtOldTilt);
        }
    }

    // 快速换弹各段的时间点（标准时间，秒）
    void RifleSpeedTimes(out float tDrop, out float tGrabbed, out float t1, out float t2, out float t3, out float tGrip,
                         out float t5, out float t6, out float t7, out float t8)
    {
        tDrop = rsFlickTime * 0.7f;                                        // 整把枪沉到最低点：旧弹匣脱手
        tGrabbed = rtFetchTime + rtGrabTime;                               // 新弹匣拿出来了
        t1 = Mathf.Max(rsFlickTime + rsRaiseTime, tGrabbed);               // 枪抬好 + 新弹匣在手里
        t2 = t1 + rtBringTime;                                             // 新弹匣送到弹匣口
        t3 = t2 + rtInsertTime;                                            // 插到底（枪上下抖一下）
        tGrip = t3 + rsRegripTime;                                         // 前手抓回护木
        t5 = t3 + rsSettleTime;                                            // 放大抖动 + 拉栓组弹回
        t6 = t5 + rtZoomTime;                                              // 放大抖动结束，开始放平
        t7 = t6 + rtLevelTime;                                             // 枪口放平（枪身抖一下）
        t8 = Mathf.Max(t7 + rtGripShakeTime, tGrip);                       // 结束
    }

    float RifleSpeedTotal()
    {
        RifleSpeedTimes(out _, out _, out _, out _, out _, out _, out _, out _, out _, out float t8);
        return Mathf.Max(0.05f, t8);
    }

    void RifleSpeedReload(float dt)
    {
        _reloadT += dt * _reloadRate;                                      // 往前走
        float t = _reloadT;
        RifleSpeedTimes(out float tDrop, out float tg, out float t1, out float t2, out float t3, out float tGrip,
                        out float t5, out float t6, out float t7, out float t8);
        if (t >= t8) { StopGunReload(); ReloadDonePose(); return; }       // 播完了：回到平时的样子
        GunRig g = _rlGun;
        GunReloadGeo geo = Geo(g);
        ReloadStyle st = _rlStyle;

        // ---- 1. 枪：枪托往下 → 整把枪往下沉 → 抬起 + 往后收 → 放大抖动 → 放平；抖动 ----
        float stockDown = Bump(t, 0f, rsFlickTime * 0.6f);                 // ① 先：枪托往下（绕握把转，枪口往上翘）
        float sink = Bump(t, rsFlickTime * 0.3f, rsFlickTime * 0.7f);      // ① 后：整把枪往下一沉
        float raiseK = Ease(Seg(t, rsFlickTime, rsRaiseTime));             // ② 抬起来的程度
        float back = t < t6 ? raiseK : 1f - Ease(Seg(t, t6, rtLevelTime));  // 往后收的程度（放平时收回来）
        float up = rsGunUp * back;                                         // 枪口往上抬（放平时一起放下）
        float aimNow = _aimAng * (1f - back);                              // 瞄准角度：抬枪时转回正前方，放平时转回去对准
        float shIns = Shake(t - t3, rsInsertShakeTime);                    // ③ 插到底：上下抖一下（力量感）
        float shZoom = Shake(t - t5, rtZoomTime);                          // ④ 放大抖动时也带一点旋转
        float shLevel = Shake(t - t7, rtGripShakeTime);                    // ④ 放平的一下：枪身抖一下
        Vector2 gunPos = ReloadAimHand(aimNow, 1f - back) + rsGunBack * back
                       + new Vector2(0f, -rsFlickY * sink + rsInsertShakeY * shIns + rtGripShakeY * shLevel);
        float gunAng = aimNow + up + rsFlickAngle * stockDown + rtZoomShake * shZoom + rtGripShake * shLevel;
        { Vector2 tr = Grip.Offset(_time, out float trA); gunPos += tr; gunAng += trA; }   // 持枪手类型：颤抖的手换弹时也抖
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
        _handPoint.localScale = Vector3.one * (1f + rtZoom * ZoomPop(t - t5));   // ④ 突然放大一下（模拟往镜头方向抖）

        // ---- 2. 拉栓组：打空了挂在后面，放大抖动的同时弹回去 ----
        float lockK = t < t5 ? Ease(Seg(t, 0f, rsFlickTime)) : 1f - Seg(t, t5, rtBoltReturnTime);
        g.Kick(rlSlideLock * lockK, PPU_FULL);

        // ---- 3. 旧弹匣：整把枪沉到最低点时脱手，自己掉下去（下落参数用"换弹 2：快速换弹"那几个）----
        Vector2 magIn = geo.magIn, magDir = geo.magDir, magInHand = geo.magInHand;
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);  // 枪的本地坐标 → 根节点坐标
        if (t >= tDrop && !_qrDropped)
        {
            _qrDropped = true;
            Vector2 vel = Rot(-magDir, gunAng) * qrSlideSpeed + new Vector2(RandIn(qrKick), -rsFlickY * 6f);   // 顺着弹匣口往下 + 被抖下去的速度
            DropGunMag(_rlOldMag.sprite, geo.oldOrder, G2R(magIn), gunAng, vel, RandIn(qrSpin) * 0.5f);   // 步枪弹匣重，转得慢一点
        }

        // ---- 4. 前手：拿新弹匣 → 直接插进弹匣口 → 抓回护木 ----
        Vector2 restPos = G2R(geo.rest); float restAng = gunAng + geo.restAng;           // 前手平时握着护木
        st.TakePose(this, magInHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);   // 拿新弹匣的地方
        float readyAng = gunAng;                                           // 新弹匣正对弹匣口
        float depth = t < t2 ? geo.ready : Mathf.Lerp(geo.ready, 0f, Ease(Seg(t, t2, rtInsertTime)));
        Vector2 readyPos = G2R(magIn - magDir * depth) - Rot(magInHand, readyAng);   // 弹匣口（往里插）
        Vector2 seatedPos = G2R(magIn) - Rot(magInHand, readyAng);         // 插到底时手在哪
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));

        Vector2 hp; float ha; int leg = 0;
        if (t < rtFetchTime)                                               // ① 伸到配件（和往下抖同时）
        {
            float e = Ease(Seg(t, 0f, rtFetchTime));
            hp = Vector2.Lerp(restPos, takePos, e) + Arc(e); ha = Mathf.LerpAngle(restAng, takeAng, e); leg = 1;
        }
        else if (t < tg) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, rtFetchTime, rtGrabTime))); ha = takeAng; leg = 2; }   // ① 拿出新弹匣
        else if (t < t1) { hp = takeOut; ha = takeAng; leg = 2; }          // 等枪抬好
        else if (t < t2)                                                   // ③ 拿着弹匣直接送到弹匣口
        {
            float e = Ease(Seg(t, t1, rtBringTime));
            hp = Vector2.Lerp(takeOut, readyPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, readyAng, e); leg = 1;
        }
        else if (t < t3) { hp = readyPos; ha = readyAng; }                 // ③ 插进去
        else                                                               // ③ 插完直接抓回护木
        {
            float e = Ease(Seg(t, t3, rsRegripTime));
            hp = Vector2.Lerp(seatedPos, restPos, e); ha = Mathf.LerpAngle(readyAng, restAng, e);
        }

        // 配件：新弹匣一到手里配件就少一个（旧弹匣丢在地上，不收回去）
        bool showNew = t >= rtFetchTime && t < t3;
        if (showNew) MarkTaken();

        // ---- 5. 摆手和弹匣 ----
        bool regripped = t >= tGrip;                                       // 前手抓回护木：换回枪上的那只手
        if (geo.hand != null) geo.hand.enabled = regripped;
        _rlHandSprite.enabled = !regripped;
        PlaceReloadHand(geo, hp, ha, st.Behind(this, hp, leg));
        _rlNewMag.enabled = showNew;                                       // 新弹匣：底部在手里
        Vector2 nb = hp + Rot(magInHand, ha);
        _rlNewMag.transform.localPosition = new Vector3(nb.x, nb.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha);
        if (geo.mag != null) geo.mag.enabled = !(t >= tDrop && t < t3);    // 旧弹匣掉了以后枪里没弹匣，插好了再显示
        _rlOldMag.enabled = false;                                         // 旧弹匣不在手里（掉地上了）
    }

    // 普通换弹各段的时间点（标准时间，秒；s = 这次用的配件）
    void RifleNormalTimes(ReloadStyle s, out float tA, out float tB, out float t1, out float t2, out float t3, out float t4,
                          out float t5, out float t6, out float t7, out float t8, out float t9, out float t10)
    {
        tA = rnReachTime;                                                  // 前手抓住旧弹匣
        tB = tA + rtPullOldTime;                                           // 旧弹匣拔下来
        t1 = tB + Mathf.Max(rnRaiseTime, rtStowTime);                      // 枪抬好 + 旧弹匣到配件
        t2 = t1 + rtPushTime;                                              // 旧弹匣收进去
        t3 = t2 + s.SwitchTime(this);                                      // 手移到拿新弹匣的地方
        t4 = t3 + rtGrabTime;                                              // 拿出新弹匣
        t5 = t4 + rtBringTime;                                             // 送到弹匣口
        t6 = t5 + rtInsertTime;                                            // 插到底（枪抖一下）
        t7 = t6 + rtStowTime + rtPushTime;                                 // 枪口降到 Rt Gun Mid（和战术换弹"收旧弹匣"那段一样长）→ 放大抖动 + 拉栓组弹回
        t8 = t7 + rtZoomTime;                                              // 开始放平
        t9 = t8 + rtLevelTime;                                             // 枪口放平，同时前手抓住护木（枪身抖一下）
        t10 = t9 + rtGripShakeTime;                                        // 结束
    }

    float RifleNormalTotal(ReloadStyle s)
    {
        RifleNormalTimes(s, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out float t10);
        return Mathf.Max(0.05f, t10);
    }

    void RifleNormalReload(float dt)
    {
        _reloadT += dt * _reloadRate;                                      // 往前走
        float t = _reloadT;
        ReloadStyle st = _rlStyle;
        RifleNormalTimes(st, out float tA, out float tB, out float t1, out float t2, out float t3, out float t4,
                         out float t5, out float t6, out float t7, out float t8, out float t9, out float t10);
        if (t >= t10) { StopGunReload(); ReloadDonePose(); return; }      // 播完了：回到平时的样子
        GunRig g = _rlGun;
        GunReloadGeo geo = Geo(g);

        // ---- 1. 枪：拔下旧弹匣后抬高 + 往后收 → 装好后降到 Rt Gun Mid → 放大抖动 → 放平；抖动（装好以后同战术换弹）----
        float raiseK = Ease(Seg(t, tB, rnRaiseTime));                      // ② 抬起来的程度
        float up = rnGunUp * raiseK;                                       // ② 抬高
        if (t >= t6) up = Mathf.Lerp(rnGunUp, rtGunMid, Ease(Seg(t, t6, t7 - t6)));   // 装好后往下
        if (t >= t8) up = Mathf.Lerp(rtGunMid, 0f, Ease(Seg(t, t8, rtLevelTime)));    // 放平
        float back = t < t8 ? raiseK : 1f - Ease(Seg(t, t8, rtLevelTime)); // 往后收的程度（放平时收回来）
        float aimNow = _aimAng * (1f - back);                              // 瞄准角度：抬枪时转回正前方，放平时转回去对准
        float shIns = Shake(t - t6, rlReleaseTime);                        // 插到底：枪抖一下（同战术换弹）
        float shZoom = Shake(t - t7, rtZoomTime);                          // 放大抖动时也带一点旋转
        float shGrip = Shake(t - t9, rtGripShakeTime);                     // 前手抓住护木：枪身抖一下
        Vector2 gunPos = ReloadAimHand(aimNow, 1f - back) + rnGunBack * back
                       + new Vector2(0f, rlShake * 0.5f * shIns + rtGripShakeY * shGrip);
        float gunAng = aimNow + up + rtInsertShake * shIns + rtZoomShake * shZoom + rtGripShake * shGrip;
        { Vector2 tr = Grip.Offset(_time, out float trA); gunPos += tr; gunAng += trA; }   // 持枪手类型：颤抖的手换弹时也抖
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
        _handPoint.localScale = Vector3.one * (1f + rtZoom * ZoomPop(t - t7));   // 突然放大一下（模拟往镜头方向抖）

        // ---- 2. 拉栓组：打空了挂在后面，放大抖动的同时弹回去 ----
        float lockK = t < t7 ? Ease(Seg(t, 0f, rnReachTime)) : 1f - Seg(t, t7, rtBoltReturnTime);
        g.Kick(rlSlideLock * lockK, PPU_FULL);

        // ---- 3. 前手的几个关键姿势 ----
        Vector2 magIn = geo.magIn, magDir = geo.magDir, magInHand = geo.magInHand;
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);  // 枪的本地坐标 → 根节点坐标
        Vector2 restPos = G2R(geo.rest); float restAng = gunAng + geo.restAng;           // 前手平时握着护木
        st.TakePose(this, magInHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);         // 拿新弹匣的地方
        st.StowPose(this, magInHand, 0f, out Vector2 stowPos, out Vector2 stowOut, out float stowAng);         // 收旧弹匣的地方（旧弹匣正拿着）
        float grabAng = gunAng;                                            // ① 抓旧弹匣：手里弹匣的位置正好对上枪里的弹匣
        Vector2 grabPos = G2R(magIn) - Rot(magInHand, grabAng);
        Vector2 pulledPos = grabPos + Rot(-magDir, gunAng) * rtPullOldDist;   // 沿弹匣方向拔下来
        float readyAng = gunAng;                                           // 新弹匣正对弹匣口
        float depth = t < t5 ? geo.ready : Mathf.Lerp(geo.ready, 0f, Ease(Seg(t, t5, rtInsertTime)));
        Vector2 readyPos = G2R(magIn - magDir * depth) - Rot(magInHand, readyAng);
        Vector2 seatedPos = G2R(magIn) - Rot(magInHand, readyAng);         // 插到底时手在哪
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));

        Vector2 hp; float ha; int leg = 0;
        if (t < tA)                                                        // ① 伸手去抓旧弹匣
        {
            float e = Ease(Seg(t, 0f, rnReachTime));
            hp = Vector2.Lerp(restPos, grabPos, e); ha = Mathf.LerpAngle(restAng, grabAng, e);
        }
        else if (t < tB) { hp = Vector2.Lerp(grabPos, pulledPos, Ease(Seg(t, tA, rtPullOldTime))); ha = grabAng; }   // ① 拔下来
        else if (t < t1)                                                   // ② 带到配件（枪同时抬起来）
        {
            float e = Ease(Seg(t, tB, t1 - tB));
            hp = Vector2.Lerp(pulledPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(grabAng, stowAng, e); leg = 1;
        }
        else if (t < t2) { hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, t1, rtPushTime))); ha = stowAng; leg = 2; }   // ② 收进去
        else if (t < t3)                                                   // ③ 手移到拿新弹匣的地方
        {
            float e = Ease(Seg(t, t2, t3 - t2));
            hp = Vector2.Lerp(stowPos, takePos, e); ha = Mathf.LerpAngle(stowAng, takeAng, e); leg = 2;
        }
        else if (t < t4) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, t3, rtGrabTime))); ha = takeAng; leg = 2; }   // ③ 拿出新弹匣
        else if (t < t5)                                                   // ③ 直接送到弹匣口
        {
            float e = Ease(Seg(t, t4, rtBringTime));
            hp = Vector2.Lerp(takeOut, readyPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, readyAng, e); leg = 1;
        }
        else if (t < t6) { hp = readyPos; ha = readyAng; }                 // ③ 插进去
        else                                                               // 往回走，枪放平的那一刻正好抓住护木（同战术换弹）
        {
            float e = Ease(Seg(t, t6, t9 - t6));
            hp = Vector2.Lerp(seatedPos, restPos, e) + Arc(e); ha = Mathf.LerpAngle(readyAng, restAng, e);
        }

        // 配件：旧弹匣收到底，配件里多一个空弹匣；新弹匣一到手里，配件里就少一个
        if (t >= t2) MarkStowed();
        bool showNew = t >= t3 && t < t6;
        if (showNew) MarkTaken();

        // ---- 4. 摆手和两个弹匣 ----
        bool regripped = t >= t9;                                          // 前手抓回护木：换回枪上的那只手
        if (geo.hand != null) geo.hand.enabled = regripped;
        _rlHandSprite.enabled = !regripped;
        PlaceReloadHand(geo, hp, ha, st.Behind(this, hp, leg));
        Vector2 inHand = hp + Rot(magInHand, ha);                          // 手里弹匣的底部
        bool showOld = t >= tA && t < t2 && !_rlOldStowed;                 // 旧弹匣：被抓住 ~ 收回去
        _rlOldMag.enabled = showOld;
        _rlOldMag.transform.localPosition = new Vector3(inHand.x, inHand.y, 0f);
        _rlOldMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha);
        _rlNewMag.enabled = showNew;
        _rlNewMag.transform.localPosition = new Vector3(inHand.x, inHand.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha);
        if (geo.mag != null) geo.mag.enabled = !(t >= tA && t < t6);       // 旧弹匣被拔走后枪里没弹匣，插好了再显示
    }
}
