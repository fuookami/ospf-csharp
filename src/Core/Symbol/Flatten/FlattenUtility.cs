#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;

namespace Fuookami.Ospf.Core.Symbol.Flatten
{
    /// <summary>
    /// 线性扁平化数据 / Linear flatten data (monomials + constant).
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record LinearFlattenData<V>(
        IReadOnlyList<LinearMonomial<V>> Monomials,
        V Constant
    ) where V : struct, IRing<V>;

    /// <summary>
    /// 二次扁平化数据 / Quadratic flatten data (monomials + constant).
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record QuadraticFlattenData<V>(
        IReadOnlyList<QuadraticMonomial<V>> Monomials,
        V Constant
    ) where V : struct, IRing<V>;

    /// <summary>
    /// 表达式扁平化工具 / Expression flatten utilities.
    /// <para>提供线性与二次表达式的合并、乘法和归一化操作，用于模型构建时的表达式展开。</para>
    /// <para>Provides merge, multiply, and normalize operations for linear and quadratic
    /// expressions, used during model construction for expression expansion.</para>
    /// </summary>
    internal static class FlattenUtility
    {
        // ========== Merge Operations ==========

        /// <summary>
        /// 合并线性单项式，将相同变量的系数相加。
        /// Merge linear monomials by combining coefficients of same variables.
        /// </summary>
        public static LinearFlattenData<Flt64> MergeLinearMonomials(
            IReadOnlyList<LinearMonomial<Flt64>> monomials,
            Flt64 constant)
        {
            var merged = new Dictionary<IVariableItem, Flt64>();
            foreach (var m in monomials)
            {
                if (m.Symbol is not IVariableItem vi) continue;
                if (m.Coefficient != Flt64.Zero)
                {
                    merged[vi] = (merged.TryGetValue(vi, out var existing) ? existing : Flt64.Zero) + m.Coefficient;
                }
            }
            var normalized = new List<LinearMonomial<Flt64>>(merged.Count);
            foreach (var (variable, coefficient) in merged)
            {
                if (coefficient != Flt64.Zero)
                    normalized.Add(new LinearMonomial<Flt64>(coefficient, variable));
            }
            return new LinearFlattenData<Flt64>(normalized, constant);
        }

        /// <summary>
        /// 合并多个 LinearFlattenData，将所有单项式和常量合并。
        /// Merge multiple LinearFlattenData by combining all monomials and constants.
        /// </summary>
        public static LinearFlattenData<Flt64> MergeLinearFlattenDataFlt64(
            IReadOnlyList<LinearFlattenData<Flt64>> flattenDataList,
            Flt64 initialConstant)
        {
            var merged = new Dictionary<IVariableItem, Flt64>();
            var totalConstant = initialConstant;
            foreach (var fd in flattenDataList)
            {
                totalConstant += fd.Constant;
                foreach (var m in fd.Monomials)
                {
                    if (m.Symbol is not IVariableItem vi) continue;
                    if (m.Coefficient != Flt64.Zero)
                        merged[vi] = (merged.TryGetValue(vi, out var existing) ? existing : Flt64.Zero) + m.Coefficient;
                }
            }
            var normalized = new List<LinearMonomial<Flt64>>(merged.Count);
            foreach (var (variable, coefficient) in merged)
            {
                if (coefficient != Flt64.Zero)
                    normalized.Add(new LinearMonomial<Flt64>(coefficient, variable));
            }
            return new LinearFlattenData<Flt64>(normalized, totalConstant);
        }

        /// <summary>
        /// 合并二次单项式，将相同变量对的系数相加。
        /// Merge quadratic monomials by combining coefficients of same variable pairs.
        /// </summary>
        public static QuadraticFlattenData<Flt64> MergeQuadraticMonomials(
            IReadOnlyList<QuadraticMonomial<Flt64>> monomials,
            Flt64 constant)
        {
            var merged = new Dictionary<(IVariableItem, IVariableItem?), Flt64>();
            foreach (var m in monomials)
            {
                if (m.Symbol1 is not IVariableItem v1) continue;
                var v2 = m.Symbol2 as IVariableItem;
                var key = v2 is not null
                    ? (v1.Identifier < v2.Identifier ? (v1, (IVariableItem?)v2) : (v2, (IVariableItem?)v1))
                    : (v1, (IVariableItem?)null);
                if (m.Coefficient != Flt64.Zero)
                    merged[key] = (merged.TryGetValue(key, out var existing) ? existing : Flt64.Zero) + m.Coefficient;
            }
            var normalized = new List<QuadraticMonomial<Flt64>>(merged.Count);
            foreach (var (key, coefficient) in merged)
            {
                if (coefficient != Flt64.Zero)
                    normalized.Add(new QuadraticMonomial<Flt64>(coefficient, key.Item1, key.Item2));
            }
            return new QuadraticFlattenData<Flt64>(normalized, constant);
        }

