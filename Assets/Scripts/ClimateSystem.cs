using System.Collections.Generic;
using UnityEngine;

public enum PrecipLevel { None, Light, Moderate, Heavy }   // 雨 / 雪：小 / 中 / 大
public enum WindLevel { None, Breeze, Moderate, Strong }   // 风：微风 / 中风 / 大风
public enum TempState { Normal, Hot, Cold, ExtremeHot, ExtremeCold }

// ============================================================================
// 天气 + 气温系统（纯规则，不走 LLM）。每回合（AIDirector 推进一天时）由 WorldManager.RunOneTurnResolve 调 OnTurn。
//
// 【天气】随机触发，每种天气持续 1~3 回合后重新掷骰。
//   · 下雨：小雨 / 中雨 / 暴雨——为数不多的水源。要建「雨水收集器」接雨水，再用「水源净化器」（耗电）净化成净化水（见 WaterWorks）。
//   · 风：微风 / 中风 / 大风，和下雨不冲突。暴雨 + 大风 = 「亚空间风暴」事件（走 EventExecutor，会记进事件编年史）。
//   · 下雪：小雪 / 中雪 / 大雪——让气温变低，雨水收集器也能接到水（太冷时下的雨会变成雪）。
//   · 冰雹：不分级；这时在营地外面的人会被砸伤（掉生命）。
// 【气温】环境气温随机漂移，异常度越高，越容易出现极端温度；雪 / 雨 / 冰雹 / 风会拉低气温。
//   营地加温器 / 降温器（PowerGrid）在通电时改变附近的温度——NPC 按自己所在位置的温度结算：
//   · 正常 -10 ~ 30℃：没有任何影响。
//   · 炎热 30℃ 以上：体力消耗加快（每高 1 度，消耗倍率 +hotDrainPerDegree，默认 +5%）。
//     这里只提供倍率 ClimateSystem.EnergyFactor(npc)，不直接扣体力——体力怎么消耗由 NPC 健康系统决定。
//   · 寒冷 -10℃ 以下：走路变慢；没有防寒装备时，每低 5 度生命 -3（每回合）。
//   · 异常 40℃ 以上 / -40℃ 以下：营地基础设施受限（具体负面效果待定，先只有标记 InfrastructureLimited 和提示）。
// 数值都在 Inspector 里，可以随时调。
// ============================================================================
public class ClimateSystem : MonoBehaviour
{
    public static ClimateSystem Instance { get; private set; }

    [Header("降水概率（每次重新掷骰时）")]
    [Range(0f, 1f)] public float precipChance = 0.4f;     // 下雨 / 下雪的概率（异常度高时略增）
    [Range(0f, 1f)] public float hailChance = 0.06f;      // 冰雹的概率
    [Range(0f, 1f)] public float heavyChance = 0.17f;     // 降水里"大"的概率（异常度高时增加）
    [Range(0f, 1f)] public float moderateChance = 0.33f;  // 降水里"中"的概率
    [Tooltip("气温低于多少度时，降水是雪而不是雨")]
    public float snowBelow = 2f;

    [Header("风的概率")]
    [Range(0f, 1f)] public float strongWindChance = 0.12f;
    [Range(0f, 1f)] public float moderateWindChance = 0.22f;
    [Range(0f, 1f)] public float breezeChance = 0.36f;

    [Header("风力发电（每台每回合）")]
    public float breezePower = 0.1f;
    public float moderatePower = 3f;
    public float strongPower = 6f;
    public float stormPower = 10f;

    [Header("气温")]
    public float startTemperature = 15f;
    [Tooltip("气温围绕这个值随机起伏")]
    public float meanTemperature = 15f;
    [Tooltip("气温每回合的起伏幅度（异常度 100 时翻倍：越异常越容易出现极端气温）")]
    public float temperatureSwing = 4f;
    public float hotThreshold = 30f;
    public float coldThreshold = -10f;
    public float extremeHot = 40f;
    public float extremeCold = -40f;

    [Header("炎热：体力消耗倍率（NPC 健康系统用 ClimateSystem.EnergyFactor(npc) 读取）")]
    [Tooltip("超过炎热线后，每高 1 度，体力消耗倍率增加多少（0.05 = +5%）")]
    public float hotDrainPerDegree = 0.05f;

