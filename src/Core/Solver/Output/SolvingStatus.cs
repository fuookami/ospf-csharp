#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Output
{
    /// <summary>
    /// 求解过程状态快照 / Solving process status snapshot.
    /// </summary>
    /// <param name="Obj">当前目标值 / Current objective value</param>
    /// <param name="PossibleBestObj">可能的最优目标值 / Possible best objective value</param>
    /// <param name="Gap">当前间隙 / Current gap</param>
    /// <param name="Solver">求解器名称 / Solver name</param>
    /// <param name="SolverIndex">求解器索引 / Solver index</param>
    /// <param name="Status">求解器状态 / Solver status</param>
    public sealed record SolvingStatus(
        Flt64 Obj,
        Flt64 PossibleBestObj,
        Flt64 Gap,
        string Solver,
        int SolverIndex,
        SolverStatus Status);

    /// <summary>
    /// 求解状态回调委托 / Solving status callback delegate.
    /// </summary>
    /// <param name="status">求解状态 / Solving status</param>
    /// <returns>操作结果 / Operation result</returns>
    public delegate Try SolvingStatusCallBack(SolvingStatus status);
}
