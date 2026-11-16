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
/// 二次下包络 <c>y = min pᵢ</c> / A quadratic lower envelope `y = min pᵢ`.
/// </summary>
/// <remarks>
/// 对齐基线 `QuadraticMin.kt` 把原本的<b>空壳</b>换成真实编码。
///
/// **这个类在此之前是空壳**：**`Evaluate` 恒返回 `null`、`RegisterConstraints` 什么都不做。**
/// **它能编译、有名字、能进注册表，所以"类型存在"的扫描把它算作已覆盖——
/// 而它对任何模型都没有作用。**
/// **"实现存在"与"实现被使用"不是一回事；这里连"实现存在"都不成立。**
///
/// **编码分两段**：
///
/// | 段 | 内容 | 何时 |
/// | --- | --- | --- |
/// | 下界 | <c>y ≤ pᵢ</c> 逐项 | 总是 |
/// | 上界 | <c>y ≥ pᵢ − Mᵢ(1 − uᵢ)</c>、<c>Σuᵢ = 1</c> | 仅 `exact` |
///
/// **只有下界时 `y` 是"不大于所有 pᵢ"的任意值**——**求解器会把它推走或留在任意可行处，
/// 于是 `y` 根本不是 min。**
///
/// **大 M 取 `Mᵢ = pᵢ.upper − minLower`**——**"第 i 项最多比最小值高出多少"，这是紧的。**
/// **用一个全局常数会让某些项的上界约束松弛到失效。**
///
/// **`u` 是 `BinVar`、且 `Σuᵢ = 1`**：**没有这条，多个 `uᵢ` 可同时为 1，
/// 于是 `y ≥ pᵢ` 对所有 i 同时成立，`y` 被迫 ≥ max 而不是 min。**
///
/// Replaces a stub with the real encoding, following the baseline's `QuadraticMin.kt`.
///
/// **The class was a stub until now**: **`Evaluate` always returned `null` and `RegisterConstraints` did nothing.**
/// **It compiled, had a name and could be registered, so a "does the type exist" scan counted it as covered — while
/// it had no effect on any model.**
/// **"The implementation exists" is not "the implementation is used"; here even the first did not hold.**
///
/// **The encoding has two parts**:
///
/// | Part | Content | When |
/// | --- | --- | --- |
/// | lower | `y ≤ pᵢ` for each i | always |
/// | upper | `y ≥ pᵢ − Mᵢ(1 − uᵢ)`, `Σuᵢ = 1` | only when `exact` |
///
/// **With only the lower rows, `y` is any value no greater than every `pᵢ`** — **the solver pushes it away or leaves
/// it wherever feasible, so `y` is not the min at all.**
///
/// **The big-M is `Mᵢ = pᵢ.upper − minLower`** — **"how far entry i can exceed the smallest lower bound", which is
/// tight.** **One global constant leaves some entries' upper rows too loose to bind.**
///
/// **`u` is `BinVar` and `Σuᵢ = 1`**: **without that row several `uᵢ` can be one at once, so `y ≥ pᵢ` holds for every
/// i and `y` is forced to at least the max rather than the min.**
/// </remarks>
public sealed class QuadraticMinFunction : IMathFunctionSymbol<Flt64> {
    private readonly QuadraticPolynomial<Flt64>[] _polynomials;
    private readonly BinVar[] _binVars;
    private readonly RealVar _resultVar;
    private readonly Flt64? _explicitBigM;
    private readonly IIntoValue<Flt64> _converter;

    /// <summary>构造一个二次下包络 / Build one.</summary>
    public QuadraticMinFunction(
        IReadOnlyList<QuadraticPolynomial<Flt64>> polynomials,
        IIntoValue<Flt64> converter,
        bool exact = true,
        Flt64? bigM = null,
        string name = "quadraticMin",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(polynomials);
        ArgumentNullException.ThrowIfNull(converter);

        if (polynomials.Count == 0) {
            throw new ArgumentException("a min needs at least one entry", nameof(polynomials));
        }

        if (bigM is { } explicitValue && explicitValue.CompareTo(converter.Zero) <= 0) {
            throw new ArgumentException(
                $"an explicit big-M must be positive, got {explicitValue}", nameof(bigM));
        }

        _polynomials = new QuadraticPolynomial<Flt64>[polynomials.Count];
        for (int index = 0; index < polynomials.Count; ++index) {
            _polynomials[index] = polynomials[index];
        }

        _converter = converter;
        _explicitBigM = bigM;
        Exact = exact;
        Name = name;
        DisplayName = displayName;

        _resultVar = new RealVar($"{name}_result");

        // **只有精确模式才需要 u。** **非精确模式下没有 `Σuᵢ = 1` 与之配对，
        // 于是它们也不该出现在辅助变量表里——否则被注册却永远不被约束。**
        //
        // **Only the exact mode needs the u variables.** **With `exact = false` there is no `Σuᵢ = 1` to pair them
        // with, so they must not appear in the helper list either, or they get registered and never constrained.**
        _binVars = exact ? CreateBinVars(name, polynomials.Count) : Array.Empty<BinVar>();
    }

