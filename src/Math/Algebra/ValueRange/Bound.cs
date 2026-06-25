#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Algebra.ValueRange
{
    /// <summary>
    /// 边界 / Bound.
    /// 表示值范围的一个边界点，包含值包装器和区间类型。
    /// 当值为无穷大时，区间类型自动设置为开区间。
    /// Represents a boundary point of a value range, containing a value wrapper and interval type.
    /// When the value is infinity, the interval type is automatically set to Open.
    /// </summary>
    /// <typeparam name="T">数值类型 / Number type.</typeparam>
    public sealed record Bound<T> :
        IOrd<Bound<T>>,
        IPlus<Bound<T>, Bound<T>?>,
        IMinus<Bound<T>, Bound<T>?>,
        ITimes<Bound<T>, Bound<T>?>,
        IDiv<Bound<T>, Bound<T>?>
        where T : struct, IRealNumber<T>, INumberField<T>
    {
        /// <summary>边界值的包装器 / Value wrapper of the bound.</summary>
        public ValueWrapper<T> Value { get; }

        /// <summary>边界的区间类型 / Interval type of the bound.</summary>
        public Interval Interval { get; }

        public Bound(ValueWrapper<T> value, Interval interval)
        {
            Value = value;
            // 无穷值强制开区间 / Infinity forces Open
            Interval = value.IsInfinityOrNegativeInfinity ? new Interval.Open() : interval;
        }

        /// <summary>复制边界 / Copies the bound.</summary>
        public Bound<T> Copy() => new(Value.Copy(), Interval);

        /// <summary>判断边界值是否等于指定数值（仅在闭区间时有效）/ Determines if bound value equals specified number (only valid for closed interval).</summary>
        public bool Eq(T rhs) => Value.EqScalar(rhs) && Interval is Interval.Closed;

        /// <summary>部分相等比较 / Partial equality comparison.</summary>
        public bool? PartialEq(Bound<T> rhs) => Value.PartialEq(rhs.Value) is bool b
            ? b && Interval == rhs.Interval
            : Value.PartialEq(rhs.Value);

        /// <summary>部分序比较 / Partial order comparison.</summary>
        public Order? PartialOrd(Bound<T> rhs) => Value.PartialOrd(rhs.Value) switch
        {
            Order.Equal => Interval.Outer(rhs.Interval) ? new Order.Less() : new Order.Greater(),
            var o => o,
        };

        /// <summary>全序比较 / Total order comparison.</summary>
        public Order Ord(Bound<T> rhs) => PartialOrd(rhs) ?? new Order.Less();

        /// <summary>部分序比较（显式接口实现）/ Partial order comparison (explicit interface).</summary>
        Order? IPartialOrd<Bound<T>>.PartialOrd(Bound<T> rhs) => PartialOrd(rhs);

        /// <summary>相等比较 / Equality comparison.</summary>
        public bool Eq(Bound<T> rhs) => PartialEq(rhs) == true;

        /// <summary>不等比较 / Inequality comparison.</summary>
        public bool Neq(Bound<T> rhs) => !Eq(rhs);

        /// <summary>比较到（用于 IComparable）/ Compare to (for IComparable).</summary>
        public int CompareTo(Bound<T>? other) => other is null ? 1 : Ord(other).Value;

        /// <summary>小于 / Less than.</summary>
        public bool Ls(Bound<T> rhs) => CompareTo(rhs) < 0;
        /// <summary>小于等于 / Less than or equal.</summary>
        public bool Leq(Bound<T> rhs) => CompareTo(rhs) <= 0;
        /// <summary>大于 / Greater than.</summary>
        public bool Gr(Bound<T> rhs) => CompareTo(rhs) > 0;
        /// <summary>大于等于 / Greater than or equal.</summary>
        public bool Geq(Bound<T> rhs) => CompareTo(rhs) >= 0;

        // 算术：与数值 / Arithmetic with scalar
        /// <summary>边界与数值相加 / Adds a number to the bound.</summary>
        public Bound<T>? Plus(T rhs) =>
            Value.Plus(rhs) is { } v ? new Bound<T>(v, Interval) : null;

        /// <summary>两个边界相加 / Adds two bounds.</summary>
        public Bound<T>? Plus(Bound<T> rhs) =>
            Value.Plus(rhs.Value) is { } v ? new Bound<T>(v, Interval.Intersect(rhs.Interval)) : null;

        /// <summary>边界与数值相减 / Subtracts a number from the bound.</summary>
        public Bound<T>? Minus(T rhs) =>
            Value.Minus(rhs) is { } v ? new Bound<T>(v, Interval) : null;

        /// <summary>两个边界相减 / Subtracts two bounds.</summary>
        public Bound<T>? Minus(Bound<T> rhs) =>
            Value.Minus(rhs.Value) is { } v ? new Bound<T>(v, Interval.Intersect(rhs.Interval)) : null;

        /// <summary>边界与数值相乘 / Multiplies bound by a number.</summary>
        public Bound<T>? Times(T rhs) =>
            Value.Times(rhs) is { } v ? new Bound<T>(v, Interval) : null;

        /// <summary>两个边界相乘 / Multiplies two bounds.</summary>
        public Bound<T>? Times(Bound<T> rhs) =>
            Value.Times(rhs.Value) is { } v ? new Bound<T>(v, Interval.Intersect(rhs.Interval)) : null;

        /// <summary>边界与数值相除 / Divides bound by a number.</summary>
        public Bound<T>? Div(T rhs) =>
            Value.Div(rhs) is { } v ? new Bound<T>(v, Interval) : null;

        /// <summary>两个边界相除 / Divides two bounds.</summary>
        public Bound<T>? Div(Bound<T> rhs) =>
            Value.Div(rhs.Value) is { } v ? new Bound<T>(v, Interval.Intersect(rhs.Interval)) : null;

        // 取负：单一泛型算子 / Unary minus (generic)
        public static Bound<T> operator -(Bound<T> b) => new(-b.Value, b.Interval);

        /// <summary>转换为 Flt64 类型的边界 / Converts to Flt64 typed bound.</summary>
        public Bound<Flt64> ToFlt64() => new(new ValueWrapper<Flt64>.Value(Value.ToFlt64()), Interval);

        /// <summary>获取字符串表示 / Gets string representation.</summary>
        public override string ToString() => $"Bound({Value}, {Interval})";
    }
}
