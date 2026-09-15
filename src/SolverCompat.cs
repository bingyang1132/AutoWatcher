using System.Linq.Expressions;
using System.Reflection;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Models;

namespace AutoWatcher;

/// <summary>
/// 求解器里"有就用、没有就跳"的那几个入口。
/// </summary>
/// <remarks>
/// 适配层要同时对得上两种求解器：创意工坊/GitHub 发布版，和开发版。有些入口只存在于其中一边，
/// 直接写死引用会让整个程序集在缺那个类型的求解器上加载失败——那不是"少一个功能"，是整个 Mod
/// 起不来。所以这类入口在加载时用反射绑一次，绑不上就留空，调用点判空跳过。
///
/// 绑定用表达式树编译成委托，只在加载时做一次。运行时是一次判空加一次直接调用，
/// 不是每次都走反射——这些方法在一次搜索里可能被调上千次。
///
/// 这里**只放"没有也能正常工作"的入口**。适配层真正离不开的东西（例如
/// <c>CardChoiceMirrors</c>）不走这里，走 <see cref="AdapterSelfCheck"/>：缺了就干净地拒绝
/// 加载并说明原因，而不是装上一半。
/// </remarks>
/// <summary>一个可选入口的探测结果。</summary>
/// <remarks>
/// 分这几档是有代价换来的。原来只有「绑上／没绑上」两态，没绑上一律说成
/// 「求解器没有这个入口」，于是两件完全不同的事长得一模一样：
/// <b>上游还没做</b>（跳过是预期行为），和<b>上游改了签名、我们过期了</b>（是 bug）。
/// 2026-09-15 就栽在这上面：求解器 0.38.3 给 <c>GrowthSourceMirrors.Register</c> 末尾加了
/// 一个可选参数，我们写死 4 参的探测静默失效，勤学精进与许愿的成长额度悄悄没了两天，
/// 而加载日志只是平静地说了句「求解器没有这个入口」——连我自己都被这句话骗过一次。
/// </remarks>
internal enum SolverEntryState
{
    /// <summary>绑上了。</summary>
    Bound,

    /// <summary>类型或成员在求解器里根本不存在。上游还没做，跳过是预期行为。</summary>
    Absent,

    /// <summary>成员在，形状和我们认的对不上。<b>大概率是适配层过期</b>，要去看上游改了什么。</summary>
    Mismatched,

    /// <summary>探测本身抛了异常。</summary>
    Faulted,
}

internal static class SolverCompat
{
    private static readonly Dictionary<string, SolverEntryState> EntryStates = new(StringComparer.Ordinal);

    /// <summary>记下一次探测的结果，原样返回绑定值，方便写在 return 上。</summary>
    private static T? Record<T>(string entry, SolverEntryState state, T? value)
        where T : class
    {
        EntryStates[entry] = state;
        return value;
    }

    private static SolverEntryState StateOf(string entry)
        => EntryStates.TryGetValue(entry, out SolverEntryState state) ? state : SolverEntryState.Absent;

    /// <summary>
    /// 一个入口在加载日志里怎么写。<b>「上游没有」和「签名对不上」必须分开</b>，
    /// 后者是我们的 bug，不能和前者共用一句话。
    /// </summary>
    /// <param name="label">中文名，例如「局外成长来源登记」。</param>
    /// <param name="absent">上游没有这个入口时的后果说明。</param>
    private static string Describe(string entry, string label, string absent)
        => StateOf(entry) switch
        {
            SolverEntryState.Bound => $"{label}：已绑定",
            SolverEntryState.Absent => $"{label}：求解器没有这个入口，{absent}",
            SolverEntryState.Mismatched =>
                $"{label}：求解器有这个入口但签名对不上，**适配层需要更新**，暂时{absent}",
            _ => $"{label}：探测出错，**这是适配层的缺陷**，暂时{absent}",
        };

    /// <summary>
    /// 记一笔"这一类跨战斗收益已经兑现"。只有玩家打开"强制兑现跨战斗收益"时才有消费者，
    /// 而整个特性只存在于开发版求解器上。绑不上就是没有那个开关，不影响收益本身的计价。
    /// </summary>
    public static readonly Action<SimulatedCombatState>? RecordFatalKillGoal =
        BindLongTermGoal("RecordLongTermGoal");

    /// <summary>
    /// 记一笔"带这类收益的消耗牌已经打出去了"。同上。
    /// </summary>
    public static readonly Action<SimulatedCombatState>? RecordFatalKillGoalCardPlayed =
        BindLongTermGoal("RecordLongTermGoalCardPlayed");

