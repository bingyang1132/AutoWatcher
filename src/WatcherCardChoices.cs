using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards;
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
/// 许愿的三个愿望是固定结果；冥想和通晓万物的候选是某个牌堆里的牌。他山之石不在这里——
/// 它的候选是随机生成的三张，走求解器现成的生成选项通道，见 WatcherCardMirrors 里的
/// ForeignInfluence。
/// </remarks>
internal static class WatcherCardChoices
{
    public static void RegisterAll()
    {
        CardChoiceMirrors.Register<WatcherWish_P>(WishSpec, WishApply);
        CardChoiceMirrors.Register<WatcherMeditate>(MeditateSpec, MeditateApply);
        CardChoiceMirrors.Register<WatcherOmniscience>(OmniscienceSpec, OmniscienceApply);
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
                // 长期资源刻度换不到血；玩家愿意为这笔金币挨多少打，走成长额度那一份。
                WatcherGrowthSources.RecordWishGold(combat);
                break;
        }
        return !simulator.HasPendingChoice;
    }

    // ---------- 冥想 ----------

    /// <summary>
    /// 从弃牌堆取 N 张放进手牌并保留，然后进平静、结束回合。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="PlanChoiceEffect.ModDefined"/> 而不是现成的 <c>MoveToHand</c>，是因为取回来的
    /// 牌要拿到**单回合保留**。原版的 <c>MoveToHand</c> 只搬牌，搬完没有第三方能接手的位置，而
    /// 保留在这里不是可选项：这张牌打完回合就结束，没有保留的话取回来的牌当场就被弃掉，
    /// 下回合手里是 0 张而不是 N 张。
    ///
    /// 进平静和结束回合在 OnPlay 镜像里（见 WatcherCardMirrors 的 Meditate）。顺序是对的：
    /// <c>RequestPlayerTurnEnd</c> 只打标记，搜索要等整张牌（含这次选择）结算完才推进回合。
    ///
    /// 张数照 <c>Dredge</c> 的口径按手牌上限截断。原版在放不进手牌时会走 <c>DeferRetainCard</c>，
    /// 那条路没有建模——截断之后走不到。
    /// </remarks>
    private static CardChoiceSpec MeditateSpec(
        CombatPredictionSimulator simulator,
        PredictedCard playedCard,
        WatcherMeditate card)
    {
        SimPlayerCombatState owner = simulator.State.GetPlayerCombatState(card.Owner);
        List<PredictedCard> options = owner.DiscardPile.Cards
            .Where(item => !ReferenceEquals(item.Original, playedCard.Original))
            .ToList();
        int wanted = Math.Min(
            card.DynamicVars["MagicNumber"].IntValue,
            simulator.GetMaxHandSize(card.Owner) - owner.Hand.Cards.Count);
        int count = Math.Clamp(wanted, 0, options.Count);
        return new CardChoiceSpec(
            PlanChoiceEffect.ModDefined,
            PileType.Discard,
            MinCount: count,
            MaxCount: count,
            Options: options,
            SourceCards: options,
            ReplacementValue: 0d);
    }

    private static bool MeditateApply(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        PredictedCard playedCard,
        WatcherMeditate card,
        PlanCardChoice choice)
    {
        List<PredictedCard> taken = ResolveFromPile(simulator, card.Owner, PileType.Discard, choice);
        if (taken.Count == 0)
            return !simulator.HasPendingChoice;
        simulator.AddToPile(taken, PileType.Hand, CardPilePosition.Bottom);
        foreach (PredictedCard item in taken)
            item.MutablePreview.GiveSingleTurnRetain();
        return !simulator.HasPendingChoice;
    }

    // ---------- 通晓万物 ----------

    /// <summary>
    /// 选抽牌堆里一张，打出两次，然后消耗它。
    /// </summary>
    /// <remarks>
    /// 求解器有 <c>PlanChoiceEffect.AutoPlayRepeated</c>，但它的结算第一行就是
    /// <c>if (source is not DecisionsDecisions) return true;</c>——又是一处按原版类型写死的开关，
    /// 而且候选也限定在手牌里的技能牌。所以这里走 <see cref="PlanChoiceEffect.ModDefined"/>
    /// 自己结算。
    ///
    /// "打出两次"不用自己循环：原版是先挂 <c>OmniscienceDoublePower</c> 再自动打出，那个 Power
    /// 的 <c>ModifyCardPlayCount</c> 加一次、之后自己移除，适配层已经镜像了它
    /// （<c>ModifyCardPlayCountMirrors.AfterRegistry</c>）。顺序照原版：先挂 Power 再打。
    ///
    /// 抽牌堆为空时原版直接返回，所以候选为空时下界给 0——否则搜索会永远等一个做不出的选择。
    ///
    /// 观者 0.9.28 在自动打出之前加了一道：把选中的牌先挪进出牌堆，再看它是不是被自己的
    /// 卡牌逻辑挡住（<c>UnplayableReason.BlockedByCardLogic</c>）。挡住就直接消耗掉，
    /// 既不挂 <c>OmniscienceDoublePower</c> 也不打出。这一步必须原样照做：不做的话，
    /// 求解器会把一次完整的双倍出牌算进路线，实机却只是把牌消耗了。
    /// </remarks>
    private static CardChoiceSpec OmniscienceSpec(
        CombatPredictionSimulator simulator,
        PredictedCard playedCard,
        WatcherOmniscience card)
    {
        SimPlayerCombatState owner = simulator.State.GetPlayerCombatState(card.Owner);
        List<PredictedCard> options = owner.DrawPile.Cards.ToList();
        return new CardChoiceSpec(
            PlanChoiceEffect.ModDefined,
            PileType.Draw,
            MinCount: options.Count == 0 ? 0 : 1,
            MaxCount: 1,
            Options: options,
            SourceCards: options,
            ReplacementValue: 0d);
    }

    private static bool OmniscienceApply(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        PredictedCard playedCard,
        WatcherOmniscience card,
        PlanCardChoice choice)
    {
        List<PredictedCard> chosen = ResolveFromPile(simulator, card.Owner, PileType.Draw, choice);
        if (chosen.Count == 0)
            return !simulator.HasPendingChoice;

        // 顺序照 0.9.28：先把选中的牌挪进出牌堆，再判它自己的可打出条件。反过来会算错——
        // 华丽终章那一类判据读的正是抽牌堆，牌还留在抽牌堆里的时候永远判成打不出。
        simulator.AddToPile(chosen[0], PileType.Play);
        if (simulator.HasPendingChoice)
            return false;

        // 被牌自己的逻辑挡住时原版直接消耗：不挂双倍，也不打出。
        if (!CardIsPlayableMirrors.Invoke(simulator, chosen[0]))
        {
            simulator.Exhaust(chosen[0]);
            return !simulator.HasPendingChoice;
        }

        combat.ApplyPower(
            typeof(OmniscienceDoublePower), card.Owner.Creature, 1, card.Owner.Creature);

        // 这张牌自己的结算里已经处理过的敌人死亡。拿不到就用新集合——那只会让死亡触发
        // 在同一次出牌里被重复处理一次，而通晓万物本身不造成伤害，走不到那种情况。
        ISet<uint> deaths = combat._activeCardExecutionDeaths ?? new HashSet<uint>();
        bool played = CardExecutionSupport.AutoPlay(
            simulator,
            combat,
            chosen[0],
            target: null,
            deaths,
            nestedChoiceSourceId: card.Id.Entry);
        if (simulator.HasPendingChoice)
            return false;

        // 原版：牌还在场上、而且不在消耗堆里，才消耗它。自动打出可能已经把它消耗掉了
        // （比如它自己带消耗），那种情况不能再消耗一次。
        if (played && chosen[0].GetPile(simulator.State)?.Type != PileType.Exhaust)
        {
            simulator.Exhaust(chosen[0]);
            if (simulator.HasPendingChoice)
                return false;
        }
        return true;
    }

    // ---------- 公用 ----------

    /// <summary>
    /// 把计划里的令牌在某个模拟牌堆里解析回牌。
    /// </summary>
    /// <remarks>
    /// <c>ModDefined</c> 的结算里求解器不替登记方做这一步（它连 <c>SourcePile</c> 都不查），
    /// 因为大多数登记方的选项是凭空造出来的令牌、不在任何牌堆里。候选确实来自牌堆时就得自己解析。
    /// 匹配用求解器自己的 <c>MatchesToken</c>，口径和它内部完全一致：CardId、升级等级、
    /// 同名牌的出现序号。
    /// </remarks>
    private static List<PredictedCard> ResolveFromPile(
        CombatPredictionSimulator simulator,
        Player owner,
        PileType pile,
        PlanCardChoice choice)
    {
        if (choice.Cards.Count == 0)
            return [];
        SimCardPile source = simulator.State.GetPlayerCombatState(owner).GetCardPile(pile)
            ?? throw new InvalidOperationException($"找不到模拟牌堆 {pile}。");
        List<PredictedCard> resolved = [];
        foreach (PlanCardToken token in choice.Cards)
        {
            PredictedCard? found = source.Cards
                .Where(item => CardChoiceSupport.MatchesToken(item, token))
                .Skip(token.SourceOccurrence)
                .FirstOrDefault();
            resolved.Add(found
                ?? throw new InvalidOperationException(
                    $"在{pile}里找不到计划选中的 {token.CardId}+{token.UpgradeLevel}"
                    + $"#{token.SourceOccurrence}。"));
        }
        return resolved;
    }
}
