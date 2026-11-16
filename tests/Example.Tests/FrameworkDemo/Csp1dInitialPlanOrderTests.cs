// 初始方案池"去重与截断的顺序"判据 / Criteria for the order of deduplication and truncation in the initial plan pool.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **初始方案池必须"先去重、再截断"** / The initial plan pool must **deduplicate before truncating**.
/// </summary>
/// <remarks>
/// **第五节②记的缺口**：定价值那个判据**钉不住顺序**，因为它的候选顺序不可控。
/// 这里的候选顺序**可控**——用自定义初始生成器按 `[A, A, B]` 的顺序产出。
///
/// 两种顺序的结果**条数不同**，所以可区分：
///
/// | 顺序 | 上限 2 时结果 |
/// | --- | --- |
/// | **先去重再截断**（本实现） | `[A, B]` → **2 条** |
/// | **先截断再去重** | 截断留 `[A, A]` → 去重后 **1 条** |
///
/// **§5②'s gap**: the pricing criterion **could not pin the order** because its candidate order was not controllable. Here it is — a custom initial generator yields
/// `[A, A, B]` in that order.
///
/// The two orders differ **in cardinality**, so they are distinguishable (table above).
/// </remarks>
public sealed class Csp1dInitialPlanOrderTests {
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
        new(
            id: id,
            name: "product-" + id,
            width: new List<Quantity<Flt64>> { Millimetres(width) });

    private static Csp1dProblem<Flt64> MakeProblem() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> productA = MakeProduct("A", 50.0);
        Product<Flt64> productB = MakeProduct("B", 60.0);
        Product<Flt64> productC = MakeProduct("C", 70.0);
        var builder = new Csp1dProblemBuilder<Flt64>();
        builder.AddProduct(productA);
        builder.AddProduct(productB);
        builder.AddProduct(productC);
        builder.AddMaterial(material);
        builder.AddDemand(new ProductDemand<Flt64>(productA, Millimetres(50.0)));
        builder.AddDemand(new ProductDemand<Flt64>(productB, Millimetres(60.0)));
        builder.AddDemand(new ProductDemand<Flt64>(productC, Millimetres(70.0)));
        return builder.Build();
    }

    /// <summary>
    /// 按给定顺序产出方案的初始生成器（**含重复**）/ An initial generator yielding plans in a given order (**with duplicates**).
    /// </summary>
    /// <remarks>
    /// **这是本判据能成立的关键**：默认的 `SimpleInitialGenerator` 每个（物料, 需求）只产一条，
    /// 永远不会产生重复，所以**顺序根本观察不到**。
    ///
    /// **This is what makes the criterion possible**: the default `SimpleInitialGenerator` yields one plan per (material, demand) pair and never duplicates, so the order
    /// is **unobservable**.
    /// </remarks>
    private sealed class OrderedInitialGenerator : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> {
        public IReadOnlyList<CuttingPlan<Flt64>> Generate(GenerationInput<Flt64> input) {
            Material<Flt64> material = input.Materials[0];
            Product<Flt64> productA = input.Demands[0].Product;
            Product<Flt64> productB = input.Demands[1].Product;
            Product<Flt64> productC = input.Demands[2].Product;
            // **顺序刻意是 A, A, B, C**：重复项在前，其余不同键在后。
            // **The order is deliberately A, A, B, C**: the duplicate first, the other distinct keys after.
            return new List<CuttingPlan<Flt64>> {
                MakePlan("a1", material, productA),
                MakePlan("a2", material, productA),
                MakePlan("b1", material, productB),
                MakePlan("c1", material, productC),
            };
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

    /// <summary>**上限卡在重复项中间时，必须先折叠重复再截断** / With the limit cutting through the duplicates, dedup must happen before truncation.</summary>
    /// <remarks>
    /// 候选是 `[A, A, B, C]`、上限 **2**：
    ///
    /// | 实现 | 结果 |
    /// | --- | --- |
    /// | **先去重再截断**（本实现） | `[A, B]` → **2 条** |
    /// | **先截断再去重** | `[A, A]` → 去重后 **1 条** |
    /// | **根本不截断** | `[A, B, C]` → **3 条** |
    ///
    /// **一条判据同时钉住两件事**：顺序错了得 1，漏了截断得 3，**都不是 2**。
    ///
    /// Candidates are `[A, A, B, C]` with a limit of **2** (table above). **One criterion pins both things**: wrong order gives 1, missing truncation gives 3, and
    /// **neither is 2**.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldDeduplicateBeforeTruncatingTheInitialPlans() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var generation = new Csp1dColumnGeneration<Flt64>(
            new MockColumnGenerationSolver(),
            initialGenerator: new OrderedInitialGenerator());
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .SetColumnGeneration(
                maxInitialPlans: new Int64(2),
                maxPricingPlans: new Int64(8),
                iterationLimit: new Int64(1))
            .Build();

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem, config);

        result.Trace.InitialPlanCount.ToULong().Should().Be(
            2UL,
            "deduplicating [A, A, B, C] gives [A, B] and truncation keeps both; truncating first leaves 1 and never truncating leaves 3");
    }

    /// <summary>**上限不小于候选数时，去重后条数等于不同键数** / With a limit at or above the candidate count, the count equals the distinct-key count.</summary>
    /// <remarks>
    /// **反向对照**：上限放到 8 时，两种顺序**结果相同**（都是去重后 3 条）。
    /// 若这条也失败，说明问题不在顺序而在去重本身——**把两种失败原因分开**。
    ///
    /// **Reverse control**: with a limit of 8 both orders **agree** (3 distinct keys either way). If this one also failed, the problem would be deduplication itself rather
    /// than the order — **separating the two failure causes**.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldKeepDistinctPlansWhenTheLimitIsGenerous() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var generation = new Csp1dColumnGeneration<Flt64>(
            new MockColumnGenerationSolver(),
            initialGenerator: new OrderedInitialGenerator());
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .SetColumnGeneration(
                maxInitialPlans: new Int64(8),
                maxPricingPlans: new Int64(8),
                iterationLimit: new Int64(1))
            .Build();

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem, config);

        result.Trace.InitialPlanCount.ToULong().Should().Be(
            3UL,
            "there are three distinct canonical keys, so a generous limit keeps exactly three");
    }
}
