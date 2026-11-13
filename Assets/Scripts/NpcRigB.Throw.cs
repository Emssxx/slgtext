using UnityEngine;

// ============================================================================
// npc2 扔手雷（NpcRigB 的一部分）：腰上挂着两颗（Grenade1 / Grenade2），扔一颗少一颗。
//   拿着枪：辅助手离开枪（枪单手放低），伸到腰上拿手雷 → 拿到胸前拔环（手一扯）→ 往后上方抡到头后面
//          → 猛地从头顶往前甩出去（出手）→ 顺势往前下方收 → 回到枪上。空手也一样，用前面那只手。
//   身体跟着动：抡的时候往后仰，甩的时候往前压。
//   手和手雷：用持枪左手（衣服的手，和握枪一样的握法）竖着握住手雷，拉环 / 握片露在拳头上面（thGrenadeInHand 调对齐）。
//   出手那一刻调 onRelease（游戏里 = AbaOperative 生成飞出去的手雷）；测试时没给就直接扔一颗不算谁的。
// 位置都是角色 PSD 坐标（和上面的 P_ 常量一样）：手心在哪；角度 = 手从"平着朝前"（和握枪一样）转多少度（正 = 往上抬）。
// ============================================================================
public partial class NpcRigB
{
    [Header("扔手雷（时间 = 秒；位置 = 角色 PSD 坐标，手心在哪；角度 = 手从平着朝前往上抬多少度）")]
    public float thReachTime = 0.22f;                          // ① 手伸到腰上的手雷
    public float thGrabTime = 0.08f;                           // ② 抓住
    public float thChestTime = 0.18f;                          // ③ 拿到胸前
    public float thPinTime = 0.14f;                            // ④ 拔环（手往下一扯）
    public float thWindTime = 0.22f;                           // ⑤ 往后上方抡到头后面
    public float thThrowTime = 0.11f;                          // ⑥ 从头顶甩出去（越短越猛）
    public float thFollowTime = 0.15f;                         // ⑦ 出手后顺势往前下方收
    public float thReturnTime = 0.25f;                         // ⑧ 手回到枪上 / 垂下来
    public Vector2 thChest = new Vector2(940f, 1160f);         // ③ 胸前（手心）
    public float thChestAng = 0f;                              //    手平着，手雷竖着（拉环朝上）
    public Vector2 thPinPull = new Vector2(0f, 45f);           // ④ 拔环时手往哪扯一下（PSD 像素，y 正 = 往下）
    public Vector2 thBack = new Vector2(720f, 820f);           // ⑤ 抡到头后面（手心）
    public float thBackAng = 110f;                             //    小臂竖起来、拳头朝后上方
    public Vector2 thRelease = new Vector2(1280f, 880f);       // ⑥ 出手的位置（手心）
    public float thReleaseAng = 20f;                           //    从头顶往前甩，拳头朝前上方
    public Vector2 thFollow = new Vector2(1400f, 1200f);       // ⑦ 收手（手心）
    public float thFollowAng = -50f;                           //    拳头朝前下方
    public float thLeanBack = 6f;                              // 抡的时候身体往后仰多少度
    public float thLeanForward = -9f;                          // 甩出去时身体往前压多少度
    public float thShift = 0.02f;                              // 甩出去时身体往前挪多远（单位）
    public Vector2 thGrenadeInHand = new Vector2(36f, 290f);   // 手雷（轴心 = 拉环顶）相对手心在哪（手的本地，PSD 像素，x 正 = 拳头朝的方向，y 正 = 往上）：拳头握住手雷身子，拉环露在上面
    public bool testThrow;                                     // 勾一下：扔一颗手雷（往测试目标 / 正前方 3 单位）
    public bool testRefillGrenades;                            // 勾一下：腰上两颗手雷补回来

