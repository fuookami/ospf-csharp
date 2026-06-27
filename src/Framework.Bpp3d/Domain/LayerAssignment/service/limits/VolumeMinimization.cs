#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 体积最小化目标 / Volume minimization objective.
/// 最小化总使用体积 / Minimizes the total volume used.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class VolumeMinimization<V> : IBpp3dObjective<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>目标名称 / Objective name.</summary>
    public string Name => nameof(VolumeMinimization<V>);
}
