using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 状态词条（NPC 健康系统）。存进 NpcData.conditions 的是下面 TagId 里的英文 id。
public enum HealthTag
{
    Dying,          // 濒死：无法移动、工作、战斗
    CriticalWound,  // 致命伤：移动 -80%，无法工作、战斗
    Injured,        // 受伤：移动 -60%，体力消耗 +0.5/秒，无法工作
    Psychosis,      // 精神病：对营地的人信任降为 0，移动 +30%，理智 -0.2/秒
    Sick,           // 生病：移动 -40%，饥饿消耗 +0.5/秒，理智 -0.2/秒
    Parched,        // 饥渴难耐：好感 -20%，体力消耗 +0.6/秒，理智 -0.5/秒
    VeryThirsty,    // 口渴难耐：体力消耗 +0.6/秒
    Hungry,         // 饥饿：对其他人好感 -20%
    Thirsty,        // 口渴：体力消耗 +0.3/秒
    Fatigued        // 疲惫：移动 -30%，工作速度 -50%
}

public enum InjuryCause { Generic, Hail, Field, Combat }

// ============================================================================
// NPC 健康系统（纯规则，不走 LLM）。挂在场景里一个物体上（WorldManager.Start 调 NpcHealth.Ensure()），
// 每帧按"每秒"结算（建造模式暂停游戏时也跟着停）。
//
// 【饥饿值】NpcData.satiety：100 = 不饿，0 = 饿坏了（旧代码里的 hunger 是它的反面，保留成兼容属性）。
//   每秒 -0.3；吃东西回复；为 0 时每秒掉 0.5 生命。偏低时获得「饥饿」。
// 【水分】NpcData.water：100 = 不渴。每秒 -WaterPerSecond（炎热时按 ClimateSystem.EnergyFactor 加快）；
//   低于 DrinkBelow 时自动从仓库喝一份净化水（WaterWorks.TryDrink）；为 0 时每秒掉 0.5 生命。
// 【体力】走路不消耗（energyPerUnitMoved = 0）；施工每秒 -0.3；战斗每秒 -2（ConsumeCombat，留给以后的战斗系统）；超过 30℃ 每高 1 度消耗 +5%；
//   原地休息每秒 +0.3，同时额外消耗饥饿值。偏低时获得「疲惫」，NPC 会自己去休息，闲置活动也变少。
// 【理智】沿用原来的（WorldRules），这里只加上各词条带来的每秒下降。
// 【生命】受伤 / 致命伤 / 濒死 / 生病 / 精神病，具体效果见 HealthTag。
// 所有数值都在 Inspector 里（挂着 NpcHealth 的物体），以后调平衡改这里。
// ============================================================================
public class NpcHealth : MonoBehaviour
{
    public static NpcHealth Instance { get; private set; }

    [Header("体力")]
    public float energyPerUnitMoved = 0f;        // 每走 1 格消耗（0 = 走路不累：原来 0.3 时 NPC 走几趟就累得不动了）
    public float buildEnergyPerSecond = 0.3f;  // 施工每秒消耗（原来 1）
    public float combatEnergyPerSecond = 2f;   // 战斗每秒消耗
    public float restEnergyPerSecond = 0.3f;   // 原地休息每秒恢复
    [Tooltip("休息时每恢复 1 点体力，额外消耗多少饥饿值")]
    public float restSatietyPerEnergy = 0.5f;
    public float fatigueBelow = 25f;           // 体力低于它 → 疲惫
    public float fatigueClearAt = 60f;         // 体力回到它以上 → 疲惫消失
    [Range(0f, 1f)] public float fatigueMoveFactor = 0.7f;
    [Range(0f, 1f)] public float fatigueWorkFactor = 0.5f;

    [Header("饥饿值（100 = 不饿，0 = 饿）")]
    public float satietyPerSecond = 0.3f;
    public float starvingHealthPerSecond = 0.5f;
    public float hungryBelow = 30f;
    public float hungryClearAt = 50f;
    [Tooltip("饥饿 / 饥渴难耐时，对其他人的好感降低多少（-100~100 的好感度里减掉的点数，20 = 降低 20%）")]
    public float affinityPenalty = 20f;