    /// <summary>
    /// 把一个 Power 的隐藏状态登记进搜索的状态指纹。
    /// </summary>
    /// <remarks>
    /// 参数依次是 Power 类型、状态名、读取函数。绑不上说明求解器还没有这个入口，那时候只在
    /// 状态不为零时记一条风险——数值照样算得对，缺的是"只在这个状态上不同的两条分支不会被
    /// 去重掉"这一层保证。
    /// </remarks>
    public static readonly Action<Type, string, Func<CombatPredictionSimulator, PowerModel, long>>?
        RegisterPowerHiddenState = BindPowerHiddenState("Register");

    /// <summary>
    /// 登记根捕获：把实机实例的隐藏状态搬进模拟。靠 <c>_internalData</c> 保存状态的 Power
    /// 必须有这一步，因为克隆会把它重置成初值。
    /// </summary>
    /// <remarks>
    /// 绑不上时改走 Harmony 补丁（<see cref="WatcherDevaRootCapturePatch" />），两条路调同一个
    /// 播种函数。
    /// </remarks>
    public static readonly Action<Type, Action<CombatPredictionSimulator, PowerModel, PowerModel>>?
        RegisterPowerHiddenRootCapture = BindPowerHiddenRootCapture();

    /// <summary>
    /// <c>GrowthSourceMirrors.Register</c>。绑上了才能给观者的局外成长牌配独立的战损额度。
    /// </summary>
    private static readonly MethodInfo? GrowthSourceRegister = BindGrowthSourceRegister();

    /// <summary>求解器有没有第三方局外成长来源的登记入口。</summary>
    public static bool HasGrowthSourceRegistry => GrowthSourceRegister is not null;

    /// <summary>
    /// 给一张牌登记移除估值偏置。参数是牌的类型和偏置，负数表示「这张牌该先烧」。绑不上说明
    /// 求解器还没有这个入口，那时候观者的打击、防御按通用估值算成有伤害/格挡的普通牌，
    /// 净化不会优先烧它们。
    /// </summary>
    public static readonly Action<Type, double>? RegisterCardRemovalOffset = BindCardRemovalOffset();

    /// <summary>绑定的结果，写进加载日志，方便一眼看出装的是哪种求解器。</summary>
    /// <remarks>
    /// 每一项都区分「上游没有」和「签名对不上」——见 <see cref="SolverEntryState"/>。
    /// 后者会另外单独 Warn 一次，因为那是需要有人去改代码的事，不该只躺在一行说明里。
    /// </remarks>
    public static string Summary => string.Join("。", [
        Describe(LongTermGoalEntry, "跨战斗收益目标", "已跳过"),
        Describe(HiddenStateEntry, "Power 隐藏状态进指纹", "光辉与天人形态改记风险"),
        Describe(GrowthSourceEntry, "局外成长来源登记", "勤学精进与许愿的金币只走长期资源刻度"),
        Describe(CardRemovalEntry, "移除估值偏置", "净化不会优先烧打击防御"),
    ]);

    /// <summary>
    /// 把「签名对不上」和「探测出错」单独喊一次。加载日志里那一行说明太容易被当成常态读过去。
    /// </summary>
    public static void WarnOnStaleEntries()
    {
        foreach ((string entry, SolverEntryState state) in EntryStates)
        {
            if (state is SolverEntryState.Mismatched)
            {
                EngineDiagnostics.Warn(
                    $"[AutoWatcher] {entry} 在求解器里存在，但签名和适配层认的对不上——"
                    + "这一项已按「没有」处理，对应功能不生效。适配层需要跟上上游的改动。");
            }
            else if (state is SolverEntryState.Faulted)
            {
                EngineDiagnostics.Warn(
                    $"[AutoWatcher] {entry} 的探测抛了异常，已按「没有」处理，对应功能不生效。");
            }
        }
    }

    private const string LongTermGoalEntry = "SimulatedCombatState.RecordLongTermGoal";
    private const string HiddenStateEntry = "PowerHiddenStateMirrors.Register";
    private const string GrowthSourceEntry = "GrowthSourceMirrors.Register";
    private const string CardRemovalEntry = "CardRemovalValueMirrors.Register";

