#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Functional;
using System;

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 机制模型割平面支持扩展 / Mechanism model cut support extensions for Benders decomposition.
/// <para>向线性机制模型便捷地添加割平面约束，用于 Benders 分解迭代。</para>
/// <para>Convenience methods for adding cut constraints to a linear mechanism model,
/// used during Benders decomposition iterations.</para>
/// </summary>
public static class MechanismModelCutExtensions {
    /// <summary>
    /// 向线性机制模型添加割平面约束。
    /// Add cut constraint to linear mechanism model.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="model">线性机制模型 / Linear mechanism model</param>
    /// <param name="cut">割平面不等式 / Cut inequality</param>
    /// <param name="group">约束组（可空）/ Constraint group (nullable)</param>
    /// <param name="name">约束名称（可空）/ Constraint name (nullable)</param>
    /// <returns>操作结果 / Operation result</returns>
    public static Try AddCut<V>(
        this IAbstractLinearMechanismModel<V> model,
        LinearInequality<V> cut,
        IMetaConstraintGroup? group = null,
        string? name = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        return model.AddConstraint(cut, name, from: ((Fuookami.Ospf.Core.Symbol.IIntermediateSymbol Symbol, bool Direction)?)null);
    }
}
