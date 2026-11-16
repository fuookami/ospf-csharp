#nullable enable

using Fuookami.Ospf.Math.Symbol.Expression;
using System;

namespace Fuookami.Ospf.Math.Symbol.Serde;
/// <summary>
/// 符号身份序列化工具（前向声明，a later phase 完整移植）/ Symbol identity serde helpers (forward-declared; a later phase owns full module).
/// </summary>
internal static class SymbolIdentitySerde {
    /// <summary>将符号序列化为标识字符串 / Serialize symbol to identifier string.</summary>
    public static string ToSerializedIdentifier(ISymbol symbol) => symbol switch {
        PathSymbol ps => ps.SymbolId,
        IIdentifiedSymbol ids => ids.SymbolId,
        _ => SymbolIdentity.Identity(symbol)
    };

    /// <summary>从标识字符串反序列化符号 / Deserialize symbol from identifier string.</summary>
    public static ISymbol SymbolOfSerializedIdentifier(string identifier) {
        ArgumentNullException.ThrowIfNull(identifier);

        if (identifier.StartsWith("path:", StringComparison.Ordinal)) {
            return PathSymbol.From(identifier["path:".Length..]);
        }
        if (identifier.StartsWith("name:", StringComparison.Ordinal)) {
            return NamedSymbol.Of(identifier["name:".Length..]);
        }

        // **基线对非前缀标识符走 `defaultSymbolOf(identifier)`，即"按名字建一个普通符号"。**
        // **此前这里直接抛异常，而多项式解析出来的记号全是这种标识符。**
        // **缺的不是 hex/JSON 那几段（那些确实留给 a later phase），缺的是最简单的那个回退。**
        // **The code routes unprefixed identifiers to `defaultSymbolOf(identifier)`.**
        // **This used to throw, and every identifier a polynomial parse produces is one of those.**
        return NamedSymbol.Of(identifier);
    }
}

/// <summary>ISymbol 扩展：转符号身份表达式 / ISymbol extension to identity expression.</summary>
internal static class SymbolIdentityExprExtensions {
    /// <summary>将符号转为身份表达式（identity 过渡）/ Convert symbol to identity expression (transitional).</summary>
    public static ISymbol ToSymbolIdentityExpr(this ISymbol symbol) => symbol;
}
