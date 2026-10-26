using UnityEngine;
using System;

// LLM 调用的统一出口 + プロバイダー選択。
//
// 上层（NpcAgent / AIDirector）は AIClientHub.Instance.Send(...) だけを呼ぶ。
// どのAI（本地Ollama / Claude / OpenAI / Gemini）を使うかは、ここで一元管理する。
// プロバイダーを増やしても、上层のコードは一切変えなくてよい。
public enum AIProvider { LocalOllama, Claude, OpenAI, Gemini }

public class AIClientHub : MonoBehaviour
{
    public static AIClientHub Instance;

    [Header("どのAIを使うか")]
    public AIProvider provider = AIProvider.LocalOllama;

    [Header("语言守卫（防止 AI 说错语言，详见 LanguageGuard）")]
    public bool languageGuard = true;   // 总开关
    public int maxRetries = 2;          // 语言不对时最多重试几次，之后转"翻译修复"

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    // 統一送信入口。
    public void Send(string systemPrompt, string userPrompt, Action<string> callback, bool jsonFormat = false)
    {
        IAIClient client = GetActiveClient();
        if (client == null)
        {
            Debug.LogError($"[AIClientHub] プロバイダー {provider} のクライアントが見つかりません。");
            callback?.Invoke(jsonFormat ? "{\"dialogue\":\"……（AI未就绪）\"}" : "……（AI未就绪）");
            return;
        }
        // 语言守卫关闭（或本地化系统没就绪）时，按原样直接发送。
        if (!languageGuard || LocalizationManager.Instance == null)
        {
            client.SendChatRequest(systemPrompt, userPrompt, callback, jsonFormat);
            return;
        }

        // 语言守卫开启：
        //   1. 在 system 开头加语言锚点、在 user 结尾加语言提醒（所有调用自动生效）。
        //   2. 回复回来后由代码检查语言，不对就重试 / 翻译修复。
        Language lang = LocalizationManager.Instance.current;
        string sys = LanguageGuard.SystemHeader(lang) + systemPrompt;
        string usr = userPrompt + LanguageGuard.UserReminder(lang);
        Attempt(client, sys, usr, callback, jsonFormat, lang, 0);
    }

    // 发送一次并检查语言。attempt = 第几次重试（0 = 第一次）。
    void Attempt(IAIClient client, string sys, string usr, Action<string> callback, bool jsonFormat, Language lang, int attempt)
    {
        // 重试时在 user 末尾追加"上次说错了语言"的纠正提示。
        string usrThisTime = attempt > 0 ? usr + LanguageGuard.RetryNote(lang) : usr;

        client.SendChatRequest(sys, usrThisTime, raw =>
        {
            // 语言正确 → 原样交给调用方。
            if (LanguageGuard.IsCorrect(raw, jsonFormat, lang, true))
            {
                callback?.Invoke(raw);
                return;
            }

            if (attempt < maxRetries)
            {
                Debug.LogWarning($"[语言守卫] 第{attempt + 1}次回复语言不对，重试：{raw}");
                Attempt(client, sys, usr, callback, jsonFormat, lang, attempt + 1);
            }
            else
            {
                Debug.LogWarning($"[语言守卫] 重试{maxRetries}次仍不对，转翻译修复：{raw}");
                LanguageGuard.Repair(raw, jsonFormat, lang, client, callback);
            }
        }, jsonFormat);
    }

    // 現在のプロバイダーに対応するクライアントを返す。
    IAIClient GetActiveClient()
    {
        switch (provider)
        {
            case AIProvider.Claude:  return ClaudeAIClient.Instance;
            case AIProvider.OpenAI:  return OpenAIClient.Instance;
            case AIProvider.Gemini:  return GeminiAIClient.Instance;
            case AIProvider.LocalOllama:
            default:
                return OllamaAIClient.Instance as IAIClient;
        }
    }

    // 実行時にプロバイダーを切り替える（ボタン等に割り当て可能）。
    public void SetProvider(AIProvider p)
    {
        provider = p;
        Debug.Log($"[AIClientHub] AIプロバイダーを切り替え：{p}");
    }

    // 現在のプロバイダー名（状態表示用）。
    public string CurrentProviderName()
    {
        switch (provider)
        {
            case AIProvider.Claude: return "クラウド (Claude)";
            case AIProvider.OpenAI: return "クラウド (ChatGPT)";
            case AIProvider.Gemini: return "クラウド (Gemini)";
            default:                return "ローカル (Ollama)";
        }
    }
}
