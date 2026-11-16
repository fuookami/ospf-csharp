// 二维套裁问题、配置与解 / The 2D cutting stock problem, configuration and solution.
// 对应（`sheetConfig` 见文末说明）。

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.SheetAssignment;
using Fuookami.Ospf.Framework.Csp2d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Application.Model;

/// <summary>主目标口径 / The primary objective measure.</summary>
public enum Csp2dObjectiveKind {
    /// <summary>消耗段高最小（卷材条带排样） / Minimum consumed segment height (coil strip packing).</summary>
    ConsumedLength,

    /// <summary>板材张数最小（定尺） / Minimum sheet count (fixed sheets).</summary>
    SheetCount,

    /// <summary>余料面积最小 / Minimum waste area.</summary>
    Waste,
}

/// <summary>
/// 列生成配置 / Column generation configuration.
/// </summary>
/// <param name="MaxCandidatesPerIteration">每轮每物料候选数上限 / The per-round candidate limit per material.</param>
/// <param name="IterationLimit">迭代上限 / The iteration limit.</param>
/// <param name="Primary">主目标口径，为空时按物料形态自动选择 / The primary objective, auto-selected by material kind when null.</param>
/// <param name="PrimaryWeight">主目标权重 / The primary weight.</param>
/// <param name="SheetCountWeight">张数次目标权重 / The sheet count secondary weight.</param>
/// <param name="WasteWeight">余料次目标权重 / The waste secondary weight.</param>
public sealed record Csp2dConfiguration(
    int MaxCandidatesPerIteration = 16,
    int IterationLimit = 6,
    Csp2dObjectiveKind? Primary = null,
    Flt64? PrimaryWeight = null,
    Flt64? SheetCountWeight = null,
    Flt64? WasteWeight = null) {
    /// <summary>主目标权重，缺省为 1 / The primary weight, defaulting to 1.</summary>
    public Flt64 PrimaryWeightOrDefault => PrimaryWeight ?? Flt64.One;

    /// <summary>张次数目标权重，缺省为 1e-6 / The sheet count secondary weight, defaulting to 1e-6.</summary>
    public Flt64 SheetCountWeightOrDefault => SheetCountWeight ?? new Flt64(1e-6);

    /// <summary>余料次目标权重，缺省为零 / The waste secondary weight, defaulting to zero.</summary>
    public Flt64 WasteWeightOrDefault => WasteWeight ?? Flt64.Zero;
}

/// <summary>
/// 二维套裁问题 / A 2D cutting stock problem.
/// </summary>
/// <param name="Materials">物料列表 / The material list.</param>
/// <param name="Products">产品列表 / The product list.</param>
/// <param name="Demands">产品需求 / The product demands.</param>
/// <param name="Kerf">刀缝宽度 / The kerf width.</param>
/// <param name="Machines">设备列表 / The machine list.</param>
/// <param name="Costars">副产物列表 / The by-product list.</param>
/// <param name="Process">裁切工艺 / The cutting process.</param>
/// <param name="Config">配置 / The configuration.</param>
/// <param name="YieldConfig">良率配置 / The yield configuration.</param>
/// <param name="SheetConfig">板长分配配置 / The sheet assignment configuration.</param>
public sealed record Csp2dProblem(
    IReadOnlyList<Material2<Flt64>> Materials,
    IReadOnlyList<Product2<Flt64>> Products,
    IReadOnlyList<ProductDemand2<Flt64>> Demands,
    Quantity<Flt64> Kerf,
    IReadOnlyList<Machine2<Flt64>>? Machines = null,
    IReadOnlyList<Costar2<Flt64>>? Costars = null,
    CuttingProcess2 Process = CuttingProcess2.Freeform,
    Csp2dConfiguration? Config = null,
    Csp2dYieldConfig? YieldConfig = null,
    Csp2dSheetAssignmentConfig? SheetConfig = null) {
    /// <summary>配置，缺省为空配置 / The configuration, defaulting to the empty one.</summary>
    public Csp2dConfiguration Settings => Config ?? new Csp2dConfiguration();

    /// <summary>良率配置，缺省为空配置 / The yield configuration, defaulting to the empty one.</summary>
    public Csp2dYieldConfig Yield => YieldConfig ?? new Csp2dYieldConfig();

    /// <summary>板长分配配置，缺省为空配置 / The sheet assignment configuration, defaulting to the empty one.</summary>
    public Csp2dSheetAssignmentConfig SheetAssignment => SheetConfig ?? new Csp2dSheetAssignmentConfig();

    /// <summary>
    /// 校验问题定义 / Validate the problem definition.
    /// </summary>
    /// <returns>校验结果 / The validation result.</returns>
    /// <remarks>
    /// **顺序有讲究**：先查"有没有料/有没有需求"，因为那两条失败会让后面的逐项校验**全部无从谈起**；
    /// 再逐条校验物料、需求集合、副产物，**遇到第一处失败立刻返回并带上原因**。
    ///
    /// **The order matters**: "any material / any demand" comes first because those two failures make every later per-item check **meaningless**; then materials,
    /// the demand set and by-products are validated one by one, and **the first failure returns with its reason**.
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (Materials.Count == 0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "问题没有物料 / The problem has no material"));
        }
        if (Demands.Count == 0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "问题没有需求 / The problem has no demand"));
        }
        foreach (Material2<Flt64> material in Materials) {
            Result<Success, ErrorCode, Error<ErrorCode>> result = material.Validate();
            if (result is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return result;
            }
        }
        Result<Success, ErrorCode, Error<ErrorCode>> demands =
            new ProductDemands2<Flt64>(Demands).Validate();
        if (demands is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return demands;
        }
        foreach (Costar2<Flt64> costar in Costars ?? new List<Costar2<Flt64>>()) {
            Result<Success, ErrorCode, Error<ErrorCode>> result = costar.Validate();
            if (result is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return result;
            }
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 产品编号到产品的映射 / The map from product identifier to product.
    /// </summary>
    /// <returns>产品映射 / The product map.</returns>
    public IReadOnlyDictionary<string, Product2<Flt64>> ProductsById() {
        var map = new Dictionary<string, Product2<Flt64>>();
        foreach (Product2<Flt64> product in Products) {
            map[product.Id.Value] = product;
        }
        return map;
    }

    /// <summary>
    /// 选择主目标口径 / Select the primary objective measure.
    /// </summary>
    /// <returns>目标口径 / The objective measure.</returns>
    /// <remarks>
    /// 配置显式给出时**以配置为准**；否则**全是卷材**才用「消耗段高」，只要有一张定尺板就用「板材张数」——
    /// 混料时用段高会把定尺板也算进"长度"，那不是同一个量纲的口径。
    ///
    /// An explicit configuration **wins**; otherwise the consumed segment height applies **only when every material is a coil**, and a single fixed sheet switches to
    /// the sheet count — mixing materials under the segment measure would count sheets as "length", which is not the same measure at all.
    /// </remarks>
    public Csp2dObjectiveKind PrimaryObjective() {
        if (Settings.Primary is { } primary) {
            return primary;
        }
        bool allCoils = true;
        foreach (Material2<Flt64> material in Materials) {
            if (!material.IsCoil) {
                allCoils = false;
                break;
            }
        }
        return allCoils ? Csp2dObjectiveKind.ConsumedLength : Csp2dObjectiveKind.SheetCount;
    }

    /// <summary>
    /// 需求件数总和，作为使用次数变量的上界 / The total demanded piece count, used as the usage variable bound.
    /// </summary>
    /// <returns>件数总和，最低为 1 / The total piece count, floored at 1.</returns>
    /// <remarks>
    /// **只统计件数口径的需求**：面积/重量口径的"件数"要等排样才知道，拿它当上界会把变量上界算小。
    /// **总和为零时返回 1**：零上界会让所有方案不可用，那不是"没有需求"而是模型写错。
    ///
    /// **Only amount-unit demands are counted**: the piece count under an area or weight demand is only known after nesting, and using it as a bound would under-estimate.
    /// **A zero total returns 1**: a zero bound makes every plan unusable, which is not "no demand" but a broken model.
    /// </remarks>
    public ulong TotalPieceDemand() {
        ulong total = 0UL;
        foreach (ProductDemand2<Flt64> demand in Demands) {
            if (demand.PiecesOrNull() is { } pieces) {
                total += pieces;
            }
        }
        return total == 0UL ? 1UL : total;
    }
}

