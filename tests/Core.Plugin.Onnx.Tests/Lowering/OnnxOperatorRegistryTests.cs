#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **前置检查：在建模型之前失败** / Fail before a model is built.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 各算子模块声明的支持集合，汇总成"这张图能不能被表达"的一处判定。
/// **降阶到一半才发现算人不支持，模型已经建了一半——** **而那半个模型是可以求解的，于是"部分导入"会以"成功"的面目出现。**
///
/// Syncs the support sets the baseline declares, gathered into one "can this graph be expressed" judgement. **Finding an unsupported operator half-way through
/// leaves half a model behind, and half a model is solvable, so a partial import presents itself as success.**
/// </remarks>
public class OnnxOperatorRegistryTests
{
    /// <summary>**未知算子被拒绝并点出节点** / An unknown operator is refused and the node named.</summary>
    [Fact]
    public void AnUnknownOperator_ShouldBeRefused() {
        OnnxOperatorRegistry.PreflightResult result = OnnxOperatorRegistry.Preflight(
            Graph(Node("NonMaxSuppression", "boxes", "count")),
            new OnnxImportOptions());

        result.IsValid.Should().BeFalse("NonMaxSuppression is not something this plugin lowers");
        result.NodeName.Should().Be("n_NonMaxSuppression", "the offending node has to be named");
        result.Failure.Should().Contain("NonMaxSuppression");
    }

    /// <summary>**线性模式下 `Mul` 被拒绝，二次模式下通过** / Mul is refused in linear mode and allowed in quadratic.</summary>
    /// <remarks>
    /// **阶数是硬约束：** **`Mul` 会把两个线性式乘成二次。** **若线性模式下"近似处理"它，模型算的完全是另一件事——**
    /// **而那样同样会求解成功。**
    ///
    /// **The degree is a hard constraint: Mul multiplies two linear expressions into a quadratic one. Approximating it in linear mode computes something else entirely —
    /// and that also solves successfully.**
    /// </remarks>
    [Fact]
    public void Mul_ShouldRespectTheImportMode() {
        OnnxGraph graph = Graph(Node("Mul", "a", "b"));

        OnnxOperatorRegistry.PreflightResult linear = OnnxOperatorRegistry.Preflight(graph, new OnnxImportOptions(OnnxImportMode.Linear));
        linear.IsValid.Should().BeFalse("a quadratic cannot be expressed with linear limits");
        linear.Failure.Should().Contain("2").And.Contain("1");

        OnnxOperatorRegistry.Preflight(graph, new OnnxImportOptions(OnnxImportMode.Quadratic)).IsValid
            .Should().BeTrue("quadratic mode exists precisely for this");
    }

    /// <summary>**需要界这件事由 `OnnxInputBounds` 报告，不在这里** / Bounds are reported by InputBounds, not here.</summary>
    /// <remarks>
    /// **两个阶段都报会让同一个问题出现两条消息、两个阶段名，** **而调用方会以为那是两件事。** '
    /// **这条判据钉住分工：本阶段只管"这个算子支不支持、阶数够不够"。**
    ///
    /// **Both stages reporting gives one problem two messages and two stage names, and the caller takes them for two problems.** **The criterion pins the division:
    /// this stage answers only "is the operator supported and is the degree enough".**
    /// </remarks>
    [Fact]
    public void Bounds_ShouldNotBeReportedHere() {
        OnnxOperatorRegistry.PreflightResult result = OnnxOperatorRegistry.Preflight(Graph(Node("Relu", "x", "y")), new OnnxImportOptions());

        result.IsValid.Should().BeTrue("Relu is supported at linear degree; whether x has a range is the next stage's question");
    }

    /// <summary>**加法不需要区间** / Addition needs no bounds.</summary>
    [Fact]
    public void Addition_ShouldNotNeedBounds() {
        OnnxOperatorRegistry.Preflight(Graph(Node("Add", "a", "b")), new OnnxImportOptions()).IsValid.Should().BeTrue();
        OnnxOperatorRegistry.Preflight(Graph(Node("MatMul", "a", "b")), new OnnxImportOptions(OnnxImportMode.Quadratic)).IsValid
            .Should().BeTrue("MatMul needs degree but not bounds");
    }

    /// <summary>**报第一个不支持的，不是一次报全部** / Report the first, not all.</summary>
    /// <remarks>
    /// **收集全部听起来更友好，实际会让人一次改一批、每次都要重建；** **先报第一个，改一个再看下一个，收敛更快。**
    ///
    /// **Collecting everything sounds friendlier but makes the reader change a batch and rebuild each time; reporting the first, fixing one, looking again converges
    /// faster.**
    /// </remarks>
    [Fact]
    public void ThePreflight_ShouldReportTheFirstFailure() {
        OnnxGraph graph = Graph(
            Node("Add", "a", "b"),
            Node("NonMaxSuppression", "c", "d"),
            Node("AlsoUnknown", "e", "f"));

        OnnxOperatorRegistry.PreflightResult result = OnnxOperatorRegistry.Preflight(graph, new OnnxImportOptions());
        result.NodeName.Should().Be("n_NonMaxSuppression", "the first unsupported node in graph order is the one to report");
    }

    /// <summary>**选项本身无效时先报选项** / An invalid option set is reported before any operator.</summary>
    [Fact]
    public void AnInvalidOptionSet_ShouldBeReportedFirst() {
        var broken = new OnnxImportOptions(inputBounds: new Dictionary<string, OnnxBounds> { ["x"] = new(5.0, 1.0) });
        OnnxOperatorRegistry.PreflightResult result = OnnxOperatorRegistry.Preflight(Graph(Node("Add", "a", "b")), broken);

        result.IsValid.Should().BeFalse("a broken option set cannot express anything, whatever the graph contains");
        result.NodeName.Should().BeNull("no node is at fault here");
    }

    /// <summary>**登记表里的阶数都不超过三次** / No registered degree exceeds cubic.</summary>
    /// <remarks>
    /// **超过三次需要四次及以上的求解器能力，而本插件声明的模式只到三次：** **表里出现一个四阶算子会让"三次模式"永远不可用，**
    /// **而这种不一致只在特定模型上才暴露。**
    ///
    /// **Anything past cubic needs quartic solver capability, while this plugin's modes stop at cubic: a degree-4 entry would make cubic mode permanently unusable, and
    /// that inconsistency only surfaces on particular models.**
    /// </remarks>
    [Fact]
    public void TheRegistry_ShouldStayWithinCubic() {
        foreach (KeyValuePair<string, OnnxOperatorRegistry.Requirement> entry in OnnxOperatorRegistry.Requirements) {
            entry.Value.MaxDegree.Should().BeInRange(1, 3, entry.Key + " is registered with a degree the modes cannot provide");
            entry.Value.OpType.Should().Be(entry.Key, "a mismatch between the key and the record makes the table lie about itself");
        }
    }

    private static OnnxNode Node(string opType, params string[] names) {
        var inputs = new List<string>();
        for (int index = 0; index < names.Length - 1; ++index) {
            inputs.Add(names[index]);
        }
        return new OnnxNode(opType, "n_" + opType, inputs, new[] { names[^1] }, Array.Empty<OnnxAttribute>());
    }

    private static OnnxGraph Graph(params OnnxNode[] nodes) =>
        new("g", nodes, Array.Empty<OnnxTensor>(), Array.Empty<OnnxValueInfo>(), Array.Empty<OnnxValueInfo>(), Array.Empty<OnnxValueInfo>());
}