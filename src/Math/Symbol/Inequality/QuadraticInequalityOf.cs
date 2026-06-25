#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;

namespace Fuookami.Ospf.Math.Symbol.Inequality;
/// <summary>
/// 二次不等式 / Quadratic Inequality (Kotlin data class QuadraticInequalityOf&lt;T&gt;).
/// </summary>
public sealed record QuadraticInequalityOf<T>(
    QuadraticPolynomial<T> Lhs,
    QuadraticPolynomial<T> Rhs,
    Comparison Comparison,
    string Name = "",
    string DisplayName = ""
) where T : struct, IRing<T> {
    /// <summary>反转不等式 / Reverses the inequality (swap sides + reverse comparison).</summary>
    public QuadraticInequalityOf<T> Reverse()
        => new(Rhs, Lhs, Comparison.Reverse(), Name, DisplayName);
}

/// <summary>
/// 二次不等式构建算子 / Quadratic-inequality builder operators.
/// </summary>
public static class QuadraticInequalityOps {
    // ===== QuadraticPolynomial x QuadraticPolynomial =====
    /// <summary>二次多项式小于等于二次多项式 / Quadratic polynomial LE quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Le<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.LE);
    /// <summary>二次多项式小于二次多项式 / Quadratic polynomial LT quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Lt<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.LT);
    /// <summary>二次多项式等于二次多项式 / Quadratic polynomial EQ quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Eq<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.EQ);
    /// <summary>二次多项式不等于二次多项式 / Quadratic polynomial NE quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Ne<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.NE);
    /// <summary>二次多项式大于等于二次多项式 / Quadratic polynomial GE quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Ge<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.GE);
    /// <summary>二次多项式大于二次多项式 / Quadratic polynomial GT quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Gt<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.GT);

    // ===== aliases =====
    /// <summary>别名 Leq / Alias Leq.</summary>
    public static QuadraticInequalityOf<T> Leq<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Le(rhs);
    /// <summary>别名 Geq / Alias Geq.</summary>
    public static QuadraticInequalityOf<T> Geq<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Ge(rhs);
    /// <summary>别名 Neq / Alias Neq.</summary>
    public static QuadraticInequalityOf<T> Neq<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Ne(rhs);
    /// <summary>别名 Ls / Alias Ls.</summary>
    public static QuadraticInequalityOf<T> Ls<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Lt(rhs);
    /// <summary>别名 Gr / Alias Gr.</summary>
    public static QuadraticInequalityOf<T> Gr<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Gt(rhs);

