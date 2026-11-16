#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 二次输入的绝对值：桥接后交给线性 `AbsFunction` / A quadratic-input absolute value, bridged.
/// </summary>
/// <remarks>
/// 对齐。
///
/// **与六个类同形**：**桥接，然后委托。绝对值编码本身没有任何新增。**
///
/// **但这里有一个必须说清的差别**：**`AbsFunction` 接受的是<b>线性</b>多项式，
/// 所以桥接之后 `|y|` 与 `|p|` 是同一个数——只要桥接等式真的成立。**
/// **而桥接等式如果不成立（比如忘了注册），`|y|` 会取到任意值。**
///
/// Following its.
///
/// **Same shape as the six classes**: **bridge, then delegate. No absolute-value encoding is new.**
///
/// **One difference must be stated**: **`AbsFunction` takes a <b>linear</b> polynomial, so after bridging `|y|` equals
/// `|p|` — as long as the bridge equality actually holds.** **If it does not (say the registration was forgotten),
/// `|y|` takes an arbitrary value.**
/// </remarks>
public sealed class QuadraticAbsFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _polynomial;

    /// <summary>创建二次输入的绝对值 / Create one.</summary>
    public QuadraticAbsFunction(
        QuadraticPolynomial<Flt64> polynomial,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        string name = "quadratic_abs",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(polynomial);
        ArgumentNullException.ThrowIfNull(converter);

        _polynomial = polynomial;

        (LinearPolynomial<Flt64>[] inputs, RealVar[] bridges, QuadraticInequalityOf<Flt64>[] rows) =
            QuadraticExtremumBridging.Bridge(new[] { polynomial }, converter, name);
        Bridges = bridges;
        BridgeRows = rows;

        Inner = new AbsFunction<Flt64>(
            polynomial: inputs[0],
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName);
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>输入多项式 / The input.</summary>
    public QuadraticPolynomial<Flt64> Polynomial => _polynomial;

    /// <summary>桥接变量 / The bridge.</summary>
    public IReadOnlyList<RealVar> Bridges { get; }

    /// <summary>桥接等式 / The bridge rows.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> BridgeRows { get; }

    /// <summary>被委托的线性函数 / The delegated linear function.</summary>
    public AbsFunction<Flt64> Inner { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem>();
            helpers.AddRange(Bridges);
            helpers.AddRange(Inner.HelperVariables);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols =>
        QuadraticExtremumBridging.SymbolsOf(new[] { _polynomial });

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial => Inner.ResultPolynomial;

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) =>
        QuadraticExtremumBridging.EvaluateDelegated(Inner, new[] { _polynomial }, Bridges, values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Try bridged = QuadraticExtremumBridging.RegisterBridgeRows(model, BridgeRows);
        if (bridged is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return Inner.RegisterConstraints(model);
    }
}

/// <summary>
/// 二次输入的正部 <c>max(p, 0)</c> / The positive part of a quadratic input.
/// </summary>
/// <remarks>
/// 对齐。
///
/// **的恒等式是 `max(p, 0) = −min(−p, 0)`，而 `min` 用 `exact = true` 的 `QuadraticMinFunction` 实现。**
/// **所以本类没有自己的极值编码**：**它构造一个 `−p` 与 `0` 的 `QuadraticMinFunction`，
/// 再对它的结果取负。**
///
/// **三处容易写错，而三处都不报错**：
/// **① 忘了取负（得到 `min(−p, 0)`，即负部而不是正部）；**
/// **② `−p` 只取负了 monomial 而没取负常数项；**
/// **③ `exact = false`（于是 `min` 变成松弛，结果偏小）。**
///
/// **`selectorVars` 在输入<b>必然非负或必然非正</b>时为空**：
/// **那时 `min` 不需要选择器，因为 `p` 与 `0` 的大小关系已经由界确定。**
/// **判据必须覆盖这一点**：**同一个多项式在有界与无界两种情形下辅助变量数量不同。**
///
/// **`negateAndNormalizeZero` 把 `−0` 归成 `0`**：
/// **浮点里 `−0` 与 `0` 相等但可观察不同（比如格式化输出 `-0`），
/// 而归一化让"正部在零点"给出干净的零。**
///
/// Following its.
///
/// **identity is `max(p, 0) = −min(−p, 0)`, with the `min` built from an `exact = true`
/// `QuadraticMinFunction`.** **So this class has no extremum encoding of its own**: **it builds a
/// `QuadraticMinFunction` over `−p` and `0`, then negates the result.**
///
/// **Three things are easy to get wrong, and none throws**.
/// **① forgetting the outer negation (yielding `min(−p, 0)`, the negative part rather than the positive one);**
/// **② negating the monomials but not the constant term;**
/// **③ `exact = false` (making the `min` a relaxation, so the result comes out too small).**
///
/// **`selectorVars` is empty when the input is <b>necessarily non-negative or necessarily non-positive</b>**.
/// **then the `min` needs no selector, because the bounds already fix how `p` compares to zero.**
/// **The criterion must cover that**: **the same polynomial yields different helper counts under bounds and
/// without them.**
///
/// **`negateAndNormalizeZero` folds `−0` into `0`**.
/// **in floating point `−0` equals `0` but is observably different (formatting shows `-0`), and normalising gives a
/// clean zero for the positive part at the origin.**
/// </remarks>
public sealed class QuadraticPositivePartFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _input;
    private readonly QuadraticPolynomial<Flt64> _negativeInput;
    private readonly IIntoValue<Flt64> _converter;
    private readonly bool _inputIsNonNegative;
    private readonly bool _inputIsNonPositive;

    /// <summary>创建二次输入的正部 / Create one.</summary>
    public QuadraticPositivePartFunction(
        QuadraticPolynomial<Flt64> input,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        string name = "quadratic_positive_part",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(converter);

        _input = input;
        _converter = converter;
        Name = name;
        DisplayName = displayName;

        // **界决定了 `min` 需不需要选择器**：**必然非负时 `min(−p, 0) = −p`，
        // 必然非正时 `min(−p, 0) = 0`，两者都不需要选。**
        //
        // **The bounds decide whether the `min` needs a selector**: **if the input is necessarily non-negative then
        // `min(−p, 0) = −p`, and if necessarily non-positive then `min(−p, 0) = 0`; neither needs one.**
        if (QuadraticBounds.Of(input, converter) is { } bounds) {
            _inputIsNonNegative = bounds.Lower.CompareTo(converter.Zero) >= 0;
            _inputIsNonPositive = bounds.Upper.CompareTo(converter.Zero) <= 0;
        }

        // **`−p` 的常数项也要取负**：**只取负 monomial 会让 `min` 与 `0` 的比较偏移一个常数，
        // 而结果是"差不多对"——偏一个常数，不报错。**
        //
        // **The constant term of `−p` must be negated too**: **negating only the monomials offsets the comparison
        // against zero by a constant, and the result is "nearly right" — off by a constant, silently.**
        var negated = new List<QuadraticMonomial<Flt64>>(input.Monomials.Count);
        foreach (QuadraticMonomial<Flt64> monomial in input.Monomials) {
            negated.Add(new QuadraticMonomial<Flt64>(
                monomial.Coefficient.Negate(), monomial.Symbol1, monomial.Symbol2));
        }

        _negativeInput = new QuadraticPolynomial<Flt64>(negated, input.Constant.Negate());

        Inner = new QuadraticMinFunction(
            polynomials: new[] {
                _negativeInput,
                new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), converter.Zero),
            },
            converter: converter,
            exact: true,
            bigM: bigM,
            name: $"{name}_positive_part",
            displayName: displayName);
    }

    /// <summary>输入多项式 / The input.</summary>
    public QuadraticPolynomial<Flt64> Input => _input;

    /// <summary>取负后的输入 / The negated input.</summary>
    public QuadraticPolynomial<Flt64> NegativeInput => _negativeInput;

    /// <summary>输入是否必然非负 / Whether the input is necessarily non-negative.</summary>
    public bool InputIsNonNegative => _inputIsNonNegative;

    /// <summary>输入是否必然非正 / Whether the input is necessarily non-positive.</summary>
    public bool InputIsNonPositive => _inputIsNonPositive;

    /// <summary>被委托的最小值函数 / The delegated minimum.</summary>
    public QuadraticMinFunction Inner { get; }

    /// <summary>结果变量 / The result variable.</summary>
    public RealVar ResultVar => Inner.ResultVar;

    /// <summary>选择器变量；输入必然定号时为空 / The selectors; empty when the sign is fixed.</summary>
    public IReadOnlyList<IVariableItem> SelectorVars =>
        _inputIsNonNegative || _inputIsNonPositive
            ? Array.Empty<IVariableItem>()
            : Inner.HelperVariables;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem> { ResultVar };
            helpers.AddRange(SelectorVars);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols =>
        QuadraticExtremumBridging.SymbolsOf(new[] { _input });

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(_converter.One.Negate(), ResultVar) },
        _converter.Zero);

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        if (QuadraticBounds.Evaluate(_input, values) is not { } input) {
            return null;
        }

        // `max(p, 0)` 直接算，不走 `min`：**这条路径与编码无关，
        // 所以它能独立验证编码对不对。**
        //
        // `max(p, 0)` is computed directly rather than through the `min`: **this path is independent of the encoding,
        // so it can check the encoding.**
        Flt64 positive = input.CompareTo(_converter.Zero) > 0 ? input : _converter.Zero;
        return positive.CompareTo(_converter.Zero) == 0 ? _converter.Zero : positive;
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) =>
        Inner.RegisterConstraints(model);
}
