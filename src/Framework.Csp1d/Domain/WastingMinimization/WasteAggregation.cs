#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization;
/// <summary>
/// 浪费最小化聚合根 / Waste minimization aggregation root.
///
/// 管理浪费最小化的配置信息和目标项计算逻辑。
/// 浪费目标项直接作用于 x 变量（余宽*penalty*x、余料*penalty*x 等），
/// 不需要额外的 slack 变量。
/// Manage waste minimization configuration and objective term computation.
/// Waste objective terms act directly on x variables (restWidth*penalty*x, restMaterial*penalty*x, etc.)
/// without additional slack variables.
/// </summary>
/// <param name="CuttingPlans">切割方案列表 / Cutting plan list.</param>
/// <param name="TrimWidthPenalty">余宽惩罚权重 / Trim width penalty weight.</param>
/// <param name="MaterialCostPenalty">按物料 ID 的单位成本惩罚 / Per-material unit cost penalty.</param>
/// <param name="OverProductionAreaPenalty">超产面积惩罚权重 / Over-production area penalty weight.</param>
/// <param name="RestMaterialPenalty">余料惩罚权重 / Rest material penalty weight.</param>
public sealed record WasteAggregation(
    IReadOnlyList<CuttingPlan<Flt64>> CuttingPlans,
    Flt64? TrimWidthPenalty = null,
    IReadOnlyDictionary<string, Flt64>? MaterialCostPenalty = null,
    Flt64? OverProductionAreaPenalty = null,
    Flt64? RestMaterialPenalty = null
) {
    /// <summary>
    /// 注册到元模型 / Register to meta model.
    ///
    /// 浪费最小化不需要注册额外变量。
    /// Waste minimization does not need to register additional variables.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) => Results.OkInstance;

    /// <summary>
    /// 是否存在任何浪费惩罚配置 / Whether any waste penalty configuration exists.
    /// </summary>
    public bool HasAnyPenalty =>
        TrimWidthPenalty is not null ||
        (MaterialCostPenalty is not null && MaterialCostPenalty.Count > 0) ||
        OverProductionAreaPenalty is not null ||
        RestMaterialPenalty is not null;
}
