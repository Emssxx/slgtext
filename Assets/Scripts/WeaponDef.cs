using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// WeaponDef —— 一把枪的数据（枪自己的属性）。说明见 RigWeapons.cs。
//   【改数值】菜单 SLG → 武器 → 生成武器数值文件：在 Assets/Resources/Weapons/ 生成每把枪一个文件（aba_pistol / aba_rifle / suit_pistol），
//     选中文件就能在 Inspector 里改；有文件就用文件，没有才用代码里的默认值。运行时改也马上生效，而且退出运行后还保留。
//     改乱了：文件右上角 ⋮ → "恢复代码里的默认值"。
//   【新枪】Project 窗口右键 → Create → SLG → 武器（枪械动画），文件名写武器 id，放进 Assets/Resources/Weapons/。
//   （Unity 要求 ScriptableObject 的类名和文件名一样，所以单独一个文件）
// ============================================================================
[CreateAssetMenu(menuName = "SLG/武器（枪械动画）", fileName = "Weapon")]
public class WeaponDef : ScriptableObject
{
    [Header("是什么")]
    public string id = "";
    public HoldType hold = HoldType.Pistol;
    public string artFolder = "NpcArt2";                     // 图在 Resources 下哪个文件夹

    [Header("位置（武器 PSD 坐标，原图像素）")]
    public Vector2 grip;                                     // 握把：整把枪绕它转，持枪主手在这里
    public Vector2 muzzle;                                   // 枪口（子弹、枪火从这里出来）
    public Vector2 restGrip;                                 // 端枪时握把相对脚底中心在哪（原图像素；角色没给就用这个）
    public Vector2 shoulder;                                 // 肩膀（枪绕它转，相对脚底中心，单位；角色没给就用这个）
    public List<WeaponPart> parts = new List<WeaponPart>();

    [Header("开火手感")]
    public float rpm = 400f;                                 // 连发射速（发 / 分钟）
    public Vector2 recoilKick;                               // 每枪枪口上扬多少度（随机，两个数之间）
    public Vector2 recoilHandX;                              // 每枪枪往后顶多远（沿枪的方向，随机）
    public Vector2 recoilHandY;                              // 每枪枪往上抬多高（随机）
    public float handRecoilRise = 0.03f;                     // 被顶回去用几秒（越小越猛）
    public Vector2 handRecoilReturn;                         // 回正的快慢（越大越慢，随机）
    public Vector2 muzzleRise;                               // 枪口上扬用几秒（随机）
    public float muzzleReturn = 0.09f;                       // 枪口回正的快慢
    public float recoilLean = 3f;                            // 每枪身体往后仰多少度
    public float actionShift = 0.012f;                       // 每枪身体往后挪多远
    public float kickBack = 0.03f, kickHold = 0.03f, kickReturn = 0.15f;   // 滑套 / 拉栓：往后弹几秒、停几秒、回来几秒

    [Header("枪火 / 子弹")]
    public float flashTime = 0.07f;
    public float flashSize = 1f;
    public string[] flashSprites = new string[0];            // 枪火图（随机一张）
    public string bulletSprite = "";                         // 子弹图

    [Header("枪上的手电筒（枪的属性）")]
    public bool hasLamp = true;
    public Vector2 lamp;                                     // 灯头（武器 PSD 坐标）
    public bool lightOn = true;
    public Color lightColor = new Color(0.85f, 0.93f, 1f);
    public float lightIntensity = 1f;
    public float lightRange = 5f;
    public float lightAngle = 35f;
    public float lightGlowSize = 0.6f;                       // 灯头本身发光点多大
    public bool flickerOnDraw = false;                       // 刚打开时闪一下

    [Header("专用动画类（空 = 通用；填类名，比如 LayeredFlashGun）")]
    public string animator = "";                             // 这把枪的动画类（继承 CharacterRigBase.GunAnimator）

