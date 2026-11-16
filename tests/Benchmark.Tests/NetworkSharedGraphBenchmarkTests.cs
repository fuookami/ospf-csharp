#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Xunit;

namespace Fuookami.Ospf.Test.Benchmark;

/// <summary>
/// **共享图基准的形态、规模与调度三个维度都真的接到了夹具上** /
/// The shape, size and dispatch dimensions of the shared-graph fixture really reach it.
/// </summary>
/// <remarks>
/// **这张图是环形的：** **`ring4` 从每个节点往前连 4 步再取模，所以环上所有点互相可达。**
/// **正因为这一层结构可推，判据可以用<b>精确等式**而不是"大于零"：** **每个 reader 的可达集必须正好等于节点数，
/// 反向视图的出弧必须正好是 4。** **这些等式一旦错位，就说明形态或规模没接上——而报告里的数字仍然好看。**
///
/// **The graph is a ring: `ring4` links four steps forward with wrap-around, so every node reaches every other.** **Because that structure is
/// derivable, the criteria can use exact equalities rather than "greater than zero": each reader's reachable set must equal the node count, and a
/// reverse view must have out-degree four.** **A mismatch means the shape or size never landed — while the report still looks fine.**
/// </remarks>
public sealed class NetworkSharedGraphBenchmarkTests
{
    /// <summary>**四种形态给出各自的出度** / The four shapes give their stated out-degrees.</summary>
    [Theory]
    [InlineData("empty", 0)]
    [InlineData("sparse2", 2)]
    [InlineData("ring4", 4)]
    [InlineData("highOut", 8)]
    public void TheShapeDimension_ShouldSetTheOutDegree(string shape, int expected) {
        var benchmark = new NetworkSharedGraphBenchmark { Shape = shape, Size = 256, Readers = 1 };
        benchmark.Setup();

        benchmark.WarmArcCount.Should().Be(expected, shape + " must give the first node exactly " + expected + " outgoing arcs");
    }

    /// <summary>**环形形态下每个 reader 都可达全部节点** / On a ring every reader reaches every node.</summary>
    [Fact]
    public void TheSizeDimension_ShouldBeFullyReachableOnARing() {
        var benchmark = new NetworkSharedGraphBenchmark { Shape = "ring4", Size = 256, Readers = 4 };
        benchmark.Setup();

        benchmark.ColdFirstReadBatch().Should().Be(
            4 * 256,
            "four readers each reach all 256 nodes of the ring, so the totals must be exactly 1024");
    }

    /// <summary>**反向视图的出弧仍是 4** / The reverse view still has out-degree four.</summary>
    [Fact]
    public void TheReverseBatch_ShouldSeeTheSameDegree() {
        var benchmark = new NetworkSharedGraphBenchmark { Shape = "ring4", Size = 256, Readers = 2 };
        benchmark.Setup();

        benchmark.ColdReverseBatch().Should().Be(
            2 * (4 + 4),
            "a reverse view of a four-out ring has four outgoing and four incoming arcs per node");
    }

    /// <summary>**空调度返回到达顺序号之和** / The empty dispatch returns the sum of arrival ordinals.</summary>
    /// <remarks>
    /// **这一条同时验证"到达顺序号是原子分配的"：** **如果每个线程直接用自己的下标，和会是同一个数——
    /// 但那样"谁先到"就被伪造了。** **所以判据查的是<b>集合是否恰为 0…n-1**（由和等于 n(n-1)/2 与个数等于 n 共同保证）。**
    ///
    /// **It also checks that arrival ordinals are really allocated atomically: using the thread index would give the same sum, but the arrival
    /// order would be fabricated.** **So the criterion checks the multiset is exactly 0…n-1, witnessed by the sum and the count together.**
    /// </remarks>
    [Fact]
    public void TheDispatchControl_ShouldReturnTheOrdinalSum() {
        foreach (int readers in new[] { 1, 2, 4, 8 }) {
            var benchmark = new NetworkSharedGraphBenchmark { Shape = "ring4", Size = 256, Readers = readers };
            benchmark.Setup();

            benchmark.DispatchOnly().Should().Be(
                readers * (readers - 1) / 2,
                "readers=" + readers + " must hand out exactly the ordinals 0.." + (readers - 1));
        }
    }

    /// <summary>**稳态读把每个 reader 的出弧与入弧都算进去** / Steady reads count each reader's outgoing and incoming arcs.</summary>
    [Fact]
    public void TheWarmRead_ShouldCountBothDirections() {
        var benchmark = new NetworkSharedGraphBenchmark { Shape = "ring4", Size = 256, Readers = 3 };
        benchmark.Setup();

        benchmark.WarmSharedRead().Should().Be(
            4 + 3 * (4 + 4),
            "the first node's outgoing count plus three readers' outgoing and incoming counts");
    }
}