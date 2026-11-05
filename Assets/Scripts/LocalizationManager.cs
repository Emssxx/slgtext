using System;
using System.Collections.Generic;
using UnityEngine;

// 语言枚举。要加更多语言就往这里加。
public enum Language { Chinese, Japanese }

// 语言管理器（单例）：管理当前语言、翻译表，提供取文本的方法。
// 切换语言时触发 OnLanguageChanged 事件，UI 监听它来刷新。
public class LocalizationManager : MonoBehaviour
{
    public static LocalizationManager Instance;

    public Language current = Language.Chinese; // 当前语言，默认中文

    // 语言切换时通知（UI 订阅它来刷新自己）。
    public static event Action OnLanguageChanged;

    // 翻译表：key → (中文, 日文)。以后加文本就往这里加一行。
    private Dictionary<string, (string zh, string ja)> _table;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildTable();
    }

    // 构建翻译表。每个 key 对应中文和日文两种写法。
    void BuildTable()
    {
        _table = new Dictionary<string, (string, string)>
        {
            // —— NPC 状态卡 ——
            { "stat_health",  ("生命", "生命") },
            { "stat_hunger",  ("饥饿", "満腹") }, // 饥饿值：100 = 不饿
            { "stat_energy",  ("体力", "体力") },
            { "stat_sanity",  ("理智", "正気") },
            { "stat_trust",   ("信任", "信頼") },
            { "stat_water",   ("水分", "水分") },
            { "stat_healthy", ("健康", "健康") },
            { "gender_male",  ("男", "男性") },
            { "gender_female",("女", "女性") },
            // 由 5 维性格数值推出的词条（≥70 / ≤30 时显示在对话窗口的词条区）
            { "pers_brave_hi",     ("勇敢", "勇敢") },
            { "pers_brave_lo",     ("怯懦", "臆病") },
            { "pers_altruism_hi",  ("热心", "世話好き") },
            { "pers_altruism_lo",  ("自私", "利己的") },
            { "pers_caution_hi",   ("谨慎", "慎重") },
            { "pers_caution_lo",   ("鲁莽", "向こう見ず") },
            { "pers_social_hi",    ("外向", "社交的") },
            { "pers_social_lo",    ("内向", "内向的") },
            { "pers_optimism_hi",  ("乐观", "楽観的") },
            { "pers_optimism_lo",  ("悲观", "悲観的") },

            // —— 系统提示 ——
            { "day_start",    ("新的一天开始了", "新しい一日が始まった") },
            { "go_to",        ("前往", "向かう") },
            { "start_action", ("开始行动", "行動開始") },

            // —— 精神状态前缀 ——
            { "state_dazed",        ("（眼神涣散）", "（うつろな目）") },
            { "state_hallucinate",  ("（声音发颤）", "（声が震えている）") },
            { "state_broken",       ("（已不成人形）", "（もはや人の形ではない）") },

            // —— 按钮/界面 ——
            { "btn_build",    ("建造  [B]", "建築  [B]") }, // 建造模式切换按钮（含快捷键提示）

            // —— 对话框标题 / 输入框 ——
            { "chat_panel_title",  ("信息", "メッセージ") },
            { "chat_send",         ("发送", "送信") },
            { "chat_placeholder",  ("说点什么...", "何か話しかけて...") },

            // —— 系统提示 / 事件 ——
            { "system_speaker", ("系统", "システム") },
            { "danger_approach", ("【危险】有什么东西靠近了！", "【危険】何かが近づいてくる！") },
            // 整句模板（中日语序不同，所以不拆半句）。{0} = NPC 名字。
            { "now_talking_with", ("你现在正在和【{0}】对话。", "今、【{0}】と話しています。") },
            { "event_day_prefix", ("[第{0}天] ", "[{0}日目] ") },   // 事件栏每条前缀，{0} = 天数
            { "player_speaker",   ("你", "あなた") },               // 聊天栏里玩家自己的名字
            { "ai_no_response",       ("……（没有回应）", "……（返事がない）") },             // 本地AI请求失败时NPC的兜底台词
            { "ai_cloud_no_response", ("……（云端没有回应）", "……（クラウドから応答がない）") }, // 云端AI请求失败时NPC的兜底台词
            { "npc_dead_cannot_talk", ("{0} 已经死亡，无法与其对话。", "{0}はすでに亡くなっていて、話すことができない。") },

            // —— 新UI：对话框三栏标题 ——
            { "tab_chat", ("对话", "会話") },
            { "tab_event", ("事件", "イベント") },
            { "tab_log", ("日志", "ログ") },

            // —— 新UI：NPC列表 / 卡片 ——
            { "npc_list_title", ("幸存者列表", "生存者リスト") },
            { "btn_close", ("关闭", "閉じる") },

            // —— 新UI：建造菜单（顺便备好，以后用）——
            { "build_menu_title", ("建造", "建築") },
            { "build_wall", ("墙", "壁") },
            { "build_box", ("箱子", "箱") },

            // —— AI模型调试UI（AIModelDebugUI，右下角展示当前模型）——
            { "ai_debug_label_local",   ("本地", "ローカル") },   // 用本地Ollama时的前缀
            { "ai_debug_label_cloud",   ("云端", "クラウド") },   // 用云端Claude时的前缀

            // —— 事件横幅（ScreenEffectManager.PlayEventEffect，屏幕正中短暂出现的大字）——
            { "event_banner_space_warp",     ("空间扭曲", "空間の歪み") },
            { "event_banner_time_disorder",  ("时间失序", "時間の乱れ") },
            { "event_banner_whisper_vision", ("低语与幻象", "囁きと幻影") },
            { "event_banner_storm",          ("亚空间风暴", "亜空間の嵐") },

            // —— NPC行为日志（NpcDecisionRules.Execute写进日志栏，{0}=NPC名字）——
            { "log_eat_success", ("{0} 吃了{1}。", "{0}は{1}を口にした。") },
            { "log_eat_fail",    ("{0} 想吃东西，但仓库空了。", "{0}は何か食べようとしたが、倉庫は空だった。") },
            { "log_rest",        ("{0} 休息了一会儿。", "{0}はしばらく休んだ。") },
            { "log_gather",      ("{0} 外出搜寻，带回了一些物资。", "{0}は捜索に出て、物資を持ち帰った。") },
            { "log_flee",        ("{0} 因为身体危急在躲避危险。", "{0}は身の危険を感じて避難している。") },
            { "log_idle",        ("{0} 在营地待命。", "{0}は野営地で待機している。") },
            { "log_space_warp_victim", ("{0} 被空间扭曲卷走，出现在了别处。", "{0}は空間の歪みに飲み込まれ、別の場所に現れた。") },

            // —— 主界面（Main Menu）——
            { "btn_start", ("开始游戏", "ゲーム開始") },
            { "btn_quit", ("退出", "終了") },
            { "provider_local", ("本地", "ローカル") },
            { "provider_prefix", ("AI：", "AI：") },
            { "menu_hint", ("使用云端AI需要各自的API key。", "クラウドAIを使う場合は、各自のAPIキーが必要です。") },

            // —— 物品 / 仓库面板（StoragePanelUI、ItemUseRules）——
            { "storage_title",       ("营地仓库", "拠点の倉庫") },
            { "storage_target",      ("使用对象：", "使用対象：") },
            { "storage_no_target",   ("（未选择）", "（未選択）") },
            { "storage_empty",       ("仓库是空的。", "倉庫は空っぽだ。") },
            { "storage_pick_hint",   ("点击左侧物品查看详情。", "左の物品をクリックすると詳細が見られる。") },
            { "storage_use_on",      ("对 {0} 使用", "{0}に使う") },
            { "btn_use",             ("使用", "使う") },
            { "log_use_item",        ("{0} 使用了{1}。", "{0}は{1}を使った。") },
            { "item_fail_no_target", ("没有使用对象。先选择一个 NPC 对话。", "使用対象がいない。先にNPCを選んで会話しよう。") },
            { "item_fail_dead",      ("对方已经死亡。", "相手はもう死んでいる。") },
            { "item_fail_unknown",   ("未知的物品。", "不明な物品。") },
            { "item_fail_not_usable",("这个物品不能直接使用。", "この物品は直接使えない。") },
            { "item_fail_empty",     ("仓库里已经没有了。", "倉庫にもう残っていない。") },
            { "item_fail_broken",    ("已经崩解的人，药物也救不回来了。", "崩壊した者には、もう薬も効かない。") },
            { "item_cat_material",   ("建造材料（不能直接使用）", "建築素材（直接は使えない）") },
            { "item_cat_special",    ("亚空间特产（用途未明）", "亜空間の産物（用途不明）") },

            // —— AI 终端机 / 建造分类 / 科技 / 升级 ——
            { "terminal_name",        ("AI终端机", "AI端末") },
            { "terminal_built",       ("AI终端机建成（{0}）。", "AI端末が完成した（{0}）。") },
            { "tier_none",            ("未建终端机", "端末未建設") },
            { "tier_label",           ("Lv{0}", "Lv{0}") },
            { "build_cat_basic",      ("基础", "基礎") },
            { "build_cat_defense",    ("防御", "防御") },
            { "build_cat_production", ("生产", "生産") },
            { "build_cat_power",      ("电力", "電力") },
            { "build_cat_living",     ("生活", "生活") },
            { "build_locked_msg",     ("{0} 还不能建造：{1}", "{0}はまだ建てられない：{1}") },
            { "lock_already_built",   ("已建造（整局只能有一座）", "建設済み（1基のみ）") },
            { "lock_need_tier",       ("需要终端机 {0}", "端末 {0} が必要") },
            { "lock_need_tech",       ("需要科技「{0}」", "技術「{0}」が必要") },
            { "lock_terminal_offline",("终端机未在线", "端末がオフライン") },
            { "lock_no_materials",    ("材料不足", "素材が足りない") },
            { "lock_short_built",     ("已建", "建設済") },
            { "lock_short_tech",      ("需科技", "要技術") },
            { "cost_none",            ("无需材料", "素材不要") },
            { "tech_done",            ("已研究", "研究済み") },
            { "tech_researching",     ("研究中", "研究中") },
            { "upgrade_done",         ("已完成", "完了") },
            { "upgrade_to",           ("升级到 {0}", "{0} へアップグレード") },
            { "job_queued",           ("已加入任务队列：{0}", "タスクに追加：{0}") },
            { "job_already_queued",   ("已在任务队列中", "すでにタスクにある") },
            { "job_queue_full",       ("任务队列已满", "タスクキューが満杯") },
            { "job_done_craft",       ("终端机合成完成：{0} ×{1}", "端末の合成完了：{0} ×{1}") },
            { "job_done_research",    ("终端机研究完成：{0}", "端末の研究完了：{0}") },
            { "job_done_upgrade",     ("终端机升级完成：{0}", "端末のアップグレード完了：{0}") },
            { "term_tab_upgrade",     ("升级", "アップグレード") },
            { "term_tab_craft",       ("合成", "合成") },
            { "term_tab_tech",        ("科技", "技術") },
            { "term_tab_power",       ("能源", "エネルギー") },
            { "build_wire",           ("电线（连线）", "電線（接続）") },
            { "term_status_online",   ("在线", "オンライン") },
            { "term_status_building", ("建造中", "建設中") },
            { "term_status_offline",  ("离线", "オフライン") },
            { "term_slots",           ("算力槽 {0}/{1}　排队 {2}", "演算スロット {0}/{1}　待機 {2}") },
            { "term_slots_info",      ("算力槽：{0}（可同时处理 {0} 项任务）", "演算スロット：{0}（同時に{0}件処理できる）") },
            { "term_idle",            ("空闲中", "待機中") },
            { "term_max_level",       ("已达最高等级", "最高レベルに到達") },
            { "term_pick_hint",       ("点击左侧条目查看详情。", "左の項目をクリックすると詳細が見られる。") },
            { "term_cost",            ("材料：", "素材：") },
            { "term_time",            ("耗时 {0} 秒", "所要時間 {0} 秒") },
            { "term_require",         ("条件：", "条件：") },
            { "craft_out",            ("产出：{0} ×{1}", "産出：{0} ×{1}") },
            { "btn_craft",            ("合成", "合成する") },
            { "btn_research",         ("研究", "研究する") },
            { "btn_upgrade",          ("升级", "アップグレード") },

            // —— 闲置行为（NpcIdleBrain / IdleChat）——
            { "idle_act_stand",       ("在发呆", "ぼんやりしている") },
            { "idle_act_daze",        ("在出神", "放心している") },
            { "idle_act_rest",        ("在原地歇息", "その場で休んでいる") },
            { "idle_act_wander",      ("在营地里闲逛", "拠点をぶらついている") },
            { "idle_act_chat",        ("在和{0}聊天", "{0}と話している") },
            { "idle_log_chat",        ("{0} 和 {1} 聊了聊{2}。", "{0}と{1}は、{2}について言葉を交わした。") },
            { "idle_log_chat_tense",  ("{0} 和 {1} 起了点争执。", "{0}と{1}の間で、ちょっとした言い争いがあった。") },
            { "idle_mem_chat",        ("和{0}聊了聊{1}。", "{0}と{1}について話した。") },
            { "idle_mem_chat_tense",  ("和{0}起了点争执。", "{0}と少し言い争った。") },
            { "idle_topic_smalltalk", ("家常", "他愛もないこと") },
            { "idle_topic_past",      ("以前的生活", "昔の暮らし") },
            { "idle_topic_food",      ("食物", "食料") },
            { "idle_topic_tired",     ("疲惫", "疲れ") },
            { "idle_topic_fear",      ("营地周围的异状", "拠点の周りの異変") },
            { "idle_topic_camp",      ("营地的事", "拠点のこと") },
            { "idle_topic_terminal",  ("终端机", "端末") },
            { "idle_topic_friendly",  ("彼此的心事", "互いの気持ち") },
            { "idle_topic_tense",     ("彼此的不满", "お互いへの不満") },
            { "idle_topic_hallucination", ("看见的怪东西", "見えたという奇妙なもの") },

            // —— 需求行为（NpcNeedRunner）——
            { "need_act_eat",         ("去营地拿吃的", "食べ物を取りに拠点へ向かっている") },
            { "need_act_rest",        ("回营地休息", "拠点で休もうとしている") },
            { "need_act_gather",      ("外出搜寻物资", "物資を探しに出ている") },
            { "need_act_flee",        ("惊慌地逃回营地", "慌てて拠点へ逃げている") },

            // —— NPC 专属对话窗口（NpcChatWindow）——
            { "chatwin_title",        ("与 {0} 对话", "{0}との会話") },
            { "chatwin_status",       ("信任 {0} · {1}", "信頼 {0}・{1}") },
            { "chatwin_thinking",     ("{0} 在想……", "{0}が考えている……") },
            { "chatwin_rerouted",     ("（转而对 {0} 说）", "（{0}に話しかけた）") },
            { "chatwin_order_started",("（{0} 开始行动：{1}）", "（{0}が行動を開始：{1}）") },
            { "chatwin_placeholder",  ("说点什么，或下指令…（Enter 发送）", "話しかける／指示を出す…（Enterで送信）") },
            { "chatwin_dead",         ("已死亡", "死亡") },
            { "chatwin_header",       ("对话 — {0}", "会話 — {0}") },
            { "chatwin_you",          ("你（AI）", "あなた（AI）") },
            // —— NPC 自主意图（NpcIntentBrain）——
            { "intent_thinking",      ("在想接下来做什么", "次に何をするか考え中") },
            { "intent_reason",        ("（{0}心想：{1}）", "（{0}の考え：{1}）") },
            { "intent_say",           ("{0}：「{1}」", "{0}：「{1}」") },
            { "intent_log_build",     ("{0} 打算在{1}建一座{2}。", "{0}は{1}に{2}を建てるつもりだ。") },
            { "intent_log_gather",    ("{0} 打算去采集{1}。", "{0}は{1}を採りに行くつもりだ。") },
            { "intent_log_scavenge",  ("{0} 打算出营地找些吃的。", "{0}は拠点の外へ食べ物を探しに行くつもりだ。") },
            { "intent_log_rest",      ("{0} 打算歇一会儿。", "{0}は少し休むつもりだ。") },
            { "intent_log_regroup",   ("{0} 打算回到营地中心，待在大家身边。", "{0}は拠点の真ん中に戻り、みんなのそばにいるつもりだ。") },
            { "intent_need_mat",      ("{0} 发现{1}还差 {2} 个，先去采集。", "{0}は{1}があと{2}個足りないので、先に採りに行く。") },
            { "intent_took_mat",      ("{0} 从仓库取出了 {1}，去施工。", "{0}は倉庫から{1}を取り出し、建設に向かった。") },
            { "intent_give_up",       ("{0} 凑不齐材料，放弃了建{1}的计划。", "{0}は材料が集まらず、{1}を建てる計画をあきらめた。") },
            { "intent_no_spot",       ("{0} 想建{1}，但找不到合适的地方。", "{0}は{1}を建てたかったが、いい場所が見つからなかった。") },
            { "intent_interrupted",   ("{0} 放下了自己的计划。", "{0}は自分の計画を中断した。") },
            { "intent_act_fetch",     ("去仓库取材料：{0}", "倉庫へ材料を取りに行く：{0}") },
            { "intent_area_north",    ("营地北边", "拠点の北側") },
            { "intent_area_south",    ("营地南边", "拠点の南側") },
            { "intent_area_east",     ("营地东边", "拠点の東側") },
            { "intent_area_west",     ("营地西边", "拠点の西側") },
            // —— NPC 之间的关系（RelationshipManager）——
            { "rel_label_hate",       ("仇恨", "憎しみ") },
            { "rel_label_dislike",    ("厌恶", "嫌悪") },
            { "rel_label_suspicious", ("怀疑", "疑い") },
            { "rel_label_close",      ("亲近", "親しみ") },
            { "rel_label_friendly",   ("友善", "好意") },
            { "rel_label_neutral",    ("普通", "普通") },
            { "rel_label_respect",    ("敬重", "尊敬") },
            { "rel_label_contempt",   ("看不起", "軽蔑") },
            { "rel_changed",          ("{0} 对 {1} 的感觉变成了：{2}", "{0}の{1}への気持ちが変わった：{2}") },
            { "rel_reason_built_together", ("第{0}天：和{1}一起建成了{2}", "{0}日目：{1}と一緒に{2}を完成させた") },
            { "rel_reason_initiative",("第{0}天：{1}主动修好了{2}", "{0}日目：{1}が自分から{2}を建てた") },
            { "rel_reason_helped",    ("第{0}天：{1}给我用了{2}", "{0}日目：{1}が私に{2}を使ってくれた") },
            { "rel_reason_quarrel",   ("第{0}天：和{1}起了口角", "{0}日目：{1}と口論になった") },
            { "rel_reason_chat_good", ("第{0}天：和{1}聊得很投机", "{0}日目：{1}と話が弾んだ") },
            { "rel_reason_chat_past", ("第{0}天：和{1}聊起了过去", "{0}日目：{1}と昔の話をした") },
            { "rel_reason_chat_cold", ("第{0}天：和{1}聊得很僵", "{0}日目：{1}との会話がぎこちなかった") },
            { "rel_reason_agreed",    ("第{0}天：{1}答应帮我{2}", "{0}日目：{1}が{2}のを引き受けてくれた") },
            { "rel_reason_refused",   ("第{0}天：{1}拒绝了我的请求", "{0}日目：{1}に頼みを断られた") },
            { "rel_reason_helped_gather", ("第{0}天：{1}帮我采回了{2}", "{0}日目：{1}が{2}を採ってきてくれた") },
            // —— NPC 之间的请求（NpcIntentBrain 第 3 阶段）——
            { "social_job_gather",    ("去采集{0}", "{0}を採りに行く") },
            { "social_job_build",     ("一起建{0}", "一緒に{0}を建てる") },
            { "social_ask_bubble",    ("{0}，能帮我{1}吗？", "{0}、{1}のを手伝ってくれないか？") },
            { "social_log_ask",       ("{0} 请 {1} 帮忙{2}。", "{0}は{1}に{2}のを頼んだ。") },
            { "social_log_reply",     ("{0} {1}：「{2}」", "{0}は{1}：「{2}」") },
            { "social_outcome_yes",   ("答应了", "引き受けた") },
            { "social_outcome_no",    ("拒绝了", "断った") },
            { "social_reply_yes",     ("行，我这就去。", "わかった、すぐ行く。") },
            { "social_reply_no",      ("……我不想去。", "……行きたくない。") },
            { "social_reply_busy",    ("我手上还有活。", "今は手が離せない。") },
            { "social_reply_tired",   ("我累得动不了了。", "もうくたくただ。") },
            { "social_reply_cannot",  ("我……动不了。", "体が……動かない。") },
            { "social_log_no_show",   ("{0} 没能过来帮忙。", "{0}は手伝いに来られなかった。") },
            { "social_log_join",      ("{0} 过来和 {1} 一起施工。", "{0}は{1}と一緒に建設を始めた。") },
            // —— 领导与分组（NpcSociety 第 4 阶段）——
            { "society_meeting_start",("营地的人聚到一起开会，要推选一个领导。", "拠点のみんなが集まり、リーダーを選ぶ話し合いを始めた。") },
            { "society_elected",      ("大家推举 {0} 当领导（{1} / {2} 票）。", "みんなは{0}をリーダーに選んだ（{1}／{2}票）。") },
            { "society_no_confidence",("大家对 {0} 越来越不服，领导的位置空了出来。", "みんなは{0}に従わなくなり、リーダーの座が空いた。") },
            { "society_plan",         ("早会：{0} 给大家分了工——{1}", "朝の集まり：{0}が仕事を割り振った――{1}") },
            { "society_refused",      ("{0} 不服 {1} 的安排，今天自己干自己的。", "{0}は{1}の割り振りに従わず、今日は好きにするつもりだ。") },
            { "society_leader_line",  ("……好吧，我试试。", "……わかった、やってみる。") },
            { "rel_reason_elected",   ("第{0}天：大家推举{1}当了领导", "{0}日目：{1}がリーダーに選ばれた") },
            { "rel_reason_disobeyed", ("第{0}天：{1}不听我的安排", "{0}日目：{1}が私の割り振りに従わなかった") },
            { "role_leader",          ("领导", "リーダー") },
            { "role_build",           ("建造组", "建設班") },
            { "role_gather",          ("采集组", "採集班") },
            { "role_scavenge",        ("搜寻组", "探索班") },
            { "role_care",            ("医护组", "看護班") },
            { "role_rest",            ("休整", "休養") },
            { "role_log_gather",      ("{0} 按分工去采集{1}。", "{0}は割り振りどおり{1}を採りに行く。") },
            { "role_log_scavenge",    ("{0} 按分工出营地找吃的。", "{0}は割り振りどおり食べ物を探しに出た。") },
            { "role_log_build",       ("{0} 按分工去施工：{1}", "{0}は割り振りどおり建設に向かう：{1}") },
            { "role_log_care",        ("{0} 按分工去照顾 {1}。", "{0}は割り振りどおり{1}の世話をしに行く。") },
            { "mental_lucid",         ("清醒", "正常") },
            { "mental_dazed",         ("恍惚", "放心") },
            { "mental_halluc",        ("幻觉", "幻覚") },
            { "mental_broken",        ("崩解", "崩壊") },

            // —— 主界面布局（HudLayout）——
            { "hud_drawer_tab",       ("记\n录", "記\n録") },

            // —— 指令系统（NpcCommands / NpcTaskRunner，AI 词库）——
            { "order_go",             ("去{0}", "{0}へ向かう") },
            { "order_return",         ("回营地", "拠点に戻る") },
            { "order_use_self",       ("去仓库拿{0}，自己用掉", "倉庫で{0}を取って、自分に使う") },
            { "order_use_on",         ("拿{0}给{1}用", "{0}を{1}に使う") },
            { "order_fetch",          ("去仓库拿一个{0}带在身上", "倉庫で{0}を一つ取って持っておく") },
            { "order_search_item",    ("出去找{0}", "{0}を探しに出る") },
            { "order_gather",         ("出去搜寻物资", "物資を探しに出る") },
            { "order_rest",           ("回营地休息", "拠点で休む") },
            { "order_eat",            ("去仓库吃点东西", "倉庫で何か食べる") },
            { "order_push",           ("把{0}推到{1}", "{0}を{1}まで押していく") },
            { "dest_npc",             ("{0}那里", "{0}のところ") },
            { "fail_place_unknown",   ("不知道那是哪里", "それがどこか分からない") },
            { "fail_terminal_none",   ("终端机还没建成", "端末はまだ建っていない") },
            { "fail_target_dead",     ("{0}已经死了", "{0}はもう死んでいる") },
            { "fail_no_food",         ("仓库里没有吃的了", "倉庫に食べ物が残っていない") },
            { "fail_box_unknown",     ("找不到要推的东西", "押す物が見つからない") },
            { "sit_order_ok",         ("AI（ABA 的生存支援AI）刚给你下了指令，你答应了，正要去做：{0}\n用你的口吻、按你此刻对AI的态度，简短地回应一句（可以带点情绪）。",
                                       "AI（ABAの生存支援AI）から指示を受け、あなたは引き受けて、これから実行する：{0}\nあなたの口調で、今のAIへの態度に合わせて短く返事をすること（感情がにじんでいてよい）。") },
            { "sit_order_fail",       ("AI 让你：{0}\n但你做不到，原因：{1}\n用你的口吻简短地说明。",
                                       "AIに指示された：{0}\nしかしあなたにはできない。理由：{1}\nあなたの口調で短く伝えること。") },
            { "cmd_log_use_on",       ("{0} 给 {1} 用了{2}。", "{0}は{1}に{2}を使った。") },
            { "cmd_log_use_fail",     ("{0} 没能用上{1}。", "{0}は{1}を使えなかった。") },
            { "cmd_log_take",         ("{0} 从仓库拿走了一个{1}。", "{0}は倉庫から{1}を一つ持ち出した。") },
            { "cmd_log_take_fail",    ("{0} 去了仓库，但{1}已经没有了。", "{0}は倉庫に行ったが、{1}はもう無かった。") },
            { "cmd_log_found",        ("{0} 外出搜寻，找到了{1}。", "{0}は探索に出て、{1}を見つけた。") },
            { "cmd_log_not_found",    ("{0} 没找到{1}，只带回了一些吃的。", "{0}は{1}を見つけられず、食料を少し持ち帰った。") },

            // 以后要加文本，继续往这里加：{ "key", ("中文", "日文") },
        };
    }

    // 取当前语言的文本。找不到 key 就原样返回 key（方便发现漏翻译）。
    public string Get(string key)
    {
        if (_table != null && _table.TryGetValue(key, out var pair))
            return current == Language.Chinese ? pair.zh : pair.ja;
        return key; // 没找到，返回 key 本身提醒你补
    }

    // 取当前语言的文本，并用 string.Format 填入参数（模板里写 {0}{1}... 的场景，比如日志里插入NPC名字）。
    public string GetFormat(string key, params object[] args)
    {
        return string.Format(Get(key), args);
    }

    // 切换语言（在中/日之间切）。
    public void Toggle()
    {
        current = (current == Language.Chinese) ? Language.Japanese : Language.Chinese;
        OnLanguageChanged?.Invoke(); // 通知所有监听者刷新
    }

    // 给 AI 提示词用：返回强力的语言指令，确保 AI 按当前界面语言输出。
    // 放在提示词【最末尾】，用强硬明确的措辞，避免 AI 漂到别的语言
    //（尤其换云端模型时，弱指令容易被提示词主体的语言带跑）。
    public string GetAILanguageInstruction()
    {
        if (current == Language.Japanese)
        {
            return
                "\n\n==============================\n" +
                "【最重要・言語指定（厳守）】\n" +
                "上記の資料に中国語の言葉（地名など）が含まれていても、それはあくまで「あなたへの資料」である。\n" +
                "あなたの出力（JSON内の dialogue などすべての文字列）は、" +
                "必ず「自然な日本語」だけで書くこと。\n" +
                "・中国語の単語や漢字表現をそのまま混ぜてはならない。\n" +
                "・中国語の設定は、意味を汲み取った上で、必ず日本語に「翻訳」して表現すること。\n" +
                "・一文の中に中国語と日本語が混在することは、絶対に許されない。\n" +
                "出力はすべて日本語。これを最優先で厳守すること。\n" +
                "（日本語の文体の例：「……分かった。すぐに向かう。」）\n" +
                "==============================";
        }
        else
        {
            return
                "\n\n==============================\n" +
                "【最重要·语言指定（严格遵守）】\n" +
                "你的输出（JSON里的 dialogue 等所有字符串），必须只用「自然的中文」书写。\n" +
                "・绝对不要混入日语单词或假名。\n" +
                "・一句话里中文和日语混用，是绝对不允许的。\n" +
                "输出全部用中文。请最优先严格遵守。\n" +
                "==============================";
        }
    }
}
