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
/// 渲染适配器 / Renderer adapter.
/// 将装箱结果转换为渲染 DTO / Converts packing results to rendering DTOs.
/// </summary>
/// <remarks>
/// 实现（12325 B）的**装载计划产出**部分。
///
/// **修复前**：`ToSchema` 只产出两个 KPI（`bin_count` / `material_count`），**一条装载计划都没有**，
/// 于是"渲染成功"与"渲染出空图"完全无法区分。
///
/// Ports the **loading-plan production** half of its (12325 B).
/// **Before the fix**: `ToSchema` produced two KPIs and **not a single loading plan**, so "rendered successfully" and "rendered an empty picture"
/// were indistinguishable.
/// </remarks>
public sealed class PackingRendererAdapter {
    /// <summary>
    /// 将装箱结果转换为渲染方案 DTO / Convert packing result to rendering schema DTO.
    /// </summary>
    /// <param name="result">装箱结果 / Packing result</param>
    /// <returns>渲染方案 DTO / Rendering schema DTO</returns>
    public Result<SchemaDTO, ErrorCode, Error<ErrorCode>> ToSchema(PackingResult result) =>
        ToSchema(result, null);

    /// <summary>
    /// 将装箱结果转换为渲染方案 DTO，并回写求解器选出的圆柱半径 / Convert packing result to a rendering schema DTO, writing back the solver-selected
    /// cylinder radii.
    /// </summary>
    /// <remarks>
    /// 这是 `toSchema(result, continuousRadiusSelectionResults)` 的第二半。**只有无参版本是不够的**：
    /// 连续半径求解器把半径作为**变量**交给 MILP，选中的值只存在于解向量里；不回写进 DTO，渲染出的圆柱就仍是**标称半径**，
    /// 与实际装箱的体积、装载率都不一致——而两者在类型上完全相同。
    ///
    /// This is the second half of `toSchema(result, continuousRadiusSelectionResults)`. **The parameterless version alone is not enough**:
    /// the continuous-radius solver hands the radius to the MILP **as a variable**, so the selected value only exists in the solution vector. Without
    /// writing it back into the DTO, the rendered cylinder keeps its **nominal radius** and disagrees with the actual packed volume and loading rate — while
    /// both look identical in the types.
    /// </remarks>
    /// <param name="result">装箱结果 / Packing result.</param>
    /// <param name="continuousRadiusSelectionResults">
    /// 求解器选出的半径，键为变量名或货物编号 / Solver-selected radii, keyed by variable name or item id.
    /// </param>
    /// <returns>渲染方案 DTO / Rendering schema DTO</returns>
    public Result<SchemaDTO, ErrorCode, Error<ErrorCode>> ToSchema(
        PackingResult result,
        IReadOnlyDictionary<string, double>? continuousRadiusSelectionResults) {
        ArgumentNullException.ThrowIfNull(result);

        var kpi = new Dictionary<string, string> {
            ["bin_count"] = result.Aggregation.Bins.Count.ToString(),
            ["material_count"] = result.MaterialSummary.Count.ToString()
        };
        if (result.Info is not null) {
            foreach (KeyValuePair<string, string> kvp in result.Info) {
                kpi[kvp.Key] = kvp.Value;
            }
        }

        var plans = new List<RenderLoadingPlanDto>();
        foreach (PackingBin bin in result.Aggregation.Bins) {
            // 空箱子没有可校验的几何；非空箱子先过几何守卫，通过后再产出计划。
            // An empty bin has no geometry to verify; a non-empty bin goes through the geometry guard before a plan is produced.
            if (bin.Items.Count > 0) {
                Result<Success, ErrorCode, Error<ErrorCode>> validation =
                    PackingGeometryGuard.RequirePackedBinShapeGeometry(bin, "PackingRendererAdapter.toSchema");
                if (validation is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                    return new Failed<SchemaDTO, ErrorCode, Error<ErrorCode>>(failed.Error);
                }
            }
            plans.Add(ToPlan(bin, continuousRadiusSelectionResults));
        }

        return new Ok<SchemaDTO, ErrorCode, Error<ErrorCode>>(new SchemaDTO(kpi, plans));
    }

