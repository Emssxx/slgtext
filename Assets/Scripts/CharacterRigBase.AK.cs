using UnityEngine;

// ============================================================================
// AK 系列换弹（CharacterRigBase 的一部分，partial）：弹匣是"摇进去"的 —— 弹匣前端的挂钩先挂上枪的"弹匣挂载旋转点"，
//   再绕着这个点往后一转卡到底；取下也是先绕这个点往前转、再拿下来。（WeaponDef.magRock = 挂载旋转点）
//   左手 = 持枪手（握把），右手 = 辅助手（护木；WeaponDef.reloadHand）。拉栓 = 枪上会往后弹的部件（kick > 0）。
//
//   快速：左手离开握把抓住拉栓往后拉住，枪口上抬 30°、往后退 → 右手去配件拿新弹匣 → 用新弹匣把旧弹匣敲下来
//         （干脆利落，震一下；旧弹匣转着飞出去掉地上）→ 新弹匣的挂载点先对上枪的挂载点 → 绕着它转进去（右手还按在弹匣上）
//         → 左手松开拉栓（拉栓弹回去，震一下）→ 左手回握把 → 右手迅速回护木（震一下）
//   战术：左手把枪抬起 80°、往后退，同时右手去配件拿新弹匣 → 拿着新弹匣抓住旧弹匣，绕挂载点把旧弹匣往前转（枪往受力方向抖）
//         → 取下旧弹匣（两个都在右手）→ 和快速一样把新弹匣摇进去 → 枪口降到 30° → 左手拉栓上膛（抖，有力量感）
//         → 右手把旧弹匣放回配件 → 右手回护木（抖）
//   普通：枪口上抬、往后退 → 右手抓住旧弹匣绕挂载点往前转、取下 → 放回配件 → 拿新弹匣摇进去 → 左手拉栓上膛 → 右手回护木
// ============================================================================
public abstract partial class CharacterRigBase
{
    [Header("AK 系列换弹（时间 = 秒，距离 = 单位，角度 = 度）")]
    public float akRaiseTime = 0.25f;                         // 枪抬起来
    public float akSpeedUp = 30f;                             // 快速：枪口上抬多少度
    public float akTacUp = 80f;                               // 战术：枪口上抬多少度
    public float akNormalUp = 30f;                            // 普通：枪口上抬多少度
    public float akRackUp = 30f;                              // 战术：上膛前枪口降到多少度
    public Vector2 akBack = new Vector2(-0.3f, 0.03f);        // 快速 / 普通换弹时枪往后（x 负）/ 往上收多少（单位）
    public Vector2 akTacBack = new Vector2(-0.15f, 0.03f);    // 战术换弹时枪往后 / 往上收多少（枪抬得高，退一半就够）
    public float akAroundOut = 0.25f;                         // 右手去 / 从身后配件：先往前（x 正方向）绕出去多远，再从身体另一边绕到身后（单位）
    public float akFetchTime = 0.3f;                          // 右手伸到配件
    public float akGrabTime = 0.12f;                          // 从配件里拿出新弹匣
    public float akBringTime = 0.25f;                         // 拿着新弹匣到枪旁边
    public float akSeekTime = 0.14f;                          // 新弹匣的挂载点去找枪的挂载点（先对准）
    public Vector2 akSeekOffset = new Vector2(0.02f, -0.05f); // 找的时候先停在挂载点前下方多远（枪的本地坐标）
    public float akAlignTime = 0.08f;                         // 挂载点对上
    public float akRockIn = 28f;                              // 新弹匣挂上时先往前斜多少度（然后转进去）
    public float akRockTime = 0.1f;                           // 绕挂载点转进去
    public float akInsertShake = 6f;                          // 卡到底那一下枪抖多少度
    public float akHoldTime = 0.06f;                          // 卡到底后右手按住多久再继续
    public float akReturnTime = 0.18f;                        // 右手回护木（迅速）
    public float akReturnShake = 4f;                          // 右手回到护木时枪抖多少度
    public float akLevelTime = 0.25f;                         // 枪放平（回到平时持枪）

    [Header("AK 系列换弹：拉栓（左手）")]
    public float akChargeReachTime = 0.18f;                   // 左手离开握把伸到拉栓
    public float akChargePullTime = 0.12f;                    // 拉栓往后拉到底
    public Vector2 akChargeGrip = new Vector2(0.055f, 0.008f);   // 左手抓拉栓时手心相对拉栓中心（枪的本地坐标；AK 的拉机柄在拉栓前端）
    public float akChargeAngle = 0f;                          // 左手抓拉栓时多转多少度
    public float akBoltShake = 8f;                            // 拉栓弹回去（上膛）那一下枪抖多少度
    public float akBoltShakeX = 0.025f;                       // 拉栓弹回去时枪前后抖多远
    public float akLeftHomeTime = 0.15f;                      // 左手回握把

