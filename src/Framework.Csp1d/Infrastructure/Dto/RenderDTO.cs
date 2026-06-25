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
[Serializable]
public sealed record RenderCuttingPlanProductionDTO(
    /// <summary>名称 / Name.</summary>
    string Name,
    /// <summary>X坐标 / X coordinate.</summary>
    FltX X,
    /// <summary>宽度 / Width.</summary>
    FltX Width,
    /// <summary>单位长度 / Unit length.</summary>
    FltX? UnitLength,
    /// <summary>生产类型 / Production type.</summary>
    RenderProductionType ProductionType,
    /// <summary>附加信息 / Additional info.</summary>
    IReadOnlyDictionary<string, string> Info
);

/// <summary>
/// 渲染切割方案DTO / Render cutting plan DTO.
/// </summary>
[Serializable]
public sealed record RenderCuttingPlanDTO(
    /// <summary>分组标识 / Group identifiers.</summary>
    IReadOnlyList<string> Group,
    /// <summary>生产项列表 / List of productions.</summary>
    IReadOnlyList<RenderCuttingPlanProductionDTO> Productions,
    /// <summary>宽度 / Width.</summary>
    FltX Width,
    /// <summary>标准宽度 / Standard width.</summary>
    FltX StandardWidth,
    /// <summary>数量 / Amount.</summary>
    UInt64 Amount,
    /// <summary>附加信息 / Additional info.</summary>
    IReadOnlyDictionary<string, string> Info
);

/// <summary>
/// 渲染方案DTO / Render schema DTO.
/// </summary>
[Serializable]
public sealed record RenderSchemaDTO(
    /// <summary>KPI指标 / KPI metrics.</summary>
    IReadOnlyDictionary<string, string> Kpi,
    /// <summary>切割方案列表 / List of cutting plans.</summary>
    IReadOnlyList<RenderCuttingPlanDTO> CuttingPlans
);
