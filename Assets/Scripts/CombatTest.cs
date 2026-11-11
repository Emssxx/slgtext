using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using NavMeshPlus.Components;   // 2D 导航网格（和主场景同一套）
using NavMeshPlus.Extensions;

// ============================================================================
// 战斗测试场景（Scenes/CombatTest）：灰色地砖铺满的空场地，自己挑属性放 NPC 和敌人，看他们打。
//
// 【用法】运行后左边有面板：
//   · 选"放 NPC"/"放敌人"/"ABA 小组"，调好属性，在地图上左键点哪里就放在哪里；右键点一个人 / 敌人 / ABA 队员 = 删掉它。
//   · WASD / 方向键 移动镜头，滚轮缩放。
//   · 地砖：细线 = 1 小格，粗线 = 1 大格（1 大格 = 3×3 小格），方便看射击距离。
// 【无限弹匣】勾着时，拿枪的 NPC 身上的弹匣一直补满 3 个（空弹匣也自动清掉）。
// 【和主场景的区别】不跑开场、AI、地形、饥饿等系统，只留战斗要用的：世界数据（WorldManager，不跑它的开局）、
//   敌人系统（EnemyDirector，关掉自动刷怪）、导航网格（运行时按地砖范围烘焙）。
// ============================================================================
public class CombatTest : MonoBehaviour
{
    [Header("场地")]
    public Vector2Int mapSize = new Vector2Int(24, 16);       // 地砖铺多大（大格；1 大格 = 1 单位）
    public GameObject npcPrefab;                              // NPC 预制体（npc_1）

    [Header("要放的 NPC（运行时面板里也能改）")]
    public TestWeapon npcWeapon = TestWeapon.Pistol;          // 拿什么武器
    [Range(1, 5)] public int npcWeaponLevel = 3;              // 武器操作等级
    [Range(0, 100)] public int npcBravery = 80;               // 勇敢（低了会逃跑）
    [Range(1, 100)] public int npcHealth = 100;               // 生命
    [Range(0, 100)] public int npcSanity = 100;               // 理智
    [Range(0f, 1f)] public float npcNervous = 0f;             // 紧张（手抖）
    public bool npcForceFight = true;                         // 强制迎战（不会因为害怕逃跑）

    [Header("要放的敌人（运行时面板里也能改）")]
    public int enemyIndex = 0;                                // 敌人种类（EnemyDefs 里的第几种）
    [Range(1, 6)] public int enemyCount = 1;                  // 一次放几个
    public bool enemyAssault = true;                          // 袭击（见人就打）；不勾 = 游荡（造物见人就扑，人类只找落单的）

    [Header("要放的 ABA 清除小组（运行时面板里也能改）")]
    [Range(1, 6)] public int abaCount = 4;                    // 一组几个人
    public bool abaHitSurvivors = true;                       // 也打没带小型 AI 终端机的 NPC（不勾 = 只打敌人）

    [Header("其他")]
    public bool infiniteMags = true;                          // 无限弹匣

    public enum TestWeapon
    {
        [InspectorName("手枪")] Pistol,
        [InspectorName("斧头")] Axe,
        [InspectorName("锤子")] Hammer,
        [InspectorName("空手")] Fists,
    }

    enum PlaceMode { None, Npc, Enemy, Aba }                  // 左键点地图时放什么
    PlaceMode _mode = PlaceMode.Npc;
    bool _ready;                                              // 导航网格烘焙好了才能放
    int _npcCounter;                                          // NPC 编号（id 不重复）
    readonly List<GameObject> _npcs = new List<GameObject>(); // 放出来的 NPC
    readonly List<EnemyDef> _enemyDefs = new List<EnemyDef>();// 所有敌人种类
    Rect _panel = new Rect(10, 10, 300, 560);                 // 面板位置（按 720 高的屏幕算，会跟着屏幕缩放）
    Vector2 _scroll;                                          // 面板滚动
    static readonly string[] WeaponNames = { "手枪", "斧头", "锤子", "空手" };

    // ============ 开场：搭场地 ============

