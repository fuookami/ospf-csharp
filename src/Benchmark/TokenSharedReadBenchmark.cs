#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// **共享同一份 token 列表的并发首读、稳态读，以及真实的建表工作量** /
/// Concurrent first reads, steady reads and real index-build work over one shared token list.
/// </summary>
/// <remarks>
/// ****
///
/// **它补的是 `PreparationHotPathBenchmark` 的空档：** **那个基准的 `ColdTokenIndices` 是单线程整体成本哨兵**，
/// 而这里所有 reader 共享同一个列表和同一组 token**，一次冷批次作为一个完整操作计时**——
/// **含线程调度、栅栏对齐和全部 reader 完成。** **`DispatchOnly` 是同一调度协议下的空跑对照，
/// 所以"调度开销"和"建表开销"能分开。**
///
/// **建表工作量由变量标识符的读取次数给出：** **一次完整建表约等于每个 token 一次读，查询本身另计；
/// 超过这个数的读取就是重复建表。** **这正是 `ColdBuildWork` 返回 `reads=…;indexSum=…` 的原因。**
///
/// **Corresponds to its 184-line.** **It fills a gap left by `PreparationHotPathBenchmark`: that one's
/// `ColdTokenIndices` is a single-threaded whole-cost sentinel, while here every reader shares one list and one token set and a cold batch is
/// timed as one operation including dispatch, barrier alignment and completion of all readers.** **`DispatchOnly` is the empty-dispatch control
/// under the same protocol, so scheduling cost and build cost separate.** **Build work is counted as variable-identifier reads: one full build
/// costs about one read per token plus one per query, so reads above that are repeated builds — which is why `ColdBuildWork` reports
/// `reads=…;indexSum=…`.**
/// </remarks>
[MemoryDiagnoser]
public class TokenSharedReadBenchmark
{
    /// <summary>规模档位 / The dataset scale.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    /// <summary>同时首次访问的 reader 数 / The number of readers performing the first accesses together.</summary>
    [Params(1, 2, 4, 8)]
    public int Readers { get; set; } = 1;

    /// <summary>规模 / The scale.</summary>
    internal int Size => Dataset switch {
        "medium" => 2_048,
        "large" => 4_096,
        _ => 1_024,
    };

    /// <summary>最近一次冷建表的标识符读取次数（测试可见）/ Identifier reads of the last cold build (visible to tests).</summary>
    internal long LastBuildReads { get; private set; }

    private Dictionary<VariableItemKey, Token<Flt64>> _tokenMap = null!;
    private IReadOnlyList<Token<Flt64>> _ordered = null!;
    private TokenList<Flt64> _warmList = null!;
    private TokenList<Flt64> _countingList = null!;
    private IReadOnlyList<Token<Flt64>> _countingOrdered = null!;
    private ManualTokenList<Flt64> _writer = null!;
    private ManualTokenList<Flt64> _countingWriter = null!;
    private long _identifierReads;

    /// <summary>共享列表、已就绪索引与 reader 线程都在计时外建立 / Shared lists, ready index and reader threads are built outside timing.</summary>
    [GlobalSetup]
    public void Setup() {
        _writer = new ManualTokenList<Flt64>(checkTokenExists: true);
        var variables = new List<RealVar>(Size);
        for (int index = 0; index < Size; ++index) {
            variables.Add(new RealVar($"shared_{index}"));
        }
        _writer.Add(variables);
        _tokenMap = _writer.Tokens.ToDictionary(token => token.Key, token => token);
        _warmList = new TokenList<Flt64>(_tokenMap);
        _ordered = _warmList.TokensInSolver;
        // **先读一次把索引建起来：** **否则第一次测量里混着冷建表，而这里要测的是稳态读。**
        // **Prime the index, or the first measurement would include the cold build.**
        _ = _warmList.IndexOf(_ordered[^1]);

        _countingWriter = new ManualTokenList<Flt64>(checkTokenExists: true);
        var counting = new List<CountingVariable>(Size);
        for (int index = 0; index < Size; ++index) {
            counting.Add(new CountingVariable(1_000_000 + index, $"counted_{index}", this));
        }
        _countingWriter.Add(counting);
        _countingList = new TokenList<Flt64>(
            _countingWriter.Tokens.ToDictionary(token => token.Key, token => token));
        _countingOrdered = _countingList.TokensInSolver;
    }

    /// <summary>解除列表回调 / Detach the list callbacks.</summary>
    [GlobalCleanup]
    public void Cleanup() {
        _warmList.Dispose();
        _countingList.Dispose();
        _countingWriter.Dispose();
        _writer.Dispose();
    }

