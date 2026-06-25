#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Service.Pipeline;
/// <summary>
/// 长度分配目标管线 / Length assignment objective pipeline
///
/// 在目标函数中添加长度相关惩罚项：
/// - 总卷长惩罚: sum(totalLengthPenalty * assigned_length_i)
/// - 超长惩罚: sum(overLengthPenalty * over_length_i)
/// - 批次最小惩罚: 对 Σx_j 施加额外加权
///
/// Add length-related penalty terms to the objective function.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type</typeparam>
public class LengthObjectivePipeline<V> : IPipeline<LinearMetaModel<Flt64>>
    where V : struct {
    private readonly ProduceAggregation<V> _produce;
    private readonly LengthAggregation _length;
    private readonly LengthAssignmentModelingConfig<V> _config;

    /// <summary>
    /// 构造 / Constructor
    /// </summary>
    /// <param name="produce">产出聚合 / Produce aggregation</param>
    /// <param name="length">长度分配聚合 / Length assignment aggregation</param>
    /// <param name="config">长度分配建模配置 / Length assignment modeling configuration</param>
    public LengthObjectivePipeline(
        ProduceAggregation<V> produce,
        LengthAggregation length,
        LengthAssignmentModelingConfig<V> config) {
        _produce = produce;
        _length = length;
        _config = config;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => "length_objective";

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) =>
        // length 目标项由 Csp1dProduceContext 统一组装到目标函数中
        // Length objective terms are assembled by Csp1dProduceContext into the objective function
        Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 生成目标项单项式 / Generate objective monomials
    /// </summary>
    /// <returns>目标项单项式列表 / Objective monomial list</returns>
    public List<LinearMonomial<Flt64>> ObjectiveMonomials() {
        var monomials = new List<LinearMonomial<Flt64>>();

        // 总卷长惩罚: sum(totalLengthPenalty * assigned_length_i)
        V? totalLengthPenalty = _config.TotalLengthPenalty;
        if (totalLengthPenalty.HasValue) {
            V penaltyValue = totalLengthPenalty.Value;
            for (int demandIndex = 0; demandIndex < _length.Demands.Count; demandIndex++) {
                URealVar? assignedVar = demandIndex < _length.AssignedLength.Count ? _length.AssignedLength[demandIndex] : null;
                if (assignedVar == null) {
                    continue;
                }

                monomials.Add(new LinearMonomial<Flt64>(ToFlt64(penaltyValue), assignedVar));
            }
        }

        // 超长惩罚: sum(overLengthPenalty[productId] * over_length_i)
        if (_config.OverLengthPenalty is { Count: > 0 }) {
            for (int demandIndex = 0; demandIndex < _length.Demands.Count; demandIndex++) {
                ProductDemand<Flt64> demand = _length.Demands[demandIndex];
                URealVar? overVar = demandIndex < _length.OverLength.Count ? _length.OverLength[demandIndex] : null;
                if (overVar == null) {
                    continue;
                }

                if (!_config.OverLengthPenalty.TryGetValue(demand.Product.Id, out V penalty)) {
                    continue;
                }

                monomials.Add(new LinearMonomial<Flt64>(ToFlt64(penalty), overVar));
            }
        }

        return monomials;
    }

    /// <summary>
    /// 获取批次系数 / Get batch coefficient
    ///
    /// 当 batchMinPenalty 非空时，对 Σx_j 施加额外加权。
    /// Apply extra weight to Σx_j when batchMinPenalty is present.
    /// </summary>
    /// <returns>批次系数 / Batch coefficient</returns>
    public Flt64 BatchCoefficient() {
        V? batchMinPenalty = _config.BatchMinPenalty;
        if (!batchMinPenalty.HasValue) {
            return Flt64.One;
        }

        return Flt64.One + ToFlt64(batchMinPenalty.Value);
    }

    private static Flt64 ToFlt64(V value) {
        if (value is Flt64 f) {
            return f;
        }

        if (value is FltX x) {
            return x.ToFlt64();
        }

        if (value is Fuookami.Ospf.Math.Algebra.Number.Int64 i) {
            return i.ToFlt64();
        }

        return Flt64.Zero;
    }
}
