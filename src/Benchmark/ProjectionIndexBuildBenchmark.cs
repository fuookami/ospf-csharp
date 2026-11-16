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
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 区分不同消费面的新版本准备与同版本复用 / Separate cold-revision preparation from same-revision reuse by consumer.
/// </summary>
/// <remarks>
///
/// **`Setup` 里既建了一次投影（`_warm`）又消费过一次**，所以 `WarmRevision` 测的是"同版本复用"，
/// 而 `ColdRevision` 每次都新建。**两者若共用同一个未消费的投影，"冷"的那半就没测到索引构建**，
/// 而这两个路径的差距正是这个基准要回答的问题。
///
/// **`Setup` both creates a projection (`_warm`) and consumes it once**, so `WarmRevision` measures same-revision reuse while `ColdRevision` builds
/// anew each call. **Sharing one unconsumed projection would leave the "cold" half measuring nothing**, and the gap between the two paths is exactly
/// what this benchmark exists to answer.
/// </remarks>
[MemoryDiagnoser]
public class ProjectionIndexBuildBenchmark {
    /// <summary>变量数，行数为其一半 / Variable count, with half as many rows.</summary>
    [Params(256, 1024, 4096)]
    public int Size { get; set; } = 256;

    /// <summary>消费面：`snapshot`、`pricing` 或 `master` / The consumer: `snapshot`, `pricing`, or `master`.</summary>
    [Params("snapshot", "pricing", "master")]
    public string Consumer { get; set; } = "snapshot";

    private LinearMetaModel<Flt64> _source = null!;
    private List<ColumnDecision> _decisions = null!;
    private List<ProjectedColumn<Flt64>> _columns = null!;
    private ColumnProjection<Flt64> _warm = null!;

    /// <summary>块标识 / Block identities.</summary>
    internal static readonly IReadOnlyList<string> BlockIds =
        Enumerable.Range(0, 4).Select(index => $"b{index}").ToList();

    /// <summary>固定源模型，刷新及消费仍在计时内 / Fix the source model; refresh and consumption remain timed.</summary>
    [GlobalSetup]
    public void Setup() {
        _source = new LinearMetaModel<Flt64>(
            name: "projection_index_build",
            objectCategory: ObjectCategory.Minimum);
        var variables = new List<BinVar>(Size);
        for (int index = 0; index < Size; ++index) {
            variables.Add(new BinVar($"v{index}"));
        }
        Try added = _source.Add(variables);
        if (!added.IsOk) {
            throw new InvalidOperationException(
                "基准变量必须可加入模型 / the benchmark variables must be addable: " + added);
        }
        for (int row = 0; row < Size / 2; ++row) {
            var monomials = new List<LinearMonomial<Flt64>>(3);
            for (int offset = 0; offset < 3; ++offset) {
                monomials.Add(new LinearMonomial<Flt64>(
                    Flt64.One,
                    variables[(row + (offset * 4)) % Size]));
            }
            var lhs = new LinearPolynomial<Flt64>(monomials, Flt64.Zero);
            var rhs = new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), new Flt64(2));
            Try rowAdded = _source.AddConstraint(
                new LinearInequality<Flt64>(lhs, rhs, Comparison.LE, Name: $"r{row}"),
                group: null,
                name: $"r{row}");
            if (!rowAdded.IsOk) {
                throw new InvalidOperationException(
                    "基准行必须可加入模型 / the benchmark rows must be addable: " + rowAdded);
            }
        }
        _decisions = variables
            .Select((variable, index) => new ColumnDecision(
                Id: $"d{index}",
                BlockId: BlockIds[index % BlockIds.Count],
                Variable: variable))
            .ToList();
        _columns = BlockIds
            .Select(block => new ProjectedColumn<Flt64>(
                id: block,
                blockId: block,
                values: _decisions
                    .Where(decision => decision.BlockId == block)
                    .ToDictionary(decision => decision.Id, _ => Flt64.Zero)))
            .ToList();
        _warm = Create();
        ConsumeAsync(_warm).GetAwaiter().GetResult();
    }

    /// <summary>新投影的完整入口结果 / The complete entry result of a new projection.</summary>
    /// <returns>消费结果 / The consumption result.</returns>
    [Benchmark]
    public object ColdRevision() => ConsumeAsync(Create()).GetAwaiter().GetResult();

    /// <summary>同版本重复入口结果 / The repeated entry result within one revision.</summary>
    /// <returns>消费结果 / The consumption result.</returns>
    [Benchmark]
    public object WarmRevision() => ConsumeAsync(_warm).GetAwaiter().GetResult();

    internal ColumnProjection<Flt64> Create() =>
        new(source: _source, decisions: _decisions, blockIds: BlockIds);

    /// <summary>
    /// 注册主问题列并返回登记结果 / Register the master columns and return the registration.
    /// </summary>
    /// <remarks>
    /// **单独暴露是为了让测试能检查"列真的被登记了"**：`Rows` 来自源模型的行，与登记的列数无关，
    /// 所以"不登记任何列"时行数不变、`master` 分支照样返回 8——**只断言行数的测试根本没测到列登记**。
    /// **Exposed separately so a test can check the columns really got registered**: `Rows` comes from the source model's rows and is independent of the
    /// registered column count, so "register no columns" leaves the row count unchanged and the `master` branch still returns 8 — **a test asserting only
    /// the row count never exercised column registration**.
    /// </remarks>
    /// <param name="projection">投影 / The projection.</param>
    /// <returns>登记结果 / The registration.</returns>
    internal async Task<ColumnMasterRegistration<Flt64>> RegisterMasterAsync(
        ColumnProjection<Flt64> projection) =>
        Unwrap(
            await projection.MasterAsync(
                columns: _columns, phaseOne: false, integer: false),
            "master");

    internal async Task<object> ConsumeAsync(ColumnProjection<Flt64> projection) {
        switch (Consumer) {
            case "pricing": {
                Result<ColumnPricingRequest<Flt64>, ErrorCode, Error<ErrorCode>> request =
                    await projection.PricingRequestAsync(
                        blockId: BlockIds[0], phaseOne: false, seed: true);
                return Unwrap(request, "pricing");
            }
            case "master": {
                ColumnMasterRegistration<Flt64> registration =
                    await RegisterMasterAsync(projection);
                try {
                    return registration.Rows.Count;
                } finally {
                    registration.Model.Close();
                }
            }
            default: {
                Result<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>> snapshot =
                    await projection.SnapshotAsync();
                return Unwrap(snapshot, "snapshot");
            }
        }
    }

    internal LinearMetaModel<Flt64> Source => _source;

    private static T Unwrap<T>(
        Result<T, ErrorCode, Error<ErrorCode>> result,
        string consumer) {
        if (result is Ok<T, ErrorCode, Error<ErrorCode>> ok) {
            return ok.Value;
        }
        throw new InvalidOperationException(
            $"基准投影的 {consumer} 消费必须成功 / the benchmark's {consumer} consumption must succeed: "
            + result);
    }
}
