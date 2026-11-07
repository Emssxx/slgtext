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

            // —— AI 终端机：命令台（TerminalPanelUI / TerminalConsole）——
            // 开机脚本：一行一个，按编号顺序播（编号断掉就停）。可以随便改 / 加行。
            //   {dots} 点点点铺到状态栏那一列　{count:4096} 数字从 0 数上去　{bar} 进度条　{pw:8} 打 8 个 *
            //   {wait:0.5} 停 0.5 秒　{hex:14} 单独一行：刷 14 行内存数据（屏幕往上滚）
            //   {users} {alive} {turbines} {slots} {tier} {day}：换成当前的数字
            //   [OK] [ON] [LOCKED] 绿　[WARN] 黄　[FAIL] [NO CARRIER] [OFF] 红
            { "term_boot_01", ("电源 {dots}[ON]{wait:0.25}", "電源 {dots}[ON]{wait:0.25}") },
            { "term_boot_02", ("", "") },
            { "term_boot_03", ("ABA-BIOS Ver 4.03    (C) ABA 生存支援局 技术部", "ABA-BIOS Ver 4.03    (C) ABA 生存支援局 技術部") },
            { "term_boot_04", ("CPU : ABA-SX486 / 33MHz {dots}[OK]", "CPU : ABA-SX486 / 33MHz {dots}[OK]") },
            { "term_boot_05", ("内存自检 : {count:4096}K {dots}[OK]", "メモリチェック : {count:4096}K {dots}[OK]") },
            { "term_boot_06", ("存储设备 : ABA-HDD0 (512MB) {dots}[OK]", "記憶装置 : ABA-HDD0 (512MB) {dots}[OK]") },
            { "term_boot_07", ("亚空间锚点 {dots}[LOCKED]", "亜空間アンカー {dots}[LOCKED]") },
            { "term_boot_08", ("外部网络（ABA 本部） {dots}[NO CARRIER]", "外部ネットワーク（ABA 本部） {dots}[NO CARRIER]") },
            { "term_boot_09", ("", "") },
            { "term_boot_10", ("载入 ABA-DOS 6.22 ...{wait:0.3}", "ABA-DOS 6.22 を読み込み中 ...{wait:0.3}") },
            { "term_boot_11", ("{hex:14}", "{hex:14}") },
            { "term_boot_12", ("  HIMEM.SYS     {dots}[OK]", "  HIMEM.SYS     {dots}[OK]") },
            { "term_boot_13", ("  ANCHOR.SYS    {dots}[OK]", "  ANCHOR.SYS    {dots}[OK]") },
            { "term_boot_14", ("  POWER.DRV     发电机 ×{turbines} {dots}[OK]", "  POWER.DRV     発電機 ×{turbines} {dots}[OK]") },
            { "term_boot_15", ("  WORKSHOP.DRV  算力槽 ×{slots} {dots}[OK]", "  WORKSHOP.DRV  演算スロット ×{slots} {dots}[OK]") },
            { "term_boot_16", ("  SENSOR.DRV    {dots}[OK]", "  SENSOR.DRV    {dots}[OK]") },
            { "term_boot_17", ("  UPLINK.DRV    {dots}[FAIL]", "  UPLINK.DRV    {dots}[FAIL]") },
            { "term_boot_18", ("自检完成：错误 0，警告 1（与本部失去联络，以孤立模式运行）", "自己診断完了：エラー 0、警告 1（本部との通信途絶。孤立モードで起動）") },
            { "term_boot_19", ("", "") },
            { "term_boot_20", ("启动生存支援 AI 核心 {bar}", "生存支援 AI コアを起動中 {bar}") },
            { "term_boot_21", ("终端等级 {tier}　登录成员 {users} 名（在线 {alive}）", "端末レベル {tier}　ログインメンバー {users} 名（オンライン {alive}）") },
            { "term_boot_22", ("", "") },
            { "term_boot_23", ("login: AI", "login: AI") },
            { "term_boot_24", ("password: {pw:8}", "password: {pw:8}") },
            { "term_boot_25", ("ACCESS GRANTED{wait:0.3}", "ACCESS GRANTED{wait:0.3}") },
            { "term_boot_skip",       ("按任意键 / 点击屏幕跳过", "任意のキー / クリックでスキップ") },
            { "term_welcome",         ("欢迎回来，支援 AI。", "おかえりなさい、支援 AI。") },
            { "term_logout_msg",      ("连接已断开。", "接続を切断しました。") },
            { "term_power",           ("电源", "電源") },
            { "term_offline_msg",     ("终端机没有上线（施工中或未激活）。上线后会自动重新启动。", "端末がオフライン（建設中または未起動）。オンラインになると自動で再起動する。") },
            { "term_home_title",      ("生存支援终端 · 主菜单", "生存支援端末 · メインメニュー") },
            { "term_lbl_level",       ("终端等级", "端末レベル") },
            { "term_lbl_produce",     ("发电", "発電") },
            { "term_lbl_store",       ("储电", "蓄電") },
            { "term_lbl_use",         ("用电", "消費") },
            { "term_lbl_weather",     ("天气", "天気") },
            { "term_lbl_water",       ("净化水", "浄化水") },
            { "term_lbl_cost",        ("材料", "素材") },
            { "term_lbl_time",        ("耗时", "所要時間") },
            { "term_lbl_require",     ("条件", "条件") },
            { "term_lbl_state",       ("状态", "状態") },
            { "term_lbl_slots",       ("算力槽", "演算スロット") },
            { "term_lbl_gear",        ("档位", "段階") },
            { "term_seconds",         ("{0} 秒", "{0} 秒") },
            { "term_per_turn",        (" /回合", " /ターン") },
            { "term_shed",            ("断电 {0} 台", "停止 {0} 台") },
            { "term_no_power",        ("还没有发电设备", "発電設備がまだない") },
            { "term_water_line",      ("{0} 份（{1}）", "{0} 杯（{1}）") },
            { "term_menu_tech",       ("科技", "技術") },
            { "term_menu_users",      ("登录成员", "ログインメンバー") },
            { "term_menu_craft",      ("合成", "合成") },
            { "term_menu_upgrade",    ("升级", "アップグレード") },
            { "term_menu_power",      ("能源管理", "エネルギー管理") },
            { "term_menu_jobs",       ("任务队列", "タスクキュー") },
            { "term_menu_logout",     ("注销", "ログアウト") },
            { "term_sum_tech",        ("已研究 {0}/{1}", "研究済み {0}/{1}") },
            { "term_sum_users",       ("在线 {0} · 离线 {1}", "オンライン {0} · オフライン {1}") },
            { "term_sum_craft",       ("可合成 {0}/{1}", "合成可能 {0}/{1}") },
            { "term_sum_next",        ("下一级 {0}", "次は {0}") },
            { "term_sum_power",       ("用电设备 {0} 台", "電力設備 {0} 台") },
            { "term_sum_jobs",        ("处理中 {0} · 排队 {1}", "処理中 {0} · 待機 {1}") },
            { "term_back",            ("返回", "戻る") },
            { "term_prev",            ("上一页", "前のページ") },
            { "term_next",            ("下一页", "次のページ") },
            { "term_page",            ("第 {0}/{1} 页", "{0}/{1} ページ") },
            { "term_empty",           ("（空）", "（なし）") },
            { "term_not_found",       ("找不到这个条目", "項目が見つからない") },
            { "term_done",            ("完成", "完了") },
            { "term_queued",          ("排队中", "待機中") },
            { "term_ready",           ("可研究", "研究可能") },
            { "term_can_craft",       ("可合成", "合成可能") },
            { "term_can_upgrade",     ("可升级", "アップグレード可能") },
            { "term_current",         ("当前", "現在") },
            { "term_remaining",       ("剩余 {0} 秒", "残り {0} 秒") },
            { "term_jobs_paused",     ("终端机离线：任务暂停中", "端末オフライン：タスク一時停止中") },
            { "term_col_name",        ("姓名", "名前") },
            { "term_col_role",        ("分工", "役割") },
            { "term_col_state",       ("状态", "状態") },
            { "term_user_job",        ("职业", "職業") },
            { "term_user_role",       ("分工", "役割") },
            { "term_user_doing",      ("正在", "行動") },
            { "term_user_vitals",     ("体征", "バイタル") },
            { "term_user_vitals_fmt", ("生命 {0}　饥饿 {1}　水分 {2}　体力 {3}", "生命 {0}　満腹 {1}　水分 {2}　体力 {3}") },
            { "term_user_security",   ("安全感", "安心感") },
            { "term_user_uneasy",     ("（不安）", "（不安）") },
            { "term_user_trust",      ("信任度", "信頼度") },
            { "term_user_state",      ("状态", "状態") },
            { "term_user_traits",     ("特征", "特徴") },
            { "term_user_healthy",    ("健康", "健康") },
            { "term_user_dead",       ("无生命体征", "バイタル反応なし") },
            { "term_user_broken",     ("人格崩解", "人格崩壊") },
            { "term_user_idle",       ("待命", "待機") },
            { "term_user_call",       ("建立通讯（打开对话）", "通信を開く（会話）") },
            { "term_user_locate",     ("定位（镜头移过去）", "位置を表示（カメラ移動）") },
            { "term_user_offline",    ("对方已离线", "相手はオフライン") },
            { "term_user_none",       ("没有登录成员", "メンバーがいない") },
            { "term_dev_level",       ("档位 {0} / {1}", "段階 {0} / {1}") },
            { "term_dev_short",       ("档位 {0}", "段階 {0}") },
            { "term_dev_up",          ("档位 +1", "段階 +1") },
            { "term_dev_down",        ("档位 -1", "段階 -1") },
            { "term_dev_off",         ("关闭", "オフにする") },
            { "term_dev_max",         ("已经是最高档", "すでに最大") },
            { "term_dev_min",         ("已经关着", "すでにオフ") },
            { "term_dev_set",         ("{0} → 档位 {1}", "{0} → 段階 {1}") },

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
            { "intent_log_water",     ("{0} 打算去湖边打水，倒进雨水收集器。", "{0}は湖で水を汲んで、雨水タンクへ運ぶつもりだ。") },
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
            { "society_meeting_start",("营地的人不约而同地聚到了一起。", "拠点のみんなが自然と集まってきた。") },
            { "society_emerged",      ("这几天大家渐渐开始听 {0} 的——{1}", "ここ数日、みんなは少しずつ{0}の言うことを聞くようになった――{1}") },
            { "society_plan",         ("早会：{0} 给大家分了工——{1}", "朝の集まり：{0}が仕事を割り振った――{1}") },
            { "society_replan",       ("{0} 调整了分工——{1}", "{0}が割り振りを変えた――{1}") },
            { "society_project",      ("{0} 决定营地先建{1}。", "{0}は、まず{1}を建てると決めた。") },
            { "society_refused",      ("{0} 不服 {1} 的安排，今天自己干自己的。", "{0}は{1}の割り振りに従わず、今日は好きにするつもりだ。") },
            { "society_leader_line",  ("……好吧，我试试。", "……わかった、やってみる。") },
            { "rel_reason_elected",   ("第{0}天：大家推举{1}当了领导", "{0}日目：{1}がリーダーに選ばれた") },
            { "rel_reason_disobeyed", ("第{0}天：{1}不听我的安排", "{0}日目：{1}が私の割り振りに従わなかった") },
            { "role_leader",          ("领导", "リーダー") },
            { "role_build",           ("建造组", "建設班") },
            { "role_haul",            ("搬运组", "運搬班") },
            { "role_gather",          ("采集组", "採集班") },
            { "role_scavenge",        ("搜寻组", "探索班") },
            { "role_care",            ("医护组", "看護班") },
            { "role_rest",            ("休整", "休養") },
            { "role_log_gather",      ("{0} 按分工去采集{1}。", "{0}は割り振りどおり{1}を採りに行く。") },
            { "role_log_scavenge",    ("{0} 按分工出营地找吃的。", "{0}は割り振りどおり食べ物を探しに出た。") },
            { "role_log_build",       ("{0} 按分工去施工：{1}", "{0}は割り振りどおり建設に向かう：{1}") },
            { "role_log_care",        ("{0} 按分工去照顾 {1}。", "{0}は割り振りどおり{1}の世話をしに行く。") },
            { "role_log_wall",        ("{0} 按分工开始修一段围墙：{1}", "{0}は割り振りどおり壁を一区画建て始めた：{1}") },
            { "role_log_wall_gather", ("{0} 去采{1}，准备修围墙。", "{0}は壁の材料に{1}を採りに行く。") },
            // —— 分工里的各种活（NpcIntentBrain.DoRoleWork）——
            { "role_log_job",         ("{0}［{1}］{2}", "{0}［{1}］{2}") },
            { "role_log_wall_ready",  ("{0} 把材料运到了：{1}，可以开工了。", "{0}が材料を運び終えた：{1}、着工できる。") },
            { "job_desc_build",       ("去施工：{0}", "建設に向かう：{0}") },
            { "job_desc_haul_wall",   ("把{0}运到{1}", "{0}を{1}へ運ぶ") },
            { "job_desc_haul_water",  ("去湖边打水，倒进雨水收集器", "湖で水を汲み、雨水タンクへ運ぶ") },
            { "job_desc_haul_project",("把{0}运过去，准备建{1}", "{0}を運んで{1}を建てる準備") },
            { "job_desc_gather",      ("去采集{0}", "{0}を採りに行く") },
            { "job_desc_care",        ("去照顾 {0}", "{0}の世話をしに行く") },
            { "job_desc_scavenge",    ("出营地找吃的", "拠点の外へ食べ物を探しに行く") },
            { "job_desc_rest",        ("歇一会儿", "少し休む") },
            { "society_plan_line",    ("就照这个分工来，大家辛苦了。", "この割り振りで頼む。みんな、よろしく。") },
            // —— NPC 任务栏（NpcTaskPanel）——
            { "taskpanel_free",       ("自由行动", "自由行動") },
            { "taskpanel_idle",       ("空闲", "待機中") },
            { "taskpanel_leader",     ("领导", "リーダー") },
            // —— 任务浮窗：营地目标（CampGoals / NpcTaskPanel）——
            { "taskpanel_goal_no",    ("目标{0}", "目標{0}") },
            { "taskpanel_owner",      ("负责人：{0}", "担当：{0}") },
            { "taskpanel_owner_none", ("负责人：还没有（大家各干各的）", "担当：まだいない（みんな好きに動いている）") },
            { "taskpanel_owner_ai",   ("AI（你）", "AI（あなた）") },
            { "taskpanel_empty",      ("眼下没有目标。", "今のところ目標はない。") },
            { "goal_enclose",         ("将营地用围墙围起来", "拠点を壁で囲む") },
            { "goal_build",           ("建一座{0}", "{0}を建てる") },
            { "goal_water",           ("让净化水够喝", "飲み水を確保する") },
            { "goal_food",            ("备足食物", "食料を蓄える") },
            { "goal_medicine",        ("找到药品", "薬を見つける") },
            { "goal_health",          ("治好伤病的人", "けが人・病人を治す") },
            { "goal_rest",            ("让疲惫的人恢复体力", "疲れた人を休ませる") },
            { "goal_gather",          ("采集{0}", "{0}を集める") },
            { "goal_ai_power",        ("给{0}接上电源", "{0}を電源につなぐ") },
            { "goal_power",           ("恢复供电", "電力を回復する") },
            // —— 资源栏：状态小字 + 悬停信息浮窗（ResourceHud）——
            { "res_state_good",       ("充足", "十分") },
            { "res_mat_project_lack", ("建造项目还缺 {0}", "建設プロジェクトにあと{0}") },
            { "res_mat_wall_short",   ("不够修一段墙", "壁一区画分に足りない") },
            { "tip_state_good",       ("状态：充足", "状態：十分") },
            // —— 天气浮窗（WeatherPanel）——
            { "wx_rain_none",         ("没有下雨", "雨は降っていない") },
            { "wx_rain_light",        ("小雨", "小雨") },
            { "wx_rain_moderate",     ("中雨", "雨") },
            { "wx_rain_heavy",        ("大雨", "大雨") },
            { "wx_snow_none",         ("没有下雪", "雪は降っていない") },
            { "wx_snow_light",        ("小雪", "小雪") },
            { "wx_snow_moderate",     ("中雪", "雪") },
            { "wx_snow_heavy",        ("大雪", "大雪") },
            { "wx_wind_none",         ("没有风", "無風") },
            { "wx_wind_breeze",       ("微风", "そよ風") },
            { "wx_wind_moderate",     ("中风", "やや強い風") },
            { "wx_wind_strong",       ("大风", "強風") },
            { "wx_hail_none",         ("没有冰雹", "ひょうは降っていない") },
            { "wx_hail",              ("冰雹", "ひょう") },
            { "wx_temp_normal",       ("气温正常", "過ごしやすい") },
            { "wx_temp_cold",         ("寒冷", "寒い") },
            { "wx_temp_hot",          ("炎热", "暑い") },
            { "wx_temp_ext_cold",     ("严寒", "極寒") },
            { "wx_temp_ext_hot",      ("酷热", "酷暑") },
            // —— 建筑完成提示（HudToasts）——
            { "toast_built",          ("{0} 建成了", "{0}が完成した") },
            { "toast_crew",           ("施工：{0}", "担当：{0}") },
            { "toast_goal_done",      ("目标完成：{0}", "目標達成：{0}") },
            // —— 左下角坐标浮窗（HoverInfo）——
            { "grid_idle",            ("把鼠标移到地图上查看这一格", "マウスをマップに合わせるとマスの情報を表示") },
            { "tip_power_title",      ("电力", "電力") },
            { "tip_water_title",      ("净化水", "浄化水") },
            { "tip_power_none",       ("还没有发电设备。", "発電設備がまだない。") },
            { "tip_power_stored",     ("储电：{0} / {1}", "蓄電：{0} / {1}") },
            { "tip_power_flow",       ("本回合：发电 +{0}　用电 −{1}", "このターン：発電 +{0}　消費 −{1}") },
            { "tip_power_devices",    ("风力发电机 {0} 台　蓄电池 {1} 座", "風力発電機 {0} 基　蓄電池 {1} 基") },
            { "tip_power_shed",       ("电不够：{0} 台设备被断电", "電力不足：{0} 台が停止中") },
            { "tip_power_unlinked",   ("{0} 台设备没接上电源", "{0} 台が電源につながっていない") },
            { "tip_power_hint",       ("风越大发电越多；接电线、调档位在 AI 终端机里做。", "風が強いほど発電量が増える。配線と出力の調整はAI端末で行う。") },
            { "tip_water_stored",     ("净化水：{0} 份", "浄化水：{0}") },
            { "tip_water_demand",     ("全营地每天约喝 {0} 份，{1}", "拠点全体で1日約{0}、{1}") },
            { "tip_water_no_collector",("还没有雨水收集器：下的雨接不住", "雨水タンクがない：雨をためられない") },
            { "tip_water_collect",    ("雨水收集器 {0} 台：存雨水 {1} / {2}", "雨水タンク {0} 基：雨水 {1} / {2}") },
            { "tip_water_no_purifier",("还没有水源净化器：雨水喝不了", "浄水器がない：雨水は飲めない") },
            { "tip_water_purify",     ("水源净化器 {0} 台：上回合净化 {1} 份", "浄水器 {0} 台：前のターンに {1} 浄化") },
            { "tip_water_unlinked",   ("净化器没接上电源，不能工作", "浄水器が電源につながっておらず、動いていない") },
            { "tip_water_thirsty",    ("{0} 人正在口渴", "{0}人が喉の渇きに苦しんでいる") },
            { "tip_water_rain",       ("正在下雨：收集器在接雨水", "雨が降っている：タンクが雨水をためている") },
            { "tip_water_hint",       ("雨水 / 湖水 → 收集器 → 净化器（要电）→ 能喝的净化水", "雨水・湖の水 → 雨水タンク → 浄水器（電気が必要）→ 飲める浄化水") },
            { "tip_mat_have",         ("仓库：{0}", "倉庫：{0}") },
            { "tip_mat_from_wood",    ("来源：砍树", "入手：木を伐る") },
            { "tip_mat_from_stone",   ("来源：采石头堆", "入手：岩場で採る") },
            { "tip_mat_from_ore",     ("来源：采矿脉", "入手：鉱脈で掘る") },
            { "tip_mat_used",         ("用来建：{0}", "使い道：{0}") },
            { "tip_mat_unused",       ("现在能建的东西里用不到它", "今建てられる物には使わない") },
            { "tip_mat_project",      ("建造项目「{0}」还缺 {1}", "建設プロジェクト「{0}」にあと{1}必要") },
            { "tip_mat_wall",         ("营地还空着 {0} 段墙位，每段要 {1} 块", "拠点の壁はあと{0}区画、1区画に{1}個") },
            { "tip_mat_gatherers",    ("正在采：{0}", "採集中：{0}") },
            { "tip_mat_nobody",       ("现在没人专门在采", "今は専門に採っている人がいない") },
            { "mental_lucid",         ("清醒", "正常") },
            { "mental_dazed",         ("恍惚", "放心") },
            { "mental_halluc",        ("幻觉", "幻覚") },
            { "mental_broken",        ("崩解", "崩壊") },

            // —— 主界面布局（HudLayout）——
            { "hud_drawer_tab",       ("记\n录", "記\n録") },

            // —— 指令系统（NpcCommands / NpcTaskRunner，AI 词库）——
            { "order_go",             ("去{0}", "{0}へ向かう") },
            { "order_build",          ("建{0}（按AI画的蓝图，由你指挥）", "{0}を建てる（AIの設計図どおり、君が指揮する）") },
            { "order_build_help",     ("去接手{0}的工地，带人把它建完", "{0}の工事現場を引き継いで、完成させる") },
            { "sit_build_note",       ("要的材料：{0}。仓库不够就先去采，再搬到工地施工。", "必要な材料：{0}。倉庫に足りなければ先に集めて、現場に運んで建てる。") },
            { "fail_build_spot",      ("营地里找不到能放下它的地方", "拠点にそれを置ける場所が見つからない") },
            { "fail_build_walls_done",("营地的墙位都已经有墙或工地了", "拠点の壁の位置はもう全部埋まっている") },
            { "bp_note_order",        ("AI 在对话里交代 {0} 去建。", "AIが会話の中で{0}に建設を指示した。") },
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

            // —— 建造蓝图（Blueprint / BlueprintDesigner / BlueprintInbox）——
            { "bp_status_draft",      ("草稿", "下書き") },
            { "bp_status_saved",      ("已保存", "保存済み") },
            { "bp_status_pending",    ("等待批准", "承認待ち") },
            { "bp_status_approved",   ("已批准", "承認済み") },
            { "bp_status_rejected",   ("已否决", "却下") },
            { "bp_status_modified",   ("已转为修改", "修正に回した") },
            { "bp_status_building",   ("建造中", "建設中") },
            { "bp_status_done",       ("已建成", "完成") },
            { "bp_why_bounds",        ("超出地图", "マップの外") },
            { "bp_why_fog",           ("还没探明", "未探索") },
            { "bp_why_occupied",      ("已被占用", "使用中") },
            { "bp_why_wall",          ("岩壁", "岩壁") },
            { "bp_why_water",         ("水面", "水面") },
            { "bp_why_swamp",         ("沼泽", "沼地") },
            { "bp_why_resource",      ("有树石挡着", "木や岩がある") },
            { "bp_why_overlap",       ("和蓝图里的重叠", "設計図内で重なる") },
            { "bp_why_unique",        ("只能建一座", "一つしか建てられない") },
            { "bp_have",              ("有 {0}", "在庫{0}") },
            { "bp_empty",             ("（空）", "（空）") },
            { "bp_items",             ("{0} 项", "{0}件") },
            { "bp_untitled",          ("（未命名）", "（無題）") },
            { "bp_name_player",       ("蓝图 #{0}", "設計図 #{0}") },
            { "bp_sign_modified",     ("{0}（AI 修改）", "{0}（AI修正）") },
            { "bp_name_build",        ("{0}建造计划", "{0}建設計画") },
            { "bp_note_build",        ("营地需要{0}。", "拠点に{0}が必要だ。") },
            { "bp_name_walls",        ("营地围墙计划", "拠点の囲い計画") },
            { "bp_note_walls",        ("营地的{0}还没围上，补 {1} 段墙。", "拠点の{0}がまだ開いている。壁を{1}区画足す。") },
            { "bp_note_project",      ("大家商量好要建{0}。", "みんなで{0}を建てると決めた。") },
            // NPC 说的话（显示在对话框和头顶气泡）
            { "bp_say_build",         ("我画了一份{0}的建造计划，你看看行不行？", "{0}の建設計画を描いてみた。見てくれないか？") },
            { "bp_say_walls",         ("营地的{0}还空着，我把围墙计划画好了，你批一下？", "拠点の{0}がまだ空いている。囲いの計画を描いたから、許可をくれないか？") },
            { "bp_say_project",       ("按我们商量的，{0}的计划画好了，请你过目。", "話し合った通り、{0}の計画を描いた。目を通してくれ。") },
            { "bp_npc_ack_approve",   ("收到，我这就安排大家动工。", "了解。すぐにみんなで取りかかる。") },
            { "bp_npc_ack_send",      ("蓝图收到了，交给我吧。", "設計図、受け取った。任せてくれ。") },
            { "bp_npc_nothing",       ("……蓝图上的地方现在都放不下，没法开工。", "……設計図の場所は今どこも使えない。始められないよ。") },
            { "bp_npc_ask_reason",    ("……为什么不行？能告诉我理由吗？", "……どうしてダメなんだ？理由を聞かせてくれないか。") },
            { "bp_npc_done",          ("「{0}」建好了，一共 {1} 处。", "「{0}」が完成した。全部で{1}か所だ。") },
            // 对话框里的系统行（>> ）
            { "bp_sys_approved",      ("AI 批准了「{0}」：放下 {1} 处规划区。", "AIが「{0}」を承認：計画区画を{1}か所配置。") },
            { "bp_sys_sent",          ("AI 发来蓝图「{0}」：放下 {1} 处规划区。", "AIから設計図「{0}」：計画区画を{1}か所配置。") },
            { "bp_sys_skipped",       ("（{0} 处放不下：{1}）", "（{0}か所は配置できず：{1}）") },
            { "bp_sys_rejected",      ("AI 否决了「{0}」。", "AIが「{0}」を却下した。") },
            { "bp_sys_modifying",     ("AI 正在修改「{0}」。", "AIが「{0}」を修正中。") },
            // NPC 的记忆（会进提示词）
            { "bp_mem_proposed",      ("画了一份「{0}」交给 AI，等它批准。", "「{0}」を描いてAIに提出した。承認待ち。") },
            { "bp_mem_approved",      ("AI 批准了「{0}」，由我指挥建造。", "AIが「{0}」を承認した。私が建設を指揮する。") },
            { "bp_mem_sent",          ("AI 发来蓝图「{0}」，让我指挥建造。", "AIから設計図「{0}」が届き、建設の指揮を任された。") },
            { "bp_mem_rejected",      ("交上去的「{0}」被 AI 否决了。", "提出した「{0}」がAIに却下された。") },
            { "bp_mem_modified",      ("交上去的「{0}」被 AI 拿去修改了。", "提出した「{0}」はAIが修正することになった。") },
            { "bp_mem_done",          ("「{0}」全部建好了。", "「{0}」がすべて完成した。") },
            // 日志 / 提示
            { "bp_log_proposed",      ("{0} 交来一份建造计划「{1}」（{2} 项），等你批准。", "{0}が建設計画「{1}」（{2}件）を提出。承認待ち。") },
            { "bp_log_sent",          ("蓝图「{0}」交给 {1} 指挥建造（{2} 处规划区）。", "設計図「{0}」を{1}に任せた（計画区画{2}か所）。") },
            { "bp_log_wait",          ("{0} 想建{1}，计划已经交给你，等你批准。", "{0}は{1}を建てたい。計画を提出済み、承認待ち。") },
            { "bp_log_job",           ("{0} 按蓝图干活：{1}", "{0}は設計図に沿って作業：{1}") },
            { "bp_log_delivered",     ("{0} 把材料送到了{1}的工地。", "{0}が{1}の現場に素材を届けた。") },
            { "job_desc_haul_site",   ("把{0}送到{1}的工地", "{0}を{1}の現場へ運ぶ") },
            { "intent_filler",        ("边想边采{0}", "考えながら{0}を採取") },
            { "toast_plan_done",      ("「{0}」全部建成", "「{0}」がすべて完成") },
            { "toast_plan_foreman",   ("指挥：{0}", "指揮：{0}") },
            { "bp_inbox_line",        ("有一份建造计划想给你看", "見てほしい建設計画がある") },
            { "chatwin_plan_open",    ("点击打开计划文件", "クリックで計画ファイルを開く") },
            { "chatwin_plan_review",  ("点击查看并批复", "クリックして確認・返答") },
            { "chatwin_plan_missing", ("文件丢失", "ファイルなし") },
            // 蓝图规划（BPLAN.EXE）
            { "bp_btn_build",         ("建筑", "建築") },
            { "bp_tool_undo",         ("撤回", "元に戻す") },
            { "bp_tool_delete",       ("删除", "削除") },
            { "bp_tool_save",         ("保存", "保存") },
            { "bp_tool_test",         ("测试", "テスト") },
            { "bp_tool_approve",      ("批准", "承認") },
            { "bp_tool_reject",       ("不允许", "却下") },
            { "bp_tool_modify",       ("修改", "修正") },
            { "bp_tool_copy",         ("复制修改", "コピーして修正") },
            { "bp_boot_title",        ("蓝图规划系统", "設計図プランナー") },
            { "bp_boot_load",         ("读取地形", "地形読込") },
            { "bp_boot_stats",        ("地形读取完毕：资源点 {0} · 建筑 {1} · 工地 {2}", "地形読込完了：資源 {0}・建物 {1}・現場 {2}") },
            { "bp_con_open",          ("打开文件 {0}", "ファイルを開く {0}") },
            { "bp_help_edit",         ("[≡] 选建筑 · 左键放置 · 拖动连放（按住 Shift 放一圈）· 右键删除 / 取消选择", "[≡] で建築を選ぶ・左クリックで配置・ドラッグで連続配置（Shift で一周）・右クリックで削除／選択解除") },
            { "bp_help_edit2",        ("中键或右键拖动平移 · 滚轮缩放 · Ctrl+Z 撤回 · Del 删除模式 · Esc 退出", "中／右ドラッグで移動・ホイールで拡大縮小・Ctrl+Z で元に戻す・Del で削除モード・Esc で終了") },
            { "bp_help_review",       ("[1] 批准  [2] 不允许  [3] 修改 —— 也可以先按「测试」看看路通不通。", "[1] 承認  [2] 却下  [3] 修正 —— 先に「テスト」で通れるか確かめてもいい。") },
            { "bp_help_view",         ("只读。要改的话点「复制修改」。", "閲覧のみ。変えるなら「コピーして修正」。") },
            { "bp_con_draft",         ("继续上次的草稿（{0} 项）。", "前回の下書きの続き（{0}件）。") },
            { "bp_con_review_head",   ("{0} 交来一份建造计划（第 {1} 天）：", "{0}から建設計画が届いた（{1}日目）：") },
            { "bp_lbl_cost",          ("材料：", "素材：") },
            { "bp_lbl_reason",        ("理由：", "理由：") },
            { "bp_con_progress",      ("建造进度：{0}/{1}", "建設進捗：{0}/{1}") },
            { "bp_con_removed",       ("删除了 {0} 项", "{0}件を削除") },
            { "bp_con_placed",        ("放下了 {0} 项", "{0}件を配置") },
            { "bp_con_skip",          ("放不下：", "配置できず：") },
            { "bp_con_wire_none",     ("这里没有能接电线的设备。", "ここには電線をつなげる設備がない。") },
            { "bp_con_wire_from",     ("从 {0} 拉线：再点另一台设备（已连上的会断开）。", "{0}から配線：もう一台をクリック（接続済みなら切断）。") },
            { "bp_con_delete_on",     ("删除模式：点一下或拖出方框删除（再按「删除」退出）。", "削除モード：クリックかドラッグで削除（もう一度「削除」で終了）。") },
            { "bp_con_wire_on",       ("电线模式：先点一台设备，再点另一台。", "電線モード：設備を一台クリックして、次にもう一台。") },
            { "bp_con_selected",      ("选中 {0}（占地 {1}）", "{0}を選択（{1}）") },
            { "bp_con_undo_none",     ("没有可以撤回的操作。", "元に戻せる操作がない。") },
            { "bp_con_empty",         ("蓝图是空的，先放点东西。", "設計図が空っぽだ。まず何か置こう。") },
            { "bp_con_untested",      ("提示：还没测试寻路（「测试」）。", "注意：まだ経路テストをしていない（「テスト」）。") },
            { "bp_con_save_errors",   ("提示：测试发现 {0} 处走不通。", "注意：テストで通れない場所が{0}か所見つかった。") },
            { "bp_con_pick_first",    ("先在左上角 [≡] 菜单里选一个建筑。", "まず左上の [≡] メニューで建築を選んで。") },
            { "bp_con_deselect",      ("取消选择。", "選択解除。") },
            { "bp_ask_save",          ("要保存这份蓝图吗？", "この設計図を保存しますか？") },
            { "bp_opt_save",          ("保存", "保存する") },
            { "bp_opt_keep_editing",  ("继续修改", "編集を続ける") },
            { "bp_con_saved",         ("蓝图完成，已保存：{0}「{1}」", "設計図完成、保存しました：{0}「{1}」") },
            { "bp_ask_send",          ("您要将这份蓝图发给谁？（由他指挥建造）", "この設計図を誰に送りますか？（その人が建設を指揮する）") },
            { "bp_mark_author",       ("← 原作者", "← 作成者") },
            { "bp_opt_no_send",       ("先不发", "まだ送らない") },
            { "bp_con_not_sent",      ("蓝图已保存，没有发出。之后可以从 [≡] 菜单里打开再发。", "設計図は保存済み、未送信。あとで [≡] メニューから開いて送れる。") },
            { "bp_con_sent",          ("已发给 {0}：放下 {1}/{2} 处规划区，材料送到就开工。", "{0}に送信：計画区画 {1}/{2} を配置。素材が届けば着工。") },
            { "bp_opt_approve",       ("批准（{0} 指挥建造）", "承認（{0}が指揮）") },
            { "bp_opt_reject",        ("不允许（{0} 会问你理由）", "却下（{0}が理由を聞いてくる）") },
            { "bp_opt_modify",        ("修改（复制一份自己改）", "修正（コピーして自分で直す）") },
            { "bp_con_approved",      ("已批准：{0} 开始指挥建造（放下 {1}/{2} 处）。", "承認：{0}が建設を指揮（{1}/{2}か所を配置）。") },
            { "bp_con_rejected",      ("已否决。{0} 想知道理由——去对话框里告诉他吧。", "却下した。{0}は理由を知りたがっている——会話で伝えよう。") },
            { "bp_con_modify",        ("已复制一份，签名：{0}。改完保存，再发给谁都行。", "コピーを作成、署名：{0}。直して保存したら、誰に送ってもいい。") },
            { "bp_file_missing",      ("找不到这份蓝图文件。", "この設計図ファイルが見つからない。") },
            { "bp_con_readonly",      ("这份蓝图是只读的。", "この設計図は閲覧のみ。") },
            { "bp_top_review",        ("审阅 NPC 交来的计划", "提出された計画を確認") },
            { "bp_top_view",          ("查看（只读）", "閲覧（読み取り専用）") },
            { "bp_top_delete",        ("删除模式", "削除モード") },
            { "bp_top_wire",          ("电线模式", "電線モード") },
            { "bp_top_pick",          ("← 先在 [≡] 菜单里选一个建筑", "← まず [≡] メニューで建築を選ぶ") },
            { "bp_top_selected",      ("选中：", "選択中：") },
            { "bp_menu_saved",        ("已保存的蓝图", "保存した設計図") },
            { "bp_menu_none",         ("（还没有）", "（まだない）") },
            { "bp_menu_new",          ("新建空白蓝图", "新しい設計図") },
            { "bp_test_start",        ("寻路测试：从营地中心出发，检查 {0} 个目标……", "経路テスト：拠点の中心から {0} か所の目標を確認……") },
            { "bp_test_scan",         ("扫描可通行区域", "通行範囲スキャン") },
            { "bp_test_cells",        (" 格", "マス") },
            { "bp_test_paths",        ("描绘路线", "経路を描画") },
            { "bp_test_done",         ("测试完成：扫描 {0} 格，用时 {1} 秒。", "テスト完了：{0}マスをスキャン、{1}秒。") },
            { "bp_test_ok",           ("全部 {0} 个目标都走得到，没有问题。", "{0}か所すべてに到達できる。問題なし。") },
            { "bp_test_errors",       ("发现 {0} 处走不通：", "通れない場所が{0}か所：") },
            { "bp_test_item",         ("{0}（{1}）：走不到它旁边，没法施工", "{0}（{1}）：そばまで行けず、建てられない") },
            { "bp_test_npc",          ("{0}（{1}）：被困住了，回不了营地", "{0}（{1}）：閉じ込められて拠点に戻れない") },
            { "bp_test_building",     ("{0}（{1}）：从营地走不过去", "{0}（{1}）：拠点からたどり着けない") },
            { "bp_test_enclosed",     ("营地（{0}）：被围死了，出不去——留一个门", "拠点（{0}）：囲まれて外に出られない——出入口を残そう") },
            { "bp_test_water",        ("水边（{0}）：走不到水源", "水辺（{0}）：水源にたどり着けない") },
            { "bp_test_more",         ("……还有 {0} 处", "……ほか{0}か所") },
            { "bp_test_click",        ("点一下报错那一行，地图上会标出位置。", "エラーの行をクリックすると、地図上に位置を示す。") },
            { "bp_test_no_start",     ("营地中心附近没有能站的地方，没法测试。", "拠点の中心付近に立てる場所がなく、テストできない。") },

            // —— 开场（OpenDirector）：第一场黑屏对话 ——
            { "open_cap_place",       ("亚空间 · 废弃营地", "亜空間・廃棄された拠点") },
            { "open_cap_time",        ("被卷进来，已经有一段日子了。", "巻き込まれてから、もうしばらく経つ。") },
            { "open_amb_01",          ("（金属箱被撬开的声音）", "（金属の箱がこじ開けられる音）") },
            { "open_amb_02",          ("（有人在翻找东西）", "（誰かが中身をあさっている）") },
            { "open_amb_03",          ("（防护服的拉链声）", "（防護服のジッパーの音）") },
            { "open_amb_04",          ("（远处传来低沉的嗡鸣）", "（遠くで低いうなりが響く）") },
            { "open_amb_05",          ("（几个人的呼吸声）", "（何人かの息づかい）") },
            { "open_click",           ("（咔哒）", "（カチッ）") },
            { "open_skip_hint",       ("[Esc] 跳过　[空格 / 点击] 下一句", "[Esc] スキップ　[スペース / クリック] 次へ") },
            { "open_fb_01",           ("都在这儿了？净化水、营养块……还有这几套防护服。", "これで全部か？浄水、栄養ブロック……それにこの防護服。") },
            { "open_fb_02",           ("够我们撑上一阵子了。", "しばらくは持ちこたえられるな。") },
            { "open_fb_03",           ("撑一阵子？然后呢，继续在这鬼地方等死？", "しばらく？そのあとは？ここで死ぬのを待つのか？") },
            { "open_fb_04",           ("有了这些，说不定能找到出去的路。", "これだけあれば、出口だって見つかるかもしれない。") },
            { "open_fb_05",           ("别想太远，先把今天活过去。", "先のことはいい。まずは今日を生き延びよう。") },
            { "open_fb_06",           ("这衣服倒挺合身……就是不知道原来的主人去哪了。", "この服、妙に体に合うな……元の持ち主はどこへ行ったんだろう。") },
            { "open_fb_07",           ("……别说了。", "……やめてよ、その話。") },
            { "open_fb_08",           ("咦，胸口这个按钮是干什么的——", "あれ、胸のこのボタンは何だ——") },
            { "open_fb_switch",       ("……咦？我好像按到了什么。", "……あれ？何か押しちゃったみたい。") },
            // —— 开场：AI 启动日志 ——
            { "boot_title",           ("ABA 生存支援单元　固件 v3.2.7", "ABA 生存支援ユニット　ファームウェア v3.2.7") },
            { "boot_sub",             ("(C) ABA 特异现象调查局　· 防护服内置生存支援 AI ·", "(C) ABA 特異現象調査局　・防護服内蔵 生存支援AI・") },
            { "boot_power",           ("主电源接通：防护服电池 {0}%", "主電源オン：防護服バッテリー {0}%") },
            { "boot_mem",             ("内存自检", "メモリ自己診断") },
            { "boot_server",          ("推理服务：连接 {0}", "推論サーバー：{0} に接続") },
            { "boot_server_ok",       ("OK　Ollama {0}", "OK　Ollama {0}") },
            { "boot_server_ng",       ("NG　连不上（对话改用本地台词）", "NG　接続できない（会話はローカル台詞で代用）") },
            { "boot_server_cloud",    ("推理服务：云端 {0}", "推論サーバー：クラウド {0}") },
            { "boot_main",            ("主模型 {0}　{1}", "メインモデル {0}　{1}") },
            { "boot_main_loaded",     ("OK　已在显存　{0}", "OK　VRAM に常駐　{0}") },
            { "boot_main_loading",    ("载入显存中", "VRAM に読み込み中") },
            { "boot_main_done",       ("OK　载入完成 {0} 秒　{1}", "OK　読み込み完了 {0}秒　{1}") },
            { "boot_main_missing",    ("NG　没有安装（ollama pull {0}）", "NG　未インストール（ollama pull {0}）") },
            { "boot_main_bg",         ("后台继续载入", "バックグラウンドで読み込み継続") },
            { "boot_lite",            ("辅助模型 {0}（隐形参谋）", "補助モデル {0}（見えない参謀）") },
            { "boot_lite_ok",         ("OK　{0}", "OK　{0}") },
            { "boot_lite_off",        ("未启用（由代码兜底）", "無効（コードで代用）") },
            { "boot_lite_ng",         ("没有安装（由代码兜底）", "未インストール（コードで代用）") },
            { "boot_vision",          ("视觉系统：头盔摄像头 ×{0}　视野半径 {1} 格", "視覚システム：ヘルメットカメラ ×{0}　視界半径 {1}マス") },
            { "boot_hearing",         ("听觉系统：语音通道 ×{0}　识别语言 {1}", "聴覚システム：音声チャンネル ×{0}　認識言語 {1}") },
            { "boot_lang_name",       ("简体中文", "日本語") },
            { "boot_kb",              ("知识库：物品 {0} · 建筑 {1} · 科技 {2} · 配方 {3} · 世界设定 {4} 字", "知識ベース：アイテム {0}・建築 {1}・技術 {2}・レシピ {3}・世界設定 {4}字") },
            { "boot_kb_ok",           ("索引完成", "インデックス完了") },
            { "boot_persona",         ("人格建模：幸存者 ×{0}（性格 5 维 · 词条 {1}）", "人格モデリング：生存者 ×{0}（性格5軸・特性 {1}）") },
            { "boot_persona_done",    ("OK　全部由主模型写成", "OK　すべてメインモデルで生成") },
            { "boot_persona_partial", ("{0}/{1}　其余先用模板，后台继续写", "{0}/{1}　残りはテンプレートで、バックグラウンドで続行") },
            { "boot_persona_skip",    ("跳过（后台通道关闭，使用模板）", "スキップ（バックグラウンド無効、テンプレート使用）") },
            { "boot_terrain",         ("地形测绘：{0}×{1} 格 · 资源点 {2} · 水域 {3} 格", "地形測量：{0}×{1}マス・資源 {2}・水域 {3}マス") },
            { "boot_render_ok",       ("渲染完成", "描画完了") },
            { "boot_nav",             ("导航网格", "ナビメッシュ") },
            { "boot_camp",            ("营地判定：中心 X{0} Y{1} · 范围 {2}×{3}", "拠点判定：中心 X{0} Y{1}・範囲 {2}×{3}") },
            { "boot_camp_none",       ("营地判定", "拠点判定") },
            { "boot_hud",             ("战术界面 HUD", "戦術インターフェース HUD") },
            { "boot_hud_ok",          ("载入", "ロード") },
            { "boot_ready",           (">>> 系统就绪。接入防护服神经链路……", ">>> システム準備完了。防護服の神経リンクに接続……") },
            // —— 开场：第二场（讨论 → 投票）——
            { "open_s2_discuss",      ("幸存者们正在讨论", "生存者たちが話し合っている") },
            { "open_s2_team",         ("一个人活不下去——得团队合作，得有人带头。", "一人では生き残れない——協力しなければ。誰かが先頭に立たないと。") },
            { "open_s2_vote",         ("投票选出领导者", "リーダーを投票で選ぶ") },
            { "open_vote_for",        ("我投{0}。", "{0}に一票。") },
            { "open_vote_self",       ("我听大家的。", "みんなに任せる。") },
            { "open_s2_result",       ("{0} 当选领导者（{1}/{2} 票）", "{0}がリーダーに選ばれた（{2}票中{1}票）") },
            { "open_s2_reason",       ("大家觉得：{0}", "みんなの見立て：{0}") },
            { "open_s2_alone",        ("只剩 {0} 一个人。从现在起，一切由他自己决定。", "残ったのは{0}ひとり。これからは自分ですべてを決める。") },
            // —— 领导（NpcSociety）——
            { "society_voted",        ("大家投票选出 {0} 当领导（{1}/{2} 票）——{3}", "投票で{0}がリーダーに選ばれた（{2}票中{1}票）——{3}") },
            { "society_chosen",       ("大家推举 {0} 带头——{1}", "みんなは{0}に先頭を任せた——{1}") },
            { "society_reelected",    ("{0} 不在了，大家推举 {1} 接着带头——{2}", "{0}がいなくなり、みんなは{1}に後を任せた——{2}") },
            { "society_vote_line",    ("……好。既然大家信得过我，我们一起活下去。", "……わかった。信じてくれるなら、みんなで生き延びよう。") },
            { "leader_thinking",      ("正在思考……", "考え中……") },

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
