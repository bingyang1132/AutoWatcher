using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.Common.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;
using V = AutoWatcher.WatcherVerbs;
using S = AutoWatcher.WatcherStanceVerbs;

namespace AutoWatcher;

internal static partial class WatcherCardMirrors
{
    public static int RegisterC(MethodMirrorRegistry<CardModel, CardOnPlayMirrorContext> r)
    {
        r.Register<WatcherRushdown>(Rushdown);
        r.Register<WatcherSafety>(Safety);
        r.Register<WatcherSanctification>(MultiplayerOnly);
        r.Register<WatcherSanctity>(Sanctity);
        r.Register<WatcherSandsOfTime>(SandsOfTime);
        r.Register<WatcherSashWhip>(SashWhip);
        r.Register<WatcherScrawl_P>(Scrawl);
        r.Register<WatcherSerenity>(Serenity);
        r.Register<WatcherSignatureMove>(SignatureMove);
        r.Register<WatcherSimmeringFury>(SimmeringFury);
        r.Register<WatcherSmite>(Smite);
        r.Register<WatcherSpiritShield>(SpiritShield);
        r.Register<WatcherStudy>(Study);
        r.Register<WatcherSwivel>(Swivel);
        r.Register<WatcherTalkToTheHand>(TalkToTheHand);
        r.Register<WatcherTantrum>(Tantrum);
        r.Register<WatcherThirdEye>(ThirdEye);
        r.Register<WatcherThroughViolence>(ThroughViolence);
        r.Register<WatcherTranquility>(Tranquility);
        r.Register<WatcherVault>(Vault);
        r.Register<WatcherWallop>(Wallop);
        r.Register<WatcherWaveOfTheHand>(WaveOfTheHand);
        r.Register<WatcherWeave>(Weave);
        r.Register<WatcherWheelKick>(WheelKick);
        r.Register<WatcherWindmillStrike>(WindmillStrike);
        r.Register<WatcherWishAlmighty>(NoOpOnPlay);
        r.Register<WatcherWishFameAndFortune>(NoOpOnPlay);
        r.Register<WatcherWishLiveForever>(NoOpOnPlay);
        r.Register<WatcherWish_P>(Wish);
        r.Register<WatcherWorship>(Worship);
        r.Register<WatcherWreathOfFlame>(WreathOfFlame);
        // WatcherV2ChoiceTokenBase 是抽象基类，永远不会作为确切运行时类型出现，不注册。
        return 31;
    }

    private static void Rushdown(WatcherRushdown card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(RushdownPower), VarInt(card, "MagicNumber"));

    private static void Safety(WatcherSafety card, CardOnPlayMirrorContext context)
        => V.Block(context);

    private static void Sanctity(WatcherSanctity card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        // 条件在格挡结算之后才判断。
        if (V.PreviousPlayedCardType(context) == CardType.Skill)
            V.Draw(context, VarInt(card, "MagicNumber"));
    }

    /// <summary>只有伤害。每次被保留时费用降 1 的部分不在这里。</summary>
    /// <remarks>
    /// 降费发生在 WatcherRetainCompat.AfterCardRetained 里，改的是这张牌实例本场战斗的费用。
    /// 根状态克隆时已经带上了之前所有保留累积的结果；路线内部发生的保留不会被算进去。
    /// </remarks>
    private static void SandsOfTime(WatcherSandsOfTime card, CardOnPlayMirrorContext context)
        => V.Attack(context);

    private static void SashWhip(WatcherSashWhip card, CardOnPlayMirrorContext context)
    {
        V.Attack(context);
        if (V.PreviousPlayedCardType(context) == CardType.Attack)
            V.PowerOnTarget(context, typeof(WeakPower), VarInt(card, "MagicNumber"));
    }

    /// <summary>抽到手牌满 10 张。</summary>
    /// <remarks>结算时这张牌已经离开手牌，所以不计自己。</remarks>
    private static void Scrawl(WatcherScrawl_P card, CardOnPlayMirrorContext context)
        => V.Draw(context, 10 - V.HandCount(context));

    /// <summary>25 是源码里的硬编码字面量，不是牌上的变量，而且是设成确切数量不是叠加。</summary>
    private static void Serenity(WatcherSerenity card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        V.SetPower(context, typeof(RestfulPower), 25);
        S.EnterCalm(context);
    }

    private static void SignatureMove(WatcherSignatureMove card, CardOnPlayMirrorContext context)
        => V.Attack(context);

    private static void SimmeringFury(WatcherSimmeringFury card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(SimmeringFuryPower), VarInt(card, "MagicNumber"));

    private static void Smite(WatcherSmite card, CardOnPlayMirrorContext context)
        => V.Attack(context);

