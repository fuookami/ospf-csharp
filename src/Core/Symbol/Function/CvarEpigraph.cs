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
/// CVaR 的上镜图：<c>θ + Σ pᵢ·uᵢ/(1−α)</c>，约束 <c>uᵢ ≥ max(0, Lᵢ − θ)</c> /
/// The CVaR epigraph: `θ + Σ pᵢ·uᵢ/(1−α)` with `uᵢ ≥ max(0, Lᵢ − θ)`.
/// </summary>
/// <remarks>
/// B1160 按基线 `Cvar.kt` 的 `CvarEpigraphFunction` 补。
///
/// **与 `CvarFunction` 的区别就是这一批的全部内容**：
///
/// | | `CvarFunction` | `CvarEpigraphFunction` |
/// | --- | --- | --- |
/// | 结果 | 已经是最小值 | **一个含 `θ` 的上界式** |
/// | 辅助量 | 每对情景一个 `MaxFunction` | **一个 `θ` 加每个情景一个 `uᵢ`** |
/// | 求值 | CVaR 本身 | CVaR 本身（同一个式子） |
/// | 约束 | 每个 `MaxFunction` 自己那套 | **只有 `Lᵢ − θ − uᵢ ≤ 0`** |
///
/// **关键性质：`ResultPolynomial` 在任意 `θ` 处给的是上界，不是 CVaR。**
/// **它只在最优阈值处取等号，其余处严格大于。**
/// **所以"把 `ResultPolynomial` 当 CVaR 用"是错的**——
/// **它必须进目标函数被最小化，`θ` 才会被推到最优处。**
/// **判据必须验证这一点：在非最优阈值处，多项式值严格大于 `Evaluate` 的值。**
///
/// **`uᵢ` 是 `URealVar`（非负）**：**`uᵢ ≥ 0` 由变量范围承担，而不是再写一条行**——
/// **少一条行，语义完全一样。**
///
/// **`θ` 的范围取所有损失界的并**：**窄了会把最优阈值排除在外，
/// 于是最优解落在边界上、结果偏大，而模型仍然可行。**
///
/// B1160, following the baseline's `CvarEpigraphFunction`.
///
/// **The difference from `CvarFunction` is the whole content of this batch**:
///
/// | | `CvarFunction` | `CvarEpigraphFunction` |
/// | --- | --- | --- |
/// | Result | already the minimum | **an upper bound containing `θ`** |
/// | Helpers | one `MaxFunction` per pair | **one `θ` plus one `uᵢ` per scenario** |
/// | Evaluation | CVaR itself | CVaR itself (same expression) |
/// | Rows | each `MaxFunction` brings its own | **only `Lᵢ − θ − uᵢ ≤ 0`** |
///
/// **The key property: `ResultPolynomial` gives an upper bound at any `θ`, not the CVaR.**
/// **It equals the CVaR only at the optimal threshold and is strictly greater elsewhere.**
/// **So "use `ResultPolynomial` as the CVaR" is wrong** —
/// **it has to enter the objective and be minimised for `θ` to reach the optimum.**
/// **The criterion must check that: at a non-optimal threshold the polynomial strictly exceeds `Evaluate`.**
///
/// **`uᵢ` is a `URealVar` (non-negative)**: **`uᵢ ≥ 0` comes from the variable's range rather than another row** —
/// **one row fewer, same meaning.**
///
/// **`θ` ranges over the union of every loss interval**: **narrower would exclude the optimal threshold, so the
/// optimum lands on a boundary, the result is too large and the model stays feasible.**
/// </remarks>
public sealed class CvarEpigraphFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;

    /// <summary>创建 CVaR 上镜图 / Create a CVaR epigraph.</summary>
    public CvarEpigraphFunction(
        IReadOnlyList<LinearPolynomial<V>> losses,
        IReadOnlyList<V> probabilities,
        V alpha,
        IIntoValue<V> converter,
        string name = "cvar_epigraph",
        string? displayName = null) {
        if (losses is null || losses.Count == 0) {
            throw new ArgumentException("a CVaR epigraph needs at least one scenario", nameof(losses));
        }

        if (probabilities is null || probabilities.Count != losses.Count) {
            throw new ArgumentException(
                "a CVaR epigraph needs one probability per loss", nameof(probabilities));
        }

        _converter = converter ?? throw new ArgumentNullException(nameof(converter));

        // **`alpha = 1` 时 1−α 为零**：**除法不抛异常，而是给出 inf/NaN 并悄悄污染目标函数。**
        //
        // **At `alpha = 1` the denominator vanishes**: **the division does not throw — it yields inf/NaN and quietly
        // poisons the objective.**
        if (alpha.CompareTo(converter.Zero) < 0 || alpha.CompareTo(converter.One) >= 0) {
            throw new ArgumentException($"a CVaR epigraph needs alpha in [0, 1) (alpha={alpha})", nameof(alpha));
        }

        foreach (V probability in probabilities) {
            if (probability.CompareTo(converter.Zero) < 0) {
                throw new ArgumentException("CVaR epigraph probabilities must be non-negative", nameof(probabilities));
            }
        }

        V total = converter.Zero;
        foreach (V probability in probabilities) {
            total = total.Plus(probability);
        }

        if (total.CompareTo(converter.One) != 0) {
            throw new ArgumentException(
                $"CVaR epigraph probabilities must sum to one (sum={total})", nameof(probabilities));
        }

        Losses = losses.ToArray();
        Probabilities = probabilities.ToArray();
        Alpha = alpha;
        Name = name;
        DisplayName = displayName;

        var bounds = new ConditionBounds<V>[losses.Count];
        V globalLower = converter.Zero;
        V globalUpper = converter.Zero;
        bool first = true;
        for (int index = 0; index < losses.Count; ++index) {
            bounds[index] = PolynomialBounds.FiniteBoundsOf(losses[index], converter)
                ?? throw new ArgumentException(
                    $"scenario {index} needs finite bounds: bound its variables", nameof(losses));

            if (first) {
                globalLower = bounds[index].Lower;
                globalUpper = bounds[index].Upper;
                first = false;
                continue;
            }

            if (bounds[index].Lower.CompareTo(globalLower) < 0) {
                globalLower = bounds[index].Lower;
            }

            if (bounds[index].Upper.CompareTo(globalUpper) > 0) {
                globalUpper = bounds[index].Upper;
            }
        }

        ThresholdVar = new RealVar($"{name}_threshold");
        ThresholdVar.Range.Geq(converter.FromValue(globalLower));
        ThresholdVar.Range.Leq(converter.FromValue(globalUpper));

        var excessVars = new URealVar[losses.Count];
        for (int index = 0; index < losses.Count; ++index) {
            excessVars[index] = new URealVar($"{name}_excess_{index}");

            // **`uᵢ` 的上界是 `Lᵢ.upper − globalLower`**：**它最多是"该情景最高"减去"全局最低"。
            // 用 `globalUpper` 代替只会让松弛变差。**
            //
            // **`uᵢ` is bounded by `Lᵢ.upper − globalLower`**: **the most it can ever exceed. Using `globalUpper`
            // instead only weakens the relaxation.**
            V excessUpper = bounds[index].Upper.Minus(globalLower);
            if (excessUpper.CompareTo(converter.Zero) > 0) {
                excessVars[index].Range.Leq(converter.FromValue(excessUpper));
            }
        }

        ExcessVars = excessVars;
    }

    /// <summary>各情景的损失 / The per-scenario losses.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Losses { get; }

    /// <summary>各情景的概率 / The per-scenario probabilities.</summary>
    public IReadOnlyList<V> Probabilities { get; }

    /// <summary>置信水平 / The confidence level.</summary>
    public V Alpha { get; }

    /// <summary>阈值变量 / The threshold variable.</summary>
    public RealVar ThresholdVar { get; }

    /// <summary>各情景的超出量 / The per-scenario excess.</summary>
    public IReadOnlyList<URealVar> ExcessVars { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem> { ThresholdVar };
            foreach (URealVar excess in ExcessVars) {
                helpers.Add(excess);
            }

            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Losses);

    /// <summary>
    /// 上界式 <c>θ + Σ pᵢ·uᵢ/(1−α)</c> / The upper bound `θ + Σ pᵢ·uᵢ/(1−α)`.
    /// </summary>
    /// <remarks>
    /// **这不是 CVaR，而是它的一个上界**——**只在最优阈值处取等号。**
    ///
    /// **This is not the CVaR but an upper bound on it** — **equality holds only at the optimal threshold.**
    /// </remarks>
    public LinearPolynomial<V> ResultPolynomial {
        get {
            V denominator = _converter.One.Minus(Alpha);
            var terms = new List<LinearMonomial<V>> {
                new(_converter.One, ThresholdVar),
            };

            for (int index = 0; index < ExcessVars.Count; ++index) {
                terms.Add(new LinearMonomial<V>(Probabilities[index].Div(denominator), ExcessVars[index]));
            }

            return new LinearPolynomial<V>(terms, _converter.Zero);
        }
    }

    /// <summary>每个情景一条 <c>Lᵢ − θ − uᵢ ≤ 0</c> / One row per scenario.</summary>
    /// <remarks>
    /// **只有这一组行**：**`uᵢ ≥ 0` 由 `URealVar` 的范围承担，`uᵢ ≥ Lᵢ − θ` 由这条行承担，
    /// 于是 `uᵢ = max(0, Lᵢ − θ)`。**
    /// **两条合起来才是 `max`，少任何一条都让 `uᵢ` 可以取大（目标偏大）或取小（约束被违反）。**
    ///
    /// **Only this group**: **`uᵢ ≥ 0` comes from the `URealVar` range and `uᵢ ≥ Lᵢ − θ` from this row, so
    /// `uᵢ = max(0, Lᵢ − θ)`.** **Both together make the `max`; dropping either lets `uᵢ` be too large (objective too
    /// big) or too small (constraint violated).**
    /// </remarks>
    public IReadOnlyList<LinearInequality<V>> ExcessRows() {
        var rows = new LinearInequality<V>[Losses.Count];
        for (int index = 0; index < Losses.Count; ++index) {
            LinearPolynomial<V> loss = Losses[index];
            var terms = new List<LinearMonomial<V>>(loss.Monomials.Count + 2);
            foreach (LinearMonomial<V> monomial in loss.Monomials) {
                terms.Add(monomial);
            }

            terms.Add(new LinearMonomial<V>(_converter.One.Negate(), ThresholdVar));
            terms.Add(new LinearMonomial<V>(_converter.One.Negate(), ExcessVars[index]));

            rows[index] = new LinearInequality<V>(
                Lhs: new LinearPolynomial<V>(terms, loss.Constant),
                Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _converter.Zero),
                Comparison: Comparison.LE,
                Name: $"{Name}_excess_lb_{index}");
        }

        return rows;
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) {
        var evaluated = new List<V>(Losses.Count);
        foreach (LinearPolynomial<V> loss in Losses) {
            if (FunctionEvaluation.Polynomial(loss, values).Value is not { } value) {
                return null;
            }

            evaluated.Add(value);
        }

        return CvarValue(evaluated);
    }

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        new Ok<V?, ErrorCode, Error<ErrorCode>>(Evaluate(values));

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) =>
        IndicatorSupport.AddConstraints(model, ExcessRows());

    /// <summary>
    /// 对给定的一组损失值求 CVaR / Compute the CVaR for a concrete set of losses.
    /// </summary>
    /// <remarks>
    /// **与 `CvarFunction` 里那段逐字相同**：**两者的语义定义是同一个式子，区别只在编码方式**
    /// （**一个用 `MinFunction` 展开，一个用 `θ` 加非负超出量**）。
    ///
    /// **Verbatim the same as the block in `CvarFunction`**: **both share one semantic definition; only the encoding
    /// differs** (**one expands through `MinFunction`, the other through a `θ` plus non-negative excesses**).
    /// </remarks>
    private V CvarValue(IReadOnlyList<V> losses) {
        V denominator = _converter.One.Minus(Alpha);
        V? minimum = null;

        foreach (V threshold in losses) {
            V candidate = threshold;
            for (int index = 0; index < losses.Count; ++index) {
                V excess = losses[index].Minus(threshold);
                if (excess.CompareTo(_converter.Zero) > 0) {
                    candidate = candidate.Plus(Probabilities[index].Times(excess).Div(denominator));
                }
            }

            if (minimum is null || candidate.CompareTo(minimum.Value) < 0) {
                minimum = candidate;
            }
        }

        return minimum!.Value;
    }
}
