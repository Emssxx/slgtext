using System;
using System.Collections.Generic;

// 本地台词（不走 LLM，永远可用；LLM 配音忙/失败/超时时的兜底）：按话题从剧本里挑一段，两人你一句我一句。
// 每段剧本按"发起方、对方、发起方、对方……"的顺序交替。中日两套，按界面语言选。
// 占位符（按"说这句话的人"来替换）：
//   {me} 自己的名字　{you} 对方的名字　{myjob} 自己以前的职业　{yourjob} 对方以前的职业　{day} 第几天
// 加台词：往对应话题的数组里加一段即可（中日两边最好成对加）。
public class LocalIdleChatVoice : IIdleChatVoice
{
    public void Compose(IdleChatSession session, Action<List<IdleChatLine>> done)
    {
        if (done != null) done(Build(session));
    }

    public void Cancel(IdleChatSession session) { } // 同步生成，没有要取消的

    // 生成一场闲聊的台词（同步）。
    public static List<IdleChatLine> Build(IdleChatSession s)
    {
        List<IdleChatLine> result = new List<IdleChatLine>();
        NpcData a = s.A, b = s.B;
        if (a == null || b == null) return result;

        bool jp = LanguageGuard.IsJp;
        string[][] pool = GetPool(s.topic, jp);
        if (pool == null || pool.Length == 0) pool = GetPool(IdleChatTopic.SmallTalk, jp);
        string[] script = pool[PickIndex(s.topic, pool.Length)];

        for (int i = 0; i < script.Length; i++)
        {
            bool byA = i % 2 == 0;
            NpcData speaker = byA ? a : b;
            NpcData listener = byA ? b : a;
            result.Add(new IdleChatLine(byA, Fill(script[i], speaker, listener, s.world)));
        }
        return result;
    }

    // 同一话题不连着用同一段剧本。
    static readonly Dictionary<IdleChatTopic, int> _lastScript = new Dictionary<IdleChatTopic, int>();
    static int PickIndex(IdleChatTopic topic, int count)
    {
        int idx = UnityEngine.Random.Range(0, count);
        if (count > 1 && _lastScript.TryGetValue(topic, out int last) && last == idx)
            idx = (idx + 1 + UnityEngine.Random.Range(0, count - 1)) % count;
        _lastScript[topic] = idx;
        return idx;
    }

    static string Fill(string line, NpcData speaker, NpcData listener, WorldState w)
    {
        return line
            .Replace("{me}", speaker.npcName)
            .Replace("{you}", listener.npcName)
            .Replace("{myjob}", Job(speaker))
            .Replace("{yourjob}", Job(listener))
            .Replace("{day}", w != null ? w.day.ToString() : "?");
    }

    static string Job(NpcData n)
    {
        return string.IsNullOrEmpty(n.profession) ? LanguageGuard.Pick("普通人", "ごく普通の人") : n.profession;
    }

    static string[][] GetPool(IdleChatTopic t, bool jp)
    {
        switch (t)
        {
            case IdleChatTopic.SmallTalk:     return jp ? SmallTalkJa : SmallTalkZh;
            case IdleChatTopic.Past:          return jp ? PastJa : PastZh;
            case IdleChatTopic.Food:          return jp ? FoodJa : FoodZh;
            case IdleChatTopic.Tired:         return jp ? TiredJa : TiredZh;
            case IdleChatTopic.Fear:          return jp ? FearJa : FearZh;
            case IdleChatTopic.Camp:          return jp ? CampJa : CampZh;
            case IdleChatTopic.Terminal:      return jp ? TerminalJa : TerminalZh;
            case IdleChatTopic.Friendly:      return jp ? FriendlyJa : FriendlyZh;
            case IdleChatTopic.Tense:         return jp ? TenseJa : TenseZh;
            case IdleChatTopic.Hallucination: return jp ? HallucinationJa : HallucinationZh;
        }
        return null;
    }

    // ============================================================
    // 剧本（偶数句 = 发起方，奇数句 = 对方）
    // ============================================================

