#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
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
/// 累进费率：每段用各自的边际费率，费用<b>连续</b> / An incremental tariff: a marginal rate per segment, and the cost is continuous.
/// </summary>
/// <remarks>
/// 对齐基线 `Tariff.kt` 的 `IncrementalTariff`。
///
/// <c>cost(b₀) = baseCost</c>，然后 <c>cost(bᵢ₊₁) = cost(bᵢ) + rateᵢ · (bᵢ₊₁ − bᵢ)</c>。
/// **在断点处是连续的**——**这就是"累进"的含义：越过一个断点，只有断点之后的那部分享受新费率。**
///
/// **与 `AllUnitsDiscountFunction` 的差别正是这一点**：
/// **这里的费用在断点处连续，而全量折扣在那里会<b>跳降</b>。**
/// **两者用同一组断点与费率时在第一个区间内完全相同，所以只测小值的判据分不开它们。**
///
/// B1155, following the baseline's `IncrementalTariff`.
///
/// `cost(b₀) = baseCost`, then `cost(bᵢ₊₁) = cost(bᵢ) + rateᵢ · (bᵢ₊₁ − bᵢ)`.
/// **It is continuous at every breakpoint** — **that is what "incremental" means: past a breakpoint only the portion
/// above it enjoys the new rate.**
///
/// **That is exactly the difference from `AllUnitsDiscountFunction`**:
/// **the cost here is continuous at breakpoints, while an all-units discount <b>jumps down</b> there.**
/// **Given the same breakpoints and rates the two agree on the first segment, so a criterion that only tests small
/// values cannot separate them.**
/// </remarks>
public static class IncrementalTariff
{
    /// <summary>造一个累进费率的分段线性函数 / Build the piecewise function.</summary>
    public static Result<UnivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>> Create<V>(
        LinearPolynomial<V> x,
        IReadOnlyList<V> breakpoints,
        IReadOnlyList<V> marginalRates,
        IIntoValue<V> converter,
        V? baseCost = null,
        string name = "incremental_tariff",
        string? displayName = null) where V : struct, IRealNumber<V>, INumberField<V>, IFloatingNumber<V> {
        if (x is null || converter is null || breakpoints is null || marginalRates is null || string.IsNullOrWhiteSpace(name)) {
            return Invalid<V>("an incremental tariff needs an input, breakpoints, rates, a converter and a name");
        }

        // 至少两个断点，且费率数 = 区间数。
        if (breakpoints.Count < 2) {
            return Invalid<V>($"an incremental tariff needs at least two breakpoints (got {breakpoints.Count})");
        }

        if (marginalRates.Count != breakpoints.Count - 1) {
            return Invalid<V>(
                $"one rate per interval: {breakpoints.Count} breakpoints need {breakpoints.Count - 1} rates, " +
                $"got {marginalRates.Count}");
        }

        // **断点必须严格递增**：**相等或倒序的断点会让"第 i 段"没有意义，
        // 而分段函数仍然能构造出来（区间宽度为负时斜率变成反的）。**
        for (int index = 0; index + 1 < breakpoints.Count; ++index) {
            if (breakpoints[index].CompareTo(breakpoints[index + 1]) >= 0) {
                return Invalid<V>(
                    $"breakpoints must strictly increase: b{index} = {breakpoints[index]} >= b{index + 1} = " +
                    $"{breakpoints[index + 1]}");
            }
        }

        // **费率为负会让"买得越多花得越少"**——**那不是折扣，那是套利，
        // 而分段函数照样构造成功、求解器照样给出"更优"的解。**
        foreach (V rate in marginalRates) {
            if (rate.CompareTo(converter.Zero) < 0) {
                return Invalid<V>($"marginal rates must be non-negative, got {rate}");
            }
        }

        V cost = baseCost ?? converter.Zero;
        var points = new List<Point<Dim2, V>>(breakpoints.Count) {
            Point<Dim2, V>.Create(breakpoints[0], cost),
        };

        for (int index = 0; index < marginalRates.Count; ++index) {
            V width = breakpoints[index + 1].Minus(breakpoints[index]);
            cost = cost.Plus(marginalRates[index].Times(width));
            if (!FunctionEvaluation.IsUsable(cost)) {
                return Invalid<V>($"the cumulative cost became unusable at breakpoint {index + 1}");
            }

            points.Add(Point<Dim2, V>.Create(breakpoints[index + 1], cost));
        }

        return UnivariateLinearPiecewiseFunction.FromPoints<V>(
            input: x,
            points: points,
            converter: converter,
            name: name,
            displayName: displayName);
    }

    private static Result<UnivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>> Invalid<V>(string message)
        where V : struct, IFloatingNumber<V> =>
        new Failed<UnivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, message);
}

