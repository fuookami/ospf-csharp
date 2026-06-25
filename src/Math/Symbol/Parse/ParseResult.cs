#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Symbol.Parse;
/// <summary>
/// 解析结果类型别名 / Parse result type alias.
/// Kotlin: typealias ParseResult&lt;T&gt; = Ret&lt;T&gt;
/// </summary>
public static class ParseResult {
    /// <summary>创建成功解析结果 / Create successful parse result.</summary>
    public static Result<T, ErrorCode, Error<ErrorCode>> Ok<T>(T value)
        => new Ok<T, ErrorCode, Error<ErrorCode>>(value);

    /// <summary>创建失败解析结果 / Create failed parse result.</summary>
    public static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(ErrorCode code, string? message = null)
        => new Failed<T, ErrorCode, Error<ErrorCode>>(code, message);
}

/// <summary>
/// 数值解析器接口 / Number parser interface.
/// Kotlin fun interface NumberParser&lt;T&gt;.
/// </summary>
/// <typeparam name="T">解析目标类型 / The target type to parse into.</typeparam>
public interface INumberParser<T> where T : struct {
    /// <summary>
    /// 将字符串解析为数值 / Parse a string into a numeric value.
    /// </summary>
    /// <param name="text">待解析的字符串 / The string to parse.</param>
    /// <returns>解析结果，失败返回 null / The parsed value, or null if parsing fails.</returns>
    T? Parse(string text);
}

/// <summary>
/// Int64 数值解析器 / Int64 number parser.
/// </summary>
public sealed class Int64NumberParser : INumberParser<Fuookami.Ospf.Math.Algebra.Number.Int64> {
    /// <summary>单例实例 / Singleton instance.</summary>
    public static readonly Int64NumberParser Instance = new();

    private Int64NumberParser() { }

    /// <inheritdoc/>
    public Fuookami.Ospf.Math.Algebra.Number.Int64? Parse(string text) {
        if (long.TryParse(text, out long value)) {
            return new Fuookami.Ospf.Math.Algebra.Number.Int64(value);
        }

        return null;
    }
}
