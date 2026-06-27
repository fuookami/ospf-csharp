#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;

/// <summary>
/// 装箱结果 / Packing result.
/// </summary>
/// <param name="Aggregation">装箱聚合 / Packing aggregation</param>
/// <param name="MaterialSummary">物料汇总 / Material summary</param>
/// <param name="Info">附加信息 / Additional info</param>
public sealed record PackingResult(
    PackingAggregationNonGeneric Aggregation,
    IReadOnlyList<MaterialSummaryEntry> MaterialSummary,
    IReadOnlyDictionary<string, string>? Info = null);

/// <summary>
/// 装箱聚合（非泛型）/ Packing aggregation (non-generic).
/// </summary>
/// <param name="Bins">箱子列表 / Bin list</param>
public sealed record PackingAggregationNonGeneric(IReadOnlyList<PackingBin> Bins);

/// <summary>
/// 装箱中的箱子 / Packing bin.
/// </summary>
/// <param name="Items">物品列表 / Item list</param>
public sealed record PackingBin(IReadOnlyList<PackingItem> Items);

/// <summary>
/// 装箱中的货物 / Packing item.
/// </summary>
/// <param name="Item">物品 / Item</param>
/// <param name="Amount">数量 / Amount</param>
public sealed record PackingItem(Infra.Item Item, ulong Amount);

/// <summary>
/// 材质汇总条目 / Material summary entry.
/// </summary>
/// <param name="Material">物料键 / Material key</param>
/// <param name="Amount">数量 / Amount</param>
public sealed record MaterialSummaryEntry(MaterialKey Material, ulong Amount);

/// <summary>
/// 装箱中的已包装货物 / Packaged item.
/// </summary>
/// <param name="Item">物品 / Item</param>
/// <param name="Amount">数量 / Amount</param>
public sealed record PackagedItem(Infra.Item Item, ulong Amount);

/// <summary>
/// 物料装箱计划结果（非泛型）/ Material packing plan result (non-generic).
/// </summary>
/// <param name="SolveInfo">求解信息 / Solve info</param>
/// <param name="RestMaterials">剩余物料 / Rest materials</param>
/// <param name="PackagedItems">已包装货物 / Packaged items</param>
public sealed record MaterialPackingPlanResult(
    PackingSolveInfo SolveInfo,
    IReadOnlyDictionary<MaterialKey, ulong> RestMaterials,
    IReadOnlyList<PackagedItem> PackagedItems);

/// <summary>
/// 渲染方案 DTO / Schema DTO.
/// </summary>
/// <param name="Kpi">KPI 键值对 / KPI key-value pairs</param>
public sealed record SchemaDTO(IReadOnlyDictionary<string, string> Kpi);
