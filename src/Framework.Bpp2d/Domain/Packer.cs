// 装箱器 / Packer.
// 与固定 Kotlin 基线 `bpp2d/domain/packing/service/Packer.kt` 对应：把排样完成的板材展开为二维放置列表，计算装载
// 顺序、校验几何并汇总物料，输出 `PackingResult`。
// Mirrors the pinned baseline: expands the laid-out sheets into 2D placements, computes the loading order, validates
// the geometry, summarises materials and emits a `PackingResult`.
//
// 层模型已在 Batch 82 移植，因此本类同时提供**接 `Sheet` 的正规入口**（`InvokeSheets`）与接扁平放置列表的入口
// （`Invoke`）。前者与基线签名一致，后者在只要放置、不关心层的场景下少一次包装。
// The layer model landed in Batch 82, so this offers both the **proper `Sheet` entry point** (`InvokeSheets`) and the
// flat-placement one (`Invoke`). The former matches the baseline signature; the latter skips a wrapper when only
// placements matter.
//
// 物料汇总在 Batch 83 接通：`MaterialKey`／`Material` 与 `IAbstractItem.MaterialAmounts` 均已移植。
// The material summary came online in Batch 83: `MaterialKey` / `Material` and `IAbstractItem.MaterialAmounts` are all
// ported now.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 一块待装箱板材 / A sheet to pack.
/// </summary>
/// <param name="Type">板材形状 / Sheet shape.</param>
/// <param name="Placements">已放置的件 / Placed items.</param>
/// <param name="BatchNo">批次号 / Batch number.</param>
/// <param name="TypeCode">板材类型编码 / Sheet type code.</param>
public sealed record PackedSheetInput(
    Sheet2Shape<FltX> Type,
    IReadOnlyList<Placement2<FltX>> Placements,
    BatchNo? BatchNo = null,
    SheetTypeId? TypeCode = null);

