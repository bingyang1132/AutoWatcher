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
    /// 2026-09-06 从 <c>0.30.0</c> 抬到 <c>0.31.1</c>。这一次**不是**纯版本号跟进：上游把我们的
    /// #15 #18 #40 #41 #46 #47 一起合了，还合了 #43 的战前预测 API，<c>cd55cee..0552b33</c> 在
    /// 适配层直接依赖的面上动了 64 个文件、约 1700 行。
    ///
    /// 已经撞到一次真实断裂：<c>SimulatedCombatState.PrepareExtraPlayerTurn</c> 被拆成
    /// <c>TryPrepareExtraPlayerTurn</c> 和 <c>TryPrepareLiveExtraPlayerTurn</c>，返回值语义也变了。
    /// 名字恰好一起改了，所以我们撞到的是编译错误；要是只改语义不改名字，就是静默反义。
    ///
    /// 抬版本号只挡得住编译期断裂。行为漂移要靠 <c>tools/run-watcher-matrix.ps1</c> 全量跑一遍，
    /// 那才是这次跟进的验收条件。
    /// </remarks>
    public static readonly Version CombatSolverVersion = new(0, 31, 1, 0);

    public static string? TryComputeSha256(Assembly assembly)
    {
        string location = assembly.Location;
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
            return null;
        using FileStream stream = File.OpenRead(location);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
