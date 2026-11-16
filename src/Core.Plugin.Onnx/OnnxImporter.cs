#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
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

        OnnxAffineChain.Result chain = OnnxAffineChain.Evaluate(graph, variables);
        if (chain.Failure is not null) {
            return new Imported(null, null, "evaluate", chain.Failure);
        }

        return new Imported(chain.Polynomial, variables, null, null);
    }
}