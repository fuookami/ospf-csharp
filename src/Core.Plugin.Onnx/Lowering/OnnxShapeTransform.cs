#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **只改形状、不改表达式的那组算子** / The operators that change the shape and leave the expression alone.
/// </summary>
/// <remarks>
/// `OnnxShapeOperators` 在逐节点求值里的那一半。**这一组算子的共同点是"数值一个都不动"：** '
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
    /// **会改变元素顺序的算子** / The operators that change the element order.
    /// </summary>
    /// <remarks>
    /// **`reshape`／`flatten` 原样带着 `input.elements`，而 `transpose` 会按坐标重排元素。**
    /// **这一层每个绑定只有一个标量表达式加一个形状，没有逐元素列表——** **所以"重排"在这里无处可写。**
    ///
    /// **不写"无处可写"的后果是静默的：** **形状换了、表达式没换，** **下游若按位置去接（`MatMul`、`Concat`），**
    /// **拿到的就是错位的元素，而模型照常求解。** **所以顺序敏感的算子在多元素张量上必须拒绝，而不是装作支持。**
    ///
    /// reshape and flatten carry input.elements unchanged while transpose permutes them by coordinate. **Each binding here holds one scalar expression
    /// plus a shape and no element list, so the permutation has nowhere to be written down.**
    ///
    /// **The consequence of not writing that down is silent: the shape changes and the expression does not, and a downstream operator that reads positionally (MatMul, Concat)
    /// receives misaligned elements while the model still solves.** **So an order-sensitive operator on a multi-element tensor has to be refused, not pretended.**
    /// </remarks>
    public static readonly IReadOnlySet<string> OrderChanging = new HashSet<string>(StringComparer.Ordinal) {
        "Transpose",
    };

    /// <summary>
    /// **按位置消费张量的算子** / The operators that consume a tensor positionally.
    /// **其中 `Conv`／`MaxPool`／`AveragePool` 只用于判断"下游会不会按位置读"，本身并未实现；** '
    /// **这里也没有实现它们。**
    ///
    /// **Of these, Conv, MaxPool and AveragePool serve only to decide whether a downstream reads positionally; they are not implemented here.**
    /// </summary>
    /// <remarks>
    /// **重排只有在被这些算子读到的时候才是可见的：** **它们按"第几个元素"取值，** **而逐元素的加法、激活、`Reshape` 都不看位置。**
    /// **所以"要不要拒绝 `Transpose`"不是一个全局问题，而是"这个重排有没有落到一个按位置读的算子上"。**
    ///
    /// **A permutation is only visible where one of these operators reads it: they take "the n-th element", while elementwise addition, activations and Reshape do not look at
    /// position.** **So "should Transpose be refused" is not a global question but "did this permutation land on a positional reader".**
    /// </remarks>
    public static readonly IReadOnlySet<string> PositionalConsumers = new HashSet<string>(StringComparer.Ordinal) {
        "Concat", "Split", "Gather", "MatMul", "Gemm", "Conv", "MaxPool", "AveragePool",
    };

    /// <summary>
    /// 判断一个算子在该张量上是否可安全落地 / Whether an operator can be applied safely to this tensor.
    /// </summary>
    /// <param name="opType">算子类型 / The operator type.</param>
    /// <param name="shape">输入形状 / The input shape.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>是否可落地 / Whether it can be applied.</returns>
    /// <remarks>
    /// **单元素张量上顺序没有意义，所以放过；** **多元素上顺序有意义，而这一层表达不了它——拒绝。** '
    /// **放过单元素的理由是它真的无害，不是因为"反正没人检查"。**
    ///
    /// **On a one-element tensor order carries no meaning, so it passes; on a multi-element tensor order does carry meaning and this layer cannot express it, so it is refused.**
    /// **The one-element pass exists because it is genuinely harmless, not because nobody checks.**
    /// </remarks>
    public static bool CanApply(string opType, IReadOnlyList<int> shape, out string? failure) {
        if (!OrderChanging.Contains(opType)) {
            failure = null;
            return true;
        }
        long elements = 1;
        foreach (int dimension in shape) {
            elements *= dimension;
        }
        if (elements <= 1) {
            failure = null;
            return true;
        }
        failure = "'" + opType + "' 会重排 " + elements
                  + " 个元素，而这一层每个绑定只有一个标量表达式、没有逐元素列表 / the operator permutes elements while this layer holds one scalar expression per binding "
                  + "and no element list, so the permutation cannot be written down（重排无处可写，而下游按位置读时会静默错位 / the reorder is silently lost and a positional downstream reads the wrong element）";
        return false;
    }

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
    /// **负轴加的是**秩**，不是 `秩 + 1`：** **`-1` 在秩为 3 时指第 2 位，而 `3 + 1 - 1 = 3` 会指到界外——** '
    /// **这类错只在小形状上才看得出来。**
    ///
    /// **A negative axis adds the rank, not rank + 1: -1 with rank 3 means position 2, while 3 + 1 - 1 = 3 points outside — that kind of error only shows on small shapes.**
    /// </remarks>
    private static int Normalise(int axis, int rank) => axis < 0 ? rank + axis : axis;
}