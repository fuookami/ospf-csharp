#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 候选排序 / Candidate ranking.
/// </summary>
/// <remarks>
/// **顺序只有一条规则：有效性排序存在时它优先，否则退到漏斗短名单。**
/// **有效性优先是有意的**——漏斗只按对偶阈值分档，**而有效性是实测出来的。**
///
/// **One rule orders everything: the effectiveness ranking wins when present, otherwise the funnel shortlist.**
/// **Effectiveness first is deliberate** — the funnel only buckets by dual threshold, **while effectiveness is measured.**
/// </remarks>
public class RankConstraintCandidatesTests {
    private static ConstraintId Id(string value) => new(value);

    private static EffectivenessRanking Ranking(params string[] ids) {
        EffectivenessEntry[] entries = ids
            .Select(id => new EffectivenessEntry(
                ConstraintId: Id(id),
                Group: null,
                Tier: CandidateTier.TierA,
                Activity: ActivityStatus.Active,
                DualValue: null,
                LocalEffective: null,
                RemovalEffective: null,
                FirstEffectiveDelta: null,
                MaxImprovement: null,
                MarginalEffect: null,
                Status: AnalysisStatus.Reachable))
            .ToArray();
        return new EffectivenessRanking(new Flt64(0.0), entries);
    }

    private static CandidateFunnelRanking Funnel(params (string Name, CandidateTier Tier)[] candidates) {
        ConstraintCandidate[] built = candidates
            .Select((candidate, index) => new ConstraintCandidate(
                Id(candidate.Name), null, candidate.Tier, ActivityStatus.Active, null, null, null, index))
            .ToArray();
        return new CandidateFunnelRanking(0.0, built, 0, 0, 0, 0);
    }

    /// <summary>**有效性排序存在时它优先** / The effectiveness ranking wins.</summary>
    /// <remarks>
    /// **反向对照**：漏斗里放一个不同的名字，**如果实现读错了来源，这条立刻红。**
    ///
    /// **The reverse control**: the funnel holds different names, **so a wrong source shows up immediately.**
    /// </remarks>
    [Fact]
    public void TheEffectivenessRanking_ShouldOutrankTheFunnel() {
        IReadOnlyList<ConstraintId> order = CriticalPipelineStages.RankConstraintCandidates(
            Ranking("measured:a", "measured:b"),
            Funnel(("funnel:x", CandidateTier.TierA)));

        order.Should().Equal(new[] { Id("measured:a"), Id("measured:b") },
            "a measured order beats a threshold bucket");
    }

    /// <summary>**没有有效性排序时退到漏斗短名单** / Without one, the funnel shortlist is used.</summary>
    [Fact]
    public void TheMissingRanking_ShouldFallBackToTheFunnel() {
        IReadOnlyList<ConstraintId> order = CriticalPipelineStages.RankConstraintCandidates(
            ranking: null,
            Funnel(("funnel:x", CandidateTier.TierA)));

        order.Should().Equal(Id("funnel:x"));
    }

    /// <summary>**两者都没有就是空** / Neither means empty.</summary>
    /// <remarks>
    /// **反向对照**：只测前两条会让"随便返回点什么"通过。
    /// **Neither case above covers "no input at all", and that is the one that must not invent an order.**
    /// </remarks>
    [Fact]
    public void TheMissingBoth_ShouldReturnEmpty() {
        CriticalPipelineStages.RankConstraintCandidates(null, null)
            .Should().BeEmpty("with no source there is no order to give");
    }

    /// <summary>**上限小于等于 0 返回空，不是"不设上限"** / A non-positive limit is empty, not unlimited.</summary>
    /// <remarks>
    /// **这是最容易被写成反义的一条**：`limit <= 0` 直觉上像"不限"，
    /// **而语义是"不要任何候选"——写成不限会让上游的 0 变成全量分析。**
    ///
    /// **This is the one most easily inverted**: `limit <= 0` reads like "no cap", **while the code means "no candidates at all" — inverting it turns an upstream zero into a full analysis.**
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void TheNonPositiveLimit_ShouldReturnEmpty(int limit) {
        CriticalPipelineStages.RankConstraintCandidates(Ranking("measured:a", "measured:b"), null, limit)
            .Should().BeEmpty("a non-positive limit means no candidates, not every candidate");

        // **漏斗那条路必须一起查。** `Take(-3)` 本身就返回空，
        // **所以只测有效性那一路的话，"负上限没被挡住"永远看不出来**——
        // **而漏斗的 `Shortlist(-3)` 有自己的一道 `limit <= 0` 门，走进去才知道挡没挡。**
        // **The funnel path has to be checked too.** `Take(-3)` returns empty by itself, **so testing only the effectiveness path can never show a negative limit slipping through** — **and the funnel Shortlist has its own
        // guard, so only going through it tells you whether the outer one exists.**
        CriticalPipelineStages.RankConstraintCandidates(
                null,
                Funnel(("funnel:x", CandidateTier.TierA)),
                limit)
            .Should().BeEmpty("the funnel path needs the same guard, not just its own");
    }

    /// <summary>**上限真的截断** / The limit truncates.</summary>
    [Fact]
    public void TheLimit_ShouldTruncateBothSources() {
        CriticalPipelineStages.RankConstraintCandidates(Ranking("a", "b", "c"), null, 2)
            .Should().HaveCount(2);
        CriticalPipelineStages.RankConstraintCandidates(Ranking("a", "b", "c"), null, 2)
            .Should().Equal(new[] { Id("a"), Id("b") }, "the first two in the given order");

        CriticalPipelineStages.RankConstraintCandidates(
                null,
                Funnel(("a", CandidateTier.TierA), ("b", CandidateTier.TierA), ("c", CandidateTier.TierA)),
                2)
            .Should().HaveCount(2, "the funnel path honours the same limit");
    }
}
