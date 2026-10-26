using UnityEngine;
using UnityEngine.InputSystem; // 新 Input System

// 建造模式总开关：挂在"建造 [B]"按钮上。
// 按 B 键 或 点击按钮 → 进入/退出建造模式，同时显示/隐藏面板和网格。
public class BuildModeToggle : MonoBehaviour
{
    [Header("引用（Inspector 拖入）")]
    public GameObject buildPanel;   // 建造选项面板（平时隐藏）
    public GridOverlay gridOverlay; // 网格线渲染器

    [Tooltip("旧版场景里的建造选项面板（墙 / 箱子两个按钮）。现在建造菜单由 BuildManager 在右下角生成并滑出，默认不再显示旧面板。")]
    public bool showLegacyPanel = false;

    // UI Button 的 OnClick 事件调这个。
    public void OnClick() => Toggle();

    void Update()
    {
        // 输入栏聚焦时不响应快捷键（直接查 inputField.isFocused，比 EventSystem 选中状态更可靠）
        if (ChatManager.InputFocused) return;

        // 新 Input System 写法：检测 B 键按下
        if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            Toggle();
    }

    // 进入/退出建造模式切换。
    void Toggle()
    {
        if (BuildManager.Instance == null) return;

        if (BuildManager.Instance.IsBuildMode)
            Exit();
        else
            Enter();
    }

    void Enter()
    {
        // 进入建造模式（类型在模式内的菜单里选）
        BuildManager.Instance.EnterBuildMode();

        if (buildPanel   != null && showLegacyPanel) buildPanel.SetActive(true);
        if (gridOverlay  != null) gridOverlay.enabled = true;
    }

    public void Exit()
    {
        BuildManager.Instance.ExitBuildMode();

        if (buildPanel  != null) buildPanel.SetActive(false);
        if (gridOverlay != null) gridOverlay.enabled = false;
    }
}
