#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 固定整数 LP 敏感性协议 / The fixed-integer LP sensitivity protocol.
/// </summary>
/// <remarks>
/// **实现的是协议，不是求解**：`FixedIntegerLpBackend` 的求解实现要真实 LP 后端，
/// 这里只把**不碰后端的那一半**（范围、结果、选项、请求、报告）搬过来，并把边界写清楚。
///
/// 判据钉的都是**某个值不合法时会不会被放过去**，因为这一层的作用就是"让不合法的值进不去"。
///
/// **The protocol, not the solving is implemented**: the backend hook needs a real LP solver, so only **the half that touches no backend** came over. The criteria pin **whether an
/// invalid value gets through**, because keeping invalid values out is this layer's whole job.
/// </remarks>
public class FixedIntegerLpSensitivityTests {
    /// <summary>**下界大于上界的"范围"要被拒** / A range with the lower end above the upper is refused.</summary>
    /// <remarks>
    /// 只查有限性会让 `[5, 3]` 通过——**而它覆盖的区间是空的**，一个空范围在报告里和"范围很窄"长得一样。
    ///
    /// Checking finiteness alone lets `[5, 3]` through, **and the interval it covers is empty** — an empty range looks exactly like "a narrow range" in a report.
    /// </remarks>
    [Fact]
    public void TheRange_ShouldRefuseAnInvertedInterval() {
        var inverted = new SensitivityRange(new Flt64(5.0), new Flt64(3.0));
        Action validate = inverted.Validate;
        validate.Should().Throw<ArgumentException>("[5, 3] covers nothing, and an empty range reads as a narrow one");

        new SensitivityRange(new Flt64(3.0), new Flt64(5.0)).Invoking(r => r.Validate()).Should().NotThrow();
        new SensitivityRange(new Flt64(3.0)).Invoking(r => r.Validate()).Should().NotThrow();
        new SensitivityRange().Invoking(r => r.Validate()).Should().NotThrow();
    }

    /// <summary>**非有限端点要被拒** / A non-finite endpoint is refused.</summary>
    [Fact]
    public void TheRange_ShouldRefuseANonFiniteEndpoint() {
        Action infiniteUpper = () => new SensitivityRange(null, new Flt64(double.PositiveInfinity)).Validate();
        infiniteUpper.Should().Throw<ArgumentException>("an infinite upper end is no range at all");
    }

    /// <summary>**`NaN` 阈值要被拒** / A `NaN` threshold is refused.</summary>
    /// <remarks>
    /// `NaN` 会让每次比较都为假，于是**没有一条被判为有效**，而报告看起来只是"都没超阈值"。
    ///
    /// `NaN` makes every comparison false, so **nothing is classified effective**, while the report simply looks like "none exceeded the threshold".
    /// </remarks>
    [Fact]
    public void TheOptions_ShouldRefuseNaN() {
        Action nan = () => new FixedIntegerLpSensitivityOptions(double.NaN).Validate();
        nan.Should().Throw<ArgumentException>("a NaN threshold silently disables the classification");
        Action negative = () => new FixedIntegerLpSensitivityOptions(-1e-9).Validate();
        negative.Should().Throw<ArgumentException>();

        var standard = new FixedIntegerLpSensitivityOptions();
        standard.DualTolerance.Should().Be(FixedIntegerLpSensitivityOptions.DefaultDualTolerance,
            "the default is part of the protocol, so it has to match the baseline's 1e-9");
        standard.Invoking(o => o.Validate()).Should().NotThrow();
    }

    /// <summary>**非有限目标值或对偶值要被拒** / A non-finite objective or dual is refused.</summary>
    [Fact]
    public void TheSolveResult_ShouldRefuseNonFiniteNumbers() {
        var badObjective = new FixedIntegerLpSolveResult(AnalysisStatus.Unknown, ObjectiveValue: new Flt64(double.PositiveInfinity));
        Action objective = badObjective.Validate;
        objective.Should().Throw<ArgumentException>();

        var badDual = new FixedIntegerLpSolveResult(
            AnalysisStatus.Unknown,
            Duals: new Dictionary<ConstraintId, Flt64?> { [new ConstraintId("c1")] = new Flt64(double.NaN) });
        Action dual = badDual.Validate;
        dual.Should().Throw<ArgumentException>("a NaN dual would be copied straight into a report");

        // **合法的要给过**：只钉拒绝会漏掉"全都拒绝"。
        // **Valid ones have to pass**: pinning only the refusals would miss an implementation that refuses everything.
        var good = new FixedIntegerLpSolveResult(
            AnalysisStatus.Reachable,
            ObjectiveValue: new Flt64(1.0),
            Duals: new Dictionary<ConstraintId, Flt64?> { [new ConstraintId("c1")] = new Flt64(0.5) });
        good.Invoking(r => r.Validate()).Should().NotThrow();
    }

    /// <summary>**作用域只有一个成员，而且它不是全局影子价格** / One scope, and it is not a global shadow price.</summary>
    [Fact]
    public void TheScope_ShouldBeTheFixedIncumbentOne() {
        // **`Equal(params T[] values)` 的最后一个字符串不是 reason**，它会被当成元素（这条判据自己先撞过）。
        // **The trailing string in `Equal(params T[])` is not a reason**, it becomes an element — this criterion hit that first.
        string[] scopes = Enum.GetNames<LocalSensitivityScope>();
        scopes.Should().HaveCount(1,
            "the baseline has exactly one scope; a second one would claim a validity these duals do not have");
        scopes[0].Should().Be("FixedIntegerIncumbent");

        var sensitivity = new LocalConstraintSensitivity(new ConstraintId("c1"), DualValue: new Flt64(2.0));
        sensitivity.Scope.Should().Be(LocalSensitivityScope.FixedIntegerIncumbent);
        sensitivity.Dual.Should().Be(sensitivity.DualValue, "the adapter alias has to track the field");
        sensitivity.Effective.Should().Be(sensitivity.LocalEffective);
    }

    /// <summary>**报告按约束 ID 查得到，查不到时返回 `null`** / A report finds by ID and returns null when absent.</summary>
    [Fact]
    public void TheReport_ShouldFindByConstraintId() {
        var report = new FixedIntegerLpSensitivityReport(
            AnalysisStatus.Reachable,
            Sensitivities: [
                new LocalConstraintSensitivity(new ConstraintId("c1"), DualValue: new Flt64(1.0)),
                new LocalConstraintSensitivity(new ConstraintId("c2")),
            ]);

        report.Constraint(new ConstraintId("c1")).Should().NotBeNull();
        report.Constraint(new ConstraintId("c2"))!.DualValue.Should().BeNull(
            "a record with no dual is still a record");
        report.Constraint(new ConstraintId("nope")).Should().BeNull();
        report.LocalSensitivities.Should().HaveCount(2);
    }
}
