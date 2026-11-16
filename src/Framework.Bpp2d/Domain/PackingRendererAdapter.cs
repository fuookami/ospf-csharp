// 渲染 DTO 与装箱渲染适配 / Render DTOs and the packing render adapter.
// And.
//
// 面向外部渲染器的 DTO 一律使用 `double` 而非量纲类型：**跨界即降维**，渲染器不关心单位语义，只关心数值。
// 因此这里的转换是唯一允许"丢掉单位"的地方，量纲校验必须发生在转换之前。
// DTOs destined for an external renderer use `double` rather than dimensioned types: **crossing the boundary means
// dropping dimensions**, since a renderer cares about numbers, not unit semantics. This conversion is therefore the
// only place where dropping units is allowed, and dimension validation must happen before it.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 渲染形状类型 / Render shape type.
/// </summary>
public enum RenderShape2TypeDto {
    /// <summary>矩形件 / Rectangle.</summary>
    Rectangle,

    /// <summary>圆形件 / Circle.</summary>
    Circle,
}

/// <summary>
/// 二维矩形描述 / 2D rectangle description.
/// </summary>
/// <param name="X">左下角 x 坐标 / Lower-left x coordinate.</param>
/// <param name="Y">左下角 y 坐标 / Lower-left y coordinate.</param>
/// <param name="Width">宽度 / Width.</param>
/// <param name="Height">高度 / Height.</param>
public sealed record RenderRectangle2Dto(double X, double Y, double Width, double Height);

/// <summary>
/// 二维板材渲染描述 / 2D sheet render description.
/// </summary>
/// <param name="Id">板材编号 / Sheet identifier.</param>
/// <param name="TypeCode">板材类型编码 / Sheet type code.</param>
/// <param name="Width">板材宽度 / Sheet width.</param>
/// <param name="Height">板材高度 / Sheet height.</param>
/// <param name="Area">板材面积 / Sheet area.</param>
/// <param name="LoadingRate">板材装载率 / Sheet loading rate.</param>
public sealed record RenderSheet2Dto(
    string Id,
    string TypeCode,
    double Width,
    double Height,
    double Area,
    double LoadingRate);

/// <summary>
/// 二维物品渲染描述 / 2D item render description.
/// </summary>
/// <param name="Name">物品名称 / Item name.</param>
/// <param name="ShapeType">渲染形状类型 / Render shape type.</param>
/// <param name="X">左下角 x 坐标 / Lower-left x coordinate.</param>
/// <param name="Y">左下角 y 坐标 / Lower-left y coordinate.</param>
/// <param name="BoundingWidth">包围宽度 / Bounding width.</param>
/// <param name="BoundingHeight">包围高度 / Bounding height.</param>
/// <param name="ActualArea">实际占用面积 / Actual occupied area.</param>
/// <param name="BoundingArea">外接矩形面积 / Enclosing rectangle area.</param>
/// <param name="Radius">圆形件半径 / Circle radius.</param>
/// <param name="Diameter">圆形件直径 / Circle diameter.</param>
/// <param name="Rotated">是否旋转 90 度 / Whether rotated by 90 degrees.</param>
/// <param name="LoadingOrder">装载顺序 / Loading order.</param>
/// <param name="Info">附加信息 / Additional info.</param>
public sealed record RenderItem2Dto(
    string Name,
    RenderShape2TypeDto ShapeType,
    double X,
    double Y,
    double BoundingWidth,
    double BoundingHeight,
    double ActualArea,
    double BoundingArea,
    double? Radius = null,
    double? Diameter = null,
    bool Rotated = false,
    long LoadingOrder = 0L,
    IReadOnlyDictionary<string, string>? Info = null);

