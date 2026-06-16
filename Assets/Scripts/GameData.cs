using System;                       // [Serializable] 等基础类型
using System.Collections.Generic;   // List<T> 列表类型

// ============================================================
// 数据层：本文件全是"纯数据类"，故意不引用 UnityEngine，让数据与引擎解耦。
// 加了 [Serializable] 后，它们依然能在 Inspector 显示、也能转成 JSON 喂给 AI。
// 本文件是整个项目的地基，其它系统都依赖这里的数据结构。
// ============================================================


// —— 一条"记忆/事件"：某个 NPC 或世界经历过的事。喂给 AI 当上下文，让行为前后连贯。——
[Serializable]
public class MemoryEntry
{
    public int day;         // 这件事发生在第几天
    public string content;  // 事件内容，比如"我和李四一起修好了发电机"
}


// —— 物品堆叠：一种物品 + 数量。——
[Serializable]
public class ItemStack
{
    public string itemName; // 物品名，比如"罐头"
    public int count;       // 数量
}


// —— 性格（5 个维度，0~100，越高越强）。结构化存储，方便规则读取、也方便翻译成文字喂 AI。——
[Serializable]
public class Personality
{
    public int bravery = 50;      // 勇敢：影响面对危险的反应（高者更晚逃跑）
    public int altruism = 50;     // 利他：影响是否帮助他人
    public int caution = 50;      // 谨慎：影响冒险倾向
    public int sociability = 50;  // 外向：影响话多话少（崩解疯话也用它区分尖叫/失语）
    public int optimism = 50;     // 乐观：影响精神韧性（高者 SAN 衰减略慢）
}


// —— NPC 对另一个 NPC 的关系。单向：A→B 和 B→A 各存一条，可不对称。——
[Serializable]
public class Relationship
{
    public string targetId;        // 对谁（另一个 NPC 的 id）
    public float trust = 50f;      // 信任度 0~100：信不信任对方（能力、可靠性）
    public float affinity = 0f;    // 友善度 -100~100：喜不喜欢对方（可为负=厌恶）
    public string note;            // 备注：这段关系的来由（如"曾一起逃生"）
}


// —— 一个 NPC（亚空间里的幸存者）。AI 大脑要操控的核心对象。——
[Serializable]
public class NpcData
{
    // —— 身份 ——
    public string id;          // 唯一编号，比如 "npc_01"，程序靠它区分不同 NPC
    public string npcName;     // 显示名字
    public string gender;      // 性别（词条池生成时赋予）
    public string profession;  // 职业，比如"教师""消防员"（影响崩解疯话的职业残留）

    // —— 性格（结构化 5 维）+ 词条 ——
    public Personality personality = new Personality(); // 5 维性格数值
    public List<string> traits = new List<string>();    // 随机生成时抽到的性格词条（如"寡言""念旧"），喂 AI 用
    public string values;        // 价值观（一句话，喂 AI）
    public string speakingStyle; // 说话风格（喂 AI，NPC 对话要符合）
    public string background;    // 背景故事（喂 AI）
    public string currentGoal;   // 当前目标（喂 AI）

    // —— 生存数值（范围 0~100）——
    public float health = 100f; // 生命
    public float hunger = 0f;   // 饥饿度，越高越饿
    public float energy = 100f; // 精力/体力
    public float sanity = 100f; // 精神/理智（亚空间会侵蚀理智，本作特色数值）

    // —— 精神状态标志（由 WorldRules / MentalState 维护）——
    public bool isHallucinating = false;          // 是否处于幻觉状态
    public bool isBroken = false;                 // 是否已人格崩解（不可逆）
    public List<string> mutations = new List<string>(); // 崩解后的身体异变（如"羊角""手心的眼睛"）

    // —— 对玩家的信任度：决定听不听玩家指令（0~100）——
    public float playerTrust = 50f;

    // —— 对其他 NPC 的关系（单向，每个其他 NPC 一条）——
    public List<Relationship> relationships = new List<Relationship>();

    // —— 当前状态 ——
    public string location;      // 当前所在区域，比如"营地"
    public string currentAction; // 正在做的事，比如"在休息"

    // —— 拥有的东西 ——
    public List<ItemStack> inventory = new List<ItemStack>(); // 身上携带的物品

    // —— 记忆 ——
    public List<MemoryEntry> memories = new List<MemoryEntry>(); // 最近经历，喂 AI 用
}


// —— 地点：地图上的一个命名位置（供 AI 指挥"去某地"用）。——
[Serializable]
public class LocationData
{
    public string locName; // 地点名，比如"仓库""营地"
    public float x;        // 世界坐标 X
    public float y;        // 世界坐标 Y
}


// —— 世界物品：散落在地图上的可互动物体（可被推、被捡）。——
[Serializable]
public class WorldItem
{
    public string id;        // 唯一编号
    public string itemName;  // 物品名，比如"木箱"
    public string itemType;  // 类型，比如"box""supply"
    public float x;          // 世界坐标 X
    public float y;          // 世界坐标 Y
}


// —— 整个世界的全局状态。游戏里只有"一份"这个对象。——
[Serializable]
public class WorldState
{
    public int day = 1;               // 现在是第几天
    public string timeOfDay = "白天"; // 时段：白天 / 夜晚
    public float anomalyLevel = 0f;   // 亚空间异常程度（0~100），AI 导演的"难度旋钮"
    public string anomalyMood = "平静"; // 由异常度推导的"情绪"（导演定基调用，WorldRules 维护）
    public bool isSafeZone = false;   // 当前是否身处安全据点（影响 SAN/体力回复）
    public string globalSituation;    // 当前大局的一句话描述

    public List<NpcData> npcs = new List<NpcData>();              // 世界里所有 NPC
    public List<LocationData> locations = new List<LocationData>(); // 所有命名地点
    public List<WorldItem> worldItems = new List<WorldItem>();      // 地图上的可互动物品
    public List<ItemStack> sharedStorage = new List<ItemStack>();   // 营地公共仓库物资
    public List<MemoryEntry> eventLog = new List<MemoryEntry>();    // 全局大事记
}