    /// <summary>
    /// 登记一个第三方局外成长来源，返回"记一次收益到手"的委托。求解器没有这个入口时返回
    /// <c>null</c>，调用点判空跳过。
    /// </summary>
    /// <param name="id">持久化键，改了等于换来源，玩家原来填的额度不再生效。</param>
    /// <param name="card">侧栏这一行的图标与标题，求解器在构建侧栏时才调用。</param>
    /// <param name="hasTarget">判牌组里一张牌算不算这个来源的目标。</param>
    /// <param name="title">
    /// 侧栏标题，参数是 <paramref name="card" /> 取回来的那张牌；留空用牌自己的名字。
    /// 和 <paramref name="card" /> 一起延迟调用。
    /// </param>
    /// <remarks>
    /// 句柄类型 <c>GrowthSourceHandle</c> 编译时不可见，所以登记走反射拿到装箱的句柄，再把它
    /// 作为常量嵌进一个编译好的委托里——和 <see cref="BindLongTermGoal" /> 对枚举值是同一手法。
    /// 登记只在加载时发生两次，反射的代价无所谓；<b>记账</b>那一侧是直接调用，不走反射。
    /// </remarks>
    public static Action<SimulatedCombatState>? RegisterGrowthSource(
        string id,
        Func<CardModel> card,
        Func<CardModel, bool> hasTarget,
        Func<CardModel, string>? title = null)
    {
        if (GrowthSourceRegister is null)
            return null;
        try
        {
            // 参数个数按实际签名填。求解器 0.38.3 起在末尾多了一个可选的 opportunityTarget，
            // 我们不用它，补 null 走默认；固定传 4 个会抛 TargetParameterCountException。
            object?[] arguments = new object?[GrowthSourceRegister.GetParameters().Length];
            arguments[0] = id;
            arguments[1] = card;
            arguments[2] = hasTarget;
            arguments[3] = title;
            object? handle = GrowthSourceRegister.Invoke(null, arguments);
            if (handle is null)
                return null;
            MethodInfo? record = typeof(SimulatedCombatState).GetMethod(
                "RecordGrowthReward",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [handle.GetType()],
                modifiers: null);
            if (record is null)
                return null;
            ParameterExpression combat = Expression.Parameter(typeof(SimulatedCombatState), "combat");
            return Expression.Lambda<Action<SimulatedCombatState>>(
                Expression.Call(combat, record, Expression.Constant(handle, handle.GetType())),
                combat).Compile();
        }
        catch (Exception ex)
        {
            EngineDiagnostics.Warn($"[AutoWatcher] 没能登记局外成长来源 {id}：{ex.Message}");
            return null;
        }
    }

    /// <summary>找 <c>GrowthSourceMirrors.Register</c>；签名对不上就当没有。</summary>
    /// <remarks>
    /// <para><b>参数个数别写死。</b>这里原来要求正好 4 个参数。求解器 0.38.3
    /// （「stop search at proven growth targets」）在末尾加了第 5 个可选参数
    /// <c>opportunityTarget</c>，于是这个入口从那一版起**静默地绑不上了**——
    /// 加载日志只剩一句「求解器没有这个入口」，勤学精进和许愿的金币退回长期资源刻度，
    /// 两张牌再也拿不到自己的成长额度。没有报错，没有异常，只有一行看起来一直都在的说明。</para>
    ///
    /// <para>所以只认前 4 个参数的形状，后面多出来的可选参数一律容忍：那是上游加功能的正常方式，
    /// 不该被读成「入口没了」。真正对不上的是前 4 个的类型，那才算没有。</para>
    /// </remarks>
    private static MethodInfo? BindGrowthSourceRegister()
    {
        try
        {
            MethodInfo? register = typeof(SimulatedCombatState).Assembly
                .GetType("CombatSolver.GrowthSourceMirrors", throwOnError: false)
                ?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            if (register is null)
                return Record<MethodInfo>(GrowthSourceEntry, SolverEntryState.Absent, null);
            ParameterInfo[] parameters = register.GetParameters();
            if (parameters.Length < 4
                || parameters[0].ParameterType != typeof(string)
                || parameters[1].ParameterType != typeof(Func<CardModel>)
                || parameters[2].ParameterType != typeof(Func<CardModel, bool>)
                || parameters[3].ParameterType != typeof(Func<CardModel, string>))
            {
                return Record<MethodInfo>(GrowthSourceEntry, SolverEntryState.Mismatched, null);
            }
            // 多出来的必须都是可选的，否则我们补的 null 不一定是它想要的默认值。
            for (int index = 4; index < parameters.Length; index++)
            {
                if (!parameters[index].IsOptional)
                    return Record<MethodInfo>(GrowthSourceEntry, SolverEntryState.Mismatched, null);
            }
            return Record(GrowthSourceEntry, SolverEntryState.Bound, register);
        }
        catch (Exception ex)
        {
            EngineDiagnostics.Warn($"[AutoWatcher] 没能绑上 GrowthSourceMirrors.Register：{ex.Message}");
            return Record<MethodInfo>(GrowthSourceEntry, SolverEntryState.Faulted, null);
        }
    }

