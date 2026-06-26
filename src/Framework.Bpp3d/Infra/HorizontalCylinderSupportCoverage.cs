#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 水平圆柱支撑覆盖几何 / Horizontal cylinder support coverage geometry.
/// 用于验证水平圆柱体是否有足够的底部支撑。
/// Used to validate that horizontal cylinders have sufficient bottom support.
/// </summary>
public sealed record HorizontalCylinderSupportGeometry(
    double MinX, double MaxX,
    double MinY, double MaxY,
    double MinZ, double MaxZ,
    bool IsCylinder) {
    /// <summary>获取指定轴的最小值 / Get min along axis.</summary>
    public double Min(Axis3 axis) => axis switch {
        Axis3.X => MinX, Axis3.Y => MinY, Axis3.Z => MinZ, _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };
    /// <summary>获取指定轴的最大值 / Get max along axis.</summary>
    public double Max(Axis3 axis) => axis switch {
        Axis3.X => MaxX, Axis3.Y => MaxY, Axis3.Z => MaxZ, _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };
    /// <summary>获取指定轴的中心值 / Get center along axis.</summary>
    public double Center(Axis3 axis) => (Min(axis) + Max(axis)) / 2.0;
}

/// <summary>
/// 水平圆柱支撑覆盖验证器 / Horizontal cylinder support coverage validator.
/// </summary>
public static class HorizontalCylinderSupportCoverage {
    /// <summary>容差常量 / Tolerance constant.</summary>
    public const double Tolerance = 1e-7;

    /// <summary>
    /// 获取水平圆柱底部支撑线的径向轴。
    /// Gets the radial axis for the bottom support line of a horizontal cylinder.
    /// </summary>
    public static Axis3 SupportRadialAxis(Axis3 cylinderAxis) => cylinderAxis switch {
        Axis3.X => Axis3.Z,
        Axis3.Y => Axis3.Y,
        Axis3.Z => Axis3.X,
        _ => throw new ArgumentOutOfRangeException(nameof(cylinderAxis))
    };

    /// <summary>
    /// 检查一组一维区间是否完全覆盖目标跨度。
    /// Checks if a set of 1D intervals fully covers a target span.
    /// </summary>
    public static bool IntervalsCoverSpan(double targetMin, double targetMax, IReadOnlyList<(double Min, double Max)> intervals, double tolerance = Tolerance) {
        if (intervals.Count == 0) return false;

        var sorted = new List<(double Min, double Max)>(intervals);
        sorted.Sort((a, b) => a.Min.CompareTo(b.Min));

        double covered = targetMin;
        foreach ((double min, double max) in sorted) {
            if (min > covered + tolerance) return false;
            if (max > covered) covered = max;
        }
        return covered >= targetMax - tolerance;
    }

    /// <summary>
    /// 检查水平圆柱是否有足够的长方体支撑覆盖。
    /// Checks if a horizontal cylinder has sufficient cuboid support coverage.
    /// </summary>
    public static bool HasSufficientSupport(
        Axis3 cylinderAxis,
        double cylinderLength,
        double cylinderRadius,
        IReadOnlyList<HorizontalCylinderSupportGeometry> supports,
        double tolerance = Tolerance) {

        Axis3 radialAxis = SupportRadialAxis(cylinderAxis);
        Axis3 lengthAxis = cylinderAxis;

        // Check if cylinder is on the floor (Y == 0)
        double cylinderBottom = cylinderRadius; // assuming center at radius height
        if (cylinderBottom <= tolerance) return true;

        // Build support intervals along the cylinder's length axis
        var intervals = new List<(double Min, double Max)>();
        foreach (HorizontalCylinderSupportGeometry support in supports) {
            if (support.IsCylinder) continue; // only cuboid support counts

            // Check radial overlap: support must reach the cylinder's bottom
            double supportTop = support.Max(Axis3.Y);
            if (supportTop < cylinderBottom - tolerance) continue;

            // Check transverse overlap: support must be under the cylinder
            double supportMinRadial = support.Min(radialAxis);
            double supportMaxRadial = support.Max(radialAxis);
            double cylinderMinRadial = -cylinderRadius;
            double cylinderMaxRadial = cylinderRadius;
            if (supportMaxRadial < cylinderMinRadial + tolerance || supportMinRadial > cylinderMaxRadial - tolerance) continue;

            intervals.Add((support.Min(lengthAxis), support.Max(lengthAxis)));
        }

        return IntervalsCoverSpan(0, cylinderLength, intervals, tolerance);
    }
}
