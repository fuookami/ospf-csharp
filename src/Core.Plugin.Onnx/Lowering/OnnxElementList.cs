#nullable enable

// IDE0007 与 IDE0008 会在同一个泛型局部上互相要求（一个说用 var、一个说用显式类型），而这里两处都触发。
// 按仓库既有做法在文件级关闭这两条，理由记录在此。
//
// IDE0007 and IDE0008 demand opposite things on the same generic local, and both fire here. Disabled at file scope per the existing repository practice, with the reason recorded.
#pragma warning disable IDE0007, IDE0008

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **逐元素表示：形状 + 每个位置一个表达式** / The per-element representation: a shape plus one expression per position.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxTensorExpr`。**这是清单第 2、3 项共同缺的那一层：** '
/// **既有实现每个名字只有一个标量表达式，于是 `Transpose` 的重排无处可写、`Scan` 的运行期切片无处可切。**
///
/// **这一批只做这一层本体，不接线：** **`Transpose` 按坐标重排、`SliceLeading` 沿首维切片，两者都是纯函数，**
/// **可以直接用"某位置的元素是不是原来那一个"钉住。** **接线要改求值器的每个调用点，那属于下一批——**
/// **先把"重排算得对"这件事独立验证，接线时才有可对照的东西。**
///
/// Syncs `OnnxTensorExpr`. **This is the layer the checklist's items 2 and 3 both lack:** **the existing implementation holds one scalar expression per name, so a Transpose has
/// nowhere to write the permutation and a Scan runtime slice has nowhere to be cut.**
///
/// **This batch builds the layer itself without wiring it: Transpose permutes by coordinate and SliceLeading cuts along the leading axis, both pure functions, and both can be pinned
/// directly by "is the element at this position the same one as before".** **Wiring touches every call site of the evaluator, which is the next batch — verifying that the
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