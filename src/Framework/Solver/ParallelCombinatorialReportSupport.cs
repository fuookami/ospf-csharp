#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Report;
namespace Fuookami.Ospf.Framework.Solver;

internal static class ParallelCombinatorialReportSupport {
    internal static async Task<Result<CombinatorialSolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SolveAsync<TSolver>(
        Result<Success, ErrorCode, Error<ErrorCode>> validation,
        IReadOnlyList<Func<TSolver>> factories,
        ParallelCombinatorialMode mode,
        Func<ObjectCategory> category,
        Func<TSolver, string> name,
        Func<TSolver, Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>> solve,
        ILogger logger,
        CancellationToken cancellationToken) {
        if (validation is Failed<Success, ErrorCode, Error<ErrorCode>> invalid) {
            return Results.Failed<CombinatorialSolveReport<Flt64>>(invalid.Error);
        }
        if (validation is Fatal<Success, ErrorCode, Error<ErrorCode>> fatalValidation) {
            return new Fatal<CombinatorialSolveReport<Flt64>, ErrorCode, Error<ErrorCode>>(fatalValidation.Errors);
        }
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Results.Failed<CombinatorialSolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "模型校验结果无效 / Invalid model validation result"));
        }
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(new CombinatorialSolveReport<Flt64>(
                FinalReport: SerialCombinatorialReportSupport.Cancelled(),
                Attempts: Array.Empty<SolveAttemptTrace<Flt64>>(),
                SelectedAttemptId: null,
                SelectionReason: SolveSelectionReason.NoSuccessfulAttempt));
        }
        if (mode is not ParallelCombinatorialMode.First and not ParallelCombinatorialMode.Best) {
            return Results.Failed<CombinatorialSolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "组合求解模式无效 / Invalid combinatorial solve mode"));
        }

        ObjectCategory direction = ObjectCategory.Minimum;
        if (mode == ParallelCombinatorialMode.Best) {
            try {
                direction = category();
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                LogFailure(logger, error);
                return Results.Failed<CombinatorialSolveReport<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "读取模型目标失败 / Failed to read model objective"));
            }
            if (direction is not ObjectCategory.Minimum and not ObjectCategory.Maximum) {
                return Results.Failed<CombinatorialSolveReport<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "模型目标方向无效 / Invalid model objective direction"));
            }
        }

        Task<SolveAttemptTrace<Flt64>>[] tasks = factories.Select((factory, index) => Task.Run(() => AttemptAsync(
            factory: factory,
            index: index,
            name: name,
            solve: solve,
            logger: logger,
            cancellationToken: cancellationToken))).ToArray();
        SolveAttemptTrace<Flt64>[] attempts = await Task.WhenAll(tasks);
        SolveAttemptTrace<Flt64>[] successful = attempts.Where(attempt =>
            attempt.Report?.Solution is not null &&
            attempt.Report.SolutionPresence is SolutionPresence.Incumbent or SolutionPresence.Optimal).ToArray();
        SolveAttemptTrace<Flt64>? selected = successful.FirstOrDefault();
        SolveSelectionReason reason = selected is null ? SolveSelectionReason.NoSuccessfulAttempt : SolveSelectionReason.FirstFeasible;
        if (selected is not null && mode == ParallelCombinatorialMode.Best && successful.All(attempt =>
            attempt.Report!.Solution!.Objective is Flt64 objective && double.IsFinite(objective.ToDouble()))) {
            foreach (SolveAttemptTrace<Flt64> candidate in successful.Skip(1)) {
                double value = candidate.Report!.Solution!.Objective.GetValueOrDefault().ToDouble();
                double best = selected.Report!.Solution!.Objective.GetValueOrDefault().ToDouble();
                if (IsBetter(
                    value: value,
                    best: best,
                    direction: direction)) {
                    selected = candidate;
                }
            }
            reason = SolveSelectionReason.BestObjective;
        }
        SolveAttemptTrace<Flt64>? terminal = selected ?? attempts.FirstOrDefault(attempt =>
            attempt.Report is { } report &&
            (report.ProblemStatus != ProblemStatus.Unknown || report.TerminationReason == TerminationReason.Cancelled));
        return Results.Ok(new CombinatorialSolveReport<Flt64>(
            FinalReport: terminal?.Report,
            Attempts: Array.AsReadOnly(attempts),
            SelectedAttemptId: selected?.AttemptId,
            SelectionReason: reason));
    }

    private static async Task<SolveAttemptTrace<Flt64>> AttemptAsync<TSolver>(
        Func<TSolver> factory,
        int index,
        Func<TSolver, string> name,
        Func<TSolver, Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>> solve,
        ILogger logger,
        CancellationToken cancellationToken) {
        long started = Stopwatch.GetTimestamp();
        bool cancelledAtStart = cancellationToken.IsCancellationRequested;
        string backend = $"uninitialized-{index}";
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        try {
            TSolver solver = factory();
            backend = name(solver);
            result = await solve(solver);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            result = Results.Ok(SerialCombinatorialReportSupport.Cancelled());
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            LogFailure(logger, error);
            result = Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "并行求解器调用异常 / Parallel solver invocation failed"));
        }

        SolveReport<Flt64>? report = (result as Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>)?.Value;
        IReadOnlyList<Error<ErrorCode>> errors = result switch {
            Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> { Value: not null } => [],
            Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed => [failed.Error],
            Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> fatal => fatal.Errors,
            _ => [new Err<ErrorCode>(ErrorCode.OREngineSolvingException,
                "求解器返回无效结果 / Solver returned an invalid result")],
        };
        return new SolveAttemptTrace<Flt64>(
            AttemptId: new SolveAttemptId($"parallel-{index}"),
            BackendId: backend,
            ParentAttemptId: new SolveAttemptId("parallel-combinatorial"),
            Report: report,
            Elapsed: Stopwatch.GetElapsedTime(started),
            Errors: Array.AsReadOnly(errors.Select(SerialCombinatorialReportSupport.ToIssue).ToArray()),
            CancellationReason: report?.TerminationReason == TerminationReason.Cancelled || (report is null && cancelledAtStart)
                ? "cancellation requested"
                : null);
    }

    internal static bool IsBetter(double value, double best, ObjectCategory direction) {
        int comparison = value.CompareTo(best);
        // Kotlin 区分正负零；.NET CompareTo 将其视为相等。 / Kotlin orders signed zero; .NET CompareTo treats it as equal.
        if (comparison == 0 && value == 0.0 && best == 0.0) {
            comparison = BitConverter.DoubleToInt64Bits(value).CompareTo(BitConverter.DoubleToInt64Bits(best));
        }
        return direction == ObjectCategory.Minimum ? comparison < 0 : comparison > 0;
    }

    internal static void LogFailure(ILogger logger, Exception error) {
        try {
            logger.LogError(error, "combinatorial solver operation failed / 组合求解器操作失败");
        }
        catch (Exception loggingError) when (loggingError is not OutOfMemoryException and not AccessViolationException) {
            // 日志提供方失败不能覆盖求解结果。 / A broken logging provider must not replace the solve result.
        }
    }
}
