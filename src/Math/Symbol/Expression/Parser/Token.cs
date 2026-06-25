#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Expression.Parser
{
    /// <summary>词法单元类型 / Token Type.</summary>
    public enum TokenType
    {
        // Literals
        True, False, Null, String, Number, Identifier,
        // Keywords
        And, Or, Not, In, Is, Like, Contains, Prefix, Suffix, Regex, Exact,
        // Comparison operators
        Eq, Ne, Lt, Le, Gt, Ge,
        // Other symbols
        LParen, RParen, Comma,
        // Special
        Eof, Unknown,
    }

    /// <summary>词法单元 / Token. type/value/position.</summary>
    public sealed record Token(TokenType Type, string Value, int Position = 0)
    {
        /// <inheritdoc/>
        public override string ToString() => Type switch
        {
            TokenType.Eof => "EOF",
            TokenType.Unknown => $"UNKNOWN({Value})",
            _ => $"{Type}({Value})",
        };

        /// <summary>创建 EOF 词法单元 / Create EOF token.</summary>
        public static Token Eof(int position = 0) => new(TokenType.Eof, "", position);
        /// <summary>创建未知词法单元 / Create unknown token.</summary>
        public static Token Unknown(string value, int position = 0) => new(TokenType.Unknown, value, position);
    }

    /// <summary>词法单元扩展 / Token extensions.</summary>
    public static class TokenExtensions
    {
        private static readonly HashSet<TokenType> ComparisonOps = new()
        {
            TokenType.Eq, TokenType.Ne, TokenType.Lt, TokenType.Le, TokenType.Gt, TokenType.Ge
        };
        private static readonly HashSet<TokenType> PatternOps = new()
        {
            TokenType.Like, TokenType.Contains, TokenType.Prefix, TokenType.Suffix, TokenType.Regex, TokenType.Exact
        };

        /// <summary>判断是否是比较操作符 / Check if token is a comparison operator.</summary>
        public static bool IsComparisonOperator(this Token token) => ComparisonOps.Contains(token.Type);
        /// <summary>判断是否是模式匹配操作符 / Check if token is a pattern match operator.</summary>
        public static bool IsPatternOperator(this Token token) => PatternOps.Contains(token.Type);

        /// <summary>转换为比较操作符 / Convert to comparison operator, null if not supported.</summary>
        public static ComparisonOperator? ToComparisonOperator(this TokenType type) => type switch
        {
            TokenType.Eq => ComparisonOperator.Eq,
            TokenType.Ne => ComparisonOperator.Ne,
            TokenType.Lt => ComparisonOperator.Lt,
            TokenType.Le => ComparisonOperator.Le,
            TokenType.Gt => ComparisonOperator.Gt,
            TokenType.Ge => ComparisonOperator.Ge,
            _ => null,
        };

        /// <summary>转换为模式匹配模式 / Convert to pattern match mode, null if not supported.</summary>
        public static PatternMatchMode? ToPatternMatchMode(this TokenType type) => type switch
        {
            TokenType.Like => PatternMatchMode.Like,
            TokenType.Contains => PatternMatchMode.Contains,
            TokenType.Prefix => PatternMatchMode.Prefix,
            TokenType.Suffix => PatternMatchMode.Suffix,
            TokenType.Regex => PatternMatchMode.Regex,
            TokenType.Exact => PatternMatchMode.Exact,
            _ => null,
        };
    }
}
