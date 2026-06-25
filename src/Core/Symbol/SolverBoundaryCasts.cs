#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Symbol;
/// <summary>
/// 求解器边界类型转换 / Solver boundary type casts.
/// <para>提供运行时求解器边界的 Flt64 类型转换工具，将泛型引用转换回 Flt64 以调用泛型方法。</para>
/// <para>Provides runtime solver-boundary Flt64 type cast utilities, converting generic
/// references back to Flt64 for calling generic methods.</para>
/// </summary>
internal static class SolverBoundaryCasts {
    /// <summary>创建目标类型的完整表达式值域（无界）/ Create full (unbounded) expression range for target type.</summary>
    public static ExpressionRange<Flt64> FullExpressionRange() => ExpressionRange<Flt64>.Create();

    /// <summary>使用转换器将 Flt64 值映射转换为目标类型值映射 / Convert Flt64 value map to target type value map using converter.</summary>
    public static IReadOnlyDictionary<ISymbol, V> MapValues<V>(IReadOnlyDictionary<ISymbol, Flt64> values, IFlt64ValueConverter<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var result = new Dictionary<ISymbol, V>(values.Count);
        foreach ((ISymbol? k, Flt64 v) in values) {
            result[k] = converter.IntoValue(v);
        }

        return result;
    }

    /// <summary>将依赖符号转换为目标类型中间符号 / Cast dependency symbol to target type intermediate symbol.</summary>
    public static IIntermediateSymbol<V> DependencyAsIntermediate<V>(IIntermediateSymbol dependency)
        where V : struct, IRealNumber<V>, INumberField<V> => (IIntermediateSymbol<V>)dependency;

    /// <summary>将可变线性多项式转换为 Flt64 类型 / Convert mutable linear polynomial to Flt64 type.</summary>
    public static MutableLinearPolynomial<Flt64> LinearPolynomialAsFlt64<V>(MutableLinearPolynomial<V> polynomial)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var monomials = new List<LinearMonomial<Flt64>>(polynomial.Monomials.Count);
        foreach (LinearMonomial<V> m in polynomial.Monomials) {
            monomials.Add(new LinearMonomial<Flt64>(m.Coefficient.ToFlt64(), m.Symbol));
        }

        return new MutableLinearPolynomial<Flt64>(monomials, polynomial.Constant.ToFlt64());
    }

    /// <summary>将可变二次多项式转换为 Flt64 类型 / Convert mutable quadratic polynomial to Flt64 type.</summary>
    public static MutableQuadraticPolynomial<Flt64> QuadraticPolynomialAsFlt64<V>(MutableQuadraticPolynomial<V> polynomial)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var monomials = new List<QuadraticMonomial<Flt64>>(polynomial.Monomials.Count);
        foreach (QuadraticMonomial<V> m in polynomial.Monomials) {
            monomials.Add(new QuadraticMonomial<Flt64>(m.Coefficient.ToFlt64(), m.Symbol1, m.Symbol2));
        }

        return new MutableQuadraticPolynomial<Flt64>(monomials, constant: polynomial.Constant.ToFlt64());
    }

    /// <summary>将对象安全转换为线性不等式 / Safely cast object to linear inequality.</summary>
    public static LinearInequality<V>? LinearInequalityAs<V>(object? cut)
        where V : struct, IRealNumber<V>, INumberField<V> => cut as LinearInequality<V>;

    /// <summary>将对象安全转换为二次不等式 / Safely cast object to quadratic inequality.</summary>
    public static QuadraticInequalityOf<V>? QuadraticInequalityAs<V>(object? cut)
        where V : struct, IRealNumber<V>, INumberField<V> => cut as QuadraticInequalityOf<V>;
}
