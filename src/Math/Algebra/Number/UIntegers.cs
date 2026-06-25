#nullable enable

using System;
using System.Numerics;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Algebra.Number
{
    // ===== UInteger constants =====

    internal sealed class UInt8Constants : IRealNumberConstants<UInt8>, INumericConstants<UInt8>
    {
        public static readonly UInt8Constants Instance = new();
        public UInt8 Zero => new(0);
        public UInt8 One => new(1);
        public UInt8 Two => new(2);
        public UInt8 Three => new(3);
        public UInt8 Five => new(5);
        public UInt8 Ten => new(10);
        public UInt8 Minimum => new(byte.MinValue);
        public UInt8 Maximum => new(byte.MaxValue);
        public UInt8 PositiveMinimum => One;
        public int? DecimalDigits => null;
        public UInt8 DecimalPrecision => Zero;
        public UInt8 Epsilon => Zero;
        UInt8? INumericConstants<UInt8>.Half => null;
        UInt8? INumericConstants<UInt8>.PositiveInfinity => null;
        UInt8? INumericConstants<UInt8>.NegativeInfinity => null;
        UInt8? INumericConstants<UInt8>.NaN => null;
        UInt8? INumericConstants<UInt8>.Epsilon => Zero;
        UInt8? INumericConstants<UInt8>.DecimalPrecision => Zero;
        UInt8? INumericConstants<UInt8>.Pi => null;
        UInt8? INumericConstants<UInt8>.E => null;
        UInt8? INumericConstants<UInt8>.Lg2 => null;
        public bool IsNaN(UInt8 v) => false;
    }

    internal sealed class UInt16Constants : IRealNumberConstants<UInt16>, INumericConstants<UInt16>
    {
        public static readonly UInt16Constants Instance = new();
        public UInt16 Zero => new(0);
        public UInt16 One => new(1);
        public UInt16 Two => new(2);
        public UInt16 Three => new(3);
        public UInt16 Five => new(5);
        public UInt16 Ten => new(10);
        public UInt16 Minimum => new(ushort.MinValue);
        public UInt16 Maximum => new(ushort.MaxValue);
        public UInt16 PositiveMinimum => One;
        public int? DecimalDigits => null;
        public UInt16 DecimalPrecision => Zero;
        public UInt16 Epsilon => Zero;
        UInt16? INumericConstants<UInt16>.Half => null;
        UInt16? INumericConstants<UInt16>.PositiveInfinity => null;
        UInt16? INumericConstants<UInt16>.NegativeInfinity => null;
        UInt16? INumericConstants<UInt16>.NaN => null;
        UInt16? INumericConstants<UInt16>.Epsilon => Zero;
        UInt16? INumericConstants<UInt16>.DecimalPrecision => Zero;
        UInt16? INumericConstants<UInt16>.Pi => null;
        UInt16? INumericConstants<UInt16>.E => null;
        UInt16? INumericConstants<UInt16>.Lg2 => null;
        public bool IsNaN(UInt16 v) => false;
    }

    internal sealed class UInt32Constants : IRealNumberConstants<UInt32>, INumericConstants<UInt32>
    {
        public static readonly UInt32Constants Instance = new();
        public UInt32 Zero => new(0);
        public UInt32 One => new(1);
        public UInt32 Two => new(2);
        public UInt32 Three => new(3);
        public UInt32 Five => new(5);
        public UInt32 Ten => new(10);
        public UInt32 Minimum => new(uint.MinValue);
        public UInt32 Maximum => new(uint.MaxValue);
        public UInt32 PositiveMinimum => One;
        public int? DecimalDigits => null;
        public UInt32 DecimalPrecision => Zero;
        public UInt32 Epsilon => Zero;
        UInt32? INumericConstants<UInt32>.Half => null;
        UInt32? INumericConstants<UInt32>.PositiveInfinity => null;
        UInt32? INumericConstants<UInt32>.NegativeInfinity => null;
        UInt32? INumericConstants<UInt32>.NaN => null;
        UInt32? INumericConstants<UInt32>.Epsilon => Zero;
        UInt32? INumericConstants<UInt32>.DecimalPrecision => Zero;
        UInt32? INumericConstants<UInt32>.Pi => null;
        UInt32? INumericConstants<UInt32>.E => null;
        UInt32? INumericConstants<UInt32>.Lg2 => null;
        public bool IsNaN(UInt32 v) => false;
    }

    internal sealed class UInt64Constants : IRealNumberConstants<UInt64>, INumericConstants<UInt64>
    {
        public static readonly UInt64Constants Instance = new();
        public UInt64 Zero => new(0);
        public UInt64 One => new(1);
        public UInt64 Two => new(2);
        public UInt64 Three => new(3);
        public UInt64 Five => new(5);
        public UInt64 Ten => new(10);
        public UInt64 Minimum => new(ulong.MinValue);
        public UInt64 Maximum => new(ulong.MaxValue);
        public UInt64 PositiveMinimum => One;
        public int? DecimalDigits => null;
        public UInt64 DecimalPrecision => Zero;
        public UInt64 Epsilon => Zero;
        UInt64? INumericConstants<UInt64>.Half => null;
        UInt64? INumericConstants<UInt64>.PositiveInfinity => null;
        UInt64? INumericConstants<UInt64>.NegativeInfinity => null;
        UInt64? INumericConstants<UInt64>.NaN => null;
        UInt64? INumericConstants<UInt64>.Epsilon => Zero;
        UInt64? INumericConstants<UInt64>.DecimalPrecision => Zero;
        UInt64? INumericConstants<UInt64>.Pi => null;
        UInt64? INumericConstants<UInt64>.E => null;
        UInt64? INumericConstants<UInt64>.Lg2 => null;
        public bool IsNaN(UInt64 v) => false;
    }

    internal sealed class UIntXConstants : IRealNumberConstants<UIntX>, INumericConstants<UIntX>
    {
        public static readonly UIntXConstants Instance = new();
        public UIntX Zero => new(BigInteger.Zero);
        public UIntX One => new(BigInteger.One);
        public UIntX Two => new(2);
        public UIntX Three => new(3);
        public UIntX Five => new(5);
        public UIntX Ten => new(10);
        public UIntX Minimum => new(ulong.MinValue);
        public UIntX Maximum => new(ulong.MaxValue);
        public UIntX PositiveMinimum => One;
        public int? DecimalDigits => null;
        public UIntX DecimalPrecision => Zero;
        public UIntX Epsilon => Zero;
        UIntX? INumericConstants<UIntX>.Half => null;
        UIntX? INumericConstants<UIntX>.PositiveInfinity => null;
        UIntX? INumericConstants<UIntX>.NegativeInfinity => null;
        UIntX? INumericConstants<UIntX>.NaN => null;
        UIntX? INumericConstants<UIntX>.Epsilon => Zero;
        UIntX? INumericConstants<UIntX>.DecimalPrecision => Zero;
        UIntX? INumericConstants<UIntX>.Pi => null;
        UIntX? INumericConstants<UIntX>.E => null;
        UIntX? INumericConstants<UIntX>.Lg2 => null;
        public bool IsNaN(UIntX v) => false;
    }

    // ===== UInt8 =====

    public readonly struct UInt8 : IUIntegerNumber<UInt8>, ICopyable<UInt8>, IEquatable<UInt8>, IComparable<UInt8>,
        IRem<UInt8, UInt8>
    {
        internal readonly byte Value;
        public UInt8(byte value) { Value = value; }
        public UInt8(bool value) { Value = value ? (byte)1 : (byte)0; }
        public static readonly UInt8 Zero = new(0);
        public static readonly UInt8 One = new(1);
        public static readonly UInt8 Two = new(2);

        IArithmeticConstants<UInt8> IArithmetic<UInt8>.Constants => UInt8Constants.Instance;
        IRealNumberConstants<UInt8> IRealNumber<UInt8>.Constants => UInt8Constants.Instance;
        public UInt8 Copy() => new(Value);
        UInt8 IInvariant<UInt8>.Value() => this;
        public UInt8 Plus(UInt8 rhs) => new((byte)(Value + rhs.Value));
        public UInt8 Minus(UInt8 rhs) => new(unchecked((byte)(Value - rhs.Value)));
        public UInt8 Times(UInt8 rhs) => new((byte)(Value * rhs.Value));
        public UInt8 Div(UInt8 rhs) => new((byte)(Value / rhs.Value));
        public UInt8 Rem(UInt8 rhs) => new((byte)(Value % rhs.Value));
        public UInt8 IntDiv(UInt8 rhs) => new((byte)(Value / rhs.Value));
        public UInt8 Negate() => new(0); // unsigned: no-op
        public UInt8 Abs() => this;
        public UInt8 Increment() => new((byte)(Value + 1));
        public UInt8 Decrement() => new((byte)(Value - 1));
        public UInt8 Cross(UInt8 rhs) => Times(rhs);
        public UInt8 Pow(int index) => new((byte)global::System.Math.Pow(Value, index));
        public UInt8 Sqr() => new((byte)(Value * Value));
        public UInt8 Cub() => new((byte)(Value * Value * Value));
        public UInt8 Reciprocal() => Value == 1 ? One : throw new InvalidOperationException("Reciprocal of unsigned integer only defined for 1");
        public Order Ord(UInt8 rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
        public Order? PartialOrd(UInt8 rhs) => Ord(rhs);
        public bool Eq(UInt8 rhs) => Value == rhs.Value;
        public bool? PartialEq(UInt8 rhs) => Value == rhs.Value;
        public int CompareTo(UInt8 other) => Value.CompareTo(other.Value);
        public bool Ls(UInt8 rhs) => Value < rhs.Value;
        public bool Leq(UInt8 rhs) => Value <= rhs.Value;
        public bool Gr(UInt8 rhs) => Value > rhs.Value;
        public bool Geq(UInt8 rhs) => Value >= rhs.Value;
        public bool Equiv(UInt8 rhs) => Value == rhs.Value;
        bool IBounded<UInt8>.IsBounded => true;
        UInt8? IBounded<UInt8>.MinBound => UInt8Constants.Instance.Minimum;
        UInt8? IBounded<UInt8>.MaxBound => UInt8Constants.Instance.Maximum;
        bool IBounded<UInt8>.IsWithinBounds(UInt8 value) => true;
        UInt8 IBounded<UInt8>.ClampToBounds(UInt8 value) => value;
        bool IInfinite<UInt8>.SupportsInfinity => false;
        UInt8? IInfinite<UInt8>.PositiveInfinity => null;
        UInt8? IInfinite<UInt8>.NegativeInfinityValue => null;
        bool IInfinite<UInt8>.IsPositiveInfinity(UInt8 value) => false;
        bool IInfinite<UInt8>.IsNegativeInfinity(UInt8 value) => false;
        bool IFixed<UInt8>.IsFixed => false;
        int? IFixed<UInt8>.FixedDigits => null;
        UInt8? IFixed<UInt8>.FixedPrecision => null;
        UInt8? IEpsilon<UInt8>.PrecisionEpsilon => null;
        bool IRealNumber<UInt8>.IsPositiveInfinity() => false;
        bool IRealNumber<UInt8>.IsNegativeInfinity() => false;
        bool IRealNumber<UInt8>.IsSelfWithinBounds() => true;
        UInt8 IRealNumber<UInt8>.ClampSelfToBounds() => this;
        public Flt64 ToFlt64() => new(Value);
        public Flt32 ToFlt32() => new(Value);
        public FltX ToFltX() => new((decimal)Value);
        public Int64 ToInt64() => new(Value);
        public Int32 ToInt32() => new(Value);
        public Range RangeTo(UInt8 rhs) => new(Value, rhs.Value);
        public Range Until(UInt8 rhs) => new(Value, rhs.Value);
        public static UInt8 operator +(UInt8 l, UInt8 r) => new((byte)(l.Value + r.Value));
        public static UInt8 operator -(UInt8 l, UInt8 r) => new(unchecked((byte)(l.Value - r.Value)));
        public static UInt8 operator *(UInt8 l, UInt8 r) => new((byte)(l.Value * r.Value));
        public static UInt8 operator /(UInt8 l, UInt8 r) => new((byte)(l.Value / r.Value));
        public static UInt8 operator %(UInt8 l, UInt8 r) => new((byte)(l.Value % r.Value));
        public static UInt8 operator ++(UInt8 v) => new((byte)(v.Value + 1));
        public static UInt8 operator --(UInt8 v) => new((byte)(v.Value - 1));
        public static bool operator ==(UInt8 l, UInt8 r) => l.Value == r.Value;
        public static bool operator !=(UInt8 l, UInt8 r) => l.Value != r.Value;
        public static bool operator <(UInt8 l, UInt8 r) => l.Value < r.Value;
        public static bool operator >(UInt8 l, UInt8 r) => l.Value > r.Value;
        public static bool operator <=(UInt8 l, UInt8 r) => l.Value <= r.Value;
        public static bool operator >=(UInt8 l, UInt8 r) => l.Value >= r.Value;
        public override string ToString() => Value.ToString();
        public override bool Equals(object? obj) => obj is UInt8 other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
        bool IEquatable<UInt8>.Equals(UInt8 other) => Value == other.Value;
    }

    // ===== UInt16 =====

    public readonly struct UInt16 : IUIntegerNumber<UInt16>, ICopyable<UInt16>, IEquatable<UInt16>, IComparable<UInt16>,
        IRem<UInt16, UInt16>
    {
        internal readonly ushort Value;
        public UInt16(ushort value) { Value = value; }
        public static readonly UInt16 Zero = new(0);
        public static readonly UInt16 One = new(1);

        IArithmeticConstants<UInt16> IArithmetic<UInt16>.Constants => UInt16Constants.Instance;
        IRealNumberConstants<UInt16> IRealNumber<UInt16>.Constants => UInt16Constants.Instance;
        public UInt16 Copy() => new(Value);
        UInt16 IInvariant<UInt16>.Value() => this;
        public UInt16 Plus(UInt16 rhs) => new((ushort)(Value + rhs.Value));
        public UInt16 Minus(UInt16 rhs) => new(unchecked((ushort)(Value - rhs.Value)));
        public UInt16 Times(UInt16 rhs) => new((ushort)(Value * rhs.Value));
        public UInt16 Div(UInt16 rhs) => new((ushort)(Value / rhs.Value));
        public UInt16 Rem(UInt16 rhs) => new((ushort)(Value % rhs.Value));
        public UInt16 IntDiv(UInt16 rhs) => new((ushort)(Value / rhs.Value));
        public UInt16 Negate() => new(0);
        public UInt16 Abs() => this;
        public UInt16 Increment() => new((ushort)(Value + 1));
        public UInt16 Decrement() => new((ushort)(Value - 1));
        public UInt16 Cross(UInt16 rhs) => Times(rhs);
        public UInt16 Pow(int index) => new((ushort)global::System.Math.Pow(Value, index));
        public UInt16 Sqr() => new((ushort)(Value * Value));
        public UInt16 Cub() => new((ushort)(Value * Value * Value));
        public UInt16 Reciprocal() => Value == 1 ? One : throw new InvalidOperationException("Reciprocal of unsigned integer only defined for 1");
        public Order Ord(UInt16 rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
        public Order? PartialOrd(UInt16 rhs) => Ord(rhs);
        public bool Eq(UInt16 rhs) => Value == rhs.Value;
        public bool? PartialEq(UInt16 rhs) => Value == rhs.Value;
        public int CompareTo(UInt16 other) => Value.CompareTo(other.Value);
        public bool Ls(UInt16 rhs) => Value < rhs.Value;
        public bool Leq(UInt16 rhs) => Value <= rhs.Value;
        public bool Gr(UInt16 rhs) => Value > rhs.Value;
        public bool Geq(UInt16 rhs) => Value >= rhs.Value;
        public bool Equiv(UInt16 rhs) => Value == rhs.Value;
        bool IBounded<UInt16>.IsBounded => true;
        UInt16? IBounded<UInt16>.MinBound => UInt16Constants.Instance.Minimum;
        UInt16? IBounded<UInt16>.MaxBound => UInt16Constants.Instance.Maximum;
        bool IBounded<UInt16>.IsWithinBounds(UInt16 value) => true;
        UInt16 IBounded<UInt16>.ClampToBounds(UInt16 value) => value;
        bool IInfinite<UInt16>.SupportsInfinity => false;
        UInt16? IInfinite<UInt16>.PositiveInfinity => null;
        UInt16? IInfinite<UInt16>.NegativeInfinityValue => null;
        bool IInfinite<UInt16>.IsPositiveInfinity(UInt16 value) => false;
        bool IInfinite<UInt16>.IsNegativeInfinity(UInt16 value) => false;
        bool IFixed<UInt16>.IsFixed => false;
        int? IFixed<UInt16>.FixedDigits => null;
        UInt16? IFixed<UInt16>.FixedPrecision => null;
        UInt16? IEpsilon<UInt16>.PrecisionEpsilon => null;
        bool IRealNumber<UInt16>.IsPositiveInfinity() => false;
        bool IRealNumber<UInt16>.IsNegativeInfinity() => false;
        bool IRealNumber<UInt16>.IsSelfWithinBounds() => true;
        UInt16 IRealNumber<UInt16>.ClampSelfToBounds() => this;
        public Flt64 ToFlt64() => new(Value);
        public Flt32 ToFlt32() => new(Value);
        public FltX ToFltX() => new((decimal)Value);
        public Int64 ToInt64() => new(Value);
        public Int32 ToInt32() => new(Value);
        public Range RangeTo(UInt16 rhs) => new(Value, rhs.Value);
        public Range Until(UInt16 rhs) => new(Value, rhs.Value);
        public static UInt16 operator +(UInt16 l, UInt16 r) => new((ushort)(l.Value + r.Value));
        public static UInt16 operator -(UInt16 l, UInt16 r) => new(unchecked((ushort)(l.Value - r.Value)));
        public static UInt16 operator *(UInt16 l, UInt16 r) => new((ushort)(l.Value * r.Value));
        public static UInt16 operator /(UInt16 l, UInt16 r) => new((ushort)(l.Value / r.Value));
        public static UInt16 operator %(UInt16 l, UInt16 r) => new((ushort)(l.Value % r.Value));
        public static UInt16 operator ++(UInt16 v) => new((ushort)(v.Value + 1));
        public static UInt16 operator --(UInt16 v) => new((ushort)(v.Value - 1));
        public static bool operator ==(UInt16 l, UInt16 r) => l.Value == r.Value;
        public static bool operator !=(UInt16 l, UInt16 r) => l.Value != r.Value;
        public static bool operator <(UInt16 l, UInt16 r) => l.Value < r.Value;
        public static bool operator >(UInt16 l, UInt16 r) => l.Value > r.Value;
        public static bool operator <=(UInt16 l, UInt16 r) => l.Value <= r.Value;
        public static bool operator >=(UInt16 l, UInt16 r) => l.Value >= r.Value;
        public override string ToString() => Value.ToString();
        public override bool Equals(object? obj) => obj is UInt16 other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
        bool IEquatable<UInt16>.Equals(UInt16 other) => Value == other.Value;
    }

    // ===== UInt32 =====

    public readonly struct UInt32 : IUIntegerNumber<UInt32>, ICopyable<UInt32>, IEquatable<UInt32>, IComparable<UInt32>,
        IRem<UInt32, UInt32>
    {
        internal readonly uint Value;
        public UInt32(uint value) { Value = value; }
        public static readonly UInt32 Zero = new(0);
        public static readonly UInt32 One = new(1);

        IArithmeticConstants<UInt32> IArithmetic<UInt32>.Constants => UInt32Constants.Instance;
        IRealNumberConstants<UInt32> IRealNumber<UInt32>.Constants => UInt32Constants.Instance;
        public UInt32 Copy() => new(Value);
        UInt32 IInvariant<UInt32>.Value() => this;
        public UInt32 Plus(UInt32 rhs) => new(Value + rhs.Value);
        public UInt32 Minus(UInt32 rhs) => new(unchecked(Value - rhs.Value));
        public UInt32 Times(UInt32 rhs) => new(Value * rhs.Value);
        public UInt32 Div(UInt32 rhs) => new(Value / rhs.Value);
        public UInt32 Rem(UInt32 rhs) => new(Value % rhs.Value);
        public UInt32 IntDiv(UInt32 rhs) => new(Value / rhs.Value);
        public UInt32 Negate() => new(0);
        public UInt32 Abs() => this;
        public UInt32 Increment() => new(Value + 1);
        public UInt32 Decrement() => new(Value - 1);
        public UInt32 Cross(UInt32 rhs) => Times(rhs);
        public UInt32 Pow(int index) => new((uint)global::System.Math.Pow(Value, index));
        public UInt32 Sqr() => new(Value * Value);
        public UInt32 Cub() => new(Value * Value * Value);
        public UInt32 Reciprocal() => Value == 1 ? One : throw new InvalidOperationException("Reciprocal of unsigned integer only defined for 1");
        public Order Ord(UInt32 rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
        public Order? PartialOrd(UInt32 rhs) => Ord(rhs);
        public bool Eq(UInt32 rhs) => Value == rhs.Value;
        public bool? PartialEq(UInt32 rhs) => Value == rhs.Value;
        public int CompareTo(UInt32 other) => Value.CompareTo(other.Value);
        public bool Ls(UInt32 rhs) => Value < rhs.Value;
        public bool Leq(UInt32 rhs) => Value <= rhs.Value;
        public bool Gr(UInt32 rhs) => Value > rhs.Value;
        public bool Geq(UInt32 rhs) => Value >= rhs.Value;
        public bool Equiv(UInt32 rhs) => Value == rhs.Value;
        bool IBounded<UInt32>.IsBounded => true;
        UInt32? IBounded<UInt32>.MinBound => UInt32Constants.Instance.Minimum;
        UInt32? IBounded<UInt32>.MaxBound => UInt32Constants.Instance.Maximum;
        bool IBounded<UInt32>.IsWithinBounds(UInt32 value) => true;
        UInt32 IBounded<UInt32>.ClampToBounds(UInt32 value) => value;
        bool IInfinite<UInt32>.SupportsInfinity => false;
        UInt32? IInfinite<UInt32>.PositiveInfinity => null;
        UInt32? IInfinite<UInt32>.NegativeInfinityValue => null;
        bool IInfinite<UInt32>.IsPositiveInfinity(UInt32 value) => false;
        bool IInfinite<UInt32>.IsNegativeInfinity(UInt32 value) => false;
        bool IFixed<UInt32>.IsFixed => false;
        int? IFixed<UInt32>.FixedDigits => null;
        UInt32? IFixed<UInt32>.FixedPrecision => null;
        UInt32? IEpsilon<UInt32>.PrecisionEpsilon => null;
        bool IRealNumber<UInt32>.IsPositiveInfinity() => false;
        bool IRealNumber<UInt32>.IsNegativeInfinity() => false;
        bool IRealNumber<UInt32>.IsSelfWithinBounds() => true;
        UInt32 IRealNumber<UInt32>.ClampSelfToBounds() => this;
        public Flt64 ToFlt64() => new(Value);
        public Flt32 ToFlt32() => new(Value);
        public FltX ToFltX() => new((decimal)Value);
        public Int64 ToInt64() => new(Value);
        public Int32 ToInt32() => new((int)Value);
        public Range RangeTo(UInt32 rhs) => new((int)Value, (int)rhs.Value);
        public Range Until(UInt32 rhs) => new((int)Value, (int)rhs.Value);
        public static UInt32 operator +(UInt32 l, UInt32 r) => new(l.Value + r.Value);
        public static UInt32 operator -(UInt32 l, UInt32 r) => new(unchecked(l.Value - r.Value));
        public static UInt32 operator *(UInt32 l, UInt32 r) => new(l.Value * r.Value);
        public static UInt32 operator /(UInt32 l, UInt32 r) => new(l.Value / r.Value);
        public static UInt32 operator %(UInt32 l, UInt32 r) => new(l.Value % r.Value);
        public static UInt32 operator ++(UInt32 v) => new(v.Value + 1);
        public static UInt32 operator --(UInt32 v) => new(v.Value - 1);
        public static bool operator ==(UInt32 l, UInt32 r) => l.Value == r.Value;
        public static bool operator !=(UInt32 l, UInt32 r) => l.Value != r.Value;
        public static bool operator <(UInt32 l, UInt32 r) => l.Value < r.Value;
        public static bool operator >(UInt32 l, UInt32 r) => l.Value > r.Value;
        public static bool operator <=(UInt32 l, UInt32 r) => l.Value <= r.Value;
        public static bool operator >=(UInt32 l, UInt32 r) => l.Value >= r.Value;
        public override string ToString() => Value.ToString();
        public override bool Equals(object? obj) => obj is UInt32 other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
        bool IEquatable<UInt32>.Equals(UInt32 other) => Value == other.Value;
    }

    // ===== UInt64 =====

    public readonly struct UInt64 : IUIntegerNumber<UInt64>, ICopyable<UInt64>, IEquatable<UInt64>, IComparable<UInt64>,
        IRem<UInt64, UInt64>
    {
        internal readonly ulong Value;
        public UInt64(ulong value) { Value = value; }
        public UInt64(int value) { Value = (ulong)value; }
        public static readonly UInt64 Zero = new(0);
        public static readonly UInt64 One = new(1);

        IArithmeticConstants<UInt64> IArithmetic<UInt64>.Constants => UInt64Constants.Instance;
        IRealNumberConstants<UInt64> IRealNumber<UInt64>.Constants => UInt64Constants.Instance;
        public UInt64 Copy() => new(Value);
        UInt64 IInvariant<UInt64>.Value() => this;
        public UInt64 Plus(UInt64 rhs) => new(Value + rhs.Value);
        public UInt64 Minus(UInt64 rhs) => new(unchecked(Value - rhs.Value));
        public UInt64 Times(UInt64 rhs) => new(Value * rhs.Value);
        public UInt64 Div(UInt64 rhs) => new(Value / rhs.Value);
        public UInt64 Rem(UInt64 rhs) => new(Value % rhs.Value);
        public UInt64 IntDiv(UInt64 rhs) => new(Value / rhs.Value);
        public UInt64 Negate() => new(0);
        public UInt64 Abs() => this;
        public UInt64 Increment() => new(Value + 1);
        public UInt64 Decrement() => new(Value - 1);
        public UInt64 Cross(UInt64 rhs) => Times(rhs);
        public UInt64 Pow(int index) => new((ulong)global::System.Math.Pow(Value, index));
        public UInt64 Sqr() => new(Value * Value);
        public UInt64 Cub() => new(Value * Value * Value);
        public UInt64 Reciprocal() => Value == 1 ? One : throw new InvalidOperationException("Reciprocal of unsigned integer only defined for 1");
        public Order Ord(UInt64 rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
        public Order? PartialOrd(UInt64 rhs) => Ord(rhs);
        public bool Eq(UInt64 rhs) => Value == rhs.Value;
        public bool? PartialEq(UInt64 rhs) => Value == rhs.Value;
        public int CompareTo(UInt64 other) => Value.CompareTo(other.Value);
        public bool Ls(UInt64 rhs) => Value < rhs.Value;
        public bool Leq(UInt64 rhs) => Value <= rhs.Value;
        public bool Gr(UInt64 rhs) => Value > rhs.Value;
        public bool Geq(UInt64 rhs) => Value >= rhs.Value;
        public bool Equiv(UInt64 rhs) => Value == rhs.Value;
        bool IBounded<UInt64>.IsBounded => true;
        UInt64? IBounded<UInt64>.MinBound => UInt64Constants.Instance.Minimum;
        UInt64? IBounded<UInt64>.MaxBound => UInt64Constants.Instance.Maximum;
        bool IBounded<UInt64>.IsWithinBounds(UInt64 value) => true;
        UInt64 IBounded<UInt64>.ClampToBounds(UInt64 value) => value;
        bool IInfinite<UInt64>.SupportsInfinity => false;
        UInt64? IInfinite<UInt64>.PositiveInfinity => null;
        UInt64? IInfinite<UInt64>.NegativeInfinityValue => null;
        bool IInfinite<UInt64>.IsPositiveInfinity(UInt64 value) => false;
        bool IInfinite<UInt64>.IsNegativeInfinity(UInt64 value) => false;
        bool IFixed<UInt64>.IsFixed => false;
        int? IFixed<UInt64>.FixedDigits => null;
        UInt64? IFixed<UInt64>.FixedPrecision => null;
        UInt64? IEpsilon<UInt64>.PrecisionEpsilon => null;
        bool IRealNumber<UInt64>.IsPositiveInfinity() => false;
        bool IRealNumber<UInt64>.IsNegativeInfinity() => false;
        bool IRealNumber<UInt64>.IsSelfWithinBounds() => true;
        UInt64 IRealNumber<UInt64>.ClampSelfToBounds() => this;
        public Flt64 ToFlt64() => new(Value);
        public Flt32 ToFlt32() => new(Value);
        public FltX ToFltX() => new((decimal)Value);
        public Int64 ToInt64() => new((long)Value);
        public Int32 ToInt32() => new((int)Value);
        public Range RangeTo(UInt64 rhs) => new((int)Value, (int)rhs.Value);
        public Range Until(UInt64 rhs) => new((int)Value, (int)rhs.Value);
        public static UInt64 operator +(UInt64 l, UInt64 r) => new(l.Value + r.Value);
        public static UInt64 operator -(UInt64 l, UInt64 r) => new(unchecked(l.Value - r.Value));
        public static UInt64 operator *(UInt64 l, UInt64 r) => new(l.Value * r.Value);
        public static UInt64 operator /(UInt64 l, UInt64 r) => new(l.Value / r.Value);
        public static UInt64 operator %(UInt64 l, UInt64 r) => new(l.Value % r.Value);
        public static UInt64 operator ++(UInt64 v) => new(v.Value + 1);
        public static UInt64 operator --(UInt64 v) => new(v.Value - 1);
        public static bool operator ==(UInt64 l, UInt64 r) => l.Value == r.Value;
        public static bool operator !=(UInt64 l, UInt64 r) => l.Value != r.Value;
        public static bool operator <(UInt64 l, UInt64 r) => l.Value < r.Value;
        public static bool operator >(UInt64 l, UInt64 r) => l.Value > r.Value;
        public static bool operator <=(UInt64 l, UInt64 r) => l.Value <= r.Value;
        public static bool operator >=(UInt64 l, UInt64 r) => l.Value >= r.Value;
        public override string ToString() => Value.ToString();
        public override bool Equals(object? obj) => obj is UInt64 other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
        bool IEquatable<UInt64>.Equals(UInt64 other) => Value == other.Value;
    }

    // ===== UIntX =====

    public readonly struct UIntX : IUIntegerNumber<UIntX>, ICopyable<UIntX>, IEquatable<UIntX>, IComparable<UIntX>,
        IRem<UIntX, UIntX>
    {
        internal readonly BigInteger Value;
        public UIntX(BigInteger value) { Value = value; }
        public UIntX(long value) { Value = new BigInteger(value); }
        public static readonly UIntX Zero = new(BigInteger.Zero);
        public static readonly UIntX One = new(BigInteger.One);

        IArithmeticConstants<UIntX> IArithmetic<UIntX>.Constants => UIntXConstants.Instance;
        IRealNumberConstants<UIntX> IRealNumber<UIntX>.Constants => UIntXConstants.Instance;
        public UIntX Copy() => new(Value);
        UIntX IInvariant<UIntX>.Value() => this;
        public UIntX Plus(UIntX rhs) => new(Value + rhs.Value);
        public UIntX Minus(UIntX rhs) => new(Value - rhs.Value);
        public UIntX Times(UIntX rhs) => new(Value * rhs.Value);
        public UIntX Div(UIntX rhs) => new(Value / rhs.Value);
        public UIntX Rem(UIntX rhs) => new(Value % rhs.Value);
        public UIntX IntDiv(UIntX rhs) => new(Value / rhs.Value);
        public UIntX Negate() => new(-Value);
        public UIntX Abs() => new(BigInteger.Abs(Value));
        public UIntX Increment() => new(Value + 1);
        public UIntX Decrement() => new(Value - 1);
        public UIntX Cross(UIntX rhs) => Times(rhs);
        public UIntX Pow(int index) => new(BigInteger.Pow(Value, index));
        public UIntX Sqr() => new(Value * Value);
        public UIntX Cub() => new(Value * Value * Value);
        public UIntX Reciprocal() => Value == 1 ? One : throw new InvalidOperationException("Reciprocal of unsigned integer only defined for 1");
        public Order Ord(UIntX rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
        public Order? PartialOrd(UIntX rhs) => Ord(rhs);
        public bool Eq(UIntX rhs) => Value == rhs.Value;
        public bool? PartialEq(UIntX rhs) => Value == rhs.Value;
        public int CompareTo(UIntX other) => Value.CompareTo(other.Value);
        public bool Ls(UIntX rhs) => Value < rhs.Value;
        public bool Leq(UIntX rhs) => Value <= rhs.Value;
        public bool Gr(UIntX rhs) => Value > rhs.Value;
        public bool Geq(UIntX rhs) => Value >= rhs.Value;
        public bool Equiv(UIntX rhs) => Value == rhs.Value;
        bool IBounded<UIntX>.IsBounded => false;
        UIntX? IBounded<UIntX>.MinBound => null;
        UIntX? IBounded<UIntX>.MaxBound => null;
        bool IBounded<UIntX>.IsWithinBounds(UIntX value) => true;
        UIntX IBounded<UIntX>.ClampToBounds(UIntX value) => value;
        bool IInfinite<UIntX>.SupportsInfinity => false;
        UIntX? IInfinite<UIntX>.PositiveInfinity => null;
        UIntX? IInfinite<UIntX>.NegativeInfinityValue => null;
        bool IInfinite<UIntX>.IsPositiveInfinity(UIntX value) => false;
        bool IInfinite<UIntX>.IsNegativeInfinity(UIntX value) => false;
        bool IFixed<UIntX>.IsFixed => false;
        int? IFixed<UIntX>.FixedDigits => null;
        UIntX? IFixed<UIntX>.FixedPrecision => null;
        UIntX? IEpsilon<UIntX>.PrecisionEpsilon => null;
        bool IRealNumber<UIntX>.IsPositiveInfinity() => false;
        bool IRealNumber<UIntX>.IsNegativeInfinity() => false;
        bool IRealNumber<UIntX>.IsSelfWithinBounds() => true;
        UIntX IRealNumber<UIntX>.ClampSelfToBounds() => this;
        public Flt64 ToFlt64() => new((double)Value);
        public Flt32 ToFlt32() => new((float)(double)Value);
        public FltX ToFltX() => new((decimal)Value);
        public Int64 ToInt64() => new((long)Value);
        public Int32 ToInt32() => new((int)Value);
        public Range RangeTo(UIntX rhs) => new((int)Value, (int)rhs.Value);
        public Range Until(UIntX rhs) => new((int)Value, (int)rhs.Value);
        public static UIntX operator +(UIntX l, UIntX r) => new(l.Value + r.Value);
        public static UIntX operator -(UIntX l, UIntX r) => new(l.Value - r.Value);
        public static UIntX operator *(UIntX l, UIntX r) => new(l.Value * r.Value);
        public static UIntX operator /(UIntX l, UIntX r) => new(l.Value / r.Value);
        public static UIntX operator %(UIntX l, UIntX r) => new(l.Value % r.Value);
        public static UIntX operator -(UIntX v) => new(-v.Value);
        public static UIntX operator ++(UIntX v) => new(v.Value + 1);
        public static UIntX operator --(UIntX v) => new(v.Value - 1);
        public static bool operator ==(UIntX l, UIntX r) => l.Value == r.Value;
        public static bool operator !=(UIntX l, UIntX r) => l.Value != r.Value;
        public static bool operator <(UIntX l, UIntX r) => l.Value < r.Value;
        public static bool operator >(UIntX l, UIntX r) => l.Value > r.Value;
        public static bool operator <=(UIntX l, UIntX r) => l.Value <= r.Value;
        public static bool operator >=(UIntX l, UIntX r) => l.Value >= r.Value;
        public override string ToString() => Value.ToString();
        public override bool Equals(object? obj) => obj is UIntX other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
        bool IEquatable<UIntX>.Equals(UIntX other) => Value == other.Value;
    }
}
