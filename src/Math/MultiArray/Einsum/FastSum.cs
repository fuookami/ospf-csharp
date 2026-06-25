#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.MultiArray.Einsum
{
    /// <summary>
    /// 轴越界异常 / Axis out of bounds exception.
    /// </summary>
    public sealed class AxisOutOfBoundsException : Exception
    {
        public AxisOutOfBoundsException(int axis, int dimension)
            : base($"Axis {axis} is out of bounds for array with {dimension} dimensions.") { }
    }

    /// <summary>
    /// Einstein 求和操作 / Einstein summation operations.
    /// 提供 matmul, dot, trace, outer, transpose, contract 等核心操作。
    /// Provides matmul, dot, trace, outer, transpose, contract core operations.
    /// </summary>
    internal static class Operations
    {
        // ===== Matrix multiplication =====

        /// <summary>矩阵乘法 / Matrix multiplication (ij,jk->ik).</summary>
        public static Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> Matmul<T, SA, SB>(
            AbstractMultiArray<T, SA> a, AbstractMultiArray<T, SB> b, T zero)
            where T : struct, IRing<T>
            where SA : IShape
            where SB : IShape
        {
            if (a.Shape.Dimension != 2 || b.Shape.Dimension != 2)
                return Failed<MultiArray<T, DynShape>>("matmul requires 2D arrays.");

            var m = a.Shape[0];
            var k1 = a.Shape[1];
            var k2 = b.Shape[0];
            var n = b.Shape[1];

            if (k1 != k2)
                return Failed<MultiArray<T, DynShape>>($"Inner dimensions mismatch: {k1} vs {k2}.");

            var shape = DynShape.Invoke(new[] { m, n });
            var result = MutableMultiArray<T, DynShape>.Factory.NewBy(shape, (_, _) => zero);

            for (var i = 0; i < m; i++)
            for (var j = 0; j < n; j++)
            {
                var sum = zero;
                for (var kk = 0; kk < k1; kk++)
                {
                    var av = a[new[] { i, kk }];
                    var bv = b[new[] { kk, j }];
                    sum = sum.Plus(av.Times(bv));
                }
                result[new[] { i, j }] = sum;
            }

            return new Ok<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>>(result.ToImmutable());
        }

        // ===== Dot product =====

        /// <summary>点积 / Dot product (i,i->).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Dot<T, SA, SB>(
            AbstractMultiArray<T, SA> a, AbstractMultiArray<T, SB> b, T zero)
            where T : struct, IRing<T>
            where SA : IShape
            where SB : IShape
        {
            if (a.Shape.Dimension != 1 || b.Shape.Dimension != 1)
                return Failed<T>("dot requires 1D arrays.");
            if (a.Shape[0] != b.Shape[0])
                return Failed<T>("dot requires same-length arrays.");

            var sum = zero;
            for (var i = 0; i < a.Shape[0]; i++)
                sum = sum.Plus(a[i].Times(b[i]));

            return new Ok<T, ErrorCode, Error<ErrorCode>>(sum);
        }

        // ===== Trace =====

        /// <summary>迹 / Trace (ii->).</summary>
        public static Result<T, ErrorCode, Error<ErrorCode>> Trace<T, S>(
            AbstractMultiArray<T, S> a, T zero)
            where T : struct, IRing<T>
            where S : IShape
        {
            if (a.Shape.Dimension != 2)
                return Failed<T>("trace requires 2D array.");

            var n = System.Math.Min(a.Shape[0], a.Shape[1]);
            var sum = zero;
            for (var i = 0; i < n; i++)
                sum = sum.Plus(a[new[] { i, i }]);

            return new Ok<T, ErrorCode, Error<ErrorCode>>(sum);
        }

        // ===== Outer product =====

        /// <summary>外积 / Outer product (i,j->ij).</summary>
        public static Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> Outer<T, SA, SB>(
            AbstractMultiArray<T, SA> a, AbstractMultiArray<T, SB> b, T zero)
            where T : struct, IRing<T>
            where SA : IShape
            where SB : IShape
        {
            if (a.Shape.Dimension != 1 || b.Shape.Dimension != 1)
                return Failed<MultiArray<T, DynShape>>("outer requires 1D arrays.");

            var m = a.Shape[0];
            var n = b.Shape[0];
            var shape = DynShape.Invoke(new[] { m, n });
            var result = MutableMultiArray<T, DynShape>.Factory.NewBy(shape, (_, _) => zero);

            for (var i = 0; i < m; i++)
            for (var j = 0; j < n; j++)
                result[new[] { i, j }] = a[i].Times(b[j]);

            return new Ok<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>>(result.ToImmutable());
        }

        // ===== Transpose =====

        /// <summary>转置 / Transpose (ij->ji).</summary>
        public static Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> Transpose<T, S>(
            AbstractMultiArray<T, S> a)
            where T : struct, IRing<T>
            where S : IShape
        {
            if (a.Shape.Dimension != 2)
                return Failed<MultiArray<T, DynShape>>("transpose requires 2D array.");

            var m = a.Shape[0];
            var n = a.Shape[1];
            var shape = DynShape.Invoke(new[] { n, m });
            var result = MutableMultiArray<T, DynShape>.Factory.NewBy(shape, (_, _) => default!);

            for (var i = 0; i < m; i++)
            for (var j = 0; j < n; j++)
                result[new[] { j, i }] = a[new[] { i, j }];

            return new Ok<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>>(result.ToImmutable());
        }

        // ===== Contract =====

        /// <summary>缩并 / Contract (reduce along specified axes).</summary>
        public static Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> Contract<T, SA, SB>(
            AbstractMultiArray<T, SA> a, int axisA,
            AbstractMultiArray<T, SB> b, int axisB, T zero)
            where T : struct, IRing<T>
            where SA : IShape
            where SB : IShape
        {
            if (axisA < 0 || axisA >= a.Shape.Dimension)
                return Failed<MultiArray<T, DynShape>>($"axisA {axisA} out of bounds.");
            if (axisB < 0 || axisB >= b.Shape.Dimension)
                return Failed<MultiArray<T, DynShape>>($"axisB {axisB} out of bounds.");
            if (a.Shape[axisA] != b.Shape[axisB])
                return Failed<MultiArray<T, DynShape>>("Contracted dimensions must match.");

            var resultDims = new List<int>();
            for (var i = 0; i < a.Shape.Dimension; i++)
                if (i != axisA) resultDims.Add(a.Shape[i]);
            for (var i = 0; i < b.Shape.Dimension; i++)
                if (i != axisB) resultDims.Add(b.Shape[i]);

            var shape = DynShape.Invoke(resultDims.ToArray());
            var result = MutableMultiArray<T, DynShape>.Factory.NewBy(shape, (_, _) => zero);

            var k = a.Shape[axisA];
            var aOtherDims = Enumerable.Range(0, a.Shape.Dimension).Where(i => i != axisA).ToArray();
            var bOtherDims = Enumerable.Range(0, b.Shape.Dimension).Where(i => i != axisB).ToArray();

            foreach ((int linIdx, int[] vecIdx, T val) in result.Enumerate())
            {
                var sum = zero;
                for (var kk = 0; kk < k; kk++)
                {
                    var aIdx = new int[a.Shape.Dimension];
                    aIdx[axisA] = kk;
                    var aoi = 0;
                    foreach (var d in aOtherDims)
                    {
                        aIdx[d] = vecIdx[aoi];
                        aoi++;
                    }

                    var bIdx = new int[b.Shape.Dimension];
                    bIdx[axisB] = kk;
                    var boi = aOtherDims.Length;
                    foreach (var d in bOtherDims)
                    {
                        bIdx[d] = vecIdx[boi];
                        boi++;
                    }

                    sum = sum.Plus(a[aIdx].Times(b[bIdx]));
                }
                result[vecIdx] = sum;
            }

            return new Ok<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>>(result.ToImmutable());
        }

        // ===== Error helpers =====

        internal static Result<U, ErrorCode, Error<ErrorCode>> Failed<U>(string message)
            => new Failed<U, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);
    }
}
