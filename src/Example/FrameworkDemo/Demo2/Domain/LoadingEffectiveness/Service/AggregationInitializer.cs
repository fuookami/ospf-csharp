#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service;

/// <summary>
/// 装载效能聚合初始化器 / The loading-effectiveness aggregation initializer.
/// </summary>
/// <remarks>
/// 与对齐：**六条校验** + **四项派生**。
///
/// 六条校验，顺序照做：拖车名非空 → 拖车名唯一 → 顺序在 `0..255` → 同车内货物不重复 →
/// 每件货最多属于一辆车 → 拖车引用的货物必须存在。
///
/// 四项派生：
///
/// | 量 | 规则 |
/// | --- | --- |
/// | `cargosBySource` | 请求里**按顺序**分组：`cargo.source -> 下标列表` |
/// | `earlyEnd` | `(舱位数 − 1).coerceAtLeast(0) / 2` |
/// | `bigM` | `舱位数` |
/// | 拖车类型 | **显式映射**，不靠枚举序号 |
///
/// **six validations** plus **four derivations**. The validations, in order: trailer name non-blank; names unique; order within `0..255`; items
/// unique within a trailer; each item in at most one trailer; every referenced cargo exists. The derivations: `cargosBySource` groups the request **in order**, `earlyEnd` is
/// `(positionCount − 1).coerceAtLeast(0) / 2`, `bigM` is the position count, and trailer types are mapped **explicitly** rather than by enum ordinal.
/// </remarks>
internal static class AggregationInitializer
{
    /// <summary>
    /// 初始化聚合 / Initialize the aggregation.
    /// </summary>
    /// <param name="stowageAggregation">配载聚合 / The stowage aggregation.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <returns>聚合结果 / The aggregation result.</returns>
    public static Result<LoadingEffectivenessAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        StowageAggregation? stowageAggregation,
        RequestDTO input,
        StowageMode stowageMode)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (stowageAggregation is null)
        {
            return Results.Failed<LoadingEffectivenessAggregation>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "配载聚合未初始化 / the stowage aggregation is not initialised"));
        }

        Result<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>> trailers = BuildTrailers(
            input, stowageAggregation.Items);
        if (trailers is Failed<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<LoadingEffectivenessAggregation, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        IReadOnlyList<Trailer> built = ((Ok<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>>)trailers).Value!;

        return Results.Ok<LoadingEffectivenessAggregation>(new LoadingEffectivenessAggregation(
            stowageMode: stowageMode,
            items: stowageAggregation.Items,
            positions: stowageAggregation.Positions,
            stowage: stowageAggregation.Stowage,
            trailers: built,
            flight: stowageAggregation.Flight,
            cargosBySource: CargosBySource(input),
            load: stowageAggregation.Load));
    }

    /// <summary>
    /// 请求里按来源分组的货物下标 / The request's item indices grouped by source.
    /// </summary>
    /// <remarks>
    /// `mapIndexed { index, cargo -> cargo.source to index }.groupBy(...)`：**保序**、**容许重复来源**，
    /// 组内下标按请求顺序。用 `Dictionary` 的插入序来实现，不要换成排序。
    ///
    /// The code's `mapIndexed(...).groupBy(...)`: **order-preserving** and **tolerant of repeated sources**, with indices inside a group following request order. Implemented via
    /// `Dictionary` insertion order; do not replace with a sort.
    /// </remarks>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>来源到货物下标 / The source-to-item-index map.</returns>
    internal static IReadOnlyDictionary<string, IReadOnlyList<int>> CargosBySource(RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var grouped = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var order = new List<string>();
        for (int index = 0; index < input.Cargos.Count; ++index)
        {
            string source = input.Cargos[index].Source;
            if (!grouped.TryGetValue(source, out List<int>? bucket))
            {
                bucket = new List<int>();
                grouped[source] = bucket;
                order.Add(source);
            }
            bucket.Add(index);
        }
        var result = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        foreach (string source in order)
        {
            result[source] = grouped[source];
        }
        return result;
    }

    /// <summary>
    /// 校验并构造拖车 / Validate and build the trailers.
    /// </summary>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <param name="items">配载聚合的货物 / The stowage aggregation's items.</param>
    /// <returns>拖车列表 / The trailer list.</returns>
    internal static Result<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>> BuildTrailers(
        RequestDTO input,
        IReadOnlyList<Item> items)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(items);
        IReadOnlyList<TrailerInput> source = input.Trailers ?? Array.Empty<TrailerInput>();

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (TrailerInput trailer in source)
        {
            // 1. **名称非空**。
            // 1. **The name is not blank.**
            if (string.IsNullOrWhiteSpace(trailer.Name))
            {
                return Fail("拖车名称不能为空 / Trailer names must not be blank");
            }
            // 2. **名称唯一**。
            // 2. **The names are unique.**
            if (!names.Add(trailer.Name))
            {
                return Fail("拖车名称必须唯一 / Trailer names must be unique");
            }
            // 3. **顺序在 0..255**。
            // 3. **The order is within 0..255.**
            if (trailer.Order < 0 || trailer.Order > byte.MaxValue)
            {
                return Fail("拖车顺序必须在 0 到 255 之间 / Trailer order must be between 0 and 255");
            }
            // 4. **同车内货物不重复**。
            // 4. **Items are unique within a trailer.**
            var withinTrailer = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in trailer.Items)
            {
                if (!withinTrailer.Add(name))
                {
                    return Fail("同一拖车的货物不能重复 / Trailer item names must be unique within a trailer");
                }
            }
        }

        // 5. **每件货最多属于一辆车**。
        // 5. **Each item belongs to at most one trailer.**
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TrailerInput trailer in source)
        {
            foreach (string name in trailer.Items)
            {
                if (owner.TryGetValue(name, out string? previous))
                {
                    return Fail("每件货物最多只能属于一辆拖车 / Each cargo item may belong to at most one trailer: "
                        + name + " in " + previous + " and " + trailer.Name);
                }
                owner[name] = trailer.Name;
            }
        }

        var byName = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (Item item in items)
        {
            byName.TryAdd(item.Id, item);
        }

        var built = new List<Trailer>(source.Count);
        foreach (TrailerInput trailer in source)
        {
            var members = new List<Item>(trailer.Items.Count);
            foreach (string name in trailer.Items)
            {
                // 6. **引用的货物必须存在**。
                // 6. **Every referenced cargo exists.**
                if (!byName.TryGetValue(name, out Item? item))
                {
                    return Fail("拖车引用了未知货物 / Trailer references unknown cargo " + name);
                }
                members.Add(item);
            }
            built.Add(new Trailer(MapType(trailer.Type), (byte)trailer.Order, trailer.Name, members));
        }
        return Results.Ok<IReadOnlyList<Trailer>>(built);
    }

    /// <summary>
    /// 拖车类型显式映射 / Map the trailer type explicitly.
    /// </summary>
    /// <param name="type">输入类型 / The input type.</param>
    /// <returns>领域类型 / The domain type.</returns>
    private static TrailerType MapType(TrailerTypeInput type) => type switch
    {
        TrailerTypeInput.Hardstand => TrailerType.Hardstand,
        TrailerTypeInput.Transit => TrailerType.Transit,
        TrailerTypeInput.Warehouse => TrailerType.Warehouse,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown trailer type"),
    };

    /// <summary>
    /// 构造一条失败 / Build one failure.
    /// </summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        Results.Failed<IReadOnlyList<Trailer>>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
