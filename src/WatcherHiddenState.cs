using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Models;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 观者那两处「不在 <c>DynamicVars</c> 里、但影响后续结算」的 Power 状态的登记。
/// </summary>
/// <remarks>
/// <para>
/// 状态指纹只收 <c>DynamicVars</c>。放在普通私有字段或 <c>_internalData</c> 里的计数两边都
/// 看不见，后果是<b>只在这个计数上不同的两条搜索分支指纹相同，会被当成同一个状态去掉一条</b>。
/// 数值本身算得对，但搜索可能把算得对的那条丢了，所以记风险标记解决不了——红字只是显示。
/// </para>
/// <para>
/// 两处：
/// <list type="bullet">
/// <item><b>累计真言</b>（光辉的伤害）。<c>WatcherStatePower</c> 的普通私有 <c>int</c>，
/// <c>MemberwiseClone</c> 会带过去，所以只要登记读取函数。</item>
/// <item><b>天人形态的实例个数</b>。存在 <c>DevaPower</c> 的 <c>_internalData</c> 里，克隆会重置，
/// 所以还要登记根捕获，见 <see cref="WatcherDevaInstances" />。</item>
/// </list>
/// </para>
/// <para>
/// 登记入口是可选的：绑不上就跳过，光辉那边改记风险，天人形态的数值照样算对（个数走
/// <c>StateStore</c>，与指纹无关），只是分支可能被去重。
/// </para>
/// </remarks>
internal static class WatcherHiddenState
{
    /// <summary>累计真言有没有真的进指纹。光辉据此决定要不要记风险。</summary>
    public static bool MantraGainedInFingerprint { get; private set; }

    public static void RegisterAll()
    {
        // 根捕获先登记：有登记入口就走它，没有就由 Entry 装 Harmony 补丁补同一件事。
        SolverCompat.RegisterPowerHiddenRootCapture?.Invoke(
            typeof(DevaPower),
            static (simulator, clone, original) =>
                WatcherDevaInstances.Seed(simulator, (DevaPower)clone, (DevaPower)original));

        if (SolverCompat.RegisterPowerHiddenState is not { } register)
            return;

        register(
            typeof(WatcherStatePower),
            "TotalMantraGained",
            static (_, power) => ((WatcherStatePower)power).TotalMantraGainedThisCombat);
        register(
            typeof(DevaPower),
            "InstanceCount",
            static (simulator, power) =>
                WatcherDevaInstances.InstanceCount(simulator, (DevaPower)power));
        MantraGainedInFingerprint = true;
    }

    /// <summary>根捕获走的是登记还是 Harmony 补丁。Entry 据此决定装不装补丁。</summary>
    public static bool NeedsRootCapturePatch => SolverCompat.RegisterPowerHiddenRootCapture is null;
}
