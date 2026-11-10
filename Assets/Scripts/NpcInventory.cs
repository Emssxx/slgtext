using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// NPC 物品栏（纯规则，不走 LLM）。数据就是 NpcData.inventory（物品 id + 数量），这里管规则和"自己去拿 / 放回"。
//
// 【格子】每人 Slots 格，一格放一种东西；同一种最多带几个看 StackLimit（手枪 / 斧头 / 锤子 / 工具包 1，医疗包 / 水瓶 2，弹匣 3）。
// 【挂件】NpcRig 只显示物品栏里有的那几件（锤子 / 斧头 / 枪套+手枪 / 医疗包 / 工具包 / 水瓶）；弹匣没有挂件图。
//         任何人的物品栏一变 Revision 就 +1，NpcRig 和界面看到变了就刷新。
// 【手枪】身上的弹匣是备用的；枪里的子弹记在 NpcData.pistolAmmo。打空了换一个弹匣（装满 MagSize 发）。
//         没子弹也没弹匣 → 这把枪用不了，打架时换成斧头 / 锤子 / 空手（NpcCombat.WeaponOf）。
//         换弹分两种（NpcCombat 选）：战术换弹 = 换下来的旧弹匣收回物品栏，变成"空弹匣"（占一格，最多 3 个；在营地里用不上会自己放回仓库）；
//         快速换弹 = 旧弹匣直接丢在地上（没了）。
// 【消耗】医疗包：包扎时用掉（受伤 / 生病又带着医疗包，会自己包扎）；
//         水瓶：在营地外口渴只能喝自己带的水瓶（喝一瓶少一瓶，WaterWorks.TryDrink），营地里照旧喝仓库的净化水。
// 【和仓库拿放】
//   · 玩家说"去仓库拿手枪" / "把斧头放回仓库" / "把东西都放回去" → NpcCommands 排任务，NpcTaskRunner 走到仓库执行。
//     玩家叫他拿的东西他会一直带着（不会自己放回），直到玩家叫他放回。
//   · 自己拿（autoLoadout）：人在营地里时，按今天的分工带上要用的东西，用不上的过一会儿放回去：
//       施工 → 锤子、工具包；采集 → 砍树带斧头 / 采石挖矿割草带锤子 + 水瓶；探寻 → 水瓶、医疗包、斧头；医护 → 医疗包；
//       过了安全期（或附近有敌人）胆子大的（勇敢 ≥ 60）带手枪 + 2 个弹匣。
//     仓库没有水瓶时，用 1 份净化水灌一瓶（仓库净化水少于 3 份就不灌，留给营地喝）。
//   · 死了：死在营地里，身上的东西收回仓库；死在外面，东西就丢在外面了。
// ============================================================================
public class NpcInventory : MonoBehaviour
{
    public static NpcInventory Instance { get; private set; }

    public const int Slots = 6;      // 物品栏格数
    public const int MagSize = 8;    // 一个弹匣几发
    public const string EmptyMag = "magazine_empty";   // 空弹匣（战术换弹收回来的）

    // 任何人的物品栏 / 子弹变了就 +1（NpcRig、对话窗、悬停框据此刷新）
    public static int Revision { get; private set; }

    [Header("自己去仓库拿 / 放回")]
    public bool autoLoadout = true;
    [Tooltip("每隔几秒看一次每个人缺什么")]
    public float checkInterval = 2f;
    [Tooltip("用不上的东西带着超过几秒就放回仓库（玩家叫他拿的除外）")]
    public float returnAfter = 30f;
    [Tooltip("仓库净化水少于几份就不灌水瓶（留给营地里喝）")]
    public int keepWaterForCamp = 3;
    [Tooltip("过了安全期（或附近有敌人）勇敢到多少的人会去带手枪")]
    public float pistolBravery = 60f;

