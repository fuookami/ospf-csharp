#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 五阶段编排 / The five-stage orchestration.
/// </summary>
/// <remarks>
/// **冲突只在目标不可达时才跑。** 目标可达还跑冲突，**报告会带一份"冲突"解释，而目标明明没问题。**
///
/// **Conflict analysis runs only when the target is unreachable.** Otherwise **the report carries a "conflict" explanation for a target that is fine.**
/// </remarks>
public class CriticalConstraintPipelineTests {
    private static ConstraintProgrammingModelSnapshot Snapshot() {
        var model = new ConstraintProgrammingModel("pipeline", ObjectCategory.Maximum, identityNamespace: "pipeline");
        var x = new IntVar("x");
        var xId = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: xId, variable: x, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/x")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(x, domain, xId);
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(4)).Value,
            id: new ConstraintId("c:le"), name: "le").Value.Should().NotBeNull();
        model.Maximize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        return model.Snapshot().Value!;
    }

    private static ConstraintActivityReport Activity(int candidateCount = 0) {
        var activities = new List<ConstraintActivity>();
        for (int index = 0; index < candidateCount; index++) {
            activities.Add(new ConstraintActivity(
                constraintId: new ConstraintId($"c:{index}"),
                group: null,
                status: ActivityStatus.Active,
                evidence: new ActivityEvidence(
                    new DiagnosticSource.Constraint(new ConstraintId($"c:{index}")), ActivityEvidenceKind.Comparison),
                slack: 0.0));
        }
        return new ConstraintActivityReport(
            Activities: activities, GroupSummaries: [], Tolerance: 1e-6, NearlyActiveTolerance: 1e-3);
    }

    private static TargetFeasibilityReport Target(AnalysisStatus status) =>
        new(Target: ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0)),
            Status: status);

    private static Func<CancellationToken, Task<Result<ConstraintActivityReport, ErrorCode, Error<ErrorCode>>>>
        ActivityBackend(ConstraintActivityReport report) =>
        _ => Task.FromResult(Results.Ok(report));

    /// <summary>**目标可达时不跑冲突分析** / A reachable target does not run conflict analysis.</summary>
    [Fact]
    public async Task TheReachableTarget_ShouldNotRunConflictAnalysis() {
        int conflictCalls = 0;
        var backends = new CriticalPipelineBackends {
            Activity = ActivityBackend(Activity()),
            Target = _ => Task.FromResult(Results.Ok(Target(AnalysisStatus.Reachable))),
            Conflict = _ => {
                conflictCalls++;
                return Task.FromResult(Results.Ok(
                    ConflictExplanationAssembler.AssembleReachable(
                        Snapshot(), Target(AnalysisStatus.Reachable).Target,
                        Target(AnalysisStatus.Reachable), new ConflictExtractionTier(), [])));
            },
        };

        Result<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>> result =
            await CriticalConstraintPipeline.AnalyzeAsync(
                Snapshot(), "objective:main", new Flt64(4.0), baselineProvenOptimal: true,
                target: Target(AnalysisStatus.Reachable).Target,
                options: new CriticalConstraintAnalysisOptions(), backends: backends);

        conflictCalls.Should().Be(0, "a reachable target has nothing to explain");
        result.Value!.Conflict.Should().BeNull("no conflict explanation belongs in this report");
    }

    /// <summary>**目标不可达且没冲突后端时要记原因** / An unreachable target without a conflict backend is recorded.</summary>
    /// <remarks>
    /// 静默跳过会让读者以为"目标不可达但没有阻塞原因"，**而真正的原因是环境里没有这个后端。**
    ///
    /// Skipping silently reads as "the target is unreachable but nothing blocks it", **while the real reason is that this build has no such backend.**
    /// </remarks>
    [Fact]
    public async Task TheUnreachableTargetWithoutConflictBackend_ShouldNoteTheReason() {
        var backends = new CriticalPipelineBackends {
            Activity = ActivityBackend(Activity()),
            Target = _ => Task.FromResult(Results.Ok(Target(AnalysisStatus.Unreachable))),
        };

        Result<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>> result =
            await CriticalConstraintPipeline.AnalyzeAsync(
                Snapshot(), "objective:main", new Flt64(4.0), baselineProvenOptimal: true,
                target: Target(AnalysisStatus.Unreachable).Target,
                options: new CriticalConstraintAnalysisOptions(), backends: backends);

        CriticalConstraintAnalysisReport report = result.Value!;
        report.UnavailableReasons.Should().Contain(CriticalPipelineStages.ConflictBackendMissing);
        report.Status.Should().Be(AnalysisStatus.Unsupported,
            "a missing conflict backend is a capability gap, so the report cannot claim to be complete");
    }

    /// <summary>**阶段 1 失败就整批失败，不吞掉错误** / A stage-1 failure fails the batch.</summary>
    /// <remarks>
    /// 吞掉错误继续跑，**报告会带着一份空的活动性结果出去**，而它看起来完全正常。
    ///
    /// Swallowing the error would **let a report go out carrying an empty activity result**, looking entirely normal.
    /// </remarks>
    [Fact]
    public async Task TheActivityFailure_ShouldFailTheBatch() {
        var backends = new CriticalPipelineBackends {
            Activity = _ => Task.FromResult(Results.Failed<ConstraintActivityReport>(
                new Err<ErrorCode>(ErrorCode.Other, "activity backend exploded"))),
        };

        Result<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>> result =
            await CriticalConstraintPipeline.AnalyzeAsync(
                Snapshot(), "objective:main", new Flt64(4.0), baselineProvenOptimal: true,
                target: null, options: new CriticalConstraintAnalysisOptions(), backends: backends);

        result.Should().BeOfType<Failed<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>>>(
            "the stage error is forwarded rather than replaced by an empty result");
    }

    /// <summary>**短名单为空但有候选时要记原因** / An empty shortlist with candidates is recorded.</summary>
    [Fact]
    public async Task TheEmptyShortlist_ShouldNoteTheReason() {
        var backends = new CriticalPipelineBackends {
            Activity = ActivityBackend(Activity(candidateCount: 3)),
            Perturbation = (_, _) => Task.FromResult(Results.Ok(new ConstraintPerturbationReport(
                ConstraintId: new ConstraintId("c:0"), Status: AnalysisStatus.Reachable))),
        };

        Result<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>> result =
            await CriticalConstraintPipeline.AnalyzeAsync(
                Snapshot(), "objective:main", new Flt64(4.0), baselineProvenOptimal: true,
                target: null,
                options: new CriticalConstraintAnalysisOptions(CandidateLimit: 0),
                backends: backends);

        CriticalConstraintAnalysisReport report = result.Value!;
        report.UnavailableReasons.Should().Contain(CriticalPipelineStages.ShortlistEmpty,
            "the limit zeroed the shortlist out while candidates existed");
    }
}
