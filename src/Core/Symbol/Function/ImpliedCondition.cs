#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 非折叠蕴含：前置成立时，把输入压进给定区间 / A non-folded implication: gate the input into an interval.
/// </summary>
/// <remarks>
/// 对齐。
///
/// **语义**：**`antecedent = 1` 时要求 `input ∈ [bounds.lower, bounds.upper]`；
/// `antecedent = 0` 时这两条行不起作用。**
///
/// **三行，各行都有它存在的理由**：
/// **① `input − lowerAdjust·(indicator + antecedent) ≥ lower − lowerAdjust`——下界门控；**
/// **② `input − upperAdjust·(indicator − antecedent) ≤ upperAdjust`——上界门控；**
/// **③ `antecedent − indicator ≤ 0`——把两个二元量链起来。**
///
/// **`lowerAdjust = tolerance − bounds.lower`**：**它<b>不是</b> `bounds.lower`。**
/// **用 `bounds.lower` 会让下界行在 `antecedent = 0` 时给出 `input ≥ bounds.lower`——
/// 也就是"前置不成立时输入仍被压在区间内"，蕴含退化成合取。**
/// **而 `tolerance − lower` 恰好把这个偏移抵消掉，于是前置为 0 时行变成 `input ≥ lower − lowerAdjust`，
/// 而 `lower − lowerAdjust = tolerance − 2·lower` 是一个更松的界。**
///
/// **`③` 是必需的一行**：**只写 ①② 时 `indicator` 是自由的，
/// 求解器可以为了满足区间而把它设成 1，于是"蕴含"变成"永远成立"。**
///
/// Following its.
///
/// **Semantics**: **when `antecedent = 1` the input must lie in `[bounds.lower, bounds.upper]`; when
/// `antecedent = 0` these rows do nothing.**
///
/// **Three rows, each with a reason to exist**.
/// **① `input − lowerAdjust·(indicator + antecedent) ≥ lower − lowerAdjust` — the lower gate;**
/// **② `input − upperAdjust·(indicator − antecedent) ≤ upperAdjust` — the upper gate;**
/// **③ `antecedent − indicator ≤ 0` — linking the two binary quantities.**
///
/// **`lowerAdjust = tolerance − bounds.lower`**, **which is <b>not</b> `bounds.lower`.**
/// **Using `bounds.lower` would make the lower row read `input ≥ bounds.lower` when `antecedent = 0` — that is,
/// "the input stays inside the interval even when the antecedent is false", degrading the implication into a
/// conjunction.** **`tolerance − lower` cancels that offset, so with a false antecedent the row becomes
/// `input ≥ lower − lowerAdjust`, a looser bound.**
///
/// **`③` is essential**: **with only ①② the `indicator` is free, so the solver may set it to one to satisfy the
/// interval — turning "implies" into "always".**
/// </remarks>
public sealed record ImpliedCondition
{
    /// <summary>创建一条非折叠蕴含 / Create one.</summary>
    public ImpliedCondition(
        LinearPolynomial<Flt64> input,
        IVariableItem indicatorVariable,
        ConditionBounds<Flt64> bounds,
        string name) {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(indicatorVariable);
        ArgumentNullException.ThrowIfNull(bounds);

        Input = input;
        IndicatorVariable = indicatorVariable;
        Bounds = bounds;
        Name = name;
    }

    /// <summary>被门控的输入 / The gated input.</summary>
    public LinearPolynomial<Flt64> Input { get; }

    /// <summary>指示变量 / The indicator variable.</summary>
    public IVariableItem IndicatorVariable { get; }

    /// <summary>前置成立时要求的区间 / The interval required when the antecedent holds.</summary>
    public ConditionBounds<Flt64> Bounds { get; }

    /// <summary>名称 / The name.</summary>
    public string Name { get; }

