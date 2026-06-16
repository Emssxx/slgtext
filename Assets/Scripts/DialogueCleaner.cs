using System.Text.RegularExpressions;

// 对话清洗器：在显示 NPC 对话前，用代码强行去掉出戏内容。
// 这是最后一道保险——即使模型不听提示词规则，也能挡住自言自语、称呼穿帮。
public static class DialogueCleaner
{
    // 传入 NPC 说的原话 + 它自己的名字，返回清洗后的话。
    public static string Clean(string dialogue, string selfName)
    {
        if (string.IsNullOrEmpty(dialogue)) return dialogue;

        string s = dialogue.Trim();

        // 1. 去掉开头的“自己名字：”或“自己名字 ”前缀（自言自语的典型形式）。
        //    例如“老五：老五，我们……”→去掉开头的“老五：”。
        if (!string.IsNullOrEmpty(selfName))
        {
            // 形如 “老五：” “老五:” “老五，” “老五 ” 开头的，统统去掉。
            string pattern = "^" + Regex.Escape(selfName) + @"[\s：:，,、]+";
            s = Regex.Replace(s, pattern, "");

            // 有时会连着出现两次（“老五：老五 ”），再清一次。
            s = Regex.Replace(s, pattern, "");
        }

        // 2. 把穿帮称呼替换掉（模型偶尔会说“玩家”）。
        s = s.Replace("玩家", "你");

        // 3. 去掉明显的破戏词（保险起见，温和处理：只删独立出现的）。
        //    这里只做最小替换，避免误伤正常句子。
        s = s.Replace("（游戏）", "").Replace("这款游戏", "这里").Replace("游戏里", "这里");

        return s.Trim();
    }
}
