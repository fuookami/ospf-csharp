#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Output;
/// <summary>
/// 求解过程状态快照 / Solving process status snapshot.
/// </summary>
/// <param name="Solver">求解器名称 / Solver name</param>
/// <param name="SolverIndex">求解器索引 / Solver index</param>
/// <param name="SolverConfig">求解器配置 / Solver configuration</param>
/// <param name="IntermediateModel">中间模型视图（可选）/ Intermediate model view (optional)</param>
/// <param name="SolverModel">求解器模型（可选）/ Solver model (optional)</param>
/// <param name="SolverCallBack">求解器回调（可选）/ Solver callback (optional)</param>
/// <param name="ObjectCategory">目标类别（可选）/ Object category (optional)</param>
/// <param name="Time">当前时间 / Current time</param>
/// <param name="Obj">当前目标值 / Current objective value</param>
/// <param name="PossibleBestObj">可能的最优目标值 / Possible best objective value</param>
/// <param name="InitialBestObj">初始最优目标值 / Initial best objective value</param>
/// <param name="Gap">当前间隙 / Current gap</param>
/// <param name="CurrentBestSolution">当前最优解（可选）/ Current best solution (optional)</param>
/// <param name="Iterations">迭代次数（可选）/ Iteration count (optional)</param>
/// <param name="NodeCount">节点数（可选）/ Node count (optional)</param>
/// <param name="BestBound">最优界（可选）/ Best bound (optional)</param>
/// <param name="MipGap">MIP 间隙 / MIP gap</param>
/// <param name="SolveTime">求解时间 / Solve time</param>
public sealed record SolvingStatus(
    string Solver,
    int SolverIndex,
    SolverConfig SolverConfig,
    object? IntermediateModel = null,
    object? SolverModel = null,
    object? SolverCallBack = null,
    Fuookami.Ospf.Core.Model.Basic.ObjectCategory? ObjectCategory = null,
    TimeSpan Time = default,
    Flt64 Obj = default,
    Flt64 PossibleBestObj = default,
    Flt64 InitialBestObj = default,
    Flt64 Gap = default,
    IReadOnlyList<Flt64>? CurrentBestSolution = null,
    ulong? Iterations = null,
    ulong? NodeCount = null,
    Flt64? BestBound = null,
    Flt64 MipGap = default,
    TimeSpan SolveTime = default);

/// <summary>
/// 求解状态回调委托 / Solving status callback delegate.
/// </summary>
/// <param name="status">求解状态 / Solving status</param>
/// <returns>操作结果 / Operation result</returns>
public delegate Try SolvingStatusCallBack(SolvingStatus status);
