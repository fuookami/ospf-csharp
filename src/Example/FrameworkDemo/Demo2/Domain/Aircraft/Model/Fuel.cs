#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 油箱类型。Fuel tank type classification.
/// </summary>
public enum FuelTankType
{
    Main,
    Center,
    OuterMain,
    InnerMain,
    Reserve
}

/// <summary>
/// 油箱。Fuel tank with capacity and balanced arm data.
/// </summary>
/// <param name="Type">油箱类型 / Tank type</param>
/// <param name="Name">油箱名称 / Tank name</param>
/// <param name="MaxVolume">最大容量 / Maximum volume (liter)</param>
public sealed record FuelTank(
    FuelTankType Type,
    string Name,
    double MaxVolume);

/// <summary>
/// 燃油常量。Fuel constant with density, weight, and index.
/// </summary>
/// <param name="Density">密度 / Density (kg/liter)</param>
/// <param name="Weight">重量 / Weight (kg)</param>
/// <param name="Index">指数 / Index</param>
public sealed record FuelConstant(
    double Density,
    double Weight,
    double Index);
