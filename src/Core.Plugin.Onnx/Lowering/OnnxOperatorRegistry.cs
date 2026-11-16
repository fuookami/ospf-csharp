#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **算子登记表与导入前的覆盖性检查** / The operator registry and the pre-import coverage check.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 里各算子模块声明的支持集合（`OnnxActivation.OPERATORS`、形状算子、聚合等），**把它们汇总成一处"这张图能不能被表达"的判定。**
/// **为什么要在降阶之前单独做一次：** **降阶到一半才发现某个算子不支持，模型已经建了一半——** **而那半个模型是可以求解的，**
/// **于是"部分导入"会以"成功"的面目出现。前置检查让这件事在**建模型之前**就失败。
///
/// Syncs the support sets the baseline declares across its operator modules, gathering them into one "can this graph be expressed" judgement. **Why a separate pass
/// before lowering: discovering an unsupported operator half-way through leaves half a model behind, and half a model is solvable — so a partial import would
/// present itself as success.** The preflight makes it fail before any model is built.
/// </remarks>
public static class OnnxOperatorRegistry
{
    /// <summary>
    /// 一个算子的降阶要求 / What lowering one operator requires.
    /// </summary>
    /// <param name="OpType">算子类型 / The operator type.</param>
    /// <param name="MaxDegree">允许的最高阶 / The highest degree it may produce.</param>
    /// <param name="NeedsBounds">是否需要有限输入范围 / Whether it needs finite input bounds.</param>
    public sealed record Requirement(string OpType, int MaxDegree, bool NeedsBounds);

    /// <summary>
    /// 支持表 / The support table.
    /// </summary>
    /// <remarks>
    /// **阶数不是"越大越好"的标签而是硬约束：** **`Mul` 会把两个线性式乘成二次，** **所以线性模式下它不可用——**
    /// **而一个"近似处理"的 `Mul` 会让模型算的完全是另一件事。**
    ///
    /// **The degree is a hard constraint rather than a label: `Mul` multiplies two linear expressions into a quadratic one, so it is unusable in linear mode — and an
    /// "approximated" Mul would compute something else entirely.**
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, Requirement> Requirements =
        new Dictionary<string, Requirement>(StringComparer.Ordinal) {
            // 逐元素线性 / elementwise linear
            // 控制流：一阶（分支体与循环体目前只表达仿射），且不需要算子级的界——
            // 界由输入张量自己提供，不因为"这里有控制流"而变得必要。
            //
            // Control flow: first degree (bodies currently express affine forms only) and no operator-level bound requirement — bounds come from the input tensors and do
            // not become necessary merely because control flow is present.
            // 形状算子：一阶、不改数值。`Transpose` 登记为可用，而"落到按位置消费者上时拒绝"由求值器负责——
            // 前置检查只看"这个算子认识不认识"，不看"这一步在当前位置可不可观察"。
            //
            // Shape operators: first degree, no value changes. Transpose is registered as known, and "refused when it reaches a positional reader" belongs to the evaluator — preflight
            // only answers "do we know this operator", not "is this step observable at this position".
            ["Reshape"] = new("Reshape", 1, false),
            ["Flatten"] = new("Flatten", 1, false),
            ["Squeeze"] = new("Squeeze", 1, false),
            ["Unsqueeze"] = new("Unsqueeze", 1, false),
            ["Transpose"] = new("Transpose", 1, false),
            ["Concat"] = new("Concat", 1, false),
            ["If"] = new("If", 1, false),
            ["Loop"] = new("Loop", 1, false),
            ["Scan"] = new("Scan", 1, false),
            ["Add"] = new("Add", 1, false),
            ["Sub"] = new("Sub", 1, false),
            ["Sum"] = new("Sum", 1, false),
            ["Neg"] = new("Neg", 1, false),
            ["Identity"] = new("Identity", 1, false),
            ["Cast"] = new("Cast", 1, false),
            ["Constant"] = new("Constant", 1, false),

            // 乘法：需要二次 / multiplication needs quadratic
            ["Mul"] = new("Mul", 2, false),
            ["MatMul"] = new("MatMul", 2, false),

            // 激活：四个精确、四个近似 / four exact activations, four approximated
            ["Relu"] = new("Relu", 1, true),
            ["Clip"] = new("Clip", 1, true),
            ["LeakyRelu"] = new("LeakyRelu", 1, true),
            ["HardSigmoid"] = new("HardSigmoid", 1, true),
            ["Sigmoid"] = new("Sigmoid", 1, true),
            ["Tanh"] = new("Tanh", 1, true),
            ["Softplus"] = new("Softplus", 1, true),
            ["Elu"] = new("Elu", 1, true),

            // 形状 / shape
            ["Reshape"] = new("Reshape", 1, false),
            ["Flatten"] = new("Flatten", 1, false),
            ["Transpose"] = new("Transpose", 1, false),
            ["Concat"] = new("Concat", 1, false),
            ["Split"] = new("Split", 1, false),
            ["Gather"] = new("Gather", 1, false),
            ["Shape"] = new("Shape", 1, false),
            ["ReduceSum"] = new("ReduceSum", 1, false),
            ["ReduceMean"] = new("ReduceMean", 1, false),
            ["Softmax"] = new("Softmax", 1, false),
            ["Unsqueeze"] = new("Unsqueeze", 1, false),
            ["Squeeze"] = new("Squeeze", 1, false),
            ["Slice"] = new("Slice", 1, false),
            ["Pad"] = new("Pad", 1, false),
        };

