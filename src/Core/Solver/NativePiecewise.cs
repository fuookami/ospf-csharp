#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// &lt;summary>
/// 原生 PWL 所需的已转换 primitive 数据 / Converted primitive data required by native PWL.
/// &lt;/summary>
/// &lt;remarks>
/// 对齐的 `NativePiecewiseData`。
/// **Following its `NativePiecewiseData`.**
/// &lt;/remarks>
public sealed record NativePiecewiseData(
    VariableItemKey InputKey,
    VariableItemKey ResultKey,
    double[] XPoints,
    double[] YPoints,
    string Name);

/// &lt;summary>
/// 把 PWL 结构校验并转换为求解器无关的 primitive 数据 /
/// Validate a PWL structure and convert it into solver-independent primitive data.
/// &lt;/summary>
/// &lt;remarks>
/// **这一层<b>不</b>碰求解器边界与 fallback 选择变量的使用情况**（有意不处理）。
/// **它只回答一个问题：这个结构能不能被翻译成一组 `x/y` 断点。**
///
/// **断点必须<b>严格</b>递增：`x[i-1] >= x[i]` 即拒。**
///
/// **这与 `selectSegment` 形成对照：那里<b>没有</b>这项检查，
/// 所以一组乱序断点在报告侧会落到 `no valid segment` 这条误导性的消息上。
/// 而在这里，它得到的是准确的 `breakpoints must be strictly increasing`。**
///
/// **同一份数据在两条路径上得到两种质量的消息——这是"检查放在哪一层"的代价，
/// 而不是矛盾：primitive 层是<b>入口</b>，报告层是<b>下游</b>，
/// 入口严格、下游宽松是正常的，只要下游的错误消息不至于把人引向错误的方向。**
///
/// **`y` 点由每段的两个端点算出来，并顺带检查<b>连续性</b>：
/// 第 `i` 段在 `x[i]` 处的值必须等于第 `i-1` 段在 `x[i]` 处的值（`yPoints[i]`）。**
///
/// **所以一个"断点处跳变"的分段线性会被拒——即使每一段自己都是合法的直线。**
/// **理由：native primitive 只吃一组 `(x_i, y_i)` 点，它没有"每段一个截距"的概念。
/// 不连续的分段线性在这种表示下<b>无法表达</b>，所以它必须在转换时就被拒绝，
/// 而不是被静默地压成一条折线。**
///
/// **`maximumMagnitude` 用 `abs(value) &lt; maximumMagnitude`，即<b>严格</b>上界**，
/// 与 `NativeAbs` 一致。
///
/// **This layer does <b>not</b> touch solver bounds or fallback selector usage** (the implementation comment says so
/// explicitly). **It answers one question: can this structure be translated into a set of `x/y` breakpoints.**
///
/// **Breakpoints must be <b>strictly</b> increasing: `x[i-1] >= x[i]` is rejected.**
///
/// **This contrasts with the earlier `selectSegment`, which has <b>no</b> such check, so shuffled breakpoints land on the
/// misleading `no valid segment` message there while here they get the accurate `breakpoints must be strictly
/// increasing`.**
///
/// **The same data gets messages of different quality on the two paths — the price of choosing where to check, not a
/// contradiction: the primitive layer is the <b>entry</b> and the reporting layer is <b>downstream</b>. Strict at the
/// entry and lenient downstream is normal, as long as the downstream message does not point the wrong way.**
///
/// **`y` points are computed from each segment's two endpoints, which also checks <b>continuity</b>: segment `i`'s value
/// at `x[i]` must equal segment `i-1`'s value there (`yPoints[i]`).**
///
/// **So a piecewise function that jumps at a breakpoint is rejected — even though each segment is a legal line.**
/// **The native primitive takes only a set of `(x_i, y_i)` points; it has no notion of a per-segment intercept. A
/// discontinuous piecewise function is <b>inexpressible</b> in that representation, so it must be rejected at conversion
/// rather than silently flattened into a polyline.**
///
/// **`maximumMagnitude` uses `abs(value) &lt; maximumMagnitude`, a <b>strict</b> bound, matching `NativeAbs`.**
/// &lt;/remarks>
public static class NativePiecewise
{
    /// &lt;summary>校验并转换 / Validate and convert.&lt;/summary>
    public static Result<NativePiecewiseData, ErrorCode, Error<ErrorCode>> Prepare(
        UnivariateLinearPiecewiseStructure structure,
        double maximumMagnitude = double.PositiveInfinity) {
        ArgumentNullException.ThrowIfNull(structure);

        if (double.IsNaN(maximumMagnitude) || maximumMagnitude <= 0.0) {
            return Invalid("maximum magnitude must be positive");
        }

        IIntoValue<Flt64>? maybeConverter = structure.Converter;
        if (maybeConverter is null) {
            return Invalid("value converter is missing");
        }

        IIntoValue<Flt64> converter = maybeConverter;

        try {
            // **`UnivariateLinearPiecewiseStructure` 没有 `InputVariableKeyOrNull`
            // （那是 `AbsStructure` 上的），所以这里就地判定裸变量。**
            // **The PWL structure has no `InputVariableKeyOrNull` (that lives on `AbsStructure`), so the bare-variable
            // test is done here.**
            if (structure.Input.Monomials.Count != 1) {
                return Invalid("input is not a single variable");
            }

            if (structure.Input.Monomials[0].Symbol is not IVariableItem inputVariable) {
                return Invalid("input symbol is not a variable");
            }

            VariableItemKey inputKey = inputVariable.Key;

            double inputCoefficient =
                converter.FromValue(structure.Input.Monomials[0].Coefficient).ToSolverDouble();
            double inputConstant =
                converter.FromValue(structure.Input.Constant).ToSolverDouble();

            if (!Usable(inputCoefficient, maximumMagnitude) || inputCoefficient != 1.0 ||
                !Usable(inputConstant, maximumMagnitude) || inputConstant != 0.0) {
                return Invalid("input must be x = 1 * variable + 0");
            }

            double[] xPoints = Convert(structure.Breakpoints, converter);
            double[] slopes = Convert(structure.Slopes, converter);
            double[] intercepts = Convert(structure.Intercepts, converter);

            if (xPoints.Length < 2 || slopes.Length != xPoints.Length - 1 ||
                intercepts.Length != slopes.Length) {
                return Invalid("breakpoint, slope, and intercept counts do not match");
            }

            foreach (double[] values in new[] { xPoints, slopes, intercepts }) {
                foreach (double value in values) {
                    if (!Usable(value, maximumMagnitude)) {
                        return Invalid("a non-finite numeric value is present");
                    }
                }
            }

            // **严格递增。`>=` 而不是 `>`：相等也拒，否则两个断点重合会造出零宽分段。**
            // **Strictly increasing, with `>=` rather than `>`: equality is rejected too, otherwise two coincident
            // breakpoints create a zero-width segment.**
            for (int index = 1; index < xPoints.Length; ++index) {
                if (xPoints[index - 1] >= xPoints[index]) {
                    return Invalid("breakpoints must be strictly increasing");
                }
            }

            double[] yPoints = new double[xPoints.Length];
            for (int index = 0; index < slopes.Length; ++index) {
                double lowerValue = (slopes[index] * xPoints[index]) + intercepts[index];
                double upperValue = (slopes[index] * xPoints[index + 1]) + intercepts[index];

                if (!Usable(lowerValue, maximumMagnitude) ||
                    !Usable(upperValue, maximumMagnitude)) {
                    return Invalid("a segment endpoint is not finite");
                }

                // **连续性：第 `i` 段的左端必须等于第 `i-1` 段的右端。**
                // **Continuity: segment `i`'s left end must equal segment `i-1`'s right end.**
                if (index > 0 && lowerValue != yPoints[index]) {
                    return Invalid("piecewise relation is discontinuous");
                }

                if (index == 0) {
                    yPoints[0] = lowerValue;
                }


                yPoints[index + 1] = upperValue;
            }

            return new Ok<NativePiecewiseData, ErrorCode, Error<ErrorCode>>(
                new NativePiecewiseData(
                    InputKey: inputKey,
                    ResultKey: structure.ResultVariable.Key,
                    XPoints: xPoints,
                    YPoints: yPoints,
                    Name: structure.Name));
        }
        catch (Exception error) {
            return Invalid($"numeric conversion failed: {error.Message}");
        }
    }

    private static double[] Convert(
        IReadOnlyList<Flt64> values,
        IIntoValue<Flt64> converter) {
        double[] converted = new double[values.Count];
        for (int index = 0; index < values.Count; ++index) {
            converted[index] = converter.FromValue(values[index]).ToSolverDouble();
        }

        return converted;
    }

    private static bool Usable(double value, double maximumMagnitude) =>
        double.IsFinite(value) && global::System.Math.Abs(value) < maximumMagnitude;

    private static Result<NativePiecewiseData, ErrorCode, Error<ErrorCode>> Invalid(string reason) =>
        new Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, $"invalid native PWL data: {reason}");
}
