#nullable enable
#if HEXALY_AVAILABLE

using System;
using System.Collections.Generic;
using System.IO;
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
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Hexaly.Optimizer;

namespace Fuookami.Ospf.Core.Plugin.Hexaly.Tests;

/// <summary>
/// Hexaly 原生入口的 SDK 无关契约 / SDK-independent native Hexaly entry contracts.
/// </summary>
public class HexalyContractTest {
    [Fact]
    public void EntriesDeclareOnlyImplementedCapabilities() {
        var linear = new HexalyLinearSolver();
        var quadratic = new HexalyQuadraticSolver();

        linear.Name.Should().Be("hexaly");
        quadratic.Name.Should().Be("hexaly");
        linear.Descriptor.Should().NotBeNull();
        quadratic.Descriptor.Should().NotBeNull();
        linear.Descriptor!.SolverId.Should().Be("hexaly");
        quadratic.Descriptor!.SolverId.Should().Be("hexaly");
        linear.Descriptor.Capabilities.ModelTypes.Should().Contain([
            SolverModelType.Lp,
            SolverModelType.Mip,
        ]);
        quadratic.Descriptor.Capabilities.ModelTypes.Should().Contain([
            SolverModelType.Qp,
            SolverModelType.Qcp,
            SolverModelType.Mip,
        ]);
        foreach (SolverCapabilities capabilities in new[] {
            linear.Descriptor.Capabilities,
            quadratic.Descriptor.Capabilities,
        }) {
            capabilities.WarmStart.Should().BeTrue();
            capabilities.Callback.Should().BeTrue();
            capabilities.Interrupt.Should().BeTrue();
            capabilities.Dual.Should().BeFalse();
            capabilities.Farkas.Should().BeFalse();
            capabilities.SolutionPool.Should().BeFalse();
            capabilities.NativeIis.Should().BeFalse();
        }
    }

    [Fact]
    public async Task PreCancellationReturnsReportWithoutCreatingEnvironment() {
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(12),
            ThreadCount = 2,
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        IAbstractLinearSolver linear = new HexalyLinearSolver(config);
        IAbstractQuadraticSolver quadratic = new HexalyQuadraticSolver(config);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>[] results = [
            await linear.SolveReportAsync(
                model: null!,
                converter: IIntoValue<Flt64>.Identity,
                cancellationToken: cancellation.Token),
            await quadratic.SolveReportAsync(
                model: null!,
                converter: IIntoValue<Flt64>.Identity,
                cancellationToken: cancellation.Token),
        ];

        foreach (Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result in results) {
            result.IsOk.Should().BeTrue(result.ToString());
            SolveReport<Flt64> report = result.Value;
            report.ProblemStatus.Should().Be(ProblemStatus.Unknown);
            report.TerminationReason.Should().Be(TerminationReason.Cancelled);
            report.SolutionPresence.Should().Be(SolutionPresence.None);
            report.Solution.Should().BeNull();
            report.Provenance!.Descriptor.SolverId.Should().Be("hexaly");
            report.Provenance.EffectiveParameters["time"].Should().Be("12");
            report.Provenance.EffectiveParameters["threadNum"].Should().Be("2");
            report.Fingerprints.Solver!.Value.Should().HaveLength(64);
        }
    }

