#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests;

/// <summary>
/// **单一入口：步骤次序本身就是契约** / The single entry point, where the order is the contract.
/// </summary>
/// <remarks>
/// `OnnxFunctionalModel` 主干：**解析 → 图校验 → 前置检查 → 界解析 → 仿射求值。**
/// **同一份坏模型可能同时触发三处错误，报告哪一处决定了调用方能否一次改对**——所以判据不只验"会不会失败"，还验"先报哪一处"。
///
/// Syncs the trunk: **parse → graph validation → preflight → bounds → affine evaluation.** **One bad model can trip three checks at once, and which one is reported
/// decides whether the caller can fix it in one pass**, so the criteria check which failure comes first.
/// </remarks>
public class OnnxImporterTests
{
    /// <summary>**字节不合法时报 `parse`，不进入任何后续步骤** / Unparseable bytes fail at parse.</summary>
    [Fact]
    public void BrokenBytes_ShouldFailAtParse() {
        OnnxImporter.Imported result = OnnxImporter.Import(new byte[] { 0x0A, 0x7F, 0x01 }, new OnnxImportOptions());

        result.IsOk.Should().BeFalse();
        result.Stage.Should().Be("parse", "nothing downstream can be asked about a model that did not parse");
    }

    /// <summary>**没有计算图的模型在 `parse` 阶段就被拒绝** / A model without a graph is refused at parse.</summary>
    [Fact]
    public void AModelWithoutAGraph_ShouldFailAtParse() {
        OnnxImporter.Imported result = OnnxImporter.Import(
            VarintField(1, 8), // 只有 ir_version
            new OnnxImportOptions());

        result.Stage.Should().Be("parse");
        result.Failure.Should().Contain("计算图");
    }

    /// <summary>**空图在 `graph` 阶段失败，而不是走到算子检查** / An empty graph fails at graph validation.</summary>
    /// <remarks>
    /// **次序在这里有意义：** **空图既"没有算子"也"没有输出"，** **若先报算子就会把一个"模型是空的"说成"算子不支持"，**
    /// **而后者会让人去改算子列表。**
    ///
    /// **The order matters here: an empty graph has neither operators nor outputs, and reporting operators first would describe "the model is empty" as "the operator is
    /// unsupported" — which sends the reader off to change an operator list.**
    /// </remarks>
    [Fact]
    public void AnEmptyGraph_ShouldFailAtGraph() {
        OnnxImporter.Imported result = OnnxImporter.Import(ModelWithGraph(StringField(2, "g")), new OnnxImportOptions());

        result.Stage.Should().Be("graph");
        result.Failure.Should().Contain("既无节点也无初始化器");
    }

    /// <summary>**不支持的算子在 `operators` 阶段失败** / An unsupported operator fails at the operator stage.</summary>
    [Fact]
    public void AnUnsupportedOperator_ShouldFailAtOperators() {
        byte[] node = Concat(StringField(4, "NonMaxSuppression"), StringField(2, "y"));
        byte[] graph = Concat(
            BytesField(1, node),
            StringField(2, "g"),
            BytesField(12, ValueInfo("y")));

        OnnxImporter.Imported result = OnnxImporter.Import(ModelWithGraph(graph), new OnnxImportOptions());

        result.Stage.Should().Be("operators");
        result.Failure.Should().Contain("NonMaxSuppression");
    }

    /// <summary>**需要界却缺界在 `bounds` 阶段失败** / A missing bound fails at the bounds stage.</summary>
    [Fact]
    public void AMissingBound_ShouldFailAtBounds() {
        byte[] node = Concat(StringField(1, "x"), StringField(2, "y"), StringField(4, "Tanh"));
        byte[] graph = Concat(
            BytesField(1, node),
            StringField(2, "g"),
            BytesField(11, ValueInfo("x")),
            BytesField(12, ValueInfo("y")));

        OnnxImporter.Imported result = OnnxImporter.Import(ModelWithGraph(graph), new OnnxImportOptions());

        result.Stage.Should().Be("bounds");
        result.Failure.Should().Contain("x", "the missing input has to be named");
    }

