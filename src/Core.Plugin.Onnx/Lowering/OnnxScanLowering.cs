#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **`Scan` 真的展开：长度取自首维，每轮喂一个切片** / Scan unrolled: length from the leading dimension, one slice per round.
/// </summary>
/// <remarks>
/// `lowerScan` 的落地形态。**接线与 `Loop` 一样，多出来的只有"每轮多喂一个切片"：**
/// **`Scan` 的每一轮都从一个被逐轮切片的输入里取第 `i` 片，** **而那个片在展开后就是第 `i` 轮的一个独立绑定。**
///
/// **切片个数必须等于扫描长度：** **少给会让最后几轮拿到空绑定，多给会让多出来的切片被静默丢弃——** **两者都不会报错，只是模型的最后几轮或前几轮算的是别的东西。**
///
/// The concrete `lowerScan`. **The wiring matches Loop; the one addition is a slice per round: Scan reads round `i`'s slice from a per-round-sliced input, and after
/// unrolling that slice is simply an independent binding for round `i`.**
///
/// **The slice count must equal the scan length: too few leaves the last rounds with nothing, too many silently discards the extra — neither throws, the model just
/// computes something else over its first or last rounds.**
/// </remarks>
public static class OnnxScanLowering
{
    /// <summary>
    /// 展开结果 / The unrolling result.
    /// </summary>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    /// <param name="FinalStates">最终状态 / The final states.</param>
    public sealed record Result(string? Failure, IReadOnlyList<OnnxLoweringContext.Binding>? FinalStates)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 用逐元素表示驱动展开：非常量扫描输入也能逐轮切片 / Unroll driven by the per-element representation, so a non-constant scan input can be sliced round by round.
    /// </summary>
    /// <param name="node">节点 / The node.</param>
    /// <param name="context">外层上下文 / The outer context.</param>
    /// <param name="initialStates">状态初值 / The initial states.</param>
    /// <param name="scanInput">扫描输入的逐元素表示 / The scan input's per-element representation.</param>
    /// <param name="bodyInputNames">循环体接收的名字（状态在前，其后是切片）/ The body names, states first and then the slice.</param>
    /// <param name="outputNames">节点声明的输出名 / The node's declared output names.</param>
    /// <param name="constants">常量表 / The constant table.</param>
    /// <returns>展开结果 / The result.</returns>
    /// <remarks>
    /// **这一入口存在的理由，是"长度与切片都来自那个张量本身"：** **长度取首维，第 `i` 片取 `SliceLeading(i)`。** '
    /// **切片之后每轮的输入可以含变量——** **这正是"运行期切片"与"常量数组切片"的差别：** **前者切的是表达式，后者切的是数。**
    ///
    /// **The reason this entry exists is that both the length and the slices come from that tensor: the length is the leading dimension and round i
    /// takes SliceLeading(i).** **A sliced round input may carry variables — that is exactly the difference between runtime slicing and slicing a constant array: one cuts expressions,
    /// the other cuts numbers.**
    /// </remarks>
    public static Result LowerWithElements(
        OnnxScanExpansion.ScanNode node,
        OnnxLoweringContext context,
        IReadOnlyList<OnnxLoweringContext.Binding> initialStates,
        OnnxElementList scanInput,
        IReadOnlyList<string> bodyInputNames,
        IReadOnlyList<string> outputNames,
        IReadOnlyDictionary<string, double>? constants = null) {
        if (scanInput is null) {
            return new Result("扫描输入为空 / the scan input is null", null);
        }
        int length = scanInput.Shape.Count == 0 ? 0 : scanInput.Shape[0];
        if (length != node.ScanInputShape) {
            return new Result(
                "扫描输入的首维 " + length + " 与节点声明的扫描长度 " + node.ScanInputShape
                + " 不符 / the leading dimension disagrees with the declared scan length, so some round would read the wrong slice", null);
        }

        var slices = new List<OnnxLoweringContext.Binding>(length);
        for (int round = 0; round < length; ++round) {
            OnnxElementList.Result slice = scanInput.SliceLeading(round);
            if (!slice.IsOk) {
                return new Result(slice.Failure, null);
            }
            // 切片若不是单元素，说明循环体要的是张量而不是一个位置——那一层还没做，明确拒绝。
            // A slice with more than one element means the body wants a tensor rather than one position; that layer is not here, so refuse explicitly.
            if (slice.Value!.Count != 1) {
                return new Result(
                    "第 " + round + " 片有 " + slice.Value.Count
                    + " 个元素，而这一层只能把单元素切片喂给循环体 / the slice holds several elements while this layer can only feed a single-element slice", null);
            }
            slices.Add(new OnnxLoweringContext.Binding(slice.Value.Elements[0], slice.Value.Shape));
        }

        return Lower(node, context, initialStates, slices, bodyInputNames, outputNames, constants);
    }

