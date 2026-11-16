// 新列需求贡献单项式判据 / Criteria for the demand-contribution monomials of newly added columns.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.Domain.Produce;

/// <summary>
/// **新列的需求贡献必须真的写进中间符号** / A new column's demand contribution must actually reach the intermediate symbols.
/// </summary>
/// <remarks>
/// **记的缺口**："新列的需求贡献单项式是否真接上未验证"——那一批只验证了**变量注册**
/// （`x_N` 出现在模型里），**没验证系数**。
///
/// 系数写错的后果是**静默的**：需求约束的左边少了一项，求解器会认为"这条需求已被满足得更多"，
/// **得到的解看起来可行、实际不满足需求**。
///
/// **The gap recorded**: "whether a new column's demand-contribution monomial is really wired is unverified" — that batch only verified **variable registration**
/// (`x_N` appearing in the model), **not the coefficient**.
///
/// A wrong coefficient fails **silently**: the demand constraint's left side loses a term, so the solver believes the demand is better satisfied, and **the solution looks
/// feasible while the demand is not met**.
/// </remarks>
public class ProduceAggregationDemandMonomialTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));

    private static Product<Flt64> MakeProduct(string id, double width) =>
        new(id: id, name: id, width: new List<Quantity<Flt64>> { Millimetres(width) });

    private static CuttingPlan<Flt64> MakePlan(
        string id,
        Material<Flt64> material,
        Product<Flt64> product,
        double contribution) =>
        new(
            id: id,
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(contribution)),
            });

    private static (ProduceAggregation<Flt64> Aggregation, LinearMetaModel<Flt64> Model) MakeAggregation(
        IReadOnlyList<CuttingPlan<Flt64>> initialPlans,
        IReadOnlyList<ProductDemand<Flt64>> demands,
        Material<Flt64> material) {
        var model = new LinearMetaModel<Flt64>();
        var aggregation = new ProduceAggregation<Flt64>(
            cuttingPlans: initialPlans,
            demands: demands,
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>>());
        return (aggregation, model);
    }

    /// <summary>**新列的贡献系数必须等于它声明的数量** / The new column's coefficient must equal its declared quantity.</summary>
    /// <remarks>
    /// 断言**系数等于 50**，而不是"多了一个单项式"——
    /// 系数写成 1 的实现同样"多了一个单项式"，**但那是错的**。
    ///
    /// This asserts **the coefficient is 50**, not "a monomial appeared" — an implementation writing coefficient 1 also "adds a monomial" and **is wrong**.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldAddTheDemandContributionWithItsQuantityAsCoefficient() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> productA = MakeProduct("A", 50.0);
        Product<Flt64> productB = MakeProduct("B", 60.0);
        var demands = new List<ProductDemand<Flt64>> {
            new(productA, Millimetres(50.0)),
            new(productB, Millimetres(60.0)),
        };
        // **池里那条必须与新列内容不同**，否则会被规范键去重掉，`AddColumns` 什么都不加。
        // **The pool entry must differ in content from the new column**, or it is deduped by canonical key and `AddColumns` adds nothing.
        CuttingPlan<Flt64> existing = MakePlan("p1", material, productB, 60.0);
        (ProduceAggregation<Flt64> aggregation, LinearMetaModel<Flt64> model) =
            MakeAggregation(new List<CuttingPlan<Flt64>> { existing }, demands, material);
        int before = aggregation.DemandQuantity[0].Polynomial.Monomials.Count;

        CuttingPlan<Flt64> added = MakePlan("p2", material, productA, 50.0);
        aggregation.AddColumns(UInt64.One, new List<CuttingPlan<Flt64>> { added }, model)
            .IsOk.Should().BeTrue();

        IReadOnlyList<LinearMonomial<Flt64>> monomials = aggregation.DemandQuantity[0].Polynomial.Monomials;
        monomials.Count.Should().BeGreaterThan(before, "the new column contributes to demand A");
        double lastCoefficient = monomials[^1].Coefficient.ToDouble();
        lastCoefficient.Should().BeApproximately(
            50.0,
            1e-9,
            "the coefficient must be the declared contribution quantity, not 1");
    }

    /// <summary>**初始方案也必须填充中间符号**——第一轮 LP 靠它 / Initial plans must populate the intermediate symbols — the first LP depends on it.</summary>
    /// <remarks>
    /// **记的缺口**："初始方案的贡献单项式仍不登记"，并误以为"`addInitialPlans` 只登记 id/键"。
    ///
    /// **那是错的。** `register` 在 `addInitialPlans` **之后**还会建 `PlanBatchTerms`
    /// 用它填 `demandQuantity` / `materialQuantity` /
    /// `machineBatchQuantity` / `machineCapacityQuantity`。C# **完全没有这个类**
    /// （`grep PlanBatchTerms` 零命中），构造阶段只建空符号。
    ///
    /// 后果是**第一轮 LP 的需求与物料约束左边恒为零**——求解器会认为"什么都不做就已满足全部需求"。
    ///
    /// **The gap recorded** ("initial plans' contribution monomials are still unregistered") also wrongly assumed "its `addInitialPlans` only registers
    /// ids/keys, so both sides agree". **That was wrong.** The code's `register` builds `PlanBatchTerms` **after** `addInitialPlans` and fills the
    /// four symbol families from it. C# **has no such class at all** (`grep PlanBatchTerms` finds nothing) and its constructor only creates empty symbols.
    ///
    /// The consequence: **the first LP's demand and material constraints have an identically zero left side** — the solver believes doing nothing already satisfies every demand.
    /// </remarks>
    [Fact]
    public void Constructor_ShouldPopulateTheSymbolsFromTheInitialPlans() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> productA = MakeProduct("A", 50.0);
        var demands = new List<ProductDemand<Flt64>> { new(productA, Millimetres(50.0)) };
        CuttingPlan<Flt64> plan = MakePlan("p1", material, productA, 50.0);

        (ProduceAggregation<Flt64> aggregation, _) = MakeAggregation(
            new List<CuttingPlan<Flt64>> { plan },
            demands,
            material);

        // **需求符号必须带一个系数为 50 的单项式**，不是空的。
        // **The demand symbol must carry one monomial with coefficient 50**, not be empty.
        IReadOnlyList<LinearMonomial<Flt64>> demandMonomials =
            aggregation.DemandQuantity[0].Polynomial.Monomials;
        demandMonomials.Should().HaveCount(1);
        demandMonomials[0].Coefficient.ToDouble().Should().BeApproximately(50.0, 1e-9);

        // **物料符号必须带一个系数为 1 的单项式**。
        // **The material symbol must carry one monomial with coefficient 1.**
        IReadOnlyList<LinearMonomial<Flt64>> materialMonomials =
            aggregation.MaterialQuantity[0].Polynomial.Monomials;
        materialMonomials.Should().HaveCount(1);
        materialMonomials[0].Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>**同一方案内同一（产品, 单位）只计一次** / A repeated (product, unit) pair within one plan counts once.</summary>
    /// <remarks>
    /// `seen` 集合就是干这个的。少了它，一条方案里写两次同样的贡献，
    /// **需求约束的系数会翻倍**——解看起来更"够"，实际不够。
    ///
    /// The code's `seen` set does exactly this. Without it, writing the same contribution twice in one plan **doubles the demand coefficient** — the solution looks
    /// better satisfied than it is.
    /// </remarks>
    [Fact]
    public void Constructor_ShouldCountARepeatedContributionOnlyOnce() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> productA = MakeProduct("A", 50.0);
        var demands = new List<ProductDemand<Flt64>> { new(productA, Millimetres(50.0)) };
        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(productA, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(productA, Millimetres(50.0)),
                new(productA, Millimetres(50.0)),
            });

        (ProduceAggregation<Flt64> aggregation, _) = MakeAggregation(
            new List<CuttingPlan<Flt64>> { plan },
            demands,
            material);

        IReadOnlyList<LinearMonomial<Flt64>> monomials =
            aggregation.DemandQuantity[0].Polynomial.Monomials;
        monomials.Should().HaveCount(1, "the duplicate pair must be collapsed, not summed");
        monomials[0].Coefficient.ToDouble().Should().BeApproximately(
            50.0,
            1e-9,
            "a doubled coefficient would be 100");
    }

    /// <summary>**单位不匹配的贡献不得计入** / A contribution whose unit does not match must not be counted.</summary>
    /// <remarks>
    /// 条件是两个等号：`contribution.product.id == demand.product.id && contribution.quantity.unit == demand.quantity.unit`。
    ///
    /// 若实现只比较产品 id，一条**以厘米为单位**的贡献会被当成**毫米**的贡献计入，
    /// **系数差 10 倍而毫无提示**。这条判据钉住那个单位比较。
    ///
    /// The code requires both: `contribution.product.id == demand.product.id && contribution.quantity.unit == demand.quantity.unit`. If the implementation compared only
    /// the product id, a contribution in **centimetres** would count as one in **millimetres** — **a factor of 10 with no warning**. This pins the unit comparison.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldIgnoreAContributionWithADifferentUnit() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> productA = MakeProduct("A", 50.0);
        var demands = new List<ProductDemand<Flt64>> {
            new(productA, Millimetres(50.0)),
        };
        CuttingPlan<Flt64> existing = MakePlan("p1", material, productA, 50.0);
        (ProduceAggregation<Flt64> aggregation, LinearMetaModel<Flt64> model) =
            MakeAggregation(new List<CuttingPlan<Flt64>> { existing }, demands, material);
        int before = aggregation.DemandQuantity[0].Polynomial.Monomials.Count;

        var wrongUnit = new CuttingPlan<Flt64>(
            id: "p2",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(productA, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(productA, new Quantity<Flt64>(new Flt64(5.0), LengthUnits.Centimeter)),
            });
        aggregation.AddColumns(UInt64.One, new List<CuttingPlan<Flt64>> { wrongUnit }, model)
            .IsOk.Should().BeTrue();

        aggregation.DemandQuantity[0].Polynomial.Monomials.Count.Should().Be(
            before,
            "a centimetre-valued contribution must not be counted against a millimetre demand");
    }
}
