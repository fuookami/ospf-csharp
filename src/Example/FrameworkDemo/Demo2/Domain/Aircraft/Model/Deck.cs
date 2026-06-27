#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 甲板位置。Deck location in the aircraft.
/// </summary>
public enum DeckLocation
{
    /// <summary>主甲板 / Main deck</summary>
    Main,
    /// <summary>前下货舱 / Lower forward cargo</summary>
    LowForward,
    /// <summary>后下货舱 / Lower aft cargo</summary>
    LowAft
}

/// <summary>
/// 甲板。Deck with doors, positions, and door-proximity mappings.
/// </summary>
public sealed class Deck
{
    /// <summary>甲板位置 / Deck location</summary>
    public DeckLocation Location { get; }
    /// <summary>舱门列表 / Hatch door list</summary>
    public IReadOnlyList<HatchDoor> Doors { get; }
    /// <summary>装载位置列表 / Position list</summary>
    public IReadOnlyList<Position> Positions { get; }
    /// <summary>舱门邻接关系 / Door ubiety mappings</summary>
    public IReadOnlyDictionary<HatchDoor, IReadOnlyList<DoorUbiety>> DoorUbieties { get; }

    public Deck(
        DeckLocation location,
        IReadOnlyList<HatchDoor> doors,
        IReadOnlyList<Position> positions,
        IReadOnlyDictionary<HatchDoor, IReadOnlyList<DoorUbiety>> doorUbieties)
    {
        Location = location;
        Doors = doors;
        Positions = positions;
        DoorUbieties = doorUbieties;
    }

    /// <inheritdoc />
    public override string ToString() => Location.ToString();
}
