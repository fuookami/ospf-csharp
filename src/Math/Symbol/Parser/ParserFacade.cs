#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Operation;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Parser;
/// <summary>
/// 解析器门面 / Parser facade.
/// 薄包装层，统一入口分发到具体解析函数。
/// Thin wrapper providing a unified entry point dispatching to concrete parse functions.
/// </summary>
public static class Parser {
    /// <summary>
    /// 解析线性多项式（可能返回 null）/ Parse linear polynomial (may return null).
    /// </summary>
    public static LinearPolynomial<T>? ParseLinearPolynomialOrNull<T>(
        string text,
        IReadOnlyDictionary<string, ISymbol> symbolMap)
        where T : struct, Algebra.Concept.IRing<T> {
        // Flt64 specialization
        if (typeof(T) == typeof(Flt64)) {
            return ParseLinearFlt64OrNull(text, symbolMap) as LinearPolynomial<T>;
        }

        return null;
    }

    /// <summary>
    /// 解析二次多项式（可能返回 null）/ Parse quadratic polynomial (may return null).
    /// </summary>
    public static QuadraticPolynomial<T>? ParseQuadraticPolynomialOrNull<T>(
        string text,
        IReadOnlyDictionary<string, ISymbol> symbolMap)
        where T : struct, Algebra.Concept.IRing<T> {
        if (typeof(T) == typeof(Flt64)) {
            return ParseQuadraticFlt64OrNull(text, symbolMap) as QuadraticPolynomial<T>;
        }

        return null;
    }

    /// <summary>
    /// 解析 Flt64 线性多项式 / Parse Flt64 linear polynomial.
    /// </summary>
    public static LinearPolynomial<Flt64>? ParseLinearFlt64OrNull(
        string text,
        IReadOnlyDictionary<string, ISymbol> symbolMap)
        => ParseOps.ParseLinearFlt64(text, symbolMap);

    /// <summary>
    /// 解析 Flt64 二次多项式 / Parse Flt64 quadratic polynomial.
    /// </summary>
    public static QuadraticPolynomial<Flt64>? ParseQuadraticFlt64OrNull(
        string text,
        IReadOnlyDictionary<string, ISymbol> symbolMap)
        => ParseOps.ParseQuadraticFlt64(text, symbolMap);
}
