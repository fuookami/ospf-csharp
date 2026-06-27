#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 压舱物。Ballast for weight balance.
/// </summary>
public sealed class Ballast
{
    /// <summary>飞机型号 / Aircraft model</summary>
    public AircraftModel AircraftModel { get; }
    /// <summary>位置列表 / Position list</summary>
    public IReadOnlyList<StowagePosition> Positions { get; }
    /// <summary>最小压舱物重量 / Minimum ballast weight</summary>
    public double? MinBallastWeight { get; }

    public Ballast(
        AircraftModel aircraftModel,
        IReadOnlyList<StowagePosition> positions,
        double? minBallastWeight)
    {
        AircraftModel = aircraftModel;
        Positions = positions;
        MinBallastWeight = minBallastWeight;
    }
}