    [Header("AK 系列换弹：取下旧弹匣")]
    public float akKnockTime = 0.08f;                         // 快速：新弹匣敲旧弹匣（干脆利落）
    public Vector2 akKnockFrom = new Vector2(-0.09f, -0.03f); // 快速：敲之前新弹匣停在旧弹匣后面多远（弹匣底相对旧弹匣底，枪的本地坐标）
    public Vector2 akKnockTo = new Vector2(-0.015f, -0.005f); // 快速：敲到时新弹匣在哪
    public float akKnockShake = 6f;                           // 快速：敲下来那一下枪抖多少度
    public float akKnockRock = 35f;                           // 快速：旧弹匣被敲得往前转多少度再飞出去
    public Vector2 akKnockFling = new Vector2(0.6f, -0.4f);   // 快速：旧弹匣飞出去的速度（枪的本地方向：x 往前，y 往上；单位 / 秒）
    public Vector2 akKnockSpin = new Vector2(400f, 1000f);    // 快速：旧弹匣转多快（度 / 秒，随机方向）
    public float akReachOldTime = 0.25f;                      // 普通：右手伸去抓旧弹匣
    public float akRockOut = 30f;                             // 战术 / 普通：旧弹匣绕挂载点往前转多少度
    public float akRockOutTime = 0.15f;                       // 往前转用几秒
    public float akRockShake = 4f;                            // 往前转时枪往受力方向抖多少度
    public float akPullOldTime = 0.12f;                       // 取下旧弹匣
    public float akPullOldDist = 0.12f;                       // 取下时拉多远
    public Vector2 akOldOffset = new Vector2(-0.03f, 0f);     // 战术：旧弹匣在手里的位置（和新弹匣并排）
    public float akOldTilt = 25f;                             // 战术：旧弹匣在手里歪多少
    public float akNewTilt = -10f;                            // 战术：抓旧弹匣时新弹匣在手里歪多少
    public float akStowTime = 0.28f;                          // 带着旧弹匣到配件
    public float akPushTime = 0.12f;                          // 把旧弹匣塞进去

    // ---------------- 几何 ----------------
    // 弹匣绕挂载点转了 phi 度时，弹匣底在哪（枪的本地坐标）。rock = 挂载点，seated = 插好时的弹匣底
    static Vector2 AkBottom(Vector2 rock, Vector2 seated, float phi) => rock + Rot(seated - rock, phi);

    // 右手去 / 从身后的配件：从 x 正方向（身体前面）绕出去，再从身体另一边绕到身后（不往 x 负方向直接穿过去）
    //   u = 这一段的进度（0~1，不用先 Ease）；走的是一条往前鼓出去的弧线（二次贝塞尔曲线）
    Vector2 AkAround(Vector2 from, Vector2 to, float u)
    {
        float e = Ease(u);
        Vector2 c = new Vector2(Mathf.Max(from.x, to.x) + akAroundOut, (from.y + to.y) * 0.5f);   // 弧线往前鼓到的控制点
        float v = 1f - e;
        return v * v * from + 2f * v * e * c + e * e * to;                  // 二次贝塞尔：from → (往前绕) → to
    }

    // 左手抓拉栓、拉到 s（0 = 原位，1 = 最后面）时手在哪（枪的本地坐标）
    Vector2 AkChargeHand(BullpupGeo b, float s) => b.chargerAt + new Vector2(-b.chargerKick * s / PPU_FULL, 0f) + akChargeGrip;

    // 弹匣直接摆在某个位置（不在手里：比如还插在枪里被敲得转起来）
    void AkPlaceMag(SpriteRenderer mag, Vector2 bottom, float ang, bool show, int order)
    {
        mag.enabled = show;
        if (!show) return;
        mag.transform.localPosition = new Vector3(bottom.x, bottom.y, 0f);
        mag.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
        mag.sortingOrder = order;
    }

    // 枪：瞄准角度渐渐转回正前方 + 抬起 + 往后退 + 抖动（k = 换弹姿势的程度 0~1）
    void AkGun(float k, float up, float shakeAng, Vector2 shakePos, Vector2 back, out Vector2 gunPos, out float gunAng)
    {
        float aimNow = _aimAng * (1f - k);
        gunPos = ReloadAimHand(aimNow, 1f - k) + back * k + shakePos;
        gunAng = aimNow + up + shakeAng;
        Vector2 tr = Grip.Offset(_time, out float trA);                    // 持枪手类型：颤抖的手换弹时也抖
        gunPos += tr; gunAng += trA;
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
    }

