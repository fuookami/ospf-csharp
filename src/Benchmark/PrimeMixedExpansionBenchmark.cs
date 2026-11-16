#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// **reader 与 writer 同时动同一个缓存**：混合批次、纯 writer、纯 reader /
/// Readers and one writer sharing the same cache: mixed batch, writer only, readers only.
/// </summary>
/// <remarks>
/// **只做前半段。**
///
/// **它测的是既有两个素性基准都不测的形状：** **`SharedPrimeReadBenchmark` 让多 reader 共享一张已经建好的表，
/// 而这个基准让 reader 在查、writer 在建**——**缓存在查询途中扩容，读者可能落在旧代上。**
///
/// **三个方法把成本拆开：** **`MixedBatch` 是两者的叠加，`WriterOnly` 是扩容序列本身，
/// `ReaderOnly` 是读者在没有 writer 时的建代成本。** **三者相减才知道"叠加"是不是真的只是相加。**
///
/// **`CyclicBarrier` 的对应物是 .NET 的 `Barrier`**：**每个 reader 在屏障上对齐后才开始读，
/// 这样"并发"是真的同时开始，而不是线程池调度先后。**
///
/// **Corresponds to the first half of the file.** **The existing primality benchmarks do not cover this shape: `SharedPrimeReadBenchmark`
/// lets several readers share a table that is already built, while here the readers query while a writer expands the cache, so a reader can
/// land on an older generation.** **The three methods split the cost: `MixedBatch` runs both, `WriterOnly` is the expansion sequence alone, and
/// `ReaderOnly` is the readers' build cost without a writer — so the difference reveals whether the overlap is really just a sum.**
/// **`CyclicBarrier` maps to .NET's `Barrier`, so the readers really start together instead of in thread-pool order.**
/// </remarks>
[MemoryDiagnoser]
public class PrimeMixedExpansionBenchmark
{
    private const int CoveredRange = 1_000;

    /// <summary>writer 的扩容形态 / The writer's expansion pattern.</summary>
    [Params("readyControl", "fewLarge", "consecutive", "smallSteps")]
    public string Pattern { get; set; } = "readyControl";

    /// <summary>并发 reader 数 / The number of concurrent readers.</summary>
    [Params(1, 2, 4, 8)]
    public int Readers { get; set; } = 1;

