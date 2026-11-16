#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **四个近似激活算子的降阶** / Lowering for the four approximated activations.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxActivation` 近似那一半（`Sigmoid`／`Tanh`／`Softplus`／`Elu`）。**这一步把 B1320 的切分策略接到 C# 侧的
/// `UnivariateLinearPiecewiseFunction` 上。**
/// **契约有三条，每条漏掉都只会在求解阶段表现为"结果不对"：**
/// **① 断点必须严格递增；② 段数必须等于「断点数 − 1」；③ 边界必须真的有限。**
///
/// Syncs the approximated half of `OnnxActivation`. **This wires the B1320 segmentation into `UnivariateLinearPiecewiseFunction`. Three contracts, each of which shows
/// up only as "the answer is wrong" once solved: breakpoints strictly increasing, segment count equal to breakpoints minus one, and bounds actually finite.**
/// </remarks>
public static class OnnxApproximationLowering
{
    /// <summary>
    /// 降阶结果 / The lowering result.
    /// </summary>
    /// <param name="Pieces">切分结果 / The segmentation.</param>
    /// <param name="Function">分段线性函数符号 / The piecewise function symbol.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    /// <remarks>
    /// **`Pieces` 与 `Function` 分开：** **切分结果在任何情况下都算出来了、可以直接验；** **而函数符号的构造要求输入符号已经注册有限界，**
    /// **那是模型层的前提（`RealVar` 默认是 ±`double.MaxValue`，形式有限、实际无界）。** **把两者绑成一个返回值会让"构造失败"掩盖"切分是否正确"。**
    ///
    /// **`Pieces` and `Function` are separate: the segmentation is computed either way and can be verified directly, while constructing the function symbol requires the
    /// input symbols to carry finite bounds — a model-layer precondition (`RealVar` defaults to ±`double.MaxValue`, formally finite and practically unbounded). Tying
    /// them into one return value would let "construction failed" hide "was the segmentation right".**
    /// </remarks>
    public sealed record Lowering(
        OnnxPiecewiseApproximation.Approximation? Pieces,
        UnivariateLinearPiecewiseFunction<Flt64>? Function,
        string? Failure);

    /// <summary>
    /// 取一个近似算子的数学函数 / The mathematical function of one approximated operator.
    /// </summary>
    /// <param name="opType">算子类型 / The operator type.</param>
    /// <param name="alpha">`Elu` 的系数 / Elu's coefficient.</param>
    /// <returns>函数；未知算子为 null / The function, null for an unknown operator.</returns>
    public static Func<double, double>? FunctionOf(string opType, double alpha = 1.0) => opType switch {
        "Sigmoid" => OnnxActivationSemantics.Sigmoid,
        "Tanh" => System.Math.Tanh,
        "Softplus" => OnnxActivationSemantics.Softplus,
        "Elu" => x => OnnxActivationSemantics.Elu(x, alpha),
        _ => null,
    };

    /// <summary>
    /// 把近似算子降阶成一个分段线性函数符号 / Lower an approximated operator into a piecewise function symbol.
    /// </summary>
    /// <param name="opType">算子类型 / The operator type.</param>
    /// <param name="input">输入表达式 / The input expression.</param>
    /// <param name="lower">输入下界 / The input's lower bound.</param>
    /// <param name="upper">输入上界 / The input's upper bound.</param>
    /// <param name="segments">段数 / The segment count.</param>
    /// <param name="alpha">`Elu` 的系数 / Elu's coefficient.</param>
    /// <returns>降阶结果 / The result.</returns>
    public static Lowering Lower(
        string opType,
        LinearPolynomial<Flt64> input,
        double lower,
        double upper,
        int segments = OnnxPiecewiseApproximation.DefaultSegments,
        double alpha = 1.0) {
        Func<double, double>? function = FunctionOf(opType, alpha);
        if (function is null) {
            return new Lowering(null, null, "不支持近似降阶的算子 '" + opType + "' / not an approximated operator");
        }

        OnnxPiecewiseApproximation.Result approximation = OnnxPiecewiseApproximation.Approximate(function, lower, upper, segments);
        if (approximation.Value is not OnnxPiecewiseApproximation.Approximation pieces) {
            return new Lowering(null, null, approximation.Failure);
        }

        // 契约 ①：断点严格递增。切分策略算出来的断点在退化区间上会相等，而相等断点会让某段长度为零。
        // Contract one: breakpoints strictly increase. The segmentation yields equal breakpoints on a degenerate range, and a zero-width segment follows.
        for (int index = 1; index < pieces.Breakpoints.Count; ++index) {
            if (pieces.Breakpoints[index] <= pieces.Breakpoints[index - 1]) {
                return new Lowering(null, null, "断点未严格递增（第 " + index + " 个）/ the breakpoints are not strictly increasing");
            }
        }

        // 契约 ②：段数 = 断点数 − 1。多一段或少一段都会让斜率与区间错配，而那个错配在求解后只表现为"值偏了"。
        // Contract two: segments = breakpoints - 1. One more or one fewer misaligns slopes and intervals, and the misalignment only shows as "the value drifted".
        if (pieces.Slopes.Count != pieces.Breakpoints.Count - 1 || pieces.Intercepts.Count != pieces.Slopes.Count) {
            return new Lowering(null, null, "段数与断点数不匹配 / the segment count disagrees with the breakpoints");
        }

        var breakpoints = new Flt64[pieces.Breakpoints.Count];
        var slopes = new Flt64[pieces.Slopes.Count];
        var intercepts = new Flt64[pieces.Intercepts.Count];
        for (int index = 0; index < breakpoints.Length; ++index) {
            breakpoints[index] = new Flt64(pieces.Breakpoints[index]);
        }
        for (int index = 0; index < slopes.Length; ++index) {
            slopes[index] = new Flt64(pieces.Slopes[index]);
            intercepts[index] = new Flt64(pieces.Intercepts[index]);
        }

        Result<UnivariateLinearPiecewiseFunction<Flt64>, ErrorCode, Error<ErrorCode>> created =
            UnivariateLinearPiecewiseFunction<Flt64>.Create(
                input: input,
                breakpoints: breakpoints,
                slopes: slopes,
                intercepts: intercepts,
                converter: IIntoValue<Flt64>.Identity,
                name: "onnx_" + opType);

        if (!created.IsOk) {
            return new Lowering(pieces, null, "分段线性函数构造失败 / the piecewise function could not be constructed (the input symbols need finite bounds)");
        }
        return new Lowering(pieces, created.Value, null);
    }
}