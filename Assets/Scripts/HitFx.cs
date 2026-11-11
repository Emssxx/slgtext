using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// HitFx —— 着弹点效果（所有 NPC 共用；以后敌人、建筑也可以用）
//   图：Resources/NpcArt/Fx/HitFlesh1~3（肉体）、HitMetal1~3（金属，打到地面也用这组），从着弹点 PSD 导出。
//   肉体：打中人 / 生物时血雾一下炸开，往子弹飞的方向散一点、慢慢往下落、淡出。
//   金属：打中金属 / 地面时火花一闪（自己发亮），再加一点点光照亮周围（URP 2D，微弱）。
// 用法：HitFx.Flesh(...) / HitFx.Metal(...)，手感参数由调用的人传进来（NPC 身上的 Inspector「着弹点效果」）。
//   每个效果是一个单独的小物体，自己播完自己删，不挂在人身上（人死了、删了效果照样播完）。
// ============================================================================
public class HitFx : MonoBehaviour
{
    static Sprite[] _fleshSprites, _metalSprites;     // 读好的图（第一次用时读，所有效果共用）

    SpriteRenderer _sr;                                // 效果图
    UnityEngine.Rendering.Universal.Light2D _light;    // 金属火花照亮周围的光（旧管线下没有）
    bool _isMetal;                                     // 是金属火花（否则是血雾）
    float _t, _life, _size, _lightPeak, _alpha0;       // 播了多久 / 一共多久 / 大小 / 光最亮时的亮度 / 一开始的透明度
    Vector2 _vel;                                      // 血雾飘的速度（单位/秒）

    // 血雾：at = 着弹点（世界坐标）；dir = 子弹飞的方向；variant = 用第几张（0~2，越大越大团）；size = 大小倍数；life = 持续几秒
    public static void Flesh(Vector3 at, Vector2 dir, int variant, float size, float life, int sortingLayerID)
    {
        LoadSprites();
        if (_fleshSprites.Length == 0) return;                                            // 没找到图：不播
        HitFx fx = Make("HitFlesh", _fleshSprites[Mathf.Clamp(variant, 0, _fleshSprites.Length - 1)], at, sortingLayerID);
        fx._sr.flipX = dir.x < 0f;                                                        // 往子弹飞的那边喷
        fx.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-15f, 15f));       // 每次角度有点不一样
        fx._life = Mathf.Max(0.05f, life);
        fx._size = size;
        fx._vel = (dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.zero) * 0.15f;  // 被子弹带着往前散一点
        fx._alpha0 = 0.95f;
        fx.Apply();                                                                       // 第一帧就摆好（不会闪一下原大小）
    }

    // 金属火花（打到地面也用）：lightColor / lightIntensity / lightRadius = 照亮周围的光（亮度 0 = 不照亮）
    public static void Metal(Vector3 at, float size, float life, Color lightColor, float lightIntensity, float lightRadius, int sortingLayerID)
    {
        LoadSprites();
        if (_metalSprites.Length == 0) return;
        HitFx fx = Make("HitMetal", _metalSprites[Random.Range(0, _metalSprites.Length)], at, sortingLayerID);   // 三张随机一张
        fx._isMetal = true;
        fx._sr.flipX = Random.value < 0.5f;                                               // 随机翻转 + 随机角度：每次火花形状都不一样
        fx.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        fx._life = Mathf.Max(0.03f, life);
        fx._size = size;
        fx._alpha0 = 1f;
        fx._lightPeak = lightIntensity * Random.Range(0.7f, 1.2f);                        // 每一下亮度不一样
        if (fx._lightPeak > 0f && RenderMats.IsURP)
            fx._light = WorldLighting.CreatePoint("HitLight", fx.transform, lightColor, fx._lightPeak, lightRadius, 360f);
        fx.Apply();
    }

    // 生成效果物体：不受光（自己发亮，亚空间很暗也看得见），画在人和建筑上面
    static HitFx Make(string name, Sprite sprite, Vector3 at, int sortingLayerID)
    {
        GameObject go = new GameObject(name);
        go.transform.position = at;
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerID = sortingLayerID;                                               // 和人同一个排序层
        sr.sortingOrder = 61;                                                             // 在子弹（60）上面
        RenderMats.MakeUnlit(sr);
        HitFx fx = go.AddComponent<HitFx>();
        fx._sr = sr;
        return fx;
    }

    // 第一次用时读图（找不到的跳过）
    static void LoadSprites()
    {
        if (_fleshSprites != null) return;
        _fleshSprites = LoadSet("HitFlesh");
        _metalSprites = LoadSet("HitMetal");
    }

    static Sprite[] LoadSet(string prefix)
    {
        List<Sprite> list = new List<Sprite>();
        for (int i = 1; i <= 3; i++)
        {
            Sprite s = Resources.Load<Sprite>("NpcArt/Fx/" + prefix + i);
            if (s != null) list.Add(s);
            else Debug.LogWarning("[HitFx] 找不到素材：NpcArt/Fx/" + prefix + i);
        }
        return list.ToArray();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;                                                             // 暂停时不动
        _t += dt;
        if (!_isMetal)                                                                    // 血雾：往前散、慢慢减速、往下落
        {
            _vel *= Mathf.Max(0f, 1f - 4f * dt);
            _vel.y -= 0.35f * dt;
            transform.position += new Vector3(_vel.x * dt, _vel.y * dt, 0f);
        }
        Apply();
        if (_t >= _life) Destroy(gameObject);                                             // 播完：删掉（光也一起删）
    }

    // 按播放进度摆大小 / 透明度 / 光
    void Apply()
    {
        float u = Mathf.Clamp01(_t / _life);                                              // 进度 0~1
        float s, a;
        if (_isMetal)
        {
            float e = 1f - (1f - u) * (1f - u) * (1f - u);                               // 一下炸开（先快后慢）
            s = _size * Mathf.Lerp(0.7f, 1.15f, e);
            a = _alpha0 * (1f - u * u);                                                   // 前面一直很亮，最后一下灭掉
            if (_light != null) _light.intensity = _lightPeak * (1f - u) * (1f - u) * Random.Range(0.85f, 1f);   // 光一闪就暗下去
        }
        else
        {
            float pop = Mathf.Clamp01(_t / 0.06f);                                        // 前 0.06 秒：一下炸开
            s = _size * (0.5f + 0.5f * (1f - (1f - pop) * (1f - pop))) * (1f + 0.15f * u);   // 之后再慢慢散大一点
            a = _alpha0 * (u < 0.3f ? 1f : 1f - Mathf.Pow((u - 0.3f) / 0.7f, 1.5f));      // 先停留一下，再淡出
        }
        transform.localScale = new Vector3(s, s, 1f);
        Color c = _sr.color; c.a = a; _sr.color = c;
    }
}
