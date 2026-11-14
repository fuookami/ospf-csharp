#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Gurobi11;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi11.Tests;

/// <summary>Gurobi11 入口与统一报告的 SDK 无关契约。</summary>
public class Gurobi11ContractTest {
    [Fact]
    public void EntriesKeepStableNamesAndInterfaces() {
        var linear = new Gurobi11LinearSolver();
        var quadratic = new Gurobi11QuadraticSolver();
        var column = new Gurobi11ColumnGenerationSolver();
        var benders = new Gurobi11BendersDecompositionSolver();

        linear.Name.Should().Be("gurobi11");
        quadratic.Name.Should().Be("gurobi11");
        column.Name.Should().Be("gurobi11");
        benders.Name.Should().Be("gurobi11");
        linear.Should().BeAssignableTo<ILinearSolver>();
        quadratic.Should().BeAssignableTo<IQuadraticSolver>();
        column.Should().BeAssignableTo<Framework.Solver.IColumnGenerationSolver>();
        benders.Should().BeAssignableTo<Framework.Solver.IBendersDecompositionSolver>();
    }

    [Fact]
    public void BendersTimeLimitKeepsGurobi11IdentityAndConfiguration() {
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(30),
            ThreadCount = 3,
        };
        var original = new Gurobi11BendersDecompositionSolver(config);

        Gurobi11BendersDecompositionSolver budgeted = original.WithTimeLimit(TimeSpan.FromSeconds(7))
            .Should().BeOfType<Gurobi11BendersDecompositionSolver>().Subject;

        budgeted.Should().NotBeSameAs(original);
        budgeted.Name.Should().Be("gurobi11");
        budgeted.Config.TimeLimit.Should().Be(TimeSpan.FromSeconds(7));
        budgeted.Config.ThreadCount.Should().Be(3);
        original.Config.TimeLimit.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task PreCancellationKeepsGurobi11ProvenanceAndFingerprints() {
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(12),
            ThreadCount = 2,
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        IAbstractLinearSolver linear = new Gurobi11LinearSolver(config);
        IAbstractQuadraticSolver quadratic = new Gurobi11QuadraticSolver(config);
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
            result.IsOk.Should().BeTrue();
            SolveReport<Flt64> report = result.Value;
            report.TerminationReason.Should().Be(TerminationReason.Cancelled);
            report.SolutionPresence.Should().Be(SolutionPresence.None);
            report.Provenance.Should().NotBeNull();
            report.Provenance!.Descriptor.SolverId.Should().Be("gurobi11");
            report.Provenance.Descriptor.BackendName.Should().Be("Gurobi 11");
            report.Provenance.EffectiveParameters["time"].Should().Be("12");
            report.Provenance.EffectiveParameters["threadNum"].Should().Be("2");
            report.Fingerprints.Configuration.Should().NotBeNull();
            report.Fingerprints.Solver.Should().NotBeNull();
            report.Fingerprints.Solver!.Value.Should().HaveLength(64);
        }
        results[0].Value.Provenance!.Descriptor.Capabilities.ModelTypes
            .Should().Contain([
                SolverModelType.Lp,
                SolverModelType.Mip,
            ]);
        results[1].Value.Provenance!.Descriptor.Capabilities.ModelTypes
            .Should().Contain([
                SolverModelType.Qp,
                SolverModelType.Qcp,
                SolverModelType.Mip,
            ]);
    }
}
