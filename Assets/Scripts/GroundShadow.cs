using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;   // 2D 光（Light2D）

// ============================================================================
// 人 / 敌人脚下的影子（URP 2D 光）：附近有会投影子的灯（手电筒、终端机、枪火）照到他时，
// 从脚底往"背着光"的方向拉出一条柔和的黑影，离灯越远影子越长、越淡。
//
// 【为什么不用 Unity 自带的投影体】人是二十几块图拼的、朝左时整个左右翻，自带的投影体会把人自己的身体也盖黑。
//   这里的影子是画在人下面的一张图，只落在地上，不会盖住人。
// 【谁会挂】WorldLighting 每秒检查一次，自动给所有 NPC 和敌人挂上；旧管线下什么都不做。
// ============================================================================
public class GroundShadow : MonoBehaviour
{
    public float feetY = -0.453125f;   // 脚底在物体位置下面多少（NPC / 敌人都是这个）
    public float width = 0.32f;        // 影子多宽（单位）
    public float minLength = 0.35f;    // 离灯很近时影子多长
    public float maxLength = 1.6f;     // 最长多长
    public int sortingOrder = -100;    // 在 NPC 自己的图下面（NPC 的部件都在同一个 SortingGroup 里）

    const int MaxShadows = 2;          // 同时最多几条影子（取照得最亮的两盏灯）
    readonly SpriteRenderer[] _sr = new SpriteRenderer[MaxShadows];
    readonly Light2D[] _best = new Light2D[MaxShadows];
    readonly float[] _bestK = new float[MaxShadows];
    static Sprite _sprite;             // 影子图（代码画，所有人共用）

    void LateUpdate()
    {
        WorldLighting w = WorldLighting.Instance;
        bool on = RenderMats.IsURP && w != null && w.shadows;
        for (int i = 0; i < MaxShadows; i++) { _best[i] = null; _bestK[i] = 0f; }
        Vector3 feet = transform.position + new Vector3(0f, feetY, 0f);
        if (on)
        {
            // 找照得最亮的两盏灯（自己身上的灯不算）
            List<Light2D> lights = WorldLighting.ShadowLights;
            for (int i = 0; i < lights.Count; i++)
            {
                Light2D l = lights[i];
                if (l == null || !l.isActiveAndEnabled || l.intensity <= 0.01f) continue;
                if (l.transform.IsChildOf(transform)) continue;                    // 自己的手电筒不给自己投影子
                Vector2 d = feet - l.transform.position;
                float dist = d.magnitude, r = l.pointLightOuterRadius;
                if (dist >= r || dist < 0.05f) continue;                          // 照不到 / 贴在灯上
                if (l.pointLightOuterAngle < 359f && Vector2.Angle(l.transform.up, d) > l.pointLightOuterAngle * 0.5f) continue;   // 在聚光的光束外面
                float k = l.intensity * (1f - dist / r);                          // 这盏灯照到他有多亮
                for (int j = 0; j < MaxShadows; j++)                              // 插进前两名
                {
                    if (k <= _bestK[j]) continue;
                    for (int m = MaxShadows - 1; m > j; m--) { _best[m] = _best[m - 1]; _bestK[m] = _bestK[m - 1]; }
                    _best[j] = l; _bestK[j] = k;
                    break;
                }
            }
        }
        for (int i = 0; i < MaxShadows; i++)
        {
            Light2D l = _best[i];
            if (l == null) { if (_sr[i] != null) _sr[i].enabled = false; continue; }
            SpriteRenderer sr = Get(i);
            sr.enabled = true;
            Vector2 d = feet - l.transform.position;                              // 从灯指向脚：影子往这个方向拉
            float dist = d.magnitude, r = Mathf.Max(0.01f, l.pointLightOuterRadius);
            float len = Mathf.Lerp(minLength, maxLength, Mathf.Clamp01(dist / r));   // 离灯越远影子越长
            sr.transform.position = feet;
            sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            sr.transform.localScale = new Vector3(len, width / 0.5f, 1f);           // 图本身长 1、宽 0.5 单位
            float a = Mathf.Clamp01(_bestK[i]) * w.shadowStrength * 0.85f;        // 照得越亮影子越深
            sr.color = new Color(0f, 0f, 0f, a);
        }
    }

    // 第 i 条影子的图（第一次用时生成）
    SpriteRenderer Get(int i)
    {
        if (_sr[i] != null) return _sr[i];
        if (_sprite == null) _sprite = MakeSprite();
        SpriteRenderer sr = new GameObject("GroundShadow").AddComponent<SpriteRenderer>();
        sr.transform.SetParent(transform, true);
        sr.sprite = _sprite;
        sr.sortingOrder = sortingOrder;
        RenderMats.MakeUnlit(sr);                                                 // 影子本身不受光（黑色半透明）
        _sr[i] = sr;
        return sr;
    }

    // 影子图：从左边（脚底）往右拉长的柔和椭圆，靠脚的地方最深、往外渐淡
    static Sprite MakeSprite()
    {
        const int w = 64, h = 32;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float u = x / (float)(w - 1);                                     // 0 = 脚底，1 = 影子尖
                float v = (y - (h - 1) * 0.5f) / ((h - 1) * 0.5f);                // -1 ~ 1
                float half = Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(u * 2f - 0.9f) / 1.1f, 2f)));   // 椭圆的半宽
                float edge = Mathf.Clamp01((half - Mathf.Abs(v)) / 0.35f);          // 边缘柔和
                float fade = Mathf.Clamp01(1f - u * 0.85f);                       // 越往外越淡
                t.SetPixel(x, y, new Color(1f, 1f, 1f, edge * fade));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.06f, 0.5f), 64f);   // 1 单位长；轴心靠左 = 脚底
    }
}
