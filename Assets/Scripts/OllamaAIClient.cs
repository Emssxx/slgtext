using UnityEngine;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// 与本地 Ollama 通信的客户端（单例）。是 LLM 调用的统一出口。
// 支持 qwen3 这类思考型模型：自动关闭思考模式，并过滤掉回复里的 <think> 段（见 BuildRequest / StripThinking）。
// 以后要切换"本地/云端"，只需在这里换实现，上层（NpcAgent/AIDirector）不用动。
// 同时支持"后台请求"（IAIBackgroundClient）：NPC 闲聊配音这类可随时取消的请求。
// 小模型（助手，默认 qwen3:4b）：和主模型同时加载、同时跑，专做短活（玩家输入分类、敌人台词、记忆摘要等）。
//   启动时检查有没有装、预先加载进显存；由 AIClientHub.SendLite 调度。没装 / 连不上时一律回调 null，调用方用代码兜底。
// 开场的 AI 启动日志（OpeningDirector）用 Probe / WarmMain 查真实状态：Ollama 版本、模型参数量 / 量化、是否已在显存、占多少显存。
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

    [Header("小模型（助手：玩家输入分类、敌人台词、记忆摘要等短活）")]
    [Tooltip("关掉或没装小模型时，用到它的地方会自动改用代码兜底。")]
    public bool liteEnabled = true;
    [Tooltip("必须和 `ollama list` 显示的名字一致。先在命令行执行：ollama pull qwen3:4b。显存够也可以换 qwen3.5:4b。")]
    public string liteModelName = "qwen3:4b";
    [Tooltip("小模型所在的 Ollama 地址。空 = 和主模型同一台；想放到另一台电脑上就填 http://那台电脑的IP:11434/api/chat")]
    public string liteApiUrl = "";
    [Range(0f, 1.5f)] public float liteTemperature = 0.3f; // 分类这类活要稳，低一点
    [Tooltip("小模型的上下文长度。短活用不着长上下文，省显存。运行中别改（会让 Ollama 重新加载模型）。")]
    public int liteNumCtx = 4096;
    [Tooltip("模型用完后在显存里保留多久（主模型、小模型都适用）。Ollama 默认 5 分钟就卸载，下次用要重新加载好几秒。")]
    public string keepAlive = "30m";
    [Tooltip("小模型只用 CPU 跑（不占显卡显存）。显存不够、ollama ps 里看到模型被挤到 CPU 时打开。运行中别改。")]
    public bool liteCpuOnly = false;
    [Tooltip("只用 CPU 时最多用几个线程（留一些核给游戏本身，0 = Ollama 自己决定）。")]
    public int liteCpuThreads = 6;

    // 小模型状态（启动时检查一次；没装的话装好后可以在 Inspector 右键「重新检查小模型」）
    public bool LiteReady { get; private set; }
    public string LiteStatus { get; private set; } = "...";   // 调试显示用：... 检查中 / OK 就绪 / NG 没装或连不上
    string LiteUrl => string.IsNullOrEmpty(liteApiUrl) ? ollamaApiUrl : liteApiUrl;

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

    void Start()
    {
        if (Instance == this && liteEnabled) CheckLite();
    }

    [ContextMenu("重新检查小模型")]
    void RecheckLite()
    {
        LiteReady = false;
        LiteStatus = "...";
        if (liteEnabled) CheckLite();
    }

    // 启动时检查小模型：Ollama 里装了没有 → 预先加载进显存（第一次用的时候就不用等加载）。
    async void CheckLite()
    {
        string chatUrl = LiteUrl;
        string tagsUrl = BaseUrl(chatUrl) + "/api/tags";
        try
        {
            string body = await _client.GetStringAsync(tagsUrl);
            OllamaTags tags = JsonConvert.DeserializeObject<OllamaTags>(body);
            bool found = false;
            if (tags != null && tags.models != null)
                foreach (OllamaTagModel m in tags.models)
                    if (m != null && SameModel(m.name, liteModelName)) { found = true; break; }
            if (!found)
            {
                LiteStatus = "NG";
                Debug.LogWarning($"[小模型] Ollama 里没有 {liteModelName}。请在命令行执行：ollama pull {liteModelName}（没装时自动用代码兜底）");
                return;
            }

            // 预热：messages 为空 = 只把模型加载进显存，不生成。num_ctx 要和正式请求一致，否则之后会重新加载
            float t0 = Time.realtimeSinceStartup;
            Dictionary<string, object> req = new Dictionary<string, object>
            {
                { "model", liteModelName },
                { "messages", new object[0] },
                { "stream", false },
                { "options", LiteOptions(0) },
            };
            if (!string.IsNullOrEmpty(keepAlive)) req["keep_alive"] = keepAlive;
            var content = new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _client.PostAsync(chatUrl, content);
            resp.EnsureSuccessStatusCode();
            LiteReady = true;
            LiteStatus = "OK";
            Debug.Log($"[小模型] {liteModelName} 已就绪（加载 {Time.realtimeSinceStartup - t0:0.0} 秒）");
        }
        catch (Exception e)
        {
            LiteStatus = "NG";
            Debug.LogWarning($"[小模型] 连不上 Ollama（{tagsUrl}）：{e.Message}。用到小模型的地方会改用代码兜底。");
        }
    }

    // 小模型的生成参数。num_ctx / num_gpu 每次都要一样，否则 Ollama 会重新加载模型。
    // num_gpu = 0：一层都不放显卡 = 只用 CPU。
    Dictionary<string, object> LiteOptions(int maxTokens)
    {
        Dictionary<string, object> o = new Dictionary<string, object>
        {
            { "temperature", liteTemperature }, { "top_p", topP }, { "repeat_penalty", repeatPenalty }, { "num_ctx", liteNumCtx },
        };
        if (maxTokens > 0) o["num_predict"] = maxTokens;
        if (liteCpuOnly)
        {
            o["num_gpu"] = 0;
            if (liteCpuThreads > 0) o["num_thread"] = liteCpuThreads;
        }
        return o;
    }

    // "http://localhost:11434/api/chat" → "http://localhost:11434"
    static string BaseUrl(string chatUrl)
    {
        int i = chatUrl.IndexOf("/api/", StringComparison.Ordinal);
        return i > 0 ? chatUrl.Substring(0, i) : chatUrl.TrimEnd('/');
    }

    // 名字一样吗（没写标签时 Ollama 显示成 ":latest"）。
    static bool SameModel(string listed, string wanted)
    {
        if (string.IsNullOrEmpty(listed) || string.IsNullOrEmpty(wanted)) return false;
        if (string.Equals(listed, wanted, StringComparison.OrdinalIgnoreCase)) return true;
        return wanted.IndexOf(':') < 0 && string.Equals(listed, wanted + ":latest", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 小模型请求（由 AIClientHub.SendLite 调度，上层不要直接调用）。和主模型互不排队。
    /// 失败、超时、被取消、没就绪时回调 null（调用方用代码兜底），不弹错误。
    /// </summary>
    public async void SendLiteRequest(string systemPrompt, string userPrompt, Action<string> callback,
                                      bool jsonFormat, int maxTokens, CancellationToken token)
    {
        if (!LiteReady) { callback?.Invoke(null); return; }
        try
        {
            object requestData = BuildRequest(liteModelName, systemPrompt, userPrompt, jsonFormat, LiteOptions(maxTokens));
            var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _client.PostAsync(LiteUrl, content, token);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            if (token.IsCancellationRequested) { callback?.Invoke(null); return; }

            var result = JsonConvert.DeserializeObject<OllamaResponse>(responseBody);
            string text = (result != null && result.message != null) ? StripThinking(result.message.content) : null;
            callback?.Invoke(string.IsNullOrEmpty(text) ? null : text);
        }
        catch (OperationCanceledException)
        {
            callback?.Invoke(null); // 超时：安静地放弃
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[小模型] 请求失败：{e.Message}");
            callback?.Invoke(null);
        }
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
            object requestData = BuildRequest(modelName, systemPrompt, userPrompt, jsonFormat, BuildOptions());

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
            object requestData = BuildRequest(modelName, systemPrompt, userPrompt, jsonFormat, options);

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

    // ============ AI 启动（开场的启动日志用）：查推理服务和模型的真实状态 ============

    public class ModelState
    {
        public string name = "";
        public bool installed, loaded;
        public string parameterSize = "", quantization = "";
        public long sizeVram;   // 已载入时占的显存（字节）
    }

    public class ProbeResult
    {
        public bool ok;               // 连上了
        public string version = "";   // Ollama 版本
        public string error = "";
        public readonly ModelState main = new ModelState(), lite = new ModelState();
    }

    // 查一遍：/api/version（连不连得上）→ /api/tags（装了哪些、参数量、量化）→ /api/ps（哪些已在显存）。
    public async void Probe(Action<ProbeResult> done)
    {
        ProbeResult r = new ProbeResult();
        r.main.name = modelName;
        r.lite.name = liteModelName;
        try
        {
            string mainBase = BaseUrl(ollamaApiUrl);
            JObject v = JObject.Parse(await _client.GetStringAsync(mainBase + "/api/version"));
            r.version = (string)v["version"] ?? "";
            r.ok = true;
            await ReadModels(mainBase, r.main);
            string liteBase = BaseUrl(LiteUrl);
            if (liteEnabled) await ReadModels(liteBase, r.lite);
        }
        catch (Exception e)
        {
            r.error = e.Message;
        }
        done?.Invoke(r);
    }

    static async System.Threading.Tasks.Task ReadModels(string baseUrl, ModelState m)
    {
        JObject tags = JObject.Parse(await _client.GetStringAsync(baseUrl + "/api/tags"));
        foreach (JToken t in tags["models"] as JArray ?? new JArray())
        {
            if (!SameModel((string)t["name"], m.name)) continue;
            m.installed = true;
            m.parameterSize = (string)t["details"]?["parameter_size"] ?? "";
            m.quantization = (string)t["details"]?["quantization_level"] ?? "";
        }
        JObject ps = JObject.Parse(await _client.GetStringAsync(baseUrl + "/api/ps"));
        foreach (JToken t in ps["models"] as JArray ?? new JArray())
        {
            if (!SameModel((string)t["name"], m.name) && !SameModel((string)t["model"], m.name)) continue;
            m.loaded = true;
            m.sizeVram = t["size_vram"] != null ? (long)t["size_vram"] : 0L;
        }
    }

    // 把主模型预先载入显存（messages 为空 = 只载入不生成；num_ctx 要和正式请求一致，否则之后会重新载入）。
    // done(成功, 用了几秒)
    public async void WarmMain(Action<bool, float> done)
    {
        float t0 = Time.realtimeSinceStartup;
        try
        {
            Dictionary<string, object> req = new Dictionary<string, object>
            {
                { "model", modelName },
                { "messages", new object[0] },
                { "stream", false },
                { "options", new Dictionary<string, object> { { "num_ctx", numCtx } } },
            };
            if (!string.IsNullOrEmpty(keepAlive)) req["keep_alive"] = keepAlive;
            var content = new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _client.PostAsync(ollamaApiUrl, content);
            resp.EnsureSuccessStatusCode();
            done?.Invoke(true, Time.realtimeSinceStartup - t0);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[主模型] 预热失败：" + e.Message);
            done?.Invoke(false, Time.realtimeSinceStartup - t0);
        }
    }

    // "http://localhost:11434/api/chat" → "localhost:11434"（启动日志显示用）
    public string ServerHost
    {
        get
        {
            string b = BaseUrl(ollamaApiUrl);
            int i = b.IndexOf("://", StringComparison.Ordinal);
            return i >= 0 ? b.Substring(i + 3) : b;
        }
    }

    // ============ 请求体（前台 / 后台 / 小模型共用）============
    // 思考型模型（qwen3 等）：顶层加 think=false 关闭思考；再在用户消息末尾加 /no_think（qwen3 的软开关，
    // 旧版 Ollama 不认 think 字段时也能生效；qwen3.5 不用这个软开关）。非思考型模型（qwen2.5 等）什么都不加。
    // keep_alive：让模型在显存里多留一会儿（主模型、小模型同时常驻，切换不用重新加载）。
    object BuildRequest(string model, string systemPrompt, string userPrompt, bool jsonFormat, object options)
    {
        bool noThink = disableThinking && IsThinkingModel(model);
        string lower = model.ToLowerInvariant();
        if (noThink && lower.StartsWith("qwen3") && !lower.StartsWith("qwen3.")) userPrompt += " /no_think";

        Dictionary<string, object> req = new Dictionary<string, object>
        {
            { "model", model },
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
        if (!string.IsNullOrEmpty(keepAlive)) req["keep_alive"] = keepAlive;
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

// —— /api/tags（Ollama 里装了哪些模型）——
[Serializable]
public class OllamaTags
{
    public List<OllamaTagModel> models;
}

[Serializable]
public class OllamaTagModel
{
    public string name;
}
