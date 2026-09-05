using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Block;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Damage;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnEnd;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;
using SV = SolverWatcherAdapter.WatcherSimVerbs;

namespace SolverWatcherAdapter;

/// <summary>
/// 观者的 Power 重写了、而求解器确实会分发的那些钩子的镜像。
/// </summary>
/// <remarks>
/// 只列 <c>After*</c> 和 <c>Before*</c>。<c>Modify*</c> / <c>Should*</c> 那些求解器会回落到
/// mod 自己的实现，本来就正确，一个都不用写——观者有 7 个这类重写，包括姿态的伤害倍率。
///
/// 不补这些的后果不是算错，是每次触发都记一条风险，把真正值得看的红字淹掉；而效果本身会被
/// 当成空操作，所以确实也算错。
/// </remarks>
internal static class WatcherPowerMirrors
{
    public static void RegisterAll()
    {
        AfterBlockGainedMirrors.Registry.Register<WaveOfTheHandPower>(WaveOfTheHand);
        AfterCardDrawnMirrors.Registry.Register<WatcherDeusExMachina>(DeusExMachina);
        AfterCardGeneratedForCombatMirrors.Registry.Register<MasterRealityPower>(MasterReality);
        AfterCardPlayedMirrors.Registry.Register<BlessProphecyDamagePower>(BlessProphecyDamage);
        AfterCardPlayedMirrors.Registry.Register<WatcherTantrum>(Tantrum);
        AfterDamageGivenMirrors.Registry.Register<BlockReturnPower>(BlockReturn);
        AfterDamageGivenMirrors.Registry.Register<ColdObservationPower>(ColdObservation);
        AfterDamageGivenMirrors.Registry.Register<DivineDoomPower>(DivineDoom);
        AfterDamageGivenMirrors.Registry.Register<GospelPower>(Gospel);
        AfterDamageReceivedMirrors.Registry.Register<DeepThoughtSleepPower>(DeepThoughtSleep);
        AfterDamageReceivedMirrors.Registry.Register<WishPlatedArmorPower>(WishPlatedArmorDecay);
        ModifyCardPlayCountMirrors.AfterRegistry.Register<OmniscienceDoublePower>(OmniscienceDouble);
        BeforeSideTurnEndMirrors.Registry.Register<LikeWaterPower>(LikeWater);
        BeforeSideTurnEndMirrors.Registry.Register<WishPlatedArmorPower>(WishPlatedArmorBlock);
    }

    /// <summary>
    /// 各类钩子上下文都继承自 <c>CombatMirrorContext&lt;TBase&gt;</c>，但泛型参数不同
    /// （卡牌类是 CardModel，其余是 AbstractModel），所以这里也要开泛型才接得住两边。
    /// </summary>
    private static WatcherSim Sim<TBase>(CombatMirrorContext<TBase> context, Player owner)
        where TBase : AbstractModel
        => WatcherSim.From(context.CombatState, context.Simulator, context.State, context.History, owner);

