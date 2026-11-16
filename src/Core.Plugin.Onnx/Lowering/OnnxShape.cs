#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **张量的形状算术** / Tensor shape arithmetic.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 里 `OnnxShape` 那一块。**它是 `Transpose`／`Reshape`／`Flatten`／`Concat`／`Split`／
/// `Gather`／`ReduceSum` 全部形状算子的共用底座，而它的错法都属于"不抛异常但算错"：** **步长算错只是索引取到别的元素，
/// 广播算错只是形状多出一维。**
///
/// Syncs the `OnnxShape` part of. **It is the shared foundation of every shape operator (Transpose, Reshape, Flatten, Concat, Split, Gather,
/// ReduceSum), and all of its mistakes are of the "no exception, wrong answer" kind: a wrong stride just fetches another element, a wrong broadcast just adds
/// a dimension.**
/// </remarks>
public static class OnnxShape
{
    /// <summary>
    /// 行主序步长 / Row-major strides.
    /// </summary>
    /// <param name="shape">形状 / The shape.</param>
    /// <returns>每维步长 / The stride of each dimension.</returns>
    /// <remarks>
    /// **从最后一维往前累乘，不是从第一维往后：** **写成 `strides[i] = shape[i] * strides[i-1]` 得到的是一组"看起来合理"的数，**
    /// **而用它折出来的索引会把 `[1, 2]` 与 `[2, 1]` 弄反。**
    ///
    /// **The product runs from the last dimension backwards, not forwards: writing `strides[i] = shape[i] * strides[i-1]` yields a plausible-looking array whose
    /// folded indices swap `[1, 2]` with `[2, 1]`.**
    /// </remarks>
    public static int[] Strides(IReadOnlyList<int> shape) {
        int[] strides = new int[shape.Count];
        for (int index = 0; index < strides.Length; ++index) {
            strides[index] = 1;
        }
        for (int index = shape.Count - 1; index >= 0; --index) {
            strides[index] = index == shape.Count - 1 ? 1 : strides[index + 1] * shape[index + 1];
        }
        return strides;
    }

    /// <summary>
    /// 把展平索引展开成多维坐标 / Expand a flattened index into coordinates.
    /// </summary>
    /// <param name="index">展平索引 / The flattened index.</param>
    /// <param name="strides">步长 / The strides.</param>
    /// <returns>坐标 / The coordinates.</returns>
    public static int[] Coordinates(int index, IReadOnlyList<int> strides) {
        int[] coordinates = new int[strides.Count];
        int remaining = index;
        for (int dimension = 0; dimension < strides.Count; ++dimension) {
            coordinates[dimension] = remaining / strides[dimension];
            remaining %= strides[dimension];
        }
        return coordinates;
    }

    /// <summary>
    /// 把多维坐标折成展平索引 / Fold coordinates into a flattened index.
    /// </summary>
    /// <param name="coordinates">坐标 / The coordinates.</param>
    /// <param name="strides">步长 / The strides.</param>
    /// <returns>展平索引 / The flattened index.</returns>
    public static int Flatten(IReadOnlyList<int> coordinates, IReadOnlyList<int> strides) {
        int index = 0;
        for (int dimension = 0; dimension < coordinates.Count; ++dimension) {
            index += coordinates[dimension] * strides[dimension];
        }
        return index;
    }

    /// <summary>
    /// 元素总数 / The element count.
    /// </summary>
    /// <param name="shape">形状 / The shape.</param>
    /// <returns>总数 / The count.</returns>
    public static int ElementCount(IReadOnlyList<int> shape) {
        int count = 1;
        for (int index = 0; index < shape.Count; ++index) {
            count *= shape[index];
        }
        return count;
    }

    /// <summary>
    /// 规范化轴（负数从末尾数起）/ Normalise an axis, counting negatives from the end.
    /// </summary>
    /// <param name="axis">轴 / The axis.</param>
    /// <param name="rank">秩 / The rank.</param>
    /// <returns>规范化后的轴；越界时为 null / The normalised axis, or null when out of range.</returns>
    /// <remarks>
    /// **越界返回 null 而不是取模：** **`axis % rank` 会把 `axis = rank` 静默折回 0，** **而 `axis = rank` 是一个明显写错的模型——**
    /// **折回 0 让它在第一维上悄悄算完。**
    ///
    /// **Out of range returns null rather than wrapping: `axis % rank` would fold `axis = rank` back to 0, while `axis = rank` is a plainly wrong model, and
    /// folding lets it quietly finish on the first dimension.**
    /// </remarks>
    public static int? NormalizeAxis(long axis, int rank) {
        long normalised = axis < 0 ? axis + rank : axis;
        if (normalised < 0 || normalised >= rank) {
            return null;
        }
        return (int)normalised;
    }

