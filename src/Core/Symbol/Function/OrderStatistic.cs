#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 用比较网络把若干多项式降序排列 / Sort polynomials descending with a comparison network.
/// </summary>
/// <remarks>
/// 的做法：**冒泡排序，每一对比较都换成一个 <c>MaxFunction</c> 与一个
/// <c>MinFunction</c>**，于是"第 k 大"就变成"排序后第 k 个"。
///
/// **代价是 O(n²) 对函数**——**n 个输入要 n(n−1) 个函数**。
/// **这是有意的选择**，因为顺序统计量本来就没有更省的精确线性编码。
/// **`ponytail:` 有意保留 O(n²)：输入通常是个位数；若某天 n 上百，需要的是排序网络（Batcher）而不是加大 bigM。**
///
/// **`MaxFunction` 与 `MinFunction` 必须成对用同一对多项式**——
/// **只用 Max 得到的是"每对里的较大者"，重复使用会让同一个多项式出现在多个位置，
/// 而"第 k 大"于是变成一个没有意义的量。**
///
/// Following its approach: **bubble sort where each comparison becomes one `MaxFunction` and one
/// `MinFunction`**, so "the k-th largest" is "the k-th after sorting".
///
/// **The cost is O(n²) function pairs** — **n inputs need n(n−1) functions.** **That is its choice and this
/// repo follows it**, because order statistics have no cheaper exact linear encoding.
/// **`ponytail:` O(n²) is deliberate: the input count is usually single digits; if it ever reaches the hundreds, the
/// answer is a Batcher sorting network, not a bigger bigM.**
///
/// **`MaxFunction` and `MinFunction` must be used as a pair on the same two polynomials** —
/// **using only Max yields "the larger of each pair", and reusing it puts one polynomial in several positions, which
/// makes "the k-th largest" meaningless.**
/// </remarks>
internal sealed class DescendingOrder<V> where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>降序排列后的多项式 / The polynomials, descending.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Polynomials { get; }

    /// <summary>排序用到的全部函数 / Every function used by the sort.</summary>
    public IReadOnlyList<IMathFunctionSymbol<V>> Functions { get; }

    private DescendingOrder(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        IReadOnlyList<IMathFunctionSymbol<V>> functions) {
        Polynomials = polynomials;
        Functions = functions;
    }

    /// <summary>排一次序 / Sort once.</summary>
    public static DescendingOrder<V> Sort(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        IIntoValue<V> converter,
        V? bigM,
        string name,
        string? displayName) {
        var ordered = new LinearPolynomial<V>[polynomials.Count];
        for (int index = 0; index < polynomials.Count; ++index) {
            ordered[index] = polynomials[index];
        }

        var functions = new List<IMathFunctionSymbol<V>>();

        for (int pass = 0; pass + 1 < ordered.Length; ++pass) {
            for (int index = 0; index + pass + 1 < ordered.Length; ++index) {
                LinearPolynomial<V>[] pair = { ordered[index], ordered[index + 1] };

                var maximum = new MaxFunction<V>(
                    polynomials: pair,
                    converter: converter,
                    bigM: bigM,
                    name: $"{name}_cmp_{pass}_{index}_max",
                    displayName: displayName);
                var minimum = new MinFunction<V>(
                    polynomials: pair,
                    converter: converter,
                    bigM: bigM,
                    name: $"{name}_cmp_{pass}_{index}_min",
                    displayName: displayName);

                functions.Add(maximum);
                functions.Add(minimum);
                ordered[index] = maximum.ResultPolynomial;
                ordered[index + 1] = minimum.ResultPolynomial;
            }
        }

        return new DescendingOrder<V>(Array.AsReadOnly(ordered), functions.AsReadOnly());
    }

    /// <summary>不排序，原样返回（`k` 为 0 或全部时用）/ No sort at all, used when k is zero or everything.</summary>
    /// <remarks>
    /// **`k = 0` 时结果是常数 0、`k = n` 时是全部之和——两者都不需要知道顺序。**
    /// **跳过排序省掉 n(n−1) 个函数，而那些函数的辅助变量与约束本来会让模型毫无必要地变大。**
    ///
    /// **With `k = 0` the result is the constant zero and with `k = n` it is the whole sum — neither needs the order.**
    /// **Skipping the sort saves n(n−1) functions whose helpers and constraints would otherwise bloat the model for no
    /// reason.**
    /// </remarks>
    public static DescendingOrder<V> Unsorted(IReadOnlyList<LinearPolynomial<V>> polynomials) =>
        new(polynomials, Array.Empty<IMathFunctionSymbol<V>>());
}

