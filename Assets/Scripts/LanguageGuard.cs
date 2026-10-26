using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// 语言守卫（纯规则，最后一道保险）：检查 AI 的回复是不是"当前界面语言"，不对就纠正。
//
// 为什么需要它：本地 7B 小模型的语言控制力有限，光靠提示词无法 100% 保证不说错语言。
// 所以由【代码】来做最终判断（符合"LLM 配音、代码做决定"的架构）：
//   1. 检查回复里的文字字段（dialogue / event / reply / reason）。
//   2. 语言不对 → 带上纠正提示重试（最多 N 次，由 AIClientHub 控制）。
//   3. 还不对 → 让模型做"翻译"（翻译任务对小模型来说简单得多，非常可靠）。
//   4. 翻译还不对 → 换成一句安全的占位台词。玩家永远不会看到错误语言。
//
// 判断方法（不依赖任何外部库）：
//   · 日语：一定有平假名/片假名；且不会出现简体字专用字（这、们、说、你…）。
//   · 中文：不会出现平假名/片假名。
public static class LanguageGuard
{
    // 当前界面是否日语。
    public static bool IsJp => LocalizationManager.Instance != null
                               && LocalizationManager.Instance.current == Language.Japanese;

    // 按当前语言二选一（写双语提示词用）。
    public static string Pick(string zh, string ja) => IsJp ? ja : zh;

    // 要检查/修复的 JSON 文字字段（type、action、destination 这类英文代码不检查，也不翻译）。
    static readonly HashSet<string> TextKeys = new HashSet<string> { "dialogue", "event", "reply", "reason" };

    // 简体中文专用字：这些字在正常日语里几乎不会出现，出现 2 个以上就判定混入了中文。
    const string SimplifiedOnly = "这们么吗呢说还对让个为时过边现开关见东样话请谁问题听觉吧啊哪她给从你怎儿别两发动气门车长头实无种应该处经进认识欢爱难电脑业";

    // ================= 判断 =================

    // 这段回复的语言对不对。strict=true：重试阶段的严格检查；false：翻译后的宽松检查。
    public static bool IsCorrect(string raw, bool json, Language lang, bool strict = true)
    {
        if (string.IsNullOrEmpty(raw)) return true;
        foreach (string text in ExtractTexts(raw, json))
            if (!TextMatches(text, lang, strict)) return false;
        return true;
    }

