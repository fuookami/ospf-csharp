#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using System;
using System.Collections.Generic;
using System.Linq;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 求解器未知输出里的见证解 / The witness carried by a solver's "unknown" output.
/// </summary>
/// <remarks>
/// **未知输出可以带回一个候选解**，而它做一件很有分寸的事：
/// **候选解若通过完整校验，就把状态从 `Unknown` 提升为 `Reachable`。**
///
/// **An unknown output may carry a candidate solution**, and the code does something careful with it: **if the candidate passes full validation, the status is promoted from `Unknown` to `Reachable`.**
///
/// **两个方向都不能松：**
///
/// | 情况 | 状态 | 解 |
/// | --- | --- | --- |
/// | 校验通过 | **`Reachable`** | 保留 |
/// | 校验不过 / 没有候选 | `Unknown` | **丢弃** |
///
/// **丢弃是重点。** 一份没通过校验的解留在报告里，**下游会把它当成"虽然未知但总归是个解"。**
///
/// **Dropping it is the point.** A solution that failed validation left in the report **gets read downstream as "unknown, but at least it is a solution".**
/// </remarks>
public static class UnknownOutputWitness
{
    /// <summary>
    /// 把位置对应的取值列表映射成解 / Map a positionally-ordered value list into a solution.
    /// </summary>
    /// <param name="snapshot">**求解时那份**快照 / **The snapshot that was solved**.</param>
    /// <param name="values">按变量位置排列的取值 / The values in variable order.</param>
    /// <returns>解，或 <c>null</c> 表示没有候选 / The solution, or <c>null</c> when there is no candidate.</returns>
    /// <remarks>
    /// **求解器给的是一个没有变量名的取值列表，映射只能靠位置。**
    /// 位置对不上时**每种结果都是错的**，而错法有两种：
    ///
    /// | 情况 | 这里的行为 | 后果 |
    /// | --- | --- | --- |
    /// | 列表更短 | **少映射几个变量** | 后面报"缺少变量赋值"——**原因是对的** |
    /// | 列表更长 | **多余的值被忽略** | 不抛异常，**但也不假装它们属于这个模型** |
    ///
    /// **A shorter list maps fewer variables**, which the later checks report as missing assignments — **the right reason**. **A longer list has its extras ignored**: no exception, **and no pretending they belong to this model**.
    ///
    /// **不要在这里抛异常。** 位置对不上是求解器输出与模型不匹配，**是一个要报告的结果，不是一个要崩溃的瞬间。**
    ///
    /// **Nothing throws here.** A positional mismatch means the solver's output does not match the model — **a result to report, not a moment to crash.**
    /// </remarks>
    public static ConstraintProgrammingSolution? MapPositional(
        ConstraintProgrammingModelSnapshot snapshot,
        IReadOnlyList<Int64> values) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0) {
            return null;
        }

        var mapped = new Dictionary<VariableId, Int64>();
        int index = 0;
        foreach (ConstraintProgrammingVariableSnapshot variable in snapshot.Variables) {
            if (index >= values.Count) {
                break;
            }
            mapped[variable.Id] = values[index];
            ++index;
        }

        return mapped.Count == 0 ? null : new ConstraintProgrammingSolution(mapped);
    }

    /// <summary>
    /// 未知输出 + 候选解 → 最终状态 / An unknown output plus a candidate → the final status.
    /// </summary>
    /// <param name="snapshot">**求解时那份**快照 / **The snapshot that was solved**.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="integerBound">目标的整数边界 / The target's integer bound.</param>
    /// <param name="values">求解器带回的取值，可为 <c>null</c> / The values the solver carried, or <c>null</c>.</param>
    /// <returns>提升结果 / The promotion outcome.</returns>
    /// <remarks>
    /// **提升只在完整校验通过时发生。** 校验包含原始约束与目标边界——
    /// **少了任何一条，"可达"就成了"求解器给了个数"。**
    ///
    /// **The promotion happens only on a full validation pass**, which includes the original constraints and the target bound — **skip either and "reachable" becomes "the solver gave a number".**
    /// </remarks>
    public static UnknownWitnessResult Promote(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        Int64 integerBound,
        IReadOnlyList<Int64>? values) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);

        ConstraintProgrammingSolution? candidate = values is null ? null : MapPositional(snapshot, values);
        if (candidate is null) {
            return new UnknownWitnessResult(AnalysisStatus.Unknown, null, null);
        }

        if (TargetWitness.Validate(snapshot, target, integerBound, candidate) is not null) {
            // **校验不过就丢掉。** 留一份没通过校验的解，下游会读成"虽然未知但总归是个解"。
            // **A failed candidate is dropped.** Leaving it read downstream as "unknown, but at least it is a solution".
            return new UnknownWitnessResult(AnalysisStatus.Unknown, null, null);
        }

        Int64? exact = snapshot.Objective(target.ObjectiveId)?.Expression.Evaluate(candidate.Values)
            is Fuookami.Ospf.Utils.Functional.Ok<Int64, ErrorCode, Error<ErrorCode>> ok
                ? ok.Value
                : null;

        return new UnknownWitnessResult(AnalysisStatus.Reachable, candidate, exact);
    }
}

/// <summary>
/// 未知输出的提升结果 / The outcome of promoting an unknown output.
/// </summary>
/// <param name="Status">最终状态 / The final status.</param>
/// <param name="Solution">保留的解，未提升时为 <c>null</c> / The kept solution, or <c>null</c> when not promoted.</param>
/// <param name="ExactObjective">精确目标值 / The exact objective value.</param>
public readonly record struct UnknownWitnessResult(
    AnalysisStatus Status,
    ConstraintProgrammingSolution? Solution,
    Int64? ExactObjective)
{
    /// <summary>是否提升成了 `Reachable` / Whether it was promoted to Reachable.</summary>
    public bool Promoted => Status == AnalysisStatus.Reachable && Solution is not null;
}
