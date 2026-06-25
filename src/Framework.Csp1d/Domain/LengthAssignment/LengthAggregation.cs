#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment;
/// <summary>
/// 长度分配聚合根 / Length assignment aggregation root.
///
/// 管理动态卷长变量（assigned_length_i）和超长松弛变量（over_length_i），
/// 替代 Csp1dMilpSolver 中的 LengthSlackVars 内部类。
/// Manage dynamic length variables (assigned_length_i) and over-length slack variables (over_length_i),
/// replacing the LengthSlackVars inner class in Csp1dMilpSolver.
/// </summary>
/// <param name="Config">长度分配建模配置 / Length assignment modeling configuration.</param>
/// <param name="Demands">需求列表 / Demand list.</param>
public sealed record LengthAggregation(
    LengthAssignmentModelingConfig<Flt64> Config,
    IReadOnlyList<ProductDemand<Flt64>> Demands
) {
    /// <summary>
    /// 已分配卷长变量，按 demand 索引 / Assigned length variables, indexed by demand.
    /// </summary>
    public IReadOnlyList<URealVar?> AssignedLength { get; } = BuildAssignedLength(Config, Demands);

    /// <summary>
    /// 超长松弛变量，按 demand 索引 / Over-length slack variables, indexed by demand.
    /// </summary>
    public IReadOnlyList<URealVar?> OverLength { get; } = BuildOverLength(Config, Demands);

    /// <summary>
    /// 是否存在任何长度变量 / Whether any length variables exist.
    /// </summary>
    public bool HasAny {
        get {
            for (int i = 0; i < AssignedLength.Count; i++) {
                if (AssignedLength[i] is not null) {
                    return true;
                }
            }
            for (int i = 0; i < OverLength.Count; i++) {
                if (OverLength[i] is not null) {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 注册到元模型 / Register to meta model.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        foreach (URealVar? v in AssignedLength) {
            if (v is not null) {
                Try result = model.Add(v);
                if (result.IsFailed) {
                    return result;
                }
            }
        }
        foreach (URealVar? v in OverLength) {
            if (v is not null) {
                Try result = model.Add(v);
                if (result.IsFailed) {
                    return result;
                }
            }
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 提取长度分配建模结果 / Extract length assignment modeling result.
    /// </summary>
    /// <param name="model">抽象线性元模型 / Abstract linear meta model.</param>
    /// <returns>长度分配建模结果 / Length assignment modeling result.</returns>
    public LengthAssignmentModelingResult<Flt64>? ExtractResult(IAbstractLinearMetaModel<Flt64> model) {
        if (!HasAny) {
            return null;
        }

        var assignedLengths = new List<ModeledAssignedLength<Flt64>>();
        var overLengths = new List<ModeledOverLength<Flt64>>();

        for (int demandIndex = 0; demandIndex < Demands.Count; demandIndex++) {
            ProductDemand<Flt64> demand = Demands[demandIndex];

            URealVar? assignedVar = AssignedLength[demandIndex];
            if (assignedVar is not null) {
                Token<Flt64>? token = model.Tokens.Find(assignedVar);
                double? doubleValue = token?.DoubleResult;
                if (doubleValue is not null && doubleValue >= 0.0) {
                    assignedLengths.Add(new ModeledAssignedLength<Flt64>(
                        demand.Product.Id,
                        ConvertSolverValue(demand.Quantity.Value, new Flt64(doubleValue.Value))
                    ));
                }
            }

            URealVar? overVar = OverLength[demandIndex];
            if (overVar is not null) {
                Token<Flt64>? token = model.Tokens.Find(overVar);
                double? doubleValue = token?.DoubleResult;
                if (doubleValue is not null && doubleValue > 0.0) {
                    overLengths.Add(new ModeledOverLength<Flt64>(
                        demand.Product.Id,
                        ConvertSolverValue(demand.Quantity.Value, new Flt64(doubleValue.Value))
                    ));
                }
            }
        }

        return new LengthAssignmentModelingResult<Flt64>(
            assignedLengths.Count > 0 ? assignedLengths : null,
            overLengths.Count > 0 ? overLengths : null
        );
    }

    /// <summary>
    /// 转换 solver 值到领域数值类型 / Convert solver value to domain numeric type.
    /// </summary>
    /// <param name="sample">领域数值样本 / Domain value sample.</param>
    /// <param name="value">solver 边界值 / Solver boundary value.</param>
    /// <returns>与 sample 同类型的领域数值 / Domain value with the same numeric type as sample.</returns>
    internal static Flt64 ConvertSolverValue(Flt64 sample, Flt64 value) => value;

    private static IReadOnlyList<URealVar?> BuildAssignedLength(
        LengthAssignmentModelingConfig<Flt64> config,
        IReadOnlyList<ProductDemand<Flt64>> demands) {
        var result = new URealVar?[demands.Count];
        for (int i = 0; i < demands.Count; i++) {
            string productId = demands[i].Product.Id;
            bool isDynamic = config.DynamicProductIds is not null && config.DynamicProductIds.Contains(productId);
            bool hasBound = (config.AssignedLengthLowerBound is not null && config.AssignedLengthLowerBound.ContainsKey(productId)) ||
                           (config.AssignedLengthUpperBound is not null && config.AssignedLengthUpperBound.ContainsKey(productId));
            bool hasPenalty = config.TotalLengthPenalty is not null ||
                             (config.OverLengthPenalty is not null && config.OverLengthPenalty.ContainsKey(productId));
            if (isDynamic && (hasBound || hasPenalty)) {
                result[i] = new URealVar($"assigned_length_{i}");
            }
        }
        return result;
    }

    private static IReadOnlyList<URealVar?> BuildOverLength(
        LengthAssignmentModelingConfig<Flt64> config,
        IReadOnlyList<ProductDemand<Flt64>> demands) {
        var result = new URealVar?[demands.Count];
        for (int i = 0; i < demands.Count; i++) {
            ProductDemand<Flt64> demand = demands[i];
            string productId = demand.Product.Id;
            bool isDynamic = config.DynamicProductIds is not null && config.DynamicProductIds.Contains(productId);
            bool hasOverBound = config.OverLengthUpperBound is not null && config.OverLengthUpperBound.ContainsKey(productId);
            bool hasOverPenalty = config.OverLengthPenalty is not null && config.OverLengthPenalty.ContainsKey(productId);
            bool hasMaxOverLength = demand.Product.MaxOverProduceLength is not null;
            if (isDynamic && (hasOverBound || hasOverPenalty || hasMaxOverLength)) {
                result[i] = new URealVar($"over_length_{i}");
            }
        }
        return result;
    }
}
