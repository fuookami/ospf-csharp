#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol;
/// <summary>转换为线性多项式 / Convert to linear polynomial.</summary>
public interface IToLinearPolynomial<T> where T : struct, IRing<T> {
    /// <summary>转为线性多项式 / Convert to linear polynomial.</summary>
    LinearPolynomial<T> ToLinearPolynomial();
}

/// <summary>转换为二次多项式 / Convert to quadratic polynomial.</summary>
public interface IToQuadraticPolynomial<T> where T : struct, IRing<T> {
    /// <summary>转为二次多项式 / Convert to quadratic polynomial.</summary>
    QuadraticPolynomial<T> ToQuadraticPolynomial();
}

/// <summary>转换为规范多项式 / Convert to canonical polynomial.</summary>
public interface IToCanonicalPolynomial<T> where T : struct, IRing<T> {
    /// <summary>转为规范多项式 / Convert to canonical polynomial.</summary>
    CanonicalPolynomial<T> ToCanonicalPolynomial();
}

/// <summary>尝试转线性多项式 / Try to convert to linear polynomial (Kotlin TryToLinearPolynomial).</summary>
public interface ITryToLinearPolynomial<T> where T : struct, IRing<T> {
    /// <summary>尝试转为线性多项式，失败返回 null / Returns linear polynomial or null.</summary>
    LinearPolynomial<T>? ToLinearPolynomialOrNull();
}

/// <summary>尝试转二次多项式 / Try to convert to quadratic polynomial (Kotlin TryToQuadraticPolynomial).</summary>
public interface ITryToQuadraticPolynomial<T> where T : struct, IRing<T> {
    /// <summary>尝试转为二次多项式，失败返回 null / Returns quadratic polynomial or null.</summary>
    QuadraticPolynomial<T>? ToQuadraticPolynomialOrNull();
}
