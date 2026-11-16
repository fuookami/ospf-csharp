#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 二次掩码：<c>y = z · p</c>，<c>z</c> 为二值 / Quadratic masking: `y = z · p` with a binary `z`.
/// </summary>
/// <remarks>
/// 对齐把原本的<b>空壳</b>换成真实编码。
///
/// **五条约束**：
///
/// | 行 | 内容 | 作用 |
/// | --- | --- | --- |
/// | 1 | <c>y − p + M·z ≤ M</c> | `z = 0` 时给 <c>y ≤ p</c> |
/// | 2 | <c>y − p − M·z ≥ −M</c> | `z = 0` 时给 <c>y ≥ p</c> |
/// | 3 | <c>y ≤ M·z</c> | `z = 0` 时把 `y` 逼到 ≤ 0 |
/// | 4 | <c>y ≥ −M·z</c> | `z = 0` 时把 `y` 逼到 ≥ 0 |
///
/// **1 与 2 合起来在 `z = 0` 时给 `y = p`**；**而 `z = 0` 时 `y` 本该是 0**——
/// **所以 3 与 4 才是这里的关键：它们把 `y` 钉在 0 上。**
/// **只有 1、2 而没有 3、4 时，`y` 在 `z = 0` 时等于 `p` 而不是 0**，
/// **于是"掩码关掉"这个语义完全失效，而模型仍然可行。**
///
/// **`z = 1` 时 1、2 给 `y ≤ p + 0` 与 `y ≥ p − 0`，即 `y = p`；3、4 变成 `y ≤ M` 与 `y ≥ −M`。**
///
/// **这也是本类与 `MaskingFunction` 的差别**：**后者是一位线性掩码，前者接受二次多项式 `p`。**
///
/// Replaces a stub with the real encoding, following its.
///
/// **Five rows**.
///
/// | Row | Content | Effect |
/// | --- | --- | --- |
/// | 1 | `y − p + M·z ≤ M` | at `z = 0` gives `y ≤ p` |
/// | 2 | `y − p − M·z ≥ −M` | at `z = 0` gives `y ≥ p` |
/// | 3 | `y ≤ M·z` | at `z = 0` forces `y ≤ 0` |
/// | 4 | `y ≥ −M·z` | at `z = 0` forces `y ≥ 0` |
///
/// **Rows 1 and 2 together give `y = p` at `z = 0`** — **while `y` should be zero then** —
/// **so rows 3 and 4 are what is turned on here: they pin `y` to zero.**
/// **With 1 and 2 but not 3 and 4, `y` equals `p` at `z = 0` instead of zero**, **so "the mask is off" stops meaning
/// anything while the model stays feasible.**
///
/// **At `z = 1` rows 1 and 2 give `y ≤ p` and `y ≥ p`, i.e. `y = p`, while 3 and 4 relax to `y ≤ M` and
/// `y ≥ −M`.**
///
/// **That is also the difference from `MaskingFunction`**: **that one masks a linear form, this one accepts a
/// quadratic `p`.**
/// </remarks>
public sealed class QuadraticMaskingRangeFunction : IMathFunctionSymbol<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _input;
    private readonly BinVar _z;
    private readonly RealVar _resultVar;
    private readonly Flt64 _bigM;
    private readonly IIntoValue<Flt64> _converter;

    /// <summary>构造一个二次掩码 / Build one.</summary>
    public QuadraticMaskingRangeFunction(
        QuadraticPolynomial<Flt64> polynomial,
        BinVar z,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        string name = "quadraticMaskingRange",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(polynomial);
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(converter);

        _input = polynomial;
        _z = z;
        _converter = converter;
        Name = name;
        DisplayName = displayName;
        _resultVar = new RealVar($"{name}_y");

        _bigM = bigM ?? DefaultBigM(polynomial, converter);

        // **`M` 必须为正**：**`M = 0` 时第 3、4 行变成 `y ≤ 0` 与 `y ≥ 0`，
        // 于是 `y` 恒为 0——掩码"永远关掉"，而第 1、2 行在 `z = 1` 时又要求 `y = p`。**
        // **两者冲突 → 模型不可行；而如果 `p` 恰好在某点取 0，模型可行而语义是错的。**
        //
        // **`M` must be positive**: **at `M = 0` rows 3 and 4 read `y ≤ 0` and `y ≥ 0`, so `y` is always zero — the
        // mask is "always off" — while rows 1 and 2 demand `y = p` at `z = 1`.**
        // **The two conflict, making the model infeasible; and if `p` happens to be zero somewhere the model is
        // feasible with the wrong meaning.**
        if (_bigM.CompareTo(converter.Zero) <= 0) {
            throw new ArgumentException($"the big-M must be positive, got {_bigM}", nameof(bigM));
        }
    }

    /// <summary>被掩码的多项式 / The masked polynomial.</summary>
    public QuadraticPolynomial<Flt64> Input => _input;

    /// <summary>二值掩码 / The binary mask.</summary>
    public BinVar Z => _z;

    /// <summary>结果变量 / The result variable.</summary>
    public RealVar ResultVar => _resultVar;

    /// <summary>大 M / The big-M.</summary>
    public Flt64 BigM => _bigM;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => new IVariableItem[] { _resultVar };

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(_input);

    /// <summary>五行约束，顺序固定 / The five rows, in fixed order.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> Rows() {
        List<QuadraticMonomial<Flt64>> NegatedInput() {
            var monomials = new List<QuadraticMonomial<Flt64>>(_input.Monomials.Count);
            foreach (QuadraticMonomial<Flt64> monomial in _input.Monomials) {
                monomials.Add(new QuadraticMonomial<Flt64>(
                    monomial.Coefficient.Negate(), monomial.Symbol1, monomial.Symbol2));
            }

            return monomials;
        }

        // **这三个量用局部函数而不是局部变量**：**可推断类型的局部量会同时触发 IDE0007（用 var）
        // 与 IDE0008（用显式类型），两条规则互斥，所以只能绕开"声明"本身。**
        //
        // **These three use local functions rather than locals**: **an inferable local triggers both IDE0007 (use
        // var) and IDE0008 (use explicit type), which contradict each other, so the declaration itself must be
        // avoided.**
        static QuadraticMonomial<Flt64> Monomial(Flt64 coefficient, ISymbol symbol) =>
            QuadraticMonomial<Flt64>.Linear(coefficient, symbol);

        // 第 1 行：y − p + M·z ≤ M
        // Row 1: y - p + M*z <= M
        List<QuadraticMonomial<Flt64>> firstMonomials = NegatedInput();
        firstMonomials.Add(Monomial(_converter.One, _resultVar));
        firstMonomials.Add(Monomial(_bigM, _z));

        // 第 2 行：y − p − M·z ≥ −M
        // Row 2: y - p - M*z >= -M
        List<QuadraticMonomial<Flt64>> secondMonomials = NegatedInput();
        secondMonomials.Add(Monomial(_converter.One, _resultVar));
        secondMonomials.Add(Monomial(_bigM.Negate(), _z));

        return new[] {
            new QuadraticInequalityOf<Flt64>(
                Lhs: new QuadraticPolynomial<Flt64>(firstMonomials, _input.Constant.Negate()),
                Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), _bigM),
                Comparison: Comparison.LE,
                Name: $"{Name}_upper"),
            new QuadraticInequalityOf<Flt64>(
                Lhs: new QuadraticPolynomial<Flt64>(secondMonomials, _input.Constant.Negate()),
                Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), _bigM.Negate()),
                Comparison: Comparison.GE,
                Name: $"{Name}_lower"),

            // 第 3 行：y ≤ M·z —— z = 0 时把 y 逼到 ≤ 0。
            // Row 3: y <= M*z, forcing y <= 0 at z = 0.
            new QuadraticInequalityOf<Flt64>(
                Lhs: new QuadraticPolynomial<Flt64>(
                    new[] { Monomial(_converter.One, _resultVar) }, _converter.Zero),
                Rhs: new QuadraticPolynomial<Flt64>(
                    new[] { Monomial(_bigM, _z) }, _converter.Zero),
                Comparison: Comparison.LE,
                Name: $"{Name}_zero_upper"),

            // 第 4 行：y ≥ −M·z —— z = 0 时把 y 逼到 ≥ 0。
            // Row 4: y >= -M*z, forcing y >= 0 at z = 0.
            new QuadraticInequalityOf<Flt64>(
                Lhs: new QuadraticPolynomial<Flt64>(
                    new[] { Monomial(_converter.One, _resultVar) }, _converter.Zero),
                Rhs: new QuadraticPolynomial<Flt64>(
                    new[] { Monomial(_bigM.Negate(), _z) }, _converter.Zero),
                Comparison: Comparison.GE,
                Name: $"{Name}_zero_lower"),
        };
    }

    /// <summary>按语义求值：<c>z · p</c> / Evaluate by semantics: `z · p`.</summary>
    /// <remarks>
    /// **`z` 必须是 0 或 1**：**其它值在语义上不存在**——
    /// **而五条约束在 `z = 0.5` 时仍然可满足，给出一个看起来像插值的数。**
    ///
    /// **`z` must be 0 or 1**: **any other value has no meaning** —
    /// **while the five rows stay satisfiable at `z = 0.5`, yielding a number that looks like an interpolation.**
    /// </remarks>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        if (!values.TryGetValue(_z, out Flt64 mask)) {
            return null;
        }

        if (mask.CompareTo(_converter.Zero) != 0 && mask.CompareTo(_converter.One) != 0) {
            return null;
        }

        Flt64? input = EvaluateQuadratic(_input, values);
        if (input is not { } polynomialValue) {
            return null;
        }

        return mask.CompareTo(_converter.One) == 0 ? polynomialValue : _converter.Zero;
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        // **空输入没有约束要写**：**`Σ` 为空时五条行都退化成 `0 ≤ 0` 之类的平凡式，
        // 交给线性模型也无事可做。** **而"必须用二次模型"这个要求只在真有二次项时才成立。**
        //
        // **An empty input has nothing to write**: **with no terms every row degenerates into a triviality such as
        // `0 ≤ 0`, and a linear model has nothing to do with them.** **The "needs a quadratic model" requirement
        // only holds when there really are quadratic terms.**
        if (_input.Monomials.Count == 0) {
            return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
        }

        if (model is not IAbstractQuadraticMechanismModel<Flt64> quadratic) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "a quadratic mask needs a quadratic mechanism model");
        }

        foreach (QuadraticInequalityOf<Flt64> row in Rows()) {
            Try added = quadratic.AddConstraint(row, row.Name, ((IIntermediateSymbol, bool)?)null);
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                return failure;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    /// <summary>默认大 M / The default big-M.</summary>
    /// <remarks>
    /// **取 `max(|lower|, |upper|)`，至少为 1。** **`M` 必须同时容纳 `p` 的正负两向，
    /// 所以取绝对值最大的那一端。**
    ///
    /// **`max(|lower|, |upper|)`, at least one.** **`M` must cover `p` in both directions, so the larger absolute end
    /// wins.**
    /// </remarks>
    private static Flt64 DefaultBigM(QuadraticPolynomial<Flt64> polynomial, IIntoValue<Flt64> converter) {
        if (QuadraticBounds.Of(polynomial, converter) is not { } bounds) {
            return converter.One;
        }

        Flt64 lowerMagnitude = bounds.Lower.CompareTo(converter.Zero) < 0
            ? bounds.Lower.Negate()
            : bounds.Lower;
        Flt64 upperMagnitude = bounds.Upper.CompareTo(converter.Zero) < 0
            ? bounds.Upper.Negate()
            : bounds.Upper;

        Flt64 magnitude = lowerMagnitude.CompareTo(upperMagnitude) >= 0 ? lowerMagnitude : upperMagnitude;
        return magnitude.CompareTo(converter.Zero) > 0 ? magnitude : converter.One;
    }

    private static Flt64? EvaluateQuadratic(
        QuadraticPolynomial<Flt64> polynomial, IReadOnlyDictionary<ISymbol, Flt64> values) {
        Flt64 total = polynomial.Constant;
        foreach (QuadraticMonomial<Flt64> monomial in polynomial.Monomials) {
            if (!values.TryGetValue(monomial.Symbol1, out Flt64 first)) {
                return null;
            }

            if (monomial.Symbol2 is { } secondSymbol) {
                if (!values.TryGetValue(secondSymbol, out Flt64 second)) {
                    return null;
                }

                total = total.Plus(monomial.Coefficient.Times(first).Times(second));
            } else {
                total = total.Plus(monomial.Coefficient.Times(first));
            }
        }

        return total;
    }
}
