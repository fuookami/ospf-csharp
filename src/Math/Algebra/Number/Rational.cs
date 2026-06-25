#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Math.Algebra.Number;
// ===== RtnX (simplified rational over double, backward compat) =====

/// <summary>
/// 任意精度有理数（简化实现）/ Arbitrary-precision rational number (simplified)
/// </summary>
public readonly struct RtnX : IFloatingNumber<RtnX>, ICopyable<RtnX>, IEquatable<RtnX>, IComparable<RtnX>,
    IRem<RtnX, RtnX>, IFlt64ValueConverter<RtnX> {
    internal readonly double Numerator;
    internal readonly double Denominator;

    public RtnX(double numerator, double denominator) {
        if (denominator == 0) {
            denominator = 1; // defensive: zero-den → normalize to num/1
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    public static readonly RtnX Zero = new(0, 1);
    public static readonly RtnX One = new(1, 1);

    // ===== Interface properties =====
    IArithmeticConstants<RtnX> IArithmetic<RtnX>.Constants => RtnXConstants.Instance;
    IRealNumberConstants<RtnX> IRealNumber<RtnX>.Constants => RtnXConstants.Instance;
    IFloatingNumberConstants<RtnX> IFloatingNumber<RtnX>.Constants => RtnXConstants.Instance;

    // ===== Factory methods =====
    public static Result<RtnX, ErrorCode, Error<ErrorCode>> Of(double num, double den) {
        if (den == 0) {
            return Results.Failed<RtnX>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "Rational denominator cannot be zero"));
        }

        return Results.Ok(new RtnX(num, den));
    }

    public static RtnX? OfOrNull(double num, double den) => den == 0 ? null : new RtnX(num, den);

    // ===== ICopyable =====
    public RtnX Copy() => new(Numerator, Denominator);

    // ===== IInvariant =====
    RtnX IInvariant<RtnX>.Value() => this;

    // ===== Arithmetic =====
    public RtnX Plus(RtnX rhs) => new(Numerator * rhs.Denominator + rhs.Numerator * Denominator, Denominator * rhs.Denominator);
    public RtnX Minus(RtnX rhs) => new(Numerator * rhs.Denominator - rhs.Numerator * Denominator, Denominator * rhs.Denominator);
    public RtnX Times(RtnX rhs) => new(Numerator * rhs.Numerator, Denominator * rhs.Denominator);
    public RtnX Div(RtnX rhs) => new(Numerator * rhs.Denominator, Denominator * rhs.Numerator);
    public RtnX Rem(RtnX rhs) => new(Numerator % rhs.Numerator, Denominator);
    public RtnX IntDiv(RtnX rhs) => new(global::System.Math.Floor(ToDouble() / rhs.ToDouble()), 1);
    public RtnX Negate() => new(-Numerator, Denominator);
    public RtnX Abs() => new(global::System.Math.Abs(Numerator), Denominator);
    public RtnX Reciprocal() => new(Denominator, Numerator);
    public RtnX Increment() => new(Numerator + Denominator, Denominator);
    public RtnX Decrement() => new(Numerator - Denominator, Denominator);
    public RtnX Cross(RtnX rhs) => Times(rhs);

    // ===== IPow =====
    public RtnX Pow(int index) => new(global::System.Math.Pow(Numerator, index), global::System.Math.Pow(Denominator, index));
    public RtnX Sqr() => new(Numerator * Numerator, Denominator * Denominator);
    public RtnX Cub() => new(Numerator * Numerator * Numerator, Denominator * Denominator * Denominator);

    // ===== IFloatingNumber extras =====
    public RtnX Sqrt() => new(global::System.Math.Sqrt(Numerator / Denominator), 1);
    public RtnX Acos() => new(global::System.Math.Acos(Numerator / Denominator), 1);
    public static RtnX FromInt32(int value) => new(value, 1);

    // ===== Comparison =====
    private double ToDouble() => Numerator / Denominator;
    public Order Ord(RtnX rhs) => ToDouble().CompareTo(rhs.ToDouble()) < 0 ? new Order.Less() : ToDouble() > rhs.ToDouble() ? new Order.Greater() : new Order.Equal();
    public Order? PartialOrd(RtnX rhs) => Ord(rhs);
    public bool Eq(RtnX rhs) => Numerator * rhs.Denominator == rhs.Numerator * Denominator;
    public bool? PartialEq(RtnX rhs) => Eq(rhs);
    public int CompareTo(RtnX other) => ToDouble().CompareTo(other.ToDouble());
    public bool Ls(RtnX rhs) => Numerator * rhs.Denominator < rhs.Numerator * Denominator;
    public bool Leq(RtnX rhs) => Numerator * rhs.Denominator <= rhs.Numerator * Denominator;
    public bool Gr(RtnX rhs) => Numerator * rhs.Denominator > rhs.Numerator * Denominator;
    public bool Geq(RtnX rhs) => Numerator * rhs.Denominator >= rhs.Numerator * Denominator;
    public bool Equiv(RtnX rhs) => global::System.Math.Abs(ToDouble() - rhs.ToDouble()) < 1e-10;

    // ===== IBounded (explicit) =====
    bool IBounded<RtnX>.IsBounded => false;
    RtnX? IBounded<RtnX>.MinBound => null;
    RtnX? IBounded<RtnX>.MaxBound => null;
    bool IBounded<RtnX>.IsWithinBounds(RtnX value) => true;
    RtnX IBounded<RtnX>.ClampToBounds(RtnX value) => value;

    // ===== IInfinite (explicit) =====
    bool IInfinite<RtnX>.SupportsInfinity => false;
    RtnX? IInfinite<RtnX>.PositiveInfinity => null;
    RtnX? IInfinite<RtnX>.NegativeInfinityValue => null;
    bool IInfinite<RtnX>.IsPositiveInfinity(RtnX value) => false;
    bool IInfinite<RtnX>.IsNegativeInfinity(RtnX value) => false;

    // ===== IFixed (explicit) =====
    bool IFixed<RtnX>.IsFixed => false;
    int? IFixed<RtnX>.FixedDigits => null;
    RtnX? IFixed<RtnX>.FixedPrecision => null;

    // ===== IEpsilon (explicit) =====
    RtnX? IEpsilon<RtnX>.PrecisionEpsilon => null;

    // ===== IRealNumber =====
    bool IRealNumber<RtnX>.IsPositiveInfinity() => false;
    bool IRealNumber<RtnX>.IsNegativeInfinity() => false;
    bool IRealNumber<RtnX>.IsSelfWithinBounds() => true;
    RtnX IRealNumber<RtnX>.ClampSelfToBounds() => this;
    public Flt64 ToFlt64() => new(ToDouble());
    public Flt32 ToFlt32() => new((float)ToDouble());
    public FltX ToFltX() => new((decimal)ToDouble());
    public Int64 ToInt64() => new((long)ToDouble());
    public Int32 ToInt32() => new((int)ToDouble());

    // ===== IHasZero/IHasOne (from IFlt64ValueConverter, explicit) =====
    RtnX IHasZero<RtnX>.Zero => new(0, 1);
    RtnX IHasOne<RtnX>.One => new(1, 1);

    // ===== IFlt64ValueConverter (explicit) =====
    RtnX IFlt64ValueConverter<RtnX>.IntoValue(Flt64 value) => new(value.Value, 1);
    Flt64 IFlt64ValueConverter<RtnX>.FromValue(RtnX value) => value.ToFlt64();

    // ===== Operators =====
    public static RtnX operator +(RtnX l, RtnX r) => l.Plus(r);
    public static RtnX operator -(RtnX l, RtnX r) => l.Minus(r);
    public static RtnX operator *(RtnX l, RtnX r) => l.Times(r);
    public static RtnX operator /(RtnX l, RtnX r) => l.Div(r);
    public static RtnX operator %(RtnX l, RtnX r) => l.Rem(r);
    public static RtnX operator -(RtnX v) => v.Negate();
    public static RtnX operator ++(RtnX v) => v.Increment();
    public static RtnX operator --(RtnX v) => v.Decrement();
    public static bool operator ==(RtnX l, RtnX r) => l.Eq(r);
    public static bool operator !=(RtnX l, RtnX r) => !l.Eq(r);
    public static bool operator <(RtnX l, RtnX r) => l.Ls(r);
    public static bool operator >(RtnX l, RtnX r) => l.Gr(r);
    public static bool operator <=(RtnX l, RtnX r) => l.Leq(r);
    public static bool operator >=(RtnX l, RtnX r) => l.Geq(r);

    // ===== IRangeTo =====
    public Range RangeTo(RtnX rhs) => new((int)ToDouble(), (int)rhs.ToDouble());
    public Range Until(RtnX rhs) => new((int)ToDouble(), (int)rhs.ToDouble());

    // ===== Object =====
    public override string ToString() => Denominator == 1 ? $"{Numerator}" : $"{Numerator}/{Denominator}";
    public override bool Equals(object? obj) => obj is RtnX other && Eq(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    bool IEquatable<RtnX>.Equals(RtnX other) => Eq(other);
}

// ===== RtnX Constants =====

internal sealed class RtnXConstants : IFloatingNumberConstants<RtnX>, INumericConstants<RtnX> {
    public static readonly RtnXConstants Instance = new();
    public RtnX Zero => new(0, 1);
    public RtnX One => new(1, 1);
    public RtnX Two => new(2, 1);
    public RtnX Three => new(3, 1);
    public RtnX Five => new(5, 1);
    public RtnX Ten => new(10, 1);
    public RtnX Half => new(1, 2);
    public RtnX Minimum => new(long.MinValue, 1);
    public RtnX Maximum => new(long.MaxValue, 1);
    public RtnX PositiveMinimum => One;
    public int? DecimalDigits => null;
    public RtnX DecimalPrecision => new(1e-18, 1);
    public RtnX Epsilon => DecimalPrecision;
    public RtnX Pi => new(global::System.Math.PI, 1);
    public RtnX E => new(global::System.Math.E, 1);
    public RtnX Lg2 => new(global::System.Math.Log(2), 1);
    // IHasFixedPrecision explicit
    RtnX? IHasFixedPrecision<RtnX>.DecimalPrecision => DecimalPrecision;
    RtnX? IHasFixedPrecision<RtnX>.Epsilon => Epsilon;
    // IHasInfinity explicit
    RtnX? IHasInfinity<RtnX>.Infinity => null;
    RtnX? IHasInfinity<RtnX>.NegativeInfinity => null;
    // IHasNaN explicit
    RtnX? IHasNaN<RtnX>.NaN => null;
    // INumericConstants nullable explicit
    RtnX? INumericConstants<RtnX>.Half => Half;
    RtnX? INumericConstants<RtnX>.PositiveInfinity => null;
    RtnX? INumericConstants<RtnX>.NegativeInfinity => null;
    RtnX? INumericConstants<RtnX>.NaN => null;
    RtnX? INumericConstants<RtnX>.Epsilon => Epsilon;
    RtnX? INumericConstants<RtnX>.DecimalPrecision => DecimalPrecision;
    RtnX? INumericConstants<RtnX>.Pi => Pi;
    RtnX? INumericConstants<RtnX>.E => E;
    RtnX? INumericConstants<RtnX>.Lg2 => Lg2;
    public bool IsNaN(RtnX v) => false;
}

// ===== URtn8 (backward compat stub) =====

/// <summary>
/// 无符号有理数 / Unsigned rational number
/// </summary>
public readonly struct URtn8 : IUIntegerNumber<URtn8>, ICopyable<URtn8>, IEquatable<URtn8>, IComparable<URtn8>,
    IRem<URtn8, URtn8> {
    internal readonly UInt8 Numerator;
    internal readonly UInt8 Denominator;

    public URtn8(UInt8 numerator, UInt8 denominator) {
        Numerator = numerator;
        Denominator = denominator.Value == 0 ? UInt8.One : denominator;
    }

    public static readonly URtn8 Zero = new(UInt8.Zero, UInt8.One);
    public static readonly URtn8 One = new(UInt8.One, UInt8.One);

    IArithmeticConstants<URtn8> IArithmetic<URtn8>.Constants => URtn8Constants.Instance;
    IRealNumberConstants<URtn8> IRealNumber<URtn8>.Constants => URtn8Constants.Instance;
    public URtn8 Copy() => new(Numerator, Denominator);
    URtn8 IInvariant<URtn8>.Value() => this;
    public URtn8 Plus(URtn8 rhs) => new(new UInt8((byte)(Numerator.Value * rhs.Denominator.Value + rhs.Numerator.Value * Denominator.Value)), new UInt8((byte)(Denominator.Value * rhs.Denominator.Value)));
    public URtn8 Minus(URtn8 rhs) => new(new UInt8((byte)(Numerator.Value * rhs.Denominator.Value - rhs.Numerator.Value * Denominator.Value)), new UInt8((byte)(Denominator.Value * rhs.Denominator.Value)));
    public URtn8 Times(URtn8 rhs) => new(new UInt8((byte)(Numerator.Value * rhs.Numerator.Value)), new UInt8((byte)(Denominator.Value * rhs.Denominator.Value)));
    public URtn8 Div(URtn8 rhs) => new(new UInt8((byte)(Numerator.Value * rhs.Denominator.Value)), new UInt8((byte)(Denominator.Value * rhs.Numerator.Value)));
    public URtn8 Rem(URtn8 rhs) => new(new UInt8((byte)(Numerator.Value % rhs.Numerator.Value)), Denominator);
    public URtn8 IntDiv(URtn8 rhs) => new(new UInt8((byte)((Numerator.Value * rhs.Denominator.Value) / (Denominator.Value * rhs.Numerator.Value))), UInt8.One);
    public URtn8 Negate() => this;
    public URtn8 Abs() => this;
    public URtn8 Reciprocal() => new(Denominator, Numerator);
    public URtn8 Increment() => new(new UInt8((byte)(Numerator.Value + Denominator.Value)), Denominator);
    public URtn8 Decrement() => new(new UInt8((byte)(Numerator.Value - Denominator.Value)), Denominator);
    public URtn8 Cross(URtn8 rhs) => Times(rhs);
    public URtn8 Pow(int index) => new(new UInt8((byte)global::System.Math.Pow(Numerator.Value, index)), new UInt8((byte)global::System.Math.Pow(Denominator.Value, index)));
    public URtn8 Sqr() => new(new UInt8((byte)(Numerator.Value * Numerator.Value)), new UInt8((byte)(Denominator.Value * Denominator.Value)));
    public URtn8 Cub() => Times(this).Times(this);
    private double ToDouble() => (double)Numerator.Value / Denominator.Value;
    public Order Ord(URtn8 rhs) => ToDouble().CompareTo(rhs.ToDouble()) < 0 ? new Order.Less() : ToDouble() > rhs.ToDouble() ? new Order.Greater() : new Order.Equal();
    public Order? PartialOrd(URtn8 rhs) => Ord(rhs);
    public bool Eq(URtn8 rhs) => Numerator.Value * rhs.Denominator.Value == rhs.Numerator.Value * Denominator.Value;
    public bool? PartialEq(URtn8 rhs) => Eq(rhs);
    public int CompareTo(URtn8 other) => ToDouble().CompareTo(other.ToDouble());
    public bool Ls(URtn8 rhs) => Numerator.Value * rhs.Denominator.Value < rhs.Numerator.Value * Denominator.Value;
    public bool Leq(URtn8 rhs) => Numerator.Value * rhs.Denominator.Value <= rhs.Numerator.Value * Denominator.Value;
    public bool Gr(URtn8 rhs) => Numerator.Value * rhs.Denominator.Value > rhs.Numerator.Value * Denominator.Value;
    public bool Geq(URtn8 rhs) => Numerator.Value * rhs.Denominator.Value >= rhs.Numerator.Value * Denominator.Value;
    public bool Equiv(URtn8 rhs) => Eq(rhs);
    bool IBounded<URtn8>.IsBounded => true;
    URtn8? IBounded<URtn8>.MinBound => Zero;
    URtn8? IBounded<URtn8>.MaxBound => null;
    bool IBounded<URtn8>.IsWithinBounds(URtn8 value) => true;
    URtn8 IBounded<URtn8>.ClampToBounds(URtn8 value) => value;
    bool IInfinite<URtn8>.SupportsInfinity => false;
    URtn8? IInfinite<URtn8>.PositiveInfinity => null;
    URtn8? IInfinite<URtn8>.NegativeInfinityValue => null;
    bool IInfinite<URtn8>.IsPositiveInfinity(URtn8 value) => false;
    bool IInfinite<URtn8>.IsNegativeInfinity(URtn8 value) => false;
    bool IFixed<URtn8>.IsFixed => false;
    int? IFixed<URtn8>.FixedDigits => null;
    URtn8? IFixed<URtn8>.FixedPrecision => null;
    URtn8? IEpsilon<URtn8>.PrecisionEpsilon => null;
    bool IRealNumber<URtn8>.IsPositiveInfinity() => false;
    bool IRealNumber<URtn8>.IsNegativeInfinity() => false;
    bool IRealNumber<URtn8>.IsSelfWithinBounds() => true;
    URtn8 IRealNumber<URtn8>.ClampSelfToBounds() => this;
    public Flt64 ToFlt64() => new(ToDouble());
    public Flt32 ToFlt32() => new((float)ToDouble());
    public FltX ToFltX() => new((decimal)ToDouble());
    public Int64 ToInt64() => new((long)ToDouble());
    public Int32 ToInt32() => new((int)ToDouble());
    public Range RangeTo(URtn8 rhs) => new((int)ToDouble(), (int)rhs.ToDouble());
    public Range Until(URtn8 rhs) => new((int)ToDouble(), (int)rhs.ToDouble());
    public static URtn8 operator +(URtn8 l, URtn8 r) => l.Plus(r);
    public static URtn8 operator -(URtn8 l, URtn8 r) => l.Minus(r);
    public static URtn8 operator *(URtn8 l, URtn8 r) => l.Times(r);
    public static URtn8 operator /(URtn8 l, URtn8 r) => l.Div(r);
    public static URtn8 operator %(URtn8 l, URtn8 r) => l.Rem(r);
    public static bool operator ==(URtn8 l, URtn8 r) => l.Eq(r);
    public static bool operator !=(URtn8 l, URtn8 r) => !l.Eq(r);
    public static bool operator <(URtn8 l, URtn8 r) => l.Ls(r);
    public static bool operator >(URtn8 l, URtn8 r) => l.Gr(r);
    public static bool operator <=(URtn8 l, URtn8 r) => l.Leq(r);
    public static bool operator >=(URtn8 l, URtn8 r) => l.Geq(r);
    public override string ToString() => Denominator.Value == 1 ? $"{Numerator}" : $"{Numerator}/{Denominator}";
    public override bool Equals(object? obj) => obj is URtn8 other && Eq(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    bool IEquatable<URtn8>.Equals(URtn8 other) => Eq(other);
}

internal sealed class URtn8Constants : IRealNumberConstants<URtn8>, INumericConstants<URtn8> {
    public static readonly URtn8Constants Instance = new();
    public URtn8 Zero => new(UInt8.Zero, UInt8.One);
    public URtn8 One => new(UInt8.One, UInt8.One);
    public URtn8 Two => new(new UInt8(2), UInt8.One);
    public URtn8 Three => new(new UInt8(3), UInt8.One);
    public URtn8 Five => new(new UInt8(5), UInt8.One);
    public URtn8 Ten => new(new UInt8(10), UInt8.One);
    public URtn8 Minimum => Zero;
    public URtn8 Maximum => new(new UInt8(byte.MaxValue), UInt8.One);
    public URtn8 PositiveMinimum => One;
    public int? DecimalDigits => null;
    public URtn8 DecimalPrecision => Zero;
    public URtn8 Epsilon => Zero;
    URtn8? INumericConstants<URtn8>.Half => null;
    URtn8? INumericConstants<URtn8>.PositiveInfinity => null;
    URtn8? INumericConstants<URtn8>.NegativeInfinity => null;
    URtn8? INumericConstants<URtn8>.NaN => null;
    URtn8? INumericConstants<URtn8>.Epsilon => Zero;
    URtn8? INumericConstants<URtn8>.DecimalPrecision => Zero;
    URtn8? INumericConstants<URtn8>.Pi => null;
    URtn8? INumericConstants<URtn8>.E => null;
    URtn8? INumericConstants<URtn8>.Lg2 => null;
    public bool IsNaN(URtn8 v) => false;
}