    /// <summary>
    /// 单个箱子的渲染装载计划 / Rendering loading plan of a single bin.
    /// </summary>
    /// <param name="bin">已装箱的箱子 / Packed bin.</param>
    /// <param name="continuousRadiusSelectionResults">求解器选出的半径 / Solver-selected radii.</param>
    /// <returns>装载计划 DTO / The loading plan DTO.</returns>
    private static RenderLoadingPlanDto ToPlan(
        PackingBin bin,
        IReadOnlyDictionary<string, double>? continuousRadiusSelectionResults) {
        double containerWidth = bin.Type?.Width.ToDouble() ?? 0.0;
        double containerHeight = bin.Type?.Height.ToDouble() ?? 0.0;
        double containerDepth = bin.Type?.Depth.ToDouble() ?? 0.0;
        double containerVolume = containerWidth * containerHeight * containerDepth;

        // **按装载顺序输出**：无序的渲染图看不出装载动作的先后。
        // **Emit in loading order**: an unordered rendering cannot show the sequence of loading actions.
        var items = bin.Items
            .OrderBy(item => item.LoadingOrder)
            .Select(item => ToItem(item, continuousRadiusSelectionResults))
            .ToList();
        double usedVolume = items.Sum(item => item.ActualVolume ?? 0.0);
        // **重量按箱累加**：逐件重量来自货物本身，箱级重量是它们的和，不是另算一个数。
        // **Weights add up per bin**: each item's weight comes from the item itself, and the bin-level weight is their sum rather than a separate number.
        double usedWeight = items.Sum(item => item.Weight);
        double loadingRate = containerVolume > 0.0 ? usedVolume / containerVolume : 0.0;

        return new RenderLoadingPlanDto(
            Group: bin.Group is null ? null : string.Join("|", bin.Group),
            Name: bin.Name ?? string.Empty,
            TypeCode: bin.Type?.TypeCode ?? string.Empty,
            Width: containerWidth,
            Height: containerHeight,
            Depth: containerDepth,
            LoadingRate: loadingRate,
            Weight: usedWeight,
            Volume: usedVolume,
            Items: items);
    }

    /// <summary>
    /// 单个货物的渲染 DTO / Rendering DTO of a single item.
    /// </summary>
    /// <param name="item">装箱物品 / Packed item.</param>
    /// <param name="continuousRadiusSelectionResults">求解器选出的半径 / Solver-selected radii.</param>
    /// <returns>物品 DTO / The item DTO.</returns>
    private static RenderLoadingPlanItemDto ToItem(
        PackingItem item,
        IReadOnlyDictionary<string, double>? continuousRadiusSelectionResults) {
        PackingItemGeometry? geometry = item.Geometry;
        if (geometry is null) {
            // 走到这里意味着调用方绕过了几何守卫。给出零尺寸而不是崩溃，但**顺序与名称仍如实传出**。
            // Reaching here means the caller bypassed the geometry guard. Emit zeros rather than crash, but **still report the order and the name truthfully**.
            return new RenderLoadingPlanItemDto(
                Name: item.Item.Name,
                PackageType: null,
                Width: 0.0, Height: 0.0, Depth: 0.0,
                X: 0.0, Y: 0.0, Z: 0.0,
                Weight: item.Item.Weight?.ToDouble() ?? 0.0,
                LoadingOrder: (int)item.LoadingOrder,
                ShapeType: "unknown",
                RenderShapeType: RenderShapeType.Cuboid,
                AlgorithmShapeType: RenderAlgorithmShapeType.Cuboid);
        }

        bool isCylinder = geometry.IsCylinder;
        Axis3 axis = geometry.Axis ?? Axis3.Y;
        // **求解器选出的半径优先于标称半径**：两者不同时以求解器为准，否则渲染的圆柱与实际装箱的体积对不上。
        // **A solver-selected radius wins over the nominal one**: when they differ the solver decides, or the rendered cylinder disagrees with the packed volume.
        double radius = isCylinder
            ? SelectedRadiusOf(item, continuousRadiusSelectionResults) ?? geometry.Radius
            : geometry.Radius;
        RenderAlgorithmShapeType algorithm = !isCylinder
            ? RenderAlgorithmShapeType.Cuboid
            : axis switch {
                Axis3.X => RenderAlgorithmShapeType.HorizontalCylinderX,
                Axis3.Y => RenderAlgorithmShapeType.VerticalCylinder,
                _ => RenderAlgorithmShapeType.HorizontalCylinderZ,
            };
        return new RenderLoadingPlanItemDto(
            Name: item.Item.Name,
            PackageType: null,
            Width: geometry.Width,
            Height: geometry.Height,
            Depth: geometry.Depth,
            X: geometry.MinX,
            Y: geometry.MinY,
            Z: geometry.MinZ,
            Weight: item.Item.Weight?.ToDouble() ?? 0.0,
            LoadingOrder: (int)item.LoadingOrder,
            ShapeType: isCylinder ? "cylinder" : "cuboid",
            RenderShapeType: isCylinder ? RenderShapeType.Cylinder : RenderShapeType.Cuboid,
            AlgorithmShapeType: algorithm,
            Radius: isCylinder ? radius : null,
            Diameter: isCylinder ? radius * 2.0 : null,
            Axis: isCylinder ? axis : null,
            BoundingWidth: geometry.Width,
            BoundingHeight: geometry.Height,
            BoundingDepth: geometry.Depth,
            ActualVolume: Volume(geometry, isCylinder ? radius : null));
    }

