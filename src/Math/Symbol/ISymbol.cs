#nullable enable

namespace Fuookami.Ospf.Math.Symbol
{
    /// <summary>
    /// 符号接口 / Symbol Interface.
    /// 表示符号表达式中的变量符号 / Represents a variable symbol in symbolic expressions.
    /// </summary>
    public interface ISymbol
    {
        /// <summary>符号的唯一标识名称 / Unique identifier name of the symbol.</summary>
        string Name { get; }

        /// <summary>显示名称（可选）/ Display name (optional).</summary>
        string? DisplayName { get; }
    }
}
