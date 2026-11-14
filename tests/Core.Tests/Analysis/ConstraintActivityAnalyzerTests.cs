#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using System;
using System.Collections.Generic;
using System.Linq;
using ReportRet = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Analysis.ConstraintActivityReport, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 活动性分析 / Activity analysis.
/// </summary>
/// <remarks>
/// **B970 补 `ConstraintActivityAnalyzer` 的核心**（基线 566 行）。基线的 `analyze(session, …)` 重载要
/// `CriticalConstraintAnalysisSession`（缓存与生命周期），那一层还没搬，**本批只搬纯计算入口，没搬的重载没有空跑版本**。
///
/// 判据钉的是**"精确值分类、标量值展示"**这条分工：分类用 `BigInteger` 精确算，
/// 标量只用于展示。**两者混用会让 `9007199254740993` 这类值在容差边界上分类错。**
///
/// **B970 ported the core of `ConstraintActivityAnalyzer`** (566 baseline lines). The criteria pin the split between **"classify exactly, display a scalar"**: classification uses
/// `BigInteger`, the scalar is for display only. **Mixing them misclassifies values like `9007199254740993` at a tolerance boundary.**
/// </remarks>
public class ConstraintActivityAnalyzerTests {
    /// <summary>构造一个"变量 + 一条下界约束"的最小模型 / Build a minimal model: one variable and one lower-bound constraint.</summary>
    private static (ConstraintProgrammingModelSnapshot Snapshot, VariableId Id) BuildBoundModel(
        Int64 lower, Int64 upper) {
        using var model = new ConstraintProgrammingModel(name: "activity", objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        var id = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(lower, upper).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, id);

        model.RegisterVariable(
            id: id,
            variable: variable,
            domain: domain,
            scope: ModelElementScope.Stable,
            origin: "fixture/x").IsOk.Should().BeTrue();
        model.RegisterExpression("x", expression).IsOk.Should().BeTrue();

        ConstraintProgrammingConstraint.IntegerComparison constraint =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, upper).Value;
        model.AddConstraint(constraint, "constraint:x").IsOk.Should().BeTrue();
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).IsOk.Should().BeTrue();

        return (model.Snapshot().Value, id);
    }

    /// <summary>**`NaN` 容差在构造时被拒** / A `NaN` tolerance is refused at construction.</summary>
    /// <remarks>
    /// `NaN` 让每次比较都为假，于是**没有一条被判为 active 或 violated**，报告看起来只是"全部 inactive"。
    /// **而"`NaN` 比较恒假"这件事不会在任何地方报错。**
    ///
    /// `NaN` makes every comparison false, so **nothing is ever classified active or violated** and the report simply looks "all inactive". **Nothing anywhere reports that a
    /// `NaN` comparison is always false.**
    /// </remarks>
    [Fact]
    public void TheConstructor_ShouldRefuseNaN() {
        Action nanTolerance = () => new ConstraintActivityAnalyzer(tolerance: double.NaN);
        nanTolerance.Should().Throw<ArgumentException>("a NaN tolerance silently disables classification");

        Action inverted = () => new ConstraintActivityAnalyzer(tolerance: 1e-3, nearlyActiveTolerance: 1e-6);
        inverted.Should().Throw<ArgumentException>(
            "NearlyActive below the active tolerance would make a NearlyActive state unreachable");

        Action zeroFloor = () => new ConstraintActivityAnalyzer(normalizationFloor: 0.0);
        zeroFloor.Should().Throw<ArgumentException>("a zero denominator floor divides by zero");

        var standard = new ConstraintActivityAnalyzer();
        standard.Tolerance.Should().Be(ConstraintActivityAnalyzer.DefaultTolerance);
        standard.NearlyActiveTolerance.Should()
            .Be(ConstraintActivityAnalyzer.DefaultTolerance * ConstraintActivityAnalyzer.DefaultNearlyActiveFactor,
                "the default NearlyActive threshold is the tolerance times the default factor");
    }

    /// <summary>**没有基线解时全部 `Unknown`，不是 `Inactive`** / With no baseline every record is `Unknown`, not `Inactive`.</summary>
    /// <remarks>
    /// **`Unknown` 与 `Inactive` 是两件事**：前者是"没证据"，后者是"有证据且不活跃"。
    /// 合并会让一份没有解的报告看起来像"模型很松"。
    ///
    /// **`Unknown` and `Inactive` are different**: the first is "no evidence", the second "evidence says inactive". Merging them makes a report with no solution look like
    /// "the model is loose".
    /// </remarks>
    [Fact]
    public void TheMissingSolution_ShouldYieldUnknown() {
        (ConstraintProgrammingModelSnapshot snapshot, _) = BuildBoundModel(Int64.Zero, new Int64(3));
        var analyzer = new ConstraintActivityAnalyzer();

        ReportRet report = analyzer.Analyze(snapshot, solution: null);
        ConstraintActivityReport result = report.Value;
        result.Should().NotBeNull();
        result.Activities.Should().NotBeEmpty();
        result.Activities.Should().OnlyContain(activity => activity.Status == ActivityStatus.Unknown,
            "no solution means no evidence, which is Unknown and never Inactive");
        result.Activities.Should().OnlyContain(activity => !activity.Slack.HasValue,
            "an unknown record has no slack to report");
        result.Activities.Should().OnlyContain(
            activity => !string.IsNullOrEmpty(activity.Evidence.Message),
            "every unknown record has to say why");
    }

    /// <summary>**变量边界按两侧分别登记，`Boolean` 值域不加第三条** / Bounds per side, and a `Boolean` domain gets no third record.</summary>
    [Fact]
    public void TheBoundRecords_ShouldBePerSide() {
        (ConstraintProgrammingModelSnapshot snapshot, VariableId id) = BuildBoundModel(Int64.Zero, new Int64(3));

        ReportRet report = new ConstraintActivityAnalyzer().Analyze(
            snapshot,
            new Dictionary<VariableId, Int64> { [id] = new Int64(1) });

        ConstraintActivityReport result = report.Value;
        result.VariableActivities.Should().HaveCount(2,
            "one lower bound and one upper bound, and an interval domain adds no sparse-domain record");
        result.VariableActivities.Select(activity => activity.Source.StableId)
            .Should().BeEquivalentTo(new[] { "variable:" + id.Value + ":lower", "variable:" + id.Value + ":upper" });
        result.VariableActivities.Should().OnlyContain(activity => !activity.ConstraintId.HasValue,
            "a variable bound has no constraint identity");
    }

    /// <summary>**边界上的值判为 `Active`，离得远的判为 `Inactive`** / A value on the boundary is active; a far one is inactive.</summary>
    [Fact]
    public void TheBoundClassification_ShouldUseTheTolerance() {
        (ConstraintProgrammingModelSnapshot snapshot, VariableId id) = BuildBoundModel(Int64.Zero, new Int64(3));

        ConstraintActivityReport onBoundary = new ConstraintActivityAnalyzer().Analyze(
            snapshot, new Dictionary<VariableId, Int64> { [id] = new Int64(3) }).Value;
        onBoundary.VariableActivities.Should().Contain(
            activity => activity.Source.StableId.EndsWith(":upper", StringComparison.Ordinal)
                && activity.Status == ActivityStatus.Active,
            "a value sitting on the upper bound is active");

        ConstraintActivityReport farAway = new ConstraintActivityAnalyzer().Analyze(
            snapshot, new Dictionary<VariableId, Int64> { [id] = Int64.Zero }).Value;
        farAway.VariableActivities.Should().Contain(
            activity => activity.Source.StableId.EndsWith(":upper", StringComparison.Ordinal)
                && activity.Status == ActivityStatus.Inactive,
            "a value three away from the upper bound is inactive");
    }

    /// <summary>**精确字符串与浮点值并存，且大整数不丢精度** / The exact spelling sits beside the float, and large integers keep their digits.</summary>
    /// <remarks>
    /// `9007199254740993` 走一趟 `double` 会变成 `9007199254740992`。**作为约束边界那是两个不同的模型。**
    ///
    /// `9007199254740993` through a `double` becomes `9007199254740992`. **As a constraint boundary those are two different models.**
    /// </remarks>
    [Fact]
    public void TheEvidence_ShouldKeepTheExactInteger() {
        var large = new Int64(9007199254740993L);
        (ConstraintProgrammingModelSnapshot snapshot, VariableId id) = BuildBoundModel(Int64.Zero, large);

        ConstraintActivityReport result = new ConstraintActivityAnalyzer().Analyze(
            snapshot, new Dictionary<VariableId, Int64> { [id] = large }).Value;

        ConstraintActivity upper = result.VariableActivities.Single(
            activity => activity.Source.StableId.EndsWith(":upper", StringComparison.Ordinal));
        upper.Evidence.ExactBoundary.Should().Be("9007199254740993",
            "the exact spelling has to survive, or a boundary silently moves by one");
        upper.Evidence.ExactValue.Should().Be("9007199254740993");
        upper.Status.Should().Be(ActivityStatus.Active,
            "classification uses the exact value, so an exact hit is active even though the float cannot represent it");
    }

    /// <summary>**组汇总只统计约束实例** / Group summaries count constraint instances only.</summary>
    [Fact]
    public void TheGroupSummaries_ShouldCountConstraintsOnly() {
        (ConstraintProgrammingModelSnapshot snapshot, VariableId id) = BuildBoundModel(Int64.Zero, new Int64(3));

        ConstraintActivityReport result = new ConstraintActivityAnalyzer().Analyze(
            snapshot, new Dictionary<VariableId, Int64> { [id] = new Int64(1) }).Value;

        result.TotalConstraints.Should().Be(result.ConstraintActivities.Count);
        result.TotalActivities.Should().Be(result.Activities.Count);
        result.TotalActivities.Should().BeGreaterThan(result.TotalConstraints,
            "the variable bounds are records too, they are just not constraints");
        foreach (ActivityGroupSummary summary in result.Groups) {
            (summary.Active + summary.NearlyActive + summary.Inactive
                + summary.SatisfiedWithoutSlackMetric + summary.Violated + summary.Unknown)
                .Should().Be(summary.Total, "the parts have to add up to the whole");
        }
    }
}
