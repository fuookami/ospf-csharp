#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
namespace Fuookami.Ospf.Framework.Solver;

internal static class CombinatorialLegacySupport {
    internal static async Task<Result<T, ErrorCode, Error<ErrorCode>>> SolveAsync<TSolver, T>(
        Result<Success, ErrorCode, Error<ErrorCode>> validation,
        IReadOnlyList<Func<TSolver>> factories,
        ParallelCombinatorialMode mode,
        Func<ObjectCategory> category,
        Func<TSolver, Task<Result<T, ErrorCode, Error<ErrorCode>>>> solve,
        Func<T, (bool Valid, bool HasIncumbent, double? Objective, ErrorCode? StopCode, bool Cancelled)> describe,
        ILogger logger,
        CancellationToken cancellationToken) {
        if (InvalidValidation<T>(validation) is { } invalid) {
            return invalid;
        }
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled<T>();
        }
        if (mode is not ParallelCombinatorialMode.First and not ParallelCombinatorialMode.Best) {
            return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                "组合求解模式无效 / Invalid combinatorial solve mode"));
        }

        ObjectCategory direction = ObjectCategory.Minimum;
        if (mode == ParallelCombinatorialMode.Best) {
            try {
                direction = category();
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                ParallelCombinatorialReportSupport.LogFailure(logger, error);
                return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "读取模型目标失败 / Failed to read model objective"));
            }
            if (direction is not ObjectCategory.Minimum and not ObjectCategory.Maximum) {
                return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "模型目标方向无效 / Invalid model objective direction"));
            }
        }

        Task<Result<T, ErrorCode, Error<ErrorCode>>>[] tasks = factories.Select(factory => Task.Run(() => AttemptAsync(
            factory: factory,
            solve: solve,
            logger: logger,
            cancellationToken: cancellationToken))).ToArray();
        Result<T, ErrorCode, Error<ErrorCode>>[] results = await Task.WhenAll(tasks);
        var candidates = new List<(Result<T, ErrorCode, Error<ErrorCode>> Result, double? Objective)>();
        Result<T, ErrorCode, Error<ErrorCode>>? terminal = null;
        for (int i = 0; i < results.Length; i++) {
            if (results[i] is not Ok<T, ErrorCode, Error<ErrorCode>> ok) {
                continue;
            }
            (bool valid, bool incumbent, double? objective, _, _) = describe(ok.Value);
            if (!valid) {
                results[i] = Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid,
                    "求解器返回无效结果 / Solver returned an invalid result"));
            }
            else if (incumbent) {
                candidates.Add((results[i], objective));
            }
            else {
                terminal ??= results[i];
            }
        }
        if (candidates.Count > 0) {
            (Result<T, ErrorCode, Error<ErrorCode>> Result, double? Objective) selected = candidates[0];
            if (mode == ParallelCombinatorialMode.Best && candidates.All(candidate =>
                candidate.Objective is double value && double.IsFinite(value))) {
                foreach ((Result<T, ErrorCode, Error<ErrorCode>> Result, double? Objective) candidate in candidates.Skip(1)) {
                    if (ParallelCombinatorialReportSupport.IsBetter(
                        value: candidate.Objective.GetValueOrDefault(),
                        best: selected.Objective.GetValueOrDefault(),
                        direction: direction)) {
                        selected = candidate;
                    }
                }
            }
            return selected.Result;
        }
        if (terminal is not null) {
            return terminal;
        }
        return results.OfType<Fatal<T, ErrorCode, Error<ErrorCode>>>().FirstOrDefault()
            ?? (Result<T, ErrorCode, Error<ErrorCode>>?)results.OfType<Failed<T, ErrorCode, Error<ErrorCode>>>().FirstOrDefault()
            ?? Results.Failed<T>(new Err<ErrorCode>(ErrorCode.SolverNotFound,
                "没有可用的组合求解器 / No combinatorial solver was available"));
    }

    internal static async Task<Result<T, ErrorCode, Error<ErrorCode>>> SolveSerialAsync<TSolver, T>(
        Result<Success, ErrorCode, Error<ErrorCode>> validation,
        IReadOnlyList<Func<TSolver>> factories,
        IReadOnlySet<ErrorCode> stopCodes,
        Func<TSolver, Task<Result<T, ErrorCode, Error<ErrorCode>>>> solve,
        Func<T, (bool Valid, bool HasIncumbent, double? Objective, ErrorCode? StopCode, bool Cancelled)> describe,
        ILogger logger,
        CancellationToken cancellationToken) {
        if (InvalidValidation<T>(validation) is { } invalid) {
            return invalid;
        }
        Result<T, ErrorCode, Error<ErrorCode>>? fallback = null;
        Result<T, ErrorCode, Error<ErrorCode>>? firstFailure = null;
        foreach (Func<TSolver> factory in factories) {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled<T>();
            }
            Result<T, ErrorCode, Error<ErrorCode>> result = await AttemptAsync(
                factory: factory,
                solve: solve,
                logger: logger,
                cancellationToken: cancellationToken);
            if (result is Ok<T, ErrorCode, Error<ErrorCode>> ok) {
                (bool valid, bool incumbent, _, ErrorCode? stopCode, bool cancelled) = describe(ok.Value);
                if (!valid) {
                    result = Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid,
                        "求解器返回无效结果 / Solver returned an invalid result"));
                }
                else if (cancelled || incumbent || (stopCode is { } code && stopCodes.Contains(code))) {
                    return result;
                }
                else {
                    fallback ??= result;
                    continue;
                }
            }
            if (result is Fatal<T, ErrorCode, Error<ErrorCode>>) {
                return result;
            }
            if (result is Failed<T, ErrorCode, Error<ErrorCode>> failure) {
                if (failure.Code == ErrorCode.ApplicationStopped || stopCodes.Contains(failure.Code)) {
                    return result;
                }
                firstFailure ??= result;
            }
        }
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled<T>();
        }
        return fallback ?? firstFailure ?? Results.Failed<T>(new Err<ErrorCode>(ErrorCode.SolverNotFound,
            "没有可用的组合求解器 / No combinatorial solver was available"));
    }

    private static Result<T, ErrorCode, Error<ErrorCode>>? InvalidValidation<T>(
        Result<Success, ErrorCode, Error<ErrorCode>> validation) => validation switch {
        Ok<Success, ErrorCode, Error<ErrorCode>> => null,
        Failed<Success, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
        Fatal<Success, ErrorCode, Error<ErrorCode>> fatal => new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
        _ => Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
            "模型校验结果无效 / Invalid model validation result")),
    };

    internal static (bool Valid, bool HasIncumbent, double? Objective, ErrorCode? StopCode, bool Cancelled) Describe(FeasibleSolverOutput<Flt64>? output) =>
        output?.Solution is not null ? (true, true, output.Obj.ToDouble(), null, false) : (false, false, null, null, false);

    internal static (bool Valid, bool HasIncumbent, double? Objective, ErrorCode? StopCode, bool Cancelled) Describe(
        (FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions) output) =>
        output.Solutions is not null ? Describe(output.Output) : (false, false, null, null, false);

    internal static (bool Valid, bool HasIncumbent, double? Objective, ErrorCode? StopCode, bool Cancelled) Describe(SolverOutput? output) {
        if (output is ISolverOutputWrapper) {
            var visited = new HashSet<SolverOutput>(ReferenceEqualityComparer.Instance);
            try {
                while (output is ISolverOutputWrapper wrapper) {
                    // shortcut: 超过 1024 层被拒绝；需要更深诊断时改用有界扁平协议。 / shortcut: Rejects over 1024 layers; use a bounded flat protocol for deeper diagnostics.
                    if (visited.Count == 1024 || !visited.Add(output)) {
                        return (false, false, null, null, false);
                    }
                    output = wrapper.Output;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                return (false, false, null, null, false);
            }
        }
        return output switch {
            null => (false, false, null, null, false),
            FeasibleSolverOutput<Flt64> feasible => Describe(feasible),
            SolveReport<Flt64> report => (
                true,
                report.Solution is not null && report.SolutionPresence is SolutionPresence.Incumbent or SolutionPresence.Optimal,
                report.Solution?.Objective?.ToDouble(),
                report.ProblemStatus switch {
                    ProblemStatus.Infeasible => ErrorCode.ORModelInfeasible,
                    ProblemStatus.Unbounded => ErrorCode.ORModelUnbounded,
                    ProblemStatus.InfeasibleOrUnbounded => ErrorCode.ORModelInfeasibleOrUnbounded,
                    _ => null,
                },
                report.TerminationReason == TerminationReason.Cancelled),
            LinearInfeasibleSolverOutput linear => (true, false, null, linear.Status.ErrCode(), false),
            QuadraticInfeasibleSolverOutput quadratic => (true, false, null, quadratic.Status.ErrCode(), false),
            _ => (true, false, null, null, false),
        };
    }

    private static async Task<Result<T, ErrorCode, Error<ErrorCode>>> AttemptAsync<TSolver, T>(
        Func<TSolver> factory,
        Func<TSolver, Task<Result<T, ErrorCode, Error<ErrorCode>>>> solve,
        ILogger logger,
        CancellationToken cancellationToken) {
        try {
            Result<T, ErrorCode, Error<ErrorCode>> result = await solve(factory());
            return result switch {
                Ok<T, ErrorCode, Error<ErrorCode>> => result,
                Failed<T, ErrorCode, Error<ErrorCode>> { Error: not null } => result,
                Fatal<T, ErrorCode, Error<ErrorCode>> { Errors: not null } fatal when fatal.Errors.All(error => error is not null) => result,
                _ => Results.Failed<T>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException,
                    "求解器返回无效结果 / Solver returned an invalid result")),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return Cancelled<T>();
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            ParallelCombinatorialReportSupport.LogFailure(logger, error);
            return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException,
                "组合求解器调用异常 / Combinatorial solver invocation failed"));
        }
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Cancelled<T>() =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ApplicationStopped,
            "求解已取消 / Solve was cancelled"));
}
