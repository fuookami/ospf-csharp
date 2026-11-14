#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Solver.ConstraintProgramming;

/// <summary>
/// 小模型确定性穷举 CP 求解器，仅用于契约测试。
/// Deterministic exhaustive CP solver for small models and contract tests only.
/// </summary>
public sealed class FakeConstraintProgrammingSolver : IConstraintProgrammingSolver {
    /// <summary>默认单变量枚举上限 / Default per-variable enumeration limit.</summary>
    public const int DefaultEnumerationLimit = 100_000;

    private readonly int _enumerationLimit;

    /// <summary>创建 CP 契约求解器 / Create a CP contract solver.</summary>
    /// <param name="enumerationLimit">单个变量值域的枚举上限 / Per-variable domain enumeration limit.</param>
    public FakeConstraintProgrammingSolver(int enumerationLimit = DefaultEnumerationLimit) {
        _enumerationLimit = enumerationLimit;
        Descriptor = new SolverDescriptor(
            SolverId: "fake-cp",
            BackendName: "OSPF exhaustive CP test solver",
            Capabilities: new SolverCapabilities(
                ModelTypes: new HashSet<SolverModelType> { SolverModelType.Cp },
                Interrupt: true,
                ConstraintProgrammingFeatures: Enum
                    .GetValues<ConstraintProgrammingFeature>()
                    .ToDictionary(
                        feature => feature,
                        _ => ConstraintProgrammingSupportLevel.Native)));
    }

    /// <summary>求解器描述符 / Solver descriptor.</summary>
    public SolverDescriptor Descriptor { get; }

