using System;
using System.Collections.Generic;

// 一个任务 = 一个动作 + 它需要的参数。
[Serializable]
public class NpcTask
{
    public string action;       // "go"（去某地）/ "act"（原地互动）/ "push"（推物品到某地）
    public string destination;   // 目标地点名（go / push 用）
    public string target;        // 目标物品名（push 用）
}

// AI 解析返回的整包：一串有序任务 + 一句给玩家看的回应话。
[Serializable]
public class TaskPlan
{
    public List<NpcTask> tasks = new List<NpcTask>(); // 有序任务列表
    public string reply;                               // NPC 接到指令时说的一句话（可选）
}
