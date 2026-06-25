#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Service.Pipeline;
/// <summary>
/// 批次最小化目标管线 / Batch minimization objective pipeline
///
/// 添加基础目标函数：minimize sum(x_j)
/// 可选地通过 batchCoefficient 施加额外权重。
///
/// Add base objective function: minimize sum(x_j)
/// Optionally apply extra weight via batchCoefficient.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type</typeparam>
public class BatchMinimizationObjective<V> : IPipeline<LinearMetaModel<Flt64>>
    where V : struct {
    private readonly ProduceAggregation<V> _produce;
    private readonly Flt64 _batchCoefficient;

    /// <summary>
    /// 构造 / Constructor
    /// </summary>
    /// <param name="produce">产出聚合 / Produce aggregation</param>
    /// <param name="batchCoefficient">批次目标系数（默认 1.0）/ Batch objective coefficient (default 1.0)</param>
    public BatchMinimizationObjective(
        ProduceAggregation<V> produce,
        Flt64? batchCoefficient = null) {
        _produce = produce;
        _batchCoefficient = batchCoefficient ?? Flt64.One;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => "batch_minimization";

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        var monomials = new List<LinearMonomial<Flt64>>();
        for (int index = 0; index < _produce.PlanCount; index++) {
            monomials.Add(new LinearMonomial<Flt64>(_batchCoefficient, _produce[index]!));
        }

        var objective = new LinearPolynomial<Flt64>(monomials, Flt64.Zero);
        return model.AddObject(ObjectCategory.Minimum, objective, "batch_minimization", null);
    }
}
