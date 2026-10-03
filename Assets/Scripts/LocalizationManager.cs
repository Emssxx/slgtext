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

            // —— AI模型调试UI（AIModelDebugUI，右下角展示当前模型）——
            { "ai_debug_label_local",   ("本地", "ローカル") },   // 用本地Ollama时的前缀
            { "ai_debug_label_cloud",   ("云端", "クラウド") },   // 用云端Claude时的前缀

            // —— 事件横幅（ScreenEffectManager.PlayEventEffect，屏幕正中短暂出现的大字）——
            { "event_banner_space_warp",     ("空间扭曲", "空間の歪み") },
            { "event_banner_time_disorder",  ("时间失序", "時間の乱れ") },
            { "event_banner_whisper_vision", ("低语与幻象", "囁きと幻影") },

            // —— NPC行为日志（NpcDecisionRules.Execute写进日志栏，{0}=NPC名字）——
            { "log_eat_success", ("{0} 吃了一个罐头。", "{0}は缶詰を食べた。") },
            { "log_eat_fail",    ("{0} 想吃东西，但仓库空了。", "{0}は何か食べようとしたが、倉庫は空だった。") },
            { "log_rest",        ("{0} 休息了一会儿。", "{0}はしばらく休んだ。") },
            { "log_gather",      ("{0} 搜寻带回了 1 个罐头。", "{0}は捜索して缶詰を1つ持ち帰った。") },
            { "log_flee",        ("{0} 因为身体危急在躲避危险。", "{0}は身の危険を感じて避難している。") },
            { "log_idle",        ("{0} 在营地待命。", "{0}は野営地で待機している。") },
            { "log_space_warp_victim", ("{0} 被空间扭曲卷走，出现在了别处。", "{0}は空間の歪みに飲み込まれ、別の場所に現れた。") },

            // —— 主界面（Main Menu）——
            { "btn_start", ("开始游戏", "ゲーム開始") },
            { "btn_quit", ("退出", "終了") },
            { "provider_local", ("本地", "ローカル") },
            { "provider_prefix", ("AI：", "AI：") },
            { "menu_hint", ("使用云端AI需要各自的API key。", "クラウドAIを使う場合は、各自のAPIキーが必要です。") },

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

    // 取当前语言的文本，并用 string.Format 填入参数（模板里写 {0}{1}... 的场景，比如日志里插入NPC名字）。
    public string GetFormat(string key, params object[] args)
    {
        return string.Format(Get(key), args);
    }

    // 切换语言（在中/日之间切）。
    public void Toggle()
    {
        current = (current == Language.Chinese) ? Language.Japanese : Language.Chinese;
        OnLanguageChanged?.Invoke(); // 通知所有监听者刷新
    }

    // 给 AI 提示词用：返回强力的语言指令，确保 AI 按当前界面语言输出。
    // 放在提示词【最末尾】，用强硬明确的措辞，避免 AI 漂到别的语言
    //（尤其换云端模型时，弱指令容易被提示词主体的语言带跑）。
    public string GetAILanguageInstruction()
    {
        if (current == Language.Japanese)
        {
            return
                "\n\n==============================\n" +
                "【最重要・言語指定（厳守）】\n" +
                "上記の設定（世界観・人物設定・状態など）は中国語で書かれているが、" +
                "それはあくまで「あなたへの資料」である。\n" +
                "あなたの出力（JSON内の dialogue などすべての文字列）は、" +
                "必ず「自然な日本語」だけで書くこと。\n" +
                "・中国語の単語や漢字表現をそのまま混ぜてはならない。\n" +
                "・中国語の設定は、意味を汲み取った上で、必ず日本語に「翻訳」して表現すること。\n" +
                "・一文の中に中国語と日本語が混在することは、絶対に許されない。\n" +
                "出力はすべて日本語。これを最優先で厳守すること。\n" +
                "==============================";
        }
        else
        {
            return
                "\n\n==============================\n" +
                "【最重要·语言指定（严格遵守）】\n" +
                "你的输出（JSON里的 dialogue 等所有字符串），必须只用「自然的中文」书写。\n" +
                "・绝对不要混入日语单词或假名。\n" +
                "・一句话里中文和日语混用，是绝对不允许的。\n" +
                "输出全部用中文。请最优先严格遵守。\n" +
                "==============================";
        }
    }
}
