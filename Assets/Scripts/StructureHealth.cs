using UnityEngine;
using NavMeshPlus.Components;

// ============================================================================
// 墙的耐久（被敌人打的时候才挂上来，不用改预制体）。
//   · 只有营地的墙会被拆（挡路的建筑里，墙以外的不拆；终端机这种唯一建筑也不拆），免得把发电、终端这些系统弄坏。
//   · 耐久打光 → 墙消失、格子空出来、导航网格重新烘焙 → 敌人从缺口进来；
//     CampZone 下一次扫描会发现这段墙位又空了，NPC 会照常把它补上（自主建造 / 蓝图）。
//   · 耐久数值在 EnemyDirector 上（wallHp）。
// ============================================================================
public class StructureHealth : MonoBehaviour
{
    public float maxHp = 120f;
    public float hp = 120f;

    Transform _bar, _fill;
    float _showUntil;
    string _name;
    bool _isWall;
    BuildingFootprint _fp;

    // 打一下。返回 true = 这一下把它打坏了（物体已经开始销毁）。
    public static bool Damage(GameObject go, float dmg, Enemy by)
    {
        if (go == null || dmg <= 0f) return false;
        StructureHealth s = go.GetComponent<StructureHealth>();
        if (s == null) s = go.AddComponent<StructureHealth>().Init();
        return s.Hit(dmg, by);
    }

    StructureHealth Init()
    {
        _fp = GetComponent<BuildingFootprint>();
        BuildableData d = DataOf(_fp);
        _isWall = IsWall(d);
        EnemyDirector dir = EnemyDirector.Instance;
        maxHp = dir != null ? (_isWall ? dir.wallHp : dir.structureHp) : 120f;
        hp = maxHp;
        _name = d != null ? d.LocalizedName : gameObject.name;
        // 耐久条（挨打后显示几秒）
        float top = 0.55f;
        GridManager g = GridManager.Instance;
        if (_fp != null && g != null) top = _fp.size.y * g.cellSize * 0.5f + 0.15f;
        _bar = EnemyDirector.Quad("WallHp", transform, new Color(0f, 0f, 0f, 0.75f), 6);
        _bar.localPosition = new Vector3(0f, top, 0f);
        _bar.localScale = new Vector3(0.6f, 0.06f, 1f);
        _fill = EnemyDirector.Quad("Fill", _bar, new Color(0.95f, 0.8f, 0.3f), 7);
        _bar.gameObject.SetActive(false);
        return this;
    }

    bool Hit(float dmg, Enemy by)
    {
        if (hp <= 0f) return true;
        hp -= dmg;
        _showUntil = Time.time + 5f;
        float r = Mathf.Clamp01(hp / Mathf.Max(1f, maxHp));
        if (_fill != null)
        {
            _fill.localScale = new Vector3(r, 1f, 1f);
            _fill.localPosition = new Vector3(-(1f - r) * 0.5f, 0f, 0f);
        }
        if (_bar != null) _bar.gameObject.SetActive(true);
        if (hp > 0f) return false;
        Break(by);
        return true;
    }

    void Update()
    {
        if (_bar != null && _bar.gameObject.activeSelf && Time.time >= _showUntil) _bar.gameObject.SetActive(false);
    }

    void Break(Enemy by)
    {
        GridManager g = GridManager.Instance;
        if (_fp != null && g != null) g.ClearArea(_fp.origin, _fp.size);   // 格子空出来（可以重新建）
        EnemyDirector.OnStructureBroken(transform.position, _name, _isWall, by);
        // 先拿掉"不可走"标记再销毁，烘焙时就不会再算它
        NavMeshModifier mod = GetComponent<NavMeshModifier>();
        if (mod != null) Destroy(mod);
        gameObject.SetActive(false);
        Destroy(gameObject);
        if (EnemyDirector.Instance != null) EnemyDirector.Instance.RequestNavRebake();
    }

    // ============ 查询 ============

    // 这块地上的建筑是什么（按占地格记下的名字对回建筑数据）
    public static BuildableData DataOf(BuildingFootprint fp)
    {
        GridManager g = GridManager.Instance;
        if (fp == null || g == null || BuildManager.Instance == null || BuildManager.Instance.buildables == null) return null;
        string name = g.GetOccupied(fp.origin.x, fp.origin.y);
        if (string.IsNullOrEmpty(name)) return null;
        foreach (BuildableData d in BuildManager.Instance.buildables)
            if (d != null && d.displayName == name) return d;
        return null;
    }

    static bool IsWall(BuildableData d)
    {
        if (d == null) return false;
        BuildableData wall = CampZone.WallData();
        return d == wall || d.id == "wall";
    }

    // 敌人够到这段墙要多近（墙的半宽）
    public static float Reach(GameObject go)
    {
        BuildingFootprint fp = go != null ? go.GetComponent<BuildingFootprint>() : null;
        GridManager g = GridManager.Instance;
        if (fp == null || g == null) return 0.5f;
        return Mathf.Max(fp.size.x, fp.size.y) * g.cellSize * 0.5f;
    }

    // 从 from 往 toward 走，挡在路上（radius 以内、偏前方）的那段墙。只找建好的墙。
    public static GameObject FindBlocking(Vector3 from, Vector3 toward, float radius)
    {
        Vector2 dir = toward - from;
        if (dir.sqrMagnitude > 0.0001f) dir.Normalize();
        BuildingFootprint best = null;
        float bestScore = float.MaxValue;
        foreach (BuildingFootprint fp in BuildingFootprint.All)
        {
            if (fp == null || !fp.gameObject.activeInHierarchy) continue;
            if (fp.GetComponent<ConstructionSite>() != null) continue;   // 还没建好的规划区
            if (fp.GetComponent<NavMeshModifier>() == null) continue;    // 不挡路
            if (!IsWall(DataOf(fp))) continue;                           // 只拆墙
            Vector2 c = fp.transform.position;
            float dist = Vector2.Distance(from, c);
            if (dist > radius) continue;
            float ahead = Vector2.Dot(c - (Vector2)from, dir);
            float score = dist - ahead * 0.6f;   // 越近、越在前进方向上越优先
            if (score < bestScore) { bestScore = score; best = fp; }
        }
        return best != null ? best.gameObject : null;
    }
}
