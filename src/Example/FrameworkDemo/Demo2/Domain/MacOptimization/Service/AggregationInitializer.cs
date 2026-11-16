#nullable enable

using System;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service;

/// <summary>
/// 重心优化聚合初始化器 / The MAC-optimisation aggregation initializer.
/// </summary>
/// <remarks>
/// **一条校验**——配载位置不能为空——然后原样把上游的五个部件
/// 交给聚合。
///
/// 三处必须沿用：
///
/// 1. **只校验位置非空**。这一条是"MAC 优化至少需要一个装载位置"；不加别的校验，也不翻译 DTO。
/// 2. **`MACRange` 由聚合自己算**，初始化器不碰 `TotalWeight` 以外的重量数据。
/// 3. **上游聚合可能为 null**（起可空），取非空会抛 `NullReferenceException`——那是崩溃不是失败，所以显式守卫。
///
/// **one check** — stowage positions must not be empty — then the five upstream parts are handed to the aggregation as-is. Three things must be copied:
/// **only the position check**, matching its "MAC optimisation requires at least one stowage position", with no other validation and no DTO translation; **`MACRange` is
/// computed by the aggregation**, and the initializer touches no weight data beyond `TotalWeight`; and **the upstream aggregations may be null** (nullable since then), where taking
/// non-null throws a `NullReferenceException` — a crash rather than a failure — so they are guarded explicitly.
/// </remarks>
internal static class AggregationInitializer
{
    /// <summary>
    /// 初始化重心优化聚合 / Initialize the MAC-optimisation aggregation.
    /// </summary>
    /// <param name="aircraftAggregation">飞机聚合 / The aircraft aggregation.</param>
    /// <param name="stowageAggregation">配载聚合 / The stowage aggregation.</param>
    /// <param name="macAggregation">重心聚合 / The MAC aggregation.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>聚合结果 / The aggregation result.</returns>
    public static Result<MacOptimizationAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        AircraftAggregation? aircraftAggregation,
        StowageAggregation? stowageAggregation,
        MacAggregation? macAggregation,
        RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (aircraftAggregation is null)
        {
            return Fail("飞机聚合未初始化 / the aircraft aggregation is not initialised");
        }
        if (stowageAggregation is null)
        {
            return Fail("配载聚合未初始化 / the stowage aggregation is not initialised");
        }
        if (macAggregation is null)
        {
            return Fail("重心聚合未初始化 / the MAC aggregation is not initialised");
        }
        if (stowageAggregation.Positions.Count == 0)
        {
            return Fail("MAC 优化聚合至少需要一个装载位置 / MAC optimization aggregation requires at least one stowage position");
        }

        return Results.Ok<MacOptimizationAggregation>(new MacOptimizationAggregation(
            aircraftModel: aircraftAggregation.AircraftModel,
            formula: aircraftAggregation.Formula,
            totalWeight: stowageAggregation.TotalWeight,
            torque: macAggregation.Torque,
            horizontalStabilizers: macAggregation.HorizontalStabilizers));
    }

    /// <summary>
    /// 构造一条失败 / Build one failure.
    /// </summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<MacOptimizationAggregation, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        Results.Failed<MacOptimizationAggregation>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
