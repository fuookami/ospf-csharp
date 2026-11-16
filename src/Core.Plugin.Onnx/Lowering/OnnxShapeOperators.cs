#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **六个形状算子的形状推导** / Shape inference for six shape operators.
/// </summary>
/// <remarks>
/// 。**这些算子只算形状、不搬元素，所以"错一位就静默"：**
/// **`Reshape` 的 `-1` 算错只是某个维度变成另一个数，`Concat` 的轴错只是拼错一维，`ReduceSum` 的 `keepDims` 错只是秩差一。**
/// **它们都不会抛异常，只会在下游让一个约束绑错变量。**
///
/// Syncs. **These operators compute shapes without moving elements, so "one off" is silent: a wrong `-1` in Reshape just makes a dimension
/// another number, a wrong axis in Concat just joins the wrong dimension, a wrong `keepDims` just differs by one rank.** **None of them throws; downstream a
/// constraint simply binds the wrong variable.**
/// </remarks>
public static class OnnxShapeOperators
{
    /// <summary>
    /// `Reshape` 的目标形状 / The target shape of a `Reshape`.
    /// </summary>
    /// <param name="inputShape">输入形状 / The input shape.</param>
    /// <param name="requested">请求的目标形状 / The requested target shape.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>目标形状 / The target shape.</returns>
    /// <remarks>
    /// **`-1` 是"由元素数推断"，`0` 是"沿用同位置的源维度"，两者都不是普通的数字。** **`0` 尤其容易被当成"这个维度是空的"——**
    /// **而一个空的维度会让元素数为 0，整条分支直接失效。** 两者都按 ONNX 语义处理，其余非正数一律拒绝。
    ///
    /// **`-1` means "infer from the element count" and `0` means "reuse the source dimension at this position"; neither is an ordinary number.** **`0` is
    /// especially easy to read as "this dimension is empty", and an empty dimension makes the element count zero, killing the branch outright.** Both follow the
    /// ONNX semantics; any other non-positive value is refused.
    /// </remarks>
    public static int[]? Reshape(IReadOnlyList<int> inputShape, IReadOnlyList<long> requested, out string? failure) {
        int total = OnnxShape.ElementCount(inputShape);
        int[] resolved = new int[requested.Count];
        int inferredIndex = -1;
        long known = 1;

        for (int position = 0; position < requested.Count; ++position) {
            long dimension = requested[position];
            if (dimension == -1L) {
                if (inferredIndex >= 0) {
                    failure = "shape 中出现多个 -1 / the shape contains more than one -1";
                    return null;
                }
                inferredIndex = position;
            }
            else if (dimension == 0L) {
                if (position >= inputShape.Count) {
                    failure = "0 出现在源形状不存在的位置 " + position + " / a 0 entry points past the source shape";
                    return null;
                }
                resolved[position] = inputShape[position];
                known *= resolved[position];
            }
            else if (dimension > 0L) {
                resolved[position] = (int)dimension;
                known *= resolved[position];
            }
            else {
                failure = "非法维度 " + dimension + " / illegal dimension";
                return null;
            }
        }

        if (inferredIndex >= 0) {
            if (known == 0L || total % known != 0L) {
                failure = "无法从元素数 " + total + " 推断 -1 维度 / cannot infer the -1 dimension from " + total + " elements";
                return null;
            }
            resolved[inferredIndex] = (int)(total / known);
        }

        long product = 1;
        for (int index = 0; index < resolved.Length; ++index) {
            product *= resolved[index];
        }
        if (product != total) {
            failure = "目标形状 [" + string.Join(", ", resolved) + "] 的元素数 " + product + " 与源元素数 " + total + " 不符 / target shape holds a different element count";
            return null;
        }
        for (int index = 0; index < resolved.Length; ++index) {
            if (resolved[index] <= 0) {
                failure = "Reshape 目标形状含非正维度 [" + string.Join(", ", resolved) + "] / target shape contains a non-positive dimension";
                return null;
            }
        }
        failure = null;
        return resolved;
    }

