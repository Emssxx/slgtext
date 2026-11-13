using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;   // 滚轮缩放、右键拖动镜头（新输入系统）

// ============================================================================
// AnimTestHub —— 动画测试中心（Scenes/AnimTest）：所有角色 / 怪物 / 建筑的动画都在这里测。
//
// 【用法】运行后左上角有面板：
//   ① 上面一排选类别（角色 / 怪物 / 建筑）；② 下面点名字，就只显示那一个，镜头自动对准；
//   ③ 点「在 Hierarchy 里选中」→ 在 Inspector 里用它自己的测试开关（Test Held、Test Reload、Test Speed……）；
//   ④ 滚轮缩放、按住右键拖动镜头；「重置镜头」回到这一项设好的位置。
//
// 【以后加新东西】把要测的物体放进场景，然后在本组件 Inspector 的 Entries 列表里加一行：
//   类别、名字、物体、镜头大小 / 高度、操作说明。就这么多，不用改代码。
//
// 【武器测试窗口】选中角色时右边有"武器测试"窗口（面板上的按钮开 / 关，标题栏可以拖）：
//   拿哪把枪（所有 WeaponDef，包括 Resources/Weapons 里的）/ 空手 / 其他东西；挂饰开关；换弹配件开关（关掉胸挂就从屁股后面拿）；
//   开火（一枪 / 一组连发、循环）；四种换弹；npc1 的武器等级 / 紧张（手抖）。窗口里改的是测试开关，不改数值文件。
//
// 【它不改什么】只负责显示 / 隐藏列表里的物体和移动镜头，不改它们身上的任何参数（武器测试窗口除外：它就是用来按测试开关的）。
//   隐藏的物体不运行（换弹之类正在播放的动作会停掉，切回来后重新触发就行）。
// ============================================================================
public class AnimTestHub : MonoBehaviour
{
    public enum Category
    {
        [InspectorName("角色")] Character,
        [InspectorName("怪物")] Monster,
        [InspectorName("建筑")] Building,
    }

    [System.Serializable]
    public class Entry
    {
        public Category category;                 // 类别
        public string label = "";                 // 面板上显示的名字
        public GameObject target;                 // 场景里要测的物体
        public float cameraSize = 0.8f;           // 镜头大小（正交尺寸：越小越近）
        public float cameraY = 0.05f;             // 镜头高度（世界坐标 y）
        [TextArea(1, 4)] public string hint = ""; // 操作说明（显示在面板下面）
    }

    public List<Entry> entries = new List<Entry>();   // 所有测试项（按顺序显示）
    public int startIndex = 0;                         // 一开始显示第几项（从 0 数）
    [Range(0.05f, 1f)] public float timeScale = 1f;    // 播放速度（慢放看快动作，比如滑套、后坐；面板上也能拖）

    static readonly string[] CategoryNames = { "角色", "怪物", "建筑" };
    int _current = -1;                                 // 现在显示的是第几项
    Category _tab;                                     // 面板上选中的类别
    Vector2 _scroll;                                   // 列表滚动位置
    Camera _cam;                                       // 主镜头
    Vector2 _lastMouse;                                // 右键拖动：上一帧鼠标位置

    void OnDisable() { Time.timeScale = 1f; }          // 离开场景 / 停止运行：播放速度恢复正常

    void Start()
    {
        _cam = Camera.main;
        AddMissingMonsters();                                             // 场景里还没放的怪物：自动生成一只放进怪物栏
        foreach (Entry e in entries)
        {
            if (e.target == null) continue;
            CharacterRigBase r = e.target.GetComponentInChildren<CharacterRigBase>(true);
            if (r != null && r.IsMonster) e.category = Category.Monster;   // 怪物（被感染的研究员……）自动放进怪物栏
            e.target.SetActive(false);                                     // 先全部藏起来
        }
        if (entries.Count > 0) Show(Mathf.Clamp(startIndex, 0, entries.Count - 1));
    }

