using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 观者自己的 Power 重写了、而求解器确实会分发的那些钩子的镜像。
/// </summary>
/// <remarks>
/// 这一类和卡牌镜像是两件不同的事。求解器只镜像它自己管的那批钩子，未知类型走 Unsupported
/// 分支：不生效、但记一条风险。所以哪怕一个钩子的实际效果只是些计数器，不补镜像也会让每一条
/// 路线都带着未镜像标记，把真正值得看的红字淹掉。
///
/// 反过来说，那些走 Harmony postfix 的 <c>XxxCompat</c> 方法不属于这一类——求解器从不调用被
/// 打补丁的广播方法，所以它们既不生效也不记风险，只能逐个手写补。真言满 10 转神圣就是那一类，
/// 已经在 <see cref="WatcherVerbs.GainMantra" /> 里补上了。
/// </remarks>
internal static class WatcherHookMirrors
{
    public static void RegisterAll()
    {
        AfterCardPlayedMirrors.Registry.Register<WatcherStatePower>(StatePowerAfterCardPlayed);
        WatcherItemMirrors.RegisterAll();
        WatcherPowerMirrors.RegisterAll();
    }

    /// <summary>
    /// 对应 <c>WatcherStatePower.AfterCardPlayed</c>：更新本回合的出牌统计与天命观测值。
    /// </summary>
    /// <remarks>
    /// 这些计数器本身不改变任何结算，但有牌读它们，而且不补这个镜像的话每打一张牌都会记一条
    /// 未镜像风险。天命的判定按原版实现：先比对上一次观测值，低了就说明这张牌消耗过天命。
    ///
    /// 必须取可变克隆再赋值。计数器的 setter 会走 AssertMutable，对根实例赋值会抛异常。
    /// </remarks>
    private static void StatePowerAfterCardPlayed(
        WatcherStatePower power,
        AfterCardPlayedMirrorContext context)
    {
        if (power.Owner is not { } owner)
            return;
        if (!ReferenceEquals(context.CardPlay.Card.Owner, owner.Player))
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;
        if (combat.GetMutablePower<WatcherStatePower>(owner) is not { } state)
            return;

        CardModel played = context.CardPlay.Card;
        state.CardsPlayedThisTurn++;
        state.LastPlayedCardType = played.Type;
        if (played.Type == CardType.Attack)
            state.AttacksPlayedThisTurn++;

        if (played is IProphecyCard)
        {
            state.ProphecyPlaysThisCombat++;
            state.ProphecyPlaysThisTurn++;
            // 原版在这里还会结算预言牌的终局效果，那部分没有镜像。
            EngineDiagnostics.Warn(
                $"[AutoWatcher] 未镜像：{played.Id.Entry} 作为预言牌打出后的终局结算");
            context.History.RecordRisk(PredictionRiskReason.MethodMirrorIncomplete);
        }

        int knowFate = combat.GetAmount<KnowFatePower>(owner);
        if (knowFate < state.KnowFateLastObserved)
            state.KnowFateConsumedThisTurn = true;
        state.KnowFateLastObserved = knowFate;
        state.KnowFateConsumptionAttemptedThisCard = false;
    }
}
