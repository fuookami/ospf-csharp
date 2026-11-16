#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Graph;

/// <summary>
/// **图的可用性：验，不是排** / Usability is validated, not sorted.
/// </summary>
/// <remarks>
/// `parseGraphBody`。**ONNX 要求节点已按拓扑序排列，所以这里只验。** **排一遍会把次序错乱的图悄悄"修好"，**
/// **于是模型算出的东西与导出时想表达的不一致，而两条路径都"成功"了。**
///
/// Syncs `parseGraphBody`. **ONNX requires topological order, so this only validates. Sorting would quietly "fix" a mis-ordered graph, and what the model computes
/// would differ from what the exporter meant while both paths report success.**
/// </remarks>
public class OnnxGraphValidatorTests
{
    /// <summary>**次序错乱被拒绝，而不是被排好** / A mis-ordered graph is refused, not sorted.</summary>
    /// <remarks>
    /// **这个用例是核心：** **`y` 先于 `x` 被用到，正确做法是报错；** **若实现悄悄做拓扑排序，模型会"跑通"，**
    /// **而它的语义已经与导出者写下的不同。**
    ///
    /// **This is the core case of the batch: `y` is used before it exists and the right response is an error. An implementation that silently topological-sorts would
    /// make the model "work", with semantics the exporter never wrote.**
    /// </remarks>
    [Fact]
    public void AMisOrderedGraph_ShouldBeRefused() {
        var graph = new OnnxGraph(
            "g",
            new[] {
                Node("Mul", "y", "z"),   // 用了还不存在的 y / uses y before it exists
                Node("Add", "x", "y"),
            },
            System.Array.Empty<OnnxTensor>(),
            new[] { ValueInfo("x") },
            new[] { ValueInfo("z") },
            System.Array.Empty<OnnxValueInfo>());

        OnnxGraphValidator.Result result = OnnxGraphValidator.Validate(graph);

        result.IsValid.Should().BeFalse("a graph that uses a tensor before producing it is not topologically ordered");
        result.Failure.Should().Contain("y");
        result.NodeName.Should().Be("n_Mul", "the failing node has to be named, or the reader cannot find it in a 5000-node graph");
    }

    /// <summary>**次序正确的图通过** / A correctly ordered graph passes.</summary>
    [Fact]
    public void AnOrderedGraph_ShouldPass() {
        var graph = new OnnxGraph(
            "g",
            new[] {
                Node("Add", "x", "y"),
                Node("Mul", "y", "z"),
            },
            System.Array.Empty<OnnxTensor>(),
            new[] { ValueInfo("x") },
            new[] { ValueInfo("z") },
            System.Array.Empty<OnnxValueInfo>());

        OnnxGraphValidator.Validate(graph).IsValid.Should().BeTrue();
    }

    /// <summary>**既无节点也无初始化器的图是错的** / A graph with neither nodes nor initialisers is wrong.</summary>
    /// <remarks>
    /// **空图会让降阶产出空约束集，而优化器对空问题照样"求解成功"。** **那种"成功"最难查：** **结果是一个常数，没人在意它从哪来。**
    ///
    /// **An empty graph lowers to an empty constraint set, and the optimiser still "solves" it. That kind of success is the hardest to trace: the answer is a
    /// constant and nobody asks where it came from.**
    /// </remarks>
    [Fact]
    public void AnEmptyGraph_ShouldBeRefused() {
        var graph = new OnnxGraph(
            "g",
            System.Array.Empty<OnnxNode>(),
            System.Array.Empty<OnnxTensor>(),
            System.Array.Empty<OnnxValueInfo>(),
            System.Array.Empty<OnnxValueInfo>(),
            System.Array.Empty<OnnxValueInfo>());

        OnnxGraphValidator.Validate(graph).IsValid.Should().BeFalse();
    }

    /// <summary>**重复的输出名被拒绝** / A duplicated output name is refused.</summary>
    /// <remarks>
    /// **两个节点写同一个输出名时，后写的那次会静默覆盖前一次：** **图仍然能算，** **只是第一个节点的结果被第二个顶掉，**
    /// **而在导出的模型里它们本是两个不同的张量。**
    ///
    /// **When two nodes write the same output name the later one silently overwrites the earlier: the graph still computes, except the first node's result is
    /// replaced, while in the exported model they were two distinct tensors.**
    /// </remarks>
    [Fact]
    public void ADuplicatedOutput_ShouldBeRefused() {
        var graph = new OnnxGraph(
            "g",
            new[] {
                Node("Add", "x", "y"),
                Node("Mul", "x", "y"),
            },
            System.Array.Empty<OnnxTensor>(),
            new[] { ValueInfo("x") },
            new[] { ValueInfo("y") },
            System.Array.Empty<OnnxValueInfo>());

        OnnxGraphValidator.Result result = OnnxGraphValidator.Validate(graph);
        result.IsValid.Should().BeFalse();
        result.NodeName.Should().Be("n_Mul", "the second writer is the one to report");
    }

