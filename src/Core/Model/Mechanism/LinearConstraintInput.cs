#nullable enable

using System;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 线性约束输入 / Linear constraint input.
/// <para>对应 Kotlin <c>LinearConstraintInput&lt;V&gt;</c>：携带已归一化（<c>lhs - rhs</c>）的扁平化表达式、
/// 比较符号、左侧值范围与归一化前的右侧常量，供满足数量、条件选择等函数符号做范围推导。</para>
/// <para>Mirrors Kotlin <c>LinearConstraintInput&lt;V&gt;</c>: it carries the flattened normalized expression
/// (<c>lhs - rhs</c>), the comparison sign, the left-hand side value range, and the pre-normalization
/// right-hand side constant, used by satisfied-count and conditional-selection function symbols.</para>
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
/// <param name="FlattenData">归一化后的扁平化数据 / Normalized flatten data</param>
/// <param name="Sign">比较符号 / Comparison sign</param>
/// <param name="LhsRange">左侧值范围 / Left-hand side value range</param>
/// <param name="Name">约束名称 / Constraint name</param>
/// <param name="DisplayName">约束显示名称（可空）/ Constraint display name (nullable)</param>
/// <param name="RhsConstant">归一化前的右侧常量 / Right-hand side constant before normalization</param>
public sealed record LinearConstraintInput<V>(
    SymbolFlatten.LinearFlattenData<V> FlattenData,
    Comparison Sign,
    ValueRange<V> LhsRange,
    string Name,
    string? DisplayName,
    V RhsConstant)
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 从线性不等式创建约束输入 / Create a constraint input from a linear inequality.
    /// </summary>
    /// <param name="relation">线性不等式 / Linear inequality</param>
    /// <param name="lhsRange">左侧值范围 / Left-hand side value range</param>
    /// <param name="rhsConstant">归一化前的右侧常量 / Right-hand side constant before normalization</param>
    /// <param name="name">约束名称 / Constraint name</param>
    /// <param name="displayName">约束显示名称（可空）/ Constraint display name (nullable)</param>
    /// <returns>约束输入或结构化失败 / Constraint input or a structured failure</returns>
    /// <remarks>
    /// 扁平输入不保留函数生命周期，因此拒绝函数符号；无法证明展开精确时返回失败。
    /// Flattened inputs cannot retain function registration and reject function symbols; unproven exact expansion fails.
    /// </remarks>
    public static Result<LinearConstraintInput<V>, ErrorCode, Error<ErrorCode>> From(
        LinearInequality<V>? relation,
        ValueRange<V> lhsRange,
        V rhsConstant,
        string name = "",
        string? displayName = null) {
        try {
            if (relation?.Lhs?.Monomials is null || relation.Rhs?.Monomials is null || lhsRange is null) {
                return Invalid();
            }
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened =
                relation.ToLinearFlattenData(
                    tokens: null,
                    allowUnregisteredFunctions: false
                );
            if (flattened is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> failed) {
                return new Failed<LinearConstraintInput<V>, ErrorCode, Error<ErrorCode>>(failed.Error);
            }
            if (flattened is Fatal<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> fatal) {
                return new Fatal<LinearConstraintInput<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
            }
            if (!ExactLinearExpansion.MatchesDifference(relation, flattened.Value)) {
                return Invalid();
            }
            return Results.Ok(new LinearConstraintInput<V>(
                FlattenData: flattened.Value,
                Sign: relation.Comparison,
                LhsRange: lhsRange,
                Name: name,
                DisplayName: displayName,
                RhsConstant: rhsConstant
            ));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid();
        }
    }

    private static Result<LinearConstraintInput<V>, ErrorCode, Error<ErrorCode>> Invalid() =>
        Results.Failed<LinearConstraintInput<V>>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "约束输入无效或展开结果不精确。 / Invalid constraint input or inexact expansion."
        ));
}


