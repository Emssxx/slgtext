using System;
using System.Threading;

// 支持"后台请求"的 LLM 客户端（目前只有本地 Ollama 实现）。
// 后台请求 = 不重要、可以随时放弃的请求（例如 NPC 之间闲聊的配音）：
//   · 可以随时取消（玩家一说话就立刻让出本地模型）
//   · 限制生成长度（更快）
//   · 失败、超时、被取消时回调 null（调用方改用本地兜底），不弹错误
// 由 AIClientHub.SendBackground 统一调度，上层不要直接调用。
public interface IAIBackgroundClient
{
    void SendBackgroundRequest(string systemPrompt, string userPrompt, Action<string> callback,
                               bool jsonFormat, int maxTokens, CancellationToken token);
}