    /// <summary>
    /// 广播后的形状 / The broadcast result shape.
    /// </summary>
    /// <param name="left">左形状 / The left shape.</param>
    /// <param name="right">右形状 / The right shape.</param>
    /// <param name="failure">不兼容时的说明 / The explanation when incompatible.</param>
    /// <returns>广播形状；不兼容时为 null / The broadcast shape, or null when incompatible.</returns>
    /// <remarks>
    /// **对齐是从末尾对齐的，不是从开头：** **`[2, 3]` 与 `[3]` 要广播成 `[2, 3]`，** **而从开头对齐会得到 `[2, 3]` 的错配或直接拒绝。**
    /// **维度为 1 的一侧被拉伸，两侧都非 1 且不等才是错误。**
    ///
    /// **Alignment runs from the end, not the start: `[2, 3]` and `[3]` broadcast to `[2, 3]`, while aligning from the start mismatches or refuses outright.
    /// A dimension of 1 is stretched; only two non-1 unequal dimensions are an error.**
    /// </remarks>
    public static int[]? BroadcastShape(IReadOnlyList<int> left, IReadOnlyList<int> right, out string? failure) {
        int rank = System.Math.Max(left.Count, right.Count);
        int[] result = new int[rank];
        for (int position = 0; position < rank; ++position) {
            int leftIndex = left.Count - rank + position;
            int rightIndex = right.Count - rank + position;
            int leftDimension = leftIndex >= 0 ? left[leftIndex] : 1;
            int rightDimension = rightIndex >= 0 ? right[rightIndex] : 1;
            if (leftDimension != rightDimension && leftDimension != 1 && rightDimension != 1) {
                failure = "广播维度不兼容：[" + string.Join(", ", left) + "] 与 [" + string.Join(", ", right)
                          + "] / incompatible broadcast dimensions";
                return null;
            }
            result[position] = System.Math.Max(leftDimension, rightDimension);
        }
        failure = null;
        return result;
    }

    /// <summary>
    /// `Transpose` 的目标形状 / The target shape of a `Transpose`.
    /// </summary>
    /// <param name="shape">输入形状 / The input shape.</param>
    /// <param name="perm">置换；为空表示反转 / The permutation; empty means reverse.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>目标形状 / The target shape.</returns>
    /// <remarks>
    /// **空 `perm` 在 ONNX 里表示"反转各维"，不是"不变"：** **把它当成不变会让 3 阶张量整体不转置，** **而下游仍然能算完——
    /// 只是每个元素都来自另一个位置。** 判据钉住这一点。
    ///
    /// **An empty `perm` means "reverse the dimensions" in ONNX, not "leave them": treating it as identity leaves a rank-3 tensor untransposed, and the
    /// downstream still completes — every element just comes from another position.** A criterion pins this.
    /// </remarks>
    public static int[]? TransposeShape(IReadOnlyList<int> shape, IReadOnlyList<long> perm, out string? failure) {
        int rank = shape.Count;
        int[] normalised = new int[rank];
        if (perm.Count == 0) {
            for (int index = 0; index < rank; ++index) {
                normalised[index] = rank - 1 - index;
            }
        }
        else {
            if (perm.Count != rank) {
                failure = "Transpose 置换长度 " + perm.Count + " 与秩 " + rank + " 不符 / permutation length does not match the rank";
                return null;
            }
            bool[] seen = new bool[rank];
            for (int index = 0; index < rank; ++index) {
                int? axis = NormalizeAxis(perm[index], rank);
                if (axis is not int value) {
                    failure = "Transpose 置换 " + perm[index] + " 超出秩 " + rank + " 的范围 / permutation out of range";
                    return null;
                }
                if (seen[value]) {
                    failure = "Transpose 置换 [" + string.Join(", ", perm) + "] 不是 " + rank + " 阶排列 / permutation is not a permutation";
                    return null;
                }
                seen[value] = true;
                normalised[index] = value;
            }
        }
        int[] target = new int[rank];
        for (int index = 0; index < rank; ++index) {
            target[index] = shape[normalised[index]];
        }
        failure = null;
        return target;
    }
}