using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

// NPC 闲聊的 LLM 配音（IIdleChatVoice 的实现）。
// 遵循"LLM配音、代码做决定"：谁和谁聊、聊什么话题、聊完怎么结算，全由代码（IdleChatRules）决定；
// LLM 只负责按两人的人设、关系、精神状态和话题，写出这几句台词。
// 走 AIClientHub.SendBackground（后台请求）：只在本地模型空闲时生成，玩家一说话就让路。
// 忙 / 失败 / 超时 / 语言不对 / 格式不对 → 回调 null，ChatActivity 自动改用本地台词，玩家看不出区别。
public class LlmIdleChatVoice : IIdleChatVoice
{
    IdleChatSession _pending; // 正在等 LLM 生成台词的那场闲聊

    public void Compose(IdleChatSession session, Action<List<IdleChatLine>> done)
    {
        NpcIdleManager mgr = NpcIdleManager.Instance;
        NpcData a = session.A, b = session.B;
        if (mgr == null || !mgr.llmVoice || UnityEngine.Random.value > mgr.llmVoiceChance
            || AIClientHub.Instance == null || a == null || b == null)
        {
            done(null);
            return;
        }

        int count = RollLineCount(session.topic);
        string sys = BuildSystemPrompt(session, a, b, count);
        string usr = LanguageGuard.Pick("请写出这段对话。", "この会話を書いてください。");

        bool sent = AIClientHub.Instance.SendBackground(sys, usr, raw =>
        {
            if (_pending == session) _pending = null;
            List<IdleChatLine> lines = Parse(raw, a, b, count);
            if (lines != null)
                Debug.Log($"[闲聊配音] {a.npcName} × {b.npcName}（{session.topic}）LLM 台词 {lines.Count} 句");
            else if (!string.IsNullOrEmpty(raw))
                Debug.Log("[闲聊配音] LLM 结果不可用，改用本地台词：" + raw);
            done(lines);
        }, true);

        if (sent) _pending = session;
        else done(null); // 本地模型正忙（玩家在说话等）/ 云端模式：直接用本地台词
    }

    // 闲聊散场或等太久：取消还在生成的请求，别占着显卡。
    public void Cancel(IdleChatSession session)
    {
        if (session == null || _pending != session) return;
        _pending = null;
        if (AIClientHub.Instance != null) AIClientHub.Instance.CancelBackground();
    }

    // ============ 提示词（整段按当前界面语言写）============