    // —— 家常 ——
    static readonly string[][] SmallTalkZh =
    {
        new[] { "这里的天……一直是这个颜色吗？", "从醒过来那天起，就没变过。", "……真想再看一次蓝天。" },
        new[] { "你昨晚睡着了吗？", "一闭上眼，就会听见奇怪的声音。", "我也是……还以为只有我这样。" },
        new[] { "今天是第{day}天了吧？", "我已经数不清日子了。", "我在墙上划记号，一天一道。" },
        new[] { "{you}，你说外面的人还在找我们吗？", "……也许吧。也许早就当我们死了。" },
    };
    static readonly string[][] SmallTalkJa =
    {
        new[] { "ここの空……ずっとこの色なのか？", "目が覚めた日から、一度も変わってない。", "……もう一度、青空が見たいな。" },
        new[] { "昨日の夜、眠れた？", "目を閉じると、変な音が聞こえてくるんだ。", "自分もだ……自分だけかと思ってた。" },
        new[] { "今日で{day}日目だよな？", "もう日にちが分からなくなってきた。", "壁に印をつけてるんだ。一日一本ずつ。" },
        new[] { "{you}、外の人たちはまだ私たちを探してると思う？", "……たぶんね。もう死んだことにされてるかもしれないけど。" },
    };

    // —— 以前的生活 ——
    static readonly string[][] PastZh =
    {
        new[] { "{you}，你以前是做什么的？", "我？我以前是{myjob}。", "到了这种地方，以前的事好像都成了上辈子。" },
        new[] { "当{myjob}的时候，我总想着放个长假。", "现在倒是放了个很长的假。", "……这玩笑一点也不好笑。" },
        new[] { "{yourjob}的本事，在这里用得上吗？", "能用上的，我都会用。只要能活下去。" },
    };
    static readonly string[][] PastJa =
    {
        new[] { "{you}は、前は何をしてたんだ？", "私？前は{myjob}だった。", "こんな所に来ると、昔のことが前世みたいに思えるな。" },
        new[] { "{myjob}をやってた頃は、ずっと長い休みが欲しいと思ってた。", "今まさに、とんでもなく長い休みだな。", "……その冗談、全然笑えない。" },
        new[] { "{yourjob}の経験って、ここで役に立つと思う？", "使えるものは何でも使うさ。生き残るためならね。" },
    };

    // —— 食物 ——
    static readonly string[][] FoodZh =
    {
        new[] { "仓库里还剩多少吃的？", "省着点吃，应该还能撑几天。", "几天之后呢？", "……到时候再说吧。" },
        new[] { "我做梦都梦到热腾腾的饭菜。", "别说了，我肚子都叫了。" },
        new[] { "那些营养块，你吃得惯吗？", "没什么味道，但总比饿着强。" },
    };
    static readonly string[][] FoodJa =
    {
        new[] { "倉庫に食べ物はあとどれくらい残ってる？", "節約すれば、あと何日かはもつはずだ。", "その後は？", "……その時に考えよう。" },
        new[] { "夢にまで温かいご飯が出てくるよ。", "やめてくれ、腹が鳴ってきた。" },
        new[] { "あの栄養ブロック、もう慣れた？", "味はほとんどしないけど、飢えるよりはましだ。" },
    };

    // —— 疲惫 ——
    static readonly string[][] TiredZh =
    {
        new[] { "我的腿像灌了铅一样。", "去歇一会儿吧，这边我先看着。" },
        new[] { "你脸色很差，多久没合眼了？", "……记不清了。", "再这样下去，你会撑不住的。" },
        new[] { "好累……这里连时间都让人疲惫。", "撑住，待会儿我们轮流休息。" },
    };
    static readonly string[][] TiredJa =
    {
        new[] { "足が鉛みたいに重い。", "少し休んできなよ。ここは見ておくから。" },
        new[] { "顔色がひどいぞ。どれくらい寝てないんだ？", "……覚えてない。", "このままじゃ、体がもたないぞ。" },
        new[] { "疲れた……ここは時間の流れまで人を疲れさせる。", "踏ん張ろう。あとで交代で休めばいい。" },
    };

    // —— 营地周围的异状 ——
    static readonly string[][] FearZh =
    {
        new[] { "你有没有觉得，雾比昨天更近了？", "别说了……我不想去想。" },
        new[] { "刚才那边……是不是有东西在动？", "我什么都没看到。", "……但愿是我看错了。" },
        new[] { "这个地方……好像在呼吸。", "你也感觉到了？地面偶尔会轻轻起伏。" },
    };
    static readonly string[][] FearJa =
    {
        new[] { "霧、昨日より近づいてる気がしないか？", "やめてくれ……考えたくない。" },
        new[] { "さっき、あっちで……何か動かなかった？", "何も見えなかったけど。", "……見間違いだといいんだけど。" },
        new[] { "この場所……まるで呼吸してるみたいだ。", "あなたも感じた？ 地面がときどき、かすかに波打つんだ。" },
    };

