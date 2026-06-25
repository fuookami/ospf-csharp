#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Numerics;

namespace Fuookami.Ospf.Math.Algebra.Number;
// ===== Integer constants =====

internal sealed class Int8Constants : IRealNumberConstants<Int8>, INumericConstants<Int8> {
    public static readonly Int8Constants Instance = new();
    public Int8 Zero => new(0);
    public Int8 One => new(1);
    public Int8 Two => new(2);
    public Int8 Three => new(3);
    public Int8 Five => new(5);
    public Int8 Ten => new(10);
    public Int8 Minimum => new(sbyte.MinValue);
    public Int8 Maximum => new(sbyte.MaxValue);
    public Int8 PositiveMinimum => One;
    public int? DecimalDigits => null;
    public Int8 DecimalPrecision => Zero;
    public Int8 Epsilon => Zero;
    Int8? INumericConstants<Int8>.Half => null;
    Int8? INumericConstants<Int8>.PositiveInfinity => null;
    Int8? INumericConstants<Int8>.NegativeInfinity => null;
    Int8? INumericConstants<Int8>.NaN => null;
    Int8? INumericConstants<Int8>.Epsilon => Zero;
    Int8? INumericConstants<Int8>.DecimalPrecision => Zero;
    Int8? INumericConstants<Int8>.Pi => null;
    Int8? INumericConstants<Int8>.E => null;
    Int8? INumericConstants<Int8>.Lg2 => null;
    public bool IsNaN(Int8 v) => false;
}

internal sealed class Int16Constants : IRealNumberConstants<Int16>, INumericConstants<Int16> {
    public static readonly Int16Constants Instance = new();
    public Int16 Zero => new(0);
    public Int16 One => new(1);
    public Int16 Two => new(2);
    public Int16 Three => new(3);
    public Int16 Five => new(5);
    public Int16 Ten => new(10);
    public Int16 Minimum => new(short.MinValue);
    public Int16 Maximum => new(short.MaxValue);
    public Int16 PositiveMinimum => One;
    public int? DecimalDigits => null;
    public Int16 DecimalPrecision => Zero;
    public Int16 Epsilon => Zero;
    Int16? INumericConstants<Int16>.Half => null;
    Int16? INumericConstants<Int16>.PositiveInfinity => null;
    Int16? INumericConstants<Int16>.NegativeInfinity => null;
    Int16? INumericConstants<Int16>.NaN => null;
    Int16? INumericConstants<Int16>.Epsilon => Zero;
    Int16? INumericConstants<Int16>.DecimalPrecision => Zero;
    Int16? INumericConstants<Int16>.Pi => null;
    Int16? INumericConstants<Int16>.E => null;
    Int16? INumericConstants<Int16>.Lg2 => null;
    public bool IsNaN(Int16 v) => false;
}

internal sealed class Int32Constants : IRealNumberConstants<Int32>, INumericConstants<Int32> {
    public static readonly Int32Constants Instance = new();
    public Int32 Zero => new(0);
    public Int32 One => new(1);
    public Int32 Two => new(2);
    public Int32 Three => new(3);
    public Int32 Five => new(5);
    public Int32 Ten => new(10);
    public Int32 Minimum => new(int.MinValue);
    public Int32 Maximum => new(int.MaxValue);
    public Int32 PositiveMinimum => One;
    public int? DecimalDigits => null;
    public Int32 DecimalPrecision => Zero;
    public Int32 Epsilon => Zero;
    Int32? INumericConstants<Int32>.Half => null;
    Int32? INumericConstants<Int32>.PositiveInfinity => null;
    Int32? INumericConstants<Int32>.NegativeInfinity => null;
    Int32? INumericConstants<Int32>.NaN => null;
    Int32? INumericConstants<Int32>.Epsilon => Zero;
    Int32? INumericConstants<Int32>.DecimalPrecision => Zero;
    Int32? INumericConstants<Int32>.Pi => null;
    Int32? INumericConstants<Int32>.E => null;
    Int32? INumericConstants<Int32>.Lg2 => null;
    public bool IsNaN(Int32 v) => false;
}

