#nullable enable

using System;
using System.Numerics;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Value;
using NInt8 = Fuookami.Ospf.Math.Algebra.Number.Int8;
using NInt16 = Fuookami.Ospf.Math.Algebra.Number.Int16;
using NInt32 = Fuookami.Ospf.Math.Algebra.Number.Int32;
using NInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using NUInt8 = Fuookami.Ospf.Math.Algebra.Number.UInt8;
using NUInt16 = Fuookami.Ospf.Math.Algebra.Number.UInt16;
using NUInt32 = Fuookami.Ospf.Math.Algebra.Number.UInt32;
using NUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Symbol.Function;

internal readonly record struct ExactBoundValue(BigInteger Numerator, BigInteger Denominator) {
    private static readonly BigInteger DecimalMaximum = (BigInteger.One << 96) - 1;

    public static ExactBoundValue Zero => new(BigInteger.Zero, BigInteger.One);
    public int Sign => Numerator.Sign;
    public ExactBoundValue Negate() => new(-Numerator, Denominator);
    public ExactBoundValue Abs() => new(BigInteger.Abs(Numerator), Denominator);
    public ExactBoundValue Add(ExactBoundValue other) => Normalize(
        (Numerator * other.Denominator) + (other.Numerator * Denominator),
        Denominator * other.Denominator
    );
    public ExactBoundValue Multiply(ExactBoundValue other) => Normalize(
        Numerator * other.Numerator,
        Denominator * other.Denominator
    );
    public int CompareTo(ExactBoundValue other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public static bool TryRead<V>(V value, out ExactBoundValue exact)
        where V : struct, IRealNumber<V> {
        switch (value) {
            case Flt64 number:
                return TryDouble(number.ToDouble(), out exact);
            case Flt32 number:
                return TryDouble(number.ToFlt64().ToDouble(), out exact);
            case FltX number:
                exact = FromDecimal(number.ToDecimal());
                return true;
            case RtnX number:
                (double numerator, double denominator) = number.ToParts();
                if (denominator <= 0 || !TryDouble(numerator, out ExactBoundValue n) ||
                    !TryDouble(denominator, out ExactBoundValue d)) {
                    break;
                }
                exact = Normalize(n.Numerator * d.Denominator, n.Denominator * d.Numerator);
                return true;
            case NInt8 number:
                exact = Integer(number.ToInt64().ToLong());
                return true;
            case NInt16 number:
                exact = Integer(number.ToInt64().ToLong());
                return true;
            case NInt32 number:
                exact = Integer(number.ToInt64().ToLong());
                return true;
            case NInt64 number:
                exact = Integer(number.ToLong());
                return true;
            case NUInt8 number:
                exact = Integer(number.ToInt64().ToLong());
                return true;
            case NUInt16 number:
                exact = Integer(number.ToInt64().ToLong());
                return true;
            case NUInt32 number:
                exact = Integer(number.ToInt64().ToLong());
                return true;
            case NUInt64 number:
                exact = Integer(number.ToULong());
                return true;
            case IntX number:
                exact = Integer(number.ToBigInteger());
                return true;
            case UIntX number:
                exact = Integer(number.ToBigInteger());
                return true;
        }
        exact = default;
        return false;
    }

    public static bool TryRead<V>(V value, IIntoValue<V> converter, out ExactBoundValue exact)
        where V : struct, IRealNumber<V> {
        if (!TryRead(value, out exact) || !FunctionEvaluation.IsUsable(value)) {
            return false;
        }
        double natural = value.ToFlt64().ToDouble();
        double converted = converter.FromValue(value).ToDouble();
        return BigMValues.IsUsable(new Flt64(converted)) && natural == converted &&
            (exact.Sign == 0 || converted != 0.0);
    }

    public bool TryRound<V>(IIntoValue<V> converter, bool upper, out V result)
        where V : struct, IRealNumber<V> {
        try {
            object? rounded = null;
            if (typeof(V) == typeof(Flt64)) {
                rounded = new Flt64(RoundDouble(upper));
            }
            else if (typeof(V) == typeof(Flt32)) {
                rounded = new Flt32(RoundFloat(upper));
            }
            else if (typeof(V) == typeof(FltX) && TryDecimal(upper, out decimal decimalValue)) {
                rounded = new FltX(decimalValue);
            }
            else if (typeof(V) == typeof(RtnX)) {
                rounded = new RtnX(RoundDouble(upper), 1);
            }
            else {
                BigInteger integer = RoundInteger(upper);
                if (typeof(V) == typeof(NInt8)) {
                    rounded = new NInt8((sbyte)integer);
                }
                else if (typeof(V) == typeof(NInt16)) {
                    rounded = new NInt16((short)integer);
                }
                else if (typeof(V) == typeof(NInt32)) {
                    rounded = new NInt32((int)integer);
                }
                else if (typeof(V) == typeof(NInt64)) {
                    rounded = new NInt64((long)integer);
                }
                else if (typeof(V) == typeof(NUInt8)) {
                    rounded = new NUInt8((byte)integer);
                }
                else if (typeof(V) == typeof(NUInt16)) {
                    rounded = new NUInt16((ushort)integer);
                }
                else if (typeof(V) == typeof(NUInt32)) {
                    rounded = new NUInt32((uint)integer);
                }
                else if (typeof(V) == typeof(NUInt64)) {
                    rounded = new NUInt64((ulong)integer);
                }
                else if (typeof(V) == typeof(IntX)) {
                    rounded = new IntX(integer);
                }
                else if (typeof(V) == typeof(UIntX) && integer.Sign >= 0) {
                    rounded = new UIntX(integer);
                }
            }
            if (rounded is V candidate && TryRead(
                    value: candidate,
                    converter: converter,
                    exact: out ExactBoundValue actual
                ) &&
                (upper ? actual.CompareTo(this) >= 0 : actual.CompareTo(this) <= 0)) {
                result = candidate;
                return true;
            }
        }
        catch (ArithmeticException) { }
        catch (InvalidOperationException) { }
        result = default;
        return false;
    }

    public bool TryRoundSolverUpper<V>(IIntoValue<V> converter, out V result)
        where V : struct, IRealNumber<V> {
        result = default;
        if (!TryRound(
                converter: converter,
                upper: true,
                result: out V candidate
            )) {
            return false;
        }
        if (TryRead(converter.FromValue(candidate), out ExactBoundValue encoded) && encoded.CompareTo(this) >= 0) {
            result = candidate;
            return true;
        }
        if (!TryRound(
                converter: IIntoValue<Flt64>.Identity,
                upper: true,
                result: out Flt64 solverValue
            )) {
            return false;
        }
        // shortcut: 两次仍无法证明编码上界则拒绝；需要更广泛的转换器支持时扩展 / Reject after two unproven encodings; extend for broader converters.
        for (int attempt = 0; attempt < 2; ++attempt) {
            if (TryRead(solverValue, out ExactBoundValue solverBound) && solverBound.TryRound(
                    converter: converter,
                    upper: true,
                    result: out candidate
                ) && TryRead(converter.FromValue(candidate), out encoded) && encoded.CompareTo(this) >= 0) {
                result = candidate;
                return true;
            }
            solverValue = new Flt64(global::System.Math.BitIncrement(solverValue.ToDouble()));
        }
        return false;
    }

    private static ExactBoundValue Integer(BigInteger value) => new(value, BigInteger.One);

    private static ExactBoundValue Normalize(BigInteger numerator, BigInteger denominator) {
        if (numerator.IsZero) {
            return Integer(BigInteger.Zero);
        }
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        return new ExactBoundValue(numerator / divisor, denominator / divisor);
    }

    private static bool TryDouble(double value, out ExactBoundValue exact) {
        if (!double.IsFinite(value)) {
            exact = default;
            return false;
        }
        ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        int exponent = (int)((bits >> 52) & 0x7ff);
        ulong fraction = bits & 0x000f_ffff_ffff_ffffUL;
        BigInteger significand = exponent == 0 ? fraction : fraction | (1UL << 52);
        if ((bits >> 63) != 0) {
            significand = -significand;
        }
        int shift = exponent == 0 ? -1074 : exponent - 1075;
        exact = shift >= 0
            ? new ExactBoundValue(significand << shift, BigInteger.One)
            : Normalize(significand, BigInteger.One << -shift);
        return true;
    }

    private static ExactBoundValue FromDecimal(decimal value) {
        int[] bits = decimal.GetBits(value);
        BigInteger significand = (uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) |
            ((BigInteger)(uint)bits[2] << 64);
        if ((bits[3] & int.MinValue) != 0) {
            significand = -significand;
        }
        int scale = (bits[3] >> 16) & 0xff;
        return Normalize(significand, BigInteger.Pow(10, scale));
    }

    private BigInteger RoundInteger(bool upper) {
        var quotient = BigInteger.DivRem(
            dividend: Numerator,
            divisor: Denominator,
            remainder: out BigInteger remainder
        );
        return remainder.IsZero || (upper ? Sign < 0 : Sign > 0)
            ? quotient
            : quotient + (upper ? BigInteger.One : -BigInteger.One);
    }

    private bool TryDecimal(bool upper, out decimal value) {
        var numerator = BigInteger.Abs(Numerator);
        for (int scale = 28; scale >= 0; --scale) {
            var quotient = BigInteger.DivRem(
                dividend: numerator * BigInteger.Pow(10, scale),
                divisor: Denominator,
                remainder: out BigInteger remainder
            );
            if (!remainder.IsZero && (upper ? Sign > 0 : Sign < 0)) {
                ++quotient;
            }
            if (quotient <= DecimalMaximum) {
                int resultScale = scale;
                while (resultScale > 0 && quotient % 10 == 0) {
                    quotient /= 10;
                    --resultScale;
                }
                value = new decimal(
                    lo: unchecked((int)(uint)(quotient & uint.MaxValue)),
                    mid: unchecked((int)(uint)((quotient >> 32) & uint.MaxValue)),
                    hi: unchecked((int)(uint)((quotient >> 64) & uint.MaxValue)),
                    isNegative: Sign < 0,
                    scale: (byte)resultScale
                );
                return true;
            }
        }
        value = default;
        return false;
    }

    private double Approximate() {
        if (Sign == 0) {
            return 0.0;
        }
        var magnitude = BigInteger.Abs(Numerator);
        int numeratorShift = (int)global::System.Math.Max(0, magnitude.GetBitLength() - 53);
        int denominatorShift = (int)global::System.Math.Max(0, Denominator.GetBitLength() - 53);
        double ratio = (double)(magnitude >> numeratorShift) / (double)(Denominator >> denominatorShift);
        double approximation = global::System.Math.ScaleB(ratio, numeratorShift - denominatorShift);
        return Sign < 0 ? -approximation : approximation;
    }

    private double RoundDouble(bool upper) {
        double current = Approximate();
        if (double.IsPositiveInfinity(current)) {
            current = double.MaxValue;
        }
        if (double.IsNegativeInfinity(current)) {
            current = -double.MaxValue;
        }
        while (true) {
            TryDouble(current, out ExactBoundValue represented);
            int comparison = represented.CompareTo(this);
            if (upper ? comparison < 0 : comparison > 0) {
                current = upper ? global::System.Math.BitIncrement(current) : global::System.Math.BitDecrement(current);
                if (!double.IsFinite(current)) {
                    return current;
                }
                continue;
            }
            double next = upper ? global::System.Math.BitDecrement(current) : global::System.Math.BitIncrement(current);
            if (!double.IsFinite(next) || !TryDouble(next, out ExactBoundValue candidate) ||
                (upper ? candidate.CompareTo(this) < 0 : candidate.CompareTo(this) > 0)) {
                return current;
            }
            current = next;
        }
    }

    private float RoundFloat(bool upper) {
        float current = (float)Approximate();
        if (float.IsPositiveInfinity(current)) {
            current = float.MaxValue;
        }
        if (float.IsNegativeInfinity(current)) {
            current = -float.MaxValue;
        }
        while (true) {
            TryDouble(current, out ExactBoundValue represented);
            int comparison = represented.CompareTo(this);
            if (upper ? comparison < 0 : comparison > 0) {
                current = upper ? global::System.MathF.BitIncrement(current) : global::System.MathF.BitDecrement(current);
                if (!float.IsFinite(current)) {
                    return current;
                }
                continue;
            }
            float next = upper ? global::System.MathF.BitDecrement(current) : global::System.MathF.BitIncrement(current);
            if (!float.IsFinite(next) || !TryDouble(next, out ExactBoundValue candidate) ||
                (upper ? candidate.CompareTo(this) < 0 : candidate.CompareTo(this) > 0)) {
                return current;
            }
            current = next;
        }
    }
}
