using System.Reflection;
using System.Security.Cryptography;

namespace SolverWatcherAdapter;

/// <summary>
/// 这个适配层是针对下面这些确切版本逐个动词核对过的。
/// </summary>
/// <remarks>
/// 观者按文件哈希钉死，不按版本号钉死。作者不一定每次改动都会升版本号，而适配层是按
/// 反编译出来的具体实现写的：一个没升版本号的签名改动会让某张牌变成"没有效果但看起来正常"。
/// 哈希是唯一能挡住这种情况的检查。求解器按程序集版本号钉死就够了，因为那是本地构建产物，
/// 版本号由我们自己维护。
/// </remarks>
internal static class PinnedTargets
{
    /// <summary>观者 v0.9.25，Workshop 3747526116，2026-09-04 钉死。</summary>
    public const string WatcherDllSha256 =
        "9b6d6b8806e37b07a83576fef766a07aa42fc642168fa2c3e58e148c6f377733";

    /// <summary>核对适配层时所针对的求解器版本。</summary>
    /// <remarks>
    /// 2026-09-05 从 <c>0.29.0</c> 抬到 <c>0.30.0</c>。上游 <c>f63c57c..b04d3ec</c> 这 26 个提交里，
    /// <c>src/</c> 只动了 <c>CombatBugReportExporter.cs</c> 和 <c>UnattendedTestRunner.cs</c>，
    /// 适配层用到的镜像注册表、模拟状态和选择通道一行没改，所以这次是纯版本号跟进，
    /// 不需要重新逐个动词核对。
    /// </remarks>
    public static readonly Version CombatSolverVersion = new(0, 30, 0, 0);

    public static string? TryComputeSha256(Assembly assembly)
    {
        string location = assembly.Location;
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
            return null;
        using FileStream stream = File.OpenRead(location);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
