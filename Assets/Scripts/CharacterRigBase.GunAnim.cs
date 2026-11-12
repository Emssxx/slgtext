using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 枪的专用动画类（CharacterRigBase 的一部分，partial）
//   每把枪 = WeaponDef（数据：部件、锚点、手感、枪火图、换弹方式）+ 一个 GunAnimator（代码：枪火怎么画、换弹怎么动、
//   开枪 / 每帧额外动哪些部件）。WeaponDef.animator 填类名，空 = 通用（GunAnimator 本身）。
//   和 NPC 骨架没有绑定：任何角色拿起这把枪，都按这把枪的数据 + 动画类播；手用角色衣服上的持枪手，握在枪的锚点上。
//
//   写一把特殊枪：新建一个类继承 CharacterRigBase.GunAnimator，只重写要改的那几个方法，然后在 WeaponDef.animator 里填类名。
//     例：class PumpShotgun : CharacterRigBase.GunAnimator { public override void OnFire(...) { ... 拉一下护木 ... } }
//   现在有：GunAnimator（通用：图片枪火）、LayeredFlashGun（三层枪火 + 烟，防护服手枪用）。
//
// 颤抖换弹也在这里（基于普通换弹的分段计划）：持枪手类型（GripStyle）手抖到一定程度就会手忙脚乱——新弹匣掉地上、插歪卡住。
// ============================================================================
public abstract partial class CharacterRigBase
{
    // ======================= 枪的动画类 =======================
    public class GunAnimator
    {
        static readonly Dictionary<string, GunAnimator> _cache = new Dictionary<string, GunAnimator>();
        static readonly GunAnimator _default = new GunAnimator();

        // 这把枪用哪个动画类（WeaponDef.animator 填的类名；找不到就用通用的）
        public static GunAnimator For(WeaponDef d)
        {
            string name = d != null ? d.animator : null;
            if (string.IsNullOrEmpty(name)) return _default;
            if (_cache.TryGetValue(name, out GunAnimator a)) return a;
            System.Type t = System.Type.GetType(name) ?? System.Type.GetType(typeof(CharacterRigBase).FullName + "+" + name);
            if (t != null && typeof(GunAnimator).IsAssignableFrom(t) && !t.IsAbstract) a = (GunAnimator)System.Activator.CreateInstance(t);
            else { Debug.LogWarning("[GunAnimator] 找不到动画类：" + name + "（用通用的）"); a = _default; }
            _cache[name] = a;
            return a;
        }

        // 开枪的枪火（默认：这把枪的枪火图随机一张）
        public virtual void Flash(CharacterRigBase rig, GunRig gun) { rig.GunFlash(); }
        // 开枪那一下（通用后坐之外还要做的，比如泵动、退壳）
        public virtual void OnFire(CharacterRigBase rig, GunRig gun) { }
        // 每帧：持枪 / 后坐摆好之后（额外会动的部件）
        public virtual void Pose(CharacterRigBase rig, GunRig gun, float dt) { }
        // 每帧：换弹动画（默认：按 WeaponDef.reloadMotion 播通用的手枪式 / 步枪式）
        public virtual void Reload(CharacterRigBase rig, GunRig gun, float dt) { rig.DefaultGunReload(dt); }
        // 某个配件 + 某种换弹一共要多少"标准时间"（自己写了换弹动画就要一起改）
        public virtual float ReloadTotal(CharacterRigBase rig, GunRig gun, ReloadStyle style, ReloadKind kind) { return rig.DefaultGunReloadTotal(gun, style, kind); }
    }

    // 三层枪火（① 火焰 ② 火花 + 亮光 ③ 烟），图在 Resources/NpcArt/Fx/
    public class LayeredFlashGun : GunAnimator
    {
        public override void Flash(CharacterRigBase rig, GunRig gun) { rig.LayeredFlash(gun); }
    }

