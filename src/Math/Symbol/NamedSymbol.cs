#nullable enable

using System;

namespace Fuookami.Ospf.Math.Symbol;

/// <summary>
/// 按名字标识的符号 / A symbol identified by its name.
/// </summary>
/// <param name="Name">名字 / The name.</param>
/// <param name="DisplayName">显示名 / The display name.</param>
/// <remarks>
/// **`DefaultSymbol(name, displayName?)`，而 C# 此前没有任何"按名字标识"的符号实现**——
/// 只有 `PathSymbol`。**于是 `SymbolOfSerializedIdentifier` 对任何不带 `path:` 前缀的标识符直接抛异常**，
/// 而多项式解析出来的记号恰好全是这种标识符（`x`、`y`、`z_1`）。
///
/// **名字与显示名分开，是因为后者可以为空**：`x` 的显示名不写时就是它自己，
/// 而**写成 `new NamedSymbol("x", "x")` 与写成 `new NamedSymbol("x")` 在序列化上不是一回事**——
/// 前者把显示名写实了，后者留在"没指定"。
///
/// **This implementation has `DefaultSymbol(name, displayName?)`, and C# had no name-identified symbol at all** — only `PathSymbol`.
/// **So `SymbolOfSerializedIdentifier` threw for every identifier without a `path:` prefix**, and every identifier a polynomial parse produces is exactly that (`x`, `y`, `z_1`).
/// **Name and display name are separate because the latter may be absent**: an `x` with no display name is just itself, and **`new NamedSymbol("x", "x")` and `new NamedSymbol("x")` are not the same thing once serialized**
/// — the first pins the display name, the second leaves it unspecified.
/// </remarks>
public sealed record NamedSymbol(string Name, string? DisplayName = null) : ISymbol, IIdentifiedSymbol
{
    /// <inheritdoc/>
    public string SymbolId => "name:" + Name;

    /// <inheritdoc/>
    public override string ToString() => DisplayName is null
        ? "NamedSymbol(" + Name + ")"
        : "NamedSymbol(" + Name + ", " + DisplayName + ")";

    /// <summary>按名字构造 / Build from a name.</summary>
    /// <param name="name">名字 / The name.</param>
    /// <returns>符号 / The symbol.</returns>
    /// <exception cref="ArgumentException">名字为空 / The name is empty.</exception>
    public static NamedSymbol Of(string name) => string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("符号名不能为空 / a symbol name cannot be empty", nameof(name))
        : new NamedSymbol(name);
}
