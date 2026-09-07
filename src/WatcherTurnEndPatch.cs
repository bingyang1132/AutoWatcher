using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;
using SV = AutoWatcher.WatcherSimVerbs;

namespace AutoWatcher;

/// <summary>
/// 玩家回合结束时观者那些 <c>AfterSideTurnEnd</c> 效果的补丁。
/// </summary>
/// <remarks>
/// 求解器**完全不镜像** <c>AfterSideTurnEnd</c>——它的整个 Hooks 目录里没有这个方法名。所以这
/// 一类既不生效也不记风险，是无声缺口，而且其中一条后果很重：<c>Divinity.AfterSideTurnEnd</c>
/// 会退出姿态，也就是神圣本来在回合结束时消失。不补的话模拟里的神圣会一直留着，之后每个回合
/// 的攻击都按神圣的伤害倍率算，路线会被系统性地高估。
///
/// 这里用 Harmony 而不是注册表，是因为这个位置根本没有注册表。求解器把玩家侧回合结束的 Power
/// 效果写成了一段硬编码流程，补在那段流程的收尾之后，语义上正好对应 <c>AfterSideTurnEnd</c>。
/// 那个收尾在 0.32.0 和 0.33.0 上是两个不同的方法，见 <see cref="ResolveTarget" />。
///
/// 代价是这条依赖求解器的一个内部方法名和签名。所以 <see cref="ResolveTarget" /> 解析不到就
/// 直接抛异常，让整个适配层的注册失败、求解器停在第三方检查上——宁可明确不可用，也不要装着
/// 装着少了一半回合结束效果。
/// </remarks>
internal static class WatcherTurnEndPatch
{
    /// <summary>0.33.0 起玩家回合结束被拆成两段，这一段的末尾才是原来那个位置。</summary>
    private const string LifecycleTypeName = "CombatSolver.PlayerTurnEndLifecycle";
    private const string LifecycleMethodName = "RunPhaseTwo";

    /// <summary>0.32.0 及更早的整段流程。</summary>
    private const string LegacyMethodName = "TriggerPlayerSideTurnEndEffects";

    /// <summary>
    /// 找出该补在哪里，并给出对应的 Postfix 名字。
    /// </summary>
    /// <remarks>
    /// 求解器 0.33.0 把 <c>CorePowerSupport.TriggerPlayerSideTurnEndEffects</c> 拆开了：改名成
    /// <c>TriggerPlayerRegularSideTurnEndEffects</c>，并把结尾的 <c>EndTurnPowerSupport.TriggerLate</c>
    /// 和 <c>NormalizeCardAfflictions</c> 挪到了 <c>PlayerTurnEndLifecycle.RunPhaseTwo</c> 的末尾。
    ///
    /// <b>所以新版该补 <c>RunPhaseTwo</c>，不是补那个改了名的方法。</b> 观者的回合结束效果里有
    /// 终焉的群体伤害，它跑在晚阶段 Power 之前还是之后是有区别的；补在 <c>RunPhaseTwo</c> 末尾
    /// 才和 0.32.0 上验过的位置一致。
    ///
    /// 两个名字都不能写成 <c>nameof</c>：各自只存在于一边，写死引用会让程序集在另一边编译不过、
    /// 装上也起不来。所以按字符串找，找到哪个用哪个。
    ///
    /// Harmony 按参数名注入，而两版的那个参数一个叫 <c>players</c> 一个叫 <c>participants</c>，
    /// 所以两个 Postfix 只是签名不同的薄壳，逻辑都在 <see cref="RunAll" />。
    /// </remarks>
    public static (MethodInfo Target, string PostfixName) ResolveTarget()
    {
        Type? lifecycle = typeof(CorePowerSupport).Assembly
            .GetType(LifecycleTypeName, throwOnError: false);
        if (lifecycle is not null
            && AccessTools.Method(lifecycle, LifecycleMethodName) is { } phaseTwo)
        {
            return (phaseTwo, nameof(PostfixParticipants));
        }
        if (AccessTools.Method(typeof(CorePowerSupport), LegacyMethodName) is { } legacy)
            return (legacy, nameof(Postfix));
        throw new MissingMethodException(
            $"{LifecycleTypeName}/{nameof(CorePowerSupport)}",
            $"{LifecycleMethodName}/{LegacyMethodName}");
    }

