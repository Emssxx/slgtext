using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 水源（纯规则）——参考冰汽时代 / 缺氧 / 环世界这类生存建造游戏的通用做法：
//   【来源】下雨 / 下雪不直接给水。
//   · 雨水收集器（RainCollector）：下雨 / 下雪时每回合接雨水，存在自己的水箱里（每台最多 CollectorCapacity）。
//     雨水不能直接喝，装满后再下的雨就流走了。
//   · 湖水（地形系统）：对 NPC 说"去打水"，NPC 去湖边装水、倒进收集器（AddRaw），一样要净化才能喝。
//   · 水源净化器（PowerNode，kind = Purifier）：要用电线接到电网上。每回合从所有收集器里抽雨水
//     （收集器和净化器之间当作有水管连着，不用拉线），净化成「净化水」放进公共仓库。
//     档位 1~3（AI 终端机「能源」页调）：每档每回合最多净化 PurifyPerLevel 份；
//     耗电按实际净化量折算（见 PowerGrid.PurifierPower），没有雨水时待机不耗电。
//   【消耗】每个人有自己的「水分」（NpcData.water，NPC 健康系统 NpcHealth 每秒扣）。水分低于 NpcHealth.drinkBelow 时
//     自动从仓库喝一份净化水（TryDrink）；仓库没水就会口渴 → 口渴难耐 → 掉血，后果都在 NpcHealth 里。
// 每回合顺序（ClimateSystem.OnTurn）：收集器接雨 → 电网结算（净化器按要处理的水量用电）→ 净化。
// ============================================================================
public static class WaterWorks
{
    public const string WaterItem = "water";     // 净化水的物品 id

    public static int CollectorCapacity = 10;    // 每台收集器最多存多少份雨水
    public static int PurifyPerLevel = 2;        // 净化器每档每回合最多净化多少份

    public static int PurifiedLastTurn { get; private set; }  // 上回合净化了多少
    public static int DrunkLastTurn { get; private set; }     // 上回合大家一共喝了多少
    static int _drunkThisTurn;

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

    // 全营地每天大约要喝几份净化水（按现在的气温和 NpcHealth 的水分消耗估算）。
    public static float DailyDemand
    {
        get
        {
            NpcHealth h = NpcHealth.Instance;
            if (h == null || h.waterPerDrink <= 0f || WorldManager.Instance == null || WorldManager.Instance.world == null) return 0f;
            float d = 0f;
            foreach (NpcData n in WorldManager.Instance.world.npcs)
                if (n != null && !n.IsDead) d += h.waterPerSecond * ClimateSystem.EnergyFactor(n) * h.secondsPerDay / h.waterPerDrink;
            return d;
        }
    }

    // 现有净化水还够喝几天（不算以后的产出）。
    public static float DaysLeft
    {
        get { float d = DailyDemand; return d > 0.001f ? StoredWater / d : float.PositiveInfinity; }
    }

    // 现在口渴（口渴 / 口渴难耐 / 饥渴难耐）的人数。
    public static int ThirstyCount
    {
        get
        {
            if (WorldManager.Instance == null || WorldManager.Instance.world == null) return 0;
            int c = 0;
            foreach (NpcData n in WorldManager.Instance.world.npcs)
                if (n != null && !n.IsDead && (NpcHealth.Has(n, HealthTag.Thirsty) || NpcHealth.Has(n, HealthTag.VeryThirsty) || NpcHealth.Has(n, HealthTag.Parched))) c++;
            return c;
        }
    }

    // 喝一份水（NpcHealth 在水分偏低时调用；水分由调用方加）。喝不到返回 false。
    //   营地里：从仓库喝一份净化水。营地外：只能喝自己带的水瓶（喝一瓶少一瓶，NpcInventory）；没带水瓶就只能忍着，回营地再喝。
    public static bool TryDrink(NpcData n)
    {
        if (n == null || n.IsDead || WorldManager.Instance == null) return false;
        NpcMover body = NpcView.GetMover(n.id);
        if (body != null && !NpcInventory.InCamp(body.transform.position))
            return NpcInventory.Remove(n, "bottle", 1);
        if (!WorldManager.Instance.ConsumeFromStorage(WaterItem, 1)) return false;
        _drunkThisTurn++;
        return true;
    }

    // 新的一回合（ClimateSystem.OnTurn 开头调用）：记下上回合喝了多少。
    public static void BeginTurn()
    {
        DrunkLastTurn = _drunkThisTurn;
        _drunkThisTurn = 0;
    }

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

    // 往收集器里倒水（NPC 从湖里打来的水等）：先倒进空位最多的。返回实际倒进去的份数（都满了就流走）。
    public static int AddRaw(int amount)
    {
        int added = 0;
        while (amount > 0)
        {
            RainCollector best = FreestCollector();
            if (best == null) break;
            best.stored++;
            amount--;
            added++;
        }
        return added;
    }

    // 建好的、空位最多的收集器（都满了 / 没有返回 null）。
    public static RainCollector FreestCollector()
    {
        RainCollector best = null;
        foreach (RainCollector r in RainCollector.All)
            if (r != null && r.built && r.stored < CollectorCapacity && (best == null || r.stored < best.stored)) best = r;
        return best;
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

    // 新的一局：清空统计。
    public static void Reset()
    {
        PurifiedLastTurn = DrunkLastTurn = _drunkThisTurn = 0;
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
