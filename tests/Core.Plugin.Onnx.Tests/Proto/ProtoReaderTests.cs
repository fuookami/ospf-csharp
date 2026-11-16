#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Proto;

/// <summary>
/// **线格式读取器：只认规范，不认"看起来对"** / The wire reader follows the specification, not plausibility.
/// </summary>
/// <remarks>
/// 手写读取器替代三万余行生成代码的**唯一理由是可验证**：**判据用手写字节按 protobuf 规范编码，逐字段断言。**
/// **反过来说，如果这里只测"能读出个数"，那么"读对了"与"读错但恰好不炸"无法区分。**
///
/// Hand-writing instead of generating 30k lines is justified only by verifiability: **the criteria encode bytes by hand per the protobuf specification and assert
/// field by field.** **Otherwise "it parsed" cannot be told apart from "it parsed wrongly without blowing up".**
/// </remarks>
public class ProtoReaderTests
{
    private static bool Throws(System.Action action) {
        try {
            action();
            return false;
        }
        catch (System.FormatException) {
            return true;
        }
    }

    /// <summary>**变长整数含多字节与最大值** / Varints include multi-byte and the maximum.</summary>
    [Fact]
    public void Varints_ShouldDecodeAcrossWidths() {
        new ProtoReader(new byte[] { 0x00 }).ReadVarint().Should().Be(0UL);
        new ProtoReader(new byte[] { 0x7F }).ReadVarint().Should().Be(127UL);
        new ProtoReader(new byte[] { 0x80, 0x01 }).ReadVarint().Should().Be(128UL, "128 is the first value that needs two bytes");
        new ProtoReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })
            .ReadVarint().Should().Be(ulong.MaxValue);
    }

    /// <summary>**标签拆出字段号与线类型** / A tag splits into field number and wire type.</summary>
    [Fact]
    public void ATag_ShouldSplitIntoFieldAndWireType() {
        // 字段 2、线类型 0 → (2 << 3) | 0 = 0x10
        var reader = new ProtoReader(new byte[] { 0x10 });
        reader.TryReadTag(out int field, out int wire).Should().BeTrue();
        field.Should().Be(2);
        wire.Should().Be(0);

        // 字段 1、线类型 2 → (1 << 3) | 2 = 0x0A
        var reader2 = new ProtoReader(new byte[] { 0x0A });
        reader2.TryReadTag(out int field2, out int wire2);
        field2.Should().Be(1);
        wire2.Should().Be(2, "length-delimited is wire type 2");
    }

    /// <summary>**`float_data` 是 packed fixed32，按位重组而不是按字节复制** / `float_data` is packed fixed32.</summary>
    /// <remarks>
    /// ONNX 的 `TensorProto.float_data` 规范里是 `repeated float [packed = true]`，即一串定长 32 位。
    /// **按单个定长读会算错偏移，而错位后的浮点数常常仍在合理范围内**——不抛异常，只是数变了。
    ///
    /// In the specification `TensorProto.float_data` is `repeated float [packed = true]`, a run of fixed 32-bit values. **Reading them as individual fixed values
    /// shifts the segment, and the shifted floats usually still look plausible** — nothing throws, the numbers just changed.
    /// </remarks>
    [Fact]
    public void PackedFloats_ShouldRoundTripExactly() {
        float[] source = { 1.5f, -0.25f, 3.0f };
        byte[] payload = new byte[source.Length * 4];
        for (int index = 0; index < source.Length; ++index) {
            BitConverter.GetBytes(source[index]).CopyTo(payload, index * 4);
        }

        byte[] framed = new byte[1 + payload.Length];
        framed[0] = (byte)payload.Length;
        payload.CopyTo(framed, 1);

        var reader = new ProtoReader(framed);
        List<float> values = reader.ReadPackedFloat();

        values.Should().HaveCount(3);
        values.Should().Equal(source, "an exact round-trip is the only proof that the bit reconstruction is right");
    }

    /// <summary>**打包变长整数逐项解出** / Packed varints decode item by item.</summary>
    [Fact]
    public void PackedVarints_ShouldDecodeEachItem() {
        // 1, 128, 300 的变长编码，前面加长度 6
        byte[] framed = { 6, 0x01, 0x80, 0x01, 0xAC, 0x02, 0x00 };

        List<long> values = new ProtoReader(framed).ReadPackedVarint();

        values.Should().Equal(new long[] { 1, 128, 300, 0 }, "each element is decoded independently; a shared cursor would smear them");
    }

    /// <summary>**截断的载荷要报错，不返回半个值** / A truncated payload throws rather than returning half a value.</summary>
    /// <remarks>
    /// **沉默的半个值最危险：** **它会被当成"这个张量只有这么长"，于是形状推演走上另一条分支——而调用方无从知道数据被截断了。**
    ///
    /// **A silent half value is the dangerous one: it reads as "this tensor is that short", so shape inference takes another branch, and the caller has no way
    /// to know the data was cut.**
    /// </remarks>
    [Fact]
    public void TruncatedPayloads_ShouldThrow() {
        Throws(() => new ProtoReader(new byte[] { 0x80 }).ReadVarint())
            .Should().BeTrue("a continuation bit with no following byte is a truncated varint");

        Throws(() => new ProtoReader(new byte[] { 5, 0x01, 0x02 }).ReadBytes())
            .Should().BeTrue("a length of five with two bytes left is truncated");

        Throws(() => {
            var reader = new ProtoReader(new byte[] { 0x0A, 0x01 });
            reader.TryReadTag(out _, out _);
            reader.ReadBytes();
        }).Should().BeTrue();
    }

    /// <summary>**未知字段可跳过，且不影响后续字段** / Unknown fields can be skipped without disturbing the rest.</summary>
    /// <remarks>
    /// **ONNX 会随版本新增字段：** **遇到不认识的字段就报错会让插件在"模型比我们新"时整个失败；** **跳过它们才能让旧插件读新模型——**
    /// **而跳过必须精确，否则后面的字段全部错位。**
    ///
    /// **ONNX adds fields across versions: refusing unknown fields makes the plugin fail outright on any model newer than it, while skipping them lets the old
    /// plugin read a new model — and the skip has to be exact or every later field shifts.**
    /// </remarks>
    [Fact]
    public void UnknownFields_ShouldBeSkippable() {
        // 字段 9 (线类型 2) = "skip me"，随后字段 8 (线类型 2) = "keep"
        byte[] bytes = new byte[] { 0x4A, 7, (byte)'s', (byte)'k', (byte)'i', (byte)'p', (byte)' ', (byte)'m', (byte)'e',
                                 0x42, 4, (byte)'k', (byte)'e', (byte)'e', (byte)'p' };
        var reader = new ProtoReader(bytes);

        reader.TryReadTag(out int first, out int wire1);
        first.Should().Be(9);
        reader.SkipField(wire1);

        reader.TryReadTag(out int second, out int wire2);
        second.Should().Be(8);
        reader.ReadString().Should().Be("keep", "the skipped field must not shift what follows");
    }
}