#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Report;

public class SolveReportTest {
    [Fact]
    public void ProblemTerminationAndSolutionPresenceAreOrthogonal() {
        SolveReport<Flt64> report = new() {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.TimeLimit,
            SolutionPresence = SolutionPresence.Incumbent,
            Solution = new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(new[] { new Flt64(1.0) }),
                Objective: new Flt64(1.0)),
            Statistics = new SolveStatistics(
                SolveTime: TimeSpan.FromSeconds(2),
                Iterations: 3,
                Nodes: 5,
                BestBound: new Flt64(0.5),
                Gap: new Flt64(0.5)),
        };

        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.TerminationReason.Should().Be(TerminationReason.TimeLimit);
        report.SolutionPresence.Should().Be(SolutionPresence.Incumbent);
        report.Iterations.Should().Be(3);
        report.NodeCount.Should().Be(5);
        report.SolveTime.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void LegacyFeasibleOutputPreservesStatisticsAndSolutionPool() {
        FeasibleSolverOutput<Flt64> output = new(
            Obj: new Flt64(10.0),
            Solution: new Solution<Flt64>(new[] { new Flt64(1.0), new Flt64(2.0) }),
            Time: TimeSpan.FromSeconds(1),
            PossibleBestObj: new Flt64(8.0),
            Gap: new Flt64(0.25),
            Iterations: 4,
            NodeCount: 6,
            BestBound: new Flt64(8.0),
            MipGap: new Flt64(0.25),
            SolveTime: TimeSpan.FromSeconds(2));
        Solution<Flt64>[] pool = {
            new(new[] { new Flt64(1.0), new Flt64(2.0) }),
            new(new[] { new Flt64(0.0), new Flt64(1.0) }),
        };

        var report = output.ToSolveReport(solutionPool: pool);

        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.TerminationReason.Should().Be(TerminationReason.Completed);
        report.SolutionPresence.Should().Be(SolutionPresence.Incumbent);
        report.Statistics.Gap.Should().Be(new Flt64(0.25));
        report.Solution.Should().NotBeNull();
        report.Solution!.Pool.Should().HaveCount(2);
    }

    [Fact]
    public void LegacyUnsupportedOutputProducesStructuredError() {
        SolveOutputPlaceholder output = new();

        var report = output.ToSolveReport();

        report.TerminationReason.Should().Be(TerminationReason.BackendFailure);
        report.Diagnostics.Errors.Should().ContainSingle(error =>
            error.Code == "legacy-output-unsupported");
    }

    [Fact]
    public void WrappedIisOutputIsNotSilentlyConvertedWithoutItsEvidence() {
        SolverOutput output = new LinearInfeasibleSolverOutput(SolverStatus.Infeasible).WithIIS("evidence");
        var report = output.ToSolveReport();
        report.ProblemStatus.Should().Be(ProblemStatus.Unknown);
        report.TerminationReason.Should().Be(TerminationReason.BackendFailure);
        report.Diagnostics.InfeasibilityEvidence.Should().BeNull();
        report.Diagnostics.Errors.Should().ContainSingle(error => error.Code == "legacy-output-unsupported");
    }

