#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 剩余量最小化目标 / Rest amount minimization objective.
/// 最小化未分配的剩余量 / Minimizes the unassigned rest amount.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class RestAmountMinimization<V> : IBpp3dObjective<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>目标名称 / Objective name.</summary>
    public string Name => nameof(RestAmountMinimization<V>);
}
