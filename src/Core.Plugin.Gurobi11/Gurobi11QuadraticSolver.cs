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

using GurobiCallback = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiQuadraticSolverCallBack;
using GurobiIdentity = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiBackendIdentity;
using GurobiSolver = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiQuadraticSolver;

namespace Fuookami.Ospf.Core.Plugin.Gurobi11;

/// <summary>
/// Gurobi 11 二次求解器兼容门面。
/// Gurobi 11 quadratic solver compatibility facade.
/// </summary>
public sealed class Gurobi11QuadraticSolver : IQuadraticSolver {
    private readonly GurobiSolver _solver;

    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi11";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config => _solver.Config;

    /// <summary>创建 Gurobi 11 二次求解器 / Create a Gurobi 11 quadratic solver.</summary>
    public Gurobi11QuadraticSolver(
        SolverConfig? config = null,
        GurobiCallback? callBack = null) {
        _solver = new GurobiSolver(config, callBack, GurobiIdentity.Gurobi11);
    }

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        _solver.InvokeAsync(model, solvingStatusCallBack, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        _solver.InvokeReportAsync(model, solvingStatusCallBack, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default) =>
        _solver.InvokeAsync(model, solvingStatusCallBack, iisConfig, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        _solver.InvokeAsync(model, solutionAmount, solvingStatusCallBack, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        _solver.InvokeReportAsync(model, solutionAmount, solvingStatusCallBack, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        _solver.DumpAsync(model, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) =>
        _solver.DumpAsync(
            model,
            registrationStatusCallBack,
            dumpingStatusCallBack,
            cancellationToken);
}
