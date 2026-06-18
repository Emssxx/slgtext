using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// 网格点击输入：鼠标点击地图格子 → 把格子编号（如 "G7"）追加到聊天输入栏。
// 不触发任何移动，只插入编号，让玩家自己组织指令。
// 点在 UI 上时不处理（让 UI 正常响应自己的点击事件）。
public class GridClickInput : MonoBehaviour
{
    [Header("引用（Inspector 拖入）")]
    public Camera mainCamera;
    public ChatManager chatManager;

    void Awake()
    {
        if (mainCamera == null) mainCamera = Camera.main;
    }

    void Update()
    {
        if (Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        // 点在 UI 上时不处理，让按钮/输入框正常响应
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (GridManager.Instance == null || chatManager == null) return;

        // 屏幕坐标 → 世界坐标（2D 正交相机：z 用相机距离修正）
        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        float depth = -mainCamera.transform.position.z;
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(
            new Vector3(mouseScreen.x, mouseScreen.y, depth));
        worldPos.z = 0f;

        // 世界坐标 → 格子坐标
        Vector2Int cell = GridManager.Instance.WorldToCell(worldPos);

        // 超出地图范围不处理
        if (!GridManager.Instance.InBounds(cell.x, cell.y)) return;

        // 格子坐标 → 编号（如 "G7"）
        string label = GridManager.Instance.CellToLabel(cell.x, cell.y);

        // 追加到输入框：有内容则加空格分隔，没有就直接写
        string current = chatManager.inputField.text;
        chatManager.inputField.text = string.IsNullOrEmpty(current)
            ? label
            : current + " " + label;

        // 聚焦输入框，光标移到末尾
        chatManager.inputField.ActivateInputField();
        chatManager.inputField.caretPosition = chatManager.inputField.text.Length;
    }
}
