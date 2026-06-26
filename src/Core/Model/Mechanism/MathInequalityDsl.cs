#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;

namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 数学不等式 DSL 扩展方法 / Mathematical inequality DSL extension methods.
/// <para>提供从多项式与单项式之间构建约束的便捷 API。</para>
/// <para>Provides fluent API for building constraints from polynomials and monomials.</para>
/// </summary>
public static class MathInequalityDslExtensions {
    // ===== LinearMonomial <=> LinearPolynomial =====

    /// <summary>
    /// 线性单项式小于等于线性多项式 / Linear monomial less-than-or-equal linear polynomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端多项式 / Right-hand side polynomial</param>
    /// <returns>线性不等式 / Linear inequality</returns>
    public static LinearInequality<V> Leq<V>(this LinearMonomial<V> lhs, LinearPolynomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToLinearPolynomial().Le(rhs);

    /// <summary>
    /// 线性多项式小于等于线性单项式 / Linear polynomial less-than-or-equal linear monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端多项式 / Left-hand side polynomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>线性不等式 / Linear inequality</returns>
    public static LinearInequality<V> Leq<V>(this LinearPolynomial<V> lhs, LinearMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.Le(rhs.ToLinearPolynomial());

    /// <summary>
    /// 线性单项式大于等于线性多项式 / Linear monomial greater-than-or-equal linear polynomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端多项式 / Right-hand side polynomial</param>
    /// <returns>线性不等式 / Linear inequality</returns>
    public static LinearInequality<V> Geq<V>(this LinearMonomial<V> lhs, LinearPolynomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToLinearPolynomial().Ge(rhs);

    /// <summary>
    /// 线性多项式大于等于线性单项式 / Linear polynomial greater-than-or-equal linear monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端多项式 / Left-hand side polynomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>线性不等式 / Linear inequality</returns>
    public static LinearInequality<V> Geq<V>(this LinearPolynomial<V> lhs, LinearMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.Ge(rhs.ToLinearPolynomial());

    /// <summary>
    /// 线性单项式等于线性多项式 / Linear monomial equal linear polynomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端多项式 / Right-hand side polynomial</param>
    /// <returns>线性不等式 / Linear inequality</returns>
    public static LinearInequality<V> Eq<V>(this LinearMonomial<V> lhs, LinearPolynomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToLinearPolynomial().Eq(rhs);

    /// <summary>
    /// 线性多项式等于线性单项式 / Linear polynomial equal linear monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端多项式 / Left-hand side polynomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>线性不等式 / Linear inequality</returns>
    public static LinearInequality<V> Eq<V>(this LinearPolynomial<V> lhs, LinearMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.Eq(rhs.ToLinearPolynomial());

    // ===== QuadraticMonomial <=> QuadraticPolynomial =====

    /// <summary>
    /// 二次单项式小于等于二次多项式 / Quadratic monomial less-than-or-equal quadratic polynomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端多项式 / Right-hand side polynomial</param>
    /// <returns>二次不等式 / Quadratic inequality</returns>
    public static QuadraticInequalityOf<V> Leq<V>(this QuadraticMonomial<V> lhs, QuadraticPolynomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToQuadraticPolynomial().Le(rhs);

    /// <summary>
    /// 二次多项式小于等于二次单项式 / Quadratic polynomial less-than-or-equal quadratic monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端多项式 / Left-hand side polynomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>二次不等式 / Quadratic inequality</returns>
    public static QuadraticInequalityOf<V> Leq<V>(this QuadraticPolynomial<V> lhs, QuadraticMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.Le(rhs.ToQuadraticPolynomial());

    /// <summary>
    /// 二次单项式大于等于二次多项式 / Quadratic monomial greater-than-or-equal quadratic polynomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端多项式 / Right-hand side polynomial</param>
    /// <returns>二次不等式 / Quadratic inequality</returns>
    public static QuadraticInequalityOf<V> Geq<V>(this QuadraticMonomial<V> lhs, QuadraticPolynomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToQuadraticPolynomial().Ge(rhs);

    /// <summary>
    /// 二次多项式大于等于二次单项式 / Quadratic polynomial greater-than-or-equal quadratic monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端多项式 / Left-hand side polynomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>二次不等式 / Quadratic inequality</returns>
    public static QuadraticInequalityOf<V> Geq<V>(this QuadraticPolynomial<V> lhs, QuadraticMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.Ge(rhs.ToQuadraticPolynomial());

    /// <summary>
    /// 二次单项式等于二次多项式 / Quadratic monomial equal quadratic polynomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端多项式 / Right-hand side polynomial</param>
    /// <returns>二次不等式 / Quadratic inequality</returns>
    public static QuadraticInequalityOf<V> Eq<V>(this QuadraticMonomial<V> lhs, QuadraticPolynomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToQuadraticPolynomial().Eq(rhs);

    /// <summary>
    /// 二次多项式等于二次单项式 / Quadratic polynomial equal quadratic monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端多项式 / Left-hand side polynomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>二次不等式 / Quadratic inequality</returns>
    public static QuadraticInequalityOf<V> Eq<V>(this QuadraticPolynomial<V> lhs, QuadraticMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.Eq(rhs.ToQuadraticPolynomial());

    // ===== LinearMonomial <=> LinearMonomial (convenience) =====

    /// <summary>
    /// 线性单项式小于等于线性单项式 / Linear monomial less-than-or-equal linear monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>线性不等式 / Linear inequality</returns>
    public static LinearInequality<V> Leq<V>(this LinearMonomial<V> lhs, LinearMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToLinearPolynomial().Le(rhs.ToLinearPolynomial());

    /// <summary>
    /// 线性单项式大于等于线性单项式 / Linear monomial greater-than-or-equal linear monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>线性不等式 / Linear inequality</returns>
    public static LinearInequality<V> Geq<V>(this LinearMonomial<V> lhs, LinearMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToLinearPolynomial().Ge(rhs.ToLinearPolynomial());

    /// <summary>
    /// 线性单项式等于线性单项式 / Linear monomial equal linear monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>线性不等式 / Linear inequality</returns>
    public static LinearInequality<V> Eq<V>(this LinearMonomial<V> lhs, LinearMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToLinearPolynomial().Eq(rhs.ToLinearPolynomial());

    // ===== QuadraticMonomial <=> QuadraticMonomial (convenience) =====

    /// <summary>
    /// 二次单项式小于等于二次单项式 / Quadratic monomial less-than-or-equal quadratic monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>二次不等式 / Quadratic inequality</returns>
    public static QuadraticInequalityOf<V> Leq<V>(this QuadraticMonomial<V> lhs, QuadraticMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToQuadraticPolynomial().Le(rhs.ToQuadraticPolynomial());

    /// <summary>
    /// 二次单项式大于等于二次单项式 / Quadratic monomial greater-than-or-equal quadratic monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>二次不等式 / Quadratic inequality</returns>
    public static QuadraticInequalityOf<V> Geq<V>(this QuadraticMonomial<V> lhs, QuadraticMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToQuadraticPolynomial().Ge(rhs.ToQuadraticPolynomial());

    /// <summary>
    /// 二次单项式等于二次单项式 / Quadratic monomial equal quadratic monomial.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="lhs">左端单项式 / Left-hand side monomial</param>
    /// <param name="rhs">右端单项式 / Right-hand side monomial</param>
    /// <returns>二次不等式 / Quadratic inequality</returns>
    public static QuadraticInequalityOf<V> Eq<V>(this QuadraticMonomial<V> lhs, QuadraticMonomial<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        lhs.ToQuadraticPolynomial().Eq(rhs.ToQuadraticPolynomial());
}
