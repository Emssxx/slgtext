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
        // 1. 推进世界 + 规则结算（封装在 RunOneTurnResolve，顺序不变）。
        WorldManager.Instance.RunOneTurnResolve();
        WorldState world = WorldManager.Instance.world;

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
        // 世界概况：当前语言的简短文字（代替原来整份世界 JSON，防止中文混入和上下文溢出）。
        string context = PromptContext.WorldSummary(WorldManager.Instance.world);
        string langRule = LocalizationManager.Instance != null ? LocalizationManager.Instance.GetAILanguageInstruction() : "";

        // 整段提示词按当前界面语言写（事件类型代码是英文，两种语言通用）。
        string systemPrompt = LanguageGuard.IsJp
        ?
            WorldLore.Background + "\n\n" +
            WorldLore.WorldLaws + "\n\n" +
            WorldLore.DirectorGuidance + "\n\n" +
            WorldLore.EventThemes + "\n\n" +
            "あなたは亜空間サバイバルの「ディレクター」であり、状況をより緊迫させる、あるいはより不気味にする突発的な出来事を作る役目を持つ。\n" +
            context + "\n" +
            "現在の状況をもとに、次のイベントの種類から1つを選ぶこと（自分で新しく作ってはならない）：\n" +
            "  storm（亜空間の嵐）\n" +
            "  space_warp（空間の歪み：誰かが突然別の場所に現れる）\n" +
            "  time_disorder（時間の乱れ）\n" +
            "  causal_reversal（因果の逆転）\n" +
            "  whisper_vision（囁きと幻影）\n" +
            EnemyDirector.DirectorTypeLines(true) +   // 安全期が過ぎたら：造物の襲来 / 略奪者の襲撃
            "異常度が高いほど、危険で非現実的な種類を選ぶこと。\n\n" +
            "【出力形式】JSONオブジェクトを1つだけ出力し、それ以外は何も書かないこと：\n" +
            "{\n" +
            "  \"type\": \"上に挙げた種類のいずれか（英字のまま）\",\n" +
            "  \"event\": \"その種類に合った不気味な描写（1〜2文）\"\n" +
            "}" +
            "\n\n" + langRule
        :
            WorldLore.Background + "\n\n" +
            WorldLore.WorldLaws + "\n\n" +
            WorldLore.DirectorGuidance + "\n\n" +
            WorldLore.EventThemes + "\n\n" +
            "你是一名末世亚空间生存的导演，负责制造让局势更紧张或更诡异的突发事件。\n" +
            context + "\n" +
            "请基于当前状态，从以下事件类型里选一个（禁止自创）：\n" +
            "  storm（亚空间风暴）\n" +
            "  space_warp（空间扭曲：有人莫名出现在别的地方）\n" +
            "  time_disorder（时间失序）\n" +
            "  causal_reversal（因果倒置）\n" +
            "  whisper_vision（低语与幻象）\n" +
            EnemyDirector.DirectorTypeLines(false) +  // 过了安全期才有：造物来袭 / 掠夺者来袭
            "异常度越高越倾向危险/超现实的类型。\n\n" +
            "【输出要求】只输出一个 JSON 对象，不要多余文字：\n" +
            "{\n" +
            "  \"type\": \"上面列出的类型之一\",\n" +
            "  \"event\": \"符合该类型的诡异描述，1~2句\"\n" +
            "}" +
            "\n\n" + langRule;

        // 走 AI 调度器排队（导演优先级低，但排久了会自动升高，不会一直轮不到）。
        string userPrompt = LanguageGuard.Pick("生成本回合的事件。", "今回のイベントを生成してください。");
        bool queued = AiScheduler.Submit(new AiRequest
        {
            kind = AiKind.Director, key = "director", json = true,
            system = () => systemPrompt, user = () => userPrompt,
            onResult = raw => OnEventGenerated(raw ?? ""),
        });
        if (!queued) AIClientHub.Instance.Send(systemPrompt, userPrompt, OnEventGenerated, true); // 调度器拒绝（不该发生）：照旧直接发
    }

    void OnEventGenerated(string rawJson)
    {
        _busy = false;
        EventData data = ParseEvent(rawJson);
        // 只用解析出来的事件描述；拿不到（比如AI失败返回了兜底的 {"dialogue":...}）就不显示，
        // 避免把一串原始 JSON 显示在事件栏里。
        string eventText = data.eventDesc;
        string eventType = data.type; // 事件类型，第二步传给 EventExecutor

        WorldState world = WorldManager.Instance.world;

        if (!string.IsNullOrEmpty(eventText))
        {
            EventLogManager.Instance.AddEvent(world.day, eventText);
            NpcSecurity.NotifyDanger(6f); // 诡异事件让人心里发毛
            world.eventLog.Add(new MemoryEntry { day = world.day, content = eventText });
            // （旧版在这里会随机点名一个 NPC 用 LLM 复述/评论事件——信息重复又容易说错，已删除。
            //   NPC 仍然"知道"发生了什么：事件写进了 eventLog，和他聊天时会出现在提示词的"最近发生的事"里。）
        }
        else
        {
            Debug.LogWarning("[AIDirector] 没解析出事件描述，本回合不显示事件。原文：" + rawJson);
        }

        // 异常度越高，越可能升级为"危险生物冲击"。
        // 敌人系统开着、过了安全期：变成真的有造物冲营地来（看见它才掉理智，EnemyDirector）；否则照旧是一次全员精神冲击。
        float anomaly = world.anomalyLevel;
        if (UnityEngine.Random.value < anomaly / 150f
            && !(EnemyDirector.Instance != null && EnemyDirector.Instance.TriggerIncursion(anomaly / 100f, null, true)))
        {
            float severity = anomaly / 100f;
            WorldRules.ApplyCreatureShock(world, severity);
            NpcSecurity.NotifyDanger(15f + 15f * severity); // 所有人的安全感往下掉，之后慢慢恢复
            EventLogManager.Instance.AddEvent(world.day,
                LocalizationManager.Instance != null ? LocalizationManager.Instance.Get("danger_approach") : "【危险】有什么东西靠近了！");
            EventChronicle.Record("creature", null, null); // 记进事件编年史（每个 NPC 按自己面对亚空间生物的态度）
        }

        // 把事件类型交给执行器，触发对应的客观后果（执行器会把事件连同导演的描述记进事件编年史）。
        if (EventExecutor.Instance != null && !string.IsNullOrEmpty(eventType))
            EventExecutor.Instance.ExecuteEvent(eventType, eventText);
        else if (!string.IsNullOrEmpty(eventText))
            EventChronicle.Record("other", eventText, null); // 没给出类型也记下来，这样 NPC 也会记得
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
