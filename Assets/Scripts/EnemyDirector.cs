using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using NavMeshPlus.Components;

// ============================================================================
// 敌人总管（纯规则，不走 LLM）。WorldManager.Start 里 Ensure()，所有数值都在 Inspector 上。
//
// 【敌人从哪来】
//   1. 游荡的（地图上一直有）：每 wanderCheckInterval 秒看一眼，少了就在迷雾里、离营地远的地方补。
//      数量随天数 / 异常度慢慢变多；种类随天数解锁（残影 → 拾荒者 → 潜猎者 → 很少见的畸变体）。
//      安全期（前 safeDays 天）它们不靠近营地；过了安全期会一点点往营地这边挪。
//   2. AI 导演的事件：导演从事件列表里选了 creature_incursion（造物来袭）/ raid（掠夺者来袭）→ EventExecutor 调这里；
//      以前"异常度高时有东西靠近了"那一下精神冲击，过了安全期也变成真的造物冲营地（看见它才掉理智）。
//      安全期内导演不会拿到这两个选项；两次袭击至少隔 raidCooldown 秒。
//
// 【还管什么】给每个 NPC 挂上 NpcCombat（打 / 躲）；造物的"看见就掉理智"；营地附近有敌人时安全感下降；
//   敌人倒下 / 逃走 / 抢东西 / 破墙 / 杀人的日志；墙被拆后重新烘焙导航网格；枪口的弹道线。
//
// 【以后接 AI】Describe() = 眼下的威胁（给领导 / 导演的提示词用）；NpcCombat.SetStance = 让领导下"打 / 躲"。
// 【测试】debugKeys 开着时：F6 刷一只游荡的造物（在附近），F7 造物来袭，F8 掠夺者来袭（无视安全期和冷却）。
// ============================================================================
public class EnemyDirector : MonoBehaviour
{
    public static EnemyDirector Instance { get; private set; }

    [Header("总开关")]
    public bool enemiesEnabled = true;

    [Header("安全期（前几天：游荡的敌人不靠近营地，导演不发动袭击）")]
    public int safeDays = 10;

    [Header("游荡的敌人（地图上一直有）")]
    public float wanderCheckInterval = 12f;
    public int maxWanderers = 6;
    public bool autoSpawn = true;          // 自动刷游荡的敌人（战斗测试场景关掉，只用自己放的）
    [Tooltip("离营地至少多远才刷（还要尽量在迷雾里，玩家看不见的地方）")]
    public float spawnMinDistance = 14f;
    public float spawnMaxDistance = 30f;
    [Tooltip("撤走的敌人离营地这么远、又没人看得见 → 消失")]
    public float leaveDistance = 26f;

    [Header("袭击")]
    public int maxEnemies = 14;
    [Tooltip("两次袭击（造物来袭 / 掠夺者）至少隔多少秒")]
    public float raidCooldown = 45f;

    [Header("战斗")]
    [Tooltip("NPC 身边多远有敌人就进入战斗（打或躲）")]
    public float npcAlertRange = 4.5f;
    [Tooltip("防护服减伤：敌人的伤害 × 这个数")]
    [Range(0.3f, 1f)] public float suitArmor = 0.85f;
    [Tooltip("一场仗里累计掉这么多血 → 「受伤」")]
    public float injureAfterDamage = 18f;
    public float wallHp = 120f;
    public float structureHp = 80f;

    [Header("测试")]
    [Tooltip("F6 刷一只游荡的造物 · F7 造物来袭 · F8 掠夺者来袭（无视安全期和冷却）")]
    public bool debugKeys = true;

    float _wanderTimer = 6f, _auraTimer, _scanTimer, _dangerTimer, _lastRaidAt = -9999f, _fledLogAt = -99f;
    bool _bakePending;
    readonly List<KeyValuePair<GameObject, float>> _fx = new List<KeyValuePair<GameObject, float>>();
    static Sprite _white;

    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;

