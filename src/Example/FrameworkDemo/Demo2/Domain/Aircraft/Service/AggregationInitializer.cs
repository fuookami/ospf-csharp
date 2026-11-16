#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Service;

/// <summary>
/// 飞机聚合初始化器 / The aircraft aggregation initializer.
/// </summary>
/// <remarks>
/// 与基线 `aircraft/service/AggregationInitializer.kt` 对齐。**校验在前、构造在后**，任何一条校验失败都不构造聚合。
///
/// 校验顺序照抄基线（八条），因为消息与判定必须一一对应：
///
/// | # | 校验 | 为什么不能省 |
/// | --- | --- | --- |
/// | 1 | 机型非 `Unknown` | `AircraftModel` 由机型派生单位，未知机型没有单位 |
/// | 2 | 至少一个舱位 | 空甲板让后续所有约束退化 |
/// | 3 | 舱位数不超过 256 | 装载顺序索引是 `byte` |
/// | 4 | 舱位名非空 | 名是配载解的主键 |
/// | 5 | 舱位名唯一 | 重名让解无法回指 |
/// | 6 | 最大载重为正且有限 | 载重约束的分母 |
/// | 7 | 力臂有限 | 力矩计算的输入 |
/// | 8 | 空间/装载顺序有效 | 见下 |
///
/// **第 8 条是复合条件**，基线把它写成一行"或"：空间表与舱位表大小不等、任一空间 id 为空、任一舱位的 `spaceId` 不在空间表里、
/// 任一 `partitionId` 为空、任一 `loadingOrder` 越界、装载顺序不唯一——六种情形共用同一条消息。照抄这条"合并"是有意的：
/// 拆成六条消息会偏离基线，而基线的调用方只区分成功与失败。
///
/// Aligned with the baseline: **validation first, construction second**, and no aggregation is built when any check fails. The eight checks are copied in baseline order, because
/// their messages and predicates pair up one to one (tabulated above). **Check 8 is a compound condition** the baseline writes as a single disjunction — space table size mismatch,
/// a blank space id, a position's `spaceId` missing from the table, a blank `partitionId`, an out-of-range `loadingOrder`, or a duplicate loading order — with six situations
/// sharing one message. Copying that merge is deliberate: splitting it into six messages would diverge from the baseline, whose callers only distinguish success from failure.
/// </remarks>
internal static class AggregationInitializer
{
    /// <summary>装载顺序索引上限 / The loading-order index limit</summary>
    private const int MaxPositionCount = 256;

