#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;

/// <summary>
/// 装箱快照 / A packing snapshot.
/// </summary>
/// <param name="SheetCount">板材数 / The sheet count.</param>
/// <param name="ItemCount">件数 / The item count.</param>
/// <param name="ActualArea">实际占用面积 / The actual occupied area.</param>
/// <param name="BoundingArea">外接面积 / The bounding area.</param>
/// <param name="SheetArea">板材总面积 / The total sheet area.</param>
/// <param name="RemainingArea">剩余面积 / The remaining area.</param>
/// <param name="AreaUtilization">面积利用率 / The area utilisation.</param>
/// <param name="LoadingRate">平均装载率 / The average loading rate.</param>
/// <param name="RectangleCount">矩形件数 / The rectangle count.</param>
/// <param name="CircleCount">圆形件数 / The circle count.</param>
/// <param name="RemnantCount">余料块数 / The remnant count.</param>
/// <param name="RemnantArea">余料面积 / The remnant area.</param>
/// <remarks>
/// 与基线 `ColumnGenerationPackingSnapshot` 对齐。**三处口径必须沿用**：
///
/// 1. **`ActualArea` 与 `BoundingArea` 是两个数**，不能用一个顶：圆形件按 πr² 计实际面积，按 2r×2r 计外接面积。
///    只留一个会让"圆形件多的方案"看起来和方形方案一样好。
/// 2. **`LoadingRate` 是各板材装载率的平均值**，不是总面积比——板材大小不同时两者不等。
/// 3. **`RemainingArea` 钳到零**：实际面积超过板材面积时（重叠摆放）不返回负数。
///
/// Aligned with its `ColumnGenerationPackingSnapshot`. **Three conventions must be copied**: `ActualArea` and `BoundingArea` are **two separate numbers** rather than one
/// standing in for the other, since a circle counts πr² of actual area and 2r×2r of bounding area and keeping only one makes a circle-heavy plan look as good as a rectangular one;
/// `LoadingRate` is the **average of the per-sheet rates** rather than a ratio of totals, which differ whenever sheets have different sizes; and `RemainingArea` is **clamped at zero** so an
/// overfilled sheet (overlapping placements) does not report a negative number.
/// </remarks>
public sealed record ColumnGenerationPackingSnapshot(
    int SheetCount,
    int ItemCount,
    FltX ActualArea,
    FltX BoundingArea,
    FltX SheetArea,
    FltX RemainingArea,
    FltX AreaUtilization,
    FltX LoadingRate,
    int RectangleCount,
    int CircleCount,
    int RemnantCount = 0,
    FltX? RemnantArea = null)
{
    /// <summary>余料面积（未给则零）/ The remnant area, zero when unset</summary>
    public FltX RemnantAreaValue => RemnantArea ?? new FltX(0.0);

    /// <summary>
    /// 按不变区域性格式化一个数 / Format a number under the invariant culture.
    /// </summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>文本 / The text.</returns>
    private static string Format(FltX value) =>
        value.ToFlt64().ToDouble().ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// 转成键值信息 / Convert to key-value information.
    /// </summary>
    /// <returns>键值信息 / The key-value information.</returns>
    /// <remarks>
    /// **键名照做**（`sheet_count` 等蛇形名）：这些键会被下游当契约读，改成 C# 风格的 PascalCase 会静默丢字段。
    /// 数值统一走 <see cref="Format"/>：`FltX.ToString` 不接 `IFormatProvider`，直接用它会让小数点随区域设置变成逗号；
/// `FltX.Value` 是 `internal`，所以经 `ToFlt64.ToDouble` 取数。
    ///
    /// **The key names are copied from the code** (snake case such as `sheet_count`): downstream code reads them as a contract, and switching to C#-style PascalCase would silently
    /// drop fields. Values are formatted with the invariant culture, or the decimal separator would become a comma under some locales.
    /// </remarks>
    public IReadOnlyDictionary<string, string> ToInfo()
    {
        return new Dictionary<string, string>
        {
            ["sheet_count"] = SheetCount.ToString(),
            ["item_count"] = ItemCount.ToString(),
            ["actual_area"] = Format(ActualArea),
            ["bounding_area"] = Format(BoundingArea),
            ["sheet_area"] = Format(SheetArea),
            ["remaining_area"] = Format(RemainingArea),
            ["area_utilization"] = Format(AreaUtilization),
            ["loading_rate"] = Format(LoadingRate),
            ["rectangle_count"] = RectangleCount.ToString(),
            ["circle_count"] = CircleCount.ToString(),
            ["remnant_count"] = RemnantCount.ToString(),
            ["remnant_area"] = Format(RemnantAreaValue),
        };
    }
}

