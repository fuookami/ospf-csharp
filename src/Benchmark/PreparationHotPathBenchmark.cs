#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using System;
using System.Collections.Generic;
using System.Linq;

using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 使用真实入口测量批量准备，规模按 N / 2N / 4N 增长 / Measure the production preparation APIs at N / 2N / 4N sizes.
/// </summary>
/// <remarks>
/// 与基线 `PreparationHotPathBenchmark.kt`（8312 B）对应。
///
/// **基线最后一个基准 `handleConstraintProgrammingSolution`（`bulkSolutionWithSharedOwner`）
/// 依赖 `ManualTokenList.tokensInSolver.reversed` 构造"逆序共享 owner"**；C# 侧 `TokensInSolver` 是
/// **按 `SolverIndex` 升序计算的只读属性**，没有"按注册逆序"的入口。这里保留单项与批量注册两个基准，
/// **不写一个不会真正逆序的替代夹具**——那会让"重复存在性扫描"这一被测行为消失，而基准仍然跑得出数字。
/// 缺口留在 P9 台账。
///
/// **The baseline's `bulkSolutionWithSharedOwner` relies on `ManualTokenList.tokensInSolver.reversed` to build a "reverse-shared owner"**; on the C# side
/// `TokensInSolver` is **a read-only property computed in `SolverIndex` ascending order**, with no way to register in reverse. keeps the
/// single-item and batch registration benchmarks and **does not write a stand-in that never actually reverses** — that would make the "repeated
/// existence scan" behaviour disappear while the benchmark still produces numbers. The gap stays on the P9 ledger.
/// </remarks>
[MemoryDiagnoser]
public class PreparationHotPathBenchmark {
    /// <summary>数据规模 / Dataset size.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    private int _primeLimit;
    private Shape2 _shape = null!;
    private ConstraintProgrammingModelSnapshot _snapshot = null!;
    private ConstraintProgrammingSolution _solution = null!;
    private ConstraintProgrammingCheckpointEnvelope _envelope = null!;
    private Dictionary<VariableItemKey, Token<Flt64>> _tokenMap = null!;
    private IntegerDomain.Values _sparseDomain = null!;
    private List<RealVar> _registrationVariables = null!;
    private readonly FakeConstraintProgrammingSolver _solver = new();

    /// <summary>规模倍数 / The scale factor.</summary>
    internal int Factor => Dataset switch {
        "medium" => 2,
        "large" => 4,
        _ => 1,
    };

    /// <summary>初始化只读输入，缓存捕获夹具包含完整 incumbent / Prepare immutable inputs and a checkpoint containing a complete incumbent.</summary>
    [GlobalSetup]
    public void Setup() {
        int factor = Factor;
        _primeLimit = 10_000 * factor;
        _shape = Shape2.WithOrder(64, 16 * factor, StorageOrder.RowMajor);

        var tokenList = new ManualTokenList<Flt64>(checkTokenExists: true);
        var tokenVariables = new List<RealVar>(1_024 * factor);
        for (int index = 0; index < 1_024 * factor; ++index) {
            tokenVariables.Add(new RealVar($"token_{index}"));
        }
        Try tokenAdded = tokenList.Add(tokenVariables);
        if (!tokenAdded.IsOk) {
            throw new InvalidOperationException(
                "基准 token 必须可注册 / the benchmark tokens must register: " + tokenAdded);
        }
        _tokenMap = tokenList.Tokens
            .ToDictionary(token => token.Key, token => token);
        tokenList.Dispose();

        Result<IntegerDomain.Values, ErrorCode, Error<ErrorCode>> domain =
            IntegerDomain.CreateValues(
                Enumerable.Range(0, 4_096 * factor)
                    .Select(index => new Int64(index * 2L)));
        _sparseDomain = domain is Ok<IntegerDomain.Values, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("基准稀疏值域必须合法 / the sparse domain must be valid");

        _registrationVariables = new List<RealVar>(1_024 * factor);
        for (int index = 0; index < 1_024 * factor; ++index) {
            _registrationVariables.Add(new RealVar($"registration_{index}"));
        }

        var variables = new List<ConstraintProgrammingVariableSnapshot>(128 * factor);
        var values = new Dictionary<VariableId, Int64>(128 * factor);
        for (int index = 0; index < 128 * factor; ++index) {
            var id = new VariableId($"v{index}");
            variables.Add(new ConstraintProgrammingVariableSnapshot(
                id: id,
                name: $"v{index}",
                typeName: "Integer",
                domain: IntegerDomain.Boolean));
            values[id] = Int64.One;
        }
        Result<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            ConstraintProgrammingModelSnapshot.Create(
                name: "preparation",
                objectCategory: ObjectCategory.Minimum,
                variables: variables,
                intervals: Array.Empty<IntervalVariable>(),
                expressions: Array.Empty<ConstraintProgrammingExpressionSnapshot>(),
                constraints: Array.Empty<ConstraintProgrammingConstraintSnapshot>(),
                objectives: Array.Empty<ConstraintProgrammingObjectiveSnapshot>(),
                constraintGroups: Array.Empty<string>());
        _snapshot = snapshot is Ok<
                ConstraintProgrammingModelSnapshot,
                ErrorCode,
                Error<ErrorCode>> snapshotOk
            ? snapshotOk.Value
            : throw new InvalidOperationException(
                "基准快照必须合法 / the benchmark snapshot must be valid: " + snapshot);
        _solution = new ConstraintProgrammingSolution(values: values);
        _envelope = CaptureCheckpoint();
    }

