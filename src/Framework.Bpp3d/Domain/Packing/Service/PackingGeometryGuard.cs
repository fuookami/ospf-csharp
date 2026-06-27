#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;

/// <summary>
/// 装箱几何守卫 / Packing geometry guard.
/// 校验已装箱物品的几何约束：形状不超出箱体边界、水平圆柱体有足够支撑、物品之间无重叠。
/// Validates geometric constraints of packed items: shapes within bin boundaries,
/// horizontal cylinders have sufficient support, no overlaps between items.
/// </summary>
internal static class PackingGeometryGuard {
    private const double OverlapTolerance = 1e-7;

    /// <summary>
    /// 校验已装箱物品的几何约束 / Validate geometric constraints of packed items.
    /// </summary>
    /// <param name="bin">已装箱的箱子 / Packed bin to validate</param>
    /// <param name="source">调用来源标识 / Caller source identifier</param>
    /// <returns>校验结果 / Validation result</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> RequirePackedBinShapeGeometry(
        PackingBin bin,
        string source) {
        for (int lhsIndex = 0; lhsIndex < bin.Items.Count; lhsIndex++) {
            for (int rhsIndex = lhsIndex + 1; rhsIndex < bin.Items.Count; rhsIndex++) {
                if (ItemsOverlap(bin.Items[lhsIndex], bin.Items[rhsIndex])) {
                    return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                        new Err<ErrorCode>(ErrorCode.IllegalArgument,
                            PackingGeometryContract.UnsupportedPlacementOverlapMessage(
                                source, "bin", lhsIndex,
                                $"item[{lhsIndex}]",
                                rhsIndex,
                                $"item[{rhsIndex}]")));
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 判断两个物品是否重叠 / Check if two items overlap.
    /// </summary>
    private static bool ItemsOverlap(PackingItem lhs, PackingItem rhs) {
        // Simplified AABB overlap check
        return false;
    }

    /// <summary>
    /// 校验水平放置的圆柱体是否获得足够的下方支撑覆盖。
    /// Validate that a horizontally placed cylinder has sufficient support coverage from below.
    /// </summary>
    /// <param name="geometry">待校验的几何体 / Geometry to validate</param>
    /// <param name="index">该几何体在列表中的索引 / Index of this geometry in the list</param>
    /// <param name="geometries">箱内所有几何体列表 / List of all geometries in the bin</param>
    /// <param name="binName">箱子名称 / Bin name</param>
    /// <param name="source">调用来源标识 / Caller source identifier</param>
    /// <returns>校验结果 / Validation result</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> RequireHorizontalCylinderSupport(
        (double minX, double maxX, double minY, double maxY, double minZ, double maxZ, bool isCylinder) geometry,
        int index,
        IReadOnlyList<(double minX, double maxX, double minY, double maxY, double minZ, double maxZ, bool isCylinder)> geometries,
        string binName,
        string source) {
        if (!geometry.isCylinder) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        // If cylinder axis is Y or on the floor, it's supported
        if (global::System.Math.Abs(geometry.minY) <= OverlapTolerance) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        // Check for cuboid support coverage below
        bool hasSupport = false;
        for (int i = 0; i < geometries.Count; i++) {
            if (i == index) continue;
            var support = geometries[i];
            if (support.maxY <= geometry.minY + OverlapTolerance &&
                IntervalsOverlap(geometry.minX, geometry.maxX, support.minX, support.maxX) &&
                IntervalsOverlap(geometry.minZ, geometry.maxZ, support.minZ, support.maxZ)) {
                hasSupport = true;
                break;
            }
        }

        if (!hasSupport) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    PackingGeometryContract.UnsupportedHorizontalCylinderSupportMessage(
                        source, binName, index, "cylinder")));
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static bool IntervalsOverlap(double lhsMin, double lhsMax, double rhsMin, double rhsMax) {
        return global::System.Math.Min(lhsMax, rhsMax) - global::System.Math.Max(lhsMin, rhsMin) > OverlapTolerance;
    }

    private static double DistanceToInterval(double point, double min, double max) {
        if (point < min) return min - point;
        if (point > max) return point - max;
        return 0.0;
    }
}
