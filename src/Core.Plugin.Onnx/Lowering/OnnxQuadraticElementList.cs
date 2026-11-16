#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **逐元素列表的二次版本** / The second-degree version of the per-element list.
/// </summary>
/// <remarks>
/// **按"整次导入的模式"决定元素装在哪个次数域里，** **而不是逐节点判断。** '
/// **所以这里也是一个独立的表示，** **而不是让同一个列表有时装一次、有时装二次。**
///
/// **它只在一处产生：** **两侧都含变量的矩阵乘。** **其余情形（一侧常量、逐元素运算、形状算子）仍然走一次域，** '
/// **因为那才是它们的自然次数——** **把常量乘常量也升到二次只会让模型白白多出一层。**
///
/// **The code decides at import time which degree domain the elements live in, not per node.** So this is a separate representation rather than one list that sometimes
/// holds linear and sometimes quadratic terms.
///
/// **It is produced in exactly one place: a matrix product with variables on both sides.** Everything else (one constant side, elementwise operators, shape operators) stays in the first-degree
/// domain, **because that is their natural degree — raising even a constant-times-constant to second degree would add a layer to the model for nothing.**
/// </remarks>
public sealed class OnnxQuadraticElementList
{
    private readonly IReadOnlyList<int> _shape;
    private readonly IReadOnlyList<QuadraticPolynomial<Flt64>> _elements;

    private OnnxQuadraticElementList(IReadOnlyList<int> shape, IReadOnlyList<QuadraticPolynomial<Flt64>> elements) {
        _shape = shape;
        _elements = elements;
    }

    /// <summary>形状 / The shape.</summary>
    public IReadOnlyList<int> Shape => _shape;

    /// <summary>逐位置的二次式子 / The second-degree form at each position.</summary>
    public IReadOnlyList<QuadraticPolynomial<Flt64>> Elements => _elements;

    /// <summary>元素个数 / The element count.</summary>
    public int Count => _elements.Count;

    /// <summary>
    /// 构造结果 / The construction result.
    /// </summary>
    /// <param name="Value">构造出的表示 / The representation built.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Result(OnnxQuadraticElementList? Value, string? Failure)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 由形状与逐位置式子构造 / Build from a shape and per-position forms.
    /// </summary>
    /// <param name="shape">形状 / The shape.</param>
    /// <param name="elements">逐位置式子 / The per-position forms.</param>
    /// <returns>构造结果 / The result.</returns>
    /// <remarks>
    /// **元素个数必须等于形状的乘积，** **与一次域同一个检查点：** **不等时任何按坐标的访问都会静静读到别的元素。**
    ///
    /// **The element count must equal the product of the shape, the same checkpoint as in the first-degree domain: otherwise any coordinate access quietly reads another element.**
    /// </remarks>
    public static Result Create(IReadOnlyList<int> shape, IReadOnlyList<QuadraticPolynomial<Flt64>> elements) {
        if (shape is null || elements is null) {
            return new Result(null, "形状或元素为空 / the shape or the elements are null");
        }
        foreach (int dimension in shape) {
            if (dimension < 0) {
                return new Result(null, "形状含负的维度 " + dimension + " / the shape holds a negative dimension");
            }
        }
        long expected = 1;
        foreach (int dimension in shape) {
            expected *= dimension;
        }
        if (expected != elements.Count) {
            return new Result(null,
                "形状要 " + expected + " 个元素，而给了 " + elements.Count + " 个 / the shape needs " + expected + " elements but " + elements.Count + " were given");
        }
        return new Result(new OnnxQuadraticElementList(shape, elements), null);
    }

