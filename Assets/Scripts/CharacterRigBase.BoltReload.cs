using UnityEngine;

// ============================================================================
// 拉栓枪换弹（ReloadMotion.Bolt；CharacterRigBase 的一部分）：狙击枪重，换得慢一些。
//   左手 = 持枪手（握把，也是拉栓的手），右手 = 辅助手（护木；WeaponDef.reloadHand）
//   战术：左手先把拉栓拉到最后（抖）→ 右手托住枪，枪口上扬 80° → 左手去配件拿新弹匣 → 拿着新弹匣把旧弹匣拔下来（抖）
//         → 插上新弹匣（抖）→ 左手把旧弹匣放回配件 → 回来把拉栓推回去锁住（抖）→ 左手回握把
//   快速：枪举起来往后收（和 AK 一样）→ 右手把旧弹匣拔下来甩掉（拔下来那一下抖）→ 右手去配件拿新弹匣 → 插上（抖）
//         → 右手和枪一起回位（抖）→ 左手拉一次栓（拉栓动画）→ 左手回握把
//   普通：和快速前半段一样，但旧弹匣不甩：右手拔下来（不抖）带回配件放好 → 拿新弹匣 → 插上（抖）→ 右手和枪回位（抖）→ 左手拉栓 → 回握把
//   拉栓那一段和开完枪拉栓是同一套（CharacterRigBase.Bolt.cs 的 bo… 参数），往后拉时枪也被带着往后、枪口上扬。
// ============================================================================
public abstract partial class CharacterRigBase
{
    [Header("拉栓枪换弹（时间 = 秒，角度 = 度，距离 = 单位）")]
    public float bwRaiseTime = 0.35f;                          // 枪举起来
    public float bwUp = 30f;                                   // 快速 / 普通：枪口上抬多少度
    public float bwTacUp = 80f;                                // 战术：枪口上抬多少度（狙击枪重，右手托着）
    public Vector2 bwBack = new Vector2(-0.3f, 0.03f);         // 快速 / 普通：枪往后（x 负）/ 往上收多少
    public Vector2 bwTacBack = new Vector2(-0.15f, 0.03f);     // 战术：枪往后收多少
    public float bwReachTime = 0.25f;                          // 手伸到旧弹匣
    public float bwPullTime = 0.14f;                           // 拔下旧弹匣
    public float bwPullDist = 0.16f;                           // 拔出来多远
    public float bwPullShake = 6f;                             // 拔下来那一下枪抖多少度
    public Vector2 bwFling = new Vector2(-0.3f, -0.8f);        // 快速：旧弹匣甩出去的速度（枪的本地方向）
    public Vector2 bwFlingSpin = new Vector2(-500f, -250f);    // 快速：甩出去转多快（度 / 秒）
    public float bwFetchTime = 0.32f;                          // 手到配件
    public float bwGrabTime = 0.16f;                           // 从配件里拿出新弹匣
    public float bwBringTime = 0.3f;                           // 拿到弹匣口下面
    public float bwAlignTime = 0.12f;                          // 对准
    public float bwInsertTime = 0.12f;                         // 插进去
    public float bwInsertShake = 8f;                           // 插到底时枪抖多少度
    public float bwInsertShakeY = 0.03f;                       // 插到底时枪上下抖多少
    public float bwStowTime = 0.3f;                            // 带着旧弹匣到配件
    public float bwPushTime = 0.12f;                           // 塞进配件
    public float bwReturnTime = 0.3f;                          // 右手和枪一起回位
    public float bwReturnShake = 5f;                           // 回位时枪抖多少度
    public float bwLeftReturnTime = 0.2f;                      // 拉完栓左手回握把
    public float bwNewTilt = 15f;                              // 战术：新旧弹匣一起拿着时新弹匣歪多少
    public float bwOldTilt = -15f;                             // 战术：旧弹匣歪多少
    public Vector2 bwOldOffset = new Vector2(0.04f, 0f);       // 战术：旧弹匣在新弹匣旁边多远

    void BoltReload(float dt)
    {
        if (_reloadKind == ReloadKind.Tactical) BoltTactical(dt);
        else BoltMagSwap(dt, _reloadKind == ReloadKind.Speed);
    }