    // 开枪的枪火：交给这把枪的动画类
    public virtual void MuzzleFlash()
    {
        if (_gun == null || _muzzle == null) return;
        GunAnimator.For(_gun.def).Flash(this, _gun);
    }

    // ======================= 三层枪火（LayeredFlashGun 用） =======================
    protected static Sprite[] _fxFire, _fxFlash, _fxGlow, _fxSmoke;   // 枪火图：枪火 / 闪光 / 亮光（PSD 画的，各 2 种）、烟（代码画，3 种）
    protected static Sprite _fxRicochet;                      // 跳弹图（PSD 画的）
    protected static Material _fxAdditive;                    // 亮光 1 用的叠加发光材质（PSD 里是"亮光"混合模式）

    public void LayeredFlash(GunRig gun)
    {
        if (_muzzle == null || gun == null) return;
        WeaponDef d = gun.def;
        EnsureFx();
        float k = Mathf.Max(0.05f, d.flashSize);
        Vector3 at = _muzzle.position;                                         // 子弹生成点
        Vector3 dir = _muzzle.TransformVector(Vector3.right);                  // 枪口朝哪（含朝左翻转）
        dir.z = 0f;
        bool left = _muzzle.lossyScale.x < 0f;                                 // 角色朝左（整个角色是左右翻过来的）
        float size = Mathf.Abs(_muzzle.lossyScale.y);                          // 角色本身的缩放
        float ang = dir.sqrMagnitude > 0.000001f ? Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg : (left ? 180f : 0f);
        // 图层顺序和 PSD 一样：枪火在最下面，亮光在中间，闪光在最上面（都画在人上面、子弹下面）
        FxLayer("MuzzleFire", Pick(_fxFire), 55, d.flashTime, k * size, 0.9f, 1.05f, false, at, ang, left);
        int g = _fxGlow.Length > 0 ? Random.Range(0, _fxGlow.Length) : -1;
        if (g >= 0) FxLayer("MuzzleGlow", _fxGlow[g], 56, d.glowTime, k * size, 0.95f, 1.05f, g == 0, at, ang, left);   // 亮光 1 = 叠加发光，亮光 2 = 普通
        FxLayer("MuzzleFlash", Pick(_fxFlash), 57, d.sparkTime, k * size, 0.9f, 1.1f, false, at, ang, left);
        if (d.smoke) MuzzleSmoke(k, d.smokeTime);
        if (RenderMats.IsURP && muzzleLightIntensity > 0f) StartCoroutine(MuzzleLight(at));   // 火光照亮周围
    }

    // 从几张图里随机挑一张（没有就返回 null）
    protected static Sprite Pick(Sprite[] list) { return list != null && list.Length > 0 ? list[Random.Range(0, list.Length)] : null; }

    // 枪火的一层：固定在世界里的 at 点（不挂在枪上），转到 ang 度（朝左时上下翻 = 左右镜像），从 grow0 倍长到 grow1 倍，然后淡掉
    protected void FxLayer(string name, Sprite sprite, int order, float life, float k, float grow0, float grow1, bool additive, Vector3 at, float ang, bool left)
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
    protected void MuzzleSmoke(float k, float smokeTime)
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
    protected SpriteRenderer FxSprite(string name, Sprite sprite, int order, Transform parent)
    {
        SpriteRenderer sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        RenderMats.MakeUnlit(sr);                                            // 2D 光照：枪火 / 跳弹 / 烟自己发亮，不受光（亮光 1 之后会换成叠加材质）
        if (parent != null) sr.transform.SetParent(parent, false);
        return sr;
    }

