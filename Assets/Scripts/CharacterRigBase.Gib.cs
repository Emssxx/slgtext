using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;   // SortingGroup：炸飞的每一块自己当一个整体参与前后遮挡

// ============================================================================
// 尸体爆炸（CharacterRigBase 的一部分，所有拼装角色共用：NPC、ABA 队员、怪物）
//   一团放大的血雾盖住整个人 / 怪 → 部件全部拆开往外炸飞（人 = 手、脚、躯干、头、挂件、枪；怪 = 所有零件），
//   飞的路上一路滴血、落地溅一滩，最后躺在地上。炸完原来的角色藏起来。
//   什么时候炸（Gibs.cs）：被手雷炸死 / 尸体被手雷炸到 / 过量伤害打死；测试：勾 Test Gib（动画测试里的「尸体爆炸」按钮）。
//   测试里点「复活」：零件全部收回来，人重新站好。
//   哪些部件算一块：默认 = 根节点下面每一个子物体（脚、拿东西的手点……）+ 身体下面每一个子物体（手、躯干、头、挂件……）；
//     角色可以改（CollectGibPieces，比如蜘蛛的大腿和小腿分开飞）。
// ============================================================================
public abstract partial class CharacterRigBase
{
    [Header("尸体爆炸（速度 = 单位 / 秒，按体型放大）")]
    public bool testGib;                                       // 勾一下：尸体爆炸（站着的会先倒下再炸；自动取消勾选）
    public float gibUp = 3.2f;                                 // 零件往上飞的初速度
    public float gibOut = 1.8f;                                // 零件往外飞的初速度
    public float gibSpin = 720f;                               // 零件最多每秒转几度

    static readonly List<CharacterRigBase> _corpses = new List<CharacterRigBase>();   // 倒在地上、还没炸的尸体（手雷炸尸体用）
    protected bool _gibbed;                                    // 已经炸了
    public bool IsGibbed => _gibbed;

    // 一块零件原来挂在哪（复活时收回来）
    struct GibRec
    {
        public Transform t, parent;
        public Vector3 pos, scale;
        public Quaternion rot;
        public int sibling;
    }
    readonly List<GibRec> _gibRecs = new List<GibRec>();
    readonly List<SpriteRenderer> _gibHidden = new List<SpriteRenderer>();   // 炸的时候顺便藏起来的（脚下的影子）

    // 现在倒在地上、还没炸的尸体（删掉了的自动去掉）
    public static List<CharacterRigBase> Corpses()
    {
        _corpses.RemoveAll(r => r == null || !r._dead || r._gibbed);
        return new List<CharacterRigBase>(_corpses);
    }

    // 倒下时记一下（PlayDeath 调）
    protected void NoteCorpse() { if (!_corpses.Contains(this)) _corpses.Add(this); }

    // 炸：dir = 冲击从哪边来（往这个方向炸得多一点），power = 多猛（1 = 普通，手雷正中间更猛）
    public void Gib(Vector2 dir, float power = 1f)
    {
        if (!_built || _gibbed || _rig == null) return;
        if (!_dead) PlayDeath(dir, 1f, HitPoint(dir), false);          // 还站着：先算倒下（死亡状态、武器脱手）
        if (!_dead) return;
        Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.zero;
        float k = Mathf.Clamp(Mathf.Abs(_rig.lossyScale.y), 0.3f, 4f);     // 体型（蜘蛛幼体 0.5 / 变异体 3）：飞得远近、血多少都按它
        float groundY = transform.TransformPoint(new Vector3(0f, FEET_Y, 0f)).y;   // 地面（脚底）

        // 整只的范围：血雾要盖住它
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool has = false;
        foreach (SpriteRenderer sr in _rig.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!sr.enabled || sr.sprite == null) continue;
            if (!has) { b = sr.bounds; has = true; } else b.Encapsulate(sr.bounds);
        }
        SortingGroup grp = GetComponent<SortingGroup>();
        int layer = grp != null ? grp.sortingLayerID : 0, order = grp != null ? grp.sortingOrder : 0;
        Gibs.Burst(b, d, layer, groundY, k);                                  // 血雾 + 地上炸开一大摊血

        // 拆零件
        List<Transform> pieces = new List<Transform>();
        CollectGibPieces(pieces);
        _gibRecs.Clear();
        foreach (Transform t in pieces)
        {
            if (t == null) continue;
            _gibRecs.Add(new GibRec { t = t, parent = t.parent, pos = t.localPosition, rot = t.localRotation, scale = t.localScale, sibling = t.GetSiblingIndex() });
            t.SetParent(null, true);                                          // 不挂在角色身上了：世界里自己飞
            SortingGroup sg = t.gameObject.AddComponent<SortingGroup>();      // 这一块自己是一个整体（里面的前后顺序不变）
            sg.sortingLayerID = layer; sg.sortingOrder = order;
            GibPiece.Launch(t, this, groundY, d, power, k);
        }
        _rig.gameObject.SetActive(false);                                     // 剩下的（藏着的部件）一起藏起来
        _gibHidden.Clear();
        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>())   // 脚下的影子：人没了影子也没了
            if (sr.enabled && sr.name == "GroundShadow") { sr.enabled = false; _gibHidden.Add(sr); }
        _gibbed = true;
        _corpses.Remove(this);
    }

    // 哪些部件算一块：根节点下面的（身体除外）+ 身体下面的；看不见的不要
    protected virtual void CollectGibPieces(List<Transform> list)
    {
        foreach (Transform c in _rig) if (c != _body) list.Add(c);
        if (_body != null) foreach (Transform c in _body) list.Add(c);
        list.RemoveAll(t => !HasVisible(t));
    }

    // 这一块（连同子物体）有没有看得见的图
    protected static bool HasVisible(Transform t)
    {
        foreach (SpriteRenderer sr in t.GetComponentsInChildren<SpriteRenderer>(false))
            if (sr.enabled && sr.sprite != null && sr.color.a > 0.02f) return true;
        return false;
    }

    // 收回来（测试里复活时）：零件挂回原处、摆回原来的位置，角色重新显示
    void Ungib()
    {
        if (!_gibbed) return;
        foreach (GibRec r in _gibRecs)
        {
            if (r.t == null) continue;                                        // 已经淡出删掉了：收不回来
            GibPiece gp = r.t.GetComponent<GibPiece>();
            if (gp != null) { gp.enabled = false; gp.RestoreAlpha(); Destroy(gp); }   // 先停掉（这一帧不再动它）
            SortingGroup sg = r.t.GetComponent<SortingGroup>();
            if (sg != null) Destroy(sg);
            r.t.SetParent(r.parent, false);
            r.t.SetSiblingIndex(r.sibling);
            r.t.localPosition = r.pos; r.t.localRotation = r.rot; r.t.localScale = r.scale;
        }
        _gibRecs.Clear();
        foreach (SpriteRenderer sr in _gibHidden) if (sr != null) sr.enabled = true;
        _gibHidden.Clear();
        _rig.gameObject.SetActive(true);
        _gibbed = false;
    }
}