    /// <summary>
    /// `Flatten` 的目标形状 / The target shape of a `Flatten`.
    /// </summary>
    /// <param name="inputShape">输入形状 / The input shape.</param>
    /// <param name="axis">展平轴 / The flatten axis.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>目标形状 / The target shape.</returns>
    /// <remarks>
    /// **轴的合法区间是 `[-rank, rank]`，比别的算子多一个端点：** **`Flatten` 的 `axis = rank` 合法且含义是"左半边什么也不留"。**
    /// **按别的算子那样判 `axis >= rank` 越界，会把这个合法用例全部拒掉。**
    ///
    /// **The legal axis range is `[-rank, rank]`, one endpoint wider than elsewhere: `Flatten` allows `axis = rank`, meaning "the left half is empty". Judging it
    /// out of range like every other operator would refuse that whole legal case.**
    /// </remarks>
    public static int[]? Flatten(IReadOnlyList<int> inputShape, long axis, out string? failure) {
        int rank = inputShape.Count;

        // 轴区间是 [-rank, rank]：负值加的是 rank，而不是 rank + 1。
        // 用 rank + 1 做规范化会让 axis = -1 落到 rank（即"右边什么也不留"），把一个合法的 [-1] 读成了 [24, 1]。
        //
        // The range is [-rank, rank]: a negative axis adds rank, not rank + 1. Normalising against rank + 1 sends axis = -1 to rank (the "right half empty" case),
        // reading a legal -1 as [24, 1].
        long normalisedRaw = axis < 0 ? axis + rank : axis;
        if (normalisedRaw < 0 || normalisedRaw > rank) {
            failure = "Flatten 轴 " + axis + " 超出秩 " + rank + " 的范围 / Flatten axis is out of range";
            return null;
        }
        int split = (int)normalisedRaw;
        int left = 1;
        for (int index = 0; index < split; ++index) {
            left *= inputShape[index];
        }
        int right = 1;
        for (int index = split; index < rank; ++index) {
            right *= inputShape[index];
        }
        failure = null;
        return new[] { left, right };
    }

    /// <summary>
    /// `Concat` 的目标形状 / The target shape of a `Concat`.
    /// </summary>
    /// <param name="shapes">各输入形状 / The input shapes.</param>
    /// <param name="axis">拼接轴 / The concatenation axis.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>目标形状 / The target shape.</returns>
    /// <remarks>
    /// **非拼接轴必须完全一致：** **只校验轴合法、不校验其余维度，会让 `Concat([2,3],[2,4], axis=0)` 静默产出 `[4,3]`，**
    /// **而第二个输入的 4 个元素被整段丢掉。**
    ///
    /// **Every non-concatenated axis has to match exactly: validating only the axis would let `Concat([2,3],[2,4], axis=0)` silently produce `[4,3]`, dropping all
    /// four elements of the second input.**
    /// </remarks>
    public static int[]? Concat(IReadOnlyList<IReadOnlyList<int>> shapes, long axis, out string? failure) {
        if (shapes.Count == 0) {
            failure = "Concat 没有输入 / Concat has no inputs";
            return null;
        }
        int rank = shapes[0].Count;
        int? normalised = OnnxShape.NormalizeAxis(axis, rank);
        if (normalised is not int target) {
            failure = "Concat 轴 " + axis + " 超出秩 " + rank + " 的范围 / Concat axis is out of range";
            return null;
        }
        int[] result = new int[rank];
        for (int index = 0; index < rank; ++index) {
            result[index] = shapes[0][index];
        }
        result[target] = 0;
        for (int which = 0; which < shapes.Count; ++which) {
            IReadOnlyList<int> shape = shapes[which];
            if (shape.Count != rank) {
                failure = "Concat 输入 " + which + " 的秩 " + shape.Count + " 与首输入的秩 " + rank + " 不符 / the ranks differ";
                return null;
            }
            for (int index = 0; index < rank; ++index) {
                if (index == target) {
                    result[index] += shape[index];
                }
                else if (shape[index] != shapes[0][index]) {
                    failure = "Concat 在非拼接轴 " + index + " 上维度不一致：" + shapes[0][index] + " 与 " + shape[index]
                              + " / dimensions differ on a non-concatenated axis";
                    return null;
                }
            }
        }
        failure = null;
        return result;
    }

    /// <summary>
    /// `Split` 的目标形状 / The target shapes of a `Split`.
    /// </summary>
    /// <param name="inputShape">输入形状 / The input shape.</param>
    /// <param name="axis">拆分轴 / The split axis.</param>
    /// <param name="sizes">每段长度；为 null 时按段数均分 / The segment lengths, or null to divide evenly by count.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>各输出形状 / The output shapes.</returns>
    /// <remarks>
    /// **不给 `sizes` 时"均分"必须覆盖不能整除的情形：** **ONNX 允许最后一段吃掉余数，** **而不是要求每个模型都恰好整除——**
    /// **按整除拒绝会让一大类真实模型打不开。**
    ///
    /// **Without `sizes`, the even division must handle the non-divisible case: ONNX lets the last segment absorb the remainder rather than requiring every model to
    /// divide exactly, and refusing would reject a large class of real models.**
    /// </remarks>
    public static IReadOnlyList<int[]>? Split(IReadOnlyList<int> inputShape, long axis, IReadOnlyList<long>? sizes, out string? failure) {
        int rank = inputShape.Count;
        int? normalised = OnnxShape.NormalizeAxis(axis, rank);
        if (normalised is not int target) {
            failure = "Split 轴 " + axis + " 超出秩 " + rank + " 的范围 / Split axis is out of range";
            return null;
        }
        int length = inputShape[target];
        var results = new List<int[]>();

        if (sizes is null || sizes.Count == 0) {
            int segments = 2;
            int baseLength = length / segments;
            for (int index = 0; index < segments; ++index) {
                int segmentLength = index == segments - 1 ? length - baseLength * (segments - 1) : baseLength;
                results.Add(WithAxis(inputShape, target, segmentLength));
            }
        }
        else {
            long sum = 0;
            foreach (long size in sizes) {
                if (size <= 0) {
                    failure = "Split 段长 " + size + " 非正 / a split length is not positive";
                    return null;
                }
                sum += size;
            }
            if (sum != length) {
                failure = "Split 段长之和 " + sum + " 与轴长 " + length + " 不符 / the lengths do not add up to the axis length";
                return null;
            }
            foreach (long size in sizes) {
                results.Add(WithAxis(inputShape, target, (int)size));
            }
        }
        failure = null;
        return results;
    }

