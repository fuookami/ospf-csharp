#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization.Service.Pipeline;
/// <summary>
/// 浪费最小化目标管线 / Waste minimization objective pipeline
///
/// 在目标函数中添加浪费相关惩罚项：
/// - 余宽惩罚: sum(restWidth * trimPenalty * x_j)
/// - 余料面积代理惩罚: sum(restMaterialValue * restPenalty * x_j)
/// - 物料成本惩罚: sum(materialCostPenalty[materialId] * x_j)
/// - 超产面积代理惩罚: sum(overArea * overAreaPenalty)
///
/// Add waste-related penalty terms to the objective function.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type</typeparam>
public class WasteObjectivePipeline<V> : IPipeline<LinearMetaModel<Flt64>>
    where V : struct {
    private readonly ProduceAggregation<V> _produce;
    private readonly WasteAggregation _waste;
    private readonly IReadOnlyList<ProductDemand<V>> _demands;
    private readonly IReadOnlyList<URealVar?> _overProductionVars;
    private readonly OverProductionAreaMeasure _overProductionAreaMeasure;
    private readonly RestMaterialMeasure _restMaterialMeasure;

    /// <summary>
    /// 构造 / Constructor
    /// </summary>
    /// <param name="produce">产出聚合 / Produce aggregation</param>
    /// <param name="waste">浪费聚合 / Waste aggregation</param>
    /// <param name="demands">需求列表 / Demand list</param>
    /// <param name="overProductionVars">超产松弛变量列表（超产面积惩罚需要）/ Over-production slack variables (needed for over-production area penalty)</param>
    /// <param name="overProductionAreaMeasure">超产面积度量口径 / Over-production area measure policy</param>
    /// <param name="restMaterialMeasure">余料度量口径 / Rest material measure policy</param>
    public WasteObjectivePipeline(
        ProduceAggregation<V> produce,
        WasteAggregation waste,
        IReadOnlyList<ProductDemand<V>> demands,
        IReadOnlyList<URealVar?>? overProductionVars = null,
        OverProductionAreaMeasure overProductionAreaMeasure = OverProductionAreaMeasure.ProductMaxWidthProxy,
        RestMaterialMeasure restMaterialMeasure = RestMaterialMeasure.RestWidthByMaterialLengthProxy) {
        _produce = produce;
        _waste = waste;
        _demands = demands;
        _overProductionVars = overProductionVars ?? System.Array.Empty<URealVar?>();
        _overProductionAreaMeasure = overProductionAreaMeasure;
        _restMaterialMeasure = restMaterialMeasure;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => "waste_objective";

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) =>
        // waste 目标项由 Csp1dProduceContext 统一组装到目标函数中
        // Waste objective terms are assembled by Csp1dProduceContext into the objective function
        Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 生成目标项单项式 / Generate objective monomials
    /// </summary>
    /// <returns>目标项单项式列表 / Objective monomial list</returns>
    public List<LinearMonomial<Flt64>> ObjectiveMonomials() {
        var monomials = new List<LinearMonomial<Flt64>>();

        // 余宽惩罚: sum(restWidth * trimPenalty * x_j)
        Flt64? trimPenalty = _waste.TrimWidthPenalty;
        if (trimPenalty != null) {
            for (int index = 0; index < _produce.PlanCount; index++) {
                CuttingPlan<V> plan = _produce.CuttingPlans[index];
                V? restWidthValue = plan.RestWidth?.Value;
                if (restWidthValue == null) {
                    continue;
                }

                if (restWidthValue.Value.ToFlt64() > Flt64.Zero) {
                    Flt64 coeff = restWidthValue.Value.ToFlt64() * trimPenalty.Value;
                    monomials.Add(new LinearMonomial<Flt64>(coeff, _produce[index]!));
                }
            }
        }

        // 余料面积代理惩罚: sum(restMaterialValue * restPenalty * x_j)
        Flt64? restMaterialPenalty = _waste.RestMaterialPenalty;
        if (restMaterialPenalty != null) {
            for (int index = 0; index < _produce.PlanCount; index++) {
                CuttingPlan<V> plan = _produce.CuttingPlans[index];
                V? restMaterialValue = RestMaterialValue(plan, _restMaterialMeasure);
                if (restMaterialValue == null) {
                    continue;
                }

                Flt64 coeff = restMaterialValue.Value.ToFlt64() * restMaterialPenalty.Value;
                monomials.Add(new LinearMonomial<Flt64>(coeff, _produce[index]!));
            }
        }

        // 物料成本惩罚: sum(materialCostPenalty[materialId] * x_j)
        if (_waste.MaterialCostPenalty is { Count: > 0 }) {
            for (int index = 0; index < _produce.PlanCount; index++) {
                CuttingPlan<V> plan = _produce.CuttingPlans[index];
                if (_waste.MaterialCostPenalty.TryGetValue(plan.Material.Id, out Flt64 costPenalty)) {
                    monomials.Add(new LinearMonomial<Flt64>(costPenalty.ToFlt64(), _produce[index]!));
                }
            }
        }

        // 超产面积代理惩罚: sum(overArea * overAreaPenalty)
        Flt64? overAreaPenalty = _waste.OverProductionAreaPenalty;
        if (overAreaPenalty != null) {
            for (int demandIndex = 0; demandIndex < _demands.Count; demandIndex++) {
                ProductDemand<V> demand = _demands[demandIndex];
                URealVar? overVar = _overProductionVars.ElementAtOrDefault(demandIndex);
                if (overVar == null) {
                    continue;
                }

                V? productWidthValue = OverProductionAreaWidthValue(demand, _overProductionAreaMeasure);
                if (productWidthValue == null) {
                    continue;
                }

                Flt64 coeff = productWidthValue.Value.ToFlt64() * overAreaPenalty.Value;
                monomials.Add(new LinearMonomial<Flt64>(coeff, overVar));
            }
        }

        return monomials;
    }

    private V? RestMaterialValue(CuttingPlan<V> plan, RestMaterialMeasure measure) {
        Quantity<V>? restWidth = plan.RestWidth;
        if (restWidth == null) {
            return null;
        }

        return measure switch {
            RestMaterialMeasure.RestWidthByMaterialLengthProxy =>
                plan.Material.Length != null
                    ? (V?)(dynamic)restWidth.Value * (dynamic)plan.Material.Length.Value
                    : null,
            _ => null
        };
    }

    private V? OverProductionAreaWidthValue(ProductDemand<V> demand, OverProductionAreaMeasure measure) {
        return measure switch {
            OverProductionAreaMeasure.ProductMaxWidthProxy => demand.Product.MaxWidth()?.Value,
            _ => null
        };
    }
}

/// <summary>
/// 超产面积度量口径 / Over-production area measure policy
/// </summary>
public enum OverProductionAreaMeasure {
    /// <summary>使用产品最大宽度代理 / Use product max width proxy</summary>
    ProductMaxWidthProxy
}

/// <summary>
/// 余料度量口径 / Rest material measure policy
/// </summary>
public enum RestMaterialMeasure {
    /// <summary>使用余宽乘物料长度代理 / Use rest width by material length proxy</summary>
    RestWidthByMaterialLengthProxy
}