internal sealed class Int64Constants : IRealNumberConstants<Int64>, INumericConstants<Int64> {
    public static readonly Int64Constants Instance = new();
    public Int64 Zero => new(0);
    public Int64 One => new(1);
    public Int64 Two => new(2);
    public Int64 Three => new(3);
    public Int64 Five => new(5);
    public Int64 Ten => new(10);
    public Int64 Minimum => new(long.MinValue);
    public Int64 Maximum => new(long.MaxValue);
    public Int64 PositiveMinimum => One;
    public int? DecimalDigits => null;
    public Int64 DecimalPrecision => Zero;
    public Int64 Epsilon => Zero;
    Int64? INumericConstants<Int64>.Half => null;
    Int64? INumericConstants<Int64>.PositiveInfinity => null;
    Int64? INumericConstants<Int64>.NegativeInfinity => null;
    Int64? INumericConstants<Int64>.NaN => null;
    Int64? INumericConstants<Int64>.Epsilon => Zero;
    Int64? INumericConstants<Int64>.DecimalPrecision => Zero;
    Int64? INumericConstants<Int64>.Pi => null;
    Int64? INumericConstants<Int64>.E => null;
    Int64? INumericConstants<Int64>.Lg2 => null;
    public bool IsNaN(Int64 v) => false;
}

internal sealed class IntXConstants : IRealNumberConstants<IntX>, INumericConstants<IntX> {
    public static readonly IntXConstants Instance = new();
    public IntX Zero => new(BigInteger.Zero);
    public IntX One => new(BigInteger.One);
    public IntX Two => new(2);
    public IntX Three => new(3);
    public IntX Five => new(5);
    public IntX Ten => new(10);
    public IntX Minimum => new(long.MinValue); // BigInteger has no MinValue
    public IntX Maximum => new(long.MaxValue);
    public IntX PositiveMinimum => One;
    public int? DecimalDigits => null;
    public IntX DecimalPrecision => Zero;
    public IntX Epsilon => Zero;
    IntX? INumericConstants<IntX>.Half => null;
    IntX? INumericConstants<IntX>.PositiveInfinity => null;
    IntX? INumericConstants<IntX>.NegativeInfinity => null;
    IntX? INumericConstants<IntX>.NaN => null;
    IntX? INumericConstants<IntX>.Epsilon => Zero;
    IntX? INumericConstants<IntX>.DecimalPrecision => Zero;
    IntX? INumericConstants<IntX>.Pi => null;
    IntX? INumericConstants<IntX>.E => null;
    IntX? INumericConstants<IntX>.Lg2 => null;
    public bool IsNaN(IntX v) => false;
}

// ===== Int8 =====

