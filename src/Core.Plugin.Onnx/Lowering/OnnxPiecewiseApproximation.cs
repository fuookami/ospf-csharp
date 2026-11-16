#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **分段线性近似的切分策略** / The segmentation policy for piecewise-linear approximation.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxActivation.kt` 里"分段近似"那一半。**降阶本身（把节点接进多项式与中间符号）依赖的是一整套模型机制，
/// 还没移植；**但切分策略是独立可验的数学**，而它恰好也是**最容易静默出错的地方**：** 段数、边界与斜率算法之一写错，
/// 得到的近似仍然"看起来像 Sigmoid"，只是偏离了真实曲线。**
///
/// Syncs the piecewise-approximation half of `OnnxActivation.kt`. **Lowering a node into polynomials and intermediate symbols needs the whole model machinery,
/// which is not ported yet; the segmentation policy, however, is independently verifiable mathematics — and exactly where a silent error lives: a wrong segment
/// count, bound or slope formula still looks like a sigmoid, it just drifts from the true curve.**
/// </remarks>
public static class OnnxPiecewiseApproximation
{
    /// <summary>默认分段数 / The default segment count.</summary>
    public const int DefaultSegments = 8;

    /// <summary>允许的分段数下界 / The lowest allowed segment count.</summary>
    public const int MinSegments = 2;

    /// <summary>允许的分段数上界 / The highest allowed segment count.</summary>
    public const int MaxSegments = 1024;

    /// <summary>
    /// 分段近似允许的最大边界绝对值 / The largest usable absolute bound.
    /// </summary>
    /// <remarks>
    /// **这不是"越大越好"的门槛，而是"形式有限、实际无界"的识别器：** **变量的默认范围是 ±`double.MaxValue` 这种**
    /// **区间——对它切分会得到一条毫无意义的近似曲线，**而那条曲线依然能求解、依然返回"最优解"。**
    ///
    /// **This is not a "bigger is better" threshold but a recogniser for "formally finite, practically unbounded": the default variable range is a
    /// ±`double.MaxValue` interval, and splitting it produces a meaningless approximation curve — which still solves and still returns an "optimum".**
    /// </remarks>
    public const double MaxPiecewiseBound = 1.0e6;

    /// <summary>
    /// 一个分段线性近似式 / One piecewise-linear approximation.
    /// </summary>
    /// <param name="Breakpoints">断点 / The breakpoints.</param>
    /// <param name="Slopes">每段斜率 / The slope of each segment.</param>
    /// <param name="Intercepts">每段截距 / The intercept of each segment.</param>
    public sealed record Approximation(
        IReadOnlyList<double> Breakpoints,
        IReadOnlyList<double> Slopes,
        IReadOnlyList<double> Intercepts)
    {
        /// <summary>
        /// 在 `x` 处取近似值 / Evaluate the approximation at `x`.
        /// </summary>
        /// <param name="x">自变量 / The argument.</param>
        /// <returns>近似值 / The approximation.</returns>
        /// <remarks>
        /// **区间外按端点段外推，不夹紧：** **模型里 `x` 的取值范围由约束保证，夹紧会掩盖"约束没兜住"这件事——**
        /// **而外推至少让数值随 `x` 变化，错误在解上看得出。**
        ///
        /// **Outside the range the end segment extrapolates rather than clamping: the model's constraints are supposed to bound `x`, and clamping would hide the
        /// case where they do not — extrapolation at least keeps the value moving with `x`, so the error is visible in the solution.**
        /// </remarks>
        public double Evaluate(double x) {
            if (Slopes.Count == 0) {
                throw new InvalidOperationException("空近似不可取值 / an empty approximation has no value");
            }
            int segment = Slopes.Count - 1;
            for (int index = 0; index < Slopes.Count; ++index) {
                if (x <= Breakpoints[index + 1]) {
                    segment = index;
                    break;
                }
            }
            return Slopes[segment] * x + Intercepts[segment];
        }
    }

    /// <summary>
    /// 结果：近似式或失败原因 / The result: an approximation or a failure reason.
    /// </summary>
    /// <param name="Value">近似式 / The approximation.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Result(Approximation? Value, string? Failure);

    /// <summary>
    /// 把一元函数切成 `segments` 段线性近似 / Split a univariate function into `segments` linear pieces.
    /// </summary>
    /// <param name="function">被近似函数 / The approximated function.</param>
    /// <param name="lower">下界 / The lower bound.</param>
    /// <param name="upper">上界 / The upper bound.</param>
    /// <param name="segments">段数 / The segment count.</param>
    /// <returns>近似式或失败原因 / The approximation or a failure reason.</returns>
    public static Result Approximate(Func<double, double> function, double lower, double upper, int segments = DefaultSegments) {
        if (segments < MinSegments || segments > MaxSegments) {
            return new Result(null, "分段数 " + segments + " 超出允许区间 [" + MinSegments + ", " + MaxSegments + "] / segment count out of range");
        }
        if (double.IsNaN(lower) || double.IsNaN(upper) || double.IsInfinity(lower) || double.IsInfinity(upper)) {
            return new Result(null, "边界不是有限值 / bounds are not finite");
        }
        if (lower > upper) {
            return new Result(null, "下界大于上界 / the lower bound exceeds the upper bound");
        }
        if (System.Math.Abs(lower) > MaxPiecewiseBound || System.Math.Abs(upper) > MaxPiecewiseBound) {
            return new Result(
                null,
                "边界超过分段近似的可识别上限 " + MaxPiecewiseBound
                + "，按缺少有效范围处理 / bound exceeds the piecewise limit and is treated as missing");
        }
        if (lower == upper) {
            // 常量输入塌缩成一个常数：零斜率、单断点。 / A constant input collapses: zero slope, a single breakpoint.
            double constant = function(lower);
            return new Result(new Approximation(
                new[] { lower, upper },
                new[] { 0.0 },
                new[] { constant }), null);
        }

        double[] breakpoints = new double[segments + 1];
        for (int index = 0; index <= segments; ++index) {
            breakpoints[index] = lower + (upper - lower) * index / segments;
        }
        double[] values = new double[segments + 1];
        for (int index = 0; index <= segments; ++index) {
            values[index] = function(breakpoints[index]);
        }
        double[] slopes = new double[segments];
        double[] intercepts = new double[segments];
        for (int index = 0; index < segments; ++index) {
            slopes[index] = (values[index + 1] - values[index]) / (breakpoints[index + 1] - breakpoints[index]);
            intercepts[index] = values[index] - slopes[index] * breakpoints[index];
        }
        return new Result(new Approximation(breakpoints, slopes, intercepts), null);
    }

    /// <summary>**需要分段近似的算子** / The operators requiring piecewise approximation.</summary>
    public static readonly IReadOnlySet<string> Approximated = new HashSet<string> {
        "Sigmoid", "Tanh", "Softplus", "Elu",
    };

    /// <summary>**激活算子集合** / The supported activation operators.</summary>
    public static readonly IReadOnlySet<string> Operators = new HashSet<string> {
        "Relu", "Clip", "LeakyRelu", "HardSigmoid", "Sigmoid", "Tanh", "Softplus", "Elu",
    };
}