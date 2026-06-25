#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Operation;
/// <summary>
/// 不等式 DSL / Inequality DSL.
/// 提供符号与多项式之间的比较运算构建不等式。
/// Provides comparison operations between symbols and polynomials to build inequalities.
/// </summary>
public static class InequalityDsl {
    // ===== Symbol vs Flt64 =====

    /// <summary>符号小于 Flt64 / Symbol less than Flt64.</summary>
    public static LinearInequality<Flt64> Lt(ISymbol lhs, Flt64 rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.LT);

    /// <summary>符号小于等于 Flt64 / Symbol less than or equal Flt64.</summary>
    public static LinearInequality<Flt64> Le(ISymbol lhs, Flt64 rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.LE);

    /// <summary>符号等于 Flt64 / Symbol equal Flt64.</summary>
    public static LinearInequality<Flt64> Eq(ISymbol lhs, Flt64 rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.EQ);

    /// <summary>符号不等于 Flt64 / Symbol not equal Flt64.</summary>
    public static LinearInequality<Flt64> Ne(ISymbol lhs, Flt64 rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.NE);

    /// <summary>符号大于等于 Flt64 / Symbol greater than or equal Flt64.</summary>
    public static LinearInequality<Flt64> Ge(ISymbol lhs, Flt64 rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.GE);

    /// <summary>符号大于 Flt64 / Symbol greater than Flt64.</summary>
    public static LinearInequality<Flt64> Gt(ISymbol lhs, Flt64 rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.GT);

    // ===== Flt64 vs Symbol =====

    /// <summary>Flt64 小于符号 / Flt64 less than symbol.</summary>
    public static LinearInequality<Flt64> Lt(Flt64 lhs, ISymbol rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.LT);

    /// <summary>Flt64 小于等于符号 / Flt64 less than or equal symbol.</summary>
    public static LinearInequality<Flt64> Le(Flt64 lhs, ISymbol rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.LE);

    /// <summary>Flt64 等于符号 / Flt64 equal symbol.</summary>
    public static LinearInequality<Flt64> Eq(Flt64 lhs, ISymbol rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.EQ);

    /// <summary>Flt64 大于等于符号 / Flt64 greater than or equal symbol.</summary>
    public static LinearInequality<Flt64> Ge(Flt64 lhs, ISymbol rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.GE);

    /// <summary>Flt64 大于符号 / Flt64 greater than symbol.</summary>
    public static LinearInequality<Flt64> Gt(Flt64 lhs, ISymbol rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.GT);

    // ===== Symbol vs Symbol =====

    /// <summary>符号小于符号 / Symbol less than symbol.</summary>
    public static LinearInequality<Flt64> Lt(ISymbol lhs, ISymbol rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.LT);

    /// <summary>符号小于等于符号 / Symbol less than or equal symbol.</summary>
    public static LinearInequality<Flt64> Le(ISymbol lhs, ISymbol rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.LE);

    /// <summary>符号等于符号 / Symbol equal symbol.</summary>
    public static LinearInequality<Flt64> Eq(ISymbol lhs, ISymbol rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.EQ);

    /// <summary>符号大于等于符号 / Symbol greater than or equal symbol.</summary>
    public static LinearInequality<Flt64> Ge(ISymbol lhs, ISymbol rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.GE);

    /// <summary>符号大于符号 / Symbol greater than symbol.</summary>
    public static LinearInequality<Flt64> Gt(ISymbol lhs, ISymbol rhs)
        => new(Flt64QuickDsl.LinearPolynomial(lhs), Flt64QuickDsl.LinearPolynomial(rhs), Comparison.GT);

    // ===== Symbol vs int =====

    /// <summary>符号小于整数 / Symbol less than int.</summary>
    public static LinearInequality<Flt64> Lt(ISymbol lhs, int rhs) => Lt(lhs, new Flt64(rhs));

    /// <summary>符号小于等于整数 / Symbol less than or equal int.</summary>
    public static LinearInequality<Flt64> Le(ISymbol lhs, int rhs) => Le(lhs, new Flt64(rhs));

    /// <summary>符号等于整数 / Symbol equal int.</summary>
    public static LinearInequality<Flt64> Eq(ISymbol lhs, int rhs) => Eq(lhs, new Flt64(rhs));

    /// <summary>符号大于等于整数 / Symbol greater than or equal int.</summary>
    public static LinearInequality<Flt64> Ge(ISymbol lhs, int rhs) => Ge(lhs, new Flt64(rhs));

    /// <summary>符号大于整数 / Symbol greater than int.</summary>
    public static LinearInequality<Flt64> Gt(ISymbol lhs, int rhs) => Gt(lhs, new Flt64(rhs));
}
