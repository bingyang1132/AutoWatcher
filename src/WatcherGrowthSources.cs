using CombatSolver;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Models;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 观者那两处「收益落在这场战斗之外」的地方，登记成求解器的局外成长来源。
/// </summary>
/// <remarks>
/// <para>
/// 求解器默认只看本场战斗的血量与胜负，所以「多挨几点伤害换一次永久升级」一律判成亏。
/// 成长策略侧栏让玩家给每个来源单独填一份「每次收益允许的额外战损」，搜索据此在打分里记一笔
/// HP 信用额度。原版那八个来源写死在枚举里，第三方要走
/// <c>GrowthSourceMirrors</c> 登记。
/// </para>
/// <para>
/// 观者这两处：
/// <list type="bullet">
/// <item><b>勤学精进</b>：斩杀时永久升级牌组里的一张随机牌。和原版猎杀、狂宴、贪婪之手同一类，
/// 都是斩杀兑现，所以判据不要求 <c>DeckVersion</c>——升的是别的牌，这一张是不是局外牌组实例
/// 无所谓。</item>
/// <item><b>许愿三选一里的金币那一支</b>：和贪婪之手同一类。侧栏那一行的图标取金币愿望自己那张
/// 牌，标题写成「许愿·金币愿望名」，两截都是牌自己的官方译名；判据认的却是牌组里的许愿——
/// 愿望牌只在选择里存在，进不了牌组。</item>
/// </list>
/// </para>
/// <para>
/// 长期资源刻度和成长额度是两回事，两处都要记：<c>RecordLongTermResource</c> 记的是「这条线路
/// 带走了多少局外价值」，它在最终选择里排在所有掉血项之后，所以永远换不到血；成长额度记的是
/// 「玩家愿意为这次收益额外付多少血」。只记前者的话，求解器知道金币到手了，却不知道玩家愿意
/// 为它挨打。
/// </para>
/// <para>
/// 登记入口是可选的：绑不上就两个都留空，收益照旧只走长期资源刻度，行为和登记这个口子之前
/// 一样。见 <see cref="SolverCompat.RegisterGrowthSource" />。
/// </para>
/// </remarks>
internal static class WatcherGrowthSources
{
    /// <summary>勤学精进那一份额度的持久化键。改了等于换来源，玩家填的额度会失效。</summary>
    private const string LessonLearnedId = "AutoWatcher.LessonLearned";

    /// <summary>许愿金币那一份额度的持久化键。同上。</summary>
    private const string WishGoldId = "AutoWatcher.WishGold";

    private static Action<SimulatedCombatState>? _lessonLearned;
    private static Action<SimulatedCombatState>? _wishGold;

    public static void RegisterAll()
    {
        _lessonLearned = SolverCompat.RegisterGrowthSource(
            LessonLearnedId,
            static () => CanonicalModels.Card<WatcherLessonLearned>(),
            static card => card is WatcherLessonLearned);
        _wishGold = SolverCompat.RegisterGrowthSource(
            WishGoldId,
            static () => CanonicalModels.Card<WatcherWishFameAndFortune>(),
            static card => card is WatcherWish_P,
            static option => CanonicalModels.Card<WatcherWish_P>().Title + "·" + option.Title);
    }

    /// <summary>勤学精进斩杀兑现了一次永久升级。</summary>
    public static void RecordLessonLearned(SimulatedCombatState combat) => _lessonLearned?.Invoke(combat);

    /// <summary>许愿选了金币那一支。</summary>
    public static void RecordWishGold(SimulatedCombatState combat) => _wishGold?.Invoke(combat);
}
