# 物品系统（第1批）— 给 Claude Code 的指令

> 这批是在**工程当前版本**上设计的（已读过工程里最新的 GameData / WorldManager / NpcDecisionRules / LocalizationManager）。
> 改动前请先 git commit 一次。

---

## 第一步：加入 4 个新脚本（都是新文件，不覆盖任何已有文件）

放进 `Assets/Scripts/`：
- `ItemData.cs` — 物品定义（id、中日名、说明、图标、种类、效果数值）
- `ItemDatabase.cs` — 物品图鉴单例（列表为空时自动填入 7 种默认物品）
- `ItemUseRules.cs` — 物品使用规则（纯规则，不走 LLM）
- `StoragePanelUI.cs` — 营地仓库面板（I 键开关，运行时自建 UI）

先确认工程里没有同名类（ItemData / ItemDatabase / ItemUseRules / StoragePanelUI / ItemCategory），编译通过后停下让我测。

---

## 第二步：改 3 个已有文件（只改下面列出的行，最小改动）

### 2-1. WorldManager.cs —— `CreateWorld()` 里的初始仓库
仓库从此存**物品 id**（英文），不再存中文名。把：
```csharp
world.sharedStorage.Add(new ItemStack { itemName = "罐头", count = 3 });
world.sharedStorage.Add(new ItemStack { itemName = "饮用水", count = 5 });
```
改成：
```csharp
// 仓库存物品 id（定义见 ItemDatabase）。用英文 id，喂给 AI 的世界 JSON 里不会混入中文。
world.sharedStorage.Add(new ItemStack { itemName = "ration",    count = 3 });  // 蛋白营养块
world.sharedStorage.Add(new ItemStack { itemName = "water",     count = 4 });  // 净化水
world.sharedStorage.Add(new ItemStack { itemName = "sedative",  count = 1 });  // 镇静剂
world.sharedStorage.Add(new ItemStack { itemName = "medkit",    count = 1 });  // 急救包
world.sharedStorage.Add(new ItemStack { itemName = "stimulant", count = 1 });  // 兴奋剂
world.sharedStorage.Add(new ItemStack { itemName = "nano",      count = 10 }); // 纳米物质（建造材料）
world.sharedStorage.Add(new ItemStack { itemName = "crystal",   count = 1 });  // 异变结晶
```

### 2-2. NpcDecisionRules.cs —— 去掉写死的"罐头"

**(a) `IsStorageLow`** 整个方法体改成：
```csharp
// 仓库是否物资不足：所有"食物类"物品加起来少于 2 份。
static bool IsStorageLow(WorldState world)
{
    if (ItemDatabase.Instance == null) return true;
    return ItemDatabase.Instance.CountInStorage(ItemCategory.Food, world) < 2;
}
```

**(b) `Execute` 里的 `case Behavior.Eat:`** 改成（加花括号，避免和别的 case 变量冲突）：
```csharp
case Behavior.Eat:
{
    // 在仓库里找任意一种食物（不再写死"罐头"），用统一的物品规则吃掉它。
    string food = ItemDatabase.Instance != null
        ? ItemDatabase.Instance.FindFirstInStorage(ItemCategory.Food, world) : null;
    if (food != null && ItemUseRules.Use(npc, food, world, false)) // false：日志由这里自己写
    {
        npc.currentAction = "刚吃过东西";
        log = Loc("log_eat_success", npc.npcName, ItemDatabase.Instance.GetName(food));
    }
    else { npc.currentAction = "在找吃的"; log = Loc("log_eat_fail", npc.npcName); }
    break;
}
```

**(c) `case Behavior.Gather:`** 里：
```csharp
WorldManager.Instance.AddToStorage("罐头", 1);
```
改成：
```csharp
WorldManager.Instance.AddToStorage("ration", 1); // 搜寻带回 1 份蛋白营养块
```

### 2-3. LocalizationManager.cs —— 翻译表

**改 2 条已有的：**
```csharp
{ "log_eat_success", ("{0} 吃了{1}。", "{0}は{1}を口にした。") },
{ "log_gather",      ("{0} 外出搜寻，带回了一些物资。", "{0}は捜索に出て、物資を持ち帰った。") },
```

**在 `// 以后要加文本…` 这一行上面，加入：**
```csharp
// —— 物品 / 仓库面板（StoragePanelUI、ItemUseRules）——
{ "storage_title",       ("营地仓库", "拠点の倉庫") },
{ "storage_target",      ("使用对象：", "使用対象：") },
{ "storage_no_target",   ("（未选择）", "（未選択）") },
{ "storage_empty",       ("仓库是空的。", "倉庫は空っぽだ。") },
{ "storage_pick_hint",   ("点击左侧物品查看详情。", "左の物品をクリックすると詳細が見られる。") },
{ "storage_use_on",      ("对 {0} 使用", "{0}に使う") },
{ "btn_use",             ("使用", "使う") },
{ "log_use_item",        ("{0} 使用了{1}。", "{0}は{1}を使った。") },
{ "item_fail_no_target", ("没有使用对象。先选择一个 NPC 对话。", "使用対象がいない。先にNPCを選んで会話しよう。") },
{ "item_fail_dead",      ("对方已经死亡。", "相手はもう死んでいる。") },
{ "item_fail_unknown",   ("未知的物品。", "不明な物品。") },
{ "item_fail_not_usable",("这个物品不能直接使用。", "この物品は直接使えない。") },
{ "item_fail_empty",     ("仓库里已经没有了。", "倉庫にもう残っていない。") },
{ "item_fail_broken",    ("已经崩解的人，药物也救不回来了。", "崩壊した者には、もう薬も効かない。") },
{ "item_cat_material",   ("建造材料（不能直接使用）", "建築素材（直接は使えない）") },
{ "item_cat_special",    ("亚空间特产（用途未明）", "亜空間の産物（用途不明）") },
```

**另外：** 全工程搜一下 `"罐头"` 和 `"饮用水"`，如果上面 3 个文件之外还有地方用到（比如 WorldLore、EventExecutor），告诉我位置，先别改。

---

## 第三步：场景挂载（先告诉我计划再建物体）

1. 游戏场景里建 1 个空物体 `ItemSystem`，挂 `ItemDatabase` + `StoragePanelUI`。
2. `StoragePanelUI.font` 拖入和 NpcCard 相同的中日文 TMP 字体（不拖中文会变方块）。
3. 确认 I 键没有被别的脚本占用。

---

## 第四步：测试

1. 按 I 打开仓库：7 种物品 + 数量，点物品右侧显示说明和效果（绿=好、红=坏）。
2. 点场景里某个 NPC（切换对话目标）→ 面板上方"使用对象"跟着变。
3. 选"镇静剂" → 对该 NPC 使用 → 数量 -1，NPC 理智 +25（用 F2 SAN 面板或状态卡确认），日志栏出现"X 使用了镇静剂"。
4. 选"纳米物质" → 没有使用按钮，显示"建造材料（不能直接使用）"。
5. 把某 NPC 的 SAN 调到 0 让他崩解 → 对他用镇静剂 → 提示"药物也救不回来"，不扣数量。
6. 等 NPC 饿了自己吃饭 → 日志显示"X 吃了蛋白营养块"，仓库数量减少。
7. 主菜单切日语进游戏 → 面板、物品名、日志全是日语。

测通后提醒我 git commit。
