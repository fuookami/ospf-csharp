#nullable enable

// IDE0007 与 IDE0008 会在同一个泛型局部变量上互相要求（一个说用 var、一个说用显式类型），
// 而这批代码里两处都触发。按仓库既有做法在文件级关闭这两条，理由记录在此。
//
// IDE0007 and IDE0008 demand opposite things on the same generic local, and both fire in this file. Disabled at file scope per the existing repository practice,
// with the reason recorded here.
#pragma warning disable IDE0007, IDE0008


using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Inference;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Inference;

/// <summary>
/// **推理层：真跑一个模型，而不是“接口有了”** / The inference layer actually runs a model.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxInference`。**判据用运行时真跑一个手工编码的最小模型（`z = x + y`），与手算对照——**
/// **“包装了运行时”和“真的算对了”是两件事，只有后者值得写成判据。**
///
/// Syncs `OnnxInference`. **The criteria actually run a hand-encoded minimal model and compare with hand arithmetic — “the runtime is wrapped” and “it computed the
/// right thing” are two different claims, and only the second is worth a criterion.**
/// </remarks>
public class OnnxInferenceTests
{
    /// <summary>**一个加法模型真的算对了** / An addition model computes the right values.</summary>
    [Fact]
    public void AnAdditionModel_ShouldComputeTheSum() {
        using OnnxInference? inference = OnnxInference.Load(AddModelBytes(), out string? loadFailure);
        loadFailure.Should().BeNull(loadFailure);
        inference.Should().NotBeNull();

        inference!.InputNames.Should().BeEquivalentTo(new[] { "x", "y" });
        inference.OutputNames.Should().BeEquivalentTo(new[] { "z" });

        IReadOnlyDictionary<string, float[]>? outputs = inference.Run(
            new Dictionary<string, float[]> {
                ["x"] = new[] { 1.0f, 2.0f, 3.0f },
                ["y"] = new[] { 10.0f, 20.0f, 30.0f },
            },
            new Dictionary<string, int[]> {
                ["x"] = new[] { 3 },
                ["y"] = new[] { 3 },
            },
            out string? runFailure);

        runFailure.Should().BeNull(runFailure);
        outputs!["z"].Should().Equal(new[] { 11.0f, 22.0f, 33.0f },
            "the model is an elementwise add, so every entry is the sum of the two inputs");
    }

    /// <summary>**未知输入名被拒绝，不静默忽略** / An unknown input name is refused.</summary>
    /// <remarks>
    /// **忽略一个输入会让模型用它自己的默认（通常没有），** **于是结果看着正常、其实缺了一项。**
    ///
    /// **Ignoring an input leaves the model with its own default (usually none), and the result looks normal while missing a term.**
    /// </remarks>
    [Fact]
    public void AnUnknownInput_ShouldBeRefused() {
        using OnnxInference? inference = OnnxInference.Load(AddModelBytes(), out _);
        inference.Should().NotBeNull();

        inference!.Run(
            new Dictionary<string, float[]> {
                ["x"] = new[] { 1.0f },
                ["y"] = new[] { 2.0f },
                ["ghost"] = new[] { 3.0f },
            },
            new Dictionary<string, int[]> {
                ["x"] = new[] { 1 },
                ["y"] = new[] { 1 },
                ["ghost"] = new[] { 1 },
            },
            out string? failure).Should().BeNull();

        failure.Should().Contain("ghost");
    }

    /// <summary>**缺一个模型输入也被拒绝** / A missing model input is refused.</summary>
    [Fact]
    public void AMissingInput_ShouldBeRefused() {
        using OnnxInference? inference = OnnxInference.Load(AddModelBytes(), out _);

        inference!.Run(
            new Dictionary<string, float[]> { ["x"] = new[] { 1.0f } },
            new Dictionary<string, int[]> { ["x"] = new[] { 1 } },
            out string? failure).Should().BeNull();

        failure.Should().Contain("y", "the model needs both inputs, and the one left out has to be named");
    }

    /// <summary>**取值个数与形状不符被拒绝** / A value count that disagrees with the shape is refused.</summary>
    /// <remarks>
    /// **形状与取值不一致时若让运行时去猜，报出来的会是运行时的内部消息，而真实原因在调用方这一侧。**
    ///
    /// **When the shape and the value count disagree and the runtime is left to guess, the message comes from inside the runtime while the cause is on the caller's
    /// side.**
    /// </remarks>
    [Fact]
    public void AMismatchedShape_ShouldBeRefused() {
        using OnnxInference? inference = OnnxInference.Load(AddModelBytes(), out _);

        inference!.Run(
            new Dictionary<string, float[]> {
                ["x"] = new[] { 1.0f, 2.0f },
                ["y"] = new[] { 1.0f, 2.0f },
            },
            new Dictionary<string, int[]> {
                ["x"] = new[] { 3 },
                ["y"] = new[] { 3 },
            },
            out string? failure).Should().BeNull();

        failure.Should().Contain("2").And.Contain("3", "both sides of the mismatch have to appear");
    }

    /// <summary>**坏字节的失败消息带上运行时的原话** / Bad bytes carry the runtime's own words.</summary>
    [Fact]
    public void BadBytes_ShouldCarryTheRuntimeMessage() {
        OnnxInference.Load(new byte[] { 0x08, 0x08, 0x3A, 0x05 }, out string? failure).Should().BeNull();
        failure.Should().NotBeNull("those bytes are not a usable model");
        failure!.Length.Should().BeGreaterThan(10, "a bare 'failed' tells the reader nothing about what went wrong");
    }

    /// <summary>**关闭后再跑被拒绝，而不是崩** / Running after dispose is refused rather than crashing.</summary>
    [Fact]
    public void RunningAfterDispose_ShouldBeRefused() {
        OnnxInference inference = OnnxInference.Load(AddModelBytes(), out _)!;
        inference.Dispose();

        inference.Run(
            new Dictionary<string, float[]> { ["x"] = new[] { 1.0f }, ["y"] = new[] { 1.0f } },
            new Dictionary<string, int[]> { ["x"] = new[] { 1 }, ["y"] = new[] { 1 } },
            out string? failure).Should().BeNull();

        failure.Should().NotBeNull("a disposed session is a caller error, not a crash");
    }

    // ---- 手工编码的最小 ONNX 模型：z = x + y ----

    private static byte[] AddModelBytes() {
        byte[] graph = Concat(
            BytesField(1, Concat(
                StringField(1, "x"),
                StringField(1, "y"),
                StringField(2, "z"),
                StringField(4, "Add"))),
            StringField(2, "g"),
            BytesField(11, ValueInfo("x", 3)),
            BytesField(11, ValueInfo("y", 3)),
            BytesField(12, ValueInfo("z", 3)));

        return Concat(
            VarintField(1, 8),
            BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
            BytesField(7, graph));
    }

    private static byte[] ValueInfo(string name, int length) =>
        Concat(
            StringField(1, name),
            BytesField(2, BytesField(1, Concat(
                VarintField(1, 1),
                BytesField(2, BytesField(1, VarintField(1, length)))))));

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