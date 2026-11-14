#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using GurobiCallback = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiLinearSolverCallBack;
using GurobiIdentity = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiBackendIdentity;
using GurobiSolver = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiLinearSolver;

namespace Fuookami.Ospf.Core.Plugin.Gurobi11;

/// <summary>
/// Gurobi 11 线性求解器兼容门面。
/// Gurobi 11 linear solver compatibility facade.
/// </summary>
/// <remarks>
/// 委托同一 Gurobi 原生实现，工程在 Gurobi 11 与 10 托管程序集之间选择。
/// Delegates to the shared Gurobi native implementation, whose project selects the Gurobi 11 or 10 assembly.
/// </remarks>
public sealed class Gurobi11LinearSolver : ILinearSolver {
    private readonly GurobiSolver _solver;

    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi11";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config => _solver.Config;

    /// <summary>创建 Gurobi 11 线性求解器 / Create a Gurobi 11 linear solver.</summary>
    public Gurobi11LinearSolver(
        SolverConfig? config = null,
        GurobiCallback? callBack = null) {
        _solver = new GurobiSolver(config, callBack, GurobiIdentity.Gurobi11);
    }

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        _solver.InvokeAsync(model, solvingStatusCallBack, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        _solver.InvokeReportAsync(model, solvingStatusCallBack, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) =>
        _solver.InvokeAsync(model, solvingStatusCallBack, iisConfig, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        _solver.InvokeAsync(model, solutionAmount, solvingStatusCallBack, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        _solver.InvokeReportAsync(model, solutionAmount, solvingStatusCallBack, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        _solver.DumpAsync(model, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) =>
        _solver.DumpAsync(
            model,
            registrationStatusCallBack,
            dumpingStatusCallBack,
            cancellationToken);
}
