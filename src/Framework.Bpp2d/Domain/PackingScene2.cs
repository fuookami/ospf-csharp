#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;
/// <summary>
/// 二维装箱场景
/// 2D packing scene.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
/// <param name="Sheet">板材 / Sheet</param>
/// <param name="Placements">放置列表 / List of placements</param>
public sealed record PackingScene2<V>(
    Sheet2<V> Sheet,
    IReadOnlyList<PlannedRectangle2<V>> Placements
) where V : struct, IFloatingNumber<V> {
    /// <summary>板材面积 / Sheet area</summary>
    public Quantity<V> SheetArea => QuantityArithmetic.Product(Sheet.Width, Sheet.Height);

    /// <summary>
    /// 获取板材对应的盒体需求
    /// Get the box need for the sheet (origin at (0, 0)).
    /// </summary>
    public Box2Need<V> SheetBox2Need() {
        Quantity<V> zeroX = QuantityArithmetic.ZeroOf(Sheet.Width);
        Quantity<V> zeroY = QuantityArithmetic.ZeroOf(Sheet.Height);
        return new Box2Need<V>(zeroX, zeroY, Sheet.Width, Sheet.Height);
    }

    /// <summary>
    /// 检查所有放置是否都在板材内
    /// Check whether all placements are inside the sheet.
    /// </summary>
    public bool AllInsideSheet() {
        Box2Need<V> sheetBox = SheetBox2Need();
        foreach (PlannedRectangle2<V> placement in Placements) {
            if (!placement.ToBox2Need().Inside(sheetBox)) { return false; }
        }
        return true;
    }

    /// <summary>
    /// 计算已使用面积
    /// Compute the used area (sum of placement projection areas).
    /// </summary>
    public Quantity<V> UsedArea() {
        Quantity<V> acc = QuantityArithmetic.ZeroOf(SheetArea);
        foreach (PlannedRectangle2<V> placement in Placements) {
            acc = QuantityArithmetic.Plus(acc, placement.ToPlacement2Need().Projection.Area);
        }
        return acc;
    }

    /// <summary>
    /// 计算剩余面积
    /// Compute the remaining area (sheet area - used area).
    /// </summary>
    public Quantity<V> RemainingArea() => QuantityArithmetic.Minus(SheetArea, UsedArea());

    /// <summary>
    /// 计算板材利用率
    /// Compute the sheet utilization ratio (used value / sheet value).
    /// </summary>
    public Result<V, ErrorCode, Error<ErrorCode>> Utilization() {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> used = UsedArea().ConvertTo(SheetArea.Unit);
        if (used.IsFailed) {
            return new Failed<V, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "Cannot convert used area to sheet area unit."));
        }
        V ratio = used.Value.Value.Div(SheetArea.Value);
        return new Ok<V, ErrorCode, Error<ErrorCode>>(ratio);
    }

    /// <summary>
    /// 获取所有重叠的物料对
    /// Get all overlapping item pairs (by item id).
    /// </summary>
    public List<(string, string)> OverlappedPairs() {
        var result = new List<(string, string)>();
        for (int i = 0; i < Placements.Count; ++i) {
            PlannedRectangle2<V> lhs = Placements[i];
            var lhsBox = lhs.ToBox2Need();
            for (int j = i + 1; j < Placements.Count; ++j) {
                PlannedRectangle2<V> rhs = Placements[j];
                var rhsBox = rhs.ToBox2Need();
                if (lhsBox.Overlaps(rhsBox)) {
                    result.Add((lhs.Item.Id, rhs.Item.Id));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 获取非法重叠的物料对
    /// Get illegally overlapping item pairs (delegates to OverlappedPairs).
    /// </summary>
    public List<(string, string)> IllegalOverlaps() => OverlappedPairs();
}