    /// <summary>
    /// 二次域的 `Gemm` / Gemm in the second-degree domain.
    /// </summary>
    /// <param name="left">左操作数 / The left operand.</param>
    /// <param name="right">右操作数 / The right operand.</param>
    /// <param name="bias">偏置 `C`（可为一次或二次），可空 / the bias C, linear or quadratic, nullable.</param>
    /// <param name="transA">是否转置左操作数 / whether to transpose the left operand.</param>
    /// <param name="transB">是否转置右操作数 / whether to transpose the right operand.</param>
    /// <param name="alpha">乘积的缩放 / the scale on the product.</param>
    /// <param name="beta">偏置的缩放 / the scale on the bias.</param>
    /// <returns>结果 / The result.</returns>
    /// <remarks>
    /// **顺序与一次域一致：** **转置 → 二次域相乘 → 乘 `alpha` → 加 `beta·C`。** '
    /// **`C` 允许是一次或二次：** **符号偏置在一次域里线性、在二次域里"第二个符号为空"，** **提升到二次域相加仍不超过二次；** '
    /// **而"二次乘积再加二次偏置"会到三次——** **那种情形在这里拒绝，而不是悄悄截断。**
    ///
    /// **The order matches the first-degree version: transpose, multiply in the second-degree domain, scale by alpha, add beta·C.** C may be linear or quadratic: **a symbolic bias is linear in
    /// the first-degree domain and "second symbol empty" in the second, so lifting it stays within degree two; a quadratic product plus a quadratic bias would reach degree three —** that case is
    /// refused here instead of being quietly truncated.
    /// </remarks>
    public static Result Gemm(
        OnnxElementList left,
        OnnxElementList right,
        OnnxQuadraticElementList? bias,
        bool transA,
        bool transB,
        double alpha,
        double beta) {
        if (left is null || right is null) {
            return new Result(null, "Gemm 的一方为空 / one of the operands is null");
        }
        if (left.Shape.Count != 2 || right.Shape.Count != 2) {
            return new Result(null, "Gemm 只支持二维输入 / Gemm supports 2-D inputs only");
        }
        OnnxElementList leftOperand = transA ? OnnxElementList.Transpose2D(left) : left;
        OnnxElementList rightOperand = transB ? OnnxElementList.Transpose2D(right) : right;
        Result product = MatMul(leftOperand, rightOperand);
        if (product.Value is not OnnxQuadraticElementList scaled) {
            return new Result(null, product.Failure);
        }
        if (alpha != 1.0) {
            scaled = scaled.Scale(alpha);
        }
        if (bias is null) {
            return new Result(scaled, null);
        }
        // `C` 若含二次项，`乘积 + beta·C` 仍不超过二次；**但那是"二次 + 二次"，是否越界要看乘积本身——**
        // **所以这里只拒绝"乘积已是二次且偏置也含二次项"的情形，并点名三次。**
        //
        // If C holds quadratic terms, product + beta·C stays within degree two only when the product is linear; **so the only case refused here is "the product is already quadratic and the bias
        // holds quadratic terms too", named as degree three.**
        if (HasQuadraticTerms(scaled) && HasQuadraticTerms(bias)) {
            return new Result(
                null,
                "Gemm 的乘积与偏置都含二次项，相加会到三次 / both the product and the bias carry quadratic terms, which sums to degree three");
        }
        OnnxQuadraticElementList scaledBias = beta == 1.0 ? bias : bias.Scale(beta);
        return scaled.Add(scaledBias);
    }