    [Theory]
    [InlineData(SolverStatus.Infeasible, ProblemStatus.Infeasible, TerminationReason.Completed)]
    [InlineData(SolverStatus.Unbounded, ProblemStatus.Unbounded, TerminationReason.Completed)]
    [InlineData(SolverStatus.InfeasibleOrUnbounded, ProblemStatus.InfeasibleOrUnbounded, TerminationReason.Completed)]
    [InlineData(SolverStatus.Feasible, ProblemStatus.Unknown, TerminationReason.BackendFailure)]
    [InlineData(SolverStatus.Optimal, ProblemStatus.Unknown, TerminationReason.BackendFailure)]
    [InlineData(SolverStatus.SolvingException, ProblemStatus.Unknown, TerminationReason.BackendFailure)]
    [InlineData((SolverStatus)255, ProblemStatus.Unknown, TerminationReason.BackendFailure)]
    public void LegacyNonfeasibleOutputsMustNotMisreportStatus(
        SolverStatus status,
        ProblemStatus expectedStatus,
        TerminationReason expectedTermination) {
        SolverOutput[] outputs = {
            new LinearInfeasibleSolverOutput(status),
            new QuadraticInfeasibleSolverOutput(status),
        };
        foreach (SolverOutput output in outputs) {
            var report = output.ToSolveReport();
            report.ProblemStatus.Should().Be(expectedStatus);
            report.TerminationReason.Should().Be(expectedTermination);
            report.SolutionPresence.Should().Be(SolutionPresence.None);
            report.Proof.Status.Should().Be(ProofStatus.None);
            if (status != SolverStatus.Infeasible) {
                report.Diagnostics.InfeasibilityEvidence.Should().BeNull();
            }
            if (expectedStatus == ProblemStatus.Unknown) {
                report.Diagnostics.Errors.Should().ContainSingle(error => error.Code == "legacy-status-unusable");
            }
        }
    }

    [Fact]
    public void FingerprintsAreCanonicalAndCaseInsensitiveToInputOrder() {
        AuditFingerprint first = SolveFingerprinting.Configuration(
            parameters: new System.Collections.Generic.Dictionary<string, string> {
                ["threads"] = "4",
                ["seed"] = "7",
            });
        AuditFingerprint reordered = SolveFingerprinting.Configuration(
            parameters: new System.Collections.Generic.Dictionary<string, string> {
                ["seed"] = "7",
                ["threads"] = "4",
            });
        AuditFingerprint changed = SolveFingerprinting.Configuration(
            parameters: new System.Collections.Generic.Dictionary<string, string> {
                ["seed"] = "8",
                ["threads"] = "4",
            });

        first.Should().Be(reordered);
        first.Should().NotBe(changed);
        first.Algorithm.Should().Be("SHA-256");
        first.Value.Should().HaveLength(64);
    }

    [Fact]
    public void CancellationRetainsFirstFactAndRunsInterruptOnce() {
        int interruptionCount = 0;
        var handle = SolveHandle.Create(_ => {
            ++interruptionCount;
            return Results.OkInstance;
        });

        handle.Cancel(CancellationSource.Remote, "用户取消");
        handle.Cancel(CancellationSource.Timeout, "超时");

        interruptionCount.Should().Be(1);
        handle.Token.IsCancellationRequested.Should().BeTrue();
        handle.Token.Record!.Source.Should().Be(CancellationSource.Remote);
        handle.Token.Record.Reason.Should().Be("用户取消");
    }

    [Fact]
    public void CancellationWireCodesRoundTripKnownSources() {
        foreach (CancellationSource source in Enum.GetValues<CancellationSource>()) {
            CancellationSource parsed = CancellationSourceExtensions.FromWireCode(
                source.ToWireCode());
            if (source == CancellationSource.Other) {
                parsed.Should().Be(CancellationSource.Other);
            }
            else {
                parsed.Should().Be(source);
            }
        }
    }

    [Fact]
    public void MissingConstraintProgrammingCapabilityIsUnsupported() {
        SolverCapabilities capabilities = new(
            ModelTypes: new HashSet<SolverModelType> { SolverModelType.Mip },
            ConstraintProgrammingFeatures: new System.Collections.Generic.Dictionary<
                ConstraintProgrammingFeature,
                ConstraintProgrammingSupportLevel> {
                [ConstraintProgrammingFeature.BooleanLogic] =
                    ConstraintProgrammingSupportLevel.ExactLowering,
            });

        capabilities.SupportsConstraintProgramming(
            ConstraintProgrammingFeature.BooleanLogic).Should().BeTrue();
        capabilities.SupportsConstraintProgramming(
            ConstraintProgrammingFeature.Cumulative).Should().BeFalse();
    }

    private sealed record SolveOutputPlaceholder : SolverOutput;
}
