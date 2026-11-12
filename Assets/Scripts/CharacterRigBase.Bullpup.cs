using UnityEngine;

// ============================================================================
// 无托步枪式换弹（CharacterRigBase 的一部分，partial）：弹匣在握把后面（比如 ABA-X95），没有空仓挂机。
//   左手 = 持枪手（握把），右手 = 辅助手（护木；WeaponDef.reloadHand）。拉栓 = 枪上会往后弹的部件（kick > 0）。
//   怎么插、怎么对准由枪决定（弹匣部件 / 插入方向）；从哪拿新弹匣由配件决定（胸挂 / 屁股后面）。
//
//   快速：左手离开握把去抓弹匣（抓上去抖一下）→ 用手甩掉（一定是手甩）；同时右手去抓拉栓、往后拉住，
//         枪没了左手支撑：枪尾往下沉 15~30°、往后退 → 左手去拿新弹匣，干脆利落插进去 → 再拍一下弹匣底（枪尾从下往上抖）
//         → 左手先回握把 → 右手松开拉栓，和枪一起回位（枪身抖一下）
//   战术：左手把枪抬起 80°、往后退，同时右手去拿新弹匣 → 拿着新弹匣取下旧弹匣（两个弹匣成 V 字）→ 插上新弹匣
//         → 带着旧弹匣再拍一下弹匣底 → 旧弹匣放回去 → 右手去抓拉栓往后拉（上膛），拉到底枪轻微转 + 前后抖 → 右手回位
//   普通：左手把枪抬起 80°、往后退，同时右手取下旧弹匣 → 放回去 → 再拿新弹匣插上 → 拍一下弹匣底 → 上膛 → 右手回位
// ============================================================================
public abstract partial class CharacterRigBase
{
    [Header("无托步枪换弹（时间 = 秒，距离 = 单位，角度 = 度）")]
    public float bpRaiseTime = 0.3f;                          // 战术 / 普通：左手把枪抬起来
    public float bpUp = 80f;                                  // 战术 / 普通：枪口抬多少度
    public Vector2 bpBack = new Vector2(-0.03f, 0f);          // 换弹时枪往后退多少（三种都用；在收到挂载位置之后再加）
    [Range(0f, 1f)] public float bpMountPull = 0.9f;          // 换弹时枪往后收到挂载位置（胸挂 / 背带上挂枪的地方）的程度（1 = 枪的中心正好到挂载位置）
    public float bpFetchTime = 0.3f;                          // 手伸到配件（拿新弹匣）
    public float bpGrabTime = 0.12f;                          // 从配件里拿出新弹匣
    public float bpBringTime = 0.25f;                         // 拿着新弹匣到弹匣口
    public float bpAlignTime = 0.12f;                         // 对准弹匣口
    public float bpInsertTime = 0.1f;                         // 插进去
    public float bpReachOldTime = 0.25f;                      // 普通：右手伸去抓旧弹匣
    public float bpPullOldTime = 0.12f;                       // 拔出旧弹匣
    public float bpPullOldDist = 0.12f;                       // 拔出旧弹匣拉多远（沿弹匣方向）
    public Vector2 bpOldOffset = new Vector2(-0.03f, 0f);     // 战术：旧弹匣在手里的位置（和新弹匣并排）
    public float bpOldTilt = 25f;                             // 战术：旧弹匣在手里歪多少（V 字）
    public float bpNewTilt = -10f;                            // 战术：新弹匣在手里歪多少（V 字的另一边）
    public float bpSlapTime = 0.16f;                          // 再拍一下弹匣底：手往下退再往上拍
    public float bpSlapDist = 0.03f;                          // 拍之前手往下退多远
    public float bpSlapShake = 8f;                            // 拍上去时枪尾从下往上抖多少度（原来 4；战斗步枪插到底抖 8）
    public float bpSlapShakeY = 0.025f;                       // 拍上去时枪往上弹多少（原来 0.006；战斗步枪 0.01~0.04）
    public float bpStowTime = 0.28f;                          // 带着旧弹匣到配件
    public float bpPushTime = 0.12f;                          // 把旧弹匣塞进去
    public float bpChargeReachTime = 0.2f;                    // 右手伸到拉栓
    public float bpChargePullTime = 0.14f;                    // 拉栓往后拉到底
    public Vector2 bpChargeGrip = new Vector2(0f, 0.012f);    // 右手抓拉栓时手心相对拉栓中心（枪的本地坐标）
    public float bpChargeAngle = 0f;                          // 右手抓拉栓时多转多少度
    public float bpChargeShake = 6f;                          // 拉到底：枪转着抖多少度（原来 3；战斗步枪抓住时抖 6）
    public float bpChargeShakeX = 0.025f;                     // 拉到底：枪前后抖多远（原来 0.012）
    public float bpChargeShakeTime = 0.3f;                    // 拉到底那一下抖多久
    public float bpReturnTime = 0.25f;                        // 手回位
    public float bpLevelTime = 0.3f;                          // 枪放平（回到平时持枪）

