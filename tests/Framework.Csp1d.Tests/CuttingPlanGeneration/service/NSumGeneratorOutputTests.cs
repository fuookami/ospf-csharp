// NSum 生成器的真实产出测试 / Tests for NSum's actual plan output.

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
/// `NSumGenerator` 必须真的做深度优先搜索，而不只是构造一个收集器。
/// `NSumGenerator` must really run a depth-first search rather than merely construct a collector.
/// </summary>
/// <remarks>
/// 之前它的方法体只有一行注释：`// Stub: NSum search implementation deferred`。
/// 与 `NSame` 相同，既有测试只断言空输入返回空。
///
/// **这里的核心判据是"组合"**：NSum 的语义是**多个不同宽度、总和不超过幅宽**。
/// 若实现退化成"每个宽度单独一条"（即 NSame 的语义），条数会**看起来合理**而语义完全错了。
///
/// Before its body was a single comment: `// Stub: NSum search implementation deferred`. As with `NSame`, the existing test only asserted empty input
/// yields empty output.
///
/// **The core criterion here is "combinations"**: NSum's semantics is **several distinct widths whose total fits the material**. If the implementation
/// degenerated to "one plan per width" (i.e. NSame's semantics), the count would **look plausible** while the semantics were entirely wrong.
/// </remarks>
public class NSumGeneratorOutputTests {
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
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        generator.Generate(new GenerationInput<Flt64>()).Should().BeEmpty();
    }

    /// <summary>**两个能一起装的宽度必须产生一条双切片方案** / Two widths that fit together must produce a two-slice plan.</summary>
    /// <remarks>
    /// 幅宽上界 200mm，两个 50mm 宽度的产品**合计 100mm**，装得下。
    /// 若实现只产出单切片方案，这条会失败——**而单切片方案的条数同样"合理"**。
    ///
    /// The upper bound is 200mm; two 50mm products total **100mm** and fit. If the implementation emitted only single-slice plans this fails — **while the
    /// single-slice count looks equally plausible**.
    /// </remarks>
    [Fact]
    public void TwoNarrowProducts_ShouldProduceACombinedPlan() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(
            MakeInput(("A", 50.0), ("B", 50.0)));

        plans.Should().Contain(plan => plan.Slices.Count == 2);
    }

    /// <summary>**单切片方案也必须产出** / Single-slice plans must be produced too.</summary>
    [Fact]
    public void SingleProduct_ShouldStillProduceAPlan() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput(("A", 50.0)));

        plans.Should().Contain(plan => plan.Slices.Count == 1);
    }

    /// <summary>**总和超过幅宽的组不得产出** / A combination exceeding the material must not be emitted.</summary>
    /// <remarks>
    /// 三个 100mm 宽度合计 300mm，超过 200mm 上界。**任何三切片方案都是物理上不可能的**。
    ///
    /// Three 100mm widths total 300mm, exceeding the 200mm bound. **Any three-slice plan would be physically impossible.**
    /// </remarks>
    [Fact]
    public void CombinationExceedingWidth_ShouldNotBeEmitted() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(
            MakeInput(("A", 100.0), ("B", 100.0), ("C", 100.0)));

        plans.Should().NotContain(plan => plan.Slices.Count >= 3);
    }

    /// <summary>**方案 ID 必须互不相同** / Plan ids must be distinct.</summary>
    [Fact]
    public void PlanIds_ShouldBeDistinct() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(
            MakeInput(("A", 50.0), ("B", 50.0), ("C", 60.0)));

        plans.Select(plan => plan.Id).Distinct().Should().HaveCount(plans.Count);
    }

    /// <summary>**统计必须反映真实产出** / The statistics must reflect the real output.</summary>
    [Fact]
    public void Report_ShouldCountTheEmittedPlans() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        CuttingPlanGenerationReport<CuttingPlan<Flt64>> report = generator.GenerateWithReport(
            MakeInput(("A", 50.0), ("B", 50.0)));

        report.Statistics.AcceptedPlans.Should().Be(report.Plans.Count);
        report.Statistics.AcceptedPlans.Should().BePositive();
        report.Statistics.VisitedNodes.Should().BePositive();
    }

    /// <summary>**每条方案的总宽不得超过幅宽** / Every plan's total width must stay within the material.</summary>
    /// <remarks>
    /// 这是**最重要的正确性判据**：搜索若漏掉剩余宽度检查，会产出超宽的方案——
    /// 而那些方案**看起来完全正常**，只有在求解阶段才会暴露。
    ///
    /// This is **the most important correctness criterion**: a search that misses the remaining-width check emits over-wide plans — and those **look perfectly
    /// normal**, surfacing only at the solving stage.
    /// </remarks>
    [Fact]
    public void EveryPlanTotalWidth_ShouldFitTheMaterial() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(
            MakeInput(("A", 50.0), ("B", 50.0), ("C", 60.0), ("D", 70.0)));

        plans.Should().NotBeEmpty();
        foreach (CuttingPlan<Flt64> plan in plans) {
            double total = plan.Slices.Sum(slice => slice.Width.Value.ToDouble());
            total.Should().BeLessThanOrEqualTo(200.0, "plan {0} exceeds the material width", plan.Id);
        }
    }

    /// <summary>**同一组合只能被枚举一次** / Each combination must be enumerated exactly once.</summary>
    /// <remarks>
    /// 这条测试是这里**唯一能抓住"候选下标不单调"的判据**。
    ///
    /// 前一条测试断言"切片的产品集合互不相同"，但**那条判据抓不到缺陷**：候选下标不单调时，
    /// 搜索会把 {A,B} 枚举成 `[A,B]` 与 `[B,A]` 两次，**而去重按内容判等，第二次被静默丢掉**——
    /// 方案集合完全一样，`Plans` 的条数也完全一样。
    ///
    /// 唯一的痕迹在**候选计数**：`GeneratedCandidates` 会明显大于 `AcceptedPlans`。
    /// **这正是"去重把缺陷吸收了"的形态**，判据必须落在去重之前的那一层。
    ///
    /// This is the **only criterion in that catches a non-monotonic candidate index**.
    ///
    /// The preceding test asserts the plans' product sets are distinct, but **that criterion cannot catch the defect**: with a non-monotonic index the search
    /// enumerates {A,B} twice as `[A,B]` and `[B,A]`, **and de-duplication compares by content so the second is silently dropped** — the plan set is identical
    /// and so is `Plans.Count`.
    ///
    /// The only trace is **the candidate counter**: `GeneratedCandidates` rises well above `AcceptedPlans`. **This is exactly the "de-duplication absorbed the
    /// defect" shape**, so the criterion must sit on the layer before de-duplication.
    /// </remarks>
    [Fact]
    public void Candidates_ShouldNotExceedAcceptedPlans() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        CuttingPlanGenerationReport<CuttingPlan<Flt64>> report = generator.GenerateWithReport(
            MakeInput(("A", 50.0), ("B", 60.0), ("C", 70.0)));

        report.Statistics.AcceptedPlans.Should().BePositive();
        report.Statistics.GeneratedCandidates.Should().Be(
            report.Statistics.AcceptedPlans,
            "a non-monotonic candidate index re-enumerates permutations that de-duplication then hides");
    }

    /// <summary>**同一组切片不得以不同顺序重复出现** / The same slice set must not reappear in a different order.</summary>
    /// <remarks>
    /// 搜索用"候选下标单调不减"避免重复枚举。若这一点写错，会产生大量重复方案——
    /// **而去重会静默吸收它们，使"方案数看起来正常"**。所以这里断言**切片的产品集合互不相同**。
    ///
    /// The search keeps candidate indices monotonic to avoid re-enumerating. Getting this wrong floods duplicates — **and de-duplication silently absorbs them,
    /// making "the plan count look normal"**. So this asserts the plans' product sets are mutually distinct.
    /// </remarks>
    [Fact]
    public void Plans_ShouldNotRepeatTheSameProductSet() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(
            MakeInput(("A", 50.0), ("B", 60.0), ("C", 70.0)));

        var signatures = plans
            .Select(plan => string.Join(",", plan.Slices
                .Select(slice => slice.Production is Product<Flt64> product ? product.Id : "?")
                .OrderBy(id => id, System.StringComparer.Ordinal)))
            .ToList();

        signatures.Should().OnlyHaveUniqueItems();
    }
}
