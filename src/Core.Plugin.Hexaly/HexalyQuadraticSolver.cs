#nullable enable

using System.Collections.Generic;
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

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

/// <summary>
/// Hexaly 二次求解器 / Hexaly quadratic solver.
/// </summary>
public sealed class HexalyQuadraticSolver : IQuadraticSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "hexaly";

    /// <inheritdoc/>
    public SolverDescriptor? Descriptor { get; } =
        HexalyReportSupport.Descriptor(quadratic: true);

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly HexalySolverCallBack? _callBack;

    /// <summary>
    /// 创建 Hexaly 二次求解器 / Create a Hexaly quadratic solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">求解器生命周期回调 / Solver lifecycle callback.</param>
    public HexalyQuadraticSolver(
        SolverConfig? config = null,
        HexalySolverCallBack? callBack = null) {
        Config = config ?? new SolverConfig();
        _callBack = callBack;
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        HexalyReportSupport.ToLegacy(await InvokeReportAsync(
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

    /// <inheritdoc/>
    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default) {
        if (iisConfig is not null) {
            return Results.Failed<SolverOutput>(new Err<ErrorCode>(
                ErrorCode.Other,
                "Hexaly 原生 IIS 尚未同步 / Native Hexaly IIS has not been synchronized"));
        }
        return (await InvokeAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken))
            .Map<SolverOutput>(output => output);
    }

    /// <inheritdoc/>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
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
            HexalyReportSupport.ToLegacy(report);
        return legacy switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok((ok.Value, new List<List<Flt64>> {
                    new(ok.Value.Solution.Values),
                })),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(failed.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "未知求解结果 / Unknown solve result")),
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
        return Task.FromResult(model.ToQuadraticTetradModel(
            cancellationToken: cancellationToken));
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
        return QuadraticMechanismModel<Flt64>.InvokeAsync(
            metaModel: model,
            concurrent: Config.DumpMechanismModelConcurrent,
            blocking: Config.DumpMechanismModelBlocking,
            registrationStatusCallBack: registrationStatusCallBack,
            dumpingStatusCallBack: dumpingStatusCallBack);
    }

    private Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportCore(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromResult(Results.Ok(HexalyReportSupport.Cancelled(
                config: Config,
                quadratic: true)));
        }
        if (solutionAmount > 1) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.Other,
                "Hexaly 适配器尚未实现解池 / The Hexaly adapter does not yet implement a solution pool")));
        }

        Result<Success, ErrorCode, Error<ErrorCode>> validation =
            ValidateConfig(Config);
        if (!validation.IsOk) {
            return Task.FromResult(Propagate<SolveReport<Flt64>>(validation));
        }

        Result<HexalySnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            HexalySnapshot.CaptureQuadratic(
                model: model,
                cancellationToken: cancellationToken);
        if (snapshot is not Ok<HexalySnapshot, ErrorCode, Error<ErrorCode>> captured) {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromResult(Results.Ok(HexalyReportSupport.Cancelled(
                    config: Config,
                    quadratic: true)));
            }
            return Task.FromResult(
                Propagate<SolveReport<Flt64>, HexalySnapshot>(snapshot));
        }

        return Task.FromResult(HexalyNativeSolver.Solve(
            snapshot: captured.Value,
            config: Config,
            callBack: _callBack,
            statusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken,
            name: model.Name));
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ValidateConfig(
        SolverConfig config) {
        Result<Success, ErrorCode, Error<ErrorCode>> validation =
            config.ValidateRuntime();
        if (!validation.IsOk) {
            return validation;
        }
        if (config.BackendConfiguration is not null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "Hexaly 暂不支持后端连接配置 / Hexaly backend connection configuration is not yet supported"));
        }
        return Results.OkInstance;
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T>(
        Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "Hexaly 配置结果无效 / Invalid Hexaly configuration result")),
        };

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "Hexaly 模型快照结果无效 / Invalid Hexaly model snapshot result")),
        };
}
