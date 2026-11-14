#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Linq;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// RHS 扰动派生 / Deriving a perturbed RHS.
/// </summary>
/// <remarks>
/// 移植自 `deriveRhsSnapshot`。**三种"不能扰动"都显式返回原因，不给一份没改过的快照。**
///
/// Ported from `deriveRhsSnapshot`. **All three "cannot perturb" cases return a reason instead of an unchanged snapshot.**
/// </remarks>
public class RhsPerturbationTests {
    private static ConstraintProgrammingModelSnapshot Fixture(out ConstraintId constraintId) {
        var model = new ConstraintProgrammingModel("perturbation", ObjectCategory.Maximum, identityNamespace: "perturbation");
        var y = new IntVar("y");
        var yId = new VariableId("source:y");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(100)).Value;
        model.RegisterVariable(id: yId, variable: y, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/y")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(y, domain, yId);
        model.RegisterExpression("y", expression).Value.Should().NotBeNull();
        constraintId = new ConstraintId("constraint:cap");
        model.AddConstraint(
                ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(8)).Value,
                id: constraintId, name: "cap")
            .Value.Should().NotBeNull();
        model.Maximize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        return model.Snapshot().Value!;
    }

    /// <summary>**派生只改一条约束，别的都一样** / The derivation changes one constraint and nothing else.</summary>
    [Fact]
    public void TheDerivation_ShouldChangeOnlyThatRhs() {
        ConstraintProgrammingModelSnapshot snapshot = Fixture(out ConstraintId id);
        RhsPerturbationResult result = ConstraintPerturbationSupport.DeriveRhsSnapshot(
            snapshot, id, new Flt64(2.0));

        result.Should().BeOfType<RhsPerturbationReady>();
        var ready = (RhsPerturbationReady)result;
        ready.NewRhs.ToDouble().Should().Be(10.0, "8 + 2 = 10");

        ConstraintProgrammingModelSnapshot derived = ready.Snapshot;
        derived.Constraints.Single(entry => entry.Id == id).Constraint
            .Should().BeOfType<ConstraintProgrammingConstraint.IntegerComparison>();
        ((ConstraintProgrammingConstraint.IntegerComparison)derived.Constraints
                .Single(entry => entry.Id == id).Constraint).Rhs.ToFlt64().ToDouble()
            .Should().Be(10.0);

        // 其余结构必须原样。
        derived.Variables.Select(entry => entry.Id.Value).Should()
            .Equal(snapshot.Variables.Select(entry => entry.Id.Value));
        derived.Expressions.Select(entry => entry.Name).Should()
            .Equal(snapshot.Expressions.Select(entry => entry.Name));
        derived.Objectives.Select(entry => entry.Id.Value).Should()
            .Equal(snapshot.Objectives.Select(entry => entry.Id.Value));

        // **原快照不能被改**：它是别的分析的基线。
        ((ConstraintProgrammingConstraint.IntegerComparison)snapshot.Constraints
                .Single(entry => entry.Id == id).Constraint).Rhs.ToFlt64().ToDouble()
            .Should().Be(8.0, "the source snapshot is the baseline for every other analysis and must not move");
    }

    /// <summary>**非整数 delta 必须被拒，不许悄悄取整** / A fractional delta is refused, never rounded.</summary>
    /// <remarks>
    /// 悄悄取整会让"扰动了 0.5"和"扰动了 0"给出同一份模型，**而报告里写的是 0.5。**
    ///
    /// Rounding silently would make "perturbed by 0.5" and "perturbed by 0" produce the same model, **while the report says 0.5.**
    /// </remarks>
    [Fact]
    public void TheFractionalDelta_ShouldBeRefused() {
        ConstraintProgrammingModelSnapshot snapshot = Fixture(out ConstraintId id);

        ConstraintPerturbationSupport.DeriveRhsSnapshot(snapshot, id, new Flt64(0.5))
            .Should().BeOfType<RhsPerturbationUnsupported>("0.5 cannot be expressed as an Int64 RHS delta");
        ConstraintPerturbationSupport.DeriveRhsSnapshot(snapshot, id, new Flt64(double.NaN))
            .Should().BeOfType<RhsPerturbationUnsupported>();
    }

    /// <summary>**约束不存在要被拒，不给原快照** / A missing constraint is refused, not the original snapshot.</summary>
    /// <remarks>
    /// 把"没改"当成"改好了"，会让后续求解解的是一份没被扰动的模型，**而报告会照常写上一个改善量。**
    ///
    /// Treating "nothing changed" as "changed" would solve an unperturbed model, **and the report would still print an improvement number.**
    /// </remarks>
    [Fact]
    public void TheMissingConstraint_ShouldBeRefused() {
        ConstraintProgrammingModelSnapshot snapshot = Fixture(out _);
        RhsPerturbationResult result = ConstraintPerturbationSupport.DeriveRhsSnapshot(
            snapshot, new ConstraintId("constraint:absent"), new Flt64(1.0));

        result.Should().BeOfType<RhsPerturbationUnsupported>();
        ((RhsPerturbationUnsupported)result).Message.Should().Contain("不存在");
    }

    /// <summary>**溢出要被拒，不许静默回绕** / Overflow is refused, not silently wrapped.</summary>
    /// <remarks>
    /// C# 的 `long` 溢出默认静默回绕：**扰动一下 RHS 变成负数，而没有任何一处报错。**
    ///
    /// `long` overflow wraps silently in C# by default: **a perturbed RHS turns negative with nothing reporting it.**
    /// </remarks>
    [Fact]
    public void TheOverflow_ShouldBeRefused() {
        ConstraintProgrammingModelSnapshot snapshot = Fixture(out ConstraintId id);
        ConstraintPerturbationSupport.DeriveRhsSnapshot(snapshot, id, new Flt64(9.3e18))
            .Should().BeOfType<RhsPerturbationUnsupported>("the sum would leave the Int64 range");
    }
}
