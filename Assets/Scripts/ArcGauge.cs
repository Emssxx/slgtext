using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================
// 圆形格子刻度环（资源栏用）：一圈 segments 个一样大的小方格，从正上方顺时针排。
//   fill（0~1）决定亮几格（有一点就亮一格）：没亮的格子直接隐藏。
//   常用法：同一个位置放两层——底层 SetFill(1)、很淡（刻度底），上层 SetFill(比例)、正常亮度。
//   每一格都是普通的 Image（不用自定义网格，保证一定画得出来），颜色交给 HudGlow 管（见 Segments）。
//   所有格子大小完全一样，没有长短刻度。
// 用法：ArcGauge g = go.AddComponent<ArcGauge>(); g.Build(直径); g.SetFill(0.4f);
// ============================================================================
public class ArcGauge : MonoBehaviour
{
    [Range(4, 60)] public int segments = 24;
    public float thickness = 5f;          // 格子的径向厚度（界面单位）
    public float gapDeg = 4f;             // 格子之间的缝（度）
    public float startDeg = 90f;          // 起点（0° = 右，90° = 上），顺时针排
    [Range(0f, 1f)] public float fill = 1f;

    readonly List<Image> _segs = new List<Image>();
    public IReadOnlyList<Image> Segments => _segs;

    // 按直径生成格子（重复调用会重建）。
    public void Build(float diameter)
    {
        foreach (Image s in _segs) if (s != null) Destroy(s.gameObject);
        _segs.Clear();
        int n = Mathf.Max(4, segments);
        float step = 360f / n;
        float rc = diameter * 0.5f - thickness * 0.5f;                                  // 格子中心所在的半径
        float w = 2f * rc * Mathf.Sin(Mathf.Max(1f, step - gapDeg) * 0.5f * Mathf.Deg2Rad); // 格子的切向宽度（弦长）
        for (int i = 0; i < n; i++)
        {
            float a = startDeg - (i + 0.5f) * step;
            GameObject go = new GameObject("Seg" + i, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * rc;
            rt.sizeDelta = new Vector2(w, thickness);
            rt.localEulerAngles = new Vector3(0f, 0f, a - 90f);                         // 宽边沿着圆周
            Image img = go.AddComponent<Image>();
            img.raycastTarget = false;
            _segs.Add(img);
        }
        Apply();
    }

    public void SetFill(float f)
    {
        fill = Mathf.Clamp01(f);
        Apply();
    }

    void Apply()
    {
        int n = _segs.Count;
        int lit = fill <= 0f ? 0 : Mathf.Clamp(Mathf.CeilToInt(fill * n - 0.0001f), 1, n);
        for (int i = 0; i < n; i++)
            if (_segs[i] != null && _segs[i].gameObject.activeSelf != (i < lit)) _segs[i].gameObject.SetActive(i < lit);
    }
}