    // 场景里还没放的怪物：自动生成（位置 / 镜头照着第一项），放进怪物栏
    //   被感染的研究员一只；眼球蜘蛛三种大小各一只（幼体 / 青年体 / 变异体）
    void AddMissingMonsters()
    {
        bool hasInfected = false, hasSpider = false;
        foreach (Entry e in entries)
        {
            if (e.target == null) continue;
            if (e.target.GetComponentInChildren<InfectedRig>(true) != null) hasInfected = true;   // 已经有了
            if (e.target.GetComponentInChildren<SpiderRig>(true) != null) hasSpider = true;
        }
        Entry like = entries.Count > 0 ? entries[0] : null;
        Vector3 at = like != null && like.target != null ? like.target.transform.position : Vector3.zero;
        float camSize = like != null ? like.cameraSize : 0.8f, camY = like != null ? like.cameraY : 0.05f;
        if (!hasInfected)
        {
            GameObject go = new GameObject("被感染的研究员（自动生成）");
            go.transform.position = at;
            InfectedRig rig = go.AddComponent<InfectedRig>();
            rig.UseMonsterDefaults();                                     // 慢、拖着走的走路手感（代码加的组件不会自动用）
            entries.Add(new Entry
            {
                category = Category.Monster, label = "被感染的研究员", target = go,
                cameraSize = camSize, cameraY = camY,
                hint = "右边「怪物测试」窗口：攻击 / 循环 / 受击 / 死亡。点「在 Hierarchy 里选中」后，Inspector 里 Test Speed / Test Walk Cycle 走路，Test Aim Target 拖个物体进去看瞳孔追踪"
            });
        }
        if (!hasSpider)
        {
            string[] names = { "眼球蜘蛛·幼体", "眼球蜘蛛·青年体", "眼球蜘蛛·变异体" };
            float[] sizes = { 0.5f, 1.2f, 3f };                           // 和 NPC 比的高度（同 EnemyDefs 的 scale）
            for (int i = 0; i < 3; i++)
            {
                GameObject go = new GameObject(names[i] + "（自动生成）");
                go.transform.position = at;
                SpiderRig rig = go.AddComponent<SpiderRig>();
                rig.UseMonsterDefaults();
                rig.size = sizes[i];
                float k = Mathf.Max(1f, sizes[i]);                         // 大的镜头拉远一点
                float feet = at.y - 0.453125f;                             // 脚底
                entries.Add(new Entry
                {
                    category = Category.Monster, label = names[i], target = go,
                    cameraSize = camSize * k, cameraY = feet + (camY - feet) * k,
                    hint = "右边「怪物测试」窗口：攻击（扑咬）/ 循环 / 受击 / 死亡。点「在 Hierarchy 里选中」后，Inspector 里 Test Speed / Test Walk Cycle 走路，Test Aim Target 拖个物体进去看瞳孔追踪；Size 改大小"
                });
            }
        }
    }

    // 只显示第 i 项，镜头对准它
    void Show(int i)
    {
        for (int j = 0; j < entries.Count; j++)
            if (entries[j].target != null) entries[j].target.SetActive(j == i);   // 只开这一项
        _current = i;
        _tab = entries[i].category;               // 面板跳到它的类别
        ResetCamera();
    }

    // 镜头回到这一项设好的位置和大小（对准物体的 x）
    void ResetCamera()
    {
        if (_cam == null || _current < 0) return;
        Entry e = entries[_current];
        float x = e.target != null ? e.target.transform.position.x : 0f;
        _cam.orthographicSize = Mathf.Max(0.1f, e.cameraSize);
        _cam.transform.position = new Vector3(x, e.cameraY, _cam.transform.position.z);
    }

    void Update()
    {
        Time.timeScale = Mathf.Clamp(timeScale, 0.05f, 1f);                // 慢放（只在这个测试场景里）
        if (_cam == null) return;
        Mouse mouse = Mouse.current;                       // 没有鼠标就是 null
        if (mouse == null) return;
        Vector2 pos = mouse.position.ReadValue();
        Vector2 gp = GuiPoint(pos);
        if (PanelRect().Contains(gp) || (_weaponWin && _winRect.Contains(gp))) { _lastMouse = pos; return; }   // 鼠标在面板 / 窗口上：不动镜头

        float wheel = mouse.scroll.ReadValue().y;          // 滚轮：往前推 = 放大
        if (Mathf.Abs(wheel) > 0.01f)
            _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize * (wheel > 0f ? 0.9f : 1.1f), 0.1f, 50f);

