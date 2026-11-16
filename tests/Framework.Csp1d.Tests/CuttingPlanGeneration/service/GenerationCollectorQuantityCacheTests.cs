// 生成收集器的统计必须真的把数量缓存计数带进报告
// The generation collector's report must really carry the quantity-cache counts.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.Service;

/// <summary>
/// <see cref="GenerationCollector{TPlan}"/> 的 <see cref="GenerationCollector{TPlan}.Report"/> 必须把
/// **数量缓存命中/未命中**带进 <see cref="CuttingPlanGenerationStatistics"/>。
/// <see cref="GenerationCollector{TPlan}.Report"/> must carry the **quantity-cache hits and misses** into
/// <see cref="CuttingPlanGenerationStatistics"/>.
/// </summary>
/// <remarks>
/// 之前这两项**从来没被赋值**：`Report` 里没有它们，collector 里也没有对应字段。
/// 而下游是完整的——`Csp1dSolutionEnrichment` 读它们写进 KPI，`Csp1dColumnGeneration` 合并它们，
/// 记录类型里也有这两个参数。**整条管线都在等一个永远为 0 的值。**
///
/// 判据必须落在**具体数值**上，不能是"字段存在"——那两个参数本来就在，一直是 0。
///
/// Before these two were **never assigned**: `Report` did not include them and the collector had no fields for them. The downstream was complete —
/// `Csp1dSolutionEnrichment` reads them into KPIs, `Csp1dColumnGeneration` merges them, and the record type declares both parameters. **The whole pipeline
/// was waiting on a value that was always zero.**
///
/// The criterion must land on **specific numbers**, not "the field exists" — both parameters existed all along, at zero.
/// </remarks>
public class GenerationCollectorQuantityCacheTests {
    private static GenerationCollector<string> MakeCollector() =>
        new(
            maxPlans: 16,
            deadlineMs: null,
            enableDominancePruning: false,
            dominanceStrategy: DominanceStrategy.SameContribution,
            canonicalKeyResolver: null,
            dominanceAcceptOverride: null
        );

    /// <summary>**未记录时必须是 0，而不是别的数** / Before recording, both must be 0.</summary>
    [Fact]
    public void Report_ShouldStartAtZero() {
        GenerationCollector<string> collector = MakeCollector();

        CuttingPlanGenerationStatistics statistics = collector.Report();

        statistics.QuantityCacheHits.Should().Be(0);
        statistics.QuantityCacheMisses.Should().Be(0);
    }

    /// <summary>**记录的命中数必须出现在报告里** / Recorded hits must appear in the report.</summary>
    [Fact]
    public void Report_ShouldCarryQuantityCacheHits() {
        GenerationCollector<string> collector = MakeCollector();
        collector.RecordQuantityCacheHits(7);

        collector.Report().QuantityCacheHits.Should().Be(7);
    }

    /// <summary>**记录的未命中数必须出现在报告里** / Recorded misses must appear in the report.</summary>
    [Fact]
    public void Report_ShouldCarryQuantityCacheMisses() {
        GenerationCollector<string> collector = MakeCollector();
        collector.RecordQuantityCacheMisses(3);

        collector.Report().QuantityCacheMisses.Should().Be(3);
    }

    /// <summary>**多次记录必须累加** / Repeated recordings must accumulate.</summary>
    /// <remarks>
    /// 若记录方法写成赋值而不是累加，单次调用照样通过——**只有多次调用才区分得出来**。
    ///
    /// If the recording method assigned instead of accumulating, a single call would still pass — **only repeated calls tell them apart**.
    /// </remarks>
    [Fact]
    public void RecordQuantityCache_ShouldAccumulate() {
        GenerationCollector<string> collector = MakeCollector();
        collector.RecordQuantityCacheHits(2);
        collector.RecordQuantityCacheHits(5);
        collector.RecordQuantityCacheMisses(1);
        collector.RecordQuantityCacheMisses(4);

        CuttingPlanGenerationStatistics statistics = collector.Report();

        statistics.QuantityCacheHits.Should().Be(7);
        statistics.QuantityCacheMisses.Should().Be(5);
    }

