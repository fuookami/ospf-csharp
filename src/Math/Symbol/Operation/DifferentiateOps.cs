#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Operation;

/// <summary>
/// 微分梯度运算 / Differentiation gradient operations.
/// 计算梯度向量（各偏导数列表）。
/// Computes gradient vectors (list of partial derivatives).
/// </summary>
public static class DifferentiateGradientOps {
    /// <summary>线性多项式的梯度 / Gradient of a linear polynomial.</summary>
    public static List<T> GradientLinear<T>(this LinearPolynomial<T> p, IReadOnlyList<ISymbol> order)
        where T : struct, IRing<T>, IRealNumber<T> {
        var gradient = new List<T>(order.Count);
        foreach (ISymbol sym in order) { gradient.Add(DifferentiateOps.Differentiate(p, sym)); }
        return gradient;
    }

    /// <summary>二次多项式的梯度 / Gradient of a quadratic polynomial.</summary>
    public static List<LinearPolynomial<T>> GradientQuadratic<T>(this QuadraticPolynomial<T> p, IReadOnlyList<ISymbol> order)
        where T : struct, IRing<T>, IRealNumber<T> {
        var gradient = new List<LinearPolynomial<T>>(order.Count);
        foreach (ISymbol sym in order) { gradient.Add(DifferentiateOps.Differentiate(p, sym)); }
        return gradient;
    }

    /// <summary>规范多项式的梯度 / Gradient of a canonical polynomial.</summary>
    public static List<CanonicalPolynomial<T>> GradientCanonical<T>(this CanonicalPolynomial<T> p, IReadOnlyList<ISymbol> order)
        where T : struct, IRing<T>, IRealNumber<T> {
        var gradient = new List<CanonicalPolynomial<T>>(order.Count);
        foreach (ISymbol sym in order) { gradient.Add(DifferentiateOps.Differentiate(p, sym)); }
        return gradient;
    }
}
