// VRP 策略扩展点：弧可行性与路线成本 / VRP policy extensions: arc feasibility and route cost.
// 与固定 Kotlin 基线 `ArcFeasibilityPolicy.kt`（50 行）、`RouteCostPolicy.kt`（54 行）对应。
// Mirrors the pinned baseline's `ArcFeasibilityPolicy.kt` and `RouteCostPolicy.kt`.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;

/// <summary>
/// 静态弧可行性扩展点 / Static arc-feasibility extension point.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IArcFeasibilityPolicy<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 判断弧是否允许 / Check whether an arc is allowed.
    /// </summary>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="vehicleType">车辆类型 / Vehicle type.</param>
    /// <returns>可行性或判断失败 / Feasibility, or an evaluation failure.</returns>
    Result<bool, ErrorCode, Error<ErrorCode>> IsFeasible(
        NetworkNode<V> from, NetworkNode<V> to, VehicleType<V> vehicleType);

    /// <summary>
    /// 按稳定弧标识判断可行性；默认退回端点策略 / Check feasibility by stable arc identity; defaults to the endpoint policy.
    /// </summary>
    /// <remarks>
    /// **默认实现退回端点策略**，而不是提供某个"更精确"的猜测：绝大多数可行性规则（区域限制、禁行时段）只依赖端点与车型，
    /// 平行弧只在少数场景下才需要区分。默认退回保证既有实现不必改动就继续正确。
    /// **The default delegates to the endpoint policy** rather than guessing something "more precise": most feasibility rules
    /// (zones, curfews) depend only on endpoints and vehicle type, and parallel arcs matter only in a few settings. Delegating
    /// keeps existing implementations correct without changes.
    /// </remarks>
    /// <param name="arc">资源弧 / Resource arc.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="vehicleType">车辆类型 / Vehicle type.</param>
    /// <returns>可行性或判断失败 / Feasibility, or an evaluation failure.</returns>
    Result<bool, ErrorCode, Error<ErrorCode>> IsFeasible(
        ResourceArc<VehicleTypeId> arc,
        NetworkNode<V> from,
        NetworkNode<V> to,
        VehicleType<V> vehicleType) {
        ArgumentNullException.ThrowIfNull(arc);
        return IsFeasible(from, to, vehicleType);
    }
}

/// <summary>
/// 默认仅禁止自环 / Default policy that only rejects self-loops.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class DefaultArcFeasibilityPolicy<V> : IArcFeasibilityPolicy<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 判断弧是否允许 / Check whether an arc is allowed.
    /// </summary>
    /// <remarks>
    /// **只禁自环**：一条从节点出发又回到同一点的弧在 elementary 路线里会让客户重复出现，同时让距离与成本累加出现零增益，
    /// 是最常见的退化输入。其它一切放行，具体业务约束交给上层策略组合。
    /// **Self-loops only**: an arc leaving and returning to the same node repeats a customer in an elementary route and adds a
    /// zero-gain step to distance and cost — the most common degenerate input. Everything else passes; business constraints
    /// belong to composed upper-layer policies.
    /// </remarks>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="vehicleType">车辆类型 / Vehicle type.</param>
    /// <returns>可行性 / Feasibility.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> IsFeasible(
        NetworkNode<V> from,
        NetworkNode<V> to,
        VehicleType<V> vehicleType) {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return Results.Ok(!from.Id.Equals(to.Id));
    }
}

