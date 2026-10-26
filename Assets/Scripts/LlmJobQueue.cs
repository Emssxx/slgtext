using System;
using System.Collections.Generic;
using UnityEngine;

// 一个后台 LLM 任务（例如：给某个 NPC 写演员提示词、给某次事件写 NPC 的看法）。
public class LlmJob
{
    public string name;                 // 日志里显示的名字
    public int priority;                // 越大越先做（演员提示词 10 > 事件当事人 6 > 事件目击者 4）
    public int maxTokens = 256;         // 最多生成多少 token
    public Func<string> system;         // 发送那一刻才拼提示词（拿到最新的状态）
    public Func<string> user;
    public Func<string, bool> accept;   // 拿到模型输出：解析 + 检查 + 写回。返回 false = 输出不合格
    public Func<bool> stillValid;       // 还需要吗？（例如事件已经太旧了）返回 false 就丢掉
    public Action giveUp;               // 放弃时（多次失败）调用，可为空——代码模板一直在兜底

    internal int badOutputs;            // 输出不合格的次数
    internal int noReplies;             // 没拿到结果的次数（被玩家的对话打断 / 超时 / 模型没开）
    internal float notBefore;           // 这个时间之前不要发（失败后稍等再试）
    internal long order;                // 排队先后
}

// 后台 LLM 任务队列（纯代码调度）：
//   · 一次只跑一个，走 AIClientHub.SendBackground —— 只在模型空闲时发，玩家一说话就让路（被打断的任务稍后重来）。
//   · 按优先级先后执行；输出不合格最多重试 2 次；一直拿不到结果（例如没开 Ollama）就放弃，代码模板兜底。
// 由 NpcActors（演员提示词）和 EventChronicle（事件看法）往里放任务。
public class LlmJobQueue : MonoBehaviour
{
    public static LlmJobQueue Instance { get; private set; }

    public int maxBadOutputs = 2;   // 输出不合格时最多重试几次
    public int maxNoReplies = 8;    // 被打断 / 没回应最多重来几次
    public float retryDelay = 4f;   // 失败后隔几秒再试

    readonly List<LlmJob> _jobs = new List<LlmJob>();
    LlmJob _running;
    long _counter;

    public int Pending => _jobs.Count + (_running != null ? 1 : 0);

    public static LlmJobQueue Ensure()
    {
        if (Instance != null) return Instance;
        GameObject go = new GameObject("LlmJobQueue");
        return go.AddComponent<LlmJobQueue>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Enqueue(LlmJob job)
    {
        if (job == null || job.system == null || job.user == null || job.accept == null) return;
        job.order = _counter++;
        _jobs.Add(job);
    }

    // 新的一局：清空所有任务（正在跑的那个回来后也会被忽略）。
    public void Clear()
    {
        _jobs.Clear();
        _running = null;
    }

    void Update()
    {
        if (_running != null || _jobs.Count == 0) return;
        AIClientHub hub = AIClientHub.Instance;
        if (hub == null || hub.ForegroundBusy || hub.BackgroundBusy) return;

        LlmJob job = PickNext();
        if (job == null) return;

        string sys, usr;
        try { sys = job.system(); usr = job.user(); }
        catch (Exception e)
        {
            Debug.LogWarning("[后台任务] 拼提示词出错，放弃 " + job.name + "：" + e.Message);
            _jobs.Remove(job);
            return;
        }

        LlmJob sent = job;
        bool ok = hub.SendBackground(sys, usr, raw => OnResult(sent, raw), true, job.maxTokens);
        if (!ok) return; // 模型正忙 / 间隔未到：下一帧再试
        _jobs.Remove(job);
        _running = job;
    }

    LlmJob PickNext()
    {
        float now = Time.realtimeSinceStartup;
        LlmJob best = null;
        for (int i = _jobs.Count - 1; i >= 0; i--)
        {
            LlmJob j = _jobs[i];
            bool valid = true;
            try { valid = j.stillValid == null || j.stillValid(); } catch { valid = false; }
            if (!valid) { _jobs.RemoveAt(i); continue; }
            if (j.notBefore > now) continue;
            if (best == null || j.priority > best.priority || (j.priority == best.priority && j.order < best.order)) best = j;
        }
        return best;
    }

    void OnResult(LlmJob job, string raw)
    {
        if (_running != job) return; // 这一局已经清空了
        _running = null;

        if (string.IsNullOrEmpty(raw))
        {
            job.noReplies++;
            if (job.noReplies >= maxNoReplies) { Abandon(job, "一直没有拿到结果"); return; }
            Requeue(job);
            return;
        }

        bool accepted = false;
        try { accepted = job.accept(raw); }
        catch (Exception e) { Debug.LogWarning("[后台任务] 处理结果出错 " + job.name + "：" + e.Message); }

        if (accepted)
        {
            Debug.Log("[后台任务] 完成：" + job.name);
            return;
        }
        job.badOutputs++;
        if (job.badOutputs > maxBadOutputs) { Abandon(job, "输出多次不合格"); return; }
        Debug.Log("[后台任务] 输出不合格，重试：" + job.name + "\n" + raw);
        Requeue(job);
    }

    void Requeue(LlmJob job)
    {
        job.notBefore = Time.realtimeSinceStartup + retryDelay;
        _jobs.Add(job);
    }

    void Abandon(LlmJob job, string why)
    {
        Debug.LogWarning("[后台任务] 放弃（" + why + "），继续使用代码模板：" + job.name);
        try { if (job.giveUp != null) job.giveUp(); } catch { }
    }
}
