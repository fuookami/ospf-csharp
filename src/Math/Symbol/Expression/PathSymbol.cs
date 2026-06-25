#nullable enable

using Fuookami.Ospf.Math.Symbol;

namespace Fuookami.Ospf.Math.Symbol.Expression
{
    /// <summary>
    /// 路径符号 / Path Symbol.
    /// Wraps a PropertyPath as a symbol for referencing fields/properties.
    /// Kotlin data class PathSymbol : Symbol, IdentifiedSymbol; C# sealed record.
    /// </summary>
    public sealed record PathSymbol(PropertyPath Path) : ISymbol, IIdentifiedSymbol
    {
        /// <summary>符号名称，等于路径值 / Symbol name, equals path value.</summary>
        public string Name => Path.Value;

        /// <summary>显示名称，默认为路径值 / Display name, defaults to path value.</summary>
        public string? DisplayName => Path.Value;

        /// <summary>符号唯一标识，格式为 path:${value} / Unique id, format path:${value}.</summary>
        public string SymbolId => $"path:{Path.Value}";

        /// <inheritdoc/>
        public override string ToString() => $"PathSymbol({Path})";

        /// <summary>从属性路径创建路径符号 / Create path symbol from property path.</summary>
        public static PathSymbol From(PropertyPath path) => new(path);

        /// <summary>从字符串创建路径符号 / Create path symbol from string.</summary>
        public static PathSymbol From(string path) => new(PropertyPath.Parse(path));

        /// <summary>从分段创建路径符号 / Create path symbol from segments.</summary>
        public static PathSymbol Of(params string[] segments) => new(PropertyPath.Of(segments));
    }

    /// <summary>符号与路径符号扩展 / Symbol &lt;-&gt; PathSymbol extensions.</summary>
    public static class PathSymbolExtensions
    {
        /// <summary>属性路径转路径符号 / PropertyPath to PathSymbol.</summary>
        public static PathSymbol ToPathSymbol(this PropertyPath path) => PathSymbol.From(path);

        /// <summary>字符串转路径符号 / String to PathSymbol.</summary>
        public static PathSymbol ToPathSymbol(this string path) => PathSymbol.From(path);

        /// <summary>符号尝试转属性路径 / Symbol to PropertyPathOrNull (PathSymbol only).</summary>
        public static PropertyPath? ToPropertyPathOrNull(this ISymbol symbol) =>
            symbol is PathSymbol ps ? ps.Path : null;

        /// <summary>符号是否是路径符号 / Whether symbol is a PathSymbol.</summary>
        public static bool IsPathSymbol(this ISymbol symbol) => symbol is PathSymbol;

        /// <summary>识别符号尝试从 symbolId 解析属性路径 / IdentifiedSymbol to PropertyPathOrNull via path: id.</summary>
        public static PropertyPath? ToPropertyPathFromIdOrNull(this IIdentifiedSymbol symbol)
        {
            var id = symbol.SymbolId;
            if (!id.StartsWith("path:")) return null;
            return PropertyPath.ParseOrNull(id["path:".Length..]);
        }
    }
}
