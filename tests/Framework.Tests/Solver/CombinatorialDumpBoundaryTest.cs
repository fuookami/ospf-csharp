#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Framework.Solver;
namespace Fuookami.Ospf.Framework.Tests.Solver;

public class CombinatorialDumpBoundaryTest {
    public enum FactoryOutcome { Empty, Throws, Null, Cancelled, UnrelatedCancellation }

    public static IEnumerable<object[]> Cases() {
        foreach (bool quadratic in new[] { false, true }) {
            foreach (bool serial in new[] { false, true }) {
                foreach (bool meta in new[] { false, true }) {
                    foreach (FactoryOutcome outcome in Enum.GetValues<FactoryOutcome>()) {
                        yield return [quadratic, serial, meta, outcome];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DumpReturnsFailureForUnavailableFirstFactory(
        bool quadratic,
        bool serial,
        bool meta,
        FactoryOutcome outcome) {
        int laterCalls = 0;
        using var cancellation = new CancellationTokenSource();
        if (outcome == FactoryOutcome.Cancelled) { cancellation.Cancel(); }
        ErrorCode expected = outcome switch {
            FactoryOutcome.Empty => ErrorCode.SolverNotFound,
            FactoryOutcome.Cancelled => ErrorCode.ApplicationStopped,
            _ => ErrorCode.OREngineSolvingException,
        };

        ErrorCode actual;
        if (quadratic) {
            Func<IAbstractQuadraticSolver>[] factories = outcome == FactoryOutcome.Empty ? [] : [
                () => Create<IAbstractQuadraticSolver>(outcome),
                () => { laterCalls++; throw new InvalidOperationException(); },
            ];
            IAbstractQuadraticSolver solver = serial
                ? new SerialCombinatorialQuadraticSolver(factories)
                : new ParallelCombinatorialQuadraticSolver(factories);
            actual = meta
                ? Code(await solver.DumpAsync(
                    model: (QuadraticMetaModel<Flt64>)null!,
                    registrationStatusCallBack: null,
                    dumpingStatusCallBack: null,
                    cancellationToken: cancellation.Token))
                : Code(await solver.DumpAsync((QuadraticMechanismModel<Flt64>)null!, cancellation.Token));
        }
        else {
            Func<IAbstractLinearSolver>[] factories = outcome == FactoryOutcome.Empty ? [] : [
                () => Create<IAbstractLinearSolver>(outcome),
                () => { laterCalls++; throw new InvalidOperationException(); },
            ];
            IAbstractLinearSolver solver = serial
                ? new SerialCombinatorialLinearSolver(factories)
                : new ParallelCombinatorialLinearSolver(factories);
            actual = meta
                ? Code(await solver.DumpAsync(
                    model: (LinearMetaModel<Flt64>)null!,
                    registrationStatusCallBack: null,
                    dumpingStatusCallBack: null,
                    cancellationToken: cancellation.Token))
                : Code(await solver.DumpAsync((LinearMechanismModel<Flt64>)null!, cancellation.Token));
        }
        Assert.Equal(expected, actual);
        Assert.Equal(0, laterCalls);
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
    public async Task InvalidBackendDumpAndBrokenLoggerReturnFailure(bool quadratic, bool serial, bool meta) {
        ParallelCombinatorialLegacyTest.Fake fake = ParallelCombinatorialLegacyTest.Successful(ParallelCombinatorialLegacyTest.Feasible(1));
        var logger = new ParallelCombinatorialLegacyTest.ThrowingLogger();
        ErrorCode actual;
        if (quadratic) {
            IAbstractQuadraticSolver solver = serial
                ? new SerialCombinatorialQuadraticSolver(new IAbstractQuadraticSolver[] { fake }, logger)
                : new ParallelCombinatorialQuadraticSolver(new IAbstractQuadraticSolver[] { fake }, logger: logger);
            actual = meta
                ? Code(await solver.DumpAsync(
                    model: (QuadraticMetaModel<Flt64>)null!,
                    registrationStatusCallBack: null,
                    dumpingStatusCallBack: null))
                : Code(await solver.DumpAsync((QuadraticMechanismModel<Flt64>)null!));
        }
        else {
            IAbstractLinearSolver solver = serial
                ? new SerialCombinatorialLinearSolver(new IAbstractLinearSolver[] { fake }, logger)
                : new ParallelCombinatorialLinearSolver(new IAbstractLinearSolver[] { fake }, logger: logger);
            actual = meta
                ? Code(await solver.DumpAsync(
                    model: (LinearMetaModel<Flt64>)null!,
                    registrationStatusCallBack: null,
                    dumpingStatusCallBack: null))
                : Code(await solver.DumpAsync((LinearMechanismModel<Flt64>)null!));
        }
        Assert.Equal(ErrorCode.OREngineSolvingException, actual);
    }

    private static T Create<T>(FactoryOutcome outcome) where T : class => outcome switch {
        FactoryOutcome.Null => null!,
        FactoryOutcome.Cancelled or FactoryOutcome.UnrelatedCancellation => throw new OperationCanceledException(),
        _ => throw new InvalidOperationException("factory failure"),
    };

    private static ErrorCode Code<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.IsType<Failed<T, ErrorCode, Error<ErrorCode>>>(result).Code;
}