    // —— 营地的事（终端机建成前）——
    static readonly string[][] CampZh =
    {
        new[] { "我们得想办法把这里弄得像个营地。", "先把能用的东西都收集起来吧。" },
        new[] { "你觉得这里安全吗？", "比外面强。至少我们还在一起。" },
        new[] { "要是有道墙挡一挡就好了。", "材料够的话，我们自己搭一道。" },
    };
    static readonly string[][] CampJa =
    {
        new[] { "ここを、ちゃんとした拠点にしないとな。", "まずは使えそうな物を全部集めよう。" },
        new[] { "ここは安全だと思う？", "外よりはましだ。少なくとも、みんな一緒にいる。" },
        new[] { "壁でもあれば、少しは安心できるのに。", "材料が足りれば、自分たちで建てよう。" },
    };

    // —— 终端机（建成后）——
    static readonly string[][] TerminalZh =
    {
        new[] { "那台终端机……你觉得它真的在帮我们吗？", "至少它让我们看清了周围。" },
        new[] { "终端里的那个声音，有时候让我觉得……它在观察我们。", "也许它只是想让我们活下去。" },
        new[] { "终端机亮起来以后，这里总算有点营地的样子了。", "希望有一天，它能带我们出去。" },
    };
    static readonly string[][] TerminalJa =
    {
        new[] { "あの端末……本当に私たちを助けてくれてると思う？", "少なくとも、周りが見えるようにはしてくれた。" },
        new[] { "端末の中のあの声、ときどき……こっちを観察してるように感じる。", "ただ、私たちを生かそうとしてるだけかもしれない。" },
        new[] { "端末が動き出してから、やっと拠点らしくなってきたな。", "いつか、ここから連れ出してくれるといいんだけど。" },
    };

    // —— 心里话（关系好）——
    static readonly string[][] FriendlyZh =
    {
        new[] { "有你在，我安心多了。", "彼此彼此。我们一定能出去的。" },
        new[] { "{you}，出去以后你想做什么？", "先好好睡一觉。然后……请你吃顿饭。", "说定了。" },
        new[] { "谢谢你一直照应我。", "换作是你，也会这么做的。" },
    };
    static readonly string[][] FriendlyJa =
    {
        new[] { "あなたがいてくれて、だいぶ心強いよ。", "お互い様だ。きっと、ここから出られる。" },
        new[] { "{you}、ここを出たら何がしたい？", "まずはぐっすり眠る。それから……ご飯でもおごるよ。", "約束だからな。" },
        new[] { "いつも気にかけてくれて、ありがとう。", "あなたでも、きっと同じことをしたさ。" },
    };

    // —— 不满（关系差）——
    static readonly string[][] TenseZh =
    {
        new[] { "……你又在看什么？", "没什么。少管我。" },
        new[] { "上次分东西，你是不是多拿了？", "你非要在这种时候算这个吗？" },
        new[] { "我不太信得过你，{you}。", "随你。反正我也没指望你。" },
    };
    static readonly string[][] TenseJa =
    {
        new[] { "……何をじろじろ見てる？", "別に。放っておいてくれ。" },
        new[] { "この前の配給、多めに取っただろ？", "こんな時に、そんなことを言い出すのか？" },
        new[] { "正直、{you}のことはあまり信用できない。", "好きにしろ。こっちも期待してない。" },
    };

    // —— 幻觉（发起方在幻觉中，对方安抚）——
    static readonly string[][] HallucinationZh =
    {
        new[] { "你听见了吗？那边有人在叫我的名字。", "……那边什么都没有。你还好吗？" },
        new[] { "别回头……它就站在你身后。", "（回头看了看）……{you}，那里没有人。", "……它刚才明明就在那儿。" },
        new[] { "仓库里的东西……是不是都不见了？", "没有，我刚去看过，都还在。", "……是吗。" },
    };
    static readonly string[][] HallucinationJa =
    {
        new[] { "聞こえた？ あっちで誰かが私の名前を呼んでる。", "……あっちには何もないよ。大丈夫か？" },
        new[] { "振り向くな……そいつ、あなたの真後ろに立ってる。", "（振り返って）……{you}、誰もいないよ。", "……さっきまで、確かにそこにいたんだ。" },
        new[] { "倉庫の物……全部なくなってないか？", "いや、さっき見てきたけど、ちゃんとあるよ。", "……そうか。" },
    };
}
