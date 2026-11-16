#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **激活算子的取值语义与属性校验** / The activation operators' value semantics and attribute validation.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxActivation.kt` 里"算子算什么"那一半。**为什么先把取值语义单独做出来：**
/// **降阶（把算子接进多项式与函数符号）还没移植，而"这个算子算什么、哪些属性组合是错的"是可以独立验证的。** '
/// **把错判留到降阶层，表现出来只是"模型不可解"，而真实原因是某个 `alpha` 为负。**
///
/// Syncs the "what does this operator compute" half of `OnnxActivation.kt`. **Why the value semantics come first: lowering (wiring an operator into polynomials
/// and function symbols) is not ported yet, while "what this operator computes and which attribute combinations are wrong" can be verified on its own.**
/// **Leaving a bad judgement to the lowering layer shows up only as "the model does not solve", while the real cause is a negative `alpha`.**
/// </remarks>
public static class OnnxActivationSemantics
{
    /// <summary>
    /// `Relu`：`max(0, x)` / Relu: `max(0, x)`.
    /// </summary>
    /// <param name="x">输入 / The input.</param>
    /// <returns>输出 / The output.</returns>
    public static double Relu(double x) => x > 0.0 ? x : 0.0;

    /// <summary>
    /// `LeakyRelu`：`x >= 0 ? x : alpha·x` / LeakyRelu.
    /// </summary>
    /// <param name="x">输入 / The input.</param>
    /// <param name="alpha">负半轴斜率 / The negative-side slope.</param>
    /// <returns>输出 / The output.</returns>
    public static double LeakyRelu(double x, double alpha) => x >= 0.0 ? x : alpha * x;

    /// <summary>
    /// `Clip`：`min(max(x, lower), upper)` / Clip.
    /// </summary>
    /// <param name="x">输入 / The input.</param>
    /// <param name="lower">下界（null 表示不截断）/ The lower bound, null for no clamp.</param>
    /// <param name="upper">上界（null 表示不截断）/ The upper bound, null for no clamp.</param>
    /// <returns>输出 / The output.</returns>
    public static double Clip(double x, double? lower, double? upper) {
        double value = x;
        if (lower is double low && value < low) {
            value = low;
        }
        if (upper is double high && value > high) {
            value = high;
        }
        return value;
    }

    /// <summary>
    /// `HardSigmoid`：`clamp(alpha·x + beta, 0, 1)` / HardSigmoid.
    /// </summary>
    /// <param name="x">输入 / The input.</param>
    /// <param name="alpha">斜率 / The slope.</param>
    /// <param name="beta">偏移 / The offset.</param>
    /// <returns>输出 / The output.</returns>
    public static double HardSigmoid(double x, double alpha, double beta) {
        double value = alpha * x + beta;
        if (value < 0.0) {
            return 0.0;
        }
        return value > 1.0 ? 1.0 : value;
    }

    /// <summary>
    /// `Sigmoid`：`1 / (1 + e^-x)` / Sigmoid.
    /// </summary>
    /// <param name="x">输入 / The input.</param>
    /// <returns>输出 / The output.</returns>
    public static double Sigmoid(double x) => 1.0 / (1.0 + System.Math.Exp(-x));

    /// <summary>
    /// `Softplus`：`ln(1 + e^x)` / Softplus.
    /// </summary>
    /// <param name="x">输入 / The input.</param>
    /// <returns>输出 / The output.</returns>
    /// <remarks>
    /// **直接写 `ln(1 + e^x)` 在 `x` 稍大时就溢出成 `∞`：** **`e^710` 已经是 `double` 的上限，而模型里的激活输入常常超过这个数。**
    /// **所以用等价形式 `max(x, 0) + ln(1 + e^-|x|)`——**它在整个实数轴上稳定，而结果与定义完全相同。**
    ///
    /// **Writing `ln(1 + e^x)` directly overflows to infinity once `x` is modest: `e^710` is already the `double` limit, and activation inputs often exceed it.**
    /// **The equivalent `max(x, 0) + ln(1 + e^-|x|)` is stable across the whole real line and computes exactly the same thing.**
    /// </remarks>
    public static double Softplus(double x) {
        double shifted = System.Math.Abs(x);
        double tail = System.Math.Log(1.0 + System.Math.Exp(-shifted));
        return (x > 0.0 ? x : 0.0) + tail;
    }

