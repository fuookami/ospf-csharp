#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 区间归属：在 <c>[lower, upper]</c> 内取 <c>x</c>、明确在外取 0、**容差带内给 `null`** /
/// In-range gives `x`, clearly outside gives zero, **the tolerance band gives `null`**.
/// </summary>
/// <remarks>
/// 对齐把空壳换成真实编码。
///
/// **三段语义**：
///
/// | 输入 | 结果 |
/// | --- | --- |
/// | <c>lower ≤ x ≤ upper</c> | `x` |
/// | <c>x ≤ lower − tol</c> 或 <c>x ≥ upper + tol</c> | `0` |
/// | 其余（容差带内） | **`null`** |
///
/// **第三段是这里的核心**：**容差带内既不算"在内"也不算"明确在外"，
/// 所以返回 `null`——"不知道"而不是"猜一个"。**
/// **把容差带并进任意一侧都会让"明确在外"失去意义**：
/// **并进"在内"则 `x` 稍微越界就返回越界值；并进"在外"则边界附近一律返回 0。**
///
/// **三个状态变量 `inside`/`below`/`above` 互斥且完备**：**`sum = 1`。**
/// **`below` 与 `above` 的界用了容差**（`lower − tol`、`upper + tol`），
/// **而 `inside` 的界不用**——**容差只出现在"明确在外"这一侧。**
///
/// **`y` 由 `inside` 掩码**：**`inside = 1` 时 `y = x`、`inside = 0` 时 `y = 0`。**
///
/// Replaces a stub with the real encoding, following its.
///
/// **Three-way semantics**.
///
/// | Input | Result |
/// | --- | --- |
/// | `lower ≤ x ≤ upper` | `x` |
/// | `x ≤ lower − tol` or `x ≥ upper + tol` | `0` |
/// | anything else (inside the tolerance band) | **`null`** |
///
/// **The third case is the point**: **inside the band the input is neither "in" nor "clearly out", so
/// it returns `null` — "unknown" rather than a guess.**
/// **Folding the band into either side destroys "clearly out"**.
/// **into "in" and a slightly out-of-range `x` returns the out-of-range value; into "out" and everything near the
/// boundary returns zero.**
///
/// **The three state variables `inside`/`below`/`above` are exclusive and exhaustive**: **`sum = 1`.**
/// **`below` and `above` use the tolerance in their bounds** (`lower − tol`, `upper + tol`), **while `inside` does
/// not** — **the tolerance appears only on the "clearly out" side.**
///
/// **`y` is masked by `inside`**: **`y = x` when `inside = 1` and `y = 0` when `inside = 0`.**
/// </remarks>
public sealed class QuadraticInStepRangeFunction : IMathFunctionSymbol<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _x;
    private readonly Flt64 _bigM;
    private readonly Flt64 _tolerance;
    private readonly RealVar _y;
    private readonly BinVar _inside;
    private readonly BinVar _below;
    private readonly BinVar _above;
    private readonly IIntoValue<Flt64> _converter;

    /// <summary>构造一个区间归属 / Build one.</summary>
    public QuadraticInStepRangeFunction(
        QuadraticPolynomial<Flt64> x,
        Flt64 lower,
        Flt64 upper,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        Flt64? outsideTolerance = null,
        string name = "quadraticInStepRange",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(converter);

        // **`lower ≤ upper`**：**倒序的界会让三段划分里没有任何区间"在内"，
        // 而三条状态约束照样可满足（全都落进 below 或 above）。**
        //
        // **`lower ≤ upper`**: **reversed bounds leave no interval "in", while the three state rows still satisfy
        // (everything falls into below or above).**
        if (lower.CompareTo(upper) > 0) {
            throw new ArgumentException(
                $"the lower bound {lower} exceeds the upper bound {upper}", nameof(lower));
        }

        _tolerance = outsideTolerance ?? converter.IntoValue(new Flt64(1e-6));
        if (_tolerance.CompareTo(converter.Zero) <= 0) {
            throw new ArgumentException(
                $"the outside tolerance must be positive, got {_tolerance}", nameof(outsideTolerance));
        }

        _x = x;
        _converter = converter;
        Lower = lower;
        Upper = upper;
        Name = name;
        DisplayName = displayName;

        _bigM = bigM ?? DefaultBigM(x, converter);
        if (_bigM.CompareTo(converter.Zero) <= 0) {
            throw new ArgumentException($"the big-M must be positive, got {_bigM}", nameof(bigM));
        }

        _y = new RealVar($"{name}_y");
        _inside = new BinVar($"{name}_inside");
        _below = new BinVar($"{name}_below");
        _above = new BinVar($"{name}_above");
    }

    /// <summary>被判断的多项式 / The polynomial being judged.</summary>
    public QuadraticPolynomial<Flt64> X => _x;

    /// <summary>下界 / The lower bound.</summary>
    public Flt64 Lower { get; }

    /// <summary>上界 / The upper bound.</summary>
    public Flt64 Upper { get; }

    /// <summary>大 M / The big-M.</summary>
    public Flt64 BigM => _bigM;

    /// <summary>容差 / The outside tolerance.</summary>
    public Flt64 OutsideTolerance => _tolerance;

    /// <summary>结果变量 / The result variable.</summary>
    public RealVar Y => _y;

    /// <summary>三个状态变量 / The three state variables.</summary>
    public BinVar Inside => _inside;

    /// <summary>明确低于下界 / Clearly below.</summary>
    public BinVar Below => _below;

    /// <summary>明确高于上界 / Clearly above.</summary>
    public BinVar Above => _above;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables =>
        new IVariableItem[] { _y, _inside, _below, _above };

    /// <inheritdoc/>
    /// <inheritdoc/>
    /// <remarks>
    /// **返回 `null`，而不是输入符号的集合。**
    /// **仓库的输入契约是：不实现 `IHasResultPolynomial` 的函数，`InputSymbols` 必须是 `null`**——
    /// **它们的结果不通过结果多项式暴露，于是"输入"这个概念在适配器层没有落点。**
    /// **声明成集合会让 `FunctionInputContractTest` 直接红（"expected null but found {...}"）。**
    ///
    /// **`null`, not the set of input symbols.**
    /// **The repository's contract is: a function that does not implement `IHasResultPolynomial` must return `null`
    /// for `InputSymbols`** — **its result is not exposed through a result polynomial, so "the inputs" have nowhere to
    /// land in the adapter.** **Declaring a set turns `FunctionInputContractTest` red outright.**
    /// </remarks>
    public IReadOnlyCollection<ISymbol>? InputSymbols => null;

    /// <summary>本函数读到的符号 / The symbols this function reads.</summary>
    internal IReadOnlyCollection<ISymbol> ReadSymbols => FunctionInputs.Of(_x);

    /// <summary>九条约束，顺序固定 / The nine rows, in fixed order.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> Rows() {
        Flt64 lower = Lower;
        Flt64 upper = Upper;
        Flt64 m = _bigM;
        Flt64 tolerance = _tolerance;

        QuadraticMonomial<Flt64> Monomial(Flt64 coefficient, ISymbol symbol) =>
            QuadraticMonomial<Flt64>.Linear(coefficient, symbol);

        List<QuadraticMonomial<Flt64>> NegatedX() {
            var monomials = new List<QuadraticMonomial<Flt64>>(_x.Monomials.Count);
            foreach (QuadraticMonomial<Flt64> monomial in _x.Monomials) {
                monomials.Add(new QuadraticMonomial<Flt64>(
                    monomial.Coefficient.Negate(), monomial.Symbol1, monomial.Symbol2));
            }

            return monomials;
        }

        // **这些式子用局部函数返回**：**可推断类型的局部量会同时触发 IDE0007（用 var）
        // 与 IDE0008（用显式类型），两条规则互斥，只能绕开"声明"本身。**
        //
        // **These forms come back from local functions**: **an inferable local triggers both IDE0007 (use var) and
        // IDE0008 (use explicit type), which contradict each other, so the declaration itself must be avoided.**
        QuadraticPolynomial<Flt64> WithMask(
            IReadOnlyList<QuadraticMonomial<Flt64>> baseMonomials,
            Flt64 coefficient,
            ISymbol symbol,
            Flt64 constant) {
            var monomials = new List<QuadraticMonomial<Flt64>>(baseMonomials.Count + 1);
            foreach (QuadraticMonomial<Flt64> monomial in baseMonomials) {
                monomials.Add(monomial);
            }

            monomials.Add(Monomial(coefficient, symbol));
            return new QuadraticPolynomial<Flt64>(monomials, constant);
        }

        var rows = new List<QuadraticInequalityOf<Flt64>>();

        // 1. inside = 1 => x ≥ lower : x − m·inside ≥ lower − m
        //
        // **注意 right 端是 `lower − m`**：**inside = 1 时左侧减 m，与右侧的 −m 抵消，
        // 于是该行退化为 `x ≥ lower`；inside = 0 时它宽容到 `x ≥ lower − m`，不构成限制。**
        //
        // **Note the right-hand side is `lower − m`**: **at inside = 1 the −m cancels and the row reads `x ≥ lower`;
        // at inside = 0 it relaxes to `x ≥ lower − m`, which binds nothing.**
        rows.Add(new QuadraticInequalityOf<Flt64>(
            Lhs: WithMask(_x.Monomials, m.Negate(), _inside, _x.Constant),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), lower.Minus(m)),
            Comparison: Comparison.GE,
            Name: $"{Name}_inside_lb"));

        // 2. inside = 1 => x ≤ upper : x + m·inside ≤ upper + m
        rows.Add(new QuadraticInequalityOf<Flt64>(
            Lhs: WithMask(_x.Monomials, m, _inside, _x.Constant),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), upper.Plus(m)),
            Comparison: Comparison.LE,
            Name: $"{Name}_inside_ub"));

        // 3. below = 1 => x ≤ lower − tol : x + m·below ≤ lower − tol + m
        rows.Add(new QuadraticInequalityOf<Flt64>(
            Lhs: WithMask(_x.Monomials, m, _below, _x.Constant),
            Rhs: new QuadraticPolynomial<Flt64>(
                Array.Empty<QuadraticMonomial<Flt64>>(),
                lower.Minus(tolerance).Plus(m)),
            Comparison: Comparison.LE,
            Name: $"{Name}_below"));

        // 4. above = 1 => x ≥ upper + tol : x − m·above ≥ upper + tol − m
        rows.Add(new QuadraticInequalityOf<Flt64>(
            Lhs: WithMask(_x.Monomials, m.Negate(), _above, _x.Constant),
            Rhs: new QuadraticPolynomial<Flt64>(
                Array.Empty<QuadraticMonomial<Flt64>>(),
                upper.Plus(tolerance).Minus(m)),
            Comparison: Comparison.GE,
            Name: $"{Name}_above"));

        // 5. inside + below + above = 1
        //
        // **没有它，三个状态可以同时为 0**——**于是 `y` 的两侧约束都被松弛，
        // `y` 变成自由变量，模型仍然可行。**
        //
        // **Without it the three states can all be zero** — **so both of `y`'s rows relax, `y` becomes free and the
        // model stays feasible.**
        rows.Add(new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(
                new[] {
                    Monomial(_converter.One, _inside),
                    Monomial(_converter.One, _below),
                    Monomial(_converter.One, _above),
                },
                _converter.Zero),
            Rhs: new QuadraticPolynomial<Flt64>(
                Array.Empty<QuadraticMonomial<Flt64>>(), _converter.One),
            Comparison: Comparison.EQ,
            Name: $"{Name}_partition"));

        // 6. inside = 1 => y = x : y − x − m·inside ≥ −m
        rows.Add(new QuadraticInequalityOf<Flt64>(
            Lhs: WithMask(WithMask(NegatedX(), _converter.One, _y, _x.Constant.Negate()).Monomials,
                m.Negate(), _inside, _x.Constant.Negate()),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), m.Negate()),
            Comparison: Comparison.GE,
            Name: $"{Name}_value_lb"));

        // 7. inside = 1 => y = x : y − x + m·inside ≤ m
        rows.Add(new QuadraticInequalityOf<Flt64>(
            Lhs: WithMask(WithMask(NegatedX(), _converter.One, _y, _x.Constant.Negate()).Monomials,
                m, _inside, _x.Constant.Negate()),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), m),
            Comparison: Comparison.LE,
            Name: $"{Name}_value_ub"));

        // 8. inside = 0 => y = 0 : y − m·inside ≤ 0 与 y + m·inside ≥ 0
        rows.Add(new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(
                new[] { Monomial(_converter.One, _y), Monomial(m.Negate(), _inside) },
                _converter.Zero),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), _converter.Zero),
            Comparison: Comparison.LE,
            Name: $"{Name}_zero_ub"));

        rows.Add(new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(
                new[] { Monomial(_converter.One, _y), Monomial(m, _inside) },
                _converter.Zero),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), _converter.Zero),
            Comparison: Comparison.GE,
            Name: $"{Name}_zero_lb"));

        return rows;
    }

    /// <summary>按语义求值 / Evaluate by semantics.</summary>
    /// <remarks>
    /// **容差带内返回 `null` 是刻意的**：**这不是"没算出来"，而是"这个问题在这点上没有定义"。**
    /// **两者在调用方看来一样，但区别在于：把容差带并进某一侧会让答案错，而不是让答案缺失。**
    ///
    /// **Returning `null` inside the tolerance band is deliberate**: **it does not mean "could not compute" but "the
    /// question is undefined there".**
    /// **The caller sees the same thing either way, but folding the band into one side makes the answer wrong rather
    /// than absent.**
    /// </remarks>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        if (QuadraticBounds.Evaluate(_x, values) is not { } value) {
            return null;
        }

        // 在区间内：取 x 本身。
        // Inside the interval: the value itself.
        if (value.CompareTo(Lower) >= 0 && value.CompareTo(Upper) <= 0) {
            return value;
        }

        Flt64 clearlyBelow = Lower.Minus(_tolerance);
        Flt64 clearlyAbove = Upper.Plus(_tolerance);
        if (value.CompareTo(clearlyBelow) <= 0 || value.CompareTo(clearlyAbove) >= 0) {
            return _converter.Zero;
        }

        return null;
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        if (model is not IAbstractQuadraticMechanismModel<Flt64> quadratic) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "a quadratic step range needs a quadratic mechanism model");
        }

        foreach (QuadraticInequalityOf<Flt64> row in Rows()) {
            Try added = quadratic.AddConstraint(row, row.Name, ((IIntermediateSymbol, bool)?)null);
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                return failure;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    private static Flt64 DefaultBigM(QuadraticPolynomial<Flt64> x, IIntoValue<Flt64> converter) {
        if (QuadraticBounds.Of(x, converter) is not { } bounds) {
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
}