    /// <summary>
    /// 取该货物被求解器选中的圆柱半径 / The cylinder radius the solver selected for this item.
    /// </summary>
    /// <remarks>
    /// 这里先按**变量名**查、再按**唯一键**查（同键多条视为有歧义而放弃），即先变量名、再货物编号；
    /// **查不到返回 null 而不是编一个数**，调用方退回标称半径。
    ///
    /// The lookup goes by **variable name** first and then by **unique key** (several results sharing a key count as ambiguous and are dropped).
    /// It goes variable name first, then item id, and **returns null rather than inventing a number** when neither hits, letting the caller
    /// fall back to the nominal radius.
    /// </remarks>
    /// <param name="item">装箱物品 / Packed item.</param>
    /// <param name="results">求解器选出的半径 / Solver-selected radii.</param>
    /// <returns>半径，或 null / The radius, or null.</returns>
    private static double? SelectedRadiusOf(
        PackingItem item,
        IReadOnlyDictionary<string, double>? results) {
        if (results is null || results.Count == 0) {
            return null;
        }
        string variableName = $"cr_item-{item.Item.Id}";
        if (results.TryGetValue(variableName, out double byVariable)) {
            return byVariable;
        }
        if (results.TryGetValue(item.Item.Id, out double byItem)) {
            return byItem;
        }
        return null;
    }

    /// <summary>
    /// 包围盒对应的实际体积 / Actual volume described by the bounding-box snapshot.
    /// </summary>
    /// <param name="geometry">几何快照 / Geometry snapshot.</param>
    /// <param name="radiusOverride">求解器选出的半径 / Solver-selected radius.</param>
    /// <returns>体积 / The volume.</returns>
    private static double Volume(PackingItemGeometry geometry, double? radiusOverride) {
        if (!geometry.IsCylinder) {
            return geometry.Width * geometry.Height * geometry.Depth;
        }
        // 圆柱体积按轴向取高度 / A cylinder's height is taken along its axis.
        double height = (geometry.Axis ?? Axis3.Y) switch {
            Axis3.X => geometry.Width,
            Axis3.Y => geometry.Height,
            _ => geometry.Depth,
        };
        // 回写后的半径决定体积；没有回写时退回标称半径。
        // The written-back radius decides the volume; without one it falls back to the nominal radius.
        double radius = radiusOverride ?? geometry.Radius;
        return global::System.Math.PI * radius * radius * height;
    }
}