    [Header("无托步枪快速换弹（拔 / 插 / 拍 / 拉栓用上面的参数）")]
    public float bqGrabTime = 0.18f;                          // 左手离开握把去抓弹匣
    public float bqGrabShake = 6f;                            // 左手抓上弹匣的一瞬间枪抖多少度（原来 3）
    public float bqTilt = 22f;                                // 没了左手支撑：枪尾往下沉多少度（15~30）
    public float bqTiltTime = 0.18f;                          // 沉下去用几秒
    public float bqFlingTime = 0.14f;                         // 甩掉旧弹匣：手甩一下
    public Vector2 bqFling = new Vector2(-0.6f, -0.5f);       // 甩出去的速度（x 往前，y 往上；单位 / 秒）
    public Vector2 bqFlingHand = new Vector2(-0.025f, -0.02f);   // 甩的时候手往后下方挥多远（枪的本地坐标；越小手动得越少）
    public Vector2 bqFlingSpin = new Vector2(-900f, -500f);   // 甩出去时转多快（度 / 秒）
    public float bqInsertTime = 0.07f;                        // 干脆利落插进去
    public float bqReleaseTime = 0.25f;                       // 右手松开拉栓，和枪一起回位
    public float bqEndShake = 6f;                             // 回位时枪身抖多少度（原来 3）

    // 第二只换弹手（左手 = 持枪手）：无托快速换弹时左手要离开握把
    protected Transform _rlHand2;
    protected SpriteRenderer _rlHand2Sprite;

    // 无托换弹要用的几个量
    struct BullpupGeo
    {
        public SpriteRenderer main;      // 枪上的持枪手（左手，握把）
        public Vector2 mainRest;         // 左手平时在哪（枪的本地坐标）
        public float mainRestAng;
        public SpriteRenderer charger;   // 拉栓
        public Vector2 chargerAt;        // 拉栓中心（枪的本地坐标）
        public float chargerKick;        // 拉栓往后拉多远（原图像素）
    }

    BullpupGeo BGeo(GunRig g)
    {
        WeaponDef d = g.def;
        BullpupGeo o = new BullpupGeo();
        foreach (WeaponPart p in d.parts)
        {
            if (p == null) continue;
            SpriteRenderer sr = g.Part(p.name);
            if (sr == null) continue;
            if (p.hand && p.name != d.reloadHand && o.main == null)
            {
                o.main = sr;
                o.mainRest = sr.transform.localPosition;
                o.mainRestAng = sr.transform.localEulerAngles.z;
            }
            if (p.kick > 0f && o.charger == null)
            {
                o.charger = sr;
                o.chargerAt = CanvasToLocal(p.pivot - d.grip);
                o.chargerKick = p.kick;
            }
        }
        return o;
    }

    void EnsureRlHand2()
    {
        if (_rlHand2 != null) return;
        _rlHand2 = new GameObject("ReloadHand2").transform;
        _rlHand2.SetParent(_rig, false);
        _rlHand2Sprite = _rlHand2.gameObject.AddComponent<SpriteRenderer>();
        _rlHand2Sprite.enabled = false;
    }

    // 拉栓拉到 s（0 = 原位，1 = 最后面）时，右手在哪（枪的本地坐标）
    Vector2 ChargeHandLocal(BullpupGeo b, float s) => b.chargerAt + new Vector2(-b.chargerKick * s / PPU_FULL, 0f) + bpChargeGrip;

