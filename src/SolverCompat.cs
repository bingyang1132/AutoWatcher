using System.Linq.Expressions;
using System.Reflection;
using CombatSolver;
using CombatSolver.Engine.Common;

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

    /// <summary>绑定的结果，写进加载日志，方便一眼看出装的是哪种求解器。</summary>
    public static string Summary =>
        RecordFatalKillGoal is null
            ? "跨战斗收益目标：求解器没有这个入口，已跳过"
            : "跨战斗收益目标：已绑定";

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
