#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
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
/// 互补约束 <c>x ⊥ y</c>：两者至多一个为正 / Complementarity `x ⊥ y`: at most one of them is positive.
/// </summary>
/// <remarks>
/// B1156 按基线 `Complementarity.kt` 的 `ComplementarityFunction` 补。
///
/// **六条约束，一个二值变量 `s`**：
///
/// | 行 | 内容 |
/// | --- | --- |
/// | 1–2 | <c>xL ≤ x ≤ xU</c> |
/// | 3–4 | <c>yL ≤ y ≤ yU</c> |
/// | 5 | <c>x − xU·s ≤ 0</c> |
/// | 6 | <c>y + yU·s ≤ yU</c> |
///
/// **第 5、6 行各自只有一个大 M 项，靠非负性把另一个逼到 0**：
/// **`s = 0` 时第 5 行给 <c>x ≤ 0</c>，配合 <c>x ≥ 0</c> 得 <c>x = 0</c>；**
/// **`s = 1` 时第 6 行给 <c>y ≤ 0</c>，配合 <c>y ≥ 0</c> 得 <c>y = 0</c>。**
///
/// **所以两侧的区间必须非负**——**这正是"靠非负性"这个机制的前提，
/// 而一个负的 `xL` 会让 `s = 0` 时的 `x = 0` 不成立，于是互补性静默失效（两侧都能为正）。**
///
/// **两个大 M 的符号相反**（第 5 行是 `−xU`、第 6 行是 `+yU`）——
/// **写成都用负号会让 `s = 1` 时 `y` 反而被放大。**
///
/// **`Evaluate` 返回的是"违反指示"而不是"满足指示"**：
/// **两侧同时严格为正时返回 1，否则返回 0。** **这是基线的语义，也是最容易读反的一处。**
///
/// B1156, following the baseline's `ComplementarityFunction`.
///
/// **Six rows and one binary `s`**:
///
/// | Rows | Content |
/// | --- | --- |
/// | 1–2 | `xL ≤ x ≤ xU` |
/// | 3–4 | `yL ≤ y ≤ yU` |
/// | 5 | `x − xU·s ≤ 0` |
/// | 6 | `y + yU·s ≤ yU` |
///
/// **Rows 5 and 6 carry a single big-M term each and rely on non-negativity to force the other side to zero**:
/// **`s = 0` makes row 5 read `x ≤ 0`, which with `x ≥ 0` gives `x = 0`;**
/// **`s = 1` makes row 6 read `y ≤ 0`, which with `y ≥ 0` gives `y = 0`.**
///
/// **So both intervals must be non-negative** — **that is the premise of the "rely on non-negativity" mechanism, and
/// a negative `xL` breaks `x = 0` at `s = 0`, so complementarity silently stops holding (both sides can be
/// positive).**
///
/// **The two big-M terms have opposite signs** (row 5 uses `−xU`, row 6 uses `+yU`) —
/// **making both negative would enlarge `y` at `s = 1` instead.**
///
/// **`Evaluate` returns a violation indicator, not a satisfaction indicator**:
/// **it returns 1 when both sides are strictly positive and 0 otherwise.** **That is the baseline's semantics and the
/// easiest thing here to read the wrong way round.**
/// </remarks>
public sealed class ComplementarityFunction<V> : IMathFunctionSymbol<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly BinVar _selector;

    /// <summary>构造一个互补约束 / Build one.</summary>
    public ComplementarityFunction(
        LinearPolynomial<V> x,
        LinearPolynomial<V> y,
        IIntoValue<V> converter,
        ConditionBounds<V>? xBounds = null,
        ConditionBounds<V>? yBounds = null,
        string name = "complementarity",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(converter);

        X = x;
        Y = y;
        Converter = converter;
        Name = name;
        DisplayName = displayName;

        XBounds = xBounds ?? PolynomialBounds.FiniteBoundsOf(x, converter)
            ?? throw new ArgumentException(
                "the left side needs finite non-negative bounds: pass them or bound its variables",
                nameof(xBounds));
        YBounds = yBounds ?? PolynomialBounds.FiniteBoundsOf(y, converter)
            ?? throw new ArgumentException(
                "the right side needs finite non-negative bounds: pass them or bound its variables",
                nameof(yBounds));

        // **两侧都必须非负**——六条约束里的第 5、6 行靠这一点把另一侧逼到 0。
        // **Both sides must be non-negative** — rows 5 and 6 rely on it to force the other side to zero.
        if (XBounds.Lower.CompareTo(converter.Zero) < 0) {
            throw new ArgumentException(
                $"the left side must be non-negative for complementarity to hold (lower={XBounds.Lower})",
                nameof(xBounds));
        }

        if (YBounds.Lower.CompareTo(converter.Zero) < 0) {
            throw new ArgumentException(
                $"the right side must be non-negative for complementarity to hold (lower={YBounds.Lower})",
                nameof(yBounds));
        }

        _selector = new BinVar($"{name}_complementarity");
    }

    /// <summary>左侧 / The left side.</summary>
    public LinearPolynomial<V> X { get; }

    /// <summary>右侧 / The right side.</summary>
    public LinearPolynomial<V> Y { get; }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>左侧区间 / The left side's interval.</summary>
    public ConditionBounds<V> XBounds { get; }

    /// <summary>右侧区间 / The right side's interval.</summary>
    public ConditionBounds<V> YBounds { get; }

    /// <summary>二值选择变量 / The binary selector.</summary>
    public BinVar Selector => _selector;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => new IVariableItem[] { _selector };

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(new[] { X, Y });

    /// <summary>六条约束 / The six rows.</summary>
    public IReadOnlyList<LinearInequality<V>> Rows() => new[] {
        new LinearInequality<V>(
            Lhs: X, Rhs: Constant(XBounds.Upper), Comparison: Comparison.LE,
            Name: $"{Name}_x_domain_upper"),
        new LinearInequality<V>(
            Lhs: X, Rhs: Constant(XBounds.Lower), Comparison: Comparison.GE,
            Name: $"{Name}_x_domain_lower"),
        new LinearInequality<V>(
            Lhs: Y, Rhs: Constant(YBounds.Upper), Comparison: Comparison.LE,
            Name: $"{Name}_y_domain_upper"),
        new LinearInequality<V>(
            Lhs: Y, Rhs: Constant(YBounds.Lower), Comparison: Comparison.GE,
            Name: $"{Name}_y_domain_lower"),

        // **`x ≤ xU · s`**：s = 0 时把 x 逼到 0（配合 x ≥ 0）。
        // **`x ≤ xU · s`**: at s = 0 it forces x to zero (with x >= 0).
        new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(
                Append(X.Monomials, new LinearMonomial<V>(XBounds.Upper.Negate(), _selector)),
                X.Constant),
            Rhs: Constant(Converter.Zero),
            Comparison: Comparison.LE,
            Name: $"{Name}_x_mutex"),

        // **`y ≤ yU · (1 − s)`**，即 `y + yU·s ≤ yU`：s = 1 时把 y 逼到 0。
        // **`y ≤ yU · (1 - s)`**, i.e. `y + yU*s <= yU`: at s = 1 it forces y to zero.
        new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(
                Append(Y.Monomials, new LinearMonomial<V>(YBounds.Upper, _selector)),
                Y.Constant),
            Rhs: Constant(YBounds.Upper),
            Comparison: Comparison.LE,
            Name: $"{Name}_y_mutex"),
    };

    /// <summary>
    /// 违反指示：两侧同时严格为正时返回 1 / A violation indicator: 1 when both sides are strictly positive.
    /// </summary>
    /// <remarks>
    /// **返回 `null` 的两种输入**：**某一侧求不出来**、**某一侧为负或越界**（互补性只在非负区间上有意义）。
    ///
    /// **Two inputs return `null`**: **a side cannot be evaluated**, **or a side is negative or out of range**
    /// (complementarity only means anything on non-negative intervals).
    /// </remarks>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        V? leftValue = FunctionEvaluation.Polynomial(X, values).Value;
        V? rightValue = FunctionEvaluation.Polynomial(Y, values).Value;
        if (leftValue is not { } left || rightValue is not { } right) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        if (!XBounds.Contains(left) || !YBounds.Contains(right)) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        if (left.CompareTo(Converter.Zero) < 0 || right.CompareTo(Converter.Zero) < 0) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        bool bothPositive = left.CompareTo(Converter.Zero) > 0 && right.CompareTo(Converter.Zero) > 0;
        return new Ok<V?, ErrorCode, Error<ErrorCode>>(bothPositive ? Converter.One : Converter.Zero);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) =>
        IndicatorSupport.AddConstraints(model, Rows());

    private static IReadOnlyList<LinearMonomial<V>> Append(
        IReadOnlyList<LinearMonomial<V>> monomials, LinearMonomial<V> extra) {
        var all = new List<LinearMonomial<V>>(monomials.Count + 1);
        foreach (LinearMonomial<V> monomial in monomials) {
            all.Add(monomial);
        }

        all.Add(extra);
        return all;
    }

    private LinearPolynomial<V> Constant(V value) => new(Array.Empty<LinearMonomial<V>>(), value);
}
