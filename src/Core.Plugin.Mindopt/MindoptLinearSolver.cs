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

namespace Fuookami.Ospf.Core.Plugin.Mindopt;

/// <summary>
/// MindOPT 线性求解器 / MindOPT linear solver.
/// </summary>
public sealed class MindoptLinearSolver : ILinearSolver {
    /// <inheritdoc/>
    public string Name => "mindopt";

    /// <inheritdoc/>
    public SolverDescriptor? Descriptor { get; } = new(
        SolverId: "mindopt",
        BackendName: "MindOPT",
        Capabilities: MindoptReportSupport.LinearCapabilities,
        BackendVersion: MindoptReportSupport.NativeVersion);

    /// <inheritdoc/>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建 MindOPT 线性求解器 / Create a MindOPT linear solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public MindoptLinearSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        return MindoptReportSupport.ToLegacy(await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken));
    }

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

    internal async Task<Result<MindoptLinearOutcome, ErrorCode, Error<ErrorCode>>>
        InvokeReportWithDualsAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default,
            bool requireFarkas = false) {
        var capture = new MindoptLinearDualCapture {
            CaptureFarkas = requireFarkas,
        };
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report =
            await InvokeReportCore(
                model: model,
                solutionAmount: 1,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken,
                dualCapture: capture);
        return report switch {
            Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok(new MindoptLinearOutcome(
                    Report: ok.Value,
                    RowDuals: capture.RowDuals,
                    FarkasDuals: capture.FarkasDuals)),
            Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<MindoptLinearOutcome>(failed.Error),
            Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<MindoptLinearOutcome, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<MindoptLinearOutcome>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException)),
        };
    }

    private Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportCore(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken,
        MindoptLinearDualCapture? dualCapture = null) {
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromResult(Results.Ok(MindoptReportSupport.Cancelled(
                config: Config,
                quadratic: false)));
        }
        if (solutionAmount > int.MaxValue) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "解池容量超出 MindOPT 整数范围 / Solution pool capacity exceeds the MindOPT integer range")));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> config = ValidateConfig(Config);
        if (!config.IsOk) {
            return Task.FromResult(config.Bind<SolveReport<Flt64>>(_ =>
                Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "MindOPT 线性配置无效 / Invalid MindOPT linear configuration"))));
        }
        Result<MindoptLinearSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            MindoptLinearSnapshot.Capture(
                model: model,
                cancellationToken: cancellationToken);
        if (snapshot is not Ok<MindoptLinearSnapshot, ErrorCode, Error<ErrorCode>> captured) {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromResult(Results.Ok(MindoptReportSupport.Cancelled(
                    config: Config,
                    quadratic: false)));
            }
            return Task.FromResult(snapshot.Bind<SolveReport<Flt64>>(_ =>
                Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "MindOPT 线性模型快照无效 / Invalid MindOPT linear model snapshot"))));
        }
        return Task.FromResult(MindoptNativeSolver.SolveLinear(
            snapshot: captured.Value,
            config: Config,
            sourceModel: model,
            statusCallBack: solvingStatusCallBack,
            solutionAmount: (int)solutionAmount,
            cancellationToken: cancellationToken,
            name: model.Name,
            dualCapture: dualCapture));
    }

    /// <inheritdoc/>
    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) {
        if (iisConfig is not null) {
            return Results.Failed<SolverOutput>(new Err<ErrorCode>(
                ErrorCode.Other,
                "MindOPT 原生 IIS 尚未同步 / Native MindOPT IIS has not been synchronized"));
        }
        return (await InvokeAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken))
            .Map<SolverOutput>(output => output);
    }

    /// <inheritdoc/>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report =
            await InvokeReportAsync(
                model: model,
                solutionAmount: solutionAmount,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy =
            MindoptReportSupport.ToLegacy(report);
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
        return config.BackendConfiguration is null
            ? Results.OkInstance
            : Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "MindOPT 暂不支持后端连接配置 / MindOPT backend connection configuration is not yet supported"));
    }

    private static List<List<Flt64>> Solutions(
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report) =>
        report is Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value.Solution?.Pool
                .Select(solution => solution.Values.ToList())
                .ToList()
                ?? []
            : [];
}
