using UnityEngine;
using UnityEngine.AI;

// 寻路小工具（纯规则）：营地中心、导航网格采样、能不能走到、在不在地图里、在不在迷雾里、随机找落点。
// 需求行为（NpcNeedRunner）等"要找个地方走过去"的系统共用。
public static class NpcNav
{
    static NavMeshPath _path;

    // 营地中心：交给 NpcIdleManager（手动指定 → 终端机 → NPC 生成点）；没有管理器时用终端机或兜底位置。
    public static Vector3 CampCenter(Vector3 fallback)
    {
        if (NpcIdleManager.Instance != null) return NpcIdleManager.Instance.GetCampCenter(fallback);
        if (TerminalController.Instance != null) return TerminalController.Instance.transform.position;
        return fallback;
    }

    // 营地半径（和闲逛范围一致）。
    public static float CampRadius => NpcIdleManager.Instance != null ? NpcIdleManager.Instance.campRadius : 4.5f;

    // 把一个点吸附到导航网格上（z 保持原样）。
    public static bool Sample(Vector3 p, float maxDist, out Vector3 result)
    {
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, maxDist, NavMesh.AllAreas))
        {
            result = new Vector3(hit.position.x, hit.position.y, p.z);
            return true;
        }
        result = p;
        return false;
    }

    // 从 from 能不能完整走到 to（被墙围死的地方算走不到）。
    public static bool Reachable(Vector3 from, Vector3 to)
    {
        if (!NavMesh.SamplePosition(from, out NavMeshHit a, 1f, NavMesh.AllAreas)) return false;
        if (!NavMesh.SamplePosition(to, out NavMeshHit b, 1f, NavMesh.AllAreas)) return false;
        if (_path == null) _path = new NavMeshPath();
        return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, _path)
               && _path.status == NavMeshPathStatus.PathComplete;
    }

    // 在不在网格地图范围内。
    public static bool InGrid(Vector3 p)
    {
        GridManager g = GridManager.Instance;
        if (g == null) return true;
        Vector2Int c = g.WorldToCell(p);
        return g.InBounds(c.x, c.y);
    }

    // 这个位置现在是否"看不清"（迷雾：没探索过，或探索过但不在视野内）。没有迷雾系统时返回 false。
    public static bool IsUnseen(Vector3 p)
    {
        if (FogManager.Instance == null || GridManager.Instance == null) return false;
        Vector2Int c = GridManager.Instance.WorldToCell(p);
        return FogManager.Instance.GetState(c.x, c.y) != FogState.Visible;
    }

    // 在 center 周围 [minR, maxR] 的环形里随机找一个走得到的点。
    // preferUnseen：优先选迷雾里看不清的地方（出去搜寻时用，顺便把迷雾揭开）；找不到再放宽。
    public static bool TryRandomPoint(Vector3 from, Vector3 center, float minR, float maxR, bool preferUnseen, out Vector3 point)
    {
        int passes = preferUnseen ? 2 : 1;
        for (int pass = 0; pass < passes; pass++)
        {
            bool needUnseen = preferUnseen && pass == 0;
            for (int i = 0; i < 16; i++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                float r = Random.Range(minR, maxR);
                Vector3 c = new Vector3(center.x + Mathf.Cos(ang) * r, center.y + Mathf.Sin(ang) * r, from.z);
                if (!InGrid(c)) continue;
                if (!Sample(c, 0.8f, out Vector3 p) || !InGrid(p)) continue;
                if (i < 12 && TerrainSystem.AvoidForWander(p)) continue; // 尽量别选沼泽（走进去很慢）
                if (needUnseen && !IsUnseen(p)) continue;
                if (!Reachable(from, p)) continue;
                point = p;
                return true;
            }
        }
        point = from;
        return false;
    }
}
