#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;

/// <summary>
/// 标准执行器配置 / The standard-executor configuration.
/// </summary>
/// <remarks>
/// 与基线 `ColumnGenerationStandardExecutorConfig` 对齐：15 个字段，**默认值全部照抄**。三类字段各有用意：
///
/// | 类别 | 字段 | 用意 |
/// | --- | --- | --- |
/// | 求解器口径 | `RmpSolveNamePrefix` / `FinalSolveNamePrefix` / `RmpToLogModel` / `FinalToLogModel` | 求解器日志里的模型名，前缀区分 RMP 与最终 MILP |
/// | 数值口径 | `RmpAreaCoefficient` / `FinalSheetAmountCoefficient` / `IntegralityTolerance` / `ReducedCostTolerance` | 目标函数权重与两个容差 |
/// | 目标权重 | `SheetAreaWeight` / `SheetAmountWeight` / `SheetTypeCostWeight` + 两张系数字典 | 见下面的平局规则 |
///
/// **`SheetAmountWeight` 默认 `1e-6`，不是 1**：基线注释写明"余料为主目标；板数权重保持极小，仅用于在'余料相同'的解之间打破平局，
/// 避免多选零余料的整板"。把它当成普通权重调大，会得到"多选整板"的解——余料一样、板数却更多。
///
/// Aligned with the baseline's `ColumnGenerationStandardExecutorConfig`: 15 fields with **all defaults copied**. **`SheetAmountWeight` defaults to `1e-6` rather than 1**: the baseline comment
/// says waste is the primary objective and the sheet-count weight stays tiny, only breaking ties between equal-waste solutions so the model does not add perfectly filled sheets. Raising it
/// like an ordinary weight yields solutions with the same waste but more sheets.
/// </remarks>
/// <param name="RmpSolveNamePrefix">RMP 模型名前缀 / The RMP model-name prefix.</param>
/// <param name="FinalSolveNamePrefix">最终模型名前缀 / The final model-name prefix.</param>
/// <param name="RmpToLogModel">是否记录 RMP 模型 / Whether to log the RMP model.</param>
/// <param name="FinalToLogModel">是否记录最终模型 / Whether to log the final model.</param>
/// <param name="RmpAreaCoefficient">RMP 面积系数 / The RMP area coefficient.</param>
/// <param name="FinalSheetAmountCoefficient">最终板数系数 / The final sheet-amount coefficient.</param>
/// <param name="EnableFinalCapacityConstraint">最终是否启用容量约束 / Whether the final capacity constraint is enabled.</param>
/// <param name="IntegralityTolerance">整数性容差 / The integrality tolerance.</param>
/// <param name="ReducedCostTolerance">检验数容差 / The reduced-cost tolerance.</param>
/// <param name="SheetAreaWeight">板材面积权重 / The sheet-area weight.</param>
/// <param name="SheetAmountWeight">板数权重，默认极小仅供平局 / The sheet-amount weight, tiny by default and only for ties.</param>
/// <param name="SheetTypeCostWeight">板材类型成本权重 / The sheet-type cost weight.</param>
/// <param name="SheetTypeCosts">各类型成本 / The per-type costs.</param>
/// <param name="SheetTypeAmountCoefficients">各类型数量系数 / The per-type amount coefficients.</param>
public sealed record ColumnGenerationStandardExecutorConfig(
    string RmpSolveNamePrefix = "bpp2d-rmp",
    string FinalSolveNamePrefix = "bpp2d-final",
    bool RmpToLogModel = false,
    bool FinalToLogModel = false,
    FltX? RmpAreaCoefficient = null,
    FltX? FinalSheetAmountCoefficient = null,
    bool EnableFinalCapacityConstraint = true,
    FltX? IntegralityTolerance = null,
    FltX? ReducedCostTolerance = null,
    FltX? SheetAreaWeight = null,
    FltX? SheetAmountWeight = null,
    FltX? SheetTypeCostWeight = null,
    IReadOnlyDictionary<SheetTypeId, FltX>? SheetTypeCosts = null,
    IReadOnlyDictionary<SheetTypeId, FltX>? SheetTypeAmountCoefficients = null)
{
    /// <summary>RMP 面积系数（未给则一）/ The RMP area coefficient, one when unset</summary>
    public FltX RmpAreaCoefficientValue => RmpAreaCoefficient ?? new FltX(1.0);

    /// <summary>最终板数系数（未给则一）/ The final sheet-amount coefficient, one when unset</summary>
    public FltX FinalSheetAmountCoefficientValue => FinalSheetAmountCoefficient ?? new FltX(1.0);

    /// <summary>整数性容差（未给则 <c>1e-6</c>）/ The integrality tolerance, 1e-6 when unset</summary>
    public FltX IntegralityToleranceValue => IntegralityTolerance ?? new FltX(1e-6);

    /// <summary>检验数容差（未给则 <c>1e-6</c>）/ The reduced-cost tolerance, 1e-6 when unset</summary>
    public FltX ReducedCostToleranceValue => ReducedCostTolerance ?? new FltX(1e-6);

    /// <summary>板材面积权重（未给则一）/ The sheet-area weight, one when unset</summary>
    public FltX SheetAreaWeightValue => SheetAreaWeight ?? new FltX(1.0);

    /// <summary>板数权重（未给则 <c>1e-6</c>，仅供平局）/ The sheet-amount weight, 1e-6 when unset and only for ties</summary>
    public FltX SheetAmountWeightValue => SheetAmountWeight ?? new FltX(1e-6);

    /// <summary>板材类型成本权重（未给则零）/ The sheet-type cost weight, zero when unset</summary>
    public FltX SheetTypeCostWeightValue => SheetTypeCostWeight ?? new FltX(0.0);

    /// <summary>各类型成本（未给则空表）/ The per-type costs, empty when unset</summary>
    public IReadOnlyDictionary<SheetTypeId, FltX> CostMap =>
        SheetTypeCosts ?? new Dictionary<SheetTypeId, FltX>();

    /// <summary>各类型数量系数（未给则空表）/ The per-type amount coefficients, empty when unset</summary>
    public IReadOnlyDictionary<SheetTypeId, FltX> AmountCoefficientMap =>
        SheetTypeAmountCoefficients ?? new Dictionary<SheetTypeId, FltX>();
}
