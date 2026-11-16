using System;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 质量守卫配置的合并与归一化 / The quality-guard merge and normalisation.
/// </summary>
/// <remarks>
/// 基线 `resolveQualityGuardConfig` 干两件事：**把 `null` 的字段用默认值补上**，然后**把五个风险权重归一化到和为 1**，
/// 其余九个标量各自 `coerce` 进合法区间。本仓这块逐行一致，**同样一条判据都没有**。
///
/// The baseline's resolver does two things: **fill `null` fields from the defaults**, then **normalise the five risk weights to sum to one**, with the other nine
/// scalars coerced into their valid ranges. This port matches line for line (checked) and again **had no criterion at all**.
///
/// **归一化为什么重要**：质量评分是"风险 × 权重"的和。权重没归一化，一个把 `gap` 权重填成 `3.5` 的调用方会**把评分整体放大 10 倍**，
/// 进而让"评分低于阈值"这条判定永远成立——**症状是守卫从不触发，看起来像"质量一直很好"。**
///
/// **Why normalisation matters**: the quality score is a weighted sum of risks. Without it, a caller who sets the gap weight to `3.5` **scales the whole score by
/// ten**, so "score is below the threshold" holds forever — **the symptom is a guard that never fires, which looks like "quality is always fine".**
/// </remarks>
public sealed class Demo2BendersQualityGuardTests
{
    /// <summary>
    /// **不给覆盖 → 原样返回默认值** / No override returns the defaults untouched.
    /// </summary>
    /// <remarks>
    /// 默认值本身**已经是归一化的**（0.35+0.2+0.2+0.15+0.1 = 1.0）。这条把这件事钉住：如果谁改了默认值又没保持和为 1，
    /// "不给覆盖"这条路径就会带着一组未归一化的权重出去，而**没有任何判据会因此变红**。
    ///
    /// The defaults are **already normalised** (0.35+0.2+0.2+0.15+0.1 = 1.0). This pins that: change a default without keeping the sum at one and the "no
    /// override" path hands out an unnormalised set, **with nothing turning red.**
    /// </remarks>
    [Fact]
    public void NoOverride_ShouldReturnTheNormalisedDefaults() {
        BendersQualityGuardConfig resolved = BendersStrategy.ResolveQualityGuardConfig(null);

        resolved.Should().Be(new BendersQualityGuardConfig(),
            "with no override the defaults are returned as-is");
        (resolved.ScoreGapWeight + resolved.ScoreTimeWeight + resolved.ScoreIterationWeight
            + resolved.ScoreCutDensityWeight + resolved.ScoreTrajectoryWeight)
            .Should().BeApproximately(1.0, 1e-12, "the defaults must already sum to one");
    }

    /// <summary>
    /// **部分覆盖 → 其余字段仍是默认值，权重仍然归一化** / A partial override keeps the other fields at their defaults and still normalises the weights.
    /// </summary>
    /// <remarks>
    /// 这是最容易被写错的一格：只填一个权重时，**没填的必须取默认值**再参与归一化——不是当成 0，也不是跳过归一化。
    /// 基线用的是 `override.scoreXWeight ?: default.scoreXWeight`，本仓照抄；这条判据钉的就是"没填的取默认"。
    ///
    /// This is the easiest cell to get wrong: with a single weight supplied, **the missing ones must take their defaults** before normalisation — not zero, and
    /// not skipped. The baseline writes `override.scoreXWeight ?: default.scoreXWeight`; this criterion pins exactly that.
    /// </remarks>
    [Fact]
    public void APartialOverride_ShouldStillSumToOne() {
        BendersQualityGuardConfig resolved = BendersStrategy.ResolveQualityGuardConfig(
            new BendersQualityOverrideConfig(ScoreGapWeight: 1.0));

        resolved.ScoreGapWeight.Should().BeGreaterThan(resolved.ScoreTimeWeight,
            "the supplied gap weight is above its default share of 0.2");
        (resolved.ScoreGapWeight + resolved.ScoreTimeWeight + resolved.ScoreIterationWeight
            + resolved.ScoreCutDensityWeight + resolved.ScoreTrajectoryWeight)
            .Should().BeApproximately(1.0, 1e-12);

        // 未覆盖的标量仍是默认值。
        // Uncovered scalars stay at their defaults.
        resolved.WeakGapMultiplier.Should().Be(new BendersQualityGuardConfig().WeakGapMultiplier);
        resolved.IterationPressurePercent.Should().Be(new BendersQualityGuardConfig().IterationPressurePercent);
    }

