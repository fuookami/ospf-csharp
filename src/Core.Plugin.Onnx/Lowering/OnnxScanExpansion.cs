#nullable enable

using System;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **`Scan` 的展开契约：与 `Loop` 同构，多一个状态量** / The Scan contract: like Loop, plus a carried state.
/// </summary>
/// <remarks>
/// `OnnxSubgraph.lowerScan`。**`Scan` 与 `Loop` 的差别不在"重复"，而在"重复什么带过去"：**
/// **`Loop` 带的是它自己声明的累加器；** **`Scan` 带的是**状态量**（state），而扫描长度来自某个输入的首维，不是节点属性。**
///
/// **那个首维是最容易读错的地方：** **扫描长度不在 `Scan` 的节点上说，却在一个输入张量的形状里。** '
/// **若把它读成"这个输入的总元素数"，长度会大一个量级，而展开后的模型只是"更慢"，不是"报错"。**
///
/// Syncs `lowerScan`. **Scan differs from Loop not in repeating but in what it carries: Loop carries the accumulators it declares, while Scan carries states, and the scan
/// length comes from the leading dimension of one input rather than from a node attribute.**
///
/// **That leading dimension is what gets misread: the length is not stated on the node but hidden in an input's shape. Reading it as "the input's total element count"
/// makes the length an order of magnitude too large, and the unrolled model is merely "slower" rather than wrong-looking.**
/// </remarks>
public static class OnnxScanExpansion
{
    /// <summary>
    /// 一个 `Scan` 节点的声明 / The declaration of one Scan node.
    /// </summary>
    /// <param name="Body">循环体 / The body graph.</param>
    /// <param name="ScanInputShape">扫描输入的形状 / The scan input's shape.</param>
    /// <param name="ScannedInputShape">被逐轮切片输入的形状 / The shape of the input sliced per round.</param>
    /// <param name="States">状态量个数（初值）/ The number of states (initial).</param>
    /// <param name="BodyStatesIn">循环体接收的状态数 / The states the body receives.</param>
    /// <param name="BodyStatesOut">循环体产出的状态数 / The states the body produces.</param>
    /// <param name="FinalStates">最终状态数 / The final state count.</param>
    public sealed record ScanNode(
        OnnxGraph? Body,
        long ScanInputShape,
        long ScannedInputShape,
        int States,
        int BodyStatesIn,
        int BodyStatesOut,
        int FinalStates);

    /// <summary>
    /// 判定结果 / The decision.
    /// </summary>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    /// <param name="ScanLength">扫描长度 / The scan length.</param>
    /// <param name="UnrolledNodes">展开后的节点数 / The node count after unrolling.</param>
    public sealed record Decision(string? Failure, long ScanLength = 0, int UnrolledNodes = 0)
    {
        /// <summary>是否可展开 / Whether it can be unrolled.</summary>
        public bool CanUnroll => Failure is null;
    }

    /// <summary>
    /// 判定一个 `Scan` 能否展开 / Decide whether a Scan can be unrolled.
    /// </summary>
    /// <param name="node">节点 / The node.</param>
    /// <returns>判定结果 / The decision.</returns>
    public static Decision Decide(ScanNode node) {
        if (node.Body is null) {
            return new Decision("'Scan' 缺少循环体 / the scan has no body");
        }

        // 长度来自首维，不是元素总数。
        // The length is the leading dimension, not the element count.
        long length = node.ScanInputShape;
        if (length < 0) {
            return new Decision("'Scan' 的扫描输入首维为负 / the scan input's leading dimension is negative");
        }
        if (length == 0) {
            return new Decision("'Scan' 的扫描输入首维为 0 / the scan input has an empty leading dimension, so there is nothing to scan");
        }
        if (length > OnnxSubgraphPolicy.MaxUnroll) {
            return new Decision(
                "'Scan' 的扫描长度 " + length + " 超出可展开区间 0.." + OnnxSubgraphPolicy.MaxUnroll
                + " / the scan length is outside the unrollable range");
        }
        if (node.ScannedInputShape != length) {
            return new Decision(
                "被逐轮切片的输入首维 " + node.ScannedInputShape + " 与扫描长度 " + length
                + " 不符 / a per-round sliced input disagrees with the scan length");
        }

        if (node.States != node.BodyStatesIn) {
            return new Decision(
                "状态量初值有 " + node.States + " 个，而循环体接收 " + node.BodyStatesIn + " 个 / the initial states and the body's inputs disagree");
        }
        if (node.BodyStatesOut != node.BodyStatesIn) {
            return new Decision(
                "循环体接收 " + node.BodyStatesIn + " 个状态却产出 " + node.BodyStatesOut + " 个 / the body does not produce one state per state input");
        }
        if (node.FinalStates != node.BodyStatesOut) {
            return new Decision(
                "最终状态有 " + node.FinalStates + " 个，而循环体产出 " + node.BodyStatesOut + " 个 / the final states and the body's outputs disagree");
        }

        long nodes = length * System.Math.Max(node.Body.Nodes.Count, 1);
        if (nodes > OnnxLoopExpansion.MaxUnrolledNodes) {
            return new Decision(
                "展开后有 " + nodes + " 个节点，超出预算 " + OnnxLoopExpansion.MaxUnrolledNodes
                + " / the unrolled body exceeds the node budget");
        }
        return new Decision(null, length, (int)nodes);
    }
}