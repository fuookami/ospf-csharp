#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Symbol.Operation
{
    /// <summary>
    /// 线性多项式矩阵形式 / Linear polynomial matrix form c^T * x + d.
    /// </summary>
    /// <typeparam name="T">系数环类型 / Coefficient ring type.</typeparam>
    public sealed record LinearMatrixForm<T>(
        IReadOnlyList<T> C,
        T D,
        IReadOnlyList<ISymbol> Order)
        where T : struct, IRing<T>, IRealNumber<T>;

    /// <summary>
    /// 二次多项式矩阵形式 / Quadratic polynomial matrix form x^T * Q * x + c^T * x + d.
    /// </summary>
    /// <typeparam name="T">系数环类型 / Coefficient ring type.</typeparam>
    public sealed record QuadraticMatrixForm<T>(
        IReadOnlyList<IReadOnlyList<T>> Q,
        IReadOnlyList<T> C,
        T D,
        IReadOnlyList<ISymbol> Order)
        where T : struct, IRing<T>, IRealNumber<T>;

    /// <summary>
    /// 矩阵形式转换运算 / Matrix form conversion operations.
    /// 提供将多项式转换为矩阵形式（c/d 向量和 Q 矩阵）的功能。
    /// Provides conversion of polynomials to matrix form (c/d vectors and Q matrix).
    /// </summary>
    public static class MatrixFormOps
    {
        // ===== Validation =====

        private static Result<Success, ErrorCode, Error<ErrorCode>> ValidateOrder(IReadOnlyList<ISymbol> order)
        {
            var seen = new HashSet<ISymbol>();
            foreach (var s in order)
            {
                if (!seen.Add(s))
                    return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                        ErrorCode.IllegalArgument, "Symbol order contains duplicated symbols.");
            }
            return Results.OkInstance;
        }

        private static Result<int, ErrorCode, Error<ErrorCode>> RequireSymbolIndex(
            ISymbol symbol, IReadOnlyDictionary<ISymbol, int> indexOfSymbol)
        {
            return indexOfSymbol.TryGetValue(symbol, out var i)
                ? new Ok<int, ErrorCode, Error<ErrorCode>>(i)
                : new Failed<int, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.DataNotFound, $"Symbol {symbol.Name} not found in order.");
        }

        // ===== LinearPolynomial -> LinearMatrixForm =====

        /// <summary>将线性多项式转换为矩阵形式 / Convert linear polynomial to matrix form.</summary>
        public static Result<LinearMatrixForm<T>, ErrorCode, Error<ErrorCode>> ToMatrixForm<T>(
            this LinearPolynomial<T> p,
            IReadOnlyList<ISymbol> order,
            T zero)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var validation = ValidateOrder(order);
            if (validation.IsFailed)
                return new Failed<LinearMatrixForm<T>, ErrorCode, Error<ErrorCode>>(
                    ((Failed<Success, ErrorCode, Error<ErrorCode>>)validation).Error);

            var source = p.CombineLinearTerms(zero);
            var c = new T[order.Count];
            for (var i = 0; i < c.Length; i++) c[i] = zero;

            var indexOfSymbol = new Dictionary<ISymbol, int>(order.Count);
            for (var i = 0; i < order.Count; i++) indexOfSymbol[order[i]] = i;

            foreach (var m in source.Monomials)
            {
                var idxResult = RequireSymbolIndex(m.Symbol, indexOfSymbol);
                if (idxResult.IsFailed)
                    return new Failed<LinearMatrixForm<T>, ErrorCode, Error<ErrorCode>>(
                        ((Failed<int, ErrorCode, Error<ErrorCode>>)idxResult).Error);
                c[idxResult.Value] = c[idxResult.Value].Plus(m.Coefficient);
            }

            return new Ok<LinearMatrixForm<T>, ErrorCode, Error<ErrorCode>>(
                new LinearMatrixForm<T>(c, source.Constant, order));
        }

        // ===== LinearMatrixForm -> LinearPolynomial =====

        /// <summary>从矩阵形式还原线性多项式 / Reconstruct linear polynomial from matrix form.</summary>
        public static LinearPolynomial<T> LinearPolynomialFromMatrixForm<T>(
            IReadOnlyList<T> c, T d, IReadOnlyList<ISymbol> order, T zero)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var monomials = new List<LinearMonomial<T>>(order.Count);
            for (var i = 0; i < order.Count; i++)
            {
                if (!c[i].Eq(zero))
                    monomials.Add(new LinearMonomial<T>(c[i], order[i]));
            }
            return new LinearPolynomial<T>(monomials, d).CombineLinearTerms(zero);
        }

        /// <summary>从线性矩阵形式还原多项式 / Reconstruct polynomial from linear matrix form.</summary>
        public static LinearPolynomial<T> LinearPolynomialFromMatrixForm<T>(
            LinearMatrixForm<T> form, T zero)
            where T : struct, IRing<T>, IRealNumber<T>
            => LinearPolynomialFromMatrixForm(form.C, form.D, form.Order, zero);

        // ===== QuadraticPolynomial -> QuadraticMatrixForm =====

        /// <summary>将二次多项式转换为矩阵形式 / Convert quadratic polynomial to matrix form.</summary>
        public static Result<QuadraticMatrixForm<T>, ErrorCode, Error<ErrorCode>> ToMatrixForm<T>(
            this QuadraticPolynomial<T> p,
            IReadOnlyList<ISymbol> order,
            T zero,
            System.Func<T, (T Left, T Right)> splitOffDiagonal)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var validation = ValidateOrder(order);
            if (validation.IsFailed)
                return new Failed<QuadraticMatrixForm<T>, ErrorCode, Error<ErrorCode>>(
                    ((Failed<Success, ErrorCode, Error<ErrorCode>>)validation).Error);

            var source = p.CombineQuadraticTerms(zero);
            var n = order.Count;
            var q = new T[n][];
            for (var i = 0; i < n; i++)
            {
                q[i] = new T[n];
                for (var j = 0; j < n; j++) q[i][j] = zero;
            }
            var c = new T[n];
            for (var i = 0; i < n; i++) c[i] = zero;

            var indexOfSymbol = new Dictionary<ISymbol, int>(n);
            for (var i = 0; i < n; i++) indexOfSymbol[order[i]] = i;

            foreach (var m in source.Monomials)
            {
                if (m.IsQuadratic)
                {
                    var iResult = RequireSymbolIndex(m.Symbol1, indexOfSymbol);
                    if (iResult.IsFailed)
                        return new Failed<QuadraticMatrixForm<T>, ErrorCode, Error<ErrorCode>>(
                            ((Failed<int, ErrorCode, Error<ErrorCode>>)iResult).Error);
                    var jResult = RequireSymbolIndex(m.Symbol2!, indexOfSymbol);
                    if (jResult.IsFailed)
                        return new Failed<QuadraticMatrixForm<T>, ErrorCode, Error<ErrorCode>>(
                            ((Failed<int, ErrorCode, Error<ErrorCode>>)jResult).Error);

                    var i = iResult.Value;
                    var j = jResult.Value;
                    if (i == j)
                    {
                        q[i][j] = q[i][j].Plus(m.Coefficient);
                    }
                    else
                    {
                        var (left, right) = splitOffDiagonal(m.Coefficient);
                        q[i][j] = q[i][j].Plus(left);
                        q[j][i] = q[j][i].Plus(right);
                    }
                }
                else
                {
                    var iResult = RequireSymbolIndex(m.Symbol1, indexOfSymbol);
                    if (iResult.IsFailed)
                        return new Failed<QuadraticMatrixForm<T>, ErrorCode, Error<ErrorCode>>(
                            ((Failed<int, ErrorCode, Error<ErrorCode>>)iResult).Error);
                    c[iResult.Value] = c[iResult.Value].Plus(m.Coefficient);
                }
            }

            return new Ok<QuadraticMatrixForm<T>, ErrorCode, Error<ErrorCode>>(
                new QuadraticMatrixForm<T>(q, c, source.Constant, order));
        }

        // ===== QuadraticMatrixForm -> QuadraticPolynomial =====

        /// <summary>从矩阵形式还原二次多项式 / Reconstruct quadratic polynomial from matrix form.</summary>
        public static QuadraticPolynomial<T> QuadraticPolynomialFromMatrixForm<T>(
            IReadOnlyList<IReadOnlyList<T>> q, IReadOnlyList<T> c, T d,
            IReadOnlyList<ISymbol> order, T zero,
            System.Func<T, T, T>? mergeOffDiagonal = null)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            mergeOffDiagonal ??= (lhs, rhs) => lhs.Plus(rhs);
            var monomials = new List<QuadraticMonomial<T>>();
            for (var i = 0; i < order.Count; i++)
            {
                if (!q[i][i].Eq(zero))
                    monomials.Add(QuadraticMonomial<T>.Quadratic(q[i][i], order[i], order[i]));
                if (!c[i].Eq(zero))
                    monomials.Add(QuadraticMonomial<T>.Linear(c[i], order[i]));
                for (var j = i + 1; j < order.Count; j++)
                {
                    var coefficient = mergeOffDiagonal(q[i][j], q[j][i]);
                    if (!coefficient.Eq(zero))
                        monomials.Add(QuadraticMonomial<T>.Quadratic(coefficient, order[i], order[j]));
                }
            }
            return new QuadraticPolynomial<T>(monomials, d).CombineQuadraticTerms(zero);
        }

        /// <summary>从二次矩阵形式还原多项式 / Reconstruct polynomial from quadratic matrix form.</summary>
        public static QuadraticPolynomial<T> QuadraticPolynomialFromMatrixForm<T>(
            QuadraticMatrixForm<T> form, T zero,
            System.Func<T, T, T>? mergeOffDiagonal = null)
            where T : struct, IRing<T>, IRealNumber<T>
            => QuadraticPolynomialFromMatrixForm(form.Q, form.C, form.D, form.Order, zero, mergeOffDiagonal);

        // ===== CanonicalPolynomial -> QuadraticMatrixForm =====

        /// <summary>将规范多项式转换为矩阵形式（需为二次以下）/ Convert canonical polynomial to matrix form (must be at most quadratic).</summary>
        public static Result<QuadraticMatrixForm<T>, ErrorCode, Error<ErrorCode>> ToMatrixForm<T>(
            this CanonicalPolynomial<T> p,
            IReadOnlyList<ISymbol> order,
            T zero,
            System.Func<T, (T Left, T Right)> splitOffDiagonal)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var quadratic = p.ToQuadraticPolynomialOrNull();
            if (quadratic is null)
                return new Failed<QuadraticMatrixForm<T>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument, "Canonical polynomial is not quadratic.");
            return quadratic.ToMatrixForm(order, zero, splitOffDiagonal);
        }
    }

    /// <summary>
    /// 合并同类项扩展 / Combine like terms extensions (internal helpers for MatrixForm).
    /// </summary>
    internal static class CombineTermsInternal
    {
        internal static LinearPolynomial<T> CombineLinearTerms<T>(this LinearPolynomial<T> p, T zero)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var grouped = new Dictionary<ISymbol, T>();
            foreach (var m in p.Monomials)
            {
                if (grouped.TryGetValue(m.Symbol, out var existing))
                    grouped[m.Symbol] = existing.Plus(m.Coefficient);
                else
                    grouped[m.Symbol] = m.Coefficient;
            }
            var monomials = grouped
                .Where(kv => !kv.Value.Eq(zero))
                .Select(kv => new LinearMonomial<T>(kv.Value, kv.Key))
                .ToList();
            return new LinearPolynomial<T>(monomials, p.Constant);
        }

        internal static QuadraticPolynomial<T> CombineQuadraticTerms<T>(this QuadraticPolynomial<T> p, T zero)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var grouped = new Dictionary<(ISymbol, ISymbol?), T>();
            foreach (var m in p.Monomials)
            {
                var key = (m.Symbol1, m.Symbol2);
                if (grouped.TryGetValue(key, out var existing))
                    grouped[key] = existing.Plus(m.Coefficient);
                else
                    grouped[key] = m.Coefficient;
            }
            var monomials = grouped
                .Where(kv => !kv.Value.Eq(zero))
                .Select(kv => new QuadraticMonomial<T>(kv.Value, kv.Key.Item1, kv.Key.Item2))
                .ToList();
            return new QuadraticPolynomial<T>(monomials, p.Constant);
        }

        internal static CanonicalPolynomial<T> CombineCanonicalPolynomialTerms<T>(this CanonicalPolynomial<T> p, T zero)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var symbolIndex = new Dictionary<ISymbol, int>();
            var symbolList = new List<ISymbol>();
            foreach (var m in p.Monomials)
            {
                foreach (var s in m.Powers.Keys)
                {
                    if (!symbolIndex.ContainsKey(s))
                    {
                        symbolIndex[s] = symbolList.Count;
                        symbolList.Add(s);
                    }
                }
            }

            var grouped = new Dictionary<PowerVectorKey, (T Coefficient, IReadOnlyDictionary<ISymbol, int> Powers)>();
            foreach (var m in p.Monomials)
            {
                var key = PowerVectorKey.Create(
                    (IReadOnlyDictionary<ISymbol, int>)m.Powers,
                    (IReadOnlyDictionary<ISymbol, int>)symbolIndex,
                    symbolList.Count);

                if (grouped.TryGetValue(key, out var existing))
                    grouped[key] = (existing.Coefficient.Plus(m.Coefficient), existing.Powers);
                else
                    grouped[key] = (m.Coefficient, m.Powers);
            }

            var result = grouped.Values
                .Where(kv => !kv.Coefficient.Eq(zero))
                .Select(kv => new CanonicalMonomial<T>(kv.Coefficient, kv.Powers))
                .ToList();
            return new CanonicalPolynomial<T>(result, p.Constant);
        }
    }
}
