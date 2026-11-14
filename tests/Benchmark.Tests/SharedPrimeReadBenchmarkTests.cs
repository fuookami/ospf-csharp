// 共享素数读基准测试 / Tests for the shared prime read benchmark.

using System.Diagnostics;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Math.Ordinary;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// 共享素数读基准 / The shared prime read benchmark.
/// </summary>
public sealed class SharedPrimeReadBenchmarkTests {
    /// <summary>**查询值落在建表范围内，且确实是素数** / The query lies inside the built table and is genuinely prime.</summary>
    /// <remarks>
    /// 查询值一旦超出建表范围，每次读都会走"扩展筛法"，基准就从"共享读"变成了"扩展写"——
    /// **两者数量级不同，而结果仍然是一个正确的布尔值**。
    ///
    /// Once the query falls outside the built range, every read goes through "extend the sieve", turning the benchmark from a shared read into an
    /// expanding write — **different by orders of magnitude, while still returning a correct boolean**.
    /// </remarks>
    [Fact]
    public void Setup_ShouldQueryInsideTheBuiltTable() {
        var benchmark = new SharedPrimeReadBenchmark();

        benchmark.Setup();

        benchmark.Query.CompareTo(
                new Fuookami.Ospf.Math.Algebra.Number.UInt64(SharedPrimeReadBenchmark.DefaultSieveBound))
            .Should().BeNegative(
                "查询值必须在默认建表范围内 / the query must lie inside the default sieve bound");
        new PrimeCache().IsPrime(benchmark.Query).Should().BeTrue("997 是素数 / 997 is prime");
    }

    /// <summary>**重复读取不再扩展筛法** / Repeated reads never extend the sieve again.</summary>
    /// <remarks>
    /// 判据是"第一次与第二万次的耗时同量级"：若读路径会扩展筛法，第二次读到新上界时会重新分配并重筛，
    /// 于是后续读的耗时随次数上涨。**这不是计时精度问题——量级差在小样本上就能看出来**。
    ///
    /// The criterion is "the first and the twenty-thousandth read cost the same order of magnitude": if the read path extended the sieve, a read
    /// reaching a new upper bound would reallocate and re-sieve, so later reads would grow with repetition. **This is not a timing-precision issue — the
    /// order-of-magnitude gap shows up on a small sample**.
    /// </remarks>
    [Fact]
    public void ReadyRead_ShouldNotExtendTheSieve() {
        var benchmark = new SharedPrimeReadBenchmark();
        benchmark.Setup();

        benchmark.ReadyRead().Should().BeTrue();
        var stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < 20_000; ++index) {
            benchmark.ReadyRead();
        }
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(System.TimeSpan.FromSeconds(2),
            "两万次已建表读取不应触发任何扩展 / twenty thousand ready reads must not trigger any extension");
    }

    /// <summary>**共享同一张表时结果一致** / Readers sharing one table agree.</summary>
    /// <remarks>
    /// 基准的意义是"多个 reader 共享已建表"，因此断言落在"同一实例多次读给出同一答案"，而不是"新建实例也算得对"。
    ///
    /// The point of the benchmark is "several readers share one built table", so the assertion is on "the same instance answers consistently", not on
    /// "a fresh instance also computes correctly".
    /// </remarks>
    [Fact]
    public void ReadyRead_ShouldBeStableAcrossReads() {
        var benchmark = new SharedPrimeReadBenchmark();
        benchmark.Setup();

        for (int index = 0; index < 5; ++index) {
            benchmark.ReadyRead().Should().BeTrue();
        }
        // **必须让同一个入口答一个 `false`**：只验证"素数读到 `true`"时，`ReadyRead()` 直接
        // `return true` 也能通过——**那条断言根本没证明它在读缓存**。
        // **The same entry point must answer a `false`**: verifying only "a prime reads as `true`" also passes when `ReadyRead()` simply
        // `return true` — **that assertion never showed it reads the cache**.
        benchmark.Query = new Fuookami.Ospf.Math.Algebra.Number.UInt64(998UL);
        benchmark.ReadyRead().Should().BeFalse("998 不是素数 / 998 is not prime");
        benchmark.Cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64(998UL))
            .Should().BeFalse("998 不是素数 / 998 is not prime");
    }
}