    // 枪：瞄准角度渐渐转回正前方 + 抬起 / 沉下 + 往后退 + 抖动（k = 换弹姿势的程度 0~1）
    void BpGun(float k, float up, float shakeAng, Vector2 shakePos, out Vector2 gunPos, out float gunAng)
    {
        float aimNow = _aimAng * (1f - k);
        Vector2 basePos = ReloadAimHand(aimNow, 1f - k);
        if (bpMountPull > 0f && _rlGun != null && GunStowSpot(out Vector2 spot))   // 枪往后收到挂载位置附近
        {
            WeaponDef d = _rlGun.def;
            Vector2 c = d.mountCenter != Vector2.zero ? d.mountCenter : d.grip;      // 枪的中心
            Vector2 want = spot + Rot((Vector2)CanvasToLocal(d.grip - c), aimNow + up);   // 枪的中心对上挂载位置时，握把在哪
            basePos = Vector2.Lerp(basePos, want, bpMountPull * k);
        }
        gunPos = basePos + bpBack * k + shakePos;
        gunAng = aimNow + up + shakeAng;
        Vector2 tr = Grip.Offset(_time, out float trA);                    // 持枪手类型：颤抖的手换弹时也抖
        gunPos += tr; gunAng += trA;
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
    }

    // 去 / 从屁股后面拿弹匣：先横着（x）绕到身后，再往下（y）；回来时反过来先往上、再横着出来。不从屁股中间穿过去
    //   u = 这一段的进度（0~1，不用先 Ease）；toBack = 往身后走
    static Vector2 AroundBack(Vector2 from, Vector2 to, float u, bool toBack)
    {
        float ex = toBack ? Ease(Mathf.Clamp01(u / 0.65f)) : Ease(Mathf.Clamp01((u - 0.35f) / 0.65f));   // 横着走
        float ey = toBack ? Ease(Mathf.Clamp01((u - 0.45f) / 0.55f)) : Ease(Mathf.Clamp01(u / 0.55f));   // 上下走
        return new Vector2(Mathf.Lerp(from.x, to.x, ex), Mathf.Lerp(from.y, to.y, ey));
    }

    // 拍弹匣底：0 → 往下退 → 往上拍回去（返回手离插到底的位置多远，沿弹匣方向往下）
    float SlapDepth(float t, float a) { float u = Seg(t, a, bpSlapTime); return u <= 0f || u >= 1f ? 0f : bpSlapDist * (u < 0.6f ? Ease(u / 0.6f) : 1f - (u - 0.6f) / 0.4f); }

    // 手的图层（在身后 = 压到身体下面）
    void BpOrder(SpriteRenderer sr, GunReloadGeo geo, bool behind) { sr.sortingOrder = behind ? _rlStyle.BehindOrder(0) : geo.handOrder; }

    // 弹匣摆在手里（底部在 magInHand）
    void BpMag(SpriteRenderer mag, Vector2 hp, float ha, Vector2 inHand, float tilt, bool show, int order)
    {
        mag.enabled = show;
        if (!show) return;
        Vector2 b = hp + Rot(inHand, ha);
        mag.transform.localPosition = new Vector3(b.x, b.y, 0f);
        mag.transform.localRotation = Quaternion.Euler(0f, 0f, ha + tilt);
        mag.sortingOrder = order;
    }

    void BullpupReload(float dt)
    {
        if (_reloadKind == ReloadKind.Speed) BpSpeed(dt);
        else if (_reloadKind == ReloadKind.Tactical) BpTactical(dt);
        else BpNormal(dt);
    }

    float BullpupReloadTotal(ReloadStyle s, ReloadKind kind)
    {
        float end;
        if (kind == ReloadKind.Speed) { BpSpeedTimes(out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out end); }
        else if (kind == ReloadKind.Tactical) { BpTacticalTimes(out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out end); }
        else { BpNormalTimes(s, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out end); }
        return Mathf.Max(0.05f, end);
    }

    // ---------------- 快速 ----------------
    void BpSpeedTimes(out float qa, out float qb, out float qc, out float qd, out float qe, out float qf, out float qg, out float qh, out float qi, out float tPulled, out float end)
    {
        qa = bqGrabTime;                       // 左手抓住弹匣
        qb = qa + bpPullOldTime;               // 拔出来
        qc = qb + bqFlingTime;                 // 甩掉
        qd = qc + bpFetchTime;                 // 左手到配件
        qe = qd + bpGrabTime;                  // 拿出新弹匣
        qf = qe + bpBringTime;                 // 到弹匣口
        qg = qf + bqInsertTime;                // 插到底
        qh = qg + bpSlapTime;                  // 拍一下弹匣底
        qi = qh + bpReturnTime;                // 左手回到握把
        tPulled = bpChargeReachTime + bpChargePullTime;   // 右手拉住拉栓
        end = Mathf.Max(qi, tPulled) + bqReleaseTime;     // 右手松开、和枪一起回位
    }

