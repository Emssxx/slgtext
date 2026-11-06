using UnityEngine;
using UnityEngine.UI;

// ============================================================================
// 3/4 圆的格子刻度进度（顶部资源栏用）：缺口朝下，从左下（225°）顺时针转到右下（-45°），一共 270°。
//   一圈切成 segments 个小格，格子之间留 gapDeg 度的缝；fill（0~1）决定亮几格（有一点就亮一格）。
//   常用法：同一个位置放两层——底层 fill = 1、很淡（刻度底），上层 fill = 比例、正常亮度。
//   颜色用 Graphic.color（交给 HudGlow 管：平时淡绿，不足时淡黄 + 闪烁）。
// ============================================================================
public class ArcGauge : MaskableGraphic
{
    [Range(4, 60)] public int segments = 18;
    public float thickness = 4f;          // 环的粗细（界面单位）
    public float gapDeg = 4f;             // 格子之间的缝（度）
    public float startDeg = 225f;         // 起点（0° = 右，90° = 上）
    public float sweepDeg = 270f;         // 一共转多少度（顺时针）
    [Range(0f, 1f)] public float fill = 1f;
    [Tooltip("每隔几格画一个长一点的刻度（0 = 不画）")]
    public int majorEvery = 3;
    public float majorExtra = 2f;         // 长刻度往外多出多少

    public void SetFill(float f)
    {
        f = Mathf.Clamp01(f);
        if (Mathf.Abs(f - fill) < 0.0005f) return;
        fill = f;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        Vector2 c = r.center;
        float outer = Mathf.Min(r.width, r.height) * 0.5f - majorExtra;
        float inner = Mathf.Max(0f, outer - thickness);
        int lit = fill <= 0f ? 0 : Mathf.Clamp(Mathf.CeilToInt(fill * segments - 0.0001f), 1, segments);
        float step = sweepDeg / segments;
        Color32 col = color;
        for (int i = 0; i < lit; i++)
        {
            float a0 = (startDeg - i * step - gapDeg * 0.5f) * Mathf.Deg2Rad;
            float a1 = (startDeg - (i + 1) * step + gapDeg * 0.5f) * Mathf.Deg2Rad;
            float o = outer + (majorEvery > 0 && i % majorEvery == 0 ? majorExtra : 0f);
            Quad(vh, c, a0, a1, inner, o, col);
        }
    }

    static void Quad(VertexHelper vh, Vector2 c, float a0, float a1, float rIn, float rOut, Color32 col)
    {
        Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
        Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
        int k = vh.currentVertCount;
        vh.AddVert(c + d0 * rIn, col, Vector2.zero);
        vh.AddVert(c + d0 * rOut, col, Vector2.zero);
        vh.AddVert(c + d1 * rOut, col, Vector2.zero);
        vh.AddVert(c + d1 * rIn, col, Vector2.zero);
        vh.AddTriangle(k, k + 1, k + 2);
        vh.AddTriangle(k + 2, k + 3, k);
    }
}