    [Header("寒冷")]
    [Tooltip("低于寒冷线后，每低这么多度……")]
    public float coldStepDegrees = 5f;
    [Tooltip("……生命 -这么多（每回合）")]
    public float coldHealthPerStep = 3f;
    [Range(0.1f, 1f)] public float coldSpeedFactor = 0.7f;
    [Tooltip("防寒装备的物品 id（带在身上就不会冻伤；物品还没做，先留接口）")]
    public string coldGearItemId = "cold_gear";

    [Header("水（每台雨水收集器每回合接到的雨水：小 / 中 / 大）")]
    public int[] rainWater = { 1, 2, 3 };
    public int[] snowWater = { 1, 1, 2 };

    [Header("冰雹")]
    public float hailDamagePerTurn = 6f;

    // —— 当前天气 ——
    public PrecipLevel Rain { get; private set; }
    public PrecipLevel Snow { get; private set; }
    public WindLevel Wind { get; private set; }
    public bool Hail { get; private set; }
    public bool StormActive { get; private set; }
    public float Ambient { get; private set; }
    public TempState CampState { get; private set; }
    public bool InfrastructureLimited => CampState == TempState.ExtremeHot || CampState == TempState.ExtremeCold;

    int _precipTurns, _windTurns, _stormTurns;
    float _baseline;
    int _waterThisSpell;          // 这一场雨雪里收集器接到的雨水
    bool _rainWasted;             // 这一场雨雪下的时候没有收集器
    bool _fullWarned, _noPowerWarned;
    readonly Dictionary<string, float> _healthDelta = new Dictionary<string, float>();
    readonly HashSet<string> _hailHurt = new HashSet<string>();
    float _speedTimer;

    public static ClimateSystem Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("ClimateSystem").AddComponent<ClimateSystem>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Ambient = startTemperature;
        _baseline = startTemperature;
        Wind = WindLevel.Breeze;
        _windTurns = 2;
        _precipTurns = 1;
        PowerGrid.Clear();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ============ 每回合 ============
    public void OnTurn(WorldState w)
    {
        if (w == null) return;
        float a = Mathf.Clamp01(w.anomalyLevel / 100f);
        PrecipLevel oldRain = Rain, oldSnow = Snow;
        WindLevel oldWind = Wind;
        bool oldHail = Hail;

        // 1. 天气
        if (_stormTurns > 0)
        {
            _stormTurns--;
            Rain = PrecipLevel.Heavy; Snow = PrecipLevel.None; Hail = false; Wind = WindLevel.Strong;
        }
        else
        {
            if (--_precipTurns <= 0) RollPrecip(a);
            if (--_windTurns <= 0) RollWind(a);
        }

        // 2. 气温：基准值围绕平均气温随机起伏（异常度越高起伏越大，越容易出现极端气温），
        //    再加上天气的影响（雪/雨/冰雹/风降温，无风无雨的日子升温），环境气温慢慢靠过去
        float swing = temperatureSwing * (1f + a);
        float noise = (Random.Range(-1f, 1f) + Random.Range(-1f, 1f) + Random.Range(-1f, 1f)) * swing; // 近似正态分布
        _baseline += (meanTemperature - _baseline) * 0.1f + noise;
        _baseline = Mathf.Clamp(_baseline, -60f, 60f);
        float target = _baseline + WeatherOffset();
        Ambient += (target - Ambient) * 0.5f + Random.Range(-1f, 1f);
        if (Rain != PrecipLevel.None && Ambient < snowBelow - 2f && _stormTurns <= 0) { Snow = Rain; Rain = PrecipLevel.None; } // 太冷：雨变雪
        else if (Snow != PrecipLevel.None && Ambient > snowBelow + 3f) { Rain = Snow; Snow = PrecipLevel.None; }               // 回暖：雪变雨

        // 3. 暴雨 + 大风 = 亚空间风暴
        bool stormNow = Rain == PrecipLevel.Heavy && Wind == WindLevel.Strong;
        if (stormNow && !StormActive)
        {
            StormActive = true;
            string desc = LanguageGuard.Pick("暴雨和狂风搅在一起，天空裂开一道暗色的口子——亚空间风暴来了。",
                                             "豪雨と強風が絡み合い、空に暗い裂け目が開いた――亜空間の嵐だ。");
            if (EventExecutor.Instance != null) EventExecutor.Instance.ExecuteEvent("storm", desc);
            else EventChronicle.Record("storm", desc, null);
        }
        else if (!stormNow) StormActive = false;

        // 4. 水：雨水收集器接雨（不再直接进仓库）
        int perCollector = 0;
        if (Rain != PrecipLevel.None) perCollector += rainWater[Mathf.Clamp((int)Rain - 1, 0, rainWater.Length - 1)];
        if (Snow != PrecipLevel.None) perCollector += snowWater[Mathf.Clamp((int)Snow - 1, 0, snowWater.Length - 1)];
        int overflow;
        int caught = WaterWorks.Collect(perCollector, out overflow);
        _waterThisSpell += caught;
        if (perCollector > 0 && WaterWorks.CollectorCount == 0) _rainWasted = true;

        // 5. 电力（风力发电 → 设备耗电；净化器只有在收集器里有雨水时才用电）
        PowerGrid.Tick(WindPowerPerTurn);

        // 6. 净化：通电的净化器把雨水变成净化水，放进仓库
        WaterWorks.Purify();

        // 7. 天气 / 水源的提示
        AnnounceWeather(w, oldRain, oldSnow, oldWind, oldHail);
        AnnounceWater(w, overflow);

        // 8. 每个人：冻伤、冰雹（体力消耗交给 NPC 健康系统，它读 EnergyFactor）
        if (!Hail) _hailHurt.Clear();
        _healthDelta.Clear();
        Vector3 camp = NpcNav.CampCenter(Vector3.zero);
        float campR = NpcNav.CampRadius + 1f;
        foreach (NpcData n in w.npcs)
        {
            if (n.IsDead) continue;
            float t = TemperatureOf(n);

            float dh = 0f;
            if (t < coldThreshold && !HasColdGear(n))
                dh -= coldHealthPerStep * Mathf.Ceil((coldThreshold - t) / coldStepDegrees);

            if (Hail)
            {
                NpcMover m = NpcView.GetMover(n.id);
                if (m != null && Vector2.Distance(m.transform.position, camp) > campR)
                {
                    dh -= hailDamagePerTurn;
                    if (_hailHurt.Add(n.id) && LogManager.Instance != null)
                        LogManager.Instance.AddLog(n.npcName, LanguageGuard.Pick(n.npcName + " 在营地外被冰雹砸伤了。", n.npcName + "は拠点の外で雹に打たれて怪我をした。"), n.id);
                }
            }
            if (dh != 0f) _healthDelta[n.id] = dh;
        }

        // 9. 营地气温状态变化的提示
        UpdateCampState(w);
    }

