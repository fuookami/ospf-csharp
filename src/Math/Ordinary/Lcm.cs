#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Ordinary
{
    /// <summary>
    /// 最小公倍数 / Least Common Multiple (LCM).
    /// <para>提供整数、浮点数 LCM 计算；支持 GCD 公式和因式分解两种方法。</para>
    /// <para>Provides LCM for integers and floats; supports GCD-formula and factorization methods.</para>
    /// </summary>
    public static class Lcm
    {
        /// <summary>因式分解 LCM（内部）/ LCM via factorization (internal).</summary>
        internal static T LcmImpl<T>(IEnumerable<T> numbers, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IPow<T>, IDiv<T, T>, IRem<T, T>
        {
            var factors = numbers.Select(n => Factorization.FactorizeImpl(n, constants)).ToList();
            if (factors.Count == 0) return constants.One;
            if (factors.Any(f => f.Count == 0)) return constants.Zero;
            if (factors.Count == 1) return numbers.First();
            var merged = factors
                .SelectMany(f => f)
                .GroupBy(p => p.Prime)
                .ToDictionary(g => g.Key, g => g.Max(p => p.Exponent));
            var acc = constants.One;
            foreach (var (prime, exponent) in merged)
                acc = acc.Times(prime.Pow(exponent));
            return acc;
        }

        /// <summary>通过因式分解计算 LCM / Compute LCM via factorization.</summary>
        public static T LcmByFactorization<T>(IEnumerable<T> numbers, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IPow<T>, IDiv<T, T>, IRem<T, T>
            => LcmImpl(numbers, constants);

        /// <summary>通过因式分解计算 LCM（注册表解析）/ LCM via factorization (registry-resolved).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> LcmByFactorization<T>(IEnumerable<T> numbers)
            where T : struct, IInteger<T>, IPow<T>, IDiv<T, T>, IRem<T, T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Results.Ok(LcmByFactorization(numbers, c));
        }

        /// <summary>通过因式分解计算两个整数的 LCM / LCM of two via factorization.</summary>
        public static T LcmByFactorization<T>(T x, T y, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IPow<T>, IDiv<T, T>, IRem<T, T>
            => LcmByFactorization(new[] { x, y }, constants);

        /// <summary>通过因式分解计算多个整数的 LCM（可变参数）/ LCM via factorization (vararg).</summary>
        public static T LcmByFactorization<T>(T x, T y, T z, INumericConstants<T> constants, params T[] rest)
            where T : struct, IInteger<T>, IPow<T>, IDiv<T, T>, IRem<T, T>
            => LcmByFactorization(new[] { x, y, z }.Concat(rest), constants);

        /// <summary>GCD 公式 LCM：lcm(x,y)=|x/gcd|*|y| / LCM via GCD formula.</summary>
        public static T Of<T>(T x, T y, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IRem<T, T>, IDiv<T, T>
        {
            var px = x.Abs(); var py = y.Abs();
            if (px.Eq(constants.Zero) || py.Eq(constants.Zero)) return constants.Zero;
            var g = Gcd.GcdModImpl(px, py);
            return px.Div(g).Times(py);
        }

        /// <summary>计算两个整数的 LCM（注册表解析）/ LCM of two (registry-resolved).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Of<T>(T x, T y)
            where T : struct, IInteger<T>, IRem<T, T>, IMinus<T, T>, IDiv<T, T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Results.Ok(Of(x, y, c));
        }

        /// <summary>计算多个整数的 LCM / Compute LCM of multiple integers.</summary>
        public static T Of<T>(IEnumerable<T> numbers, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IPow<T>, IRem<T, T>, IDiv<T, T>
            => LcmImpl(numbers, constants);

        /// <summary>计算多个整数的 LCM（注册表解析）/ LCM of multiple (registry-resolved).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Of<T>(IEnumerable<T> numbers)
            where T : struct, IInteger<T>, IPow<T>, IRem<T, T>, IDiv<T, T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Results.Ok(Of(numbers, c));
        }

        /// <summary>计算多个整数的 LCM（可变参数）/ LCM (vararg).</summary>
        public static T Of<T>(T x, T y, T z, INumericConstants<T> constants, params T[] rest)
            where T : struct, IInteger<T>, IPow<T>, IRem<T, T>, IDiv<T, T>
            => Of(new[] { x, y, z }.Concat(rest), constants);

        // —— FltX 特化 / FltX specialization ——

        /// <summary>计算两个浮点数的 LCM / Compute LCM of two FltX values.</summary>
        public static FltX Of(FltX x, FltX y)
        {
            var px = x.Abs(); var py = y.Abs();
            var g = Gcd.Of(px, py);
            return px.Div(g).Times(py);
        }

        /// <summary>计算多个浮点数的 LCM / Compute LCM of multiple FltX values.</summary>
        public static FltX Of(IEnumerable<FltX> numbers)
        {
            var (integerNumbers, factor) = FltXHelper.ScaleToIntegers(numbers);
            return LcmImpl(integerNumbers.OrderByDescending(n => n), IntXConstants.Instance).ToFltX()
                / FltXConstants.Instance.Ten.Pow(factor);
        }

        /// <summary>计算多个浮点数的 LCM（可变参数）/ LCM of FltX (vararg).</summary>
        public static FltX Of(FltX x, FltX y, FltX z, params FltX[] rest) => Of(new[] { x, y, z }.Concat(rest));

        // —— RtnX 特化 / RtnX specialization ——
        // Note: RtnX in C# uses double Numerator/Denominator (simplified).
        // Lcm(RtnX) specializations are deferred to future IntX-backed RtnX port.
    }
}
