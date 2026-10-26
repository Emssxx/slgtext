using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 闲置活动（纯规则，不走 LLM）。
// 每种活动 = 一个类：Score 决定"现在想不想做"（0 = 不做），Run 是执行协程。
// NpcIdleBrain 每次空下来，会把所有活动打一遍分，按分数加权随机挑一个。
//
// 【加新活动的模板】
//   public class XxxActivity : IdleActivity
//   {
//       public override string Id => "xxx";
//       public override float Score(NpcIdleBrain brain, IdleContext ctx) { 条件不满足 return 0f; 否则返回权重; }
//       public override IEnumerator Run(NpcIdleBrain brain, IdleContext ctx)
//       {
//           brain.SetAction("idle_act_xxx");                // 记下在做什么（翻译表加一条）
//           if (!brain.MoveTo(目标点, 0.6f)) yield break;     // 走过去（被指令接管时会返回 false）
//           yield return brain.WaitArrive(8f);
//           brain.Say("一句话", 3f);                          // 想说话就冒气泡
//           yield return new WaitForSeconds(3f);
//       }
//   }
//   然后在 NpcIdleManager.Awake 里 activities.Add(new XxxActivity());
//   例：围着篝火取暖（有篝火建筑才有分）、巡视围墙（勇敢的人分高）、对着迷雾发呆（理智低时分高）、
//       崩解者的蹒跚游荡、睡觉（有床才有分）……都按这个套路加。
// ============================================================

// 闲置活动的"当下情况"快照：活动打分和执行时都读它。
public class IdleContext
{
    public NpcData npc;
    public WorldState world;
    public NpcDecisionRules.Behavior need;   // 需求层判断（Idle/Eat/Rest/Gather/Flee）
    public SanityLevel mental;               // 精神等级（清醒/失神/幻觉/崩解）
    public Vector3 anchor;                   // 活动范围中心（平时 = 营地；做完玩家指令后 = 指令终点）
    public float anchorRadius;               // 活动范围半径
    public bool holding;                     // 是否正在"指令终点附近待命"
    public string lastActivityId;            // 上一个活动（避免老是重复）
    public NpcIdleBrain chatPartner;         // 闲聊打分时顺便找好的聊天对象
}

// 闲置活动基类。
public abstract class IdleActivity
{
    public abstract string Id { get; }                     // 英文短 id，如 "stand"
    public virtual float Cooldown => 0f;                   // 做完后多少秒内不再选它
    public virtual bool Interruptible => true;             // 能否被别人拉去聊天时打断
    public abstract float Score(NpcIdleBrain brain, IdleContext ctx);   // 0 或以下 = 现在不做
    public abstract IEnumerator Run(NpcIdleBrain brain, IdleContext ctx);
    public virtual void OnStop(NpcIdleBrain brain, bool interrupted) { } // 结束/被打断时的额外清理（通用清理 Brain 已做）

    protected static NpcIdleManager Mgr => NpcIdleManager.Instance;
}

// ------------------------------------------------------------
// 发呆：站在原地，偶尔左右张望。内向/累了/失神/需要休息时更常发生。
// ------------------------------------------------------------
public class StandIdleActivity : IdleActivity
{
    public override string Id => "stand";

    public override float Score(NpcIdleBrain brain, IdleContext ctx)
    {
        float s = Mgr.standWeight;
        s *= 1.3f - ctx.npc.personality.sociability / 200f;            // 内向的人更爱发呆
        if (ctx.need == NpcDecisionRules.Behavior.Rest) s *= 4f;        // 累了：原地歇着
        if (ctx.need == NpcDecisionRules.Behavior.Flee) s *= 0.4f;      // 慌了：站不住
        if (ctx.mental == SanityLevel.Dazed) s *= 1.8f;                 // 失神：更常发呆
        if (ctx.mental == SanityLevel.Hallucinating) s *= 0.7f;         // 幻觉：坐立不安
        if (ctx.npc.energy < 40f) s *= 1.4f;
        return s;
    }

    public override IEnumerator Run(NpcIdleBrain brain, IdleContext ctx)
    {
        NpcIdleManager m = Mgr;
        float dur = Random.Range(m.standSeconds.x, m.standSeconds.y);
        if (ctx.need == NpcDecisionRules.Behavior.Rest) dur *= 2f;
        if (ctx.mental == SanityLevel.Dazed) dur *= 1.6f;

        brain.StopMoving();
        if (ctx.need == NpcDecisionRules.Behavior.Rest) brain.SetAction("idle_act_rest");
        else if (ctx.mental == SanityLevel.Dazed) brain.SetAction("idle_act_daze");
        else brain.SetAction("idle_act_stand");

        int looks = ctx.mental == SanityLevel.Dazed ? 0 : Random.Range(0, 3); // 失神的人不张望
        float nextLook = Random.Range(1f, 2.5f);
        float t = 0f;
        while (t < dur)
        {
            if (looks > 0 && t >= nextLook)
            {
                brain.FaceRandom();
                looks--;
                nextLook = t + Random.Range(1.2f, 2.5f);
            }
            t += Time.deltaTime;
            yield return null;
        }
    }
}

