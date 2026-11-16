#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx;

/// <summary>
/// **导入一个 ONNX 模型的单一入口** / The single entry point for importing an ONNX model.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxFunctionalModel` 里"从字节到可用模型"那条主干。**它把前 17 批的东西按固定次序串起来：**
/// **解析 → 图校验 → 前置检查 → 界解析 → 仿射求值 → 模型组装。**
///
/// **次序本身就是契约：** **同一份坏模型可能同时触发三处错误，报告哪一处决定了调用方能否一次改对。** '
/// **所以判据不只验"会不会失败"，还验"先报哪一处"。**
///
/// Syncs the "bytes to usable model" trunk of `OnnxFunctionalModel`. **It strings the previous seventeen batches together in a fixed order: parse → graph
/// validation → preflight → bound resolution → affine evaluation → model assembly.**
///
/// **The order is itself the contract: one bad model can trip three checks at once, and which one is reported decides whether the caller can fix it in one pass.**
/// **The criteria therefore check not only that it fails, but which failure comes first.**
/// </remarks>
public static class OnnxImporter
{
    /// <summary>
    /// 导入结果 / The import result.
    /// </summary>
    /// <param name="Polynomial">求值出的仿射多项式 / The evaluated affine polynomial.</param>
    /// <param name="Variables">输入变量 / The input variables.</param>
    /// <param name="Stage">失败发生在哪一步 / The stage the failure happened in.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Imported(
        LinearPolynomial<Flt64>? Polynomial,
        IReadOnlyDictionary<string, RealVar>? Variables,
        string? Stage,
        string? Failure)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 导入 / Import.
    /// </summary>
    /// <param name="bytes">模型字节 / The model bytes.</param>
    /// <param name="options">导入选项 / The import options.</param>
    /// <returns>导入结果 / The result.</returns>
    public static Imported Import(byte[] bytes, OnnxImportOptions options) {
        if (!options.Validate(out string? optionFailure)) {
            return new Imported(null, null, "options", optionFailure);
        }

        OnnxModel model;
        try {
            model = OnnxModelParser.Parse(bytes);
        }
        catch (FormatException error) {
            return new Imported(null, null, "parse", "模型字节无法解析 / the bytes are not a parseable model: " + error.Message);
        }

        if (model.Graph is not OnnxGraph graph) {
            return new Imported(null, null, "parse", "模型没有计算图 / the model carries no graph");
        }

        OnnxGraphValidator.Result valid = OnnxGraphValidator.Validate(graph);
        if (!valid.IsValid) {
            return new Imported(null, null, "graph", valid.Failure);
        }

        OnnxOperatorRegistry.PreflightResult preflight = OnnxOperatorRegistry.Preflight(graph, options);
        if (!preflight.IsValid) {
            return new Imported(null, null, "operators", preflight.Failure);
        }

        OnnxInputBounds.Resolution bounds = OnnxInputBounds.Resolve(graph, options);
        if (!bounds.IsComplete) {
            string detail = bounds.Failure
                            ?? ("缺少输入区间：" + string.Join(", ", bounds.Missing) + " / missing input bounds");
            return new Imported(null, null, "bounds", detail);
        }

        // 变量来自图的输入，不是"需要界的那些输入"：一条纯仿射链没有任何算子要求界，
        // 而它照样有 x 和 y——按界建变量会让它取不到任何输入。
        //
        // Variables come from the graph's inputs, not from "the inputs that need bounds": a purely affine chain has no operator demanding a bound and still has x and y,
        // and building variables from the bounds would leave it with none.
        var initializerNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxTensor initializer in graph.Initializers) {
            if (initializer.Name is not null) {
                initializerNames.Add(initializer.Name);
            }
        }
        var variables = new Dictionary<string, RealVar>(StringComparer.Ordinal);
        foreach (OnnxValueInfo input in graph.Inputs) {
            if (initializerNames.Contains(input.Name)) {
                continue;
            }
            variables[input.Name] = new RealVar(input.Name);
        }

        // 带控制流的图走逐节点求值：`If` 的分支体要递归走一遍，而仿射链只看节点、不看子图。
        // 两条路都收敛到同一个 `LinearPolynomial`，所以返回类型不变。
        //
        // A graph with control flow goes through node-by-node evaluation: an If body has to be walked recursively, while the affine chain looks at nodes and not at
        // subgraphs. Both paths end in the same LinearPolynomial, so the return type is unchanged.
        if (NeedsEvaluator(graph)) {
            return ImportThroughEvaluator(graph, variables);
        }

        OnnxAffineChain.Result chain = OnnxAffineChain.Evaluate(graph, variables);
        if (chain.Failure is not null) {
            return new Imported(null, null, "evaluate", chain.Failure);
        }

        return new Imported(chain.Polynomial, variables, null, null);
    }

    private static IReadOnlyList<int> DeclaredShape(OnnxGraph graph, string name) {
        foreach (OnnxValueInfo input in graph.Inputs) {
            if (input.Name != name) {
                continue;
            }
            var shape = new List<int>(input.Shape.Count);
            foreach (OnnxDimension dimension in input.Shape) {
                if (dimension.Value is not long value) {
                    // 动态维在导入期没有确定值；留空比猜一个值安全。
                    // A dynamic dimension has no value at import time; leaving it empty is safer than guessing one.
                    return Array.Empty<int>();
                }
                shape.Add((int)value);
            }
            return shape;
        }
        return Array.Empty<int>();
    }

    /// <summary>
    /// 这张图是否必须走逐节点求值 / Whether this graph has to go through node-by-node evaluation.
    /// </summary>
    /// <remarks>
    /// **判断依据是"仿射链不认识这些算子"，不是"这些算子叫控制流"：** **`Reshape` 不是控制流，**
    /// **但它同样需要逐节点走一遍才能换形状。** **按名字列举会漏掉新接进来的算子，按"谁认识"判断则不会。**
    ///
    /// **The test is "the affine chain does not know these operators", not "these are called control flow": Reshape is not control flow, yet it also needs a node-by-node
    /// walk to change the shape.** **Enumerating names misses each newly wired operator; asking "who knows it" does not.**
    /// </remarks>
    private static bool NeedsEvaluator(OnnxGraph graph) {
        foreach (OnnxNode node in graph.Nodes) {
            if (OnnxShapeTransform.Operators.Contains(node.OpType)) {
                return true;
            }
            if (node.OpType is "If" or "Loop" or "Scan") {
                return true;
            }
        }
        return false;
    }

    private static Imported ImportThroughEvaluator(OnnxGraph graph, Dictionary<string, RealVar> variables) {
        var context = new OnnxLoweringContext();
        foreach (KeyValuePair<string, RealVar> entry in variables) {
            var polynomial = new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, entry.Value) },
                Flt64.Zero);
            // 形状要用图里声明的：`Reshape`／`Squeeze` 的守恒检查没有形状就无从谈起，
            // 而"空形状"会让任何多元素的重塑都判成元素个数不符。
            //
            // The shape comes from what the graph declares: Reshape and Squeeze cannot check conservation without one, and an empty shape makes every multi-element reshape look
            // like a mismatch.
            context.TryBind(entry.Key, polynomial, DeclaredShape(graph, entry.Key), out _);
        }

        // 常量初值必须交给求值器：`If` 的条件常常是一个 initializer，
        // 它既不是变量也不是节点输出，只作为常量存在。
        //
        // Initialisers have to reach the evaluator: an If condition is often an initializer, which is neither a variable nor a node output and exists only as a constant.
        var constants = new Dictionary<string, double>(StringComparer.Ordinal);
        var arrays = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        var intArrays = new Dictionary<string, IReadOnlyList<long>>(StringComparer.Ordinal);
        foreach (OnnxTensor initializer in graph.Initializers) {
            if (initializer.Name is null) {
                continue;
            }
            if (initializer.FloatData is { Count: > 0 } floats) {
                constants[initializer.Name] = floats[0];
                // 整个数组也要留下：`Scan` 的长度与每轮切片都来自它，只留第一个元素会让扫描长度恒为 1。
                // The whole array has to survive as well: Scan takes both its length and its slices from it, and keeping only the first element makes every scan length one.
                var values = new List<double>(floats.Count);
                foreach (float value in floats) {
                    values.Add(value);
                }
                arrays[initializer.Name] = values;
            }
            else if (initializer.Int64Data is { Count: > 0 } integers) {
                constants[initializer.Name] = integers[0];
                // 整数数组同样整份留下：`Reshape` 的目标形状、`Transpose` 的置换轴都可能是它们。
                // Integer arrays survive whole as well: a Reshape target or a Transpose permutation can be one.
                intArrays[initializer.Name] = new List<long>(integers);
            }
        }

        OnnxGraphEvaluator.Result evaluated = OnnxGraphEvaluator.Evaluate(graph, context, constants, arrays, intArrays);
        if (!evaluated.IsOk) {
            return new Imported(
                null,
                variables,
                "evaluate",
                evaluated.NodeName is null ? evaluated.Failure : evaluated.Failure + "（节点 " + evaluated.NodeName + "）");
        }
        if (graph.Outputs.Count == 0) {
            return new Imported(null, variables, "evaluate", "图没有输出 / the graph declares no output");
        }
        return new Imported(evaluated.Outputs![graph.Outputs[0].Name].Expression, variables, null, null);
    }
}