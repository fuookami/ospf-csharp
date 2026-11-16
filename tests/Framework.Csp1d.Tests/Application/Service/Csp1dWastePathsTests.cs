// waste 其余三路的判据 / Criteria for the other three waste paths.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **waste 的其余三路（余料、物料成本、超产面积）各自必须算对** / The other three waste paths (rest material, material cost, over-production area) must each be right.
/// </summary>
/// <remarks>
/// **记的缺口**："`ExtractWasteResult` 的其余三路无判据"——那一批只钉住了 `TotalTrimWidth`。
///
/// 三路**共用** `SolutionAmount` 这个入口，但**各自的乘子不同**：
///
/// | 路 | 乘子 | 容易错的地方 |
/// | --- | --- | --- |
/// | 余料 | 余料面积代理 | 代理值算错 |
/// | 物料成本 | **按物料 id** 的单价惩罚 | 不同物料混在一起累加 |
/// | 超产面积 | 产品**最大宽度** | 用错产品、漏乘超产量 |
///
/// 第二行是这里的重点：`costByMaterial` 是**按物料分桶**的字典，
/// 两条不同物料的方案**必须各出一项**，而不是合成一项。
///
/// **the earlier gap**: "the other three paths of `ExtractWasteResult` have no criteria" — that batch pinned only `TotalTrimWidth`. The three paths **share** the
/// `SolutionAmount` entry point (fixed) but **each has a different multiplier** (table above). The second row is's focus: `costByMaterial` is a
/// **per-material** dictionary, so two plans on different materials **must yield two entries**, not one merged entry.
/// </remarks>
public class Csp1dWastePathsTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial(string id) =>
        new(
            id: id,
            name: id,
            // **必须给 `length`**：余料代理是"余宽 × 物料长度"，没有长度就返回 null，
            // `TotalRestMaterial` 跟着为 null。这也是该路一直没判据的原因之一。
            // **`length` is required**: the rest-material proxy is "rest width x material length", and without a length it returns null, leaving `TotalRestMaterial`
            // null as well — one reason this path never had a criterion.
            length: Millimetres(6000.0),
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
        double sliceWidth) =>
        new(
            id: id,
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(sliceWidth)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(sliceWidth)),
            });

    private static WasteMinimizationResult<Flt64>? ExtractWaste(
        IReadOnlyList<CuttingPlan<Flt64>> plans,
        IReadOnlyList<ProductDemand<Flt64>> demands,
        IReadOnlyList<Material<Flt64>> materials,
        WasteMinimizationConfig<Flt64> wasteConfig,
        IReadOnlyDictionary<int, double> amounts) {
        var input = new ProduceInput<Flt64>(
            cuttingPlans: plans,
            demands: demands,
            materials: materials,
            machines: new List<Machine<Flt64>>());
        var model = new LinearMetaModel<Flt64>();
        Csp1dProduceContext<Flt64> context = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.MILP)
            .WasteConfig(wasteConfig)
            .Build();
        context.Register(model).IsOk.Should().BeTrue();
        foreach ((int index, double amount) in amounts) {
            model.Tokens.Find(context.Produce[index]!)!.SetResult(new Flt64(amount));
        }
        return context.ExtractWasteResult(model);
    }

    /// <summary>**两条不同物料必须各出一项成本** / Two different materials must yield two cost entries.</summary>
    /// <remarks>
    /// 物料 `M1` 单价 2、`M2` 单价 5，各用 3 次：
    ///
    /// | 实现 | 结果 |
    /// | --- | --- |
    /// | 按物料分桶 | `M1: 6`、`M2: 15` |
    /// | 混成一个桶 | 一项 `21` |
    ///
    /// 混桶的实现**总数对、分项错**——按物料看成本的下游会全部错位。
    ///
    /// Materials `M1` at 2 and `M2` at 5, each used 3 times (table above). A merged-bucket implementation **gets the total right and the breakdown wrong**, which
    /// misplaces every downstream consumer that looks at per-material cost.
    /// </remarks>
    [Fact]
    public void ExtractWasteResult_ShouldBucketMaterialCostsPerMaterial() {
        Material<Flt64> material1 = MakeMaterial("M1");
        Material<Flt64> material2 = MakeMaterial("M2");
        Product<Flt64> product = MakeProduct("A", 60.0);
        CuttingPlan<Flt64> plan1 = MakePlan("p1", material1, product, 60.0);
        var plan2 = new CuttingPlan<Flt64>(
            id: "p2",
            material: material2,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(70.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(70.0)),
            });
        var wasteConfig = new WasteMinimizationConfig<Flt64> {
            MaterialCostPenalty = new Dictionary<string, Flt64> {
                ["M1"] = new Flt64(2.0),
                ["M2"] = new Flt64(5.0),
            },
        };

        WasteMinimizationResult<Flt64>? result = ExtractWaste(
            new List<CuttingPlan<Flt64>> { plan1, plan2 },
            new List<ProductDemand<Flt64>> { new(product, Millimetres(60.0)) },
            new List<Material<Flt64>> { material1, material2 },
            wasteConfig,
            new Dictionary<int, double> { [0] = 3.0, [1] = 3.0 });

        result.Should().NotBeNull();
        result!.MaterialCosts.Should().HaveCount(2, "each material gets its own entry");
        result.MaterialCosts.Single(c => c.MaterialId == "M1").Cost.ToDouble().Should().BeApproximately(6.0, 1e-9);
        result.MaterialCosts.Single(c => c.MaterialId == "M2").Cost.ToDouble().Should().BeApproximately(15.0, 1e-9);
    }

    /// <summary>**同一物料的多条方案必须累加到同一个桶** / Multiple plans on one material must accumulate into one bucket.</summary>
    /// <remarks>
    /// 这是上一条的**反向对照**：上一条钉"不能混桶"，这条钉"**不能重复出桶**"。
    ///
    /// 物料 `M1` 单价 2，两条方案各用 2 次：正确结果是**一项 `8`**，
    /// 出两项的实现（每条方案一项）在这条上红。
    ///
    /// This is the **reverse control** for the previous one: that pins "must not merge buckets", this pins "**must not duplicate buckets**". Material `M1` at 2 with two
    /// plans used twice each gives **one entry of 8**; an implementation emitting one entry per plan fails here.
    /// </remarks>
    [Fact]
    public void ExtractWasteResult_ShouldAccumulateCostsForTheSameMaterial() {
        Material<Flt64> material = MakeMaterial("M1");
        Product<Flt64> product = MakeProduct("A", 60.0);
        CuttingPlan<Flt64> plan1 = MakePlan("p1", material, product, 60.0);
        var plan2 = new CuttingPlan<Flt64>(
            id: "p2",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(70.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(70.0)),
            });
        var wasteConfig = new WasteMinimizationConfig<Flt64> {
            MaterialCostPenalty = new Dictionary<string, Flt64> { ["M1"] = new Flt64(2.0) },
        };

        WasteMinimizationResult<Flt64>? result = ExtractWaste(
            new List<CuttingPlan<Flt64>> { plan1, plan2 },
            new List<ProductDemand<Flt64>> { new(product, Millimetres(60.0)) },
            new List<Material<Flt64>> { material },
            wasteConfig,
            new Dictionary<int, double> { [0] = 2.0, [1] = 2.0 });

        result.Should().NotBeNull();
        result!.MaterialCosts.Should().HaveCount(1, "both plans share the material, so there is one bucket");
        result.MaterialCosts[0].Cost.ToDouble().Should().BeApproximately(
            8.0,
            1e-9,
            "2 per unit times 2 uses, twice over, is 8");
    }

    /// <summary>**余料面积代理必须乘上次数** / The rest-material proxy must be multiplied by the amount.</summary>
    /// <remarks>
    /// 余料惩罚权重 1、方案用 4 次。判据断言**结果非空且等于余料代理 × 4**，
    /// 而不是"非空"——漏乘次数的实现同样非空。
    ///
    /// Rest-material penalty weight 1, the plan used 4 times. The assertion is on **the proxy times 4** rather than "non-null" — an implementation that forgets the
    /// multiplier is equally non-null.
    /// </remarks>
    [Fact]
    public void ExtractWasteResult_ShouldMultiplyTheRestMaterialByTheAmount() {
        Material<Flt64> material = MakeMaterial("M1");
        Product<Flt64> product = MakeProduct("A", 60.0);
        CuttingPlan<Flt64> plan = MakePlan("p1", material, product, 60.0);
        var wasteConfig = new WasteMinimizationConfig<Flt64> {
            RestMaterialPenalty = new Flt64(1.0),
        };

        WasteMinimizationResult<Flt64>? once = ExtractWaste(
            new List<CuttingPlan<Flt64>> { plan },
            new List<ProductDemand<Flt64>> { new(product, Millimetres(60.0)) },
            new List<Material<Flt64>> { material },
            wasteConfig,
            new Dictionary<int, double> { [0] = 1.0 });
        WasteMinimizationResult<Flt64>? fourTimes = ExtractWaste(
            new List<CuttingPlan<Flt64>> { plan },
            new List<ProductDemand<Flt64>> { new(product, Millimetres(60.0)) },
            new List<Material<Flt64>> { material },
            wasteConfig,
            new Dictionary<int, double> { [0] = 4.0 });

        once.Should().NotBeNull();
        fourTimes.Should().NotBeNull();
        once!.TotalRestMaterial.Should().NotBeNull();
        fourTimes!.TotalRestMaterial.Should().NotBeNull();
        // **用比值而不是绝对值**：余料代理的具体公式不是本判据要钉的东西，
        // "用 4 次 = 4 倍"才是。
        // **A ratio rather than an absolute value**: the proxy's exact formula is not what this pins — "4 uses is 4 times" is.
        (fourTimes.TotalRestMaterial!.Value.ToDouble() / once.TotalRestMaterial!.Value.ToDouble())
            .Should().BeApproximately(4.0, 1e-9, "four uses must cost four times one use");
    }
}