// ------------------------------------------------------------
// 闲逛：在活动范围（营地 / 指令终点附近）里随便走走，慢悠悠的。
// ------------------------------------------------------------
public class WanderActivity : IdleActivity
{
    public override string Id => "wander";

    public override float Score(NpcIdleBrain brain, IdleContext ctx)
    {
        if (ctx.need == NpcDecisionRules.Behavior.Rest) return 0f;      // 要休息就不走动
        Personality p = ctx.npc.personality;
        float s = Mgr.wanderWeight;
        s *= 0.7f + (100 - p.caution) / 200f + p.bravery / 400f;        // 不谨慎、胆大的人更爱到处走
        if (ctx.need == NpcDecisionRules.Behavior.Flee) s *= 1.4f;       // 慌了：来回踱步
        if (ctx.need == NpcDecisionRules.Behavior.Gather) s *= 1.3f;     // 缺物资：四处张罗
        if (ctx.mental == SanityLevel.Hallucinating) s *= 1.4f;          // 幻觉：坐立不安
        if (ctx.npc.energy < 40f) s *= 0.5f;
        if (ctx.holding) s *= 0.6f;                                      // 在指令终点：少走动
        return s;
    }

    public override IEnumerator Run(NpcIdleBrain brain, IdleContext ctx)
    {
        NpcIdleManager m = Mgr;
        if (!m.TryGetWanderPoint(brain, ctx, out Vector3 target)) yield break;

        float speed = m.wanderSpeed;
        if (ctx.need == NpcDecisionRules.Behavior.Flee) speed *= 1.6f;  // 慌张时走得快
        if (ctx.mental == SanityLevel.Dazed) speed *= 0.8f;             // 失神时更慢

        brain.SetAction("idle_act_wander");
        if (!brain.MoveTo(target, speed)) yield break;

        float dist = Vector2.Distance(brain.transform.position, target);
        float timeout = dist / Mathf.Max(0.1f, brain.BaseSpeed * speed) * 2f + 2f; // 被卡住也不会一直等
        yield return brain.WaitArrive(timeout);
        brain.StopMoving();
        yield return new WaitForSeconds(Random.Range(0.3f, 1.2f)); // 走到了，站一下再说
    }
}

// ------------------------------------------------------------
// 闲聊（发起方）：找一个同伴，走到他身边，面对面你一句我一句（头顶气泡），
// 聊完按规则结算关系/理智/记忆。说什么话题由规则决定（IdleChatRules），
// 台词由 IdleChat.Voice 提供（默认本地台词；以后可以换成 LLM 配音）。
// ------------------------------------------------------------
public class ChatActivity : IdleActivity
{
    public override string Id => "chat";
    public override bool Interruptible => false;

    public override float Score(NpcIdleBrain brain, IdleContext ctx)
    {
        if (!brain.ChatReady) return 0f;
        if (ctx.need == NpcDecisionRules.Behavior.Rest || ctx.need == NpcDecisionRules.Behavior.Flee) return 0f;
        ctx.chatPartner = Mgr.FindChatPartner(brain, ctx);
        if (ctx.chatPartner == null) return 0f;                          // 附近没有能聊的人
        float s = Mgr.chatWeight;
        s *= 0.3f + ctx.npc.personality.sociability / 100f * 1.2f;       // 外向的人更爱聊
        if (ctx.mental == SanityLevel.Dazed) s *= 0.6f;
        if (ctx.mental == SanityLevel.Hallucinating) s *= 0.6f;
        if (ctx.holding) s *= 0.5f;
        return s;
    }