    void RollPrecip(float a)
    {
        Rain = PrecipLevel.None; Snow = PrecipLevel.None; Hail = false;
        float r = Random.value;
        float pHail = hailChance + 0.04f * a;
        float pPrecip = precipChance + 0.1f * a;
        if (r < pHail) { Hail = true; _precipTurns = 1; return; }
        if (r < pHail + pPrecip)
        {
            float lr = Random.value;
            float pHeavy = heavyChance + 0.15f * a;
            PrecipLevel lv = lr < pHeavy ? PrecipLevel.Heavy : (lr < pHeavy + moderateChance ? PrecipLevel.Moderate : PrecipLevel.Light);
            if (Ambient < snowBelow) Snow = lv; else Rain = lv;
            _precipTurns = Random.Range(1, 4);
            return;
        }
        _precipTurns = Random.Range(1, 3);
    }

    void RollWind(float a)
    {
        float r = Random.value;
        float pStrong = strongWindChance + 0.12f * a;
        if (r < pStrong) Wind = WindLevel.Strong;
        else if (r < pStrong + moderateWindChance) Wind = WindLevel.Moderate;
        else if (r < pStrong + moderateWindChance + breezeChance) Wind = WindLevel.Breeze;
        else Wind = WindLevel.None;
        _windTurns = Random.Range(1, 4);
    }