    // 左手：握把 ↔ 拉栓（tReach 开始伸过去，tPull 开始拉，tRel 松开，tHome 回到握把）
    void AkLeftHand(float t, BullpupGeo b, GunReloadGeo geo, System.Func<Vector2, Vector2> G2R, float gunAng,
                    float tReach, float tPull, float tRel, float tHome, float pull)
    {
        EnsureRlHand2();
        Vector2 lRest = G2R(b.mainRest); float lRestAng = gunAng + b.mainRestAng;
        float cAng = gunAng + akChargeAngle;
        Vector2 lp; float la;
        if (t < tReach) { lp = lRest; la = lRestAng; }
        else if (t < tPull) { float e = Ease(Seg(t, tReach, tPull - tReach)); lp = Vector2.Lerp(lRest, G2R(AkChargeHand(b, 0f)), e); la = Mathf.LerpAngle(lRestAng, cAng, e); }
        else if (t < tRel) { lp = G2R(AkChargeHand(b, pull)); la = cAng; }
        else { float e = Ease(Seg(t, tRel, tHome - tRel)); lp = Vector2.Lerp(G2R(AkChargeHand(b, 0f)), lRest, e); la = Mathf.LerpAngle(cAng, lRestAng, e); }
        bool lOut = t >= tReach && t < tHome;
        if (b.main != null) b.main.enabled = !lOut;
        _rlHand2Sprite.enabled = lOut;
        if (b.main != null) _rlHand2Sprite.sprite = b.main.sprite;
        _rlHand2.localPosition = new Vector3(lp.x, lp.y, 0f);
        _rlHand2.localRotation = Quaternion.Euler(0f, 0f, la);
        BpOrder(_rlHand2Sprite, geo, false);
    }

    // 新弹匣摇进去的三段：找挂载点 → 对上 → 绕着转进去。返回手的位置 / 角度（弹匣底在 inHand）
    void AkInsertPose(float t, float tSeek, float tAlign, float tRock, Vector2 from, float fromAng,
                      Vector2 rock, Vector2 seated, Vector2 inHand, float gunAng, System.Func<Vector2, Vector2> G2R,
                      out Vector2 hp, out float ha)
    {
        float aIn = gunAng + akRockIn;                                      // 挂上时弹匣往前斜
        Vector2 seekB = G2R(AkBottom(rock + akSeekOffset, seated + akSeekOffset, akRockIn));   // 挂载点停在前下方
        Vector2 alignB = G2R(AkBottom(rock, seated, akRockIn));             // 挂载点对上
        Vector2 seekHand = seekB - Rot(inHand, aIn), alignHand = alignB - Rot(inHand, aIn);
        if (t < tSeek) { float e = Ease(Seg(t, tSeek - akSeekTime, akSeekTime)); hp = Vector2.Lerp(from, seekHand, e); ha = Mathf.LerpAngle(fromAng, aIn, e); }
        else if (t < tAlign) { hp = Vector2.Lerp(seekHand, alignHand, Ease(Seg(t, tSeek, akAlignTime))); ha = aIn; }
        else
        {
            float phi = Mathf.Lerp(akRockIn, 0f, Ease(Seg(t, tAlign, akRockTime)));   // 绕挂载点转进去
            Vector2 bw = G2R(AkBottom(rock, seated, phi));
            ha = gunAng + phi; hp = bw - Rot(inHand, ha);
        }
    }

    void AkReload(float dt)
    {
        if (_reloadKind == ReloadKind.Speed) AkSpeed(dt);
        else if (_reloadKind == ReloadKind.Tactical) AkTactical(dt);
        else AkNormal(dt);
    }

    float AkReloadTotal(ReloadStyle s, ReloadKind kind)
    {
        float end;
        if (kind == ReloadKind.Speed) AkSpeedTimes(out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out end);
        else if (kind == ReloadKind.Tactical) AkTacticalTimes(out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out end);
        else AkNormalTimes(s, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out end);
        return Mathf.Max(0.05f, end);
    }

