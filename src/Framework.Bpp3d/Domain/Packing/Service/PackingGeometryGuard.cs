#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
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
/// <remarks>
/// 移植（23023 B）的**终态重叠检测**：长方体-长方体、圆柱-长方体、
/// 同轴圆柱-圆柱、异轴圆柱-圆柱四种配对。
///
/// **这里不移植**：宽相位扫掠表（`candidateGeometryPairs` / `sweepScore`，大箱降复杂度用）、
/// 越界检测。两者留在 P9 台账。
///
/// Ports its **final-state overlap detection**: box-box, cylinder-box, same-axis cylinder-cylinder, and different-axis
/// cylinder-cylinder. **Not implemented**: the broad-phase sweep table (its `candidateGeometryPairs` / `sweepScore`, an O(n log n) shortcut for
/// large bins) and the outside-bin check. Both stay on the P9 ledger.
/// </remarks>
internal static class PackingGeometryGuard {
    private const double OverlapTolerance = 1e-7;

    /// <summary>宽相位扫掠的最大坐标量级 / Maximum coordinate magnitude for the broad-phase sweep.</summary>
    private const double BroadPhaseMaxMagnitude = 1e6;

    /// <summary>宽相位扫掠的最小物品数 / Minimum item count for the broad-phase sweep.</summary>
    private const int BroadPhaseMinItemCount = 16;