    // 天气对气温的影响（度）。
    float WeatherOffset()
    {
        float off = 0f;
        if (Snow != PrecipLevel.None) off -= 4f * (int)Snow;          // 小雪 -4 / 中雪 -8 / 大雪 -12
        if (Rain != PrecipLevel.None) off -= (int)Rain;               // 小雨 -1 / 中雨 -2 / 暴雨 -3
        if (Hail) off -= 6f;
        if (Wind != WindLevel.None) off -= (int)Wind == 3 ? 3f : (int)Wind * 0.5f + 0.5f; // 微风 -1 / 中风 -1.5 / 大风 -3
        if (StormActive || _stormTurns > 0) off -= 3f;
        if (Rain == PrecipLevel.None && Snow == PrecipLevel.None && !Hail && Wind <= WindLevel.Breeze) off += 5f; // 无风无雨：闷热
        return off;
    }

    // 导演选了「亚空间风暴」事件：天气直接变成暴雨 + 大风，持续 turns 回合。
    public void ForceStorm(int turns)
    {
        Rain = PrecipLevel.Heavy; Snow = PrecipLevel.None; Hail = false; Wind = WindLevel.Strong;
        _stormTurns = Mathf.Max(_stormTurns, turns);
        StormActive = true;
    }

    // ============ 查询 ============

    public float WindPowerPerTurn
    {
        get
        {
            if (StormActive) return stormPower;
            switch (Wind)
            {
                case WindLevel.Breeze: return breezePower;
                case WindLevel.Moderate: return moderatePower;
                case WindLevel.Strong: return strongPower;
                default: return 0f;
            }
        }
    }

    public float LocalTemperature(Vector3 pos) => Ambient + PowerGrid.TemperatureOffsetAt(pos);

    public float CampTemperature => LocalTemperature(NpcNav.CampCenter(Vector3.zero));

    public float TemperatureOf(NpcData n)
    {
        NpcMover m = n != null ? NpcView.GetMover(n.id) : null;
        return m != null ? LocalTemperature(m.transform.position) : Ambient;
    }

    public float HeatFactor(float t) => t > hotThreshold ? 1f + hotDrainPerDegree * (t - hotThreshold) : 1f;

    public TempState StateOf(float t)
    {
        if (t > extremeHot) return TempState.ExtremeHot;
        if (t < extremeCold) return TempState.ExtremeCold;
        if (t > hotThreshold) return TempState.Hot;
        if (t < coldThreshold) return TempState.Cold;
        return TempState.Normal;
    }

    bool HasColdGear(NpcData n)
    {
        if (n.inventory == null || string.IsNullOrEmpty(coldGearItemId)) return false;
        foreach (ItemStack s in n.inventory) if (s.itemName == coldGearItemId && s.count > 0) return true;
        return false;
    }

    // —— 给别的系统用的静态入口（没有气温系统时返回"无影响"）——

    // 体力消耗倍率（外出搜寻等消耗体力的行为乘上它）。
    public static float EnergyFactor(NpcData n) => Instance != null && n != null ? Instance.HeatFactor(Instance.TemperatureOf(n)) : 1f;

    // 这一回合气温 / 冰雹造成的生命变化（WorldRules.ApplyHealth 统一结算，死亡记录才不会漏）。
    public static float HealthDeltaFor(NpcData n)
    {
        if (Instance == null || n == null) return 0f;
        return Instance._healthDelta.TryGetValue(n.id, out float d) ? d : 0f;
    }

    // ============ 每秒：寒冷时走路变慢 ============
    void Update()
    {
        _speedTimer -= Time.deltaTime;
        if (_speedTimer > 0f || WorldManager.Instance == null) return;
        _speedTimer = 1f;
        foreach (NpcData n in WorldManager.Instance.world.npcs)
        {
            if (n.IsDead) continue;
            NpcMover m = NpcView.GetMover(n.id);
            if (m == null) continue;
            float t = LocalTemperature(m.transform.position);
            m.SetTemperatureFactor(t < coldThreshold ? coldSpeedFactor : 1f);
        }
    }

    // ============ 文字 ============

    static string PrecipName(PrecipLevel lv, bool snow, bool jp)
    {
        if (snow)
            switch (lv) { case PrecipLevel.Light: return jp ? "小雪" : "小雪"; case PrecipLevel.Moderate: return jp ? "雪" : "中雪"; case PrecipLevel.Heavy: return jp ? "大雪" : "大雪"; }
        else
            switch (lv) { case PrecipLevel.Light: return jp ? "小雨" : "小雨"; case PrecipLevel.Moderate: return jp ? "雨" : "中雨"; case PrecipLevel.Heavy: return jp ? "豪雨" : "暴雨"; }
        return "";
    }

