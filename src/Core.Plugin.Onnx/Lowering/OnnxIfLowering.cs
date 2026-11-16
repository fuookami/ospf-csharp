#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **把 `If` 真的展开：选分支 → 压作用域 → 求值 → 弹作用域** / Actually unroll an If: choose, push, evaluate, pop.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `lowerIf` 的落地形态，把三件东西接起来：**`OnnxIfExpansion` 选分支、`OnnxLoweringContext` 提供作用域、
/// `OnnxGraphEvaluator` 走分支体。**
///
/// **"子图"在这里不再是一个特殊概念：** **它只是"在子上下文里运行的一张普通图"。** **于是 `Loop`／`Scan` 的展开会复用同一条接线，只多一层"把上一轮结果写回种子"。**
///
/// **一遍失败也必须把作用域弹掉：** **否则失败之后外层会多出半张分支体的表，** **而后续节点可能意外命中一个本不属于它们的名字——**
/// **那种错在数值上表现为"某一项莫名多出来"。**
///
/// Syncs the concrete form of `lowerIf`, wiring three things together: `OnnxIfExpansion` chooses, `OnnxLoweringContext` supplies the scope, `OnnxGraphEvaluator` walks
/// the body.
///
/// **A subgraph stops being a special concept here: it is just an ordinary graph run in a child context.** **Loop and Scan will reuse the same wiring with one extra step —
/// writing the previous round back into the seed.**
///
/// **A failed pass still has to pop the scope: otherwise the outer scope keeps half of a body's table, later nodes may hit names that never belonged to them, and that shows
/// up numerically as "one term appeared out of nowhere".**
/// </remarks>
public static class OnnxIfLowering
{
    /// <summary>
    /// 展开结果 / The unrolling result.
    /// </summary>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    /// <param name="Outputs">节点输出名到绑定的映射 / The mapping from the node's output names to bindings.</param>
    public sealed record Result(string? Failure, IReadOnlyDictionary<string, OnnxLoweringContext.Binding>? Outputs)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 在上下文里展开一个 `If` / Unroll one If inside a context.
    /// </summary>
    /// <param name="node">节点 / The node.</param>
    /// <param name="context">外层上下文 / The outer context.</param>
    /// <param name="constants">常量名到标量值的映射 / The mapping from constant names to scalar values.</param>
    /// <returns>展开结果 / The result.</returns>
    public static Result Lower(
        OnnxIfExpansion.IfNode node,
        OnnxLoweringContext context,
        IReadOnlyDictionary<string, double>? constants = null) {
        OnnxIfExpansion.Expansion expansion = OnnxIfExpansion.Expand(node);
        if (!expansion.IsOk) {
            return new Result(expansion.Failure, null);
        }

        context.PushScope();
        IReadOnlyList<string>? declaredNames = null;
        OnnxGraph? chosenBody = null;
        IReadOnlyDictionary<string, OnnxLoweringContext.Binding>? bodyOutputs = null;
        try {
            chosenBody = expansion.Chosen!;
            OnnxGraphEvaluator.Result evaluated = OnnxGraphEvaluator.Evaluate(chosenBody, context, constants);
            if (!evaluated.IsOk) {
                return new Result(
                    evaluated.NodeName is null ? evaluated.Failure : evaluated.Failure + "（节点 " + evaluated.NodeName + "）",
                    null);
            }
            bodyOutputs = evaluated.Outputs;
            declaredNames = expansion.OutputNames!;
        }
        finally {
            // 无论成败都弹：失败后留着半张表，比失败本身更难查。
            // Pop either way: half a table left behind after a failure is harder to trace than the failure itself.
            context.TryPopScope(out _);
        }

        // 绑定发生在弹出之后、在外层：节点声明的输出本来就属于外层作用域。
        // 绑定到内层会让外层看不见它——那不是"作用域严格"，是错的语义。
        //
        // The bindings happen after the pop, in the outer scope: the node's declared outputs belong there by definition. Binding them inside would hide them from the outer
        // scope — that is not strict scoping, it is the wrong semantics.
        var outputs = new Dictionary<string, OnnxLoweringContext.Binding>(StringComparer.Ordinal);
        for (int index = 0; index < declaredNames!.Count; ++index) {
            string declared = declaredNames[index];
            OnnxLoweringContext.Binding binding = Matching(bodyOutputs!, chosenBody!.Outputs[index].Name);
            outputs[declared] = binding;
            if (!context.TryRebind(declared, binding.Expression, binding.Shape, out string? bindFailure)) {
                return new Result(bindFailure, null);
            }
        }
        return new Result(null, outputs);
    }

    private static OnnxLoweringContext.Binding Matching(IReadOnlyDictionary<string, OnnxLoweringContext.Binding> outputs, string name) {
        if (outputs.TryGetValue(name, out OnnxLoweringContext.Binding? binding) && binding is not null) {
            return binding;
        }
        throw new InvalidOperationException("分支体输出 '" + name + "' 缺失 / the body output is missing");
    }
}