    // 单段文字是否符合目标语言。
    public static bool TextMatches(string text, Language lang, bool strict = true)
    {
        if (string.IsNullOrEmpty(text)) return true;
        int kana = 0, han = 0, simplified = 0;
        foreach (char c in text)
        {
            if ((c >= 'ぁ' && c <= 'ゟ') || (c >= 'ァ' && c <= 'ヺ') || c == 'ー') kana++; // 平假名/片假名/长音
            else if ((c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿')) han++;              // 汉字
            if (SimplifiedOnly.IndexOf(c) >= 0) simplified++;
        }

        if (lang == Language.Japanese)
        {
            if (kana + han < 2) return true;                  // 太短（如"……"），不判断
            if (simplified >= 2) return false;                // 混入了简体中文
            if (!strict) return true;                         // 宽松模式到此为止
            if (kana == 0 && han >= 3) return false;          // 全是汉字没有假名 → 中文
            if (han >= 10 && kana < han * 0.12f) return false; // 长句几乎全是汉字 → 大概率中文
            return true;
        }
        else // 中文
        {
            return kana < 2; // 中文里不该出现假名（允许 1 个误差）
        }
    }

    // 从回复里取出要检查的文字：JSON 取指定字段，否则整段都算。
    static List<string> ExtractTexts(string raw, bool json)
    {
        var list = new List<string>();
        JObject obj = json ? TryParse(raw) : null;
        if (obj == null) { list.Add(raw); return list; }
        foreach (var prop in obj.Properties())
            if (TextKeys.Contains(prop.Name) && prop.Value.Type == JTokenType.String)
                list.Add((string)prop.Value);
        return list;
    }

    // 截取第一个 { 到最后一个 }，尝试解析成 JSON 对象（失败返回 null）。
    static JObject TryParse(string raw)
    {
        try
        {
            int s = raw.IndexOf('{'), e = raw.LastIndexOf('}');
            if (s < 0 || e <= s) return null;
            return JObject.Parse(raw.Substring(s, e - s + 1));
        }
        catch { return null; }
    }

    // ================= 提示词增强（由 AIClientHub 自动加，不用每处自己写）=================

    // 加在 system 提示词【最前面】的语言锚点（小模型对开头和结尾最敏感）。
    public static string SystemHeader(Language lang) => lang == Language.Japanese
        ? "【出力言語：日本語】この会話での出力は、すべて自然な日本語で書くこと。中国語は一切使わない。\n\n"
        : "【输出语言：中文】本次对话的所有输出，都必须用自然的简体中文书写，不得使用日语。\n\n";

    // 加在 user 提示词【最后面】的提醒（离生成位置最近，效果最强）。
    public static string UserReminder(Language lang) => lang == Language.Japanese
        ? "\n\n（※返答は必ず日本語のみで書くこと）"
        : "\n\n（※回答必须只用中文）";

    // 重试时追加的纠正提示。
    public static string RetryNote(Language lang) => lang == Language.Japanese
        ? "\n\n【注意】直前の返答には中国語が混ざっていたため却下された。今回は必ず自然な日本語だけで書き直すこと。"
        : "\n\n【注意】上一次的回答混入了日语，已被驳回。这次必须只用自然的中文重新写。";

    // ================= 翻译修复 =================

    // 重试都失败后：把错误语言的字段逐个交给模型"翻译"成目标语言，再拼回原来的 JSON 结构。
    public static void Repair(string raw, bool json, Language lang, IAIClient client, Action<string> callback)
    {
        JObject obj = json ? TryParse(raw) : null;

        // 非 JSON（或解析失败）：整段翻译。
        if (obj == null)
        {
            Translate(raw, lang, client, translated =>
                callback?.Invoke(TextMatches(translated, lang, false) ? translated : SafeLine()));
            return;
        }

        // JSON：找出语言不对的字段，排队逐个翻译。
        var badKeys = new List<string>();
        foreach (var prop in obj.Properties())
            if (TextKeys.Contains(prop.Name) && prop.Value.Type == JTokenType.String
                && !TextMatches((string)prop.Value, lang, true))
                badKeys.Add(prop.Name);

        TranslateNext(obj, badKeys, 0, lang, client, callback);
    }

    // 依次翻译 badKeys 里的字段（一个翻完再翻下一个），全部完成后回调。
    static void TranslateNext(JObject obj, List<string> keys, int i, Language lang, IAIClient client, Action<string> callback)
    {
        if (i >= keys.Count) { callback?.Invoke(obj.ToString(Formatting.None)); return; }

        string key = keys[i];
        Translate((string)obj[key], lang, client, translated =>
        {
            obj[key] = TextMatches(translated, lang, false) ? translated : SafeLine();
            TranslateNext(obj, keys, i + 1, lang, client, callback);
        });
    }

    // 让模型把一段文字翻译成目标语言（纯文本输出）。
    static void Translate(string text, Language lang, IAIClient client, Action<string> done)
    {
        string sys = lang == Language.Japanese
            ? "あなたはプロの翻訳者です。入力された文章を、自然な日本語に翻訳してください。" +
              "意味・口調・感情はそのまま保つこと。説明や注釈は付けず、翻訳した文章だけを出力すること。" +
              "出力に中国語を一切含めないこと。"
            : "你是专业翻译。把输入的文字翻译成自然的简体中文。" +
              "保持原意、语气和情绪。不要加任何解释或注释，只输出翻译后的文字。" +
              "输出中不得包含任何日语假名。";

        client.SendChatRequest(sys, text, result => done(CleanTranslation(result)), false);
    }

    // 去掉翻译结果外面多余的引号、空白。
    static string CleanTranslation(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '「' && s[s.Length - 1] == '」')))
            s = s.Substring(1, s.Length - 2).Trim();
        return s;
    }

    // 实在修不好时的安全台词（两种语言通用）。
    public static string SafeLine() => "……";
}
