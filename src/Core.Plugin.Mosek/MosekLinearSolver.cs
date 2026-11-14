#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using mosek;
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
using MosekTask = mosek.Task;
using SysException = System.Exception;

namespace Fuookami.Ospf.Core.Plugin.Mosek;

/// <summary>
/// MOSEK 线性求解器 / MOSEK linear solver.
/// </summary>
public sealed class MosekLinearSolver : ILinearSolver {
    /// <inheritdoc/>
    public string Name => "mosek";

    /// <inheritdoc/>
    public SolverDescriptor? Descriptor { get; } = MosekReportSupport.Descriptor();

    /// <inheritdoc/>
    public SolverConfig Config { get; }

    private readonly MosekSolverCallBack? _callBack;

    /// <summary>
    /// 创建 MOSEK 线性求解器 / Create a MOSEK linear solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">求解器生命周期回调 / Solver lifecycle callback.</param>
    public MosekLinearSolver(
        SolverConfig? config = null,
        MosekSolverCallBack? callBack = null) {
        Config = config ?? new SolverConfig();
        _callBack = callBack;
    }

    /// <inheritdoc/>
    public async System.Threading.Tasks.Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        MosekReportSupport.ToLegacy(await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public System.Threading.Tasks.Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        InvokeReportCore(
            model: model,
            solutionAmount: 1,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public System.Threading.Tasks.Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        InvokeReportCore(
            model: model,
            solutionAmount: solutionAmount,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    private async System.Threading.Tasks.Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportCore(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(MosekReportSupport.Cancelled(Config));
        }
        if (solutionAmount > 1) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.Other,
                "MOSEK 适配器尚未实现解池 / The MOSEK adapter does not yet implement a solution pool"));
        }
        if (_callBack?.NativeCallback is not null) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.Other,
                "MOSEK 原生进度回调尚未同步 / Native MOSEK progress callbacks are not yet synchronized"));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> config = ValidateConfig(Config);
        if (!config.IsOk) {
            return PropagateError<SolveReport<Flt64>>(config);
        }
        Result<MosekLinearSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            MosekLinearSnapshot.Capture(
                model: model,
                cancellationToken: cancellationToken);
        if (snapshot is not Ok<MosekLinearSnapshot, ErrorCode, Error<ErrorCode>> captured) {
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(MosekReportSupport.Cancelled(Config));
            }
            return snapshot switch {
                Failed<MosekLinearSnapshot, ErrorCode, Error<ErrorCode>> failed =>
                    Results.Failed<SolveReport<Flt64>>(failed.Error),
                Fatal<MosekLinearSnapshot, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "MOSEK 线性模型快照无效 / Invalid MOSEK linear model snapshot")),
            };
        }
        using var impl = new MosekLinearSolverImpl(
            config: Config,
            callBack: _callBack,
            statusCallBack: solvingStatusCallBack);
        return await impl.InvokeReportAsync(
            snapshot: captured.Value,
            name: model.Name,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async System.Threading.Tasks.Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) {
        if (iisConfig is not null) {
            return Results.Failed<SolverOutput>(new Err<ErrorCode>(
                ErrorCode.Other,
                "MOSEK 原生 IIS 尚未同步 / Native MOSEK IIS has not been synchronized"));
        }
        return (await InvokeAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken))
            .Map<SolverOutput>(output => output);
    }

    /// <inheritdoc/>
    public async System.Threading.Tasks.Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
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
            MosekReportSupport.ToLegacy(report);
        return legacy switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok((ok.Value, new List<List<Flt64>>())),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(failed.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException)),
        };
    }

    /// <inheritdoc/>
    public System.Threading.Tasks.Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) {
        if (Config.DumpIntermediateModelBounds.GetValueOrDefault()
            || Config.DumpIntermediateModelForceBounds.GetValueOrDefault()
            || Config.DumpIntermediateModelConcurrent.GetValueOrDefault()) {
            return System.Threading.Tasks.Task.FromResult(
                Results.Failed<LinearTriadModel>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持指定的中间模型转储配置 / The requested intermediate-model dump configuration is not yet supported")));
        }
        return System.Threading.Tasks.Task.FromResult(
            model.ToLinearTriadModel(
            cancellationToken: cancellationToken,
            configPolicy: Config.FunctionExpansionPolicy));
    }

    /// <inheritdoc/>
    public System.Threading.Tasks.Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return System.Threading.Tasks.Task.FromResult(
                Results.Failed<LinearMechanismModel<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型转换已取消 / Model conversion was cancelled")));
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
        if (config.BackendConfiguration is not null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "MOSEK 暂不支持后端连接配置 / MOSEK backend connection configuration is not yet supported"));
        }
        if (config.NotImprovementTime is not null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.Other,
                "MOSEK 无改进中断需要原生进度回调，当前尚未同步 / " +
                "MOSEK no-improvement interruption requires native progress callbacks and is not yet synchronized"));
        }
        return Results.OkInstance;
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> PropagateError<T>(
        Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)),
        };

    private sealed class MosekLinearSolverImpl : MosekSolver {
        private readonly SolverConfig _config;
        private readonly MosekSolverCallBack? _callBack;
        private readonly SolvingStatusCallBack? _statusCallBack;
        private MosekLinearSnapshot? _snapshot;

        internal MosekLinearSolverImpl(
            SolverConfig config,
            MosekSolverCallBack? callBack,
            SolvingStatusCallBack? statusCallBack) {
            _config = config;
            _callBack = callBack;
            _statusCallBack = statusCallBack;
        }

        internal async System.Threading.Tasks.Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>
            InvokeReportAsync(
                MosekLinearSnapshot snapshot,
                string name,
                CancellationToken cancellationToken) {
            _snapshot = snapshot;
            Result<Success, ErrorCode, Error<ErrorCode>> initialized = Init(
                name: name,
                callBack: _callBack?.CreatingEnvironmentFunction,
                cancellationToken: cancellationToken);
            if (!initialized.IsOk) {
                return CancelledOrPropagate(initialized, cancellationToken);
            }

            Result<Success, ErrorCode, Error<ErrorCode>> dumped = Dump();
            if (!dumped.IsOk) {
                return CancelledOrPropagate(dumped, cancellationToken);
            }

            Result<Success, ErrorCode, Error<ErrorCode>> configured = Configure();
            if (!configured.IsOk) {
                return CancelledOrPropagate(configured, cancellationToken);
            }
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(MosekReportSupport.Cancelled(_config));
            }

            var stopwatch = Stopwatch.StartNew();
            Result<Success, ErrorCode, Error<ErrorCode>> solved = Solve();
            stopwatch.Stop();
            if (!solved.IsOk) {
                ExecuteFailureCallback();
                return PropagateError<SolveReport<Flt64>>(solved);
            }
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(MosekReportSupport.Cancelled(_config));
            }

            Result<Success, ErrorCode, Error<ErrorCode>> status = AnalyzeStatus(
                isMip: snapshot.Columns.Any(column => column.Integer));
            if (!status.IsOk) {
                ExecuteFailureCallback();
                return PropagateError<SolveReport<Flt64>>(status);
            }
            return AnalyzeSolution(
                elapsed: stopwatch.Elapsed,
                cancellationToken: cancellationToken);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> Dump() {
            MosekLinearSnapshot snapshot = _snapshot
                ?? throw new InvalidOperationException("MOSEK snapshot not initialized.");
            try {
                MosekModel.appendvars(snapshot.Columns.Length);
                for (int column = 0; column < snapshot.Columns.Length; column++) {
                    MosekColumn source = snapshot.Columns[column];
                    MosekModel.putvarname(column, $"x{column}");
                    MosekModel.putvartype(
                        column,
                        source.Integer ? variabletype.type_int : variabletype.type_cont);
                    PutVariableBound(column, source.Lower, source.Upper);
                    if (source.Initial is { } initial) {
                        MosekModel.putxxslice(
                            soltype.bas,
                            column,
                            column + 1,
                            [initial]);
                    }
                }

                MosekModel.appendcons(snapshot.Rows.Length);
                for (int row = 0; row < snapshot.Rows.Length; row++) {
                    MosekRow source = snapshot.Rows[row];
                    MosekModel.putconname(row, $"r{row}");
                    PutConstraintBound(
                        row: row,
                        relation: source.Relation,
                        right: source.Right);
                    MosekModel.putarow(
                        row,
                        source.Columns,
                        source.Coefficients);
                }

                for (int column = 0; column < snapshot.Objective.Length; column++) {
                    MosekModel.putcj(column, snapshot.Objective[column]);
                }
                MosekModel.putcfix(snapshot.Constant);
                MosekModel.putobjsense(
                    snapshot.Category == ObjectCategory.Minimum
                        ? objsense.minimize
                        : objsense.maximize);
                return CallbackOrOk(CallBackPoint.AfterModeling);
            }
            catch (SysException error) when (error is not OutOfMemoryException and not AccessViolationException) {
                return Failure(
                    MosekReportSupport.ClassifyErrorCode(
                        error,
                        ErrorCode.OREngineModelingException),
                    $"MOSEK 模型写入失败：{error.Message} / " +
                    $"MOSEK model dump failed: {error.Message}");
            }
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> Configure() {
            MosekLinearSnapshot snapshot = _snapshot
                ?? throw new InvalidOperationException("MOSEK snapshot not initialized.");
            try {
                MosekModel.putintparam(
                    iparam.optimizer,
                    snapshot.Columns.Any(column => column.Integer)
                        ? optimizertype.mixed_int
                        : optimizertype.free);
                MosekModel.putdouparam(
                    dparam.optimizer_max_time,
                    _config.TimeLimit.TotalSeconds);
                MosekModel.putdouparam(
                    dparam.mio_max_time,
                    _config.TimeLimit.TotalSeconds);
                MosekModel.putdouparam(
                    dparam.mio_tol_abs_gap,
                    _config.Gap.ToDouble());
                MosekModel.putintparam(
                    iparam.num_threads,
                    _config.NativeThreadCount!.Value);
                return CallbackOrOk(CallBackPoint.Configuration);
            }
            catch (SysException error) when (error is not OutOfMemoryException and not AccessViolationException) {
                return Failure(
                    MosekReportSupport.ClassifyErrorCode(
                        error,
                        ErrorCode.OREngineModelingException),
                    $"MOSEK 参数配置失败：{error.Message} / " +
                    $"MOSEK parameter configuration failed: {error.Message}");
            }
        }

        private Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> AnalyzeSolution(
            TimeSpan elapsed,
            CancellationToken cancellationToken) {
            if (!Status.Succeeded()) {
                ExecuteFailureCallback();
                return MosekReportSupport.Create(
                    status: Status,
                    solution: null,
                    statistics: ReadStatistics(elapsed: elapsed, solution: null),
                    config: _config,
                    terminationReason: MapTerminationReason(ResultCode));
            }

            try {
                int variableCount = MosekModel.getnumvar();
                double[] values = MosekModel.getxxslice(
                    SolutionType,
                    0,
                    variableCount);
                double objective = MosekModel.getprimalobj(SolutionType);
                var solution = new SolveSolution<Flt64>(
                    Values: new Solution<Flt64>(
                        values.Select(value => new Flt64(value)).ToArray()),
                    Objective: new Flt64(objective));
                SolveStatistics statistics = ReadStatistics(
                    elapsed: elapsed,
                    solution: solution);
                Result<Success, ErrorCode, Error<ErrorCode>> callback =
                    CallbackOrOk(CallBackPoint.AnalyzingSolution);
                if (!callback.IsOk) {
                    return PropagateError<SolveReport<Flt64>>(callback);
                }
                Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report =
                    MosekReportSupport.Create(
                        status: Status,
                        solution: solution,
                        statistics: statistics,
                        config: _config,
                        terminationReason: MapTerminationReason(ResultCode));
                if (report is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
                    return report;
                }
                Result<Success, ErrorCode, Error<ErrorCode>> notified = NotifyStatus(
                    solution: solution,
                    statistics: statistics);
                if (!notified.IsOk) {
                    return PropagateError<SolveReport<Flt64>>(notified);
                }
                return cancellationToken.IsCancellationRequested
                    ? Results.Ok(MosekReportSupport.Cancelled(_config))
                    : ok;
            }
            catch (SysException error) when (error is not OutOfMemoryException and not AccessViolationException) {
                return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                    MosekReportSupport.ClassifyErrorCode(
                        error,
                        ErrorCode.ORSolutionInvalid),
                    $"MOSEK 解读取失败：{error.Message} / " +
                    $"MOSEK solution readback failed: {error.Message}"));
            }
        }

        private SolveStatistics ReadStatistics(
            TimeSpan elapsed,
            SolveSolution<Flt64>? solution) {
            double nativeTime = TryGet(dinfitem.optimizer_time) ?? double.NaN;
            TimeSpan solveTime = double.IsFinite(nativeTime) && nativeTime >= 0
                ? TimeSpan.FromSeconds(nativeTime)
                : elapsed;
            int simplex = TryGet(iinfitem.sim_primal_iter) ?? 0;
            int dualSimplex = TryGet(iinfitem.sim_dual_iter) ?? 0;
            int interior = TryGet(iinfitem.intpnt_iter) ?? 0;
            ulong? iterations = simplex > 0 || dualSimplex > 0 || interior > 0
                ? (ulong)System.Math.Max(simplex, System.Math.Max(dualSimplex, interior))
                : null;
            ulong? nodes = _snapshot!.Columns.Any(column => column.Integer)
                ? (ulong)System.Math.Max(TryGet(iinfitem.mio_num_solved_nodes) ?? 0, 0)
                : null;
            Flt64? bestBound = null;
            Flt64? gap = null;
            if (solution is not null) {
                bool isMip = _snapshot.Columns.Any(column => column.Integer);
                double bound = isMip
                    ? TryGet(dinfitem.mio_obj_bound) ?? double.NaN
                    : solution.Objective?.ToDouble() ?? double.NaN;
                if (double.IsFinite(bound)) {
                    bestBound = new Flt64(bound);
                }
                double nativeGap = isMip
                    ? TryGet(dinfitem.mio_obj_abs_gap) ?? double.NaN
                    : 0;
                if (double.IsFinite(nativeGap)) {
                    gap = new Flt64(nativeGap);
                }
            }
            return new SolveStatistics(
                SolveTime: solveTime,
                Iterations: iterations,
                Nodes: nodes,
                BestBound: bestBound,
                Gap: gap);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> NotifyStatus(
            SolveSolution<Flt64> solution,
            SolveStatistics statistics) {
            if (_statusCallBack is null) {
                return Results.OkInstance;
            }
            Flt64 objective = solution.Objective ?? Flt64.Zero;
            Flt64 bestBound = statistics.BestBound ?? objective;
            Flt64 gap = statistics.Gap ?? Flt64.Zero;
            Result<Success, ErrorCode, Error<ErrorCode>> result = _statusCallBack(
                new SolvingStatus(
                    Solver: "mosek",
                    SolverIndex: 0,
                    SolverConfig: _config,
                    IntermediateModel: null,
                    SolverModel: MosekModel,
                    SolverCallBack: _callBack,
                    ObjectCategory: _snapshot!.Category,
                    Time: statistics.SolveTime ?? TimeSpan.Zero,
                    Obj: objective,
                    PossibleBestObj: bestBound,
                    InitialBestObj: objective,
                    Gap: gap,
                    CurrentBestSolution: solution.Values.Values,
                    Iterations: statistics.Iterations,
                    NodeCount: statistics.Nodes,
                    BestBound: bestBound,
                    MipGap: gap,
                    SolveTime: statistics.SolveTime ?? TimeSpan.Zero));
            return result;
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> CallbackOrOk(CallBackPoint point) =>
            _callBack?.ExecIfContain(
                point: point,
                status: Status.Succeeded() ? Status : null,
                mosekModel: MosekModel)
            ?? Results.OkInstance;

        private void ExecuteFailureCallback() {
            _callBack?.ExecIfContain(
                point: CallBackPoint.AfterFailure,
                status: Status,
                mosekModel: MosekModel);
        }

        private void PutVariableBound(
            int column,
            double lower,
            double upper) {
            if (double.IsNegativeInfinity(lower) && double.IsPositiveInfinity(upper)) {
                MosekModel.putvarbound(column, boundkey.fr, 0, 0);
            }
            else if (double.IsNegativeInfinity(lower)) {
                MosekModel.putvarbound(column, boundkey.up, 0, upper);
            }
            else if (double.IsPositiveInfinity(upper)) {
                MosekModel.putvarbound(column, boundkey.lo, lower, 0);
            }
            else if (lower == upper) {
                MosekModel.putvarbound(column, boundkey.fx, lower, upper);
            }
            else {
                MosekModel.putvarbound(column, boundkey.ra, lower, upper);
            }
        }

        private void PutConstraintBound(
            int row,
            ConstraintRelation relation,
            double right) {
            if (relation == ConstraintRelation.LessEqual) {
                MosekModel.putconbound(row, boundkey.up, 0, right);
            }
            else if (relation == ConstraintRelation.GreaterEqual) {
                MosekModel.putconbound(row, boundkey.lo, right, 0);
            }
            else {
                MosekModel.putconbound(row, boundkey.fx, right, right);
            }
        }

        private double? TryGet(dinfitem item) {
            try {
                return MosekModel.getdouinf(item);
            }
            catch (SysException error) when (error is not OutOfMemoryException and not AccessViolationException) {
                return null;
            }
        }

        private int? TryGet(iinfitem item) {
            try {
                return MosekModel.getintinf(item);
            }
            catch (SysException error) when (error is not OutOfMemoryException and not AccessViolationException) {
                return null;
            }
        }

        private Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> CancelledOrPropagate(
            Result<Success, ErrorCode, Error<ErrorCode>> result,
            CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? Results.Ok(MosekReportSupport.Cancelled(_config))
                : PropagateError<SolveReport<Flt64>>(result);

        private static TerminationReason MapTerminationReason(rescode code) => code switch {
            rescode.trm_max_time => TerminationReason.TimeLimit,
            rescode.trm_max_iterations => TerminationReason.IterationLimit,
            rescode.trm_objective_range => TerminationReason.ObjectiveLimit,
            rescode.trm_num_max_num_int_solutions => TerminationReason.SolutionLimit,
            rescode.trm_mio_num_relaxs or rescode.trm_mio_num_branches => TerminationReason.NodeLimit,
            rescode.trm_numerical_problem => TerminationReason.NumericalFailure,
            rescode.trm_internal or rescode.trm_internal_stop or rescode.trm_lost_race =>
                TerminationReason.BackendFailure,
            rescode.trm_user_callback => TerminationReason.Interrupted,
            _ => TerminationReason.Completed,
        };

        private static Result<Success, ErrorCode, Error<ErrorCode>> Failure(
            ErrorCode code,
            string message) =>
            Results.Failed<Success>(new Err<ErrorCode>(code, message));
    }
}
