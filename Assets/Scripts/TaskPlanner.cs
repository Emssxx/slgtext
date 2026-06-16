using Newtonsoft.Json;
using UnityEngine;

// 任务规划器：把玩家的复杂指令（"去仓库拿东西再回营地"）交给 AI，
// 解析成一串有序任务。AI 只负责"在名字层面"拆解动作和地点，不碰坐标。
public static class TaskPlanner
{
    // 构建给 AI 的解析提示词。world 用来告诉 AI 有哪些合法地点和物品。
    public static string BuildParsePrompt(WorldState world)
    {
        string locList = CommandParser.GetLocationListText(world); // 复用之前的地点列表

        // 物品名列表
        var itemNames = new System.Collections.Generic.List<string>();
        foreach (WorldItem it in world.worldItems) itemNames.Add(it.itemName);
        string itemList = string.Join("、", itemNames);

        return
            "你是一个指令解析器。把玩家对幸存者下达的指令，解析成一串有序的任务。\n" +
            "可用的地点只有：" + locList + "\n" +
            "可用的物品只有：" + itemList + "\n" +
            "可用的动作类型：\n" +
            "- go：走到某个地点，需要 destination（地点名）\n" +
            "- act：在原地做一个互动动作（如拿东西、查看、操作），无需参数\n" +
            "- push：把某个物品推到某个地点，需要 target（物品名）和 destination（地点名）\n\n" +
            "【输出要求】只输出一个 JSON，不要任何多余文字，格式：\n" +
            "{\n" +
            "  \"tasks\": [ {\"action\":\"go\",\"destination\":\"仓库\"}, {\"action\":\"act\"}, {\"action\":\"go\",\"destination\":\"营地\"} ],\n" +
            "  \"reply\": \"好的，这就去办。\"\n" +
            "}\n" +
            "地点名和物品名必须从上面给的列表里选，不要自己编。如果玩家的话根本不是行动指令（只是聊天），返回空的 tasks 列表。";
    }

    // 解析 AI 返回的 JSON，健壮地取出 TaskPlan（失败返回 null）。
    public static TaskPlan ParsePlan(string rawJson)
    {
        if (string.IsNullOrEmpty(rawJson)) return null;
        try
        {
            // 截取第一个 { 到最后一个 }，防止 AI 多输出杂字。
            int start = rawJson.IndexOf('{');
            int end = rawJson.LastIndexOf('}');
            if (start < 0 || end < 0 || end <= start) return null;
            string json = rawJson.Substring(start, end - start + 1);

            return JsonConvert.DeserializeObject<TaskPlan>(json);
        }
        catch
        {
            return null; // 解析失败
        }
    }
}
