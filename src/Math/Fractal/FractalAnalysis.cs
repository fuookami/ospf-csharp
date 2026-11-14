#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Math.Fractal;

/// <summary>分形逃逸时间与维数分析 / Fractal escape-time and dimension analysis.</summary>
public static class FractalAnalysis {
    /// <summary>
    /// 计算 z -> z^2 + c 的逃逸时间 / Compute escape time for z -> z^2 + c.
    /// </summary>
    public static Result<UInt64, ErrorCode, Error<ErrorCode>> EscapeTime(
        Point<Dim2, Flt64> z,
        Point<Dim2, Flt64> c,
        UInt64? maxIterations = null,
        Flt64? bailoutRadius = null) {
        UInt64 limit = maxIterations ?? new UInt64(100);
        double radius = bailoutRadius?.ToDouble() ?? 2.0;
        if (limit.ToULong() == 0UL) {
            return Failed<UInt64>("最大迭代次数必须为正 / maxIterations must be positive");
        }
        if (radius <= 0.0) {
            return Failed<UInt64>("逃逸半径必须为正 / bailoutRadius must be positive");
        }

        double bailoutSquared = radius * radius;
        double x = z[0].ToDouble();
        double y = z[1].ToDouble();
        double cx = c[0].ToDouble();
        double cy = c[1].ToDouble();

        UInt64 iterations = UInt64.Zero;
        while (iterations.Ls(limit)) {
            double xSquared = x * x;
            double ySquared = y * y;
            if (xSquared + ySquared > bailoutSquared) {
                break;
            }

            double nextX = xSquared - ySquared + cx;
            double nextY = 2.0 * x * y + cy;
            x = nextX;
            y = nextY;
            iterations++;
        }
        return Results.Ok(iterations);
    }

