#nullable enable

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 物料编号值对象 / Material number value object.
/// </summary>
public sealed record MaterialNo(string No);

/// <summary>
/// 包装模式值对象 / Package pattern value object.
/// </summary>
public sealed record PackagePattern(string Code) {
    /// <summary>判断是否属于指定模式 / Check if belongs to the specified pattern.</summary>
    public bool Belong(PackagePattern other) => Code.StartsWith(other.Code);
}

/// <summary>
/// 包装编码值对象 / Package code value object.
/// </summary>
public sealed record PackageCode(string Code) {
    /// <summary>判断是否属于指定编码 / Check if belongs to the specified code.</summary>
    public bool Belong(PackageCode other) => Code.StartsWith(other.Code);
}

/// <summary>
/// 多批次标记 / Multi-batch sentinel.
/// </summary>
public static class BatchConstants {
    /// <summary>多批次通配符 / Multi-batch wildcard.</summary>
    public static readonly BatchNo MultiBatchNo = new("*");
}