    static string BuildSystemPrompt(IdleChatSession s, NpcData a, NpcData b, int count)
    {
        bool jp = LanguageGuard.IsJp;
        string context = PromptContext.WorldSummary(s.world);
        StringBuilder sb = new StringBuilder();
        if (jp)
        {
            sb.AppendLine("あなたは亜空間サバイバルドラマの脚本家で、拠点にいる二人の生存者の短い会話を書く。");
            sb.AppendLine("【状況】普通の人々が「亜空間」と呼ばれる異常な領域に巻き込まれた。時間も空間も信用できず、長くいるほど精神がむしばまれる。" +
                          "彼らは廃墟になった拠点で必死に生き延びている。本当の恐怖は日常の言葉ににじむもので、毎回「怖い」と叫ぶ必要はない。");
            sb.Append(context);
            sb.AppendLine(CharacterCard("A", a, b, true));
            sb.AppendLine(CharacterCard("B", b, a, true));
            sb.AppendLine("【この会話の内容】" + TopicDesc(s.topic, true));
            sb.AppendLine("【書き方のルール】");
            sb.AppendLine("1. 全部で" + count + "行。AとBが交互に話し、最初の行はAが話す。");
            sb.AppendLine("2. 各行は実際に口に出す台詞だけ。話し言葉で短く（1行30文字以内）、それぞれの性格・話し方・精神状態に合わせる。");
            sb.AppendLine("3. 名前の前置き、ナレーション、動作の描写、かぎかっこは書かない。");
            sb.AppendLine("4. 「ゲーム」「脚本」「AI」「プレイヤー」には絶対に触れない。");
            sb.AppendLine("5. 自然な日本語だけで書き、中国語を混ぜない。");
            sb.AppendLine("【出力形式】JSONオブジェクトを1つだけ出力し、それ以外は何も書かないこと：");
            sb.Append("{\"lines\": [\"Aの最初の台詞\", \"Bの返事\", \"……\"]}");
        }
        else
        {
            sb.AppendLine("你是一部亚空间生存剧的编剧，要写营地里两名幸存者之间的一小段对话。");
            sb.AppendLine("【处境】一群普通人被卷进了名为「亚空间」的异常领域：时间和空间都不可靠，待得越久精神越被侵蚀。" +
                          "他们在一处废弃营地里挣扎求生。真正的恐惧藏在日常的话里，不要每句都喊害怕。");
            sb.Append(context);
            sb.AppendLine(CharacterCard("A", a, b, false));
            sb.AppendLine(CharacterCard("B", b, a, false));
            sb.AppendLine("【这段对话的内容】" + TopicDesc(s.topic, false));
            sb.AppendLine("【写作要求】");
            sb.AppendLine("1. 一共 " + count + " 句，A 和 B 轮流说，第一句由 A 说。");
            sb.AppendLine("2. 每句都是直接说出口的话：口语、简短（每句不超过 25 个字），符合各自的性格、说话风格和精神状态。");
            sb.AppendLine("3. 不要写名字前缀，不要写旁白和动作描写，不要加引号。");
            sb.AppendLine("4. 绝不提到\"游戏\"\"剧本\"\"编剧\"\"AI\"\"玩家\"。");
            sb.AppendLine("5. 只用自然的简体中文，不要夹杂日语。");
            sb.AppendLine("【输出格式】只输出一个 JSON 对象，不要任何多余文字：");
            sb.Append("{\"lines\": [\"A 的第一句\", \"B 的回应\", \"……\"]}");
        }
        return sb.ToString();
    }

    static string CharacterCard(string tag, NpcData n, NpcData other, bool jp)
    {
        string traits = (n.traits != null && n.traits.Count > 0) ? string.Join("、", n.traits) : (jp ? "特になし" : "无");
        string job = string.IsNullOrEmpty(n.profession) ? "不明" : n.profession;
        string style = string.IsNullOrEmpty(n.speakingStyle) ? "普通" : n.speakingStyle;
        bool male = n.gender != null && n.gender.Contains("男");
        string brief = NpcActors.Brief(n, jp); // 演员提示词里的固定事实（年龄、家庭、宠物、爱好）：闲聊聊到过去时不会乱编
        if (jp)
            return "【人物" + tag + "】" + n.npcName + "（" + (male ? "男性" : "女性") + "、以前は" + job + "）\n" +
                   (brief.Length > 0 ? "・経歴：" + brief + "\n" : "") +
                   "・性格：" + traits + "\n" +
                   "・話し方：" + style + "\n" +
                   "・精神状態：" + Mental(n, true) + "\n" +
                   "・体の状態：" + NpcHealth.BriefLine(n, true) + "（会話ににじませる）\n" +
                   "・" + other.npcName + "への気持ち：" + Relation(n, other, true);
        return "【人物" + tag + "】" + n.npcName + "（" + (male ? "男" : "女") + "，以前是" + job + "）\n" +
               (brief.Length > 0 ? "・背景：" + brief + "\n" : "") +
               "・性格：" + traits + "\n" +
               "・说话风格：" + style + "\n" +
               "・精神状态：" + Mental(n, false) + "\n" +
               "・身体状况：" + NpcHealth.BriefLine(n, false) + "（聊天时要体现出来）\n" +
               "・对" + other.npcName + "的感觉：" + Relation(n, other, false);
    }

