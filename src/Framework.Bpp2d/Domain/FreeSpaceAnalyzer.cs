// 余料分析 / Remnant analysis.
// 与固定 Kotlin 对应：排样终态需要知道每块板材上还剩哪些
// 可用矩形区域，供余料入库或后续工单复用。本分析器按「水平切片」把已用区域从板材中扣掉，输出互不重叠的自由矩形。
// At the final packing state, 2D packing needs to know which rectangular regions remain
// usable on each sheet. This analyser subtracts occupied regions by horizontal slices and reports pairwise disjoint
// free rectangles.
//
// 这是**余料报告**，不是余料库存。跨工单的库存、优先级与库位管理属于仓储语义，不在本框架内。
// This is **remnant reporting**, not remnant inventory. Cross-job stock, priority and warehouse location management
// are inventory semantics and stay outside this framework.
//
// 层模型（`Sheet`／`SheetLayer`／`Layer`）已在 earlier 移植，因此本类同时提供**接 `Sheet` 的正规入口**
// （`AnalyzeSheets`）与接扁平放置列表的入口（`Analyze`／`AnalyzeSheet`）。两者共用同一套切片算法，差别只在
// 如何取得占用区域。
// The layer model (`Sheet` / `SheetLayer` / `Layer`) landed in earlier, so this offers both the **proper `Sheet`
// entry point** (`AnalyzeSheets`) and the flat-placement one (`Analyze` / `AnalyzeSheet`). Both share one slicing
// algorithm; only how the occupied regions are obtained differs.
//
// **单位前提**：所有量值必须使用同一单位。基线同样按原始数值比较，因此这不是新增限制，而是把既有前提写明。
// **Unit precondition**: every quantity must use one unit. The code also compares raw values, so this documents an
// existing precondition rather than adding one.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 板材上的一块剩余矩形 / A remaining rectangle on a sheet.
/// </summary>
/// <param name="SheetName">板材名称 / Sheet name.</param>
/// <param name="Rectangle">自由矩形 / Free rectangle.</param>
/// <param name="Area">自由矩形面积 / Free rectangle area.</param>
public sealed record Remnant2(string SheetName, Box2Geometry<FltX> Rectangle, Quantity<FltX> Area);

/// <summary>
/// 余料分析器 / Remnant analyser.
/// </summary>
public static class FreeSpaceAnalyzer {
    /// <summary>
    /// 分析一组板材的余料 / Analyse the remnants of a set of sheets.
    /// </summary>
    /// <param name="sheets">板材与其放置 / Sheets with their placements.</param>
    /// <returns>每块板材的余料矩形列表 / Remnant rectangles per sheet.</returns>
    public static IReadOnlyList<Remnant2> Analyze(
        IReadOnlyList<(Sheet2Shape<FltX> Sheet, IReadOnlyList<Placement2<FltX>> Placements)> sheets) {
        ArgumentNullException.ThrowIfNull(sheets);
        var result = new List<Remnant2>();
        for (int index = 0; index < sheets.Count; index++) {
            string name = string.Format(CultureInfo.InvariantCulture, "sheet-{0}", index + 1);
            result.AddRange(AnalyzeSheet(name, sheets[index].Sheet, sheets[index].Placements));
        }
        return result;
    }

    /// <summary>
    /// 分析一组板材实例的余料，板材名称按序号生成 / Analyse a set of sheet instances, naming them by index.
    /// </summary>
    /// <remarks>
    /// 这是与基线签名一致的**正规入口**：占用区域由 `Sheet → SheetLayer → Layer → Placement2` 逐层展开得到。
    /// This is the **proper entry point** matching the code signature: occupied regions come from unfolding
    /// `Sheet → SheetLayer → Layer → Placement2`.
    /// </remarks>
    /// <param name="sheets">已装箱板材 / Packed sheets.</param>
    /// <returns>每块板材的余料矩形列表 / Remnant rectangles per sheet.</returns>
    public static IReadOnlyList<Remnant2> AnalyzeSheets(IReadOnlyList<Sheet<FltX>> sheets) {
        ArgumentNullException.ThrowIfNull(sheets);
        var result = new List<Remnant2>();
        for (int index = 0; index < sheets.Count; index++) {
            string name = string.Format(CultureInfo.InvariantCulture, "sheet-{0}", index + 1);
            result.AddRange(AnalyzeSheet(name, sheets[index]));
        }
        return result;
    }

    /// <summary>
    /// 分析单块板材实例的余料 / Analyse the remnants of a single sheet instance.
    /// </summary>
    /// <param name="name">板材名称 / Sheet name.</param>
    /// <param name="sheet">已装箱板材 / Packed sheet.</param>
    /// <returns>余料矩形列表，按面积降序 / Remnant rectangles, largest area first.</returns>
    public static IReadOnlyList<Remnant2> AnalyzeSheet(string name, Sheet<FltX> sheet) {
        ArgumentNullException.ThrowIfNull(sheet);
        return AnalyzeSheet(
            name,
            new Sheet2Shape<FltX>(sheet.Width, sheet.Height),
            sheet.Placements);
    }

