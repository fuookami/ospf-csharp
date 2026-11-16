#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 把变量钉在一个值上：<c>variable = value</c> / Pin a variable to a value.
/// </summary>
/// <remarks>
/// 对齐基线 `DiscreteCondition.kt` 的 `fixedVariableEquality`。
///
/// **它只有一行，但有两个容易漏的地方**：
///
/// **① 系数必须是 `one`，常数必须是 `zero`，<b>不能反过来</b>。**
/// **`one` 与 `zero` 由调用方传入，而不是从 `Flt64` 现造**——
/// **这是为了让它能用在任何 `V` 上（包括定点数与高精度数），
/// 而"现造一个 1"在那些类型上不一定等于调用方的单位元。**
///
/// **② 右端是 `value`，左端是变量，方向不能反。**
/// **反了在 `EQ` 下数值上等价，但名称与系数的符号会变**——
/// **而 `EQ` 恰好是对称的，所以这是一个"改了也测不出来"的错误。**
///
/// **本函数<b>不做</b>可用性检查**：**基线也没有做。**
/// **它只是构造一行；检查在调用方。** **这一点要说清，否则读代码的人会以为这里已经把关了。**
///
/// B1169, following the baseline's `fixedVariableEquality`.
///
/// **It is one row, but two things are easy to miss**:
///
/// **① The coefficient must be `one` and the constant `zero`, <b>not the other way round</b>.**
/// **`one` and `zero` come from the caller rather than being built from `Flt64`** — **so it works for any `V`
/// (fixed-point, high-precision), where "a freshly built one" need not equal the caller's identity.**
///
/// **② The right-hand side is `value` and the left-hand side is the variable; the direction must not flip.**
/// **Flipped, it is numerically identical under `EQ`, but the name and the coefficient signs change** —
/// **and `EQ` is symmetric, so this is an error that "changes nothing measurable".**
///
/// **This function <b>performs no</b> usability check**: **neither does the baseline.**
/// **It only builds a row; the checking is the caller's.** **That has to be said, or a reader would assume the gate is
/// here.**
/// </remarks>
public static class FixedVariableEquality
{
    /// <summary>构造 <c>variable = value</c> 的等式行 / Build the `variable = value` row.</summary>
    /// <param name="variable">被固定的变量 / The variable being pinned.</param>
    /// <param name="value">固定到的值 / The value it is pinned to.</param>
    /// <param name="zero">调用方的零元 / The caller's zero.</param>
    /// <param name="one">调用方的单位元 / The caller's one.</param>
    /// <param name="name">行名称 / The row name.</param>
    public static LinearInequality<Flt64> Of(
        IVariableItem variable,
        Flt64 value,
        Flt64 zero,
        Flt64 one,
        string name) {
        ArgumentNullException.ThrowIfNull(variable);

        return new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(one, variable) },
                zero),
            Rhs: new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(),
                value),
            Comparison: Comparison.EQ,
            Name: name);
    }
}