    /// <summary>已就绪共享列表上的稳态读索引和 / Index sum of steady reads on the ready shared list.</summary>
    /// <returns>索引和 / The index sum</returns>
    [Benchmark]
    public int WarmSharedRead() => RunBatch(slot => {
        int sum = 0;
        for (int step = 0; step < 64; ++step) {
            sum += _warmList.IndexOf(_ordered[(slot * 64 + step) % Size]) ?? -1;
        }
        return sum;
    });

    /// <summary>一次完整冷并发首读批次 / One complete cold concurrent first-read batch.</summary>
    /// <returns>索引和 / The index sum</returns>
    [Benchmark]
    public int ColdFirstReadBatch() {
        var list = new TokenList<Flt64>(_tokenMap);
        try {
            return RunBatch(slot => list.IndexOf(_ordered[(slot * 131 + 7) % Size]) ?? -1);
        }
        finally {
            list.Dispose();
        }
    }

    /// <summary>每个 reader 查不同 key 的冷批次 / A cold batch where every reader queries distinct keys.</summary>
    /// <returns>索引和 / The index sum</returns>
    [Benchmark]
    public int ColdDistinctKeyBatch() {
        var list = new TokenList<Flt64>(_tokenMap);
        try {
            return RunBatch(slot => {
                int sum = 0;
                for (int step = 0; step < 16; ++step) {
                    sum += list.IndexOf(_ordered[(slot * 16 + step) % Size]) ?? -1;
                }
                return sum;
            });
        }
        finally {
            list.Dispose();
        }
    }

    /// <summary>同一调度协议下的空调度对照 / The empty-dispatch control under the same protocol.</summary>
    /// <returns>槽位号之和 / The sum of slot numbers</returns>
    [Benchmark]
    public int DispatchOnly() => RunBatch(slot => slot);

    /// <summary>一次冷并发批次的标识符读取次数与索引和 / Identifier reads and index sum of one cold batch.</summary>
    /// <returns>`reads=…;indexSum=…` / `reads=…;indexSum=…`</returns>
    [Benchmark]
    public string ColdBuildWork() {
        Interlocked.Exchange(ref _identifierReads, 0L);
        var list = new TokenList<Flt64>(_countingList.Tokens.ToDictionary(token => token.Key, token => token));
        try {
            int sum = RunBatch(slot => list.IndexOf(_countingOrdered[(slot * 131 + 7) % Size]) ?? -1);
            LastBuildReads = Interlocked.Read(ref _identifierReads);
            return "reads=" + LastBuildReads + ";indexSum=" + sum;
        }
        finally {
            list.Dispose();
        }
    }

    /// <summary>在栅栏上对齐后并发执行一批 / Run one batch after aligning every reader on a barrier.</summary>
    /// <param name="body">每个 reader 的工作 / The work of one reader.</param>
    /// <returns>各 reader 结果之和 / The sum of the reader results</returns>
    private int RunBatch(Func<int, int> body) {
        using var barrier = new Barrier(Readers);
        var workers = new Thread[Readers];
        int[] results = new int[Readers];
        for (int slot = 0; slot < Readers; ++slot) {
            int captured = slot;
            workers[slot] = new Thread(() => {
                barrier.SignalAndWait();
                results[captured] = body(captured);
            });
            workers[slot].Start();
        }

        foreach (Thread worker in workers) {
            worker.Join();
        }

        int total = 0;
        foreach (int result in results) {
            total += result;
        }
        return total;
    }

    /// <summary>标识符读取可计数的变量项 / A variable item whose identifier reads are counted.</summary>
    /// <remarks>
    /// **`CountingVariable` 在这里照搬：** **标识符由夹具自己固定分配，不依赖内部生成器，
    /// 于是"读取次数"就是"建表时问过多少次标识符"的直接读数。**
    ///
    /// **`CountingVariable` is mirrored here: the identifier is fixed by the fixture rather than generated, so the read count is a
    /// direct reading of how often a build asked for an identifier.**
    /// </remarks>
    private sealed class CountingVariable : AbstractVariableItem<Flt64, Continuous>
    {
        private readonly Fuookami.Ospf.Math.Algebra.Number.UInt64 _fixed;
        private readonly TokenSharedReadBenchmark _owner;

        internal CountingVariable(int fixedIdentifier, string name, TokenSharedReadBenchmark owner)
            : base(Continuous.Instance, name, NumericConstantsRegistry.For<Flt64>()) {
            _fixed = new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)fixedIdentifier);
            _owner = owner;
        }

        public override Fuookami.Ospf.Math.Algebra.Number.UInt64 Identifier {
            get {
                Interlocked.Increment(ref _owner._identifierReads);
                return _fixed;
            }
        }

        public override int Dimension => 0;

        public override int Index => 0;

        public override int[] VectorView => _vectorView;

        private static readonly int[] _vectorView = [0];
    }
}