    /// <summary>
    /// **权重和为 0 → 退回默认权重，而不是 0/0** / A zero weight sum falls back to the default weights instead of dividing by zero.
    /// </summary>
    /// <remarks>
    /// 五个权重都填 0（或全为负）时，归一化会得到 `NaN`，而 `NaN` 参与比较的结果永远是 false——
    /// **守卫从此不再触发，评分也不再是可比较的数**。基线用 `<= 1e-12` 兜底回（0.35, 0.2, 0.2, 0.15, 0.1）。
    ///
    /// With all five weights zero (or all negative) the normalisation yields `NaN`, and any comparison with `NaN` is false — **the guard stops firing and the
    /// score stops being a comparable number**. The baseline catches it with `<= 1e-12` and falls back to (0.35, 0.2, 0.2, 0.15, 0.1).
    /// </remarks>
    [Fact]
    public void AZeroWeightSum_ShouldFallBackToTheDefaultShares() {
        BendersQualityGuardConfig resolved = BendersStrategy.ResolveQualityGuardConfig(
            new BendersQualityOverrideConfig(ScoreGapWeight: 0.0, ScoreTimeWeight: 0.0, ScoreIterationWeight: 0.0, ScoreCutDensityWeight: 0.0, ScoreTrajectoryWeight: 0.0));

        resolved.ScoreGapWeight.Should().BeApproximately(0.35, 1e-12);
        resolved.ScoreTimeWeight.Should().BeApproximately(0.2, 1e-12);
        resolved.ScoreIterationWeight.Should().BeApproximately(0.2, 1e-12);
        resolved.ScoreCutDensityWeight.Should().BeApproximately(0.15, 1e-12);
        resolved.ScoreTrajectoryWeight.Should().BeApproximately(0.1, 1e-12);
        resolved.ScoreGapWeight.Should().NotBe(double.NaN);
    }

    /// <summary>
    /// **九个标量各自被夹进合法区间** / Each of the nine scalars is coerced into its valid range.
    /// </summary>
    /// <remarks>
    /// 基线逐项 `coerceAtLeast` / `coerceIn`。**这些夹取不是为了好看**：`iterationPressurePercent = 0` 会让"迭代压力"在第一次迭代就触发，
    /// `trajectoryMinSnapshots = 0` 会让轨迹分析在只有一个快照时也算数——**两种都表现为"守卫过早开火"**，
    /// 而调用方会以为是自己配的阈值不对。
    ///
    /// 一次给全部九个越界值，断言每一个都被夹回边界。**判据钉的是边界，不是"大致被夹过"。**
    ///
    /// The baseline coerces each one. **These are not cosmetic**: `iterationPressurePercent = 0` fires iteration pressure on the first iteration, and
    /// `trajectoryMinSnapshots = 0` lets trajectory analysis run on a single snapshot — **both look like "the guard fired too early"**, and the caller will
    /// assume their thresholds are wrong. All nine are given out-of-range values at once and each is asserted back on its boundary.
    /// </remarks>
    [Fact]
    public void EveryScalar_ShouldBeClampedToItsBound() {
        BendersQualityGuardConfig defaults = new();
        BendersQualityGuardConfig resolved = BendersStrategy.ResolveQualityGuardConfig(
            new BendersQualityOverrideConfig(
                WeakGapMultiplier: -1.0,
                WeakGapFloor: 0.0,
                IterationPressurePercent: 0,
                CutDensityMinIterations: 0,
                CutDensityThreshold: -1.0,
                TrajectoryMinSnapshots: 0,
                TrajectoryStepMultiplier: -1.0,
                TrajectoryStepFloor: 0.0,
                TimeGuardMinMs: 0));

        resolved.WeakGapMultiplier.Should().Be(1.0);
        resolved.WeakGapFloor.Should().Be(1e-12);
        resolved.IterationPressurePercent.Should().Be(1);
        resolved.CutDensityMinIterations.Should().Be(1);
        resolved.CutDensityThreshold.Should().Be(0.0);
        resolved.TrajectoryMinSnapshots.Should().Be(2);
        resolved.TrajectoryStepMultiplier.Should().Be(1.0);
        resolved.TrajectoryStepFloor.Should().Be(1e-12);
        resolved.TimeGuardMinMs.Should().Be(1);

        // 五个权重这次没填，应当仍取默认值并归一化成一个和为 1 的分布。
        // The five weights were not supplied here, so they keep their defaults and normalise to a distribution summing to one.
        (resolved.ScoreGapWeight + resolved.ScoreTimeWeight + resolved.ScoreIterationWeight
            + resolved.ScoreCutDensityWeight + resolved.ScoreTrajectoryWeight)
            .Should().BeApproximately(1.0, 1e-12);
        resolved.ScoreGapWeight.Should().BeApproximately(defaults.ScoreGapWeight, 1e-12);
    }

    /// <summary>
    /// **上界存在，而且只对那一项** / An upper bound exists, and only for that one field.
    /// </summary>
    /// <remarks>
    /// `iterationPressurePercent` 是九个标量里**唯一有上界的**（`coerceIn(1, 100)`）。把它填成 100 必须保留，
    /// 填成 101 必须被夹回 100——**"上限"与"下限"是两条不同的边界，只测下限会漏掉一半。**
    ///
    /// `iterationPressurePercent` is **the only one of the nine with an upper bound** (`coerceIn(1, 100)`). 100 must survive, 101 must clamp to 100 —
    /// **the upper bound and the lower bound are two different edges, and testing only the lower misses half.**
    /// </remarks>
    [Theory]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(99, 99)]
    public void IterationPressurePercent_ShouldKeepItsUpperBound(int supplied, int expected) {
        BendersQualityGuardConfig resolved = BendersStrategy.ResolveQualityGuardConfig(
            new BendersQualityOverrideConfig(IterationPressurePercent: supplied));

        resolved.IterationPressurePercent.Should().Be(expected);
    }
}
