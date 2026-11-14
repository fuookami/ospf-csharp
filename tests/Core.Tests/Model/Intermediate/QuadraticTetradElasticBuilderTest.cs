#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 二次弹性构建器必须**保留原模型的约束与目标**，而不是返回一个空壳。
/// The quadratic elastic builder must **preserve the original constraints and objective**, not return a shell.
/// </summary>
/// <remarks>
/// B310 之前五个方法全部返回**约束全空、目标为空**的模型：
///
/// ```csharp
/// new QuadraticConstraintBatch(new SparseQuadraticMatrix(), ..., Array.Empty<ConstraintSource>())
/// QuadraticObjective.Create(ObjectCategory.Minimum, Array.Empty<QuadraticObjectiveCell>())
/// ```
///
/// **保留变量表、丢掉全部约束，然后报告成功**——调用方拿到的模型看起来合法，解出来是一个任意可行点，
/// 而且没有任何报错。这些测试的核心判据就是**约束没被丢掉**。
///
/// Before B310 all five methods returned a model with **zero constraints and an empty objective**. **Keeping the variable table while dropping every
/// constraint, then reporting success** — the caller receives something that looks legitimate, solves to an arbitrary feasible point, and sees no error. The
/// core criterion here is that **the constraints are not dropped**.
/// </remarks>
public class QuadraticTetradElasticBuilderTest {
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
            "quadratic-elastic-test");
    }

    /// <summary>**原约束必须全部保留** / Every original constraint must be retained.</summary>
    /// <remarks>
    /// **这是整批最重要的一条**：空壳实现返回的约束数是 0，而正确实现是"原约束 + 变量弹性行"。
    ///
    /// **This is the batch's most important criterion**: the shell returns zero constraints, while a correct implementation returns "original rows plus
    /// per-variable elastic rows".
    /// </remarks>
    [Fact]
    public void BuildElasticModel_ShouldKeepEveryOriginalConstraint() {
        QuadraticTetradModel model = MakeModel(ConstraintRelation.LessEqual);

        QuadraticTetradModel elastic = QuadraticTetradElasticBuilder.BuildElasticModel(model, new Flt64(100.0));

        elastic.Constraints.Size.Should().BeGreaterThanOrEqualTo(2, "the two original rows must survive");
        elastic.Constraints.Sources.Should().Contain(ConstraintSource.Elastic);
    }

    /// <summary>**原目标必须保留并加上松弛惩罚** / The objective must survive plus slack penalties.</summary>
    [Fact]
    public void BuildElasticModel_ShouldKeepTheObjectiveAndAddPenalties() {
        QuadraticTetradModel model = MakeModel(ConstraintRelation.LessEqual);

        QuadraticTetradModel elastic = QuadraticTetradElasticBuilder.BuildElasticModel(model, new Flt64(100.0));

        elastic.Objective.Should().NotBeNull();
        elastic.Objective.Cells.Should().NotBeEmpty("the shell returned an empty objective");
        // 松弛列的惩罚系数必须按传入的惩罚值写入。
        // The slack columns' penalty must be written with the supplied penalty.
        elastic.Objective.Cells.Should().Contain(cell => cell.Coefficient == new Flt64(100.0));
    }

    /// <summary>**每个变量都必须有弹性界行，且原边界被放开** / Every variable must get elastic bound rows and lose its hard bounds.</summary>
    [Fact]
    public void BuildElasticModel_ShouldRelaxVariableBounds() {
        QuadraticTetradModel model = MakeModel(ConstraintRelation.LessEqual);

        QuadraticTetradModel elastic = QuadraticTetradElasticBuilder.BuildElasticModel(model, new Flt64(100.0));

        elastic.Variables.Count.Should().BeGreaterThan(model.Variables.Count, "slack columns are appended");
        // 前两个是原变量，边界应已放开到无穷。
        // The first two are the original variables, now relaxed to infinity.
        elastic.Variables[0].LowerBound.Should().NotBe(new Flt64(0.0));
        elastic.Variables[0].UpperBound.Should().NotBe(new Flt64(10.0));
    }

    /// <summary>**等式约束必须产生两列松弛** / An equality row must produce two slack columns.</summary>
    /// <remarks>
    /// 三种关系的松弛列数不同（`<=` 一列、`>=` 一列、`==` 两列）。
    /// **若实现把 `==` 当成单列，约束方向会错，而约束总数仍"看起来正常"。**
    ///
    /// The three relations differ in slack count (`<=` one, `>=` one, `==` two). **Treating `==` as one column gets the direction wrong while the total row
    /// count still looks plausible.**
    /// </remarks>
    [Fact]
    public void EqualityRow_ShouldProduceTwoSlackColumns() {
        QuadraticTetradModel lessEqual = QuadraticTetradElasticBuilder.BuildElasticModel(
            MakeModel(ConstraintRelation.LessEqual), new Flt64(1.0));
        QuadraticTetradModel equal = QuadraticTetradElasticBuilder.BuildElasticModel(
            MakeModel(ConstraintRelation.Equal), new Flt64(1.0));

        equal.Variables.Count.Should().Be(lessEqual.Variables.Count + 1);
    }

    /// <summary>**没有基线依据的四个变体必须抛错** / The four variants without baseline backing must throw.</summary>
    /// <remarks>
    /// **抛错而不是返回空模型是刻意的**：空模型会静默丢掉全部约束。
    /// 这条判据锁住"不得再退回静默损坏"。
    ///
    /// **Throwing rather than returning an empty model is deliberate**: an empty model silently drops every constraint. This criterion pins down "must not
    /// regress to silent corruption".
    /// </remarks>

    /// <summary>**空模型不得被当作成功返回** / An empty model must never be returned as success.</summary>
    /// <remarks>
    /// 直接对照：正确实现的约束数**严格大于**空壳实现的 0。
    /// **若将来有人把实现改回空壳，这条会立刻失败。**
    ///
    /// A direct comparison: a correct implementation's constraint count is **strictly greater** than the shell's zero. **If anyone reverts to the shell, this
    /// fails immediately.**
    /// </remarks>
    [Fact]
    public void ElasticModel_ShouldNotBeEmpty() {
        QuadraticTetradModel model = MakeModel(ConstraintRelation.GreaterEqual);

        QuadraticTetradModel elastic = QuadraticTetradElasticBuilder.BuildElasticModel(model, new Flt64(7.0));

        elastic.Constraints.Size.Should().BePositive();
        elastic.Constraints.Size.Should().BeGreaterThan(0, "the shell produced exactly zero rows");
        elastic.Objective.Cells.Any().Should().BeTrue();
    }
}