    /// <summary>
    /// `Gather` 的目标形状 / The target shape of a `Gather`.
    /// </summary>
    /// <param name="dataShape">数据形状 / The data shape.</param>
    /// <param name="indicesShape">索引形状 / The indices shape.</param>
    /// <param name="axis">取轴 / The gather axis.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>目标形状 / The target shape.</returns>
    /// <remarks>
    /// **`Gather` 是把索引形状塞进被取的轴，不是替换：** **秩等于 `数据秩 - 1 + 索引秩`。** **当成替换会让 1 阶数据配 1 阶索引得到 0 阶，**
    /// **而正确结果是 1 阶——** **秩差一，下游每个维度都错开一位。**
    ///
    /// **`Gather` splices the indices shape into the gathered axis rather than replacing it: the rank is `data rank - 1 + indices rank`.** **Treating it as a
    /// replacement turns rank-1 data with rank-1 indices into rank 0, while the right answer is rank 1 — off by one, so every downstream dimension shifts.**
    /// </remarks>
    public static int[]? Gather(IReadOnlyList<int> dataShape, IReadOnlyList<int> indicesShape, long axis, out string? failure) {
        int rank = dataShape.Count;
        int? normalised = OnnxShape.NormalizeAxis(axis, rank);
        if (normalised is not int target) {
            failure = "Gather 轴 " + axis + " 超出秩 " + rank + " 的范围 / Gather axis is out of range";
            return null;
        }
        var result = new List<int>();
        for (int index = 0; index < target; ++index) {
            result.Add(dataShape[index]);
        }
        result.AddRange(indicesShape);
        for (int index = target + 1; index < rank; ++index) {
            result.Add(dataShape[index]);
        }
        failure = null;
        return result.ToArray();
    }

    /// <summary>
    /// `ReduceSum` 的目标形状 / The target shape of a `ReduceSum`.
    /// </summary>
    /// <param name="inputShape">输入形状 / The input shape.</param>
    /// <param name="axes">归约轴；为空表示全部 / The reduced axes; empty means all.</param>
    /// <param name="keepDims">是否保留维度 / Whether to keep the reduced dimensions.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>目标形状 / The target shape.</returns>
    /// <remarks>
    /// **`keepDims` 只影响秩，不影响元素个数：** **两种写法得到的元素数都是 1，** **所以"元素数对不对"完全测不出 `keepDims` 是否生效。**
    /// **判据必须单独打秩。**
    ///
    /// **`keepDims` changes only the rank, not the element count: both forms hold one element, so "is the element count right" cannot detect a wrong `keepDims` at
    /// all.** A criterion has to assert the rank separately.
    /// </remarks>
    public static int[]? ReduceSum(IReadOnlyList<int> inputShape, IReadOnlyList<long>? axes, bool keepDims, out string? failure) {
        int rank = inputShape.Count;
        bool[] reduced = new bool[rank];
        if (axes is null || axes.Count == 0) {
            for (int index = 0; index < rank; ++index) {
                reduced[index] = true;
            }
        }
        else {
            foreach (long axis in axes) {
                int? normalised = OnnxShape.NormalizeAxis(axis, rank);
                if (normalised is not int target) {
                    failure = "ReduceSum 轴 " + axis + " 超出秩 " + rank + " 的范围 / ReduceSum axis is out of range";
                    return null;
                }
                reduced[target] = true;
            }
        }
        var result = new List<int>();
        for (int index = 0; index < rank; ++index) {
            if (reduced[index]) {
                if (keepDims) {
                    result.Add(1);
                }
            }
            else {
                result.Add(inputShape[index]);
            }
        }
        failure = null;
        return result.ToArray();
    }

    private static int[] WithAxis(IReadOnlyList<int> shape, int axis, int value) {
        int[] copy = new int[shape.Count];
        for (int index = 0; index < shape.Count; ++index) {
            copy[index] = shape[index];
        }
        copy[axis] = value;
        return copy;
    }
}