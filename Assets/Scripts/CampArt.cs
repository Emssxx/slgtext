using UnityEngine;

// ============================================================================
// 营地美术（手绘素材）：图都放在 Assets/Resources/CampArt/ 里，这里负责把它们读出来、换到游戏里的东西上。
//   · 格栅地砖  CampFloor_Grate：TerrainSystem 铺营地地砖时用（BuildCampFloor）。
//   · 铁丝网    Fence_H（正面，一片 = 1 格宽）/ Fence_V（侧面，只看得到一根柱子）：墙建造物的外观换成它（FenceSkin）。
//   · 升降杆大门 Gate_*：见 CampGate.cs。
// 找不到图的时候全部自动退回原来的样子（像素地砖、像素墙），不会报错。
// 图片的大小 / 轴心写在每张图旁边的 .meta 里（Pixels Per Unit、Pivot），想调位置可以直接在 Unity 里改图片的导入设置。
// ============================================================================
public static class CampArt
{
    const string Dir = "CampArt/";

    // 读一张图（没有返回 null）。Unity 自己会缓存，重复读不费事。
    public static Sprite Load(string name)
    {
        return Resources.Load<Sprite>(Dir + name);
    }

    // 格栅地砖
    public static Sprite Floor => Load("CampFloor_Grate");

    // 放下一块规划区时 BuildManager 调：墙 → 挂上 FenceSkin，把外观换成铁丝网。
    public static void OnPlaced(BuildableData data, GameObject go)
    {
        if (data == null || go == null) return;
        if (data.id != "wall") return;                       // 只管墙
        if (Load("Fence_H") == null) return;                 // 没有铁丝网的图：保持原样
        if (go.GetComponent<FenceSkin>() == null) go.AddComponent<FenceSkin>();
    }
}

// 挂在墙上：把墙的图换成铁丝网。
//   营地南北两边的墙 → 正面的网（Fence_H）；东西两边的墙 → 侧面的柱子（Fence_V）。
//   不在营地那一圈上的墙：上下有墙、左右没有 → 侧面；其他情况 → 正面。
// 只换图，墙的占地、碰撞、挡路、耐久都和原来一样。
public class FenceSkin : MonoBehaviour
{
    void Start()
    {
        Apply();
    }

    public void Apply()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null) return;
        bool vertical = IsVertical(GetComponent<BuildingFootprint>());
        Sprite s = CampArt.Load(vertical ? "Fence_V" : "Fence_H");
        if (s == null) s = CampArt.Load("Fence_H");          // 侧面的图没有就用正面的
        if (s == null) return;
        sr.sprite = s;                                        // 图的轴心在离网脚 0.5 格高的地方，所以网脚正好踩在这一格的下沿
    }

    // 这面墙该不该用"侧面"的图。
    static bool IsVertical(BuildingFootprint fp)
    {
        if (fp == null) return false;
        CampZone z = CampZone.Instance;
        if (z != null && z.Ready)
            foreach (CampZone.Slot s in z.Slots)
                if (s.origin == fp.origin) return s.side == "east" || s.side == "west";   // 在营地那一圈上：看它在哪一边

        GridManager g = GridManager.Instance;
        if (g == null) return false;
        string me = g.GetOccupied(fp.origin.x, fp.origin.y);  // 这一格记的建筑名（墙）
        if (string.IsNullOrEmpty(me)) return false;
        bool up = g.GetOccupied(fp.origin.x, fp.origin.y + fp.size.y) == me;
        bool down = g.GetOccupied(fp.origin.x, fp.origin.y - 1) == me;
        bool left = g.GetOccupied(fp.origin.x - 1, fp.origin.y) == me;
        bool right = g.GetOccupied(fp.origin.x + fp.size.x, fp.origin.y) == me;
        return (up || down) && !(left || right);
    }
}