    static string Mental(NpcData n, bool jp)
    {
        switch (MentalState.GetLevel(n))
        {
            case SanityLevel.Dazed:
                return jp ? "ぼんやりしていて、話に集中できない" : "恍惚，注意力很难集中";
            case SanityLevel.Hallucinating:
                return jp ? "幻覚を見ていて、存在しないものが見える・聞こえる" : "正处于幻觉中，会看见、听见不存在的东西";
            default:
                return jp ? "正常" : "清醒";
        }
    }

    static string Relation(NpcData from, NpcData to, bool jp)
    {
        if (NpcHealth.DistrustsEveryone(from)) return jp ? "誰も信じられず、相手が自分を陥れようとしていると疑っている" : "谁也不信，怀疑对方想害自己";
        float aff = RelationshipManager.AffinityOf(from, to.id); // 饥饿等状态会让好感暂时变低
        if (aff >= 40f) return jp ? "とても信頼していて、親しい" : "非常信赖、很亲近";
        if (aff >= 15f) return jp ? "仲が良い" : "关系不错";
        if (aff > -15f) return "普通";
        if (aff > -40f) return jp ? "少し反感がある" : "有些反感";
        return jp ? "ひどく嫌っている" : "非常讨厌";
    }

    // 话题 → 给编剧的一句说明（话题本身由 IdleChatRules 按规则选好）。
    static string TopicDesc(IdleChatTopic t, bool jp)
    {
        switch (t)
        {
            case IdleChatTopic.Past:
                return jp ? "ここに来る前の暮らしや、昔の仕事について。" : "聊起被卷进来之前的生活，和各自以前的职业。";
            case IdleChatTopic.Food:
                return jp ? "食料や倉庫の残りについての心配（愚痴や、苦し紛れの冗談でもいい）。" : "担心食物和仓库的存量（可以抱怨，也可以苦中作乐）。";
            case IdleChatTopic.Tired:
                return jp ? "二人（あるいはどちらか）がひどく疲れていて、休むよう声をかけ合う。" : "两人（或其中一人）很疲惫，互相提醒休息。";
            case IdleChatTopic.Fear:
                return jp ? "拠点の周りの不気味な兆し（霧、影、妙な音）への不安。はっきり言わず、遠回しに。" : "营地周围诡异的迹象（雾、影子、奇怪的声音）让人不安，说得含蓄一些。";
            case IdleChatTopic.Camp:
                return jp ? "拠点をどうやってもっと安全で住みやすくするか（物資集め、壁を建てるなど）。" : "讨论怎么把营地弄得更安全、更像样（收集物资、搭墙之类）。";
            case IdleChatTopic.Terminal:
                return jp ? "AI端末と、その中の「声」について。信じるか、疑うか。" : "聊那台 AI 终端机，以及终端里那个「声音」——信任还是怀疑。";
            case IdleChatTopic.Friendly:
                return jp ? "二人は仲が良い。励まし合ったり、本音を打ち明けたりする。" : "两人关系很好，互相打气，或说几句心里话。";
            case IdleChatTopic.Tense:
                return jp ? "二人は仲が悪い。言葉にとげがあり、ちょっとした言い争いになる（手は出さない。長引かせない）。" : "两人关系很差，话里带刺，起了点小争执（不动手，也不要吵太久）。";
            case IdleChatTopic.Hallucination:
                return jp ? "Aは幻覚を見ていて、存在しないものが見える・聞こえると言う。Bは戸惑いながらなだめるか、否定する。" : "A 正在出现幻觉，说自己看见/听见了不存在的东西；B 困惑地安抚或反驳。";
            default:
                return jp ? "睡眠や空の色、何日経ったかなど、日常のささいなこと。淡々とした中に少しの不安がにじむ。" : "随便聊聊日常小事（睡眠、天色、日子过了多久之类），平淡里带着一丝不安。";
        }
    }

