using UnityEngine;

// 建造选项按钮：建造面板里选择"要放什么"。
// 点击 → 把 BuildManager 的当前建筑类型切换成自己，并高亮自己、取消同级其他按钮高亮。
public class BuildButton : MonoBehaviour
{
    [Header("建造目标")]
    public BuildableData data; // 拖入 Wall.asset / Box.asset

    [Header("选中状态颜色")]
    public Color selectedColor   = new Color(0.25f, 0.55f, 0.25f, 0.95f); // 绿色 = 已选
    public Color unselectedColor = new Color(0.15f, 0.15f, 0.15f, 0.88f); // 深灰 = 未选

    private UnityEngine.UI.Image _bg; // 背景图，用来改颜色

    void Awake()
    {
        _bg = GetComponent<UnityEngine.UI.Image>();
    }

    // UI Button 的 OnClick 事件调这个。
    public void OnClick()
    {
        if (BuildManager.Instance == null || data == null) return;

        // 把选中的建造物数据传给 BuildManager
        BuildManager.Instance.SelectBuildType(data);

        // 高亮自己，取消同级其他建造按钮的高亮
        UpdateHighlight();
    }

    // 刷新高亮（自己高亮，兄弟节点取消高亮）。
    public void UpdateHighlight()
    {
        if (transform.parent == null) return;
        foreach (Transform sibling in transform.parent)
        {
            var btn = sibling.GetComponent<BuildButton>();
            if (btn == null || btn._bg == null) continue;
            btn._bg.color = (btn == this) ? btn.selectedColor : btn.unselectedColor;
        }
    }
}