    /// <summary>绑 <c>CardRemovalValueMirrors.Register&lt;TCard&gt;(double)</c>。</summary>
    /// <remarks>方法是泛型的，类型参数每次现拼。登记只在加载时发生两次，反射的代价无所谓。</remarks>
    private static Action<Type, double>? BindCardRemovalOffset()
    {
        try
        {
            MethodInfo? generic = typeof(SimulatedCombatState).Assembly
                .GetType("CombatSolver.CardRemovalValueMirrors", throwOnError: false)
                ?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            if (generic is null)
                return Record<Action<Type, double>>(CardRemovalEntry, SolverEntryState.Absent, null);
            if (!generic.IsGenericMethodDefinition
                || generic.GetParameters() is not [{ ParameterType: { } parameter }]
                || parameter != typeof(double))
            {
                return Record<Action<Type, double>>(CardRemovalEntry, SolverEntryState.Mismatched, null);
            }
            return Record<Action<Type, double>>(CardRemovalEntry, SolverEntryState.Bound,
                (cardType, offset) => generic.MakeGenericMethod(cardType).Invoke(null, [offset]));
        }
        catch (Exception ex)
        {
            EngineDiagnostics.Warn($"[AutoWatcher] 没能绑上 CardRemovalValueMirrors.Register：{ex.Message}");
            return Record<Action<Type, double>>(CardRemovalEntry, SolverEntryState.Faulted, null);
        }
    }

    private static Type? HiddenStateMirrors()
        => typeof(SimulatedCombatState).Assembly
            .GetType("CombatSolver.PowerHiddenStateMirrors", throwOnError: false);

    /// <summary>
    /// 绑 <c>PowerHiddenStateMirrors.Register&lt;TPower&gt;(name, Func&lt;Sim, TPower, long&gt;)</c>。
    /// </summary>
    /// <remarks>
    /// 那个方法是泛型的，而调用方只有一个 <c>Type</c>，所以每次登记都要把读取函数从
    /// <c>Func&lt;Sim, PowerModel, long&gt;</c> 包成 <c>Func&lt;Sim, TPower, long&gt;</c>。
    /// 登记只在加载时发生几次，包装的代价无所谓；<b>读取</b>那一侧是编译好的委托直接调用，
    /// 不走反射。
    /// </remarks>
    private static Action<Type, string, Func<CombatPredictionSimulator, PowerModel, long>>?
        BindPowerHiddenState(string name)
    {
        try
        {
            MethodInfo? generic = HiddenStateMirrors()?.GetMethod(
                name, BindingFlags.Public | BindingFlags.Static);
            if (generic is null)
            {
                return Record<Action<Type, string, Func<CombatPredictionSimulator, PowerModel, long>>>(
                    HiddenStateEntry, SolverEntryState.Absent, null);
            }
            if (!generic.IsGenericMethodDefinition)
            {
                return Record<Action<Type, string, Func<CombatPredictionSimulator, PowerModel, long>>>(
                    HiddenStateEntry, SolverEntryState.Mismatched, null);
            }
            EntryStates[HiddenStateEntry] = SolverEntryState.Bound;
            return (powerType, stateName, read) =>
            {
                Type funcType = typeof(Func<,,>).MakeGenericType(
                    typeof(CombatPredictionSimulator), powerType, typeof(long));
                ParameterExpression simulator =
                    Expression.Parameter(typeof(CombatPredictionSimulator), "simulator");
                ParameterExpression power = Expression.Parameter(powerType, "power");
                Delegate typed = Expression.Lambda(
                    funcType,
                    Expression.Invoke(
                        Expression.Constant(read),
                        simulator,
                        Expression.Convert(power, typeof(PowerModel))),
                    simulator,
                    power).Compile();
                generic.MakeGenericMethod(powerType).Invoke(null, [stateName, typed]);
            };
        }
        catch (Exception ex)
        {
            EngineDiagnostics.Warn($"[AutoWatcher] 没能绑上 PowerHiddenStateMirrors.{name}：{ex.Message}");
            return Record<Action<Type, string, Func<CombatPredictionSimulator, PowerModel, long>>>(
                HiddenStateEntry, SolverEntryState.Faulted, null);
        }
    }