/// <summary>
/// 8位有符号整数 / 8-bit signed integer
/// </summary>
public readonly struct Int8 : IIntegerNumber<Int8>, ICopyable<Int8>, IEquatable<Int8>, IComparable<Int8>,
    IRem<Int8, Int8> {
    internal readonly sbyte Value;
    public Int8(sbyte value) { Value = value; }

    public static readonly Int8 Zero = new(0);
    public static readonly Int8 One = new(1);

    // ===== Interface properties (explicit) =====
    IArithmeticConstants<Int8> IArithmetic<Int8>.Constants => Int8Constants.Instance;
    IRealNumberConstants<Int8> IRealNumber<Int8>.Constants => Int8Constants.Instance;

    // ===== ICopyable =====
    public Int8 Copy() => new(Value);

    // ===== IInvariant =====
    Int8 IInvariant<Int8>.Value() => this;

    // ===== Arithmetic =====
    public Int8 Plus(Int8 rhs) => new((sbyte)(Value + rhs.Value));
    public Int8 Minus(Int8 rhs) => new((sbyte)(Value - rhs.Value));
    public Int8 Times(Int8 rhs) => new((sbyte)(Value * rhs.Value));
    public Int8 Div(Int8 rhs) => new((sbyte)(Value / rhs.Value));
    public Int8 Rem(Int8 rhs) => new((sbyte)(Value % rhs.Value));
    public Int8 IntDiv(Int8 rhs) => new((sbyte)(Value / rhs.Value));
    public Int8 Negate() => new((sbyte)(-Value));
    public Int8 Abs() => new((sbyte)global::System.Math.Abs(Value));
    public Int8 Increment() => new((sbyte)(Value + 1));
    public Int8 Decrement() => new((sbyte)(Value - 1));
    public Int8 Cross(Int8 rhs) => Times(rhs);

    // ===== IPow =====
    public Int8 Pow(int index) => new((sbyte)global::System.Math.Pow(Value, index));
    public Int8 Sqr() => new((sbyte)(Value * Value));
    public Int8 Cub() => new((sbyte)(Value * Value * Value));

    // ===== IReciprocal =====
    public Int8 Reciprocal() => Value == 1 ? One : Value == -1 ? new(-1) : throw new InvalidOperationException("Reciprocal of integer only defined for ±1");

    // ===== Comparison =====
    public Order Ord(Int8 rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
    public Order? PartialOrd(Int8 rhs) => Ord(rhs);
    public bool Eq(Int8 rhs) => Value == rhs.Value;
    public bool? PartialEq(Int8 rhs) => Value == rhs.Value;
    public int CompareTo(Int8 other) => Value.CompareTo(other.Value);
    public bool Ls(Int8 rhs) => Value < rhs.Value;
    public bool Leq(Int8 rhs) => Value <= rhs.Value;
    public bool Gr(Int8 rhs) => Value > rhs.Value;
    public bool Geq(Int8 rhs) => Value >= rhs.Value;
    public bool Equiv(Int8 rhs) => Value == rhs.Value;

    // ===== IBounded (explicit) =====
    bool IBounded<Int8>.IsBounded => true;
    Int8? IBounded<Int8>.MinBound => Int8Constants.Instance.Minimum;
    Int8? IBounded<Int8>.MaxBound => Int8Constants.Instance.Maximum;
    bool IBounded<Int8>.IsWithinBounds(Int8 value) => true;
    Int8 IBounded<Int8>.ClampToBounds(Int8 value) => value;

    // ===== IInfinite (explicit) =====
    bool IInfinite<Int8>.SupportsInfinity => false;
    Int8? IInfinite<Int8>.PositiveInfinity => null;
    Int8? IInfinite<Int8>.NegativeInfinityValue => null;
    bool IInfinite<Int8>.IsPositiveInfinity(Int8 value) => false;
    bool IInfinite<Int8>.IsNegativeInfinity(Int8 value) => false;

    // ===== IFixed (explicit) =====
    bool IFixed<Int8>.IsFixed => false;
    int? IFixed<Int8>.FixedDigits => null;
    Int8? IFixed<Int8>.FixedPrecision => null;

    // ===== IEpsilon (explicit) =====
    Int8? IEpsilon<Int8>.PrecisionEpsilon => null;

    // ===== IRealNumber =====
    bool IRealNumber<Int8>.IsPositiveInfinity() => false;
    bool IRealNumber<Int8>.IsNegativeInfinity() => false;
    bool IRealNumber<Int8>.IsSelfWithinBounds() => true;
    Int8 IRealNumber<Int8>.ClampSelfToBounds() => this;
    public Flt64 ToFlt64() => new(Value);
    public Flt32 ToFlt32() => new(Value);
    public FltX ToFltX() => new(Value);
    public Int64 ToInt64() => new(Value);
    public Int32 ToInt32() => new(Value);

    // ===== IRangeTo =====
    public Range RangeTo(Int8 rhs) => new(Value, rhs.Value);
    public Range Until(Int8 rhs) => new(Value, rhs.Value);

    // ===== Operators =====
    public static Int8 operator +(Int8 l, Int8 r) => new((sbyte)(l.Value + r.Value));
    public static Int8 operator -(Int8 l, Int8 r) => new((sbyte)(l.Value - r.Value));
    public static Int8 operator *(Int8 l, Int8 r) => new((sbyte)(l.Value * r.Value));
    public static Int8 operator /(Int8 l, Int8 r) => new((sbyte)(l.Value / r.Value));
    public static Int8 operator %(Int8 l, Int8 r) => new((sbyte)(l.Value % r.Value));
    public static Int8 operator -(Int8 v) => new((sbyte)(-v.Value));
    public static Int8 operator ++(Int8 v) => new((sbyte)(v.Value + 1));
    public static Int8 operator --(Int8 v) => new((sbyte)(v.Value - 1));
    public static bool operator ==(Int8 l, Int8 r) => l.Value == r.Value;
    public static bool operator !=(Int8 l, Int8 r) => l.Value != r.Value;
    public static bool operator <(Int8 l, Int8 r) => l.Value < r.Value;
    public static bool operator >(Int8 l, Int8 r) => l.Value > r.Value;
    public static bool operator <=(Int8 l, Int8 r) => l.Value <= r.Value;
    public static bool operator >=(Int8 l, Int8 r) => l.Value >= r.Value;

    // ===== Object =====
    public override string ToString() => Value.ToString();
    public override bool Equals(object? obj) => obj is Int8 other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    bool IEquatable<Int8>.Equals(Int8 other) => Value == other.Value;
}

// ===== Int16 =====

public readonly struct Int16 : IIntegerNumber<Int16>, ICopyable<Int16>, IEquatable<Int16>, IComparable<Int16>,
    IRem<Int16, Int16> {
    internal readonly short Value;
    public Int16(short value) { Value = value; }
    public static readonly Int16 Zero = new(0);
    public static readonly Int16 One = new(1);

    IArithmeticConstants<Int16> IArithmetic<Int16>.Constants => Int16Constants.Instance;
    IRealNumberConstants<Int16> IRealNumber<Int16>.Constants => Int16Constants.Instance;
    public Int16 Copy() => new(Value);
    Int16 IInvariant<Int16>.Value() => this;
    public Int16 Plus(Int16 rhs) => new((short)(Value + rhs.Value));
    public Int16 Minus(Int16 rhs) => new((short)(Value - rhs.Value));
    public Int16 Times(Int16 rhs) => new((short)(Value * rhs.Value));
    public Int16 Div(Int16 rhs) => new((short)(Value / rhs.Value));
    public Int16 Rem(Int16 rhs) => new((short)(Value % rhs.Value));
    public Int16 IntDiv(Int16 rhs) => new((short)(Value / rhs.Value));
    public Int16 Negate() => new((short)(-Value));
    public Int16 Abs() => new((short)global::System.Math.Abs(Value));
    public Int16 Increment() => new((short)(Value + 1));
    public Int16 Decrement() => new((short)(Value - 1));
    public Int16 Cross(Int16 rhs) => Times(rhs);
    public Int16 Pow(int index) => new((short)global::System.Math.Pow(Value, index));
    public Int16 Sqr() => new((short)(Value * Value));
    public Int16 Cub() => new((short)(Value * Value * Value));
    public Int16 Reciprocal() => Value == 1 ? One : Value == -1 ? new(-1) : throw new InvalidOperationException("Reciprocal of integer only defined for ±1");
    public Order Ord(Int16 rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
    public Order? PartialOrd(Int16 rhs) => Ord(rhs);
    public bool Eq(Int16 rhs) => Value == rhs.Value;
    public bool? PartialEq(Int16 rhs) => Value == rhs.Value;
    public int CompareTo(Int16 other) => Value.CompareTo(other.Value);
    public bool Ls(Int16 rhs) => Value < rhs.Value;
    public bool Leq(Int16 rhs) => Value <= rhs.Value;
    public bool Gr(Int16 rhs) => Value > rhs.Value;
    public bool Geq(Int16 rhs) => Value >= rhs.Value;
    public bool Equiv(Int16 rhs) => Value == rhs.Value;
    bool IBounded<Int16>.IsBounded => true;
    Int16? IBounded<Int16>.MinBound => Int16Constants.Instance.Minimum;
    Int16? IBounded<Int16>.MaxBound => Int16Constants.Instance.Maximum;
    bool IBounded<Int16>.IsWithinBounds(Int16 value) => true;
    Int16 IBounded<Int16>.ClampToBounds(Int16 value) => value;
    bool IInfinite<Int16>.SupportsInfinity => false;
    Int16? IInfinite<Int16>.PositiveInfinity => null;
    Int16? IInfinite<Int16>.NegativeInfinityValue => null;
    bool IInfinite<Int16>.IsPositiveInfinity(Int16 value) => false;
    bool IInfinite<Int16>.IsNegativeInfinity(Int16 value) => false;
    bool IFixed<Int16>.IsFixed => false;
    int? IFixed<Int16>.FixedDigits => null;
    Int16? IFixed<Int16>.FixedPrecision => null;
    Int16? IEpsilon<Int16>.PrecisionEpsilon => null;
    bool IRealNumber<Int16>.IsPositiveInfinity() => false;
    bool IRealNumber<Int16>.IsNegativeInfinity() => false;
    bool IRealNumber<Int16>.IsSelfWithinBounds() => true;
    Int16 IRealNumber<Int16>.ClampSelfToBounds() => this;
    public Flt64 ToFlt64() => new(Value);
    public Flt32 ToFlt32() => new(Value);
    public FltX ToFltX() => new(Value);
    public Int64 ToInt64() => new(Value);
    public Int32 ToInt32() => new(Value);
    public Range RangeTo(Int16 rhs) => new(Value, rhs.Value);
    public Range Until(Int16 rhs) => new(Value, rhs.Value);
    public static Int16 operator +(Int16 l, Int16 r) => new((short)(l.Value + r.Value));
    public static Int16 operator -(Int16 l, Int16 r) => new((short)(l.Value - r.Value));
    public static Int16 operator *(Int16 l, Int16 r) => new((short)(l.Value * r.Value));
    public static Int16 operator /(Int16 l, Int16 r) => new((short)(l.Value / r.Value));
    public static Int16 operator %(Int16 l, Int16 r) => new((short)(l.Value % r.Value));
    public static Int16 operator -(Int16 v) => new((short)(-v.Value));
    public static Int16 operator ++(Int16 v) => new((short)(v.Value + 1));
    public static Int16 operator --(Int16 v) => new((short)(v.Value - 1));
    public static bool operator ==(Int16 l, Int16 r) => l.Value == r.Value;
    public static bool operator !=(Int16 l, Int16 r) => l.Value != r.Value;
    public static bool operator <(Int16 l, Int16 r) => l.Value < r.Value;
    public static bool operator >(Int16 l, Int16 r) => l.Value > r.Value;
    public static bool operator <=(Int16 l, Int16 r) => l.Value <= r.Value;
    public static bool operator >=(Int16 l, Int16 r) => l.Value >= r.Value;
    public override string ToString() => Value.ToString();
    public override bool Equals(object? obj) => obj is Int16 other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    bool IEquatable<Int16>.Equals(Int16 other) => Value == other.Value;
}

// ===== Int32 =====

public readonly struct Int32 : IIntegerNumber<Int32>, ICopyable<Int32>, IEquatable<Int32>, IComparable<Int32>,
    IRem<Int32, Int32> {
    internal readonly int Value;
    public Int32(int value) { Value = value; }
    public static readonly Int32 Zero = new(0);
    public static readonly Int32 One = new(1);

    IArithmeticConstants<Int32> IArithmetic<Int32>.Constants => Int32Constants.Instance;
    IRealNumberConstants<Int32> IRealNumber<Int32>.Constants => Int32Constants.Instance;
    public Int32 Copy() => new(Value);
    Int32 IInvariant<Int32>.Value() => this;
    public Int32 Plus(Int32 rhs) => new(Value + rhs.Value);
    public Int32 Minus(Int32 rhs) => new(Value - rhs.Value);
    public Int32 Times(Int32 rhs) => new(Value * rhs.Value);
    public Int32 Div(Int32 rhs) => new(Value / rhs.Value);
    public Int32 Rem(Int32 rhs) => new(Value % rhs.Value);
    public Int32 IntDiv(Int32 rhs) => new(Value / rhs.Value);
    public Int32 Negate() => new(-Value);
    public Int32 Abs() => new(global::System.Math.Abs(Value));
    public Int32 Increment() => new(Value + 1);
    public Int32 Decrement() => new(Value - 1);
    public Int32 Cross(Int32 rhs) => Times(rhs);
    public Int32 Pow(int index) => new((int)global::System.Math.Pow(Value, index));
    public Int32 Sqr() => new(Value * Value);
    public Int32 Cub() => new(Value * Value * Value);
    public Int32 Reciprocal() => Value == 1 ? One : Value == -1 ? new(-1) : throw new InvalidOperationException("Reciprocal of integer only defined for ±1");
    public Order Ord(Int32 rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
    public Order? PartialOrd(Int32 rhs) => Ord(rhs);
    public bool Eq(Int32 rhs) => Value == rhs.Value;
    public bool? PartialEq(Int32 rhs) => Value == rhs.Value;
    public int CompareTo(Int32 other) => Value.CompareTo(other.Value);
    public bool Ls(Int32 rhs) => Value < rhs.Value;
    public bool Leq(Int32 rhs) => Value <= rhs.Value;
    public bool Gr(Int32 rhs) => Value > rhs.Value;
    public bool Geq(Int32 rhs) => Value >= rhs.Value;
    public bool Equiv(Int32 rhs) => Value == rhs.Value;
    bool IBounded<Int32>.IsBounded => true;
    Int32? IBounded<Int32>.MinBound => Int32Constants.Instance.Minimum;
    Int32? IBounded<Int32>.MaxBound => Int32Constants.Instance.Maximum;
    bool IBounded<Int32>.IsWithinBounds(Int32 value) => true;
    Int32 IBounded<Int32>.ClampToBounds(Int32 value) => value;
    bool IInfinite<Int32>.SupportsInfinity => false;
    Int32? IInfinite<Int32>.PositiveInfinity => null;
    Int32? IInfinite<Int32>.NegativeInfinityValue => null;
    bool IInfinite<Int32>.IsPositiveInfinity(Int32 value) => false;
    bool IInfinite<Int32>.IsNegativeInfinity(Int32 value) => false;
    bool IFixed<Int32>.IsFixed => false;
    int? IFixed<Int32>.FixedDigits => null;
    Int32? IFixed<Int32>.FixedPrecision => null;
    Int32? IEpsilon<Int32>.PrecisionEpsilon => null;
    bool IRealNumber<Int32>.IsPositiveInfinity() => false;
    bool IRealNumber<Int32>.IsNegativeInfinity() => false;
    bool IRealNumber<Int32>.IsSelfWithinBounds() => true;
    Int32 IRealNumber<Int32>.ClampSelfToBounds() => this;
    public Flt64 ToFlt64() => new(Value);
    public Flt32 ToFlt32() => new(Value);
    public FltX ToFltX() => new(Value);
    public Int64 ToInt64() => new(Value);
    public Int32 ToInt32_() => this;
    Int32 IRealNumber<Int32>.ToInt32() => this;
    public Range RangeTo(Int32 rhs) => new(Value, rhs.Value);
    public Range Until(Int32 rhs) => new(Value, rhs.Value);
    public static Int32 operator +(Int32 l, Int32 r) => new(l.Value + r.Value);
    public static Int32 operator -(Int32 l, Int32 r) => new(l.Value - r.Value);
    public static Int32 operator *(Int32 l, Int32 r) => new(l.Value * r.Value);
    public static Int32 operator /(Int32 l, Int32 r) => new(l.Value / r.Value);
    public static Int32 operator %(Int32 l, Int32 r) => new(l.Value % r.Value);
    public static Int32 operator -(Int32 v) => new(-v.Value);
    public static Int32 operator ++(Int32 v) => new(v.Value + 1);
    public static Int32 operator --(Int32 v) => new(v.Value - 1);
    public static bool operator ==(Int32 l, Int32 r) => l.Value == r.Value;
    public static bool operator !=(Int32 l, Int32 r) => l.Value != r.Value;
    public static bool operator <(Int32 l, Int32 r) => l.Value < r.Value;
    public static bool operator >(Int32 l, Int32 r) => l.Value > r.Value;
    public static bool operator <=(Int32 l, Int32 r) => l.Value <= r.Value;
    public static bool operator >=(Int32 l, Int32 r) => l.Value >= r.Value;
    public override string ToString() => Value.ToString();
    public override bool Equals(object? obj) => obj is Int32 other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    bool IEquatable<Int32>.Equals(Int32 other) => Value == other.Value;
}

// ===== Int64 =====

public readonly struct Int64 : IIntegerNumber<Int64>, ICopyable<Int64>, IEquatable<Int64>, IComparable<Int64>,
    IRem<Int64, Int64> {
    internal readonly long Value;
    public Int64(long value) { Value = value; }
    public static readonly Int64 Zero = new(0);
    public static readonly Int64 One = new(1);

    IArithmeticConstants<Int64> IArithmetic<Int64>.Constants => Int64Constants.Instance;
    IRealNumberConstants<Int64> IRealNumber<Int64>.Constants => Int64Constants.Instance;
    public Int64 Copy() => new(Value);
    Int64 IInvariant<Int64>.Value() => this;
    public Int64 Plus(Int64 rhs) => new(Value + rhs.Value);
    public Int64 Minus(Int64 rhs) => new(Value - rhs.Value);
    public Int64 Times(Int64 rhs) => new(Value * rhs.Value);
    public Int64 Div(Int64 rhs) => new(Value / rhs.Value);
    public Int64 Rem(Int64 rhs) => new(Value % rhs.Value);
    public Int64 IntDiv(Int64 rhs) => new(Value / rhs.Value);
    public Int64 Negate() => new(-Value);
    public Int64 Abs() => new(global::System.Math.Abs(Value));
    public Int64 Increment() => new(Value + 1);
    public Int64 Decrement() => new(Value - 1);
    public Int64 Cross(Int64 rhs) => Times(rhs);
    public Int64 Pow(int index) => new((long)global::System.Math.Pow(Value, index));
    public Int64 Sqr() => new(Value * Value);
    public Int64 Cub() => new(Value * Value * Value);
    public Int64 Reciprocal() => Value == 1 ? One : Value == -1 ? new(-1) : throw new InvalidOperationException("Reciprocal of integer only defined for ±1");
    public Order Ord(Int64 rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
    public Order? PartialOrd(Int64 rhs) => Ord(rhs);
    public bool Eq(Int64 rhs) => Value == rhs.Value;
    public bool? PartialEq(Int64 rhs) => Value == rhs.Value;
    public int CompareTo(Int64 other) => Value.CompareTo(other.Value);
    public bool Ls(Int64 rhs) => Value < rhs.Value;
    public bool Leq(Int64 rhs) => Value <= rhs.Value;
    public bool Gr(Int64 rhs) => Value > rhs.Value;
    public bool Geq(Int64 rhs) => Value >= rhs.Value;
    public bool Equiv(Int64 rhs) => Value == rhs.Value;
    bool IBounded<Int64>.IsBounded => true;
    Int64? IBounded<Int64>.MinBound => Int64Constants.Instance.Minimum;
    Int64? IBounded<Int64>.MaxBound => Int64Constants.Instance.Maximum;
    bool IBounded<Int64>.IsWithinBounds(Int64 value) => true;
    Int64 IBounded<Int64>.ClampToBounds(Int64 value) => value;
    bool IInfinite<Int64>.SupportsInfinity => false;
    Int64? IInfinite<Int64>.PositiveInfinity => null;
    Int64? IInfinite<Int64>.NegativeInfinityValue => null;
    bool IInfinite<Int64>.IsPositiveInfinity(Int64 value) => false;
    bool IInfinite<Int64>.IsNegativeInfinity(Int64 value) => false;
    bool IFixed<Int64>.IsFixed => false;
    int? IFixed<Int64>.FixedDigits => null;
    Int64? IFixed<Int64>.FixedPrecision => null;
    Int64? IEpsilon<Int64>.PrecisionEpsilon => null;
    bool IRealNumber<Int64>.IsPositiveInfinity() => false;
    bool IRealNumber<Int64>.IsNegativeInfinity() => false;
    bool IRealNumber<Int64>.IsSelfWithinBounds() => true;
    Int64 IRealNumber<Int64>.ClampSelfToBounds() => this;
    public Flt64 ToFlt64() => new(Value);
    public Flt32 ToFlt32() => new(Value);
    public FltX ToFltX() => new(Value);
    public Int64 ToInt64_() => this;
    Int64 IRealNumber<Int64>.ToInt64() => this;
    public Int32 ToInt32() => new((int)Value);
    public long ToLong() => Value;
    public Range RangeTo(Int64 rhs) => new((int)Value, (int)rhs.Value);
    public Range Until(Int64 rhs) => new((int)Value, (int)rhs.Value);
    public static Int64 operator +(Int64 l, Int64 r) => new(l.Value + r.Value);
    public static Int64 operator -(Int64 l, Int64 r) => new(l.Value - r.Value);
    public static Int64 operator *(Int64 l, Int64 r) => new(l.Value * r.Value);
    public static Int64 operator /(Int64 l, Int64 r) => new(l.Value / r.Value);
    public static Int64 operator %(Int64 l, Int64 r) => new(l.Value % r.Value);
    public static Int64 operator -(Int64 v) => new(-v.Value);
    public static Int64 operator ++(Int64 v) => new(v.Value + 1);
    public static Int64 operator --(Int64 v) => new(v.Value - 1);
    public static bool operator ==(Int64 l, Int64 r) => l.Value == r.Value;
    public static bool operator !=(Int64 l, Int64 r) => l.Value != r.Value;
    public static bool operator <(Int64 l, Int64 r) => l.Value < r.Value;
    public static bool operator >(Int64 l, Int64 r) => l.Value > r.Value;
    public static bool operator <=(Int64 l, Int64 r) => l.Value <= r.Value;
    public static bool operator >=(Int64 l, Int64 r) => l.Value >= r.Value;
    public override string ToString() => Value.ToString();
    public override bool Equals(object? obj) => obj is Int64 other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    bool IEquatable<Int64>.Equals(Int64 other) => Value == other.Value;
}

// ===== IntX =====

public readonly struct IntX : IIntegerNumber<IntX>, ICopyable<IntX>, IEquatable<IntX>, IComparable<IntX>,
    IRem<IntX, IntX> {
    internal readonly BigInteger Value;
    public IntX(BigInteger value) { Value = value; }
    public IntX(long value) { Value = new BigInteger(value); }
    public static readonly IntX Zero = new(BigInteger.Zero);
    public static readonly IntX One = new(BigInteger.One);

    IArithmeticConstants<IntX> IArithmetic<IntX>.Constants => IntXConstants.Instance;
    IRealNumberConstants<IntX> IRealNumber<IntX>.Constants => IntXConstants.Instance;
    public IntX Copy() => new(Value);
    IntX IInvariant<IntX>.Value() => this;
    public IntX Plus(IntX rhs) => new(Value + rhs.Value);
    public IntX Minus(IntX rhs) => new(Value - rhs.Value);
    public IntX Times(IntX rhs) => new(Value * rhs.Value);
    public IntX Div(IntX rhs) => new(Value / rhs.Value);
    public IntX Rem(IntX rhs) => new(Value % rhs.Value);
    public IntX IntDiv(IntX rhs) => new(Value / rhs.Value);
    public IntX Negate() => new(-Value);
    public IntX Abs() => new(BigInteger.Abs(Value));
    public IntX Increment() => new(Value + 1);
    public IntX Decrement() => new(Value - 1);
    public IntX Cross(IntX rhs) => Times(rhs);
    public IntX Pow(int index) => new(BigInteger.Pow(Value, index));
    public IntX Sqr() => new(Value * Value);
    public IntX Cub() => new(Value * Value * Value);
    public IntX Reciprocal() => Value == 1 ? One : Value == -1 ? new(-1) : throw new InvalidOperationException("Reciprocal of integer only defined for ±1");
    public Order Ord(IntX rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
    public Order? PartialOrd(IntX rhs) => Ord(rhs);
    public bool Eq(IntX rhs) => Value == rhs.Value;
    public bool? PartialEq(IntX rhs) => Value == rhs.Value;
    public int CompareTo(IntX other) => Value.CompareTo(other.Value);
    public bool Ls(IntX rhs) => Value < rhs.Value;
    public bool Leq(IntX rhs) => Value <= rhs.Value;
    public bool Gr(IntX rhs) => Value > rhs.Value;
    public bool Geq(IntX rhs) => Value >= rhs.Value;
    public bool Equiv(IntX rhs) => Value == rhs.Value;
    bool IBounded<IntX>.IsBounded => false;
    IntX? IBounded<IntX>.MinBound => null;
    IntX? IBounded<IntX>.MaxBound => null;
    bool IBounded<IntX>.IsWithinBounds(IntX value) => true;
    IntX IBounded<IntX>.ClampToBounds(IntX value) => value;
    bool IInfinite<IntX>.SupportsInfinity => false;
    IntX? IInfinite<IntX>.PositiveInfinity => null;
    IntX? IInfinite<IntX>.NegativeInfinityValue => null;
    bool IInfinite<IntX>.IsPositiveInfinity(IntX value) => false;
    bool IInfinite<IntX>.IsNegativeInfinity(IntX value) => false;
    bool IFixed<IntX>.IsFixed => false;
    int? IFixed<IntX>.FixedDigits => null;
    IntX? IFixed<IntX>.FixedPrecision => null;
    public IntX? PrecisionEpsilon => null;
    bool IRealNumber<IntX>.IsPositiveInfinity() => false;
    bool IRealNumber<IntX>.IsNegativeInfinity() => false;
    bool IRealNumber<IntX>.IsSelfWithinBounds() => true;
    IntX IRealNumber<IntX>.ClampSelfToBounds() => this;
    public Flt64 ToFlt64() => new((double)Value);
    public Flt32 ToFlt32() => new((float)(double)Value);
    public FltX ToFltX() => new((decimal)Value);
    public Int64 ToInt64() => new((long)Value);
    public Int32 ToInt32() => new((int)Value);
    public Range RangeTo(IntX rhs) => new((int)Value, (int)rhs.Value);
    public Range Until(IntX rhs) => new((int)Value, (int)rhs.Value);
    public static IntX operator +(IntX l, IntX r) => new(l.Value + r.Value);
    public static IntX operator -(IntX l, IntX r) => new(l.Value - r.Value);
    public static IntX operator *(IntX l, IntX r) => new(l.Value * r.Value);
    public static IntX operator /(IntX l, IntX r) => new(l.Value / r.Value);
    public static IntX operator %(IntX l, IntX r) => new(l.Value % r.Value);
    public static IntX operator -(IntX v) => new(-v.Value);
    public static IntX operator ++(IntX v) => new(v.Value + 1);
    public static IntX operator --(IntX v) => new(v.Value - 1);
    public static bool operator ==(IntX l, IntX r) => l.Value == r.Value;
    public static bool operator !=(IntX l, IntX r) => l.Value != r.Value;
    public static bool operator <(IntX l, IntX r) => l.Value < r.Value;
    public static bool operator >(IntX l, IntX r) => l.Value > r.Value;
    public static bool operator <=(IntX l, IntX r) => l.Value <= r.Value;
    public static bool operator >=(IntX l, IntX r) => l.Value >= r.Value;
    public override string ToString() => Value.ToString();
    public override bool Equals(object? obj) => obj is IntX other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    bool IEquatable<IntX>.Equals(IntX other) => Value == other.Value;
}
