#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 货物状态。Item status for stowage planning.
/// </summary>
public enum ItemStatus
{
    Reserved,
    Optional,
    Preassigned,
    Loaded,
    AdjustmentNeeded
}

/// <summary>
/// 货物位置能力。Item location capability.
/// </summary>
public sealed class ItemLocation
{
    /// <summary>启用的甲板位置 / Enabled deck locations</summary>
    public IReadOnlySet<DeckLocation> EnabledLocations { get; }

    public ItemLocation(IReadOnlySet<DeckLocation> enabledLocations)
    {
        EnabledLocations = enabledLocations;
    }

    /// <summary>是否在指定甲板位置启用 / Whether enabled in specified deck location</summary>
    public bool EnabledIn(DeckLocation location) => EnabledLocations.Contains(location);
}

/// <summary>
/// 货物项。Cargo item for stowage planning.
/// </summary>
public sealed class Item
{
    /// <summary>货物名称 / Item name</summary>
    public string Name { get; }
    /// <summary>重量 / Weight (kg)</summary>
    public double Weight { get; }
    /// <summary>优先级 / Priority</summary>
    public CargoPriority Priority { get; }
    /// <summary>货物类型 / Cargo type</summary>
    public CargoType CargoType { get; }
    /// <summary>始发站 / Source station</summary>
    public string Source { get; }
    /// <summary>目的站 / Destination station</summary>
    public string Destination { get; }
    /// <summary>状态 / Status</summary>
    public ItemStatus Status { get; }
    /// <summary>位置能力 / Location capability</summary>
    public ItemLocation Location { get; }

    public Item(
        string name,
        double weight,
        CargoPriority priority,
        CargoType cargoType,
        string source,
        string destination,
        ItemStatus status,
        ItemLocation location)
    {
        Name = name;
        Weight = weight;
        Priority = priority;
        CargoType = cargoType;
        Source = source;
        Destination = destination;
        Status = status;
        Location = location;
    }
}
