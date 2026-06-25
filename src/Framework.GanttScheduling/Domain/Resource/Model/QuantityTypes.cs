#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// 资源数量类型工具方法 / Resource quantity type utility methods.
/// </summary>
public static class ResourceQuantityTypes {
    /// <summary>计算资源容量的求解器下界 / Calculate solver lower bound for resource capacity.</summary>
    public static Flt64 SolverLowerBound<V>(V lowerBound) where V : struct, IRealNumber<V>
        => new Flt64(Convert.ToDouble(lowerBound));

    /// <summary>计算资源容量的求解器上界 / Calculate solver upper bound for resource capacity.</summary>
    public static Flt64 SolverUpperBound<V>(V upperBound) where V : struct, IRealNumber<V>
        => new Flt64(Convert.ToDouble(upperBound));
}