    /// <summary>**作为初始化器出现的输入不是真实输入** / An input that is also an initialiser is not an input.</summary>
    /// <remarks>
    /// **导出器常把权重同时写进 `input` 与 `initializer`：** **把它当真实输入会让降阶层要求调用方绑定一份权重，**
    /// **而调用方的数据里没有这一项——** **症状是"缺少输入"，原因是权重被当成了数据。**
    ///
    /// **Exporters often list a weight in both `input` and `initializer`: treating it as a real input makes the lowering layer demand a binding the caller has no
    /// data for, and the symptom is "missing input" while the cause is a weight read as data.**
    /// </remarks>
    [Fact]
    public void AnInitialiserInput_ShouldNotRequireData() {
        var graph = new OnnxGraph(
            "g",
            new[] { Node("MatMul", "x", "w", "y") },
            new[] { Tensor("w") },
            new[] { ValueInfo("x"), ValueInfo("w") },
            new[] { ValueInfo("y") },
            System.Array.Empty<OnnxValueInfo>());

        OnnxGraphValidator.Validate(graph).IsValid.Should().BeTrue(
            "w arrives as an initialiser, so the node input resolves without any caller binding");
    }

    /// <summary>**空字符串输入表示"可选且缺省"，不是缺失** / An empty input means "omitted", not missing.</summary>
    [Fact]
    public void AnEmptyInputName_ShouldBeTreatedAsOmitted() {
        var graph = new OnnxGraph(
            "g",
            new[] { Node("Clip", "x", "", "y") },
            System.Array.Empty<OnnxTensor>(),
            new[] { ValueInfo("x") },
            new[] { ValueInfo("y") },
            System.Array.Empty<OnnxValueInfo>());

        OnnxGraphValidator.Validate(graph).IsValid.Should().BeTrue(
            "an empty name marks an omitted optional position and must not be reported as an undefined tensor");
    }

    /// <summary>**未被任何东西定义的图输出被拒绝** / An output nothing defines is refused.</summary>
    [Fact]
    public void AnUndefinedGraphOutput_ShouldBeRefused() {
        var graph = new OnnxGraph(
            "g",
            new[] { Node("Add", "x", "y") },
            System.Array.Empty<OnnxTensor>(),
            new[] { ValueInfo("x") },
            new[] { ValueInfo("z") },
            System.Array.Empty<OnnxValueInfo>());

        OnnxGraphValidator.Result result = OnnxGraphValidator.Validate(graph);
        result.IsValid.Should().BeFalse();
        result.Failure.Should().Contain("z");
    }

    /// <summary>**子图允许引用外层作用域** / A subgraph may reference the enclosing scope.</summary>
    /// <remarks>
    /// **`If`／`Loop` 的子图天然会用到外层的张量，** **按一张独立图去验会把每一个合法子图全部拒掉。** **所以那条开关不是宽容，是必需。**
    ///
    /// **The subgraphs of `If` and `Loop` naturally reference outer tensors, and validating them as standalone graphs would refuse every legal subgraph. That flag is
    /// not leniency, it is required.**
    /// </remarks>
    [Fact]
    public void ASubgraph_ShouldAllowOuterReferences() {
        var subgraph = new OnnxGraph(
            "then",
            new[] { Node("Add", "outer", "folded", "result") },
            System.Array.Empty<OnnxTensor>(),
            System.Array.Empty<OnnxValueInfo>(),
            new[] { ValueInfo("result") },
            System.Array.Empty<OnnxValueInfo>());

        OnnxGraphValidator.Validate(subgraph).IsValid.Should().BeFalse(
            "as a standalone graph the reference to the outer tensor is undefined");

        OnnxGraphValidator.Validate(subgraph, allowExternalInputs: true).IsValid.Should().BeTrue(
            "inside an If/Loop body that reference is exactly how it is supposed to work");
    }

    private static OnnxNode Node(string opType, params string[] names) {
        string input = names.Length > 1 ? names[0] : string.Empty;
        string output = names[^1];
        var outputs = new List<string> { output };
        var inputs = new List<string>();
        for (int index = 0; index < names.Length - 1; ++index) {
            inputs.Add(names[index]);
        }
        _ = input;
        return new OnnxNode(opType, "n_" + opType, inputs, outputs, System.Array.Empty<OnnxAttribute>());
    }

    private static OnnxValueInfo ValueInfo(string name) =>
        new(name, 1, new[] { new OnnxDimension(1, null) });

    private static OnnxTensor Tensor(string name) =>
        new(name, 1, new long[] { 1 }, new[] { 1.0f }, System.Array.Empty<long>(), System.Array.Empty<double>(), null);
}