    // 一个枪火图层的一生：大小从 s0 变到 s1、（世界里的）位置飘 drift、透明度先淡入（fadeIn = 占一生的比例）再按 pow 的快慢淡出
    protected IEnumerator FxLife(SpriteRenderer sr, float life, Vector3 s0, Vector3 s1, Vector3 drift, bool world, float fadeIn, float alphaMax, float pow)
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
    protected static void EnsureFx()
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
    protected static Sprite[] FxLoad(params string[] names)
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
    protected static float FxHash(int ix, int iy, int seed)
    {
        unchecked
        {
            uint h = (uint)ix * 374761393u + (uint)iy * 668265263u + (uint)seed * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xffff) / 65535f;
        }
    }

    protected static float FxNoise(float x, float y, int seed)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = FxHash(ix, iy, seed), b = FxHash(ix + 1, iy, seed), c = FxHash(ix, iy + 1, seed), d = FxHash(ix + 1, iy + 1, seed);
        float top = a + (b - a) * fx, bottom = c + (d - c) * fx;
        return top + (bottom - top) * fy;
    }

    protected static float FxFbm(float x, float y, int seed)                            // 4 层叠加：大团块 + 小细节
    {
        float s = 0f, amp = 0.5f, f = 1f, tot = 0f;
        for (int i = 0; i < 4; i++) { s += FxNoise(x * f, y * f, seed + i * 31) * amp; tot += amp; amp *= 0.5f; f *= 2f; }
        return s / tot;
    }

    // 按 0~1 的"温度"取颜色（几个颜色节点之间渐变）
    protected static Color FxRamp(float[] at, Color[] cols, float t)
    {
        t = Mathf.Clamp01(t);
        for (int i = 0; i < at.Length - 1; i++)
            if (t <= at[i + 1]) return Color.Lerp(cols[i], cols[i + 1], (t - at[i]) / Mathf.Max(0.000001f, at[i + 1] - at[i]));
        return cols[cols.Length - 1];
    }

    // 烟图：0 = 圆团，1 = 往前拉长的一团，2 = 一缕一缕的
    protected static Sprite SmokeSprite(int kind)
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


    // ======================= 颤抖换弹（手枪式；基于普通换弹） =======================
    [Header("颤抖换弹（手枪式；基于普通换弹，没列出来的时间 / 姿势用上面的）")]
    public float trembleReloadMul = 1.5f;                     // 颤抖换弹时枪和手抖多厉害（持枪手类型的抖动 × 这个倍数）
    [Range(0f, 1f)] public float srFailChance = 0.35f;       // 每次插弹匣插不进去的概率
    public int srMaxFails = 2;                                // 最多连续插不进去几次
    public float srFailDepth = 0.5f;                          // 插不进去时插进去了多少就卡住（0~1）
    public float srFailOffset = 0.012f;                       // 插歪了往前偏多少（按枪的方向，单位）
    public float srFailTilt = 10f;                            // 插歪了歪多少度
    public float srFailBackTime = 0.2f;                       // 卡住后退出来的时间
    public float srFailShake = 0.6f;                          // 卡住时枪磕一下的幅度（乘在 Rl Shake 上）
    [Range(0f, 1f)] public float srDropChance = 0.25f;       // 新弹匣拿回来的路上掉到地上的概率（最多掉 2 次）
    public Vector2 srSlipOffset = new Vector2(0.03f, -0.1f);  // 新弹匣滑掉时换弹手在哪（相对握把下方：x 往前，y 往上）
    public Vector2 srFling = new Vector2(0.5f, 0.9f);         // 滑掉时弹出去的速度（x 往前，y 往上；单位 / 秒，每次随机 0.7~1.3 倍）
    public Vector2 srFlingSpin = new Vector2(-1000f, -600f);  // 弹出去时转多快：在范围里随机（度 / 秒）
    public float srFumbleTime = 0.3f;                         // 掉了以后手愣住多久，再回去拿一个

    public System.Action OnNewMagDropped;                            // 颤抖换弹时新弹匣掉到地上了（游戏规则之后接：物品栏再扣一个弹匣）

    // 一段：换弹手从哪个姿势到哪个姿势、拿着什么、这段开始时发生什么
    struct ShakySeg
    {
        public int from, to;       // 姿势（下面的 Pose 常量）
        public float len;          // 多长（标准时间）
        public float start;        // 从第几秒开始（算好的）
        public int item;           // 换弹手拿着：0 没有 / 1 旧弹匣 / 2 新弹匣 / 3 前半段旧、后半段新 / 4 前半段空、后半段新
        public int evt;            // 开始时：0 无 / 1 旧弹匣弹出 / 2 新弹匣掉了 / 3 卡住了（枪磕一下）/ 4 第一次对准 / 5 插好了
    }
    // 握把 / 握把下方 / 收旧弹匣的地方 / 对准 / 插好 / 插歪卡住 / 掉弹匣的地方 / 拿新弹匣的地方（后两个配件给）
    const int PoseRest = 0, PoseUnder = 1, PoseStow = 2, PoseReady = 3, PoseIn = 4, PoseFail = 5, PoseSlip = 6, PoseTake = 7;
    readonly List<ShakySeg> _shaky = new List<ShakySeg>();
    readonly List<float> _tFails = new List<float>();         // 每次卡住（枪磕一下）的时刻
    int _shakyFired;                                          // 下一段要触发事件的序号
    float _tOldOut, _tAlign0, _tSeated, _tShakyEnd;           // 旧弹匣弹出 / 第一次对准 / 插好 / 结束 的时刻
    readonly Vector2[] _poseP = new Vector2[8];
    readonly float[] _poseA = new float[8];

    void AddShaky(int from, int to, float len, int item, int evt)
    {
        float start = 0f;
        if (_shaky.Count > 0) { ShakySeg last = _shaky[_shaky.Count - 1]; start = last.start + last.len; }
        _shaky.Add(new ShakySeg { from = from, to = to, len = Mathf.Max(0f, len), start = start, item = item, evt = evt });
    }

    // 开始颤抖换弹时：随机好这次新弹匣掉几次、插歪几次，排出整段动作；返回一共多长
    float BuildShakyPlan()
    {
        _shaky.Clear(); _tFails.Clear(); _shakyFired = 0;
        AddShaky(PoseRest, PoseRest, rlLockTime, 0, 0);                     // ① 抬枪、空仓挂机（换弹手还握着枪）
        AddShaky(PoseRest, PoseUnder, nrCatchTime, 0, 0);                   // ② 换弹手移到握把下方
        AddShaky(PoseUnder, PoseUnder, rlDropTime, 1, 1);                   // ③ 旧弹匣弹出，掉进手里
        AddShaky(PoseUnder, PoseStow, rlStowTime, 1, 0);                    // ④ 带到收弹匣的地方
        AddShaky(PoseStow, PoseTake, rlGrabTime, 3, 0);                     // ⑤ 放下旧的，拿起新的
        for (int i = 0; i < 2 && UnityEngine.Random.value < srDropChance; i++)   // 新弹匣掉了（最多 2 次）：回去再拿一个
        {
            AddShaky(PoseTake, PoseSlip, rlBringTime, 2, 0);                //   拿到枪下方……
            AddShaky(PoseSlip, PoseSlip, srFumbleTime, 0, 2);               //   手一滑掉了，愣一下
            AddShaky(PoseSlip, PoseTake, rlBringTime, 0, 0);                //   回去
            AddShaky(PoseTake, PoseTake, rlGrabTime, 4, 0);                 //   再拿一个
        }
        AddShaky(PoseTake, PoseUnder, rlBringTime, 2, 0);                   // ⑥ 拿到握把下方
        AddShaky(PoseUnder, PoseReady, rlAlignTime, 2, 4);                  // ⑦ 对准
        for (int i = 0; i < srMaxFails && UnityEngine.Random.value < srFailChance; i++)   // 插不进去：退出来重新对准
        {
            AddShaky(PoseReady, PoseFail, rlInsertTime, 2, 0);              //   插歪了，卡住
            AddShaky(PoseFail, PoseUnder, srFailBackTime, 2, 3);            //   枪磕一下，退出来
            AddShaky(PoseUnder, PoseReady, rlAlignTime, 2, 0);              //   重新对准
        }
        AddShaky(PoseReady, PoseIn, rlInsertTime, 2, 0);                    // ⑧ 插进去
        AddShaky(PoseIn, PoseRest, rlReturnTime, 0, 5);                     // ⑨ 插好了，回到枪上
        _tOldOut = _shaky[2].start;
        foreach (ShakySeg g in _shaky)
        {
            if (g.evt == 4) _tAlign0 = g.start;
            if (g.evt == 3) _tFails.Add(g.start);
            if (g.evt == 5) _tSeated = g.start;
        }
        _tShakyEnd = _tSeated + rlReturnTime;
        return Mathf.Max(0.05f, _tShakyEnd);
    }

    static bool IsPouchPose(int p) => p == PoseStow || p == PoseTake;

    // 每帧：颤抖换弹（枪的动作和普通换弹一样，多了一直抖、卡住时磕一下）
    void ShakyReload(float dt)
    {
        _reloadT += dt * _reloadRate;
        float t = _reloadT;
        if (_shaky.Count == 0 || t >= _tShakyEnd) { StopGunReload(); ReloadDonePose(); return; }   // 播完了
        GunRig gr = _rlGun;
        GunReloadGeo geo = Geo(gr);
        ReloadStyle st = _rlStyle;
        GripStyle grip = Grip;
        int si = 0;
        while (si < _shaky.Count - 1 && t >= _shaky[si].start + _shaky[si].len) si++;
        ShakySeg cur = _shaky[si];
        float k = cur.len > 0f ? Mathf.Clamp01((t - cur.start) / cur.len) : 1f;
        float e = Ease(k);

        // ---- 1. 枪 ----
        float raise = t < _tSeated ? Ease(t / Mathf.Max(0.001f, rlLockTime)) : 1f - Ease(Seg(t, _tSeated, rlReturnTime));
        float align = t < _tSeated ? rlGunAlign * Ease(Seg(t, _tAlign0, rlAlignTime))
                                   : rlGunAlign * (1f - Ease(Seg(t, _tSeated, rlReleaseTime)));
        float slideAt = _tSeated + Mathf.Max(0f, rlSlideDelay);
        float shakeDrop = Shake(t - _tOldOut, rlDropTime);
        float shakeInsert = Shake(t - _tSeated, rlReleaseTime);
        float shakeSlide = Shake(t - slideAt, rlReleaseTime);
        float bump = 0f;
        foreach (float f in _tFails) bump += Shake(t - f, rlDropTime);      // 卡住：枪磕一下
        Vector2 gunTrem = grip.Shake(_time, 0f, trembleReloadMul, out float gunTremA);   // 枪一直抖（持枪手类型决定怎么抖）
        float aimNow = _aimAng * (1f - raise);
        Vector2 gunPos = ReloadAimHand(aimNow, 1f - raise) + rlGunRaise * raise
                       + new Vector2(0f, rlShake * (shakeDrop + shakeInsert + srFailShake * bump)) + gunTrem;
        float gunAng = aimNow + rlGunTilt * raise + align + rlShakeAngle * (shakeDrop + shakeSlide) + gunTremA;
        _handPoint.localPosition = new Vector3(gunPos.x, gunPos.y, 0f);
        _handPoint.localRotation = Quaternion.Euler(0f, 0f, gunAng);
        float slideK = t < slideAt ? Ease(t / Mathf.Max(0.001f, rlLockTime)) : 1f - Seg(t, slideAt, rlReleaseTime * 0.4f);
        gr.Kick(rlSlideLock * slideK, PPU_FULL);

        // ---- 2. 这一帧各个姿势在哪（跟着枪走；拿 / 收弹匣的地方由配件给） ----
        Vector2 magIn = geo.magIn, magDir = geo.magDir, magInHand = geo.magInHand;
        System.Func<Vector2, Vector2> G2R = p => gunPos + Rot(p, gunAng);
        _poseP[PoseRest] = G2R(geo.rest); _poseA[PoseRest] = gunAng + geo.restAng;
        _poseP[PoseUnder] = G2R(magIn - magDir * (geo.ready + rlUnderGap)) - Rot(magInHand, gunAng); _poseA[PoseUnder] = gunAng;
        st.StowPose(this, magInHand, 0f, out _poseP[PoseStow], out _, out _poseA[PoseStow]);
        st.TakePose(this, magInHand, 0f, out _poseP[PoseTake], out _, out _poseA[PoseTake]);
        _poseP[PoseReady] = G2R(magIn - magDir * geo.ready) - Rot(magInHand, gunAng); _poseA[PoseReady] = gunAng;
        _poseP[PoseIn] = G2R(magIn) - Rot(magInHand, gunAng); _poseA[PoseIn] = gunAng;
        float failAng = gunAng + srFailTilt;                                // 插歪了：歪一点、偏一点、只插进去一半
        _poseA[PoseFail] = failAng;
        _poseP[PoseFail] = G2R(magIn - magDir * (geo.ready * (1f - Mathf.Clamp01(srFailDepth))) + new Vector2(srFailOffset, 0f)) - Rot(magInHand, failAng);
        _poseP[PoseSlip] = _poseP[PoseUnder] + srSlipOffset;
        _poseA[PoseSlip] = _poseA[PoseUnder];

        // ---- 3. 换弹手 ----
        Vector2 hp = Vector2.Lerp(_poseP[cur.from], _poseP[cur.to], e);
        float ha = Mathf.LerpAngle(_poseA[cur.from], _poseA[cur.to], e);
        bool viaPouch = IsPouchPose(cur.from) || IsPouchPose(cur.to);
        bool atPouch = IsPouchPose(cur.from) && IsPouchPose(cur.to);
        if (viaPouch && !atPouch) hp += new Vector2(0f, rlArc * Mathf.Sin(Mathf.PI * e));   // 走弧线
        int leg = atPouch ? 2 : viaPouch ? 1 : 0;
        Vector2 handTrem = grip.Shake(_time, 7.3f, trembleReloadMul, out float handTremA);   // 手也一直抖（和枪抖得不一样）
        hp += handTrem; ha += handTremA;
        bool second = k >= 0.5f;
        bool holdOld = cur.item == 1 || (cur.item == 3 && !second);
        bool holdNew = cur.item == 2 || ((cur.item == 3 || cur.item == 4) && second);
        bool reloadHand = si > 0;
        if (cur.item == 3 && second) MarkStowed();                          // 旧弹匣收好了
        if (holdNew) MarkTaken();                                           // 新弹匣离开配件

        // ---- 4. 摆换弹手和两个弹匣 ----
        if (geo.hand != null) geo.hand.enabled = !reloadHand;
        _rlHandSprite.enabled = reloadHand;
        PlaceReloadHand(geo, hp, ha, st.Behind(this, hp, leg));
        Vector2 nb = hp + Rot(magInHand, ha);
        _rlNewMag.enabled = holdNew;
        _rlNewMag.transform.localPosition = new Vector3(nb.x, nb.y, 0f);
        _rlNewMag.transform.localRotation = Quaternion.Euler(0f, 0f, ha);
        if (geo.mag != null) geo.mag.enabled = !(t >= _tOldOut && t < _tSeated);
        _rlOldMag.enabled = holdOld;
        if (holdOld)
        {
            Vector2 op = nb; float oa = ha;
            if (cur.evt == 1)                                               // ③ 从枪里掉进手里
            {
                op = Vector2.Lerp(G2R(magIn), nb, e);
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
            if (g.evt == 2)                                                 // 新弹匣从手里滑掉，掉到地上
            {
                DropGunMag(_rlNewMag.sprite, geo.newOrder - 1, nb, ha, srFling * UnityEngine.Random.Range(0.7f, 1.3f), RandIn(srFlingSpin));
                if (OnNewMagDropped != null) OnNewMagDropped();
            }
        }
    }
}
