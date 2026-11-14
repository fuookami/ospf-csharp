#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;

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
public sealed record PackingBin(IReadOnlyList<PackingItem> Items) {
    /// <summary>箱号 / Bin name.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// 箱子类型；null 表示装箱路径没有提供容器尺寸 / The bin type; null means the packing path supplied no container extents.
    /// </summary>
    /// <remarks>
    /// **没有容器尺寸就没有"越界"的定义**：越界检测必须知道箱子的宽/高/深，而 `Items` 只能描述箱内有什么、不能描述箱子有多大。
    ///
    /// **Without container extents there is no meaning to "outside the bin"**: the check has to know the container's width/height/depth, and `Items`
    /// can only describe what is inside, never how large the box is.
    /// </remarks>
    public BinType<FltX>? Type { get; init; }

    /// <summary>批次号 / Batch number.</summary>
    public BatchNo? BatchNo { get; init; }

    /// <summary>分组 / Group label.</summary>
    public IReadOnlyList<string>? Group { get; init; }
}

/// <summary>
/// 装箱中的货物 / Packing item.
/// </summary>
/// <param name="Item">物品 / Item</param>
/// <param name="Amount">数量 / Amount</param>
/// <param name="Geometry">
/// 冻结的包围盒快照；null 表示装箱路径没有提供几何 / The frozen bounding-box snapshot; null means the packing path supplied no geometry.
/// </param>
/// <param name="LoadingOrder">装载顺序 / Loading order.</param>
public sealed record PackingItem(
    Infra.Item Item,
    ulong Amount,
    PackingItemGeometry? Geometry = null,
    ulong LoadingOrder = 0);

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
/// <param name="Kpi">KPI 指标 / KPI metrics.</param>
/// <param name="LoadingPlans">
/// 装载计划列表；null 表示调用方没有要求渲染计划 / The loading plans; null means the caller did not ask for a rendering plan.
/// </param>
public sealed record SchemaDTO(
    IReadOnlyDictionary<string, string> Kpi,
    IReadOnlyList<Infra.RenderLoadingPlanDto>? LoadingPlans = null);
