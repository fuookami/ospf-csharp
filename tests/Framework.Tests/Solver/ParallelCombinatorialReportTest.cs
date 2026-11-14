#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
using ParallelCombinatorialLinearSolver = Fuookami.Ospf.Framework.Solver.ParallelCombinatorialLinearSolver;
using ParallelCombinatorialMode = Fuookami.Ospf.Framework.Solver.ParallelCombinatorialMode;
using ParallelCombinatorialQuadraticSolver = Fuookami.Ospf.Framework.Solver.ParallelCombinatorialQuadraticSolver;
using ReportResult = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Solver.Report.SolveReport<Fuookami.Ospf.Math.Algebra.Number.Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Framework.Tests.Solver;

public class ParallelCombinatorialReportTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FirstRunsBlockingBackendsConcurrentlyAndWaitsInFactoryOrder(bool quadratic, bool pool) {
        using var entered = new CountdownEvent(2);
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        var secondFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Fake("first", (_, _, _, _) => {
            entered.Signal();
            if (!releaseFirst.Wait(TimeSpan.FromSeconds(15))) { throw new TimeoutException(); }
            return Task.FromResult(Results.Ok(Feasible(3)));
        });
        var second = new Fake("second", (_, _, _, _) => {
            entered.Signal();
            if (!releaseSecond.Wait(TimeSpan.FromSeconds(15))) { throw new TimeoutException(); }
            secondFinished.SetResult();
            return Task.FromResult(Results.Ok(Feasible(1)));
        });
        using var cancellation = new CancellationTokenSource();
        SolvingStatusCallBack callback = _ => Results.OkInstance;
        Task<ReportResult> solve = RunAsync(
            quadratic: quadratic,
            pool: pool,
            mode: ParallelCombinatorialMode.First,
            factories: [() => first, () => second],
            cancellationToken: cancellation.Token,
            callback: callback);
        try {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(12))));
            releaseSecond.Set();
            await secondFinished.Task.WaitAsync(TimeSpan.FromSeconds(12));
            Assert.False(solve.IsCompleted);
            releaseFirst.Set();
            ReportResult result = await solve.WaitAsync(TimeSpan.FromSeconds(12));
            Assert.True(result.IsOk, result.ToString());
            Assert.Equal(new SolveAttemptId("parallel-0"), result.Value.SelectedAttemptId);
            Assert.Equal(SolveSelectionReason.FirstFeasible, result.Value.SelectionReason);
            Assert.Equal(new[] { "first", "second" }, result.Value.Attempts.Select(attempt => attempt.BackendId));
            Assert.Equal(new Flt64(3), result.Value.Solution!.Objective);
            Assert.Same(first.LastModel, second.LastModel);
            foreach (Fake fake in new[] { first, second }) {
                Assert.Equal(pool ? 2UL : null, fake.LastAmount);
                Assert.Equal(cancellation.Token, fake.LastToken);
                Assert.Same(callback, fake.LastCallback);
                Assert.Equal(1, fake.Calls);
            }
            Assert.All(result.Value.Attempts, attempt => {
                Assert.Equal(new SolveAttemptId("parallel-combinatorial"), attempt.ParentAttemptId);
                Assert.True(attempt.Elapsed >= TimeSpan.Zero);
            });
            Assert.Throws<NotSupportedException>(() => ((IList<SolveAttemptTrace<Flt64>>)result.Value.Attempts).Add(result.Value.Attempts[0]));
        }
        finally {
            releaseFirst.Set();
            releaseSecond.Set();
            await solve.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Theory]
    [InlineData(false, false, ObjectCategory.Minimum, 1)]
    [InlineData(false, true, ObjectCategory.Maximum, 0)]
    [InlineData(true, false, ObjectCategory.Maximum, 0)]
    [InlineData(true, true, ObjectCategory.Minimum, 1)]
    public async Task BestUsesObjectiveDirectionAndKeepsFirstTie(bool quadratic, bool pool, ObjectCategory category, int selected) {
        Fake[] solvers = new[] {
            Successful("first", 5),
            Successful("second", -2),
            Successful("third", 5),
        };
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: pool,
            mode: ParallelCombinatorialMode.Best,
            factories: solvers.Select(solver => (Func<Fake>)(() => solver)).ToArray(),
            category: category);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(new SolveAttemptId($"parallel-{selected}"), result.Value.SelectedAttemptId);
        Assert.Equal(SolveSelectionReason.BestObjective, result.Value.SelectionReason);
        Assert.Equal(new Flt64(selected == 1 ? -2 : 5), result.Value.Solution!.Objective);
        Assert.Equal(3, result.Value.Attempts.Count);
        Assert.Single(result.Value.Solution.Pool);
        Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
    }

    [Theory]
    [InlineData(false, ObjectCategory.Minimum, false, 1)]
    [InlineData(false, ObjectCategory.Minimum, true, 0)]
    [InlineData(false, ObjectCategory.Maximum, false, 0)]
    [InlineData(false, ObjectCategory.Maximum, true, 1)]
    [InlineData(true, ObjectCategory.Minimum, false, 1)]
    [InlineData(true, ObjectCategory.Minimum, true, 0)]
    [InlineData(true, ObjectCategory.Maximum, false, 0)]
    [InlineData(true, ObjectCategory.Maximum, true, 1)]
    public async Task BestOrdersSignedZeroLikeThePinnedKotlinComparator(
        bool quadratic,
        ObjectCategory category,
        bool firstNegative,
        int selected) {
        double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        Fake first = Successful("first", firstNegative ? negativeZero : 0.0);
        Fake second = Successful("second", firstNegative ? 0.0 : negativeZero);
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => first, () => second],
            category: category);
        Assert.True(result.IsOk);
        Assert.Equal(new SolveAttemptId($"parallel-{selected}"), result.Value.SelectedAttemptId);
        Assert.Equal(SolveSelectionReason.BestObjective, result.Value.SelectionReason);
        Assert.Equal(category == ObjectCategory.Minimum ? long.MinValue : 0,
            BitConverter.DoubleToInt64Bits(result.Value.Solution!.Objective!.Value.ToDouble()));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task BestFallsBackToFirstWhenAnyIncumbentObjectiveIsNotComparable(bool quadratic, int invalid) {
        Flt64? objective = invalid switch {
            0 => null,
            1 => new Flt64(double.NaN),
            2 => new Flt64(double.PositiveInfinity),
            _ => new Flt64(double.NegativeInfinity),
        };
        Fake first = Successful("first", 4);
        var incomparable = new Fake("incomparable", (_, _, _, _) => Task.FromResult(Results.Ok(Feasible(objective))));
        Fake last = Successful("last", -1);
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => first, () => incomparable, () => last]);
        Assert.True(result.IsOk);
        Assert.Equal(new SolveAttemptId("parallel-0"), result.Value.SelectedAttemptId);
        Assert.Equal(SolveSelectionReason.FirstFeasible, result.Value.SelectionReason);
        Assert.Equal(objective?.ToDouble(), result.Value.Attempts[1].Report!.Solution!.Objective?.ToDouble());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyExplicitIncumbentsAreSelectableEvenAfterCancelledTerminal(bool quadratic) {
        var absent = new Fake("absent", (_, _, _, _) => Task.FromResult(Results.Ok(
            Empty(TerminationReason.Cancelled) with { SolutionPresence = SolutionPresence.Incumbent })));
        var none = new Fake("none", (_, _, _, _) => Task.FromResult(Results.Ok(
            Feasible(-10) with { SolutionPresence = SolutionPresence.None })));
        var unknown = new Fake("unknown", (_, _, _, _) => Task.FromResult(Results.Ok(
            Feasible(-20) with { SolutionPresence = (SolutionPresence)99 })));
        Fake selected = Successful("selected", 2);
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => absent, () => none, () => unknown, () => selected]);
        Assert.True(result.IsOk);
        Assert.Equal(new SolveAttemptId("parallel-3"), result.Value.SelectedAttemptId);
        Assert.Equal(SolveSelectionReason.BestObjective, result.Value.SelectionReason);
        Assert.Equal(new Flt64(2), result.Value.Solution!.Objective);
        Assert.Equal(4, result.Value.Attempts.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PreservesAllFailuresAndKnownTerminalWithoutSelectingIt(bool quadratic, bool pool) {
        SolveReport<Flt64> unknown = Empty(TerminationReason.TimeLimit);
        SolveReport<Flt64> infeasible = Empty(TerminationReason.Completed) with {
            ProblemStatus = ProblemStatus.Infeasible,
            Proof = new SolveProof(ProofStatus.Claimed),
        };
        var nonterminal = new Fake("unknown", (_, _, _, _) => Task.FromResult(Results.Ok(unknown)));
        var terminal = new Fake("infeasible", (_, _, _, _) => Task.FromResult(Results.Ok(infeasible)));
        var fatal = new Fake("fatal", (_, _, _, _) => Task.FromResult<ReportResult>(
            new Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.AuthenticationError, "license"),
                new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, "numerical"))));
        var failed = new Fake("failed", (_, _, _, _) => Task.FromResult(Results.Failed<SolveReport<Flt64>>(
            new Err<ErrorCode>(ErrorCode.IllegalArgument, "invalid"))));
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: pool,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => nonterminal, () => terminal, () => fatal, () => failed, () => throw new InvalidOperationException("secret")]);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Infeasible, result.Value.ProblemStatus);
        Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
        Assert.Null(result.Value.SelectedAttemptId);
        Assert.Equal(SolveSelectionReason.NoSuccessfulAttempt, result.Value.SelectionReason);
        Assert.Equal(5, result.Value.Attempts.Count);
        Assert.Same(unknown, result.Value.Attempts[0].Report);
        Assert.Same(infeasible, result.Value.Attempts[1].Report);
        Assert.Equal("uninitialized-4", result.Value.Attempts[4].BackendId);
        Assert.Equal(new[] { SolveIssueCategory.License, SolveIssueCategory.Numerical, SolveIssueCategory.InvalidInput, SolveIssueCategory.Backend },
            result.Value.Diagnostics.Errors.Select(error => error.Category));
        Assert.DoesNotContain("secret", result.Value.Diagnostics.Errors[3].Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDoesNotLoseStartedAttemptsOrAReportedIncumbent(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var incumbent = new Fake("incumbent", (_, _, _, _) => {
            entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(15))) { throw new TimeoutException(); }
            return Task.FromResult(Results.Ok(Feasible(2) with { TerminationReason = TerminationReason.Cancelled }));
        });
        var cancelled = new Fake("cancelled", (_, _, _, _) => {
            entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(15))) { throw new TimeoutException(); }
            throw new OperationCanceledException();
        });
        Task<ReportResult> solve = RunAsync(
            quadratic: quadratic,
            pool: true,
            mode: ParallelCombinatorialMode.First,
            factories: [() => incumbent, () => cancelled],
            cancellationToken: cancellation.Token);
        try {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(12))));
            cancellation.Cancel();
            Assert.False(solve.IsCompleted);
            release.Set();
            ReportResult result = await solve.WaitAsync(TimeSpan.FromSeconds(12));
            Assert.True(result.IsOk);
            Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
            Assert.Equal(new SolveAttemptId("parallel-0"), result.Value.SelectedAttemptId);
            Assert.Equal(2, result.Value.Attempts.Count);
            Assert.All(result.Value.Attempts, attempt => Assert.Equal("cancellation requested", attempt.CancellationReason));
            Assert.Empty(result.Value.Diagnostics.Errors);
        }
        finally {
            release.Set();
            await solve.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrelatedCancellationExceptionIsAFailure(bool quadratic) {
        var throwing = new Fake("exception", (_, _, _, _) => throw new OperationCanceledException());
        Fake successful = Successful("success", 1);
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            mode: ParallelCombinatorialMode.First,
            factories: [() => throwing, () => successful]);
        Assert.True(result.IsOk);
        Assert.Equal(new SolveAttemptId("parallel-1"), result.Value.SelectedAttemptId);
        Assert.Equal(TerminationReason.TimeLimit, result.Value.TerminationReason);
        Assert.Equal("OREngineSolvingException", Assert.Single(result.Value.Diagnostics.Errors).Code);
        Assert.Null(result.Value.Attempts[0].CancellationReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenLoggerDoesNotReplaceBackendResult(bool quadratic) {
        Fake successful = Successful("success", 1);
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            mode: ParallelCombinatorialMode.First,
            factories: [() => throw new InvalidOperationException("backend"), () => successful],
            logger: new ThrowingLogger());
        Assert.True(result.IsOk);
        Assert.Equal(new SolveAttemptId("parallel-1"), result.Value.SelectedAttemptId);
        Assert.Equal("OREngineSolvingException", Assert.Single(result.Value.Diagnostics.Errors).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCancellationDoesNotOverwriteCompletedIncumbent(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Fake("completed", (_, _, _, _) => {
            completed.SetResult();
            return Task.FromResult(Results.Ok(Feasible(1)));
        });
        var second = new Fake("late", async (_, _, _, _) => {
            await completed.Task;
            cancellation.Cancel();
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(ErrorCode.Other, "late failure"));
        });
        ReportResult result = await RunAsync(
            quadratic: quadratic,
            pool: false,
            mode: ParallelCombinatorialMode.First,
            factories: [() => first, () => second],
            cancellationToken: cancellation.Token);
        Assert.True(result.IsOk);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(TerminationReason.TimeLimit, result.Value.TerminationReason);
        Assert.Equal(new SolveAttemptId("parallel-0"), result.Value.SelectedAttemptId);
        Assert.Equal("Other", Assert.Single(result.Value.Diagnostics.Errors).Code);
        Assert.Null(result.Value.Attempts[0].CancellationReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidationPreCancellationAndFactorySnapshot(bool quadratic) {
        int initialized = 0;
        Fake fake = Successful("fake", 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ReportResult cancelled = await RunAsync(
            quadratic: quadratic,
            pool: true,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => { Interlocked.Increment(ref initialized); return fake; }],
            cancellationToken: cancellation.Token);
        Assert.True(cancelled.IsOk);
        Assert.Equal(TerminationReason.Cancelled, cancelled.Value.TerminationReason);
        Assert.Empty(cancelled.Value.Attempts);
        Assert.Equal(0, initialized);
        ReportResult cancelledEmpty = await RunAsync(
            quadratic: quadratic,
            pool: false,
            mode: ParallelCombinatorialMode.First,
            factories: [],
            cancellationToken: cancellation.Token);
        Assert.True(cancelledEmpty.IsOk);
        Assert.Equal(TerminationReason.Cancelled, cancelledEmpty.Value.TerminationReason);
        Assert.Empty(cancelledEmpty.Value.Attempts);

        ReportResult invalid = await RunAsync(
            quadratic: quadratic,
            pool: false,
            mode: ParallelCombinatorialMode.First,
            factories: [() => { Interlocked.Increment(ref initialized); return fake; }],
            invalidModel: true);
        Assert.True(invalid.IsFailed);
        Assert.Equal(0, initialized);

        ReportResult snapshot;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            var factories = new List<Func<IAbstractQuadraticSolver>> { () => { Interlocked.Increment(ref initialized); return fake; } };
            IAbstractQuadraticSolver solver = new ParallelCombinatorialQuadraticSolver(factories);
            factories.Clear();
            Assert.Equal(0, initialized);
            snapshot = await solver.InvokeReportAsync(model);
        }
        else {
            using LinearTriadModel model = LinearModel();
            var factories = new List<Func<IAbstractLinearSolver>> { () => { Interlocked.Increment(ref initialized); return fake; } };
            IAbstractLinearSolver solver = new ParallelCombinatorialLinearSolver(factories);
            factories.Clear();
            Assert.Equal(0, initialized);
            snapshot = await solver.InvokeReportAsync(model);
        }
        Assert.True(snapshot.IsOk);
        Assert.Single(snapshot.Value.Attempts);
        Assert.Equal(1, initialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidModeOrDirectionFailsBeforeCreatingBackends(bool quadratic) {
        int initialized = 0;
        Fake fake = Successful("fake", 1);
        ReportResult invalidMode = await RunAsync(
            quadratic: quadratic,
            pool: false,
            mode: (ParallelCombinatorialMode)123,
            factories: [() => { Interlocked.Increment(ref initialized); return fake; }]);
        ReportResult invalidDirection = await RunAsync(
            quadratic: quadratic,
            pool: true,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => { Interlocked.Increment(ref initialized); return fake; }],
            category: (ObjectCategory)123);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(invalidMode).Code);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(invalidDirection).Code);
        Assert.Equal(0, initialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyAndUnknownReportsUseBackendFailureButCancelledTerminalIsPreserved(bool quadratic) {
        ReportResult empty = await RunAsync(
            quadratic: quadratic,
            pool: false,
            mode: ParallelCombinatorialMode.First,
            factories: []);
        Assert.True(empty.IsOk);
        Assert.Equal(TerminationReason.BackendFailure, empty.Value.TerminationReason);
        Assert.Equal("combinatorial-no-successful-attempt", Assert.Single(empty.Value.Diagnostics.Errors).Code);

        var limited = new Fake("limited", (_, _, _, _) => Task.FromResult(Results.Ok(Empty(TerminationReason.TimeLimit))));
        var cancelled = new Fake("cancelled", (_, _, _, _) => Task.FromResult(Results.Ok(Empty(TerminationReason.Cancelled))));
        ReportResult known = await RunAsync(
            quadratic: quadratic,
            pool: true,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => limited, () => cancelled]);
        Assert.True(known.IsOk);
        Assert.Equal(TerminationReason.Cancelled, known.Value.TerminationReason);
        Assert.Null(known.Value.SelectedAttemptId);
        Assert.Equal(2, known.Value.Attempts.Count);
        Assert.Empty(known.Value.Diagnostics.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectReportAndCheckedGenericConversionRetainAttempts(bool quadratic) {
        SolveReport<Flt64> backend = Feasible(1);
        var fake = new Fake("fake", (_, _, _, _) => Task.FromResult(Results.Ok(backend)));
        Result<CombinatorialSolveReport<Flt64>, ErrorCode, Error<ErrorCode>> direct;
        ReportResult converted;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            var solver = new ParallelCombinatorialQuadraticSolver(new IAbstractQuadraticSolver[] { fake });
            direct = await solver.SolveCombinatorialReportAsync(model, 2);
            converted = await ((IAbstractQuadraticSolver)solver).SolveReportAsync(model, new ZeroConverter());
        }
        else {
            using LinearTriadModel model = LinearModel();
            var solver = new ParallelCombinatorialLinearSolver(new IAbstractLinearSolver[] { fake });
            direct = await solver.SolveCombinatorialReportAsync(model, 2);
            converted = await ((IAbstractLinearSolver)solver).SolveReportAsync(model, new ZeroConverter());
        }
        Assert.True(direct.IsOk);
        Assert.Same(backend, direct.Value.FinalReport);
        Assert.Same(backend, Assert.Single(direct.Value.Attempts).Report);
        Assert.Equal(new SolveAttemptId("parallel-0"), direct.Value.SelectedAttemptId);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(converted).Code);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task CombinatorsCanNestThroughAbstractInterfaces(bool quadratic, bool serialOuter, bool pool) {
        SolveReport<Flt64> backend = Feasible(7);
        var fake = new Fake("leaf", (_, _, _, _) => Task.FromResult(Results.Ok(backend)));
        ReportResult result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            IAbstractQuadraticSolver inner = serialOuter
                ? new ParallelCombinatorialQuadraticSolver(new IAbstractQuadraticSolver[] { fake })
                : new SerialCombinatorialQuadraticSolver(new IAbstractQuadraticSolver[] { fake });
            IAbstractQuadraticSolver outer = serialOuter
                ? new SerialCombinatorialQuadraticSolver(new IAbstractQuadraticSolver[] { inner })
                : new ParallelCombinatorialQuadraticSolver(new IAbstractQuadraticSolver[] { inner });
            result = pool ? await outer.InvokeReportAsync(model, 2) : await outer.InvokeReportAsync(model);
        }
        else {
            using LinearTriadModel model = LinearModel();
            IAbstractLinearSolver inner = serialOuter
                ? new ParallelCombinatorialLinearSolver(new IAbstractLinearSolver[] { fake })
                : new SerialCombinatorialLinearSolver(new IAbstractLinearSolver[] { fake });
            IAbstractLinearSolver outer = serialOuter
                ? new SerialCombinatorialLinearSolver(new IAbstractLinearSolver[] { inner })
                : new ParallelCombinatorialLinearSolver(new IAbstractLinearSolver[] { inner });
            result = pool ? await outer.InvokeReportAsync(model, 2) : await outer.InvokeReportAsync(model);
        }
        Assert.True(result.IsOk, result.ToString());
        SolveReport<Flt64> innerReport = Assert.Single(result.Value.Attempts).Report!;
        Assert.Same(backend, Assert.Single(innerReport.Attempts).Report);
        Assert.Same(backend.Solution, result.Value.Solution);
        Assert.Equal(pool ? 2UL : null, fake.LastAmount);
        Assert.Equal(1, fake.Calls);
    }

    private static async Task<ReportResult> RunAsync(
        bool quadratic,
        bool pool,
        ParallelCombinatorialMode mode,
        IReadOnlyList<Func<Fake>> factories,
        CancellationToken cancellationToken = default,
        SolvingStatusCallBack? callback = null,
        ObjectCategory category = ObjectCategory.Minimum,
        bool invalidModel = false,
        ILogger? logger = null) {
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel(category);
            if (invalidModel) { model.Dispose(); }
            IReadOnlyList<Func<IAbstractQuadraticSolver>> typed = factories.Select(factory => (Func<IAbstractQuadraticSolver>)(() => factory())).ToArray();
            IAbstractQuadraticSolver solver = new ParallelCombinatorialQuadraticSolver(
                solverFactories: typed,
                mode: mode,
                logger: logger);
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
            using LinearTriadModel model = LinearModel(category);
            if (invalidModel) { model.Dispose(); }
            IReadOnlyList<Func<IAbstractLinearSolver>> typed = factories.Select(factory => (Func<IAbstractLinearSolver>)(() => factory())).ToArray();
            IAbstractLinearSolver solver = new ParallelCombinatorialLinearSolver(
                solverFactories: typed,
                mode: mode,
                logger: logger);
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

    private static Fake Successful(string name, double objective) => new(name, (_, _, _, _) => Task.FromResult(Results.Ok(Feasible(objective))));
    private static SolveReport<Flt64> Empty(TerminationReason reason) => new() {
        ProblemStatus = ProblemStatus.Unknown,
        TerminationReason = reason,
        SolutionPresence = SolutionPresence.None,
    };
    private static SolveReport<Flt64> Feasible(double objective) => Feasible(new Flt64(objective));
    private static SolveReport<Flt64> Feasible(Flt64? objective) => new() {
        ProblemStatus = ProblemStatus.Feasible,
        TerminationReason = TerminationReason.TimeLimit,
        SolutionPresence = SolutionPresence.Incumbent,
        Solution = new SolveSolution<Flt64>(
            Values: new Solution<Flt64>([]),
            Objective: objective,
            Pool: [new Solution<Flt64>([])]),
        Proof = new SolveProof(ProofStatus.Claimed),
    };
    private static LinearTriadModel LinearModel(ObjectCategory category = ObjectCategory.Minimum) => new(
        variables: [],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([]),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<LinearObjectiveCell>(category, []),
        name: "linear");
    private static QuadraticTetradModel QuadraticModel(ObjectCategory category = ObjectCategory.Minimum) => new(
        variables: [],
        constraints: new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix([]),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<QuadraticObjectiveCell>(category, []),
        name: "quadratic");

    private sealed class ZeroConverter : IIntoValue<Flt64> {
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => Flt64.Zero;
        public Flt64 FromValue(Flt64 value) => value;
    }

    private sealed class ThrowingLogger : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("logger");
    }

    private sealed class Fake(
        string name,
        Func<object, ulong?, SolvingStatusCallBack?, CancellationToken, Task<ReportResult>> run) : IAbstractLinearSolver, IAbstractQuadraticSolver {
        public string Name => name;
        public int Calls { get; private set; }
        public object? LastModel { get; private set; }
        public ulong? LastAmount { get; private set; }
        public SolvingStatusCallBack? LastCallback { get; private set; }
        public CancellationToken LastToken { get; private set; }
        private Task<ReportResult> Run(
            object model,
            ulong? amount,
            SolvingStatusCallBack? callback,
            CancellationToken token) {
            Calls++;
            LastModel = model;
            LastAmount = amount;
            LastCallback = callback;
            LastToken = token;
            return run(
                arg1: model,
                arg2: amount,
                arg3: callback,
                arg4: token);
        }
        public Task<ReportResult> InvokeReportAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            Run(
                model: model,
                amount: null,
                callback: solvingStatusCallBack,
                token: cancellationToken);
        public Task<ReportResult> InvokeReportAsync(
            ILinearTriadModelView model,
            ulong solutionAmount,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            Run(
                model: model,
                amount: solutionAmount,
                callback: solvingStatusCallBack,
                token: cancellationToken);
        public Task<ReportResult> InvokeReportAsync(
            IQuadraticTetradModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            Run(
                model: model,
                amount: null,
                callback: solvingStatusCallBack,
                token: cancellationToken);
        public Task<ReportResult> InvokeReportAsync(
            IQuadraticTetradModelView model,
            ulong solutionAmount,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            Run(
                model: model,
                amount: solutionAmount,
                callback: solvingStatusCallBack,
                token: cancellationToken);
        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? callback = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            LinearTriadModel model,
            SolvingStatusCallBack? callback = null,
            IisConfig? iisConfig = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            ulong amount,
            SolvingStatusCallBack? callback = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model,
            SolvingStatusCallBack? callback = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model,
            SolvingStatusCallBack? callback,
            IisConfig? iisConfig,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model,
            ulong amount,
            SolvingStatusCallBack? callback = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(LinearMechanismModel<Flt64> model, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMetaModel<Flt64> model,
            RegistrationStatusCallBack? registration,
            MechanismModelDumpingStatusCallBack? dumping,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(QuadraticMechanismModel<Flt64> model, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            QuadraticMetaModel<Flt64> model,
            RegistrationStatusCallBack? registration,
            MechanismModelDumpingStatusCallBack? dumping,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