    /// <summary>
    /// 从请求初始化飞机聚合 / Initialize the aircraft aggregation from the request.
    /// </summary>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>飞机聚合结果 / The aircraft aggregation result.</returns>
    public static Result<AircraftAggregation, ErrorCode, Error<ErrorCode>> Initialize(RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.AircraftType == AircraftTypeInput.Unknown)
        {
            return Fail("未知机型不能初始化飞机聚合 / Unknown aircraft type cannot initialize aircraft aggregation");
        }
        if (input.Positions.Count == 0)
        {
            return Fail("飞机至少需要一个可用舱位 / Aircraft requires at least one available position");
        }
        if (input.Positions.Count > MaxPositionCount)
        {
            return Fail("舱位数量超过装载顺序索引上限：" + input.Positions.Count
                + " / Position count exceeds loading-order index limit: " + input.Positions.Count);
        }
        foreach (PositionInput position in input.Positions)
        {
            if (string.IsNullOrWhiteSpace(position.Name))
            {
                return Fail("舱位名称不能为空 / Position name must not be blank");
            }
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (PositionInput position in input.Positions)
        {
            if (!names.Add(position.Name))
            {
                return Fail("舱位名称必须唯一 / Position names must be unique");
            }
        }
        foreach (PositionInput position in input.Positions)
        {
            if (!double.IsFinite(position.MaxWeight) || position.MaxWeight <= 0.0)
            {
                return Fail("舱位最大载重必须为正且有限 / Position maximum weight must be positive and finite");
            }
        }
        foreach (PositionInput position in input.Positions)
        {
            if (!double.IsFinite(position.LongitudinalArm) || !double.IsFinite(position.LateralArm))
            {
                return Fail("舱位力臂必须为有限值 / Position arms must be finite");
            }
        }

        // **第 8 条：空间与装载顺序**。六种情形共用一条消息，照抄基线的合并。
        // **Check 8: spaces and loading order**. Six situations share one message, copying the baseline's merge.
        var spaces = new Dictionary<string, LoadingSpaceInput>(StringComparer.Ordinal);
        foreach (LoadingSpaceInput space in input.Spaces ?? Array.Empty<LoadingSpaceInput>())
        {
            if (space is null || string.IsNullOrWhiteSpace(space.Id) || !spaces.TryAdd(space.Id, space))
            {
                return Fail("物理空间或装载顺序无效 / Invalid physical space or loading order");
            }
        }
        var orders = new HashSet<int>();
        foreach (PositionInput position in input.Positions)
        {
            if (!spaces.ContainsKey(position.SpaceId)
                || string.IsNullOrWhiteSpace(position.PartitionId)
                || position.LoadingOrder is < 0 or > 255
                || !orders.Add(position.LoadingOrder))
            {
                return Fail("物理空间或装载顺序无效 / Invalid physical space or loading order");
            }
        }
        foreach (PositionInput position in input.Positions)
        {
            if (position.Geometry is not { } geometry
                || !double.IsFinite(geometry.Front) || !double.IsFinite(geometry.Back)
                || !double.IsFinite(geometry.Left) || !double.IsFinite(geometry.Right)
                || geometry.Front >= geometry.Back || geometry.Left >= geometry.Right)
            {
                return Fail("舱位必须提供有限正矩形边界 / Positions require finite positive rectangle bounds");
            }
        }

        // ---- 构造 ----
        // ---- Construction ----
        // **机型必须来自请求**。 之前这里**硬编码成 `AircraftType. `**，请求里的机型只进了 `modelName`——
        // 于是 `aggregation.AircraftModel.Type` 永远是 ：**所有按机型分流的地方都走了窄体那条**，而且没有任何报错。
        // 的压舱物、主甲板门空位偏好、以及宽体的二维装载内核**全都因此从未生效**。
        //
        // **The type must come from the request** . Before this line **hard-coded `AircraftType. `** and let the requested type land only in
        // `modelName`, so `aggregation.AircraftModel.Type` was always : **every type-dependent branch took the narrow-body path**, silently. ballast,
        // the main-deck-door preference and the wide-body 2-D kernel **had therefore never taken effect**.
        AircraftType aircraftType = input.AircraftType switch {
            AircraftTypeInput.B757 => AircraftType.B757,
            AircraftTypeInput.B767 => AircraftType.B767,
            AircraftTypeInput.B747 => AircraftType.B747,
            // **` ` 与 `Unknown` 都落到 **：`Unknown` 在上面的校验里已被拒，这里只是让 switch 穷尽。
            // **Both ` ` and `Unknown` fall to **; `Unknown` is refused by the check above, so this only makes the switch exhaustive.
            _ => AircraftType.B737,
        };
        var aircraftModel = new AircraftModel(aircraftType, input.AircraftType.ToString(), new AircraftMinorModel(input.AircraftType.ToString()));
        var formula = new Formula(
            aircraftModel: aircraftModel,
            lip: 0.0,
            chord: 1.0,
            standardDatum: 0.0,
            forceDistanceCoefficient: 1.0,
            doiCorrection: 0.0);
        var fuselage = new Fuselage(Liferaft: null, Dow: 0.0, Doi: 0.0, BalancedArm: 0.0);

        double ToModelLength(double value) => input.LengthUnit switch
        {
            LengthUnitInput.Meter => value / 0.0254,
            LengthUnitInput.Centimeter => value / 2.54,
            _ => value,
        };

        var basePositions = new List<AircraftPosition>(input.Positions.Count);
        for (int index = 0; index < input.Positions.Count; ++index)
        {
            PositionInput position = input.Positions[index];
            PositionGeometryInput geometry = position.Geometry!;
            PositionLocation location = spaces[position.SpaceId].Location switch
            {
                DeckLocationInput.Main => PositionLocation.NormalMainLocation,
                DeckLocationInput.LowForward => PositionLocation.LowForwardNotBulkLocation,
                _ => PositionLocation.LowAftNotBulkLocation,
            };
            // **矩形边界必须和力臂用同一个单位**： 实测这里把几何**硬编码成英寸**（`* 0.0254`），而力臂走 `ToModelLength`
            // 按 `input.LengthUnit` 换算。请求里写的是米，于是 1.0 x 1.0 的舱位被算成 `0.0254 x 0.0254 = 0.000645` 平方米，
            // 表面密度系数放大 1550 倍（8 kg 变 12400），`≤ 1000` 的上限连一件货都装不下——**而所有约束看上去都加对了**。
            // 现在两边都走 `ToModelLength`：先换算成模型单位（英寸），再按 0.0254 折回平方米。
            //
            // **The rectangle must use the same unit as the arms**: measured that this line **hard-codes inches** (`* 0.0254`) while the arms go through
            // `ToModelLength`, which honours `input.LengthUnit`. Requests are written in metres, so a 1.0 x 1.0 position came out as `0.0254 x 0.0254 = 0.000645`
            // square metres, inflating the surface-density coefficient 1550x (8 kg became 12400) so the `<= 1000` cap could not fit a single piece — **while every
            // constraint looked correctly added**. Both now go through `ToModelLength`: convert to model units (inches), then fold back to square metres with 0.0254.
            double lengthInInches = ToModelLength(geometry.Back) - ToModelLength(geometry.Front);
            double widthInInches = ToModelLength(geometry.Right) - ToModelLength(geometry.Left);
            basePositions.Add(new AircraftPosition(
                id: (ulong)index,
                spaceName: position.Name,
                sizeCode: "N",
                linearLoadingOrder: (byte)position.LoadingOrder,
                coordinate: new PositionCoordinate(
                    frontArm: ToModelLength(geometry.Front),
                    backArm: ToModelLength(geometry.Back),
                    leftArm: ToModelLength(geometry.Left),
                    rightArm: ToModelLength(geometry.Right)),
                shape: new PositionShape(
                    lengthInInches,
                    widthInInches,
                    0.0,
                    DeclaredArea: lengthInInches * 0.0254 * (widthInInches * 0.0254)),
                location: location));
        }

        // **按装载顺序串前后驱**：先排序，再逐位连接。
        // **Link predecessors and successors in loading order**: sort first, then connect position by position.
        var orderedPositions = new List<AircraftPosition>(basePositions);
        orderedPositions.Sort((left, right) => left.LinearLoadingOrder.CompareTo(right.LinearLoadingOrder));
        for (int index = 0; index < orderedPositions.Count; ++index)
        {
            AircraftPosition position = orderedPositions[index];
            position.LoadingOrder = new LoadingOrder(
                location: position.Location.Location,
                order: position.LinearLoadingOrder,
                directPrec: index == 0
                    ? new HashSet<AircraftPosition>()
                    : new HashSet<AircraftPosition> { orderedPositions[index - 1] },
                directSucc: index == orderedPositions.Count - 1
                    ? new HashSet<AircraftPosition>()
                    : new HashSet<AircraftPosition> { orderedPositions[index + 1] });
        }

        // **物理邻接按"物理空间"分组**，不是按甲板：基线用 `spacesByPosition` 把同 `spaceId` 的舱位连起来。
        // **Physical adjacency groups by physical space**, not by deck: the baseline uses `spacesByPosition` to chain positions sharing a `spaceId`.
        var spaceByPosition = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PositionInput position in input.Positions)
        {
            spaceByPosition[position.Name] = position.SpaceId;
        }
        var bySpace = new Dictionary<string, List<AircraftPosition>>(StringComparer.Ordinal);
        foreach (AircraftPosition position in orderedPositions)
        {
            string spaceId = spaceByPosition[position.SpaceName];
            if (!bySpace.TryGetValue(spaceId, out List<AircraftPosition>? bucket))
            {
                bucket = new List<AircraftPosition>();
                bySpace[spaceId] = bucket;
            }
            bucket.Add(position);
        }
        var physicalNeighbours = new List<Neighbour>();
        foreach (List<AircraftPosition> bucket in bySpace.Values)
        {
            for (int index = 0; index + 1 < bucket.Count; ++index)
            {
                physicalNeighbours.Add(new Neighbour(NeighbourType.Physics, new PositionPair(bucket[index], bucket[index + 1])));
            }
        }
        var adjacentNeighbours = new List<Neighbour>();
        for (int index = 0; index + 1 < orderedPositions.Count; ++index)
        {
            adjacentNeighbours.Add(new Neighbour(NeighbourType.Physics, new PositionPair(orderedPositions[index], orderedPositions[index + 1])));
        }