    /// <summary>混合批次：reader 查、writer 建，同时进行 / Mixed: readers query while the writer builds.</summary>
    /// <returns>`readerHits=…;writer=…` / `readerHits=…;writer=…`</returns>
    [Benchmark]
    public string MixedBatch() {
        var cache = new PrimeCache();
        using var barrier = new Barrier(Readers);
        var readers = new Thread[Readers];
        int[] hits = new int[Readers];
        for (int slot = 0; slot < Readers; ++slot) {
            int captured = slot;
            readers[slot] = new Thread(() => {
                barrier.SignalAndWait();
                int count = 0;
                for (int step = 0; step < 128; ++step) {
                    if (cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)((captured * 128 + step) % CoveredRange)))) {
                        ++count;
                    }
                }
                hits[captured] = count;
            });
            readers[slot].Start();
        }

        int writerProgress = Expand(cache);
        foreach (Thread reader in readers) {
            reader.Join();
        }

        int readerHits = 0;
        foreach (int count in hits) {
            readerHits += count;
        }
        return "readerHits=" + readerHits + ";writer=" + writerProgress;
    }

    /// <summary>单线程扩容序列，作为没有 reader 并发时的 writer 基线 / The expansion sequence alone.</summary>
    /// <returns>writer 进度 / Writer progress</returns>
    [Benchmark]
    public int WriterOnly() => Expand(new PrimeCache());

    /// <summary>只有 reader 的共享冷批次，用来分离 reader 自己的建代成本 / Readers only, separating their own build cost.</summary>
    /// <returns>reader 命中数 / Reader hit count</returns>
    [Benchmark]
    public int ReaderOnly() {
        var cache = new PrimeCache();
        using var barrier = new Barrier(Readers);
        var readers = new Thread[Readers];
        int[] hits = new int[Readers];
        for (int slot = 0; slot < Readers; ++slot) {
            int captured = slot;
            readers[slot] = new Thread(() => {
                barrier.SignalAndWait();
                int count = 0;
                for (int step = 0; step < 128; ++step) {
                    if (cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)((captured * 128 + step) % CoveredRange)))) {
                        ++count;
                    }
                }
                hits[captured] = count;
            });
            readers[slot].Start();
        }

        foreach (Thread reader in readers) {
            reader.Join();
        }

        int total = 0;
        foreach (int count in hits) {
            total += count;
        }
        return total;
    }

    /// <summary>按形态执行一轮扩容 / Run one expansion pass for the pattern.</summary>
    /// <param name="cache">被扩容的缓存 / The cache being expanded.</param>
    /// <returns>本轮进度 / Progress of this pass</returns>
    private int Expand(PrimeCache cache) => Pattern switch {
        "fewLarge" => Sum(new[] { 50_000UL, 200_000UL, 500_000UL }, cache),
        "consecutive" => CountConsecutive(cache),
        "smallSteps" => SmallSteps(cache),
        _ => cache.GetPrimes(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)CoveredRange)).Count,
    };

    private static int Sum(IReadOnlyList<ulong> limits, PrimeCache cache) {
        int total = 0;
        foreach (ulong limit in limits) {
            total += cache.GetPrimes(new Fuookami.Ospf.Math.Algebra.Number.UInt64(limit)).Count;
        }
        return total;
    }

    private static int CountConsecutive(PrimeCache cache) {
        int count = 0;
        for (int value = CoveredRange; value < CoveredRange + 256; ++value) {
            if (cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)value))) {
                ++count;
            }
        }
        return count;
    }

    private static int SmallSteps(PrimeCache cache) {
        int limit = CoveredRange;
        int progress = 0;
        for (int round = 0; round < 48; ++round) {
            limit += 211;
            progress += cache.GetPrimes(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)limit)).Count;
        }
        return progress;
    }
}

/// <summary>
/// **reader 尾延迟：writer 在整个计时窗里持续扩容，reader 只读已覆盖范围** /
/// Reader tail latency: the writer keeps expanding through the whole window while readers stay inside the covered range.
/// </summary>
/// <remarks>
/// **与后半段的 `PrimeReaderLatencyBenchmark` 对应。**
///
/// **writer 是唯一的扩容者，而且在每个测量迭代里循环扩容、到界后重建一整代再继续——
/// **这样扩容压力贯穿整个计时窗，不会因为序列跑完而退化成"稳态读"。** **重建成本只落在 writer 线程上；
/// reader 只按 volatile 引用读当前代，不加锁、不新增原子写。**
///
/// **一个诚实的能力差：** **JMH `Mode.SampleTime` 取 p95/p99，而本仓的基准运行器只跑
/// BenchmarkDotNet 的默认模式，不产出采样分位数**。** **所以这里能测的是"读过一次要多久"，不是"尾延迟"。**
/// **会话限：** **要真的量尾延迟，得给运行器加 `SampleTime` 等价物（BDN 没有同名模式，需要按迭代取样自建），
/// 那是一件独立的工作。** **在那之前，这个基准的名字里不写"尾延迟"以外的承诺。**
///
/// **Corresponds to the `PrimeReaderLatencyBenchmark` half of the file.** **The writer is the only expander and it loops the
/// sequence inside every measurement iteration, rebuilding a whole generation at the ceiling, so expansion pressure spans the window instead
/// of degrading into steady reads; rebuild cost lands on the writer thread, and readers only load a volatile reference.** **One honest gap:
/// JMH `Mode.SampleTime` yields p95/p99, while this repo's runner only drives BenchmarkDotNet's defaults and produces no sample
/// percentiles — so what is measured here is "how long a read takes", not "tail latency".** **Ceiling: measuring tail latency needs a SampleTime
/// equivalent in the runner (BDN has no such mode; it would have to sample per iteration), which is separate work.**
/// </remarks>
[MemoryDiagnoser]
public class PrimeReaderLatencyBenchmark
{
    private const int CoveredRange = 1_000;
    private const int ExpansionCeiling = 200_000;

