using System.Collections.Generic;
using UnityEngine;

// 物品图鉴（单例）：游戏里所有物品的定义都在这里。
// 场景里挂一个空物体即可。列表为空时会自动填入默认物品（开箱即用）；
// 想改数值/加图标，运行一次后在 Inspector 里直接改列表即可（或右键组件 → 填入默认物品）。
public class ItemDatabase : MonoBehaviour
{
    public static ItemDatabase Instance { get; private set; }

    [Tooltip("所有物品定义。为空时 Awake 会自动填入默认物品。")]
    public List<ItemData> items = new List<ItemData>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (items == null || items.Count == 0) FillDefaults();
        else AddMissingDefaults(); // Inspector 里改过列表的：后来新加的默认物品（木材、石材……）也补进来
    }

    // 默认物品（呼应《资源与材料设定》：亚空间没有传统资源，物资来自终端机重构或现实传送）。
    [ContextMenu("填入默认物品")]
    public void FillDefaults()
    {
        items = Defaults();
    }

    // 列表里没有的默认物品补上（按 id；已有的不动，保留 Inspector 里改的数值）。
    void AddMissingDefaults()
    {
        foreach (ItemData d in Defaults())
            if (Get(d.id) == null) items.Add(d);
    }

    static List<ItemData> Defaults()
    {
        return new List<ItemData>
        {
            // —— 食物：NPC 饿了会自己吃 ——
            ItemData.Make("ration", "蛋白营养块", "タンパク栄養ブロック", ItemCategory.Food,
                -30f, 0f, 0f, 0f,
                "终端机从现实传送来的压缩口粮。味道很差，但能填饱肚子。",
                "端末が現実世界から転送した圧縮食料。味はひどいが、腹は満たせる。"),
            ItemData.Make("water", "净化水", "浄化水", ItemCategory.Food,
                -10f, 10f, 0f, 0f,
                "经终端机净化的水。能稍微缓解饥饿、恢复一点体力。",
                "端末で浄化された水。空腹を少し和らげ、体力をわずかに回復する。"),

            // —— 医疗：玩家（AI）决定给谁用 ——
            ItemData.Make("sedative", "镇静剂", "鎮静剤", ItemCategory.Medical,
                0f, -10f, 25f, 0f,
                "压制亚空间带来的精神侵蚀。能恢复理智，但会让人昏沉。对已崩解者无效。",
                "亜空間による精神侵食を抑える。正気を回復するが、だるくなる。崩壊した者には効かない。"),
            ItemData.Make("medkit", "急救包", "救急キット", ItemCategory.Medical,
                0f, 0f, 0f, 30f,
                "止血与缝合用品。恢复生命。",
                "止血と縫合の道具。生命を回復する。"),
            ItemData.Make("stimulant", "兴奋剂", "興奮剤", ItemCategory.Medical,
                10f, 40f, -5f, 0f,
                "强行唤醒身体。大幅恢复体力，但会更饿，也会轻微损耗理智。",
                "体を無理やり覚醒させる。体力を大きく回復するが、空腹が進み、正気も少し削れる。"),

            // —— 材料 / 特殊：不能直接使用 ——
            ItemData.Make("nano", "纳米物质", "ナノ物質", ItemCategory.Material,
                0f, 0f, 0f, 0f,
                "终端机将亚空间的扭曲物质重构而成的基础材料。用于建造。",
                "端末が亜空間の歪んだ物質を再構成した基礎素材。建築に使う。"),
            ItemData.Make("crystal", "异变结晶", "変異結晶", ItemCategory.Special,
                0f, 0f, 0f, 0f,
                "亚空间特有的发光结晶。蕴含能量，用途尚不明确。",
                "亜空間特有の発光する結晶。エネルギーを秘めているが、用途はまだ分からない。"),

            // —— 地形资源（地形系统：对 NPC 说"去砍树 / 采石头 / 挖矿 / 割草 / 钓鱼"）——
            ItemData.Make("wood", "木材", "木材", ItemCategory.Material,
                0f, 0f, 0f, 0f,
                "从营地周围的树上砍下的木材。亚空间的树摸起来有点凉。用于建造。",
                "拠点の周りの木から切り出した木材。亜空間の木は触るとひんやりする。建築に使う。"),
            ItemData.Make("stone", "石材", "石材", ItemCategory.Material,
                0f, 0f, 0f, 0f,
                "从石头上凿下的石块。用于建造。",
                "岩から切り出した石。建築に使う。"),
            ItemData.Make("ore", "矿石", "鉱石", ItemCategory.Material,
                0f, 0f, 0f, 0f,
                "亚空间矿石，夹着发光的结晶。以后可以冶炼。",
                "亜空間の鉱石。光る結晶が混じっている。いずれ精錬できる。"),
            ItemData.Make("fiber", "植物纤维", "植物繊維", ItemCategory.Material,
                0f, 0f, 0f, 0f,
                "割下的草晒干搓成的纤维。可以编绳、做简单的东西。",
                "刈った草を干して撚った繊維。縄や簡単な道具になる。"),
            ItemData.Make("subspace_protein", "亚空间蛋白质", "亜空間タンパク質", ItemCategory.Food,
                -20f, 0f, -2f, 0f,
                "从湖里钓上来的亚空间鱼处理成的蛋白质。能填饱肚子，但味道让人不安。",
                "湖で釣れた亜空間の魚から取ったタンパク質。腹は満たせるが、味がどこか不穏だ。"),

            // —— 装备：NPC 带在身上用（物品栏 NpcInventory；带着才显示挂件）——
            ItemData.Make("bottle", "水瓶", "水筒", ItemCategory.Food,
                -10f, 10f, 0f, 0f,
                "灌满净化水的水瓶。出营地时带着，在外面口渴只能喝它，喝完就没了。",
                "浄化水を詰めた水筒。拠点の外では喉が渇いてもこれしか飲めない。飲めばなくなる。"),
            ItemData.Make("pistol", "手枪", "拳銃", ItemCategory.Gear,
                0f, 0f, 0f, 0f,
                "从遗弃设备里翻出来的老式手枪。要有弹匣才能开火，一个弹匣 8 发。",
                "放棄された設備の中から見つかった古い拳銃。マガジンがないと撃てない。1 本 8 発。"),
            ItemData.Make("magazine", "手枪弹匣", "拳銃のマガジン", ItemCategory.Gear,
                0f, 0f, 0f, 0f,
                "手枪用的弹匣，装满 8 发。打空了换一个。",
                "拳銃用のマガジン。8 発入り。撃ち尽くしたら交換する。"),
            ItemData.Make("magazine_empty", "空弹匣", "空のマガジン", ItemCategory.Gear,     // 战术换弹收回来的空弹匣（NpcInventory.EmptyMag）
                0f, 0f, 0f, 0f,
                "打空的手枪弹匣。战术换弹时收回来的，里面一发都没有。",
                "撃ち尽くした拳銃のマガジン。タクティカルリロードで回収したもので、中身は空。"),
            ItemData.Make("axe", "斧头", "斧", ItemCategory.Gear,
                0f, 0f, 0f, 0f,
                "伐木用的斧头。危急时也能拿来防身。",
                "木を伐るための斧。いざという時は身を守る武器にもなる。"),
            ItemData.Make("hammer", "锤子", "ハンマー", ItemCategory.Gear,
                0f, 0f, 0f, 0f,
                "施工、敲石头用的锤子。危急时也能拿来防身。",
                "工事や石割りに使うハンマー。いざという時は身を守る武器にもなる。"),
            ItemData.Make("toolkit", "工具包", "工具キット", ItemCategory.Gear,
                0f, 0f, 0f, 0f,
                "螺丝刀、扳手、电工胶带……修修补补用的工具包。",
                "ドライバー、レンチ、絶縁テープ……修理に使う工具一式。"),
            ItemData.Make("ai_terminal", "小型AI终端机", "小型AI端末", ItemCategory.Gear,      // ABA 清除小组认它：带着的人不会被清除（AbaForce）
                0f, 0f, 0f, 0f,
                "ABA 配发的小型 AI 终端机。清除小组不会对带着它的人开枪。",
                "ABA支給の小型AI端末。掃討班はこれを持つ者を撃たない。"),
        };
    }

    // 按 id 查物品定义（找不到返回 null）。
    public ItemData Get(string id)
    {
        foreach (ItemData it in items)
            if (it.id == id) return it;
        return null;
    }

    // 当前是否日语界面。
    static bool IsJp => LocalizationManager.Instance != null
                        && LocalizationManager.Instance.current == Language.Japanese;

    // 按当前语言取物品名（找不到定义时直接显示 id，方便发现漏配）。
    public string GetName(string id)
    {
        ItemData it = Get(id);
        if (it == null) return id;
        return IsJp ? it.nameJa : it.nameZh;
    }

    // 按当前语言取物品说明。
    public string GetDesc(string id)
    {
        ItemData it = Get(id);
        if (it == null) return "";
        return IsJp ? it.descJa : it.descZh;
    }

    // 在仓库里找第一个"某种类、且数量>0"的物品，返回它的 id（没有则 null）。
    // NPC 饿了自己找吃的就用这个（不再写死"罐头"）。
    public string FindFirstInStorage(ItemCategory category, WorldState world)
    {
        foreach (ItemStack stack in world.sharedStorage)
        {
            if (stack.count <= 0) continue;
            ItemData it = Get(stack.itemName);
            if (it != null && it.category == category) return stack.itemName;
        }
        return null;
    }

    // 统计仓库里某种类物品的总数量（判断"物资是否不足"用）。
    public int CountInStorage(ItemCategory category, WorldState world)
    {
        int total = 0;
        foreach (ItemStack stack in world.sharedStorage)
        {
            ItemData it = Get(stack.itemName);
            if (it != null && it.category == category) total += stack.count;
        }
        return total;
    }
}