    [Header("多层枪火（LayeredFlashGun 用：闪光 / 亮光 / 烟）")]
    public float sparkTime = 0.05f;                          // 闪光（火花线）亮多久（秒）
    public float glowTime = 0.06f;                           // 亮光亮多久（秒）
    public bool smoke = false;                               // 开枪后冒烟
    public float smokeTime = 0.6f;                           // 烟飘多久（秒）

    [Header("换弹（枪的属性：弹匣怎么拔、怎么对准、怎么插；从哪拿新弹匣由角色身上的配件决定）")]
    public ReloadMotion reloadMotion = ReloadMotion.Pistol;  // 换弹动作：手枪式 / 步枪式
    public string magPart = "";                              // 枪里的弹匣是哪个部件（它的轴心 = 弹匣底部）
    public string reloadHand = "";                           // 换弹时离开枪去拿弹匣的那只手（部件名；换弹手就用这张持枪手的图）
    public Vector2 magDir = new Vector2(0f, 1f);             // 弹匣插进去的方向（枪的本地坐标，沿弹匣往上）
    public Vector2 magInHand = new Vector2(0f, 0.012f);      // 弹匣底部在换弹手里的位置（单位）
    public float magReady = 0.085f;                          // 对准时弹匣还差多远插到底（单位）

    public float ShotInterval => 60f / Mathf.Max(30f, rpm);

    // 按名字找部件的数据（没有 = null）
    public WeaponPart PartDef(string partName)
    {
        foreach (WeaponPart p in parts) if (p != null && p.name == partName) return p;
        return null;
    }

    // ============ 武器库 ============
    static readonly Dictionary<string, WeaponDef> _cache = new Dictionary<string, WeaponDef>();
    public static readonly string[] BuiltInIds = { "aba_pistol", "aba_rifle", "suit_pistol" };   // 代码里有默认值的枪

    // 恢复成代码里的默认值（只改这个文件，id / 文件名不变）
    [ContextMenu("恢复代码里的默认值")]
    void ResetToBuiltIn()
    {
        WeaponDef b = BuiltIn(id, false);
        if (b == null) { Debug.LogWarning("[WeaponDef] 代码里没有这把枪的默认值：" + id); return; }
        string keep = name;
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(b), this);
        name = keep;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        if (Application.isPlaying) Destroy(b); else DestroyImmediate(b);
    }

#if UNITY_EDITOR
    // 菜单：把代码里的默认武器生成成数值文件（已经有的不覆盖）
    [UnityEditor.MenuItem("SLG/武器/生成武器数值文件")]
    static void CreateWeaponAssets()
    {
        const string dir = "Assets/Resources/Weapons";
        if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Resources")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Resources");
        if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Resources", "Weapons");
        int made = 0;
        foreach (string wid in BuiltInIds)
        {
            string path = dir + "/" + wid + ".asset";
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponDef>(path) != null) continue;   // 已经有了：不覆盖你改过的
            WeaponDef d = BuiltIn(wid, false);
            if (d == null) continue;
            UnityEditor.AssetDatabase.CreateAsset(d, path);
            made++;
        }
        UnityEditor.AssetDatabase.SaveAssets();
        _cache.Clear();                                                    // 运行中生成的话，下次拿枪就读新文件
        UnityEditor.EditorGUIUtility.PingObject(UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(dir));
        Debug.Log("[WeaponDef] 生成了 " + made + " 个武器数值文件（" + dir + "）");
    }
