using UnityEngine;

// ============================================================================
// 尸体爆炸用到的东西（拆零件本身在 CharacterRigBase.Gib.cs）：
//   Gibs      —— 什么时候算"过量伤害"、爆炸那一下的血雾 + 地上炸开的一大摊血
//   GibPiece  —— 炸飞的一块零件：往上 / 往外飞、转着落地、弹一下、滑一段停下；飞的路上一路滴血，落地溅一滩
//   BloodDrip —— 零件甩出来的小血滴：在空中划一道弧，落到地上变成一个小血点
// 地面是俯视的：每个东西都记"地面位置 + 离地高度"，画在 地面位置 + (0, 高度)。
// ============================================================================
public static class Gibs
{
    // 过量伤害：打死的那一下伤害 ≥ 这么多，而且 ≥ 最大血量的这个比例，就炸
    //   人（100 血）：狙击枪（75）一枪打死会炸，步枪 / 手枪不会；蜘蛛幼体（25 血）：狙击枪会炸；变异体（12000 血）：不会
    public const float OverkillMinDamage = 50f;
    public const float OverkillHpRatio = 0.5f;
    public static bool IsOverkill(float damage, float maxHp) => damage >= Mathf.Max(OverkillMinDamage, maxHp * OverkillHpRatio);

    static Sprite _mistRef;                                    // 用来量血雾图有多大（HitFx 里最大的那张）

    // 爆炸那一下：b = 整只的范围（世界坐标），dir = 冲击方向，layer = 排序层，groundY = 地面，k = 体型
    public static void Burst(Bounds b, Vector2 dir, int layer, float groundY, float k)
    {
        if (_mistRef == null) _mistRef = Resources.Load<Sprite>("NpcArt/Fx/HitFlesh3");
        float art = _mistRef != null ? Mathf.Max(_mistRef.bounds.size.x, _mistRef.bounds.size.y) : 0.5f;   // 血雾图原大多大（单位）
        float cover = Mathf.Max(b.size.x, b.size.y, 0.2f) * 1.3f;                                           // 要盖住多大（比整只再大一圈）
        float size = cover / Mathf.Max(0.05f, art);
        HitFx.Flesh(b.center, dir, 2, size, 0.9f, layer);                                                  // 一大团盖住整只
        for (int i = 0; i < 4; i++)                                                                         // 周围再炸开几小团（更有体积）
        {
            Vector3 off = new Vector3(Random.Range(-b.extents.x, b.extents.x), Random.Range(-b.extents.y, b.extents.y), 0f) * 0.8f;
            Vector2 d = (dir + Random.insideUnitCircle * 0.8f).normalized;
            HitFx.Flesh(b.center + off, d, Random.Range(0, 3), size * Random.Range(0.4f, 0.65f), Random.Range(0.6f, 0.85f), layer);
        }
        Vector3 feet = new Vector3(b.center.x, groundY, 0f);                                                // 地上：中间一大摊 + 四周溅开
        BloodPools.Splat(feet, 1.4f * k, true);
        for (int i = 0; i < 7; i++)
        {
            Vector2 off = (dir * 0.5f + Random.insideUnitCircle).normalized * Random.Range(0.15f, 0.6f) * k;
            off.y *= 0.55f;                                                                                 // 俯视：上下方向压扁
            BloodPools.Splat(feet + (Vector3)off, Random.Range(0.4f, 0.9f) * k, Random.value < 0.4f);
        }
    }
}

// ============================================================================
// 炸飞的一块零件
// ============================================================================
public class GibPiece : MonoBehaviour
{
    const float Gravity = 9f;                                  // 重力（乘体型：大的飞得高也落得快，飞行时间差不多）
    const float StayAfterOwner = 40f;                          // 角色删掉以后零件还在地上留多久（秒）
    const float FadeTime = 4f;                                 // 然后几秒淡掉

