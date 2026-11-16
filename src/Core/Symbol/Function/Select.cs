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
/// 二值选择：<c>mask = 1 ? then : otherwise</c> / A binary select: `mask = 1 ? then : otherwise`.
/// </summary>
/// <remarks>
/// 对齐基线 `Select.kt` 的 `SelectFunction`。
///
/// **编码的关键是"被屏蔽的差"**：令 <c>d = then − otherwise</c>，辅助变量 <c>z = mask · d</c>。
/// 于是 <c>result = otherwise + z</c>——**一条等式就够了，不需要两条分支不等式。**
///
/// **`z = mask · d` 用四条边夹住（不是等式）**：
///
/// | 边 | 不等式 |
/// | --- | --- |
/// | `zero_upper` | <c>z ≤ dU · mask</c> |
/// | `zero_lower` | <c>z ≥ dL · mask</c> |
/// | `one_upper` | <c>z ≤ d + dL · (1 − mask)</c> |
/// | `one_lower` | <c>z ≥ d + dU · (1 − mask)</c> |
///
/// **这四条在 `mask ∈ {0,1}` 时把 `z` 夹成恰好 `mask·d`**——
/// **`mask = 0` 时前两条给 `z ≤ 0` 与 `z ≥ 0`，后两条给 `z ≤ d + dL` 与 `z ≥ d + dU`（与 `d ∈ [dL,dU]` 一致）；**
/// **`mask = 1` 时反过来。**
///
/// **四条边里 `dL` 与 `dU` 的位置不能互换**：**`zero_upper` 用 `dU`（因为 `mask = 0` 时它必须允许 `z = 0`）、
/// `one_lower` 用 `dU`；写成 `zero_upper` 用 `dL` 会让 `mask = 0` 时 `z` 被迫为负。**
///
/// B1153, following the baseline's `SelectFunction`.
///
/// **The encoding turns on "the masked difference"**: let `d = then − otherwise` and an auxiliary `z = mask · d`.
/// Then `result = otherwise + z` — **one equality suffices; no pair of branch inequalities is needed.**
///
/// **`z = mask · d` is bracketed by four rows rather than an equality**:
///
/// | Edge | Inequality |
/// | --- | --- |
/// | `zero_upper` | `z ≤ dU · mask` |
/// | `zero_lower` | `z ≥ dL · mask` |
/// | `one_upper` | `z ≤ d + dL · (1 − mask)` |
/// | `one_lower` | `z ≥ d + dU · (1 − mask)` |
///
/// **With `mask ∈ {0,1}` these four pin `z` to exactly `mask·d`** —
/// **at `mask = 0` the first two give `z ≤ 0` and `z ≥ 0`, the last two give `z ≤ d + dL` and `z ≥ d + dU` (consistent
/// with `d ∈ [dL, dU]`);** **at `mask = 1` it is the other way round.**
///
/// **`dL` and `dU` cannot be swapped**: **`zero_upper` uses `dU` (with `mask = 0` it has to allow `z = 0`) and
/// `one_lower` uses `dU`; writing `dL` in `zero_upper` would force `z` negative at `mask = 0`.**
/// </remarks>
public sealed class SelectFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly RealVar _resultVar;
    private readonly RealVar _maskedDifferenceVar;
    private readonly LinearPolynomial<V> _difference;

    /// <summary>构造一个二值选择 / Build one.</summary>
    public SelectFunction(
        IVariableItem mask,
        LinearPolynomial<V> then,
        LinearPolynomial<V> otherwise,
        IIntoValue<V> converter,
        ConditionBounds<V>? thenBounds = null,
        ConditionBounds<V>? otherwiseBounds = null,
        string name = "select",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(then);
        ArgumentNullException.ThrowIfNull(otherwise);
        ArgumentNullException.ThrowIfNull(converter);

        // **选择变量必须是二值的。** **连续变量会让"mask = 1 ? then : otherwise"这个语义不存在**——
        // **而四条边仍然会构造成功，只是 `mask = 0.5` 时给出一个介于两者之间的值，看起来像插值。**
        //
        // **The selector must be binary.** **A continuous variable makes the "mask = 1 ? then : otherwise" semantics
        // nonexistent** — **while the four rows still build fine, yielding an in-between value at mask = 0.5 that looks
        // like interpolation.**
        if (mask is not BinVar) {
            throw new ArgumentException(
                $"a select needs a binary selector, not {mask.GetType().Name}",
                nameof(mask));
        }

        Mask = mask;
        Then = then;
        Otherwise = otherwise;
        Converter = converter;
        Name = name;
        DisplayName = displayName;

        // 分支区间：优先用调用方给的，否则用多项式自己的有限界。
        // Branch intervals: the caller's when given, otherwise the polynomial's own finite bounds.
        ThenBounds = thenBounds ?? PolynomialBounds.FiniteBoundsOf(then, converter)
            ?? throw new ArgumentException(
                "the then-branch needs finite bounds: pass them explicitly or bound its variables",
                nameof(thenBounds));
        OtherwiseBounds = otherwiseBounds ?? PolynomialBounds.FiniteBoundsOf(otherwise, converter)
            ?? throw new ArgumentException(
                "the else-branch needs finite bounds: pass them explicitly or bound its variables",
                nameof(otherwiseBounds));

        _difference = then - otherwise;

        // d = then - otherwise 的界：最坏情况是 then 的上界减 otherwise 的下界。
        // The bounds of d = then - otherwise: worst case is then's upper minus otherwise's lower.
        DifferenceBounds = new ConditionBounds<V>(
            ThenBounds.Lower.Minus(OtherwiseBounds.Upper),
            ThenBounds.Upper.Minus(OtherwiseBounds.Lower));

        ResultBounds = new ConditionBounds<V>(
            ThenBounds.Lower.CompareTo(OtherwiseBounds.Lower) <= 0 ? ThenBounds.Lower : OtherwiseBounds.Lower,
            ThenBounds.Upper.CompareTo(OtherwiseBounds.Upper) >= 0 ? ThenBounds.Upper : OtherwiseBounds.Upper);

        _resultVar = new RealVar($"{name}_select");
        _maskedDifferenceVar = new RealVar($"{name}_select_delta");

        _resultVar.Range.Geq(converter.FromValue(ResultBounds.Lower));
        _resultVar.Range.Leq(converter.FromValue(ResultBounds.Upper));

        // z = mask*d 的范围：mask ∈ [0,1]，所以 z 落在 d 的界与 0 的并集里。
        // z = mask*d ranges over the union of d's bounds and zero, since mask is in [0, 1].
        V maskLower = DifferenceBounds.Lower.CompareTo(converter.Zero) < 0
            ? DifferenceBounds.Lower
            : converter.Zero;
        V maskUpper = DifferenceBounds.Upper.CompareTo(converter.Zero) > 0
            ? DifferenceBounds.Upper
            : converter.Zero;
        _maskedDifferenceVar.Range.Geq(converter.FromValue(maskLower));
        _maskedDifferenceVar.Range.Leq(converter.FromValue(maskUpper));
    }

    /// <summary>选择变量 / The selector.</summary>
    public IVariableItem Mask { get; }

    /// <summary>`mask = 1` 时的分支 / The branch taken when `mask = 1`.</summary>
    public LinearPolynomial<V> Then { get; }

    /// <summary>`mask = 0` 时的分支 / The branch taken when `mask = 0`.</summary>
    public LinearPolynomial<V> Otherwise { get; }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>`then` 的区间 / The then-branch's interval.</summary>
    public ConditionBounds<V> ThenBounds { get; }

    /// <summary>`otherwise` 的区间 / The else-branch's interval.</summary>
    public ConditionBounds<V> OtherwiseBounds { get; }

    /// <summary>`then − otherwise` 的区间 / The interval of `then − otherwise`.</summary>
    public ConditionBounds<V> DifferenceBounds { get; }

    /// <summary>结果的区间 / The interval of the result.</summary>
    public ConditionBounds<V> ResultBounds { get; }

    /// <summary>结果辅助变量 / The result auxiliary variable.</summary>
    public RealVar ResultVar => _resultVar;

    /// <summary>被屏蔽的差 / The masked difference.</summary>
    public RealVar MaskedDifferenceVar => _maskedDifferenceVar;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => new IVariableItem[] { _resultVar, _maskedDifferenceVar };

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(new[] { Then, Otherwise });

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(Converter.One, _resultVar) },
        Converter.Zero);

    /// <summary>
    /// 四条屏蔽边 / The four masking rows.
    /// </summary>
    /// <remarks>
    /// **顺序固定为零上、零下、一上、一下**，**因为判据要逐条核对 `dL`/`dU` 的位置。**
    ///
    /// **The order is fixed at zero-upper, zero-lower, one-upper, one-lower**, **because the criteria check where `dL`
    /// and `dU` land.**
    /// </remarks>
    public IReadOnlyList<LinearInequality<V>> MaskingRows() {
        V dL = DifferenceBounds.Lower;
        V dU = DifferenceBounds.Upper;
        V zero = Converter.Zero;

        // z - dU*mask <= 0
        // z - dL*mask >= 0
        // z - d + dL*(1 - mask) <= 0  →  z - d - dL*mask + dL <= 0
        // z - d + dU*(1 - mask) >= 0  →  z - d - dU*mask + dU >= 0
        return new[] {
            Row(
                new[] { (Converter.One, (ISymbol)_maskedDifferenceVar), (dU.Negate(), (ISymbol)Mask) },
                zero, Comparison.LE, "zero_upper"),
            Row(
                new[] { (Converter.One, (ISymbol)_maskedDifferenceVar), (dL.Negate(), (ISymbol)Mask) },
                zero, Comparison.GE, "zero_lower"),
            DifferenceRow(dL, Comparison.LE, "one_upper"),
            DifferenceRow(dU, Comparison.GE, "one_lower"),
        };
    }

    /// <summary>
    /// 结果等式 <c>result − z − otherwise = 0</c> / The result equality `result − z − otherwise = 0`.
    /// </summary>
    public LinearInequality<V> ResultRow() {
        var terms = new List<LinearMonomial<V>> {
            new(Converter.One, _resultVar),
            new(Converter.One.Negate(), _maskedDifferenceVar),
        };

        foreach (LinearMonomial<V> monomial in Otherwise.Monomials) {
            terms.Add(new LinearMonomial<V>(monomial.Coefficient.Negate(), monomial.Symbol));
        }

        return new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(terms, Otherwise.Constant.Negate()),
            Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), Converter.Zero),
            Comparison: Comparison.EQ,
            Name: $"{Name}_select_result");
    }

    /// <summary>两条分支各自的区间约束 / The interval rows for both branches.</summary>
    public IReadOnlyList<LinearInequality<V>> BranchRows() => new[] {
        new LinearInequality<V>(
            Lhs: Then, Rhs: Constant(ThenBounds.Upper), Comparison: Comparison.LE,
            Name: $"{Name}_select_then_upper"),
        new LinearInequality<V>(
            Lhs: Then, Rhs: Constant(ThenBounds.Lower), Comparison: Comparison.GE,
            Name: $"{Name}_select_then_lower"),
        new LinearInequality<V>(
            Lhs: Otherwise, Rhs: Constant(OtherwiseBounds.Upper), Comparison: Comparison.LE,
            Name: $"{Name}_select_else_upper"),
        new LinearInequality<V>(
            Lhs: Otherwise, Rhs: Constant(OtherwiseBounds.Lower), Comparison: Comparison.GE,
            Name: $"{Name}_select_else_lower"),
    };

    /// <summary>
    /// 按语义求值：只看 `mask`，不看辅助变量 / Evaluate by semantics: only `mask`, no auxiliary variables.
    /// </summary>
    /// <remarks>
    /// **这条路径不经过那四条边**，**所以它验证的是"语义对不对"，不是"编码对不对"。**
    /// **编码由 `MaskingRows` / `ResultRow` 的系数判据单独钉住。**
    ///
    /// **This path does not go through the four rows**, **so it verifies the semantics, not the encoding.**
    /// **The encoding is pinned separately by the criteria on `MaskingRows` / `ResultRow` coefficients.**
    /// </remarks>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        V? maskValue = values.TryGetValue(Mask, out V stored) ? stored : null;
        if (maskValue is not { } selector) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        V? thenValue = FunctionEvaluation.Polynomial(Then, values).Value;
        V? elseValue = FunctionEvaluation.Polynomial(Otherwise, values).Value;
        if (thenValue is not { } chosen || elseValue is not { } rejected) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        // 分支值必须落在各自声明的区间里——否则编码的假设已经不成立。
        // The branch values must lie inside their declared intervals, or the encoding's premise no longer holds.
        if (!ThenBounds.Contains(chosen) || !OtherwiseBounds.Contains(rejected)) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        if (selector.CompareTo(Converter.Zero) == 0) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(rejected);
        }

        if (selector.CompareTo(Converter.One) == 0) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(chosen);
        }

        // mask 既不是 0 也不是 1：不是"取中间值"，而是输入不合法。
        // A mask that is neither zero nor one is an illegal input, not "take the middle".
        return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        var constraints = new List<LinearInequality<V>>(BranchRows());
        constraints.AddRange(MaskingRows());
        constraints.Add(ResultRow());
        return IndicatorSupport.AddConstraints(model, constraints);
    }

    /// <summary>造一条 `z − d + k·(1 − mask) ≶ 0` 行 / Build a `z − d + k·(1 − mask) ≶ 0` row.</summary>
    private LinearInequality<V> DifferenceRow(V offset, Comparison comparison, string suffix) {
        var terms = new List<LinearMonomial<V>> {
            new(Converter.One, _maskedDifferenceVar),
        };

        // −d 展开：把 _difference 的每个 monomial 与常数取反。
        // -d expands to the negation of every monomial and the constant of _difference.
        foreach (LinearMonomial<V> monomial in _difference.Monomials) {
            terms.Add(new LinearMonomial<V>(monomial.Coefficient.Negate(), monomial.Symbol));
        }

        terms.Add(new LinearMonomial<V>(offset.Negate(), (ISymbol)Mask));

        return new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(terms, _difference.Constant.Negate()),
            Rhs: Constant(offset.Negate()),
            Comparison: comparison,
            Name: $"{Name}_select_{suffix}");
    }

    private LinearInequality<V> Row(
        (V Coefficient, ISymbol Symbol)[] terms, V rhs, Comparison comparison, string suffix) =>
        new(
            Lhs: new LinearPolynomial<V>(
                Array.ConvertAll(terms, term => new LinearMonomial<V>(term.Coefficient, term.Symbol)),
                Converter.Zero),
            Rhs: Constant(rhs),
            Comparison: comparison,
            Name: $"{Name}_select_{suffix}");

    private LinearPolynomial<V> Constant(V value) => new(Array.Empty<LinearMonomial<V>>(), value);

}
