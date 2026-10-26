using UnityEngine;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

// 与本地 Ollama 通信的客户端（单例）。是 LLM 调用的统一出口。
// 支持 qwen3 这类思考型模型：自动关闭思考模式，并过滤掉回复里的 <think> 段（见 BuildRequest / StripThinking）。
// 以后要切换"本地/云端"，只需在这里换实现，上层（NpcAgent/AIDirector）不用动。
// 同时支持"后台请求"（IAIBackgroundClient）：NPC 闲聊配音这类可随时取消的请求。
public class OllamaAIClient : MonoBehaviour, IAIClient, IAIBackgroundClient
{
    public static OllamaAIClient Instance;

    [Header("Ollama 设置")]
    public string ollamaApiUrl = "http://localhost:11434/api/chat";
    public string modelName = "qwen2.5:7b"; // 必须和 `ollama list` 显示的名字一致

    [Header("生成参数（降低随机性 + 加大上下文，减少小模型发疯）")]
    [Range(0f, 1.5f)] public float temperature = 0.6f; // 越低越稳定（默认 0.8 偏随机，容易突然换语言）
    [Range(0f, 1f)] public float topP = 0.9f;           // 只从概率最高的 90% 候选里选词
    public float repeatPenalty = 1.1f;                  // 抑制重复
    public int numCtx = 8192;                           // 上下文长度：太短会截掉提示词开头（世界观/语言指令）

    [Header("思考型模型（qwen3 等）")]
    [Tooltip("关闭思考模式：qwen3 这类模型默认会先输出一段 <think> 思考，既慢又会弄乱 JSON。勾选后请求时关闭思考（非思考型模型不受影响）。")]
    public bool disableThinking = true;

    // 全局共用一个 HttpClient，120 秒超时（14B 模型首次加载 + 写演员提示词这类长输出会比较慢）。
    private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(120) };

    // 请求失败时的安全默认值（只含 dialogue，匹配新架构）。
    // 从常量改成属性：每次用到时按当前界面语言取文字（常量没法跟着语言变）
    private static string FallbackJson => "{\"dialogue\":\"" +
        (LocalizationManager.Instance != null ? LocalizationManager.Instance.Get("ai_no_response") : "……（没有回应）") + "\"}";

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
    }

    // Ollama 的生成参数（每次请求都带上）。
    object BuildOptions()
    {
        return new { temperature = temperature, top_p = topP, repeat_penalty = repeatPenalty, num_ctx = numCtx };
    }

    /// <summary>
    /// 向本地 Ollama 发一次对话请求。
    /// systemPrompt=设定；userPrompt=本轮输入；callback=拿到回复后调用；
    /// jsonFormat=true 时要求模型输出 JSON（更稳，解析不易出错）。
    /// </summary>
    public async void SendChatRequest(string systemPrompt, string userPrompt, Action<string> callback, bool jsonFormat = false)
    {
        try
        {
            // 构建请求体。jsonFormat=true 时加 format="json"，让 Ollama 强制返回合法 JSON。
            object requestData = BuildRequest(systemPrompt, userPrompt, jsonFormat, BuildOptions());

            string json = JsonConvert.SerializeObject(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _client.PostAsync(ollamaApiUrl, content);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<OllamaResponse>(responseBody);

            if (result == null || result.message == null || string.IsNullOrEmpty(result.message.content))
            {
                Debug.LogError("AI 返回内容为空或格式异常：" + responseBody);
                callback?.Invoke(FallbackJson);
                return;
            }

            string text = StripThinking(result.message.content);
            Debug.Log("AI 响应成功：" + text);
            callback?.Invoke(string.IsNullOrEmpty(text) ? FallbackJson : text);
        }
        catch (Exception e)
        {
            Debug.LogError($"AI 请求失败：{e.Message}");
            callback?.Invoke(FallbackJson);
        }
    }

    /// <summary>
    /// 后台请求（NPC 闲聊配音等，由 AIClientHub.SendBackground 调度）：
    /// 可以随时取消（玩家说话时立刻让出模型）；生成长度有上限（更快）；
    /// 失败、超时、被取消时回调 null（调用方改用本地台词），不报错误。
    /// </summary>
    public async void SendBackgroundRequest(string systemPrompt, string userPrompt, Action<string> callback,
                                            bool jsonFormat, int maxTokens, CancellationToken token)
    {
        try
        {
            // num_ctx 必须和前台请求一致：Ollama 遇到不同的 num_ctx 会重新加载模型（很慢）。num_predict 不影响。
            object options = new
            {
                temperature = temperature, top_p = topP, repeat_penalty = repeatPenalty,
                num_ctx = numCtx, num_predict = maxTokens
            };
            object requestData = BuildRequest(systemPrompt, userPrompt, jsonFormat, options);

            var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _client.PostAsync(ollamaApiUrl, content, token); // 取消时连接断开，Ollama 会停止生成
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            if (token.IsCancellationRequested) { callback?.Invoke(null); return; }

            var result = JsonConvert.DeserializeObject<OllamaResponse>(responseBody);
            string text = (result != null && result.message != null) ? StripThinking(result.message.content) : null;
            callback?.Invoke(string.IsNullOrEmpty(text) ? null : text);
        }
        catch (OperationCanceledException)
        {
            callback?.Invoke(null); // 被前台请求打断，或超时：安静地放弃
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[后台AI] 请求失败：{e.Message}");
            callback?.Invoke(null);
        }
    }

    // ============ 请求体（前台 / 后台共用）============
    // 思考型模型（qwen3 等）：顶层加 think=false 关闭思考；再在用户消息末尾加 /no_think（qwen3 的软开关，
    // 旧版 Ollama 不认 think 字段时也能生效）。非思考型模型（qwen2.5 等）什么都不加。
    object BuildRequest(string systemPrompt, string userPrompt, bool jsonFormat, object options)
    {
        bool noThink = disableThinking && IsThinkingModel(modelName);
        if (noThink && modelName.ToLowerInvariant().StartsWith("qwen3")) userPrompt += " /no_think";

        Dictionary<string, object> req = new Dictionary<string, object>
        {
            { "model", modelName },
            { "messages", new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                }
            },
            { "stream", false },
            { "options", options },
        };
        if (jsonFormat) req["format"] = "json";
        if (noThink) req["think"] = false;
        return req;
    }

    // 名字看起来是思考型模型吗（qwen3 / deepseek-r1 / qwq）。
    static bool IsThinkingModel(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string n = name.ToLowerInvariant();
        return n.StartsWith("qwen3") || n.Contains("deepseek-r1") || n.Contains("qwq");
    }

    // 去掉回复里的 <think>…</think> 思考段（万一模型没听话还是输出了，或只剩一个没闭合的 <think>）。
    static string StripThinking(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = Regex.Replace(s, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
        int open = s.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (open >= 0) s = s.Substring(0, open); // 没闭合：后面全是思考，丢掉
        return s.Trim();
    }
}

// —— 接住 Ollama 返回的 JSON ——
[Serializable]
public class OllamaResponse
{
    public OllamaMessage message;
}

[Serializable]
public class OllamaMessage
{
    public string role;
    public string content;
}
