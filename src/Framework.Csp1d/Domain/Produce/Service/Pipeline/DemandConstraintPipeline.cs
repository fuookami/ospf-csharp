#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Service.Pipeline;
/// <summary>
/// 需求平衡约束管线 / Demand balance constraint pipeline
///
/// 为每个产品需求添加平衡约束：
/// - 有 yield slack 时：demandQuantity[i] - over + under = demand
/// - 无 yield slack 时：demandQuantity[i] &gt;= demand
///
/// demandQuantity[i] 是中间符号，由 ProduceAggregation 在 register 和 addColumns 时管理，
/// 约束管线不再直接引用 x 变量，因此 addColumns 时只需 flush 中间符号，无需刷新约束。
///
/// 实现 CGPipeline 接口，通过 constraint.args = Csp1dShadowPriceKey 关联影子价格，
/// 替代原先的约束名映射机制。LP 对偶值提取通过 refresh / extractor
/// 直接从 AbstractCsp1dShadowPriceMap 获取，无需手动遍历 constraint name。
///
/// Add balance constraint for each product demand:
/// - With yield slack: demandQuantity[i] - over + under = demand
/// - Without yield slack: demandQuantity[i] &gt;= demand
///
/// demandQuantity[i] is an intermediate symbol managed by ProduceAggregation during register
/// and addColumns. Constraint pipelines no longer reference x variables directly, so addColumns
/// only needs to flush intermediate symbols without refreshing constraints.
///
/// Implements CGPipeline interface, associating shadow prices via constraint.args = Csp1dShadowPriceKey,
/// replacing the previous constraint-name based mechanism. LP dual value extraction uses
/// refresh / extractor to obtain values directly from AbstractCsp1dShadowPriceMap without
/// manual constraint name traversal.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type</typeparam>
public class DemandConstraintPipeline<V> : ICGPipeline<
        AbstractCsp1dShadowPriceArguments,
        LinearMetaModel<Flt64>,
        AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>
    where V : struct {
    private readonly ProduceAggregation<V> _produce;
    private readonly IReadOnlyList<ProductDemand<V>> _demands;
    private readonly IReadOnlyList<URealVar?> _yieldUnderVars;
    private readonly IReadOnlyList<URealVar?> _yieldOverVars;

    /// <summary>
    /// 构造 / Constructor
    /// </summary>
    /// <param name="produce">产出聚合 / Produce aggregation</param>
    /// <param name="demands">需求列表 / Demand list</param>
    /// <param name="yieldUnderVars">欠产松弛变量 / Under-production slack variables</param>
    /// <param name="yieldOverVars">超产松弛变量 / Over-production slack variables</param>
    public DemandConstraintPipeline(
        ProduceAggregation<V> produce,
        IReadOnlyList<ProductDemand<V>> demands,
        IReadOnlyList<URealVar?>? yieldUnderVars = null,
        IReadOnlyList<URealVar?>? yieldOverVars = null) {
        _produce = produce;
        _demands = demands;
        _yieldUnderVars = yieldUnderVars ?? Array.Empty<URealVar?>();
        _yieldOverVars = yieldOverVars ?? Array.Empty<URealVar?>();
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => "demand_constraint";

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        bool hasYieldSlack = _yieldUnderVars.Any(v => v != null) || _yieldOverVars.Any(v => v != null);

        for (int demandIndex = 0; demandIndex < _demands.Count; demandIndex++) {
            ProductDemand<V> demand = _demands[demandIndex];
            var priceKey = new ProductDemandShadowPriceKey(
                demand.Product.Id,
                ShadowPriceUnitSymbol(demand.Quantity.Unit));

            if (hasYieldSlack) {
                AddEqualityConstraint(model, demandIndex, demand, priceKey);
            }
            else {
                AddGeConstraint(model, demandIndex, demand, priceKey);
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public ShadowPriceExtractor<
            AbstractCsp1dShadowPriceArguments,
            AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>? Extractor() {
        if (_demands.Count == 0) {
            return null;
        }

        return (map, args) => {
            if (args is Csp1dCuttingPlanShadowPriceArguments<V> cuttingPlanArgs) {
                Flt64 price = Flt64.Zero;
                foreach (ProductDemand<V> demand in _demands) {
                    var key = new ProductDemandShadowPriceKey(
                        demand.Product.Id,
                        ShadowPriceUnitSymbol(demand.Quantity.Unit));
                    Flt64? shadowPrice = map[key]?.Price;
                    if (shadowPrice == null) {
                        continue;
                    }

                    CuttingPlanDemandContribution<V>? contribution = cuttingPlanArgs.Plan.DemandContributions
                        .FirstOrDefault(c =>
                            c.Product.Id == demand.Product.Id
                            && c.Quantity.Unit == demand.Quantity.Unit);
                    if (contribution == null) {
                        continue;
                    }

                    price += shadowPrice.Value * contribution.Quantity.Value.ToFlt64();
                }
                return price;
            }
            return Flt64.Zero;
        };
    }

    /// <summary>
    /// 使用中间符号构建需求贡献 LHS / Build demand contribution LHS using intermediate symbol
    ///
    /// 引用 produce.demandQuantity[demandIndex] 而非直接引用 x 变量，
    /// 这样 addColumns 刷新中间符号时约束自动包含新列系数。
    ///
    /// Reference produce.demandQuantity[demandIndex] instead of x variables directly,
    /// so that addColumns flush of intermediate symbols automatically includes new column coefficients.
    /// </summary>
    private LinearPolynomial<Flt64> BuildDemandLhs(int demandIndex) {
        return new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(Flt64.One, _produce.DemandQuantity[demandIndex]) },
            Flt64.Zero);
    }

    private void AddEqualityConstraint(
        LinearMetaModel<Flt64> model,
        int demandIndex,
        ProductDemand<V> demand,
        ProductDemandShadowPriceKey priceKey) {
        URealVar? underVar = _yieldUnderVars.ElementAtOrDefault(demandIndex);
        URealVar? overVar = _yieldOverVars.ElementAtOrDefault(demandIndex);

        if (underVar != null || overVar != null) {
            var monomials = new List<LinearMonomial<Flt64>>
            {
                new(Flt64.One, _produce.DemandQuantity[demandIndex])
            };
            if (underVar != null) {
                monomials.Add(new LinearMonomial<Flt64>(Flt64.One, underVar));
            }
            if (overVar != null) {
                monomials.Add(new LinearMonomial<Flt64>(new Flt64(-1.0), overVar));
            }

            var lhs = new LinearPolynomial<Flt64>(monomials, Flt64.Zero);
            LinearPolynomial<Flt64> rhs = ConstantPolynomial(demand.Quantity.Value.ToFlt64());
            model.AddConstraint(
                new LinearInequality<Flt64>(lhs, rhs, Comparison.EQ),
                this,
                name: $"demand_{demandIndex}",
                args: priceKey);
        }
        else {
            AddGeConstraint(model, demandIndex, demand, priceKey);
        }
    }

    private void AddGeConstraint(
        LinearMetaModel<Flt64> model,
        int demandIndex,
        ProductDemand<V> demand,
        ProductDemandShadowPriceKey priceKey) {
        var lhs = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(Flt64.One, _produce.DemandQuantity[demandIndex]) },
            Flt64.Zero);
        LinearPolynomial<Flt64> rhs = ConstantPolynomial(demand.Quantity.Value.ToFlt64());
        model.AddConstraint(
            new LinearInequality<Flt64>(lhs, rhs, Comparison.GE),
            this,
            name: $"demand_{demandIndex}",
            args: priceKey);
    }

    /// <summary>
    /// 创建常数多项式 / Create constant polynomial
    /// </summary>
    internal static LinearPolynomial<Flt64> ConstantPolynomial(Flt64 value)
        => new(Array.Empty<LinearMonomial<Flt64>>(), value);

    /// <summary>
    /// 获取影子价格单位符号 / Get shadow price unit symbol
    /// </summary>
    internal static string ShadowPriceUnitSymbol(PhysicalUnit unit)
        => unit.Symbol ?? unit.ToString()!;
}