    float _throwT = -1f;                                       // 扔到哪一步了（秒；-1 = 没在扔）
    Vector3 _throwAt;                                          // 往哪扔（世界坐标，人站的那一层）
    System.Action<Vector3> _onRelease;                         // 出手时调（参数 = 手雷在哪，世界坐标）
    bool _released;                                            // 已经出手了
    string _takenGear;                                         // 拿的是腰上哪一颗（Grenade1 / Grenade2）
    Transform _throwHand;                                      // 扔手雷的那只手（单独一个，挂在身体上）
    SpriteRenderer _throwHandSr, _throwGrenadeSr;              // 手的图 / 手里的手雷
    Vector3 _throwStartPos; float _throwStartAng;              // 开始扔时手在哪（身体本地）
    SpriteRenderer _throwHidHand;                              // 扔的时候藏起来的那只手（枪上的辅助手 / 平时的前手）

    public bool IsThrowing => _throwT >= 0f;
    bool ThrowFacing(out bool left) { left = _throwAt.x < transform.position.x; return _throwT >= 0f; }   // 正在扔：朝扔的方向
    public int GrenadesLeft => (GearShown("Grenade1") ? 1 : 0) + (GearShown("Grenade2") ? 1 : 0);   // 腰上还有几颗

    bool GearShown(string key) => _gear.TryGetValue(key, out Transform t) && t != null && t.GetComponent<SpriteRenderer>().enabled;

    // 腰上的手雷补满（测试 / 补给）
    public void RefillGrenades()
    {
        foreach (string k in new[] { "Grenade1", "Grenade2" })
            if (_gear.TryGetValue(k, out Transform t) && t != null) t.GetComponent<SpriteRenderer>().enabled = true;
    }

    // 现在能不能扔：拼好了、活着、腰上还有、没在换弹 / 取出步枪 / 扔、手上不是匕首
    public bool CanThrow => _built && !_dead && GrenadesLeft > 0 && _throwT < 0f && _reloadT < 0f && _drawT < 0f && _held != "dagger";

    // 外部调用：往 target（世界坐标，人站的那一层）扔一颗。onRelease = 出手时调（游戏里生成飞出去的手雷）。扔不了返回 false
    public bool PlayThrow(Vector3 target, System.Action<Vector3> onRelease)
    {
        if (!CanThrow) return false;
        EnsureThrowHand();
        _throwAt = target;
        _onRelease = onRelease;
        _released = false;
        _takenGear = null;
        _throwT = 0f;
        _recoilT = -1f; _burstLeft = 0;                                     // 停掉开枪
        // 开始的位置：拿着枪 = 枪上的辅助手；空手 = 平时的前手
        _throwHidHand = null;
        if (_gun != null) _throwHidHand = _gun.Part(_gun.def.reloadHand);
        if (_throwHidHand == null) _throwHidHand = _bodyHandL;
        Transform from = _throwHidHand != null ? _throwHidHand.transform : _handL;
        _throwStartPos = _body.InverseTransformPoint(from.position);
        _throwStartAng = Mathf.DeltaAngle(0f, from.eulerAngles.z - _body.eulerAngles.z) * (_rig.lossyScale.x < 0f ? -1f : 1f);
        return true;
    }

    // 停掉（换手 / 倒下）：没出手的那颗就不扔了
    void StopThrow()
    {
        if (_throwT < 0f) return;
        _throwT = -1f;
        if (_throwHandSr != null) _throwHandSr.enabled = false;
        if (_throwGrenadeSr != null) _throwGrenadeSr.enabled = false;
        if (_throwHidHand != null && (_throwHidHand != _bodyHandL || _held == null)) _throwHidHand.enabled = true;   // 藏起来的手放回来
        _throwHidHand = null;
    }

    void EnsureThrowHand()
    {
        if (_throwHand != null) return;
        _throwHand = new GameObject("ThrowHand").transform;
        _throwHand.SetParent(_body, false);
        _throwHandSr = _throwHand.gameObject.AddComponent<SpriteRenderer>();
        _throwHandSr.sprite = GripHandArt(false, HoldType.Pistol);          // 持枪左手（衣服的手）：像握枪一样握住手雷
        _throwHandSr.sortingOrder = 31;                                     // 和前手一样：在身上所有东西前面
        _throwGrenadeSr = new GameObject("ThrowGrenade").AddComponent<SpriteRenderer>();
        _throwGrenadeSr.transform.SetParent(_throwHand, false);
        _throwGrenadeSr.sprite = Load("Grenade1");
        _throwGrenadeSr.sortingOrder = 30;                                  // 在手下面（手指握在外面）
        _throwHandSr.enabled = _throwGrenadeSr.enabled = false;
        if (_bodyHandL != null) { _throwHandSr.sharedMaterial = _bodyHandL.sharedMaterial; _throwGrenadeSr.sharedMaterial = _bodyHandL.sharedMaterial; }   // 和身上一样受光
    }

