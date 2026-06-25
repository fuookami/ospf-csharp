#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Infrastructure.Dto;
/// <summary>
/// 渲染生产类型 / Render production type.
/// </summary>
public enum RenderProductionType {
    /// <summary>产品 / Product.</summary>
    Product,
    /// <summary>联副产品 / Co-product.</summary>
    Costar
}

/// <summary>
/// 渲染切割方案生产项DTO / Render cutting plan production DTO.
/// </summary>
/// <param name="Name">名称 / Name.</param>
/// <param name="X">X坐标 / X coordinate.</param>
/// <param name="Width">宽度 / Width.</param>
/// <param name="UnitLength">单位长度 / Unit length.</param>
/// <param name="ProductionType">生产类型 / Production type.</param>
/// <param name="Info">附加信息 / Additional info.</param>
[Serializable]
public sealed record RenderCuttingPlanProductionDTO(
    string Name,
    FltX X,
    FltX Width,
    FltX? UnitLength,
    RenderProductionType ProductionType,
    IReadOnlyDictionary<string, string> Info
);

/// <summary>
/// 渲染切割方案DTO / Render cutting plan DTO.
/// </summary>
/// <param name="Group">分组标识 / Group identifiers.</param>
/// <param name="Productions">生产项列表 / List of productions.</param>
/// <param name="Width">宽度 / Width.</param>
/// <param name="StandardWidth">标准宽度 / Standard width.</param>
/// <param name="Amount">数量 / Amount.</param>
/// <param name="Info">附加信息 / Additional info.</param>
[Serializable]
public sealed record RenderCuttingPlanDTO(
    IReadOnlyList<string> Group,
    IReadOnlyList<RenderCuttingPlanProductionDTO> Productions,
    FltX Width,
    FltX StandardWidth,
    UInt64 Amount,
    IReadOnlyDictionary<string, string> Info
);

/// <summary>
/// 渲染方案DTO / Render schema DTO.
/// </summary>
/// <param name="Kpi">KPI指标 / KPI metrics.</param>
/// <param name="CuttingPlans">切割方案列表 / List of cutting plans.</param>
[Serializable]
public sealed record RenderSchemaDTO(
    IReadOnlyDictionary<string, string> Kpi,
    IReadOnlyList<RenderCuttingPlanDTO> CuttingPlans
);