/// <summary>
/// 第 k 大（从 0 开始）/ The k-th largest, zero-based.
/// </summary>
/// <remarks>
/// 对齐的 `KthLargestFunction`。
///
/// **`k` 从 0 开始**：**`k = 0` 是最大值，`k = n − 1` 是最小值。**
/// **从 1 开始的约定会让 `k = 0` 与 `k = n` 都越界，而越界检查的边界会差一位。**
///
/// The k-th largest.
///
/// **`k` is zero-based**: **`k = 0` is the maximum and `k = n − 1` the minimum.**
/// **A one-based convention would put both `k = 0` and `k = n` out of range, and the bounds check would be off by
/// one.**
/// </remarks>
public sealed class KthLargestFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly DescendingOrder<V> _sorted;

    /// <summary>构造一个"第 k 大" / Build one.</summary>
    public KthLargestFunction(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        int k,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "kth_largest",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(polynomials);
        ArgumentNullException.ThrowIfNull(converter);

        if (polynomials.Count == 0) {
            throw new ArgumentException("the k-th largest of no inputs does not exist", nameof(polynomials));
        }

        // k 必须落在 [0, n-1]：越界不是"取端点"，而是调用方算错了序号。
        // k must lie in [0, n-1]: out of range is not "clamp to the end" but a caller whose index is wrong.
        if (k < 0 || k >= polynomials.Count) {
            throw new ArgumentOutOfRangeException(
                nameof(k),
                k,
                $"k must be within [0, {polynomials.Count - 1}] for {polynomials.Count} inputs");
        }

        Polynomials = polynomials;
        K = k;
        Converter = converter;
        BigM = bigM;
        Name = name;
        DisplayName = displayName;

        _sorted = DescendingOrder<V>.Sort(polynomials, converter, bigM, name, displayName);
    }

    /// <summary>输入多项式 / The input polynomials.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Polynomials { get; }

    /// <summary>序号，从 0 开始 / The index, zero-based.</summary>
    public int K { get; }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>大 M / The big-M bound.</summary>
    public V? BigM { get; }

    /// <inheritdoc/>
    public IReadOnlyList<IMathFunctionSymbol<V>> InnerFunctions => _sorted.Functions;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem>();
            foreach (IMathFunctionSymbol<V> function in _sorted.Functions) {
                foreach (IVariableItem helper in function.HelperVariables) {
                    helpers.Add(helper);
                }
            }

            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomials);

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _sorted.Polynomials[K];

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        var evaluated = new V[Polynomials.Count];
        for (int index = 0; index < Polynomials.Count; ++index) {
            Result<V?, ErrorCode, Error<ErrorCode>> value = FunctionEvaluation.Polynomial(Polynomials[index], values);
            if (value is Failed<V?, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }

            if (value.Value is not { } present) {
                return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
            }

            evaluated[index] = present;
        }

        Array.Sort(evaluated, (left, right) => right.CompareTo(left));
        return new Ok<V?, ErrorCode, Error<ErrorCode>>(evaluated[K]);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        foreach (IMathFunctionSymbol<V> function in _sorted.Functions) {
            Try registered = function.RegisterAuxiliaryTokens(tokens);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        foreach (IMathFunctionSymbol<V> function in _sorted.Functions) {
            Try registered = function.RegisterConstraints(model);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }
}

