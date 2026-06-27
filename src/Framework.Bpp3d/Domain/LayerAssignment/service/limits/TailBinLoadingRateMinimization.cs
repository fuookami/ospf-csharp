#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 尾箱装载率最小化目标 / Tail bin loading rate minimization objective.
/// 最小化尾箱的装载率 / Minimizes the loading rate of the tail bin.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class TailBinLoadingRateMinimization<V> : IBpp3dObjective<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>目标名称 / Objective name.</summary>
    public string Name => nameof(TailBinLoadingRateMinimization<V>);
}
