#nullable enable
#if !HEXALY_AVAILABLE

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
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Plugin.Hexaly.Tests;

/// <summary>
/// 无 Hexaly SDK 时的契约测试 / Hexaly SDK-free contract tests.
/// </summary>
public class HexalyUnavailableTest {
    [Fact]
    public async Task SolveEntriesReturnEnvironmentLost() {
        using LinearTriadModel linear = LinearModel();
        var linearSolver = new HexalyLinearSolver();
        AssertEnvironmentLost(await linearSolver.InvokeReportAsync(linear));
        AssertEnvironmentLost(await linearSolver.InvokeReportAsync(linear, 3));
        AssertEnvironmentLost(await linearSolver.InvokeAsync((ILinearTriadModelView)linear));
        AssertEnvironmentLost(await linearSolver.InvokeAsync(
            model: linear,
            solvingStatusCallBack: null,
            iisConfig: null));
        AssertEnvironmentLost(await linearSolver.InvokeAsync(
            model: (ILinearTriadModelView)linear,
            solutionAmount: 3));

        using QuadraticTetradModel quadratic = QuadraticModel();
        var quadraticSolver = new HexalyQuadraticSolver();
        AssertEnvironmentLost(await quadraticSolver.InvokeReportAsync(quadratic));
        AssertEnvironmentLost(await quadraticSolver.InvokeReportAsync(quadratic, 3));
        AssertEnvironmentLost(await quadraticSolver.InvokeAsync((IQuadraticTetradModelView)quadratic));
        AssertEnvironmentLost(await quadraticSolver.InvokeAsync(
            model: (IQuadraticTetradModelView)quadratic,
            solvingStatusCallBack: null,
            iisConfig: null));
        AssertEnvironmentLost(await quadraticSolver.InvokeAsync(
            model: (IQuadraticTetradModelView)quadratic,
            solutionAmount: 3));
    }

    [Fact]
    public async Task ColumnGenerationEntriesReturnEnvironmentLost() {
        using var parent = new LinearMetaModel<Flt64>(name: "hexaly-no-sdk-column");
        var solver = new HexalyColumnGenerationSolver();
        AssertEnvironmentLost(await solver.SolveMILPAsync(
            name: "hexaly-no-sdk-column",
            metaModel: parent));
        AssertEnvironmentLost(await solver.SolveMILPAsync(
            metaModel: parent,
            options: new FrameworkSolveOptions()));
        AssertEnvironmentLost(await solver.SolveMILPAsync(
            name: "hexaly-no-sdk-column",
            metaModel: parent,
            amount: 1));
        AssertEnvironmentLost(await solver.SolveMILPWithSolutionPoolAsync(
            metaModel: parent,
            options: new FrameworkSolveOptions()));
        AssertEnvironmentLost(await solver.SolveLPAsync(
            name: "hexaly-no-sdk-column",
            metaModel: parent));
    }

    [Fact]
    public async Task DumpEntriesRemainAvailableWithoutSdk() {
        using var linearParent = new LinearMetaModel<Flt64>(name: "hexaly-no-sdk-linear-dump");
        using LinearMechanismModel<Flt64> linearMechanism =
            (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        IAbstractLinearSolver linear = new HexalyLinearSolver();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> linearDump =
            await linear.DumpAsync(linearMechanism);
        linearDump.IsOk.Should().BeTrue(linearDump.ToString());
        using LinearTriadModel linearModel = linearDump.Value;
        linearModel.Variables.Should().BeEmpty();

        using var quadraticParent = new QuadraticMetaModel<Flt64>(name: "hexaly-no-sdk-quadratic-dump");
        using QuadraticMechanismModel<Flt64> quadraticMechanism =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        IAbstractQuadraticSolver quadratic = new HexalyQuadraticSolver();
        Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> quadraticDump =
            await quadratic.DumpAsync(quadraticMechanism);
        quadraticDump.IsOk.Should().BeTrue(quadraticDump.ToString());
        using QuadraticTetradModel quadraticModel = quadraticDump.Value;
        quadraticModel.Variables.Should().BeEmpty();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(
            ErrorCode.ApplicationStopped,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(
                await linear.DumpAsync(
                    model: linearMechanism,
                    cancellationToken: cancellation.Token)).Code);
        Assert.Equal(
            ErrorCode.ApplicationStopped,
            Assert.IsType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>(
                await quadratic.DumpAsync(
                    model: quadraticMechanism,
                    cancellationToken: cancellation.Token)).Code);
    }

    [Fact]
    public void UnavailableEntriesDoNotClaimCapabilities() {
        new HexalyLinearSolver().Descriptor.Should().BeNull();
        new HexalyQuadraticSolver().Descriptor.Should().BeNull();
    }

    private static LinearTriadModel LinearModel() => new(
        variables: [],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix(),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: []),
        name: "hexaly-no-sdk-linear");

    private static QuadraticTetradModel QuadraticModel() => new(
        variables: [],
        constraints: new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix(),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<QuadraticObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: []),
        name: "hexaly-no-sdk-quadratic");

    private static void AssertEnvironmentLost<T>(
        Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.Equal(
            ErrorCode.OREngineEnvironmentLost,
            Assert.IsType<Failed<T, ErrorCode, Error<ErrorCode>>>(result).Code);
}
#endif
