using MegaCrit.Sts2.Core.Entities.Cards;
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
    /// <summary>勤学精进斩杀收益折到求解器的长期资源刻度上的值，与原版猎杀同档。</summary>
    private const int LessonLearnedLongTermResourceValue = 30;

    public static int RegisterB(MethodMirrorRegistry<CardModel, CardOnPlayMirrorContext> r)
    {
        r.Register<WatcherFlyingSleeves>(FlyingSleeves);
        r.Register<WatcherFollowUp>(FollowUp);
        r.Register<WatcherForeignInfluence>(ForeignInfluence);
        r.Register<WatcherForesight>(Foresight);
        r.Register<WatcherHalt>(Halt);
        r.Register<WatcherHymnV2>(NoOpOnPlay);
        r.Register<WatcherIndignation>(Indignation);
        r.Register<WatcherInnerPeace>(InnerPeace);
        r.Register<WatcherInsight>(Insight);
        r.Register<WatcherIntentProxy>(NoOpOnPlay);
        r.Register<WatcherJudgment>(Judgment);
        r.Register<WatcherJustLucky>(JustLucky);
        r.Register<WatcherLessonLearned>(LessonLearned);
        r.Register<WatcherLikeWater>(LikeWater);
        r.Register<WatcherMasterReality>(MasterReality);
        r.Register<WatcherMeditate>(Meditate);
        r.Register<WatcherMentalFortress>(MentalFortress);
        r.Register<WatcherMockery>(MultiplayerOnly);
        r.Register<WatcherNirvana>(Nirvana);
        r.Register<WatcherOmega>(Omega);
        r.Register<WatcherOmniscience>(Omniscience);
        r.Register<WatcherPerseverance>(Perseverance);
        r.Register<WatcherPersuasion>(MultiplayerOnly);
        r.Register<WatcherPray>(Pray);
        r.Register<WatcherPreach>(Preach);
        r.Register<WatcherPressurePoints>(PressurePoints);
        r.Register<WatcherProstrate>(Prostrate);
        r.Register<WatcherProtect>(Protect);
        r.Register<WatcherRagnarok>(Ragnarok);
        r.Register<WatcherReachHeaven>(ReachHeaven);
        r.Register<WatcherRelinquish>(MultiplayerOnly);
        r.Register<WatcherRevelation_P>(NoOpOnPlay);
        return 32;
    }

    private static void FlyingSleeves(WatcherFlyingSleeves card, CardOnPlayMirrorContext context)
        => V.Attack(context, hits: 2);

    private static void FollowUp(WatcherFollowUp card, CardOnPlayMirrorContext context)
    {
        V.Attack(context);
        if (V.PreviousPlayedCardType(context) == CardType.Attack)
            V.GainEnergy(context, card.DynamicVars.Energy.BaseValue);
    }

    private static void ForeignInfluence(WatcherForeignInfluence card, CardOnPlayMirrorContext context)
        => V.PlayerChoice(context, $"{card.Id.Entry} 从三张其他角色的攻击牌里选一张加入手牌");

    private static void Foresight(WatcherForesight card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(ForesightPower), VarInt(card, "MagicNumber"));

    /// <summary>基础格挡，处于愤怒时再加一份。</summary>
    /// <remarks>
    /// MagicNumber 本身就是一个 BlockVar，走的是和基础格挡同一条管线，而且是两次独立的加格挡
    /// 调用，所以任何按次生效的格挡修正会算两遍。
    /// </remarks>
    private static void Halt(WatcherHalt card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        if (V.PowerAmount<Wrath>(context) > 0)
            V.BlockFor(context, Var(card, "MagicNumber"), ValueProp.Move);
    }

    private static void Indignation(WatcherIndignation card, CardOnPlayMirrorContext context)
    {
        // 严格二选一，永远不会两个都做。
        if (V.PowerAmount<Wrath>(context) > 0)
        {
            int amount = VarInt(card, "MagicNumber");
            foreach (var enemy in V.Combat(context).HittableEnemies.ToArray())
                V.Effects(context).ApplyPower(typeof(VulnerablePower), enemy, amount, V.Self(context));
            return;
        }
        S.EnterWrath(context);
    }

    private static void InnerPeace(WatcherInnerPeace card, CardOnPlayMirrorContext context)
    {
        if (V.PowerAmount<Calm>(context) > 0)
        {
            V.Draw(context, VarInt(card, "MagicNumber"));
            return;
        }
        S.EnterCalm(context);
    }

    private static void Insight(WatcherInsight card, CardOnPlayMirrorContext context)
        => V.Draw(context, VarInt(card, "MagicNumber"));

    /// <summary>目标当前生命不高于阈值时直接击杀。</summary>
    /// <remarks>比的是当前生命，格挡不参与；阈值取截断后的整数值。</remarks>
    private static void Judgment(WatcherJudgment card, CardOnPlayMirrorContext context)
    {
        if (context.CardPlay.Target is not { } target)
        {
            V.Unmirrored(context, $"{card.Id.Entry} 需要目标但 CardPlay 没有给");
            return;
        }
        if (V.CurrentHpOf(context, target) <= VarInt(card, "MagicNumber"))
            V.KillTarget(context);
    }

    /// <summary>顺序要紧：预视完整结算完，才轮到格挡和伤害。</summary>
    private static void JustLucky(WatcherJustLucky card, CardOnPlayMirrorContext context)
    {
        V.Scry(context, VarInt(card, "MagicNumber"));
        V.Block(context);
        V.Attack(context);
    }

    /// <summary>击杀时永久升级一张牌组里的牌。</summary>
    /// <remarks>
    /// 那是对主牌组的永久升级，不是战斗内升级，超出了单场战斗模拟的范围，所以只记风险。
    /// 击杀判定本身也依赖攻击前捕获的目标 Power 集合，这里一并归入未镜像。
    /// </remarks>
    /// <summary>勤学精进：斩杀时永久升级牌组里的一张随机牌。</summary>
    /// <remarks>
    /// 升级哪一张超出单场战斗的范围，也不影响本场，所以不模拟。但"斩杀了没有"影响的是求解器
    /// 该不该为了这张牌去凑最后一击，那是本场之内的决策，必须建模。按原版猎杀、饱食、贪婪之手
    /// 同一条路记：兑现时记一笔长期资源加一个 <c>FatalKillBonus</c> 目标。
    ///
    /// 它消耗，所以打出去就先记一笔"已经打出了"。不斩杀的话这张牌就白扔了，求解器要能把
    /// "从没抽到"和"当普通攻击打掉了"区分开。
    ///
    /// 折价 <see cref="LessonLearnedLongTermResourceValue" /> 取和猎杀一样的 30。牌组里随机一张
    /// 永久升级，和猎杀那笔一次性收益是同一个量级；随机落在打击防御上会缩水，但那是期望值内的事。
    ///
    /// 一处简化：原版在牌组里没有可升级的牌时什么都不给。牌组内容不在战斗状态里，取不到，
    /// 所以这里一律按给了算。整副牌全升满才会差，那时这张牌本来也没人留。
    /// </remarks>
    private static void LessonLearned(WatcherLessonLearned card, CardOnPlayMirrorContext context)
    {
        V.RecordFatalKillCardPlayed(context);
        int historyStart = context.History.Entries.Count;
        V.Attack(context);
        if (V.WasFatalKill(context, historyStart))
            V.RecordFatalKillBonus(context, LessonLearnedLongTermResourceValue);
    }

    private static void LikeWater(WatcherLikeWater card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(LikeWaterPower), VarInt(card, "MagicNumber"));

    /// <summary>数量是硬编码的 1，不随升级变化。</summary>
    private static void MasterReality(WatcherMasterReality card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(MasterRealityPower), 1);

    private static void Meditate(WatcherMeditate card, CardOnPlayMirrorContext context)
    {
        V.PlayerChoice(context, $"{card.Id.Entry} 从弃牌堆里选 {VarInt(card, "MagicNumber")} 张回手并保留");
        S.EnterCalm(context);
        if (context.CardPlay.IsLastInSeries)
            V.ForceEndTurn(context);
    }

    private static void MentalFortress(WatcherMentalFortress card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(MentalFortressPower), VarInt(card, "MagicNumber"));

    private static void Nirvana(WatcherNirvana card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(NirvanaPower), VarInt(card, "MagicNumber"));

    private static void Omega(WatcherOmega card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(OmegaPower), VarInt(card, "MagicNumber"));

    /// <summary>从抽牌堆选一张，加倍打出后放逐。</summary>
    /// <remarks>
    /// 选牌之外还有一层顺序依赖：加倍的 Power 必须在自动打出之前施加，否则那张牌不会结算两次。
    /// 整张牌都依赖玩家选择，所以整体记为未建模的选择。
    /// </remarks>
    private static void Omniscience(WatcherOmniscience card, CardOnPlayMirrorContext context)
        => V.PlayerChoice(context, $"{card.Id.Entry} 从抽牌堆选一张牌加倍打出");

    /// <summary>格挡。每次被保留时基础格挡会永久增长，那部分不在这里。</summary>
    /// <remarks>
    /// 增长发生在 WatcherRetainCompat.AfterCardRetained 里，改的是这张牌实例的 Block 基础值。
    /// 根状态克隆时已经带上了之前所有保留累积的结果，所以开局数值是对的；路线内部发生的保留
    /// 不会被算进去。这属于保留钩子的覆盖范围，不在卡牌镜像里补。
    /// </remarks>
    private static void Perseverance(WatcherPerseverance card, CardOnPlayMirrorContext context)
        => V.Block(context);

    private static void Pray(WatcherPray card, CardOnPlayMirrorContext context)
    {
        V.GainMantra(context, VarInt(card, "MagicNumber"));
        V.AddCards<WatcherInsight>(context, PileType.Draw, 1, CardPilePosition.Random);
    }

    /// <summary>三个数量都是硬编码字面量，不随升级变化。多人局那一路单人局不会走。</summary>
    private static void Preach(WatcherPreach card, CardOnPlayMirrorContext context)
    {
        V.Power(context, typeof(DevotionPower), 2);
        V.Power(context, typeof(GospelPower), 1);
    }

    /// <summary>先给目标叠标记，然后每个带标记的敌人按自己的标记层数吃一次穿透伤害。</summary>
    /// <remarks>
    /// 顺序要紧：标记先叠到本次目标上，所以那个目标这一跳就吃上了本次叠的层数。
    /// 那一跳伤害带 Unblockable 和 Unpowered，无视格挡也无视一切伤害修正，动词层里没有对应
    /// 形式，所以记风险。标记本身是叠对了的，缺的只是这一跳伤害。
    /// </remarks>
    private static void PressurePoints(WatcherPressurePoints card, CardOnPlayMirrorContext context)
    {
        V.PowerOnTarget(context, typeof(MarkPower), VarInt(card, "MagicNumber"));
        V.Unmirrored(context, $"{card.Id.Entry} 让每个带标记的敌人按层数吃一次无视格挡的伤害");
    }

    private static void Prostrate(WatcherProstrate card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        V.GainMantra(context, VarInt(card, "MagicNumber"));
    }

    private static void Protect(WatcherProtect card, CardOnPlayMirrorContext context)
        => V.Block(context);

    /// <summary>目标类型写的是全体，实际是逐跳重掷随机敌人。</summary>
    private static void Ragnarok(WatcherRagnarok card, CardOnPlayMirrorContext context)
        => V.AttackRandomEnemy(context, VarInt(card, "MagicNumber"));

    private static void ReachHeaven(WatcherReachHeaven card, CardOnPlayMirrorContext context)
    {
        V.Attack(context);
        V.AddCards<WatcherThroughViolence>(context, PileType.Draw, 1, CardPilePosition.Random);
    }
}
