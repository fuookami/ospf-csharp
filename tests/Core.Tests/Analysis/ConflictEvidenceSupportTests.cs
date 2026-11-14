#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 冲突证据的映射与汇总 / Mapping and summarizing conflict evidence.
/// </summary>
/// <remarks>
/// **指错位置比不给位置更糟**：用户会去改一个改不动的地方。
///
/// **Pointing at the wrong place is worse than pointing nowhere**: the user edits something that will not help.
/// </remarks>
public class ConflictEvidenceSupportTests {
    /// <summary>**三种成员映射互不相同** / The three member kinds map differently.</summary>
    /// <remarks>
    /// 值域不能被当成边界：**让人去调边界，而问题在值域，等于指错位置。**
    ///
    /// A domain must not be reported as a bound: **sending the user to a bound when the problem is a sparse domain points at the wrong edit.**
    /// </remarks>
    [Fact]
    public void TheMembers_ShouldMapToDistinctSources() {
        ConflictEvidenceSupport.SourceOf(new InfeasibilityMember.ConstraintMember(new ConstraintId("c1")))
            .Should().BeOfType<DiagnosticSource.Constraint>();

        DiagnosticSource lower = ConflictEvidenceSupport.SourceOf(new InfeasibilityMember.VariableBoundMember(
            new VariableBoundRef(new VariableId("x"), BoundSide.Lower)));
        lower.Should().BeOfType<DiagnosticSource.VariableLowerBound>(
            "the lower bound and the upper bound are two different edits");

        DiagnosticSource upper = ConflictEvidenceSupport.SourceOf(new InfeasibilityMember.VariableBoundMember(
            new VariableBoundRef(new VariableId("x"), BoundSide.Upper)));
        upper.Should().BeOfType<DiagnosticSource.VariableUpperBound>();
        upper.Should().NotBe(lower, "collapsing the two sides leaves the user unable to tell which one to relax");

        ConflictEvidenceSupport.SourceOf(new InfeasibilityMember.VariableDomainMember(
                new VariableDomainRef(new VariableId("x"))))
            .Should().BeOfType<DiagnosticSource.SparseDomain>("a sparse domain is not a bound");
    }

    private static ConstraintProgrammingModelSnapshot Snapshot() {
        var model = new ConstraintProgrammingModel("groups", ObjectCategory.Maximum, identityNamespace: "groups");
        var x = new IntVar("x");
        var xId = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: xId, variable: x, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/x")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(x, domain, xId);
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();

        // **先注册无组约束**：模型的 `GroupName` 是 `group?.Name ?? _currentGroupName`，
        // 也就是说**不带 group 的约束会继承"当前组"**。放后面注册就不再是无组的了。
        //
        // **Register the ungrouped one first**: the model sets `GroupName = group?.Name ?? _currentGroupName`, so **a constraint added without a group inherits the current one**.
        // Added later, it would no longer be ungrouped.
        model.AddConstraint(ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, new Int64(1)).Value,
            id: new ConstraintId("c:loose"), name: "loose").Value.Should().NotBeNull();

        var packing = new TestGroup("packing");
        model.AddConstraint(ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(4)).Value,
            id: new ConstraintId("c:packing-1"), name: "packing-1", group: packing).Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, new Int64(2)).Value,
            id: new ConstraintId("c:packing-2"), name: "packing-2", group: packing).Value.Should().NotBeNull();
        model.Maximize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        return model.Snapshot().Value!;
    }

    /// <summary>**只有带组名的约束计入按组汇总** / Only named-group constraints enter the group summary.</summary>
    /// <remarks>
    /// 一份"某组有 2 条冲突"的汇总，说的是**两条有组名的约束**，不是"其中一条组名未知"。
    /// 把它们混进同一行，用户会去查一个不存在的组。
    ///
    /// "2 conflicts in this group" means **two constraints that have a group**, not "one of which has an unknown group". Merging them sends the user looking for a group that
    /// does not exist.
    /// </remarks>
    [Fact]
    public void TheGroupSummary_ShouldSkipConstraintsWithoutAGroup() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var sources = new List<DiagnosticSource> {
            new DiagnosticSource.Constraint(new ConstraintId("c:packing-1")),
            new DiagnosticSource.Constraint(new ConstraintId("c:packing-2")),
            new DiagnosticSource.Constraint(new ConstraintId("c:loose")),
            new DiagnosticSource.VariableLowerBound(new VariableId("source:x")),
        };

        IReadOnlyList<ConflictGroupSummary> summaries =
            ConflictEvidenceSupport.GroupSummaries(snapshot, sources);

        summaries.Should().ContainSingle("only the packing group has named members");
        summaries[0].Group.Should().Be("packing");
        summaries[0].Count.Should().Be(2,
            "two constraints carry that group; the ungrouped one and the variable bound are not counted in it");

        // **反向对照**：只断言"未分组的被跳过"会漏掉"全都跳过"。
        // **The reverse control**: asserting only "the ungrouped one is skipped" would miss "everything is skipped".
        summaries.Select(entry => entry.Group).Should().NotContain("", "an empty group name is not a group");
    }

    /// <summary>**空输入给空汇总，不炸** / Empty input gives an empty summary.</summary>
    [Fact]
    public void TheEmptyInput_ShouldBeEmpty() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        ConflictEvidenceSupport.GroupSummaries(snapshot, []).Should().BeEmpty();
    }

    /// <summary>**未知的 id 不参与汇总** / An unknown constraint id takes no part.</summary>
    [Fact]
    public void TheUnknownConstraint_ShouldNotAppear() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        ConflictEvidenceSupport.GroupSummaries(
                snapshot, [new DiagnosticSource.Constraint(new ConstraintId("c:absent"))])
            .Should().BeEmpty("there is no group to look up");
    }

    /// <summary>测试用的约束组 / A constraint group for the fixture.</summary>
    private sealed class TestGroup(string name) : Fuookami.Ospf.Core.Model.Mechanism.IMetaConstraintGroup {
        public bool Lazy => false;

        public string Name => name;
    }
}
