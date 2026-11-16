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
/// 按下标取表项：<c>index = lowerIndex + i ? values[i]</c> / Table lookup by integer index.
/// </summary>
/// <remarks>
/// 对齐基线 `Element.kt` 的 `ElementFunction`。
///
/// **编码是三段**：
/// ① **一个 one-hot 的 `Σ selector_i = 1`**；
/// ② **每个表项各自一组屏蔽边**（与 `SelectFunction` 同一模式：<c>z_i = selector_i · values[i]</c>）；
/// ③ **两条等式**：`index − Σ i·selector_i = lowerIndex` 与 `result − Σ z_i = 0`。
///
/// **这里最容易漏的是第 ③ 段的第一条**：**没有它，`selector` 可以是任何 one-hot 组合，
/// 而结果只是"某个表项的值"——与 `index` 完全脱钩。**
/// **模型仍然可行、目标值仍然合理，只是答案与输入无关。**
///
/// **`lowerIndex` 是偏移而不是截断**：**`lowerIndex = 1` 表示下标从 1 起，
/// 而内部槽位仍然从 0 数——把两者搞混会让差一位。**
///
/// Following the baseline's `ElementFunction`.
///
/// **The encoding has three parts**:
/// ① **a one-hot `Σ selector_i = 1`**;
/// ② **a masking group per entry** (the same pattern as `SelectFunction`: `z_i = selector_i · values[i]`);
/// ③ **two equalities**: `index − Σ i·selector_i = lowerIndex` and `result − Σ z_i = 0`.
///
/// **The easiest thing to drop is the first equality in part ③**: **without it `selector` can be any one-hot
/// combination and the result is just "some entry's value", decoupled from `index`.**
/// **The model stays feasible and the objective reasonable; the answer simply has nothing to do with the input.**
///
/// **`lowerIndex` is an offset, not a truncation**: **`lowerIndex = 1` means indices start at one while the internal
/// slots still count from zero — confusing the two is an off-by-one.**
/// </remarks>
public sealed class ElementFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly RealVar _resultVar;
    private readonly BinVar[] _selectors;
    private readonly RealVar[] _maskedValues;

    /// <summary>构造一个表查找 / Build one.</summary>
    public ElementFunction(
        IVariableItem index,
        IReadOnlyList<LinearPolynomial<V>> values,
        IIntoValue<V> converter,
        int lowerIndex = 0,
        IReadOnlyList<ConditionBounds<V>>? valueBounds = null,
        string name = "element",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(converter);

        if (values.Count == 0) {
            throw new ArgumentException("an element lookup needs at least one table entry", nameof(values));
        }

        // **下标必须是整数。** **连续下标会让 `index − Σ i·selector_i = lowerIndex` 这条等式
        // 与 one-hot 一起把 `index` 逼成一个整数——看起来"自动取整"，实际上是模型在替调用方做四舍五入。**
        //
        // **The index must be an integer.** **A continuous index lets the one-hot plus the index equality force
        // `index` to an integer — which looks like "automatic rounding" but is the model rounding on the caller's
        // behalf.**
        if (index is not IntVar) {
            throw new ArgumentException(
                $"an element lookup needs an integer index, not {index.GetType().Name}",
                nameof(index));
        }

        if (valueBounds is not null && valueBounds.Count != values.Count) {
            throw new ArgumentException(
                $"valueBounds has {valueBounds.Count} entries for {values.Count} table entries",
                nameof(valueBounds));
        }

        Index = index;
        Values = values;
        Converter = converter;
        LowerIndex = lowerIndex;
        Name = name;
        DisplayName = displayName;

        var bounds = new ConditionBounds<V>[values.Count];
        for (int slot = 0; slot < values.Count; ++slot) {
            bounds[slot] = (valueBounds is null ? null : valueBounds[slot])
                ?? PolynomialBounds.FiniteBoundsOf(values[slot], converter)
                ?? throw new ArgumentException(
                    $"table entry {slot} needs finite bounds: pass them explicitly or bound its variables",
                    nameof(valueBounds));
        }

        ValueBounds = Array.AsReadOnly(bounds);

        V resultLower = bounds[0].Lower;
        V resultUpper = bounds[0].Upper;
        for (int slot = 1; slot < bounds.Length; ++slot) {
            if (bounds[slot].Lower.CompareTo(resultLower) < 0) {
                resultLower = bounds[slot].Lower;
            }

            if (bounds[slot].Upper.CompareTo(resultUpper) > 0) {
                resultUpper = bounds[slot].Upper;
            }
        }

        ResultBounds = new ConditionBounds<V>(resultLower, resultUpper);

        _resultVar = new RealVar($"{name}_element");
        _resultVar.Range.Geq(converter.FromValue(ResultBounds.Lower));
        _resultVar.Range.Leq(converter.FromValue(ResultBounds.Upper));

        _selectors = new BinVar[values.Count];
        _maskedValues = new RealVar[values.Count];
        for (int slot = 0; slot < values.Count; ++slot) {
            _selectors[slot] = new BinVar($"{name}_element_select_{slot}");
            _maskedValues[slot] = new RealVar($"{name}_element_value_{slot}");

            // z_i = selector_i · values[i]，所以它的范围是「0 与 values[i] 的界」的并集。
            // z_i = selector_i * values[i], so it ranges over the union of zero and values[i]'s bounds.
            V maskLower = bounds[slot].Lower.CompareTo(converter.Zero) < 0
                ? bounds[slot].Lower
                : converter.Zero;
            V maskUpper = bounds[slot].Upper.CompareTo(converter.Zero) > 0
                ? bounds[slot].Upper
                : converter.Zero;
            _maskedValues[slot].Range.Geq(converter.FromValue(maskLower));
            _maskedValues[slot].Range.Leq(converter.FromValue(maskUpper));
        }
    }

    /// <summary>下标变量 / The index variable.</summary>
    public IVariableItem Index { get; }

    /// <summary>表项 / The table entries.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Values { get; }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>第一个槽位对应的下标 / The index of the first slot.</summary>
    public int LowerIndex { get; }

    /// <summary>每个表项的区间 / Each entry's interval.</summary>
    public IReadOnlyList<ConditionBounds<V>> ValueBounds { get; }

    /// <summary>结果的区间 / The result's interval.</summary>
    public ConditionBounds<V> ResultBounds { get; }

    /// <summary>结果辅助变量 / The result auxiliary variable.</summary>
    public RealVar ResultVar => _resultVar;

    /// <summary>one-hot 选择变量 / The one-hot selectors.</summary>
    public IReadOnlyList<BinVar> Selectors => _selectors;

    /// <summary>被屏蔽的表项值 / The masked entry values.</summary>
    public IReadOnlyList<RealVar> MaskedValues => _maskedValues;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem> { _resultVar };
            helpers.AddRange(_selectors);
            helpers.AddRange(_maskedValues);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Values);

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(Converter.One, _resultVar) },
        Converter.Zero);

    /// <summary>one-hot 边 <c>Σ selector_i = 1</c> / The one-hot row.</summary>
    public LinearInequality<V> OneHotRow() {
        var terms = new LinearMonomial<V>[_selectors.Length];
        for (int slot = 0; slot < _selectors.Length; ++slot) {
            terms[slot] = new LinearMonomial<V>(Converter.One, _selectors[slot]);
        }

        return new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(terms, Converter.Zero),
            Rhs: Constant(Converter.One),
            Comparison: Comparison.EQ,
            Name: $"{Name}_element_one_hot");
    }

    /// <summary>
    /// 下标等式 <c>index − Σ i·selector_i = lowerIndex</c> / The index equality.
    /// </summary>
    /// <remarks>
    /// **这是这里最容易漏的一条**：**没有它，`selector` 可以是任何 one-hot 组合，
    /// 而结果只是"某个表项的值"——与 `index` 完全脱钩。**
    ///
    /// **This is the row most easily dropped**: **without it `selector` can be any one-hot combination and the result
    /// is just "some entry's value", decoupled from `index`.**
    /// </remarks>
    public LinearInequality<V> IndexRow() {
        var terms = new List<LinearMonomial<V>> {
            new(Converter.One, Index),
        };

        for (int slot = 0; slot < _selectors.Length; ++slot) {
            V offset = Converter.IntoValue(new Flt64(slot));
            terms.Add(new LinearMonomial<V>(offset.Negate(), _selectors[slot]));
        }

        return new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(terms, Converter.Zero),
            Rhs: Constant(Converter.IntoValue(new Flt64(LowerIndex))),
            Comparison: Comparison.EQ,
            Name: $"{Name}_element_index");
    }

    /// <summary>结果等式 <c>result − Σ z_i = 0</c> / The result equality.</summary>
    public LinearInequality<V> ResultRow() {
        var terms = new List<LinearMonomial<V>> {
            new(Converter.One, _resultVar),
        };

        foreach (RealVar masked in _maskedValues) {
            terms.Add(new LinearMonomial<V>(Converter.One.Negate(), masked));
        }

        return new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(terms, Converter.Zero),
            Rhs: Constant(Converter.Zero),
            Comparison: Comparison.EQ,
            Name: $"{Name}_element_result");
    }

    /// <summary>某个槽位的表项区间边 / The interval rows for one entry.</summary>
    public IReadOnlyList<LinearInequality<V>> EntryRows(int slot) {
        ConditionBounds<V> bounds = ValueBounds[slot];
        return new[] {
            new LinearInequality<V>(
                Lhs: Values[slot], Rhs: Constant(bounds.Upper), Comparison: Comparison.LE,
                Name: $"{Name}_element_entry_{slot}_upper"),
            new LinearInequality<V>(
                Lhs: Values[slot], Rhs: Constant(bounds.Lower), Comparison: Comparison.GE,
                Name: $"{Name}_element_entry_{slot}_lower"),
        };
    }

    /// <summary>某个槽位的四条屏蔽边 / The four masking rows for one entry.</summary>
    /// <remarks>
    /// **与 `SelectFunction` 同一模式，只是被屏蔽的是表项本身而不是"两分支之差"。**
    ///
    /// **Same pattern as `SelectFunction`, except the masked quantity is the entry itself rather than a branch
    /// difference.**
    /// </remarks>
    public IReadOnlyList<LinearInequality<V>> MaskingRows(int slot) {
        ConditionBounds<V> bounds = ValueBounds[slot];
        BinVar selector = _selectors[slot];
        RealVar masked = _maskedValues[slot];
        V lower = bounds.Lower;
        V upper = bounds.Upper;

        return new[] {
            new LinearInequality<V>(
                Lhs: new LinearPolynomial<V>(
                    new[] {
                        new LinearMonomial<V>(Converter.One, masked),
                        new LinearMonomial<V>(upper.Negate(), selector),
                    },
                    Converter.Zero),
                Rhs: Constant(Converter.Zero),
                Comparison: Comparison.LE,
                Name: $"{Name}_element_mask_{slot}_zero_upper"),
            new LinearInequality<V>(
                Lhs: new LinearPolynomial<V>(
                    new[] {
                        new LinearMonomial<V>(Converter.One, masked),
                        new LinearMonomial<V>(lower.Negate(), selector),
                    },
                    Converter.Zero),
                Rhs: Constant(Converter.Zero),
                Comparison: Comparison.GE,
                Name: $"{Name}_element_mask_{slot}_zero_lower"),
            DifferenceRow(slot, lower, Comparison.LE, "one_upper"),
            DifferenceRow(slot, upper, Comparison.GE, "one_lower"),
        };
    }

    /// <summary>
    /// 按语义求值：只看 `index`，不看辅助变量 / Evaluate by semantics: only `index`, no auxiliary variables.
    /// </summary>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        V? indexValue = values.TryGetValue(Index, out V stored) ? stored : null;
        if (indexValue is not { } selector) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        int? slot = SlotOf(selector);
        if (slot is not { } chosen) {
            // 下标不在表内：不是"夹到端点"，而是输入不合法。
            // An index outside the table is an illegal input, not "clamp to the end".
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        V? selected = FunctionEvaluation.Polynomial(Values[chosen], values).Value;
        if (selected is not { } picked || !ValueBounds[chosen].Contains(picked)) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        return new Ok<V?, ErrorCode, Error<ErrorCode>>(picked);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        var constraints = new List<LinearInequality<V>> { OneHotRow(), IndexRow() };

        for (int slot = 0; slot < Values.Count; ++slot) {
            constraints.AddRange(EntryRows(slot));
            constraints.AddRange(MaskingRows(slot));
        }

        constraints.Add(ResultRow());
        return IndicatorSupport.AddConstraints(model, constraints);
    }

    /// <summary>找出下标对应的槽位 / Map an index value to a slot.</summary>
    private int? SlotOf(V indexValue) {
        for (int slot = 0; slot < Values.Count; ++slot) {
            V expected = Converter.IntoValue(new Flt64(LowerIndex + slot));
            if (indexValue.CompareTo(expected) == 0) {
                return slot;
            }
        }

        return null;
    }

    /// <summary>造一条 <c>z − input + offset·(1 − selector) ≶ 0</c> 行 / Build one.</summary>
    private LinearInequality<V> DifferenceRow(int slot, V offset, Comparison comparison, string suffix) {
        LinearPolynomial<V> input = Values[slot];
        var terms = new List<LinearMonomial<V>> {
            new(Converter.One, _maskedValues[slot]),
        };

        foreach (LinearMonomial<V> monomial in input.Monomials) {
            terms.Add(new LinearMonomial<V>(monomial.Coefficient.Negate(), monomial.Symbol));
        }

        terms.Add(new LinearMonomial<V>(offset.Negate(), _selectors[slot]));

        return new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(terms, input.Constant.Negate()),
            Rhs: Constant(offset.Negate()),
            Comparison: comparison,
            Name: $"{Name}_element_mask_{slot}_{suffix}");
    }

    private LinearPolynomial<V> Constant(V value) => new(Array.Empty<LinearMonomial<V>>(), value);

}
