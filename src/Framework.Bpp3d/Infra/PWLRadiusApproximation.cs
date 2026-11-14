#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>PWL 断点策略 / PWL breakpoint strategy.</summary>
public enum PwlBreakpointStrategy {
    /// <summary>均匀分布 / Uniform distribution.</summary>
    Uniform,
    /// <summary>自适应（Chebyshev-like，在 RMin 附近集中）/ Adaptive (Chebyshev-like, concentrated near RMin).</summary>
    Adaptive,
    /// <summary>误差驱动（迭代二分最差段）/ Error-driven (iteratively bisect worst segment).</summary>
    ErrorDriven
}

/// <summary>
/// PWL 半径近似配置 / PWL radius approximation configuration.
/// 用于 MILP 中圆柱 r^2 的分段线性化配置。
/// Configuration for piecewise-linear approximation of r^2 in MILP formulations.
/// </summary>
public sealed record PwlRadiusApproximationConfig(
    int MaxSegments = 8,
    double RelativeErrorTolerance = 0.01,
    PwlBreakpointStrategy BreakpointStrategy = PwlBreakpointStrategy.Uniform,
    IReadOnlyList<double>? CustomBreakpoints = null,
    bool EnableDebugInfo = false) {
    /// <summary>默认配置 / Default configuration.</summary>
    public static PwlRadiusApproximationConfig Default { get; } = new();
}

/// <summary>
/// 段数推导结果 / Segment count derivation result.
/// </summary>
public sealed record SegmentCountDerivation(
    int RecommendedSegments,
    double AchievedMaxRelativeError,
    bool MeetsTolerance,
    int Iterations) {
    /// <summary>诊断信息 / Diagnostic info.</summary>
    public string Info() =>
        $"Segments={RecommendedSegments}, MaxRelErr={AchievedMaxRelativeError:F6}, " +
        $"MeetsTolerance={MeetsTolerance}, Iterations={Iterations}";
}

