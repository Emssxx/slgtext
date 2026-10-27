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
        string langRule = LocalizationManager.Instance != null ? LocalizationManager.Instance.GetAILanguageInstruction() : "";

        // 日语界面：整段提示词用日语写（动作代码 go/act/push 和坐标两种语言通用）。
        if (LanguageGuard.IsJp)
        {
            return
                "あなたは指示の解析器です。プレイヤーが生存者に出した指示を、順番付きのタスクの列に変換してください。\n" +
                "使える地点は次のものだけ：" + locList + "\n" +
                "使える物品は次のものだけ：" + itemList + "\n" +
                "使える動作の種類：\n" +
                "- go：ある地点へ歩いていく。destination（地点名）が必要\n" +
                "- act：その場で何か操作をする（物を取る、調べる、操作する等）。引数は不要\n" +
                "- push：ある物品をある地点まで押す。target（物品名）と destination（地点名）が必要\n\n" +
                "【出力形式】JSONを1つだけ出力し、それ以外は何も書かないこと：\n" +
                "{\n" +
                "  \"tasks\": [ {\"action\":\"go\",\"destination\":\"（地点名）\"}, {\"action\":\"act\"}, {\"action\":\"go\",\"destination\":\"（地点名）\"} ],\n" +
                "  \"reply\": \"<...>\"\n" +
                "}\n" +
                "地点名と物品名は必ず上の一覧から選び、勝手に作らないこと。" +
                "一覧の地点名のほかに、プレイヤーがマップの座標を目的地として言うことがある。" +
                "座標は x と y の2つの数字（例：6,7 や (12,3)、x6 y7）である。" +
                "プレイヤーが座標を言った場合は、destination に「x,y」の形（例：6,7）で入れ、数字を書き換えないこと。" +
                "プレイヤーの言葉が行動の指示ではなく、ただの会話であれば、tasks は空のリストにすること。" +
                "\n注意：reply は、NPCが指示を受けて返す短い一言。下の言語指定に必ず従うこと。\n" +
                langRule;
        }

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
            "  \"tasks\": [ {\"action\":\"go\",\"destination\":\"（地点名）\"}, {\"action\":\"act\"}, {\"action\":\"go\",\"destination\":\"（地点名）\"} ],\n" +
            "  \"reply\": \"<...>\"\n" +
            "}\n" +
            "地点名和物品名必须从上面给的列表里选，不要自己编。" +
            "除了上面列出的地点名，玩家也可能用地图坐标作为目的地，" +
            "坐标是 x、y 两个数字（例如 6,7、(12,3)、x6 y7），" +
            "如果玩家提到坐标，把它写成「x,y」的形式（例如 6,7）填进 destination，不要改动数字。" +
            "如果玩家的话根本不是行动指令（只是聊天），返回空的 tasks 列表。" +
            // reply 示例用中性的 <...> 占位，不写任何具体语言的句子，避免把 AI 带偏到某种语言
            "\n注意：reply 是 NPC 接到命令后回应的一句简短的话，必须遵守下面的语言要求。\n" +
            // 语言指令放在最末尾（和 NpcAgent/AIDirector 一致），由 LocalizationManager 按当前界面语言决定
            langRule;
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