/// <summary>
/// 二维装载方案 / 2D loading plan.
/// </summary>
/// <param name="Name">方案名称 / Plan name.</param>
/// <param name="Sheet">板材描述 / Sheet description.</param>
/// <param name="Items">物品描述列表 / Item descriptions.</param>
/// <param name="Kpi">关键指标 / Key indicators.</param>
public sealed record RenderLoadingPlan2Dto(
    string Name,
    RenderSheet2Dto Sheet,
    IReadOnlyList<RenderItem2Dto> Items,
    IReadOnlyDictionary<string, string>? Kpi = null);

/// <summary>
/// 二维渲染方案 / 2D render schema.
/// </summary>
/// <param name="Plans">装载方案列表 / Loading plans.</param>
/// <param name="Summary">汇总信息 / Summary information.</param>
public sealed record RenderSchema2Dto(
    IReadOnlyList<RenderLoadingPlan2Dto> Plans,
    IReadOnlyDictionary<string, string>? Summary = null);

/// <summary>
/// 装箱渲染适配 / Packing render adapter.
/// </summary>
public static class PackingRendererAdapter {
    /// <summary>
    /// 把装箱结果转换为渲染方案 DTO / Convert a packing result into the render schema DTO.
    /// </summary>
    /// <remarks>
    /// 转换前**复用 `PackingGeometryGuard` 重新校验每块板材**：交给外部渲染器的数据必须自洽，否则渲染出来的图
    /// 看起来正常、实际与几何不符，排查时会被引向错误的结论。圆形件额外输出 `radius` 与 `diameter`；
    /// `actualArea` 始终是**实际占用面积**（圆形件为 πr²），`boundingArea` 另行给出。
    ///
    /// Each sheet is **re-validated through `PackingGeometryGuard`** before conversion: data handed to an external
    /// renderer must be self-consistent, otherwise the picture looks fine while contradicting the geometry and sends
    /// debugging down the wrong path. Circle items additionally carry `radius` and `diameter`; `actualArea` is always
    /// the **actual occupied area** (πr² for circles) with `boundingArea` reported separately.
    /// </remarks>
    /// <param name="result">装箱结果 / Packing result.</param>
    /// <returns>渲染方案 DTO 或失败 / The render schema DTO, or a failure.</returns>
    public static Result<RenderSchema2Dto, ErrorCode, Error<ErrorCode>> ToSchema(
        PackingResult result) {
        ArgumentNullException.ThrowIfNull(result);

        var plans = new List<RenderLoadingPlan2Dto>(result.Aggregation.Bins.Count);
        foreach (PackedSheet packedSheet in result.Aggregation.Bins) {
            Result<Success, ErrorCode, Error<ErrorCode>> validation =
                PackingGeometryGuard.RequireSheetGeometry(
                    sheet: packedSheet.Type,
                    placements: packedSheet.PackedItems.Select(item => item.Placement).ToList(),
                    sheetName: packedSheet.Name,
                    source: "PackingRendererAdapter.toSchema");
            if (!validation.IsOk) {
                return validation switch {
                    Failed<Success, ErrorCode, Error<ErrorCode>> failure =>
                        Results.Failed<RenderSchema2Dto>(failure.Error),
                    Fatal<Success, ErrorCode, Error<ErrorCode>> fatal =>
                        new Fatal<RenderSchema2Dto, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                    _ => Results.Failed<RenderSchema2Dto>(new Bpp2dInternalError(
                        "渲染校验内部失败 / Render validation failed internally")),
                };
            }

            var items = new List<RenderItem2Dto>(packedSheet.PackedItems.Count);
            for (int index = 0; index < packedSheet.PackedItems.Count; index++) {
                items.Add(ToItemDto(packedSheet.PackedItems[index], packedSheet.Name, index));
            }

            double loadingRate = packedSheet.LoadingRate.ToFlt64().ToDouble();
            plans.Add(new RenderLoadingPlan2Dto(
                Name: packedSheet.Name,
                Sheet: new RenderSheet2Dto(
                    Id: packedSheet.Name,
                    TypeCode: packedSheet.TypeCode?.Value ?? string.Empty,
                    Width: packedSheet.Type.Width.Value.ToFlt64().ToDouble(),
                    Height: packedSheet.Type.Height.Value.ToFlt64().ToDouble(),
                    Area: packedSheet.Type.Area.Value.ToFlt64().ToDouble(),
                    LoadingRate: loadingRate),
                Items: items,
                Kpi: new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["item_count"] = items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["loading_rate"] = loadingRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }));
        }

