#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 飞机类型。Aircraft type classification.
/// </summary>
public enum AircraftType
{
    B737,
    B757,
    B767,
    B747
}

/// <summary>
/// 飞机类型扩展方法。Extension methods for AircraftType.
/// </summary>
public static class AircraftTypeExtensions
{
    /// <summary>是否窄体 / Whether narrow body</summary>
    public static bool IsNarrowBody(this AircraftType type) =>
        type == AircraftType.B737 || type == AircraftType.B757;

    /// <summary>是否宽体 / Whether wide body</summary>
    public static bool IsWideBody(this AircraftType type) =>
        type == AircraftType.B767 || type == AircraftType.B747;

    /// <summary>是否需要压舱物 / Whether ballast is needed</summary>
    public static bool BallastNeeded(this AircraftType type) =>
        type == AircraftType.B757 || type == AircraftType.B767;

    /// <summary>是否偏好主甲板门空位 / Whether main deck door empty preferred</summary>
    public static bool MainDeckDoorEmptyPrefer(this AircraftType type) =>
        type == AircraftType.B757 || type == AircraftType.B767;
}

/// <summary>
/// 飞机型号。Aircraft model with type classification and physical unit definitions.
/// </summary>
public sealed class AircraftModel
{
    /// <summary>飞机类型（可能为 null 表示未知） / Aircraft type (null for unknown)</summary>
    public AircraftType? Type { get; }
    /// <summary>型号名称 / Model name</summary>
    public string ModelName { get; }
    /// <summary>子型号 / Minor model</summary>
    public Infrastructure.AircraftMinorModel MinorModel { get; }

    public AircraftModel(AircraftType? type, string modelName, Infrastructure.AircraftMinorModel minorModel)
    {
        Type = type;
        ModelName = modelName;
        MinorModel = minorModel;
    }

    /// <summary>
    /// 从子型号创建飞机型号。Create aircraft model from minor model.
    /// </summary>
    public static AircraftModel FromMinorModel(Infrastructure.AircraftMinorModel minorModel)
    {
        AircraftType? type = minorModel.Model switch
        {
            var s when s.Contains("B737") => AircraftType.B737,
            var s when s.Contains("B757") => AircraftType.B757,
            var s when s.Contains("B767") => AircraftType.B767,
            var s when s.Contains("B747") => AircraftType.B747,
            _ => (AircraftType?)null
        };
        return new AircraftModel(type, type?.ToString() ?? "Other", minorModel);
    }

    /// <summary>是否窄体 / Whether narrow body</summary>
    public bool NarrowBody => Type?.IsNarrowBody() ?? true;
    /// <summary>是否宽体 / Whether wide body</summary>
    public bool WideBody => Type?.IsWideBody() ?? false;
    /// <summary>是否需要压舱物 / Whether ballast is needed</summary>
    public bool BallastNeeded => Type?.BallastNeeded() ?? false;
    /// <summary>是否偏好主甲板门空位 / Whether main deck door empty preferred</summary>
    public bool MainDeckDoorEmptyPrefer => Type?.MainDeckDoorEmptyPrefer() ?? false;

    /// <summary>长度单位（英寸）/ Length unit (inch)</summary>
    public static PhysicalUnit LengthUnit => LengthUnits.Inch;

    /// <summary>重量单位（千克）/ Weight unit (kilogram)</summary>
    public static PhysicalUnit WeightUnit => SIBaseUnits.Kilogram;

    /// <summary>力单位（千克力）/ Force unit (kilogram-force)</summary>
    public static PhysicalUnit ForceUnit => ForceUnits.KilogramForce;

    /// <summary>力矩单位（千克力·英寸）/ Torque unit (kilogram-force inch)</summary>
    /// <remarks>
    /// `torqueUnit = forceUnit * lengthUnit`。C# 的 `PhysicalUnit` 有 `Multiply`，所以这里沿用乘法而不是写死一个常量——
    /// 写死会在换单位时静默不同步。
    ///
    /// The code's `torqueUnit = forceUnit * lengthUnit`. C#'s `PhysicalUnit` has `Multiply`, so this multiplies rather than hard-coding a constant: a constant silently
    /// desynchronises when the units change.
    /// </remarks>
    public static PhysicalUnit TorqueUnit => ForceUnit.Multiply(LengthUnit);

    /// <summary>面积单位 / The area unit</summary>
    /// <remarks>
    /// 由长度单位平方得来，所以 <see cref="PositionShape.Area"/>（长×宽，英寸²）与它同量纲，不需要再做单位换算。
    ///
    /// Squared from the length unit, so <see cref="PositionShape.Area"/> (length × width, square inch) shares its dimension and needs no conversion.
    /// </remarks>
    public static PhysicalUnit AreaUnit => LengthUnit.Pow(2);

    /// <summary>表面密度单位 / The surface density unit</summary>
    /// <remarks>
    /// 重量 / 面积。**不是**体积密度：表面密度问的是"这块甲板单位面积上压了多少重量"，与货物占多高无关。
    ///
    /// Weight / area. **Not** a volumetric density: surface density asks how much weight sits on a unit of deck area, regardless of how tall the cargo is.
    /// </remarks>
    public static PhysicalUnit SurfaceDensityUnit => WeightUnit.Divide(AreaUnit);

    /// <summary>
    /// 由重量求重力 / Gravity force from a weight.
    /// </summary>
    /// <param name="weight">重量 / The weight.</param>
    /// <returns>重力 / The gravity force.</returns>
    /// <remarks>
    /// `weight * (Flt64.one * (forceUnit / weightUnit))`——先把"每单位重量的力"做成量纲，再乘重量。
    /// 不能直接乘 9.80665：那是 `KilogramForce` 内部的比例，而这里要的是**量纲换算**，两者只是数值上巧合相等。
    ///
    /// The code is `weight * (Flt64.one * (forceUnit / weightUnit))` — build the force-per-weight dimension first, then multiply. Multiplying by 9.80665 directly would
    /// coincide numerically here but conflate a unit ratio with a conversion factor.
    /// </remarks>
    public static Quantity<Flt64> Gravity(Quantity<Flt64> weight) {
        var forcePerWeight = new Quantity<Flt64>(Flt64.One, ForceUnit.Divide(WeightUnit));
        return weight.Multiply(forcePerWeight);
    }
}
