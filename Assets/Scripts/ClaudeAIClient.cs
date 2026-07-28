using UnityEngine;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;

// 云端 Claude（Anthropic API）客户端。实现 IAIClient，与 OllamaAIClient 可互换。
// 用途：发表/演示时的后备——本地 LLM 跑不动时切到云端，稳定性更好。
//
// 【API key 的安全处理】
//   key 不写在代码里、不进 git、不打包进游戏。
//   而是从「用户电脑本地目录」的一个文件读取：
//     Application.persistentDataPath/apikey.txt
//   你只需在自己演示的电脑上，把 key 写进那个文件即可。
//   别人拿到游戏也没有 key，因此不会泄露你的额度。
public class ClaudeAIClient : MonoBehaviour, IAIClient
{
    public static ClaudeAIClient Instance;

    [Header("Claude 设置")]
    public string apiUrl = "https://api.anthropic.com/v1/messages";
    public string modelName = "claude-3-5-sonnet-20241022"; // 可按需改成其他 Claude 模型
    public int maxTokens = 1024;

    // key 文件名（放在 persistentDataPath 下）。
    public string apiKeyFileName = "apikey.txt";

    private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };
    private string _apiKey = null;

    private const string FallbackJson = "{\"dialogue\":\"……（云端没有回应）\"}";

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }

        LoadApiKey();
    }

    // 从本地文件读取 API key（不硬编码、不打包）。
    void LoadApiKey()
    {
        string path = Path.Combine(Application.persistentDataPath, apiKeyFileName);
        if (File.Exists(path))
        {
            _apiKey = File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(_apiKey))
                Debug.LogWarning($"[ClaudeAIClient] key 文件为空：{path}");
            else
                Debug.Log("[ClaudeAIClient] API key 已从本地文件加载。");
        }
        else
        {
            Debug.LogWarning($"[ClaudeAIClient] 未找到 key 文件：{path}\n" +
                "请在该路径创建 apikey.txt 并写入你的 Anthropic API key（云端功能才可用）。");
        }
    }

    public async void SendChatRequest(string systemPrompt, string userPrompt, Action<string> callback, bool jsonFormat = false)
    {
        // 没有 key 就直接回退（不崩，方便本地为主时云端未配置）。
        if (string.IsNullOrEmpty(_apiKey))
        {
            Debug.LogError("[ClaudeAIClient] 没有 API key，无法调用云端。返回兜底。");
            callback?.Invoke(jsonFormat ? FallbackJson : "……（云端未配置）");
            return;
        }

        try
        {
            // Anthropic Messages API 请求体。
            // system 作为独立字段；jsonFormat 时在 user 内容里追加"仅输出JSON"的指示。
            string userContent = userPrompt;
            if (jsonFormat)
                userContent += "\n\n（重要：只输出合法的 JSON，不要任何多余文字、不要 Markdown 代码块。）";

            var requestData = new
            {
                model = modelName,
                max_tokens = maxTokens,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userContent }
                }
            };

            string json = JsonConvert.SerializeObject(requestData);

            // 构建请求（Anthropic 需要 x-api-key 和 anthropic-version 头）。
            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<ClaudeResponse>(responseBody);

            // Claude 的回复在 content[0].text。
            if (result?.content != null && result.content.Count > 0 && !string.IsNullOrEmpty(result.content[0].text))
            {
                callback?.Invoke(result.content[0].text);
            }
            else
            {
                Debug.LogError("[ClaudeAIClient] 返回内容为空或格式异常：" + responseBody);
                callback?.Invoke(jsonFormat ? FallbackJson : "……（云端回应异常）");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[ClaudeAIClient] 请求失败：" + e.Message);
            callback?.Invoke(jsonFormat ? FallbackJson : "……（云端连接失败）");
        }
    }
}

// Anthropic Messages API 的响应结构（只取需要的字段）。
[Serializable]
public class ClaudeResponse
{
    public List<ClaudeContent> content;
}
[Serializable]
public class ClaudeContent
{
    public string type;
    public string text;
}