    /// <summary>格挡等于手牌数乘以倍率，手牌为空则什么都不给。</summary>
    private static void SpiritShield(WatcherSpiritShield card, CardOnPlayMirrorContext context)
    {
        decimal amount = V.HandCount(context) * Var(card, "MagicNumber");
        if (amount > 0)
            V.BlockFor(context, amount, ValueProp.Move);
    }

    private static void Study(WatcherStudy card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(StudyPower), 1);

    private static void Swivel(WatcherSwivel card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        V.Power(context, typeof(FreeAttackPower), 1);
    }

    /// <summary>攻击后无条件给目标挂反弹格挡，不检查目标是否还活着。</summary>
    private static void TalkToTheHand(WatcherTalkToTheHand card, CardOnPlayMirrorContext context)
    {
        V.Attack(context);
        V.PowerOnTarget(context, typeof(BlockReturnPower), VarInt(card, "MagicNumber"));
    }

    /// <summary>多段攻击后进入愤怒。伤害不随升级涨，只涨段数。</summary>
    /// <remarks>
    /// 打出后把自己随机洗回抽牌堆的行为在 AfterCardPlayed 里，不在 OnPlay，所以不属于这个
    /// 镜像的范围；那需要单独覆盖打出后钩子。
    /// </remarks>
    private static void Tantrum(WatcherTantrum card, CardOnPlayMirrorContext context)
    {
        V.Attack(context, VarInt(card, "MagicNumber"));
        S.EnterWrath(context);
    }

    private static void ThirdEye(WatcherThirdEye card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        V.Scry(context, VarInt(card, "MagicNumber"));
    }

    private static void ThroughViolence(WatcherThroughViolence card, CardOnPlayMirrorContext context)
        => V.Attack(context);

    private static void Tranquility(WatcherTranquility card, CardOnPlayMirrorContext context)
        => S.EnterCalm(context);

    private static void Vault(WatcherVault card, CardOnPlayMirrorContext context)
        => V.TakeExtraTurn(context);

    /// <summary>格挡等于目标实际掉的血，不是造成的伤害。</summary>
    /// <remarks>
    /// 被目标格挡吸收掉的那部分不算。给的格挡带 Unpowered，所以敏捷之类的修正不放大它。
    /// </remarks>
    private static void Wallop(WatcherWallop card, CardOnPlayMirrorContext context)
    {
        if (context.CardPlay.Target is not { } target)
        {
            V.Unmirrored(context, $"{card.Id.Entry} 需要目标但 CardPlay 没有给");
            return;
        }
        int hpBefore = V.CurrentHpOf(context, target);
        V.Attack(context);
        int hpLost = hpBefore - V.CurrentHpOf(context, target);
        if (hpLost > 0)
            V.BlockFor(context, hpLost, ValueProp.Unpowered | ValueProp.Move);
    }

    private static void WaveOfTheHand(WatcherWaveOfTheHand card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(WaveOfTheHandPower), VarInt(card, "MagicNumber"));

    private static void Weave(WatcherWeave card, CardOnPlayMirrorContext context)
        => V.Attack(context);

    private static void WheelKick(WatcherWheelKick card, CardOnPlayMirrorContext context)
    {
        V.Attack(context);
        V.Draw(context, VarInt(card, "MagicNumber"));
    }

    /// <summary>只有伤害。每次被保留时基础伤害永久增长的部分不在这里。</summary>
    /// <remarks>
    /// 增长在 WatcherRetainCompat.AfterCardRetained 里改这张牌实例的伤害基础值。根状态克隆
    /// 时已经带上之前累积的结果；路线内部发生的保留不会被算进去，那部分要靠覆盖保留钩子来补。
    /// </remarks>
    private static void WindmillStrike(WatcherWindmillStrike card, CardOnPlayMirrorContext context)
        => V.Attack(context);

    /// <summary>
    /// 三选一。整张牌的效果都在选择里，登记在 <see cref="WatcherCardChoices"/>，这里必须是空的。
    /// </summary>
    /// <remarks>
    /// 求解器出牌时会自己调 <c>ResolveManualCardChoice</c> 去查选择规格，不需要 OnPlay 镜像
    /// 主动请求。在这里再施加一次效果就会算两遍。
    ///
    /// 这一版观者里 <c>TryConsumeKnowFateBoost</c> 是基类实现、恒为 <c>false</c> 且不消耗任何
    /// 东西，所以没有"即使没选也已经消耗了天命"这回事；以后观者加了会消耗天命的变体，
    /// 那一层顺序依赖才需要建模。
    /// </remarks>
    private static void Wish(WatcherWish_P card, CardOnPlayMirrorContext context)
    {
    }

    private static void Worship(WatcherWorship card, CardOnPlayMirrorContext context)
        => V.GainMantra(context, VarInt(card, "MagicNumber"));

    private static void WreathOfFlame(WatcherWreathOfFlame card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(VigorPower), VarInt(card, "MagicNumber"));
}