    /// <summary>绑 <c>PowerHiddenStateMirrors.RegisterRootCapture&lt;TPower&gt;</c>。</summary>
    private static Action<Type, Action<CombatPredictionSimulator, PowerModel, PowerModel>>?
        BindPowerHiddenRootCapture()
    {
        try
        {
            MethodInfo? generic = HiddenStateMirrors()?.GetMethod(
                "RegisterRootCapture", BindingFlags.Public | BindingFlags.Static);
            if (generic is null || !generic.IsGenericMethodDefinition)
                return null;
            return (powerType, capture) =>
            {
                Type actionType = typeof(Action<,,>).MakeGenericType(
                    typeof(CombatPredictionSimulator), powerType, powerType);
                ParameterExpression simulator =
                    Expression.Parameter(typeof(CombatPredictionSimulator), "simulator");
                ParameterExpression clone = Expression.Parameter(powerType, "clone");
                ParameterExpression original = Expression.Parameter(powerType, "original");
                Delegate typed = Expression.Lambda(
                    actionType,
                    Expression.Invoke(
                        Expression.Constant(capture),
                        simulator,
                        Expression.Convert(clone, typeof(PowerModel)),
                        Expression.Convert(original, typeof(PowerModel))),
                    simulator,
                    clone,
                    original).Compile();
                generic.MakeGenericMethod(powerType).Invoke(null, [typed]);
            };
        }
        catch (Exception ex)
        {
            EngineDiagnostics.Warn(
                $"[AutoWatcher] 没能绑上 PowerHiddenStateMirrors.RegisterRootCapture：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 绑 <c>SimulatedCombatState.&lt;name&gt;(LongTermGoals.FatalKillBonus)</c>。
    /// </summary>
    /// <remarks>
    /// <c>LongTermGoals</c> 编译时不可见，所以枚举值只能反射取、再作为常量嵌进表达式里。
    /// 任何一步取不到就返回 null，不抛——这条路径的前提就是"可能没有"。
    /// </remarks>
    private static Action<SimulatedCombatState>? BindLongTermGoal(string name)
    {
        try
        {
            string entry = $"SimulatedCombatState.{name}";
            Type? goals = typeof(SimulatedCombatState).Assembly
                .GetType("CombatSolver.LongTermGoals", throwOnError: false);
            // 这个枚举在上游从来没有存在过（翻遍全部提交历史），所以「没有」是常态，不是回归。
            if (goals is null)
                return Record<Action<SimulatedCombatState>>(entry, SolverEntryState.Absent, null);
            if (!goals.IsEnum)
                return Record<Action<SimulatedCombatState>>(entry, SolverEntryState.Mismatched, null);
            object? fatalKill = Enum.GetNames(goals).Contains("FatalKillBonus")
                ? Enum.Parse(goals, "FatalKillBonus")
                : null;
            if (fatalKill is null)
                return Record<Action<SimulatedCombatState>>(entry, SolverEntryState.Mismatched, null);
            MethodInfo? method = typeof(SimulatedCombatState).GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [goals],
                modifiers: null);
            // 枚举在、方法不在：上游动过这一块，不是「从来没做」。
            if (method is null)
                return Record<Action<SimulatedCombatState>>(entry, SolverEntryState.Mismatched, null);
            ParameterExpression combat = Expression.Parameter(typeof(SimulatedCombatState), "combat");
            return Record<Action<SimulatedCombatState>>(entry, SolverEntryState.Bound,
                Expression.Lambda<Action<SimulatedCombatState>>(
                    Expression.Call(combat, method, Expression.Constant(fatalKill, goals)),
                    combat).Compile());
        }
        catch (Exception ex)
        {
            // 绑不上不是错误，但要留痕：否则"开关对某张牌不起作用"会变成没人能解释的现象。
            EngineDiagnostics.Warn($"[AutoWatcher] 没能绑上 SimulatedCombatState.{name}：{ex.Message}");
            return Record<Action<SimulatedCombatState>>(
                $"SimulatedCombatState.{name}", SolverEntryState.Faulted, null);
        }
    }
}
