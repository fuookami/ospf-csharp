#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Service;

/// <summary>
/// MAC 聚合初始化器 / The MAC aggregation initializer.
/// </summary>
/// <remarks>
/// **一条校验、一次构造**。
///
/// `horizontalStabilizers` 传的是**空表**。这不是"还没接"：现有实现里就是空的，因为安定面数据来自机型表，而这张表本仓还没有。
/// 因此 `MacAggregation.Register` 的安定面循环在这里是空转，`trim` 符号不会出现。沿用并记录，不假装已经接上。
///
/// **one validation, one construction**. `horizontalStabilizers` is an **empty map**. That is not "not wired yet";
/// it is the current state: stabilizer data comes from a type table this implementation does not have. The stabilizer loop in `MacAggregation.Register` is therefore a no-op here and
/// no `trim` symbol appears. Copied and recorded rather than pretending it is wired.
/// </remarks>
internal static class AggregationInitializer
{
    /// <summary>
    /// 从飞机与配载聚合构造 MAC 聚合 / Build the MAC aggregation from the aircraft and stowage aggregations.
    /// </summary>
    /// <param name="aircraftAggregation">飞机聚合 / The aircraft aggregation.</param>
    /// <param name="stowageAggregation">配载聚合 / The stowage aggregation.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>聚合或失败 / The aggregation or a failure.</returns>
    public static Result<MacAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        AircraftAggregation aircraftAggregation,
        StowageAggregation stowageAggregation,
        RequestDTO input) {
        ArgumentNullException.ThrowIfNull(aircraftAggregation);
        ArgumentNullException.ThrowIfNull(stowageAggregation);
        ArgumentNullException.ThrowIfNull(input);

        if (stowageAggregation.Positions.Count == 0) {
            return Results.Failed<MacAggregation>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "MAC 聚合至少需要一个装载位置 / MAC aggregation requires at least one stowage position"));
        }

        return Results.Ok(new MacAggregation(
            fuselage: aircraftAggregation.Fuselage,
            fuel: aircraftAggregation.Fuel,
            formula: aircraftAggregation.Formula,
            positions: stowageAggregation.Positions,
            load: stowageAggregation.Load,
            totalWeight: stowageAggregation.TotalWeight,
            // 这里就是空表，见类注释。
            // This is an empty map; see the class remarks.
            horizontalStabilizers: new Dictionary<HorizontalStabilizer.Key, (IReadOnlyList<HorizontalStabilizer.Point>, HorizontalStabilizer.Limit)>(),
            wideBody: aircraftAggregation.AircraftModel.WideBody));
    }
}
