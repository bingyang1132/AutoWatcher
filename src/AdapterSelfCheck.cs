using System.Reflection;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 加载期校验适配层的前提是否成立。不成立就整体不注册。
/// </summary>
/// <remarks>
/// 这里刻意选择"要么全装上，要么一个都不装"。装一半比不装更糟：求解器会拿着一部分正确的
/// 镜像给出看起来可信的路线，而缺掉的那部分静默变成空操作。一个都不装的话，求解器会在
/// 那道第三方 mod 的门上明确停下并显示原因，玩家立刻知道适配层没生效。
/// </remarks>
internal static class AdapterSelfCheck
{
    public readonly record struct Result(bool Ok, string Detail);

    public static Result Run()
    {
        Assembly watcher = typeof(Wrath).Assembly;
        string? watcherHash = PinnedTargets.TryComputeSha256(watcher);
        if (watcherHash is null)
        {
            return new Result(
                false,
                "无法读取观者 mod 的程序集文件来核对哈希。适配层只对钉死的那一份观者有效，"
                + "在不能确认是哪一份的情况下不会注册任何镜像。");
        }
        if (!string.Equals(watcherHash, PinnedTargets.WatcherDllSha256, StringComparison.Ordinal))
        {
            return new Result(
                false,
                $"观者 mod 与钉死的版本不是同一份。钉死的哈希是 {PinnedTargets.WatcherDllSha256}，"
                + $"实际装的是 {watcherHash}。适配层的每一个动词都是按钉死那一份的实现逐条核对的，"
                + "换了版本必须重新核对，所以这次不注册任何镜像。");
        }

        Version? solver = typeof(CombatSolver.Entry).Assembly.GetName().Version;
        if (solver is null || solver != PinnedTargets.CombatSolverVersion)
        {
            return new Result(
                false,
                $"求解器版本与钉死的不一致。适配层针对 {PinnedTargets.CombatSolverVersion} 核对，"
                + $"实际装的是 {solver?.ToString() ?? "未知"}。适配层要接进求解器的内部注册表，"
                + "版本一换这些内部结构就可能改名或改语义，所以这次不注册任何镜像。");
        }

        return new Result(true, $"观者哈希与求解器 {solver} 均与钉死版本一致。");
    }
}
