using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnEnd;
using CombatSolver.Engine.InCombat.Mirrors.Potions.OnUse;
using WatcherMod;
using SV = SolverWatcherAdapter.WatcherSimVerbs;

namespace SolverWatcherAdapter;

/// <summary>
/// 观者的 9 个遗物和 3 个药水。
/// </summary>
/// <remarks>
/// 遗物只有三个需要注册镜像，因为观者的遗物分成三类，只有一类走求解器会分发的钩子：
///
/// 走求解器会分发的钩子（阳、香料、斗篷扣）——不注册的话每次触发都记一条风险。
///
/// 战斗开始前触发（清水、泪滴挂坠）——求解器的根快照是在战斗开始之后取的，那张奇迹已经在
/// 手上、平静已经进了，所以**不需要镜像**，补了反而会重复计算。
///
/// 按遗物 ID 轮询而不是钩子（金瞳、紫莲花）——它们自己什么都不重写，效果写在观者的辅助函数
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
        SV.Power(sim, typeof(YangDexterityPower), 1);
    }

    /// <summary>香料：自己洗牌后预视 3 张。</summary>
    /// <remarks>
    /// 实际张数还要过金瞳和守视的加减。预视本身可能因为抽牌堆为空而再触发一次洗牌，原版是
    /// 有可能递归的；这里的预视动词不重洗牌堆，所以不会递归。
    /// </remarks>
    private static void MelangeAfterShuffle(Melange relic, AfterShuffleMirrorContext context)
    {
        if (relic.Owner is not { } owner || !ReferenceEquals(context.Player, owner))
            return;
        WatcherSim sim = WatcherSim.From(
            context.CombatState, context.Simulator, context.State, context.History, owner);
        SV.Scry(sim, 3, relic.Id.Entry);
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
    /// 结果完全取决于玩家当场的选择，而且原版是按引用相等比较两张选项牌，没法从状态推出来。
    /// 所以整瓶记为未建模的选择，不猜一个姿态——猜错会让求解器按错误的伤害倍率排路线。
    /// </remarks>
    private static void StancePotionOnUse(StancePotion potion, PotionOnUseMirrorContext context)
    {
        if (potion.Owner is not { } owner)
            return;
        WatcherSim sim = WatcherSim.From(
            context.CombatState, context.Simulator, context.State, context.History, owner);
        sim.PlayerChoice($"{potion.Id.Entry} 在平静和愤怒之间选一个姿态");
    }
}
