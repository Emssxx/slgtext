using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // 项目用的是新输入系统（和 BuildModeToggle 一样）

// 【临时调试工具】实机调节 NPC 的 SAN（理智）和健康数值，方便观察各精神等级、状态词条的表现。
// 只改数据（sanity / satiety / water / energy / health 等），词条由 NpcHealth 自己按新数值加减。
// 精神等级、对话表现、名片血条等，都由现有系统自己读新数值来显示。
// 用法：运行游戏后按 F2 显示/隐藏面板。
// 测完按老规矩：先 git commit，再删掉这个脚本 + 场景里挂它的物体。
public class DebugSanityPanel : MonoBehaviour
{
    public bool visible = true;            // 面板当前是否显示（F2 切换）

    // 冻结开关：打开后，每帧把 SAN 拉回你设的值，不让规则的每回合衰减把它改掉。
    private bool _freeze = false;
    // 记录每个 NPC 被冻结时的 SAN 值（key = NPC 对象本身）。
    private Dictionary<NpcData, float> _frozen = new Dictionary<NpcData, float>();

    private Vector2 _scroll; // 面板内容太长时的滚动位置

    void Update()
    {
        // 按 F2 切换显示/隐藏（先判断键盘存在，避免没键盘时报空引用）
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame) visible = !visible;
    }

    // LateUpdate 在所有 Update 之后执行：规则结算完了，我们再把冻结值盖回去。
    void LateUpdate()
    {
        if (!_freeze) return;
        foreach (var pair in _frozen)
            ApplySanity(pair.Key, pair.Value);
    }

    void OnGUI()
    {
        if (!visible) return;
        if (WorldManager.Instance == null || WorldManager.Instance.world == null) return;
        WorldState world = WorldManager.Instance.world;

        // 面板放在屏幕右侧，宽 360，高度最多 600（超出可滚动）。
        float height = Mathf.Min(600, Screen.height - 40);
        GUILayout.BeginArea(new Rect(Screen.width - 380, 20, 360, height), GUI.skin.box);
        GUILayout.Label("【调试】SAN 调节（F2 隐藏）");

        // 冻结开关：从关→开的瞬间，记下所有 NPC 当前的 SAN
        bool newFreeze = GUILayout.Toggle(_freeze, " 冻结 SAN（不随回合衰减）");
        if (newFreeze && !_freeze)
        {
            _frozen.Clear();
            foreach (NpcData n in world.npcs) _frozen[n] = n.sanity;
        }
        _freeze = newFreeze;

        // 测试需求行为：清空仓库里的食物 → 物资不足，NPC 会出去搜寻
        if (GUILayout.Button("清空仓库食物（测试外出搜寻）")) ClearFood(world);
        // 清空净化水：不然 NPC 水分低于 60 会自己去仓库喝，"渴"按钮的效果马上就没了
        if (GUILayout.Button("清空仓库净化水（测试口渴）")) ClearWater(world);

        GUILayout.Space(6);
        _scroll = GUILayout.BeginScrollView(_scroll);

        foreach (NpcData npc in world.npcs)
        {
            if (npc.IsDead) continue; // 死亡的 NPC 不显示

            // 第一行：名字 + 数值 + 当前精神等级 + 正在做的事
            GUILayout.Label($"{npc.npcName}   SAN:{npc.sanity:0}   {LevelName(MentalState.GetLevel(npc))}   {npc.currentAction}");
            // 健康数值（饥饿值 / 水分：100 = 不饿 / 不渴）
            GUILayout.Label($"饥饿值:{npc.satiety:0}  水分:{npc.water:0}  体力:{npc.energy:0}  生命:{npc.health:0}");
            // 当前的状态词条（受伤、口渴、疲惫……），没有就显示"健康"
            GUILayout.Label("词条：" + TagsText(npc));

            // 第二行：滑条，拖动直接改 SAN（0~100）
            float slider = GUILayout.HorizontalSlider(npc.sanity, 0f, 100f);
            if (!Mathf.Approximately(slider, npc.sanity)) SetSanity(npc, slider);

            // 第三行：快捷按钮，正好卡在各等级分界线上（见 MentalState.GetLevel）
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("100")) SetSanity(npc, 100f); // 清醒
            if (GUILayout.Button("59")) SetSanity(npc, 59f);   // 恍惚（<60）
            if (GUILayout.Button("29")) SetSanity(npc, 29f);   // 幻觉（<30）
            if (GUILayout.Button("0")) SetSanity(npc, 0f);     // 崩解（下回合规则会锁定 isBroken）
            // 解除崩解：只有已崩解的 NPC 才显示这个按钮
            if (npc.isBroken && GUILayout.Button("解除崩解"))
            {
                npc.isBroken = false;        // 撤销崩解锁定（正式规则里是不可逆的，这里仅供测试）
                npc.mutations.Clear();       // 清掉崩解时长出的异变
                npc.currentAction = "";      // 清掉"人格崩解"的状态文字
                SetSanity(npc, 100f);        // 顺便把 SAN 拉回满，不然下回合又会被锁定
            }
            GUILayout.EndHorizontal();

            // 第四行：测试健康系统（词条由 NpcHealth 每秒按数值自动加上）
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("饿")) npc.satiety = 10f;   // 饥饿值 10 → 「饥饿」，NPC 会去吃东西
            if (GUILayout.Button("渴")) npc.water = 10f;     // 水分 10 → 「口渴难耐」（仓库有水会自己去喝）
            if (GUILayout.Button("累")) npc.energy = 10f;    // 体力 10 → 「疲惫」，NPC 会去休息
            if (GUILayout.Button("伤")) NpcHealth.Injure(npc, 30f, InjuryCause.Generic); // 扣 30 生命 + 「受伤」
            if (GUILayout.Button("复原")) Restore(npc);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // 把仓库里所有食物类物品清零（测试"物资不足 → 外出搜寻"）。
    void ClearFood(WorldState world)
    {
        if (ItemDatabase.Instance == null) return;
        foreach (ItemStack s in world.sharedStorage)
        {
            ItemData d = ItemDatabase.Instance.Get(s.itemName);
            if (d != null && d.category == ItemCategory.Food) s.count = 0;
        }
    }

    // 把仓库里的净化水清零（测试"没水喝 → 口渴"）。
    void ClearWater(WorldState world)
    {
        foreach (ItemStack s in world.sharedStorage)
            if (s.itemName == WaterWorks.WaterItem) s.count = 0;
    }

    // 复原：四项数值拉满，再借急救包的规则治好受伤和生病（不消耗仓库里的急救包）。
    void Restore(NpcData npc)
    {
        npc.satiety = 100f;
        npc.water = 100f;
        npc.energy = 100f;
        npc.health = 100f;
        NpcHealth.OnItemUsed(npc, "medkit"); // 去掉「受伤」「生病」，并按新数值重算其它词条
    }

    // 状态词条拼成一行文字（临时工具，固定用中文）。
    string TagsText(NpcData npc)
    {
        List<HealthTag> tags = NpcHealth.Tags(npc);
        if (tags == null || tags.Count == 0) return "健康";
        List<string> names = new List<string>();
        foreach (HealthTag t in tags) names.Add(NpcHealth.TagName(t, false));
        return string.Join("、", names);
    }

    // 设置某个 NPC 的 SAN，同时更新冻结记录。
    void SetSanity(NpcData npc, float value)
    {
        ApplySanity(npc, value);
        if (_freeze) _frozen[npc] = value; // 冻结中改了值，就冻结到新值
    }

    // 真正写入数据：SAN + 幻觉标志（和 WorldRules 用同一个阈值，保持一致）。
    void ApplySanity(NpcData npc, float value)
    {
        npc.sanity = Mathf.Clamp(value, 0f, 100f);
        npc.isHallucinating = npc.sanity < WorldRules.HallucinationThreshold;
    }

    // 把精神等级枚举转成中文显示（临时工具，不走翻译表）。
    string LevelName(SanityLevel level)
    {
        switch (level)
        {
            case SanityLevel.Lucid: return "清醒";
            case SanityLevel.Dazed: return "恍惚";
            case SanityLevel.Hallucinating: return "幻觉";
            default: return "崩解";
        }
    }
}
