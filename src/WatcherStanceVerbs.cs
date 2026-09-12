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
/// 姿态切换在模拟里的等价实现。对应 <c>WatcherCombatHelper.ChangeStance&lt;T&gt;</c>。
/// </summary>
/// <remarks>
/// 姿态本身就是原版可见的 <see cref="PowerModel" />，所以把它按 Power 施加和清零之后，
/// 求解器的状态指纹、剪枝和去重都自动认得它，不需要额外的预测状态。这是整个适配层能成立的
/// 关键：<c>Wrath.ModifyDamageMultiplicative</c> 这类只读钩子求解器本来就会回落到 mod 自己的
/// 实现，所以姿态一旦在模拟里正确，伤害倍率就自动正确。
///
/// 逻辑写在 <see cref="WatcherSim" /> 上而不是卡牌上下文上，因为遗物泪滴挂坠、药水神赐甘露
/// 和多张卡牌进入的是同一个姿态，不该按上下文类型复制三份。
/// </remarks>
internal static class WatcherStanceVerbs
{
    public static void EnterWrath(WatcherSim sim) => ChangeStance<Wrath>(sim);

    public static void EnterCalm(WatcherSim sim) => ChangeStance<Calm>(sim);

    public static void EnterDivinity(WatcherSim sim) => ChangeStance<Divinity>(sim);

    public static void EnterForeseen(WatcherSim sim) => ChangeStance<Foreseen>(sim);

    public static void EnterWrath(CardOnPlayMirrorContext context) => EnterWrath(WatcherSim.From(context));

    public static void EnterCalm(CardOnPlayMirrorContext context) => EnterCalm(WatcherSim.From(context));

    public static void EnterDivinity(CardOnPlayMirrorContext context) => EnterDivinity(WatcherSim.From(context));

    public static void EnterForeseen(CardOnPlayMirrorContext context) => EnterForeseen(WatcherSim.From(context));

    public static void ExitStance(CardOnPlayMirrorContext context) => ExitStance(WatcherSim.From(context));

    public static void ExitStance(WatcherSim sim)
    {
        Type? current = CurrentStance(sim);
        if (current is null)
            return;
        ClearAllStances(sim);
        AfterStanceChanged(sim, current, newStance: null);
    }

    private static void ChangeStance<TStance>(WatcherSim sim)
        where TStance : PowerModel
    {
        // 入定锁：有这个 Power 时整个切换被吞掉，连退出当前姿态都不发生。
        if (sim.Combat.GetAmount<CannotChangeStancePower>(sim.Self) > 0)
            return;

        Type target = typeof(TStance);
        Type? current = CurrentStance(sim);
        if (current == target)
            return;

        ClearAllStances(sim);
        sim.Effects.ApplyPower(target, sim.Self, 1, sim.Self);

        // 进入神圣给 3 点能量，这是 Divinity.AfterApplied 的效果，不是切换本身的效果。
        if (target == typeof(Divinity))
            sim.Simulator.GainEnergy(sim.Owner, 3m);

        AfterStanceChanged(sim, current, target);
    }

    /// <summary>
    /// 顺序要紧：原版按 神圣、预知、愤怒、平静 逐个移除，平静的 +2 能量在新姿态生效之前就落地。
    /// </summary>
    private static void ClearAllStances(WatcherSim sim)
    {
        ClearStance<Divinity>(sim);
        ClearStance<Foreseen>(sim);
        ClearStance<Wrath>(sim);
        if (ClearStance<Calm>(sim))
            sim.Simulator.GainEnergy(sim.Owner, 2m);
    }

    private static bool ClearStance<TStance>(WatcherSim sim)
        where TStance : PowerModel
    {
        if (sim.Combat.GetPower<TStance>(sim.Self) is not { Amount: > 0 } power)
            return false;
        sim.Combat.SetPowerAmount(power, 0);
        return true;
    }

