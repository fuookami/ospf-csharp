#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 模型自己的 `Elastic` 必须真的产出弹性模型，而不只是存在。
/// The model's own `Elastic` must actually produce an elastic model, not merely exist.
/// </summary>
/// <remarks>
/// **`buildElasticModel` 是扩展函数，由 `QuadraticTetradModel.elastic` 调用。**
/// C# 侧原先只有静态的 `QuadraticTetradElasticBuilder.BuildElasticModel(model, penalty)`，
/// **模型自己没有 `Elastic`**——个公开入口在 C# 里根本不存在。
///
/// 补上了入口。**这里的判据要能区分"补了入口"与"补了个空壳"**：
/// 只断言"返回非空模型"是不够的——返回 `this` 也能通过。所以判据必须落在
/// **弹性模型特有的痕迹**上：约束数与变量数都因松弛列而增加。
///
/// **In the implementation `buildElasticModel` is an extension function called by `QuadraticTetradModel.elastic`.** The C# side originally had only the static
/// `QuadraticTetradElasticBuilder.BuildElasticModel(model, penalty)` and **the model had no `Elastic`** — its public entry point did not exist in C#.
///
/// adds the entry point. **The criterion must distinguish "entry added" from "entry added as a shell"**: asserting only "returns a non-null model" is not
/// enough — returning `this` would also pass. So the criteria land on **traces unique to an elastic model**: both the constraint and the variable count grow because
/// of slack columns.
/// </remarks>
public class QuadraticTetradModelElasticEntryTests {
    private static ModelViewVariable Var(int index, string name, double lower, double upper) =>
        new(
            index: index,
            lowerBound: new Flt64(lower),
            upperBound: new Flt64(upper),
            type: Continuous.Instance,
            name: name,
            initialResult: Flt64.Zero
        );

    /// <summary>构造两个变量、两条约束的最小二次模型 / A minimal quadratic model with two variables and two constraints.</summary>
    /// <param name="sign">第一条约束的关系符号 / Relation of the first constraint.</param>
    /// <returns>模型 / The model.</returns>
    private static QuadraticTetradModel MakeModel(ConstraintRelation sign) {
        var variables = new List<ModelViewVariable> {
            Var(0, "x", 0.0, 10.0),
            Var(1, "y", 0.0, 10.0),
        };
        var row0 = new SparseQuadraticVector();
        row0.Add(0, 1, new Flt64(2.0));
        var row1 = new SparseQuadraticVector();
        row1.Add(0, -1, new Flt64(3.0));
        var lhs = new SparseQuadraticMatrix(new[] { row0, row1 });
        var objectiveCells = new List<QuadraticObjectiveCell> {
            new(0, -1, new Flt64(1.0)),
            new(1, -1, new Flt64(1.0)),
        };
        return new QuadraticTetradModel(
            variables,
            new QuadraticConstraintBatch(
                lhs,
                new[] { sign, ConstraintRelation.LessEqual },
                new[] { new Flt64(5.0), new Flt64(4.0) },
                new[] { "row0", "row1" },
                new[] { ConstraintSource.Origin, ConstraintSource.Origin }),
            QuadraticObjective.Create(ObjectCategory.Minimum, objectiveCells),
            "quadratic-elastic-entry-test");
    }

    /// <summary>**`Elastic` 必须增加约束与变量** / `Elastic` must add constraints and variables.</summary>
    /// <remarks>
    /// 若 `Elastic` 只是 `return this`，约束数与变量数都**不变**，这条会红。
    ///
    /// If `Elastic` merely returned `this`, both counts would stay **unchanged** and this goes red.
    /// </remarks>
    [Fact]
    public void Elastic_ShouldAddSlackConstraintsAndVariables() {
        QuadraticTetradModel model = MakeModel(ConstraintRelation.LessEqual);

        int constraintCount = model.Constraints.Size;
        int variableCount = model.Variables.Count;

        QuadraticTetradModel elastic = model.Elastic();

        elastic.Constraints.Size.Should().BeGreaterThan(constraintCount, "elasticity adds slack rows");
        elastic.Variables.Count.Should().BeGreaterThan(variableCount, "elasticity adds slack columns");
    }

