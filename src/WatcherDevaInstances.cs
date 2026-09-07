using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 天人形态的实例个数：模拟里的存放、从实机快照带过来，以及每回合的能量。
/// </summary>
/// <remarks>
/// <para>
/// <c>DevaPower</c> 用 <c>_internalData</c> 存一个 <c>List&lt;int&gt;</c> 实例表，N 张牌是 N 个
/// 独立成长的实例，不等于一个数量为 N 的实例。但整张表不必复现：<c>AddInstance</c> 之后
/// <c>SetAmount(Instances.Sum())</c>，而 <c>AfterEnergyReset</c> 每回合先给 <c>Sum()</c> 点能量
/// 再把每个实例加一——也就是说下一回合的总和等于当前总和加实例个数。总和就是 <c>Amount</c>，
/// 求解器本来就在算，所以<b>只需要多记一个整数：实例个数</b>。
/// </para>
/// <para>
/// 个数不能存在 Power 上。<c>PowerModel.DeepCloneFields</c> 把 <c>_internalData</c> 重置成
/// <c>InitInternalData()</c>，而搜索每次分叉都会克隆 Power，所以写在那里的东西每一层都被清空。
/// 求解器给这类状态准备的地方是 <c>simulator.StateStore</c>：它按模型索引、随分叉复制，
/// 原版虚空形态、硬化外壳那一批走的就是这条路。
/// </para>
/// <para>
/// 也正因为克隆会重置，实机快照里已有的实例个数必须在根捕获时读一次实机实例。求解器把这件事
/// 写在 <c>PowerPredictionStateSupport.CaptureRootState</c> 里，那是一个按原版类型的
/// <c>switch</c>。有第三方登记入口时走登记（<c>PowerHiddenStateMirrors.RegisterRootCapture</c>），
/// 没有就补在那个方法后面——两条路都调
/// <see cref="Seed" />，不写两份逻辑。
/// </para>
/// </remarks>
internal static class WatcherDevaInstances
{
    /// <summary>模拟里的实例个数。总和走 Power 自己的 <c>Amount</c>，这里只记个数。</summary>
    internal sealed class InstanceCountState : IPredictionStateForkable
    {
        public int Count { get; set; }

        public object Fork(PredictionForkContext context) => MemberwiseClone();
    }

    /// <summary>
    /// 根捕获：把实机实例的实例个数搬进模拟状态。
    /// </summary>
    /// <remarks>
    /// 键用克隆、值从实机读，和原版同一个形状。搜索途中新施加的实例不走这里，它们的个数由
    /// <see cref="AddInstance" /> 从零累加，也是对的。
    /// </remarks>
    public static void Seed(CombatPredictionSimulator simulator, DevaPower clone, DevaPower original)
    {
        int count = LiveInstanceCount(original);
        if (count <= 0)
            return;
        simulator.StateStore.GetReadOnly(clone, () => new InstanceCountState { Count = count });
    }

    /// <summary>读实机实例的实例表长度。表还没初始化时按零算。</summary>
    private static int LiveInstanceCount(DevaPower power)
    {
        try
        {
            return power.Instances?.Count ?? 0;
        }
        catch (Exception)
        {
            // 实例表是 _internalData，理论上构造时就初始化了。真取不到时按零算而不是抛：
            // 少算一个天人形态的成长，比让整次搜索失败好。
            return 0;
        }
    }

    /// <summary>模拟里的实例个数。没有记录过就是零。</summary>
    public static int InstanceCount(CombatPredictionSimulator simulator, DevaPower power)
        => simulator.StateStore.Peek(power, static _ => new InstanceCountState()).Count;

    /// <summary>打出一张天人形态：实例个数加一。总和由调用方按原版的分支自己加。</summary>
    public static void AddInstance(CombatPredictionSimulator simulator, DevaPower power)
        => simulator.StateStore.Get(power, static _ => new InstanceCountState()).Count += 1;
}

/// <summary>
/// 根捕获补丁：求解器还没有第三方登记入口时，把实例个数补在原版那个 <c>switch</c> 后面。
/// </summary>
/// <remarks>
/// 有 <c>PowerHiddenStateMirrors.RegisterRootCapture</c> 时不装这条补丁，走登记。
/// 目标方法解析不到就抛，让整个适配层不注册——漏了根捕获的后果是实机已经打出的天人形态在
/// 模拟里个数为零，每回合的能量少算，属于系统性低估，不能静默。
/// </remarks>
internal static class WatcherDevaRootCapturePatch
{
    public static MethodInfo ResolveTarget()
        => AccessTools.Method(
               typeof(PowerPredictionStateSupport),
               nameof(PowerPredictionStateSupport.CaptureRootState))
           ?? throw new MissingMethodException(
               nameof(PowerPredictionStateSupport),
               nameof(PowerPredictionStateSupport.CaptureRootState));

    public static void Postfix(
        CombatPredictionSimulator simulator,
        PowerModel target,
        PowerModel source)
    {
        if (target is DevaPower clone && source is DevaPower original)
            WatcherDevaInstances.Seed(simulator, clone, original);
    }
}

/// <summary>
/// 天人形态每回合的能量。
/// </summary>
/// <remarks>
/// 原版是 <c>DevaPower.AfterEnergyReset</c>：先按实例表总和给能量，再把每个实例加一。求解器把
/// 玩家侧的 <c>AfterEnergyReset</c> 写成了 <c>PersistentPowerSupport.TriggerAfterEnergyReset</c>
/// 里一段按类型的硬编码流程，没有注册点，所以补在它后面。
///
/// 注意这条的返回值口径和回合开始那两条相反：这个方法返回的是
/// <c>!simulator.HasPendingChoice</c>，也就是 <c>true</c> 表示一切正常。
///
/// 原版这里<b>不</b>判无能量增益，所以镜像也不判——按反编译出的实现写，不按别的 Power 类推。
/// </remarks>
internal static class WatcherDevaEnergyPatch
{
    public static MethodInfo ResolveTarget()
        => AccessTools.Method(
               typeof(PersistentPowerSupport),
               nameof(PersistentPowerSupport.TriggerAfterEnergyReset))
           ?? throw new MissingMethodException(
               nameof(PersistentPowerSupport),
               nameof(PersistentPowerSupport.TriggerAfterEnergyReset));

    public static void Postfix(
        bool __result,
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Player player)
    {
        if (!__result)
            return;
        Creature owner = player.Creature;
        if (!simulator.State.GetCreature(owner).IsAlive)
            return;
        if (combat.GetPower<DevaPower>(owner) is not { Amount: > 0 } deva)
            return;
        int count = WatcherDevaInstances.InstanceCount(simulator, deva);
        if (count <= 0)
            return;

        // 先按总和给能量。
        simulator.State.GetPlayerCombatState(player).GainEnergy(deva.Amount);
        // 再把每个实例加一：总和涨的正好是实例个数。
        if (combat.GetMutablePower<DevaPower>(owner) is { } mutable)
            combat.SetPowerAmount(mutable, mutable.Amount + count);
    }
}