    /// <summary>
    /// `Elu`：`x >= 0 ? x : alpha·(e^x - 1)` / Elu.
    /// </summary>
    /// <param name="x">输入 / The input.</param>
    /// <param name="alpha">负半轴系数 / The negative-side coefficient.</param>
    /// <returns>输出 / The output.</returns>
    public static double Elu(double x, double alpha) => x >= 0.0 ? x : alpha * (System.Math.Exp(x) - 1.0);

    /// <summary>
    /// 属性校验结果 / The attribute validation result.
    /// </summary>
    /// <param name="Failure">失败原因，合法时为 null / The failure reason, null when valid.</param>
    /// <param name="Alpha">生效的 `alpha` / The effective `alpha`.</param>
    /// <param name="Beta">生效的 `beta` / The effective `beta`.</param>
    public sealed record AttributeCheck(string? Failure, double? Alpha, double? Beta);

    /// <summary>
    /// 校验一个激活节点的属性 / Validate one activation node's attributes.
    /// </summary>
    /// <param name="opType">算子类型 / The operator type.</param>
    /// <param name="alpha">节点给出的 `alpha`（可空）/ The node's `alpha`, if any.</param>
    /// <param name="beta">节点给出的 `beta`（可空）/ The node's `beta`, if any.</param>
    /// <param name="lower">`Clip` 的下界 / Clip's lower bound.</param>
    /// <param name="upper">`Clip` 的上界 / Clip's upper bound.</param>
    /// <returns>校验结果 / The check result.</returns>
    /// <remarks>
    /// **默认值不是可有可无的装饰：** **`LeakyRelu` 的 `alpha` 默认 0.01、`HardSigmoid` 的 `alpha`/`beta` 默认 0.2/0.5，**
    /// **都是 ONNX 规范里写死的。** **漏掉默认值会让"没写属性的节点"被当成 `alpha = 0`，** **即整条负半轴被压成零——**
    /// **而那看起来就像模型自己学出来的。**
    ///
    /// **The defaults are not decoration: LeakyRelu's `alpha` defaults to 0.01 and HardSigmoid's to 0.2/0.5, both fixed by the specification.** **Dropping them
    /// would turn a node without attributes into `alpha = 0`, flattening the whole negative half to zero — which looks like something the model learned.**
    /// </remarks>
    public static AttributeCheck CheckAttributes(
        string opType,
        double? alpha = null,
        double? beta = null,
        double? lower = null,
        double? upper = null) {
        switch (opType) {
            case "LeakyRelu": {
                double effective = alpha ?? 0.01;
                if (effective < 0.0) {
                    return new AttributeCheck("LeakyRelu 的 alpha 不得为负 / LeakyRelu alpha must not be negative", null, null);
                }
                return new AttributeCheck(null, effective, null);
            }
            case "HardSigmoid": {
                double effectiveAlpha = alpha ?? 0.2;
                double effectiveBeta = beta ?? 0.5;
                return new AttributeCheck(null, effectiveAlpha, effectiveBeta);
            }
            case "Elu": {
                double effective = alpha ?? 1.0;
                return new AttributeCheck(null, effective, null);
            }
            case "Clip": {
                if (lower is double low && upper is double high && low > high) {
                    return new AttributeCheck(
                        "Clip 下界 " + low + " 大于上界 " + high + " / Clip lower bound exceeds the upper bound",
                        null,
                        null);
                }
                return new AttributeCheck(null, null, null);
            }
            case "Relu":
            case "Sigmoid":
            case "Tanh":
            case "Softplus":
                return new AttributeCheck(null, null, null);
            default:
                return new AttributeCheck("不支持的激活算子 / unsupported activation operator: " + opType, null, null);
        }
    }
}