    // ---------------- 快速 ----------------
    void AkSpeedTimes(out float a1, out float a2, out float r1, out float r2, out float r3, out float r4, out float r5, out float r6, out float r7,
                      out float rel, out float l3, out float rr, out float end)
    {
        a1 = akChargeReachTime;                        // 左手到拉栓
        a2 = a1 + akChargePullTime;                    // 拉到底（一直拉着）
        r1 = akFetchTime;                              // 右手到配件
        r2 = r1 + akGrabTime;                          // 拿出新弹匣
        r3 = Mathf.Max(r2 + akBringTime, a2);          // 新弹匣停在旧弹匣后面（拉栓也拉好了）
        r4 = r3 + akKnockTime;                         // 敲下旧弹匣
        r5 = r4 + akSeekTime;                          // 找挂载点
        r6 = r5 + akAlignTime;                         // 对上
        r7 = r6 + akRockTime;                          // 转进去卡到底
        rel = r7 + akHoldTime;                         // 左手松开拉栓
        l3 = rel + akLeftHomeTime;                     // 左手回握把
        rr = l3 + akReturnTime;                        // 右手回护木
        end = Mathf.Max(rr + 0.15f, l3 + akLevelTime);
    }

    void AkSpeed(float dt)
    {
        _reloadT += dt * _reloadRate;
        float t = _reloadT;
        AkSpeedTimes(out float a1, out float a2, out float r1, out float r2, out float r3, out float r4, out float r5, out float r6, out float r7,
                     out float rel, out float l3, out float rr, out float end);
        if (t >= end) { StopGunReload(); ReloadDonePose(); return; }
        GunRig g = _rlGun; GunReloadGeo geo = Geo(g); BullpupGeo b = BGeo(g); ReloadStyle st = _rlStyle; WeaponDef d = g.def;
        Vector2 rock = CanvasToLocal(d.magRock - d.grip), seated = geo.magIn, inHand = geo.magInHand;

        // ---- 1. 枪：上抬 30°、往后退；敲 / 卡到底 / 拉栓弹回 / 右手回位各抖一下 ----
        float k = t < l3 ? Ease(Seg(t, 0f, akRaiseTime)) : 1f - Ease(Seg(t, l3, akLevelTime));
        float bolt = Shake(t - rel, 0.3f);
        float shA = akKnockShake * Shake(t - r4, 0.22f) - akInsertShake * Shake(t - r7, 0.25f) + akBoltShake * bolt - akReturnShake * Shake(t - rr, 0.22f);
        float aimNow = _aimAng * (1f - k);
        Vector2 shP = Rot(new Vector2(akBoltShakeX * bolt, 0f), aimNow + akSpeedUp * k);
        AkGun(k, akSpeedUp * k, shA, shP, akBack, out Vector2 gunPos, out float gunAng);
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);

        // ---- 2. 拉栓：左手拉到底一直拉着，松开时弹回去 ----
        float pull = t < rel ? Ease(Seg(t, a1, akChargePullTime)) : 1f - Seg(t, rel, 0.05f);
        g.Kick(pull, PPU_FULL);
        AkLeftHand(t, b, geo, G2R, gunAng, 0f, a1, rel, l3, pull);

