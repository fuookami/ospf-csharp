#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Service.Pipeline;
/// <summary>
/// 产出偏差目标管线 / Yield deviation objective pipeline
///
/// 在目标函数中添加欠产和超产惩罚项：
/// minimize sum(underPenalty * under_production_i + overPenalty * over_production_i)
///
/// Add under-production and over-production penalty terms to the objective function:
/// minimize sum(underPenalty * under_production_i + overPenalty * over_production_i)
///
/// 注意：此管线不直接设置目标函数，而是返回目标项的单项式列表，
/// 由 Csp1dProduceContext 统一组装目标函数。
///
/// Note: This pipeline does not set the objective function directly, but returns
/// the objective monomials for Csp1dProduceContext to assemble the full objective.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type</typeparam>
public class YieldObjectivePipeline<V> : IPipeline<LinearMetaModel<Flt64>>
    where V : struct {
    private readonly YieldAggregation _yield;
    private readonly YieldModelingConfig<V> _config;
    private readonly IReadOnlyList<ProductDemand<V>> _demands;

    /// <summary>
    /// 构造 / Constructor
    /// </summary>
    /// <param name="yield">产出偏差聚合 / Yield deviation aggregation</param>
    /// <param name="config">产出建模配置 / Yield modeling configuration</param>
    /// <param name="demands">需求列表 / Demand list</param>
    public YieldObjectivePipeline(
        YieldAggregation yield,
        YieldModelingConfig<V> config,
        IReadOnlyList<ProductDemand<V>> demands) {
        _yield = yield;
        _config = config;
        _demands = demands;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => "yield_objective";

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) =>
        // yield 目标项由 Csp1dProduceContext 统一组装到目标函数中
        // Yield objective terms are assembled by Csp1dProduceContext into the objective function
        Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 生成目标项单项式 / Generate objective monomials
    /// </summary>
    /// <returns>目标项单项式列表 / Objective monomial list</returns>
    public List<LinearMonomial<Flt64>> ObjectiveMonomials() {
        var monomials = new List<LinearMonomial<Flt64>>();

        for (int demandIndex = 0; demandIndex < _demands.Count; demandIndex++) {
            ProductDemand<V> demand = _demands[demandIndex];
            ProductDemandShadowPriceKey demandKey = YieldAggregation.DemandShadowPriceKey(demand);

            if (_config.UnderProductionPenalty?.TryGetValue(demandKey, out V underPenalty) == true) {
                URealVar? underVar = _yield.UnderProduction.ElementAtOrDefault(demandIndex);
                if (underVar != null) {
                    monomials.Add(new LinearMonomial<Flt64>(underPenalty.ToFlt64(), underVar));
                }
            }

            if (_config.OverProductionPenalty?.TryGetValue(demandKey, out V overPenalty) == true) {
                URealVar? overVar = _yield.OverProduction.ElementAtOrDefault(demandIndex);
                if (overVar != null) {
                    monomials.Add(new LinearMonomial<Flt64>(overPenalty.ToFlt64(), overVar));
                }
            }
        }

        return monomials;
    }
}
