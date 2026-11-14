#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

namespace Fuookami.Ospf.Core.Plugin.Scip;

/// <summary>
/// SCIP 二次求解器 / SCIP quadratic solver.
/// </summary>
public sealed class ScipQuadraticSolver : IQuadraticSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "scip";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    public ScipQuadraticSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        ScipLinearSolver.ToLegacy(await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        InvokeReportCore(
            model: model,
            solutionAmount: 0,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        InvokeReportCore(
            model: model,
            solutionAmount: solutionAmount,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    private Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportCore(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromResult(Results.Ok(ScipLinearNativeSolver.Cancelled()));
        }
        if (solutionAmount > int.MaxValue) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "解池容量超出 SCIP 整数范围 / Solution pool capacity exceeds the SCIP integer range")));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> validation = Config.ValidateRuntime();
        if (validation is Failed<Success, ErrorCode, Error<ErrorCode>> invalid) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(invalid.Error));
        }
        if (validation is Fatal<Success, ErrorCode, Error<ErrorCode>> invalidMany) {
            return Task.FromResult<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(
                new Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>(invalidMany.Errors));
        }
        IBackendConfiguration? backend = Config.BackendConfiguration;
        if (backend is not null and not ScipSolverConfig) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"SCIP 求解器不能使用 {backend.Type} backend 配置 / " +
                    $"SCIP solver cannot use backend configuration {backend.Type}")));
        }
        if (backend is ScipSolverConfig { Deterministic: true } && Config.NativeThreadCount > 1) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "SCIP deterministic 配置必须使用单线程 / SCIP deterministic configuration requires one thread")));
        }
        if (backend is ScipSolverConfig { RandomSeed: { } seed } && (seed < int.MinValue || seed > int.MaxValue)) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "SCIP randomSeed 超出原生整数范围 / SCIP randomSeed exceeds the native integer range")));
        }
        Result<ScipQuadraticSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            ScipQuadraticSnapshot.Capture(model, cancellationToken);
        if (snapshot is Failed<ScipQuadraticSnapshot, ErrorCode, Error<ErrorCode>> { Code: ErrorCode.ApplicationStopped }
            && cancellationToken.IsCancellationRequested) {
            return Task.FromResult(Results.Ok(ScipLinearNativeSolver.Cancelled()));
        }
        return Task.FromResult(snapshot.Bind(value => ScipQuadraticNativeSolver.Solve(
            model: value,
            config: Config,
            sourceModel: model,
            statusCallBack: solvingStatusCallBack,
            solutionAmount: (int)solutionAmount,
            cancellationToken: cancellationToken)));
    }

    /// <inheritdoc/>
    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        if (report is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok
            || ok.Value.ProblemStatus != ProblemStatus.Infeasible) {
            return ScipLinearSolver.ToLegacy(report).Map<SolverOutput>(output => output);
        }
        SolveReport<Flt64> value = ok.Value;
        SolveIssue[] issues = iisConfig is null
            ? []
            : [new SolveIssue(
                Code: "quadratic-iis-unavailable",
                Category: SolveIssueCategory.Unsupported,
                Message: "SCIP 二次 IIS 尚未实现 / SCIP quadratic IIS is not implemented")];
        return Results.Ok<SolverOutput>(new QuadraticInfeasibleSolverOutput(
            Status: SolverStatus.Infeasible,
            Iterations: value.Statistics.Iterations,
            NodeCount: value.Statistics.Nodes,
            SolveTime: value.Statistics.SolveTime,
            Iis: model as BasicQuadraticTetradModelView,
            IisAvailable: false,
            Diagnostics: value.Diagnostics with {
                Errors = [.. value.Diagnostics.Errors, .. issues],
            }) {
            BestBound = value.Statistics.BestBound,
            MipGap = value.Statistics.Gap,
        });
    }

    /// <inheritdoc/>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await InvokeReportAsync(
            model: model,
            solutionAmount: solutionAmount,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy = ScipLinearSolver.ToLegacy(report);
        return legacy switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok => Results.Ok((
                ok.Value,
                report.Value.Solution?.Pool.Select(solution => solution.Values.ToList()).ToList()
                    ?? new List<List<Flt64>>())),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(failed.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException)),
        };
    }

    /// <summary>
    /// 转储二次机制模型为四元组模型 / Dump quadratic mechanism model to tetrad model.
    /// </summary>
    public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(model.ToQuadraticTetradModel(cancellationToken: cancellationToken));

    /// <summary>
    /// 转储二次元模型为机制模型 / Dump quadratic meta model to mechanism model.
    /// </summary>
    public async Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) {
        return await QuadraticMechanismModel<Flt64>.InvokeAsync(
            model,
            concurrent: Config.DumpMechanismModelConcurrent,
            blocking: Config.DumpMechanismModelBlocking,
            registrationStatusCallBack: registrationStatusCallBack,
            dumpingStatusCallBack: dumpingStatusCallBack);
    }
}
