#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 装载顺序计算器 / Loading order calculator.
/// 确定货物在箱体中的卸载/装载顺序。
/// Determines the unloading/loading sequence for items in a bin.
/// </summary>
public sealed class LoadingOrderCalculator {
    private readonly Quantity<FltX>? _maxBlockDepth;
    private readonly Func<ActualItem, ActualItem, bool> _sameTypeJudger;

    /// <summary>
    /// 构造装载顺序计算器 / Construct loading order calculator.
    /// </summary>
    /// <param name="maxBlockDepth">最大块深度 / Maximum block depth.</param>
    /// <param name="sameTypeJudger">同类型判断器 / Same type judger.</param>
    public LoadingOrderCalculator(
        Quantity<FltX>? maxBlockDepth = null,
        Func<ActualItem, ActualItem, bool>? sameTypeJudger = null) {
        _maxBlockDepth = maxBlockDepth;
        _sameTypeJudger = sameTypeJudger ?? ((a, b) => a.PackageType == b.PackageType);
    }

    /// <summary>
    /// 计算装载顺序 / Calculate loading order.
    /// </summary>
    /// <param name="placements">货物放置列表 / Item placements.</param>
    /// <returns>带顺序号的放置列表 / Placements with sequence numbers.</returns>
    public IReadOnlyList<(QuantityPlacement3<ActualItem, FltX> Placement, int Order)> Invoke(
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> placements) {
        if (placements.Count == 0) return Array.Empty<(QuantityPlacement3<ActualItem, FltX>, int)>();

        // Sort by Z, Y, X (back-to-front, bottom-to-top, right-to-left)
        var sorted = placements
            .OrderBy(p => p.Position.Z.Value.ToFlt64().ToDouble())
            .ThenBy(p => p.Position.Y.Value.ToFlt64().ToDouble())
            .ThenBy(p => p.Position.X.Value.ToFlt64().ToDouble())
            .ToList();

        // Build forward dependency matrix
        var order = new int[sorted.Count];
        for (int i = 0; i < sorted.Count; i++) {
            order[i] = i + 1; // 1-based
        }

        // Apply forward dependencies: if item A blocks item B from the loading face, A must come after B
        for (int i = 0; i < sorted.Count; i++) {
            for (int j = i + 1; j < sorted.Count; j++) {
                if (Forward(sorted[i], sorted[j])) {
                    // i blocks j, so j should come before i
                    if (order[i] <= order[j]) {
                        (order[i], order[j]) = (order[j], order[i]);
                    }
                }
            }
        }

        return sorted.Select((p, idx) => (p, order[idx])).ToList();
    }

    /// <summary>
    /// 检查 a 是否阻挡 b（从装载面方向）。
    /// Check if a blocks b (from the loading face direction).
    /// </summary>
    private static bool Forward(
        QuantityPlacement3<ActualItem, FltX> a,
        QuantityPlacement3<ActualItem, FltX> b) {
        // Check bottom footprint overlap
        double aMinX = a.Position.X.Value.ToFlt64().ToDouble();
        double aMaxX = aMinX + a.Unit.Width.Value.ToFlt64().ToDouble();
        double aMinZ = a.Position.Z.Value.ToFlt64().ToDouble();
        double aMaxZ = aMinZ + a.Unit.Depth.Value.ToFlt64().ToDouble();

        double bMinX = b.Position.X.Value.ToFlt64().ToDouble();
        double bMaxX = bMinX + b.Unit.Width.Value.ToFlt64().ToDouble();
        double bMinZ = b.Position.Z.Value.ToFlt64().ToDouble();
        double bMaxZ = bMinZ + b.Unit.Depth.Value.ToFlt64().ToDouble();

        bool overlapX = aMinX < bMaxX && aMaxX > bMinX;
        bool overlapZ = aMinZ < bMaxZ && aMaxZ > bMinZ;

        if (!overlapX || !overlapZ) return false;

        // a is closer to loading face (Z=0) than b
        return aMinZ < bMinZ;
    }
}
