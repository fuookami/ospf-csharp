// 长度目标项的判据 / Criteria for the length objective terms.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Service.Pipeline;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Domain.LengthAssignment;

/// <summary>
/// **长度目标项：总长惩罚、超长惩罚、批次系数** / The length objective: total-length penalty, over-length penalty, batch coefficient.
/// </summary>
/// <remarks>
/// **记的缺口**："`LengthObjectivePipeline` 无判据"。
///
/// 三部分各自写错的后果：
///
/// | 部分 | 形式 | 写错的后果 |
/// | --- | --- | --- |
/// | 总长惩罚 | `sum(totalLengthPenalty × assigned_i)` | 漏乘 ⇒ 惩罚不生效 |
/// | 超长惩罚 | `sum(overLengthPenalty[id] × over_i)` | 用错产品的系数 ⇒ 惩罚错位 |
/// | 批次系数 | `1 + batchMinPenalty` | **写成 `1 × penalty` 或 `penalty` ⇒ 批次成本全错** |
///
/// **the earlier gap**: "`LengthObjectivePipeline` has no criteria". Each part fails differently (table above). The last row is the sharpest: `1 + penalty` written as `1 * penalty`
/// or plain `penalty` gets the batch cost entirely wrong.
/// </remarks>
public class LengthObjectivePipelineTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Product<Flt64> MakeProduct(string id) =>
        new(
            id: id,
            name: id,
            width: new List<Quantity<Flt64>> { Millimetres(60.0) },
            maxOverProduceLength: Millimetres(500.0));

    private static (LengthAggregation Length, LengthObjectivePipeline<Flt64> Pipeline) Build(
        LengthAssignmentModelingConfig<Flt64> config,
        IReadOnlyList<ProductDemand<Flt64>> demands) {
        var length = new LengthAggregation(config, demands);
        var produce = new ProduceAggregation<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>>(),
            demands: demands,
            materials: new List<Material<Flt64>>(),
            machines: new List<Machine<Flt64>>());
        var pipeline = new LengthObjectivePipeline<Flt64>(produce, length, config);
        return (length, pipeline);
    }

    /// <summary>**总长惩罚 = 系数 × 每条已分配长度变量** / The total-length penalty is the coefficient times each assigned-length variable.</summary>
    /// <remarks>
    /// 两条需求、系数 3 ⇒ **两个**单项式，系数都是 3。
    ///
    /// 断言**数量 + 每个系数**：只断言"非空"抓不住"只给第一条加"。
    ///
    /// Two demands with coefficient 3 give **two** monomials, both with coefficient 3. The assertions cover **the count and every coefficient**: "non-empty" cannot catch
    /// "only the first demand gets a term".
    /// </remarks>
    [Fact]
    public void ObjectiveMonomials_ShouldEmitOneTotalLengthTermPerDemand() {
        var demands = new List<ProductDemand<Flt64>> {
            new(MakeProduct("A"), Millimetres(60.0)),
            new(MakeProduct("B"), Millimetres(70.0)),
        };
        // **只启用总长惩罚**，不配超长惩罚——否则两组单项式混在一起，数不清。
        // **Enable only the total-length penalty**; adding an over-length penalty would mix two sets of monomials and make the count meaningless.
        var config = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string> { "A", "B" },
            TotalLengthPenalty: new Flt64(3.0));
        (_, LengthObjectivePipeline<Flt64> pipeline) = Build(config, demands);

        IReadOnlyList<LinearMonomial<Flt64>> monomials = pipeline.ObjectiveMonomials();

        monomials.Should().HaveCount(2, "one total-length term per demand");
        monomials.Should().OnlyContain(m => global::System.Math.Abs(m.Coefficient.ToDouble() - 3.0) < 1e-9);
    }

    /// <summary>**超长惩罚用对应产品的系数，不是别的产品的** / The over-length penalty uses the matching product's coefficient, not another product's.</summary>
    /// <remarks>
    /// `A` 的系数是 5、`B` 的是 9。若实现把字典的第一个值当成所有产品的系数，
    /// **两条都会是 5**——总数对、分项错。这条判据按**变量**核对系数，
    /// 所以错位会当场红。
    ///
    /// `A`'s coefficient is 5 and `B`'s is 9. If the implementation used the dictionary's first value for every product, **both would be 5** — right total, wrong breakdown. This
    /// criterion checks the coefficient **per variable**, so a mismatch fails immediately.
    /// </remarks>
    [Fact]
    public void ObjectiveMonomials_ShouldUseTheMatchingProductPenalty() {
        Product<Flt64> productA = MakeProduct("A");
        Product<Flt64> productB = MakeProduct("B");
        var demands = new List<ProductDemand<Flt64>> {
            new(productA, Millimetres(60.0)),
            new(productB, Millimetres(70.0)),
        };
        var config = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string> { "A", "B" },
            OverLengthPenalty: new Dictionary<string, Flt64> { ["A"] = new Flt64(5.0), ["B"] = new Flt64(9.0) });
        (LengthAggregation length, LengthObjectivePipeline<Flt64> pipeline) = Build(config, demands);

        IReadOnlyList<LinearMonomial<Flt64>> monomials = pipeline.ObjectiveMonomials();

        monomials.Should().HaveCount(2);
        double coefficientForA = monomials.Single(m => Equals(m.Symbol, length.OverLength[0])).Coefficient.ToDouble();
        double coefficientForB = monomials.Single(m => Equals(m.Symbol, length.OverLength[1])).Coefficient.ToDouble();
        coefficientForA.Should().BeApproximately(5.0, 1e-9, "A's own penalty is 5");
        coefficientForB.Should().BeApproximately(9.0, 1e-9, "B's own penalty is 9");
    }

    /// <summary>**批次系数是 `1 + 惩罚`，不是 `1 × 惩罚`** / The batch coefficient is `1 + penalty`, not `1 * penalty`.</summary>
    /// <remarks>
    /// 惩罚 0.5 ⇒ 系数 **1.5**。
    ///
    /// | 实现 | 结果 |
    /// | --- | --- |
    /// | 正确（基线 `1 + penalty`） | **1.5** |
    /// | `1 * penalty` | 0.5 |
    /// | 直接用 `penalty` | 0.5 |
    /// | 恒 `1` | 1.0 |
    ///
    /// 三个错误实现被一条判据分开。
    ///
    /// A penalty of 0.5 gives a coefficient of **1.5** (table above); one criterion separates all three wrong implementations.
    /// </remarks>
    [Fact]
    public void BatchCoefficient_ShouldBeOnePlusThePenalty() {
        var demands = new List<ProductDemand<Flt64>> { new(MakeProduct("A"), Millimetres(60.0)) };
        var config = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string> { "A" },
            OverLengthPenalty: new Dictionary<string, Flt64> { ["A"] = new Flt64(1.0) },
            BatchMinPenalty: new Flt64(0.5));
        (_, LengthObjectivePipeline<Flt64> pipeline) = Build(config, demands);

        pipeline.BatchCoefficient().ToDouble().Should().BeApproximately(
            1.5,
            1e-9,
            "the baseline is 1 + batchMinPenalty");
    }

    /// <summary>**没有批次最小惩罚时系数为 1**——反向对照 / With no batch-min penalty the coefficient is 1 — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"**恒返回 1.5**"之类的实现可能蒙混过关。
    ///
    /// Without this, an implementation that **always returns 1.5** could slip through.
    /// </remarks>
    [Fact]
    public void BatchCoefficient_ShouldBeOneWithoutAPenalty() {
        var demands = new List<ProductDemand<Flt64>> { new(MakeProduct("A"), Millimetres(60.0)) };
        var config = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string> { "A" },
            OverLengthPenalty: new Dictionary<string, Flt64> { ["A"] = new Flt64(1.0) });
        (_, LengthObjectivePipeline<Flt64> pipeline) = Build(config, demands);

        pipeline.BatchCoefficient().ToDouble().Should().BeApproximately(1.0, 1e-9);
    }
}
