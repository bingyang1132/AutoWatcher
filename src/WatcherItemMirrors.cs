using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnEnd;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Potions.OnUse;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Players;
using WatcherMod;
using S = AutoWatcher.WatcherStanceVerbs;
using SV = AutoWatcher.WatcherSimVerbs;

namespace AutoWatcher;

/// <summary>
/// 观者的 9 个遗物和 3 个药水。
/// </summary>
/// <remarks>
/// 遗物只有三个需要注册镜像，因为观者的遗物分成三类，只有一类走求解器会分发的钩子：
///
/// 走求解器会分发的钩子（阳、美琅脂、斗篷扣）——不注册的话每次触发都记一条风险。
///
/// 战斗开始前触发（清水、泪滴挂坠）——求解器的根快照是在战斗开始之后取的，那张奇迹已经在
/// 手上、平静已经进了，所以**不需要镜像**，补了反而会重复计算。
///
/// 按遗物 ID 轮询而不是钩子（黄金眼、紫色莲花）——它们自己什么都不重写，效果写在观者的辅助函数
/// 里。所以只能在对应动词里读，已分别在 <see cref="WatcherSimVerbs.EffectiveScryAmount" /> 和
/// 姿态切换的连带效果里实现。光靠钩子镜像会漏掉这两个。
///
/// 剩下达玛茹走回合初，而求解器的遗物回合初是一段硬编码 switch，没有注册点，见 RegisterAll。
/// </remarks>
internal static class WatcherItemMirrors
{
    public static void RegisterAll()
    {
        BeforeCardPlayedMirrors.Registry.Register<Yang>(YangBeforeCardPlayed);
        AfterShuffleMirrors.Registry.Register<Melange>(MelangeAfterShuffle);
        BeforeSideTurnEndMirrors.Registry.Register<CloakClasp_P>(CloakClaspBeforeSideTurnEnd);

        PotionOnUseMirrors.Registry.Register<Ambrosia>(AmbrosiaOnUse);
        PotionOnUseMirrors.Registry.Register<BottledMiracle>(BottledMiracleOnUse);
        PotionOnUseMirrors.Registry.Register<StancePotion>(StancePotionOnUse);
        // 姿态药水的两个结果由这条登记展开成搜索分支，效果在 StancePotionChoiceApply 里施加。
        PotionChoiceMirrors.Register<StancePotion>(StancePotionChoiceSpec, StancePotionChoiceApply);
    }

    // ---------- 遗物 ----------

    /// <summary>阳：自己打出攻击牌之前先获得 1 点临时敏捷。</summary>
    /// <remarks>
    /// 时机要紧：敏捷在这张牌结算之前就到位，所以同一张牌自己的格挡也吃这个加成。
    /// </remarks>
    private static void YangBeforeCardPlayed(Yang relic, BeforeCardPlayedMirrorContext context)
    {
        if (relic.Owner is not { } owner)
            return;
        CardModel played = context.CardPlay.Card;
        if (!ReferenceEquals(played.Owner, owner) || played.Type != CardType.Attack)
            return;
        WatcherSim sim = WatcherSim.From(
            context.CombatState, context.Simulator, context.State, context.History, owner);
        SV.TemporaryDexterity(sim, typeof(YangDexterityPower), 1);
    }

    /// <summary>美琅脂：自己洗牌后预见 3 张。</summary>
    /// <remarks>
    /// 实际张数还要过黄金眼和守视的加减。预见动词在抽牌堆为空时会自己洗一次牌（照
    /// <c>CardPileCmd.ShuffleIfNecessary</c>），那次洗牌会再触发这个钩子一遍——但只有一遍：
    /// 洗完抽牌堆就不是空的了，第二次预见不会再洗。
    /// </remarks>
    private static void MelangeAfterShuffle(Melange relic, AfterShuffleMirrorContext context)
    {
        if (relic.Owner is not { } owner || !ReferenceEquals(context.Player, owner))
            return;
        WatcherSim sim = WatcherSim.From(
            context.CombatState, context.Simulator, context.State, context.History, owner);
        SV.Scry(sim, 3, relic.Id.Entry, maxBranches: 1);
    }

