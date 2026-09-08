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
internal static class SolverCompat
{
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
    /// 声明一张牌属于哪一类起手牌，进求解器的移除估值。参数是牌的类型和类别名
    /// （<c>"Strike"</c> 或 <c>"Defend"</c>）。绑不上说明求解器还没有这个入口，那时候观者的
    /// 打击、防御按通用估值算成有伤害/格挡的普通牌，净化不会优先烧它们。
    /// </summary>
    public static readonly Action<Type, string>? RegisterBasicCardRemoval = BindBasicCardRemoval();

    /// <summary>绑定的结果，写进加载日志，方便一眼看出装的是哪种求解器。</summary>
    public static string Summary =>
        (RecordFatalKillGoal is null
            ? "跨战斗收益目标：求解器没有这个入口，已跳过"
            : "跨战斗收益目标：已绑定")
        + (RegisterPowerHiddenState is null
            ? "。Power 隐藏状态进指纹：求解器没有这个入口，光辉与天人形态改记风险"
            : "。Power 隐藏状态进指纹：已绑定")
        + (GrowthSourceRegister is null
            ? "。局外成长来源登记：求解器没有这个入口，勤学精进与许愿的金币只走长期资源刻度"
            : "。局外成长来源登记：已绑定")
        + (RegisterBasicCardRemoval is null
            ? "。起手牌移除估值：求解器没有这个入口，净化不会优先烧打击防御"
            : "。起手牌移除估值：已绑定");

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
            object? handle = GrowthSourceRegister.Invoke(null, [id, card, hasTarget, title]);
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
    private static MethodInfo? BindGrowthSourceRegister()
    {
        try
        {
            MethodInfo? register = typeof(SimulatedCombatState).Assembly
                .GetType("CombatSolver.GrowthSourceMirrors", throwOnError: false)
                ?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            return register?.GetParameters().Length == 4 ? register : null;
        }
        catch (Exception ex)
        {
            EngineDiagnostics.Warn($"[AutoWatcher] 没能绑上 GrowthSourceMirrors.Register：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 绑 <c>CardRemovalValueMirrors.Register&lt;TCard&gt;(BasicCardRemovalKind)</c>。
    /// </summary>
    /// <remarks>
    /// 方法是泛型的、参数是编译时不可见的枚举，所以两样都反射取：类型参数每次现拼，枚举值按
    /// 名字解析成常量。登记只在加载时发生两次，反射的代价无所谓。
    /// </remarks>
    private static Action<Type, string>? BindBasicCardRemoval()
    {
        try
        {
            Assembly solver = typeof(SimulatedCombatState).Assembly;
            MethodInfo? generic = solver
                .GetType("CombatSolver.CardRemovalValueMirrors", throwOnError: false)
                ?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            Type? kindType = solver.GetType("CombatSolver.BasicCardRemovalKind", throwOnError: false);
            if (generic is null || !generic.IsGenericMethodDefinition || kindType is null || !kindType.IsEnum)
                return null;
            return (cardType, kindName) =>
            {
                if (!Enum.GetNames(kindType).Contains(kindName))
                {
                    EngineDiagnostics.Warn(
                        $"[AutoWatcher] 求解器的起手牌类别里没有 {kindName}，{cardType.Name} 没登记。");
                    return;
                }
                generic.MakeGenericMethod(cardType).Invoke(null, [Enum.Parse(kindType, kindName)]);
            };
        }
        catch (Exception ex)
        {
            EngineDiagnostics.Warn($"[AutoWatcher] 没能绑上 CardRemovalValueMirrors.Register：{ex.Message}");
            return null;
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
            if (generic is null || !generic.IsGenericMethodDefinition)
                return null;
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
            return null;
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
            Type? goals = typeof(SimulatedCombatState).Assembly
                .GetType("CombatSolver.LongTermGoals", throwOnError: false);
            if (goals is null || !goals.IsEnum)
                return null;
            object? fatalKill = Enum.GetNames(goals).Contains("FatalKillBonus")
                ? Enum.Parse(goals, "FatalKillBonus")
                : null;
            if (fatalKill is null)
                return null;
            MethodInfo? method = typeof(SimulatedCombatState).GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [goals],
                modifiers: null);
            if (method is null)
                return null;
            ParameterExpression combat = Expression.Parameter(typeof(SimulatedCombatState), "combat");
            return Expression.Lambda<Action<SimulatedCombatState>>(
                Expression.Call(combat, method, Expression.Constant(fatalKill, goals)),
                combat).Compile();
        }
        catch (Exception ex)
        {
            // 绑不上不是错误，但要留痕：否则"开关对某张牌不起作用"会变成没人能解释的现象。
            EngineDiagnostics.Warn($"[AutoWatcher] 没能绑上 SimulatedCombatState.{name}：{ex.Message}");
            return null;
        }
    }
}