    // ===== QuadraticMonomial x QuadraticMonomial =====
    /// <summary>二次单项式小于等于二次单项式 / Quadratic monomial LE quadratic monomial.</summary>
    public static QuadraticInequalityOf<T> Le<T>(this QuadraticMonomial<T> lhs, QuadraticMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Le(rhs.AsQuadraticPolynomial());
    /// <summary>二次单项式小于二次单项式 / Quadratic monomial LT quadratic monomial.</summary>
    public static QuadraticInequalityOf<T> Lt<T>(this QuadraticMonomial<T> lhs, QuadraticMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Lt(rhs.AsQuadraticPolynomial());
    /// <summary>二次单项式等于二次单项式 / Quadratic monomial EQ quadratic monomial.</summary>
    public static QuadraticInequalityOf<T> Eq<T>(this QuadraticMonomial<T> lhs, QuadraticMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Eq(rhs.AsQuadraticPolynomial());
    /// <summary>二次单项式大于等于二次单项式 / Quadratic monomial GE quadratic monomial.</summary>
    public static QuadraticInequalityOf<T> Ge<T>(this QuadraticMonomial<T> lhs, QuadraticMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Ge(rhs.AsQuadraticPolynomial());
    /// <summary>二次单项式大于二次单项式 / Quadratic monomial GT quadratic monomial.</summary>
    public static QuadraticInequalityOf<T> Gt<T>(this QuadraticMonomial<T> lhs, QuadraticMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Gt(rhs.AsQuadraticPolynomial());

    // ===== QuadraticPolynomial x LinearMonomial =====
    /// <summary>二次多项式小于等于线性单项式 / Quadratic polynomial LE linear monomial.</summary>
    public static QuadraticInequalityOf<T> Le<T>(this QuadraticPolynomial<T> lhs, LinearMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.Le(rhs.AsQuadraticPolynomial());
    /// <summary>线性单项式小于等于二次多项式 / Linear monomial LE quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Le<T>(this LinearMonomial<T> lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Le(rhs);

    // ===== QuadraticPolynomial x scalar =====
    /// <summary>二次多项式小于等于标量 / Quadratic polynomial LE scalar.</summary>
    public static QuadraticInequalityOf<T> Le<T>(this QuadraticPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Le(rhs.AsQuadraticPolynomial());
    /// <summary>标量小于等于二次多项式 / Scalar LE quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Le<T>(this T lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Le(rhs);
    /// <summary>二次多项式小于标量 / Quadratic polynomial LT scalar.</summary>
    public static QuadraticInequalityOf<T> Lt<T>(this QuadraticPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Lt(rhs.AsQuadraticPolynomial());
    /// <summary>标量小于二次多项式 / Scalar LT quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Lt<T>(this T lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Lt(rhs);
    /// <summary>二次多项式等于标量 / Quadratic polynomial EQ scalar.</summary>
    public static QuadraticInequalityOf<T> Eq<T>(this QuadraticPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Eq(rhs.AsQuadraticPolynomial());
    /// <summary>标量等于二次多项式 / Scalar EQ quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Eq<T>(this T lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Eq(rhs);
    /// <summary>二次多项式大于等于标量 / Quadratic polynomial GE scalar.</summary>
    public static QuadraticInequalityOf<T> Ge<T>(this QuadraticPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Ge(rhs.AsQuadraticPolynomial());
    /// <summary>标量大于等于二次多项式 / Scalar GE quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Ge<T>(this T lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Ge(rhs);
    /// <summary>二次多项式大于标量 / Quadratic polynomial GT scalar.</summary>
    public static QuadraticInequalityOf<T> Gt<T>(this QuadraticPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Gt(rhs.AsQuadraticPolynomial());
    /// <summary>标量大于二次多项式 / Scalar GT quadratic polynomial.</summary>
    public static QuadraticInequalityOf<T> Gt<T>(this T lhs, QuadraticPolynomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsQuadraticPolynomial().Gt(rhs);

    // ===== named overloads =====
    /// <summary>命名二次多项式小于等于 / Named quadratic polynomial LE.</summary>
    public static QuadraticInequalityOf<T> Le<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.LE, name, displayName);
    /// <summary>命名二次多项式大于等于 / Named quadratic polynomial GE.</summary>
    public static QuadraticInequalityOf<T> Ge<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.GE, name, displayName);
    /// <summary>命名二次多项式等于 / Named quadratic polynomial EQ.</summary>
    public static QuadraticInequalityOf<T> Eq<T>(this QuadraticPolynomial<T> lhs, QuadraticPolynomial<T> rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.EQ, name, displayName);

    // ===== internal helpers =====
    internal static QuadraticPolynomial<T> AsQuadraticPolynomial<T>(this QuadraticMonomial<T> m) where T : struct, IRing<T>
        => new(new[] { m }, LinearMonomial<T>.ZeroOf(m.Coefficient));

    internal static QuadraticPolynomial<T> AsQuadraticPolynomial<T>(this LinearMonomial<T> m) where T : struct, IRing<T>
        => new(new[] { QuadraticMonomial<T>.Linear(m.Coefficient, m.Symbol) }, LinearMonomial<T>.ZeroOf(m.Coefficient));

    internal static QuadraticPolynomial<T> AsQuadraticPolynomial<T>(this T value) where T : struct, IRing<T>
        => new(Array.Empty<QuadraticMonomial<T>>(), value);
}
