using UnityEngine;
using TMPro;

// 通用本地化文本组件：挂在任何需要跟随语言切换的 UI 文本上。
// 用法：把这个脚本挂到带 TMP_Text 的 UI 物体上，在 Inspector 填一个 key（如 "tab_chat"），
// 它会自动在游戏开始时、以及每次语言切换时，把文本设成对应语言的翻译。
// 省去给每个 UI 手写监听的麻烦。
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("翻译表里的 key，如 tab_chat / tab_event / btn_close")]
    public string key;

    private TMP_Text _text;

    void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        // 订阅语言切换事件，切换时自动刷新。
        LocalizationManager.OnLanguageChanged += Refresh;
        Refresh(); // 启用时先刷一次（保证初始语言正确）
    }

    void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= Refresh;
    }

    // 按当前语言刷新文本。外部代码动态挂载时可手动调用一次。
    public void Refresh()
    {
        if (_text == null || string.IsNullOrEmpty(key)) return;
        if (LocalizationManager.Instance == null) return;
        _text.text = LocalizationManager.Instance.Get(key);
    }
}
