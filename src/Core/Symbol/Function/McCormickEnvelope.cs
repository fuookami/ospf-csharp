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
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 双线性项 <c>x·y</c> 的 McCormick 包络 / The McCormick envelope of the bilinear term `x·y`.
/// </summary>
/// <remarks>
/// B1152 按基线 `McCormickEnvelope.kt` 补。
///
/// 给定 <c>x ∈ [xL, xU]</c>、<c>y ∈ [yL, yU]</c>，用一个辅助变量 <c>w</c> 夹住 <c>x·y</c>：
///
/// | 边 | 不等式 |
/// | --- | --- |
/// | 下左 | <c>w ≥ xL·y + yL·x − xL·yL</c> |
/// | 下右 | <c>w ≥ xU·y + yU·x − xU·yU</c> |
/// | 上左 | <c>w ≤ xU·y + yL·x − xU·yL</c> |
/// | 上右 | <c>w ≤ xL·y + yU·x − xL·yU</c> |
///
/// **四条边的方向不是"两条上界两条下界"那么随便配的**：
/// **两条 `≥` 必须用「一个角的两侧同号」——`(xL,yL)` 与 `(xU,yU)`；**
/// **两条 `≤` 必须用「交叉配对」——`(xU,yL)` 与 `(xL,yU)`。**
/// **把它们配错（例如 `≥` 用交叉配对）会得到一个不含 `x·y` 的多面体，于是松弛"合法"但无效。**
///
/// **包络不是等式**：**`w` 只在 `x` 或 `y` 取到端点时才恰好等于 `x·y`；在区间内部它是松弛的。**
/// **所以它只能用于松弛，不能用来"计算乘积"。**
///
/// B1152, following the baseline's `McCormickEnvelope.kt`.
///
/// Given `x ∈ [xL, xU]` and `y ∈ [yL, yU]`, an auxiliary `w` brackets `x·y`:
///
/// | Edge | Inequality |
/// | --- | --- |
/// | lower-left | `w ≥ xL·y + yL·x − xL·yL` |
/// | lower-right | `w ≥ xU·y + yU·x − xU·yU` |
/// | upper-left | `w ≤ xU·y + yL·x − xU·yL` |
/// | upper-right | `w ≤ xL·y + yU·x − xL·yU` |
///
/// **The pairing of the four edges is not arbitrary**:
/// **the two `≥` rows use the "same corner on both sides" pairing, `(xL,yL)` and `(xU,yU)`;**
/// **the two `≤` rows use the cross pairing, `(xU,yL)` and `(xL,yU)`.**
/// **Swapping them (say `≥` on a cross pairing) yields a polyhedron that does not contain `x·y`, so the relaxation is
/// "legal" and useless.**
///
/// **The envelope is not an equality**: **`w` equals `x·y` only when `x` or `y` sits on a bound; inside the box it is
/// slack.** **So it is a relaxation, never a way to "compute the product".**
/// </remarks>
public sealed class McCormickEnvelopeFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly RealVar _resultVar;

    /// <summary>构造一个 McCormick 包络 / Build one.</summary>
    public McCormickEnvelopeFunction(
        LinearPolynomial<V> left,
        LinearPolynomial<V> right,
        ConditionBounds<V> leftBounds,
        ConditionBounds<V> rightBounds,
        IIntoValue<V> converter,
        string name = "mccormick",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(leftBounds);
        ArgumentNullException.ThrowIfNull(rightBounds);
        ArgumentNullException.ThrowIfNull(converter);

        Left = left;
        Right = right;
        LeftBounds = leftBounds;
        RightBounds = rightBounds;
        Converter = converter;
        Name = name;
        DisplayName = displayName;

        _resultVar = new RealVar($"{name}_mccormick");
        ResultBounds = CornerProductBounds();
        _resultVar.Range.Geq(converter.FromValue(ResultBounds.Lower));
        _resultVar.Range.Leq(converter.FromValue(ResultBounds.Upper));
    }

    /// <summary>左侧因子 / The left factor.</summary>
    public LinearPolynomial<V> Left { get; }

    /// <summary>右侧因子 / The right factor.</summary>
    public LinearPolynomial<V> Right { get; }

    /// <summary>左侧因子的区间 / The left factor's interval.</summary>
    public ConditionBounds<V> LeftBounds { get; }

    /// <summary>右侧因子的区间 / The right factor's interval.</summary>
    public ConditionBounds<V> RightBounds { get; }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>
    /// 四个角乘积的包围区间 / The span of the four corner products.
    /// </summary>
    /// <remarks>
    /// **它只是 `w` 的可行范围，不是 `x·y` 的精确范围**——
    /// **精确范围与角乘积的最小最大值相同（双线性函数在矩形上的极值必在角上取到），**
    /// **但这不等于"每个中间值都能被 `w` 取到"**：**包络内部是松弛的。**
    ///
    /// **It is only the feasible range of `w`, not the exact range of `x·y`** —
    /// **the exact range does coincide with the min and max over corners (a bilinear function on a rectangle attains its
    /// extrema at corners),** **but that does not mean every value in between is attainable by `w`**: **the interior of
    /// the envelope is slack.**
    /// </remarks>
    public ConditionBounds<V> ResultBounds { get; }

    /// <summary>结果辅助变量 / The result auxiliary variable.</summary>
    public RealVar ResultVar => _resultVar;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => new IVariableItem[] { _resultVar };

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(new[] { Left, Right });

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(Converter.One, _resultVar) },
        Converter.Zero);

    /// <summary>
    /// 四条包络边 / The four envelope edges.
    /// </summary>
    /// <remarks>
    /// **顺序固定为下左、下右、上左、上右**，**因为判据要逐条核对系数，而顺序不稳会让核对变成"找到就行"。**
    ///
    /// **The order is fixed at lower-left, lower-right, upper-left, upper-right**, **because the criteria check the
    /// coefficients row by row, and an unstable order would turn that into "find it anywhere".**
    /// </remarks>
    public IReadOnlyList<LinearInequality<V>> EnvelopeRows() {
        V xL = LeftBounds.Lower;
        V xU = LeftBounds.Upper;
        V yL = RightBounds.Lower;
        V yU = RightBounds.Upper;

        return new[] {
            EnvelopeRow(yL, xL, xL.Times(yL), Comparison.GE, "lower_left"),
            EnvelopeRow(yU, xU, xU.Times(yU), Comparison.GE, "lower_right"),
            EnvelopeRow(yL, xU, xU.Times(yL), Comparison.LE, "upper_left"),
            EnvelopeRow(yU, xL, xL.Times(yU), Comparison.LE, "upper_right"),
        };
    }

    /// <summary>
    /// 乘积本身（不经过包络）/ The product itself, bypassing the envelope.
    /// </summary>
    /// <remarks>
    /// **包络只能给松弛值，所以"乘积是多少"必须单独算。**
    /// **越界的输入返回 `null` 而不是"夹到端点"**：**夹到端点会给出一个看起来合法的数，
    /// 而那个数对应的是另一个输入。**
    ///
    /// **The envelope only yields a relaxation, so "what the product is" is computed separately.**
    /// **An out-of-range input returns `null` rather than being clamped**: **clamping yields a plausible-looking number
    /// that corresponds to a different input.**
    /// </remarks>
    public V? EvaluateProduct(IReadOnlyDictionary<ISymbol, V> values) {
        V? leftValue = FunctionEvaluation.Polynomial(Left, values).Value;
        if (leftValue is not { } x || !LeftBounds.Contains(x)) {
            return null;
        }

        V? rightValue = FunctionEvaluation.Polynomial(Right, values).Value;
        if (rightValue is not { } y || !RightBounds.Contains(y)) {
            return null;
        }

        return x.Times(y);
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        V? relaxed = values.TryGetValue(_resultVar, out V stored) ? stored : null;
        if (relaxed is not { } w || !ResultBounds.Contains(w)) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        return new Ok<V?, ErrorCode, Error<ErrorCode>>(w);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        var constraints = new List<LinearInequality<V>> {
            // 两侧因子的区间本身也要进模型——否则求解器不知道 x、y 被夹在哪。
            // The factors' own intervals go into the model too, or the solver does not know where x and y are held.
            new(
                Lhs: Left,
                Rhs: Constant(LeftBounds.Upper),
                Comparison: Comparison.LE,
                Name: $"{Name}_left_upper"),
            new(
                Lhs: Left,
                Rhs: Constant(LeftBounds.Lower),
                Comparison: Comparison.GE,
                Name: $"{Name}_left_lower"),
            new(
                Lhs: Right,
                Rhs: Constant(RightBounds.Upper),
                Comparison: Comparison.LE,
                Name: $"{Name}_right_upper"),
            new(
                Lhs: Right,
                Rhs: Constant(RightBounds.Lower),
                Comparison: Comparison.GE,
                Name: $"{Name}_right_lower"),
        };

        constraints.AddRange(EnvelopeRows());
        return IndicatorSupport.AddConstraints(model, constraints);
    }

    /// <summary>造一条包络边 / Build one envelope edge.</summary>
    private LinearInequality<V> EnvelopeRow(V leftScale, V rightScale, V cornerProduct, Comparison comparison, string suffix) {
        var terms = new List<LinearMonomial<V>> {
            new(Converter.One, _resultVar),
        };

        // 行 = w − leftScale·left − rightScale·right。
        // 这里用 monomials + 显式常数，而不是"整条多项式乘系数"——与基线的构造一致。
        //
        // Row = w - leftScale*left - rightScale*right, using monomials plus an explicit constant rather than scaling the
        // whole polynomial, matching the baseline's construction.
        foreach (LinearMonomial<V> monomial in Left.Monomials) {
            terms.Add(new LinearMonomial<V>(leftScale.Times(monomial.Coefficient).Negate(), monomial.Symbol));
        }

        foreach (LinearMonomial<V> monomial in Right.Monomials) {
            terms.Add(new LinearMonomial<V>(rightScale.Times(monomial.Coefficient).Negate(), monomial.Symbol));
        }

        V constant = leftScale.Times(Left.Constant).Plus(rightScale.Times(Right.Constant)).Negate();

        return new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(terms, constant),
            Rhs: Constant(cornerProduct.Negate()),
            Comparison: comparison,
            Name: $"{Name}_{suffix}");
    }

    /// <summary>四个角乘积的最小最大值 / The min and max over the four corner products.</summary>
    private ConditionBounds<V> CornerProductBounds() {
        V[] products = {
            LeftBounds.Lower.Times(RightBounds.Lower),
            LeftBounds.Lower.Times(RightBounds.Upper),
            LeftBounds.Upper.Times(RightBounds.Lower),
            LeftBounds.Upper.Times(RightBounds.Upper),
        };

        V lower = products[0];
        V upper = products[0];
        for (int index = 1; index < products.Length; ++index) {
            if (products[index].CompareTo(lower) < 0) {
                lower = products[index];
            }

            if (products[index].CompareTo(upper) > 0) {
                upper = products[index];
            }
        }

        return new ConditionBounds<V>(lower, upper);
    }

    private LinearPolynomial<V> Constant(V value) =>
        new(Array.Empty<LinearMonomial<V>>(), value);
}