    /// <summary>**跨贡献策略下被拒绝的候选记进跨贡献计数** / Under the cross-contribution strategy, a rejected candidate counts as cross-contribution.</summary>
    /// <remarks>
    /// 之前 `_dominanceStrategy` **只被赋值、从不被读取**，所以 `CrossContribution` 与 `SameContribution`
    /// 的行为完全相同——只有统计字段名不同，且两个都恒为 0。
    ///
    /// 这里的判据是**两个策略下两个计数器各是多少**：同一次拒绝，同贡献策略记 `DominatedCandidates`，
    /// 跨贡献策略记 `CrossContributionDominated`。**只断言"拒绝发生了"抓不到策略没生效。**
    ///
    /// Before `_dominanceStrategy` was **assigned but never read**, so `CrossContribution` and `SameContribution` behaved identically — only the statistics
    /// field names differed, and both were always zero.
    ///
    /// The criterion here is **what each of the two counters holds under each strategy**: for the same rejection, the same-contribution strategy records
    /// `DominatedCandidates` while the cross-contribution strategy records `CrossContributionDominated`. **Asserting only "a rejection happened" cannot catch
    /// a strategy that never took effect.**
    /// </remarks>
    [Fact]
    public void CrossContributionStrategy_ShouldRouteRejectionsToItsOwnCounter() {
        var sameContribution = new GenerationCollector<string>(
            maxPlans: 16,
            deadlineMs: null,
            enableDominancePruning: false,
            dominanceStrategy: DominanceStrategy.SameContribution,
            canonicalKeyResolver: null,
            dominanceAcceptOverride: (_, _) => false
        );
        var crossContribution = new GenerationCollector<string>(
            maxPlans: 16,
            deadlineMs: null,
            enableDominancePruning: false,
            dominanceStrategy: DominanceStrategy.CrossContribution,
            canonicalKeyResolver: null,
            dominanceAcceptOverride: (_, _) => false
        );

        sameContribution.Record("a", plan => new CuttingPlanCanonicalKey(plan), feasible: true);
        crossContribution.Record("b", plan => new CuttingPlanCanonicalKey(plan), feasible: true);

        sameContribution.Report().DominatedCandidates.Should().Be(1);
        sameContribution.Report().CrossContributionDominated.Should().Be(0);

        crossContribution.Report().CrossContributionDominated.Should().Be(1);
        crossContribution.Report().DominatedCandidates.Should().Be(0);
    }

    /// <summary>**跨贡献 dominance 计数必须真的进报告** / The cross-contribution count must reach the report.</summary>
    /// <remarks>
    /// 之前这个字段标着 `#pragma warning disable CS0649 // Intentionally unassigned: reserved for future use`——
    /// **编译器被告知"这个字段永远不会被赋值"，而那条 `pragma` 正是缺陷的痕迹**。
    ///
    /// Before this field carried `#pragma warning disable CS0649 // Intentionally unassigned: reserved for future use` — **the compiler was told
    /// "this field will never be assigned", and that `pragma` was itself the trace of the defect**.
    /// </remarks>
    [Fact]
    public void Report_ShouldCarryCrossContributionDominated() {
        GenerationCollector<string> collector = MakeCollector();
        collector.RecordCrossContributionDominated(3);
        collector.RecordCrossContributionDominated(2);

        collector.Report().CrossContributionDominated.Should().Be(5);
    }

    /// <summary>**默认参数是 1，不是 0** / The default argument is 1, not 0.</summary>
    /// <remarks>
    /// 这里每次剪枝只递增 1，所以默认参数必须是 1。若写成 0，**每次调用都"成功"但计数不动**，
    /// 而调用方看起来完全正常。
    ///
    /// The code increments by one per prune, so the default must be 1. If it were 0, **every call would "succeed" while the count never moves**, and the
    /// caller would look perfectly normal.
    /// </remarks>
    [Fact]
    public void RecordCrossContributionDominated_ShouldDefaultToOne() {
        GenerationCollector<string> collector = MakeCollector();
        collector.RecordCrossContributionDominated();

        collector.Report().CrossContributionDominated.Should().Be(1);
    }

    /// <summary>**dominance 策略必须被真的保存下来** / The dominance strategy must really be stored.</summary>
    /// <remarks>
    /// 这条测试防的是 发现的缺陷形态：**参数被存进字段、字段却从不被读取**。
    /// 只断言"构造不抛异常"是抓不到的。
    ///
    /// This guards the defect shape found: **the argument is stored into a field, and the field is never read**. Asserting only "the constructor does not
    /// throw" cannot catch it.
    /// </remarks>
    [Fact]
    public void DominanceStrategy_ShouldReflectTheConstructorArgument() {
        var sameContribution = new GenerationCollector<string>(
            maxPlans: 16,
            deadlineMs: null,
            enableDominancePruning: false,
            dominanceStrategy: DominanceStrategy.SameContribution,
            canonicalKeyResolver: null,
            dominanceAcceptOverride: null
        );
        var crossContribution = new GenerationCollector<string>(
            maxPlans: 16,
            deadlineMs: null,
            enableDominancePruning: false,
            dominanceStrategy: DominanceStrategy.CrossContribution,
            canonicalKeyResolver: null,
            dominanceAcceptOverride: null
        );

        sameContribution.DominanceStrategy.Should().Be(DominanceStrategy.SameContribution);
        crossContribution.DominanceStrategy.Should().Be(DominanceStrategy.CrossContribution);
        crossContribution.DominanceStrategy.Should().NotBe(sameContribution.DominanceStrategy);
    }

    /// <summary>**命中与未命中不得互相污染** / Hits and misses must not contaminate each other.</summary>
    [Fact]
    public void QuantityCacheHits_ShouldNotLeakIntoMisses() {
        GenerationCollector<string> collector = MakeCollector();
        collector.RecordQuantityCacheHits(9);

        CuttingPlanGenerationStatistics statistics = collector.Report();

        statistics.QuantityCacheHits.Should().Be(9);
        statistics.QuantityCacheMisses.Should().Be(0);
    }
}
