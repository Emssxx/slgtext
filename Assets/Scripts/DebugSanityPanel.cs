using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // 项目用的是新输入系统（和 BuildModeToggle 一样）

// 【临时调试工具】实机调节 NPC 的 SAN（理智）值，方便观察各精神等级的表现。
// 只改数据（npc.sanity / isHallucinating / isBroken），不调用、不修改任何规则代码。
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

        GUILayout.Space(6);
        _scroll = GUILayout.BeginScrollView(_scroll);

        foreach (NpcData npc in world.npcs)
        {
            if (npc.IsDead) continue; // 死亡的 NPC 不显示

            // 第一行：名字 + 数值 + 当前精神等级
            GUILayout.Label($"{npc.npcName}   SAN:{npc.sanity:0}   {LevelName(MentalState.GetLevel(npc))}");

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
            GUILayout.Space(8);
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
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
