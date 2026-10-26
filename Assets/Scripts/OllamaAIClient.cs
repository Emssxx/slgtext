using UnityEngine;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

// 与本地 Ollama 通信的客户端（单例）。是 LLM 调用的统一出口。
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

    // 全局共用一个 HttpClient，60 秒超时（本地模型首次加载可能慢）。
    private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };

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
            object requestData;
            if (jsonFormat)
            {
                requestData = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user",   content = userPrompt }
                    },
                    stream = false,
                    format = "json", // 强制 JSON 输出
                    options = BuildOptions()
                };
            }
            else
            {
                requestData = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user",   content = userPrompt }
                    },
                    stream = false,
                    options = BuildOptions()
                };
            }

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

            Debug.Log("AI 响应成功：" + result.message.content);
            callback?.Invoke(result.message.content);
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
            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt }
            };
            object requestData = jsonFormat
                ? (object)new { model = modelName, messages = messages, stream = false, format = "json", options = options }
                : new { model = modelName, messages = messages, stream = false, options = options };

            var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _client.PostAsync(ollamaApiUrl, content, token); // 取消时连接断开，Ollama 会停止生成
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            if (token.IsCancellationRequested) { callback?.Invoke(null); return; }

            var result = JsonConvert.DeserializeObject<OllamaResponse>(responseBody);
            string text = (result != null && result.message != null) ? result.message.content : null;
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
