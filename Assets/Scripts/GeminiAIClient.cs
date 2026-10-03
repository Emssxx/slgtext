using UnityEngine;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;

// 云端 Google Gemini 客户端。实现 IAIClient，与其他客户端可互换。
//
// 【API key】
//   key はコードに書かず、利用者PC内のファイルから読む：
//     Application.persistentDataPath/gemini_key.txt
//   各利用者が、自分のGemini APIキーを、そのファイルに入れる。
public class GeminiAIClient : MonoBehaviour, IAIClient
{
    public static GeminiAIClient Instance;

    [Header("Gemini 设置")]
    // モデル名は URL に含む。キーは送信時にクエリで付ける。
    public string modelName = "gemini-2.0-flash";
    public int maxTokens = 1024;
    public string apiKeyFileName = "gemini_key.txt";

    private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };
    private string _apiKey = null;

    public bool HasApiKey => !string.IsNullOrEmpty(_apiKey);
    public string KeyFilePath => Path.Combine(Application.persistentDataPath, apiKeyFileName);

    // 从常量改成属性：每次用到时按当前界面语言取文字（常量没法跟着语言变）
    private static string FallbackJson => "{\"dialogue\":\"" +
        (LocalizationManager.Instance != null ? LocalizationManager.Instance.Get("ai_cloud_no_response") : "……（云端没有回应）") + "\"}";

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
            if (!string.IsNullOrEmpty(_apiKey)) Debug.Log("[GeminiAIClient] APIキー読込OK。");
        }
        else Debug.LogWarning($"[GeminiAIClient] keyファイル未検出：{path}");
    }

    public async void SendChatRequest(string systemPrompt, string userPrompt, Action<string> callback, bool jsonFormat = false)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            callback?.Invoke(jsonFormat ? FallbackJson : "……（Gemini未配置）");
            return;
        }
        try
        {
            // Gemini：system は systemInstruction、本文は contents に入れる。
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={_apiKey}";

            var requestData = new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = userPrompt } } }
                },
                generationConfig = new { maxOutputTokens = maxTokens }
            };
            string json = JsonConvert.SerializeObject(requestData);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string body = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<GeminiResponse>(body);

            // 回复在 candidates[0].content.parts[0].text。
            if (result?.candidates != null && result.candidates.Count > 0
                && result.candidates[0].content?.parts != null && result.candidates[0].content.parts.Count > 0
                && !string.IsNullOrEmpty(result.candidates[0].content.parts[0].text))
            {
                callback?.Invoke(result.candidates[0].content.parts[0].text);
            }
            else
            {
                Debug.LogError("[GeminiAIClient] 返回异常：" + body);
                callback?.Invoke(jsonFormat ? FallbackJson : "……（回应异常）");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[GeminiAIClient] 请求失败：" + e.Message);
            callback?.Invoke(jsonFormat ? FallbackJson : "……（连接失败）");
        }
    }
}

[Serializable] public class GeminiResponse { public List<GeminiCandidate> candidates; }
[Serializable] public class GeminiCandidate { public GeminiContent content; }
[Serializable] public class GeminiContent { public List<GeminiPart> parts; }
[Serializable] public class GeminiPart { public string text; }
