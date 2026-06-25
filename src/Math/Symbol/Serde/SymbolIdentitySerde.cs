#nullable enable

using Fuookami.Ospf.Math.Symbol.Expression;
using System;

namespace Fuookami.Ospf.Math.Symbol.Serde;
/// <summary>
/// 符号身份序列化工具（前向声明，phase 13 完整移植）/ Symbol identity serde helpers (forward-declared; phase 13 owns full module).
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
        if (identifier.StartsWith("path:")) {
            return PathSymbol.From(identifier["path:".Length..]);
        }

        throw new NotSupportedException("Full symbol deserialization requires phase 13.");
    }
}

/// <summary>ISymbol 扩展：转符号身份表达式 / ISymbol extension to identity expression.</summary>
internal static class SymbolIdentityExprExtensions {
    /// <summary>将符号转为身份表达式（identity 过渡）/ Convert symbol to identity expression (transitional).</summary>
    public static ISymbol ToSymbolIdentityExpr(this ISymbol symbol) => symbol;
}
