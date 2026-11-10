using UnityEngine;
using UnityEngine.Rendering;   // URP 的"相机画完"回调

// 网格线渲染器：建造模式下在地图上画半透明网格线。
// 读 GridManager 的尺寸设置，用 GL 直接画线，零额外物体开销。
// 由 BuildModeToggle 控制 enabled 开/关。
[RequireComponent(typeof(Camera))]
public class GridOverlay : MonoBehaviour
{
    public Color lineColor = new Color(1f, 1f, 1f, 0.2f); // 白色半透明

    private Material _mat;

    void Awake()
    {
        // Internal-Colored 是 Unity 内置的纯色不受光照影响的 Shader
        _mat = new Material(Shader.Find("Hidden/Internal-Colored"));
        _mat.hideFlags = HideFlags.HideAndDontSave;
        _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _mat.SetInt("_Cull",    (int)UnityEngine.Rendering.CullMode.Off);
        _mat.SetInt("_ZWrite",  0);
    }

    Camera _cam;                   // 挂在哪个相机上

    // URP 不会调用 OnPostRender，改用"某个相机画完"的回调（旧管线照样走 OnPostRender）
    void OnEnable()
    {
        _cam = GetComponent<Camera>();
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
    }

    void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam == _cam) DrawGrid();   // 只在自己这个相机上画
    }

    // 旧管线（内置管线）：OnPostRender 在相机渲染完场景后调用，画在场景最上层。
    void OnPostRender()
    {
        DrawGrid();
    }

    // 画网格线
    void DrawGrid()
    {
        if (GridManager.Instance == null) return;
        // 不在建造模式就不画（Esc 退出建造模式时不会经过 BuildModeToggle，之前网格线会一直留着）
        if (BuildManager.Instance == null || !BuildManager.Instance.IsBuildMode) return;

        var gm = GridManager.Instance;
        float ox = gm.origin.x;
        float oy = gm.origin.y;
        float totalW = gm.gridWidth  * gm.cellSize;
        float totalH = gm.gridHeight * gm.cellSize;

        GL.PushMatrix();
        if (_cam != null)                                       // 明确用这个相机的视角画（URP 的回调里不会自动设好）
        {
            GL.LoadProjectionMatrix(_cam.projectionMatrix);
            GL.modelview = _cam.worldToCameraMatrix;
        }
        _mat.SetPass(0);
        GL.Begin(GL.LINES);
        GL.Color(lineColor);

        // 竖线（每列左边 + 最右边）
        for (int x = 0; x <= gm.gridWidth; x++)
        {
            float wx = ox + x * gm.cellSize;
            GL.Vertex3(wx, oy,           0f);
            GL.Vertex3(wx, oy + totalH,  0f);
        }

        // 横线（每行下边 + 最顶边）
        for (int y = 0; y <= gm.gridHeight; y++)
        {
            float wy = oy + y * gm.cellSize;
            GL.Vertex3(ox,          wy, 0f);
            GL.Vertex3(ox + totalW, wy, 0f);
        }

        GL.End();
        GL.PopMatrix();
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }
}