/// <summary>
/// 装箱器，把最终板材转换为装箱结果 / Packer converting the final sheets into a packing result.
/// </summary>
/// <param name="ItemCatalog">物品目录，用于旋转合法性判定；缺失的物品视为允许旋转 / Item catalogue for rotation
/// legality; a missing item is treated as allowing rotation.</param>
public sealed record Packer(
    IReadOnlyDictionary<string, IAbstractItem<FltX>>? ItemCatalog = null) {
    /// <summary>
    /// 展开板材实例并生成装箱结果 / Expand sheet instances and build the packing result.
    /// </summary>
    /// <param name="sheets">最终板材列表 / Final sheet list.</param>
    /// <param name="context">装箱上下文 / Packing context.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>装箱结果或失败 / The packing result, or a failure.</returns>
    public Result<PackingResult, ErrorCode, Error<ErrorCode>> InvokeSheets(
        IReadOnlyList<Sheet<FltX>> sheets,
        PackingContext? context = null,
        Quantity<FltX>? kerf = null) {
        ArgumentNullException.ThrowIfNull(sheets);
        return Invoke(
            sheets.Select(sheet => new PackedSheetInput(
                Type: new Sheet2Shape<FltX>(sheet.Width, sheet.Height),
                Placements: sheet.Placements,
                BatchNo: sheet.BatchNo,
                TypeCode: sheet.Type.TypeCode)).ToList(),
            context,
            kerf);
    }

    /// <summary>
    /// 展开板材并生成装箱结果 / Expand the sheets and build the packing result.
    /// </summary>
    /// <param name="sheets">最终板材列表 / Final sheet list.</param>
    /// <param name="context">装箱上下文 / Packing context.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>装箱结果或失败 / The packing result, or a failure.</returns>
    public Result<PackingResult, ErrorCode, Error<ErrorCode>> Invoke(
        IReadOnlyList<PackedSheetInput> sheets,
        PackingContext? context = null,
        Quantity<FltX>? kerf = null) {
        ArgumentNullException.ThrowIfNull(sheets);

        IPackingRotationRule rotationRule =
            PackingRotationRule.ByItemCatalog(
                ItemCatalog ?? new Dictionary<string, IAbstractItem<FltX>>());

        var packedSheets = new List<PackedSheet>(sheets.Count);
        for (int index = 0; index < sheets.Count; index++) {
            PackedSheetInput sheet = sheets[index];
            string name = string.Format(CultureInfo.InvariantCulture, "sheet-{0}", index + 1);

            Result<Success, ErrorCode, Error<ErrorCode>> validation = PackingGeometryGuard.Validate(
                sheet: sheet.Type,
                placements: sheet.Placements,
                sheetName: name,
                source: "Packer.invoke",
                rotationRule: rotationRule,
                kerf: kerf);
            if (!validation.IsOk) {
                return validation switch {
                    Failed<Success, ErrorCode, Error<ErrorCode>> failure =>
                        Results.Failed<PackingResult>(failure.Error),
                    Fatal<Success, ErrorCode, Error<ErrorCode>> fatal =>
                        new Fatal<PackingResult, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                    _ => Results.Failed<PackingResult>(new Bpp2dInternalError(
                        "装箱校验内部失败 / Packing validation failed internally")),
                };
            }

            IReadOnlyList<ulong> orders = LoadingOrderCalculator.Invoke(sheet.Placements);
            packedSheets.Add(new PackedSheet(
                Name: name,
                Type: sheet.Type,
                BatchNo: sheet.BatchNo,
                TypeCode: sheet.TypeCode,
                Items: sheet.Placements
                    .Select((placement, itemIndex) => new PackedItem(
                        Placement: placement,
                        LoadingOrder: orders[itemIndex],
                        ItemId: placement.ItemId))
                    .ToList()));
        }

        IReadOnlyList<Remnant2> remnants = FreeSpaceAnalyzer.Analyze(
            sheets.Select(sheet => (sheet.Type, sheet.Placements)).ToList());

        return Results.Ok(new PackingResult(
            Aggregation: new PackingAggregation(packedSheets),
            MaterialSummary: SummarizeMaterials(packedSheets),
            Remnants: remnants,
            Info: context?.ExtraInfo));
    }

    /// <summary>
    /// 汇总物料使用情况 / Summarise the material usage.
    /// </summary>
    /// <remarks>
    /// 物料件数来自物品目录中每个已装箱物品的 `MaterialAmounts`；**目录中缺失的物品不计入**——这是刻意的取舍：
    /// 宁可少报，也不凭放置上的编号猜一个物料用量（猜错会让采购按错误数量下单）。
    /// Material amounts come from the `MaterialAmounts` of every packed item in the catalogue; **items missing from the
    /// catalogue are not counted**. That is deliberate: under-reporting is preferable to guessing a material amount from
    /// the id on a placement, since a wrong guess has procurement ordering the wrong quantity.
    /// </remarks>
    /// <param name="sheets">已装箱板材列表 / Packed sheet list.</param>
    /// <returns>物料汇总列表 / Material summary list.</returns>
    private IReadOnlyList<MaterialSummary> SummarizeMaterials(IReadOnlyList<PackedSheet> sheets) {
        IReadOnlyDictionary<string, IAbstractItem<FltX>> catalog =
            ItemCatalog ?? new Dictionary<string, IAbstractItem<FltX>>();

        IEnumerable<IReadOnlyDictionary<MaterialKey, ulong>> maps = sheets
            .SelectMany(sheet => sheet.PackedItems)
            .Where(packed => packed.ItemId is not null)
            .Select(packed => catalog.TryGetValue(packed.ItemId!, out IAbstractItem<FltX>? item)
                ? item.MaterialAmounts
                : null)
            .Where(map => map is not null)
            .Select(map => map!);
        return MaterialSummaryOps.Accumulate(maps);
    }
}
