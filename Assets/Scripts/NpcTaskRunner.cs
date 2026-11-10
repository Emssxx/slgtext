using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI; // 推箱子用 NavMeshAgent

// 任务队列执行器：挂在 NPC 物体上（和 NpcMover 同一个物体）。
// 持有一串任务，一个接一个执行，每个完成后自动取下一个，直到全部做完。
// 支持动作：go（走到地点）、act（原地互动）、push（推箱子到目标，方案A）、harvest（采集天然资源 / 钓鱼 / 打水，地形系统）。
[RequireComponent(typeof(NpcMover))]
public class NpcTaskRunner : MonoBehaviour
{
    public string npcId;             // 对应数据 id（和 NpcView 一致）
    public float actDuration = 1.5f; // act 互动动作持续时间

    private NpcMover _mover;
    private Animator _animator;
    private NpcView _view;
    private Queue<NpcTask> _queue = new Queue<NpcTask>();
    private bool _running = false;
    private string _claimedId; // 在 NpcBusy 登记占用时用的 id（物体销毁后也能正确解除）
    private System.Action<bool> _onDone; // 这一串任务结束时通知谁：true = 全部做完，false = 被取消 / 被新的一串替换（NpcIntentBrain 用）

    // 是否正在执行玩家指令的任务（闲置行为系统读它：有任务时不去干扰移动）。
    public bool IsBusy => _running;

    void Awake()
    {
        _mover = GetComponent<NpcMover>();
        _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        _view = GetComponent<NpcView>();
    }

    void OnDisable()
    {
        // 物体被关掉/销毁时协程会停，这里把状态和占用登记一起收拾干净。
        if (_running) { _running = false; ReleaseClaim(); }
        FinishCallback(false);
    }

    // 取消所有任务并立刻停止移动（被事件打断时调用）。
    public void CancelAll()
    {
        NpcHealth.SetResting(CurrentId, false);
        _queue.Clear();          // 清空待执行队列
        _running = false;        // 重置运行标志
        StopAllCoroutines();     // 停止正在跑的任务协程（DoGo/DoAct/DoPush）
        _mover.Stop();           // 通知 NpcMover 停下，清路径、停动画
        ReleaseClaim();
        FinishCallback(false);
    }

