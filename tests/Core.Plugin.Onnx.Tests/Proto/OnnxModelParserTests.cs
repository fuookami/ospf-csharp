#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Proto;

/// <summary>
/// **字段号必须与 `onnx.proto` 一致** / The field numbers must match `onnx.proto`.
/// </summary>
/// <remarks>
/// **这一组判据的意义全在"独立编码"上：** 下面每个字段都按规范手写编号（`(field &lt;&lt; 3) | wire`），
/// **解析器里若把 `NodeProto.op_type` 写成 3 而不是 4，这里的断言就会失败**——**而真实模型上它不会有任何症状，**
/// 只会表现为"这个节点没有算子类型"。
///
/// **These criteria get their meaning from independent encoding: every field below is numbered by hand per the specification, so a parser that gave
/// `NodeProto.op_type` the number 3 instead of 4 fails here** — **while on a real model it would show no symptom at all, just "this node has no operator type".**
/// </remarks>
public class OnnxModelParserTests
{
    /// <summary>**顶层字段：`ir_version`/`producer_name`/`graph`/`opset_import`** / Top-level fields.</summary>
    [Fact]
    public void TheModelHeader_ShouldBeReadByItsFieldNumbers() {
        byte[] graph = GraphWithSingleInput();
        byte[] model = Concat(
            VarintField(1, 8),                       // ModelProto.ir_version = 8
            StringField(2, "pytorch"),               // ModelProto.producer_name
            BytesField(8, Concat(                    // ModelProto.opset_import
                StringField(1, ""),                   //   OperatorSetIdProto.domain
                VarintField(2, 17))),                 //   OperatorSetIdProto.version = 17
            BytesField(7, graph));                   // ModelProto.graph

        OnnxModel parsed = OnnxModelParser.Parse(model);

        parsed.IrVersion.Should().Be(8);
        parsed.ProducerName.Should().Be("pytorch");
        parsed.Opsets.Should().HaveCount(1);
        parsed.Opsets[0].Version.Should().Be(17, "OperatorSetIdProto.version is field 2; reading field 1 would give the domain");
        parsed.Graph.Should().NotBeNull();
    }

    /// <summary>**`NodeProto` 的 op_type 是字段 4，input/output 是 1/2** / Node fields.</summary>
    [Fact]
    public void TheNode_ShouldBeReadByItsFieldNumbers() {
        byte[] node = Concat(
            StringField(1, "x"),          // NodeProto.input
            StringField(2, "y"),          // NodeProto.output
            StringField(3, "n1"),         // NodeProto.name
            StringField(4, "Relu"));      // NodeProto.op_type

        OnnxNode parsed = OnnxModelParser.ParseNode(node);

        parsed.OpType.Should().Be("Relu", "op_type is field 4; a parser reading field 3 would return the node name");
        parsed.Name.Should().Be("n1");
        parsed.Inputs.Should().Equal("x");
        parsed.Outputs.Should().Equal("y");
    }

    /// <summary>**属性：`name`=1、`f`=2（定长 32 位）、`type`=20** / Attribute fields.</summary>
    /// <remarks>
    /// **`f` 的线类型是 5（定长 32 位），不是 0：** **把它当变长读会把四个字节的浮点当整数解码，得到的数毫无意义却不报错。**
    ///
    /// **`f` uses wire type 5 (fixed 32-bit), not 0: decoding it as a varint reads four float bytes as an integer, producing a meaningless number with no error.**
    /// </remarks>
    [Fact]
    public void TheAttribute_ShouldRespectItsWireTypes() {
        byte[] attribute = Concat(
            StringField(1, "alpha"),                  // AttributeProto.name
            Fixed32Field(2, 0.5f),                    // AttributeProto.f
            VarintField(20, 1));                      // AttributeProto.type = FLOAT

        OnnxAttribute parsed = OnnxModelParser.ParseAttribute(attribute);

        parsed.Name.Should().Be("alpha");
        parsed.Float.Should().Be(0.5f, "f is a fixed 32-bit field; treating it as a varint would produce a meaningless integer");
        parsed.Kind.Should().Be(1, "AttributeProto.type is field 20, not a low number");
    }

    /// <summary>**张量的 `dims`=1、`data_type`=2、`float_data`=4（packed）** / Tensor fields.</summary>
    [Fact]
    public void TheTensor_ShouldBeReadByItsFieldNumbers() {
        byte[] tensor = Concat(
            BytesField(1, PackedVarints(2, 3)),        // TensorProto.dims（packed）
            VarintField(2, 1),                          // TensorProto.data_type = FLOAT
            BytesField(4, PackedFloats(1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f)), // TensorProto.float_data
            StringField(8, "w"));                       // TensorProto.name

        OnnxTensor parsed = OnnxModelParser.ParseTensor(tensor);

        parsed.Dims.Should().Equal(new long[] { 2, 3 }, "dims is field 1 and arrives packed");
        parsed.DataType.Should().Be(1);
        parsed.Name.Should().Be("w", "name is field 8, not 3");
        parsed.FloatData.Should().HaveCount(6);
        parsed.FloatData[5].Should().Be(6.0f);
    }