/// <summary>
/// 全量折扣：<b>整批数量</b>适用同一档位的单价 / An all-units discount: one unit price for the whole quantity.
/// </summary>
/// <remarks>
/// 对齐基线 `Tariff.kt` 的 `AllUnitsDiscountFunction`。
///
/// <c>cost = quantity · rate(tier)</c>——**单价按整批数量选，所以费用对数量是线性的，但斜率在断点处改变。**
///
/// **关键性质：费用在断点处<b>跳降</b>，不连续。**
/// **买 99 件按高档价、买 100 件时整批按低档价——于是 100 件的总价可能低于 99 件。**
/// **这不是 bug，而是"全量折扣"的定义；而它与 `IncrementalTariff` 在第一个区间内完全相同。**
/// **所以要分开两者，判据必须跨越一个断点比较"跳出前后"。**
///
/// **`boundaryGap` 让档位区间在断点处收半开**：**没有它，断点同时属于两个档位，
/// 而 one-hot 约束不禁止两个 selector 同时为 1，于是 `result = Σ masked` 会把两个档位加起来。**
///
/// B1155, following the baseline's `AllUnitsDiscountFunction`.
///
/// `cost = quantity · rate(tier)` — **the unit price is picked from the whole quantity, so the cost is linear in the
/// quantity with a slope change at each breakpoint.**
///
/// **The key property: the cost <b>jumps down</b> at a breakpoint; it is not continuous.**
/// **Ninety-nine units pay the higher price and a hundred units pay the lower price on all hundred — so a hundred can
/// cost less than ninety-nine.**
/// **That is not a bug but the definition of "all units"; and it agrees with `IncrementalTariff` on the first
/// segment.** **So separating the two requires a criterion that crosses a breakpoint.**
///
/// **`boundaryGap` makes the tier intervals half-open at breakpoints**: **without it a breakpoint belongs to two
/// tiers, and the one-hot constraint does not forbid both selectors, so `result = Σ masked` would add both tiers.**
/// </remarks>
public sealed class AllUnitsDiscountFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly RealVar _resultVar;
    private readonly BinVar[] _selectorVars;
    private readonly MaskingFunction<V>[] _masking;

    /// <summary>构造一个全量折扣 / Build one.</summary>
    public AllUnitsDiscountFunction(
        LinearPolynomial<V> x,
        IReadOnlyList<V> breakpoints,
        IReadOnlyList<V> rates,
        IIntoValue<V> converter,
        V? boundaryGap = null,
        string name = "all_units_discount",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(breakpoints);
        ArgumentNullException.ThrowIfNull(rates);
        ArgumentNullException.ThrowIfNull(converter);

        if (breakpoints.Count < 2) {
            throw new ArgumentException(
                $"an all-units discount needs at least two breakpoints (got {breakpoints.Count})",
                nameof(breakpoints));
        }

        if (rates.Count != breakpoints.Count - 1) {
            throw new ArgumentException(
                $"one rate per tier: {breakpoints.Count} breakpoints give {breakpoints.Count - 1} tiers, " +
                $"got {rates.Count}",
                nameof(rates));
        }

        for (int index = 0; index + 1 < breakpoints.Count; ++index) {
            if (breakpoints[index].CompareTo(breakpoints[index + 1]) >= 0) {
                throw new ArgumentException(
                    $"breakpoints must strictly increase: b{index} = {breakpoints[index]} >= b{index + 1} = " +
                    $"{breakpoints[index + 1]}",
                    nameof(breakpoints));
            }
        }

        foreach (V rate in rates) {
            if (rate.CompareTo(converter.Zero) < 0) {
                throw new ArgumentException(
                    $"rates must be non-negative, got {rate}", nameof(rates));
            }
        }

        X = x;
        Breakpoints = breakpoints;
        Rates = rates;
        Converter = converter;
        Name = name;
        DisplayName = displayName;

        // **默认边界间隙为 0**：**那时断点同时属于两个档位，而 `TryEvaluate` 取"第一个匹配"的档位，
        // 与基线的 `firstOrNull` 一致。** **正的间隙把区间收成半开，让 one-hot 真正唯一。**
        //
        // **The boundary gap defaults to zero**: **a breakpoint then belongs to two tiers, and `TryEvaluate` takes the
        // first match, matching the baseline's `firstOrNull`.** **A positive gap makes the intervals half-open so the
        // one-hot is genuinely unique.**
        BoundaryGap = boundaryGap ?? converter.Zero;

        _resultVar = new RealVar($"{name}_cost");
        _selectorVars = new BinVar[rates.Count];
        _masking = new MaskingFunction<V>[rates.Count];

        for (int tier = 0; tier < rates.Count; ++tier) {
            _selectorVars[tier] = new BinVar($"{name}_tier_{tier}");

            // 该档位的"整批数量 × 单价"。
            // The tier's "whole quantity times unit price".
            LinearPolynomial<V> scaled = x * rates[tier];
            _masking[tier] = new MaskingFunction<V>(
                input: scaled,
                mask: _selectorVars[tier],
                converter: converter,
                name: $"{name}_rate_{tier}",
                displayName: displayName);
        }
    }

    /// <summary>被计价的数量 / The quantity being priced.</summary>
    public LinearPolynomial<V> X { get; }

    /// <summary>断点 / The breakpoints.</summary>
    public IReadOnlyList<V> Breakpoints { get; }

    /// <summary>各档单价 / The per-tier unit prices.</summary>
    public IReadOnlyList<V> Rates { get; }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>断点处的收窄量 / The gap that makes tier intervals half-open.</summary>
    public V BoundaryGap { get; }

    /// <summary>结果辅助变量 / The result auxiliary variable.</summary>
    public RealVar ResultVar => _resultVar;

    /// <summary>各档的选择变量 / The per-tier selectors.</summary>
    public IReadOnlyList<BinVar> SelectorVars => _selectorVars;

    /// <summary>各档的屏蔽函数 / The per-tier masking functions.</summary>
    public IReadOnlyList<MaskingFunction<V>> MaskingFunctions => _masking;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem> { _resultVar };
            helpers.AddRange(_selectorVars);
            foreach (MaskingFunction<V> masking in _masking) {
                foreach (IVariableItem helper in masking.HelperVariables) {
                    helpers.Add(helper);
                }
            }

            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(X);

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(Converter.One, _resultVar) },
        Converter.Zero);

    /// <summary>
    /// 某个数量落在哪个档位 / Which tier a quantity falls in.
    /// </summary>
    /// <remarks>
    /// **最后一个档位的上界是开区间**（`quantity >= breakpoints[last-1]`），
    /// **因为断点表只给出各档的起点：`breakpoints` 有 n 项就对应 n−1 个档位，而最后一档没有上界。**
    ///
    /// **The last tier has no upper bound** (`quantity >= breakpoints[last-1]`),
    /// **because the breakpoint list only gives each tier's start: n breakpoints mean n−1 tiers, and the last one is
    /// open-ended.**
    /// </remarks>
    public int? TierOf(V quantity) {
        for (int tier = 0; tier < Rates.Count; ++tier) {
            V lower = Breakpoints[tier];
            if (tier == Rates.Count - 1) {
                if (quantity.CompareTo(lower) >= 0) {
                    return tier;
                }

                continue;
            }

            V upper = Breakpoints[tier + 1].Minus(BoundaryGap);
            if (quantity.CompareTo(lower) >= 0 && quantity.CompareTo(upper) <= 0) {
                return tier;
            }
        }

        return null;
    }

    /// <summary>按语义求值 / Evaluate by semantics.</summary>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        V? quantity = FunctionEvaluation.Polynomial(X, values).Value;
        if (quantity is not { } amount) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        // 低于第一个断点：不是"按第一档计价"，而是输入不在定价范围内。
        // Below the first breakpoint is an out-of-range input, not "price it in the first tier".
        if (amount.CompareTo(Breakpoints[0]) < 0) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        // 高于最后断点同样是越界：断点表只到那里为止。
        // Above the last breakpoint is also out of range: the table ends there.
        if (amount.CompareTo(Breakpoints[Breakpoints.Count - 1]) > 0) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        if (TierOf(amount) is not { } tier) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        // **单价按整批数量选，所以这里是 quantity * rate，不是分段累加。**
        // **The unit price comes from the whole quantity, so this is quantity * rate, not a segmented accumulation.**
        return new Ok<V?, ErrorCode, Error<ErrorCode>>(amount.Times(Rates[tier]));
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        var constraints = new List<LinearInequality<V>>();

        // one-hot：恰好一个档位被选中。**没有它，`result = Σ masked` 会把多个档位加起来。**
        // One-hot: exactly one tier is selected. **Without it, `result = Σ masked` adds several tiers.**
        var selectorTerms = new List<LinearMonomial<V>>();
        foreach (BinVar selector in _selectorVars) {
            selectorTerms.Add(new LinearMonomial<V>(Converter.One, selector));
        }

        constraints.Add(new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(selectorTerms, Converter.Zero),
            Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), Converter.One),
            Comparison: Comparison.EQ,
            Name: $"{Name}_tier_one_hot"));

        // result = Σ masked：合成最终费用。
        // result = Σ masked composes the final cost.
        var resultTerms = new List<LinearMonomial<V>> {
            new(Converter.One, _resultVar),
        };

        foreach (MaskingFunction<V> masking in _masking) {
            resultTerms.Add(new LinearMonomial<V>(Converter.One.Negate(), masking.ResultVar));
        }

        constraints.Add(new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(resultTerms, Converter.Zero),
            Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), Converter.Zero),
            Comparison: Comparison.EQ,
            Name: $"{Name}_cost"));

        Try added = IndicatorSupport.AddConstraints(model, constraints);
        if (added is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
            return failed;
        }

        foreach (MaskingFunction<V> masking in _masking) {
            Try registered = masking.RegisterConstraints(model);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> maskingFailed) {
                return maskingFailed;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }
}