/// <summary>
/// 路线总成本计算扩展点 / Route total-cost calculation extension point.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IRouteCostPolicy<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 是否符合增量定价使用的固定成本加逐弧成本分解 / Whether this policy matches the fixed-plus-each-arc decomposition used
    /// by incremental pricing.
    /// </summary>
    /// <remarks>
    /// **默认为假，且这是一个安全默认**：增量定价会复用"固定成本 + 逐弧成本之和"来避免重复计算整条路线。若某个策略并非
    /// 可加（例如按距离折扣、阶梯计费），却报告为可加，定价就会用一个偏低的 reduced cost 接受本不该接受的列，
    /// 而且不会有任何报错。因此必须由知道自己是可加的策略主动声明。
    /// **False by default, and that is the safe default**: incremental pricing reuses "fixed cost + sum of arc costs" to avoid
    /// recomputing whole routes. A non-additive policy (distance discounts, tiered tariffs) that claims additivity makes
    /// pricing accept columns it should reject, using an understated reduced cost — with no error at all. So a policy must
    /// opt in only when it knows it is additive.
    /// </remarks>
    bool IsAdditive => false;

    /// <summary>
    /// 汇总固定成本与弧成本 / Aggregate fixed and arc costs.
    /// </summary>
    /// <param name="vehicleType">车辆类型 / Vehicle type.</param>
    /// <param name="arcCosts">弧成本 / Arc costs.</param>
    /// <returns>路线成本或计算失败 / The route cost, or a calculation failure.</returns>
    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Cost(
        VehicleType<V> vehicleType, IReadOnlyList<Quantity<V>> arcCosts);
}

/// <summary>
/// 固定成本加弧成本策略 / Fixed-cost-plus-arc-cost policy.
/// </summary>
/// <remarks>
/// **每个量都先换算到目标成本单位再相加**，而不是先按各自单位求和最后换算：混单位的加法在物理上无定义，先换算后求和是唯一
/// 正确的顺序。任一项换算失败即整体失败，不做"跳过该项"的容错——跳过会让成本偏低且无人察觉。
/// **Every quantity is converted to the target cost unit before summing**, rather than summed in mixed units and converted
/// at the end: adding mixed units is physically undefined, so convert-then-sum is the only correct order. Any failed
/// conversion fails the whole computation; silently skipping would understate cost with nobody noticing.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="costUnit">目标成本单位 / Target cost unit.</param>
public class FixedPlusArcCostPolicy<V>(PhysicalUnit costUnit) : IRouteCostPolicy<V>
    where V : struct, IRealNumber<V> {
    /// <summary>目标成本单位 / Target cost unit.</summary>
    protected PhysicalUnit CostUnit { get; } = costUnit;

    /// <inheritdoc/>
    public virtual bool IsAdditive => true;

    /// <inheritdoc/>
    public virtual Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Cost(
        VehicleType<V> vehicleType,
        IReadOnlyList<Quantity<V>> arcCosts) {
        ArgumentNullException.ThrowIfNull(vehicleType);
        ArgumentNullException.ThrowIfNull(arcCosts);

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> fixedCost = vehicleType.FixedCost.ConvertTo(CostUnit);
        if (fixedCost is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return Incompatible("车辆固定成本", "vehicle fixed cost");
        }
        V total = fixedCost.Value.Value;
        foreach (Quantity<V> arcCost in arcCosts) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> normalized = arcCost.ConvertTo(CostUnit);
            if (normalized is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
                return Incompatible("弧成本", "arc cost");
            }
            total = total.Plus(normalized.Value.Value);
        }
        return Results.Ok(new Quantity<V>(total, CostUnit));
    }

    /// <summary>构造单位不兼容失败 / Build an incompatible-unit failure.</summary>
    /// <param name="chinese">中文名 / Chinese name.</param>
    /// <param name="english">英文名 / English name.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Incompatible(
        string chinese, string english) =>
        NetworkSchedulingErrors.Failure<Quantity<V>>(
            $"计算路线成本失败：{chinese} 单位不兼容 / Failed to calculate route cost: incompatible {english} unit");
}

/// <summary>
/// Demo17 固定车辆成本加完整精度距离成本策略 / Demo17 fixed-vehicle plus full-precision distance cost policy.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="costUnit">目标成本单位 / Target cost unit.</param>
public sealed class Demo17CostPolicy<V>(PhysicalUnit costUnit) : FixedPlusArcCostPolicy<V>(costUnit)
    where V : struct, IRealNumber<V>;