    float _timer;
    static readonly Dictionary<string, HashSet<string>> _kept = new Dictionary<string, HashSet<string>>();        // 玩家叫他拿的
    static readonly Dictionary<string, Dictionary<string, float>> _idleSince = new Dictionary<string, Dictionary<string, float>>(); // 用不上从什么时候开始
    static readonly Dictionary<string, float> _bandageAt = new Dictionary<string, float>();
    readonly Dictionary<string, int> _want = new Dictionary<string, int>();

    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;

    public static NpcInventory Ensure()
    {
        if (Instance != null) return Instance;
        NpcInventory found = FindFirstObjectByType<NpcInventory>();
        if (found != null) return found;
        return new GameObject("NpcInventory").AddComponent<NpcInventory>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ============ 查 ============

    public static int Count(NpcData n, string id)
    {
        if (n == null || n.inventory == null || string.IsNullOrEmpty(id)) return 0;
        int c = 0;
        foreach (ItemStack s in n.inventory) if (s != null && s.itemName == id) c += s.count;
        return c;
    }

    public static bool Has(NpcData n, string id) => Count(n, id) > 0;

    public static int UsedSlots(NpcData n)
    {
        if (n == null || n.inventory == null) return 0;
        int c = 0;
        foreach (ItemStack s in n.inventory) if (s != null && s.count > 0) c++;
        return c;
    }

    // 同一种东西最多带几个
    public static int StackLimit(string id)
    {
        switch (id)
        {
            case "pistol": case "axe": case "hammer": case "toolkit": return 1;
            case "medkit": case "bottle": return 2;
            case "magazine": return 3;
            case EmptyMag: return 3;                        // 空弹匣也最多带 3 个
            default: return 3;
        }
    }

    // 还能不能再带一个。不能时 why = 原因（已翻译）
    public static bool CanAdd(NpcData n, string id, out string why)
    {
        why = null;
        if (n == null || string.IsNullOrEmpty(id)) { why = "?"; return false; }
        int have = Count(n, id);
        if (have >= StackLimit(id)) { why = LF("inv_fail_stack", ItemName(id), StackLimit(id)); return false; }
        if (have == 0 && UsedSlots(n) >= Slots) { why = LF("inv_fail_full", Slots); return false; }
        return true;
    }

    // 仓库能不能给出一个（水瓶：没有现成的，可以用净化水灌）
    public static bool StorageCanSupply(string id)
    {
        WorldState w = World;
        if (w == null) return false;
        if (ItemUseRules.GetCount(id, w) > 0) return true;
        if (id == "bottle") return ItemUseRules.GetCount(WaterWorks.WaterItem, w) > 0;
        return false;
    }

    // ============ 改 ============

    public static bool Add(NpcData n, string id, int count = 1)
    {
        if (n == null || string.IsNullOrEmpty(id) || count <= 0) return false;
        if (n.inventory == null) n.inventory = new List<ItemStack>();
        ItemStack stack = null;
        foreach (ItemStack s in n.inventory) if (s != null && s.itemName == id) { stack = s; break; }
        if (stack == null) n.inventory.Add(new ItemStack { itemName = id, count = count });
        else stack.count += count;
        Revision++;
        return true;
    }

    public static bool Remove(NpcData n, string id, int count = 1)
    {
        if (n == null || n.inventory == null || count <= 0) return false;
        for (int i = 0; i < n.inventory.Count; i++)
        {
            ItemStack s = n.inventory[i];
            if (s == null || s.itemName != id || s.count <= 0) continue;
            s.count -= count;
            if (s.count <= 0)
            {
                n.inventory.RemoveAt(i);
                if (_kept.TryGetValue(n.id, out HashSet<string> k)) k.Remove(id);   // 用完了：不再算"玩家叫他拿的"
                if (id == "pistol") n.pistolAmmo = 0;                                 // 枪没了，枪里的子弹也跟着走
            }
            Revision++;
            return true;
        }
        return false;
    }

    // 从仓库拿一个带在身上。byPlayer = 玩家叫他拿的（之后不会自己放回）。成功返回 true，不成功 why = 原因。
    public static bool TakeFromStorage(NpcData n, string id, bool byPlayer, out string why)
    {
        why = null;
        WorldManager wm = WorldManager.Instance;
        WorldState w = World;
        if (n == null || wm == null || w == null) { why = "?"; return false; }
        if (!CanAdd(n, id, out why)) return false;
        bool filled = false;
        if (!wm.ConsumeFromStorage(id, 1))
        {
            // 水瓶：没有现成的就用 1 份净化水灌一瓶
            if (id == "bottle" && wm.ConsumeFromStorage(WaterWorks.WaterItem, 1)) filled = true;
            else { why = L("item_fail_empty"); return false; }
        }
        Add(n, id, 1);
        if (byPlayer) KeptSet(n.id).Add(id);
        Log(n, filled ? LF("inv_log_fill", n.npcName) : LF("inv_log_take", n.npcName, ItemName(id)));
        return true;
    }

    // 放回仓库。id = "*" 就是身上所有东西。返回放回了几样。
    public static int PutBack(NpcData n, string id)
    {
        WorldManager wm = WorldManager.Instance;
        if (n == null || n.inventory == null || wm == null) return 0;
        List<string> names = new List<string>();
        for (int i = n.inventory.Count - 1; i >= 0; i--)
        {
            ItemStack s = n.inventory[i];
            if (s == null || s.count <= 0 || (id != "*" && s.itemName != id)) continue;
            wm.AddToStorage(s.itemName, s.count);
            names.Add(s.count > 1 ? ItemName(s.itemName) + "×" + s.count : ItemName(s.itemName));
            if (s.itemName == "pistol")
            {
                if (n.pistolAmmo >= MagSize) wm.AddToStorage("magazine", 1);   // 枪里是满的：当一个弹匣还回去（不满的就散了）
                n.pistolAmmo = 0;
            }
            n.inventory.RemoveAt(i);
            if (_kept.TryGetValue(n.id, out HashSet<string> k)) k.Remove(s.itemName);
        }
        if (names.Count == 0) return 0;
        Revision++;
        Log(n, LF("inv_log_return", n.npcName, string.Join(Sep, names)));
        return names.Count;
    }

    static HashSet<string> KeptSet(string npcId)
    {
        if (!_kept.TryGetValue(npcId, out HashSet<string> k)) { k = new HashSet<string>(); _kept[npcId] = k; }
        return k;
    }

    // ============ 手枪 ============

    // 这把枪现在能不能打（有枪，而且枪里有子弹或者身上有弹匣）
    public static bool PistolReady(NpcData n)
    {
        return Has(n, "pistol") && (n.pistolAmmo > 0 || Has(n, "magazine"));
    }

    // 打空了换弹匣：用掉身上一个弹匣，装满。没有弹匣返回 false。
    public static bool Reload(NpcData n)
    {
        if (n == null || !Has(n, "pistol") || !Remove(n, "magazine", 1)) return false;
        n.pistolAmmo = MagSize;
        Revision++;
        return true;
    }

    // 战术换弹：换下来的旧弹匣（已经打空）收回物品栏，变成一个空弹匣。放不下（格子满了 / 空弹匣带满了）返回 false
    public static bool StowEmptyMag(NpcData n)
    {
        if (!CanAdd(n, EmptyMag, out _)) return false;                    // 放不下
        return Add(n, EmptyMag, 1);                                         // 收进物品栏（Add 里会让界面刷新）
    }

    // 开了一枪
    public static void SpendRound(NpcData n)
    {
        if (n == null || n.pistolAmmo <= 0) return;
        n.pistolAmmo--;
        Revision++;
    }

    // ============ 文字 ============

    // 一行写完身上带着什么："手枪（6/8）、弹匣×2、斧头"；什么都没有 = "空"
    public static string Describe(NpcData n, bool jp)
    {
        if (n == null || n.inventory == null || UsedSlots(n) == 0) return L("inv_empty");
        List<string> parts = new List<string>();
        foreach (ItemStack s in n.inventory)
        {
            if (s == null || s.count <= 0) continue;
            string t = ItemName(s.itemName);
            if (s.itemName == "pistol") t += (jp ? "（" : "（") + n.pistolAmmo + "/" + MagSize + (jp ? "）" : "）");
            else if (s.count > 1) t += "×" + s.count;
            parts.Add(t);
        }
        return string.Join(Sep, parts);
    }

    // 格子里的小字：手枪 = 子弹数，其他 = 数量（1 个不写）
    public static string SlotSub(NpcData n, ItemStack s)
    {
        if (n == null || s == null) return "";
        if (s.itemName == "pistol") return n.pistolAmmo + "/" + MagSize;
        return s.count > 1 ? "×" + s.count : "";
    }

    // ============ 每隔几秒：自己拿 / 放回 / 包扎 / 遗物 ============

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        _timer -= dt;
        if (_timer > 0f) return;
        _timer = checkInterval;
        WorldState w = World;
        if (w == null || w.npcs == null) return;
        foreach (NpcData n in w.npcs.ToArray())
        {
            if (n == null) continue;
            if (n.IsDead) { HandleDead(n, w); continue; }
            if (n.isBroken) continue;
            TryBandage(n);
            if (autoLoadout) Loadout(n, w);
        }
    }

