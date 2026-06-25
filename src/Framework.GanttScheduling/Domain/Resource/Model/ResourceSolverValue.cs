#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// 资源求解器数值转换扩展方法 / Resource solver value conversion extension methods.
/// </summary>
public static class ResourceSolverValueExtensions {
    /// <summary>将域值转换为 solver Flt64 / Convert domain value to solver Flt64.</summary>
    public static Flt64 ToSolverValue<V>(this V value) where V : struct, IRealNumber<V>
        => new Flt64(Convert.ToDouble(value));

    /// <summary>计算资源容量的求解器数值范围下界 / Calculate solver value range lower bound.</summary>
    public static Flt64 SolverValueRangeLower<V>(this IAbstractResourceCapacity<V> capacity)
        where V : struct, IRealNumber<V>
        => capacity.SolverLowerBound() - capacity.SolverLessQuantity();

    /// <summary>计算资源容量的求解器数值范围上界 / Calculate solver value range upper bound.</summary>
    public static Flt64 SolverValueRangeUpper<V>(this IAbstractResourceCapacity<V> capacity)
        where V : struct, IRealNumber<V>
        => capacity.SolverUpperBound() + capacity.SolverOverQuantity();
}
