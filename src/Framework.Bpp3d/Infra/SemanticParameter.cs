#nullable enable

using System;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 物料编号值对象 / Material number value object.
/// </summary>
public sealed record MaterialNo(string No);

/// <summary>
/// 包装模式值对象 / Package pattern value object.
/// </summary>
public sealed record PackagePattern(string Code) {
    /// <summary>
    /// 此模式是否为另一个模式的**前缀** / Whether this pattern is a **prefix of** the other.
    /// </summary>
    /// <param name="other">用于判断包含关系的另一个包装模式 / Another package pattern to check containment against.</param>
    /// <returns>此模式是否为对方的前缀 / Whether this pattern is a prefix of the other.</returns>
    /// <remarks>
    /// **方向是"我是对方的前缀"，不是"对方是我的前缀"**。基线写的是 `ano.code.startsWith(code)`：
    /// 短模式 `A` 属于长模式 `AB`，反过来不成立。写成 `Code.StartsWith(other.Code)` 会把包含关系整个颠倒，
    /// 而这类错误**在两侧字符串等长时完全看不出来**，只在真实的长短层级里才暴露。
    ///
    /// **The direction is "I am a prefix of the other", not "the other is a prefix of me"**. The code writes `ano.code.startsWith(code)`: the short pattern `A`
    /// belongs to the long pattern `AB`, never the reverse. Writing `Code.StartsWith(other.Code)` inverts the containment entirely, and **such a bug is invisible
    /// whenever both strings are the same length** — it only surfaces in a real short/long hierarchy.
    /// </remarks>
    public bool Belong(PackagePattern other) => other.Code.StartsWith(Code, StringComparison.Ordinal);
}

/// <summary>
/// 包装编码值对象 / Package code value object.
/// </summary>
public sealed record PackageCode(string Code) {
    /// <summary>
    /// 此编码是否为另一个编码的**前缀** / Whether this code is a **prefix of** the other.
    /// </summary>
    /// <param name="other">用于判断包含关系的另一个包装编码 / Another package code to check containment against.</param>
    /// <returns>此编码是否为对方的前缀 / Whether this code is a prefix of the other.</returns>
    public bool Belong(PackageCode other) => other.Code.StartsWith(Code, StringComparison.Ordinal);
}

/// <summary>
/// 多批次标记 / Multi-batch sentinel.
/// </summary>
public static class BatchConstants {
    /// <summary>多批次通配符 / Multi-batch wildcard.</summary>
    public static readonly BatchNo MultiBatchNo = new("*");
}
