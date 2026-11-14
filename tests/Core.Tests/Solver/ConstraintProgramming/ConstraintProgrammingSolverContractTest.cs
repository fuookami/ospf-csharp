#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Solver.ConstraintProgramming;

public class ConstraintProgrammingSolverContractTest {
    [Fact]
    public void ExactSolutionShouldLookupValuesIntervalsAndDetectPrecisionLimit() {
        var variable = new VariableId("amount");
        var interval = new IntervalId("window");
        var solution = new ConstraintProgrammingSolution(
            values: new Dictionary<VariableId, Int64> {
                [variable] = new Int64(9),
            },
            intervals: new Dictionary<IntervalId, IntervalValue> {
                [interval] = new IntervalValue(
                    Start: new Int64(1),
                    Size: new Int64(2),
                    End: new Int64(3)),
            });

        solution.Value(variable).Value.Should().Be(new Int64(9));
        solution.Value(variable).Value.Should().Be(new Int64(9));
        solution.Interval(interval).Value.Present.Should().BeTrue();
        solution.AsList(new[] { variable, new VariableId("missing") }).Values
            .Should().Equal(new Int64(9));
        ConstraintProgrammingSolution.ToCompatibilityFlt64(
            new Int64(9_007_199_254_740_991L)).Should().NotBeNull();
        ConstraintProgrammingSolution.ToCompatibilityFlt64(
            new Int64(9_007_199_254_740_992L)).Should().BeNull();
    }

    [Fact]
    public void OptionsShouldUseDeterministicBackendFingerprint() {
        var configuration = new BackendConfigurationSnapshot(
            Type: "test",
            Parameters: new[] {
                new BackendParameter("threads", new BackendParameterValue.Integer(4)),
            });
        var options = new ConstraintProgrammingSolveOptions(
            TimeLimit: TimeSpan.FromSeconds(2),
            NodeLimit: 10,
            SolutionLimit: 3,
            ThreadCount: 4,
            RandomSeed: 17,
            CollectConflict: true,
            ShrinkConflict: true,
            ConflictShrinkLimit: 5,
            Deterministic: true,
            LogEnabled: false,
            BackendConfiguration: configuration);

        options.EffectiveConfigurationFingerprint.Should().NotBeNull();
        options.EffectiveConfigurationFingerprint!.Value.Value
            .Should().Be(configuration.Fingerprint());
    }

    [Fact]
    public void OutputsShouldPreserveExactObjectivesProofsAndConflicts() {
        var report = new SolveReport<Int64> {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.Completed,
            SolutionPresence = SolutionPresence.Incumbent,
        };
        var solution = new ConstraintProgrammingSolution(
            values: new Dictionary<VariableId, Int64> {
                [new VariableId("x")] = new Int64(7),
            });
        ConstraintProgrammingFeasibleOutput feasible = new(
            solution: solution,
            status: SolverStatus.Feasible,
            proofStatus: ProofStatus.None,
            report: report,
            exactObjective: new Int64(7));
        var conflict = new ConstraintProgrammingConflict(
            constraintIds: new[] { new ConstraintId("c") },
            variableIds: new[] { new VariableId("x") },
            message: "conflict",
            validity: EvidenceValidity.Verified);
        ConstraintProgrammingInfeasibleOutput infeasible = new(
            conflict: conflict,
            proofStatus: ProofStatus.Verified,
            report: report);
        ConstraintProgrammingUnknownOutput unknown = new(
            terminationReason: TerminationReason.NodeLimit,
            report: report);

        feasible.Solution.Should().BeSameAs(solution);
        feasible.Report.Should().BeSameAs(report);
        feasible.ExactObjective.Should().Be(new Int64(7));
        infeasible.Conflict.Should().BeSameAs(conflict);
        infeasible.ProofStatus.Should().Be(ProofStatus.Verified);
        unknown.TerminationReason.Should().Be(TerminationReason.NodeLimit);
    }
}
