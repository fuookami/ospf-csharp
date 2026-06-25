#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Operation
{
    // ===== Polynomial Serde DTOs =====

    /// <summary>规范单项式数据 / Canonical monomial data.</summary>
    public sealed record CanonicalMonomialData(
        double Coefficient,
        Dictionary<string, int> Powers);

    /// <summary>规范多项式数据 / Canonical polynomial data.</summary>
    public sealed record CanonicalPolynomialData(
        List<CanonicalMonomialData> Monomials,
        double Constant);

    /// <summary>线性单项式数据 / Linear monomial data.</summary>
    public sealed record LinearMonomialData(
        double Coefficient,
        string Symbol);

    /// <summary>线性多项式数据 / Linear polynomial data.</summary>
    public sealed record LinearPolynomialData(
        List<LinearMonomialData> Monomials,
        double Constant);

    /// <summary>二次单项式数据 / Quadratic monomial data.</summary>
    public sealed record QuadraticMonomialData(
        double Coefficient,
        string Symbol1,
        string? Symbol2);

    /// <summary>二次多项式数据 / Quadratic polynomial data.</summary>
    public sealed record QuadraticPolynomialData(
        List<QuadraticMonomialData> Monomials,
        double Constant);

    // ===== Inequality Serde DTOs =====

    /// <summary>规范不等式数据 / Canonical inequality data.</summary>
    public sealed record CanonicalInequalityData(
        CanonicalPolynomialData Lhs,
        CanonicalPolynomialData Rhs,
        string Comparison);

    /// <summary>线性不等式数据 / Linear inequality data.</summary>
    public sealed record LinearInequalityData(
        LinearPolynomialData Lhs,
        LinearPolynomialData Rhs,
        string Comparison);

    /// <summary>二次不等式数据 / Quadratic inequality data.</summary>
    public sealed record QuadraticInequalityData(
        QuadraticPolynomialData Lhs,
        QuadraticPolynomialData Rhs,
        string Comparison);

    /// <summary>
    /// 序列化/反序列化运算 / Serialization/deserialization operations.
    /// 提供 Flt64 多项式和不等式的 JSON 序列化。
    /// Provides JSON serialization for Flt64 polynomials and inequalities.
    /// </summary>
    public static class SerdeOps
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        // ===== ToData =====

        /// <summary>线性多项式转数据 / Linear polynomial to data.</summary>
        public static LinearPolynomialData ToData(this LinearPolynomial<Flt64> p)
            => new(p.Monomials.Select(m => new LinearMonomialData(m.Coefficient.Value, m.Symbol.Name)).ToList(),
                   p.Constant.Value);

        /// <summary>二次多项式转数据 / Quadratic polynomial to data.</summary>
        public static QuadraticPolynomialData ToData(this QuadraticPolynomial<Flt64> p)
            => new(p.Monomials.Select(m => new QuadraticMonomialData(
                       m.Coefficient.Value, m.Symbol1.Name, m.Symbol2?.Name)).ToList(),
                   p.Constant.Value);

        /// <summary>规范多项式转数据 / Canonical polynomial to data.</summary>
        public static CanonicalPolynomialData ToData(this CanonicalPolynomial<Flt64> p)
            => new(p.Monomials.Select(m => new CanonicalMonomialData(
                       m.Coefficient.Value,
                       m.Powers.ToDictionary(kv => kv.Key.Name, kv => kv.Value))).ToList(),
                   p.Constant.Value);

        // ===== FromData =====

        /// <summary>从数据还原线性多项式（Flt64）/ Reconstruct Flt64 linear polynomial from data.</summary>
        public static LinearPolynomial<Flt64> FromData(LinearPolynomialData data, IReadOnlyDictionary<string, ISymbol> symbolMap)
        {
            var monomials = data.Monomials.Select(m =>
            {
                var sym = symbolMap[m.Symbol];
                return new LinearMonomial<Flt64>(new Flt64(m.Coefficient), sym);
            }).ToList();
            return new LinearPolynomial<Flt64>(monomials, new Flt64(data.Constant));
        }

        /// <summary>从数据还原二次多项式（Flt64）/ Reconstruct Flt64 quadratic polynomial from data.</summary>
        public static QuadraticPolynomial<Flt64> FromData(QuadraticPolynomialData data, IReadOnlyDictionary<string, ISymbol> symbolMap)
        {
            var monomials = data.Monomials.Select(m =>
            {
                var s1 = symbolMap[m.Symbol1];
                var s2 = m.Symbol2 is not null ? symbolMap[m.Symbol2] : null;
                return new QuadraticMonomial<Flt64>(new Flt64(m.Coefficient), s1, s2);
            }).ToList();
            return new QuadraticPolynomial<Flt64>(monomials, new Flt64(data.Constant));
        }

        // ===== JSON serialization =====

        /// <summary>线性多项式转 JSON / Linear polynomial to JSON.</summary>
        public static string ToJsonString(this LinearPolynomial<Flt64> p)
            => JsonSerializer.Serialize(p.ToData(), JsonOptions);

        /// <summary>二次多项式转 JSON / Quadratic polynomial to JSON.</summary>
        public static string ToJsonString(this QuadraticPolynomial<Flt64> p)
            => JsonSerializer.Serialize(p.ToData(), JsonOptions);

        /// <summary>规范多项式转 JSON / Canonical polynomial to JSON.</summary>
        public static string ToJsonString(this CanonicalPolynomial<Flt64> p)
            => JsonSerializer.Serialize(p.ToData(), JsonOptions);
    }
}