    /// <summary>
    /// 前置检查结果 / The preflight result.
    /// </summary>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    /// <param name="NodeName">出错的节点 / The offending node.</param>
    public sealed record PreflightResult(string? Failure, string? NodeName = null)
    {
        /// <summary>是否通过 / Whether it passed.</summary>
        public bool IsValid => Failure is null;
    }

    /// <summary>
    /// 在降阶之前检查整张图能否被给定选项表达 / Check the whole graph against the options before lowering.
    /// </summary>
    /// <param name="graph">图 / The graph.</param>
    /// <param name="options">导入选项 / The import options.</param>
    /// <returns>检查结果 / The result.</returns>
    /// <remarks>
    /// **遍历顺序是图本身的顺序，报的是第一个不支持的位置：** **把全部不支持的算子收集起来一起报，听起来更友好，**
    /// **实际会让人去改一批东西而每次都要重建——** **先报第一个，改一个再看下一个，收敛更快。**
    ///
    /// **The walk follows the graph's own order and reports the first unsupported position: collecting every unsupported operator sounds friendlier, but it makes
    /// the reader change a batch of things and rebuild each time — reporting the first, fixing one, looking again converges faster.**
    /// </remarks>
    public static PreflightResult Preflight(OnnxGraph graph, OnnxImportOptions options) {
        if (!options.Validate(out string? optionFailure)) {
            return new PreflightResult(optionFailure);
        }

        foreach (OnnxNode node in graph.Nodes) {
            if (!Requirements.TryGetValue(node.OpType, out Requirement? requirement)) {
                return new PreflightResult(
                    "不支持的算子 '" + node.OpType + "' / unsupported operator",
                    node.Name);
            }
            if (requirement.MaxDegree > options.MaxDegree) {
                return new PreflightResult(
                    "算子 '" + node.OpType + "' 需要 " + requirement.MaxDegree + " 阶，而当前导入模式只允许 " + options.MaxDegree
                    + " 阶 / the operator needs a higher degree than the import mode allows",
                    node.Name);
            }
            // 界缺失由 OnnxInputBounds 统一报告：界是配置问题、算子支持是代码问题，两者不该在同一处混报。
            // Missing bounds are reported by OnnxInputBounds: a bound is a configuration problem and operator support a code problem, and one place should not report both.
        }
        return new PreflightResult(null);
    }
}