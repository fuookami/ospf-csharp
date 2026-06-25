#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Ordinary
{
    /// <summary>
    /// 幂函数 / Power Functions.
    /// <para>为实数类型提供整数指数幂和浮点指数幂的高精度计算。</para>
    /// <para>Provides high-precision integer-exponent and floating-exponent power for real number types.</para>
    /// </summary>
    public static class Pow
    {
        /// <summary>归一化 FltX 精度 / Normalize FltX scale; non-FltX types remain unchanged.</summary>
        private static T NormalizeFltXScale<T>(T value, int digits)
            where T : struct, IFloatingNumber<T>
            => value is FltX f ? (T)(object)f.WithScale(digits, MidpointRounding.AwayFromZero) : value;

        /// <summary>正整数指数快速幂（while 循环）/ Positive fast-power (while loop).</summary>
        private static T PowPosImpl<T>(T value, T @base, int index, int digits, T precision)
            where T : struct, ITimesSemigroup<T>, IRealNumber<T>
        {
            while (true)
            {
                if (index == 1) return value.Times(@base);
                if (index % 2 == 0) { @base = @base.Times(@base); index /= 2; }
                else { value = value.Times(@base); index -= 1; }
            }
        }

        /// <summary>负整数指数快速幂（while 循环）/ Negative fast-power (while loop).</summary>
        private static T PowNegImpl<T>(T value, T @base, int index, int digits, T precision)
            where T : struct, ITimesGroup<T>, IRealNumber<T>
        {
            while (true)
            {
                if (index == -1) return value.Div(@base);
                if (index % 2 == 0) { @base = @base.Times(@base); index /= 2; }
                else { value = value.Div(@base); index += 1; }
            }
        }

        /// <summary>整数指数幂（TimesGroup，支持负指数）/ Integer-exponent power (TimesGroup, negative supported).</summary>
        public static T Of<T>(T @base, int index, INumericConstants<T> constants, int digits = 0, T? precision = default)
            where T : struct, ITimesGroup<T>, IRealNumber<T>
        {
            T eps = precision ?? constants.Epsilon ?? constants.One;
            if (index >= 1) return PowPosImpl(constants.One, @base, index, digits, eps);
            if (index <= -1) return PowNegImpl(constants.One, @base, index, digits, eps);
            return constants.One;
        }

        /// <summary>整数指数幂（注册表解析）/ Integer power (registry-resolved).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Of<T>(T @base, int index, int digits = 0, T? precision = default)
            where T : struct, ITimesGroup<T>, IRealNumber<T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Results.Ok(Of(@base, index, c, digits, precision));
        }

        /// <summary>安全正整数指数幂（Semigroup，负指数→Failed）/ Safe positive power (Semigroup; negative→Failed).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Safe<T>(T @base, int index, INumericConstants<T> constants, int digits = 0, T? precision = default)
            where T : struct, ITimesSemigroup<T>, IRealNumber<T>
            => OrNull(@base, index, constants, digits, precision) is { } v
                ? Results.Ok(v)
                : new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"负指数幂需要乘法群支持：{@base.GetType()} / Negative exponent requires TimesGroup support: {@base.GetType()}.");

        /// <summary>安全正整数指数幂（注册表解析）/ Safe power (registry-resolved).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Safe<T>(T @base, int index, int digits = 0, T? precision = default)
            where T : struct, ITimesSemigroup<T>, IRealNumber<T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Safe(@base, index, c, digits, precision);
        }

        /// <summary>尝试计算正整数指数幂，负指数返回 null / Power or null for negative exponent.</summary>
        public static T? OrNull<T>(T @base, int index, INumericConstants<T> constants, int digits = 0, T? precision = default)
            where T : struct, ITimesSemigroup<T>, IRealNumber<T>
        {
            T eps = precision ?? constants.Epsilon ?? constants.One;
            if (index >= 1) return PowPosImpl(constants.One, @base, index, digits, eps);
            if (index <= -1) return default;
            return constants.One;
        }

        /// <summary>尝试计算正整数指数幂（注册表解析）/ Power or null (registry-resolved).</summary>
        public static T? OrNull<T>(T @base, int index, int digits = 0, T? precision = default)
            where T : struct, ITimesSemigroup<T>, IRealNumber<T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null ? default : OrNull(@base, index, c, digits, precision);
        }

        /// <summary>浮点指数幂 powf（ln+exp）/ Floating-exponent power via ln+exp.</summary>
        public static T Powf<T>(T @base, T index, IFloatingNumberConstants<T> constants, int digits = 0, T? precision = default)
            where T : struct, IFloatingNumber<T>
        {
            T eps = precision ?? ((IRealNumberConstants<T>)constants).Epsilon;
            var lnBase = Log.Ln(@base, constants, digits, eps)!.Value;
            return Exp(index.Times(lnBase), constants, digits, eps);
        }

        /// <summary>浮点指数幂（注册表解析）/ Floating power (registry-resolved).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Powf<T>(T @base, T index, int digits = 0, T? precision = default)
            where T : struct, IFloatingNumber<T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>() as IFloatingNumberConstants<T>;
            return c is null
                ? new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 IFloatingNumberConstants<{typeof(T).Name}> / No IFloatingNumberConstants<{typeof(T).Name}> registered.")
                : Results.Ok(Powf(@base, index, c, digits, precision));
        }

        /// <summary>指数函数 exp（泰勒级数）/ Exponential via Taylor series.</summary>
        public static T Exp<T>(T index, IFloatingNumberConstants<T> constants, int digits = 0, T? precision = default)
            where T : struct, IFloatingNumber<T>
        {
            T eps = precision ?? ((IRealNumberConstants<T>)constants).Epsilon;
            var one = ((INumericConstants<T>)constants).One;
            var value = one;
            var term = one;
            var i = one;
            while (true)
            {
                var thisItem = NormalizeFltXScale(term.Times(index).Div(i), digits);
                value = value.Plus(thisItem);
                i = i.Increment();
                if (thisItem.Abs().PartialOrd(eps) is Order.Less or Order.Equal) break;
                term = thisItem;
            }
            return value;
        }

        /// <summary>指数函数 exp（注册表解析）/ Exp (registry-resolved).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Exp<T>(T index, int digits = 0, T? precision = default)
            where T : struct, IFloatingNumber<T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>() as IFloatingNumberConstants<T>;
            return c is null
                ? new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 IFloatingNumberConstants<{typeof(T).Name}> / No IFloatingNumberConstants<{typeof(T).Name}> registered.")
                : Results.Ok(Exp(index, c, digits, precision));
        }
    }
}
