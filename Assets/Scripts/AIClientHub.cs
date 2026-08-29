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
        client.SendChatRequest(systemPrompt, userPrompt, callback, jsonFormat);
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
