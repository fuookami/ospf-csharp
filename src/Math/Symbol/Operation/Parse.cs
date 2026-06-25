#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Math.Symbol.Parse;

namespace Fuookami.Ospf.Math.Symbol.Operation
{
    /// <summary>
    /// Flt64 多项式和不等式解析器 / Flt64 polynomial and inequality parser.
    /// 提供从字符串解析线性/二次/规范多项式和不等式的功能。
    /// Provides parsing of linear/quadratic/canonical polynomials and inequalities from strings.
    /// </summary>
    public static class ParseOps
    {
        // ===== LinearPolynomial parsing =====

        /// <summary>
        /// 解析 Flt64 线性多项式 / Parse Flt64 linear polynomial.
        /// 格式: "3x + 2y - 5" or "3*x + 2*y - 5"
        /// </summary>
        public static LinearPolynomial<Flt64>? ParseLinearFlt64(
            string text,
            IReadOnlyDictionary<string, ISymbol> symbolMap)
        {
            var terms = ParseTerms(text, symbolMap);
            if (terms is null) return null;

            var monomials = new List<LinearMonomial<Flt64>>();
            var constant = Flt64.Zero;

            foreach (var (coeff, symbols) in terms)
            {
                if (symbols.Count == 0)
                    constant = constant.Plus(coeff);
                else if (symbols.Count == 1)
                    monomials.Add(new LinearMonomial<Flt64>(coeff, symbols[0]));
                else
                    return null; // Not linear
            }

            return new LinearPolynomial<Flt64>(monomials, constant);
        }

        // ===== QuadraticPolynomial parsing =====

        /// <summary>
        /// 解析 Flt64 二次多项式 / Parse Flt64 quadratic polynomial.
        /// </summary>
        public static QuadraticPolynomial<Flt64>? ParseQuadraticFlt64(
            string text,
            IReadOnlyDictionary<string, ISymbol> symbolMap)
        {
            var terms = ParseTerms(text, symbolMap);
            if (terms is null) return null;

            var monomials = new List<QuadraticMonomial<Flt64>>();
            var constant = Flt64.Zero;

            foreach (var (coeff, symbols) in terms)
            {
                if (symbols.Count == 0)
                    constant = constant.Plus(coeff);
                else if (symbols.Count == 1)
                    monomials.Add(QuadraticMonomial<Flt64>.Linear(coeff, symbols[0]));
                else if (symbols.Count == 2)
                    monomials.Add(QuadraticMonomial<Flt64>.Quadratic(coeff, symbols[0], symbols[1]));
                else
                    return null; // Not quadratic
            }

            return new QuadraticPolynomial<Flt64>(monomials, constant);
        }

        // ===== Inequality parsing =====

        /// <summary>
        /// 解析 Flt64 线性不等式 / Parse Flt64 linear inequality.
        /// 格式: "3x + 2y <= 5"
        /// </summary>
        public static LinearInequality<Flt64>? ParseLinearInequalityFlt64(
            string text,
            IReadOnlyDictionary<string, ISymbol> symbolMap)
        {
            var split = SplitInequality(text);
            if (split is null) return null;
            var (lhsText, comp, rhsText) = split.Value;

            var lhs = ParseLinearFlt64(lhsText, symbolMap);
            var rhs = ParseLinearFlt64(rhsText, symbolMap);
            if (lhs is null || rhs is null) return null;

            return new LinearInequality<Flt64>(lhs, rhs, comp.Value);
        }

        // ===== Internal parsing helpers =====

        private static (string Lhs, Comparison? Comp, string Rhs)? SplitInequality(string text)
        {
            var operators = new[] { "<=", ">=", "!=", "<", ">", "=" };
            foreach (var op in operators)
            {
                var idx = text.IndexOf(op, StringComparison.Ordinal);
                if (idx < 0) continue;

                var comp = op switch
                {
                    "<=" => Comparison.LE,
                    ">=" => Comparison.GE,
                    "!=" => Comparison.NE,
                    "<" => Comparison.LT,
                    ">" => Comparison.GT,
                    "=" => Comparison.EQ,
                    _ => (Comparison?)null
                };

                if (comp is not null)
                    return (text[..idx].Trim(), comp, text[(idx + op.Length)..].Trim());
            }
            return null;
        }

        private static List<(Flt64 Coefficient, List<ISymbol> Symbols)>? ParseTerms(
            string text,
            IReadOnlyDictionary<string, ISymbol> symbolMap)
        {
            var result = new List<(Flt64, List<ISymbol>)>();
            // Simple tokenization: split on + and -
            var tokens = Tokenize(text);
            if (tokens is null) return null;

            foreach (var token in tokens)
            {
                var (coeff, symbols) = ParseSingleTerm(token, symbolMap);
                if (coeff is null) return null;
                result.Add((coeff.Value, symbols));
            }

            return result;
        }

        private static List<string>? Tokenize(string text)
        {
            text = text.Replace(" ", "");
            if (string.IsNullOrEmpty(text)) return new List<string>();

            var tokens = new List<string>();
            var start = 0;
            for (var i = 1; i < text.Length; i++)
            {
                if (text[i] is '+' or '-')
                {
                    tokens.Add(text[start..i]);
                    start = i;
                }
            }
            tokens.Add(text[start..]);
            return tokens;
        }

        private static (Flt64? Coefficient, List<ISymbol> Symbols) ParseSingleTerm(
            string token,
            IReadOnlyDictionary<string, ISymbol> symbolMap)
        {
            if (string.IsNullOrEmpty(token)) return (null, new List<ISymbol>());

            var symbols = new List<ISymbol>();
            var remaining = token;

            // Extract coefficient
            var coeffStr = "";
            var i = 0;
            if (remaining.Length > 0 && remaining[0] is '+' or '-')
            {
                coeffStr += remaining[0];
                i = 1;
            }
            for (; i < remaining.Length; i++)
            {
                if (remaining[i] is >= '0' and <= '9' or '.')
                    coeffStr += remaining[i];
                else
                    break;
            }
            remaining = remaining[i..];

            if (remaining.Contains('*'))
            {
                // Format: coeff*sym1*sym2 or sym1*sym2
                var parts = remaining.Split('*');
                foreach (var part in parts)
                {
                    if (symbolMap.TryGetValue(part.Trim(), out var sym))
                        symbols.Add(sym);
                    else if (string.IsNullOrEmpty(coeffStr))
                        coeffStr = part.Trim();
                }
            }
            else
            {
                // Try to find symbol in remaining
                foreach (var (name, sym) in symbolMap)
                {
                    if (remaining.Contains(name))
                    {
                        symbols.Add(sym);
                        remaining = remaining.Replace(name, "");
                    }
                }
            }

            if (string.IsNullOrEmpty(coeffStr)) coeffStr = "1";
            if (coeffStr is "+" or "") coeffStr = "1";
            if (coeffStr == "-") coeffStr = "-1";

            if (!double.TryParse(coeffStr, out var coeff))
                return (null, symbols);

            return (new Flt64(coeff), symbols);
        }
    }
}
