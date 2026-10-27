using System.Collections.Generic;
using System.Text.RegularExpressions;

// 指令的种类。
public enum CmdIntent { Go, Return, UseItem, Fetch, Gather, Rest, Eat, Push }

// 解析出来的一条指令（由代码从玩家的话里提取，不靠 LLM）。
public class NpcCommand
{
    public CmdIntent intent;
    public string itemId;        // UseItem / Fetch：物品 id（来自词库）
    public string targetNpcId;   // UseItem：用在谁身上（来自名册；null = 自己）
    public string destination;   // Go / Push：地点记号（"@camp" "@storage" "@terminal" "@npc:id"）、坐标（"6,7"）或场景地点名
    public string boxName;       // Push：要推的物体名
}

// 指令系统（纯规则，"代码做决定"）：
//   Parse：用 AI 词库 + NPC 名册 + 动词表，从玩家的话里认出"要做什么、对什么、给谁"。认不出就返回 null，当作普通聊天。
//   Plan ：检查做不做得到（仓库有没有、地点在不在、对象活着没），排出 NpcTaskRunner 的任务队列，
//          并生成一句"命令内容"（显示在对话窗口，也交给 LLM 让 NPC 用自己的口吻回应）。
// 动词表中日两种语言都收，玩家用哪种语言下指令都行。想支持新说法，往对应的表里加词即可。
public static class NpcCommands
{
    static readonly string[] ReturnWords = { "回来", "回营地", "回去", "停下", "停止", "别去了", "不用去了", "算了", "撤回", "收工",
                                             "戻って", "戻れ", "帰って", "帰れ", "止まって", "止まれ", "やめて", "中止", "引き返" };
    static readonly string[] NegWords = { "不要", "别去", "别拿", "别用", "别动", "别走", "别吃", "不用", "不必", "不许", "不准", "ないで", "するな" };
    static readonly string[] UseWords = { "用", "使用", "吃", "喝", "服用", "注射", "打一针", "包扎", "治疗", "敷",
                                          "使って", "使え", "使用", "飲んで", "飲ませ", "食べて", "食べさせ", "手当て", "治療", "打って" };
    static readonly string[] FetchWords = { "拿", "取", "找", "带", "搬", "领", "弄", "捡",
                                            "取って", "取りに", "持って", "探して", "持ってきて", "取ってきて", "拾って" };
    static readonly string[] GiveWords = { "给", "に使", "にあげ", "に渡", "に飲ませ", "に食べさせ" };
    static readonly string[] GatherWords = { "搜寻", "搜索", "搜刮", "找物资", "收集", "采集", "找吃的", "找点吃的", "找食物", "出去找", "侦察", "探索", "巡逻", "找点东西",
                                             "物資を探", "食料を探", "食べ物を探", "集めて", "調達", "偵察", "見回り" };
    static readonly string[] RestWords = { "休息", "歇", "睡觉", "睡一会", "躺", "休んで", "休憩", "寝て", "眠って", "横になって" };
    static readonly string[] EatWords = { "吃饭", "吃点东西", "吃东西", "去吃", "填肚子", "垫垫肚子", "食事", "何か食べ", "ご飯" };
    static readonly string[] GoWords = { "去", "到", "前往", "过去", "走到", "过来", "来", "移动", "跑",
                                         "行って", "行け", "向かって", "移動", "来て", "進んで", "行く" };
    static readonly string[] PushWords = { "推", "挪", "押して", "運んで", "動かして" };

    // 坐标（x,y）：6,7 / (6,7) / （6，7） / x6 y7 / X:6 Y:7 …（鼠标在地图上停半秒能看到格子的坐标）
    static readonly Regex GridCoord = new Regex(@"(?<![\d.])[\(（\[]?\s*[xX]?\s*[:：=]?\s*(\d{1,2})(?:\s*[,，、]\s*[yY]?|\s+[yY]|\s*[yY])\s*[:：=]?\s*(\d{1,2})(?![\d.])\s*[\)）\]]?");
    // 旧的网格编号（G7、B12…）也还认：前后不能紧挨着别的英文字母/数字。
    static readonly Regex GridLabel = new Regex(@"(?<![A-Za-z0-9])([A-Za-z])(\d{1,2})(?![0-9])");

    // ============ 解析 ============

