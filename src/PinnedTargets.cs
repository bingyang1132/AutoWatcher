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
    /// <c>0.32.0</c> 是第一个**发布产物里就带全**适配层需要的五条扩展点的版本：第三方战略估值、
    /// 药水选择、卡牌选择、牌堆弃牌、可打出性覆盖。前一个版本 <c>0.31.3</c> 的源码里有前四条，
    /// 但发布的二进制不含卡牌选择那一条（它是随后合进主线的），所以不能拿它当下限。
    ///
    /// 光看版本号仍然不够——这就是结构自检存在的理由。
    /// </remarks>
    public static readonly Version CombatSolverMinimumVersion = new(0, 32, 0);

    /// <summary>已经逐个动词核对过的那一份观者：v0.9.28，Workshop 3747526116。</summary>
    /// <remarks>
    /// 逐个动词的核对是在 v0.9.25 上做的，之后每次抬这个常量都是把两版反编译出来整体 diff、
    /// 只确认核对结论仍然成立，不是重新核对一遍。历次证据见 docs/VERIFICATION.md。
    ///
    /// <para>
    /// 0.9.27 → 0.9.28 是 +927 / −25 行，其中 648 行是新的审判锤特效。逐条看下来只有两处动了
    /// 玩法，两处都已经跟进：
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>通晓万物</b>多了一道「被卡牌逻辑挡住就直接消耗」，见
    /// <c>WatcherCardChoices.OmniscienceApply</c>。这一处不跟进会静默算错：求解器把双倍出牌
    /// 算进路线，实机只是消耗掉。
    /// </item>
    /// <item>
    /// <b>预见</b>回收迂回的判据从 <c>Id.Entry == "WEAVE"</c> 改成 <c>card is WatcherWeave</c>。
    /// 前者匹配不上（真实 id 是 <c>WATCHER_WEAVE</c>），也就是 0.9.27 实机压根没回收；
    /// 0.9.28 是修好了。我们的镜像一直按 <c>WATCHER_WEAVE</c> 算，现在改成同源的类型判据，
    /// 见 <c>WatcherSimVerbs.Scry</c>。
    /// </item>
    /// </list>
    /// <para>
    /// 其余全是特效、音效、卡面切换和着色器字符串的换行符：新增 <c>StsJudgmentHammerEffect</c>、
    /// <c>StsJudgmentGhostEffect</c>、<c>StsJudgmentMoteEffect</c>、<c>StsJudgmentTextEffect</c>
    /// 和 <c>WatcherJudgmentDeath</c>（一个只改死亡动画的 Harmony 前缀）。审判自己的斩杀判据
    /// （<c>CurrentHp &lt;= MagicNumber</c> 就击杀）一个字没动。
    /// </para>
    ///
    /// 抬这个常量的实际作用是让工坊用户不再看到「这一份观者不是核对过的那一版」那句警告——
    /// 那句话本身是对的，但对着一个已经跟进过的版本发警告，只会让真正该注意的时候没人看。
    /// </remarks>
    public const string VerifiedWatcherDllSha256 =
        "3262f52087cfbee9d0efd72e193aa6c3d9164b412f499d1c7b46ace0ac4311e5";

    public static string? TryComputeSha256(Assembly assembly)
    {
        string location = assembly.Location;
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
            return null;
        using FileStream stream = File.OpenRead(location);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