        /// <summary>
        /// 合并多个 QuadraticFlattenData / Merge multiple QuadraticFlattenData.
        /// </summary>
        public static QuadraticFlattenData<Flt64> MergeQuadraticFlattenDataFlt64(
            IReadOnlyList<QuadraticFlattenData<Flt64>> flattenDataList,
            Flt64 initialConstant)
        {
            var merged = new Dictionary<(IVariableItem, IVariableItem?), Flt64>();
            var totalConstant = initialConstant;
            foreach (var fd in flattenDataList)
            {
                totalConstant += fd.Constant;
                foreach (var m in fd.Monomials)
                {
                    if (m.Symbol1 is not IVariableItem v1) continue;
                    var v2 = m.Symbol2 as IVariableItem;
                    var key = v2 is not null
                        ? (v1.Identifier < v2.Identifier ? (v1, (IVariableItem?)v2) : (v2, (IVariableItem?)v1))
                        : (v1, (IVariableItem?)null);
                    if (m.Coefficient != Flt64.Zero)
                        merged[key] = (merged.TryGetValue(key, out var existing) ? existing : Flt64.Zero) + m.Coefficient;
                }
            }
            var normalized = new List<QuadraticMonomial<Flt64>>(merged.Count);
            foreach (var (key, coefficient) in merged)
            {
                if (coefficient != Flt64.Zero)
                    normalized.Add(new QuadraticMonomial<Flt64>(coefficient, key.Item1, key.Item2));
            }
            return new QuadraticFlattenData<Flt64>(normalized, totalConstant);
        }

        // ========== Multiply Operations ==========

        /// <summary>
        /// 将两个线性扁平化数据相乘 (线性 * 线性 -> 二次)。
        /// Multiply two linear flatten data (linear * linear -> quadratic).
        /// </summary>
        public static QuadraticFlattenData<Flt64> MultiplyLinear(
            LinearFlattenData<Flt64> lhs,
            LinearFlattenData<Flt64> rhs)
        {
            var monomials = new List<QuadraticMonomial<Flt64>>();
            foreach (var m1 in lhs.Monomials)
            {
                if (m1.Symbol is not IVariableItem v1) continue;
                foreach (var m2 in rhs.Monomials)
                {
                    if (m2.Symbol is not IVariableItem v2) continue;
                    monomials.Add(new QuadraticMonomial<Flt64>(m1.Coefficient * m2.Coefficient, v1, v2));
                }
            }
            if (rhs.Constant != Flt64.Zero)
            {
                foreach (var m1 in lhs.Monomials)
                {
                    if (m1.Symbol is not IVariableItem v1) continue;
                    monomials.Add(new QuadraticMonomial<Flt64>(m1.Coefficient * rhs.Constant, v1, null));
                }
            }
            if (lhs.Constant != Flt64.Zero)
            {
                foreach (var m2 in rhs.Monomials)
                {
                    if (m2.Symbol is not IVariableItem v2) continue;
                    monomials.Add(new QuadraticMonomial<Flt64>(lhs.Constant * m2.Coefficient, v2, null));
                }
            }
            return MergeQuadraticMonomials(monomials, lhs.Constant * rhs.Constant);
        }

        // ========== Normalize Operations ==========

        /// <summary>
        /// 归一化线性扁平化数据，移除零系数项。
        /// Normalize linear flatten data by removing zero coefficients.
        /// </summary>
        public static LinearFlattenData<Flt64> NormalizeLinear(LinearFlattenData<Flt64> data)
        {
            var filtered = new List<LinearMonomial<Flt64>>();
            foreach (var m in data.Monomials)
            {
                if (m.Coefficient != Flt64.Zero)
                    filtered.Add(m);
            }
            return new LinearFlattenData<Flt64>(filtered, data.Constant);
        }

        /// <summary>
        /// 归一化二次扁平化数据，移除零系数项并规范化键。
        /// Normalize quadratic flatten data by removing zero coefficients and canonicalizing keys.
        /// </summary>
        public static QuadraticFlattenData<Flt64> NormalizeQuadratic(QuadraticFlattenData<Flt64> data)
        {
            var filtered = new List<QuadraticMonomial<Flt64>>();
            foreach (var m in data.Monomials)
            {
                if (m.Coefficient != Flt64.Zero)
                    filtered.Add(m);
            }
            return MergeQuadraticMonomials(filtered, data.Constant);
        }
    }
}