#endif

    // 按 id 拿一把枪：Resources/Weapons/<id> 有文件就用文件，没有就用代码里的默认值
    public static WeaponDef Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_cache.TryGetValue(id, out WeaponDef d) && d != null) return d;
        d = Resources.Load<WeaponDef>("Weapons/" + id);
        if (d != null && string.IsNullOrEmpty(d.magPart)) FillReload(d, BuiltIn(id, false));   // 以前建的资源文件还没有"换弹"一栏：先用代码里的默认值
        if (d == null) d = BuiltIn(id);
        if (d != null) _cache[id] = d;
        return d;
    }

    // 把 from 的"换弹"一栏抄给 to（from = null 就不动）
    static void FillReload(WeaponDef to, WeaponDef from)
    {
        if (from == null) return;
        to.reloadMotion = from.reloadMotion; to.magPart = from.magPart; to.reloadHand = from.reloadHand;
        to.magDir = from.magDir; to.magInHand = from.magInHand; to.magReady = from.magReady;
    }

    static WeaponPart P(string name, string path, float x, float y, int order, float kick = 0f, bool hand = false)
        => new WeaponPart { name = name, path = path, pivot = new Vector2(x, y), order = order, kick = kick, hand = hand };

    // 代码里的默认武器（数值照搬现在的手感）
    static WeaponDef BuiltIn(string id, bool warn = true)
    {
        WeaponDef d = CreateInstance<WeaponDef>();
        d.id = id;
        d.name = id;
        switch (id)
        {
            case "aba_pistol":   // ABA 战术手枪（npc2 的图）
                d.hold = HoldType.Pistol;
                d.grip = new Vector2(1792, 1085); d.muzzle = new Vector2(2061, 855); d.lamp = new Vector2(2056, 962);
                d.restGrip = new Vector2(1792 - 1161, 1085 - 1988);
                d.shoulder = new Vector2(0.175f, 0.466f);
                d.parts.Add(P("PistolMag", "Weapons/PistolMag", 1699, 1144, 40));
                d.parts.Add(P("PistolSlide", "Weapons/PistolSlide", 1858, 870, 41, 150f));
                d.parts.Add(P("PistolBody", "Weapons/PistolBody", 1852, 981, 42));
                d.parts.Add(P("PistolHandL", "Weapons/PistolHandL", 1720, 1036, 43, 0f, true));
                d.parts.Add(P("PistolHandR", "Weapons/PistolHandR", 1792, 1085, 44, 0f, true));
                d.rpm = 300f;
                d.recoilKick = new Vector2(50f, 50f); d.recoilHandX = new Vector2(-0.05f, -0.1f); d.recoilHandY = new Vector2(0.09f, 0.18f);
                d.handRecoilRise = 0.03f; d.handRecoilReturn = new Vector2(0.09f, 0.24f); d.muzzleRise = new Vector2(0.09f, 0.15f); d.muzzleReturn = 0.09f;
                d.recoilLean = 3f; d.actionShift = 0.012f;
                d.kickBack = 0.03f; d.kickHold = 0.03f; d.kickReturn = 0.15f;
                d.flashTime = 0.07f; d.flashSize = 1f;
                d.flashSprites = new[] { "Weapons/PistolFlash1", "Weapons/PistolFlash2" };
                d.bulletSprite = "Weapons/PistolBullet";
                d.reloadMotion = ReloadMotion.Pistol; d.magPart = "PistolMag"; d.reloadHand = "PistolHandR";
                d.magDir = new Vector2(0.262f, 0.965f); d.magInHand = new Vector2(0f, 0.012f); d.magReady = 0.085f;
                return d;
            case "aba_rifle":    // ABA 战术步枪（npc2 的图）
                d.hold = HoldType.Rifle;
                d.grip = new Vector2(1430, 1135); d.muzzle = new Vector2(2444, 892); d.lamp = new Vector2(2276, 1018);
                d.restGrip = new Vector2(1430 - 1161, 1135 - 1988);
                d.shoulder = new Vector2(-0.083f, 0.51f);
                d.parts.Add(P("RifleInner", "Weapons/RifleInner", 1782, 916, 39));
                d.parts.Add(P("RifleCharger", "Weapons/RifleCharger", 1768, 908, 40, 120f));
                d.parts.Add(P("RifleBolt", "Weapons/RifleBolt", 1526, 908, 41, 0f));
                d.parts.Add(P("RifleMag", "Weapons/RifleMag", 1860, 1343, 42));
                d.parts.Add(P("RifleBody", "Weapons/RifleBody", 1883, 990, 43));
                d.parts.Add(P("RifleCover", "Weapons/RifleCover", 1786, 960, 44));
                d.parts.Add(P("RifleStock", "Weapons/RifleStock", 1184, 1075, 45));
                d.parts.Add(P("RifleHandL", "Weapons/RifleHandL", 1430, 1135, 46, 0f, true));
                d.parts.Add(P("RifleHandR", "Weapons/RifleHandR", 1958, 1066, 47, 0f, true));
                d.rpm = 400f;
                d.recoilKick = new Vector2(5.28f, 1.65f); d.recoilHandX = new Vector2(-0.06f, -0.035f); d.recoilHandY = new Vector2(0.005f, 0.02f);
                d.handRecoilRise = 0.02f; d.handRecoilReturn = new Vector2(0.06f, 0.09f); d.muzzleRise = new Vector2(0.02f, 0.04f); d.muzzleReturn = 0.07f;
                d.recoilLean = 1.5f; d.actionShift = 0.01f;
                d.kickBack = 0.02f; d.kickHold = 0f; d.kickReturn = 0.05f;
                d.flashTime = 0.05f; d.flashSize = 2f;
                d.flashSprites = new[] { "Weapons/RifleFlash1", "Weapons/RifleFlash2" };
                d.bulletSprite = "Weapons/RifleBullet";
                d.flickerOnDraw = true;
                d.reloadMotion = ReloadMotion.Rifle; d.magPart = "RifleMag"; d.reloadHand = "RifleHandR";
                d.magDir = new Vector2(-0.102f, 0.995f); d.magInHand = new Vector2(0f, -0.04f); d.magReady = 0.1f;
                return d;
            case "suit_pistol":  // 防护服手枪（npc1 的图；部件本来就拆好了：弹匣 / 枪身 / 滑套 / 两只持枪手）
                d.hold = HoldType.Pistol;
                d.artFolder = "NpcArt";
                d.grip = new Vector2(1697, 1172); d.muzzle = new Vector2(1914, 979);
                d.restGrip = new Vector2(1697 - 1162, 1172 - 1987);
                d.shoulder = new Vector2(0.175f, 0.421f);   // = npc1 的 Aim Shoulder（握把位置和原来一模一样）
                d.hasLamp = false;
                d.rpm = 300f;
                d.recoilKick = new Vector2(80f, 100f); d.recoilHandX = new Vector2(-0.4f, -0.2f); d.recoilHandY = new Vector2(0.1f, 0.3f);
                d.handRecoilRise = 0.03f; d.handRecoilReturn = new Vector2(0.18f, 0.24f); d.muzzleRise = new Vector2(0.03f, 0.15f); d.muzzleReturn = 0.09f;
                d.recoilLean = 3f; d.actionShift = 0.012f;
                d.kickBack = 0.025f; d.kickHold = 0f; d.kickReturn = 0.12f;
                d.flashTime = 0.07f; d.flashSize = 2f;
                d.animator = "LayeredFlashGun";             // 三层枪火 + 烟
                d.sparkTime = 0.05f; d.glowTime = 0.06f; d.smoke = true; d.smokeTime = 0.6f;
                d.bulletSprite = "";                        // 空 = 角色默认的子弹图
                d.parts.Add(P("PistolMag", "Held/PistolMag", 1610, 1222, 25));
                d.parts.Add(P("PistolFrame", "Held/PistolFrame", 1697, 1172, 26));
                d.parts.Add(P("PistolSlide", "Held/PistolSlide", 1729.5f, 981.5f, 27, 150f));
                d.parts.Add(P("PistolLeftHand", "Held/PistolLeftHand", 1697, 1172, 28, 0f, true));
                d.parts.Add(P("PistolRightHand", "Held/PistolRightHand", 1697, 1172, 29, 0f, true));
                d.reloadMotion = ReloadMotion.Pistol; d.magPart = "PistolMag"; d.reloadHand = "PistolRightHand";
                d.magDir = new Vector2(0.135f, 0.991f); d.magInHand = new Vector2(0f, 0.012f); d.magReady = 0.085f;
                return d;
        }
        if (warn) Debug.LogWarning("[WeaponDef] 没有这把武器：" + id);
        return null;
    }
}
