#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;

namespace Fuookami.Ospf.Math.Symbol.Inequality;
/// <summary>
/// 线性不等式 / Linear Inequality.
/// </summary>
public sealed record LinearInequality<T>(
    LinearPolynomial<T> Lhs,
    LinearPolynomial<T> Rhs,
    Comparison Comparison,
    string Name = "",
    string DisplayName = ""
) where T : struct, IRing<T> {
    /// <summary>反转不等式 / Reverses the inequality (swap sides + reverse comparison).</summary>
    public LinearInequality<T> Reverse()
        => new(Rhs, Lhs, Comparison.Reverse(), Name, DisplayName);
}

/// <summary>
/// 线性不等式构建算子 / Linear-inequality builder operators.
/// C# has no infix; call as extension methods: poly1.Le(poly2), poly.Eq(scalar), etc.
/// </summary>
public static class LinearInequalityOps {
    // ===== polynomial vs polynomial =====

    /// <summary>小于 / Less than.</summary>
    public static LinearInequality<T> Lt<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => new(lhs, rhs, Comparison.LT);
    /// <summary>小于等于 / Less than or equal.</summary>
    public static LinearInequality<T> Le<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => new(lhs, rhs, Comparison.LE);
    /// <summary>等于 / Equal.</summary>
    public static LinearInequality<T> Eq<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => new(lhs, rhs, Comparison.EQ);
    /// <summary>不等于 / Not equal.</summary>
    public static LinearInequality<T> Ne<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => new(lhs, rhs, Comparison.NE);
    /// <summary>大于等于 / Greater than or equal.</summary>
    public static LinearInequality<T> Ge<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => new(lhs, rhs, Comparison.GE);
    /// <summary>大于 / Greater than.</summary>
    public static LinearInequality<T> Gt<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => new(lhs, rhs, Comparison.GT);

    // ===== aliases =====
    /// <summary>小于等于（别名）/ Less than or equal (alias).</summary>
    public static LinearInequality<T> Leq<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Le(rhs);
    /// <summary>大于等于（别名）/ Greater than or equal (alias).</summary>
    public static LinearInequality<T> Geq<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Ge(rhs);
    /// <summary>不等于（别名）/ Not equal (alias).</summary>
    public static LinearInequality<T> Neq<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Ne(rhs);
    /// <summary>小于（别名）/ Less than (alias).</summary>
    public static LinearInequality<T> Ls<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Lt(rhs);
    /// <summary>大于（别名）/ Greater than (alias).</summary>
    public static LinearInequality<T> Gr<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Gt(rhs);

