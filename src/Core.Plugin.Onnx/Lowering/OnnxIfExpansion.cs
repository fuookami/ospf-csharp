#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **`If` 的展开：条件为常量时选一个分支体** / Unrolling an `If`: with a constant condition, one branch body is chosen.
/// </summary>
/// <remarks>
/// `OnnxSubgraph.lowerIf`。**这是三类控制流里最浅的一类，因为它不需要迭代变量，只需要"选"。**
///
/// **但它有一条容易漏的约束：** **两个分支必须产出同样的东西（同样的个数、同样的类型）。** **ONNX 允许它们内部完全不同，**
/// **而外部只看得见结果——** **若两边的输出个数不同，选中的是哪一个都会让下游按错的个数去接，** **而错位之后每个维度都跟着错。**
///
/// Syncs `lowerIf`. **It is the shallowest of the three control-flow forms because it needs no iteration variable, only a choice.**
///
/// **It carries one easily-missed constraint: both branches must produce the same thing — the same count and the same types. ONNX allows them to differ internally while
/// only the result is visible, and if their output counts differ, whichever is chosen leaves the downstream wiring to the wrong arity, with every dimension shifting
/// after that.**
/// </remarks>
public static class OnnxIfExpansion
{
    /// <summary>
    /// 展开结果 / The unrolling result.
    /// </summary>
    /// <param name="Chosen">被选中的分支体 / The chosen branch body.</param>
    /// <param name="OutputNames">分支体产出的名字（已按外部输出名对齐）/ The produced names, aligned to the outer output names.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Expansion(OnnxGraph? Chosen, IReadOnlyList<string>? OutputNames, string? Failure)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 一个 `If` 节点的分支 / The branches of one `If` node.
    /// </summary>
    /// <param name="Condition">条件值 / The condition value.</param>
    /// <param name="ConditionIsConstant">条件是否来自常量 / Whether the condition comes from a constant.</param>
    /// <param name="Then">真分支体 / The true branch body.</param>
    /// <param name="Else">假分支体 / The false branch body.</param>
    /// <param name="Outputs">节点声明的输出名 / The node's declared output names.</param>
    public sealed record IfNode(
        double Condition,
        bool ConditionIsConstant,
        OnnxGraph? Then,
        OnnxGraph? Else,
        IReadOnlyList<string> Outputs);

    /// <summary>
    /// 展开一个 `If` / Unroll one `If`.
    /// </summary>
    /// <param name="node">节点 / The node.</param>
    /// <returns>展开结果 / The result.</returns>
    public static Expansion Expand(IfNode node) {
        if (!node.ConditionIsConstant) {
            return new Expansion(
                null,
                null,
                "'If' 的条件不是常量 / the condition is not constant, so nothing can be chosen at import time");
        }
        if (node.Then is null || node.Else is null) {
            return new Expansion(null, null, "'If' 缺少一个分支 / one of the branches is missing");
        }
        if (node.Outputs.Count == 0) {
            return new Expansion(null, null, "'If' 没有声明输出 / the node declares no output");
        }

        // 两个分支必须产出同样多的结果：选哪个都行，但下游按节点声明的个数去接。
        // Both branches must produce the same number of results: either may be chosen, but the downstream wires to the count the node declares.
        if (node.Then.Outputs.Count != node.Else.Outputs.Count) {
            return new Expansion(
                null,
                null,
                "两个分支的输出个数不同（" + node.Then.Outputs.Count + " 与 " + node.Else.Outputs.Count
                + "）/ the branches produce different output counts");
        }
        if (node.Then.Outputs.Count != node.Outputs.Count) {
            return new Expansion(
                null,
                null,
                "分支输出个数 " + node.Then.Outputs.Count + " 与节点声明的输出个数 " + node.Outputs.Count
                + " 不符 / the branch produces a different number of outputs than the node declares");
        }

        bool takeThen = node.Condition != 0.0;
        OnnxGraph chosen = takeThen ? node.Then : node.Else;

        // 分支体自己的输出名与节点声明的输出名通常不同：ONNX 子图按位置对齐，不按名字。
        // The body's own output names usually differ from the node's declared ones: ONNX subgraphs align by position, not by name.
        var aligned = new List<string>(chosen.Outputs.Count);
        for (int index = 0; index < node.Outputs.Count; ++index) {
            aligned.Add(node.Outputs[index]);
        }
        return new Expansion(chosen, aligned, null);
    }

    /// <summary>
    /// 嵌套深度检查 / Check the nesting depth.
    /// </summary>
    /// <param name="depth">嵌套层数 / The nesting depth.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>是否可展开 / Whether it can be unrolled.</returns>
    /// <remarks>
    /// **每一层 `If` 都只选一个分支，所以展开不放大规模——** **但太深的嵌套说明模型作者用了控制流代替计算，** **而那种模型展开后在数值上与原意对齐的成本很高。**
    /// 上限与 `Loop` 的展开上限共用同一个常量，理由一致：**守规模，不守性能。**
    ///
    /// **Each If level chooses a single branch, so unrolling does not multiply the size — but a very deep nesting says the author used control flow in place of
    /// arithmetic, and such a model is expensive to align numerically after unrolling.** The limit shares the constant used for Loop unrolling, for the same reason:
    /// **it guards size, not performance.**
    /// </remarks>
    public static bool CheckDepth(int depth, out string? failure) {
        if (depth < 0) {
            failure = "嵌套深度为负 / the nesting depth is negative";
            return false;
        }
        if (depth > OnnxSubgraphPolicy.MaxUnroll) {
            failure = "嵌套深度 " + depth + " 超出上限 " + OnnxSubgraphPolicy.MaxUnroll + " / the nesting depth exceeds the limit";
            return false;
        }
        failure = null;
        return true;
    }
}