    [Header("水分（100 = 不渴，0 = 渴坏了）")]
    public float waterPerSecond = 0.5f;
    public float dehydratedHealthPerSecond = 0.5f;
    public float drinkBelow = 60f;             // 低于它就自动喝水
    public float waterPerDrink = 35f;          // 喝一份净化水回复多少
    public float thirstyBelow = 40f, thirstyClearAt = 50f;
    public float veryThirstyBelow = 15f, veryThirstyClearAt = 25f;
    public float thirstyEnergyPerSecond = 0.3f;
    public float veryThirstyEnergyPerSecond = 0.6f;
    [Tooltip("饥饿值和水分都低于它 → 饥渴难耐")]
    public float parchedBelow = 30f;
    public float parchedClearAt = 45f;
    public float parchedEnergyPerSecond = 0.6f;
    public float parchedSanityPerSecond = 0.5f;

    [Header("生病")]
    [Range(0f, 1f)] public float sickMoveFactor = 0.6f;
    public float sickSatietyPerSecond = 0.5f;
    public float sickSanityPerSecond = 0.2f;
    [Tooltip("每天得病的基础概率")]
    [Range(0f, 1f)] public float sickDailyChance = 0.02f;
    public Vector2 sickDays = new Vector2(2f, 4f);   // 病几天自己好

    [Header("受伤 / 致命伤 / 濒死")]
    [Range(0f, 1f)] public float injuredMoveFactor = 0.4f;
    public float injuredEnergyPerSecond = 0.5f;
    public float injuryHealDays = 3f;                // 受伤几天后（期间没再受伤）自己好
    public float criticalBelow = 30f, criticalClearAt = 40f;
    [Range(0f, 1f)] public float criticalMoveFactor = 0.2f;
    public float dyingBelow = 10f, dyingClearAt = 20f;
    [Tooltip("吃饱喝足、没生病时，每秒自然回血")]
    public float regenPerSecond = 0.05f;
    [Tooltip("外出搜寻时受伤的概率（异常度越高越容易）")]
    [Range(0f, 1f)] public float fieldInjuryChance = 0.06f;
    public float fieldInjuryDamage = 8f;

    [Header("精神病")]
    public float psychosisBelow = 15f, psychosisClearAt = 35f;
    public float psychosisMoveFactor = 1.3f;
    public float psychosisSanityPerSecond = 0.2f;

    [Header("雾")]
    [Tooltip("NPC 站在雾里时每秒掉多少理智（营地和雾散开的区域里不掉）。0 = 不掉")]
    public float fogSanityPerSecond = 1f;

    [Header("其他")]
    [Tooltip("一天（一回合）多少秒，和 AIDirector.secondsPerTurn 一致")]
    public float secondsPerDay = 20f;

    // —— 每个 NPC 的运行时状态（不存档）——
    class State
    {
        public bool resting;
        public float sinceInjury = 9999f;
        public float sickLeft;
        public int injuryPart = -1;
        public InjuryCause injuryCause;
        public int sickness = -1;
        public float drinkCooldown;
        public bool wasDead;
    }

    readonly Dictionary<string, State> _states = new Dictionary<string, State>();
    float _speedTimer;
    int _lastDay = -1;

    public static NpcHealth Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("NpcHealth").AddComponent<NpcHealth>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        AIDirector d = FindFirstObjectByType<AIDirector>();
        if (d != null) secondsPerDay = d.secondsPerTurn;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        WorldState w = WorldManager.Instance != null ? WorldManager.Instance.world : null;
        if (w == null) return;
        float dt = Time.deltaTime;
        if (dt > 0f) foreach (NpcData n in w.npcs) Tick(n, dt, w);

        if (w.day != _lastDay)
        {
            if (_lastDay >= 0) OnNewDay(w);
            _lastDay = w.day;
        }

