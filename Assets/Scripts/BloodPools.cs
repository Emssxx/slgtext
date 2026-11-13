using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// BloodPools —— 挨打的生物脚下的一滩血（NPC、敌人、ABA 队员都用）
//   图：Resources/Fx/Blood/BloodPool1~4（一滩）、BloodDrop1~3（溅出去的小滴），俯视压扁的椭圆。
//   每挨一下：脚下没有血就新生成一滩（从小慢慢渗开）；已经有了就让它再大一点（最多 maxScale 倍）；
//     顺着挨打的方向溅一两小滴；打死时那一滩更大。
//   一滩血在地上留 stayTime 秒，然后慢慢淡掉；地上最多 maxPools 滩（多了先删最早的）。
//   画在地面层（sortingOrder -1：人 / 建筑下面，地砖 / 地形上面），受光（夜里是暗的）。
// 用法：BloodPools.Spawn(挨打的物体, 伤害, 是否打死, 打过来的方向)。不用放进场景，第一次用时自己生成。
// ============================================================================
public class BloodPools : MonoBehaviour
{
    [Header("一滩血")]
    public float feetY = -0.453125f;     // 脚底在物体位置下面多少（NPC / 敌人都是这个，乘物体缩放）
    public float baseSize = 0.55f;       // 第一下的大小（图的倍数；图原大 = 0.5 单位宽）
    public float sizePerDamage = 0.02f;  // 伤害每多 1 点大一点
    public float growPerHit = 0.18f;     // 同一滩血每再挨一下变大多少
    public float maxScale = 1.6f;        // 最大几倍
    public float killMul = 1.5f;         // 打死时这一滩再大多少
    public float spreadTime = 0.8f;      // 渗开要几秒
    public float mergeDist = 0.25f;      // 离上一滩这么近就让上一滩变大（不新生成）
    [Header("溅出去的小滴")]
    public int dropsPerHit = 2;
    public Vector2 dropDist = new Vector2(0.12f, 0.35f);   // 溅多远（顺着挨打的方向）
    public float dropSize = 0.5f;
    [Header("留多久")]
    public float stayTime = 90f;         // 在地上留几秒
    public float fadeTime = 12f;         // 然后几秒淡掉
    public int maxPools = 160;           // 地上最多几滩（大滩 + 小滴一起算）
    public int sortingOrder = -1;        // 地面层

    static BloodPools _inst;
    static Sprite[] _pools, _drops;

    class Pool
    {
        public SpriteRenderer sr;
        public float born, size, target, from, growAt;
    }
    readonly List<Pool> _all = new List<Pool>();
    readonly Dictionary<int, Pool> _last = new Dictionary<int, Pool>();   // 每个生物脚下最近的那一滩

    static BloodPools Ensure()
    {
        if (_inst != null) return _inst;
        _inst = FindAnyObjectByType<BloodPools>();
        if (_inst == null) _inst = new GameObject("BloodPools").AddComponent<BloodPools>();
        return _inst;
    }

    static void LoadSprites()
    {
        if (_pools != null) return;
        List<Sprite> p = new List<Sprite>(), d = new List<Sprite>();
        for (int i = 1; i <= 4; i++) { Sprite s = Resources.Load<Sprite>("Fx/Blood/BloodPool" + i); if (s != null) p.Add(s); }
        for (int i = 1; i <= 3; i++) { Sprite s = Resources.Load<Sprite>("Fx/Blood/BloodDrop" + i); if (s != null) d.Add(s); }
        _pools = p.ToArray(); _drops = d.ToArray();
    }

    // 挨了一下：who = 挨打的物体（NPC / 敌人 / ABA 队员），dir = 打过来的方向（子弹飞的方向）
    public static void Spawn(Transform who, float damage, bool kill, Vector2 dir)
    {
        if (who == null || damage <= 0f) return;
        try { Ensure().Hit(who, damage, kill, dir); }
        catch (System.Exception e) { Debug.LogWarning("[BloodPools] " + e.Message); }   // 血只是效果：出错也不影响打仗
    }

