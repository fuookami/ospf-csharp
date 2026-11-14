#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Solver;
namespace Fuookami.Ospf.Framework.Tests.Solver;

public class QuadraticDumpForwardingTest {
    [Fact]
    public async Task UnmigratedSolverCannotFabricateReportFromLegacyOutput() {
        IQuadraticSolver solver = new DumpSolver(Results.Failed<QuadraticTetradModel>(new Err<ErrorCode>(ErrorCode.Other)));
        (await solver.InvokeReportAsync(null!)).Should()
            .BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.Other);
        (await solver.InvokeReportAsync(null!, 2)).Should()
            .BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.Other);
        (await solver.SolveReportAsync(null!, IIntoValue<Flt64>.Identity)).Should()
            .BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.Other);
    }

    [Fact]
    public async Task CombinatorialSolvers_ShouldForwardFatalAndCancellation() {
        using var parent = new QuadraticMetaModel<Flt64>();
        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var error = new Err<ErrorCode>(ErrorCode.IllegalArgument, "failure");
        var fatal = new Fatal<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>(error);
        var fake = new DumpSolver(fatal);
        IAbstractQuadraticSolver[] solvers = {
            new Fuookami.Ospf.Framework.Solver.ParallelCombinatorialQuadraticSolver(new IQuadraticSolver[] { fake }),
            new SerialCombinatorialQuadraticSolver(new IQuadraticSolver[] { fake }),
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (IAbstractQuadraticSolver solver in solvers) {
            Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> result = await solver.DumpAsync(model, cancellation.Token);
            result.Should().BeSameAs(fatal);
            fake.LastCancellation.Should().Be(cancellation.Token);
        }
    }

    [Fact]
    public async Task SharedQuadraticConversion_ShouldHandleCancellation() {
        using var parent = new QuadraticMetaModel<Flt64>();
        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> result = model.ToQuadraticTetradModel();
        result.IsOk.Should().BeTrue();
        using QuadraticTetradModel converted = result.Value;
        converted.Objective.Cells.Should().BeEmpty();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        model.ToQuadraticTetradModel(cancellationToken: cancellation.Token)
            .Should().BeOfType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public async Task CombinatorialSolvers_ShouldForwardMetaDumpArgumentsAndFailure() {
        using var model = new QuadraticMetaModel<Flt64>();
        var fake = new DumpSolver(Results.Failed<QuadraticTetradModel>(new Err<ErrorCode>(ErrorCode.Other)));
        int laterCalls = 0;
        Func<IAbstractQuadraticSolver>[] factories = [() => fake, () => { laterCalls++; throw new InvalidOperationException(); }];
        IAbstractQuadraticSolver[] solvers = {
            new ParallelCombinatorialQuadraticSolver(factories),
            new SerialCombinatorialQuadraticSolver(factories),
        };
        RegistrationStatusCallBack registration = _ => Results.OkInstance;
        MechanismModelDumpingStatusCallBack dumping = _ => Results.OkInstance;
        using var cancellation = new CancellationTokenSource();
        foreach (IAbstractQuadraticSolver solver in solvers) {
            Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await solver.DumpAsync(
                model: model,
                registrationStatusCallBack: registration,
                dumpingStatusCallBack: dumping,
                cancellationToken: cancellation.Token);
            result.Should().BeSameAs(fake.MetaResult);
            fake.LastRegistration.Should().BeSameAs(registration);
            fake.LastDumping.Should().BeSameAs(dumping);
            fake.LastCancellation.Should().Be(cancellation.Token);
        }
        laterCalls.Should().Be(0);
    }

    private sealed class DumpSolver(Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> result) : IQuadraticSolver {
        public string Name => "dump-fake";
        public SolverConfig Config { get; } = new SolverConfig();
        public Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> MetaResult { get; } =
            new Fatal<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.Other));
        public CancellationToken LastCancellation { get; private set; }
        public RegistrationStatusCallBack? LastRegistration { get; private set; }
        public MechanismModelDumpingStatusCallBack? LastDumping { get; private set; }

        public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
            QuadraticMechanismModel<Flt64> model,
            CancellationToken cancellationToken = default) {
            LastCancellation = cancellationToken;
            return Task.FromResult(result);
        }

        public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            QuadraticMetaModel<Flt64> model,
            RegistrationStatusCallBack? registrationStatusCallBack,
            MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
            CancellationToken cancellationToken = default) {
            LastRegistration = registrationStatusCallBack;
            LastDumping = dumpingStatusCallBack;
            LastCancellation = cancellationToken;
            return Task.FromResult(MetaResult);
        }

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model,
            SolvingStatusCallBack? solvingStatusCallBack,
            IisConfig? iisConfig,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IQuadraticTetradModelView model,
            ulong solutionAmount,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
