#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **`Loop` 真的展开：同一条接线，多一层"把上一轮结果写回种子"** / Loop unrolled: the same wiring plus one step.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `lowerLoop` 落地形态。**`If` 只跑一次体，`Loop` 跑 `trip` 次，而每一轮的输入是上一轮的输出——** '
/// **这就是它与 `If` 的全部差别，接线完全一样：压作用域、灌种子、走体、弹作用域。**
///
/// **每一轮都必须用干净的作用域：** **若复用上一层，第二轮会看见第一轮的中间结果，** **而那种错不会报错——只是某一项用了上一轮的值，'
/// **而它看起来像一条正常的递推。**
///
/// Syncs the concrete `lowerLoop`. **If runs the body once; Loop runs it `trip` times with each round's inputs being the previous round's outputs — that is the whole
/// difference, and the wiring is identical: push, seed, walk, pop.**
///
/// **Every round needs a clean scope: reusing one lets the second round see the first round's intermediates, and that throws nothing — it just means some term used the
/// previous round's value, which looks like a legitimate recurrence.**
/// </remarks>
public static class OnnxLoopLowering
{
    /// <summary>
    /// 展开结果 / The unrolling result.
    /// </summary>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    /// <param name="FinalStates">最终的循环携带值 / The final carried values.</param>
    public sealed record Result(string? Failure, IReadOnlyList<OnnxLoweringContext.Binding>? FinalStates)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 在上下文里展开一个 `Loop` / Unroll one Loop inside a context.
    /// </summary>
    /// <param name="node">节点 / The node.</param>
    /// <param name="context">外层上下文 / The outer context.</param>
    /// <param name="initialStates">携带值的初值 / The initial carried values.</param>
    /// <param name="bodyInputNames">循环体接收的名字（迭代变量在最前，其后是携带值）/ The names the body receives, iteration variable first.</param>
    /// <param name="outputNames">节点声明的输出名 / The node's declared output names.</param>
    /// <param name="constants">常量表 / The constant table.</param>
    /// <returns>展开结果 / The result.</returns>
    public static Result Lower(
        OnnxLoopExpansion.LoopNode node,
        OnnxLoweringContext context,
        IReadOnlyList<OnnxLoweringContext.Binding> initialStates,
        IReadOnlyList<string> bodyInputNames,
        IReadOnlyList<string> outputNames,
        IReadOnlyDictionary<string, double>? constants = null) {
        OnnxLoopExpansion.Decision decision = OnnxLoopExpansion.Decide(node);
        if (!decision.CanUnroll) {
            return new Result(decision.Failure, null);
        }
        if (initialStates.Count != node.CarriedInputs) {
            return new Result(
                "给了 " + initialStates.Count + " 个初值，而节点声明 " + node.CarriedInputs + " 个 / the initial values disagree with the declaration", null);
        }
        if (bodyInputNames.Count != node.CarriedInputs + 1) {
            return new Result(
                "循环体接收名字有 " + bodyInputNames.Count + " 个，应当是迭代变量加 " + node.CarriedInputs + " 个携带值 / the body input names disagree with the declaration",
                null);
        }
        if (outputNames.Count != node.CarriedOutputs) {
            return new Result(
                "输出名有 " + outputNames.Count + " 个，而节点声明 " + node.CarriedOutputs + " 个 / the output names disagree with the declaration", null);
        }

        var carried = new List<OnnxLoweringContext.Binding>(initialStates);
        for (int round = 0; round < node.TripCount; ++round) {
            // 每轮一个干净的作用域：复用会让第二轮看见第一轮的中间结果。
            // A clean scope per round: reuse would let the second round see the first round's intermediates.
            context.PushScope();
            OnnxGraphEvaluator.Result? evaluated = null;
            try {
                var iteration = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(round));
                if (!context.TryBind(bodyInputNames[0], iteration, Array.Empty<int>(), out string? iterationFailure)) {
                    return new Result(iterationFailure, null);
                }
                // ponytail: 就地更新的循环体（输出名等于输入名）会被"同层重名拒绝"挡下，而 ONNX 允许这种写法
                // （引用该名字处之后的含义是新值）。要支持它需要让 TryBind 支持同层覆盖并定义"之后"的语义；
                // 目前的循环体必须用不同的输入名与输出名。升级路径：给上下文加 TryRebind，只在体作用域内允许覆盖。
                //
                // ponytail: an in-place body (output name equal to input name) is refused by the duplicate rule, while ONNX allows it (later references mean the new
                // value). Supporting it needs TryBind to allow same-scope overwrite plus a definition of "later"; bodies must use distinct input and output names for now.
                // Upgrade path: add TryRebind to the context, allowing overwrite inside a body scope only.
                for (int index = 0; index < carried.Count; ++index) {
                    if (!context.TryBind(bodyInputNames[index + 1], carried[index].Expression, carried[index].Shape, out string? seedFailure)) {
                        return new Result(seedFailure, null);
                    }
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

            // 上一轮的输出就是下一轮的输入：这一句是 Loop 与 If 的全部差别。
            // The previous round's outputs are the next round's inputs: this line is the whole difference between Loop and If.
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