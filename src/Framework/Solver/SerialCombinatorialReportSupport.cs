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
using Fuookami.Ospf.Core.Solver.Report;
namespace Fuookami.Ospf.Framework.Solver;

internal static class SerialCombinatorialReportSupport {
    internal static readonly IReadOnlySet<ErrorCode> DefaultStopCodes = new HashSet<ErrorCode> {
        ErrorCode.ORModelInfeasible,
        ErrorCode.ORModelUnbounded,
    };

    internal static async Task<Result<CombinatorialSolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SolveAsync<TSolver>(
        Result<Success, ErrorCode, Error<ErrorCode>> validation,
        IReadOnlyList<Func<TSolver>> factories,
        IReadOnlySet<ErrorCode> stopCodes,
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

        var attempts = new List<SolveAttemptTrace<Flt64>>();
        if (cancellationToken.IsCancellationRequested) {
            return Finish(
                report: Cancelled(),
                attempts: attempts,
                selected: null);
        }
        for (int i = 0; i < factories.Count; i++) {
            if (cancellationToken.IsCancellationRequested) {
                return Finish(
                    report: Cancelled(),
                    attempts: attempts,
                    selected: null);
            }

            long started = Stopwatch.GetTimestamp();
            var id = new SolveAttemptId($"serial-{i}");
            string backend = $"uninitialized-{i}";
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
            try {
                TSolver solver = factories[i]();
                backend = name(solver);
                result = await solve(solver);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                result = Results.Ok(Cancelled());
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                ParallelCombinatorialReportSupport.LogFailure(logger, error);
                result = Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    "串行求解器调用异常 / Serial solver invocation failed"));
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            if (result is Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> { Value: not null } ok) {
                SolveReport<Flt64> report = ok.Value;
                attempts.Add(new SolveAttemptTrace<Flt64>(
                    AttemptId: id,
                    BackendId: backend,
                    ParentAttemptId: new SolveAttemptId("serial-combinatorial"),
                    Report: report,
                    Elapsed: elapsed,
                    CancellationReason: report.TerminationReason == TerminationReason.Cancelled
                        ? "cancellation requested"
                        : null));
                if (report.TerminationReason == TerminationReason.Cancelled) {
                    return Finish(
                        report: report,
                        attempts: attempts,
                        selected: null);
                }
                bool incumbent = report.Solution is not null &&
                    report.SolutionPresence is SolutionPresence.Incumbent or SolutionPresence.Optimal;
                ErrorCode? stopCode = report.ProblemStatus switch {
                    ProblemStatus.Infeasible => ErrorCode.ORModelInfeasible,
                    ProblemStatus.Unbounded => ErrorCode.ORModelUnbounded,
                    ProblemStatus.InfeasibleOrUnbounded => ErrorCode.ORModelInfeasibleOrUnbounded,
                    _ => null,
                };
                if (incumbent || (stopCode is { } code && stopCodes.Contains(code))) {
                    return Finish(
                        report: report,
                        attempts: attempts,
                        selected: incumbent ? id : null);
                }
                continue;
            }

            IReadOnlyList<Error<ErrorCode>> errors = result switch {
                Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed => [failed.Error],
                Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> fatal => fatal.Errors,
                _ => [new Err<ErrorCode>(ErrorCode.OREngineSolvingException,
                    "求解器返回无效结果 / Solver returned an invalid result")],
            };
            attempts.Add(new SolveAttemptTrace<Flt64>(
                AttemptId: id,
                BackendId: backend,
                ParentAttemptId: new SolveAttemptId("serial-combinatorial"),
                Elapsed: elapsed,
                Errors: Array.AsReadOnly(errors.Select(ToIssue).ToArray())));
            if (result is Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ||
                errors.Any(error => stopCodes.Contains(error.Code))) {
                break;
            }
        }
        return Finish(
            report: null,
            attempts: attempts,
            selected: null);
    }

    internal static SolveReport<Flt64> ToTerminalReport(CombinatorialSolveReport<Flt64> combinatorial) {
        SolveReport<Flt64> report = combinatorial.FinalReport ?? new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Unknown,
            TerminationReason = TerminationReason.BackendFailure,
            SolutionPresence = SolutionPresence.None,
        };
        SolveIssue[] errors = report.Diagnostics.Errors.Concat(combinatorial.Attempts.SelectMany(attempt => attempt.Errors)).ToArray();
        if (errors.Length == 0 && combinatorial.FinalReport is null) {
            errors = [new SolveIssue(
                Code: "combinatorial-no-successful-attempt",
                Category: SolveIssueCategory.Backend,
                Message: "所有组合后端均未产生可选结果 / No combinatorial backend produced a selectable result")];
        }
        return report with {
            Diagnostics = report.Diagnostics with { Errors = Array.AsReadOnly(errors) },
            Attempts = combinatorial.Attempts,
            SelectedAttemptId = combinatorial.SelectedAttemptId,
            SelectionReason = combinatorial.SelectionReason,
        };
    }

    private static Result<CombinatorialSolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Finish(
        SolveReport<Flt64>? report,
        List<SolveAttemptTrace<Flt64>> attempts,
        SolveAttemptId? selected) => Results.Ok(new CombinatorialSolveReport<Flt64>(
            FinalReport: report,
            Attempts: Array.AsReadOnly(attempts.ToArray()),
            SelectedAttemptId: selected,
            SelectionReason: selected is null ? SolveSelectionReason.NoSuccessfulAttempt : SolveSelectionReason.FirstFeasible));

    internal static SolveReport<Flt64> Cancelled() => new() {
        ProblemStatus = ProblemStatus.Unknown,
        TerminationReason = TerminationReason.Cancelled,
        SolutionPresence = SolutionPresence.None,
    };

    internal static SolveIssue ToIssue(Error<ErrorCode> error) => new(
        Code: error.Code.ToString(),
        Category: error.Code switch {
            ErrorCode.IllegalArgument or ErrorCode.SymbolRepetitive or ErrorCode.DataEmpty or ErrorCode.DataNotFound => SolveIssueCategory.InvalidInput,
            ErrorCode.SerializationFailed or ErrorCode.DeserializationFailed => SolveIssueCategory.Parsing,
            ErrorCode.AuthenticationError => SolveIssueCategory.License,
            ErrorCode.SolverNotFound or ErrorCode.OREngineEnvironmentLost or ErrorCode.OREngineConnectionOvertime => SolveIssueCategory.Environment,
            ErrorCode.ORSolutionInvalid => SolveIssueCategory.Numerical,
            _ => SolveIssueCategory.Backend,
        },
        Message: error.Message);
}