    /// <summary>斗篷扣：回合结束前按手牌数获得格挡。</summary>
    /// <remarks>
    /// 格挡带 Unpowered，敏捷之类的修正不放大它。手牌数是在回合结束钩子触发的那一刻读的，
    /// 所以对同期的弃牌和保留效果的先后顺序敏感。
    /// </remarks>
    private static void CloakClaspBeforeSideTurnEnd(
        CloakClasp_P relic,
        BeforeSideTurnEndMirrorContext context)
    {
        if (relic.Owner is not { } owner || context.Side != owner.Creature.Side)
            return;
        WatcherSim sim = WatcherSim.From(
            context.CombatState, context.Simulator, context.State, context.History, owner);
        int handCount = sim.OwnerState.Hand.Cards.Count;
        if (handCount > 0)
            SV.BlockFor(sim, handCount, ValueProp.Unpowered);
    }

    // ---------- 药水 ----------

    private static void AmbrosiaOnUse(Ambrosia potion, PotionOnUseMirrorContext context)
    {
        if (potion.Owner is not { } owner)
            return;
        WatcherStanceVerbs.EnterDivinity(WatcherSim.From(
            context.CombatState, context.Simulator, context.State, context.History, owner));
    }

    /// <summary>瓶装奇迹：往手里加两张奇迹。</summary>
    /// <remarks>原版是两次独立的创建加入，所以手牌满的时候可能第一张进得去、第二张进不去。</remarks>
    private static void BottledMiracleOnUse(BottledMiracle potion, PotionOnUseMirrorContext context)
    {
        if (potion.Owner is not { } owner)
            return;
        WatcherSim sim = WatcherSim.From(
            context.CombatState, context.Simulator, context.State, context.History, owner);
        SV.AddCards<WatcherMiracle>(sim, PileType.Hand, 2);
    }

    /// <summary>姿态药水：在平静和愤怒之间二选一。</summary>
    /// <remarks>
    /// 效果不在这里施加。挑哪个姿态是一次真正的搜索分支，走
    /// <see cref="PotionChoiceMirrors"/>：候选由 <see cref="StancePotionChoiceSpec"/> 给出，
    /// 结果由 <see cref="StancePotionChoiceApply"/> 施加。求解器的出牌展开会先调这个 OnUse 镜像、
    /// 再调登记的 apply，所以这里保持空操作，不然姿态会被施加两次。
    ///
    /// 原版比的是引用相等（`val == calmChoice`），但两张选项牌是两个不同的类型、各只有一张，
    /// 所以按类型判完全等价。
    /// </remarks>
    private static void StancePotionOnUse(StancePotion potion, PotionOnUseMirrorContext context)
    {
    }

    /// <summary>形态药剂的两个候选：平静和愤怒，正好选一个。</summary>
    /// <remarks>
    /// 候选必须和原生页面上真正显示的那两张一致、顺序也一致，否则部署时按卡牌令牌在页面上定位
    /// 会错位。原版的 <c>OnUse</c> 是先建平静再建愤怒，这里照同一个顺序。
    ///
    /// 下界取 1 而不是 0。原生页面允许一张都不选（`Min=0`），但"用了药水又什么都不选"只是白扔
    /// 一瓶药，不值得多展开一条分支；要不要用这瓶药本来就是上一层的决定。
    /// </remarks>
    private static CardChoiceSpec StancePotionChoiceSpec(
        CombatPredictionSimulator simulator,
        StancePotion potion)
    {
        Player owner = potion.Owner;
        List<PredictedCard> options =
        [
            PredictedCard.Create(CanonicalModels.Card<WatcherStancePotionCalmChoice>(), owner),
            PredictedCard.Create(CanonicalModels.Card<WatcherStancePotionWrathChoice>(), owner),
        ];
        return new CardChoiceSpec(
            PlanChoiceEffect.ModDefined,
            PileType.None,
            1,
            1,
            options,
            options,
            ReplacementValue: 0d);
    }

    private static bool StancePotionChoiceApply(
        CombatPredictionSimulator simulator,
        StancePotion potion,
        PlanCardChoice choice)
    {
        if (choice.Cards.Count != 1)
        {
            throw new InvalidOperationException(
                $"形态药剂应当正好选一张，实际 {choice.Cards.Count} 张。");
        }
        WatcherSim sim = WatcherSim.From(
            simulator.State.CombatState,
            simulator,
            simulator.State,
            simulator.History,
            potion.Owner);
        // 按牌 ID 比，不写死字符串——ID 从规范实例上取，改了名也不会静默失配。
        if (choice.Cards[0].CardId == CanonicalModels.Card<WatcherStancePotionCalmChoice>().Id.Entry)
            S.EnterCalm(sim);
        else
            S.EnterWrath(sim);
        return !simulator.HasPendingChoice;
    }
}
