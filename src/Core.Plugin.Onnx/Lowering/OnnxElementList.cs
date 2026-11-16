#nullable enable

// IDE0007 与 IDE0008 会在同一个泛型局部上互相要求（一个说用 var、一个说用显式类型），而这里两处都触发。
// 按仓库既有做法在文件级关闭这两条，理由记录在此。
//
// IDE0007 and IDE0008 demand opposite things on the same generic local, and both fire here. Disabled at file scope per the existing repository practice, with the reason recorded.
#pragma warning disable IDE0007, IDE0008

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **逐元素表示：形状 + 每个位置一个表达式** / The per-element representation: a shape plus one expression per position.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxTensorExpr`。**这是清单第 2、3 项共同缺的那一层：** '
/// **既有实现每个名字只有一个标量表达式，于是 `Transpose` 的重排无处可写、`Scan` 的运行期切片无处可切。**
///
/// **这一层只做本体，不接线：** **`Transpose` 按坐标重排、`SliceLeading` 沿首维切片，两者都是纯函数，**
/// **可以直接用"某位置的元素是不是原来那一个"钉住。** **接线要改求值器的每个调用点——**
/// **先把"重排算得对"这件事独立验证，接线时才有可对照的东西。**
///
/// Syncs `OnnxTensorExpr`. **This is the layer the checklist's items 2 and 3 both lack:** **the existing implementation holds one scalar expression per name, so a Transpose has
/// nowhere to write the permutation and a Scan runtime slice has nowhere to be cut.**
///
/// ** builds the layer itself without wiring it: Transpose permutes by coordinate and SliceLeading cuts along the leading axis, both pure functions, and both can be pinned
/// directly by "is the element at this position the same one as before".** **Wiring touches every call site of the evaluator, which is the next step — verifying that the
/// permutation is computed correctly first gives the wiring something to compare against.**
/// </remarks>
public sealed class OnnxElementList
{
    private readonly IReadOnlyList<int> _shape;
    private readonly IReadOnlyList<LinearPolynomial<Flt64>> _elements;

    private OnnxElementList(IReadOnlyList<int> shape, IReadOnlyList<LinearPolynomial<Flt64>> elements) {
        _shape = shape;
        _elements = elements;
    }

    /// <summary>形状 / The shape.</summary>
    public IReadOnlyList<int> Shape => _shape;

    /// <summary>逐位置的表达式 / The expression at each position.</summary>
    public IReadOnlyList<LinearPolynomial<Flt64>> Elements => _elements;

    /// <summary>元素个数 / The element count.</summary>
    public int Count => _elements.Count;

    /// <summary>
    /// 构造结果 / The construction result.
    /// </summary>
    /// <param name="Value">构造出的表示 / The representation built.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Result(OnnxElementList? Value, string? Failure)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 由形状与逐位置表达式构造 / Build from a shape and per-position expressions.
    /// </summary>
    /// <param name="shape">形状 / The shape.</param>
    /// <param name="elements">逐位置表达式 / The per-position expressions.</param>
    /// <returns>构造结果 / The result.</returns>
    /// <remarks>
    /// **元素个数必须等于形状的乘积：** **不等时任何按坐标的访问都会越界或读到别的元素，** '
    /// **而那种错在数值上表现为"某个位置的值不知从哪来"——** **所以构造处就是唯一的检查点。**
    ///
    /// **The element count must equal the product of the shape: otherwise every coordinate access either goes out of range or reads another element, and that shows up numerically as
    /// "the value at this position came from nowhere" — so the constructor is the one place to check it.**
    /// </remarks>
    public static Result Create(IReadOnlyList<int> shape, IReadOnlyList<LinearPolynomial<Flt64>> elements) {
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
                "形状要 " + expected + " 个元素，而给了 " + elements.Count + " 个 / the shape needs " + expected
                + " elements but " + elements.Count + " were given");
        }
        return new Result(new OnnxElementList(shape, elements), null);
    }

    /// <summary>
    /// 按置换重排元素 / Permute the elements. 对齐基线 `transpose`。
    /// </summary>
    /// <param name="permutation">目标轴顺序 / The target axis order.</param>
    /// <returns>重排后的表示 / The permuted representation.</returns>
    /// <remarks>
    /// **坐标映射按行主序，与基线的 `flatten(sourceCoordinates, sourceStrides)` 同一个算法：** '
    /// **目标位置 `(t₀..tₙ)` 读的是源张量的 `(t_{p₀}..)` 处的元素——** **也就是"谁是第几个维度"换了，而元素本身没动。**
    ///
    /// **The coordinate mapping is row-major and follows the same algorithm as the baseline's flatten(sourceCoordinates, sourceStrides): the target position (t₀..tₙ) reads the
    /// source element at (t_{p₀}..) — what changed is which dimension is which, not the elements themselves.**
    /// </remarks>
    public Result Transpose(IReadOnlyList<int> permutation) {
        if (permutation is null || permutation.Count != _shape.Count) {
            return new Result(null, "置换的轴数 " + (permutation?.Count ?? 0) + " 与秩 " + _shape.Count + " 不符 / the permutation length disagrees with the rank");
        }
        int[] targetShape = new int[permutation.Count];
        var seen = new HashSet<int>();
        for (int index = 0; index < permutation.Count; ++index) {
            int axis = permutation[index];
            if (axis < 0 || axis >= _shape.Count) {
                return new Result(null, "置换里的轴 " + axis + " 越界 / the permutation holds an out-of-range axis");
            }
            if (!seen.Add(axis)) {
                return new Result(null, "置换里的轴 " + axis + " 重复 / the permutation repeats an axis");
            }
            targetShape[index] = _shape[axis];
        }

        int[] sourceStrides = Strides(_shape);
        int[] targetStrides = Strides(targetShape);
        var reordered = new LinearPolynomial<Flt64>[_elements.Count];
        int[] targetCoordinate = new int[targetShape.Length];
        for (int targetIndex = 0; targetIndex < _elements.Count; ++targetIndex) {
            int remainder = targetIndex;
            for (int axis = 0; axis < targetShape.Length; ++axis) {
                targetCoordinate[axis] = remainder / targetStrides[axis];
                remainder %= targetStrides[axis];
            }
            int sourceIndex = 0;
            for (int axis = 0; axis < permutation.Count; ++axis) {
                sourceIndex += targetCoordinate[axis] * sourceStrides[permutation[axis]];
            }
            reordered[targetIndex] = _elements[sourceIndex];
        }
        return new Result(new OnnxElementList(targetShape, reordered), null);
    }

    /// <summary>
    /// 沿首维取第 `index` 片 / Take the `index`-th slice along the leading axis. 对齐基线 `sliceLeading`。
    /// </summary>
    /// <param name="index">第几片 / Which slice.</param>
    /// <returns>切片后的表示 / The sliced representation.</returns>
    /// <remarks>
    /// **这就是 `Scan` 运行期切片要的那一刀：** **首维长度是轮数，第 `i` 片就是第 `i` 轮喂给循环体的东西。** '
    /// **它只要求"有元素列表"，不要求元素是常量——** **所以切片之后每轮的输入可以是含变量的表达式。**
    ///
    /// **This is the cut Scan runtime slicing needs: the leading dimension is the round count, and slice i is what round i feeds the body.** It only requires an element list, not
    /// constant elements — so a sliced round input may carry variables.
    /// </remarks>
    public Result SliceLeading(int index) {
        if (_shape.Count == 0) {
            return new Result(null, "标量没有可切的首维 / a scalar has no leading axis to slice");
        }
        int length = _shape[0];
        if (index < 0 || index >= length) {
            return new Result(null, "第 " + index + " 片越界（首维长度 " + length + "）/ the slice index is out of range");
        }
        int sliceSize = _elements.Count / System.Math.Max(length, 1);
        var slice = new List<LinearPolynomial<Flt64>>(sliceSize);
        int start = index * sliceSize;
        for (int offset = 0; offset < sliceSize; ++offset) {
            slice.Add(_elements[start + offset]);
        }
        int[] sliceShape = new int[_shape.Count - 1];
        for (int axis = 1; axis < _shape.Count; ++axis) {
            sliceShape[axis - 1] = _shape[axis];
        }
        return new Result(new OnnxElementList(sliceShape, slice), null);
    }

    /// <summary>
    /// 按轴拼接多个逐元素张量 / Concatenate several element lists along an axis. 对齐基线 `concat`。
    /// </summary>
    /// <param name="parts">待拼接的张量，至少一个 / the tensors to concatenate, at least one.</param>
    /// <param name="axis">拼接轴，负数按秩归一 / the axis, with negatives normalised by the rank.</param>
    /// <returns>拼接结果 / The concatenation.</returns>
    /// <remarks>
    /// **算法与基线的 `concat` 同一个循环结构：** **外层按"拼接轴之前的坐标"分组，** **每组里依次把每个输入的对应一段接上来。**
    /// **这一段的大小是 `该输入在拼接轴上的长度 × 轴后元素数`，** **起点是 `外层下标 × 段长`。**
    ///
    /// **除拼接轴之外的每一维都必须相同：** **不同就意味着"每组的段长不一样"，** **而按下标去取会取到别的输入的尾巴——** '
    /// **错位之后数值上看不出异常，只是那一段整体挪了位。**
    ///
    /// **The loop structure matches the baseline's concat: an outer loop over the coordinates before the axis, and within each group each input's run is appended in turn.** **A run is
    /// length-on-axis times elements-after-axis long, starting at outer index times run length.**
    ///
    /// **Every dimension other than the axis has to agree: otherwise the runs differ in size and indexing by offset reads another input's tail — the shift is invisible numerically,
    /// the run has simply moved.**
    /// </remarks>
    public static Result Concat(IReadOnlyList<OnnxElementList> parts, int axis) {
        if (parts is null || parts.Count == 0) {
            return new Result(null, "Concat 至少需要一个输入 / Concat needs at least one input");
        }
        int rank = parts[0]._shape.Count;
        if (rank == 0) {
            return new Result(null, "标量没有可拼接的轴 / a scalar has no axis to concatenate along");
        }
        int normalized = axis < 0 ? axis + rank : axis;
        if (normalized < 0 || normalized >= rank) {
            return new Result(null, "拼接轴 " + axis + " 越界（秩 " + rank + "）/ the axis is out of range for the rank");
        }
        foreach (OnnxElementList part in parts) {
            if (part._shape.Count != rank) {
                return new Result(null, "Concat 输入秩不一致：" + rank + " 与 " + part._shape.Count + " / the inputs have different ranks");
            }
            for (int dimension = 0; dimension < rank; ++dimension) {
                if (dimension != normalized && part._shape[dimension] != parts[0]._shape[dimension]) {
                    return new Result(
                        null,
                        "Concat 在维度 " + dimension + " 上形状不一致：" + parts[0]._shape[dimension] + " 与 " + part._shape[dimension]
                        + " / the shapes differ on a dimension other than the axis");
                }
            }
        }

        var target = new int[rank];
        for (int dimension = 0; dimension < rank; ++dimension) {
            target[dimension] = dimension == normalized
                ? SumOnAxis(parts, dimension)
                : parts[0]._shape[dimension];
        }
        long outer = 1;
        for (int dimension = 0; dimension < normalized; ++dimension) {
            outer *= System.Math.Max(target[dimension], 1);
        }
        long inner = 1;
        for (int dimension = normalized + 1; dimension < rank; ++dimension) {
            inner *= System.Math.Max(target[dimension], 1);
        }

        var elements = new List<LinearPolynomial<Flt64>>();
        for (long outerIndex = 0; outerIndex < outer; ++outerIndex) {
            foreach (OnnxElementList part in parts) {
                long runLength = System.Math.Max(part._shape[normalized], 1) * inner;
                long start = outerIndex * runLength;
                for (long offset = 0; offset < runLength; ++offset) {
                    elements.Add(part._elements[(int)(start + offset)]);
                }
            }
        }
        return new Result(new OnnxElementList(target, elements), null);
    }

    /// <summary>
    /// 矩阵乘 / Matrix multiplication. 对齐基线 `lowerMatMul` 的线性部分。
    /// </summary>
    /// <param name="right">右操作数 / The right operand.</param>
    /// <returns>结果 / The product.</returns>
    /// <remarks>
    /// **秩 1 按 `(1,K)`／`(K,1)` 参与计算，末了把那一维压掉**（与基线同一个处理）；**秩 0 拒绝。**
    ///
    /// **这里只处理"至少一侧是常量"的线性情形。** **两侧都含变量时每个元素是二次的，而这一层只装一次多项式——**
    /// **基线能算是因为它有 `OnnxDegreeMode`，C# 侧的二次路径是独立的（`OnnxQuadraticProduct`）。** '
    /// **所以这里明确拒绝并点名原因，** **而不是把二次的乘积静默塞进一次多项式。**
    ///
    /// **批量维（秩 > 2）也明确拒绝：** **基线按 numpy 规则广播它，而这一层还没做——** **把批量维当普通维度算会让索引整体错位。**
    ///
    /// **Rank-1 operands participate as (1,K)/(K,1) with that dimension squeezed at the end, as in the baseline; rank 0 is refused.**
    ///
 /// ** covers the linear case where at least one side is constant.** **With variables on both sides each element becomes quadratic while this layer holds linear polynomials —
    /// the baseline can do it because it has OnnxDegreeMode, and the quadratic path on the C# side is separate (OnnxQuadraticProduct).** **So it is refused with the reason named, rather than
    /// quietly stuffing a quadratic product into a linear polynomial.**
    ///
    /// **Batch dimensions (rank > 2) are refused too: the baseline broadcasts them under numpy rules and this layer does not — treating a batch dimension as an ordinary one misaligns every
    /// index.**
    /// </remarks>
    public Result MatMul(OnnxElementList right) {
        if (right is null) {
            return new Result(null, "MatMul 的右操作数为空 / the right operand is null");
        }
        var leftShape = new List<int>(_shape);
        if (leftShape.Count == 1) {
            leftShape = new List<int> { 1, leftShape[0] };
        }
        var rightShape = new List<int>(right._shape);
        if (rightShape.Count == 1) {
            rightShape = new List<int> { rightShape[0], 1 };
        }
        if (leftShape.Count < 2 || rightShape.Count < 2) {
            return new Result(null, "MatMul 操作数秩不得为 0 / MatMul operands must not be rank 0");
        }

        int rows = leftShape[^2];
        int inner = leftShape[^1];
        int rightInner = rightShape[^2];
        int columns = rightShape[^1];
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
        ExpandTo(this, leftTarget, out OnnxElementList? leftFull);
        ExpandTo(right, rightTarget, out OnnxElementList? rightFull);
        if (leftFull is null || rightFull is null) {
            return new Result(null, "MatMul 的批量维无法展开 / the batch dimensions cannot be expanded");
        }

        if (inner != rightInner) {
            return new Result(null, "MatMul 内维不一致：" + inner + " 与 " + rightInner + " / MatMul inner dimensions differ");
        }
        // **两侧**都含变量才升到二次：**一侧是常量权重时乘积仍是一次，而那才是全连接层的常态。**
        // **原先把判定写成"任一侧含变量就拒绝"，混合情形被一律挡下——** **恒等权重的 `Gemm` 判据一接上就把这个反了的条件抓了出来。**
        //
        // **Only when both sides carry variables does the product reach degree two: with a constant weight it stays linear, which is the normal shape of a fully connected layer.** The check
        // used to be "either side symbolic is refused", which blocked every mixed case — the identity-weight Gemm criterion caught the inverted condition as soon as it was wired.
        if (!IsConstant(this) && !IsConstant(right)) {
            return new Result(
                null,
                "MatMul 两侧都含变量时结果是二次的，而这一层只装一次多项式 / a product of two symbolic operands is quadratic while this layer holds linear polynomials"
                + "（基线用 OnnxDegreeMode 处理，C# 侧二次路径是 OnnxQuadraticProduct / the baseline does it through OnnxDegreeMode；the quadratic path here is OnnxQuadraticProduct）");
        }

        // 混合情形下必须"把一个元素乘上另一侧的常数"，**而不是只取两边的常数项——** '
        // **只取常数项会把符号项整个丢掉，而结果看起来只是个常数。** **这就是恒等权重那一批里输出只剩 "1" 的原因。**
        //
        // In the mixed case one element has to be multiplied by the other side's constant, **not both sides' constant terms taken —** that drops every symbolic term and leaves what looks
        // like a plain constant. **That is why the identity-weight batch produced just "1".**
        bool rightIsConstant = IsConstant(rightFull);
        long leftBatchSize = rows * inner;
        long rightBatchSize = rightInner * columns;
        var elements = new List<LinearPolynomial<Flt64>>((int)(batchCount * rows * columns));
        for (long batch = 0; batch < batchCount; ++batch) {
            long leftOffset = batch * leftBatchSize;
            long rightOffset = batch * rightBatchSize;
            for (int row = 0; row < rows; ++row) {
                for (int column = 0; column < columns; ++column) {
                    var sum = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
                    for (int position = 0; position < inner; ++position) {
                        LinearPolynomial<Flt64> lhsElement = leftFull._elements[(int)(leftOffset + row * inner + position)];
                        LinearPolynomial<Flt64> rhsElement = rightFull._elements[(int)(rightOffset + position * columns + column)];
                        sum = rightIsConstant
                            ? sum + (lhsElement * new Flt64(rhsElement.Constant.ToDouble()))
                            : sum + (rhsElement * new Flt64(lhsElement.Constant.ToDouble()));
                    }
                    elements.Add(WithoutZeroTerms(sum));
                }
            }
        }

        var target = new List<int>(batchShape);
        if (_shape.Count != 1) {
            target.Add(rows);
        }
        if (right._shape.Count != 1) {
            target.Add(columns);
        }
        if (target.Count == 0) {
            target.Add(1);
        }
        return Create(target, elements);
    }

    /// <summary>
    /// 通用矩阵乘 / General matrix multiplication. 对齐基线 `lowerGemm`。
    /// </summary>
    /// <param name="right">右操作数 / The right operand.</param>
    /// <param name="bias">偏置 `C`，可空 / The bias C, nullable.</param>
    /// <param name="transA">是否转置左操作数 / Whether to transpose the left operand.</param>
    /// <param name="transB">是否转置右操作数 / Whether to transpose the right operand.</param>
    /// <param name="alpha">乘积的缩放 / The scale on the product.</param>
    /// <param name="beta">偏置的缩放 / The scale on the bias.</param>
    /// <returns>结果 / The result.</returns>
    /// <remarks>
    /// **顺序与基线一致：** **先按 `transA`／`transB` 转置，再乘，再乘 `alpha`，再把 `beta·C` 逐元素加上。** '
    /// **顺序换了会得到不同的数，而每一步单独看都对。**
    ///
    /// **只支持二维输入：** **基线的 `Gemm` 也只支持二维（它当场拒绝更高秩），** **而"秩 1 也当向量处理"是 `MatMul` 的规则，不是 `Gemm` 的。**
    ///
    /// **偏置 `C` 可以是 `[M,N]` 或 `[N]`：** **后者按行广播——** **这是 ONNX 允许的两种形态；** **其余形状拒绝，** '
    /// **因为"形状差不多"的偏置会让某一列静默加上别的列的值。**
    ///
    /// **The order matches the baseline: transpose by transA/transB, multiply, scale by alpha, then add beta·C elementwise.** **Any other order yields different numbers while every step looks right on its
    /// own.**
    ///
    /// **Two-dimensional inputs only: the baseline's Gemm refuses higher rank outright, and "rank 1 behaves as a vector" is MatMul's rule, not Gemm's.**
    ///
    /// **The bias may be [M,N] or [N]; the latter broadcasts over rows — the two forms ONNX allows.** **Other shapes are refused, because a roughly-shaped bias silently adds one column's values to
    /// another.**
    /// </remarks>
    public Result Gemm(
        OnnxElementList right,
        OnnxElementList? bias,
        bool transA,
        bool transB,
        double alpha,
        double beta) {
        if (right is null) {
            return new Result(null, "Gemm 的右操作数为空 / the right operand is null");
        }
        if (_shape.Count != 2 || right._shape.Count != 2) {
            return new Result(
                null,
                "Gemm 只支持二维输入，实际为 [" + string.Join(",", _shape) + "] 与 [" + string.Join(",", right._shape) + "] / Gemm supports 2-D inputs only");
        }
        OnnxElementList left = transA ? Transpose2D(this) : this;
        OnnxElementList product = transB ? Transpose2D(right) : right;
        Result multiplied = left.MatMul(product);
        if (multiplied.Value is not OnnxElementList multipliedList) {
            return new Result(null, multiplied.Failure);
        }
        OnnxElementList scaled = Scale(multipliedList, alpha);
        if (bias is null) {
            return new Result(scaled, null);
        }
        OnnxElementList scaledBias = Scale(bias, beta);
        return Add(scaled, scaledBias);
    }

    /// <summary>二维转置，与基线 `transpose2d` 同一个循环顺序 / A 2-D transpose with the baseline's loop order.</summary>
    public static OnnxElementList Transpose2D(OnnxElementList tensor) {
        int rows = tensor._shape[0];
        int columns = tensor._shape[1];
        var elements = new List<LinearPolynomial<Flt64>>(rows * columns);
        for (int column = 0; column < columns; ++column) {
            for (int row = 0; row < rows; ++row) {
                elements.Add(tensor._elements[row * columns + column]);
            }
        }
        return new OnnxElementList(new[] { columns, rows }, elements);
    }

    private static OnnxElementList Scale(OnnxElementList tensor, double factor) {
        var elements = new List<LinearPolynomial<Flt64>>(tensor.Count);
        foreach (LinearPolynomial<Flt64> element in tensor._elements) {
            elements.Add(element * new Flt64(factor));
        }
        return new OnnxElementList(tensor._shape, elements);
    }

    /// <summary>逐元素相加，`[N]` 偏置按行广播 / Elementwise addition, with an [N] bias broadcast over rows.</summary>
    private static Result Add(OnnxElementList left, OnnxElementList right) {
        if (left.Count == right.Count && left._shape.Count == right._shape.Count) {
            var same = new List<LinearPolynomial<Flt64>>(left.Count);
            for (int index = 0; index < left.Count; ++index) {
                same.Add(left._elements[index] + right._elements[index]);
            }
            var copy = new List<int>(left._shape);
            return new Result(new OnnxElementList(copy, same), null);
        }
        if (left._shape.Count == 2 && right._shape.Count == 1 && right._shape[0] == left._shape[1]) {
            int rows = left._shape[0];
            int columns = left._shape[1];
            var broadcast = new List<LinearPolynomial<Flt64>>(left.Count);
            for (int row = 0; row < rows; ++row) {
                for (int column = 0; column < columns; ++column) {
                    broadcast.Add(left._elements[row * columns + column] + right._elements[column]);
                }
            }
            return new Result(new OnnxElementList(new[] { rows, columns }, broadcast), null);
        }
        return new Result(
            null,
            "Gemm 的偏置形状 [" + string.Join(",", right._shape) + "] 与结果 [" + string.Join(",", left._shape)
            + "] 不相容 / the bias shape is incompatible with the result");
    }

    /// <summary>
    /// 沿轴求和 / Sum along axes. 对齐基线 `lowerReduceSum`。
    /// </summary>
    /// <param name="axes">要归约的轴；为空表示全部 / the axes to reduce, empty meaning all.</param>
    /// <param name="keepDims">是否保留被归约的维度（长度为 1）/ whether the reduced dimensions stay, of length one.</param>
    /// <returns>结果 / The result.</returns>
    /// <remarks>
    /// **累加按"源坐标映射到目标坐标"分组，** **与基线同一个做法：** **每个位置算出它落到目标的哪个下标，落到同一个下标的加在一起。**
    ///
    /// **`keepdims` 不是装饰：** **`true` 时形状是 `[1,1]`，`false` 时是空形状（标量）——** '
    /// **两者元素个数都是 1，而下游按形状判断的东西完全不同。**
    ///
    /// **轴为空表示"全部归约"；** **负数轴按秩归一，越界拒绝。**
    ///
    /// **The accumulation groups by "source coordinate maps to which target coordinate", the same way the baseline does: each position works out the target index it falls into and positions
    /// sharing one index are added together.**
    ///
    /// **keepdims is not decoration: with true the shape is [1,1] and with false it is empty (a scalar) — both hold one element, and what a downstream reads off the shape differs
    /// entirely.**
    ///
    /// **An empty axes list means "reduce everything"; negatives are normalised by the rank and out-of-range axes are refused.**
    /// </remarks>
    public Result ReduceSum(IReadOnlyList<int>? axes, bool keepDims) {
        int rank = _shape.Count;
        var normalized = new List<int>();
        if (axes is null || axes.Count == 0) {
            for (int dimension = 0; dimension < rank; ++dimension) {
                normalized.Add(dimension);
            }
        }
        else {
            foreach (int axis in axes) {
                int converted = axis < 0 ? axis + rank : axis;
                if (converted < 0 || converted >= rank) {
                    return new Result(null, "ReduceSum 轴 " + axis + " 超出秩 " + rank + " 的范围 / the axis is out of range for the rank");
                }
                if (!normalized.Contains(converted)) {
                    normalized.Add(converted);
                }
            }
        }

        var targetShape = new List<int>();
        for (int dimension = 0; dimension < rank; ++dimension) {
            if (!normalized.Contains(dimension)) {
                targetShape.Add(_shape[dimension]);
            }
        }
        int[] sourceStrides = Strides(_shape);
        int[] targetStrides = Strides(targetShape);
        var slots = new Dictionary<int, int>();
        var accumulator = new List<LinearPolynomial<Flt64>>();
        var coordinate = new int[rank];
        int targetRank = targetShape.Count;
        var targetCoordinate = new int[targetRank];
        for (int index = 0; index < Count; ++index) {
            int remainder = index;
            for (int dimension = 0; dimension < rank; ++dimension) {
                coordinate[dimension] = remainder / System.Math.Max(sourceStrides[dimension], 1);
                remainder %= System.Math.Max(sourceStrides[dimension], 1);
            }
            int targetIndex = 0;
            int cursor = 0;
            for (int dimension = 0; dimension < rank; ++dimension) {
                if (normalized.Contains(dimension)) {
                    continue;
                }
                targetCoordinate[cursor] = coordinate[dimension];
                targetIndex += coordinate[dimension] * System.Math.Max(targetStrides[cursor], 1);
                ++cursor;
            }
            if (!slots.TryGetValue(targetIndex, out int slot)) {
                slot = accumulator.Count;
                slots[targetIndex] = slot;
                accumulator.Add(_elements[index]);
            }
            else {
                accumulator[slot] = WithoutZeroTerms(accumulator[slot] + _elements[index]);
            }
        }

        var finalShape = new List<int>();
        if (keepDims) {
            for (int dimension = 0; dimension < rank; ++dimension) {
                finalShape.Add(normalized.Contains(dimension) ? 1 : _shape[dimension]);
            }
        }
        else {
            finalShape.AddRange(targetShape);
        }
        return Create(finalShape, accumulator);
    }

    /// <summary>
    /// 去掉系数为零的项 / Drop monomials whose coefficient is zero.
    /// </summary>
    /// <remarks>
    /// **零系数项不是错的，但它会让模型多出一列"系数恒为零"的变量：** **求解器看得见它，而它在每条约束里都不起作用——** '
    /// **这类列会让"变量数"这个指标失去意义，也会让下游的化简多做一遍无用功。**
    ///
    /// **A zero-coefficient term is not wrong, but it adds a column whose coefficient is always zero: the solver sees it and it does nothing in any constraint —** such columns make
    /// "variable count" meaningless and give any downstream simplification useless work.
    /// </remarks>
    private static LinearPolynomial<Flt64> WithoutZeroTerms(LinearPolynomial<Flt64> polynomial) {
        var kept = new List<LinearMonomial<Flt64>>(polynomial.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            if (monomial.Coefficient.ToDouble() != 0.0) {
                kept.Add(monomial);
            }
        }
        return new LinearPolynomial<Flt64>(kept, polynomial.Constant);
    }

    /// <summary>
    /// 按 numpy 规则把元素展开到目标形状 / Expand the elements to a target shape under numpy rules.
    /// </summary>
    /// <remarks>
    /// **从右往左对齐：** **源维为 1、或源没有这一维时按目标长度重复，** **其余必须相等。** '
    /// **"源没有这一维"按重复处理而不是拒绝——** **批量维广播的常见形态正是 `[2,2]` 对 `[3,2,2]`。**
    ///
    /// **Align from the right: a source dimension of 1, or one the source does not have, repeats to the target length, and everything else has to match.** **"The source lacks this
    /// dimension" means repeat rather than refuse — the normal shape of batch broadcasting is [2,2] against [3,2,2].**
    /// </remarks>
    /// <summary>
    /// 按 numpy 规则算出"目标每个下标读源里哪个下标" / The source index each target index reads, under numpy rules.
    /// </summary>
    /// <param name="source">源形状 / The source shape.</param>
    /// <param name="target">目标形状 / The target shape.</param>
    /// <returns>下标映射，或 `null` 表示不相容 / the index map, or null when incompatible.</returns>
    /// <remarks>
    /// **下标数学只写一份：** **一次域与二次域的展开规则完全一样，** **差的只是"拿这套下标去取哪一种元素"。** '
    /// **所以这里返回下标，两个域各自用自己的元素去取——** **而不是把同一段循环抄两遍。**
    ///
    /// **The index arithmetic is written once: the expansion rules are identical in the two domains and the only difference is which elements the indices are used to pick.** So this returns
    /// indices and each domain reads its own elements — **rather than copying the same loop twice.**
    /// </remarks>
    internal static int[]? ExpandIndices(IReadOnlyList<int> source, IReadOnlyList<int> target) {
        if (target.Count < source.Count) {
            return null;
        }
        int offset = target.Count - source.Count;
        for (int dimension = 0; dimension < source.Count; ++dimension) {
            int wanted = target[offset + dimension];
            int given = source[dimension];
            if (given != wanted && given != 1) {
                return null;
            }
        }
        long total = 1;
        foreach (int dimension in target) {
            total *= System.Math.Max(dimension, 1);
        }
        int[] targetStrides = Strides(target);
        int[] sourceStrides = Strides(source);
        var indices = new int[total];
        var coordinate = new int[target.Count];
        for (long index = 0; index < total; ++index) {
            long remainder = index;
            for (int dimension = 0; dimension < target.Count; ++dimension) {
                coordinate[dimension] = (int)(remainder / System.Math.Max(targetStrides[dimension], 1));
                remainder %= System.Math.Max(targetStrides[dimension], 1);
            }
            int sourceIndex = 0;
            for (int dimension = 0; dimension < source.Count; ++dimension) {
                int position = offset + dimension;
                int taken = source[dimension] == 1 ? 0 : coordinate[position];
                sourceIndex += taken * System.Math.Max(sourceStrides[dimension], 1);
            }
            indices[index] = sourceIndex;
        }
        return indices;
    }

    private static void ExpandTo(OnnxElementList source, IReadOnlyList<int> target, out OnnxElementList? expanded) {
        expanded = null;
        int[]? indices = ExpandIndices(source._shape, target);
        if (indices is null) {
            return;
        }
        var result = new List<LinearPolynomial<Flt64>>(indices.Length);
        foreach (int index in indices) {
            result.Add(source._elements[index]);
        }
        expanded = new OnnxElementList(new List<int>(target), result);
    }

    private static bool IsConstant(OnnxElementList list) {
        foreach (LinearPolynomial<Flt64> element in list._elements) {
            if (element.Monomials.Count != 0) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 按索引沿轴采集 / Gather by index along an axis. 对齐基线 `lowerGather`。
    /// </summary>
    /// <param name="indices">索引（负数按轴长归一）/ the indices, with negatives normalised by the axis length.</param>
    /// <param name="axis">采集轴，负数按秩归一 / the axis, negatives normalised by the rank.</param>
    /// <returns>采集结果 / The gather result.</returns>
    /// <remarks>
    /// **索引顺序就是结果顺序：** **`[2,0]` 取出来的是"第二个、再第一个"，不是排好序的两个。** '
    /// **排序会静默改掉语义，而形状与元素个数完全一样。**
    ///
    /// **负数按轴长归一，越界拒绝：** **两者在数值上都表现为"取到了别的元素"——** **归一写错会取到对称位置，越界放过会绕回开头。**
    ///
    /// **The index order is the result order: [2,0] yields the second element and then the first, not the two in sorted order.** Sorting would silently change the meaning while the shape
    /// and the count stay identical.
    ///
    /// **Negatives are normalised by the axis length and out-of-range indices are refused: both show up numerically as "a different element came back" — a wrong normalisation picks the
    /// mirrored position and letting an out-of-range index through wraps to the start.**
    /// </remarks>
    public Result Gather(IReadOnlyList<int> indices, int axis) {
        if (indices is null) {
            return new Result(null, "Gather 需要索引 / Gather needs indices");
        }
        int rank = _shape.Count;
        if (rank == 0) {
            return new Result(null, "标量没有可采集的轴 / a scalar has no axis to gather along");
        }
        int normalized = axis < 0 ? axis + rank : axis;
        if (normalized < 0 || normalized >= rank) {
            return new Result(null, "采集轴 " + axis + " 越界（秩 " + rank + "）/ the axis is out of range for the rank");
        }
        int axisSize = _shape[normalized];
        var resolved = new List<int>(indices.Count);
        foreach (int raw in indices) {
            int index = raw < 0 ? raw + axisSize : raw;
            if (index < 0 || index >= axisSize) {
                return new Result(
                    null,
                    "索引 " + raw + " 超出轴长度 " + axisSize + " / the index " + raw + " is out of range for axis size " + axisSize);
            }
            resolved.Add(index);
        }

        long outer = 1;
        for (int dimension = 0; dimension < normalized; ++dimension) {
            outer *= System.Math.Max(_shape[dimension], 1);
        }
        long inner = 1;
        for (int dimension = normalized + 1; dimension < rank; ++dimension) {
            inner *= System.Math.Max(_shape[dimension], 1);
        }

        var target = new List<int>(rank + 1);
        for (int dimension = 0; dimension < normalized; ++dimension) {
            target.Add(_shape[dimension]);
        }
        target.Add(resolved.Count);
        for (int dimension = normalized + 1; dimension < rank; ++dimension) {
            target.Add(_shape[dimension]);
        }

        var elements = new List<LinearPolynomial<Flt64>>();
        for (long outerIndex = 0; outerIndex < outer; ++outerIndex) {
            foreach (int index in resolved) {
                long start = outerIndex * axisSize * inner + index * inner;
                for (long offset = 0; offset < inner; ++offset) {
                    elements.Add(_elements[(int)(start + offset)]);
                }
            }
        }
        return Create(target, elements);
    }

    /// <summary>
    /// 分片结果 / The split result.
    /// </summary>
    /// <param name="Values">分出的各段 / The pieces produced.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record SplitResult(IReadOnlyList<OnnxElementList>? Values, string? Failure)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 按轴分片 / Split along an axis. 对齐基线 `split`。
    /// </summary>
    /// <param name="axis">分片轴，负数按秩归一 / the axis, negatives normalised by the rank.</param>
    /// <param name="sizes">每段长度；为空表示均分 / the size of each piece; empty means split evenly.</param>
    /// <param name="outputCount">输出个数 / the number of outputs.</param>
    /// <returns>分片结果 / The result.</returns>
    /// <remarks>
    /// **切口与基线同一个游标：** **每处理完外层一个坐标，游标就前进"该段长度 × 轴后元素数"，** **于是下一段从上一个的末尾接着取。**
    ///
    /// **均分要求轴长能被整除，否则拒绝。** **"除不尽就向下取整"会让最后一段短一截，而形状上谁也看不出来——** '
    /// **只有那段少了一个元素。**
    ///
    /// **The cut follows the baseline's single cursor: after each outer coordinate the cursor advances by that piece's size times elements-after-axis, so the next piece starts where the
    /// previous ended.**
    ///
    /// **An even split requires the axis length to divide, and refuses otherwise.** "Round down when it does not divide" leaves the last piece short, which no shape reveals — only that
    /// piece is missing an element.**
    /// </remarks>
    public SplitResult Split(int axis, IReadOnlyList<int>? sizes, int outputCount) {
        int rank = _shape.Count;
        if (rank == 0) {
            return new SplitResult(null, "标量没有可分的轴 / a scalar has no axis to split along");
        }
        int normalized = axis < 0 ? axis + rank : axis;
        if (normalized < 0 || normalized >= rank) {
            return new SplitResult(null, "分片轴 " + axis + " 越界（秩 " + rank + "）/ the axis is out of range for the rank");
        }
        if (outputCount <= 0) {
            return new SplitResult(null, "分片需要至少一个输出 / a split needs at least one output");
        }
        int axisSize = _shape[normalized];
        var resolved = new List<int>(outputCount);
        if (sizes is null || sizes.Count == 0) {
            if (axisSize % outputCount != 0) {
                return new SplitResult(
                    null,
                    "轴长度 " + axisSize + " 无法均分为 " + outputCount + " 段 / the axis of length " + axisSize
                    + " cannot be split evenly into " + outputCount + " parts");
            }
            for (int index = 0; index < outputCount; ++index) {
                resolved.Add(axisSize / outputCount);
            }
        }
        else {
            if (sizes.Count != outputCount) {
                return new SplitResult(null, "split 长度 " + sizes.Count + " 与输出数量 " + outputCount + " 不符 / the split length disagrees with the output count");
            }
            int total = 0;
            foreach (int size in sizes) {
                if (size <= 0) {
                    return new SplitResult(null, "split 含非正段长 " + size + " / the split holds a non-positive segment size");
                }
                total += size;
                resolved.Add(size);
            }
            if (total != axisSize) {
                return new SplitResult(null, "split 之和 " + total + " 与轴长度 " + axisSize + " 不符 / the split sum disagrees with the axis length");
            }
        }

        long outer = 1;
        for (int dimension = 0; dimension < normalized; ++dimension) {
            outer *= System.Math.Max(_shape[dimension], 1);
        }
        long inner = 1;
        for (int dimension = normalized + 1; dimension < rank; ++dimension) {
            inner *= System.Math.Max(_shape[dimension], 1);
        }

        var buffers = new List<LinearPolynomial<Flt64>>[outputCount];
        var shapes = new int[outputCount][];
        for (int index = 0; index < outputCount; ++index) {
            buffers[index] = new List<LinearPolynomial<Flt64>>();
            var shape = new int[rank];
            for (int dimension = 0; dimension < rank; ++dimension) {
                shape[dimension] = dimension == normalized ? resolved[index] : _shape[dimension];
            }
            shapes[index] = shape;
        }

        long cursor = 0;
        for (long outerIndex = 0; outerIndex < outer; ++outerIndex) {
            for (int outputIndex = 0; outputIndex < outputCount; ++outputIndex) {
                long sliceSize = System.Math.Max(resolved[outputIndex], 1) * inner;
                for (long offset = 0; offset < sliceSize; ++offset) {
                    buffers[outputIndex].Add(_elements[(int)(cursor + offset)]);
                }
                cursor += sliceSize;
            }
        }

        var pieces = new List<OnnxElementList>(outputCount);
        for (int index = 0; index < outputCount; ++index) {
            Result piece = Create(shapes[index], buffers[index]);
            if (piece.Value is not OnnxElementList list) {
                return new SplitResult(null, piece.Failure);
            }
            pieces.Add(list);
        }
        return new SplitResult(pieces, null);
    }

    private static int SumOnAxis(IReadOnlyList<OnnxElementList> parts, int axis) {
        int total = 0;
        foreach (OnnxElementList part in parts) {
            total += part._shape[axis];
        }
        return total;
    }

    /// <summary>行主序步长 / Row-major strides.</summary>
    private static int[] Strides(IReadOnlyList<int> shape) {
        var strides = new int[shape.Count];
        int running = 1;
        for (int axis = shape.Count - 1; axis >= 0; --axis) {
            strides[axis] = running;
            running *= System.Math.Max(shape[axis], 1);
        }
        return strides;
    }
}