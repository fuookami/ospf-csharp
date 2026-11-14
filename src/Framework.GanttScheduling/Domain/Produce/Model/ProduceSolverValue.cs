#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>
/// 求解器数值转换扩展方法 / Solver value conversion extension methods
/// </summary>
public static class ProduceSolverValueExtensions {
    /// <summary>将域值转换为 solver Flt64 / Convert domain value to solver Flt64</summary>
    /// <remarks>
    /// **`Flt64` 不实现 `IConvertible`，`Convert.ToDouble` 会抛**：`V = Flt64` 是产能调度里最常见的调用，
    /// 于是这个助手在"本来就不用转换"的那条路径上正好炸掉。直接放行 `Flt64`，其余照旧。
    ///
    /// **`Flt64` does not implement `IConvertible`, so `Convert.ToDouble` throws** — and `V = Flt64` is the most common call in capacity scheduling, so the helper blew up exactly on the
    /// path that needed no conversion at all. `Flt64` now passes straight through and everything else behaves as before.
    /// </remarks>
    public static Flt64 ToSolverValue<V>(this V value) where V : struct, IRealNumber<V>
        => value is Flt64 direct ? direct : new Flt64(Convert.ToDouble(value));

    /// <summary>计算 MaterialDemand 的求解器数值范围下界 / Calculate solver value range lower bound for MaterialDemand</summary>
    public static Flt64 SolverValueRangeLower(this MaterialDemand demand)
        => demand.SolverLowerBound() - demand.SolverLessQuantity();

    /// <summary>计算 MaterialDemand 的求解器数值范围上界 / Calculate solver value range upper bound for MaterialDemand</summary>
    public static Flt64 SolverValueRangeUpper(this MaterialDemand demand)
        => demand.SolverUpperBound() + demand.SolverOverQuantity();

    /// <summary>计算 MaterialReserves 的求解器数值范围下界 / Calculate solver value range lower bound for MaterialReserves</summary>
    public static Flt64 SolverValueRangeLower(this MaterialReserves reserves)
        => reserves.SolverLowerBound() - reserves.SolverLessQuantity();

    /// <summary>计算 MaterialReserves 的求解器数值范围上界 / Calculate solver value range upper bound for MaterialReserves</summary>
    public static Flt64 SolverValueRangeUpper(this MaterialReserves reserves)
        => reserves.SolverUpperBound() + reserves.SolverOverQuantity();
}
