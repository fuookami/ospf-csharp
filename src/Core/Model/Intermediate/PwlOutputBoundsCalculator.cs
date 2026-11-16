#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// 分段线性函数的输出范围 / A piecewise-linear function's output range.
/// </summary>
/// <remarks>
/// 对齐基线 `DeferredFunctionFallback.kt` 的 `PwlOutputBounds`。
/// **B1193, following the baseline's `PwlOutputBounds`.**
/// </remarks>
/// <param name="Lower">输出下界 / Output lower bound.</param>
/// <param name="Upper">输出上界 / Output upper bound.</param>
public sealed record PwlOutputBounds(Flt64 Lower, Flt64 Upper);

/// <summary>
/// 计算分段线性函数的输出范围 / Compute a piecewise-linear function's output range.
/// </summary>
/// <remarks>
/// 对齐基线 `DeferredFunctionFallback.kt` 的 `calculatePwlOutputBounds`。
///
/// **只在本段的两个端点取值，不取内部 —— 而这只对<b>线性</b>分段成立。**
///
/// **一段线性函数在闭区间上的极值必定落在端点，所以端点够了。**
/// **如果哪天分段变成二次，同一个实现会静默地少掉内部极值——而那正是
/// 二次函数与线性函数的唯一区别。**
/// **所以这条前提写在这里：`ponytail:` 记的是"只支持线性分段，内部极值不处理，
/// 支持二次分段时必须改成求导或三点采样"。**
///
/// **断点的严格递增只比较<b>相邻</b>对，而这对全局严格递增是充分的
/// （相邻都严格小于，则整列严格递增——传递性）。**
///
/// **`Breakpoints.Count &lt; 2` 那一支给出的是"数量不匹配"消息，
/// 而它不是冗余：** **少了它，`Count == 1` 配空斜率表会先通过尺寸检查，
/// 然后在"端点求值"那一步以"输出非有限或无界"失败——那条消息指向数值，
/// 而真正的原因是"没有段可算"。**
///
/// B1193, following the baseline's `calculatePwlOutputBounds`.
///
/// **Only the two endpoints of each segment are evaluated, never an interior point — and that holds only for
/// <b>linear</b> pieces.**
///
/// **A linear function attains its extremes over a closed interval at the endpoints, so endpoints suffice.**
/// **Should the pieces ever become quadratic, the same implementation would silently miss interior extremes — which is
/// exactly what distinguishes a quadratic from a linear piece.**
/// **The premise is recorded here: `ponytail:` notes "linear pieces only, interior extremes not handled; a quadratic
/// piece needs differentiation or three-point sampling".**
///
/// **Breakpoints are compared only against their <b>neighbour</b>, which is sufficient for global strict increase (each
/// adjacent pair strictly smaller implies the whole list is strictly increasing, by transitivity).**
///
/// **The `Breakpoints.Count &lt; 2` branch is not redundant**: **without it, `Count == 1` with empty slope tables would
/// pass the size checks and then fail at the endpoint evaluation with "non-finite or unbounded output" — a message
/// pointing at numerics while the cause is "there is no segment".**
/// </remarks>
public static class PwlOutputBoundsCalculator
{
    /// <summary>计算输出范围 / Compute the output range.</summary>
    public static Result<PwlOutputBounds, ErrorCode, Error<ErrorCode>> Calculate(
        IReadOnlyList<Flt64> breakpoints,
        IReadOnlyList<Flt64> slopes,
        IReadOnlyList<Flt64> intercepts,
        IIntoValue<Flt64> converter) {
        ArgumentNullException.ThrowIfNull(breakpoints);
        ArgumentNullException.ThrowIfNull(slopes);
        ArgumentNullException.ThrowIfNull(intercepts);
        ArgumentNullException.ThrowIfNull(converter);

        try {
            if (breakpoints.Count < 2 ||
                slopes.Count != breakpoints.Count - 1 ||
                intercepts.Count != breakpoints.Count - 1) {
                return Fail(
                    "piecewise breakpoints, slopes and intercepts have inconsistent counts");
            }

            foreach (Flt64 value in breakpoints) {
                if (!BigMValues.IsUsable(value)) {
                    return Fail(
                        "piecewise breakpoints, slopes and intercepts must be finite representable values");
                }
            }

            foreach (Flt64 value in slopes) {
                if (!BigMValues.IsUsable(value)) {
                    return Fail(
                        "piecewise breakpoints, slopes and intercepts must be finite representable values");
                }
            }

            foreach (Flt64 value in intercepts) {
                if (!BigMValues.IsUsable(value)) {
                    return Fail(
                        "piecewise breakpoints, slopes and intercepts must be finite representable values");
                }
            }

            for (int index = 0; index < breakpoints.Count - 1; ++index) {
                if (breakpoints[index].CompareTo(breakpoints[index + 1]) >= 0) {
                    return Fail($"piecewise breakpoints must be strictly increasing (index {index})");
                }
            }

            // **每段两个端点。** **只有线性分段才能这样取极值。**
            // **Two endpoints per segment.** **Only linear pieces allow sampling extremes this way.**
            var outputs = new List<Flt64>(2 * slopes.Count);
            for (int index = 0; index < slopes.Count; ++index) {
                outputs.Add(slopes[index].Times(breakpoints[index]).Plus(intercepts[index]));
                outputs.Add(slopes[index].Times(breakpoints[index + 1]).Plus(intercepts[index]));
            }

            if (outputs.Count == 0) {
                return Fail(
                    "evaluating piecewise endpoints produced a non-finite or unbounded output");
            }

            foreach (Flt64 value in outputs) {
                if (!BigMValues.IsUsable(value)) {
                    return Fail(
                        "evaluating piecewise endpoints produced a non-finite or unbounded output");
                }
            }

            Flt64 lower = outputs[0];
            Flt64 upper = outputs[0];
            for (int index = 1; index < outputs.Count; ++index) {
                if (outputs[index].CompareTo(lower) < 0) {
                    lower = outputs[index];
                }

                if (outputs[index].CompareTo(upper) > 0) {
                    upper = outputs[index];
                }
            }

            return new Ok<PwlOutputBounds, ErrorCode, Error<ErrorCode>>(
                new PwlOutputBounds(lower, upper));
        }
        catch (Exception error) {
            return Fail($"failed to calculate piecewise output bounds: {error.Message}");
        }
    }

    private static Result<PwlOutputBounds, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);
}
