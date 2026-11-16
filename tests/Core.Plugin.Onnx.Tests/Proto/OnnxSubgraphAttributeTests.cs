#nullable enable

using System;
using System.Text;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Proto;

/// <summary>
/// **子图属性的解析：控制流能不能导入，全看这一步** / Parsing the subgraph attribute: it decides whether control flow can be imported at all.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `AttributeProto.g` 读取。**这一条之前是缺的，而缺得很难发现：** **`If`／`Loop`／`Scan` 的分支体就存在这个字段里，'
/// **不解析它，子图在文件里有、在内存里没有——** **于是前面所有控制流的展开代码都到不了真实模型。**
///
/// **这类缺口最危险的地方在于它的表现：** **不是"报错"，而是"控制流算子被当作不支持"**——**看起来像"还没做"，其实是"读漏了一个字段"。**
///
/// Syncs reading `AttributeProto.g`. **It was missing, and missing in a way that is easy to overlook: the body of If / Loop / Scan lives in this field, and without
/// parsing it the subgraph exists in the file and nowhere in memory — so every control-flow unrolling written earlier could never reach a real model.**
///
/// **What makes such a gap dangerous is how it shows: not an error, but "the control-flow operator is unsupported" — which reads like "not done yet" when it is really
/// "one field was never read".**
/// </remarks>
public class OnnxSubgraphAttributeTests
{
    /// <summary>**子图属性被解析出一张图** / A subgraph attribute parses into a graph.</summary>
    [Fact]
    public void ASubgraphAttribute_ShouldParseIntoAGraph() {
        byte[] attribute = Attribute("then_branch", kind: 5, body: GraphBytes("body", "y"));

        OnnxAttribute parsed = OnnxModelParser.ParseAttribute(attribute);

        parsed.Name.Should().Be("then_branch");
        parsed.Kind.Should().Be(5, "AttributeProto.type 5 is a graph attribute");
        parsed.Graph.Should().NotBeNull("the body has to land in memory, or every control-flow unrolling is unreachable");
        parsed.Graph!.Name.Should().Be("body");
        parsed.Graph.Outputs.Should().HaveCount(1);
        parsed.Graph.Outputs[0].Name.Should().Be("y");
    }

    /// <summary>**子图里的节点也被解析出来** / The body's nodes are parsed as well.</summary>
    [Fact]
    public void TheBodyNodes_ShouldBeParsed() {
        OnnxAttribute parsed = OnnxModelParser.ParseAttribute(
            Attribute("body", kind: 5, body: GraphBytes("body", "out", node: true)));

        parsed.Graph!.Nodes.Should().HaveCount(1);
        parsed.Graph.Nodes[0].OpType.Should().Be("Add");
        parsed.Graph.Nodes[0].Inputs.Should().Equal(new[] { "a", "b" });
        parsed.Graph.Nodes[0].Outputs.Should().Equal(new[] { "out" });
    }

    /// <summary>**不是图属性的没带图** / A non-graph attribute carries no graph.</summary>
    /// <remarks>
    /// **这条钉住"不猜"：** **把一个浮点属性也塞一张空图会让下游以为"这是个控制流节点"，** **而它其实只是某个算子的参数。**
    ///
    /// **This pins "no guessing": hanging an empty graph on a float attribute would tell the downstream "this is a control-flow node" when it is merely an operator's
    /// parameter.**
    /// </remarks>
    [Fact]
    public void ANonGraphAttribute_ShouldCarryNoGraph() {
        OnnxAttribute parsed = OnnxModelParser.ParseAttribute(Concat(
            StringField(1, "alpha"),
            Fixed32Field(2, BitConverter.SingleToInt32Bits(0.5f)),
            VarintField(20, 1)));

        parsed.Name.Should().Be("alpha");
        parsed.Float.Should().Be(0.5f);
        parsed.Graph.Should().BeNull("a float attribute is not a subgraph");
    }

    /// <summary>**嵌套子图（体里还有体）也能解析** / A nested body inside a body parses too.</summary>
    /// <remarks>
    /// **`Loop` 体里嵌 `If` 是真实模型里的常见形状。** **只有一层能解析的实现在嵌套处会静默给出"图存在但没有体"——** **而那是最难查的一类：**
    /// **外层一切正常，内层的控制流悄悄不见。**
    ///
    /// **A Loop body containing an If is a common real shape. An implementation that parses one level silently yields "a graph with no body" at the nesting, which is the
    /// hardest kind to trace: everything outside looks right and the inner control flow quietly vanishes.**
    /// </remarks>
    [Fact]
    public void ANestedBody_ShouldParse() {
        byte[] inner = Attribute("inner", kind: 5,
            body: GraphBytes("inner_body", "z", node: true));
        // 属性属于 NodeProto（字段 5），不是 GraphProto（字段 5 是 initializer）。
        // The attribute belongs to NodeProto (field 5); GraphProto's field 5 is the initializer list.
        byte[] outerBody = Concat(
            BytesField(1, Concat(StringField(1, "n"), StringField(4, "If"), BytesField(5, inner))),
            StringField(2, "outer_body"),
            BytesField(12, ValueInfo("z")));

        OnnxAttribute parsed = OnnxModelParser.ParseAttribute(Attribute("then_branch", kind: 5, body: outerBody));

        parsed.Graph.Should().NotBeNull();
        parsed.Graph!.Nodes.Should().HaveCount(1);
        parsed.Graph.Nodes[0].OpType.Should().Be("If");
        OnnxAttribute? nested = null;
        foreach (OnnxAttribute attribute in parsed.Graph.Nodes[0].Attributes) {
            if (attribute.Name == "inner") {
                nested = attribute;
            }
        }
        nested.Should().NotBeNull("the inner body is an attribute of the inner node");
        nested!.Graph.Should().NotBeNull("two levels deep is where a one-level parser gives up without saying so");
        nested.Graph!.Outputs[0].Name.Should().Be("z");
    }

    // ---- 手工编码的最小 protobuf ----

    private static byte[] Attribute(string name, int kind, byte[] body) => Concat(
        StringField(1, name),
        BytesField(6, body),
        VarintField(20, kind));

    private static byte[] GraphBytes(string name, string output, bool node = false) => Concat(
        node
            ? BytesField(1, Concat(StringField(1, "a"), StringField(1, "b"), StringField(2, output), StringField(4, "Add")))
            : Array.Empty<byte>(),
        StringField(2, name),
        BytesField(12, ValueInfo(output)));

    private static byte[] ValueInfo(string name) =>
        Concat(StringField(1, name), BytesField(2, BytesField(1, Concat(VarintField(1, 1), BytesField(2, BytesField(1, VarintField(1, 1)))))));

    private static byte[] Tag(int field, int wire) => Varint((ulong)((field << 3) | wire));

    private static byte[] Varint(ulong value) {
        var bytes = new System.Collections.Generic.List<byte>();
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

    private static byte[] Fixed32Field(int field, int bits) => Concat(
        Tag(field, 5),
        new[] { (byte)(bits & 0xFF), (byte)((bits >> 8) & 0xFF), (byte)((bits >> 16) & 0xFF), (byte)((bits >> 24) & 0xFF) });

    private static byte[] StringField(int field, string value) => BytesField(field, Encoding.UTF8.GetBytes(value));

    private static byte[] BytesField(int field, byte[] payload) => Concat(Tag(field, 2), Varint((ulong)payload.Length), payload);

    private static byte[] Concat(params byte[][] parts) {
        var result = new System.Collections.Generic.List<byte>();
        foreach (byte[] part in parts) {
            result.AddRange(part);
        }
        return result.ToArray();
    }
}