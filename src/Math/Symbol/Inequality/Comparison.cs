#nullable enable

using System;

namespace Fuookami.Ospf.Math.Symbol.Inequality;
/// <summary>
/// 比较运算符枚举 / Comparison Operator Enumeration.
/// </summary>
public enum Comparison {
    /// <summary>小于 / Less than.</summary>
    LT,
    /// <summary>小于等于 / Less than or equal.</summary>
    LE,
    /// <summary>等于 / Equal.</summary>
    EQ,
    /// <summary>不等于 / Not equal.</summary>
    NE,
    /// <summary>大于等于 / Greater than or equal.</summary>
    GE,
    /// <summary>大于 / Greater than.</summary>
    GT,
}

/// <summary>
/// 比较运算扩展 / Comparison behavior (Kotlin enum computed properties + reverse()).
/// </summary>
public static class ComparisonExtensions {
    /// <summary>获取比较符号 / Gets the comparison symbol string.</summary>
    public static string Symbol(this Comparison c) => c switch {
        Comparison.LT => "<",
        Comparison.LE => "<=",
        Comparison.EQ => "=",
        Comparison.NE => "!=",
        Comparison.GE => ">=",
        Comparison.GT => ">",
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    /// <summary>是否为严格比较 / Whether the comparison is strict (no equality).</summary>
    public static bool IsStrict(this Comparison c) => c is Comparison.LT or Comparison.GT or Comparison.NE;

    /// <summary>是否包含相等 / Whether the comparison includes equality.</summary>
    public static bool IncludesEquality(this Comparison c) => c is Comparison.LE or Comparison.EQ or Comparison.GE;

    /// <summary>是否为小于类型 / Whether the comparison is less-like.</summary>
    public static bool IsLessLike(this Comparison c) => c is Comparison.LT or Comparison.LE;

    /// <summary>是否为大于类型 / Whether the comparison is greater-like.</summary>
    public static bool IsGreaterLike(this Comparison c) => c is Comparison.GT or Comparison.GE;

    /// <summary>反转比较运算符 / Reverses the comparison operator.</summary>
    public static Comparison Reverse(this Comparison c) => c switch {
        Comparison.LT => Comparison.GT,
        Comparison.LE => Comparison.GE,
        Comparison.EQ => Comparison.EQ,
        Comparison.NE => Comparison.NE,
        Comparison.GE => Comparison.LE,
        Comparison.GT => Comparison.LT,
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };
}
