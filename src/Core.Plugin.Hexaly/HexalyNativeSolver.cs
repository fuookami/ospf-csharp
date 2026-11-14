#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Hexaly.Optimizer;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

internal static class HexalyNativeSolver {
    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Solve(
        HexalySnapshot snapshot,
        SolverConfig config,
        HexalySolverCallBack? callBack,
        SolvingStatusCallBack? statusCallBack,
        CancellationToken cancellationToken,
        string name) {
        if (snapshot is null) {
            return Failure<SolveReport<Flt64>>(
                ErrorCode.IllegalArgument,
                "Hexaly 模型快照为空 / Hexaly model snapshot is null");
        }
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(HexalyReportSupport.Cancelled(
                config: config,
                quadratic: snapshot.IsQuadratic));
        }

        HexalyOptimizer? optimizer = null;
        try {
            optimizer = new HexalyOptimizer();
            Result<Success, ErrorCode, Error<ErrorCode>> initialized =
                ExecuteCreatingCallback(optimizer: optimizer, callBack: callBack);
            if (!initialized.IsOk) {
                return Propagate<SolveReport<Flt64>>(initialized);
            }

            HxModel model = optimizer.GetModel();
            HxExpression[] variables;
            HxExpression[] constraints;
            HxExpression objective;
            Result<Success, ErrorCode, Error<ErrorCode>> dumped = BuildModel(
                optimizer: optimizer,
                model: model,
                snapshot: snapshot,
                callBack: callBack,
                out variables,
                out constraints,
                out objective);
            if (!dumped.IsOk) {
                return Propagate<SolveReport<Flt64>>(dumped);
            }

            var callbackState = new HexalyCallbackState();
            Result<Success, ErrorCode, Error<ErrorCode>> configured = Configure(
                optimizer: optimizer,
                model: model,
                snapshot: snapshot,
                variables: variables,
                constraints: constraints,
                objective: objective,
                config: config,
                callBack: callBack,
                statusCallBack: statusCallBack,
                callbackState: callbackState,
                cancellationToken: cancellationToken);
            if (!configured.IsOk) {
                if (cancellationToken.IsCancellationRequested) {
                    return Results.Ok(HexalyReportSupport.Cancelled(
                        config: config,
                        quadratic: snapshot.IsQuadratic));
                }
                return Propagate<SolveReport<Flt64>>(configured);
            }
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(HexalyReportSupport.Cancelled(
                    config: config,
                    quadratic: snapshot.IsQuadratic));
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(() => {
                callbackState.Cancel();
                try {
                    optimizer.Stop();
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                    callbackState.Fail(
                        $"Hexaly 取消失败：{error.Message} / Hexaly cancellation failed: {error.Message}");
                }
            });

            var stopwatch = Stopwatch.StartNew();
            model.Close();
            optimizer.Solve();
            stopwatch.Stop();

            HxSolution solution = optimizer.GetSolution();
            HxSolutionStatus nativeStatus = solution.GetStatus();
            if (cancellationToken.IsCancellationRequested || callbackState.Cancelled) {
                return Results.Ok(HexalyReportSupport.Cancelled(
                    config: config,
                    quadratic: snapshot.IsQuadratic));
            }
            if (callbackState.Error is { } callbackError) {
                return Failure<SolveReport<Flt64>>(
                    ErrorCode.OREngineSolvingException,
                    $"Hexaly 回调失败：{callbackError} / Hexaly callback failed: {callbackError}");
            }

            TerminationReason termination = DetermineTerminationReason(
                nativeStatus: nativeStatus,
                elapsed: stopwatch.Elapsed,
                config: config,
                callbackState: callbackState);
            Result<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> readback =
                ReadSolution(
                    solution: solution,
                    variables: variables,
                    columns: snapshot.Columns,
                    objective: objective,
                    status: nativeStatus);
            if (readback is not Ok<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> readbackOk) {
                return Propagate<SolveReport<Flt64>, SolveSolution<Flt64>?>(readback);
            }

            SolveStatistics statistics = ReadStatistics(
                optimizer: optimizer,
                solution: solution,
                elapsed: stopwatch.Elapsed,
                solutionReadback: readbackOk.Value);
            Result<Success, ErrorCode, Error<ErrorCode>> lifecycle = ExecuteLifecycleCallback(
                status: nativeStatus,
                solution: readbackOk.Value,
                callBack: callBack,
                optimizer: optimizer,
                variables: variables,
                constraints: constraints);
            if (!lifecycle.IsOk) {
                return Propagate<SolveReport<Flt64>>(lifecycle);
            }

            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report =
                HexalyReportSupport.Create(
                    status: nativeStatus,
                    solution: readbackOk.Value,
                    statistics: statistics,
                    terminationReason: termination,
                    config: config,
                    quadratic: snapshot.IsQuadratic);
            if (report is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>) {
                return report;
            }
            Result<Success, ErrorCode, Error<ErrorCode>> notified = ExecuteStatusCallback(
                statusCallBack: statusCallBack,
                status: nativeStatus,
                solution: readbackOk.Value,
                statistics: statistics,
                optimizer: optimizer,
                snapshot: snapshot,
                config: config,
                callBack: callBack);
            return notified.IsOk ? report : Propagate<SolveReport<Flt64>>(notified);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure<SolveReport<Flt64>>(
                HexalyReportSupport.ClassifyException(
                    error,
                    ErrorCode.OREngineEnvironmentLost),
                $"Hexaly 环境或求解失败：{error.Message} / Hexaly environment or solve failed: {error.Message}");
        }
        finally {
            try {
                optimizer?.Dispose();
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            }
        }
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ExecuteCreatingCallback(
        HexalyOptimizer optimizer,
        HexalySolverCallBack? callBack) {
        if (callBack?.CreatingEnvironmentFunction is not { } creating) {
            return Results.OkInstance;
        }
        try {
            return creating(optimizer);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                HexalyReportSupport.ClassifyException(
                    error,
                    ErrorCode.OREngineEnvironmentLost),
                $"Hexaly 环境回调失败：{error.Message} / Hexaly environment callback failed: {error.Message}");
        }
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> BuildModel(
        HexalyOptimizer optimizer,
        HxModel model,
        HexalySnapshot snapshot,
        HexalySolverCallBack? callBack,
        out HxExpression[] variables,
        out HxExpression[] constraints,
        out HxExpression objective) {
        variables = Array.Empty<HxExpression>();
        constraints = Array.Empty<HxExpression>();
        objective = model.Sum();
        try {
            variables = new HxExpression[snapshot.Columns.Length];
            for (int column = 0; column < variables.Length; column++) {
                HexalyColumn source = snapshot.Columns[column];
                HxExpression variable = source.Kind switch {
                    HexalyColumnKind.Binary => model.Bool(),
                    HexalyColumnKind.Integer => model.Int(source.IntegerLower, source.IntegerUpper),
                    _ => model.Float(source.Lower, source.Upper),
                };
                variable.SetName($"x{column}");
                if (source.Initial is { } initial) {
                    if (source.Kind == HexalyColumnKind.Continuous) {
                        variable.SetValue(initial);
                    }
                    else {
                        variable.SetValue((long)initial);
                    }
                }
                variables[column] = variable;
            }

            constraints = new HxExpression[snapshot.Rows.Length];
            for (int row = 0; row < constraints.Length; row++) {
                HexalyRow source = snapshot.Rows[row];
                HxExpression lhs = BuildExpression(model: model, terms: source.Terms, variables: variables);
                HxExpression constraint;
                if (source.Relation == ConstraintRelation.LessEqual) {
                    constraint = model.Leq(lhs, source.Right);
                }
                else if (source.Relation == ConstraintRelation.Equal) {
                    constraint = model.Eq(lhs, source.Right);
                }
                else if (source.Relation == ConstraintRelation.GreaterEqual) {
                    constraint = model.Geq(lhs, source.Right);
                }
                else {
                    throw new InvalidOperationException("Invalid constraint relation.");
                }
                model.Constraint(constraint);
                constraints[row] = constraint;
            }

            objective = BuildExpression(model: model, terms: snapshot.Objective, variables: variables);
            objective.AddOperand(snapshot.Constant);
            if (snapshot.Category == ObjectCategory.Maximum) {
                model.Maximize(objective);
            }
            else {
                model.Minimize(objective);
            }
            return callBack?.ExecIfContain(
                point: CallBackPoint.AfterModeling,
                status: null,
                optimizer: optimizer,
                variables: variables,
                constraints: constraints) ?? Results.OkInstance;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                HexalyReportSupport.ClassifyException(
                    error,
                    ErrorCode.OREngineModelingException),
                $"Hexaly 模型写入失败：{error.Message} / Hexaly model dump failed: {error.Message}");
        }
    }

    private static HxExpression BuildExpression(
        HxModel model,
        IReadOnlyList<HexalyTerm> terms,
        IReadOnlyList<HxExpression> variables) {
        HxExpression expression = model.Sum();
        foreach (HexalyTerm term in terms) {
            HxExpression product = model.Prod(term.Coefficient, variables[term.First]);
            if (term.Second is { } second) {
                product = model.Prod(product, variables[second]);
            }
            expression.AddOperand(product);
        }
        return expression;
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> Configure(
        HexalyOptimizer optimizer,
        HxModel model,
        HexalySnapshot snapshot,
        IReadOnlyList<HxExpression> variables,
        IReadOnlyList<HxExpression> constraints,
        HxExpression objective,
        SolverConfig config,
        HexalySolverCallBack? callBack,
        SolvingStatusCallBack? statusCallBack,
        HexalyCallbackState callbackState,
        CancellationToken cancellationToken) {
        try {
            HxParam parameters = optimizer.GetParam();
            parameters.SetTimeLimit(TimeLimitSeconds(config.TimeLimit));
            parameters.SetNbThreads(config.NativeThreadCount!.Value);
            if (statusCallBack is not null
                || callBack?.NativeCallback is not null
                || config.Gap.ToDouble() > 0
                || config.NotImprovementTime is not null) {
                var monitor = new NoImprovementMonitor(config);
                var stopwatch = Stopwatch.StartNew();
                Flt64? initialObjective = null;
                var nativeCallback = new HxCallback((nativeOptimizer, callbackType) => {
                    if (callbackState.Cancelled) {
                        return;
                    }
                    try {
                        callBack?.NativeCallback?.Invoke(nativeOptimizer, callbackType);
                        if (callbackType != HxCallbackType.IterationTicked) {
                            return;
                        }
                        HxSolution currentSolution = nativeOptimizer.GetSolution();
                        double objectiveValue = objective.GetDoubleValue();
                        double boundValue = currentSolution.GetDoubleObjectiveBound(0);
                        if (!double.IsFinite(objectiveValue) || !double.IsFinite(boundValue)) {
                            return;
                        }
                        var currentObjective = new Flt64(objectiveValue);
                        var currentBound = new Flt64(boundValue);
                        initialObjective ??= currentObjective;
                        TimeSpan elapsed = stopwatch.Elapsed;
                        double gap = RelativeGap(objectiveValue, boundValue);
                        if (config.Gap.ToDouble() > 0
                            && gap <= config.Gap.ToDouble()) {
                            callbackState.StopForGap();
                            nativeOptimizer.Stop();
                        }
                        if (monitor.ShouldAbort(
                            time: elapsed,
                            obj: currentObjective,
                            bound: currentBound)) {
                            callbackState.StopForNoImprovement();
                            nativeOptimizer.Stop();
                        }

                        if (statusCallBack is not null) {
                            IReadOnlyList<Flt64>? values = ReadCallbackValues(
                                columns: snapshot.Columns,
                                variables: variables);
                            if (values is not null) {
                                Result<Success, ErrorCode, Error<ErrorCode>> statusResult =
                                    statusCallBack(new SolvingStatus(
                                        Solver: "hexaly",
                                        SolverIndex: 0,
                                        SolverConfig: config,
                                        IntermediateModel: snapshot,
                                        SolverModel: nativeOptimizer.GetModel(),
                                        SolverCallBack: callBack,
                                        ObjectCategory: snapshot.Category,
                                        Time: elapsed,
                                        Obj: currentObjective,
                                        PossibleBestObj: currentBound,
                                        InitialBestObj: initialObjective ?? currentObjective,
                                        Gap: SolvingStatusSupport.Gap(currentObjective, currentBound),
                                        CurrentBestSolution: values,
                                        Iterations: ReadIterations(nativeOptimizer),
                                        BestBound: currentBound,
                                        MipGap: SolvingStatusSupport.Gap(currentObjective, currentBound),
                                        SolveTime: elapsed));
                                if (!statusResult.IsOk) {
                                    callbackState.Fail("求解状态回调返回失败 / Solving-status callback failed");
                                    nativeOptimizer.Stop();
                                }
                            }
                        }
                    }
                    catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                        callbackState.Fail(error.Message);
                        try {
                            nativeOptimizer.Stop();
                        }
                        catch (Exception stopError) when (stopError is not OutOfMemoryException and not AccessViolationException) {
                        }
                    }
                });
                optimizer.AddCallback(HxCallbackType.IterationTicked, nativeCallback);
            }
            Result<Success, ErrorCode, Error<ErrorCode>> callback =
                callBack?.ExecIfContain(
                    point: CallBackPoint.Configuration,
                    status: null,
                    optimizer: optimizer,
                    variables: variables,
                    constraints: constraints) ?? Results.OkInstance;
            if (cancellationToken.IsCancellationRequested) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationStopped,
                    "Hexaly 配置已取消 / Hexaly configuration was cancelled"));
            }
            return callback;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                HexalyReportSupport.ClassifyException(
                    error,
                    ErrorCode.OREngineModelingException),
                $"Hexaly 参数配置失败：{error.Message} / Hexaly parameter configuration failed: {error.Message}");
        }
    }

    private static Result<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> ReadSolution(
        HxSolution solution,
        IReadOnlyList<HxExpression> variables,
        IReadOnlyList<HexalyColumn> columns,
        HxExpression objective,
        HxSolutionStatus status) {
        if (status is not HxSolutionStatus.Optimal and not HxSolutionStatus.Feasible) {
            return Results.Ok<SolveSolution<Flt64>?>(null);
        }
        try {
            var values = new Flt64[variables.Count];
            for (int index = 0; index < variables.Count; index++) {
                values[index] = columns[index].Kind == HexalyColumnKind.Continuous
                    ? new Flt64(variables[index].GetDoubleValue())
                    : new Flt64(variables[index].GetIntValue());
                if (!double.IsFinite(values[index].ToDouble())) {
                    return Results.Failed<SolveSolution<Flt64>?>(
                        new Err<ErrorCode>(
                            ErrorCode.ORSolutionInvalid,
                            $"第 {index} 个 Hexaly 变量值无效 / Hexaly variable {index} is not finite"));
                }
            }
            double objectiveValue = objective.GetDoubleValue();
            if (!double.IsFinite(objectiveValue)) {
                return Results.Failed<SolveSolution<Flt64>?>(
                    new Err<ErrorCode>(
                        ErrorCode.ORSolutionInvalid,
                        "Hexaly 目标值不是有限数 / Hexaly objective is not finite"));
            }
            return Results.Ok<SolveSolution<Flt64>?>(new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(values),
                Objective: new Flt64(objectiveValue)));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Results.Failed<SolveSolution<Flt64>?>(
                new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    $"Hexaly 解读取失败：{error.Message} / Hexaly solution readback failed: {error.Message}"));
        }
    }

    private static SolveStatistics ReadStatistics(
        HexalyOptimizer optimizer,
        HxSolution solution,
        TimeSpan elapsed,
        SolveSolution<Flt64>? solutionReadback) {
        ulong? iterations = ReadIterations(optimizer);
        Flt64? bestBound = null;
        Flt64? gap = null;
        if (solutionReadback is not null) {
            try {
                double bound = solution.GetDoubleObjectiveBound(0);
                if (double.IsFinite(bound)) {
                    bestBound = new Flt64(bound);
                }
                double nativeGap = solution.GetObjectiveGap(0);
                if (double.IsFinite(nativeGap)) {
                    gap = new Flt64(nativeGap);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            }
        }
        return new SolveStatistics(
            SolveTime: elapsed,
            Iterations: iterations,
            BestBound: bestBound,
            Gap: gap);
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ExecuteLifecycleCallback(
        HxSolutionStatus status,
        SolveSolution<Flt64>? solution,
        HexalySolverCallBack? callBack,
        HexalyOptimizer optimizer,
        IReadOnlyList<HxExpression> variables,
        IReadOnlyList<HxExpression> constraints) {
        if (callBack is null) {
            return Results.OkInstance;
        }
        SolverStatus legacyStatus = status switch {
            HxSolutionStatus.Optimal => SolverStatus.Optimal,
            HxSolutionStatus.Feasible => SolverStatus.Feasible,
            HxSolutionStatus.Inconsistent => SolverStatus.Infeasible,
            _ => SolverStatus.SolvingException,
        };
        try {
            return callBack.ExecIfContain(
                point: solution is null ? CallBackPoint.AfterFailure : CallBackPoint.AnalyzingSolution,
                status: legacyStatus,
                optimizer: optimizer,
                variables: variables,
                constraints: constraints) ?? Results.OkInstance;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                ErrorCode.OREngineSolvingException,
                $"Hexaly 生命周期回调失败：{error.Message} / Hexaly lifecycle callback failed: {error.Message}");
        }
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ExecuteStatusCallback(
        SolvingStatusCallBack? statusCallBack,
        HxSolutionStatus status,
        SolveSolution<Flt64>? solution,
        SolveStatistics statistics,
        HexalyOptimizer optimizer,
        HexalySnapshot snapshot,
        SolverConfig config,
        HexalySolverCallBack? callBack) {
        if (statusCallBack is null || solution?.Objective is not { } objective) {
            return Results.OkInstance;
        }
        try {
            Flt64 bound = statistics.BestBound ?? objective;
            Flt64 gap = statistics.Gap ?? Flt64.Zero;
            return statusCallBack(new SolvingStatus(
                Solver: "hexaly",
                SolverIndex: 0,
                SolverConfig: config,
                IntermediateModel: snapshot,
                SolverModel: optimizer.GetModel(),
                SolverCallBack: callBack,
                ObjectCategory: snapshot.Category,
                Time: statistics.SolveTime ?? TimeSpan.Zero,
                Obj: objective,
                PossibleBestObj: bound,
                InitialBestObj: objective,
                Gap: gap,
                CurrentBestSolution: solution.Values.Values,
                Iterations: statistics.Iterations,
                BestBound: bound,
                MipGap: gap,
                SolveTime: statistics.SolveTime ?? TimeSpan.Zero));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                ErrorCode.OREngineSolvingException,
                $"Hexaly 状态回调失败：{error.Message} / Hexaly status callback failed: {error.Message}");
        }
    }

    private static TerminationReason DetermineTerminationReason(
        HxSolutionStatus nativeStatus,
        TimeSpan elapsed,
        SolverConfig config,
        HexalyCallbackState callbackState) {
        if (callbackState.Cancelled) {
            return TerminationReason.Cancelled;
        }
        if (callbackState.NoImprovement) {
            return TerminationReason.Interrupted;
        }
        if (callbackState.GapReached) {
            return TerminationReason.ObjectiveLimit;
        }
        if (nativeStatus is not HxSolutionStatus.Optimal and not HxSolutionStatus.Inconsistent
            && config.TimeLimit > TimeSpan.Zero
            && elapsed >= config.TimeLimit) {
            return TerminationReason.TimeLimit;
        }
        return TerminationReason.Completed;
    }

    private static IReadOnlyList<Flt64>? ReadCallbackValues(
        IReadOnlyList<HexalyColumn> columns,
        IReadOnlyList<HxExpression> variables) {
        try {
            var values = new Flt64[variables.Count];
            for (int index = 0; index < variables.Count; index++) {
                values[index] = columns[index].Kind == HexalyColumnKind.Continuous
                    ? new Flt64(variables[index].GetDoubleValue())
                    : new Flt64(variables[index].GetIntValue());
            }
            return values;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return null;
        }
    }

    private static ulong? ReadIterations(HexalyOptimizer optimizer) {
        try {
            long iterations = optimizer.GetStatistics().GetNbIterations();
            return iterations >= 0 ? (ulong)iterations : null;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return null;
        }
    }

    private static int TimeLimitSeconds(TimeSpan timeLimit) {
        double seconds = System.Math.Ceiling(timeLimit.TotalSeconds);
        return seconds >= int.MaxValue ? int.MaxValue : (int)seconds;
    }

    private static double RelativeGap(double objective, double bound) {
        return System.Math.Abs(objective - bound)
            / System.Math.Max(1.0, System.Math.Abs(objective));
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T>(
        Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(ErrorCode.OREngineSolvingException, "Hexaly 操作结果无效 / Invalid Hexaly operation result"),
        };

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(ErrorCode.OREngineSolvingException, "Hexaly 操作结果无效 / Invalid Hexaly operation result"),
        };

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));

    private static Result<Success, ErrorCode, Error<ErrorCode>> Failure(
        ErrorCode code,
        string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(code, message));

    private sealed class HexalyCallbackState {
        private readonly object _gate = new();
        private bool _cancelled;
        private bool _noImprovement;
        private bool _gapReached;
        private string? _error;

        internal bool Cancelled {
            get {
                lock (_gate) {
                    return _cancelled;
                }
            }
        }

        internal bool NoImprovement {
            get {
                lock (_gate) {
                    return _noImprovement;
                }
            }
        }

        internal bool GapReached {
            get {
                lock (_gate) {
                    return _gapReached;
                }
            }
        }

        internal string? Error {
            get {
                lock (_gate) {
                    return _error;
                }
            }
        }

        internal void Cancel() {
            lock (_gate) {
                _cancelled = true;
            }
        }

        internal void StopForNoImprovement() {
            lock (_gate) {
                _noImprovement = true;
            }
        }

        internal void StopForGap() {
            lock (_gate) {
                _gapReached = true;
            }
        }

        internal void Fail(string message) {
            lock (_gate) {
                _error ??= message;
            }
        }
    }
}
