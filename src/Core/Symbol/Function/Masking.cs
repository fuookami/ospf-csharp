#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 掩码函数 / Masking function
/// </summary>
/// <remarks>
/// `y = x × mask`（`mask` 为二值）。`mask = 1` 时 `y = x`，`mask = 0` 时 `y = 0`。
///
/// 用**四行 Big-M** 表达，而不是"当 mask=0 时令 y=0"那种只在一边生效的写法：
/// <list type="number">
/// <item>`y - upper·mask &lt;= 0`（mask=0 → y ≤ 0）</item>
/// <item>`y - lower·mask &gt;= 0`（mask=0 → y ≥ 0）</item>
/// <item>`y - x - lower·mask &lt;= -lower`（mask=1 → y ≤ x）</item>
/// <item>`y - x - upper·mask &gt;= -upper`（mask=1 → y ≥ x）</item>
/// </list>
/// ①② 一起把 `y` 夹成 0，③④ 一起把 `y` 夹成 `x`。**少任何一行都会让 `y` 在某个 mask 值下自由浮动**，
/// 而自由的 `y` 通常会被目标推到边界上，读起来恰好是个"像样"的值。
///
/// Expressed with **four Big-M rows** rather than "pin y=0 when mask=0": (1) `y - upper·mask &lt;= 0`; (2) `y - lower·mask &gt;= 0`; (3) `y - x - lower·mask &lt;= -lower`;
/// (4) `y - x - upper·mask &gt;= -upper`. Rows 1–2 clamp `y` to 0 together, rows 3–4 clamp `y` to `x` together. **Dropping any one row leaves `y` free at some mask value**, and a
/// free `y` is usually pushed to a bound by the objective, reading back as a plausible value.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class MaskingFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly V _bigM;
    private readonly V _lower;
    private readonly V _upper;

    /// <summary>输入多项式 / The input polynomial.</summary>
    public LinearPolynomial<V> Input { get; }
    /// <summary>二值掩码变量 / The binary mask variable.</summary>
    public IVariableItem Mask { get; }
    /// <summary>结果变量 / The result variable.</summary>
    public RealVar ResultVar { get; }

    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => new IVariableItem[] { ResultVar };

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Input).Append(Mask).ToArray();

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(_converter.One, ResultVar) },
        _converter.Zero);

    /// <summary>
    /// 创建掩码函数 / Create a masking function.
    /// </summary>
    /// <param name="input">输入多项式 / The input polynomial.</param>
    /// <param name="mask">二值掩码变量 / The binary mask variable.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选 Big-M；默认由输入边界推导 / Optional Big-M; inferred from the input bounds by default.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public MaskingFunction(
        LinearPolynomial<V> input,
        IVariableItem mask,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "masking",
        string? displayName = null) {
        if (input is null) {
            throw new ArgumentNullException(nameof(input));
        }
        if (mask is null) {
            throw new ArgumentNullException(nameof(mask));
        }
        if (converter is null) {
            throw new ArgumentNullException(nameof(converter));
        }
        Input = new LinearPolynomial<V>(Array.AsReadOnly(input.Monomials.ToArray()), input.Constant);
        Mask = mask;
        _converter = converter;
        _bigM = bigM ?? BigMValues.InferredBigM(Input, converter) ?? FallbackBigM(converter);
        LinearPolynomialBounds<V>? bounds = BigMValues.FiniteBounds(Input, converter);
        _lower = bounds?.LowerBound ?? _bigM.Negate();
        _upper = bounds?.UpperBound ?? _bigM;
        Name = name;
        DisplayName = displayName;
        ResultVar = new RealVar(name + "_masking");
    }

    /// <summary>推导失败时回退到 `1e6` / The code falls back to `1e6` when the inference fails.</summary>
    private static V FallbackBigM(IIntoValue<V> converter) => converter.IntoValue(new Flt64(1e6));

    /// <summary>Big-M 上下界 / The Big-M bounds.</summary>
    public (V Lower, V Upper) Bounds => (_lower, _upper);

    /// <inheritdoc/>
    /// <remarks>
    /// `mask` 读不到 → **零**；`mask = 0` → 零；否则是输入的值。
    /// 第一条容易被写成"读不到就 `null`"——那会把"掩码未知"变成"结果未知"，而掩码本身是个决策变量。
    ///
    /// A missing `mask` → **zero**; `mask = 0` → zero; otherwise the input's value. The first rule is easy to write as "missing means `null`" — which turns
    /// "the mask is unknown" into "the result is unknown", while the mask is a decision variable.
    /// </remarks>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        if (values is null || !values.TryGetValue(Mask, out V maskValue)) {
            return Results.Ok<V?>(_converter.Zero);
        }
        if (maskValue.Eq(_converter.Zero)) {
            return Results.Ok<V?>(_converter.Zero);
        }
        return FunctionEvaluation.Polynomial(Input, values);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => tokens.Add(HelperVariables);

    /// <inheritdoc/>
    /// <remarks>
    /// `deferredStructure` 一条路径（把掩码交给 `ConditionalValue` 那套中间结构去生成约束）。C# 没有
    /// `MaskingStructure` / `ConditionalValue`，所以**只实现直接的四行 Big-M**——这也是掩码不是二值类型时
    /// 自己走的那一支。
    ///
    /// The code has a `deferredStructure` path handing the mask to the `ConditionalValue` intermediate machinery. C# has no `MaskingStructure` / `ConditionalValue`, so only
    /// the direct four-row Big-M is implemented — which is also the branch the code itself takes when the mask is not binary.
    /// </remarks>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (model is null) {
            throw new ArgumentNullException(nameof(model));
        }
        if (!BigMValues.IsUsable(_bigM, _converter) || !BigMValues.IsUsable(_lower, _converter) || !BigMValues.IsUsable(_upper, _converter)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "掩码的 Big-M 或边界含非有限、不可表示值。 / The masking Big-M or bounds contain a non-finite or unrepresentable value."));
        }

        var zero = new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _converter.Zero);
        LinearMonomial<V> result = new(_converter.One, ResultVar);
        LinearMonomial<V>[] negatedInput = Input.Monomials
            .Select(monomial => new LinearMonomial<V>(monomial.Coefficient.Negate(), monomial.Symbol))
            .ToArray();

        // ③④ 的右侧是 `-lower` / `-upper`，左侧的常量是 `-input.constant`：把 `y - x` 里的 `x` 拆成"项 + 常量"再搬过去。
        // Rows 3–4 carry `-lower` / `-upper` on the right while the left constant is `-input.constant`: `y - x` splits `x` into terms plus a constant that moves across.
        LinearInequality<V>[] constraints = new[] {
            new LinearInequality<V>(
                new LinearPolynomial<V>(
                    new[] { result, new LinearMonomial<V>(_upper.Negate(), Mask) },
                    _converter.Zero),
                zero,
                Comparison.LE,
                Name + "_masking_zero_ub"),
            new LinearInequality<V>(
                new LinearPolynomial<V>(
                    new[] { result, new LinearMonomial<V>(_lower.Negate(), Mask) },
                    _converter.Zero),
                zero,
                Comparison.GE,
                Name + "_masking_zero_lb"),
            new LinearInequality<V>(
                new LinearPolynomial<V>(
                    negatedInput.Append(result).Append(new LinearMonomial<V>(_lower.Negate(), Mask)).ToArray(),
                    Input.Constant.Negate()),
                new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _lower.Negate()),
                Comparison.LE,
                Name + "_masking_eq_ub"),
            new LinearInequality<V>(
                new LinearPolynomial<V>(
                    negatedInput.Append(result).Append(new LinearMonomial<V>(_upper.Negate(), Mask)).ToArray(),
                    Input.Constant.Negate()),
                new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _upper.Negate()),
                Comparison.GE,
                Name + "_masking_eq_lb"),
        };

        return IndicatorSupport.AddConstraints(model, constraints);
    }
}

