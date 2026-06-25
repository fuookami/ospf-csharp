#nullable enable

using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Math;
/// <summary>
/// 比较运算符：相等 / Comparison operator: Equal
/// </summary>
public sealed record ComparisonEqual<T>
    where T : struct, IMinus<T, T>, IAbs<T>, IOrd<T> {
    public readonly T Precision;
    public readonly bool HasPrecision;

    public ComparisonEqual() { HasPrecision = false; Precision = default; }
    public ComparisonEqual(T precision) { Precision = precision; HasPrecision = true; }

    /// <summary>创建实例 / Create instance</summary>
    public static ComparisonEqual<T> Invoke(T precision) => new(precision);

    /// <summary>比较两个值 / Compare two values</summary>
    public bool this[T lhs, T rhs] {
        get {
            if (!HasPrecision) {
                return lhs.Eq(rhs);
            }
            return lhs.Minus(rhs).Abs().Leq(Precision);
        }
    }
}

/// <summary>
/// 比较运算符：不等 / Comparison operator: Unequal
/// </summary>
public sealed record ComparisonUnequal<T>
    where T : struct, IMinus<T, T>, IAbs<T>, IOrd<T> {
    public readonly T Precision;
    public readonly bool HasPrecision;

    public ComparisonUnequal() { HasPrecision = false; Precision = default; }
    public ComparisonUnequal(T precision) { Precision = precision; HasPrecision = true; }

    /// <summary>创建实例 / Create instance</summary>
    public static ComparisonUnequal<T> Invoke(T precision) => new(precision);

    /// <summary>比较两个值 / Compare two values</summary>
    public bool this[T lhs, T rhs] {
        get {
            if (!HasPrecision) {
                return lhs.Neq(rhs);
            }
            return lhs.Minus(rhs).Abs().Geq(Precision);
        }
    }
}

/// <summary>
/// 比较运算符：小于 / Comparison operator: Less
/// </summary>
public sealed record ComparisonLess<T>
    where T : struct, IMinus<T, T>, IAbs<T>, IOrd<T>, INeg<T> {
    public readonly T Precision;
    public readonly bool HasPrecision;

    public ComparisonLess() { HasPrecision = false; Precision = default; }
    public ComparisonLess(T precision) { Precision = precision; HasPrecision = true; }

    /// <summary>创建实例 / Create instance</summary>
    public static ComparisonLess<T> Invoke(T precision) => new(precision);

    /// <summary>比较两个值 / Compare two values</summary>
    public bool this[T lhs, T rhs] {
        get {
            if (!HasPrecision) {
                return lhs.Ls(rhs);
            }
            return lhs.Minus(rhs).Leq(Precision.Negate());
        }
    }
}

/// <summary>
/// 比较运算符：小于等于 / Comparison operator: LessEqual
/// </summary>
public sealed record ComparisonLessEqual<T>
    where T : struct, IMinus<T, T>, IAbs<T>, IOrd<T> {
    public readonly T Precision;
    public readonly bool HasPrecision;

    public ComparisonLessEqual() { HasPrecision = false; Precision = default; }
    public ComparisonLessEqual(T precision) { Precision = precision; HasPrecision = true; }

    /// <summary>创建实例 / Create instance</summary>
    public static ComparisonLessEqual<T> Invoke(T precision) => new(precision);

    /// <summary>比较两个值 / Compare two values</summary>
    public bool this[T lhs, T rhs] {
        get {
            if (!HasPrecision) {
                return lhs.Leq(rhs);
            }
            return lhs.Minus(rhs).Abs().Leq(Precision);
        }
    }
}

/// <summary>
/// 比较运算符：大于 / Comparison operator: Greater
/// </summary>
public sealed record ComparisonGreater<T>
    where T : struct, IMinus<T, T>, IAbs<T>, IOrd<T> {
    public readonly T Precision;
    public readonly bool HasPrecision;

    public ComparisonGreater() { HasPrecision = false; Precision = default; }
    public ComparisonGreater(T precision) { Precision = precision; HasPrecision = true; }

    /// <summary>创建实例 / Create instance</summary>
    public static ComparisonGreater<T> Invoke(T precision) => new(precision);

    /// <summary>比较两个值 / Compare two values</summary>
    public bool this[T lhs, T rhs] {
        get {
            if (!HasPrecision) {
                return lhs.Gr(rhs);
            }
            return lhs.Minus(rhs).Abs().Geq(Precision);
        }
    }
}

/// <summary>
/// 比较运算符：大于等于 / Comparison operator: GreaterEqual
/// </summary>
public sealed record ComparisonGreaterEqual<T>
    where T : struct, IMinus<T, T>, IAbs<T>, IOrd<T>, INeg<T> {
    public readonly T Precision;
    public readonly bool HasPrecision;

    public ComparisonGreaterEqual() { HasPrecision = false; Precision = default; }
    public ComparisonGreaterEqual(T precision) { Precision = precision; HasPrecision = true; }

    /// <summary>创建实例 / Create instance</summary>
    public static ComparisonGreaterEqual<T> Invoke(T precision) => new(precision);

    /// <summary>比较两个值 / Compare two values</summary>
    public bool this[T lhs, T rhs] {
        get {
            if (!HasPrecision) {
                return lhs.Geq(rhs);
            }
            return lhs.Minus(rhs).Geq(Precision.Negate());
        }
    }
}

/// <summary>
/// 组合比较运算符 / Combined comparison operator
/// </summary>
public sealed record ComparisonOperator<T>
    where T : struct, IMinus<T, T>, IAbs<T>, IOrd<T>, INeg<T> {
    public readonly T Precision;
    public readonly bool HasPrecision;

    public ComparisonOperator() { HasPrecision = false; Precision = default; }
    public ComparisonOperator(T precision) { Precision = precision; HasPrecision = true; }

    /// <summary>相等 / Equal</summary>
    public bool Eq(T lhs, T rhs) => HasPrecision ? new ComparisonEqual<T>(Precision)[lhs, rhs] : new ComparisonEqual<T>()[lhs, rhs];
    /// <summary>不等 / Not equal</summary>
    public bool Neq(T lhs, T rhs) => HasPrecision ? new ComparisonUnequal<T>(Precision)[lhs, rhs] : new ComparisonUnequal<T>()[lhs, rhs];
    /// <summary>小于 / Less than</summary>
    public bool Ls(T lhs, T rhs) => HasPrecision ? new ComparisonLess<T>(Precision)[lhs, rhs] : new ComparisonLess<T>()[lhs, rhs];
    /// <summary>小于等于 / Less than or equal</summary>
    public bool Leq(T lhs, T rhs) => HasPrecision ? new ComparisonLessEqual<T>(Precision)[lhs, rhs] : new ComparisonLessEqual<T>()[lhs, rhs];
    /// <summary>大于 / Greater than</summary>
    public bool Gr(T lhs, T rhs) => HasPrecision ? new ComparisonGreater<T>(Precision)[lhs, rhs] : new ComparisonGreater<T>()[lhs, rhs];
    /// <summary>大于等于 / Greater than or equal</summary>
    public bool Geq(T lhs, T rhs) => HasPrecision ? new ComparisonGreaterEqual<T>(Precision)[lhs, rhs] : new ComparisonGreaterEqual<T>()[lhs, rhs];
}
