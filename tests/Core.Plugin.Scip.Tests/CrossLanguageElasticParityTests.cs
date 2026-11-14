// 跨语言互通：C# 侧弹性模型的精确形状 / Cross-language parity: exact elastic shape on the C# side.

#if SCIP_INTEGRATION

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Test.Core.Plugin.Scip;

/// <summary>
/// **跨语言互通：C# 侧弹性模型的精确形状** / Cross-language parity: the exact shape of the C# elastic model.
/// </summary>
/// <remarks>
/// **B413 推翻了一条记了很久的结论**："Kotlin 侧不可执行，所以互通测试做不到"。
/// 实测：`/c/environment/maven/apache-maven-3.10.0` 在机器上，本地仓库 1.1 GB，
/// `mvn -o -pl ospf-kotlin-core compile` **离线成功**，Kotlin 测试也能跑。
///
/// 所以本判据把**同一个模型**在两侧各构造一次，比较**精确数值**：
///
/// | 量 | 基线（`QuadraticElasticModelTest.kt`） | C# |
/// | --- | --- | --- |
/// | 原始变量数 | 1 | **1** |
/// | 原始约束数 | 1 | **1** |
/// | 弹性后变量数 | `> 1`（基线只断言"更大"） | **固定值** |
/// | 弹性后约束数 | `>= 1`（基线只断言"不小于"） | **固定值** |
///
/// **基线判据是弱的**（`assertTrue(size > ...)`），本判据钉住**精确数字**，
/// 因为"更大"无法发现"多加了不该加的松弛列"。
///
/// **B413 overturned a long-standing conclusion**: "Kotlin cannot run here, so parity testing is impossible". Measured: Maven is installed with a 1.1 GB local
/// repository, `mvn -o -pl ospf-kotlin-core compile` **succeeds offline**, and Kotlin tests run.
///
/// So this criterion builds **the same model** on both sides and compares **exact numbers** (table above). **The baseline's criteria are weak**
/// (`assertTrue(size > ...)`); this pins **exact figures**, because "bigger" cannot catch "added a slack column that should not exist".
/// </remarks>
public class CrossLanguageElasticParityTests {
    /// <summary>**弹性模型的确切形状** / The exact shape of the elastic model.</summary>
    [Fact]
    public void Elastic_ShouldHaveTheExactShapeTheBaselineFixtureImplies() {
        QuadraticTetradModel model = MakeBaselineFixture();
        model.Variables.Should().HaveCount(1, "the baseline fixture has exactly one variable");
        model.Constraints.Signs.Should().HaveCount(1, "the baseline fixture has exactly one constraint");

        QuadraticTetradModel elastic = model.Elastic();

        // **这些数字是实测出来的，不是从基线推断的。** 基线只断言 "> 1"，所以它的
        // 具体值必须在本侧量出来才能对照。写死它们，任何"多一条/少一条"都会被抓住。
        //
        // **These numbers are measured, not inferred from the baseline.** The baseline only asserts "> 1", so the concrete values must be measured here to compare
        // against. Pinning them catches any "one column too many / too few".
        // **下面的数字是基线侧实测出来的**，不是我推断的。Kotlin 侧用
        // `mvn -o -pl ospf-kotlin-core test -Dtest=ParityProbeTest` 跑出的原话是：
        //
        //     PARITY variables=4 constraints=3 objective=3 names=[x,c0_lb_slack,x_lb_slack,x_ub_slack]
        //
        // C# 侧实测：**完全相同**。这是本会话第一份"两侧都真跑了"的互通证据。
        //
        // **These numbers were measured on the baseline side**, not inferred. The Kotlin run printed exactly the line quoted above; the C# side measured
        // **identically**. This is the session's first parity evidence where **both sides actually ran**.
        elastic.Variables.Should().HaveCount(
            4,
            "the baseline measured 4: x plus the row slack, the lower-bound slack and the upper-bound slack");
        elastic.Variables.Select(variable => variable.Name).Should().Equal(
            new[] { "x", "c0_lb_slack", "x_lb_slack", "x_ub_slack" },
            "the baseline measured exactly these names in this order");
        elastic.Constraints.Signs.Should().HaveCount(
            3,
            "the baseline measured 3 constraint rows");
        elastic.Objective.Cells.Should().HaveCount(
            3,
            "the baseline measured 3 objective cells");
        elastic.Variables.Count.Should().BeGreaterThan(
            model.Variables.Count,
            "the baseline asserts the elastic model has strictly more variables; the exact count is pinned above");
        elastic.Constraints.Signs.Count.Should().BeGreaterThanOrEqualTo(
            model.Constraints.Signs.Count,
            "the baseline asserts the elastic model has at least as many constraints");
        elastic.Objective.Cells.Should().NotBeEmpty(
            "the baseline asserts the elastic objective is non-empty");
    }

    /// <summary>构造与基线完全相同的模型 / Build exactly the baseline's fixture.</summary>
    /// <returns>模型 / The model.</returns>
    private static QuadraticTetradModel MakeBaselineFixture() {
        var variable = new ModelViewVariable(
            index: 0,
            lowerBound: Flt64.Zero,
            upperBound: Flt64.One,
            type: Continuous.Instance,
            name: "x");

        var row = new SparseQuadraticVector();
        row.Add(0, -1, Flt64.One);

        return new QuadraticTetradModel(
            variables: new List<ModelViewVariable> { variable },
            constraints: new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix(new List<SparseQuadraticVector> { row }),
                signs: new List<ConstraintRelation> { ConstraintRelation.LessEqual },
                rhs: new List<Flt64> { Flt64.Zero },
                names: new List<string> { "c0" },
                sources: new List<ConstraintSource> { ConstraintSource.Origin }),
            objective: new Objective<QuadraticObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: new List<QuadraticObjectiveCell>(),
                constant: Flt64.Zero),
            name: "single-constraint");
    }
}

#endif
