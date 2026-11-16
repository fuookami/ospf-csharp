#nullable enable

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

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 跨贡献剪枝必须**在真实生成路径上真的发生**，而不只是类存在。
/// Cross-contribution pruning must **actually happen on a real generation path**, not merely exist as a class.
/// </summary>
/// <remarks>
/// 写了判定、 写了投影器，但 **`GenerationCollector` 的 `crossContributionProjector` 参数
/// 从来没有被任何生成器传入过**——于是跨贡献剪枝在真实运行中**从不发生**，
/// 而策略枚举、计数器、判定函数全都在，看起来一切正常。
///
/// 用 `CrossContributionProjector.ForGenerator<V>` 把四个生成器全部接上。
///
/// **本文件的判据是差分**：同一次生成，只切换"策略 + 剪枝开关"，产出条数必须不同。
/// 若投影器没接上，两种配置的产出**完全一样**——而任何单边断言（比如"产出非空"）都不会失败。
///
/// wrote the decision and the projector, but **no generator ever passed `GenerationCollector`'s `crossContributionProjector`** — so cross-contribution
/// pruning **never happened in a real run**, while the strategy enum, the counters and the decision function were all present and looked fine.
///
/// wires all four generators through `CrossContributionProjector.ForGenerator<V>`.
///
/// **The criterion here is differential**: in one generation, toggling only "strategy plus pruning switch" must change the accepted count. If the projector were not
/// wired, both configurations would produce **identical results** — and any one-sided assertion (such as "output is non-empty") would never fail.
/// </remarks>
public class CrossContributionWiringTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static GenerationInput<Flt64> MakeInput() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)
                ),
                Step: Millimetres(1.0)
            )
        );
        var a = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) }
        );
        var b = new Product<Flt64>(
            id: "B",
            name: "product-b",
            width: new List<Quantity<Flt64>> { Millimetres(60.0) }
        );
        return new GenerationInput<Flt64> {
            Materials = new List<Material<Flt64>> { material },
            Demands = new List<ProductDemand<Flt64>> {
                new(a, Millimetres(50.0)),
                new(b, Millimetres(60.0)),
            },
        };
    }

    /// <summary>
    /// 单一产品、两个宽度候选的输入 / Input with one product carrying two width candidates.
    /// </summary>
    /// <remarks>
    /// **这个输入是给 `NSame` 用的，而且不能省。** `NSame` 对每个"需求 × 宽度"只产出一条单切片方案，
    /// 若两个宽度属于**不同产品**，两条方案的贡献互不可比（A 不支配 B），**支配判定永远不会命中**。
    ///
    /// 同一产品的 60mm 与 50mm 才有可比性：60mm 方案余宽更小、贡献更大，**支配 50mm 方案**。
    ///
    /// **顺序不能反**：收集器的语义是"**已有方案支配新候选**时拒绝新候选"，
    /// 所以必须让**更优的 60mm 先到**，50mm 后到时才会被剪掉。若 50mm 先到，60mm 后到，
    /// 两者互不支配（60mm 不"被"50mm 支配），**两条都会留下**——这一条我在这里实际踩过。
    ///
    /// **This input exists for `NSame` and cannot be dropped.** `NSame` emits one single-slice plan per "demand × width"; if the two widths belong to **different
    /// products** their contributions are incomparable (A does not dominate B) and **the dominance decision never fires**.
    ///
    /// One product's 50mm and 60mm are comparable: the 60mm plan has less rest width and a larger contribution, so it **dominates** the 50mm plan.
    /// </remarks>
    private static GenerationInput<Flt64> MakeSingleProductTwoWidthsInput() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)
                ),
                Step: Millimetres(1.0)
            )
        );
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(60.0), Millimetres(50.0) }
        );
        return new GenerationInput<Flt64> {
            Materials = new List<Material<Flt64>> { material },
            Demands = new List<ProductDemand<Flt64>> { new(product, Millimetres(50.0)) },
        };
    }

    /// <summary>**开启跨贡献剪枝后产出必须减少** / Turning cross-contribution pruning on must reduce the output.</summary>
    /// <remarks>
    /// 关掉时收集器只做去重；打开时额外做跨贡献支配判定。
    /// **若投影器没接上，判定块永远不会执行，两次产出完全相同。**
    ///
    /// With pruning off the collector only de-duplicates; with it on it additionally runs the cross-contribution dominance decision. **If the projector were not
    /// wired the decision block would never execute and both runs would be identical.**
    /// </remarks>
    [Fact]
    public void CrossContributionPruning_ShouldReduceTheAcceptedPlans() {
        IReadOnlyList<ICuttingPlanConstraint<Flt64>> constraints = GenerationConstraints<Flt64>.Unconstrained().ToConstraints();
        var off = new FullSumGenerator<Flt64>(constraints);
        var on = new FullSumGenerator<Flt64>(
            constraints,
            enableDominancePruning: true,
            dominanceStrategy: DominanceStrategy.CrossContribution);

        CuttingPlanGenerationReport<CuttingPlan<Flt64>> offReport = off.GenerateWithReport(MakeInput());
        CuttingPlanGenerationReport<CuttingPlan<Flt64>> onReport = on.GenerateWithReport(MakeInput());

        offReport.Statistics.AcceptedPlans.Should().BePositive();
        onReport.Statistics.AcceptedPlans.Should().BeLessThan(
            offReport.Statistics.AcceptedPlans,
            "cross-contribution pruning must actually reject some candidates");
    }

    /// <summary>**被剪掉的候选必须真的被计数** / Pruned candidates must actually be counted.</summary>
    /// <remarks>
    /// 差分只能说明"少了"，这条说明"少的原因是被支配剪枝"，而不是别的统计口径差异。
    ///
    /// The differential only shows "fewer"; this shows **the reason is dominance pruning** rather than some other accounting difference.
    /// </remarks>
    [Fact]
    public void CrossContributionPruning_ShouldCountDominatedCandidates() {
        var generator = new FullSumGenerator<Flt64>(
            GenerationConstraints<Flt64>.Unconstrained().ToConstraints(),
            enableDominancePruning: true,
            dominanceStrategy: DominanceStrategy.CrossContribution);

        CuttingPlanGenerationReport<CuttingPlan<Flt64>> report = generator.GenerateWithReport(MakeInput());

        report.Statistics.AcceptedPlans.Should().BePositive();
        report.Statistics.CrossContributionDominated.Should().BePositive(
            "a non-zero dominance count is the trace that the decision block ran");
    }

    /// <summary>**其余三个生成器也必须接上投影器** / The other three generators must be wired too.</summary>
    /// <remarks>
    /// 只接一个生成器是不够的——**这正是本项目反复出现的形态**：修好一条路径，
    /// 同形态的其余三条保持原样。这条把四个生成器一起钉住。
    ///
    /// Wiring one generator is not enough — **this is the recurring shape in this project**: fix one path and leave the three siblings untouched. This pins all four
    /// together.
    /// </remarks>
    [Fact]
    public void AllFourGenerators_ShouldPruneUnderCrossContribution() {
        IReadOnlyList<ICuttingPlanConstraint<Flt64>> constraints = GenerationConstraints<Flt64>.Unconstrained().ToConstraints();
        var generators = new List<(string Name, ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> Off, ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> On)> {
            (
                "NSame",
                new NSameGenerator<Flt64>(constraints),
                new NSameGenerator<Flt64>(constraints, enableDominancePruning: true, dominanceStrategy: DominanceStrategy.CrossContribution)),
            (
                "NSum",
                new NSumGenerator<Flt64>(constraints),
                new NSumGenerator<Flt64>(constraints, enableDominancePruning: true, dominanceStrategy: DominanceStrategy.CrossContribution)),
            (
                "FullSum",
                new FullSumGenerator<Flt64>(constraints),
                new FullSumGenerator<Flt64>(constraints, enableDominancePruning: true, dominanceStrategy: DominanceStrategy.CrossContribution)),
            (
                "DFS",
                new DFSGenerator<Flt64>(constraints),
                new DFSGenerator<Flt64>(constraints, enableDominancePruning: true, dominanceStrategy: DominanceStrategy.CrossContribution)),
        };

        foreach ((string name, ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> off,
                 ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> on) in generators) {
            GenerationInput<Flt64> input = MakeSingleProductTwoWidthsInput();
            long offCount = off.GenerateWithReport(input).Statistics.AcceptedPlans;
            long onCount = on.GenerateWithReport(input).Statistics.AcceptedPlans;
            offCount.Should().BePositive("{0} should produce plans", name);
            onCount.Should().BeLessThan(offCount, "{0} must apply cross-contribution pruning", name);
        }
    }
}
