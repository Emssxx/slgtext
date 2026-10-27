using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json; // 用 Newtonsoft 序列化（比 JsonUtility 支持更全）

// 世界管理器（新架构版）：世界状态的唯一真相来源（单例）。
// 负责：随机生成 NPC（词条池）、初始化 NPC 间关系、登记场景标记、提供查询/仓库工具。
public class WorldManager : MonoBehaviour
{
    public static WorldManager Instance { get; private set; }

    public WorldState world = new WorldState();

    [Header("生成设置")]
    public int npcCount = 3; // 开局生成几个 NPC

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        CreateWorld();                                  // 1. 造世界 + 随机生成 NPC
        GenerateTerrain();                              // 1.2 按地图种子生成地形 + 天然资源，并重新烘焙 NavMesh（水、沼泽、挡路的树石）
        // NavMesh 在上一步已经按新地形烘焙好了，所以 world.npcs 数据一生成好，马上就能按数据在场景里生成 NPC 物体。
        FindFirstObjectByType<NpcSpawner>()?.SpawnAll(); // 1.5 按 world.npcs 数据自动生成场景 NPC
        RegisterAllMarkers();                            // 2. 登记场景里的地点/物品标记
        RelationshipManager.InitAllRelationships(world); // 3. 初始化 NPC 间关系
        NpcMemory.BeginSession(world);                   // 4. 建本局的 NPC 档案文件夹（名册、AI 词库、每人的档案和聊天记录）
        HudLayout.Ensure();                              // 5. 整理主界面布局（左上事件/日志抽屉、右下建造、顶部天数）
        ClimateSystem.Ensure();                          // 6. 天气 + 气温 + 电力（每回合在 RunOneTurnResolve 里结算）
        NpcHealth.Ensure();                              // 7. NPC 健康系统（饥饿值 / 水分 / 体力 / 状态词条，每秒结算）
        ConstructionManager.Ensure();                    // 8. 规划区施工（点蓝图指派施工的人）
        Debug.Log("当前世界快照：\n" + ExportWorldAsJson());
    }

    // 地形（TerrainSystem）：失败了也不影响开局，用场景原来的地图继续。
    void GenerateTerrain()
    {
        try { TerrainSystem.Ensure().Generate(); }
        catch (System.Exception e) { Debug.LogError("[Terrain] 地形生成失败，用原来的地图继续：" + e); }
    }

    // 造世界：用词条池随机生成 NPC。
    void CreateWorld()
    {
        world = new WorldState();
        world.day = 1;
        world.timeOfDay = "白天";
        world.globalSituation = "你们刚被卷入亚空间，在一处废弃营地中醒来。";

        // 随机生成 npcCount 个各不相同的 NPC。
        NpcGenerator.ResetUsedNames();   // 重置已用名字，避免跨局累积导致名字重复
        for (int i = 0; i < npcCount; i++)
        {
            string id = "npc_" + (i + 1).ToString("00"); // npc_01, npc_02...
            NpcData npc = NpcGenerator.Generate(id);
            world.npcs.Add(npc);
        }

        // 公共仓库放点初始物资。
        // 仓库存物品 id（定义见 ItemDatabase）。用英文 id，喂给 AI 的世界 JSON 里不会混入中文。
        world.sharedStorage.Add(new ItemStack { itemName = "ration",    count = 3 });  // 蛋白营养块
        world.sharedStorage.Add(new ItemStack { itemName = "water",     count = 4 });  // 净化水
        world.sharedStorage.Add(new ItemStack { itemName = "sedative",  count = 1 });  // 镇静剂
        world.sharedStorage.Add(new ItemStack { itemName = "medkit",    count = 1 });  // 急救包
        world.sharedStorage.Add(new ItemStack { itemName = "stimulant", count = 1 });  // 兴奋剂
        world.sharedStorage.Add(new ItemStack { itemName = "nano",      count = 10 }); // 纳米物质（建造材料）
        world.sharedStorage.Add(new ItemStack { itemName = "crystal",   count = 1 });  // 异变结晶
    }

    // 扫描场景里所有 LocationMarker / WorldItemMarker，统一登记到世界数据。
    // （解决各标记 Start 时序问题：由 WorldManager 统一在生成世界后登记。）
    void RegisterAllMarkers()
    {
        foreach (LocationMarker m in FindObjectsByType<LocationMarker>()) // 新 API 替代过时的 FindObjectsOfType（默认不排序，更快）
            m.Register();
        foreach (WorldItemMarker m in FindObjectsByType<WorldItemMarker>())
            m.Register();

        Debug.Log($"已登记 {world.locations.Count} 个地点、{world.worldItems.Count} 个物品");
    }

    // 把世界状态转成 JSON（喂 AI 用）。
    public string ExportWorldAsJson()
    {
        return JsonConvert.SerializeObject(world, Formatting.Indented);
    }

    // —— 查询/工具方法 ——

    // 按 id 找 NPC。
    public NpcData GetNpcById(string id)
    {
        foreach (NpcData npc in world.npcs)
            if (npc.id == id) return npc;
        return null;
    }

    // 推进一个回合（天数+1）。具体数值影响由 WorldRules 处理。
    public void AdvanceTurn()
    {
        world.day += 1;
    }

    // 跑完整的一回合结算（推进+规则+崩解+生命），供外部系统调用。
    // AIDirector 正常回合用这个，EventExecutor 时间失序快进也用这个，逻辑只有一份。
    public void RunOneTurnResolve()
    {
        AdvanceTurn();
        if (ClimateSystem.Instance != null) ClimateSystem.Instance.OnTurn(world); // 天气、气温、风力发电、加温/降温、收集雨水
        TerrainSystem.OnTurn(world);                                              // 地形：湖里的鱼恢复（以后植物生长）
        WorldRules.ApplyTurnEffects(world);
        WorldRules.ApplyBreakdown(world);
        WorldRules.ApplyHealth(world);
    }

    // —— 仓库操作 ——

    // 从公共仓库消耗指定物品，成功返回 true。
    public bool ConsumeFromStorage(string itemName, int amount)
    {
        foreach (ItemStack stack in world.sharedStorage)
        {
            if (stack.itemName == itemName && stack.count >= amount)
            {
                stack.count -= amount;
                return true;
            }
        }
        return false;
    }

    // 往公共仓库添加物品（没有则新建一条）。
    public void AddToStorage(string itemName, int amount)
    {
        foreach (ItemStack stack in world.sharedStorage)
        {
            if (stack.itemName == itemName)
            {
                stack.count += amount;
                return;
            }
        }
        world.sharedStorage.Add(new ItemStack { itemName = itemName, count = amount });
    }
}
