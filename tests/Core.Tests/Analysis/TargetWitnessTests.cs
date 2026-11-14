#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 见证解的自校验 / Self-validation of a witness solution.
/// </summary>
/// <remarks>
/// **求解器说"我找到了一个解"。这个解可能不属于这个模型、可能越界、可能违反原始约束、
/// 也可能根本不满足目标。任何一条不查，报出去的 `Reachable` 就是一个没验过的断言。**
///
/// **The solver says "I found a solution". It may belong to another model, be out of domain, violate an original constraint, or simply not meet the target. Skip any one, and the `Reachable` you report is
/// an assertion nobody verified.**
/// </remarks>
public class TargetWitnessTests {
    private const string X = "source:x";

    private static ConstraintProgrammingModelSnapshot Build(int lower, int upper, bool atLeast) {
        // **目标方向必须与模型方向一致**，否则 `Snapshot()` 直接返回 null。
        // **The objective direction has to match the model direction**, or `Snapshot()` returns null.
        ObjectCategory category = atLeast ? ObjectCategory.Minimum : ObjectCategory.Maximum;
        using var model = new ConstraintProgrammingModel(name: "witness", objectCategory: category);
        var variable = new IntVar("x");
        var id = new VariableId(X);
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(lower), new Int64(upper)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, id);

        model.RegisterVariable(id: id, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull();
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        // **x >= lower 这条约束让"违反原始约束"可以被构造出来。**
        // **The `x >= lower` constraint is what makes "violates an original constraint" constructible.**
        ConstraintProgrammingConstraint.IntegerComparison constraint =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, new Int64(lower)).Value;
        model.AddConstraint(constraint, "constraint:x").Value.Should().NotBeNull();

        if (atLeast) {
            model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        }
        else {
            model.Maximize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        }

        return model.Snapshot().Value;
    }

    private static ConstraintProgrammingSolution Solution(long value) =>
        new(new Dictionary<VariableId, Int64> { [new VariableId(X)] = new Int64(value) });

    private static ObjectiveTarget AtLeast(double value) =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(value));

    private static ObjectiveTarget AtMost(double value) =>
        ObjectiveTarget.CreateAtMost(new ObjectiveId("objective:main"), new Flt64(value));

    /// <summary>**满足目标的解通过** / A solution that meets the target passes.</summary>
    [Fact]
    public void TheSatisfyingWitness_ShouldPass() {
        TargetWitness.Validate(Build(0, 10, atLeast: true), AtLeast(2.0), new Int64(2), Solution(5))
            .Should().BeNull("x = 5 satisfies x ≥ 2");
    }

    /// <summary>**`AtLeast` 不够大要拒绝** / AtLeast rejects a value that is too small.</summary>
    /// <remarks>
    /// **这一条就是这个类存在的理由**：求解器报了一个"可行解"，
    /// **而它并不满足目标——不查就会把不可达标报成可达标。**
    ///
    /// **This is why this class exists**: the solver reported a "feasible solution" **that does not meet the target — unchecked, an unreachable target is reported as reachable.**
    /// </remarks>
    [Fact]
    public void TheAtLeastWitness_ShouldBeRejectedWhenTooSmall() {
        TargetWitness.Validate(Build(0, 10, atLeast: true), AtLeast(2.0), new Int64(2), Solution(1))
            .Should().Contain("目标边界", "x = 1 does not satisfy x ≥ 2");
    }

    /// <summary>**`AtMost` 太大要拒绝** / AtMost rejects a value that is too large.</summary>
    /// <remarks>
    /// **反向对照**：只测 `AtLeast` 会让"两个方向都写成 `>=`"通过上一条，
    /// **而那样上界目标就永远不会被拒绝。**
    ///
    /// **The reverse control**: testing only `AtLeast` lets "use `>=` for both" pass the case above, **and then an upper-bound target is never rejected.**
    /// </remarks>
    [Fact]
    public void TheAtMostWitness_ShouldBeRejectedWhenTooLarge() {
        TargetWitness.Validate(Build(0, 10, atLeast: false), AtMost(2.0), new Int64(2), Solution(5))
            .Should().Contain("目标边界", "x = 5 does not satisfy x ≤ 2");

        TargetWitness.Validate(Build(0, 10, atLeast: false), AtMost(2.0), new Int64(2), Solution(1))
            .Should().BeNull("x = 1 does satisfy x ≤ 2");
    }

    /// <summary>**未知变量要拒绝** / An unknown variable is rejected.</summary>
    /// <remarks>
    /// **解可能来自另一个模型。** 这条排在最前面，因为后面每条检查都会因为它而误判。
    ///
    /// **The solution may belong to another model.** This runs first because every later check would misjudge because of it.
    /// </remarks>
    [Fact]
    public void TheUnknownVariable_ShouldBeRejected() {
        var foreign = new ConstraintProgrammingSolution(
            new Dictionary<VariableId, Int64> { [new VariableId("source:other")] = new Int64(1) });

        TargetWitness.Validate(Build(0, 10, atLeast: true), AtLeast(2.0), new Int64(2), foreign)
            .Should().Contain("未知变量", "a value for a variable this model does not have is not a witness");
    }

    /// <summary>**越界要拒绝** / An out-of-domain value is rejected.</summary>
    [Fact]
    public void TheOutOfDomainValue_ShouldBeRejected() {
        TargetWitness.Validate(Build(0, 3, atLeast: true), AtLeast(2.0), new Int64(2), Solution(9))
            .Should().Contain("值域", "x = 9 is outside [0, 3] even though it satisfies x ≥ 2");
    }

    /// <summary>**违反原始约束的解要拒绝** / A solution that violates an original constraint is rejected.</summary>
    /// <remarks>
    /// **值域合法不等于约束合法。** 值域 [0, 10] 里的 x = 7 完全合法，
    /// **但如果模型有一条 `x ≤ 4`，那它就不是这个模型的解。**
    /// **查值域不查约束的实现会把它当成见证。**
    ///
    /// **Being in domain is not being feasible.** x = 7 is perfectly legal in [0, 10], **but with an `x ≤ 4` in the model it is not a solution of this model.** **An implementation that checks the domain but not the constraints accepts
    /// it as a witness.**
    /// </remarks>
    [Fact]
    public void TheConstraintViolatingWitness_ShouldBeRejected() {
        TargetWitness.Validate(BuildWithUpperConstraint(), AtLeast(2.0), new Int64(2), Solution(7))
            .Should().Contain("违反原始约束",
                "x = 7 is in domain but breaks x ≤ 4, so it is not a witness for anything");
    }

    /// <summary>一个值域比约束更宽的模型：`x ∈ [0, 10]` 且 `x ≤ 4` / A model where the domain is wider than the constraint.</summary>
    private static ConstraintProgrammingModelSnapshot BuildWithUpperConstraint() {
        using var model = new ConstraintProgrammingModel(
            name: "witness-narrow", objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        var id = new VariableId(X);
        IntegerDomain domain = IntegerDomain.CreateInterval(Int64.Zero, new Int64(10)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, id);

        model.RegisterVariable(id: id, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull();
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        ConstraintProgrammingConstraint.IntegerComparison constraint =
            ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(4)).Value;
        model.AddConstraint(constraint, "constraint:narrow").Value.Should().NotBeNull();
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();

        return model.Snapshot().Value;
    }
}
