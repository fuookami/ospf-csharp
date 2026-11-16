#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Graph;

/// <summary>
/// **一张图的可用性判定** / Whether a graph is usable at all.
/// </summary>
/// <remarks>
/// 里 `parseGraphBody` 那一半。**ONNX 要求节点已按拓扑序排列，所以这里做的是"验"而不是"排"：**
/// **排一遍会把一张次序错乱的图悄悄"修好"，于是模型跑出来的东西与导出时想表达的并不一致。**
///
/// Syncs the `parseGraphBody` half of. **ONNX requires the nodes to be topologically ordered, so this validates rather than sorts:**
/// **sorting would quietly "fix" a mis-ordered graph, and what the model computes would not be what the exporter meant.**
/// </remarks>
public static class OnnxGraphValidator
{
    /// <summary>
    /// 校验结果 / The validation result.
    /// </summary>
    /// <param name="Failure">失败原因，通过时为 null / The failure reason, null when valid.</param>
    /// <param name="NodeName">出错节点 / The offending node.</param>
    public sealed record Result(string? Failure, string? NodeName = null)
    {
        /// <summary>是否通过 / Whether it passed.</summary>
        public bool IsValid => Failure is null;
    }

    /// <summary>
    /// 校验一张图 / Validate one graph.
    /// </summary>
    /// <param name="graph">图 / The graph.</param>
    /// <param name="allowExternalInputs">是否允许引用外层作用域（子图）/ Whether references to the enclosing scope are allowed (a subgraph).</param>
    /// <returns>校验结果 / The result.</returns>
    public static Result Validate(OnnxGraph graph, bool allowExternalInputs = false) {
        if (graph.Nodes.Count == 0 && graph.Initializers.Count == 0) {
            return new Result("图既无节点也无初始化器 / the graph has neither nodes nor initialisers");
        }

        var initializerNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxTensor initializer in graph.Initializers) {
            if (initializer.Name is null || initializer.Name.Length == 0) {
                return new Result("初始化器缺少名称 / an initialiser has no name");
            }
            if (!initializerNames.Add(initializer.Name)) {
                return new Result("初始化器 '" + initializer.Name + "' 重复定义 / duplicated initialiser");
            }
        }

        var available = new HashSet<string>(initializerNames, StringComparer.Ordinal);
        foreach (OnnxValueInfo input in graph.Inputs) {
            // 作为初始化器出现的输入不是真实输入——它是权重，不是数据入口。
            // An input that also appears as an initialiser is not a real input: it is a weight, not a data entry point.
            if (!initializerNames.Contains(input.Name)) {
                available.Add(input.Name);
            }
        }

        foreach (OnnxNode node in graph.Nodes) {
            foreach (string input in node.Inputs) {
                // 空字符串在 ONNX 里表示"这个位置可选且缺省"，不是缺失的输入。
                // An empty string means "this optional position is omitted", not a missing input.
                if (input.Length == 0) {
                    continue;
                }
                if (available.Contains(input)) {
                    continue;
                }
                if (allowExternalInputs) {
                    continue;
                }
                return new Result(
                    "节点输入 '" + input + "' 在当前位置不可得（要求图按拓扑序排列）/ node input is unavailable at this position, so the graph is not topologically ordered",
                    node.Name);
            }
            foreach (string output in node.Outputs) {
                if (output.Length == 0) {
                    continue;
                }
                if (!available.Add(output)) {
                    return new Result("输出张量 '" + output + "' 重复定义 / duplicated output tensor", node.Name);
                }
            }
        }

        if (!allowExternalInputs) {
            foreach (OnnxValueInfo output in graph.Outputs) {
                if (!available.Contains(output.Name)) {
                    return new Result("图输出 '" + output.Name + "' 未被任何节点或常量定义 / the graph output is not defined by anything");
                }
            }
        }

        return new Result(null);
    }
}