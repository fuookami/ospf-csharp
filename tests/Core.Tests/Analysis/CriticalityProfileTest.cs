#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

public class CriticalityProfileTest {
    [Fact]
    public void MultiTargetProfileSeparatesLocalPersistentAndStructuralEvidence() {
        var a = new ConstraintId("a");
        var b = new ConstraintId("b");
        var c = new ConstraintId("c");
        CriticalityObservation[] observations = {
            Observation("o1", 1.0, AnalysisStatus.Unreachable, a, b),
            Observation("o2", 2.0, AnalysisStatus.Unreachable, b, c),
            Observation("o3", 3.0, AnalysisStatus.Unknown, a, b, c),
        };

        CriticalityProfile profile = CriticalityProfileBuilder.Build(observations);

        profile.ProvenTargetCount.Should().Be(2);
        profile.Classifications[a].Should().BeEquivalentTo(
            new[] { CriticalityKind.LocalBottleneck });
        profile.Classifications[b].Should().BeEquivalentTo(
            new[] { CriticalityKind.PersistentBottleneck, CriticalityKind.StructuralBottleneck });
        profile.Classifications[c].Should().BeEquivalentTo(
            new[] { CriticalityKind.LocalBottleneck });
        profile.ConstraintsOf(CriticalityKind.StructuralBottleneck).Should().Contain(b);
    }

    [Fact]
    public void UnknownAndUnsupportedObservationsNeverBecomeBottleneckEvidence() {
        var a = new ConstraintId("a");
        var b = new ConstraintId("b");
        CriticalityObservation[] observations = {
            Observation("o1", 1.0, AnalysisStatus.Unreachable, a),
            Observation("o2", 2.0, AnalysisStatus.Unknown, a, b),
            Observation("o3", 3.0, AnalysisStatus.Unsupported, a, b),
            Observation("o4", 4.0, AnalysisStatus.Reachable, b),
        };

        CriticalityProfile profile = CriticalityProfileBuilder.Build(observations);

        profile.Classifications.Keys.Should().BeEquivalentTo(new[] { a });
        profile.Classifications[a].Should().BeEquivalentTo(
            new[] { CriticalityKind.LocalBottleneck });
    }

    [Fact]
    public void ValidationRejectsDuplicateTargetsBlankIdsAndNonfiniteBounds() {
        ObjectiveTarget target = ObjectiveTarget.CreateAtLeast(
            new ObjectiveId("objective"),
            new Flt64(1.0));
        CriticalityProfile duplicate = new(new[] {
            new CriticalityObservation(target, AnalysisStatus.Unreachable),
            new CriticalityObservation(target, AnalysisStatus.Unreachable),
        });
        CriticalityProfile blankConstraint = new(new[] {
            new CriticalityObservation(
                target,
                AnalysisStatus.Unreachable,
                new HashSet<ConstraintId> { new(" ") }),
        });
        CriticalityProfile nonfinite = new(new[] {
            new CriticalityObservation(
                ObjectiveTarget.CreateAtMost(
                    new ObjectiveId("objective"),
                    new Flt64(double.PositiveInfinity)),
                AnalysisStatus.Unreachable),
        });

        duplicate.Validate().IsOk.Should().BeFalse();
        blankConstraint.Validate().IsOk.Should().BeFalse();
        nonfinite.Validate().IsOk.Should().BeFalse();
        CriticalityProfileBuilder.TryBuild(duplicate.Observations).IsOk.Should().BeFalse();
    }

    [Fact]
    public void AnalysisStatusRequiresAProofOnlyForOptimalityDependentConclusions() {
        ProblemStatus.Infeasible.ToAnalysisStatus().Should().Be(AnalysisStatus.Unreachable);
        ProblemStatus.Infeasible.ToAnalysisStatus(proven: false).Should().Be(AnalysisStatus.Unknown);
        ProblemStatus.Infeasible.ToAnalysisStatus(proven: true).Should().Be(AnalysisStatus.Unreachable);
        ProblemStatus.Feasible.ToAnalysisStatus(proven: false).Should().Be(AnalysisStatus.Unknown);
        ProblemStatus.Feasible.ToAnalysisStatus(proven: true).Should().Be(AnalysisStatus.Reachable);
        ProblemStatus.Unbounded.ToAnalysisStatus(proven: true).Should().Be(AnalysisStatus.Unknown);
        AnalysisStatus.Unknown.IsProven().Should().BeFalse();
        AnalysisStatus.Unsupported.IsProven().Should().BeFalse();
    }

    [Fact]
    public void ProfileSnapshotsObservationEvidenceAndClassifications() {
        var a = new ConstraintId("a");
        var evidence = new HashSet<ConstraintId> { a };
        var classifications = new Dictionary<ConstraintId, IReadOnlySet<CriticalityKind>> {
            [a] = new HashSet<CriticalityKind> { CriticalityKind.LocalBottleneck },
        };
        var observation = new CriticalityObservation(
            ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective"), new Flt64(1.0)),
            AnalysisStatus.Unreachable,
            evidence);

        CriticalityProfile profile = new(
            new List<CriticalityObservation> { observation },
            classifications);
        evidence.Add(new ConstraintId("later"));
        classifications.Clear();

        profile.Observations.Single().BlockingConstraintIds.Should().Equal(a);
        profile.Classifications.Should().ContainKey(a);
        profile.Validate().IsOk.Should().BeTrue();
    }

    private static CriticalityObservation Observation(
        string objectiveId,
        double value,
        AnalysisStatus status,
        params ConstraintId[] constraints) =>
        new(
            ObjectiveTarget.CreateAtLeast(
                new ObjectiveId(objectiveId),
                new Flt64(value)),
            status,
            constraints.ToHashSet());
}
