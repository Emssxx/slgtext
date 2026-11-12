using UnityEngine;

// ============================================================================
// 步枪取出动画（CharacterRigBase 的一部分，partial；枪械通用：npc1、npc2 和以后的角色都用这一套）
//   ① 辅助手（右手）伸过去抓住挂在身上那把枪的上边 → 抓住的一瞬间，挂着的那把换成能开火的枪
//   ② 抓着枪拿到身体中间（顺便从挂着时的大小变回正常大小）
//   ③ 持枪手（左手）抓住枪托后侧往后一拉，把枪托拉开（震一下）；无托步枪没有能拉的枪托：跳过，左手直接去握把
//   ④ 左手回到握把 → ⑤ 右手松开，枪口因为重力往下一沉，右手回到护木 → ⑥ 回到平时持枪的样子
//   枪决定：抓哪（WeaponDef.drawGrab）、枪托（stockPart / stockGrab / stockCollapse）、枪的中心（mountCenter）
//   角色决定：枪挂在身上哪、斜多少、多大（DrawMountPose），挂着的那把怎么显示 / 藏（ShowMountedGun），平时的两只手（DrawBodyHands）
// ============================================================================
public abstract partial class CharacterRigBase
{
    [Header("步枪取出动画（拿出步枪时播放；所有角色共用，枪挂在身上的位置由角色决定）")]
    public bool rifleDrawAnim = true;                         // 拿出步枪时播不播取出动画（不播 = 直接拿在手上）
    public float rdReachTime = 0.3f;                          // ① 右手伸过去抓挂着的步枪的上边
    // ① 右手抓枪的地方、③ 左手抓枪托的地方 / 枪托收起多少：枪的属性，在 WeaponDef 的"从胸挂上取出"一栏
    public float rdGrabAngle = 180f;                          // ① 右手抓枪时相对枪转多少度：180 = 手倒过来，手指（那个小突起）朝下对准枪身
    public float rdLiftTime = 0.35f;                          // ② 右手抓着枪拿到身体中间
    public Vector2 rdCenterGrip = new Vector2(1060f, 1150f);  // ② 拿到身体中间时握把在哪（角色 PSD 原图坐标）
    public float rdCenterAngle = 15f;                         // ② 拿到身体中间时枪口朝上多少度
    public float rdStockGrabTime = 0.2f;                      // ③ 左手伸到枪托后侧
    public float rdStockPullTime = 0.15f;                     // ③ 往后拉开枪托
    public float rdStockShake = 5f;                           // ③ 枪托拉开时枪震多少度
    public float rdLeftHomeTime = 0.2f;                       // ④ 左手回到握把
    public float rdRightHomeTime = 0.3f;                      // ⑤ 右手松开、回到护木（同时枪口往下一沉）
    public float rdDropAngle = 30f;                           // ⑤ 右手松开时枪口往下沉多少度（重力）
    public float rdSettleTime = 0.3f;                         // ⑥ 回到平时持枪的样子

    protected const float MOUNT_ART_SCALE = 0.81f;            // 角色自己画的挂着的步枪图比能开火的步枪小多少（用部件拼的不缩小 = 1）
    protected float _drawT = -1f;                             // 取出动画进行了多久（-1 = 没在播）
    Transform _drStock;                                       // 这次取出的枪托（无托 = null）
    Vector3 _drStockBase;                                     // 枪托原位

    // 角色 PSD 的根（脚底中心）：rdCenterGrip 按它换算
    protected virtual Vector2 PsdRoot => new Vector2(1161f, 1988f);

    // ---- 角色给的 ----
    // 这把枪挂在身上时：枪的中心在哪（根节点本地坐标）、斜多少度、多大；没有挂着的地方就返回 false（不播取出动画）
    protected virtual bool DrawMountPose(GunRig g, out Vector2 center, out float angle, out float scale)
    { center = Vector2.zero; angle = 0f; scale = 1f; return false; }
    // 挂在身上的那把显示 / 藏起来（抓住的一瞬间换成手上的）
    protected virtual void ShowMountedGun(GunRig g, bool show) { }
    // 平时的两只手显示 / 藏起来（main = 持枪手那边，support = 辅助手那边）
    protected virtual void DrawBodyHands(bool main, bool support) { }
    // 取出动画播完了（比如打开枪上的手电）
    protected virtual void OnGunDrawFinished() { }

