#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Operation
{
    /// <summary>
    /// Flt64 快捷运算 / Flt64 quick ops.
    /// 顶层乘除运算符重载 / Top-level multiply/divide operator overloads.
    /// </summary>
    public static class Flt64QuickOps
    {
        // ===== Flt64 * Symbol =====

        /// <summary>Flt64 乘符号 / Flt64 times symbol.</summary>
        public static LinearMonomial<Flt64> Multiply(Flt64 lhs, ISymbol rhs)
            => new(lhs, rhs);

        /// <summary>符号乘 Flt64 / Symbol times Flt64.</summary>
        public static LinearMonomial<Flt64> Multiply(ISymbol lhs, Flt64 rhs)
            => new(rhs, lhs);

        // ===== Flt64 * LinearMonomial =====

        /// <summary>Flt64 乘线性单项式 / Flt64 times linear monomial.</summary>
        public static LinearMonomial<Flt64> Multiply(Flt64 lhs, LinearMonomial<Flt64> rhs)
            => new(lhs * rhs.Coefficient, rhs.Symbol);

        /// <summary>线性单项式乘 Flt64 / Linear monomial times Flt64.</summary>
        public static LinearMonomial<Flt64> Multiply(LinearMonomial<Flt64> lhs, Flt64 rhs)
            => new(lhs.Coefficient * rhs, lhs.Symbol);

        // ===== LinearMonomial * Symbol => QuadraticMonomial =====

        /// <summary>线性单项式乘符号得到二次单项式 / Linear monomial times symbol = quadratic monomial.</summary>
        public static QuadraticMonomial<Flt64> Multiply(LinearMonomial<Flt64> lhs, ISymbol rhs)
            => new(lhs.Coefficient, lhs.Symbol, rhs);

        /// <summary>符号乘线性单项式得到二次单项式 / Symbol times linear monomial = quadratic monomial.</summary>
        public static QuadraticMonomial<Flt64> Multiply(ISymbol lhs, LinearMonomial<Flt64> rhs)
            => new(rhs.Coefficient, lhs, rhs.Symbol);

        // ===== LinearMonomial / Flt64 =====

        /// <summary>线性单项式除以 Flt64 / Linear monomial divided by Flt64.</summary>
        public static LinearMonomial<Flt64> Divide(LinearMonomial<Flt64> lhs, Flt64 rhs)
            => new(lhs.Coefficient / rhs, lhs.Symbol);
    }
}
