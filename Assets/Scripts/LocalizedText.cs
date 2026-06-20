using UnityEngine;
using TMPro;

// 通用本地化文字组件：挂在任意 TMP Text 节点上，填好 key，
// 启动时和每次切换语言时自动从 LocalizationManager 取对应文字。
public class LocalizedText : MonoBehaviour
{
    public string key; // 对应 LocalizationManager 翻译表里的 key

    TMP_Text _text;

    void Awake()
    {
        _text = GetComponent<TMP_Text>(); // 取同节点的 TMP_Text 组件
    }

    void Start()
    {
        LocalizationManager.OnLanguageChanged += Refresh; // 订阅语言切换事件
        Refresh(); // 启动时先刷一次，确保初始语言正确
    }

    void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= Refresh; // 销毁时取消订阅，防止内存泄漏
    }

    // 从 LocalizationManager 取对应 key 的文字并写入 TMP_Text。
    void Refresh()
    {
        if (_text != null && LocalizationManager.Instance != null)
            _text.text = LocalizationManager.Instance.Get(key);
    }
}
