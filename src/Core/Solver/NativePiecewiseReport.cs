#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// &lt;summary>
/// 分段选择结果 / The result of selecting a segment.
/// &lt;/summary>
/// &lt;remarks>
/// B1202 按基线 `NativePiecewiseReport.kt` 的 `SegmentSelection` 补。
/// **注意 `Input` 是<b>夹紧之后</b>的值，不是调用方传进来的那个。**
///
/// **B1202, following the baseline's `SegmentSelection`.** **Note that `Input` is the <b>clamped</b> value, not the one
/// the caller passed in.**
/// &lt;/remarks>
/// &lt;param name="Index">分段下标 / Segment index.&lt;/param>
/// &lt;param name="Input">夹紧之后的输入 / The clamped input.&lt;/param>
public sealed record SegmentSelection(int Index, double Input);

/// &lt;summary>
/// 分段线性报告：分段选择 / Piecewise reporting: segment selection.
/// &lt;/summary>
/// &lt;remarks>
/// B1202 按基线 `NativePiecewiseReport.kt` 的 `selectSegment`、`finiteSolverDouble(s)` 与
/// `PIECEWISE_TOLERANCE` 补。
///
/// **容差只在定义域的两个<b>端点</b>起作用，内部断点没有容差。**
///
/// **`value &lt; lower` 时若差额不超过 `1e-6`，值被<b>夹到</b> `lower`；
/// `value > upper` 同理。而内部断点不在这个机制里——
/// 稍微越过某个内部断点的点直接落进相邻那段，不会被拒绝也不会被夹。**
///
/// **返回的是夹紧之后的值。** **所以一个比定义域下界小 `1e-9` 的输入，
/// 报回来的是恰好等于下界的数。** **如果调用方拿返回值去做下一步运算，
/// 它看到的是一个<b>没被要求过</b>的数——这件事必须显式。**
///
/// **内部断点归<b>前</b>一段。** **查找是"第一个满足 `bp[i] &lt;= value &lt;= bp[i+1]` 的 i"，
/// 两端都闭合，所以一个恰好落在内部断点上的值会命中左边那一段。**
///
/// **这看起来是一个任意的约定，但它必须是<b>确定的</b>：**
/// **分段线性在断点处连续当且仅当两段在该点的值相等；
/// 不相等时这个点有两个"正确答案"，而报告必须给出一个。**
///
/// **Tolerance applies only at the two <b>ends</b> of the domain; interior breakpoints have none.**
///
/// **When `value &lt; lower` and the excess is at most `1e-6`, the value is <b>clamped</b> to `lower`; likewise above
/// `upper`.** **Interior breakpoints are outside this mechanism — a point slightly past an interior breakpoint simply
/// falls into the neighbouring segment, neither rejected nor clamped.**
///
/// **The clamped value is what comes back.** **So an input `1e-9` below the domain reports exactly the lower bound.** **If
/// the caller feeds that return value onward it sees a number it did not ask for — which has to be explicit.**
///
/// **An interior breakpoint belongs to the <b>earlier</b> segment.** **The search is "the first `i` with
/// `bp[i] &lt;= value &lt;= bp[i+1]`", closed at both ends, so a value landing exactly on an interior breakpoint hits the
/// segment on its left.**
///
/// **That looks arbitrary, but it has to be <b>deterministic</b>**: **a piecewise function is continuous at a breakpoint
/// exactly when the two segments agree there; when they disagree the point has two "right answers" and the report must
/// pick one.**
/// &lt;/remarks>
public static class NativePiecewiseReport
{
    /// &lt;summary>分段选择的容差 / The tolerance used for segment selection.&lt;/summary>
    public const double PiecewiseTolerance = 1e-6;

    /// &lt;summary>选择输入所在的分段 / Select the segment containing the input.&lt;/summary>
    public static Result<SegmentSelection, ErrorCode, Error<ErrorCode>> SelectSegment(
        Flt64 input,
        IReadOnlyList<Flt64> breakpoints) {
        ArgumentNullException.ThrowIfNull(breakpoints);

        // **断点数量先查：它决定"这个结构本身是否成立"，与输入是否可用无关。**
        // **The breakpoint count is checked first: it decides whether the structure itself holds, independent of the
        // input.**
        if (breakpoints.Count < 2) {
            return Failure("PWL requires at least two breakpoints");
        }

        Result<double, ErrorCode, Error<ErrorCode>> convertedInput =
            FiniteSolverDouble(input, "native.pwl.input");
        if (convertedInput is Failed<double, ErrorCode, Error<ErrorCode>> badInput) {
            return Forward(badInput.Error);
        }

        double value = ((Ok<double, ErrorCode, Error<ErrorCode>>)convertedInput).Value;

        Result<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>> convertedBreakpoints =
            FiniteSolverDoubles(breakpoints, "native.pwl.breakpoints");
        if (convertedBreakpoints is Failed<
                IReadOnlyList<double>, ErrorCode, Error<ErrorCode>> badBreakpoints) {
            return Forward(badBreakpoints.Error);
        }

        IReadOnlyList<double> points =
            ((Ok<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>>)convertedBreakpoints).Value;

        double tolerance = PiecewiseTolerance;
        double lower = points[0];
        double upper = points[points.Count - 1];

        // **端点容差：夹而不拒。**
        // **Endpoint tolerance: clamp rather than reject.**
        if (value < lower) {
            if (lower - value > tolerance) {
                return Failure("PWL input is outside its domain");
            }

            value = lower;
        }
        else if (value > upper) {
            if (value - upper > tolerance) {
                return Failure("PWL input is outside its domain");
            }

            value = upper;
        }

        for (int index = 0; index < points.Count - 1; ++index) {
            if (value >= points[index] && value <= points[index + 1]) {
                return new Ok<SegmentSelection, ErrorCode, Error<ErrorCode>>(
                    new SegmentSelection(index, value));
            }
        }

        return Failure("PWL input has no valid segment");
    }

    private static Result<double, ErrorCode, Error<ErrorCode>> FiniteSolverDouble(
        Flt64 value,
        string fieldName) {
        try {
            double converted = value.ToSolverDouble();
            if (!double.IsFinite(converted)) {
                return new Failed<double, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"numeric value must be finite: {fieldName}");
            }

            return new Ok<double, ErrorCode, Error<ErrorCode>>(converted);
        }
        catch (Exception error) {
            return new Failed<double, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"numeric conversion failed at {fieldName}: {error.Message}");
        }
    }

    private static Result<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>> FiniteSolverDoubles(
        IReadOnlyList<Flt64> values,
        string fieldName) {
        var converted = new List<double>(values.Count);
        for (int index = 0; index < values.Count; ++index) {
            Result<double, ErrorCode, Error<ErrorCode>> one =
                FiniteSolverDouble(values[index], $"{fieldName}[{index}]");
            if (one is Failed<double, ErrorCode, Error<ErrorCode>> bad) {
                return new Failed<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>>(
                    bad.Error.Code, bad.Error.Message);
            }

            converted.Add(((Ok<double, ErrorCode, Error<ErrorCode>>)one).Value);
        }

        return new Ok<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>>(converted);
    }

    private static Result<SegmentSelection, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        new Failed<SegmentSelection, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, message);

    private static Result<SegmentSelection, ErrorCode, Error<ErrorCode>> Forward(
        Error<ErrorCode> error) =>
        new Failed<SegmentSelection, ErrorCode, Error<ErrorCode>>(error.Code, error.Message);
}
