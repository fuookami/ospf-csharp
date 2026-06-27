#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// ULD 类别。ULD category (pallet or container).
/// </summary>
public enum ULDCategory
{
    Pallet,
    Container
}

/// <summary>
/// ULD 代码。ULD code classification.
/// </summary>
public enum ULDCode
{
    PAG, PAJ, PMC, PRA, PLA, PYB, P1P, PZA, PGA,
    SZX, DQF, AAY, AAD, RTE, AAX, AKE, DPE, P6P, LAY, ALF, AMA, AMD, FQA
}

/// <summary>
/// ULD 代码扩展方法。Extension methods for ULDCode.
/// </summary>
public static class ULDCodeExtensions
{
    /// <summary>获取 ULD 类别 / Get ULD category</summary>
    public static ULDCategory GetCategory(this ULDCode code) => code switch
    {
        ULDCode.SZX or ULDCode.DQF or ULDCode.AAY or ULDCode.AAD or
        ULDCode.RTE or ULDCode.AAX or ULDCode.AKE or ULDCode.DPE or
        ULDCode.P6P or ULDCode.LAY or ULDCode.ALF or ULDCode.AMA or
        ULDCode.AMD or ULDCode.FQA => ULDCategory.Container,
        _ => ULDCategory.Pallet
    };
}

/// <summary>
/// ULD。Unit Load Device with name and code classification.
/// </summary>
public sealed class ULD
{
    private static readonly Dictionary<string, ULD> Cache = new();

    /// <summary>名称 / Name</summary>
    public string Name { get; }
    /// <summary>代码 / Code</summary>
    public ULDCode? Code { get; }
    /// <summary>类别 / Category</summary>
    public ULDCategory Category { get; }

    private ULD(string name, ULDCode? code)
    {
        Name = name;
        Code = code;
        Category = code?.GetCategory() ?? ULDCategory.Pallet;
    }

    /// <summary>
    /// 从代码获取 ULD 实例。Get ULD instance from code (cached).
    /// </summary>
    public static ULD FromCode(ULDCode code) =>
        Cache.TryGetValue(code.ToString(), out var uld) ? uld :
        Cache[code.ToString()] = new ULD(code.ToString(), code);

    /// <summary>
    /// 从名称获取 ULD 实例。Get ULD instance from name (cached).
    /// </summary>
    public static ULD FromName(string name)
    {
        if (Cache.TryGetValue(name, out var uld)) return uld;
        var code = System.Enum.TryParse<ULDCode>(name, out var parsed) ? parsed : (ULDCode?)null;
        return Cache[name] = new ULD(name, code);
    }
}
