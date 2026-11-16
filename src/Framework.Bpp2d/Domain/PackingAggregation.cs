// 二维装箱聚合 / 2D packing aggregation.
// 与固定 Kotlin 对应：三维侧的 `PackedBin` 在二维侧成为 `PackedSheet`
// （一块板材及其上的放置），聚合结果再与物料汇总、附加信息一起构成 `PackingResult`。
// The 3D `PackedBin` becomes `PackedSheet` (one sheet plus its placements), and the
// aggregation together with the material summary and extra info forms `PackingResult`.
//
// **两处类型替换，写在文件头而不是让调用方猜**：
//   1. 基线 `PackedSheet.type` 是 `SheetType<FltX>`（带板材类型编码、批次等仓储语义），该类型未实现；这里用
//      `Sheet2Shape<FltX>`——聚合只需要宽、高与面积，恰好是该类型的全部内容。
//   2. 基线 `MaterialSummary.material` 是 `MaterialKey`（未实现的开放类），这里用物料编号文本 `MaterialNo`。
//      用值对象而非裸 `string`，这样"物料编号"不会和别的字符串参数互相串位。
// **Two type substitutions, stated here rather than left for the caller to guess**: (1) its
// `PackedSheet.type` is `SheetType<FltX>` (carrying type code and batch/inventory semantics) which is not implemented;
// `Sheet2Shape<FltX>` is used instead, since aggregation needs only width, height and area — exactly its contents.
// (2) `MaterialSummary.material` is its `MaterialKey`, implemented in earlier alongside `Material`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 已装箱物品：放置信息与装载顺序 / Packed item: the placement plus its loading order.
/// </summary>
/// <param name="Placement">物品在板材上的二维放置 / 2D placement on the sheet.</param>
/// <param name="LoadingOrder">板材内的装载顺序，首个为 1 / Loading order inside the sheet, the first being 1.</param>
/// <param name="ItemId">物品编号文本 / Plain item identifier text.</param>
public sealed record PackedItem(
    Placement2<FltX> Placement,
    ulong LoadingOrder,
    string? ItemId = null);

/// <summary>
/// 已装箱板材：一块板材及其上的物品 / Packed sheet: one sheet and the items on it.
/// </summary>
/// <remarks>
/// <see cref="ActualArea"/> 与 <see cref="LoadingRate"/> 都以**实际占用面积**为口径：圆形件按 πr² 计入，
/// 而非外接矩形面积。用外接面积会把装载率算高，让本该再排一件的板材看起来已满。
///
/// Both <see cref="ActualArea"/> and <see cref="LoadingRate"/> use the **actual occupied area**: circles count by πr²,
/// not by their enclosing rectangle. Using the enclosing area inflates the loading rate and makes a sheet that could
/// still take one more item look full.
/// </remarks>
/// <param name="Name">板材实例名称 / Sheet instance name.</param>
/// <param name="Type">板材形状 / Sheet shape.</param>
/// <param name="TypeCode">板材类型编码 / Sheet type code.</param>
/// <param name="BatchNo">批次号 / Batch number.</param>
/// <param name="Items">已装箱物品列表 / Packed item list.</param>
public sealed record PackedSheet(
    string Name,
    Sheet2Shape<FltX> Type,
    BatchNo? BatchNo = null,
    IReadOnlyList<PackedItem>? Items = null,
    SheetTypeId? TypeCode = null) {
    /// <summary>已装箱物品列表，永不为 null / Packed items, never null.</summary>
    public IReadOnlyList<PackedItem> PackedItems => Items ?? Array.Empty<PackedItem>();

    /// <summary>板材实际占用面积，无物品时为零 / Actually occupied area, zero when it holds no item.</summary>
    public Quantity<FltX> ActualArea =>
        QuantityCore.Sum(PackedItems.Select(item => item.Placement.Area))
        ?? QuantityOps.QuantityZeroOf(Type.Width);

    /// <summary>
    /// 板材装载率：实际占用面积与板材面积之比，板材面积非正时为零 / Loading rate, zero when the sheet area is
    /// not positive.
    /// </summary>
    public FltX LoadingRate {
        get {
            Quantity<FltX> sheetArea = Type.Area;
            if (!QuantityCore.IsPositive(sheetArea)) {
                return QuantityOps.QuantityZeroOf(sheetArea).Value;
            }
            return ActualArea.Value.Div(sheetArea.Value);
        }
    }
}

/// <summary>
/// 一次装箱结果中所有板材的聚合 / Aggregation of all sheets in one packing result.
/// </summary>
/// <param name="Bins">已装箱板材列表 / Packed sheet list.</param>
public sealed record PackingAggregation(IReadOnlyList<PackedSheet> Bins);

/// <summary>
/// 物料使用汇总 / Material usage summary.
/// </summary>
/// <param name="Material">物料键 / Material key.</param>
/// <param name="Amount">该物料的总件数 / Total amount of the material.</param>
public sealed record MaterialSummary(MaterialKey Material, ulong Amount);

/// <summary>
/// 最终装箱结果 / Final packing result.
/// </summary>
/// <param name="Aggregation">所有板材的装箱聚合 / Packing aggregation over all sheets.</param>
/// <param name="MaterialSummary">物料使用汇总 / Material usage summary.</param>
/// <param name="Remnants">每块板材的余料矩形 / Remnant rectangles per sheet.</param>
/// <param name="Info">附加键值信息 / Additional key-value information.</param>
public sealed record PackingResult(
    PackingAggregation Aggregation,
    IReadOnlyList<MaterialSummary>? MaterialSummary = null,
    IReadOnlyList<Remnant2>? Remnants = null,
    IReadOnlyDictionary<string, string>? Info = null) {
    /// <summary>物料使用汇总，永不为 null / Material usage summary, never null.</summary>
    public IReadOnlyList<MaterialSummary> MaterialSummaries =>
        MaterialSummary ?? Array.Empty<MaterialSummary>();

    /// <summary>余料矩形，永不为 null / Remnant rectangles, never null.</summary>
    public IReadOnlyList<Remnant2> RemnantList => Remnants ?? Array.Empty<Remnant2>();

    /// <summary>附加键值信息，永不为 null / Additional info, never null.</summary>
    public IReadOnlyDictionary<string, string> ExtraInfo =>
        Info ?? new Dictionary<string, string>(StringComparer.Ordinal);
}