    void Hit(Transform who, float damage, bool kill, Vector2 dir)
    {
        LoadSprites();
        if (_pools.Length == 0) return;
        float k = Mathf.Clamp(Mathf.Abs(who.lossyScale.y), 0.5f, 3f);       // 大块头的血多一点
        Vector3 feet = who.position + new Vector3(0f, feetY * k, 0f);
        int key = who.GetInstanceID();
        float add = (baseSize + damage * sizePerDamage) * k;
        if (_last.TryGetValue(key, out Pool p) && p.sr != null && Vector2.Distance(p.sr.transform.position, feet) < mergeDist * k)
        {
            Grow(p, Mathf.Min(p.target + growPerHit * k, maxScale * k) * (kill ? killMul : 1f));   // 已经有一滩：再大一点
            p.born = Time.time;                                             // 重新算留多久
        }
        else
        {
            p = Make(_pools[Random.Range(0, _pools.Length)], feet, 0.15f * add);
            Grow(p, Mathf.Min(add, maxScale * k) * (kill ? killMul : 1f));
            _last[key] = p;
        }
        Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Random.insideUnitCircle.normalized;
        for (int i = 0; i < dropsPerHit && _drops.Length > 0; i++)          // 顺着挨打的方向溅出去几小滴
        {
            Vector2 off = (d + Random.insideUnitCircle * 0.5f).normalized * Random.Range(dropDist.x, dropDist.y) * k;
            off.y *= 0.55f;                                                 // 俯视：上下方向压扁
            Pool dp = Make(_drops[Random.Range(0, _drops.Length)], feet + (Vector3)off, 0.1f);
            Grow(dp, dropSize * Random.Range(0.7f, 1.3f) * k);
        }
    }

    void Grow(Pool p, float to) { p.from = p.size; p.target = to; p.growAt = Time.time; }

    Pool Make(Sprite s, Vector3 at, float startSize)
    {
        while (_all.Count >= Mathf.Max(8, maxPools))                        // 太多了：删最早的
        {
            if (_all[0].sr != null) Destroy(_all[0].sr.gameObject);
            _all.RemoveAt(0);
        }
        SpriteRenderer sr = new GameObject("Blood").AddComponent<SpriteRenderer>();
        sr.transform.SetParent(transform, false);
        sr.transform.position = new Vector3(at.x, at.y, 0f);
        sr.sprite = s;
        sr.flipX = Random.value < 0.5f;
        sr.sortingOrder = sortingOrder;
        Material lit = RenderMats.SpriteLit;                                // 受光：夜里是暗的
        if (lit != null) sr.sharedMaterial = lit;
        Pool p = new Pool { sr = sr, born = Time.time, size = startSize, target = startSize, from = startSize, growAt = Time.time };
        sr.transform.localScale = Vector3.one * startSize;
        _all.Add(p);
        return p;
    }

    void Update()
    {
        float now = Time.time;
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            Pool p = _all[i];
            if (p.sr == null) { _all.RemoveAt(i); continue; }
            float u = Mathf.Clamp01((now - p.growAt) / Mathf.Max(0.01f, spreadTime));   // 渗开：先快后慢
            p.size = Mathf.Lerp(p.from, p.target, 1f - (1f - u) * (1f - u) * (1f - u));
            p.sr.transform.localScale = Vector3.one * p.size;
            float age = now - p.born - stayTime;                            // 过了留的时间：淡掉
            if (age > 0f)
            {
                float a = 1f - age / Mathf.Max(0.01f, fadeTime);
                if (a <= 0f) { Destroy(p.sr.gameObject); _all.RemoveAt(i); continue; }
                Color c = p.sr.color; c.a = a; p.sr.color = c;
            }
        }
    }
}
