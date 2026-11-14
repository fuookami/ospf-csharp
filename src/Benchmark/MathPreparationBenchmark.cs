#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Algebra.Number;
using Flt64 = Fuookami.Ospf.Math.Algebra.Number.Flt64;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Expression.Operation;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Utils.Parallel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comparison = Fuookami.Ospf.Math.Symbol.Expression.Comparison<
    Fuookami.Ospf.Math.Algebra.Number.Flt64>;
using ComparisonOperator = Fuookami.Ospf.Math.Symbol.Expression.ComparisonOperator;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 数学、数组与有界并发的真实入口对照夹具 / Production-entry fixtures for math, arrays, and bounded concurrency.
/// </summary>
/// <remarks>
/// 与基线 `MathPreparationBenchmark.kt`（7783 B）的**可移植子集**对应：`contraction`、`normalizeShared` /
/// `normalizeFlat`、`matchingTraversal`、`boundedWorkerMap`。
///
/// **四处明确不移植**（C# 侧没有对应入口，且不写"看起来等价"的替代）：
///
/// 1. `strictGradient` / `exactGradient`：基线用 `CanonicalPolynomial.compileGradientCanonical`，C# 只有
///    `LinearPolynomial.CompileGradientLinear` / 二次版——**没有 canonical 幂次多项式的梯度编译**。
/// 2. `sumAxes`：C# `MultiArray` **没有任何按轴归约**（`SumAxes` / 同类入口全仓不存在）。
/// 3. `cumsumAxis`：同上，**没有前缀归约**。
/// 4. `CanonicalLinearPreparationBenchmark`（单因子回归哨兵）：同一个 `compileGradientCanonical` 缺口。
///
/// **Four deliberate non-ports** (no corresponding C# entry, and no "looks equivalent" stand-in):
/// the two gradient benchmarks and the one-factor sentinel need `compileGradientCanonical`, which C# does not have (only
/// `CompileGradientLinear` / the quadratic variant); `sumAxes` and `cumsumAxis` need axis reduction and prefix reduction, **neither of which exists
/// anywhere in the C# `MultiArray`**. The gaps stay on the P9 ledger.
/// </remarks>
[MemoryDiagnoser]
public class MathPreparationBenchmark {
    /// <summary>按 N / 2N / 4N 增长的规模 / Dataset growing at N / 2N / 4N.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    private MultiArray<Flt64, Shape3> _array = null!;
    private MultiArray<Flt64, Shape2> _a = null!;
    private MultiArray<Flt64, Shape2> _b = null!;
    private BooleanExpression _sharedExpression = null!;
    private BooleanExpression _flatExpression = null!;
    private List<int> _tasks = null!;
    private readonly NormalizeConfig _normalization = new(SortOperands: true);

    /// <summary>规模倍数 / The scale factor.</summary>
    internal int Factor => Dataset switch {
        "medium" => 2,
        "large" => 4,
        _ => 1,
    };

    /// <summary>在计时外创建输入 / Create the inputs outside timing.</summary>
    [GlobalSetup]
    public void Setup() {
        int factor = Factor;
        _array = MultiArray<Flt64, Shape3>.Factory.NewBy(
            Shape3.WithOrder(32, 16 * factor, 8, StorageOrder.RowMajor),
            (index, _) => new Flt64((index % 17 - 8) / 10.0));
        _a = MultiArray<Flt64, Shape2>.Factory.NewBy(
            Shape2.WithOrder(16, 32 * factor, StorageOrder.RowMajor),
            (index, _) => new Flt64((index % 7 - 3) / 10.0));
        _b = MultiArray<Flt64, Shape2>.Factory.NewBy(
            Shape2.WithOrder(32 * factor, 16, StorageOrder.RowMajor),
            (index, _) => new Flt64((index % 11 - 5) / 10.0));

        // 共享标量 DAG：同一个祖先被重复引用 6 次，规范化若不做去重会指数展开。
        // Shared scalar DAG: one ancestor referenced six times; normalization without de-duplication expands exponentially.
        ScalarExpression<Flt64> shared = new ScalarReference<Flt64>(
            PropertyPath.Parse("shared"));
        for (int depth = 0; depth < 6; ++depth) {
            shared = new ScalarFunction<Flt64>("dag", new[] { shared, shared });
        }
        _sharedExpression = new AndExpression(
            Enumerable.Range(0, 16 * factor)
                .Select(index => (BooleanExpression)new Comparison(
                    ComparisonOperator.Eq,
                    shared,
                    new ScalarConstant<Flt64>(new Flt64(index % 7))))
                .ToList());
        _flatExpression = new AndExpression(
            Enumerable.Range(0, 16 * factor)
                .Select(index => (BooleanExpression)new Comparison(
                    ComparisonOperator.Eq,
                    new ScalarReference<Flt64>(PropertyPath.Parse($"p{index}")),
                    new ScalarConstant<Flt64>(new Flt64(index))))
                .ToList());
        _tasks = Enumerable.Range(0, 512 * factor).ToList();
    }

    /// <summary>同序遍历的真实累加 / Production matching-order traversal accumulation.</summary>
    /// <returns>累加结果 / The accumulation.</returns>
    [Benchmark]
    public Flt64 MatchingTraversal() {
        Flt64 sum = Flt64.Zero;
        foreach (Flt64 value in _array.IterWithOrder()) {
            sum = sum + value;
        }
        return sum;
    }

    /// <summary>缩并结果 / The contraction result.</summary>
    /// <returns>缩并后的元素数量 / The element count after contraction.</returns>
    [Benchmark]
    public int Contraction() {
        Result<object, ErrorCode, Error<ErrorCode>> contracted =
            Fuookami.Ospf.Math.MultiArray.Einsum.EinsumParser
                .Einsum<Flt64, Shape2, Shape2>(_a, _b, "ij,jk->ik", Flt64.Zero);
        return contracted is Ok<object, ErrorCode, Error<ErrorCode>> ok
            && ok.Value is MultiArray<Flt64, DynShape> product
                ? product.Count
                : throw new InvalidOperationException(
                    "基准缩并必须成功 / the benchmark contraction must succeed: " + contracted);
    }

    /// <summary>共享标量 DAG 的规范化结果 / The normalized shared-scalar DAG.</summary>
    /// <returns>规范化的深度 / The normalized depth.</returns>
    [Benchmark]
    public int NormalizeShared() => Normalize.Apply(_sharedExpression, _normalization).Depth();

    /// <summary>普通浅树的规范化结果 / The normalized ordinary shallow tree.</summary>
    /// <returns>规范化的深度 / The normalized depth.</returns>
    [Benchmark]
    public int NormalizeFlat() => Normalize.Apply(_flatExpression, _normalization).Depth();

    /// <summary>四 worker 按序映射结果 / The ordered four-worker mapping result.</summary>
    /// <returns>映射后的列表 / The mapped list.</returns>
    [Benchmark]
    public List<int> BoundedWorkerMap() =>
        _tasks.MapParallelly(task => task + 1, concurrency: 4)
            .GetAwaiter().GetResult();

    internal MultiArray<Flt64, Shape3> Array => _array;

    internal BooleanExpression SharedExpression => _sharedExpression;

    internal BooleanExpression FlatExpression => _flatExpression;

    internal IReadOnlyList<BooleanExpression> SharedOperands =>
        ((AndExpression)_sharedExpression).Operands;

    internal IReadOnlyList<BooleanExpression> FlatOperands =>
        ((AndExpression)_flatExpression).Operands;

    internal List<int> Tasks => _tasks;
}