    /// <summary>凌波微步：进入愤怒时抽牌。这是观者能打出无限循环的那一条。</summary>
    /// <remarks>
    /// 原版分两种情况，判据是此刻出牌堆空不空：
    /// 出牌堆是空的，说明这次进入愤怒不是某张牌打出来的（比如沸腾之怒在回合开始时进愤怒），
    /// 当场就抽；出牌堆非空，说明正在结算一张牌，就把要抽的张数记在 Power 的
    /// <c>PendingDraws</c> 上，等那张牌离开出牌堆再抽。
    ///
    /// 这个先后不是细节，它决定无限循环成不成立。以爆发+加宁静那套为例：抽牌堆里只剩宁静时，
    /// 打出爆发+进入愤怒要抽 2 张。按原版的顺序，爆发先进弃牌堆，然后抽 2 张——抽到宁静，
    /// 抽牌堆空了就把弃牌堆洗回来，于是爆发又回到手里，循环成立。如果在结算当中就抽，
    /// 爆发还在出牌堆里，第二张什么也抽不到，循环断掉。
    ///
    /// 延后的那部分由 <see cref="WatcherRushdownPatch" /> 在一张牌结算完之后清空。
    /// </remarks>
    private static void RushdownDraw(WatcherSim sim)
    {
        if (sim.Combat.GetMutablePower<RushdownPower>(sim.Self) is not { Amount: > 0 } rushdown)
            return;

        if (sim.OwnerState.PlayPile.Cards.Count == 0)
            sim.Simulator.Draw(sim.Owner, rushdown.Amount);
        else
            rushdown.PendingDraws += rushdown.Amount;
    }

    private static Type? CurrentStance(WatcherSim sim)
    {
        if (sim.Combat.GetAmount<Divinity>(sim.Self) > 0)
            return typeof(Divinity);
        if (sim.Combat.GetAmount<Foreseen>(sim.Self) > 0)
            return typeof(Foreseen);
        if (sim.Combat.GetAmount<Wrath>(sim.Self) > 0)
            return typeof(Wrath);
        if (sim.Combat.GetAmount<Calm>(sim.Self) > 0)
            return typeof(Calm);
        return null;
    }

    /// <summary>对应 <c>WatcherCombatHelper.OnStanceChanged</c> 的六路扇出。</summary>
    /// <remarks>
    /// 紫色莲花和黄金眼这类遗物在原版里是按遗物 ID 字符串轮询的，不是钩子，所以必须在这里读，
    /// 否则光靠钩子镜像会漏掉。退出预知的悟命那一路仍记风险，它依赖天命消耗标记。
    /// </remarks>
    private static void AfterStanceChanged(WatcherSim sim, Type? oldStance, Type? newStance)
    {
        if (newStance == typeof(Wrath))
            RushdownDraw(sim);

        if (sim.Combat.GetAmount<MentalFortressPower>(sim.Self) is > 0 and var fortress
            && oldStance != newStance)
        {
            sim.Simulator.GainBlock(sim.Self, fortress, ValueProp.Unpowered, null, null);
        }

        if (oldStance == typeof(Calm)
            && sim.Owner.Relics.Any(relic => relic.Id.Entry == "VIOLET_LOTUS"))
        {
            sim.Simulator.GainEnergy(sim.Owner, 1m);
        }

        // 任何姿态变化都让弃牌堆里的疾风连击回手，包括退出姿态。
        PredictedCard[] flurries = sim.OwnerState.DiscardPile.Cards
            .Where(card => card.Preview is WatcherFlurryOfBlows)
            .ToArray();
        if (flurries.Length > 0)
            sim.Simulator.AddToPile(flurries, PileType.Hand);

        if (oldStance == typeof(Foreseen)
            && sim.Combat.GetPower<WatcherStatePower>(sim.Self)?.KnowFateConsumedThisTurn == true)
        {
            sim.Unmirrored("退出预知时的悟命");
        }
    }
}
