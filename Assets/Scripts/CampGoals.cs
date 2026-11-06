using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 营地目标（任务浮窗 NpcTaskPanel 显示的内容）："目标1　将营地用围墙围起来 / 负责人：田中、铃木"。
//   目标从哪来（每 0.5 秒看一次，由 NpcTaskPanel 调 Tick）：
//     · 营地还没围起来（围墙已解锁）→ 「将营地用围墙围起来」
//     · 领导定的建造项目（NpcSociety.Project）→ 「建一座 X」
//     · 营地参谋（CampAdvisor，隐形的 4B）眼下的建议：建什么 / 打水 / 找吃的 / 找药 / 治伤 / 休息 / 采什么 / 只有 AI 能做的事
//   完成没有：由代码按实际情况判断（围起来了、X 建成了、净化水够喝了……），不看 AI 说没说。
//   负责人：按领导的分工找（建造 → 建造组 + 搬运组，打水 → 搬运组……）；只有 AI 能做的事，负责人是「AI（你）」。
//   完成的目标在浮窗里打勾、划掉，过几秒再从列表里拿掉；浮窗关着的时候完成的，等下次打开浮窗再播动画。
//   参谋不再提、也没完成的目标，过 staleSeconds 秒拿掉（情况变了）。
// ============================================================================
public static class CampGoals
{
    public class Goal
    {
        public string key;       // enclose / build:rain_collector / water / food / medicine / health / rest / gather:stone / ai:water_purifier / power
        public string kind;
        public string target;
        public int baseline;     // build：定这个目标时已经建成了几座
        public float createdAt, lastWanted;
        public float doneAt = -1f;
        public bool animated;    // 浮窗里的打勾动画放过了
        public float animatedAt;
        public bool Done => doneAt >= 0f;
    }

    public static float staleSeconds = 60f;      // 不再需要、也没完成：过多久拿掉
    public static float doneKeepSeconds = 6f;    // 打勾动画放完后再留几秒
    public static float doneWaitSeconds = 120f;  // 完成了但浮窗一直没打开：最多留多久

    static readonly List<Goal> _goals = new List<Goal>();
    public static IReadOnlyList<Goal> All => _goals;
    public static int Version { get; private set; }   // 列表里多了 / 少了目标就 +1（浮窗重排）
    static WorldState _world;

    // ============ 每 0.5 秒 ============

    public static void Tick()
    {
        WorldState w = World;
        if (w != _world) { _world = w; _goals.Clear(); Version++; }
        if (w == null) return;
        float now = Time.time;

        // 1. 现在需要哪些目标
        if (NpcIntentBrain.WallsNeeded()) Want("enclose", "enclose", "wall", now);
        BuildableData project = NpcSociety.Project;
        if (project != null) Want("build:" + project.id, "build", project.id, now);
        foreach (CampAdvisor.Advice a in CampAdvisor.Current)
        {
            switch (a.act)
            {
                case "build":
                    if (a.target == "wall") Want("enclose", "enclose", "wall", now);
                    else Want("build:" + a.target, "build", a.target, now);
                    break;
                case "haul_water": Want("water", "water", "", now); break;
                case "scavenge":
                    if (a.need == "health") Want("medicine", "medicine", "", now);
                    else Want("food", "food", "", now);
                    break;
                case "care":   Want("health", "health", "", now); break;
                case "rest":   Want("rest", "rest", "", now); break;
                case "gather": Want("gather:" + a.target, "gather", a.target, now); break;
                case "ask_ai":
                    if (a.target == PowerBuildings.PurifierId) Want("ai:" + a.target, "ai", a.target, now);
                    else Want("power", "power", "", now);
                    break;
            }
        }

        // 2. 完成了没有；该拿掉的拿掉
        for (int i = _goals.Count - 1; i >= 0; i--)
        {
            Goal g = _goals[i];
            if (!g.Done && Reached(g, w)) g.doneAt = now;
            bool remove = g.Done
                ? (g.animated ? now - g.animatedAt > doneKeepSeconds : now - g.doneAt > doneWaitSeconds)
                : now - g.lastWanted > staleSeconds;
            if (remove) { _goals.RemoveAt(i); Version++; }
        }
    }

    static void Want(string key, string kind, string target, float now)
    {
        foreach (Goal g in _goals)
            if (g.key == key)
            {
                if (!g.Done) g.lastWanted = now;
                return;   // 刚完成、还在显示勾的目标：不重复加
            }
        Goal n = new Goal { key = key, kind = kind, target = target ?? "", createdAt = now, lastWanted = now };
        if (kind == "build") n.baseline = BuiltCount(target);
        if (Reached(n, World)) return;   // 本来就满足了：不用当目标
        _goals.Add(n);
        Version++;
    }

    // ============ 完成条件（代码按实际情况判断）============

