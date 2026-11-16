#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 诊断来源与活动性证据 / Diagnostic sources and activity evidence.
/// </summary>
/// <remarks>
/// 补的是 `core.analysis` 里不依赖原生后端的那三个类型**。判据钉的都是"换个后端还能不能对上"，
/// 因为那正是这些类型存在的理由。
///
/// **implemented the three `core.analysis` types that need no native backend.** The criteria pin "does it still line up after the backend changes", because that is the reason
/// these types exist.
/// </remarks>
public class DiagnosticSourceTests {
    /// <summary>**稳定身份不含行号、列号** / The stable identity carries no row or column.</summary>
    [Fact]
    public void TheStableIds_ShouldNotCarryBackendPositions() {
        var constraint = new DiagnosticSource.Constraint(new ConstraintId("c1"));
        var lower = new DiagnosticSource.VariableLowerBound(new VariableId("x"));
        var upper = new DiagnosticSource.VariableUpperBound(new VariableId("x"));
        var bound = new DiagnosticSource.VariableBound(new VariableBoundRef(new VariableId("x"), BoundSide.Upper));
        var domain = new DiagnosticSource.SparseDomain(new VariableId("x"));

        constraint.StableId.Should().Be("constraint:c1");
        lower.StableId.Should().Be("variable:x:lower");
        upper.StableId.Should().Be("variable:x:upper");
        bound.StableId.Should().Be("variable:x:upper",
            "a bound reference's identity comes from its side, not from the type it was constructed as");
        domain.StableId.Should().Be("variable:x:domain");

        // **没有任何一个含数字位置**：这些 id 要能跨后端对照。
        // **Not one carries a numeric position**: these ids have to line up across backends.
        foreach (string id in new[] { constraint.StableId, lower.StableId, upper.StableId, bound.StableId, domain.StableId }) {
            id.Should().NotMatchRegex(@":\d+$",
                "a trailing numeric position would break the moment the backend reordered its rows");
        }
    }

    /// <summary>**从引用构造时，边不对就拒** / Constructing from a reference refuses the wrong side.</summary>
    /// <remarks>
    /// 静默接受会让"下界来源"指向一个上界，**而 `StableId` 仍然说 lower**——身份与内容对不上，是最难查的那种错。
    ///
    /// Accepting it silently points a lower-bound source at an upper bound **while `StableId` still says lower** — identity disagreeing with content is the hardest kind of
    /// bug to find.
    /// </remarks>
    [Fact]
    public void TheSideSpecificSources_ShouldRefuseTheWrongSide() {
        var upperRef = new VariableBoundRef(new VariableId("x"), BoundSide.Upper);
        var lowerRef = new VariableBoundRef(new VariableId("x"), BoundSide.Lower);

        Action fromUpper = () => _ = new DiagnosticSource.VariableLowerBound(upperRef);
        Action fromLower = () => _ = new DiagnosticSource.VariableUpperBound(lowerRef);

        fromUpper.Should().Throw<ArgumentException>("a lower-bound source built from an upper bound would lie in its StableId");
        fromLower.Should().Throw<ArgumentException>();

        // **对的边要能构造**：只钉"拒绝"会漏掉"全都拒绝"这种实现。
        // **The right side has to work**: pinning only the refusals would miss an implementation that refuses everything.
        new DiagnosticSource.VariableLowerBound(lowerRef).StableId.Should().Be("variable:x:lower");
        new DiagnosticSource.VariableUpperBound(upperRef).StableId.Should().Be("variable:x:upper");
    }

    /// <summary>**目标条件没有不相容成员** / An objective target has no incompatibility member.</summary>
    /// <remarks>
    /// 目标不满足不是"某个约束不相容"。硬塞一个成员进去，IIS 报告就会多出一条后端从没找到的成员。
    ///
    /// A missed target is not "some constraint is inconsistent"; forcing a member in would add one the backend never found to the IIS report.
    /// </remarks>
    [Fact]
    public void TheProjection_ShouldLeaveTheObjectiveTargetUnmapped() {
        var target = new DiagnosticSource.ObjectiveTargetSource(
            ObjectiveTarget.CreateAtLeast(new ObjectiveId("obj"), Fuookami.Ospf.Math.Algebra.Number.Flt64.One));

        target.AsInfeasibilityMember().Should().BeNull(
            "an objective target is not an infeasibility member; inventing one would put a member in the report the solver never produced");
        target.Kind.Should().Be("objective-target");

        // **其余四种都要投影得出来**：否则"返回 null"可能是"全都返回 null"。
        // **The other four must project**: otherwise "returns null" could mean "always returns null".
        new DiagnosticSource.Constraint(new ConstraintId("c1")).AsInfeasibilityMember()
            .Should().BeOfType<InfeasibilityMember.ConstraintMember>();
        new DiagnosticSource.VariableBound(new VariableBoundRef(new VariableId("x"), BoundSide.Lower))
            .AsInfeasibilityMember().Should().BeOfType<InfeasibilityMember.VariableBoundMember>();
        new DiagnosticSource.SparseDomain(new VariableId("x")).AsInfeasibilityMember()
            .Should().BeOfType<InfeasibilityMember.VariableDomainMember>();
    }

