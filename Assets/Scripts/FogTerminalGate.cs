using UnityEngine;

// 战争迷雾 · 终端机激活门槛（第4批）。
// 把迷雾和 AI 终端机的激活状态连起来：
//   终端机【未激活】：关闭NPC揭雾，只露出营地（终端机）周围固定几格。
//                     —— 玩家是AI，没接入终端机就"看不清"这个世界。
//   终端机【激活】  ：开启NPC揭雾（第3批），世界视野随NPC探索揭开。
//                     —— AI接入了视觉，豁然开朗。
//
// 依赖：TerminalController（读激活状态）、FogVisionUpdater（揭雾开关）、
//       FogManager（揭雾接口）、GridManager（坐标）。
public class FogTerminalGate : MonoBehaviour
{
    [Header("引用")]
    public FogVisionUpdater visionUpdater;   // NPC揭雾更新器（Inspector拖入，或自动找）

    [Header("未激活时露出的营地范围")]
    public int campRevealRadius = 5;         // 未激活时，营地（终端机）周围露出的半径（格）

    // 记录上一帧终端机是否激活，用于检测"激活状态发生变化"的那一刻。
    private bool _lastActive;
    private bool _inited;

    void Start()
    {
        if (visionUpdater == null)
            visionUpdater = FindObjectOfType<FogVisionUpdater>();
    }

    void Update()
    {
        if (TerminalController.Instance == null || FogManager.Instance == null) return;

        bool active = TerminalController.Instance.IsOnline();

        // 首次运行，或激活状态发生了变化时，处理一次。
        if (!_inited || active != _lastActive)
        {
            _inited = true;
            _lastActive = active;
            OnTerminalStateChanged(active);
        }

        // 未激活期间，持续保持营地周围露出（防止被 DemoteVisibleToExplored 盖回去）。
        if (!active)
            RevealCampArea();
    }

    // 终端机激活状态变化时调用。
    void OnTerminalStateChanged(bool active)
    {
        if (visionUpdater != null)
            visionUpdater.enableNpcReveal = active;  // 激活才开启NPC揭雾

        if (active)
        {
            // 刚激活：豁然开朗——先把营地周围揭一片，之后交给NPC揭雾持续更新。
            RevealCampArea();
            // （可选演出：这里以后可以加"迷雾大范围散开"的特效/音效。）
        }
        else
        {
            // 未激活：只露营地周围（其余保持黑）。
            RevealCampArea();
        }
    }

    // 露出营地（终端机）周围固定范围。
    void RevealCampArea()
    {
        if (TerminalController.Instance == null || GridManager.Instance == null) return;

        // 用终端机自身的位置作为营地中心（终端机就是营地核心）。
        Vector3 campPos = TerminalController.Instance.transform.position;
        Vector2Int center = GridManager.Instance.WorldToCell(campPos);
        FogManager.Instance.RevealArea(center.x, center.y, campRevealRadius);
    }
}