    /// <summary>冷缓存连续素性查询 / Consecutive primality queries from a cold cache.</summary>
    /// <returns>素数数量 / The number of primes.</returns>
    [Benchmark]
    public int ColdPrimeSequence() {
        var cache = new PrimeCache();
        int count = 0;
        for (int value = 2; value <= _primeLimit; ++value) {
            if (cache.IsPrime(new UInt64((ulong)value))) {
                ++count;
            }
        }
        return count;
    }

    /// <summary>返回分配的数组以消耗实际结果 / Return the allocated array so the allocation is actually consumed.</summary>
    /// <returns>常量数组 / The constant-filled array.</returns>
    [Benchmark]
    public MutableMultiArray<int, Shape2> ConstantArray() =>
        MutableMultiArray<int, Shape2>.Factory.NewWith(_shape, 7);

    /// <summary>冷 token 位置索引，包括构造与全部不同 key 的首次查询 / Cold token positions, including construction and first queries for every distinct key.</summary>
    /// <returns>位置校验和 / The position checksum.</returns>
    [Benchmark]
    public int ColdTokenIndices() {
        var tokens = new TokenList<Flt64>(_tokenMap);
        try {
            int sum = 0;
            foreach (Token<Flt64> token in _tokenMap.Values) {
                sum += tokens.IndexOf(token) ?? -1;
            }
            return sum;
        } finally {
            tokens.Dispose();
        }
    }

    /// <summary>固定查询数，仅改变离散域基数 / Keep the query count fixed while varying the sparse-domain cardinality.</summary>
    /// <returns>命中次数 / The membership count.</returns>
    [Benchmark]
    public int SparseDomainMembership() {
        int hits = 0;
        for (long query = -128L; query < 0L; ++query) {
            if (_sparseDomain.Contains(new Int64(query))) {
                ++hits;
            }
        }
        return hits;
    }

    /// <summary>逐项生产注册，包含事务与回调成本 / Per-item production registration, including transactions and callbacks.</summary>
    /// <returns>注册数量 / The registered count.</returns>
    [Benchmark]
    public int SingleTokenRegistration() {
        var tokens = new ManualTokenList<Flt64>(checkTokenExists: true);
        try {
            foreach (RealVar variable in _registrationVariables) {
                tokens.Add(variable);
            }
            return tokens.Tokens.Count;
        } finally {
            tokens.Dispose();
        }
    }

    /// <summary>同一数据的一次批量生产注册，供对照单项入口 / One production batch over the same data, alongside the single-item entry.</summary>
    /// <returns>注册数量 / The registered count.</returns>
    [Benchmark]
    public int BatchTokenRegistration() {
        var tokens = new ManualTokenList<Flt64>(checkTokenExists: true);
        try {
            tokens.Add(_registrationVariables);
            return tokens.Tokens.Count;
        } finally {
            tokens.Dispose();
        }
    }

    /// <summary>捕获完整解，覆盖逐变量值域校验 / Capture a full incumbent, including per-variable domain validation.</summary>
    /// <returns>checkpoint envelope / The checkpoint envelope.</returns>
    [Benchmark]
    public ConstraintProgrammingCheckpointEnvelope CaptureCheckpoint() {
        Result<ConstraintProgrammingCheckpointEnvelope, ErrorCode, Error<ErrorCode>> captured =
            ConstraintProgrammingCheckpointCodec.Capture(
                snapshot: _snapshot,
                descriptor: _solver.Descriptor,
                checkpointId: "preparation-checkpoint",
                createdAtEpochMs: 0L,
                incumbent: _solution,
                configurationFingerprint: "preparation-configuration-fingerprint");
        return captured is Ok<
                ConstraintProgrammingCheckpointEnvelope,
                ErrorCode,
                Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException(
                "基准捕获必须成功 / the benchmark capture must succeed: " + captured);
    }

    /// <summary>恢复完整解，覆盖同一批次的变量索引 / Restore a full incumbent through the batch variable-index path.</summary>
    /// <returns>恢复的变量数量 / The number of restored variables.</returns>
    [Benchmark]
    public int RestoreCheckpoint() {
        Result<ConstraintProgrammingCheckpointRestore, ErrorCode, Error<ErrorCode>> restored =
            ConstraintProgrammingCheckpointCodec.Restore(
                envelope: _envelope,
                snapshot: _snapshot,
                expectedConfigurationFingerprint: "preparation-configuration-fingerprint",
                expectedSolverFingerprint: _envelope.SolverFingerprint,
                allowLegacyConfigurationFingerprint: true);
        ConstraintProgrammingCheckpointRestore value =
            restored is Ok<ConstraintProgrammingCheckpointRestore, ErrorCode, Error<ErrorCode>> ok
                ? ok.Value
                : throw new InvalidOperationException(
                    "基准恢复必须成功 / the benchmark restore must succeed: " + restored);
        return value.Incumbent?.Values.Count
            ?? throw new InvalidOperationException(
                "基准恢复必须带回 incumbent / the benchmark restore must return an incumbent");
    }

    internal ConstraintProgrammingModelSnapshot Snapshot => _snapshot;

    internal int PrimeLimit => _primeLimit;

    internal Shape2 Shape => _shape;
}
