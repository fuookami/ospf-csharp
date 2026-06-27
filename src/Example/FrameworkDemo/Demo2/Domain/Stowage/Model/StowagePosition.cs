#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 装载位置状态。Stowage position status flags.
/// </summary>
public sealed class PositionStatus
{
    /// <summary>是否需要配载 / Whether stowage needed</summary>
    public bool StowageNeeded { get; }
    /// <summary>是否需要调整 / Whether adjustment needed</summary>
    public bool AdjustmentNeeded { get; }
    /// <summary>是否需要谓词权重 / Whether predicate weight needed</summary>
    public bool PredicateWeightNeeded { get; }
    /// <summary>是否需要建议权重 / Whether recommended weight needed</summary>
    public bool RecommendedWeightNeeded { get; }
    /// <summary>是否可用 / Whether available</summary>
    public bool Available => StowageNeeded || AdjustmentNeeded;

    public PositionStatus(bool stowageNeeded, bool adjustmentNeeded, bool predicateWeightNeeded, bool recommendedWeightNeeded)
    {
        StowageNeeded = stowageNeeded;
        AdjustmentNeeded = adjustmentNeeded;
        PredicateWeightNeeded = predicateWeightNeeded;
        RecommendedWeightNeeded = recommendedWeightNeeded;
    }
}

/// <summary>
/// 装载位置最大装载重量。Position maximum load weight.
/// </summary>
/// <param name="Mlw">最大装载重量 / Maximum load weight (kg)</param>
public sealed record MaxLoadWeightLimit(double Mlw);

/// <summary>
/// 装载位置能力。Position capability check result.
/// </summary>
public sealed class PositionCapability
{
    /// <summary>是否通过 / Whether OK</summary>
    public bool Ok { get; }

    public PositionCapability(bool ok) { Ok = ok; }
}

/// <summary>
/// 配载位置。Stowage-specific position with status and loaded items.
/// </summary>
public sealed class StowagePosition
{
    /// <summary>飞机位置引用 / Aircraft position reference</summary>
    public Position AircraftPosition { get; }
    /// <summary>状态 / Status</summary>
    public PositionStatus Status { get; }
    /// <summary>最大装载重量限制 / Maximum load weight limit</summary>
    public MaxLoadWeightLimit Mlw { get; }
    /// <summary>最大装载数量 / Maximum load amount</summary>
    public ulong Mla { get; }
    /// <summary>已装载货物 / Loaded items</summary>
    public IReadOnlyList<Item> LoadedItems { get; }
    /// <summary>谓词装载重量 / Predicate load weight</summary>
    public double? Plw { get; }

    public StowagePosition(
        Position aircraftPosition,
        PositionStatus status,
        MaxLoadWeightLimit mlw,
        ulong mla,
        IReadOnlyList<Item>? loadedItems = null,
        double? plw = null)
    {
        AircraftPosition = aircraftPosition;
        Status = status;
        Mlw = mlw;
        Mla = mla;
        LoadedItems = loadedItems ?? new List<Item>();
        Plw = plw;
    }

    /// <summary>位置名称 / Position name</summary>
    public string Name => AircraftPosition.SpaceName;
    /// <summary>位置位置 / Position location</summary>
    public PositionLocation Location => AircraftPosition.Location;

    /// <summary>检查货物是否启用 / Check if item is enabled at this position</summary>
    public PositionCapability Enabled(Item item)
    {
        // TODO: Port from Kotlin Position.enabled()
        return new PositionCapability(true);
    }
}
