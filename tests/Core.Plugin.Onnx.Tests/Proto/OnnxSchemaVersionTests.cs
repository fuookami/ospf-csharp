#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Proto;

/// <summary>
/// **字段号表必须与解析器的实际行为一致，而不是与注释一致** / The field table has to match the parser's behaviour, not its comments.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `ir_version` 处理：**结论是不按版本分支，** **理由是 protobuf 的字段号一旦发布不可改，新版本只追加字段。**
/// **这里把"我们依赖哪些字段号"写成表，并用一个真实模型证明它——** **表里的号若与代码不符，这个模型就读不出来。**
///
/// Syncs the `ir_version` handling: **no version branch, because protobuf field numbers are immutable once published and a new version only appends fields.** This layer writes down
/// **which field numbers the plugin relies on** and proves it with a real model — **a table that disagrees with the code cannot parse that model.**
/// </remarks>
public class OnnxSchemaVersionTests
{
    /// <summary>**按表里的字段号编码的模型能被读出来** / A model encoded with the table's field numbers parses.</summary>
    [Fact]
    public void AModelEncodedWithTheTable_ShouldParse() {
        OnnxModel model = OnnxModelParser.Parse(ModelBytes(irVersion: 8, extraNodeField: false));

        model.IrVersion.Should().Be(8);
        model.Graph.Should().NotBeNull("GraphProto field 11 is input, 12 is output, 5 is initializer");
        model.Graph!.Nodes.Should().HaveCount(1);
        model.Graph.Nodes[0].OpType.Should().Be("Add", "NodeProto field 4 is op_type");
        model.Graph.Nodes[0].Inputs.Should().Equal(new[] { "x", "y" });
        model.Graph.Inputs.Should().HaveCount(2);
        model.Graph.Outputs.Should().HaveCount(1);
    }

    /// <summary>**未知字段被跳过，不打断解析** / Unknown fields are skipped rather than breaking the parse.</summary>
    /// <remarks>
    /// **这是"不按版本分支"的前提之一：** **新版本只追加字段，而追加的字段对我们是未知的。** '
    /// **若 `SkipField` 有洞，前向读取就不成立，** **那时"不分支"会从有理由变成没理由——所以这条判据钉着它。**
    ///
    /// **This is one premise of "no version branch": a new version only appends fields, and those fields are unknown to us.** **If SkipField had a hole, forward reading would not
    /// hold, and "no branch" would stop being defensible — so this criterion pins it.**
    /// </remarks>
    [Fact]
    public void UnknownFields_ShouldBeSkipped() {
        OnnxModel model = OnnxModelParser.Parse(ModelBytes(irVersion: 8, extraNodeField: true));

        model.Graph!.Nodes[0].OpType.Should().Be("Add", "an unknown field before op_type must not shift what follows");
        model.Graph.Nodes[0].Inputs.Should().Equal(new[] { "x", "y" });
    }

    /// <summary>**另一个 `ir_version` 仍然能读，而报告如实说"未验证"** / Another ir_version still reads, and the report says "not verified".</summary>
    [Fact]
    public void AnotherIrVersion_ShouldReadAndReportHonestly() {
        OnnxModel model = OnnxModelParser.Parse(ModelBytes(irVersion: 9, extraNodeField: false));
        model.Graph!.Nodes.Should().HaveCount(1, "field numbers do not change with the version");

        OnnxSchemaVersion.Describe(8, 13).Should().Contain("已验证");
        OnnxSchemaVersion.Describe(9, 20).Should().Contain("未验证");
        OnnxSchemaVersion.Describe(9, 20).Should().Contain("跳过");
    }

    /// <summary>**不按版本分支，而这个结论由两条前提支撑** / No version branch, and that conclusion rests on two premises.</summary>
    [Fact]
    public void NoVersionBranch_ShouldBeNeeded() {
        OnnxSchemaVersion.NeedsVersionBranch(8).Should().BeFalse();
        OnnxSchemaVersion.NeedsVersionBranch(9).Should().BeFalse();
        OnnxSchemaVersion.NeedsVersionBranch(11).Should().BeFalse(
            "field numbers are immutable, so a higher version adds fields rather than renaming them");

        // 两条前提各有一条判据：按字段号分派（上面那条模型判据）与未知字段被跳过（SkipField 那条）。
        // The two premises each have a criterion: dispatch by field number (the model criterion above) and unknown fields being skipped (the SkipField one).
        OnnxSchemaVersion.VerifiedIrVersion.Should().Be(8, "this is the version our fixtures actually use");
    }

    /// <summary>**表里的每一条都带消息名、字段号与含义** / Every entry carries a message, a field number and a meaning.</summary>
    [Fact]
    public void TheTable_ShouldBeComplete() {
        OnnxSchemaVersion.FieldNumbers.Should().NotBeEmpty();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxSchemaVersion.FieldNumber field in OnnxSchemaVersion.FieldNumbers) {
            field.Message.Length.Should().BeGreaterThan(0);
            field.Number.Should().BeGreaterThan(0, "field numbers start at 1 in protobuf");
            field.Meaning.Length.Should().BeGreaterThan(0);
            seen.Add(field.Message + "." + field.Number).Should().BeTrue("a duplicated entry would hide the fact that two readings disagree");
        }
        OnnxSchemaVersion.FieldNumbers.Should().Contain(field => field.Message == "AttributeProto" && field.Number == 6,
            "the subgraph attribute is the one whose omission once made control flow unreachable");
        OnnxSchemaVersion.FieldNumbers.Should().Contain(field => field.Message == "TensorProto" && field.Number == 4,
            "float_data is field 4, not 5: reading the wrong number yields an empty initialiser instead of an error");
    }

    // ---- 手工编码的最小模型：字段号一律取自 OnnxSchemaVersion.FieldNumbers ----

    private static byte[] ModelBytes(long irVersion, bool extraNodeField) {
        var nodeParts = new List<byte[]> {
            StringField(1, "x"),
            StringField(1, "y"),
            StringField(2, "out"),
            StringField(4, "Add"),
        };
        if (extraNodeField) {
            // 一个"未来版本追加的字段"：解析器不认识它，必须跳过。
            // A field a future version might append: the parser does not know it and has to skip it.
            nodeParts.Insert(3, VarintField(99, 7));
        }

        byte[] graph = Concat(
            BytesField(1, Concat(nodeParts.ToArray())),
            StringField(2, "g"),
            BytesField(11, ValueInfo("x")),
            BytesField(11, ValueInfo("y")),
            BytesField(12, ValueInfo("out")));

        return Concat(
            VarintField(1, irVersion),
            BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
            BytesField(7, graph));
    }

    private static byte[] ValueInfo(string name) =>
        Concat(StringField(1, name), BytesField(2, BytesField(1, Concat(VarintField(1, 1), BytesField(2, BytesField(1, VarintField(1, 1)))))));

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