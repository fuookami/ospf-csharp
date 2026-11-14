#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Solver;
using ReportResult = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Solver.Report.SolveReport<Fuookami.Ospf.Math.Algebra.Number.Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Framework.Tests.Solver;

public class SerialCombinatorialReportTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RetainsFailedAndNoIncumbentAttemptsBeforeSuccess(bool quadratic, bool pool) {
        var failed = new Fake("failed", (_, _, _) => Task.FromResult(Results.Failed<SolveReport<Flt64>>(
            new Err<ErrorCode>(ErrorCode.IllegalArgument, "bad input"))));
        var limited = new Fake("limited", (_, _, _) => Task.FromResult(Results.Ok(Empty(TerminationReason.TimeLimit))));
        SolveReport<Flt64> native = Feasible();
        var success = new Fake("success", (_, _, _) => Task.FromResult(Results.Ok(native)));
        var unused = new Fake("unused", (_, _, _) => throw new InvalidOperationException());
        using var cancellation = new CancellationTokenSource();
        SolvingStatusCallBack callback = _ => Results.OkInstance;

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await RunAsync(
            quadratic: quadratic,
            pool: pool,
            factories: [() => failed, () => limited, () => success, () => unused],
            cancellationToken: cancellation.Token,
            callback: callback);

        Assert.True(result.IsOk, result.ToString());
        SolveReport<Flt64> report = result.Value;
        Assert.Equal(ProblemStatus.Feasible, report.ProblemStatus);
        Assert.Equal(TerminationReason.TimeLimit, report.TerminationReason);
        Assert.Equal(ProofStatus.Claimed, report.Proof.Status);
        Assert.Same(native.Solution, report.Solution);
        Assert.Single(report.Solution!.Pool);
        Assert.Equal(new SolveAttemptId("serial-2"), report.SelectedAttemptId);
        Assert.Equal(SolveSelectionReason.FirstFeasible, report.SelectionReason);
        Assert.Equal(new[] { "failed", "limited", "success" }, report.Attempts.Select(attempt => attempt.BackendId));
        Assert.All(report.Attempts, attempt => {
            Assert.Equal(new SolveAttemptId("serial-combinatorial"), attempt.ParentAttemptId);
            Assert.NotNull(attempt.Elapsed);
            Assert.True(attempt.Elapsed >= TimeSpan.Zero);
        });
        Assert.Equal(SolveIssueCategory.InvalidInput, Assert.Single(report.Diagnostics.Errors).Category);
        Assert.Same(native, report.Attempts[2].Report);
        Assert.Equal(0, unused.Calls);
        foreach (Fake fake in new[] { failed, limited, success }) {
            Assert.Equal(pool ? 2UL : null, fake.LastAmount);
            Assert.Equal(cancellation.Token, fake.LastToken);
            Assert.Same(callback, fake.LastCallback);
        }
        Assert.Throws<NotSupportedException>(() => ((IList<SolveAttemptTrace<Flt64>>)report.Attempts).Add(report.Attempts[0]));
    }

    [Theory]
    [InlineData(false, false, ProblemStatus.Infeasible)]
    [InlineData(false, true, ProblemStatus.Unbounded)]
    [InlineData(true, false, ProblemStatus.Unbounded)]
    [InlineData(true, true, ProblemStatus.Infeasible)]
    public async Task KnownNoIncumbentTerminalIsNotSelected(bool quadratic, bool pool, ProblemStatus status) {
        SolveReport<Flt64> terminal = Empty(TerminationReason.Completed) with {
            ProblemStatus = status,
            Proof = new SolveProof(ProofStatus.Claimed),
        };
        var first = new Fake("terminal", (_, _, _) => Task.FromResult(Results.Ok(terminal)));
        var next = new Fake("unused", (_, _, _) => Task.FromResult(Results.Ok(Feasible())));
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: pool,
            factories: [() => first, () => next]);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(status, result.Value.ProblemStatus);
        Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
        Assert.Null(result.Value.SelectedAttemptId);
        Assert.Equal(SolveSelectionReason.NoSuccessfulAttempt, result.Value.SelectionReason);
        Assert.Single(result.Value.Attempts);
        Assert.Equal(0, next.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingSolutionOrPresenceDoesNotBecomeASelectedIncumbent(bool quadratic) {
        var missingSolution = new Fake("no-solution", (_, _, _) => Task.FromResult(Results.Ok(
            Empty(TerminationReason.TimeLimit) with { SolutionPresence = SolutionPresence.Incumbent })));
        var missingPresence = new Fake("no-presence", (_, _, _) => Task.FromResult(Results.Ok(
            Feasible() with { ProblemStatus = ProblemStatus.Unknown, SolutionPresence = SolutionPresence.None })));
        var success = new Fake("success", (_, _, _) => Task.FromResult(Results.Ok(Feasible())));
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            factories: [() => missingSolution, () => missingPresence, () => success]);
        Assert.True(result.IsOk);
        Assert.Equal(3, result.Value.Attempts.Count);
        Assert.Equal(new SolveAttemptId("serial-2"), result.Value.SelectedAttemptId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StopCodesControlBothReportAndLegacyFailure(bool quadratic, bool pool) {
        var ambiguous = new Fake("ambiguous", (_, _, _) => Task.FromResult(Results.Ok(
            Empty(TerminationReason.Completed) with { ProblemStatus = ProblemStatus.InfeasibleOrUnbounded })));
        var failure = new Fake("failure", (_, _, _) => Task.FromResult(Results.Failed<SolveReport<Flt64>>(
            new Err<ErrorCode>(ErrorCode.ORModelUnbounded, "unbounded failure"))));
        var last = new Fake("last", (_, _, _) => Task.FromResult(Results.Ok(Feasible())));
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: pool,
            factories: [() => ambiguous, () => failure, () => last]);
        Assert.True(result.IsOk);
        Assert.Equal(TerminationReason.BackendFailure, result.Value.TerminationReason);
        Assert.Equal(2, result.Value.Attempts.Count);
        Assert.Equal("ORModelUnbounded", Assert.Single(result.Value.Diagnostics.Errors).Code);
        Assert.Equal(0, last.Calls);

        result = await RunAsync(
            quadratic: quadratic,
            pool: pool,
            factories: [() => ambiguous, () => last],
            stopCodes: new HashSet<ErrorCode> { ErrorCode.ORModelInfeasibleOrUnbounded });
        Assert.True(result.IsOk);
        Assert.Equal(ProblemStatus.InfeasibleOrUnbounded, result.Value.ProblemStatus);
        Assert.Single(result.Value.Attempts);
        Assert.Null(result.Value.SelectedAttemptId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FatalAndExceptionsRetainErrorsWithoutLosingAttempts(bool quadratic) {
        var thrown = new Fake("throwing", (_, _, _) => throw new InvalidOperationException("private detail"));
        var fatal = new Fake("fatal", (_, _, _) => Task.FromResult<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(
            new Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.AuthenticationError, "license"),
                new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, "numerical"))));
        var unused = new Fake("unused", (_, _, _) => Task.FromResult(Results.Ok(Feasible())));
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            factories: [() => thrown, () => fatal, () => unused]);
        Assert.True(result.IsOk);
        Assert.Equal(TerminationReason.BackendFailure, result.Value.TerminationReason);
        Assert.Equal(2, result.Value.Attempts.Count);
        Assert.Equal(new[] { SolveIssueCategory.Backend, SolveIssueCategory.License, SolveIssueCategory.Numerical },
            result.Value.Diagnostics.Errors.Select(error => error.Category));
        Assert.DoesNotContain("private detail", result.Value.Diagnostics.Errors[0].Message);
        Assert.Equal(0, unused.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationPreventsNextFactoryAndRetainsCompletedAttempt(bool quadratic, bool pool) {
        int initialized = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fake = new Fake("fake", (_, _, _) => Task.FromResult(Results.Ok(Feasible())));
        ReportResult preCancelled = await RunAsync(
            quadratic: quadratic,
            pool: pool,
            factories: [() => { initialized++; return fake; }],
            cancellationToken: cancellation.Token);
        Assert.True(preCancelled.IsOk);
        Assert.Equal(TerminationReason.Cancelled, preCancelled.Value.TerminationReason);
        Assert.Empty(preCancelled.Value.Attempts);
        Assert.Equal(0, initialized);

        using var during = new CancellationTokenSource();
        var first = new Fake("first", (_, _, _) => {
            during.Cancel();
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(ErrorCode.Other, "failure")));
        });
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: pool,
            factories: [() => first, () => { initialized++; return fake; }],
            cancellationToken: during.Token);
        Assert.True(result.IsOk);
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.Null(Assert.Single(result.Value.Attempts).CancellationReason);
        Assert.Equal("Other", Assert.Single(result.Value.Diagnostics.Errors).Code);
        Assert.Equal(0, initialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledReportAndLateCancellationDoNotInventSelection(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        var cancelled = new Fake("cancelled", (_, _, _) => Task.FromResult(Results.Ok(
            Feasible() with { TerminationReason = TerminationReason.Cancelled })));
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            factories: [() => cancelled],
            cancellationToken: cancellation.Token);
        Assert.True(result.IsOk);
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.Null(result.Value.SelectedAttemptId);
        Assert.Equal("cancellation requested", Assert.Single(result.Value.Attempts).CancellationReason);

        var completed = new Fake("completed", (_, _, _) => {
            cancellation.Cancel();
            return Task.FromResult(Results.Ok(Feasible()));
        });
        result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            factories: [() => completed],
            cancellationToken: cancellation.Token);
        Assert.True(result.IsOk);
        Assert.Equal(TerminationReason.TimeLimit, result.Value.TerminationReason);
        Assert.Equal(new SolveAttemptId("serial-0"), result.Value.SelectedAttemptId);
        Assert.Null(Assert.Single(result.Value.Attempts).CancellationReason);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InvalidModelIsRejectedBeforeAnyFactory(bool quadratic, bool pool) {
        int calls = 0;
        var fake = new Fake("fake", (_, _, _) => Task.FromResult(Results.Ok(Feasible())));
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: pool,
            factories: [() => { calls++; return fake; }],
            invalidModel: true);
        Assert.True(result.IsFailed);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericReportUsesCheckedConversionAndPreservesAttempts(bool quadratic) {
        var fake = new Fake("fake", (_, _, _) => Task.FromResult(Results.Ok(Feasible())));
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> converted;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            IAbstractQuadraticSolver solver = new SerialCombinatorialQuadraticSolver(new IAbstractQuadraticSolver[] { fake });
            converted = await solver.SolveReportAsync(model, IIntoValue<Flt64>.Identity);
            Assert.Single(converted.Value.Attempts);
            converted = await solver.SolveReportAsync(model, new ZeroConverter());
        }
        else {
            using LinearTriadModel model = LinearModel();
            IAbstractLinearSolver solver = new SerialCombinatorialLinearSolver(new IAbstractLinearSolver[] { fake });
            converted = await solver.SolveReportAsync(model, IIntoValue<Flt64>.Identity);
            Assert.Single(converted.Value.Attempts);
            converted = await solver.SolveReportAsync(model, new ZeroConverter());
        }
        Assert.Equal(ErrorCode.IllegalArgument,
            Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(converted).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptySolversDistinguishPreCancellationFromNoAttempt(bool quadratic) {
        ReportResult empty = await RunAsync(
            quadratic: quadratic,
            pool: false,
            factories: []);
        Assert.True(empty.IsOk);
        Assert.Equal(TerminationReason.BackendFailure, empty.Value.TerminationReason);
        Assert.Equal("combinatorial-no-successful-attempt", Assert.Single(empty.Value.Diagnostics.Errors).Code);
        Assert.Empty(empty.Value.Attempts);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ReportResult cancelled = await RunAsync(
            quadratic: quadratic,
            pool: true,
            factories: [],
            cancellationToken: cancellation.Token);
        Assert.True(cancelled.IsOk);
        Assert.Equal(TerminationReason.Cancelled, cancelled.Value.TerminationReason);
        Assert.Empty(cancelled.Value.Attempts);
        Assert.Empty(cancelled.Value.Diagnostics.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonterminalReportIsRetainedWithoutClaimingThatNoReportExists(bool quadratic) {
        SolveReport<Flt64> limited = Empty(TerminationReason.TimeLimit);
        var fake = new Fake("limited", (_, _, _) => Task.FromResult(Results.Ok(limited)));
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            factories: [() => fake]);
        Assert.True(result.IsOk);
        Assert.Equal(TerminationReason.BackendFailure, result.Value.TerminationReason);
        Assert.Same(limited, Assert.Single(result.Value.Attempts).Report);
        SolveIssue issue = Assert.Single(result.Value.Diagnostics.Errors);
        Assert.Contains("selectable result", issue.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationExceptionIsTerminalOnlyWhenTokenIsCancelled(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        var throwing = new Fake("throwing", (_, _, _) => throw new OperationCanceledException());
        var success = new Fake("success", (_, _, _) => Task.FromResult(Results.Ok(Feasible())));
        ReportResult recovered = await RunAsync(
            quadratic: quadratic,
            pool: false,
            factories: [() => throwing, () => success],
            cancellationToken: cancellation.Token);
        Assert.True(recovered.IsOk);
        Assert.Equal(new SolveAttemptId("serial-1"), recovered.Value.SelectedAttemptId);
        Assert.Equal("OREngineSolvingException", Assert.Single(recovered.Value.Diagnostics.Errors).Code);

        var cancelled = new Fake("cancelled", (_, _, _) => {
            cancellation.Cancel();
            throw new OperationCanceledException();
        });
        ReportResult stopped = await RunAsync(
            quadratic: quadratic,
            pool: true,
            factories: [() => cancelled, () => success],
            cancellationToken: cancellation.Token);
        Assert.True(stopped.IsOk);
        Assert.Equal(TerminationReason.Cancelled, stopped.Value.TerminationReason);
        Assert.Equal("cancellation requested", Assert.Single(stopped.Value.Attempts).CancellationReason);
        Assert.Empty(stopped.Value.Diagnostics.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenLoggerCannotMaskFactoryFailureOrLaterReport(bool quadratic) {
        var success = new Fake("success", (_, _, _) => Task.FromResult(Results.Ok(Feasible())));
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            factories: [() => throw new InvalidOperationException("secret"), () => success],
            logger: new ParallelCombinatorialLegacyTest.ThrowingLogger());
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(new SolveAttemptId("serial-1"), result.Value.SelectedAttemptId);
        Assert.Equal("OREngineSolvingException", Assert.Single(result.Value.Diagnostics.Errors).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CombinatorialEntryKeepsSelectionAndOriginalBackendReport(bool quadratic) {
        SolveReport<Flt64> report = Feasible();
        var fake = new Fake("fake", (_, _, _) => Task.FromResult(Results.Ok(report)));
        Result<CombinatorialSolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            var solver = new SerialCombinatorialQuadraticSolver(new IAbstractQuadraticSolver[] { fake });
            result = await solver.SolveCombinatorialReportAsync(model, 2);
        }
        else {
            using LinearTriadModel model = LinearModel();
            var solver = new SerialCombinatorialLinearSolver(new IAbstractLinearSolver[] { fake });
            result = await solver.SolveCombinatorialReportAsync(model, 2);
        }
        Assert.True(result.IsOk);
        Assert.Same(report, result.Value.FinalReport);
        Assert.Same(report, Assert.Single(result.Value.Attempts).Report);
        Assert.Equal(new SolveAttemptId("serial-0"), result.Value.SelectedAttemptId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FactoriesAndStopCodesAreSnapshottedWithoutEagerInitialization(bool quadratic) {
        int initializations = 0;
        var terminal = new Fake("terminal", (_, _, _) => Task.FromResult(Results.Ok(
            Empty(TerminationReason.Completed) with { ProblemStatus = ProblemStatus.InfeasibleOrUnbounded })));
        var next = new Fake("next", (_, _, _) => Task.FromResult(Results.Ok(Feasible())));
        var stopCodes = new HashSet<ErrorCode> { ErrorCode.ORModelInfeasibleOrUnbounded };
        ReportResult result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            var factories = new List<Func<IAbstractQuadraticSolver>> {
                () => { initializations++; return terminal; },
                () => { initializations++; return next; },
            };
            IAbstractQuadraticSolver solver = new SerialCombinatorialQuadraticSolver(factories, stopCodes: stopCodes);
            factories.Clear();
            stopCodes.Clear();
            Assert.Equal(0, initializations);
            result = await solver.InvokeReportAsync(model);
        }
        else {
            using LinearTriadModel model = LinearModel();
            var factories = new List<Func<IAbstractLinearSolver>> {
                () => { initializations++; return terminal; },
                () => { initializations++; return next; },
            };
            IAbstractLinearSolver solver = new SerialCombinatorialLinearSolver(factories, stopCodes: stopCodes);
            factories.Clear();
            stopCodes.Clear();
            Assert.Equal(0, initializations);
            result = await solver.InvokeReportAsync(model);
        }
        Assert.True(result.IsOk);
        Assert.Equal(ProblemStatus.InfeasibleOrUnbounded, result.Value.ProblemStatus);
        Assert.Equal(1, initializations);
        Assert.Equal(0, next.Calls);
    }

    private static async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> RunAsync(
        bool quadratic,
        bool pool,
        IReadOnlyList<Func<Fake>> factories,
        CancellationToken cancellationToken = default,
        IReadOnlySet<ErrorCode>? stopCodes = null,
        SolvingStatusCallBack? callback = null,
        bool invalidModel = false,
        Microsoft.Extensions.Logging.ILogger? logger = null) {
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            if (invalidModel) { model.Dispose(); }
            IReadOnlyList<Func<IAbstractQuadraticSolver>> typed = factories.Select(factory => (Func<IAbstractQuadraticSolver>)(() => factory())).ToArray();
            IAbstractQuadraticSolver solver = new SerialCombinatorialQuadraticSolver(
                solverFactories: typed,
                logger: logger,
                stopCodes: stopCodes);
            return pool
                ? await solver.InvokeReportAsync(
                    model: model,
                    solutionAmount: 2,
                    solvingStatusCallBack: callback,
                    cancellationToken: cancellationToken)
                : await solver.InvokeReportAsync(
                    model: model,
                    solvingStatusCallBack: callback,
                    cancellationToken: cancellationToken);
        }
        else {
            using LinearTriadModel model = LinearModel();
            if (invalidModel) { model.Dispose(); }
            IReadOnlyList<Func<IAbstractLinearSolver>> typed = factories.Select(factory => (Func<IAbstractLinearSolver>)(() => factory())).ToArray();
            IAbstractLinearSolver solver = new SerialCombinatorialLinearSolver(
                solverFactories: typed,
                logger: logger,
                stopCodes: stopCodes);
            return pool
                ? await solver.InvokeReportAsync(
                    model: model,
                    solutionAmount: 2,
                    solvingStatusCallBack: callback,
                    cancellationToken: cancellationToken)
                : await solver.InvokeReportAsync(
                    model: model,
                    solvingStatusCallBack: callback,
                    cancellationToken: cancellationToken);
        }
    }

    private static SolveReport<Flt64> Empty(TerminationReason reason) => new() {
        ProblemStatus = ProblemStatus.Unknown,
        TerminationReason = reason,
        SolutionPresence = SolutionPresence.None,
    };

    private static SolveReport<Flt64> Feasible() => new() {
        ProblemStatus = ProblemStatus.Feasible,
        TerminationReason = TerminationReason.TimeLimit,
        SolutionPresence = SolutionPresence.Incumbent,
        Solution = new SolveSolution<Flt64>(
            Values: new Solution<Flt64>([]),
            Objective: Flt64.One,
            Pool: [new Solution<Flt64>([])]),
        Proof = new SolveProof(ProofStatus.Claimed),
    };

    private static LinearTriadModel LinearModel() => new(
        variables: [],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([]),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, []),
        name: "linear");

    private static QuadraticTetradModel QuadraticModel() => new(
        variables: [],
        constraints: new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix([]),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, []),
        name: "quadratic");

    private sealed class ZeroConverter : IIntoValue<Flt64> {
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => Flt64.Zero;
        public Flt64 FromValue(Flt64 value) => value;
    }

    private sealed class Fake(
        string name,
        Func<ulong?, SolvingStatusCallBack?, CancellationToken, Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>> run)
        : IAbstractLinearSolver, IAbstractQuadraticSolver {
        public string Name => name;
        public int Calls { get; private set; }
        public ulong? LastAmount { get; private set; }
        public SolvingStatusCallBack? LastCallback { get; private set; }
        public CancellationToken LastToken { get; private set; }

        private Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> Run(
            ulong? amount, SolvingStatusCallBack? callback, CancellationToken token) {
            Calls++;
            LastAmount = amount;
            LastCallback = callback;
            LastToken = token;
            return run(
                arg1: amount,
                arg2: callback,
                arg3: token);
        }

        public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
            ILinearTriadModelView model, SolvingStatusCallBack? solvingStatusCallBack = null, CancellationToken cancellationToken = default) =>
            Run(
                amount: null,
                callback: solvingStatusCallBack,
                token: cancellationToken);
        public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
            ILinearTriadModelView model, ulong solutionAmount, SolvingStatusCallBack? solvingStatusCallBack = null, CancellationToken cancellationToken = default) =>
            Run(
                amount: solutionAmount,
                callback: solvingStatusCallBack,
                token: cancellationToken);
        public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
            IQuadraticTetradModelView model, SolvingStatusCallBack? solvingStatusCallBack = null, CancellationToken cancellationToken = default) =>
            Run(
                amount: null,
                callback: solvingStatusCallBack,
                token: cancellationToken);
        public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
            IQuadraticTetradModelView model, ulong solutionAmount, SolvingStatusCallBack? solvingStatusCallBack = null, CancellationToken cancellationToken = default) =>
            Run(
                amount: solutionAmount,
                callback: solvingStatusCallBack,
                token: cancellationToken);

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model, SolvingStatusCallBack? callback = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            LinearTriadModel model, SolvingStatusCallBack? callback = null, IisConfig? iisConfig = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model, ulong amount, SolvingStatusCallBack? callback = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model, SolvingStatusCallBack? callback = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model, SolvingStatusCallBack? callback, IisConfig? iisConfig, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model, ulong amount, SolvingStatusCallBack? callback = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMechanismModel<Flt64> model, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMetaModel<Flt64> model, RegistrationStatusCallBack? registration, MechanismModelDumpingStatusCallBack? dumping, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
            QuadraticMechanismModel<Flt64> model, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            QuadraticMetaModel<Flt64> model, RegistrationStatusCallBack? registration, MechanismModelDumpingStatusCallBack? dumping, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