    public override IEnumerator Run(NpcIdleBrain brain, IdleContext ctx)
    {
        NpcIdleManager m = Mgr;
        NpcIdleBrain partner = ctx.chatPartner;
        if (partner == null) yield break;

        // 先记一个短冷却：就算这次没聊成（走不过去/被打断），也不会马上反复去找同一个人。
        brain.SetChatCooldown(8f);
        m.SetPairCooldown(brain.NpcId, partner.NpcId, m.pairCooldown);

        IdleChatSession s = new IdleChatSession(brain, partner, ctx.world);
        if (!partner.TryJoinAsPartner(s)) yield break;
        brain.BeginChatAsInitiator(s);
        partner.SetChatCooldown(8f);

        NpcData me = brain.Data;
        NpcData other = partner.Data;
        if (me == null || other == null) { s.Abort(); yield break; }
        s.topic = IdleChatRules.PickTopic(me, other, ctx.world);         // 话题由规则决定
        brain.SetAction("idle_act_chat", other.npcName);

        // 1. 走到对方身边（对方会停下来，转身看着你走过来）
        if (Vector2.Distance(brain.transform.position, partner.transform.position) > m.talkDistance + 0.3f)
        {
            Vector3 meet = m.GetMeetingPoint(partner.transform.position, brain.transform.position);
            if (!brain.MoveTo(meet, 1f)) { s.Abort(); yield break; }
            yield return null;
            float walk = 0f;
            while (brain.IsMoving && walk < 8f)
            {
                if (s.Ended) yield break;
                walk += Time.deltaTime;
                yield return null;
            }
            brain.StopMoving();
            if (s.Ended) yield break;
            if (Vector2.Distance(brain.transform.position, partner.transform.position) > m.talkDistance + 1f)
            {
                s.Abort(); // 走不过去，算了
                yield break;
            }
        }
        brain.Face(partner.transform.position);
        partner.Face(brain.transform.position);

        // 2. 要台词（来源可能是异步的，最多等 VoiceTimeout 秒；等不到就用本地台词）
        List<IdleChatLine> lines = null;
        bool got = false;
        IdleChat.Voice.Compose(s, result => { lines = result; got = true; });
        float wait = 0f;
        while (!got && wait < IdleChat.VoiceTimeout)
        {
            if (s.Ended) yield break;
            wait += Time.deltaTime;
            yield return null;
        }
        if (lines == null || lines.Count == 0) lines = LocalIdleChatVoice.Build(s);
        s.lines = lines;

        // 3. 你一句我一句
        foreach (IdleChatLine line in lines)
        {
            if (s.Ended) yield break;
            NpcIdleBrain speaker = line.byInitiator ? brain : partner;
            NpcIdleBrain listener = line.byInitiator ? partner : brain;
            if (speaker == null || listener == null) { s.Abort(); yield break; }

            float dur = IdleChat.LineDuration(line.text);
            speaker.Face(listener.transform.position);
            listener.Face(speaker.transform.position);
            speaker.Say(line.text, dur);
            if (m.logChatLines && LogManager.Instance != null)
            {
                NpcData sd = speaker.Data;
                if (sd != null) LogManager.Instance.AddLog(sd.npcName, sd.npcName + "：" + line.text, sd.id);
            }

            float t = 0f;
            while (t < dur + 0.35f)
            {
                if (s.Ended) yield break;
                t += Time.deltaTime;
                yield return null;
            }
        }

        // 4. 聊完：规则结算（关系、理智、记忆、一条行为日志）
        IdleChatRules.ApplyOutcome(s);
        s.Complete();
        brain.SetChatCooldown(IdleChatRules.RollCooldown(me));
        partner.SetChatCooldown(IdleChatRules.RollCooldown(other));
        yield return new WaitForSeconds(Random.Range(0.4f, 1f));
    }
}

// ------------------------------------------------------------
// 陪聊（被拉去聊天的一方）：停下脚步，转身看着对方，直到聊天结束。
// 分数恒为 0，不会被随机选中，只由 NpcIdleBrain.TryJoinAsPartner 直接启动。
// ------------------------------------------------------------
public class ChatPartnerActivity : IdleActivity
{
    public override string Id => "chat_partner";
    public override bool Interruptible => false;
    public override float Score(NpcIdleBrain brain, IdleContext ctx) => 0f;

    public override IEnumerator Run(NpcIdleBrain brain, IdleContext ctx)
    {
        IdleChatSession s = brain.ChatSession;
        if (s == null) yield break;
        brain.StopMoving();
        NpcData other = s.initiator != null ? s.initiator.Data : null;
        if (other != null) brain.SetAction("idle_act_chat", other.npcName);

        float start = Time.time;
        while (!s.Ended)
        {
            if (s.initiator == null || Time.time - start > 40f) // 发起方没了 / 拖太久：散场
            {
                s.Abort();
                yield break;
            }
            brain.Face(s.initiator.transform.position);
            yield return null;
        }
        yield return new WaitForSeconds(Random.Range(0.5f, 1.2f));
    }
}
