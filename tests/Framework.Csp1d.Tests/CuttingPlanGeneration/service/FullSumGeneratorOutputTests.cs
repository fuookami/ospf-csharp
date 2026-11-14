// FullSum 生成器的真实产出测试 / Tests for FullSum's actual plan output.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.Service;

/// <summary>
/// `FullSumGenerator` 必须真的搜索可重复多刀的组合，而不只是构造一个收集器。
/// `FullSumGenerator` must really search combinations that may repeat a width rather than merely construct a collector.
/// </summary>
/// <remarks>
/// B307 之前它的方法体只有一行注释：`// Stub: FullSum search implementation deferred`。
///
/// **本批的核心判据是"同一宽度可以多刀"**：这是 FullSum 与 `NSum` 的**唯一语义差别**。
/// 若实现退化成"每个入口只取一次"（即 `NSum` 的语义），方案条数会**看起来完全合理**而语义错了。
///
/// Before B307 its body was a single comment: `// Stub: FullSum search implementation deferred`.
///
/// **The core criterion is "one width may be cut several times"**: that is FullSum's **only semantic difference** from `NSum`. If the implementation
/// degenerated to "take each entry once" (i.e. `NSum`'s semantics) the plan count would **look entirely plausible** while the semantics were wrong.
/// </remarks>
public class FullSumGeneratorOutputTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial(double upperBound = 200.0) =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(upperBound)
                ),
                Step: Millimetres(1.0)
            )
        );

    private static Product<Flt64> MakeProduct(string id, double width) =>
        new(
            id: id,
            name: "product-" + id,
            width: new List<Quantity<Flt64>> { Millimetres(width) }
        );

    private static GenerationInput<Flt64> MakeInput(params (string Id, double Width)[] products) =>
        new() {
            Materials = new List<Material<Flt64>> { MakeMaterial() },
            Demands = products
                .Select(product => new ProductDemand<Flt64>(
                    MakeProduct(product.Id, product.Width),
                    Millimetres(product.Width)))
                .ToList(),
        };

    /// <summary>**空输入必须返回空** / Empty input must yield nothing.</summary>
    [Fact]
    public void EmptyInput_ShouldReturnEmpty() {
        var generator = new FullSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        generator.Generate(new GenerationInput<Flt64>()).Should().BeEmpty();
    }

    /// <summary>**同一宽度必须能出多刀** / One width must be cut several times.</summary>
    /// <remarks>
    /// 幅宽上界 200mm，产品 50mm。**`amount = 4` 的切片是 FullSum 独有的形态**：
    /// 200mm 恰好被四个 50mm 装满。若实现退化成每个入口只取一次，这条会失败，
    /// **而其余方案的条数仍然"合理"**。
    ///
    /// The bound is 200mm, the product 50mm. **A slice with `amount = 4` is FullSum's signature**: 200mm is exactly filled by four 50mm cuts. If the
    /// implementation took each entry once this fails, **while the remaining plan count still looks plausible**.
    /// </remarks>
    [Fact]
    public void OneProduct_ShouldBeCutSeveralTimes() {
        var generator = new FullSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput(("A", 50.0)));

        plans.SelectMany(plan => plan.Slices)
            .Should().Contain(slice => slice.Amount == new UInt64(4UL));
    }

    /// <summary>**装满的组合必须真的装满** / A full combination must actually fill the material.</summary>
    /// <remarks>
    /// 四个 50mm 恰好 200mm。**若搜索把"数量"重复加到宽度里但忘了算进 `usedWidth`，
    /// 会产出"标签写 4 刀、实际只算 1 刀"的方案**——那种方案的总宽看起来只有 50mm，
    /// 而它会在求解阶段被当成装不满的方案。
    ///
    /// Four 50mm is exactly 200mm. **If the search repeats the width for the label but forgets to fold it into `usedWidth`, it emits plans that say four cuts
    /// while accounting for one** — such a plan's total width looks like 50mm and gets treated as under-filled at solving time.
    /// </remarks>
    [Fact]
    public void FullCombination_ShouldReachTheMaterialBound() {
        var generator = new FullSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput(("A", 50.0)));

        plans.Should().Contain(
            plan => plan.Slices.Sum(slice => slice.Width.Value.ToDouble() * slice.Amount.ToULong()) >= 200.0 - 1e-9);
    }

    /// <summary>**总宽不得超过幅宽** / The total width must not exceed the material.</summary>
    /// <remarks>
    /// 把 `amount` 也计入宽度后重算：**若数量被计入 `usedWidth` 两次，或没有上限约束，就会出现超宽方案**。
    ///
    /// Recomputing with `amount` folded into the width: **folding the amount into `usedWidth` twice, or missing an upper bound, produces over-wide plans**.
    /// </remarks>
    [Fact]
    public void EveryPlanTotalWidth_ShouldFitTheMaterial() {
        var generator = new FullSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(
            MakeInput(("A", 50.0), ("B", 60.0), ("C", 70.0)));

        plans.Should().NotBeEmpty();
        foreach (CuttingPlan<Flt64> plan in plans) {
            double total = plan.Slices.Sum(slice => slice.Width.Value.ToDouble() * slice.Amount.ToULong());
            total.Should().BeLessThanOrEqualTo(200.0, "plan {0} exceeds the material width", plan.Id);
        }
    }

    /// <summary>**方案 ID 必须互不相同** / Plan ids must be distinct.</summary>
    [Fact]
    public void PlanIds_ShouldBeDistinct() {
        var generator = new FullSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(
            MakeInput(("A", 50.0), ("B", 60.0)));

        plans.Select(plan => plan.Id).Distinct().Should().HaveCount(plans.Count);
    }

    /// <summary>**统计必须反映真实产出** / The statistics must reflect the real output.</summary>
    [Fact]
    public void Report_ShouldCountTheEmittedPlans() {
        var generator = new FullSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        CuttingPlanGenerationReport<CuttingPlan<Flt64>> report = generator.GenerateWithReport(
            MakeInput(("A", 50.0), ("B", 60.0)));

        report.Statistics.AcceptedPlans.Should().Be(report.Plans.Count);
        report.Statistics.AcceptedPlans.Should().BePositive();
    }

    /// <summary>**同一组合只能被枚举一次** / Each combination must be enumerated exactly once.</summary>
    /// <remarks>
    /// 与 `NSum` 同样的形态：候选下标或数量枚举若重复，**去重会把重复静默吸收**，
    /// 方案集合完全一样，只有候选计数会暴露。
    ///
    /// The same shape as in `NSum`: if the entry index or amount enumeration repeats, **de-duplication silently absorbs the duplicates**, the plan set stays
    /// identical, and only the candidate counter gives it away.
    /// </remarks>
    [Fact]
    public void Candidates_ShouldNotExceedAcceptedPlans() {
        var generator = new FullSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        CuttingPlanGenerationReport<CuttingPlan<Flt64>> report = generator.GenerateWithReport(
            MakeInput(("A", 50.0), ("B", 60.0), ("C", 70.0)));

        report.Statistics.AcceptedPlans.Should().BePositive();
        report.Statistics.GeneratedCandidates.Should().Be(
            report.Statistics.AcceptedPlans,
            "a repeated enumeration would be hidden by de-duplication");
    }

    /// <summary>**单刀方案也必须产出** / Single-cut plans must be produced too.</summary>
    [Fact]
    public void SingleCut_ShouldStillProduceAPlan() {
        var generator = new FullSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput(("A", 50.0), ("B", 60.0)));

        plans.Should().Contain(plan => plan.Slices.Count == 1);
    }

    /// <summary>**多宽度的组合也必须产出** / Combinations across several widths must be produced.</summary>
    [Fact]
    public void SeveralWidths_ShouldProduceACombinedPlan() {
        var generator = new FullSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(
            MakeInput(("A", 50.0), ("B", 60.0), ("C", 70.0)));

        plans.Should().Contain(plan => plan.Slices.Count >= 2);
    }
}
