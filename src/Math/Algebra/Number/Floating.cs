#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Math.Algebra.Number;
// ===== Flt32 Constants =====

internal sealed class Flt32Constants : IFloatingNumberConstants<Flt32>, INumericConstants<Flt32> {
    public static readonly Flt32Constants Instance = new();
    public Flt32 Zero => new(0f);
    public Flt32 One => new(1f);
    public Flt32 Two => new(2f);
    public Flt32 Three => new(3f);
    public Flt32 Five => new(5f);
    public Flt32 Ten => new(10f);
    public Flt32 Half => new(0.5f);
    public Flt32 Minimum => new(-float.MaxValue);
    public Flt32 Maximum => new(float.MaxValue);
    public Flt32 PositiveMinimum => Epsilon;
    public int? DecimalDigits => 6;
    public Flt32 DecimalPrecision => new(1.19209e-07f);
    public Flt32 Epsilon => new(float.Epsilon);
    // IHasFixedPrecision explicit (nullable returns)
    Flt32? IHasFixedPrecision<Flt32>.DecimalPrecision => DecimalPrecision;
    Flt32? IHasFixedPrecision<Flt32>.Epsilon => Epsilon;
    public Flt32 Pi => new((float)global::System.Math.PI);
    public Flt32 E => new((float)global::System.Math.E);
    public Flt32 Lg2 => new((float)global::System.Math.Log(2));
    // IHasInfinity explicit
    Flt32? IHasInfinity<Flt32>.Infinity => new(float.PositiveInfinity);
    Flt32? IHasInfinity<Flt32>.NegativeInfinity => new(float.NegativeInfinity);
    // IHasNaN explicit
    Flt32? IHasNaN<Flt32>.NaN => new(float.NaN);
    // INumericConstants nullable explicit
    Flt32? INumericConstants<Flt32>.Half => Half;
    Flt32? INumericConstants<Flt32>.PositiveInfinity => new(float.PositiveInfinity);
    Flt32? INumericConstants<Flt32>.NegativeInfinity => new(float.NegativeInfinity);
    Flt32? INumericConstants<Flt32>.NaN => new(float.NaN);
    Flt32? INumericConstants<Flt32>.Epsilon => Epsilon;
    Flt32? INumericConstants<Flt32>.DecimalPrecision => DecimalPrecision;
    Flt32? INumericConstants<Flt32>.Pi => Pi;
    Flt32? INumericConstants<Flt32>.E => E;
    Flt32? INumericConstants<Flt32>.Lg2 => Lg2;
    public bool IsNaN(Flt32 v) => float.IsNaN(v.Value);
}

// ===== Flt64 Constants =====

internal sealed class Flt64Constants : IFloatingNumberConstants<Flt64>, INumericConstants<Flt64>, IFlt64ValueConverter<Flt64> {
    public static readonly Flt64Constants Instance = new();
    public Flt64 Zero => new(0.0);
    public Flt64 One => new(1.0);
    public Flt64 Two => new(2.0);
    public Flt64 Three => new(3.0);
    public Flt64 Five => new(5.0);
    public Flt64 Ten => new(10.0);
    public Flt64 Half => new(0.5);
    public Flt64 Minimum => new(-double.MaxValue);
    public Flt64 Maximum => new(double.MaxValue);
    public Flt64 PositiveMinimum => Epsilon;
    public int? DecimalDigits => 15;
    public Flt64 DecimalPrecision => new(2.22045e-16);
    public Flt64 Epsilon => new(double.Epsilon);
    // IHasFixedPrecision explicit (nullable returns)
    Flt64? IHasFixedPrecision<Flt64>.DecimalPrecision => DecimalPrecision;
    Flt64? IHasFixedPrecision<Flt64>.Epsilon => Epsilon;
    public Flt64 Pi => new(global::System.Math.PI);
    public Flt64 E => new(global::System.Math.E);
    public Flt64 Lg2 => new(global::System.Math.Log(2));
    // IHasInfinity explicit
    Flt64? IHasInfinity<Flt64>.Infinity => new(double.PositiveInfinity);
    Flt64? IHasInfinity<Flt64>.NegativeInfinity => new(double.NegativeInfinity);
    // IHasNaN explicit
    Flt64? IHasNaN<Flt64>.NaN => new(double.NaN);
    // INumericConstants nullable explicit
    Flt64? INumericConstants<Flt64>.Half => Half;
    Flt64? INumericConstants<Flt64>.PositiveInfinity => new(double.PositiveInfinity);
    Flt64? INumericConstants<Flt64>.NegativeInfinity => new(double.NegativeInfinity);
    Flt64? INumericConstants<Flt64>.NaN => new(double.NaN);
    Flt64? INumericConstants<Flt64>.Epsilon => Epsilon;
    Flt64? INumericConstants<Flt64>.DecimalPrecision => DecimalPrecision;
    Flt64? INumericConstants<Flt64>.Pi => Pi;
    Flt64? INumericConstants<Flt64>.E => E;
    Flt64? INumericConstants<Flt64>.Lg2 => Lg2;
    public bool IsNaN(Flt64 v) => double.IsNaN(v.Value);
    // IFlt64ValueConverter
    Flt64 IFlt64ValueConverter<Flt64>.IntoValue(Flt64 value) => value;
    Flt64 IFlt64ValueConverter<Flt64>.FromValue(Flt64 value) => value;
}

// ===== FltX Constants =====

