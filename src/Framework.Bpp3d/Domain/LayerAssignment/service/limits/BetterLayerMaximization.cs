#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 更优层最大化目标 / Better layer maximization objective.
/// 最大化层与箱子的匹配系数 / Maximizes layer-bin matching coefficients.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class BetterLayerMaximization<V> : IBpp3dObjective<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>目标名称 / Objective name.</summary>
    public string Name => nameof(BetterLayerMaximization<V>);

    /// <summary>层与箱子的系数函数 / Coefficient function for layer and bin.</summary>
    public Func<object, object, V>? Coefficient { get; init; }
}
