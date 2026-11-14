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
/// COPT 二次求解器 / COPT quadratic solver.
/// </summary>
public sealed class CoptQuadraticSolver : IQuadraticSolver {
    /// <inheritdoc/>
    public string Name => "copt";

    /// <summary>求解器描述符 / Solver descriptor.</summary>
    public SolverDescriptor? Descriptor { get; } = new(
        SolverId: "copt",
        BackendName: "COPT",
        Capabilities: CoptReportSupport.QuadraticCapabilities,
        BackendVersion: CoptReportSupport.NativeVersion);

    /// <inheritdoc/>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建 COPT 二次求解器 / Create a COPT quadratic solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public CoptQuadraticSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        return CoptReportSupport.ToLegacy(await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        InvokeReportCore(
            model: model,
            solutionAmount: 1,
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
                    "COPT 二次配置无效 / Invalid COPT quadratic configuration"))));
        }
        Result<CoptQuadraticSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            CoptQuadraticSnapshot.Capture(model: model, cancellationToken: cancellationToken);
        if (snapshot is not Ok<CoptQuadraticSnapshot, ErrorCode, Error<ErrorCode>> captured) {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromResult(Results.Ok(CoptReportSupport.Cancelled()));
            }
            return Task.FromResult(snapshot.Bind<SolveReport<Flt64>>(_ =>
                Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "COPT 二次模型快照无效 / Invalid COPT quadratic model snapshot"))));
        }
        return Task.FromResult(CoptNativeSolver.SolveQuadratic(
            snapshot: captured.Value,
            config: Config,
            sourceModel: model,
            statusCallBack: solvingStatusCallBack,
            solutionAmount: (int)solutionAmount,
            cancellationToken: cancellationToken,
            name: model.Name));
    }

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default) {
        if (iisConfig is not null) {
            return Task.FromResult(Results.Failed<SolverOutput>(new Err<ErrorCode>(
                ErrorCode.Other,
                "COPT 二次 IIS 尚未接入 / Native COPT quadratic IIS is not yet wired")));
        }
        return InvokeAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken)
            .ContinueWith(
                task => task.Result.Map<SolverOutput>(output => output),
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
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
    public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) {
        if (Config.DumpIntermediateModelBounds.GetValueOrDefault()
            || Config.DumpIntermediateModelForceBounds.GetValueOrDefault()
            || Config.DumpIntermediateModelConcurrent.GetValueOrDefault()) {
            return Task.FromResult(Results.Failed<QuadraticTetradModel>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持指定的中间模型转储配置 / The requested intermediate-model dump configuration is not yet supported")));
        }
        return Task.FromResult(model.ToQuadraticTetradModel(cancellationToken: cancellationToken));
    }

    /// <inheritdoc/>
    public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromResult(Results.Failed<QuadraticMechanismModel<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型转换已取消 / Model conversion was cancelled")));
        }
        if (dumpingStatusCallBack is not null
            || Config.DumpMechanismModelConcurrent.GetValueOrDefault()
            || Config.DumpMechanismModelBlocking.GetValueOrDefault()) {
            return Task.FromResult(Results.Failed<QuadraticMechanismModel<Flt64>>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持指定的机制模型转储配置或回调 / The requested mechanism-model dump configuration or callback is not yet supported")));
        }
        return QuadraticMechanismModel<Flt64>.InvokeAsync(
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

    private static List<List<Flt64>> Solutions(
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report) =>
        report is Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value.Solution?.Pool
                .Select(solution => solution.Values.ToList())
                .ToList()
                ?? new List<List<Flt64>>()
            : new List<List<Flt64>>();
}
