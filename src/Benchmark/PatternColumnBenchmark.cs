#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;
using Fuookami.Ospf.Framework.Bpp1d.Domain.PatternAssignment;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 按批追加方案列：注册、追加、关闭完整模型 / Append pattern columns in batches: register, append, close a full model.
/// </summary>
/// <remarks>
/// 补齐**：一直有，本仓没有。
/// 它测的是**列生成的写入侧成本**——`addColumns` 每加一批列，模型的行、列与基础目标项都跟着长。
/// **这条路径不会被"求解一次"的基准覆盖**：求解基准里列数固定，而这里的开销随列数线性增长。
///
/// 规格：一个物料（`amount = 10000`）、一个箱型（长与载重都是 10000）、间距 0；
/// 列数由 `dataset` 决定（256 / 512 / 1024），每条列消耗 `i` 件物料，按 `batchSize` 分批追加。
///
/// fills a gap**: its (74 lines) has always existed and this repo had none. It measures **the write side of column
/// generation** — each `addColumns` batch grows the model's rows, columns and base objective, and **no solve-once benchmark covers that path**, because a solve benchmark
/// fixes the column count while this cost grows linearly with it. The spec matches the code: one item (`amount = 10000`), one bin type (length and capacity both
/// 10000), zero spacing, `dataset` deciding the column count (256 / 512 / 1024), each column consuming `i` items, appended in `batchSize` batches.
/// </remarks>
[MemoryDiagnoser]
public class PatternColumnBenchmark {
    /// <summary>列数规模 / The column-count scale.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    /// <summary>每批追加多少列 / How many columns per batch.</summary>
    [Params(1, 32)]
    public int BatchSize { get; set; } = 1;

    /// <summary>夹具输入（测试可见）/ The fixture input (visible to tests).</summary>
    internal PackingProblem<Flt64> Problem => _problem;

    /// <summary>分批后的列（测试可见）/ The batched columns (visible to tests).</summary>
    internal IReadOnlyList<IReadOnlyList<PackingPattern<Flt64>>> Batches => _batches;

    private PackingProblem<Flt64> _problem = null!;
    private List<IReadOnlyList<PackingPattern<Flt64>>> _batches = null!;

    /// <summary>只随列数与批大小变化的固定输入 / Fixed input varying only in column count and batch size.</summary>
    [GlobalSetup]
    public void Setup() {
        int columns = Dataset switch {
            "medium" => 512,
            "large" => 1024,
            _ => 256,
        };

        _problem = new PackingProblem<Flt64>(
            Items: [
                new Item<Flt64>(
                    "item",
                    new Quantity<Flt64>(Flt64.One, SIBaseUnits.Meter),
                    new Quantity<Flt64>(Flt64.One, SIBaseUnits.Kilogram),
                    10_000UL),
            ],
            BinTypes: [
                new BinType<Flt64>(
                    "bin",
                    new Quantity<Flt64>(new Flt64(10_000), SIBaseUnits.Meter),
                    new Quantity<Flt64>(new Flt64(10_000), SIBaseUnits.Kilogram),
                    Flt64.One),
            ],
            Spacing: new Quantity<Flt64>(Flt64.Zero, SIBaseUnits.Meter));

        var patterns = new List<PackingPattern<Flt64>>(columns);
        for (int index = 1; index <= columns; ++index) {
            patterns.Add(new PackingPattern<Flt64>(
                "bin",
                new Dictionary<string, ulong> { ["item"] = (ulong)index }));
        }

        _batches = Enumerable.Range(0, (patterns.Count + BatchSize - 1) / BatchSize)
            .Select(batch => (IReadOnlyList<PackingPattern<Flt64>>)patterns
                .Skip(batch * BatchSize)
                .Take(BatchSize)
                .ToArray())
            .ToList();
    }

    /// <summary>注册、逐批追加、关闭，并消费实际生成的基础目标项数 / Register, append in batches, close, and consume the real base-objective term count.</summary>
    /// <returns>最终基础目标项数 / The final base-objective term count.</returns>
    [Benchmark]
    public int AddColumnBatches() {
        using var model = new LinearMetaModel<Flt64>("pattern-columns");
        var context = new PatternAssignmentContext<Flt64>(_problem, Flt64PackingValueAdapter.Instance);
        // **注册、每批追加、关闭都在计时之内**：是这么做的，分开计时会漏掉建行与建列那一半。
        // **Register, each batch and close all sit inside the timing**, as in the code; timing them apart would miss the row and column construction.
        // **失败就抛**：`Should` 在基准项目里不可用（那是测试项目的东西），
        // 而 `check` 的等价物就是抛异常——**基准里静默忽略失败会把"什么都没做"计时成一个很快的结果。**
        // **Throw on failure**: `Should` belongs to the test projects, and the equivalent of its `check` is to throw —
        // **silently ignoring a failure would time "did nothing" as a very fast result.**
        if (context.Register([], model).IsFailed) {
            throw new System.InvalidOperationException("pattern context failed to register");
        }
        foreach (IReadOnlyList<PackingPattern<Flt64>> batch in _batches) {
            if (context.AddColumns(batch, model).IsFailed) {
                throw new System.InvalidOperationException("adding a column batch failed");
            }
        }
        // **消费真实列数**：池子里的列就是这一轮建出来的列。
        // **Consume the real column count**: the pool holds exactly the columns built in this run.
        return context.Aggregation.Pool.Count;
    }
}
