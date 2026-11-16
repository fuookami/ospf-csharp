#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 活动性报告 / The activity report.
/// </summary>
/// <remarks>
/// 补 `ConstraintActivityReport`（基线 126 行）**，它只依赖 补的三个类型，不需要原生后端。
///
/// 判据钉的是**分类边界**：哪些记录算约束、哪些算变量、组汇总统计谁。**这三条错了，报告仍然"能跑"，只是数字全对不上。**
///
/// **ported `ConstraintActivityReport` (126 baseline lines)**, which depends only on the earlier three types and needs no native backend. The criteria pin the **classification
/// boundaries** — which records are constraints, which are variables, and what the group summaries count. **Get these wrong and the report still runs, with every number off.**
/// </remarks>
public class ConstraintActivityReportTests {
    private static ActivityEvidence Evidence(string id) => new(
        new DiagnosticSource.Constraint(new ConstraintId(id)),
        ActivityEvidenceKind.Comparison,
        Satisfied: true);

    private static ConstraintActivity Constraint(string id, string? group, ActivityStatus status) => new(
        new ConstraintId(id),
        group,
        status,
        Evidence(id));

    private static ConstraintActivity Variable(string id, ActivityStatus status) => new(
        null,
        null,
        status,
        new ActivityEvidence(
            new DiagnosticSource.VariableLowerBound(new VariableId(id)),
            ActivityEvidenceKind.VariableBound,
            Satisfied: true));

    /// <summary>**约束与变量按 `ConstraintId` 是否为空分类** / Constraints and variables split on whether `ConstraintId` is null.</summary>
    [Fact]
    public void TheClassification_ShouldFollowTheConstraintId() {
        var report = new ConstraintActivityReport(
            [
                Constraint("c1", "g1", ActivityStatus.Active),
                Variable("x", ActivityStatus.NearlyActive),
                Constraint("c2", "g1", ActivityStatus.Violated),
                Variable("y", ActivityStatus.Inactive),
            ],
            [],
            Tolerance: 1e-6,
            NearlyActiveTolerance: 1e-3);

        report.ConstraintActivities.Select(activity => activity.ConstraintId!.Value.Value)
            .Should().Equal("c1", "c2");
        report.VariableActivities.Select(activity => activity.Source.StableId)
            .Should().Equal("variable:x:lower", "variable:y:lower");
        report.TotalConstraints.Should().Be(2);
        report.TotalActivities.Should().Be(4,
            "the two totals differ on purpose: a report that only counted constraints would hide the variable evidence");
    }

    /// <summary>**按 ID 查找只命中约束** / A lookup by ID finds constraints only.</summary>
    /// <remarks>
    /// 变量记录的 `ConstraintId` 是 `null`。如果查找实现用"匹配 `null`"，那查任何不存在的 id 都会**返回第一条变量记录**。
    ///
    /// A variable record's `ConstraintId` is `null`. A lookup that "matches null" would **return the first variable record** for any id that does not exist.
    /// </remarks>
    [Fact]
    public void TheLookup_ShouldNotFallThroughToAVariableRecord() {
        var report = new ConstraintActivityReport(
            [
                Variable("x", ActivityStatus.Inactive),
                Constraint("c1", "g1", ActivityStatus.Active),
            ],
            [],
            Tolerance: 1e-6,
            NearlyActiveTolerance: 1e-3);

        report.Constraint(new ConstraintId("c1")).Should().NotBeNull();
        report.Constraint(new ConstraintId("c1"))!.Status.Should().Be(ActivityStatus.Active);
        report.Constraint(new ConstraintId("nope")).Should().BeNull(
            "an unknown id must not fall through to the variable record whose ConstraintId happens to be null");
    }

    /// <summary>**组汇总只统计约束实例** / Group summaries count constraint instances only.</summary>
    /// <remarks>
    /// 基线把变量边界留在 `activities` 里、但不进组计数。混进去会让"这个组里有 2 条"变成"2 条约束 + 1 个变量边界"——**而后者的 2 没人能解释。**
    ///
    /// The baseline keeps variable bounds in `activities` but out of the group counts. Mixing them turns "two in this group" into "two constraints and one variable bound" —
    /// **and nobody can explain the two.**
    /// </remarks>
    [Fact]
    public void TheGroupSummaries_ShouldCountConstraintsOnly() {
        var report = new ConstraintActivityReport(
            [
                Constraint("c1", "g1", ActivityStatus.Active),
                Constraint("c2", "g1", ActivityStatus.Violated),
                Variable("x", ActivityStatus.Active),
            ],
            [
                new ActivityGroupSummary("g1", Total: 2, Active: 1, NearlyActive: 0, Inactive: 0,
                    SatisfiedWithoutSlackMetric: 0, Violated: 1, Unknown: 0),
            ],
            Tolerance: 1e-6,
            NearlyActiveTolerance: 1e-3);

        ActivityGroupSummary summary = report.Groups.Single();
        summary.Total.Should().Be(2, "the variable record is not part of the group");
        summary.ConstraintCount.Should().Be(summary.Total, "the compatibility alias has to agree with the field it aliases");
        // **各状态计数之和要等于 total**：否则汇总表自己就不自洽。
        // **The per-status counts have to add up to the total**: otherwise the summary table disagrees with itself.
        (summary.Active + summary.NearlyActive + summary.Inactive
            + summary.SatisfiedWithoutSlackMetric + summary.Violated + summary.Unknown)
            .Should().Be(summary.Total, "a summary whose parts do not add up to its whole is worse than no summary");
    }

    /// <summary>**`Source` 缺省回落到证据来源，且在构造时定下** / `Source` defaults to the evidence source, resolved at construction.</summary>
    [Fact]
    public void TheSource_ShouldDefaultToTheEvidenceSource() {
        ConstraintActivity withoutSource = Constraint("c1", "g1", ActivityStatus.Active);
        withoutSource.Source.Should().BeSameAs(withoutSource.Evidence.Source,
            "the fallback is the evidence's own source");

        var explicitSource = new DiagnosticSource.SparseDomain(new VariableId("x"));
        var withSource = new ConstraintActivity(
            new ConstraintId("c1"), "g1", ActivityStatus.Active, Evidence("c1"), source: explicitSource);
        withSource.Source.Should().BeSameAs(explicitSource,
            "an explicit source wins over the fallback, or a caller could never override it");
    }
}
