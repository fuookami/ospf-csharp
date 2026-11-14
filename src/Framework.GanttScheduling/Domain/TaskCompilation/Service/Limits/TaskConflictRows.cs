#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using System.Linq;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 同一执行器上两个任务不能同时开工的行 / The row that keeps two tasks off one executor.
/// </summary>
/// <remarks>
/// 两个冲突约束（`TaskConflictConstraint` 的谓词版与 `TaskTimeConflictConstraint` 的时间相交版）生出的是**同一种行**：
/// `x[左, e] + x[右, e] &lt;= 1`。差别只在"哪些对被算作冲突"。抽成一处，免得两份实现慢慢分叉。
///
/// Both conflict constraints (the predicate version and the time-intersection version) emit the **same row**:
/// `x[left, e] + x[right, e] &lt;= 1`. Only the "which pairs conflict" test differs; keeping the row in one place stops the two copies drifting.
/// </remarks>
public static class TaskConflictRows {
    /// <summary>
    /// 按冲突谓词添加约束行 / Add the constraint rows for a conflict predicate.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type.</typeparam>
    /// <typeparam name="A">分配策略类型 / Assignment policy type.</typeparam>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="executors">执行器列表 / The executors.</param>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="compilation">编译结果 / The compilation.</param>
    /// <param name="conflict">冲突判定谓词 / The conflict predicate.</param>
    /// <param name="name">约束名前缀 / Constraint-name prefix.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 只走 `i &lt; j` 的**无序对**：同一对写两次是冗余，而"漏掉某一半"这种错在 `(i, j)` 与 `(j, i)` 两个循环里根本看不出来。
    ///
    /// Only the **unordered** pairs `i &lt; j` are visited: writing a pair twice is redundant, while a "missed half" bug is invisible in an `(i, j)`/`(j, i)` double loop.
    /// </remarks>
    public static Try Add<T, E, A>(
        IAbstractLinearMetaModel<Flt64> linear,
        IReadOnlyList<E> executors,
        IReadOnlyList<T> tasks,
        ICompilationColumns<E, A> compilation,
        Func<E, T, T, bool> conflict,
        string name)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        ArgumentNullException.ThrowIfNull(linear);
        ArgumentNullException.ThrowIfNull(conflict);

        var one = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One);
        foreach (E executor in executors) {
            for (int i = 0; i < tasks.Count; ++i) {
                for (int j = i + 1; j < tasks.Count; ++j) {
                    if (!conflict(executor, tasks[i], tasks[j])) {
                        continue;
                    }
                    if (!compilation.TaskAssignment.TryGetValue((tasks[i], executor), out object? left)
                        || left is not Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64> leftSymbol) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.ApplicationError,
                            "编译尚未注册，取不到任务的分配列：" + tasks[i].Id + "@" + executor.Id
                            + " / the compilation has not been registered, so the task's assignment column is unavailable: " + tasks[i].Id + "@" + executor.Id));
                    }
                    if (!compilation.TaskAssignment.TryGetValue((tasks[j], executor), out object? right)
                        || right is not Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64> rightSymbol) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.ApplicationError,
                            "编译尚未注册，取不到任务的分配列：" + tasks[j].Id + "@" + executor.Id
                            + " / the compilation has not been registered, so the task's assignment column is unavailable: " + tasks[j].Id + "@" + executor.Id));
                    }
                    LinearPolynomial<Flt64> sum = new(
                        Enumerable.ToArray(Enumerable.Concat(leftSymbol.Polynomial.Monomials, rightSymbol.Polynomial.Monomials)),
                        leftSymbol.Polynomial.Constant + rightSymbol.Polynomial.Constant);
                    Try added = linear.AddConstraint(
                        new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>(
                            sum,
                            one,
                            Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE,
                            name + "_" + tasks[i].Id + "_" + tasks[j].Id),
                        group: null,
                        name: name + "_" + tasks[i].Id + "_" + tasks[j].Id);
                    if (!added.IsOk) {
                        return added;
                    }
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
