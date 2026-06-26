#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>渲染形状类型 / Render shape type.</summary>
public enum RenderShapeType { Cuboid, Cylinder }

/// <summary>渲染轴类型 / Render axis type.</summary>
public enum RenderAxis3 { X, Y, Z }

/// <summary>渲染算法形状类型 / Render algorithm shape type.</summary>
public enum RenderAlgorithmShapeType { Cuboid, VerticalCylinder, HorizontalCylinderX, HorizontalCylinderZ }

/// <summary>
/// 渲染装载计划货物 DTO / Render loading plan item DTO.
/// 用于将装箱结果序列化为 JSON/XML 以供渲染。
/// </summary>
public sealed record RenderLoadingPlanItemDto(
    string Name,
    string? PackageType,
    double Width,
    double Height,
    double Depth,
    double X,
    double Y,
    double Z,
    double Weight,
    int LoadingOrder,
    string ShapeType,
    RenderShapeType RenderShapeType,
    RenderAlgorithmShapeType AlgorithmShapeType,
    double? Radius = null,
    double? Diameter = null,
    Axis3? Axis = null,
    double? BoundingWidth = null,
    double? BoundingHeight = null,
    double? BoundingDepth = null,
    double? ActualVolume = null,
    IReadOnlyDictionary<string, string>? Info = null);

/// <summary>
/// 渲染装载计划 DTO / Render loading plan DTO.
/// </summary>
public sealed record RenderLoadingPlanDto(
    string? Group,
    string Name,
    string TypeCode,
    double Width,
    double Height,
    double Depth,
    double LoadingRate,
    double Weight,
    double Volume,
    IReadOnlyList<RenderLoadingPlanItemDto> Items,
    IReadOnlyDictionary<string, string>? Info = null);

/// <summary>
/// 渲染 Schema DTO / Rendering schema DTO.
/// 顶层 DTO，包含 KPI 指标和装载计划。
/// </summary>
public sealed record SchemaDto(
    IReadOnlyDictionary<string, string> Kpi,
    IReadOnlyList<RenderLoadingPlanDto>? LoadingPlans = null);
