#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 真实函数基准值：逼近误差的对照物 / The true function's value: what the approximation is measured against.
/// </summary>
/// <remarks>
/// 对齐基线 `NonlinearPiecewise.kt` 基类的 `originalValueFlt64` / `originalValue`。
///
/// **为什么需要它**：**四个分段逼近函数（`exp`/`ln`/`1/x`/`x^p`）都只返回<b>逼近值</b>。**
/// **没有真实值，就<b>无法测出逼近误差</b>——**
/// **而"分段数够不够"恰恰是这个方向唯一需要判断的事。**
/// **"误差在断点处为零"是一个陷阱**：**线性插值在每个断点处与真实函数完全重合**，
/// **所以"抽查几个断点"永远测不出段数不足。**
///
/// **三处必须检查，而基线三处都检查了**：
/// **① 输入非有限；② 输入落在 `[breakpoints.first, breakpoints.last]` 之外；③ 真实值非有限。**
/// **漏掉 ① 会让 `NaN` 一路传到误差计算里，而 `NaN` 与任何数比较都是 `false`，
/// 于是"误差过大"的判据<b>静默通过</b>。**
/// **漏掉 ② 会让 `ln` 在负值处给出 `NaN`、`1/x` 在零附近给出 `inf`，同样静默。**
///
/// adds
///
/// **Why it is needed**: **all four piecewise functions (`exp`/`ln`/`1/x`/`x^p`) return only the <b>approximation</b>.**
/// **Without the true value the approximation error <b>cannot be measured</b>** —
/// **and "are there enough segments" is the one question this direction must answer.**
/// **"The error is zero at the breakpoints" is a trap**: **linear interpolation coincides exactly with the true
/// function at every breakpoint**, **so "spot-check a few breakpoints" can never reveal too few segments.**
///
/// **Three things must be checked, and the baseline checks all three**:
/// **① non-finite input; ② input outside `[breakpoints.first, breakpoints.last]`; ③ non-finite true value.**
/// **Skipping ① lets `NaN` flow into the error computation, and `NaN` compares `false` against everything, so an
/// "error too large" criterion <b>passes silently</b>.**
/// **Skipping ② lets `ln` produce `NaN` at negatives and `1/x` produce `inf` near zero — equally silently.**
/// </remarks>
internal static class NonlinearSourceValue
{
    /// <summary>在逼近定义域内求真实函数值 / Evaluate the true function inside the approximation domain.</summary>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> Of(
        Func<double, double> source,
        double lower,
        double upper,
        double input,
        string what) {
        if (!double.IsFinite(input)) {
            return new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"{what}: the source-function input must be finite (input={input})");
        }

        // **域外必须拒绝，而不是外推**：**分段函数只在 `[lower, upper]` 上有定义，
        // 而在域外它仍会"给出一个数"——那个数与真实函数无关。**
        //
        // **Outside the domain must be rejected rather than extrapolated**: **the piecewise function is defined only
        // on `[lower, upper]`, and outside it still "gives a number" — one unrelated to the true function.**
        if (input < lower || input > upper) {
            return new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"{what}: the source-function input must lie inside the finite approximation domain " +
                $"[{lower}, {upper}] (input={input})");
        }

        double value;
        try {
            value = source(input);
        } catch (Exception error) when (error is not OutOfMemoryException) {
            return new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"{what}: source-function evaluation failed: {error.Message}");
        }

        if (!double.IsFinite(value)) {
            return new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"{what}: source-function evaluation produced a non-finite value (input={input}, value={value})");
        }

        return new Ok<Flt64, ErrorCode, Error<ErrorCode>>(new Flt64(value));
    }

    /// <summary>在逼近定义域内测量逼近误差 / Measure the approximation error inside the domain.</summary>
    /// <remarks>
    /// **`|逼近值 − 真实值|`**。**两个值都由调用方给出，这个函数只做减法与取绝对值**——
    /// **它不负责判断"误差可接受"，那是调用方的事。**
    ///
    /// **`|approximation − true|`.** **Both values come from the caller; this only subtracts and takes the absolute
    /// value** — **it does not decide whether the error is acceptable; that is the caller's job.**
    /// </remarks>
    public static Result<double, ErrorCode, Error<ErrorCode>> ErrorAt(
        Func<double, double> source,
        double lower,
        double upper,
        double input,
        double approximation,
        string what) {
        Result<Flt64, ErrorCode, Error<ErrorCode>> truth = Of(source, lower, upper, input, what);
        if (truth is Failed<Flt64, ErrorCode, Error<ErrorCode>> failed) {
            return new Failed<double, ErrorCode, Error<ErrorCode>>(failed.Error);
        }

        // **逼近值本身也可能非有限**：**那时"误差"是 `NaN` 或 `inf`，
        // 而 `NaN > 容差` 是 `false`——判据会静默通过。**
        //
        // **The approximation itself can be non-finite**: **then the "error" is `NaN` or `inf`, and `NaN > tolerance`
        // is `false` — the criterion passes silently.**
        if (!double.IsFinite(approximation)) {
            return new Failed<double, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"{what}: the approximation is not finite (input={input}, value={approximation})");
        }

        return new Ok<double, ErrorCode, Error<ErrorCode>>(
            global::System.Math.Abs(approximation - truth.Value.ToDouble()));
    }
}
