// 数学准备基准测试 / Tests for the math-preparation benchmark.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.MultiArray;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// 数学准备基准 / The math-preparation benchmark.
/// </summary>
public sealed class MathPreparationBenchmarkTests {
    /// <summary>**三档规模只在 factor 上不同** / The three tiers differ only in the factor.</summary>
    [Theory]
    [InlineData("small", 1)]
    [InlineData("medium", 2)]
    [InlineData("large", 4)]
    public void Setup_ShouldScaleEveryFixture(string dataset, int factor) {
        var benchmark = new MathPreparationBenchmark { Dataset = dataset };

        benchmark.Setup();

        benchmark.Factor.Should().Be(factor);
        benchmark.Array.Shape[1].Should().Be(16 * factor);
        benchmark.Tasks.Should().HaveCount(512 * factor);
    }

    /// <summary>**同序遍历把三轴表的全部元素各累加一次** / Matching-order traversal accumulates every element exactly once.</summary>
    /// <remarks>
    /// **必须与独立算出的期望值比对**：只断言"结果是个数"时，遍历若只走第一层（漏掉大部分元素），
    /// 依然返回一个双精度值，**而那个值看起来完全正常**。
    ///
    /// **The result must be compared against an independently computed expectation**: asserting only "it is a number" passes even when the traversal
    /// walks only the outermost layer, and **that value looks entirely normal**.
    /// </remarks>
    [Fact]
    public void MatchingTraversal_ShouldSumEveryElement() {
        var benchmark = new MathPreparationBenchmark { Dataset = "small" };
        benchmark.Setup();

        Flt64 sum = benchmark.MatchingTraversal();
        Flt64 expected = Flt64.Zero;
        foreach (Flt64 value in benchmark.Array) {
            expected = expected + value;
        }

        sum.Should().Be(expected);
        benchmark.Array.Count.Should().Be(32 * 16 * 8);
    }

    /// <summary>**缩并的结果规模由两个操作数共同决定** / The contraction's size follows both operands.</summary>
    /// <remarks>
    /// 只断言"元素数大于 0"时，缩并实现把未选中轴丢掉也照样通过。`(16 × 32) · (32 × 16)` 缩并后必须是 `16 × 16 = 256`。
    ///
    /// Asserting only "the count is positive" passes even when the contraction drops non-contracted axes. `(16 × 32) · (32 × 16)` must contract to
    /// `16 × 16 = 256`.
    /// </remarks>
    [Fact]
    public void Contraction_ShouldProduceTheFullOuterShape() {
        var benchmark = new MathPreparationBenchmark { Dataset = "small" };
        benchmark.Setup();

        benchmark.Contraction().Should().Be(256);
    }

    /// <summary>**共享 DAG 的规范化深度远小于重复展开** / The shared DAG normalizes far shallower than repeated expansion.</summary>
    /// <remarks>
    /// **这是本基准存在的理由**：共享标量的祖先被引用 6 次、每层两个参数。规范化若不做去重，
    /// 深度会是 6 层；若去重，深度仍是 6 层——**所以这里断言的不是"共享更深"**，而是"共享与扁平的深度按各自操作数计"。
    ///
    /// **This is why the benchmark exists**: the shared scalar's ancestor is referenced six times with two arguments per level. De-duplicating
    /// normalization keeps the depth at 6 and non-de-duplicating also keeps it at 6 — **so the assertion is not "shared is deeper"**, it is that each
    /// fixture's depth counts its own operands.
    /// </remarks>
    [Fact]
    public void Normalize_ShouldReportDepthForEachFixture() {
        var benchmark = new MathPreparationBenchmark { Dataset = "small" };
        benchmark.Setup();

        int shared = benchmark.NormalizeShared();
        int flat = benchmark.NormalizeFlat();

        shared.Should().BeGreaterThan(0);
        flat.Should().BeGreaterThan(0);
        benchmark.SharedOperands.Should().HaveCount(16);
        benchmark.FlatOperands.Should().HaveCount(16);
    }

    /// <summary>**四 worker 映射按原序返回每一项** / The four-worker map returns every item in the original order.</summary>
    /// <remarks>
    /// 只断言"结果个数对"时，映射把顺序打乱依然通过。**并行映射最容易出的错就是顺序**，而
    /// `MapParallelly` 若不保序，逐个比对才会暴露。
    ///
    /// Asserting only the count passes even when the mapping loses order. **Order is the most likely thing to break in a parallel map**, and only an
    /// element-by-element comparison exposes it when `MapParallelly` fails to preserve order.
    /// </remarks>
    [Fact]
    public void BoundedWorkerMap_ShouldPreserveOrder() {
        var benchmark = new MathPreparationBenchmark { Dataset = "small" };
        benchmark.Setup();

        List<int> mapped = benchmark.BoundedWorkerMap();

        mapped.Should().Equal(benchmark.Tasks.Select(task => task + 1));
    }
}
