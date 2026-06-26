#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using System;

namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 线性约束输入封装 / Linear constraint input wrapper.
/// <para>将线性不等式与约束元数据打包为一个不可变记录，便于批量注册约束。</para>
/// <para>Packs a linear inequality together with constraint metadata into an immutable
/// record, facilitating batch constraint registration.</para>
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
/// <param name="Inequality">线性不等式 / Linear inequality</param>
/// <param name="Group">约束组（可空）/ Constraint group (nullable)</param>
/// <param name="Lazy">是否惰性求值 / Whether to lazily evaluate</param>
/// <param name="Name">约束名称（可空）/ Constraint name (nullable)</param>
/// <param name="DisplayName">约束显示名称（可空）/ Constraint display name (nullable)</param>
/// <param name="Args">附加参数（可空）/ Additional arguments (nullable)</param>
/// <param name="Priority">约束优先级 / Constraint priority</param>
/// <param name="WithRangeSet">是否使用范围集（可空）/ Whether to use range set (nullable)</param>
public sealed record LinearConstraintInput<V>(
    LinearInequality<V> Inequality,
    IMetaConstraintGroup? Group = null,
    bool Lazy = false,
    string? Name = null,
    string? DisplayName = null,
    object? Args = null,
    int Priority = 0,
    bool? WithRangeSet = null)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// Flt64 线性约束输入别名 / Flt64 linear constraint input alias.
/// <para>以 Flt64 为值类型的线性约束输入特化。</para>
/// <para>Specialization of linear constraint input with Flt64 as the value type.</para>
/// </summary>
/// <param name="Inequality">线性不等式 / Linear inequality</param>
/// <param name="Group">约束组（可空）/ Constraint group (nullable)</param>
/// <param name="Lazy">是否惰性求值 / Whether to lazily evaluate</param>
/// <param name="Name">约束名称（可空）/ Constraint name (nullable)</param>
/// <param name="DisplayName">约束显示名称（可空）/ Constraint display name (nullable)</param>
/// <param name="Args">附加参数（可空）/ Additional arguments (nullable)</param>
/// <param name="Priority">约束优先级 / Constraint priority</param>
/// <param name="WithRangeSet">是否使用范围集（可空）/ Whether to use range set (nullable)</param>
public sealed record Flt64LinearConstraintInput(
    LinearInequality<Flt64> Inequality,
    IMetaConstraintGroup? Group = null,
    bool Lazy = false,
    string? Name = null,
    string? DisplayName = null,
    object? Args = null,
    int Priority = 0,
    bool? WithRangeSet = null);

/// <summary>
/// 线性约束输入扩展方法 / Linear constraint input extension methods.
/// </summary>
public static class LinearConstraintInputExtensions {
    /// <summary>
    /// 将 Flt64 约束输入转换为 Flt64 约束输入（身份转换，保持 API 对称性）。
    /// Convert Flt64 constraint input to Flt64 constraint input (identity, for API symmetry).
    /// </summary>
    /// <param name="input">Flt64 约束输入 / Flt64 constraint input</param>
    /// <returns>Flt64 约束输入 / Flt64 constraint input</returns>
    public static LinearConstraintInput<Flt64> ToLinearConstraintInput(
        this Flt64LinearConstraintInput input) {
        return new LinearConstraintInput<Flt64>(
            input.Inequality, input.Group, input.Lazy,
            input.Name, input.DisplayName, input.Args,
            input.Priority, input.WithRangeSet);
    }
}
