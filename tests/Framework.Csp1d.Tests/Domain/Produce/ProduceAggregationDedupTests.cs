// ProduceAggregation 加列去重的双键判据 / Criteria for ProduceAggregation's two-key column deduplication.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.Domain.Produce;

/// <summary>
/// **`ProduceAggregation.AddColumns` 必须按 `id` 与 `canonicalKey` 双键去重**
/// / `ProduceAggregation.AddColumns` must dedupe by both `id` and `canonicalKey`.
/// </summary>
/// <remarks>
/// **B361 记的缺口**写着"`ProduceAggregation._planKeyIndex` 仍用默认键"。查基线后发现**那半句判断是错的**：
/// 基线 `ProduceAggregation.kt:56` 用的正是 **`candidate.canonicalKey()`（默认键）**，
/// 所以 C# 在这里用默认键**与基线一致**，不是缺口。
///
/// **但同一条语句里还有第二个条件**：`candidate.id !in registeredIds`。C# 原本**只有键索引**，
/// 于是"同 id、不同内容"的两条方案会被**同时接受**——这才是真缺口。
///
/// **B361's ledger entry** said "`ProduceAggregation._planKeyIndex` still uses the default key". Checking the baseline showed **that half was wrong**: the baseline at
/// `ProduceAggregation.kt:56` uses exactly **`candidate.canonicalKey()` (the default key)**, so C# matches the baseline there and it is not a gap.
///
/// **But that same statement has a second condition**: `candidate.id !in registeredIds`. C# originally had **only the key index**, so two plans sharing an id but differing
/// in content would **both be accepted** — that was the real gap.
/// </remarks>
public class ProduceAggregationDedupTests {
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

