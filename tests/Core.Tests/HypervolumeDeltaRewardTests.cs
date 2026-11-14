// 超体积增量奖励测试 / Tests for the hypervolume-delta reward.

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 超体积增量奖励 / The hypervolume-delta reward.
/// </summary>
public sealed class HypervolumeDeltaRewardTests {
    /// <summary>**二维精确超体积增量与独立实现一致** / The 2-D exact delta matches an independent implementation.</summary>
    /// <remarks>
    /// 黄金向量来自独立的 Python 实现（`hv2d`），不是从 C# 输出复制的：参考点 `(4,4)`，旧点 `(1,3)`、新点 `(3,1)`；
    /// 旧体积 `3.0`、合并后 `5.0`、增量 `2.0`。
    /// The golden vectors come from an independent Python implementation (`hv2d`), not copied from C# output: reference `(4,4)`, old
    /// point `(1,3)`, new point `(3,1)`; old volume `3.0`, merged `5.0`, delta `2.0`.
    /// </remarks>
    [Fact]
    public void Measure_ShouldMatchTwoDimensionalGoldenVector() {
        HypervolumeDeltaReward reward = BuildReward(new[] { 4.0, 4.0 });

        Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> measured = reward.Measure(Context(
            old: new[] { new[] { 1.0, 3.0 } },
            fresh: new[] { new[] { 3.0, 1.0 } }));

        measured.Should().BeOfType<Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>>();
        HypervolumeMeasurement measurement =
            ((Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>)measured).Value;
        measurement.Delta.Should().BeApproximately(2.0, 1e-12);
        measurement.Exact.Should().BeTrue();
    }

    /// <summary>**三维精确超体积与独立实现一致** / The 3-D exact volume matches an independent implementation.</summary>
    [Fact]
    public void Measure_ShouldMatchThreeDimensionalGoldenVector() {
        HypervolumeDeltaReward reward = BuildReward(new[] { 4.0, 4.0, 4.0 });

        Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> measured = reward.Measure(Context(
            old: Array.Empty<double[]>(),
            fresh: new[] { new[] { 1.0, 3.0, 3.0 }, new[] { 3.0, 1.0, 3.0 }, new[] { 3.0, 3.0, 1.0 } }));

        measured.Should().BeOfType<Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>>();
        // 独立 Python 实现的 3 维体积是 7.0。
        // The independent Python implementation gives a 3-D volume of 7.0.
        ((Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>)measured).Value.Delta
            .Should().BeApproximately(7.0, 1e-12);
    }

    /// <summary>**被支配的新点不增加体积** / A dominated new point adds no volume.</summary>
    [Fact]
    public void Measure_ShouldIgnoreDominatedPoints() {
        HypervolumeDeltaReward reward = BuildReward(new[] { 4.0, 4.0 });

        Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> measured = reward.Measure(Context(
            old: new[] { new[] { 1.0, 1.0 } },
            fresh: new[] { new[] { 2.0, 2.0 } }));

        ((Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>)measured).Value.Delta
            .Should().Be(0.0, "(2,2) 被 (1,1) 支配 / (2,2) is dominated by (1,1)");
    }

    /// <summary>**未校准前一律返回 `delta = 0` 且 `calibrated = false`** / Before calibration the delta is 0 and calibrated is false.</summary>
    /// <remarks>
    /// 跳过校准直接算会让参考点由第一批候选决定，**而第一批候选往往远离真实前沿，整段搜索的增量从此系统性地偏大**。
    /// Skipping calibration lets the first batch decide the reference point, and **since that first batch is typically far from the true
    /// front, every later delta is systematically inflated**.
    /// </remarks>
    [Fact]
    public void Measure_ShouldNotCalibrateImmediately() {
        // 校准观测数设为 32：一次调用远不足以校准。
        // The calibration count is 32: one call is far from enough.
        HypervolumeDeltaReward reward = new(calibrationObservations: 32);

        Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> measured = reward.Measure(Context(
            old: Array.Empty<double[]>(),
            fresh: new[] { new[] { 1.0, 1.0 } }));

        measured.Should().BeOfType<Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>>();
        HypervolumeMeasurement measurement =
            ((Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>)measured).Value;
        measurement.Delta.Should().Be(0.0);
        measurement.Calibrated.Should().BeFalse();
    }

