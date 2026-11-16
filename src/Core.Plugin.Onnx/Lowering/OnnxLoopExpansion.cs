#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **`Loop` 的展开契约：迭代变量、累加器与规模守卫** / The Loop unrolling contract: iteration variable, accumulators, size guard.
/// </summary>
/// <remarks>
/// `OnnxSubgraph.lowerLoop`。**`If` 只做"选"，`Loop` 要做"重复并且把上一轮的结果带进下一轮"——**
/// **于是它多出三件 `If` 没有的东西：迭代变量的绑定、循环携带值（累加器）的初值与更新、以及展开后的规模。**
///
/// **这里只做契约判定，不做展开本身：** **展开需要子图体的递归降阶（把子图节点真正走一遍求值），** **那一步依赖多数算子的降阶路径，还没到。**
/// **把契约与展开分开，是为了让"次数对不对、累加器齐不齐"能有自己的证据。**
///
/// Syncs `lowerLoop`. **If only chooses; Loop repeats and carries the previous round's result into the next, so it has three things If does not: binding the iteration
/// variable, the initial value and update of each carried value, and the size after unrolling.**
///
/// ** decides the contract, not the unrolling: unrolling needs the recursive lowering of the body, which depends on most operators' lowering paths and is not
/// here yet. Keeping the contract apart lets "is the count right, are the accumulators complete" have evidence of its own.**
/// </remarks>
public static class OnnxLoopExpansion
{
    /// <summary>
    /// 展开规模预算（展开后允许的最大节点数）/ The unroll size budget, the most nodes allowed after unrolling.
    /// </summary>
    /// <remarks>
    /// **次数上限（256）不够用：** **一个 200 次、每轮 50 个节点的循环展开后是一万个节点，** **它通过了次数检查，而模型已经大到没人能看。**
    /// **所以规模要单独守，而不是只守次数。**
    ///
    /// **The trip limit of 256 is not enough: a 200-iteration loop with 50 nodes per round unrolls to ten thousand nodes, passes the trip check, and is already too large
    /// to inspect. Size has to be guarded on its own, not only the trip count.**
    /// </remarks>
    public const int MaxUnrolledNodes = 10_000;

    /// <summary>
    /// 一个 `Loop` 节点的声明 / The declaration of one Loop node.
    /// </summary>
    /// <param name="TripCount">最大迭代次数 / The maximum trip count.</param>
    /// <param name="LimitsAreConstant">次数与终止条件是否都是常量 / Whether the count and termination are constant.</param>
    /// <param name="Body">循环体 / The body graph.</param>
    /// <param name="CarriedInputs">循环携带值的个数（初值）/ The number of carried values (initial).</param>
    /// <param name="CarriedOutputs">循环携带值的个数（更新后）/ The number of carried values (updated).</param>
    /// <param name="BodyCarriedInputs">循环体接收的携带值个数 / The carried values the body receives.</param>
    /// <param name="BodyCarriedOutputs">循环体产出的携带值个数 / The carried values the body produces.</param>
    public sealed record LoopNode(
        long TripCount,
        bool LimitsAreConstant,
        OnnxGraph? Body,
        int CarriedInputs,
        int CarriedOutputs,
        int BodyCarriedInputs,
        int BodyCarriedOutputs);

    /// <summary>
    /// 判定结果 / The decision.
    /// </summary>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    /// <param name="UnrolledNodes">展开后的节点数 / The node count after unrolling.</param>
    public sealed record Decision(string? Failure, int UnrolledNodes = 0)
    {
        /// <summary>是否可展开 / Whether it can be unrolled.</summary>
        public bool CanUnroll => Failure is null;
    }

    /// <summary>
    /// 判定一个 `Loop` 能否展开 / Decide whether a Loop can be unrolled.
    /// </summary>
    /// <param name="node">节点 / The node.</param>
    /// <returns>判定结果 / The decision.</returns>
    public static Decision Decide(LoopNode node) {
        if (!node.LimitsAreConstant) {
            return new Decision(
                "'Loop' 的迭代次数或终止条件不是常量 / the trip count or termination is not constant, so the loop cannot be unrolled at import time");
        }
        if (node.TripCount < 0) {
            return new Decision("'Loop' 的迭代次数为负 / the trip count is negative");
        }
        if (node.TripCount > OnnxSubgraphPolicy.MaxUnroll) {
            return new Decision(
                "'Loop' 的迭代次数 " + node.TripCount + " 超出可展开区间 0.." + OnnxSubgraphPolicy.MaxUnroll
                + " / the trip count is outside the unrollable range");
        }
        if (node.Body is null) {
            return new Decision("'Loop' 缺少循环体 / the loop has no body");
        }

        // 携带值必须一一对上：初值个数、循环体接收个数、循环体产出个数、最终个数，四者相等。
        // 少一个就会在某一轮之后丢失上一轮的结果，而展开后的模型照常可解——只是少了一条递推。
        //
        // The carried values must match on all four counts: initial, body-received, body-produced and final. One missing drops a round's result, and the unrolled model
        // still solves — it just lost a recurrence.
        if (node.CarriedInputs != node.BodyCarriedInputs) {
            return new Decision(
                "循环携带值的初值有 " + node.CarriedInputs + " 个，而循环体接收 " + node.BodyCarriedInputs
                + " 个 / the carried initial values and the body's inputs disagree");
        }
        if (node.BodyCarriedOutputs != node.BodyCarriedInputs) {
            return new Decision(
                "循环体接收 " + node.BodyCarriedInputs + " 个携带值却产出 " + node.BodyCarriedOutputs
                + " 个 / the body does not produce one output per carried input");
        }
        if (node.CarriedOutputs != node.BodyCarriedOutputs) {
            return new Decision(
                "循环最终携带值有 " + node.CarriedOutputs + " 个，而循环体产出 " + node.BodyCarriedOutputs
                + " 个 / the final carried values and the body's outputs disagree");
        }

        // 规模守卫：次数上限通过之后仍可能太大。
        // The size guard: passing the trip limit still leaves a model that is far too large.
        long nodes = (long)node.TripCount * System.Math.Max(node.Body.Nodes.Count, 1);
        if (nodes > MaxUnrolledNodes) {
            return new Decision(
                "展开后有 " + nodes + " 个节点，超出预算 " + MaxUnrolledNodes
                + " / the unrolled body exceeds the node budget");
        }
        return new Decision(null, (int)nodes);
    }
}