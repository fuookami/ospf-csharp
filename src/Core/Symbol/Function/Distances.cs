#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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
/// 一个共有辅助件：把两组多项式逐项相减并取绝对值 / Shared helper: pair up, subtract, take absolute values.
/// </summary>
/// <remarks>
/// B1149。`L1DistanceFunction` 与 `LInfinityDistanceFunction` 的构造**只差最后一步**
/// （求和 vs 取最大），**所以"逐项相减再取绝对值"共享一份。**
///
/// B1149. The two distance functions differ **only in the last step** (sum versus maximum),
/// **so "subtract pairwise, then take absolute values" is shared.**
/// </remarks>
internal static class DistanceTerms {
    /// <summary>逐项相减并取绝对值 / Subtract pairwise and take absolute values.</summary>
    /// <remarks>
    /// **先减后取绝对值，而不是先取绝对值再减**——
    /// **后者给出的是 <c>|a| − |b|</c>，它与 <c>|a − b|</c> 在符号相反时符号相反。**
    /// **而"距离非负"这条通常不足以分开两者**（某些输入下两者都非负）。
    ///
    /// **Subtract first, then take absolute values, never the other way round** —
    /// **the other order yields `|a| − |b|`, which has the opposite sign from `|a − b|` when the operands differ in
    /// sign.** **And "the distance is non-negative" is usually not enough to separate them**, because on some inputs
    /// both are non-negative.
    /// </remarks>
    public static AbsFunction<V>[] AbsoluteDifferences<V>(
        IReadOnlyList<LinearPolynomial<V>> left,
        IReadOnlyList<LinearPolynomial<V>> right,
        IIntoValue<V> converter,
        V? bigM,
        string name,
        string? displayName) where V : struct, IRealNumber<V>, INumberField<V> {
        var absolutes = new AbsFunction<V>[left.Count];
        for (int index = 0; index < left.Count; ++index) {
            absolutes[index] = new AbsFunction<V>(
                polynomial: left[index] - right[index],
                converter: converter,
                bigM: bigM,
                name: $"{name}_abs_{index}",
                displayName: displayName);
        }

        return absolutes;
    }

    /// <summary>收集所有辅助变量 / Collect every helper variable.</summary>
    public static IReadOnlyList<IVariableItem> Helpers<V>(IEnumerable<IMathFunctionSymbol<V>> functions)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var helpers = new List<IVariableItem>();
        foreach (IMathFunctionSymbol<V> function in functions) {
            foreach (IVariableItem helper in function.HelperVariables) {
                helpers.Add(helper);
            }
        }

        return helpers.AsReadOnly();
    }
}