    /// <summary>**精确字符串与浮点值分开保留** / The exact spelling is kept apart from the float.</summary>
    /// <remarks>
    /// `9007199254740993` 走一趟 `double` 会变成 `9007199254740992`。作为约束边界，**那是两个不同的模型**。
    ///
    /// `9007199254740993` through a `double` becomes `9007199254740992`; **as a constraint boundary those are two different models.**
    /// </remarks>
    [Fact]
    public void TheEvidence_ShouldKeepTheExactSpelling() {
        var evidence = new ActivityEvidence(
            new DiagnosticSource.Constraint(new ConstraintId("c1")),
            ActivityEvidenceKind.Comparison,
            Lhs: 9007199254740992.0,
            Rhs: 9007199254740992.0,
            Relation: ConstraintProgrammingComparison.Equal,
            ExactLhs: "9007199254740993",
            ExactRhs: "9007199254740993",
            Satisfied: true);

        evidence.ExactLhs.Should().Be("9007199254740993");
        evidence.Lhs.Should().Be(9007199254740992.0,
            "the float is deliberately lossy, which is why the exact spelling has to travel beside it");
        evidence.ExactLhs.Should().NotBe(evidence.Lhs!.Value.ToString("R"),
            "if the two agreed here, the field would be redundant and the test would prove nothing");
    }

    /// <summary>**`ActivityStatus` 六个成员，顺序** / Six members, in its order.</summary>
    /// <remarks>
    /// 顺序会进序列化与比较，所以**成员集合对不代表顺序对**。
    ///
    /// The order reaches serialization and comparison, so **the right set does not imply the right order**.
    /// </remarks>
    [Fact]
    public void TheActivityStatus_ShouldKeepTheBaselineOrder() {
        string[] names = Enum.GetNames<ActivityStatus>();
        names.Should().Equal(
            "Active",
            "NearlyActive",
            "Inactive",
            "SatisfiedWithoutSlackMetric",
            "Violated",
            "Unknown");
    }

    /// <summary>**求解器成员反推回来的来源能再正推回去** / A recovered source round-trips back to the member.</summary>
    /// <remarks>
    /// **这是 `ConflictAnalyzer.sourceOf` 与既有 `AsInfeasibilityMember` 之间的双向一致。**
    /// **只测一个方向，两张表就可以各错一半而都"通过"。**
    ///
    /// **This is the two-way agreement between `sourceOf` and the existing `AsInfeasibilityMember`.**
    /// **Testing one direction lets both tables be half wrong and still "pass".**
    /// </remarks>
    [Theory]
    [InlineData("constraint")]
    [InlineData("lower")]
    [InlineData("upper")]
    [InlineData("domain")]
    public void TheMemberRoundTrip_ShouldPreserveEverything(string kind) {
        InfeasibilityMember member = kind switch {
            "constraint" => new InfeasibilityMember.ConstraintMember(new ConstraintId("constraint:c")),
            "lower" => new InfeasibilityMember.VariableBoundMember(
                new VariableBoundRef(new VariableId("source:x"), BoundSide.Lower)),
            "upper" => new InfeasibilityMember.VariableBoundMember(
                new VariableBoundRef(new VariableId("source:x"), BoundSide.Upper)),
            _ => new InfeasibilityMember.VariableDomainMember(
                new VariableDomainRef(new VariableId("source:x"))),
        };

        var source = DiagnosticSource.FromInfeasibilityMember(member);

        source.AsInfeasibilityMember().Should().Be(member,
            "a translation that loses information cannot be translated back");
    }

    /// <summary>**边界方向不能丢** / The bound side cannot be lost.</summary>
    /// <remarks>
    /// **丢掉方向等于把"下界太紧"和"上界太紧"报成同一件事**，而修法一个要放宽下界、一个要放宽上界。
    ///
    /// **Dropping the side reports "the lower bound is too tight" and "the upper bound is too tight" as one thing**, while one fix relaxes the lower bound and the other the upper.
    /// </remarks>
    [Fact]
    public void TheLowerAndUpperBounds_ShouldStayDistinct() {
        var lower = DiagnosticSource.FromInfeasibilityMember(
            new InfeasibilityMember.VariableBoundMember(
                new VariableBoundRef(new VariableId("source:x"), BoundSide.Lower)));
        var upper = DiagnosticSource.FromInfeasibilityMember(
            new InfeasibilityMember.VariableBoundMember(
                new VariableBoundRef(new VariableId("source:x"), BoundSide.Upper)));

        lower.Should().NotBe(upper, "the same variable on the other side is a different fix");
        ((DiagnosticSource.VariableBound)lower).Ref.Side.Should().Be(BoundSide.Lower);
        ((DiagnosticSource.VariableBound)upper).Ref.Side.Should().Be(BoundSide.Upper);
    }
}
