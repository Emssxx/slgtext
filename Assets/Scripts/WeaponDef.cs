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
    public string displayName = "";                          // 显示名（测试窗口里用；空 = 用 id）
    public HoldType hold = HoldType.Pistol;
    public string artFolder = "NpcArt2";                     // 图在 Resources 下哪个文件夹

    [Header("位置（武器 PSD 坐标，原图像素）")]
    public Vector2 grip;                                     // 握把：整把枪绕它转，持枪主手在这里
    // 持枪手的锚点 = 手部件的轴心 = 图上十字线的中心；角色衣服上每只手的图，轴心都设在手心中心（十字对十字）
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

    [Header("镭射瞄准线（枪的属性：从镭射十字沿枪管方向射出一条激光）")]
    public bool hasLaser = false;
    public Vector2 laser;                                    // 镭射发射点（武器 PSD 坐标）
    public Color laserColor = new Color(1f, 0.12f, 0.1f, 0.75f);   // 颜色（透明度 = 起点的亮度，越远越淡）
    public float laserWidth = 0.005f;                        // 粗细（单位）
    public float laserLength = 6f;                           // 多长（单位；末端淡出）
    public float laserDotSize = 0.25f;                       // 发射点那一小团光多大（0 = 不要）
    public float laserLight = 0f;                            // 激光沿线的一点点光照亮度（照亮周围；0 = 不要）
    public float laserLightRadius = 0.35f;                   // 每一团光照多大（单位）

    [Header("专用动画类（空 = 通用；填类名，比如 LayeredFlashGun）")]
    public string animator = "";                             // 这把枪的动画类（继承 CharacterRigBase.GunAnimator）

    [Header("从胸挂上取出（npc2 的取出动画用；坐标 = 武器 PSD 原图像素）")]
    public Vector2 drawGrab;                                 // 右手抓枪的地方（枪上边，手倒过来抓）
    public string stockPart = "";                            // 能拉开的枪托部件（空 = 没有，比如无托步枪：跳过拉枪托）
    public Vector2 stockGrab;                                // 左手抓枪托的哪里
    public float stockCollapse = 120f;                       // 枪托一开始收起来多少（原图像素）
    public string mountArt = "";                             // 挂在胸挂上时的整枪图（空 = 用角色自己画的）
    public Vector2 mountCenter;                              // 整枪图的中心（轴心）

    [Header("挂在身上的图（Resources 路径，空 = 没有）")]
    public bool characterMountArt = false;                   // 胸挂 / 斜挂用角色自己画的那张（战斗步枪）；false = 用枪的部件直接拼（不缩小，和拿在手上一样大）
    public string holsterArt = "";                           // 插在枪套里（npc1 防护服的武器挂载点）
    public string slingArt = "";                             // 斜挂在身上（npc1 的步枪挂载点；和 npc2 胸挂上的步枪一样斜）

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

    [Header("战斗数值（枪的属性：谁拿都一样；人的手感——等级、紧张、体力——由开枪的人再乘）")]
    public int magSize = 12;                                 // 一个弹匣几发
    public float damage = 15f;                               // 每发伤害
    public float spread = 2f;                                // 散布 σ（度；5 级手感、站着冷静时）
    public float movingMul = 1.3f;                           // 边走边打散布倍数
    public float recoilPerShot = 0.45f;                      // 每开一枪散布加多少度
    public float recoilRecover = 8f;                         // 每秒恢复多少度（压枪）
    public float leadError = 0.06f;                          // 预判误差（0.06 = ±6%）
    public float bulletSpeed = 150f;                         // 子弹速度（单位 / 秒）
    public float burstPause = 0.3f;                          // 两组点射之间停多久（秒）
    public float range = 8f;                                 // 有效射程（单位）
    public float knockback = 0f;                             // 击退（打中时把目标往后推的初速度；0 = 没有）
    [Range(0f, 1f)] public float critChance = 0.1f;          // 打中要害的几率
    public float critMul = 1.8f;                             // 要害伤害倍数
    public float speedReloadTime = 1.6f;                     // 快速换弹几秒（游戏规则用；动画会压到这个时间）
    public float tacticalReloadTime = 2.3f;                  // 战术换弹几秒

    [Header("瞄准配件（枪的属性）：可变倍率瞄准镜 / 镭射指示")]
    public float scopeMax = 1f;                              // 瞄准镜最高几倍（1 = 没有瞄准镜）
    public float scopeNear = 3f;                             // 这个距离以内用 1 倍
    public float scopeFar = 8f;                              // 这个距离以外用最高倍
    public float scopeGain = 0.25f;                          // 每多 1 倍，散布除以 (1 + 这个)
    public float laserAim = 1f;                              // 镭射指示：边走边打散布再乘这个（< 1 = 更准；1 = 没效果）

    public float ShotInterval => 60f / Mathf.Max(30f, rpm);

    // 这个距离上瞄准镜几倍
    public float ScopeAt(float dist) => scopeMax <= 1f ? 1f : Mathf.Lerp(1f, scopeMax, Mathf.InverseLerp(scopeNear, scopeFar, dist));
    // 枪的配件让散布变成几倍（瞄准镜：越远越准，倍率高时边走边打难一点；镭射：边走边打更准）。谁开枪都乘这个
    public float AimSpreadMul(float dist, bool moving)
    {
        float zoom = ScopeAt(dist);
        float m = 1f / (1f + (zoom - 1f) * scopeGain);
        if (moving) m *= laserAim * (1f + (zoom - 1f) * 0.1f);
        return m;
    }
    // 子弹图的完整 Resources 路径（没有 = 空）
    public string BulletPath => string.IsNullOrEmpty(bulletSprite) ? "" : artFolder + "/" + bulletSprite;

    // 按名字找部件的数据（没有 = null）
    public WeaponPart PartDef(string partName)
    {
        foreach (WeaponPart p in parts) if (p != null && p.name == partName) return p;
        return null;
    }

    // ============ 武器库 ============
    static readonly Dictionary<string, WeaponDef> _cache = new Dictionary<string, WeaponDef>();
    public static readonly string[] BuiltInIds = { "aba_pistol", "aba_rifle", "suit_pistol", "aba-x95", "aba-ar15" };   // 代码里有默认值的枪

    public string Label => string.IsNullOrEmpty(displayName) ? id : displayName;

    // 所有枪：代码里的 + Resources/Weapons 里的文件（测试窗口列出来）
    public static List<WeaponDef> All()
    {
        List<WeaponDef> list = new List<WeaponDef>();
        HashSet<string> seen = new HashSet<string>();
        foreach (string wid in BuiltInIds) { WeaponDef d = Get(wid); if (d != null && seen.Add(d.id)) list.Add(d); }
        foreach (WeaponDef f in Resources.LoadAll<WeaponDef>("Weapons"))
        {
            if (f == null) continue;
            string wid = string.IsNullOrEmpty(f.id) ? f.name : f.id;
            if (seen.Add(wid)) { WeaponDef d = Get(wid); if (d != null) list.Add(d); }
        }
        return list;
    }

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
        if (d != null && d.drawGrab == Vector2.zero && d.mountCenter == Vector2.zero) FillDraw(d, BuiltIn(id, false));   // 还没有"取出"一栏
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

    // 以前建的资源文件还没有"取出"一栏：抄代码里的默认值
    static void FillDraw(WeaponDef to, WeaponDef from)
    {
        if (from == null) return;
        to.drawGrab = from.drawGrab; to.stockPart = from.stockPart; to.stockGrab = from.stockGrab;
        to.stockCollapse = from.stockCollapse; to.mountArt = from.mountArt; to.mountCenter = from.mountCenter;
        to.holsterArt = from.holsterArt; to.slingArt = from.slingArt;
        to.characterMountArt = from.characterMountArt;
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
                d.displayName = "ABA 战术手枪";
                d.hold = HoldType.Pistol;
                d.grip = new Vector2(1792, 1085); d.muzzle = new Vector2(2061, 855); d.lamp = new Vector2(2056, 962);
                d.restGrip = new Vector2(1792 - 1161, 1085 - 1988);
                d.shoulder = new Vector2(0.175f, 0.466f);
                d.parts.Add(P("PistolMag", "Weapons/PistolMag", 1699, 1144, 40));
                d.parts.Add(P("PistolSlide", "Weapons/PistolSlide", 1858, 870, 41, 150f));
                d.parts.Add(P("PistolBody", "Weapons/PistolBody", 1852, 981, 42));
                d.parts.Add(P("PistolHandL", "Weapons/PistolHandL", 1738.3f, 1022.2f, 43, 0f, true));   // 手的锚点 = 手心中心
                d.parts.Add(P("PistolHandR", "Weapons/PistolHandR", 1785.8f, 1101.3f, 44, 0f, true));
                d.rpm = 300f;
                d.recoilKick = new Vector2(50f, 50f); d.recoilHandX = new Vector2(-0.05f, -0.1f); d.recoilHandY = new Vector2(0.09f, 0.18f);
                d.handRecoilRise = 0.03f; d.handRecoilReturn = new Vector2(0.09f, 0.24f); d.muzzleRise = new Vector2(0.09f, 0.15f); d.muzzleReturn = 0.09f;
                d.recoilLean = 3f; d.actionShift = 0.012f;
                d.kickBack = 0.03f; d.kickHold = 0.03f; d.kickReturn = 0.15f;
                d.flashTime = 0.07f; d.flashSize = 1f;
                d.flashSprites = new[] { "Weapons/PistolFlash1", "Weapons/PistolFlash2" };
                d.bulletSprite = "Weapons/PistolBullet";
                d.reloadMotion = ReloadMotion.Pistol; d.magPart = "PistolMag"; d.reloadHand = "PistolHandR";
                d.magDir = new Vector2(0.262f, 0.965f); d.magInHand = new Vector2(0.0032f, 0.0204f); d.magReady = 0.085f;   // 弹匣底部相对手心中心
                d.holsterArt = "WeaponArt/aba_pistol/Holster";
                // 战斗数值（ABA 战术手枪：子弹带击退）
                d.magSize = 12; d.damage = 24f; d.spread = 2f; d.movingMul = 1.25f; d.recoilPerShot = 0.9f; d.recoilRecover = 7f;
                d.leadError = 0.08f; d.bulletSpeed = 130f; d.burstPause = 0.3f; d.range = 6f; d.knockback = 4.5f;
                d.critChance = 0.08f; d.critMul = 1.6f; d.speedReloadTime = 1.2f; d.tacticalReloadTime = 1.8f;
                return d;
            case "aba_rifle":    // ABA 战术步枪（npc2 的图）
                d.displayName = "战斗步枪";
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
                d.parts.Add(P("RifleHandL", "Weapons/RifleHandL", 1446.3f, 1118.2f, 46, 0f, true));   // 手的锚点 = 手心中心
                d.parts.Add(P("RifleHandR", "Weapons/RifleHandR", 1951.8f, 1081.3f, 47, 0f, true));
                d.rpm = 400f;
                d.recoilKick = new Vector2(5.28f, 1.65f); d.recoilHandX = new Vector2(-0.06f, -0.035f); d.recoilHandY = new Vector2(0.005f, 0.02f);
                d.handRecoilRise = 0.02f; d.handRecoilReturn = new Vector2(0.06f, 0.09f); d.muzzleRise = new Vector2(0.02f, 0.04f); d.muzzleReturn = 0.07f;
                d.recoilLean = 1.5f; d.actionShift = 0.01f;
                d.kickBack = 0.02f; d.kickHold = 0f; d.kickReturn = 0.05f;
                d.flashTime = 0.05f; d.flashSize = 2f;
                d.flashSprites = new[] { "Weapons/RifleFlash1", "Weapons/RifleFlash2" };
                d.bulletSprite = "Weapons/RifleBullet";
                d.flickerOnDraw = true;
                d.drawGrab = new Vector2(2100f, 745f); d.stockPart = "RifleStock"; d.stockGrab = new Vector2(1000f, 1010f); d.stockCollapse = 120f;
                d.mountArt = ""; d.mountCenter = new Vector2(1713f, 1014f);   // 胸挂上用 npc2 自己画的那把
                d.characterMountArt = true;                 // 挂在身上用角色画的图（其他枪都用部件拼）
                d.reloadMotion = ReloadMotion.Rifle; d.magPart = "RifleMag"; d.reloadHand = "RifleHandR";
                d.magDir = new Vector2(-0.102f, 0.995f); d.magInHand = new Vector2(0.0032f, -0.0321f); d.magReady = 0.1f;   // 弹匣底部相对手心中心
                d.slingArt = "NpcArt2/Rifle";
                // 战斗数值（战斗步枪：弹匣大、单发重一点，散布比另外两把大）
                d.rpm = 750f;                               // 和游戏里的射速统一（原来动画 400、游戏 750）
                d.magSize = 35; d.damage = 15f; d.spread = 1.3f; d.movingMul = 1.3f; d.recoilPerShot = 0.45f; d.recoilRecover = 8f;
                d.leadError = 0.06f; d.bulletSpeed = 160f; d.burstPause = 0.35f; d.range = 9f; d.knockback = 0f;
                d.critChance = 0.12f; d.critMul = 1.8f; d.speedReloadTime = 1.7f; d.tacticalReloadTime = 2.4f;
                return d;
            case "suit_pistol":  // 防护服手枪（npc1 的图；部件本来就拆好了：弹匣 / 枪身 / 滑套 / 两只持枪手）
                d.displayName = "防护服手枪";
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
                d.parts.Add(P("PistolLeftHand", "Held/PistolLeftHand", 1623.6f, 1115.5f, 28, 0f, true));   // 手的锚点 = 手心中心
                d.parts.Add(P("PistolRightHand", "Held/PistolRightHand", 1693.2f, 1182.4f, 29, 0f, true));
                d.reloadMotion = ReloadMotion.Pistol; d.magPart = "PistolMag"; d.reloadHand = "PistolRightHand";
                d.magDir = new Vector2(0.135f, 0.991f); d.magInHand = new Vector2(0.002f, 0.0174f); d.magReady = 0.085f;   // 弹匣底部相对手心中心
                d.holsterArt = "NpcArt/Gear/Pistol";
                // 战斗数值（防护服手枪：伤害 / 散布由 NPC 自己的规则算，这里只管击退等枪的属性）
                d.magSize = 12; d.knockback = 2.5f; d.bulletSpeed = 120f; d.range = 6f;
                return d;
            case "aba-x95":      // ABA-X95 无托步枪（图：Resources/WeaponArt/aba-x95；坐标 = x96.PSD 里的十字线）
                d.displayName = "ABA-X95";
                d.hold = HoldType.Rifle;
                d.artFolder = "WeaponArt/aba-x95";
                d.grip = new Vector2(618f, 430.5f);          // 持枪手（握把）十字
                d.muzzle = new Vector2(1227f, 281.5f);       // 枪口十字
                // 端枪：枪托十字（72.5, 260.5）顶在肩膀上 → 握把相对脚底中心的位置
                d.shoulder = new Vector2(-0.083f, 0.51f);
                d.restGrip = new Vector2(-0.083f * 1936.55f + (618f - 72.5f), -0.51f * 1936.55f + (430.5f - 260.5f));
                d.hasLamp = false;
                d.parts.Add(P("X95Mag", "Mag", 452f, 611.5f, 42));                 // 弹匣（轴心 = 弹匣底部十字）
                d.parts.Add(P("X95Body", "Body", 649.5f, 318.5f, 43));             // 枪身
                d.parts.Add(P("X95Charger", "Charger", 913f, 233f, 44, 238f));     // 拉栓：往后拉到"枪栓移动位置"十字（675）
                d.parts.Add(P("X95HandL", "", 618f, 430.5f, 46, 0f, true));        // 持枪手锚点（握把；图用角色衣服的手）
                d.parts.Add(P("X95HandR", "", 958.5f, 347f, 47, 0f, true));        // 辅助手锚点（护木；换弹时离开枪的那只）
                // 手感（原型 QBZ-95，和战斗步枪拉开差距）：无托 = 枪托顶在肩上、枪管和肩膀在一条线上，
                //   后坐力直直往后顶（往后顶得更狠、身体晃得更多），但枪口几乎不往上跳；射速快、回正也快，连发是"突突突"的密集感
                d.rpm = 650f;                                                      // 射速：650 发/分（战斗步枪 400）
                d.recoilKick = new Vector2(2.8f, 4.4f);                            // 每枪枪口上扬（度）：和战斗步枪差不多（原来 1.6~3）
                d.recoilHandX = new Vector2(-0.08f, -0.055f);                      // 每枪往后顶：比战斗步枪狠（直线后坐）
                d.recoilHandY = new Vector2(0.005f, 0.018f);                       // 每枪往上抬：接近战斗步枪（原来 0.002~0.008）
                d.handRecoilRise = 0.014f;                                         // 被顶回去用几秒：更短更脆
                d.handRecoilReturn = new Vector2(0.045f, 0.065f);                  // 回正：更快（射速高，要赶在下一枪前回来）
                d.muzzleRise = new Vector2(0.012f, 0.025f); d.muzzleReturn = 0.05f;   // 枪口上扬 / 回正都更快
                d.recoilLean = 2.5f; d.actionShift = 0.016f;                       // 身体被顶得晃、挪（原来 2 / 0.014）
                d.kickBack = 0.015f; d.kickHold = 0f; d.kickReturn = 0.04f;        // 拉栓：跟得上 650 发/分（一枪间隔 0.09 秒）
                d.flashTime = 0.04f; d.flashSize = 1f;                             // 枪火更短（射速快，一闪一闪连起来）
                d.flashSprites = new[] { "Flash1", "Flash2" };
                d.bulletSprite = "Bullet";
                d.drawGrab = new Vector2(1000f, 70f);       // 护木上边再往上一点（手倒过来抓，手指碰到枪身）
                d.stockPart = "";                           // 无托：没有能拉开的枪托
                d.mountCenter = new Vector2(649.5f, 364.5f);   // 挂在身上时枪的中心（挂件 = 用部件拼的整枪，不缩小）
                d.reloadMotion = ReloadMotion.Bullpup; d.magPart = "X95Mag"; d.reloadHand = "X95HandR";   // 无托步枪：弹匣在握把后面
                d.magDir = new Vector2(-0.183f, 0.983f);    // 弹匣底部十字 → 弹匣顶部十字
                d.magInHand = new Vector2(0.0032f, -0.0321f); d.magReady = 0.1f;   // 弹匣底部相对手心中心
                d.slingArt = "WeaponArt/aba-x95/Sling";
                // 战斗数值（原型 QBZ-95：5.8mm，射速快、散布小、单发略重；无托短小边走边打稳一点；步枪不击退）
                d.magSize = 30; d.damage = 17f; d.spread = 1.1f; d.movingMul = 1.25f; d.recoilPerShot = 0.32f; d.recoilRecover = 9f;
                d.leadError = 0.06f; d.bulletSpeed = 170f; d.burstPause = 0.3f; d.range = 9.5f; d.knockback = 0f;
                d.critChance = 0.12f; d.critMul = 1.8f; d.speedReloadTime = 1.8f; d.tacticalReloadTime = 2.6f;
                return d;
            case "aba-ar15":     // ABA-AR15（图：Resources/WeaponArt/aba-ar15；坐标 = aba-ar15.PSD 里的十字线）
                d.displayName = "ABA-AR15";
                d.hold = HoldType.Rifle;
                d.artFolder = "WeaponArt/aba-ar15";
                d.grip = new Vector2(673.5f, 412.5f);         // 持枪手（握把）十字
                d.muzzle = new Vector2(1809f, 253.5f);        // 枪口十字
                d.lamp = new Vector2(1629f, 251f);            // 手电十字
                d.hasLaser = true; d.laser = new Vector2(862f, 162.5f);   // 镭射十字（瞄准镜旁边）：红外激光沿枪管方向射出去
                d.laserColor = new Color(1f, 0.1f, 0.12f, 0.55f); d.laserWidth = 0.004f; d.laserDotSize = 0.2f;   // 红外：细、偏暗
                d.laserLight = 0.35f; d.laserLightRadius = 0.35f;  // 沿着激光一点点光，照亮经过的地方
                // 端枪：枪托十字（159.5, 288）顶在肩膀上 → 握把相对脚底中心的位置
                d.shoulder = new Vector2(-0.083f, 0.51f);
                d.restGrip = new Vector2(-0.083f * 1936.55f + (673.5f - 159.5f), -0.51f * 1936.55f + (412.5f - 288f));
                d.parts.Add(P("AR15Mag", "Mag", 976f, 603.5f, 40));                // 弹匣（轴心 = 弹匣底部十字）
                d.parts.Add(P("AR15Inner", "Inner", 916f, 240f, 41));              // 内构造（上弹器后面窗口里，上弹器往后走时露出来）
                d.parts.Add(P("AR15Charger", "Charger", 920f, 242f, 42, 216f));    // 上弹器（在枪身下面，从窗口里看到）：往后拉到"上弹器位置"十字（704）
                d.parts.Add(P("AR15Body", "Body", 1132f, 306f, 43));               // 枪身（上弹器那里开了窗口）
                d.parts.Add(P("AR15Bolt", "Bolt", 766f, 206f, 44));                // 拉栓（不动）
                d.parts.Add(P("AR15Stock", "Stock", 342f, 332f, 45));              // 伸缩枪托（取出时拉开）
                d.parts.Add(P("AR15HandL", "", 673.5f, 412.5f, 46, 0f, true));     // 持枪手锚点（握把；图用角色衣服的手）
                d.parts.Add(P("AR15HandR", "", 1306f, 381f, 47, 0f, true));        // 辅助手锚点（护木；换弹时离开枪的那只）
                // 手感（AR-15：缓冲管吸收后坐，往后顶得轻；枪口上跳中等但很稳定、每枪差不多，好预测；射速快）
                //   战斗步枪 = 慢、跳得忽大忽小；X95 = 直往后顶得狠；AR15 = 轻快、稳
                d.rpm = 800f;                                                      // 射速：800 发/分（原型 M4A1，和 AbaForce 一致）
                d.recoilKick = new Vector2(3.4f, 4.4f);                            // 每枪枪口上扬（度）：范围窄 = 稳定
                d.recoilHandX = new Vector2(-0.045f, -0.03f);                      // 每枪往后顶：比战斗步枪轻（缓冲管）
                d.recoilHandY = new Vector2(0.006f, 0.016f);                       // 每枪往上抬
                d.handRecoilRise = 0.016f;
                d.handRecoilReturn = new Vector2(0.05f, 0.07f);                    // 回正快（射速高）
                d.muzzleRise = new Vector2(0.015f, 0.03f); d.muzzleReturn = 0.055f;
                d.recoilLean = 1.3f; d.actionShift = 0.009f;                       // 身体晃得少
                d.kickBack = 0.012f; d.kickHold = 0f; d.kickReturn = 0.035f;       // 上弹器：跟得上 800 发/分（一枪间隔 0.075 秒）
                d.flashTime = 0.045f; d.flashSize = 1.5f;
                d.flashSprites = new[] { "Flash1", "Flash2" };
                d.bulletSprite = "Bullet";
                d.flickerOnDraw = true;                     // 手电刚打开闪一下
                d.drawGrab = new Vector2(1300f, 165f);      // 护木上边（手倒过来抓）
                d.stockPart = "AR15Stock"; d.stockGrab = new Vector2(190f, 300f); d.stockCollapse = 110f;   // 伸缩枪托：取出时左手往后拉开
                d.mountCenter = new Vector2(984f, 342f);    // 挂在身上时枪的中心（挂件 = 用部件拼的整枪，不缩小）
                d.reloadMotion = ReloadMotion.Rifle; d.magPart = "AR15Mag"; d.reloadHand = "AR15HandR";   // 和战斗步枪一样的步枪式换弹
                d.magDir = new Vector2(-0.191f, 0.982f);    // 弹匣底部十字 → 弹匣顶部十字
                d.magInHand = new Vector2(0.0032f, -0.0321f); d.magReady = 0.1f;   // 弹匣底部相对手心中心
                // 战斗数值（原型 M4A1：5.56mm，射速快、后坐轻、很稳；单发略轻；步枪不击退）
                d.magSize = 30; d.damage = 14f; d.spread = 1.1f; d.movingMul = 1.35f; d.recoilPerShot = 0.34f; d.recoilRecover = 9.5f;
                d.leadError = 0.05f; d.bulletSpeed = 175f; d.burstPause = 0.28f; d.range = 11f; d.knockback = 0f;
                d.critChance = 0.13f; d.critMul = 1.8f; d.speedReloadTime = 1.6f; d.tacticalReloadTime = 2.3f;
                d.scopeMax = 3f; d.scopeNear = 3f; d.scopeFar = 8f; d.scopeGain = 0.25f;   // 1~3 倍可变瞄准镜：近处 1 倍，3 倍时散布 ÷1.5
                d.laserAim = 0.85f;                         // 红外镭射：边走边打更准
                return d;
        }
        if (warn) Debug.LogWarning("[WeaponDef] 没有这把武器：" + id);
        return null;
    }
}
