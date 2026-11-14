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
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 未知输出里的见证解 / The witness inside an unknown output.
/// </summary>
/// <remarks>
/// **未知输出可以带回一个候选解**，校验通过就提升成 `Reachable`。
/// **两个方向都不能松：通过就提升，不通过就丢弃。**
///
/// **An unknown output may carry a candidate**, promoted to `Reachable` when it validates. **Neither direction may be loose: promote when it passes, drop when it does not.**
/// </remarks>
public class UnknownOutputWitnessTests {
    private const string X = "source:x";

    private static ConstraintProgrammingModelSnapshot Build(int upper) {
        using var model = new ConstraintProgrammingModel(name: "unknown", objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        var id = new VariableId(X);
        IntegerDomain domain = IntegerDomain.CreateInterval(Int64.Zero, new Int64(upper)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, id);

        model.RegisterVariable(id: id, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull();
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        ConstraintProgrammingConstraint.IntegerComparison constraint =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, Int64.Zero).Value;
        model.AddConstraint(constraint, "constraint:x").Value.Should().NotBeNull();
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();

        return model.Snapshot().Value;
    }

    private static ObjectiveTarget AtLeast(double value) =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(value));

    /// <summary>**校验通过就提升成 `Reachable`** / A validating candidate is promoted to Reachable.</summary>
    /// <remarks>
    /// **这是"未知"里唯一能变成"可达"的路。** 不给这条路，一个其实已经找到解的后端
    /// **只能一直报未知。**
    ///
    /// **This is the only path from "unknown" to "reachable".** Without it, a backend that did find a solution **can only keep reporting unknown.**
    /// </remarks>
    [Fact]
    public void TheValidatingCandidate_ShouldBePromoted() {
        UnknownWitnessResult result = UnknownOutputWitness.Promote(
            Build(upper: 10), AtLeast(2.0), new Int64(2), [new Int64(5)]);

        result.Status.Should().Be(AnalysisStatus.Reachable, "x = 5 satisfies x ≥ 2 and stays in [0, 10]");
        result.Solution.Should().NotBeNull("the promoted result keeps the witness that earned it");
        result.ExactObjective!.Value.ToLong().Should().Be(5);
    }

    /// <summary>**校验不过就丢弃解，不只是降级状态** / A failing candidate is dropped, not just downgraded.</summary>
    /// <remarks>
    /// **状态降回 `Unknown` 还不够。** 一份没通过校验的解留在报告里，
    /// **下游会读成"虽然未知但总归是个解"。**
    ///
    /// **Downgrading the status is not enough.** A solution that failed validation left in the report **gets read downstream as "unknown, but at least it is a solution".**
    /// </remarks>
    [Fact]
    public void TheFailingCandidate_ShouldBeDroppedEntirely() {
        UnknownWitnessResult result = UnknownOutputWitness.Promote(
            Build(upper: 10), AtLeast(9.0), new Int64(9), [new Int64(5)]);

        result.Status.Should().Be(AnalysisStatus.Unknown, "x = 5 does not satisfy x ≥ 9");
        result.Solution.Should().BeNull("a witness that failed validation is not a witness");
        result.ExactObjective.Should().BeNull("and it has no objective value either");
        result.Promoted.Should().BeFalse();
    }

    /// <summary>**没有候选时不提升** / No candidate means no promotion.</summary>
    /// <remarks>
    /// **反向对照**：只测上面两条会让"没有候选也报可达"通过前一条之外的部分。
    ///
    /// **The reverse control**: the two cases above let "promote even with no candidate" pass all but one.
    /// </remarks>
    [Fact]
    public void TheMissingCandidate_ShouldStayUnknown() {
        UnknownWitnessResult result = UnknownOutputWitness.Promote(
            Build(upper: 10), AtLeast(2.0), new Int64(2), values: null);

        result.Status.Should().Be(AnalysisStatus.Unknown);
        result.Solution.Should().BeNull();
    }

    /// <summary>**短列表少映射几个变量，不抛异常** / A short list maps fewer variables and throws nothing.</summary>
    /// <remarks>
    /// **位置对不上是要报告的结果，不是要崩溃的瞬间。** 少映射的变量在后续校验里会报"越界/未知"，
    /// **那条消息指的方向是对的。**
    ///
    /// **A positional mismatch is a result to report, not a moment to crash.** The unmapped variable surfaces later as an out-of-domain/unknown complaint, **which points in the right direction.**
    /// </remarks>
    [Fact]
    public void TheShortValueList_ShouldMapWhatItCanWithoutThrowing() {
        System.Action call = () => UnknownOutputWitness.MapPositional(Build(upper: 10), []);
        call.Should().NotThrow("an empty list is a missing candidate, not an error");

        UnknownOutputWitness.MapPositional(Build(upper: 10), []).Should().BeNull(
            "an empty list maps nothing, so there is no solution to return");
    }

    /// <summary>**映射用的是位置，变量顺序变了结果就变** / The mapping is positional, so order matters.</summary>
    /// <remarks>
    /// **这条判据把一个隐患写明了**：求解器返回的列表没有变量名，
    /// **换一个变量顺序的求解器，每个值都会挂到别的变量上。**
    /// 这里钉的不是"能处理"，而是**"它是位置映射"这个事实本身**。
    ///
    /// **This criterion states a hazard plainly**: the list has no variable names, **so a solver that orders variables differently attaches every value to the wrong variable.** This pins the fact of positional
    /// mapping, not a handling of it.
    /// </remarks>
    [Fact]
    public void ThePositionalMapping_ShouldFollowVariableOrder() {
        ConstraintProgrammingSolution? mapped = UnknownOutputWitness.MapPositional(Build(upper: 10), [new Int64(7)]);

        mapped.Should().NotBeNull();
        mapped!.Values.Should().ContainKey(new VariableId(X), "the first value goes to the first variable");
        mapped.Values[new VariableId(X)].ToLong().Should().Be(7);
    }
}
