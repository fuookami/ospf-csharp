#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 阈值二分 / The threshold bisection.
/// </summary>
/// <remarks>
/// **一次没测出结论的探测，若被当成"无效"，报告会声称"这个 delta 以下确认无效果"——而那一半根本没测过。**
///
/// **Treating an inconclusive probe as "no effect" makes the report claim "confirmed no effect below this delta" about a range that was never measured.**
/// </remarks>
public class ThresholdRefinementTests {
    /// <summary>**二分把区间收紧到最小有效 delta** / The bisection narrows to the smallest effective delta.</summary>
    /// <remarks>
    /// 效果从 delta ≥ 5 开始。区间 [0, 16]，四次二分后上界必须落到 5 附近，**而下界必须始终是"确实测过且无效"的点。**
    ///
    /// The effect starts at delta &gt;= 5. From [0, 16], four bisections have to bring the upper bound down to around 5, **and the lower bound must always be a point that really
    /// was measured and really showed nothing.**
    /// </remarks>
    [Fact]
    public void TheBisection_ShouldNarrowToTheThreshold() {
        var probed = new List<double>();
        AdaptivePerturbationResult result = ConstraintPerturbationSupport.RefineThreshold(
            new Flt64(0.0), new Flt64(16.0), iterations: 6,
            probe: delta => {
                probed.Add(delta.ToDouble());
                return delta.ToDouble() >= 5.0
                    ? PerturbationOutcome.Effective
                    : PerturbationOutcome.NoObservedEffect;
            });

        probed.Should().NotBeEmpty();
        result.UpperBound!.Value.ToDouble().Should().BeGreaterThanOrEqualTo(5.0,
            "the upper bound is a delta that was actually effective");
        result.LowerBound!.Value.ToDouble().Should().BeLessThan(5.0,
            "the lower bound is a delta that was actually measured and showed nothing");
        result.Attempts.Should().Be(probed.Count, "every probe is counted");
    }

    /// <summary>**没结论就停，不往任何一边推** / An inconclusive probe stops the search.</summary>
    /// <remarks>
    /// 第一次探测就 `Unknown`。**若把它当成 `NoObservedEffect`，下界会被推到 8，报告会声称"8 以下确认无效"——而 8 这个点根本没测出结论。**
    ///
    /// The first probe is `Unknown`. **Treating it as `NoObservedEffect` would push the lower bound to 8 and claim "confirmed no effect below 8" — about a point that measured
    /// nothing.**
    /// </remarks>
    [Fact]
    public void TheInconclusiveProbe_ShouldStopWithoutMovingAnyBound() {
        AdaptivePerturbationResult result = ConstraintPerturbationSupport.RefineThreshold(
            new Flt64(0.0), new Flt64(16.0), iterations: 6,
            probe: _ => PerturbationOutcome.Unknown);

        result.Attempts.Should().Be(1, "one inconclusive probe is enough to stop");
        result.LowerBound!.Value.ToDouble().Should().Be(0.0, "nothing was proven ineffective");
        result.UpperBound!.Value.ToDouble().Should().Be(16.0, "nothing was proven effective");
        result.Outcome.Should().Be(AdaptivePerturbationOutcome.UnknownDueToBudget,
            "the interval never moved, so no adaptive conclusion was reached");
        result.FirstEffectiveDelta.Should().BeNull("no effective delta was ever observed");
    }

    /// <summary>**整数中点卡住时必须退出** / An integer midpoint that cannot move must end the loop.</summary>
    /// <remarks>
    /// 区间 [0, 1]：中点是 0，落在下界上，**再探一次也是 0**。不退出的话，次数耗尽而区间没变，
    /// 报告会带着一个"已经收敛"的外观。
    ///
    /// With [0, 1] the midpoint is 0, which sits on the lower bound, **and probing again gives 0**. Without the guard the iterations burn out and the interval never moves, while
    /// the report looks as if it converged.
    /// </remarks>
    [Fact]
    public void TheStuckMidpoint_ShouldEndTheLoop() {
        int calls = 0;
        AdaptivePerturbationResult result = ConstraintPerturbationSupport.RefineThreshold(
            new Flt64(0.0), new Flt64(1.0), iterations: 8,
            probe: _ => {
                calls++;
                return PerturbationOutcome.NoObservedEffect;
            });

        calls.Should().Be(0, "there is no integer strictly between 0 and 1, so a probe would be wasted");
        result.Attempts.Should().Be(0);
        result.Outcome.Should().Be(AdaptivePerturbationOutcome.UnknownDueToBudget);
    }

    /// <summary>**次数用完时如实说没有收敛** / Running out of iterations is reported honestly.</summary>
    [Fact]
    public void TheExhaustedIterations_ShouldNotClaimConvergence() {
        AdaptivePerturbationResult result = ConstraintPerturbationSupport.RefineThreshold(
            new Flt64(0.0), new Flt64(1000.0), iterations: 2,
            probe: delta => delta.ToDouble() > 900.0
                ? PerturbationOutcome.Effective
                : PerturbationOutcome.NoObservedEffect);

        result.Attempts.Should().Be(2);
        result.Outcome.Should().Be(AdaptivePerturbationOutcome.ThresholdInterval,
            "the interval is still wide, so only an interval can be reported");
        result.FirstEffectiveDelta.Should().BeNull("nothing is claimed to be the first effective delta");
        (result.UpperBound!.Value.ToDouble() - result.LowerBound!.Value.ToDouble())
            .Should().BeGreaterThan(1.0, "a wide interval is exactly what ThresholdInterval means");
    }

    /// <summary>**收敛后给出首次有效 delta** / A converged search names the first effective delta.</summary>
    [Fact]
    public void TheConvergedSearch_ShouldNameTheFirstEffectiveDelta() {
        AdaptivePerturbationResult result = ConstraintPerturbationSupport.RefineThreshold(
            new Flt64(10.0), new Flt64(12.0), iterations: 8,
            probe: delta => delta.ToDouble() >= 11.0
                ? PerturbationOutcome.Effective
                : PerturbationOutcome.NoObservedEffect);

        // 10 无效、12 有效 → 中点 11 有效 → 上界落到 11 → 区间 [10, 11] 宽 1 → 收敛。
        result.Outcome.Should().Be(AdaptivePerturbationOutcome.FirstEffective);
        result.FirstEffectiveDelta!.Value.ToDouble().Should().Be(11.0);
        result.LowerBound!.Value.ToDouble().Should().Be(10.0);
        result.UpperBound!.Value.ToDouble().Should().Be(11.0);
    }
}
