#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 货物代码。Cargo code classification.
/// </summary>
public enum CargoCode
{
    BAL, ELD, FKI, CCD, ICE, AVI, AOG, ELI, ELM, MAG, HUB,
    PER, YYI, MAT, RRM, BIG, OHG, RFG, RFL, RFS, ROX, YYE,
    HWJ, RRY, RRW, CVV, Crush, Stiff, Empty, Virtual
}

/// <summary>
/// 货物类型。Cargo type with code and type name.
/// </summary>
public sealed class CargoType
{
    private static readonly Dictionary<string, CargoType> Cache = new();

    /// <summary>货物代码 / Cargo code</summary>
    public CargoCode? Code { get; }
    /// <summary>类型名称 / Type name</summary>
    public string Type { get; }

    private CargoType(CargoCode? code, string type)
    {
        Code = code;
        Type = type;
    }

    /// <summary>从代码获取货物类型 / Get cargo type from code</summary>
    public static CargoType FromCode(CargoCode code) =>
        Cache.TryGetValue(code.ToString(), out var ct) ? ct :
        Cache[code.ToString()] = new CargoType(code, code.ToString());

    /// <summary>从名称获取货物类型 / Get cargo type from name</summary>
    public static CargoType FromName(string name)
    {
        if (Cache.TryGetValue(name, out var ct)) return ct;
        var code = System.Enum.TryParse<CargoCode>(name, out var parsed) ? parsed : (CargoCode?)null;
        return Cache[name] = new CargoType(code, name);
    }
}

/// <summary>
/// 货物优先级类别。Cargo priority category.
/// </summary>
public enum CargoPriorityCategory
{
    High,
    Normal,
    Low
}

/// <summary>
/// 货物优先级。Cargo priority with name, level, category, and transfer flag.
/// </summary>
/// <param name="Name">优先级名称 / Priority name</param>
/// <param name="Priority">优先级值 / Priority value</param>
/// <param name="Category">优先级类别 / Priority category</param>
/// <param name="Transfer">是否中转 / Whether transfer</param>
public sealed record CargoPriority(
    string Name,
    UInt64 Priority,
    CargoPriorityCategory Category,
    bool Transfer = false);