    /// <summary>使用盒计数法估计二维点集维数 / Estimate a 2D point-set dimension by box counting.</summary>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> BoxCountingDimension(
        IReadOnlyList<Point<Dim2, Flt64>> points,
        IReadOnlyList<int>? scales = null) {
        if (points.Count < 2) {
            return Failed<Flt64>("至少需要两个点 / at least two points are required");
        }

        IReadOnlyList<int> effectiveScales = scales ?? new[] { 2, 4, 8, 16, 32 };
        if (effectiveScales.Count < 2) {
            return Failed<Flt64>("网格划分数需至少两个不小于 2 的值 / scales must contain at least two values >= 2");
        }
        for (int index = 0; index < effectiveScales.Count; index++) {
            if (effectiveScales[index] < 2) {
                return Failed<Flt64>("网格划分数需至少两个不小于 2 的值 / scales must contain at least two values >= 2");
            }
        }

        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;
        for (int index = 0; index < points.Count; index++) {
            double x = points[index][0].ToDouble();
            double y = points[index][1].ToDouble();
            minX = System.Math.Min(minX, x);
            maxX = System.Math.Max(maxX, x);
            minY = System.Math.Min(minY, y);
            maxY = System.Math.Max(maxY, y);
        }

        double width = maxX - minX;
        double height = maxY - minY;
        if (width <= 0.0 && height <= 0.0) {
            return Failed<Flt64>("点集退化，无法估计维数 / points are degenerate");
        }

        var logEpsilons = new List<double>(effectiveScales.Count);
        var logCounts = new List<double>(effectiveScales.Count);
        for (int scaleIndex = 0; scaleIndex < effectiveScales.Count; scaleIndex++) {
            int scale = effectiveScales[scaleIndex];
            var occupied = new HashSet<long>();
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) {
                int ix = width <= 0.0
                    ? 0
                    : System.Math.Clamp(
                        (int)(((points[pointIndex][0].ToDouble() - minX) / width) * scale),
                        0,
                        scale - 1);
                int iy = height <= 0.0
                    ? 0
                    : System.Math.Clamp(
                        (int)(((points[pointIndex][1].ToDouble() - minY) / height) * scale),
                        0,
                        scale - 1);
                occupied.Add((long)iy * scale + ix);
            }

            if (occupied.Count == 0) {
                continue;
            }
            logEpsilons.Add(System.Math.Log(1.0 / scale));
            logCounts.Add(System.Math.Log(occupied.Count));
        }

        double? slope = LeastSquaresSlope(logEpsilons, logCounts);
        return slope is null
            ? Failed<Flt64>("网格划分数不足，无法拟合维数 / not enough scales to fit the dimension")
            : Results.Ok(new Flt64(-slope.Value));
    }

    /// <summary>使用相关维数估计解集维数 / Estimate a solution-set dimension by correlation dimension.</summary>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> CorrelationDimension(
        IReadOnlyList<IReadOnlyList<Flt64>> solutions,
        int radiusAmount = 12,
        int pairSampleLimit = 20000) {
        if (solutions.Count < 2) {
            return Failed<Flt64>("至少需要两个解 / at least two solutions are required");
        }
        if (radiusAmount < 2) {
            return Failed<Flt64>("半径采样数量至少为 2 / radiusAmount must be at least 2");
        }
        if (pairSampleLimit < 2) {
            return Failed<Flt64>("成对采样数量至少为 2 / pairSampleLimit must be at least 2");
        }

        int dimension = solutions[0].Count;
        if (dimension == 0) {
            return Failed<Flt64>("解向量不得为空 / solution vectors must be non-empty");
        }
        for (int index = 0; index < solutions.Count; index++) {
            if (solutions[index].Count != dimension) {
                return Failed<Flt64>("解向量必须维度一致 / solution vectors must share the same dimension");
            }
        }

        var distances = new List<double>();
        for (int i = 0; i < solutions.Count; i++) {
            for (int j = i + 1; j < solutions.Count; j++) {
                double squared = 0.0;
                for (int k = 0; k < dimension; k++) {
                    double difference = solutions[i][k].ToDouble() - solutions[j][k].ToDouble();
                    squared += difference * difference;
                }
                distances.Add(System.Math.Sqrt(squared));
                if (distances.Count >= pairSampleLimit) {
                    break;
                }
            }
            if (distances.Count >= pairSampleLimit) {
                break;
            }
        }
        if (distances.Count < 2) {
            return Failed<Flt64>("成对距离数量不足 / not enough pairwise distances");
        }

        double maximum = 0.0;
        for (int index = 0; index < distances.Count; index++) {
            maximum = System.Math.Max(maximum, distances[index]);
        }
        if (maximum <= 0.0) {
            return Failed<Flt64>("所有解完全相同，无法估计维数 / all solutions are identical");
        }

        var logRadii = new List<double>(radiusAmount);
        var logCorrelations = new List<double>(radiusAmount);
        for (int index = 0; index < radiusAmount; index++) {
            double ratio = 0.01 * System.Math.Pow(0.98 / 0.01, (double)index / (radiusAmount - 1));
            double radius = maximum * ratio;
            int correlated = 0;
            for (int distanceIndex = 0; distanceIndex < distances.Count; distanceIndex++) {
                if (distances[distanceIndex] < radius) {
                    correlated++;
                }
            }
            if (correlated == 0) {
                continue;
            }
            logRadii.Add(System.Math.Log(radius));
            logCorrelations.Add(System.Math.Log((double)correlated / distances.Count));
        }

        double? slope = LeastSquaresSlope(logRadii, logCorrelations);
        return slope is null
            ? Failed<Flt64>("半径采样不足，无法拟合维数 / not enough radii to fit the dimension")
            : Results.Ok(new Flt64(slope.Value));
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(string message)
        => new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);

    private static double? LeastSquaresSlope(IReadOnlyList<double> xs, IReadOnlyList<double> ys) {
        if (xs.Count < 2 || xs.Count != ys.Count) {
            return null;
        }

        double count = xs.Count;
        double sumX = 0.0;
        double sumY = 0.0;
        for (int index = 0; index < xs.Count; index++) {
            sumX += xs[index];
            sumY += ys[index];
        }

        double meanX = sumX / count;
        double meanY = sumY / count;
        double numerator = 0.0;
        double denominator = 0.0;
        for (int index = 0; index < xs.Count; index++) {
            double dx = xs[index] - meanX;
            numerator += dx * (ys[index] - meanY);
            denominator += dx * dx;
        }
        return denominator <= 0.0 ? null : numerator / denominator;
    }
}
