#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Service.Pipeline;
/// <summary>
/// 长度分配约束管线 / Length assignment constraint pipeline
///
/// 为动态长度产品添加约束：
/// - 下界约束: assigned_length_i &gt;= lowerBound
/// - 上界约束: assigned_length_i &lt;= upperBound
/// - 超长上限约束: over_length_i &lt;= overLengthUpperBound
/// - 卷长-超长关联约束: assigned_length_i - over_length_i &lt;= maxOverProduceLength
///
/// Add constraints for dynamic-length products.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type</typeparam>
public class LengthConstraintPipeline<V> : IPipeline<LinearMetaModel<Flt64>>
    where V : struct {
    private readonly LengthAggregation _length;
    private readonly LengthAssignmentModelingConfig<V> _config;
    private readonly IReadOnlyList<ProductDemand<V>> _demands;

    /// <summary>
    /// 构造 / Constructor
    /// </summary>
    /// <param name="length">长度分配聚合 / Length assignment aggregation</param>
    /// <param name="config">长度分配建模配置 / Length assignment modeling configuration</param>
    /// <param name="demands">需求列表 / Demand list</param>
    public LengthConstraintPipeline(
        LengthAggregation length,
        LengthAssignmentModelingConfig<V> config,
        IReadOnlyList<ProductDemand<V>> demands) {
        _length = length;
        _config = config;
        _demands = demands;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => "length_constraint";

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        for (int demandIndex = 0; demandIndex < _demands.Count; demandIndex++) {
            ProductDemand<V> demand = _demands[demandIndex];
            string productId = demand.Product.Id;
            if (_config.DynamicProductIds is null || !_config.DynamicProductIds.Contains(productId)) {
                continue;
            }

            URealVar? assignedVar = demandIndex < _length.AssignedLength.Count ? _length.AssignedLength[demandIndex] : null;
            URealVar? overVar = demandIndex < _length.OverLength.Count ? _length.OverLength[demandIndex] : null;

            // 下界约束: assigned_length_i >= lowerBound
            if (_config.AssignedLengthLowerBound?.TryGetValue(productId, out V lowerBound) == true && assignedVar != null) {
                model.AddConstraint(
                    new LinearInequality<Flt64>(
                        new LinearPolynomial<Flt64>(
                            new List<LinearMonomial<Flt64>> { new(Flt64.One, assignedVar) },
                            Flt64.Zero),
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), lowerBound.ToFlt64()),
                        Comparison.GE),
                    this,
                    name: $"assigned_length_lower_bound_{demandIndex}");
            }

            // 上界约束: assigned_length_i <= upperBound
            if (_config.AssignedLengthUpperBound?.TryGetValue(productId, out V upperBound) == true && assignedVar != null) {
                model.AddConstraint(
                    new LinearInequality<Flt64>(
                        new LinearPolynomial<Flt64>(
                            new List<LinearMonomial<Flt64>> { new(Flt64.One, assignedVar) },
                            Flt64.Zero),
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), upperBound.ToFlt64()),
                        Comparison.LE),
                    this,
                    name: $"assigned_length_upper_bound_{demandIndex}");
            }

            // 超长上限约束: over_length_i <= overLengthUpperBound
            if (_config.OverLengthUpperBound?.TryGetValue(productId, out V overLengthUpperBound) == true && overVar != null) {
                model.AddConstraint(
                    new LinearInequality<Flt64>(
                        new LinearPolynomial<Flt64>(
                            new List<LinearMonomial<Flt64>> { new(Flt64.One, overVar) },
                            Flt64.Zero),
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), overLengthUpperBound.ToFlt64()),
                        Comparison.LE),
                    this,
                    name: $"over_length_bound_{demandIndex}");
            }

            // 卷长-超长关联约束: assigned_length_i - over_length_i <= maxOverProduceLength
            Quantity<V>? maxOverProduceLength = demand.Product.MaxOverProduceLength;
            if (maxOverProduceLength != null && assignedVar != null && overVar != null) {
                model.AddConstraint(
                    new LinearInequality<Flt64>(
                        new LinearPolynomial<Flt64>(
                            new List<LinearMonomial<Flt64>>
                            {
                                new(Flt64.One, assignedVar),
                                new(new Flt64(-1.0), overVar)
                            },
                            Flt64.Zero),
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), maxOverProduceLength.Value.ToFlt64()),
                        Comparison.LE),
                    this,
                    name: $"assigned_over_length_link_{demandIndex}");
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