    /// <inheritdoc/>
    public async Task<Result<
        ConstraintProgrammingSolverOutput,
        ErrorCode,
        Error<ErrorCode>>> SolveAsync(
        ConstraintProgrammingModel model,
        ConstraintProgrammingSolveOptions? options = null,
        CancellationToken cancellationToken = default) {
        Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> created =
            CreateSession(model, options);
        if (created is Failed<
                IConstraintProgrammingSession,
                ErrorCode,
                Error<ErrorCode>> failed) {
            return Results.Failed<ConstraintProgrammingSolverOutput>(failed.Error);
        }

        using IConstraintProgrammingSession session = created.Value;
        return await session
            .SolveAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Result<
        IConstraintProgrammingSession,
        ErrorCode,
        Error<ErrorCode>> CreateSession(
        ConstraintProgrammingModel model,
        ConstraintProgrammingSolveOptions? options = null) {
        ArgumentNullException.ThrowIfNull(model);
        return _enumerationLimit > 0
            ? Results.Ok<IConstraintProgrammingSession>(
                new FakeSession(
                    model: model,
                    options: options ?? new ConstraintProgrammingSolveOptions(),
                    enumerationLimit: _enumerationLimit,
                    descriptor: Descriptor))
            : Failure<IConstraintProgrammingSession>(
                ErrorCode.IllegalArgument,
                "穷举规模上限必须为正 / Enumeration limit must be positive");
    }

    private sealed class FakeSession : IConstraintProgrammingSession {
        private bool _closed;

        public FakeSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions options,
            int enumerationLimit,
            SolverDescriptor descriptor) {
            Model = model;
            Options = options;
            EnumerationLimit = enumerationLimit;
            Descriptor = descriptor;
        }

        private int EnumerationLimit { get; }

        private SolverDescriptor Descriptor { get; }

        public ConstraintProgrammingModel Model { get; }

        public ConstraintProgrammingSolveOptions Options { get; }

        public bool IsClosed => _closed;

        public Task<Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>>> SolveAsync(
            IReadOnlyList<BooleanLiteral>? assumptions = null,
            IReadOnlyDictionary<VariableId, Int64>? fixedValues = null,
            ConstraintProgrammingSolution? hints = null,
            CancellationToken cancellationToken = default) {
            return Task.FromResult(Solve(
                assumptions ?? Array.Empty<BooleanLiteral>(),
                fixedValues ?? new Dictionary<VariableId, Int64>(),
                hints,
                cancellationToken));
        }

        public void Dispose() {
            if (_closed) {
                return;
            }
            _closed = true;
        }

        private Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> Solve(
            IReadOnlyList<BooleanLiteral> assumptions,
            IReadOnlyDictionary<VariableId, Int64> fixedValues,
            ConstraintProgrammingSolution? hints,
            CancellationToken cancellationToken) {
            if (_closed) {
                return Failure<ConstraintProgrammingSolverOutput>(
                    ErrorCode.ApplicationStopped,
                    "CP 会话已关闭 / CP session is closed");
            }

            var stopwatch = Stopwatch.StartNew();
            Result<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>> snapshotResult =
                Model.Snapshot();
            if (snapshotResult is Failed<
                    ConstraintProgrammingModelSnapshot,
                    ErrorCode,
                    Error<ErrorCode>> snapshotFailure) {
                return Results.Failed<ConstraintProgrammingSolverOutput>(
                    snapshotFailure.Error);
            }

            string? optionsError = ValidateOptions(Options);
            if (optionsError is not null) {
                return Failure<ConstraintProgrammingSolverOutput>(
                    ErrorCode.IllegalArgument,
                    optionsError);
            }

            ConstraintProgrammingModelSnapshot snapshot = snapshotResult.Value;
            Result<Dictionary<VariableId, Int64>, ErrorCode, Error<ErrorCode>> fixedResult =
                ValidateFixedValues(snapshot, fixedValues);
            if (fixedResult is Failed<Dictionary<VariableId, Int64>, ErrorCode, Error<ErrorCode>> fixedFailure) {
                return Results.Failed<ConstraintProgrammingSolverOutput>(
                    fixedFailure.Error);
            }

            Result<Dictionary<VariableId, Int64>, ErrorCode, Error<ErrorCode>> hintResult =
                ValidateHints(snapshot, hints, fixedResult.Value);
            if (hintResult is Failed<Dictionary<VariableId, Int64>, ErrorCode, Error<ErrorCode>> hintFailure) {
                return Results.Failed<ConstraintProgrammingSolverOutput>(
                    hintFailure.Error);
            }

            string? assumptionError = ValidateAssumptions(snapshot, assumptions);
            if (assumptionError is not null) {
                return Failure<ConstraintProgrammingSolverOutput>(
                    ErrorCode.IllegalArgument,
                    assumptionError);
            }

            var domains = new List<(VariableId Id, IReadOnlyList<Int64> Values)>();
            foreach (ConstraintProgrammingVariableSnapshot variable in snapshot.Variables) {
                IReadOnlyList<Int64>? domainValues;
                if (fixedValues.TryGetValue(variable.Id, out Int64 fixedValue)) {
                    domainValues = new[] { fixedValue };
                }
                else {
                    domainValues = variable.Domain.Enumerate(EnumerationLimit);
                }
                if (domainValues is null) {
                    return Results.Ok<ConstraintProgrammingSolverOutput>(
                        UnknownOutput(
                            snapshot: snapshot,
                            stopwatch: stopwatch,
                            terminationReason: TerminationReason.NodeLimit,
                            descriptor: Descriptor));
                }
                domains.Add((variable.Id, domainValues));
            }

            Candidate? best = null;
            StopReason stopReason = StopReason.None;
            Error<ErrorCode>? evaluationError = null;
            ulong assignmentCount = 0;
            ulong solutionCount = 0;
            var values = new Dictionary<VariableId, Int64>();

            void Visit(int index) {
                if (stopReason != StopReason.None) {
                    return;
                }
                if (cancellationToken.IsCancellationRequested) {
                    stopReason = StopReason.Cancelled;
                    return;
                }
                if (Options.CancellationToken?.IsCancellationRequested == true) {
                    stopReason = StopReason.Cancelled;
                    return;
                }
                if (Options.TimeLimit is { } timeLimit &&
                    stopwatch.Elapsed >= timeLimit) {
                    stopReason = StopReason.TimeLimit;
                    return;
                }
                if (Options.NodeLimit is { } nodeLimit &&
                    assignmentCount >= nodeLimit) {
                    stopReason = StopReason.NodeLimit;
                    return;
                }

                if (index == domains.Count) {
                    ++assignmentCount;
                    Result<CandidateEvaluation, ErrorCode, Error<ErrorCode>> evaluation =
                        EvaluateCandidate(
                            snapshot,
                            values,
                            assumptions);
                    if (evaluation is Failed<
                            CandidateEvaluation,
                            ErrorCode,
                            Error<ErrorCode>> evaluationFailure) {
                        stopReason = StopReason.EvaluationFailure;
                        evaluationError = evaluationFailure.Error;
                        return;
                    }
                    if (evaluation.Value.Satisfied &&
                        evaluation.Value.Candidate is { } candidate) {
                        ++solutionCount;
                        if (best is null ||
                            IsBetter(snapshot.ObjectCategory, candidate, best)) {
                            best = candidate;
                        }
                        if (Options.SolutionLimit is { } solutionLimit &&
                            solutionCount >= solutionLimit) {
                            stopReason = StopReason.SolutionLimit;
                        }
                    }
                    return;
                }

                (VariableId id, IReadOnlyList<Int64> domain) = domains[index];
                foreach (Int64 value in domain) {
                    values[id] = value;
                    Visit(index + 1);
                    values.Remove(id);
                    if (stopReason != StopReason.None) {
                        return;
                    }
                }
            }

            Dictionary<VariableId, Int64> hintedValues = hintResult.Value;
            if (snapshot.Variables.Count > 0 &&
                snapshot.Variables.All(variable => hintedValues.ContainsKey(variable.Id))) {
                Result<CandidateEvaluation, ErrorCode, Error<ErrorCode>> hintEvaluation =
                    EvaluateCandidate(snapshot, hintedValues, assumptions);
                if (hintEvaluation is Failed<
                        CandidateEvaluation,
                        ErrorCode,
                        Error<ErrorCode>> hintEvaluationFailure) {
                    return Results.Failed<ConstraintProgrammingSolverOutput>(
                        hintEvaluationFailure.Error);
                }
                if (hintEvaluation.Value.Satisfied &&
                    hintEvaluation.Value.Candidate is { } candidate) {
                    best = candidate;
                }
            }

            Visit(0);

            if (stopReason == StopReason.EvaluationFailure) {
                return Results.Failed<ConstraintProgrammingSolverOutput>(
                    evaluationError ?? new Err<ErrorCode>(
                        code: ErrorCode.ApplicationError,
                        message: "CP 穷举求值失败 / CP exhaustive evaluation failed"));
            }

            if (stopReason is StopReason.Cancelled or StopReason.TimeLimit or
                StopReason.NodeLimit or StopReason.SolutionLimit) {
                TerminationReason termination = stopReason switch {
                    StopReason.Cancelled => TerminationReason.Cancelled,
                    StopReason.TimeLimit => TerminationReason.TimeLimit,
                    StopReason.SolutionLimit => TerminationReason.SolutionLimit,
                    _ => TerminationReason.NodeLimit,
                };
                if (best is not null) {
                    return Results.Ok<ConstraintProgrammingSolverOutput>(
                        FeasibleOutput(
                            snapshot: snapshot,
                            candidate: best,
                            stopwatch: stopwatch,
                            terminationReason: termination,
                            descriptor: Descriptor,
                            proofStatus: ProofStatus.None,
                            solverStatus: SolverStatus.Feasible));
                }
                return Results.Ok<ConstraintProgrammingSolverOutput>(
                    UnknownOutput(
                        snapshot: snapshot,
                        stopwatch: stopwatch,
                        terminationReason: termination,
                        descriptor: Descriptor));
            }

            if (best is not null) {
                return Results.Ok<ConstraintProgrammingSolverOutput>(
                    FeasibleOutput(
                        snapshot: snapshot,
                        candidate: best,
                        stopwatch: stopwatch,
                        terminationReason: TerminationReason.Completed,
                        descriptor: Descriptor,
                        proofStatus: ProofStatus.Verified,
                        solverStatus: SolverStatus.Optimal));
            }

            var conflict = new ConstraintProgrammingConflict(
                constraintIds: snapshot.Constraints.Select(constraint => constraint.Id),
                message: "所有 CP 赋值均违反约束 / Every CP assignment violates the constraints",
                validity: EvidenceValidity.Verified);
            return Results.Ok<ConstraintProgrammingSolverOutput>(
                new ConstraintProgrammingInfeasibleOutput(
                    conflict: conflict,
                    proofStatus: ProofStatus.Verified,
                    report: Report(
                        snapshot: snapshot,
                        stopwatch: stopwatch,
                        problemStatus: ProblemStatus.Infeasible,
                        terminationReason: TerminationReason.Completed,
                        solutionPresence: SolutionPresence.None,
                        candidate: null,
                        proofStatus: ProofStatus.Verified,
                        descriptor: Descriptor)));
        }

        private static string? ValidateOptions(ConstraintProgrammingSolveOptions options) {
            if (options.TimeLimit is { } timeLimit && timeLimit < TimeSpan.Zero) {
                return "timeLimit 不得为负 / timeLimit must not be negative";
            }
            if (options.SolutionLimit == 0) {
                return "solutionLimit 必须为正 / solutionLimit must be positive";
            }
            if (options.NodeLimit == 0) {
                return "nodeLimit 必须为正 / nodeLimit must be positive";
            }
            if (options.ThreadCount is <= 0) {
                return "threadCount 必须为正 / threadCount must be positive";
            }
            return null;
        }

        private static Result<
            Dictionary<VariableId, Int64>,
            ErrorCode,
            Error<ErrorCode>> ValidateFixedValues(
            ConstraintProgrammingModelSnapshot snapshot,
            IReadOnlyDictionary<VariableId, Int64> fixedValues) {
            var values = new Dictionary<VariableId, Int64>();
            foreach ((VariableId id, Int64 value) in fixedValues) {
                ConstraintProgrammingVariableSnapshot? variable = snapshot.Variable(id);
                if (variable is null) {
                    return Failure<Dictionary<VariableId, Int64>>(
                        ErrorCode.DataNotFound,
                        $"固定值引用了未知变量：{id.Value} / Fixed value references an unknown variable: {id.Value}");
                }
                if (!variable.Domain.Contains(value)) {
                    return Failure<Dictionary<VariableId, Int64>>(
                        ErrorCode.ORSolutionInvalid,
                        $"固定值超出变量值域：{id.Value}={value} / Fixed value is outside the variable domain: {id.Value}={value}");
                }
                values[id] = value;
            }
            return Results.Ok(values);
        }

        private static Result<
            Dictionary<VariableId, Int64>,
            ErrorCode,
            Error<ErrorCode>> ValidateHints(
            ConstraintProgrammingModelSnapshot snapshot,
            ConstraintProgrammingSolution? hints,
            IReadOnlyDictionary<VariableId, Int64> fixedValues) {
            var values = new Dictionary<VariableId, Int64>(fixedValues);
            if (hints is null) {
                return Results.Ok(values);
            }

            foreach ((VariableId id, Int64 value) in hints.Values) {
                ConstraintProgrammingVariableSnapshot? variable = snapshot.Variable(id);
                if (variable is null) {
                    return Failure<Dictionary<VariableId, Int64>>(
                        ErrorCode.DataNotFound,
                        $"CP hint 引用了未知变量：{id.Value} / CP hint references an unknown variable: {id.Value}");
                }
                if (!variable.Domain.Contains(value)) {
                    return Failure<Dictionary<VariableId, Int64>>(
                        ErrorCode.ORSolutionInvalid,
                        $"CP hint 超出变量值域：{id.Value}={value} / CP hint is outside the variable domain: {id.Value}={value}");
                }
                if (values.TryGetValue(id, out Int64 existing) && existing != value) {
                    return Failure<Dictionary<VariableId, Int64>>(
                        ErrorCode.ORSolutionInvalid,
                        $"CP hint 与固定值冲突：{id.Value} / CP hint conflicts with a fixed value: {id.Value}");
                }
                values[id] = value;
            }

            foreach ((IntervalId id, IntervalValue hint) in hints.Intervals) {
                IntervalVariable? interval = snapshot.Interval(id);
                if (interval is null) {
                    return Failure<Dictionary<VariableId, Int64>>(
                        ErrorCode.DataNotFound,
                        $"CP hint 引用了未知 interval：{id.Value} / CP hint references an unknown interval: {id.Value}");
                }
                int presenceValue = hint.Present ? 1 : 0;
                if (interval.Presence?.VariableId is { } presenceId) {
                    if (values.TryGetValue(presenceId, out Int64 existing) &&
                        existing.ToLong() != presenceValue) {
                        return Failure<Dictionary<VariableId, Int64>>(
                            ErrorCode.ORSolutionInvalid,
                            $"CP interval hint 与 presence literal 冲突：{id.Value} / CP interval hint conflicts with its presence literal: {id.Value}");
                    }
                    values[presenceId] = new Int64(presenceValue);
                }
                if (hint.Present) {
                    Result<Dictionary<VariableId, Int64>, ErrorCode, Error<ErrorCode>> start =
                        PutHint(values, snapshot, interval.Start, hint.Start, id);
                    if (start.IsFailed) {
                        return start;
                    }
                    Result<Dictionary<VariableId, Int64>, ErrorCode, Error<ErrorCode>> end =
                        PutHint(values, snapshot, interval.End, hint.End, id);
                    if (end.IsFailed) {
                        return end;
                    }
                    Result<Dictionary<VariableId, Int64>, ErrorCode, Error<ErrorCode>> size =
                        PutHint(values, snapshot, interval.Size, hint.Size, id);
                    if (size.IsFailed) {
                        return size;
                    }
                }
                Result<IntervalValue, ErrorCode, Error<ErrorCode>> evaluated =
                    interval.Evaluate(values);
                if (evaluated is Failed<IntervalValue, ErrorCode, Error<ErrorCode>> failed) {
                    return Results.Failed<Dictionary<VariableId, Int64>>(failed.Error);
                }
                if (evaluated.Value != hint) {
                    return Failure<Dictionary<VariableId, Int64>>(
                        ErrorCode.ORSolutionInvalid,
                        $"CP interval hint 不满足 interval 定义：{id.Value} / CP interval hint does not satisfy the interval definition: {id.Value}");
                }
            }
            return Results.Ok(values);
        }

        private static Result<
            Dictionary<VariableId, Int64>,
            ErrorCode,
            Error<ErrorCode>> PutHint(
            Dictionary<VariableId, Int64> values,
            ConstraintProgrammingModelSnapshot snapshot,
            ConstraintProgrammingExpression expression,
            Int64 value,
            IntervalId intervalId) {
            if (expression is not ConstraintProgrammingExpression.Variable variable) {
                return Results.Ok(values);
            }
            ConstraintProgrammingVariableSnapshot? definition = snapshot.Variable(variable.VariableId);
            if (definition is null) {
                return Failure<Dictionary<VariableId, Int64>>(
                    ErrorCode.DataNotFound,
                    $"CP interval hint 引用了未知变量：{variable.VariableId.Value} / CP interval hint references an unknown variable: {variable.VariableId.Value}");
            }
            if (!definition.Domain.Contains(value)) {
                return Failure<Dictionary<VariableId, Int64>>(
                    ErrorCode.ORSolutionInvalid,
                    $"CP interval hint 超出变量值域：{intervalId.Value} / CP interval hint is outside the variable domain: {intervalId.Value}");
            }
            if (values.TryGetValue(variable.VariableId, out Int64 existing) &&
                existing != value) {
                return Failure<Dictionary<VariableId, Int64>>(
                    ErrorCode.ORSolutionInvalid,
                    $"CP interval hint 变量值冲突：{variable.VariableId.Value} / CP interval hint has conflicting values: {variable.VariableId.Value}");
            }
            values[variable.VariableId] = value;
            return Results.Ok(values);
        }

        private static string? ValidateAssumptions(
            ConstraintProgrammingModelSnapshot snapshot,
            IReadOnlyList<BooleanLiteral> assumptions) {
            foreach (BooleanLiteral assumption in assumptions) {
                if (assumption.VariableId is { } id && snapshot.Variable(id) is null) {
                    return "assumption 引用了未注册变量 / Assumption references an unregistered variable";
                }
            }
            return null;
        }

        private static Result<
            CandidateEvaluation,
            ErrorCode,
            Error<ErrorCode>> EvaluateCandidate(
            ConstraintProgrammingModelSnapshot snapshot,
            IReadOnlyDictionary<VariableId, Int64> values,
            IReadOnlyList<BooleanLiteral> assumptions) {
            foreach (BooleanLiteral assumption in assumptions) {
                Result<bool, ErrorCode, Error<ErrorCode>> evaluated =
                    assumption.EvaluateInteger(values);
                if (evaluated is Failed<bool, ErrorCode, Error<ErrorCode>> failed) {
                    return Results.Failed<CandidateEvaluation>(failed.Error);
                }
                if (!evaluated.Value) {
                    return Results.Ok(new CandidateEvaluation(false, null));
                }
            }

            var intervals = new Dictionary<IntervalId, IntervalValue>();
            foreach (IntervalVariable interval in snapshot.Intervals) {
                Result<IntervalValue, ErrorCode, Error<ErrorCode>> evaluated =
                    interval.Evaluate(values);
                if (evaluated is Failed<IntervalValue, ErrorCode, Error<ErrorCode>> failed) {
                    return Results.Failed<CandidateEvaluation>(failed.Error);
                }
                intervals[interval.Id] = evaluated.Value;
            }

            foreach (ConstraintProgrammingConstraintSnapshot constraint in snapshot.Constraints) {
                Result<bool, ErrorCode, Error<ErrorCode>> evaluated =
                    constraint.Constraint.IsSatisfied(values);
                if (evaluated is Failed<bool, ErrorCode, Error<ErrorCode>> failed) {
                    return Results.Failed<CandidateEvaluation>(failed.Error);
                }
                if (!evaluated.Value) {
                    return Results.Ok(new CandidateEvaluation(false, null));
                }
            }

            Int64? objective = null;
            if (snapshot.Objectives.Count > 0) {
                Result<Int64, ErrorCode, Error<ErrorCode>> evaluated =
                    snapshot.Objectives[0].Expression.Evaluate(values);
                if (evaluated is Failed<Int64, ErrorCode, Error<ErrorCode>> failed) {
                    return Results.Failed<CandidateEvaluation>(failed.Error);
                }
                objective = evaluated.Value;
            }
            return Results.Ok(new CandidateEvaluation(
                true,
                new Candidate(
                    Values: new Dictionary<VariableId, Int64>(values),
                    Intervals: intervals,
                    Objective: objective)));
        }

        private static bool IsBetter(
            ObjectCategory category,
            Candidate candidate,
            Candidate incumbent) {
            if (candidate.Objective is null) {
                return false;
            }
            if (incumbent.Objective is null) {
                return true;
            }
            return category == ObjectCategory.Minimum
                ? candidate.Objective.Value < incumbent.Objective.Value
                : candidate.Objective.Value > incumbent.Objective.Value;
        }

        private static ConstraintProgrammingFeasibleOutput FeasibleOutput(
            ConstraintProgrammingModelSnapshot snapshot,
            Candidate candidate,
            Stopwatch stopwatch,
            TerminationReason terminationReason,
            SolverDescriptor descriptor,
            ProofStatus proofStatus,
            SolverStatus solverStatus) =>
            new(
                solution: new ConstraintProgrammingSolution(
                    values: candidate.Values,
                    intervals: candidate.Intervals),
                objective: candidate.Objective is { } objective
                    ? ConstraintProgrammingSolution.ToCompatibilityFlt64(objective)
                    : null,
                bestBound: candidate.Objective is { } bestBound
                    ? ConstraintProgrammingSolution.ToCompatibilityFlt64(bestBound)
                    : null,
                status: solverStatus,
                proofStatus: proofStatus,
                report: Report(
                    snapshot: snapshot,
                    stopwatch: stopwatch,
                    problemStatus: ProblemStatus.Feasible,
                    terminationReason: terminationReason,
                    solutionPresence: solverStatus == SolverStatus.Optimal
                        ? SolutionPresence.Optimal
                        : SolutionPresence.Incumbent,
                    candidate: candidate,
                    proofStatus: proofStatus,
                    descriptor: descriptor),
                exactObjective: candidate.Objective);

        private static ConstraintProgrammingUnknownOutput UnknownOutput(
            ConstraintProgrammingModelSnapshot snapshot,
            Stopwatch stopwatch,
            TerminationReason terminationReason,
            SolverDescriptor descriptor) =>
            new(
                terminationReason: terminationReason,
                report: Report(
                    snapshot: snapshot,
                    stopwatch: stopwatch,
                    problemStatus: ProblemStatus.Unknown,
                    terminationReason: terminationReason,
                    solutionPresence: SolutionPresence.None,
                    candidate: null,
                    proofStatus: ProofStatus.None,
                    descriptor: descriptor));

        private static SolveReport<Int64> Report(
            ConstraintProgrammingModelSnapshot snapshot,
            Stopwatch stopwatch,
            ProblemStatus problemStatus,
            TerminationReason terminationReason,
            SolutionPresence solutionPresence,
            Candidate? candidate,
            ProofStatus proofStatus,
            SolverDescriptor descriptor) {
            SolveSolution<Int64>? solution = candidate is null
                ? null
                : new SolveSolution<Int64>(
                    Values: new Solution<Int64>(
                        snapshot.Variables
                            .Select(variable => candidate.Values[variable.Id])
                            .ToList()),
                    Objective: candidate.Objective);
            return new SolveReport<Int64> {
                ProblemStatus = problemStatus,
                TerminationReason = terminationReason,
                SolutionPresence = solutionPresence,
                Solution = solution,
                Proof = new SolveProof(
                    Status: proofStatus,
                    Kind: "exhaustive-enumeration"),
                Statistics = new SolveStatistics(
                    SolveTime: stopwatch.Elapsed),
                Provenance = new SolverProvenance(
                    Descriptor: descriptor,
                    Deterministic: true),
            };
        }
    }

    private sealed record CandidateEvaluation(
        bool Satisfied,
        Candidate? Candidate);

    private sealed record Candidate(
        Dictionary<VariableId, Int64> Values,
        Dictionary<IntervalId, IntervalValue> Intervals,
        Int64? Objective);

    private enum StopReason {
        None,
        TimeLimit,
        NodeLimit,
        SolutionLimit,
        Cancelled,
        EvaluationFailure,
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));
}
