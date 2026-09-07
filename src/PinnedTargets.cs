using System.Reflection;
using System.Security.Cryptography;

namespace AutoWatcher;

/// <summary>
/// 适配层核对时所针对的版本，以及能不能装得上的下限。
/// </summary>
/// <remarks>
/// <para>
/// 这里以前是两道**精确**钉死：观者按文件 SHA256、求解器按程序集版本号严格相等。那两道在开发
/// 期是对的——只要有一点漂移就停下来重新核对。但作为发布产物它们是砖：观者或求解器随便哪个
/// 更新一次，所有用户的适配层就一个镜像都不注册，而绝大多数更新其实什么都没动到我们用的面上。
/// </para>
/// <para>
/// 现在分成三层，从硬到软：
/// </para>
/// <list type="number">
/// <item><b>版本下限</b>：低于下限直接拒绝加载。适配层用到的东西是那一版才有的，谈不上兼容。</item>
/// <item><b>结构自检</b>：核对我们真正伸手进去的那几个成员还在不在（见
/// <see cref="AdapterSelfCheck"/>）。不在就干净地拒绝加载并说明缺了什么，不装一半。</item>
/// <item><b>核对标记</b>：哈希只用来判断"这一版我核对过没有"。不一致仍然加载，但会在日志里
/// 明确警告。</item>
/// </list>
/// <para>
/// 第三层是这次改动里唯一放松的地方，代价要说清楚：观者可以在**不改任何签名**的情况下改数值
/// 或改语义，结构自检抓不到这种漂移，那时适配层会算错而且不会自己知道。用哈希钉死能挡住它，
/// 代价是每次更新都砖。选后者是因为砖掉是必然发生的，而静默漂移是偶发的、且有警告兜底。
/// </para>
/// </remarks>
internal static class PinnedTargets
{
    /// <summary>
    /// 观者**不能**按程序集版本号判。
    /// </summary>
    /// <remarks>
    /// <c>Watcher.dll</c> 的程序集版本是 <c>0.0.0.0</c>——它没设 <c>AssemblyVersion</c>，
    /// 真正的 <c>0.9.25</c> 只写在 <c>Watcher.json</c> 里。所以
    /// <c>assembly.GetName().Version</c> 拿到的永远是 0，任何版本下限都会把所有版本都挡掉。
    /// 这个坑真踩过：加了下限之后适配层在每一条夹具里都拒绝加载。
    ///
    /// 版本下限交给清单里的 <c>dependencies</c>（由 Mod 加载器执行），运行期只做结构自检。
    /// 顺带一提，求解器的问题包里 <c>environment.mods</c> 记的也是程序集版本，
    /// 所以观者在那儿也显示成 0.0.0.0，不要拿它当版本依据。
    /// </remarks>
    public const string WatcherVersionSource = "Watcher.json（清单），不是程序集版本号";

    /// <summary>
    /// 求解器的最低版本。适配层要接进它的内部注册表，这些注册表是逐版加进去的。
    /// </summary>
    /// <remarks>
    /// <c>0.31.3</c> 是上游合并了第三方战略估值、药水选择、卡牌选择、牌堆弃牌、可打出性覆盖
    /// 五条扩展点之后的版本。注意：**已发布的 0.31.3 二进制不含卡牌选择那一条**，那条是随
    /// 后合进主线的，所以光看版本号不够，还要靠结构自检。
    /// </remarks>
    public static readonly Version CombatSolverMinimumVersion = new(0, 31, 3);

    /// <summary>已经逐个动词核对过的那一份观者：v0.9.25，Workshop 3747526116，2026-09-04。</summary>
    public const string VerifiedWatcherDllSha256 =
        "9b6d6b8806e37b07a83576fef766a07aa42fc642168fa2c3e58e148c6f377733";

    public static string? TryComputeSha256(Assembly assembly)
    {
        string location = assembly.Location;
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
            return null;
        using FileStream stream = File.OpenRead(location);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
