#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Service.Pipeline;
/// <summary>
/// 超产上限约束管线 / Over-production upper bound constraint pipeline
///
/// 为每个配置了超产上限的需求添加约束：over_production_i &lt;= upperBound
///
/// Add constraint for each demand with over-production upper bound: over_production_i &lt;= upperBound
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type</typeparam>
public class YieldConstraintPipeline<V> : ICGPipeline<
        AbstractCsp1dShadowPriceArguments,
        LinearMetaModel<Flt64>,
        AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>
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
    public YieldConstraintPipeline(
        YieldAggregation yield,
        YieldModelingConfig<V> config,
        IReadOnlyList<ProductDemand<V>> demands) {
        _yield = yield;
        _config = config;
        _demands = demands;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => "yield_constraint";

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        if (_config.OverProductionUpperBound is null or { Count: 0 }) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        for (int demandIndex = 0; demandIndex < _demands.Count; demandIndex++) {
            ProductDemand<V> demand = _demands[demandIndex];
            ProductDemandShadowPriceKey demandKey = YieldAggregation.DemandShadowPriceKey(demand);
            if (!_config.OverProductionUpperBound.TryGetValue(demandKey, out V upperBound)) {
                continue;
            }

            URealVar? overVar = _yield.OverProduction.ElementAtOrDefault(demandIndex);
            if (overVar == null) {
                continue;
            }

            var priceKey = new YieldOverProductionBoundShadowPriceKey(
                demandKey.ProductId,
                demandKey.UnitSymbol);

            model.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(
                        new List<LinearMonomial<Flt64>> { new(Flt64.One, overVar) },
                        Flt64.Zero),
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), upperBound.ToFlt64()),
                    Comparison.LE),
                this,
                name: $"over_production_bound_{demandIndex}",
                args: priceKey);
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public ShadowPriceExtractor<
            AbstractCsp1dShadowPriceArguments,
            AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>? Extractor()
        => (_, _) => Flt64.Zero;
}
