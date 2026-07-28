using UnityEngine;
using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

// 与本地 Ollama 通信的客户端（单例）。是 LLM 调用的统一出口。
// 以后要切换"本地/云端"，只需在这里换实现，上层（NpcAgent/AIDirector）不用动。
public class OllamaAIClient : MonoBehaviour, IAIClient
{
    public static OllamaAIClient Instance;

    [Header("Ollama 设置")]
    public string ollamaApiUrl = "http://localhost:11434/api/chat";
    public string modelName = "qwen2.5:7b"; // 必须和 `ollama list` 显示的名字一致

    // 全局共用一个 HttpClient，60 秒超时（本地模型首次加载可能慢）。
    private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };

    // 请求失败时的安全默认值（只含 dialogue，匹配新架构）。
    private const string FallbackJson = "{\"dialogue\":\"……（没有回应）\"}";

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
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
                    format = "json" // 强制 JSON 输出
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
                    stream = false
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