    // 开始取出（拿出步枪时角色调）：能播就播，返回 true
    protected bool StartGunDraw()
    {
        GunRig g = _gun;
        if (!rifleDrawAnim || g == null || g.def.hold != HoldType.Rifle) return false;
        if (!DrawMountPose(g, out _, out _, out _)) return false;          // 身上没有挂着这把枪的地方：直接拿在手上
        _drawT = 0f;
        ShowMountedGun(g, true);                                            // 一开始还挂在身上
        foreach (SpriteRenderer sr in g.parts) if (sr != null) sr.enabled = false;   // 手上的先不显示
        DrawBodyHands(true, false);                                         // 左手还是平时的手；右手换成单独动的持枪手
        SpriteRenderer stock = string.IsNullOrEmpty(g.def.stockPart) ? null : g.Part(g.def.stockPart);
        _drStock = stock != null ? stock.transform : null;
        if (_drStock != null)
        {
            _drStockBase = _drStock.localPosition;
            _drStock.localPosition = _drStockBase + new Vector3(g.def.stockCollapse / PPU_FULL, 0f, 0f);   // 枪托先收起来
        }
        return true;
    }

    // 打断取出（换手 / 收枪时调）：枪托、单独动的手、大小都复原
    protected void StopGunDraw()
    {
        if (_drawT < 0f) return;
        _drawT = -1f;
        RestoreDrawPose();
    }

    void RestoreDrawPose()
    {
        if (_drStock != null) { _drStock.localPosition = _drStockBase; _drStock = null; }
        if (_rlHandSprite != null) _rlHandSprite.enabled = false;
        if (_rlHand2Sprite != null) _rlHand2Sprite.enabled = false;
        if (_handPoint != null) _handPoint.localScale = Vector3.one;
    }

    // 播完：平时持枪的样子
    void FinishGunDraw()
    {
        _drawT = -1f;
        RestoreDrawPose();
        if (_gun != null) { ShowMountedGun(_gun, false); _gun.Show(true); }
        DrawBodyHands(false, false);
        OnGunDrawFinished();
    }

    // 各段的时间点（秒）。没有能拉开的枪托：跳过 ③
    void GunDrawTimes(out float tGrab, out float tCenter, out float tStock, out float tPulled, out float tLeftHome, out float tRightHome, out float tEnd)
    {
        bool stock = _drStock != null;
        tGrab = rdReachTime;
        tCenter = tGrab + rdLiftTime;
        tStock = tCenter + (stock ? rdStockGrabTime : 0f);
        tPulled = tStock + (stock ? rdStockPullTime : 0f);
        tLeftHome = tPulled + rdLeftHomeTime;
        tRightHome = tLeftHome + rdRightHomeTime;
        tEnd = tRightHome + rdSettleTime;
    }

