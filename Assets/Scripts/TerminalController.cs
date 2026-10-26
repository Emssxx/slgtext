using UnityEngine;

// AI 终端机控制器（MVP 极简版 + 可扩展骨架）。
// 设定：终端机是玩家（AI）在亚空间里的"锚点"——玩家通过它指挥、获取信息。
//
// 【设计原则】MVP 功能极简，但结构上预留扩展位，方便以后加功能（肯定会改）。
//   - MVP：终端机只是"存在的核心建筑"，标记玩家的锚点，预留信息显示接口。
//   - 以后：重构物质、传送资源、科技升级等，都作为独立"功能模块"挂上来，
//           不需要改这个核心脚本。
//
// 扩展方式：以后每加一个功能，就新建一个继承 TerminalModule 的脚本，
//   挂到终端机物体上即可。TerminalController 会自动收集并管理它们。
public class TerminalController : MonoBehaviour
{
    public static TerminalController Instance { get; private set; }

    [Header("基础设置")]
    public string terminalName = "AI终端机"; // 显示名
    public bool isActive = true;             // 是否激活（MVP 默认开机；以后可做"未激活→激活"的开局解锁）

    [Header("建造状态")]
    [Tooltip("场景里预先摆好的终端机保持勾选；由建造系统放下的会自动变为未建成，施工完成才算建成。")]
    public bool isBuilt = true;

    // 挂在这个终端机上的所有功能模块（以后扩展用，自动收集）。
    private TerminalModule[] _modules;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 自动收集挂在自己身上的所有功能模块（MVP 阶段可能一个都没有，没关系）。
        _modules = GetComponents<TerminalModule>();
    }

    void Start()
    {
        // 初始化所有模块。
        foreach (var m in _modules)
            m.OnTerminalStart(this);
    }

    // —— MVP 阶段的极简功能 ——

    // 终端机是否在线（玩家的指挥/信息能力是否可用）。
    // MVP 里默认 true；以后可做"没造终端机时受限、造了才解锁"。
    public bool IsOnline()
    {
        return isActive && isBuilt; // 建成 + 激活才算在线（施工中不在线，迷雾/任务都不生效）
    }

    // 建造系统放下预览时调用：施工完成前不算建成。
    void OnBuildPlaced() { isBuilt = false; }

    // 施工完成时调用：终端机上线，营地进入"一产"。
    void OnBuildComplete()
    {
        isBuilt = true;
        if (WorldManager.Instance == null) return;
        WorldState w = WorldManager.Instance.world;
        if (w.terminalLevel < 1) w.terminalLevel = 1;
        string msg = ProgressionRules.LF("terminal_built", ProgressionRules.TierLabel(w.terminalLevel));
        if (EventLogManager.Instance != null) EventLogManager.Instance.AddEvent(w.day, msg);
        w.eventLog.Add(new MemoryEntry { day = w.day, content = msg }); // 让 AI 导演/NPC 知道
    }

    // 预留：以后终端机要显示信息（地图情报、资源概览等）时，从这里走。
    // MVP 阶段先空着，或只返回一句话。
    public string GetStatusInfo()
    {
        return isActive ? $"{terminalName}：在线" : $"{terminalName}：离线";
    }

    // —— 扩展接口：以后加功能时用 ——

    // 按类型取某个功能模块（以后用，如 GetModule<PurifyModule>()）。
    public T GetModule<T>() where T : TerminalModule
    {
        foreach (var m in _modules)
            if (m is T tm) return tm;
        return null;
    }
}

// 终端机功能模块的基类。
// 以后每个新功能（重构物质、传送资源、科技…）都继承它，做成独立脚本，挂到终端机上。
// 这样加功能 = 加一个新模块脚本，完全不改 TerminalController 核心。
public abstract class TerminalModule : MonoBehaviour
{
    // 终端机启动时调用，子类在这里做自己的初始化。
    public virtual void OnTerminalStart(TerminalController terminal) { }
}