        if (mouse.rightButton.isPressed)                   // 按住右键拖动：镜头跟着鼠标反方向移动
        {
            Vector2 d = pos - _lastMouse;
            float unitsPerPixel = 2f * _cam.orthographicSize / Screen.height;   // 屏幕 1 像素 = 多少单位
            _cam.transform.position -= new Vector3(d.x, d.y, 0f) * unitsPerPixel;
        }
        _lastMouse = pos;
    }

    // ============ 面板 ============

    float GuiScale() { return Mathf.Max(0.5f, Screen.height / 720f); }   // 屏幕越大面板越大（和战斗测试一样）
    static Rect PanelRect() { return new Rect(8f, 8f, 230f, 370f); }      // 面板位置（缩放前）
    Vector2 GuiPoint(Vector2 screen) { float s = GuiScale(); return new Vector2(screen.x / s, (Screen.height - screen.y) / s); }   // 屏幕坐标 → 面板坐标

    void OnGUI()
    {
        float s = GuiScale();
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        GUILayout.BeginArea(PanelRect(), GUI.skin.box);
        GUILayout.Label("动画测试中心");
        _tab = (Category)GUILayout.Toolbar((int)_tab, CategoryNames);   // 类别

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(150f));
        bool any = false;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e.category != _tab) continue;              // 只列这个类别的
            any = true;
            string name = (i == _current ? "▶ " : "　") + (string.IsNullOrEmpty(e.label) ? ("第 " + (i + 1) + " 项") : e.label);
            if (GUILayout.Button(name)) Show(i);
        }
        if (!any) GUILayout.Label("暂无（之后有动画了再加）");
        GUILayout.EndScrollView();

        if (_current >= 0)
        {
            Entry cur = entries[_current];
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重置镜头")) ResetCamera();
#if UNITY_EDITOR
            if (GUILayout.Button("在 Hierarchy 里选中") && cur.target != null)
                UnityEditor.Selection.activeGameObject = cur.target;   // 选中后 Inspector 里就能调它的测试开关
#endif
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(cur.hint)) GUILayout.Label(cur.hint);   // 这一项的操作说明
        }
        GUILayout.Label("播放速度：" + Mathf.RoundToInt(timeScale * 100f) + "%");
        timeScale = GUILayout.HorizontalSlider(timeScale, 0.05f, 1f);       // 拖到左边 = 慢放
        GUILayout.Label("滚轮缩放，按住右键拖动镜头");
        if (CurrentRig() != null && GUILayout.Button(_weaponWin ? "关掉测试窗口" : "打开测试窗口")) _weaponWin = !_weaponWin;
        GUILayout.EndArea();
        if (_weaponWin && CurrentRig() != null) _winRect = GUILayout.Window(7301, _winRect, WeaponWindow, CurrentRig().IsMonster ? "怪物测试" : "武器测试");
    }

    // ============ 武器测试窗口 ============

    bool _weaponWin = true;                                // 窗口开着没有
    Rect _winRect = new Rect(246f, 8f, 270f, 520f);        // 窗口位置（缩放前；标题栏能拖）
    Vector2 _winScroll;
    List<WeaponDef> _weapons;                              // 所有枪（第一次打开时读）
    static readonly CharacterRigBase.ReloadKind[] Kinds =
        { CharacterRigBase.ReloadKind.Tactical, CharacterRigBase.ReloadKind.Speed, CharacterRigBase.ReloadKind.Normal, CharacterRigBase.ReloadKind.Shaky };

    // 现在显示的这一项上的角色（不是角色 = null）
    CharacterRigBase CurrentRig()
    {
        if (_current < 0 || _current >= entries.Count || entries[_current].target == null) return null;
        return entries[_current].target.GetComponentInChildren<CharacterRigBase>();
    }

    void WeaponWindow(int id)
    {
        CharacterRigBase rig = CurrentRig();
        if (rig == null) { GUILayout.Label("先在左边选一个角色"); GUI.DragWindow(); return; }
        if (rig.IsMonster)                                                  // 怪物：不拿枪，只有攻击 / 受击 / 死亡
        {
            GUILayout.Label("【攻击】");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("攻击")) rig.testFire = true;
            rig.testLoop = GUILayout.Toggle(rig.testLoop, "循环");
            rig.testFaceLeft = GUILayout.Toggle(rig.testFaceLeft, "朝左");
            GUILayout.EndHorizontal();
            GUILayout.Label("【受击 / 死亡】");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("受击")) rig.testHit = true;
            if (GUILayout.Button("死亡")) rig.testDeath = true;
            if (GUILayout.Button("复活")) rig.testRevive = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("走路 / 瞳孔追踪：Inspector 里的 Test Speed、Test Walk Cycle、Test Aim Target");
            GUI.DragWindow();
            return;
        }
        if (_weapons == null || GUILayout.Button("刷新武器列表（新建了武器文件后点）")) _weapons = WeaponDef.All();
        _winScroll = GUILayout.BeginScrollView(_winScroll);

        // ---- 拿什么 ----
        GUILayout.Label("【拿什么】现在：" + HeldLabel(rig));
        foreach (WeaponDef d in _weapons)
        {
            if (d == null || !rig.CanTestEquip(d)) continue;
            bool on = rig.HeldGun != null && rig.HeldGun.def == d;
            if (GUILayout.Button((on ? "▶ " : "　") + d.Label + (d.hold == HoldType.Rifle ? "（步枪）" : "（手枪）"))) rig.TestEquip(d.id);
        }
        GUILayout.BeginHorizontal();
        int n = 0;
        foreach (CharacterRigBase.TestOption o in rig.TestHoldExtras())
        {
            if (GUILayout.Button(o.label)) rig.TestEquip(o.key);
            if (++n % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
        }
        GUILayout.EndHorizontal();

        // ---- 挂饰 / 换弹配件 ----
        GUILayout.Space(6f);
        GUILayout.Label("【挂饰】");
        foreach (CharacterRigBase.MountPoint m in rig.Mounts)
        {
            bool on = m.item != null;
            bool gunMount = m.kind == CharacterRigBase.MountKind.Weapon || m.kind == CharacterRigBase.MountKind.Rifle;
            string[] choices = rig.MountChoices(m);                        // 工具挂环：能选挂锤子 / 斧头 / 手雷
            if (!gunMount && choices != null)
            {
                GUILayout.Label(MountLabel(m) + "挂环：" + (on ? ToolLabel(m.item) : "空"));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("空")) rig.TestSetMount(m, false);
                foreach (string c in choices) if (GUILayout.Button(ToolLabel(c))) rig.TestSetMount(m, true, c);
                GUILayout.EndHorizontal();
                continue;
            }
            if (!gunMount)
            {
                bool want = GUILayout.Toggle(on, MountLabel(m));
                if (want != on) rig.TestSetMount(m, want);
                continue;
            }
            GUILayout.Label(MountLabel(m) + "：" + (on ? MountItemLabel(m.item) : "空"));   // 武器 / 步枪挂载点：选挂哪把枪
            GUILayout.BeginHorizontal();
            int k = 0;
            if (GUILayout.Button("空")) rig.TestSetMount(m, false);
            k++;
            foreach (WeaponDef d in _weapons)
            {
                if (d == null || !rig.CanMount(m, d)) continue;
                if (GUILayout.Button(d.Label)) rig.TestSetMount(m, true, rig.MountItemFor(d));
                if (++k % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
            }
            GUILayout.EndHorizontal();
        }
        if (rig.Mounts.Count == 0) GUILayout.Label("（这个角色没有挂载点）");
        GUILayout.Label("【换弹配件】（关掉 = 当作没穿，换弹改从下一个配件 / 屁股后面拿）");
        foreach (CharacterRigBase.ReloadStyle st in rig.ReloadGear) st.disabled = !GUILayout.Toggle(!st.disabled, st.label);
        if (rig.ReloadGear.Count == 0) GUILayout.Label("（没有配件：从屁股后面拿）");
        GUILayout.Label("现在这把枪换弹用：" + rig.ReloadGearFor);
        string gear = rig.TestGearInfo();
        if (!string.IsNullOrEmpty(gear)) { GUILayout.Label(gear); if (GUILayout.Button("配件补满（弹匣）")) rig.TestRefillGear(); }

        // ---- 开火 ----
        GUILayout.Space(6f);
        GUILayout.Label("【开火】步枪 = 一组连发");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("开火")) rig.testFire = true;
        rig.testLoop = GUILayout.Toggle(rig.testLoop, "循环");
        rig.testFaceLeft = GUILayout.Toggle(rig.testFaceLeft, "朝左");
        GUILayout.EndHorizontal();

        // ---- 手雷 ----
        if (rig.CanTestThrow)
        {
            GUILayout.Space(6f);
            GUILayout.Label("【手雷】往测试目标扔（没有就往正前方 3 单位；npc1 要先在工具挂环上挂手雷）");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("扔手雷")) rig.TestThrow();
            if (GUILayout.Button("补手雷")) rig.RefillGrenades();
            GUILayout.EndHorizontal();
            GUILayout.Label("身上还有手雷：" + rig.GrenadesLeft);
        }

        // ---- 换弹 ----
        GUILayout.Space(6f);
        GUILayout.Label("【换弹】");
        GUILayout.BeginHorizontal();
        foreach (CharacterRigBase.ReloadKind k in Kinds)
            if (GUILayout.Button(CharacterRigBase.KindLabel(k))) rig.PlayReload(0f, k);
        GUILayout.EndHorizontal();
        GUILayout.Label(rig.ReloadStatus);

        // ---- 持枪手（npc1） ----
        if (rig is NpcRig n1)
        {
            GUILayout.Space(6f);
            GUILayout.Label("【持枪手】武器等级 " + n1.weaponLevel + "（1 级手抖）  紧张 " + n1.nervous.ToString("0.00"));
            n1.weaponLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(n1.weaponLevel, 1f, 5f));
            n1.nervous = GUILayout.HorizontalSlider(n1.nervous, 0f, 1f);
        }
        GUILayout.EndScrollView();
        GUI.DragWindow();
    }

    static string HeldLabel(CharacterRigBase rig)
    {
        if (rig.HeldGun != null) return rig.HeldGun.def.Label;
        return string.IsNullOrEmpty(rig.HeldKey) ? "空手" : rig.HeldKey;
    }

    // 挂着的物品 id → 显示名（是枪就用枪的名字）
    string MountItemLabel(string item)
    {
        if (_weapons != null)
            foreach (WeaponDef d in _weapons) if (d != null && d.id == item) return d.Label;
        if (item == "pistol") return "防护服手枪";
        if (item == "rifle") return "战斗步枪";
        return item;
    }

    // 工具挂环上的东西 → 显示名
    static string ToolLabel(string item)
    {
        switch (item)
        {
            case "hammer": return "锤子";
            case "axe": return "斧头";
            case "grenade": return "手雷";
            default: return item;
        }
    }

    static string MountLabel(CharacterRigBase.MountPoint m)
    {
        switch (m.kind)
        {
            case CharacterRigBase.MountKind.Hammer: return "锤子";
            case CharacterRigBase.MountKind.Axe: return "斧头";
            case CharacterRigBase.MountKind.Medkit: return "医疗包";
            case CharacterRigBase.MountKind.Toolkit: return "工具包";
            case CharacterRigBase.MountKind.Bottle: return "水杯";
            case CharacterRigBase.MountKind.Weapon: return "武器挂载点";
            case CharacterRigBase.MountKind.Rifle: return "步枪挂载点";
            default: return m.id;
        }
    }
}
