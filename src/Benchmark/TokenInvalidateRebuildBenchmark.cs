#nullable enable

using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// **索引失效后的下一次读** / The first read after the index was invalidated.
/// </summary>
/// <remarks>
/// **与基线 `TokenInvalidateRebuildBenchmark.kt`（104 行，`19c72455` 新增）对应。**
///
/// **它测的东西，既有基准都不测：** **每次操作先增删同一个 token 让缓存失效，再对一个稳定 token 做首读，
/// 迫使索引重建；同一批还有两个对照组（只读、只增删），把"重建成本"从"读成本"和"改成本"里分出来。**
///
/// **修改只发生在调用线程上**，符合列表自身的单写锁契约，不涉及未支持的并发写。
///
/// **Corresponds to the baseline's `TokenInvalidateRebuildBenchmark.kt` (104 lines, added in `19c72455`).** **Each operation adds
/// then removes one token to invalidate the cache, then performs a first read of a stable token to force a rebuild; two controls
/// (reads only, mutations only) separate rebuild cost from read cost and mutation cost.** **Mutation stays on the calling thread,
/// matching the list's single-writer lock contract.**
/// </remarks>
[MemoryDiagnoser]
public class TokenInvalidateRebuildBenchmark
{
    /// <summary>规模档位 / The dataset scale.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    /// <summary>每操作的失效重建周期数 / Invalidation-rebuild cycles per operation.</summary>
    [Params(16)]
    public int Cycles { get; set; } = 16;

    private ManualTokenList<Flt64> _writer = null!;
    private IReadOnlyList<Token<Flt64>> _stable = null!;
    private List<RealVar> _probes = null!;

    /// <summary>
    /// **计时外的稳定 token 数量** / The number of stable tokens, built outside timing.
    /// </summary>
    /// <remarks>
    /// **暴露它只为了让判据能核对"规模档位真的建对了"。** **时间数据本身看不出这一点：档位写错时报告照样出数，只变了分母。**
    ///
    /// **Exposed only so a criterion can check the scale was really built.** **Timings cannot show it: with a wrong scale the report
    /// still prints numbers, only the denominator changed.**
    /// </remarks>
    public int StableTokenCount => _stable.Count;

    private int Size => Dataset switch {
        "medium" => 2_048,
        "large" => 4_096,
        _ => 1_024,
    };

    /// <summary>固定结构、已就绪索引与探针变量都在计时外建立 / Fixed structure, ready index and probes are built outside timing.</summary>
    [GlobalSetup]
    public void Setup() {
        _writer = new ManualTokenList<Flt64>(checkTokenExists: true);
        var variables = new List<RealVar>(Size);
        for (int index = 0; index < Size; ++index) {
            variables.Add(new RealVar($"ir_{index}"));
        }
        Try added = _writer.Add(variables);
        if (!added.IsOk) {
            throw new InvalidOperationException("基准 token 必须可注册 / the benchmark tokens must register: " + added);
        }

        _stable = _writer.TokensInSolver;
        // **先做一次读，把索引建起来：** **否则第一次测量里混着"冷建表"，而这里要测的是"失效后的重建"。**
        // **Prime the index first, or the first measurement would include the cold build.**
        _ = _writer.IndexOf(_stable[^1]);

        _probes = new List<RealVar>(64);
        for (int index = 0; index < 64; ++index) {
            _probes.Add(new RealVar($"probe_{index}"));
        }
    }

    /// <summary>解除列表回调 / Detach the list callbacks.</summary>
    [GlobalCleanup]
    public void Cleanup() => _writer.Dispose();

    /// <summary>
    /// **增删 → 首读 → 增删**：一次操作内完成固定的重建次数 / Add-remove, read, remove: a fixed number of rebuilds per operation.
    /// </summary>
    [Benchmark]
    public int InvalidateThenRebuild() {
        int sum = 0;
        for (int cycle = 0; cycle < Cycles; ++cycle) {
            RealVar probe = _probes[cycle % _probes.Count];
            Try added = _writer.Add(probe);
            try {
                sum += _writer.IndexOf(_stable[cycle % _stable.Count]) ?? -1;
            }
            finally {
                _writer.Remove(probe);
            }
            // **`Add` 的结果必须被消费：** **重复注册会被拒，而这次失败会让"失效"没有真的发生，
            // 于是这里测到的仍是稳态读——一个静默的假测量。**
            //
            // **The `Add` result must be consumed: a rejected duplicate registration would mean the index was never invalidated,
            // and this benchmark would silently measure steady-state reads instead.**
            if (!added.IsOk) {
                throw new InvalidOperationException("探针 token 必须能注册 / the probe token must register: " + added);
            }
        }
        return sum;
    }

    /// <summary>对照：不做失效与重建，只做同量读取 / Control: the same number of reads, no invalidation.</summary>
    [Benchmark]
    public int WarmReadsOnly() {
        int sum = 0;
        for (int cycle = 0; cycle < Cycles; ++cycle) {
            sum += _writer.IndexOf(_stable[cycle % _stable.Count]) ?? -1;
        }
        return sum;
    }

    /// <summary>对照：只做增删而不读取 / Control: add and remove only.</summary>
    [Benchmark]
    public int MutationOnly() {
        for (int cycle = 0; cycle < Cycles; ++cycle) {
            RealVar probe = _probes[cycle % _probes.Count];
            Try added = _writer.Add(probe);
            _writer.Remove(probe);
            if (!added.IsOk) {
                throw new InvalidOperationException("探针 token 必须能注册 / the probe token must register: " + added);
            }
        }
        return Cycles;
    }
}