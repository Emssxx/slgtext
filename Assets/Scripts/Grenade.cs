using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 手雷（扔出去之后的那颗；扔的动作在 NpcRigB.Throw）
//   飞：从手里出来，地面上的位置从扔的人匀速走到落点，高度 = 出手高度渐渐降到地面 + 一条往上鼓的弧线，边飞边转。
//   落地：往前滚一小段停下；引信从出手开始算，到时间就炸。
//   炸：爆炸图（Resources/NpcArt/Fx/Explosion）一下炸开再淡出 + 一闪橙光（URP 2D）；
//       半径里、中间没墙挡着的人 / 敌人 / ABA 队员都扣血（越近越多）并往外推。手雷不认人：自己人也会炸到。
// 用法：Grenade.Throw(出手位置, 扔的人站的位置, 落点, 数值, 谁扔的, 排序层)
//   位置都是"人站的那一层"的世界坐标（和 transform.position 一样，不是脚底）：落点就是对着目标的 transform.position。
// ============================================================================
public class Grenade : MonoBehaviour
{
    // 手雷的数值（ABA 的在 AbaForce「手雷」一栏里调）
    [System.Serializable]
    public class Spec
    {
        public float damage = 90f;                 // 正中间的伤害（越往外越少，半径边上 = 0）
        public float radius = 1.6f;                // 伤害半径（单位）
        public float fuse = 1.5f;                  // 引信：出手后几秒炸
        public float speed = 5f;                   // 飞多快（地面上的速度，单位 / 秒）
        public float arc = 0.18f;                  // 弧线往上鼓多高（每飞 1 单位多鼓这么多）
        public float knock = 5f;                   // 把人往外推的初速度（正中间；单位 / 秒）
        public float size = 1f;                    // 爆炸图大小倍数（1 = 2 单位宽）
        public Color lightColor = new Color(1f, 0.62f, 0.25f, 1f);   // 爆炸照亮周围的光
        public float lightIntensity = 3.5f;
        public float lightRadius = 3.5f;
    }

    public const float FEET_Y = -0.453125f;        // 人站的那一层 → 脚底（地面）差多少
    static Sprite _grenadeSprite, _boomSprite;

    Spec _s;
    AbaOperative _by;                              // 谁扔的（ABA 队员；打死了算他的）
    NpcData _byNpc;                                // 谁扔的（幸存者）
    SpriteRenderer _sr;
    Vector2 _from, _to;                            // 地面上从哪飞到哪（人站的那一层）
    float _h0;                                     // 出手时离"人站的那一层"多高
    float _flight, _t, _spin;                      // 飞几秒 / 出手后过了多久 / 转多快（度 / 秒）
    float _ang;                                    // 现在转到多少度
    bool _landed;
    Vector2 _rollDir; float _rollV;                // 落地后往前滚
    int _layer;

    // 扔出去：handPos = 出手时手雷在哪（世界）；standPos = 扔的人站的位置；target = 落点；layer = 排序层
    public static Grenade Throw(Vector3 handPos, Vector3 standPos, Vector3 target, Spec spec, AbaOperative by, int layer, NpcData byNpc = null)
    {
        if (_grenadeSprite == null) _grenadeSprite = Resources.Load<Sprite>("NpcArt2/Grenade1");
        GameObject go = new GameObject("Grenade");
        Grenade g = go.AddComponent<Grenade>();
        g._s = spec ?? new Spec();
        g._by = by;
        g._byNpc = byNpc;
        g._layer = layer;
        g._sr = go.AddComponent<SpriteRenderer>();
        g._sr.sprite = _grenadeSprite;
        g._sr.sortingLayerID = layer;
        g._sr.sortingOrder = 59;                                                   // 子弹（60）下面
        g._from = standPos;
        g._to = target;
        g._h0 = handPos.y - standPos.y;
        g._from.x = handPos.x;                                                     // 从手里出来（左右按手的位置）
        float dist = Vector2.Distance(g._from, g._to);
        g._flight = Mathf.Max(0.2f, dist / Mathf.Max(0.5f, g._s.speed));
        g._spin = Random.Range(500f, 800f) * (target.x >= handPos.x ? -1f : 1f);  // 往前翻着飞
        g._rollDir = (g._to - g._from).sqrMagnitude > 0.0001f ? (g._to - g._from).normalized : Vector2.right;
        g._rollV = g._s.speed * 0.25f;                                            // 落地后还有一点往前的速度
        g.Place();
        return g;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;                                                      // 暂停时停在半空
        _t += dt;
        if (!_landed && _t >= _flight) { _landed = true; _from = _to; }           // 落地
        if (_landed)                                                               // 滚：越滚越慢
        {
            _to += _rollDir * _rollV * dt;
            _rollV = Mathf.Max(0f, _rollV - 3f * dt);
            _spin = Mathf.MoveTowards(_spin, 0f, 2500f * dt);
        }
        _ang += _spin * dt;
        Place();
        if (_t >= Mathf.Max(_s.fuse, _flight)) { Explode(); Destroy(gameObject); }
    }

    // 摆在现在的位置（地面位置 + 高度）
    void Place()
    {
        Vector2 g; float h;
        if (_landed) { g = _to; h = FEET_Y; }
        else
        {
            float u = Mathf.Clamp01(_t / _flight);
            g = Vector2.Lerp(_from, _to, u);
            float dist = Vector2.Distance(_from, _to);
            h = Mathf.Lerp(_h0, FEET_Y, u) + _s.arc * dist * 4f * u * (1f - u);   // 出手高度 → 地面，中间往上鼓
        }
        transform.position = new Vector3(g.x, g.y + h, 0f);
        transform.rotation = Quaternion.Euler(0f, 0f, _ang);
    }

