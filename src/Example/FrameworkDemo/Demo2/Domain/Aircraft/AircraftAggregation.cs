#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;

/// <summary>
/// 飞机聚合根。Aircraft aggregation root holding all aircraft domain data.
/// </summary>
public sealed class AircraftAggregation
{
    /// <summary>注册号 / Registration number</summary>
    public Infrastructure.RegNo RegNo { get; }
    /// <summary>飞机型号 / Aircraft model</summary>
    public AircraftModel AircraftModel { get; }
    /// <summary>公式 / Formula</summary>
    public Formula Formula { get; }
    /// <summary>机身 / Fuselage</summary>
    public Fuselage Fuselage { get; }
    /// <summary>油箱 / Fuel tanks</summary>
    public IReadOnlyList<FuelTank> FuelTanks { get; }
    /// <summary>燃油常量（按飞行阶段） / Fuel constants by flight phase</summary>
    public IReadOnlyDictionary<FlightPhase, FuelConstant> Fuel { get; }
    /// <summary>甲板列表 / Deck list</summary>
    public IReadOnlyList<Deck> Decks { get; }
    /// <summary>邻接关系（按类型） / Neighbours by type</summary>
    public IReadOnlyDictionary<NeighbourType, IReadOnlyList<Neighbour>> Neighbours { get; }

    /// <summary>所有位置（从甲板展开） / All positions (flattened from decks)</summary>
    public IReadOnlyList<Position> Positions { get; }

    public AircraftAggregation(
        Infrastructure.RegNo regNo,
        AircraftModel aircraftModel,
        Formula formula,
        Fuselage fuselage,
        IReadOnlyList<FuelTank> fuelTanks,
        IReadOnlyDictionary<FlightPhase, FuelConstant> fuel,
        IReadOnlyList<Deck> decks,
        IReadOnlyDictionary<NeighbourType, IReadOnlyList<Neighbour>> neighbours)
    {
        RegNo = regNo;
        AircraftModel = aircraftModel;
        Formula = formula;
        Fuselage = fuselage;
        FuelTanks = fuelTanks;
        Fuel = fuel;
        Decks = decks;
        Neighbours = neighbours;

        var positions = new List<Position>();
        foreach (var deck in decks)
        {
            positions.AddRange(deck.Positions);
        }
        Positions = positions;
    }
}
