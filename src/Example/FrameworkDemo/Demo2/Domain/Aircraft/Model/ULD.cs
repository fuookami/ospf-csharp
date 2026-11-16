#nullable enable

using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, ULD> Cache = new();

    /// <summary>名称 / Name</summary>
    public string Name { get; }
    /// <summary>代码 / Code</summary>
    public ULDCode? Code { get; }
    /// <summary>类别 / Category</summary>
    public ULDCategory Category { get; }

    /// <summary>
    /// 构造 ULD。Construct a ULD from a name and an explicit code.
    /// </summary>
    /// <param name="name">名称 / The name.</param>
    /// <param name="code">代码 / The code.</param>
    /// <remarks>
    /// **公开**是必须的：基线 `ULD` 是 `data class`，主构造函数本来就公开，所以 `ULD("AKE12345MU", ULDCode.AKE)` 这种
    /// "名称带编号、代码另行给出"的实例是常规写法。C# 之前把构造函数设成 private，只留两个工厂，而
    /// `FromName("AKE12345MU")` 按**整个名称**匹配代码（基线同口径），于是永远得到 `Code = null`——
    /// `enabled` 里那两条集装器规则因此是**永不触发的死代码**，而它们看起来完全正常。
    ///
    /// **Public on purpose**: its `ULD` is a `data class` whose primary constructor is public, so `ULD("AKE12345MU", ULDCode.AKE)` — a name carrying a number with the
    /// code given separately — is the ordinary form. C# had the constructor private behind two factories, and `FromName("AKE12345MU")` matches the code against the **whole**
    /// name (as the code does), so `Code` was always null — making the two ULD rules in `enabled` **dead code that never fires** while looking perfectly fine.
    /// </remarks>
    public ULD(string name, ULDCode? code)
    {
        Name = name;
        Code = code;
        Category = code?.GetCategory() ?? ULDCategory.Pallet;
    }

    /// <summary>
    /// 从代码获取 ULD 实例。Get ULD instance from code (cached).
    /// </summary>
    public static ULD FromCode(ULDCode code) =>
        Cache.GetOrAdd(code.ToString(), key => new ULD(key, code));

    /// <summary>
    /// 从名称获取 ULD 实例。Get ULD instance from name (cached).
    /// </summary>
    public static ULD FromName(string name) =>
        Cache.GetOrAdd(name, key => {
            ULDCode? code = System.Enum.TryParse<ULDCode>(key, out ULDCode parsed) ? parsed : (ULDCode?)null;
            return new ULD(key, code);
        });
}
