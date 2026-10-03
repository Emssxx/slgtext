using System.Collections.Generic;
using UnityEngine;

// 指令解析器：把玩家的自然语言，解析成"动作 + 目标地点"。
// 策略：关键词匹配为主（快、准），匹配不到再交给 AI 兜底。
// 这一版先做"去某地点"(go)，推箱子(push)下一步加。
public static class CommandParser
{
    // 解析结果。
    public class Command
    {
        public string action;       // "go"（去某地）；以后扩展 "push"
        public string destination;   // 目标地点名（已确认存在于 world.locations）
        public bool valid;           // 是否成功解析出一个可执行指令
    }

    // 表示"移动"意图的动词关键词（中日两种语言，便于两种语言的指令都能快速识别）。
    static readonly string[] MoveVerbs = {
        // 中文
        "去", "到", "走到", "前往", "过去",
        // 日语
        "行って", "行け", "向かって", "移動", "進んで", "来て"
    };

    // 第一层：纯关键词匹配。成功返回 Command，失败返回 null（交给 AI 兜底）。
    public static Command TryKeywordParse(string playerText, WorldState world)
    {
        if (string.IsNullOrEmpty(playerText)) return null;

        // 1. 话里有没有移动动词？
        bool hasMoveVerb = false;
        foreach (string v in MoveVerbs)
            if (playerText.Contains(v)) { hasMoveVerb = true; break; }

        // 2. 话里有没有提到某个登记地点的名字？
        string matchedLoc = null;
        foreach (LocationData loc in world.locations)
        {
            if (!string.IsNullOrEmpty(loc.locName) && playerText.Contains(loc.locName))
            {
                matchedLoc = loc.locName;
                break;
            }
        }

        // 3. 既有"去"的意思、又点到了某个地点 → 确定是移动指令。
        if (matchedLoc != null && hasMoveVerb)
        {
            return new Command { action = "go", destination = matchedLoc, valid = true };
        }

        // 只点到地点没动词，也大概率是想去那里（放宽）。
        if (matchedLoc != null)
        {
            return new Command { action = "go", destination = matchedLoc, valid = true };
        }

        return null; // 关键词没匹配到，交给 AI 兜底
    }

    // 查某地点的坐标。找不到返回 false。
    public static bool TryGetLocationPos(string locName, WorldState world, out float x, out float y)
    {
        // 第一优先：按地点名匹配 world.locations（原有逻辑保留）
        foreach (LocationData loc in world.locations)
        {
            if (loc.locName == locName)
            {
                x = loc.x; y = loc.y;
                return true;
            }
        }

        // 第二优先：尝试把 locName 当网格编号（如 G7）解析
        // 先检查 GridManager 是否存在，避免没有网格的场景报空引用
        if (GridManager.Instance != null)
        {
            Vector2Int cell;
            // LabelToCell 把 "G7" 转成格子坐标 (6,7)
            if (GridManager.Instance.LabelToCell(locName, out cell))
            {
                // CellToWorld 把格子坐标转成世界坐标（取格子中心）
                Vector3 worldPos = GridManager.Instance.CellToWorld(cell);
                x = worldPos.x;
                y = worldPos.y;
                return true;
            }
        }

        // 两者都不匹配，返回失败
        x = 0; y = 0;
        return false;
    }

    // 拼一个"地点名列表"字符串，给 AI 兜底时用（让它只能从这些里选）。
    public static string GetLocationListText(WorldState world)
    {
        List<string> names = new List<string>();
        foreach (LocationData loc in world.locations)
            names.Add(loc.locName);
        return string.Join("、", names);
    }
}