    // 按分工带东西；用不上的放回去。每次最多拿一样，免得一下子刷一堆日志。
    void Loadout(NpcData n, WorldState w)
    {
        Vector3 pos = NpcPos(n, out bool hasBody);
        if (!hasBody || !InCamp(pos)) return;   // 只在营地里（仓库旁边）拿放
        Desired(n, _want);

        // 1. 缺的：拿一样
        foreach (KeyValuePair<string, int> kv in _want)
        {
            if (Count(n, kv.Key) >= kv.Value) continue;
            if (!StorageCanSupply(kv.Key) || !CanAdd(n, kv.Key, out _)) continue;
            if (kv.Key == "bottle" && ItemUseRules.GetCount("bottle", w) <= 0 && ItemUseRules.GetCount(WaterWorks.WaterItem, w) < keepWaterForCamp) continue;
            if (TakeFromStorage(n, kv.Key, false, out _)) break;
        }

        // 2. 用不上的（不是玩家叫他拿的）：带着超过 returnAfter 秒就放回
        if (n.inventory == null) return;
        if (!_idleSince.TryGetValue(n.id, out Dictionary<string, float> idle)) { idle = new Dictionary<string, float>(); _idleSince[n.id] = idle; }
        _kept.TryGetValue(n.id, out HashSet<string> kept);
        string toReturn = null;
        foreach (ItemStack s in n.inventory)
        {
            if (s == null || s.count <= 0) continue;
            bool needed = _want.ContainsKey(s.itemName);
            if (needed || (kept != null && kept.Contains(s.itemName))) { idle.Remove(s.itemName); continue; }
            if (!idle.TryGetValue(s.itemName, out float since)) { idle[s.itemName] = Time.time; continue; }
            if (Time.time - since >= returnAfter) { toReturn = s.itemName; break; }
        }
        if (toReturn != null)
        {
            idle.Remove(toReturn);
            PutBack(n, toReturn);
        }
    }