    // 扔的时候朝着目标那边（枪单手放低，不瞄准）
    void ThrowAim(float dt)
    {
        _aimLeft = _throwAt.x < transform.position.x;                       // 面朝扔的方向（扔完接着瞄准时也先保持这个朝向）
        _aimUntil = 0f;                                                     // 扔的时候不瞄准（朝向由 ThrowFacing 管）：不然扔完那一下会按旧的瞄准点把枪口猛地抬起来
        _readyUntil = 0f;
        _lockTarget = null;
        _aimAng = Mathf.MoveTowards(_aimAng, lowReadyAngle, lowReadySpeed * 2f * dt);   // 枪口往下
        _lowK = Mathf.MoveTowards(_lowK, 1f, 4f * dt);
    }

    // 身体跟着扔的动作仰 / 压（AnimateHeld 里加到出手的晃动上）
    void ThrowLean()
    {
        if (_throwT < 0f) return;
        ThrowTimes(out float a, out float b, out float c, out float d, out float e, out float f, out float g, out float h);
        float t = _throwT;
        float lean = 0f, shift = 0f;
        if (t >= d && t < e) lean = thLeanBack * Ease(Seg(t, d, thWindTime));                       // 抡：往后仰
        else if (t >= e && t < f) { float u = Ease(Seg(t, e, thThrowTime)); lean = Mathf.Lerp(thLeanBack, thLeanForward, u); shift = thShift * u; }   // 甩：往前压
        else if (t >= f && t < h) { float u = Ease(Seg(t, f, h - f)); lean = Mathf.Lerp(thLeanForward, 0f, u); shift = thShift * (1f - u); }      // 慢慢直起来
        _actionLean += lean; _actionShift += shift;
    }

    void ThrowTimes(out float a, out float b, out float c, out float d, out float e, out float f, out float g, out float h)
    {
        a = thReachTime; b = a + thGrabTime; c = b + thChestTime; d = c + thPinTime;
        e = d + thWindTime; f = e + thThrowTime; g = f + thFollowTime; h = g + thReturnTime;
    }