    CharacterRigBase _owner;                                   // 从谁身上炸下来的（它还在：零件一直留着，测试里复活能收回去）
    Vector2 _g, _vg;                                           // 地面位置 / 在地面上的速度
    float _h, _vh;                                             // 离地高度 / 往上的速度
    float _spin, _k;                                           // 每秒转几度 / 体型
    bool _landed;                                              // 落地停下了
    int _bounces;                                              // 弹了几下
    float _age, _trailT, _dripT, _gone;                        // 飞了多久 / 下一个地上血点 / 下一滴血 / 角色删掉多久了
    SpriteRenderer[] _srs; float[] _alpha;                     // 这一块所有的图（淡出用）和原来的透明度

    // 让一块零件飞起来：groundY = 地面，dir = 冲击方向，power = 多猛，k = 体型
    public static GibPiece Launch(Transform piece, CharacterRigBase owner, float groundY, Vector2 dir, float power, float k)
    {
        GibPiece p = piece.gameObject.AddComponent<GibPiece>();
        p._owner = owner;
        p._k = k;
        Vector3 pos = piece.position;
        p._g = new Vector2(pos.x, groundY);
        p._h = Mathf.Max(0f, pos.y - groundY);
        Vector2 from = owner != null ? (Vector2)owner.transform.position : (Vector2)pos;
        Vector2 outDir = new Vector2(pos.x - from.x, 0f);                                     // 从身体中间往外：左边的往左飞，右边的往右飞
        outDir.x = Mathf.Abs(outDir.x) < 0.01f ? Random.Range(-1f, 1f) : Mathf.Sign(outDir.x);
        float up = owner != null ? owner.gibUp : 3.2f, side = owner != null ? owner.gibOut : 1.8f, spin = owner != null ? owner.gibSpin : 720f;
        Vector2 v = outDir * Random.Range(0.4f, 1f) + dir * Random.Range(0.6f, 1.4f) + new Vector2(0f, Random.Range(-0.6f, 0.6f));   // 往外 + 顺着冲击 + 地面上前后乱一点
        p._vg = v * side * power * k;
        p._vh = Random.Range(0.55f, 1.15f) * up * Mathf.Sqrt(power) * k;
        p._spin = Random.Range(0.3f, 1f) * spin * (Random.value < 0.5f ? -1f : 1f);
        p._srs = piece.GetComponentsInChildren<SpriteRenderer>(true);
        p._alpha = new float[p._srs.Length];
        for (int i = 0; i < p._srs.Length; i++) p._alpha[i] = p._srs[i].color.a;
        return p;
    }

