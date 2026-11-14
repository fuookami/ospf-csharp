#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 真实稀疏主问题装配及局部定价准备 / Production sparse master assembly and local pricing preparation.
/// </summary>
/// <remarks>
/// 与基线 `ModelPreparationBenchmark.kt`（5394 B）的两个 `ColumnProjection` 基准对应。
///
/// **基线第三个基准 `registerDependencyChain` 没有移植**：它依赖 `ManualTokenTable` 与
/// `LinearIntermediateSymbol.dependencies` 这两个 **C# 侧不存在的入口**（C# 只有 `ConcurrentTokenTable` 与
/// `ConcurrentMutableTokenTable`，没有"手工指定符号依赖"的公开构造）。**本批不写一个"看起来等价"的替代夹具**——
/// 那会让基准报告里的一个数字指向一段没人验证过的路径。该缺口保留在 P9 台账。
///
/// **The baseline's third benchmark, `registerDependencyChain`, is not ported**: it relies on `ManualTokenTable` and
/// `LinearIntermediateSymbol.dependencies`, **neither of which exists on the C# side** (C# has only `ConcurrentTokenTable` and
/// `ConcurrentMutableTokenTable`, with no public way to declare symbol dependencies by hand). **This batch does not write a "looks equivalent"
/// stand-in fixture** — that would make one number in the benchmark report point at a path nobody validated. The gap stays on the P9 ledger.
/// </remarks>
[MemoryDiagnoser]
public class ModelPreparationBenchmark {
    /// <summary>单维度按 N / 2N / 4N 扩展 / Single-dimension N / 2N / 4N scale.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    private LinearMetaModel<Flt64> _source = null!;
    private ColumnProjection<Flt64> _projection = null!;
    private List<ProjectedColumn<Flt64>> _columns = null!;

    /// <summary>块标识 / Block identities.</summary>
    internal static readonly IReadOnlyList<string> BlockIds =
        Enumerable.Range(0, 8).Select(index => $"b{index}").ToList();

    /// <summary>规模倍数 / The scale factor.</summary>
    internal int Factor => Dataset switch {
        "medium" => 2,
        "large" => 4,
        _ => 1,
    };