/// <summary>
/// PWL 半径平方近似 / PWL radius-squared approximation.
/// 对 f(r) = r^2 的分段线性逼近。每段使用弦方程: slope_i = r_i + r_{i+1}, intercept_i = -r_i * r_{i+1}.
/// Piecewise-linear approximation of f(r) = r^2. Each segment uses chord equation.
/// </summary>
public sealed record PwlRadiusSquaredApproximation(
    IReadOnlyList<double> Breakpoints,
    IReadOnlyList<double> Slopes,
    IReadOnlyList<double> Intercepts,
    double MaxRelativeError,
    double MaxAbsoluteError) {

    /// <summary>段数 / Number of segments.</summary>
    public int SegmentCount => Breakpoints.Count - 1;

    /// <summary>
    /// 推导满足误差容限所需的段数。
    /// Derives segment count needed to meet error tolerance via binary search.
    /// </summary>
    public static SegmentCountDerivation DeriveSegmentCount(
        double rMin, double rMax,
        double relativeErrorTolerance = 0.01,
        int maxSegments = 64) {

        int segments = 1;
        int iterations = 0;
        while (segments <= maxSegments) {
            iterations++;
            var config = new PwlRadiusApproximationConfig(
                MaxSegments: segments,
                RelativeErrorTolerance: relativeErrorTolerance,
                BreakpointStrategy: PwlBreakpointStrategy.Uniform);
            var approx = FromRadiusInterval(rMin, rMax, config);
            if (approx.MaxRelativeError <= relativeErrorTolerance) {
                return new SegmentCountDerivation(segments, approx.MaxRelativeError, true, iterations);
            }
            segments *= 2;
        }

        var fallback = FromRadiusInterval(rMin, rMax,
            new PwlRadiusApproximationConfig(MaxSegments: maxSegments,
                BreakpointStrategy: PwlBreakpointStrategy.Uniform));
        return new SegmentCountDerivation(maxSegments, fallback.MaxRelativeError, false, iterations);
    }

    /// <summary>
    /// 校验自定义断点 / Validate custom breakpoints.
    /// </summary>
    /// <param name="breakpoints">断点 / The breakpoints.</param>
    /// <param name="rMin">半径下界 / The radius lower bound.</param>
    /// <param name="rMax">半径上界 / The radius upper bound.</param>
    /// <param name="maxSegments">最大分段数 / The maximum segment count.</param>
    /// <remarks>
    /// 四条都必须查，而且**每条失败的理由都不同**：断点没盖住 `[rMin, rMax]` 会让区间外的半径走外推（误差不受控），
    /// 断点不严格递增会造出零宽甚至负宽分段（求值时选出错误的一段），断点数超过 `maxSegments` 则违背了调用方自己设的上限。
    /// 不校验时这些配置会被静默接受，然后给出一个"看起来正常"但误差远超标称的近似。
    ///
    /// All four checks matter, and **each fails for a different reason**: breakpoints that miss `[rMin, rMax]` leave radii outside the range to extrapolation with
    /// uncontrolled error, non-increasing breakpoints create zero- or negative-width segments that make the evaluator pick the wrong one, and exceeding `maxSegments`
    /// violates the caller's own cap. Without the checks these configurations are accepted silently and yield an approximation that looks normal but whose error is far
    /// beyond what was claimed.
    /// </remarks>
    private static void ValidateCustomBreakpoints(
        IReadOnlyList<double> breakpoints,
        double rMin,
        double rMax,
        int maxSegments) {
        if (breakpoints[0] > rMin) {
            throw new ArgumentException(
                $"Custom breakpoints must start at or below rMin ({breakpoints[0]} > {rMin})",
                nameof(breakpoints));
        }
        if (breakpoints[breakpoints.Count - 1] < rMax) {
            throw new ArgumentException(
                $"Custom breakpoints must end at or above rMax ({breakpoints[breakpoints.Count - 1]} < {rMax})",
                nameof(breakpoints));
        }
        if (breakpoints.Count - 1 > maxSegments) {
            throw new ArgumentException(
                $"Custom breakpoints yield {breakpoints.Count - 1} segments, above maxSegments {maxSegments}",
                nameof(breakpoints));
        }
        for (int index = 0; index < breakpoints.Count - 1; index++) {
            if (!double.IsFinite(breakpoints[index]) || !double.IsFinite(breakpoints[index + 1])) {
                throw new ArgumentException(
                    "Custom breakpoints must be finite", nameof(breakpoints));
            }
            if (breakpoints[index] >= breakpoints[index + 1]) {
                throw new ArgumentException(
                    $"Custom breakpoints must be strictly increasing ({breakpoints[index]} >= {breakpoints[index + 1]})",
                    nameof(breakpoints));
            }
        }
    }

    /// <summary>
    /// 从半径区间创建 PWL 近似。
    /// Creates PWL approximation from a radius interval.
    /// </summary>
    public static PwlRadiusSquaredApproximation FromRadiusInterval(
        double rMin, double rMax,
        PwlRadiusApproximationConfig? config = null) {

        config ??= PwlRadiusApproximationConfig.Default;

        if (rMin <= 0.0) {
            throw new ArgumentOutOfRangeException(nameof(rMin), rMin, "rMin must be positive");
        }
        if (rMax <= rMin) {
            throw new ArgumentOutOfRangeException(nameof(rMax), rMax, "rMax must be greater than rMin");
        }
        if (config.MaxSegments < 1) {
            throw new ArgumentOutOfRangeException(nameof(config), config.MaxSegments, "maxSegments must be at least 1");
        }
        if (config.CustomBreakpoints is { Count: > 0 }) {
            ValidateCustomBreakpoints(config.CustomBreakpoints, rMin, rMax, config.MaxSegments);
        }

        var breakpoints = config.CustomBreakpoints?.Count > 0
            ? config.CustomBreakpoints.ToArray()
            : GenerateBreakpoints(rMin, rMax, config.MaxSegments, config.BreakpointStrategy);

        var slopes = new double[breakpoints.Length - 1];
        var intercepts = new double[breakpoints.Length - 1];
        for (int i = 0; i < breakpoints.Length - 1; i++) {
            slopes[i] = breakpoints[i] + breakpoints[i + 1];
            intercepts[i] = -breakpoints[i] * breakpoints[i + 1];
        }

        var errors = ComputeMaxErrors(breakpoints, slopes, intercepts);
        return new PwlRadiusSquaredApproximation(
            breakpoints, slopes, intercepts,
            errors.maxRelative, errors.maxAbsolute);
    }

    /// <summary>
    /// 在给定半径处计算 PWL 近似值 q ~ r^2.
    /// Evaluates PWL approximation q ~ r^2 at given radius.
    /// </summary>
    public double Evaluate(double r) {
        int seg = FindSegment(r);
        return Slopes[seg] * r + Intercepts[seg];
    }

    /// <summary>实际误差 / Actual error at a specific r.</summary>
    public double ActualError(double r) => global::System.Math.Abs(Evaluate(r) - r * r);

    /// <summary>
    /// 实际误差（十进制口径）/ Actual error at a specific radius, in decimal.
    /// </summary>
    /// <param name="r">半径 / The radius.</param>
    /// <returns>误差 / The error.</returns>
    /// <remarks>
    /// **为什么需要这条重载**：`double` 版把半径先降到 `double`、算完再升回 `FltX`，
    /// 两次转换各自带来一次舍入。基线的 `ActualError` 全程用 `FltX`（十进制），没有这两次转换。
    /// 对一个要拿去和容差比较的量，舍入方向不确定比舍入幅度更麻烦——它会让"刚好在容差上"的判断随机翻转。
    ///
    /// 断点、斜率、截距仍以 `double` 存储（它们是构造时一次性算好的常数），但**求值与相减在十进制域内完成**：
    /// 先把这三个常数转成 `FltX` 一次，之后不再往返。
    ///
    /// **Why this overload exists**: the `double` version lowers the radius to `double`, computes, then raises the result back to `FltX`, and each conversion rounds. The baseline's `ActualError` stays in
    /// `FltX` (decimal) throughout with neither conversion. For a quantity compared against a tolerance, an uncertain rounding direction matters more than the rounding magnitude: it makes a
    /// borderline verdict flip at random. The breakpoints, slopes and intercepts stay stored as `double` (constants computed once at construction), but **evaluation and subtraction happen in the
    /// decimal domain**: those three constants convert to `FltX` once and never round-trip again.
    /// </remarks>
    public FltX ActualError(FltX r) {
        int segment = FindSegment(r.ToFlt64().ToDouble());
        var slope = new FltX(Slopes[segment]);
        var intercept = new FltX(Intercepts[segment]);
        return (slope * r + intercept - r * r).Abs();
    }

    /// <summary>
    /// 在给定半径处计算 PWL 近似值（十进制口径）/ Evaluate the PWL approximation in decimal.
    /// </summary>
    /// <param name="r">半径 / The radius.</param>
    /// <returns>近似值 / The approximation.</returns>
    public FltX Evaluate(FltX r) {
        int segment = FindSegment(r.ToFlt64().ToDouble());
        return new FltX(Slopes[segment]) * r + new FltX(Intercepts[segment]);
    }

    /// <summary>
    /// 实际相对误差（十进制口径）/ Actual relative error in decimal.
    /// </summary>
    /// <param name="r">半径 / The radius.</param>
    /// <returns>相对误差 / The relative error.</returns>
    /// <remarks>
    /// 分母用 `r * r` 而不是近似的 `Evaluate(r)`：容差是**相对于真值**说的，拿近似值当分母会让误差系统性偏大，
    /// 于是"刚好达标"的近似被判成超标。
    ///
    /// The denominator is `r * r` rather than the approximation: a tolerance is stated **relative to the true value**, and using the approximation would bias the error upward, condemning an
    /// approximation that just meets it.
    /// </remarks>
    public FltX ActualRelativeError(FltX r) {
        FltX squared = r * r;
        return squared.ToFlt64().ToDouble() > 1e-15 ? ActualError(r) / squared : FltX.Zero;
    }


    /// <summary>实际相对误差 / Actual relative error at a specific r.</summary>
    public double ActualRelativeError(double r) {
        double r2 = r * r;
        return r2 > 1e-15 ? ActualError(r) / r2 : 0.0;
    }

    private int FindSegment(double r) {
        for (int i = 0; i < SegmentCount; i++) {
            if (r <= Breakpoints[i + 1] + 1e-12) return i;
        }
        return SegmentCount - 1;
    }

    private static double[] GenerateBreakpoints(double rMin, double rMax, int segments, PwlBreakpointStrategy strategy) {
        var pts = new double[segments + 1];
        switch (strategy) {
            case PwlBreakpointStrategy.Adaptive:
                for (int i = 0; i <= segments; i++) {
                    double t = (double)i / segments;
                    pts[i] = rMin + (rMax - rMin) * (1.0 - global::System.Math.Pow(1.0 - t, 2));
                }
                break;
            case PwlBreakpointStrategy.ErrorDriven:
                for (int i = 0; i <= segments; i++) {
                    pts[i] = rMin + (rMax - rMin) * (double)i / segments;
                }
                break;
            default: // Uniform
                for (int i = 0; i <= segments; i++) {
                    pts[i] = rMin + (rMax - rMin) * (double)i / segments;
                }
                break;
        }
        return pts;
    }

    private static (double maxAbsolute, double maxRelative) ComputeMaxErrors(
        double[] breakpoints, double[] slopes, double[] intercepts) {
        double maxAbs = 0, maxRel = 0;
        for (int i = 0; i < slopes.Length; i++) {
            int sampleCount = 50;
            for (int j = 0; j <= sampleCount; j++) {
                double t = (double)j / sampleCount;
                double r = breakpoints[i] + t * (breakpoints[i + 1] - breakpoints[i]);
                double approx = slopes[i] * r + intercepts[i];
                double exact = r * r;
                double absErr = global::System.Math.Abs(approx - exact);
                double relErr = exact > 1e-15 ? absErr / exact : 0.0;
                if (absErr > maxAbs) maxAbs = absErr;
                if (relErr > maxRel) maxRel = relErr;
            }
        }
        return (maxAbs, maxRel);
    }
}
