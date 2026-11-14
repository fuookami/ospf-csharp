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
/// 松弛变量函数 / Slack variable function
/// </summary>
/// <remarks>
/// 为 `x ? y` 引入正/负松弛，使 `x + neg - pos = y`。三种形状：
/// <list type="number">
/// <item>**两个方向都开** → 精确的四行绝对值公式（`result = |x - y|`）。只写下界两行时，
///   求解器可以把 `neg` 与 `pos` 一起抬高而仍然满足全部约束，事后读出的松弛值是**假的**；</item>
/// <item>**只有一个方向且非阈值模式** → 一行等式 `x + neg - pos = y`；</item>
/// <item>**阈值模式** → 不加等式，只加 `x + neg &gt;= y` / `x - pos &lt;= y`。</item>
/// </list>
///
/// Introduces positive/negative slack for `x ? y` so that `x + neg - pos = y`. Three shapes: **both directions open** → the exact four-row absolute-value formulation
/// (`result = |x - y|`), because with only the two lower rows the solver can raise `neg` and `pos` together while satisfying everything, and the slack read back afterwards is
/// **fake**; **one direction, non-threshold** → a single equality; **threshold mode** → no equality, just the two one-sided rows.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class SlackFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly Lazy<URealVar?> _negVar;
    private readonly Lazy<URealVar?> _posVar;
    private readonly Lazy<BinVar?> _sideVar;

    /// <summary>左侧多项式 / Left-hand polynomial.</summary>
    public LinearPolynomial<V> X { get; }
    /// <summary>右侧多项式 / Right-hand polynomial.</summary>
    public LinearPolynomial<V> Y { get; }
    /// <summary>是否包含负松弛 / Whether negative slack is included.</summary>
    public bool WithNegative { get; }
    /// <summary>是否包含正松弛 / Whether positive slack is included.</summary>
    public bool WithPositive { get; }
    /// <summary>是否阈值模式 / Whether threshold mode is used.</summary>
    public bool Threshold { get; }
    /// <summary>是否添加约束 / Whether constraints are added.</summary>
    public bool Constraint { get; }

    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <summary>负松弛变量 / Negative slack variable.</summary>
    public URealVar? NegVar => _negVar.Value;
    /// <summary>正松弛变量 / Positive slack variable.</summary>
    public URealVar? PosVar => _posVar.Value;
    /// <summary>绝对值分支变量 / Absolute-value branch variable.</summary>
    public BinVar? SideVar => _sideVar.Value;

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => new IVariableItem?[] { NegVar, PosVar, SideVar }
        .Where(item => item is not null)
        .Select(item => (IVariableItem)item!)
        .ToArray();

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(new[] { X, Y });

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        new LinearMonomial<V>?[] {
            WithNegative && NegVar is not null ? new LinearMonomial<V>(_converter.One, NegVar) : null,
            WithPositive && PosVar is not null ? new LinearMonomial<V>(_converter.One, PosVar) : null,
        }.Where(monomial => monomial is not null).Select(monomial => monomial!).ToArray(),
        _converter.Zero
    );

    /// <summary>
    /// 创建松弛变量函数 / Create a slack variable function.
    /// </summary>
    /// <param name="x">左侧多项式 / Left-hand polynomial.</param>
    /// <param name="y">右侧多项式 / Right-hand polynomial.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="withNegative">是否包含负松弛 / Whether negative slack is included.</param>
    /// <param name="withPositive">是否包含正松弛 / Whether positive slack is included.</param>
    /// <param name="threshold">是否阈值模式 / Whether threshold mode is used.</param>
    /// <param name="constraint">是否添加约束 / Whether constraints are added.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public SlackFunction(
        LinearPolynomial<V> x,
        LinearPolynomial<V> y,
        IIntoValue<V> converter,
        bool withNegative = true,
        bool withPositive = true,
        bool threshold = false,
        bool constraint = true,
        string name = "slack",
        string? displayName = null) {
        if (x is null) {
            throw new ArgumentNullException(nameof(x));
        }
        if (y is null) {
            throw new ArgumentNullException(nameof(y));
        }
        if (converter is null) {
            throw new ArgumentNullException(nameof(converter));
        }
        // 基线 `require(withNegative || withPositive)`：两个方向都关掉的"松弛"是**恒等于零**的假符号，
        // 却仍然返回 `Ok`——调用方以为拿到了一条松弛。
        // The baseline's `require(withNegative || withPositive)`: a "slack" with both directions off is a **constant zero** fake symbol that still returns `Ok` — the caller believes it
        // got a slack.
        if (!withNegative && !withPositive) {
            throw new ArgumentException(
                "松弛必须至少包含一个方向 / a slack must include at least one direction", nameof(withNegative));
        }
        X = new LinearPolynomial<V>(Array.AsReadOnly(x.Monomials.ToArray()), x.Constant);
        Y = new LinearPolynomial<V>(Array.AsReadOnly(y.Monomials.ToArray()), y.Constant);
        _converter = converter;
        WithNegative = withNegative;
        WithPositive = withPositive;
        Threshold = threshold;
        Constraint = constraint;
        Name = name;
        DisplayName = displayName;

        // shortcut: 整数松弛仍用 `URealVar` 承载——`UIntVar` 的缺省上界是 `ulong.MaxValue`（1.8e19，
        // 超过 binary64 精确整数上限 2^53），求解器在注册阶段就会拒；离散时间的松弛路径目前也没有调用方。
        // shortcut: integer slacks still ride on `URealVar` — `UIntVar`'s default upper bound is `ulong.MaxValue` (1.8e19, beyond binary64's exact-integer limit of 2^53) and is
        // rejected at registration; the discrete-time slack path has no caller yet either.
        _negVar = new Lazy<URealVar?>(() => WithNegative ? new URealVar(Name + "_neg") : null);
        _posVar = new Lazy<URealVar?>(() => WithPositive ? new URealVar(Name + "_pos") : null);
        _sideVar = new Lazy<BinVar?>(() => WithNegative && WithPositive ? new BinVar(Name + "_side") : null);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 两个方向都开 → `|x - y|`；只有负 → `max(y - x, 0)`；只有正 → `max(x - y, 0)`。
    /// 三个分支的**方向**都能写反，而写反之后求值仍然是个非负数——只检查"非负"发现不了。
    ///
    /// Both open → `|x - y|`; negative only → `max(y - x, 0)`; positive only → `max(x - y, 0)`. Each branch's **direction** can be inverted, and an inverted branch still evaluates
    /// to a non-negative number — checking "non-negative" alone catches nothing.
    /// </remarks>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        Result<V?, ErrorCode, Error<ErrorCode>> left = FunctionEvaluation.Polynomial(X, values);
        if (left.IsFailed) {
            return left;
        }
        Result<V?, ErrorCode, Error<ErrorCode>> right = FunctionEvaluation.Polynomial(Y, values);
        if (right.IsFailed) {
            return right;
        }
        if (left.Value is not { } x || right.Value is not { } y) {
            return Results.Ok<V?>(default(V?));
        }
        return FunctionEvaluation.Difference(x, y)
            .Map(difference => difference is { } diff ? SlackOf(diff) : default(V?));
    }

    private V? SlackOf(V difference) {
        if (WithNegative && WithPositive) {
            return difference.Abs();
        }
        if (WithNegative) {
            return difference.Ls(_converter.Zero) ? difference.Negate() : _converter.Zero;
        }
        return difference.Gr(_converter.Zero) ? difference : _converter.Zero;
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (model is null) {
            throw new ArgumentNullException(nameof(model));
        }
        if (!Constraint) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        var zero = new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _converter.Zero);
        IReadOnlyList<LinearInequality<V>> constraints;

        if (WithNegative && WithPositive && NegVar is not null && PosVar is not null && SideVar is not null) {
            LinearPolynomial<V> difference = Difference(X, Y);
            LinearPolynomial<V> result = ResultPolynomial;
            LinearPolynomial<V> resultMinusDifference = Difference(result, difference);
            LinearPolynomial<V> resultPlusDifference = Sum(result, difference);
            // 未激活的分支可能暴露两倍绝对差值，因此 M 必须覆盖 `2 * max|x - y|`，不能只取 `max|x - y|`。
            // Each inactive branch can expose twice the absolute difference, so M must cover `2 * max|x - y|`, not only `max|x - y|`.
            if (BigMValues.InferredBigM(difference, _converter) is not { } magnitude) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "松弛的绝对值公式无法推导 Big-M。 / The Big-M of the slack's absolute-value formulation cannot be inferred."));
            }
            V bigM = magnitude.Times(_converter.One.Plus(_converter.One));
            constraints = new[] {
                new LinearInequality<V>(resultMinusDifference, zero, Comparison.GE, Name + "_abs_ge_difference"),
                new LinearInequality<V>(resultPlusDifference, zero, Comparison.GE, Name + "_abs_ge_negative_difference"),
                new LinearInequality<V>(
                    Append(resultMinusDifference, new LinearMonomial<V>(bigM, SideVar)),
                    new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), bigM),
                    Comparison.LE,
                    Name + "_abs_branch_positive"),
                new LinearInequality<V>(
                    Append(resultPlusDifference, new LinearMonomial<V>(bigM.Negate(), SideVar)),
                    zero,
                    Comparison.LE,
                    Name + "_abs_branch_negative"),
            };
        } else if (!Threshold) {
            constraints = new[] {
                new LinearInequality<V>(SignedLeft(), Y, Comparison.EQ, Name),
            };
        } else {
            var rows = new List<LinearInequality<V>>();
            if (WithNegative && NegVar is not null) {
                rows.Add(new LinearInequality<V>(
                    Append(X, new LinearMonomial<V>(_converter.One, NegVar)), Y, Comparison.GE, Name + "_neg"));
            }
            if (WithPositive && PosVar is not null) {
                rows.Add(new LinearInequality<V>(
                    Append(X, new LinearMonomial<V>(_converter.One.Negate(), PosVar)), Y, Comparison.LE, Name + "_pos"));
            }
            constraints = rows;
        }

        return IndicatorSupport.AddConstraints(model, constraints);
    }

    /// <summary>带符号的左侧 `x + neg - pos` / The signed left-hand side `x + neg - pos`.</summary>
    public LinearPolynomial<V> SignedLeft() {
        LinearPolynomial<V> result = X;
        if (WithNegative && NegVar is not null) {
            result = Append(result, new LinearMonomial<V>(_converter.One, NegVar));
        }
        if (WithPositive && PosVar is not null) {
            result = Append(result, new LinearMonomial<V>(_converter.One.Negate(), PosVar));
        }
        return result;
    }

    private static LinearPolynomial<V> Append(LinearPolynomial<V> polynomial, LinearMonomial<V> monomial) =>
        new(polynomial.Monomials.Append(monomial).ToArray(), polynomial.Constant);

    private static LinearPolynomial<V> Sum(LinearPolynomial<V> first, LinearPolynomial<V> second) =>
        new(first.Monomials.Concat(second.Monomials).ToArray(), first.Constant.Plus(second.Constant));

    private static LinearPolynomial<V> Difference(LinearPolynomial<V> first, LinearPolynomial<V> second) =>
        new(
            first.Monomials
                .Concat(second.Monomials.Select(monomial => new LinearMonomial<V>(monomial.Coefficient.Negate(), monomial.Symbol)))
                .ToArray(),
            first.Constant.Minus(second.Constant));
}