    static string WindName(WindLevel w, bool jp)
    {
        switch (w)
        {
            case WindLevel.Breeze: return jp ? "そよ風" : "微风";
            case WindLevel.Moderate: return jp ? "風" : "中风";
            case WindLevel.Strong: return jp ? "強風" : "大风";
        }
        return "";
    }

    // 天气的简短描述："暴雨 · 大风"（风暴时"亚空间风暴"）。
    public string WeatherText(bool jp)
    {
        if (StormActive) return jp ? "亜空間の嵐" : "亚空间风暴";
        List<string> parts = new List<string>();
        if (Hail) parts.Add(jp ? "雹" : "冰雹");
        if (Rain != PrecipLevel.None) parts.Add(PrecipName(Rain, false, jp));
        if (Snow != PrecipLevel.None) parts.Add(PrecipName(Snow, true, jp));
        if (Wind != WindLevel.None) parts.Add(WindName(Wind, jp));
        if (parts.Count == 0) return jp ? "穏やか" : "无风无雨";
        return string.Join(" · ", parts);
    }

    // HUD 上的一行："暴雨 · 大风　12℃（营地 18℃）"
    public string HudText(bool jp)
    {
        float camp = CampTemperature;
        string s = WeatherText(jp) + "　" + Mathf.RoundToInt(Ambient) + "℃";
        if (Mathf.Abs(camp - Ambient) >= 1f) s += (jp ? "（拠点 " : "（营地 ") + Mathf.RoundToInt(camp) + "℃）";
        return s;
    }

    // 给 LLM 的一行（NPC 对话、导演都会看到）。
    public string PromptLine(bool jp)
    {
        float camp = CampTemperature;
        string s = (jp ? "・天気：" : "・天气：") + WeatherText(jp) + (jp ? "。気温 " : "；气温 ") + Mathf.RoundToInt(Ambient) + "℃";
        if (Mathf.Abs(camp - Ambient) >= 1f) s += (jp ? "（拠点のまわり " : "（营地附近 ") + Mathf.RoundToInt(camp) + "℃）";
        switch (StateOf(camp))
        {
            case TempState.Hot: s += jp ? "。暑くて体力の消耗が早い" : "，很热，体力消耗得快"; break;
            case TempState.Cold: s += jp ? "。寒くて体が思うように動かず、凍えて体を傷めている" : "，很冷，手脚发僵，冻得伤身"; break;
            case TempState.ExtremeHot: s += jp ? "。異常な暑さで、拠点の設備がまともに動かない" : "，热得反常，营地的设施都不太正常"; break;
            case TempState.ExtremeCold: s += jp ? "。異常な寒さで、拠点の設備がまともに動かない" : "，冷得反常，营地的设施都不太正常"; break;
        }
        return s;
    }

    void AnnounceWeather(WorldState w, PrecipLevel oldRain, PrecipLevel oldSnow, WindLevel oldWind, bool oldHail)
    {
        bool jp = LanguageGuard.IsJp;
        List<string> msgs = new List<string>();
        bool hadWater = oldRain != PrecipLevel.None || oldSnow != PrecipLevel.None;
        bool hasWater = Rain != PrecipLevel.None || Snow != PrecipLevel.None;

        bool noCollector = WaterWorks.CollectorCount == 0;
        if (Rain != oldRain && Rain != PrecipLevel.None)
            msgs.Add(jp ? PrecipName(Rain, false, true) + "が降りはじめた。" + (noCollector ? "雨水タンクを建てれば水を集められる。" : "雨水タンクに水がたまっていく。")
                        : "开始下" + PrecipName(Rain, false, false) + "了，" + (noCollector ? "建雨水收集器才能接到雨水。" : "雨水收集器开始接水。"));
        if (Snow != oldSnow && Snow != PrecipLevel.None)
            msgs.Add(jp ? PrecipName(Snow, true, true) + "が降りはじめた。気温が下がっていく。" : "开始下" + PrecipName(Snow, true, false) + "了，气温在下降。");
        if (Hail && !oldHail)
            msgs.Add(jp ? "雹が降ってきた！拠点の外にいる者は怪我をする。" : "下冰雹了！在营地外面的人会被砸伤。");
        if (hadWater && !hasWater)
        {
            bool needPurifier = WaterWorks.PurifierCount == 0;
            if (_waterThisSpell > 0)
                msgs.Add(jp ? "降水がやんだ。雨水タンクに合わせて " + _waterThisSpell + " 杯分の雨水がたまった。" + (needPurifier ? "飲むには浄水器が必要だ。" : "")
                            : "雨雪停了，雨水收集器这一场一共接到 " + _waterThisSpell + " 份雨水。" + (needPurifier ? "要建水源净化器才能变成能喝的净化水。" : ""));
            else if (_rainWasted)
                msgs.Add(jp ? "降水がやんだ。雨水タンクがなく、雨水はそのまま流れてしまった。" : "雨雪停了。没有雨水收集器，雨水都白白流走了。");
            _waterThisSpell = 0;
            _rainWasted = false;
            _fullWarned = false;
        }
        if (Wind != oldWind && !StormActive)
        {
            if (Wind == WindLevel.None) msgs.Add(jp ? "風がやんだ。" : "风停了。");
            else msgs.Add(jp ? WindName(Wind, true) + "が吹いている。" : "刮起了" + WindName(Wind, false) + "。");
        }
        if (msgs.Count == 0 || EventLogManager.Instance == null) return;
        EventLogManager.Instance.AddEvent(w.day, string.Join(" ", msgs));
    }

