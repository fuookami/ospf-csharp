#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 箱数量最小化目标 / Bin amount minimization objective.
/// 最小化使用的箱子数量 / Minimizes the number of bins used.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class BinAmountMinimization<V> : IBpp3dObjective<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>目标名称 / Objective name.</summary>
    public string Name => nameof(BinAmountMinimization<V>);
}
