using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;

namespace SolverWatcherAdapter;

/// <summary>
/// 让求解器认得观者的额外回合。
/// </summary>
/// <remarks>
/// 求解器判断"这个玩家能不能再来一个回合"的地方是硬编码的：
/// <c>SimulatedCombatState.PrepareExtraPlayerTurn</c> 只看龙涎香和帕尔之眼两个来源，
/// 不去问各个 Power 的 <c>ShouldTakeExtraTurn</c>。所以观者的额外回合 Power 在模拟里
/// 是完全看不见的。
///
/// 不补的话，跳跃打出去就是花 3 点能量强制结束回合、什么也不换回来。求解器会因此永远
/// 不打它；更糟的是玩家自己打了以后，求解器的续算和实际对不上，会一直重算。
///
/// 消耗那一边同样要补，否则 Power 留着不掉，模拟里会一个回合接一个回合地无限额外回合。
/// </remarks>
internal static class WatcherExtraTurnPatch
{
    public static MethodInfo ResolvePrepareTarget()
        => AccessTools.Method(
               typeof(SimulatedCombatState),
               nameof(SimulatedCombatState.PrepareExtraPlayerTurn))
           ?? throw new MissingMethodException(
               nameof(SimulatedCombatState),
               nameof(SimulatedCombatState.PrepareExtraPlayerTurn));

    public static MethodInfo ResolveConsumeTarget()
        => AccessTools.Method(
               typeof(SimulatedCombatState),
               nameof(SimulatedCombatState.ConsumeExtraTurnSources))
           ?? throw new MissingMethodException(
               nameof(SimulatedCombatState),
               nameof(SimulatedCombatState.ConsumeExtraTurnSources));

    public static void PreparePostfix(
        SimulatedCombatState __instance,
        Player player,
        ref bool __result)
    {
        if (__instance.GetAmount<WatcherExtraTurnPower>(player.Creature) > 0)
            __result = true;
    }

    /// <summary>
    /// 额外回合用掉以后把 Power 去掉，对应原版 <c>AfterTakingExtraTurn</c> 里的自我移除。
    /// </summary>
    /// <remarks>
    /// 这里只动观者自己的 Power，不碰帕尔之眼的状态。原版有一条补丁专门让观者给的额外回合
    /// 不去消耗帕尔之眼，而求解器这个方法里帕尔之眼那一支的条件是"本回合一张牌都没手动打过"，
    /// 打了跳跃就不满足，所以两边的结果本来就一致，不需要额外处理。
    /// </remarks>
    public static void ConsumePostfix(SimulatedCombatState __instance, Player player)
    {
        if (__instance.GetMutablePower<WatcherExtraTurnPower>(player.Creature) is { Amount: > 0 } power)
            __instance.SetPowerAmount(power, 0);
    }
}
