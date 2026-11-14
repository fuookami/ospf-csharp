// 多样性度量测试 / Tests for the diversity metrics.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 多样性度量的语义（与 `TopologyAndSurvivorTests` 中的存在性检查互补）/ The diversity metrics' semantics, complementing the
/// existence checks in `TopologyAndSurvivorTests`.
/// </summary>
public sealed class DiversityMetricSemanticsTests {
    /// <summary>**平均两两距离是"平均欧氏距离"，不是距离的均方根** / The mean pairwise distance is a mean of Euclidean distances, not an RMS.</summary>
    /// <remarks>
    /// 三个一维解 0、1、3：三对距离是 1、3、2，平均为 `(1+3+2)/3 = 2.0`；均方根是 `sqrt((1+9+4)/3) = sqrt(14/3) ≈ 2.1602`。
    /// 两种值都是"合理的正数"，**只看数值大小完全看不出用了哪一个**——这正是此前误写成 RMS 却未被发现的原因。
    /// Three one-dimensional solutions at 0, 1 and 3 give pair distances 1, 3 and 2, so the mean is `(1+3+2)/3 = 2.0` while the RMS is
    /// `sqrt((1+9+4)/3) ≈ 2.1602`. Both are plausible positive numbers, **so the value alone reveals nothing** — which is why the earlier
    /// RMS version went unnoticed.
    /// </remarks>
    [Fact]
    public void MeanPairwiseDistance_ShouldBeArithmeticMean() {
        Solution<Flt64>[] solutions = {
            new Solution<Flt64>(new[] { new Flt64(0.0) }),
            new Solution<Flt64>(new[] { new Flt64(1.0) }),
            new Solution<Flt64>(new[] { new Flt64(3.0) }),
        };

        MeanPairwiseDistance.Instance.Measure(solutions, IIntoValue<Flt64>.Identity)
            .ToDouble().Should().BeApproximately(2.0, 1e-12);
    }

    /// <summary>**二维距离是欧氏距离，不是各维差的平均** / The 2-D distance is Euclidean, not an average of per-axis differences.</summary>
    /// <remarks>
    /// 两个解 `(0,0)` 与 `(3,4)`：欧氏距离是 5，各维差的平均是 `(3+4)/2 = 3.5`。用平均会让高维解集的多样性被严重低估。
    /// Solutions `(0,0)` and `(3,4)` are 5 apart Euclidean, while averaging the per-axis differences gives 3.5. Averaging badly
    /// underestimates diversity in high-dimensional sets.
    /// </remarks>
    [Fact]
    public void MeanPairwiseDistance_ShouldUseEuclideanDistance() {
        Solution<Flt64>[] solutions = {
            new Solution<Flt64>(new[] { new Flt64(0.0), new Flt64(0.0) }),
            new Solution<Flt64>(new[] { new Flt64(3.0), new Flt64(4.0) }),
        };

        MeanPairwiseDistance.Instance.Measure(solutions, IIntoValue<Flt64>.Identity)
            .ToDouble().Should().BeApproximately(5.0, 1e-12);
    }

    /// <summary>**不足两个解时返回 0** / Fewer than two solutions returns 0.</summary>
    [Fact]
    public void MeanPairwiseDistance_ShouldBeZeroForTinySets() {
        MeanPairwiseDistance.Instance.Measure(Array.Empty<Solution<Flt64>>(), IIntoValue<Flt64>.Identity)
            .ToDouble().Should().Be(0.0);
        MeanPairwiseDistance.Instance.Measure(
                new[] { new Solution<Flt64>(new[] { new Flt64(5.0) }) }, IIntoValue<Flt64>.Identity)
            .ToDouble().Should().Be(0.0);
    }

    /// <summary>**分形维数在退化分布上返回 0** / The fractal dimension returns 0 on a degenerate distribution.</summary>
    /// <remarks>
    /// 全部解重合时关联维数估计失败；把失败当成"高多样性"会让停止判据在该停的时候不停。
    /// When every solution coincides the correlation-dimension estimate fails; treating failure as "high diversity" would keep the
    /// stopping criterion from stopping when it should.
    /// </remarks>
    [Fact]
    public void FractalDimension_ShouldReturnZeroOnDegenerateInput() {
        Solution<Flt64>[] solutions = {
            new Solution<Flt64>(new[] { new Flt64(5.0), new Flt64(5.0) }),
            new Solution<Flt64>(new[] { new Flt64(5.0), new Flt64(5.0) }),
            new Solution<Flt64>(new[] { new Flt64(5.0), new Flt64(5.0) }),
        };

        FractalDimensionDiversity.Instance.Measure(solutions, IIntoValue<Flt64>.Identity)
            .ToDouble().Should().Be(0.0);
    }

    /// <summary>**分形维数在铺开的分布上给出有限正值** / The fractal dimension is finite and positive on a spread distribution.</summary>
    [Fact]
    public void FractalDimension_ShouldBePositiveOnSpreadInput() {
        var solutions = new List<Solution<Flt64>>();
        for (int index = 0; index < 20; index++) {
            solutions.Add(new Solution<Flt64>(new[] {
                new Flt64(index * 1.0),
                new Flt64((index * index) % 7 * 1.0),
            }));
        }

        double value = FractalDimensionDiversity.Instance
            .Measure(solutions, IIntoValue<Flt64>.Identity).ToDouble();

        double.IsFinite(value).Should().BeTrue();
        value.Should().BeGreaterThan(0.0);
    }
}