        return Results.Ok(new RenderSchema2Dto(
            Plans: plans,
            Summary: SummaryOf(result, plans.Count)));
    }

    /// <summary>
    /// 把已装箱物品转换为渲染物品 DTO / Convert a packed item into the render item DTO.
    /// </summary>
    /// <param name="packed">已装箱物品 / Packed item.</param>
    /// <param name="sheetName">所属板材名称 / Owning sheet name.</param>
    /// <param name="index">物品在板材内的序号 / Item index inside the sheet.</param>
    /// <returns>渲染物品 DTO / The render item DTO.</returns>
    private static RenderItem2Dto ToItemDto(PackedItem packed, string sheetName, int index) {
        Placement2<FltX> placement = packed.Placement;
        PackingShape2<FltX> shape = placement.Shape;
        var circle = shape as CirclePackingShape2<FltX>;
        return new RenderItem2Dto(
            Name: packed.ItemId ?? string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}-item-{1}",
                sheetName,
                index + 1),
            ShapeType: shape.ShapeType == PackingShape2Type.Rectangle
                ? RenderShape2TypeDto.Rectangle
                : RenderShape2TypeDto.Circle,
            X: placement.X.Value.ToFlt64().ToDouble(),
            Y: placement.Y.Value.ToFlt64().ToDouble(),
            BoundingWidth: shape.BoundingWidth.Value.ToFlt64().ToDouble(),
            BoundingHeight: shape.BoundingHeight.Value.ToFlt64().ToDouble(),
            ActualArea: shape.Area.Value.ToFlt64().ToDouble(),
            BoundingArea: shape.BoundingArea.Value.ToFlt64().ToDouble(),
            Radius: circle?.Radius.Value.ToFlt64().ToDouble(),
            Diameter: circle?.Diameter.Value.ToFlt64().ToDouble(),
            Rotated: placement.Rotated,
            LoadingOrder: (long)packed.LoadingOrder,
            Info: new Dictionary<string, string>(StringComparer.Ordinal) {
                ["sheet"] = sheetName,
            });
    }

    /// <summary>
    /// 汇总渲染信息，调用方附加信息最后合并 / Summarise the render information, merging caller info last.
    /// </summary>
    /// <remarks>
    /// 调用方附加信息**最后写入**，因此可以覆盖上面的统计键。这是有意的：调用方比框架更清楚本次渲染想强调什么。
    /// Caller info is written **last** and can therefore override the statistic keys above. That is intentional: the
    /// caller knows better than the framework what this particular render should emphasise.
    /// </remarks>
    /// <param name="result">装箱结果 / Packing result.</param>
    /// <param name="sheetCount">板材数量 / Sheet count.</param>
    /// <returns>汇总信息 / Summary information.</returns>
    private static IReadOnlyDictionary<string, string> SummaryOf(
        PackingResult result,
        int sheetCount) {
        Dictionary<string, string> summary = new(StringComparer.Ordinal) {
            ["sheet_count"] = sheetCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["item_count"] = result.Aggregation.Bins
                .Sum(bin => bin.PackedItems.Count)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["material_count"] = result.MaterialSummaries.Count
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (MaterialSummary materialSummary in result.MaterialSummaries) {
            summary[string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "material:{0}",
                materialSummary.Material)] =
                materialSummary.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        foreach (KeyValuePair<string, string> pair in result.ExtraInfo) {
            summary[pair.Key] = pair.Value;
        }
        return summary;
    }
}
