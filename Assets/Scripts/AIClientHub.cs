using UnityEngine;
using System;

// LLM 调用的统一出口 + 本地/云端切换开关。
//
// 上层（NpcAgent / AIDirector）改为调用：
//     AIClientHub.Instance.Send(systemPrompt, userPrompt, callback, jsonFormat);
// 而不再直接调 OllamaAIClient。这样切换本地/云端，上层完全不用改。
//
// 【切换方式】
//   - Inspector 里勾选 useCloud：true=云端Claude，false=本地Ollama。
//   - 或运行时调用 SetUseCloud(true/false)（比如发表时本地卡了，一键切云端）。
public class AIClientHub : MonoBehaviour
{
    public static AIClientHub Instance;

    [Header("使用云端？（true=Claude云端，false=本地Ollama）")]
    public bool useCloud = false;

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    // 统一发送入口。根据 useCloud 选择本地或云端客户端。
    public void Send(string systemPrompt, string userPrompt, Action<string> callback, bool jsonFormat = false)
    {
        IAIClient client = GetActiveClient();
        if (client == null)
        {
            Debug.LogError("[AIClientHub] 找不到可用的 AI 客户端（本地/云端都没就绪）。");
            callback?.Invoke(jsonFormat ? "{\"dialogue\":\"……（AI未就绪）\"}" : "……（AI未就绪）");
            return;
        }
        client.SendChatRequest(systemPrompt, userPrompt, callback, jsonFormat);
    }

    // 取当前应使用的客户端。
    IAIClient GetActiveClient()
    {
        if (useCloud)
        {
            if (ClaudeAIClient.Instance != null) return ClaudeAIClient.Instance;
            Debug.LogWarning("[AIClientHub] useCloud=true 但云端客户端不存在，尝试回退本地。");
            return OllamaAIClient.Instance as IAIClient;
        }
        else
        {
            if (OllamaAIClient.Instance != null) return OllamaAIClient.Instance as IAIClient;
            Debug.LogWarning("[AIClientHub] 本地客户端不存在，尝试使用云端。");
            return ClaudeAIClient.Instance;
        }
    }

    // 运行时切换（发表时可绑到一个按钮/快捷键上，本地卡了一键切云端）。
    public void SetUseCloud(bool cloud)
    {
        useCloud = cloud;
        Debug.Log($"[AIClientHub] 已切换到：{(cloud ? "云端 Claude" : "本地 Ollama")}");
    }
}
