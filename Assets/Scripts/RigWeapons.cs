using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 枪械动画（和角色分开）：一把枪 = 一份 WeaponDef（数据）+ 拿在手上时拼出来的 GunRig（场景里的部件）。
//
// 【枪自己的属性】部件图和位置、握把、枪口、开火手感（后坐 / 枪口上扬 / 滑套或拉栓）、枪火、子弹、
//   枪上的手电筒（灯头位置、颜色、亮度、照多远、光束多宽）。换了枪这些全跟着换。
// 【角色给的】肩膀在哪（枪绕它转）、端枪时握把在身上的位置、持枪手的图（角色自己画的手；没画就用枪自带的）、
//   持枪手的类型（GripStyle：稳 / 颤抖……）。
// 【调数值】在 Project 窗口右键 → Create → SLG → 武器（枪械动画），文件名写武器 id（比如 aba_rifle），
//   放进 Assets/Resources/Weapons/ —— 有这个文件就用它（Inspector 里改），没有就用下面代码里的默认值。
// ============================================================================
// 武器种类：枪（有动画、部件）/ 近战（斧头、锤子……只有数值）
public enum WeaponKind
{
    [InspectorName("枪")] Gun,
    [InspectorName("近战")] Melee,
}

public enum HoldType
{
    [InspectorName("手枪")] Pistol,
    [InspectorName("步枪")] Rifle,
}

// 换弹动作（枪决定）：手枪式 = 换弹手从握把下面把弹匣插进握把；步枪式 = 前手换弹匣，枪口抬高、拉栓组弹回
public enum ReloadMotion
{
    [InspectorName("手枪式")] Pistol,
    [InspectorName("步枪式")] Rifle,
    [InspectorName("无托步枪式")] Bullpup,   // 弹匣在握把后面（CharacterRigBase.Bullpup.cs）
    [InspectorName("AK式")] AK,
    [InspectorName("拉栓式")] Bolt,         // 拉栓枪：左手开栓 / 关栓，换完弹匣拉一次栓（CharacterRigBase.BoltReload.cs）             // AK 系列：弹匣绕"弹匣挂载旋转点"摇进 / 摇出，左手拉栓（CharacterRigBase.AK.cs）
}

[System.Serializable]
public class WeaponPart
{
    public string name;         // 部件名（换弹 / 取出动画按名字找它，比如 PistolSlide、RifleMag）
    public string path;         // 图（素材文件夹下的路径）
    public Vector2 pivot;       // 轴心（武器 PSD 坐标）
    public int order;           // 前后顺序（在角色的 SortingGroup 里）
    public float kick;          // 开枪时沿枪身往后弹多远（原图像素；0 = 不动。滑套 / 上弹器 / 拉栓）
    public bool hand;           // 这是持枪手（角色画了自己的手就换成角色的图）
}

// 拿在手上的一把枪（场景里拼出来的部件）
public class GunRig
{
    public WeaponDef def;
    public Transform root;                       // 整把枪（挂在手点下面，原点 = 握把）
    public Transform muzzle;                     // 枪口
    public Transform lamp;                       // 手电筒灯头（没有灯 = null）
    public SpriteRenderer lampGlow;              // 灯头发光点
    public Transform laser;                      // 镭射发射点（没有镭射 = null）
    public SpriteRenderer laserGlow;             // 镭射发射点那一小团光
    public readonly List<SpriteRenderer> parts = new List<SpriteRenderer>();
    readonly Dictionary<string, SpriteRenderer> _byName = new Dictionary<string, SpriteRenderer>();
    public readonly List<Transform> movers = new List<Transform>();     // 开枪时往后弹的部件
    public readonly List<Vector3> moverBase = new List<Vector3>();
    public readonly List<float> moverKick = new List<float>();

