#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Fuookami.Ospf.Math.Symbol;
/// <summary>
/// 符号标识 / Symbol Identifier (Kotlin @JvmInline value class SymbolId).
/// </summary>
public readonly record struct SymbolId(string Value) {
    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>稳定符号接口 / Stable Symbol (provides a stable id).</summary>
public interface IStableSymbol : ISymbol {
    /// <summary>稳定符号标识 / Stable symbol identifier.</summary>
    SymbolId StableSymbolId { get; }
}

/// <summary>可标识符号接口 / Identifiable Symbol (string id -> stable id).</summary>
public interface IIdentifiedSymbol : IStableSymbol {
    /// <summary>字符串符号标识 / String symbol identifier.</summary>
    string SymbolId { get; }

    /// <inheritdoc/>
    SymbolId IStableSymbol.StableSymbolId => new(SymbolId);
}

/// <summary>拥有者符号基类接口 / Owned-symbol-like (id is the stable id).</summary>
public interface IOwnedSymbolLike : IStableSymbol {
    /// <summary>符号标识 / Symbol identifier.</summary>
    SymbolId Id { get; }

    /// <inheritdoc/>
    SymbolId IStableSymbol.StableSymbolId => Id;
}

/// <summary>
/// 拥有者符号 / Owned Symbol (Kotlin data class OwnedSymbol).
/// </summary>
public sealed record OwnedSymbol(
    SymbolId Id,
    string Name,
    string? DisplayName = null
) : IOwnedSymbolLike {
    /// <summary>从现有符号创建拥有者符号 / Creates an owned symbol from an existing symbol.</summary>
    public OwnedSymbol(ISymbol symbol, SymbolId? id = null)
        : this(id ?? SymbolIdentity.StableId(symbol), symbol.Name, symbol.DisplayName) { }
}

/// <summary>
/// 符号身份工具 / Symbol identity helpers (Kotlin free functions + comparators).
/// </summary>
public static class SymbolIdentity {
    /// <summary>尝试获取稳定标识，无则返回 null / Returns stable id if available, else null.</summary>
    public static SymbolId? StableIdOrNull(ISymbol symbol)
        => (symbol as IStableSymbol)?.StableSymbolId;

    /// <summary>是否有稳定标识 / Whether the symbol has a stable id.</summary>
    public static bool HasStableId(ISymbol symbol) => StableIdOrNull(symbol) is not null;

    /// <summary>获取稳定标识结果 / Ret&lt;SymbolId&gt; (Kotlin requireStableId).</summary>
    public static Result<SymbolId, ErrorCode, Error<ErrorCode>> RequireStableId(ISymbol symbol)
        => StableIdOrNull(symbol) is { } id
            ? new Ok<SymbolId, ErrorCode, Error<ErrorCode>>(id)
            : new Failed<SymbolId, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationError, $"Symbol {symbol.Name} has no explicit stable identity.");

    /// <summary>获取或生成稳定标识 / Gets or generates a stable id (name + RuntimeHelpers.GetHashCode fallback).</summary>
    public static SymbolId StableId(ISymbol symbol)
        => StableIdOrNull(symbol) ?? new SymbolId($"{symbol.Name}#{RuntimeHelpers.GetHashCode(symbol)}");

    /// <summary>创建拥有者符号 / Creates an owned symbol.</summary>
    public static OwnedSymbol Owned(ISymbol symbol, SymbolId? id = null)
        => new(symbol, id ?? StableId(symbol));

    /// <summary>获取符号身份字符串 / Gets the identity string of a symbol.</summary>
    public static string Identity(ISymbol symbol) => StableId(symbol).Value;

    /// <summary>默认符号比较器 / Default comparator (name then identity).</summary>
    public static readonly IComparer<ISymbol> DefaultSymbolComparator =
        Comparer<ISymbol>.Create((a, b) => {
            int byName = string.CompareOrdinal(a.Name, b.Name);
            return byName != 0 ? byName : string.CompareOrdinal(Identity(a), Identity(b));
        });

    /// <summary>默认稳定符号比较器 / Default stable-symbol comparator (name then stable id).</summary>
    public static readonly IComparer<ISymbol> DefaultStableSymbolComparator =
        Comparer<ISymbol>.Create((a, b) => {
            int byName = string.CompareOrdinal(a.Name, b.Name);
            if (byName != 0) {
                return byName;
            }

            SymbolId lhs = StableIdOrNull(a) ?? StableId(a);
            SymbolId rhs = StableIdOrNull(b) ?? StableId(b);
            return string.CompareOrdinal(lhs.Value, rhs.Value);
        });
}
