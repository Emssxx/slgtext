using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;   // 滚轮缩放、右键拖动镜头（新输入系统）

// ============================================================================
// AnimTestHub —— 动画测试中心（Scenes/AnimTest）：所有角色 / 怪物 / 建筑的动画都在这里测。
//
// 【用法】运行后左上角有面板：
//   ① 上面一排选类别（角色 / 怪物 / 建筑）；② 下面点名字，就只显示那一个，镜头自动对准；
//   ③ 点「在 Hierarchy 里选中」→ 在 Inspector 里用它自己的测试开关（Test Held、Test Reload、Test Speed……）；
//   ④ 滚轮缩放、按住右键拖动镜头；「重置镜头」回到这一项设好的位置。
//
// 【以后加新东西】把要测的物体放进场景，然后在本组件 Inspector 的 Entries 列表里加一行：
//   类别、名字、物体、镜头大小 / 高度、操作说明。就这么多，不用改代码。
//
// 【它不改什么】只负责显示 / 隐藏列表里的物体和移动镜头，不改它们身上的任何参数。
//   隐藏的物体不运行（换弹之类正在播放的动作会停掉，切回来后重新触发就行）。
// ============================================================================
public class AnimTestHub : MonoBehaviour
{
    public enum Category
    {
        [InspectorName("角色")] Character,
        [InspectorName("怪物")] Monster,
        [InspectorName("建筑")] Building,
    }

    [System.Serializable]
    public class Entry
    {
        public Category category;                 // 类别
        public string label = "";                 // 面板上显示的名字
        public GameObject target;                 // 场景里要测的物体
        public float cameraSize = 0.8f;           // 镜头大小（正交尺寸：越小越近）
        public float cameraY = 0.05f;             // 镜头高度（世界坐标 y）
        [TextArea(1, 4)] public string hint = ""; // 操作说明（显示在面板下面）
    }

    public List<Entry> entries = new List<Entry>();   // 所有测试项（按顺序显示）
    public int startIndex = 0;                         // 一开始显示第几项（从 0 数）

    static readonly string[] CategoryNames = { "角色", "怪物", "建筑" };
    int _current = -1;                                 // 现在显示的是第几项
    Category _tab;                                     // 面板上选中的类别
    Vector2 _scroll;                                   // 列表滚动位置
    Camera _cam;                                       // 主镜头
    Vector2 _lastMouse;                                // 右键拖动：上一帧鼠标位置

    void Start()
    {
        _cam = Camera.main;
        foreach (Entry e in entries)
            if (e.target != null) e.target.SetActive(false);   // 先全部藏起来
        if (entries.Count > 0) Show(Mathf.Clamp(startIndex, 0, entries.Count - 1));
    }

    // 只显示第 i 项，镜头对准它
    void Show(int i)
    {
        for (int j = 0; j < entries.Count; j++)
            if (entries[j].target != null) entries[j].target.SetActive(j == i);   // 只开这一项
        _current = i;
        _tab = entries[i].category;               // 面板跳到它的类别
        ResetCamera();
    }

    // 镜头回到这一项设好的位置和大小（对准物体的 x）
    void ResetCamera()
    {
        if (_cam == null || _current < 0) return;
        Entry e = entries[_current];
        float x = e.target != null ? e.target.transform.position.x : 0f;
        _cam.orthographicSize = Mathf.Max(0.1f, e.cameraSize);
        _cam.transform.position = new Vector3(x, e.cameraY, _cam.transform.position.z);
    }

    void Update()
    {
        if (_cam == null) return;
        Mouse mouse = Mouse.current;                       // 没有鼠标就是 null
        if (mouse == null) return;
        Vector2 pos = mouse.position.ReadValue();
        if (PanelRect().Contains(GuiPoint(pos))) { _lastMouse = pos; return; }   // 鼠标在面板上：不动镜头

        float wheel = mouse.scroll.ReadValue().y;          // 滚轮：往前推 = 放大
        if (Mathf.Abs(wheel) > 0.01f)
            _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize * (wheel > 0f ? 0.9f : 1.1f), 0.1f, 50f);

        if (mouse.rightButton.isPressed)                   // 按住右键拖动：镜头跟着鼠标反方向移动
        {
            Vector2 d = pos - _lastMouse;
            float unitsPerPixel = 2f * _cam.orthographicSize / Screen.height;   // 屏幕 1 像素 = 多少单位
            _cam.transform.position -= new Vector3(d.x, d.y, 0f) * unitsPerPixel;
        }
        _lastMouse = pos;
    }

    // ============ 面板 ============

    float GuiScale() { return Mathf.Max(0.5f, Screen.height / 720f); }   // 屏幕越大面板越大（和战斗测试一样）
    static Rect PanelRect() { return new Rect(8f, 8f, 230f, 330f); }      // 面板位置（缩放前）
    Vector2 GuiPoint(Vector2 screen) { float s = GuiScale(); return new Vector2(screen.x / s, (Screen.height - screen.y) / s); }   // 屏幕坐标 → 面板坐标

    void OnGUI()
    {
        float s = GuiScale();
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        GUILayout.BeginArea(PanelRect(), GUI.skin.box);
        GUILayout.Label("动画测试中心");
        _tab = (Category)GUILayout.Toolbar((int)_tab, CategoryNames);   // 类别

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(150f));
        bool any = false;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e.category != _tab) continue;              // 只列这个类别的
            any = true;
            string name = (i == _current ? "▶ " : "　") + (string.IsNullOrEmpty(e.label) ? ("第 " + (i + 1) + " 项") : e.label);
            if (GUILayout.Button(name)) Show(i);
        }
        if (!any) GUILayout.Label("暂无（之后有动画了再加）");
        GUILayout.EndScrollView();

        if (_current >= 0)
        {
            Entry cur = entries[_current];
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重置镜头")) ResetCamera();
#if UNITY_EDITOR
            if (GUILayout.Button("在 Hierarchy 里选中") && cur.target != null)
                UnityEditor.Selection.activeGameObject = cur.target;   // 选中后 Inspector 里就能调它的测试开关
#endif
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(cur.hint)) GUILayout.Label(cur.hint);   // 这一项的操作说明
        }
        GUILayout.Label("滚轮缩放，按住右键拖动镜头");
        GUILayout.EndArea();
    }
}