    /// <summary>writer 的扩容形态 / The writer's expansion pattern.</summary>
    [Params("readyOnly", "smallSteps", "consecutive", "fewLarge")]
    public string Pattern { get; set; } = "readyOnly";

    // **`volatile` 发布：** **writer 在迭代中间换整代，读到旧代仍然是一份完整快照（旧代发布后不可变）。**
    // **Published as `volatile`: the writer swaps generations mid-iteration, and an old generation is still a complete snapshot.**
    private volatile PrimeCache _cache = new();
    private volatile bool _running;
    private Thread? _writer;
    private long _writerSteps;
    private long _writerRebuilds;
    private long _writerLimit;

    /// <summary>每次迭代重置为新的已就绪代，必要时启动循环扩容的 writer / Reset per iteration and start the writer.</summary>
    [IterationSetup]
    public void StartWriter() {
        _cache = ReadyCache();
        Interlocked.Exchange(ref _writerSteps, 0L);
        Interlocked.Exchange(ref _writerRebuilds, 0L);
        Interlocked.Exchange(ref _writerLimit, CoveredRange);
        _running = Pattern != "readyOnly";
        if (_running) {
            _writer = new Thread(() => {
                while (_running) {
                    ExpandPass();
                    if (!_running) {
                        break;
                    }
                    // **固定协议：到界后重建一整代再继续，保证扩容贯穿整个计时窗。**
                    // **Fixed protocol: rebuild a whole generation at the ceiling so expansion spans the window.**
                    _cache = ReadyCache();
                    Interlocked.Increment(ref _writerRebuilds);
                    Interlocked.Exchange(ref _writerLimit, CoveredRange);
                }
            }) {
                IsBackground = true,
                Name = "benchmark-prime-writer",
            };
            _writer.Start();
        }
        else {
            _writer = null;
        }
    }

    /// <summary>停止 writer / Stop the writer.</summary>
    [IterationCleanup]
    public void StopWriter() {
        _running = false;
        _writer?.Join(TimeSpan.FromSeconds(120));
        _writer = null;
    }

    /// <summary>已覆盖范围内的单点素性读 / A single primality read inside the covered range.</summary>
    /// <returns>素性结果 / The primality result</returns>
    [Benchmark]
    public bool CoveredRead() => _cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64(997UL));

    /// <summary>已覆盖范围内的区间素数计数 / A prime count over a covered range.</summary>
    /// <returns>不超过查询上界的素数数量 / The prime count within the bound</returns>
    [Benchmark]
    public int CoveredRangeRead() => _cache.GetPrimes(new Fuookami.Ospf.Math.Algebra.Number.UInt64(500UL)).Count;

    /// <summary>迭代信息（供诊断读） / The iteration figures, readable for diagnosis.</summary>
    internal (long Steps, long Rebuilds, long Limit) Progress =>
        (Interlocked.Read(ref _writerSteps), Interlocked.Read(ref _writerRebuilds), Interlocked.Read(ref _writerLimit));

    private static PrimeCache ReadyCache() {
        var cache = new PrimeCache();
        _ = cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64(997UL));
        return cache;
    }

    private void ExpandPass() {
        switch (Pattern) {
            case "consecutive": {
                for (int value = CoveredRange; value < ExpansionCeiling; ++value) {
                    if (!_running) {
                        return;
                    }
                    _cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)value));
                    Interlocked.Increment(ref _writerSteps);
                    Interlocked.Exchange(ref _writerLimit, value);
                }
                break;
            }
            case "fewLarge": {
                long limit = CoveredRange;
                while (limit < ExpansionCeiling) {
                    if (!_running) {
                        return;
                    }
                    limit += 10_000L;
                    _cache.GetPrimes(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)limit));
                    Interlocked.Increment(ref _writerSteps);
                    Interlocked.Exchange(ref _writerLimit, limit);
                }
                break;
            }
            default: {
                long limit = CoveredRange;
                while (limit < ExpansionCeiling) {
                    if (!_running) {
                        return;
                    }
                    limit += 211L;
                    _cache.GetPrimes(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)limit));
                    Interlocked.Increment(ref _writerSteps);
                    Interlocked.Exchange(ref _writerLimit, limit);
                }
                break;
            }
        }
    }
}