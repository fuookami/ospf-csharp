#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 多 reader 共享已建表的生产素性查询，不测后台预热 / Production primality reads sharing a ready table, not background warmup.
/// </summary>
/// <remarks>
/// 与基线 `SharedPrimeReadBenchmark.kt`（923 B）对应。
///
/// **建表在计时外，读线程只查询已覆盖范围**。查询值一旦超出 `Setup` 建好的范围，每次读都会走"扩展筛法"，
/// 于是这个基准就从"共享读"变成了"扩展写"——**两者数量级不同，而断言/日志里看不出来**。
///
/// **The table is built outside timing and readers only query the covered range.** Once the query value falls outside what `Setup` built, every read
/// goes through "extend the sieve", turning this benchmark from a shared read into an expanding write — **different by orders of magnitude, and nothing in
/// the log shows it**.
/// </remarks>
[MemoryDiagnoser]
public class SharedPrimeReadBenchmark {
    /// <summary>已建表的默认上界 / The default upper bound of the built table.</summary>
    public const ulong DefaultSieveBound = 1000UL;

    /// <summary>被查询的值，必须落在 <see cref="DefaultSieveBound"/> 内 / The queried value, which must lie within <see cref="DefaultSieveBound"/>.</summary>
    public const ulong QueryValue = 997UL;

    private PrimeCache _cache = null!;
    private UInt64 _query;

    /// <summary>建表在计时之外 / Build the table outside timing.</summary>
    [GlobalSetup]
    public void Setup() {
        _cache = new PrimeCache();
        _query = new UInt64(QueryValue);
    }

    /// <summary>
    /// 已缓存的素性结果 / The cached primality result.
    /// </summary>
    /// <returns>是否素数 / Whether the value is prime.</returns>
    [Benchmark]
    public bool ReadyRead() => _cache.IsPrime(_query);

    /// <summary>
    /// 查询值 / The queried value.
    /// </summary>
    /// <remarks>
    /// **可写是为了让测试能把查询换成一个合数**：只断言"读到一个正确的 `true`"时，
    /// `ReadyRead` 直接 `return true` 也能通过——**那条测试根本没证明它在读缓存**。
    /// **Settable so a test can point the query at a composite**: asserting only "a correct `true` came back" also passes when `ReadyRead` simply
    /// `return true` — **that test never showed it reads the cache**.
    /// </remarks>
    internal UInt64 Query {
        get => _query;
        set => _query = value;
    }

    /// <summary>素数缓存 / The prime cache.</summary>
    internal PrimeCache Cache => _cache;
}
