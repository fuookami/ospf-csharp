#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 飞行阶段。Flight phase for weight and balance calculations.
/// </summary>
public enum FlightPhase
{
    /// <summary>零燃油 / Zero fuel</summary>
    ZeroFuel,
    /// <summary>起飞 / Take off</summary>
    TakeOff,
    /// <summary>着陆 / Landing</summary>
    Landing
}
