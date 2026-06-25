#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Operation;
/// <summary>
/// Flt64 线性矩阵形式 / Flt64 linear matrix form (double[] vector).
/// 使用 Double 数组表示系数向量。
/// Uses Double array for coefficient vector.
/// </summary>
public sealed record Flt64LinearMatrixForm(
    double[] C,
    Flt64 D,
    IReadOnlyList<ISymbol> Order);

/// <summary>
/// Flt64 二次矩阵形式 / Flt64 quadratic matrix form (double[][] Q, double[] c).
/// 使用 Double 数组表示系数矩阵和向量。
/// Uses Double arrays for coefficient matrix and vectors.
/// </summary>
public sealed record Flt64QuadraticMatrixForm(
    double[][] Q,
    double[] C,
    Flt64 D,
    IReadOnlyList<ISymbol> Order);

/// <summary>
/// Flt64 矩阵形式快捷函数 / Flt64 matrix form convenience functions.
/// 提供 Flt64 多项式与 Double 数组矩阵形式之间的转换。
/// Provides conversion between Flt64 polynomials and Double array matrix forms.
/// </summary>
public static class Flt64MatrixFormOps {
    // ===== Linear -> Flt64LinearMatrixForm =====

    /// <summary>将 Flt64 线性多项式转换为 Double 数组矩阵形式 / Convert Flt64 linear polynomial to double array matrix form.</summary>
    public static Result<Flt64LinearMatrixForm, ErrorCode, Error<ErrorCode>> ToFlt64MatrixForm(
        this LinearPolynomial<Flt64> p, IReadOnlyList<ISymbol> order) {
        Result<LinearMatrixForm<Flt64>, ErrorCode, Error<ErrorCode>> result = p.ToMatrixForm(order, Flt64.Zero);
        if (result.IsFailed) {
            return new Failed<Flt64LinearMatrixForm, ErrorCode, Error<ErrorCode>>(
                ((Failed<LinearMatrixForm<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error);
        }

        LinearMatrixForm<Flt64> form = result.Value;
        return new Ok<Flt64LinearMatrixForm, ErrorCode, Error<ErrorCode>>(
            new Flt64LinearMatrixForm(
                form.C.Select(v => v.ToDouble()).ToArray(),
                form.D,
                form.Order));
    }

    // ===== Flt64LinearMatrixForm -> Linear =====

    /// <summary>从 Double 数组矩阵形式还原 Flt64 线性多项式 / Reconstruct Flt64 linear polynomial from double array matrix form.</summary>
    public static LinearPolynomial<Flt64> Flt64LinearPolynomialFromMatrixForm(
        double[] c, Flt64 d, IReadOnlyList<ISymbol> order) {
        return MatrixFormOps.LinearPolynomialFromMatrixForm(
            c.Select(v => new Flt64(v)).ToArray(), d, order, Flt64.Zero);
    }

    /// <summary>从 Flt64 线性矩阵形式还原多项式 / Reconstruct polynomial from Flt64 linear matrix form.</summary>
    public static LinearPolynomial<Flt64> Flt64LinearPolynomialFromMatrixForm(Flt64LinearMatrixForm form)
        => Flt64LinearPolynomialFromMatrixForm(form.C, form.D, form.Order);

    // ===== Quadratic -> Flt64QuadraticMatrixForm =====

    /// <summary>将 Flt64 二次多项式转换为 Double 数组矩阵形式 / Convert Flt64 quadratic polynomial to double array matrix form.</summary>
    public static Result<Flt64QuadraticMatrixForm, ErrorCode, Error<ErrorCode>> ToFlt64MatrixForm(
        this QuadraticPolynomial<Flt64> p, IReadOnlyList<ISymbol> order) {
        Result<QuadraticMatrixForm<Flt64>, ErrorCode, Error<ErrorCode>> result = p.ToMatrixForm(order, Flt64.Zero, coeff => {
            Flt64 half = coeff / new Flt64(2.0);
            return (half, half);
        });
        if (result.IsFailed) {
            return new Failed<Flt64QuadraticMatrixForm, ErrorCode, Error<ErrorCode>>(
                ((Failed<QuadraticMatrixForm<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error);
        }

        QuadraticMatrixForm<Flt64> form = result.Value;
        return new Ok<Flt64QuadraticMatrixForm, ErrorCode, Error<ErrorCode>>(
            new Flt64QuadraticMatrixForm(
                form.Q.Select(row => row.Select(v => v.ToDouble()).ToArray()).ToArray(),
                form.C.Select(v => v.ToDouble()).ToArray(),
                form.D,
                form.Order));
    }

    /// <summary>将 Flt64 规范多项式转换为 Double 数组矩阵形式 / Convert Flt64 canonical polynomial to double array matrix form.</summary>
    public static Result<Flt64QuadraticMatrixForm, ErrorCode, Error<ErrorCode>> ToFlt64MatrixForm(
        this CanonicalPolynomial<Flt64> p, IReadOnlyList<ISymbol> order) {
        Result<QuadraticMatrixForm<Flt64>, ErrorCode, Error<ErrorCode>> result = p.ToMatrixForm(order, Flt64.Zero, coeff => {
            Flt64 half = coeff / new Flt64(2.0);
            return (half, half);
        });
        if (result.IsFailed) {
            return new Failed<Flt64QuadraticMatrixForm, ErrorCode, Error<ErrorCode>>(
                ((Failed<QuadraticMatrixForm<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error);
        }

        QuadraticMatrixForm<Flt64> form = result.Value;
        return new Ok<Flt64QuadraticMatrixForm, ErrorCode, Error<ErrorCode>>(
            new Flt64QuadraticMatrixForm(
                form.Q.Select(row => row.Select(v => v.ToDouble()).ToArray()).ToArray(),
                form.C.Select(v => v.ToDouble()).ToArray(),
                form.D,
                form.Order));
    }

    // ===== Flt64QuadraticMatrixForm -> Quadratic =====

    /// <summary>从 Double 数组矩阵形式还原 Flt64 二次多项式 / Reconstruct Flt64 quadratic polynomial from double array matrix form.</summary>
    public static QuadraticPolynomial<Flt64> Flt64QuadraticPolynomialFromMatrixForm(
        double[][] q, double[] c, Flt64 d, IReadOnlyList<ISymbol> order) {
        return MatrixFormOps.QuadraticPolynomialFromMatrixForm(
            q.Select(row => row.Select(v => new Flt64(v)).ToArray()).ToArray(),
            c.Select(v => new Flt64(v)).ToArray(),
            d, order, Flt64.Zero);
    }

    /// <summary>从 Flt64 二次矩阵形式还原多项式 / Reconstruct polynomial from Flt64 quadratic matrix form.</summary>
    public static QuadraticPolynomial<Flt64> Flt64QuadraticPolynomialFromMatrixForm(Flt64QuadraticMatrixForm form)
        => Flt64QuadraticPolynomialFromMatrixForm(form.Q, form.C, form.D, form.Order);
}
