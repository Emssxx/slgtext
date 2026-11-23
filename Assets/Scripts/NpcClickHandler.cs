using UnityEngine;

// NPC 点击标记（留着是因为 NPC 预制体上挂着它，GridClickInput 也靠它认出"点到的是人"）。
// 点击 NPC 打开对话窗口的逻辑已经移到界面层的 WorldClickRouter（统一处理点 NPC / 敌人 / 建筑 / 资源）。
// 本脚本不再做任何事；以后新做的 NPC 不挂它也能点。
[RequireComponent(typeof(NpcView))]
public class NpcClickHandler : MonoBehaviour
{
}