/// <summary>
/// L1 距离（各分量绝对差之和）/ The L1 distance, the sum of absolute component differences.
/// </summary>
/// <remarks>
/// 对齐基线 `CompositeFunctions.kt` 的 `L1DistanceFunction`。
///
/// <c>Σ |left_i − right_i|</c>。
///
/// The L1 distance from the baseline, added in B1149.
///
/// <c>Σ |left_i − right_i|</c>.
/// </remarks>
public sealed class L1DistanceFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly AbsFunction<V>[] _absoluteValues;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    /// <summary>构造一个 L1 距离 / Build one.</summary>
    public L1DistanceFunction(
        IReadOnlyList<LinearPolynomial<V>> left,
        IReadOnlyList<LinearPolynomial<V>> right,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "l1_distance",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(converter);

        // 两组分量必须一一对应：长度不同不是"较短的为准"，而是调用方弄错了。
        // 而按较短的截断会让多出来的分量静默消失，距离于是偏小。
        //
        // The two sides must pair up: unequal lengths are not "the shorter wins" but a caller mistake, and truncating
        // to the shorter silently drops components, which makes the distance smaller than it is.
        if (left.Count != right.Count) {
            throw new ArgumentException(
                $"L1 distance needs equally many components on both sides (left={left.Count}, right={right.Count})",
                nameof(right));
        }

        if (left.Count == 0) {
            throw new ArgumentException("L1 distance of zero components is not a distance", nameof(left));
        }

        Left = left;
        Right = right;
        Converter = converter;
        BigM = bigM;
        Name = name;
        DisplayName = displayName;

        _absoluteValues = DistanceTerms.AbsoluteDifferences(left, right, converter, bigM, name, displayName);
        _helperVariables = DistanceTerms.Helpers(_absoluteValues);
    }

    /// <summary>左侧分量 / The left-hand components.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Left { get; }

    /// <summary>右侧分量 / The right-hand components.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Right { get; }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>大 M / The big-M bound, if any.</summary>
    public V? BigM { get; }

    /// <inheritdoc/>
    public IReadOnlyList<IMathFunctionSymbol<V>> InnerFunctions => _absoluteValues;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Left.Concat(Right));

    /// <summary>各绝对差之和 / The sum of the absolute differences.</summary>
    public LinearPolynomial<V> ResultPolynomial {
        get {
            LinearPolynomial<V> total = Zero(Converter);
            foreach (AbsFunction<V> absolute in _absoluteValues) {
                total += absolute.ResultPolynomial;
            }

            return total;
        }
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        V total = Converter.Zero;
        for (int index = 0; index < _absoluteValues.Length; ++index) {
            Result<V?, ErrorCode, Error<ErrorCode>> absolute = _absoluteValues[index].TryEvaluate(values);
            if (absolute is Failed<V?, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }

            if (absolute.Value is not { } value) {
                return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
            }

            total = total.Plus(value);
        }

        return new Ok<V?, ErrorCode, Error<ErrorCode>>(total);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        foreach (AbsFunction<V> absolute in _absoluteValues) {
            Try registered = absolute.RegisterAuxiliaryTokens(tokens);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        foreach (AbsFunction<V> absolute in _absoluteValues) {
            Try registered = absolute.RegisterConstraints(model);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    private static LinearPolynomial<V> Zero(IIntoValue<V> converter) =>
        new(Array.Empty<LinearMonomial<V>>(), converter.Zero);
}

/// <summary>
/// L∞ 距离（各分量绝对差的最大值）/ The L∞ distance, the largest absolute component difference.
/// </summary>
/// <remarks>
/// 对齐基线 `CompositeFunctions.kt` 的 `LInfinityDistanceFunction`。
///
/// <c>max_i |left_i − right_i|</c>。
///
/// **它的评估值 <c>max</c> 与它被编码成约束时用的辅助变量不是同一件事**——
/// **`MaxFunction` 用大 M 把 <c>z ≥ |left_i − right_i|</c> 逐条挂上，于是 <c>z</c> 只有在下界方向上被约束。**
/// **目标里最小化它才恰好等于最大绝对差；最大化它就会跑偏。**
///
/// The L∞ distance from the baseline, added in B1149.
///
/// <c>max_i |left_i − right_i|</c>.
///
/// **Its evaluated value is `max`, while the constraint encoding uses an auxiliary variable** —
/// **`MaxFunction` posts `z ≥ |left_i − right_i|` for each component through big-M, so `z` is bounded only from
/// below.** **Minimising it in an objective makes it equal the largest absolute difference; maximising it does not.**
/// </remarks>
public sealed class LInfinityDistanceFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly AbsFunction<V>[] _absoluteValues;
    private readonly MaxFunction<V> _maximum;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    /// <summary>构造一个 L∞ 距离 / Build one.</summary>
    public LInfinityDistanceFunction(
        IReadOnlyList<LinearPolynomial<V>> left,
        IReadOnlyList<LinearPolynomial<V>> right,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "linfinity_distance",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(converter);

        if (left.Count != right.Count) {
            throw new ArgumentException(
                $"L-infinity distance needs equally many components on both sides " +
                $"(left={left.Count}, right={right.Count})",
                nameof(right));
        }

        if (left.Count == 0) {
            throw new ArgumentException(
                "L-infinity distance of zero components is not a distance", nameof(left));
        }

        Left = left;
        Right = right;
        Converter = converter;
        BigM = bigM;
        Name = name;
        DisplayName = displayName;

        _absoluteValues = DistanceTerms.AbsoluteDifferences(left, right, converter, bigM, name, displayName);

        var polynomials = new LinearPolynomial<V>[_absoluteValues.Length];
        for (int index = 0; index < _absoluteValues.Length; ++index) {
            polynomials[index] = _absoluteValues[index].ResultPolynomial;
        }

        _maximum = new MaxFunction<V>(
            polynomials: polynomials,
            converter: converter,
            bigM: bigM,
            name: $"{name}_max",
            displayName: displayName);

        var inner = new List<IMathFunctionSymbol<V>>(_absoluteValues) { _maximum };
        _helperVariables = DistanceTerms.Helpers(inner);
    }

    /// <summary>左侧分量 / The left-hand components.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Left { get; }

    /// <summary>右侧分量 / The right-hand components.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Right { get; }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>大 M / The big-M bound, if any.</summary>
    public V? BigM { get; }

    /// <inheritdoc/>
    public IReadOnlyList<IMathFunctionSymbol<V>> InnerFunctions {
        get {
            var inner = new List<IMathFunctionSymbol<V>>(_absoluteValues) { _maximum };
            return inner.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Left.Concat(Right));

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _maximum.ResultPolynomial;

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        V? maximum = null;
        foreach (AbsFunction<V> absolute in _absoluteValues) {
            Result<V?, ErrorCode, Error<ErrorCode>> evaluated = absolute.TryEvaluate(values);
            if (evaluated is Failed<V?, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }

            if (evaluated.Value is not { } value) {
                return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
            }

            if (maximum is not { } current || value.CompareTo(current) > 0) {
                maximum = value;
            }
        }

        return new Ok<V?, ErrorCode, Error<ErrorCode>>(maximum);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        foreach (AbsFunction<V> absolute in _absoluteValues) {
            Try registered = absolute.RegisterAuxiliaryTokens(tokens);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }
        }

        return _maximum.RegisterAuxiliaryTokens(tokens);
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        foreach (AbsFunction<V> absolute in _absoluteValues) {
            Try registered = absolute.RegisterConstraints(model);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }
        }

        return _maximum.RegisterConstraints(model);
    }
}
