#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Yield;
/// <summary>
/// 产出上下文，按产品+单位聚合贡献并与需求对比 / Yield context: aggregates contributions by product+unit and compares with demands.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class YieldContext<V> where V : struct {
    private readonly IQuantityArithmetic<V> _arithmetic;

    /// <summary>
    /// 构造产出上下文 / Construct yield context.
    /// </summary>
    /// <param name="arithmetic">物理量算术策略 / Quantity arithmetic strategy.</param>
    public YieldContext(IQuantityArithmetic<V> arithmetic) {
        _arithmetic = arithmetic;
    }

    /// <summary>
    /// 分析产出偏差：按产品+单位聚合贡献，只在同单位下比较 / Analyze yield deviation: aggregate by product+unit, compare only under same unit.
    /// </summary>
    /// <param name="produce">主问题产出 / Master problem output.</param>
    /// <param name="demands">需求列表 / Demand list.</param>
    /// <returns>产出偏差分析 / Yield deviation analysis.</returns>
    public Result<YieldAnalysis<V>, ErrorCode, Error<ErrorCode>> Analyze(
        Produce<V> produce,
        IReadOnlyList<ProductDemand<V>> demands) {
        Result<Dictionary<DemandAggregationKey<V>, List<CuttingPlanDemandContribution<V>>>, ErrorCode, Error<ErrorCode>> contributionResult = AggregateContributions(produce);
        if (contributionResult is Failed<Dictionary<DemandAggregationKey<V>, List<CuttingPlanDemandContribution<V>>>, ErrorCode, Error<ErrorCode>> contribFailed) {
            return new Failed<YieldAnalysis<V>, ErrorCode, Error<ErrorCode>>(contribFailed.Error);
        }
        if (contributionResult is Fatal<Dictionary<DemandAggregationKey<V>, List<CuttingPlanDemandContribution<V>>>, ErrorCode, Error<ErrorCode>> fat) {
            return new Fatal<YieldAnalysis<V>, ErrorCode, Error<ErrorCode>>(fat.Errors);
        }

        Dictionary<DemandAggregationKey<V>, List<CuttingPlanDemandContribution<V>>> contributionByKey = contributionResult.Value;
        var underProductions = new List<UnderProduction<V>>();
        var overProductions = new List<OverProduction<V>>();
        var outputs = new List<ProductOutput<V>>();

        foreach (ProductDemand<V> demand in demands) {
            var key = new DemandAggregationKey<V>(demand.Product.Id, demand.Quantity.Unit);
            List<CuttingPlanDemandContribution<V>> contributions = contributionByKey.TryGetValue(key, out List<CuttingPlanDemandContribution<V>>? c) ? c : new List<CuttingPlanDemandContribution<V>>();

            Result<Quantity<V>?, ErrorCode, Error<ErrorCode>> totalOutputResult = SumContributions(contributions);
            if (totalOutputResult is Failed<Quantity<V>?, ErrorCode, Error<ErrorCode>> sumFailed) {
                return new Failed<YieldAnalysis<V>, ErrorCode, Error<ErrorCode>>(sumFailed.Error);
            }
            if (totalOutputResult is Fatal<Quantity<V>?, ErrorCode, Error<ErrorCode>> fatSum) {
                return new Fatal<YieldAnalysis<V>, ErrorCode, Error<ErrorCode>>(fatSum.Errors);
            }

            Quantity<V>? totalOutput = totalOutputResult.Value;
            if (totalOutput is null) {
                underProductions.Add(new UnderProduction<V>(
                    demand,
                    demand.Quantity
                ));
                continue;
            }

            outputs.Add(new ProductOutput<V>(
                demand.Product,
                totalOutput,
                demand.Mode
            ));

            dynamic comparison = ((dynamic)totalOutput.Value).PartialOrd((object)demand.Quantity.Value);
            if (comparison is Order.Less) {
                Result<Quantity<V>, ErrorCode, Error<ErrorCode>> subResult = _arithmetic.Subtract(demand.Quantity, totalOutput);
                if (subResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> subFailed) {
                    return new Failed<YieldAnalysis<V>, ErrorCode, Error<ErrorCode>>(subFailed.Error);
                }
                if (subResult is Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fatSub) {
                    return new Fatal<YieldAnalysis<V>, ErrorCode, Error<ErrorCode>>(fatSub.Errors);
                }
                underProductions.Add(new UnderProduction<V>(
                    demand,
                    subResult.Value
                ));
            }
            else if (comparison is Order.Greater) {
                Result<Quantity<V>, ErrorCode, Error<ErrorCode>> subResult = _arithmetic.Subtract(totalOutput, demand.Quantity);
                if (subResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> subFailed2) {
                    return new Failed<YieldAnalysis<V>, ErrorCode, Error<ErrorCode>>(subFailed2.Error);
                }
                if (subResult is Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fatSub) {
                    return new Fatal<YieldAnalysis<V>, ErrorCode, Error<ErrorCode>>(fatSub.Errors);
                }
                overProductions.Add(new OverProduction<V>(
                    demand,
                    subResult.Value
                ));
            }
        }

        return new Ok<YieldAnalysis<V>, ErrorCode, Error<ErrorCode>>(new YieldAnalysis<V>(
            underProductions,
            overProductions,
            outputs
        ));
    }

    private Result<Dictionary<DemandAggregationKey<V>, List<CuttingPlanDemandContribution<V>>>, ErrorCode, Error<ErrorCode>>
        AggregateContributions(Produce<V> produce) {
        var map = new Dictionary<DemandAggregationKey<V>, List<CuttingPlanDemandContribution<V>>>();

        foreach (CuttingPlanUsage<V> usage in produce.CuttingPlans) {
            foreach (CuttingPlanDemandContribution<V> contribution in usage.Plan.DemandContributions) {
                Result<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>> multipliedResult = MultiplyContribution(contribution, usage.Amount);
                if (multipliedResult is Failed<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>> multFailed) {
                    return new Failed<Dictionary<DemandAggregationKey<V>, List<CuttingPlanDemandContribution<V>>>, ErrorCode, Error<ErrorCode>>(multFailed.Error);
                }
                if (multipliedResult is Fatal<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>> fat) {
                    return new Fatal<Dictionary<DemandAggregationKey<V>, List<CuttingPlanDemandContribution<V>>>, ErrorCode, Error<ErrorCode>>(fat.Errors);
                }

                CuttingPlanDemandContribution<V> multiplied = multipliedResult.Value;
                var key = new DemandAggregationKey<V>(contribution.Product.Id, multiplied.Quantity.Unit);
                if (!map.TryGetValue(key, out List<CuttingPlanDemandContribution<V>>? list)) {
                    list = new List<CuttingPlanDemandContribution<V>>();
                    map[key] = list;
                }
                list.Add(multiplied);
            }
        }

        return new Ok<Dictionary<DemandAggregationKey<V>, List<CuttingPlanDemandContribution<V>>>, ErrorCode, Error<ErrorCode>>(map);
    }

    private Result<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>> MultiplyContribution(
        CuttingPlanDemandContribution<V> contribution,
        UInt64 times) {
        Quantity<V> total = _arithmetic.Zero(contribution.Quantity.Unit);
        UInt64 count = UInt64.Zero;
        while (count < times) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> addResult = _arithmetic.Add(total, contribution.Quantity);
            if (addResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> addFailed) {
                return new Failed<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>>(addFailed.Error);
            }
            if (addResult is Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fat) {
                return new Fatal<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>>(fat.Errors);
            }
            total = addResult.Value;
            count += UInt64.One;
        }
        return new Ok<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>>(
            new CuttingPlanDemandContribution<V>(contribution.Product, total)
        );
    }

    private Result<Quantity<V>?, ErrorCode, Error<ErrorCode>> SumContributions(
        IReadOnlyList<CuttingPlanDemandContribution<V>> contributions) {
        if (contributions.Count == 0) {
            return new Ok<Quantity<V>?, ErrorCode, Error<ErrorCode>>(null);
        }

        CuttingPlanDemandContribution<V> first = contributions[0];
        Quantity<V> total = _arithmetic.Zero(first.Quantity.Unit);

        foreach (CuttingPlanDemandContribution<V> contribution in contributions) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> addResult = _arithmetic.Add(total, contribution.Quantity);
            if (addResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> addSumFailed) {
                return new Failed<Quantity<V>?, ErrorCode, Error<ErrorCode>>(addSumFailed.Error);
            }
            if (addResult is Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fat) {
                return new Fatal<Quantity<V>?, ErrorCode, Error<ErrorCode>>(fat.Errors);
            }
            total = addResult.Value;
        }

        return new Ok<Quantity<V>?, ErrorCode, Error<ErrorCode>>(total);
    }
}