/// <summary>
/// 前 k 大之和 / The sum of the k largest.
/// </summary>
/// <remarks>
/// **`k = 0` 时结果是常数 0、`k = n` 时是全部之和——两者都跳过排序**，
/// **于是省掉 n(n−1) 个函数。**
///
/// **`k` 的合法范围是 `[0, n]`（**含 n**），与 `KthLargestFunction` 的 `[0, n−1]` 不同**——
/// **两者边界差一，是因为"前 n 大之和"有意义而"第 n 大"不存在。**
///
/// **With `k = 0` the result is the constant zero and with `k = n` it is the whole sum — both skip the sort**,
/// **saving n(n−1) functions.**
///
/// **The legal range of `k` is `[0, n]` (inclusive of n), unlike `KthLargestFunction`'s `[0, n−1]`** —
/// **the boundaries differ by one because "the sum of all n" exists while "the n-th largest" does not.**
/// </remarks>
public sealed class TopKSumFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly DescendingOrder<V> _sorted;

    /// <summary>构造一个"前 k 大之和" / Build one.</summary>
    public TopKSumFunction(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        int k,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "top_k_sum",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(polynomials);
        ArgumentNullException.ThrowIfNull(converter);

        // k 落在 [0, n]：这里 n 是允许的，与 KthLargestFunction 差一。
        // k lies in [0, n]: n itself is allowed here, one more than KthLargestFunction.
        if (k < 0 || k > polynomials.Count) {
            throw new ArgumentOutOfRangeException(
                nameof(k),
                k,
                $"k must be within [0, {polynomials.Count}] for {polynomials.Count} inputs");
        }

        Polynomials = polynomials;
        K = k;
        Converter = converter;
        BigM = bigM;
        Name = name;
        DisplayName = displayName;

        _sorted = (k == 0 || k == polynomials.Count)
            ? DescendingOrder<V>.Unsorted(polynomials)
            : DescendingOrder<V>.Sort(polynomials, converter, bigM, name, displayName);
    }

    /// <summary>输入多项式 / The input polynomials.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Polynomials { get; }

    /// <summary>取前几个 / How many of the largest to take.</summary>
    public int K { get; }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>大 M / The big-M bound.</summary>
    public V? BigM { get; }

    /// <inheritdoc/>
    public IReadOnlyList<IMathFunctionSymbol<V>> InnerFunctions => _sorted.Functions;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem>();
            foreach (IMathFunctionSymbol<V> function in _sorted.Functions) {
                foreach (IVariableItem helper in function.HelperVariables) {
                    helpers.Add(helper);
                }
            }

            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomials);

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial {
        get {
            LinearPolynomial<V> total = new(Array.Empty<LinearMonomial<V>>(), Converter.Zero);
            for (int index = 0; index < K; ++index) {
                total += _sorted.Polynomials[index];
            }

            return total;
        }
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        var evaluated = new V[Polynomials.Count];
        for (int index = 0; index < Polynomials.Count; ++index) {
            Result<V?, ErrorCode, Error<ErrorCode>> value = FunctionEvaluation.Polynomial(Polynomials[index], values);
            if (value is Failed<V?, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }

            if (value.Value is not { } present) {
                return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
            }

            evaluated[index] = present;
        }

        Array.Sort(evaluated, (left, right) => right.CompareTo(left));

        V total = Converter.Zero;
        for (int index = 0; index < K; ++index) {
            total = total.Plus(evaluated[index]);
        }

        return new Ok<V?, ErrorCode, Error<ErrorCode>>(total);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        foreach (IMathFunctionSymbol<V> function in _sorted.Functions) {
            Try registered = function.RegisterAuxiliaryTokens(tokens);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        foreach (IMathFunctionSymbol<V> function in _sorted.Functions) {
            Try registered = function.RegisterConstraints(model);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }
}
