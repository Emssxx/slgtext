using UnityEngine;
using UnityEngine.InputSystem; // 新 Input System

// 建造按钮总开关：挂在"建造 [B]"按钮上。
// 按 B 键 或 点击按钮（底部任务栏的 blueprint）→ 打开 / 关闭蓝图规划（BlueprintDesigner）。
// 以前的建造模式（暂停时间、地图格子变绿变红）不再使用：建造都改成画蓝图 → 发给 NPC 指挥建造。
public class BuildModeToggle : MonoBehaviour
{
    [Header("引用（Inspector 拖入）")]
    public GameObject buildPanel;   // 旧版建造选项面板（不再使用，保留引用以免场景丢失）
    public GridOverlay gridOverlay; // 旧版网格线渲染器（不再使用）

    [Tooltip("旧版场景里的建造选项面板（墙 / 箱子两个按钮）。现在建造在蓝图规划里进行，默认不再显示旧面板。")]
    public bool showLegacyPanel = false;

    // UI Button 的 OnClick 事件调这个。
    public void OnClick() => Toggle();

    void Start()
    {
        BlueprintDesigner.Ensure();   // 提前建好（第一次打开不卡）
    }

    void Update()
    {
        // 输入栏聚焦时不响应快捷键（直接查 inputField.isFocused，比 EventSystem 选中状态更可靠）
        if (ChatManager.InputFocused) return;
        if (OpeningDirector.Playing) return;   // 开场演出中

        // 新 Input System 写法：检测 B 键按下
        if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            Toggle();
    }

    // 打开 / 关闭蓝图规划。
    void Toggle()
    {
        BlueprintDesigner d = BlueprintDesigner.Ensure();
        if (d.IsOpen && !d.Closing) Exit();
        else Enter();
    }

    void Enter()
    {
        // 旧的建造模式万一开着（比如别处调了 EnterBuildMode）：先退出，恢复时间
        if (BuildManager.Instance != null && BuildManager.Instance.IsBuildMode) BuildManager.Instance.ExitBuildMode();
        BlueprintDesigner.Ensure().Open();

        if (buildPanel != null && showLegacyPanel) buildPanel.SetActive(true);
    }

    public void Exit()
    {
        if (BlueprintDesigner.Instance != null && BlueprintDesigner.Instance.IsOpen) BlueprintDesigner.Instance.Close();
        if (BuildManager.Instance != null && BuildManager.Instance.IsBuildMode) BuildManager.Instance.ExitBuildMode();

        if (buildPanel != null) buildPanel.SetActive(false);
        if (gridOverlay != null) gridOverlay.enabled = false;
    }
}