internal sealed class FltXConstants : IFloatingNumberConstants<FltX>, INumericConstants<FltX>, IFlt64ValueConverter<FltX> {
    public static readonly FltXConstants Instance = new();
    public FltX Zero => new(0m);
    public FltX One => new(1m);
    public FltX Two => new(2m);
    public FltX Three => new(3m);
    public FltX Five => new(5m);
    public FltX Ten => new(10m);
    public FltX Half => new(0.5m);
    public FltX Minimum => new(decimal.MinValue);
    public FltX Maximum => new(decimal.MaxValue);
    public FltX PositiveMinimum => Epsilon;
    public int? DecimalDigits => 18;
    public FltX DecimalPrecision => new(1e-18m);
    public FltX Epsilon => DecimalPrecision;
    // IHasFixedPrecision explicit (nullable returns)
    FltX? IHasFixedPrecision<FltX>.DecimalPrecision => DecimalPrecision;
    FltX? IHasFixedPrecision<FltX>.Epsilon => Epsilon;
    public FltX Pi => new((decimal)global::System.Math.PI);
    public FltX E => new((decimal)global::System.Math.E);
    public FltX Lg2 => new((decimal)global::System.Math.Log(2));
    // IHasInfinity explicit (decimal has no infinity)
    FltX? IHasInfinity<FltX>.Infinity => null;
    FltX? IHasInfinity<FltX>.NegativeInfinity => null;
    // IHasNaN explicit (decimal has no NaN)
    FltX? IHasNaN<FltX>.NaN => null;
    // INumericConstants nullable explicit
    FltX? INumericConstants<FltX>.Half => Half;
    FltX? INumericConstants<FltX>.PositiveInfinity => null;
    FltX? INumericConstants<FltX>.NegativeInfinity => null;
    FltX? INumericConstants<FltX>.NaN => null;
    FltX? INumericConstants<FltX>.Epsilon => Epsilon;
    FltX? INumericConstants<FltX>.DecimalPrecision => DecimalPrecision;
    FltX? INumericConstants<FltX>.Pi => Pi;
    FltX? INumericConstants<FltX>.E => E;
    FltX? INumericConstants<FltX>.Lg2 => Lg2;
    public bool IsNaN(FltX v) => false;
    // IFlt64ValueConverter
    FltX IFlt64ValueConverter<FltX>.IntoValue(Flt64 value) => new((decimal)value.Value);
    Flt64 IFlt64ValueConverter<FltX>.FromValue(FltX value) => new((double)value.Value);
}

// ===== Flt32 =====

