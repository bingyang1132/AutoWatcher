using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 观者所有牌共用的效果动词。
/// </summary>
/// <remarks>
/// 观者的 101 张牌实际只用到十来个动词，绝大多数牌是原版命令加一个观者动词。所以这一层是整个
/// 适配的地基：动词对了，每张牌的镜像就只是几行声明式的组合，一行一效果，可以逐行对照反编译
/// 源码来审。
/// </remarks>
internal static class WatcherVerbs
{
    // ---------- 上下文取值 ----------

    public static SimulatedCombatState Combat(CardOnPlayMirrorContext context)
        => context.CombatState as SimulatedCombatState
            ?? throw new InvalidOperationException("观者镜像需要可写的模拟战斗状态。");

    public static ICombatPredictionEffectSink Effects(CardOnPlayMirrorContext context)
        => (ICombatPredictionEffectSink)Combat(context);

    public static Player Owner(CardOnPlayMirrorContext context) => context.PreviewCard.Owner;

    public static Creature Self(CardOnPlayMirrorContext context) => context.PreviewCard.Owner.Creature;

    // ---------- 攻击 ----------

    /// <summary>按牌自己的伤害变量打单体。目标由 CardPlay 给出。</summary>
    public static void Attack(CardOnPlayMirrorContext context, int hits = 1)
    {
        if (context.CardPlay.Target is null)
        {
            Unmirrored(context, $"{context.PreviewCard.Id.Entry} 需要目标但 CardPlay 没有给");
            return;
        }
        context.AttackSingle(hits);
    }

    /// <summary>按显式数值打单体。用于伤害不是直接取自伤害变量的牌。</summary>
    public static void AttackFor(CardOnPlayMirrorContext context, decimal amount, int hits = 1)
    {
        if (context.CardPlay.Target is not { } target)
        {
            Unmirrored(context, $"{context.PreviewCard.Id.Entry} 需要目标但 CardPlay 没有给");
            return;
        }
        DamageCmd.Attack(amount)
            .FromCard(context.PreviewCard, context.CardPlay)
            .WithHitCount(hits)
            .Targeting(target)
            .Simulate(context.Simulator);
    }

    public static void AttackAllEnemies(CardOnPlayMirrorContext context, int hits = 1)
        => context.AttackAllOpponents(hits);

    public static void AttackAllEnemiesFor(CardOnPlayMirrorContext context, decimal amount, int hits = 1)
        => DamageCmd.Attack(amount)
            .FromCard(context.PreviewCard, context.CardPlay)
            .WithHitCount(hits)
            .TargetingAllOpponents(context.CombatState)
            .Simulate(context.Simulator);

    public static void AttackRandomEnemy(CardOnPlayMirrorContext context, int hits = 1)
        => context.AttackRandomOpponents(hits);

    // ---------- 格挡、抽牌、能量 ----------

    /// <summary>按牌自己的格挡变量给自己加格挡，沿用该变量的 ValueProp。</summary>
    public static void Block(CardOnPlayMirrorContext context) => context.GainBlock(Self(context));

    public static void BlockFor(
        CardOnPlayMirrorContext context,
        decimal amount,
        ValueProp props = ValueProp.Move)
        => context.GainBlock(Self(context), amount, props);

    public static void Draw(CardOnPlayMirrorContext context, int count)
    {
        if (count > 0)
            context.Simulator.Draw(Owner(context), count);
    }

    public static void GainEnergy(CardOnPlayMirrorContext context, decimal amount)
    {
        if (amount > 0)
            context.Simulator.GainEnergy(Owner(context), amount);
    }

    // ---------- Power ----------

    /// <summary>施加任意 PowerModel，包括观者自己的。</summary>
    /// <remarks>
    /// SimulatedCombatState.ApplyPower 走 MakeGenericMethod，对任何 PowerModel 子类都成立，
    /// 不需要逐类型注册。这是那 36 张只施加一个 Power 的牌能便宜覆盖的原因——观者的
    /// WatcherPowerCmdCompat.Apply 只是对原版 PowerCmd.Apply 的反射包装，没有自己的语义。
    /// </remarks>
    public static void Power(CardOnPlayMirrorContext context, Type powerType, int amount)
        => Effects(context).ApplyPower(powerType, Self(context), amount, Self(context));