    /// <summary>
    /// 生成三行门控约束 / Generate the three gated rows.
    /// </summary>
    /// <param name="antecedent">前置变量，必须是二元 / The antecedent, which must be binary.</param>
    /// <param name="tolerance">容差，必须为正 / The tolerance, which must be positive.</param>
    /// <param name="converter">数值转换器 / The converter.</param>
    public Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> GenerateConstraints(
        IVariableItem antecedent,
        Flt64 tolerance,
        IIntoValue<Flt64> converter) {
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(converter);

        // **两个二元量必须是不同的**：**同一个变量同时当前置与指示，
        // 第 ③ 行会退化成 `0 ≤ 0`，于是整个链路失效而模型仍可行。**
        //
        // **The two binary quantities must differ**: **if one variable served as both antecedent and indicator, row ③
        // would degenerate to `0 ≤ 0`, the link would vanish and the model would stay feasible.**
        if (!antecedent.TypeKind.IsBinaryType || !IndicatorVariable.TypeKind.IsBinaryType) {
            return Fail("the antecedent and the indicator must both be binary");
        }

        if (ReferenceEquals(antecedent, IndicatorVariable)) {
            return Fail("the antecedent and the indicator must be different variables");
        }

        if (!BigMValues.IsUsable(tolerance) || tolerance.CompareTo(Flt64.Zero) <= 0) {
            return Fail("the tolerance must be finite and positive");
        }

        if (!BigMValues.IsUsable(Bounds.Lower) || !BigMValues.IsUsable(Bounds.Upper)) {
            return Fail("the condition bounds must be finite");
        }

        // **两个界检查**：**`lower > 0` 与 `upper < tolerance` 都被拒绝。**
        // **理由是这两个情形下 `lowerAdjust` 或 `upperAdjust` 会变成 0 或负数，
        // 于是门控行的系数消失，前置为假时约束不再松弛。**
        //
        // **The code rejects `lower > 0` and `upper < tolerance`.** **In both cases `lowerAdjust` or
        // `upperAdjust` becomes zero or negative, the gate coefficient vanishes, and the constraint no longer relaxes
        // when the antecedent is false.**
        if (Bounds.Lower.CompareTo(Flt64.Zero) > 0) {
            return Fail($"the lower bound must not exceed zero (lower={Bounds.Lower})");
        }

        if (Bounds.Upper.CompareTo(tolerance) < 0) {
            return Fail($"the upper bound must be at least the tolerance (upper={Bounds.Upper}, tolerance={tolerance})");
        }

        if (!ConditionPolynomials.IsUsable(Input)) {
            return Fail("the input polynomial must have finite, encodable coefficients");
        }

        Flt64 lowerAdjustment = tolerance.Minus(Bounds.Lower);
        Flt64 upperAdjustment = Bounds.Upper;

        var rows = new List<LinearInequality<Flt64>>(3);

        // ① 下界门控：`input − lowerAdjust·indicator − lowerAdjust·antecedent ≥ lower − lowerAdjust`
        var lowerTerms = new List<LinearMonomial<Flt64>>(Input.Monomials.Count + 2);
        foreach (LinearMonomial<Flt64> monomial in Input.Monomials) {
            lowerTerms.Add(monomial);
        }

        lowerTerms.Add(new LinearMonomial<Flt64>(lowerAdjustment.Negate(), IndicatorVariable));
        lowerTerms.Add(new LinearMonomial<Flt64>(lowerAdjustment.Negate(), antecedent));
        rows.Add(new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(lowerTerms, Input.Constant),
            Rhs: new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), Bounds.Lower.Minus(lowerAdjustment)),
            Comparison: Comparison.GE,
            Name: $"{Name}_con_gated_lower"));

        // ② 上界门控：`input − upperAdjust·indicator + upperAdjust·antecedent ≤ upperAdjust`
        var upperTerms = new List<LinearMonomial<Flt64>>(Input.Monomials.Count + 2);
        foreach (LinearMonomial<Flt64> monomial in Input.Monomials) {
            upperTerms.Add(monomial);
        }

        upperTerms.Add(new LinearMonomial<Flt64>(upperAdjustment.Negate(), IndicatorVariable));
        upperTerms.Add(new LinearMonomial<Flt64>(upperAdjustment, antecedent));
        rows.Add(new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(upperTerms, Input.Constant),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), upperAdjustment),
            Comparison: Comparison.LE,
            Name: $"{Name}_con_gated_upper"));

        // ③ 链接：`antecedent − indicator ≤ 0`
        rows.Add(new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(converter.One, antecedent),
                    new LinearMonomial<Flt64>(converter.One.Negate(), IndicatorVariable),
                },
                converter.Zero),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), converter.Zero),
            Comparison: Comparison.LE,
            Name: $"{Name}_imply_link"));

        foreach (LinearInequality<Flt64> row in rows) {
            if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                return Fail("the gated rows must have finite, encodable coefficients");
            }

            // **行的"实际差值"也要可用**：**左右两端各自可用不等于它们的差可用**——
            // **两个 `1e300` 相减会溢出成 `inf`，而两端都通过了检查。**
            //
            // **The row's actual difference must be usable too**: **both sides passing does not imply their
            // difference does** — **two `1e300`s subtract into `inf` while both ends passed.**
            var difference = new List<LinearMonomial<Flt64>>(row.Lhs.Monomials.Count + row.Rhs.Monomials.Count);
            foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
                difference.Add(monomial);
            }

            foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
                difference.Add(new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol));
            }

            if (!ConditionPolynomials.IsUsable(
                new LinearPolynomial<Flt64>(difference, row.Lhs.Constant.Minus(row.Rhs.Constant)))) {
                return Fail("the gated row difference must be finite and encodable");
            }
        }

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows.AsReadOnly());
    }

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, message);
}