    private static bool HasQuadraticTerms(OnnxQuadraticElementList list) {
        foreach (QuadraticPolynomial<Flt64> element in list.Elements) {
            foreach (QuadraticMonomial<Flt64> monomial in element.Monomials) {
                if (monomial.Symbol2 is not null) {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 把一次域的元素提升到二次域 / Lift a first-degree element list into the second-degree domain.
    /// </summary>
    /// <remarks>
    /// **提升是安全的、下降不是：** **一次项在二次域里就是「第二个符号为空」的单项式，所以提升只是换一个容器；**
    /// **而二次项掉回一次域会丢掉交叉项，那一步永远不做。**
    ///
    /// **Lifting is safe and lowering is not: a linear term is a second-degree monomial whose second symbol is empty, so lifting only changes the container; a quadratic term falling
    /// back into the first-degree domain would lose its cross terms, and that step is never taken.**
    /// </remarks>
    /// <param name="linear">一次域的元素列表 / The first-degree element list.</param>
    /// <returns>二次域的表示 / The second-degree representation.</returns>
    public static OnnxQuadraticElementList FromLinear(OnnxElementList linear) {
        var elements = new List<QuadraticPolynomial<Flt64>>(linear.Count);
        foreach (LinearPolynomial<Flt64> element in linear.Elements) {
            var monomials = new List<QuadraticMonomial<Flt64>>(element.Monomials.Count);
            foreach (LinearMonomial<Flt64> monomial in element.Monomials) {
                monomials.Add(new QuadraticMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
            }
            elements.Add(new QuadraticPolynomial<Flt64>(monomials, element.Constant));
        }
        return Create(linear.Shape, elements).Value!;
    }

    /// <summary>按常数缩放 / Scale by a constant.</summary>
    public OnnxQuadraticElementList Scale(double factor) {
        var elements = new List<QuadraticPolynomial<Flt64>>(_elements.Count);
        foreach (QuadraticPolynomial<Flt64> element in _elements) {
            elements.Add(element * new Flt64(factor));
        }
        return new OnnxQuadraticElementList(_shape, elements);
    }

    /// <summary>
    /// 逐元素相加，`[N]` 偏置按行广播 / Elementwise addition, with an [N] bias broadcast over rows.
    /// </summary>
    /// <remarks>
    /// **与一次域同一套广播规则：** **`[M,N]` 逐位置、`[N]` 按行；** **其余形状拒绝而不是"差不多就加"——** '
    /// **形状差不多的偏置会让某一列静默加上别的列的值。**
    ///
    /// **The same broadcast rules as the first-degree domain: [M,N] positionally and [N] by row; anything else is refused rather than "close enough" —** a roughly-shaped bias silently adds one
    /// column's values to another.
    /// </remarks>
    public Result Add(OnnxQuadraticElementList right) {
        if (right is null) {
            return new Result(null, "相加的一方为空 / the addend is null");
        }
        if (_elements.Count == right._elements.Count && _shape.Count == right._shape.Count) {
            var same = new List<QuadraticPolynomial<Flt64>>(_elements.Count);
            for (int index = 0; index < _elements.Count; ++index) {
                same.Add(_elements[index] + right._elements[index]);
            }
            return Create(new List<int>(_shape), same);
        }
        if (_shape.Count == 2 && right._shape.Count == 1 && right._shape[0] == _shape[1]) {
            int rows = _shape[0];
            int columns = _shape[1];
            var broadcast = new List<QuadraticPolynomial<Flt64>>(_elements.Count);
            for (int row = 0; row < rows; ++row) {
                for (int column = 0; column < columns; ++column) {
                    broadcast.Add(_elements[row * columns + column] + right._elements[column]);
                }
            }
            return Create(new[] { rows, columns }, broadcast);
        }
        return new Result(
            null,
            "偏置形状 [" + string.Join(",", right._shape) + "] 与结果 [" + string.Join(",", _shape) + "] 不相容 / the bias shape is incompatible with the result");
    }

    public static Result MatMul(OnnxElementList left, OnnxElementList right) {
        if (left is null || right is null) {
            return new Result(null, "矩阵乘的一方为空 / one of the operands is null");
        }
        IReadOnlyList<int> leftShape = left.Shape;
        IReadOnlyList<int> rightShape = right.Shape;
        if (leftShape.Count == 1) {
            leftShape = new[] { 1, leftShape[0] };
        }
        if (rightShape.Count == 1) {
            rightShape = new[] { rightShape[0], 1 };
        }
        if (leftShape.Count < 2 || rightShape.Count < 2) {
            return new Result(null, "MatMul 操作数秩不得为 0 / MatMul operands must not be rank 0");
        }
        int rows = leftShape[^2];
        int inner = leftShape[^1];
        int rightInner = rightShape[^2];
        int columns = rightShape[^1];
        if (inner != rightInner) {
            return new Result(null, "MatMul 内维不一致：" + inner + " 与 " + rightInner + " / MatMul inner dimensions differ");
        }
        var leftBatch = new List<int>();
        for (int dimension = 0; dimension < leftShape.Count - 2; ++dimension) {
            leftBatch.Add(leftShape[dimension]);
        }
        var rightBatch = new List<int>();
        for (int dimension = 0; dimension < rightShape.Count - 2; ++dimension) {
            rightBatch.Add(rightShape[dimension]);
        }
        IReadOnlyList<int>? batchShape = OnnxShape.BroadcastShape(leftBatch, rightBatch, out string? batchFailure);
        if (batchShape is null) {
            return new Result(null, "MatMul 批量维不相容：" + batchFailure + " / incompatible batch dimensions");
        }
        long batchCount = 1;
        foreach (int dimension in batchShape) {
            batchCount *= System.Math.Max(dimension, 1);
        }
        var leftTarget = new List<int>(batchShape) { rows, inner };
        var rightTarget = new List<int>(batchShape) { rightInner, columns };
        int[]? leftIndices = OnnxElementList.ExpandIndices(leftShape, leftTarget);
        int[]? rightIndices = OnnxElementList.ExpandIndices(rightShape, rightTarget);
        if (leftIndices is null || rightIndices is null) {
            return new Result(
                null,
                "二次域的 MatMul 无法把批量维展开到 [" + string.Join(",", leftTarget) + "] / [" + string.Join(",", rightTarget) + "] / the batch dimensions cannot be expanded");
        }
        long leftBatchSize = rows * inner;
        long rightBatchSize = rightInner * columns;
        var elements = new List<QuadraticPolynomial<Flt64>>((int)(batchCount * rows * columns));
        for (long batch = 0; batch < batchCount; ++batch) {
            long leftOffset = batch * leftBatchSize;
            long rightOffset = batch * rightBatchSize;
            for (int row = 0; row < rows; ++row) {
                for (int column = 0; column < columns; ++column) {
                    QuadraticPolynomial<Flt64>? sum = null;
                    for (int position = 0; position < inner; ++position) {
                        OnnxQuadraticProduct.Result term = OnnxQuadraticProduct.Multiply(
                            left.Elements[leftIndices[leftOffset + row * inner + position]],
                            right.Elements[rightIndices[rightOffset + position * columns + column]]);
                        if (term.Polynomial is not QuadraticPolynomial<Flt64> product) {
                            return new Result(null, term.Failure);
                        }
                        sum = sum is null ? product : sum + product;
                    }
                    elements.Add(sum!);
                }
            }
        }
        var target = new List<int>(batchShape);
        if (left.Shape.Count != 1) {
            target.Add(rows);
        }
        if (right.Shape.Count != 1) {
            target.Add(columns);
        }
        if (target.Count == 0) {
            target.Add(1);
        }
        return Create(target, elements);
    }
}