    IEnumerator Start()
    {
        // 1. 世界数据：加一个 WorldManager，但马上关掉，不让它跑开局（开场、AI、地形、自动生成 NPC）
        if (WorldManager.Instance == null)
        {
            WorldManager wm = new GameObject("WorldManager").AddComponent<WorldManager>();
            wm.enabled = false;                                   // 关掉 = 它的 Start 不会执行
        }
        WorldManager.Instance.world = new WorldState { day = 30 };   // 过了安全期（敌人会主动打人）

        // 2. 敌人系统：关掉自动刷怪，只用自己放的
        EnemyDirector dir = EnemyDirector.Ensure();
        dir.autoSpawn = false;

        // 3. 灰色地砖 + 导航网格
        BuildFloor();
        yield return null;                                        // 等一帧，碰撞体登记好了再烘焙
        BuildNavMesh();

        // 4. 敌人种类列表（面板用）
        _enemyDefs.Clear();
        foreach (EnemyDef d in EnemyDefs.All) _enemyDefs.Add(d);
        enemyIndex = Mathf.Clamp(enemyIndex, 0, Mathf.Max(0, _enemyDefs.Count - 1));
        _ready = true;
    }

    // 灰色地砖：一整张图，细线 = 小格，粗线 = 大格
    void BuildFloor()
    {
        const int px = 48;                                        // 1 大格画多少像素
        int sub = 3;                                              // 1 大格 = 3×3 小格（和主场景一样）
        int w = mapSize.x * px, h = mapSize.y * px;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color baseC = new Color(0.46f, 0.47f, 0.49f);             // 地砖灰
        Color thin = new Color(0.40f, 0.41f, 0.43f);              // 小格线
        Color thick = new Color(0.30f, 0.31f, 0.33f);             // 大格线
        Color[] pix = new Color[w * h];
        int step = px / sub;                                      // 1 小格多少像素
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = baseC;
                if (x % step == 0 || y % step == 0) c = thin;     // 小格线
                if (x % px == 0 || y % px == 0 || x % px == 1 || y % px == 1) c = thick;   // 大格线（2 像素粗）
                pix[y * w + x] = c;
            }
        tex.SetPixels(pix);
        tex.Apply();
        GameObject floor = new GameObject("Floor");
        SpriteRenderer sr = floor.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), px);   // 1 大格 = 1 单位
        sr.sortingOrder = -10;                                    // 最底下
    }

    // 导航网格：按地砖范围烘焙（和主场景一样：旋转 -90° 的 NavMeshSurface + 2D 收集 + 可行走的碰撞体）
    void BuildNavMesh()
    {
        GameObject ground = new GameObject("NavGround");
        ground.layer = 2;                                         // Ignore Raycast
        BoxCollider2D box = ground.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(mapSize.x, mapSize.y);
        ground.AddComponent<NavMeshModifier>();                   // 可行走

        GameObject navGo = new GameObject("NavMeshSurface");
        navGo.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        NavMeshSurface surface = navGo.AddComponent<NavMeshSurface>();
        surface.agentTypeID = 0;
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.defaultArea = 0;
        navGo.AddComponent<CollectSources2d>();                   // 收集 2D 碰撞体
        surface.BuildNavMesh();
    }

    // ============ 每帧 ============

    void Update()
    {
        if (!_ready) return;
        MoveCamera();
        HandleClicks();
        if (infiniteMags) RefillMags();
        if (AbaForce.Instance != null) AbaForce.Instance.targetSurvivors = abaHitSurvivors;
        _npcs.RemoveAll(g => g == null);
    }

    // 镜头：WASD / 方向键移动，滚轮缩放
    void MoveCamera()
    {
        Camera cam = Camera.main;
        Keyboard kb = Keyboard.current;
        if (cam == null) return;
        Vector3 move = Vector3.zero;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
        }
        cam.transform.position += move * cam.orthographicSize * 1.2f * Time.unscaledDeltaTime;
        Mouse m = Mouse.current;
        if (m != null && !MouseOverPanel())
        {
            float scroll = m.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) cam.orthographicSize = Mathf.Clamp(cam.orthographicSize - Mathf.Sign(scroll) * 0.5f, 1.5f, 14f);
        }
    }

    // 左键：放 NPC / 敌人；右键：删掉点到的
    void HandleClicks()
    {
        Mouse m = Mouse.current;
        Camera cam = Camera.main;
        if (m == null || cam == null || MouseOverPanel()) return;
        Vector3 wp = cam.ScreenToWorldPoint(m.position.ReadValue());
        wp.z = 0f;
        if (m.leftButton.wasPressedThisFrame)
        {
            if (_mode == PlaceMode.Npc) SpawnNpc(wp);
            else if (_mode == PlaceMode.Enemy) SpawnEnemies(wp);
            else if (_mode == PlaceMode.Aba) SpawnAba(wp);
        }
        if (m.rightButton.wasPressedThisFrame) DeleteNear(wp);
    }

    // 鼠标在不在面板上（在面板上点不算点地图）
    bool MouseOverPanel()
    {
        Mouse m = Mouse.current;
        if (m == null) return false;
        Vector2 sp = m.position.ReadValue();
        float s = GuiScale();
        Vector2 gp = new Vector2(sp.x / s, (Screen.height - sp.y) / s);   // 屏幕坐标 → 面板坐标（面板 y 朝下）
        return _panel.Contains(gp);
    }

    // ============ 放 / 删 ============

    // 按面板上的属性放一个 NPC
    void SpawnNpc(Vector3 at)
    {
        if (npcPrefab == null) { Debug.LogError("[CombatTest] 没有指定 NPC 预制体（npcPrefab）"); return; }
        if (!NavMesh.SamplePosition(at, out NavMeshHit hit, 1f, NavMesh.AllAreas)) return;   // 点到场地外面
        _npcCounter++;
        string id = "test_npc_" + _npcCounter.ToString("00");
        NpcData d = NpcGenerator.Generate(id);                    // 随机外观 / 名字 / 性格
        d.personality.bravery = npcBravery;                       // 再按面板改
        d.health = npcHealth;
        d.sanity = npcSanity;
        if (d.inventory == null) d.inventory = new List<ItemStack>();
        d.inventory.Clear();
        switch (npcWeapon)                                        // 武器放进物品栏（NpcCombat 按物品栏选武器）
        {
            case TestWeapon.Pistol:
                NpcInventory.Add(d, "pistol");
                NpcInventory.Add(d, "magazine", 3);
                d.pistolAmmo = NpcInventory.MagSize;
                break;
            case TestWeapon.Axe: NpcInventory.Add(d, "axe"); break;
            case TestWeapon.Hammer: NpcInventory.Add(d, "hammer"); break;
        }
        WorldManager.Instance.world.npcs.Add(d);

        GameObject go = Instantiate(npcPrefab, hit.position, Quaternion.identity);
        go.name = "NPC_" + id;
        NpcView view = go.GetComponent<NpcView>();
        if (view != null) view.SetId(id);                         // 绑定数据
        NavMeshAgent agent = go.GetComponent<NavMeshAgent>();
        if (agent != null && !agent.isOnNavMesh) agent.Warp(hit.position);
        NpcRig rig = go.GetComponent<NpcRig>();                   // 外观：武器等级、紧张
        if (rig != null) { rig.weaponLevel = npcWeaponLevel; rig.nervous = npcNervous; }
        if (go.GetComponent<NpcCombat>() == null) go.AddComponent<NpcCombat>();   // 战斗（主场景由 EnemyDirector 每秒补挂，这里直接挂上）
        if (npcForceFight) NpcCombat.SetStance(id, NpcCombat.Stance.Fight, 999999f);   // 强制迎战
        _npcs.Add(go);
    }

    // 按面板上的种类 / 数量放敌人
    void SpawnEnemies(Vector3 at)
    {
        if (_enemyDefs.Count == 0) return;
        if (!NavMesh.SamplePosition(at, out NavMeshHit hit, 1f, NavMesh.AllAreas)) return;
        EnemyDef def = _enemyDefs[Mathf.Clamp(enemyIndex, 0, _enemyDefs.Count - 1)];
        for (int i = 0; i < enemyCount; i++)
        {
            Vector2 off = i == 0 ? Vector2.zero : Random.insideUnitCircle * 0.8f;   // 多个时散开一点
            Vector3 p = hit.position + new Vector3(off.x, off.y, 0f);
            Enemy.Spawn(def, p, enemyAssault ? Enemy.Goal.Assault : Enemy.Goal.Wander, p);
        }
    }

    // 放一个 ABA 清除小组（AbaForce.Deploy：队形 / 战术都是正式规则，和主场景一样）
    void SpawnAba(Vector3 at)
    {
        if (!NavMesh.SamplePosition(at, out NavMeshHit hit, 1f, NavMesh.AllAreas)) return;
        AbaForce.Ensure().targetSurvivors = abaHitSurvivors;
        AbaForce.Deploy(hit.position, abaCount);
    }

    void ClearAba()
    {
        foreach (AbaOperative o in AbaOperative.All.ToArray()) if (o != null) Destroy(o.gameObject);
        foreach (AbaSquad q in AbaSquad.All.ToArray()) if (q != null) Destroy(q.gameObject);
    }

    // 右键：删掉离鼠标最近的 NPC / 敌人 / ABA 队员（0.6 单位以内）
    void DeleteNear(Vector3 at)
    {
        GameObject best = null; float bestD = 0.6f;
        foreach (GameObject g in _npcs)
        {
            if (g == null) continue;
            float d = Vector2.Distance(g.transform.position, at);
            if (d < bestD) { bestD = d; best = g; }
        }
        foreach (Enemy e in Enemy.All)
        {
            if (e == null) continue;
            float d = Vector2.Distance(e.transform.position, at);
            if (d < bestD) { bestD = d; best = e.gameObject; }
        }
        foreach (AbaOperative o in AbaOperative.All)
        {
            if (o == null) continue;
            float d = Vector2.Distance(o.transform.position, at);
            if (d < bestD) { bestD = d; best = o.gameObject; }
        }
        if (best == null) return;
        RemoveNpcData(best);
        Destroy(best);
    }

    // 删 NPC 时把它的数据也从世界里删掉
    void RemoveNpcData(GameObject go)
    {
        NpcView v = go.GetComponent<NpcView>();
        if (v == null || WorldManager.Instance == null) return;
        WorldManager.Instance.world.npcs.RemoveAll(n => n != null && n.id == v.npcId);
        _npcs.Remove(go);
    }

    void ClearEnemies()
    {
        foreach (Enemy e in Enemy.All.ToArray()) if (e != null) Destroy(e.gameObject);
    }

    void ClearNpcs()
    {
        foreach (GameObject g in _npcs.ToArray()) if (g != null) { RemoveNpcData(g); Destroy(g); }
        _npcs.Clear();
    }

    // 无限弹匣：拿枪的人弹匣一直补满 3 个，空弹匣清掉
    void RefillMags()
    {
        if (WorldManager.Instance == null) return;
        foreach (NpcData n in WorldManager.Instance.world.npcs)
        {
            if (n == null || n.IsDead || !NpcInventory.Has(n, "pistol")) continue;
            int have = NpcInventory.Count(n, "magazine");
            if (have < 3) NpcInventory.Add(n, "magazine", 3 - have);
            int empty = NpcInventory.Count(n, NpcInventory.EmptyMag);
            if (empty > 0) NpcInventory.Remove(n, NpcInventory.EmptyMag, empty);
        }
        foreach (AbaOperative o in AbaOperative.All) if (o != null && o.Alive && o.SpareMags < 2) o.RefillSpare();   // ABA：备用弹匣补满
    }

    // ============ 面板 ============

    float GuiScale() { return Mathf.Max(0.5f, Screen.height / 720f); }   // 屏幕越大面板越大

    void OnGUI()
    {
        float s = GuiScale();
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        DrawLabels(s);
        GUILayout.BeginArea(_panel, GUI.skin.box);
        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.Label(_ready ? "战斗测试（左键放置，右键删除，WASD 移动，滚轮缩放）" : "正在搭场地……");

        GUILayout.Label("左键放：");
        _mode = (PlaceMode)GUILayout.Toolbar((int)_mode, new[] { "不放", "NPC", "敌人", "ABA" });

        GUILayout.Space(6);
        GUILayout.Label("—— NPC ——");
        npcWeapon = (TestWeapon)GUILayout.Toolbar((int)npcWeapon, WeaponNames);
        npcWeaponLevel = IntSlider("武器操作等级", npcWeaponLevel, 1, 5);
        npcBravery = IntSlider("勇敢", npcBravery, 0, 100);
        npcHealth = IntSlider("生命", npcHealth, 1, 100);
        npcSanity = IntSlider("理智", npcSanity, 0, 100);
        GUILayout.Label("紧张（手抖） " + npcNervous.ToString("0.00"));
        npcNervous = GUILayout.HorizontalSlider(npcNervous, 0f, 1f);
        npcForceFight = GUILayout.Toggle(npcForceFight, "强制迎战（不逃跑）");

        GUILayout.Space(6);
        GUILayout.Label("—— 敌人 ——");
        if (_enemyDefs.Count > 0)
        {
            string[] names = new string[_enemyDefs.Count];
            for (int i = 0; i < names.Length; i++) names[i] = EnemyLabel(_enemyDefs[i]);
            enemyIndex = GUILayout.SelectionGrid(Mathf.Clamp(enemyIndex, 0, names.Length - 1), names, 2);
            EnemyDef d = _enemyDefs[enemyIndex];
            GUILayout.Label($"血 {d.maxHp:0}  伤害 {d.damage:0}  攻击距离 {d.attackRange:0.##}  速度 {d.speed:0.#}  视野 {d.sight:0.#}");
        }
        enemyCount = IntSlider("一次放几个", enemyCount, 1, 6);
        enemyAssault = GUILayout.Toggle(enemyAssault, "袭击（见人就打；不勾 = 游荡）");

        GUILayout.Space(6);
        GUILayout.Label("—— ABA 清除小组 ——");
        abaCount = IntSlider("一组几个人", abaCount, 1, 6);
        abaHitSurvivors = GUILayout.Toggle(abaHitSurvivors, "也打 NPC（没带小型 AI 终端机的）");
        GUILayout.Label("（F10 也能在鼠标位置放一组）");

        GUILayout.Space(6);
        infiniteMags = GUILayout.Toggle(infiniteMags, "无限弹匣");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("清空敌人")) ClearEnemies();
        if (GUILayout.Button("清空 NPC")) ClearNpcs();
        if (GUILayout.Button("清空 ABA")) ClearAba();
        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // 带名字的整数滑条
    static int IntSlider(string label, int v, int min, int max)
    {
        GUILayout.Label(label + " " + v);
        return Mathf.RoundToInt(GUILayout.HorizontalSlider(v, min, max));
    }

    // 敌人显示名（有翻译用翻译，没有就用 id）
    static string EnemyLabel(EnemyDef d)
    {
        string n = d.Name;
        return string.IsNullOrEmpty(n) || n == d.NameKey || n == d.id ? d.id : n + "（" + d.id + "）";
    }

    // 每个人 / 敌人头顶的小字：NPC = 等级、武器、子弹、血；敌人 = 血
    void DrawLabels(float s)
    {
        Camera cam = Camera.main;
        if (cam == null || WorldManager.Instance == null) return;
        GUIStyle st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
        st.normal.textColor = Color.white;
        foreach (GameObject g in _npcs)
        {
            if (g == null) continue;
            NpcView v = g.GetComponent<NpcView>();
            NpcData n = v != null ? WorldManager.Instance.GetNpcById(v.npcId) : null;
            NpcRig rig = g.GetComponent<NpcRig>();
            if (n == null) continue;
            string weapon = NpcInventory.Has(n, "pistol") ? "手枪 " + n.pistolAmmo + "/" + NpcInventory.MagSize
                          : NpcInventory.Has(n, "axe") ? "斧头" : NpcInventory.Has(n, "hammer") ? "锤子" : "空手";
            string text = n.IsDead ? n.npcName + " 死亡" : $"Lv{(rig != null ? rig.weaponLevel : 3)} {weapon}  HP {n.health:0}";
            Label(cam, g.transform.position + new Vector3(0f, 0.75f, 0f), text, st, s);
        }
        foreach (Enemy e in Enemy.All)
        {
            if (e == null || !e.Alive) continue;
            Label(cam, e.transform.position + new Vector3(0f, 0.25f + 0.9f * e.Def.scale, 0f), e.Def.id + "  HP " + e.Hp.ToString("0"), st, s);
        }
        foreach (AbaOperative o in AbaOperative.All)
        {
            if (o == null || !o.Alive || o.Weapon == null) continue;
            string gun = o.Weapon.id == "pistol" ? "手枪" : "步枪";
            string phase = o.Squad != null ? " [" + o.Squad.phase + "]" : "";
            string text = $"ABA{o.Slot + 1} {gun} {o.InGun}/{o.Weapon.magSize}+{o.SpareMags}  HP {o.Hp:0}  体力 {o.Stamina:0}" + (o.Slot == 0 ? phase : "");
            Label(cam, o.transform.position + new Vector3(0f, 0.75f, 0f), text, st, s);
        }
    }

    // 在世界坐标的位置写一行字
    static void Label(Camera cam, Vector3 world, string text, GUIStyle st, float s)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z < 0f) return;
        Rect r = new Rect(sp.x / s - 140f, (Screen.height - sp.y) / s - 10f, 280f, 20f);   // 加宽：ABA 那行字比较长
        GUI.Label(r, text, st);
    }
}