    /// <summary>
    /// 校验已装箱物品的几何约束 / Validate geometric constraints of packed items.
    /// </summary>
    /// <param name="bin">已装箱的箱子 / Packed bin to validate</param>
    /// <param name="source">调用来源标识 / Caller source identifier</param>
    /// <returns>校验结果 / Validation result</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> RequirePackedBinShapeGeometry(
        PackingBin bin,
        string source) {
        ArgumentNullException.ThrowIfNull(bin);

        // **缺少几何显然是"验不了"，不能当成"没重叠"**：静默跳过会让这个守卫对整条旧路径恒真。
        // **Missing geometry plainly means "cannot verify", not "no overlap"**: skipping silently would make the guard vacuously true on the whole legacy path.
        for (int index = 0; index < bin.Items.Count; ++index) {
            if (bin.Items[index].Geometry is null) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        PackingGeometryContract.UnsupportedMissingGeometryMessage(source, index)));
            }
        }

        // 越界检测：只有**知道箱子多大**才能谈"越界"。`PackingBin.Type` 缺失时显式拒绝，而不是默认"装得下"。
        // The outside-bin check needs **the container's extents** to mean anything. A missing `PackingBin.Type` is rejected explicitly rather than
        // defaulting to "it fits".
        if (bin.Type is null) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    PackingGeometryContract.UnsupportedMissingBinTypeMessage(source)));
        }
        double containerWidth = bin.Type.Width.ToDouble();
        double containerHeight = bin.Type.Height.ToDouble();
        double containerDepth = bin.Type.Depth.ToDouble();
        for (int index = 0; index < bin.Items.Count; ++index) {
            PackingItemGeometry geometry = bin.Items[index].Geometry!;
            if (geometry.MinX < -OverlapTolerance
                || geometry.MinY < -OverlapTolerance
                || geometry.MinZ < -OverlapTolerance
                || geometry.MaxX > containerWidth + OverlapTolerance
                || geometry.MaxY > containerHeight + OverlapTolerance
                || geometry.MaxZ > containerDepth + OverlapTolerance) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        PackingGeometryContract.UnsupportedOutsideBinGeometryMessage(
                            source,
                            bin.Name ?? "bin",
                            index,
                            $"box=({geometry.MinX}, {geometry.MinY}, {geometry.MinZ})-({geometry.MaxX}, {geometry.MaxY}, {geometry.MaxZ}) container=({containerWidth}, {containerHeight}, {containerDepth})")));
            }
        }

        // 宽相位：物品够多且数值安全时，用一维扫掠只保留"可能相交"的配对；无法证明安全就退回全配对。
        // Broad phase: with enough items and safe numbers, a one-dimensional sweep keeps only the "possibly intersecting" pairs; when safety cannot be
        // established it falls back to the exhaustive pairing.
        var geometries = new List<PackingItemGeometry>(bin.Items.Count);
        foreach (PackingItem item in bin.Items) {
            geometries.Add(item.Geometry!);
        }
        IReadOnlyList<(int Lhs, int Rhs)> pairs = CandidateGeometryPairs(geometries)
            ?? ExhaustivePairs(geometries.Count);
        foreach ((int lhsIndex, int rhsIndex) in pairs) {
            {
                PackingItemGeometry lhs = geometries[lhsIndex];
                PackingItemGeometry rhs = geometries[rhsIndex];
                if (Overlaps(lhs, rhs)) {
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

        return RequireHorizontalCylinderSupport(bin, source);
    }

    /// <summary>
    /// 两个几何快照是否重叠 / Whether two geometry snapshots overlap.
    /// </summary>
    /// <param name="lhs">左侧几何 / Left-hand geometry</param>
    /// <param name="rhs">右侧几何 / Right-hand geometry</param>
    /// <returns>是否重叠 / Whether they overlap</returns>
    internal static bool Overlaps(PackingItemGeometry lhs, PackingItemGeometry rhs) {
        ArgumentNullException.ThrowIfNull(lhs);
        ArgumentNullException.ThrowIfNull(rhs);
        return (lhs.IsCylinder, rhs.IsCylinder) switch {
            (true, true) => CylinderCylinderOverlaps(lhs, rhs),
            (true, false) => CylinderBoxOverlaps(lhs, rhs),
            (false, true) => CylinderBoxOverlaps(rhs, lhs),
            _ => BoxBoxOverlaps(lhs, rhs),
        };
    }

    /// <summary>全配对 / Every unordered pair, in its order.</summary>
    /// <param name="count">物品数 / Item count.</param>
    /// <returns>配对列表 / The pair list.</returns>
    private static IReadOnlyList<(int Lhs, int Rhs)> ExhaustivePairs(int count) {
        var pairs = new List<(int Lhs, int Rhs)>();
        for (int lhsIndex = 0; lhsIndex < count; ++lhsIndex) {
            for (int rhsIndex = lhsIndex + 1; rhsIndex < count; ++rhsIndex) {
                pairs.Add((lhsIndex, rhsIndex));
            }
        }
        return pairs;
    }

    /// <summary>
    /// 保守的候选配对 / Conservative candidate pairs.
    /// </summary>
    /// <remarks>
    /// 对应 `candidateGeometryPairs`：**含圆柱的配对全部保留**（圆柱判据不是包围盒，扫掠无法剪枝），
    /// 只对长方体之间用一维扫掠剪掉确定不相交的配对。任何一步无法证明安全都返回 `null`，由调用方退回全配对。
    ///
    /// **Every cylinder-related pair is kept** (the cylinder predicate is not a bounding box, so the
    /// sweep cannot prune it) and only box-box pairs are pruned by a one-dimensional sweep. Any step that cannot be proven safe returns `null` and the
    /// caller falls back to the exhaustive pairing.
    /// </remarks>
    /// <param name="geometries">箱内几何 / Geometries inside the bin.</param>
    /// <returns>有序配对，或无法证明安全时的 null / Ordered pairs, or null when safety cannot be established.</returns>
    internal static IReadOnlyList<(int Lhs, int Rhs)>? CandidateGeometryPairs(
        IReadOnlyList<PackingItemGeometry> geometries) {
        ArgumentNullException.ThrowIfNull(geometries);
        if (geometries.Count < BroadPhaseMinItemCount) {
            return null;
        }

        bool[] isCylinder = new bool[geometries.Count];
        for (int index = 0; index < geometries.Count; ++index) {
            isCylinder[index] = geometries[index].IsCylinder;
        }

        long maxCandidatePairs = global::System.Math.Max(64L, geometries.Count * 16L);
        var pairs = new List<(int Lhs, int Rhs)>();
        for (int lhsIndex = 0; lhsIndex < geometries.Count; ++lhsIndex) {
            if (!isCylinder[lhsIndex]) {
                continue;
            }
            for (int rhsIndex = 0; rhsIndex < geometries.Count; ++rhsIndex) {
                if (rhsIndex == lhsIndex || (rhsIndex < lhsIndex && isCylinder[rhsIndex])) {
                    continue;
                }
                pairs.Add((global::System.Math.Min(lhsIndex, rhsIndex), global::System.Math.Max(lhsIndex, rhsIndex)));
                if (pairs.Count > maxCandidatePairs) {
                    return null;
                }
            }
        }

        var boxIndices = new List<int>();
        for (int index = 0; index < geometries.Count; ++index) {
            if (!isCylinder[index]) {
                boxIndices.Add(index);
            }
        }
        if (boxIndices.Count < 2) {
            pairs.Sort(ComparePairs);
            return pairs;
        }

        var bounds = new Dictionary<int, SweepBounds>();
        foreach (int index in boxIndices) {
            SweepBounds? candidate = SweepBoundsOrNull(geometries[index]);
            if (candidate is null) {
                return null;
            }
            bounds[index] = candidate;
        }
        Axis3 sweepAxis = SelectSweepAxis(bounds, boxIndices);
        boxIndices.Sort((lhs, rhs) => bounds[lhs].Min(sweepAxis).CompareTo(bounds[rhs].Min(sweepAxis)));

        var active = new List<int>();
        foreach (int index in boxIndices) {
            double minValue = bounds[index].Min(sweepAxis);
            int activeIndex = 0;
            while (activeIndex < active.Count) {
                int otherIndex = active[activeIndex];
                if (bounds[otherIndex].Max(sweepAxis) < minValue) {
                    active.RemoveAt(activeIndex);
                } else {
                    pairs.Add((global::System.Math.Min(index, otherIndex), global::System.Math.Max(index, otherIndex)));
                    if (pairs.Count > maxCandidatePairs) {
                        return null;
                    }
                    ++activeIndex;
                }
            }
            active.Add(index);
        }
        pairs.Sort(ComparePairs);
        return pairs;
    }

    private static int ComparePairs((int Lhs, int Rhs) lhs, (int Lhs, int Rhs) rhs) =>
        lhs.Lhs != rhs.Lhs ? lhs.Lhs.CompareTo(rhs.Lhs) : lhs.Rhs.CompareTo(rhs.Rhs);

    /// <summary>选择更可能剪枝的扫掠轴 / Choose the sweep axis that is most likely to prune.</summary>
    /// <param name="bounds">几何范围 / Geometry bounds.</param>
    /// <param name="indices">参与评估的长方体下标 / Box indices to score.</param>
    /// <returns>扫掠轴 / The sweep axis.</returns>
    internal static Axis3 SelectSweepAxis(
        IReadOnlyDictionary<int, SweepBounds> bounds,
        IReadOnlyList<int> indices) {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(indices);
        Axis3 best = Axis3.X;
        double bestScore = double.NegativeInfinity;
        foreach (Axis3 axis in new[] { Axis3.X, Axis3.Y, Axis3.Z }) {
            double score = SweepScore(bounds, indices, axis);
            if (score > bestScore) {
                bestScore = score;
                best = axis;
            }
        }
        return best;
    }

    /// <summary>估算各轴的稀疏程度 / Estimate how sparse the intervals are on one axis.</summary>
    /// <param name="bounds">几何范围 / Geometry bounds.</param>
    /// <param name="indices">参与评估的长方体下标 / Box indices to score.</param>
    /// <param name="axis">待评估坐标轴 / Axis to score.</param>
    /// <returns>全局跨度与平均物品跨度之比 / Global span divided by the average item span.</returns>
    private static double SweepScore(
        IReadOnlyDictionary<int, SweepBounds> bounds,
        IReadOnlyList<int> indices,
        Axis3 axis) {
        double globalMin = double.PositiveInfinity;
        double globalMax = double.NegativeInfinity;
        double totalItemSpan = 0.0;
        foreach (int index in indices) {
            double minValue = bounds[index].Min(axis);
            double maxValue = bounds[index].Max(axis);
            globalMin = global::System.Math.Min(globalMin, minValue);
            globalMax = global::System.Math.Max(globalMax, maxValue);
            totalItemSpan += maxValue - minValue;
        }
        return (globalMax - globalMin) * indices.Count / totalItemSpan;
    }

    /// <summary>
    /// 仅从已捕获的几何端点构造保守扫掠范围 / Build conservative sweep bounds from captured geometry endpoints only.
    /// </summary>
    /// <param name="geometry">几何快照 / Geometry snapshot.</param>
    /// <returns>扫掠范围，无法证明安全时为 null / The bounds, or null when safety cannot be established.</returns>
    private static SweepBounds? SweepBoundsOrNull(PackingItemGeometry geometry) {
        double[] coordinates = { geometry.MinX, geometry.MinY, geometry.MinZ, geometry.MaxX, geometry.MaxY, geometry.MaxZ };
        foreach (double coordinate in coordinates) {
            if (!double.IsFinite(coordinate) || global::System.Math.Abs(coordinate) > BroadPhaseMaxMagnitude) {
                return null;
            }
        }
        if (geometry.MaxX <= geometry.MinX || geometry.MaxY <= geometry.MinY || geometry.MaxZ <= geometry.MinZ) {
            return null;
        }

        // 用与箱体判据相同的线性容差扩展端点 / Expand the endpoints by the same linear tolerance the box predicate uses.
        const double padding = OverlapTolerance;
        return new SweepBounds(
            geometry.MinX - padding, geometry.MaxX + padding,
            geometry.MinY - padding, geometry.MaxY + padding,
            geometry.MinZ - padding, geometry.MaxZ + padding);
    }

    /// <summary>保守的一维扫掠范围 / Conservative one-dimensional sweep bounds.</summary>
    /// <param name="MinX">X 下界 / X lower bound.</param>
    /// <param name="MaxX">X 上界 / X upper bound.</param>
    /// <param name="MinY">Y 下界 / Y lower bound.</param>
    /// <param name="MaxY">Y 上界 / Y upper bound.</param>
    /// <param name="MinZ">Z 下界 / Z lower bound.</param>
    /// <param name="MaxZ">Z 上界 / Z upper bound.</param>
    internal sealed record SweepBounds(
        double MinX, double MaxX,
        double MinY, double MaxY,
        double MinZ, double MaxZ) {
        /// <summary>沿指定轴的下界 / Lower bound along the axis.</summary>
        /// <param name="axis">坐标轴 / The axis.</param>
        /// <returns>下界 / The lower bound.</returns>
        public double Min(Axis3 axis) => axis switch {
            Axis3.X => MinX,
            Axis3.Y => MinY,
            _ => MinZ,
        };

        /// <summary>沿指定轴的上界 / Upper bound along the axis.</summary>
        /// <param name="axis">坐标轴 / The axis.</param>
        /// <returns>上界 / The upper bound.</returns>
        public double Max(Axis3 axis) => axis switch {
            Axis3.X => MaxX,
            Axis3.Y => MaxY,
            _ => MaxZ,
        };
    }

    private static bool BoxBoxOverlaps(PackingItemGeometry lhs, PackingItemGeometry rhs) =>
        IntervalOverlaps(lhs.MinX, lhs.MaxX, rhs.MinX, rhs.MaxX)
        && IntervalOverlaps(lhs.MinY, lhs.MaxY, rhs.MinY, rhs.MaxY)
        && IntervalOverlaps(lhs.MinZ, lhs.MaxZ, rhs.MinZ, rhs.MaxZ);

    private static bool CylinderBoxOverlaps(PackingItemGeometry cylinder, PackingItemGeometry box) {
        Axis3 axis = cylinder.Axis ?? Axis3.Y;
        if (!IntervalOverlaps(cylinder.Min(axis), cylinder.Max(axis), box.Min(axis), box.Max(axis))) {
            return false;
        }
        (Axis3 radial0, Axis3 radial1) = Radials(axis);
        double nearest0 = global::System.Math.Clamp(cylinder.Center(radial0), box.Min(radial0), box.Max(radial0));
        double nearest1 = global::System.Math.Clamp(cylinder.Center(radial1), box.Min(radial1), box.Max(radial1));
        double delta0 = cylinder.Center(radial0) - nearest0;
        double delta1 = cylinder.Center(radial1) - nearest1;
        double distance = global::System.Math.Sqrt(delta0 * delta0 + delta1 * delta1);
        return cylinder.Radius - distance > OverlapTolerance;
    }

    private static bool CylinderCylinderOverlaps(
        PackingItemGeometry lhs,
        PackingItemGeometry rhs) {
        Axis3 lhsAxis = lhs.Axis ?? Axis3.Y;
        Axis3 rhsAxis = rhs.Axis ?? Axis3.Y;
        return lhsAxis == rhsAxis
            ? SameAxisCylinderOverlaps(lhs, rhs, lhsAxis)
            : DifferentAxisCylinderOverlaps(lhs, rhs, lhsAxis, rhsAxis);
    }

    private static bool SameAxisCylinderOverlaps(
        PackingItemGeometry lhs,
        PackingItemGeometry rhs,
        Axis3 axis) {
        if (!IntervalOverlaps(lhs.Min(axis), lhs.Max(axis), rhs.Min(axis), rhs.Max(axis))) {
            return false;
        }
        (Axis3 radial0, Axis3 radial1) = Radials(axis);
        double delta0 = lhs.Center(radial0) - rhs.Center(radial0);
        double delta1 = lhs.Center(radial1) - rhs.Center(radial1);
        double distance = global::System.Math.Sqrt(delta0 * delta0 + delta1 * delta1);
        return lhs.Radius + rhs.Radius - distance > OverlapTolerance;
    }

    private static bool DifferentAxisCylinderOverlaps(
        PackingItemGeometry lhs,
        PackingItemGeometry rhs,
        Axis3 lhsAxis,
        Axis3 rhsAxis) {
        Axis3 sharedAxis = SharedAxis(lhsAxis, rhsAxis);
        double lhsAxisDistance = DistanceToInterval(rhs.Center(lhsAxis), lhs.Min(lhsAxis), lhs.Max(lhsAxis));
        double rhsAxisDistance = DistanceToInterval(lhs.Center(rhsAxis), rhs.Min(rhsAxis), rhs.Max(rhsAxis));
        double lhsSharedSquared = lhs.Radius * lhs.Radius - rhsAxisDistance * rhsAxisDistance;
        double rhsSharedSquared = rhs.Radius * rhs.Radius - lhsAxisDistance * lhsAxisDistance;
        if (lhsSharedSquared <= OverlapTolerance || rhsSharedSquared <= OverlapTolerance) {
            return false;
        }
        double lhsShared = global::System.Math.Sqrt(lhsSharedSquared);
        double rhsShared = global::System.Math.Sqrt(rhsSharedSquared);
        return IntervalOverlaps(
            lhs.Center(sharedAxis) - lhsShared,
            lhs.Center(sharedAxis) + lhsShared,
            rhs.Center(sharedAxis) - rhsShared,
            rhs.Center(sharedAxis) + rhsShared);
    }

    private static (Axis3 Radial0, Axis3 Radial1) Radials(Axis3 axis) => axis switch {
        Axis3.X => (Axis3.Y, Axis3.Z),
        Axis3.Y => (Axis3.X, Axis3.Z),
        _ => (Axis3.X, Axis3.Y),
    };

    private static Axis3 SharedAxis(Axis3 lhs, Axis3 rhs) {
        foreach (Axis3 candidate in new[] { Axis3.X, Axis3.Y, Axis3.Z }) {
            if (candidate != lhs && candidate != rhs) {
                return candidate;
            }
        }
        throw new InvalidOperationException("三个轴中必有第三个 / a third axis must exist");
    }

    private static bool IntervalOverlaps(double lhsMin, double lhsMax, double rhsMin, double rhsMax) =>
        global::System.Math.Min(lhsMax, rhsMax) - global::System.Math.Max(lhsMin, rhsMin) > OverlapTolerance;

    private static double DistanceToInterval(double point, double min, double max) {
        if (point < min) {
            return min - point;
        }
        if (point > max) {
            return point - max;
        }
        return 0.0;
    }

    /// <summary>
    /// 校验箱内每个水平圆柱是否获得足够的下方支撑覆盖 / Validate that every horizontal cylinder in the bin has sufficient support coverage.
    /// </summary>
    /// <remarks>
    /// **旧实现是逐对比较**："只要有任意一块支撑的顶面不高于圆柱底面、且在水平上与之重叠，就算有支撑"。
    /// 那会把"两块各盖一半"判成支撑充足之外的情况全都混在一起：真正的问题在于**一根圆柱需要覆盖整条轴线**，
    /// 而单块支撑只需要盖住其中一段。所以这里改为求支撑区间并集，见 `HorizontalCylinderSupportCoverage.CuboidSupportCoverage`。
    ///
    /// **The old implementation compared pairwise**: "if any single support's top is no higher than the cylinder's bottom and overlaps it horizontally,
    /// the cylinder is supported". That conflates every case, because **a cylinder needs its whole axis covered** while one support only covers a
    /// segment. So the implementation unions the support intervals instead — see `HorizontalCylinderSupportCoverage.CuboidSupportCoverage`.
    /// </remarks>
    /// <param name="bin">已装箱的箱子 / Packed bin to validate</param>
    /// <param name="source">调用来源标识 / Caller source identifier</param>
    /// <returns>校验结果 / Validation result</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> RequireHorizontalCylinderSupport(
        PackingBin bin,
        string source) {
        ArgumentNullException.ThrowIfNull(bin);

        var supports = new List<HorizontalCylinderSupportGeometry>();
        foreach (PackingItem item in bin.Items) {
            if (item.Geometry is null) {
                continue;
            }
            supports.Add(ToSupportGeometry(item.Geometry));
        }

        for (int index = 0; index < bin.Items.Count; ++index) {
            PackingItemGeometry? geometry = bin.Items[index].Geometry;
            if (geometry is null || !geometry.IsCylinder) {
                continue;
            }
            var candidates = new List<HorizontalCylinderSupportGeometry>(supports);
            candidates.RemoveAt(index);
            if (!HorizontalCylinderSupportCoverage.CuboidSupportCoverage(
                    ToSupportGeometry(geometry),
                    geometry.Axis ?? Axis3.Y,
                    candidates)) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument,
                        PackingGeometryContract.UnsupportedHorizontalCylinderSupportMessage(
                            source, bin.Name ?? "bin", index, "horizontal cylinder")));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static HorizontalCylinderSupportGeometry ToSupportGeometry(PackingItemGeometry geometry) =>
        new(geometry.MinX, geometry.MaxX, geometry.MinY, geometry.MaxY, geometry.MinZ, geometry.MaxZ, geometry.IsCylinder);

    private static bool IntervalsOverlap(double lhsMin, double lhsMax, double rhsMin, double rhsMax) =>
        global::System.Math.Min(lhsMax, rhsMax) - global::System.Math.Max(lhsMin, rhsMin) > OverlapTolerance;
}
