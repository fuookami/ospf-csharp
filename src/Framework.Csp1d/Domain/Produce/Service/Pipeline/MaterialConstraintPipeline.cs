#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using MathUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Service.Pipeline;
/// <summary>
/// 物料可用批次约束管线 / Material available batch constraint pipeline
///
/// 为每个物料添加可用批次上限约束：
/// materialQuantity[i] &lt;= available_batches
///
/// materialQuantity[i] 是中间符号，由 ProduceAggregation 管理，
/// 约束管线不再直接引用 x 变量。
///
/// 实现 CGPipeline 接口，通过 constraint.args = MaterialUsageShadowPriceKey
/// 关联影子价格，替代约束名映射。
///
/// Add available batch upper bound constraint for each material:
/// materialQuantity[i] &lt;= available_batches
///
/// materialQuantity[i] is an intermediate symbol managed by ProduceAggregation.
/// Constraint pipelines no longer reference x variables directly.
///
/// Implements CGPipeline interface, associating shadow prices via
/// constraint.args = MaterialUsageShadowPriceKey, replacing the constraint-name based mechanism.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type</typeparam>
public class MaterialConstraintPipeline<V> : ICGPipeline<
        AbstractCsp1dShadowPriceArguments,
        LinearMetaModel<Flt64>,
        AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>
    where V : struct {
    private readonly ProduceAggregation<V> _produce;
    private readonly IReadOnlyList<Material<V>> _materials;

    /// <summary>
    /// 构造 / Constructor
    /// </summary>
    /// <param name="produce">产出聚合 / Produce aggregation</param>
    /// <param name="materials">物料列表 / Material list</param>
    public MaterialConstraintPipeline(
        ProduceAggregation<V> produce,
        IReadOnlyList<Material<V>> materials) {
        _produce = produce;
        _materials = materials;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => "material_constraint";

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        for (int materialIndex = 0; materialIndex < _materials.Count; materialIndex++) {
            Material<V> material = _materials[materialIndex];
            // 跳过无限制物料 / Skip unlimited materials
            if (material.AvailableBatches == new MathUInt64(ulong.MaxValue)) {
                continue;
            }

            string constraintName = $"material_{materialIndex}";
            var priceKey = new MaterialUsageShadowPriceKey(material.Id);

            var lhs = new LinearPolynomial<Flt64>(
                new List<LinearMonomial<Flt64>> { new(Flt64.One, _produce.MaterialQuantity[materialIndex]) },
                Flt64.Zero);
            model.AddConstraint(
                new LinearInequality<Flt64>(
                    lhs,
                    DemandConstraintPipeline<V>.ConstantPolynomial(material.AvailableBatches.ToFlt64()),
                    Comparison.LE),
                this,
                name: constraintName,
                args: priceKey);
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public ShadowPriceExtractor<
            AbstractCsp1dShadowPriceArguments,
            AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>? Extractor() {
        if (_materials.Count == 0) {
            return null;
        }

        return (map, args) => {
            if (args is Csp1dCuttingPlanShadowPriceArguments<V> cuttingPlanArgs) {
                var key = new MaterialUsageShadowPriceKey(cuttingPlanArgs.Plan.Material.Id);
                return map[key]?.Price ?? Flt64.Zero;
            }
            return Flt64.Zero;
        };
    }
}
