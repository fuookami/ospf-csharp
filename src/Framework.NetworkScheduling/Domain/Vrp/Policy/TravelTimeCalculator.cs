// VRP 行驶时间与弧成本计算 / VRP travel-time and arc-cost calculation.
// 与固定 Kotlin 基线 `TravelTimeCalculator.kt`（72 行）、`ArcCostCalculator.kt`（70 行）对应。
// Mirrors the pinned baseline's `TravelTimeCalculator.kt` and `ArcCostCalculator.kt`.

using System;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
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
/// 行驶时间计算扩展点 / Travel-time calculation extension point.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface ITravelTimeCalculator<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 计算一条弧的行驶时间 / Calculate the travel time of an arc.
    /// </summary>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="vehicleType">车辆类型 / Vehicle type.</param>
    /// <returns>行驶时间或计算失败 / The travel time, or a calculation failure.</returns>
    Result<TimeSpan, ErrorCode, Error<ErrorCode>> TravelTime(
        NetworkNode<V> from, NetworkNode<V> to, VehicleType<V> vehicleType);

    /// <summary>
    /// 按稳定弧标识计算行驶时间；默认退回端点策略 / Calculate travel time by stable arc identity; defaults to the endpoint
    /// policy.
    /// </summary>
    /// <param name="arc">资源弧 / Resource arc.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="vehicleType">车辆类型 / Vehicle type.</param>
    /// <returns>行驶时间或计算失败 / The travel time, or a calculation failure.</returns>
    Result<TimeSpan, ErrorCode, Error<ErrorCode>> TravelTime(
        ResourceArc<VehicleTypeId> arc,
        NetworkNode<V> from,
        NetworkNode<V> to,
        VehicleType<V> vehicleType) {
        ArgumentNullException.ThrowIfNull(arc);
        return TravelTime(from, to, vehicleType);
    }
}

/// <summary>
/// 将距离数值按 1:1 映射为实例时间轴持续时间的显式策略 / Explicit policy mapping distance values to instance-timeline
/// durations at 1:1.
/// </summary>
/// <remarks>
/// **"1:1 映射"需要一个时间轴刻度**：距离是长度量，行驶时间是时长，两者之间没有物理换算关系，只有建模约定。这里把约定
/// 显式交给 `schedulingWindow`——它知道本实例的时间轴上一个距离单位对应多长，因此换算规则不在策略里硬编码。
/// **The 1:1 mapping needs a timeline scale**: distance is a length and travel time a duration; there is no physical
/// conversion between them, only a modelling convention. That convention is delegated explicitly to `schedulingWindow`, which
/// knows how long one distance unit is on this instance's timeline, so the rule is not hard-coded in the policy.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="distanceCalculator">距离计算器 / Distance calculator.</param>
/// <param name="schedulingWindow">业务绝对时间轴 / Business absolute timeline.</param>
public sealed class DistanceAsTravelTimeCalculator<V>(
    IDistanceCalculator<V> distanceCalculator,
    TimeWindow<V> schedulingWindow) : ITravelTimeCalculator<V> where V : struct, IRealNumber<V> {
    /// <inheritdoc/>
    public Result<TimeSpan, ErrorCode, Error<ErrorCode>> TravelTime(
        NetworkNode<V> from,
        NetworkNode<V> to,
        VehicleType<V> vehicleType) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> distance = distanceCalculator.Distance(from, to);
        if (distance is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return NetworkSchedulingErrors.Failure<TimeSpan>(
                "计算行驶时间失败：距离计算失败 / Failed to calculate travel time: distance calculation failed");
        }
        return Results.Ok(schedulingWindow.DurationOf(distance.Value.Value.ToFlt64()));
    }

    /// <inheritdoc/>
    public Result<TimeSpan, ErrorCode, Error<ErrorCode>> TravelTime(
        ResourceArc<VehicleTypeId> arc,
        NetworkNode<V> from,
        NetworkNode<V> to,
        VehicleType<V> vehicleType) {
        ArgumentNullException.ThrowIfNull(arc);
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> distance = distanceCalculator.Distance(arc, from, to);
        if (distance is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return NetworkSchedulingErrors.Failure<TimeSpan>(
                "计算行驶时间失败：弧距离计算失败 / Failed to calculate travel time: arc distance calculation failed");
        }
        return Results.Ok(schedulingWindow.DurationOf(distance.Value.Value.ToFlt64()));
    }
}

/// <summary>
/// 弧成本计算扩展点 / Arc-cost calculation extension point.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IArcCostCalculator<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 计算弧成本 / Calculate the arc cost.
    /// </summary>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="distance">距离 / Distance.</param>
    /// <param name="travelTime">行驶时间 / Travel time.</param>
    /// <param name="vehicleType">车辆类型 / Vehicle type.</param>
    /// <returns>弧成本或计算失败 / The arc cost, or a calculation failure.</returns>
    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Cost(
        NetworkNode<V> from,
        NetworkNode<V> to,
        Quantity<V> distance,
        TimeSpan travelTime,
        VehicleType<V> vehicleType);

    /// <summary>
    /// 按稳定弧标识计算成本；默认退回端点策略 / Calculate cost by stable arc identity; defaults to the endpoint policy.
    /// </summary>
    /// <param name="arc">资源弧 / Resource arc.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="distance">距离 / Distance.</param>
    /// <param name="travelTime">行驶时间 / Travel time.</param>
    /// <param name="vehicleType">车辆类型 / Vehicle type.</param>
    /// <returns>弧成本或计算失败 / The arc cost, or a calculation failure.</returns>
    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Cost(
        ResourceArc<VehicleTypeId> arc,
        NetworkNode<V> from,
        NetworkNode<V> to,
        Quantity<V> distance,
        TimeSpan travelTime,
        VehicleType<V> vehicleType) {
        ArgumentNullException.ThrowIfNull(arc);
        return Cost(from, to, distance, travelTime, vehicleType);
    }
}

/// <summary>
/// 将距离数值映射到成本单位的 Demo17 策略 / Demo17 policy mapping distance values to the cost unit.
/// </summary>
/// <remarks>
/// **距离先归一化到实例距离单位，再把数值当作成本**：这一步是"以距离计费"的建模约定，因此单位必须显式换到位，
/// 否则同一个数值在不同单位下会得到差之千里的成本。
/// **The distance is first normalized to the instance distance unit, then its value is used as the cost**: that is the
/// "charge by distance" modelling convention, so the unit must be converted explicitly — otherwise the same number yields
/// wildly different costs depending on the unit.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="distanceUnit">距离单位 / Distance unit.</param>
/// <param name="costUnit">成本单位 / Cost unit.</param>
public sealed class DistanceArcCostCalculator<V>(
    PhysicalUnit distanceUnit,
    PhysicalUnit costUnit) : IArcCostCalculator<V> where V : struct, IRealNumber<V> {
    /// <inheritdoc/>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Cost(
        NetworkNode<V> from,
        NetworkNode<V> to,
        Quantity<V> distance,
        TimeSpan travelTime,
        VehicleType<V> vehicleType) {
        ArgumentNullException.ThrowIfNull(distance);

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> normalized = distance.ConvertTo(distanceUnit);
        if (normalized is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return NetworkSchedulingErrors.Failure<Quantity<V>>(
                "计算弧成本失败：距离单位无法归一化 / Failed to calculate arc cost: distance unit cannot be normalized");
        }
        return Results.Ok(new Quantity<V>(normalized.Value.Value, costUnit));
    }
}