    // 收集器装满 / 净化器没电：各提示一次，情况变了再提示。
    void AnnounceWater(WorldState w, int overflow)
    {
        if (EventLogManager.Instance == null) return;
        bool jp = LanguageGuard.IsJp;
        if (overflow > 0 && !_fullWarned)
        {
            _fullWarned = true;
            EventLogManager.Instance.AddEvent(w.day, jp ? "雨水タンクが満杯になり、雨水があふれている。浄水器で浄化するか、タンクを増やそう。"
                                                      : "雨水收集器装满了，多的雨水流走了。用水源净化器净化掉，或者多建几个收集器。");
        }
        bool waiting = WaterWorks.RawTotal > 0;
        bool anyPurifierOn = false, anyPowered = false;
        foreach (PowerNode p in PowerNode.All)
        {
            if (p == null || p.kind != PowerNodeKind.Purifier || !p.IsBuilt || p.level <= 0) continue;
            anyPurifierOn = true;
            if (p.powered) anyPowered = true;
        }
        bool noPower = waiting && anyPurifierOn && !anyPowered;
        if (noPower && !_noPowerWarned)
            EventLogManager.Instance.AddEvent(w.day, jp ? "浄水器に電気が来ていない。雨水が浄化されずにたまっている。" : "水源净化器没有电，雨水净化不了。检查电线和发电机。");
        _noPowerWarned = noPower;
    }

    void UpdateCampState(WorldState w)
    {
        float camp = CampTemperature;
        TempState s = StateOf(camp);
        if (s == CampState) return;
        CampState = s;
        bool jp = LanguageGuard.IsJp;
        int t = Mathf.RoundToInt(camp);
        string msg;
        switch (s)
        {
            case TempState.Hot: msg = jp ? "気温が " + t + "℃ まで上がった。暑さで体力の消耗が早くなる。" : "气温升到 " + t + "℃，太热了，大家体力消耗变快。"; break;
            case TempState.Cold: msg = jp ? "気温が " + t + "℃ まで下がった。寒さで動きが鈍り、防寒具がないと体を傷める。" : "气温降到 " + t + "℃，太冷了，大家行动变慢，没有防寒装备会冻伤。"; break;
            case TempState.ExtremeHot: msg = jp ? "異常な高温（" + t + "℃）。拠点の設備が制限されている。" : "异常高温（" + t + "℃）！营地基础设施受限。"; break;
            case TempState.ExtremeCold: msg = jp ? "異常な低温（" + t + "℃）。拠点の設備が制限されている。" : "异常低温（" + t + "℃）！营地基础设施受限。"; break;
            default: msg = jp ? "気温が平常に戻った（" + t + "℃）。" : "气温恢复正常（" + t + "℃）。"; break;
        }
        if (EventLogManager.Instance != null) EventLogManager.Instance.AddEvent(w.day, msg);
        if (w.eventLog != null) w.eventLog.Add(new MemoryEntry { day = w.day, content = msg });
    }
}