    /// <summary>**`Elastic` 不得返回原模型** / `Elastic` must not return the original model.</summary>
    [Fact]
    public void Elastic_ShouldReturnADistinctModel() {
        QuadraticTetradModel model = MakeModel(ConstraintRelation.LessEqual);

        QuadraticTetradModel elastic = model.Elastic();

        elastic.Should().NotBeSameAs(model);
    }

    /// <summary>**`Elastic` 必须与静态构建器等价** / `Elastic` must agree with the static builder.</summary>
    /// <remarks>
    /// 这条把入口与构建器绑在一起：若 `Elastic` 用了别的惩罚或别的路径，约束/变量数会与
    /// `BuildElasticModel(this, Flt64.One)` 不一致。
    ///
    /// This ties the entry point to the builder: if `Elastic` used another penalty or another path, the counts would differ from
    /// `BuildElasticModel(this, Flt64.One)`.
    /// </remarks>
    [Fact]
    public void Elastic_ShouldMatchTheStaticBuilderWithUnitPenalty() {
        QuadraticTetradModel model = MakeModel(ConstraintRelation.GreaterEqual);

        QuadraticTetradModel viaEntry = model.Elastic();
        QuadraticTetradModel viaBuilder = QuadraticTetradElasticBuilder.BuildElasticModel(model, Flt64.One);

        viaEntry.Constraints.Size.Should().Be(viaBuilder.Constraints.Size);
        viaEntry.Variables.Count.Should().Be(viaBuilder.Variables.Count);
    }

    /// <summary>**`Elastic` 的松弛惩罚必须恰好是 `1.0`** / `Elastic`'s slack penalty must be exactly `1.0`.</summary>
    /// <remarks>
    /// **这条是这里补上的，原因是上一条变异存活。**
    ///
    /// `Elastic_ShouldMatchTheStaticBuilderWithUnitPenalty` 只比较**约束数与变量数**，
    /// 而这两者**与惩罚值无关**——把 `Flt64.One` 改成 `new Flt64(7.0)`，约束数和变量数完全不变，
    /// 那条判据照样绿。**判据的覆盖面比它想守的东西窄。**
    ///
    /// 这条改看目标里松弛列的系数：`Elastic` 必须地用 `1.0`。
    ///
    /// **This one was added because the previous mutation survived.** `Elastic_ShouldMatchTheStaticBuilderWithUnitPenalty` compares only **constraint and
    /// variable counts**, and neither depends on the penalty — changing `Flt64.One` to `new Flt64(7.0)` leaves both counts identical and that criterion stays
    /// green. **The criterion's coverage was narrower than the thing it meant to guard.**
    ///
 /// This one looks at the slack coefficients in the objective: `Elastic` must use `1.0`.
    /// </remarks>
    [Fact]
    public void Elastic_ShouldUseUnitPenaltyForSlackColumns() {
        QuadraticTetradModel model = MakeModel(ConstraintRelation.LessEqual);

        QuadraticTetradModel elastic = model.Elastic();

        int originalCellCount = model.Objective.Cells.Count;
        elastic.Objective.Cells.Count.Should().BeGreaterThan(originalCellCount, "slack penalties must be added");

        // 新增的那些单元格就是松弛惩罚；系数必须全是 1.0。
        // The added cells are the slack penalties; every coefficient must be 1.0.
        var slackCoefficients = elastic.Objective.Cells
            .Skip(originalCellCount)
            .Select(cell => cell.Coefficient)
            .ToList();
        slackCoefficients.Should().OnlyContain(coefficient => coefficient == Flt64.One);
    }

    /// <summary>**弹性约束必须标记为弹性来源** / Elastic constraints must be marked with the elastic source.</summary>
    /// <remarks>
    /// `ConstraintSource.Elastic` 在存在。
    /// 若 `Elastic` 只是复制了原约束、**没有改来源标记**，下游就无法区分哪些行是松弛行。
    ///
 /// `ConstraintSource.Elastic` exists in the implementation ( verified the enum matches item by item). If `Elastic` merely copied the original constraints
    /// **without changing the source mark**, downstream cannot tell which rows are slack rows.
    /// </remarks>
    [Fact]
    public void Elastic_ShouldMarkSlackConstraintsWithTheElasticSource() {
        QuadraticTetradModel model = MakeModel(ConstraintRelation.LessEqual);

        QuadraticTetradModel elastic = model.Elastic();

        elastic.Constraints.Sources.Should().Contain(
            ConstraintSource.Elastic,
            "at least one row must be an elastic slack row");
    }
}