    private static CuttingPlan<Flt64> MakePlan(string id, Material<Flt64> material, Product<Flt64> product) =>
        new(
            id: id,
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(50.0)),
            });

    private static (ProduceAggregation<Flt64> Aggregation, IAbstractLinearMetaModel<Flt64> Model) MakeAggregation(
        IReadOnlyList<CuttingPlan<Flt64>> plans,
        IReadOnlyList<ProductDemand<Flt64>> demands,
        Material<Flt64> material) {
        var model = new LinearMetaModel<Flt64>();
        var aggregation = new ProduceAggregation<Flt64>(
            cuttingPlans: plans,
            demands: demands,
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>>());
        return (aggregation, model);
    }

    /// <summary>**同 id、不同内容的新列必须被拒** / A new column with an existing id but different content must be rejected.</summary>
    /// <remarks>
    /// 池里已有 `id = "p1"`（产品 A）。新列**也叫 `"p1"` 但换成产品 B**——
    /// 规范键不同，**只有 id 相同**。
    ///
    /// | 实现 | 结果 |
    /// | --- | --- |
    /// | 基线（双键） | **0 条被接受** |
    /// | 仅键索引（B362 前） | **1 条被接受** |
    ///
    /// The pool already holds `id = "p1"` (product A). The new column is **also `"p1"` but for product B** — a different canonical key, **only the id collides**
    /// (table above).
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldRejectANewColumnThatReusesAnExistingId() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> productA = MakeProduct("A", 50.0);
        Product<Flt64> productB = MakeProduct("B", 60.0);
        var demands = new List<ProductDemand<Flt64>> {
            new(productA, Millimetres(50.0)),
            new(productB, Millimetres(60.0)),
        };
        CuttingPlan<Flt64> existing = MakePlan("p1", material, productA);
        (ProduceAggregation<Flt64> aggregation, IAbstractLinearMetaModel<Flt64> model) =
            MakeAggregation(new List<CuttingPlan<Flt64>> { existing }, demands, material);

        CuttingPlan<Flt64> colliding = MakePlan("p1", material, productB);
        Result<IReadOnlyList<CuttingPlan<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            aggregation.AddColumns(UInt64.One, new List<CuttingPlan<Flt64>> { colliding }, model);

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeEmpty("the id already exists, so the column must be rejected");
    }

    /// <summary>**id 不同的同键新列也必须被拒** / A new column with a new id but an existing canonical key must still be rejected.</summary>
    /// <remarks>
    /// **反向对照，钉住"双键"的另一半**：只查 id 不查键的实现在这条上会红。
    ///
    /// **Reverse control pinning the other half of the two-key rule**: an implementation checking only the id would fail here.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldRejectANewColumnThatReusesAnExistingCanonicalKey() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> productA = MakeProduct("A", 50.0);
        var demands = new List<ProductDemand<Flt64>> {
            new(productA, Millimetres(50.0)),
        };
        CuttingPlan<Flt64> existing = MakePlan("p1", material, productA);
        (ProduceAggregation<Flt64> aggregation, IAbstractLinearMetaModel<Flt64> model) =
            MakeAggregation(new List<CuttingPlan<Flt64>> { existing }, demands, material);

        CuttingPlan<Flt64> sameContentNewId = MakePlan("p2", material, productA);
        Result<IReadOnlyList<CuttingPlan<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            aggregation.AddColumns(UInt64.One, new List<CuttingPlan<Flt64>> { sameContentNewId }, model);

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeEmpty("the canonical key already exists, so the column must be rejected");
    }

    /// <summary>**第一次加列后，新 id 必须进入 id 集合** / After the first add, the new id must enter the id set.</summary>
    /// <remarks>
    /// **这条是变异测试逼出来的。** 我最初只测"构造时已有的 id 会被拒"，
    /// 于是变异"**加列后不把 id 记进 `_planIds`**"**存活**——因为没有任何判据连续加两次列。
    ///
    /// 判据：第一次接受 `p2`，第二次送一条**同样叫 `p2`、但内容不同**的列，必须被拒。
    ///
    /// **A mutation forced this criterion.** I first only tested "an id present at construction is rejected", so the mutation "**do not record the id in `_planIds`
    /// after adding**" **survived** — no criterion added columns twice.
    ///
    /// The criterion: accept `p2` in the first call, then submit another column **also named `p2` but with different content**, which must be rejected.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldRegisterTheNewIdsForLaterCalls() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> productA = MakeProduct("A", 50.0);
        Product<Flt64> productB = MakeProduct("B", 60.0);
        var demands = new List<ProductDemand<Flt64>> {
            new(productA, Millimetres(50.0)),
            new(productB, Millimetres(60.0)),
        };
        // **第三种产品是关键**：第二列的规范键必须与池里任何一条都**不同**，
        // 否则它会被**键**检查拒掉，id 检查有没有生效就看不出来了。
        // **The third product is essential**: the second column's canonical key must differ from every pool entry, otherwise the **key** check rejects it and the id
        // check's effect is invisible.
        Product<Flt64> productC = MakeProduct("C", 70.0);
        demands.Add(new ProductDemand<Flt64>(productC, Millimetres(70.0)));
        CuttingPlan<Flt64> existing = MakePlan("p1", material, productA);
        (ProduceAggregation<Flt64> aggregation, IAbstractLinearMetaModel<Flt64> model) =
            MakeAggregation(new List<CuttingPlan<Flt64>> { existing }, demands, material);

        CuttingPlan<Flt64> first = MakePlan("p2", material, productB);
        aggregation.AddColumns(UInt64.One, new List<CuttingPlan<Flt64>> { first }, model)
            .Value.Should().ContainSingle("p2 is genuinely new");

        CuttingPlan<Flt64> secondWithSameId = MakePlan("p2", material, productC);
        Result<IReadOnlyList<CuttingPlan<Flt64>>, ErrorCode, Error<ErrorCode>> second =
            aggregation.AddColumns(new UInt64(2), new List<CuttingPlan<Flt64>> { secondWithSameId }, model);

        second.IsOk.Should().BeTrue();
        second.Value.Should().BeEmpty("p2 was registered by the first call");
    }

    /// <summary>**真正的新列必须被接受** / A genuinely new column must be accepted.</summary>
    /// <remarks>
    /// **第三条用来证明前两条不是"一律拒绝"**：id 新、键也新的列必须进去，
    /// 否则前两条在"什么都拒"的实现里也会绿。
    ///
    /// **This proves the first two are not "reject everything"**: a column with a new id and a new key must be accepted, or the first two would also pass on an
    /// implementation that rejects everything.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldAcceptAGenuinelyNewColumn() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> productA = MakeProduct("A", 50.0);
        Product<Flt64> productB = MakeProduct("B", 60.0);
        var demands = new List<ProductDemand<Flt64>> {
            new(productA, Millimetres(50.0)),
            new(productB, Millimetres(60.0)),
        };
        CuttingPlan<Flt64> existing = MakePlan("p1", material, productA);
        (ProduceAggregation<Flt64> aggregation, IAbstractLinearMetaModel<Flt64> model) =
            MakeAggregation(new List<CuttingPlan<Flt64>> { existing }, demands, material);

        CuttingPlan<Flt64> fresh = MakePlan("p2", material, productB);
        Result<IReadOnlyList<CuttingPlan<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            aggregation.AddColumns(UInt64.One, new List<CuttingPlan<Flt64>> { fresh }, model);

        result.IsOk.Should().BeTrue();
        result.Value.Should().ContainSingle("both the id and the canonical key are new");
    }
}
