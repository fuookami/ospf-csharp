#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Math.Ordinary
{
    /// <summary>
    /// 因式分解 / Integer Factorization.
    /// <para>提供质因数分解、反分解、因数计算、因数个数、欧拉函数。</para>
    /// <para>Provides prime factorization, defactorize, divisors, divisor count, Euler totient.</para>
    /// </summary>
    public static class Factorization
    {
        /// <summary>使用预计算素数表进行质因数分解 / Core factorization using pre-computed primes.</summary>
        private static IReadOnlyList<(T Prime, int Exponent)> FactorizeWithPrimes<T>(
            T num, IEnumerable<T> primes, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>
        {
            if (num.PartialOrd(constants.One) is Order.Less or Order.Equal) return Array.Empty<(T, int)>();
            var n = num;
            var factors = new List<(T Prime, int Exponent)>();
            foreach (var prime in primes)
            {
                if (prime.Times(prime).PartialOrd(num) is Order.Greater) break;
                var index = 0;
                while (n.Rem(prime).Eq(constants.Zero)) { index++; n = n.Div(prime); }
                if (index != 0) factors.Add((prime, index));
            }
            if (n.PartialOrd(constants.One) is Order.Greater) factors.Add((n, 1));
            return factors;
        }

        /// <summary>对整数进行质因数分解（内部实现）/ Prime factorization (internal).</summary>
        internal static IReadOnlyList<(T Prime, int Exponent)> FactorizeImpl<T>(T num, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>
        {
            var sqrtULong = num.ToFlt64().Sqrt().Floor().ToUInt64().Value + 1UL;
            var sqrtLimit = constants.One;
            for (var count = sqrtULong - 1UL; count > 0UL; count--) sqrtLimit = sqrtLimit.Increment();
            var primes = Prime.GetPrimesImpl(sqrtLimit, constants);
            return FactorizeWithPrimes(num, primes, constants);
        }

        /// <summary>UInt64 专用优化（直接用缓存素数表）/ UInt64-optimized (cached prime table).</summary>
        internal static IReadOnlyList<(UInt64 Prime, int Exponent)> FactorizeImpl(UInt64 num, INumericConstants<UInt64> constants)
        {
            if (num.PartialOrd(UInt64Constants.Instance.One) is Order.Less or Order.Equal) return Array.Empty<(UInt64, int)>();
            var sqrtLimit = num.ToFlt64().Sqrt().Floor().ToUInt64().Increment();
            var primes = Prime.GetPrimesUpTo(sqrtLimit);
            return FactorizeWithPrimes(num, primes, constants);
        }

        /// <summary>对整数进行质因数分解 / Prime factorization.</summary>
        public static IReadOnlyList<(T Prime, int Exponent)> Of<T>(T num, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>
            => FactorizeImpl(num, constants);

        /// <summary>对整数进行质因数分解（注册表解析）/ Factorize (registry-resolved).</summary>
        public static Result<IReadOnlyList<(T Prime, int Exponent)>, ErrorCode, Error<ErrorCode>> Of<T>(T num)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<IReadOnlyList<(T, int)>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Results.Ok<IReadOnlyList<(T, int)>>(Of(num, c));
        }

        /// <summary>将质因数分解结果还原为原整数 / Convert factorization back to integer.</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Defactorize<T>(IEnumerable<(T Prime, int Exponent)> factors, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IPow<T>
        {
            var value = constants.One;
            foreach (var (factor, index) in factors)
            {
                if (index < 0)
                    return new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                        $"不支持负指数：{index} / Negative factor index not supported: {index}.");
                if (index == 0) continue;
                value = value.Times(factor.Pow(index));
            }
            return Results.Ok(value);
        }

        /// <summary>将质因数分解结果还原为原整数（注册表解析）/ Defactorize (registry-resolved).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Defactorize<T>(IEnumerable<(T Prime, int Exponent)> factors)
            where T : struct, IInteger<T>, IPow<T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Defactorize(factors, c);
        }

        /// <summary>根据质因数分解结果计算所有因数（内部）/ Compute all divisors from factors (internal).</summary>
        internal static IReadOnlyList<T> DivisorsImpl<T>(IReadOnlyList<(T Prime, int Exponent)> factors, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IPow<T>
        {
            var values = new List<T> { constants.One };
            foreach (var (factor, index) in factors)
            {
                if (index <= 0) continue;
                var next = new List<T>(values.Count * (index + 1));
                var factorPower = constants.One;
                for (var k = 0; k <= index; k++)
                {
                    foreach (var v in values) next.Add(v.Times(factorPower));
                    factorPower = factorPower.Times(factor);
                }
                values = next;
            }
            values.Sort((a, b) =>
            {
                var ord = a.PartialOrd(b);
                return ord is Order.Less ? -1 : ord is Order.Greater ? 1 : 0;
            });
            return values;
        }

        /// <summary>计算整数的所有因数（内部）/ Compute all divisors (internal).</summary>
        internal static IReadOnlyList<T> DivisorsImpl<T>(T num, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>, IPow<T>
            => DivisorsImpl(FactorizeImpl(num, constants), constants);

        /// <summary>根据质因数分解结果计算所有因数 / Compute divisors from factors.</summary>
        public static IReadOnlyList<T> Divisors<T>(IReadOnlyList<(T Prime, int Exponent)> factors, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IPow<T>
            => DivisorsImpl(factors, constants);

        /// <summary>根据质因数分解结果计算所有因数（注册表解析）/ Divisors from factors (registry-resolved).</summary>
        public static Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>> Divisors<T>(IReadOnlyList<(T Prime, int Exponent)> factors)
            where T : struct, IInteger<T>, IPow<T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Results.Ok<IReadOnlyList<T>>(Divisors(factors, c));
        }

        /// <summary>计算整数的所有因数 / Compute all divisors of an integer.</summary>
        public static IReadOnlyList<T> Divisors<T>(T num, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>, IPow<T>
            => DivisorsImpl(num, constants);

        /// <summary>计算整数的所有因数（注册表解析）/ Divisors (registry-resolved).</summary>
        public static Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>> Divisors<T>(T num)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>, IPow<T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Results.Ok<IReadOnlyList<T>>(Divisors(num, c));
        }

        /// <summary>根据质因数分解结果计算因数个数 / Compute divisor count from factors.</summary>
        public static int DivisorCount<T>(IEnumerable<(T Prime, int Exponent)> factors)
            where T : struct, IInteger<T>
        {
            var count = 1;
            foreach (var (_, index) in factors) if (index > 0) count *= (index + 1);
            return count;
        }

        /// <summary>计算整数的因数个数 / Compute divisor count of an integer.</summary>
        public static int DivisorCount<T>(T num, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>
            => DivisorCount(FactorizeImpl(num, constants));

        /// <summary>计算整数的因数个数（注册表解析）/ Divisor count (registry-resolved).</summary>
        public static Result<int, ErrorCode, Error<ErrorCode>> DivisorCount<T>(T num)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<int, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Results.Ok(DivisorCount(num, c));
        }

        /// <summary>计算欧拉函数值（内部）/ Compute Euler's totient (internal).</summary>
        internal static T EulerTotientImpl<T>(T num, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>, IMinus<T, T>
        {
            if (num.Eq(constants.Zero) || num.Eq(constants.One)) return num;
            var value = num;
            var one = constants.One;
            foreach (var (prime, _) in FactorizeImpl(num, constants))
                value = value.Div(prime).Times(prime.Minus(one));
            return value;
        }

        /// <summary>计算欧拉函数值 phi(n) / Compute Euler's totient phi(n).</summary>
        public static T EulerTotient<T>(T num, INumericConstants<T> constants)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>, IMinus<T, T>
            => EulerTotientImpl(num, constants);

        /// <summary>计算欧拉函数值（注册表解析）/ Euler's totient (registry-resolved).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> EulerTotient<T>(T num)
            where T : struct, IInteger<T>, IDiv<T, T>, IRem<T, T>, IMinus<T, T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>();
            return c is null
                ? new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                    $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
                : Results.Ok(EulerTotient(num, c));
        }
    }
}
