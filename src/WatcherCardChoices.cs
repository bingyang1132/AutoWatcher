using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 观者卡牌里"打出后当场让玩家选一个"的那几张。
/// </summary>
/// <remarks>
/// 走求解器的 <see cref="CardChoiceMirrors"/>：不登记的话
/// <c>CardChoiceSupport.GetSpec</c> 对第三方卡牌一律返回 <c>null</c>，也就是"这张牌没有选择"，
/// 于是牌照样打得出去、效果在模拟里静默变成空操作。卡牌自己的 OnPlay 镜像补不了这个，
/// 因为选择的展开发生在出牌路径上、不在效果镜像里。
///
/// 目前只有许愿一张。全知、外域影响、冥想的选项都是"从某个牌堆里挑一张牌"，
/// 要先把那几个牌堆的候选口径定下来，还没做。
/// </remarks>
internal static class WatcherCardChoices
{
    public static void RegisterAll()
    {
        CardChoiceMirrors.Register<WatcherWish_P>(WishSpec, WishApply);
    }

    /// <summary>
    /// 许愿的三个愿望，顺序和原版 <c>OnPlay</c> 里那个 options 列表一致：力量、镀甲、金币。
    /// </summary>
    /// <remarks>
    /// 三张选项牌会跟着本牌一起升级（原版对三张都调了 <c>UpgradeInternal</c>），这里必须照做：
    /// 部署时按 CardId 加升级等级在原生页面上定位，升级等级不对就找不到那个选项。
    /// 数值也从选项牌自己的 <c>MagicNumber</c> 上读，不写死 3/6/25 —— 那三个数是原版
    /// <c>OnPlay</c> 和选项牌的 <c>CanonicalVars</c> 各写了一遍，读牌上的那一份改了数值也不会失配。
    /// </remarks>
    private static List<PredictedCard> BuildOptions(Player owner, bool boosted)
    {
        List<PredictedCard> options =
        [
            PredictedCard.Create(CanonicalModels.Card<WatcherWishAlmighty>(), owner),
            PredictedCard.Create(CanonicalModels.Card<WatcherWishLiveForever>(), owner),
            PredictedCard.Create(CanonicalModels.Card<WatcherWishFameAndFortune>(), owner),
        ];
        if (boosted)
        {
            foreach (PredictedCard option in options)
                option.Upgrade();
        }
        return options;
    }

    private static CardChoiceSpec WishSpec(
        CombatPredictionSimulator simulator,
        PredictedCard playedCard,
        WatcherWish_P card)
    {
        // 这一版观者里 TryConsumeKnowFateBoost 是基类实现、恒为 false 且不消耗任何东西，
        // 所以 boosted 就是本牌的升级状态。以后观者加了会消耗天命的变体，这里要跟着改。
        List<PredictedCard> options = BuildOptions(card.Owner, card.IsUpgraded);
        return new CardChoiceSpec(
            PlanChoiceEffect.ModDefined,
            PileType.None,
            MinCount: 1,
            MaxCount: 1,
            Options: options,
            SourceCards: options,
            ReplacementValue: 0d);
    }

    private static bool WishApply(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        PredictedCard playedCard,
        WatcherWish_P card,
        PlanCardChoice choice)
    {
        if (choice.Cards.Count != 1)
        {
            throw new InvalidOperationException(
                $"许愿应当正好选一个愿望，实际 {choice.Cards.Count} 个。");
        }

        List<PredictedCard> options = BuildOptions(card.Owner, card.IsUpgraded);
        string chosenId = choice.Cards[0].CardId;
        int index = options.FindIndex(option => option.Preview.Id.Entry == chosenId);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"许愿的选择 {chosenId} 不是三个愿望之一。");
        }

        int amount = options[index].Preview.DynamicVars["MagicNumber"].IntValue;
        Creature self = card.Owner.Creature;
        switch (index)
        {
            case 0:
                combat.ApplyPower(typeof(StrengthPower), self, amount, self);
                break;
            case 1:
                combat.ApplyPower(typeof(WishPlatedArmorPower), self, amount, self);
                break;
            default:
                // 金币直接落在求解器现成的长期资源刻度上，和贪婪之手同一条路：面值直记。
                // 那把刻度在最终选择里排在所有掉血项之后，所以金币永远换不到血，只在打平时起作用。
                combat.GainPlayerGold(card.Owner, amount);
                combat.RecordLongTermResource(amount);
                break;
        }
        return !simulator.HasPendingChoice;
    }
}
