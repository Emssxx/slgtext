using UnityEngine;
using System;
using Newtonsoft.Json;

// AI 导演：游戏的"讲述者"。主动推进回合、施加生存压力，并在合适时机让 LLM 生成
// 一个"亚空间事件"，让世界感觉活的、不可预测。属 LLM 白名单（低频可等待）。
public class AIDirector : MonoBehaviour
{
    [Header("引用与设置")]
    public ChatManager chatManager;
    public float secondsPerTurn = 20f; // 每隔多少秒推进一个回合
    [Range(0f, 1f)]
    public float eventChance = 0.5f;   // 每回合触发事件的概率

    private float _timer;
    private bool _busy;

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= secondsPerTurn)
        {
            _timer = 0f;
            DoTurn();
        }
    }

    void DoTurn()
    {
        // 1. 推进世界 + 规则结算（顺序：推进→回合影响→崩解→生命）。
        WorldManager.Instance.AdvanceTurn();
        WorldState world = WorldManager.Instance.world;
        WorldRules.ApplyTurnEffects(world);
        WorldRules.ApplyBreakdown(world);
        WorldRules.ApplyHealth(world);

        EventLogManager.Instance.AddEvent(world.day,
            LocalizationManager.Instance != null ? LocalizationManager.Instance.Get("day_start") : "新的一天开始了。");

        // 2. 按概率触发 AI 事件。
        if (!_busy && UnityEngine.Random.value < eventChance)
            RequestEvent();
    }

    // 让 AI 即兴生成一个亚空间事件。
    void RequestEvent()
    {
        _busy = true;
        string worldJson = WorldManager.Instance.ExportWorldAsJson();

        string systemPrompt =
            WorldLore.Background + "\n\n" +
            WorldLore.WorldLaws + "\n\n" +
            WorldLore.DirectorGuidance + "\n\n" +
            WorldLore.EventThemes + "\n\n" +
            "你是一名末世亚空间生存的导演，负责制造让局势更紧张或更诡异的突发事件。\n" +
            "当前世界状态（JSON）：\n" + worldJson + "\n\n" +
            "请基于当前状态，从以下事件类型里选一个（禁止自创）：\n" +
            "  storm（亚空间风暴）\n" +
            "  space_warp（空间扭曲：有人莫名出现在别的地方）\n" +
            "  time_disorder（时间失序）\n" +
            "  causal_reversal（因果倒置）\n" +
            "  whisper_vision（低语与幻象）\n" +
            "异常度越高越倾向危险/超现实的类型。\n\n" +
            "【输出要求】只输出一个 JSON 对象，不要多余文字：\n" +
            "{\n" +
            "  \"type\": \"上面五个之一\",\n" +
            "  \"event\": \"符合该类型的诡异描述，1~2句\"\n" +
            "}" +
            "\n\n" + (LocalizationManager.Instance != null ? LocalizationManager.Instance.GetAILanguageInstruction() : "");

        OllamaAIClient.Instance.SendChatRequest(systemPrompt, "生成本回合的事件。", OnEventGenerated, true);
    }

    void OnEventGenerated(string rawJson)
    {
        _busy = false;
        EventData data = ParseEvent(rawJson);
        // eventDesc 优先，解析失败时用原文兜底
        string eventText = string.IsNullOrEmpty(data.eventDesc) ? rawJson : data.eventDesc;
        string eventType = data.type; // 事件类型，第二步传给 EventExecutor

        EventLogManager.Instance.AddEvent(WorldManager.Instance.world.day, eventText);

        WorldState world = WorldManager.Instance.world;
        world.eventLog.Add(new MemoryEntry { day = world.day, content = eventText });

        // 事件发生后，60% 概率点名一个 NPC 针对事件反应。
        if (UnityEngine.Random.value < 0.6f)
            MakeNpcReactToEvent(eventText);

        // 异常度越高，越可能升级为"危险生物冲击"。
        float anomaly = world.anomalyLevel;
        if (UnityEngine.Random.value < anomaly / 150f)
        {
            float severity = anomaly / 100f;
            WorldRules.ApplyCreatureShock(world, severity);
            EventLogManager.Instance.AddEvent(world.day,
                LocalizationManager.Instance != null ? LocalizationManager.Instance.Get("danger_approach") : "【危险】有什么东西靠近了！");
        }

        // 把事件类型交给执行器，触发对应的客观后果。
        if (EventExecutor.Instance != null && !string.IsNullOrEmpty(eventType))
            EventExecutor.Instance.ExecuteEvent(eventType);
    }

    // 点名一个清醒 NPC 针对事件反应。
    void MakeNpcReactToEvent(string eventDesc)
    {
        WorldState world = WorldManager.Instance.world;
        var candidates = new System.Collections.Generic.List<NpcData>();
        foreach (NpcData npc in world.npcs)
            if (npc.health > 0f && !npc.isBroken) candidates.Add(npc);
        if (candidates.Count == 0) return;

        NpcData chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        NpcAgent agent = FindObjectOfType<NpcAgent>();
        if (agent != null)
        {
            string reason = "你刚刚经历了这件事：" + eventDesc + "。请针对它做出你这个角色会有的即时反应。";
            agent.MakeNpcSpeak(chosen, reason);
        }
    }

    // 解析 {"type":"...","event":"..."}，返回 EventData。
    // 解析失败时把原文塞进 eventDesc 兜底，type 为 null。
    EventData ParseEvent(string raw)
    {
        try
        {
            int start = raw.IndexOf('{');
            int end = raw.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                string jsonPart = raw.Substring(start, end - start + 1);
                EventData d = JsonConvert.DeserializeObject<EventData>(jsonPart);
                if (d != null) return d;
            }
        }
        catch (Exception e) { Debug.LogWarning("解析事件 JSON 失败：" + e.Message); }
        return new EventData { eventDesc = raw };
    }
}

// 事件输出契约：导演输出带 type 和 event 两个字段的 JSON。
[Serializable]
public class EventData
{
    [JsonProperty("event")]
    public string eventDesc; // 事件描述文字，显示在日志里

    [JsonProperty("type")]
    public string type;      // 事件类型ID，传给 EventExecutor 执行后果
}