/// <summary>
/// 32位浮点数 / 32-bit floating-point number
/// </summary>
public readonly struct Flt32 : IFloatingNumber<Flt32>, ICopyable<Flt32>, IEquatable<Flt32>, IComparable<Flt32>,
    IRem<Flt32, Flt32> {
    internal readonly float Value;
    public Flt32(float value) { Value = value; }

    // ===== Interface properties (explicit) =====
    IArithmeticConstants<Flt32> IArithmetic<Flt32>.Constants => Flt32Constants.Instance;
    IRealNumberConstants<Flt32> IRealNumber<Flt32>.Constants => Flt32Constants.Instance;
    IFloatingNumberConstants<Flt32> IFloatingNumber<Flt32>.Constants => Flt32Constants.Instance;

    // ===== ICopyable =====
    public Flt32 Copy() => new(Value);

    // ===== IInvariant (explicit) =====
    Flt32 IInvariant<Flt32>.Value() => this;

    // ===== Arithmetic =====
    public Flt32 Plus(Flt32 rhs) => new(Value + rhs.Value);
    public Flt32 Minus(Flt32 rhs) => new(Value - rhs.Value);
    public Flt32 Times(Flt32 rhs) => new(Value * rhs.Value);
    public Flt32 Div(Flt32 rhs) => new(Value / rhs.Value);
    public Flt32 Rem(Flt32 rhs) => new(Value % rhs.Value);
    public Flt32 IntDiv(Flt32 rhs) => new((float)global::System.Math.Floor(Value / rhs.Value));
    public Flt32 Negate() => new(-Value);
    public Flt32 Abs() => new(global::System.Math.Abs(Value));
    public Flt32 Reciprocal() => new(1f / Value);
    public Flt32 Increment() => new(Value + 1f);
    public Flt32 Decrement() => new(Value - 1f);
    public Flt32 Cross(Flt32 rhs) => Times(rhs);

    // ===== IPow =====
    public Flt32 Pow(int index) => new((float)global::System.Math.Pow(Value, index));
    public Flt32 Sqr() => new(Value * Value);
    public Flt32 Cub() => new(Value * Value * Value);

    // ===== IFloatingNumber extras =====
    public Flt32 Sqrt() => new((float)global::System.Math.Sqrt(Value));
    public Flt32 Acos() => new((float)global::System.Math.Acos(Value));
    public static Flt32 FromInt32(int value) => new(value);

    // ===== Trigonometric =====
    public Flt32 Sin() => new((float)global::System.Math.Sin(Value));
    public Flt32 Cos() => new((float)global::System.Math.Cos(Value));
    public Flt32? Tan() { Flt32 c = Cos(); return c.Equiv(Flt32Constants.Instance.Zero) ? null : new((float)global::System.Math.Tan(Value)); }
    public Flt32? Sec() { Flt32 c = Cos(); return c.Equiv(Flt32Constants.Instance.Zero) ? null : c.Reciprocal(); }
    public Flt32? Csc() { Flt32 s = Sin(); return s.Equiv(Flt32Constants.Instance.Zero) ? null : s.Reciprocal(); }
    public Flt32? Cot() { Flt32 s = Sin(); return s.Equiv(Flt32Constants.Instance.Zero) ? null : Cos() / s; }
    public Flt32? Asin() => Value < -1f || Value > 1f ? null : new((float)global::System.Math.Asin(Value));
    public Flt32 Atan() => new((float)global::System.Math.Atan(Value));
    public Flt32? Asec() => Equiv(Flt32Constants.Instance.Zero) ? null : Reciprocal().Acos();
    public Flt32? Acsc() => Equiv(Flt32Constants.Instance.Zero) ? null : Reciprocal().Asin();
    public Flt32? Acot() => Equiv(Flt32Constants.Instance.Zero) ? null : Reciprocal().Atan();

    // ===== Hyperbolic =====
    public Flt32 Sinh() => new((float)global::System.Math.Sinh(Value));
    public Flt32 Cosh() => new((float)global::System.Math.Cosh(Value));
    public Flt32 Sech() => Cosh().Reciprocal();
    public Flt32? Csch() => Equiv(Flt32Constants.Instance.Zero) ? null : Sinh().Reciprocal();
    public Flt32 Tanh() => new((float)global::System.Math.Tanh(Value));
    public Flt32? Coth() => Equiv(Flt32Constants.Instance.Zero) ? null : new(1f / (float)global::System.Math.Tanh(Value));
    public Flt32 Asinh() => new((float)global::System.Math.Asinh(Value));
    public Flt32? Acosh() => Value < 1f ? null : new((float)global::System.Math.Acosh(Value));
    public Flt32? Asech() => Equiv(Flt32Constants.Instance.Zero) ? null : Reciprocal().Acosh();
    public Flt32? Acsch() => Equiv(Flt32Constants.Instance.Zero) ? null : Reciprocal().Asinh();
    public Flt32? Atanh() => Value <= -1f || Value >= 1f ? null : new((float)global::System.Math.Atanh(Value));
    public Flt32? Acoth() => Equiv(Flt32Constants.Instance.Zero) ? null : Reciprocal().Atanh();

    // ===== Exponential / Logarithmic =====
    public Flt32 Exp() => new((float)global::System.Math.Exp(Value));
    public Flt32? Ln() => Value <= 0f ? null : new((float)global::System.Math.Log(Value));
    public Flt32? Lg() => Value <= 0f ? null : new((float)global::System.Math.Log10(Value));
    public Flt32? Lg2() => Value <= 0f ? null : new((float)global::System.Math.Log2(Value));
    public Flt32? Log(Flt32 baseValue) => Value <= 0f || baseValue.Value <= 0f || baseValue.Equiv(Flt32Constants.Instance.One) ? null : new((float)(global::System.Math.Log(Value) / global::System.Math.Log(baseValue.Value)));

    // ===== Rounding =====
    public Flt32 Floor() => new((float)global::System.Math.Floor(Value));
    public Flt32 Ceil() => new((float)global::System.Math.Ceiling(Value));
    public Flt32 Round() => new((float)global::System.Math.Round(Value));
    public Flt32 Trunc() => new((float)global::System.Math.Truncate(Value));
    public Flt32 BankerRound() => new((float)global::System.Math.Round(Value, MidpointRounding.ToEven));
    public Flt32 FloorTo(int precision) => new((float)(global::System.Math.Floor(Value * global::System.Math.Pow(10, precision)) / global::System.Math.Pow(10, precision)));
    public Flt32 CeilTo(int precision) => new((float)(global::System.Math.Ceiling(Value * global::System.Math.Pow(10, precision)) / global::System.Math.Pow(10, precision)));
    public Flt32 RoundTo(int precision) => new((float)(global::System.Math.Round(Value * global::System.Math.Pow(10, precision)) / global::System.Math.Pow(10, precision)));
    public Flt32 TruncTo(int precision) => new((float)(global::System.Math.Truncate(Value * global::System.Math.Pow(10, precision)) / global::System.Math.Pow(10, precision)));
    public Flt32 BankerRoundTo(int precision) => new((float)(global::System.Math.Round(Value * global::System.Math.Pow(10, precision), MidpointRounding.ToEven) / global::System.Math.Pow(10, precision)));

    // ===== Type Conversions =====
    public Int8 ToInt8() => new((sbyte)(int)Value);
    public Int16 ToInt16() => new((short)(int)Value);
    public UInt8 ToUInt8() => new((byte)(uint)Value);
    public UInt16 ToUInt16() => new((ushort)(uint)Value);
    public UInt32 ToUInt32() => new((uint)Value);
    public UInt64 ToUInt64() => new((ulong)Value);
    public IntX ToIntX() => new((long)Value);
    public UIntX ToUIntX() => new((long)(ulong)Value);

    // ===== Comparison =====
    public Order Ord(Flt32 rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
    public Order? PartialOrd(Flt32 rhs) => Ord(rhs);
    public bool Eq(Flt32 rhs) => Value == rhs.Value;
    public bool? PartialEq(Flt32 rhs) => Value == rhs.Value;
    public int CompareTo(Flt32 other) => Value.CompareTo(other.Value);
    public bool Ls(Flt32 rhs) => Value < rhs.Value;
    public bool Leq(Flt32 rhs) => Value <= rhs.Value;
    public bool Gr(Flt32 rhs) => Value > rhs.Value;
    public bool Geq(Flt32 rhs) => Value >= rhs.Value;
    public bool Equiv(Flt32 rhs) => global::System.Math.Abs(Value - rhs.Value) <= Flt32Constants.Instance.DecimalPrecision.Value;

    // ===== IBounded (explicit) =====
    bool IBounded<Flt32>.IsBounded => true;
    Flt32? IBounded<Flt32>.MinBound => Flt32Constants.Instance.Minimum;
    Flt32? IBounded<Flt32>.MaxBound => Flt32Constants.Instance.Maximum;
    bool IBounded<Flt32>.IsWithinBounds(Flt32 value) => value.Value >= -float.MaxValue && value.Value <= float.MaxValue;
    Flt32 IBounded<Flt32>.ClampToBounds(Flt32 value) => new(global::System.Math.Clamp(value.Value, -float.MaxValue, float.MaxValue));

    // ===== IInfinite (explicit) =====
    bool IInfinite<Flt32>.SupportsInfinity => true;
    Flt32? IInfinite<Flt32>.PositiveInfinity => new(float.PositiveInfinity);
    Flt32? IInfinite<Flt32>.NegativeInfinityValue => new(float.NegativeInfinity);
    bool IInfinite<Flt32>.IsPositiveInfinity(Flt32 value) => float.IsPositiveInfinity(value.Value);
    bool IInfinite<Flt32>.IsNegativeInfinity(Flt32 value) => float.IsNegativeInfinity(value.Value);

    // ===== IFixed (explicit) =====
    bool IFixed<Flt32>.IsFixed => false;
    int? IFixed<Flt32>.FixedDigits => null;
    Flt32? IFixed<Flt32>.FixedPrecision => null;

    // ===== IEpsilon (explicit) =====
    Flt32? IEpsilon<Flt32>.PrecisionEpsilon => Flt32Constants.Instance.Epsilon;

    // ===== IRealNumber =====
    bool IRealNumber<Flt32>.IsPositiveInfinity() => float.IsPositiveInfinity(Value);
    bool IRealNumber<Flt32>.IsNegativeInfinity() => float.IsNegativeInfinity(Value);
    bool IRealNumber<Flt32>.IsSelfWithinBounds() => !float.IsInfinity(Value) && !float.IsNaN(Value);
    Flt32 IRealNumber<Flt32>.ClampSelfToBounds() => float.IsNaN(Value) ? Flt32Constants.Instance.Zero : new(global::System.Math.Clamp(Value, -float.MaxValue, float.MaxValue));
    public Flt64 ToFlt64() => new(Value);
    public Flt32 ToFlt32() => this;
    public FltX ToFltX() => new((decimal)Value);
    public Int64 ToInt64() => new((long)Value);
    public Int32 ToInt32() => new((int)Value);

    // ===== Operators =====
    public static Flt32 operator +(Flt32 l, Flt32 r) => new(l.Value + r.Value);
    public static Flt32 operator -(Flt32 l, Flt32 r) => new(l.Value - r.Value);
    public static Flt32 operator *(Flt32 l, Flt32 r) => new(l.Value * r.Value);
    public static Flt32 operator /(Flt32 l, Flt32 r) => new(l.Value / r.Value);
    public static Flt32 operator %(Flt32 l, Flt32 r) => new(l.Value % r.Value);
    public static Flt32 operator -(Flt32 v) => new(-v.Value);
    public static Flt32 operator ++(Flt32 v) => new(v.Value + 1f);
    public static Flt32 operator --(Flt32 v) => new(v.Value - 1f);
    public static bool operator ==(Flt32 l, Flt32 r) => l.Value == r.Value;
    public static bool operator !=(Flt32 l, Flt32 r) => l.Value != r.Value;
    public static bool operator <(Flt32 l, Flt32 r) => l.Value < r.Value;
    public static bool operator >(Flt32 l, Flt32 r) => l.Value > r.Value;
    public static bool operator <=(Flt32 l, Flt32 r) => l.Value <= r.Value;
    public static bool operator >=(Flt32 l, Flt32 r) => l.Value >= r.Value;

    // ===== IRangeTo =====
    public Range RangeTo(Flt32 rhs) => new((int)Value, (int)rhs.Value);
    public Range Until(Flt32 rhs) => new((int)Value, (int)rhs.Value);

    // ===== Object =====
    public override string ToString() => Value.ToString();
    public override bool Equals(object? obj) => obj is Flt32 other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    bool IEquatable<Flt32>.Equals(Flt32 other) => Value == other.Value;
}

// ===== Flt64 — THE default scalar =====

/// <summary>
/// 64位浮点数（默认标量类型）/ 64-bit floating-point number (default scalar type)
/// </summary>
public readonly struct Flt64 : IFloatingNumber<Flt64>, ICopyable<Flt64>, IEquatable<Flt64>, IComparable<Flt64>,
    IRem<Flt64, Flt64>, IFlt64ValueConverter<Flt64> {
    internal readonly double Value;
    public Flt64(double value) { Value = value; }

    // ===== Static constants (backward compat) =====
    public static readonly Flt64 Zero = new(0.0);
    public static readonly Flt64 One = new(1.0);

    // ===== Interface properties (explicit) =====
    IArithmeticConstants<Flt64> IArithmetic<Flt64>.Constants => Flt64Constants.Instance;
    IRealNumberConstants<Flt64> IRealNumber<Flt64>.Constants => Flt64Constants.Instance;
    IFloatingNumberConstants<Flt64> IFloatingNumber<Flt64>.Constants => Flt64Constants.Instance;

    // ===== ICopyable =====
    public Flt64 Copy() => new(Value);

    // ===== IInvariant (explicit) =====
    Flt64 IInvariant<Flt64>.Value() => this;

    // ===== IHasZero/IHasOne (from IFlt64ValueConverter, explicit) =====
    Flt64 IHasZero<Flt64>.Zero => new(0.0);
    Flt64 IHasOne<Flt64>.One => new(1.0);

    // ===== Arithmetic =====
    public Flt64 Plus(Flt64 rhs) => new(Value + rhs.Value);
    public Flt64 Minus(Flt64 rhs) => new(Value - rhs.Value);
    public Flt64 Times(Flt64 rhs) => new(Value * rhs.Value);
    public Flt64 Div(Flt64 rhs) => new(Value / rhs.Value);
    public Flt64 Rem(Flt64 rhs) => new(Value % rhs.Value);
    public Flt64 IntDiv(Flt64 rhs) => new(global::System.Math.Floor(Value / rhs.Value));
    public Flt64 Negate() => new(-Value);
    public Flt64 Abs() => new(global::System.Math.Abs(Value));
    public Flt64 Reciprocal() => new(1.0 / Value);
    public Flt64 Increment() => new(Value + 1.0);
    public Flt64 Decrement() => new(Value - 1.0);
    public Flt64 Cross(Flt64 rhs) => Times(rhs);

    // ===== IPow =====
    public Flt64 Pow(int index) => new(global::System.Math.Pow(Value, index));
    public Flt64 Sqr() => new(Value * Value);
    public Flt64 Cub() => new(Value * Value * Value);

    // ===== IFloatingNumber extras =====
    public Flt64 Sqrt() => new(global::System.Math.Sqrt(Value));
    public Flt64 Acos() => new(global::System.Math.Acos(Value));
    public static Flt64 FromInt32(int value) => new(value);
    public double ToDouble() => Value;

    // ===== Trigonometric =====
    public Flt64 Sin() => new(global::System.Math.Sin(Value));
    public Flt64 Cos() => new(global::System.Math.Cos(Value));
    public Flt64? Tan() { Flt64 c = Cos(); return c.Equiv(Flt64Constants.Instance.Zero) ? null : new(global::System.Math.Tan(Value)); }
    public Flt64? Sec() { Flt64 c = Cos(); return c.Equiv(Flt64Constants.Instance.Zero) ? null : c.Reciprocal(); }
    public Flt64? Csc() { Flt64 s = Sin(); return s.Equiv(Flt64Constants.Instance.Zero) ? null : s.Reciprocal(); }
    public Flt64? Cot() { Flt64 s = Sin(); return s.Equiv(Flt64Constants.Instance.Zero) ? null : Cos() / s; }
    public Flt64? Asin() => Value < -1.0 || Value > 1.0 ? null : new(global::System.Math.Asin(Value));
    public Flt64 Atan() => new(global::System.Math.Atan(Value));
    public Flt64? Asec() => Equiv(Flt64Constants.Instance.Zero) ? null : Reciprocal().Acos();
    public Flt64? Acsc() => Equiv(Flt64Constants.Instance.Zero) ? null : Reciprocal().Asin();
    public Flt64? Acot() => Equiv(Flt64Constants.Instance.Zero) ? null : Reciprocal().Atan();

    // ===== Hyperbolic =====
    public Flt64 Sinh() => new(global::System.Math.Sinh(Value));
    public Flt64 Cosh() => new(global::System.Math.Cosh(Value));
    public Flt64 Sech() => Cosh().Reciprocal();
    public Flt64? Csch() => Equiv(Flt64Constants.Instance.Zero) ? null : Sinh().Reciprocal();
    public Flt64 Tanh() => new(global::System.Math.Tanh(Value));
    public Flt64? Coth() => Equiv(Flt64Constants.Instance.Zero) ? null : new(1.0 / global::System.Math.Tanh(Value));
    public Flt64 Asinh() => new(global::System.Math.Asinh(Value));
    public Flt64? Acosh() => Value < 1.0 ? null : new(global::System.Math.Acosh(Value));
    public Flt64? Asech() => Equiv(Flt64Constants.Instance.Zero) ? null : Reciprocal().Acosh();
    public Flt64? Acsch() => Equiv(Flt64Constants.Instance.Zero) ? null : Reciprocal().Asinh();
    public Flt64? Atanh() => Value <= -1.0 || Value >= 1.0 ? null : new(global::System.Math.Atanh(Value));
    public Flt64? Acoth() => Equiv(Flt64Constants.Instance.Zero) ? null : Reciprocal().Atanh();

    // ===== Exponential / Logarithmic =====
    public Flt64 Exp() => new(global::System.Math.Exp(Value));
    public Flt64? Ln() => Value <= 0.0 ? null : new(global::System.Math.Log(Value));
    public Flt64? Lg() => Value <= 0.0 ? null : new(global::System.Math.Log10(Value));
    public Flt64? Lg2() => Value <= 0.0 ? null : new(global::System.Math.Log2(Value));
    public Flt64? Log(Flt64 baseValue) => Value <= 0.0 || baseValue.Value <= 0.0 || baseValue.Equiv(Flt64Constants.Instance.One) ? null : new(global::System.Math.Log(Value) / global::System.Math.Log(baseValue.Value));

    // ===== Rounding =====
    public Flt64 Floor() => new(global::System.Math.Floor(Value));
    public Flt64 Ceil() => new(global::System.Math.Ceiling(Value));
    public Flt64 Round() => new(global::System.Math.Round(Value));
    public Flt64 Trunc() => new(global::System.Math.Truncate(Value));
    public Flt64 BankerRound() => new(global::System.Math.Round(Value, MidpointRounding.ToEven));
    public Flt64 FloorTo(int precision) => new(global::System.Math.Floor(Value * global::System.Math.Pow(10, precision)) / global::System.Math.Pow(10, precision));
    public Flt64 CeilTo(int precision) => new(global::System.Math.Ceiling(Value * global::System.Math.Pow(10, precision)) / global::System.Math.Pow(10, precision));
    public Flt64 RoundTo(int precision) => new(global::System.Math.Round(Value * global::System.Math.Pow(10, precision)) / global::System.Math.Pow(10, precision));
    public Flt64 TruncTo(int precision) => new(global::System.Math.Truncate(Value * global::System.Math.Pow(10, precision)) / global::System.Math.Pow(10, precision));
    public Flt64 BankerRoundTo(int precision) => new(global::System.Math.Round(Value * global::System.Math.Pow(10, precision), MidpointRounding.ToEven) / global::System.Math.Pow(10, precision));

    // ===== Type Conversions =====
    public Int8 ToInt8() => new((sbyte)(int)Value);
    public Int16 ToInt16() => new((short)(int)Value);
    public UInt8 ToUInt8() => new((byte)(uint)Value);
    public UInt16 ToUInt16() => new((ushort)(uint)Value);
    public UInt32 ToUInt32() => new((uint)Value);
    public UInt64 ToUInt64() => new((ulong)Value);
    public IntX ToIntX() => new((long)Value);
    public UIntX ToUIntX() => new((long)(ulong)Value);

    // ===== Comparison =====
    public Order Ord(Flt64 rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
    public Order? PartialOrd(Flt64 rhs) => Ord(rhs);
    public bool Eq(Flt64 rhs) => Value == rhs.Value;
    public bool? PartialEq(Flt64 rhs) => Value == rhs.Value;
    public int CompareTo(Flt64 other) => Value.CompareTo(other.Value);
    public bool Ls(Flt64 rhs) => Value < rhs.Value;
    public bool Leq(Flt64 rhs) => Value <= rhs.Value;
    public bool Gr(Flt64 rhs) => Value > rhs.Value;
    public bool Geq(Flt64 rhs) => Value >= rhs.Value;
    public bool Equiv(Flt64 rhs) => global::System.Math.Abs(Value - rhs.Value) <= Flt64Constants.Instance.DecimalPrecision.Value;

    // ===== IBounded (explicit) =====
    bool IBounded<Flt64>.IsBounded => true;
    Flt64? IBounded<Flt64>.MinBound => Flt64Constants.Instance.Minimum;
    Flt64? IBounded<Flt64>.MaxBound => Flt64Constants.Instance.Maximum;
    bool IBounded<Flt64>.IsWithinBounds(Flt64 value) => value.Value >= -double.MaxValue && value.Value <= double.MaxValue;
    Flt64 IBounded<Flt64>.ClampToBounds(Flt64 value) => new(global::System.Math.Clamp(value.Value, -double.MaxValue, double.MaxValue));

    // ===== IInfinite (explicit) =====
    bool IInfinite<Flt64>.SupportsInfinity => true;
    Flt64? IInfinite<Flt64>.PositiveInfinity => new(double.PositiveInfinity);
    Flt64? IInfinite<Flt64>.NegativeInfinityValue => new(double.NegativeInfinity);
    bool IInfinite<Flt64>.IsPositiveInfinity(Flt64 value) => double.IsPositiveInfinity(value.Value);
    bool IInfinite<Flt64>.IsNegativeInfinity(Flt64 value) => double.IsNegativeInfinity(value.Value);

    // ===== IFixed (explicit) =====
    bool IFixed<Flt64>.IsFixed => false;
    int? IFixed<Flt64>.FixedDigits => null;
    Flt64? IFixed<Flt64>.FixedPrecision => null;

    // ===== IEpsilon (explicit) =====
    Flt64? IEpsilon<Flt64>.PrecisionEpsilon => Flt64Constants.Instance.Epsilon;

    // ===== IRealNumber =====
    bool IRealNumber<Flt64>.IsPositiveInfinity() => double.IsPositiveInfinity(Value);
    bool IRealNumber<Flt64>.IsNegativeInfinity() => double.IsNegativeInfinity(Value);
    bool IRealNumber<Flt64>.IsSelfWithinBounds() => !double.IsInfinity(Value) && !double.IsNaN(Value);
    Flt64 IRealNumber<Flt64>.ClampSelfToBounds() => double.IsNaN(Value) ? Flt64Constants.Instance.Zero : new(global::System.Math.Clamp(Value, -double.MaxValue, double.MaxValue));
    public Flt64 ToFlt64() => this;
    public Flt32 ToFlt32() => new((float)Value);
    public FltX ToFltX() => new((decimal)Value);
    public Int64 ToInt64() => new((long)Value);
    public Int32 ToInt32() => new((int)Value);

    // ===== IFlt64ValueConverter (explicit) =====
    Flt64 IFlt64ValueConverter<Flt64>.IntoValue(Flt64 value) => value;
    Flt64 IFlt64ValueConverter<Flt64>.FromValue(Flt64 value) => value;

    // ===== Operators =====
    public static Flt64 operator +(Flt64 l, Flt64 r) => new(l.Value + r.Value);
    public static Flt64 operator -(Flt64 l, Flt64 r) => new(l.Value - r.Value);
    public static Flt64 operator *(Flt64 l, Flt64 r) => new(l.Value * r.Value);
    public static Flt64 operator /(Flt64 l, Flt64 r) => new(l.Value / r.Value);
    public static Flt64 operator %(Flt64 l, Flt64 r) => new(l.Value % r.Value);
    public static Flt64 operator -(Flt64 v) => new(-v.Value);
    public static Flt64 operator ++(Flt64 v) => new(v.Value + 1.0);
    public static Flt64 operator --(Flt64 v) => new(v.Value - 1.0);
    public static bool operator ==(Flt64 l, Flt64 r) => l.Value == r.Value;
    public static bool operator !=(Flt64 l, Flt64 r) => l.Value != r.Value;
    public static bool operator <(Flt64 l, Flt64 r) => l.Value < r.Value;
    public static bool operator >(Flt64 l, Flt64 r) => l.Value > r.Value;
    public static bool operator <=(Flt64 l, Flt64 r) => l.Value <= r.Value;
    public static bool operator >=(Flt64 l, Flt64 r) => l.Value >= r.Value;

    // ===== IRangeTo =====
    public Range RangeTo(Flt64 rhs) => new((int)Value, (int)rhs.Value);
    public Range Until(Flt64 rhs) => new((int)Value, (int)rhs.Value);

    // ===== Object =====
    public override string ToString() => Value.ToString();
    public override bool Equals(object? obj) => obj is Flt64 other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    bool IEquatable<Flt64>.Equals(Flt64 other) => Value == other.Value;
}

// ===== FltX =====

/// <summary>
/// 任意精度浮点数（基于 decimal）/ Arbitrary-precision floating-point number (decimal-backed)
/// </summary>
public readonly struct FltX : IFloatingNumber<FltX>, ICopyable<FltX>, IEquatable<FltX>, IComparable<FltX>,
    IRem<FltX, FltX>, IFlt64ValueConverter<FltX> {
    internal readonly decimal Value;
    public FltX(decimal value) { Value = value; }
    public FltX(double value) { Value = (decimal)value; }
    public FltX(long value) { Value = value; }
    public FltX(string value) { Value = decimal.Parse(value); }

    // ===== Static constants (backward compat) =====
    public static readonly FltX Zero = new(0m);
    public static readonly FltX One = new(1m);

    // ===== Interface properties (explicit) =====
    IArithmeticConstants<FltX> IArithmetic<FltX>.Constants => FltXConstants.Instance;
    IRealNumberConstants<FltX> IRealNumber<FltX>.Constants => FltXConstants.Instance;
    IFloatingNumberConstants<FltX> IFloatingNumber<FltX>.Constants => FltXConstants.Instance;

    // ===== ICopyable =====
    public FltX Copy() => new(Value);

    // ===== IInvariant (explicit) =====
    FltX IInvariant<FltX>.Value() => this;

    // ===== IHasZero/IHasOne (from IFlt64ValueConverter, explicit) =====
    FltX IHasZero<FltX>.Zero => new(0m);
    FltX IHasOne<FltX>.One => new(1m);

    // ===== Arithmetic =====
    public FltX Plus(FltX rhs) => new(Value + rhs.Value);
    public FltX Minus(FltX rhs) => new(Value - rhs.Value);
    public FltX Times(FltX rhs) => new(Value * rhs.Value);
    public FltX Div(FltX rhs) => new(Value / rhs.Value);
    public FltX Rem(FltX rhs) => new(Value % rhs.Value);
    public FltX IntDiv(FltX rhs) => new(global::System.Math.Floor(Value / rhs.Value));
    public FltX Negate() => new(-Value);
    public FltX Abs() => new(global::System.Math.Abs(Value));
    public FltX Reciprocal() => new(1m / Value);
    public FltX Increment() => new(Value + 1m);
    public FltX Decrement() => new(Value - 1m);
    public FltX Cross(FltX rhs) => Times(rhs);

    // ===== IPow =====
    public FltX Pow(int index) => new((decimal)global::System.Math.Pow((double)Value, index));
    public FltX Sqr() => new(Value * Value);
    public FltX Cub() => new(Value * Value * Value);

    // ===== IFloatingNumber extras =====
    public FltX Sqrt() => new((decimal)global::System.Math.Sqrt((double)Value));
    public FltX Acos() => new((decimal)global::System.Math.Acos((double)Value));
    public static FltX FromInt32(int value) => new(value);

    // ===== Trigonometric (delegate to Flt64) =====
    public FltX Sin() => ToFlt64().Sin().ToFltX();
    public FltX Cos() => ToFlt64().Cos().ToFltX();
    public FltX? Tan() => ToFlt64().Tan()?.ToFltX();
    public FltX? Sec() => ToFlt64().Sec()?.ToFltX();
    public FltX? Csc() => ToFlt64().Csc()?.ToFltX();
    public FltX? Cot() => ToFlt64().Cot()?.ToFltX();
    public FltX? Asin() => ToFlt64().Asin()?.ToFltX();
    public FltX Atan() => ToFlt64().Atan().ToFltX();
    public FltX? Asec() => ToFlt64().Asec()?.ToFltX();
    public FltX? Acsc() => ToFlt64().Acsc()?.ToFltX();
    public FltX? Acot() => ToFlt64().Acot()?.ToFltX();

    // ===== Hyperbolic (delegate to Flt64) =====
    public FltX Sinh() => ToFlt64().Sinh().ToFltX();
    public FltX Cosh() => ToFlt64().Cosh().ToFltX();
    public FltX Sech() => ToFlt64().Sech().ToFltX();
    public FltX? Csch() => ToFlt64().Csch()?.ToFltX();
    public FltX Tanh() => ToFlt64().Tanh().ToFltX();
    public FltX? Coth() => ToFlt64().Coth()?.ToFltX();
    public FltX Asinh() => ToFlt64().Asinh().ToFltX();
    public FltX? Acosh() => ToFlt64().Acosh()?.ToFltX();
    public FltX? Asech() => ToFlt64().Asech()?.ToFltX();
    public FltX? Acsch() => ToFlt64().Acsch()?.ToFltX();
    public FltX? Atanh() => ToFlt64().Atanh()?.ToFltX();
    public FltX? Acoth() => ToFlt64().Acoth()?.ToFltX();

    // ===== Exponential / Logarithmic (delegate to Flt64) =====
    public FltX Exp() => ToFlt64().Exp().ToFltX();
    public FltX? Ln() => Value <= 0m ? null : new((decimal)global::System.Math.Log((double)Value));
    public FltX? Lg() => Value <= 0m ? null : new((decimal)global::System.Math.Log10((double)Value));
    public FltX? Lg2() => Value <= 0m ? null : new((decimal)global::System.Math.Log2((double)Value));
    public FltX? Log(FltX baseValue) => Value <= 0m || baseValue.Value <= 0m || baseValue.Equiv(FltXConstants.Instance.One) ? null : new((decimal)(global::System.Math.Log((double)Value) / global::System.Math.Log((double)baseValue.Value)));

    // ===== Rounding (decimal math) =====
    public FltX Floor() => new(global::System.Math.Floor(Value));
    public FltX Ceil() => new(global::System.Math.Ceiling(Value));
    public FltX Round() => new(global::System.Math.Round(Value));
    public FltX Trunc() => new(global::System.Math.Truncate(Value));
    public FltX BankerRound() => new(global::System.Math.Round(Value, MidpointRounding.ToEven));
    public FltX FloorTo(int precision) => new(global::System.Math.Round(Value, precision, MidpointRounding.ToNegativeInfinity));
    public FltX CeilTo(int precision) => new(global::System.Math.Round(Value, precision, MidpointRounding.ToPositiveInfinity));
    public FltX RoundTo(int precision) => new(global::System.Math.Round(Value, precision));
    public FltX TruncTo(int precision) => new(global::System.Math.Round(Value, precision, Value >= 0 ? MidpointRounding.ToNegativeInfinity : MidpointRounding.ToPositiveInfinity));
    public FltX BankerRoundTo(int precision) => new(global::System.Math.Round(Value, precision, MidpointRounding.ToEven));

    // ===== Type Conversions =====
    public Int8 ToInt8() => new((sbyte)(int)Value);
    public Int16 ToInt16() => new((short)(int)Value);
    public UInt8 ToUInt8() => new((byte)(uint)Value);
    public UInt16 ToUInt16() => new((ushort)(uint)Value);
    public UInt32 ToUInt32() => new((uint)Value);
    public UInt64 ToUInt64() => new((ulong)Value);
    public IntX ToIntX() => new((long)global::System.Math.Truncate(Value));
    public UIntX ToUIntX() => new((long)global::System.Math.Truncate(Value));

    // ===== Decimal-specific =====
    public string ToPlainString() => Value.ToString();
    public string ToEngineeringString() => Value.ToString();

    // ===== Comparison =====
    public Order Ord(FltX rhs) => Value.CompareTo(rhs.Value) < 0 ? new Order.Less() : Value > rhs.Value ? new Order.Greater() : new Order.Equal();
    public Order? PartialOrd(FltX rhs) => Ord(rhs);
    public bool Eq(FltX rhs) => Value == rhs.Value;
    public bool? PartialEq(FltX rhs) => Value == rhs.Value;
    public int CompareTo(FltX other) => Value.CompareTo(other.Value);
    public bool Ls(FltX rhs) => Value < rhs.Value;
    public bool Leq(FltX rhs) => Value <= rhs.Value;
    public bool Gr(FltX rhs) => Value > rhs.Value;
    public bool Geq(FltX rhs) => Value >= rhs.Value;
    public bool Equiv(FltX rhs) => global::System.Math.Abs(Value - rhs.Value) <= FltXConstants.Instance.DecimalPrecision.Value;

    // ===== IBounded (explicit) =====
    bool IBounded<FltX>.IsBounded => false;
    FltX? IBounded<FltX>.MinBound => null;
    FltX? IBounded<FltX>.MaxBound => null;
    bool IBounded<FltX>.IsWithinBounds(FltX value) => true;
    FltX IBounded<FltX>.ClampToBounds(FltX value) => value;

    // ===== IInfinite (explicit) =====
    bool IInfinite<FltX>.SupportsInfinity => false;
    FltX? IInfinite<FltX>.PositiveInfinity => null;
    FltX? IInfinite<FltX>.NegativeInfinityValue => null;
    bool IInfinite<FltX>.IsPositiveInfinity(FltX value) => false;
    bool IInfinite<FltX>.IsNegativeInfinity(FltX value) => false;

    // ===== IFixed (explicit) =====
    bool IFixed<FltX>.IsFixed => false;
    int? IFixed<FltX>.FixedDigits => null;
    FltX? IFixed<FltX>.FixedPrecision => null;

    // ===== IEpsilon (explicit) =====
    FltX? IEpsilon<FltX>.PrecisionEpsilon => FltXConstants.Instance.Epsilon;

    // ===== IRealNumber =====
    bool IRealNumber<FltX>.IsPositiveInfinity() => false;
    bool IRealNumber<FltX>.IsNegativeInfinity() => false;
    bool IRealNumber<FltX>.IsSelfWithinBounds() => true;
    FltX IRealNumber<FltX>.ClampSelfToBounds() => this;
    public Flt64 ToFlt64() => new((double)Value);
    public Flt32 ToFlt32() => new((float)Value);
    public FltX ToFltX() => this;
    public Int64 ToInt64() => new((long)Value);
    public Int32 ToInt32() => new((int)Value);

    // ===== IFlt64ValueConverter (explicit) =====
    FltX IFlt64ValueConverter<FltX>.IntoValue(Flt64 value) => new((decimal)value.Value);
    Flt64 IFlt64ValueConverter<FltX>.FromValue(FltX value) => new((double)value.Value);

    // ===== Operators =====
    public static FltX operator +(FltX l, FltX r) => new(l.Value + r.Value);
    public static FltX operator -(FltX l, FltX r) => new(l.Value - r.Value);
    public static FltX operator *(FltX l, FltX r) => new(l.Value * r.Value);
    public static FltX operator /(FltX l, FltX r) => new(l.Value / r.Value);
    public static FltX operator %(FltX l, FltX r) => new(l.Value % r.Value);
    public static FltX operator -(FltX v) => new(-v.Value);
    public static FltX operator ++(FltX v) => new(v.Value + 1m);
    public static FltX operator --(FltX v) => new(v.Value - 1m);
    public static bool operator ==(FltX l, FltX r) => l.Value == r.Value;
    public static bool operator !=(FltX l, FltX r) => l.Value != r.Value;
    public static bool operator <(FltX l, FltX r) => l.Value < r.Value;
    public static bool operator >(FltX l, FltX r) => l.Value > r.Value;
    public static bool operator <=(FltX l, FltX r) => l.Value <= r.Value;
    public static bool operator >=(FltX l, FltX r) => l.Value >= r.Value;

    // ===== IRangeTo =====
    public Range RangeTo(FltX rhs) => new((int)Value, (int)rhs.Value);
    public Range Until(FltX rhs) => new((int)Value, (int)rhs.Value);

    // ===== Object =====
    public override string ToString() => Value.ToString();
    public override bool Equals(object? obj) => obj is FltX other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    bool IEquatable<FltX>.Equals(FltX other) => Value == other.Value;
}
