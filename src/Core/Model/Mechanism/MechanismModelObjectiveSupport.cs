#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 机制模型目标函数支持扩展 / Mechanism model objective support extensions.
/// <para>提供从单目标机制模型中便捷提取目标值的辅助方法。</para>
/// <para>Provides convenience helpers for extracting objective values
/// from single-objective mechanism models.</para>
/// </summary>
public static class MechanismModelObjectiveExtensions {
    /// <summary>
    /// 获取单目标模型的目标值。
    /// Get objective value from single-objective model.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="model">单目标机制模型 / Single-objective mechanism model</param>
    /// <returns>目标值；未知或类型不匹配时为 null / Objective value, or null if unknown or mismatched.</returns>
    /// <remarks>单个子目标使用自身方向，多个子目标相对于模型方向相加或相减；空目标需要已注册的数值零。
    /// A single part uses its own direction; multiple parts use the model direction; an empty objective requires a registered numeric zero.</remarks>
    public static V? GetObjectiveValue<V>(this ISingleObjectMechanismModel<V> model)
        where V : struct, IRealNumber<V>, INumberField<V> {
        IReadOnlyList<object> parts = model.ObjectFunction.SubObjects;
        if (parts.Count == 0) {
            return NumericConstantsRegistry.ForOrNull<V>()?.Zero;
        }
        if (parts[0] is not SubObject<V> first) {
            return null;
        }

        ObjectCategory direction = parts.Count == 1 ? first.Category : model.ObjectFunction.Category;
        V? sum = null;
        foreach (object part in parts) {
            if (part is not SubObject<V> objective || objective.Evaluate() is not { } value) {
                return null;
            }
            V signedValue = objective.Category == direction ? value : value.Negate();
            sum = sum is { } current ? current.Plus(signedValue) : signedValue;
        }
        return sum;
    }
}