    // 每帧（AfterFrame）：摆扔手雷的手
    void AnimateThrow(float dt)
    {
        if (testRefillGrenades) { testRefillGrenades = false; RefillGrenades(); }
        if (testThrow)                                                      // 测试：往测试目标扔（没有就往正前方 3 单位）
        {
            testThrow = false;
            Vector3 at = testAimTarget != null ? testAimTarget.position : transform.position + new Vector3(FacingLeft ? -3f : 3f, 0f, 0f);
            PlayThrow(at, null);
        }
        if (_throwT < 0f) return;
        _throwT += dt;
        ThrowTimes(out float a, out float b, out float c, out float d, out float e, out float f, out float g, out float h);
        float t = _throwT;
        if (t >= h) { StopThrow(); return; }

        // 腰上那颗：拿到手里时藏起来
        if (_takenGear == null && t >= a)
        {
            _takenGear = GearShown("Grenade1") ? "Grenade1" : "Grenade2";
            if (_gear.TryGetValue(_takenGear, out Transform gt) && gt != null) gt.GetComponent<SpriteRenderer>().enabled = false;
        }
        // 关键姿势（身体本地）
        Vector3 belt = BeltGrabPos(out float beltAng);
        Vector3 back = ThPos(thBack), chest = ThPos(thChest), rel = ThPos(thRelease), fol = ThPos(thFollow);
        Vector3 start = CurrentStart(out float startAng);
        Vector3 p; float ang;
        if (t < a) { float u = Ease(Seg(t, 0f, a)); p = Vector3.Lerp(start, belt, u); ang = Mathf.LerpAngle(startAng, beltAng, u); }
        else if (t < b) { p = belt; ang = beltAng; }
        else if (t < c) { float u = Ease(Seg(t, b, thChestTime)); p = Vector3.Lerp(belt, chest, u); ang = Mathf.LerpAngle(beltAng, thChestAng, u); }
        else if (t < d)                                                     // 拔环：往下一扯再回来
        {
            float u = Seg(t, c, thPinTime);
            float k = u < 0.4f ? Ease(u / 0.4f) : 1f - Ease((u - 0.4f) / 0.6f);
            p = chest + (Vector3)CanvasDelta(thPinPull) * k; ang = thChestAng - 15f * k;
        }
        else if (t < e) { float u = Ease(Seg(t, d, thWindTime)); p = Vector3.Lerp(chest, back, u); ang = Mathf.Lerp(thChestAng, thBackAng, u); }   // 往后上方抡到头后面（小臂竖起来）
        else if (t < f) { float u = Seg(t, e, thThrowTime); u = u * u; p = Vector3.Lerp(back, rel, u); ang = Mathf.Lerp(thBackAng, thReleaseAng, u); }   // 越甩越快
        else if (t < g) { float u = Ease(Seg(t, f, thFollowTime)); p = Vector3.Lerp(rel, fol, u); ang = Mathf.Lerp(thReleaseAng, thFollowAng, u); }
        else { float u = Ease(Seg(t, g, thReturnTime)); p = Vector3.Lerp(fol, start, u); ang = Mathf.LerpAngle(thFollowAng, startAng, u); }

        _throwHand.localPosition = p;
        _throwHand.localRotation = Quaternion.Euler(0f, 0f, ang);
        _throwHandSr.enabled = true;
        if (_throwHidHand != null) _throwHidHand.enabled = false;           // 枪上的辅助手 / 平时的前手：扔的时候藏起来
        _throwGrenadeSr.transform.localPosition = HandDelta(thGrenadeInHand);
        _throwGrenadeSr.enabled = t >= a && !_released;                     // 抓到之后、出手之前：手里有手雷

        // 出手
        if (!_released && t >= f)
        {
            _released = true;
            Vector3 at = _throwGrenadeSr.transform.position;
            _throwGrenadeSr.enabled = false;
            if (_onRelease != null) _onRelease(at);
            else Grenade.Throw(at, transform.position, _throwAt, null, null, _throwGrenadeSr.sortingLayerID);   // 测试：直接扔一颗（不算谁的）
        }
    }

    // 腰上那颗手雷：拳头正好握住它的身子时，手心在哪（身体本地）；手平着
    Vector3 BeltGrabPos(out float ang)
    {
        ang = 0f;
        string key = _takenGear ?? (GearShown("Grenade1") ? "Grenade1" : "Grenade2");
        Vector3 gp = _gear.TryGetValue(key, out Transform t) && t != null ? t.localPosition : ThPos(P_GRENADE1);   // 手雷的轴心（拉环顶）
        return gp - (Vector3)HandDelta(thGrenadeInHand);   // 手心 = 手雷轴心 - 手雷在手里的位置
    }

    // 开始 / 回去的位置：拿着枪就是枪上辅助手现在在哪（枪会动），空手 = 平时的前手
    Vector3 CurrentStart(out float ang)
    {
        if (_gun != null && _throwHidHand != null && _throwHidHand != _bodyHandL)
        {
            Transform h = _throwHidHand.transform;
            ang = Mathf.DeltaAngle(0f, h.eulerAngles.z - _body.eulerAngles.z) * (_rig.lossyScale.x < 0f ? -1f : 1f);
            return _body.InverseTransformPoint(h.position);
        }
        ang = _throwStartAng;
        return _handL != null ? _handL.localPosition : _throwStartPos;
    }

    Vector3 ThPos(Vector2 psd) => CanvasToLocal(psd - P_BODY);              // 角色 PSD 坐标 → 身体本地
    static Vector2 CanvasDelta(Vector2 d) => new Vector2(d.x / PPU_FULL, -d.y / PPU_FULL);   // PSD 像素差 → 单位（y 往下为正）
    static Vector2 HandDelta(Vector2 d) => d / PPU_FULL;                     // 手的本地像素（y 往上为正）→ 单位
}