    static int RollLineCount(IdleChatTopic t)
    {
        if (t == IdleChatTopic.Tense) return UnityEngine.Random.value < 0.5f ? 2 : 3; // 吵架说两三句就散
        float r = UnityEngine.Random.value;
        return r < 0.2f ? 2 : (r < 0.75f ? 3 : 4);
    }

    // ============ 解析 + 检查（任何一步不合格都整段作废，改用本地台词）============

    static List<IdleChatLine> Parse(string raw, NpcData a, NpcData b, int count)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        List<string> texts = ExtractLines(raw);
        if (texts == null || texts.Count == 0) return null;

        Language lang = LanguageGuard.IsJp ? Language.Japanese : Language.Chinese;
        List<IdleChatLine> result = new List<IdleChatLine>();
        foreach (string t in texts)
        {
            if (result.Count >= count) break;
            bool byA = result.Count % 2 == 0;
            string line = CleanLine(t, byA ? a : b, a, b);
            if (string.IsNullOrEmpty(line)) continue;
            if (line.Length > 60) return null;                            // 太长：多半跑偏了
            if (!LanguageGuard.TextMatches(line, lang, true)) return null; // 语言不对
            result.Add(new IdleChatLine(byA, line));
        }
        return result.Count >= 2 ? result : null;
    }

    // 从模型输出里取出台词列表。首选 {"lines":[...]}，也兼容几种常见的跑偏格式。
    static List<string> ExtractLines(string raw)
    {
        try
        {
            int s = raw.IndexOf('{'), e = raw.LastIndexOf('}');
            if (s < 0 || e <= s) return null;
            JObject obj = JObject.Parse(raw.Substring(s, e - s + 1));

            JArray arr = obj["lines"] as JArray;
            if (arr == null)
                foreach (JProperty p in obj.Properties())
                    if (p.Value is JArray found) { arr = found; break; }

            List<string> list = new List<string>();
            if (arr != null)
            {
                foreach (JToken tok in arr)
                {
                    if (tok.Type == JTokenType.String) list.Add((string)tok);
                    else if (tok is JObject o) // 例如 {"speaker":"A","text":"…"}
                    {
                        JToken v = o["text"] ?? o["line"] ?? o["content"] ?? o["dialogue"];
                        if (v != null && v.Type == JTokenType.String) list.Add((string)v);
                    }
                }
                return list;
            }

            // 没有数组：按顺序收集所有字符串字段（例如 {"A":"…","B":"…"}）
            foreach (JProperty p in obj.Properties())
                if (p.Value.Type == JTokenType.String) list.Add((string)p.Value);
            // 只有一个字段、里面用换行分了好几句
            if (list.Count == 1 && list[0].Contains("\n"))
                list = new List<string>(list[0].Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
            return list;
        }
        catch
        {
            return null;
        }
    }

    // 清洗一句台词：去掉说话人前缀、自报名字、穿帮词、外层引号。
    static string CleanLine(string raw, NpcData speaker, NpcData a, NpcData b)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string s = raw.Trim();

        // "A：" "人物B:" "名字：" 这类前缀（模型常会自作主张加上）
        List<string> labels = new List<string> { "[ABＡＢ]" };
        if (!string.IsNullOrEmpty(a.npcName)) labels.Add(Regex.Escape(a.npcName));
        if (!string.IsNullOrEmpty(b.npcName)) labels.Add(Regex.Escape(b.npcName));
        s = Regex.Replace(s, @"^\s*(?:人物)?\s*(?:" + string.Join("|", labels) + @")\s*[：:]\s*", "");

        s = DialogueCleaner.Clean(s, speaker.npcName); // 去自报名字、"玩家"等穿帮词
        return StripQuotes(s.Trim());
    }

    static string StripQuotes(string s)
    {
        if (s.Length < 2) return s;
        char f = s[0], l = s[s.Length - 1];
        if ((f == '「' && l == '」') || (f == '『' && l == '』') || (f == '“' && l == '”') || (f == '"' && l == '"'))
            return s.Substring(1, s.Length - 2).Trim();
        return s;
    }
}
