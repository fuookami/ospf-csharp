#nullable enable

using System.Diagnostics;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 不支持特性通知工具，记录求解器不支持的功能警告。
/// Unsupported feature notice utility, logging warnings for features not supported by the solver.
/// </summary>
public static class UnsupportedFeatureNotice {
    /// <summary>
    /// 记录约束优先级被忽略的警告。
    /// Log a warning that constraint priorities are being ignored.
    /// </summary>
    /// <param name="solverName">求解器名称 / Solver name</param>
    /// <param name="priorityAmount">被忽略的约束优先级数量 / Number of ignored constraint priorities</param>
    public static void WarnIgnoredConstraintPriority(string solverName, int priorityAmount) {
        Trace.TraceWarning(
            $"Solver '{solverName}' does not support constraint priorities; {priorityAmount} priority value(s) will be ignored.");
    }
}