    float BoltReloadTotal(ReloadStyle s, ReloadKind kind)
    {
        float end = kind == ReloadKind.Tactical ? BoltTacticalTimes(null).end : BoltSwapTimes(s, kind == ReloadKind.Speed).end;
        return Mathf.Max(0.05f, end);
    }

    // ---------------- 公共小工具 ----------------
    // 左手（持枪手，第二只换弹手）：摆到 hp / ha，图层按在不在身后
    void PlaceLeftHand(GunReloadGeo geo, BullpupGeo b, Vector2 hp, float ha, bool show, bool behind)
    {
        EnsureRlHand2();
        if (b.main != null) { b.main.enabled = !show; _rlHand2Sprite.sprite = b.main.sprite; }
        _rlHand2Sprite.enabled = show;
        _rlHand2.localPosition = new Vector3(hp.x, hp.y, 0f);
        _rlHand2.localRotation = Quaternion.Euler(0f, 0f, ha);
        _rlHand2Sprite.sortingOrder = behind ? _rlStyle.BehindOrder(0) : geo.handOrder;
    }

    // 弹匣对准 / 插好时手在哪（手 = 弹匣底 - 弹匣在手里的位置）
    Vector2 MagHand(Vector2 bottom, Vector2 inHand, float ang) => bottom - Rot(inHand, ang);

    // ---------------- 快速 / 普通（右手换弹匣，最后左手拉栓）----------------
    struct SwapTimes { public float grab, pulled, flingT, stowed, pushed, switched, taken, ready, seated, back, b0, end; }

    SwapTimes BoltSwapTimes(ReloadStyle s, bool speed)
    {
        SwapTimes o = new SwapTimes();
        o.grab = Mathf.Max(bwRaiseTime, bwReachTime);              // 右手抓住旧弹匣（枪也举好了）
        o.pulled = o.grab + bwPullTime;                             // 拔下来
        float t = o.pulled;
        if (speed) { o.flingT = t; }                                // 快速：拔下来就甩掉，直接去拿新的
        else
        {
            o.stowed = t + bwStowTime; o.pushed = o.stowed + bwPushTime;   // 普通：带回配件放好
            o.switched = o.pushed + (s != null ? s.SwitchTime(this) : 0f);
            t = o.switched - bwFetchTime;                           // 下面"去配件"那一段换成"在配件里移过去"
        }
        float fetched = speed ? t + bwFetchTime : o.switched;
        o.taken = fetched + bwGrabTime;                             // 拿出新弹匣
        o.ready = o.taken + bwBringTime + bwAlignTime;              // 到弹匣口下面、对准
        o.seated = o.ready + bwInsertTime;                          // 插到底
        o.back = o.seated + 0.06f + bwReturnTime;                   // 右手和枪一起回位
        o.b0 = o.back + boReachTime;                                // 左手抓到拉栓，开始拉
        o.end = o.b0 + BoltCycleLength + bwLeftReturnTime;
        return o;
    }

    void BoltMagSwap(float dt, bool speed)
    {
        _reloadT += dt * _reloadRate;
        float t = _reloadT;
        ReloadStyle st = _rlStyle;
        SwapTimes T = BoltSwapTimes(st, speed);
        GunRig g = _rlGun;
        if (t >= T.end) { PlaceBolt(g, 0f, 0f); StopGunReload(); ReloadDonePose(); return; }
        GunReloadGeo geo = Geo(g); BullpupGeo b = BGeo(g);
        Vector2 seated = geo.magIn, dir = geo.magDir.normalized, inHand = geo.magInHand;

        // ---- 1. 枪：举起来往后收 → 插好后和右手一起回位 → 拉栓时被手带着抖 ----
        float k = t < T.seated ? Ease(Seg(t, 0f, bwRaiseTime)) : 1f - Ease(Seg(t, T.seated + 0.06f, bwReturnTime));
        BoltCycle(t - T.b0, out float lift, out float pull, out float bAng, out float bBack);
        float shA = (speed ? bwPullShake * Shake(t - T.pulled, 0.2f) : 0f)
                  - bwInsertShake * Shake(t - T.seated, 0.25f) - bwReturnShake * Shake(t - T.back, 0.22f) + bAng;
        float aimNow = _aimAng * (1f - k);
        Vector2 shP = Rot(new Vector2(-bBack, bwInsertShakeY * Shake(t - T.seated, 0.25f)), aimNow + bwUp * k);
        AkGun(k, bwUp * k, shA, shP, bwBack, out Vector2 gunPos, out float gunAng);
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);
        PlaceBolt(g, lift, pull);

