// 定价生成器"去重与截断的顺序"判据 / Criteria for the order of deduplication and truncation in the pricing generator.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **定价生成器必须"先去重、再截断"** / The pricing generator must **deduplicate before truncating**.
/// </summary>
/// <remarks>
/// **B364 记的缺口**：上一批覆盖的是**初始方案池**的去重+截断；定价器里"先截断再去重"仍无判据。
///
/// 这里能钉住是因为**候选顺序可控**：用一个按固定顺序产出的枚举器，
/// 默认的 `SimpleInitialGenerator` 每个（物料, 需求）只产一条，**永远不会重复**。
///
/// **B364's gap**: the previous batch covered the **initial plan pool**; "truncate before dedupe" in the pricing generator still had no criterion.
///
/// It is pinnable here because **the candidate order is controllable**: an enumerator yielding a fixed order, whereas the default `SimpleInitialGenerator` yields one plan
/// per (material, demand) pair and **never duplicates**.
/// </remarks>
public class Csp1dPricingOrderTests {
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

    private static ProductDemand<Flt64> MakeDemand(Product<Flt64> product) =>
        new(product, Millimetres(50.0));

    private static ShadowPriceMap<Flt64> PricesFor(params string[] productIds) =>
        new(productIds.ToDictionary(
            id => (Csp1dShadowPriceKey)new ProductDemandShadowPriceKey(id, "mm"),
            _ => new Flt64(0.05)));

    /// <summary>按固定顺序产出 `[A, A, B, C]` 的枚举器 / An enumerator yielding `[A, A, B, C]` in that order.</summary>
    private sealed class OrderedEnumerator : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> {
        public IReadOnlyList<CuttingPlan<Flt64>> Generate(GenerationInput<Flt64> input) {
            Material<Flt64> material = input.Materials[0];
            var plans = new List<CuttingPlan<Flt64>>();
            // **顺序刻意是 A, A, B, C**：重复项在前，其余不同键在后。
            // **The order is deliberately A, A, B, C**: the duplicate first, the other distinct keys after.
            plans.Add(MakePlan("a1", material, input.Demands[0].Product));
            plans.Add(MakePlan("a2", material, input.Demands[0].Product));
            plans.Add(MakePlan("b1", material, input.Demands[1].Product));
            plans.Add(MakePlan("c1", material, input.Demands[2].Product));
            return plans;
        }

        private static CuttingPlan<Flt64> MakePlan(string id, Material<Flt64> material, Product<Flt64> product) =>
            new(
                id: id,
                material: material,
                slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
                demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                    new(product, Millimetres(50.0)),
                });
    }

    private static Csp1dPricingInput<Flt64> MakeInput(ulong maxGeneratedPlans) {
        Product<Flt64> productA = MakeProduct("A", 50.0);
        Product<Flt64> productB = MakeProduct("B", 60.0);
        Product<Flt64> productC = MakeProduct("C", 70.0);
        return new Csp1dPricingInput<Flt64>(
            GenerationInput: new GenerationInput<Flt64> {
                Materials = new List<Material<Flt64>> { MakeMaterial() },
                Demands = new List<ProductDemand<Flt64>> {
                    MakeDemand(productA),
                    MakeDemand(productB),
                    MakeDemand(productC),
                },
            },
            ShadowPrices: PricesFor("A", "B", "C"),
            MaxGeneratedPlans: new UInt64(maxGeneratedPlans));
    }

    /// <summary>**上限卡在重复项中间时，必须先折叠重复再截断** / With the limit cutting through the duplicates, dedup must precede truncation.</summary>
    /// <remarks>
    /// 候选 `[A, A, B, C]`、上限 **2**：
    ///
    /// | 实现 | 结果 |
    /// | --- | --- |
    /// | **先去重再截断**（基线） | `[A, B]` → **2** |
    /// | **先截断再去重** | `[A, A]` → **1** |
    /// | **根本不截断** | `[A, B, C]` → **3** |
    ///
    /// 一条判据钉住两个变异；"根本不去重"由下一条反向对照钉住。
    ///
    /// Candidates are `[A, A, B, C]` with a limit of **2** (table above). One criterion pins two mutants; "no deduplication at all" is pinned by the reverse control below.
    /// </remarks>
    [Fact]
    public void Generate_ShouldDeduplicateBeforeTruncating() {
        var pricing = new ReducedCostPricingGenerator<Flt64>(new OrderedEnumerator());

        IReadOnlyList<CuttingPlan<Flt64>> plans = pricing.Generate(MakeInput(maxGeneratedPlans: 2UL));

        plans.Should().HaveCount(
            2,
            "deduplicating [A, A, B, C] gives [A, B] and the limit of 2 keeps both; truncating first leaves 1 and never truncating leaves 3");
    }

    /// <summary>**上限放宽时，重复项必须被折叠** / With a generous limit the duplicate must still collapse.</summary>
    /// <remarks>
    /// **反向对照**：上限 8 时正确实现给 **3**（`A, B, C`），**不去重给 4**。
    /// 若只留上一条，"根本不去重"的实现在上限 2 下**恰好也给 2**，就钉不住。
    ///
    /// **Reverse control**: with a limit of 8 the correct implementation yields **3** (`A, B, C`) and **no deduplication yields 4**. With only the previous criterion, a
    /// no-dedup implementation **also yields 2** under a limit of 2 and could not be caught.
    /// </remarks>
    [Fact]
    public void Generate_ShouldCollapseTheDuplicateWithAGenerousLimit() {
        var pricing = new ReducedCostPricingGenerator<Flt64>(new OrderedEnumerator());

        IReadOnlyList<CuttingPlan<Flt64>> plans = pricing.Generate(MakeInput(maxGeneratedPlans: 8UL));

        plans.Should().HaveCount(
            3,
            "there are three distinct canonical keys; a fourth entry would mean the duplicate survived");
    }
}
