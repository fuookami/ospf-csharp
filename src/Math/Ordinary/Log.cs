#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Ordinary
{
    /// <summary>
    /// 对数函数 / Logarithm Functions.
    /// <para>为浮点数类型提供自然对数和任意底数对数的高精度计算。</para>
    /// <para>Provides high-precision natural logarithm and arbitrary-base logarithm for floating-point types.</para>
    /// </summary>
    public static class Log
    {
        /// <summary>归一化 FltX 精度 / Normalize FltX scale.</summary>
        private static T NormalizeFltXScale<T>(T value, int digits)
            where T : struct, IFloatingNumber<T>
            => value is FltX f ? (T)(object)f.WithScale(digits, MidpointRounding.AwayFromZero) : value;

        /// <summary>
        /// 自然对数 ln（泰勒级数，y=(x-1)/(x+1)）/ Natural log via Taylor series.
        /// </summary>
        /// <returns>ln(x)，x&lt;=0 返回 null / ln(x), or null if x &lt;= 0.</returns>
        public static T? Ln<T>(T x, IFloatingNumberConstants<T> constants, int? digits = null, T? precision = default)
            where T : struct, IFloatingNumber<T>
        {
            var d = digits ?? ((INumericConstants<T>)constants).DecimalDigits!.Value;
            T eps = precision ?? ((IRealNumberConstants<T>)constants).Epsilon;
            var two = ((INumericConstants<T>)constants).Two;
            var one = ((INumericConstants<T>)constants).One;
            var zero = ((INumericConstants<T>)constants).Zero;
            if (x.PartialOrd(zero) is Order.Less or Order.Equal) return default;
            if (x.PartialOrd(two) is Order.Less or Order.Equal)
            {
                var y = x.Minus(one).Div(x.Plus(one));
                var yPow = y; var value = y; var i = one;
                while (true)
                {
                    yPow = NormalizeFltXScale(yPow.Times(y).Times(y), d);
                    var term = NormalizeFltXScale(yPow.Div(two.Times(i).Plus(one)), d);
                    value = value.Plus(term);
                    i = i.Increment();
                    if (term.Abs().PartialOrd(eps) is Order.Less or Order.Equal) break;
                }
                return value.Times(two);
            }
            var m = x; var k = zero;
            while (m.PartialOrd(two) is Order.Greater or Order.Equal) { m = m.Div(two); k = k.Increment(); }
            while (m.PartialOrd(one) is Order.Less) { m = m.Times(two); k = k.Minus(one); }
            return Ln(m, constants, d, eps)!.Value.Plus(k.Times(constants.Lg2!));
        }

        /// <summary>自然对数 ln（注册表解析）/ Natural log (registry-resolved).</summary>
        public static T? Ln<T>(T x, int? digits = null, T? precision = default)
            where T : struct, IFloatingNumber<T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>() as IFloatingNumberConstants<T>;
            return c is null ? default : Ln(x, c, digits, precision);
        }

        /// <summary>
        /// 任意底对数 log(x, base)=ln(x)/ln(base) / Arbitrary-base logarithm via change of base.
        /// </summary>
        public static T? Logarithm<T>(T x, T @base, IFloatingNumberConstants<T> constants, int? digits = null, T? precision = default)
            where T : struct, IFloatingNumber<T>
        {
            var d = digits ?? ((INumericConstants<T>)constants).DecimalDigits!.Value;
            T eps = precision ?? ((IRealNumberConstants<T>)constants).Epsilon;
            var lhs = Ln(x, constants, d, eps);
            var rhs = Ln(@base, constants, d, eps);
            return lhs is not null && rhs is not null ? lhs.Value.Div(rhs.Value) : default;
        }

        /// <summary>任意底对数（注册表解析）/ Arbitrary-base log (registry-resolved).</summary>
        public static T? Logarithm<T>(T x, T @base, int? digits = null, T? precision = default)
            where T : struct, IFloatingNumber<T>
        {
            var c = NumericConstantsRegistry.ForOrNull<T>() as IFloatingNumberConstants<T>;
            return c is null ? default : Logarithm(x, @base, c, digits, precision);
        }
    }
}