    // 从玩家的话里认出指令。actor：执行指令的 NPC。认不出返回 null（当作普通聊天）。
    public static NpcCommand Parse(string text, NpcData actor, WorldState world)
    {
        if (string.IsNullOrEmpty(text) || actor == null) return null;
        string t = text.Trim();

        LexEntry item = GameLexicon.FindFirst(t, LexEntry.KindItem);
        LexEntry place = GameLexicon.FindFirst(t, LexEntry.KindPlace);
        LexEntry box = GameLexicon.FindFirst(t, LexEntry.KindBox);
        List<string> others = NpcRoster.FindMentioned(t, actor.id);
        string grid = FindGrid(t);

        // 1. 叫回来 / 停下（最优先：玩家想撤销）
        if (Has(t, ReturnWords)) return new NpcCommand { intent = CmdIntent.Return, destination = "@camp" };

        // 2. 否定句（"别担心""不要去"）当聊天
        if (Has(t, NegWords)) return null;

        // 3. 提到了物品：用 / 拿 / 给别人用
        if (item != null)
        {
            bool use = Has(t, UseWords), fetch = Has(t, FetchWords), give = Has(t, GiveWords);
            if (others.Count > 0 && (use || fetch || give))
                return new NpcCommand { intent = CmdIntent.UseItem, itemId = item.id, targetNpcId = others[0] };
            if (use) return new NpcCommand { intent = CmdIntent.UseItem, itemId = item.id };
            if (fetch) return new NpcCommand { intent = CmdIntent.Fetch, itemId = item.id };
            // 只提到物品、没有动作（"你有急救包吗"）→ 聊天
        }

        // 4. 搜寻 / 休息 / 吃饭
        if (Has(t, GatherWords)) return new NpcCommand { intent = CmdIntent.Gather };
        if (Has(t, RestWords)) return new NpcCommand { intent = CmdIntent.Rest, destination = "@camp" };
        if (Has(t, EatWords)) return new NpcCommand { intent = CmdIntent.Eat, destination = "@storage" };

        // 5. 推东西到某处
        if (box != null && Has(t, PushWords) && (grid != null || place != null))
            return new NpcCommand { intent = CmdIntent.Push, boxName = box.id, destination = grid ?? place.id };

        // 6. 移动：网格编号（短句里单独出现也算）/ 地点 / 去某人那里
        bool go = Has(t, GoWords);
        if (grid != null && (go || t.Length - grid.Length <= 3))
            return new NpcCommand { intent = CmdIntent.Go, destination = grid };
        if (go && place != null) return new NpcCommand { intent = CmdIntent.Go, destination = place.id };
        if (go && others.Count > 0) return new NpcCommand { intent = CmdIntent.Go, destination = "@npc:" + others[0] };

        return null;
    }

    static bool Has(string t, string[] words)
    {
        foreach (string w in words) if (t.Contains(w)) return true;
        return false;
    }

    // 找出话里的坐标，统一写成 "x,y"（GridManager.LabelToCell 认得）。
    static string FindGrid(string t)
    {
        Match c = GridCoord.Match(t);
        if (c.Success) return c.Groups[1].Value + "," + c.Groups[2].Value;
        Match m = GridLabel.Match(t);
        if (!m.Success) return null;
        int x = char.ToUpperInvariant(m.Groups[1].Value[0]) - 'A';
        return x + "," + m.Groups[2].Value; // 旧编号 G7 → 6,7
    }

    // ============ 排任务 ============

