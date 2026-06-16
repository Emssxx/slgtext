using UnityEngine;

// 地点标记：挂在空物体上，放在地图某位置，代表一个命名地点。
// 新架构：不在 Start 自注册，而是提供 Register() 由 WorldManager 统一登记
// （解决各标记 Start 时序问题）。
public class LocationMarker : MonoBehaviour
{
    public string locName = "新地点"; // 地点名（Inspector 填）

    // 由 WorldManager 在生成世界后统一调用，把自己登记进世界。
    public void Register()
    {
        WorldManager.Instance.world.locations.Add(new LocationData
        {
            locName = locName,
            x = transform.position.x,
            y = transform.position.y
        });
    }

    // Scene 视图画标记，方便摆位置（不影响运行）。
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.3f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.4f, "📍" + locName);
#endif
    }
}
