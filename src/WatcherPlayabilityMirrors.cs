using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 观者里带"什么时候才打得出"条件的牌。
/// </summary>
/// <remarks>
/// 求解器对没有登记的牌会按 <c>CardModel.IsPlayable</c> 的基类实现算，也就是恒真。对不重写这个
/// 属性的牌那是准确的；重写了的必须登记，否则求解器会把一张当时打不出的牌排进路线，一直到
/// 部署那一步才报 <c>card_unplayable</c>，而这时前面几个动作已经打出去了，只能从更差的局面重算。
///
/// 判据必须读模拟里的牌堆。原版那个 getter 读的是主人的实时手牌，而模拟用的是预览克隆，
/// 走原版实现拿到的是搜索开始那一刻的真实手牌，甚至根本取不到主人。
///
/// 钉死的这一版观者只有 <see cref="WatcherSignatureMove" /> 一张需要登记。另一处重写在
/// <c>WatcherV2ChoiceTokenBase</c>（姿态药水的两张选择令牌），那是写死的 <c>false</c>，
/// 不读任何状态，基类回落本来就对，不用登记。
/// </remarks>
internal static class WatcherPlayabilityMirrors
{
    public static void Register()
    {
        CardIsPlayableMirrors.Registry.Register<WatcherSignatureMove>(SignatureMove);
    }

    /// <summary>标志性一击：手牌里的攻击牌不超过一张才打得出。</summary>
    /// <remarks>
    /// 原版数的是整只手牌里 <see cref="CardType.Attack" /> 的张数，包含它自己，上限为一。
    /// 也就是说它必须是手上唯一的攻击牌。
    /// </remarks>
    private static bool SignatureMove(WatcherSignatureMove card, CardIsPlayableMirrorContext context)
    {
        if (card.Owner is null)
            return true;
        return context.State.GetPlayerCombatState(card.Owner).Hand.Cards
            .Count(handCard => handCard.Preview.Type == CardType.Attack) <= 1;
    }
}
