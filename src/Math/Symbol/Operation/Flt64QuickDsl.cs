#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Operation
{
    /// <summary>
    /// Flt64 快捷 DSL / Flt64 quick DSL.
    /// 顶层工厂与聚合函数 / Top-level factory and aggregation functions.
    /// </summary>
    public static class Flt64QuickDsl
    {
        // ===== LinearPolynomial factories =====

        /// <summary>创建空线性多项式 / Create empty linear polynomial.</summary>
        public static LinearPolynomial<Flt64> LinearPolynomial()
            => new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);

        /// <summary>从 Flt64 常数创建线性多项式 / Create linear polynomial from Flt64 constant.</summary>
        public static LinearPolynomial<Flt64> LinearPolynomial(Flt64 constant)
            => new(Array.Empty<LinearMonomial<Flt64>>(), constant);

        /// <summary>从单项式创建线性多项式 / Create linear polynomial from monomial.</summary>
        public static LinearPolynomial<Flt64> LinearPolynomial(LinearMonomial<Flt64> monomial)
            => new(new[] { monomial }, Flt64.Zero);

        /// <summary>从符号创建线性多项式 / Create linear polynomial from symbol.</summary>
        public static LinearPolynomial<Flt64> LinearPolynomial(ISymbol symbol)
            => new(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero);

        /// <summary>创建可变线性多项式 / Create mutable linear polynomial.</summary>
        public static MutableLinearPolynomial<Flt64> MutableLinearPolynomial()
            => new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);

        // ===== QuadraticPolynomial factories =====

        /// <summary>创建空二次多项式 / Create empty quadratic polynomial.</summary>
        public static QuadraticPolynomial<Flt64> QuadraticPolynomial()
            => new(Array.Empty<QuadraticMonomial<Flt64>>(), Flt64.Zero);

        /// <summary>从 Flt64 常数创建二次多项式 / Create quadratic polynomial from Flt64 constant.</summary>
        public static QuadraticPolynomial<Flt64> QuadraticPolynomial(Flt64 constant)
            => new(Array.Empty<QuadraticMonomial<Flt64>>(), constant);

        /// <summary>从符号创建二次多项式 / Create quadratic polynomial from symbol.</summary>
        public static QuadraticPolynomial<Flt64> QuadraticPolynomial(ISymbol symbol)
            => new(new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, symbol) }, Flt64.Zero);

        /// <summary>创建可变二次多项式 / Create mutable quadratic polynomial.</summary>
        public static MutableQuadraticPolynomial<Flt64> MutableQuadraticPolynomial()
            => new(Array.Empty<QuadraticMonomial<Flt64>>(), null, Flt64.Zero);

        // ===== Aggregation functions =====

        /// <summary>对符号求和（线性）/ Sum symbols (linear).</summary>
        public static LinearPolynomial<Flt64> SumVars<E>(IEnumerable<E> items, Func<E, ISymbol?> selector)
        {
            var monomials = new List<LinearMonomial<Flt64>>();
            foreach (var item in items)
            {
                var sym = selector(item);
                if (sym is not null)
                    monomials.Add(new LinearMonomial<Flt64>(Flt64.One, sym));
            }
            return new LinearPolynomial<Flt64>(monomials, Flt64.Zero);
        }

        /// <summary>对符号求和（线性）/ Sum symbols (linear).</summary>
        public static LinearPolynomial<Flt64> Sum(IEnumerable<ISymbol> symbols)
        {
            var monomials = new List<LinearMonomial<Flt64>>();
            foreach (var sym in symbols)
                monomials.Add(new LinearMonomial<Flt64>(Flt64.One, sym));
            return new LinearPolynomial<Flt64>(monomials, Flt64.Zero);
        }

        /// <summary>对符号求和（二次）/ Sum symbols (quadratic).</summary>
        public static QuadraticPolynomial<Flt64> QsumVars<E>(IEnumerable<E> items, Func<E, ISymbol?> selector)
        {
            var monomials = new List<QuadraticMonomial<Flt64>>();
            foreach (var item in items)
            {
                var sym = selector(item);
                if (sym is not null)
                    monomials.Add(QuadraticMonomial<Flt64>.Linear(Flt64.One, sym));
            }
            return new QuadraticPolynomial<Flt64>(monomials, Flt64.Zero);
        }

        /// <summary>对符号求和（二次）/ Sum symbols (quadratic).</summary>
        public static QuadraticPolynomial<Flt64> Qsum(IEnumerable<ISymbol> symbols)
        {
            var monomials = new List<QuadraticMonomial<Flt64>>();
            foreach (var sym in symbols)
                monomials.Add(QuadraticMonomial<Flt64>.Linear(Flt64.One, sym));
            return new QuadraticPolynomial<Flt64>(monomials, Flt64.Zero);
        }
    }
}