    public static void Postfix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        IReadOnlyList<Creature> players)
        => RunAll(simulator, combat, players);

    public static void PostfixParticipants(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        IReadOnlyList<Creature> participants)
        => RunAll(simulator, combat, participants);

    private static void RunAll(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        IReadOnlyList<Creature> players)
    {
        foreach (Creature creature in players)
        {
            if (creature.Player is not { } owner)
                continue;
            if (!simulator.State.GetCreature(creature).IsAlive)
                continue;
            RunFor(new WatcherSim(combat, simulator, simulator.State, simulator.History, owner));
        }
    }

    /// <summary>
    /// 按声明顺序跑观者的回合结束效果。
    /// </summary>
    /// <remarks>
    /// 真实的触发顺序由 Power 的施加先后决定，这里复现不了，所以固定成一个顺序并在这里写明。
    /// 目前这几个之间没有互相依赖：神圣退出、两个自我移除、造牌、群体伤害，彼此不读对方的结果。
    /// </remarks>
    private static void RunFor(WatcherSim sim)
    {
        // 神圣在回合结束时退出。走姿态动词，这样退出的连带效果（疾风连击回手、心灵堡垒格挡、
        // 紫色莲花能量）也一并正确。
        if (sim.Combat.GetAmount<Divinity>(sim.Self) > 0)
            WatcherStanceVerbs.ExitStance(sim);

        RemoveSelf<WaveOfTheHandPower>(sim);
        RemoveSelf<BlessProphecyDamagePower>(sim);

        // 钻研：按层数往抽牌堆随机位置塞洞察。
        if (SV.PowerAmount<StudyPower>(sim) is > 0 and var study)
            SV.AddCards<WatcherInsight>(sim, PileType.Draw, study, CardPilePosition.Random);

        // 终焉：对所有可命中敌人造成等于层数的伤害，不受力量影响。
        if (SV.PowerAmount<OmegaPower>(sim) is > 0 and var omega)
        {
            foreach (Creature enemy in sim.Combat.HittableEnemies.ToArray())
                sim.Simulator.Damage(enemy, omega, ValueProp.Unpowered, sim.Self);
        }


        // 悟命：把下回合要给的真言存进 Power 的私有字段，那不是可镜像的状态。
        if (SV.PowerAmount<EnlightenFatePower>(sim) > 0)
            sim.Unmirrored("悟命在回合结束时排入下回合的真言");
    }

    /// <summary>回合结束时自我移除的 Power。模拟里移除等价于把数量置零。</summary>
    private static void RemoveSelf<TPower>(WatcherSim sim)
        where TPower : PowerModel
    {
        if (sim.Combat.GetPower<TPower>(sim.Self) is { Amount: > 0 } power)
            sim.Combat.SetPowerAmount(power, 0);
    }
}

/// <summary>
/// 玩家回合开始时观者 Power 效果的补丁。
/// </summary>
/// <remarks>
/// 和回合结束那条同理：<c>AfterPlayerTurnStart</c> 不在求解器镜像的方法列表里，而求解器把
/// 玩家回合开始的 Power 效果写成了一段按类型的硬编码 switch，没有注册点。
///
/// 这一批的分量不小：虔诚每回合给真言、战歌每回合造打击、收集每回合造升级过的奇迹、
/// 沸腾之怒进愤怒并抽牌然后消失、预知每回合给天命。它们大多是观者的循环引擎，全漏掉的话
/// 路线会系统性低估。
/// </remarks>
internal static class WatcherPowerTurnStartPatch
{
    public static MethodInfo ResolveTarget()
        => AccessTools.Method(
               typeof(TurnStartPowerSupport),
               nameof(TurnStartPowerSupport.TriggerAfterPlayerTurnStart))
           ?? throw new MissingMethodException(
               nameof(TurnStartPowerSupport),
               nameof(TurnStartPowerSupport.TriggerAfterPlayerTurnStart));