    /// <summary>**算子受支持但链不是仿射时在 `evaluate` 阶段失败** / A supported but non-affine chain fails at evaluation.</summary>
    [Fact]
    public void ANonAffineChain_ShouldFailAtEvaluate() {
        byte[] node = Concat(StringField(1, "x"), StringField(1, "y"), StringField(2, "out"), StringField(4, "Mul"));
        byte[] graph = Concat(
            BytesField(1, node),
            StringField(2, "g"),
            BytesField(11, ValueInfo("x")),
            BytesField(11, ValueInfo("y")),
            BytesField(12, ValueInfo("out")));

        var options = new OnnxImportOptions(OnnxImportMode.Quadratic, inputBounds: new Dictionary<string, OnnxBounds> {
            ["x"] = new(-1.0, 1.0),
            ["y"] = new(-1.0, 1.0),
        });

        OnnxImporter.Imported result = OnnxImporter.Import(ModelWithGraph(graph), options);

        result.Stage.Should().Be("evaluate", "two variables multiplied is a quadratic, which this chain does not express");
        result.Polynomial.Should().BeNull();
    }

    /// <summary>**一条合法的仿射链走完全程** / A legal affine chain goes all the way through.</summary>
    [Fact]
    public void AValidAffineChain_ShouldImport() {
        byte[] node = Concat(StringField(1, "x"), StringField(1, "y"), StringField(2, "out"), StringField(4, "Add"));
        byte[] graph = Concat(
            BytesField(1, node),
            StringField(2, "g"),
            BytesField(11, ValueInfo("x")),
            BytesField(11, ValueInfo("y")),
            BytesField(12, ValueInfo("out")));

        var options = new OnnxImportOptions(inputBounds: new Dictionary<string, OnnxBounds> {
            ["x"] = new(-1.0, 1.0),
            ["y"] = new(-1.0, 1.0),
        });

        OnnxImporter.Imported result = OnnxImporter.Import(ModelWithGraph(graph), options);

        result.IsOk.Should().BeTrue(result.Failure);
        result.Polynomial!.Monomials.Should().HaveCount(2, "x and y both survive with coefficient one");
        result.Variables.Should().HaveCount(2);
        result.Variables!.Should().ContainKey("x").And.ContainKey("y");
    }

    /// <summary>**选项无效时最先报 `options`** / An invalid option set is reported first.</summary>
    [Fact]
    public void InvalidOptions_ShouldBeReportedFirst() {
        var options = new OnnxImportOptions(inputBounds: new Dictionary<string, OnnxBounds> { ["x"] = new(5.0, 1.0) });

        OnnxImporter.Imported result = OnnxImporter.Import(new byte[] { 0x08, 0x08 }, options);

        result.Stage.Should().Be("options", "no model can be blamed for an option set that cannot express anything");
    }

    // ---- protobuf 编码辅助（按规范，不经过解析器）----

    private static byte[] ModelWithGraph(byte[] graph) => Concat(VarintField(1, 8), BytesField(7, graph));

    private static byte[] ValueInfo(string name) =>
        Concat(StringField(1, name), BytesField(2, BytesField(1, VarintField(1, 1))));

    private static byte[] Tag(int field, int wire) => Varint((ulong)((field << 3) | wire));

    private static byte[] Varint(ulong value) {
        var bytes = new List<byte>();
        do {
            byte current = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) {
                current |= 0x80;
            }
            bytes.Add(current);
        } while (value != 0);
        return bytes.ToArray();
    }

    private static byte[] VarintField(int field, long value) => Concat(Tag(field, 0), Varint((ulong)value));

    private static byte[] StringField(int field, string value) => BytesField(field, Encoding.UTF8.GetBytes(value));

    private static byte[] BytesField(int field, byte[] payload) => Concat(Tag(field, 2), Varint((ulong)payload.Length), payload);

    private static byte[] Concat(params byte[][] parts) {
        var result = new List<byte>();
        foreach (byte[] part in parts) {
            result.AddRange(part);
        }
        return result.ToArray();
    }
}
