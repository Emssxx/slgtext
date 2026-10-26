using UnityEngine;

// 精神状态等级。
public enum SanityLevel { Lucid, Dazed, Hallucinating, Broken }

// 精神状态机：根据 SAN（和崩解锁）判断 NPC 处于哪一级，并生成对应的“状态注入文本”喂给 AI。
// 取代旧的 HallucinationSystem——把分级、幻觉、崩解退行都集中在这里。
public static class MentalState
{
    // 判断当前精神等级。注意：一旦 isBroken（崩解）为真，永远返回 Broken（不可逆）。
    public static SanityLevel GetLevel(NpcData npc)
    {
        if (npc.isBroken) return SanityLevel.Broken;        // 崩解不可逆
        if (npc.sanity <= 0f) return SanityLevel.Broken;    // 刚归零，也算崩解
        if (npc.sanity < 30f) return SanityLevel.Hallucinating;
        if (npc.sanity < 60f) return SanityLevel.Dazed;
        return SanityLevel.Lucid;
    }

    // 生成要拼进 system prompt 的状态文本。清醒时返回空串（不影响）。
    public static string GetInjection(NpcData npc, WorldState world)
    {
        switch (GetLevel(npc))
        {
            case SanityLevel.Dazed:
                return LanguageGuard.Pick(
                    "\n【你此刻的精神状态：失神】你的注意力很难集中，思绪飘忽，常常走神、答非所问、" +
                    "说到一半忘了要说什么。你说话仍大致正常，但明显有些恍惚、迟钝。",
                    "\n【今のあなたの精神状態：放心】集中できず、考えがふらつく。よく上の空になり、的外れな返事をしたり、" +
                    "話の途中で何を言おうとしたか忘れたりする。話し方はおおむね普通だが、明らかにぼんやりして鈍い。");

            case SanityLevel.Hallucinating:
                return GetHallucinationText(npc, world);

            case SanityLevel.Broken:
                return GetBrokenText(npc, world);

            default: // Lucid
                return "";
        }
    }

    // —— 幻觉级（SAN 1~30）：看见不存在的东西，但还认得自己。随机一种。——
    static string GetHallucinationText(NpcData npc, WorldState world)
    {
        int kind = Random.Range(0, 3);
        switch (kind)
        {
            case 0:
                return LanguageGuard.Pick(
                    "\n【你此刻的精神状态：幻觉】你清楚地看见角落里有一个扭曲的、像魔鬼一样的身影正盯着你，" +
                    "它真实得可怕。你会忍不住对它做出反应、向同伴示警，尽管别人也许什么都没看到。",
                    "\n【今のあなたの精神状態：幻覚】部屋の隅に、ねじれた悪魔のような影がこちらを見つめているのがはっきり見える。" +
                    "恐ろしいほど本物だ。周りの者には何も見えていないかもしれないが、あなたは思わず反応し、仲間に警告してしまう。");
            case 1:
                return LanguageGuard.Pick(
                    "\n【你此刻的精神状态：幻觉】你确信仓库里的食物已经凭空消失、一点不剩，" +
                    "你为即将饿死而恐慌。无论别人怎么说，你都坚信这一点。",
                    "\n【今のあなたの精神状態：幻覚】倉庫の食料が跡形もなく消えてしまったと確信している。" +
                    "飢え死にするのではないかと怯えている。誰に何を言われても、そう信じて疑わない。");
            default:
                string other = GetAnotherNpcName(npc, world);
                return LanguageGuard.Pick(
                    "\n【你此刻的精神状态：幻觉】你强烈怀疑" + other + "正在密谋加害你，" +
                    "你对" + other + "充满警惕和敌意，说话也带着这种猜忌。",
                    "\n【今のあなたの精神状態：幻覚】" + other + "が自分に危害を加えようと企んでいると強く疑っている。" +
                    other + "を警戒し、敵意を抱いていて、話す言葉にもその疑いがにじむ。");
        }
    }

    // —— 崩解级（SAN=0）：退行到旧职业人格 + 按性格分化。具体异变在下一步实现。——
    static string GetBrokenText(NpcData npc, WorldState world)
    {
        // 性格分化：外向→尖叫嘶吼；内向→失语、只能哼吟。
        string reaction = npc.personality.sociability >= 50
            ? LanguageGuard.Pick("你已无法正常交流，只会发出语无伦次的尖叫、嘶吼和断断续续的喊叫。",
                                 "もはやまともに話せない。支離滅裂な悲鳴、うなり声、途切れ途切れの叫びしか出ない。")
            : LanguageGuard.Pick("你陷入失语，几乎说不出完整的话，只能发出含糊的哼吟和气音。",
                                 "言葉を失い、まともな文はほとんど話せない。かすれたうめき声や息の音しか出ない。");

        // 职业回退：退行到旧身份的本能行为。
        string regress = LanguageGuard.Pick(
            "你的意识退回到了崩溃前的旧身份「" + (string.IsNullOrEmpty(npc.profession) ? "过去的自己" : npc.profession) +
            "」，你会无意识地重复那个身份最熟悉的言行（例如教师会开始讲课、医生会呼喊救治、军人会喊战术口令）。",
            "あなたの意識は、崩壊する前の昔の自分「" + (string.IsNullOrEmpty(npc.profession) ? "かつての自分" : npc.profession) +
            "」に戻ってしまった。その立場で一番慣れた言動を無意識に繰り返す（教師なら授業を始め、医者なら手当てを叫び、軍人なら号令をかける）。");

        // 异变：把已出现的异变特征描述进去。
        string mutationDesc = "";
        if (npc.mutations != null && npc.mutations.Count > 0)
            mutationDesc = LanguageGuard.Pick("你的身体正在发生不可逆的异变：", "あなたの体には取り返しのつかない変異が起きている：")
                           + string.Join("、", npc.mutations) + "。";

        return
            LanguageGuard.Pick("\n【你此刻的精神状态：人格崩解】你的人格已经彻底崩溃，不再是清醒的人。",
                               "\n【今のあなたの精神状態：人格崩壊】人格は完全に崩れ、もう正気の人間ではない。") +
            reaction + regress + mutationDesc +
            LanguageGuard.Pick("你的话语应当破碎、失常、夹杂旧身份的残留，绝不能像正常人那样有条理地对话。",
                               "言葉は砕け、異常で、昔の自分の名残が混ざる。普通の人のように筋道立てて話してはならない。");
    }

    static string GetAnotherNpcName(NpcData self, WorldState world)
    {
        foreach (NpcData n in world.npcs)
            if (n.id != self.id) return n.npcName;
        return LanguageGuard.Pick("你的同伴", "仲間");
    }
}