        // 移动速度：每 0.25 秒同步一次
        _speedTimer -= Time.unscaledDeltaTime;
        if (_speedTimer <= 0f)
        {
            _speedTimer = 0.25f;
            foreach (NpcData n in w.npcs)
            {
                NpcMover m = NpcView.GetMover(n.id);
                if (m != null) m.SetHealthFactor(MoveFactor(n));
            }
        }
    }

    // 时间失序之类"跳过一段时间"的事件：把这段时间的消耗一次结算掉（分成 1 秒一步）。
    public static void Advance(WorldState w, float seconds)
    {
        if (Instance == null || w == null) return;
        for (float t = 0f; t < seconds; t += 1f)
            foreach (NpcData n in w.npcs) Instance.Tick(n, Mathf.Min(1f, seconds - t), w);
    }

    // 这个 NPC 现在是不是站在雾里（只在永久雾模式下算；场景里找不到他时不算）。
    static bool InFog(NpcData n)
    {
        FogManager f = FogManager.Instance;
        if (f == null || !f.permanentFog) return false;
        NpcMover m = NpcView.GetMover(n.id);
        return m != null && FogManager.InFog(m.transform.position);
    }

    // ============ 每帧结算 ============

    void Tick(NpcData n, float dt, WorldState w)
    {
        State s = StateOf(n.id);
        if (n.IsDead)
        {
            if (n.conditions != null && n.conditions.Count > 0) n.conditions.Clear();
            s.wasDead = true;
            return;
        }

        // 1. 饥饿值（休息时用饥饿值换体力）
        float satDrain = satietyPerSecond + (Has(n, HealthTag.Sick) ? sickSatietyPerSecond : 0f);
        if (s.resting && n.energy < 100f && n.satiety > 0f)
        {
            float rec = Mathf.Min(restEnergyPerSecond * dt, 100f - n.energy);
            n.energy += rec;
            satDrain += restEnergyPerSecond * restSatietyPerEnergy;
        }
        n.satiety = Mathf.Clamp(n.satiety - satDrain * dt, 0f, 100f);

        // 2. 水分（低了自动喝）
        n.water = Mathf.Clamp(n.water - waterPerSecond * ClimateSystem.EnergyFactor(n) * dt, 0f, 100f);
        s.drinkCooldown -= dt;
        if (n.water < drinkBelow && s.drinkCooldown <= 0f)
        {
            s.drinkCooldown = 2f;
            if (WaterWorks.TryDrink(n)) n.water = Mathf.Clamp(n.water + waterPerDrink, 0f, 100f);
        }

        // 3. 体力：词条带来的额外消耗
        float eDrain = 0f;
        if (Has(n, HealthTag.Injured) || Has(n, HealthTag.CriticalWound)) eDrain += injuredEnergyPerSecond;
        float thirst = 0f;
        if (Has(n, HealthTag.Thirsty)) thirst = Mathf.Max(thirst, thirstyEnergyPerSecond);
        if (Has(n, HealthTag.VeryThirsty)) thirst = Mathf.Max(thirst, veryThirstyEnergyPerSecond);
        if (Has(n, HealthTag.Parched)) thirst = Mathf.Max(thirst, parchedEnergyPerSecond);
        eDrain += thirst;
        if (eDrain > 0f) n.energy = Mathf.Clamp(n.energy - eDrain * ClimateSystem.EnergyFactor(n) * dt, 0f, 100f); // 炎热时消耗更快

        // 4. 理智
        if (!n.isBroken)
        {
            float san = 0f;
            if (Has(n, HealthTag.Sick)) san += sickSanityPerSecond;
            if (Has(n, HealthTag.Psychosis)) san += psychosisSanityPerSecond;
            if (Has(n, HealthTag.Parched)) san += parchedSanityPerSecond;
            if (fogSanityPerSecond > 0f && InFog(n)) san += fogSanityPerSecond;   // 永久雾：人在雾里掉理智
            if (san > 0f)
            {
                n.sanity = Mathf.Clamp(n.sanity - san * dt, 0f, 100f);
                n.isHallucinating = n.sanity < WorldRules.HallucinationThreshold;
            }
        }

        // 5. 生命
        float hp = 0f;
        if (n.satiety <= 0f) hp -= starvingHealthPerSecond;
        if (n.water <= 0f) hp -= dehydratedHealthPerSecond;
        if (hp == 0f && n.satiety >= 40f && n.water >= 40f && !Has(n, HealthTag.Sick) && !n.isBroken && n.health < 100f)
            hp += regenPerSecond;
        if (hp != 0f) n.health = Mathf.Clamp(n.health + hp * dt, 0f, 100f);

        // 6. 计时
        s.sinceInjury += dt;
        if (s.sickLeft > 0f) s.sickLeft -= dt;

        // 7. 词条
        UpdateTags(n, s);

        // 8. 被饿死 / 渴死：和每回合结算一样记下死亡（日志、事件编年史）
        if (n.health <= 0f && !s.wasDead)
        {
            s.wasDead = true;
            WorldRules.Die(w, n);
        }
    }

    void UpdateTags(NpcData n, State s)
    {
        // 疲惫
        if (n.energy < fatigueBelow) Gain(n, HealthTag.Fatigued);
        else if (n.energy >= fatigueClearAt) Lose(n, HealthTag.Fatigued);

        // 饥饿
        if (n.satiety < hungryBelow) Gain(n, HealthTag.Hungry);
        else if (n.satiety >= hungryClearAt) Lose(n, HealthTag.Hungry);

        // 口渴两档（同一时间只有一档）
        if (n.water < veryThirstyBelow) { Lose(n, HealthTag.Thirsty, false); Gain(n, HealthTag.VeryThirsty); }
        else if (Has(n, HealthTag.VeryThirsty))
        {
            if (n.water >= veryThirstyClearAt) { Lose(n, HealthTag.VeryThirsty, false); if (n.water < thirstyClearAt) Gain(n, HealthTag.Thirsty, false); }
        }
        else if (n.water < thirstyBelow) Gain(n, HealthTag.Thirsty);
        else if (n.water >= thirstyClearAt) Lose(n, HealthTag.Thirsty);

        // 饥渴难耐
        if (n.satiety < parchedBelow && n.water < parchedBelow) Gain(n, HealthTag.Parched);
        else if (n.satiety >= parchedClearAt || n.water >= parchedClearAt) Lose(n, HealthTag.Parched);

        // 生病（得病在每天的掷骰里）
        if (Has(n, HealthTag.Sick) && s.sickLeft <= 0f) { Lose(n, HealthTag.Sick); s.sickness = -1; }

        // 受伤：几天没再受伤、血量回到致命线以上就好了
        if (Has(n, HealthTag.Injured) && s.sinceInjury >= injuryHealDays * secondsPerDay && n.health > criticalBelow)
        { Lose(n, HealthTag.Injured); s.injuryPart = -1; }

        // 致命伤：受伤且血量很低
        if (Has(n, HealthTag.Injured) && n.health <= criticalBelow) Gain(n, HealthTag.CriticalWound);
        else if (Has(n, HealthTag.CriticalWound) && (n.health > criticalClearAt || !Has(n, HealthTag.Injured))) Lose(n, HealthTag.CriticalWound);

        // 濒死
        if (n.health <= dyingBelow) Gain(n, HealthTag.Dying);
        else if (n.health > dyingClearAt) Lose(n, HealthTag.Dying);

        // 精神病（已崩解的人走崩解那一套）
        if (!n.isBroken && n.sanity <= psychosisBelow) Gain(n, HealthTag.Psychosis);
        else if (n.isBroken || n.sanity >= psychosisClearAt) Lose(n, HealthTag.Psychosis);
    }

    // 每天一次：得病掷骰。
    void OnNewDay(WorldState w)
    {
        float a = Mathf.Clamp01(w.anomalyLevel / 100f);
        foreach (NpcData n in w.npcs)
        {
            if (n.IsDead || n.isBroken || Has(n, HealthTag.Sick)) continue;
            float chance = sickDailyChance + a * 0.05f;
            if (Has(n, HealthTag.VeryThirsty) || Has(n, HealthTag.Parched)) chance += 0.08f;
            if (Has(n, HealthTag.Hungry)) chance += 0.05f;
            if (ClimateSystem.Instance != null)
            {
                TempState t = ClimateSystem.Instance.StateOf(ClimateSystem.Instance.TemperatureOf(n));
                if (t != TempState.Normal) chance += 0.05f;
            }
            if (Random.value >= chance) continue;
            State s = StateOf(n.id);
            s.sickness = Random.Range(0, SicknessNames.GetLength(0));
            s.sickLeft = Random.Range(sickDays.x, sickDays.y) * secondsPerDay;
            Gain(n, HealthTag.Sick);
        }
    }

    // ============ 词条的增减 + 提示 ============

    static readonly string[] TagIds = { "dying", "critical", "injured", "psychosis", "sick", "parched", "very_thirsty", "hungry", "thirsty", "fatigued" };
    public static string TagId(HealthTag t) => TagIds[(int)t];

    public static bool Has(NpcData n, HealthTag t) => n != null && n.conditions != null && n.conditions.Contains(TagId(t));

    void Gain(NpcData n, HealthTag t, bool announce = true)
    {
        if (Has(n, t)) return;
        if (n.conditions == null) n.conditions = new List<string>();
        n.conditions.Add(TagId(t));
        if (announce) Announce(n, t, true);
    }

    void Lose(NpcData n, HealthTag t, bool announce = true)
    {
        if (n.conditions == null || !n.conditions.Remove(TagId(t))) return;
        if (announce) Announce(n, t, false);
    }

    void Announce(NpcData n, HealthTag t, bool gained)
    {
        bool jp = LanguageGuard.IsJp;
        string name = TagName(n, t, jp);
        string msg = gained
            ? (jp ? n.npcName + "：「" + name + "」になった。" : n.npcName + " 出现了「" + name + "」。")
            : (jp ? n.npcName + "：「" + name + "」が治まった。" : n.npcName + " 的「" + name + "」消失了。");
        if (LogManager.Instance != null) LogManager.Instance.AddLog(n.npcName, msg, n.id);
        // 严重的状态写进记忆，AI 演员和事件推理都看得到
        bool serious = t == HealthTag.Sick || t == HealthTag.Injured || t == HealthTag.CriticalWound || t == HealthTag.Dying || t == HealthTag.Psychosis;
        if (serious && n.memories != null && WorldManager.Instance != null)
            n.memories.Add(new MemoryEntry { day = WorldManager.Instance.world.day, content = msg });
    }

    // ============ 对外接口 ============

    State StateOf(string id)
    {
        if (!_states.TryGetValue(id ?? "", out State s)) { s = new State(); _states[id ?? ""] = s; }
        return s;
    }

    static NpcData Find(string id) => WorldManager.Instance != null && !string.IsNullOrEmpty(id) ? WorldManager.Instance.GetNpcById(id) : null;

    // NpcMover 每帧报告走了多远。
    public static void OnMoved(string npcId, float distance)
    {
        if (Instance == null || distance <= 0f) return;
        NpcData n = Find(npcId);
        if (n == null || n.IsDead) return;
        n.energy = Mathf.Clamp(n.energy - distance * Instance.energyPerUnitMoved * ClimateSystem.EnergyFactor(n), 0f, 100f); // 炎热：每高 1 度 +5%
    }

    public static void ConsumeBuild(NpcData n, float dt)
    {
        if (Instance == null || n == null) return;
        n.energy = Mathf.Clamp(n.energy - Instance.buildEnergyPerSecond * ClimateSystem.EnergyFactor(n) * dt, 0f, 100f);
    }

    public static void ConsumeCombat(NpcData n, float dt)
    {
        if (Instance == null || n == null) return;
        n.energy = Mathf.Clamp(n.energy - Instance.combatEnergyPerSecond * ClimateSystem.EnergyFactor(n) * dt, 0f, 100f);
    }

    // 开始 / 停止原地休息（休息期间每秒回体力、额外耗饥饿值）。
    public static void SetResting(string npcId, bool resting)
    {
        if (Instance == null || string.IsNullOrEmpty(npcId)) return;
        Instance.StateOf(npcId).resting = resting;
    }

    public static bool IsResting(string npcId) => Instance != null && !string.IsNullOrEmpty(npcId) && Instance.StateOf(npcId).resting;

    // 受伤：扣血 + 「受伤」词条（伤在哪由原因决定）。
    public static void Injure(NpcData n, float damage, InjuryCause cause)
    {
        if (n == null || n.IsDead) return;
        n.health = Mathf.Clamp(n.health - damage, 0f, 100f);
        MarkInjured(n, cause);
    }

    // 只加「受伤」词条（伤害由别处扣，比如冰雹在每回合结算里扣血）。
    public static void MarkInjured(NpcData n, InjuryCause cause)
    {
        if (Instance == null || n == null || n.IsDead) return;
        State s = Instance.StateOf(n.id);
        s.sinceInjury = 0f;
        if (!Has(n, HealthTag.Injured) || s.injuryPart < 0)
        {
            s.injuryCause = cause;
            s.injuryPart = Random.Range(0, PartsOf(cause).GetLength(0));
        }
        Instance.Gain(n, HealthTag.Injured);
    }

    // 外出搜寻时掷一次受伤（异常度越高越容易）。返回是否受伤。
    public static bool RollFieldInjury(NpcData n)
    {
        if (Instance == null || n == null) return false;
        float a = WorldManager.Instance != null ? Mathf.Clamp01(WorldManager.Instance.world.anomalyLevel / 100f) : 0f;
        if (Random.value >= Instance.fieldInjuryChance * (1f + a)) return false;
        Injure(n, Instance.fieldInjuryDamage, InjuryCause.Field);
        return true;
    }

    // 用了物品之后（ItemUseRules 调）：急救包治好受伤和生病。
    public static void OnItemUsed(NpcData n, string itemId)
    {
        if (Instance == null || n == null) return;
        if (itemId == "medkit")
        {
            State s = Instance.StateOf(n.id);
            Instance.Lose(n, HealthTag.Injured);
            Instance.Lose(n, HealthTag.Sick);
            s.injuryPart = -1;
            s.sickness = -1;
            s.sickLeft = 0f;
        }
        Instance.UpdateTags(n, Instance.StateOf(n.id));
    }

    // —— 效果 ——

    public static float MoveFactor(NpcData n)
    {
        if (Instance == null || n == null) return 1f;
        if (Has(n, HealthTag.Dying)) return 0f;
        float f = 1f;
        if (Has(n, HealthTag.CriticalWound)) f *= Instance.criticalMoveFactor;
        else if (Has(n, HealthTag.Injured)) f *= Instance.injuredMoveFactor;
        if (Has(n, HealthTag.Sick)) f *= Instance.sickMoveFactor;
        if (Has(n, HealthTag.Fatigued)) f *= Instance.fatigueMoveFactor;
        if (Has(n, HealthTag.Psychosis)) f *= Instance.psychosisMoveFactor;
        return f;
    }

    // 工作速度倍率：0 = 不能工作。
    public static float WorkSpeed(NpcData n)
    {
        if (n == null || n.IsDead || n.isBroken) return 0f;
        if (Has(n, HealthTag.Dying) || Has(n, HealthTag.CriticalWound) || Has(n, HealthTag.Injured)) return 0f;
        return Has(n, HealthTag.Fatigued) && Instance != null ? Instance.fatigueWorkFactor : 1f;
    }

    public static bool CanWork(NpcData n) => WorkSpeed(n) > 0f;
    public static bool CanMove(NpcData n) => n != null && !n.IsDead && !Has(n, HealthTag.Dying);
    public static bool CanFight(NpcData n) => n != null && !n.IsDead && !Has(n, HealthTag.Dying) && !Has(n, HealthTag.CriticalWound);

    // 不能工作的原因（给玩家看）。能工作返回 null。
    public static string CannotWorkReason(NpcData n, bool jp)
    {
        if (n == null) return null;
        if (n.IsDead) return jp ? "死亡" : "已死亡";
        if (n.isBroken) return jp ? "人格崩壊" : "人格崩解";
        if (Has(n, HealthTag.Dying)) return TagName(n, HealthTag.Dying, jp);
        if (Has(n, HealthTag.CriticalWound)) return TagName(n, HealthTag.CriticalWound, jp);
        if (Has(n, HealthTag.Injured)) return TagName(n, HealthTag.Injured, jp);
        return null;
    }

    // 对别人好感的减值（饥饿 / 饥渴难耐，不叠加）。
    public static float AffinityPenalty(NpcData n)
    {
        if (Instance == null || n == null) return 0f;
        return Has(n, HealthTag.Hungry) || Has(n, HealthTag.Parched) ? Instance.affinityPenalty : 0f;
    }

    // 精神病：谁也不信。
    public static bool DistrustsEveryone(NpcData n) => Has(n, HealthTag.Psychosis);

    // 闲置活动的间隔倍率：累了、饿了就不爱动（活动没那么频繁）。
    public static float IdleGapMultiplier(NpcData n)
    {
        if (n == null) return 1f;
        float m = 1f;
        if (Has(n, HealthTag.Fatigued)) m *= 2.5f;
        if (Has(n, HealthTag.Hungry) || Has(n, HealthTag.Parched)) m *= 2f;
        if (Has(n, HealthTag.Sick)) m *= 1.5f;
        return m;
    }

    // 按严重程度排好的词条（濒死在最前）。
    public static List<HealthTag> Tags(NpcData n)
    {
        List<HealthTag> list = new List<HealthTag>();
        if (n == null || n.conditions == null) return list;
        foreach (HealthTag t in System.Enum.GetValues(typeof(HealthTag)))
            if (Has(n, t)) list.Add(t);
        return list; // 枚举顺序就是严重程度
    }

    // 没有任何生命类词条（受伤 / 致命伤 / 濒死 / 生病 / 精神病）= 「健康」。
    public static bool IsHealthy(NpcData n)
    {
        return n != null && !n.IsDead && !Has(n, HealthTag.Dying) && !Has(n, HealthTag.CriticalWound) && !Has(n, HealthTag.Injured)
               && !Has(n, HealthTag.Sick) && !Has(n, HealthTag.Psychosis);
    }

    // ============ 文字 ============

    static readonly string[,] Names =
    {
        { "濒死", "瀕死" }, { "致命伤", "致命傷" }, { "受伤", "負傷" }, { "精神病", "精神錯乱" }, { "生病", "病気" },
        { "饥渴难耐", "飢えと渇き" }, { "口渴难耐", "ひどい渇き" }, { "饥饿", "空腹" }, { "口渴", "渇き" }, { "疲惫", "疲労" }
    };

    static readonly string[,] SicknessNames = { { "发烧", "発熱" }, { "咳嗽不止", "ひどい咳" }, { "腹泻", "下痢" }, { "头痛恶心", "頭痛と吐き気" } };
    static readonly string[,] HailParts = { { "头", "頭" }, { "肩膀", "肩" }, { "后背", "背中" } };
    static readonly string[,] FieldParts = { { "脚踝", "足首" }, { "小腿", "すね" }, { "手掌", "手のひら" } };
    static readonly string[,] CombatParts = { { "手臂", "腕" }, { "肋骨", "あばら" }, { "大腿", "太もも" } };
    static readonly string[,] GenericParts = { { "身上", "体" } };

    static string[,] PartsOf(InjuryCause c)
    {
        switch (c)
        {
            case InjuryCause.Hail: return HailParts;
            case InjuryCause.Field: return FieldParts;
            case InjuryCause.Combat: return CombatParts;
            default: return GenericParts;
        }
    }

    public static string TagName(HealthTag t, bool jp) => Names[(int)t, jp ? 1 : 0];

    // 带细节的名字：受伤（左腿）、生病（发烧）。
    public static string TagName(NpcData n, HealthTag t, bool jp)
    {
        string name = TagName(t, jp);
        string detail = Detail(n, t, jp);
        return string.IsNullOrEmpty(detail) ? name : name + "（" + detail + "）";
    }

    static string Detail(NpcData n, HealthTag t, bool jp)
    {
        if (Instance == null || n == null) return null;
        State s = Instance.StateOf(n.id);
        if ((t == HealthTag.Injured || t == HealthTag.CriticalWound) && s.injuryPart >= 0)
        {
            string[,] parts = PartsOf(s.injuryCause);
            return parts[Mathf.Clamp(s.injuryPart, 0, parts.GetLength(0) - 1), jp ? 1 : 0];
        }
        if (t == HealthTag.Sick && s.sickness >= 0) return SicknessNames[Mathf.Clamp(s.sickness, 0, SicknessNames.GetLength(0) - 1), jp ? 1 : 0];
        return null;
    }

    public static string InjuryPart(NpcData n, bool jp)
    {
        string d = Detail(n, HealthTag.Injured, jp);
        return string.IsNullOrEmpty(d) ? (jp ? "体" : "身上") : d;
    }

    public static Color TagColor(HealthTag t)
    {
        switch (t)
        {
            case HealthTag.Dying:
            case HealthTag.CriticalWound: return new Color(0.62f, 0.08f, 0.1f, 1f);
            case HealthTag.Injured: return new Color(0.6f, 0.2f, 0.16f, 1f);
            case HealthTag.Psychosis: return new Color(0.42f, 0.16f, 0.5f, 1f);
            case HealthTag.Sick: return new Color(0.36f, 0.42f, 0.14f, 1f);
            case HealthTag.Parched:
            case HealthTag.Hungry: return new Color(0.55f, 0.36f, 0.1f, 1f);
            case HealthTag.VeryThirsty:
            case HealthTag.Thirsty: return new Color(0.14f, 0.36f, 0.55f, 1f);
            default: return new Color(0.3f, 0.3f, 0.36f, 1f);
        }
    }

    public static readonly Color HealthyColor = new Color(0.16f, 0.42f, 0.24f, 1f);

    // 一行简短描述（闲聊人物卡、世界概况用）：如「受伤（脚踝）、饥饿、疲惫」；没有词条返回「健康」。
    public static string BriefLine(NpcData n, bool jp)
    {
        List<HealthTag> tags = Tags(n);
        if (tags.Count == 0) return jp ? "健康" : "健康";
        List<string> parts = new List<string>();
        foreach (HealthTag t in tags) parts.Add(TagName(n, t, jp));
        return string.Join("、", parts);
    }

    // ============ AI 演员：身体状态提示词 ============
    // 放在 NpcAgent 系统提示词的"精神状态"后面。没有任何词条时返回空字符串（不占上下文）。
    public static string ActorBlock(NpcData n, bool jp)
    {
        List<HealthTag> tags = Tags(n);
        if (tags.Count == 0 || n.IsDead) return "";
        StringBuilder sb = new StringBuilder();
        if (jp)
        {
            sb.AppendLine("【今のあなたの体の状態】（あなたの身に実際に起きている事実。台詞に必ずにじませること。数値や「状態」「ステータス」といったゲーム用語は口にしない）");
            foreach (HealthTag t in tags) sb.AppendLine("・" + TagName(n, t, true) + "：" + ActLine(n, t, true));
            sb.Append("【演じ方】一度の返事で自然に出すのは、上から順に一番つらいもの1〜2個まで。残りは口調や間ににじませる。体のことを聞かれたら正直に答える。つらさを大げさに説明せず、短い言葉で感じさせること。");
        }
        else
        {
            sb.AppendLine("【你现在的身体状况】（这是真实发生在你身上的事，说话时必须带出来。不要报数值，不要说「状态」「词条」这类游戏用语）");
            foreach (HealthTag t in tags) sb.AppendLine("・" + TagName(n, t, false) + "：" + ActLine(n, t, false));
            sb.Append("【表演要求】每次回复最多自然带出一到两种最难受的感受（按上面从上到下的顺序挑），其余的放进语气和停顿里。被问到身体时要如实说。不要长篇解释自己多难受，用短短的话让人感觉到。");
        }
        return sb.ToString();
    }

    static string ActLine(NpcData n, HealthTag t, bool jp)
    {
        string part = InjuryPart(n, jp);
        string sick = Detail(n, HealthTag.Sick, jp) ?? (jp ? "体調不良" : "不舒服");
        switch (t)
        {
            case HealthTag.Dying:
                return jp ? "あなたはもう長くない。声はかすれて途切れ途切れ、数語しか話せない。助けを求めるか、ぼんやりと誰かを呼ぶ。例：「……さむい……だれか……」"
                          : "你快不行了，声音又轻又断，只能挤出几个字，会求救或迷迷糊糊地叫人。例：「……好冷……谁来……」";
            case HealthTag.CriticalWound:
                return jp ? "あなたの" + part + "はひどい傷で、血が止まらない。痛みでまともに話せず、動くことも働くこともできない。例：「" + part + "が……頼む、手を貸して……」"
                          : "你的" + part + "伤得很重，血止不住，疼得说不出完整的话，动不了也干不了活。例：「我的" + part + "……帮帮我……」";
            case HealthTag.Injured:
                return jp ? "あなたは" + part + "を怪我している。動くたびに痛み、足取りも重く、力仕事はできない。話すとき痛みに息をのむ。例：「っ……" + part + "が痛む。ちょっと待ってくれ」"
                          : "你的" + part + "受了伤，一动就疼，走得很慢，干不了活。说话时会倒吸凉气、提到" + part + "疼。例：「嘶……" + part + "疼得厉害，让我缓缓。」";
            case HealthTag.Psychosis:
                return jp ? "あなたは拠点の誰も信じられない。みんなが（AIも含めて）自分を陥れようとしている気がして、落ち着かず、じっとしていられない。疑い深く、敵意のある話し方になる。例：「近寄るな！みんなで俺をはめる気だろう」"
                          : "你觉得营地里每个人（包括AI）都在算计你、想害你，谁也不信。坐立不安，总想走来走去。说话多疑、带敌意。例：「别靠近我！你们都想害我，我知道的。」";
            case HealthTag.Sick:
                return jp ? "あなたは病気だ（" + sick + "）。体がだるく、頭がぼうっとして、気分が悪い。例：「ごほっ……具合が悪い、力が入らないんだ」"
                          : "你病了（" + sick + "），浑身发虚、头昏脑胀，难受得提不起精神。例：「咳咳……我好像病了，浑身没劲。」";
            case HealthTag.Parched:
                return jp ? "腹も減り、喉も渇いて、もう限界に近い。いらいらして誰にでも当たり、頭もぼんやりしてきている。例：「ごちゃごちゃ言うな……水でも食い物でも、何でもいいから」"
                          : "又饿又渴，快撑不住了。烦躁易怒，对谁都没耐心，脑子也开始发昏。例：「别跟我废话……水、吃的，随便什么都行！」";
            case HealthTag.VeryThirsty:
                return jp ? "喉が焼けるように渇いている。声はかすれ、頭の中は水のことでいっぱい。例：「水……水をくれ……」"
                          : "你渴得受不了，嗓子像冒烟，声音沙哑，满脑子都是水。例：「水……先给我口水……」";
            case HealthTag.Hungry:
                return jp ? "あなたはお腹が空いている。胃が空っぽで力が出ず、他人にも少し冷たくなる。例：「何か食べる物はないか？腹が減って仕方ない」"
                          : "你饿了，胃里空落落的，没什么力气，对别人也没什么好脸色。例：「有吃的吗？饿得心慌。」";
            case HealthTag.Thirsty:
                return jp ? "喉が渇いて、唇が乾いている。ときどき水のことを口にする。例：「……その前に、水を一口もらえないか」"
                          : "你口渴，嘴唇发干，会时不时提到想喝水。例：「……先让我喝口水。」";
            case HealthTag.Fatigued:
                return jp ? "あなたはくたくたに疲れている。まぶたが重く、座って休みたいばかりで、働く気力がわかない。例：「少し休ませてくれ……もう足が動かない」"
                          : "你累坏了，眼皮打架，只想坐下歇着，干活提不起劲。例：「让我歇会儿……实在走不动了。」";
        }
        return "";
    }
}