    public static EnemyDirector Ensure()
    {
        if (Instance != null) return Instance;
        EnemyDirector d = FindFirstObjectByType<EnemyDirector>();
        if (d != null) return d;
        return new GameObject("EnemyDirector").AddComponent<EnemyDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        AbaForce.Ensure();   // ABA 清除小组（F10 测试；战斗测试场景也会有）
        NpcTactics.Ensure(); // 幸存者的小队战术（团队大脑）
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static bool IsSafePeriod
    {
        get
        {
            WorldState w = World;
            return Instance != null && w != null && w.day <= Instance.safeDays;
        }
    }

    public static float AlertRange => Instance != null ? Instance.npcAlertRange : 4.5f;
    public static float LeaveDistance => Instance != null ? Instance.leaveDistance : 26f;

    // 眼下有没有威胁（营地附近 / 看得见的活着的敌人）。对话里的"迎战 / 快躲"只在有威胁时才当成命令。
    public static bool AnyThreat
    {
        get
        {
            Vector3 camp = NpcNav.CampCenter(Vector3.zero);
            foreach (Enemy e in Enemy.All)
                if (e != null && e.Alive && (e.Visible || Vector2.Distance(e.transform.position, camp) < NpcNav.CampRadius + 10f)) return true;
            foreach (AbaOperative o in AbaOperative.All)   // ABA 清除小组也算威胁
                if (o != null && o.Alive && (o.Visible || Vector2.Distance(o.transform.position, camp) < NpcNav.CampRadius + 10f)) return true;
            return false;
        }
    }

    // ============ 每帧 ============

    void Update()
    {
        float dt = Time.deltaTime;
        UpdateFx();
        if (!enemiesEnabled || OpeningDirector.Playing || dt <= 0f) return;
        WorldState w = World;
        if (w == null) return;

        _scanTimer -= dt;
        if (_scanTimer <= 0f) { _scanTimer = 1f; AttachCombat(); }
        _wanderTimer -= dt;
        if (_wanderTimer <= 0f) { _wanderTimer = wanderCheckInterval; if (autoSpawn) TopUpWanderers(w); }
        _auraTimer -= dt;
        if (_auraTimer <= 0f) { _auraTimer = 0.5f; ApplyAuras(w, 0.5f); }
        if (debugKeys) DebugKeys();
    }

    // 每个 NPC 身上挂一个 NpcCombat（后生成的 NPC 也会挂上）
    void AttachCombat()
    {
        foreach (NpcView v in NpcView.GetAllViews())
            if (v != null && v.GetComponent<NpcCombat>() == null) v.gameObject.AddComponent<NpcCombat>();
    }

    // ============ 游荡的敌人 ============

    void TopUpWanderers(WorldState w)
    {
        int wanderers = 0;
        foreach (Enemy e in Enemy.All) if (e != null && e.Alive && e.goal == Enemy.Goal.Wander) wanderers++;
        int target = Mathf.Clamp(1 + w.day / 4 + Mathf.RoundToInt(w.anomalyLevel / 30f), 1, maxWanderers);
        if (wanderers >= target || Enemy.All.Count >= maxEnemies) return;

        float r = Random.value;
        EnemyDef def;
        if (w.day > safeDays + 5 && r < 0.08f && Enemy.CountAlive("aberration") == 0) def = EnemyDefs.Get("aberration");
        else if (w.day >= 3 && r < 0.3f) def = EnemyDefs.Get("scavenger");
        else if (w.day >= 5 && r < 0.6f) def = EnemyDefs.Get("stalker");
        else def = EnemyDefs.Get("shade");
        if (def == null) return;
        int n = Mathf.Min(Random.Range(def.packMin, def.packMax + 1), Mathf.Max(1, maxEnemies - Enemy.All.Count));
        if (!FindSpawnPoint(spawnMinDistance, spawnMaxDistance, null, true, out Vector3 p)) return;
        SpawnGroup(def, n, Enemy.Goal.Wander, p);
    }

    void SpawnGroup(EnemyDef def, int count, Enemy.Goal goal, Vector3 at)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 off = i == 0 ? Vector2.zero : Random.insideUnitCircle * 1.3f;
            Vector3 pos = at + new Vector3(off.x, off.y, 0f);
            Enemy.Spawn(def, pos, goal, at);
        }
    }

    // 在营地周围 [minD, maxD] 的环里找个刷怪点：尽量在迷雾里、离 NPC 远、和营地连得通。side = 从哪一边来（null = 随便）。
    bool FindSpawnPoint(float minD, float maxD, string side, bool preferUnseen, out Vector3 point)
    {
        Vector3 camp = NpcNav.CampCenter(Vector3.zero);
        for (int i = 0; i < 48; i++)
        {
            float ang = side != null ? SideAngle(side) + Random.Range(-40f, 40f) : Random.Range(0f, 360f);
            float rad = ang * Mathf.Deg2Rad;
            float r = Random.Range(minD, maxD) * (i < 32 ? 1f : 0.7f);   // 找不到就放近一点
            Vector3 c = camp + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * r;
            if (!NpcNav.InGrid(c)) continue;
            if (!NpcNav.Sample(c, 1.5f, out Vector3 s) || !NpcNav.InGrid(s)) continue;
            if (preferUnseen && i < 32 && !NpcNav.IsUnseen(s)) continue;
            if (NearAnyNpc(s, 7f)) continue;
            if (i < 24 && !NpcNav.Reachable(s, OutsideCampPoint(camp, s))) continue;   // 别刷在走不出来的地方
            point = s;
            return true;
        }
        point = camp;
        return false;
    }

    // 营地外面、朝着 from 那一侧的一个点（营地被墙围死时也能用来判断连通）
    static Vector3 OutsideCampPoint(Vector3 camp, Vector3 from)
    {
        Vector3 dir = from - camp;
        dir.z = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.right;
        Vector3 p = camp + dir.normalized * (NpcNav.CampRadius + 2.5f);
        return NpcNav.Sample(p, 2f, out Vector3 s) ? s : p;
    }

    static float SideAngle(string side)
    {
        switch (side)
        {
            case "north": return 90f;
            case "south": return 270f;
            case "west": return 180f;
            default: return 0f;
        }
    }

    static string RandomSide()
    {
        string[] sides = { "north", "south", "east", "west" };
        if (CampZone.Instance != null && CampZone.Instance.Ready && Random.value < 0.5f) return CampZone.Instance.MostOpenSide();   // 一半的时候挑最敞开的那边
        return sides[Random.Range(0, sides.Length)];
    }

    static bool NearAnyNpc(Vector3 p, float r)
    {
        foreach (NpcView v in NpcView.GetAllViews())
            if (v != null && Vector2.Distance(v.transform.position, p) < r) return true;
        return false;
    }

    // ============ 袭击（AI 导演的事件 / 异常度触发 / 测试键）============

    // 造物来袭。severity 0~1（越大越多、越可能带畸变体）。record = 自己记进事件编年史（从 EventExecutor 来的由它记）。
    // 返回 false = 没发动（安全期 / 冷却中 / 关着 / 找不到刷怪点）。
    public bool TriggerIncursion(float severity, string text, bool record, bool force = false)
    {
        WorldState w = World;
        if (w == null || (!enemiesEnabled && !force)) return false;
        if (!force && (IsSafePeriod || Time.time - _lastRaidAt < raidCooldown || Enemy.All.Count >= maxEnemies)) return false;
        severity = Mathf.Clamp01(severity);
        string side = RandomSide();
        if (!FindSpawnPoint(16f, 24f, side, true, out Vector3 p)) return false;
        _lastRaidAt = Time.time;

        int weak = 2 + Mathf.RoundToInt(severity * 2f);
        bool big = severity >= 0.45f || w.day >= safeDays + 8 || force;
        for (int i = 0; i < weak; i++)
        {
            EnemyDef d = EnemyDefs.Get(w.day >= 5 && Random.value < 0.6f ? "stalker" : "shade");
            Vector2 off = Random.insideUnitCircle * 1.5f;
            Enemy.Spawn(d, p + new Vector3(off.x, off.y, 0f), Enemy.Goal.Assault, p);
        }
        if (big) Enemy.Spawn(EnemyDefs.Get("aberration"), p, Enemy.Goal.Assault, p);

        bool jp = LanguageGuard.IsJp;
        string sideName = CampZone.SideName(side, jp);
        AddEvent(w, LF(big ? "enemy_event_incursion_big" : "enemy_event_incursion", sideName));
        Toast("threat : creature", L("event_banner_creature_incursion"), LF("enemy_toast_from", sideName));
        if (record)
        {
            if (ScreenEffectManager.Instance != null) ScreenEffectManager.Instance.PlayEventEffect("creature_incursion");
            EventChronicle.Record("creature", text, null);
        }
        NpcSecurity.NotifyDanger(10f + 10f * severity);
        return true;
    }

    // 掠夺者来袭（人类）：冲营地中心抢仓库，抢到就跑；受伤也会跑。
    public bool TriggerRaid(float severity, string text, bool record, bool force = false)
    {
        WorldState w = World;
        if (w == null || (!enemiesEnabled && !force)) return false;
        if (!force && (IsSafePeriod || Time.time - _lastRaidAt < raidCooldown || Enemy.All.Count >= maxEnemies)) return false;
        severity = Mathf.Clamp01(severity);
        string side = RandomSide();
        if (!FindSpawnPoint(16f, 24f, side, true, out Vector3 p)) return false;
        _lastRaidAt = Time.time;

        int count = Mathf.Clamp(2 + Mathf.RoundToInt(severity * 2f), 2, 4);
        EnemyDef scav = EnemyDefs.Get("scavenger");
        for (int i = 0; i < count; i++)
        {
            Vector2 off = Random.insideUnitCircle * 1.5f;
            Enemy.Spawn(scav, p + new Vector3(off.x, off.y, 0f), Enemy.Goal.Raid, p);
        }
        bool armed = severity >= 0.5f || w.day >= 15 || force;
        if (armed) Enemy.Spawn(EnemyDefs.Get("raider"), p, Enemy.Goal.Raid, p);

        bool jp = LanguageGuard.IsJp;
        string sideName = CampZone.SideName(side, jp);
        AddEvent(w, LF("enemy_event_raid", armed ? EnemyDefs.Get("raider").Name : scav.Name, sideName));
        Toast("threat : raid", L("event_banner_raid"), LF("enemy_toast_from", sideName));
        if (record)
        {
            if (ScreenEffectManager.Instance != null) ScreenEffectManager.Instance.PlayEventEffect("raid");
            EventChronicle.Record("raid", text, null);
        }
        NpcSecurity.NotifyDanger(8f + 8f * severity);
        return true;
    }

    // 给 AI 导演的提示词：过了安全期才多给两种事件可选
    public static string DirectorTypeLines(bool jp)
    {
        if (Instance == null || !Instance.enemiesEnabled || IsSafePeriod) return "";
        return jp ? "  creature_incursion（造物の襲来：名状しがたいものの群れが拠点へ向かってくる）\n  raid（略奪者の襲撃：別の生存者の一団が物資を奪いに来る）\n"
                  : "  creature_incursion（造物来袭：一群无法名状的东西朝营地扑来）\n  raid（掠夺者来袭：另一伙幸存者来抢物资）\n";
    }

    // ============ 造物的"看见就掉理智"、营地附近有敌人 ============

    void ApplyAuras(WorldState w, float dt)
    {
        Vector3 camp = NpcNav.CampCenter(Vector3.zero);
        bool nearCamp = false;
        foreach (Enemy e in Enemy.All)
        {
            if (e == null || !e.Alive) continue;
            if (Vector2.Distance(e.transform.position, camp) < NpcNav.CampRadius + 6f) nearCamp = true;
            if (e.Def.sanityAura <= 0f || !e.Visible) continue;   // 看得见才会被吓到
            foreach (NpcData n in w.npcs)
            {
                if (n == null || n.IsDead || n.isBroken) continue;
                NpcMover m = NpcView.GetMover(n.id);
                if (m == null || Vector2.Distance(m.transform.position, e.transform.position) > e.Def.sanityAura) continue;
                float k = Mathf.Clamp(1f - n.personality.bravery * 0.004f, 0.5f, 1f);   // 勇敢的人掉得少
                n.sanity = Mathf.Clamp(n.sanity - e.Def.sanityPerSecond * k * dt, 0f, 100f);
                n.isHallucinating = n.sanity < WorldRules.HallucinationThreshold;
            }
        }
        _dangerTimer -= dt;
        if (nearCamp && _dangerTimer <= 0f)
        {
            _dangerTimer = 3f;
            NpcSecurity.NotifyDanger(4f);   // 营地附近有东西：大家心里不踏实（安全感↓ → 更想修墙）
        }
    }

    // ============ 敌人那边的通知 ============

    public static void OnEnemySpotted(Enemy e)
    {
        if (e == null || Instance == null) return;
        if (e.Def.IsStrong && World != null) AddEvent(World, LF("enemy_event_spotted_strong", e.Name));
    }

    public static void OnEnemyKilled(Enemy e, NpcData by)
    {
        WorldState w = World;
        if (e == null || w == null) return;
        if (by != null && !by.IsDead)
        {
            string text = LF("combat_log_kill", by.npcName, e.Name);
            by.memories.Add(new MemoryEntry { day = w.day, content = text });
            if (LogManager.Instance != null) LogManager.Instance.AddLog(by.npcName, text, by.id);
        }
        if (e.Def.IsStrong) AddEvent(w, LF("enemy_event_strong_down", e.Name));
        if (e.KilledByAba) return;   // ABA 打死的：东西不会进我们的仓库
        if (!string.IsNullOrEmpty(e.Def.dropItem) && Random.value < e.Def.dropChance && WorldManager.Instance != null)
        {
            WorldManager.Instance.AddToStorage(e.Def.dropItem, 1);
            AddEvent(w, LF("enemy_event_drop", e.Name, ItemName(e.Def.dropItem)));
        }
    }

    public static void OnEnemyFled(Enemy e)
    {
        WorldState w = World;
        if (e == null || w == null || Instance == null || !e.Visible) return;
        if (Time.time - Instance._fledLogAt < 3f) return;   // 一伙人一起跑，只记一条
        Instance._fledLogAt = Time.time;
        AddEvent(w, LF("enemy_event_fled", e.Name));
    }

    public static void OnNpcKilled(NpcData n, Enemy by, string byName = null)
    {
        WorldState w = World;
        if (n == null || w == null) return;
        AddEvent(w, LF("enemy_event_npc_killed", n.npcName, by != null ? by.Name : (byName ?? "?")));
    }

    public static void OnStructureBroken(Vector3 pos, string structName, bool wall, Enemy by)
    {
        WorldState w = World;
        if (w == null) return;
        string who = by != null ? by.Name : "?";
        if (wall) AddEvent(w, LF("enemy_event_wall_broken", who, CampZone.SideName(SideOf(pos), LanguageGuard.IsJp)));
        else AddEvent(w, LF("enemy_event_structure_broken", who, structName));
        NpcSecurity.NotifyDanger(8f);
    }

    // 抢仓库：随机拿走几份（每样 1 份）
    public static void Loot(Enemy e, int count)
    {
        WorldState w = World;
        if (e == null || w == null || WorldManager.Instance == null || count <= 0) return;
        List<string> taken = new List<string>();
        for (int i = 0; i < count; i++)
        {
            List<ItemStack> pool = new List<ItemStack>();
            foreach (ItemStack s in w.sharedStorage) if (s != null && s.count > 0) pool.Add(s);
            if (pool.Count == 0) break;
            ItemStack pick = pool[Random.Range(0, pool.Count)];
            if (WorldManager.Instance.ConsumeFromStorage(pick.itemName, 1)) taken.Add(ItemName(pick.itemName));
        }
        if (taken.Count == 0)
        {
            if (e.Visible) NpcSpeechBubble.Show(e.transform, LanguageGuard.Pick("……什么都没有。", "……何もない。"), 2f);
            return;
        }
        AddEvent(w, LF("enemy_event_looted", e.Name, string.Join(LanguageGuard.Pick("、", "、"), taken)));
        NpcSecurity.NotifyDanger(6f);
    }

    // ============ 墙没了：重新烘焙导航网格 ============

    public void RequestNavRebake()
    {
        if (_bakePending || !isActiveAndEnabled) return;
        StartCoroutine(RebakeSoon());
    }

    IEnumerator RebakeSoon()
    {
        _bakePending = true;
        yield return new WaitForSeconds(0.2f);   // 等墙真的销毁了再烘焙；同时拆好几段只烘焙一次
        _bakePending = false;
        NavMeshSurface s = BuildManager.Instance != null && BuildManager.Instance.navMeshSurface != null
            ? BuildManager.Instance.navMeshSurface : FindFirstObjectByType<NavMeshSurface>();
        if (s == null) { Debug.LogWarning("[EnemyDirector] 场景里没有 NavMeshSurface：墙拆了但导航网格没更新。"); yield break; }
        Physics2D.SyncTransforms();
        // 在原来的网格上更新（和 TerrainSystem 一样，不先清空，NPC 不会掉出导航网格）
        if (s.navMeshData != null) s.UpdateNavMesh(s.navMeshData);
        else s.BuildNavMesh();
    }

    // ============ 给 AI 用（以后接）============

    // 眼下的威胁，一两句话（给领导 / 导演的提示词）。只算看得见的、或者已经到营地附近的。
    public static string Describe(bool jp)
    {
        Vector3 camp = NpcNav.CampCenter(Vector3.zero);
        Dictionary<string, int> count = new Dictionary<string, int>();
        Dictionary<string, string> where = new Dictionary<string, string>();
        foreach (Enemy e in Enemy.All)
        {
            if (e == null || !e.Alive) continue;
            float d = Vector2.Distance(e.transform.position, camp);
            if (!e.Visible && d > NpcNav.CampRadius + 10f) continue;
            string name = e.Name;
            count.TryGetValue(name, out int k);
            count[name] = k + 1;
            if (!where.ContainsKey(name))
            {
                string state = e.state == Enemy.State.BreakWall ? (jp ? "壁を壊している" : "正在拆墙")
                             : Enemy.NearCamp(e.transform.position, 0f) ? (jp ? "拠点の中" : "已经在营地里")
                             : (jp ? "拠点の" + CampZone.SideName(SideOf(e.transform.position), true) + "、約" + Mathf.RoundToInt(d) + "マス"
                                   : "营地" + CampZone.SideName(SideOf(e.transform.position), false) + "，约 " + Mathf.RoundToInt(d) + " 格");
                where[name] = state;
            }
        }
        if (count.Count == 0) return jp ? "近くに敵の姿はない。" : "附近没有发现敌人。";
        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, int> kv in count) parts.Add(kv.Key + "×" + kv.Value + (jp ? "（" : "（") + where[kv.Key] + "）");
        return (jp ? "敵：" : "敌人：") + string.Join(jp ? "、" : "，", parts);
    }

    // ============ 小工具 ============

    static string SideOf(Vector3 p)
    {
        Vector3 d = p - NpcNav.CampCenter(p);
        if (Mathf.Abs(d.x) > Mathf.Abs(d.y)) return d.x >= 0f ? "east" : "west";
        return d.y >= 0f ? "north" : "south";
    }

    static void AddEvent(WorldState w, string text)
    {
        if (w == null || string.IsNullOrEmpty(text)) return;
        if (EventLogManager.Instance != null) EventLogManager.Instance.AddEvent(w.day, text);
        w.eventLog.Add(new MemoryEntry { day = w.day, content = text });
    }

    static void Toast(string tag, string title, string sub)
    {
        if (HudToasts.Instance != null) HudToasts.Instance.Show(tag, title, sub);
    }

    static string ItemName(string id)
    {
        return ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(id) : id;
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] a) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, a) : key;

    // 白色方块（血条、弹道线用）
    public static Sprite WhiteSprite
    {
        get
        {
            if (_white != null) return _white;
            Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
            t.Apply();
            _white = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);   // 1 × 1 单位
            return _white;
        }
    }

    public static Transform Quad(string name, Transform parent, Color c, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = WhiteSprite;
        sr.color = c;
        sr.sortingOrder = order;
        RenderMats.MakeUnlit(sr);                                   // 2D 光照：血条 / 弹道线一直看得清
        return go.transform;
    }

    // 一闪而过的弹道线（开枪时）
    public static void Tracer(Vector3 from, Vector3 to, Color c)
    {
        if (Instance == null) return;
        if (NpcNav.IsUnseen(from) && NpcNav.IsUnseen(to)) return;   // 迷雾里开枪看不见
        Vector3 a = from + new Vector3(0f, 0.1f, 0f), b = to + new Vector3(0f, 0.1f, 0f);
        Vector3 d = b - a;
        d.z = 0f;
        float len = d.magnitude;
        if (len < 0.05f) return;
        Transform t = Quad("Tracer", null, c, 8);
        t.position = (a + b) * 0.5f;
        t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        t.localScale = new Vector3(len, 0.035f, 1f);
        Instance._fx.Add(new KeyValuePair<GameObject, float>(t.gameObject, Time.time + 0.09f));
    }

    void UpdateFx()
    {
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            if (_fx[i].Key != null && Time.time < _fx[i].Value) continue;
            if (_fx[i].Key != null) Destroy(_fx[i].Key);
            _fx.RemoveAt(i);
        }
    }

    // ============ 测试键 ============

    void DebugKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || ChatManager.InputFocused) return;
        if (kb.f6Key.wasPressedThisFrame)
        {
            if (FindSpawnPoint(8f, 12f, null, false, out Vector3 p)) SpawnGroup(EnemyDefs.Get("shade"), 1, Enemy.Goal.Wander, p);
        }
        if (kb.f7Key.wasPressedThisFrame) TriggerIncursion(0.7f, null, true, true);
        if (kb.f8Key.wasPressedThisFrame) TriggerRaid(0.6f, null, true, true);
    }
}
