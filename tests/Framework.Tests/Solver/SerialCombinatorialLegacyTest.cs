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
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Framework.Solver;
using static Fuookami.Ospf.Framework.Tests.Solver.ParallelCombinatorialLegacyTest;
using PayloadResult = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Framework.Tests.Solver.ParallelCombinatorialLegacyTest.Payload, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Framework.Tests.Solver;

public class SerialCombinatorialLegacyTest {
    public enum Entry { Ordinary, Pool, Iis, ConcreteIis }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task RetriesOldBackendsAndForwardsArguments(bool quadratic, Entry entry) {
        Payload expected = Feasible(3);
        Fake first = Failing(ErrorCode.Other);
        Fake second = Successful(expected);
        Fake unused = Successful(Feasible(4));
        using var cancellation = new CancellationTokenSource();
        SolvingStatusCallBack callback = _ => Results.OkInstance;
        var iis = new IisConfig();
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => first, () => second, () => unused],
            cancellationToken: cancellation.Token,
            callback: callback,
            iis: iis);
        Assert.True(result.IsOk, result.ToString());
        Assert.Same(expected.Output, result.Value.Output);
        if (entry == Entry.Pool) { Assert.Same(expected.Pool, result.Value.Pool); }
        Assert.Equal(0, unused.Calls);
        Assert.Same(first.LastModel, second.LastModel);
        foreach (Fake fake in new[] { first, second }) {
            Assert.Equal(entry == Entry.Pool ? 2UL : null, fake.LastAmount);
            Assert.Same(entry is Entry.Iis or Entry.ConcreteIis ? iis : null, fake.LastIis);
            Assert.Same(callback, fake.LastCallback);
            Assert.Equal(cancellation.Token, fake.LastToken);
            Assert.Equal(1, fake.Calls);
        }
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task StopCodesAndFatalAreTerminalAndPreserveErrors(bool quadratic, Entry entry) {
        Fake unused = Successful(Feasible(3));
        PayloadResult stopped = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => Failing(ErrorCode.Other), () => Failing(ErrorCode.ORModelInfeasible), () => unused]);
        Assert.Equal(ErrorCode.ORModelInfeasible, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(stopped).Code);
        Assert.Equal(0, unused.Calls);

        var fatal = new Fatal<Payload, ErrorCode, Error<ErrorCode>>(
            new Err<ErrorCode>(ErrorCode.AuthenticationError, "license"),
            new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, "numeric"));
        PayloadResult failed = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => new Fake((_, _, _, _) => Task.FromResult<PayloadResult>(fatal)), () => unused]);
        Assert.Equal(new[] { ErrorCode.AuthenticationError, ErrorCode.ORSolutionInvalid },
            Assert.IsType<Fatal<Payload, ErrorCode, Error<ErrorCode>>>(failed).Errors.Select(error => error.Code));
        Assert.Equal(0, unused.Calls);
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task SnapshotsFactoriesAndStopCodes(bool quadratic, Entry entry) {
        var stopCodes = new HashSet<ErrorCode> { ErrorCode.Other };
        Fake unused = Successful(Feasible(3));
        var factories = new List<Func<Fake>> { () => Failing(ErrorCode.Other), () => unused };
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: factories,
            stopCodes: stopCodes,
            afterConstruction: () => { factories.Clear(); stopCodes.Clear(); });
        Assert.Equal(ErrorCode.Other, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(result).Code);
        Assert.Equal(0, unused.Calls);
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task InvalidModelsEmptyFactoriesAndPreCancellationDoNotStartBackends(bool quadratic, Entry entry) {
        int initialized = 0;
        Func<Fake> factory = () => { initialized++; return Successful(Feasible(1)); };
        PayloadResult invalid = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [factory],
            invalidModel: true);
        Assert.True(invalid.IsFailed);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        PayloadResult cancelled = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [factory],
            cancellationToken: cancellation.Token);
        Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(cancelled).Code);
        PayloadResult empty = await RunAsync(
            quadratic: quadratic,
            entry: entry,
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
    [InlineData(true, Entry.ConcreteIis)]
    public async Task BadFactoriesResultsAndLoggerDoNotMaskLaterSuccess(bool quadratic, Entry entry) {
        Payload expected = Feasible(3);
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [
                () => throw new InvalidOperationException("factory secret"),
                () => new Fake((_, _, _, _) => Task.FromResult<PayloadResult>(new InvalidResult<Payload>())),
                () => Successful(new Payload(null!, [])),
                () => Successful(expected),
            ],
            logger: new ThrowingLogger());
        Assert.True(result.IsOk, result.ToString());
        Assert.Same(expected.Output, result.Value.Output);
        PayloadResult invalid = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => Successful(new Payload(null!, []))]);
        Assert.Equal(ErrorCode.ORSolutionInvalid, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(invalid).Code);
        PayloadResult unknown = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => new Fake((_, _, _, _) => Task.FromResult<PayloadResult>(new InvalidResult<Payload>()))]);
        Assert.Equal(ErrorCode.OREngineSolvingException, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(unknown).Code);
        foreach (PayloadResult malformed in new PayloadResult[] {
            new Failed<Payload, ErrorCode, Error<ErrorCode>>(null!),
            new Fatal<Payload, ErrorCode, Error<ErrorCode>>((IReadOnlyList<Error<ErrorCode>>)null!),
            new Fatal<Payload, ErrorCode, Error<ErrorCode>>(new Error<ErrorCode>[] { null! }),
        }) {
            PayloadResult rejected = await RunAsync(
                quadratic: quadratic,
                entry: entry,
                factories: [() => new Fake((_, _, _, _) => Task.FromResult(malformed))]);
            Assert.Equal(ErrorCode.OREngineSolvingException, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(rejected).Code);
        }
        if (entry == Entry.Pool) {
            PayloadResult invalidPool = await RunAsync(
                quadratic: quadratic,
                entry: entry,
                factories: [() => Successful(new Payload(expected.Output, null!))]);
            Assert.Equal(ErrorCode.ORSolutionInvalid, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(invalidPool).Code);
        }
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task CancellationBetweenAttemptsStopsAndLateSuccessIsRetained(bool quadratic, Entry entry) {
        using var cancellation = new CancellationTokenSource();
        Fake unused = Successful(Feasible(5));
        var first = new Fake((_, _, _, _) => {
            cancellation.Cancel();
            return Task.FromResult(Results.Failed<Payload>(new Err<ErrorCode>(ErrorCode.Other)));
        });
        PayloadResult stopped = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => first, () => unused],
            cancellationToken: cancellation.Token);
        Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(stopped).Code);
        Assert.Equal(0, unused.Calls);

        using var late = new CancellationTokenSource();
        Payload expected = Feasible(2);
        var success = new Fake((_, _, _, _) => { late.Cancel(); return Task.FromResult(Results.Ok(expected)); });
        PayloadResult completed = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => success, () => unused],
            cancellationToken: late.Token);
        Assert.True(completed.IsOk, completed.ToString());
        Assert.Same(expected.Output, completed.Value.Output);
        Assert.Equal(0, unused.Calls);
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task CancellationExceptionsAreDistinguishedAndNoLaterFactoryStarts(bool quadratic, Entry entry) {
        Fake throwing = new((_, _, _, _) => throw new OperationCanceledException("unrelated"));
        Payload expected = Feasible(2);
        PayloadResult recovered = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => throwing, () => Successful(expected)]);
        Assert.True(recovered.IsOk);
        Assert.Same(expected.Output, recovered.Value.Output);
        PayloadResult failed = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => throwing]);
        Failed<Payload, ErrorCode, Error<ErrorCode>> error = Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(failed);
        Assert.Equal(ErrorCode.OREngineSolvingException, error.Code);
        Assert.DoesNotContain("unrelated", error.Error.Message);

        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Fake unused = Successful(expected);
        Fake cancellable = new(async (_, _, _, _) => {
            entered.SetResult();
            await release.Task;
            throw new OperationCanceledException();
        });
        Task<PayloadResult> task = RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => cancellable, () => unused],
            cancellationToken: cancellation.Token);
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            release.SetResult();
            PayloadResult cancelled = await task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(cancelled).Code);
            Assert.Equal(0, unused.Calls);
        }
        finally {
            release.TrySetResult();
            await task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Theory]
    [InlineData(false, Entry.Ordinary)]
    [InlineData(false, Entry.Pool)]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Ordinary)]
    [InlineData(true, Entry.Pool)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task StopFailureAndFatalTakePrecedenceOverConcurrentCancellation(bool quadratic, Entry entry) {
        using var cancellation = new CancellationTokenSource();
        Fake stopped = new((_, _, _, _) => {
            cancellation.Cancel();
            return Task.FromResult(Results.Failed<Payload>(new Err<ErrorCode>(ErrorCode.ORModelUnbounded)));
        });
        PayloadResult failure = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => stopped],
            cancellationToken: cancellation.Token);
        Assert.Equal(ErrorCode.ORModelUnbounded, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(failure).Code);

        using var fatalCancellation = new CancellationTokenSource();
        Fake fatal = new((_, _, _, _) => {
            fatalCancellation.Cancel();
            return Task.FromResult<PayloadResult>(new Fatal<Payload, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.AuthenticationError)));
        });
        PayloadResult fatalResult = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => fatal],
            cancellationToken: fatalCancellation.Token);
        Assert.IsType<Fatal<Payload, ErrorCode, Error<ErrorCode>>>(fatalResult);
    }

    [Theory]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task IisHonorsKnownStopStatusIncludingWrappedOutput(bool quadratic, Entry entry) {
        SolverOutput infeasible = quadratic
            ? new QuadraticInfeasibleSolverOutput(SolverStatus.Infeasible)
            : new LinearInfeasibleSolverOutput(SolverStatus.Infeasible);
        SolverOutputWithIIS<string> evidence = infeasible.WithIIS("evidence");
        SolverOutputWithoutIIS wrapped = evidence.WithoutIIS();
        Assert.Same(evidence, wrapped.Output);
        Assert.Equal("evidence", evidence.Iis);
        Fake unused = Successful(Feasible(3));
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => Successful(new Payload(wrapped, [])), () => unused]);
        Assert.True(result.IsOk, result.ToString());
        Assert.Same(wrapped, result.Value.Output);
        Assert.Equal(0, unused.Calls);
    }

    [Theory]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task IisStopCodesCanBeConfiguredForRawStatusesAndReports(bool quadratic, Entry entry) {
        foreach (SolverStatus status in new[] { SolverStatus.Infeasible, SolverStatus.Unbounded, SolverStatus.InfeasibleOrUnbounded }) {
            SolverOutput output = quadratic
                ? new QuadraticInfeasibleSolverOutput(status)
                : new LinearInfeasibleSolverOutput(status);
            Payload expected = Feasible(3);
            PayloadResult continued = await RunAsync(
                quadratic: quadratic,
                entry: entry,
                factories: [() => Successful(new Payload(output, [])), () => Successful(expected)],
                stopCodes: new HashSet<ErrorCode>());
            Assert.True(continued.IsOk);
            Assert.Same(expected.Output, continued.Value.Output);
            PayloadResult stopped = await RunAsync(
                quadratic: quadratic,
                entry: entry,
                factories: [() => Successful(new Payload(output, [])), () => Successful(expected)],
                stopCodes: new HashSet<ErrorCode> { status.ErrCode()!.Value });
            Assert.True(stopped.IsOk);
            Assert.Same(output, stopped.Value.Output);
        }
        var report = new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.InfeasibleOrUnbounded,
            TerminationReason = TerminationReason.Completed,
            SolutionPresence = SolutionPresence.None,
        };
        PayloadResult reportResult = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => Successful(new Payload(report, [])), () => Successful(Feasible(3))],
            stopCodes: new HashSet<ErrorCode> { ErrorCode.ORModelInfeasibleOrUnbounded });
        Assert.True(reportResult.IsOk);
        Assert.Same(report, reportResult.Value.Output);
    }

    [Theory]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task IisInvalidWrappersAreRejectedWithoutHidingLaterSuccess(bool quadratic, Entry entry) {
        Payload expected = Feasible(3);
        foreach (SolverOutput wrapper in new SolverOutput[] { new CycleWrapper(), new ThrowingWrapper(), new GrowingWrapper(), new SolverOutputWithoutIIS(null!) }) {
            PayloadResult invalid = await RunAsync(
                quadratic: quadratic,
                entry: entry,
                factories: [() => Successful(new Payload(wrapper, []))]);
            Assert.Equal(ErrorCode.ORSolutionInvalid, Assert.IsType<Failed<Payload, ErrorCode, Error<ErrorCode>>>(invalid).Code);
            PayloadResult recovered = await RunAsync(
                quadratic: quadratic,
                entry: entry,
                factories: [() => Successful(new Payload(wrapper, [])), () => Successful(expected)]);
            Assert.True(recovered.IsOk);
            Assert.Same(expected.Output, recovered.Value.Output);
        }
    }

    [Theory]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task IisContinuesNonterminalOutputsAndRetainsFirstDiagnosticAsFallback(bool quadratic, Entry entry) {
        var diagnostic = new CustomDiagnosticOutput("first evidence");
        var nextDiagnostic = new CustomDiagnosticOutput("second evidence");
        Payload expected = Feasible(3);
        PayloadResult recovered = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => Successful(new Payload(diagnostic, [])), () => Successful(expected)]);
        Assert.True(recovered.IsOk);
        Assert.Same(expected.Output, recovered.Value.Output);
        PayloadResult fallback = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => Successful(new Payload(diagnostic, [])), () => Successful(new Payload(nextDiagnostic, [])), () => Failing(ErrorCode.Other)]);
        Assert.True(fallback.IsOk);
        Assert.Same(diagnostic, fallback.Value.Output);
        PayloadResult fatal = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => Successful(new Payload(diagnostic, [])), () => new Fake((_, _, _, _) =>
                Task.FromResult<PayloadResult>(new Fatal<Payload, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.Other))))]);
        Assert.IsType<Fatal<Payload, ErrorCode, Error<ErrorCode>>>(fatal);
    }

    [Theory]
    [InlineData(false, Entry.Iis)]
    [InlineData(true, Entry.Iis)]
    [InlineData(true, Entry.ConcreteIis)]
    public async Task IisCancelledReportWinsEvenWhenItContainsAnIncumbent(bool quadratic, Entry entry) {
        var report = new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.Cancelled,
            SolutionPresence = SolutionPresence.Incumbent,
            Solution = new SolveSolution<Flt64>(Values: new Solution<Flt64>([])),
        };
        Fake unused = Successful(Feasible(3));
        PayloadResult result = await RunAsync(
            quadratic: quadratic,
            entry: entry,
            factories: [() => Successful(new Payload(report, [])), () => unused]);
        Assert.True(result.IsOk);
        Assert.Same(report, result.Value.Output);
        Assert.Equal(0, unused.Calls);
    }

    private sealed record CycleWrapper : SolverOutput, ISolverOutputWrapper {
        public SolverOutput Output => this;
    }

    private sealed record ThrowingWrapper : SolverOutput, ISolverOutputWrapper {
        public SolverOutput Output => throw new InvalidOperationException("wrapper");
    }

    private sealed record GrowingWrapper : SolverOutput, ISolverOutputWrapper {
        public SolverOutput Output => new GrowingWrapper();
    }

    private static async Task<PayloadResult> RunAsync(
        bool quadratic,
        Entry entry,
        IReadOnlyList<Func<Fake>> factories,
        CancellationToken cancellationToken = default,
        IReadOnlySet<ErrorCode>? stopCodes = null,
        SolvingStatusCallBack? callback = null,
        IisConfig? iis = null,
        bool invalidModel = false,
        ILogger? logger = null,
        Action? afterConstruction = null) {
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel(ObjectCategory.Minimum);
            if (invalidModel) { model.Dispose(); }
            IReadOnlyList<Func<IAbstractQuadraticSolver>> typed = factories.Select(factory => (Func<IAbstractQuadraticSolver>)(() => factory())).ToArray();
            var solver = new SerialCombinatorialQuadraticSolver(
                solverFactories: typed,
                logger: logger,
                stopCodes: stopCodes);
            afterConstruction?.Invoke();
            return entry switch {
                Entry.Ordinary => (await solver.InvokeAsync(
                    model: (IQuadraticTetradModelView)model,
                    solvingStatusCallBack: callback,
                    cancellationToken: cancellationToken)).Map(output => new Payload(output, [])),
                Entry.Pool => (await solver.InvokeAsync(
                    model: model,
                    solutionAmount: 2,
                    solvingStatusCallBack: callback,
                    cancellationToken: cancellationToken)).Map(value => new Payload(value.Output, value.Solutions)),
                Entry.ConcreteIis => (await solver.InvokeAsync(
                    model: model,
                    solvingStatusCallBack: callback,
                    iisConfig: iis,
                    cancellationToken: cancellationToken)).Map(output => new Payload(output, [])),
                _ => (await solver.InvokeAsync(
                    model: (IQuadraticTetradModelView)model,
                    solvingStatusCallBack: callback,
                    iisConfig: iis,
                    cancellationToken: cancellationToken)).Map(output => new Payload(output, [])),
            };
        }
        using LinearTriadModel linear = LinearModel(ObjectCategory.Minimum);
        if (invalidModel) { linear.Dispose(); }
        IReadOnlyList<Func<IAbstractLinearSolver>> linearFactories = factories.Select(factory => (Func<IAbstractLinearSolver>)(() => factory())).ToArray();
        var linearSolver = new SerialCombinatorialLinearSolver(
            solverFactories: linearFactories,
            logger: logger,
            stopCodes: stopCodes);
        afterConstruction?.Invoke();
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
}
