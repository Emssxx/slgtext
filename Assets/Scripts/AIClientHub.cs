using UnityEngine;
using System;
using System.Threading;

// LLM 调用的统一出口 + プロバイダー選択。
//
// 上层（NpcAgent / AIDirector）は AIClientHub.Instance.Send(...) だけを呼ぶ。
// どのAI（本地Ollama / Claude / OpenAI / Gemini）を使うかは、ここで一元管理する。
// プロバイダーを増やしても、上层のコードは一切変えなくてよい。
//
// 【前台 / 后台】
//   Send           = 前台请求（玩家对话、指令解析、导演事件）：重要，带语言守卫的重试与翻译修复。
//   SendBackground = 后台请求（NPC 之间闲聊的配音）：不重要，只在模型空闲时发，
//                    前台请求一来就立刻取消，失败/被取消时回调 null，由调用方用本地台词兜底。
public enum AIProvider { LocalOllama, Claude, OpenAI, Gemini }

public class AIClientHub : MonoBehaviour
{
    public static AIClientHub Instance;

    [Header("どのAIを使うか")]
    public AIProvider provider = AIProvider.LocalOllama;

    [Header("语言守卫（防止 AI 说错语言，详见 LanguageGuard）")]
    public bool languageGuard = true;   // 总开关
    public int maxRetries = 2;          // 语言不对时最多重试几次，之后转"翻译修复"

    [Header("后台请求（NPC 闲聊配音等：玩家/导演的请求一来就让路）")]
    public bool backgroundEnabled = true;       // 总开关
    public bool backgroundOnCloud = false;      // 云端按量计费：默认只在本地模型上跑后台请求
    public float backgroundMinInterval = 3f;    // 两次后台请求之间至少隔几秒（给显卡喘口气）
    public int backgroundMaxTokens = 256;       // 后台请求最多生成多少 token（越少越快）

    int _foregroundCount;                     // 正在进行中的前台请求数
    float _lastForegroundTime;                // 最近一次前台请求开始的时间（防止计数卡死）
    CancellationTokenSource _backgroundCts;   // 正在进行中的后台请求（本地，可取消）
    bool _backgroundCloudBusy;                // 正在进行中的后台请求（云端，不可取消）
    float _nextBackgroundTime;

    // 前台是否有请求在跑（超过 120 秒没结束的视为卡死，不再算忙）。
    public bool ForegroundBusy => _foregroundCount > 0 && Time.realtimeSinceStartup - _lastForegroundTime < 120f;
    public bool BackgroundBusy => _backgroundCts != null || _backgroundCloudBusy;

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    // 統一送信入口（前台请求）。
    public void Send(string systemPrompt, string userPrompt, Action<string> callback, bool jsonFormat = false)
    {
        IAIClient client = GetActiveClient();
        if (client == null)
        {
            Debug.LogError($"[AIClientHub] プロバイダー {provider} のクライアントが見つかりません。");
            callback?.Invoke(jsonFormat ? "{\"dialogue\":\"……（AI未就绪）\"}" : "……（AI未就绪）");
            return;
        }

        // 前台优先：正在跑的后台请求（NPC 闲聊配音）立刻取消，把本地模型让给玩家/导演。
        CancelBackground();
        _foregroundCount++;
        _lastForegroundTime = Time.realtimeSinceStartup;
        Action<string> done = raw =>
        {
            _foregroundCount = Mathf.Max(0, _foregroundCount - 1);
            callback?.Invoke(raw);
        };

        // 语言守卫关闭（或本地化系统没就绪）时，按原样直接发送。
        if (!languageGuard || LocalizationManager.Instance == null)
        {
            client.SendChatRequest(systemPrompt, userPrompt, done, jsonFormat);
            return;
        }

        // 语言守卫开启：
        //   1. 在 system 开头加语言锚点、在 user 结尾加语言提醒（所有调用自动生效）。
        //   2. 回复回来后由代码检查语言，不对就重试 / 翻译修复。
        Language lang = LocalizationManager.Instance.current;
        string sys = LanguageGuard.SystemHeader(lang) + systemPrompt;
        string usr = userPrompt + LanguageGuard.UserReminder(lang);
        Attempt(client, sys, usr, done, jsonFormat, lang, 0);
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

    // 后台请求：只在"没有前台请求、也没有别的后台请求"时才发。发出去返回 true；
    // 不发（忙 / 关闭 / 云端）返回 false，调用方应立刻用本地兜底。
    // 结果回调：成功 = 模型原文（语言由调用方自己检查）；失败/超时/被取消 = null。
    // 不做重试和翻译修复（省显卡，反正有本地兜底）。
    // maxTokens：这次最多生成多少 token（0 = 用 backgroundMaxTokens）。写演员提示词这类长输出要给大一点。
    public bool SendBackground(string systemPrompt, string userPrompt, Action<string> callback, bool jsonFormat = true, int maxTokens = 0)
    {
        if (!backgroundEnabled || ForegroundBusy || BackgroundBusy) return false;
        if (Time.realtimeSinceStartup < _nextBackgroundTime) return false;

        IAIClient client = GetActiveClient();
        if (client == null) return false;
        IAIBackgroundClient bg = client as IAIBackgroundClient;
        if (bg == null && !backgroundOnCloud) return false; // 云端按量计费：默认不跑后台请求

        string sys = systemPrompt, usr = userPrompt;
        if (languageGuard && LocalizationManager.Instance != null)
        {
            Language lang = LocalizationManager.Instance.current;
            sys = LanguageGuard.SystemHeader(lang) + systemPrompt;
            usr = userPrompt + LanguageGuard.UserReminder(lang);
        }
        _nextBackgroundTime = Time.realtimeSinceStartup + backgroundMinInterval;

        if (bg != null)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            _backgroundCts = cts;
            bg.SendBackgroundRequest(sys, usr, raw =>
            {
                if (_backgroundCts == cts) _backgroundCts = null;
                cts.Dispose();
                callback?.Invoke(raw);
            }, jsonFormat, maxTokens > 0 ? maxTokens : backgroundMaxTokens, cts.Token);
        }
        else
        {
            _backgroundCloudBusy = true;
            client.SendChatRequest(sys, usr, raw =>
            {
                _backgroundCloudBusy = false;
                callback?.Invoke(raw);
            }, jsonFormat);
        }
        return true;
    }

    // 取消正在进行的后台请求（前台请求来了、或调用方不需要结果了）。云端请求无法取消，只能等它结束。
    public void CancelBackground()
    {
        if (_backgroundCts == null) return;
        CancellationTokenSource cts = _backgroundCts;
        _backgroundCts = null;
        try { cts.Cancel(); }
        catch (Exception e) { Debug.LogWarning("[AIClientHub] 取消后台请求时出错：" + e.Message); }
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
