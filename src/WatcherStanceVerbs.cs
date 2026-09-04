using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using WatcherMod;

namespace SolverWatcherAdapter;

/// <summary>
/// 姿态切换在模拟里的等价实现。对应 <c>WatcherCombatHelper.ChangeStance&lt;T&gt;</c>。
/// </summary>
/// <remarks>
/// 姿态本身就是原版可见的 <see cref="PowerModel" />，所以把它按 Power 施加和清零之后，
/// 求解器的状态指纹、剪枝和去重都自动认得它，不需要额外的预测状态。这是整个适配层能成立的
/// 关键：<c>Wrath.ModifyDamageMultiplicative</c> 这类只读钩子求解器本来就会回落到 mod 自己的
/// 实现，所以姿态一旦在模拟里正确，伤害倍率就自动正确。
/// </remarks>
internal static class WatcherStanceVerbs
{
    public static void EnterWrath(CardOnPlayMirrorContext context) => ChangeStance<Wrath>(context);

    public static void EnterCalm(CardOnPlayMirrorContext context) => ChangeStance<Calm>(context);

    public static void EnterDivinity(CardOnPlayMirrorContext context) => ChangeStance<Divinity>(context);

    public static void EnterForeseen(CardOnPlayMirrorContext context) => ChangeStance<Foreseen>(context);

    public static void ExitStance(CardOnPlayMirrorContext context)
    {
        SimulatedCombatState combat = RequireSimulated(context);
        Player owner = context.PreviewCard.Owner;
        Creature creature = owner.Creature;
        Type? current = CurrentStance(combat, creature);
        if (current is null)
            return;
        ClearAllStances(context, combat, creature, owner);
        AfterStanceChanged(context, combat, owner, creature, current, newStance: null);
    }

    private static void ChangeStance<TStance>(CardOnPlayMirrorContext context)
        where TStance : PowerModel
    {
        SimulatedCombatState combat = RequireSimulated(context);
        Player owner = context.PreviewCard.Owner;
        Creature creature = owner.Creature;

        // 入定锁：有这个 Power 时整个切换被吞掉，连退出当前姿态都不发生。
        if (combat.GetAmount<CannotChangeStancePower>(creature) > 0)
            return;

        Type target = typeof(TStance);
        Type? current = CurrentStance(combat, creature);
        if (current == target)
            return;

        ClearAllStances(context, combat, creature, owner);
        ((ICombatPredictionEffectSink)combat).ApplyPower(target, creature, 1, creature);

        // 进入神圣给 3 点能量，这是 Divinity.AfterApplied 的效果，不是切换本身的效果。
        if (target == typeof(Divinity))
            context.Simulator.GainEnergy(owner, 3m);

        AfterStanceChanged(context, combat, owner, creature, current, target);
    }

    /// <summary>
    /// 顺序要紧：原版按 神圣、预知、愤怒、平静 逐个移除，平静的 +2 能量在新姿态生效之前就落地。
    /// </summary>
    private static void ClearAllStances(
        CardOnPlayMirrorContext context,
        SimulatedCombatState combat,
        Creature creature,
        Player owner)
    {
        ClearStance<Divinity>(combat, creature);
        ClearStance<Foreseen>(combat, creature);
        ClearStance<Wrath>(combat, creature);
        if (ClearStance<Calm>(combat, creature))
            context.Simulator.GainEnergy(owner, 2m);
    }

    private static bool ClearStance<TStance>(SimulatedCombatState combat, Creature creature)
        where TStance : PowerModel
    {
        if (combat.GetPower<TStance>(creature) is not { Amount: > 0 } power)
            return false;
        combat.SetPowerAmount(power, 0);
        return true;
    }

    private static Type? CurrentStance(SimulatedCombatState combat, Creature creature)
    {
        if (combat.GetAmount<Divinity>(creature) > 0)
            return typeof(Divinity);
        if (combat.GetAmount<Foreseen>(creature) > 0)
            return typeof(Foreseen);
        if (combat.GetAmount<Wrath>(creature) > 0)
            return typeof(Wrath);
        if (combat.GetAmount<Calm>(creature) > 0)
            return typeof(Calm);
        return null;
    }

    /// <summary>
    /// 对应 <c>WatcherCombatHelper.OnStanceChanged</c> 的六路扇出。
    /// </summary>
    /// <remarks>
    /// 只实现能确定做对的三路。剩下三路会记一条风险，让求解器把它显示成红色的"未镜像"，
    /// 而不是静默算错。凌波微步要在出牌堆非空时改 Power 上的一个字段，紫莲花之外的遗物读取
    /// 和天命消耗标记都依赖 WatcherStatePower 的私有计数器，这些都值得单独一轮验证再接。
    /// </remarks>
    private static void AfterStanceChanged(
        CardOnPlayMirrorContext context,
        SimulatedCombatState combat,
        Player owner,
        Creature creature,
        Type? oldStance,
        Type? newStance)
    {
        if (newStance == typeof(Wrath) && combat.GetAmount<RushdownPower>(creature) > 0)
            RecordUnmirrored(context, "凌波微步的进入愤怒抽牌");

        if (combat.GetAmount<MentalFortressPower>(creature) is > 0 and var fortress
            && oldStance != newStance)
        {
            context.GainBlock(creature, fortress, ValueProp.Unpowered);
        }

        if (oldStance == typeof(Calm)
            && owner.Relics.Any(relic => relic.Id.Entry == "VIOLET_LOTUS"))
        {
            context.Simulator.GainEnergy(owner, 1m);
        }

        // 任何姿态变化都让弃牌堆里的疾风连打回手，包括退出姿态。
        PredictedCard[] flurries = context.OwnerState.DiscardPile.Cards
            .Where(card => card.Preview is WatcherFlurryOfBlows)
            .ToArray();
        if (flurries.Length > 0)
            context.Simulator.AddToPile(flurries, PileType.Hand);

        if (oldStance == typeof(Foreseen)
            && combat.GetPower<WatcherStatePower>(creature)?.KnowFateConsumedThisTurn == true)
        {
            RecordUnmirrored(context, "退出预知时的悟命");
        }
    }

    private static void RecordUnmirrored(CardOnPlayMirrorContext context, string what)
    {
        EngineDiagnostics.Warn($"[SolverWatcherAdapter] 未镜像的姿态切换连带效果：{what}");
        context.History.RecordRisk(PredictionRiskReason.MethodMirrorIncomplete);
    }

    private static SimulatedCombatState RequireSimulated(CardOnPlayMirrorContext context)
        => context.CombatState as SimulatedCombatState
            ?? throw new InvalidOperationException(
                "观者姿态镜像需要可写的模拟战斗状态。");
}
