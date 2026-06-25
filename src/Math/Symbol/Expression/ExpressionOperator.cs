#nullable enable

using System;

namespace Fuookami.Ospf.Math.Symbol.Expression;
/// <summary>
/// 标量一元操作符 / Scalar unary operator.
/// </summary>
public enum UnaryOperator {
    /// <summary>负号 / Negation</summary>
    Negate,
    /// <summary>正号 / Positive (identity)</summary>
    Positive,
    /// <summary>绝对值 / Absolute value</summary>
    Abs
}

/// <summary>
/// 标量二元操作符 / Scalar binary operator.
/// </summary>
public enum BinaryOperator {
    /// <summary>加法 / Addition</summary>
    Add,
    /// <summary>减法 / Subtraction</summary>
    Subtract,
    /// <summary>乘法 / Multiplication</summary>
    Multiply,
    /// <summary>除法 / Division</summary>
    Divide,
    /// <summary>取模 / Modulo</summary>
    Modulo,
    /// <summary>幂运算 / Power</summary>
    Power
}

/// <summary>
/// 比较操作符 / Comparison operator.
/// </summary>
public enum ComparisonOperator {
    /// <summary>等于 / Equal</summary>
    Eq,
    /// <summary>不等于 / Not Equal</summary>
    Ne,
    /// <summary>小于 / Less Than</summary>
    Lt,
    /// <summary>小于等于 / Less Than or Equal</summary>
    Le,
    /// <summary>大于 / Greater Than</summary>
    Gt,
    /// <summary>大于等于 / Greater Than or Equal</summary>
    Ge
}

/// <summary>
/// 模式匹配模式 / Pattern match mode (SQL-dialect-agnostic).
/// </summary>
public enum PatternMatchMode {
    /// <summary>精确匹配 / Exact match</summary>
    Exact,
    /// <summary>前缀匹配 / Prefix match</summary>
    Prefix,
    /// <summary>后缀匹配 / Suffix match</summary>
    Suffix,
    /// <summary>包含匹配 / Contains match</summary>
    Contains,
    /// <summary>通配符匹配 / Wildcard match (SQL LIKE)</summary>
    Like,
    /// <summary>正则匹配 / Regex match</summary>
    Regex
}

/// <summary>
/// 布尔操作符 / Boolean operator.
/// </summary>
public enum BooleanOperator {
    /// <summary>逻辑与 / Logical AND</summary>
    And,
    /// <summary>逻辑或 / Logical OR</summary>
    Or,
    /// <summary>逻辑非 / Logical NOT</summary>
    Not
}

/// <summary>
/// 空值检查类型 / Null check type.
/// </summary>
public enum NullCheckType {
    /// <summary>是空值 / Is Null</summary>
    IsNull,
    /// <summary>非空值 / Is Not Null</summary>
    IsNotNull
}

/// <summary>
/// 操作符符号映射 / Operator symbol mapping.
/// Kotlin object OperatorSymbols; C# static class (no state).
/// </summary>
public static class OperatorSymbols {
    /// <summary>获取一元操作符的符号 / Get symbol for unary operator.</summary>
    public static string Unary(UnaryOperator op) => op switch {
        UnaryOperator.Negate => "-",
        UnaryOperator.Positive => "+",
        UnaryOperator.Abs => "abs",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>获取二元操作符的符号 / Get symbol for binary operator.</summary>
    public static string Binary(BinaryOperator op) => op switch {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Power => "^",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>获取比较操作符的符号 / Get symbol for comparison operator.</summary>
    public static string Comparison(ComparisonOperator op) => op switch {
        ComparisonOperator.Eq => "=",
        ComparisonOperator.Ne => "<>",
        ComparisonOperator.Lt => "<",
        ComparisonOperator.Le => "<=",
        ComparisonOperator.Gt => ">",
        ComparisonOperator.Ge => ">=",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>获取布尔操作符的符号 / Get symbol for boolean operator.</summary>
    public static string Boolean(BooleanOperator op) => op switch {
        BooleanOperator.And => "and",
        BooleanOperator.Or => "or",
        BooleanOperator.Not => "not",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>获取空值检查类型的符号 / Get symbol for null check type.</summary>
    public static string NullCheck(NullCheckType type) => type switch {
        NullCheckType.IsNull => "is null",
        NullCheckType.IsNotNull => "is not null",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

/// <summary>
/// 比较操作符扩展 / Comparison operator extensions.
/// Kotlin ComparisonOperator.inverse() (C# enums carry no instance methods).
/// </summary>
public static class ComparisonOperatorExtensions {
    /// <summary>返回比较操作符的反向操作符 / Return the inverse comparison operator.</summary>
    public static ComparisonOperator Inverse(this ComparisonOperator op) => op switch {
        ComparisonOperator.Eq => ComparisonOperator.Ne,
        ComparisonOperator.Ne => ComparisonOperator.Eq,
        ComparisonOperator.Lt => ComparisonOperator.Gt,
        ComparisonOperator.Le => ComparisonOperator.Ge,
        ComparisonOperator.Gt => ComparisonOperator.Lt,
        ComparisonOperator.Ge => ComparisonOperator.Le,
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };
}
