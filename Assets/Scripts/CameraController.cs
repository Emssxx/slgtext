using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// 摄像机：滚轮缩放（以鼠标所指的位置为中心放大 / 缩小）+ 按住鼠标中键拖动画面。
// 画面不会拖出地图外；缩到比地图还大时自动居中。用不受暂停影响的时间（建造模式也能缩放）。
// 鼠标在界面（对话窗口、日志、建造菜单……）上时滚轮不缩放，留给界面自己滚动。
// 由 HudLayout 调 CameraController.Ensure() 挂到主摄像机上，不用改场景。
// 相机震动（ScreenEffectManager）只是叠加一个偏移，不会把缩放 / 拖动的位置复位。
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    public static CameraController Instance { get; private set; }

    [Header("缩放")]
    [Tooltip("最多放大到开局画面的几倍（2.5 = 放大 2.5 倍）")]
    public float maxZoomIn = 2.5f;
    [Tooltip("最多缩小到开局画面的几倍（1.3 = 看到的范围变成 1.3 倍）")]
    public float maxZoomOut = 1.3f;
    [Tooltip("滚轮每格缩放多少（0.15 = 15%）")]
    [Range(0.02f, 0.5f)] public float zoomStep = 0.15f;
    [Tooltip("缩放跟手程度：越大越快到位")]
    public float zoomSmooth = 14f;
    public bool zoomToCursor = true;

    [Header("拖动")]
    public bool middleMouseDrag = true;
    [Tooltip("画面边缘最多露出地图外多少格")]
    public float edgeMargin = 1f;

    Camera _cam;
    float _baseSize, _targetSize;
    Vector3 _pos;              // 不含震动偏移的"真实"位置
    bool _dragging;
    Vector2 _dragLastScreen;

    public float Zoom => _baseSize > 0f ? _baseSize / _cam.orthographicSize : 1f; // >1 = 放大

    public static CameraController Ensure()
    {
        if (Instance != null) return Instance;
        Camera c = Camera.main;
        if (c == null) return null;
        CameraController cc = c.GetComponent<CameraController>();
        return cc != null ? cc : c.gameObject.AddComponent<CameraController>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _cam = GetComponent<Camera>();
        _baseSize = _cam.orthographicSize;
        _targetSize = _baseSize;
        _pos = transform.position;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void LateUpdate()
    {
        if (_cam == null || !_cam.orthographic) return;
        float dt = Time.unscaledDeltaTime;
        Mouse mouse = Mouse.current;

        // 1. 滚轮 → 目标缩放
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !PointerOverUI())
            {
                int notches = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(scroll) / 120f), 1, 3); // Windows 一格 = 120
                float f = scroll > 0f ? Mathf.Pow(1f - zoomStep, notches) : Mathf.Pow(1f + zoomStep, notches);
                _targetSize = Mathf.Clamp(_targetSize * f, MinSize, MaxSize);
            }
        }

        // 2. 平滑缩放；以鼠标位置为中心（鼠标指着的那个点缩放前后不动）
        float size = _cam.orthographicSize;
        if (!Mathf.Approximately(size, _targetSize))
        {
            float next = Mathf.Lerp(size, _targetSize, 1f - Mathf.Exp(-zoomSmooth * dt));
            if (Mathf.Abs(next - _targetSize) < 0.001f) next = _targetSize;
            if (zoomToCursor && mouse != null && MouseInView(mouse))
            {
                Vector2 sp = mouse.position.ReadValue();
                Vector3 before = ScreenToWorld(sp);
                _cam.orthographicSize = next;
                transform.position = _pos;
                Vector3 after = ScreenToWorld(sp);
                _pos += before - after;
            }
            else _cam.orthographicSize = next;
        }

        // 3. 中键拖动
        if (middleMouseDrag && mouse != null)
        {
            Vector2 sp = mouse.position.ReadValue();
            if (mouse.middleButton.wasPressedThisFrame && !PointerOverUI()) { _dragging = true; _dragLastScreen = sp; }
            if (!mouse.middleButton.isPressed) _dragging = false;
            if (_dragging)
            {
                float unitsPerPixel = _cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);
                Vector2 d = sp - _dragLastScreen;
                _pos -= new Vector3(d.x, d.y, 0f) * unitsPerPixel;
                _dragLastScreen = sp;
            }
        }

        // 4. 不拖出地图；震动只叠加偏移
        _pos = ClampToMap(_pos);
        transform.position = _pos + ScreenEffectManager.ShakeOffset;
    }

    // 外部把镜头对准某处（以后点 NPC 列表跳转等可以用）。
    public void LookAt(Vector3 world)
    {
        _pos = ClampToMap(new Vector3(world.x, world.y, _pos.z));
    }

    float MinSize => _baseSize / Mathf.Max(1f, maxZoomIn);

    float MaxSize
    {
        get
        {
            float max = _baseSize * Mathf.Max(1f, maxZoomOut);
            GridManager g = GridManager.Instance;
            if (g == null) return max;
            // 再大就整张地图都装下还有富余了：没必要
            float mapH = g.gridHeight * g.cellSize + edgeMargin * 2f;
            float mapW = g.gridWidth * g.cellSize + edgeMargin * 2f;
            float fit = Mathf.Max(mapH * 0.5f, mapW * 0.5f / Mathf.Max(0.1f, _cam.aspect));
            return Mathf.Max(_baseSize, Mathf.Min(max, fit));
        }
    }

    Vector3 ClampToMap(Vector3 p)
    {
        GridManager g = GridManager.Instance;
        if (g == null) return p;
        float halfH = _cam.orthographicSize;
        float halfW = halfH * _cam.aspect;
        float minX = g.origin.x - edgeMargin, maxX = g.origin.x + g.gridWidth * g.cellSize + edgeMargin;
        float minY = g.origin.y - edgeMargin, maxY = g.origin.y + g.gridHeight * g.cellSize + edgeMargin;
        p.x = (maxX - minX) <= halfW * 2f ? (minX + maxX) * 0.5f : Mathf.Clamp(p.x, minX + halfW, maxX - halfW);
        p.y = (maxY - minY) <= halfH * 2f ? (minY + maxY) * 0.5f : Mathf.Clamp(p.y, minY + halfH, maxY - halfH);
        return p;
    }

    Vector3 ScreenToWorld(Vector2 sp)
    {
        Vector3 w = _cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -transform.position.z));
        w.z = 0f;
        return w;
    }

    static bool MouseInView(Mouse mouse)
    {
        Vector2 sp = mouse.position.ReadValue();
        return sp.x >= 0f && sp.y >= 0f && sp.x <= Screen.width && sp.y <= Screen.height;
    }

    static bool PointerOverUI()
    {
        if (EventSystem.current == null || Mouse.current == null) return false;
        PointerEventData data = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, results);
        return results.Count > 0;
    }
}
