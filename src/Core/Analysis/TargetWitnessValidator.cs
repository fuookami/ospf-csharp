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
/// 校验目标见证解 / Validate a target witness.
/// </summary>
/// <remarks>
/// **求解器说"可行"不等于"可行"**：这一步拿解出来的解逐条复核，**只要有一步不过，那个"可行"就不能写进报告。**
/// 移植自 `TargetFeasibilityAnalyzer.validateTargetWitness`，五项检查按序执行。
///
/// **A solver saying "feasible" is not feasibility**: this re-checks the solution it returned, item by item, and **if any check fails the word "feasible" must not reach the
/// report.** Ported from `TargetFeasibilityAnalyzer.validateTargetWitness`; the five checks run in order.
/// </remarks>
public static class TargetWitnessValidator
{
    /// <summary>
    /// 校验一个见证解 / Validate a witness.
    /// </summary>
    /// <param name="snapshot">实际提交给后端的那份快照 / The snapshot actually sent to the backend.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="integerBound">已取整的边界 / The rounded bound.</param>
    /// <param name="solution">求解器返回的解 / The solution the solver returned.</param>
    /// <returns>不通过的原因；<c>null</c> 表示通过 / The reason it failed, or <c>null</c> when it is valid.</returns>
    /// <remarks>
    /// **校验的必须是提交给后端的那份快照**，而不是基线快照：冲突删除复验求解的是选择性重建的模型，
    /// 它的值域与激活约束都可能不同。**拿基线去校验,会把一个本来正确的解判成错的。**
    ///
    /// **It has to validate the snapshot that went to the backend**, not the baseline snapshot: a conflict deletion check solves a selectively rebuilt model whose domains and active
    /// constraints may differ, and **validating against the baseline snapshot would reject a solution that is actually right.**
    /// </remarks>
    public static string? Validate(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        Int64 integerBound,
        ConstraintProgrammingSolution solution) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(solution);

        IReadOnlyDictionary<VariableId, IntegerDomain> domains = snapshot.Variables
            .ToDictionary(definition => definition.Id, definition => definition.Domain);

        // 1. 解里出现快照没有的变量。
        // 1. The solution names a variable the snapshot does not have.
        // **`VariableId` 是结构体**，`FirstOrDefault` 在没有命中时返回**默认实例**而不是 `null`——
        // 直接判 `is not null` 会让这条检查永远成立。先投影成可空再取。
        // **`VariableId` is a struct**, so `FirstOrDefault` yields **the default instance** rather than `null` when nothing matches;
        // testing `is not null` directly would make this check fire every time. Project to nullable first.
        VariableId? unknown = solution.Values.Keys
            .Where(id => !domains.ContainsKey(id))
            .Select(id => (VariableId?)id)
            .FirstOrDefault();
        if (unknown is not null) {
            return $"见证解包含未知变量：{unknown.Value} / witness contains an unknown variable: {unknown.Value}";
        }

        // 2. 解里有值落在变量值域之外。
        // 2. A value falls outside its variable's domain.
        foreach (KeyValuePair<VariableId, Int64> pair in solution.Values) {
            if (!domains[pair.Key].Contains(pair.Value)) {
                return $"见证解超出变量值域：{pair.Key.Value} / witness is outside a variable domain: {pair.Key.Value}";
            }
        }

        // 3. 任何一条原始约束被违反，或者求不出值。
        // 3. Any original constraint is violated, or cannot be evaluated.
        foreach (ConstraintProgrammingConstraintSnapshot entry in snapshot.Constraints) {
            Result<bool, ErrorCode, Error<ErrorCode>> satisfied = entry.Constraint.IsSatisfied(solution.Values);
            if (satisfied is Failed<bool, ErrorCode, Error<ErrorCode>>) {
                return $"原始约束无法求值：{entry.Id.Value} / original constraint could not be evaluated: {entry.Id.Value}";
            }
            if (satisfied is not Ok<bool, ErrorCode, Error<ErrorCode>> okSatisfied || !okSatisfied.Value) {
                return $"见证解违反原始约束：{entry.Id.Value} / witness violates original constraint: {entry.Id.Value}";
            }
        }

        // 4. 目标表达式求不出值。
        // 4. The objective expression cannot be evaluated.
        ConstraintProgrammingObjectiveSnapshot? objective = snapshot.Objectives
            .FirstOrDefault(candidate => candidate.Id == target.ObjectiveId);
        if (objective is null) {
            return "目标引用了未注册 Objective / target references an unregistered objective";
        }
        Result<Int64, ErrorCode, Error<ErrorCode>> evaluated = objective.Expression.Evaluate(solution.Values);
        if (evaluated is not Ok<Int64, ErrorCode, Error<ErrorCode>> okValue) {
            return "目标表达式无法求值 / objective expression could not be evaluated";
        }

        // 5. 解存在，但**没到目标边界**。
        // 5. The solution exists but **does not reach the target bound**.
        Int64 exact = okValue.Value!;
        bool targetSatisfied = target.Relation == ObjectiveTargetRelation.AtLeast
            ? exact.CompareTo(integerBound) >= 0
            : exact.CompareTo(integerBound) <= 0;
        if (!targetSatisfied) {
            return "见证解不满足目标边界 / witness does not satisfy the target bound";
        }

        return null;
    }
}