    // 每帧（角色的 AnimateHeld 里：_drawT >= 0 时调它，不做别的持枪动作）
    protected void AnimateGunDraw(float dt)
    {
        GunRig g = _gun;
        if (g == null) { StopGunDraw(); return; }
        _drawT += dt;
        float t = _drawT;
        GunDrawTimes(out float tGrab, out float tCenter, out float tStock, out float tPulled, out float tLeftHome, out float tRightHome, out float tEnd);
        if (t >= tEnd || !DrawMountPose(g, out Vector2 mc, out float swapAng, out float mScale)) { FinishGunDraw(); return; }

        // ---- 1. 挂在身上那把现在在哪（随身体晃）：换成手上的枪时从这个位置、角度、大小开始 ----
        WeaponDef wd = g.def;
        Vector2 center = wd.mountCenter != Vector2.zero ? wd.mountCenter : wd.grip;   // 枪的中心
        Vector2 swapPos = mc + Rot((Vector2)CanvasToLocal(wd.grip - center) * mScale, swapAng);   // 握把在哪
        Vector2 centerPos = CanvasToLocal(rdCenterGrip - PsdRoot);         // 拿到身体中间时握把在哪
        Vector2 normalPos = GunAimHand(_aimAng, Vector2.zero); float normalAng = _aimAng;   // 平时持枪（没接敌 = 枪口朝下）

        // ---- 2. 枪的位置 / 角度 / 大小 ----
        Vector2 gunPos; float gunAng, scale = 1f;
        float shStock = Shake(t - tPulled, 0.3f);                          // ③ 枪托拉开：震一下
        if (t < tGrab) { gunPos = swapPos; gunAng = swapAng; scale = mScale; }
        else if (t < tCenter)                                              // ② 拿到身体中间（顺便变回正常大小）
        {
            float e = Ease(Seg(t, tGrab, rdLiftTime));
            gunPos = Vector2.Lerp(swapPos, centerPos, e); gunAng = Mathf.LerpAngle(swapAng, rdCenterAngle, e);
            scale = Mathf.Lerp(mScale, 1f, e);
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

        // ---- 3. 枪托：一开始收起来，③ 被左手往后拉开（拉过头一点再弹回）----
        float stockU = Seg(t, tStock, rdStockPullTime);
        float c1 = 1.70158f, su = stockU - 1f;
        float pulled = stockU <= 0f ? 0f : (stockU >= 1f ? 1f : 1f + (c1 + 1f) * su * su * su + c1 * su * su);
        float stockOff = _drStock != null ? wd.stockCollapse * (1f - pulled) : 0f;   // 枪托往前推了多少（原图像素）
        if (_drStock != null) _drStock.localPosition = _drStockBase + new Vector3(stockOff / PPU_FULL, 0f, 0f);

        // 枪上的两只持枪手（辅助手 = reloadHand，持枪手 = 另一只）
        SpriteRenderer sup = g.Part(wd.reloadHand), main = null;
        foreach (WeaponPart p in wd.parts) if (p != null && p.hand && p.name != wd.reloadHand) { main = g.Part(p.name); break; }

        // ---- 4. 右手：伸过去抓挂着的枪的上边 → 抓着枪 → 松开回护木 ----
        Vector2 grabLocal = CanvasToLocal(wd.drawGrab - wd.grip);          // 右手抓枪的地方（枪上边）
        Vector2 restLocal = sup != null ? (Vector2)sup.transform.localPosition : Vector2.zero;   // 右手平时握的护木
        Vector2 handStart = _handR != null ? (Vector2)_rig.InverseTransformPoint(_handR.position) : gunPos;   // 从身体旁边的手出发
        Vector2 swapGrab = mc + Rot((Vector2)CanvasToLocal(wd.drawGrab - center) * mScale, swapAng);   // 挂着的枪的上边
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
        if (_rlHandSprite != null)
        {
            if (sup != null) { _rlHandSprite.sprite = sup.sprite; _rlHandSprite.sortingOrder = sup.sortingOrder; }
            _rlHandSprite.enabled = !rightHome;
            _rlHand.localPosition = new Vector3(hp.x, hp.y, 0f);
            _rlHand.localRotation = Quaternion.Euler(0f, 0f, ha);
            _rlHand.localScale = Vector3.one;
        }

        // ---- 5. 左手：抓枪托后侧 → 跟着枪托往后拉 → 回到握把（无托：直接伸到握把）----
        EnsureRlHand2();
        Vector2 stockLocal = (Vector2)CanvasToLocal(wd.stockGrab - wd.grip) + new Vector2(stockOff / PPU_FULL, 0f);   // 枪托后侧（跟着枪托动）
        Vector2 leftStart = _handL != null ? (Vector2)_rig.InverseTransformPoint(_handL.position) : gunPos;
        bool leftOut = t >= tCenter && t < tLeftHome;                      // 左手单独动的这段
        Vector2 lp; float la = gunAng;
        if (_drStock == null) { float e = Ease(Seg(t, tCenter, tLeftHome - tCenter)); lp = Vector2.Lerp(leftStart, G(Vector2.zero), e); la = Mathf.LerpAngle(0f, gunAng, e); }
        else if (t < tStock) { float e = Ease(Seg(t, tCenter, rdStockGrabTime)); lp = Vector2.Lerp(leftStart, G(stockLocal), e); la = Mathf.LerpAngle(0f, gunAng, e); }   // ③ 伸到枪托后侧
        else if (t < tPulled) lp = G(stockLocal);                          // ③ 往后拉
        else { float e = Ease(Seg(t, tPulled, rdLeftHomeTime)); lp = Vector2.Lerp(G(stockLocal), G(Vector2.zero), e); }   // ④ 回到握把
        if (main != null) { _rlHand2Sprite.sprite = main.sprite; _rlHand2Sprite.sortingOrder = main.sortingOrder; }
        _rlHand2Sprite.enabled = leftOut;
        _rlHand2.localPosition = new Vector3(lp.x, lp.y, 0f);
        _rlHand2.localRotation = Quaternion.Euler(0f, 0f, la);
        _rlHand2.localScale = Vector3.one;

        // ---- 6. 显示 / 藏：抓住的一瞬间挂着的枪换成手上的；两只手回到枪上以后用枪上的那两只 ----
        bool grabbed = t >= tGrab;
        ShowMountedGun(g, !grabbed);
        foreach (SpriteRenderer sr in g.parts) if (sr != null) sr.enabled = grabbed;
        if (main != null) main.enabled = t >= tLeftHome;                   // 左手回到握把后用枪上的
        if (sup != null) sup.enabled = rightHome;                          // 右手回到护木后用枪上的
        DrawBodyHands(t < tCenter, false);                                 // 平时的左手到 ③ 才换成持枪左手；右手一开始就换了
    }
}
