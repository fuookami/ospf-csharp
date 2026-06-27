#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// 飞机子型号（值类型）。Aircraft minor model (value type wrapper).
/// </summary>
/// <param name="Model">型号字符串 / Model string</param>
public readonly record struct AircraftMinorModel(string Model);

/// <summary>
/// 注册号（值类型）。Registration number (value type wrapper).
/// </summary>
/// <param name="No">注册号 / Registration number</param>
public readonly record struct RegNo(string No);

/// <summary>
/// 航班号（值类型）。Flight number (value type wrapper).
/// </summary>
/// <param name="No">航班号 / Flight number</param>
public readonly record struct FlightNo(string No);

/// <summary>
/// IATA 代码（值类型）。IATA code (value type wrapper).
/// </summary>
/// <param name="Code">IATA 代码 / IATA code</param>
public readonly record struct IATACode(string Code);

/// <summary>
/// 平均气动弦百分比（值类型）。MAC percentage (value type wrapper).
/// </summary>
/// <param name="Value">MAC 百分比值 / MAC percentage value</param>
public readonly record struct MAC(double Value) : IPartialOrd<MAC>
{
    private const double Precision = 1e-3;

    /// <inheritdoc />
    public Order? PartialOrd(MAC other)
    {
        if (Value < other.Value - Precision) return new Order.Less();
        if (Value > other.Value + Precision) return new Order.Greater();
        return new Order.Equal();
    }
}

/// <summary>
/// 水平安定面角度（值类型）。Horizontal stabilizer angle (value type wrapper).
/// </summary>
/// <param name="Angle">角度字符串 / Angle string</param>
public readonly record struct HorizontalStabilizerAngle(string Angle);

/// <summary>
/// 水平安定面推力减速（值类型）。Horizontal stabilizer thrust drate (value type wrapper).
/// </summary>
/// <param name="Mod">修正字符串 / Modification string</param>
public readonly record struct HorizontalStabilizerThrustDrate(string Mod);
