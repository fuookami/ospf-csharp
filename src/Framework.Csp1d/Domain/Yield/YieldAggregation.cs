#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Yield;
/// <summary>
/// 产出偏差聚合根 / Yield deviation aggregation root.
///
/// 管理 yield slack 变量（under_production_i, over_production_i），
/// 替代 Csp1dMilpSolver 中的 YieldSlackVars 内部类。
/// Manage yield slack variables (under_production_i, over_production_i),
/// replacing the YieldSlackVars inner class in Csp1dMilpSolver.
/// </summary>
/// <param name="Config">产出建模配置 / Yield modeling configuration.</param>
/// <param name="Demands">需求列表 / Demand list.</param>
/// <param name="NeedsOverSlackForOverArea">是否因超产面积惩罚需要超产 slack / Whether over-production slack is needed for over-production area penalty.</param>
public sealed record YieldAggregation(
    YieldModelingConfig<Flt64> Config,
    IReadOnlyList<ProductDemand<Flt64>> Demands,
    bool NeedsOverSlackForOverArea = false
) {
    /// <summary>
    /// 欠产松弛变量，按 demand 索引；不需要时为 null / Under-production slack variables, indexed by demand; null when not needed.
    /// </summary>
    public IReadOnlyList<URealVar?> UnderProduction { get; } = BuildUnderProduction(Config, Demands);

    /// <summary>
    /// 超产松弛变量，按 demand 索引；不需要时为 null / Over-production slack variables, indexed by demand; null when not needed.
    /// </summary>
    public IReadOnlyList<URealVar?> OverProduction { get; } = BuildOverProduction(Config, Demands, NeedsOverSlackForOverArea);

    /// <summary>
    /// 是否存在任何 slack 变量 / Whether any slack variables exist.
    /// </summary>
    public bool HasAny {
        get {
            for (int i = 0; i < UnderProduction.Count; i++) {
                if (UnderProduction[i] is not null) {
                    return true;
                }
            }
            for (int i = 0; i < OverProduction.Count; i++) {
                if (OverProduction[i] is not null) {
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
        foreach (URealVar? v in UnderProduction) {
            if (v is not null) {
                Try result = model.Add(v);
                if (result.IsFailed) {
                    return result;
                }
            }
        }
        foreach (URealVar? v in OverProduction) {
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
    /// 提取产出建模结果 / Extract yield modeling result.
    /// </summary>
    /// <param name="model">抽象线性元模型 / Abstract linear meta model.</param>
    /// <returns>产出建模结果 / Yield modeling result.</returns>
    public YieldModelingResult<Flt64>? ExtractResult(IAbstractLinearMetaModel<Flt64> model) {
        var underProductions = new List<ModeledUnderProduction<Flt64>>();
        var overProductions = new List<ModeledOverProduction<Flt64>>();

        for (int demandIndex = 0; demandIndex < Demands.Count; demandIndex++) {
            ProductDemand<Flt64> demand = Demands[demandIndex];

            URealVar? underVar = UnderProduction[demandIndex];
            if (underVar is not null) {
                Token<Flt64>? token = model.Tokens.Find(underVar);
                double? doubleValue = token?.DoubleResult;
                if (doubleValue is not null && doubleValue > 0.0) {
                    underProductions.Add(new ModeledUnderProduction<Flt64>(
                        demand.Product.Id,
                        demand.Quantity.Unit.Symbol ?? demand.Quantity.Unit.ToString(),
                        ConvertSolverValue(demand.Quantity.Value, new Flt64(doubleValue.Value))
                    ));
                }
            }

            URealVar? overVar = OverProduction[demandIndex];
            if (overVar is not null) {
                Token<Flt64>? token = model.Tokens.Find(overVar);
                double? doubleValue = token?.DoubleResult;
                if (doubleValue is not null && doubleValue > 0.0) {
                    overProductions.Add(new ModeledOverProduction<Flt64>(
                        demand.Product.Id,
                        demand.Quantity.Unit.Symbol ?? demand.Quantity.Unit.ToString(),
                        ConvertSolverValue(demand.Quantity.Value, new Flt64(doubleValue.Value))
                    ));
                }
            }
        }

        return new YieldModelingResult<Flt64>(
            underProductions.Count > 0 ? underProductions : null,
            overProductions.Count > 0 ? overProductions : null
        );
    }

    /// <summary>
    /// 转换 solver 值到领域数值类型 / Convert solver value to domain numeric type.
    /// </summary>
    /// <param name="sample">领域数值样本 / Domain value sample.</param>
    /// <param name="value">solver 边界值 / Solver boundary value.</param>
    /// <returns>与 sample 同类型的领域数值 / Domain value with the same numeric type as sample.</returns>
    internal static Flt64 ConvertSolverValue(Flt64 sample, Flt64 value) => value;

    /// <summary>
    /// 从 Result 中提取 Ok 值 / Extract Ok value from Result.
    /// </summary>
    /// <typeparam name="T">值类型 / Value type.</typeparam>
    /// <param name="result">Result 实例 / Result instance.</param>
    /// <returns>Ok 值 / Ok value.</returns>
    internal static T UnwrapOk<T>(Result<T, ErrorCode, Error<ErrorCode>> result) {
        if (result is Ok<T, ErrorCode, Error<ErrorCode>> ok) {
            return ok.Value;
        }
        throw new InvalidOperationException("Result is not Ok.");
    }

    internal static ProductDemandShadowPriceKey DemandShadowPriceKey<V>(ProductDemand<V> demand)
        where V : struct {
        return new ProductDemandShadowPriceKey(
            demand.Product.Id,
            demand.Quantity.Unit.Symbol ?? demand.Quantity.Unit.ToString()
        );
    }

    private static IReadOnlyList<URealVar?> BuildUnderProduction(
        YieldModelingConfig<Flt64> config,
        IReadOnlyList<ProductDemand<Flt64>> demands) {
        var result = new URealVar?[demands.Count];
        for (int i = 0; i < demands.Count; i++) {
            ProductDemandShadowPriceKey key = DemandShadowPriceKey(demands[i]);
            if (config.UnderProductionPenalty is not null && config.UnderProductionPenalty.ContainsKey(key)) {
                result[i] = new URealVar($"under_production_{i}");
            }
        }
        return result;
    }

    private static IReadOnlyList<URealVar?> BuildOverProduction(
        YieldModelingConfig<Flt64> config,
        IReadOnlyList<ProductDemand<Flt64>> demands,
        bool needsOverSlackForOverArea) {
        var result = new URealVar?[demands.Count];
        for (int i = 0; i < demands.Count; i++) {
            ProductDemandShadowPriceKey key = DemandShadowPriceKey(demands[i]);
            bool needsOverSlack =
                (config.OverProductionPenalty is not null && config.OverProductionPenalty.ContainsKey(key)) ||
                (config.OverProductionUpperBound is not null && config.OverProductionUpperBound.ContainsKey(key)) ||
                needsOverSlackForOverArea;
            if (needsOverSlack) {
                result[i] = new URealVar($"over_production_{i}");
            }
        }
        return result;
    }
}