    /// <summary>**形状：符号维度读成 null，不是 0** / A symbolic dimension reads as null, not zero.</summary>
    /// <remarks>
    /// **`dim_param` 是字段 2、`dim_value` 是字段 1：** **把符号维度读成 0 会让"未知"变成"空张量"，**
    /// **形状推演从此走上完全不同的分支，而两者在类型上都是 `long`，调用方无从分辨。**
    ///
    /// **`dim_param` is field 2 and `dim_value` is field 1: reading a symbolic dimension as zero turns "unknown" into "empty tensor", sending shape inference down a
    /// completely different branch, and both are `long` so the caller cannot tell them apart.**
    /// </remarks>
    [Fact]
    public void ASymbolicDimension_ShouldBeNullNotZero() {
        // 逐层构造，不靠括号计数 / built layer by layer rather than by counting parentheses
        byte[] shape = BytesField(1, SymbolicDimensionOnly());          // TensorShapeProto.dim
        byte[] tensorType = Concat(VarintField(1, 1), BytesField(2, shape)); // elem_type + shape
        byte[] type = BytesField(1, tensorType);                        // TypeProto.tensor_type
        byte[] valueInfo = Concat(StringField(1, "x"), BytesField(2, type));

        OnnxValueInfo parsed = OnnxModelParser.ParseValueInfo(valueInfo);

        parsed.Name.Should().Be("x");
        parsed.ElementType.Should().Be(1);
        parsed.Shape.Should().HaveCount(1);
        parsed.Shape[0].Value.Should().BeNull("a symbolic dimension has no fixed value");
        parsed.Shape[0].Symbol.Should().Be("batch", "and it keeps its name, or the caller loses why it is unknown");

        OnnxDimension both = OnnxModelParser.ParseValueInfo(valueInfo).Shape[0];
        (both.Value, both.Symbol).Should().Be((null, "batch"), "dim_value and dim_param are distinct fields, not one");
    }

    /// <summary>**未知字段跳过不破坏后续字段** / Unknown fields are skipped without disturbing what follows.</summary>
    /// <remarks>
    /// **`ModelProto.metadata_props`（字段 14）本插件不读，但真实模型里几乎总有。** **遇到它就失败会让插件在最普通的模型上整个不可用。**
    ///
    /// **`ModelProto.metadata_props` (field 14) is not read here, yet real models almost always carry it. Failing on it would make the plugin unusable on the most
    /// ordinary model.**
    /// </remarks>
    [Fact]
    public void UnknownModelFields_ShouldNotBreakParsing() {
        byte[] model = Concat(
            BytesField(14, StringField(1, "author")),  // ModelProto.metadata_props（本插件不读）
            VarintField(1, 8),                          // ir_version
            StringField(2, "tf2onnx"));

        OnnxModel parsed = OnnxModelParser.Parse(model);

        parsed.IrVersion.Should().Be(8, "the skipped metadata must not shift the field that follows");
        parsed.ProducerName.Should().Be("tf2onnx");
    }

    /// <summary>**节点属性走通到图这一层** / Attributes survive through the graph layer.</summary>
    [Fact]
    public void ANodeAttribute_ShouldSurviveIntoTheGraph() {
        byte[] attribute = Concat(StringField(1, "alpha"), Fixed32Field(2, 0.25f), VarintField(20, 1));
        byte[] node = Concat(
            StringField(4, "LeakyRelu"),
            BytesField(5, attribute));       // NodeProto.attribute

        OnnxGraph graph = OnnxModelParser.ParseGraph(BytesField(1, node));

        graph.Nodes.Should().HaveCount(1);
        graph.Nodes[0].OpType.Should().Be("LeakyRelu");
        graph.Nodes[0].Attributes.Should().HaveCount(1);
        graph.Nodes[0].Attributes[0].Float.Should().Be(0.25f);
    }

    /// <summary>只含 `dim_param` 的 `Dimension`（不含外层 dim 标签）/ A `Dimension` carrying only `dim_param`, without the outer dim tag.</summary>
    private static byte[] SymbolicDimensionOnly() =>
        StringField(2, "batch"); // TensorShapeProto.Dimension.dim_param

    private static byte[] GraphWithSingleInput() {
        byte[] valueInfo = Concat(StringField(1, "x"), BytesField(2, BytesField(1, BytesField(1, VarintField(1, 1)))));
        return Concat(StringField(2, "g"), BytesField(11, valueInfo)); // GraphProto.name, GraphProto.input
    }

    // ---- protobuf 编码辅助（按规范，不经过解析器）----
    // ---- protobuf encoding helpers (by the specification, not through the parser) ----

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

    private static byte[] Fixed32Field(int field, float value) {
        byte[] payload = BitConverter.GetBytes(value);
        return Concat(Tag(field, 5), payload);
    }

    private static byte[] PackedVarints(params long[] values) {
        var payload = new List<byte>();
        foreach (long value in values) {
            payload.AddRange(Varint((ulong)value));
        }
        return payload.ToArray();
    }

    private static byte[] PackedFloats(params float[] values) {
        var payload = new List<byte>();
        foreach (float value in values) {
            payload.AddRange(BitConverter.GetBytes(value));
        }
        return payload.ToArray();
    }

    private static byte[] Concat(params byte[][] parts) {
        var result = new List<byte>();
        foreach (byte[] part in parts) {
            result.AddRange(part);
        }
        return result.ToArray();
    }
}