    /// <summary>被比较的各个多项式 / The entries being compared.</summary>
    public IReadOnlyList<QuadraticPolynomial<Flt64>> Polynomials => _polynomials;

    /// <summary>结果变量 / The result variable.</summary>
    public RealVar ResultVar => _resultVar;

    /// <summary>精确模式的选择变量 / The selectors used in the exact mode.</summary>
    public IReadOnlyList<BinVar> BinVars => _binVars;

    /// <summary>是否精确 / Whether the encoding is exact.</summary>
    public bool Exact { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem> { _resultVar };
            helpers.AddRange(_binVars);
            return helpers.AsReadOnly();
        }
    }

    /// <summary>逐项下界 <c>y ≤ pᵢ</c> / The per-entry lower rows.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> LowerRows() {
        var rows = new QuadraticInequalityOf<Flt64>[_polynomials.Length];
        for (int index = 0; index < _polynomials.Length; ++index) {
            rows[index] = LowerRow(index);
        }

        return rows;
    }

    /// <summary>逐项上界 <c>y ≥ pᵢ − Mᵢ(1 − uᵢ)</c> / The per-entry upper rows.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> UpperRows() {
        if (!Exact) {
            return Array.Empty<QuadraticInequalityOf<Flt64>>();
        }

        if (SmallestLowerBound() is not { } minLower) {
            return Array.Empty<QuadraticInequalityOf<Flt64>>();
        }

        var rows = new QuadraticInequalityOf<Flt64>[_polynomials.Length];
        for (int index = 0; index < _polynomials.Length; ++index) {
            rows[index] = UpperRow(index, _explicitBigM ?? TightBigM(index, minLower));
        }

        return rows;
    }

    /// <summary>one-hot：<c>Σuᵢ = 1</c> / The one-hot row.</summary>
    /// <remarks>
    /// **没有它，多个 `uᵢ` 可同时为 1，于是 `y ≥ pᵢ` 对所有 i 同时成立，`y` 被迫 ≥ max 而不是 min。**
    ///
    /// **Without it several `uᵢ` can be one at once, so `y ≥ pᵢ` holds for every i and `y` is forced to at least the
    /// max rather than the min.**
    /// </remarks>
    public LinearInequality<Flt64> OneHotRow() {
        var terms = new LinearMonomial<Flt64>[_binVars.Length];
        for (int index = 0; index < _binVars.Length; ++index) {
            terms[index] = new LinearMonomial<Flt64>(_converter.One, _binVars[index]);
        }

        return new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(terms, _converter.Zero),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), _converter.One),
            Comparison: Comparison.EQ,
            Name: $"{Name}_one_hot");
    }

    /// <summary>按语义求值：直接取最小值 / Evaluate by semantics: take the minimum.</summary>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        Flt64? smallest = null;
        foreach (QuadraticPolynomial<Flt64> polynomial in _polynomials) {
            if (QuadraticBounds.Evaluate(polynomial, values) is not { } current) {
                return null;
            }

            if (smallest is not { } best || current.CompareTo(best) < 0) {
                smallest = current;
            }
        }

        return smallest;
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        // **上界需要大 M，而大 M 需要每一项都有有限界。** **推不出来就报错，不静默降级成非精确**——
        // **降级会让模型可行、目标值合理，而 `y` 根本不是 min。**
        //
        // **The upper rows need a big-M, which needs every entry to have finite bounds.** **Report rather than
        // silently degrade to the inexact mode** — **degrading keeps the model feasible with a plausible objective
        // while `y` is not the min.**
        if (Exact && _explicitBigM is null && SmallestLowerBound() is null) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "an exact min needs finite bounds on every entry, or an explicit big-M");
        }

        if (model is not IAbstractQuadraticMechanismModel<Flt64> quadratic) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "a quadratic min needs a quadratic mechanism model");
        }

        foreach (QuadraticInequalityOf<Flt64> row in LowerRows()) {
            Try added = quadratic.AddConstraint(row, row.Name, ((IIntermediateSymbol, bool)?)null);
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> lowerFailure) {
                return lowerFailure;
            }
        }

        if (!Exact) {
            return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
        }

        foreach (QuadraticInequalityOf<Flt64> row in UpperRows()) {
            Try added = quadratic.AddConstraint(row, row.Name, ((IIntermediateSymbol, bool)?)null);
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> upperFailure) {
                return upperFailure;
            }
        }

        return quadratic.AddConstraint(OneHotRow(), $"{Name}_one_hot", ((IIntermediateSymbol, bool)?)null);
    }

    /// <summary>一项的下界行 <c>result − p ≤ 0</c> / One lower row.</summary>
    private QuadraticInequalityOf<Flt64> LowerRow(int index) {
        QuadraticPolynomial<Flt64> polynomial = _polynomials[index];
        var monomials = new List<QuadraticMonomial<Flt64>>(polynomial.Monomials.Count + 1);

        // −p 展开：每个 monomial 取反。
        // -p expands by negating every monomial.
        foreach (QuadraticMonomial<Flt64> monomial in polynomial.Monomials) {
            monomials.Add(new QuadraticMonomial<Flt64>(
                monomial.Coefficient.Negate(), monomial.Symbol1, monomial.Symbol2));
        }

        monomials.Add(QuadraticMonomial<Flt64>.Linear(_converter.One, _resultVar));

        return new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(monomials, polynomial.Constant.Negate()),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), _converter.Zero),
            Comparison: Comparison.LE,
            Name: $"{Name}_lb_{index}");
    }

    /// <summary>一项的上界行 <c>result − p − M·u ≥ −M</c> / One upper row.</summary>
    private QuadraticInequalityOf<Flt64> UpperRow(int index, Flt64 bigM) {
        QuadraticPolynomial<Flt64> polynomial = _polynomials[index];
        var monomials = new List<QuadraticMonomial<Flt64>>(polynomial.Monomials.Count + 2);

        foreach (QuadraticMonomial<Flt64> monomial in polynomial.Monomials) {
            monomials.Add(new QuadraticMonomial<Flt64>(
                monomial.Coefficient.Negate(), monomial.Symbol1, monomial.Symbol2));
        }

        monomials.Add(QuadraticMonomial<Flt64>.Linear(_converter.One, _resultVar));
        monomials.Add(QuadraticMonomial<Flt64>.Linear(bigM.Negate(), _binVars[index]));

        return new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(monomials, polynomial.Constant.Negate()),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), bigM.Negate()),
            Comparison: Comparison.GE,
            Name: $"{Name}_ub_{index}");
    }

    /// <summary>第 i 项的紧大 M / Entry i's tight big-M.</summary>
    /// <remarks>
    /// **`Mᵢ = pᵢ.upper − minLower`，且至少为 1。** **取 1 的下限是为了让"所有项下界相等"时
    /// `M` 不为 0**——**`M = 0` 会让上界行退化成 `y ≥ pᵢ`，与下界行合起来把 `y` 钉死在 `pᵢ` 上。**
    ///
    /// **`Mᵢ = pᵢ.upper − minLower`, at least one.** **The floor of one keeps `M` non-zero when every lower bound
    /// coincides** — **`M = 0` degenerates the upper row into `y ≥ pᵢ`, which together with the lower row pins `y`
    /// to `pᵢ`.**
    /// </remarks>
    private Flt64 TightBigM(int index, Flt64 minLower) {
        if (FiniteUpperOf(_polynomials[index]) is not { } upper) {
            return _converter.One;
        }

        Flt64 candidate = upper.Minus(minLower);
        return candidate.CompareTo(_converter.Zero) > 0 ? candidate : _converter.One;
    }

    /// <summary>所有项下界里的最小值 / The smallest of every entry's lower bound.</summary>
    /// <remarks>
    /// **任何一项推不出界就整体返回 `null`**：**用"能推出来的那几项"的最小值会让大 M 偏小，
    /// 于是某些项的上界约束失效——而那表现为"求解成功但结果不是 min"。**
    ///
    /// **One entry without bounds makes the whole thing `null`**: **using the smallest of the entries that do have
    /// bounds makes the big-M too small, so some upper rows stop binding — and that shows up as "solved fine, but the
    /// answer is not the min".**
    /// </remarks>
    private Flt64? SmallestLowerBound() {
        Flt64? smallest = null;
        foreach (QuadraticPolynomial<Flt64> polynomial in _polynomials) {
            if (QuadraticBounds.Of(polynomial, _converter) is not { } bounds) {
                return null;
            }

            if (smallest is not { } best || bounds.Lower.CompareTo(best) < 0) {
                smallest = bounds.Lower;
            }
        }

        return smallest;
    }

    private Flt64? FiniteUpperOf(QuadraticPolynomial<Flt64> polynomial) =>
        QuadraticBounds.Of(polynomial, _converter)?.Upper;


    private static BinVar[] CreateBinVars(string name, int count) {
        var vars = new BinVar[count];
        for (int index = 0; index < count; ++index) {
            vars[index] = new BinVar($"{name}_u_{index}");
        }

        return vars;
    }
}
