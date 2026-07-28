using System;

// LLM 客户端的统一接口。
// 本地（OllamaAIClient）和云端（ClaudeAIClient）都实现这个接口，
// 上层（NpcAgent / AIDirector）只依赖接口，不关心背后是本地还是云端。
// 切换本地/云端时，上层代码完全不用改。
public interface IAIClient
{
    // 发一次对话请求。
    //   systemPrompt = 角色/世界观设定
    //   userPrompt   = 本轮输入
    //   callback     = 拿到回复后调用（传入回复字符串）
    //   jsonFormat   = true 时要求模型输出 JSON（更稳，便于解析）
    void SendChatRequest(string systemPrompt, string userPrompt, Action<string> callback, bool jsonFormat = false);
}
