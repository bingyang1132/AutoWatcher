using System.Reflection;
using System.Text;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 加载期校验适配层的前提是否成立。不成立就整体不注册。
/// </summary>
/// <remarks>
/// <para>
/// 这里刻意选择"要么全装上，要么一个都不装"。装一半比不装更糟：求解器会拿着一部分正确的
/// 镜像给出看起来可信的路线，而缺掉的那部分静默变成空操作。一个都不装的话，求解器会在
/// 那道第三方 Mod 的门上明确停下并显示原因，玩家立刻知道适配层没生效。
/// </para>
/// <para>
/// 结构自检**只能用反射**，不能直接写类型名。适配层的注册代码是直接引用求解器内部类型的，
/// 缺哪个类型，那段代码在 JIT 的时候就会抛 <c>TypeLoadException</c>；自检的存在就是为了在那
/// 之前给出一句人能看懂的话。如果自检自己也直接引用了那些类型，它会先崩，等于没有自检。
/// </para>
/// </remarks>
internal static class AdapterSelfCheck
{
    public readonly record struct Result(bool Ok, string Detail);

    /// <summary>
    /// 求解器里适配层离不开的扩展点。少任何一个，注册都会在 JIT 时炸，所以这里先查。
    /// </summary>
    /// <remarks>
    /// 只列**新加的**扩展点。求解器的老接口面太大，逐个查没有意义；而会出问题的恰好都是
    /// 这些逐版加进去的入口——用户手里的求解器比适配层旧，缺的一定是它们。
    /// </remarks>
    private static readonly (string Type, string Member)[] RequiredSolverMembers =
    [
        ("CombatSolver.CardChoiceMirrors", "Register"),
        ("CombatSolver.PotionChoiceMirrors", "Register"),
        ("CombatSolver.StrategicEffectMirrors", "Register"),
        ("CombatSolver.StrategicEffectHost", "Enemy"),
        ("CombatSolver.PlanChoiceEffect", "ModDefined"),
        ("CombatSolver.Engine.Common.ICombatPredictionChoiceSink", "ResolvePileDiscardChoice"),
    ];

    /// <summary>
    /// 观者里适配层伸手进去的私有成员。这些是 publicizer 放开的，也是最容易在更新里改掉的。
    /// </summary>
    /// <remarks>
    /// 只有私有成员值得列。公开成员改了会直接编译不过，用户手上不会出现那种组合；
    /// 而 Harmony 补丁的目标方法各自的 <c>ResolveTarget()</c> 解析不到就抛，已经有覆盖。
    /// </remarks>
    private static readonly (string Type, string Member)[] RequiredWatcherMembers =
    [
        ("WatcherMod.WatcherStatePower", "KnowFateConsumedThisTurn"),
        ("WatcherMod.WatcherStatePower", "KnowFateConsumptionAttemptedThisCard"),
        ("WatcherMod.WatcherStatePower", "KnowFateLastObserved"),
        ("WatcherMod.EndTurnDeathPower", "_appliedOnTurn"),
        // 天人形态的实例表。适配层只读它的长度，而那个长度决定每回合给多少能量。
        ("WatcherMod.DevaPower", "Instances"),
    ];

    public static Result Run()
    {
        Assembly watcher = typeof(Wrath).Assembly;
        Assembly solver = typeof(CombatSolver.Entry).Assembly;

        // 只有求解器能按程序集版本号判。观者的程序集版本是 0.0.0.0，见 PinnedTargets。
        if (CheckVersion(solver, PinnedTargets.CombatSolverMinimumVersion, "求解器") is { } solverVersionError)
            return new Result(false, solverVersionError + " 请从求解器的 GitHub 取最新版。");

        if (CheckMembers(solver, RequiredSolverMembers) is { } missingSolver)
        {
            return new Result(
                false,
                $"求解器缺少适配层需要的扩展点：{missingSolver}。"
                + "已发布的求解器可能还不含最新合并的扩展点，请从它的 GitHub 取最新版；"
                + "在此之前适配层不注册任何镜像。");
        }
        if (CheckMembers(watcher, RequiredWatcherMembers) is { } missingWatcher)
        {
            return new Result(
                false,
                $"观者 Mod 的内部结构和适配层预期的不一样，缺少：{missingWatcher}。"
                + "适配层要读这几个只在钩子里维护的字段才能补上求解器不分发的那些效果，"
                + "读不到就会静默算错，所以这次不注册任何镜像。等适配层跟进新版观者。");
        }

        // 到这里就装得上了。剩下的只是"这一版我核对过没有"。
        StringBuilder detail = new();
        detail.Append("求解器 ").Append(solver.GetName().Version)
            .Append(" 与观者的结构自检通过。");
        string? hash = PinnedTargets.TryComputeSha256(watcher);
        if (hash is null)
        {
            detail.Append(" 读不到观者的程序集文件，无法判断是否核对过这一版。");
        }
        else if (!string.Equals(hash, PinnedTargets.VerifiedWatcherDllSha256, StringComparison.Ordinal))
        {
            // 结构对得上但不是核对过的那一份。签名没变、数值或语义变了的情况自检抓不到，
            // 所以这里必须留一句明确的话，否则算错了没人能解释。
            detail.Append(" 注意：这一份观者不是适配层逐条核对过的那一版（核对过的是 ")
                .Append(PinnedTargets.VerifiedWatcherDllSha256[..12])
                .Append("，实际是 ").Append(hash[..12])
                .Append("）。结构没变所以照常注册，但如果观者在这一版改了数值或效果，"
                    + "适配层会算错且不会自己发现。发现路线不对请报 Bug。");
        }
        detail.Append(' ').Append(SolverCompat.Summary).Append('。');
        return new Result(true, detail.ToString());
    }

    private static string? CheckVersion(Assembly assembly, Version minimum, string what)
    {
        Version? actual = assembly.GetName().Version;
        if (actual is null)
            return $"读不到{what}的程序集版本号，适配层不注册任何镜像。";
        if (actual < minimum)
            return $"{what}版本过低：需要至少 {minimum}，实际是 {actual}。";
        return null;
    }

    /// <summary>返回第一个缺失成员的描述，全都在就返回 <c>null</c>。</summary>
    private static string? CheckMembers(
        Assembly assembly,
        (string Type, string Member)[] required)
    {
        foreach ((string typeName, string memberName) in required)
        {
            Type? type = assembly.GetType(typeName, throwOnError: false);
            if (type is null)
                return typeName;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static;
            if (type.GetMember(memberName, flags).Length == 0)
                return $"{typeName}.{memberName}";
        }
        return null;
    }
}