    /// <summary>创建只读输入，不计时 / Create the read-only inputs, outside timing.</summary>
    [GlobalSetup]
    public void Setup() {
        int factor = Factor;
        _source = new LinearMetaModel<Flt64>(
            name: "preparation_source",
            objectCategory: ObjectCategory.Minimum);
        var variables = new List<BinVar>(128 * factor);
        for (int index = 0; index < 128 * factor; ++index) {
            variables.Add(new BinVar($"v{index}"));
        }
        Try added = _source.Add(variables);
        if (!added.IsOk) {
            throw new InvalidOperationException(
                "基准变量必须可加入模型 / the benchmark variables must be addable: " + added);
        }
        for (int row = 0; row < 32; ++row) {
            int first = row % variables.Count;
            int second = (first + (variables.Count / 2) + 1) % variables.Count;
            var lhs = new LinearPolynomial<Flt64>(
                new List<LinearMonomial<Flt64>> {
                    new(Flt64.One, variables[first]),
                    new(new Flt64(2), variables[second]),
                },
                Flt64.Zero);
            var rhs = new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), new Flt64(100));
            Try rowAdded = _source.AddConstraint(
                new LinearInequality<Flt64>(lhs, rhs, Comparison.LE, Name: $"r{row}"),
                group: null,
                name: $"r{row}");
            if (!rowAdded.IsOk) {
                throw new InvalidOperationException(
                    "基准行必须可加入模型 / the benchmark rows must be addable: " + rowAdded);
            }
        }
        var decisions = variables
            .Select((variable, index) => new ColumnDecision(
                Id: $"d{index}",
                BlockId: BlockIds[index % BlockIds.Count],
                Variable: variable))
            .ToList();
        _projection = new ColumnProjection<Flt64>(
            source: _source,
            decisions: decisions,
            blockIds: BlockIds);
        _columns = new List<ProjectedColumn<Flt64>>(BlockIds.Count * 8);
        foreach (string block in BlockIds) {
            var mine = decisions
                .Where(decision => decision.BlockId == block)
                .Select(decision => decision.Id)
                .ToList();
            for (int column = 0; column < 8; ++column) {
                var values = new Dictionary<string, Flt64>(StringComparer.Ordinal);
                foreach (string id in mine) {
                    values[id] = new Flt64(column % 2);
                }
                _columns.Add(new ProjectedColumn<Flt64>(
                    id: $"{block}_{column}", blockId: block, values: values));
            }
        }
    }

    /// <summary>
    /// 完整装配后的行数与列变量数之和 / The sum of rows and column variables after full master assembly.
    /// </summary>
    /// <remarks>
    /// **与基线的一处有意偏离**：基线返回 `registration.rows.size`。C# 侧的行来自**源模型**，
    /// 与登记了几列完全无关——实测"一列都不登记"时行数仍是 32，于是**只看行数的基准对列登记是盲的**，
    /// 而这个基准要测的正是"把列装配进主问题"。改为行数 + 列变量数，度量才与被测路径一致。
    ///
    /// **A deliberate divergence from the baseline**: the baseline returns `registration.rows.size`. On the C# side the rows come from the **source
    /// model** and are unrelated to how many columns were registered — measured, "register no columns" still yields 32 rows, so **a row-count-only
    /// benchmark is blind to column registration**, which is exactly the path this benchmark exists to measure. Summing rows and column variables makes
    /// the measurement track the measured path.
    /// </remarks>
    /// <returns>行数与列变量数之和 / The sum of rows and column variables.</returns>
    [Benchmark]
    public int AssembleSparseMaster() {
        Result<ColumnMasterRegistration<Flt64>, ErrorCode, Error<ErrorCode>> registered =
            _projection.MasterAsync(columns: _columns, phaseOne: false, integer: false)
                .GetAwaiter().GetResult();
        ColumnMasterRegistration<Flt64> registration = Unwrap(registered, "master");
        try {
            return registration.Rows.Count + registration.ColumnVariables.Count;
        } finally {
            registration.Model.Close();
        }
    }

    /// <summary>标量入口准备的局部变量数量 / The local-variable count prepared through the scalar APIs.</summary>
    /// <returns>局部变量总数 / The total local-variable count.</returns>
    [Benchmark]
    public int PrepareScalarPricingModels() {
        int amount = 0;
        foreach (string block in BlockIds) {
            amount += PrepareOneBlock(block);
        }
        return amount;
    }

    /// <summary>
    /// 只准备一个块的局部定价模型 / Prepare the local pricing model of a single block.
    /// </summary>
    /// <remarks>
    /// **必须先有当前版本快照**：`PricingModelAsync` 要校验版本，而版本只有在 `SnapshotAsync` 或 `MasterAsync`
    /// 之后才建立。直接调它得到的是 `ApplicationStopped / 模型已关闭` ——**这个失败信息指向"模型被关掉"，
    /// 与真因（还没建立版本）毫无关系**。基准里因此先做一次 `SnapshotAsync`。
    ///
    /// **A current revision must exist first**: `PricingModelAsync` validates the revision, and a revision only exists after `SnapshotAsync` or
    /// `MasterAsync`. Calling it directly yields `ApplicationStopped / model is closed` — **a message that points at "the model was closed" and has
    /// nothing to do with the real cause (no revision yet)**. The benchmark therefore takes one `SnapshotAsync` first.
    /// </remarks>
    /// <returns>局部目标的项数 / The number of terms in the local objective.</returns>
    internal int PrepareOneBlock(string block) {
        Result<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>> snapshot =
            _projection.SnapshotAsync().GetAwaiter().GetResult();
        Unwrap(snapshot, "snapshot");
        Result<ColumnPricingRequest<Flt64>, ErrorCode, Error<ErrorCode>> request =
            _projection.PricingRequestAsync(blockId: block, phaseOne: false, seed: true)
                .GetAwaiter().GetResult();
        ColumnPricingRequest<Flt64> unwrapped = Unwrap(request, "pricing");
        Result<LinearMetaModel<Flt64>, ErrorCode, Error<ErrorCode>> model =
            _projection.PricingModelAsync(unwrapped).GetAwaiter().GetResult();
        LinearMetaModel<Flt64> pricing = Unwrap(model, "pricing model");
        try {
            // **度量目标的项数，不是行数**：本夹具的行由每个块的变量组成，而块的定价模型只有"完全属于本块"的行——
            // 实测为 0。用行数当度量会让这个基准恒为 0，**而一个恒为 0 的基准看起来"跑通了"**。
            // **Measure the objective's term count, not the row count**: the fixture's rows span variables from several blocks, and a block's pricing
            // model keeps only rows that lie entirely inside it — measured as 0. Using the row count would make this benchmark read 0 forever, **and a
            // zero-forever benchmark still looks like it "ran"**.
            return pricing.MetaSubObjects.Sum(sub => sub.Polynomial.Monomials.Count);
        } finally {
            pricing.Close();
        }
    }

    internal LinearMetaModel<Flt64> Source => _source;

    internal ColumnProjection<Flt64> Projection => _projection;

    internal IReadOnlyList<ProjectedColumn<Flt64>> Columns => _columns;

    /// <summary>装配一次稀疏主问题 / Assemble the sparse master once.</summary>
    /// <returns>登记结果 / The registration.</returns>
    internal ColumnMasterRegistration<Flt64> RegisterMaster() {
        Result<ColumnMasterRegistration<Flt64>, ErrorCode, Error<ErrorCode>> registered =
            _projection.MasterAsync(columns: _columns, phaseOne: false, integer: false)
                .GetAwaiter().GetResult();
        return Unwrap(registered, "master");
    }

    private static T Unwrap<T>(
        Result<T, ErrorCode, Error<ErrorCode>> result,
        string consumer) {
        if (result is Ok<T, ErrorCode, Error<ErrorCode>> ok) {
            return ok.Value;
        }
        throw new InvalidOperationException(
            $"基准的 {consumer} 准备必须成功 / the benchmark's {consumer} preparation must succeed: "
            + result);
    }
}
