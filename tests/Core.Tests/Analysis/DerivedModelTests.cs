#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Linq;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 带目标的派生重建 / Rebuilding with a target.
/// </summary>
/// <remarks>
/// 这是 `build(snapshot, target)` 的实现：重建模型，再追加一条目标约束。
///
/// The implementation of `build(snapshot, target)`: rebuild the model, then append a target constraint.
/// </remarks>
public class DerivedModelTests {
    private static ConstraintProgrammingModel BuildModel() {
        var model = new ConstraintProgrammingModel("fixture", ObjectCategory.Maximum, identityNamespace: "fixture");
        var y = new IntVar("y");
        var yId = new VariableId("source:y");
        IntegerDomain yDomain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: yId, variable: y, domain: yDomain, scope: ModelElementScope.Stable, origin: "fixture/y")
            .Value.Should().NotBeNull();
        var yExpression = new ConstraintProgrammingExpression.Variable(y, yDomain, yId);
        model.RegisterExpression("y", yExpression).Value.Should().NotBeNull();
        model.Maximize(expression: yExpression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        return model;
    }

    /// <summary>**目标约束必须真的被加上** / The target constraint has to be really added.</summary>
    [Fact]
    public void TheDerivedModel_ShouldCarryTheTargetConstraint() {
        ConstraintProgrammingModelSnapshot snapshot = BuildModel().Snapshot().Value!;
        ObjectiveTarget target = ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(7.0));

        Result<DerivedModel, ErrorCode, Error<ErrorCode>> derived =
            SnapshotModelRebuilder.RebuildWithTarget(snapshot, target);
        derived.IsOk.Should().BeTrue(derived.ToString());

        ConstraintProgrammingModelSnapshot again = derived.Value!.Model.Snapshot().Value!;
        again.Constraints.Select(entry => entry.Id.Value)
            .Should().Contain(derived.Value.TargetConstraintId.Value,
                "a derived model without the target constraint answers a different question");
        derived.Value.TargetConstraintId.Value.Should().StartWith("analysis-target:",
            "the target constraint stays identifiable, so a report can name it");
        again.Objectives.Should().ContainSingle("the derived model belongs to one objective");
    }

    /// <summary>
    /// **取整方向不能反：`>=` 向上、`<=` 向下**
    /// </summary>
    /// <remarks>
    /// 取错方向等于把门槛放低一格，而报告照样给出一个像样的结论。
    /// 6.5 在 `>=` 下必须变成 7、在 `<=` 下必须变成 6；两条都要查，只查一条会让"永远向上取整"通过。
    ///
    /// The wrong direction lowers the bar by one unit, and the report still looks conclusive. 6.5 has to become 7 under &gt;= and 6 under &lt;=; checking only one would let an
    /// always-round-up implementation pass.
    /// </remarks>
    [Fact]
    public void TheTargetBound_ShouldRoundByDirection() {
        ConstraintProgrammingModelSnapshot snapshot = BuildModel().Snapshot().Value!;

        DerivedModel atLeast = SnapshotModelRebuilder.RebuildWithTarget(
            snapshot, ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(6.5))).Value!;
        DerivedModel atMost = SnapshotModelRebuilder.RebuildWithTarget(
            snapshot, ObjectiveTarget.CreateAtMost(new ObjectiveId("objective:main"), new Flt64(6.5))).Value!;

        string atLeastText = Text(atLeast);
        string atMostText = Text(atMost);

        atLeastText.Should().NotBe(atMostText,
            "6.5 has to round to 7 under >= and 6 under <=; identical text means one direction rounded the wrong way");
        atLeastText.Should().Contain("7", "rounding up takes 6.5 to 7");
        atMostText.Should().Contain("6", "rounding down takes 6.5 to 6");
    }

    /// <summary>**目标不存在时报 `DataNotFound`** / A missing objective is `DataNotFound`.</summary>
    [Fact]
    public void TheMissingObjective_ShouldBeDataNotFound() {
        ConstraintProgrammingModelSnapshot snapshot = BuildModel().Snapshot().Value!;
        Result<DerivedModel, ErrorCode, Error<ErrorCode>> derived = SnapshotModelRebuilder.RebuildWithTarget(
            snapshot, ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:absent"), new Flt64(1.0)));

        derived.IsOk.Should().BeFalse("there is no objective to constrain");
        derived.ToString().Should().Contain("objective:absent",
            "the error names the objective it could not find, or the caller cannot fix it");
    }

    private static string Text(DerivedModel derived) => string.Join("|", derived.Model.Snapshot().Value!.Constraints
        .Select(entry => entry.Constraint.ToString()));
}
