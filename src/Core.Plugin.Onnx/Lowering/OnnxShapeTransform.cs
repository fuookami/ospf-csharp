#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **只改形状、不改表达式的那组算子** / The operators that change the shape and leave the expression alone.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxShapeOperators` 在逐节点求值里的那一半。**这一组算子的共同点是"数值一个都不动"：** '
/// **`Reshape`／`Transpose`／`Flatten`／`Squeeze`／`Unsqueeze` 只是换了个说法，而仿射表达式本身与形状无关。**
///
/// **正因为"数值不动"，它们的错最容易漏：** **元素个数对不上时不会报错，只是后面某个算子按错的维度去接，**
/// **而错位之后每个维度都跟着错——** **所以元素个数守恒必须在这里检查，而不是指望下游。**
///
/// Syncs the node-by-node half of `OnnxShapeOperators`. **This group has one thing in common: not a single value moves — Reshape, Transpose, Flatten, Squeeze and Unsqueeze
/// only restate the shape, while an affine expression is shape-agnostic.**
///
/// **Precisely because the values do not move, their mistakes slip through most easily: a mismatched element count raises nothing, a later operator simply wires to the
/// wrong rank, and every dimension is off after that — so element-count conservation has to be checked here rather than left to the downstream.**
/// </remarks>
public static class OnnxShapeTransform
{
    /// <summary>本层支持的算子 / The operators this layer supports.</summary>
    public static readonly IReadOnlySet<string> Operators = new HashSet<string>(StringComparer.Ordinal) {
        "Reshape", "Transpose", "Flatten", "Squeeze", "Unsqueeze",
    };

    /// <summary>
    /// 变换结果 / The transformation result.
    /// </summary>
    /// <param name="Shape">变换后的形状 / The shape after the transformation.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Result(IReadOnlyList<int>? Shape, string? Failure)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 计算一个形状算子的结果形状 / Compute the result shape of one shape operator.
    /// </summary>
    /// <param name="opType">算子类型 / The operator type.</param>
    /// <param name="inputShape">输入形状 / The input shape.</param>
    /// <param name="target">`Reshape` 的目标形状（可为 null）/ The Reshape target, possibly null.</param>
    /// <param name="axes">`Transpose` 的置换轴 / The permutation for Transpose.</param>
    /// <param name="squeezeAxes">`Squeeze`／`Unsqueeze` 的轴（为空表示全部）/ The axes for Squeeze or Unsqueeze, empty meaning all.</param>
    /// <returns>变换结果 / The result.</returns>
    public static Result Compute(
        string opType,
        IReadOnlyList<int> inputShape,
        IReadOnlyList<int>? target = null,
        IReadOnlyList<int>? axes = null,
        IReadOnlyList<int>? squeezeAxes = null) {
        switch (opType) {
            case "Reshape":
                return Reshape(inputShape, target);
            case "Flatten":
                return new Result(Flatten(inputShape, axes), null);
            case "Transpose":
                return Transpose(inputShape, axes);
            case "Squeeze":
                return Squeeze(inputShape, squeezeAxes);
            case "Unsqueeze":
                return Unsqueeze(inputShape, squeezeAxes);
            default:
                return new Result(null, "算子 '" + opType + "' 不属于只改形状的那一组 / the operator is not part of the shape-only group");
        }
    }

    private static Result Reshape(IReadOnlyList<int> input, IReadOnlyList<int>? target) {
        if (target is null) {
            // 目标形状来自一个输入张量。它不是常量时，导入期不知道要变成什么。
            // The target comes from an input tensor; when it is not constant the import time cannot know what to reshape into.
            return new Result(null, "'Reshape' 的目标形状必须是常量 / the Reshape target has to be constant");
        }

        long wanted = 1;
        int inferAt = -1;
        for (int index = 0; index < target.Count; ++index) {
            if (target[index] == -1) {
                if (inferAt >= 0) {
                    return new Result(null, "'Reshape' 的目标形状有多于一个 -1 / the target asks to infer more than one dimension");
                }
                inferAt = index;
                continue;
            }
            if (target[index] < 0) {
                return new Result(null, "'Reshape' 的目标形状含负数 " + target[index] + " / the target holds a negative dimension");
            }
            wanted *= target[index];
        }

        long have = Elements(input);
        var resolved = new List<int>(target.Count);
        foreach (int dimension in target) {
            resolved.Add(dimension);
        }
        if (inferAt >= 0) {
            if (wanted == 0) {
                return new Result(null, "'Reshape' 要推断的那一维前面已经有 0 / a zero in the target makes the inferred dimension undefined");
            }
            if (have % wanted != 0) {
                return new Result(null,
                    "'Reshape' 的元素个数 " + have + " 无法被 " + wanted + " 整除 / the element count does not divide evenly, so the inferred dimension is not an integer");
            }
            resolved[inferAt] = (int)(have / wanted);
            wanted *= resolved[inferAt];
        }

        // 元素个数守恒：这是这一组算子唯一能出错的地方，也是唯一必须在导入期挡住的地方。
        // Element-count conservation: the only place this group can go wrong, and the only place that has to be stopped at import time.
        if (wanted != have) {
            return new Result(null,
                "'Reshape' 从 " + have + " 个元素变成 " + wanted + " 个 / the reshape changes the element count, which no downstream operator can recover from");
        }
        return new Result(resolved, null);
    }

