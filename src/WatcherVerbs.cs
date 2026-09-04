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

namespace SolverWatcherAdapter;

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
    /// 原版读的是 CombatManager 的全局打牌历史，这里只能读模拟历史。两者的差别在于：搜索是从
    /// 当前状态起算的，所以路线里的第一张牌在模拟历史里没有前一张，而实际对局中这一回合可能
    /// 已经打过牌了。这种情况下返回 null 并记一条风险——宁可少报这个加成，也不要凭空假设一个
    /// 类型。不从镜像里读 CombatManager，因为镜像跑在后台线程上，读实时状态是求解器明确禁止的。
    /// </remarks>
    public static CardType? PreviousPlayedCardType(CardOnPlayMirrorContext context)
    {
        Player owner = Owner(context);
        CardModel self = context.PreviewCard;
        CombatPredictionCardPlayStartedEntry? previous = context.History
            .OfType<CombatPredictionCardPlayStartedEntry>()
            .LastOrDefault(entry =>
                ReferenceEquals(entry.CardPlay.Card.Owner, owner)
                && !ReferenceEquals(entry.CardPlay.Card, self));
        if (previous is not null)
            return previous.CardPlay.Card.Type;
        Unmirrored(context, $"{self.Id.Entry} 的上一张牌类型在搜索起点之前，模拟历史里看不到");
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

    // ---------- 观者专有 ----------

    /// <summary>对应 WatcherCombatHelper.GainMantra，含真言满 10 转神圣。</summary>
    /// <remarks>
    /// 转换规则来自 Mantra.AfterPowerAmountChangedCompat：阈值是变更后 Amount 大于等于 10，
    /// 减的是正好 10 而不是清零，而且 _isResolving 闩锁让嵌套的那次变更不再触发钩子。所以
    /// 0 到 20 只转换一次、留下 10。这里不能写成循环。
    ///
    /// 原版这条路径挂在 hook 广播方法的 Harmony postfix 上，求解器用自己的 mirror 替掉了整个
    /// hook 分发、从不调那个被打补丁的方法，所以它既不生效也不记风险——是必须手写补上的五个
    /// 无声缺口之一。
    /// </remarks>
    public static void GainMantra(CardOnPlayMirrorContext context, int amount)
    {
        if (amount <= 0)
            return;
        SimulatedCombatState combat = Combat(context);
        Creature self = Self(context);

        WatcherStatePower state = EnsureState(context);
        state.TotalMantraGainedThisCombat += amount;
        state.MantraGainedThisTurn += amount;

        Effects(context).ApplyPower(typeof(Mantra), self, amount, self);

        if (combat.GetMutablePower<Mantra>(self) is not { Amount: >= 10 } mantra)
            return;
        combat.SetPowerAmount(mantra, mantra.Amount - 10);
        WatcherStanceVerbs.EnterDivinity(context);
    }

    /// <summary>对应 WatcherCombatHelper.EnsureState：没有就以数量 1 施加一个。</summary>
    /// <remarks>
    /// 返回的一定是可变克隆。WatcherStatePower 的计数器 setter 会走 AssertMutable，对根实例
    /// 赋值会抛异常。
    /// </remarks>
    public static WatcherStatePower EnsureState(CardOnPlayMirrorContext context)
    {
        SimulatedCombatState combat = Combat(context);
        Creature self = Self(context);
        if (combat.GetAmount<WatcherStatePower>(self) <= 0)
            Effects(context).ApplyPower(typeof(WatcherStatePower), self, 1, self);
        return combat.GetMutablePower<WatcherStatePower>(self)
            ?? throw new InvalidOperationException("施加后仍然取不到观者状态 Power。");
    }

    public static WatcherStatePower? PeekState(CardOnPlayMirrorContext context)
        => Combat(context).GetPower<WatcherStatePower>(Self(context));

    /// <summary>对应 WatcherCombatHelper.ConsumeKnowFate。返回实际消耗掉的天命层数。</summary>
    /// <remarks>
    /// 两个细节按原版实现：尝试标记 KnowFateConsumptionAttemptedThisCard 在检查层数之前就置位，
    /// 也就是即使一层都没消耗到也算尝试过；消耗满的时候是移除整个 Power 而不是减到 0。
    /// </remarks>
    public static int ConsumeKnowFate(CardOnPlayMirrorContext context, int amount)
    {
        if (amount <= 0)
            return 0;
        SimulatedCombatState combat = Combat(context);
        Creature self = Self(context);

        WatcherStatePower state = EnsureState(context);
        state.KnowFateConsumptionAttemptedThisCard = true;

        if (combat.GetMutablePower<KnowFatePower>(self) is not { Amount: > 0 } power)
            return 0;

        int consumed = Math.Min(power.Amount, amount);
        state.KnowFateConsumedThisTurn = true;
        combat.SetPowerAmount(power, power.Amount - consumed);
        state.KnowFateLastObserved = combat.GetAmount<KnowFatePower>(self);
        return consumed;
    }

    /// <summary>对应 WatcherCombatHelper.GetEffectiveScryAmount。</summary>
    public static int EffectiveScryAmount(CardOnPlayMirrorContext context, int amount)
    {
        if (amount <= 0)
            return 0;
        if (Owner(context).Relics.Any(relic => relic.Id.Entry == "GOLDEN_EYE"))
            amount += 2;
        if (PowerAmount<GuardNextScryPower>(context) > 0)
            amount = Math.Max(0, amount - 2);
        return amount;
    }

    /// <summary>
    /// 预视。只镜像它的连带效果，不镜像玩家挑哪几张丢掉。
    /// </summary>
    /// <remarks>
    /// 挑牌是搜索分支问题而不是镜像问题：要在 beam 上再开一层组合分支。这里按"一张都不丢"
    /// 建模，因为那是玩家一定可以做出的选择，所以路线仍然可执行；同时记一条选择风险，让求解器
    /// 明确说出这条路线没有探索丢牌的可能性，而不是假装探索过了。
    ///
    /// 连带效果照原版 OnScry 实现：涅槃按层数给不受力量影响的格挡，弃牌堆里的经纬回手。
    /// </remarks>
    public static void Scry(CardOnPlayMirrorContext context, int amount)
    {
        int effective = EffectiveScryAmount(context, amount);
        if (effective <= 0)
            return;

        if (PowerAmount<NirvanaPower>(context) is > 0 and var nirvana)
            BlockFor(context, nirvana, ValueProp.Unpowered);

        PredictedCard[] weaves = context.OwnerState.DiscardPile.Cards
            .Where(card => card.Preview.Id.Entry == "WATCHER_WEAVE")
            .ToArray();
        if (weaves.Length > 0)
            context.Simulator.AddToPile(weaves, PileType.Hand);

        PlayerChoice(context, $"{context.PreviewCard.Id.Entry} 预视 {effective} 张后丢弃哪几张");
    }

    /// <summary>
    /// 额外回合。只施加 Power，不代替求解器结束回合。
    /// </summary>
    /// <remarks>
    /// 原版 TakeExtraTurn 施加 WatcherExtraTurnPower 之后会强制结束回合，但结束回合在求解器
    /// 里是它自己的一个动作、由搜索决定，卡牌镜像不该越过它去改回合流程。所以这里只施加 Power
    /// 并记一条风险。
    /// </remarks>
    public static void TakeExtraTurn(CardOnPlayMirrorContext context)
    {
        Power(context, typeof(WatcherExtraTurnPower), 1);
        Unmirrored(context, $"{context.PreviewCard.Id.Entry} 打出后会强制结束当前回合");
    }

    // ---------- 诚实降级 ----------

    /// <summary>声明这一处效果没有镜像。求解器会显示成红色的未镜像，而不是静默算错。</summary>
    public static void Unmirrored(CardOnPlayMirrorContext context, string what)
    {
        EngineDiagnostics.Warn($"[SolverWatcherAdapter] 未镜像：{what}");
        context.History.RecordRisk(PredictionRiskReason.MethodMirrorIncomplete);
    }

    /// <summary>声明这一处需要玩家在结算中做选择，求解器的搜索还没有为它开分支。</summary>
    public static void PlayerChoice(CardOnPlayMirrorContext context, string what)
    {
        EngineDiagnostics.Warn($"[SolverWatcherAdapter] 未建模的结算内选择：{what}");
        context.History.RecordRisk(PredictionRiskReason.UnresolvedPlayerChoice);
    }
}