        // ---- 2. 右手（换弹匣）----
        Vector2 rRest = G2R(geo.rest); float rRestAng = gunAng + geo.restAng;
        st.TakePose(this, inHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);
        st.StowPose(this, inHand, 0f, out Vector2 stowPos, out Vector2 stowOut, out float stowAng);
        bool hip = st is HipPouchReload;
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));
        Vector2 seatHand = MagHand(G2R(seated), inHand, gunAng);
        Vector2 pulledHand = MagHand(G2R(seated - dir * bwPullDist), inHand, gunAng);
        Vector2 readyHand = MagHand(G2R(seated - dir * geo.ready), inHand, gunAng);
        float fetchStart = speed ? T.flingT : T.switched - bwFetchTime;
        Vector2 hp; float ha; int leg = 0;
        if (t < T.grab) { float e = Ease(Seg(t, 0f, T.grab)); hp = Vector2.Lerp(rRest, seatHand, e); ha = Mathf.LerpAngle(rRestAng, gunAng, e); }
        else if (t < T.pulled) { hp = Vector2.Lerp(seatHand, pulledHand, Ease(Seg(t, T.grab, bwPullTime))); ha = gunAng; }
        else if (!speed && t < T.stowed) { float u = Seg(t, T.pulled, bwStowTime), e = Ease(u); hp = hip ? AkAround(pulledHand, stowOut, u) : Vector2.Lerp(pulledHand, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(gunAng, stowAng, e); leg = 1; }
        else if (!speed && t < T.pushed) { hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, T.stowed, bwPushTime))); ha = stowAng; leg = 2; }
        else if (!speed && t < T.switched) { float e = Ease(Seg(t, T.pushed, T.switched - T.pushed)); hp = Vector2.Lerp(stowPos, takePos, e); ha = Mathf.LerpAngle(stowAng, takeAng, e); leg = 2; }
        else if (speed && t < fetchStart + bwFetchTime) { float u = Seg(t, fetchStart, bwFetchTime), e = Ease(u); hp = hip ? AkAround(pulledHand, takePos, u) : Vector2.Lerp(pulledHand, takePos, e) + Arc(e); ha = Mathf.LerpAngle(gunAng, takeAng, e); leg = 1; }
        else if (t < T.taken) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, T.taken - bwGrabTime, bwGrabTime))); ha = takeAng; leg = 2; }
        else if (t < T.ready - bwAlignTime) { float u = Seg(t, T.taken, bwBringTime), e = Ease(u); hp = hip ? AkAround(takeOut, readyHand, u) : Vector2.Lerp(takeOut, readyHand, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, gunAng, e); leg = 1; }
        else if (t < T.ready) { hp = readyHand; ha = gunAng; }
        else if (t < T.seated) { hp = Vector2.Lerp(readyHand, seatHand, Ease(Seg(t, T.ready, bwInsertTime))); ha = gunAng; }
        else if (t < T.seated + 0.06f) { hp = seatHand; ha = gunAng; }
        else { float e = Ease(Seg(t, T.seated + 0.06f, bwReturnTime)); hp = Vector2.Lerp(seatHand, rRest, e); ha = Mathf.LerpAngle(gunAng, rRestAng, e); }
        bool rOut = t < T.back;
        if (geo.hand != null) geo.hand.enabled = !rOut;
        _rlHandSprite.enabled = rOut;
        if (geo.hand != null) _rlHandSprite.sprite = geo.hand.sprite;
        PlaceReloadHand(geo, hp, ha, st.Behind(this, hp, leg));

        // ---- 3. 弹匣 ----
        bool oldInHand = t >= T.grab && (speed ? t < T.pulled : t < T.pushed && !_rlOldStowed);
        if (!speed && t >= T.pushed) MarkStowed();
        BpMag(_rlOldMag, hp, ha, inHand, 0f, oldInHand, _rlOldMag.sortingOrder);
        if (speed && t >= T.pulled && !_qrDropped)                                  // 快速：拔下来一甩
        {
            _qrDropped = true;
            DropGunMag(_rlOldMag.sprite, geo.oldOrder, G2R(seated - dir * bwPullDist), gunAng, Rot(bwFling, gunAng), RandIn(bwFlingSpin));   // 从拔出来的地方甩出去
        }
        bool newShow = t >= T.taken - bwGrabTime * 0.5f && t < T.seated;
        if (newShow) MarkTaken();
        BpMag(_rlNewMag, hp, ha, inHand, 0f, newShow, _rlNewMag.sortingOrder);
        if (geo.mag != null) geo.mag.enabled = !(t >= T.grab && t < T.seated);

        // ---- 4. 左手：插好、回位之后拉一次栓 ----
        BoltLeftHand(t, T.back, T.b0, T.b0 + BoltCycleLength, T.end, g, geo, b, G2R, gunAng, lift, pull);
    }

    // 左手：握把 → 拉栓球头（跟着拉栓走）→ 握把。tGo = 开始伸过去，tOn = 抓到，tOff = 拉完，tHome = 回到握把
    void BoltLeftHand(float t, float tGo, float tOn, float tOff, float tHome, GunRig g, GunReloadGeo geo, BullpupGeo b,
                      System.Func<Vector2, Vector2> G2R, float gunAng, float lift, float pull)
    {
        Vector2 lRest = G2R(b.mainRest); float lRestAng = gunAng + b.mainRestAng;
        Vector2 knob = G2R(KnobLocal(g, lift, pull)); float kAng = lRestAng + boHandAngle;
        Vector2 knob0 = G2R(KnobLocal(g, 0f, 0f));
        Vector2 lp; float la; bool show = t >= tGo && t < tHome;
        if (t < tGo) { lp = lRest; la = lRestAng; }
        else if (t < tOn) { float e = Ease(Seg(t, tGo, tOn - tGo)); lp = Vector2.Lerp(lRest, knob0, e); la = Mathf.LerpAngle(lRestAng, kAng, e); }
        else if (t < tOff) { lp = knob; la = kAng; }
        else { float e = Ease(Seg(t, tOff, tHome - tOff)); lp = Vector2.Lerp(knob0, lRest, e); la = Mathf.LerpAngle(kAng, lRestAng, e); }
        PlaceLeftHand(geo, b, lp, la, show, false);
    }

    // ---------------- 战术（左手先开栓、换弹匣、收旧弹匣、关栓）----------------
    struct TacTimes { public float open, raised, fetched, taken, grabOld, pulled, ready, seated, stowed, pushed, atKnob, closed, end; }

    TacTimes BoltTacticalTimes(ReloadStyle s)
    {
        TacTimes o = new TacTimes();
        float open0 = boReachTime;                                  // 左手抓到拉栓
        o.open = open0 + boLiftTime + boPullTime;                   // 抬起、拉到最后（拉开不推回去）
        o.raised = o.open + bwRaiseTime;                            // 右手托着枪口上扬 80°（同时左手已经往配件去了）
        o.fetched = o.open + bwFetchTime;
        o.taken = o.fetched + bwGrabTime;                           // 拿出新弹匣
        o.grabOld = Mathf.Max(o.taken + bwBringTime, o.raised);     // 拿着新弹匣抓住旧弹匣
        o.pulled = o.grabOld + bwPullTime;                          // 拔下旧弹匣（抖）
        o.ready = o.pulled + bwAlignTime;                           // 新弹匣对准
        o.seated = o.ready + bwInsertTime;                          // 插好（抖）
        o.stowed = o.seated + 0.06f + bwStowTime;                   // 带着旧弹匣到配件
        o.pushed = o.stowed + bwPushTime;                           // 塞进去
        o.atKnob = o.pushed + bwFetchTime;                          // 回到拉栓（枪口同时放下来）
        o.closed = o.atKnob + boPushTime + boLockTime;              // 推回去、压下锁住（抖）
        o.end = o.closed + bwLeftReturnTime + 0.1f;
        return o;
    }

    void BoltTactical(float dt)
    {
        _reloadT += dt * _reloadRate;
        float t = _reloadT;
        ReloadStyle st = _rlStyle;
        TacTimes T = BoltTacticalTimes(st);
        GunRig g = _rlGun;
        if (t >= T.end) { PlaceBolt(g, 0f, 0f); StopGunReload(); ReloadDonePose(); return; }
        GunReloadGeo geo = Geo(g); BullpupGeo b = BGeo(g);
        Vector2 seated = geo.magIn, dir = geo.magDir.normalized, inHand = geo.magInHand;
        Vector2 oldInHand = inHand + bwOldOffset;

        // ---- 拉栓：拉开（抬 + 往后，拉到最后不推回去）→ 换完弹匣推回去锁住 ----
        float o0 = boReachTime, o1 = o0 + boLiftTime, o2 = o1 + boPullTime;
        float c0 = T.atKnob, c1 = c0 + boPushTime;
        float lift = t < o0 ? 0f : t < o1 ? Ease(Seg(t, o0, boLiftTime)) : t < c1 ? 1f : 1f - Ease(Seg(t, c1, boLockTime));
        float pull = t < o1 ? 0f : t < o2 ? Ease(Seg(t, o1, boPullTime)) : t < c0 ? 1f : 1f - Ease(Seg(t, c0, boPushTime));
        float drag = (t >= o1 && t < o2) ? pull : (t >= c0 && t < c1 ? pull : 0f);  // 拉 / 推的时候枪被手带着

        // ---- 枪：拉开栓后右手托着枪口上扬 80° → 收好旧弹匣后放下来 ----
        float k = t < T.pushed ? Ease(Seg(t, T.open, bwRaiseTime)) : 1f - Ease(Seg(t, T.pushed, bwFetchTime));
        float shA = boPullRise * drag + boShake * (Shake(t - o0 - 0f, 0.15f) + Shake(t - o2, 0.2f) - Shake(t - T.closed, 0.18f))
                  + bwPullShake * Shake(t - T.pulled + bwPullTime, bwPullTime + 0.12f) - bwInsertShake * Shake(t - T.seated, 0.25f);
        float aimNow = _aimAng * (1f - k);
        Vector2 shP = Rot(new Vector2(-(boPullBack * drag + boShakeX * Shake(t - o2, 0.18f)), bwInsertShakeY * Shake(t - T.seated, 0.25f)), aimNow + bwTacUp * k);
        AkGun(k, bwTacUp * k, shA, shP, bwTacBack, out Vector2 gunPos, out float gunAng);
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);
        PlaceBolt(g, lift, pull);

        // ---- 左手：拉栓 → 配件拿新弹匣 → 抓旧弹匣、拔下来、插新的 → 旧弹匣收回配件 → 回拉栓关上 → 回握把 ----
        Vector2 lRest = G2R(b.mainRest); float lRestAng = gunAng + b.mainRestAng;
        float kAng = lRestAng + boHandAngle;
        Vector2 knob = G2R(KnobLocal(g, lift, pull));
        st.TakePose(this, inHand, bwNewTilt, out Vector2 takePos, out Vector2 takeOut, out float takeAng);
        st.StowPose(this, oldInHand, bwOldTilt, out Vector2 stowPos, out Vector2 stowOut, out float stowAng);
        bool hip = st is HipPouchReload;
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));
        float grabAng = gunAng - bwOldTilt;                                  // 手里"旧弹匣的位置"对上枪里的旧弹匣
        Vector2 grabPos = MagHand(G2R(seated), oldInHand, grabAng);
        Vector2 pulledPos = MagHand(G2R(seated - dir * bwPullDist), oldInHand, grabAng);
        float alignAng = gunAng - bwNewTilt;                                 // 换成新弹匣对着弹匣口
        Vector2 readyPos = MagHand(G2R(seated - dir * geo.ready), inHand, alignAng);
        Vector2 seatPos = MagHand(G2R(seated), inHand, alignAng);
        Vector2 lp; float la; int leg = 0;
        if (t < o0) { float e = Ease(Seg(t, 0f, o0)); lp = Vector2.Lerp(lRest, knob, e); la = Mathf.LerpAngle(lRestAng, kAng, e); }
        else if (t < T.open) { lp = knob; la = kAng; }
        else if (t < T.fetched) { float u = Seg(t, T.open, bwFetchTime), e = Ease(u); lp = hip ? AkAround(knob, takePos, u) : Vector2.Lerp(knob, takePos, e) + Arc(e); la = Mathf.LerpAngle(kAng, takeAng, e); leg = 1; }
        else if (t < T.taken) { lp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, T.fetched, bwGrabTime))); la = takeAng; leg = 2; }
        else if (t < T.grabOld) { float u = Seg(t, T.taken, T.grabOld - T.taken), e = Ease(u); lp = hip ? AkAround(takeOut, grabPos, u) : Vector2.Lerp(takeOut, grabPos, e) + Arc(e); la = Mathf.LerpAngle(takeAng, grabAng, e); leg = 1; }
        else if (t < T.pulled) { lp = Vector2.Lerp(grabPos, pulledPos, Ease(Seg(t, T.grabOld, bwPullTime))); la = grabAng; }
        else if (t < T.ready) { float e = Ease(Seg(t, T.pulled, bwAlignTime)); lp = Vector2.Lerp(pulledPos, readyPos, e); la = Mathf.LerpAngle(grabAng, alignAng, e); }
        else if (t < T.seated) { lp = Vector2.Lerp(readyPos, seatPos, Ease(Seg(t, T.ready, bwInsertTime))); la = alignAng; }
        else if (t < T.seated + 0.06f) { lp = seatPos; la = alignAng; }
        else if (t < T.stowed) { float u = Seg(t, T.seated + 0.06f, bwStowTime), e = Ease(u); lp = hip ? AkAround(seatPos, stowOut, u) : Vector2.Lerp(seatPos, stowOut, e) + Arc(e); la = Mathf.LerpAngle(alignAng, stowAng, e); leg = 1; }
        else if (t < T.pushed) { lp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, T.stowed, bwPushTime))); la = stowAng; leg = 2; }
        else if (t < T.atKnob) { float u = Seg(t, T.pushed, bwFetchTime), e = Ease(u); lp = hip ? AkAround(stowPos, knob, u) : Vector2.Lerp(stowPos, knob, e) + Arc(e); la = Mathf.LerpAngle(stowAng, kAng, e); leg = 1; }
        else if (t < T.closed) { lp = knob; la = kAng; }
        else { float e = Ease(Seg(t, T.closed, bwLeftReturnTime)); lp = Vector2.Lerp(knob, lRest, e); la = Mathf.LerpAngle(kAng, lRestAng, e); }
        bool behind = st.Behind(this, lp, leg);
        PlaceLeftHand(geo, b, lp, la, t < T.closed + bwLeftReturnTime, behind);
        _rlHandSprite.enabled = false;                                       // 右手一直托着枪（枪上的辅助手照常显示）
        if (geo.hand != null) geo.hand.enabled = true;

        // ---- 弹匣（都在左手里）----
        int nOrder = behind ? _rlStyle.BehindOrder(1) : geo.newOrder, oOrder = behind ? _rlStyle.BehindOrder(2) : geo.oldOrder;
        bool newShow = t >= T.fetched + bwGrabTime * 0.5f && t < T.seated;
        if (newShow) MarkTaken();
        if (t >= T.pushed) MarkStowed();
        BpMag(_rlNewMag, lp, la, inHand, bwNewTilt, newShow, nOrder);       // 新弹匣在手里歪着（对准时手转过来，弹匣正好对着弹匣口）
        BpMag(_rlOldMag, lp, la, oldInHand, bwOldTilt, t >= T.grabOld && t < T.pushed && !_rlOldStowed, oOrder);
        if (geo.mag != null) geo.mag.enabled = !(t >= T.grabOld && t < T.seated);
    }
}
