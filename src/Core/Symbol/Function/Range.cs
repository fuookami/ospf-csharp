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
/// 一组多项式的极差：<c>max − min</c> / The range of several polynomials: `max - min`.
/// </summary>
/// <remarks>
/// 对齐的 `RangeFunction`。
///
/// **极差不是"两两之差的最大值"**，**而是 `max(inputs) − min(inputs)`。**
/// **两者在只有两个输入时相同，在三个及以上时不同**：
/// **`[0, 5, 5]` 的极差是 `5`，而两两之差的最大值也是 `5`；**
/// **但 `[0, 3, 5]` 的极差是 `5`，两两之差的最大值仍是 `5`——**
/// **真正分开两者的是"中间值远小于两端"的情形**：
/// **`[0, 100, 100]` 的极差是 `100`，而两两之差的最大值也是 `100`。**
/// **所以判据必须用"两端接近、中间远离"的反例**，见判据里的具体取值。
///
/// **本类不含任何极值编码**：**它构造一个 `MaxFunction` 与一个 `MinFunction`，
/// 再把两者相减。** **编码都在那两个类里。**
///
/// **`Evaluate` 直接按定义算，不走 `max`/`min`**：
/// **这条路径与编码无关，所以它能独立验证编码对不对。**
///
/// Following its `RangeFunction`.
///
/// **The range is not "the largest pairwise difference"** but `max(inputs) - min(inputs)`.
/// **The two agree for two inputs and differ beyond that.**
///
/// **No extremum encoding lives here**: **it builds one `MaxFunction` and one `MinFunction` and subtracts them.**
/// **The encoding is entirely in those two classes.**
///
/// **`Evaluate` computes the definition directly rather than through `max`/`min`**.
/// **that path is independent of the encoding, so it can check the encoding.**
/// </remarks>
public sealed class RangeFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V>, IRing<V>, IField<V> {
    private readonly IIntoValue<V> _converter;

    /// <summary>创建一组多项式的极差 / Create the range of several polynomials.</summary>
    public RangeFunction(
        IReadOnlyList<LinearPolynomial<V>> inputs,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "range",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(converter);

        // **空输入必须拒绝，而不是给 0**：**空集的极差没有定义**，
        // **给 0 会让"我漏了一个输入"看起来像"极差是零"。**
        //
        // **Empty input must be rejected rather than yielding zero**: **the range of the empty set is undefined**, and
        // **zero would make "I forgot an input" look like "the range is zero".**
        if (inputs.Count == 0) {
            throw new ArgumentException("a range needs at least one input", nameof(inputs));
        }

        _converter = converter;
        var snapshot = new LinearPolynomial<V>[inputs.Count];
        for (int index = 0; index < inputs.Count; ++index) {
            snapshot[index] = inputs[index];
        }

        Inputs = snapshot;
        Name = name;
        DisplayName = displayName;

        Maximum = new MaxFunction<V>(
            polynomials: snapshot,
            converter: converter,
            bigM: bigM,
            name: $"{name}_max",
            displayName: displayName);

        Minimum = new MinFunction<V>(
            polynomials: snapshot,
            converter: converter,
            bigM: bigM,
            name: $"{name}_min",
            displayName: displayName);
    }

    /// <summary>参与求极差的多项式 / The polynomials whose range is taken.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Inputs { get; }

    /// <summary>最大值 / The maximum.</summary>
    public MaxFunction<V> Maximum { get; }

    /// <summary>最小值 / The minimum.</summary>
    public MinFunction<V> Minimum { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem>();
            helpers.AddRange(Maximum.HelperVariables);
            helpers.AddRange(Minimum.HelperVariables);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Inputs);

    /// <summary>
    /// 结果式 <c>max − min</c> / The result, `max - min`.
    /// </summary>
    /// <remarks>
    /// **`MaxFunction` 与 `MinFunction` 各自的结果式是一组辅助变量之和，
    /// 所以相减就是两个辅助变量表达式的差。** **这里不做化简**：
    /// **化简会改变"结果式由哪些变量组成"这个可观察事实，而判据要核对它。**
    ///
    /// **The `MaxFunction` and `MinFunction` results are each a sum over helper variables, so the difference is the
    /// difference of those two expressions.** **Nothing is simplified here**: **simplifying would change the observable
    /// fact of which variables the result is built from, and the criterion checks exactly that.**
    /// </remarks>
    public LinearPolynomial<V> ResultPolynomial {
        get {
            LinearPolynomial<V> maximum = Maximum.ResultPolynomial;
            LinearPolynomial<V> minimum = Minimum.ResultPolynomial;

            var monomials = new List<LinearMonomial<V>>(
                maximum.Monomials.Count + minimum.Monomials.Count);

            foreach (LinearMonomial<V> monomial in maximum.Monomials) {
                monomials.Add(monomial);
            }

            foreach (LinearMonomial<V> monomial in minimum.Monomials) {
                monomials.Add(new LinearMonomial<V>(monomial.Coefficient.Negate(), monomial.Symbol));
            }

            return new LinearPolynomial<V>(monomials, maximum.Constant.Minus(minimum.Constant));
        }
    }

    /// <summary>按定义直接求极差 / Compute the range from its definition.</summary>
    /// <remarks>
    /// **不走 `max`/`min` 的编码**：**这条路径与编码无关，所以它能独立验证编码对不对。**
    /// **任一输入求值失败就整体返回 `null`**——**填 0 会让缺失的输入看起来像"参与了且值为零"。**
    ///
    /// **Does not go through the `max`/`min` encoding**: **this path is independent of it, so it can check it.**
    /// **Any failing input makes the whole result `null`** — **filling in zero would make a missing input look like
    /// "participated with value zero".**
    /// </remarks>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) {
        if (FunctionEvaluation.Polynomial(Inputs[0], values).Value is not { } first) {
            return null;
        }

        V minimum = first;
        V maximum = first;

        for (int index = 1; index < Inputs.Count; ++index) {
            if (FunctionEvaluation.Polynomial(Inputs[index], values).Value is not { } value) {
                return null;
            }

            if (value.CompareTo(minimum) < 0) {
                minimum = value;
            }

            if (value.CompareTo(maximum) > 0) {
                maximum = value;
            }
        }

        return maximum.Minus(minimum);
    }

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        new Ok<V?, ErrorCode, Error<ErrorCode>>(Evaluate(values));

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        Try first = Maximum.RegisterAuxiliaryTokens(tokens);
        if (first is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return Minimum.RegisterAuxiliaryTokens(tokens);
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        Try first = Maximum.RegisterConstraints(model);
        if (first is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return Minimum.RegisterConstraints(model);
    }
}
