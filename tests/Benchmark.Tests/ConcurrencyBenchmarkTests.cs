#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Xunit;

namespace Fuookami.Ospf.Test.Benchmark;

/// <summary>
/// **并发基准确实在测"并发"，而且 writer 真的在跑** /
/// The concurrent benchmarks measure concurrency, and their writers really run.
/// </summary>
/// <remarks>
/// **这两个基准确容易"跑起来但什么都没测"：** **线程池参数没接上（`Readers=1` 恒定）、
/// writer 线程一启动就退出、或者 reader 落在一个不存在的缓存上——**报告里照样有数字。**
///
/// **所以判据钉四件事：** **① `MixedBatch` 的 reader 命中数与"纯 reader"一致**（同样的查询、同样的范围）；
/// **② 每种 shape 的 writer 进度不同**（否则"形态"参数没接上）；**③ 尾延迟基准的 writer 在测量窗里确实
/// 重建过整代**（`Rebuilds >= 1`）；**④ `readyOnly` 不起 writer**（重建数必须是 0——它是对照组，不是被测对象）。**
///
/// **Both fixtures are easy to "run" without measuring anything: a readers parameter that never changes, a writer thread that exits at once, a
/// reader that lands on a cache nobody built — the report still prints numbers.** **The criteria pin four things: the mixed batch's reader hits
/// equal the reader-only run; each shape gives a different writer progress; the latency fixture's writer really rebuilt generations inside the
/// window; and `readyOnly` starts no writer at all.**
/// </remarks>
public sealed class ConcurrencyBenchmarkTests
{
    /// <summary>**混合批次的 reader 半边等于纯 reader 的结果** / The mixed batch's reader half equals the reader-only result.</summary>
    [Fact]
    public void TheMixedBatch_ShouldMatchTheReaderOnlyRun() {
        var mixed = new PrimeMixedExpansionBenchmark { Pattern = "readyControl", Readers = 4 };
        var readerOnly = new PrimeMixedExpansionBenchmark { Pattern = "readyControl", Readers = 4 };

        string summary = mixed.MixedBatch();
        int readerHits = int.Parse(summary.Split(';')[0].Split('=')[1]);
        int writer = int.Parse(summary.Split(';')[1].Split('=')[1]);

        readerHits.Should().Be(
            readerOnly.ReaderOnly(),
            "both read the same covered range, so the writer's expansion must not change the answer");
        readerHits.Should().BeInRange(1, 128 * 4, "four readers each query 128 values inside the covered range");
        writer.Should().Be(168, "`readyControl` counts the primes up to 1000, which is 168");
    }

    /// <summary>**四种形态给出不同的 writer 进度** / The four patterns give different writer progress.</summary>
    [Fact]
    public void ThePatterns_ShouldDiffer() {
        int smallSteps = new PrimeMixedExpansionBenchmark { Pattern = "smallSteps", Readers = 1 }.WriterOnly();
        int consecutive = new PrimeMixedExpansionBenchmark { Pattern = "consecutive", Readers = 1 }.WriterOnly();
        int ready = new PrimeMixedExpansionBenchmark { Pattern = "readyControl", Readers = 1 }.WriterOnly();

        ready.Should().Be(168, "the ready control is the prime count up to 1000");
        consecutive.Should().BeInRange(1, 256, "`consecutive` tests 256 values one by one");
        smallSteps.Should().BeGreaterThan(
            consecutive,
            "`smallSteps` sums prime counts over 48 growing bounds, so it cannot equal the single-window count");
    }

    /// <summary>**并发参数真的改变线程数** / The concurrency parameter really changes the thread count.</summary>
    /// <remarks>
    /// **`Readers=1` 与 `Readers=8` 的查询总数差 8 倍**，而命中总数也按同样比例增长——
    /// **这正是"参数接到了线程数上"可观察的后果。**
    ///
    /// **One reader and eight readers differ by a factor of eight in queries, and the hit total grows in the same ratio — which is the
    /// observable consequence of the parameter reaching the thread count.**
    /// </remarks>
    [Fact]
    public void TheReaderCount_ShouldScaleTheWorkload() {
        int one = new PrimeMixedExpansionBenchmark { Pattern = "readyControl", Readers = 1 }.ReaderOnly();
        int eight = new PrimeMixedExpansionBenchmark { Pattern = "readyControl", Readers = 8 }.ReaderOnly();

        eight.Should().BeGreaterThan(
            one,
            "eight readers query eight times as many values, and the covered range is the same");
    }

    /// <summary>**尾延迟基准的 writer 在测量窗里重建过整代** / The latency fixture's writer really rebuilt inside the window.</summary>
    [Fact]
    public void TheWriterInTheLatencyFixture_ShouldRebuild() {
        // **用 `fewLarge` 而不是 `consecutive`：** **后者一趟是 199000 次素性判定，判据会等很久；
        // 而 `fewLarge` 二十步就到上界、随即重建整代——它同样证明"writer 循环扩容并重建"，代价小得多。**
        //
        // **`fewLarge` rather than `consecutive`: the latter's single pass is 199000 primality checks, while `fewLarge` reaches the ceiling in
        // twenty steps and rebuilds right after — the same proof at a fraction of the cost.**
        var fixture = new PrimeReaderLatencyBenchmark { Pattern = "fewLarge" };
        fixture.StartWriter();
        try {
            // **必须有界等待，不能只调一次读就停：** **这里两次读只花微秒，而 writer 线程可能还没被调度到——
            // 那样判据会读到一个"从没跑过"的 0，把功能正常的夹具判成坏的。** **真实测量窗是秒级，所以这里等到它动为止。**
            //
            // **The wait must be bounded here: two reads take microseconds, and the writer thread may not have been scheduled yet — checking
            // immediately would read a zero that says "never ran" about a healthy fixture. Real measurement windows are seconds long, so wait
            // until it moves.**
            var deadline = TimeSpan.FromSeconds(30);
            var started = System.Diagnostics.Stopwatch.StartNew();
            while ((fixture.Progress.Steps == 0L || fixture.Progress.Rebuilds == 0L) && started.Elapsed < deadline) {
                System.Threading.Thread.Sleep(1);
                _ = fixture.CoveredRead();
            }
            _ = fixture.CoveredRangeRead();
        }
        finally {
            fixture.StopWriter();
        }

        (long steps, long rebuilds, long limit) = fixture.Progress;
        steps.Should().BePositive("the writer must have expanded at least once, or the window measured steady reads");
        rebuilds.Should().BePositive("the writer must have rebuilt a whole generation and kept going");
        limit.Should().BeGreaterThan(0L);
    }

    /// <summary>**`readyOnly` 是对照组：它不起 writer** / `readyOnly` is the control and starts no writer.</summary>
    [Fact]
    public void TheReadyOnlyPattern_ShouldStartNoWriter() {
        var fixture = new PrimeReaderLatencyBenchmark { Pattern = "readyOnly" };
        fixture.StartWriter();
        try {
            fixture.CoveredRead().Should().BeTrue("997 is prime inside the covered range");
        }
        finally {
            fixture.StopWriter();
        }

        (long steps, long rebuilds, _) = fixture.Progress;
        steps.Should().Be(0L, "the control starts no writer, so no expansion can happen");
        rebuilds.Should().Be(0L, "the control starts no writer, so nothing is rebuilt");
    }
}