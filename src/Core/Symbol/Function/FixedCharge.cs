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
/// 固定费用函数：**启用就付固定成本，另按活动量付单位成本** / A fixed charge: pay to activate, then per unit.
/// </summary>
/// <remarks>
/// 对齐基线 Tariff.kt 的 FixedChargeFunction。
///
/// 这是“开一台机器要一笔钱，之后每生产一件再付一点”的形状，也就是选址、机组组合、
/// 批量折扣的起订费那一类模型。
///
/// **它和普通线性成本的区别只在“启用”这一步**：cost = fixedCost · active + unitCost · activity，
/// 而 active 是 0/1 变量。**少了 active 那一项，模型会说“不启用也免费”，而目标函数看起来完全正常。**
///
/// The FixedChargeFunction from the code.
///
/// This is “pay to start a machine, then pay per item” — siting, unit commitment, and the setup fee of
/// quantity discounts all take this shape.
///
/// **The only difference from an ordinary linear cost is the activation term**.
/// cost = fixedCost · active + unitCost · activity with a binary active.
/// **Drop the activation term and the model says “not activating is free”, with an objective that looks perfectly
/// ordinary.**
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FixedChargeFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    /// <summary>创建固定费用函数 / Create a fixed charge function.</summary>
    /// <param name="activation">启用变量，必须是 0/1 / Activation variable, must be binary.</param>
    /// <param name="fixedCost">启用一次的固定成本，必须非负 / Fixed cost paid once when active, must be non-negative.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="activity">可选活动量多项式 / Optional activity polynomial.</param>
    /// <param name="unitCost">单位成本，必须非负 / Unit cost, must be non-negative.</param>
    /// <param name="minimumActive">启用时的最小活动量 / Minimum activity when active.</param>
    /// <param name="maximumActivity">启用时的最大活动量 / Maximum activity when active.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public FixedChargeFunction(
        IVariableItem activation,
        V fixedCost,
        IIntoValue<V> converter,
        LinearPolynomial<V>? activity = null,
        V? unitCost = null,
        V? minimumActive = null,
        V? maximumActivity = null,
        string name = "fixed_charge",
        string? displayName = null) {
        Activation = activation ?? throw new ArgumentNullException(nameof(activation));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));

        // 负的固定成本会变成“启用反而省钱”，于是求解器会把每个变量都打开——
        // 这不是“更优解”，是模型写错了。
        //
        // A negative fixed cost means “activating pays you”, so the solver opens everything —
        // not a better solution, a broken model.
        if (fixedCost.CompareTo(converter.Zero) < 0) {
            throw new ArgumentException(
                $"fixed cost must be non-negative (fixedCost={fixedCost})",
                nameof(fixedCost));
        }

        if (unitCost is { } perUnit && perUnit.CompareTo(converter.Zero) < 0) {
            throw new ArgumentException(
                $"unit cost must be non-negative (unitCost={perUnit})",
                nameof(unitCost));
        }

        // 活动量给了才谈上下界；只给一个界会让约束缺一条边，于是“启用但不活动”或“活动但不启用”会漏过去。
        //
        // Bounds only matter when there is an activity, and supplying just one side leaves a constraint open,
        // so “active but idle” or “busy but inactive” slips through.
        if (activity is not null && (minimumActive is null || maximumActivity is null)) {
            throw new ArgumentException(
                "an activity needs both a minimum and a maximum, or one side of the link is unconstrained",
                nameof(minimumActive));
        }

        if (minimumActive is { } lower && maximumActivity is { } upper && lower.CompareTo(upper) > 0) {
            throw new ArgumentException(
                $"minimum activity must not exceed the maximum (min={lower}, max={upper})",
                nameof(minimumActive));
        }

        FixedCost = fixedCost;
        Activity = activity;
        UnitCost = unitCost ?? converter.Zero;
        MinimumActive = minimumActive;
        MaximumActivity = maximumActivity;
        Name = name;
        DisplayName = displayName;

        ResultVar = new RealVar($"{name}_cost");
        _helperVariables = new IVariableItem[] { ResultVar };
    }

    /// <summary>启用变量 / Activation variable.</summary>
    public IVariableItem Activation { get; }

    /// <summary>固定成本 / Fixed cost.</summary>
    public V FixedCost { get; }

    /// <summary>活动量多项式 / Activity polynomial.</summary>
    public LinearPolynomial<V>? Activity { get; }

    /// <summary>单位成本 / Unit cost.</summary>
    public V UnitCost { get; }

    /// <summary>启用时的最小活动量 / Minimum activity when active.</summary>
    public V? MinimumActive { get; }

    /// <summary>启用时的最大活动量 / Maximum activity when active.</summary>
    public V? MaximumActivity { get; }

    /// <summary>成本结果变量 / The cost result variable.</summary>
    public RealVar ResultVar { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols {
        get {
            var symbols = new List<ISymbol> { Activation };
            if (Activity is not null) {
                symbols.AddRange(Activity.Monomials
                    .Where(term => term.Symbol is not null)
                    .Select(term => term.Symbol!));
            }

            return symbols;
        }
    }

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(_converter.One, ResultVar) },
        _converter.Zero);

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) {
        if (!values.TryGetValue(Activation, out V active)) {
            return null;
        }

        // 启用变量不是 0/1 时无法给值：它不是“约等于 0.5”，而是“这个模型还不该被求值”。
        //
        // A fractional activation has no value to return: not “roughly 0.5” but “this model is not ready to evaluate”.
        bool isZero = active.CompareTo(_converter.Zero) == 0;
        bool isOne = active.CompareTo(_converter.One) == 0;
        if (!isZero && !isOne) {
            return null;
        }

        V variableCost = _converter.Zero;
        if (Activity is not null) {
            Result<V?, ErrorCode, Error<ErrorCode>> amount = FunctionEvaluation.Polynomial(Activity, values);
            if (amount.Value is not { } resolved) {
                return null;
            }

            // 没启用却活动，或者启用了却超出区间，都是“模型不一致”，不是“取个近似值”。
            //
            // Idle-but-busy or active-but-outside the interval is an inconsistent model, not a value to approximate.
            if (isZero && resolved.CompareTo(_converter.Zero) != 0) {
                return null;
            }

            if (isOne &&
                (MinimumActive is { } lower && resolved.CompareTo(lower) < 0 ||
                    MaximumActivity is { } upper && resolved.CompareTo(upper) > 0)) {
                return null;
            }

            variableCost = resolved.Times(UnitCost);
        }

        V fixedPart = isOne ? FixedCost : _converter.Zero;
        return variableCost.Plus(fixedPart);
    }

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        new Ok<V?, ErrorCode, Error<ErrorCode>>(Evaluate(values));

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (model is null) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, "model is null");
        }

        var rows = new List<LinearInequality<V>>();
        var output = new List<LinearMonomial<V>> {
            new(_converter.One, ResultVar),
            new(_converter.Zero.Minus(FixedCost), Activation),
        };

        if (Activity is not null) {
            foreach (LinearMonomial<V> term in Activity.Monomials) {
                if (term.Symbol is not null) {
                    output.Add(new LinearMonomial<V>(_converter.Zero.Minus(UnitCost).Times(term.Coefficient), term.Symbol));
                }
            }

            V upper = MaximumActivity!.Value;
            V lower = MinimumActive!.Value;

            // 活动量 ≤ 上界 · 启用：不启用时活动量必须为 0。
            // activity <= maximum * active: idle when inactive.
            rows.Add(new LinearInequality<V>(
                Lhs: Activity + new LinearPolynomial<V>(
                    new[] { new LinearMonomial<V>(_converter.Zero.Minus(upper), Activation) },
                    _converter.Zero),
                Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _converter.Zero),
                Comparison: Comparison.LE,
                Name: $"{Name}_activity_upper"));

            // 活动量 ≥ 下界 · 启用：启用时不能低于最小批量。
            // activity >= minimum * active: no activation below the minimum batch.
            rows.Add(new LinearInequality<V>(
                Lhs: Activity + new LinearPolynomial<V>(
                    new[] { new LinearMonomial<V>(_converter.Zero.Minus(lower), Activation) },
                    _converter.Zero),
                Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _converter.Zero),
                Comparison: Comparison.GE,
                Name: $"{Name}_activity_lower"));
        }

        V constant = Activity is null
            ? _converter.Zero
            : _converter.Zero.Minus(UnitCost.Times(Activity.Constant));

        rows.Add(new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(output.AsReadOnly(), constant),
            Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _converter.Zero),
            Comparison: Comparison.EQ,
            Name: $"{Name}_cost_link"));

        // 约束必须经 IndicatorSupport 写入，不能直接往模型上加：
        // 它负责事务与“写完之后再校验界依赖”的顺序，而直接加会绕过这两件事。
        //
        // Constraints go through IndicatorSupport rather than straight onto the model:
        // it owns the transaction and the “re-validate bound dependencies after writing” order,
        // and adding directly bypasses both.
        return IndicatorSupport.AddConstraints(model: model, constraints: rows);
    }
}