    /// <summary>**校准完成后参考点严格劣于坐标上界** / After calibration the reference point is strictly worse than the maximum.</summary>
    /// <remarks>
    /// 当余量退化为 0 时参考点与上界重合，**体积恒为 0 而每一步都返回成功**。
    /// When the margin degenerates to 0 the reference point coincides with the maximum, **the volume stays 0 while every step returns
    /// success**.
    /// </remarks>
    [Fact]
    public void Measure_ShouldCalibrateStrictlyBeyondMaximum() {
        HypervolumeDeltaReward reward = new(calibrationObservations: 1);

        reward.Measure(Context(old: Array.Empty<double[]>(), fresh: new[] { new[] { 1.0, 1.0 } }))
            .Should().BeOfType<Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>>();

        IReadOnlyList<double> reference = reward.Reference;
        reference.Count.Should().Be(2);
        reference[0].Should().BeGreaterThan(1.0, "参考点必须严格劣于上界 / the reference must be strictly worse than the maximum");
        reference[1].Should().BeGreaterThan(1.0);
        // 余量 = max(spread=0, max(|1|,1)) * 0.1 = 0.1 → 参考点 1.1。
        // Margin = max(spread=0, max(|1|,1)) * 0.1 = 0.1 → reference 1.1.
        reference[0].Should().BeApproximately(1.1, 1e-12);
    }

