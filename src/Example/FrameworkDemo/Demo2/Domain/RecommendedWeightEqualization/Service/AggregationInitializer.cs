#nullable enable

using System;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service;

/// <summary>
/// 推荐重量均衡聚合初始化器 / The recommended-weight-equalisation aggregation initializer.
/// </summary>
/// <remarks>
/// **没有任何校验**，直接原样搬九个上游部件。
///
/// 三处必须沿用：
///
/// 1. **零校验**。`return Ok(Aggregation(...))`，一条判断都没有。加校验会让某些本来能跑通的请求在这里失败。
/// 2. **预约映射取内层字典** `StowageAggregation.Appointment.Mapping`。
/// 3. **`Ballast` 原样透传**（可为 `null`）。本仓的聚合不持有它——它在这里一次都没被读，见 `PriorityAppointment.From` 的说明。
///
/// **no validation at all**, just the nine upstream parts copied across. Three things must be copied: **zero checks** — the code is a bare
/// `return Ok(Aggregation(...))`, and adding any would fail requests that work today; **the appointment map is the inner dictionary**
/// `StowageAggregation.Appointment.Mapping`; and **`Ballast` is passed through** (nullable) — this implementation's aggregation does not hold it, since it is never read here (see
/// `PriorityAppointment.From`).
/// </remarks>
internal static class AggregationInitializer
{
    /// <summary>
    /// 初始化聚合 / Initialize the aggregation.
    /// </summary>
    /// <param name="aircraftAggregation">飞机聚合 / The aircraft aggregation.</param>
    /// <param name="stowageAggregation">配载聚合 / The stowage aggregation.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>聚合结果 / The aggregation result.</returns>
    public static Result<RecommendedWeightEqualizationAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        AircraftAggregation? aircraftAggregation,
        StowageAggregation? stowageAggregation,
        RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // **上游可空**（起），取非空会抛 `NullReferenceException`——那是崩溃不是失败。
        // **The upstreams are nullable** (since then); taking non-null throws a `NullReferenceException`, a crash rather than a failure.
        if (aircraftAggregation is null)
        {
            return Fail("飞机聚合未初始化 / the aircraft aggregation is not initialised");
        }
        if (stowageAggregation is null)
        {
            return Fail("配载聚合未初始化 / the stowage aggregation is not initialised");
        }

        return Results.Ok<RecommendedWeightEqualizationAggregation>(new RecommendedWeightEqualizationAggregation(
            aircraftModel: aircraftAggregation.AircraftModel,
            items: stowageAggregation.Items,
            positions: stowageAggregation.Positions,
            // **`Appointment.Mapping` 的键值是 `Aircraft.Model.Position`**，而本域用 `StowagePosition`。
            // 按 `SpaceName` 把 `Position` 映射回 `StowagePosition`——两边来自同一个配载聚合，名字唯一（`Aircraft.AggregationInitializer` 已校验）。
            // **`Appointment.Mapping`'s values are `Aircraft.Model.Position`** while this domain uses `StowagePosition`. Map them back by `SpaceName`; both sides come from the same
            // stowage aggregation and names are unique (validated by `Aircraft.AggregationInitializer`).
            appointment: MapAppointment(stowageAggregation),
            stowage: stowageAggregation.Stowage,
            load: stowageAggregation.Load));
    }

    /// <summary>
    /// 把按 `Position` 的预约映射成按 `StowagePosition` / Map the `Position`-keyed appointments onto `StowagePosition`.
    /// </summary>
    /// <param name="aggregation">配载聚合 / The stowage aggregation.</param>
    /// <returns>按 `StowagePosition` 的预约 / The `StowagePosition`-keyed appointments.</returns>
    private static System.Collections.Generic.IReadOnlyDictionary<Stowage.Model.Item, Stowage.Model.StowagePosition> MapAppointment(
        StowageAggregation aggregation)
    {
        var byName = new System.Collections.Generic.Dictionary<string, Stowage.Model.StowagePosition>(StringComparer.Ordinal);
        foreach (Stowage.Model.StowagePosition position in aggregation.Positions)
        {
            byName.TryAdd(position.Name, position);
        }
        var mapped = new System.Collections.Generic.Dictionary<Stowage.Model.Item, Stowage.Model.StowagePosition>();
        foreach (System.Collections.Generic.KeyValuePair<Stowage.Model.Item, Aircraft.Model.Position> entry
            in aggregation.Appointment.Mapping)
        {
            if (byName.TryGetValue(entry.Value.SpaceName, out Stowage.Model.StowagePosition? found))
            {
                mapped[entry.Key] = found;
            }
        }
        return mapped;
    }

    /// <summary>
    /// 构造一条失败 / Build one failure.
    /// </summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<RecommendedWeightEqualizationAggregation, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        Results.Failed<RecommendedWeightEqualizationAggregation>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
