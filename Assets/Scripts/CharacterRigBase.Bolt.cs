using UnityEngine;

// ============================================================================
// 拉栓枪（WeaponDef.boltAction）：每开一枪手动拉一次栓（CharacterRigBase 的一部分，所有角色共用）
//   开完枪先等后坐过去 → 持枪手（左手）离开握把，抓住拉栓（拉机柄的球头）→ 往上抬（拉栓绕根部转起来）
//   → 往后拉到底（枪栓 + 拉栓一起往后，到"枪栓最远位置"）→ 往前推回去 → 往下压锁住 → 手回到握把。
//   抬、拉到底、压下去这三下枪都轻轻抖一下（金属咔哒的力量感）。快速换弹（打空了）插好新弹匣后也拉一次。
//   枪的数据：boltPart（枪栓）、boltHandle（拉栓）、boltTravel（往后拉多远，原图像素）、boltLift（抬多少度）、boltGrab（手抓哪）
// ============================================================================
public abstract partial class CharacterRigBase
{
    [Header("拉栓枪：开完一枪手动拉栓（时间 = 秒）")]
    public float boDelay = 0.15f;                              // 开枪后等多久再拉（后坐先过去）
    public float boReachTime = 0.14f;                          // ① 手从握把伸到拉栓
    public float boLiftTime = 0.07f;                           // ② 拉栓往上抬
    public float boPullTime = 0.11f;                           // ③ 往后拉到底
    public float boPushTime = 0.1f;                            // ④ 往前推回去
    public float boLockTime = 0.07f;                           // ⑤ 往下压锁住
    public float boReturnTime = 0.15f;                         // ⑥ 手回到握把
    public float boHandAngle = -15f;                           // 抓拉栓时手转多少度（相对握枪时）
    public Vector2 boHandOffset = new Vector2(0f, 0f);         // 抓拉栓时手心相对拉栓球头的位置（原图像素，y 往下为正）
    public float boShake = 4f;                                 // 抬 / 拉到底 / 压下时枪抖多少度
    public float boShakeX = 0.015f;                            // 拉到底那一下枪往后顿多远（单位）
    public float boPullRise = 6f;                              // 往后拉的时候枪口跟着往上扬多少度（手往后一扯，枪也被带着往后）
    public float boPullBack = 0.025f;                          // 往后拉的时候枪跟着往后挪多远（单位）

    protected float _boltT = -1f;                              // 拉栓进行到哪（秒；-1 = 没在拉）
    GunRig _boltGun;                                           // 正在拉栓的那把枪
    Transform _boltHand; SpriteRenderer _boltHandSr;           // 拉栓的那只手（挂在枪上，跟着枪动）
    SpriteRenderer _boltHidHand;                               // 拉栓时藏起来的枪上持枪手

    public bool IsBolting => _boltT >= 0f;

    // 开始拉一次栓（开完枪 / 快速换弹插好新弹匣后）
    protected void StartBolt()
    {
        if (_gun == null || !_gun.def.boltAction) return;
        StopBolt();
        _boltGun = _gun;
        _boltT = 0f;
        WeaponDef d = _gun.def;
        _boltHidHand = null;
        foreach (WeaponPart p in d.parts)                                   // 枪上的持枪手（握把那只，不是辅助手）
            if (p != null && p.hand && p.name != d.reloadHand) { _boltHidHand = _gun.Part(p.name); break; }
        if (_boltHand == null)
        {
            _boltHand = new GameObject("BoltHand").transform;
            _boltHandSr = _boltHand.gameObject.AddComponent<SpriteRenderer>();
        }
        _boltHand.SetParent(_gun.root, false);                              // 挂在枪上：跟着瞄准 / 后坐一起动
        Sprite art = GripHandArt(false, d.hold);
        _boltHandSr.sprite = art != null ? art : (_boltHidHand != null ? _boltHidHand.sprite : null);
        if (_boltHidHand != null) { _boltHandSr.sortingOrder = _boltHidHand.sortingOrder; _boltHandSr.sharedMaterial = _boltHidHand.sharedMaterial; }
        _boltHandSr.enabled = false;
    }

    // 停掉（换枪 / 换弹 / 取出 / 倒下）：枪栓、拉栓复位，手放回握把
    protected void StopBolt()
    {
        GunRig g = _boltGun;
        _boltT = -1f;
        _boltGun = null;
        if (_boltHandSr != null) _boltHandSr.enabled = false;
        if (g == null || g.root == null) return;
        PlaceBolt(g, 0f, 0f);
        if (_boltHidHand != null) _boltHidHand.enabled = GunShown(g);       // 枪还拿在手上：握把上的手显示回来
        _boltHidHand = null;
    }

    void BoltTimes(out float a, out float b, out float c, out float d, out float e, out float f, out float end)
    {
        a = boDelay; b = a + boReachTime; c = b + boLiftTime; d = c + boPullTime; e = d + boPushTime; f = e + boLockTime; end = f + boReturnTime;
    }