    // ===== monomial vs monomial =====
    /// <summary>单项式小于等于单项式 / Monomial LE monomial.</summary>
    public static LinearInequality<T> Le<T>(this LinearMonomial<T> lhs, LinearMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsPolynomial().Le(rhs.AsPolynomial());
    /// <summary>单项式小于单项式 / Monomial LT monomial.</summary>
    public static LinearInequality<T> Lt<T>(this LinearMonomial<T> lhs, LinearMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsPolynomial().Lt(rhs.AsPolynomial());
    /// <summary>单项式等于单项式 / Monomial EQ monomial.</summary>
    public static LinearInequality<T> Eq<T>(this LinearMonomial<T> lhs, LinearMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsPolynomial().Eq(rhs.AsPolynomial());
    /// <summary>单项式不等于单项式 / Monomial NE monomial.</summary>
    public static LinearInequality<T> Ne<T>(this LinearMonomial<T> lhs, LinearMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsPolynomial().Ne(rhs.AsPolynomial());
    /// <summary>单项式大于等于单项式 / Monomial GE monomial.</summary>
    public static LinearInequality<T> Ge<T>(this LinearMonomial<T> lhs, LinearMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsPolynomial().Ge(rhs.AsPolynomial());
    /// <summary>单项式大于单项式 / Monomial GT monomial.</summary>
    public static LinearInequality<T> Gt<T>(this LinearMonomial<T> lhs, LinearMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsPolynomial().Gt(rhs.AsPolynomial());

    // ===== monomial vs polynomial =====
    /// <summary>单项式小于等于多项式 / Monomial LE polynomial.</summary>
    public static LinearInequality<T> Le<T>(this LinearMonomial<T> lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T>
        => lhs.AsPolynomial().Le(rhs);
    /// <summary>多项式小于等于单项式 / Polynomial LE monomial.</summary>
    public static LinearInequality<T> Le<T>(this LinearPolynomial<T> lhs, LinearMonomial<T> rhs) where T : struct, IRing<T>
        => lhs.Le(rhs.AsPolynomial());

    // ===== polynomial vs scalar =====
    /// <summary>多项式小于等于标量 / Polynomial LE scalar.</summary>
    public static LinearInequality<T> Le<T>(this LinearPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Le(AsLinearPolynomial(rhs));
    /// <summary>标量小于等于多项式 / Scalar LE polynomial.</summary>
    public static LinearInequality<T> Le<T>(this T lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T>
        => AsLinearPolynomial(lhs).Le(rhs);
    /// <summary>多项式小于标量 / Polynomial LT scalar.</summary>
    public static LinearInequality<T> Lt<T>(this LinearPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Lt(AsLinearPolynomial(rhs));
    /// <summary>标量小于多项式 / Scalar LT polynomial.</summary>
    public static LinearInequality<T> Lt<T>(this T lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T>
        => AsLinearPolynomial(lhs).Lt(rhs);
    /// <summary>多项式等于标量 / Polynomial EQ scalar.</summary>
    public static LinearInequality<T> Eq<T>(this LinearPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Eq(AsLinearPolynomial(rhs));
    /// <summary>标量等于多项式 / Scalar EQ polynomial.</summary>
    public static LinearInequality<T> Eq<T>(this T lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T>
        => AsLinearPolynomial(lhs).Eq(rhs);
    /// <summary>多项式不等于标量 / Polynomial NE scalar.</summary>
    public static LinearInequality<T> Ne<T>(this LinearPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Ne(AsLinearPolynomial(rhs));
    /// <summary>标量不等于多项式 / Scalar NE polynomial.</summary>
    public static LinearInequality<T> Ne<T>(this T lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T>
        => AsLinearPolynomial(lhs).Ne(rhs);
    /// <summary>多项式大于等于标量 / Polynomial GE scalar.</summary>
    public static LinearInequality<T> Ge<T>(this LinearPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Ge(AsLinearPolynomial(rhs));
    /// <summary>标量大于等于多项式 / Scalar GE polynomial.</summary>
    public static LinearInequality<T> Ge<T>(this T lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T>
        => AsLinearPolynomial(lhs).Ge(rhs);
    /// <summary>多项式大于标量 / Polynomial GT scalar.</summary>
    public static LinearInequality<T> Gt<T>(this LinearPolynomial<T> lhs, T rhs) where T : struct, IRing<T>
        => lhs.Gt(AsLinearPolynomial(rhs));
    /// <summary>标量大于多项式 / Scalar GT polynomial.</summary>
    public static LinearInequality<T> Gt<T>(this T lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T>
        => AsLinearPolynomial(lhs).Gt(rhs);

    // ===== scalar aliases =====
    /// <summary>标量小于等于多项式（别名）/ Scalar LE polynomial (alias).</summary>
    public static LinearInequality<T> Leq<T>(this T lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Le(rhs);
    /// <summary>标量大于等于多项式（别名）/ Scalar GE polynomial (alias).</summary>
    public static LinearInequality<T> Geq<T>(this T lhs, LinearPolynomial<T> rhs) where T : struct, IRing<T> => lhs.Ge(rhs);
    /// <summary>多项式小于等于标量（别名）/ Polynomial LE scalar (alias).</summary>
    public static LinearInequality<T> Leq<T>(this LinearPolynomial<T> lhs, T rhs) where T : struct, IRing<T> => lhs.Le(rhs);
    /// <summary>多项式大于等于标量（别名）/ Polynomial GE scalar (alias).</summary>
    public static LinearInequality<T> Geq<T>(this LinearPolynomial<T> lhs, T rhs) where T : struct, IRing<T> => lhs.Ge(rhs);

    // ===== named overloads (polynomial vs polynomial) =====
    /// <summary>命名小于 / Named less than.</summary>
    public static LinearInequality<T> Lt<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.LT, name, displayName);
    /// <summary>命名小于等于 / Named less than or equal.</summary>
    public static LinearInequality<T> Le<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.LE, name, displayName);
    /// <summary>命名等于 / Named equal.</summary>
    public static LinearInequality<T> Eq<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.EQ, name, displayName);
    /// <summary>命名大于等于 / Named greater than or equal.</summary>
    public static LinearInequality<T> Ge<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.GE, name, displayName);
    /// <summary>命名大于 / Named greater than.</summary>
    public static LinearInequality<T> Gt<T>(this LinearPolynomial<T> lhs, LinearPolynomial<T> rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, rhs, Comparison.GT, name, displayName);

    // ===== named overloads (polynomial vs scalar) =====
    /// <summary>命名多项式小于标量 / Named polynomial LT scalar.</summary>
    public static LinearInequality<T> Lt<T>(this LinearPolynomial<T> lhs, T rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, AsLinearPolynomial(rhs), Comparison.LT, name, displayName);
    /// <summary>命名多项式小于等于标量 / Named polynomial LE scalar.</summary>
    public static LinearInequality<T> Le<T>(this LinearPolynomial<T> lhs, T rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, AsLinearPolynomial(rhs), Comparison.LE, name, displayName);
    /// <summary>命名多项式等于标量 / Named polynomial EQ scalar.</summary>
    public static LinearInequality<T> Eq<T>(this LinearPolynomial<T> lhs, T rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, AsLinearPolynomial(rhs), Comparison.EQ, name, displayName);
    /// <summary>命名多项式大于等于标量 / Named polynomial GE scalar.</summary>
    public static LinearInequality<T> Ge<T>(this LinearPolynomial<T> lhs, T rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, AsLinearPolynomial(rhs), Comparison.GE, name, displayName);
    /// <summary>命名多项式大于标量 / Named polynomial GT scalar.</summary>
    public static LinearInequality<T> Gt<T>(this LinearPolynomial<T> lhs, T rhs, string name, string displayName = "") where T : struct, IRing<T>
        => new(lhs, AsLinearPolynomial(rhs), Comparison.GT, name, displayName);

    // ===== internal helpers =====
    internal static LinearPolynomial<T> AsPolynomial<T>(this LinearMonomial<T> m) where T : struct, IRing<T>
        => new(new[] { m }, LinearMonomial<T>.ZeroOf(m.Coefficient));

    internal static LinearPolynomial<T> AsLinearPolynomial<T>(T value) where T : struct, IRing<T>
        => new(Array.Empty<LinearMonomial<T>>(), value);
}