    public static void PowerOnTarget(CardOnPlayMirrorContext context, Type powerType, int amount)
    {
        if (context.CardPlay.Target is not { } target)
        {
            Unmirrored(context, $"{context.PreviewCard.Id.Entry} 需要目标但 CardPlay 没有给");
            return;
        }
        Effects(context).ApplyPower(powerType, target, amount, Self(context));
    }

    public static int PowerAmount<TPower>(CardOnPlayMirrorContext context)
        where TPower : PowerModel
        => Combat(context).GetAmount<TPower>(Self(context));

    /// <summary>把 Power 设成确切数量而不是叠加。对应 WatcherPowerCmdCompat.SetAmount。</summary>
    public static void SetPower(CardOnPlayMirrorContext context, Type powerType, int amount)
    {
        SimulatedCombatState combat = Combat(context);
        Creature self = Self(context);
        PowerModel? existing = combat.EffectivePowers()
            .FirstOrDefault(power => power.GetType() == powerType && ReferenceEquals(power.Owner, self));
        if (existing is null)
        {
            Effects(context).ApplyPower(powerType, self, amount, self);
            return;
        }
        combat.SetPowerAmount(existing, amount);
    }

    // ---------- 读取 ----------

    public static int CurrentHpOf(CardOnPlayMirrorContext context, Creature creature)
        => context.State.GetCreature(creature).CurrentHp;

    public static int HandCount(CardOnPlayMirrorContext context) => context.OwnerState.Hand.Cards.Count;

    public static int HittableEnemyCount(CardOnPlayMirrorContext context)
        => Combat(context).HittableEnemies.Count;

    /// <summary>
    /// 对应 WatcherSimpleAHelper.GetPreviousPlayedCardType：同一个主人打出的上一张牌的类型，
    /// 排除当前这张。
    /// </summary>
    /// <remarks>
    /// 原版读的是 <c>CombatManager.Instance.History.CardPlaysStarted</c>，那是**整场战斗**的打牌
    /// 历史，不是本回合的。所以上一张牌是跨回合的：一个回合的第一张牌看到的是上一回合最后
    /// 打出的那张。
    ///
    /// 先读模拟历史；路线里的第一张牌在模拟历史里没有前一张，这时回落到求解器的根历史快照
    /// <c>SimulatedCombatState._rootHistory.CardPlaysStarted</c>——那是主线程在搜索开始时抓下来的
    /// 实机打牌历史，正是为这种查询准备的，读它不违反"后台不读实时状态"。
    ///
    /// 这一条实机报出过偏差：粉碎关节+ 作为第 4 回合的第一张牌打出，上一回合最后打的是防御
    /// （技能），实机因此给了目标易伤，而镜像当时按"看不到前一张"处理、没给易伤，于是模拟和
    /// 实机的敌人状态从那一刻起就不一致。
    ///
    /// 两处都查不到才返回 null 并记风险——那意味着这一场还没有打出过任何别的牌。
    /// </remarks>
    public static CardType? PreviousPlayedCardType(CardOnPlayMirrorContext context)
    {
        Player owner = Owner(context);
        CardModel self = context.PreviewCard;
        CombatPredictionCardPlayStartedEntry? simulated = context.History
            .OfType<CombatPredictionCardPlayStartedEntry>()
            .LastOrDefault(entry =>
                ReferenceEquals(entry.CardPlay.Card.Owner, owner)
                && !ReferenceEquals(entry.CardPlay.Card, self));
        if (simulated is not null)
            return simulated.CardPlay.Card.Type;

        CardModel original = context.Card.Original;
        CardPlayStartedEntry? live = Combat(context)._rootHistory.CardPlaysStarted
            .LastOrDefault(entry =>
                ReferenceEquals(entry.CardPlay.Card.Owner, owner)
                && !ReferenceEquals(entry.CardPlay.Card, original));
        if (live is not null)
            return live.CardPlay.Card.Type;

        Unmirrored(context, $"{self.Id.Entry} 之前这一场没有打出过别的牌，取不到上一张牌类型");
        return null;
    }

