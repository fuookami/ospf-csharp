#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Mindopt.Tests;

/// <summary>
/// MindOPT 无许可证契约测试 / License-independent MindOPT contract tests.
/// </summary>
public class MindoptContractTest {
    [Fact]
    [Trait("Solver", "MindOPT")]
    public void LinearCapabilitiesMatchImplementedPaths() {
        SolverCapabilities capabilities = MindoptReportSupport.LinearCapabilities;

        capabilities.ModelTypes.Should().BeEquivalentTo([
            SolverModelType.Lp,
            SolverModelType.Mip,
        ]);
        capabilities.Dual.Should().BeTrue();
        capabilities.Farkas.Should().BeTrue();
        capabilities.WarmStart.Should().BeTrue();
        capabilities.SolutionPool.Should().BeTrue();
        capabilities.Interrupt.Should().BeTrue();
        capabilities.Callback.Should().BeFalse();
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public void QuadraticCapabilitiesDoNotClaimDuals() {
        SolverCapabilities capabilities = MindoptReportSupport.QuadraticCapabilities;

        capabilities.ModelTypes.Should().BeEquivalentTo([
            SolverModelType.Qp,
            SolverModelType.Qcp,
            SolverModelType.Mip,
        ]);
        capabilities.Dual.Should().BeFalse();
        capabilities.Farkas.Should().BeFalse();
        capabilities.WarmStart.Should().BeTrue();
        capabilities.SolutionPool.Should().BeTrue();
        capabilities.Interrupt.Should().BeTrue();
    }

    [Theory]
    [InlineData(MindoptStatus.IterationLimit, TerminationReason.IterationLimit)]
    [InlineData(MindoptStatus.TimeLimit, TerminationReason.TimeLimit)]
    [InlineData(MindoptStatus.NodeLimit, TerminationReason.NodeLimit)]
    [InlineData(MindoptStatus.StallingNodeLimit, TerminationReason.NodeLimit)]
    [InlineData(MindoptStatus.SolutionLimit, TerminationReason.SolutionLimit)]
    [Trait("Solver", "MindOPT")]
    public void NativeLimitsMapWithoutFabricatingProof(int status, TerminationReason expected) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            MindoptReportSupport.Create(
                status: status,
                solution: null,
                statistics: new SolveStatistics(SolveTime: TimeSpan.FromSeconds(1)),
                cancelledDuringSolve: false,
                interruptedDuringSolve: false,
                config: new SolverConfig(),
                quadratic: false);

        result.IsOk.Should().BeTrue();
        SolveReport<Flt64> report = result.Value;
        report.TerminationReason.Should().Be(expected);
        report.ProblemStatus.Should().Be(ProblemStatus.Unknown);
        report.SolutionPresence.Should().Be(SolutionPresence.None);
        report.Proof.Status.Should().Be(ProofStatus.None);
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public void InfeasibleStatusCarriesBackendProof() {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            MindoptReportSupport.Create(
                status: MindoptStatus.Infeasible,
                solution: null,
                statistics: new SolveStatistics(SolveTime: TimeSpan.FromSeconds(1)),
                cancelledDuringSolve: false,
                interruptedDuringSolve: false,
                config: new SolverConfig(),
                quadratic: false);

        result.IsOk.Should().BeTrue();
        result.Value.ProblemStatus.Should().Be(ProblemStatus.Infeasible);
        result.Value.TerminationReason.Should().Be(TerminationReason.Completed);
        result.Value.Proof.Status.Should().Be(ProofStatus.Claimed);
        result.Value.Proof.Kind.Should().Be("backend-status");
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public void CancellationIsDistinctFromBackendFailure() {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            MindoptReportSupport.Create(
                status: MindoptStatus.Optimal,
                solution: null,
                statistics: new SolveStatistics(SolveTime: TimeSpan.FromSeconds(1)),
                cancelledDuringSolve: true,
                interruptedDuringSolve: false,
                config: new SolverConfig(),
                quadratic: false);

        result.IsOk.Should().BeTrue();
        result.Value.TerminationReason.Should().Be(TerminationReason.Cancelled);
        result.Value.Proof.Status.Should().Be(ProofStatus.None);
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public void SnapshotRejectsNaNBeforeCallingTheBackend() {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 0,
                lowerBound: new Flt64(double.NaN),
                upperBound: new Flt64(1),
                type: Continuous.Instance,
                name: "x"));

        Result<MindoptLinearSnapshot, ErrorCode, Error<ErrorCode>> result =
            MindoptLinearSnapshot.Capture(model);

        result.Should().BeOfType<Failed<MindoptLinearSnapshot, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public async Task CancelledDumpDoesNotStartNativeWork() {
        using var parent = new LinearMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> mechanism =
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> result =
            await new MindoptLinearSolver().DumpAsync(
                model: mechanism,
                cancellationToken: cancellation.Token);

        result.Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

#if MINDOPT_AVAILABLE
    [Fact]
    [Trait("Solver", "MindOPT")]
    public void DescriptorUsesNativeVersionAndDeclaredCapabilities() {
        SolverDescriptor linear = Assert.IsType<SolverDescriptor>(
            new MindoptLinearSolver().Descriptor);
        SolverDescriptor quadratic = Assert.IsType<SolverDescriptor>(
            new MindoptQuadraticSolver().Descriptor);

        linear.SolverId.Should().Be("mindopt");
        linear.BackendName.Should().Be("MindOPT");
        linear.BackendVersion.Should().MatchRegex(@"^2\.3(\.|$)");
        linear.Capabilities.Dual.Should().BeTrue();
        quadratic.Capabilities.Dual.Should().BeFalse();
    }
#endif

    private static LinearTriadModel Model(ModelViewVariable variable) => new(
        variables: [variable],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix(),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [],
            constant: Flt64.Zero),
        name: "mindopt-contract");
}