    // 透明度恢复原样（收回去时）
    public void RestoreAlpha()
    {
        if (_srs == null) return;
        for (int i = 0; i < _srs.Length; i++) if (_srs[i] != null) { Color c = _srs[i].color; c.a = _alpha[i]; _srs[i].color = c; }
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;                                                                 // 暂停时不动
        _age += dt;
        if (!_landed)
        {
            _vh -= Gravity * _k * dt;
            _h += _vh * dt;
            _g += _vg * dt;
            transform.Rotate(0f, 0f, _spin * dt);
            // 飞的路上：地上一路滴血 + 甩出小血滴
            _trailT -= dt;
            if (_trailT <= 0f && _h > 0.02f)
            {
                _trailT = Random.Range(0.025f, 0.04f);                                        // 密一点：连成一道血迹
                for (int i = 0; i < 2; i++)                                                   // 每次两点，左右散开一点（不是一条细线）
                {
                    Vector2 off = Random.insideUnitCircle * 0.06f * _k;
                    off.y *= 0.55f;                                                           // 俯视：上下方向压扁
                    BloodPools.Splat(_g + off, Random.Range(0.3f, 0.55f) * _k, false);
                }
            }
            _dripT -= dt;
            if (_dripT <= 0f)                                                                 // 整段飞行都在甩血滴
            {
                _dripT = Random.Range(0.03f, 0.05f);
                BloodDrip.Spawn(_g, _h, _vg * 0.35f + Random.insideUnitCircle * 0.5f * _k, Mathf.Max(0f, _vh * 0.3f) + Random.Range(0f, 0.8f) * _k, _k, _srs.Length > 0 && _srs[0] != null ? _srs[0].sortingLayerID : 0);   // 每次两滴
                BloodDrip.Spawn(_g, _h, _vg * 0.35f + Random.insideUnitCircle * 0.4f * _k, Mathf.Max(0f, _vh * 0.3f) + Random.Range(0f, 0.8f) * _k, _k, _srs.Length > 0 && _srs[0] != null ? _srs[0].sortingLayerID : 0);
            }
            if (_h <= 0f)                                                                     // 碰到地
            {
                _h = 0f;
                BloodPools.Splat(_g, Random.Range(0.4f, 0.7f) * _k, _bounces == 0);          // 第一下落地：溅一滩
                if (_vh < -1.2f * _k && _bounces < 2) { _vh = -_vh * 0.3f; _vg *= 0.55f; _spin *= 0.5f; _bounces++; }   // 弹一下
                else { _landed = true; _vh = 0f; }
            }
        }
        else if (_vg.sqrMagnitude > 0.0001f)                                                  // 落地后在地上滑一段停下
        {
            _vg *= Mathf.Exp(-9f * dt);
            _g += _vg * dt;
            _trailT -= dt;                                                                    // 滑的时候拖出一道血
            if (_trailT <= 0f && _vg.magnitude > 0.15f * _k) { _trailT = 0.03f; BloodPools.Splat(_g, Random.Range(0.3f, 0.5f) * _k, false); }
            transform.Rotate(0f, 0f, _spin * dt * (_vg.magnitude / Mathf.Max(0.1f, _k)) * 0.3f);
        }
        transform.position = new Vector3(_g.x, _g.y + _h, transform.position.z);

        if (_owner != null) return;                                                           // 角色还在：一直留着
        _gone += dt;
        if (_gone < StayAfterOwner) return;
        float a = 1f - (_gone - StayAfterOwner) / FadeTime;
        if (a <= 0f) { Destroy(gameObject); return; }
        for (int i = 0; i < _srs.Length; i++) if (_srs[i] != null) { Color c = _srs[i].color; c.a = _alpha[i] * a; _srs[i].color = c; }
    }
}

// ============================================================================
// 零件甩出来的小血滴：空中一道弧，落地变成一个小血点
// ============================================================================
public class BloodDrip : MonoBehaviour
{
    static Sprite[] _drops;
    Vector2 _g, _vg; float _h, _vh, _k, _t;

    public static void Spawn(Vector2 ground, float height, Vector2 groundVel, float upVel, float k, int sortingLayerID)
    {
        if (_drops == null)
        {
            System.Collections.Generic.List<Sprite> l = new System.Collections.Generic.List<Sprite>();
            for (int i = 1; i <= 3; i++) { Sprite s = Resources.Load<Sprite>("Fx/Blood/BloodDrop" + i); if (s != null) l.Add(s); }
            _drops = l.ToArray();
        }
        if (_drops.Length == 0) return;
        GameObject go = new GameObject("BloodDrip");
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _drops[Random.Range(0, _drops.Length)];
        sr.sortingLayerID = sortingLayerID;
        sr.sortingOrder = 59;                                                                 // 在人上面、子弹下面
        sr.color = new Color(0.75f, 0.08f, 0.06f, 1f);
        Material lit = RenderMats.SpriteLit;                                                  // 和地上的血一样受光
        if (lit != null) sr.sharedMaterial = lit;
        float sc = Random.Range(0.12f, 0.22f) * k;
        go.transform.localScale = new Vector3(sc, sc * 1.3f, 1f);                               // 竖着拉长一点：像在飞
        BloodDrip d = go.AddComponent<BloodDrip>();
        d._g = ground; d._h = height; d._vg = groundVel; d._vh = upVel; d._k = k;
        go.transform.position = new Vector3(ground.x, ground.y + height, 0f);
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;
        _t += dt;
        _vh -= 9f * _k * dt;
        _h += _vh * dt;
        _g += _vg * dt;
        transform.position = new Vector3(_g.x, _g.y + _h, 0f);
        if (_h <= 0f || _t > 2f)                                                              // 落地：变成地上的小血点
        {
            BloodPools.Splat(_g, Random.Range(0.2f, 0.35f) * _k, false);
            Destroy(gameObject);
        }
    }
}
