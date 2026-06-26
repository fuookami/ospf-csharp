#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Inequality;
using System;

namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 不等式展平扩展方法 / Inequality flatten extension methods.
/// <para>将数学不等式直接展平为模型约束对象。</para>
/// <para>Flatten mathematical inequalities directly into model constraint objects.</para>
/// </summary>
public static class MathInequalityFlattenExtensions {
    /// <summary>
    /// 将线性不等式展平为模型约束。
    /// Flatten linear inequality into model constraint.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="inequality">线性不等式 / Linear inequality</param>
    /// <param name="group">约束组（可空）/ Constraint group (nullable)</param>
    /// <param name="priority">约束优先级 / Constraint priority</param>
    /// <returns>线性不等式约束 / Linear inequality constraint</returns>
    public static LinearInequalityConstraint<V> ToConstraint<V>(
        this LinearInequality<V> inequality,
        IMetaConstraintGroup? group = null,
        int priority = 0)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new(inequality, group, false, null, priority);

    /// <summary>
    /// 将二次不等式展平为模型约束。
    /// Flatten quadratic inequality into model constraint.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="inequality">二次不等式 / Quadratic inequality</param>
    /// <param name="group">约束组（可空）/ Constraint group (nullable)</param>
    /// <param name="priority">约束优先级 / Constraint priority</param>
    /// <returns>二次不等式约束 / Quadratic inequality constraint</returns>
    public static QuadraticInequalityConstraint<V> ToConstraint<V>(
        this QuadraticInequalityOf<V> inequality,
        IMetaConstraintGroup? group = null,
        int priority = 0)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new(inequality, group, false, null, priority);
}
