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
/// **输入界：判定与使用分开** / Input bounds: the decision kept apart from the use.
/// </summary>
/// <remarks>
/// 界缺失是配置问题，界用错是代码问题——**两种错误的消息不该混在一起**，而这一层的作用就是把前者从降阶里摘出来。
///
/// A missing bound is a configuration problem and a misused bound a code problem — **their messages should not be mixed**, and this layer exists to take the former out
/// of the lowering.
/// </remarks>
public class OnnxInputBoundsTests
{
    /// <summary>**只解析需要界的算子的输入** / Only inputs of operators that need bounds are resolved.</summary>
    /// <remarks>
    /// **要求全图每个输入都有界会把一条纯线性链挡在门外，** **而那明明不需要任何近似。**
    ///
    /// **Demanding bounds for every input would turn away a purely linear chain that needs no approximation at all.**
    /// </remarks>
    [Fact]
    public void OnlyOperatorsThatNeedBounds_ShouldDemandThem() {
        OnnxInputBounds.Resolution linear = OnnxInputBounds.Resolve(Graph(Node("Add", "a", "b")), new OnnxImportOptions());
        linear.IsComplete.Should().BeTrue("a pure linear chain needs no bounds");
        linear.Resolved.Should().BeEmpty();
        linear.Missing.Should().BeEmpty();

        OnnxInputBounds.Resolution activation = OnnxInputBounds.Resolve(Graph(Node("Tanh", "x", "y")), new OnnxImportOptions());
        activation.IsComplete.Should().BeFalse();
        activation.Missing.Should().Equal(new[] { "x" },
            "the input of the approximated operator is the one that needs a range");
    }

    /// <summary>**缺失的界被点名，而不是只说"缺少界"** / A missing bound is named.</summary>
    /// <remarks>
    /// **真实模型有几十个输入：** **只说"缺少界"等于让人逐个试。** **点名之后，配置错误与代码错误的边界才清楚。**
    ///
    /// **A real model has dozens of inputs: saying only "bounds are missing" leaves the reader trying them one by one. Naming them is what draws the line between a
    /// configuration mistake and a code mistake.**
    /// </remarks>
    [Fact]
    public void AMissingBound_ShouldBeNamed() {
        var options = new OnnxImportOptions(inputBounds: new Dictionary<string, OnnxBounds> {
            ["x"] = new(-1.0, 1.0),
        });

        OnnxInputBounds.Resolution result = OnnxInputBounds.Resolve(Graph(Node("Relu", "x", "y"), Node("Sigmoid", "z", "w")), options);

        result.Missing.Should().Equal(new[] { "z" }, "x is covered, z is not");
        result.IsComplete.Should().BeFalse();
    }

    /// <summary>**超过分段上限的界按缺失处理并说明原因** / A bound past the piecewise limit is treated as missing, with the reason.</summary>
    /// <remarks>
    /// **与分段近似共享同一条上限，不是巧合而是定义：** **`±1e6` 之外，"有限区间"这个说法已经不再描述任何可近似的东西。**
    ///
    /// **Sharing the piecewise limit is not a coincidence but the definition: past ±1e6 the phrase "finite interval" no longer describes anything approximable.**
    /// </remarks>
    [Fact]
    public void ABoundPastTheLimit_ShouldBeRefused() {
        var options = new OnnxImportOptions(inputBounds: new Dictionary<string, OnnxBounds> {
            ["x"] = new(-1e7, 1e7),
        });

        OnnxInputBounds.Resolution result = OnnxInputBounds.Resolve(Graph(Node("Tanh", "x", "y")), options);

        result.IsComplete.Should().BeFalse();
        result.Failure.Should().Contain("x");
        result.Failure.Should().Contain("x".Length > 0 ? "上限" : "limit", "the message has to say why, not only that");
    }

    /// <summary>**恰好等于上限的界可用** / A bound exactly at the limit is usable.</summary>
    [Fact]
    public void ABoundExactlyAtTheLimit_ShouldBeUsable() {
        var options = new OnnxImportOptions(inputBounds: new Dictionary<string, OnnxBounds> {
            ["x"] = new(-1e6, 1e6),
        });

        OnnxInputBounds.Resolution result = OnnxInputBounds.Resolve(Graph(Node("Tanh", "x", "y")), options);
        result.IsComplete.Should().BeTrue();
        result.Resolved["x"].Should().Be(new OnnxBounds(-1e6, 1e6));
    }

    /// <summary>**倒挂的界在解析阶段就失败** / An inverted bound fails during resolution.</summary>
    [Fact]
    public void AnInvertedBound_ShouldFailDuringResolution() {
        var options = new OnnxImportOptions(inputBounds: new Dictionary<string, OnnxBounds> {
            ["x"] = new(5.0, 1.0),
        });

        OnnxInputBounds.Resolution result = OnnxInputBounds.Resolve(Graph(Node("Relu", "x", "y")), options);
        result.IsComplete.Should().BeFalse();
        result.Failure.Should().NotBeNull("the option set is unusable, so nothing downstream should even try");
    }

    /// <summary>**同一个输入被多个算子用到时只解析一次** / An input used by several operators is resolved once.</summary>
    [Fact]
    public void ASharedInput_ShouldResolveOnce() {
        var options = new OnnxImportOptions(inputBounds: new Dictionary<string, OnnxBounds> {
            ["x"] = new(-1.0, 1.0),
        });

        OnnxInputBounds.Resolution result = OnnxInputBounds.Resolve(
            Graph(Node("Tanh", "x", "y"), Node("Sigmoid", "x", "z")),
            options);

        result.IsComplete.Should().BeTrue();
        result.Resolved.Should().HaveCount(1, "the same input does not become two entries");
        result.Missing.Should().BeEmpty();
    }

    /// <summary>**未知算子不在这一层报错** / An unknown operator is not reported here.</summary>
    /// <remarks>
    /// **前置检查已经报过它，再报一次会让同一个问题出现两条不同措辞的消息，** **而读者会以为那是两个问题。**
    ///
    /// **The preflight already reports it; reporting again gives one problem two differently worded messages, and the reader takes them for two problems.**
    /// </remarks>
    [Fact]
    public void AnUnknownOperator_ShouldBeLeftToThePreflight() {
        OnnxInputBounds.Resolution result = OnnxInputBounds.Resolve(Graph(Node("NonMaxSuppression", "a", "b")), new OnnxImportOptions());

        result.Failure.Should().BeNull("this layer decides about bounds, not about operator support");
        result.Missing.Should().BeEmpty();
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