    static bool Reached(Goal g, WorldState w)
    {
        if (w == null) return false;
        switch (g.kind)
        {
            case "enclose": return CampZone.Instance != null && CampZone.Instance.Ready && CampZone.Instance.Enclosed;
            case "build":   return BuiltCount(g.target) > g.baseline;
            case "water":   return WaterWorks.DaysLeft >= CampAdvisor.WaterLowDays && WaterWorks.ThirstyCount == 0;
            case "food":    return CampAdvisor.FoodDays(w) >= CampAdvisor.FoodLowDays;
            case "medicine":return ItemDatabase.Instance != null && ItemDatabase.Instance.CountInStorage(ItemCategory.Medical, w) > 0;
            case "health":  return HurtCount(w) == 0;
            case "rest":
            {
                int alive = 0, tired = 0;
                foreach (NpcData n in w.npcs)
                {
                    if (n == null || n.IsDead) continue;
                    alive++;
                    if (NpcHealth.Has(n, HealthTag.Fatigued)) tired++;
                }
                return tired * 2 < alive;
            }
            case "gather":
            {
                int need = 10;
                BuildableData p = NpcSociety.Project;
                if (p != null)
                    foreach (MaterialCost c in NpcIntentBrain.CostOf(p))
                        if (c != null && c.materialName == g.target) need = Mathf.Max(need, c.amount);
                return ItemUseRules.GetCount(g.target, w) >= need;
            }
            case "ai":    return CampAdvisor.PurifierLinked();
            case "power": { PowerGrid.Summary ps = PowerGrid.Total(); return ps.shed == 0 && ps.unlinkedDevices == 0; }
            default:      return false;
        }
    }

    static int HurtCount(WorldState w)
    {
        int k = 0;
        foreach (NpcData n in w.npcs)
            if (n != null && !n.IsDead && (NpcHealth.Has(n, HealthTag.Injured) || NpcHealth.Has(n, HealthTag.Sick)
                || NpcHealth.Has(n, HealthTag.CriticalWound) || NpcHealth.Has(n, HealthTag.Dying))) k++;
        return k;
    }

    // 已经建成了几座（蓝图 / 施工中的不算）。
    public static int BuiltCount(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        if (id == PowerBuildings.CollectorId) return WaterWorks.CollectorCount;
        if (id == PowerBuildings.PurifierId) return WaterWorks.PurifierCount;
        PowerNodeKind kind;
        if (id == PowerBuildings.TurbineId) kind = PowerNodeKind.Turbine;
        else if (id == PowerBuildings.BatteryId) kind = PowerNodeKind.Battery;
        else if (id == PowerBuildings.HeaterId) kind = PowerNodeKind.Heater;
        else if (id == PowerBuildings.CoolerId) kind = PowerNodeKind.Cooler;
        else return BuiltByName(id);
        int k = 0;
        foreach (PowerNode p in PowerNode.All) if (p != null && p.kind == kind && p.IsBuilt) k++;
        return k;
    }

    static int BuiltByName(string id)
    {
        BuildableData d = Data(id);
        GridManager g = GridManager.Instance;
        if (d == null || g == null) return 0;
        int k = 0;
        foreach (BuildingFootprint fp in BuildingFootprint.All)
        {
            if (fp == null || fp.GetComponent<ConstructionSite>() != null) continue;
            string name = g.GetOccupied(fp.origin.x, fp.origin.y);
            if (!string.IsNullOrEmpty(name) && name.StartsWith(d.displayName)) k++;
        }
        return k;
    }

    // ============ 文字 ============

    public static string Text(Goal g)
    {
        switch (g.kind)
        {
            case "enclose":  return L("goal_enclose");
            case "build":    return LF("goal_build", BuildName(g.target));
            case "water":    return L("goal_water");
            case "food":     return L("goal_food");
            case "medicine": return L("goal_medicine");
            case "health":   return L("goal_health");
            case "rest":     return L("goal_rest");
            case "gather":   return LF("goal_gather", ItemName(g.target));
            case "ai":       return LF("goal_ai_power", BuildName(g.target));
            case "power":    return L("goal_power");
            default:         return g.key;
        }
    }

    // 负责人（按领导的分工找）。返回 null = 负责的是 AI（玩家）。
    public static List<string> Owners(Goal g)
    {
        if (g.kind == "ai" || g.kind == "power") return null;
        List<string> names = new List<string>();
        WorldState w = World;
        if (w == null) return names;
        foreach (NpcData n in w.npcs)
        {
            if (n == null || n.IsDead || n.isBroken) continue;
            bool mine = false;
            switch (g.kind)
            {
                case "enclose":
                case "build":    mine = n.role == "build" || n.role == "haul" || BuildingIt(n, g); break;
                case "water":    mine = n.role == "haul"; break;
                case "food":
                case "medicine": mine = n.role == "scavenge"; break;
                case "health":   mine = n.role == "care"; break;
                case "rest":     mine = n.role == "rest"; break;
                case "gather":   mine = n.role == "gather" && (string.IsNullOrEmpty(n.roleTarget) || n.roleTarget == g.target); break;
            }
            if (mine) names.Add(n.npcName);
        }
        return names;
    }

    // 没有分工、但正在施工这个目标的蓝图的人（比如还没有领导时自己去修墙的人）也算负责人。
    static bool BuildingIt(NpcData n, Goal g)
    {
        ConstructionManager cm = ConstructionManager.Instance;
        ConstructionSite site = cm != null ? cm.SiteOf(n.id) : null;
        if (site == null || site.data == null) return false;
        return g.kind == "enclose" ? site.data.id == "wall" : site.data.id == g.target;
    }

    static BuildableData Data(string id)
    {
        BuildManager bm = BuildManager.Instance;
        if (bm == null || bm.buildables == null) return null;
        foreach (BuildableData d in bm.buildables) if (d != null && d.id == id) return d;
        return null;
    }

    static string BuildName(string id) { BuildableData d = Data(id); return d != null ? d.LocalizedName : id; }
    static string ItemName(string id) => string.IsNullOrEmpty(id) ? "" : ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(id) : GameLexicon.NameOf(id);
    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;
    static WorldState World => WorldManager.Instance != null ? WorldManager.Instance.world : null;
}