    /// <summary>
    /// 分析单块板材的余料 / Analyse the remnants of a single sheet.
    /// </summary>
    /// <param name="name">板材名称 / Sheet name.</param>
    /// <param name="sheet">板材形状 / Sheet shape.</param>
    /// <param name="placements">已放置的件 / Placed items.</param>
    /// <returns>余料矩形列表，按面积降序 / Remnant rectangles, largest area first.</returns>
    public static IReadOnlyList<Remnant2> AnalyzeSheet(
        string name,
        Sheet2Shape<FltX> sheet,
        IReadOnlyList<Placement2<FltX>> placements) {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(placements);

        Quantity<FltX> width = sheet.Width;
        Quantity<FltX> height = sheet.Height;
        FltX zero = ZeroOf(width);

        // 收集全部放置的包围盒（含圆形件的外接正方形，保守但安全）。
        // Collect every placement's bounding box (circles use their enclosing square, conservative but safe).
        var occupied = placements.Select(placement => placement.Box()).ToList();
        if (occupied.Count == 0) {
            var whole = Box2Geometry<FltX>.Of(
                InUnit(zero, width), InUnit(zero, width), width, height);
            return new[] {
                new Remnant2(name, whole, QuantityOps.QuantityProduct(width, height)),
            };
        }

        // 以所有 y 边界切分水平带，在每条带上求出未被占用的 x 区间。
        // Slice horizontally at every y boundary, then compute the unoccupied x intervals in each slice.
        var boundaries = new SortedSet<FltX> { zero, height.Value };
        foreach (Box2Geometry<FltX> box in occupied) {
            boundaries.Add(box.Y.Value);
            if (box.MaxY().IsOk) {
                boundaries.Add(box.MaxY().Value.Value);
            }
        }

        var remnants = new List<Remnant2>();
        FltX[] ordered = boundaries
            .Where(value => value.CompareTo(zero) >= 0 && value.CompareTo(height.Value) <= 0)
            .ToArray();
        for (int index = 0; index + 1 < ordered.Length; index++) {
            FltX lower = ordered[index];
            FltX upper = ordered[index + 1];
            if (upper.CompareTo(lower) <= 0) {
                continue;
            }

            // 该带内被占用的 x 区间。与带相交即计入。
            // Occupied x intervals within this slice; counted when they intersect the slice at all.
            var blocked = new List<(FltX Start, FltX End)>();
            foreach (Box2Geometry<FltX> box in occupied) {
                if (!box.MaxY().IsOk || !box.MaxX().IsOk) {
                    continue;
                }
                FltX boxMaxY = box.MaxY().Value.Value;
                if (box.Y.Value.CompareTo(upper) >= 0 || boxMaxY.CompareTo(lower) <= 0) {
                    continue;
                }
                blocked.Add((box.X.Value, box.MaxX().Value.Value));
            }
            blocked.Sort((left, right) => left.Start.CompareTo(right.Start));

            FltX cursor = zero;
            foreach ((FltX blockStart, FltX blockEnd) in blocked) {
                if (blockStart.CompareTo(cursor) > 0) {
                    AddRemnant(remnants, name, width, cursor, blockStart, lower, upper);
                }
                if (blockEnd.CompareTo(cursor) > 0) {
                    cursor = blockEnd;
                }
            }
            if (cursor.CompareTo(width.Value) < 0) {
                AddRemnant(remnants, name, width, cursor, width.Value, lower, upper);
            }
        }

        // 丢弃退化矩形，并按面积降序输出，便于下游优先使用大块余料。
        // Drop degenerate rectangles and sort by area descending so downstream reuses the largest first.
        return remnants
            .Where(remnant => QuantityCore.IsPositive(remnant.Area))
            .OrderByDescending(remnant => remnant.Area.Value)
            .ToList();
    }

    /// <summary>
    /// 追加一块余料矩形 / Append one remnant rectangle.
    /// </summary>
    /// <param name="target">目标列表 / Target list.</param>
    /// <param name="name">板材名称 / Sheet name.</param>
    /// <param name="width">板材宽度，用作单位样本 / Sheet width, used as the unit sample.</param>
    /// <param name="startX">左边界 / Left edge.</param>
    /// <param name="endX">右边界 / Right edge.</param>
    /// <param name="startY">下边界 / Bottom edge.</param>
    /// <param name="endY">上边界 / Top edge.</param>
    private static void AddRemnant(
        List<Remnant2> target,
        string name,
        Quantity<FltX> width,
        FltX startX,
        FltX endX,
        FltX startY,
        FltX endY) {
        if (endX.CompareTo(startX) <= 0 || endY.CompareTo(startY) <= 0) {
            return;
        }
        var rectangle = Box2Geometry<FltX>.Of(
            InUnit(startX, width),
            InUnit(startY, width),
            InUnit(endX.Minus(startX), width),
            InUnit(endY.Minus(startY), width));
        target.Add(new Remnant2(
            name,
            rectangle,
            QuantityOps.QuantityProduct(rectangle.Width, rectangle.Height)));
    }

    /// <summary>把数值绑定到给定单位 / Bind a value to the given unit.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <param name="sample">单位样本 / Unit sample.</param>
    /// <returns>量值 / Quantity.</returns>
    private static Quantity<FltX> InUnit(FltX value, Quantity<FltX> sample) =>
        new(value, sample.Unit);

    /// <summary>取量值的零值 / Zero value of a quantity.</summary>
    /// <param name="sample">单位样本 / Unit sample.</param>
    /// <returns>零量值 / Zero quantity.</returns>
    private static FltX ZeroOf(Quantity<FltX> sample) =>
        QuantityOps.QuantityZeroOf(sample).Value;
}
