using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 手牌保留时那些逐次增长的数值。
/// </summary>
/// <remarks>
/// 观者有三张牌每被保留一次数值就永久变好：洗炼的格挡、时之沙的费用、风车打击的伤害。
/// 这些写在 <c>WatcherRetainCompat.AfterCardRetained</c> 里，由 Harmony postfix 挂在原版的
/// 保留广播上——求解器从不调那个方法，所以既不生效也不记风险。
///
/// 这一条是实机报出来的：打旧日雕像时反复出现计划外重算，日志里第一处差异正是
/// <c>WATCHER_SANDS_OF_TIME</c> 的费用预测 4、实际 3。也就是说这个缺口不是理论上的，
/// 它会直接让求解器的续算作废、反复重新搜索。
///
/// 求解器把回合末的弃牌与保留写在 <c>CorePowerSupport.FlushPlayerHandAtTurnEnd</c> 里。
/// 取"被保留的牌"用的是前置补丁，在冲洗之前按 <c>ShouldRetainThisTurn</c> 记一份名单，
/// 和原版 <c>WatcherRetainCompat.AfterFlush</c> 的判据一致。
///
/// 不能改用"冲洗完还留在手上的牌"，那样有两处会错：
/// 一是求解器的冲洗前面有一道 <c>PersistentRelicSupport.ShouldFlush</c>，响铃三角这类
/// 效果会让整只手一张都不弃，那时留在手上的并不都是被保留的；
/// 二是同一个方法里紧接着会跑 <c>EndOfTurnCleanup</c>，冥想给的单回合保留在那之后就读不到了。
/// </remarks>
internal static class WatcherRetainPatch
{
    public static MethodInfo ResolveTarget()
        => AccessTools.Method(
               typeof(CorePowerSupport),
               nameof(CorePowerSupport.FlushPlayerHandAtTurnEnd))
           ?? throw new MissingMethodException(
               nameof(CorePowerSupport),
               nameof(CorePowerSupport.FlushPlayerHandAtTurnEnd));

    /// <summary>冲洗之前先记下哪些牌会被保留。</summary>
    public static void Prefix(
        CombatPredictionSimulator simulator,
        Player player,
        out PredictedCard[] __state)
    {
        __state = simulator.State.GetPlayerCombatState(player).Hand.Cards
            .Where(card => card.Preview.ShouldRetainThisTurn)
            .ToArray();
    }

    public static void Postfix(
        SimulatedCombatState combat,
        Player player,
        PredictedCard[] __state)
    {
        if (__state.Length == 0)
            return;

        foreach (PredictedCard card in __state)
            ApplyRetainGrowth(card);

        // 确立：被保留的牌本回合再降费，降幅等于层数。原版把它放在同一个保留流程里。
        if (combat.GetAmount<EstablishmentPower>(player.Creature) is > 0 and var establishment)
        {
            foreach (PredictedCard card in __state)
                card.MutablePreview.EnergyCost.AddThisCombat(-establishment, reduceOnly: true);
        }
    }

    /// <summary>
    /// 三张牌各自的增长。改的是这张牌实例的基础值，本场战斗内累积。
    /// </summary>
    /// <remarks>
    /// 原版还对一张叫 WatcherEvictGuest 的牌做同样的降费，但按类型名字符串匹配，而钉死的这一版
    /// 观者里没有这个类型，所以不实现——凭字符串去猜一个不存在的类型只会带来错觉。
    /// </remarks>
    private static void ApplyRetainGrowth(PredictedCard card)
    {
        switch (card.MutablePreview)
        {
            case WatcherPerseverance perseverance:
                perseverance.DynamicVars.Block.BaseValue +=
                    perseverance.DynamicVars["MagicNumber"].BaseValue;
                break;

            case WatcherSandsOfTime sandsOfTime:
                sandsOfTime.EnergyCost.AddThisCombat(-1, reduceOnly: true);
                break;

            case WatcherWindmillStrike windmillStrike:
                windmillStrike.DynamicVars.Damage.BaseValue +=
                    windmillStrike.DynamicVars["MagicNumber"].BaseValue;
                break;
        }
    }
}
