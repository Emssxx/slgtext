using System;
using System.Collections.Generic;
using UnityEngine;

// 语言枚举。要加更多语言就往这里加。
public enum Language { Chinese, Japanese }

// 语言管理器（单例）：管理当前语言、翻译表，提供取文本的方法。
// 切换语言时触发 OnLanguageChanged 事件，UI 监听它来刷新。
public class LocalizationManager : MonoBehaviour
{
    public static LocalizationManager Instance;

    public Language current = Language.Chinese; // 当前语言，默认中文

    // 语言切换时通知（UI 订阅它来刷新自己）。
    public static event Action OnLanguageChanged;

    // 翻译表：key → (中文, 日文)。以后加文本就往这里加一行。
    private Dictionary<string, (string zh, string ja)> _table;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildTable();
    }

    // 构建翻译表。每个 key 对应中文和日文两种写法。
    void BuildTable()
    {
        _table = new Dictionary<string, (string, string)>
        {
            // —— NPC 状态卡 ——
            { "stat_health",  ("生命", "生命") },
            { "stat_hunger",  ("饥饿", "空腹") },
            { "stat_energy",  ("体力", "体力") },
            { "stat_sanity",  ("理智", "正気") },

            // —— 系统提示 ——
            { "day_start",    ("新的一天开始了", "新しい一日が始まった") },
            { "go_to",        ("前往", "向かう") },
            { "start_action", ("开始行动", "行動開始") },

            // —— 精神状态前缀 ——
            { "state_dazed",        ("（眼神涣散）", "（うつろな目）") },
            { "state_hallucinate",  ("（声音发颤）", "（声が震えている）") },
            { "state_broken",       ("（已不成人形）", "（もはや人の形ではない）") },

            // —— 按钮/界面 ——
            { "btn_language", ("日本語", "中文") }, // 按钮显示"切换到另一种语言"
            { "btn_build",    ("建造  [B]", "建築  [B]") }, // 建造模式切换按钮（含快捷键提示）

            // —— 对话框标题 / 输入框 ——
            { "chat_panel_title",  ("信息", "メッセージ") },
            { "chat_send",         ("发送", "送信") },
            { "chat_placeholder",  ("说点什么...", "何か話しかけて...") },

            // —— 系统提示 / 事件 ——
            { "system_speaker", ("系统", "システム") },
            { "danger_approach", ("【危险】有什么东西靠近了！", "【危険】何かが近づいてくる！") },
            { "now_talking_with", ("你现在正在和", "今話している相手は") },

            // —— 新UI：对话框三栏标题 ——
            { "tab_chat", ("对话", "会話") },
            { "tab_event", ("事件", "イベント") },
            { "tab_log", ("日志", "ログ") },

            // —— 新UI：NPC列表 / 卡片 ——
            { "npc_list_title", ("幸存者列表", "生存者リスト") },
            { "btn_close", ("关闭", "閉じる") },

            // —— 新UI：建造菜单（顺便备好，以后用）——
            { "build_menu_title", ("建造", "建築") },
            { "build_wall", ("墙", "壁") },
            { "build_box", ("箱子", "箱") },

            // —— 临时调试面板（EventDebugPanel，测试完记得连这几行一起删）——
            { "debug_space_warp",     ("空间扭曲", "空間の歪み") },
            { "debug_time_disorder",  ("时间失序", "時間の乱れ") },
            { "debug_whisper_vision", ("低语与幻象", "囁きと幻影") },

            // 以后要加文本，继续往这里加：{ "key", ("中文", "日文") },
        };
    }

    // 取当前语言的文本。找不到 key 就原样返回 key（方便发现漏翻译）。
    public string Get(string key)
    {
        if (_table != null && _table.TryGetValue(key, out var pair))
            return current == Language.Chinese ? pair.zh : pair.ja;
        return key; // 没找到，返回 key 本身提醒你补
    }

    // 切换语言（在中/日之间切）。
    public void Toggle()
    {
        current = (current == Language.Chinese) ? Language.Japanese : Language.Chinese;
        OnLanguageChanged?.Invoke(); // 通知所有监听者刷新
    }

    // 给 AI 提示词用：返回当前语言的名字（让 AI 用这个语言生成）。
    public string GetAILanguageInstruction()
    {
        return current == Language.Japanese
            ? "请务必用日语（自然的日本语）回答所有内容。"
            : "请用中文回答所有内容。";
    }
}