        // ---- 3. 右手：护木 → 配件拿新弹匣 → 停在旧弹匣后面 → 敲 → 找挂载点 → 转进去 → 按住 → 回护木 ----
        Vector2 rRest = G2R(geo.rest); float rRestAng = gunAng + geo.restAng;
        st.TakePose(this, inHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);
        bool hip = st is HipPouchReload;
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));
        Vector2 knockFrom = G2R(seated + akKnockFrom) - Rot(inHand, gunAng);
        Vector2 knockTo = G2R(seated + akKnockTo) - Rot(inHand, gunAng);
        Vector2 seatHand = G2R(seated) - Rot(inHand, gunAng);
        Vector2 hp; float ha; int leg = 0;
        if (t < r1) { float u = Seg(t, 0f, akFetchTime), e = Ease(u); hp = hip ? AkAround(rRest, takePos, u) : Vector2.Lerp(rRest, takePos, e) + Arc(e); ha = Mathf.LerpAngle(rRestAng, takeAng, e); leg = 1; }
        else if (t < r2) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, r1, akGrabTime))); ha = takeAng; leg = 2; }
        else if (t < r3) { float u = Seg(t, r2, r3 - r2), e = Ease(u); hp = hip ? AkAround(takeOut, knockFrom, u) : Vector2.Lerp(takeOut, knockFrom, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, gunAng, e); leg = 1; }
        else if (t < r4) { float u = Seg(t, r3, akKnockTime); hp = Vector2.Lerp(knockFrom, knockTo, u * u); ha = gunAng; }   // 越来越快：敲上去
        else if (t < r7) AkInsertPose(t, r5, r6, r7, knockTo, gunAng, rock, seated, inHand, gunAng, G2R, out hp, out ha);
        else if (t < l3) { hp = seatHand; ha = gunAng; }                    // 按住弹匣（左手松拉栓、回握把）
        else { float e = Ease(Seg(t, l3, akReturnTime)); hp = Vector2.Lerp(seatHand, rRest, e); ha = Mathf.LerpAngle(gunAng, rRestAng, e); }
        bool rOut = t < rr;
        if (geo.hand != null) geo.hand.enabled = !rOut;
        _rlHandSprite.enabled = rOut;
        if (geo.hand != null) _rlHandSprite.sprite = geo.hand.sprite;
        bool behind = st.Behind(this, hp, leg);
        PlaceReloadHand(geo, hp, ha, behind);

        // ---- 4. 弹匣：新弹匣在右手里；旧弹匣被敲得往前转，飞出去 ----
        bool showNew = t >= r1 + akGrabTime * 0.5f && t < r7;
        if (showNew) MarkTaken();
        BpMag(_rlNewMag, hp, ha, inHand, 0f, showNew, _rlNewMag.sortingOrder);
        float hitT = r4 - akKnockTime * 0.2f;                               // 敲到的那一刻
        float flyT = hitT + 0.05f;                                          // 转一下就飞出去
        bool rocking = t >= hitT && t < flyT;
        float phiOld = akKnockRock * Ease(Seg(t, hitT, flyT - hitT));
        AkPlaceMag(_rlOldMag, G2R(AkBottom(rock, seated, phiOld)), gunAng + phiOld, rocking, geo.oldOrder);
        if (t >= flyT && !_qrDropped)
        {
            _qrDropped = true;
            Vector2 vel = Rot(akKnockFling, gunAng);                        // 往枪的前下方飞
            float spin = RandIn(akKnockSpin) * (Random.value < 0.5f ? -1f : 1f);
            DropGunMag(_rlOldMag.sprite, geo.oldOrder, G2R(AkBottom(rock, seated, akKnockRock)), gunAng + akKnockRock, vel, spin);
        }
        if (geo.mag != null) geo.mag.enabled = !(t >= hitT && t < r7);    // 旧弹匣掉了、新弹匣卡到底之前：枪里没弹匣
    }

    // ---------------- 战术 ----------------
    void AkTacticalTimes(out float f1, out float f2, out float t1, out float t2, out float t3, out float t4, out float t5, out float t6,
                         out float t7, out float t8, out float t9, out float t10, out float t11, out float t12, out float t13, out float end)
    {
        f1 = akFetchTime;                              // 右手到配件
        f2 = f1 + akGrabTime;                          // 拿出新弹匣
        t1 = Mathf.Max(f2, akRaiseTime) + akBringTime; // 拿着新弹匣抓住旧弹匣
        t2 = t1 + akRockOutTime;                       // 旧弹匣绕挂载点往前转
        t3 = t2 + akPullOldTime;                       // 取下来
        t4 = t3 + akSeekTime;                          // 新弹匣找挂载点
        t5 = t4 + akAlignTime;                         // 对上
        t6 = t5 + akRockTime;                          // 转进去卡到底
        t7 = t6 + akRaiseTime;                         // 枪口降到 30°
        t8 = t7 + akChargeReachTime;                   // 左手到拉栓
        t9 = t8 + akChargePullTime;                    // 拉到底 → 松开（上膛）
        t10 = t9 + akLeftHomeTime;                     // 左手回握把
        t11 = t9 + akStowTime;                         // 右手带着旧弹匣到配件
        t12 = t11 + akPushTime;                        // 塞进去
        t13 = t12 + akReturnTime;                      // 右手回护木
        end = Mathf.Max(t13 + 0.15f, t10 + akLevelTime);
    }

    void AkTactical(float dt)
    {
        _reloadT += dt * _reloadRate;
        float t = _reloadT;
        AkTacticalTimes(out float f1, out float f2, out float t1, out float t2, out float t3, out float t4, out float t5, out float t6,
                        out float t7, out float t8, out float t9, out float t10, out float t11, out float t12, out float t13, out float end);
        if (t >= end) { StopGunReload(); ReloadDonePose(); return; }
        GunRig g = _rlGun; GunReloadGeo geo = Geo(g); BullpupGeo b = BGeo(g); ReloadStyle st = _rlStyle; WeaponDef d = g.def;
        Vector2 rock = CanvasToLocal(d.magRock - d.grip), seated = geo.magIn, inHand = geo.magInHand;
        Vector2 oldInHand = inHand + akOldOffset;

        // ---- 1. 枪：抬起 80°、往后退 → 卡到底后降到 30° → 上膛 → 放平 ----
        float level = Mathf.Max(t10, t13);
        float k = t < level ? Ease(Seg(t, 0f, akRaiseTime)) : 1f - Ease(Seg(t, level, akLevelTime));
        float up = Mathf.Lerp(akTacUp, akRackUp, Ease(Seg(t, t6, akRaiseTime))) * k;
        float bolt = Shake(t - t9, 0.3f);
        float shA = akRockShake * Shake(t - t1, akRockOutTime + 0.1f) - akInsertShake * Shake(t - t6, 0.25f) + akBoltShake * bolt - akReturnShake * Shake(t - t13, 0.22f);
        float aimNow = _aimAng * (1f - k);
        Vector2 shP = Rot(new Vector2(akBoltShakeX * bolt, 0f), aimNow + up);
        AkGun(k, up, shA, shP, akTacBack, out Vector2 gunPos, out float gunAng);
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);

        // ---- 2. 拉栓（左手上膛：拉到底就松手弹回）----
        float pull = t < t9 ? Ease(Seg(t, t8, akChargePullTime)) : 1f - Seg(t, t9, 0.05f);
        g.Kick(pull, PPU_FULL);
        AkLeftHand(t, b, geo, G2R, gunAng, t7, t8, t9, t10, pull);

        // ---- 3. 右手 ----
        Vector2 rRest = G2R(geo.rest); float rRestAng = gunAng + geo.restAng;
        st.TakePose(this, inHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);
        st.StowPose(this, oldInHand, akOldTilt, out Vector2 stowPos, out Vector2 stowOut, out float stowAng);
        bool hip = st is HipPouchReload;
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));
        float phiOld = t < t1 ? 0f : akRockOut * Ease(Seg(t, t1, akRockOutTime));   // 旧弹匣绕挂载点往前转
        float oldAng = gunAng + phiOld;
        float grabAng = oldAng - akOldTilt;                                 // 手里"旧弹匣的位置"对上枪里的旧弹匣
        Vector2 oldB = G2R(AkBottom(rock, seated, phiOld));
        Vector2 grabPos = oldB - Rot(oldInHand, grabAng);
        Vector2 pulledPos = grabPos + Rot(new Vector2(0.4f, -1f).normalized, oldAng) * akPullOldDist;   // 往前下方拿下来
        Vector2 seatHand = G2R(seated) - Rot(inHand, gunAng);
        Vector2 hp; float ha; float newTilt = 0f; int leg = 0;
        if (t < f1) { float u = Seg(t, 0f, akFetchTime), e = Ease(u); hp = hip ? AkAround(rRest, takePos, u) : Vector2.Lerp(rRest, takePos, e) + Arc(e); ha = Mathf.LerpAngle(rRestAng, takeAng, e); leg = 1; }
        else if (t < f2) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, f1, akGrabTime))); ha = takeAng; leg = 2; }
        else if (t < t1 - akBringTime) { hp = takeOut; ha = takeAng; leg = 2; }
        else if (t < t1) { float u = Seg(t, t1 - akBringTime, akBringTime), e = Ease(u); hp = hip ? AkAround(takeOut, grabPos, u) : Vector2.Lerp(takeOut, grabPos, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, grabAng, e); newTilt = akNewTilt * e; leg = 1; }
        else if (t < t2) { hp = grabPos; ha = grabAng; newTilt = akNewTilt; }     // 跟着旧弹匣往前转
        else if (t < t3) { hp = Vector2.Lerp(grabPos, pulledPos, Ease(Seg(t, t2, akPullOldTime))); ha = grabAng; newTilt = akNewTilt; }
        else if (t < t6) { AkInsertPose(t, t4, t5, t6, pulledPos, grabAng, rock, seated, inHand, gunAng, G2R, out hp, out ha); newTilt = akNewTilt * (1f - Ease(Seg(t, t3, akSeekTime))); }
        else if (t < t9) { hp = seatHand; ha = gunAng; }                    // 按住新弹匣（旧弹匣也在手里），等左手上膛
        else if (t < t11) { float u = Seg(t, t9, akStowTime), e = Ease(u); hp = hip ? AkAround(seatHand, stowOut, u) : Vector2.Lerp(seatHand, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(gunAng, stowAng, e); leg = 1; }
        else if (t < t12) { hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, t11, akPushTime))); ha = stowAng; leg = 2; }
        else { float u = Seg(t, t12, akReturnTime), e = Ease(u); hp = hip ? AkAround(stowPos, rRest, u) : Vector2.Lerp(stowPos, rRest, e); ha = Mathf.LerpAngle(stowAng, rRestAng, e); leg = 1; }
        bool rOut = t < t13;
        if (geo.hand != null) geo.hand.enabled = !rOut;
        _rlHandSprite.enabled = rOut;
        if (geo.hand != null) _rlHandSprite.sprite = geo.hand.sprite;
        PlaceReloadHand(geo, hp, ha, st.Behind(this, hp, leg));

        // ---- 4. 弹匣 ----
        bool showNew = t >= f1 + akGrabTime * 0.5f && t < t6;
        if (showNew) MarkTaken();
        if (t >= t12) MarkStowed();
        BpMag(_rlNewMag, hp, ha, inHand, newTilt, showNew, _rlNewMag.sortingOrder);
        BpMag(_rlOldMag, hp, ha, oldInHand, akOldTilt, t >= t1 && t < t12 && !_rlOldStowed, _rlOldMag.sortingOrder);
        if (geo.mag != null) geo.mag.enabled = !(t >= t1 && t < t6);
    }

    // ---------------- 普通 ----------------
    void AkNormalTimes(ReloadStyle s, out float n1, out float n2, out float n3, out float n4, out float n5, out float n6, out float n7,
                       out float n8, out float n9, out float n10, out float n11, out float n12, out float n13, out float n14, out float end)
    {
        n1 = Mathf.Max(akReachOldTime, akRaiseTime);   // 右手抓住旧弹匣（枪也抬好了）
        n2 = n1 + akRockOutTime;                       // 绕挂载点往前转
        n3 = n2 + akPullOldTime;                       // 取下来
        n4 = n3 + akStowTime;                          // 带到配件
        n5 = n4 + akPushTime;                          // 塞进去
        n6 = n5 + s.SwitchTime(this);                  // 手移到拿新弹匣的地方
        n7 = n6 + akGrabTime;                          // 拿出新弹匣
        n8 = n7 + akBringTime + akSeekTime;            // 带过去、找挂载点
        n9 = n8 + akAlignTime;                         // 对上
        n10 = n9 + akRockTime;                         // 转进去卡到底
        n11 = n10 + akChargeReachTime;                 // 左手到拉栓
        n12 = n11 + akChargePullTime;                  // 拉到底 → 松开（上膛）
        n13 = n12 + akLeftHomeTime;                    // 左手回握把
        n14 = n13 + akReturnTime;                      // 右手回护木
        end = Mathf.Max(n14 + 0.15f, n13 + akLevelTime);
    }

    void AkNormal(float dt)
    {
        _reloadT += dt * _reloadRate;
        float t = _reloadT;
        ReloadStyle st = _rlStyle;
        AkNormalTimes(st, out float n1, out float n2, out float n3, out float n4, out float n5, out float n6, out float n7,
                      out float n8, out float n9, out float n10, out float n11, out float n12, out float n13, out float n14, out float end);
        if (t >= end) { StopGunReload(); ReloadDonePose(); return; }
        GunRig g = _rlGun; GunReloadGeo geo = Geo(g); BullpupGeo b = BGeo(g); WeaponDef d = g.def;
        Vector2 rock = CanvasToLocal(d.magRock - d.grip), seated = geo.magIn, inHand = geo.magInHand;

        // ---- 1. 枪 ----
        float k = t < n13 ? Ease(Seg(t, 0f, akRaiseTime)) : 1f - Ease(Seg(t, n13, akLevelTime));
        float bolt = Shake(t - n12, 0.3f);
        float shA = akRockShake * Shake(t - n1, akRockOutTime + 0.1f) - akInsertShake * Shake(t - n10, 0.25f) + akBoltShake * bolt - akReturnShake * Shake(t - n14, 0.22f);
        float aimNow = _aimAng * (1f - k);
        Vector2 shP = Rot(new Vector2(akBoltShakeX * bolt, 0f), aimNow + akNormalUp * k);
        AkGun(k, akNormalUp * k, shA, shP, akBack, out Vector2 gunPos, out float gunAng);
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);

        // ---- 2. 拉栓（左手上膛）----
        float pull = t < n12 ? Ease(Seg(t, n11, akChargePullTime)) : 1f - Seg(t, n12, 0.05f);
        g.Kick(pull, PPU_FULL);
        AkLeftHand(t, b, geo, G2R, gunAng, n10, n11, n12, n13, pull);

        // ---- 3. 右手 ----
        Vector2 rRest = G2R(geo.rest); float rRestAng = gunAng + geo.restAng;
        st.TakePose(this, inHand, 0f, out Vector2 takePos, out Vector2 takeOut, out float takeAng);
        st.StowPose(this, inHand, 0f, out Vector2 stowPos, out Vector2 stowOut, out float stowAng);
        bool hip = st is HipPouchReload;
        System.Func<float, Vector2> Arc = e => new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));
        float phiOld = t < n1 ? 0f : akRockOut * Ease(Seg(t, n1, akRockOutTime));
        float oldAng = gunAng + phiOld;
        Vector2 grabPos = G2R(AkBottom(rock, seated, phiOld)) - Rot(inHand, oldAng);
        Vector2 grab0 = G2R(seated) - Rot(inHand, gunAng);
        Vector2 pulledPos = grabPos + Rot(new Vector2(0.4f, -1f).normalized, oldAng) * akPullOldDist;
        Vector2 seatHand = grab0;
        Vector2 hp; float ha; int leg = 0;
        if (t < n1) { float e = Ease(Seg(t, 0f, n1)); hp = Vector2.Lerp(rRest, grab0, e); ha = Mathf.LerpAngle(rRestAng, gunAng, e); }
        else if (t < n2) { hp = grabPos; ha = oldAng; }                     // 跟着旧弹匣往前转
        else if (t < n3) { hp = Vector2.Lerp(grabPos, pulledPos, Ease(Seg(t, n2, akPullOldTime))); ha = oldAng; }
        else if (t < n4) { float u = Seg(t, n3, akStowTime), e = Ease(u); hp = hip ? AkAround(pulledPos, stowOut, u) : Vector2.Lerp(pulledPos, stowOut, e) + Arc(e); ha = Mathf.LerpAngle(oldAng, stowAng, e); leg = 1; }
        else if (t < n5) { hp = Vector2.Lerp(stowOut, stowPos, Ease(Seg(t, n4, akPushTime))); ha = stowAng; leg = 2; }
        else if (t < n6) { float e = Ease(Seg(t, n5, n6 - n5)); hp = Vector2.Lerp(stowPos, takePos, e); ha = Mathf.LerpAngle(stowAng, takeAng, e); leg = 2; }
        else if (t < n7) { hp = Vector2.Lerp(takePos, takeOut, Ease(Seg(t, n6, akGrabTime))); ha = takeAng; leg = 2; }
        else if (t < n8 - akSeekTime) { float u = Seg(t, n7, akBringTime), e = Ease(u); Vector2 near = G2R(seated + akSeekOffset * 2f) - Rot(inHand, gunAng + akRockIn); hp = hip ? AkAround(takeOut, near, u) : Vector2.Lerp(takeOut, near, e) + Arc(e); ha = Mathf.LerpAngle(takeAng, gunAng + akRockIn, e); leg = 1; }
        else if (t < n10)
        {
            Vector2 near = G2R(seated + akSeekOffset * 2f) - Rot(inHand, gunAng + akRockIn);
            AkInsertPose(t, n8, n9, n10, near, gunAng + akRockIn, rock, seated, inHand, gunAng, G2R, out hp, out ha);
        }
        else if (t < n13) { hp = seatHand; ha = gunAng; }                   // 按住新弹匣，等左手上膛
        else { float e = Ease(Seg(t, n13, akReturnTime)); hp = Vector2.Lerp(seatHand, rRest, e); ha = Mathf.LerpAngle(gunAng, rRestAng, e); }
        bool rOut = t < n14;
        if (geo.hand != null) geo.hand.enabled = !rOut;
        _rlHandSprite.enabled = rOut;
        if (geo.hand != null) _rlHandSprite.sprite = geo.hand.sprite;
        PlaceReloadHand(geo, hp, ha, st.Behind(this, hp, leg));

        // ---- 4. 弹匣 ----
        if (t >= n5) MarkStowed();
        bool showNew = t >= n6 + akGrabTime * 0.5f && t < n10;
        if (showNew) MarkTaken();
        BpMag(_rlOldMag, hp, ha, inHand, 0f, t >= n1 && t < n5 && !_rlOldStowed, _rlOldMag.sortingOrder);
        BpMag(_rlNewMag, hp, ha, inHand, 0f, showNew, _rlNewMag.sortingOrder);
        if (geo.mag != null) geo.mag.enabled = !(t >= n1 && t < n10);
    }
}