    void BpSpeed(float dt)
    {
        _reloadT += dt * _reloadRate;
        float t = _reloadT;
        BpSpeedTimes(out float qa, out float qb, out float qc, out float qd, out float qe, out float qf, out float qg, out float qh, out float qi, out float tPulled, out float end);
        if (t >= end) { StopGunReload(); ReloadDonePose(); return; }
        GunRig g = _rlGun; GunReloadGeo geo = Geo(g); BullpupGeo b = BGeo(g); ReloadStyle st = _rlStyle;
        EnsureRlHand2();
        float rel = Mathf.Max(qi, tPulled);                                // 右手松开拉栓的时刻

        // ---- 1. 枪：没了左手支撑，枪尾往下沉、往后退；左手抓弹匣抖一下、拍弹匣底抖一下、最后回位抖一下 ----
        float k = t < rel ? Ease(Seg(t, 0f, bqTiltTime)) : 1f - Ease(Seg(t, rel, bqReleaseTime));
        float shA = bqGrabShake * Shake(t - qa, 0.2f) - bpSlapShake * Shake(t - qh + bpSlapTime * 0.4f, 0.25f) + bqEndShake * Shake(t - rel, bqReleaseTime + 0.1f);
        Vector2 shP = new Vector2(0f, bpSlapShakeY * Shake(t - qh + bpSlapTime * 0.4f, 0.25f));
        BpGun(k, bqTilt * k, shA, shP, out Vector2 gunPos, out float gunAng);
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);

        // ---- 2. 拉栓：右手拉到底一直拉着，松开时弹回去 ----
        float pull = t < rel ? Ease(Seg(t, bpChargeReachTime, bpChargePullTime)) : 1f - Seg(t, rel, 0.05f);
        g.Kick(pull, PPU_FULL);

        // ---- 3. 右手：护木 → 拉栓 → 拉住 → 松开回护木 ----
        Vector2 rRest = G2R(geo.rest); float rRestAng = gunAng + geo.restAng;
        float cAng = gunAng + bpChargeAngle;
        Vector2 rp; float ra;
        if (t < bpChargeReachTime) { float e = Ease(Seg(t, 0f, bpChargeReachTime)); rp = Vector2.Lerp(rRest, G2R(ChargeHandLocal(b, 0f)), e); ra = Mathf.LerpAngle(rRestAng, cAng, e); }
        else if (t < rel) { rp = G2R(ChargeHandLocal(b, pull)); ra = cAng; }
        else { float e = Ease(Seg(t, rel, bqReleaseTime)); rp = Vector2.Lerp(G2R(ChargeHandLocal(b, 0f)), rRest, e); ra = Mathf.LerpAngle(cAng, rRestAng, e); }
        bool rOut = t < end - 0.0001f && t < rel + bqReleaseTime;
        if (geo.hand != null) geo.hand.enabled = !rOut;
        _rlHandSprite.enabled = rOut;
        _rlHandSprite.sprite = geo.hand != null ? geo.hand.sprite : _rlHandSprite.sprite;
        _rlHand.localPosition = new Vector3(rp.x, rp.y, 0f);
        _rlHand.localRotation = Quaternion.Euler(0f, 0f, ra);
        _rlHandSprite.sortingOrder = geo.handOrder;

