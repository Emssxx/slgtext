using UnityEngine;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;

// 云端 OpenAI（ChatGPT）客户端。实现 IAIClient，与其他客户端可互换。
//
// 【API key】
//   key はコードに書かず、利用者PC内のファイルから読む：
//     Application.persistentDataPath/openai_key.txt
//   各利用者が、自分のOpenAI APIキーを、そのファイルに入れる。
public class OpenAIClient : MonoBehaviour, IAIClient
{
    public static OpenAIClient Instance;

    [Header("OpenAI 设置")]
    public string apiUrl = "https://api.openai.com/v1/chat/completions";
    public string modelName = "gpt-4o";       // 可按需改
    public int maxTokens = 1024;
    public string apiKeyFileName = "openai_key.txt";

    private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };
    private string _apiKey = null;

    public bool HasApiKey => !string.IsNullOrEmpty(_apiKey);
    public string KeyFilePath => Path.Combine(Application.persistentDataPath, apiKeyFileName);

    private const string FallbackJson = "{\"dialogue\":\"……（云端没有回应）\"}";

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
        LoadApiKey();
    }

    void LoadApiKey()
    {
        string path = KeyFilePath;
        if (File.Exists(path))
        {
            _apiKey = File.ReadAllText(path).Trim();
            if (!string.IsNullOrEmpty(_apiKey)) Debug.Log("[OpenAIClient] APIキー読込OK。");
        }
        else Debug.LogWarning($"[OpenAIClient] keyファイル未検出：{path}");
    }

    public async void SendChatRequest(string systemPrompt, string userPrompt, Action<string> callback, bool jsonFormat = false)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            callback?.Invoke(jsonFormat ? FallbackJson : "……（OpenAI未配置）");
            return;
        }
        try
        {
            // OpenAI Chat Completions：messages に system と user を入れる。
            var requestData = new
            {
                model = modelName,
                max_tokens = maxTokens,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };
            string json = JsonConvert.SerializeObject(requestData);

            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Headers.Add("Authorization", "Bearer " + _apiKey); // OpenAI は Bearer 認証
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string body = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<OpenAIResponse>(body);

            // 回复在 choices[0].message.content。
            if (result?.choices != null && result.choices.Count > 0 && result.choices[0].message != null
                && !string.IsNullOrEmpty(result.choices[0].message.content))
            {
                callback?.Invoke(result.choices[0].message.content);
            }
            else
            {
                Debug.LogError("[OpenAIClient] 返回异常：" + body);
                callback?.Invoke(jsonFormat ? FallbackJson : "……（回应异常）");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[OpenAIClient] 请求失败：" + e.Message);
            callback?.Invoke(jsonFormat ? FallbackJson : "……（连接失败）");
        }
    }
}

[Serializable] public class OpenAIResponse { public List<OpenAIChoice> choices; }
[Serializable] public class OpenAIChoice { public OpenAIMessage message; }
[Serializable] public class OpenAIMessage { public string role; public string content; }
