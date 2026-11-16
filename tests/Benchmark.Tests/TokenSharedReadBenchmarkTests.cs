#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Xunit;

namespace Fuookami.Ospf.Test.Benchmark;

/// <summary>
/// **共享读基准真的在多个 reader 上读，而且"建表工作量"这个读数是真的** /
/// The shared-read fixture really reads from several readers, and its build-work reading is real.
/// </summary>
/// <remarks>
/// **这个基准最有价值也最容易假绿的是 `ColdBuildWork`：** **它靠"变量标识符被读了几次"来数建表次数。**
/// **如果那个计数变量没被真正使用（比如 `Identifier` 覆写没生效、或夹具塞进去的是普通 `RealVar`），
/// 读数是 0，而报告里会安静地写着 `reads=0`——**一条"建表工作量"的判据就这样变成了装饰。**
///
/// **所以第一条判据就钉它：** **一次完整冷建表的读数必须与 token 数同量级（每个 token 至少一次）。**
/// **其余三条钉调度协议：** **空调度返回槽位号之和、不同 reader 数的负载按比例变化、三个读方法都解析出了真实索引。**
///
/// **The most valuable and most fake-green-prone part is `ColdBuildWork`: it counts builds by how often variable identifiers are read.**
/// **If that counting variable is not really used (an override that never takes effect, or a plain `RealVar` slipping into the fixture), the reading
/// is zero while the report quietly says `reads=0` — a build-work criterion turned into decoration.** **So the first criterion pins exactly that: one
/// full cold build must read about one identifier per token.** **The rest pin the scheduling protocol.**
/// </remarks>
public sealed class TokenSharedReadBenchmarkTests
{
    /// <summary>**`ColdBuildWork` 的读数是真读数** / The `ColdBuildWork` reading is a real reading.</summary>
    [Fact]
    public void TheBuildWorkReading_ShouldBeCounted() {
        var benchmark = new TokenSharedReadBenchmark { Dataset = "small", Readers = 1 };
        benchmark.Setup();
        try {
            string reading = benchmark.ColdBuildWork();

            reading.Should().MatchRegex(@"^reads=\d+;indexSum=-?\d+$", "the reading is `reads=…;indexSum=…`");
            benchmark.LastBuildReads.Should().BePositive(
                "a full cold build reads identifiers; zero means the counting variable was never used and the metric is decoration");
            benchmark.LastBuildReads.Should().BeGreaterThanOrEqualTo(
                benchmark.Size,
                "one build costs about one identifier read per token, so fewer reads than tokens means no build happened");
        }
        finally {
            benchmark.Cleanup();
        }
    }

    /// <summary>**空调度返回槽位号之和** / The empty dispatch returns the sum of slot numbers.</summary>
    [Fact]
    public void TheDispatchControl_ShouldReturnTheSlotSum() {
        var benchmark = new TokenSharedReadBenchmark { Dataset = "small", Readers = 4 };
        benchmark.Setup();
        try {
            benchmark.DispatchOnly().Should().Be(
                0 + 1 + 2 + 3,
                "every reader returns its slot, so the sum proves all four ran and the barrier released them");
        }
        finally {
            benchmark.Cleanup();
        }
    }

    /// <summary>**三个读方法都解析出了真实索引** / All three read methods resolve real indices.</summary>
    [Fact]
    public void TheReadMethods_ShouldResolveIndices() {
        var benchmark = new TokenSharedReadBenchmark { Dataset = "small", Readers = 2 };
        benchmark.Setup();
        try {
            // **索引从 0 开始，所以"和为正"就等于"至少有一个查到了非零位置"；`-1` 会把它拉低甚至拉成负数。**
            // **Indices start at zero, so a positive sum means at least one lookup found a non-zero position; a `-1` would drag it down.**
            benchmark.WarmSharedRead().Should().BePositive("steady reads on a primed list resolve real indices");
            benchmark.ColdFirstReadBatch().Should().BePositive("the first read of a shared cold list resolves an index");
            benchmark.ColdDistinctKeyBatch().Should().BePositive("distinct-key cold reads resolve real indices");
        }
        finally {
            benchmark.Cleanup();
        }
    }

    /// <summary>**reader 数真的改变负载** / The reader count really changes the workload.</summary>
    [Fact]
    public void TheReaderCount_ShouldScaleTheWorkload() {
        var one = new TokenSharedReadBenchmark { Dataset = "small", Readers = 1 };
        one.Setup();
        var eight = new TokenSharedReadBenchmark { Dataset = "small", Readers = 8 };
        eight.Setup();
        try {
            eight.DispatchOnly().Should().Be(0 + 1 + 2 + 3 + 4 + 5 + 6 + 7, "eight readers each return their slot");
            eight.ColdFirstReadBatch().Should().NotBe(
                one.ColdFirstReadBatch(),
                "eight readers read eight different tokens, so the index sums cannot coincide");
        }
        finally {
            one.Cleanup();
            eight.Cleanup();
        }
    }

    /// <summary>**三档规模真的是 1024/2048/4096** / The three scales really are 1024/2048/4096.</summary>
    [Fact]
    public void TheThreeScales_ShouldMatchTheStatedSizes() {
        foreach ((string dataset, int expected) in new[] { ("small", 1_024), ("medium", 2_048), ("large", 4_096) }) {
            var benchmark = new TokenSharedReadBenchmark { Dataset = dataset, Readers = 1 };
            benchmark.Setup();
            try {
                benchmark.Size.Should().Be(expected, dataset + " must hold exactly " + expected + " tokens");
            }
            finally {
                benchmark.Cleanup();
            }
        }
    }
}