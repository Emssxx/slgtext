using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 语言切换按钮：点击调用 LocalizationManager.Toggle() 切换中/日；
// 按钮上的文字用 btn_language 这个翻译 key，语言切换时自动跟着刷新。
// 写法照搬 NpcCardUI.cs 里订阅 OnLanguageChanged 的方式，不碰 LocalizationManager 的核心逻辑。
public class LanguageToggleButton : MonoBehaviour
{
    public Button button;   // 按钮组件（Inspector 拖入）
    public TMP_Text label;  // 按钮上显示文字的组件（Inspector 拖入）

    // 订阅语言切换事件：语言一变就刷新按钮文字。
    void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += Refresh;
    }

    void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= Refresh;
    }

    // Start 在所有物体的 Awake 都跑完之后才执行，
    // 这样能保证 LocalizationManager.Instance 已经准备好，第一次显示不会出错。
    void Start()
    {
        button.onClick.AddListener(OnClick);
        Refresh();
    }

    void OnClick()
    {
        LocalizationManager.Instance.Toggle();
    }

    void Refresh()
    {
        if (LocalizationManager.Instance != null)
            label.text = LocalizationManager.Instance.Get("btn_language");
    }
}
