using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 水源（纯规则）：下雨 / 下雪不再直接给水。
//   · 雨水收集器（RainCollector）：下雨 / 下雪时每回合接雨水，存在自己的水箱里（每台最多 CollectorCapacity）。
//     雨水不能直接喝，装满后再下的雨就流走了。
//   · 水源净化器（PowerNode，kind = Purifier）：要用电线接到电网上。每回合从所有收集器里抽雨水
//     （收集器和净化器之间当作有水管连着，不用拉线），净化成「净化水」放进公共仓库。
//     档位 1~3（AI 终端机「能源」页调）：每档每回合净化 PurifyPerLevel 份、耗 PowerGrid.ConsumptionPerLevel 电；
//     没有雨水可净化时待机，不耗电。
// 每回合顺序（ClimateSystem.OnTurn）：收集器接雨 → 电网结算（净化器有活干才用电）→ 净化。
// ============================================================================
public static class WaterWorks
{
    public static int CollectorCapacity = 10;   // 每台收集器最多存多少份雨水
    public static int PurifyPerLevel = 2;       // 净化器每档每回合净化多少份

    public static int PurifiedLastTurn { get; private set; }

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

    // 电网结算之后调用：通电的净化器把雨水净化成净化水，放进公共仓库。返回这一回合净化了多少。
    public static int Purify()
    {
        int done = 0;
        foreach (PowerNode p in PowerNode.All)
        {
            if (p == null || p.kind != PowerNodeKind.Purifier || !p.powered || p.level <= 0) continue;
            done += TakeRaw(p.level * PurifyPerLevel);
        }
        if (done > 0 && WorldManager.Instance != null) WorldManager.Instance.AddToStorage("water", done);
        PurifiedLastTurn = done;
        return done;
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