/// <summary>
/// 装箱分析器 / The packing analyser.
/// </summary>
/// <remarks>
/// 与基线 `ColumnGenerationPackingAnalyzer` 对齐：把一次装箱结果压成一个快照，供列生成的心跳与日志读。
/// 它**不改装箱结果**，只读——所以没有失败路径，也不需要 `Result`。
///
/// Aligned with its `ColumnGenerationPackingAnalyzer`: it flattens a packing result into one snapshot for the column-generation heartbeat and logs to read. It **does not modify the
/// packing result**, only reads it, so it has no failure path and needs no `Result`.
/// </remarks>
public sealed class ColumnGenerationPackingAnalyzer
{
    /// <summary>
    /// 分析装箱结果 / Analyse a packing result.
    /// </summary>
    /// <param name="result">列生成结果，仅用于校验 / The column-generation result, used only for validation.</param>
    /// <param name="packing">装箱结果 / The packing result.</param>
    /// <returns>快照 / The snapshot.</returns>
    public ColumnGenerationPackingSnapshot Analyze(
        ColumnGenerationResult result,
        PackingResult packing)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(packing);

        FltX actualArea = new(0.0);
        FltX boundingArea = new(0.0);
        FltX sheetArea = new(0.0);
        FltX loadingRateSum = new(0.0);
        int itemCount = 0;
        int rectangleCount = 0;
        int circleCount = 0;

        foreach (PackedSheet bin in packing.Aggregation.Bins)
        {
            sheetArea = sheetArea.Plus(bin.Type.Area.Value);
            loadingRateSum = loadingRateSum.Plus(bin.LoadingRate);
            foreach (PackedItem item in bin.PackedItems)
            {
                ++itemCount;
                actualArea = actualArea.Plus(item.Placement.Shape.Area.Value);
                boundingArea = boundingArea.Plus(item.Placement.Shape.BoundingArea.Value);
                switch (item.Placement.Shape.ShapeType)
                {
                    case PackingShape2Type.Rectangle:
                        ++rectangleCount;
                        break;
                    case PackingShape2Type.Circle:
                        ++circleCount;
                        break;
                    default:
                        break;
                }
            }
        }

        FltX remnantArea = new(0.0);
        foreach (Remnant2 remnant in packing.RemnantList)
        {
            remnantArea = remnantArea.Plus(remnant.Area.Value);
        }

        FltX areaUtilization = sheetArea.ToFlt64().ToDouble() > 0.0
            ? actualArea.Div(sheetArea)
            : new FltX(0.0);
        int sheetCount = packing.Aggregation.Bins.Count;
        FltX averageLoadingRate = sheetCount == 0
            ? new FltX(0.0)
            : loadingRateSum.Div(new FltX(sheetCount));
        // **钳到零**：重叠摆放时实际面积会超过板材面积。
        // **Clamped at zero**: overlapping placements can push the actual area past the sheet area.
        FltX remainingArea = sheetArea.ToFlt64().ToDouble() > actualArea.ToFlt64().ToDouble()
            ? sheetArea.Minus(actualArea)
            : new FltX(0.0);

        return new ColumnGenerationPackingSnapshot(
            SheetCount: sheetCount,
            ItemCount: itemCount,
            ActualArea: actualArea,
            BoundingArea: boundingArea,
            SheetArea: sheetArea,
            RemainingArea: remainingArea,
            AreaUtilization: areaUtilization,
            LoadingRate: averageLoadingRate,
            RectangleCount: rectangleCount,
            CircleCount: circleCount,
            RemnantCount: packing.RemnantList.Count,
            RemnantArea: remnantArea);
    }
}
