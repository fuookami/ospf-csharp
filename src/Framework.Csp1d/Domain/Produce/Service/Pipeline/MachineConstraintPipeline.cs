#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Service.Pipeline;
/// <summary>
/// 设备约束管线 / Machine constraint pipeline
///
/// 为每个设备添加两类约束：
/// - 批次数约束：machineBatchQuantity[i] &lt;= maxBatchCount
/// - 产能约束：machineCapacityQuantity[i] &lt;= capacity
///
/// machineBatchQuantity[i] 和 machineCapacityQuantity[i] 是中间符号，
/// 由 ProduceAggregation 管理，约束管线不再直接引用 x 变量。
///
/// 实现 CGPipeline 接口，通过 constraint.args = MachineShadowPriceKey
/// 关联影子价格，替代约束名映射。
///
/// Add two types of constraints for each machine:
/// - Batch count constraint: machineBatchQuantity[i] &lt;= maxBatchCount
/// - Capacity constraint: machineCapacityQuantity[i] &lt;= capacity
///
/// machineBatchQuantity[i] and machineCapacityQuantity[i] are intermediate symbols
/// managed by ProduceAggregation. Constraint pipelines no longer reference x variables directly.
///
/// Implements CGPipeline interface, associating shadow prices via
/// constraint.args = MachineShadowPriceKey, replacing the constraint-name based mechanism.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type</typeparam>
public class MachineConstraintPipeline<V> : ICGPipeline<
        AbstractCsp1dShadowPriceArguments,
        LinearMetaModel<Flt64>,
        AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>
    where V : struct {
    private readonly ProduceAggregation<V> _produce;
    private readonly IReadOnlyList<Machine<V>> _machines;

    /// <summary>
    /// 构造 / Constructor
    /// </summary>
    /// <param name="produce">产出聚合 / Produce aggregation</param>
    /// <param name="machines">设备列表 / Machine list</param>
    public MachineConstraintPipeline(
        ProduceAggregation<V> produce,
        IReadOnlyList<Machine<V>> machines) {
        _produce = produce;
        _machines = machines;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => "machine_constraint";

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        for (int machineIndex = 0; machineIndex < _machines.Count; machineIndex++) {
            Machine<V> machine = _machines[machineIndex];
            AddMachineBatchConstraint(model, machineIndex, machine);
            AddMachineCapacityConstraint(model, machineIndex, machine);
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public ShadowPriceExtractor<
            AbstractCsp1dShadowPriceArguments,
            AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>? Extractor() {
        if (_machines.Count == 0) {
            return null;
        }

        return (map, args) => {
            if (args is Csp1dCuttingPlanShadowPriceArguments<V> cuttingPlanArgs) {
                string? machineId = cuttingPlanArgs.Plan.MachineId;
                if (machineId == null) {
                    return Flt64.Zero;
                }

                Flt64 price = Flt64.Zero;
                var batchKey = new MachineBatchShadowPriceKey(machineId);
                Flt64? batchPrice = map[batchKey]?.Price;
                if (batchPrice != null) {
                    price += batchPrice.Value;
                }

                var capacityKey = new MachineCapacityShadowPriceKey(machineId);
                Flt64? capacityPrice = map[capacityKey]?.Price;
                if (capacityPrice != null) {
                    Flt64? consumption = null;
                    V? capVal = cuttingPlanArgs.Plan.CapacityConsumption?.Value;
                    if (capVal is Flt64 cf) {
                        consumption = cf;
                    }
                    else if (capVal is FltX cx) {
                        consumption = cx.ToFlt64();
                    }

                    if (consumption != null) {
                        price += capacityPrice.Value * consumption.Value;
                    }
                }

                return price;
            }
            return Flt64.Zero;
        };
    }

    private void AddMachineBatchConstraint(
        LinearMetaModel<Flt64> model,
        int machineIndex,
        Machine<V> machine) {
        Math.Algebra.Number.UInt64? maxBatchCount = machine.MaxBatchCount;
        if (maxBatchCount == null) {
            return;
        }

        // 跳过零值中间符号（没有方案分配到该设备）
        // Skip zero-valued intermediate symbols (no plans assigned to this machine)
        ILinearIntermediateSymbol<Flt64> symbol = _produce.MachineBatchQuantity[machineIndex];
        if (symbol.Polynomial.Monomials.Count == 0) {
            return;
        }

        string constraintName = $"machine_batch_{machineIndex}";
        var priceKey = new MachineBatchShadowPriceKey(machine.Id);

        var lhs = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(Flt64.One, symbol) },
            Flt64.Zero);
        model.AddConstraint(
            new LinearInequality<Flt64>(
                lhs,
                DemandConstraintPipeline<V>.ConstantPolynomial(maxBatchCount.Value.ToFlt64()),
                Comparison.LE),
            this,
            name: constraintName,
            args: priceKey);
    }

    private void AddMachineCapacityConstraint(
        LinearMetaModel<Flt64> model,
        int machineIndex,
        Machine<V> machine) {
        Quantity<V>? capacity = machine.Capacity;
        if (capacity == null) {
            return;
        }

        // 跳过零值中间符号（没有方案产能消耗匹配该设备）
        // Skip zero-valued intermediate symbols (no plan capacity consumption matches this machine)
        ILinearIntermediateSymbol<Flt64> symbol = _produce.MachineCapacityQuantity[machineIndex];
        if (symbol.Polynomial.Monomials.Count == 0) {
            return;
        }

        string constraintName = $"machine_capacity_{machineIndex}";
        var priceKey = new MachineCapacityShadowPriceKey(machine.Id);

        var lhs = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(Flt64.One, symbol) },
            Flt64.Zero);
        model.AddConstraint(
            new LinearInequality<Flt64>(
                lhs,
                DemandConstraintPipeline<V>.ConstantPolynomial(capacity.Value.ToFlt64()),
                Comparison.LE),
            this,
            name: constraintName,
            args: priceKey);
    }
}