    // 炸：效果 + 伤害
    void Explode()
    {
        Vector2 c = _to;                                                           // 爆炸中心（人站的那一层）
        Boom.Spawn(new Vector3(c.x, c.y + FEET_Y, 0f), _s, _layer);
        float r = Mathf.Max(0.1f, _s.radius);
        // 敌人
        foreach (Enemy e in new List<Enemy>(Enemy.All))
        {
            if (e == null || !e.Alive) continue;
            if (!Hit(c, e.transform.position, r, out float k, out Vector2 dir)) continue;
            float dmg = _s.damage * k;
            if (_by != null) e.TakeHitFromAba(dmg, _by); else e.TakeDamage(dmg, _byNpc);   // 算扔的人的
            if (e.Alive)
            {
                float mass = Mathf.Max(0.6f, e.Def.scale * e.Def.scale);           // 体型越大推得越少
                Knockback.Push(e.gameObject, dir * (_s.knock * k / mass));
                e.Stagger(0.5f / mass);
            }
            else if (_by != null) _by.OnKill(e, null);
        }
        // 幸存者
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w != null)
            foreach (NpcData n in w.npcs)
            {
                if (n == null || n.IsDead) continue;
                NpcMover m = NpcView.GetMover(n.id);
                if (m == null || !Hit(c, m.transform.position, r, out float k, out Vector2 dir)) continue;
                NpcCombat.OnHit(n, _s.damage * k, null, n == _byNpc ? null : _byNpc, _by);   // 自己人扔的：算误伤
                if (!n.IsDead) Knockback.Push(m.gameObject, dir * _s.knock * k);
                else if (_by != null) _by.OnKill(null, n);
            }
        // ABA 队员（自己人也炸）
        foreach (AbaOperative o in new List<AbaOperative>(AbaOperative.All))
        {
            if (o == null || !o.Alive) continue;
            if (!Hit(c, o.transform.position, r, out float k, out Vector2 dir)) continue;
            o.TakeHit(_s.damage * k, dir, null, _byNpc);
            if (o.Alive) Knockback.Push(o.gameObject, dir * _s.knock * k);
        }
    }

    // p 在不在爆炸范围里（中间没墙挡着）；k = 伤害比例（中间 1 → 边上 0），dir = 往外推的方向
    static bool Hit(Vector2 c, Vector2 p, float r, out float k, out Vector2 dir)
    {
        Vector2 d = p - c;
        float dist = d.magnitude;
        k = 0f; dir = dist > 0.001f ? d / dist : Vector2.up;
        if (dist >= r) return false;
        if (!Ballistics.LineClear(c, p)) return false;                             // 墙 / 石头挡住了
        k = Mathf.Pow(1f - dist / r, 0.7f);                                        // 越近越痛（中间一圈都很痛）
        return true;
    }

    // ---------------- 爆炸效果（单独的小物体，自己播完自己删）----------------
    class Boom : MonoBehaviour
    {
        SpriteRenderer _sr;
        UnityEngine.Rendering.Universal.Light2D _light;
        float _t, _size, _peak;
        const float LIFE = 0.7f;                                                   // 播多久

        public static void Spawn(Vector3 at, Spec s, int layer)
        {
            if (_boomSprite == null) _boomSprite = Resources.Load<Sprite>("NpcArt/Fx/Explosion");
            if (_boomSprite == null) { Debug.LogWarning("[Grenade] 找不到素材：NpcArt/Fx/Explosion"); return; }
            GameObject go = new GameObject("GrenadeBoom");
            go.transform.position = at;                                            // 图的轴心在底部：从地面炸起来
            Boom b = go.AddComponent<Boom>();
            b._sr = go.AddComponent<SpriteRenderer>();
            b._sr.sprite = _boomSprite;
            b._sr.sortingLayerID = layer;
            b._sr.sortingOrder = 62;                                               // 在着弹点效果（61）上面
            b._sr.flipX = Random.value < 0.5f;                                     // 每次形状不太一样
            RenderMats.MakeUnlit(b._sr);                                           // 自己发亮
            b._size = s.size;
            b._peak = s.lightIntensity;
            if (s.lightIntensity > 0f && RenderMats.IsURP)
                b._light = WorldLighting.CreatePoint("BoomLight", go.transform, s.lightColor, s.lightIntensity, s.lightRadius, 360f);
            b.Apply();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _t += dt;
            Apply();
            if (_t >= LIFE) Destroy(gameObject);
        }

        void Apply()
        {
            float u = Mathf.Clamp01(_t / LIFE);
            float pop = Mathf.Clamp01(_t / 0.08f);                                 // 前 0.08 秒：一下炸开
            float s = _size * Mathf.Lerp(0.35f, 1f, 1f - (1f - pop) * (1f - pop)) * (1f + 0.12f * u);   // 之后再慢慢胀大一点
            transform.localScale = new Vector3(s, s, 1f);
            Color c = _sr.color; c.a = u < 0.25f ? 1f : 1f - Mathf.Pow((u - 0.25f) / 0.75f, 1.3f); _sr.color = c;   // 先亮一下，再淡出
            if (_light != null) _light.intensity = _peak * (1f - u) * (1f - u);   // 光一闪就暗下去
        }
    }
}
