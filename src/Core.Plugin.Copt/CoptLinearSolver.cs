#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Plugin.Copt;

/// <summary>
/// COPT 线性求解器 / COPT linear solver.
/// </summary>
public sealed class CoptLinearSolver : ILinearSolver {
    /// <inheritdoc/>
    public string Name => "copt";

    /// <inheritdoc/>
    public SolverDescriptor? Descriptor { get; } = new(
        SolverId: "copt",
        BackendName: "COPT",
        Capabilities: CoptReportSupport.LinearCapabilities,
        BackendVersion: CoptReportSupport.NativeVersion);

    /// <inheritdoc/>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建 COPT 线性求解器 / Create a COPT linear solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public CoptLinearSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        return CoptReportSupport.ToLegacy(await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc/>
    public IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> DiagnosticAnalyzers(IisConfig config) =>
        [new CoptInfeasibilityAnalyzer(config)];

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        InvokeReportCore(
            model: model,
            solutionAmount: 1,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        InvokeReportCore(
            model: model,
            solutionAmount: solutionAmount,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    private Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportCore(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken,
        CoptLinearDualCapture? dualCapture = null) {
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromResult(Results.Ok(CoptReportSupport.Cancelled()));
        }
        if (solutionAmount > int.MaxValue) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "解池容量超出 COPT 整数范围 / Solution pool capacity exceeds the COPT integer range")));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> config = ValidateConfig(Config);
        if (!config.IsOk) {
            return Task.FromResult(config.Bind<SolveReport<Flt64>>(_ =>
                Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "COPT 线性配置无效 / Invalid COPT linear configuration"))));
        }
        Result<CoptLinearSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            CoptLinearSnapshot.Capture(model: model, cancellationToken: cancellationToken);
        if (snapshot is not Ok<CoptLinearSnapshot, ErrorCode, Error<ErrorCode>> captured) {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromResult(Results.Ok(CoptReportSupport.Cancelled()));
            }
            return Task.FromResult(snapshot.Bind<SolveReport<Flt64>>(_ =>
                Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "COPT 线性模型快照无效 / Invalid COPT linear model snapshot"))));
        }
        return Task.FromResult(CoptNativeSolver.SolveLinear(
            snapshot: captured.Value,
            config: Config,
            sourceModel: model,
            statusCallBack: solvingStatusCallBack,
            solutionAmount: (int)solutionAmount,
            cancellationToken: cancellationToken,
            name: model.Name,
            dualCapture: dualCapture));
    }

    internal async Task<Result<CoptLinearOutcome, ErrorCode, Error<ErrorCode>>>
        InvokeReportWithDualsAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default,
            bool requireFarkas = false) {
        var capture = new CoptLinearDualCapture { CaptureFarkas = requireFarkas };
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report =
            await InvokeReportCore(
                model: model,
                solutionAmount: 1,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken,
                dualCapture: capture);
        return report switch {
            Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok(new CoptLinearOutcome(
                    Report: ok.Value,
                    RowDuals: capture.RowDuals,
                    FarkasDuals: capture.FarkasDuals)),
            Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<CoptLinearOutcome>(failed.Error),
            Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<CoptLinearOutcome, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<CoptLinearOutcome>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException)),
        };
    }

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) =>
        InvokeWithIisAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            iisConfig: iisConfig,
            cancellationToken: cancellationToken);

    private async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeWithIisAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken) {
        if (iisConfig is null) {
            return (await InvokeAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken)).Map<SolverOutput>(output => output);
        }
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        if (report is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok
            || ok.Value.ProblemStatus != ProblemStatus.Infeasible) {
            return CoptReportSupport.ToLegacy(report).Map<SolverOutput>(output => output);
        }
        SolveReport<Flt64> value = ok.Value;
        Result<LinearIisResult, ErrorCode, Error<ErrorCode>> computed = await this.ComputeIISAsync(
            model: model,
            config: iisConfig,
            cancellationToken: cancellationToken);
        if (computed is Ok<LinearIisResult, ErrorCode, Error<ErrorCode>> outcome) {
            return Results.Ok<SolverOutput>(new LinearInfeasibleSolverOutput(
                Status: SolverStatus.Infeasible,
                Iterations: value.Statistics.Iterations,
                NodeCount: value.Statistics.Nodes,
                SolveTime: value.Statistics.SolveTime,
                Iis: outcome.Value.Model,
                IisAvailable: true,
                Diagnostics: value.Diagnostics with {
                    InfeasibilityEvidence = outcome.Value.Evidence,
                }) {
                BestBound = value.Statistics.BestBound,
                MipGap = value.Statistics.Gap,
            });
        }
        SolveIssue[] errors = computed switch {
            Failed<LinearIisResult, ErrorCode, Error<ErrorCode>> failed => [ToIssue(failed.Error)],
            Fatal<LinearIisResult, ErrorCode, Error<ErrorCode>> fatal => [.. fatal.Errors.Select(ToIssue)],
            _ => [],
        };
        return Results.Ok<SolverOutput>(new LinearInfeasibleSolverOutput(
            Status: SolverStatus.Infeasible,
            Iterations: value.Statistics.Iterations,
            NodeCount: value.Statistics.Nodes,
            SolveTime: value.Statistics.SolveTime,
            Iis: model,
            IisAvailable: false,
            Diagnostics: value.Diagnostics with {
                Errors = [.. value.Diagnostics.Errors, .. errors],
            }) {
            BestBound = value.Statistics.BestBound,
            MipGap = value.Statistics.Gap,
        });
    }

    /// <inheritdoc/>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await InvokeReportAsync(
            model: model,
            solutionAmount: solutionAmount,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy =
            CoptReportSupport.ToLegacy(report);
        return legacy switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok((ok.Value, Solutions(report))),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(failed.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException)),
        };
    }

    /// <inheritdoc/>
    public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) {
        if (Config.DumpIntermediateModelBounds.GetValueOrDefault()
            || Config.DumpIntermediateModelForceBounds.GetValueOrDefault()
            || Config.DumpIntermediateModelConcurrent.GetValueOrDefault()) {
            return Task.FromResult(Results.Failed<LinearTriadModel>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持指定的中间模型转储配置 / The requested intermediate-model dump configuration is not yet supported")));
        }
        return Task.FromResult(model.ToLinearTriadModel(
            cancellationToken: cancellationToken,
            configPolicy: Config.FunctionExpansionPolicy));
    }

    /// <inheritdoc/>
    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromResult(Results.Failed<LinearMechanismModel<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型转换已取消 / Model conversion was cancelled")));
        }
        if (dumpingStatusCallBack is not null
            || Config.DumpMechanismModelConcurrent.GetValueOrDefault()
            || Config.DumpMechanismModelBlocking.GetValueOrDefault()) {
            return Task.FromResult(Results.Failed<LinearMechanismModel<Flt64>>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持指定的机制模型转储配置或回调 / The requested mechanism-model dump configuration or callback is not yet supported")));
        }
        return LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: model,
            concurrent: Config.DumpMechanismModelConcurrent,
            blocking: Config.DumpMechanismModelBlocking,
            registrationStatusCallBack: registrationStatusCallBack,
            dumpingStatusCallBack: dumpingStatusCallBack);
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ValidateConfig(SolverConfig config) {
        Result<Success, ErrorCode, Error<ErrorCode>> validation = config.ValidateRuntime();
        if (!validation.IsOk) {
            return validation;
        }
        if (config.BackendConfiguration is not null and not CoptSolverConfig) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"COPT 求解器不能使用 {config.BackendConfiguration.Type} backend 配置 / " +
                $"COPT solver cannot use backend configuration {config.BackendConfiguration.Type}"));
        }
        if (config.BackendConfiguration is CoptSolverConfig backend
            && (backend.Server is not null
                || backend.Port is not null
                || backend.Password is not null
                || backend.ConnectionTime is not null)
            && (backend.Server is null
                || backend.Port is null
                || backend.Password is null
                || backend.ConnectionTime is null)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "COPT 远程连接必须同时提供 server、port、password 和 connectionTime / " +
                "COPT remote connections require server, port, password, and connectionTime together"));
        }
        return Results.OkInstance;
    }

    private static SolveIssue ToIssue(Error<ErrorCode> error) => new(
        Code: error.Code.ToString() ?? "",
        Category: SolveIssueCategory.Backend,
        Message: error.Message);

    private static List<List<Flt64>> Solutions(
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report) =>
        report is Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value.Solution?.Pool
                .Select(solution => solution.Values.ToList())
                .ToList()
                ?? new List<List<Flt64>>()
            : new List<List<Flt64>>();
}