    // 检查能不能做，排出任务。orderText：命令内容（当前语言）；做不到时 failText 给出原因，返回 false。
    public static bool Plan(NpcData actor, NpcCommand cmd, WorldState w, out List<NpcTask> tasks, out string orderText, out string failText)
    {
        tasks = new List<NpcTask>();
        orderText = "";
        failText = null;
        if (actor == null || cmd == null || w == null) { failText = L("fail_place_unknown"); return false; }

        switch (cmd.intent)
        {
            case CmdIntent.Go:
            {
                orderText = LF("order_go", DestName(cmd.destination));
                if (!CommandParser.TryGetLocationPos(cmd.destination, w, out _, out _))
                {
                    failText = cmd.destination == "@terminal" ? L("fail_terminal_none") : L("fail_place_unknown");
                    return false;
                }
                tasks.Add(new NpcTask { action = "go", destination = cmd.destination });
                return true;
            }

            case CmdIntent.Return:
                orderText = L("order_return");
                tasks.Add(new NpcTask { action = "go", destination = "@camp" });
                return true;

            case CmdIntent.UseItem:
            {
                NpcData target = string.IsNullOrEmpty(cmd.targetNpcId) ? actor : WorldManager.Instance.GetNpcById(cmd.targetNpcId);
                if (target == null) target = actor;
                string itemName = GameLexicon.NameOf(cmd.itemId);
                orderText = target == actor ? LF("order_use_self", itemName) : LF("order_use_on", itemName, target.npcName);

                ItemData d = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(cmd.itemId) : null;
                bool carried = Carries(actor, cmd.itemId);
                if (d == null) { failText = L("item_fail_unknown"); return false; }
                if (!d.IsUsable) { failText = L("item_fail_not_usable"); return false; }
                if (target.IsDead) { failText = LF("fail_target_dead", target.npcName); return false; }
                if (!carried && ItemUseRules.GetCount(cmd.itemId, w) <= 0) { failText = L("item_fail_empty"); return false; }
                if (target.isBroken && d.sanityDelta > 0f && d.healthDelta <= 0f && d.hungerDelta >= 0f) { failText = L("item_fail_broken"); return false; }

                if (!carried) tasks.Add(new NpcTask { action = "go", destination = "@storage" });
                tasks.Add(new NpcTask { action = "use_item", target = cmd.itemId, destination = target == actor ? "@self" : "@npc:" + target.id });
                return true;
            }

            case CmdIntent.Fetch:
            {
                string itemName = GameLexicon.NameOf(cmd.itemId);
                if (ItemUseRules.GetCount(cmd.itemId, w) > 0)
                {
                    orderText = LF("order_fetch", itemName);
                    tasks.Add(new NpcTask { action = "go", destination = "@storage" });
                    tasks.Add(new NpcTask { action = "take_item", target = cmd.itemId });
                }
                else
                {
                    // 仓库里没有 → 出去找
                    orderText = LF("order_search_item", itemName);
                    tasks.Add(new NpcTask { action = "search_item", target = cmd.itemId });
                }
                return true;
            }

            case CmdIntent.Gather:
                orderText = L("order_gather");
                tasks.Add(new NpcTask { action = "search_item", target = "" });
                return true;

            case CmdIntent.Rest:
                orderText = L("order_rest");
                tasks.Add(new NpcTask { action = "go", destination = "@camp" });
                tasks.Add(new NpcTask { action = "rest" });
                return true;

            case CmdIntent.Eat:
                orderText = L("order_eat");
                if (ItemDatabase.Instance != null && ItemDatabase.Instance.CountInStorage(ItemCategory.Food, w) <= 0)
                {
                    failText = L("fail_no_food");
                    return false;
                }
                tasks.Add(new NpcTask { action = "go", destination = "@storage" });
                tasks.Add(new NpcTask { action = "eat" });
                return true;

            case CmdIntent.Push:
            {
                orderText = LF("order_push", cmd.boxName, DestName(cmd.destination));
                bool boxExists = false;
                foreach (WorldItem it in w.worldItems) if (it.itemName == cmd.boxName) { boxExists = true; break; }
                if (!boxExists) { failText = L("fail_box_unknown"); return false; }
                if (!CommandParser.TryGetLocationPos(cmd.destination, w, out _, out _)) { failText = L("fail_place_unknown"); return false; }
                tasks.Add(new NpcTask { action = "push", target = cmd.boxName, destination = cmd.destination });
                return true;
            }
        }
        failText = L("fail_place_unknown");
        return false;
    }

    // 地点记号 → 显示名（当前语言）。
    public static string DestName(string dest)
    {
        if (string.IsNullOrEmpty(dest)) return "";
        if (dest.StartsWith("@npc:"))
        {
            NpcData n = WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(dest.Substring(5)) : null;
            return LF("dest_npc", n != null ? n.npcName : "?");
        }
        if (dest[0] == '@') return GameLexicon.NameOf(dest);
        if (Regex.IsMatch(dest, @"^\d+,\d+$")) return "(" + dest + ")"; // 坐标
        return dest; // 场景地点名，原样显示
    }

    static bool Carries(NpcData npc, string itemId)
    {
        if (npc.inventory == null) return false;
        foreach (ItemStack s in npc.inventory) if (s.itemName == itemId && s.count > 0) return true;
        return false;
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;
    static string LF(string key, params object[] args) => LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;
}