    /// <summary>挥手：自己每次获得格挡都给所有可命中敌人上等于层数的虚弱。</summary>
    /// <remarks>没有每回合一次的限制，也不自减，所以一回合内多次加格挡就多次触发。</remarks>
    private static void WaveOfTheHand(WaveOfTheHandPower power, AfterBlockGainedMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner })
            return;
        if (!ReferenceEquals(context.Creature, power.Owner) || context.Amount <= 0m)
            return;
        WatcherSim sim = Sim(context, owner);
        foreach (Creature enemy in sim.Combat.HittableEnemies.ToArray())
            sim.Effects.ApplyPower(typeof(WeakPower), enemy, power.Amount, sim.Self);
    }

    /// <summary>天降神机：被抽到时自动打出自己，进放逐堆后给若干张奇迹。</summary>
    /// <remarks>
    /// 自动打出自己这一步没法用镜像表达——它要走完整的出牌管线并观察落到哪个牌堆。整张牌的
    /// 效果都挂在这上面，所以整体记风险。
    /// </remarks>
    private static void DeusExMachina(WatcherDeusExMachina card, AfterCardDrawnMirrorContext context)
    {
        if (!ReferenceEquals(context.InitialCard, card) || card.Owner is not { } owner)
            return;
        Sim(context, owner).Unmirrored($"{card.Id.Entry} 被抽到时会自动打出自己并生成奇迹");
    }

    /// <summary>主宰现实：战斗中生成的牌自动升级。</summary>
    private static void MasterReality(
        MasterRealityPower power,
        AfterCardGeneratedForCombatMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner })
            return;
        if (!ReferenceEquals(context.Card.Preview.Owner?.Creature, power.Owner))
            return;
        if (!context.Card.Preview.IsUpgradable)
            return;
        context.Simulator.Upgrade(context.Card);
        _ = owner;
    }

    /// <summary>祈祷之赐：一张预言攻击牌打完之后整块消失。</summary>
    /// <remarks>伤害加成本身由 ModifyDamageAdditive 提供，那条求解器会回落到原实现，不用镜像。</remarks>
    private static void BlessProphecyDamage(
        BlessProphecyDamagePower power,
        AfterCardPlayedMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner })
            return;
        CardModel played = context.CardPlay.Card;
        if (!ReferenceEquals(played.Owner, owner) || played.Type != CardType.Attack || played is not IProphecyCard)
            return;
        WatcherSim sim = Sim(context, owner);
        if (sim.Combat.GetPower<BlessProphecyDamagePower>(sim.Self) is { Amount: > 0 } live)
            sim.Combat.SetPowerAmount(live, 0);
    }

    /// <summary>怒意：打出后把自己随机洗回抽牌堆。</summary>
    /// <remarks>
    /// 原版的三个前置条件照抄：打出的就是它自己、此刻还在出牌堆、不是复制出来的那一份。
    ///
    /// 之前担心在这里挪牌堆会和求解器自己的"结果位置"流程打架，实际不会：求解器挪牌之前先判
    /// <c>card.GetPile(State)?.Type is PileType.Play</c>，这里已经把它挪走了，那一步就跳过。
    /// 判据也正好是同一个，所以两边不会各挪一次。
    ///
    /// 这一步得实现，因为它让怒意变成一个可以自己续上的循环——配合凌波微步的进入愤怒抽牌，
    /// 打出去的怒意会被自己触发的抽牌抽回来。不建模的话求解器根本看不见这条线。
    /// </remarks>
    private static void Tantrum(WatcherTantrum card, AfterCardPlayedMirrorContext context)
    {
        if (card.Owner is null || !ReferenceEquals(context.CardPlay.Card, card))
            return;
        if (context.Card.GetPile(context.State)?.Type is not PileType.Play)
            return;
        if (card.IsDupe)
            return;
        context.Simulator.AddToPile(context.Card, PileType.Draw, CardPilePosition.Random);
    }

    /// <summary>格挡反弹：挂在敌人身上，攻击它的人反而获得格挡。</summary>
    /// <remarks>
    /// 用的是总伤害而不是未格挡伤害，所以完全被挡下的一击也会触发。受益者按 玩家、宠物主人、
    /// 施加者 三级回退取。格挡带 Unpowered，敏捷不放大。
    /// </remarks>
    private static void BlockReturn(BlockReturnPower power, AfterDamageGivenMirrorContext context)
    {
        if (power.Owner is null || !ReferenceEquals(context.Target, power.Owner))
            return;
        if (context.Dealer is not { } dealer || ReferenceEquals(dealer, power.Owner))
            return;
        if (context.Source?.Preview.Type != CardType.Attack)
            return;
        if (!IsPoweredMove(context.Props) || context.Result.TotalDamage <= 0)
            return;
        Player? beneficiary = dealer.Player ?? dealer.PetOwner ?? power.Applier?.Player;
        if (beneficiary is null)
            return;
        Sim(context, beneficiary).Simulator
            .GainBlock(beneficiary.Creature, power.Amount, ValueProp.Unpowered, null, null);
    }

    /// <summary>冷眼旁观：自己造成伤害时按总伤害获得格挡，每张牌只扣一层。</summary>
    /// <remarks>
    /// 原版用一个私有字段记住上一张处理过的牌来实现"每张牌只扣一层"。那个字段不在状态指纹里，
    /// 也不会随分支克隆得对，所以这里按每次触发都扣一层建模，并记一条风险说明这一点：多段
    /// 攻击下会比实际扣得快。格挡本身是对的。
    /// </remarks>
    private static void ColdObservation(
        ColdObservationPower power,
        AfterDamageGivenMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner })
            return;
        if (!ReferenceEquals(context.Dealer, power.Owner))
            return;
        if (!IsPoweredMove(context.Props) || context.Result.TotalDamage <= 0)
            return;
        WatcherSim sim = Sim(context, owner);
        SV.BlockFor(sim, context.Result.TotalDamage, ValueProp.Unpowered);
        if (sim.Combat.GetPower<ColdObservationPower>(sim.Self) is { Amount: > 0 } live)
            sim.Combat.SetPowerAmount(live, live.Amount - 1);
        sim.Unmirrored($"{power.Id.Entry} 的每张牌只扣一层是靠 Power 上的私有字段记的");
    }

    /// <summary>神威天罚：在神圣姿态下造成未格挡伤害时，给所有敌人上末日。</summary>
    /// <remarks>
    /// 原版每回合只触发一次，重置在 AfterPlayerTurnStartEarly 里，而那个钩子求解器不分发，
    /// 所以这里没法复现"每回合一次"。按每次触发都生效建模会高估，所以改成记风险不生效——
    /// 高估路线比少算一个效果危险。
    /// </remarks>
    private static void DivineDoom(DivineDoomPower power, AfterDamageGivenMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner })
            return;
        if (!ReferenceEquals(context.Dealer, power.Owner))
            return;
        if (!IsPoweredMove(context.Props) || context.Result.UnblockedDamage <= 0)
            return;
        WatcherSim sim = Sim(context, owner);
        if (SV.PowerAmount<Divinity>(sim) <= 0)
            return;
        sim.Unmirrored($"{power.Id.Entry} 的每回合一次限制依赖求解器不分发的回合初钩子");
    }

    /// <summary>福音：自己造成未格挡伤害时获得等于层数的真言。</summary>
    /// <remarks>
    /// 走真言动词，所以满 10 转神圣也一并正确。多段攻击每一段都触发，没有每回合上限。
    /// 原版另有一半是多人局专属，单人局不会走。
    /// </remarks>
    private static void Gospel(GospelPower power, AfterDamageGivenMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner })
            return;
        if (!ReferenceEquals(context.Dealer, power.Owner))
            return;
        if (!IsPoweredMove(context.Props) || context.Result.UnblockedDamage <= 0)
            return;
        SV.GainMantra(Sim(context, owner), power.Amount);
    }

    /// <summary>深思沉眠：这一步只是记录格挡有没有被打破，真正的效果在下回合开始。</summary>
    /// <remarks>
    /// payoff 挂在走 Harmony postfix 的 AfterSideTurnStartCompat 上，求解器从不调那个方法。
    /// 只镜像这一半没有任何可观察效果，所以整体记风险。
    /// </remarks>
    private static void DeepThoughtSleep(
        DeepThoughtSleepPower power,
        AfterDamageReceivedMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner })
            return;
        if (!ReferenceEquals(context.Target, power.Owner))
            return;
        Sim(context, owner).Unmirrored($"{power.Id.Entry} 的效果在下回合开始时结算");
    }

    /// <summary>心愿镀甲：受到未格挡伤害时减一层。</summary>
    private static void WishPlatedArmorDecay(
        WishPlatedArmorPower power,
        AfterDamageReceivedMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner })
            return;
        if (!ReferenceEquals(context.Target, power.Owner) || context.Result.UnblockedDamage <= 0)
            return;
        WatcherSim sim = Sim(context, owner);
        if (sim.Combat.GetPower<WishPlatedArmorPower>(sim.Self) is { Amount: > 0 } live)
            sim.Combat.SetPowerAmount(live, live.Amount - 1);
    }

    /// <summary>全知加倍：任何一次打牌次数结算之后就整块消失。</summary>
    /// <remarks>原版这里没有任何条件判断，连是不是自己修改过的牌都不看，所以照抄无条件移除。</remarks>
    private static void OmniscienceDouble(
        OmniscienceDoublePower power,
        AfterModifyingCardPlayCountMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner })
            return;
        WatcherSim sim = Sim(context, owner);
        if (sim.Combat.GetPower<OmniscienceDoublePower>(sim.Self) is { Amount: > 0 } live)
            sim.Combat.SetPowerAmount(live, 0);
    }

    /// <summary>行云流水：回合结束前若处于平静，获得等于层数的格挡。</summary>
    /// <remarks>神圣的退出发生在 After 阶段，晚于这里，所以这时读到的姿态还是回合内的姿态。</remarks>
    private static void LikeWater(LikeWaterPower power, BeforeSideTurnEndMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner } || context.Side != power.Owner.Side)
            return;
        WatcherSim sim = Sim(context, owner);
        if (SV.PowerAmount<Calm>(sim) > 0)
            SV.BlockFor(sim, power.Amount, ValueProp.Unpowered);
    }

    /// <summary>心愿镀甲：回合结束前给等于层数的格挡。</summary>
    private static void WishPlatedArmorBlock(
        WishPlatedArmorPower power,
        BeforeSideTurnEndMirrorContext context)
    {
        if (power.Owner is not { Player: { } owner } || context.Side != power.Owner.Side)
            return;
        SV.BlockFor(Sim(context, owner), power.Amount, ValueProp.Unpowered);
    }

    /// <summary>观者反复用的那个判定：算作招式伤害且不是无强化伤害。</summary>
    private static bool IsPoweredMove(ValueProp props)
        => props.HasFlag(ValueProp.Move) && !props.HasFlag(ValueProp.Unpowered);
}