        var neighbours = new Dictionary<NeighbourType, IReadOnlyList<Neighbour>> {
            [NeighbourType.Physics] = physicalNeighbours,
            [NeighbourType.IndirectPhysics] = Retag(physicalNeighbours, NeighbourType.IndirectPhysics),
            [NeighbourType.LinearLoadingOrder] = Retag(adjacentNeighbours, NeighbourType.LinearLoadingOrder),
            [NeighbourType.TopologicalLoadingOrder] = Retag(adjacentNeighbours, NeighbourType.TopologicalLoadingOrder),
        };

        var defaultFuelTank = new FuelTank(FuelTankType.Main, "DEFAULT", 0.0);
        var defaultFuel = new FuelConstant(Density: 0.0, Weight: 0.0, Index: 0.0);
        var fuel = new Dictionary<FlightPhase, FuelConstant>();
        foreach (FlightPhase phase in Enum.GetValues<FlightPhase>())
        {
            fuel[phase] = defaultFuel;
        }

        // **甲板按 `PositionLocation.Location` 分组**，与基线 `groupBy { it.location.location }` 一致。
        // **Decks group by `PositionLocation.Location`**, matching the baseline's `groupBy`.
        var deckBuckets = new Dictionary<DeckLocation, List<AircraftPosition>>();
        foreach (AircraftPosition position in basePositions)
        {
            if (!deckBuckets.TryGetValue(position.Location.Location, out List<AircraftPosition>? bucket))
            {
                bucket = new List<AircraftPosition>();
                deckBuckets[position.Location.Location] = bucket;
            }
            bucket.Add(position);
        }
        var decks = new List<Deck>(deckBuckets.Count);
        foreach (KeyValuePair<DeckLocation, List<AircraftPosition>> entry in deckBuckets)
        {
            decks.Add(new Deck(
                location: entry.Key,
                doors: Array.Empty<HatchDoor>(),
                positions: entry.Value,
                doorUbieties: new Dictionary<HatchDoor, IReadOnlyList<DoorUbiety>>()));
        }

        return Results.Ok<AircraftAggregation>(new AircraftAggregation(
            regNo: new RegNo("DEFAULT"),
            aircraftModel: aircraftModel,
            formula: formula,
            fuselage: fuselage,
            fuelTanks: new[] { defaultFuelTank },
            fuel: fuel,
            decks: decks,
            neighbours: neighbours));
    }

    /// <summary>
    /// 换类型、保配对 / Retype while keeping the pairs.
    /// </summary>
    /// <param name="source">源邻接 / The source neighbours.</param>
    /// <param name="type">目标类型 / The target type.</param>
    /// <returns>换过类型的邻接 / The retyped neighbours.</returns>
    private static List<Neighbour> Retag(IReadOnlyList<Neighbour> source, NeighbourType type)
    {
        var result = new List<Neighbour>(source.Count);
        foreach (Neighbour neighbour in source)
        {
            result.Add(new Neighbour(type, neighbour.Pair));
        }
        return result;
    }

    /// <summary>
    /// 构造一条失败 / Build one failure.
    /// </summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<AircraftAggregation, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        Results.Failed<AircraftAggregation>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