        // ---- 4. 左手：握把 → 抓弹匣 → 拔出 → 甩掉 → 配件拿新弹匣 → 插 → 拍 → 回握把 ----
        Vector2 magIn = geo.magIn, magDir = geo.magDir, inHand = geo.magInHand;
        Vector2 lRest = G2R(b.mainRest); float lRestAng = gunAng + b.mainRestAng;
        Vector2 grabPos = G2R(magIn) - Rot(inHand, gunAng);                 // 手抓在弹匣底
        Vector2 pulledPos = grabPos + Rot(-magDir, gunAng) * bpPullOldDist;
        Vector2 flingPos = pulledPos + Rot(bqFlingHand, gunAng);           // 甩的时候手往后下方一挥（小幅度）
        bool hip = st is HipPouchReload;                                   // 新弹匣在屁股后面：手绕到身后走
        st.TakePose(this, inHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);
        float depth = t < qf ? geo.ready : Mathf.Lerp(geo.ready, 0f, Ease(Seg(t, qf, bqInsertTime)));
        Vector2 readyPos = G2R(magIn - magDir * depth) - Rot(inHand, gunAng);
        Vector2 seatedPos = G2R(magIn - magDir * SlapDepth(t, qg)) - Rot(inHand, gunAng);
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));
        Vector2 lp; float la; int leg = 0;
        if (t < qa) { float e = Ease(Seg(t, 0f, bqGrabTime)); lp = Vector2.Lerp(lRest, grabPos, e); la = Mathf.LerpAngle(lRestAng, gunAng, e); }
        else if (t < qb) { lp = Vector2.Lerp(grabPos, pulledPos, Ease(Seg(t, qa, bpPullOldTime))); la = gunAng; }
        else if (t < qc) { float e = Ease(Seg(t, qb, bqFlingTime)); lp = Vector2.Lerp(pulledPos, flingPos, e); la = gunAng - 30f * Mathf.Sin(Mathf.PI * e); }
        else if (t < qd) { float u = Seg(t, qc, bpFetchTime), e = Ease(u); lp = hip ? AroundBack(flingPos, takePos, u, true) : Vector2.Lerp(flingPos, takePos, e) + Arc(e); la = Mathf.LerpAngle(gunAng, takeAng, e); leg = 1; }
        else if (t < qe) { lp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, qd, bpGrabTime))); la = takeAng; leg = 2; }
        else if (t < qf) { float u = Seg(t, qe, bpBringTime), e = Ease(u); lp = hip ? AroundBack(takeOut, readyPos, u, false) : Vector2.Lerp(takeOut, readyPos, e) + Arc(e); la = Mathf.LerpAngle(takeAng, gunAng, e); leg = 1; }
        else if (t < qg) { lp = readyPos; la = gunAng; }
        else if (t < qh) { lp = seatedPos; la = gunAng; }
        else { float e = Ease(Seg(t, qh, bpReturnTime)); lp = Vector2.Lerp(G2R(magIn) - Rot(inHand, gunAng), lRest, e); la = Mathf.LerpAngle(gunAng, lRestAng, e); }
        bool lOut = t < qi;
        if (b.main != null) b.main.enabled = !lOut;
        _rlHand2Sprite.enabled = lOut;
        if (b.main != null) _rlHand2Sprite.sprite = b.main.sprite;
        _rlHand2.localPosition = new Vector3(lp.x, lp.y, 0f);
        _rlHand2.localRotation = Quaternion.Euler(0f, 0f, la);
        bool behind = st.Behind(this, lp, leg);
        BpOrder(_rlHand2Sprite, geo, behind);

        // ---- 5. 弹匣 ----
        bool showNew = t >= qd + bpGrabTime * 0.5f && t < qg;
        if (showNew) MarkTaken();
        BpMag(_rlNewMag, lp, la, inHand, 0f, showNew, behind ? st.BehindOrder(1) : geo.newOrder);
        bool holdOld = t >= qa && t < qb + bqFlingTime * 0.35f;            // 甩到一半脱手
        BpMag(_rlOldMag, lp, la, inHand, 0f, holdOld, geo.oldOrder);
        if (!holdOld && t >= qb && !_qrDropped)                            // 甩出去：自己往下掉
        {
            _qrDropped = true;
            Vector2 ob = lp + Rot(inHand, la);
            DropGunMag(_rlOldMag.sprite, geo.oldOrder, ob, la, bqFling, RandIn(bqFlingSpin));
        }
        if (geo.mag != null) geo.mag.enabled = !(t >= qa && t < qg);        // 弹匣离开后枪里没弹匣，插好了再显示
    }

    // ---------------- 战术 ----------------
    void BpTacticalTimes(out float tg, out float t1, out float t2, out float t3, out float t4, out float t5, out float t6, out float t7, out float t8, out float t9, out float end)
    {
        tg = bpFetchTime + bpGrabTime;                 // 右手拿出新弹匣
        t1 = Mathf.Max(bpRaiseTime, tg);               // 枪抬好 + 新弹匣在手里
        t2 = t1 + bpBringTime;                         // 拿着新弹匣抓住旧弹匣
        t3 = t2 + bpPullOldTime;                       // 拔出旧弹匣
        t4 = t3 + bpAlignTime;                         // 新弹匣对准
        t5 = t4 + bpInsertTime;                        // 插到底
        t6 = t5 + bpSlapTime;                          // 拍一下弹匣底
        t7 = t6 + bpStowTime + bpPushTime;             // 旧弹匣放回配件
        t8 = t7 + bpChargeReachTime;                   // 右手抓住拉栓
        t9 = t8 + bpChargePullTime;                    // 拉到底（上膛）
        end = t9 + Mathf.Max(bpReturnTime, Mathf.Max(bpLevelTime, bpChargeShakeTime));
    }

    void BpTactical(float dt)
    {
        _reloadT += dt * _reloadRate;
        float t = _reloadT;
        BpTacticalTimes(out float tg, out float t1, out float t2, out float t3, out float t4, out float t5, out float t6, out float t7, out float t8, out float t9, out float end);
        if (t >= end) { StopGunReload(); ReloadDonePose(); return; }
        GunRig g = _rlGun; GunReloadGeo geo = Geo(g); BullpupGeo b = BGeo(g); ReloadStyle st = _rlStyle;
        BpRaisedGun(t, t6, t9, out Vector2 gunPos, out float gunAng);
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);
        float pull = t < t9 ? Ease(Seg(t, t8, bpChargePullTime)) : 1f - Seg(t, t9, 0.05f);   // 上膛：拉到底就松手弹回
        g.Kick(pull, PPU_FULL);

        Vector2 magIn = geo.magIn, magDir = geo.magDir, inHand = geo.magInHand;
        Vector2 oldInHand = inHand + bpOldOffset;
        Vector2 rRest = G2R(geo.rest); float rRestAng = gunAng + geo.restAng;
        st.TakePose(this, inHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);
        st.StowPose(this, oldInHand, bpOldTilt, out Vector2 stowPos, out Vector2 stowOut, out float stowAng);
        float grabAng = gunAng - bpOldTilt;                                 // 手里"旧弹匣的位置"对上枪里的弹匣
        Vector2 grabPos = G2R(magIn) - Rot(oldInHand, grabAng);
        Vector2 pulledPos = grabPos + Rot(-magDir, gunAng) * bpPullOldDist;
        float readyAng = gunAng - bpNewTilt;
        float depth = t < t4 ? geo.ready : Mathf.Lerp(geo.ready, 0f, Ease(Seg(t, t4, bpInsertTime)));
        Vector2 readyPos = G2R(magIn - magDir * depth) - Rot(inHand, readyAng);
        Vector2 slapPos = G2R(magIn - magDir * SlapDepth(t, t5)) - Rot(inHand, readyAng);
        Vector2 cGrab = G2R(ChargeHandLocal(b, 0f)); float cAng = gunAng + bpChargeAngle;
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));
        bool hip = st is HipPouchReload;                                   // 弹匣在屁股后面：手绕到身后走
        Vector2 hp; float ha; float newTilt = 0f; int leg = 0;
        if (t < bpFetchTime) { float u = Seg(t, 0f, bpFetchTime), e = Ease(u); hp = hip ? AroundBack(rRest, takePos, u, true) : Vector2.Lerp(rRest, takePos, e) + Arc(e); ha = Mathf.LerpAngle(rRestAng, takeAng, e); leg = 1; }
        else if (t < tg) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, bpFetchTime, bpGrabTime))); ha = takeAng; leg = 2; }
        else if (t < t1) { hp = takeOut; ha = takeAng; leg = 2; }
        else if (t < t2) { float u = Seg(t, t1, bpBringTime), e = Ease(u); hp = hip ? AroundBack(takeOut, grabPos, u, false) : Vector2.Lerp(takeOut, grabPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, grabAng, e); newTilt = bpNewTilt * e; leg = 1; }
        else if (t < t3) { hp = Vector2.Lerp(grabPos, pulledPos, Ease(Seg(t, t2, bpPullOldTime))); ha = grabAng; newTilt = bpNewTilt; }
        else if (t < t4) { float e = Ease(Seg(t, t3, bpAlignTime)); hp = Vector2.Lerp(pulledPos, readyPos, e); ha = Mathf.LerpAngle(grabAng, readyAng, e); newTilt = bpNewTilt; }
        else if (t < t5) { hp = readyPos; ha = readyAng; newTilt = bpNewTilt; }
        else if (t < t6) { hp = slapPos; ha = readyAng; }                   // 带着旧弹匣拍一下弹匣底
        else if (t < t6 + bpStowTime) { float u = Seg(t, t6, bpStowTime), e = Ease(u); hp = hip ? AroundBack(slapPos, stowOut, u, true) : Vector2.Lerp(slapPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(readyAng, stowAng, e); leg = 1; }
        else if (t < t7) { hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, t6 + bpStowTime, bpPushTime))); ha = stowAng; leg = 2; }
        else if (t < t8) { float u = Seg(t, t7, bpChargeReachTime), e = Ease(u); hp = hip ? AroundBack(stowPos, cGrab, u, false) : Vector2.Lerp(stowPos, cGrab, e) + Arc(e); ha = Mathf.LerpAngle(stowAng, cAng, e); leg = 1; }
        else if (t < t9) { hp = G2R(ChargeHandLocal(b, pull)); ha = cAng; }
        else { float e = Ease(Seg(t, t9, bpReturnTime)); hp = Vector2.Lerp(G2R(ChargeHandLocal(b, 1f)), rRest, e); ha = Mathf.LerpAngle(cAng, rRestAng, e); }

        bool showNew = t >= bpFetchTime && t < t5;
        if (showNew) MarkTaken();
        if (t >= t7) MarkStowed();
        bool regripped = t >= t9 + bpReturnTime;
        if (geo.hand != null) geo.hand.enabled = regripped;
        _rlHandSprite.enabled = !regripped;
        PlaceReloadHand(geo, hp, ha, st.Behind(this, hp, leg));
        BpMag(_rlNewMag, hp, ha, inHand, newTilt, showNew, _rlNewMag.sortingOrder);
        BpMag(_rlOldMag, hp, ha, oldInHand, bpOldTilt, t >= t2 && t < t7 && !_rlOldStowed, _rlOldMag.sortingOrder);
        if (geo.mag != null) geo.mag.enabled = !(t >= t2 && t < t5);
    }

    // 战术 / 普通共用：枪抬起 80° + 往后退 → 拍弹匣底抖一下 → 上膛拉到底转一下、前后抖 → 放平
    void BpRaisedGun(float t, float tSlapEnd, float tPulled, out Vector2 gunPos, out float gunAng)
    {
        float k = t < tPulled ? Ease(Seg(t, 0f, bpRaiseTime)) : 1f - Ease(Seg(t, tPulled, bpLevelTime));
        float slap = Shake(t - tSlapEnd + bpSlapTime * 0.4f, 0.25f);       // 拍上去的那一刻（从下往上的枪尾抖动）
        float chg = Shake(t - tPulled, bpChargeShakeTime);                  // 拉栓拉到底
        float shA = -bpSlapShake * slap + bpChargeShake * chg;
        Vector2 shP = new Vector2(0f, bpSlapShakeY * slap);
        float aim = _aimAng * (1f - k);
        shP += Rot(new Vector2(bpChargeShakeX * chg, 0f), aim + bpUp * k);  // 前后抖（沿枪的方向）
        BpGun(k, bpUp * k, shA, shP, out gunPos, out gunAng);
    }

    // ---------------- 普通 ----------------
    void BpNormalTimes(ReloadStyle s, out float tA, out float tB, out float t1, out float t2, out float t3, out float t4, out float t5, out float t6, out float t7, out float t8, out float t9, out float tEndHand, out float end)
    {
        tA = bpReachOldTime;                           // 右手抓住旧弹匣（枪同时抬起）
        tB = tA + bpPullOldTime;                       // 拔下来
        t1 = tB + bpStowTime;                          // 带到配件
        t2 = t1 + bpPushTime;                          // 塞进去
        t3 = t2 + s.SwitchTime(this);                  // 手移到拿新弹匣的地方
        t4 = t3 + bpGrabTime;                          // 拿出新弹匣
        t5 = t4 + bpBringTime;                         // 到弹匣口
        t6 = t5 + bpInsertTime;                        // 插到底
        t7 = t6 + bpSlapTime;                          // 拍一下弹匣底
        t8 = t7 + bpChargeReachTime;                   // 右手抓住拉栓
        t9 = t8 + bpChargePullTime;                    // 拉到底（上膛）
        tEndHand = t9 + bpReturnTime;
        end = t9 + Mathf.Max(bpReturnTime, Mathf.Max(bpLevelTime, bpChargeShakeTime));
    }

    void BpNormal(float dt)
    {
        _reloadT += dt * _reloadRate;
        float t = _reloadT;
        ReloadStyle st = _rlStyle;
        BpNormalTimes(st, out float tA, out float tB, out float t1, out float t2, out float t3, out float t4, out float t5, out float t6, out float t7, out float t8, out float t9, out float tEndHand, out float end);
        if (t >= end) { StopGunReload(); ReloadDonePose(); return; }
        GunRig g = _rlGun; GunReloadGeo geo = Geo(g); BullpupGeo b = BGeo(g);
        BpRaisedGun(t, t7, t9, out Vector2 gunPos, out float gunAng);
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);
        float pull = t < t9 ? Ease(Seg(t, t8, bpChargePullTime)) : 1f - Seg(t, t9, 0.05f);
        g.Kick(pull, PPU_FULL);

        Vector2 magIn = geo.magIn, magDir = geo.magDir, inHand = geo.magInHand;
        Vector2 rRest = G2R(geo.rest); float rRestAng = gunAng + geo.restAng;
        st.TakePose(this, inHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);
        st.StowPose(this, inHand, 0f, out Vector2 stowPos, out Vector2 stowOut, out float stowAng);
        Vector2 grabPos = G2R(magIn) - Rot(inHand, gunAng);
        Vector2 pulledPos = grabPos + Rot(-magDir, gunAng) * bpPullOldDist;
        float depth = t < t5 ? geo.ready : Mathf.Lerp(geo.ready, 0f, Ease(Seg(t, t5, bpInsertTime)));
        Vector2 readyPos = G2R(magIn - magDir * depth) - Rot(inHand, gunAng);
        Vector2 slapPos = G2R(magIn - magDir * SlapDepth(t, t6)) - Rot(inHand, gunAng);
        Vector2 cGrab = G2R(ChargeHandLocal(b, 0f)); float cAng = gunAng + bpChargeAngle;
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));
        bool hip = st is HipPouchReload;                                   // 弹匣在屁股后面：手绕到身后走
        Vector2 hp; float ha; int leg = 0;
        if (t < tA) { float e = Ease(Seg(t, 0f, bpReachOldTime)); hp = Vector2.Lerp(rRest, grabPos, e); ha = Mathf.LerpAngle(rRestAng, gunAng, e); }
        else if (t < tB) { hp = Vector2.Lerp(grabPos, pulledPos, Ease(Seg(t, tA, bpPullOldTime))); ha = gunAng; }
        else if (t < t1) { float u = Seg(t, tB, bpStowTime), e = Ease(u); hp = hip ? AroundBack(pulledPos, stowOut, u, true) : Vector2.Lerp(pulledPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(gunAng, stowAng, e); leg = 1; }
        else if (t < t2) { hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, t1, bpPushTime))); ha = stowAng; leg = 2; }
        else if (t < t3) { float e = Ease(Seg(t, t2, t3 - t2)); hp = Vector2.Lerp(stowPos, takePos, e); ha = Mathf.LerpAngle(stowAng, takeAng, e); leg = 2; }
        else if (t < t4) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, t3, bpGrabTime))); ha = takeAng; leg = 2; }
        else if (t < t5) { float u = Seg(t, t4, bpBringTime), e = Ease(u); hp = hip ? AroundBack(takeOut, readyPos, u, false) : Vector2.Lerp(takeOut, readyPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, gunAng, e); leg = 1; }
        else if (t < t6) { hp = readyPos; ha = gunAng; }
        else if (t < t7) { hp = slapPos; ha = gunAng; }
        else if (t < t8) { float e = Ease(Seg(t, t7, bpChargeReachTime)); hp = Vector2.Lerp(slapPos, cGrab, e); ha = Mathf.LerpAngle(gunAng, cAng, e); }
        else if (t < t9) { hp = G2R(ChargeHandLocal(b, pull)); ha = cAng; }
        else { float e = Ease(Seg(t, t9, bpReturnTime)); hp = Vector2.Lerp(G2R(ChargeHandLocal(b, 1f)), rRest, e); ha = Mathf.LerpAngle(cAng, rRestAng, e); }

        if (t >= t2) MarkStowed();
        bool showNew = t >= t3 + bpGrabTime * 0.5f && t < t6;
        if (showNew) MarkTaken();
        bool regripped = t >= tEndHand;
        if (geo.hand != null) geo.hand.enabled = regripped;
        _rlHandSprite.enabled = !regripped;
        PlaceReloadHand(geo, hp, ha, st.Behind(this, hp, leg));
        BpMag(_rlOldMag, hp, ha, inHand, 0f, t >= tA && t < t2 && !_rlOldStowed, _rlOldMag.sortingOrder);
        BpMag(_rlNewMag, hp, ha, inHand, 0f, showNew, _rlNewMag.sortingOrder);
        if (geo.mag != null) geo.mag.enabled = !(t >= tA && t < t6);
    }
}