    // 每帧（持枪 + 后坐摆好之后）：摆拉栓的手、枪栓、拉栓
    protected void AnimateBolt(float dt)
    {
        if (_boltT < 0f) return;
        GunRig g = _boltGun;
        if (g == null || g != _gun || g.root == null) { StopBolt(); return; }
        _boltT += dt;
        BoltTimes(out float a, out float b, out float c, out float d, out float e, out float f, out float end);
        float t = _boltT;
        if (t >= end) { StopBolt(); return; }
        WeaponDef w = g.def;

        // 拉栓抬起多少（0~1）/ 往后拉了多少（0~1）/ 枪跟着抖多少
        BoltCycle(t - b, out float lift, out float pull, out float sh, out float back);
        PlaceBolt(g, lift, pull);

        // 手：握把 → 拉栓球头（跟着拉栓走）→ 回握把
        Vector3 gripPos = _boltHidHand != null ? _boltHidHand.transform.localPosition : Vector3.zero;
        float gripAng = _boltHidHand != null ? _boltHidHand.transform.localEulerAngles.z : 0f;
        Vector3 knob = KnobLocal(g, lift, pull) + new Vector3(boHandOffset.x / PPU_FULL, -boHandOffset.y / PPU_FULL, 0f);
        float knobAng = gripAng + boHandAngle;
        Vector3 hp; float ha;
        if (t < a) { hp = gripPos; ha = gripAng; }
        else if (t < b) { float u = Ease(Seg(t, a, boReachTime)); hp = Vector3.Lerp(gripPos, KnobLocal(g, 0f, 0f), u); ha = Mathf.LerpAngle(gripAng, knobAng, u); }
        else if (t < f) { hp = knob; ha = knobAng; }
        else { float u = Ease(Seg(t, f, boReturnTime)); hp = Vector3.Lerp(knob, gripPos, u); ha = Mathf.LerpAngle(knobAng, gripAng, u); }
        bool out_ = t >= a;
        _boltHand.localPosition = hp;
        _boltHand.localRotation = Quaternion.Euler(0f, 0f, ha);
        _boltHandSr.enabled = out_;
        if (_boltHidHand != null) _boltHidHand.enabled = !out_;

        // 抬起 / 拉到底 / 压下去：枪抖一下；往后拉时枪被带着往后、枪口上扬
        if (_handPoint != null)
        {
            _handPoint.localRotation = _handPoint.localRotation * Quaternion.Euler(0f, 0f, sh);
            _handPoint.localPosition += _handPoint.localRotation * new Vector3(-back, 0f, 0f);
        }
    }

    // 拉栓一个来回（tau = 从开始抬起算的时间；换弹最后拉栓也用它）：
    //   lift = 抬起 0~1，pull = 往后 0~1，shakeAng = 枪多转多少度（正 = 枪口往上），shakeBack = 枪往后挪多远
    protected void BoltCycle(float tau, out float lift, out float pull, out float shakeAng, out float shakeBack)
    {
        float c = boLiftTime, d = c + boPullTime, e = d + boPushTime, f = e + boLockTime;
        lift = tau < 0f ? 0f : tau < c ? Ease(tau / boLiftTime) : tau < e ? 1f : tau < f ? 1f - Ease((tau - e) / boLockTime) : 0f;
        pull = tau < c ? 0f : tau < d ? Ease((tau - c) / boPullTime) : tau < e ? 1f - Ease((tau - d) / boPushTime) : 0f;
        float drag = tau >= c && tau < e ? pull : 0f;                       // 往后拉 / 推回去的时候，枪被手带着
        shakeAng = boPullRise * drag + boShake * (Shake(tau, 0.15f) + Shake(tau - d, 0.2f) - Shake(tau - f, 0.15f));   // 抬起 / 拉到底：枪口往上抖；压下：往下抖
        shakeBack = boPullBack * drag + boShakeX * Shake(tau - d, 0.18f);
    }
    protected float BoltCycleLength => boLiftTime + boPullTime + boPushTime + boLockTime;

    // 枪栓 / 拉栓摆到 lift（抬起 0~1）、pull（往后 0~1）
    protected void PlaceBolt(GunRig g, float lift, float pull)
    {
        WeaponDef w = g.def;
        float back = w.boltTravel * pull / PPU_FULL;                        // 往后多远（单位）
        WeaponPart bp = w.PartDef(w.boltPart), hp = w.PartDef(w.boltHandle);
        SpriteRenderer bolt = g.Part(w.boltPart), handle = g.Part(w.boltHandle);
        if (bolt != null && bp != null) bolt.transform.localPosition = CanvasToLocal(bp.pivot - w.grip) + new Vector3(-back, 0f, 0f);
        if (handle != null && hp != null)
        {
            handle.transform.localPosition = CanvasToLocal(hp.pivot - w.grip) + new Vector3(-back, 0f, 0f);
            handle.transform.localRotation = Quaternion.Euler(0f, 0f, w.boltLift * lift);   // 绕根部抬起来
        }
    }

    // 拉栓球头现在在哪（枪的本地坐标）
    protected Vector3 KnobLocal(GunRig g, float lift, float pull)
    {
        WeaponDef w = g.def;
        WeaponPart hp = w.PartDef(w.boltHandle);
        Vector2 root = hp != null ? hp.pivot : w.boltGrab;                  // 拉栓的根部（转动点）
        Vector2 knob = Rot((Vector2)CanvasToLocal(w.boltGrab - root), w.boltLift * lift);   // 球头绕根部转
        return CanvasToLocal(root - w.grip) + new Vector3(knob.x - w.boltTravel * pull / PPU_FULL, knob.y, 0f);
    }
}