    /// <summary>**维度不符的点被拒绝** / Points with a mismatched dimension are rejected.</summary>
    [Fact]
    public void Measure_ShouldRejectDimensionMismatch() {
        HypervolumeDeltaReward reward = BuildReward(new[] { 4.0, 4.0 });

        reward.Measure(Context(old: new[] { new[] { 1.0 } }, fresh: Array.Empty<double[]>()))
            .Should().BeOfType<Failed<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**非有限坐标被拒绝** / Non-finite coordinates are rejected.</summary>
    [Fact]
    public void Measure_ShouldRejectNonFiniteCoordinates() {
        HypervolumeDeltaReward reward = BuildReward(new[] { 4.0, 4.0 });

        reward.Measure(Context(old: new[] { new[] { double.NaN, 1.0 } }, fresh: Array.Empty<double[]>()))
            .Should().BeOfType<Failed<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**几何额度耗尽时返回失败而不是截断结果** / Exhausting the work budget fails rather than truncating.</summary>
    /// <remarks>
    /// 截断会给出一个偏小的体积，**而调用方无法区分"前沿小"和"算力不够"**。
    /// Truncation yields a too-small volume, and **the caller cannot distinguish "a small front" from "not enough compute"**.
    /// </remarks>
    [Fact]
    public void Measure_ShouldFailWhenWorkExhausted() {
        HypervolumeDeltaReward reward = new(
            new[] { new Flt64(4.0), new Flt64(4.0) },
            HypervolumeMode.Exact, maxWork: 3L, samples: 8, seed: 1L, calibrationObservations: 32);

        reward.Measure(Context(old: new[] { new[] { 1.0, 3.0 } }, fresh: new[] { new[] { 3.0, 1.0 } }))
            .Should().BeOfType<Failed<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**维数超过 256 时精确模式拒绝** / Exact mode rejects dimensions above 256.</summary>
    /// <remarks>
    /// 递归深度按维数线性增长，超过会让栈溢出——**而栈溢出不是可捕获的失败，整个进程终止**。
    /// Recursion depth grows linearly with the dimension, and exceeding it overflows the stack — **and a stack overflow is not a
    /// catchable failure; the whole process dies**.
    /// </remarks>
    [Fact]
    public void Measure_ShouldRejectHighDimensionInExactMode() {
        var reference = new Flt64[257];
        for (int index = 0; index < reference.Length; index++) {
            reference[index] = new Flt64(4.0);
        }
        HypervolumeDeltaReward reward = new(reference);

        double[] point = new double[257];
        for (int index = 0; index < point.Length; index++) {
            point[index] = 1.0;
        }
        reward.Measure(Context(old: Array.Empty<double[]>(), fresh: new[] { point }))
            .Should().BeOfType<Failed<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**采样模式返回非精确测量与误差指标** / Sampled mode returns a non-exact measurement with uncertainty.</summary>
    [Fact]
    public void Measure_ShouldReportUncertaintyInSampledMode() {
        HypervolumeDeltaReward reward = new(
            new[] { new Flt64(4.0), new Flt64(4.0) },
            HypervolumeMode.Sampled, maxWork: 1_000_000L, samples: 1024, seed: 1L,
            calibrationObservations: 32);

        Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> measured = reward.Measure(Context(
            old: new[] { new[] { 1.0, 3.0 } },
            fresh: new[] { new[] { 3.0, 1.0 } }));

        measured.Should().BeOfType<Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>>();
        HypervolumeMeasurement measurement =
            ((Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>)measured).Value;
        measurement.Exact.Should().BeFalse();
        measurement.Samples.Should().Be(1024);
        measurement.StandardError.Should().BeGreaterThanOrEqualTo(0.0);
        measurement.ErrorBound.Should().BeGreaterThan(0.0);
        measurement.Delta.Should().BeGreaterThan(0.0);
    }

    /// <summary>**采样用固定种子，重复调用给出同一结果** / Sampling uses a fixed seed, so repeated calls agree.</summary>
    /// <remarks>
    /// 复用外部流会让同一份快照在不同时刻给出不同的奖励，**而快照本身看起来完全自洽**。
    /// Reusing an external stream makes the same snapshot yield different rewards at different times, **while the snapshot itself looks
    /// perfectly self-consistent**.
    /// </remarks>
    [Fact]
    public void Measure_ShouldBeDeterministicInSampledMode() {
        HypervolumeDeltaReward reward = new(
            new[] { new Flt64(4.0), new Flt64(4.0) },
            HypervolumeMode.Sampled, maxWork: 1_000_000L, samples: 512, seed: 7L,
            calibrationObservations: 32);

        double first = ((Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>)reward.Measure(Context(
            old: new[] { new[] { 1.0, 3.0 } },
            fresh: new[] { new[] { 3.0, 1.0 } }))).Value.Delta;
        double second = ((Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>)reward.Measure(Context(
            old: new[] { new[] { 1.0, 3.0 } },
            fresh: new[] { new[] { 3.0, 1.0 } }))).Value.Delta;

        second.Should().Be(first, "固定种子必须给出同一结果 / a fixed seed must give the same result");
    }

    /// <summary>**快照往返一致** / Snapshots round-trip.</summary>
    [Fact]
    public void Snapshot_ShouldRoundTrip() {
        HypervolumeDeltaReward reward = BuildReward(new[] { 4.0, 4.0 });
        reward.Measure(Context(old: new[] { new[] { 1.0, 3.0 } }, fresh: new[] { new[] { 3.0, 1.0 } }));

        JsonElement state = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)reward.Save()).Value;
        HypervolumeDeltaReward restored = BuildReward(new[] { 4.0, 4.0 });
        restored.Load(state).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        restored.LastMeasurement!.Delta.Should().BeApproximately(2.0, 1e-12);
        restored.Reference.Should().Equal(reward.Reference);
    }

    /// <summary>**定义串不匹配的快照被拒绝** / A snapshot with a mismatched definition is rejected.</summary>
    [Fact]
    public void Load_ShouldRejectMismatchedDefinition() {
        HypervolumeDeltaReward source = BuildReward(new[] { 4.0, 4.0 });
        JsonElement state = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)source.Save()).Value;

        BuildReward(new[] { 5.0, 5.0 }).Load(state)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**参数校验拒绝非法配置** / Validation rejects illegal configurations.</summary>
    [Fact]
    public void Validate_ShouldRejectBadParameters() {
        new HypervolumeDeltaReward(maxWork: 0L).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new HypervolumeDeltaReward(samples: 0).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new HypervolumeDeltaReward(calibrationObservations: 0).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new HypervolumeDeltaReward(new[] { new Flt64(double.NaN) }).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**奖励接口返回增量** / The reward interface returns the delta.</summary>
    [Fact]
    public void Reward_ShouldReturnDelta() {
        HypervolumeDeltaReward reward = BuildReward(new[] { 4.0, 4.0 });

        ((IRewardPolicy)reward).Evaluate(Context(
                old: new[] { new[] { 1.0, 3.0 } },
                fresh: new[] { new[] { 3.0, 1.0 } })).Should()
            .BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**奖励在测量失败时返回 `NaN`** / The reward returns `NaN` when the measurement fails.</summary>
    [Fact]
    public void Reward_ShouldReturnNaNOnFailure() {
        HypervolumeDeltaReward reward = BuildReward(new[] { 4.0, 4.0 });

        reward.Reward(Context(old: new[] { new[] { 1.0 } }, fresh: Array.Empty<double[]>()))
            .ToDouble().Should().Be(double.NaN);
    }

    /// <summary>构造奖励 / Build the reward.</summary>
    /// <param name="reference">参考点 / The reference point.</param>
    /// <returns>奖励 / The reward.</returns>
    private static HypervolumeDeltaReward BuildReward(double[] reference) {
        var values = new Flt64[reference.Length];
        for (int index = 0; index < reference.Length; index++) {
            values[index] = new Flt64(reference[index]);
        }
        return new HypervolumeDeltaReward(values);
    }

    /// <summary>构造奖励上下文 / Build a reward context.</summary>
    /// <param name="old">旧坐标 / The old coordinates.</param>
    /// <param name="fresh">新坐标 / The new coordinates.</param>
    /// <returns>奖励上下文 / The reward context.</returns>
    private static RewardContext Context(double[][] old, double[][] fresh) =>
        new(false, new Flt64(0.0), 1L, Reference: Points(old), Candidates: Points(fresh));

    /// <summary>把 `double[][]` 转为坐标列表 / Convert `double[][]` into a coordinate list.</summary>
    /// <param name="points">坐标 / The coordinates.</param>
    /// <returns>坐标列表 / The coordinate list.</returns>
    private static IReadOnlyList<IReadOnlyList<Flt64>> Points(double[][] points) {
        var result = new List<IReadOnlyList<Flt64>>(points.Length);
        foreach (double[] point in points) {
            var converted = new List<Flt64>(point.Length);
            foreach (double value in point) {
                converted.Add(new Flt64(value));
            }
            result.Add(converted);
        }
        return result;
    }
}
