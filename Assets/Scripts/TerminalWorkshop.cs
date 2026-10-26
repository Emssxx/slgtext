using System.Collections.Generic;
using UnityEngine;

// AI 终端机 · 工作台模块：合成（建造台）、科技研究、终端机升级，全部作为"任务"在这里排队执行。
// 挂在【终端机预制体】上（和 TerminalController 同一个物体），TerminalController 会自动收集它。
//
// 【核心设计：算力槽】
//   终端机同一时间能处理的任务数 = 算力槽数 = 终端机等级（一产 1 槽、二产 2 槽、三产 3 槽）。
//   合成、研究、升级共用这些槽——玩家（AI）要决定把有限的算力先给谁，这是资源取舍的一部分。
//   终端机离线或还在施工时，所有任务暂停。
public class TerminalWorkshop : TerminalModule
{
    [Header("任务队列")]
    public int maxWaiting = 5;                                    // 最多排队几项（不含正在处理的）
    public List<TerminalJob> running = new List<TerminalJob>();   // 正在处理的任务
    public List<TerminalJob> waiting = new List<TerminalJob>();   // 排队中的任务

    private TerminalController _terminal;

    // 当前算力槽数 = 终端机等级（至少 1）。
    public int Slots => Mathf.Clamp(ProgressionRules.Tier, 1, ProgressionRules.MaxTier);

    // 方便 UI 拿到当前终端机上的工作台。
    public static TerminalWorkshop Current =>
        TerminalController.Instance != null ? TerminalController.Instance.GetModule<TerminalWorkshop>() : null;

    public override void OnTerminalStart(TerminalController terminal) { _terminal = terminal; }

    void Update()
    {
        if (_terminal == null) _terminal = GetComponent<TerminalController>();
        if (_terminal == null || !_terminal.IsOnline()) return; // 离线 / 施工中：全部暂停

        // 1. 有空槽就从排队里拿任务开始处理。
        while (running.Count < Slots && waiting.Count > 0)
        {
            running.Add(waiting[0]);
            waiting.RemoveAt(0);
        }

        // 2. 处理中的任务倒计时，到时间就完成。
        for (int i = running.Count - 1; i >= 0; i--)
        {
            running[i].remaining -= Time.deltaTime;
            if (running[i].remaining <= 0f)
            {
                TerminalJob done = running[i];
                running.RemoveAt(i);
                Complete(done);
            }
        }
    }

    // ================= 加入任务（UI 按钮调用）=================
    // 返回是否成功；message 是给玩家看的反馈（成功/失败原因，已本地化）。
    // 材料在【加入队列时】就扣掉，防止同一份材料被多个任务重复使用。

    public bool TryEnqueueCraft(RecipeData r, out string message)
    {
        if (!ProgressionRules.CanCraft(r, out message)) return false;
        if (IsFull(out message)) return false;
        ProgressionRules.ConsumeMaterials(r.inputs);
        Add(TerminalJobType.Craft, r.id, r.craftTime);
        message = ProgressionRules.LF("job_queued", ProgressionRules.ItemName(r.outputItemId) + " ×" + r.outputCount);
        return true;
    }

    public bool TryEnqueueResearch(TechData t, out string message)
    {
        if (!ProgressionRules.CanResearch(t, out message)) return false;
        if (IsQueued(TerminalJobType.Research, t.id)) { message = ProgressionRules.L("job_already_queued"); return false; }
        if (IsFull(out message)) return false;
        ProgressionRules.ConsumeMaterials(t.cost);
        Add(TerminalJobType.Research, t.id, t.researchTime);
        message = ProgressionRules.LF("job_queued", ProgressionRules.TechName(t.id));
        return true;
    }

    public bool TryEnqueueUpgrade(TerminalUpgradeStep s, out string message)
    {
        if (!ProgressionRules.CanUpgrade(s, out message)) return false;
        if (HasJobOfType(TerminalJobType.Upgrade)) { message = ProgressionRules.L("job_already_queued"); return false; }
        if (IsFull(out message)) return false;
        ProgressionRules.ConsumeMaterials(s.cost);
        Add(TerminalJobType.Upgrade, s.toLevel.ToString(), s.upgradeTime);
        message = ProgressionRules.LF("job_queued", ProgressionRules.TierLabel(s.toLevel));
        return true;
    }

    // ================= 查询 =================

    public bool IsQueued(TerminalJobType type, string id)
    {
        foreach (var j in running) if (j.type == type && j.targetId == id) return true;
        foreach (var j in waiting) if (j.type == type && j.targetId == id) return true;
        return false;
    }

    public bool HasJobOfType(TerminalJobType type)
    {
        foreach (var j in running) if (j.type == type) return true;
        foreach (var j in waiting) if (j.type == type) return true;
        return false;
    }

    // 任务的显示名（UI 队列显示用）。
    public static string JobLabel(TerminalJob job)
    {
        switch (job.type)
        {
            case TerminalJobType.Craft:
                RecipeData r = TerminalDatabase.Instance != null ? TerminalDatabase.Instance.GetRecipe(job.targetId) : null;
                return r != null ? ProgressionRules.ItemName(r.outputItemId) + " ×" + r.outputCount : job.targetId;
            case TerminalJobType.Research:
                return ProgressionRules.TechName(job.targetId);
            default:
                int.TryParse(job.targetId, out int lv);
                return ProgressionRules.LF("upgrade_to", ProgressionRules.TierLabel(lv));
        }
    }

    // ================= 内部 =================

    bool IsFull(out string message)
    {
        message = "";
        if (waiting.Count >= maxWaiting) { message = ProgressionRules.L("job_queue_full"); return true; }
        return false;
    }

    void Add(TerminalJobType type, string id, float time)
    {
        float t = Mathf.Max(0.1f, time);
        waiting.Add(new TerminalJob { type = type, targetId = id, duration = t, remaining = t });
    }

    // 任务完成：落实结果，写事件日志和全局大事记（AI 导演/NPC 也会"知道"营地的进展）。
    void Complete(TerminalJob job)
    {
        if (WorldManager.Instance == null) return;
        WorldState w = WorldManager.Instance.world;
        string msg = "";

        switch (job.type)
        {
            case TerminalJobType.Craft:
                RecipeData r = TerminalDatabase.Instance != null ? TerminalDatabase.Instance.GetRecipe(job.targetId) : null;
                if (r != null)
                {
                    WorldManager.Instance.AddToStorage(r.outputItemId, r.outputCount); // 产物进公共仓库
                    msg = ProgressionRules.LF("job_done_craft", ProgressionRules.ItemName(r.outputItemId), r.outputCount);
                }
                break;

            case TerminalJobType.Research:
                if (!w.unlockedTechs.Contains(job.targetId)) w.unlockedTechs.Add(job.targetId);
                msg = ProgressionRules.LF("job_done_research", ProgressionRules.TechName(job.targetId));
                break;

            case TerminalJobType.Upgrade:
                if (int.TryParse(job.targetId, out int lv))
                {
                    w.terminalLevel = Mathf.Clamp(Mathf.Max(w.terminalLevel, lv), 0, ProgressionRules.MaxTier);
                    msg = ProgressionRules.LF("job_done_upgrade", ProgressionRules.TierLabel(w.terminalLevel));
                }
                break;
        }

        if (string.IsNullOrEmpty(msg)) return;
        if (EventLogManager.Instance != null) EventLogManager.Instance.AddEvent(w.day, msg);
        w.eventLog.Add(new MemoryEntry { day = w.day, content = msg });
    }
}