/// <summary>
/// 带使用次数的方案 / A plan together with its usage count.
/// </summary>
/// <param name="Plan">裁切方案 / The cutting plan.</param>
/// <param name="Usage">使用次数 / The usage count.</param>
public sealed record Csp2dPlanUsage(CuttingPlan2<Flt64> Plan, int Usage);

/// <summary>
/// 二维套裁解 / A 2D cutting stock solution.
/// </summary>
/// <param name="Usages">带使用次数的方案，仅保留使用次数为正的 / The plan usages; positive usages only.</param>
/// <param name="ConsumedLength">消耗段高之和 / The total consumed segment height.</param>
/// <param name="SheetCount">板材张数 / The sheet count.</param>
/// <param name="UsedArea">成品占用面积 / The area occupied by finished parts.</param>
/// <param name="WasteArea">余料面积 / The remnant area.</param>
/// <param name="Utilization">利用率 / The utilization.</param>
/// <param name="Coverage">产品编号到产出件数的映射 / Product identifier to produced piece count.</param>
/// <param name="Iterations">列生成迭代次数，MILP 路径为 1 / The column generation iteration count, 1 for the MILP path.</param>
/// <param name="Kpi">关键指标 / The key indicators.</param>
public sealed record Csp2dSolution(
    IReadOnlyList<Csp2dPlanUsage> Usages,
    double ConsumedLength,
    int SheetCount,
    double UsedArea,
    double WasteArea,
    double Utilization,
    IReadOnlyDictionary<string, int> Coverage,
    int Iterations,
    IReadOnlyDictionary<string, string>? Kpi = null) {
    /// <summary>
    /// 是否覆盖全部需求 / Whether the solution covers every demand.
    /// </summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <returns>是否覆盖 / Whether it is covered.</returns>
    /// <remarks>
    /// **只看件数口径的需求**：面积/重量口径没有"件数"可比，硬拿 0 去比会让**所有解都被判成未覆盖**。
    ///
    /// **Only amount-unit demands are checked**: the area and weight units have no piece count to compare, and comparing against 0 would mark **every solution uncovered**.
    /// </remarks>
    public bool Covers(Csp2dProblem problem) {
        foreach (ProductDemand2<Flt64> demand in problem.Demands) {
            if (demand.PiecesOrNull() is not { } required) {
                continue;
            }
            int produced = Coverage.TryGetValue(demand.Product.Id.Value, out int count) ? count : 0;
            if (produced < (int)required) {
                return false;
            }
        }
        return true;
    }
}
