#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// IIS 计算进度状态，携带边界与约束的剩余量和总量。
/// IIS computing progress status, carrying remaining and total amounts for bounds and constraints.
/// </summary>
/// <param name="RestBoundAmount">剩余边界数量 / Remaining bound amount</param>
/// <param name="TotalBoundAmount">总边界数量 / Total bound amount</param>
/// <param name="RestConstraintAmount">剩余约束数量 / Remaining constraint amount</param>
/// <param name="TotalConstraintAmount">总约束数量 / Total constraint amount</param>
public sealed record IisComputingStatus(
    ulong RestBoundAmount,
    ulong TotalBoundAmount,
    ulong RestConstraintAmount,
    ulong TotalConstraintAmount) {
    /// <summary>边界计算进度 / Bound computation progress.</summary>
    public Flt64 BoundProgress => Ratio(RestBoundAmount, TotalBoundAmount);

    /// <summary>约束计算进度 / Constraint computation progress.</summary>
    public Flt64 ConstraintProgress => Ratio(RestConstraintAmount, TotalConstraintAmount);

    /// <summary>总进度 / Total progress.</summary>
    public Flt64 TotalProgress => Ratio(
        RestBoundAmount + RestConstraintAmount,
        TotalBoundAmount + TotalConstraintAmount);

    // 分母为零时保留 IEEE 的无穷或 NaN，不静默归零。 / A zero denominator keeps IEEE infinity or NaN instead of being silently zeroed.
    private static Flt64 Ratio(ulong rest, ulong total) => new((double)rest / (double)total);
}

/// <summary>
/// IIS 计算状态回调委托 / IIS computing status callback delegate.
/// </summary>
/// <param name="isFeasible">当前子系统是否可行 / Whether the current subsystem is feasible</param>
/// <param name="time">已用时间 / Elapsed time</param>
/// <param name="status">IIS 计算状态 / IIS computing status</param>
/// <returns>操作结果 / Operation result</returns>
public delegate Try IisComputingStatusCallBack(bool isFeasible, TimeSpan time, IisComputingStatus status);
