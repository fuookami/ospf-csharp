#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// 条件结果：指示列为 1 时取输入，否则为 0 / Value equals the input on indicator one, otherwise zero.
/// </summary>
/// <remarks>
/// B1178 按基线 `model/intermediate/ConditionalValue.kt` 补。
///
/// **四条约束是"两个分支各两条"，而两个分支的方向相反**：
///
/// | 行 | 指示 = 0 | 指示 = 1 |
/// | --- | --- | --- |
/// | `_zero_ub` | `result ≤ 0` | `result ≤ upper` |
/// | `_zero_lb` | `result ≥ 0` | `result ≥ lower` |
/// | `_then_ub` | 无约束 | `result ≤ input` |
/// | `_then_lb` | 无约束 | `result ≥ input` |
///
/// **指示 = 0 时上下两条把 `result` 夹成 0；指示 = 1 时 `_then_*` 两条把它夹成 `input`，
/// 而 `_zero_*` 退化成输入的上下界。**
///
/// **所以 `bounds` 不是"输入的值域"，而是<b>证明</b>：** **它让 `_zero_*` 在指示 = 1 时不构成额外限制。
/// 给一个错的（过窄的）`bounds`，模型要么不可行、要么切掉合法解。**
///
/// **三条校验都不是形式**：
///
/// **① `resultVariable != indicator`** —— **同一个变量当结果和指示时，
/// 两个分支要同一个变量同时等于 0 与 `input`，四条行直接冲突。**
/// **② 指示必须是二值** —— **连续指示会让两个分支同时"部分激活"，
/// `result` 变成一个介于 0 与 `input` 之间的原像——模型可行，结果是捏造出来的。**
/// **③ `lower ≤ upper`** —— **倒序范围会让 `_zero_*` 把 `result` 夹进一个空区间。**
///
/// B1178, following the baseline's `ConditionalValue`.
///
/// **The four rows are "two per branch", and the branches pull in opposite directions**:
///
/// | Row | indicator = 0 | indicator = 1 |
/// | --- | --- | --- |
/// | `_zero_ub` | `result ≤ 0` | `result ≤ upper` |
/// | `_zero_lb` | `result ≥ 0` | `result ≥ lower` |
/// | `_then_ub` | unconstrained | `result ≤ input` |
/// | `_then_lb` | unconstrained | `result ≥ input` |
///
/// **At indicator 0 the two `_zero_*` rows pin `result` to zero; at indicator 1 the two `_then_*` rows pin it to
/// `input`, while `_zero_*` relaxes to the input's bounds.**
///
/// **So `bounds` is not "the input's range" but a <b>witness</b>**: **it keeps `_zero_*` from binding at indicator 1.
/// A wrong (too narrow) `bounds` either makes the model infeasible or cuts off legal solutions.**
///
/// **None of the three checks is cosmetic**:
///
/// **① `resultVariable != indicator`** — **with one variable serving both roles, the branches demand it be zero and
/// equal to `input` at once, and the four rows conflict outright.**
/// **② the indicator must be binary** — **a continuous indicator activates both branches partially, so `result` becomes
/// some value between 0 and `input` — the model stays feasible and the result is fabricated.**
/// **③ `lower ≤ upper`** — **reversed bounds squeeze `result` into an empty interval.**
///
/// **只支持 `Flt64`，与仓库里其他条件结构一致**：**数值可用性检查（`ConditionPolynomials.IsUsable`）
/// 本身就是 `Flt64` 的，而 `V` 版本只能靠 `ToString` 再过一遍——那会静默丢精度。**
/// **基线的泛型来自它自己的转换器体系，这里没有对应的东西，所以不假装有。**
///
/// **`Flt64`-only, like the other condition structures here**: **the numeric usability check
/// (`ConditionPolynomials.IsUsable`) is itself `Flt64`, and a `V` version could only round-trip through `ToString` —
/// which loses precision silently.** **The baseline's genericity comes from its own converter system, which has no
/// counterpart here, so this does not pretend to have one.**
/// </remarks>
/// <param name="Input">真分支输入 / The true-branch input.</param>
/// <param name="ResultVariable">保留的数值结果列 / The retained value result column.</param>
/// <param name="Bounds">原真分支范围 / The original true-branch bounds.</param>
/// <param name="Name">约束名称前缀 / The constraint name prefix.</param>
public sealed record ConditionalValue(
    LinearPolynomial<Flt64> Input,
    IVariableItem ResultVariable,
    ConditionBounds<Flt64> Bounds,
    string Name) {

    /// <summary>生成四条条件结果约束 / Generate the four conditional-value rows.</summary>
    /// <param name="indicator">指示列 / The indicator column.</param>
    /// <param name="converter">数值转换器 / The value converter.</param>
    public Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> GenerateConstraints(
        IVariableItem indicator,
        IIntoValue<Flt64> converter) {
        ArgumentNullException.ThrowIfNull(indicator);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(Bounds);

        // **同一个变量当结果与指示：两个分支要求它同时等于 0 与 `input`，四条行直接冲突。**
        // **One variable in both roles: the branches demand it be zero and equal to `input` at once.**
        if (ReferenceEquals(ResultVariable, indicator)) {
            return Fail(
                "the conditional result must not be the indicator itself, or the two branches conflict");
        }

        // **连续指示会让两个分支同时部分激活，`result` 变成 0 与 `input` 之间的某个值——可行但捏造。**
        // **A continuous indicator activates both branches partially, leaving `result` somewhere between 0 and
        // `input` — feasible but fabricated.**
        if (!indicator.TypeKind.IsBinaryType) {
            return Fail($"the indicator must be binary, but `{indicator.Name}` is not");
        }

        Flt64 zero = converter.Zero;
        Flt64 one = converter.One;

        if (!ConditionPolynomials.IsUsable(Bounds.Lower) || !ConditionPolynomials.IsUsable(Bounds.Upper)) {
            return Fail("the conditional-value bounds must be finite");
        }

        if (Bounds.Lower.CompareTo(Bounds.Upper) > 0) {
            return Fail(
                $"the conditional-value bounds require lower <= upper (lower={Bounds.Lower}, upper={Bounds.Upper})");
        }

        if (!ConditionPolynomials.IsUsable(Input)) {
            return Fail("the conditional-value input must have finite, encodable coefficients");
        }

        var result = new LinearMonomial<Flt64>(one, ResultVariable);

        var negativeInput = new LinearMonomial<Flt64>[Input.Monomials.Count];
        for (int index = 0; index < Input.Monomials.Count; ++index) {
            negativeInput[index] = new LinearMonomial<Flt64>(
                Input.Monomials[index].Coefficient.Negate(), Input.Monomials[index].Symbol);
        }

        var rows = new List<LinearInequality<Flt64>>(4) {
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    new[] { result, new LinearMonomial<Flt64>(Bounds.Upper.Negate(), indicator) }, zero),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), zero),
                Comparison: Comparison.LE,
                Name: $"{Name}_zero_ub"),
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    new[] { result, new LinearMonomial<Flt64>(Bounds.Lower.Negate(), indicator) }, zero),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), zero),
                Comparison: Comparison.GE,
                Name: $"{Name}_zero_lb"),
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append(negativeInput, result, new LinearMonomial<Flt64>(Bounds.Lower.Negate(), indicator)),
                    Input.Constant.Negate()),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Bounds.Lower.Negate()),
                Comparison: Comparison.LE,
                Name: $"{Name}_then_ub"),
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append(negativeInput, result, new LinearMonomial<Flt64>(Bounds.Upper.Negate(), indicator)),
                    Input.Constant.Negate()),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Bounds.Upper.Negate()),
                Comparison: Comparison.GE,
                Name: $"{Name}_then_lb"),
        };

        // **逐行再核一次数值可用性**：**`bounds` 通过检查不代表 `lhs − rhs` 的合并系数可用——
        // 大数相减会溢出成无穷，而那正是"能建出来但求解器读不了"的情形。**
        //
        // **Re-check each row**: **valid bounds do not imply the merged `lhs − rhs` coefficients are usable — subtracting
        // large numbers overflows to infinity, which is exactly "builds fine, the solver cannot read it".**
        foreach (LinearInequality<Flt64> row in rows) {
            if (!ConditionPolynomials.IsUsable(Flatten(row))) {
                return Fail($"the conditional-value row `{row.Name}` has unusable numeric values");
            }
        }

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }

    private static LinearMonomial<Flt64>[] Append(
        LinearMonomial<Flt64>[] prefix, LinearMonomial<Flt64> first, LinearMonomial<Flt64> second) {
        var monomials = new LinearMonomial<Flt64>[prefix.Length + 2];
        prefix.CopyTo(monomials, 0);
        monomials[prefix.Length] = first;
        monomials[prefix.Length + 1] = second;
        return monomials;
    }

    private static LinearPolynomial<Flt64> Flatten(LinearInequality<Flt64> row) {
        var monomials = new List<LinearMonomial<Flt64>>(
            row.Lhs.Monomials.Count + row.Rhs.Monomials.Count);
        monomials.AddRange(row.Lhs.Monomials);
        foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
            monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol));
        }

        return new LinearPolynomial<Flt64>(monomials, row.Lhs.Constant.Minus(row.Rhs.Constant));
    }

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> Fail(
        string message) =>
        new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, message);
}
