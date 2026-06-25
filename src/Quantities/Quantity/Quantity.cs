#nullable enable

using System;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Quantities.Quantity
{
    /// <summary>
    /// 量纲不匹配异常 / Dimension mismatch exception.
    /// Used as Error payload; not thrown from arithmetic path.
    /// </summary>
    public sealed class DimensionMismatchException : Exception
    {
        public DerivedQuantity Expected { get; }
        public DerivedQuantity Actual { get; }

        public DimensionMismatchException(DerivedQuantity expected, DerivedQuantity actual)
            : base($"Dimension mismatch: expected {expected.DimensionSymbol()}, got {actual.DimensionSymbol()}.")
        {
            Expected = expected;
            Actual = actual;
        }
    }

    /// <summary>
    /// 单位转换异常 / Unit conversion exception.
    /// </summary>
    public sealed class UnitConversionException : Exception
    {
        public UnitConversionException(string message) : base(message) { }
    }

    /// <summary>
    /// 物理量 / A physical quantity (value + unit).
    /// data class -> record. Arithmetic returns Failed on dimension mismatch, never throws.
    /// </summary>
    public sealed record Quantity<V>(V Value, PhysicalUnit Unit)
    {
        /// <summary>
        /// 转换到目标单位 / Convert to target unit.
        /// dimension mismatch -> Failed.
        /// </summary>
        public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> To(PhysicalUnit target)
        {
            if (!Unit.Quantity.Equals(target.Quantity))
                return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, new DimensionMismatchException(Unit.Quantity, target.Quantity).Message));

            if (Value is Flt64 f64)
            {
                var converted = Unit.ConvertValue(f64.ToFltX(), target);
                if (converted == null)
                    return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                        new Err<ErrorCode>(ErrorCode.IllegalArgument, "Conversion failed."));
                return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>((V)(object)converted.Value.ToFlt64(), target));
            }
            if (Value is FltX fx)
            {
                var converted = Unit.ConvertValue(fx, target);
                if (converted == null)
                    return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                        new Err<ErrorCode>(ErrorCode.IllegalArgument, "Conversion failed."));
                return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>((V)(object)converted, target));
            }
            if (Unit.IsAffine || target.IsAffine)
                return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "Affine conversion not supported for this value type."));
            var scale = Unit.To(target);
            if (scale == null)
                return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "Conversion failed."));
            return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>(Value, target));
        }

        /// <summary>别名 / Alias for To.</summary>
        public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ConvertTo(PhysicalUnit target) => To(target);
    }

    /// <summary>
    /// Quantity 运算扩展 / Quantity operator extensions.
    /// </summary>
    public static class QuantityOperators
    {
        /// <summary>创建物理量 / Create a quantity.</summary>
        public static Quantity<V> Of<V>(this PhysicalUnit unit, V value) => new(value, unit);

        /// <summary>加法 / Addition; dimension mismatch -> Failed.</summary>
        public static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Add<V>(this Quantity<V> lhs, Quantity<V> rhs)
        {
            if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity))
                return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, new DimensionMismatchException(lhs.Unit.Quantity, rhs.Unit.Quantity).Message));

            var converted = rhs.To(lhs.Unit);
            if (converted.IsFailed)
                return converted;

            return AddValues(lhs, converted.Value);
        }

        /// <summary>减法 / Subtraction; dimension mismatch -> Failed.</summary>
        public static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Subtract<V>(this Quantity<V> lhs, Quantity<V> rhs)
        {
            if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity))
                return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, new DimensionMismatchException(lhs.Unit.Quantity, rhs.Unit.Quantity).Message));

            var converted = rhs.To(lhs.Unit);
            if (converted.IsFailed)
                return converted;

            return SubtractValues(lhs, converted.Value);
        }

        /// <summary>乘法（物理量之间）/ Multiplication between quantities.</summary>
        public static Quantity<V> Multiply<V>(this Quantity<V> lhs, Quantity<V> rhs)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            return new Quantity<V>(lhs.Value.Times(rhs.Value), lhs.Unit.Multiply(rhs.Unit));
        }

        /// <summary>乘以标量 / Multiply by scalar.</summary>
        public static Quantity<V> MultiplyScalar<V>(this Quantity<V> lhs, V scalar)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            return new Quantity<V>(lhs.Value.Times(scalar), lhs.Unit);
        }

        /// <summary>除法（物理量之间）/ Division between quantities.</summary>
        public static Quantity<V> Divide<V>(this Quantity<V> lhs, Quantity<V> rhs)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            return new Quantity<V>(lhs.Value.Div(rhs.Value), lhs.Unit.Divide(rhs.Unit));
        }

        /// <summary>除以标量 / Divide by scalar.</summary>
        public static Quantity<V> DivideScalar<V>(this Quantity<V> lhs, V scalar)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            return new Quantity<V>(lhs.Value.Div(scalar), lhs.Unit);
        }

        private static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> AddValues<V>(Quantity<V> lhs, Quantity<V> rhs)
        {
            var val = lhs.Value;
            if (val is Flt64 f64lhs && rhs.Value is Flt64 f64rhs)
                return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>((V)(object)(f64lhs + f64rhs), lhs.Unit));
            if (val is FltX fxlhs && rhs.Value is FltX fxrhs)
                return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>((V)(object)(fxlhs.Plus(fxrhs)), lhs.Unit));
            if (val is Int64 i64lhs && rhs.Value is Int64 i64rhs)
                return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>((V)(object)(i64lhs + i64rhs), lhs.Unit));
            if (val is IntX ixlhs && rhs.Value is IntX ixrhs)
                return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>((V)(object)(ixlhs.Plus(ixrhs)), lhs.Unit));
            return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "Unsupported value type for addition."));
        }

        private static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> SubtractValues<V>(Quantity<V> lhs, Quantity<V> rhs)
        {
            var val = lhs.Value;
            if (val is Flt64 f64lhs && rhs.Value is Flt64 f64rhs)
                return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>((V)(object)(f64lhs - f64rhs), lhs.Unit));
            if (val is FltX fxlhs && rhs.Value is FltX fxrhs)
                return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>((V)(object)(fxlhs.Minus(fxrhs)), lhs.Unit));
            if (val is Int64 i64lhs && rhs.Value is Int64 i64rhs)
                return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>((V)(object)(i64lhs - i64rhs), lhs.Unit));
            if (val is IntX ixlhs && rhs.Value is IntX ixrhs)
                return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(new Quantity<V>((V)(object)(ixlhs.Minus(ixrhs)), lhs.Unit));
            return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "Unsupported value type for subtraction."));
        }
    }
}
