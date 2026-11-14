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
using ParallelCombinatorialMode = Fuookami.Ospf.Framework.Solver.ParallelCombinatorialMode;
using PayloadResult = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Framework.Tests.Solver.ParallelCombinatorialLegacyTest.Payload, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Framework.Tests.Solver;

public class ParallelCombinatorialLegacyTest {
    public enum Entry { Ordinary, Pool, Iis }

    [Theory]
    [InlineData(false, Entry.Ordinary, ParallelCombinatorialMode.First)]
    [InlineData(false, Entry.Pool, ParallelCombinatorialMode.First)]
    [InlineData(false, Entry.Iis, ParallelCombinatorialMode.First)]
    [InlineData(true, Entry.Ordinary, ParallelCombinatorialMode.First)]
    [InlineData(true, Entry.Pool, ParallelCombinatorialMode.First)]
    [InlineData(true, Entry.Iis, ParallelCombinatorialMode.First)]
    [InlineData(false, Entry.Ordinary, ParallelCombinatorialMode.Best)]
    [InlineData(false, Entry.Pool, ParallelCombinatorialMode.Best)]
    [InlineData(false, Entry.Iis, ParallelCombinatorialMode.Best)]
    [InlineData(true, Entry.Ordinary, ParallelCombinatorialMode.Best)]
    [InlineData(true, Entry.Pool, ParallelCombinatorialMode.Best)]
    [InlineData(true, Entry.Iis, ParallelCombinatorialMode.Best)]
    public async Task WaitsForBlockingBackendsAndSelectsInFactoryOrder(
        bool quadratic,
        Entry entry,
        ParallelCombinatorialMode mode) {
        using var entered = new CountdownEvent(2);
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        var secondFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Payload firstValue = Feasible(4);
        Payload secondValue = Feasible(-2);
        var first = new Fake((_, _, _, _) => {
            entered.Signal();
            if (!releaseFirst.Wait(TimeSpan.FromSeconds(15))) { throw new TimeoutException(); }
            return Task.FromResult(Results.Ok(firstValue));
        });
        var second = new Fake((_, _, _, _) => {
            entered.Signal();
            if (!releaseSecond.Wait(TimeSpan.FromSeconds(15))) { throw new TimeoutException(); }
            secondFinished.SetResult();
            return Task.FromResult(Results.Ok(secondValue));
        });
        using var cancellation = new CancellationTokenSource();
        SolvingStatusCallBack callback = _ => Results.OkInstance;
        var iis = new IisConfig();
        Task<PayloadResult> solve = Task.Run(() => RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: mode,
            factories: [() => first, () => second],
            cancellationToken: cancellation.Token,
            callback: callback,
            iis: iis));
        try {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));
            releaseSecond.Set();
            await secondFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(solve.IsCompleted);
            releaseFirst.Set();
            PayloadResult result = await solve.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.IsOk, result.ToString());
            Payload expected = mode == ParallelCombinatorialMode.First ? firstValue : secondValue;
            Assert.Same(expected.Output, result.Value.Output);
            if (entry == Entry.Pool) { Assert.Same(expected.Pool, result.Value.Pool); }
            Assert.Same(first.LastModel, second.LastModel);
            foreach (Fake fake in new[] { first, second }) {
                Assert.Equal(entry == Entry.Pool ? 2UL : null, fake.LastAmount);
                Assert.Same(entry == Entry.Iis ? iis : null, fake.LastIis);
                Assert.Same(callback, fake.LastCallback);
                Assert.Equal(cancellation.Token, fake.LastToken);
                Assert.Equal(1, fake.Calls);
            }
        }
        finally {
            releaseFirst.Set();
            releaseSecond.Set();
            await solve.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Theory]
    [InlineData(false, Entry.Ordinary, ObjectCategory.Minimum)]
    [InlineData(false, Entry.Pool, ObjectCategory.Maximum)]
    [InlineData(false, Entry.Iis, ObjectCategory.Maximum)]
    [InlineData(true, Entry.Ordinary, ObjectCategory.Maximum)]
    [InlineData(true, Entry.Pool, ObjectCategory.Minimum)]
    [InlineData(true, Entry.Iis, ObjectCategory.Minimum)]
    public async Task BestRespectsDirectionAndKeepsSelectedPool(
        bool quadratic,
        Entry entry,
        ObjectCategory category) {
        Payload first = Feasible(5);
        Payload second = Feasible(-2);
        Payload tie = Feasible(5);
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => Successful(first), () => Successful(second), () => Successful(tie)],
            category: category);
        Payload expected = category == ObjectCategory.Minimum ? second : first;
        Assert.True(result.IsOk, result.ToString());
        Assert.Same(expected.Output, result.Value.Output);
        if (entry == Entry.Pool) { Assert.Same(expected.Pool, result.Value.Pool); }
    }

    [Theory]
    [InlineData(false, ObjectCategory.Minimum, false, true)]
    [InlineData(false, ObjectCategory.Minimum, true, false)]
    [InlineData(false, ObjectCategory.Maximum, false, false)]
    [InlineData(false, ObjectCategory.Maximum, true, true)]
    [InlineData(true, ObjectCategory.Minimum, false, true)]
    [InlineData(true, ObjectCategory.Minimum, true, false)]
    [InlineData(true, ObjectCategory.Maximum, false, false)]
    [InlineData(true, ObjectCategory.Maximum, true, true)]
    public async Task BestOrdersSignedZero(
        bool quadratic,
        ObjectCategory category,
        bool firstNegative,
        bool selectSecond) {
        double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        Payload first = Feasible(firstNegative ? negativeZero : 0.0);
        Payload second = Feasible(firstNegative ? 0.0 : negativeZero);
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: Entry.Ordinary,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => Successful(first), () => Successful(second)],
            category: category);
        Assert.True(result.IsOk);
        Assert.Same(selectSecond ? second.Output : first.Output, result.Value.Output);
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    public async Task BestFallsBackToFirstForNonfiniteObjective(bool quadratic, Entry entry) {
        Payload first = Feasible(4);
        Payload invalid = Feasible(double.NaN);
        Payload better = Feasible(-2);
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => Successful(first), () => Successful(invalid), () => Successful(better)]);
        Assert.True(result.IsOk);
        Assert.Same(first.Output, result.Value.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IisPrefersIncumbentButPreservesNonfeasibleOutputWhenNoneExists(bool quadratic) {
        SolverOutput infeasible = quadratic
            ? new QuadraticInfeasibleSolverOutput(SolverStatus.Infeasible)
            : new LinearInfeasibleSolverOutput(SolverStatus.Infeasible);
        Payload terminal = new(infeasible, []);
        Payload feasible = Feasible(2);
        PayloadResult selected = await RunAsync(
            quadratic: quadratic,
            entry: Entry.Iis,
            mode: ParallelCombinatorialMode.First,
            factories: [() => Successful(terminal), () => Successful(feasible)]);
        Assert.True(selected.IsOk);
        Assert.Same(feasible.Output, selected.Value.Output);
        PayloadResult fallback = await RunAsync(
            quadratic: quadratic,
            entry: Entry.Iis,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => Successful(terminal), () => Failing(ErrorCode.Other)]);
        Assert.True(fallback.IsOk);
        Assert.Same(infeasible, fallback.Value.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IisReportWithoutObjectiveFallsBackToFirstIncumbent(bool quadratic) {
        var report = new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.TimeLimit,
            SolutionPresence = SolutionPresence.Incumbent,
            Solution = new SolveSolution<Flt64>(Values: new Solution<Flt64>([]), Objective: null),
        };
        Payload first = Feasible(4);
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: Entry.Iis,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => Successful(first), () => Successful(new Payload(report, [])), () => Successful(Feasible(-2))]);
        Assert.True(result.IsOk);
        Assert.Same(first.Output, result.Value.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IisPreservesUnknownDiagnosticOutputByReference(bool quadratic) {
        var diagnostic = new CustomDiagnosticOutput("iis evidence");
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: Entry.Iis,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => Successful(new Payload(diagnostic, [])), () => Failing(ErrorCode.Other)]);
        Assert.True(result.IsOk);
        Assert.Same(diagnostic, result.Value.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IisSelectsWrappedIncumbentsWithoutLosingTheWrapper(bool quadratic) {
        Payload first = Feasible(4);
        SolverOutput wrapped = Feasible(-2).Output.WithIIS("evidence").WithoutIIS();
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: Entry.Iis,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => Successful(first), () => Successful(new Payload(wrapped, []))]);
        Assert.True(result.IsOk, result.ToString());
        Assert.Same(wrapped, result.Value.Output);
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    public async Task PreCancellationValidationAndEmptyFactoriesDoNotInvokeBackend(bool quadratic, Entry entry) {
        int initialized = 0;
        Func<Fake> factory = () => { Interlocked.Increment(ref initialized); return Successful(Feasible(1)); };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        PayloadResult cancelled = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.Best,
            factories: [factory],
            cancellationToken: cancellation.Token);
        Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(cancelled).Code);
        PayloadResult invalid = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.First,
            factories: [factory],
            invalidModel: true);
        Assert.True(invalid.IsFailed);
        PayloadResult invalidMode = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: (ParallelCombinatorialMode)99,
            factories: [factory]);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(invalidMode).Code);
        PayloadResult empty = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.Best,
            factories: []);
        Assert.Equal(ErrorCode.SolverNotFound, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(empty).Code);
        Assert.Equal(0, initialized);
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    public async Task PreservesFailuresAndFatalAndIgnoresBrokenLogger(bool quadratic, Entry entry) {
        var fatal = new Fatal<Payload, ErrorCode, Error<ErrorCode>>(
            new Err<ErrorCode>(ErrorCode.AuthenticationError, "license"),
            new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, "numeric"));
        var throwing = new Fake((_, _, _, _) => throw new InvalidOperationException("secret"));
        var fatalSolver = new Fake((_, _, _, _) => Task.FromResult<PayloadResult>(fatal));
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.First,
            factories: [() => throwing, () => fatalSolver, () => Failing(ErrorCode.IllegalArgument)],
            logger: new ThrowingLogger());
        Assert.Equal(new[] { ErrorCode.AuthenticationError, ErrorCode.ORSolutionInvalid },
            Assert.IsType<Fatal<Payload, ErrorCode, Error<ErrorCode>>>(result).Errors.Select(error => error.Code));
        PayloadResult failed = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => Failing(ErrorCode.AuthenticationError), () => Failing(ErrorCode.IllegalArgument)]);
        Assert.Equal(ErrorCode.AuthenticationError, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(failed).Code);
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    public async Task CancellationWaitsForAllStartedAttemptsAndDoesNotDiscardSuccess(bool quadratic, Entry entry) {
        using var cancellation = new CancellationTokenSource();
        using var entered = new CountdownEvent(2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Payload expected = Feasible(2);
        var successful = new Fake(async (_, _, _, _) => {
            entered.Signal();
            await release.Task;
            return Results.Ok(expected);
        });
        var cancelled = new Fake(async (_, _, _, _) => {
            entered.Signal();
            await release.Task;
            throw new OperationCanceledException();
        });
        Task<PayloadResult> solve = RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.First,
            factories: [() => successful, () => cancelled],
            cancellationToken: cancellation.Token);
        try {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));
            cancellation.Cancel();
            Assert.False(solve.IsCompleted);
            release.SetResult();
            PayloadResult result = await solve.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.IsOk, result.ToString());
            Assert.Same(expected.Output, result.Value.Output);
            Assert.Equal(cancellation.Token, cancelled.LastToken);
        }
        finally {
            release.TrySetResult();
            await solve.WaitAsync(TimeSpan.FromSeconds(15));
        }

        PayloadResult allCancelled = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.First,
            factories: [() => new Fake((_, _, _, _) => throw new OperationCanceledException())],
            cancellationToken: cancellation.Token);
        Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(allCancelled).Code);
        PayloadResult unrelated = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.First,
            factories: [() => new Fake((_, _, _, _) => throw new OperationCanceledException("secret"))]);
        Failed<Payload, ErrorCode, Error<ErrorCode>> failure = Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(unrelated);
        Assert.Equal(ErrorCode.OREngineSolvingException, failure.Code);
        Assert.DoesNotContain("secret", failure.Error.Message);
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    public async Task FactoryFailuresAndInvalidOutputsDoNotMaskAValidBackend(bool quadratic, Entry entry) {
        Payload expected = Feasible(3);
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.First,
            factories: [() => throw new InvalidOperationException("factory"), () => Successful(new Payload(null!, [])), () => Successful(expected)]);
        Assert.True(result.IsOk, result.ToString());
        Assert.Same(expected.Output, result.Value.Output);
        PayloadResult invalid = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.First,
            factories: [() => Successful(new Payload(null!, []))]);
        Assert.Equal(ErrorCode.ORSolutionInvalid, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(invalid).Code);
        PayloadResult unknownResult = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            mode: ParallelCombinatorialMode.First,
            factories: [() => new Fake((_, _, _, _) => Task.FromResult<PayloadResult>(new InvalidResult<Payload>()))]);
        Assert.Equal(ErrorCode.OREngineSolvingException,
            Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(unknownResult).Code);
        if (entry == Entry.Pool) {
            PayloadResult invalidPool = await RunAsync(
                quadratic: quadratic,
                entry: entry,
                mode: ParallelCombinatorialMode.First,
                factories: [() => Successful(new Payload(expected.Output, null!))]);
            Assert.Equal(ErrorCode.ORSolutionInvalid, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(invalidPool).Code);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BestRejectsInvalidObjectiveDirectionBeforeStartingFactories(bool quadratic) {
        int initialized = 0;
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: Entry.Ordinary,
            mode: ParallelCombinatorialMode.Best,
            factories: [() => { Interlocked.Increment(ref initialized); return Successful(Feasible(1)); }],
            category: (ObjectCategory)99);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(result).Code);
        Assert.Equal(0, initialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationExceptionFromStartedBackendReturnsStopped(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new Fake(async (_, _, _, _) => {
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException();
        });
        Task<PayloadResult> solve = RunAsync(
            quadratic: quadratic,
            entry: Entry.Ordinary,
            mode: ParallelCombinatorialMode.First,
            factories: [() => fake],
            cancellationToken: cancellation.Token);
        try {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            release.SetResult();
            PayloadResult result = await solve.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        finally {
            release.TrySetResult();
            await solve.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Fact]
    public async Task QuadraticConcreteIisOverloadUsesTheSameSelection() {
        using QuadraticTetradModel model = QuadraticModel(ObjectCategory.Minimum);
        Payload first = Feasible(3);
        Payload second = Feasible(-1);
        var solver = new Fuookami.Ospf.Framework.Solver.ParallelCombinatorialQuadraticSolver(
            solvers: new IAbstractQuadraticSolver[] { Successful(first), Successful(second) });
        Result<SolverOutput, ErrorCode, Error<ErrorCode>> result = await solver.InvokeAsync(
            model: model,
            solvingStatusCallBack: null,
            iisConfig: new IisConfig());
        Assert.True(result.IsOk);
        Assert.Same(second.Output, result.Value);
    }

    private static async Task<PayloadResult> RunAsync(
        bool quadratic,
        Entry entry,
        ParallelCombinatorialMode mode,
        IReadOnlyList<Func<Fake>> factories,
        CancellationToken cancellationToken = default,
        SolvingStatusCallBack? callback = null,
        IisConfig? iis = null,
        ObjectCategory category = ObjectCategory.Minimum,
        bool invalidModel = false,
        ILogger? logger = null) {
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel(category);
            if (invalidModel) { model.Dispose(); }
            IReadOnlyList<Func<IAbstractQuadraticSolver>> typed = factories.Select(factory => (Func<IAbstractQuadraticSolver>)(() => factory())).ToArray();
            IAbstractQuadraticSolver solver = new Fuookami.Ospf.Framework.Solver.ParallelCombinatorialQuadraticSolver(
                solverFactories: typed,
                mode: mode,
                logger: logger);
            return entry switch {
                Entry.Ordinary => (await solver.InvokeAsync(
                    model: model,
                    solvingStatusCallBack: callback,
                    cancellationToken: cancellationToken)).Map(output => new Payload(output, [])),
                Entry.Pool => (await solver.InvokeAsync(
                    model: model,
                    solutionAmount: 2,
                    solvingStatusCallBack: callback,
                    cancellationToken: cancellationToken)).Map(value => new Payload(value.Output, value.Solutions)),
                _ => (await solver.InvokeAsync(
                    model: model,
                    solvingStatusCallBack: callback,
                    iisConfig: iis,
                    cancellationToken: cancellationToken)).Map(output => new Payload(output, [])),
            };
        }
        using LinearTriadModel linear = LinearModel(category);
        if (invalidModel) { linear.Dispose(); }
        IReadOnlyList<Func<IAbstractLinearSolver>> linearFactories = factories.Select(factory => (Func<IAbstractLinearSolver>)(() => factory())).ToArray();
        IAbstractLinearSolver linearSolver = new Fuookami.Ospf.Framework.Solver.ParallelCombinatorialLinearSolver(
            solverFactories: linearFactories,
            mode: mode,
            logger: logger);
        return entry switch {
            Entry.Ordinary => (await linearSolver.InvokeAsync(
                model: (ILinearTriadModelView)linear,
                solvingStatusCallBack: callback,
                cancellationToken: cancellationToken)).Map(output => new Payload(output, [])),
            Entry.Pool => (await linearSolver.InvokeAsync(
                model: linear,
                solutionAmount: 2,
                solvingStatusCallBack: callback,
                cancellationToken: cancellationToken)).Map(value => new Payload(value.Output, value.Solutions)),
            _ => (await linearSolver.InvokeAsync(
                model: linear,
                solvingStatusCallBack: callback,
                iisConfig: iis,
                cancellationToken: cancellationToken)).Map(output => new Payload(output, [])),
        };
    }

    internal static Fake Successful(Payload value) => new((_, _, _, _) => Task.FromResult(Results.Ok(value)));
    internal static Fake Failing(ErrorCode code) => new((_, _, _, _) => Task.FromResult(Results.Failed<Payload>(new Err<ErrorCode>(code))));
    internal static Payload Feasible(double value) => new(
        new FeasibleSolverOutput<Flt64>(
            Obj: new Flt64(value),
            Solution: new Solution<Flt64>([]),
            Time: TimeSpan.Zero,
            PossibleBestObj: new Flt64(value),
            Gap: Flt64.Zero),
        [[new Flt64(value)]]);
    internal static LinearTriadModel LinearModel(ObjectCategory category) => new(
        variables: [],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([]),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<LinearObjectiveCell>(category, []),
        name: "linear");
    internal static QuadraticTetradModel QuadraticModel(ObjectCategory category) => new(
        variables: [],
        constraints: new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix([]),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<QuadraticObjectiveCell>(category, []),
        name: "quadratic");

    public sealed record Payload(SolverOutput Output, List<List<Flt64>> Pool);

    internal sealed record CustomDiagnosticOutput(string Evidence) : SolverOutput;

    internal sealed record InvalidResult<T> : Result<T, ErrorCode, Error<ErrorCode>> {
        public override Result<U, ErrorCode, Error<ErrorCode>> Map<U>(Func<T, U> transform) => new InvalidResult<U>();
        public override Result<U, ErrorCode, Error<ErrorCode>> Bind<U>(Func<T, Result<U, ErrorCode, Error<ErrorCode>>> transform) => new InvalidResult<U>();
    }

    internal sealed class Fake(Func<object, ulong?, IisConfig?, CancellationToken, Task<PayloadResult>> run) : IAbstractLinearSolver, IAbstractQuadraticSolver {
        public string Name => "legacy-only";
        public int Calls { get; private set; }
        public object? LastModel { get; private set; }
        public ulong? LastAmount { get; private set; }
        public IisConfig? LastIis { get; private set; }
        public SolvingStatusCallBack? LastCallback { get; private set; }
        public CancellationToken LastToken { get; private set; }

        private Task<PayloadResult> Run(
            object model,
            ulong? amount,
            IisConfig? iis,
            SolvingStatusCallBack? callback,
            CancellationToken token) {
            Calls++;
            LastModel = model;
            LastAmount = amount;
            LastIis = iis;
            LastCallback = callback;
            LastToken = token;
            return run(
                arg1: model,
                arg2: amount,
                arg3: iis,
                arg4: token);
        }
        public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? callback = null,
            CancellationToken cancellationToken = default) =>
            (await Run(
                model: model,
                amount: null,
                iis: null,
                callback: callback,
                token: cancellationToken)).Map(value => (FeasibleSolverOutput<Flt64>)value.Output);

        public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            LinearTriadModel model,
            SolvingStatusCallBack? callback = null,
            IisConfig? iisConfig = null,
            CancellationToken cancellationToken = default) =>
            (await Run(
                model: model,
                amount: null,
                iis: iisConfig,
                callback: callback,
                token: cancellationToken)).Map(value => value.Output);

        public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            ulong amount,
            SolvingStatusCallBack? callback = null,
            CancellationToken cancellationToken = default) =>
            (await Run(
                model: model,
                amount: amount,
                iis: null,
                callback: callback,
                token: cancellationToken)).Map(value => ((FeasibleSolverOutput<Flt64>)value.Output, value.Pool));

        public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model,
            SolvingStatusCallBack? callback = null,
            CancellationToken cancellationToken = default) =>
            (await Run(
                model: model,
                amount: null,
                iis: null,
                callback: callback,
                token: cancellationToken)).Map(value => (FeasibleSolverOutput<Flt64>)value.Output);

        public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model,
            SolvingStatusCallBack? callback,
            IisConfig? iisConfig,
            CancellationToken cancellationToken = default) =>
            (await Run(
                model: model,
                amount: null,
                iis: iisConfig,
                callback: callback,
                token: cancellationToken)).Map(value => value.Output);

        public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model,
            ulong amount,
            SolvingStatusCallBack? callback = null,
            CancellationToken cancellationToken = default) =>
            (await Run(
                model: model,
                amount: amount,
                iis: null,
                callback: callback,
                token: cancellationToken)).Map(value => ((FeasibleSolverOutput<Flt64>)value.Output, value.Pool));

        public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(LinearMechanismModel<Flt64> model, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMetaModel<Flt64> model,
            RegistrationStatusCallBack? registration,
            MechanismModelDumpingStatusCallBack? dumping,
            CancellationToken cancellationToken = default) => null!;
        public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(QuadraticMechanismModel<Flt64> model, CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>(null!);
        public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            QuadraticMetaModel<Flt64> model,
            RegistrationStatusCallBack? registration,
            MechanismModelDumpingStatusCallBack? dumping,
            CancellationToken cancellationToken = default) =>
            Task.FromException<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>(new InvalidOperationException());
    }

    internal sealed class ThrowingLogger : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("logger");
    }
}
