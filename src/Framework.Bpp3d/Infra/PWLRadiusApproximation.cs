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
    /// 从半径区间创建 PWL 近似。
    /// Creates PWL approximation from a radius interval.
    /// </summary>
    public static PwlRadiusSquaredApproximation FromRadiusInterval(
        double rMin, double rMax,
        PwlRadiusApproximationConfig? config = null) {

        config ??= PwlRadiusApproximationConfig.Default;
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
