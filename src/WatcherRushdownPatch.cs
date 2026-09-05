using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;

namespace SolverWatcherAdapter;

/// <summary>
/// 一张牌结算完、离开出牌堆之后，把凌波微步欠下的抽牌补上。
/// </summary>
/// <remarks>
/// 原版这一步挂在 <c>RushdownPower.AfterCardChangedPiles</c> 上，条件是这张牌的旧牌堆是出牌堆。
/// 求解器不分发 <c>AfterCardChangedPiles</c>，所以只能补在它把牌挪出出牌堆的那个位置后面。
///
/// <c>CombatPredictionSimulator.OnPlayWrapper</c> 是求解器结算一次出牌的整个过程：付费、结算、
/// 跑打出后钩子，最后按结果位置把牌从出牌堆挪到弃牌堆或消耗堆。补在这个方法后面，正好对应
/// "牌已经离开出牌堆"，和原版的触发点一致。
///
/// 顺序要紧的原因写在 <see cref="WatcherStanceVerbs" /> 里：抽牌必须发生在这张牌进弃牌堆之后，
/// 否则抽牌堆见底重洗时抽不到它，无限循环就断了。
///
/// 怒意也靠这个顺序。它在打出后钩子里把自己洗回抽牌堆，那一步在这里之前，所以它能被自己
/// 触发的凌波微步抽牌抽回来。
/// </remarks>
internal static class WatcherRushdownPatch
{
    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(CombatPredictionSimulator), "OnPlayWrapper")
           ?? throw new MissingMethodException(
               nameof(CombatPredictionSimulator),
               "OnPlayWrapper");

    public static void Postfix(CombatPredictionSimulator __instance, PredictedCard card)
    {
        if (card.Preview.Owner is not { } owner)
            return;
        DrainPendingDraws(__instance, owner);
    }

    private static void DrainPendingDraws(CombatPredictionSimulator simulator, Player owner)
    {
        SimulatedCombatState combat = (SimulatedCombatState)simulator.State.CombatState;
        if (combat.GetMutablePower<RushdownPower>(owner.Creature) is not { PendingDraws: > 0 } rushdown)
            return;

        int draws = rushdown.PendingDraws;
        rushdown.PendingDraws = 0;
        simulator.Draw(owner, draws);
    }
}
