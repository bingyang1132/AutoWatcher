using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;
using SV = AutoWatcher.WatcherSimVerbs;

namespace AutoWatcher;

/// <summary>
/// 发牌之前的那一步：先见之明每回合开始预见。
/// </summary>
/// <remarks>
/// 观者用 Harmony postfix 挂在原版 <c>BeforeHandDraw</c> 上，遍历钩子监听者，对
/// <c>ForesightPower</c> 调 <c>WatcherCombatHelper.Scry</c>。求解器这一步是
/// <c>TurnStartPowerSupport.TriggerBeforeHandDraw</c> 里一段按类型写死的流程，没有注册表，
/// 漏了**连风险都不记**。
///
/// 后果不是算错，是卡住：预见要弹选牌页面，而路线里根本没有这一步。实机走到回合开始就停在
/// 那个页面上等人选，自动化接不下去，只能玩家自己手操。玩家报的「开了先见之明就卡死」就是这个。
///
/// 所以这里两件事都要做对：
/// <list type="number">
/// <item>预见要走**回合开始的选牌通道**（<c>TurnStartChoiceSupport.ResolvePileDiscard</c>），
/// 不能走打牌那条动作选择通道 —— 后者只在出牌的上下文里被消费。</item>
/// <item>挂起了选择就要把 <c>__result</c> 写成 <c>true</c>。这个方法的 <c>bool</c> 是
/// 「有没有产生待处理选择」，调用方拿它当搜索边界；不回写的话后面的遗物、噩梦结算会带着
/// 一个未决选择继续跑。</item>
/// </list>
///
/// 预见的前几步（必要时洗牌、黄金眼与守视的增减、涅槃的格挡、弃牌堆里的迂回回手）和打出
/// 预见牌那条共用 <c>WatcherSimVerbs</c> 里的同一段，不在这里重写。
/// </remarks>
internal static class WatcherHandDrawPatch
{
    public static MethodInfo ResolveTarget()
        => AccessTools.Method(
               typeof(TurnStartPowerSupport),
               nameof(TurnStartPowerSupport.TriggerBeforeHandDraw))
           ?? throw new MissingMethodException(
               nameof(TurnStartPowerSupport),
               nameof(TurnStartPowerSupport.TriggerBeforeHandDraw));

    public static void Postfix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Player player,
        TurnStartChoiceCursor choices,
        ref bool __result)
    {
        if (__result || combat.HasPendingChoice)
            return;
        if (!simulator.State.GetCreature(player.Creature).IsAlive)
            return;

        WatcherSim sim = new(combat, simulator, simulator.State, simulator.History, player);
        int amount = SV.PowerAmount<ForesightPower>(sim);
        if (amount <= 0)
            return;

        if (!SV.ScryAtTurnStart(sim, amount, choices, nameof(ForesightPower)))
            __result = true;
    }
}
