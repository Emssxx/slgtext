using UnityEngine;

// 可互动物品标记：挂在物品物体上（如箱子）。
// 新架构：不在 Start 自注册，而是提供 Register() 由 WorldManager 统一登记；
// 注册后持续把坐标同步回数据（物品被推动后位置会变）。
public class WorldItemMarker : MonoBehaviour
{
    public string id = "box_01";   // 唯一编号
    public string itemName = "木箱"; // 显示名
    public string itemType = "box";  // 类型：box / weapon / supply ...

    private WorldItem _data; // 注册后持有数据引用，方便实时同步坐标

    // 由 WorldManager 在生成世界后统一调用。
    public void Register()
    {
        _data = new WorldItem
        {
            id = id,
            itemName = itemName,
            itemType = itemType,
            x = transform.position.x,
            y = transform.position.y
        };
        WorldManager.Instance.world.worldItems.Add(_data);
    }

    void Update()
    {
        // 物品被推动后位置会变，每帧同步最新坐标回数据。
        if (_data != null)
        {
            _data.x = transform.position.x;
            _data.y = transform.position.y;
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.4f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.4f, "📦" + itemName);
#endif
    }
}