    /// <summary>直接击杀目标，不走伤害。对应 CreatureCmd.Kill。</summary>
    public static void KillTarget(CardOnPlayMirrorContext context)
    {
        if (context.CardPlay.Target is not { } target)
        {
            Unmirrored(context, $"{context.PreviewCard.Id.Entry} 需要目标但 CardPlay 没有给");
            return;
        }
        Effects(context).DoomKill(context.Simulator, [target]);
    }

    /// <summary>把牌堆之间移动牌。</summary>
    public static void MoveToPile(
        CardOnPlayMirrorContext context,
        IReadOnlyList<PredictedCard> cards,
        PileType pile)
    {
        if (cards.Count > 0)
            context.Simulator.AddToPile(cards, pile);
    }

    // ---------- 生成牌 ----------

    public static void AddCards<TCard>(
        CardOnPlayMirrorContext context,
        PileType pile,
        int count,
        CardPilePosition position = CardPilePosition.Bottom)
        where TCard : CardModel
    {
        if (count <= 0)
            return;
        context.Simulator.CreateAndAddGeneratedCardsToCombat<TCard>(
            Owner(context), pile, count, Owner(context), position);
    }

    // ---------- 观者专有：一律委托给通用实现 ----------
    //
    // 这些动词遗物和药水也要用，实现放在 WatcherSimVerbs 上。这里只做上下文转换，不重复
    // 写一份逻辑——两份实现迟早会各自演化，而这类分歧不会报编译错误。

    public static void GainMantra(CardOnPlayMirrorContext context, int amount)
        => WatcherSimVerbs.GainMantra(WatcherSim.From(context), amount);

    public static WatcherStatePower EnsureState(CardOnPlayMirrorContext context)
        => WatcherSimVerbs.EnsureState(WatcherSim.From(context));

    public static WatcherStatePower? PeekState(CardOnPlayMirrorContext context)
        => WatcherSimVerbs.PeekState(WatcherSim.From(context));

    public static int ConsumeKnowFate(CardOnPlayMirrorContext context, int amount)
        => WatcherSimVerbs.ConsumeKnowFate(WatcherSim.From(context), amount);

    public static int EffectiveScryAmount(CardOnPlayMirrorContext context, int amount)
        => WatcherSimVerbs.EffectiveScryAmount(WatcherSim.From(context), amount);

    public static void Scry(CardOnPlayMirrorContext context, int amount)
        => WatcherSimVerbs.Scry(WatcherSim.From(context), amount, context.PreviewCard.Id.Entry);

    /// <summary>结算中强制结束本回合。</summary>
    /// <remarks>
    /// 求解器对这件事有一等支持：<c>SimulatedCombatState.RequestPlayerTurnEnd</c> 打一个标记，
    /// 搜索在这张牌结算完之后读到标记就立刻 <c>AdvanceRound</c>，并把这个动作标成
    /// <c>EndsPlayerTurn</c>。原版虚空形态走的就是这条路
    /// （<c>CardOnPlaySupport</c> 里 <c>case VoidForm</c> 之后紧跟 <c>combat.RequestPlayerTurnEnd()</c>），
    /// 所以镜像调它并不是越权，而是照原版的用法。
    ///
    /// 这一条以前只记风险不真结束回合，后果是实机事故：求解器给出的路线是
    /// 火焰纹 → 渎神 → 结末 → 打击，它以为打击能在同一回合杀死最后一个敌人、于是渎神的
    /// 回合结束死亡永远不会到来。实际结末打完回合就结束了，打击根本没机会打出，下一回合
    /// 开始时玩家被渎神杀死。风险标记只是显示上的红字，不会把不可能的续接从搜索里去掉，
    /// 所以这种「后面还能接牌」的错误必须真的建模，光记风险不够。
    /// </remarks>
    public static void ForceEndTurn(CardOnPlayMirrorContext context)
        => Combat(context).RequestPlayerTurnEnd();