    public void Add(WeaponPart p, SpriteRenderer sr)
    {
        parts.Add(sr);
        _byName[p.name] = sr;
        if (p.kick != 0f || p.name.Contains("Slide") || p.name.Contains("Charger") || p.name.Contains("Bolt"))
        {
            movers.Add(sr.transform); moverBase.Add(sr.transform.localPosition); moverKick.Add(p.kick);
        }
    }

    public SpriteRenderer Part(string name) => name != null && _byName.TryGetValue(name, out SpriteRenderer s) ? s : null;

    // 显示 / 藏起整把枪（灯头发光点另外由灯光控制）
    public void Show(bool on)
    {
        foreach (SpriteRenderer sr in parts) if (sr != null) sr.enabled = on;
        if (!on && lampGlow != null) lampGlow.enabled = false;
        if (!on && laserGlow != null) laserGlow.enabled = false;
    }

    // 滑套 / 上弹器 / 拉栓按进度 s（0 = 原位，1 = 最后面）往后弹
    public void Kick(float s, float ppu)
    {
        for (int i = 0; i < movers.Count; i++)
            if (movers[i] != null) movers[i].localPosition = moverBase[i] + new Vector3(-moverKick[i] * s / ppu, 0f, 0f);
    }
}

// ============================================================================
// 持枪手的类型（特殊持枪手单独一类）：在端枪的位置上再加一点偏移 / 角度
//   稳（Steady）：不加；颤抖（Trembling）：用平滑噪声小幅乱抖（紧张、武器等级低）。以后的受伤单手持枪等也加在这里。
// ============================================================================
// 持枪手类型（角色的属性，和枪无关）：稳 / 颤抖……以后可以加更多（比如单手、受伤的手）
//   Offset：持枪时握把多挪多少；Shake：按指定幅度抖（颤抖换弹用）；Fumbles：换弹会不会手忙脚乱（颤抖换弹：掉弹匣、插歪）
public abstract class GripStyle
{
    public static readonly GripStyle Steady = new SteadyGrip();

    // time = 角色自己的时间；返回握把要多挪多少（单位），ang = 多转多少度
    public abstract Vector2 Offset(float time, out float ang);
    // 按幅度 k 抖（seed 不同 = 各抖各的；稳的手不抖）
    public virtual Vector2 Shake(float time, float seed, float k, out float ang) { ang = 0f; return Vector2.zero; }
    // 换弹会不会手忙脚乱（战术 / 普通换弹变成颤抖换弹）
    public virtual bool Fumbles => false;
}

public class SteadyGrip : GripStyle
{
    public override Vector2 Offset(float time, out float ang) { ang = 0f; return Vector2.zero; }
}

// 颤抖的手：平滑噪声让握把一直小幅乱抖；抖到 fumbleAt 以上换弹就手忙脚乱
public class TremblingGrip : GripStyle
{
    public float amount;        // 现在抖多厉害（0~1；角色每帧按紧张 / 武器等级设）
    public float freq = 7f;     // 抖的快慢
    public float move = 0.01f;  // 最多挪多远（单位，乘幅度）
    public float angle = 4f;    // 最多转多少度（乘幅度）
    public float seed;
    public float fumbleAt = 0.5f;   // 抖到这个程度换弹就手忙脚乱（>1 = 永远不会）

    public override Vector2 Offset(float time, out float ang) { return Shake(time, seed, amount, out ang); }

    public override Vector2 Shake(float time, float seed, float k, out float ang)
    {
        float ts = time * freq;
        float nx = Mathf.PerlinNoise(seed + 0.3f, ts) * 2f - 1f;
        float ny = Mathf.PerlinNoise(seed + 5.1f, ts * 1.13f) * 2f - 1f;
        float na = Mathf.PerlinNoise(seed + 9.7f, ts * 0.91f) * 2f - 1f;
        ang = na * angle * k;
        return new Vector2(nx, ny) * (move * k);
    }

    public override bool Fumbles => amount > 0f && amount >= fumbleAt;
}
