using UnityEngine;
using UnityEngine.InputSystem; // 新 Input System

// 建造模式总开关：挂在"建造 [B]"按钮上。
// 按 B 键 或 点击按钮 → 进入/退出建造模式，同时显示/隐藏面板和网格。
public class BuildModeToggle : MonoBehaviour
{
    [Header("引用（Inspector 拖入）")]
    public GameObject buildPanel;   // 建造选项面板（平时隐藏）
    public GridOverlay gridOverlay; // 网格线渲染器

    // UI Button 的 OnClick 事件调这个。
    public void OnClick() => Toggle();

    void Update()
    {
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
        // 用 BuildManager 现在设定的建筑类型进入（默认是 Wall，由 Inspector 设好）
        BuildManager.Instance.EnterBuildMode(
            BuildManager.Instance.currentBuildingPrefab,
            BuildManager.Instance.currentBuildingName);

        if (buildPanel   != null) buildPanel.SetActive(true);
        if (gridOverlay  != null) gridOverlay.enabled = true;
    }

    public void Exit()
    {
        BuildManager.Instance.ExitBuildMode();

        if (buildPanel  != null) buildPanel.SetActive(false);
        if (gridOverlay != null) gridOverlay.enabled = false;
    }
}
