using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver.Engine.InCombat.Simulation;

namespace SolverWatcherAdapter;

/// <summary>
/// 从任意一种镜像上下文里提取出动词层需要的那几样东西。
/// </summary>
/// <remarks>
/// 求解器为每一类钩子都定义了自己的上下文类型（卡牌打出、遗物洗牌、药水使用……），彼此没有
/// 公共基类能给到"主人是谁"。但观者的效果动词——尤其是姿态切换——在这几类之间是同一套逻辑：
/// 遗物泪滴挂坠、药水神赐甘露和卡牌宁静进入的是同一个平静。与其按上下文类型复制三份，不如
/// 把动词写在这一层上，各类上下文各写一个转换。
/// </remarks>
internal readonly struct WatcherSim
{
    public WatcherSim(
        SimulatedCombatState combat,
        CombatPredictionSimulator simulator,
        CombatPredictionState state,
        CombatPredictionHistory history,
        Player owner)
    {
        Combat = combat;
        Simulator = simulator;
        State = state;
        History = history;
        Owner = owner;
    }

    public SimulatedCombatState Combat { get; }

    public CombatPredictionSimulator Simulator { get; }

    public CombatPredictionState State { get; }

    public CombatPredictionHistory History { get; }

    public Player Owner { get; }

    public Creature Self => Owner.Creature;

    public ICombatPredictionEffectSink Effects => (ICombatPredictionEffectSink)Combat;

    public SimPlayerCombatState OwnerState => State.GetPlayerCombatState(Owner);

    public static WatcherSim From(CardOnPlayMirrorContext context)
        => From(context.CombatState, context.Simulator, context.State, context.History, context.PreviewCard.Owner);

    /// <summary>
    /// 从任意求解器镜像上下文的公共部分构造。遗物和药水的主人要由调用方给出，因为各类上下文
    /// 没有统一的取主人方式。
    /// </summary>
    public static WatcherSim From(
        ICombatState combatState,
        CombatPredictionSimulator simulator,
        CombatPredictionState state,
        CombatPredictionHistory history,
        Player owner)
    {
        if (combatState is not SimulatedCombatState combat)
            throw new InvalidOperationException("观者镜像需要可写的模拟战斗状态。");
        return new WatcherSim(combat, simulator, state, history, owner);
    }

    /// <summary>声明这一处效果没有镜像。求解器会显示成红色的未镜像，而不是静默算错。</summary>
    public void Unmirrored(string what)
    {
        EngineDiagnostics.Warn($"[SolverWatcherAdapter] 未镜像：{what}");
        History.RecordRisk(PredictionRiskReason.MethodMirrorIncomplete);
    }

    /// <summary>声明这一处需要玩家在结算中做选择，求解器的搜索还没有为它开分支。</summary>
    public void PlayerChoice(string what)
    {
        EngineDiagnostics.Warn($"[SolverWatcherAdapter] 未建模的结算内选择：{what}");
        History.RecordRisk(PredictionRiskReason.UnresolvedPlayerChoice);
    }
}