    public static void Postfix(
        bool __result,
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Player player)
    {
        if (__result)
            return;
        if (!simulator.State.GetCreature(player.Creature).IsAlive)
            return;

        WatcherSim sim = new(combat, simulator, simulator.State, simulator.History, player);

        // 战歌：按层数往手里造打击。
        if (SV.PowerAmount<BattleHymnPower>(sim) is > 0 and var hymn)
            SV.AddCards<WatcherSmite>(sim, PileType.Hand, hymn);

        // 收集：造一张升级过的奇迹进手牌，然后自减一层，减到零就消失。
        if (SV.PowerAmount<CollectPower>(sim) > 0)
        {
            foreach (SimCardPileAddResult added in
                     simulator.CreateAndAddGeneratedCardsToCombat<WatcherMiracle>(
                         player, PileType.Hand, 1, player))
            {
                simulator.Upgrade(added.CardAdded);
            }
            if (combat.GetMutablePower<CollectPower>(player.Creature) is { Amount: > 0 } collect)
                combat.SetPowerAmount(collect, collect.Amount - 1);
        }

        // 虔诚：按层数给真言，走真言动词所以满 10 转神圣也正确。
        if (SV.PowerAmount<DevotionPower>(sim) is > 0 and var devotion)
            SV.GainMantra(sim, devotion);

        // 沸腾之怒：进愤怒、按层数抽牌、然后自己消失。
        if (SV.PowerAmount<SimmeringFuryPower>(sim) is > 0 and var fury)
        {
            WatcherStanceVerbs.EnterWrath(sim);
            SV.Draw(sim, fury);
            if (combat.GetMutablePower<SimmeringFuryPower>(player.Creature) is { } live)
                combat.SetPowerAmount(live, 0);
        }

        // 预知姿态：每回合给 2 层天命。
        if (SV.PowerAmount<Foreseen>(sim) > 0)
            SV.Power(sim, typeof(KnowFatePower), 2);

        // 亵渎的回合结束死亡：不是挂上的那一回合就生效，造成足以致死的无视格挡伤害。
        if (combat.GetPower<EndTurnDeathPower>(player.Creature) is { Amount: > 0 } marker
            && marker._appliedOnTurn != combat.GetPlayerTurnNumber(player))
        {
            simulator.Damage(
                player.Creature, 99999, ValueProp.Unblockable | ValueProp.Unpowered, null);
            if (combat.GetMutablePower<EndTurnDeathPower>(player.Creature) is { } liveMarker)
                combat.SetPowerAmount(liveMarker, 0);
        }
    }
}

/// <summary>
/// 玩家回合开始时观者遗物效果的补丁。目前只有达玛茹。
/// </summary>
/// <remarks>
/// 求解器的遗物回合开始是一段按遗物类型的硬编码 switch，没有注册点，所以只能补在它后面。
/// 达玛茹每个玩家回合给 1 点真言，而真言会攒到 10 转神圣，所以漏掉它不只是少 1 点计数，
/// 而是整条神圣循环的节奏都会算错。
///
/// 和回合结束那条一样，解析不到目标方法就抛异常让整个适配层不注册。
/// </remarks>
internal static class WatcherTurnStartPatch
{
    public static MethodInfo ResolveTarget()
        => AccessTools.Method(
               typeof(SimulatedCombatState),
               nameof(SimulatedCombatState.TriggerRelicsAfterPlayerTurnStart))
           ?? throw new MissingMethodException(
               nameof(SimulatedCombatState),
               nameof(SimulatedCombatState.TriggerRelicsAfterPlayerTurnStart));

    public static void Postfix(
        SimulatedCombatState __instance,
        bool __result,
        CombatPredictionSimulator simulator,
        Player player)
    {
        // 返回 true 表示某个遗物打断了回合开始去等玩家选择，这时后面的遗物本来也不会触发。
        if (__result)
            return;
        if (!simulator.State.GetCreature(player.Creature).IsAlive)
            return;

        WatcherSim sim = new(__instance, simulator, simulator.State, simulator.History, player);
        foreach (RelicModel relic in __instance.RelicsOf(player))
        {
            if (relic.IsMelted)
                continue;
            if (relic is Damaru damaru)
                SV.GainMantra(sim, damaru.DynamicVars["Mantra"].IntValue);
        }
    }
}