    [Fact]
    public async Task MultipleSolutionRequestsAreRejectedBeforeEnvironment() {
        using LinearTriadModel linear = LinearModel();
        using QuadraticTetradModel quadratic = QuadraticModel();
        (await new HexalyLinearSolver().InvokeReportAsync(linear, 2))
            .Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.Other);
        (await new HexalyQuadraticSolver().InvokeReportAsync(quadratic, 2))
            .Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.Other);
        using var parent = new LinearMetaModel<Flt64>(name: "hexaly-pool-rejected");
        (await new HexalyColumnGenerationSolver().SolveMILPAsync(
            name: "hexaly-pool-rejected",
            metaModel: parent,
            amount: 2))
            .Should().BeOfType<Failed<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.Other);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task InvalidLinearSnapshotsFailBeforeEnvironment(int change) {
        ModelViewVariable variable = new(
            index: change == 1 ? 1 : 0,
            lowerBound: new Flt64(change == 2 ? double.PositiveInfinity : 0),
            upperBound: new Flt64(change == 3 ? double.NegativeInfinity : 1),
            type: Continuous.Instance,
            name: "x",
            initialResult: new Flt64(double.NaN));
        using LinearTriadModel model = new(
            variables: [variable],
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix([new SparseVector()]),
                signs: [ConstraintRelation.Equal],
                rhs: [Flt64.Zero],
                names: ["row"],
                sources: [ConstraintSource.Origin]),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: []),
            name: "invalid-linear-snapshot");

        (await new HexalyLinearSolver().InvokeReportAsync(model))
            .Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    [Theory]
    [InlineData(HxSolutionStatus.Feasible, ProblemStatus.Feasible, ProofStatus.None)]
    [InlineData(HxSolutionStatus.Optimal, ProblemStatus.Feasible, ProofStatus.Claimed)]
    [InlineData(HxSolutionStatus.Inconsistent, ProblemStatus.Infeasible, ProofStatus.Claimed)]
    [InlineData(HxSolutionStatus.Infeasible, ProblemStatus.Unknown, ProofStatus.None)]
    public void StatusMappingDoesNotOverclaimProof(
        HxSolutionStatus status,
        ProblemStatus problem,
        ProofStatus proof) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            HexalyReportSupport.Create(
                status: status,
                solution: null,
                statistics: new SolveStatistics(),
                terminationReason: TerminationReason.Completed,
                config: new SolverConfig(),
                quadratic: false);

        result.IsOk.Should().BeTrue(result.ToString());
        result.Value.ProblemStatus.Should().Be(problem);
        result.Value.Proof.Status.Should().Be(proof);
    }

    [Fact]
    public void InfeasibleSolutionStatusCarriesAnExplicitWarning() {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            HexalyReportSupport.Create(
                status: HxSolutionStatus.Infeasible,
                solution: null,
                statistics: new SolveStatistics(),
                terminationReason: TerminationReason.Completed,
                config: new SolverConfig(),
                quadratic: false);

        result.IsOk.Should().BeTrue(result.ToString());
        result.Value.Diagnostics.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be("hexaly-solution-infeasible");
    }

    [Fact]
    public async Task ColumnGenerationLogModelWritesOpmAndLpBeforeSolverValidation() {
        string name = Path.Combine(
            Path.GetTempPath(),
            $"ospf-hexaly-log-{Guid.NewGuid():N}");
        string opmPath = $"{name}.opm";
        string lpPath = $"{name}.lp";
        try {
            using var parent = new LinearMetaModel<Flt64>(
                name: "hexaly-log-model",
                objectCategory: ObjectCategory.Minimum);
            var x = new RealVar("x");
            parent.Add(x).IsOk.Should().BeTrue();
            var solver = new HexalyColumnGenerationSolver(
                config: new SolverConfig {
                    TimeLimit = TimeSpan.FromSeconds(-1),
                });

            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
                await solver.SolveMILPAsync(
                    name: name,
                    metaModel: parent,
                    toLogModel: true);

            result.Should().BeOfType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
            File.Exists(opmPath).Should().BeTrue();
            File.Exists(lpPath).Should().BeTrue();
            (await File.ReadAllTextAsync(opmPath)).Should().Contain("Model Name: hexaly-log-model");
            (await File.ReadAllTextAsync(lpPath)).Should().Contain("Minimum");
        }
        finally {
            if (File.Exists(opmPath)) {
                File.Delete(opmPath);
            }
            if (File.Exists(lpPath)) {
                File.Delete(lpPath);
            }
        }
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
        name: "hexaly-contract-linear");

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
        name: "hexaly-contract-quadratic");
}
#endif
