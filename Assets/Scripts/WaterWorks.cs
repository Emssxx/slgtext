using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 水源（纯规则）——参考冰汽时代 / 缺氧 / 环世界这类生存建造游戏的通用做法：
//   【来源】下雨 / 下雪不直接给水。
//   · 雨水收集器（RainCollector）：下雨 / 下雪时每回合接雨水，存在自己的水箱里（每台最多 CollectorCapacity）。
//     雨水不能直接喝，装满后再下的雨就流走了。
//   · 水源净化器（PowerNode，kind = Purifier）：要用电线接到电网上。每回合从所有收集器里抽雨水
//     （收集器和净化器之间当作有水管连着，不用拉线），净化成「净化水」放进公共仓库。
//     档位 1~3（AI 终端机「能源」页调）：每档每回合最多净化 PurifyPerLevel 份；
//     耗电按实际净化量折算（见 PowerGrid.PurifierPower），没有雨水时待机不耗电。
//   【消耗】每日配给：每个活着（没崩解）的人每天喝 RationPerPerson 份净化水，炎热时按 ClimateSystem.EnergyFactor 加量。
//     小数部分记在每个人身上慢慢累积（0.5 份/天 = 两天喝 1 份）。最缺水的人先喝。
//     轮到某人却没水 → 他的「缺水天数」+1；喝上了 → 清零。
//     缺水的后果（掉血、掉体力等）交给 NPC 健康系统：读 WaterWorks.ThirstDays(npcId)。
// 每回合顺序（ClimateSystem.OnTurn）：收集器接雨 → 电网结算（净化器按要处理的水量用电）→ 净化 → 配给。
// ============================================================================
public static class WaterWorks
{
    public const string WaterItem = "water";     // 净化水的物品 id

    public static int CollectorCapacity = 10;    // 每台收集器最多存多少份雨水
    public static int PurifyPerLevel = 2;        // 净化器每档每回合最多净化多少份

    public static bool RationsEnabled = true;    // 每日配给开关
    public static float RationPerPerson = 0.5f;  // 每人每天喝多少份净化水（正常气温下）

    public static int PurifiedLastTurn { get; private set; }  // 上回合净化了多少
    public static int DrunkLastTurn { get; private set; }     // 上回合大家一共喝了多少
    public static int ShortLastTurn { get; private set; }     // 上回合有几个人没喝上水

    static readonly Dictionary<string, float> _owed = new Dictionary<string, float>();   // 每个人"该喝还没喝"的量
    static readonly Dictionary<string, int> _thirstDays = new Dictionary<string, int>(); // 每个人连续缺水几天

    // ============ 查询 ============

    public static int CollectorCount
    {
        get { int c = 0; foreach (RainCollector r in RainCollector.All) if (r != null && r.built) c++; return c; }
    }

    public static int RawTotal
    {
        get { int s = 0; foreach (RainCollector r in RainCollector.All) if (r != null) s += r.stored; return s; }
    }

    public static int RawCapacity => CollectorCount * CollectorCapacity;

    public static int PurifierCount
    {
        get { int c = 0; foreach (PowerNode p in PowerNode.All) if (p != null && p.kind == PowerNodeKind.Purifier && p.IsBuilt) c++; return c; }
    }

    // 仓库里的净化水。
    public static int StoredWater
    {
        get
        {
            if (WorldManager.Instance == null || WorldManager.Instance.world == null) return 0;
            int c = 0;
            foreach (ItemStack s in WorldManager.Instance.world.sharedStorage) if (s.itemName == WaterItem) c += s.count;
            return c;
        }
    }

    // 全营地每天要喝多少（按现在的气温估算）。
    public static float DailyDemand
    {
        get
        {
            if (!RationsEnabled || WorldManager.Instance == null || WorldManager.Instance.world == null) return 0f;
            float d = 0f;
            foreach (NpcData n in WorldManager.Instance.world.npcs)
                if (Drinks(n)) d += RationPerPerson * ClimateSystem.EnergyFactor(n);
            return d;
        }
    }

    // 现有净化水还够喝几天（不算以后的产出）。
    public static float DaysLeft
    {
        get { float d = DailyDemand; return d > 0.001f ? StoredWater / d : float.PositiveInfinity; }
    }

    // 某人连续缺水几天（0 = 不缺水）。给 NPC 健康系统用。
    public static int ThirstDays(string npcId)
    {
        return !string.IsNullOrEmpty(npcId) && _thirstDays.TryGetValue(npcId, out int d) ? d : 0;
    }

    public static bool IsThirsty(NpcData n) => n != null && ThirstDays(n.id) > 0;

