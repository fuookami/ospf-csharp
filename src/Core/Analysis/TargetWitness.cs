#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 见证解的自校验 / Self-validation of a witness solution.
/// </summary>
/// <remarks>
/// **这是 `TargetFeasibilityAnalyzer.validateTargetWitness` 的移植，也是"可达"这个结论唯一的地基。**
///
/// 求解器说"我给你找到了一个解"。**这个解可能不属于这个模型、可能越界、可能违反原始约束、
/// 也可能根本不满足目标。** 任何一条不查，报出去的 `Reachable` 就是一个没验过的断言。
///
/// **This is the implementation of `validateTargetWitness`, and the only ground under the word "reachable".** The solver says "I found a solution". **It may belong to another model, be out of domain, violate an
/// original constraint, or simply not meet the target.** Skip any one check and the `Reachable` you report is an assertion nobody verified.
///
/// **五道检查，顺序固定：**
///
/// | 序 | 查什么 | 为什么排在这里 |
/// | --- | --- | --- |
/// | 1 | 未知变量 | **解可能来自另一个模型**，后面每条都会因此误判 |
/// | 2 | 越界 | 越界值让约束求值失去意义 |
/// | 3 | **原始约束** | 违反约束的解不是解 |
/// | 4 | 目标求值 | 前三条过了才谈得上"目标值是多少" |
/// | 5 | **目标边界** | 求得出值，还要比得上界 |
///
/// **第 5 道的方向最容易写反：`AtLeast` 要"大于等于"，`AtMost` 要"小于等于"。**
/// **反了就等于"目标不满足也报可达"——这恰好是这个类唯一不能犯的错。**
///
/// **Gate 5's direction is the easiest to invert.** **Inverted, it means "report reachable even when the target is not met" — precisely the one error this class cannot make.**
/// </remarks>
public static class TargetWitness
{
    /// <summary>
    /// 校验一个见证解 / Validate a witness solution.
    /// </summary>
    /// <param name="snapshot">**实际提交给后端的那份**快照 / **The snapshot actually sent to the backend**.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="integerBound">目标的整数边界 / The target's integer bound.</param>
    /// <param name="solution">求解器报出的解 / The solution the solver reported.</param>
    /// <returns>问题描述，或 <c>null</c> 表示通过 / The problem, or <c>null</c> when it holds up.</returns>
    /// <remarks>
    /// **`snapshot` 必须是求解时那份，不是重建的那份。** 删除复验求解的是选择性重建的模型，
    /// 它的值域和激活约束与原来不同——**拿原来的快照校验重建模型的解，会把合法的解判成越界。**
    ///
    /// **`snapshot` has to be the one that was solved, not the rebuilt one.** Deletion checks solve a selectively rebuilt model whose domains and active constraints differ from the original — **validating
    /// the rebuilt model's solution against the original reports a legitimate solution as out of domain.**
    /// </remarks>
    public static string? Validate(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        Int64 integerBound,
        ConstraintProgrammingSolution solution) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(solution);

        IReadOnlyDictionary<VariableId, Int64> values = solution.Values;
        IReadOnlyDictionary<VariableId, ConstraintProgrammingVariableSnapshot> variables =
            snapshot.Variables.ToDictionary(variable => variable.Id);

        VariableId? unknown = values.Keys
            .Where(id => !variables.ContainsKey(id))
            .Select(id => (VariableId?)id)
            .FirstOrDefault();
        if (unknown is { } unknownId) {
            return "见证解包含未知变量 / witness contains an unknown variable: " + unknownId.Value;
        }

        VariableId? outside = values
            .Where(entry => !variables[entry.Key].Domain.Contains(entry.Value))
            .Select(entry => (VariableId?)entry.Key)
            .FirstOrDefault();
        if (outside is { } outsideId) {
            return "见证解超出变量值域 / witness is outside a variable domain: " + outsideId.Value;
        }

        foreach (ConstraintProgrammingConstraintSnapshot constraint in snapshot.Constraints) {
            Result<bool, ErrorCode, Error<ErrorCode>> satisfied = constraint.Constraint.IsSatisfied(values);
            if (satisfied is not Ok<bool, ErrorCode, Error<ErrorCode>> ok) {
                return "原始约束无法求值 / original constraint could not be evaluated: " + constraint.Id.Value;
            }
            if (!ok.Value) {
                return "见证解违反原始约束 / witness violates original constraint: " + constraint.Id.Value;
            }
        }

        ConstraintProgrammingObjectiveSnapshot? objective = snapshot.Objective(target.ObjectiveId);
        if (objective is null) {
            return "目标引用了未注册 Objective / target references an unregistered objective";
        }

        Result<Int64, ErrorCode, Error<ErrorCode>> evaluated = objective.Expression.Evaluate(values);
        if (evaluated is not Ok<Int64, ErrorCode, Error<ErrorCode>> exactResult) {
            return "目标表达式无法求值 / objective expression could not be evaluated";
        }

        long exact = exactResult.Value.ToLong();
        long bound = integerBound.ToLong();

        bool satisfiedTarget = target.Relation switch
        {
            ObjectiveTargetRelation.AtLeast => exact >= bound,
            ObjectiveTargetRelation.AtMost => exact <= bound,
            _ => false,
        };

        return satisfiedTarget ? null : "见证解不满足目标边界 / witness does not satisfy the target bound";
    }
}