    // 今天要带什么（物品 id → 数量）
    void Desired(NpcData n, Dictionary<string, int> want)
    {
        want.Clear();
        switch (n.role)
        {
            case "build":
                want["hammer"] = 1;
                want["toolkit"] = 1;
                break;
            case "gather":
                if (n.roleTarget == ResourceInfo.WoodItem) want["axe"] = 1;
                else if (n.roleTarget == ResourceInfo.StoneItem || n.roleTarget == ResourceInfo.OreItem || n.roleTarget == "fiber") want["hammer"] = 1;
                want["bottle"] = 1;
                break;
            case "scavenge":
                want["bottle"] = 1;
                want["medkit"] = 1;
                want["axe"] = 1;
                break;
            case "care":
                want["medkit"] = 1;
                break;
        }
        // 防身：过了安全期 / 附近有敌人，胆子大的带枪
        bool danger = EnemyDirector.AnyThreat || (EnemyDirector.Instance != null && EnemyDirector.Instance.enemiesEnabled && !EnemyDirector.IsSafePeriod);
        if (danger && n.personality.bravery >= pistolBravery) want["pistol"] = 1;
        if (want.ContainsKey("pistol") || Has(n, "pistol")) want["magazine"] = 2;
    }

    // 受伤 / 生病又带着医疗包：自己包扎（走 NpcTaskRunner 的 use_item，和玩家叫他用是同一套）
    void TryBandage(NpcData n)
    {
        if (!Has(n, "medkit")) return;
        if (!NpcHealth.Has(n, HealthTag.Injured) && !NpcHealth.Has(n, HealthTag.Sick)) return;
        if (NpcCombat.InCombat(n.id) || NpcBusy.IsBusy(n.id) || !NpcHealth.CanMove(n)) return;
        if (_bandageAt.TryGetValue(n.id, out float t) && Time.time - t < 20f) return;
        NpcMover m = NpcView.GetMover(n.id);
        NpcTaskRunner r = m != null ? m.GetComponent<NpcTaskRunner>() : null;
        if (r == null || r.IsBusy) return;
        _bandageAt[n.id] = Time.time;
        r.RunTasks(new List<NpcTask> { new NpcTask { action = "use_item", target = "medkit", destination = "@self" } });
    }