/// <summary>
/// 掩码为多项式的掩码函数 / Masking function whose mask is a polynomial
/// </summary>
/// <remarks>
/// 做法：先建一个二值变量 `m` 并用一条等式把它**定义成**掩码多项式，再套标准的掩码约束。
/// 直接拿多项式当掩码写进 Big-M 行会在 `mask` 被多个变量共享时失去整数性，
/// 而失去之后模型**仍然可解**，只是解里 `mask` 可以是 0.5。
///
/// The code builds a binary variable `m`, defines it by an equality against the mask polynomial, then applies the standard masking rows. Writing the polynomial straight into the
/// Big-M rows loses integrality when `mask` is shared by several variables — and the model **still solves**, only with `mask = 0.5` in the answer.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class MaskingWithPolyMaskFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly MaskingFunction<V> _inner;
    private readonly LinearPolynomial<V> _maskPolynomial;
    private readonly IIntoValue<V> _converter;

    /// <summary>掩码多项式 / The mask polynomial.</summary>
    public LinearPolynomial<V> MaskPolynomial => _maskPolynomial;
    /// <summary>内部建出的二值掩码变量 / The binary mask variable created internally.</summary>
    public BinVar MaskVar { get; }
    /// <summary>结果变量 / The result variable.</summary>
    public RealVar ResultVar => _inner.ResultVar;
    /// <summary>输入多项式 / The input polynomial.</summary>
    public LinearPolynomial<V> Input => _inner.Input;

    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => new IVariableItem[] { MaskVar, ResultVar };

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols =>
        FunctionInputs.Of(_inner.Input).Concat(FunctionInputs.Of(_maskPolynomial)).Append(MaskVar).ToArray();

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _inner.ResultPolynomial;

    /// <summary>
    /// 创建多项式掩码的掩码函数 / Create a masking function with a polynomial mask.
    /// </summary>
    /// <param name="input">输入多项式 / The input polynomial.</param>
    /// <param name="maskPolynomial">掩码多项式 / The mask polynomial.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选 Big-M；默认由输入边界推导 / Optional Big-M; inferred from the input bounds by default.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public MaskingWithPolyMaskFunction(
        LinearPolynomial<V> input,
        LinearPolynomial<V> maskPolynomial,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "maskingWithPolyMask",
        string? displayName = null) {
        if (maskPolynomial is null) {
            throw new ArgumentNullException(nameof(maskPolynomial));
        }
        _maskPolynomial = new LinearPolynomial<V>(Array.AsReadOnly(maskPolynomial.Monomials.ToArray()), maskPolynomial.Constant);
        _converter = converter;
        MaskVar = new BinVar(name + "_mask_var");
        _inner = new MaskingFunction<V>(input, MaskVar, converter, bigM, name, displayName);
        Name = name;
        DisplayName = displayName;
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => _inner.Evaluate(values);

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) => _inner.TryEvaluate(values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (model is null) {
            throw new ArgumentNullException(nameof(model));
        }
        // 先把 `m` 定义成掩码多项式，**再**套掩码约束：反过来的话掩码行里的 `m` 还没有定义，
        // 求解器可以给它任意值，约束就形同虚设。
        // Define `m` as the mask polynomial **first**, then apply the masking rows: reversed, the `m` in those rows is still undefined, the solver can set it freely and the rows are
        // decorative.
        var zero = new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _converter.Zero);
        LinearInequality<V> definition = new(
            new LinearPolynomial<V>(
                _maskPolynomial.Monomials
                    .Append(new LinearMonomial<V>(_converter.One.Negate(), MaskVar))
                    .ToArray(),
                _maskPolynomial.Constant),
            zero,
            Comparison.EQ,
            Name + "_mask_definition");
        Try defined = IndicatorSupport.AddConstraints(model, new[] { definition });
        if (!defined.IsOk) {
            return defined;
        }
        return _inner.RegisterConstraints(model);
    }
}

/// <summary>
/// 掩码区间函数 / Masking range function
/// </summary>
/// <remarks>
/// **仍是空壳**（`Evaluate` 恒 `null`、`RegisterConstraints` 恒 `Ok`）。`MaskingRangeFunction` 与
/// `MaskingFunction` 共用 `MaskingStructure` 那条延迟结构路径，而 C# 没有那套中间结构——把它做实要先把
/// 等价的多行 Big-M 推导出来，属于独立一批的量。列在这里是为了不让人以为它已经被 覆盖。
///
/// **Still an empty stub** (`Evaluate` always null, `RegisterConstraints` always Ok). The code's `MaskingRangeFunction` shares the deferred `MaskingStructure` path with
/// `MaskingFunction`, and C# has no such intermediate machinery — making it real requires deriving the equivalent multi-row Big-M separately. It is listed here so nobody assumes
/// covered it.
/// </remarks>
public sealed class MaskingRangeFunction : IMathFunctionSymbol<Flt64> {
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public MaskingRangeFunction(string name = "maskingRange", string? displayName = null) {
        Name = name;
        DisplayName = displayName;
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => null;

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        Results.Ok<Success>(Results.SuccessInstance);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
