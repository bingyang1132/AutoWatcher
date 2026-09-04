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

    /// <summary>额外回合。只施加 Power，不代替求解器结束回合。</summary>
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