    // 通知这一串任务的发起者（只通知一次）。
    void FinishCallback(bool ok)
    {
        System.Action<bool> cb = _onDone;
        _onDone = null;
        if (cb == null) return;
        try { cb(ok); }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    // 执行玩家指令期间在 NpcBusy 登记占用（优先级最高），需求行为、闲置行为都会让路。
    void ClaimNpc()
    {
        _claimedId = (_view != null && !string.IsNullOrEmpty(_view.npcId)) ? _view.npcId : npcId;
        NpcBusy.Claim(_claimedId, NpcBusy.Task);
    }

    void ReleaseClaim()
    {
        if (string.IsNullOrEmpty(_claimedId)) return;
        NpcBusy.Release(_claimedId, NpcBusy.Task);
        _claimedId = null;
    }

    // 下达一串任务（清掉旧的，执行新的）。onDone：这一串结束时回调（true = 做完，false = 被取消 / 被替换），可不传。
    public void RunTasks(List<NpcTask> tasks, System.Action<bool> onDone = null)
    {
        FinishCallback(false); // 旧的一串被替换了
        _onDone = onDone;
        _queue.Clear();
        foreach (NpcTask t in tasks) _queue.Enqueue(t);
        if (!_running) StartCoroutine(RunLoop());
    }

    IEnumerator RunLoop()
    {
        _running = true;
        ClaimNpc();
        while (_queue.Count > 0)
        {
            NpcTask task = _queue.Dequeue();
            yield return StartCoroutine(DoOneTask(task));
        }
        _running = false;
        ReleaseClaim();
        FinishCallback(true);
    }

    IEnumerator DoOneTask(NpcTask task)
    {
        // 健康状态：濒死什么都做不了；受伤 / 致命伤干不了活（搜寻、推箱子、互动）
        NpcData me = Self;
        if (me != null && !NpcHealth.CanMove(me) && task.action != "use_item")
        {
            Report(me, null, LanguageGuard.Pick(me.npcName + " 动不了了（" + NpcHealth.TagName(HealthTag.Dying, false) + "）。", me.npcName + "はもう動けない（" + NpcHealth.TagName(HealthTag.Dying, true) + "）。"));
            yield break;
        }
        if (me != null && (task.action == "search_item" || task.action == "push" || task.action == "act" || task.action == "harvest") && !NpcHealth.CanWork(me))
        {
            bool jp = LanguageGuard.IsJp;
            Report(me, null, jp ? me.npcName + "は" + NpcHealth.CannotWorkReason(me, true) + "で作業できない。" : me.npcName + " " + NpcHealth.CannotWorkReason(me, false) + "，干不了活。");
            yield break;
        }
        switch (task.action)
        {
            case "go":
                yield return StartCoroutine(DoGo(task.destination));
                break;
            case "act":
                yield return StartCoroutine(DoAct());
                break;
            case "push":
                yield return StartCoroutine(DoPush(task.target, task.destination));
                break;
            // —— 以下由指令系统（NpcCommands，AI 词库）排出 ——
            case "use_item":    // 把物品用在自己/别人身上（target = 物品 id，destination = "@self" 或 "@npc:id"）
                yield return StartCoroutine(DoUseItem(task.target, task.destination));
                break;
            case "take_item":   // 从仓库拿一个物品带在身上（target = 物品 id）
                DoTakeItem(task.target);
                yield return new WaitForSeconds(0.8f);
                break;
            case "put_item":    // 把身上的东西放回仓库（target = 物品 id；"*" = 身上全部）
                DoPutItem(task.target);
                yield return new WaitForSeconds(0.8f);
                break;
            case "search_item": // 出营地搜寻（target = 想找的物品 id；空 = 普通搜寻物资）
                yield return StartCoroutine(DoSearch(task.target));
                break;
            case "rest":        // 原地休息一会儿
                yield return StartCoroutine(DoRest());
                break;
            case "eat":         // 从仓库拿吃的吃掉
                yield return new WaitForSeconds(1.5f);
                if (Self != null) NpcDecisionRules.Execute(Self, NpcDecisionRules.Behavior.Eat, WorldManager.Instance.world);
                break;
            case "harvest":     // 采集（target = "wood" / "stone" / "ore" / "fiber" / "fish" / "water"；destination = 坐标 "x,y"，可空）
                yield return StartCoroutine(DoHarvest(task.target, task.destination));
                break;
            default:
                Debug.LogWarning("未知任务动作：" + task.action);
                break;
        }
    }

    // ============ 指令系统的任务（物品 / 搜寻 / 休息）============

    string CurrentId => (_view != null && !string.IsNullOrEmpty(_view.npcId)) ? _view.npcId : npcId;
    NpcData Self => WorldManager.Instance != null ? WorldManager.Instance.GetNpcById(CurrentId) : null;

    // 走到某处（带超时，卡住也不会一直等）。
    IEnumerator WalkTo(Vector3 pos, float timeout)
    {
        _mover.MoveTo(pos);
        yield return null;
        float t = 0f;
        while (_mover.IsMoving && t < timeout) { t += Time.deltaTime; yield return null; }
    }

    // use_item：用在别人身上时，先走到对方身边（对方可能在走动，隔一会儿重新瞄准），再用。
    IEnumerator DoUseItem(string itemId, string dest)
    {
        WorldState w = WorldManager.Instance.world;
        NpcData self = Self;
        if (self == null) yield break;
        NpcData target = self;
        if (!string.IsNullOrEmpty(dest) && dest.StartsWith("@npc:"))
            target = WorldManager.Instance.GetNpcById(dest.Substring(5)) ?? self;

        if (target != self)
        {
            float t = 0f;
            while (t < 20f)
            {
                NpcMover tm = NpcView.GetMover(target.id);
                if (tm == null || target.IsDead) break;
                if (Vector2.Distance(transform.position, tm.transform.position) <= 1.0f) break;
                _mover.MoveTo(tm.transform.position);
                yield return new WaitForSeconds(0.4f);
                t += 0.4f;
            }
            _mover.Stop();
            NpcMover tm2 = NpcView.GetMover(target.id);
            if (tm2 != null) _mover.FaceTowards(tm2.transform.position);
        }
        if (itemId == "medkit") { NpcRig rig = GetComponent<NpcRig>(); if (rig != null) rig.HoldItem("medkit", 1.6f); } // 外观：包扎时手上拿出医疗包（腰上挂着才会拿）
        yield return new WaitForSeconds(1f); // 包扎 / 喂药的时间

        // 身上带着的先放回仓库再用：物品效果统一走 ItemUseRules（它从仓库扣 1 个），逻辑只有一份。
        if (TakeFromInventory(self, itemId)) WorldManager.Instance.AddToStorage(itemId, 1);
        bool ok = ItemUseRules.Use(target, itemId, w, false);
        string itemName = GameLexicon.NameOf(itemId);
        string log = !ok ? LF("cmd_log_use_fail", self.npcName, itemName)
                   : target == self ? LF("log_use_item", self.npcName, itemName)
                   : LF("cmd_log_use_on", self.npcName, target.npcName, itemName);
        Report(self, target, log);
        // 给别人用了药 / 绷带：对方更喜欢、更信任、更尊重这个人
        if (ok && target != self)
            RelationshipManager.Apply(target, self, 10f, 6f, 4f, LF("rel_reason_helped", w.day, self.npcName, itemName));
    }

    // take_item：从仓库拿一个带在身上（以后"给某人用"时优先用身上的）。
    // 物品栏规则（格子、同类上限、水瓶用净化水灌）在 NpcInventory；成功的日志它会写。玩家叫拿的东西之后不会自己放回。
    void DoTakeItem(string itemId)
    {
        NpcData self = Self;
        if (self == null) return;
        if (!NpcInventory.TakeFromStorage(self, itemId, true, out string why))
            Report(self, null, LF("inv_log_take_fail", self.npcName, GameLexicon.NameOf(itemId), why));
    }

    // put_item：把身上的东西放回仓库（"*" = 全部）。成功的日志由 NpcInventory 写。
    void DoPutItem(string itemId)
    {
        NpcData self = Self;
        if (self == null) return;
        if (NpcInventory.PutBack(self, string.IsNullOrEmpty(itemId) ? "*" : itemId) == 0)
            Report(self, null, LF("inv_log_put_none", self.npcName, itemId == "*" || string.IsNullOrEmpty(itemId) ? L("inv_anything") : GameLexicon.NameOf(itemId)));
    }

    // search_item：走到营地外（优先迷雾里没看清的地方）→ 翻找 → 回营地。
    // 指定了物品时按种类有一定概率找到，找不到也会带回一份营养块。
    IEnumerator DoSearch(string itemId)
    {
        Vector3 camp = NpcNav.CampCenter(transform.position);
        float r = NpcNav.CampRadius;
        if (NpcNav.TryRandomPoint(transform.position, camp, r + 1f, r + 6f, true, out Vector3 field))
        {
            yield return StartCoroutine(WalkTo(field, 20f));
            _mover.Stop();
            float t = 0f, next = 0.8f;
            while (t < 4f) // 原地左右张望着翻找
            {
                if (t >= next) { _mover.FaceTowards(transform.position + (Random.value < 0.5f ? Vector3.left : Vector3.right)); next = t + 1f; }
                t += Time.deltaTime;
                yield return null;
            }
            Vector3 back = camp;
            if (NpcNav.TryRandomPoint(transform.position, camp, 0f, 1.2f, false, out Vector3 near)) back = near;
            yield return StartCoroutine(WalkTo(back, 20f));
            _mover.Stop();
        }

        NpcData self = Self;
        if (self == null) yield break;
        WorldState w = WorldManager.Instance.world;
        if (string.IsNullOrEmpty(itemId))
        {
            NpcDecisionRules.Execute(self, NpcDecisionRules.Behavior.Gather, w); // 仓库 +1 营养块、写日志、可能受伤
            yield break;
        }
        // 体力按走路距离扣（NpcHealth）；外面危险，有概率受伤
        if (NpcHealth.RollFieldInjury(self))
            Report(self, null, LanguageGuard.Pick(self.npcName + " 在外面受了伤（" + NpcHealth.InjuryPart(self, false) + "）。", self.npcName + "は外で怪我をした（" + NpcHealth.InjuryPart(self, true) + "）。"));
        string itemName = GameLexicon.NameOf(itemId);
        if (Random.value < FindChance(itemId))
        {
            WorldManager.Instance.AddToStorage(itemId, 1);
            Report(self, null, LF("cmd_log_found", self.npcName, itemName));
        }
        else
        {
            WorldManager.Instance.AddToStorage("ration", 1);
            Report(self, null, LF("cmd_log_not_found", self.npcName, itemName));
        }
    }

    // ============ 采集（地形系统）============

    // harvest：
    //   树 / 石头 / 矿石 / 草：找最近的（或指定坐标附近的）→ 走到旁边干活（体力按施工扣，健康状态影响速度）→ 采完带回仓库。
    //     中途干不动了（受伤 / 体力耗尽）就停下，进度留在资源上，下次接着采。
    //   钓鱼 / 打水：交给 DoLakeWork。
    IEnumerator DoHarvest(string res, string dest)
    {
        NpcData self = Self;
        if (self == null) yield break;
        if (res == "fish" || res == "water") { yield return StartCoroutine(DoLakeWork(res == "fish")); yield break; }
        if (!ResourceInfo.TryParseKind(res, out ResourceKind kind)) yield break;
        bool jp = LanguageGuard.IsJp;

        ResourceNode node = TerrainSystem.FindNearest(kind, transform.position, CurrentId, TerrainSystem.ParseCell(dest));
        if (node == null)
        {
            Report(self, null, jp ? self.npcName + "は採れる" + ResourceInfo.KindName(kind, true) + "を見つけられなかった。" : self.npcName + " 没找到能采的" + ResourceInfo.KindName(kind, false) + "。");
            yield break;
        }
        string nodeName = node.DisplayName(jp);
        node.Reserve(CurrentId);
        self.currentAction = (jp ? "採集に向かう：" : "前往采集：") + nodeName;

        // 1. 走过去
        _mover.MoveTo(node.StandPoint(transform.position));
        yield return null;
        float walk = 0f;
        while (_mover.IsMoving && walk < 30f && node != null && !node.depleted)
        {
            node.Reserve(CurrentId);
            walk += Time.deltaTime;
            yield return null;
        }
        _mover.Stop();
        if (node == null || node.depleted)
        {
            Report(self, null, jp ? self.npcName + "が着いたときには、" + nodeName + "はもう無かった。" : self.npcName + " 赶到时，" + nodeName + "已经没了。");
            yield break;
        }
        float reach = node.size * (GridManager.Instance != null ? GridManager.Instance.cellSize : 0.34f) * 0.75f + 0.9f;
        if (Vector2.Distance(transform.position, node.Center) > reach)
        {
            node.Release(CurrentId);
            Report(self, null, jp ? self.npcName + "は" + nodeName + "までたどり着けなかった。" : self.npcName + " 走不到" + nodeName + "跟前。");
            yield break;
        }
        _mover.FaceTowards(node.Center);

        // 2. 干活
        float anim = 0f;
        while (node != null && !node.depleted && node.progress < node.WorkSeconds)
        {
            if (self.IsDead || !NpcHealth.CanWork(self) || self.energy <= 2f) break;
            float dt = Time.deltaTime;
            node.progress += dt * NpcHealth.WorkSpeed(self);
            NpcHealth.ConsumeBuild(self, dt);
            node.Reserve(CurrentId);
            self.currentAction = (jp ? "採集中：" : "采集中：") + nodeName + " " + Mathf.RoundToInt(node.Progress01 * 100f) + "%";
            anim -= dt;
            if (anim <= 0f) { if (_animator != null) _animator.SetTrigger("doAct"); anim = 1.5f; }
            yield return null;
        }
        if (node == null || node.depleted) yield break;
        node.Release(CurrentId);
        if (node.progress < node.WorkSeconds)
        {
            string why = self.IsDead ? "" : !NpcHealth.CanWork(self) ? NpcHealth.CannotWorkReason(self, jp) : (jp ? "疲れ切った" : "累得干不动了");
            Report(self, null, jp ? self.npcName + "は" + nodeName + "の採集を途中でやめた（" + why + "）。続きは次回。"
                                  : self.npcName + " 没采完" + nodeName + "（" + why + "），进度留着，下次接着采。");
            yield break;
        }

        // 3. 采完：资源消失，东西带回仓库
        string item = node.YieldItem;
        int amount = node.YieldAmount;
        node.Deplete();
        yield return StartCoroutine(CarryToStorage(item, amount, nodeName));
    }

    // 钓鱼 / 打水：走到最近的岸边，干一会儿。
    //   钓鱼：湖里有鱼、运气好就钓到一份亚空间蛋白质，带回仓库（湖里的鱼每天恢复一点，见 TerrainSystem）。
    //   打水：提几份湖水，倒进雨水收集器（湖水和雨水一样，要净化器净化后才能喝）。
    IEnumerator DoLakeWork(bool fish)
    {
        NpcData self = Self;
        if (self == null) yield break;
        bool jp = LanguageGuard.IsJp;
        TerrainSystem ts = TerrainSystem.Instance;
        if (ts == null || !TerrainSystem.TryFindShore(transform.position, out Vector3 stand, out Vector3 waterPos))
        {
            Report(self, null, jp ? self.npcName + "は行ける水辺を見つけられなかった。" : self.npcName + " 找不到能走到的水边。");
            yield break;
        }
        self.currentAction = fish ? (jp ? "釣りに向かう" : "去湖边钓鱼") : (jp ? "水汲みに向かう" : "去湖边打水");
        yield return StartCoroutine(WalkTo(stand, 30f));
        _mover.Stop();
        _mover.FaceTowards(waterPos);

        float need = fish ? ts.fishSeconds : ts.waterSeconds;
        float t = 0f, anim = 0f;
        while (t < need)
        {
            if (self.IsDead || !NpcHealth.CanWork(self))
            {
                Report(self, null, jp ? self.npcName + "は途中でやめた（" + NpcHealth.CannotWorkReason(self, true) + "）。" : self.npcName + " 干到一半停下了（" + NpcHealth.CannotWorkReason(self, false) + "）。");
                yield break;
            }
            float dt = Time.deltaTime;
            t += dt * NpcHealth.WorkSpeed(self);
            NpcHealth.ConsumeBuild(self, dt * 0.5f); // 比施工轻松
            self.currentAction = (fish ? (jp ? "釣り中 " : "钓鱼中 ") : (jp ? "水汲み中 " : "打水中 ")) + Mathf.RoundToInt(Mathf.Clamp01(t / Mathf.Max(0.1f, need)) * 100f) + "%";
            anim -= dt;
            if (anim <= 0f) { if (_animator != null) _animator.SetTrigger("doAct"); anim = 2f; }
            yield return null;
        }

        if (fish)
        {
            if (!TerrainSystem.TryCatchFish())
            {
                Report(self, null, jp ? self.npcName + "はしばらく粘ったが、何も釣れなかった。" : self.npcName + " 钓了半天，什么也没钓到。");
                yield break;
            }
            yield return StartCoroutine(CarryToStorage(ResourceInfo.FishItem, 1, jp ? "湖" : "湖里"));
            yield break;
        }

        // 打水：提到空位最多的收集器那里倒进去
        int carry = Mathf.Max(1, ts.lakeWaterPerTrip);
        RainCollector rc = WaterWorks.FreestCollector();
        if (rc == null)
        {
            Report(self, null, jp ? self.npcName + "は水を汲んだが、雨水タンクが満杯で捨てるしかなかった。" : self.npcName + " 打了水，可雨水收集器都满了，只好倒掉。");
            yield break;
        }
        self.currentAction = jp ? "水を運んでいる" : "提水回营地";
        Vector3 tank = NpcNav.Sample(rc.transform.position, 1.2f, out Vector3 beside) ? beside : rc.transform.position; // 站到收集器旁边
        yield return StartCoroutine(WalkTo(tank, 30f));
        _mover.Stop();
        int poured = WaterWorks.AddRaw(carry);
        Report(self, null, poured > 0
            ? (jp ? self.npcName + "は湖の水を" + poured + "杯汲んで雨水タンクに入れた（浄化しないと飲めない）。" : self.npcName + " 从湖里打了 " + poured + " 份水，倒进了雨水收集器（要净化后才能喝）。")
            : (jp ? self.npcName + "は水を運んだが、雨水タンクがもう満杯だった。" : self.npcName + " 把水提了回来，可收集器已经满了。"));
        self.currentAction = jp ? "水汲みを終えた" : "刚打完水";
    }

    // 把采到的东西搬回营地仓库。
    IEnumerator CarryToStorage(string item, int amount, string from)
    {
        NpcData self = Self;
        if (self == null) yield break;
        bool jp = LanguageGuard.IsJp;
        string itemName = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetName(item) : item;
        self.currentAction = (jp ? "運搬中：" : "搬运：") + itemName + " ×" + amount;
        Vector3 camp = NpcNav.CampCenter(transform.position);
        Vector3 drop = camp;
        if (NpcNav.TryRandomPoint(transform.position, camp, 0f, 1.2f, false, out Vector3 near)) drop = near;
        yield return StartCoroutine(WalkTo(drop, 30f));
        _mover.Stop();
        if (WorldManager.Instance == null) yield break;
        WorldManager.Instance.AddToStorage(item, amount);
        self.currentAction = jp ? "採集を終えた" : "刚采集完";
        Report(self, null, jp ? self.npcName + "は" + from + "から" + itemName + "×" + amount + "を持ち帰り、倉庫に入れた。"
                              : self.npcName + " 从" + from + "采到 " + itemName + " ×" + amount + "，搬回了仓库。");
    }

    // 指定物品的搜寻成功率（按种类，可调）。
    static float FindChance(string itemId)
    {
        ItemData d = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(itemId) : null;
        if (d == null) return 0.3f;
        switch (d.category)
        {
            case ItemCategory.Food:     return 0.6f;
            case ItemCategory.Medical:  return 0.35f;
            case ItemCategory.Material: return 0.5f;
            default:                    return 0.15f;
        }
    }

    // rest：原地休息（每秒回体力，消耗饥饿值，NpcHealth 结算），歇到不疲惫为止（最多 60 秒）。
    IEnumerator DoRest()
    {
        _mover.Stop();
        NpcData self = Self;
        if (self == null) yield break;
        NpcHealth.SetResting(CurrentId, true);
        NpcDecisionRules.Execute(self, NpcDecisionRules.Behavior.Rest, WorldManager.Instance.world); // 日志
        float target = NpcHealth.Instance != null ? NpcHealth.Instance.fatigueClearAt : 60f;
        float t = 0f;
        while (t < 60f && (t < 6f || self.energy < target) && self.satiety > 0f && !self.IsDead)
        {
            t += Time.deltaTime;
            yield return null;
        }
        NpcHealth.SetResting(CurrentId, false);
    }

    static bool TakeFromInventory(NpcData npc, string itemId)
    {
        return NpcInventory.Remove(npc, itemId, 1);   // 走物品栏：挂件 / 界面会跟着刷新
    }

    // 写进当事人的记忆 + 行为日志。
    static void Report(NpcData self, NpcData other, string log)
    {
        int day = WorldManager.Instance != null ? WorldManager.Instance.world.day : 0;
        IdleChatRules.AddMemory(self, day, log);
        if (other != null && other != self) IdleChatRules.AddMemory(other, day, log);
        if (LogManager.Instance != null) LogManager.Instance.AddLog(self.npcName, log, self.id);
    }

    static string LF(string key, params object[] args)
    {
        return LocalizationManager.Instance != null ? LocalizationManager.Instance.GetFormat(key, args) : key;
    }

    static string L(string key) => LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;

    // go：走到某地点，等到达。
    IEnumerator DoGo(string destination)
    {
        WorldState world = WorldManager.Instance.world;
        if (!CommandParser.TryGetLocationPos(destination, world, out float x, out float y))
        {
            Debug.LogWarning("找不到地点：" + destination);
            yield break;
        }
        _mover.MoveTo(new Vector3(x, y, 0));
        yield return null;
        while (_mover.IsMoving) yield return null;
    }

    // act：原地互动，用 idle2 动画占位。
    IEnumerator DoAct()
    {
        if (_animator != null) _animator.SetTrigger("doAct");
        yield return new WaitForSeconds(actDuration);
    }

    // push（方案A）：箱子用自己的 NavMeshAgent 寻路绕墙去目标，NPC 跟到箱子后方推。
    IEnumerator DoPush(string targetItemName, string destination)
    {
        WorldState world = WorldManager.Instance.world;

        // 找物品数据 + 场景物体
        WorldItem item = null;
        foreach (WorldItem it in world.worldItems)
            if (it.itemName == targetItemName) { item = it; break; }
        if (item == null) { Debug.LogWarning("找不到物品：" + targetItemName); yield break; }

        Transform itemTf = FindItemTransform(item.id);
        if (itemTf == null) { Debug.LogWarning("找不到物品场景物体：" + item.id); yield break; }

        NavMeshAgent boxAgent = itemTf.GetComponent<NavMeshAgent>();
        if (boxAgent == null) { Debug.LogWarning("箱子没有 NavMeshAgent：" + item.id); yield break; }

        if (!CommandParser.TryGetLocationPos(destination, world, out float dx, out float dy))
        { Debug.LogWarning("找不到地点：" + destination); yield break; }
        Vector3 goal = new Vector3(dx, dy, 0);

        // 1. NPC 先走到箱子旁边
        _mover.MoveTo(new Vector3(item.x, item.y, 0));
        yield return null;
        while (_mover.IsMoving) yield return null;

        // 2. 开始推
        if (_animator != null) _animator.SetBool("isPushing", true);

        float npcGap = 0.4f;
        float arrive = 0.2f;
        boxAgent.SetDestination(goal);

        // 3. 每帧：NPC 跟到箱子后方
        while (true)
        {
            if (!boxAgent.pathPending && boxAgent.remainingDistance <= arrive) break;

            Vector3 dir = boxAgent.velocity.normalized;
            if (dir.sqrMagnitude < 0.01f) dir = (goal - itemTf.position).normalized;

            Vector3 npcTarget = itemTf.position - dir * npcGap;
            _mover.MoveTo(npcTarget);

            yield return null;
        }

        // 4. 到了，停止推
        boxAgent.ResetPath();
        if (_animator != null) _animator.SetBool("isPushing", false);

        // 同步箱子坐标回数据
        item.x = itemTf.position.x;
        item.y = itemTf.position.y;
    }

    // 按物品 id 找场景物体。
    Transform FindItemTransform(string itemId)
    {
        foreach (WorldItemMarker m in FindObjectsByType<WorldItemMarker>()) // 新 API 替代过时的 FindObjectsOfType（默认不排序，更快）
            if (m.id == itemId) return m.transform;
        return null;
    }
}
