#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 后端冲突到原始证据的映射 / Remapping a backend conflict onto the original evidence.
/// </summary>
/// <remarks>
/// **后端会报告它自己认定的一切，包括调用方没问的东西。**
///
/// **The backend reports everything it considers relevant, including things the caller never asked about.**
/// </remarks>
public class ConflictRemapTests {
    private static ConstraintProgrammingModelSnapshot Snapshot() {
        var model = new ConstraintProgrammingModel("remap", ObjectCategory.Maximum, identityNamespace: "remap");
        var x = new IntVar("x");
        var xId = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: xId, variable: x, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/x")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(x, domain, xId);
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(4)).Value,
            id: new ConstraintId("c:asked"), name: "asked").Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, new Int64(6)).Value,
            id: new ConstraintId("c:other"), name: "other").Value.Should().NotBeNull();
        model.Maximize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        return model.Snapshot().Value!;
    }

    /// <summary>**只返回请求过的证据** / Only requested evidence comes back.</summary>
    /// <remarks>
    /// 后端报了两条约束，调用方只问了一条。**直接返回后端的集合，一份"关于 X 的解释"里会冒出 Y 的成员。**
    ///
    /// The backend named two constraints, the caller asked about one. **Returning the backend's set directly would let an explanation about X carry a member of Y.**
    /// </remarks>
    [Fact]
    public void TheRemap_ShouldFilterToTheRequestedSet() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var conflict = new ConstraintProgrammingConflict(
            constraintIds: [new ConstraintId("c:asked"), new ConstraintId("c:other")]);
        var requested = new List<DiagnosticSource> { new DiagnosticSource.Constraint(new ConstraintId("c:asked")) };

        IReadOnlyList<DiagnosticSource> remapped =
            ConflictEvidenceSupport.RemapBackendConflict(snapshot, conflict, requested);

        remapped.Should().ContainSingle("the caller asked about one constraint and only that one belongs in the answer");
        remapped[0].Should().Be(requested[0]);
    }

    /// <summary>**没请求过的证据一个都不给** / Nothing outside the request leaks through.</summary>
    /// <remarks>
    /// **反向对照**：上一条只证明"请求的留下了"，这条证明"没请求的没混进来"。
    ///
    /// **The reverse control**: the previous one only proves the requested item survived; this one proves nothing else slipped in.
    /// </remarks>
    [Fact]
    public void TheRemap_ShouldNotLeakUnrequestedEvidence() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var conflict = new ConstraintProgrammingConflict(
            constraintIds: [new ConstraintId("c:other")]);
        var requested = new List<DiagnosticSource> { new DiagnosticSource.Constraint(new ConstraintId("c:asked")) };

        ConflictEvidenceSupport.RemapBackendConflict(snapshot, conflict, requested)
            .Should().BeEmpty("the backend talked about a constraint the caller did not ask about");
    }

    /// <summary>**未注册的 id 映射不出来，直接跳过** / An unregistered id has nothing to map onto.</summary>
    [Fact]
    public void TheUnknownConstraintId_ShouldBeSkipped() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var conflict = new ConstraintProgrammingConflict(
            constraintIds: [new ConstraintId("c:absent")]);
        var requested = new List<DiagnosticSource> { new DiagnosticSource.Constraint(new ConstraintId("c:absent")) };

        ConflictEvidenceSupport.RemapBackendConflict(snapshot, conflict, requested)
            .Should().BeEmpty("the id is not in this model, so there is no original evidence behind it");
    }

    /// <summary>**没有冲突就什么都不返回** / No conflict means no evidence.</summary>
    [Fact]
    public void TheNullConflict_ShouldBeEmpty() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        ConflictEvidenceSupport.RemapBackendConflict(
                snapshot, null, [new DiagnosticSource.Constraint(new ConstraintId("c:asked"))])
            .Should().BeEmpty();
    }

    /// <summary>**假设与它的否定必须是两个不同的键** / An assumption and its negation need different keys.</summary>
    /// <remarks>
    /// 少了 `negated`，两者同键——**而"取反之后仍然不可行"和"可行"是相反的结论。**
    ///
    /// Without `negated` the two share a key — **and "still infeasible after negating" and "feasible" are opposite conclusions.**
    /// </remarks>
    [Fact]
    public void TheLiteralKey_ShouldDistinguishNegation() {
        var variable = new BinVar("analysis-assumption-0");
        var positive = new BooleanLiteral.VariableLiteral(variable, negated: false, id: new VariableId("assume:0"));
        var negative = new BooleanLiteral.VariableLiteral(variable, negated: true, id: new VariableId("assume:0"));

        ConflictEvidenceSupport.LiteralKey(positive)
            .Should().NotBe(ConflictEvidenceSupport.LiteralKey(negative),
                "an assumption and its negation are opposite hypotheses");

        // **反向对照**：同一个字面量两次取键必须相同，否则映射根本对不上。
        // **The reverse control**: the same literal twice must give the same key, or the mapping never matches.
        ConflictEvidenceSupport.LiteralKey(positive)
            .Should().Be(ConflictEvidenceSupport.LiteralKey(positive));
    }

    /// <summary>**假设能映射回它守护的那条约束** / An assumption maps back to the constraint it guards.</summary>
    [Fact]
    public void TheAssumption_ShouldMapBackToItsConstraint() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var variable = new BinVar("analysis-assumption-0");
        var variableId = new VariableId("assume:0");
        var literal = new BooleanLiteral.VariableLiteral(variable, negated: false, id: variableId);
        var askedId = new ConstraintId("c:asked");
        var literals = new Dictionary<ConstraintId, BooleanLiteral> { [askedId] = literal };

        var conflict = new ConstraintProgrammingConflict(assumptions: [literal]);
        var requested = new List<DiagnosticSource> { new DiagnosticSource.Constraint(askedId) };

        ConflictEvidenceSupport.RemapBackendConflict(snapshot, conflict, requested, literals)
            .Should().ContainSingle("the assumption belongs to the constraint the caller asked about");
    }

    /// <summary>**没有冲突能力时 `Members` 是全集，不是空集** / Without conflict support the members are the whole evidence set.</summary>
    /// <remarks>
    /// 空集合看起来更"诚实"，**但它会让下游按"最小冲突为空"去读，而真相是"这次分析根本没做"。**
    ///
    /// An empty set looks more honest, **but it makes downstream read "the minimal conflict is empty", while the truth is "this analysis never ran".**
    /// </remarks>
    [Fact]
    public void TheUnsupportedExplanation_ShouldCarryTheWholeEvidence() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();

        ConflictExplanation explanation = ConflictEvidenceSupport.UnsupportedExplanation(
            snapshot, ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0)),
            new ConflictExtractionTier());

        explanation.Status.Should().Be(AnalysisStatus.Unsupported);
        explanation.Validity.Should().Be(ConflictValidity.Unknown, "nothing was proven");
        explanation.Minimality.Should().Be(ConflictMinimality.NotChecked, "no deletion check ran");
        explanation.Members.Should().NotBeEmpty("the evidence exists, it was simply never proven to block");
        explanation.Members.Should().BeEquivalentTo(explanation.AvailableEvidence,
            "with no basis to exclude any source, the members are the whole universe");
    }

    /// <summary>**目标没有被固定住** / The target was not fixed.</summary>
    /// <remarks>
    /// 正常路径固定目标后才谈冲突。写成 `true` 会让下游以为做过那一步。
    ///
    /// The normal path fixes the target before talking about conflicts. Writing `true` would let downstream believe that step happened.
    /// </remarks>
    [Fact]
    public void TheUnsupportedExplanation_ShouldNotClaimAFixedTarget() {
        ConflictExplanation explanation = ConflictEvidenceSupport.UnsupportedExplanation(
            Snapshot(), ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0)),
            new ConflictExtractionTier());

        explanation.TargetFeasibility.TargetFixed.Should().BeFalse(
            "the target was never fixed for a conflict analysis that cannot run");
        explanation.Message.Should().Contain("未声明 conflict", "the message names the missing capability");
    }
}
