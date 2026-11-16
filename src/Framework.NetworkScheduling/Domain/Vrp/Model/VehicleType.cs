// VRPTW 模型：单位口径、容差与车辆类型 / VRPTW model: unit contract, tolerances and vehicle type.
// 与固定 Kotlin 、（68 行）、（72 行）对应。
// , and.

using System;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;

/// <summary>
/// 车辆类型稳定标识 / Stable vehicle-type identifier.
/// </summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public sealed record VehicleTypeId(string Value) : IComparable<VehicleTypeId> {
    /// <inheritdoc/>
    public int CompareTo(VehicleTypeId? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// 单个 VRPTW 实例的归一化单位口径 / Normalized unit contract for one VRPTW instance.
/// </summary>
/// <remarks>
/// **单位口径是实例级的，不是每个量各自决定**：距离、负载、成本三种单位在这里一次钉死，后续所有量的换算都朝向它们。
/// 若允许逐量自选单位，同一实例里就会出现两套口径，汇总指标（总距离、总成本）相加时无从对齐。
/// **The unit contract is per instance, not per quantity**: distance, load and cost units are fixed here once and every
/// later conversion targets them. Allowing per-quantity units would put two contracts inside one instance and make the
/// aggregates (total distance, total cost) impossible to add up consistently.
/// </remarks>
/// <param name="DistanceUnit">距离单位 / Distance unit.</param>
/// <param name="LoadUnit">负载单位 / Load unit.</param>
/// <param name="CostUnit">成本单位 / Cost unit.</param>
public sealed class VrptwUnits(
    PhysicalUnit DistanceUnit,
    PhysicalUnit LoadUnit,
    PhysicalUnit CostUnit) {
    /// <summary>距离单位 / Distance unit.</summary>
    public PhysicalUnit DistanceUnit { get; } = DistanceUnit;

    /// <summary>负载单位 / Load unit.</summary>
    public PhysicalUnit LoadUnit { get; } = LoadUnit;

    /// <summary>成本单位 / Cost unit.</summary>
    public PhysicalUnit CostUnit { get; } = CostUnit;

    /// <summary>
    /// 创建实例单位口径 / Create an instance unit contract.
    /// </summary>
    /// <remarks>
    /// **距离必须是长度量纲的线性单位**：距离要参与三角不等式与路径累加，仿射单位（如摄氏温度）带偏移，累加没有意义。
    /// 负载与成本同样不能是仿射单位。校验顺序是"先距离后其余"，因为距离的量纲错误最容易被后续求和放大成荒谬结果。
    /// **The distance unit must be linear and of length dimension**: distance participates in the triangle inequality and path
    /// accumulation, and an affine unit (like Celsius) carries an offset that makes accumulation meaningless. Load and cost
    /// likewise cannot be affine. Distance is checked first because its dimension error is the one most easily amplified into
    /// nonsense by later summation.
    /// </remarks>
    /// <param name="distanceUnit">距离单位 / Distance unit.</param>
    /// <param name="loadUnit">负载单位 / Load unit.</param>
    /// <param name="costUnit">成本单位 / Cost unit.</param>
    /// <returns>单位口径或校验失败 / The contract, or a validation failure.</returns>
    public static Result<VrptwUnits, ErrorCode, Error<ErrorCode>> Invoke(
        PhysicalUnit distanceUnit,
        PhysicalUnit loadUnit,
        PhysicalUnit costUnit) {
        ArgumentNullException.ThrowIfNull(distanceUnit);
        ArgumentNullException.ThrowIfNull(loadUnit);
        ArgumentNullException.ThrowIfNull(costUnit);

        if (distanceUnit.Quantity != Dimensions.Length || distanceUnit.IsAffine) {
            return NetworkSchedulingErrors.Failure<VrptwUnits>(
                "创建 VRPTW 单位口径失败：距离必须使用线性长度单位 / Failed to create VRPTW unit contract: distance must "
                + "use a linear length unit");
        }
        if (loadUnit.IsAffine || costUnit.IsAffine) {
            return NetworkSchedulingErrors.Failure<VrptwUnits>(
                "创建 VRPTW 单位口径失败：负载和成本单位不能是仿射单位 / Failed to create VRPTW unit contract: load and "
                + "cost units cannot be affine");
        }
        return Results.Ok(new VrptwUnits(distanceUnit, loadUnit, costUnit));
    }
}

/// <summary>
/// VRPTW 算法统一容差合同 / Unified tolerance contract for VRPTW algorithms.
/// </summary>
/// <remarks>
/// **容差集中在一处，而不是各算法各自定义**：定价、可行性、整数性、成本复核、相对 gap 五个阈值必须彼此协调（例如整数性
/// 容差放宽后，成本复核容差也要跟着放宽，否则解会被判成"成本对不上"）。分散定义时这种联动无人负责。
/// **Tolerances live in one place rather than per algorithm**: pricing, feasibility, integrality, cost validation and relative
/// gap must stay coherent with each other (widening the integrality tolerance without widening cost validation makes valid
/// solutions report "cost mismatch"). Scattered definitions leave that coupling unowned.
/// </remarks>
/// <param name="Feasibility">可行性容差 / Feasibility tolerance.</param>
/// <param name="Pricing">定价容差 / Pricing tolerance.</param>
/// <param name="Integrality">整数性容差 / Integrality tolerance.</param>
/// <param name="CostValidation">成本复核容差 / Cost-validation tolerance.</param>
/// <param name="RelativeGap">相对 gap 容差 / Relative-gap tolerance.</param>
public sealed class VrptwTolerances(
    Flt64 Feasibility,
    Flt64 Pricing,
    Flt64 Integrality,
    Flt64 CostValidation,
    Flt64 RelativeGap) {
    /// <summary>默认容差合同 / Default tolerance contract.</summary>
    public static VrptwTolerances Default { get; } = new(
        new Flt64(1e-7), new Flt64(1e-8), new Flt64(1e-7), new Flt64(1e-7), new Flt64(1e-4));

    /// <summary>可行性容差 / Feasibility tolerance.</summary>
    public Flt64 Feasibility { get; } = Feasibility;

    /// <summary>定价容差 / Pricing tolerance.</summary>
    public Flt64 Pricing { get; } = Pricing;

    /// <summary>整数性容差 / Integrality tolerance.</summary>
    public Flt64 Integrality { get; } = Integrality;

    /// <summary>成本复核容差 / Cost-validation tolerance.</summary>
    public Flt64 CostValidation { get; } = CostValidation;

    /// <summary>相对 gap 容差 / Relative-gap tolerance.</summary>
    public Flt64 RelativeGap { get; } = RelativeGap;

    /// <summary>
    /// 创建容差合同 / Create a tolerance contract.
    /// </summary>
    /// <remarks>
    /// **零容差合法，负容差与非有限值非法**：零容差表示"要求精确相等"，是一种有意的严格设定；负容差会让所有比较反向，
    /// 而 `NaN` 会让每次比较都返回假，两者都会静默地把算法变成不做任何判定。
    /// **A zero tolerance is legal; negatives and non-finite values are not**: zero means "require exact equality", a
    /// deliberate strict setting. A negative tolerance inverts every comparison and `NaN` makes each one return false; both
    /// silently turn the algorithm into one that decides nothing.
    /// </remarks>
    /// <param name="feasibility">可行性容差 / Feasibility tolerance.</param>
    /// <param name="pricing">定价容差 / Pricing tolerance.</param>
    /// <param name="integrality">整数性容差 / Integrality tolerance.</param>
    /// <param name="costValidation">成本复核容差 / Cost-validation tolerance.</param>
    /// <param name="relativeGap">相对 gap 容差 / Relative-gap tolerance.</param>
    /// <returns>容差合同或校验失败 / The contract, or a validation failure.</returns>
    public static Result<VrptwTolerances, ErrorCode, Error<ErrorCode>> Invoke(
        Flt64? feasibility = null,
        Flt64? pricing = null,
        Flt64? integrality = null,
        Flt64? costValidation = null,
        Flt64? relativeGap = null) {
        Flt64 feasibilityValue = feasibility ?? Default.Feasibility;
        Flt64 pricingValue = pricing ?? Default.Pricing;
        Flt64 integralityValue = integrality ?? Default.Integrality;
        Flt64 costValidationValue = costValidation ?? Default.CostValidation;
        Flt64 relativeGapValue = relativeGap ?? Default.RelativeGap;

        Flt64[] values = new[] { feasibilityValue, pricingValue, integralityValue, costValidationValue, relativeGapValue };
        foreach (Flt64 value in values) {
            if (!double.IsFinite(value.ToDouble()) || value.CompareTo(Flt64.Zero) < 0) {
                return NetworkSchedulingErrors.Failure<VrptwTolerances>(
                    "创建 VRPTW 容差失败：所有容差必须是有限非负数 / Failed to create VRPTW tolerances: all tolerances "
                    + "must be finite and non-negative");
            }
        }
        return Results.Ok(new VrptwTolerances(
            feasibilityValue, pricingValue, integralityValue, costValidationValue, relativeGapValue));
    }
}

/// <summary>
/// 有限车队中的车辆类型 / Vehicle type in a finite fleet.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">车辆类型稳定标识 / Stable vehicle-type identifier.</param>
/// <param name="Capacity">单车容量 / Per-vehicle capacity.</param>
/// <param name="FixedCost">固定使用成本 / Fixed usage cost.</param>
/// <param name="Amount">可用数量 / Available amount.</param>
public sealed class VehicleType<V>(
    VehicleTypeId Id,
    Quantity<V> Capacity,
    Quantity<V> FixedCost,
    int Amount) where V : struct, IRealNumber<V> {
    /// <summary>车辆类型稳定标识 / Stable vehicle-type identifier.</summary>
    public VehicleTypeId Id { get; } = Id;

    /// <summary>单车容量 / Per-vehicle capacity.</summary>
    public Quantity<V> Capacity { get; } = Capacity;

    /// <summary>固定使用成本 / Fixed usage cost.</summary>
    public Quantity<V> FixedCost { get; } = FixedCost;

    /// <summary>可用数量 / Available amount.</summary>
    public int Amount { get; } = Amount;

    /// <summary>
    /// 创建车辆类型 / Create a vehicle type.
    /// </summary>
    /// <remarks>
    /// **容量必须严格为正，不是"非负"**：零容量车辆装不下任何有需求的客户，却会占用一份车队名额并支付固定成本，
    /// 结果是最优解里出现"派了一辆空车"——这是建模笔误的典型症状，应在构造期挡住而不是让它污染求解结果。
    /// 相比之下**零固定成本合法**（自有车辆没有启用费用），**零可用数量非法**（等于把该车型从车队里删掉，应直接不写它）。
    /// **Capacity must be strictly positive, not merely non-negative**: a zero-capacity vehicle carries no demand yet consumes a
    /// fleet slot and its fixed cost, so the optimum contains "a dispatched empty vehicle" — the classic symptom of a modelling
    /// typo, best blocked at construction rather than left to poison the solve. By contrast **a zero fixed cost is legal**
    /// (owned vehicles have no activation fee) and **a zero amount is not** (equal to removing the type, which should simply be
    /// omitted).
    /// </remarks>
    /// <param name="id">车辆类型稳定标识 / Stable vehicle-type identifier.</param>
    /// <param name="capacity">正容量 / Positive capacity.</param>
    /// <param name="fixedCost">非负固定成本 / Non-negative fixed cost.</param>
    /// <param name="amount">正可用数量 / Positive available amount.</param>
    /// <returns>车辆类型或校验失败 / The vehicle type, or a validation failure.</returns>
    public static Result<VehicleType<V>, ErrorCode, Error<ErrorCode>> Invoke(
        VehicleTypeId id,
        Quantity<V> capacity,
        Quantity<V> fixedCost,
        int amount) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(fixedCost);

        if (string.IsNullOrWhiteSpace(id.Value)) {
            return NetworkSchedulingErrors.Failure<VehicleType<V>>(
                "创建车辆类型失败：车辆类型 ID 不能为空 / Failed to create vehicle type: vehicle type ID cannot be blank");
        }
        if (capacity.Value.CompareTo(capacity.Value.Constants.Zero) <= 0) {
            return NetworkSchedulingErrors.Failure<VehicleType<V>>(
                "创建车辆类型失败：车辆容量必须为正 / Failed to create vehicle type: vehicle capacity must be positive");
        }
        if (fixedCost.Value.CompareTo(fixedCost.Value.Constants.Zero) < 0) {
            return NetworkSchedulingErrors.Failure<VehicleType<V>>(
                "创建车辆类型失败：固定成本不能为负 / Failed to create vehicle type: fixed cost cannot be negative");
        }
        if (amount <= 0) {
            return NetworkSchedulingErrors.Failure<VehicleType<V>>(
                "创建车辆类型失败：可用数量必须为正 / Failed to create vehicle type: available amount must be positive");
        }
        return Results.Ok(new VehicleType<V>(id, capacity, fixedCost, amount));
    }
}