    /// <summary>
    /// 在上下文里展开一个 `Scan` / Unroll one Scan inside a context.
    /// </summary>
    /// <param name="node">节点 / The node.</param>
    /// <param name="context">外层上下文 / The outer context.</param>
    /// <param name="initialStates">状态初值 / The initial states.</param>
    /// <param name="slices">每轮的切片（个数必须等于扫描长度）/ The per-round slices, one per round.</param>
    /// <param name="bodyInputNames">循环体接收的名字（状态在前，其后是切片）/ The names the body receives, states first, then the slice.</param>
    /// <param name="outputNames">节点声明的输出名 / The node's declared output names.</param>
    /// <param name="constants">常量表 / The constant table.</param>
    /// <returns>展开结果 / The result.</returns>
    public static Result Lower(
        OnnxScanExpansion.ScanNode node,
        OnnxLoweringContext context,
        IReadOnlyList<OnnxLoweringContext.Binding> initialStates,
        IReadOnlyList<OnnxLoweringContext.Binding> slices,
        IReadOnlyList<string> bodyInputNames,
        IReadOnlyList<string> outputNames,
        IReadOnlyDictionary<string, double>? constants = null) {
        OnnxScanExpansion.Decision decision = OnnxScanExpansion.Decide(node);
        if (!decision.CanUnroll) {
            return new Result(decision.Failure, null);
        }
        if (initialStates.Count != node.States) {
            return new Result(
                "给了 " + initialStates.Count + " 个状态初值，而节点声明 " + node.States + " 个 / the initial states disagree with the declaration", null);
        }
        if (slices.Count != decision.ScanLength) {
            return new Result(
                "切片有 " + slices.Count + " 个，而扫描长度是 " + decision.ScanLength
                + " / the slice count disagrees with the scan length, so some round would run on the wrong input", null);
        }
        if (bodyInputNames.Count != node.States + 1) {
            return new Result(
                "循环体接收名字有 " + bodyInputNames.Count + " 个，应当是 " + node.States + " 个状态加一个切片 / the body input names disagree with the declaration",
                null);
        }
        if (outputNames.Count != node.FinalStates) {
            return new Result(
                "输出名有 " + outputNames.Count + " 个，而节点声明 " + node.FinalStates + " 个 / the output names disagree with the declaration", null);
        }

        var carried = new List<OnnxLoweringContext.Binding>(initialStates);
        for (int round = 0; round < decision.ScanLength; ++round) {
            context.PushScope();
            OnnxGraphEvaluator.Result? evaluated = null;
            try {
                for (int index = 0; index < carried.Count; ++index) {
                    if (!context.TryBind(bodyInputNames[index], carried[index].Expression, carried[index].Shape, out string? seedFailure)) {
                        return new Result(seedFailure, null);
                    }
                }
                // 本轮的那一片：它就是"扫描"与"循环"的唯一区别。
                // This round's slice: it is the only thing that separates a scan from a loop.
                OnnxLoweringContext.Binding slice = slices[round];
                if (!context.TryBind(bodyInputNames[^1], slice.Expression, slice.Shape, out string? sliceFailure)) {
                    return new Result(sliceFailure, null);
                }
                evaluated = OnnxGraphEvaluator.Evaluate(node.Body!, context, constants);
            }
            finally {
                context.TryPopScope(out _);
            }

            if (evaluated is null || !evaluated.IsOk) {
                string detail = evaluated?.Failure ?? "循环体求值未发生 / the body was never evaluated";
                return new Result(
                    evaluated?.NodeName is null ? detail : detail + "（第 " + round + " 轮，节点 " + evaluated.NodeName + "）",
                    null);
            }

            var next = new List<OnnxLoweringContext.Binding>(node.Body!.Outputs.Count);
            for (int index = 0; index < node.Body.Outputs.Count; ++index) {
                next.Add(evaluated.Outputs![node.Body.Outputs[index].Name]);
            }
            carried = next;
        }

        var finals = new List<OnnxLoweringContext.Binding>(outputNames.Count);
        for (int index = 0; index < outputNames.Count; ++index) {
            OnnxLoweringContext.Binding binding = carried[index];
            finals.Add(binding);
            if (!context.TryRebind(outputNames[index], binding.Expression, binding.Shape, out string? bindFailure)) {
                return new Result(bindFailure, null);
            }
        }
        return new Result(null, finals);
    }
}