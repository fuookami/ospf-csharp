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
/// 目标见证解的复核 / Re-checking a target witness.
/// </summary>
/// <remarks>
/// 求解器说"可行"不等于可行。五项检查每一条都要有一个反例——**少一条反例，那条检查就可能从来没生效过。**
///
/// A solver saying "feasible" is not feasibility. Each of the five checks needs a counterexample — **without one, that check may never have run at all.**
/// </remarks>
public class TargetWitnessValidatorTests {
    private const string ModelName = "witness-fixture";

    private static (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveId Objective) BuildFixture() {
        var model = new ConstraintProgrammingModel(ModelName, ObjectCategory.Maximum, identityNamespace: ModelName);
        var y = new IntVar("y");
        var yId = new VariableId("source:y");
        IntegerDomain yDomain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: yId, variable: y, domain: yDomain, scope: ModelElementScope.Stable, origin: "fixture/y")
            .Value.Should().NotBeNull();

        var yExpression = new ConstraintProgrammingExpression.Variable(y, yDomain, yId);
        model.RegisterExpression("y", yExpression).Value.Should().NotBeNull();
        model.AddConstraint(
                ConstraintProgrammingConstraint.CreateLessOrEqual(yExpression, new Int64(8)).Value,
                id: new ConstraintId("constraint:cap"), name: "cap")
            .Value.Should().NotBeNull();
        var objective = new ObjectiveId("objective:main");
        model.Maximize(expression: yExpression, id: objective).Value.Should().NotBeNull();
        return (model.Snapshot().Value!, objective);
    }

    private static ConstraintProgrammingSolution Solution(params (string Id, long Value)[] values) =>
        new(values.ToDictionary(
            entry => new VariableId(entry.Id),
            entry => new Int64(entry.Value)));

    private static string? Check(
        (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveId Objective) fixture,
        ObjectiveTarget target,
        Int64 bound,
        ConstraintProgrammingSolution solution) =>
        TargetWitnessValidator.Validate(fixture.Snapshot, target, bound, solution);

    /// <summary>**合格的见证解要通过** / A valid witness passes.</summary>
    /// <remarks>
    /// 这条是其余反例的对照：**没有它，"永远返回错误"也能通过所有反例。**
    ///
    /// This is the control for the counterexamples: **without it, "always returns an error" would pass them all.**
    /// </remarks>
    [Fact]
    public void TheValidWitness_ShouldPass() {
        (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveId Objective) fixture = BuildFixture();
        ObjectiveTarget target = ObjectiveTarget.CreateAtLeast(fixture.Objective, new Flt64(7.0));

        Check(fixture, target, new Int64(7), Solution(("source:y", 7))).Should().BeNull();
        Check(fixture, target, new Int64(7), Solution(("source:y", 8))).Should().BeNull(
            "8 is above the bound and inside the domain");
    }

    /// <summary>**解里出现未知变量要被拒** / An unknown variable is refused.</summary>
    [Fact]
    public void TheUnknownVariable_ShouldBeRefused() {
        (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveId Objective) fixture = BuildFixture();
        ObjectiveTarget target = ObjectiveTarget.CreateAtLeast(fixture.Objective, new Flt64(7.0));

        Check(fixture, target, new Int64(7), Solution(("source:y", 7), ("source:ghost", 1)))
            .Should().Contain("source:ghost", "the reason has to name the variable, or nobody can fix the caller");
    }

    /// <summary>**值落在值域之外要被拒** / A value outside its domain is refused.</summary>
    /// <remarks>
    /// 这条和"违反约束"不是一回事：**值域是求解器自己该守的契约，越界说明返回的东西不可信。**
    ///
    /// This is not the same as a violated constraint: **the domain is the solver's own contract, and stepping outside it means the result cannot be trusted.**
    /// </remarks>
    [Fact]
    public void TheOutOfDomainValue_ShouldBeRefused() {
        (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveId Objective) fixture = BuildFixture();
        ObjectiveTarget target = ObjectiveTarget.CreateAtLeast(fixture.Objective, new Flt64(7.0));

        Check(fixture, target, new Int64(7), Solution(("source:y", 11)))
            .Should().Contain("值域", "11 is outside [0, 10] even though it satisfies the target");
    }

    /// <summary>**违反原始约束要被拒** / A violated original constraint is refused.</summary>
    /// <remarks>
    /// **这一条最容易漏**：解满足目标、也在值域内，**但它踩了模型自己的那条约束。**
    /// 只看目标就报"可行"，会把一个不可行的模型报成可行。
    ///
    /// **This is the easiest one to miss**: the solution meets the target and stays inside the domain, **but it violates a constraint of the model itself.** Checking only the
    /// target would report an infeasible model as feasible.
    /// </remarks>
    [Fact]
    public void TheViolatedConstraint_ShouldBeRefused() {
        (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveId Objective) fixture = BuildFixture();
        ObjectiveTarget target = ObjectiveTarget.CreateAtLeast(fixture.Objective, new Flt64(9.0));

        Check(fixture, target, new Int64(9), Solution(("source:y", 9)))
            .Should().Contain("违反原始约束", "y = 9 meets the bound and breaks y <= 8");
    }

    /// <summary>**没到目标边界要被拒** / A witness short of the bound is refused.</summary>
    [Fact]
    public void TheShortWitness_ShouldBeRefused() {
        (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveId Objective) fixture = BuildFixture();
        ObjectiveTarget target = ObjectiveTarget.CreateAtLeast(fixture.Objective, new Flt64(7.0));

        Check(fixture, target, new Int64(7), Solution(("source:y", 6)))
            .Should().Contain("目标边界", "6 does not reach the bound of 7");
    }

    /// <summary>**`AtMost` 的方向不能反** / The `AtMost` direction cannot be flipped.</summary>
    /// <remarks>
    /// 只测 `AtLeast` 会让"永远 `&gt;=`"的实现通过。
    /// **同一个解（y = 7，满足 `y &lt;= 8`）在两种方向下都必须被判为没到边界，只是差在另一侧。**
    ///
    /// Testing only `AtLeast` would let an always-`&gt;=` implementation pass.
    /// **The same solution (y = 7, satisfying `y &lt;= 8`) has to miss the bound on either side, depending on the direction.**
    /// </remarks>
    [Fact]
    public void TheAtMostDirection_ShouldBeRefusedSeparately() {
        (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveId Objective) fixture = BuildFixture();
        ConstraintProgrammingSolution solution = Solution(("source:y", 7));

        ObjectiveTarget atMost = ObjectiveTarget.CreateAtMost(fixture.Objective, new Flt64(5.0));
        ObjectiveTarget atLeast = ObjectiveTarget.CreateAtLeast(fixture.Objective, new Flt64(8.0));

        Check(fixture, atMost, new Int64(5), solution).Should().Contain("目标边界",
            "7 exceeds an AtMost bound of 5, and it stays inside y <= 8 so the constraint check passes");
        Check(fixture, atLeast, new Int64(8), solution).Should().Contain("目标边界",
            "7 falls short of an AtLeast bound of 8; the same solution fails on the other side");
    }

    /// <summary>**约束检查在目标检查之前** / Constraints are checked before the target.</summary>
    /// <remarks>
    /// `y = 9` 违反 `y &lt;= 8` 的同时满足 `&gt;= 9`；**报出来的必须是约束那条**，
    /// 否则一个踩了约束的解会被写成"只是没到目标"。
    ///
    /// `y = 9` breaks `y &lt;= 8` while meeting `&gt;= 9`; **the constraint has to be the one reported**, or a solution that stepped on a constraint would read as "just short of
    /// the target".
    /// </remarks>
    [Fact]
    public void TheConstraintCheck_ShouldComeBeforeTheTarget() {
        (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveId Objective) fixture = BuildFixture();
        ObjectiveTarget atLeast = ObjectiveTarget.CreateAtLeast(fixture.Objective, new Flt64(9.0));

        Check(fixture, atLeast, new Int64(9), Solution(("source:y", 9)))
            .Should().Contain("违反原始约束", "y = 9 meets the bound and breaks y <= 8; the constraint is the real reason");
    }

    /// <summary>**目标不存在要被拒** / A missing objective is refused.</summary>
    [Fact]
    public void TheMissingObjective_ShouldBeRefused() {
        (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveId Objective) fixture = BuildFixture();
        ObjectiveTarget target = ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:absent"), new Flt64(1.0));

        Check(fixture, target, new Int64(1), Solution(("source:y", 5)))
            .Should().Contain("未注册", "there is no objective to evaluate");
    }
}