    private static IReadOnlyList<int> Flatten(IReadOnlyList<int> input, IReadOnlyList<int>? axes) {
        int axis = axes is { Count: > 0 } ? Normalise(axes[0], input.Count) : 1;
        long outer = 1;
        for (int index = 0; index < axis; ++index) {
            outer *= index < input.Count ? input[index] : 1;
        }
        long inner = Elements(input) / (outer == 0 ? 1 : outer);
        return new[] { (int)outer, (int)inner };
    }

    private static Result Transpose(IReadOnlyList<int> input, IReadOnlyList<int>? axes) {
        if (axes is null || axes.Count == 0) {
            // 缺省是逆序，而逆序几乎从来不是作者想要的——所以这不是"随便选一个"，是 ONNX 的规定。
            // The default is reversal, which is almost never what the author meant — so this is not "pick one", it is what ONNX specifies.
            var reversed = new List<int>(input.Count);
            for (int index = input.Count - 1; index >= 0; --index) {
                reversed.Add(input[index]);
            }
            return new Result(reversed, null);
        }
        if (axes.Count != input.Count) {
            return new Result(null, "'Transpose' 的轴数 " + axes.Count + " 与秩 " + input.Count + " 不符 / the permutation length disagrees with the rank");
        }
        var seen = new HashSet<int>();
        var result = new List<int>(axes.Count);
        foreach (int axis in axes) {
            int normalised = Normalise(axis, input.Count);
            if (normalised < 0 || normalised >= input.Count) {
                return new Result(null, "'Transpose' 的轴 " + axis + " 越界 / the axis is out of range");
            }
            if (!seen.Add(normalised)) {
                return new Result(null, "'Transpose' 的轴 " + axis + " 重复 / the permutation repeats an axis, so it is not a permutation");
            }
            result.Add(input[normalised]);
        }
        return new Result(result, null);
    }

    private static Result Squeeze(IReadOnlyList<int> input, IReadOnlyList<int>? axes) {
        var result = new List<int>(input.Count);
        if (axes is null || axes.Count == 0) {
            foreach (int dimension in input) {
                if (dimension != 1) {
                    result.Add(dimension);
                }
            }
            return new Result(result, null);
        }
        var drop = new HashSet<int>();
        foreach (int axis in axes) {
            int normalised = Normalise(axis, input.Count);
            if (normalised < 0 || normalised >= input.Count) {
                return new Result(null, "'Squeeze' 的轴 " + axis + " 越界 / the axis is out of range");
            }
            if (input[normalised] != 1) {
                // 删掉一维长度为 2 的轴会丢掉元素，而模型照常继续——只是每一条后续索引都错位。
                // Dropping an axis of length 2 loses elements and the model carries on — every later index is simply off by one.
                return new Result(null,
                    "'Squeeze' 要删的第 " + axis + " 维长度是 " + input[normalised] + "，不是 1 / the squeezed axis is not of length one, so elements would be lost");
            }
            drop.Add(normalised);
        }
        for (int index = 0; index < input.Count; ++index) {
            if (!drop.Contains(index)) {
                result.Add(input[index]);
            }
        }
        return new Result(result, null);
    }

    private static Result Unsqueeze(IReadOnlyList<int> input, IReadOnlyList<int>? axes) {
        if (axes is null || axes.Count == 0) {
            return new Result(null, "'Unsqueeze' 必须给出轴 / the axis list is required, and there is no sensible default");
        }
        int newRank = input.Count + axes.Count;
        var occupied = new HashSet<int>();
        foreach (int axis in axes) {
            int normalised = Normalise(axis, newRank);
            if (normalised < 0 || normalised >= newRank) {
                return new Result(null, "'Unsqueeze' 的轴 " + axis + " 越界 / the axis is out of range for the new rank");
            }
            if (!occupied.Add(normalised)) {
                return new Result(null, "'Unsqueeze' 的轴 " + axis + " 重复 / the axis is listed twice");
            }
        }
        var result = new List<int>(newRank);
        int source = 0;
        for (int index = 0; index < newRank; ++index) {
            result.Add(occupied.Contains(index) ? 1 : input[source++]);
        }
        return new Result(result, null);
    }

    private static long Elements(IReadOnlyList<int> shape) {
        long total = 1;
        foreach (int dimension in shape) {
            total *= dimension;
        }
        return total;
    }

    /// <summary>把负轴换成从末尾数的位置 / Turn a negative axis into a position counted from the end.</summary>
    /// <remarks>
    /// **负轴加的是<b>秩</b>，不是 `秩 + 1`：** **`-1` 在秩为 3 时指第 2 位，而 `3 + 1 - 1 = 3` 会指到界外——** '
    /// **这类错只在小形状上才看得出来。**
    ///
    /// **A negative axis adds the rank, not rank + 1: -1 with rank 3 means position 2, while 3 + 1 - 1 = 3 points outside — that kind of error only shows on small shapes.**
    /// </remarks>
    private static int Normalise(int axis, int rank) => axis < 0 ? rank + axis : axis;
}