#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Operation;
/// <summary>
/// 归一化配置 / Normalize configuration.
/// 控制多项式归一化行为的选项。
/// Options controlling polynomial normalization behavior.
/// </summary>
public sealed record NormalizeConfig {
    /// <summary>是否合并同类项 / Whether to combine like terms.</summary>
    public bool CombineTerms { get; init; } = true;

    /// <summary>是否排序单项式 / Whether to sort monomials.</summary>
    public bool SortMonomials { get; init; } = false;

    /// <summary>是否去除零系数项 / Whether to remove zero-coefficient terms.</summary>
    public bool RemoveZeroTerms { get; init; } = true;

    /// <summary>默认配置 / Default configuration.</summary>
    public static readonly NormalizeConfig Default = new();
}

/// <summary>
/// 归一化运算 / Normalize operations.
/// 合并同类项、去除零系数项、排序。
/// Combines like terms, removes zero-coefficient terms, and optionally sorts.
/// </summary>
public static class NormalizeOps {
    // ===== LinearPolynomial normalize =====

    /// <summary>归一化线性多项式 / Normalize linear polynomial.</summary>
    public static LinearPolynomial<T> Normalize<T>(
        this LinearPolynomial<T> p,
        NormalizeConfig? config = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        config ??= NormalizeConfig.Default;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;

        if (config.CombineTerms) {
            // Group by symbol and sum coefficients
            var grouped = new Dictionary<ISymbol, T>(p.Monomials.Count);
            foreach (LinearMonomial<T> m in p.Monomials) {
                if (grouped.TryGetValue(m.Symbol, out T existing)) {
                    grouped[m.Symbol] = existing.Plus(m.Coefficient);
                }
                else {
                    grouped[m.Symbol] = m.Coefficient;
                }
            }

            var result = grouped
                .Where(kv => !config.RemoveZeroTerms || !kv.Value.Eq(zero))
                .Select(kv => new LinearMonomial<T>(kv.Value, kv.Key))
                .ToList();

            if (config.SortMonomials) {
                result.Sort((a, b) => SymbolIdentity.DefaultSymbolComparator.Compare(a.Symbol, b.Symbol));
            }

            return new LinearPolynomial<T>(result, p.Constant);
        }

        if (config.RemoveZeroTerms) {
            var filtered = p.Monomials.Where(m => !m.Coefficient.Eq(zero)).ToList();
            return new LinearPolynomial<T>(filtered, p.Constant);
        }

        return p;
    }

    // ===== QuadraticPolynomial normalize =====

    /// <summary>归一化二次多项式 / Normalize quadratic polynomial.</summary>
    public static QuadraticPolynomial<T> Normalize<T>(
        this QuadraticPolynomial<T> p,
        NormalizeConfig? config = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        config ??= NormalizeConfig.Default;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;

        if (config.CombineTerms) {
            var grouped = new Dictionary<(ISymbol, ISymbol?), T>();
            foreach (QuadraticMonomial<T> m in p.Monomials) {
                (ISymbol Symbol1, ISymbol? Symbol2) key = (m.Symbol1, m.Symbol2);
                if (grouped.TryGetValue(key, out T existing)) {
                    grouped[key] = existing.Plus(m.Coefficient);
                }
                else {
                    grouped[key] = m.Coefficient;
                }
            }

            var result = grouped
                .Where(kv => !config.RemoveZeroTerms || !kv.Value.Eq(zero))
                .Select(kv => new QuadraticMonomial<T>(kv.Value, kv.Key.Item1, kv.Key.Item2))
                .ToList();

            return new QuadraticPolynomial<T>(result, p.Constant);
        }

        if (config.RemoveZeroTerms) {
            var filtered = p.Monomials.Where(m => !m.Coefficient.Eq(zero)).ToList();
            return new QuadraticPolynomial<T>(filtered, p.Constant);
        }

        return p;
    }

    // ===== CanonicalPolynomial normalize =====

    /// <summary>归一化规范多项式 / Normalize canonical polynomial.</summary>
    public static CanonicalPolynomial<T> Normalize<T>(
        this CanonicalPolynomial<T> p,
        NormalizeConfig? config = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        config ??= NormalizeConfig.Default;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;

        if (config.CombineTerms) {
            // Use PowerVectorKey for grouping
            var symbolIndex = new Dictionary<ISymbol, int>();
            var symbolList = new List<ISymbol>();
            foreach (CanonicalMonomial<T> m in p.Monomials) {
                foreach (ISymbol s in m.Powers.Keys) {
                    if (!symbolIndex.ContainsKey(s)) {
                        symbolIndex[s] = symbolList.Count;
                        symbolList.Add(s);
                    }
                }
            }

            var grouped = new Dictionary<PowerVectorKey, (T Coefficient, IReadOnlyDictionary<ISymbol, int> Powers)>();
            foreach (CanonicalMonomial<T> m in p.Monomials) {
                var key = PowerVectorKey.Create(
                    (IReadOnlyDictionary<ISymbol, int>)m.Powers,
                    (IReadOnlyDictionary<ISymbol, int>)symbolIndex,
                    symbolList.Count);

                if (grouped.TryGetValue(key, out (T Coefficient, IReadOnlyDictionary<ISymbol, int> Powers) existing)) {
                    grouped[key] = (existing.Coefficient.Plus(m.Coefficient), existing.Powers);
                }
                else {
                    grouped[key] = (m.Coefficient, m.Powers);
                }
            }

            var result = grouped.Values
                .Where(kv => !config.RemoveZeroTerms || !kv.Coefficient.Eq(zero))
                .Select(kv => new CanonicalMonomial<T>(kv.Coefficient, kv.Powers))
                .ToList();

            return new CanonicalPolynomial<T>(result, p.Constant);
        }

        if (config.RemoveZeroTerms) {
            var filtered = p.Monomials.Where(m => !m.Coefficient.Eq(zero)).ToList();
            return new CanonicalPolynomial<T>(filtered, p.Constant);
        }

        return p;
    }
}