    static bool Drinks(NpcData n) => n != null && !n.IsDead && !n.isBroken;

    // ============ 每回合 ============

    // 每台建好的收集器接 perCollector 份雨水。返回实际接到的总数；overflow = 因为装满而流走的。
    public static int Collect(int perCollector, out int overflow)
    {
        overflow = 0;
        int got = 0;
        if (perCollector <= 0) return 0;
        foreach (RainCollector r in RainCollector.All)
        {
            if (r == null || !r.built) continue;
            int space = Mathf.Max(0, CollectorCapacity - r.stored);
            int add = Mathf.Min(space, perCollector);
            r.stored += add;
            got += add;
            overflow += perCollector - add;
        }
        return got;
    }

    // 电网结算之后调用：通电的净化器按电网给它安排的工作量，把雨水净化成净化水放进仓库。
    public static int Purify()
    {
        int done = 0;
        foreach (PowerNode p in PowerNode.All)
        {
            if (p == null || p.kind != PowerNodeKind.Purifier || !p.powered || p.workAmount <= 0) continue;
            done += TakeRaw(p.workAmount);
        }
        if (done > 0 && WorldManager.Instance != null) WorldManager.Instance.AddToStorage(WaterItem, done);
        PurifiedLastTurn = done;
        return done;
    }

    // 每日配给。返回这一回合没喝上水的人。
    public static List<NpcData> ConsumeRations(WorldState w)
    {
        List<NpcData> missed = new List<NpcData>();
        DrunkLastTurn = 0;
        ShortLastTurn = 0;
        if (w == null || !RationsEnabled) return missed;

        List<NpcData> drinkers = new List<NpcData>();
        foreach (NpcData n in w.npcs)
        {
            if (!Drinks(n)) { _thirstDays.Remove(n.id); _owed.Remove(n.id); continue; }
            _owed.TryGetValue(n.id, out float owed);
            _owed[n.id] = owed + RationPerPerson * ClimateSystem.EnergyFactor(n);
            drinkers.Add(n);
        }
        // 最缺水的人先喝
        drinkers.Sort((a, b) => ThirstDays(b.id).CompareTo(ThirstDays(a.id)));

        foreach (NpcData n in drinkers)
        {
            float owed = _owed[n.id];
            bool missedToday = false;
            int drank = 0;
            while (owed >= 1f)
            {
                if (WorldManager.Instance != null && WorldManager.Instance.ConsumeFromStorage(WaterItem, 1))
                {
                    owed -= 1f;
                    drank++;
                    DrunkLastTurn++;
                }
                else { missedToday = true; break; }
            }
            if (missedToday)
            {
                owed = Mathf.Min(owed, 1f); // 欠的水不累积成"一口气喝好几份"
                _thirstDays[n.id] = ThirstDays(n.id) + 1;
                missed.Add(n);
            }
            else if (drank > 0) _thirstDays.Remove(n.id); // 今天喝上了：缺水清零
            _owed[n.id] = owed;
        }
        ShortLastTurn = missed.Count;
        return missed;
    }

    // 从收集器里抽雨水（先抽存得多的）。
    static int TakeRaw(int amount)
    {
        int taken = 0;
        while (amount > 0)
        {
            RainCollector best = null;
            foreach (RainCollector r in RainCollector.All)
                if (r != null && r.stored > 0 && (best == null || r.stored > best.stored)) best = r;
            if (best == null) break;
            best.stored--;
            amount--;
            taken++;
        }
        return taken;
    }

    // 新的一局：清空配给记录。
    public static void Reset()
    {
        _owed.Clear();
        _thirstDays.Clear();
        PurifiedLastTurn = DrunkLastTurn = ShortLastTurn = 0;
    }
}

// 雨水收集器：挂在建筑上。水箱里的水位用一个蓝色方块显示。
public class RainCollector : MonoBehaviour
{
    public static readonly List<RainCollector> All = new List<RainCollector>();

    public bool built = true;   // 施工完成才接雨（建造系统会发 OnBuildPlaced / OnBuildComplete）
    public int stored;          // 存了多少份雨水
    public SpriteRenderer waterLevel;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void OnBuildPlaced() { built = false; }
    void OnBuildComplete() { built = true; }

    void Update()
    {
        if (waterLevel == null) return;
        float fill = WaterWorks.CollectorCapacity > 0 ? Mathf.Clamp01((float)stored / WaterWorks.CollectorCapacity) : 0f;
        waterLevel.enabled = built && fill > 0.001f;
        waterLevel.transform.localScale = new Vector3(1f, fill, 1f);
    }
}
