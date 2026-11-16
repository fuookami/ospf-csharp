#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Graph;

/// <summary>
/// **ONNX 数据类型的编号必须与规范一致** / The ONNX data type ids must match the specification.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的。**这里钉的不是"我们的枚举有几个成员"，而是那几个编号是否与 ONNX 规范相同。**
/// **编号写错不会抛异常：** 张量会被当成另一种类型读，数值悄悄变形，**而整条链上看不出任何错误。**
///
/// Syncs. **What is pinned is not "how many members our enum has" but whether those ids match the ONNX specification.** **A wrong id throws
/// nothing: the tensor is read as another type and the numbers quietly change, with no error visible anywhere down the chain.**
/// </remarks>
public class OnnxDataTypeTests
{
    /// <summary>**编号与 ONNX 规范逐项相同** / The ids match the specification item by item.</summary>
    /// <remarks>
    /// 规范里 `TensorProto.DataType` 的编号是：1 FLOAT、2 UINT8、3 INT8、4 UINT16、5 INT16、6 INT32、7 INT64、8 STRING、
    /// 9 BOOL、10 FLOAT16、11 DOUBLE、12 UINT32、13 UINT64、16 BFLOAT16。
    ///
    /// The specification's `TensorProto.DataType` ids are as listed above.
    /// </remarks>
    [Fact]
    public void TheProtoIds_ShouldMatchTheSpecification() {
        ((int)OnnxDataType.Float32).Should().Be(1);
        ((int)OnnxDataType.UInt8).Should().Be(2);
        ((int)OnnxDataType.Int8).Should().Be(3);
        ((int)OnnxDataType.UInt16).Should().Be(4);
        ((int)OnnxDataType.Int16).Should().Be(5);
        ((int)OnnxDataType.Int32).Should().Be(6);
        ((int)OnnxDataType.Int64).Should().Be(7);
        ((int)OnnxDataType.String).Should().Be(8);
        ((int)OnnxDataType.Bool).Should().Be(9);
        ((int)OnnxDataType.Float16).Should().Be(10);
        ((int)OnnxDataType.Double).Should().Be(11);
        ((int)OnnxDataType.UInt32).Should().Be(12);
        ((int)OnnxDataType.UInt64).Should().Be(13);
        ((int)OnnxDataType.BFloat16).Should().Be(16);
    }

    /// <summary>**未知编号返回 null，不猜默认值** / An unknown id returns null instead of guessing.</summary>
    /// <remarks>
    /// **猜 `Float32` 会让一张 `Int64` 的索引表被当成浮点读**，而那种错误在数值层面才看得出来，**离出错点已经很远。**
    ///
    /// **Guessing `Float32` would read an `Int64` index table as floating point**, an error visible only at the numeric level, far from where it happened.
    /// </remarks>
    [Fact]
    public void AnUnknownProtoId_ShouldBeNull() {
        OnnxDataTypes.FromProtoId(0).Should().BeNull("0 is not a type");
        OnnxDataTypes.FromProtoId(14).Should().BeNull("14 (UINT4) was never supported by this plugin");
        OnnxDataTypes.FromProtoId(999).Should().BeNull();

        OnnxDataTypes.FromProtoId(1).Should().Be(OnnxDataType.Float32, "a known id round-trips");
        OnnxDataTypes.FromProtoId(16).Should().Be(OnnxDataType.BFloat16, "including the sparse 16, which sits past the contiguous range");
    }

    /// <summary>**分类互斥且覆盖** / The classifications are disjoint and complete.</summary>
    [Fact]
    public void TheClassifications_ShouldBeDisjoint() {
        foreach (OnnxDataType type in System.Enum.GetValues<OnnxDataType>()) {
            bool floating = OnnxDataTypes.IsFloating(type);
            bool integer = OnnxDataTypes.IsInteger(type);
            (floating && integer).Should().BeFalse(type + " cannot be both floating and integer");
            (floating || integer || type is OnnxDataType.String or OnnxDataType.Bool).Should().BeTrue(
                type + " has to fall into some class, otherwise a caller has to special-case it without knowing");
        }

        OnnxDataTypes.IsControl(OnnxDataType.Bool).Should().BeTrue("booleans are control tensors");
        OnnxDataTypes.IsControl(OnnxDataType.Int64).Should().BeTrue("integer indices are control tensors");
        OnnxDataTypes.IsControl(OnnxDataType.Float32).Should().BeFalse("a float is arithmetic, not control");
    }

    /// <summary>**字节宽度与类型一致** / The byte widths match the types.</summary>
    /// <remarks>
    /// **宽度是读原始张量缓冲时唯一的安全边界：** **算小了会截断，算大了会读到下一个元素并把整张量错位——**
    /// **两者都不抛异常。**
    ///
    /// **The width is the only safety boundary when reading a raw tensor buffer: too small truncates, too large reads into the next element and shifts the
    /// whole tensor, and neither throws.**
    /// </remarks>
    [Fact]
    public void TheByteWidths_ShouldMatchTheTypes() {
        OnnxDataTypes.ByteWidth(OnnxDataType.Float32).Should().Be(4);
        OnnxDataTypes.ByteWidth(OnnxDataType.Double).Should().Be(8);
        OnnxDataTypes.ByteWidth(OnnxDataType.Float16).Should().Be(2);
        OnnxDataTypes.ByteWidth(OnnxDataType.BFloat16).Should().Be(2);
        OnnxDataTypes.ByteWidth(OnnxDataType.Int8).Should().Be(1);
        OnnxDataTypes.ByteWidth(OnnxDataType.Bool).Should().Be(1);
        OnnxDataTypes.ByteWidth(OnnxDataType.Int64).Should().Be(8);
        OnnxDataTypes.ByteWidth(OnnxDataType.String).Should().Be(0, "strings are variable-length, so there is no fixed width");
    }
}