    // 死了：死在营地里东西收回仓库，死在外面就丢了
    void HandleDead(NpcData n, WorldState w)
    {
        if (n.inventory == null || UsedSlots(n) == 0) return;
        Vector3 pos = NpcPos(n, out bool hasBody);
        string list = Describe(n, LanguageGuard.IsJp);
        bool inCamp = !hasBody || InCamp(pos);
        if (inCamp && WorldManager.Instance != null)
            foreach (ItemStack s in n.inventory) if (s != null && s.count > 0) WorldManager.Instance.AddToStorage(s.itemName, s.count);
        n.inventory.Clear();
        n.pistolAmmo = 0;
        _kept.Remove(n.id);
        Revision++;
        string text = LF(inCamp ? "inv_event_relics" : "inv_event_lost", n.npcName, list);
        if (EventLogManager.Instance != null) EventLogManager.Instance.AddEvent(w.day, text);
        w.eventLog.Add(new MemoryEntry { day = w.day, content = text });
    }

    // ============ 小工具 ============

    static Vector3 NpcPos(NpcData n, out bool hasBody)
    {
        NpcMover m = n != null ? NpcView.GetMover(n.id) : null;
        hasBody = m != null;
        return m != null ? m.transform.position : Vector3.zero;
    }

    public static bool InCamp(Vector3 p)
    {
        if (CampZone.Instance != null && CampZone.Instance.Ready) return CampZone.Contains(p, 1f);
        return Vector2.Distance(p, NpcNav.CampCenter(p)) <= NpcNav.CampRadius + 1f;
    }

    static void Log(NpcData n, string text)
    {
        if (n == null || string.IsNullOrEmpty(text)) return;
        WorldState w = World;
        if (w != null) n.memories.Add(new MemoryEntry { day = w.day, content = text });
        if (LogManager.Instance != null) LogManager.Instance.AddLog(n.npcName, text, n.id);
    }

    static string Sep => LanguageGuard.IsJp ? "、" : "、";
    public static string ItemName(string id) => ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(id) : id;
    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] a) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, a) : key;
}
