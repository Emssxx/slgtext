using System.Collections.Generic;
using UnityEngine;

// 词条池：存放各维度的"性格/职业/性别"词条，供随机生成 NPC 时抽取。
// 每个性格词条自带：① 给 AI 的描述文字 ② 对 5 维性格数值的倾向调整。
// 想加新词条，就往对应的池子里加一行。
public static class TraitPool
{
    // —— 性别池 ——
    public static readonly string[] Genders = { "男", "女" };

    // —— 职业池（影响背景、崩解疯话的职业残留）——
    public static readonly string[] Professions =
    {
        "教师", "医生", "军人", "消防员", "学生",
        "工程师", "厨师", "警察", "记者", "农民"
    };
    // 日文职业名（和上面一一对应，顺序不能乱）。日语界面生成 NPC 时用。
    public static readonly string[] ProfessionsJa =
    {
        "教師", "医者", "軍人", "消防士", "学生",
        "エンジニア", "料理人", "警察官", "記者", "農家"
    };

    // —— 一个性格词条：名字 + 给AI的描述 + 对性格数值的倾向 ——
    public class Trait
    {
        public string name;        // 词条名，如"寡言"
        public string desc;        // 给 AI 的描述，如"你话很少，习惯用短句"
        public int bravery;        // 下面五个是对性格维度的增量调整（可正可负）
        public int altruism;
        public int caution;
        public int sociability;
        public int optimism;

        public string nameJa;      // 日文词条名
        public string descJa;      // 日文描述（日语界面时喂给 AI，避免中文混进日语提示词）

        // 按当前界面语言取词条名 / 描述。
        public string Name => LanguageGuard.IsJp ? nameJa : name;
        public string Desc => LanguageGuard.IsJp ? descJa : desc;

        public Trait(string name, string desc, string nameJa, string descJa,
                     int bravery = 0, int altruism = 0, int caution = 0,
                     int sociability = 0, int optimism = 0)
        {
            this.name = name; this.desc = desc; this.nameJa = nameJa; this.descJa = descJa;
            this.bravery = bravery; this.altruism = altruism; this.caution = caution;
            this.sociability = sociability; this.optimism = optimism;
        }
    }

    // —— 维度A：主性格（每个NPC抽1个）——
    public static readonly Trait[] MainTraits =
    {
        new Trait("开朗", "你性格开朗，遇事乐观，爱笑。", "朗らか", "あなたは明るい性格で、何事にも前向きで、よく笑う。", optimism: +25, sociability: +15),
        new Trait("阴沉", "你性格阴郁，话里常带悲观。", "陰気", "あなたは陰気な性格で、言葉にはいつも悲観がにじむ。", optimism: -25, sociability: -10),
        new Trait("暴躁", "你脾气火爆，容易被激怒。", "短気", "あなたは気性が荒く、すぐに怒る。", bravery: +15, caution: -15),
        new Trait("冷静", "你遇事沉着，很少慌乱。", "冷静", "あなたは落ち着いていて、めったに取り乱さない。", caution: +20, optimism: +5),
        new Trait("怯懦", "你胆小，害怕危险，容易退缩。", "臆病", "あなたは臆病で、危険を恐れ、すぐに尻込みする。", bravery: -25, caution: +15),
        new Trait("勇敢", "你胆大，敢于面对危险。", "勇敢", "あなたは度胸があり、危険に立ち向かえる。", bravery: +25, caution: -10),
        new Trait("偏执", "你多疑，总觉得有人针对自己。", "疑り深い", "あなたは疑り深く、いつも誰かに狙われている気がしている。", caution: +20, sociability: -15),
        new Trait("麻木", "你对一切都漠不关心，情绪平淡。", "無感動", "あなたは何事にも無関心で、感情の起伏がない。", optimism: -10, sociability: -20),
    };

    // —— 维度B：社交性（每个NPC抽1个）——
    public static readonly Trait[] SocialTraits =
    {
        new Trait("话痨", "你很爱说话，停不下来。", "おしゃべり", "あなたは話すのが大好きで、口が止まらない。", sociability: +30),
        new Trait("寡言", "你话很少，习惯用短句。", "無口", "あなたは口数が少なく、短い言葉で話す。", sociability: -30),
        new Trait("毒舌", "你说话尖刻，爱挖苦人。", "毒舌", "あなたは口が悪く、皮肉を言うのが好きだ。", sociability: +10, altruism: -15),
        new Trait("温和", "你说话温柔，待人和气。", "穏やか", "あなたは話し方が優しく、人当たりが柔らかい。", altruism: +20, sociability: +10),
    };

    // —— 维度C：癖好/创伤（每个NPC抽1个，可选）——
    public static readonly Trait[] QuirkTraits =
    {
        new Trait("念旧", "你常常想起过去的人和事。", "懐古的", "あなたはよく昔の人や出来事を思い出す。", optimism: -5),
        new Trait("洁癖", "你对脏乱无法忍受。", "潔癖", "あなたは汚れや散らかりに耐えられない。", caution: +10),
        new Trait("怕黑", "你害怕黑暗，夜里格外不安。", "暗所恐怖", "あなたは暗闇が怖く、夜は特に落ち着かない。", bravery: -10, caution: +10),
        new Trait("失去过家人", "你失去过至亲，心里有道伤。", "家族を失った", "あなたは大切な家族を失い、心に傷を抱えている。", optimism: -15, altruism: +10),
        new Trait("信教", "你信仰某种神明，靠它支撑精神。", "信心深い", "あなたはある神を信じていて、それを心の支えにしている。", optimism: +15, caution: +5),
    };

    // 从一个数组里随机抽一个。
    public static Trait PickOne(Trait[] pool)
    {
        return pool[Random.Range(0, pool.Length)];
    }
    public static string PickOne(string[] pool)
    {
        return pool[Random.Range(0, pool.Length)];
    }
}