    /// <summary>额外回合：施加 Power 并强制结束当前回合。</summary>
    /// <remarks>
    /// 顺序照原版 <c>WatcherCombatHelper.TakeExtraTurn</c>：先施加 <c>WatcherExtraTurnPower</c>，
    /// 再结束回合。求解器在回合推进里读这个 Power 得靠
    /// <see cref="WatcherExtraTurnPatch" /> 那两条补丁，因为它判断额外回合的地方是硬编码的。
    /// </remarks>
    public static void TakeExtraTurn(CardOnPlayMirrorContext context)
    {
        Power(context, typeof(WatcherExtraTurnPower), 1);
        ForceEndTurn(context);
    }

    // ---------- 诚实降级 ----------

    /// <summary>声明这一处效果没有镜像。求解器会显示成红色的未镜像，而不是静默算错。</summary>
    // ---------- 局外收益 ----------

    /// <summary>
    /// 这张牌打出去了，而它的收益要靠斩杀兑现。消耗牌才需要记。
    /// </summary>
    /// <remarks>
    /// 求解器分开记"打出了"和"兑现了"。消耗牌不斩杀就白扔，光看"兑现了"分不出"从没抽到"和
    /// "当普通攻击打掉了"这两种路线，后者才是浪费。原版的猎杀和饱食走同一条记法；贪婪之手不
    /// 消耗，所以只记兑现，不记打出。
    /// </remarks>
    public static void RecordFatalKillCardPlayed(CardOnPlayMirrorContext context)
        => SolverCompat.RecordFatalKillGoalCardPlayed?.Invoke(Combat(context));

    /// <summary>斩杀兑现了一笔带出本场的收益。</summary>
    /// <param name="value">
    /// 折算到求解器的长期资源刻度上。那个刻度大致按金币算：贪婪之手记面值，猎杀记 30，
    /// 生成一瓶药水记 20。
    /// </param>
    public static void RecordFatalKillBonus(CardOnPlayMirrorContext context, int value)
    {
        SimulatedCombatState combat = Combat(context);
        combat.RecordLongTermResource(value);
        // 目标标志位只有开发版求解器才有，没有也不影响上面那笔收益的计价。见 SolverCompat。
        SolverCompat.RecordFatalKillGoal?.Invoke(combat);
    }

    /// <summary>这张牌自己打死了目标，而且目标身上没有取消斩杀的效果。</summary>
    /// <remarks>
    /// 和求解器 <c>CorePowerSupport.WasFatalKill</c> 同一条判据，只是那个是私有的。两点都要：
    /// 死亡必须由这张牌造成（扫这次出牌以来的伤害记录，认卡牌来源和"目标被打死"标记），
    /// 而且目标的每一个 Power 都允许死亡触发斩杀 —— 复活、分裂这类会把斩杀吃掉。
    ///
    /// <paramref name="historyStart" /> 要在攻击之前取。
    /// </remarks>
    public static bool WasFatalKill(CardOnPlayMirrorContext context, int historyStart)
    {
        if (context.CardPlay.Target is not { } target)
            return false;
        SimulatedCombatState combat = Combat(context);
        bool deathTriggersFatal = combat.EffectivePowers()
            .Where(power => power.Owner == target)
            .All(power => power.ShouldOwnerDeathTriggerFatal());
        if (!deathTriggersFatal)
            return false;

        foreach (CombatPredictionHistoryEntry entry in context.History.EntriesFrom(historyStart))
        {
            if (entry is CombatPredictionDamageReceivedEntry damage
                && ReferenceEquals(damage.CardSource?.Original, context.Card.Original)
                && damage.Receiver == target
                && damage.Result.WasTargetKilled)
            {
                return true;
            }
        }
        return false;
    }

    public static void Unmirrored(CardOnPlayMirrorContext context, string what)
    {
        EngineDiagnostics.Warn($"[AutoWatcher] 未镜像：{what}");
        context.History.RecordRisk(PredictionRiskReason.MethodMirrorIncomplete);
    }

    /// <summary>声明这一处需要玩家在结算中做选择，求解器的搜索还没有为它开分支。</summary>
    public static void PlayerChoice(CardOnPlayMirrorContext context, string what)
    {
        EngineDiagnostics.Warn($"[AutoWatcher] 未建模的结算内选择：{what}");
        context.History.RecordRisk(PredictionRiskReason.UnresolvedPlayerChoice);
    }
}
